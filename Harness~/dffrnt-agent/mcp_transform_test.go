package main

import (
	"fmt"
	"testing"
)

func TestMCP_SetTransform_Position(t *testing.T) {
	c := shared
	saveIfDirty(t, c)

	name := testName("SetPos")
	c.callTool(t, "object_create", map[string]any{"name": name})
	defer c.callTool(t, "object_delete", map[string]any{"path": name})

	p := c.callTool(t, "set_transform", map[string]any{
		"path":     name,
		"position": map[string]any{"x": 1.0, "y": 2.0, "z": 3.0},
	})
	if p["status"] != "ok" {
		t.Fatalf("set_transform failed: %v", p)
	}

	get := c.callTool(t, "component_get", map[string]any{"path": name, "component": "Transform"})
	pos, _ := get["fields"].(map[string]any)["m_LocalPosition"].(map[string]any)
	if fmt.Sprint(pos["x"]) != "1" || fmt.Sprint(pos["y"]) != "2" || fmt.Sprint(pos["z"]) != "3" {
		t.Errorf("expected position (1,2,3), got %v", pos)
	}
}

func TestMCP_SetTransform_Rotation(t *testing.T) {
	c := shared
	saveIfDirty(t, c)

	name := testName("SetRot")
	c.callTool(t, "object_create", map[string]any{"name": name})
	defer c.callTool(t, "object_delete", map[string]any{"path": name})

	p := c.callTool(t, "set_transform", map[string]any{
		"path":     name,
		"rotation": map[string]any{"x": 0.0, "y": 90.0, "z": 0.0},
	})
	if p["status"] != "ok" {
		t.Fatalf("set_transform rotation failed: %v", p)
	}
}

func TestMCP_SetTransform_Scale(t *testing.T) {
	c := shared
	saveIfDirty(t, c)

	name := testName("SetScale")
	c.callTool(t, "object_create", map[string]any{"name": name})
	defer c.callTool(t, "object_delete", map[string]any{"path": name})

	p := c.callTool(t, "set_transform", map[string]any{
		"path":  name,
		"scale": map[string]any{"x": 2.0, "y": 2.0, "z": 2.0},
	})
	if p["status"] != "ok" {
		t.Fatalf("set_transform scale failed: %v", p)
	}

	get := c.callTool(t, "component_get", map[string]any{"path": name, "component": "Transform"})
	scale, _ := get["fields"].(map[string]any)["m_LocalScale"].(map[string]any)
	if fmt.Sprint(scale["x"]) != "2" {
		t.Errorf("expected scale.x=2, got %v", scale["x"])
	}
}

func TestMCP_SetTransform_MissingPath(t *testing.T) {
	c := shared

	p := c.callTool(t, "set_transform", map[string]any{
		"path":     "[MCP-DoesNotExist-xyz]",
		"position": map[string]any{"x": 1.0, "y": 0.0, "z": 0.0},
	})
	if p["status"] != "error" {
		t.Errorf("expected error for missing object, got %v", p["status"])
	}
}

func TestMCP_DuplicateObject(t *testing.T) {
	c := shared
	saveIfDirty(t, c)

	orig := testName("DupOrig")
	copy := testName("DupCopy")
	c.callTool(t, "object_create", map[string]any{"name": orig})
	defer c.callTool(t, "object_delete", map[string]any{"path": orig})
	defer c.callTool(t, "object_delete", map[string]any{"path": copy})

	p := c.callTool(t, "duplicate_object", map[string]any{"path": orig, "name": copy})
	if p["status"] != "ok" {
		t.Fatalf("duplicate_object failed: %v", p)
	}
	if p["path"] == nil {
		t.Error("missing path in response")
	}

	// Both original and copy should exist.
	if c.callTool(t, "object_find", map[string]any{"path": orig})["status"] != "ok" {
		t.Error("original object missing after duplicate")
	}
	if c.callTool(t, "object_find", map[string]any{"path": copy})["status"] != "ok" {
		t.Error("duplicate object not found")
	}
}

func TestMCP_DuplicateObject_Missing(t *testing.T) {
	c := shared

	p := c.callTool(t, "duplicate_object", map[string]any{"path": "[MCP-DoesNotExist-xyz]"})
	if p["status"] != "error" {
		t.Errorf("expected error for missing object, got %v", p["status"])
	}
}

func TestMCP_ReparentObject(t *testing.T) {
	c := shared
	saveIfDirty(t, c)

	parent := testName("ReparentParent")
	child := testName("ReparentChild")
	c.callTool(t, "object_create", map[string]any{"name": parent})
	c.callTool(t, "object_create", map[string]any{"name": child})
	defer c.callTool(t, "object_delete", map[string]any{"path": parent})

	p := c.callTool(t, "reparent_object", map[string]any{
		"path":   child,
		"parent": parent,
	})
	if p["status"] != "ok" {
		t.Fatalf("reparent_object failed: %v", p)
	}

	// Child should now be nested under parent.
	expectedPath := parent + "/" + child
	if find := c.callTool(t, "object_find", map[string]any{"path": expectedPath}); find["status"] != "ok" {
		t.Errorf("expected child at %q after reparent, got %v", expectedPath, find)
	}
}

func TestMCP_ReparentObject_ToRoot(t *testing.T) {
	c := shared
	saveIfDirty(t, c)

	parent := testName("RootParent")
	child := testName("RootChild")
	c.callTool(t, "object_create", map[string]any{"name": parent})
	c.callTool(t, "object_create", map[string]any{"name": child})
	defer c.callTool(t, "object_delete", map[string]any{"path": parent})

	// Reparent under parent first.
	c.callTool(t, "reparent_object", map[string]any{"path": child, "parent": parent})

	// Then reparent back to root (empty parent).
	p := c.callTool(t, "reparent_object", map[string]any{"path": parent + "/" + child})
	if p["status"] != "ok" {
		t.Fatalf("reparent_object to root failed: %v", p)
	}
	if c.callTool(t, "object_find", map[string]any{"path": child})["status"] != "ok" {
		t.Error("object not found at root after reparent")
	}
	defer c.callTool(t, "object_delete", map[string]any{"path": child})
}

func TestMCP_ReparentObject_MissingObject(t *testing.T) {
	c := shared

	p := c.callTool(t, "reparent_object", map[string]any{"path": "[MCP-DoesNotExist-xyz]"})
	if p["status"] != "error" {
		t.Errorf("expected error for missing object, got %v", p["status"])
	}
}

func TestMCP_ReparentObject_MissingParent(t *testing.T) {
	c := shared
	saveIfDirty(t, c)

	name := testName("ReparentBadParent")
	c.callTool(t, "object_create", map[string]any{"name": name})
	defer c.callTool(t, "object_delete", map[string]any{"path": name})

	p := c.callTool(t, "reparent_object", map[string]any{
		"path":   name,
		"parent": "[MCP-NoSuchParent-xyz]",
	})
	if p["status"] != "error" {
		t.Errorf("expected error for missing parent, got %v", p["status"])
	}
}
