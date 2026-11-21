// Package pluginmeta defines the unified plugin manifest contract: a single,
// descriptive shape for metadata produced by the native, WASM and proc-plugin
// ecosystems.
//
// IMPORTANT — a Manifest is descriptive metadata only. It does NOT create a
// dependency system and does NOT drive lifecycle: it never calls Runtime.Load,
// never registers a capability and never installs an effect. Load/Dispose,
// reconciliation and fiber ownership remain exclusively with the Runtime,
// Loader and Config extensions (AGENTS.md §1: declared authority, no startup
// orchestration, single lifecycle authority). Requires/Provides and
// CapabilityKeys are documentation of intent; the real dependency graph is
// still Component.Inject/Provide resolved through typed Key[T]. Adapters in
// ./adapters merely project loader.Artifact, config.ComponentConfig and
// procplugin.PluginManifest metadata into this common descriptor.
//
// CHECKSUM CONTRACT — one rule, two independent fields:
//
//   - source.checksum is the SHA-256 digest of the SINGLE artifact located at
//     source.location. Consumers (e.g. extensions/market) resolve that location
//     to one file and verify that file's digest against this field.
//   - package.checksum is the deterministic, aggregate SHA-256 over the whole
//     set of artifacts packaged together with the manifest. It identifies the
//     packaged artifact set as a unit.
//
// The fields have different scopes and must never be substituted for one
// another: a tool that writes them (e.g. `plugin package`) computes each from
// its own definition, and a verifier of one field never reads the other.
package pluginmeta

import (
	"bytes"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"strings"

	semver "github.com/Masterminds/semver/v3"
	toml "github.com/pelletier/go-toml/v2"
	modsemver "golang.org/x/mod/semver"
)

// Backend identifies the loading path a plugin uses. It is descriptive
// metadata; the canonical loader-side notion remains loader.BackendType.
type Backend string

// Well-known backend values for a manifest.
const (
	BackendNative Backend = "native"
	BackendWASM   Backend = "wasm"
	BackendProc   Backend = "proc"
)

// Requirement declares a capability key a plugin expects to consume. Optional
// records whether the requirement is advisory. This is a description, not a
// runtime.Requires — the Runtime never reconciles against these entries.
type Requirement struct {
	Key      string `json:"key" toml:"key"`
	Optional bool   `json:"optional" toml:"optional"`
}

// Descriptor declares a capability key a plugin offers, with human-readable
// detail. It is not registered with the Runtime.
type Descriptor struct {
	Key         string `json:"key" toml:"key"`
	Description string `json:"description" toml:"description"`
}

// Compatibility holds semver CONSTRAINT expressions (e.g. ">=1.2.0 <2.0.0"),
// not concrete versions. Runtime constrains the host runtime version; API
// constrains the plugin API version. An empty expression means unconstrained.
type Compatibility struct {
	Runtime string `json:"runtime" toml:"runtime"`
	API     string `json:"api" toml:"api"`
}

// Source records where a plugin implementation came from. Checksum, when set,
// is a lowercase-or-uppercase hex encoded SHA-256 digest (64 hex characters)
// of the SINGLE artifact at Location (see the checksum contract above).
// Integrity checking itself is the Loader's responsibility.
type Source struct {
	Kind     string `json:"kind" toml:"kind"`
	Location string `json:"location" toml:"location"`
	Checksum string `json:"checksum" toml:"checksum"`
}

// PackageInfo carries aggregate information about a packaged plugin release.
// Checksum, when set, is a lowercase-or-uppercase hex encoded SHA-256 digest
// (64 hex characters) over ALL artifacts in the package — deliberately NOT the
// digest of the single artifact at Source.Location (see the checksum contract
// above). It is additive: absent packages leave this field nil.
type PackageInfo struct {
	Checksum string `json:"checksum" toml:"checksum"`
}

// Manifest is the unified, descriptive plugin descriptor. It is intentionally
// independent of runtime.Component, runtime.Dependency and config.ComponentConfig:
// lifecycle stays with Runtime/Loader/Config.
type Manifest struct {
	ID            string         `json:"id" toml:"id"`
	Name          string         `json:"name" toml:"name"`
	Version       string         `json:"version" toml:"version"`
	Description   string         `json:"description" toml:"description"`
	Author        string         `json:"author" toml:"author"`
	License       string         `json:"license" toml:"license"`
	Repository    string         `json:"repository" toml:"repository"`
	Backend       Backend        `json:"backend" toml:"backend"`
	ModuleType    string         `json:"module_type" toml:"module_type"`
	Requires      []Requirement  `json:"requires" toml:"requires"`
	Provides      []Descriptor   `json:"provides" toml:"provides"`
	Config        map[string]any `json:"config" toml:"config"`
	Compatibility Compatibility  `json:"compatibility" toml:"compatibility"`
	Source        Source         `json:"source" toml:"source"`
	Package       *PackageInfo   `json:"package,omitempty" toml:"package,omitempty"`
}

// Validate checks the intrinsic well-formedness of m: required identity
// fields, a valid semantic version, well-formed (but not yet evaluated)
// compatibility constraints, duplicate/empty capability keys and the shape of
// both checksums (source.checksum and package.checksum). It does not compare
// against a host runtime version; use ValidateAgainst for that.
func Validate(m Manifest) error {
	if strings.TrimSpace(m.ID) == "" {
		return errors.New("pluginmeta: id is required")
	}
	if strings.TrimSpace(m.Name) == "" {
		return errors.New("pluginmeta: name is required")
	}
	if strings.TrimSpace(m.Version) == "" {
		return errors.New("pluginmeta: version is required")
	}
	if err := validateVersion(m.Version); err != nil {
		return fmt.Errorf("pluginmeta: version %q: %w", m.Version, err)
	}
	if err := validateConstraint(m.Compatibility.Runtime); err != nil {
		return fmt.Errorf("pluginmeta: compatibility.runtime: %w", err)
	}
	if err := validateConstraint(m.Compatibility.API); err != nil {
		return fmt.Errorf("pluginmeta: compatibility.api: %w", err)
	}
	if err := validateKeys(m.Requires, "requires", func(r Requirement) string { return r.Key }); err != nil {
		return err
	}
	if err := validateKeys(m.Provides, "provides", func(d Descriptor) string { return d.Key }); err != nil {
		return err
	}
	if err := validateChecksum("source.checksum", m.Source.Checksum); err != nil {
		return err
	}
	if m.Package != nil {
		if err := validateChecksum("package.checksum", m.Package.Checksum); err != nil {
			return err
		}
	}
	return nil
}

func validateChecksum(field, value string) error {
	if value == "" {
		return nil
	}
	raw, err := hex.DecodeString(value)
	if err != nil || len(raw) != sha256.Size {
		return fmt.Errorf("pluginmeta: %s must be 64 hex characters (sha256)", field)
	}
	return nil
}

// ValidateAgainst runs Validate and then evaluates Compatibility.Runtime
// against runtimeVersion and Compatibility.API against apiVersion (both accept
// bare or v-prefixed semver). The two versions are independent: an API
// constraint must never be checked against the host runtime version. An empty
// constraint imposes no restriction; an empty apiVersion means the API field
// is unconstrained (even when the manifest declares an API constraint).
func ValidateAgainst(m Manifest, runtimeVersion, apiVersion string) error {
	if err := Validate(m); err != nil {
		return err
	}
	rv, err := semver.NewVersion(normalizeVersion(runtimeVersion))
	if err != nil {
		return fmt.Errorf("pluginmeta: invalid runtime version %q: %w", runtimeVersion, err)
	}
	if err := checkConstraint("compatibility.runtime", m.Compatibility.Runtime, rv, runtimeVersion); err != nil {
		return err
	}
	if apiVersion == "" {
		return nil
	}
	av, err := semver.NewVersion(normalizeVersion(apiVersion))
	if err != nil {
		return fmt.Errorf("pluginmeta: invalid api version %q: %w", apiVersion, err)
	}
	return checkConstraint("compatibility.api", m.Compatibility.API, av, apiVersion)
}

func checkConstraint(name, expr string, v *semver.Version, version string) error {
	if expr == "" {
		return nil
	}
	c, err := semver.NewConstraint(expr)
	if err != nil {
		return fmt.Errorf("pluginmeta: %s: %w", name, err)
	}
	if ok, errs := c.Validate(v); !ok {
		return fmt.Errorf("pluginmeta: version %q does not satisfy %s constraint %q: %v",
			version, name, expr, errs)
	}
	return nil
}

// Parse decodes a Manifest from JSON or TOML. Unknown fields are rejected in
// both formats (json.Decoder.DisallowUnknownFields / toml.Decoder.DisallowUnknownFields),
// including inside nested objects such as requires, provides and source.
// Parse performs decoding only: call Validate or ValidateAgainst explicitly to
// enforce contract invariants.
func Parse(data []byte, format string) (Manifest, error) {
	var m Manifest
	switch strings.ToLower(strings.TrimSpace(format)) {
	case "json":
		dec := json.NewDecoder(bytes.NewReader(data))
		dec.DisallowUnknownFields()
		if err := dec.Decode(&m); err != nil {
			return Manifest{}, fmt.Errorf("pluginmeta: parse json: %w", err)
		}
		var extra any
		if err := dec.Decode(&extra); err != io.EOF {
			if err == nil {
				return Manifest{}, errors.New("pluginmeta: parse json: multiple JSON values")
			}
			return Manifest{}, fmt.Errorf("pluginmeta: parse json: %w", err)
		}
	case "toml":
		dec := toml.NewDecoder(bytes.NewReader(data))
		dec.DisallowUnknownFields()
		if err := dec.Decode(&m); err != nil {
			return Manifest{}, fmt.Errorf("pluginmeta: parse toml: %w", err)
		}
	default:
		return Manifest{}, fmt.Errorf("pluginmeta: unsupported format %q (want json or toml)", format)
	}
	return m, nil
}

// CapabilityKeys returns the declared requirement and provision key names in
// declaration order. This is a descriptive projection for tooling only: it
// does not register anything with the Runtime and creates no dependency edges.
func (m Manifest) CapabilityKeys() (requires, provides []string) {
	requires = make([]string, 0, len(m.Requires))
	provides = make([]string, 0, len(m.Provides))
	for _, r := range m.Requires {
		requires = append(requires, r.Key)
	}
	for _, d := range m.Provides {
		provides = append(provides, d.Key)
	}
	return requires, provides
}

// normalizeVersion accepts the bare "1.2.3" spelling in addition to the
// "v1.2.3" form required by golang.org/x/mod/semver.
func normalizeVersion(v string) string {
	v = strings.TrimSpace(v)
	if v != "" && v[0] >= '0' && v[0] <= '9' {
		return "v" + v
	}
	return v
}

func validateVersion(v string) error {
	if !modsemver.IsValid(normalizeVersion(v)) {
		return errors.New("not a valid semantic version")
	}
	return nil
}

func validateConstraint(expr string) error {
	if expr == "" {
		return nil
	}
	if _, err := semver.NewConstraint(expr); err != nil {
		return fmt.Errorf("invalid version constraint %q: %w", expr, err)
	}
	return nil
}

func validateKeys[T any](entries []T, field string, key func(T) string) error {
	seen := make(map[string]struct{}, len(entries))
	for _, entry := range entries {
		k := key(entry)
		if k == "" {
			return fmt.Errorf("pluginmeta: %s contains an empty key", field)
		}
		if _, dup := seen[k]; dup {
			return fmt.Errorf("pluginmeta: %s contains duplicate key %q", field, k)
		}
		seen[k] = struct{}{}
	}
	return nil
}
