package runtime

import "fmt"

// This file implements declared-dependency cycle detection. It runs at mount
// boundaries (Runtime.Load / Context.Child) and rejects a new Fiber that would
// create a strong dependency cycle among already-mounted Fibers, with an
// actionable diagnostic naming the keys along the cycle.
//
// Realm awareness: a consumer x may be satisfied by provider y only when
// y.realm is reachable from x.realm (same realm or an ancestor). Two scoped
// siblings in separate realms therefore never form an edge, so they cannot
// produce a false-positive cycle. Self loops (x provides a key it also
// requires) are ignored: they are satisfiable only by a provider in the same
// realm and never by a second independent Fiber.
//
// Policy: reject on Load/Child (ErrDependencyCycle), so a cyclic composition
// fails deterministically at the mount boundary instead of hanging Pending.

// declaredCycleEdge reports whether candidate's realm can resolve a provider
// from y (same realm or ancestor).
func realmCanResolve(candidate *realm, y *Fiber) bool {
	if y == nil || y.realm == nil {
		return false
	}
	for r := candidate; r != nil; r = r.parent {
		if r == y.realm {
			return true
		}
	}
	return false
}

// findDeclaredCycle returns an error describing a declared dependency cycle if
// adding candidate (with inject/provide) to fibers would create one. fibers
// must not contain candidate itself.
func findDeclaredCycle(candidate *Fiber, fibers []*Fiber) error {
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
				if !realmCanResolve(x.realm, y) {
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
