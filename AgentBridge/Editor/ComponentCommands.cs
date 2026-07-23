using System;
using System.Reflection;
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
            public string    Description => "Get serialized fields of a component. Arrays return {_items, _total}; check _truncated.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("path",      "string", "", ""),
                new ArgSpec("component", "string", "", ""),
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
            public string    Description => "Set a serialized field. Asset refs: {\"path\":\"Assets/...\"}, scene refs: {\"scene\":\"Obj\"}. Arrays: pass a plain JSON array.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("path",      "string", "", ""),
                new ArgSpec("component", "string", "", ""),
                new ArgSpec("field",     "string", "", "Serialized property name"),
                new ArgSpec("value",     "any",    "", "number, bool, string, or JSON object"),
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
                var sp = FindProperty(so, field);
                if (sp != null)
                {
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

                // Fall back to reflection for public C# properties (runtime-only, no serialized backing)
                var prop = comp.GetType().GetProperty(field, BindingFlags.Public | BindingFlags.Instance);
                if (prop == null || !prop.CanWrite)
                {
                    var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                    e["message"] = $"Field or property not found: {field}";
                    return e;
                }
                try
                {
                    object converted;
                    if (prop.PropertyType.IsEnum)
                    {
                        if (value is System.Text.Json.Nodes.JsonValue jv2 && jv2.TryGetValue<string>(out var s))
                            converted = Enum.Parse(prop.PropertyType, s, ignoreCase: true);
                        else
                            converted = Enum.ToObject(prop.PropertyType, value.GetValue<int>());
                    }
                    else
                    {
                        converted = Convert.ChangeType(value.GetValue<object>(), prop.PropertyType);
                    }
                    prop.SetValue(comp, converted);
                    SceneBridge.MarkDirty(go);
                    return AgentBridge.MakeResponse(uid, Cmd, "ok");
                }
                catch (Exception ex)
                {
                    var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                    e["message"] = $"Failed to set property '{field}': {ex.Message}";
                    return e;
                }
            }
        }

        static SerializedProperty FindProperty(SerializedObject so, string field)
        {
            var sp = so.FindProperty(field);
            if (sp != null) return sp;

            sp = so.FindProperty("m_" + field);
            if (sp != null) return sp;

            // e.g. "clearFlags" → "m_ClearFlags"
            if (field.Length > 0)
            {
                sp = so.FindProperty("m_" + char.ToUpper(field[0]) + field.Substring(1));
                if (sp != null) return sp;
            }

            var iter = so.GetIterator();
            if (iter.NextVisible(true))
                while (iter.NextVisible(false))
                {
                    if (string.Equals(iter.name, field, StringComparison.OrdinalIgnoreCase))
                        return so.FindProperty(iter.propertyPath);
                    // strip m_ prefix before comparing (e.g. m_ClearFlags vs clearFlags)
                    var n = iter.name;
                    if (n.Length > 2 && n[0] == 'm' && n[1] == '_' &&
                        string.Equals(n.Substring(2), field, StringComparison.OrdinalIgnoreCase))
                        return so.FindProperty(iter.propertyPath);
                }

            return null;
        }

        private sealed class ComponentAddCmd : IAgentCommand
        {
            public string    Cmd         => "component_add";
            public string    Description => "Add a component to a GameObject by type name.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("path", "string", "", ""),
                new ArgSpec("type", "string", "", ""),
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
                    e["message"] = SceneBridge.IsAmbiguousTypeName(p.type)
                        ? $"Ambiguous type name '{p.type}' — use a fully qualified name e.g. 'UnityEngine.UI.Button'"
                        : $"Unknown component type: {p.type}";
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
