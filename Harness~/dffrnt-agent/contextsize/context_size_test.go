// Package contextsize measures the MCP tools/list payload size.
// Run with: go test -v ./contextsize/
// No Unity session required.
package contextsize

import (
	"encoding/json"
	"fmt"
	"os"
	"sort"
	"strings"
	"testing"
)

// coreMCPTools mirrors the set registered in serve.go.
// Keep in sync when adding or removing tools.
var coreMCPTools = map[string]bool{
	"status": true, "compile": true, "refresh": true, "focus": true,
	"commands": true, "console_logs": true,
	"hierarchy": true, "object_find": true, "objects_find": true,
	"object_create": true, "object_delete": true, "object_active": true,
	"object_rename": true, "object_select": true,
	"component_get": true, "component_set": true, "component_add": true,
	"scene_info": true, "scene_open": true, "scene_save": true, "scene_new": true,
	"asset_write_text": true, "asset_create": true, "asset_delete": true,
	"asset_move": true, "asset_copy": true, "asset_find": true,
	"undo": true, "redo": true, "play_mode": true, "run_tests": true,
}

type mcpProperty struct {
	Type        string `json:"type"`
	Description string `json:"description,omitempty"`
}

type mcpInputSchema struct {
	Type       string                 `json:"type"`
	Properties map[string]mcpProperty `json:"properties,omitempty"`
}

type mcpTool struct {
	Name        string         `json:"name"`
	Description string         `json:"description,omitempty"`
	InputSchema mcpInputSchema `json:"inputSchema"`
}

func argTypeToMCPType(t string) string {
	switch strings.ToLower(t) {
	case "int", "float":
		return "number"
	case "bool":
		return "boolean"
	default:
		return "string"
	}
}

// TestMCPContextSize reports the estimated LLM context cost of the MCP
// tools/list payload. Estimated tokens = bytes / 4.
// Run with -v to see the full per-tool breakdown.
func TestMCPContextSize(t *testing.T) {
	data, err := os.ReadFile("../agent_schema.json")
	if err != nil {
		t.Fatalf("read agent_schema.json: %v", err)
	}
	var cmds []map[string]any
	if err := json.Unmarshal(data, &cmds); err != nil {
		t.Fatalf("parse agent_schema.json: %v", err)
	}

	// Hardcoded tools registered directly in serve.go.
	tools := []mcpTool{
		{
			Name:        "set_project",
			Description: "Set the Unity project path for this session.",
			InputSchema: mcpInputSchema{
				Type: "object",
				Properties: map[string]mcpProperty{
					"path": {Type: "string", Description: "Path to the Unity project root"},
				},
			},
		},
		{
			Name:        "invoke",
			Description: "Call any Unity command by name. Use 'commands' to list all available commands.",
			InputSchema: mcpInputSchema{
				Type: "object",
				Properties: map[string]mcpProperty{
					"cmd":  {Type: "string", Description: "Command name"},
					"args": {Type: "string", Description: "JSON object of arguments"},
				},
			},
		},
	}

	for _, cmd := range cmds {
		name, _ := cmd["cmd"].(string)
		if name == "" || !coreMCPTools[name] {
			continue
		}
		desc, _ := cmd["description"].(string)
		tool := mcpTool{
			Name:        name,
			Description: desc,
			InputSchema: mcpInputSchema{Type: "object"},
		}
		if rawArgs, _ := cmd["args"].([]any); len(rawArgs) > 0 {
			tool.InputSchema.Properties = make(map[string]mcpProperty, len(rawArgs))
			for _, ra := range rawArgs {
				argMap, ok := ra.(map[string]any)
				if !ok {
					continue
				}
				argName, _ := argMap["name"].(string)
				if argName == "" {
					continue
				}
				argType, _ := argMap["type"].(string)
				argDesc, _ := argMap["description"].(string)
				tool.InputSchema.Properties[argName] = mcpProperty{
					Type:        argTypeToMCPType(argType),
					Description: argDesc,
				}
			}
		}
		tools = append(tools, tool)
	}

	type row struct {
		name  string
		bytes int
	}
	rows := make([]row, 0, len(tools))
	total := 0
	for _, tool := range tools {
		b, _ := json.Marshal(tool)
		rows = append(rows, row{tool.Name, len(b)})
		total += len(b)
	}
	sort.Slice(rows, func(i, j int) bool { return rows[i].bytes > rows[j].bytes })

	t.Log("=== MCP context size ===")
	t.Logf("%-25s  %6s  %7s", "tool", "bytes", "~tokens")
	t.Logf("%s", strings.Repeat("-", 43))
	for _, r := range rows {
		t.Logf("%-25s  %6d  %7d", r.name, r.bytes, r.bytes/4)
	}
	t.Logf("%s", strings.Repeat("-", 43))
	t.Logf("%-25s  %6d  %7d",
		fmt.Sprintf("TOTAL (%d tools)", len(tools)), total, total/4)
}
