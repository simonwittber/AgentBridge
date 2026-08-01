package main

import (
	"testing"
)

func TestMCP_ProfilerStart_NoMarkers_ReturnsError(t *testing.T) {
	c := shared

	p := c.callTool(t, "profiler_start", map[string]any{})
	if p["status"] != "error" {
		t.Errorf("expected error for missing markers, got %v", p["status"])
	}
}

func TestMCP_ProfilerStart_WithMarkers(t *testing.T) {
	c := shared
	defer c.callTool(t, "profiler_clear", map[string]any{})

	p := c.callTool(t, "profiler_start", map[string]any{
		"markers": []any{"MyMarker"},
	})
	if p["status"] != "ok" {
		t.Fatalf("profiler_start failed: %v", p)
	}
	markers, _ := p["markers"].([]any)
	if len(markers) != 1 {
		t.Errorf("expected 1 marker, got %d", len(markers))
	}
}

func TestMCP_ProfilerStop(t *testing.T) {
	c := shared
	defer c.callTool(t, "profiler_clear", map[string]any{})

	c.callTool(t, "profiler_start", map[string]any{"markers": []any{"MyMarker"}})
	p := c.callTool(t, "profiler_stop", map[string]any{})
	if p["status"] != "ok" {
		t.Fatalf("profiler_stop failed: %v", p)
	}
}

func TestMCP_ProfilerClear(t *testing.T) {
	c := shared

	c.callTool(t, "profiler_start", map[string]any{"markers": []any{"MyMarker"}})
	p := c.callTool(t, "profiler_clear", map[string]any{})
	if p["status"] != "ok" {
		t.Fatalf("profiler_clear failed: %v", p)
	}
}

func TestMCP_ProfilerGetSamples_AfterStart(t *testing.T) {
	c := shared
	defer c.callTool(t, "profiler_clear", map[string]any{})

	c.callTool(t, "profiler_start", map[string]any{"markers": []any{"MyMarker"}})
	p := c.callTool(t, "profiler_get_samples", map[string]any{"marker": "MyMarker"})
	if p["status"] != "ok" {
		t.Fatalf("profiler_get_samples failed: %v", p)
	}
	if p["markers"] == nil {
		t.Error("expected markers field")
	}
}

func TestMCP_ProfilerGetSamples_MarkerFilter(t *testing.T) {
	c := shared
	defer c.callTool(t, "profiler_clear", map[string]any{})

	c.callTool(t, "profiler_start", map[string]any{
		"markers": []any{"MyMarker", "OtherMarker"},
	})
	p := c.callTool(t, "profiler_get_samples", map[string]any{"marker": "MyMarker"})
	if p["status"] != "ok" {
		t.Fatalf("profiler_get_samples with filter failed: %v", p)
	}
	markers, _ := p["markers"].([]any)
	if len(markers) != 1 {
		t.Errorf("expected 1 marker entry, got %d", len(markers))
	}
}

func TestMCP_ProfilerGetSamples_UnknownMarker(t *testing.T) {
	c := shared
	defer c.callTool(t, "profiler_clear", map[string]any{})

	c.callTool(t, "profiler_start", map[string]any{"markers": []any{"MyMarker"}})
	p := c.callTool(t, "profiler_get_samples", map[string]any{"marker": "NonExistentMarker_XYZ"})
	if p["status"] != "error" {
		t.Errorf("expected error for unknown marker, got %v", p["status"])
	}
}

func TestMCP_ProfilerGetSamples_WithRaw(t *testing.T) {
	c := shared
	defer c.callTool(t, "profiler_clear", map[string]any{})

	c.callTool(t, "profiler_start", map[string]any{"markers": []any{"MyMarker"}})
	p := c.callTool(t, "profiler_get_samples", map[string]any{
		"marker": "MyMarker",
		"raw":    true,
	})
	if p["status"] != "ok" {
		t.Fatalf("profiler_get_samples raw failed: %v", p)
	}
	markers, _ := p["markers"].([]any)
	if len(markers) != 1 {
		t.Fatalf("expected 1 marker entry, got %d", len(markers))
	}
	entry, _ := markers[0].(map[string]any)
	if entry["samples"] == nil {
		t.Error("expected samples field when raw=true")
	}
}

func TestMCP_ProfilerGetSamples_CapturesData(t *testing.T) {
	c := shared
	defer c.callTool(t, "profiler_clear", map[string]any{})

	const markerName = "AgentBridge.EditModeTest"

	c.callTool(t, "profiler_start", map[string]any{"markers": []any{markerName}})

	c.invokeCmd(t, "execute_script", map[string]any{
		"code": `
			var m = new Unity.Profiling.ProfilerMarker(Unity.Profiling.ProfilerCategory.Scripts, "AgentBridge.EditModeTest");
			for (int i = 0; i < 5; i++) { m.Begin(); m.End(); }
			return null;
		`,
	})

	p := c.callTool(t, "profiler_get_samples", map[string]any{"marker": markerName})
	if p["status"] != "ok" {
		t.Fatalf("profiler_get_samples failed: %v", p)
	}
	markers, _ := p["markers"].([]any)
	if len(markers) != 1 {
		t.Fatalf("expected 1 marker entry, got %d", len(markers))
	}
	entry, _ := markers[0].(map[string]any)
	if entry["valid"] != true {
		t.Errorf("expected valid=true, got %v", entry["valid"])
	}
	count, _ := entry["sampleCount"].(float64)
	if count < 5 {
		t.Errorf("expected sampleCount >= 5, got %v", count)
	}
}

func TestMCP_Profiler_CapturesDataInPlayMode(t *testing.T) {
	c := shared
	c.callTool(t, "play_enter", map[string]any{})
	defer func() {
		c.callTool(t, "play_exit", map[string]any{})
		c.callTool(t, "profiler_clear", map[string]any{})
	}()

	const markerName = "AgentBridge.PlayModeTest"

	c.callTool(t, "profiler_start", map[string]any{"markers": []any{markerName}})

	c.invokeCmd(t, "execute_script", map[string]any{
		"code": `
			var m = new Unity.Profiling.ProfilerMarker(Unity.Profiling.ProfilerCategory.Scripts, "AgentBridge.PlayModeTest");
			for (int i = 0; i < 5; i++) { m.Begin(); m.End(); }
			return null;
		`,
	})

	p := c.callTool(t, "profiler_get_samples", map[string]any{"marker": markerName})
	if p["status"] != "ok" {
		t.Fatalf("profiler_get_samples failed: %v", p)
	}
	markers, _ := p["markers"].([]any)
	if len(markers) != 1 {
		t.Fatalf("expected 1 marker entry, got %d", len(markers))
	}
	entry, _ := markers[0].(map[string]any)
	if entry["valid"] != true {
		t.Errorf("expected valid=true in play mode, got %v", entry["valid"])
	}
	count, _ := entry["sampleCount"].(float64)
	if count < 1 {
		t.Errorf("expected sampleCount >= 1, got %v", count)
	}
}

func TestMCP_ProfilerGetSamples_BeforeStart(t *testing.T) {
	c := shared

	c.callTool(t, "profiler_clear", map[string]any{})
	p := c.callTool(t, "profiler_get_samples", map[string]any{})
	if p["status"] != "ok" {
		t.Fatalf("profiler_get_samples before start failed: %v", p)
	}
	markers, _ := p["markers"].([]any)
	if len(markers) != 0 {
		t.Errorf("expected 0 markers after clear, got %d", len(markers))
	}
}
