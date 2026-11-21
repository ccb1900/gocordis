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
	github.com/go-toml/v2 v2.4.3
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

	"github.com/go-toml/v2"
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

// Generate writes a minimal, config-driven GoCordis application into dir.
func Generate(dir, module string) error {
	if strings.TrimSpace(module) == "" || strings.ContainsAny(module, " \t\r\n@\"'\\") || strings.Contains(module, "..") {
		return fmt.Errorf("create-cordis: invalid module path %q", module)
	}
	if dir == "" {
		return fmt.Errorf("create-cordis: empty target directory")
	}
	files := map[string]string{
		"go.mod":                    fmt.Sprintf(goModTemplate, module),
		"manifest.toml":             manifestTemplate,
		"main.go":                   fmt.Sprintf(mainTemplate, module+"/internal/app"),
		"internal/app/component.go": componentTemplate,
	}
	names := []string{"go.mod", "manifest.toml", "main.go", "internal/app/component.go"}
	var created []string
	rollback := func() {
		for i := len(created) - 1; i >= 0; i-- {
			_ = os.Remove(created[i])
		}
	}
	for _, name := range names {
		body := files[name]
		path := filepath.Join(dir, filepath.FromSlash(name))
		if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
			rollback()
			return err
		}
		f, err := os.OpenFile(path, os.O_WRONLY|os.O_CREATE|os.O_EXCL, 0o644)
		if err != nil {
			rollback()
			return fmt.Errorf("create-cordis: %s: %w", name, err)
		}
		created = append(created, path) // track before any write can fail
		if _, err := f.WriteString(body); err != nil {
			_ = f.Close()
			rollback()
			return err
		}
		if err := f.Close(); err != nil {
			rollback()
			return err
		}
	}
	return nil
}
