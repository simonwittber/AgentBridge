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

	const markerName = "AgentBridge.BenchmarkEditMode"

	// Pre-register the marker before starting the recorder.
	c.invokeCmd(t, "profiler_benchmark", map[string]any{"marker": markerName})

	c.callTool(t, "profiler_start", map[string]any{"markers": []any{markerName}})

	// Fire again so the recorder captures samples.
	c.invokeCmd(t, "profiler_benchmark", map[string]any{"marker": markerName})

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
		t.Errorf("expected sampleCount >= 5 (marker fired 5 times), got %v", count)
	}
}

func TestMCP_Profiler_RecordersRecreatdAfterPlayModeEntry(t *testing.T) {
	c := shared

	// Ensure edit mode before starting.
	c.callTool(t, "play_exit", map[string]any{})
	defer func() {
		c.callTool(t, "play_exit", map[string]any{})
		c.callTool(t, "profiler_clear", map[string]any{})
	}()

	// Start recorders before entering play mode.
	start := c.callTool(t, "profiler_start", map[string]any{"markers": []any{"MyPlayModeMarker"}})
	if start["status"] != "ok" {
		t.Fatalf("profiler_start failed: %v", start)
	}

	// Enter play mode — domain reload destroys the recorders, then EnteredPlayMode recreates them.
	enter := c.callTool(t, "play_enter", map[string]any{})
	if enter["status"] != "ok" {
		t.Fatalf("play_enter failed: %v", enter)
	}

	// Recorders should have been recreated by the EnteredPlayMode callback.
	// valid=true requires the game to have a ProfilerMarker with this name;
	// we only verify the recorder entry is present (recreation succeeded).
	p := c.callTool(t, "profiler_get_samples", map[string]any{"marker": "MyPlayModeMarker"})
	if p["status"] != "ok" {
		t.Fatalf("profiler_get_samples after play mode entry failed: %v", p)
	}
	markers, _ := p["markers"].([]any)
	if len(markers) != 1 {
		t.Errorf("expected recorder to be recreated after play mode entry, got %d marker entries", len(markers))
	}
}

func TestMCP_Profiler_CapturesDataInPlayMode(t *testing.T) {
	c := shared

	// Ensure edit mode before starting.
	c.callTool(t, "play_exit", map[string]any{})
	defer func() {
		c.callTool(t, "play_exit", map[string]any{})
		c.callTool(t, "profiler_clear", map[string]any{})
	}()

	c.callTool(t, "play_enter", map[string]any{})

	const markerName = "AgentBridge.BenchmarkPlayMode"

	// Pre-register the marker before starting the recorder.
	c.invokeCmd(t, "profiler_benchmark", map[string]any{"marker": markerName})

	c.callTool(t, "profiler_start", map[string]any{"markers": []any{markerName}})

	// Fire again so the now-valid recorder captures samples with timing.
	c.invokeCmd(t, "profiler_benchmark", map[string]any{"marker": markerName})

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
	avgMs, _ := entry["avgMs"].(float64)
	if avgMs <= 0 {
		t.Errorf("expected avgMs > 0, got %v", avgMs)
	}
	maxMs, _ := entry["maxMs"].(float64)
	if maxMs <= 0 {
		t.Errorf("expected maxMs > 0, got %v", maxMs)
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
