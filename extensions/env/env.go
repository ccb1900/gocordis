// Package env turns dotenv files into an immutable, process-env-aware capability.
// Files are read through the fs.Service capability (never opened directly), so
// the Environment Component participates in ordinary dependency reconciliation.
// Parsing is delegated to the compose-spec dotenv package, which handles
// $VAR / ${VAR} / ${VAR:-default}, quoted and multiline values.
package env

import (
	"errors"
	"fmt"
	"os"
	"sort"
	"strings"
	"sync"

	"dynamic-runtime/extensions/fs"
	"dynamic-runtime/runtime"
	"github.com/compose-spec/compose-go/v2/dotenv"
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
// order) from Base through fs.Service. An empty Mode is taken from NODE_ENV,
// falling back to "development". An explicit Mode is used verbatim.
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

// resolveMode implements the documented Mode precedence.
func resolveMode(explicit string) string {
	if explicit != "" {
		return explicit
	}
	if fromEnv := os.Getenv("NODE_ENV"); fromEnv != "" {
		return fromEnv
	}
	return "development"
}

func (c *Component) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	fsvc, err := runtime.Require(ctx, fs.ServiceKey)
	if err != nil {
		return nil, err
	}
	mode := resolveMode(c.Mode)
	base := strings.TrimSuffix(c.Base, "/")

	// A shared map lets later files both override earlier ones and resolve
	// ${VAR} references against values already parsed. os.LookupEnv is the
	// lookup function, so the process environment wins during expansion too.
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
		if err := dotenv.ParseWithFormat(strings.NewReader(string(data)), name, merged, os.LookupEnv, dotenv.DotEnv); err != nil {
			return nil, fmt.Errorf("env: parse %s: %w", name, err)
		}
		for k := range merged {
			dotenvKeys[k] = struct{}{}
		}
	}

	// Process environment wins, including when the process value is empty.
	for k := range dotenvKeys {
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

	// dotenv already expanded values against the process env and earlier file
	// entries; the snapshot is the final immutable map.
	svc := &service{values: merged}
	if err := runtime.Provide[Service](ctx, ServiceKey, svc); err != nil {
		return nil, err
	}

	if c.Populate && len(dotenvKeys) > 0 {
		owner := &populateOwner{}
		keys := make([]string, 0, len(dotenvKeys))
		for k := range dotenvKeys {
			keys = append(keys, k)
		}
		sort.Strings(keys)
		if err := ctx.Effect(func() (func() error, error) {
			for _, k := range keys {
				if err := populateSet(owner, k, merged[k]); err != nil {
					// The runtime discards the inverse on install failure, so
					// roll back this owner's already-installed registrations.
					if rbErr := populateUnwind(owner); rbErr != nil {
						return nil, errors.Join(err, rbErr)
					}
					return nil, err
				}
			}
			return func() error { return populateUnwind(owner) }, nil
		}); err != nil {
			return nil, err
		}
	}
	return nil, nil
}

// populateOwner is the identity of one Populate installation. Two components
// writing the same key (even the same value) are distinct owners.
type populateOwner struct{}

// populateEntry is one contribution to a variable's stack. prior is the value
// that must be restored when this entry is the top-most one for its key.
type populateEntry struct {
	owner    *populateOwner
	value    string
	prior    string
	hadPrior bool
}

// populateRegistry is process-global: several Env components (possibly in
// different runtimes) can populate the same variable concurrently. Each key
// owns a stack of contributions ordered by installation time.
var populateRegistry = struct {
	mu     sync.Mutex
	stacks map[string][]*populateEntry
}{stacks: make(map[string][]*populateEntry)}

// populateSet records a contribution to key and installs value into the
// process environment.
func populateSet(owner *populateOwner, key, value string) error {
	populateRegistry.mu.Lock()
	defer populateRegistry.mu.Unlock()

	stack := populateRegistry.stacks[key]
	var prior string
	var hadPrior bool
	if n := len(stack); n > 0 {
		prior, hadPrior = stack[n-1].value, true
	} else {
		prior, hadPrior = os.LookupEnv(key)
	}
	if err := os.Setenv(key, value); err != nil {
		return err
	}
	populateRegistry.stacks[key] = append(stack, &populateEntry{
		owner: owner, value: value, prior: prior, hadPrior: hadPrior,
	})
	return nil
}

// populateUnwind removes the top-most registration owned by owner from every
// key. Ownership (not value equality) decides what is removed: an owner that
// is not on top leaves the live value untouched, but its entry is retired and
// the entry above inherits its restore target.
func populateUnwind(owner *populateOwner) error {
	populateRegistry.mu.Lock()
	defer populateRegistry.mu.Unlock()

	var errs []error
	for key, stack := range populateRegistry.stacks {
		idx := -1
		for i := len(stack) - 1; i >= 0; i-- {
			if stack[i].owner == owner {
				idx = i
				break
			}
		}
		if idx < 0 {
			continue
		}
		e := stack[idx]
		top := idx == len(stack)-1
		if top {
			// Only the top-most entry owns the live value. Do not clobber a
			// value changed externally after we installed ours.
			if cur, ok := os.LookupEnv(key); ok && cur == e.value {
				var err error
				if e.hadPrior {
					err = os.Setenv(key, e.prior)
				} else {
					err = os.Unsetenv(key)
				}
				if err != nil {
					errs = append(errs, err)
				}
			}
		} else {
			// Keep the chain valid for the entry now on top.
			stack[idx+1].prior, stack[idx+1].hadPrior = e.prior, e.hadPrior
		}
		remaining := append(stack[:idx], stack[idx+1:]...)
		if len(remaining) == 0 {
			delete(populateRegistry.stacks, key)
		} else {
			populateRegistry.stacks[key] = remaining
		}
	}
	return errors.Join(errs...)
}

// Compile-time interface checks.
var (
	_ Service           = (*service)(nil)
	_ runtime.Component = (*Component)(nil)
)
