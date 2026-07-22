using System.Text.Json.Nodes;
using UnityEditor;

namespace LLMDevTools
{
    [InitializeOnLoad]
    public static class EditorBridge
    {
        static EditorBridge()
        {
            AgentBridge.Register(new PlayEnterCmd());
            AgentBridge.Register(new PlayExitCmd());
        }

        private sealed class PlayEnterCmd : IAgentCommand
        {
            public string    Cmd         => "play_enter";
            public string    Description => "Enter play mode.";
            public bool      Core        => true;
            public ArgSpec[] Args        => System.Array.Empty<ArgSpec>();

            public JsonObject Execute(string uid, string requestJson)
            {
                EditorApplication.isPlaying = true;
                var resp = AgentBridge.MakeResponse(uid, Cmd, "ok");
                resp["playing"] = true;
                return resp;
            }
        }

        private sealed class PlayExitCmd : IAgentCommand
        {
            public string    Cmd         => "play_exit";
            public string    Description => "Exit play mode.";
            public bool      Core        => true;
            public ArgSpec[] Args        => System.Array.Empty<ArgSpec>();

            public JsonObject Execute(string uid, string requestJson)
            {
                EditorApplication.isPlaying = false;
                var resp = AgentBridge.MakeResponse(uid, Cmd, "ok");
                resp["playing"] = false;
                return resp;
            }
        }
    }
}
