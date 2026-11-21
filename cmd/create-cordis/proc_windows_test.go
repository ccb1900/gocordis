//go:build windows

package main

import "os/exec"

// Windows has no POSIX process groups. The generated-app smoke test is skipped
// there instead of emulating signals; build/tidy verification still runs.
func processGroupSupported() bool { return false }

func setProcessGroup(_ *exec.Cmd) {}

func processGroupCancel(_ *exec.Cmd) func() error { return nil }
