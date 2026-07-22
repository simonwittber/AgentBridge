//go:build !windows

package main

import (
	"errors"
	"os"
	"syscall"
)

func isPidRunning(pid int) bool {
	proc, err := os.FindProcess(pid)
	if err != nil {
		return false
	}
	err = proc.Signal(syscall.Signal(0))
	// nil: running; EPERM: running but no permission to signal; ESRCH: gone
	return err == nil || errors.Is(err, syscall.EPERM)
}
