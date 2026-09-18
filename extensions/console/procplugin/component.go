package procplugin

import (
	"context"
	"encoding/json"
	"fmt"
	"log/slog"
	"net/url"
	"os"
	"os/exec"
	"path/filepath"
	stdruntime "runtime"
	"strings"
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
// 容错语义：后端启动失败时，组件降级而非阻断——Fiber 保持 Active，日志
// 记录降级原因，观察流汇报失败。平台不会被单个插件阻断。
//
// Config:
//
//	backend  — plugin executable path (required)
//	queries  — hub query names the process serves (JSON-RPC methods of the
//	           same name; params = the query string as a key→values map)
//	commands — hub command names the process serves (params = the JSON body)
//	handshake_timeout_seconds — startup handshake wait (default 5)
type Component struct {
	id       string
	dir      string
	backend  string
	queries  []string
	commands []string
	logger   *slog.Logger

	mu     sync.Mutex
	client *Client
}

// NewComponent builds the component from one discovered/declared row.
func NewComponent(cc config.ComponentConfig) (*Component, error) {
	backend := configutil.OptionalString(cc, "backend", "")
	if backend == "" {
		return nil, fmt.Errorf("proc-plugin %q: backend is required", cc.ID)
	}
	c := &Component{
		id: cc.ID, backend: backend,
		dir:    configutil.OptionalString(cc, "dir", "."),
		logger: slog.Default(),
	}
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

// resolveExecutable 解析后端可执行文件的绝对路径。清单按 POSIX 习惯写
// backend = "./alarm-demo"——路径里含分隔符时 Windows 的 CreateProcess
// 不会追加 .exe，而 Windows 构建产物是 alarm-demo.exe；相对路径再叠加
// cmd.Dir 的解析差异。因此这里一次解析到位：相对路径锚定插件目录，
// Windows 上缺省补 .exe，两不存在则返回原值让 Start 报出原始错误。
func (c *Component) resolveExecutable() string {
	p := c.backend
	if p == "" {
		return p
	}
	if !filepath.IsAbs(p) {
		p = filepath.Join(c.dir, p)
	}
	if _, err := os.Stat(p); err == nil {
		return p
	}
	if stdruntime.GOOS == "windows" && !strings.EqualFold(filepath.Ext(p), ".exe") {
		if _, err := os.Stat(p + ".exe"); err == nil {
			return p + ".exe"
		}
	}
	return p
}

func (c *Component) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	hubReg, err := runtime.Require(ctx, consolehost.HubKey)
	if err != nil {
		return nil, err
	}
	cmd := exec.CommandContext(ctx.Context(), c.resolveExecutable())
	cmd.Dir = c.dir
	client, err := Start(ctx.Context(), cmd, 5*time.Second)
	if err != nil {
		// 后端启动失败：降级而非阻断。Fiber 保持 Active，日志记录降级
		// 原因，观察流汇报失败。平台不会被单个插件阻断——操作员可以
		// 通过控制台禁用/卸载该插件，或修复后热加载恢复。
		c.logger.Warn("proc-plugin backend failed to start; running in degraded mode",
			"plugin", c.id, "backend", c.backend, "error", err)
		_ = ctx.Effect(func() (func() error, error) {
			return func() error { return nil }, nil
		})
		return nil, nil
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
