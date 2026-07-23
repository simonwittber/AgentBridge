using System;
using System.Text.Json.Nodes;
using UnityEditor;
using UnityEngine;

namespace LLMDevTools
{
    [InitializeOnLoad]
    internal static class ScriptableObjectCommands
    {
        static ScriptableObjectCommands()
        {
            AgentBridge.Register(new ScriptableGetCmd());
            AgentBridge.Register(new ScriptableSetCmd());
        }

        private sealed class ScriptableGetCmd : IAgentCommand
        {
            public string    Cmd         => "scriptable_get";
            public string    Description => "Get a serialized field value from a ScriptableObject asset.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("path",  "string", "", "Asset path, e.g. Assets/Config/MyConfig.asset"),
                new ArgSpec("field", "string", "", "Serialized field name"),
            };

            public JsonObject Execute(string uid, string requestJson)
            {
                var p  = JsonUtility.FromJson<Params>(requestJson);
                var so = Load(p.path, out var err);
                if (so == null)
                {
                    var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                    e["message"] = err;
                    return e;
                }
                var sp = FindProperty(so, p.field);
                if (sp == null)
                {
                    var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                    e["message"] = $"Field not found: {p.field}";
                    return e;
                }
                var resp = AgentBridge.MakeResponse(uid, Cmd, "ok");
                resp["field"] = p.field;
                resp["value"] = SceneBridge.SpToJson(sp);
                return resp;
            }

            [Serializable] private class Params { public string path = ""; public string field = ""; }
        }

        private sealed class ScriptableSetCmd : IAgentCommand
        {
            public string    Cmd         => "scriptable_set";
            public string    Description => "Set a serialized field on a ScriptableObject asset and save it.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("path",  "string", "", "Asset path, e.g. Assets/Config/MyConfig.asset"),
                new ArgSpec("field", "string", "", "Serialized field name"),
                new ArgSpec("value", "any",    "", "number, bool, string, or JSON object"),
            };

            public JsonObject Execute(string uid, string requestJson)
            {
                if (!SceneBridge.TryParseRequest(requestJson, out var req))
                {
                    var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                    e["message"] = "Invalid request JSON";
                    return e;
                }

                string path  = req["path"]? .GetValue<string>() ?? "";
                string field = req["field"]?.GetValue<string>() ?? "";
                var    value = req["value"];

                var so = Load(path, out var err);
                if (so == null)
                {
                    var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                    e["message"] = err;
                    return e;
                }
                var sp = FindProperty(so, field);
                if (sp == null)
                {
                    var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                    e["message"] = $"Field not found: {field}";
                    return e;
                }
                if (!SceneBridge.JsonToSp(sp, value))
                {
                    var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                    e["message"] = $"Failed to set '{field}' ({sp.propertyType}): see Console for details.";
                    return e;
                }
                so.ApplyModifiedProperties();
                AssetDatabase.SaveAssetIfDirty(so.targetObject);
                return AgentBridge.MakeResponse(uid, Cmd, "ok");
            }
        }

        static SerializedObject Load(string assetPath, out string error)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                error = "path is required";
                return null;
            }
            var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);
            if (asset == null)
            {
                error = $"ScriptableObject not found at: {assetPath}";
                return null;
            }
            error = null;
            return new SerializedObject(asset);
        }

        static SerializedProperty FindProperty(SerializedObject so, string field)
        {
            if (string.IsNullOrEmpty(field)) return null;
            var sp = so.FindProperty(field);
            if (sp != null) return sp;
            sp = so.FindProperty("m_" + field);
            if (sp != null) return sp;
            if (field.Length > 0)
            {
                sp = so.FindProperty("m_" + char.ToUpper(field[0]) + field.Substring(1));
                if (sp != null) return sp;
            }
            return null;
        }
    }
}
