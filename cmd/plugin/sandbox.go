package main

import (
	"context"
	"fmt"
	"io"
	"time"

	"dynamic-runtime/cmd/plugin/sandboxlib"
	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/devtools"
	"dynamic-runtime/runtime"
)

// codeRequiresRegistration records the sandbox boundary: pluginmeta is
// descriptive metadata and cannot build a Go component on its own.
const codeRequiresRegistration devtools.Code = "REQUIRES_BUILD_TIME_REGISTRATION"

// codeUnknownFactory reports a --factory name absent from the built-in registry.
const codeUnknownFactory devtools.Code = "UNKNOWN_FACTORY"

// sandbox inspects a plugin in a fresh, temporary Runtime and prints a
// Snapshot summary. It never touches the manifest or any user file.
//
// A plugin implementation cannot be loaded from metadata alone: the host must
// register a config.Factory at build time. Consequently the default CLI path
// reports that boundary and snapshots an empty Runtime, which it closes
// cleanly. With --factory the factory-backed public sandboxlib API is used.
func sandbox(path string, w io.Writer) int {
	return sandboxWithFactory(path, w, nil)
}

func sandboxWithFactory(path string, w io.Writer, factory config.Factory) (exit int) {
	m, err := readManifest(path)
	if err != nil {
		printDiagnostic(w, devtools.Diagnostic{
			Severity: devtools.SeverityError,
			Code:     codeParseError,
			Message:  err.Error(),
		})
		return 1
	}

	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()

	if factory != nil {
		if err := sandboxlib.Sandbox(ctx, m, factory, w); err != nil {
			printDiagnostic(w, devtools.Diagnostic{
				Severity: devtools.SeverityError,
				Code:     "SANDBOX",
				Message:  err.Error(),
			})
			return 1
		}
		return 0
	}

	// No factory available: keep the original preflight/empty-runtime behavior.
	rt, err := runtime.New()
	if err != nil {
		printDiagnostic(w, devtools.Diagnostic{
			Severity: devtools.SeverityError,
			Code:     "SANDBOX_INIT",
			Message:  err.Error(),
		})
		return 1
	}
	defer func() {
		if err := rt.Close(ctx); err != nil {
			printDiagnostic(w, devtools.Diagnostic{
				Severity: devtools.SeverityError,
				Code:     "SANDBOX_CLOSE",
				Message:  err.Error(),
			})
			exit = 1
		}
	}()

	printDiagnostic(w, devtools.Diagnostic{
		Severity: devtools.SeverityWarn,
		Code:     codeRequiresRegistration,
		Message:  "requires build-time registration: a plugin component cannot be loaded from pluginmeta metadata alone; register a config.Factory in the host to activate it",
	})

	snap, err := rt.Snapshot(ctx)
	if err != nil {
		printDiagnostic(w, devtools.Diagnostic{
			Severity: devtools.SeverityError,
			Code:     "SANDBOX_SNAPSHOT",
			Message:  err.Error(),
		})
		return 1
	}
	sandboxlib.PrintSnapshot(w, snap)
	return exit
}

func runSandbox(args []string, out, errOut io.Writer) int {
	positional, flags := normalizeValueArgs(args, map[string]bool{"factory": true})
	fs := flagSet("sandbox", errOut)
	factoryName := fs.String("factory", "", "built-in factory name to activate the plugin (e.g. demo)")
	if err := fs.Parse(flags); err != nil {
		return 2
	}
	if len(positional) != 1 {
		fmt.Fprintln(errOut, "usage: plugin sandbox <manifest path> [--factory <name>]")
		return 2
	}
	path := positional[0]
	if *factoryName == "" {
		return sandbox(path, out)
	}
	factory, err := lookupBuiltinFactory(*factoryName)
	if err != nil {
		printDiagnostic(errOut, devtools.Diagnostic{
			Severity: devtools.SeverityError,
			Code:     codeUnknownFactory,
			Message:  err.Error(),
		})
		return 1
	}
	return sandboxWithFactory(path, out, factory)
}
