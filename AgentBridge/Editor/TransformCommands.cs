using System;
using System.Text.Json.Nodes;
using UnityEditor;
using UnityEngine;

namespace LLMDevTools
{
    [InitializeOnLoad]
    internal static class TransformCommands
    {
        static TransformCommands()
        {
            AgentBridge.Register(new SetTransformCmd());
            AgentBridge.Register(new DuplicateObjectCmd());
            AgentBridge.Register(new ReparentObjectCmd());
        }

        static Vector3 ParseVec3(JsonNode node) =>
            node is JsonObject o
                ? new Vector3(
                    (float)(o["x"]?.GetValue<double>() ?? 0),
                    (float)(o["y"]?.GetValue<double>() ?? 0),
                    (float)(o["z"]?.GetValue<double>() ?? 0))
                : Vector3.zero;

        private sealed class SetTransformCmd : IAgentCommand
        {
            public string    Cmd         => "set_transform";
            public string    Description => "Set position, rotation (euler degrees), and/or scale of a GameObject in one call.";
            public bool      Core        => true;
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("path",     "string", "",      "Hierarchy path of the GameObject"),
                new ArgSpec("position", "any",    "",      "{x,y,z} position"),
                new ArgSpec("rotation", "any",    "",      "{x,y,z} euler angles in degrees"),
                new ArgSpec("scale",    "any",    "",      "{x,y,z} scale (always local space)"),
                new ArgSpec("space",    "string", "local", "\"local\" or \"world\""),
            };

            public JsonObject Execute(string uid, string requestJson)
            {
                var req  = JsonNode.Parse(requestJson);
                var path = req?["path"]?.GetValue<string>() ?? "";
                var go   = SceneBridge.FindByPath(path);
                if (go == null)
                {
                    var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                    e["message"] = $"Not found: {path}";
                    return e;
                }

                var space = req?["space"]?.GetValue<string>() ?? "local";
                bool world = string.Equals(space, "world", StringComparison.OrdinalIgnoreCase);

                Undo.RecordObject(go.transform, "Set Transform");

                if (req?["position"] is JsonNode posNode)
                {
                    var v = ParseVec3(posNode);
                    if (world) go.transform.position      = v;
                    else       go.transform.localPosition = v;
                }
                if (req?["rotation"] is JsonNode rotNode)
                {
                    var e = ParseVec3(rotNode);
                    if (world) go.transform.eulerAngles      = e;
                    else       go.transform.localEulerAngles = e;
                }
                if (req?["scale"] is JsonNode scaleNode)
                    go.transform.localScale = ParseVec3(scaleNode);

                SceneBridge.MarkDirty(go);
                return AgentBridge.MakeResponse(uid, Cmd, "ok");
            }
        }

        private sealed class DuplicateObjectCmd : IAgentCommand
        {
            public string    Cmd         => "duplicate_object";
            public string    Description => "Duplicate a GameObject, placing the copy next to the original in the hierarchy.";
            public bool      Core        => true;
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("path", "string", "", "Hierarchy path of the object to duplicate"),
                new ArgSpec("name", "string", "", "Name for the copy; defaults to the original name"),
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

                var copy = UnityEngine.Object.Instantiate(go, go.transform.parent);
                copy.name = string.IsNullOrEmpty(p.name) ? go.name : p.name;
                Undo.RegisterCreatedObjectUndo(copy, $"Duplicate {go.name}");
                SceneBridge.MarkDirty(copy);

                var resp = AgentBridge.MakeResponse(uid, Cmd, "ok");
                resp["path"] = SceneBridge.GetPath(copy);
                return resp;
            }

            [Serializable] private class Params { public string path = ""; public string name = ""; }
        }

        private sealed class ReparentObjectCmd : IAgentCommand
        {
            public string    Cmd         => "reparent_object";
            public string    Description => "Move a GameObject to a new parent in the hierarchy.";
            public bool      Core        => true;
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("path",                "string", "",     "Hierarchy path of the object to reparent"),
                new ArgSpec("parent",              "string", "",     "Hierarchy path of the new parent; omit to move to scene root"),
                new ArgSpec("keep_world_position", "bool",   "true", "If true, preserves world position, rotation, and scale"),
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

                Transform newParent = null;
                if (!string.IsNullOrEmpty(p.parent))
                {
                    var parentGo = SceneBridge.FindByPath(p.parent);
                    if (parentGo == null)
                    {
                        var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                        e["message"] = $"Parent not found: {p.parent}";
                        return e;
                    }
                    newParent = parentGo.transform;
                }

                Undo.RecordObject(go.transform, "Reparent");
                go.transform.SetParent(newParent, p.keep_world_position);
                SceneBridge.MarkDirty(go);

                var resp = AgentBridge.MakeResponse(uid, Cmd, "ok");
                resp["path"] = SceneBridge.GetPath(go);
                return resp;
            }

            [Serializable]
            private class Params
            {
                public string path                = "";
                public string parent              = "";
                public bool   keep_world_position = true;
            }
        }
    }
}
