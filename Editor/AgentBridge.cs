using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

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
        private static int  _compileErrorCount;
        private static bool _scriptsDirty;

        internal static void MarkScriptsDirty()  => _scriptsDirty = true;
        internal static void ClearScriptsDirty() => _scriptsDirty = false;

        private static readonly System.Collections.Generic.HashSet<string> _noWarnCmds =
            new() { "compile", "refresh", "status", "focus", "commands" };

        internal static void InjectWarnings(JsonObject resp)
        {
            if (!_scriptsDirty) return;
            string cmd = resp["cmd"]?.GetValue<string>() ?? "";
            if (_noWarnCmds.Contains(cmd)) return;
            resp["_warning"] = "Scripts were modified since the last compile — call 'compile' before relying on these results.";
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
                File.WriteAllText(SessionPath, new JsonObject
                {
                    ["pid"]        = System.Diagnostics.Process.GetCurrentProcess().Id,
                    ["session_id"] = _sessionId,
                    ["state"]      = _state == State.Reloading ? "reloading" : _state == State.Busy ? "busy" : "idle",
                    ["written_at"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                }.ToJsonString());
            }
            catch (IOException) { }
        }

        public static JsonObject MakeResponse(string uid, string cmd, string status) => new()
        {
            ["uid"]            = uid,
            ["cmd"]            = cmd,
            ["status"]         = status,
            ["session_id"]     = _sessionId,
            ["compile_errors"] = _compileErrorCount,
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
