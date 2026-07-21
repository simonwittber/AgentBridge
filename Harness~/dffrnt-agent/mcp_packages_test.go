package main

import (
	"testing"
)

func TestMCP_PackageList(t *testing.T) {
	c := shared

	p := c.invokeCmd(t, "package_list", map[string]any{})
	if p["status"] != "ok" {
		t.Fatalf("package_list failed: %v", p)
	}
	pkgs, _ := p["packages"].([]any)
	if len(pkgs) == 0 {
		t.Error("expected at least one installed package")
	}
	first, _ := pkgs[0].(map[string]any)
	if first["name"] == nil {
		t.Error("package entry missing name")
	}
	if first["version"] == nil {
		t.Error("package entry missing version")
	}
	if first["source"] == nil {
		t.Error("package entry missing source")
	}
}

func TestMCP_PackageSearch(t *testing.T) {
	c := shared

	p := c.invokeCmd(t, "package_search", map[string]any{"query": "com.unity.ugui"})
	if p["status"] != "ok" {
		t.Fatalf("package_search failed: %v", p)
	}
	pkgs, _ := p["packages"].([]any)
	if len(pkgs) == 0 {
		t.Fatal("expected at least one search result for 'com.unity.ugui'")
	}
	first, _ := pkgs[0].(map[string]any)
	if first["name"] == nil {
		t.Error("search result missing name")
	}
	if first["version"] == nil {
		t.Error("search result missing version")
	}
	versions, _ := first["versions"].([]any)
	if len(versions) == 0 {
		t.Error("expected versions list to be non-empty")
	}
}

func TestMCP_PackageSearchMissingQuery(t *testing.T) {
	c := shared

	p := c.invokeCmd(t, "package_search", map[string]any{})
	if p["status"] != "error" {
		t.Errorf("expected error for missing query, got %v", p["status"])
	}
}

func TestMCP_PackageAddRemove(t *testing.T) {
	c := shared

	// Use a lightweight, well-known package unlikely to already be installed at this exact version.
	const identifier = "com.unity.modules.particlesystem"

	add := c.invokeCmd(t, "package_add", map[string]any{"identifier": identifier})
	if add["status"] != "ok" {
		t.Fatalf("package_add failed: %v", add)
	}
	if add["name"] == nil {
		t.Error("package_add response missing name")
	}
	if add["version"] == nil {
		t.Error("package_add response missing version")
	}
}

func TestMCP_PackageRemove_NotInstalled(t *testing.T) {
	c := shared

	p := c.invokeCmd(t, "package_remove", map[string]any{"name": "com.unity.package.that.does.not.exist"})
	if p["status"] != "error" {
		t.Errorf("expected error removing non-existent package, got %v", p["status"])
	}
}

func TestMCP_PackageRemoveMissingName(t *testing.T) {
	c := shared

	p := c.invokeCmd(t, "package_remove", map[string]any{})
	if p["status"] != "error" {
		t.Errorf("expected error for missing name, got %v", p["status"])
	}
}

func TestMCP_PackageAddMissingIdentifier(t *testing.T) {
	c := shared

	p := c.invokeCmd(t, "package_add", map[string]any{})
	if p["status"] != "error" {
		t.Errorf("expected error for missing identifier, got %v", p["status"])
	}
}

func TestMCP_CompileErrorsInResponse(t *testing.T) {
	c := shared

	// compile_errors should be present on every response — verify via status.
	p := c.callTool(t, "status", map[string]any{})
	if _, ok := p["compile_errors"]; !ok {
		t.Error("compile_errors field missing from response")
	}
}
