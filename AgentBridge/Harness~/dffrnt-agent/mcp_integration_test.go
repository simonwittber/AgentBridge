package main

import (
	"bufio"
	"encoding/json"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"sync"
	"testing"
	"time"
)

// shared is the single MCP server process used for the entire test session.
var shared *mcpClient

// testProject is the resolved Unity project path, set in TestMain.
var testProject string

func findBinary() string {
	candidates := []string{"dffrnt-agent", "dffrnt-agent.exe"}
	// Check current directory first.
	for _, name := range candidates {
		p := filepath.Join(".", name)
		if _, err := os.Stat(p); err == nil {
			abs, _ := filepath.Abs(p)
			return abs
		}
	}
	// Fall back to PATH.
	for _, name := range candidates {
		if p, err := exec.LookPath(name); err == nil {
			return p
		}
	}
	return ""
}

func TestMain(m *testing.M) {
	binary := findBinary()
	if binary == "" {
		fmt.Fprintln(os.Stderr, "dffrnt-agent not found in current directory or PATH — skipping MCP integration tests")
		fmt.Fprintln(os.Stderr, "Build it first: go build -o dffrnt-agent.exe .")
		os.Exit(0)
	}

	project := os.Getenv("UNITY_PROJECT")
	if project == "" {
		abs, err := filepath.Abs("../../Example~")
		if err != nil {
			fmt.Fprintf(os.Stderr, "resolve Example~ path: %v\n", err)
			os.Exit(1)
		}
		project = abs
	}
	testProject = project

	// Check Unity is running before starting the server.
	data, err := os.ReadFile(filepath.Join(project, "Temp", "agent", "session.json"))
	if err != nil {
		fmt.Fprintln(os.Stderr, "Unity not running (no session file) — skipping MCP integration tests")
		os.Exit(0)
	}
	var session struct {
		PID       int   `json:"pid"`
		WrittenAt int64 `json:"written_at"`
	}
	if json.Unmarshal(data, &session) != nil || session.PID == 0 {
		fmt.Fprintln(os.Stderr, "Unity not running (invalid session) — skipping MCP integration tests")
		os.Exit(0)
	}
	if age := (time.Now().UnixMilli() - session.WrittenAt) / 1000; age > 60 {
		fmt.Fprintf(os.Stderr, "Unity session stale (%ds) — skipping MCP integration tests\n", age)
		os.Exit(0)
	}

	shared = launchMCPServer(binary, project)
	code := m.Run()
	shared.cmd.Process.Kill()
	shared.cmd.Wait()
	os.Exit(code)
}

// ── helpers ───────────────────────────────────────────────────────────────────

type mcpClient struct {
	cmd     *exec.Cmd
	enc     *json.Encoder
	nextID  int
	notifCh chan map[string]any
	mu      sync.Mutex
	pending map[int]chan map[string]any
}

func launchMCPServer(binary, project string) *mcpClient {
	cmd := exec.Command(binary, "--project", project, "serve")
	stdin, err := cmd.StdinPipe()
	if err != nil {
		panic(err)
	}
	stdout, err := cmd.StdoutPipe()
	if err != nil {
		panic(err)
	}
	if err := cmd.Start(); err != nil {
		panic(fmt.Sprintf("start serve: %v", err))
	}
	scanner := bufio.NewScanner(stdout)
	scanner.Buffer(make([]byte, 4*1024*1024), 4*1024*1024) // 4 MB for large responses (e.g. base64 screenshots)
	c := &mcpClient{
		cmd:     cmd,
		enc:     json.NewEncoder(stdin),
		nextID:  1,
		notifCh: make(chan map[string]any, 64),
		pending: make(map[int]chan map[string]any),
	}
	// Perform MCP handshake by reading directly before the background reader starts.
	c.enc.Encode(map[string]any{
		"jsonrpc": "2.0", "id": 0, "method": "initialize",
		"params": map[string]any{
			"protocolVersion": "2024-11-05",
			"capabilities":    map[string]any{},
			"clientInfo":      map[string]any{"name": "test", "version": "0.0.1"},
		},
	})
	for scanner.Scan() {
		line := strings.TrimSpace(scanner.Text())
		if line == "" {
			continue
		}
		var resp map[string]any
		if json.Unmarshal([]byte(line), &resp) == nil {
			if resp["id"] == float64(0) {
				break
			}
		}
	}
	c.enc.Encode(map[string]any{"jsonrpc": "2.0", "method": "notifications/initialized"})

	// Background reader routes all lines: responses to pending channels, notifications to notifCh.
	go func() {
		for scanner.Scan() {
			line := strings.TrimSpace(scanner.Text())
			if line == "" {
				continue
			}
			var msg map[string]any
			if json.Unmarshal([]byte(line), &msg) != nil {
				continue
			}
			if _, hasID := msg["id"]; !hasID {
				select {
				case c.notifCh <- msg:
				default:
				}
				continue
			}
			id := int(msg["id"].(float64))
			c.mu.Lock()
			ch, ok := c.pending[id]
			c.mu.Unlock()
			if ok {
				ch <- msg
			}
		}
	}()

	return c
}

func (c *mcpClient) call(t *testing.T, method string, params any) map[string]any {
	t.Helper()
	id := c.nextID
	c.nextID++

	ch := make(chan map[string]any, 1)
	c.mu.Lock()
	c.pending[id] = ch
	c.mu.Unlock()
	defer func() {
		c.mu.Lock()
		delete(c.pending, id)
		c.mu.Unlock()
	}()

	msg := map[string]any{"jsonrpc": "2.0", "id": id, "method": method}
	if params != nil {
		msg["params"] = params
	}
	if err := c.enc.Encode(msg); err != nil {
		t.Fatalf("write %s: %v", method, err)
	}

	select {
	case resp := <-ch:
		return resp
	case <-time.After(150 * time.Second):
		t.Fatalf("timeout waiting for %s (id=%d)", method, id)
		return nil
	}
}

func (c *mcpClient) notify(t *testing.T, method string, params any) {
	t.Helper()
	msg := map[string]any{"jsonrpc": "2.0", "method": method}
	if params != nil {
		msg["params"] = params
	}
	c.enc.Encode(msg)
}

// invokeCmd calls a named Unity MCP tool directly.
func (c *mcpClient) invokeCmd(t *testing.T, cmd string, args map[string]any) map[string]any {
	t.Helper()
	return c.callTool(t, cmd, args)
}

func (c *mcpClient) callTool(t *testing.T, name string, args map[string]any) map[string]any {
	t.Helper()
	resp := c.call(t, "tools/call", map[string]any{"name": name, "arguments": args})
	if errVal, ok := resp["error"]; ok {
		t.Fatalf("MCP error calling %s: %v", name, errVal)
	}
	result, _ := resp["result"].(map[string]any)
	if result == nil {
		t.Fatalf("no result for %s: %v", name, resp)
	}
	content, _ := result["content"].([]any)
	if len(content) == 0 {
		t.Fatalf("empty content for %s", name)
	}
	text, _ := content[0].(map[string]any)["text"].(string)
	if text == "" {
		t.Fatalf("no text in content for %s", name)
	}
	var payload map[string]any
	if err := json.Unmarshal([]byte(text), &payload); err != nil {
		t.Fatalf("unmarshal result for %s: %v\n%s", name, err, text)
	}
	return payload
}

func testName(suffix string) string {
	return fmt.Sprintf("[MCP-%s-%d]", suffix, time.Now().UnixMilli())
}

// waitNotification blocks until a notifications/message with the given method
// arrives on the client, or until timeout elapses.
func (c *mcpClient) waitNotification(t *testing.T, method string, timeout time.Duration) map[string]any {
	t.Helper()
	deadline := time.After(timeout)
	for {
		select {
		case n := <-c.notifCh:
			if m, _ := n["method"].(string); m == method {
				return n
			}
		case <-deadline:
			t.Fatalf("timeout waiting for notification %q", method)
			return nil
		}
	}
}

// waitTypedNotification blocks until a notifications/message whose inner
// data.type equals eventType arrives, draining unrelated notifications.
func (c *mcpClient) waitTypedNotification(t *testing.T, eventType string, timeout time.Duration) map[string]any {
	t.Helper()
	deadline := time.After(timeout)
	for {
		select {
		case n := <-c.notifCh:
			if m, _ := n["method"].(string); m != "notifications/message" {
				continue
			}
			params, _ := n["params"].(map[string]any)
			if params == nil {
				continue
			}
			data, _ := params["data"].(map[string]any)
			if data == nil {
				continue
			}
			if data["type"] == eventType {
				return n
			}
		case <-deadline:
			t.Fatalf("timeout waiting for notification with type=%q", eventType)
			return nil
		}
	}
}

// saveIfDirty saves the active scene if it has unsaved changes, preventing
// Unity from showing a save-dialog when a subsequent operation switches scenes.
// If the scene has never been saved (empty path), it logs a warning and skips —
// scene_save requires an existing path on disk.
func saveIfDirty(t *testing.T, c *mcpClient) {
	t.Helper()
	info := c.callTool(t, "scene_info", map[string]any{})
	dirty, _ := info["dirty"].(bool)
	if !dirty {
		return
	}
	path, _ := info["path"].(string)
	if path == "" {
		t.Log("warning: active scene is dirty but has never been saved — skipping scene_save")
		return
	}
	if p := c.callTool(t, "scene_save", map[string]any{}); p["status"] != "ok" {
		t.Logf("warning: scene_save failed: %v", p)
	}
}

