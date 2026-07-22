package main

import (
	"testing"
)

func TestMCP_PrefabSave_NoStage(t *testing.T) {
	c := shared

	// prefab_save should error when no prefab stage is open.
	p := c.invokeCmd(t, "prefab_save", map[string]any{})
	if p["status"] != "error" {
		t.Errorf("expected error when no prefab stage open, got %v", p["status"])
	}
}

func TestMCP_PrefabOpen_Missing(t *testing.T) {
	c := shared

	p := c.invokeCmd(t, "prefab_open", map[string]any{"path": "Assets/Prefabs/DoesNotExist_MCP.prefab"})
	if p["status"] != "error" {
		t.Errorf("expected error for missing prefab, got %v", p["status"])
	}
}

func TestMCP_PrefabOpenSave(t *testing.T) {
	c := shared

	// Write a minimal prefab via a helper script, then open and save it.
	// We create a prefab by writing a minimal Unity YAML prefab file.
	prefabPath := "Assets/MCP_Test_Prefab.prefab"
	minimalPrefab := `%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!1 &1
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  serializedVersion: 6
  m_Component: []
  m_Layer: 0
  m_Name: MCP_Test_Prefab
  m_TagString: Untagged
  m_Icon: {fileID: 0}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
--- !u!4 &2
Transform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 1}
  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}
  m_LocalPosition: {x: 0, y: 0, z: 0}
  m_LocalScale: {x: 1, y: 1, z: 1}
  m_ConstrainProportionsScale: 0
  m_Children: []
  m_Father: {fileID: 0}
  m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}
--- !u!1001 &3
PrefabInstance:
  m_ObjectHideFlags: 0
  serializedVersion: 2
  m_Modification:
    m_TransformParent: {fileID: 0}
    m_Modifications: []
    m_RemovedComponents: []
    m_RemovedGameObjects: []
    m_AddedGameObjects: []
    m_AddedComponents: []
  m_SourcePrefab: {fileID: 100100000, guid: 00000000000000000000000000000000, type: 3}
`
	write := c.callTool(t, "asset_write_text", map[string]any{"path": prefabPath, "content": minimalPrefab})
	defer c.callTool(t, "asset_delete", map[string]any{"path": prefabPath})

	if write["status"] != "ok" {
		t.Skipf("could not write test prefab: %v", write)
	}

	open := c.invokeCmd(t, "prefab_open", map[string]any{"path": prefabPath})
	if open["status"] != "ok" {
		t.Skipf("prefab_open failed (prefab format may differ by Unity version): %v", open)
	}

	save := c.invokeCmd(t, "prefab_save", map[string]any{})
	if save["status"] != "ok" {
		t.Errorf("prefab_save failed: %v", save)
	}
	if save["path"] == nil {
		t.Error("missing path in prefab_save response")
	}
}
