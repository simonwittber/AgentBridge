using System;
using System.Text.Json.Nodes;
using UnityEditor;

namespace LLMDevTools
{
    [InitializeOnLoad]
    internal static class MenuCommands
    {
        static MenuCommands()
        {
            AgentBridge.Register(new MenuItemCmd());
            AgentBridge.Register(new UuidCmd());
        }

        private sealed class MenuItemCmd : IAgentCommand
        {
            public string    Cmd         => "menu_item";
            public string    Description => "Invoke a Unity menu item by its full path string.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("path", "string", "", "Full menu path, e.g. Tools/MyTool/DoSomething"),
            };

            public JsonObject Execute(string uid, string requestJson)
            {
                var node = JsonNode.Parse(requestJson);
                var path = node?["path"]?.GetValue<string>() ?? "";

                if (string.IsNullOrEmpty(path))
                {
                    var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                    e["message"] = "path is required";
                    return e;
                }
                bool executed = EditorApplication.ExecuteMenuItem(path);
                if (executed)
                {
                    var resp = AgentBridge.MakeResponse(uid, Cmd, "ok");
                    resp["path"] = path;
                    return resp;
                }
                else
                {
                    var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                    e["message"] = $"Menu item not found: {path}";
                    return e;
                }
            }
        }

        private sealed class UuidCmd : IAgentCommand
        {
            public string Cmd         => "uuid";
            public string Description => "Generate and return a new UUID v4.";

            public JsonObject Execute(string uid, string requestJson)
            {
                var resp = AgentBridge.MakeResponse(uid, Cmd, "ok");
                resp["uuid"] = Guid.NewGuid().ToString();
                return resp;
            }
        }
    }
}
