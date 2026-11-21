package main

import (
	"fmt"
	"os"
	"path/filepath"
	"regexp"
	"strconv"
	"strings"
	"text/template"
)

// CreateOptions describes the plugin to scaffold.
type CreateOptions struct {
	ID      string
	Name    string
	Backend string
	Force   bool
}

type scaffoldData struct {
	ID            string
	Name          string
	Backend       string
	Package       string
	Module        string
	CapabilityKey string
}

var (
	idPattern   = regexp.MustCompile(`^[A-Za-z][A-Za-z0-9_.-]{0,63}$`)
	createValue = map[string]bool{"id": true, "name": true, "backend": true}
)

var goKeywords = map[string]bool{
	"break": true, "case": true, "chan": true, "const": true, "continue": true,
	"default": true, "defer": true, "else": true, "fallthrough": true, "for": true,
	"func": true, "go": true, "goto": true, "if": true, "import": true,
	"interface": true, "map": true, "package": true, "range": true, "return": true,
	"select": true, "struct": true, "switch": true, "type": true, "var": true,
}

const manifestTemplate = `id = {{quote .ID}}
name = {{quote .Name}}
version = "0.1.0"
description = {{printf "%q" .Description}}
module_type = {{quote .ID}}
backend = {{quote .Backend}}

[compatibility]
runtime = ">=0.1.0"
api = ">=0.1.0"

[source]
kind = {{quote .Backend}}
location = {{printf "%q" .Location}}

[[provides]]
key = {{quote .CapabilityKey}}
description = {{printf "%q" .ProvideDescription}}
`

const componentTemplate = `// Package {{.Package}} is a scaffolded gocordis plugin component.
package {{.Package}}

import "dynamic-runtime/runtime"

// capabilityKey is the capability this component publishes. In a real plugin
// the key belongs in the host's internal/contracts package.
var capabilityKey = runtime.NewKey[string]({{quote .CapabilityKey}})

// Component is a minimal runtime.Component. Fill Apply with the plugin's work
// and record every reversible consequence through ctx.Effect.
type Component struct{}

// New returns a fresh component instance for the host's config factory.
func New() runtime.Component { return &Component{} }

func (c *Component) Name() string                 { return {{quote .ID}} }
func (c *Component) Inject() []runtime.Dependency { return nil }
func (c *Component) Provide() []runtime.Capability {
	return []runtime.Capability{capabilityKey.Capability()}
}
func (c *Component) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	if err := runtime.Provide[string](ctx, capabilityKey, {{quote .ID}}); err != nil {
		return nil, err
	}
	return nil, nil
}
`

const goModTemplate = `module {{.Module}}

go 1.24.0

require dynamic-runtime v0.0.0
`

const readmeTemplate = `# {{.Name}}

Scaffolded by ` + "`plugin create`" + ` (id: {{.ID}}, backend: {{.Backend}}).

Files:

- ` + "`plugin.toml`" + ` — pluginmeta manifest. It is descriptive only: metadata
  never drives lifecycle (no Runtime.Load, no provider registration).
- ` + "`component.go`" + ` — a minimal runtime.Component. Register it in the host:

` + "```go" + `
reg.Register({{quote .ID}}, config.FactoryFunc(func(config.ComponentConfig) (runtime.Component, error) {
    return {{.Package}}.New(), nil
}))
` + "```" + `

- ` + "`go.mod`" + ` — add a ` + "`replace dynamic-runtime => <path-to-gocordis>`" + ` for local development.

Run ` + "`plugin doctor plugin.toml`" + ` to validate metadata. ` + "`plugin sandbox plugin.toml`" + `
prints an empty-runtime snapshot: a plugin component cannot be built from
metadata alone and requires build-time registration (no files are changed).
`

// postWriteHook is a test seam for simulating a failure part-way through
// generation. Production code leaves it nil.
var postWriteHook func(name string) error

// normalizeCreateArgs lets the positional directory appear before or after the
// flags, since Go's flag package stops parsing at the first non-flag argument.
func normalizeCreateArgs(args []string) (positional, flags []string) {
	for i := 0; i < len(args); i++ {
		a := args[i]
		if !strings.HasPrefix(a, "-") {
			positional = append(positional, a)
			continue
		}
		flags = append(flags, a)
		name := strings.TrimLeft(a, "-")
		if strings.Contains(name, "=") || !createValue[name] {
			continue
		}
		if i+1 < len(args) {
			i++
			flags = append(flags, args[i])
		}
	}
	return positional, flags
}

func packageName(id string) string {
	var b strings.Builder
	for _, r := range strings.ToLower(id) {
		switch {
		case r >= 'a' && r <= 'z', r >= '0' && r <= '9', r == '_':
			b.WriteRune(r)
		default:
			b.WriteByte('_')
		}
	}
	s := b.String()
	if s == "" {
		s = "plugin"
	}
	if s[0] >= '0' && s[0] <= '9' {
		s = "plugin_" + s
	}
	if goKeywords[s] {
		s += "_plugin"
	}
	return s
}

// mkdirAllTracked creates dir and records the ancestors it created, so a
// failed generation can remove them again in reverse order.
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

type writtenFile struct {
	path    string
	backup  []byte
	mode    os.FileMode
	existed bool
}

// Generate writes a plugin scaffold into dir. It refuses to overwrite existing
// scaffold files unless opts.Force is set, and removes every file and directory
// it created if any step fails.
func Generate(dir string, opts CreateOptions) error {
	if dir == "" {
		return fmt.Errorf("plugin: empty target directory")
	}
	if !idPattern.MatchString(opts.ID) {
		return fmt.Errorf("plugin: invalid id %q (want a letter followed by letters, digits, '_', '.' or '-')", opts.ID)
	}
	if opts.Name == "" || len(opts.Name) > 200 || strings.IndexFunc(opts.Name, func(r rune) bool { return r < 32 || r > 126 }) >= 0 {
		return fmt.Errorf("plugin: invalid name %q", opts.Name)
	}
	switch opts.Backend {
	case "native", "wasm", "proc":
	default:
		return fmt.Errorf("plugin: unsupported backend %q (want native, wasm or proc)", opts.Backend)
	}

	data := struct {
		scaffoldData
		Description        string
		Location           string
		ProvideDescription string
	}{
		scaffoldData: scaffoldData{
			ID:            opts.ID,
			Name:          opts.Name,
			Backend:       opts.Backend,
			Package:       packageName(opts.ID),
			Module:        opts.ID,
			CapabilityKey: opts.ID + ".capability",
		},
		Description:        opts.Name + " plugin scaffolded by plugin create",
		Location:           "./" + opts.ID,
		ProvideDescription: "capability provided by " + opts.ID,
	}
	files := []struct{ rel, body string }{}
	for _, t := range []struct{ rel, tpl string }{
		{"plugin.toml", manifestTemplate},
		{"component.go", componentTemplate},
		{"go.mod", goModTemplate},
		{"README.md", readmeTemplate},
	} {
		tmpl, err := template.New(t.rel).Funcs(template.FuncMap{
			"quote": strconv.Quote,
		}).Parse(t.tpl)
		if err != nil {
			return fmt.Errorf("plugin: template %s: %w", t.rel, err)
		}
		var b strings.Builder
		if err := tmpl.Execute(&b, data); err != nil {
			return fmt.Errorf("plugin: render %s: %w", t.rel, err)
		}
		files = append(files, struct{ rel, body string }{t.rel, b.String()})
	}

	// Pre-check every target before touching the filesystem: a refusal must
	// never leave a partial scaffold behind.
	type precheck struct {
		path    string
		existed bool
		info    os.FileInfo
	}
	var checks []precheck
	for _, f := range files {
		path := filepath.Join(dir, filepath.FromSlash(f.rel))
		info, err := os.Stat(path)
		if err == nil {
			if !opts.Force {
				return fmt.Errorf("plugin: %s already exists (use --force to overwrite)", path)
			}
			checks = append(checks, precheck{path: path, existed: true, info: info})
			continue
		}
		if !os.IsNotExist(err) {
			return fmt.Errorf("plugin: inspect %s: %w", path, err)
		}
		checks = append(checks, precheck{path: path})
	}

	var createdDirs []string
	var written []writtenFile
	rollback := func() {
		for i := len(written) - 1; i >= 0; i-- {
			w := written[i]
			if w.existed {
				_ = os.WriteFile(w.path, w.backup, w.mode)
			} else {
				_ = os.Remove(w.path)
			}
		}
		for i := len(createdDirs) - 1; i >= 0; i-- {
			_ = os.Remove(createdDirs[i])
		}
	}

	for i, f := range files {
		path := checks[i].path
		if err := mkdirAllTracked(filepath.Dir(path), &createdDirs); err != nil {
			rollback()
			return err
		}
		w := writtenFile{path: path, existed: checks[i].existed}
		if w.existed {
			backup, err := os.ReadFile(path)
			if err != nil {
				rollback()
				return fmt.Errorf("plugin: back up %s: %w", f.rel, err)
			}
			w.backup, w.mode = backup, checks[i].info.Mode().Perm()
		}
		written = append(written, w) // track before any write can fail
		flags := os.O_WRONLY | os.O_CREATE | os.O_EXCL
		if w.existed {
			flags = os.O_WRONLY | os.O_TRUNC
		}
		fh, err := os.OpenFile(path, flags, 0o644)
		if err != nil {
			rollback()
			return fmt.Errorf("plugin: %s: %w", f.rel, err)
		}
		if _, err := fh.WriteString(f.body); err != nil {
			_ = fh.Close()
			rollback()
			return fmt.Errorf("plugin: write %s: %w", f.rel, err)
		}
		if err := fh.Close(); err != nil {
			rollback()
			return fmt.Errorf("plugin: close %s: %w", f.rel, err)
		}
		if postWriteHook != nil {
			if err := postWriteHook(f.rel); err != nil {
				rollback()
				return err
			}
		}
	}
	return nil
}
