package main

import (
	"testing"
)

func TestMCP_Status(t *testing.T) {
	c := shared

	p := c.callTool(t, "status", map[string]any{})
	if p["status"] != "ok" {
		t.Errorf("expected ok, got %v", p["status"])
	}
	if p["uptime_s"] == nil {
		t.Error("missing uptime_s")
	}
	if p["busy"] == nil {
		t.Error("missing busy")
	}
}

func TestMCP_Commands(t *testing.T) {
	c := shared

	p := c.callTool(t, "commands", map[string]any{})
	if p["status"] != "ok" {
		t.Errorf("expected ok, got %v", p["status"])
	}
	cmds, _ := p["commands"].([]any)
	if len(cmds) == 0 {
		t.Error("expected at least one command")
	}
}

func TestMCP_Compile(t *testing.T) {
	c := shared

	p := c.callTool(t, "compile", map[string]any{})
	if p["status"] != "ok" {
		t.Errorf("expected ok, got %v", p["status"])
	}
	if p["errors"] == nil {
		t.Error("missing errors array")
	}
}

func TestMCP_Refresh(t *testing.T) {
	c := shared

	p := c.callTool(t, "refresh", map[string]any{})
	if p["status"] != "ok" {
		t.Errorf("expected ok, got %v", p["status"])
	}
}

func TestMCP_UUID(t *testing.T) {
	c := shared

	p := c.invokeCmd(t, "uuid", map[string]any{})
	if p["status"] != "ok" {
		t.Errorf("expected ok, got %v", p["status"])
	}
	uid, _ := p["uuid"].(string)
	if len(uid) != 36 {
		t.Errorf("expected 36-char UUID, got %q", uid)
	}
}
