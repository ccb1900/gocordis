package procplugin

import (
	"context"
	"encoding/json"
	"fmt"
	"net/url"
	"os/exec"
	"sync"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/console/configutil"
	consolehost "dynamic-runtime/extensions/console/host"
	"dynamic-runtime/extensions/console/hub"
	"dynamic-runtime/runtime"
)

// Component is one out-of-process console plugin: it launches the plugin
// executable, registers the manifest-declared hub queries/commands as
// Effects owned by its activation, and kills the process on revoke.
// Uninstall/disable of the component therefore stops the plugin process and
// withdraws its vocabulary — composition-governed like everything else.
//
// Config:
//
//	backend  — plugin executable path (required)
//	queries  — hub query names the process serves (JSON-RPC methods of the
//	           same name; params = the query string as a key→values map)
//	commands — hub command names the process serves (params = the JSON body)
type Component struct {
	id       string
	dir      string
	backend  string
	queries  []string
	commands []string

	mu     sync.Mutex
	client *Client
}

// NewComponent builds the component from one discovered/declared row.
func NewComponent(cc config.ComponentConfig) (*Component, error) {
	backend := configutil.OptionalString(cc, "backend", "")
	if backend == "" {
		return nil, fmt.Errorf("proc-plugin %q: backend is required", cc.ID)
	}
	c := &Component{id: cc.ID, backend: backend, dir: configutil.OptionalString(cc, "dir", ".")}
	if raw, ok := cc.Config["queries"].([]any); ok {
		for _, q := range raw {
			if s, ok := q.(string); ok && s != "" {
				c.queries = append(c.queries, s)
			}
		}
	}
	if raw, ok := cc.Config["commands"].([]any); ok {
		for _, cmd := range raw {
			if s, ok := cmd.(string); ok && s != "" {
				c.commands = append(c.commands, s)
			}
		}
	}
	return c, nil
}

func (c *Component) Name() string { return "console:proc-plugin" }
func (c *Component) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(consolehost.HubKey)}
}
func (c *Component) Provide() []runtime.Capability { return nil }

func (c *Component) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	hubReg, err := runtime.Require(ctx, consolehost.HubKey)
	if err != nil {
		return nil, err
	}
	cmd := exec.CommandContext(ctx.Context(), c.backend)
	cmd.Dir = c.dir
	client, err := Start(ctx.Context(), cmd, 5*time.Second)
	if err != nil {
		return nil, fmt.Errorf("proc-plugin %q: %w", c.id, err)
	}
	c.mu.Lock()
	c.client = client
	c.mu.Unlock()

	owner := fmt.Sprintf("proc-plugin:%s", c.id)
	deadline := 10 * time.Second
	var cleanups []func() error
	fail := func(err error) (runtime.Cleanup, error) {
		_ = client.Close()
		for i := len(cleanups) - 1; i >= 0; i-- {
			_ = cleanups[i]()
		}
		return nil, err
	}
	for _, name := range c.queries {
		name := name
		un, err := hubReg.RegisterQuery(name, owner, func(_ context.Context, values url.Values) (any, *hub.Error) {
			params := map[string]any{}
			for k, vs := range values {
				params[k] = vs
			}
			var result any
			callCtx, cancel := context.WithTimeout(context.Background(), deadline)
			defer cancel()
			if err := client.Call(callCtx, name, params, &result); err != nil {
				return nil, &hub.Error{Code: "plugin_error", Message: err.Error()}
			}
			return result, nil
		})
		if err != nil {
			return fail(err)
		}
		cleanups = append(cleanups, un)
	}
	for _, name := range c.commands {
		name := name
		un, err := hubReg.RegisterCommand(name, owner, func(callCtx context.Context, body json.RawMessage) error {
			callCtx, cancel := context.WithTimeout(callCtx, deadline)
			defer cancel()
			return client.Call(callCtx, name, json.RawMessage(body), nil)
		})
		if err != nil {
			return fail(err)
		}
		cleanups = append(cleanups, un)
	}
	if err := ctx.Effect(func() (func() error, error) {
		return func() error {
			for i := len(cleanups) - 1; i >= 0; i-- {
				_ = cleanups[i]()
			}
			c.mu.Lock()
			client := c.client
			c.client = nil
			c.mu.Unlock()
			if client != nil {
				return client.Close()
			}
			return nil
		}, nil
	}); err != nil {
		return fail(err)
	}
	return nil, nil
}

// Running reports whether the plugin process is alive (diagnostics/tests).
func (c *Component) Running() bool {
	c.mu.Lock()
	defer c.mu.Unlock()
	return c.client != nil && c.client.cmd.ProcessState == nil
}
