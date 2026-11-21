package main

import (
	"context"
	"errors"
	"fmt"
	"io"
	"os"
	"strings"
	"time"

	"dynamic-runtime/extensions/cli"
	"dynamic-runtime/runtime"
)

// registrar owns the command registrations as one reversible effect of its
// activation. Disposing the fiber withdraws the commands in reverse order.
type registrar struct {
	ver   string
	out   io.Writer
	svcCh chan cli.Service
}

func (c *registrar) Name() string { return "cli-cordis.commands" }
func (c *registrar) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(cli.ServiceKey)}
}
func (c *registrar) Provide() []runtime.Capability { return nil }

func (c *registrar) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	svc, err := runtime.Require(ctx, cli.ServiceKey)
	if err != nil {
		return nil, err
	}
	if err := ctx.Effect(func() (func() error, error) {
		disposeRun, err := svc.Register(buildWithWriter(c.ver, c.out))
		if err != nil {
			return nil, err
		}
		disposeVersion, err := svc.Register(versionWithWriter(c.ver, c.out))
		if err != nil {
			_ = disposeRun()
			return nil, err
		}
		return func() error { return errors.Join(disposeVersion(), disposeRun()) }, nil
	}); err != nil {
		return nil, err
	}
	select {
	case c.svcCh <- svc:
	default:
	}
	return nil, nil
}

// run is the testable composition root. It uses a bounded context as a guard
// timeout; no fixed sleeps and no os.Exit on success.
func run(args []string, out, errOut io.Writer) (err error) {
	if out == nil {
		out = os.Stdout
	}
	if errOut == nil {
		errOut = os.Stderr
	}
	defer func() {
		if err != nil {
			fmt.Fprintln(errOut, err)
		}
	}()

	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()

	rt, err := runtime.New()
	if err != nil {
		return err
	}
	closed := false
	defer func() {
		if !closed {
			_ = rt.Close(ctx)
		}
	}()

	cliF, err := rt.Load(&cli.Component{})
	if err != nil {
		return err
	}
	if err := cliF.Ready(ctx); err != nil {
		return err
	}

	reg := &registrar{ver: versionValue, out: out, svcCh: make(chan cli.Service, 1)}
	if _, err := rt.Load(reg); err != nil {
		return err
	}
	var svc cli.Service
	select {
	case svc = <-reg.svcCh:
	case <-ctx.Done():
		return ctx.Err()
	}

	input := "help"
	if len(args) > 0 {
		switch args[0] {
		case "--version", "-v", "version":
			// The CLI parser treats a leading --token as a flag, not a command
			// path, so map the standalone --version flag to the version command.
			input = "version"
		default:
			input = strings.Join(args, " ")
		}
	}
	if err := svc.Execute(ctx, input, nil, nil); err != nil {
		return err
	}

	if err := rt.Close(ctx); err != nil {
		return err
	}
	closed = true
	return nil
}

func main() {
	_ = run(os.Args[1:], os.Stdout, os.Stderr)
}
