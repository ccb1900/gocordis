// Package env turns dotenv files into an immutable, process-env-aware capability.
// Files are read through the fs.Service capability (never opened directly), so
// the Environment Component participates in ordinary dependency reconciliation.
package env

import (
	"errors"
	"fmt"
	"os"
	"sort"
	"strings"

	"dynamic-runtime/extensions/fs"
	"dynamic-runtime/runtime"
)

// Service is the immutable environment snapshot provided by Component.
type Service interface {
	Get(string) string
	Lookup(string) (string, bool)
	All() map[string]string
}

// ServiceKey is the typed capability key for the environment service.
var ServiceKey = runtime.NewKey[Service]("extension.env.service")

type service struct{ values map[string]string }

func (s *service) Get(key string) string { return s.values[key] }

func (s *service) Lookup(key string) (string, bool) {
	v, ok := s.values[key]
	return v, ok
}

func (s *service) All() map[string]string {
	out := make(map[string]string, len(s.values))
	for k, v := range s.values {
		out[k] = v
	}
	return out
}

// Component reads .env, .env.local, .env.<mode> and .env.<mode>.local (in that
// order) from Base through fs.Service. Mode defaults to "development". An
// empty Mode selects the default; every other value is used verbatim.
type Component struct {
	Base     string // filesystem URL, e.g. file:///tmp/app
	Mode     string
	Populate bool // publish the snapshot into os.Environ via an effect
}

func (c *Component) Name() string { return "extension.env" }

func (c *Component) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(fs.ServiceKey)}
}

func (c *Component) Provide() []runtime.Capability {
	return []runtime.Capability{ServiceKey.Capability()}
}

func (c *Component) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	fsvc, err := runtime.Require(ctx, fs.ServiceKey)
	if err != nil {
		return nil, err
	}
	mode := c.Mode
	if mode == "" {
		mode = "development"
	}
	base := strings.TrimSuffix(c.Base, "/")

	merged := make(map[string]string)
	dotenvKeys := make(map[string]struct{})
	for _, name := range []string{
		".env",
		".env.local",
		".env." + mode,
		".env." + mode + ".local",
	} {
		data, err := fsvc.ReadFile(ctx.Context(), base+"/"+name)
		if err != nil {
			if errors.Is(err, os.ErrNotExist) {
				continue // a missing dotenv file is not an error
			}
			return nil, fmt.Errorf("env: read %s: %w", name, err)
		}
		values, err := parseDotenv(string(data))
		if err != nil {
			return nil, fmt.Errorf("env: parse %s: %w", name, err)
		}
		for k, v := range values {
			merged[k] = v
			dotenvKeys[k] = struct{}{}
		}
	}

	// Process environment wins, including when the process value is empty.
	for k := range merged {
		if v, ok := os.LookupEnv(k); ok {
			merged[k] = v
		}
	}
	// The snapshot also exposes process-only variables.
	for _, entry := range os.Environ() {
		if k, v, ok := strings.Cut(entry, "="); ok {
			if _, exists := merged[k]; !exists {
				merged[k] = v
			}
		}
	}

	// Expansion resolves against the merged (post-precedence) map.
	resolved := make(map[string]string, len(merged))
	for k, v := range merged {
		resolved[k] = expand(v, func(name string) (string, bool) {
			val, ok := merged[name]
			return val, ok
		})
	}
	svc := &service{values: resolved}

	if err := runtime.Provide[Service](ctx, ServiceKey, svc); err != nil {
		return nil, err
	}

	if c.Populate && len(dotenvKeys) > 0 {
		// Only keys that came from dotenv files are published into os.Environ;
		// process-only keys in the snapshot are left alone.
		keys := make([]string, 0, len(dotenvKeys))
		for k := range dotenvKeys {
			keys = append(keys, k)
		}
		sort.Strings(keys)
		type prior struct {
			key, val string
			set      bool
		}
		priors := make([]prior, 0, len(keys))
		for _, k := range keys {
			v, ok := os.LookupEnv(k)
			priors = append(priors, prior{key: k, val: v, set: ok})
		}
		// Restore in LIFO order, but never overwrite a value that somebody else
		// changed after we installed it.
		restore := func(applied []prior) error {
			var errs []error
			for i := len(applied) - 1; i >= 0; i-- {
				p := applied[i]
				if cur, ok := os.LookupEnv(p.key); !ok || cur != resolved[p.key] {
					continue
				}
				var err error
				if p.set {
					err = os.Setenv(p.key, p.val)
				} else {
					err = os.Unsetenv(p.key)
				}
				if err != nil {
					errs = append(errs, err)
				}
			}
			return errors.Join(errs...)
		}
		if err := ctx.Effect(func() (func() error, error) {
			applied := make([]prior, 0, len(priors))
			var errs []error
			for _, p := range priors {
				if err := os.Setenv(p.key, resolved[p.key]); err != nil {
					errs = append(errs, err)
					// The runtime discards the inverse on install failure, so
					// roll back the values already installed in this loop.
					if rbErr := restore(applied); rbErr != nil {
						errs = append(errs, rbErr)
					}
					return nil, errors.Join(errs...)
				}
				applied = append(applied, p)
			}
			return func() error { return restore(applied) }, nil
		}); err != nil {
			return nil, err
		}
	}
	return nil, nil
}

// expand replaces ${VAR} occurrences. An unknown variable expands to the empty
// string; \$ and \\ are escapes handled during dotenv parsing as well.
func expand(s string, lookup func(string) (string, bool)) string {
	var b strings.Builder
	for i := 0; i < len(s); {
		if s[i] == '\\' && i+1 < len(s) {
			switch s[i+1] {
			case '$', '\\', '\'', '"':
				b.WriteByte(s[i+1])
				i += 2
				continue
			}
		}
		if s[i] == '$' && i+1 < len(s) && s[i+1] == '{' {
			if j := strings.IndexByte(s[i+2:], '}'); j >= 0 {
				name := s[i+2 : i+2+j]
				if v, ok := lookup(name); ok {
					b.WriteString(v)
				}
				i += j + 3
				continue
			}
		}
		b.WriteByte(s[i])
		i++
	}
	return b.String()
}

func parseDotenv(text string) (map[string]string, error) {
	out := make(map[string]string)
	for _, raw := range strings.Split(text, "\n") {
		line := strings.TrimSuffix(raw, "\r")
		line = strings.TrimSpace(line)
		if line == "" || strings.HasPrefix(line, "#") {
			continue
		}
		line = strings.TrimSpace(strings.TrimPrefix(line, "export "))
		key, value, ok := strings.Cut(line, "=")
		if !ok {
			continue // tolerant: ignore lines that are not assignments
		}
		key = strings.TrimSpace(key)
		if key == "" || strings.ContainsAny(key, " \t") {
			return nil, fmt.Errorf("invalid key %q", key)
		}
		v, err := parseValue(value)
		if err != nil {
			return nil, fmt.Errorf("%s: %w", key, err)
		}
		out[key] = v
	}
	return out, nil
}

func parseValue(raw string) (string, error) {
	raw = strings.TrimSpace(raw)
	if raw == "" {
		return "", nil
	}
	switch raw[0] {
	case '\'':
		if len(raw) < 2 || raw[len(raw)-1] != '\'' {
			return "", errors.New("unterminated single-quoted value")
		}
		inner := raw[1 : len(raw)-1]
		inner = strings.ReplaceAll(inner, `\'`, `'`)
		inner = strings.ReplaceAll(inner, `\\`, `\`)
		// Single quotes suppress ${...} expansion.
		return strings.ReplaceAll(inner, `$`, `\$`), nil
	case '"':
		if len(raw) < 2 || raw[len(raw)-1] != '"' {
			return "", errors.New("unterminated double-quoted value")
		}
		inner := raw[1 : len(raw)-1]
		var b strings.Builder
		for i := 0; i < len(inner); i++ {
			if inner[i] == '\\' && i+1 < len(inner) {
				switch inner[i+1] {
				case 'n':
					b.WriteByte('\n')
					i++
				case 't':
					b.WriteByte('\t')
					i++
				case 'r':
					b.WriteByte('\r')
					i++
				case '"', '\'', '\\':
					b.WriteByte(inner[i+1])
					i++
				case '$':
					b.WriteString(`\$`) // escaped dollar stays literal after expansion
					i++
				default:
					b.WriteByte(inner[i])
				}
				continue
			}
			b.WriteByte(inner[i])
		}
		return b.String(), nil
	default:
		if i := strings.Index(raw, " #"); i >= 0 {
			raw = strings.TrimSpace(raw[:i])
		}
		return raw, nil
	}
}

// Compile-time interface checks.
var (
	_ Service           = (*service)(nil)
	_ runtime.Component = (*Component)(nil)
)
