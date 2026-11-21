package explorer

import (
	"testing"

	"dynamic-runtime/extensions/config"
)

func TestSetDesiredProtectsPlatformTypes(t *testing.T) {
	s := New(nil)
	s.SetDesired(config.Config{Components: []config.ComponentConfig{
		{ID: "host", Type: "console.host"},
		{ID: "plugins", Type: "console.explorer"},
		{ID: "runtime", Type: "console.runtime"},
		{ID: "insight", Type: "console.insight"},
		{ID: "devtools", Type: "devtools"},
		{ID: "demo", Type: "demo.settings"},
	}})
	for _, id := range []string{"host", "plugins", "runtime", "insight", "devtools"} {
		s.mu.Lock()
		reason := s.protected[id]
		s.mu.Unlock()
		if reason == "" {
			t.Fatalf("platform component %q is not protected", id)
		}
	}
	s.mu.Lock()
	reason := s.protected["demo"]
	s.mu.Unlock()
	if reason != "" {
		t.Fatalf("ordinary component protected: %q", reason)
	}
}
