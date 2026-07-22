package main

import (
	"testing"
)

func TestMCP_ExecuteScript_MissingCode(t *testing.T) {
	c := shared

	p := c.invokeCmd(t, "execute_script", map[string]any{})
	if p["status"] != "error" {
		t.Errorf("expected error for missing code, got %v", p["status"])
	}
}

func TestMCP_ExecuteScript_ReturnString(t *testing.T) {
	c := shared

	p := c.invokeCmd(t, "execute_script", map[string]any{
		"code": `return "hello world";`,
	})
	if p["status"] != "ok" {
		t.Fatalf("execute_script failed: %v", p)
	}
	val, _ := p["returnValue"].(string)
	if val != "hello world" {
		t.Errorf("expected 'hello world', got %q", val)
	}
}

func TestMCP_ExecuteScript_ReturnNull(t *testing.T) {
	c := shared

	p := c.invokeCmd(t, "execute_script", map[string]any{
		"code": "return null;",
	})
	if p["status"] != "ok" {
		t.Fatalf("execute_script return null failed: %v", p)
	}
	if _, hasKey := p["returnValue"]; !hasKey {
		t.Error("expected returnValue field")
	}
}

func TestMCP_ExecuteScript_SyntaxError(t *testing.T) {
	c := shared

	p := c.invokeCmd(t, "execute_script", map[string]any{
		"code": "not valid C# !!!",
	})
	if p["status"] != "error" {
		t.Errorf("expected error for syntax error, got %v", p["status"])
	}
	if p["message"] == nil {
		t.Error("expected message field in error response")
	}
}

func TestMCP_ExecuteScript_RuntimeException(t *testing.T) {
	c := shared

	p := c.invokeCmd(t, "execute_script", map[string]any{
		"code": `throw new System.Exception("boom");`,
	})
	if p["status"] != "error" {
		t.Errorf("expected error for runtime exception, got %v", p["status"])
	}
	msg, _ := p["message"].(string)
	if msg == "" {
		t.Error("expected non-empty message")
	}
}

func TestMCP_ExecuteScript_UnityApiCall(t *testing.T) {
	c := shared

	p := c.invokeCmd(t, "execute_script", map[string]any{
		"code": "return UnityEngine.Application.productName;",
	})
	if p["status"] != "ok" {
		t.Fatalf("execute_script Unity API call failed: %v", p)
	}
}

func TestMCP_ExecuteScript_DebugLogCaptured(t *testing.T) {
	c := shared

	p := c.invokeCmd(t, "execute_script", map[string]any{
		"code": `UnityEngine.Debug.Log("captured"); return null;`,
	})
	if p["status"] != "ok" {
		t.Fatalf("execute_script debug log failed: %v", p)
	}
	logs, _ := p["logs"].([]any)
	if logs == nil {
		t.Fatal("expected logs field")
	}
	found := false
	for _, entry := range logs {
		if m, ok := entry.(map[string]any); ok {
			if msg, _ := m["message"].(string); len(msg) > 0 {
				found = true
				break
			}
		}
	}
	if !found {
		t.Error("expected at least one log entry")
	}
}
