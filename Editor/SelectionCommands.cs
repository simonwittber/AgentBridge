using System.Text.Json.Nodes;
using UnityEditor;
using UnityEngine;

namespace LLMDevTools
{
    [InitializeOnLoad]
    public static class SelectionCommands
    {
        static SelectionCommands()
        {
            AgentBridge.Register(new SelectionGetCmd());
        }

        private sealed class SelectionGetCmd : IAgentCommand
        {
            public string    Cmd         => "selection_get";
            public string    Description => "Return currently selected GameObjects and assets.";
            public ArgSpec[] Args        => new ArgSpec[0];

            public JsonObject Execute(string uid, string requestJson)
            {
                var gameObjectsArray = new JsonArray();
                foreach (var go in Selection.gameObjects)
                {
                    gameObjectsArray.Add(new JsonObject
                    {
                        ["name"]   = go.name,
                        ["path"]   = GetPath(go),
                        ["active"] = go.activeSelf,
                    });
                }

                var assetsArray = new JsonArray();
                foreach (var guid in Selection.assetGUIDs)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    var type         = AssetDatabase.GetMainAssetTypeAtPath(assetPath);
                    assetsArray.Add(new JsonObject
                    {
                        ["path"] = assetPath,
                        ["guid"] = guid,
                        ["type"] = type?.Name ?? "",
                    });
                }

                var resp = AgentBridge.MakeResponse(uid, Cmd, "ok");
                resp["gameObjects"] = gameObjectsArray;
                resp["assets"]      = assetsArray;
                return resp;
            }
        }

        private static string GetPath(GameObject go)
        {
            string path = go.name;
            var parent  = go.transform.parent;
            while (parent != null)
            {
                path   = parent.name + "/" + path;
                parent = parent.parent;
            }
            return path;
        }
    }
}
