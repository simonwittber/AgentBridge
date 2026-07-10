using System;
using System.Text.Json.Nodes;
using UnityEditor;
using UnityEngine;

namespace LLMDevTools
{
    [InitializeOnLoad]
    internal static class ComponentCommands
    {
        static ComponentCommands()
        {
            AgentBridge.Register(new ComponentGetCmd());
            AgentBridge.Register(new ComponentSetCmd());
            AgentBridge.Register(new ComponentAddCmd());
        }

        private sealed class ComponentGetCmd : IAgentCommand
        {
            public string    Cmd         => "component_get";
            public string    Description => "Get all serialized fields of a component on a GameObject.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("path",      "string", "", "Hierarchy path of the GameObject"),
                new ArgSpec("component", "string", "", "Component type name, e.g. Transform, Image, AudioSource"),
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
                var comp = SceneBridge.FindComponent(go, p.component);
                if (comp == null)
                {
                    var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                    e["message"] = $"Component not found: {p.component}";
                    return e;
                }
                var resp = AgentBridge.MakeResponse(uid, Cmd, "ok");
                resp["fields"] = SceneBridge.SerializedObjectToJson(comp);
                return resp;
            }

            [Serializable] private class Params { public string path = ""; public string component = ""; }
        }

        private sealed class ComponentSetCmd : IAgentCommand
        {
            public string    Cmd         => "component_set";
            public string    Description => "Set a serialized field on a component. Use {\"path\":\"Assets/...\"} for asset refs, {\"scene\":\"Canvas/Obj\"} for scene refs.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("path",      "string", "", "Hierarchy path of the GameObject"),
                new ArgSpec("component", "string", "", "Component type name"),
                new ArgSpec("field",     "string", "", "Serialized property name, e.g. m_LocalPosition or interactable"),
                new ArgSpec("value",     "any",    "", "New value: number, bool, string, or JSON object"),
            };

            public JsonObject Execute(string uid, string requestJson)
            {
                if (!SceneBridge.TryParseRequest(requestJson, out var req))
                {
                    var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                    e["message"] = "Invalid request JSON";
                    return e;
                }

                string   objPath   = req["path"]?     .GetValue<string>() ?? "";
                string   component = req["component"]?.GetValue<string>() ?? "";
                string   field     = req["field"]?    .GetValue<string>() ?? "";
                JsonNode value     = req["value"];

                var go = SceneBridge.FindByPath(objPath);
                if (go == null)
                {
                    var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                    e["message"] = $"Not found: {objPath}";
                    return e;
                }
                var comp = SceneBridge.FindComponent(go, component);
                if (comp == null)
                {
                    var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                    e["message"] = $"Component not found: {component}";
                    return e;
                }
                var so = new SerializedObject(comp);
                var sp = so.FindProperty(field);
                if (sp == null)
                {
                    var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                    e["message"] = $"Field not found: {field}";
                    return e;
                }
                if (!SceneBridge.JsonToSp(sp, value))
                {
                    var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                    e["message"] = $"Failed to set '{field}' ({sp.propertyType}) — see Console for details.";
                    return e;
                }
                so.ApplyModifiedProperties();
                SceneBridge.MarkDirty(go);
                return AgentBridge.MakeResponse(uid, Cmd, "ok");
            }
        }

        private sealed class ComponentAddCmd : IAgentCommand
        {
            public string    Cmd         => "component_add";
            public string    Description => "Add a component to a GameObject by type name.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("path", "string", "", "Hierarchy path of the GameObject"),
                new ArgSpec("type", "string", "", "Component type name, e.g. AudioSource, Rigidbody"),
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
                var type = SceneBridge.ResolveType(p.type);
                if (type == null || !typeof(Component).IsAssignableFrom(type))
                {
                    var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                    e["message"] = $"Unknown component type: {p.type}";
                    return e;
                }
                Undo.AddComponent(go, type);
                SceneBridge.MarkDirty(go);
                return AgentBridge.MakeResponse(uid, Cmd, "ok");
            }

            [Serializable] private class Params { public string path = ""; public string type = ""; }
        }
    }
}
