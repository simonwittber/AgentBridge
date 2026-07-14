using System;
using System.Text.Json.Nodes;
using UnityEditor;
using UnityEngine;

namespace LLMDevTools
{
    [InitializeOnLoad]
    internal static class AssetCommands
    {
        static AssetCommands()
        {
            AgentBridge.Register(new AssetInfoCmd());
            AgentBridge.Register(new AssetSetCmd());
            AgentBridge.Register(new AssetFindCmd());
        }

        private sealed class AssetInfoCmd : IAgentCommand
        {
            public string    Cmd         => "asset_info";
            public string    Description => "Get GUID and serialized importer settings for an asset.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("path", "string", "", "Asset path"),
            };

            public JsonObject Execute(string uid, string requestJson)
            {
                var p        = JsonUtility.FromJson<Params>(requestJson);
                var importer = AssetImporter.GetAtPath(p.path);
                if (importer == null)
                {
                    var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                    e["message"] = $"Asset not found: {p.path}";
                    return e;
                }
                string guid  = AssetDatabase.AssetPathToGUID(p.path);
                var resp     = AgentBridge.MakeResponse(uid, Cmd, "ok");
                resp["guid"]          = guid;
                resp["importer_type"] = importer.GetType().Name;
                resp["settings"]      = SceneBridge.SerializedObjectToJson(importer);
                return resp;
            }

            [Serializable] private class Params { public string path = ""; }
        }

        private sealed class AssetSetCmd : IAgentCommand
        {
            public string    Cmd         => "asset_set";
            public string    Description => "Set an importer field on an asset and reimport it.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("path",  "string", "", "Asset path"),
                new ArgSpec("field", "string", "", "Serialized importer property name"),
                new ArgSpec("value", "any",    "", "Value: number, bool, string, or JSON object"),
            };

            public JsonObject Execute(string uid, string requestJson)
            {
                if (!SceneBridge.TryParseRequest(requestJson, out var req))
                {
                    var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                    e["message"] = "Invalid request JSON";
                    return e;
                }

                string   assetPath = req["path"]? .GetValue<string>() ?? "";
                string   field     = req["field"]?.GetValue<string>() ?? "";
                JsonNode value     = req["value"];

                var importer = AssetImporter.GetAtPath(assetPath);
                if (importer == null)
                {
                    var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                    e["message"] = $"Asset not found: {assetPath}";
                    return e;
                }
                var so = new SerializedObject(importer);
                var sp = so.FindProperty(field);
                if (sp == null)
                {
                    var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                    e["message"] = $"Field not found: {field}";
                    return e;
                }
                if (!SceneBridge.JsonToSp(sp, value))
                {
                    var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                    e["message"] = $"Failed to set '{field}' ({sp.propertyType}) — see Console for details.";
                    return e;
                }
                so.ApplyModifiedProperties();
                importer.SaveAndReimport();
                return AgentBridge.MakeResponse(uid, Cmd, "ok");
            }
        }

        private sealed class AssetFindCmd : IAgentCommand
        {
            public string    Cmd         => "asset_find";
            public string    Description => "Find project assets using an AssetDatabase search filter.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("filter", "string", "",   "AssetDatabase filter (t:Type, l:Label)"),
                new ArgSpec("limit",  "int",    "50", "Maximum results to return"),
            };

            public JsonObject Execute(string uid, string requestJson)
            {
                var p     = JsonUtility.FromJson<Params>(requestJson);
                int limit = Math.Max(1, Math.Min(500, p.limit));

                var guids   = AssetDatabase.FindAssets(p.filter ?? "");
                var results = new JsonArray();
                int count   = Math.Min(guids.Length, limit);
                for (int i = 0; i < count; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    results.Add(new JsonObject { ["guid"] = guids[i], ["path"] = path });
                }

                var resp = AgentBridge.MakeResponse(uid, Cmd, "ok");
                resp["assets"] = results;
                resp["total"]  = guids.Length;
                return resp;
            }

            [Serializable] private class Params { public string filter = ""; public int limit = 50; }
        }
    }
}
