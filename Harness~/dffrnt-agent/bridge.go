package main

import (
	"crypto/rand"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"time"
)

const pollInterval = 250 * time.Millisecond

type Config struct {
	Project    string
	Timeout    float64
	SchemaFile string
	UnityPath  string
}

func (c Config) tempDir() string     { return filepath.Join(c.Project, "Temp") }
func (c Config) agentDir() string    { return filepath.Join(c.tempDir(), "agent") }
func (c Config) requestsDir() string { return filepath.Join(c.agentDir(), "requests") }
func (c Config) responsesDir() string { return filepath.Join(c.agentDir(), "responses") }
func (c Config) sessionPath() string { return filepath.Join(c.agentDir(), "session.json") }

// waitForReady blocks until Unity's session state is no longer "reloading".
// Returns an error if the deadline is exceeded or Unity disappears.
func waitForReady(cfg Config, deadline time.Time) error {
	for time.Now().Before(deadline) {
		data, err := os.ReadFile(cfg.sessionPath())
		if err != nil {
			time.Sleep(pollInterval)
			continue
		}
		var s struct {
			State string `json:"state"`
		}
		if json.Unmarshal(data, &s) == nil && s.State != "reloading" {
			return nil
		}
		time.Sleep(pollInterval)
	}
	return fmt.Errorf("timeout waiting for Unity domain reload to complete")
}

func send(cfg Config, cmd string, args map[string]any) (map[string]any, error) {
	if err := os.MkdirAll(cfg.requestsDir(), 0755); err != nil {
		return nil, fmt.Errorf("cannot create requests dir: %w", err)
	}
	if err := os.MkdirAll(cfg.responsesDir(), 0755); err != nil {
		return nil, fmt.Errorf("cannot create responses dir: %w", err)
	}

	waitDeadline := time.Now().Add(time.Duration(float64(time.Second) * cfg.Timeout))
	if err := waitForReady(cfg, waitDeadline); err != nil {
		return nil, err
	}
	deadline := time.Now().Add(time.Duration(float64(time.Second) * cfg.Timeout))

	uid := newUID()
	payload := map[string]any{"uid": uid, "cmd": cmd}
	for k, v := range args {
		payload[k] = v
	}
	line, err := json.Marshal(payload)
	if err != nil {
		return nil, fmt.Errorf("marshal: %w", err)
	}

	reqName := fmt.Sprintf("%020d-%s.json", time.Now().UnixMilli(), uid)
	reqPath := filepath.Join(cfg.requestsDir(), reqName)
	if err := os.WriteFile(reqPath, line, 0644); err != nil {
		return nil, fmt.Errorf("cannot write request: %w", err)
	}

	respPath := filepath.Join(cfg.responsesDir(), uid+".json")

	if asyncCmds[cmd] {
		for {
			if resp, ok, err := readResponse(respPath); ok {
				return resp, err
			}
			if age := sessionAgeSeconds(cfg); age > staleThreshold {
				if resp, ok, err := readResponse(respPath); ok {
					return resp, err
				}
				return nil, fmt.Errorf("Unity session unresponsive (last heartbeat %ds ago)", age)
			}
			time.Sleep(pollInterval)
		}
	}

	for time.Now().Before(deadline) {
		if resp, ok, err := readResponse(respPath); ok {
			return resp, err
		}
		time.Sleep(pollInterval)
	}

	return nil, fmt.Errorf("timeout after %.0fs", cfg.Timeout)
}

const staleThreshold = 30 // seconds

// asyncCmds wait for Unity liveness rather than a fixed clock deadline.
var asyncCmds = map[string]bool{"compile": true, "refresh": true}

func sessionAgeSeconds(cfg Config) int64 {
	data, err := os.ReadFile(cfg.sessionPath())
	if err != nil {
		return staleThreshold + 1
	}
	var s struct {
		WrittenAt int64 `json:"written_at"`
	}
	if json.Unmarshal(data, &s) != nil || s.WrittenAt == 0 {
		return staleThreshold + 1
	}
	return (time.Now().UnixMilli() - s.WrittenAt) / 1000
}

func readResponse(respPath string) (map[string]any, bool, error) {
	data, err := os.ReadFile(respPath)
	if err != nil {
		return nil, false, nil
	}
	os.Remove(respPath)
	var resp map[string]any
	if json.Unmarshal(data, &resp) != nil {
		return nil, true, fmt.Errorf("invalid response JSON")
	}
	return resp, true, nil
}

func checkSession(path string) error {
	data, err := os.ReadFile(path)
	if err != nil {
		return fmt.Errorf("session file missing — is Unity open?")
	}
	var session struct {
		PID       int   `json:"pid"`
		WrittenAt int64 `json:"written_at"`
	}
	if err := json.Unmarshal(data, &session); err != nil || session.PID == 0 {
		return fmt.Errorf("invalid session file")
	}
	if !isPidRunning(session.PID) {
		return fmt.Errorf("Unity process (pid %d) is not running", session.PID)
	}
	if session.WrittenAt > 0 {
		ageSeconds := (time.Now().UnixMilli() - session.WrittenAt) / 1000
		if ageSeconds > staleThreshold {
			fmt.Fprintf(os.Stderr, "warning: Unity session file is %ds old — editor may be hung\n", ageSeconds)
		}
	}
	return nil
}

func newUID() string {
	b := make([]byte, 8)
	rand.Read(b)
	return hex.EncodeToString(b)
}
