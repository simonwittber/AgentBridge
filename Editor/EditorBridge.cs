using System.Text.Json.Nodes;
using UnityEditor;
using UnityEngine;

namespace LLMDevTools
{
    [InitializeOnLoad]
    public static class EditorBridge
    {
        static EditorBridge()
        {
            AgentBridge.Register(new PlayModeCmd());
        }

        private sealed class PlayModeCmd : IAgentCommand
        {
            public string    Cmd         => "play_mode";
            public string    Description => "Enter or exit play mode, or query the current state.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("action", "string", "status", "enter, exit, or status"),
            };

            public JsonObject Execute(string uid, string requestJson)
            {
                var p = JsonUtility.FromJson<Params>(requestJson);
                switch (p.action)
                {
                    case "enter":
                        EditorApplication.isPlaying = true;
                        var r1 = AgentBridge.MakeResponse(uid, Cmd, "ok");
                        r1["playing"] = true;
                        return r1;
                    case "exit":
                        EditorApplication.isPlaying = false;
                        var r2 = AgentBridge.MakeResponse(uid, Cmd, "ok");
                        r2["playing"] = false;
                        return r2;
                    default:
                        var resp = AgentBridge.MakeResponse(uid, Cmd, "ok");
                        resp["playing"] = EditorApplication.isPlaying;
                        resp["paused"]  = EditorApplication.isPaused;
                        return resp;
                }
            }

            [System.Serializable] private class Params { public string action = "status"; }
        }
    }
}
