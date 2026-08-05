package main

import (
	"testing"
	"time"
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

	// Unity API must be wrapped in MT.Run — scripts run on a background thread.
	p := c.invokeCmd(t, "execute_script", map[string]any{
		"code": "return MT.Run(() => UnityEngine.Application.productName);",
	})
	if p["status"] != "ok" {
		t.Fatalf("execute_script Unity API call failed: %v", p)
	}
}

func TestMCP_ExecuteScript_DebugLogCaptured(t *testing.T) {
	c := shared

	p := c.invokeCmd(t, "execute_script", map[string]any{
		"code": `MT.Run(() => { UnityEngine.Debug.Log("captured"); return 0; }); return null;`,
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

// ── notifications ─────────────────────────────────────────────────────────────

func TestMCP_ExecuteScript_FiresScriptStartedNotification(t *testing.T) {
	c := shared
	c.drainNotifications()

	done := make(chan map[string]any, 1)
	go func() {
		done <- c.waitTypedNotification(t, "script_started", 30*time.Second)
	}()

	p := c.invokeCmd(t, "execute_script", map[string]any{
		"code": `return "ok";`,
	})
	if p["status"] != "ok" {
		t.Fatalf("execute_script failed: %v", p)
	}

	select {
	case n := <-done:
		if n == nil {
			t.Error("expected script_started notification")
		}
	case <-time.After(5 * time.Second):
		t.Error("script_started notification did not arrive")
	}
}

func TestMCP_ExecuteScript_FiresScriptCompletedNotification(t *testing.T) {
	c := shared
	c.drainNotifications()

	done := make(chan map[string]any, 1)
	go func() {
		done <- c.waitTypedNotification(t, "script_completed", 30*time.Second)
	}()

	p := c.invokeCmd(t, "execute_script", map[string]any{
		"code": `return "done";`,
	})
	if p["status"] != "ok" {
		t.Fatalf("execute_script failed: %v", p)
	}

	select {
	case n := <-done:
		params, _ := n["params"].(map[string]any)
		data, _ := params["data"].(map[string]any)
		if data["type"] != "script_completed" {
			t.Errorf("unexpected notification type: %v", data["type"])
		}
		inner, _ := data["data"].(map[string]any)
		if rv, _ := inner["returnValue"].(string); rv != "done" {
			t.Errorf("expected returnValue 'done', got %q", rv)
		}
	case <-time.After(5 * time.Second):
		t.Error("script_completed notification did not arrive")
	}
}

func TestMCP_ExecuteScript_FiresScriptFailedNotification(t *testing.T) {
	c := shared
	c.drainNotifications()

	done := make(chan map[string]any, 1)
	go func() {
		done <- c.waitTypedNotification(t, "script_failed", 30*time.Second)
	}()

	p := c.invokeCmd(t, "execute_script", map[string]any{
		"code": `throw new System.Exception("intentional");`,
	})
	if p["status"] != "error" {
		t.Fatalf("expected error status, got %v", p["status"])
	}

	select {
	case n := <-done:
		params, _ := n["params"].(map[string]any)
		data, _ := params["data"].(map[string]any)
		if data["type"] != "script_failed" {
			t.Errorf("unexpected notification type: %v", data["type"])
		}
		inner, _ := data["data"].(map[string]any)
		if msg, _ := inner["message"].(string); msg == "" {
			t.Error("expected non-empty message in script_failed data")
		}
	case <-time.After(5 * time.Second):
		t.Error("script_failed notification did not arrive")
	}
}

func TestMCP_ExecuteScript_MainThreadRun_AccessesUnityAPI(t *testing.T) {
	c := shared

	p := c.invokeCmd(t, "execute_script", map[string]any{
		"code": "return MT.Run(() => UnityEngine.Application.unityVersion);",
	})
	if p["status"] != "ok" {
		t.Fatalf("execute_script with MT.Run failed: %v", p)
	}
	rv, _ := p["returnValue"].(string)
	if rv == "" {
		t.Error("expected non-empty unityVersion")
	}
}
