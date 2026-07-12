package main

import (
	"fmt"
	"testing"
)

func TestMCP_TagsLayers(t *testing.T) {
	c := shared

	p := c.callTool(t, "tags_layers", map[string]any{})
	if p["status"] != "ok" {
		t.Fatalf("tags_layers failed: %v", p)
	}
	tags, _ := p["tags"].([]any)
	if len(tags) == 0 {
		t.Error("expected at least one tag")
	}
	layers, _ := p["layers"].([]any)
	if len(layers) == 0 {
		t.Error("expected at least one layer")
	}
}

func TestMCP_TagAdd(t *testing.T) {
	c := shared
	name := testName("Tag")

	add := c.callTool(t, "tag_add", map[string]any{"name": name})
	if add["status"] != "ok" {
		t.Fatalf("tag_add failed: %v", add)
	}

	tl := c.callTool(t, "tags_layers", map[string]any{})
	tags, _ := tl["tags"].([]any)
	found := false
	for _, tag := range tags {
		if fmt.Sprint(tag) == name {
			found = true
		}
	}
	if !found {
		t.Errorf("%s not found in tags after add: %v", name, tags)
	}

	dup := c.callTool(t, "tag_add", map[string]any{"name": name})
	if dup["status"] != "error" {
		t.Errorf("expected error for duplicate tag, got %v", dup["status"])
	}
}

func TestMCP_LayerAdd(t *testing.T) {
	c := shared
	name := testName("Layer")

	add := c.callTool(t, "layer_add", map[string]any{"name": name})
	if add["status"] != "ok" {
		t.Fatalf("layer_add failed: %v", add)
	}
	index, _ := add["index"].(float64)
	if index < 8 {
		t.Errorf("expected layer index >= 8 (user layers), got %v", index)
	}

	tl := c.callTool(t, "tags_layers", map[string]any{})
	layers, _ := tl["layers"].([]any)
	found := false
	for _, layer := range layers {
		if m, ok := layer.(map[string]any); ok {
			if fmt.Sprint(m["name"]) == name {
				found = true
			}
		}
	}
	if !found {
		t.Errorf("%s not found in layers after add: %v", name, layers)
	}
}
