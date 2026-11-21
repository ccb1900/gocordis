package runtime

import (
	"time"

	"dynamic-runtime/extensions/devtools"
	coreruntime "dynamic-runtime/runtime"
)

// Query and page identities are stable wire contracts shared with the console
// transports (the embedded bootstrap consumes the two named queries).
const (
	SnapshotQuery  = "runtime/snapshot"
	EventsQuery    = "runtime/events"
	DashboardQuery = "framework/dashboard"

	InspectorPageID   = "inspector"
	DashboardPageID   = "dashboard"
	InspectorRoute    = "/inspector"
	DashboardRoute    = "/dashboard"
	InspectorRenderer = "runtime-inspector"
	DashboardRenderer = "framework-dashboard"
)

// SnapshotDTO is the wire shape of runtime/snapshot. Every row is a direct
// copy of one Runtime.Snapshot row; nothing is derived or recomputed here.
type SnapshotDTO struct {
	Sequence     uint64          `json:"sequence"`
	RuntimeID    string          `json:"runtimeID"`
	State        string          `json:"state"`
	Fibers       []FiberDTO      `json:"fibers"`
	Scopes       []ScopeDTO      `json:"scopes"`
	Providers    []ProviderDTO   `json:"providers"`
	Dependencies []DependencyDTO `json:"dependencies"`
	Effects      []EffectDTO     `json:"effects"`
}

type FiberDTO struct {
	ID             uint64   `json:"id"`
	Name           string   `json:"name"`
	Component      string   `json:"component"`
	State          string   `json:"state"`
	Intent         string   `json:"intent"`
	Err            string   `json:"err"`
	ActivationID   uint64   `json:"activationID"`
	ScopeID        uint64   `json:"scopeID"`
	ParentFiberIDs []uint64 `json:"parentFiberIDs"`
	ChildFiberIDs  []uint64 `json:"childFiberIDs"`
}

type ScopeDTO struct {
	ID           uint64   `json:"id"`
	ParentID     uint64   `json:"parentID"`
	ProviderKeys []string `json:"providerKeys"`
	FiberIDs     []uint64 `json:"fiberIDs"`
}

type ProviderDTO struct {
	Key               string `json:"key"`
	State             string `json:"state"`
	Retiring          bool   `json:"retiring"`
	OwnerFiberID      uint64 `json:"ownerFiberID"`
	OwnerActivationID uint64 `json:"ownerActivationID"`
	ScopeID           uint64 `json:"scopeID"`
}

type DependencyDTO struct {
	Key                  string `json:"key"`
	ConsumerFiberID      uint64 `json:"consumerFiberID"`
	ConsumerActivationID uint64 `json:"consumerActivationID"`
	ProviderFiberID      uint64 `json:"providerFiberID"`
	ProviderActivationID uint64 `json:"providerActivationID"`
	Status               string `json:"status"`
	Reason               string `json:"reason"`
}

type EffectDTO struct {
	OwnerFiberID uint64 `json:"ownerFiberID"`
	ActivationID uint64 `json:"activationID"`
	Seq          uint64 `json:"seq"`
	Kind         string `json:"kind"`
	Key          string `json:"key"`
	State        string `json:"state"`
}

// SnapshotFrom projects one coherent Runtime.Snapshot into wire DTOs.
func SnapshotFrom(snap coreruntime.RuntimeSnapshot) SnapshotDTO {
	out := SnapshotDTO{
		Sequence:     snap.EventSequence,
		RuntimeID:    string(snap.RuntimeID),
		State:        snap.State.String(),
		Fibers:       make([]FiberDTO, 0, len(snap.Fibers)),
		Scopes:       make([]ScopeDTO, 0, len(snap.Scopes)),
		Providers:    make([]ProviderDTO, 0, len(snap.Providers)),
		Dependencies: make([]DependencyDTO, 0, len(snap.Dependencies)),
		Effects:      make([]EffectDTO, 0, len(snap.Effects)),
	}
	for _, f := range snap.Fibers {
		parents := []uint64{}
		if f.ParentFiberID != 0 {
			parents = append(parents, uint64(f.ParentFiberID))
		}
		children := make([]uint64, 0, len(f.ChildFiberIDs))
		for _, id := range f.ChildFiberIDs {
			children = append(children, uint64(id))
		}
		out.Fibers = append(out.Fibers, FiberDTO{
			ID: uint64(f.ID), Name: f.Name, Component: f.Component,
			State: f.State.String(), Intent: f.Intent.String(), Err: f.Err,
			ActivationID: uint64(f.ActivationID), ScopeID: uint64(f.ScopeID),
			ParentFiberIDs: parents, ChildFiberIDs: children,
		})
	}
	for _, s := range snap.Scopes {
		keys := make([]string, 0, len(s.ProviderKeys))
		keys = append(keys, s.ProviderKeys...)
		ids := make([]uint64, 0, len(s.FiberIDs))
		for _, id := range s.FiberIDs {
			ids = append(ids, uint64(id))
		}
		out.Scopes = append(out.Scopes, ScopeDTO{ID: uint64(s.ID), ParentID: uint64(s.ParentID), ProviderKeys: keys, FiberIDs: ids})
	}
	for _, p := range snap.Providers {
		out.Providers = append(out.Providers, ProviderDTO{
			Key: p.Key, State: p.State.String(), Retiring: p.Retiring,
			OwnerFiberID: uint64(p.OwnerFiberID), OwnerActivationID: uint64(p.OwnerActivationID), ScopeID: uint64(p.ScopeID),
		})
	}
	for _, d := range snap.Dependencies {
		out.Dependencies = append(out.Dependencies, DependencyDTO{
			Key: d.Key, ConsumerFiberID: uint64(d.ConsumerFiberID),
			ConsumerActivationID: uint64(d.ConsumerActivationID),
			ProviderFiberID:      uint64(d.ProviderFiberID), ProviderActivationID: uint64(d.ProviderActivationID),
			Status: string(d.Status), Reason: d.Reason,
		})
	}
	for _, e := range snap.Effects {
		out.Effects = append(out.Effects, EffectDTO{
			OwnerFiberID: uint64(e.OwnerFiberID), ActivationID: uint64(e.ActivationID),
			Seq: e.Seq, Kind: e.Kind.String(), Key: e.Key, State: e.State,
		})
	}
	return out
}

// EventDTO is the wire shape of one canonical runtime event.
type EventDTO struct {
	Sequence     uint64 `json:"sequence"`
	Timestamp    string `json:"timestamp"`
	RuntimeID    string `json:"runtimeID"`
	Type         string `json:"type"`
	FiberID      uint64 `json:"fiberID"`
	ActivationID uint64 `json:"activationID"`
	Data         any    `json:"data"`
}

// EventsDTO is the wire shape of runtime/events: retained events plus the
// sequence watermark a reconnecting consumer resumes from.
type EventsDTO struct {
	Events   []EventDTO `json:"events"`
	Sequence uint64     `json:"sequence"`
}

func eventDTO(ev coreruntime.RuntimeEvent) EventDTO {
	timestamp := ""
	if !ev.Timestamp.IsZero() {
		timestamp = ev.Timestamp.UTC().Format(time.RFC3339Nano)
	}
	return EventDTO{
		Sequence: ev.Sequence, Timestamp: timestamp, RuntimeID: string(ev.RuntimeID),
		Type: string(ev.Type), FiberID: uint64(ev.FiberID),
		ActivationID: uint64(ev.ActivationID), Data: ev.Data,
	}
}

// EventsFrom maps a slice of canonical events into the events DTO.
func EventsFrom(events []coreruntime.RuntimeEvent) EventsDTO {
	out := EventsDTO{Events: make([]EventDTO, 0, len(events))}
	for _, ev := range events {
		out.Events = append(out.Events, eventDTO(ev))
		if ev.Sequence > out.Sequence {
			out.Sequence = ev.Sequence
		}
	}
	return out
}

// CountsDTO summarizes one Runtime.Snapshot for the framework dashboard.
type CountsDTO struct {
	Fibers       int `json:"fibers"`
	Active       int `json:"active"`
	Providers    int `json:"providers"`
	Dependencies int `json:"dependencies"`
	Effects      int `json:"effects"`
	Failed       int `json:"failed"`
	Pending      int `json:"pending"`
}

// DiagnosticDTO is the transport-neutral devtools finding shape.
type DiagnosticDTO struct {
	Severity string `json:"severity"`
	Code     string `json:"code"`
	Message  string `json:"message"`
}

// DashboardDTO is the wire shape of framework/dashboard.
type DashboardDTO struct {
	Sequence     uint64          `json:"sequence"`
	State        string          `json:"state"`
	Counts       CountsDTO       `json:"counts"`
	RecentEvents []EventDTO      `json:"recentEvents"`
	Diagnostics  []DiagnosticDTO `json:"diagnostics"`
}

// DashboardFrom builds the dashboard DTO from the SAME snapshot/ring facts as
// the dedicated queries. Counts are a summary of one coherent snapshot, never
// a second observation of the Runtime.
func DashboardFrom(snap coreruntime.RuntimeSnapshot, events EventsDTO, diagnostics []DiagnosticDTO) DashboardDTO {
	counts := CountsDTO{
		Fibers: len(snap.Fibers), Providers: len(snap.Providers),
		Dependencies: len(snap.Dependencies), Effects: len(snap.Effects),
	}
	for _, f := range snap.Fibers {
		switch f.State {
		case coreruntime.StateActive:
			counts.Active++
		case coreruntime.StateFailed:
			counts.Failed++
		case coreruntime.StatePending:
			counts.Pending++
		}
	}
	if diagnostics == nil {
		diagnostics = []DiagnosticDTO{}
	}
	return DashboardDTO{
		Sequence: snap.EventSequence, State: snap.State.String(), Counts: counts,
		RecentEvents: events.Events, Diagnostics: diagnostics,
	}
}

// DiagnosticsFrom maps devtools findings onto the wire DTO.
func DiagnosticsFrom(in []devtools.Diagnostic) []DiagnosticDTO {
	out := make([]DiagnosticDTO, 0, len(in))
	for _, d := range in {
		out = append(out, DiagnosticDTO{Severity: string(d.Severity), Code: string(d.Code), Message: d.Message})
	}
	return out
}
