package devtools_test

import (
	"context"
	"reflect"
	"testing"
	"time"

	"dynamic-runtime/extensions/devtools"
	"dynamic-runtime/extensions/pluginmeta"
	"dynamic-runtime/runtime"
)

const checksum = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"

func nativeManifest() pluginmeta.Manifest {
	return pluginmeta.Manifest{
		ID:      "camera",
		Name:    "Camera Plugin",
		Version: "1.2.3",
		Backend: pluginmeta.BackendNative,
		Compatibility: pluginmeta.Compatibility{
			Runtime: ">=1.0.0",
			API:     "<2.0.0",
		},
		Provides: []pluginmeta.Descriptor{{Key: "camera.frame", Description: "frame stream"}},
		Source:   pluginmeta.Source{Kind: "builtin", Location: "builtin://camera", Checksum: checksum},
	}
}

func codes(diags []devtools.Diagnostic) []devtools.Code {
	out := make([]devtools.Code, 0, len(diags))
	for _, d := range diags {
		out = append(out, d.Code)
	}
	return out
}

func hasCode(diags []devtools.Diagnostic, code devtools.Code) bool {
	for _, d := range diags {
		if d.Code == code {
			return true
		}
	}
	return false
}

func severityOf(diags []devtools.Diagnostic, code devtools.Code) (devtools.Severity, bool) {
	for _, d := range diags {
		if d.Code == code {
			return d.Severity, true
		}
	}
	return "", false
}

func TestDiagnoseValidManifest(t *testing.T) {
	if got := devtools.Diagnose(nativeManifest()); len(got) != 0 {
		t.Fatalf("valid manifest produced diagnostics: %v", got)
	}
}

func TestDiagnoseMissingIdentityFields(t *testing.T) {
	m := pluginmeta.Manifest{Backend: pluginmeta.BackendNative}
	got := devtools.Diagnose(m)
	for _, code := range []devtools.Code{devtools.CodeMissingID, devtools.CodeMissingName, devtools.CodeMissingVersion} {
		if !hasCode(got, code) {
			t.Errorf("missing diagnostic %s in %v", code, codes(got))
		}
		if sev, ok := severityOf(got, code); !ok || sev != devtools.SeverityError {
			t.Errorf("%s severity = %q, want error", code, sev)
		}
	}
}

func TestDiagnoseInvalidVersion(t *testing.T) {
	m := nativeManifest()
	m.Version = "not-a-version"
	got := devtools.Diagnose(m)
	if !hasCode(got, devtools.CodeInvalidVersion) {
		t.Fatalf("invalid version not reported: %v", codes(got))
	}
}

func TestDiagnoseInvalidCompatibility(t *testing.T) {
	m := nativeManifest()
	m.Compatibility.Runtime = ">>not-a-constraint"
	got := devtools.Diagnose(m)
	if !hasCode(got, devtools.CodeInvalidCompatibility) {
		t.Fatalf("invalid compatibility constraint not reported: %v", codes(got))
	}

	m = nativeManifest()
	m.Compatibility.API = "bogus(1)"
	got = devtools.Diagnose(m)
	if !hasCode(got, devtools.CodeInvalidCompatibility) {
		t.Fatalf("invalid api constraint not reported: %v", codes(got))
	}
}

func TestDiagnoseAgainstCompatibilityMismatch(t *testing.T) {
	m := nativeManifest()
	// Runtime >=1.0.0 does not admit 0.9.0; API <2.0.0 does not admit 2.1.0.
	got := devtools.DiagnoseAgainst(m, "0.9.0", "2.1.0")
	if !hasCode(got, devtools.CodeCompatibilityMismatch) {
		t.Fatalf("compatibility mismatch not reported: %v", codes(got))
	}
	if sev, ok := severityOf(got, devtools.CodeCompatibilityMismatch); !ok || sev != devtools.SeverityError {
		t.Fatalf("mismatch severity = %q, want error", sev)
	}

	// A matching host produces no mismatch diagnostic.
	got = devtools.DiagnoseAgainst(m, "1.5.0", "1.9.0")
	if hasCode(got, devtools.CodeCompatibilityMismatch) {
		t.Fatalf("matching versions produced a mismatch: %v", got)
	}
}

func TestDiagnoseDuplicateCapabilityKeys(t *testing.T) {
	m := nativeManifest()
	m.Requires = []pluginmeta.Requirement{{Key: "fs"}, {Key: "fs"}}
	m.Provides = []pluginmeta.Descriptor{{Key: "camera.frame"}, {Key: "fs"}}
	got := devtools.Diagnose(m)
	if !hasCode(got, devtools.CodeDuplicateCapabilityKey) {
		t.Fatalf("duplicate capability key not reported: %v", codes(got))
	}
	count := 0
	for _, d := range got {
		if d.Code == devtools.CodeDuplicateCapabilityKey {
			count++
		}
	}
	if count < 2 {
		t.Fatalf("want at least two duplicate-key diagnostics, got %d: %v", count, got)
	}
}

func TestDiagnoseMissingChecksum(t *testing.T) {
	for _, backend := range []pluginmeta.Backend{pluginmeta.BackendWASM, pluginmeta.BackendProc} {
		m := nativeManifest()
		m.Backend = backend
		m.Source = pluginmeta.Source{Kind: string(backend), Location: "https://example.com/plugin"}
		got := devtools.Diagnose(m)
		if !hasCode(got, devtools.CodeMissingChecksum) {
			t.Errorf("%s backend without checksum not reported: %v", backend, codes(got))
		}
	}

	// Native backends do not need a checksum.
	m := nativeManifest()
	m.Source = pluginmeta.Source{Kind: "builtin", Location: "builtin://camera"}
	if hasCode(devtools.Diagnose(m), devtools.CodeMissingChecksum) {
		t.Fatal("native backend without checksum was reported")
	}
}

func TestDiagnoseUnsupportedBackend(t *testing.T) {
	m := nativeManifest()
	m.Backend = pluginmeta.Backend("magic")
	got := devtools.Diagnose(m)
	if !hasCode(got, devtools.CodeUnsupportedBackend) {
		t.Fatalf("unsupported backend not reported: %v", codes(got))
	}
	if sev, ok := severityOf(got, devtools.CodeUnsupportedBackend); !ok || sev != devtools.SeverityError {
		t.Fatalf("unsupported backend severity = %q, want error", sev)
	}
}

func TestDiagnoseEmptySourceLocation(t *testing.T) {
	m := nativeManifest()
	m.Source = pluginmeta.Source{Kind: "builtin", Location: "   ", Checksum: checksum}
	got := devtools.Diagnose(m)
	if !hasCode(got, devtools.CodeEmptySourceLocation) {
		t.Fatalf("empty source location not reported: %v", codes(got))
	}
	if sev, ok := severityOf(got, devtools.CodeEmptySourceLocation); !ok || sev != devtools.SeverityWarn {
		t.Fatalf("empty source location severity = %q, want warn", sev)
	}
}

func TestDiagnoseIsPureAndDeterministic(t *testing.T) {
	m := nativeManifest()
	before := nativeManifest()
	first := devtools.Diagnose(m)
	second := devtools.Diagnose(m)
	if !reflect.DeepEqual(first, second) {
		t.Fatalf("Diagnose is not deterministic: %v vs %v", first, second)
	}
	if !reflect.DeepEqual(m, before) {
		t.Fatalf("Diagnose mutated its input: %+v", m)
	}
}

type consumer struct {
	result chan []devtools.Diagnostic
}

func (c *consumer) Name() string { return "devtools-consumer" }
func (c *consumer) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(devtools.ServiceKey)}
}
func (c *consumer) Provide() []runtime.Capability { return nil }
func (c *consumer) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	svc, err := runtime.Require(ctx, devtools.ServiceKey)
	if err != nil {
		return nil, err
	}
	c.result <- svc.Diagnose(pluginmeta.Manifest{Backend: pluginmeta.BackendNative})
	return nil, nil
}

func TestServiceRegistrationLifecycle(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	defer func() {
		if err := rt.Close(ctx); err != nil {
			t.Errorf("close runtime: %v", err)
		}
	}()

	comp := &devtools.Component{RuntimeVersion: "1.0.0", APIVersion: "1.0.0"}
	f, err := rt.Load(comp)
	if err != nil {
		t.Fatal(err)
	}
	if err := f.Ready(ctx); err != nil {
		t.Fatal(err)
	}

	snap, err := rt.Snapshot(ctx)
	if err != nil {
		t.Fatal(err)
	}
	if len(snap.Providers) != 1 {
		t.Fatalf("want one provider registered, got %d: %+v", len(snap.Providers), snap.Providers)
	}

	c := &consumer{result: make(chan []devtools.Diagnostic, 1)}
	cf, err := rt.Load(c)
	if err != nil {
		t.Fatal(err)
	}
	if err := cf.Ready(ctx); err != nil {
		t.Fatal(err)
	}
	select {
	case diags := <-c.result:
		if !hasCode(diags, devtools.CodeMissingID) {
			t.Fatalf("provided service did not diagnose: %v", codes(diags))
		}
	case <-ctx.Done():
		t.Fatal("timed out waiting for provided service result")
	}

	// Effect-owned registration: disposing the component removes the provider.
	if err := f.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := f.Gone(ctx); err != nil {
		t.Fatal(err)
	}
	snap, err = rt.Snapshot(ctx)
	if err != nil {
		t.Fatal(err)
	}
	if len(snap.Providers) != 0 {
		t.Fatalf("provider leaked after dispose: %+v", snap.Providers)
	}
}
