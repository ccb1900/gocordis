package cli

import (
	"context"
	"errors"
	"reflect"
	"strings"
	"sync"
	"testing"
	"time"

	"dynamic-runtime/runtime"
)

// ---- helpers -------------------------------------------------------------

type testCommand struct {
	name    string
	aliases []string

	mu       sync.Mutex
	calls    int
	lastArgs []string
	lastOpts map[string]string
}

func (c *testCommand) Name() string      { return c.name }
func (c *testCommand) Aliases() []string { return c.aliases }
func (c *testCommand) Execute(_ context.Context, args []string, opts map[string]string) error {
	c.mu.Lock()
	defer c.mu.Unlock()
	c.calls++
	c.lastArgs = append([]string(nil), args...)
	c.lastOpts = make(map[string]string, len(opts))
	for k, v := range opts {
		c.lastOpts[k] = v
	}
	return nil
}
func (c *testCommand) snapshot() (int, []string, map[string]string) {
	c.mu.Lock()
	defer c.mu.Unlock()
	return c.calls, append([]string(nil), c.lastArgs...), c.lastOpts
}

func testCtx(t *testing.T) context.Context {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	t.Cleanup(cancel)
	return ctx
}

func newRT(t *testing.T) *runtime.Runtime {
	t.Helper()
	rt, err := runtime.New()
	if err != nil {
		t.Fatalf("runtime.New: %v", err)
	}
	t.Cleanup(func() {
		ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
		defer cancel()
		if err := rt.Close(ctx); err != nil {
			t.Errorf("rt.Close: %v", err)
		}
	})
	return rt
}

// ---- pure parser ---------------------------------------------------------

func TestParseInputPure(t *testing.T) {
	cases := []struct {
		name    string
		input   string
		head    []string
		flags   map[string]string
		args    []string
		wantErr bool
	}{
		{
			name:  "flags and negation and positional",
			input: "subcmd --name=value -a --no-flag positional",
			head:  []string{"subcmd"},
			flags: map[string]string{"name": "value", "a": "true", "flag": "false"},
			args:  []string{"positional"},
		},
		{
			name:  "space separated command path",
			input: "git remote add origin url --dry-run",
			head:  []string{"git", "remote", "add", "origin", "url"},
			flags: map[string]string{"dry-run": "true"},
		},
		{
			name:  "bare long flag",
			input: "run --verbose",
			head:  []string{"run"},
			flags: map[string]string{"verbose": "true"},
		},
		{
			name:    "duplicate flag is a syntax error",
			input:   "run -a --a=b",
			wantErr: true,
		},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			got, err := parseInput(tc.input)
			if tc.wantErr {
				var se *CliSyntaxError
				if !errors.As(err, &se) {
					t.Fatalf("err = %v (%T), want *CliSyntaxError", err, err)
				}
				return
			}
			if err != nil {
				t.Fatalf("parseInput(%q): %v", tc.input, err)
			}
			if !reflect.DeepEqual(got.head, tc.head) {
				t.Fatalf("head = %v, want %v", got.head, tc.head)
			}
			if !reflect.DeepEqual(got.flags, tc.flags) {
				t.Fatalf("flags = %v, want %v", got.flags, tc.flags)
			}
			if !reflect.DeepEqual(got.args, tc.args) {
				t.Fatalf("args = %v, want %v", got.args, tc.args)
			}
		})
	}
}

// ---- dispatch / aliases / flags -----------------------------------------

func TestDispatchAndAliases(t *testing.T) {
	ctx := testCtx(t)
	svc := newService()
	cmd := &testCommand{name: "git remote add", aliases: []string{"gra"}}
	if _, err := svc.Register(cmd); err != nil {
		t.Fatalf("Register: %v", err)
	}

	for _, input := range []string{"git remote add origin url", "gra origin url"} {
		if err := svc.Execute(ctx, input, nil, nil); err != nil {
			t.Fatalf("Execute(%q): %v", input, err)
		}
	}
	calls, args, _ := cmd.snapshot()
	if calls != 2 || !reflect.DeepEqual(args, []string{"origin", "url"}) {
		t.Fatalf("calls=%d args=%v, want 2 and [origin url]", calls, args)
	}

	// Leading empty/duplicate whitespace is harmless.
	if err := svc.Execute(ctx, "  gra   origin   url  ", nil, nil); err != nil {
		t.Fatalf("Execute with odd spacing: %v", err)
	}
}

func TestFlagsNegationAndBaseMerge(t *testing.T) {
	ctx := testCtx(t)
	svc := newService()
	cmd := &testCommand{name: "run"}
	if _, err := svc.Register(cmd); err != nil {
		t.Fatalf("Register: %v", err)
	}

	baseArgs := []string{"base"}
	baseOpts := map[string]string{"existing": "v", "name": "overridden"}
	if err := svc.Execute(ctx, "run --name=value -a --no-flag positional", baseArgs, baseOpts); err != nil {
		t.Fatalf("Execute: %v", err)
	}
	_, args, opts := cmd.snapshot()
	if !reflect.DeepEqual(args, []string{"base", "positional"}) {
		t.Fatalf("args = %v, want [base positional]", args)
	}
	want := map[string]string{"existing": "v", "name": "value", "a": "true", "flag": "false"}
	if !reflect.DeepEqual(opts, want) {
		t.Fatalf("opts = %v, want %v", opts, want)
	}
	// Caller-owned maps/slices are not mutated.
	if !reflect.DeepEqual(baseArgs, []string{"base"}) || baseOpts["name"] != "overridden" {
		t.Fatalf("caller inputs mutated: args=%v opts=%v", baseArgs, baseOpts)
	}
}

// ---- help / errors -------------------------------------------------------

func TestHelpListsCommands(t *testing.T) {
	ctx := testCtx(t)
	svc := newService()
	_, _ = svc.Register(&testCommand{name: "git remote add", aliases: []string{"gra"}})
	_, _ = svc.Register(&testCommand{name: "build"})

	if err := svc.Execute(ctx, "help", nil, nil); err != nil {
		t.Fatalf("Execute(help): %v", err)
	}
	text := svc.Help("")
	for _, want := range []string{"git remote add", "gra", "build"} {
		if !strings.Contains(text, want) {
			t.Fatalf("Help(%q) missing %q", text, want)
		}
	}
	one := svc.Help("build")
	if !strings.Contains(one, "build") {
		t.Fatalf("Help(build) = %q", one)
	}
}

func TestUnknownCommandAndDuplicateFlagErrors(t *testing.T) {
	ctx := testCtx(t)
	svc := newService()
	_, _ = svc.Register(&testCommand{name: "run"})

	err := svc.Execute(ctx, "missing --x=1", nil, nil)
	var se *CliSyntaxError
	if !errors.As(err, &se) {
		t.Fatalf("unknown command err = %v (%T), want *CliSyntaxError", err, err)
	}
	if !strings.Contains(err.Error(), "missing") {
		t.Fatalf("error should name the unknown input: %v", err)
	}

	err = svc.Execute(ctx, "run -a --a=b", nil, nil)
	if !errors.As(err, &se) {
		t.Fatalf("duplicate flag err = %v (%T), want *CliSyntaxError", err, err)
	}
}

func TestDuplicateRegistrationIsRejected(t *testing.T) {
	svc := newService()
	_, _ = svc.Register(&testCommand{name: "build", aliases: []string{"b"}})
	if _, err := svc.Register(&testCommand{name: "build"}); err == nil {
		t.Fatal("duplicate command name: want error")
	}
	if _, err := svc.Register(&testCommand{name: "other", aliases: []string{"b"}}); err == nil {
		t.Fatal("duplicate alias: want error")
	}
}

func TestRegisterDisposerIsIdempotentAndScoped(t *testing.T) {
	ctx := testCtx(t)
	svc := newService()
	one := &testCommand{name: "one"}
	two := &testCommand{name: "two"}
	disposeOne, err := svc.Register(one)
	if err != nil {
		t.Fatalf("Register one: %v", err)
	}
	disposeTwo, err := svc.Register(two)
	if err != nil {
		t.Fatalf("Register two: %v", err)
	}

	if err := disposeOne(); err != nil {
		t.Fatalf("disposeOne: %v", err)
	}
	if err := disposeOne(); err != nil {
		t.Fatalf("disposeOne twice: %v", err)
	}
	if err := svc.Execute(ctx, "one", nil, nil); err == nil {
		t.Fatal("disposed command still dispatches")
	}
	if err := svc.Execute(ctx, "two", nil, nil); err != nil {
		t.Fatalf("other registration was removed: %v", err)
	}
	if err := disposeTwo(); err != nil {
		t.Fatalf("disposeTwo: %v", err)
	}
}

// ---- Component provides ServiceKey; effect cleanup closes it ------------

type probeComponent struct{ svcCh chan Service }

func (c *probeComponent) Name() string { return "test-probe" }
func (c *probeComponent) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(ServiceKey)}
}
func (c *probeComponent) Provide() []runtime.Capability { return nil }
func (c *probeComponent) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	svc, err := runtime.Require(ctx, ServiceKey)
	if err != nil {
		return nil, err
	}
	select {
	case c.svcCh <- svc:
	default:
	}
	return nil, nil
}

func TestComponentProvidesServiceAndEffectCleanup(t *testing.T) {
	ctx := testCtx(t)
	rt := newRT(t)

	f, err := rt.Load(&Component{})
	if err != nil {
		t.Fatalf("load: %v", err)
	}
	if err := f.Ready(ctx); err != nil {
		t.Fatalf("ready: %v", err)
	}

	probe := &probeComponent{svcCh: make(chan Service, 1)}
	pf, err := rt.Load(probe)
	if err != nil {
		t.Fatalf("load probe: %v", err)
	}
	if err := pf.Ready(ctx); err != nil {
		t.Fatalf("probe ready: %v", err)
	}
	svc := <-probe.svcCh
	if _, err := svc.Register(&testCommand{name: "live"}); err != nil {
		t.Fatalf("Register while Active: %v", err)
	}

	if err := f.Dispose(); err != nil {
		t.Fatalf("Dispose: %v", err)
	}
	if err := f.Gone(ctx); err != nil {
		t.Fatalf("Gone: %v", err)
	}
	if _, err := svc.Register(&testCommand{name: "after"}); err == nil {
		t.Fatal("Register after provider Gone: want closed-service error")
	}
}

// ---- concurrent use is race-clean ---------------------------------------

func TestConcurrentUseRaceClean(t *testing.T) {
	ctx := testCtx(t)
	svc := newService()
	cmd := &testCommand{name: "work"}
	dispose, err := svc.Register(cmd)
	if err != nil {
		t.Fatalf("Register: %v", err)
	}
	var wg sync.WaitGroup
	for i := 0; i < 16; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			_ = svc.Execute(ctx, "work --k=v", nil, nil)
			_ = svc.Help("")
		}()
	}
	wg.Wait()
	if calls, _, _ := cmd.snapshot(); calls != 16 {
		t.Fatalf("calls = %d, want 16", calls)
	}
	if err := dispose(); err != nil {
		t.Fatalf("dispose: %v", err)
	}
}

// ---- quote-aware parsing / combined flags --------------------------------

func TestParseInputQuotesEscapesAndCombinedFlags(t *testing.T) {
	cases := []struct {
		name    string
		input   string
		head    []string
		flags   map[string]string
		args    []string
		wantErr bool
	}{
		{
			name:  "double and single quoted tokens",
			input: `run --name="a b" 'two words'`,
			head:  []string{"run"},
			flags: map[string]string{"name": "a b"},
			args:  []string{"two words"},
		},
		{
			name:  "escape inside single quotes",
			input: `run --x=1 'it\'s ok'`,
			head:  []string{"run"},
			flags: map[string]string{"x": "1"},
			args:  []string{"it's ok"},
		},
		{
			name:  "quoted command path words stay single tokens",
			input: `git "remote add" "origin main"`,
			head:  []string{"git", "remote add", "origin main"},
			flags: map[string]string{},
		},
		{
			name:  "escaped space",
			input: `run a\ b --x=1`,
			head:  []string{"run", "a b"},
			flags: map[string]string{"x": "1"},
		},
		{
			name:  "combined short flags with value on last",
			input: `run -ab=42`,
			head:  []string{"run"},
			flags: map[string]string{"a": "true", "b": "42"},
		},
		{
			name:  "combined short booleans",
			input: `run -ab`,
			head:  []string{"run"},
			flags: map[string]string{"a": "true", "b": "true"},
		},
		{
			name:  "negation still works",
			input: `run --no-verbose`,
			head:  []string{"run"},
			flags: map[string]string{"verbose": "false"},
		},
		{
			name:    "unterminated quote",
			input:   `run "unterminated`,
			wantErr: true,
		},
		{
			name:    "trailing escape",
			input:   `run trailing\`,
			wantErr: true,
		},
		{
			name:    "duplicate from combined and long flag",
			input:   `run -a --a=1`,
			wantErr: true,
		},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			got, err := parseInput(tc.input)
			if tc.wantErr {
				var se *CliSyntaxError
				if !errors.As(err, &se) {
					t.Fatalf("err = %v (%T), want *CliSyntaxError", err, err)
				}
				return
			}
			if err != nil {
				t.Fatalf("parseInput(%q): %v", tc.input, err)
			}
			if !reflect.DeepEqual(got.head, tc.head) || !reflect.DeepEqual(got.flags, tc.flags) || !reflect.DeepEqual(got.args, tc.args) {
				t.Fatalf("got head=%v flags=%v args=%v; want head=%v flags=%v args=%v",
					got.head, got.flags, got.args, tc.head, tc.flags, tc.args)
			}
		})
	}
}

func TestQuotedDispatchAndOptionDeclaration(t *testing.T) {
	ctx := testCtx(t)
	svc := newService()

	git := &testCommand{name: "git remote add", aliases: []string{"gra"}}
	if _, err := svc.Register(git); err != nil {
		t.Fatalf("Register git: %v", err)
	}
	first := `git "remote add" "origin main" --depth=1`
	if err := svc.Execute(ctx, first, nil, nil); err != nil {
		t.Fatalf("Execute(%q): %v", first, err)
	}
	if calls, args, opts := git.snapshot(); calls != 1 || !reflect.DeepEqual(args, []string{"origin main"}) || opts["depth"] != "1" {
		t.Fatalf("first dispatch: calls=%d args=%v opts=%v", calls, args, opts)
	}
	for _, input := range []string{`'git remote add' "my origin"`, `gra "my origin"`} {
		if err := svc.Execute(ctx, input, nil, nil); err != nil {
			t.Fatalf("Execute(%q): %v", input, err)
		}
	}
	if calls, args, _ := git.snapshot(); calls != 3 || !reflect.DeepEqual(args, []string{"my origin"}) {
		t.Fatalf("quoted path/alias dispatch: calls=%d args=%v", calls, args)
	}

	// A command declaring NO options makes every supplied option unknown, with
	// exported detail on the error.
	none := &optionsCommand{testCommand: &testCommand{name: "report"}}
	if _, err := svc.Register(none); err != nil {
		t.Fatalf("Register report: %v", err)
	}
	err := svc.Execute(ctx, "report --verbose", nil, nil)
	var se *CliSyntaxError
	if !errors.As(err, &se) {
		t.Fatalf("unknown option err = %v (%T)", err, err)
	}
	if se.Reason != "unknown option" || se.Option != "verbose" || !strings.Contains(se.Detail, "verbose") || se.Input != "report --verbose" {
		t.Fatalf("unexpected error detail: %+v", se)
	}

	// Both declaration spellings are honored; declared options are accepted.
	declared := &declaredOptionsCommand{
		testCommand: &testCommand{name: "deploy"},
		options:     []string{"--name", "verbose"},
	}
	if _, err := svc.Register(declared); err != nil {
		t.Fatalf("Register deploy: %v", err)
	}
	if err := svc.Execute(ctx, "deploy --name=x --verbose", nil, nil); err != nil {
		t.Fatalf("declared options rejected: %v", err)
	}
	err = svc.Execute(ctx, "deploy --bogus", nil, nil)
	if !errors.As(err, &se) || se.Option != "bogus" {
		t.Fatalf("unknown declared option err = %v (%+v)", err, se)
	}
}

type optionsCommand struct {
	*testCommand
	options []string
}

func (c *optionsCommand) Options() []string { return c.options }

type declaredOptionsCommand struct {
	*testCommand
	options []string
}

func (c *declaredOptionsCommand) DeclaredOptions() []string { return c.options }
