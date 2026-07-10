package main

import (
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"
)

// ── Config path helpers ───────────────────────────────────────────────────────

func TestConfigPaths(t *testing.T) {
	cfg := Config{Project: "/some/project"}
	if got := cfg.tempDir(); got != filepath.Join("/some/project", "Temp") {
		t.Errorf("tempDir: %q", got)
	}
	if got := cfg.agentDir(); !strings.HasSuffix(got, filepath.Join("Temp", "agent")) {
		t.Errorf("agentDir: %q", got)
	}
	if got := cfg.requestsDir(); !strings.HasSuffix(got, filepath.Join("agent", "requests")) {
		t.Errorf("requestsDir: %q", got)
	}
	if got := cfg.responsesDir(); !strings.HasSuffix(got, filepath.Join("agent", "responses")) {
		t.Errorf("responsesDir: %q", got)
	}
	if got := cfg.sessionPath(); !strings.HasSuffix(got, filepath.Join("agent", "session.json")) {
		t.Errorf("sessionPath: %q", got)
	}
}

// ── newUID ────────────────────────────────────────────────────────────────────

func TestNewUID_Format(t *testing.T) {
	uid := newUID()
	if len(uid) != 16 {
		t.Errorf("expected 16 hex chars, got %d: %q", len(uid), uid)
	}
	for _, c := range uid {
		if !strings.ContainsRune("0123456789abcdef", c) {
			t.Errorf("non-hex char %q in uid %q", c, uid)
		}
	}
}

func TestNewUID_Unique(t *testing.T) {
	seen := make(map[string]bool, 100)
	for i := 0; i < 100; i++ {
		uid := newUID()
		if seen[uid] {
			t.Fatalf("duplicate uid %q at iteration %d", uid, i)
		}
		seen[uid] = true
	}
}

// ── checkSession ──────────────────────────────────────────────────────────────

func TestCheckSession_MissingFile(t *testing.T) {
	err := checkSession("/nonexistent/session.json")
	if err == nil {
		t.Fatal("expected error for missing session file")
	}
}

func TestCheckSession_InvalidJSON(t *testing.T) {
	dir := t.TempDir()
	path := filepath.Join(dir, "session.json")
	os.WriteFile(path, []byte("not json"), 0644)

	err := checkSession(path)
	if err == nil {
		t.Fatal("expected error for invalid JSON")
	}
}

func TestCheckSession_ZeroPID(t *testing.T) {
	dir := t.TempDir()
	path := filepath.Join(dir, "session.json")
	os.WriteFile(path, []byte(`{"pid":0,"written_at":0}`), 0644)

	err := checkSession(path)
	if err == nil {
		t.Fatal("expected error for zero PID")
	}
}

func TestCheckSession_CurrentProcess(t *testing.T) {
	dir := t.TempDir()
	path := filepath.Join(dir, "session.json")
	pid := os.Getpid()
	data := fmt.Sprintf(`{"pid":%d,"written_at":%d}`, pid, time.Now().UnixMilli())
	os.WriteFile(path, []byte(data), 0644)

	if err := checkSession(path); err != nil {
		t.Errorf("unexpected error for own PID: %v", err)
	}
}
