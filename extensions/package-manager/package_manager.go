// Package packagemanager is the Go counterpart of @cordisjs/package-manager:
// a lifecycle-scoped service that reports and edits module dependencies of a
// Go module. It deliberately performs no network install by itself; module
// downloads remain the Go toolchain's responsibility (go mod tidy/download).
package packagemanager

import (
	"bufio"
	"bytes"
	"context"
	"errors"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"sync"

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
	body, err := os.ReadFile(filepath.Join(s.root, "go.mod"))
	if err != nil {
		return nil, err
	}
	return parseRequires(body)
}

func parseRequires(body []byte) ([]Module, error) {
	var out []Module
	inBlock := false
	sc := bufio.NewScanner(bytes.NewReader(body))
	for sc.Scan() {
		line := strings.TrimSpace(sc.Text())
		if i := strings.Index(line, "//"); i >= 0 {
			line = strings.TrimSpace(line[:i])
		}
		if line == "" {
			continue
		}
		if inBlock {
			if line == ")" {
				inBlock = false
				continue
			}
		} else if line == "require (" {
			inBlock = true
			continue
		} else if strings.HasPrefix(line, "require ") {
			line = strings.TrimSpace(strings.TrimPrefix(line, "require "))
		} else {
			continue
		}
		fields := strings.Fields(line)
		if len(fields) != 2 {
			return nil, fmt.Errorf("package-manager: malformed require line %q", line)
		}
		out = append(out, Module{Path: fields[0], Version: fields[1]})
	}
	if err := sc.Err(); err != nil {
		return nil, err
	}
	if inBlock {
		return nil, errors.New("package-manager: unterminated require block")
	}
	return out, nil
}

func validModulePath(p string) bool {
	if p == "" || strings.ContainsAny(p, " \t\r\n@\"'\\") || strings.Contains(p, "..") {
		return false
	}
	return true
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

// Apply edits go.mod atomically: the file is restored if any change fails.
func (s *service) Apply(ctx context.Context, changes []Change) (Result, error) {
	s.mu.Lock()
	defer s.mu.Unlock()

	for _, ch := range changes {
		if !validModulePath(ch.Module) {
			return Result{}, fmt.Errorf("package-manager: invalid module path %q", ch.Module)
		}
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
	current, err := parseRequires(original)
	if err != nil {
		return Result{}, err
	}
	versions := make(map[string]string, len(current))
	for _, m := range current {
		versions[m.Path] = m.Version
	}

	restore := func() { _ = os.WriteFile(path, original, mode) }
	var res Result
	for _, ch := range changes {
		old, existed := versions[ch.Module]
		args := []string{"mod", "edit"}
		detail := ChangeDetail{Module: ch.Module, Before: old, After: ch.Version}
		if ch.Version == "" {
			if !existed {
				continue
			}
			args = append(args, "-droprequire="+ch.Module)
			res.Removed = append(res.Removed, ch.Module)
		} else {
			if existed && old == ch.Version {
				continue
			}
			args = append(args, "-require="+ch.Module+"@"+ch.Version)
			if existed {
				res.Updated = append(res.Updated, ch.Module)
			} else {
				res.Added = append(res.Added, ch.Module)
			}
		}
		res.Details = append(res.Details, detail)
		if _, err := s.run(ctx, "go", args...); err != nil {
			restore()
			return Result{}, err
		}
	}
	return res, nil
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
