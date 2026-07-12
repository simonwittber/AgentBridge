package main

import (
	"testing"
)

func TestMCP_MaterialGet(t *testing.T) {
	c := shared

	path := "Assets/MCP_Test_Mat.mat"
	c.callTool(t, "asset_create", map[string]any{"type": "material", "path": path})
	defer c.callTool(t, "asset_delete", map[string]any{"path": path})

	p := c.callTool(t, "material_get", map[string]any{"path": path})
	if p["status"] != "ok" {
		t.Fatalf("material_get failed: %v", p)
	}
	if p["shader"] == nil {
		t.Error("missing shader field")
	}
	if p["properties"] == nil {
		t.Error("missing properties field")
	}
}

func TestMCP_MaterialGet_Missing(t *testing.T) {
	c := shared

	p := c.callTool(t, "material_get", map[string]any{"path": "Assets/DoesNotExist_MCP.mat"})
	if p["status"] != "error" {
		t.Errorf("expected error for missing material, got %v", p["status"])
	}
}

func TestMCP_MaterialSet(t *testing.T) {
	c := shared

	path := "Assets/MCP_Test_MatSet.mat"
	c.callTool(t, "asset_create", map[string]any{"type": "material", "path": path})
	defer c.callTool(t, "asset_delete", map[string]any{"path": path})

	// Standard shader has _Color property.
	p := c.callTool(t, "material_set", map[string]any{
		"path":     path,
		"property": "_Color",
		"value":    map[string]any{"r": 1.0, "g": 0.0, "b": 0.5, "a": 1.0},
	})
	if p["status"] != "ok" {
		t.Fatalf("material_set failed: %v", p)
	}

	// Verify the change.
	get := c.callTool(t, "material_get", map[string]any{"path": path})
	if get["status"] != "ok" {
		t.Fatalf("material_get after set failed: %v", get)
	}
	props, _ := get["properties"].(map[string]any)
	color, _ := props["_Color"].(map[string]any)
	val, _ := color["value"].(map[string]any)
	if val["r"] != float64(1) {
		t.Errorf("expected _Color.r=1, got %v", val["r"])
	}
}

func TestMCP_MaterialSet_InvalidProperty(t *testing.T) {
	c := shared

	path := "Assets/MCP_Test_MatSetBad.mat"
	c.callTool(t, "asset_create", map[string]any{"type": "material", "path": path})
	defer c.callTool(t, "asset_delete", map[string]any{"path": path})

	p := c.callTool(t, "material_set", map[string]any{
		"path":     path,
		"property": "_NonExistentProp_XYZ",
		"value":    1.0,
	})
	if p["status"] != "error" {
		t.Errorf("expected error for invalid property, got %v", p["status"])
	}
}
