//go:build !windows

package main

import (
	"os/exec"
	"syscall"
)

func processGroupSupported() bool { return true }

func setProcessGroup(cmd *exec.Cmd) {
	cmd.SysProcAttr = &syscall.SysProcAttr{Setpgid: true}
}

func processGroupCancel(cmd *exec.Cmd) func() error {
	return func() error { return syscall.Kill(-cmd.Process.Pid, syscall.SIGINT) }
}
