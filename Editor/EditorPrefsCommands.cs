using System.Text.Json.Nodes;
using UnityEditor;
using UnityEngine;

namespace LLMDevTools
{
    [InitializeOnLoad]
    public static class EditorPrefsCommands
    {
        static EditorPrefsCommands()
        {
            AgentBridge.Register(new EditorPrefGetCmd());
            AgentBridge.Register(new EditorPrefSetCmd());
        }

        private sealed class EditorPrefGetCmd : IAgentCommand
        {
            public string    Cmd         => "editor_pref_get";
            public string    Description => "Get a value from EditorPrefs.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("key",  "string", "",       "EditorPrefs key"),
                new ArgSpec("type", "string", "string", "Value type: string, int, float, bool"),
            };

            public JsonObject Execute(string uid, string requestJson)
            {
                var p = JsonUtility.FromJson<Params>(requestJson);
                if (string.IsNullOrEmpty(p.key))
                {
                    var err = AgentBridge.MakeResponse(uid, Cmd, "error");
                    err["message"] = "key is required";
                    return err;
                }

                var resp = AgentBridge.MakeResponse(uid, Cmd, "ok");
                resp["exists"] = EditorPrefs.HasKey(p.key);
                switch (p.type)
                {
                    case "int":
                        resp["value"] = EditorPrefs.GetInt(p.key);
                        break;
                    case "float":
                        resp["value"] = (double)EditorPrefs.GetFloat(p.key);
                        break;
                    case "bool":
                        resp["value"] = EditorPrefs.GetBool(p.key);
                        break;
                    default:
                        resp["value"] = EditorPrefs.GetString(p.key);
                        break;
                }
                return resp;
            }

            [System.Serializable] private class Params { public string key = ""; public string type = "string"; }
        }

        private sealed class EditorPrefSetCmd : IAgentCommand
        {
            public string    Cmd         => "editor_pref_set";
            public string    Description => "Set a value in EditorPrefs.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("key",   "string", "",       "EditorPrefs key"),
                new ArgSpec("value", "string", "",       "Value to set (as string)"),
                new ArgSpec("type",  "string", "string", "Value type: string, int, float, bool"),
            };

            public JsonObject Execute(string uid, string requestJson)
            {
                var p = JsonUtility.FromJson<Params>(requestJson);
                if (string.IsNullOrEmpty(p.key))
                {
                    var err = AgentBridge.MakeResponse(uid, Cmd, "error");
                    err["message"] = "key is required";
                    return err;
                }

                try
                {
                    switch (p.type)
                    {
                        case "int":
                            EditorPrefs.SetInt(p.key, int.Parse(p.value));
                            break;
                        case "float":
                            EditorPrefs.SetFloat(p.key, float.Parse(p.value, System.Globalization.CultureInfo.InvariantCulture));
                            break;
                        case "bool":
                            EditorPrefs.SetBool(p.key, bool.Parse(p.value));
                            break;
                        default:
                            EditorPrefs.SetString(p.key, p.value);
                            break;
                    }
                }
                catch (System.Exception ex)
                {
                    var err = AgentBridge.MakeResponse(uid, Cmd, "error");
                    err["message"] = "Parse error: " + ex.Message;
                    return err;
                }

                return AgentBridge.MakeResponse(uid, Cmd, "ok");
            }

            [System.Serializable] private class Params { public string key = ""; public string value = ""; public string type = "string"; }
        }
    }
}
