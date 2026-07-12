package main

import (
	"testing"
)

func TestMCP_MenuItem_Valid(t *testing.T) {
	c := shared

	// Window/General/Inspector is always present and safe to invoke.
	p := c.callTool(t, "menu_item", map[string]any{"path": "Window/General/Inspector"})
	if p["status"] != "ok" {
		t.Fatalf("menu_item failed for valid path: %v", p)
	}
	if p["path"] == nil {
		t.Error("missing path in response")
	}
}

func TestMCP_MenuItem_Invalid(t *testing.T) {
	c := shared

	p := c.callTool(t, "menu_item", map[string]any{"path": "DoesNotExist/FakeMenu/Item"})
	if p["status"] != "error" {
		t.Errorf("expected error for invalid menu path, got %v", p["status"])
	}
}

func TestMCP_MenuItem_MissingPath(t *testing.T) {
	c := shared

	p := c.callTool(t, "menu_item", map[string]any{})
	if p["status"] != "error" {
		t.Errorf("expected error for missing path, got %v", p["status"])
	}
}
