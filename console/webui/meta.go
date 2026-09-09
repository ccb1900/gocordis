// Meta and fleet endpoints: each console self-describes at /api/meta
// (identity, build version, composition size, served query names), and with
// peers configured the server aggregates the peers' /api/meta into
// /api/fleet — a read-only fleet view built purely on observation-boundary
// data. No remote state is mutated.
package webui

import (
	"encoding/json"
	"fmt"
	"net/http"
	"os"
	"runtime"
	"runtime/debug"
	"sort"
	"time"
)

const (
	fleetTimeout    = 3 * time.Second
	fleetCacheTTL   = 5 * time.Second
	unknownVersion  = "unknown"
	defaultHostname = "localhost"
)

// SetIdentity installs the host's stable identity for /api/meta. An empty
// value falls back to the OS hostname at request time.
func (s *Server) SetIdentity(hostID string) {
	s.mu.Lock()
	s.hostID = hostID
	s.mu.Unlock()
}

// SetFleetPeers installs the peer console base URLs aggregated by /api/fleet.
func (s *Server) SetFleetPeers(peers []string) {
	s.mu.Lock()
	s.fleetPeers = append([]string(nil), peers...)
	s.fleetCacheUntil = time.Time{} // invalidate
	s.mu.Unlock()
}

// buildMeta assembles the self-description served at /api/meta.
func (s *Server) buildMeta() map[string]any {
	hostID := s.hostID
	if hostID == "" {
		if h, err := os.Hostname(); err == nil && h != "" {
			hostID = h
		} else {
			hostID = defaultHostname
		}
	}
	pages, panels := 0, 0
	if s.adapter != nil {
		if pl, ue := s.adapter.ListPages(); ue == nil {
			pages = len(pl.Pages)
		}
		if pl, ue := s.adapter.ListPanels(); ue == nil {
			panels = len(pl.Panels)
		}
	}
	queries, commands := []string{}, []string{}
	if s.adapter != nil {
		queries, commands = s.adapter.Names()
	}
	meta := map[string]any{
		"hostId":        hostID,
		"version":       buildVersion(),
		"goVersion":     runtime.Version(),
		"startedAt":     s.startedAt.UTC().Format(time.RFC3339),
		"uptimeSeconds": int(time.Since(s.startedAt).Seconds()),
		"pages":         pages,
		"panels":        panels,
		"queries":       queries,
		"commands":      commands,
	}
	if s.explorer != nil {
		if rows, ue := s.explorer.ListPlugins(); ue == nil {
			meta["plugins"] = len(rows.Plugins)
			active := 0
			for _, p := range rows.Plugins {
				if p.State == "Active" {
					active++
				}
			}
			meta["pluginsActive"] = active
		}
	}
	return meta
}

// buildVersion derives a short version string from the main module's build
// info (VCS revision embedded by `go build` from a git checkout).
func buildVersion() string {
	bi, ok := debug.ReadBuildInfo()
	if !ok {
		return unknownVersion
	}
	rev, dirty := "", false
	for _, setting := range bi.Settings {
		switch setting.Key {
		case "vcs.revision":
			rev = setting.Value
		case "vcs.modified":
			dirty = setting.Value == "true"
		}
	}
	if rev == "" {
		if bi.Main.Version != "" && bi.Main.Version != "(devel)" {
			return bi.Main.Version
		}
		return unknownVersion
	}
	if len(rev) > 12 {
		rev = rev[:12]
	}
	if dirty {
		rev += "-dirty"
	}
	return rev
}

// serveMeta handles GET /api/meta.
func (s *Server) serveMeta(w http.ResponseWriter, _ *http.Request) {
	writeData(w, http.StatusOK, s.buildMeta())
}

// fleetEntry is one peer (or self) in the aggregated fleet view.
type fleetEntry struct {
	URL       string         `json:"url"`
	Online    bool           `json:"online"`
	Error     string         `json:"error,omitempty"`
	CheckedAt string         `json:"checkedAt"`
	Meta      map[string]any `json:"meta,omitempty"`
}

// serveFleet handles GET /api/fleet: self meta plus each configured peer's
// /api/meta, fetched server-side (browsers must not cross origins) with a
// short cache so parallel operators share one polling round.
func (s *Server) serveFleet(w http.ResponseWriter, _ *http.Request) {
	s.mu.Lock()
	if s.fleetCacheUntil.After(time.Now()) && s.fleetCache != nil {
		cached := s.fleetCache
		s.mu.Unlock()
		writeData(w, http.StatusOK, cached)
		return
	}
	s.mu.Unlock()

	entries := []fleetEntry{{
		URL:       "(self)",
		Online:    true,
		CheckedAt: time.Now().UTC().Format(time.RFC3339),
		Meta:      s.buildMeta(),
	}}
	s.mu.Lock()
	peers := append([]string(nil), s.fleetPeers...)
	s.mu.Unlock()
	sort.Strings(peers)
	for _, peer := range peers {
		entries = append(entries, s.fetchPeer(peer))
	}
	payload := map[string]any{"peers": entries, "checkedAt": time.Now().UTC().Format(time.RFC3339)}

	s.mu.Lock()
	s.fleetCache = payload
	s.fleetCacheUntil = time.Now().Add(fleetCacheTTL)
	s.mu.Unlock()
	writeData(w, http.StatusOK, payload)
}

func (s *Server) fetchPeer(peer string) fleetEntry {
	entry := fleetEntry{URL: peer, CheckedAt: time.Now().UTC().Format(time.RFC3339)}
	client := &http.Client{Timeout: fleetTimeout}
	resp, err := client.Get(peer + "/api/meta")
	if err != nil {
		entry.Error = err.Error()
		return entry
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		entry.Error = fmt.Sprintf("peer replied %d", resp.StatusCode)
		return entry
	}
	// The peer response carries the standard {"data": ...} envelope.
	var envelope struct {
		Data map[string]any `json:"data"`
	}
	if err := json.NewDecoder(resp.Body).Decode(&envelope); err != nil {
		entry.Error = err.Error()
		return entry
	}
	if envelope.Data == nil {
		entry.Error = "peer meta missing data envelope"
		return entry
	}
	entry.Online = true
	entry.Meta = envelope.Data
	return entry
}
