package insight

import (
	"context"
	"encoding/json"
	"errors"
	"sort"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/runtime"
)

// ErrUnavailable is returned when the service is used before it is bound to a
// Controller and Runtime snapshot source.
var ErrUnavailable = errors.New("insight service is not available")

// Snapshotter is the read-only Runtime surface insight needs. *runtime.Runtime
// satisfies it; tests may substitute a recording double.
type Snapshotter interface {
	Snapshot(context.Context) (runtime.RuntimeSnapshot, error)
}

// Service projects graphs from two authoritative, read-only sources: the Config
// Controller's declarations and one Runtime snapshot. It keeps no cached
// lifecycle truth and never resolves dependencies itself.
type Service struct {
	ctrl *config.Controller
	rt   Snapshotter
}

// New binds the service to a Config Controller and a Runtime snapshot source.
func New(ctrl *config.Controller, rt Snapshotter) *Service {
	return &Service{ctrl: ctrl, rt: rt}
}

// Graph takes one fresh snapshot and returns the declared/runtime graph pair.
// The snapshot's EventSequence is the version anchor of the whole response.
func (s *Service) Graph(ctx context.Context) (Graph, error) {
	if ctx == nil {
		ctx = context.Background()
	}
	if s == nil || s.ctrl == nil || s.rt == nil {
		return Graph{}, ErrUnavailable
	}
	snap, err := s.rt.Snapshot(ctx)
	if err != nil {
		return Graph{}, err
	}
	return Graph{
		EventSequence: snap.EventSequence,
		Declared:      declaredGraph(s.ctrl.Declarations()),
		Runtime:       runtimeGraph(snap),
	}, nil
}

// Schemas returns the JSON Schemas registered with the Config Controller.
func (s *Service) Schemas() (SchemaList, error) {
	if s == nil || s.ctrl == nil {
		return SchemaList{}, ErrUnavailable
	}
	registered := s.ctrl.Schemas()
	out := SchemaList{Components: make([]SchemaEntry, 0, len(registered))}
	for typ, schema := range registered {
		raw, err := schema.JSONSchema()
		if err != nil {
			return SchemaList{}, err
		}
		out.Components = append(out.Components, SchemaEntry{Type: typ, Schema: json.RawMessage(raw)})
	}
	sort.Slice(out.Components, func(i, j int) bool {
		return out.Components[i].Type < out.Components[j].Type
	})
	return out, nil
}

// declaredGraph projects controller declarations. Edges are key matches between
// declarations — potential wiring, never a claim about actual runtime bindings.
func declaredGraph(decls []config.Declaration) DeclaredGraph {
	out := DeclaredGraph{
		Nodes: make([]DeclaredNode, 0, len(decls)),
		Edges: make([]DeclaredEdge, 0),
	}
	providers := make(map[string][]string)
	for _, d := range decls {
		out.Nodes = append(out.Nodes, DeclaredNode{
			ID:       d.ID,
			Name:     d.Name,
			Type:     d.Type,
			Requires: stringsOrEmpty(d.Requires),
			Provides: stringsOrEmpty(d.Provides),
		})
		for _, key := range d.Provides {
			providers[key] = append(providers[key], d.ID)
		}
	}
	for _, d := range decls {
		for _, key := range d.Requires {
			for _, providerID := range providers[key] {
				if providerID == d.ID {
					continue // a declaration is not its own provider
				}
				out.Edges = append(out.Edges, DeclaredEdge{From: d.ID, To: providerID, Key: key})
			}
		}
	}
	sort.Slice(out.Edges, func(i, j int) bool {
		a, b := out.Edges[i], out.Edges[j]
		if a.From != b.From {
			return a.From < b.From
		}
		if a.Key != b.Key {
			return a.Key < b.Key
		}
		return a.To < b.To
	})
	return out
}

// runtimeGraph projects ONLY snapshot rows. ProviderView/DependencyView supply
// the keys and binding status; FiberSnapshot supplies identity and state.
func runtimeGraph(snap runtime.RuntimeSnapshot) RuntimeGraph {
	out := RuntimeGraph{
		Nodes: make([]RuntimeNode, 0, len(snap.Fibers)),
		Edges: make([]RuntimeEdge, 0, len(snap.Dependencies)),
	}
	provided := make(map[runtime.FiberID][]string)
	required := make(map[runtime.FiberID][]string)
	for _, p := range snap.Providers {
		provided[p.OwnerFiberID] = append(provided[p.OwnerFiberID], p.Key)
	}
	for _, d := range snap.Dependencies {
		required[d.ConsumerFiberID] = append(required[d.ConsumerFiberID], d.Key)
	}
	for _, f := range snap.Fibers {
		out.Nodes = append(out.Nodes, RuntimeNode{
			FiberID:      uint64(f.ID),
			Name:         f.Name,
			State:        f.State.String(),
			ActivationID: uint64(f.ActivationID),
			ScopeID:      uint64(f.ScopeID),
			Provides:     uniqueSorted(provided[f.ID]),
			Requires:     uniqueSorted(required[f.ID]),
		})
	}
	for _, d := range snap.Dependencies {
		out.Edges = append(out.Edges, RuntimeEdge{
			ConsumerFiberID: uint64(d.ConsumerFiberID),
			ProviderFiberID: uint64(d.ProviderFiberID),
			Key:             d.Key,
			Status:          string(d.Status),
			Reason:          d.Reason,
		})
	}
	sort.Slice(out.Nodes, func(i, j int) bool { return out.Nodes[i].FiberID < out.Nodes[j].FiberID })
	sort.Slice(out.Edges, func(i, j int) bool {
		a, b := out.Edges[i], out.Edges[j]
		if a.ConsumerFiberID != b.ConsumerFiberID {
			return a.ConsumerFiberID < b.ConsumerFiberID
		}
		if a.Key != b.Key {
			return a.Key < b.Key
		}
		return a.ProviderFiberID < b.ProviderFiberID
	})
	return out
}

func stringsOrEmpty(in []string) []string {
	if len(in) == 0 {
		return []string{}
	}
	out := make([]string, len(in))
	copy(out, in)
	return out
}

func uniqueSorted(in []string) []string {
	if len(in) == 0 {
		return []string{}
	}
	out := append([]string(nil), in...)
	sort.Strings(out)
	n := 0
	for _, s := range out {
		if n == 0 || s != out[n-1] {
			out[n] = s
			n++
		}
	}
	return out[:n]
}
