using System.Text.Json.Nodes;
using UnityEditor;
using UnityEngine;

namespace LLMDevTools
{
    [InitializeOnLoad]
    public static class PlayerSettingsCommands
    {
        static PlayerSettingsCommands()
        {
            AgentBridge.Register(new PlayerSettingsGetCmd());
            AgentBridge.Register(new PlayerSettingsSetCmd());
        }

        private sealed class PlayerSettingsGetCmd : IAgentCommand
        {
            public string    Cmd         => "player_settings_get";
            public string    Description => "Return current PlayerSettings values.";
            public ArgSpec[] Args        => new ArgSpec[0];

            public JsonObject Execute(string uid, string requestJson)
            {
                var resp = AgentBridge.MakeResponse(uid, Cmd, "ok");
                resp["companyName"]           = PlayerSettings.companyName;
                resp["productName"]           = PlayerSettings.productName;
                resp["bundleVersion"]         = PlayerSettings.bundleVersion;
                resp["defaultScreenWidth"]    = PlayerSettings.defaultScreenWidth;
                resp["defaultScreenHeight"]   = PlayerSettings.defaultScreenHeight;
                resp["runInBackground"]       = PlayerSettings.runInBackground;
                resp["applicationIdentifier"] = PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone);
                return resp;
            }
        }

        private sealed class PlayerSettingsSetCmd : IAgentCommand
        {
            public string    Cmd         => "player_settings_set";
            public string    Description => "Set a PlayerSettings value by key.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("key",   "string", "", "Setting name to change"),
                new ArgSpec("value", "string", "", "New value (as string)"),
            };

            public JsonObject Execute(string uid, string requestJson)
            {
                var p = JsonUtility.FromJson<Params>(requestJson);

                try
                {
                    switch (p.key)
                    {
                        case "companyName":
                            PlayerSettings.companyName = p.value;
                            break;
                        case "productName":
                            PlayerSettings.productName = p.value;
                            break;
                        case "bundleVersion":
                            PlayerSettings.bundleVersion = p.value;
                            break;
                        case "defaultScreenWidth":
                            PlayerSettings.defaultScreenWidth = int.Parse(p.value);
                            break;
                        case "defaultScreenHeight":
                            PlayerSettings.defaultScreenHeight = int.Parse(p.value);
                            break;
                        case "runInBackground":
                            PlayerSettings.runInBackground = bool.Parse(p.value);
                            break;
                        case "applicationIdentifier":
                            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone, p.value);
                            break;
                        default:
                        {
                            var err = AgentBridge.MakeResponse(uid, Cmd, "error");
                            err["message"] = "Unknown setting key";
                            return err;
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    var err = AgentBridge.MakeResponse(uid, Cmd, "error");
                    err["message"] = "Parse error: " + ex.Message;
                    return err;
                }

                var resp = AgentBridge.MakeResponse(uid, Cmd, "ok");
                resp["key"]   = p.key;
                resp["value"] = p.value;
                return resp;
            }

            [System.Serializable] private class Params { public string key = ""; public string value = ""; }
        }
    }
}
