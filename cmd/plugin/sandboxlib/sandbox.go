// Package sandboxlib is the public library form of the plugin sandbox.
//
// A pluginmeta.Manifest is descriptive metadata: it cannot build a Go
// component. The host must supply a config.Factory at build time. Sandbox
// creates a fresh Runtime and Controller, registers that factory under the
// manifest's logical ModuleType (falling back to the ID when empty), reconciles
// exactly one declaration whose Type is that same key and whose instance ID is
// the manifest ID, prints a Runtime.Snapshot summary to out and closes
// everything cleanly. It never writes a file.
package sandboxlib

import (
	"context"
	"errors"
	"fmt"
	"io"
	"strings"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/pluginmeta"
	"dynamic-runtime/runtime"
)

// Sandbox activates one factory-backed plugin declaration in a fresh Runtime
// and writes a snapshot summary (fibers/providers/dependencies) to out. It
// returns an error if the manifest is invalid, the factory is missing or
// faulty, or any reconciliation/close step fails. All Runtime and Controller
// resources are closed before it returns.
//
// Hosts that support real plugins embed this function with their own factory
// registry: look up the factory for the manifest, then pass it here.
func Sandbox(ctx context.Context, m pluginmeta.Manifest, factory config.Factory, out io.Writer) (err error) {
	if factory == nil {
		return errors.New("sandboxlib: a config.Factory is required")
	}
	if out == nil {
		return errors.New("sandboxlib: nil output writer")
	}
	if ctx == nil {
		ctx = context.Background()
	}
	if verr := pluginmeta.Validate(m); verr != nil {
		return fmt.Errorf("sandboxlib: invalid manifest: %w", verr)
	}

	// A plugin's logical ModuleType (e.g. "industrial.camera") is the stable
	// factory/type key; the ID is only the instance identity. Hosts look their
	// factories up by ModuleType, so registering/typing by ID breaks Native
	// plugins whose ModuleType differs from their ID (P1-5). Empty ModuleType
	// falls back to the ID for backwards compatibility.
	typeKey := m.ID
	if strings.TrimSpace(m.ModuleType) != "" {
		typeKey = m.ModuleType
	}
	reg := config.NewFactoryRegistry()
	if rerr := reg.Register(typeKey, factory); rerr != nil {
		return fmt.Errorf("sandboxlib: register factory for %q: %w", typeKey, rerr)
	}
	rt, nerr := runtime.New()
	if nerr != nil {
		return fmt.Errorf("sandboxlib: runtime init: %w", nerr)
	}
	ctrl := config.NewController(rt, reg)
	defer func() {
		// Single lifecycle authority: withdraw the Controller's owned fibers
		// first, then close the Runtime. Both errors are reported.
		if cerr := ctrl.CloseContext(ctx); cerr != nil {
			err = errors.Join(err, fmt.Errorf("sandboxlib: controller close: %w", cerr))
		}
		if cerr := rt.Close(ctx); cerr != nil {
			err = errors.Join(err, fmt.Errorf("sandboxlib: runtime close: %w", cerr))
		}
	}()

	enabled := true
	desired := config.Config{Components: []config.ComponentConfig{{
		ID:      m.ID,
		Type:    typeKey,
		Config:  m.Config,
		Enabled: &enabled,
	}}}
	if rerr := ctrl.Reconcile(ctx, desired); rerr != nil {
		err = fmt.Errorf("sandboxlib: reconcile: %w", rerr)
	} else {
		// Reconcile returns after Load, not after Apply. Wait on the fiber's
		// readiness API (never a sleep) so the snapshot shows a settled state.
		for _, owned := range ctrl.Owned() {
			if owned.Fiber != nil {
				if rerr := owned.Fiber.Ready(ctx); rerr != nil {
					err = fmt.Errorf("sandboxlib: fiber %q ready: %w", owned.ID, rerr)
					break
				}
			}
		}
	}

	// A snapshot is useful even when activation failed: it shows the Failed
	// fiber and why nothing was provided.
	snap, serr := rt.Snapshot(ctx)
	if serr != nil {
		err = errors.Join(err, fmt.Errorf("sandboxlib: snapshot: %w", serr))
	} else {
		PrintSnapshot(out, snap)
	}
	return err
}

// PrintSnapshot writes the deterministic, human-readable snapshot summary
// shared by the library and CLI sandboxes.
func PrintSnapshot(w io.Writer, snap runtime.RuntimeSnapshot) {
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
