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

func TestMCP_SceneInfo_AllFields(t *testing.T) {
	c := shared

	p := c.callTool(t, "scene_info", map[string]any{})
	if p["status"] != "ok" {
		t.Fatalf("scene_info failed: %v", p)
	}
	if _, ok := p["dirty"].(bool); !ok {
		t.Errorf("expected dirty to be a bool, got %T (%v)", p["dirty"], p["dirty"])
	}
	// path is empty string for unsaved scenes, so check the key exists.
	if _, exists := p["path"]; !exists {
		t.Error("missing path field")
	}
	if _, ok := p["root_count"].(float64); !ok {
		t.Errorf("expected root_count to be a number, got %T (%v)", p["root_count"], p["root_count"])
	}
}
