using System;
using System.Text.Json.Nodes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LLMDevTools
{
    [InitializeOnLoad]
    internal static class HierarchyCommands
    {
        static HierarchyCommands()
        {
            AgentBridge.Register(new HierarchyCmd());
            AgentBridge.Register(new ObjectFindCmd());
            AgentBridge.Register(new ObjectsFindCmd());
        }

        private sealed class HierarchyCmd : IAgentCommand
        {
            public string    Cmd         => "hierarchy";
            public string    Description => "List the scene GameObject hierarchy as a tree.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("root",  "string", "",  "Object path; omit for all roots"),
                new ArgSpec("depth", "int",    "3", "Max depth; -1 = unlimited (capped at 50)"),
            };

            public JsonObject Execute(string uid, string requestJson)
            {
                var p     = JsonUtility.FromJson<Params>(requestJson);
                int depth = p.depth < 0 ? 50 : p.depth;
                var objects = new JsonArray();

                if (!string.IsNullOrEmpty(p.root))
                {
                    var go = SceneBridge.FindByPath(p.root);
                    if (go == null)
                    {
                        var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                        e["message"] = $"Not found: {p.root}";
                        return e;
                    }
                    objects.Add(SceneBridge.BuildTree(go, depth));
                }
                else
                {
                    var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
                    var roots = prefabStage != null
                        ? new[] { prefabStage.prefabContentsRoot }
                        : SceneManager.GetActiveScene().GetRootGameObjects();
                    foreach (var go in roots)
                        objects.Add(SceneBridge.BuildTree(go, depth));
                }

                var resp = AgentBridge.MakeResponse(uid, Cmd, "ok");
                resp["objects"] = objects;
                return resp;
            }

            [Serializable] private class Params { public string root = ""; public int depth = 3; }
        }

        private sealed class ObjectFindCmd : IAgentCommand
        {
            public string    Cmd         => "object_find";
            public string    Description => "Find a GameObject by hierarchy path and return its info and component list.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("path", "string", "", "Hierarchy path"),
            };

            public JsonObject Execute(string uid, string requestJson)
            {
                var p  = JsonUtility.FromJson<Params>(requestJson);
                var go = SceneBridge.FindByPath(p.path);
                if (go == null)
                {
                    var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                    e["message"] = $"Not found: {p.path}";
                    return e;
                }
                var resp = AgentBridge.MakeResponse(uid, Cmd, "ok");
                resp["object"] = SceneBridge.GameObjectInfo(go);
                return resp;
            }

            [Serializable] private class Params { public string path = ""; }
        }

        private sealed class ObjectsFindCmd : IAgentCommand
        {
            public string    Cmd         => "objects_find";
            public string    Description => "Find all GameObjects in the scene that have a given component type.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("component",        "string", "",      "Component type name"),
                new ArgSpec("fields",           "bool",   "false", "If true, include all serialised fields for each match"),
                new ArgSpec("include_inactive", "bool",   "false", "If true, include inactive GameObjects"),
            };

            public JsonObject Execute(string uid, string requestJson)
            {
                var p    = JsonUtility.FromJson<Params>(requestJson);
                var type = SceneBridge.ResolveType(p.component);
                if (type == null || !typeof(Component).IsAssignableFrom(type))
                {
                    var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                    e["message"] = SceneBridge.IsAmbiguousTypeName(p.component)
                        ? $"Ambiguous type name '{p.component}' — use a fully qualified name e.g. 'UnityEngine.UI.Button'"
                        : $"Unknown component type: {p.component}";
                    return e;
                }

                var inactive = p.include_inactive
                    ? FindObjectsInactive.Include
                    : FindObjectsInactive.Exclude;
                var comps   = UnityEngine.Object.FindObjectsByType(type, inactive);
                var results = new JsonArray();
                foreach (var obj in comps)
                {
                    if (obj is not Component comp) continue;
                    var entry = SceneBridge.GameObjectInfo(comp.gameObject);
                    if (p.fields)
                        entry["fields"] = SceneBridge.SerializedObjectToJson(comp);
                    results.Add(entry);
                }
                var resp = AgentBridge.MakeResponse(uid, Cmd, "ok");
                resp["objects"] = results;
                return resp;
            }

            [Serializable] private class Params { public string component = ""; public bool fields = false; public bool include_inactive = false; }
        }
    }
}
