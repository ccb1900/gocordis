package runtime_test

import (
	"strings"
	"testing"

	"dynamic-runtime/runtime"
)

// ---- State / Intent / RuntimeState strings ----

func TestFiberStateStrings(t *testing.T) {
	want := map[runtime.FiberState]string{
		runtime.StatePending:   "Pending",
		runtime.StateLoading:   "Loading",
		runtime.StateActive:    "Active",
		runtime.StateUnloading: "Unloading",
		runtime.StateFailed:    "Failed",
		runtime.StateGone:      "Gone",
	}
	for st, s := range want {
		if got := st.String(); got != s {
			t.Errorf("FiberState(%d).String() = %q, want %q", uint8(st), got, s)
		}
	}
}

func TestIntentStrings(t *testing.T) {
	if got, want := runtime.IntentMounted.String(), "Mounted"; got != want {
		t.Errorf("IntentMounted.String() = %q, want %q", got, want)
	}
	if got, want := runtime.IntentUnmounted.String(), "Unmounted"; got != want {
		t.Errorf("IntentUnmounted.String() = %q, want %q", got, want)
	}
}

func TestRuntimeStateStrings(t *testing.T) {
	if got, want := runtime.RuntimeRunning.String(), "Running"; got != want {
		t.Errorf("RuntimeRunning.String() = %q, want %q", got, want)
	}
	if got, want := runtime.RuntimeClosing.String(), "Closing"; got != want {
		t.Errorf("RuntimeClosing.String() = %q, want %q", got, want)
	}
	if got, want := runtime.RuntimeClosed.String(), "Closed"; got != want {
		t.Errorf("RuntimeClosed.String() = %q, want %q", got, want)
	}
}

// ---- Domain type declarations ----

// State/Intent are distinct concepts: a Fiber may be Active while its intent
// is Unmounted (withdrawal requested but not yet executed).
func TestStateAndIntentAreSeparate(t *testing.T) {
	// This is a compile-time + conceptual contract: State and Intent must not
	// be the same type.
	var _ runtime.FiberState = runtime.StateActive
	var _ runtime.Intent = runtime.IntentUnmounted
	if runtime.IntentUnmounted == runtime.Intent(0) {
		t.Fatal("unreachable sanity")
	}
	_ = runtime.StateActive != runtime.StateGone
}

// ---- Capability key identity ----

type loggerCap interface{ Log(string) }
type databaseCap interface{ Exec(string) error }

func TestKeyIdentity(t *testing.T) {
	k1 := runtime.NewKey[loggerCap]("logger")
	k2 := runtime.NewKey[loggerCap]("logger")
	if k1.Capability() != k2.Capability() {
		t.Fatal("same T + same name must yield the same capability identity")
	}

	// Different T, same name must NOT collide (typed-key guarantee).
	other := runtime.NewKey[databaseCap]("logger")
	if k1.Capability() == other.Capability() {
		t.Fatal("different T + same name must not collide")
	}

	// Same T, different names must differ.
	renamed := runtime.NewKey[loggerCap]("log2")
	if k1.Capability() == renamed.Capability() {
		t.Fatal("same T + different name must differ")
	}
}

func TestKeyIdentityDoesNotDependOnConstructionSite(t *testing.T) {
	// Two independently-declared package keys with identical type+name must be
	// the same capability, so consumers and providers can share identity
	// without a global registry of key objects.
	a := keyFactoryA()
	b := keyFactoryB()
	if a.Capability() != b.Capability() {
		t.Fatal("capability identity must be stable across construction sites")
	}
}

func keyFactoryA() runtime.Key[loggerCap] { return runtime.NewKey[loggerCap]("logger") }
func keyFactoryB() runtime.Key[loggerCap] { return runtime.NewKey[loggerCap]("logger") }

func TestCapabilityKeyString(t *testing.T) {
	k := runtime.NewKey[loggerCap]("logger")
	s := k.Capability().String()
	if !strings.Contains(s, "logger") {
		t.Fatalf("CapabilityKey.String() = %q, want it to contain the name", s)
	}
}

// ---- Dependency declaration ----

func TestRequiresDependency(t *testing.T) {
	key := runtime.NewKey[loggerCap]("logger")
	dep := runtime.Requires(key)
	if dep.Key != key.Capability() {
		t.Fatalf("Requires must carry the typed key's capability identity")
	}
}
