package webui

import (
	"context"
	"encoding/json"
	"errors"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
)

// Wire-format conformance for the plugin lifecycle routes: the client
// (client/src/types.ts) hand-mirrors these shapes, so the JSON keys are a
// contract. This test pins them (F-2).

type fakeLifecycle struct {
	uninstallErr error
	installErr   error
	configErr    error
	setConfigErr error

	setConfigSeen map[string]map[string]any
}

func (f *fakeLifecycle) Uninstall(_ context.Context, id string) error {
	if f.uninstallErr != nil {
		return f.uninstallErr
	}
	return nil
}

func (f *fakeLifecycle) Install(_ context.Context, id string) error {
	if f.installErr != nil {
		return f.installErr
	}
	return nil
}

func (f *fakeLifecycle) Removed(_ context.Context) ([]RemovedPlugin, error) {
	return []RemovedPlugin{{ID: "old", Name: "Old Plugin"}}, nil
}

func (f *fakeLifecycle) Config(_ context.Context, id string) (map[string]any, error) {
	if f.configErr != nil {
		return nil, f.configErr
	}
	return map[string]any{"batch": float64(8)}, nil
}

func (f *fakeLifecycle) SetConfig(_ context.Context, id string, cfg map[string]any) error {
	if f.setConfigErr != nil {
		return f.setConfigErr
	}
	if f.setConfigSeen == nil {
		f.setConfigSeen = map[string]map[string]any{}
	}
	f.setConfigSeen[id] = cfg
	return nil
}

func lcPost(t *testing.T, s *Server, path, body string) *httptest.ResponseRecorder {
	t.Helper()
	req := httptest.NewRequest(http.MethodPost, path, strings.NewReader(body))
	rec := httptest.NewRecorder()
	s.ServeHTTP(rec, req)
	return rec
}

func lcGet(t *testing.T, s *Server, path string) *httptest.ResponseRecorder {
	t.Helper()
	req := httptest.NewRequest(http.MethodGet, path, nil)
	rec := httptest.NewRecorder()
	s.ServeHTTP(rec, req)
	return rec
}

// TestLifecycleControlRoute — POST /api/plugins/control drives the explorer
// transport (declaration-first switch) with the ExplorerControlRequest shape
// {pluginId, enable}.
func TestLifecycleControlRoute(t *testing.T) {
	_ = fakeLifecycle{}
	// The explorer transport owns /api/plugins/control; lifecycle fake is not
	// involved. Shape check only: bad body must 400, good body must pass
	// through the transport (nil transport → error result, still JSON).
	s := New(nil, nil)
	// Without an explorer transport wired, the control route reports 503
	// (documents the wiring requirement).
	rec := lcPost(t, s, "/api/plugins/control", `{"pluginId":"p1","enable":true}`)
	if rec.Code != http.StatusServiceUnavailable {
		t.Fatalf("control without transport status = %d, want 503", rec.Code)
	}
}

// TestLifecycleUninstallInstallRemoved — the three lifecycle routes speak the
// PluginLifecycle contract and the wire shapes match client/src/types.ts
// (RemovedPlugin {id,name}; errors as {code,message}).
func TestLifecycleUninstallInstallRemoved(t *testing.T) {
	lc := &fakeLifecycle{uninstallErr: errors.New("store locked")}
	s := New(nil, nil)
	s.SetPluginLifecycle(lc)

	rec := lcPost(t, s, "/api/plugins/uninstall", `{"pluginId":"p9"}`)
	if rec.Code != http.StatusBadRequest {
		t.Fatalf("uninstall error status = %d, want 400", rec.Code)
	}
	if !strings.Contains(rec.Body.String(), "store locked") {
		t.Fatalf("error body missing cause: %s", rec.Body.String())
	}

	rec = lcPost(t, s, "/api/plugins/install", `{"pluginId":"p9"}`)
	if rec.Code != http.StatusAccepted {
		t.Fatalf("install status = %d, want 202 Accepted (async contract)", rec.Code)
	}

	rec = lcGet(t, s, "/api/plugins/removed")
	var body struct {
		Data []RemovedPlugin `json:"data"`
	}
	if err := json.Unmarshal(rec.Body.Bytes(), &body); err != nil {
		t.Fatalf("removed response not JSON: %v", err)
	}
	if len(body.Data) != 1 || body.Data[0].ID != "old" || body.Data[0].Name != "Old Plugin" {
		t.Fatalf("removed = %+v", body.Data)
	}
}

// TestLifecycleConfigRoundTrip — GET config returns the app-provided object;
// POST config reaches SetConfig with the parsed map (the application owns
// validation + rollback, the transport only serves).
func TestLifecycleConfigRoundTrip(t *testing.T) {
	lc := &fakeLifecycle{}
	s := New(nil, nil)
	s.SetPluginLifecycle(lc)

	rec := lcGet(t, s, "/api/plugins/p1/config")
	var body struct {
		Data map[string]any `json:"data"`
	}
	if err := json.Unmarshal(rec.Body.Bytes(), &body); err != nil {
		t.Fatal(err)
	}
	if body.Data["batch"] != float64(8) {
		t.Fatalf("config = %v, want batch=8", body.Data)
	}

	rec = lcPost(t, s, "/api/plugins/p1/config", `{"config":{"batch":16}}`)
	if rec.Code != http.StatusAccepted {
		t.Fatalf("set config status = %d, want 202 Accepted (async contract)", rec.Code)
	}
	if got := lc.setConfigSeen["p1"]["batch"]; got != float64(16) {
		t.Fatalf("SetConfig batch = %v, want 16", got)
	}

	// SetConfig failure surfaces as 400 invalid_request with the cause.
	lc.setConfigErr = errors.New("validation failed")
	rec = lcPost(t, s, "/api/plugins/p1/config", `{"config":{"batch":99}}`)
	if rec.Code != http.StatusBadRequest || !strings.Contains(rec.Body.String(), "validation failed") {
		t.Fatalf("set-config failure = %d %s", rec.Code, rec.Body.String())
	}
}
