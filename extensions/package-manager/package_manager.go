// Package packagemanager is the Go counterpart of @cordisjs/package-manager:
// a lifecycle-scoped service that reports and edits module dependencies of a
// Go module. It deliberately performs no network install by itself; module
// downloads remain the Go toolchain's responsibility (go mod tidy/download).
// Only the Go toolchain is detected as an Agent; there is no npm/pnpm/yarn
// equivalent here.
package packagemanager

import (
	"bytes"
	"context"
	"errors"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"sync"

	"golang.org/x/mod/modfile"
	"golang.org/x/mod/module"
	"golang.org/x/mod/semver"

	"dynamic-runtime/runtime"
)

// AgentInfo identifies the detected package tool.
type AgentInfo struct {
	Name    string
	Version string
}

// Module is one requirement declared in go.mod.
type Module struct {
	Path    string
	Version string
}

// Change adds or updates a requirement; an empty Version removes it.
type Change struct {
	Module  string
	Version string
}

// ChangeDetail is the before/after version of one applied change.
type ChangeDetail struct {
	Module string
	Before string
	After  string
}

// Result reports applied changes plus their before/after versions.
type Result struct {
	Added   []string
	Removed []string
	Updated []string
	Details []ChangeDetail
}

// Service is the package-manager capability.
type Service interface {
	Agent(context.Context) (AgentInfo, error)
	Report(context.Context) ([]Module, error)
	Apply(context.Context, []Change) (Result, error)
}

// ServiceKey is the typed capability of this service.
var ServiceKey = runtime.NewKey[Service]("extension.package-manager.service")

// Config configures the module root. Empty Root means the process cwd.
type Config struct {
	Root string
}

type service struct {
	// mu is the single read/write lock shared by Report and Apply so a reader
	// can never observe a batch half-applied.
	mu   sync.Mutex
	root string
}

// New builds a service bound to the given module root.
func New(cfg Config) (Service, error) {
	root := cfg.Root
	if root == "" {
		var err error
		if root, err = os.Getwd(); err != nil {
			return nil, err
		}
	}
	abs, err := filepath.Abs(root)
	if err != nil {
		return nil, err
	}
	info, err := os.Stat(filepath.Join(abs, "go.mod"))
	if err != nil || info.IsDir() {
		return nil, fmt.Errorf("package-manager: no go.mod in %s", abs)
	}
	return &service{root: abs}, nil
}

func (s *service) Agent(ctx context.Context) (AgentInfo, error) {
	bin, err := exec.LookPath("go")
	if err != nil {
		return AgentInfo{}, fmt.Errorf("package-manager: go toolchain: %w", err)
	}
	out, err := s.run(ctx, bin, "version")
	if err != nil {
		return AgentInfo{}, err
	}
	fields := strings.Fields(string(out))
	version := "unknown"
	if len(fields) >= 3 {
		version = fields[2]
	}
	return AgentInfo{Name: "go", Version: version}, nil
}

func (s *service) Report(ctx context.Context) ([]Module, error) {
	s.mu.Lock()
	defer s.mu.Unlock()

	body, err := os.ReadFile(filepath.Join(s.root, "go.mod"))
	if err != nil {
		return nil, err
	}
	f, err := modfile.Parse("go.mod", body, nil)
	if err != nil {
		return nil, fmt.Errorf("package-manager: parse go.mod: %w", err)
	}
	out := make([]Module, 0, len(f.Require))
	for _, r := range f.Require {
		out = append(out, Module{Path: r.Mod.Path, Version: r.Mod.Version})
	}
	return out, nil
}

// validateChange rejects malformed module paths and non-semver versions before
// any file is touched.
func validateChange(ch Change) error {
	if err := module.CheckPath(ch.Module); err != nil {
		return fmt.Errorf("invalid module path %q: %w", ch.Module, err)
	}
	if ch.Version != "" && !semver.IsValid(ch.Version) {
		return fmt.Errorf("invalid version %q for module %q", ch.Version, ch.Module)
	}
	return nil
}

// normalizeChanges validates every change and collapses repeated modules so
// that only the last change for a path is applied. The relative order of the
// surviving changes is preserved.
func normalizeChanges(changes []Change) ([]Change, error) {
	last := make(map[string]int, len(changes))
	for i, ch := range changes {
		if err := validateChange(ch); err != nil {
			return nil, err
		}
		last[ch.Module] = i
	}
	out := make([]Change, 0, len(last))
	for i, ch := range changes {
		if last[ch.Module] == i {
			out = append(out, ch)
		}
	}
	return out, nil
}

// resultFrom builds the result from the final before/after state rather than
// from intermediate edits, so Details never disagree with the written go.mod.
func resultFrom(before, after map[string]string, changes []Change) Result {
	var res Result
	for _, ch := range changes {
		old, hadOld := before[ch.Module]
		now, hasNow := after[ch.Module]
		if hadOld == hasNow && old == now {
			continue
		}
		res.Details = append(res.Details, ChangeDetail{Module: ch.Module, Before: old, After: now})
		switch {
		case !hadOld && hasNow:
			res.Added = append(res.Added, ch.Module)
		case hadOld && !hasNow:
			res.Removed = append(res.Removed, ch.Module)
		default:
			res.Updated = append(res.Updated, ch.Module)
		}
	}
	return res
}

// validateFormatted re-parses the bytes that will be committed and checks
// that their requirements exactly match the intended final state.
func validateFormatted(formatted []byte, want map[string]string) error {
	check, err := modfile.Parse("go.mod", formatted, nil)
	if err != nil {
		return fmt.Errorf("formatted go.mod is invalid: %w", err)
	}
	if len(check.Require) != len(want) {
		return fmt.Errorf("formatted go.mod has %d requirements, want %d", len(check.Require), len(want))
	}
	seen := make(map[string]bool, len(check.Require))
	for _, r := range check.Require {
		if seen[r.Mod.Path] {
			return fmt.Errorf("formatted go.mod repeats requirement %q", r.Mod.Path)
		}
		seen[r.Mod.Path] = true
		if v, ok := want[r.Mod.Path]; !ok || v != r.Mod.Version {
			return fmt.Errorf("formatted go.mod requirement %q=%q does not match final state", r.Mod.Path, r.Mod.Version)
		}
	}
	return nil
}

// atomicReplace writes data to a temporary file in the same directory and
// commits it with a single rename. The original file is never partially
// written; any cleanup/restore error is joined with the original failure and
// returned to the caller.
func atomicReplace(path string, data []byte, mode os.FileMode) error {
	tmp, err := os.CreateTemp(filepath.Dir(path), ".go.mod-*")
	if err != nil {
		return err
	}
	tmpName := tmp.Name()
	restore := func() error {
		if err := os.Remove(tmpName); err != nil && !os.IsNotExist(err) {
			return fmt.Errorf("restore: remove temp file %s: %w", tmpName, err)
		}
		return nil
	}
	if err := tmp.Chmod(mode); err != nil {
		_ = tmp.Close()
		return errors.Join(err, restore())
	}
	if _, err := tmp.Write(data); err != nil {
		_ = tmp.Close()
		return errors.Join(err, restore())
	}
	if err := tmp.Close(); err != nil {
		return errors.Join(err, restore())
	}
	if err := os.Rename(tmpName, path); err != nil {
		return errors.Join(err, restore())
	}
	return nil
}

// Apply validates and normalizes the whole batch, stages the complete edit in
// memory, validates it, and commits it with one atomic rename.
func (s *service) Apply(ctx context.Context, changes []Change) (Result, error) {
	s.mu.Lock()
	defer s.mu.Unlock()

	normalized, err := normalizeChanges(changes)
	if err != nil {
		return Result{}, fmt.Errorf("package-manager: %w", err)
	}

	path := filepath.Join(s.root, "go.mod")
	info, err := os.Stat(path)
	if err != nil {
		return Result{}, err
	}
	mode := info.Mode().Perm()
	original, err := os.ReadFile(path)
	if err != nil {
		return Result{}, err
	}
	f, err := modfile.Parse("go.mod", original, nil)
	if err != nil {
		return Result{}, fmt.Errorf("package-manager: parse go.mod: %w", err)
	}

	before := make(map[string]string, len(f.Require))
	after := make(map[string]string, len(f.Require))
	for _, r := range f.Require {
		before[r.Mod.Path] = r.Mod.Version
		after[r.Mod.Path] = r.Mod.Version
	}
	for _, ch := range normalized {
		if ch.Version == "" {
			_ = f.DropRequire(ch.Module)
			delete(after, ch.Module)
		} else {
			if err := f.AddRequire(ch.Module, ch.Version); err != nil {
				return Result{}, fmt.Errorf("package-manager: add %s: %w", ch.Module, err)
			}
			after[ch.Module] = ch.Version
		}
	}

	res := resultFrom(before, after, normalized)
	f.Cleanup()
	formatted := modfile.Format(f.Syntax)
	if err := validateFormatted(formatted, after); err != nil {
		return Result{}, fmt.Errorf("package-manager: %w", err)
	}
	if err := ctx.Err(); err != nil {
		return Result{}, err
	}
	if err := atomicReplace(path, formatted, mode); err != nil {
		return Result{}, fmt.Errorf("package-manager: write go.mod: %w", err)
	}
	return res, nil
}

func (s *service) run(ctx context.Context, name string, args ...string) ([]byte, error) {
	cmd := exec.CommandContext(ctx, name, args...)
	cmd.Dir = s.root
	var out, errOut bytes.Buffer
	cmd.Stdout, cmd.Stderr = &out, &errOut
	if err := cmd.Run(); err != nil {
		return nil, fmt.Errorf("package-manager: %s %v: %w: %s", name, args, err, strings.TrimSpace(errOut.String()))
	}
	return out.Bytes(), nil
}

// Component exposes the service through the runtime.
type Component struct {
	Root string
}

func (c *Component) Name() string                 { return "package-manager" }
func (c *Component) Inject() []runtime.Dependency { return nil }
func (c *Component) Provide() []runtime.Capability {
	return []runtime.Capability{ServiceKey.Capability()}
}
func (c *Component) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	svc, err := New(Config{Root: c.Root})
	if err != nil {
		return nil, err
	}
	return nil, runtime.Provide(ctx, ServiceKey, svc)
}
