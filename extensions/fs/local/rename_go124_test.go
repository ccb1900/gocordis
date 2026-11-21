//go:build !go1.25

package local

import "testing"

// TestGo124RenameFallback compiles only when os.Root.Rename is unavailable. It
// guards the Go 1.24 gate: a regression that removes rename_go124.go breaks the
// build under GOTOOLCHAIN=go1.24.0 instead of silently shipping a dead API.
func TestGo124RenameFallback(t *testing.T) { testRenameConfined(t) }
