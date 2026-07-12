using System.IO;
using System.Text.Json.Nodes;
using UnityEditor;
using UnityEngine;

namespace LLMDevTools
{
    [InitializeOnLoad]
    public static class AssetMutationCommands
    {
        static AssetMutationCommands()
        {
            AgentBridge.Register(new AssetCreateCmd());
            AgentBridge.Register(new AssetWriteTextCmd());
            AgentBridge.Register(new AssetDeleteCmd());
            AgentBridge.Register(new AssetMoveCmd());
            AgentBridge.Register(new AssetCopyCmd());
        }

        private sealed class AssetCreateCmd : IAgentCommand
        {
            public string    Cmd         => "asset_create";
            public string    Description => "Create a new folder or material asset at the given path.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("path", "string", "", "Asset path to create (e.g. Assets/MyFolder)"),
                new ArgSpec("type", "string", "folder", "Asset type: folder or material"),
            };

            public JsonObject Execute(string uid, string requestJson)
            {
                var p = JsonUtility.FromJson<Params>(requestJson);
                if (string.IsNullOrEmpty(p.path))
                {
                    var err = AgentBridge.MakeResponse(uid, Cmd, "error");
                    err["message"] = "path is required";
                    return err;
                }

                EnsureFolder(System.IO.Path.GetDirectoryName(p.path).Replace('\\', '/'));

                string guid;
                if (p.type == "folder")
                {
                    string parent = System.IO.Path.GetDirectoryName(p.path).Replace('\\', '/');
                    string name   = System.IO.Path.GetFileName(p.path);
                    guid = AssetDatabase.CreateFolder(parent, name);
                }
                else if (p.type == "material")
                {
                    var mat = new Material(Shader.Find("Standard"));
                    AssetDatabase.CreateAsset(mat, p.path);
                    AssetDatabase.SaveAssets();
                    guid = AssetDatabase.AssetPathToGUID(p.path);
                }
                else
                {
                    var err = AgentBridge.MakeResponse(uid, Cmd, "error");
                    err["message"] = "Unknown type: " + p.type + ". Use folder or material.";
                    return err;
                }

                var resp = AgentBridge.MakeResponse(uid, Cmd, "ok");
                resp["path"] = p.path;
                resp["guid"] = guid;
                return resp;
            }

            [System.Serializable] private class Params { public string path = ""; public string type = "folder"; }
        }

        private sealed class AssetWriteTextCmd : IAgentCommand
        {
            public string    Cmd         => "asset_write_text";
            public string    Description => "Write a text file (script, shader, JSON, etc.) under Assets/ and reimport it. Creates parent folders automatically.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("path",    "string", "", "Asset path, e.g. Assets/Scripts/Foo.cs or Assets/Shaders/Bar.shader"),
                new ArgSpec("content", "string", "", "Full text content to write"),
            };

            public JsonObject Execute(string uid, string requestJson)
            {
                if (!SceneBridge.TryParseRequest(requestJson, out var node))
                {
                    var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                    e["message"] = "Invalid JSON";
                    return e;
                }

                var path    = node?["path"]?.GetValue<string>();
                var content = node?["content"]?.GetValue<string>() ?? "";

                if (string.IsNullOrEmpty(path))
                {
                    var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                    e["message"] = "path is required";
                    return e;
                }
                if (!PathUtils.IsUnderAssets(path))
                {
                    var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                    e["message"] = $"Path '{path}' must be under Assets/";
                    return e;
                }

                EnsureFolder(Path.GetDirectoryName(path)?.Replace('\\', '/'));
                File.WriteAllText(path, content);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

                var resp = AgentBridge.MakeResponse(uid, Cmd, "ok");
                resp["path"] = path;
                return resp;
            }
        }

        private static void EnsureFolder(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || AssetDatabase.IsValidFolder(assetPath)) return;
            EnsureFolder(System.IO.Path.GetDirectoryName(assetPath).Replace('\\', '/'));
            string parent = System.IO.Path.GetDirectoryName(assetPath).Replace('\\', '/');
            string name   = System.IO.Path.GetFileName(assetPath);
            AssetDatabase.CreateFolder(parent, name);
        }

        private sealed class AssetDeleteCmd : IAgentCommand
        {
            public string    Cmd         => "asset_delete";
            public string    Description => "Delete an asset at the given path.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("path", "string", "", "Asset path to delete"),
            };

            public JsonObject Execute(string uid, string requestJson)
            {
                var p = JsonUtility.FromJson<Params>(requestJson);
                bool ok = AssetDatabase.DeleteAsset(p.path);
                if (!ok)
                {
                    var err = AgentBridge.MakeResponse(uid, Cmd, "error");
                    err["message"] = "Asset not found or could not be deleted";
                    return err;
                }
                return AgentBridge.MakeResponse(uid, Cmd, "ok");
            }

            [System.Serializable] private class Params { public string path = ""; }
        }

        private sealed class AssetMoveCmd : IAgentCommand
        {
            public string    Cmd         => "asset_move";
            public string    Description => "Move an asset from one path to another.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("from", "string", "", "Source asset path"),
                new ArgSpec("to",   "string", "", "Destination asset path"),
            };

            public JsonObject Execute(string uid, string requestJson)
            {
                var p = JsonUtility.FromJson<Params>(requestJson);
                string errMsg = AssetDatabase.MoveAsset(p.from, p.to);
                if (!string.IsNullOrEmpty(errMsg))
                {
                    var err = AgentBridge.MakeResponse(uid, Cmd, "error");
                    err["message"] = errMsg;
                    return err;
                }
                var resp = AgentBridge.MakeResponse(uid, Cmd, "ok");
                resp["to"] = p.to;
                return resp;
            }

            [System.Serializable] private class Params { public string from = ""; public string to = ""; }
        }

        private sealed class AssetCopyCmd : IAgentCommand
        {
            public string    Cmd         => "asset_copy";
            public string    Description => "Copy an asset from one path to another.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("from", "string", "", "Source asset path"),
                new ArgSpec("to",   "string", "", "Destination asset path"),
            };

            public JsonObject Execute(string uid, string requestJson)
            {
                var p = JsonUtility.FromJson<Params>(requestJson);
                bool ok = AssetDatabase.CopyAsset(p.from, p.to);
                if (!ok)
                {
                    var err = AgentBridge.MakeResponse(uid, Cmd, "error");
                    err["message"] = "Asset copy failed";
                    return err;
                }
                var resp = AgentBridge.MakeResponse(uid, Cmd, "ok");
                resp["to"] = p.to;
                return resp;
            }

            [System.Serializable] private class Params { public string from = ""; public string to = ""; }
        }
    }
}
