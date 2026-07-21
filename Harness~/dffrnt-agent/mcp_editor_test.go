package main

import (
	"encoding/base64"
	"testing"
)

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

func TestMCP_Undo(t *testing.T) {
	c := shared
	saveIfDirty(t, c)

	name := testName("Undo")
	c.callTool(t, "object_create", map[string]any{"name": name})

	u := c.callTool(t, "undo", map[string]any{})
	if u["status"] != "ok" {
		t.Fatalf("undo failed: %v", u)
	}

	find := c.callTool(t, "object_find", map[string]any{"path": name})
	if find["status"] != "error" {
		t.Error("expected object to be gone after undo")
	}
}

func TestMCP_Redo(t *testing.T) {
	c := shared
	saveIfDirty(t, c)

	name := testName("Redo")
	c.callTool(t, "object_create", map[string]any{"name": name})
	defer c.callTool(t, "object_delete", map[string]any{"path": name})

	u := c.callTool(t, "undo", map[string]any{})
	if u["status"] != "ok" {
		t.Fatalf("undo failed: %v", u)
	}

	r := c.callTool(t, "redo", map[string]any{})
	if r["status"] != "ok" {
		t.Fatalf("redo failed: %v", r)
	}

	find := c.callTool(t, "object_find", map[string]any{"path": name})
	if find["status"] != "ok" {
		t.Error("expected object to be present after redo")
	}
}

func TestMCP_EditorPrefSetGet(t *testing.T) {
	c := shared

	set := c.invokeCmd(t, "editor_pref_set", map[string]any{"key": "MCP_Test_Key", "value": "hello_mcp", "type": "string"})
	if set["status"] != "ok" {
		t.Fatalf("editor_pref_set failed: %v", set)
	}

	get := c.invokeCmd(t, "editor_pref_get", map[string]any{"key": "MCP_Test_Key", "type": "string"})
	if get["status"] != "ok" {
		t.Fatalf("editor_pref_get failed: %v", get)
	}
	if get["value"] != "hello_mcp" {
		t.Errorf("expected value %q, got %v", "hello_mcp", get["value"])
	}
}

func TestMCP_EditorPrefSetGetInt(t *testing.T) {
	c := shared

	set := c.invokeCmd(t, "editor_pref_set", map[string]any{"key": "MCP_Test_Int", "value": "42", "type": "int"})
	if set["status"] != "ok" {
		t.Fatalf("editor_pref_set failed: %v", set)
	}

	get := c.invokeCmd(t, "editor_pref_get", map[string]any{"key": "MCP_Test_Int", "type": "int"})
	if get["status"] != "ok" {
		t.Fatalf("editor_pref_get failed: %v", get)
	}
	if get["value"] != float64(42) {
		t.Errorf("expected value 42, got %v", get["value"])
	}
}

func TestMCP_PlayerSettingsGet(t *testing.T) {
	c := shared

	p := c.invokeCmd(t, "player_settings_get", map[string]any{})
	if p["status"] != "ok" {
		t.Fatalf("player_settings_get failed: %v", p)
	}
	if p["companyName"] == nil {
		t.Error("missing companyName")
	}
	if p["productName"] == nil {
		t.Error("missing productName")
	}
}

func TestMCP_PlayerSettingsSetGet(t *testing.T) {
	c := shared

	set := c.invokeCmd(t, "player_settings_set", map[string]any{"key": "bundleVersion", "value": "1.2.3-mcp-test"})
	if set["status"] != "ok" {
		t.Fatalf("player_settings_set failed: %v", set)
	}
	defer c.invokeCmd(t, "player_settings_set", map[string]any{"key": "bundleVersion", "value": "0.1"})

	get := c.invokeCmd(t, "player_settings_get", map[string]any{})
	if get["status"] != "ok" {
		t.Fatalf("player_settings_get failed: %v", get)
	}
	if get["bundleVersion"] != "1.2.3-mcp-test" {
		t.Errorf("expected bundleVersion %q, got %v", "1.2.3-mcp-test", get["bundleVersion"])
	}
}

func TestMCP_SelectionGet(t *testing.T) {
	c := shared

	p := c.invokeCmd(t, "selection_get", map[string]any{})
	if p["status"] != "ok" {
		t.Fatalf("selection_get failed: %v", p)
	}
	if p["gameObjects"] == nil {
		t.Error("missing gameObjects field")
	}
	if p["assets"] == nil {
		t.Error("missing assets field")
	}
}

func TestMCP_Screenshot(t *testing.T) {
	c := shared

	p := c.callTool(t, "screenshot", map[string]any{})
	if p["status"] != "ok" {
		t.Fatalf("screenshot failed: %v", p)
	}
	path, _ := p["path"].(string)
	if path == "" {
		t.Error("missing or empty path in screenshot response")
	}
}

func TestMCP_Screenshot_ReturnsInlineImage(t *testing.T) {
	c := shared

	resp := c.call(t, "tools/call", map[string]any{
		"name":      "screenshot",
		"arguments": map[string]any{},
	})
	if errVal, ok := resp["error"]; ok {
		t.Fatalf("MCP error calling screenshot: %v", errVal)
	}
	result, _ := resp["result"].(map[string]any)
	if result == nil {
		t.Fatalf("no result: %v", resp)
	}
	content, _ := result["content"].([]any)

	var imgEntry map[string]any
	for _, item := range content {
		m, _ := item.(map[string]any)
		if m["type"] == "image" {
			imgEntry = m
			break
		}
	}
	if imgEntry == nil {
		t.Fatal("no image content in screenshot result")
	}
	if imgEntry["mimeType"] != "image/png" {
		t.Errorf("expected mimeType image/png, got %v", imgEntry["mimeType"])
	}
	dataStr, _ := imgEntry["data"].(string)
	if dataStr == "" {
		t.Fatal("empty image data")
	}
	raw, err := base64.StdEncoding.DecodeString(dataStr)
	if err != nil {
		t.Fatalf("base64 decode: %v", err)
	}
	pngMagic := []byte{0x89, 'P', 'N', 'G'}
	if len(raw) < 4 || string(raw[:4]) != string(pngMagic) {
		t.Errorf("data is not a PNG (first 4 bytes: %x)", raw[:min(4, len(raw))])
	}
}

func min(a, b int) int {
	if a < b {
		return a
	}
	return b
}
