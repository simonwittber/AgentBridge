package main

import (
	"bufio"
	"encoding/json"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"testing"
	"time"
)

// shared is the single MCP server process used for the entire test session.
var shared *mcpClient

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
	cmd    *exec.Cmd
	enc    *json.Encoder
	dec    *bufio.Scanner
	nextID int
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
	c := &mcpClient{
		cmd:    cmd,
		enc:    json.NewEncoder(stdin),
		dec:    bufio.NewScanner(stdout),
		nextID: 1,
	}
	// Perform MCP handshake using a dummy testing.T-like fatal.
	msg := map[string]any{
		"jsonrpc": "2.0", "id": 0, "method": "initialize",
		"params": map[string]any{
			"protocolVersion": "2024-11-05",
			"capabilities":    map[string]any{},
			"clientInfo":      map[string]any{"name": "test", "version": "0.0.1"},
		},
	}
	c.enc.Encode(msg)
	// Drain the initialize response.
	for c.dec.Scan() {
		line := strings.TrimSpace(c.dec.Text())
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
	return c
}

func (c *mcpClient) call(t *testing.T, method string, params any) map[string]any {
	t.Helper()
	id := c.nextID
	c.nextID++
	msg := map[string]any{"jsonrpc": "2.0", "id": id, "method": method}
	if params != nil {
		msg["params"] = params
	}
	if err := c.enc.Encode(msg); err != nil {
		t.Fatalf("write %s: %v", method, err)
	}

	type result struct {
		resp map[string]any
		err  string
	}
	ch := make(chan result, 1)
	go func() {
		for c.dec.Scan() {
			line := strings.TrimSpace(c.dec.Text())
			if line == "" {
				continue
			}
			var resp map[string]any
			if json.Unmarshal([]byte(line), &resp) != nil {
				continue
			}
			if _, hasID := resp["id"]; !hasID {
				continue // notification
			}
			if resp["id"] == float64(id) {
				ch <- result{resp: resp}
				return
			}
		}
		ch <- result{err: "connection closed"}
	}()

	select {
	case r := <-ch:
		if r.err != "" {
			t.Fatalf("%s (id=%d): %s", method, id, r.err)
		}
		return r.resp
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

// ── Core ──────────────────────────────────────────────────────────────────────

func TestMCP_Status(t *testing.T) {
	c := shared

	p := c.callTool(t, "status", map[string]any{})
	if p["status"] != "ok" {
		t.Errorf("expected ok, got %v", p["status"])
	}
	if p["uptime_s"] == nil {
		t.Error("missing uptime_s")
	}
	if p["busy"] == nil {
		t.Error("missing busy")
	}
}

func TestMCP_Commands(t *testing.T) {
	c := shared

	p := c.callTool(t, "commands", map[string]any{})
	if p["status"] != "ok" {
		t.Errorf("expected ok, got %v", p["status"])
	}
	cmds, _ := p["commands"].([]any)
	if len(cmds) == 0 {
		t.Error("expected at least one command")
	}
}

func TestMCP_Compile(t *testing.T) {
	c := shared

	p := c.callTool(t, "compile", map[string]any{})
	if p["status"] != "ok" {
		t.Errorf("expected ok, got %v", p["status"])
	}
	if p["errors"] == nil {
		t.Error("missing errors array")
	}
}

func TestMCP_Refresh(t *testing.T) {
	c := shared

	p := c.callTool(t, "refresh", map[string]any{})
	if p["status"] != "ok" {
		t.Errorf("expected ok, got %v", p["status"])
	}
}

func TestMCP_UUID(t *testing.T) {
	c := shared

	p := c.callTool(t, "uuid", map[string]any{})
	if p["status"] != "ok" {
		t.Errorf("expected ok, got %v", p["status"])
	}
	uid, _ := p["uuid"].(string)
	if len(uid) != 36 {
		t.Errorf("expected 36-char UUID, got %q", uid)
	}
}

// ── Scene ─────────────────────────────────────────────────────────────────────

func TestMCP_SceneInfo(t *testing.T) {
	c := shared

	p := c.callTool(t, "scene_info", map[string]any{})
	if p["status"] != "ok" {
		t.Errorf("expected ok, got %v", p["status"])
	}
	if p["scene_name"] == nil {
		t.Error("missing scene_name")
	}
	if p["root_count"] == nil {
		t.Error("missing root_count")
	}
}

// ── Hierarchy ─────────────────────────────────────────────────────────────────

func TestMCP_Hierarchy(t *testing.T) {
	c := shared

	p := c.callTool(t, "hierarchy", map[string]any{"depth": 2})
	if p["status"] != "ok" {
		t.Errorf("expected ok, got %v", p["status"])
	}
	if p["objects"] == nil {
		t.Error("missing objects")
	}
}

func TestMCP_ObjectFind(t *testing.T) {
	c := shared
	saveIfDirty(t, c)

	name := testName("Find")
	c.callTool(t, "object_create", map[string]any{"name": name})
	defer c.callTool(t, "object_delete", map[string]any{"path": name})

	p := c.callTool(t, "object_find", map[string]any{"path": name})
	if p["status"] != "ok" {
		t.Fatalf("object_find failed: %v", p)
	}
	obj, _ := p["object"].(map[string]any)
	if obj["name"] != name {
		t.Errorf("expected name %q, got %v", name, obj["name"])
	}
	if obj["components"] == nil {
		t.Error("missing components")
	}
}

func TestMCP_ObjectFind_Missing(t *testing.T) {
	c := shared

	p := c.callTool(t, "object_find", map[string]any{"path": "[MCP-DoesNotExist-x9z]"})
	if p["status"] != "error" {
		t.Errorf("expected error for missing object, got %v", p["status"])
	}
}

func TestMCP_ObjectsFind(t *testing.T) {
	c := shared
	saveIfDirty(t, c)

	a, b := testName("ObjsA"), testName("ObjsB")
	c.callTool(t, "object_create", map[string]any{"name": a})
	c.callTool(t, "object_create", map[string]any{"name": b})
	c.callTool(t, "component_add", map[string]any{"path": a, "type": "AudioSource"})
	c.callTool(t, "component_add", map[string]any{"path": b, "type": "AudioSource"})
	defer c.callTool(t, "object_delete", map[string]any{"path": a})
	defer c.callTool(t, "object_delete", map[string]any{"path": b})

	p := c.callTool(t, "objects_find", map[string]any{"component": "AudioSource"})
	if p["status"] != "ok" {
		t.Fatalf("objects_find failed: %v", p)
	}
	objects, _ := p["objects"].([]any)
	names := map[string]bool{}
	for _, o := range objects {
		m, _ := o.(map[string]any)
		names[fmt.Sprint(m["name"])] = true
	}
	if !names[a] || !names[b] {
		t.Errorf("expected both objects in result, got %v", names)
	}
}

// ── Objects ───────────────────────────────────────────────────────────────────

func TestMCP_ObjectCreateAndDelete(t *testing.T) {
	c := shared
	saveIfDirty(t, c)

	name := testName("Create")
	p := c.callTool(t, "object_create", map[string]any{"name": name})
	if p["status"] != "ok" {
		t.Fatalf("object_create failed: %v", p)
	}
	if p["path"] != name {
		t.Errorf("expected path %q, got %v", name, p["path"])
	}

	del := c.callTool(t, "object_delete", map[string]any{"path": name})
	if del["status"] != "ok" {
		t.Fatalf("object_delete failed: %v", del)
	}
	find := c.callTool(t, "object_find", map[string]any{"path": name})
	if find["status"] != "error" {
		t.Error("object should be gone after delete")
	}
}

func TestMCP_ObjectActive(t *testing.T) {
	c := shared
	saveIfDirty(t, c)

	name := testName("Active")
	c.callTool(t, "object_create", map[string]any{"name": name})
	defer c.callTool(t, "object_delete", map[string]any{"path": name})

	p := c.callTool(t, "object_active", map[string]any{"path": name, "active": false})
	if p["status"] != "ok" {
		t.Fatalf("object_active(false) failed: %v", p)
	}
	find := c.callTool(t, "object_find", map[string]any{"path": name})
	obj, _ := find["object"].(map[string]any)
	if obj["active_self"] != false {
		t.Error("expected object to be inactive")
	}

	c.callTool(t, "object_active", map[string]any{"path": name, "active": true})
	find2 := c.callTool(t, "object_find", map[string]any{"path": name})
	obj2, _ := find2["object"].(map[string]any)
	if obj2["active_self"] != true {
		t.Error("expected object to be active again")
	}
}

func TestMCP_ObjectRename(t *testing.T) {
	c := shared
	saveIfDirty(t, c)

	old := testName("RenameOld")
	newName := testName("RenameNew")
	c.callTool(t, "object_create", map[string]any{"name": old})
	defer c.callTool(t, "object_delete", map[string]any{"path": newName})

	p := c.callTool(t, "object_rename", map[string]any{"path": old, "name": newName})
	if p["status"] != "ok" {
		t.Fatalf("object_rename failed: %v", p)
	}
	if p["path"] != newName {
		t.Errorf("expected path %q, got %v", newName, p["path"])
	}
	if find := c.callTool(t, "object_find", map[string]any{"path": old}); find["status"] != "error" {
		t.Error("old name should no longer exist")
	}
}

func TestMCP_ObjectSelect(t *testing.T) {
	c := shared
	saveIfDirty(t, c)

	name := testName("Select")
	c.callTool(t, "object_create", map[string]any{"name": name})
	defer c.callTool(t, "object_delete", map[string]any{"path": name})

	p := c.callTool(t, "object_select", map[string]any{"paths": name})
	if p["status"] != "ok" {
		t.Fatalf("object_select failed: %v", p)
	}
}

func TestMCP_ObjectCreate_Primitive(t *testing.T) {
	c := shared
	saveIfDirty(t, c)

	name := testName("Cube")
	p := c.callTool(t, "object_create", map[string]any{"name": name, "primitive": "Cube"})
	defer c.callTool(t, "object_delete", map[string]any{"path": name})

	if p["status"] != "ok" {
		t.Fatalf("object_create primitive failed: %v", p)
	}
	find := c.callTool(t, "object_find", map[string]any{"path": name})
	obj, _ := find["object"].(map[string]any)
	comps, _ := obj["components"].([]any)
	hasMesh := false
	for _, comp := range comps {
		if comp == "MeshRenderer" || comp == "MeshFilter" {
			hasMesh = true
		}
	}
	if !hasMesh {
		t.Errorf("expected MeshRenderer/MeshFilter on primitive, components: %v", comps)
	}
}

// ── Components ────────────────────────────────────────────────────────────────

func TestMCP_ComponentGet(t *testing.T) {
	c := shared
	saveIfDirty(t, c)

	name := testName("CompGet")
	c.callTool(t, "object_create", map[string]any{"name": name})
	defer c.callTool(t, "object_delete", map[string]any{"path": name})

	p := c.callTool(t, "component_get", map[string]any{"path": name, "component": "Transform"})
	if p["status"] != "ok" {
		t.Fatalf("component_get failed: %v", p)
	}
	fields, _ := p["fields"].(map[string]any)
	if fields["m_LocalPosition"] == nil {
		t.Error("missing m_LocalPosition in Transform fields")
	}
}

func TestMCP_ComponentAdd(t *testing.T) {
	c := shared
	saveIfDirty(t, c)

	name := testName("CompAdd")
	c.callTool(t, "object_create", map[string]any{"name": name})
	defer c.callTool(t, "object_delete", map[string]any{"path": name})

	p := c.callTool(t, "component_add", map[string]any{"path": name, "type": "AudioSource"})
	if p["status"] != "ok" {
		t.Fatalf("component_add failed: %v", p)
	}
	find := c.callTool(t, "object_find", map[string]any{"path": name})
	comps, _ := find["object"].(map[string]any)["components"].([]any)
	found := false
	for _, comp := range comps {
		if comp == "AudioSource" {
			found = true
		}
	}
	if !found {
		t.Errorf("AudioSource not in components after add: %v", comps)
	}
}

func TestMCP_ComponentSet(t *testing.T) {
	c := shared
	saveIfDirty(t, c)

	name := testName("CompSet")
	c.callTool(t, "object_create", map[string]any{"name": name})
	defer c.callTool(t, "object_delete", map[string]any{"path": name})

	p := c.callTool(t, "component_set", map[string]any{
		"path":      name,
		"component": "Transform",
		"field":     "m_LocalPosition",
		"value":     map[string]any{"x": 3.0, "y": 6.0, "z": 9.0},
	})
	if p["status"] != "ok" {
		t.Fatalf("component_set failed: %v", p)
	}
	get := c.callTool(t, "component_get", map[string]any{"path": name, "component": "Transform"})
	pos, _ := get["fields"].(map[string]any)["m_LocalPosition"].(map[string]any)
	if fmt.Sprint(pos["x"]) != "3" {
		t.Errorf("expected x=3, got %v", pos["x"])
	}
}

// ── Assets ────────────────────────────────────────────────────────────────────

func TestMCP_AssetFind(t *testing.T) {
	c := shared

	p := c.callTool(t, "asset_find", map[string]any{"filter": "t:Script", "limit": 10})
	if p["status"] != "ok" {
		t.Fatalf("asset_find failed: %v", p)
	}
	assets, _ := p["assets"].([]any)
	if len(assets) == 0 {
		t.Error("expected at least one script asset")
	}
	first, _ := assets[0].(map[string]any)
	if first["guid"] == nil || first["path"] == nil {
		t.Errorf("asset entry missing guid or path: %v", first)
	}
}

// ── Editor ────────────────────────────────────────────────────────────────────

func TestMCP_ConsoleLogs(t *testing.T) {
	c := shared

	p := c.callTool(t, "console_logs", map[string]any{"limit": 10})
	if p["status"] != "ok" {
		t.Errorf("expected ok, got %v", p["status"])
	}
	if p["logs"] == nil {
		t.Error("missing logs")
	}
}

func TestMCP_PlayMode_Query(t *testing.T) {
	c := shared

	p := c.callTool(t, "play_mode", map[string]any{"action": "status"})
	if p["status"] != "ok" {
		t.Fatalf("play_mode status failed: %v", p)
	}
	if p["playing"] == nil {
		t.Error("missing playing field")
	}
}
