package main

import (
	"strings"
	"testing"

	"github.com/mark3labs/mcp-go/mcp"
)

// resultText extracts the first text block from a CallToolResult.
func resultText(t *testing.T, r *mcp.CallToolResult) string {
	t.Helper()
	if r == nil {
		t.Fatal("nil result")
	}
	if len(r.Content) == 0 {
		t.Fatal("result has no content blocks")
	}
	tc, ok := r.Content[0].(mcp.TextContent)
	if !ok {
		t.Fatalf("first content block is not text: %T", r.Content[0])
	}
	return tc.Text
}

func TestToolResultJSONNeverEmpty(t *testing.T) {
	cases := []struct {
		name string
		in   any
	}{
		{"nil", nil},
		{"empty map", map[string]any{}},
		{"empty slice", []any{}},
		{"nil map", map[string]any(nil)},
	}
	for _, c := range cases {
		t.Run(c.name, func(t *testing.T) {
			got := resultText(t, toolResultJSON(c.in))
			if strings.TrimSpace(got) == "" {
				t.Fatalf("empty text for %s", c.name)
			}
			switch strings.TrimSpace(got) {
			case "null", "{}", "[]":
				t.Fatalf("degenerate text %q for %s", got, c.name)
			}
		})
	}
}

func TestToolResultJSONPassesThroughContent(t *testing.T) {
	got := resultText(t, toolResultJSON(map[string]any{"status": "ok", "value": 42}))
	if !strings.Contains(got, "\"status\"") || !strings.Contains(got, "\"value\"") {
		t.Fatalf("expected real content, got %q", got)
	}
}
