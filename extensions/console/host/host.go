// Package uiplugin is the GOCORDIS UI Host plugin. It owns one Application UI
// Composition Registry per activation and exposes it as a Runtime Capability.
// Business Pages/Panels are registered by independent UI Contribution plugins;
// this host never enumerates a business plugin and never owns a default page
// set.
//
// The package also hosts the transport-facing adapter (Wails/HTTP) and the
// minimal UI Observation bridge. It never owns Application state and never
// touches Collector/Storage/FileSource/Runtime internals.
package host

import (
	"dynamic-runtime/extensions/console/registry"
)

// Re-exported composition contract aliases keep the public UI Host surface
// stable while the canonical types live in the Application layer.
type (
	PageDefinition      = registry.PageDefinition
	PanelDefinition     = registry.PanelDefinition
	CompositionSnapshot = registry.CompositionSnapshot
	Position            = registry.Position
	ContributionOwner   = registry.ContributionOwner
	Registry            = registry.Registry
)

const (
	PositionMain   = registry.PositionMain
	PositionRight  = registry.PositionRight
	PositionBottom = registry.PositionBottom
	PositionTop    = registry.PositionTop
	PositionLeft   = registry.PositionLeft
)

var (
	ErrDuplicatePage        = registry.ErrDuplicatePage
	ErrDuplicatePanel       = registry.ErrDuplicatePanel
	ErrMissingPage          = registry.ErrMissingPage
	ErrMissingPanel         = registry.ErrMissingPanel
	ErrContributionOwner    = registry.ErrContributionOwner
	ErrEmptyPageDefinition  = registry.ErrEmptyPageDefinition
	ErrEmptyPanelDefinition = registry.ErrEmptyPanelDefinition
)
