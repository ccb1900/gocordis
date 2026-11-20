package webui_test

// Access policy conformance: the transport-level authorization seam. The
// console ships allow-everything (no auth by design, user-manual §13); with
// a policy installed, every side-effectful surface (query / command /
// plugin lifecycle / plugin config) is gated SERVER-side before the hub or
// the lifecycle runs — the frontend's display filter is never the boundary.
import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"net/http"
	"net/http/httptest"
	"net/url"
	"testing"

	"dynamic-runtime/extensions/console/host"
	"dynamic-runtime/extensions/console/hub"
	appui "dynamic-runtime/extensions/console/registry"
	"dynamic-runtime/extensions/console/webui"
)

type recordingLifecycle struct {
	uninstalled, installed, setConfigs []string
}

func (l *recordingLifecycle) Uninstall(_ context.Context, id string) error {
	l.uninstalled = append(l.uninstalled, id)
	return nil
}
func (l *recordingLifecycle) Install(_ context.Context, id string) error {
	l.installed = append(l.installed, id)
	return nil
}
func (l *recordingLifecycle) Removed(_ context.Context) ([]webui.RemovedPlugin, error) {
	return nil, nil
}
func (l *recordingLifecycle) Config(_ context.Context, _ string) (map[string]any, error) {
	return map[string]any{"k": "v"}, nil
}
func (l *recordingLifecycle) SetConfig(_ context.Context, id string, _ map[string]any) error {
	l.setConfigs = append(l.setConfigs, id)
	return nil
}

func newPolicyStack(t *testing.T) (*webui.Server, *recordingLifecycle, *int) {
	t.Helper()
	reg := appui.NewRegistry(func() {})
	h := hub.New()
	if _, err := h.RegisterQuery("demo.query", "t", func(context.Context, url.Values) (any, *hub.Error) {
		return "answered", nil
	}); err != nil {
		t.Fatal(err)
	}
	calls := new(int)
	if _, err := h.RegisterCommand("demo.command", "t", func(context.Context, json.RawMessage) error {
		*calls++
		return nil
	}); err != nil {
		t.Fatal(err)
	}
	s := webui.New(host.NewHost(nil, reg, h), nil)
	lc := &recordingLifecycle{}
	s.SetPluginLifecycle(lc)
	return s, lc, calls
}

func do(s *webui.Server, method, path string, body any) *httptest.ResponseRecorder {
	var r *http.Request
	if body == nil {
		r = httptest.NewRequest(method, path, nil)
	} else {
		data, _ := json.Marshal(body)
		r = httptest.NewRequest(method, path, bytes.NewReader(data))
	}
	rec := httptest.NewRecorder()
	s.ServeHTTP(rec, r)
	return rec
}

func TestAccessPolicyDefaultAllowsEverything(t *testing.T) {
	s, _, calls := newPolicyStack(t)
	if rec := do(s, http.MethodGet, "/api/query/demo.query", nil); rec.Code != http.StatusOK {
		t.Fatalf("query = %d, want 200: %s", rec.Code, rec.Body.String())
	}
	if rec := do(s, http.MethodPost, "/api/command/demo.command", map[string]any{}); rec.Code != http.StatusAccepted {
		t.Fatalf("command = %d, want 202: %s", rec.Code, rec.Body.String())
	}
	if *calls != 1 {
		t.Fatalf("command handler calls = %d, want 1", *calls)
	}
	if rec := do(s, http.MethodPost, "/api/plugins/uninstall", map[string]any{"pluginId": "p"}); rec.Code != http.StatusAccepted {
		t.Fatalf("uninstall = %d, want 202", rec.Code)
	}
}

func TestAccessPolicyDeniesEveryGatedSurface(t *testing.T) {
	s, lc, calls := newPolicyStack(t)
	var seen []webui.AccessRequest
	s.SetAccessPolicy(func(_ context.Context, req webui.AccessRequest) error {
		seen = append(seen, req)
		if req.Kind == webui.AccessCommand && req.Name == "demo.command" {
			return errors.New("not allowed from here")
		}
		if req.Kind == webui.AccessQuery && req.Name == "demo.query" {
			return errors.New("read denied")
		}
		if req.Kind == webui.AccessLifecycle {
			return errors.New("lifecycle denied")
		}
		if req.Kind == webui.AccessConfig {
			return errors.New("config denied")
		}
		return nil
	})

	rec := do(s, http.MethodGet, "/api/query/demo.query", nil)
	if rec.Code != http.StatusForbidden {
		t.Fatalf("denied query = %d, want 403: %s", rec.Code, rec.Body.String())
	}
	var body struct {
		Code    string `json:"code"`
		Message string `json:"message"`
	}
	_ = json.Unmarshal(rec.Body.Bytes(), &body)
	if body.Code != "forbidden" || body.Message != "read denied" {
		t.Fatalf("error shape = %+v, want forbidden/read denied", body)
	}

	rec = do(s, http.MethodPost, "/api/command/demo.command", map[string]any{"x": 1})
	if rec.Code != http.StatusForbidden {
		t.Fatalf("denied command = %d, want 403: %s", rec.Code, rec.Body.String())
	}
	if *calls != 0 {
		t.Fatalf("denied command must never reach the hub handler (calls=%d)", *calls)
	}

	rec = do(s, http.MethodPost, "/api/plugins/uninstall", map[string]any{"pluginId": "p"})
	if rec.Code != http.StatusForbidden || len(lc.uninstalled) != 0 {
		t.Fatalf("uninstall = %d/%v, want 403 and no lifecycle call", rec.Code, lc.uninstalled)
	}
	rec = do(s, http.MethodPost, "/api/plugins/install", map[string]any{"pluginId": "p"})
	if rec.Code != http.StatusForbidden || len(lc.installed) != 0 {
		t.Fatalf("install = %d/%v, want 403 and no lifecycle call", rec.Code, lc.installed)
	}
	rec = do(s, http.MethodGet, "/api/plugins/p/config", nil)
	if rec.Code != http.StatusForbidden {
		t.Fatalf("config read = %d, want 403", rec.Code)
	}
	rec = do(s, http.MethodPost, "/api/plugins/p/config", map[string]any{"config": map[string]any{"a": 1}})
	if rec.Code != http.StatusForbidden || len(lc.setConfigs) != 0 {
		t.Fatalf("config write = %d/%v, want 403 and no lifecycle call", rec.Code, lc.setConfigs)
	}

	// The policy saw every category with the right name and the live request.
	want := map[webui.AccessKind]string{
		webui.AccessQuery: "demo.query", webui.AccessCommand: "demo.command",
		webui.AccessLifecycle: "p", webui.AccessConfig: "p",
	}
	got := map[webui.AccessKind]string{}
	for _, r := range seen {
		got[r.Kind] = r.Name
		if r.Request == nil || r.Request.Method == "" {
			t.Fatal("policy must receive the live *http.Request")
		}
	}
	for k, n := range want {
		if got[k] != n {
			t.Fatalf("policy saw %v=%q, want %q (all: %+v)", k, got[k], n, got)
		}
	}
}

func TestAccessPolicyAllowsUnaffectedNames(t *testing.T) {
	s, _, _ := newPolicyStack(t)
	s.SetAccessPolicy(func(_ context.Context, req webui.AccessRequest) error {
		if req.Kind == webui.AccessLifecycle && req.Name == "forbidden-plugin" {
			return errors.New("denied")
		}
		return nil
	})
	if rec := do(s, http.MethodPost, "/api/plugins/uninstall", map[string]any{"pluginId": "other"}); rec.Code != http.StatusAccepted {
		t.Fatalf("unrelated plugin uninstall = %d, want 202: %s", rec.Code, rec.Body.String())
	}
	if rec := do(s, http.MethodPost, "/api/plugins/uninstall", map[string]any{"pluginId": "forbidden-plugin"}); rec.Code != http.StatusForbidden {
		t.Fatalf("targeted uninstall = %d, want 403", rec.Code)
	}
}
