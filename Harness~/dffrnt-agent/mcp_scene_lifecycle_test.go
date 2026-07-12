package main

import (
	"testing"
)

func TestMCP_SceneNew(t *testing.T) {
	c := shared

	p := c.callTool(t, "scene_new", map[string]any{"setup": "empty"})
	if p["status"] != "ok" {
		t.Fatalf("scene_new failed: %v", p)
	}

	info := c.callTool(t, "scene_info", map[string]any{})
	if info["status"] != "ok" {
		t.Fatalf("scene_info after scene_new failed: %v", info)
	}
	rootCount, _ := info["root_count"].(float64)
	if rootCount != 0 {
		t.Errorf("expected empty scene to have 0 root objects, got %v", rootCount)
	}
}

func TestMCP_SceneNew_DefaultGameObjects(t *testing.T) {
	c := shared

	p := c.callTool(t, "scene_new", map[string]any{"setup": "defaultGameObjects"})
	if p["status"] != "ok" {
		t.Fatalf("scene_new defaultGameObjects failed: %v", p)
	}

	info := c.callTool(t, "scene_info", map[string]any{})
	rootCount, _ := info["root_count"].(float64)
	if rootCount == 0 {
		t.Error("expected defaultGameObjects scene to have at least one root object")
	}
}

func TestMCP_SceneSave(t *testing.T) {
	c := shared

	c.callTool(t, "scene_new", map[string]any{"setup": "empty"})

	savePath := "Assets/Scenes/MCP_Test_Scene.unity"
	p := c.callTool(t, "scene_save", map[string]any{"path": savePath})
	defer c.callTool(t, "asset_delete", map[string]any{"path": savePath})

	if p["status"] != "ok" {
		t.Fatalf("scene_save failed: %v", p)
	}
	if p["path"] == nil {
		t.Error("missing path in scene_save response")
	}
}

func TestMCP_SceneSave_InvalidPath(t *testing.T) {
	c := shared

	p := c.callTool(t, "scene_save", map[string]any{"path": "C:/outside/assets.unity"})
	if p["status"] != "error" {
		t.Errorf("expected error for path outside Assets/, got %v", p["status"])
	}
}

func TestMCP_SceneOpen(t *testing.T) {
	c := shared

	// Save a fresh scene to disk first, then re-open it.
	c.callTool(t, "scene_new", map[string]any{"setup": "empty"})
	savePath := "Assets/Scenes/MCP_Test_Open.unity"
	c.callTool(t, "scene_save", map[string]any{"path": savePath})
	defer c.callTool(t, "asset_delete", map[string]any{"path": savePath})

	// Open it.
	p := c.callTool(t, "scene_open", map[string]any{"path": savePath})
	if p["status"] != "ok" {
		t.Fatalf("scene_open failed: %v", p)
	}
	if p["scene_name"] == nil {
		t.Error("missing scene_name in scene_open response")
	}

	info := c.callTool(t, "scene_info", map[string]any{})
	name, _ := info["scene_name"].(string)
	if name != "MCP_Test_Open" {
		t.Errorf("expected active scene to be MCP_Test_Open, got %q", name)
	}
}

func TestMCP_SceneOpen_Missing(t *testing.T) {
	c := shared

	p := c.callTool(t, "scene_open", map[string]any{"path": "Assets/Scenes/DoesNotExist_MCP.unity"})
	if p["status"] != "error" {
		t.Errorf("expected error opening missing scene, got %v", p["status"])
	}
}
