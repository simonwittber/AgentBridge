package main

import (
	"testing"
)

func TestMCP_RunEditorTests(t *testing.T) {
	c := shared

	p := c.callTool(t, "run_editor_tests", map[string]any{
		"assembly": "com.dffrnt.llm-dev-tools.tests.Editor",
	})

	status, _ := p["status"].(string)
	if status != "ok" && status != "error" {
		t.Fatalf("unexpected run_editor_tests status %q: %v", status, p)
	}
	if p["passed"] == nil {
		t.Error("missing passed count")
	}
	if p["failed"] == nil {
		t.Error("missing failed count")
	}
	if p["total"] == nil {
		t.Error("missing total count")
	}
	if p["failures"] == nil {
		t.Error("missing failures array")
	}
}

func TestMCP_RunEditorTests_ByFilter(t *testing.T) {
	c := shared

	p := c.callTool(t, "run_editor_tests", map[string]any{
		"filter": "LLMDevTools.Tests.SceneBridgeCommandTests.SceneInfo_ReturnsOk",
	})

	status, _ := p["status"].(string)
	if status != "ok" && status != "error" {
		t.Fatalf("unexpected run_editor_tests status %q: %v", status, p)
	}
	total, _ := p["total"].(float64)
	if total == 0 {
		t.Error("expected at least one test result when filtering by exact name")
	}
}
