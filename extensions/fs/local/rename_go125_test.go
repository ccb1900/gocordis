//go:build go1.25

package local

import "testing"

// TestGo125RenameStdlib compiles on toolchains that provide os.Root.Rename and
// verifies the atomic stdlib path stays confined.
func TestGo125RenameStdlib(t *testing.T) { testRenameConfined(t) }
