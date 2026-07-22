package main

import (
	"testing"
)

func TestMCP_PlayEnterExit(t *testing.T) {
	c := shared

	// Ensure we start in edit mode.
	c.callTool(t, "play_exit", map[string]any{})

	enter := c.callTool(t, "play_enter", map[string]any{})
	if enter["status"] != "ok" {
		t.Fatalf("play_enter failed: %v", enter)
	}
	if playing, _ := enter["playing"].(bool); !playing {
		t.Error("expected playing=true after play_enter")
	}

	exit := c.callTool(t, "play_exit", map[string]any{})
	if exit["status"] != "ok" {
		t.Fatalf("play_exit failed: %v", exit)
	}
	if playing, _ := exit["playing"].(bool); playing {
		t.Error("expected playing=false after play_exit")
	}
}
