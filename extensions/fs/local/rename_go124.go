//go:build !go1.25

package local

import (
	"context"
	"fmt"
	"os"
	"path/filepath"
	"strings"
)

// rename is the Go 1.24 fallback: os.Root.Rename was added in Go 1.25, so the
// confined operation is built from Root primitives available in Go 1.24. Every
// source and destination path stays inside the Root; a symlink entry is moved
// as a link, never followed. It is non-atomic and refuses an existing target,
// which is an accepted difference only on the Go 1.24 toolchain.
func (p *Provider) rename(ctx context.Context, a, b string) error {
	if err := ctx.Err(); err != nil {
		return err
	}
	fi, err := p.root.Lstat(a)
	if err != nil {
		return fmt.Errorf("fs: rename source: %w", err)
	}
	if _, err := p.root.Lstat(b); err == nil {
		return fmt.Errorf("fs: rename target %q exists", b)
	} else if !os.IsNotExist(err) {
		return err
	}
	if fi.IsDir() && strings.HasPrefix(b, a+string(filepath.Separator)) {
		return fmt.Errorf("fs: cannot rename directory %q into itself", a)
	}
	if err := p.renameEntry(ctx, a, b, fi); err != nil {
		return err
	}
	return ctx.Err()
}

func (p *Provider) renameEntry(ctx context.Context, a, b string, fi os.FileInfo) error {
	if err := ctx.Err(); err != nil {
		return err
	}
	if fi.IsDir() {
		if err := rootMkdirAll(p.root, b, 0o755); err != nil {
			return err
		}
		d, err := p.root.Open(a)
		if err != nil {
			return err
		}
		entries, err := d.ReadDir(-1)
		_ = d.Close()
		if err != nil {
			return err
		}
		for _, e := range entries {
			childA, childB := filepath.Join(a, e.Name()), filepath.Join(b, e.Name())
			cfi, err := p.root.Lstat(childA)
			if err != nil {
				return err
			}
			if err := p.renameEntry(ctx, childA, childB, cfi); err != nil {
				return err
			}
		}
		return p.root.Remove(a)
	}
	if fi.Mode()&os.ModeSymlink != 0 {
		target, err := os.Readlink(filepath.Join(p.rootPath, a))
		if err != nil {
			return err
		}
		if err := os.Symlink(target, filepath.Join(p.rootPath, b)); err != nil {
			return err
		}
		return p.root.Remove(a)
	}
	if err := rootCopyFile(ctx, p.root, a, b, true); err != nil {
		return err
	}
	return p.root.Remove(a)
}
