// Package devtools provides pure, descriptive diagnostics over pluginmeta
// manifests and exposes them as a Runtime capability.
//
// Diagnose is side-effect free: it reads a Manifest value and returns
// Diagnostics. It never loads a component, registers a provider, calls
// Runtime.Load, or touches the filesystem. Host-version evaluation is opt-in
// through DiagnoseAgainst, which uses pluginmeta.ValidateAgainst.
package devtools

import (
	"fmt"
	"strings"

	"dynamic-runtime/extensions/pluginmeta"
)

// Severity is the seriousness of a Diagnostic.
type Severity string

const (
	SeverityError Severity = "error"
	SeverityWarn  Severity = "warn"
	SeverityInfo  Severity = "info"
)

// Code is a stable machine-readable diagnostic identifier.
type Code string

const (
	CodeMissingID              Code = "MISSING_ID"
	CodeMissingName            Code = "MISSING_NAME"
	CodeMissingVersion         Code = "MISSING_VERSION"
	CodeInvalidVersion         Code = "INVALID_VERSION"
	CodeInvalidCompatibility   Code = "INVALID_COMPATIBILITY"
	CodeCompatibilityMismatch  Code = "COMPATIBILITY_MISMATCH"
	CodeDuplicateCapabilityKey Code = "DUPLICATE_CAPABILITY_KEY"
	CodeMissingChecksum        Code = "MISSING_CHECKSUM"
	CodeUnsupportedBackend     Code = "UNSUPPORTED_BACKEND"
	CodeEmptySourceLocation    Code = "EMPTY_SOURCE_LOCATION"
)

// Diagnostic is one finding about a manifest.
type Diagnostic struct {
	Severity Severity
	Code     Code
	Message  string
}

// String renders a diagnostic in a stable, human-readable form.
func (d Diagnostic) String() string {
	return fmt.Sprintf("[%s] %s: %s", d.Severity, d.Code, d.Message)
}

// Diagnose inspects m and returns all findings in a deterministic order.
//
// It validates the semantic version and compatibility CONSTRAINT SHAPE through
// pluginmeta.Validate. No host versions are supplied, so compatibility is not
// evaluated against a concrete runtime/API version; use DiagnoseAgainst for
// that. Diagnose never mutates m and has no lifecycle side effects.
func Diagnose(m pluginmeta.Manifest) []Diagnostic {
	return diagnose(m, "", "")
}

// DiagnoseAgainst behaves like Diagnose and additionally evaluates the
// manifest's compatibility constraints against the supplied host versions via
// pluginmeta.ValidateAgainst. An empty runtimeVersion disables evaluation
// (ValidateAgainst requires a concrete runtime version); an empty apiVersion
// leaves the API constraint unconstrained, matching pluginmeta's contract.
func DiagnoseAgainst(m pluginmeta.Manifest, runtimeVersion, apiVersion string) []Diagnostic {
	return diagnose(m, runtimeVersion, apiVersion)
}

func diagnose(m pluginmeta.Manifest, runtimeVersion, apiVersion string) []Diagnostic {
	var out []Diagnostic
	add := func(sev Severity, code Code, message string) {
		out = append(out, Diagnostic{Severity: sev, Code: code, Message: message})
	}

	// Identity fields. A missing version is reported as such; a malformed one
	// is delegated to pluginmeta.Validate via a focused probe.
	if strings.TrimSpace(m.ID) == "" {
		add(SeverityError, CodeMissingID, "id is required")
	}
	if strings.TrimSpace(m.Name) == "" {
		add(SeverityError, CodeMissingName, "name is required")
	}
	if strings.TrimSpace(m.Version) == "" {
		add(SeverityError, CodeMissingVersion, "version is required")
	} else if err := pluginmeta.Validate(pluginmeta.Manifest{ID: "probe", Name: "probe", Version: m.Version}); err != nil {
		add(SeverityError, CodeInvalidVersion, strings.TrimPrefix(err.Error(), "pluginmeta: "))
	}

	// Compatibility: check the constraint shape, then (when host versions are
	// provided) evaluate them. The probe isolates compatibility from the other
	// manifest fields so every finding is attributed to the right code.
	compat := pluginmeta.Manifest{
		ID:            "probe",
		Name:          "probe",
		Version:       "0.0.1",
		Compatibility: m.Compatibility,
	}
	if err := pluginmeta.Validate(compat); err != nil {
		add(SeverityError, CodeInvalidCompatibility, strings.TrimPrefix(err.Error(), "pluginmeta: "))
	} else if runtimeVersion != "" {
		if err := pluginmeta.ValidateAgainst(compat, runtimeVersion, apiVersion); err != nil {
			add(SeverityError, CodeCompatibilityMismatch, strings.TrimPrefix(err.Error(), "pluginmeta: "))
		}
	}

	// One key may be declared at most once across requires and provides.
	seen := make(map[string]struct{}, len(m.Requires)+len(m.Provides))
	check := func(key string) {
		if key == "" {
			return // pluginmeta.Validate rejects empty keys; not our concern here
		}
		if _, dup := seen[key]; dup {
			add(SeverityError, CodeDuplicateCapabilityKey, fmt.Sprintf("capability key %q is declared more than once", key))
			return
		}
		seen[key] = struct{}{}
	}
	for _, r := range m.Requires {
		check(r.Key)
	}
	for _, p := range m.Provides {
		check(p.Key)
	}

	// Backend must be one of the well-known loading paths.
	switch m.Backend {
	case pluginmeta.BackendNative, pluginmeta.BackendWASM, pluginmeta.BackendProc:
	default:
		add(SeverityError, CodeUnsupportedBackend, fmt.Sprintf("unsupported backend %q (want native, wasm or proc)", m.Backend))
	}

	// Non-native artifacts must carry an integrity checksum.
	if m.Backend != pluginmeta.BackendNative && m.Backend != "" && strings.TrimSpace(m.Source.Checksum) == "" {
		add(SeverityWarn, CodeMissingChecksum, fmt.Sprintf("backend %q requires a sha256 source.checksum", m.Backend))
	}

	// An empty source location is suspicious for any backend.
	if strings.TrimSpace(m.Source.Location) == "" {
		add(SeverityWarn, CodeEmptySourceLocation, "source.location is empty")
	}

	return out
}
