using System;
using System.Text.Json.Nodes;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace LLMDevTools
{
    [InitializeOnLoad]
    internal static class PackageCommands
    {
        private static Request _pending;
        private static string  _pendingUid;
        private static string  _pendingCmd;

        static PackageCommands()
        {
            AgentBridge.Register(new PackageListCmd());
            AgentBridge.Register(new PackageAddCmd());
            AgentBridge.Register(new PackageRemoveCmd());
            AgentBridge.Register(new PackageSearchCmd());
            EditorApplication.update += OnUpdate;
        }

        private static void OnUpdate()
        {
            if (_pending == null || _pending.Status == StatusCode.InProgress) return;

            JsonObject resp;
            if (_pending.Status == StatusCode.Failure)
            {
                resp = AgentBridge.MakeResponse(_pendingUid, _pendingCmd, "error");
                resp["message"] = _pending.Error?.message ?? "Unknown error";
            }
            else
            {
                resp = _pendingCmd switch
                {
                    "package_list"   => BuildListResponse(),
                    "package_add"    => BuildAddResponse(),
                    "package_search" => BuildSearchResponse(),
                    _                => AgentBridge.MakeResponse(_pendingUid, _pendingCmd, "ok"),
                };
            }

            _pending    = null;
            _pendingUid = null;
            _pendingCmd = null;
            AgentBridge.Respond(resp);
        }

        private static JsonObject BuildListResponse()
        {
            var arr = new JsonArray();
            foreach (var pkg in ((ListRequest)_pending).Result)
                arr.Add(new JsonObject
                {
                    ["name"]        = pkg.name,
                    ["version"]     = pkg.version,
                    ["displayName"] = pkg.displayName,
                    ["source"]      = pkg.source.ToString(),
                });
            var resp = AgentBridge.MakeResponse(_pendingUid, _pendingCmd, "ok");
            resp["packages"] = arr;
            return resp;
        }

        private static JsonObject BuildAddResponse()
        {
            var result = ((AddRequest)_pending).Result;
            var resp   = AgentBridge.MakeResponse(_pendingUid, _pendingCmd, "ok");
            resp["name"]    = result.name;
            resp["version"] = result.version;
            return resp;
        }

        private static JsonObject BuildSearchResponse()
        {
            var arr = new JsonArray();
            foreach (var pkg in ((SearchRequest)_pending).Result)
            {
                var versions = new JsonArray();
                if (pkg.versions?.all != null)
                    foreach (var v in pkg.versions.all)
                        versions.Add(v);
                arr.Add(new JsonObject
                {
                    ["name"]        = pkg.name,
                    ["version"]     = pkg.version,
                    ["displayName"] = pkg.displayName,
                    ["description"] = pkg.description,
                    ["versions"]    = versions,
                });
            }
            var resp = AgentBridge.MakeResponse(_pendingUid, _pendingCmd, "ok");
            resp["packages"] = arr;
            return resp;
        }

        // ── Commands ──────────────────────────────────────────────────────────

        private sealed class PackageListCmd : IAgentCommand
        {
            public string    Cmd         => "package_list";
            public string    Description => "List installed Unity packages.";
            public ArgSpec[] Args        => Array.Empty<ArgSpec>();

            public JsonObject Execute(string uid, string requestJson)
            {
                _pendingUid = uid;
                _pendingCmd = Cmd;
                _pending    = Client.List(offlineMode: false);
                return null; // async — OnUpdate responds
            }
        }

        private sealed class PackageAddCmd : IAgentCommand
        {
            public string    Cmd         => "package_add";
            public string    Description => "Add or update a Unity package by identifier.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("identifier", "string", "", "Package identifier"),
            };

            public JsonObject Execute(string uid, string requestJson)
            {
                var p = JsonUtility.FromJson<Params>(requestJson);
                if (string.IsNullOrEmpty(p.identifier))
                {
                    var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                    e["message"] = "identifier is required";
                    return e;
                }
                _pendingUid = uid;
                _pendingCmd = Cmd;
                _pending    = Client.Add(p.identifier);
                return null; // async — OnUpdate responds
            }

            [Serializable] private class Params { public string identifier = ""; }
        }

        private sealed class PackageSearchCmd : IAgentCommand
        {
            public string    Cmd         => "package_search";
            public string    Description => "Search the Unity Package Registry.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("query", "string", "", "Search term"),
            };

            public JsonObject Execute(string uid, string requestJson)
            {
                var p = JsonUtility.FromJson<Params>(requestJson);
                if (string.IsNullOrEmpty(p.query))
                {
                    var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                    e["message"] = "query is required";
                    return e;
                }
                _pendingUid = uid;
                _pendingCmd = Cmd;
                _pending    = Client.Search(p.query);
                return null; // async — OnUpdate responds
            }

            [Serializable] private class Params { public string query = ""; }
        }

        private sealed class PackageRemoveCmd : IAgentCommand
        {
            public string    Cmd         => "package_remove";
            public string    Description => "Remove an installed Unity package.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("name", "string", "", "Package name"),
            };

            public JsonObject Execute(string uid, string requestJson)
            {
                var p = JsonUtility.FromJson<Params>(requestJson);
                if (string.IsNullOrEmpty(p.name))
                {
                    var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                    e["message"] = "name is required";
                    return e;
                }
                _pendingUid = uid;
                _pendingCmd = Cmd;
                _pending    = Client.Remove(p.name);
                return null; // async — OnUpdate responds
            }

            [Serializable] private class Params { public string name = ""; }
        }
    }
}
