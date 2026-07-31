using System.Text.Json.Nodes;
using UnityEditor;

namespace LLMDevTools
{
    internal sealed class PlayEnterCmd : IAgentCommand
    {
        public string    Cmd         => "play_enter";
        public string    Description => "Enter play mode. Fires a play_mode_entered notification when the transition completes.";
        public ArgSpec[] Args        => System.Array.Empty<ArgSpec>();

        public JsonObject Execute(string uid, string requestJson)
        {
            EditorApplication.isPlaying = true;
            var resp = AgentBridge.MakeResponse(uid, Cmd, "ok");
            resp["playing"] = true;
            return resp;
        }
    }

    internal sealed class PlayExitCmd : IAgentCommand
    {
        public string    Cmd         => "play_exit";
        public string    Description => "Exit play mode. Fires a play_mode_exited notification when the transition completes.";
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
