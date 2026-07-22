using System.Text.Json.Nodes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LLMDevTools
{
    [InitializeOnLoad]
    internal static class PrefabCommands
    {
        static PrefabCommands()
        {
            AgentBridge.Register(new PrefabOpenCmd());
            AgentBridge.Register(new PrefabSaveCmd());
        }

        private sealed class PrefabOpenCmd : IAgentCommand
        {
            public string    Cmd         => "prefab_open";
            public string    Description => "Open a prefab asset for editing in prefab stage.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("path", "string", "", ""),
            };

            public JsonObject Execute(string uid, string requestJson)
            {
                var p      = JsonUtility.FromJson<Params>(requestJson);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(p.path);
                if (prefab == null)
                {
                    var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                    e["message"] = $"Prefab not found: {p.path}";
                    return e;
                }
                AssetDatabase.OpenAsset(prefab);
                return AgentBridge.MakeResponse(uid, Cmd, "ok");
            }

            [System.Serializable] private class Params { public string path = ""; }
        }

        private sealed class PrefabSaveCmd : IAgentCommand
        {
            public string Cmd         => "prefab_save";
            public string Description => "";

            public JsonObject Execute(string uid, string requestJson)
            {
                var stage = PrefabStageUtility.GetCurrentPrefabStage();
                if (stage == null)
                {
                    var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                    e["message"] = "No prefab stage open";
                    return e;
                }
                string path  = stage.assetPath;
                bool   saved = PrefabUtility.SaveAsPrefabAsset(stage.prefabContentsRoot, path);
                StageUtility.GoBackToPreviousStage();
                var resp = AgentBridge.MakeResponse(uid, Cmd, saved ? "ok" : "error");
                resp["path"] = path;
                return resp;
            }
        }
    }
}
