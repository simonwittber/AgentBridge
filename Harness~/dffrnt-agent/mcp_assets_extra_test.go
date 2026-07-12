package main

import (
	"testing"
)

func TestMCP_AssetWriteText(t *testing.T) {
	c := shared

	path := "Assets/MCP_Test_WriteText.cs"
	content := "// MCP test script\npublic class MCP_Test_WriteText {}"

	p := c.callTool(t, "asset_write_text", map[string]any{"path": path, "content": content})
	defer c.callTool(t, "asset_delete", map[string]any{"path": path})

	if p["status"] != "ok" {
		t.Fatalf("asset_write_text failed: %v", p)
	}
	if p["path"] == nil {
		t.Error("missing path in response")
	}

	// Verify asset exists.
	info := c.callTool(t, "asset_info", map[string]any{"path": path})
	if info["status"] != "ok" {
		t.Errorf("asset_info after write failed: %v", info)
	}
}

func TestMCP_AssetWriteText_MissingPath(t *testing.T) {
	c := shared

	p := c.callTool(t, "asset_write_text", map[string]any{"content": "hello"})
	if p["status"] != "error" {
		t.Errorf("expected error for missing path, got %v", p["status"])
	}
}

func TestMCP_AssetInfo(t *testing.T) {
	c := shared

	// Create a text asset to inspect.
	path := "Assets/MCP_Test_AssetInfo.txt"
	c.callTool(t, "asset_write_text", map[string]any{"path": path, "content": "hello"})
	defer c.callTool(t, "asset_delete", map[string]any{"path": path})

	p := c.callTool(t, "asset_info", map[string]any{"path": path})
	if p["status"] != "ok" {
		t.Fatalf("asset_info failed: %v", p)
	}
	guid, _ := p["guid"].(string)
	if guid == "" {
		t.Error("missing or empty guid")
	}
	if p["importer_type"] == nil {
		t.Error("missing importer_type")
	}
	if p["settings"] == nil {
		t.Error("missing settings")
	}
}

func TestMCP_AssetInfo_Missing(t *testing.T) {
	c := shared

	p := c.callTool(t, "asset_info", map[string]any{"path": "Assets/DoesNotExist_MCP.txt"})
	if p["status"] != "error" {
		t.Errorf("expected error for missing asset, got %v", p["status"])
	}
}

func TestMCP_AssetSet(t *testing.T) {
	c := shared

	// Write a text asset — its importer exposes userData on all AssetImporters.
	path := "Assets/MCP_Test_AssetSet.txt"
	c.callTool(t, "asset_write_text", map[string]any{"path": path, "content": "data"})
	defer c.callTool(t, "asset_delete", map[string]any{"path": path})

	p := c.callTool(t, "asset_set", map[string]any{
		"path":  path,
		"field": "m_UserData",
		"value": "mcp-test-value",
	})
	if p["status"] != "ok" {
		t.Fatalf("asset_set failed: %v", p)
	}
}

func TestMCP_AssetSet_InvalidField(t *testing.T) {
	c := shared

	path := "Assets/MCP_Test_AssetSetBad.txt"
	c.callTool(t, "asset_write_text", map[string]any{"path": path, "content": "data"})
	defer c.callTool(t, "asset_delete", map[string]any{"path": path})

	p := c.callTool(t, "asset_set", map[string]any{
		"path":  path,
		"field": "m_NonExistentField_XYZ",
		"value": "irrelevant",
	})
	if p["status"] != "error" {
		t.Errorf("expected error for invalid field, got %v", p["status"])
	}
}
