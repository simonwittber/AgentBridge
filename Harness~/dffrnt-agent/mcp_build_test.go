package main

import (
	"testing"
)

func TestMCP_Build_InvalidTarget(t *testing.T) {
	c := shared

	p := c.callTool(t, "build", map[string]any{"target": "InvalidPlatformXYZ", "output": "Temp/build_test"})
	if p["status"] != "error" {
		t.Errorf("expected error for invalid build target, got %v", p["status"])
	}
}

func TestMCP_Build_MissingOutput(t *testing.T) {
	c := shared

	p := c.callTool(t, "build", map[string]any{"target": "StandaloneWindows64", "output": ""})
	if p["status"] != "error" {
		t.Errorf("expected error for missing output path, got %v", p["status"])
	}
}
