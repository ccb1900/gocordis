// Command platform is the runnable composition root for the Phase-2/Phase-3
// wiring: one Runtime, one Config Controller, one console hub/UI host and the
// console Web UI. Everything that lives in this file is application-level
// wiring; no component here speaks to another component's fiber.
//
// Run from the repository root:
//
//	GOCORDIS_CATALOG_DIR=./my-catalog go run ./cmd/platform
//
// The manifest declares which components exist; the Runtime alone decides when
// they become Active, are withdrawn or are replaced. Ctrl-C (SIGINT/SIGTERM)
// drains the Web UI, then the Controller, then the Runtime.
package main

import (
	"context"
	"errors"
	"fmt"
	"io/fs"
	"log"
	"net/http"
	"os"
	"os/signal"
	"path/filepath"
	"strings"
	"sync"
	"syscall"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/configwatch"
	"dynamic-runtime/extensions/console/host"
	"dynamic-runtime/extensions/console/insight"
	"dynamic-runtime/extensions/console/webui"
	"dynamic-runtime/extensions/devtools"
	"dynamic-runtime/extensions/market"
	"dynamic-runtime/runtime"
)

const (
	// Stable declaration types used by cmd/platform/manifest.toml.
	hostType     = "console.host"
	insightType  = "console.insight"
	marketType   = "market"
	devtoolsType = "devtools"
	// demoSettingsType is registered by a real config.SchemaFactory, so the
	// insight "config/schema" query has an entry to report in this application.
	demoSettingsType = "demo.settings"

	defaultAddress = "127.0.0.1:8170"
)

// app owns the composition root: the Runtime and the Config Controller that
// reconciles declarations into it. It never drives a Fiber directly.
type app struct {
	rt           *runtime.Runtime
	reg          config.FactoryRegistry
	ctrl         *config.Controller
	manifestPath string

	mu sync.RWMutex
	ui *host.UIComponent
}

// newApp builds the runtime, factory registry and controller. Factories are the
// only bridge between a manifest row and a Component; each one gets the
// application services it needs through a closure (dependency inversion).
func newApp(manifestPath string) (*app, error) {
	if manifestPath == "" {
		manifestPath = defaultManifestPath()
	}
	if abs, err := filepath.Abs(manifestPath); err == nil {
		manifestPath = abs
	}
	rt, err := runtime.New()
	if err != nil {
		return nil, fmt.Errorf("runtime: %w", err)
	}
	a := &app{rt: rt, reg: config.NewFactoryRegistry(), manifestPath: manifestPath}
	reg := a.reg

	// The controller must exist before the insight service: the service only
	// reads declarations and snapshots and never owns either source.
	a.ctrl = config.NewController(rt, reg)
	insightSvc := insight.New(a.ctrl, rt)

	for _, f := range []struct {
		typ  string
		make config.FactoryFunc
	}{
		{hostType, func(cc config.ComponentConfig) (runtime.Component, error) {
			c, err := host.NewConsole(cc)
			if err != nil {
				return nil, err
			}
			a.setUI(c)
			return c, nil
		}},
		{insightType, func(cc config.ComponentConfig) (runtime.Component, error) {
			return insight.NewComponent(cc, insightSvc)
		}},
		{marketType, func(cc config.ComponentConfig) (runtime.Component, error) {
			return market.NewComponent(cc, a.ctrl)
		}},
		{devtoolsType, func(cc config.ComponentConfig) (runtime.Component, error) {
			return &devtools.Component{
				RuntimeVersion: stringConfig(cc, "runtime_version", ""),
				APIVersion:     stringConfig(cc, "api_version", ""),
			}, nil
		}},
	} {
		if err := reg.Register(f.typ, f.make); err != nil {
			_ = rt.Close(context.Background())
			return nil, fmt.Errorf("register %q: %w", f.typ, err)
		}
	}
	// Register the demo factory as the typed value (not a plain FactoryFunc):
	// it implements config.SchemaFactory, so its real JSON Schema is both
	// enforced at Reconcile time and exposed by the config/schema hub query.
	if err := reg.Register(demoSettingsType, &demoSettingsFactory{schema: demoSettingsSchema()}); err != nil {
		_ = rt.Close(context.Background())
		return nil, fmt.Errorf("register %q: %w", demoSettingsType, err)
	}
	return a, nil
}

func (a *app) setUI(ui *host.UIComponent) {
	a.mu.Lock()
	a.ui = ui
	a.mu.Unlock()
}

func (a *app) hostUI() *host.UIComponent {
	a.mu.RLock()
	defer a.mu.RUnlock()
	return a.ui
}

// reconcileManifest reads the declaration store and hands it to the one
// authority for lifecycle changes. It waits for fibers to settle; waiting is
// not startup sequencing — dependency ordering remains the Kernel's job.
func (a *app) reconcileManifest(ctx context.Context) error {
	if ctx == nil {
		ctx = context.Background()
	}
	data, err := os.ReadFile(a.manifestPath)
	if err != nil {
		return fmt.Errorf("read manifest %q: %w", a.manifestPath, err)
	}
	source := configwatch.Source{ID: "platform-manifest", Path: a.manifestPath, Format: configwatch.FormatTOML}
	desired, err := configwatch.NewTOMLParser().Parse(ctx, source, data)
	if err != nil {
		return fmt.Errorf("parse manifest %q: %w", a.manifestPath, err)
	}
	// Resolve declaration-side paths BEFORE Reconcile: the applied spec must be
	// byte-identical to what the market service submits back on every mutation,
	// otherwise its own declaration would be replaced on each install.
	resolveDeclarationPaths(desired, filepath.Dir(a.manifestPath))
	if err := a.ctrl.Reconcile(ctx, desired); err != nil {
		return fmt.Errorf("reconcile manifest: %w", err)
	}
	for _, o := range a.ctrl.Owned() {
		if o.Fiber == nil { // disabled declaration: entry survives, no fiber
			continue
		}
		if err := o.Fiber.Ready(ctx); err != nil {
			return fmt.Errorf("component %q not ready: %w", o.ID, err)
		}
	}
	return nil
}

func (a *app) owned(id string) (config.OwnedComponent, bool) {
	for _, o := range a.ctrl.Owned() {
		if o.ID == id {
			return o, true
		}
	}
	return config.OwnedComponent{}, false
}

// webUI builds the HTTP adapter over the real console host. The host stays the
// owner of composition and hub state; the server only transports it.
func (a *app) webUI(assets fs.FS) *webui.Server {
	adapter := a.hostUI()
	if adapter == nil || adapter.HostAdapter() == nil {
		return nil
	}
	srv := webui.New(adapter.HostAdapter(), assets)
	adapter.SetObservationSink(observationSink{srv: srv})
	return srv
}

// observationSink is the production boundary between hub observations and
// browser SSE subscribers.
type observationSink struct{ srv *webui.Server }

func (s observationSink) NotifyObservation(ev host.UIObservation) { s.srv.Publish(ev) }

// serve runs the Web UI until ctx is cancelled, then drains it gracefully.
func (a *app) serve(ctx context.Context, address string, assets fs.FS) error {
	srv := a.webUI(assets)
	if srv == nil {
		return errors.New("console host is not active; cannot serve Web UI")
	}
	httpSrv := &http.Server{Addr: address, Handler: srv, ReadHeaderTimeout: 5 * time.Second}
	serveErr := make(chan error, 1)
	go func() { serveErr <- httpSrv.ListenAndServe() }()

	select {
	case err := <-serveErr:
		if errors.Is(err, http.ErrServerClosed) {
			return nil
		}
		return fmt.Errorf("webui listen: %w", err)
	case <-ctx.Done():
		shutdownCtx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
		defer cancel()
		shutdownErr := httpSrv.Shutdown(shutdownCtx)
		select {
		case err := <-serveErr:
			if err != nil && !errors.Is(err, http.ErrServerClosed) {
				return fmt.Errorf("webui serve: %w", err)
			}
		case <-shutdownCtx.Done():
			return shutdownCtx.Err()
		}
		return shutdownErr
	}
}

// shutdown withdraws every owned declaration before closing the Kernel.
func (a *app) shutdown(ctx context.Context) error {
	if ctx == nil {
		ctx = context.Background()
	}
	var errs []error
	if err := a.ctrl.CloseContext(ctx); err != nil {
		errs = append(errs, fmt.Errorf("controller close: %w", err))
	}
	if err := a.rt.Close(ctx); err != nil {
		errs = append(errs, fmt.Errorf("runtime close: %w", err))
	}
	return errors.Join(errs...)
}

// ---------------------------------------------------------------------------
// Demo settings: a component whose factory publishes a real JSON Schema.
// ---------------------------------------------------------------------------

type demoSettingsFactory struct{ schema *config.Schema }

func (f *demoSettingsFactory) Create(cc config.ComponentConfig) (runtime.Component, error) {
	return &settingsComponent{
		id:      cc.ID,
		mode:    stringConfig(cc, "mode", "balanced"),
		enabled: boolConfig(cc, "enabled", true),
		tags:    sliceConfig(cc, "tags"),
		label:   stringConfig(cc, "label", ""),
	}, nil
}

func (f *demoSettingsFactory) ConfigSchema() *config.Schema { return f.schema }

func demoSettingsSchema() *config.Schema {
	return &config.Schema{
		Type:        "object",
		Title:       "Demo settings",
		Description: "Declarative settings for the platform demo component",
		Required:    []string{"mode"},
		Properties: map[string]*config.Schema{
			"mode":    {Type: "string", Enum: []any{"fast", "balanced", "safe"}, Default: "balanced"},
			"enabled": {Type: "boolean", Default: true},
			"tags":    {Type: "array", Items: &config.Schema{Type: "string"}},
			"label":   {Type: "string", Description: "Free-form display label"},
		},
	}
}

type settingsComponent struct {
	id      string
	mode    string
	enabled bool
	tags    []string
	label   string
}

func (c *settingsComponent) Name() string                  { return "demo.settings:" + c.id }
func (c *settingsComponent) Inject() []runtime.Dependency  { return nil }
func (c *settingsComponent) Provide() []runtime.Capability { return nil }
func (c *settingsComponent) Apply(*runtime.Context) (runtime.Cleanup, error) {
	return nil, nil
}

// ---------------------------------------------------------------------------
// Config helpers and application-level path resolution.
// ---------------------------------------------------------------------------

func stringConfig(cc config.ComponentConfig, key, def string) string {
	v, ok := cc.Config[key]
	if !ok || v == nil {
		return def
	}
	if s, ok := v.(string); ok && s != "" {
		return s
	}
	return def
}

func boolConfig(cc config.ComponentConfig, key string, def bool) bool {
	if v, ok := cc.Config[key].(bool); ok {
		return v
	}
	return def
}

func sliceConfig(cc config.ComponentConfig, key string) []string {
	v, ok := cc.Config[key].([]any)
	if !ok {
		return nil
	}
	out := make([]string, 0, len(v))
	for _, item := range v {
		if s, ok := item.(string); ok {
			out = append(out, s)
		}
	}
	return out
}

// resolveDeclarationPaths canonicalizes application-specific declaration
// values once, at the declaration boundary. Keeping the resolved value in the
// applied spec makes later declaration-level mutations stable.
func resolveDeclarationPaths(cfg config.Config, baseDir string) {
	for i := range cfg.Components {
		if cfg.Components[i].Type != marketType {
			continue
		}
		if cfg.Components[i].Config == nil {
			cfg.Components[i].Config = map[string]any{}
		}
		cfg.Components[i].Config["catalog_dirs"] = resolveCatalogDirs(cfg.Components[i].Config["catalog_dirs"], baseDir)
	}
}

// resolveCatalogDirs expands ${ENV} placeholders and interprets relative paths
// against the manifest directory, keeping declarations portable.
func resolveCatalogDirs(raw any, baseDir string) []string {
	var items []string
	switch v := raw.(type) {
	case string:
		if strings.TrimSpace(v) != "" {
			items = []string{v}
		}
	case []string:
		items = v
	case []any:
		for _, item := range v {
			if s, ok := item.(string); ok {
				items = append(items, s)
			}
		}
	}
	if len(items) == 0 { // manifest omitted the key: use the in-repo demo catalog
		items = []string{"catalog"}
	}
	out := make([]string, 0, len(items))
	for _, item := range items {
		p := strings.TrimSpace(os.ExpandEnv(item))
		if p == "" {
			p = "catalog"
		}
		if !filepath.IsAbs(p) {
			p = filepath.Join(baseDir, p)
		}
		out = append(out, filepath.Clean(p))
	}
	return out
}

// defaultManifestPath accepts both repo-root and package-directory working
// directories and always returns an absolute path.
func defaultManifestPath() string {
	if p := strings.TrimSpace(os.Getenv("GOCORDIS_MANIFEST")); p != "" {
		if abs, err := filepath.Abs(p); err == nil {
			return abs
		}
		return p
	}
	for _, candidate := range []string{
		filepath.Join("cmd", "platform", "manifest.toml"),
		"manifest.toml",
	} {
		if info, err := os.Stat(candidate); err == nil && !info.IsDir() {
			if abs, err := filepath.Abs(candidate); err == nil {
				return abs
			}
			return candidate
		}
	}
	abs, _ := filepath.Abs(filepath.Join("cmd", "platform", "manifest.toml"))
	return abs
}

// defaultAssets locates the built console SPA; API routes work without it.
func defaultAssets(manifestPath string) fs.FS {
	if p := strings.TrimSpace(os.Getenv("GOCORDIS_WEB_DIST")); p != "" {
		return os.DirFS(p)
	}
	manifestDir := filepath.Dir(manifestPath)
	candidates := []string{
		filepath.Join("web", "console", "dist"),
		filepath.Join(manifestDir, "..", "..", "web", "console", "dist"),
	}
	for _, candidate := range candidates {
		if info, err := os.Stat(filepath.Join(candidate, "index.html")); err == nil && !info.IsDir() {
			return os.DirFS(candidate)
		}
	}
	return nil
}

func address() string {
	if a := strings.TrimSpace(os.Getenv("GOCORDIS_ADDR")); a != "" {
		return a
	}
	return defaultAddress
}

func run() error {
	manifestPath := defaultManifestPath()
	a, err := newApp(manifestPath)
	if err != nil {
		return err
	}
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()
	defer func() {
		closeCtx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		if err := a.shutdown(closeCtx); err != nil {
			log.Printf("[platform] shutdown: %v", err)
		}
	}()

	readyCtx, cancelReady := context.WithTimeout(ctx, 30*time.Second)
	defer cancelReady()
	if err := a.reconcileManifest(readyCtx); err != nil {
		return err
	}
	log.Printf("[platform] manifest reconciled; Web UI on http://%s", address())
	return a.serve(ctx, address(), defaultAssets(manifestPath))
}

func main() {
	if err := run(); err != nil {
		log.Fatalf("platform: %v", err)
	}
}
