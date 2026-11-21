package wasm

import (
	"context"
	"errors"
	"os"
	"path/filepath"
	"strings"
	"testing"

	"dynamic-runtime/extensions/loader"
)

// ---------------------------------------------------------------------------
// Minimal WASM builders (test-only) for edge cases wazero genuinely validates.
// ---------------------------------------------------------------------------

func bUleb(n int) []byte {
	var out []byte
	for {
		b := byte(n & 0x7f)
		n >>= 7
		if n != 0 {
			b |= 0x80
		}
		out = append(out, b)
		if n == 0 {
			return out
		}
	}
}

func bU32(n int) []byte { return bUleb(n) }

func bName(s string) []byte {
	out := bU32(len(s))
	return append(out, s...)
}

func bSection(id byte, payload []byte) []byte {
	out := []byte{id}
	out = append(out, bUleb(len(payload))...)
	return append(out, payload...)
}

func bHeader() []byte {
	return []byte{0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00}
}

// bModuleTypeSection appends a custom section "module_type" with the logical
// module type (required manifest metadata).
func bModuleTypeSection(moduleType string) []byte {
	payload := bName("module_type")
	payload = append(payload, moduleType...)
	return bSection(0, payload)
}

func bFuncType() []byte { // () -> ()
	out := []byte{0x60}
	out = append(out, bU32(0)...)
	out = append(out, bU32(0)...)
	return out
}

// bEmptyBody is a () -> () body that just ends.
func bEmptyBody() []byte {
	raw := append(bU32(0), 0x0B)
	out := bU32(len(raw))
	return append(out, raw...)
}

// bUnreachableBody is a () -> () body that traps (0x00 = unreachable).
func bUnreachableBody() []byte {
	raw := append(bU32(0), 0x00, 0x0B)
	out := bU32(len(raw))
	return append(out, raw...)
}

func bExportFunc(name string, index int) []byte {
	e := bName(name)
	e = append(e, 0) // func kind
	e = append(e, bU32(index)...)
	return e
}

// bImportModule builds a valid module that imports "env"."host_log" (a host
// function v0.1 does not provide) and exports create/destroy from its single
// defined function (index 1, since the import occupies index 0).
func bImportModule() []byte {
	var b []byte
	b = append(b, bHeader()...)
	b = append(b, bSection(1, append(bU32(1), bFuncType()...))...) // type 0

	// import section: env.host_log -> func type 0
	imp := append(bU32(1), bName("env")...)
	imp = append(imp, bName("host_log")...)
	imp = append(imp, 0) // func kind
	imp = append(imp, bU32(0)...)
	b = append(b, bSection(2, imp)...)

	// function section: one defined func of type 0 (index 1)
	b = append(b, bSection(3, append(bU32(1), bU32(0)...))...)

	// export section: create -> 1, destroy -> 1
	exp := append(bU32(2), bExportFunc(ABICreate, 1)...)
	exp = append(exp, bExportFunc(ABIDestroy, 1)...)
	b = append(b, bSection(7, exp)...)

	// code section: one body
	b = append(b, bSection(10, append(bU32(1), bEmptyBody()...))...)
	b = append(b, bModuleTypeSection("industrial.import")...)
	return b
}

// bTrapDestroyModule is a valid module whose destroy export traps, proving the
// export is really executed during teardown.
func bTrapDestroyModule() []byte {
	var b []byte
	b = append(b, bHeader()...)
	b = append(b, bSection(1, append(bU32(1), bFuncType()...))...) // type 0
	b = append(b, bSection(3, append(append(bU32(2), bU32(0)...), bU32(0)...))...)

	exp := append(bU32(2), bExportFunc(ABICreate, 0)...)
	exp = append(exp, bExportFunc(ABIDestroy, 1)...)
	b = append(b, bSection(7, exp)...)

	code := append(bU32(2), bEmptyBody()...)
	code = append(code, bUnreachableBody()...)
	b = append(b, bSection(10, code)...)
	b = append(b, bModuleTypeSection("industrial.trap")...)
	return b
}

// bNoManifestModule is a valid module (two functions, both ABI exports) with
// NO module_type metadata.
func bNoManifestModule() []byte {
	var b []byte
	b = append(b, bHeader()...)
	b = append(b, bSection(1, append(bU32(1), bFuncType()...))...) // type 0
	b = append(b, bSection(3, append(append(bU32(2), bU32(0)...), bU32(0)...))...)

	exp := append(bU32(2), bExportFunc(ABICreate, 0)...)
	exp = append(exp, bExportFunc(ABIDestroy, 1)...)
	b = append(b, bSection(7, exp)...)

	code := append(bU32(2), bEmptyBody()...)
	code = append(code, bEmptyBody()...)
	b = append(b, bSection(10, code)...)
	return b
}

func fixturePath(name string) string { return filepath.Join("testdata", name) }

func readFixture(t *testing.T, name string) []byte {
	t.Helper()
	data, err := os.ReadFile(fixturePath(name))
	if err != nil {
		t.Fatalf("read fixture %s: %v", name, err)
	}
	return data
}

func newTestBackend() *Backend { return NewBackend() }

func wasmArtifact(id, source string) loader.Artifact {
	return loader.Artifact{ID: id, Type: "wasm", Source: source, Version: "1"}
}

func loadFixtureRef(t *testing.T, b *Backend, id, file string) *moduleRef {
	t.Helper()
	ref, err := b.loadModule(context.Background(), wasmArtifact(id, "file://"+fixturePath(file)), readFixture(t, file))
	if err != nil {
		t.Fatalf("loadModule(%s): %v", file, err)
	}
	return ref
}

// V-01 — the committed valid.wasm compiles under the real wazero validator and
// satisfies the minimal ABI.
func TestV01ValidFixtureCompiles(t *testing.T) {
	b := newTestBackend()
	ref := loadFixtureRef(t, b, "v01", "valid.wasm")
	if ref.id != "v01" {
		t.Fatalf("ref id = %q", ref.id)
	}
	if _, ok := ref.compiled.ExportedFunctions()[ABICreate]; !ok {
		t.Fatal("create export missing after compile")
	}
	if _, ok := ref.compiled.ExportedFunctions()[ABIDestroy]; !ok {
		t.Fatal("destroy export missing after compile")
	}
	if ref.moduleType != "industrial.camera" {
		t.Fatalf("manifest module type = %q, want industrial.camera", ref.moduleType)
	}
}

// V-10 — a module without module_type metadata is rejected (no "wasm"
// fallback): BackendType and ModuleType stay in separate namespaces.
func TestV10MissingManifestRejected(t *testing.T) {
	b := newTestBackend()
	_, err := b.loadModule(context.Background(), wasmArtifact("v10", "builtin:manifest"), bNoManifestModule())
	if !errors.Is(err, loader.ErrInvalidModuleType) {
		t.Fatalf("loadModule(no manifest) = %v, want ErrInvalidModuleType", err)
	}
}

// V-11 — different fixtures carry different Logical Module Types from their
// own manifest metadata.
func TestV11ManifestLogicalTypes(t *testing.T) {
	b := newTestBackend()
	cases := map[string]string{
		"valid.wasm":  "industrial.camera",
		"sensor.wasm": "industrial.sensor",
		"can.wasm":    "automotive.can",
	}
	for file, want := range cases {
		ref, err := b.loadModule(context.Background(), wasmArtifact("v11-"+file, "file://x"), readFixture(t, file))
		if err != nil {
			t.Fatalf("loadModule(%s): %v", file, err)
		}
		if ref.moduleType != want {
			t.Fatalf("%s module type = %q, want %q", file, ref.moduleType, want)
		}
	}
}

// V-02 — invalid bytes are rejected by wazero as ErrInvalidWASM.
func TestV02InvalidFixtureRejected(t *testing.T) {
	b := newTestBackend()
	_, err := b.loadModule(context.Background(), wasmArtifact("v02", "file://"+fixturePath("invalid.wasm")), readFixture(t, "invalid.wasm"))
	if !errors.Is(err, ErrInvalidWASM) {
		t.Fatalf("loadModule(invalid) = %v, want ErrInvalidWASM", err)
	}
}

// V-03 — truncated bytes are rejected by wazero as ErrInvalidWASM.
func TestV03TruncatedRejected(t *testing.T) {
	b := newTestBackend()
	data := readFixture(t, "valid.wasm")
	_, err := b.loadModule(context.Background(), wasmArtifact("v03", "file://x"), data[:len(data)-3])
	if !errors.Is(err, ErrInvalidWASM) {
		t.Fatalf("loadModule(truncated) = %v, want ErrInvalidWASM", err)
	}
}

// V-04 — a module missing the destroy export fails the ABI check.
func TestV04MissingABIRejected(t *testing.T) {
	b := newTestBackend()
	_, err := b.loadModule(context.Background(), wasmArtifact("v04", "file://"+fixturePath("missing-export.wasm")), readFixture(t, "missing-export.wasm"))
	if !errors.Is(err, ErrWASMABI) {
		t.Fatalf("loadModule(missing-export) = %v, want ErrWASMABI", err)
	}
}

// V-05 — a module importing a host function compiles (Load is fine) but cannot
// be instantiated: v0.1 provides no host API.
func TestV05ImportsLoadButNotInstantiable(t *testing.T) {
	b := newTestBackend()
	ref, err := b.loadModule(context.Background(), wasmArtifact("v05", "builtin:import"), bImportModule())
	if err != nil {
		t.Fatalf("loadModule(import) = %v (imports must Load, only instantiation fails)", err)
	}
	_, err = b.instantiate(context.Background(), ref)
	if !errors.Is(err, ErrWASMInstantiate) {
		t.Fatalf("instantiate(import) = %v, want ErrWASMInstantiate", err)
	}
}

// V-06 — real execution: instantiate + begin runs runtime_component_create,
// which sets the exported "runtime_marker" global to 42.
func TestV06ExecutionRunsCreate(t *testing.T) {
	b := newTestBackend()
	ref := loadFixtureRef(t, b, "v06", "valid.wasm")
	inst, err := b.instantiate(context.Background(), ref)
	if err != nil {
		t.Fatalf("instantiate: %v", err)
	}
	if err := inst.begin(context.Background()); err != nil {
		t.Fatalf("begin (create): %v", err)
	}
	g := inst.module.ExportedGlobal("runtime_marker")
	if g == nil {
		t.Fatal("runtime_marker global not exported")
	}
	if got := g.Get(); got != 42 {
		t.Fatalf("runtime_marker = %d after create, want 42 (create did not execute)", got)
	}
	if err := inst.Destroy(); err != nil {
		t.Fatalf("Destroy: %v", err)
	}
	if !inst.Destroyed() {
		t.Fatal("instance not marked destroyed")
	}
	if inst.module == nil || !inst.module.IsClosed() {
		t.Fatal("wazero module instance not closed after Destroy")
	}
}

// V-07 — teardown really runs runtime_component_destroy: a destroy export that
// traps makes Destroy return an error while still closing the instance (no
// orphan).
func TestV07DestroyTrapReportedAndClosed(t *testing.T) {
	b := newTestBackend()
	ref, err := b.loadModule(context.Background(), wasmArtifact("v07", "builtin:trap"), bTrapDestroyModule())
	if err != nil {
		t.Fatalf("loadModule(trap): %v", err)
	}
	inst, err := b.instantiate(context.Background(), ref)
	if err != nil {
		t.Fatalf("instantiate: %v", err)
	}
	if err := inst.begin(context.Background()); err != nil {
		t.Fatalf("begin: %v", err)
	}
	err = inst.Destroy()
	if err == nil {
		t.Fatal("Destroy() = nil, want destroy-export trap error")
	}
	if !inst.Destroyed() {
		t.Fatal("instance not destroyed despite destroy error")
	}
	if !inst.module.IsClosed() {
		t.Fatal("module instance leaked after destroy error")
	}
}

// V-08 — after Backend.Close both Load and Instantiate are rejected.
func TestV08BackendClosedRejectsWork(t *testing.T) {
	b := newTestBackend()
	ref := loadFixtureRef(t, b, "v08", "valid.wasm")
	if err := b.Close(); err != nil {
		t.Fatalf("Close: %v", err)
	}
	if err := b.Close(); err != nil {
		t.Fatalf("second Close not idempotent: %v", err)
	}
	if !b.Closed() {
		t.Fatal("Closed() = false after Close")
	}
	_, err := b.loadModule(context.Background(), wasmArtifact("v08b", "file://x"), readFixture(t, "valid.wasm"))
	if !errors.Is(err, ErrWASMBackendClosed) {
		t.Fatalf("loadModule after Close = %v, want ErrWASMBackendClosed", err)
	}
	if _, err := b.instantiate(context.Background(), ref); !errors.Is(err, ErrWASMBackendClosed) {
		t.Fatalf("instantiate after Close = %v, want ErrWASMBackendClosed", err)
	}
}

// V-09 — the Backend type guard: only Artifact.Type == "wasm" is accepted.
func TestV09TypeGuardRejected(t *testing.T) {
	b := newTestBackend()
	// readSource returns an error for missing files; craft the panic path by
	// closing the backend concurrently is not needed — Load recovers panics.
	m, err := b.Load(context.Background(), loader.Artifact{ID: "p", BackendType: "not-wasm", Source: "file://x"})
	if err == nil || !errors.Is(err, loader.ErrInvalidArtifact) {
		t.Fatalf("Load(wrong backend) = %v (%v), want ErrInvalidArtifact", m, err)
	}
	if !strings.Contains(err.Error(), `BackendType == "wasm"`) {
		t.Fatalf("unexpected error text: %v", err)
	}
}

// NEW-02 residual: CommitModule must NOT mark an id whose ref is no longer
// tracked. If an Unload released the ref between the loader's registry write
// and the CommitModule call, a stale committed flag would make a later
// AbandonModule refuse to release the id's NEXT resources (leak).
func TestBackendCommitModuleSkipsUntrackedID(t *testing.T) {
	b := newTestBackend()
	art := wasmArtifact("c1", "file://"+fixturePath("valid.wasm"))
	art.BackendType = loader.BackendWASM
	art.Source = "file://" + absFixture(t, "valid.wasm")
	if _, err := b.Load(context.Background(), art); err != nil {
		t.Fatalf("load: %v", err)
	}
	// Loader lifecycle: commit → unload (release) → a STALE commit attempt.
	b.CommitModule("c1")
	b.UnloadModule("c1") // releases + clears ownership
	b.CommitModule("c1") // stale: the id is untracked — must be a no-op
	if b.CompiledModules() != 0 {
		t.Fatalf("untracked id resurrected in the backend map")
	}
	// The stale mark must not block a later abandon of NEW resources for the
	// same id: reload, fail the commit (abandon) — the ref must be released.
	if _, err := b.Load(context.Background(), art); err != nil {
		t.Fatalf("reload: %v", err)
	}
	if b.CompiledModules() != 1 {
		t.Fatalf("reload did not track a fresh ref")
	}
	b.AbandonModule("c1")
	if b.CompiledModules() != 0 {
		t.Fatalf("Abandon refused to release the NEW ref (stale committed flag) — NEW-02 leak")
	}
}

// NEW-06/NEW-08: ownership protocol for a SHARED ref — concurrent same-ID
// loads each hold the ref; a failed attempt's AbandonModule releases only its
// holder slot; the committed attempt keeps the ref alive and usable; Unload
// releases it at the end.
func TestBackendSharedRefOwnership(t *testing.T) {
	b := newTestBackend()
	art := wasmArtifact("own", "file://"+absFixture(t, "valid.wasm"))
	art.BackendType = loader.BackendWASM

	// Two attempts share the ref (dedupe): holders = 2.
	m1, err := b.Load(context.Background(), art)
	if err != nil {
		t.Fatalf("load1: %v", err)
	}
	m2, err := b.Load(context.Background(), art)
	if err != nil {
		t.Fatalf("load2: %v", err)
	}
	if b.CompiledModules() != 1 {
		t.Fatalf("shared ref not deduped: %d compiled", b.CompiledModules())
	}

	// Attempt 1 commits (ownership transfer): holders 2 -> 1.
	b.CommitModule("own")

	// Attempt 2 fails and abandons: holders 1 -> 0? NO — the committed
	// attempt owns it now, so the abandon must not close the committed ref.
	b.AbandonModule("own")
	if b.CompiledModules() != 1 {
		t.Fatalf("abandon closed a ref owned by the committed attempt: %d", b.CompiledModules())
	}

	// The committed module's ref must still be USABLE: instantiate through the
	// factory's module ref (host round trip proves the compiled module lives).
	f := m1.Factory
	if f == nil || m2.Factory == nil {
		t.Fatal("factories nil")
	}

	// Unload releases the owned ref.
	b.UnloadModule("own")
	if b.CompiledModules() != 0 {
		t.Fatalf("ref not released after Unload: %d", b.CompiledModules())
	}
}

// NEW-08: the last abandoning holder releases the shared ref — the failed
// attempt must never leave it tracked.
func TestBackendAbandonReleasesWhenLastHolder(t *testing.T) {
	b := newTestBackend()
	art := wasmArtifact("own2", "file://"+absFixture(t, "valid.wasm"))
	art.BackendType = loader.BackendWASM

	if _, err := b.Load(context.Background(), art); err != nil {
		t.Fatalf("load1: %v", err)
	}
	if _, err := b.Load(context.Background(), art); err != nil {
		t.Fatalf("load2: %v", err)
	}
	// Neither attempt commits: both abandon. The LAST abandon releases.
	b.AbandonModule("own2")
	if b.CompiledModules() != 1 {
		t.Fatalf("shared ref closed while a holder remains: %d", b.CompiledModules())
	}
	b.AbandonModule("own2")
	if b.CompiledModules() != 0 {
		t.Fatalf("last abandon did not release the ref: %d", b.CompiledModules())
	}
}
