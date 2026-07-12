package main

import (
	"testing"
)

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
