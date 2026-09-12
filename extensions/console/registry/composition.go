// Package ui holds the Application-level UI Composition contract. UI Host
// plugins and independent Application Plugins use these types; the GOCORDIS
// Runtime and Application business plugins never need to know a concrete UI
// page or panel.
package registry

import (
	"encoding/json"
	"errors"
	"sort"
	"sync"
)

// Position is a panel placement hint. P3 requires main/right/bottom; top and
// left remain accepted values for existing/legacy renderers.
type Position string

const (
	PositionMain   Position = "main"
	PositionRight  Position = "right"
	PositionBottom Position = "bottom"
	PositionTop    Position = "top"
	PositionLeft   Position = "left"
)

// PageDefinition is a stable UI Page Contribution contract. Renderer is a
// declarative identity mapped by the UI Host; it is never executable code.
type PageDefinition struct {
	ID       string
	Title    string
	Route    string
	Renderer string
	// Icon is an optional menu icon hint from the console's curated icon
	// set (e.g. "dashboard", "search"). The shell maps the name; unknown
	// or empty values get the default icon.
	Icon string
	// Description is optional page-level helper text rendered by the shell.
	Description string
	// Views is an optional declarative view schema: an ordered list of view
	// blocks (kind + per-kind fields) the client's generic renderers
	// interpret. Opaque to the console: the host transports it, the client
	// renderer interprets it. Extensible — new kinds are new renderer
	// registrations, no console change.
	Views json.RawMessage `json:"views,omitempty"`
	// View is the single-view form of Views (one block). Prefer Views.
	View json.RawMessage `json:"view,omitempty"`
	// Actions are optional page-level hub commands (e.g. a trigger button),
	// declared as [{label, command, datePicker}] JSON.
	Actions json.RawMessage `json:"actions,omitempty"`
	// Order is Contribution metadata controlling stable Composition order.
	// Entries with the same Order keep registration sequence for
	// deterministic Registry tests and plugin-authored contributions.
	Order int
}

// PanelDefinition is a stable UI Panel Contribution contract.
type PanelDefinition struct {
	ID       string
	Title    string
	Position Position
	Renderer string
	Order    int
	// Views is the optional declarative view schema for this panel: an
	// ordered list of view blocks the client's generic renderers interpret.
	Views json.RawMessage `json:"views,omitempty"`
	// Pages lists the page IDs this panel appears on. Empty means every
	// page. The binding is composition data — the host and transports only
	// carry it; clients decide rendering per active page.
	Pages []string
}

// ClientModule is one plugin frontend module contribution: an ES module the
// console serves same-origin and loads at boot. Frontend plugins are
// composition-governed like every contribution — declared as ui-client
// components, they appear in the desired configuration, honor the enabled
// switch, and vanish on uninstall. A module existing on disk is never
// loaded by itself.
type ClientModule struct {
	// Name is the URL slug under /client-modules/ (no separators).
	Name string
	// Path is the plugin directory's entry file (convention:
	// plugins/<name>/ui.js), resolved at serve time.
	Path string
}

// CompositionSnapshot is one atomic, read-only view of the current Page and
// Panel contributions. It never carries Owner/Activation identity; callers may
// mutate the returned slices without affecting Registry state.
type CompositionSnapshot struct {
	Pages  []PageDefinition
	Panels []PanelDefinition
}

// ContributionOwner records which plugin activation owns a contribution.
// ComponentID reuses the Config Component ID (a stable GOCORDIS Config
// Controller identity). ActivationID is an opaque activation generation label
// allocated by the contributor on every Apply because the public runtime API
// exposes no numeric ActivationID; cleanup is still owned and executed by the
// Runtime Effect. PluginID is the Config Type, retained as descriptive
// identity only.
type ContributionOwner struct {
	PluginID     string
	ComponentID  string
	ActivationID string
}

var (
	ErrDuplicatePage        = errors.New("duplicate UI page")
	ErrDuplicatePanel       = errors.New("duplicate UI panel")
	ErrMissingPage          = errors.New("UI page not found")
	ErrMissingPanel         = errors.New("UI panel not found")
	ErrContributionOwner    = errors.New("UI contribution owner is empty")
	ErrEmptyPageDefinition  = errors.New("UI page id is empty")
	ErrEmptyPanelDefinition = errors.New("UI panel id is empty")

	ErrDuplicateClientModule = errors.New("duplicate client module")
	ErrEmptyClientModule     = errors.New("client module name is empty")
)

// Registry is the Application/UI Host composition registry. A UI Host owns
// exactly one Registry per activation; it is never a GOCORDIS Kernel Registry.
type Registry interface {
	RegisterPage(owner ContributionOwner, def PageDefinition) (func() error, error)
	RegisterPanel(owner ContributionOwner, def PanelDefinition) (func() error, error)
	RegisterClientModule(owner ContributionOwner, def ClientModule) (func() error, error)
	ListPages() []PageDefinition
	ListPanels() []PanelDefinition
	ListClientModules() []ClientModule
	// Snapshot returns one atomic, isolated Composition view. React and
	// transport layers consume this Snapshot/DTO boundary; they never receive
	// the Registry or its Owners.
	Snapshot() CompositionSnapshot
	// Contributions is the Application/UI-Host ownership view. It is never
	// exposed to React: transport DTOs only consume ListPages/ListPanels.
	Contributions() []Contribution
}

// ContributionKind distinguishes Page and Panel contributions in the internal
// ownership snapshot.
type ContributionKind string

const (
	ContributionPage  ContributionKind = "page"
	ContributionPanel ContributionKind = "panel"
)

// Contribution is one Application/UI-Host ownership row. Only one of Page or
// Panel is populated based on Kind.
type Contribution struct {
	Kind  ContributionKind
	Owner ContributionOwner
	Page  PageDefinition
	Panel PanelDefinition
}

type pageEntry struct {
	owner ContributionOwner
	def   PageDefinition
}

type panelEntry struct {
	owner ContributionOwner
	def   PanelDefinition
}

type clientModuleEntry struct {
	owner ContributionOwner
	def   ClientModule
}

type registry struct {
	mu             sync.Mutex
	pages          map[string]pageEntry
	panels         map[string]panelEntry
	clientModules  map[string]clientModuleEntry
	pageOrder      []string
	panelOrder     []string
	clientModOrder []string
	onChange       func()
}

// NewRegistry returns one UI Composition Registry owned by a UI Host
// activation. onChange is called after a successful page/panel register or
// unregister so the existing Observation transport can emit a minimal
// invalidation event.
func NewRegistry(onChange func()) Registry {
	return &registry{
		pages:         make(map[string]pageEntry),
		panels:        make(map[string]panelEntry),
		clientModules: make(map[string]clientModuleEntry),
		onChange:      onChange,
	}
}

func (r *registry) RegisterPage(owner ContributionOwner, def PageDefinition) (func() error, error) {
	if owner.ComponentID == "" || owner.ActivationID == "" {
		return nil, ErrContributionOwner
	}
	if def.ID == "" {
		return nil, ErrEmptyPageDefinition
	}
	r.mu.Lock()
	if _, ok := r.pages[def.ID]; ok {
		r.mu.Unlock()
		return nil, ErrDuplicatePage
	}
	r.pages[def.ID] = pageEntry{owner: owner, def: def}
	r.pageOrder = append(r.pageOrder, def.ID)
	r.mu.Unlock()
	r.notify()
	return r.unregisterPageFunc(owner, def.ID), nil
}

func (r *registry) RegisterPanel(owner ContributionOwner, def PanelDefinition) (func() error, error) {
	if owner.ComponentID == "" || owner.ActivationID == "" {
		return nil, ErrContributionOwner
	}
	if def.ID == "" {
		return nil, ErrEmptyPanelDefinition
	}
	r.mu.Lock()
	if _, ok := r.panels[def.ID]; ok {
		r.mu.Unlock()
		return nil, ErrDuplicatePanel
	}
	r.panels[def.ID] = panelEntry{owner: owner, def: def}
	r.panelOrder = append(r.panelOrder, def.ID)
	r.mu.Unlock()
	r.notify()
	return r.unregisterPanelFunc(owner, def.ID), nil
}

func (r *registry) RegisterClientModule(owner ContributionOwner, def ClientModule) (func() error, error) {
	if owner.ComponentID == "" || owner.ActivationID == "" {
		return nil, ErrContributionOwner
	}
	if def.Name == "" {
		return nil, ErrEmptyClientModule
	}
	r.mu.Lock()
	if _, ok := r.clientModules[def.Name]; ok {
		r.mu.Unlock()
		return nil, ErrDuplicateClientModule
	}
	r.clientModules[def.Name] = clientModuleEntry{owner: owner, def: def}
	r.clientModOrder = append(r.clientModOrder, def.Name)
	r.mu.Unlock()
	r.notify()
	return r.unregisterClientModuleFunc(owner, def.Name), nil
}

func (r *registry) unregisterClientModuleFunc(owner ContributionOwner, id string) func() error {
	return func() error {
		r.mu.Lock()
		entry, ok := r.clientModules[id]
		if !ok {
			r.mu.Unlock()
			return nil // already removed; cleanup is idempotent
		}
		if entry.owner != owner {
			r.mu.Unlock()
			return nil // newer activation owns this ID; stale cleanup must not delete it
		}
		delete(r.clientModules, id)
		r.clientModOrder = removeOrderID(r.clientModOrder, id)
		r.mu.Unlock()
		r.notify()
		return nil
	}
}

// ListClientModules returns the registered frontend modules in
// registration order — the /api/ui/client-modules manifest source.
func (r *registry) ListClientModules() []ClientModule {
	r.mu.Lock()
	defer r.mu.Unlock()
	out := make([]ClientModule, 0, len(r.clientModOrder))
	for _, id := range r.clientModOrder {
		out = append(out, r.clientModules[id].def)
	}
	return out
}

func (r *registry) unregisterPageFunc(owner ContributionOwner, id string) func() error {
	return func() error {
		r.mu.Lock()
		entry, ok := r.pages[id]
		if !ok {
			r.mu.Unlock()
			return nil // already removed; cleanup is idempotent
		}
		if entry.owner != owner {
			r.mu.Unlock()
			return nil // newer activation owns this ID; stale cleanup must not delete it
		}
		delete(r.pages, id)
		r.pageOrder = removeOrderID(r.pageOrder, id)
		r.mu.Unlock()
		r.notify()
		return nil
	}
}

func (r *registry) unregisterPanelFunc(owner ContributionOwner, id string) func() error {
	return func() error {
		r.mu.Lock()
		entry, ok := r.panels[id]
		if !ok {
			r.mu.Unlock()
			return nil // already removed; cleanup is idempotent
		}
		if entry.owner != owner {
			r.mu.Unlock()
			return nil // newer activation owns this ID; stale cleanup must not delete it
		}
		delete(r.panels, id)
		r.panelOrder = removeOrderID(r.panelOrder, id)
		r.mu.Unlock()
		r.notify()
		return nil
	}
}

func (r *registry) ListPages() []PageDefinition {
	r.mu.Lock()
	defer r.mu.Unlock()
	entries := r.orderedPageEntriesLocked()
	out := make([]PageDefinition, 0, len(entries))
	for _, entry := range entries {
		out = append(out, entry.def)
	}
	return out
}

func (r *registry) ListPanels() []PanelDefinition {
	r.mu.Lock()
	defer r.mu.Unlock()
	entries := r.orderedPanelEntriesLocked()
	out := make([]PanelDefinition, 0, len(entries))
	for _, entry := range entries {
		out = append(out, entry.def)
	}
	return out
}

func (r *registry) Snapshot() CompositionSnapshot {
	r.mu.Lock()
	defer r.mu.Unlock()
	pageEntries := r.orderedPageEntriesLocked()
	panelEntries := r.orderedPanelEntriesLocked()
	out := CompositionSnapshot{
		Pages:  make([]PageDefinition, 0, len(pageEntries)),
		Panels: make([]PanelDefinition, 0, len(panelEntries)),
	}
	for _, entry := range pageEntries {
		out.Pages = append(out.Pages, entry.def)
	}
	for _, entry := range panelEntries {
		out.Panels = append(out.Panels, entry.def)
	}
	return out
}

func (r *registry) Contributions() []Contribution {
	r.mu.Lock()
	defer r.mu.Unlock()
	pageEntries := r.orderedPageEntriesLocked()
	panelEntries := r.orderedPanelEntriesLocked()
	out := make([]Contribution, 0, len(pageEntries)+len(panelEntries))
	for _, entry := range pageEntries {
		out = append(out, Contribution{Kind: ContributionPage, Owner: entry.owner, Page: entry.def})
	}
	for _, entry := range panelEntries {
		out = append(out, Contribution{Kind: ContributionPanel, Owner: entry.owner, Panel: entry.def})
	}
	return out
}

func (r *registry) orderedPageEntriesLocked() []pageEntry {
	entries := make([]pageEntry, 0, len(r.pages))
	for _, id := range r.pageOrder {
		if entry, ok := r.pages[id]; ok {
			entries = append(entries, entry)
		}
	}
	sort.SliceStable(entries, func(i, j int) bool {
		return entries[i].def.Order < entries[j].def.Order
	})
	return entries
}

func (r *registry) orderedPanelEntriesLocked() []panelEntry {
	entries := make([]panelEntry, 0, len(r.panels))
	for _, id := range r.panelOrder {
		if entry, ok := r.panels[id]; ok {
			entries = append(entries, entry)
		}
	}
	sort.SliceStable(entries, func(i, j int) bool {
		return entries[i].def.Order < entries[j].def.Order
	})
	return entries
}

func (r *registry) notify() {
	if r.onChange != nil {
		r.onChange()
	}
}

func removeOrderID(ids []string, id string) []string {
	out := ids[:0]
	for _, existing := range ids {
		if existing != id {
			out = append(out, existing)
		}
	}
	return out
}
