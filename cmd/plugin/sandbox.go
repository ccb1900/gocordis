package main

import (
	"context"
	"fmt"
	"io"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/devtools"
	"dynamic-runtime/runtime"
)

// codeRequiresRegistration records the sandbox boundary: pluginmeta is
// descriptive metadata and cannot build a Go component on its own.
const codeRequiresRegistration devtools.Code = "REQUIRES_BUILD_TIME_REGISTRATION"

// sandbox inspects a plugin in a fresh, temporary Runtime and prints a
// Snapshot summary. It never touches the manifest or any user file.
//
// A plugin implementation cannot be loaded from metadata alone: the host must
// register a config.Factory at build time. Consequently the CLI path reports
// that boundary and snapshots an empty Runtime, which it closes cleanly.
// sandboxWithFactory exercises the "factory available" path for hosts/tests.
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

	rt, err := runtime.New()
	if err != nil {
		printDiagnostic(w, devtools.Diagnostic{
			Severity: devtools.SeverityError,
			Code:     "SANDBOX_INIT",
			Message:  err.Error(),
		})
		return 1
	}
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
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

	if factory == nil {
		printDiagnostic(w, devtools.Diagnostic{
			Severity: devtools.SeverityWarn,
			Code:     codeRequiresRegistration,
			Message:  "requires build-time registration: a plugin component cannot be loaded from pluginmeta metadata alone; register a config.Factory in the host to activate it",
		})
	} else {
		reg := config.NewFactoryRegistry()
		if err := reg.Register(m.ID, factory); err != nil {
			printDiagnostic(w, devtools.Diagnostic{Severity: devtools.SeverityError, Code: "SANDBOX_FACTORY", Message: err.Error()})
			return 1
		}
		ctrl := config.NewController(rt, reg)
		defer func() {
			if err := ctrl.CloseContext(ctx); err != nil {
				printDiagnostic(w, devtools.Diagnostic{
					Severity: devtools.SeverityError,
					Code:     "SANDBOX_CLOSE",
					Message:  err.Error(),
				})
				exit = 1
			}
		}()
		enabled := true
		desired := config.Config{Components: []config.ComponentConfig{{
			ID:      m.ID,
			Type:    m.ID,
			Config:  m.Config,
			Enabled: &enabled,
		}}}
		if err := ctrl.Reconcile(ctx, desired); err != nil {
			printDiagnostic(w, devtools.Diagnostic{Severity: devtools.SeverityError, Code: "SANDBOX_RECONCILE", Message: err.Error()})
			return 1
		}
		// Reconcile returns after Load, not after Apply. Wait on the fiber's
		// readiness API (never a sleep) so the snapshot shows a settled state.
		for _, owned := range ctrl.Owned() {
			if owned.Fiber != nil {
				if err := owned.Fiber.Ready(ctx); err != nil {
					printDiagnostic(w, devtools.Diagnostic{Severity: devtools.SeverityError, Code: "SANDBOX_READY", Message: err.Error()})
					return 1
				}
			}
		}
	}

	snap, err := rt.Snapshot(ctx)
	if err != nil {
		printDiagnostic(w, devtools.Diagnostic{Severity: devtools.SeverityError, Code: "SANDBOX_SNAPSHOT", Message: err.Error()})
		return 1
	}
	printSnapshot(w, snap)
	return exit
}

func printSnapshot(w io.Writer, snap runtime.RuntimeSnapshot) {
	fmt.Fprintf(w, "runtime %s state=%s\n", snap.RuntimeID, snap.State)
	fmt.Fprintf(w, "fibers: %d\n", len(snap.Fibers))
	fmt.Fprintf(w, "providers: %d\n", len(snap.Providers))
	fmt.Fprintf(w, "dependencies: %d\n", len(snap.Dependencies))
	for _, f := range snap.Fibers {
		fmt.Fprintf(w, "  fiber %d %s state=%s\n", f.ID, f.Name, f.State)
	}
	for _, p := range snap.Providers {
		fmt.Fprintf(w, "  provider %s owner=%d state=%s\n", p.Key, p.OwnerFiberID, p.State)
	}
	for _, d := range snap.Dependencies {
		fmt.Fprintf(w, "  dependency %s status=%s\n", d.Key, d.Status)
	}
}
