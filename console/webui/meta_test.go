package webui

import (
	"context"
	"encoding/json"
	"io/fs"
	"net/http"
	"net/http/httptest"
	"net/url"
	"sync/atomic"
	"testing"
	"testing/fstest"

	host "dynamic-runtime/console/host"
	"dynamic-runtime/console/hub"
	"dynamic-runtime/console/registry"
	"dynamic-runtime/extensions/config"
)

// buildTestConsole wires a console host adapter directly over a registry and
// hub — no Runtime activation involved (the full component loop is covered by
// the application e2e).
type testConsole struct {
	hostID     string
	adapter    *host.Host
	hub        *hub.Registry
	registry   registry.Registry
	commandHit atomic.Int64
	server     *Server
}

func newTestConsole(t *testing.T, cc config.ComponentConfig) *testConsole {
	t.Helper()
	c, err := host.NewConsole(cc)
	if err != nil {
		t.Fatal(err)
	}
	if c.HostID() != cc.Config["host_id"] && cc.Config["host_id"] != nil {
		t.Fatalf("hostID parse mismatch: %q", c.HostID())
	}
	if len(c.FleetPeers()) == 0 && cc.Config["fleet_peers"] != nil {
		t.Fatalf("fleet peers parse mismatch: %v", c.FleetPeers())
	}
	reg := registry.NewRegistry(nil)
	h := hub.New()
	assets := fstest.MapFS{"index.html": &fstest.MapFile{Data: []byte("<html>ok</html>")}}
	adapter := host.NewHost(context.Background(), reg, h)
	return &testConsole{
		hostID:   c.HostID(),
		adapter:  adapter,
		hub:      h,
		registry: reg,
		server:   New(adapter, fs.FS(assets)),
	}
}

func TestConsoleParsesIdentityAndFleetConfig(t *testing.T) {
	c, err := host.NewConsole(config.ComponentConfig{
		ID: "ui", Type: "ui",
		Config: map[string]any{"host_id": "gauge-l1", "fleet_peers": []any{"http://peer-a:8080"}},
	})
	if err != nil {
		t.Fatal(err)
	}
	if c.HostID() != "gauge-l1" {
		t.Fatalf("hostID = %q", c.HostID())
	}
	peers := c.FleetPeers()
	if len(peers) != 1 || peers[0] != "http://peer-a:8080" {
		t.Fatalf("fleetPeers = %#v", peers)
	}
	// Defaults: empty config falls back to hostname later; peers stay empty.
	empty, err := host.NewConsole(config.ComponentConfig{ID: "ui", Type: "ui"})
	if err != nil {
		t.Fatal(err)
	}
	if empty.HostID() != "" || len(empty.FleetPeers()) != 0 {
		t.Fatalf("defaults: hostID=%q peers=%#v", empty.HostID(), empty.FleetPeers())
	}
}

func TestMetaEndpointShape(t *testing.T) {
	c := newTestConsole(t, config.ComponentConfig{ID: "ui", Type: "ui"})
	un1, err := c.hub.RegisterQuery("rows", "test", func(ctx context.Context, params url.Values) (any, *hub.Error) {
		return map[string]any{"ok": true}, nil
	})
	if err != nil {
		t.Fatal(err)
	}
	defer un1()
	un2, err := c.hub.RegisterCommand("reload", "test", func(ctx context.Context, body json.RawMessage) error { return nil })
	if err != nil {
		t.Fatal(err)
	}
	defer un2()
	pageOwner := registry.ContributionOwner{PluginID: "test", ComponentID: "c1", ActivationID: "a1"}
	if _, err := c.registry.RegisterPage(pageOwner, registry.PageDefinition{ID: "p1", Title: "P", Route: "/p", Renderer: "r"}); err != nil {
		t.Fatal(err)
	}

	srv := httptest.NewServer(c.server)
	defer srv.Close()

	envelope := getJSON(t, srv.URL+"/api/meta")
	body, _ := envelope["data"].(map[string]any)
	for _, key := range []string{"hostId", "version", "goVersion", "startedAt", "uptimeSeconds", "pages", "panels", "queries", "commands"} {
		if _, ok := body[key]; !ok {
			t.Fatalf("meta missing %q: %#v", key, body)
		}
	}
	if body["pages"] != float64(1) {
		t.Fatalf("meta pages = %#v, want 1", body["pages"])
	}
	queries, _ := body["queries"].([]any)
	if len(queries) != 1 || queries[0] != "rows" {
		t.Fatalf("meta queries = %#v", body["queries"])
	}
}

func TestQueryAndCommandPassthrough(t *testing.T) {
	c := newTestConsole(t, config.ComponentConfig{ID: "ui", Type: "ui"})
	un1, err := c.hub.RegisterQuery("rows", "test", func(ctx context.Context, params url.Values) (any, *hub.Error) {
		return map[string]any{"rows": []int{1, 2}, "got": params.Get("date")}, nil
	})
	if err != nil {
		t.Fatal(err)
	}
	defer un1()
	un2, err := c.hub.RegisterCommand("reload", "test", func(ctx context.Context, body json.RawMessage) error {
		c.commandHit.Add(1)
		return nil
	})
	if err != nil {
		t.Fatal(err)
	}
	defer un2()

	srv := httptest.NewServer(c.server)
	defer srv.Close()

	envelope := getJSON(t, srv.URL+"/api/query/rows?date=2026-09-08")
	body, _ := envelope["data"].(map[string]any)
	rows, _ := body["rows"].([]any)
	if len(rows) != 2 || body["got"] != "2026-09-08" {
		t.Fatalf("query response = %#v", body)
	}

	status, _ := postJSON(t, srv.URL+"/api/command/reload", map[string]string{"x": "1"})
	if status != http.StatusAccepted {
		t.Fatalf("command status = %d", status)
	}
	if c.commandHit.Load() != 1 {
		t.Fatal("command handler not invoked")
	}

	// Unknown names surface the not_found contract.
	status, body = getPair(t, srv.URL+"/api/query/nope")
	if status != http.StatusNotFound || body["code"] != "not_found" {
		t.Fatalf("unknown query = %d %#v", status, body)
	}
}

func TestFleetAggregatesSelfAndPeers(t *testing.T) {
	// Peer console: its own meta endpoint.
	peerConsole := newTestConsole(t, config.ComponentConfig{
		ID: "ui", Type: "ui", Config: map[string]any{"host_id": "peer-a"},
	})
	peerConsole.server.SetIdentity("peer-a")
	peer := httptest.NewServer(peerConsole.server)
	defer peer.Close()

	self := newTestConsole(t, config.ComponentConfig{
		ID: "ui", Type: "ui", Config: map[string]any{"host_id": "self-host"},
	})
	// The cmd layer wires identity + peers from the component onto the server.
	self.server.SetIdentity("self-host")
	self.server.SetFleetPeers([]string{peer.URL, "http://127.0.0.1:1"})
	srv := httptest.NewServer(self.server)
	defer srv.Close()

	envelope := getJSON(t, srv.URL+"/api/fleet")
	body, _ := envelope["data"].(map[string]any)
	peers, _ := body["peers"].([]any)
	if len(peers) != 3 { // self + online peer + unreachable peer
		t.Fatalf("fleet peers = %#v", peers)
	}
	t.Logf("fleet body = %#v", body)
	online := map[string]bool{}
	for _, raw := range peers {
		p, _ := raw.(map[string]any)
		id := ""
		if meta, ok := p["meta"].(map[string]any); ok {
			id, _ = meta["hostId"].(string)
		}
		online[id] = p["online"] == true
	}
	if !online["self-host"] || !online["peer-a"] {
		t.Fatalf("self and reachable peer must be online: %#v", online)
	}
	if online[""] {
		t.Fatal("unreachable peer must not be online")
	}
}
