//go:build !go1.25

package local

import (
	"os"
	"path/filepath"
)

// symlink is the Go 1.24 fallback: Root.Symlink was added in Go 1.25, so the
// parent is validated through Root.Open by Symlink before this raw call. The
// link text is always root-relative, so a follow stays confined to the root.
func (p *Provider) symlink(stored, linkRel string) error {
	return os.Symlink(stored, filepath.Join(p.rootPath, linkRel))
}
