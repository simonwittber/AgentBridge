package main

import (
	"fmt"
	"testing"
)

func TestMCP_Hierarchy(t *testing.T) {
	c := shared

	p := c.callTool(t, "hierarchy", map[string]any{"depth": 2})
	if p["status"] != "ok" {
		t.Errorf("expected ok, got %v", p["status"])
	}
	if p["objects"] == nil {
		t.Error("missing objects")
	}
}

func TestMCP_ObjectFind(t *testing.T) {
	c := shared
	saveIfDirty(t, c)

	name := testName("Find")
	c.callTool(t, "object_create", map[string]any{"name": name})
	defer c.callTool(t, "object_delete", map[string]any{"path": name})

	p := c.callTool(t, "object_find", map[string]any{"path": name})
	if p["status"] != "ok" {
		t.Fatalf("object_find failed: %v", p)
	}
	obj, _ := p["object"].(map[string]any)
	if obj["name"] != name {
		t.Errorf("expected name %q, got %v", name, obj["name"])
	}
	if obj["components"] == nil {
		t.Error("missing components")
	}
}

func TestMCP_ObjectFind_Missing(t *testing.T) {
	c := shared

	p := c.callTool(t, "object_find", map[string]any{"path": "[MCP-DoesNotExist-x9z]"})
	if p["status"] != "error" {
		t.Errorf("expected error for missing object, got %v", p["status"])
	}
}

func TestMCP_ObjectsFind(t *testing.T) {
	c := shared
	saveIfDirty(t, c)

	a, b := testName("ObjsA"), testName("ObjsB")
	c.callTool(t, "object_create", map[string]any{"name": a})
	c.callTool(t, "object_create", map[string]any{"name": b})
	c.callTool(t, "component_add", map[string]any{"path": a, "type": "AudioSource"})
	c.callTool(t, "component_add", map[string]any{"path": b, "type": "AudioSource"})
	defer c.callTool(t, "object_delete", map[string]any{"path": a})
	defer c.callTool(t, "object_delete", map[string]any{"path": b})

	p := c.callTool(t, "objects_find", map[string]any{"component": "AudioSource"})
	if p["status"] != "ok" {
		t.Fatalf("objects_find failed: %v", p)
	}
	objects, _ := p["objects"].([]any)
	names := map[string]bool{}
	for _, o := range objects {
		m, _ := o.(map[string]any)
		names[fmt.Sprint(m["name"])] = true
	}
	if !names[a] || !names[b] {
		t.Errorf("expected both objects in result, got %v", names)
	}
}

func TestMCP_ObjectCreateAndDelete(t *testing.T) {
	c := shared
	saveIfDirty(t, c)

	name := testName("Create")
	p := c.callTool(t, "object_create", map[string]any{"name": name})
	if p["status"] != "ok" {
		t.Fatalf("object_create failed: %v", p)
	}
	if p["path"] != name {
		t.Errorf("expected path %q, got %v", name, p["path"])
	}

	del := c.callTool(t, "object_delete", map[string]any{"path": name})
	if del["status"] != "ok" {
		t.Fatalf("object_delete failed: %v", del)
	}
	find := c.callTool(t, "object_find", map[string]any{"path": name})
	if find["status"] != "error" {
		t.Error("object should be gone after delete")
	}
}

func TestMCP_ObjectActive(t *testing.T) {
	c := shared
	saveIfDirty(t, c)

	name := testName("Active")
	c.callTool(t, "object_create", map[string]any{"name": name})
	defer c.callTool(t, "object_delete", map[string]any{"path": name})

	p := c.callTool(t, "object_active", map[string]any{"path": name, "active": false})
	if p["status"] != "ok" {
		t.Fatalf("object_active(false) failed: %v", p)
	}
	find := c.callTool(t, "object_find", map[string]any{"path": name})
	obj, _ := find["object"].(map[string]any)
	if obj["active_self"] != false {
		t.Error("expected object to be inactive")
	}

	c.callTool(t, "object_active", map[string]any{"path": name, "active": true})
	find2 := c.callTool(t, "object_find", map[string]any{"path": name})
	obj2, _ := find2["object"].(map[string]any)
	if obj2["active_self"] != true {
		t.Error("expected object to be active again")
	}
}

func TestMCP_ObjectRename(t *testing.T) {
	c := shared
	saveIfDirty(t, c)

	old := testName("RenameOld")
	newName := testName("RenameNew")
	c.callTool(t, "object_create", map[string]any{"name": old})
	defer c.callTool(t, "object_delete", map[string]any{"path": newName})

	p := c.callTool(t, "object_rename", map[string]any{"path": old, "name": newName})
	if p["status"] != "ok" {
		t.Fatalf("object_rename failed: %v", p)
	}
	if p["path"] != newName {
		t.Errorf("expected path %q, got %v", newName, p["path"])
	}
	if find := c.callTool(t, "object_find", map[string]any{"path": old}); find["status"] != "error" {
		t.Error("old name should no longer exist")
	}
}

func TestMCP_ObjectSelect(t *testing.T) {
	c := shared
	saveIfDirty(t, c)

	name := testName("Select")
	c.callTool(t, "object_create", map[string]any{"name": name})
	defer c.callTool(t, "object_delete", map[string]any{"path": name})

	p := c.callTool(t, "object_select", map[string]any{"paths": name})
	if p["status"] != "ok" {
		t.Fatalf("object_select failed: %v", p)
	}
}

func TestMCP_ObjectCreate_Primitive(t *testing.T) {
	c := shared
	saveIfDirty(t, c)

	name := testName("Cube")
	p := c.callTool(t, "object_create", map[string]any{"name": name, "primitive": "Cube"})
	defer c.callTool(t, "object_delete", map[string]any{"path": name})

	if p["status"] != "ok" {
		t.Fatalf("object_create primitive failed: %v", p)
	}
	find := c.callTool(t, "object_find", map[string]any{"path": name})
	obj, _ := find["object"].(map[string]any)
	comps, _ := obj["components"].([]any)
	hasMesh := false
	for _, comp := range comps {
		if comp == "MeshRenderer" || comp == "MeshFilter" {
			hasMesh = true
		}
	}
	if !hasMesh {
		t.Errorf("expected MeshRenderer/MeshFilter on primitive, components: %v", comps)
	}
}

func TestMCP_ComponentGet(t *testing.T) {
	c := shared
	saveIfDirty(t, c)

	name := testName("CompGet")
	c.callTool(t, "object_create", map[string]any{"name": name})
	defer c.callTool(t, "object_delete", map[string]any{"path": name})

	p := c.callTool(t, "component_get", map[string]any{"path": name, "component": "Transform"})
	if p["status"] != "ok" {
		t.Fatalf("component_get failed: %v", p)
	}
	fields, _ := p["fields"].(map[string]any)
	if fields["m_LocalPosition"] == nil {
		t.Error("missing m_LocalPosition in Transform fields")
	}
}

func TestMCP_ComponentAdd(t *testing.T) {
	c := shared
	saveIfDirty(t, c)

	name := testName("CompAdd")
	c.callTool(t, "object_create", map[string]any{"name": name})
	defer c.callTool(t, "object_delete", map[string]any{"path": name})

	p := c.callTool(t, "component_add", map[string]any{"path": name, "type": "AudioSource"})
	if p["status"] != "ok" {
		t.Fatalf("component_add failed: %v", p)
	}
	find := c.callTool(t, "object_find", map[string]any{"path": name})
	comps, _ := find["object"].(map[string]any)["components"].([]any)
	found := false
	for _, comp := range comps {
		if comp == "AudioSource" {
			found = true
		}
	}
	if !found {
		t.Errorf("AudioSource not in components after add: %v", comps)
	}
}

func TestMCP_ComponentSet(t *testing.T) {
	c := shared
	saveIfDirty(t, c)

	name := testName("CompSet")
	c.callTool(t, "object_create", map[string]any{"name": name})
	defer c.callTool(t, "object_delete", map[string]any{"path": name})

	p := c.callTool(t, "component_set", map[string]any{
		"path":      name,
		"component": "Transform",
		"field":     "m_LocalPosition",
		"value":     map[string]any{"x": 3.0, "y": 6.0, "z": 9.0},
	})
	if p["status"] != "ok" {
		t.Fatalf("component_set failed: %v", p)
	}
	get := c.callTool(t, "component_get", map[string]any{"path": name, "component": "Transform"})
	pos, _ := get["fields"].(map[string]any)["m_LocalPosition"].(map[string]any)
	if fmt.Sprint(pos["x"]) != "3" {
		t.Errorf("expected x=3, got %v", pos["x"])
	}
}
