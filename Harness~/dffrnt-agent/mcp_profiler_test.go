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

	const markerCode = `var m = new Unity.Profiling.ProfilerMarker(Unity.Profiling.ProfilerCategory.Scripts, "MyTestMarker");
for (int i = 0; i < 5; i++) { m.Begin(); m.End(); }
return null;`

	// Fire the marker once to register it with the profiler system before starting the recorder.
	c.invokeCmd(t, "execute_script", map[string]any{"code": markerCode})

	c.callTool(t, "profiler_start", map[string]any{"markers": []any{"MyTestMarker"}})

	// Fire the marker again so the recorder captures samples.
	c.invokeCmd(t, "execute_script", map[string]any{"code": markerCode})

	p := c.callTool(t, "profiler_get_samples", map[string]any{"marker": "MyTestMarker"})
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
	if count == 0 {
		t.Errorf("expected sampleCount > 0, got %v", count)
	}
}

func TestMCP_Profiler_RecordersRecreatdAfterPlayModeEntry(t *testing.T) {
	c := shared

	// Must start in edit mode.
	status := c.callTool(t, "play_mode", map[string]any{"action": "status"})
	if playing, _ := status["playing"].(bool); playing {
		t.Skip("already in play mode — skipping profiler play mode test")
	}
	defer func() {
		c.callTool(t, "play_mode", map[string]any{"action": "exit"})
		c.callTool(t, "profiler_clear", map[string]any{})
	}()

	// Start recorders before entering play mode.
	start := c.callTool(t, "profiler_start", map[string]any{"markers": []any{"MyPlayModeMarker"}})
	if start["status"] != "ok" {
		t.Fatalf("profiler_start failed: %v", start)
	}

	// Enter play mode — domain reload destroys the recorders, then EnteredPlayMode recreates them.
	enter := c.callTool(t, "play_mode", map[string]any{"action": "enter"})
	if enter["status"] != "ok" {
		t.Fatalf("play_mode enter failed: %v", enter)
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

	status := c.callTool(t, "play_mode", map[string]any{"action": "status"})
	if playing, _ := status["playing"].(bool); playing {
		t.Skip("already in play mode — skipping")
	}
	defer func() {
		c.callTool(t, "play_mode", map[string]any{"action": "exit"})
		c.callTool(t, "profiler_clear", map[string]any{})
	}()

	c.callTool(t, "play_mode", map[string]any{"action": "enter"})

	const markerCode = `var m = new Unity.Profiling.ProfilerMarker(Unity.Profiling.ProfilerCategory.Scripts, "MyPlayModeCapture");
for (int i = 0; i < 5; i++) { m.Begin(); m.End(); }
return null;`

	// Pre-register and fire the marker so the recorder can find it.
	c.invokeCmd(t, "execute_script", map[string]any{"code": markerCode})

	c.callTool(t, "profiler_start", map[string]any{"markers": []any{"MyPlayModeCapture"}})

	// Fire again so the now-valid recorder captures samples.
	c.invokeCmd(t, "execute_script", map[string]any{"code": markerCode})

	p := c.callTool(t, "profiler_get_samples", map[string]any{"marker": "MyPlayModeCapture"})
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
	if count == 0 {
		t.Errorf("expected sampleCount > 0, got %v", count)
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
