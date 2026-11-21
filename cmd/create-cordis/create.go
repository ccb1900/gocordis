// Package create implements the Go counterpart of create-cordis: a small,
// deterministic project scaffolder. It writes files only into an empty target
// directory and never runs external commands.
package main

import (
	"fmt"
	"os"
	"path/filepath"
	"strings"
)

const goModTemplate = `module %s

go 1.24.0

require (
	dynamic-runtime v0.0.0
	github.com/pelletier/go-toml/v2 v2.4.3
)

replace dynamic-runtime => github.com/ccb1900/gocordis v0.0.0
`

const manifestTemplate = `[[components]]
id = "app"
type = "app"
enabled = true

[components.config]
greeting = "hello"
`

const mainTemplate = `package main

import (
	"context"
	"log"
	"os"
	"os/signal"

	"github.com/pelletier/go-toml/v2"
	"dynamic-runtime/extensions/config"
	"dynamic-runtime/runtime"

	%q
)

func loadManifest(path string) config.Config {
	var cfg config.Config
	data, err := os.ReadFile(path)
	if err != nil {
		log.Fatal(err)
	}
	if err := toml.Unmarshal(data, &cfg); err != nil {
		log.Fatal(err)
	}
	return cfg
}

func main() {
	rt, err := runtime.New()
	if err != nil {
		log.Fatal(err)
	}
	reg := config.NewFactoryRegistry()
	if err := reg.Register("app", config.FactoryFunc(func(cc config.ComponentConfig) (runtime.Component, error) {
		return app.New(cc.Config)
	})); err != nil {
		log.Fatal(err)
	}
	ctrl := config.NewController(rt, reg)
	ctx, cancel := signal.NotifyContext(context.Background(), os.Interrupt)
	defer cancel()
	if err := ctrl.Reconcile(ctx, loadManifest("manifest.toml")); err != nil {
		log.Fatal(err)
	}
	<-ctx.Done()
	_ = ctrl.CloseContext(ctx)
	_ = rt.Close(ctx)
}
`

const componentTemplate = `package app

import (
	"log"

	"dynamic-runtime/runtime"
)

type Component struct{ greeting string }

func New(cfg map[string]any) (runtime.Component, error) {
	greeting, _ := cfg["greeting"].(string)
	if greeting == "" {
		greeting = "hello"
	}
	return &Component{greeting: greeting}, nil
}

func (c *Component) Name() string                 { return "app" }
func (c *Component) Inject() []runtime.Dependency { return nil }
func (c *Component) Provide() []runtime.Capability { return nil }
func (c *Component) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	log.Printf("%s from GoCordis", c.greeting)
	return nil, nil
}
`

// postWriteHook is a test seam for simulating a failure part-way through
// generation. Production code leaves it nil.
var postWriteHook func(name string) error

// mkdirAllTracked creates dir and records the ancestors it had to create, so
// a failed generation can remove them again (in reverse order).
func mkdirAllTracked(dir string, created *[]string) error {
	var missing []string
	p := filepath.Clean(dir)
	for {
		if _, err := os.Stat(p); err == nil {
			break
		} else if !os.IsNotExist(err) {
			return err
		}
		missing = append(missing, p)
		parent := filepath.Dir(p)
		if parent == p {
			break
		}
		p = parent
	}
	if err := os.MkdirAll(dir, 0o755); err != nil {
		return err
	}
	for i := len(missing) - 1; i >= 0; i-- {
		*created = append(*created, missing[i])
	}
	return nil
}

// Generate writes a minimal, config-driven GoCordis application into dir.
// The target must be empty (or not exist yet); every created file and
// directory is removed if any step fails.
func Generate(dir, module string) error {
	if strings.TrimSpace(module) == "" || strings.ContainsAny(module, " \t\r\n@\"'\\") || strings.Contains(module, "..") {
		return fmt.Errorf("create-cordis: invalid module path %q", module)
	}
	if dir == "" {
		return fmt.Errorf("create-cordis: empty target directory")
	}

	// Enforce the empty-dir precondition before creating anything.
	entries, err := os.ReadDir(dir)
	if err != nil {
		if !os.IsNotExist(err) {
			return fmt.Errorf("create-cordis: inspect target directory: %w", err)
		}
	} else if len(entries) > 0 {
		return fmt.Errorf("create-cordis: target directory %s is not empty", dir)
	}

	files := map[string]string{
		"go.mod":                    fmt.Sprintf(goModTemplate, module),
		"manifest.toml":             manifestTemplate,
		"main.go":                   fmt.Sprintf(mainTemplate, module+"/internal/app"),
		"internal/app/component.go": componentTemplate,
	}
	names := []string{"go.mod", "manifest.toml", "main.go", "internal/app/component.go"}
	var createdFiles, createdDirs []string
	rollback := func() {
		for i := len(createdFiles) - 1; i >= 0; i-- {
			_ = os.Remove(createdFiles[i])
		}
		for i := len(createdDirs) - 1; i >= 0; i-- {
			_ = os.Remove(createdDirs[i])
		}
	}
	for _, name := range names {
		path := filepath.Join(dir, filepath.FromSlash(name))
		if err := mkdirAllTracked(filepath.Dir(path), &createdDirs); err != nil {
			rollback()
			return err
		}
		f, err := os.OpenFile(path, os.O_WRONLY|os.O_CREATE|os.O_EXCL, 0o644)
		if err != nil {
			rollback()
			return fmt.Errorf("create-cordis: %s: %w", name, err)
		}
		createdFiles = append(createdFiles, path) // track before any write can fail
		if _, err := f.WriteString(files[name]); err != nil {
			_ = f.Close()
			rollback()
			return err
		}
		if err := f.Close(); err != nil {
			rollback()
			return err
		}
		if postWriteHook != nil {
			if err := postWriteHook(name); err != nil {
				rollback()
				return err
			}
		}
	}
	return nil
}
