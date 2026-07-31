package main

import (
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"testing"
	"time"
)

func TestMCP_Notification_PlayMode(t *testing.T) {
	c := shared

	// Start in edit mode.
	c.callTool(t, "play_exit", map[string]any{})

	c.callTool(t, "play_enter", map[string]any{})
	c.waitTypedNotification(t, "play_mode_entered", 10*time.Second)

	c.callTool(t, "play_exit", map[string]any{})
	c.waitTypedNotification(t, "play_mode_exited", 10*time.Second)
}

func TestMCP_Notification_SceneOpened(t *testing.T) {
	c := shared
	saveIfDirty(t, c)

	// Save a scene to disk so we have a path to open.
	c.callTool(t, "scene_new", map[string]any{"setup": "empty"})
	savePath := "Assets/Scenes/MCP_Notif_Scene.unity"
	c.callTool(t, "scene_save", map[string]any{"path": savePath})
	defer c.callTool(t, "asset_delete", map[string]any{"path": savePath})

	// Open the scene; sceneOpened fires synchronously during scene_open.
	c.callTool(t, "scene_open", map[string]any{"path": savePath})
	n := c.waitTypedNotification(t, "scene_opened", 3*time.Second)

	// params["data"] is the top-level payload {"type":..., "data":{...}, "written_at":...}.
	// The scene fields are in the nested "data" key.
	params, _ := n["params"].(map[string]any)
	outer, _ := params["data"].(map[string]any)
	inner, _ := outer["data"].(map[string]any)
	if inner == nil {
		t.Fatalf("scene_opened notification missing inner data: %v", outer)
	}
	if inner["name"] == nil {
		t.Error("scene_opened notification missing name")
	}
	if inner["path"] == nil {
		t.Error("scene_opened notification missing path")
	}
	if inner["mode"] == nil {
		t.Error("scene_opened notification missing mode")
	}
}

func TestMCP_Notification_DeliveredToClient(t *testing.T) {
	c := shared

	dir := filepath.Join(testProject, "Temp", "agent", "notifications")
	if err := os.MkdirAll(dir, 0755); err != nil {
		t.Fatalf("create notifications dir: %v", err)
	}

	ts := time.Now().UnixMilli()
	payload := map[string]any{
		"type":       "test_event",
		"data":       map[string]any{"hello": "world"},
		"written_at": ts,
	}
	data, _ := json.Marshal(payload)
	name := fmt.Sprintf("%020d-testnotif.json", ts)
	if err := os.WriteFile(filepath.Join(dir, name), data, 0644); err != nil {
		t.Fatalf("write notification file: %v", err)
	}

	// The watcher polls every 500ms; give it 3s to deliver.
	// Use waitTypedNotification to skip compile_started/finished noise.
	n := c.waitTypedNotification(t, "test_event", 3*time.Second)

	params, _ := n["params"].(map[string]any)
	if params == nil {
		t.Fatalf("notification missing params: %v", n)
	}
	if logger, _ := params["logger"].(string); logger != "unity-agentbridge" {
		t.Errorf("expected logger=unity-agentbridge, got %q", logger)
	}
	inner, _ := params["data"].(map[string]any)
	if inner == nil {
		t.Fatalf("notification params missing data: %v", params)
	}
	if inner["type"] != "test_event" {
		t.Errorf("expected type=test_event, got %v", inner["type"])
	}
}
