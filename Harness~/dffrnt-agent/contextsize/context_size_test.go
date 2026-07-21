// Package contextsize estimates the MCP tools/list payload size.
// Run with: go test -v ./contextsize/
// No Unity session or schema file required.
package contextsize

import (
	"encoding/json"
	"fmt"
	"sort"
	"strings"
	"testing"
)

// fixedTools are registered directly in serve.go and are always present.
var fixedTools = []mcpTool{
	{
		Name:        "set_project",
		Description: "Set the Unity project path.",
		InputSchema: mcpInputSchema{
			Type:       "object",
			Properties: map[string]mcpProperty{"path": {Type: "string"}},
		},
	},
	{
		Name:        "invoke",
		Description: "Call any Unity command by name.",
		InputSchema: mcpInputSchema{
			Type: "object",
			Properties: map[string]mcpProperty{
				"cmd":  {Type: "string"},
				"args": {Type: "string"},
			},
		},
	},
	{
		Name:        "screenshot",
		Description: "Render scene view or main camera to a PNG.",
		InputSchema: mcpInputSchema{
			Type: "object",
			Properties: map[string]mcpProperty{
				"path":     {Type: "string"},
				"width":    {Type: "number"},
				"height":   {Type: "number"},
				"max_size": {Type: "number"},
			},
		},
	},
	{
		Name:        "help",
		Description: "Get full description and argument details for any command.",
		InputSchema: mcpInputSchema{
			Type: "object",
			Properties: map[string]mcpProperty{
				"command": {Type: "string", Description: "Command name to look up"},
			},
		},
	},
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

// TestMCPContextSize reports the estimated LLM context cost of the MCP
// tools/list payload for the fixed (always-registered) tools.
// Dynamic core tools registered from Unity are not counted here since they
// require a live session, but they follow the same shape.
// Estimated tokens = bytes / 4.
func TestMCPContextSize(t *testing.T) {
	type row struct {
		name  string
		bytes int
	}
	rows := make([]row, 0, len(fixedTools))
	total := 0
	for _, tool := range fixedTools {
		b, _ := json.Marshal(tool)
		rows = append(rows, row{tool.Name, len(b)})
		total += len(b)
	}
	sort.Slice(rows, func(i, j int) bool { return rows[i].bytes > rows[j].bytes })

	t.Log("=== MCP context size (fixed tools only) ===")
	t.Logf("%-25s  %6s  %7s", "tool", "bytes", "~tokens")
	t.Logf("%s", strings.Repeat("-", 43))
	for _, r := range rows {
		t.Logf("%-25s  %6d  %7d", r.name, r.bytes, r.bytes/4)
	}
	t.Logf("%s", strings.Repeat("-", 43))
	t.Logf("%-25s  %6d  %7d",
		fmt.Sprintf("TOTAL (%d tools)", len(fixedTools)), total, total/4)
	t.Logf("note: dynamic core tools from Unity are registered at runtime via the 'core' flag")
}
