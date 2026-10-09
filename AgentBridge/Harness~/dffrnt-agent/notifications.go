package main

import (
	"context"
	"encoding/json"
	"os"
	"path/filepath"
	"sort"
	"time"

	"github.com/mark3labs/mcp-go/mcp"
	"github.com/mark3labs/mcp-go/server"
)

// startNotificationWatcher polls the Unity notifications dir and forwards each
// file to connected MCP clients as a notifications/message. Runs until ctx is
// cancelled. Only active when a project is set (serve mode only).
func startNotificationWatcher(ctx context.Context, state *serveState, s *server.MCPServer) {
	go func() {
		ticker := time.NewTicker(500 * time.Millisecond)
		defer ticker.Stop()
		for {
			select {
			case <-ctx.Done():
				return
			case <-ticker.C:
			}

			cfg := state.getConfig()
			if cfg.Project == defaultProject {
				continue
			}

			dir := cfg.notificationsDir()
			entries, err := filepath.Glob(filepath.Join(dir, "*.json"))
			if err != nil || len(entries) == 0 {
				continue
			}
			sort.Strings(entries)

			for _, path := range entries {
				data, err := os.ReadFile(path)
				if err != nil {
					continue
				}
				os.Remove(path)

				var payload map[string]any
				if json.Unmarshal(data, &payload) != nil {
					continue
				}

				notifType, _ := payload["type"].(string)
				level := string(mcp.LoggingLevelWarning)
				if notifType == "compile_failure" {
					level = string(mcp.LoggingLevelError)
				}

				s.SendNotificationToAllClients(
					string(mcp.MethodNotificationMessage),
					map[string]any{
						"level":   level,
						"logger":  "unity-agentbridge",
						"message": notificationMessage(payload),
						"data":    payload,
					},
				)
			}
		}
	}()
}

// notificationMessage returns the human-readable text for a notification payload.
// MCP clients that surface notifications as conversation content emit an empty content block without it.
// Payloads from older AgentBridge versions have no message, so one is built from the type instead.
func notificationMessage(payload map[string]any) string {
	if msg, ok := payload["message"].(string); ok && msg != "" {
		return msg
	}
	if notifType, ok := payload["type"].(string); ok && notifType != "" {
		return "Unity notification: " + notifType
	}
	return "Unity notification"
}
