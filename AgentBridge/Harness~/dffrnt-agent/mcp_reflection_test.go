package main

import (
	"strings"
	"testing"
)

func TestMCP_ReflectAssemblies(t *testing.T) {
	c := shared

	p := c.invokeCmd(t, "reflect_assemblies", map[string]any{})
	if p["status"] != "ok" {
		t.Errorf("expected ok, got %v", p["status"])
	}
	asms, _ := p["assemblies"].([]any)
	if len(asms) == 0 {
		t.Fatal("expected at least one assembly")
	}
	// Verify each entry has name and fullName.
	for _, raw := range asms {
		entry, _ := raw.(map[string]any)
		if entry["name"] == nil || entry["fullName"] == nil {
			t.Errorf("assembly entry missing name/fullName: %v", entry)
		}
	}
}

func TestMCP_ReflectAssemblies_ContainsUnityEngine(t *testing.T) {
	c := shared

	p := c.invokeCmd(t, "reflect_assemblies", map[string]any{})
	asms, _ := p["assemblies"].([]any)
	for _, raw := range asms {
		entry, _ := raw.(map[string]any)
		if name, _ := entry["name"].(string); strings.EqualFold(name, "UnityEngine.CoreModule") || strings.EqualFold(name, "UnityEngine") {
			return
		}
	}
	t.Error("expected UnityEngine or UnityEngine.CoreModule in assemblies")
}

func TestMCP_ReflectTypes_QueryTransform(t *testing.T) {
	c := shared

	p := c.invokeCmd(t, "reflect_types", map[string]any{"query": "Transform"})
	if p["status"] != "ok" {
		t.Fatalf("expected ok, got %v", p["status"])
	}
	types, _ := p["types"].([]any)
	if len(types) == 0 {
		t.Fatal("expected at least one type matching 'Transform'")
	}
	for _, raw := range types {
		entry, _ := raw.(map[string]any)
		name, _ := entry["fullName"].(string)
		if !strings.Contains(strings.ToLower(name), "transform") {
			t.Errorf("type %q does not match query 'transform'", name)
		}
	}
}

func TestMCP_ReflectTypes_Limit(t *testing.T) {
	c := shared

	p := c.invokeCmd(t, "reflect_types", map[string]any{"query": "e", "limit": 3})
	if p["status"] != "ok" {
		t.Fatalf("expected ok, got %v", p["status"])
	}
	types, _ := p["types"].([]any)
	if len(types) > 3 {
		t.Errorf("expected at most 3 results, got %d", len(types))
	}
	if p["truncated"] == nil {
		t.Error("expected truncated field")
	}
}

func TestMCP_ReflectTypes_NamespaceFilter(t *testing.T) {
	c := shared

	p := c.invokeCmd(t, "reflect_types", map[string]any{"namespace": "UnityEngine"})
	if p["status"] != "ok" {
		t.Fatalf("expected ok, got %v", p["status"])
	}
	types, _ := p["types"].([]any)
	if len(types) == 0 {
		t.Fatal("expected types in UnityEngine namespace")
	}
	for _, raw := range types {
		entry, _ := raw.(map[string]any)
		name, _ := entry["fullName"].(string)
		if !strings.Contains(name, "UnityEngine") {
			t.Errorf("type %q not in UnityEngine namespace", name)
		}
	}
}

func TestMCP_ReflectMembers_Transform(t *testing.T) {
	c := shared

	p := c.invokeCmd(t, "reflect_members", map[string]any{"type": "UnityEngine.Transform"})
	if p["status"] != "ok" {
		t.Fatalf("expected ok, got %v", p["status"])
	}
	members, _ := p["members"].([]any)
	if len(members) == 0 {
		t.Fatal("expected members on Transform")
	}
	// Must contain "position" property.
	found := false
	for _, raw := range members {
		m, _ := raw.(map[string]any)
		if strings.EqualFold(m["name"].(string), "position") {
			found = true
			break
		}
	}
	if !found {
		t.Error("expected 'position' in Transform members")
	}
}

func TestMCP_ReflectMembers_MissingType_ReturnsError(t *testing.T) {
	c := shared

	p := c.invokeCmd(t, "reflect_members", map[string]any{})
	if p["status"] != "error" {
		t.Errorf("expected error for missing type, got %v", p["status"])
	}
}

func TestMCP_ReflectMembers_UnknownType_ReturnsError(t *testing.T) {
	c := shared

	p := c.invokeCmd(t, "reflect_members", map[string]any{"type": "NoSuch.TypeXyz999"})
	if p["status"] != "error" {
		t.Errorf("expected error for unknown type, got %v", p["status"])
	}
}

func TestMCP_ReflectMembers_KindMethod(t *testing.T) {
	c := shared

	p := c.invokeCmd(t, "reflect_members", map[string]any{
		"type": "UnityEngine.Transform",
		"kind": "method",
	})
	if p["status"] != "ok" {
		t.Fatalf("expected ok, got %v", p["status"])
	}
	members, _ := p["members"].([]any)
	for _, raw := range members {
		m, _ := raw.(map[string]any)
		if m["kind"] != "method" {
			t.Errorf("expected kind=method, got %v", m["kind"])
		}
	}
}

func TestMCP_ReflectMembers_IncludeInherited(t *testing.T) {
	c := shared

	// Use UnityEngine.MonoBehaviour which has very few own members, so the
	// default limit (100) is never hit for own-only, but inherited members from
	// Behaviour/Component/Object make the inherited count clearly larger.
	base := c.invokeCmd(t, "reflect_members", map[string]any{
		"type": "UnityEngine.MonoBehaviour",
	})
	inherited := c.invokeCmd(t, "reflect_members", map[string]any{
		"type":              "UnityEngine.MonoBehaviour",
		"include_inherited": true,
	})
	baseCount := len(base["members"].([]any))
	inheritedCount := len(inherited["members"].([]any))
	if inheritedCount <= baseCount {
		t.Errorf("expected more members with include_inherited (%d) than without (%d)", inheritedCount, baseCount)
	}
}
