package main

import (
	"testing"
)

func TestMCP_PlayMode_EnterExit(t *testing.T) {
	c := shared

	// Verify we start in edit mode.
	status := c.callTool(t, "play_mode", map[string]any{"action": "status"})
	if playing, _ := status["playing"].(bool); playing {
		t.Skip("already in play mode — skipping enter/exit test")
	}

	// Enter play mode.
	enter := c.callTool(t, "play_mode", map[string]any{"action": "enter"})
	if enter["status"] != "ok" {
		t.Fatalf("play_mode enter failed: %v", enter)
	}

	// Verify we are now playing.
	afterEnter := c.callTool(t, "play_mode", map[string]any{"action": "status"})
	if playing, _ := afterEnter["playing"].(bool); !playing {
		t.Error("expected playing=true after entering play mode")
	}

	// Exit play mode.
	exit := c.callTool(t, "play_mode", map[string]any{"action": "exit"})
	if exit["status"] != "ok" {
		t.Fatalf("play_mode exit failed: %v", exit)
	}

	// Verify we are no longer playing.
	afterExit := c.callTool(t, "play_mode", map[string]any{"action": "status"})
	if playing, _ := afterExit["playing"].(bool); playing {
		t.Error("expected playing=false after exiting play mode")
	}
}

func TestMCP_PlayMode_UnknownActionFallsThrough(t *testing.T) {
	c := shared

	// Unknown actions fall through to the status query — should return ok with playing field.
	p := c.callTool(t, "play_mode", map[string]any{"action": "invalid_action_xyz"})
	if p["status"] != "ok" {
		t.Errorf("expected ok for unknown action (falls through to status), got %v", p["status"])
	}
	if p["playing"] == nil {
		t.Error("expected playing field in fallthrough response")
	}
}
