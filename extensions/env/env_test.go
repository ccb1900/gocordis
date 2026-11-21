package env_test

import (
	"context"
	"os"
	"path/filepath"
	"sync"
	"testing"
	"time"

	"dynamic-runtime/extensions/env"
	"dynamic-runtime/extensions/fs"
	"dynamic-runtime/runtime"
)

func tctx(t *testing.T) context.Context {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	t.Cleanup(cancel)
	return ctx
}

type envProbe struct{ ch chan env.Service }

func (p *envProbe) Name() string { return "env-probe" }
func (p *envProbe) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(env.ServiceKey)}
}
func (p *envProbe) Provide() []runtime.Capability { return nil }
func (p *envProbe) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	svc, err := runtime.Require(ctx, env.ServiceKey)
	if err != nil {
		return nil, err
	}
	p.ch <- svc
	return nil, nil
}

func startRT(t *testing.T, root string) (*runtime.Runtime, *runtime.Fiber, fs.Service) {
	t.Helper()
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { _ = rt.Close(context.Background()) })

	ff, err := rt.Load(&fs.Component{})
	if err != nil {
		t.Fatal(err)
	}
	if err := ff.Ready(tctx(t)); err != nil {
		t.Fatalf("fs component not ready: %v", err)
	}
	fp := &fsProbe{ch: make(chan fs.Service, 1)}
	pf, err := rt.Load(fp)
	if err != nil {
		t.Fatal(err)
	}
	if err := pf.Ready(tctx(t)); err != nil {
		t.Fatalf("fs probe not ready: %v", err)
	}
	fsvc := <-fp.ch

	if err := fsvc.Mount("file://"+root, fs.NewLocalProvider(root)); err != nil {
		t.Fatal(err)
	}
	return rt, ff, fsvc
}

type fsProbe struct{ ch chan fs.Service }

func (p *fsProbe) Name() string { return "fs-probe-env" }
func (p *fsProbe) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(fs.ServiceKey)}
}
func (p *fsProbe) Provide() []runtime.Capability { return nil }
func (p *fsProbe) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	svc, err := runtime.Require(ctx, fs.ServiceKey)
	if err != nil {
		return nil, err
	}
	p.ch <- svc
	return nil, nil
}

func loadEnv(t *testing.T, rt *runtime.Runtime, base, mode string, populate bool) (*runtime.Fiber, env.Service) {
	t.Helper()
	ef, err := rt.Load(&env.Component{Base: base, Mode: mode, Populate: populate})
	if err != nil {
		t.Fatal(err)
	}
	if err := ef.Ready(tctx(t)); err != nil {
		t.Fatalf("env component not ready: %v", err)
	}
	p := &envProbe{ch: make(chan env.Service, 1)}
	pf, err := rt.Load(p)
	if err != nil {
		t.Fatal(err)
	}
	if err := pf.Ready(tctx(t)); err != nil {
		t.Fatalf("env probe not ready: %v", err)
	}
	return ef, <-p.ch
}

func writeFile(t *testing.T, root, name, content string) {
	t.Helper()
	if err := os.WriteFile(filepath.Join(root, name), []byte(content), 0o644); err != nil {
		t.Fatal(err)
	}
}

func TestPrecedenceAndDefaultMode(t *testing.T) {
	// Process env must not interfere with these test-only keys.
	for _, k := range []string{"GOCORDIS_P_A", "GOCORDIS_P_B", "GOCORDIS_P_C"} {
		t.Setenv(k, "___unset___")
		_ = os.Unsetenv(k)
	}
	root := t.TempDir()
	writeFile(t, root, ".env", "GOCORDIS_P_A=dotenv\nGOCORDIS_P_B=dotenv\n")
	writeFile(t, root, ".env.local", "GOCORDIS_P_A=local\n")
	writeFile(t, root, ".env.development", "GOCORDIS_P_A=dev\nGOCORDIS_P_C=dev\n")
	writeFile(t, root, ".env.development.local", "GOCORDIS_P_A=devlocal\n")

	rt, _, _ := startRT(t, root)
	// An empty Mode must default to "development".
	ef, svc := loadEnv(t, rt, "file://"+root, "", false)
	if got := svc.Get("GOCORDIS_P_A"); got != "devlocal" {
		t.Fatalf("A=%q, want devlocal", got)
	}
	if got := svc.Get("GOCORDIS_P_B"); got != "dotenv" {
		t.Fatalf("B=%q, want dotenv", got)
	}
	if got := svc.Get("GOCORDIS_P_C"); got != "dev" {
		t.Fatalf("C=%q, want dev", got)
	}
	if _, ok := svc.Lookup("GOCORDIS_P_C"); !ok {
		t.Fatal("Lookup(C) missing")
	}
	if _, ok := svc.Lookup("GOCORDIS_NO_SUCH_KEY"); ok {
		t.Fatal("Lookup of unknown key succeeded")
	}
	all := svc.All()
	all["GOCORDIS_P_A"] = "mutated"
	if svc.Get("GOCORDIS_P_A") != "devlocal" {
		t.Fatal("All() is not an immutable snapshot copy")
	}
	if err := ef.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := ef.Gone(tctx(t)); err != nil {
		t.Fatal(err)
	}
}

func TestExpansionAndProcessEnvWins(t *testing.T) {
	t.Setenv("GOCORDIS_X_A", "from-process")
	t.Setenv("GOCORDIS_X_EMPTY", "")
	root := t.TempDir()
	writeFile(t, root, ".env", ""+
		"GOCORDIS_X_A=from-file\n"+
		"GOCORDIS_X_EMPTY=also-from-file\n"+
		"GOCORDIS_X_B=hello ${GOCORDIS_X_A}, empty=[${GOCORDIS_X_EMPTY}], missing=[${GOCORDIS_X_NOPE}]\n"+
		"export GOCORDIS_X_QUOTED=\"v=${GOCORDIS_X_A}\"\n"+
		"GOCORDIS_X_LITERAL='${GOCORDIS_X_A}'\n")

	rt, _, _ := startRT(t, root)
	_, svc := loadEnv(t, rt, "file://"+root, "production", false)
	if got := svc.Get("GOCORDIS_X_A"); got != "from-process" {
		t.Fatalf("A=%q, want process value", got)
	}
	if got := svc.Get("GOCORDIS_X_EMPTY"); got != "" {
		t.Fatalf("EMPTY=%q, want empty process value", got)
	}
	if got := svc.Get("GOCORDIS_X_B"); got != "hello from-process, empty=[], missing=[]" {
		t.Fatalf("B=%q", got)
	}
	if got := svc.Get("GOCORDIS_X_QUOTED"); got != "v=from-process" {
		t.Fatalf("QUOTED=%q", got)
	}
	if got := svc.Get("GOCORDIS_X_LITERAL"); got != "${GOCORDIS_X_A}" {
		t.Fatalf("LITERAL=%q, single quotes must suppress expansion", got)
	}
}

func TestMissingFilesSkipped(t *testing.T) {
	root := t.TempDir()
	writeFile(t, root, ".env", "GOCORDIS_M_ONLY=base\n")
	rt, _, _ := startRT(t, root)
	_, svc := loadEnv(t, rt, "file://"+root, "staging", false)
	if got := svc.Get("GOCORDIS_M_ONLY"); got != "base" {
		t.Fatalf("ONLY=%q", got)
	}
}

func TestPopulateRestoresOnDispose(t *testing.T) {
	const preKey = "GOCORDIS_POPULATE_PRE"
	const absentKey = "GOCORDIS_POPULATE_ABSENT"
	const changedKey = "GOCORDIS_POPULATE_CHANGED"
	const processOnly = "GOCORDIS_POPULATE_PROCESS_ONLY"
	saved := make(map[string]*string)
	for _, k := range []string{preKey, absentKey, changedKey, processOnly} {
		if v, ok := os.LookupEnv(k); ok {
			vCopy := v
			saved[k] = &vCopy
		} else {
			saved[k] = nil
		}
		_ = os.Unsetenv(k)
	}
	t.Cleanup(func() {
		for k, p := range saved {
			if p != nil {
				_ = os.Setenv(k, *p)
			} else {
				_ = os.Unsetenv(k)
			}
		}
	})

	root := t.TempDir()
	writeFile(t, root, ".env", preKey+"=from-file\n"+absentKey+"=from-file\n"+changedKey+"=from-file\n")

	t.Run("preexisting-process-env-wins", func(t *testing.T) {
		if err := os.Setenv(preKey, "original"); err != nil {
			t.Fatal(err)
		}
		rt, _, _ := startRT(t, root)
		ef, svc := loadEnv(t, rt, "file://"+root, "test", true)
		if got := svc.Get(preKey); got != "original" {
			t.Fatalf("snapshot=%q, want process value", got)
		}
		if got := os.Getenv(preKey); got != "original" {
			t.Fatalf("populated value=%q, want process value kept", got)
		}
		if got := os.Getenv(absentKey); got != "from-file" {
			t.Fatalf("absent key not populated: %q", got)
		}
		if err := ef.Dispose(); err != nil {
			t.Fatal(err)
		}
		if err := ef.Gone(tctx(t)); err != nil {
			t.Fatal(err)
		}
		if got := os.Getenv(preKey); got != "original" {
			t.Fatalf("restored preexisting value=%q", got)
		}
		if _, ok := os.LookupEnv(absentKey); ok {
			t.Fatal("populated key was not unset on dispose")
		}
		if got := svc.Get(absentKey); got != "from-file" {
			t.Fatal("snapshot mutated after dispose")
		}
	})

	t.Run("absent-external-change-and-process-only", func(t *testing.T) {
		for _, k := range []string{preKey, absentKey, changedKey, processOnly} {
			if err := os.Unsetenv(k); err != nil {
				t.Fatal(err)
			}
		}
		if err := os.Setenv(processOnly, "process"); err != nil {
			t.Fatal(err)
		}
		rt, _, _ := startRT(t, root)
		ef, svc := loadEnv(t, rt, "file://"+root, "test", true)
		if got := os.Getenv(absentKey); got != "from-file" {
			t.Fatalf("populated value=%q", got)
		}
		if got := svc.Get(absentKey); got != "from-file" {
			t.Fatalf("snapshot value=%q", got)
		}
		if got := svc.Get(processOnly); got != "process" {
			t.Fatalf("process-only snapshot=%q", got)
		}

		// Values changed externally after population must not be clobbered;
		// process-only keys are never restored because they were never populated.
		if err := os.Setenv(changedKey, "external"); err != nil {
			t.Fatal(err)
		}
		if err := os.Unsetenv(processOnly); err != nil {
			t.Fatal(err)
		}
		if err := ef.Dispose(); err != nil {
			t.Fatal(err)
		}
		if err := ef.Gone(tctx(t)); err != nil {
			t.Fatal(err)
		}
		for _, k := range []string{preKey, absentKey} {
			if _, ok := os.LookupEnv(k); ok {
				t.Fatalf("%s was not unset on dispose", k)
			}
		}
		if got := os.Getenv(changedKey); got != "external" {
			t.Fatalf("externally changed value overwritten: %q", got)
		}
		if _, ok := os.LookupEnv(processOnly); ok {
			t.Fatal("process-only key was restored by Populate")
		}
	})
}

func TestPopulateRollsBackOnSetenvFailure(t *testing.T) {
	// Sorted order: 0GOOD is set, 1BAD... fails (NUL in key), 2AFTER is untouched.
	const good = "GOCORDIS_L2_0GOOD"
	bad := "GOCORDIS_L2_1BAD\x00KEY"
	const after = "GOCORDIS_L2_2AFTER"
	prior, wasSet := os.LookupEnv(good)
	t.Cleanup(func() {
		if wasSet {
			_ = os.Setenv(good, prior)
		} else {
			_ = os.Unsetenv(good)
		}
		_ = os.Unsetenv(after)
	})
	_ = os.Unsetenv(good)
	_ = os.Unsetenv(after)

	root := t.TempDir()
	writeFile(t, root, ".env", good+"=from-file\n"+bad+"=bad\n"+after+"=after\n")
	rt, _, _ := startRT(t, root)
	f, err := rt.Load(&env.Component{Base: "file://" + root, Mode: "test", Populate: true})
	if err != nil {
		t.Fatal(err)
	}
	if err := f.Ready(tctx(t)); err == nil {
		t.Fatal("activation succeeded despite an invalid environment key")
	}
	if v, ok := os.LookupEnv(good); ok && v != prior {
		t.Fatalf("partial populate not rolled back: %q", v)
	}
	if v, ok := os.LookupEnv(after); ok {
		t.Fatalf("key after the failure was populated: %q", v)
	}
}

// stubFS provides an already-configured fs.Service. Reusing the same service
// instance avoids the race between a fresh provider going Active and a mount
// being installed afterwards.
type stubFS struct{ svc fs.Service }

func (c *stubFS) Name() string                 { return "stub-fs" }
func (c *stubFS) Inject() []runtime.Dependency { return nil }
func (c *stubFS) Provide() []runtime.Capability {
	return []runtime.Capability{fs.ServiceKey.Capability()}
}
func (c *stubFS) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	return nil, runtime.Provide[fs.Service](ctx, fs.ServiceKey, c.svc)
}

// freshFSService returns an already-mounted fs.Service from a separate runtime
// whose lifetime is tied to the test. A disposed fs.Component closes its
// service, so recovery must use a new one rather than re-mounting the old.
func freshFSService(t *testing.T, root string) fs.Service {
	t.Helper()
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { _ = rt.Close(context.Background()) })
	ff, err := rt.Load(&fs.Component{})
	if err != nil {
		t.Fatal(err)
	}
	if err := ff.Ready(tctx(t)); err != nil {
		t.Fatal(err)
	}
	p := &fsProbe{ch: make(chan fs.Service, 1)}
	pf, err := rt.Load(p)
	if err != nil {
		t.Fatal(err)
	}
	if err := pf.Ready(tctx(t)); err != nil {
		t.Fatal(err)
	}
	svc := <-p.ch
	if err := svc.Mount("file://"+root, fs.NewLocalProvider(root)); err != nil {
		t.Fatal(err)
	}
	return svc
}

func TestDependencyLossAndRecovery(t *testing.T) {
	root := t.TempDir()
	writeFile(t, root, ".env", "GOCORDIS_RECOVER=ok\n")
	rt, fsf, _ := startRT(t, root)
	ef, svc := loadEnv(t, rt, "file://"+root, "test", false)
	if got := svc.Get("GOCORDIS_RECOVER"); got != "ok" {
		t.Fatalf("initial=%q", got)
	}

	if err := fsf.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := fsf.Gone(tctx(t)); err != nil {
		t.Fatal(err)
	}
	short, cancel := context.WithTimeout(context.Background(), 60*time.Millisecond)
	defer cancel()
	if err := ef.Ready(short); err == nil {
		t.Fatal("env fiber stayed Active after fs service disappeared")
	}

	// Restore the dependency with a fresh provider whose service has the mount.
	if _, err := rt.Load(&stubFS{svc: freshFSService(t, root)}); err != nil {
		t.Fatal(err)
	}
	if err := ef.Ready(tctx(t)); err != nil {
		t.Fatalf("env fiber did not recover: %v", err)
	}
}

func TestModeFromNODE_ENV(t *testing.T) {
	t.Setenv("NODE_ENV", "staging")
	root := t.TempDir()
	writeFile(t, root, ".env", "GOCORDIS_MODE_BASE=base\n")
	writeFile(t, root, ".env.staging", "GOCORDIS_MODE_STAGING=staging\n")
	writeFile(t, root, ".env.development", "GOCORDIS_MODE_DEV=dev\n")

	// Empty Mode must inherit NODE_ENV, not the development default.
	rt, _, _ := startRT(t, root)
	_, svc := loadEnv(t, rt, "file://"+root, "", false)
	if got := svc.Get("GOCORDIS_MODE_STAGING"); got != "staging" {
		t.Fatalf("NODE_ENV mode file not used: %q", got)
	}
	if _, ok := svc.Lookup("GOCORDIS_MODE_DEV"); ok {
		t.Fatal("development file used despite NODE_ENV=staging")
	}

	// An explicit Mode still overrides NODE_ENV.
	rt2, _, _ := startRT(t, root)
	_, svc2 := loadEnv(t, rt2, "file://"+root, "development", false)
	if got := svc2.Get("GOCORDIS_MODE_DEV"); got != "dev" {
		t.Fatalf("explicit Mode ignored: %q", got)
	}
	if _, ok := svc2.Lookup("GOCORDIS_MODE_STAGING"); ok {
		t.Fatal("NODE_ENV file used despite explicit Mode")
	}
}

func TestModeFallsBackToDevelopment(t *testing.T) {
	t.Setenv("NODE_ENV", "")
	root := t.TempDir()
	writeFile(t, root, ".env.development", "GOCORDIS_MODE_FALLBACK=dev\n")
	rt, _, _ := startRT(t, root)
	_, svc := loadEnv(t, rt, "file://"+root, "", false)
	if got := svc.Get("GOCORDIS_MODE_FALLBACK"); got != "dev" {
		t.Fatalf("development fallback not used: %q", got)
	}
}

func TestDotenvMatureParsing(t *testing.T) {
	for _, k := range []string{
		"GOCORDIS_D_BASE", "GOCORDIS_D_REF", "GOCORDIS_D_BRACED",
		"GOCORDIS_D_DEFAULT", "GOCORDIS_D_MULTILINE", "GOCORDIS_D_SINGLE",
	} {
		if err := os.Unsetenv(k); err != nil {
			t.Fatal(err)
		}
	}
	root := t.TempDir()
	writeFile(t, root, ".env", "GOCORDIS_D_BASE=base\n"+
		"GOCORDIS_D_REF=$GOCORDIS_D_BASE\n"+
		"GOCORDIS_D_BRACED=${GOCORDIS_D_BASE}\n"+
		"GOCORDIS_D_DEFAULT=${GOCORDIS_D_NO_SUCH:-fallback}\n"+
		"GOCORDIS_D_MULTILINE=\"line1\nline2\"\n"+
		"GOCORDIS_D_SINGLE='literal ${GOCORDIS_D_BASE}'\n")
	rt, _, _ := startRT(t, root)
	_, svc := loadEnv(t, rt, "file://"+root, "test", false)
	for key, want := range map[string]string{
		"GOCORDIS_D_REF":       "base",
		"GOCORDIS_D_BRACED":    "base",
		"GOCORDIS_D_DEFAULT":   "fallback",
		"GOCORDIS_D_MULTILINE": "line1\nline2",
		"GOCORDIS_D_SINGLE":    "literal ${GOCORDIS_D_BASE}",
	} {
		if got := svc.Get(key); got != want {
			t.Errorf("%s=%q, want %q", key, got, want)
		}
	}
}

// newPopulatedEnv builds an isolated runtime whose env component publishes a
// single key into the shared process environment. The registry is per-process,
// so several of these overlap and exercise multi-instance ownership.
func newPopulatedEnv(t *testing.T, key, value string) (*runtime.Runtime, *runtime.Fiber) {
	t.Helper()
	root := t.TempDir()
	writeFile(t, root, ".env", key+"="+value+"\n")
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { _ = rt.Close(context.Background()) })
	ff, err := rt.Load(&fs.Component{})
	if err != nil {
		t.Fatal(err)
	}
	if err := ff.Ready(tctx(t)); err != nil {
		t.Fatal(err)
	}
	p := &fsProbe{ch: make(chan fs.Service, 1)}
	pf, err := rt.Load(p)
	if err != nil {
		t.Fatal(err)
	}
	if err := pf.Ready(tctx(t)); err != nil {
		t.Fatal(err)
	}
	if err := (<-p.ch).Mount("file://"+root, fs.NewLocalProvider(root)); err != nil {
		t.Fatal(err)
	}
	ef, err := rt.Load(&env.Component{Base: "file://" + root, Mode: "test", Populate: true})
	if err != nil {
		t.Fatal(err)
	}
	if err := ef.Ready(tctx(t)); err != nil {
		t.Fatal(err)
	}
	return rt, ef
}

func TestPopulateMultiInstanceSameValue(t *testing.T) {
	const key = "GOCORDIS_MULTI_SAME"
	prior, wasSet := os.LookupEnv(key)
	t.Cleanup(func() {
		if wasSet {
			_ = os.Setenv(key, prior)
		} else {
			_ = os.Unsetenv(key)
		}
	})
	_ = os.Unsetenv(key)

	_, efA := newPopulatedEnv(t, key, "shared")
	_, efB := newPopulatedEnv(t, key, "shared")
	if got := os.Getenv(key); got != "shared" {
		t.Fatalf("populated=%q", got)
	}
	// Disposing A must not remove B's contribution even though the values are
	// identical: ownership comes from the registry, not a value comparison.
	if err := efA.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := efA.Gone(tctx(t)); err != nil {
		t.Fatal(err)
	}
	if got := os.Getenv(key); got != "shared" {
		t.Fatalf("disposing first owner clobbered the second: %q", got)
	}
	if err := efB.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := efB.Gone(tctx(t)); err != nil {
		t.Fatal(err)
	}
	if _, ok := os.LookupEnv(key); ok {
		t.Fatal("key still set after both owners disposed")
	}
}

func TestPopulateMultiInstanceLowerOwnerFirst(t *testing.T) {
	const key = "GOCORDIS_MULTI_LOWER"
	prior, wasSet := os.LookupEnv(key)
	t.Cleanup(func() {
		if wasSet {
			_ = os.Setenv(key, prior)
		} else {
			_ = os.Unsetenv(key)
		}
	})
	_ = os.Unsetenv(key)

	// The second component sees the first one's populated value as process env,
	// so process-env-wins keeps "first" for both owners.
	_, efA := newPopulatedEnv(t, key, "first")
	_, efB := newPopulatedEnv(t, key, "second")
	if got := os.Getenv(key); got != "first" {
		t.Fatalf("populated=%q, want the first owner's value", got)
	}
	// Dispose the lower owner first; the top owner's value must survive even
	// though both owners wrote the same value.
	if err := efA.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := efA.Gone(tctx(t)); err != nil {
		t.Fatal(err)
	}
	if got := os.Getenv(key); got != "first" {
		t.Fatalf("lower owner disposal changed value: %q", got)
	}
	if err := efB.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := efB.Gone(tctx(t)); err != nil {
		t.Fatal(err)
	}
	if _, ok := os.LookupEnv(key); ok {
		t.Fatal("key still set after both owners disposed")
	}
}

func TestPopulateConcurrentUnwind(t *testing.T) {
	const key = "GOCORDIS_CONCURRENT"
	prior, wasSet := os.LookupEnv(key)
	t.Cleanup(func() {
		if wasSet {
			_ = os.Setenv(key, prior)
		} else {
			_ = os.Unsetenv(key)
		}
	})
	_ = os.Unsetenv(key)

	root := t.TempDir()
	writeFile(t, root, ".env", key+"=value\n")
	errCh := make(chan error, 16)
	var wg sync.WaitGroup
	for i := 0; i < 16; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			rt, err := runtime.New()
			if err != nil {
				errCh <- err
				return
			}
			defer func() { _ = rt.Close(context.Background()) }()
			ff, err := rt.Load(&fs.Component{})
			if err != nil {
				errCh <- err
				return
			}
			if err := ff.Ready(tctx(t)); err != nil {
				errCh <- err
				return
			}
			p := &fsProbe{ch: make(chan fs.Service, 1)}
			pf, err := rt.Load(p)
			if err != nil {
				errCh <- err
				return
			}
			if err := pf.Ready(tctx(t)); err != nil {
				errCh <- err
				return
			}
			if err := (<-p.ch).Mount("file://"+root, fs.NewLocalProvider(root)); err != nil {
				errCh <- err
				return
			}
			ef, err := rt.Load(&env.Component{Base: "file://" + root, Mode: "test", Populate: true})
			if err != nil {
				errCh <- err
				return
			}
			if err := ef.Ready(tctx(t)); err != nil {
				errCh <- err
				return
			}
			if err := ef.Dispose(); err != nil {
				errCh <- err
				return
			}
			if err := ef.Gone(tctx(t)); err != nil {
				errCh <- err
				return
			}
		}()
	}
	wg.Wait()
	close(errCh)
	for err := range errCh {
		t.Error(err)
	}
	if _, ok := os.LookupEnv(key); ok {
		t.Fatal("concurrent owners left the key set")
	}
}
