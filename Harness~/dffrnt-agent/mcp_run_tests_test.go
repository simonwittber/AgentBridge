package main

import (
	"testing"
)

func TestMCP_RunTests_EditMode(t *testing.T) {
	c := shared

	// Run only the AgentBridge edit-mode tests — scoped to keep the run fast.
	p := c.callTool(t, "run_tests", map[string]any{
		"mode":     "EditMode",
		"assembly": "com.dffrnt.llm-dev-tools.tests.Editor",
	})

	// Status is "ok" (all pass) or "error" (some fail) — both are valid responses.
	status, _ := p["status"].(string)
	if status != "ok" && status != "error" {
		t.Fatalf("unexpected run_tests status %q: %v", status, p)
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

func TestMCP_RunTests_ByFilter(t *testing.T) {
	c := shared

	// Run a single known test by full name.
	p := c.callTool(t, "run_tests", map[string]any{
		"mode":   "EditMode",
		"filter": "LLMDevTools.Tests.SceneBridgeCommandTests.SceneInfo_ReturnsOk",
	})

	status, _ := p["status"].(string)
	if status != "ok" && status != "error" {
		t.Fatalf("unexpected run_tests status %q: %v", status, p)
	}
	total, _ := p["total"].(float64)
	if total == 0 {
		t.Error("expected at least one test result when filtering by exact name")
	}
}
