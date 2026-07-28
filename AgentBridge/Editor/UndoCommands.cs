using System.Text.Json.Nodes;
using UnityEditor;

namespace LLMDevTools
{
    internal sealed class UndoCmd : IAgentCommand
    {
        public string    Cmd         => "undo";
        public string    Description => "";
        public ArgSpec[] Args        => new ArgSpec[0];

        public JsonObject Execute(string uid, string requestJson)
        {
            Undo.PerformUndo();
            return AgentBridge.MakeResponse(uid, Cmd, "ok");
        }
    }

    internal sealed class RedoCmd : IAgentCommand
    {
        public string    Cmd         => "redo";
        public string    Description => "";
        public ArgSpec[] Args        => new ArgSpec[0];

        public JsonObject Execute(string uid, string requestJson)
        {
            Undo.PerformRedo();
            return AgentBridge.MakeResponse(uid, Cmd, "ok");
        }
    }
}
