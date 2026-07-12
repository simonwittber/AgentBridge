package main

import (
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

	set := c.callTool(t, "editor_pref_set", map[string]any{"key": "MCP_Test_Key", "value": "hello_mcp", "type": "string"})
	if set["status"] != "ok" {
		t.Fatalf("editor_pref_set failed: %v", set)
	}

	get := c.callTool(t, "editor_pref_get", map[string]any{"key": "MCP_Test_Key", "type": "string"})
	if get["status"] != "ok" {
		t.Fatalf("editor_pref_get failed: %v", get)
	}
	if get["value"] != "hello_mcp" {
		t.Errorf("expected value %q, got %v", "hello_mcp", get["value"])
	}
}

func TestMCP_EditorPrefSetGetInt(t *testing.T) {
	c := shared

	set := c.callTool(t, "editor_pref_set", map[string]any{"key": "MCP_Test_Int", "value": "42", "type": "int"})
	if set["status"] != "ok" {
		t.Fatalf("editor_pref_set failed: %v", set)
	}

	get := c.callTool(t, "editor_pref_get", map[string]any{"key": "MCP_Test_Int", "type": "int"})
	if get["status"] != "ok" {
		t.Fatalf("editor_pref_get failed: %v", get)
	}
	if get["value"] != float64(42) {
		t.Errorf("expected value 42, got %v", get["value"])
	}
}

func TestMCP_PlayerSettingsGet(t *testing.T) {
	c := shared

	p := c.callTool(t, "player_settings_get", map[string]any{})
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

	set := c.callTool(t, "player_settings_set", map[string]any{"key": "bundleVersion", "value": "1.2.3-mcp-test"})
	if set["status"] != "ok" {
		t.Fatalf("player_settings_set failed: %v", set)
	}
	defer c.callTool(t, "player_settings_set", map[string]any{"key": "bundleVersion", "value": "0.1"})

	get := c.callTool(t, "player_settings_get", map[string]any{})
	if get["status"] != "ok" {
		t.Fatalf("player_settings_get failed: %v", get)
	}
	if get["bundleVersion"] != "1.2.3-mcp-test" {
		t.Errorf("expected bundleVersion %q, got %v", "1.2.3-mcp-test", get["bundleVersion"])
	}
}

func TestMCP_SelectionGet(t *testing.T) {
	c := shared

	p := c.callTool(t, "selection_get", map[string]any{})
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
