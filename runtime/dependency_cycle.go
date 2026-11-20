package runtime

import "fmt"

// This file implements declared-dependency cycle detection. It runs at mount
// boundaries (Runtime.Load / Context.Child) and rejects a new Fiber that would
// create a strong dependency cycle among already-mounted Fibers, with an
// actionable diagnostic naming the keys along the cycle.
//
// Realm awareness (ADR-0001, paper §4.4 Isolation): a consumer x may be
// satisfied by provider y only when y's effective realm for the key is exactly
// x's effective realm for that key — one namespace per (key, ρ) binding, no
// ancestor walk. Two scoped siblings in separate realms therefore never form
// an edge, so they cannot produce a false-positive cycle. Self loops (x
// provides a key it also requires) are rejected outright as unresolvable
// (see ErrSelfDependency): the gate cannot be satisfied by the fiber's own
// provision, which does not exist before the activation starts.
//
// Policy: reject on Load/Child (ErrDependencyCycle), so a cyclic composition
// fails deterministically at the mount boundary instead of hanging Pending.

// realmCanResolve reports whether x (the consumer candidate) resolves key in
// exactly the namespace y provides it in.
func realmCanResolve(x *Fiber, y *Fiber, key CapabilityKey) bool {
	if y == nil || y.realm == nil || x == nil {
		return false
	}
	return effectiveRealm(x, key) == effectiveRealm(y, key)
}

// findDeclaredCycle returns an error describing a declared dependency cycle if
// adding candidate (with inject/provide) to fibers would create one. fibers
// must not contain candidate itself.
func findDeclaredCycle(candidate *Fiber, fibers []*Fiber) error {
	// RT-13: a zero-valued capability key (never built by NewKey/NewMetaKey)
	// would collide across every type parameter; reject the declaration at the
	// mount boundary.
	for _, k := range candidate.provide {
		if !k.valid() {
			return fmt.Errorf("%w: %q declares an invalid (zero) capability in its Provide set", ErrInvalidKey, candidate.Name())
		}
	}
	for _, d := range candidate.inject {
		if !d.Key.valid() {
			return fmt.Errorf("%w: %q declares an invalid (zero) capability in its Inject set", ErrInvalidKey, candidate.Name())
		}
	}

	// RT-09: a self-dependency (the candidate requires a key it also provides)
	// is unresolvable — the gate cannot be satisfied by the fiber's own
	// provision, which does not exist before the activation starts. Reject it
	// here with the same actionable-diagnostic policy as cross-fiber cycles.
	selfDep := make([]CapabilityKey, 0, len(candidate.inject))
	for _, dep := range candidate.inject {
		if yDeclaresProvide(candidate, dep.Key) {
			selfDep = append(selfDep, dep.Key)
		}
	}
	if len(selfDep) > 0 {
		names := make([]string, 0, len(selfDep))
		for _, k := range selfDep {
			names = append(names, k.String())
		}
		return fmt.Errorf("%w: %q requires keys it also provides: %v",
			ErrSelfDependency, candidate.Name(), names)
	}

	nodes := append([]*Fiber(nil), fibers...)
	nodes = append(nodes, candidate)

	provideKeys := func(f *Fiber) map[CapabilityKey]struct{} {
		m := make(map[CapabilityKey]struct{}, len(f.provide))
		for _, k := range f.provide {
			m[k] = struct{}{}
		}
		return m
	}
	byID := make(map[FiberID]*Fiber, len(nodes))
	for _, f := range nodes {
		byID[f.id] = f
	}

	// Edge x -> y when x declares it requires a key that y declares it
	// provides AND y's realm is reachable from x's realm. Self edges (x==y)
	// are ignored.
	adj := make(map[FiberID][]FiberID)
	for _, x := range nodes {
		if x.inject == nil {
			continue
		}
		provides := provideKeys(x)
		for _, dep := range x.inject {
			if _, self := provides[dep.Key]; self {
				continue // self-satisfiable; never a cross-fiber cycle
			}
			for _, y := range nodes {
				if y == x {
					continue
				}
				if !yDeclaresProvide(y, dep.Key) {
					continue
				}
				if !realmCanResolve(x, y, dep.Key) {
					continue
				}
				adj[x.id] = append(adj[x.id], y.id)
			}
		}
	}

	// Standard DFS cycle detection over the declared graph, anchored at the
	// candidate so diagnostics always include the newly mounted Fiber.
	state := make(map[FiberID]uint8) // 0=unseen 1=visiting 2=done
	var stack []FiberID
	var visit func(id FiberID) bool
	visit = func(id FiberID) bool {
		state[id] = 1
		stack = append(stack, id)
		for _, next := range adj[id] {
			if state[next] == 1 {
				// Cycle found: build a path from next..stack.
				var cycle []FiberID
				started := false
				for _, s := range stack {
					if s == next {
						started = true
					}
					if started {
						cycle = append(cycle, s)
					}
				}
				cycle = append(cycle, next)
				return true
			}
			if state[next] == 0 && visit(next) {
				return true
			}
		}
		state[id] = 2
		stack = stack[:len(stack)-1]
		return false
	}

	if visit(candidate.id) {
		names := make([]string, 0, len(stack))
		for _, id := range stack {
			if f, ok := byID[id]; ok {
				names = append(names, f.Name())
			}
		}
		return fmt.Errorf("%w: declared dependency cycle involving %q (chain: %v)", ErrDependencyCycle, candidate.Name(), names)
	}
	return nil
}

func yDeclaresProvide(y *Fiber, key CapabilityKey) bool {
	for _, k := range y.provide {
		if k == key {
			return true
		}
	}
	return false
}
