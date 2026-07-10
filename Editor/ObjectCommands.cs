using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using UnityEditor;
using UnityEngine;

namespace LLMDevTools
{
    [InitializeOnLoad]
    internal static class ObjectCommands
    {
        static ObjectCommands()
        {
            AgentBridge.Register(new ObjectCreateCmd());
            AgentBridge.Register(new ObjectDeleteCmd());
            AgentBridge.Register(new ObjectActiveCmd());
            AgentBridge.Register(new ObjectRenameCmd());
            AgentBridge.Register(new ObjectSelectCmd());
        }

        private sealed class ObjectCreateCmd : IAgentCommand
        {
            public string    Cmd         => "object_create";
            public string    Description => "Create a new GameObject, optionally as a 3D primitive.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("name",      "string", "GameObject", "Name of the new object"),
                new ArgSpec("parent",    "string", "",           "Parent hierarchy path; omit for scene root"),
                new ArgSpec("primitive", "string", "",           "Primitive type: Cube, Sphere, Plane, Cylinder, Capsule, Quad"),
            };

            public JsonObject Execute(string uid, string requestJson)
            {
                var p  = JsonUtility.FromJson<Params>(requestJson);
                GameObject go;

                if (!string.IsNullOrEmpty(p.primitive) &&
                    Enum.TryParse<PrimitiveType>(p.primitive, true, out var primType))
                {
                    go      = GameObject.CreatePrimitive(primType);
                    go.name = string.IsNullOrEmpty(p.name) ? p.primitive : p.name;
                }
                else
                {
                    go = new GameObject(string.IsNullOrEmpty(p.name) ? "GameObject" : p.name);
                }

                if (!string.IsNullOrEmpty(p.parent))
                {
                    var parent = SceneBridge.FindByPath(p.parent);
                    if (parent != null) go.transform.SetParent(parent.transform, false);
                }

                Undo.RegisterCreatedObjectUndo(go, $"Create {go.name}");
                SceneBridge.MarkDirty(go);

                var resp = AgentBridge.MakeResponse(uid, Cmd, "ok");
                resp["path"] = SceneBridge.GetPath(go);
                return resp;
            }

            [Serializable] private class Params { public string name = ""; public string parent = ""; public string primitive = ""; }
        }

        private sealed class ObjectDeleteCmd : IAgentCommand
        {
            public string    Cmd         => "object_delete";
            public string    Description => "Delete a GameObject by hierarchy path.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("path", "string", "", "Hierarchy path of the object to delete"),
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
                Undo.DestroyObjectImmediate(go);
                return AgentBridge.MakeResponse(uid, Cmd, "ok");
            }

            [Serializable] private class Params { public string path = ""; }
        }

        private sealed class ObjectActiveCmd : IAgentCommand
        {
            public string    Cmd         => "object_active";
            public string    Description => "Set the active state of a GameObject.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("path",   "string", "",     "Hierarchy path"),
                new ArgSpec("active", "bool",   "true", "True to activate, false to deactivate"),
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
                Undo.RecordObject(go, p.active ? "Activate Object" : "Deactivate Object");
                go.SetActive(p.active);
                SceneBridge.MarkDirty(go);
                return AgentBridge.MakeResponse(uid, Cmd, "ok");
            }

            [Serializable] private class Params { public string path = ""; public bool active = true; }
        }

        private sealed class ObjectRenameCmd : IAgentCommand
        {
            public string    Cmd         => "object_rename";
            public string    Description => "Rename a GameObject.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("path", "string", "", "Hierarchy path of the object to rename"),
                new ArgSpec("name", "string", "", "New name"),
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
                if (string.IsNullOrEmpty(p.name))
                {
                    var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                    e["message"] = "name cannot be empty";
                    return e;
                }
                Undo.RecordObject(go, "Rename GameObject");
                go.name = p.name;
                SceneBridge.MarkDirty(go);
                var resp = AgentBridge.MakeResponse(uid, Cmd, "ok");
                resp["path"] = SceneBridge.GetPath(go);
                return resp;
            }

            [Serializable] private class Params { public string path = ""; public string name = ""; }
        }

        private sealed class ObjectSelectCmd : IAgentCommand
        {
            public string    Cmd         => "object_select";
            public string    Description => "Select one or more GameObjects in the editor hierarchy by path.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("path",  "string", "", "Single hierarchy path to select"),
                new ArgSpec("paths", "string", "", "Comma-separated hierarchy paths to select multiple objects"),
            };

            public JsonObject Execute(string uid, string requestJson)
            {
                var p   = JsonUtility.FromJson<Params>(requestJson);
                var gos = new List<UnityEngine.Object>();

                if (!string.IsNullOrEmpty(p.path))
                {
                    var go = SceneBridge.FindByPath(p.path);
                    if (go == null)
                    {
                        var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                        e["message"] = $"Not found: {p.path}";
                        return e;
                    }
                    gos.Add(go);
                }

                if (!string.IsNullOrEmpty(p.paths))
                    foreach (var part in p.paths.Split(','))
                    {
                        var go = SceneBridge.FindByPath(part.Trim());
                        if (go != null) gos.Add(go);
                    }

                if (gos.Count == 0)
                {
                    var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                    e["message"] = "No objects found";
                    return e;
                }

                Selection.objects = gos.ToArray();
                var resp = AgentBridge.MakeResponse(uid, Cmd, "ok");
                resp["selected"] = gos.Count;
                return resp;
            }

            [Serializable] private class Params { public string path = ""; public string paths = ""; }
        }
    }
}
