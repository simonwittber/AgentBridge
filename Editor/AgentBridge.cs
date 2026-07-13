using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.SceneManagement;

[assembly: InternalsVisibleTo("LLMDevTools.Editor.Tests")]

namespace LLMDevTools
{
    public readonly struct ArgSpec
    {
        public readonly string Name, Type, Default, Description;
        public ArgSpec(string name, string type, string @default, string description)
        { Name = name; Type = type; Default = @default; Description = description; }
    }

    public interface IAgentCommand
    {
        string     Cmd         { get; }
        string     Description => null;
        ArgSpec[]  Args        => null;
        JsonObject Execute(string uid, string requestJson);
    }

    [InitializeOnLoad]
    public static class AgentBridge
    {
        private enum State { Idle, Busy, Reloading }

        private const string RequestsDir  = "Temp/agent/requests";
        private const string ResponsesDir = "Temp/agent/responses";
        private const string SessionPath  = "Temp/agent/session.json";
        internal const string LogPath     = "Temp/agent/log";
        private const double PollInterval = 0.25;
        private const double Heartbeat    = 5.0;

        private static readonly long   _sessionId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        private static readonly double _startTime;

        private static State  _state;
        private static string _pendingUid;
        private static string _pendingCmd;
        private static string _pendingFile;
        private static string _pendingRequestJson;
        private static double _nextPoll;
        private static double _nextHeartbeat;
        private static bool   _wasPlayingBeforeReload;

        private static readonly List<CompilerMessage>              _compileMessages = new();
        private static readonly Dictionary<string, IAgentCommand> _handlers        = new();
        private static int               _compileErrorCount;
        private static volatile bool     _scriptsDirty;
        private static volatile bool     _refreshNeeded;
        private static bool              _wasUpdating;
        private static FileSystemWatcher _assetWatcher;

        internal static void MarkScriptsDirty()   => _scriptsDirty  = true;
        internal static void ClearScriptsDirty()  => _scriptsDirty  = false;
        internal static void MarkRefreshNeeded()  => _refreshNeeded = true;
        internal static void ClearRefreshNeeded() => _refreshNeeded = false;

        private static readonly HashSet<string> _noWarnCmds = new()
            { "compile", "refresh", "status", "focus", "commands", "asset_write_text" };

        private static readonly HashSet<string> _noRefreshWarnCmds = new()
        {
            "compile", "refresh", "status", "focus", "commands",
            "asset_write_text", "asset_create", "asset_delete", "asset_move", "asset_copy", "asset_set",
        };

        internal static void InjectWarnings(JsonObject resp)
        {
            string cmd  = resp["cmd"]?.GetValue<string>() ?? "";
            string warn = "";
            if (_scriptsDirty  && !_noWarnCmds.Contains(cmd))
                warn = "Scripts were modified since the last compile — call 'compile' before relying on these results.";
            if (_refreshNeeded && !_noRefreshWarnCmds.Contains(cmd))
            {
                const string r = "Assets have changed since the last refresh — call 'refresh' to sync the AssetDatabase.";
                warn = warn.Length > 0 ? warn + " " + r : r;
            }
            if (warn.Length > 0) resp["_warning"] = warn;
        }

        public static void Register(IAgentCommand handler) => _handlers[handler.Cmd] = handler;

        public static JsonObject TestInvoke(string cmd, string requestJson = "{}")
        {
            if (!_handlers.TryGetValue(cmd, out var handler)) return null;
            return handler.Execute("test", requestJson);
        }

        // ── Boot ──────────────────────────────────────────────────────────────

        static AgentBridge()
        {
            _startTime = EditorApplication.timeSinceStartup;

            Register(new StatusCmd());
            Register(new CommandsCmd());
            Register(new CompileCmd());
            Register(new RefreshCmd());
            Register(new FocusCmd());

            try
            {
                Directory.CreateDirectory(RequestsDir);
                Directory.CreateDirectory(ResponsesDir);
            }
            catch { }

            AppendLog(new JsonObject
            {
                ["_marker"] = "domain_reload_complete",
                ["_ts"]     = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            });

            // Register callbacks before replaying so async completions are caught.
            AssemblyReloadEvents.beforeAssemblyReload       += OnBeforeReload;
            CompilationPipeline.compilationStarted          += OnCompilationStarted;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;
            CompilationPipeline.compilationFinished         += OnCompilationFinished;
            EditorApplication.update                        += OnUpdate;
            EditorApplication.playModeStateChanged          += OnPlayModeChanged;

            WriteSession();

            // Delay until all [InitializeOnLoad] ctors have run so every handler is registered.
            EditorApplication.delayCall += ReplayPending;

            SetupAssetWatcher();
            AppDomain.CurrentDomain.DomainUnload += (_, __) => { _assetWatcher?.Dispose(); };
        }

        private static void ReplayPending()
        {
            try
            {
                var files = Directory.GetFiles(RequestsDir, "*.json");
                if (files.Length == 0) return;
                Array.Sort(files);
                var file = files[0];
                var line = File.ReadAllText(file).Trim();
                var req  = JsonUtility.FromJson<Request>(line);
                if (!string.IsNullOrEmpty(req?.uid) && !string.IsNullOrEmpty(req?.cmd))
                    Dispatch(req.uid, req.cmd, line, file);
            }
            catch { }
        }

        // ── Callbacks ─────────────────────────────────────────────────────────

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode)
                _wasPlayingBeforeReload = true;
            else if (change == PlayModeStateChange.ExitingPlayMode)
                _wasPlayingBeforeReload = false;
        }

        private static void OnBeforeReload()
        {
            _state = State.Reloading;

            // Delete any stale response for the pending command so it can't be
            // picked up after replay with mismatched results.
            if (!string.IsNullOrEmpty(_pendingUid))
            {
                var staleResp = Path.Combine(ResponsesDir, _pendingUid + ".json");
                try { if (File.Exists(staleResp)) File.Delete(staleResp); } catch { }
            }

            var reloadLog = new JsonObject
            {
                ["_marker"]     = "domain_reload_start",
                ["_ts"]         = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ["pending_cmd"] = _pendingCmd ?? "",
            };
            if (_wasPlayingBeforeReload)
                reloadLog["note"] = "Play mode was active and has been exited by this reload. Re-enter play mode after the domain_reload_complete marker.";
            AppendLog(reloadLog);
            WriteSession();
            // Request file stays on disk — replayed automatically after reload.
        }

        private static void OnCompilationStarted(object _) { _compileErrorCount = 0; _scriptsDirty = false; }

        private static void OnAssemblyCompilationFinished(string _, CompilerMessage[] messages)
        {
            foreach (var m in messages)
                if (m.type == CompilerMessageType.Error) _compileErrorCount++;
            if (_pendingCmd == "compile")
                _compileMessages.AddRange(messages);
        }

        private static void OnCompilationFinished(object _)
        {
            if (_pendingCmd != "compile") return;

            var errors   = new JsonArray();
            var warnings = new JsonArray();
            foreach (var m in _compileMessages)
            {
                var entry = new JsonObject
                {
                    ["text"]   = m.message,
                    ["file"]   = m.file,
                    ["line"]   = m.line,
                    ["column"] = m.column,
                };
                (m.type == CompilerMessageType.Error ? errors : warnings).Add(entry);
            }
            _compileMessages.Clear();

            var resp = MakeResponse(_pendingUid, "compile", errors.Count > 0 ? "error" : "ok");
            resp["errors"]   = errors;
            resp["warnings"] = warnings;
            Respond(resp);
        }

        private static void OnUpdate()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now < _nextPoll) return;
            _nextPoll = now + PollInterval;

            bool isUpdating = EditorApplication.isUpdating;
            if (_wasUpdating && !isUpdating) _refreshNeeded = false;
            _wasUpdating = isUpdating;

            if (_state == State.Idle)
                ReadNext();

            if (_state == State.Busy && _pendingCmd == "refresh")
            {
                if (!EditorApplication.isUpdating)
                    Respond(MakeResponse(_pendingUid, "refresh", "ok"));
            }

            if (now >= _nextHeartbeat)
            {
                _nextHeartbeat = now + Heartbeat;
                WriteSession();
            }
        }

        // ── Queue read ────────────────────────────────────────────────────────

        private static void ReadNext()
        {
            try
            {
                var files = Directory.GetFiles(RequestsDir, "*.json");
                if (files.Length == 0) return;
                Array.Sort(files);
                var file = files[0];
                var line = File.ReadAllText(file).Trim();
                if (string.IsNullOrEmpty(line)) { SafeDelete(file); return; }
                var req = JsonUtility.FromJson<Request>(line);
                if (!string.IsNullOrEmpty(req?.uid) && !string.IsNullOrEmpty(req?.cmd))
                    Dispatch(req.uid, req.cmd, line, file);
                else
                    SafeDelete(file);
            }
            catch (IOException) { }
        }

        // ── Dispatch ──────────────────────────────────────────────────────────

        private static void Dispatch(string uid, string cmd, string json, string file)
        {
            _state              = State.Busy;
            _pendingUid         = uid;
            _pendingCmd         = cmd;
            _pendingFile        = file;
            _pendingRequestJson = json;

            if (_handlers.TryGetValue(cmd, out var handler))
            {
                JsonObject resp;
                try   { resp = handler.Execute(uid, json); }
                catch (Exception ex)
                {
                    resp = MakeResponse(uid, cmd, "error");
                    resp["message"] = ex.Message;
                }
                if (resp != null) Respond(resp);
                // null = async handler; it will call Respond() when done
            }
            else
            {
                var resp = MakeResponse(uid, cmd, "error");
                resp["message"] = $"Unknown command: {cmd}";
                Respond(resp);
            }
        }

        // ── Response ──────────────────────────────────────────────────────────

        public static void Respond(JsonObject resp)
        {
            try
            {
                var uid = resp["uid"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(uid))
                {
                    Directory.CreateDirectory(ResponsesDir);
                    InjectWarnings(resp);
                    File.WriteAllText(Path.Combine(ResponsesDir, uid + ".json"), resp.ToJsonString());
                    if (_pendingFile != null && File.Exists(_pendingFile)) SafeDelete(_pendingFile);
                    AppendLog(BuildLogEntry(resp));
                }
            }
            catch (IOException) { }
            _pendingUid         = null;
            _pendingCmd         = null;
            _pendingFile        = null;
            _pendingRequestJson = null;
            _state              = State.Idle;
            WriteSession();
        }

        private static JsonObject BuildLogEntry(JsonObject resp)
        {
            if (string.IsNullOrEmpty(_pendingRequestJson)) return resp;
            try
            {
                if (JsonNode.Parse(_pendingRequestJson) is not JsonObject reqObj) return resp;
                var args = new JsonObject();
                foreach (var kv in reqObj)
                    if (kv.Key != "uid" && kv.Key != "cmd")
                        args[kv.Key] = kv.Value?.DeepClone();
                if (args.Count == 0) return resp;
                var entry = JsonNode.Parse(resp.ToJsonString()) as JsonObject ?? resp;
                entry["_req_args"] = args;
                return entry;
            }
            catch { return resp; }
        }

        private static void AppendLog(JsonObject entry)
        {
            try { File.AppendAllText(LogPath, entry.ToJsonString() + "\n"); }
            catch { }
        }

        private static void SafeDelete(string path)
        {
            try { File.Delete(path); }
            catch (Exception ex)
            {
                AppendLog(new JsonObject
                {
                    ["_marker"] = "delete_failed",
                    ["path"]    = path,
                    ["error"]   = ex.Message,
                });
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static void WriteSession()
        {
            try
            {
                var scene = SceneManager.GetActiveScene();
                File.WriteAllText(SessionPath, new JsonObject
                {
                    ["pid"]            = System.Diagnostics.Process.GetCurrentProcess().Id,
                    ["session_id"]     = _sessionId,
                    ["state"]          = _state == State.Reloading ? "reloading" : _state == State.Busy ? "busy" : "idle",
                    ["active_scene"]   = scene.name,
                    // scene_dirty: if true, scene_new/scene_open will trigger a save-dialog unless scene_save is called first
                    ["scene_dirty"]    = scene.isDirty,
                    ["play_mode"]      = EditorApplication.isPlaying,
                    ["compile_errors"]  = _compileErrorCount,
                    ["console_errors"]  = ConsoleBridge.ErrorCount,
                    ["written_at"]      = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                }.ToJsonString());
            }
            catch (IOException) { }
        }

        public static JsonObject MakeResponse(string uid, string cmd, string status) => new()
        {
            ["uid"]             = uid,
            ["cmd"]             = cmd,
            ["status"]          = status,
            ["session_id"]      = _sessionId,
            ["compile_errors"]  = _compileErrorCount,
            ["console_errors"]  = ConsoleBridge.ErrorCount,
        };

#if UNITY_EDITOR_WIN
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
#endif

        internal static void FocusUnityWindow()
        {
#if UNITY_EDITOR_WIN
            var hwnd = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            ShowWindow(hwnd, 9);
            SetForegroundWindow(hwnd);
#endif
        }

        [Serializable] private class Request { public string uid; public string cmd; }

        // ── Asset watcher ─────────────────────────────────────────────────────

        private static void SetupAssetWatcher()
        {
            try
            {
                if (!Directory.Exists("Assets")) return;
                _assetWatcher = new FileSystemWatcher("Assets")
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
                    EnableRaisingEvents = true,
                };
                _assetWatcher.Changed += (_, e) => HandleAssetChange(e.Name);
                _assetWatcher.Created += (_, e) => HandleAssetChange(e.Name);
                _assetWatcher.Deleted += (_, e) => HandleAssetChange(e.Name);
                _assetWatcher.Renamed += (_, e) => HandleAssetChange(e.Name ?? e.OldName);
            }
            catch { }
        }

        private static void HandleAssetChange(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) return;
            _refreshNeeded = true;
            string ext = Path.GetExtension(name).ToLowerInvariant();
            if (ext == ".cs" || ext == ".asmdef" || ext == ".asmref")
                _scriptsDirty = true;
        }

        // ── Built-in commands ─────────────────────────────────────────────────

        private sealed class StatusCmd : IAgentCommand
        {
            public string Cmd         => "status";
            public string Description => "Query bridge liveness, session ID, and uptime.";
            public JsonObject Execute(string uid, string requestJson)
            {
                var resp = MakeResponse(uid, Cmd, "ok");
                resp["uptime_s"] = EditorApplication.timeSinceStartup - _startTime;
                resp["busy"]     = false;
                return resp;
            }
        }

        private sealed class CommandsCmd : IAgentCommand
        {
            public string Cmd         => "commands";
            public string Description => "List all available commands and their arguments.";
            public JsonObject Execute(string uid, string requestJson)
            {
                var list = new JsonArray();
                foreach (var h in _handlers.Values)
                {
                    var args = new JsonArray();
                    if (h.Args != null)
                        foreach (var a in h.Args)
                            args.Add(new JsonObject
                            {
                                ["name"]        = a.Name,
                                ["type"]        = a.Type,
                                ["default"]     = a.Default,
                                ["description"] = a.Description,
                            });
                    list.Add(new JsonObject
                    {
                        ["cmd"]         = h.Cmd,
                        ["description"] = h.Description ?? "",
                        ["args"]        = args,
                    });
                }
                var resp = MakeResponse(uid, Cmd, "ok");
                resp["commands"] = list;
                return resp;
            }
        }

        private sealed class CompileCmd : IAgentCommand
        {
            public string Cmd         => "compile";
            public string Description => "Request script compilation; returns structured errors and warnings.";
            public JsonObject Execute(string uid, string requestJson)
            {
                _compileMessages.Clear();
                FocusUnityWindow();
                AssetDatabase.Refresh();
                if (!EditorApplication.isCompiling)
                    CompilationPipeline.RequestScriptCompilation();
                return null; // async — OnCompilationFinished responds
            }
        }

        private sealed class RefreshCmd : IAgentCommand
        {
            public string Cmd         => "refresh";
            public string Description => "Trigger AssetDatabase.Refresh() and wait for completion.";
            public JsonObject Execute(string uid, string requestJson)
            {
                FocusUnityWindow();
                AssetDatabase.Refresh();
                return null; // async — OnUpdate responds
            }
        }

        private sealed class FocusCmd : IAgentCommand
        {
            public string Cmd         => "focus";
            public string Description => "Bring the Unity Editor window to the foreground. Required for scene-view animations and screenshots to update when Unity lacks OS focus.";
            public JsonObject Execute(string uid, string requestJson)
            {
                FocusUnityWindow();
                return MakeResponse(uid, Cmd, "ok");
            }
        }
    }
}
