package pluginmeta_test

import (
	"strings"
	"testing"

	"dynamic-runtime/extensions/pluginmeta"
)

func validManifest() pluginmeta.Manifest {
	return pluginmeta.Manifest{
		ID:      "camera",
		Name:    "Camera Plugin",
		Version: "1.2.3",
		Backend: pluginmeta.BackendNative,
		Requires: []pluginmeta.Requirement{
			{Key: "fs"},
			{Key: "logger", Optional: true},
		},
		Provides: []pluginmeta.Descriptor{
			{Key: "camera.frame", Description: "frame stream"},
		},
		Source: pluginmeta.Source{Kind: "builtin", Location: "builtin://camera"},
	}
}

func TestValidateRequiredFields(t *testing.T) {
	tests := []struct {
		name   string
		mutate func(*pluginmeta.Manifest)
	}{
		{"missing ID", func(m *pluginmeta.Manifest) { m.ID = "" }},
		{"missing Name", func(m *pluginmeta.Manifest) { m.Name = "" }},
		{"missing Version", func(m *pluginmeta.Manifest) { m.Version = "" }},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			m := validManifest()
			tt.mutate(&m)
			if err := pluginmeta.Validate(m); err == nil {
				t.Fatal("Validate accepted a manifest with a missing required field")
			}
		})
	}
	if err := pluginmeta.Validate(validManifest()); err != nil {
		t.Fatalf("Validate rejected a valid manifest: %v", err)
	}
}

func TestValidateVersionNormalization(t *testing.T) {
	for _, v := range []string{"1.2.3", "v1.2.3", "1.2.3-rc.1+build.5"} {
		m := validManifest()
		m.Version = v
		if err := pluginmeta.Validate(m); err != nil {
			t.Errorf("Validate rejected normalized version %q: %v", v, err)
		}
	}
	for _, v := range []string{"not-a-version", "v", "1.2.3.4"} {
		m := validManifest()
		m.Version = v
		if err := pluginmeta.Validate(m); err == nil {
			t.Errorf("Validate accepted invalid version %q", v)
		}
	}
}

func TestValidateDuplicateKeys(t *testing.T) {
	t.Run("requires", func(t *testing.T) {
		m := validManifest()
		m.Requires = []pluginmeta.Requirement{{Key: "dup"}, {Key: "dup", Optional: true}}
		if err := pluginmeta.Validate(m); err == nil {
			t.Fatal("Validate accepted duplicate Requires keys")
		}
	})
	t.Run("provides", func(t *testing.T) {
		m := validManifest()
		m.Provides = []pluginmeta.Descriptor{{Key: "dup"}, {Key: "dup", Description: "again"}}
		if err := pluginmeta.Validate(m); err == nil {
			t.Fatal("Validate accepted duplicate Provides keys")
		}
	})
	t.Run("empty key", func(t *testing.T) {
		m := validManifest()
		m.Requires = []pluginmeta.Requirement{{Key: ""}}
		if err := pluginmeta.Validate(m); err == nil {
			t.Fatal("Validate accepted an empty requirement key")
		}
	})
}

func TestValidateChecksum(t *testing.T) {
	for _, c := range []string{
		"",
		strings.Repeat("a", 64),
		strings.Repeat("A", 64),
	} {
		m := validManifest()
		m.Source.Checksum = c
		if err := pluginmeta.Validate(m); err != nil {
			t.Errorf("Validate rejected checksum %q: %v", c, err)
		}
	}
	for _, c := range []string{
		strings.Repeat("a", 63),
		strings.Repeat("a", 66),
		strings.Repeat("z", 64),
	} {
		m := validManifest()
		m.Source.Checksum = c
		if err := pluginmeta.Validate(m); err == nil {
			t.Errorf("Validate accepted invalid checksum %q", c)
		}
	}
}

func TestValidateAgainstConstraints(t *testing.T) {
	m := validManifest()
	// Deliberately disjoint ranges: a satisfying pair is impossible if both
	// constraints are checked against the same version.
	m.Compatibility = pluginmeta.Compatibility{Runtime: ">=1.2.0 <2.0.0", API: ">=2.0.0 <3.0.0"}

	if err := pluginmeta.Validate(m); err != nil {
		t.Fatalf("Validate rejected well-formed constraints: %v", err)
	}
	if err := pluginmeta.ValidateAgainst(m, "1.5.0", "2.1.0"); err != nil {
		t.Errorf("ValidateAgainst rejected satisfying distinct runtime/API versions: %v", err)
	}
	if err := pluginmeta.ValidateAgainst(m, "v1.5.0", "v2.1.0"); err != nil {
		t.Errorf("ValidateAgainst rejected v-prefixed versions: %v", err)
	}
	// Runtime failure only; the API version satisfies its constraint.
	if err := pluginmeta.ValidateAgainst(m, "2.0.0", "2.1.0"); err == nil {
		t.Error("ValidateAgainst accepted a runtime version outside Runtime constraint")
	}
	// API failure only; the runtime version satisfies its constraint.
	if err := pluginmeta.ValidateAgainst(m, "1.5.0", "1.9.0"); err == nil {
		t.Error("ValidateAgainst accepted an API version outside API constraint")
	}

	// An empty apiVersion leaves the API field unconstrained.
	if err := pluginmeta.ValidateAgainst(m, "1.5.0", ""); err != nil {
		t.Errorf("ValidateAgainst treated an empty apiVersion as a constraint: %v", err)
	}
	// Empty constraints and missing API version are unconditional.
	unconstrained := validManifest()
	if err := pluginmeta.ValidateAgainst(unconstrained, "9.9.9", "9.9.9"); err != nil {
		t.Errorf("ValidateAgainst rejected an unconstrained manifest: %v", err)
	}
	if err := pluginmeta.ValidateAgainst(unconstrained, "9.9.9", ""); err != nil {
		t.Errorf("ValidateAgainst rejected an unconstrained manifest without apiVersion: %v", err)
	}

	// A malformed constraint is rejected even without a runtime version.
	badConstraint := validManifest()
	badConstraint.Compatibility.Runtime = "not-a-constraint"
	if err := pluginmeta.Validate(badConstraint); err == nil {
		t.Error("Validate accepted an invalid constraint expression")
	}

	// The compared-to versions must themselves be valid semver.
	badRuntime := validManifest()
	badRuntime.Compatibility.Runtime = ">=1.0.0"
	if err := pluginmeta.ValidateAgainst(badRuntime, "garbage", "1.0.0"); err == nil {
		t.Error("ValidateAgainst accepted an invalid runtime version")
	}
	if err := pluginmeta.ValidateAgainst(m, "1.5.0", "garbage"); err == nil {
		t.Error("ValidateAgainst accepted an invalid api version")
	}
}

func TestCapabilityKeys(t *testing.T) {
	m := validManifest()
	requires, provides := m.CapabilityKeys()
	if len(requires) != 2 || requires[0] != "fs" || requires[1] != "logger" {
		t.Errorf("unexpected required keys: %v", requires)
	}
	if len(provides) != 1 || provides[0] != "camera.frame" {
		t.Errorf("unexpected provided keys: %v", provides)
	}
}

// The two checksums are independent declarations: source.checksum covers the
// single artifact at source.location, package.checksum covers the aggregate
// packaged artifacts. Both must be validated with the same sha256-hex shape.
func TestValidatePackageChecksum(t *testing.T) {
	valid := strings.Repeat("a", 64)
	other := strings.Repeat("b", 64)

	for _, tc := range []struct {
		name    string
		pkg     *pluginmeta.PackageInfo
		wantErr bool
	}{
		{"absent", nil, false},
		{"empty", &pluginmeta.PackageInfo{}, false},
		{"valid", &pluginmeta.PackageInfo{Checksum: valid}, false},
		{"uppercase", &pluginmeta.PackageInfo{Checksum: strings.Repeat("A", 64)}, false},
		{"too short", &pluginmeta.PackageInfo{Checksum: strings.Repeat("a", 63)}, true},
		{"too long", &pluginmeta.PackageInfo{Checksum: strings.Repeat("a", 66)}, true},
		{"non hex", &pluginmeta.PackageInfo{Checksum: strings.Repeat("z", 64)}, true},
	} {
		t.Run(tc.name, func(t *testing.T) {
			m := validManifest()
			m.Source.Checksum = valid
			m.Package = tc.pkg
			err := pluginmeta.Validate(m)
			if (err != nil) != tc.wantErr {
				t.Fatalf("Validate(package=%+v) error = %v, wantErr %v", tc.pkg, err, tc.wantErr)
			}
		})
	}

	// A valid package.checksum must not mask an invalid source.checksum.
	m := validManifest()
	m.Source.Checksum = strings.Repeat("z", 64)
	m.Package = &pluginmeta.PackageInfo{Checksum: other}
	if err := pluginmeta.Validate(m); err == nil {
		t.Fatal("Validate accepted an invalid source.checksum with a valid package.checksum")
	}
}
