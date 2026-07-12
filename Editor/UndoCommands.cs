using System.Text.Json.Nodes;
using UnityEditor;

namespace LLMDevTools
{
    [InitializeOnLoad]
    public static class UndoCommands
    {
        static UndoCommands()
        {
            AgentBridge.Register(new UndoCmd());
            AgentBridge.Register(new RedoCmd());
        }

        private sealed class UndoCmd : IAgentCommand
        {
            public string    Cmd         => "undo";
            public string    Description => "Perform an undo operation in the editor.";
            public ArgSpec[] Args        => new ArgSpec[0];

            public JsonObject Execute(string uid, string requestJson)
            {
                Undo.PerformUndo();
                return AgentBridge.MakeResponse(uid, Cmd, "ok");
            }
        }

        private sealed class RedoCmd : IAgentCommand
        {
            public string    Cmd         => "redo";
            public string    Description => "Perform a redo operation in the editor.";
            public ArgSpec[] Args        => new ArgSpec[0];

            public JsonObject Execute(string uid, string requestJson)
            {
                Undo.PerformRedo();
                return AgentBridge.MakeResponse(uid, Cmd, "ok");
            }
        }
    }
}
