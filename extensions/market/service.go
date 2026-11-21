// Package market implements the Phase-3 backend marketplace: a local,
// descriptive plugin catalog and declaration-level install/upgrade/uninstall
// through extensions/config.Controller.
//
// It is deliberately not a second lifecycle authority (AGENTS.md §1, §7):
// market never creates, mutates or disposes a Fiber and never loads a module.
// It only computes config.ComponentConfig rows and submits them to the Config
// Controller, which is the sole owner of reconciliation. Native entries are
// build-time components and therefore read-only.
//
// A Manifest (and therefore every Catalog entry) is descriptive metadata.
// "trust" is a configured label with NO security guarantee; Source.Checksum is
// an integrity check of the artifact file at install time, not an endorsement.
package market

import (
	"context"
	"crypto/sha256"
	"crypto/subtle"
	"encoding/hex"
	"errors"
	"fmt"
	"io/fs"
	"os"
	"path/filepath"
	"reflect"
	"sort"
	"strings"
	"sync"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/pluginmeta"
)

// Errors. Compare with errors.Is.
var (
	// ErrInvalidCatalog means at least one catalog file was invalid or a plugin
	// ID was declared twice. The error text carries a reason per file; the
	// returned Catalog still contains the valid entries.
	ErrInvalidCatalog = errors.New("market: invalid catalog entries")
	// ErrBuildTimePlugin marks a native (statically linked) entry: it has no
	// declaration-level install/upgrade/uninstall.
	ErrBuildTimePlugin = errors.New("market: native plugin is build-time and read-only")
	// ErrNotFound means the id is not in the catalog.
	ErrNotFound = errors.New("market: plugin not in catalog")
	// ErrNotInstalled means no declaration row exists for the id.
	ErrNotInstalled = errors.New("market: plugin not installed")
	// ErrChecksumMismatch means the artifact's sha256 differs from the manifest.
	ErrChecksumMismatch = errors.New("market: artifact checksum mismatch")
	// ErrChecksumMissing means an installable entry declared no checksum.
	ErrChecksumMissing = errors.New("market: installable entry has no source checksum")
	// ErrInvalidChecksum means the manifest checksum is not 64 hex characters.
	ErrInvalidChecksum = errors.New("market: invalid source checksum")
	// ErrArtifactMissing / ErrArtifactUnreadable cover the artifact file itself.
	ErrArtifactMissing    = errors.New("market: source location is empty")
	ErrArtifactUnreadable = errors.New("market: artifact unreadable")
	// ErrInvalidManifest means an installable entry lacks required identity.
	ErrInvalidManifest = errors.New("market: invalid installable manifest")
	// ErrUnsupportedBackend means the backend has no declaration mapping.
	ErrUnsupportedBackend = errors.New("market: unsupported backend")
	// ErrRuntimeIncompatible means the manifest's compatibility.runtime
	// constraint rejects the host runtime version supplied to the Service.
	ErrRuntimeIncompatible = errors.New("market: plugin incompatible with host runtime version")
	// ErrAPIVersionIncompatible means the manifest's compatibility.api
	// constraint rejects the plugin API version supplied to the Service.
	ErrAPIVersionIncompatible = errors.New("market: plugin incompatible with plugin API version")
)

// Service is the backend marketplace capability exposed to applications.
type Service interface {
	Catalog(ctx context.Context) (Catalog, error)
	Install(ctx context.Context, id string) error
	Upgrade(ctx context.Context, id string) error
	Uninstall(ctx context.Context, id string) error
}

// Entry is one catalog row. Trust is descriptive metadata only; it carries no
// security guarantee. Compatible is the result of pluginmeta.ValidateAgainst
// with the runtime/API versions supplied through Component config.
// CompatibleReason is empty when compatible; otherwise it explains which
// constraint rejected the entry. Reason carries non-compatibility state (e.g.
// "manifest missing" for an installed entry whose catalog file disappeared).
// Both are additive (omitempty) so ordinary entries keep the original DTO
// shape.
type Entry struct {
	ID               string `json:"id"`
	Name             string `json:"name"`
	Version          string `json:"version"`
	Backend          string `json:"backend"`
	ModuleType       string `json:"moduleType"`
	Description      string `json:"description"`
	Source           string `json:"source"`
	Trust            string `json:"trust"`
	Installed        bool   `json:"installed"`
	Compatible       bool   `json:"compatible"`
	CompatibleReason string `json:"compatibleReason,omitempty"`
	Reason           string `json:"reason,omitempty"`
}

// Catalog is the hub query DTO. Sequence is a declaration generation counter,
// incremented once per successful Install/Upgrade/Uninstall.
type Catalog struct {
	Entries  []Entry `json:"entries"`
	Sequence uint64  `json:"sequence"`
	// Issues carries non-fatal catalog warnings (invalid files, duplicate IDs).
	// It is additive and omitted when empty, so existing consumers of
	// {"entries","sequence"} are unaffected.
	Issues []Issue `json:"issues,omitempty"`
}

// Issue describes why one catalog file was rejected.
type Issue struct {
	Path   string `json:"path"`
	Reason string `json:"reason"`
}

// ScanResult is the read-only outcome of a catalog scan.
type ScanResult struct {
	Entries []Entry `json:"entries"`
	Issues  []Issue `json:"issues,omitempty"`
}

// Options configures a Service. Base supplies additional non-market rows
// (e.g. declarations not yet applied). Independently of Base, every reconcile
// preserves the Controller's currently applied declarations that the market
// does not own, so a market mutation can never wipe unrelated app components.
// OwnID identifies the hosting Component's own declaration; market-managed
// IDs (OwnID plus installed rows, plus any ID being removed) are excluded from
// that preservation snapshot.
type Options struct {
	Controller     *config.Controller
	CatalogDirs    []string
	RuntimeVersion string
	APIVersion     string
	Trust          string
	OwnID          string
	Base           func(ctx context.Context) ([]config.ComponentConfig, error)
}

// Loader-compatible config row keys. WASM rows use the logical Module Type as
// ComponentConfig.Type (the type the loader registers); proc rows mirror
// procplugin.DiscoverPlugins ("dir", "backend", "queries", "commands").
const (
	KeySource      = "source"
	KeyPath        = "path"
	KeyVersion     = "version"
	KeyDir         = "dir"
	KeyBackend     = "backend"
	KeyQueries     = "queries"
	KeyCommands    = "commands"
	ProcPluginType = "proc-plugin"
)

type record struct {
	entry    Entry
	manifest pluginmeta.Manifest
	dir      string
}

type service struct {
	opts  Options
	trust string

	opMu     sync.Mutex
	mu       sync.RWMutex
	rows     map[string]config.ComponentConfig
	sequence uint64
}

// NewService validates the version configuration and scans the catalog dirs.
func NewService(opts Options) (Service, error) {
	if opts.Controller == nil {
		return nil, errors.New("market: options.Controller is required")
	}
	if strings.TrimSpace(opts.RuntimeVersion) == "" {
		return nil, errors.New("market: options.RuntimeVersion is required")
	}
	// Probe the version pair once; a bad constraint/version must fail at
	// construction rather than silently marking every entry incompatible.
	probe := pluginmeta.Manifest{ID: "probe", Name: "probe", Version: "0.0.0"}
	if err := pluginmeta.ValidateAgainst(probe, opts.RuntimeVersion, opts.APIVersion); err != nil {
		return nil, fmt.Errorf("market: version configuration: %w", err)
	}
	trust := opts.Trust
	if trust == "" {
		trust = "local"
	}
	// Fail fast on an unreadable configured directory. Entries themselves are
	// re-scanned on every operation so on-disk edits become visible without
	// recreating the service.
	if _, err := scan(opts.CatalogDirs, opts.RuntimeVersion, opts.APIVersion, trust); err != nil {
		return nil, err
	}
	s := &service{opts: opts, trust: trust, rows: make(map[string]config.ComponentConfig)}
	s.restoreRows()
	return s, nil
}

// restoreRows recovers install state for a fresh Service over an already
// reconciled Controller (e.g. process/market-service restart). The Controller's
// stored declarations are the authority for what is installed: a row is
// derived from the ComponentConfig itself (WASM source/path/version or Proc
// dir/backend/version keys), with no dependency on a current catalog entry.
// This keeps orphaned installs recoverable after plugin.toml is deleted, and
// Uninstall can retire them. OwnID and unrelated app components are excluded.
func (s *service) restoreRows() {
	for _, owned := range s.opts.Controller.Owned() {
		if owned.ID == s.opts.OwnID {
			continue
		}
		row, ok := rowFromOwned(owned)
		if !ok {
			continue
		}
		s.rows[owned.ID] = row
	}
}

// rowFromOwned reconstructs the declaration-level row market would have
// written, using only the stored ComponentConfig shape. WASM rows carry a
// module-type (non-proc) Type plus source/path/version keys; Proc rows use the
// proc-plugin Type plus dir/backend/version keys.
func rowFromOwned(owned config.OwnedComponent) (config.ComponentConfig, bool) {
	if owned.Type == ProcPluginType && hasConfigKeys(owned.Config, KeyDir, KeyBackend, KeyVersion) {
		return config.ComponentConfig{ID: owned.ID, Type: owned.Type, Config: owned.Config}, true
	}
	if owned.Type != "" && owned.Type != ProcPluginType && hasConfigKeys(owned.Config, KeySource, KeyPath, KeyVersion) {
		return config.ComponentConfig{ID: owned.ID, Type: owned.Type, Config: owned.Config}, true
	}
	return config.ComponentConfig{}, false
}

func hasConfigKeys(cfg map[string]any, keys ...string) bool {
	for _, k := range keys {
		if _, ok := cfg[k]; !ok {
			return false
		}
	}
	return true
}

// freshRecord re-scans the catalog and resolves one id. Invalid files and
// duplicates are loud (ErrInvalidCatalog) rather than silently skipped.
func (s *service) freshRecord(id string) (record, error) {
	res, err := scan(s.opts.CatalogDirs, s.opts.RuntimeVersion, s.opts.APIVersion, s.trust)
	if err != nil {
		return record{}, err
	}
	if len(res.issues) > 0 {
		reasons := make([]string, 0, len(res.issues))
		for _, issue := range res.issues {
			reasons = append(reasons, fmt.Sprintf("%s: %s", issue.Path, issue.Reason))
		}
		return record{}, fmt.Errorf("%w: %s", ErrInvalidCatalog, strings.Join(reasons, "; "))
	}
	for _, rec := range res.entries {
		if rec.entry.ID == id {
			return rec, nil
		}
	}
	return record{}, fmt.Errorf("%w: %q", ErrNotFound, id)
}

// Scan discovers plugin.toml/plugin.json files under dirs. Invalid files and
// duplicate IDs are returned as Issues with a reason; they are never silently
// dropped. Scan itself never fails on a per-file problem (only on I/O errors
// while walking a configured directory).
func Scan(dirs []string, runtimeVersion, apiVersion, trust string) (ScanResult, error) {
	if trust == "" {
		trust = "local"
	}
	res, err := scan(dirs, runtimeVersion, apiVersion, trust)
	if err != nil {
		return ScanResult{}, err
	}
	out := ScanResult{Entries: make([]Entry, 0, len(res.entries)), Issues: append([]Issue(nil), res.issues...)}
	for _, rec := range res.entries {
		out.Entries = append(out.Entries, rec.entry)
	}
	return out, nil
}

// compatibilityFailure classifies an incompatibility into the two typed
// errors. The runtime constraint is checked first with the API check disabled
// (an empty apiVersion imposes no API restriction), then the API constraint is
// checked. Intrinsic manifest validity was already confirmed during scan, so
// any failure here is a constraint rejection.
func compatibilityFailure(m pluginmeta.Manifest, runtimeVersion, apiVersion string) error {
	if err := pluginmeta.ValidateAgainst(m, runtimeVersion, ""); err != nil {
		return fmt.Errorf("%w for %q: %v", ErrRuntimeIncompatible, m.ID, err)
	}
	if err := pluginmeta.ValidateAgainst(m, runtimeVersion, apiVersion); err != nil {
		return fmt.Errorf("%w for %q: %v", ErrAPIVersionIncompatible, m.ID, err)
	}
	return nil
}

type scanOutcome struct {
	entries []record
	issues  []Issue
}

func scan(dirs []string, runtimeVersion, apiVersion, trust string) (scanOutcome, error) {
	out := scanOutcome{entries: []record{}, issues: []Issue{}}
	seen := make(map[string]string)
	for _, dir := range dirs {
		walkErr := filepath.WalkDir(dir, func(path string, d fs.DirEntry, walkErr error) error {
			if walkErr != nil {
				return walkErr
			}
			if d.IsDir() {
				return nil
			}
			var format string
			switch d.Name() {
			case "plugin.toml":
				format = "toml"
			case "plugin.json":
				format = "json"
			default:
				return nil
			}
			data, err := os.ReadFile(path)
			if err != nil {
				out.issues = append(out.issues, Issue{Path: path, Reason: "read failed: " + err.Error()})
				return nil
			}
			m, err := pluginmeta.Parse(data, format)
			if err != nil {
				out.issues = append(out.issues, Issue{Path: path, Reason: err.Error()})
				return nil
			}
			if err := pluginmeta.Validate(m); err != nil {
				out.issues = append(out.issues, Issue{Path: path, Reason: err.Error()})
				return nil
			}
			if prev, dup := seen[m.ID]; dup {
				out.issues = append(out.issues, Issue{Path: path, Reason: fmt.Sprintf("duplicate id %q (already declared by %s)", m.ID, prev)})
				return nil
			}
			seen[m.ID] = path
			source := m.Source.Location
			if source == "" {
				source = m.Source.Kind
			}
			reason := ""
			if err := compatibilityFailure(m, runtimeVersion, apiVersion); err != nil {
				reason = err.Error()
			}
			out.entries = append(out.entries, record{
				entry: Entry{
					ID: m.ID, Name: m.Name, Version: m.Version, Backend: string(m.Backend),
					ModuleType: m.ModuleType, Description: m.Description, Source: source, Trust: trust,
					Compatible: reason == "", CompatibleReason: reason,
				},
				manifest: m,
				dir:      filepath.Dir(path),
			})
			return nil
		})
		if walkErr != nil {
			return scanOutcome{}, fmt.Errorf("market: scan %q: %w", dir, walkErr)
		}
	}
	sort.Slice(out.entries, func(i, j int) bool { return out.entries[i].entry.ID < out.entries[j].entry.ID })
	return out, nil
}

func (s *service) Catalog(ctx context.Context) (Catalog, error) {
	if ctx == nil {
		ctx = context.Background()
	}
	res, err := Scan(s.opts.CatalogDirs, s.opts.RuntimeVersion, s.opts.APIVersion, s.trust)
	if err != nil {
		return Catalog{}, err
	}
	cat := Catalog{Entries: res.Entries, Issues: append([]Issue(nil), res.Issues...)}
	s.mu.RLock()
	cat.Sequence = s.sequence
	seen := make(map[string]struct{}, len(cat.Entries))
	for i := range cat.Entries {
		seen[cat.Entries[i].ID] = struct{}{}
		_, cat.Entries[i].Installed = s.rows[cat.Entries[i].ID]
	}
	// Recovered declarations with no current catalog file still belong in the
	// Catalog, so an orphan can be inspected and uninstalled after restart.
	for id, row := range s.rows {
		if _, ok := seen[id]; ok {
			continue
		}
		cat.Entries = append(cat.Entries, orphanEntry(id, row, s.trust))
	}
	s.mu.RUnlock()
	sort.Slice(cat.Entries, func(i, j int) bool { return cat.Entries[i].ID < cat.Entries[j].ID })
	if len(res.Issues) > 0 {
		reasons := make([]string, 0, len(res.Issues))
		for _, issue := range res.Issues {
			reasons = append(reasons, fmt.Sprintf("%s: %s", issue.Path, issue.Reason))
		}
		return cat, fmt.Errorf("%w: %s", ErrInvalidCatalog, strings.Join(reasons, "; "))
	}
	return cat, ctx.Err()
}

// orphanEntry projects a recovered declaration that has no current catalog
// file into a Catalog row. Without a manifest, compatibility cannot be
// re-evaluated, so the entry is marked compatible (no known constraint) and
// carries a Reason explaining that its manifest is missing.
func orphanEntry(id string, row config.ComponentConfig, trust string) Entry {
	e := Entry{ID: id, Trust: trust, Installed: true, Compatible: true, Reason: "manifest missing"}
	e.Version, _ = row.Config[KeyVersion].(string)
	if row.Type == ProcPluginType {
		e.Backend = string(pluginmeta.BackendProc)
		e.Source, _ = row.Config[KeyBackend].(string)
		return e
	}
	e.Backend = string(pluginmeta.BackendWASM)
	e.ModuleType = row.Type
	e.Source, _ = row.Config[KeySource].(string)
	return e
}

func (s *service) Install(ctx context.Context, id string) error {
	s.opMu.Lock()
	defer s.opMu.Unlock()
	rec, err := s.freshRecord(id)
	if err != nil {
		return err
	}
	if rec.manifest.Backend == pluginmeta.BackendNative {
		return fmt.Errorf("%w: %q", ErrBuildTimePlugin, id)
	}
	if err := compatibilityFailure(rec.manifest, s.opts.RuntimeVersion, s.opts.APIVersion); err != nil {
		return err
	}
	row, err := s.rowFor(rec)
	if err != nil {
		return err
	}
	s.mu.RLock()
	prev, had := s.rows[id]
	unchanged := had && reflect.DeepEqual(prev, row)
	s.mu.RUnlock()
	if unchanged {
		return nil
	}
	s.mu.Lock()
	s.rows[id] = row
	s.mu.Unlock()
	if err := s.reconcile(ctx); err != nil {
		s.mu.Lock()
		if had {
			s.rows[id] = prev
		} else {
			delete(s.rows, id)
		}
		s.mu.Unlock()
		return err
	}
	return nil
}

func (s *service) Upgrade(ctx context.Context, id string) error {
	s.opMu.Lock()
	defer s.opMu.Unlock()
	rec, err := s.freshRecord(id)
	if err != nil {
		return err
	}
	if rec.manifest.Backend == pluginmeta.BackendNative {
		return fmt.Errorf("%w: %q", ErrBuildTimePlugin, id)
	}
	s.mu.RLock()
	prev, installed := s.rows[id]
	s.mu.RUnlock()
	if !installed {
		return fmt.Errorf("%w: %q", ErrNotInstalled, id)
	}
	// A plugin that became incompatible after installation must not be
	// upgraded: reject before touching any declaration or fiber.
	if err := compatibilityFailure(rec.manifest, s.opts.RuntimeVersion, s.opts.APIVersion); err != nil {
		return err
	}
	// Re-read the artifact/checksum from disk: an upgrade may have changed
	// either. The new row is a declaration replacement (Config differs), never
	// a Fiber identity trick.
	row, err := s.rowFor(rec)
	if err != nil {
		return err
	}
	if reflect.DeepEqual(prev, row) {
		return nil
	}
	s.mu.Lock()
	s.rows[id] = row
	s.mu.Unlock()
	if err := s.reconcile(ctx); err != nil {
		s.mu.Lock()
		s.rows[id] = prev
		s.mu.Unlock()
		return err
	}
	return nil
}

func (s *service) Uninstall(ctx context.Context, id string) error {
	s.opMu.Lock()
	defer s.opMu.Unlock()
	// Retire from the stored declaration only. A manifest deleted or changed
	// to native after install must not strand a declaration/fiber (review 2);
	// native entries are never installed, so they cannot reach this path.
	s.mu.RLock()
	prev, installed := s.rows[id]
	s.mu.RUnlock()
	if !installed {
		return s.notInstalledError(id)
	}
	s.mu.Lock()
	delete(s.rows, id)
	s.mu.Unlock()
	if err := s.reconcile(ctx, id); err != nil {
		s.mu.Lock()
		s.rows[id] = prev
		s.mu.Unlock()
		return err
	}
	return nil
}

// reconcile submits a COMPLETE desired set. It starts from the Controller's
// applied declarations (minus IDs the market owns), overlays Base, then the
// market rows. This read-modify-write of the desired set prevents a market
// mutation from silently uninstalling unrelated app components (review 1).
// removing contains IDs about to be retired by this operation.
// notInstalledError classifies a no-op uninstall for the caller. The catalog
// is consulted ONLY to explain why there is nothing to retire; retirement
// itself never depends on the manifest being present or installable.
func (s *service) notInstalledError(id string) error {
	res, err := scan(s.opts.CatalogDirs, s.opts.RuntimeVersion, s.opts.APIVersion, s.trust)
	if err == nil {
		for _, rec := range res.entries {
			if rec.entry.ID != id {
				continue
			}
			if rec.manifest.Backend == pluginmeta.BackendNative {
				return fmt.Errorf("%w: %q", ErrBuildTimePlugin, id)
			}
			break
		}
	}
	return fmt.Errorf("%w: %q", ErrNotInstalled, id)
}

func (s *service) reconcile(ctx context.Context, removing ...string) error {
	s.mu.RLock()
	snapshot := make(map[string]config.ComponentConfig, len(s.rows))
	managed := make(map[string]struct{}, len(s.rows)+len(removing)+1)
	for id, row := range s.rows {
		snapshot[id] = row
		managed[id] = struct{}{}
	}
	s.mu.RUnlock()
	for _, id := range removing {
		managed[id] = struct{}{}
	}
	if s.opts.OwnID != "" {
		managed[s.opts.OwnID] = struct{}{}
	}

	// Precedence: applied unrelated < Base < market rows. Using a map also
	// removes duplicate IDs before validation.
	desired := make(map[string]config.ComponentConfig)
	for _, owned := range s.opts.Controller.Owned() {
		if _, isManaged := managed[owned.ID]; isManaged {
			continue
		}
		enabled := owned.Fiber != nil // disabled entries own no fiber
		desired[owned.ID] = config.ComponentConfig{
			ID: owned.ID, Type: owned.Type, Config: owned.Config, Enabled: &enabled,
		}
	}
	if s.opts.Base != nil {
		base, err := s.opts.Base(ctx)
		if err != nil {
			return err
		}
		for _, cc := range base {
			desired[cc.ID] = cc
		}
	}
	for id, row := range snapshot {
		desired[id] = row
	}

	ids := make([]string, 0, len(desired))
	for id := range desired {
		ids = append(ids, id)
	}
	sort.Strings(ids) // keep desired order deterministic
	comps := make([]config.ComponentConfig, 0, len(ids))
	for _, id := range ids {
		comps = append(comps, desired[id])
	}
	if err := s.opts.Controller.Reconcile(ctx, config.Config{Components: comps}); err != nil {
		return err
	}
	s.mu.Lock()
	s.sequence++
	s.mu.Unlock()
	return nil
}

func (s *service) rowFor(rec record) (config.ComponentConfig, error) {
	path, err := artifactPath(rec)
	if err != nil {
		return config.ComponentConfig{}, err
	}
	if err := verifyChecksum(path, rec.manifest.Source.Checksum); err != nil {
		return config.ComponentConfig{}, err
	}
	m := rec.manifest
	switch m.Backend {
	case pluginmeta.BackendWASM:
		if strings.TrimSpace(m.ModuleType) == "" {
			return config.ComponentConfig{}, fmt.Errorf("%w: %q has no module_type", ErrInvalidManifest, m.ID)
		}
		return config.ComponentConfig{
			ID: m.ID, Type: m.ModuleType,
			Config: map[string]any{
				KeySource:  "file://" + path,
				KeyPath:    path,
				KeyVersion: m.Version,
			},
		}, nil
	case pluginmeta.BackendProc:
		queries, commands := provided(m, "query:"), provided(m, "command:")
		return config.ComponentConfig{
			ID: m.ID, Type: ProcPluginType,
			Config: map[string]any{
				KeyDir:      rec.dir,
				KeyBackend:  m.Source.Location,
				KeyQueries:  queries,
				KeyCommands: commands,
				KeyVersion:  m.Version,
			},
		}, nil
	case pluginmeta.BackendNative:
		return config.ComponentConfig{}, fmt.Errorf("%w: %q", ErrBuildTimePlugin, m.ID)
	default:
		return config.ComponentConfig{}, fmt.Errorf("%w: %q", ErrUnsupportedBackend, m.Backend)
	}
}

func provided(m pluginmeta.Manifest, prefix string) []any {
	out := []any{}
	for _, d := range m.Provides {
		if strings.HasPrefix(d.Key, prefix) {
			out = append(out, d.Key)
		}
	}
	return out
}

func artifactPath(rec record) (string, error) {
	loc := strings.TrimSpace(rec.manifest.Source.Location)
	if loc == "" {
		return "", fmt.Errorf("%w: %q", ErrArtifactMissing, rec.entry.ID)
	}
	if !filepath.IsAbs(loc) {
		loc = filepath.Join(rec.dir, filepath.FromSlash(loc))
	}
	abs, err := filepath.Abs(loc)
	if err != nil {
		return "", fmt.Errorf("%w: %q: %v", ErrArtifactUnreadable, loc, err)
	}
	return abs, nil
}

func verifyChecksum(path, checksum string) error {
	if strings.TrimSpace(checksum) == "" {
		return fmt.Errorf("%w: %q", ErrChecksumMissing, path)
	}
	want, err := hex.DecodeString(checksum)
	if err != nil || len(want) != sha256.Size {
		return fmt.Errorf("%w: %q", ErrInvalidChecksum, checksum)
	}
	data, err := os.ReadFile(path)
	if err != nil {
		return fmt.Errorf("%w: %q: %v", ErrArtifactUnreadable, path, err)
	}
	sum := sha256.Sum256(data)
	if subtle.ConstantTimeCompare(sum[:], want) != 1 {
		return fmt.Errorf("%w: %q", ErrChecksumMismatch, path)
	}
	return nil
}
