package main

import (
	"testing"
)

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

func TestMCP_AssetCreateFolder(t *testing.T) {
	c := shared

	p := c.callTool(t, "asset_create", map[string]any{"type": "folder", "path": "Assets/MCP_TestFolder"})
	defer c.callTool(t, "asset_delete", map[string]any{"path": "Assets/MCP_TestFolder"})

	if p["status"] != "ok" {
		t.Fatalf("asset_create folder failed: %v", p)
	}
	if p["path"] == nil {
		t.Error("missing path in response")
	}
}

func TestMCP_AssetCreateMaterial(t *testing.T) {
	c := shared

	p := c.callTool(t, "asset_create", map[string]any{"type": "material", "path": "Assets/MCP_TestMat.mat"})
	defer c.callTool(t, "asset_delete", map[string]any{"path": "Assets/MCP_TestMat.mat"})

	if p["status"] != "ok" {
		t.Fatalf("asset_create material failed: %v", p)
	}
	guid, _ := p["guid"].(string)
	if guid == "" {
		t.Error("missing or empty guid in response")
	}
}

func TestMCP_AssetDeleteMissing(t *testing.T) {
	c := shared

	p := c.callTool(t, "asset_delete", map[string]any{"path": "Assets/DoesNotExist_MCP.mat"})
	if p["status"] != "error" {
		t.Errorf("expected error for missing asset, got %v", p["status"])
	}
}

func TestMCP_AssetMove(t *testing.T) {
	c := shared

	c.callTool(t, "asset_create", map[string]any{"type": "folder", "path": "Assets/MCP_MoveFrom"})
	defer c.callTool(t, "asset_delete", map[string]any{"path": "Assets/MCP_MoveTo"})

	p := c.callTool(t, "asset_move", map[string]any{"from": "Assets/MCP_MoveFrom", "to": "Assets/MCP_MoveTo"})
	if p["status"] != "ok" {
		t.Fatalf("asset_move failed: %v", p)
	}
}

func TestMCP_AssetCopy(t *testing.T) {
	c := shared

	c.callTool(t, "asset_create", map[string]any{"type": "folder", "path": "Assets/MCP_CopySrc"})
	defer c.callTool(t, "asset_delete", map[string]any{"path": "Assets/MCP_CopySrc"})
	defer c.callTool(t, "asset_delete", map[string]any{"path": "Assets/MCP_CopyDst"})

	p := c.callTool(t, "asset_copy", map[string]any{"from": "Assets/MCP_CopySrc", "to": "Assets/MCP_CopyDst"})
	if p["status"] != "ok" {
		t.Fatalf("asset_copy failed: %v", p)
	}
}
