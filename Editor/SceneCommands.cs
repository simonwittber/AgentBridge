using System;
using System.Text.Json.Nodes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LLMDevTools
{
    [InitializeOnLoad]
    internal static class SceneCommands
    {
        static SceneCommands()
        {
            AgentBridge.Register(new SceneInfoCmd());
            AgentBridge.Register(new SceneOpenCmd());
            AgentBridge.Register(new SceneSaveCmd());
            AgentBridge.Register(new SceneNewCmd());
        }

        private sealed class SceneInfoCmd : IAgentCommand
        {
            public string Cmd         => "scene_info";
            public string Description => "Get name, path, dirty flag, and root object count of the active scene.";

            public JsonObject Execute(string uid, string requestJson)
            {
                var scene = SceneManager.GetActiveScene();
                var resp  = AgentBridge.MakeResponse(uid, Cmd, "ok");
                resp["scene_name"] = scene.name;
                resp["path"]       = scene.path;
                resp["dirty"]      = scene.isDirty;
                resp["root_count"] = scene.rootCount;
                return resp;
            }
        }

        private sealed class SceneOpenCmd : IAgentCommand
        {
            public string    Cmd         => "scene_open";
            public string    Description => "Open a scene by asset path.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("path", "string", "",       "Asset path, e.g. Assets/Scenes/Main.unity"),
                new ArgSpec("mode", "string", "Single", "OpenSceneMode: Single or Additive"),
            };

            public JsonObject Execute(string uid, string requestJson)
            {
                var p = JsonUtility.FromJson<Params>(requestJson);
                if (string.IsNullOrEmpty(p.path))
                {
                    var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                    e["message"] = "path required";
                    return e;
                }
                var mode = p.mode == "Additive" ? OpenSceneMode.Additive : OpenSceneMode.Single;
                if (mode == OpenSceneMode.Single)
                    SceneBridge.SilentlyClearDirtyScenes();
                try
                {
                    var scene = EditorSceneManager.OpenScene(p.path, mode);
                    var resp  = AgentBridge.MakeResponse(uid, Cmd, "ok");
                    resp["scene_name"] = scene.name;
                    return resp;
                }
                catch (Exception ex)
                {
                    var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                    e["message"] = ex.Message;
                    return e;
                }
            }

            [Serializable] private class Params { public string path = ""; public string mode = "Single"; }
        }

        private sealed class SceneSaveCmd : IAgentCommand
        {
            public string    Cmd         => "scene_save";
            public string    Description => "Save the currently active scene.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("path", "string", "", "Asset path to save to; auto-generated if omitted"),
            };

            public JsonObject Execute(string uid, string requestJson)
            {
                var p     = JsonUtility.FromJson<Params>(requestJson);
                var scene = SceneManager.GetActiveScene();

                string savePath = !string.IsNullOrEmpty(p.path)    ? p.path
                                : !string.IsNullOrEmpty(scene.path) ? scene.path
                                : $"Assets/Scenes/{(string.IsNullOrEmpty(scene.name) ? "NewScene" : scene.name)}.unity";

                if (!PathUtils.IsUnderAssets(savePath))
                {
                    var err = AgentBridge.MakeResponse(uid, Cmd, "error");
                    err["message"] = $"Path '{savePath}' is outside Assets/";
                    return err;
                }
                var dir = System.IO.Path.GetDirectoryName(savePath)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);

                bool ok  = EditorSceneManager.SaveScene(scene, savePath, false);
                var resp = AgentBridge.MakeResponse(uid, Cmd, ok ? "ok" : "error");
                resp["path"] = scene.path;
                return resp;
            }

            [Serializable] private class Params { public string path = ""; }
        }

        private sealed class SceneNewCmd : IAgentCommand
        {
            public string    Cmd         => "scene_new";
            public string    Description => "Create a new scene, discarding the current one.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("setup", "string", "empty", "NewSceneSetup: empty or defaultGameObjects"),
            };

            public JsonObject Execute(string uid, string requestJson)
            {
                var p     = JsonUtility.FromJson<Params>(requestJson);
                var setup = p.setup == "defaultGameObjects" ? NewSceneSetup.DefaultGameObjects : NewSceneSetup.EmptyScene;
                SceneBridge.SilentlyClearDirtyScenes();
                EditorSceneManager.NewScene(setup, NewSceneMode.Single);
                return AgentBridge.MakeResponse(uid, Cmd, "ok");
            }

            [Serializable] private class Params { public string setup = "empty"; }
        }
    }
}
