package runtime

import (
	"sort"
)

// Developer Console Observation Model (UI-02).
//
// RuntimeSnapshot is the immutable, self-contained source of truth for the
// console: every row is a value copy produced at ONE orchestrator linearization
// point (cmdSnapshot). No kernel pointers, channels, mutexes, or provider
// values are exposed, and callers cannot mutate Runtime state through a
// snapshot. Rows are deterministically ordered so two snapshots taken while
// the Runtime is unchanged have identical semantics.
//
// Coherence (§5.5 frozen decision): the builder reads fiber state, realm
// records, and effect slots at one orchestrator boundary. Dependency statuses
// are classified against the SAME realm read (nearest record on the scope
// path, retiring flag, owner state), so a snapshot never combines two
// different moments into a "looks consistent" object.
//
// Component vs Fiber vs Activation stay distinct: a Fiber is one Component
// instance; every activation generation carries a fresh ActivationID; provider
// identity is (OwnerFiberID, OwnerActivationID) — never just a key.

// FiberSnapshot is one live Fiber and its current activation generation.
// StateGone fibers are terminal history (Timeline), not current state, so they
// are excluded from the live view — mirroring the kernel's paper-level
// semantic projection.
type FiberSnapshot struct {
	ID   FiberID
	Name string // Component Name(); diagnostics label (Fiber is the instance identity)
	// Component is the same label today; kept as a distinct field so the UI can
	// render the Component/Fiber roles separately. A stable Component identity
	// beyond the name is out of scope (decision §5.1).
	Component string

	State         FiberState
	Intent        Intent
	Err           string
	ActivationID  ActivationID // 0 = no live activation (Pending/Failed)
	ScopeID       ScopeID
	ParentFiberID FiberID
	ChildFiberIDs []FiberID

	Dependencies []DependencyView
	Providers    []ProviderView
	Effects      []EffectView
}

// ProviderView preserves Provider Identity — sibling scopes providing the same
// key are never merged (§8, §16).
type ProviderView struct {
	Key string
	// State is the owning Fiber's lifecycle state at the snapshot point.
	// Retiring distinguishes the withdrawal window: the record no longer
	// satisfies new dependents but the owner has not unloaded yet.
	State    FiberState
	Retiring bool

	OwnerFiberID      FiberID
	OwnerActivationID ActivationID
	ScopeID           ScopeID
}

// DependencyStatus classifies one declared dependency at the snapshot point.
type DependencyStatus string

const (
	// DependencySatisfied: the activation's captured provider binding is the
	// nearest record on the scope path, not retiring, and its owner is Active
	// on the same activation.
	DependencySatisfied DependencyStatus = "Satisfied"
	// DependencyWaiting: no provider is available on the scope path; the fiber
	// cannot (yet) activate.
	DependencyWaiting DependencyStatus = "Waiting"
	// DependencyWithdrawn: the activation captured a binding, but that binding
	// is no longer the satisfiable nearest record (retired/withdrawn).
	DependencyWithdrawn DependencyStatus = "Withdrawn"
)

// DependencyView is one required capability of a Fiber.
type DependencyView struct {
	Key                  string
	ConsumerFiberID      FiberID
	ConsumerActivationID ActivationID // 0 while Waiting (no live activation)
	ProviderFiberID      FiberID
	ProviderActivationID ActivationID
	Status               DependencyStatus
	Reason               string // diagnostics: e.g. "Provider unavailable"
}

// EffectView is one effect slot of one activation (registration order Seq).
type EffectView struct {
	OwnerFiberID FiberID
	ActivationID ActivationID
	Seq          uint64
	Kind         EffectKind
	Key          string // capability key for Provider effects; "" otherwise
	State        string // "Installing" | "Committed" | "Undoing" | "Undone"
}

// ScopeSnapshot is one provider scope (realm) of the scope tree (§15, §16).
type ScopeSnapshot struct {
	ID       ScopeID
	ParentID ScopeID // 0 at the root scope
	// ProviderKeys lists the keys this scope provides exclusively in its own
	// map; sibling scopes may list the same key independently.
	ProviderKeys []string
	// FiberIDs lists live fibers resolving through this scope.
	FiberIDs []FiberID
}

// RuntimeSnapshot is the full, self-contained console fact anchor.
type RuntimeSnapshot struct {
	RuntimeID RuntimeID
	State     RuntimeState
	// EventSequence is the canonical event sequence at this snapshot's
	// linearization point — the resume anchor for a reconnecting event
	// consumer (UI-03).
	EventSequence uint64

	Fibers       []FiberSnapshot
	Scopes       []ScopeSnapshot
	Providers    []ProviderView
	Dependencies []DependencyView
	Effects      []EffectView
}

// ---------------------------------------------------------------------------
// Snapshot command (orchestrator-linearized)
// ---------------------------------------------------------------------------

type snapshotReply struct {
	snap RuntimeSnapshot
	err  error
}

// cmdSnapshot requests a snapshot at this command's linearization point. Its
// apply() runs the whole projection on the orchestrator goroutine, so every
// orchestrator-published field is read at one point in the command order.
type cmdSnapshot struct {
	reply chan snapshotReply
}

func (c *cmdSnapshot) apply(o *orchestrator) {
	c.reply <- snapshotReply{snap: o.buildSnapshot()}
}

func effectStateString(st effectSlotState) string {
	switch st {
	case effectInstalling:
		return "Installing"
	case effectCommitted:
		return "Committed"
	case effectUndoing:
		return "Undoing"
	case effectUndone:
		return "Undone"
	default:
		return "?"
	}
}

type fiberInfo struct {
	f        *Fiber
	name     string
	state    FiberState
	intent   Intent
	errStr   string
	actID    ActivationID
	scopeID  ScopeID
	parentID FiberID
	children []FiberID
	declared []Dependency
	captured []DependencySnapshot
	effects  []EffectView
	deps     []DependencyView
}

type recordSnap struct {
	key      CapabilityKey
	identity ProviderIdentity
	retiring bool
}

type realmSnap struct {
	id       ScopeID
	parent   *realmSnap
	byKey    map[CapabilityKey]recordSnap
	provider []ProviderView
	keys     []string
	fibers   []FiberID
}

// nearestRecord walks the scope path (own -> parent) and returns the nearest
// record for key.
func (r *realmSnap) nearestRecord(key CapabilityKey) (recordSnap, bool) {
	for cur := r; cur != nil; cur = cur.parent {
		if rec, ok := cur.byKey[key]; ok {
			return rec, true
		}
	}
	return recordSnap{}, false
}

// buildSnapshot projects the whole Runtime state. MUST run on the orchestrator
// goroutine: fiber state/intent/activation/children are orchestrator-published
// and therefore stable here; realm records and effect slots are read under
// their own locks because Apply/Unwind goroutines mutate them.
func (o *orchestrator) buildSnapshot() RuntimeSnapshot {
	r := o.rt

	r.mu.RLock()
	state := r.state
	fibers := make([]*Fiber, 0, len(r.fibers))
	for _, f := range r.fibers {
		fibers = append(fibers, f)
	}
	r.mu.RUnlock()

	seq := r.events.current()

	// Pass 1: per-fiber facts (one f.mu read per fiber).
	infos := make([]fiberInfo, 0, len(fibers))
	ownerInfo := make(map[FiberID]fiberInfo, len(fibers))
	realmByPtr := map[*realm]*realmSnap{r.rootRealm: {id: 0}}
	for _, f := range fibers {
		name := f.component.Name()
		f.mu.RLock()
		info := fiberInfo{
			f:        f,
			name:     name,
			state:    f.state,
			intent:   f.intent,
			scopeID:  f.realm.id,
			declared: f.inject,
		}
		if f.err != nil {
			info.errStr = f.err.Error()
		}
		if f.parent != nil {
			info.parentID = f.parent.id
		}
		info.children = make([]FiberID, 0, len(f.children))
		for cid := range f.children {
			info.children = append(info.children, cid)
		}
		realm := f.realm
		act := f.activation
		if act != nil {
			info.actID = act.id
			info.captured = act.deps
			act.ctx.mu.Lock()
			for _, s := range act.ctx.effects {
				info.effects = append(info.effects, EffectView{
					OwnerFiberID: f.id,
					ActivationID: act.id,
					Seq:          s.seq,
					Kind:         s.kind,
					Key:          effectKeyString(s),
					State:        effectStateString(s.state),
				})
			}
			act.ctx.mu.Unlock()
		}
		f.mu.RUnlock()
		if info.state != StateGone {
			if _, ok := realmByPtr[realm]; !ok {
				realmByPtr[realm] = &realmSnap{id: realm.id, byKey: make(map[CapabilityKey]recordSnap)}
			}
		}
		infos = append(infos, info)
		ownerInfo[f.id] = info
	}

	// Pass 2: read each reachable realm's own map exactly once.
	for realm, rs := range realmByPtr {
		_ = realm
		rs.byKey = make(map[CapabilityKey]recordSnap)
		realm.mu.Lock()
		for _, rec := range realm.own {
			rs.byKey[rec.key] = recordSnap{key: rec.key, identity: rec.identity, retiring: rec.retiring}
			rs.keys = append(rs.keys, rec.key.String())
		}
		realm.mu.Unlock()
		sort.Strings(rs.keys)
	}

	// Link scope parents (walk realm.parent; every intermediate realm has a
	// live owner fiber and is therefore in the set).
	for realm, rs := range realmByPtr {
		for p := realm.parent; p != nil; p = p.parent {
			if parentSnap, ok := realmByPtr[p]; ok {
				rs.parent = parentSnap
				break
			}
		}
	}

	// Pass 3: provider rows from the same coherent realm read.
	for _, info := range infos {
		if info.state == StateGone {
			continue
		}
		if rs, ok := realmByPtr[info.f.realm]; ok {
			rs.fibers = append(rs.fibers, info.f.id)
		}
	}
	var provs []ProviderView
	for _, rs := range realmByPtr {
		for _, rec := range rs.byKey {
			owner, ok := ownerInfo[rec.identity.FiberID]
			if !ok || owner.state == StateGone {
				continue // defensive: records are removed before their owner ends
			}
			rs.provider = append(rs.provider, ProviderView{
				Key:               rec.key.String(),
				State:             owner.state,
				Retiring:          rec.retiring,
				OwnerFiberID:      rec.identity.FiberID,
				OwnerActivationID: rec.identity.ActivationID,
				ScopeID:           rs.id,
			})
		}
		provs = append(provs, rs.provider...)
	}

	// Pass 4: dependency views classified against the same realm read.
	satisfiable := func(cur recordSnap) bool {
		owner, ok := ownerInfo[cur.identity.FiberID]
		if !ok {
			return false
		}
		return !cur.retiring && owner.state == StateActive && owner.actID == cur.identity.ActivationID
	}
	var deps []DependencyView
	for i := range infos {
		info := &infos[i]
		if info.state == StateGone {
			continue
		}
		rs := realmByPtr[info.f.realm]
		for _, dep := range info.declared {
			dv := DependencyView{Key: dep.Key.String(), ConsumerFiberID: info.f.id, ConsumerActivationID: info.actID}
			cur, has := rs.nearestRecord(dep.Key)
			captured, bound := capturedBinding(info.captured, dep.Key)
			switch {
			case bound && has && cur.identity == captured.Provider && satisfiable(cur):
				dv.ProviderFiberID = captured.Provider.FiberID
				dv.ProviderActivationID = captured.Provider.ActivationID
				dv.Status = DependencySatisfied
			case bound:
				dv.ProviderFiberID = captured.Provider.FiberID
				dv.ProviderActivationID = captured.Provider.ActivationID
				dv.Status = DependencyWithdrawn
			default:
				dv.Status = DependencyWaiting
				dv.Reason = "Provider unavailable"
			}
			info.deps = append(info.deps, dv)
			deps = append(deps, dv)
		}
	}

	// Deterministic ordering of every row set.
	sort.Slice(infos, func(i, j int) bool { return infos[i].f.id < infos[j].f.id })
	sort.Slice(provs, func(i, j int) bool {
		a, b := provs[i], provs[j]
		if a.ScopeID != b.ScopeID {
			return a.ScopeID < b.ScopeID
		}
		if a.Key != b.Key {
			return a.Key < b.Key
		}
		if a.OwnerFiberID != b.OwnerFiberID {
			return a.OwnerFiberID < b.OwnerFiberID
		}
		return a.OwnerActivationID < b.OwnerActivationID
	})
	sort.Slice(deps, func(i, j int) bool {
		a, b := deps[i], deps[j]
		if a.ConsumerFiberID != b.ConsumerFiberID {
			return a.ConsumerFiberID < b.ConsumerFiberID
		}
		if a.ConsumerActivationID != b.ConsumerActivationID {
			return a.ConsumerActivationID < b.ConsumerActivationID
		}
		return a.Key < b.Key
	})

	scopeList := make([]ScopeSnapshot, 0, len(realmByPtr))
	for _, rs := range realmByPtr {
		row := ScopeSnapshot{ID: rs.id, ProviderKeys: rs.keys}
		if rs.parent != nil {
			row.ParentID = rs.parent.id
		}
		row.FiberIDs = rs.fibers
		sort.Slice(row.FiberIDs, func(i, j int) bool { return row.FiberIDs[i] < row.FiberIDs[j] })
		scopeList = append(scopeList, row)
	}
	sort.Slice(scopeList, func(i, j int) bool { return scopeList[i].ID < scopeList[j].ID })

	snap := RuntimeSnapshot{
		RuntimeID:     r.runtimeID,
		State:         state,
		EventSequence: seq,
		Providers:     provs,
		Dependencies:  deps,
		Scopes:        scopeList,
	}
	var effects []EffectView
	for i := range infos {
		info := &infos[i]
		if info.state == StateGone {
			continue
		}
		fs := FiberSnapshot{
			ID:            info.f.id,
			Name:          info.name,
			Component:     info.name,
			State:         info.state,
			Intent:        info.intent,
			Err:           info.errStr,
			ActivationID:  info.actID,
			ScopeID:       info.scopeID,
			ParentFiberID: info.parentID,
			ChildFiberIDs: info.children,
			Dependencies:  info.deps,
			Effects:       info.effects,
		}
		for _, p := range provs {
			if p.OwnerFiberID == info.f.id {
				fs.Providers = append(fs.Providers, p)
			}
		}
		snap.Fibers = append(snap.Fibers, fs)
		effects = append(effects, info.effects...)
	}
	sort.Slice(effects, func(i, j int) bool {
		a, b := effects[i], effects[j]
		if a.OwnerFiberID != b.OwnerFiberID {
			return a.OwnerFiberID < b.OwnerFiberID
		}
		if a.ActivationID != b.ActivationID {
			return a.ActivationID < b.ActivationID
		}
		return a.Seq < b.Seq
	})
	snap.Effects = effects
	return snap
}

func effectKeyString(s *effectSlot) string {
	if s.kind == EffectKindProvider {
		return s.key.String()
	}
	return ""
}

// capturedBinding returns the captured dependency snapshot for key, if any.
func capturedBinding(deps []DependencySnapshot, key CapabilityKey) (DependencySnapshot, bool) {
	for _, d := range deps {
		if d.Key == key {
			return d, true
		}
	}
	return DependencySnapshot{}, false
}
