using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
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
#if UNITY_7000_0_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public static class AgentBridge
    {
        private enum State { Idle, Busy, Reloading, AwaitCompile, AwaitRefresh }

        private const string RequestsDir       = "Temp/agent/requests";
        private const string ResponsesDir      = "Temp/agent/responses";
        private const string NotificationsDir  = "Temp/agent/notifications";
        private const string SessionPath       = "Temp/agent/session.json";
        internal const string LogPath          = "Temp/agent/log";
        private const double PollInterval      = 0.25;
        private const double Heartbeat         = 5.0;
        private const double AutoCompileDelay  = 10.0;

        private static readonly long   _sessionId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        private static readonly double _startTime;

        private static State  _state;
        private static string _pendingUid;
        private static string _pendingCmd;
        private static string _pendingFile;
        private static string _pendingRequestJson;
        private static string _pendingAgentId;
        private static double _nextPoll;
        private static double _nextHeartbeat;

        private static readonly List<CompilerMessage>              _compileMessages = new();
        private static readonly Dictionary<string, IAgentCommand> _handlers        = new();
        private static readonly ConcurrentQueue<MainThreadItem>   _mainThreadQueue = new();
        private static readonly int                               _mainThreadId    = System.Threading.Thread.CurrentThread.ManagedThreadId;
        private static int               _compileErrorCount;
        private static volatile bool     _scriptsDirty;
        private static volatile bool     _refreshNeeded;
        private static bool              _wasUpdating;
        private static FileSystemWatcher _assetWatcher;
        private static double            _lastScriptWrite = double.MinValue;
        private static string            _registrationPath;

        internal static void MarkScriptsDirty()   => _scriptsDirty  = true;
        internal static void ClearScriptsDirty()  => _scriptsDirty  = false;
        internal static void MarkRefreshNeeded()  => _refreshNeeded = true;
        internal static void ClearRefreshNeeded() => _refreshNeeded = false;

        private static readonly HashSet<string> _noCompileBlockCmds = new()
        {
            "compile", "refresh", "execute_script", "run_editor_tests", "run_playmode_tests",
            "status", "list_commands", "console_logs",
            "profiler_get_samples", "profiler_start", "profiler_stop", "profiler_clear",
            "asset_write_text", "asset_create", "asset_delete", "asset_move", "asset_copy", "asset_set",
        };

        private static readonly HashSet<string> _noRefreshBlockCmds = new()
        {
            "compile", "refresh",
            "status", "list_commands", "console_logs",
            "profiler_get_samples", "profiler_start", "profiler_stop", "profiler_clear",
            "asset_write_text", "asset_create", "asset_delete", "asset_move", "asset_copy", "asset_set",
        };

        internal static string CheckBlocked(string cmd)
        {
            if (_scriptsDirty && !_noCompileBlockCmds.Contains(cmd))
                return "Scripts have changed and need recompilation. Call 'compile' before running other commands.";
            if (_refreshNeeded && !_noRefreshBlockCmds.Contains(cmd))
                return "Assets have changed and need reimport. Call 'refresh' before running other commands.";
            return null;
        }

        internal static void Notify(string type, JsonObject data)
        {
            try
            {
                var ts  = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var uid = Guid.NewGuid().ToString("N")[..8];
                var entry = new JsonObject
                {
                    ["type"]       = type,
                    ["message"]    = DescribeNotification(type, data),
                    ["data"]       = data,
                    ["written_at"] = ts,
                };
                AppendLog(entry);
                Directory.CreateDirectory(NotificationsDir);
                File.WriteAllText(
                    Path.Combine(NotificationsDir, $"{ts:D20}-{uid}.json"),
                    entry.ToJsonString());
            }
            catch { }
        }

        // Builds the human-readable text that MCP clients show for a notification.
        // Clients that surface notifications as conversation content need non-empty text, and the structured data alone does not provide it.
        internal static string DescribeNotification(string type, JsonObject data)
        {
            string Field(string key) => data?[key]?.ToString();

            switch (type)
            {
                case "play_mode_entered": return "Unity entered play mode.";
                case "play_mode_exited":  return "Unity exited play mode.";
                case "compile_started":   return "Script compilation started.";
                case "refresh_started":   return "Asset refresh started.";
                case "refresh_finished":  return "Asset refresh finished.";
                case "script_started":    return "Script started.";
                case "script_completed":  return "Script completed.";
                case "script_failed":     return $"Script failed: {Field("message")}";
                case "scene_opened":      return $"Scene '{Field("name")}' opened ({Field("path")}, {Field("mode")}).";
                case "compile_finished":
                    return Field("error_count") is { } count && count != "0"
                        ? $"Script compilation finished with {count} error(s)."
                        : "Script compilation finished with no errors.";
                case "compile_failure":
                {
                    var errors  = data?["errors"] as JsonArray;
                    var first   = errors?.Count > 0 ? errors[0] : null;
                    var summary = $"Script compilation failed with {errors?.Count ?? 0} error(s).";
                    return first == null ? summary : $"{summary} First: {first["file"]}({first["line"]},{first["column"]}): {first["text"]}";
                }
                default:
                    return $"Unity notification: {type}.";
            }
        }

        public static void Register(IAgentCommand handler) => _handlers[handler.Cmd] = handler;

        internal static Action<JsonObject> _testRespondCapture;

        public static T RunOnMainThread<T>(Func<T> work)
        {
            if (System.Threading.Thread.CurrentThread.ManagedThreadId == _mainThreadId)
                return work();
            var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            _mainThreadQueue.Enqueue(new MainThreadItem { Work = () => work(), Result = tcs });
            return (T)tcs.Task.Result;
        }

        public static JsonObject TestInvoke(string cmd, string requestJson = "{}", int timeoutMs = 10000)
        {
            if (!_handlers.TryGetValue(cmd, out var handler)) return null;
            JsonObject captured = null;
            _testRespondCapture = r => captured = r;
            var result = handler.Execute("test", requestJson);
            if (result != null)
            {
                _testRespondCapture = null;
                return result;
            }
            // Async handler: pump the main-thread queue until Respond fires.
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (captured == null && DateTime.UtcNow < deadline)
            {
                while (_mainThreadQueue.TryDequeue(out var item))
                {
                    try   { item.Result.SetResult(item.Work()); }
                    catch (Exception ex) { item.Result.SetException(ex); }
                }
                System.Threading.Thread.Sleep(10);
            }
            _testRespondCapture = null;
            return captured;
        }

        // ── Boot ──────────────────────────────────────────────────────────────

        static AgentBridge()
        {
            _startTime = EditorApplication.timeSinceStartup;

            foreach (var type in TypeCache.GetTypesDerivedFrom<IAgentCommand>())
            {
                if (type.IsAbstract || type.IsInterface) continue;
                try { Register((IAgentCommand)Activator.CreateInstance(type, nonPublic: true)); }
                catch { }
            }

            try
            {
                Directory.CreateDirectory(RequestsDir);
                Directory.CreateDirectory(ResponsesDir);
                Directory.CreateDirectory(NotificationsDir);
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
            EditorSceneManager.sceneOpened                  += OnSceneOpened;

            _registrationPath = ComputeRegistrationPath();
            EditorApplication.quitting += DeleteGlobalRegistration;

            WriteSession();

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
                    Dispatch(req.uid, req.cmd, req.agent_id, line, file);
            }
            catch { }
        }

        // ── Callbacks ─────────────────────────────────────────────────────────

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                Notify("play_mode_entered", new JsonObject());
            }
            else if (change == PlayModeStateChange.EnteredEditMode)
            {
                Notify("play_mode_exited", new JsonObject());
            }
        }

        private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            Notify("scene_opened", new JsonObject
            {
                ["name"] = scene.name,
                ["path"] = scene.path,
                ["mode"] = mode.ToString(),
            });
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
            AppendLog(reloadLog);
            WriteSession();
            // Request file stays on disk — replayed automatically after reload.
        }

        private static void OnCompilationStarted(object _)
        {
            _compileErrorCount = 0;
            _scriptsDirty      = false;
            Notify("compile_started", new JsonObject());
        }

        private static void OnAssemblyCompilationFinished(string _, CompilerMessage[] messages)
        {
            foreach (var m in messages)
                if (m.type == CompilerMessageType.Error) _compileErrorCount++;
            if (_pendingCmd == "compile" || _state == State.AwaitCompile)
                _compileMessages.AddRange(messages);
        }

        private static void OnCompilationFinished(object _)
        {
            if (_pendingCmd == "compile")
            {
                _refreshNeeded = false;
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
                Notify("compile_finished", new JsonObject { ["error_count"] = _compileErrorCount });
                var resp = MakeResponse(_pendingUid, "compile", errors.Count > 0 ? "error" : "ok");
                resp["errors"]   = errors;
                resp["warnings"] = warnings;
                Respond(resp);
            }
            else if (_state == State.AwaitCompile)
            {
                _state = State.Idle;
                if (_compileErrorCount > 0)
                {
                    var errors = new JsonArray();
                    foreach (var m in _compileMessages)
                        if (m.type == CompilerMessageType.Error)
                            errors.Add(new JsonObject
                            {
                                ["text"]   = m.message,
                                ["file"]   = m.file,
                                ["line"]   = m.line,
                                ["column"] = m.column,
                            });
                    Notify("compile_failure", new JsonObject { ["errors"] = errors });
                }
                _compileMessages.Clear();
                Notify("compile_finished", new JsonObject { ["error_count"] = _compileErrorCount });
            }
        }

        private static void OnUpdate()
        {
            while (_mainThreadQueue.TryDequeue(out var item))
            {
                try   { item.Result.SetResult(item.Work()); }
                catch (Exception ex) { item.Result.SetException(ex); }
            }

            double now = EditorApplication.timeSinceStartup;
            if (now < _nextPoll) return;
            _nextPoll = now + PollInterval;

            bool isUpdating = EditorApplication.isUpdating;
            if (_wasUpdating && !isUpdating) _refreshNeeded = false;
            _wasUpdating = isUpdating;

            if (_state == State.Idle)
                ReadNext();

            if (_scriptsDirty && !EditorApplication.isCompiling && _state == State.Idle
                && (now - _lastScriptWrite) >= AutoCompileDelay)
            {
                _state        = State.AwaitCompile;
                _scriptsDirty = false;
                _compileMessages.Clear();
                AssetDatabase.Refresh();
                CompilationPipeline.RequestScriptCompilation();
            }

            if (_state == State.AwaitRefresh && !EditorApplication.isUpdating)
            {
                _refreshNeeded = false;
                _state         = State.Idle;
                Notify("refresh_finished", new JsonObject());
            }

            if (_state == State.Busy && _pendingCmd == "refresh")
            {
                if (!EditorApplication.isUpdating)
                {
                    _refreshNeeded = false;
                    Notify("refresh_finished", new JsonObject());
                    Respond(MakeResponse(_pendingUid, "refresh", "ok"));
                }
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
                    Dispatch(req.uid, req.cmd, req.agent_id, line, file);
                else
                    SafeDelete(file);
            }
            catch (IOException) { }
        }

        // ── Dispatch ──────────────────────────────────────────────────────────

        private static void Dispatch(string uid, string cmd, string agentId, string json, string file)
        {
            _state              = State.Busy;
            _pendingUid         = uid;
            _pendingCmd         = cmd;
            _pendingFile        = file;
            _pendingRequestJson = json;
            _pendingAgentId     = agentId;

            if (_scriptsDirty && !_noCompileBlockCmds.Contains(cmd))
            {
                _state       = State.AwaitCompile;
                _scriptsDirty = false;
                _compileMessages.Clear();
                AssetDatabase.Refresh();
                CompilationPipeline.RequestScriptCompilation();
                return; // request file stays on disk; ReadNext replays after compile
            }
            if (_refreshNeeded && !_noRefreshBlockCmds.Contains(cmd))
            {
                _state        = State.AwaitRefresh;
                _refreshNeeded = false;
                Notify("refresh_started", new JsonObject());
                AssetDatabase.Refresh();
                return; // request file stays on disk; ReadNext replays after refresh
            }

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
                    File.WriteAllText(Path.Combine(ResponsesDir, uid + ".json"), resp.ToJsonString());
                    if (_pendingFile != null && File.Exists(_pendingFile)) SafeDelete(_pendingFile);
                    AppendLog(BuildLogEntry(resp));
                }
            }
            catch (IOException) { }
            _testRespondCapture?.Invoke(resp);
            _pendingUid         = null;
            _pendingCmd         = null;
            _pendingFile        = null;
            _pendingRequestJson = null;
            _pendingAgentId     = null;
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
            catch (Exception ex) { Debug.LogWarning($"[AgentBridge] Failed to write log: {ex.Message}"); }
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
                    ["state"]          = _state == State.Reloading ? "reloading" : _state == State.Idle ? "idle" : "busy",
                    ["active_scene"]   = scene.name,
                    // scene_dirty: if true, scene_new/scene_open will trigger a save-dialog unless scene_save is called first
                    ["scene_dirty"]    = scene.isDirty,
                    ["play_mode"]      = EditorApplication.isPlaying,
                    ["compile_errors"]  = _compileErrorCount,
                    ["console_errors"]  = ConsoleBridge.ErrorCount,
                    ["agent_id"]        = _pendingAgentId ?? "",
                    ["written_at"]      = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                }.ToJsonString());
            }
            catch (IOException) { }
            WriteGlobalRegistration();
        }

        private static string ComputeRegistrationPath()
        {
            try
            {
                var projectPath = Path.GetFullPath(Path.GetDirectoryName(Application.dataPath));
                byte[] hash;
                using (var md5 = System.Security.Cryptography.MD5.Create())
                    hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(projectPath.ToLowerInvariant()));
                var hex = BitConverter.ToString(hash).Replace("-", "").ToLower();
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AgentBridge",
                    hex + ".json");
            }
            catch { return null; }
        }

        private static void WriteGlobalRegistration()
        {
            if (_registrationPath == null) return;
            try
            {
                var projectPath = Path.GetFullPath(Path.GetDirectoryName(Application.dataPath));
                Directory.CreateDirectory(Path.GetDirectoryName(_registrationPath));
                File.WriteAllText(_registrationPath, new JsonObject
                {
                    ["path"]       = projectPath,
                    ["pid"]        = System.Diagnostics.Process.GetCurrentProcess().Id,
                    ["written_at"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                }.ToJsonString());
            }
            catch { }
        }

        private static void DeleteGlobalRegistration()
        {
            if (_registrationPath == null) return;
            try { File.Delete(_registrationPath); } catch { }
        }

        public static JsonObject MakeResponse(string uid, string cmd, string status)
        {
            var r = new JsonObject
            {
                ["uid"]            = uid,
                ["cmd"]            = cmd,
                ["status"]         = status,
                ["compile_errors"] = _compileErrorCount,
                ["console_errors"] = ConsoleBridge.ErrorCount,
            };
            if (!string.IsNullOrEmpty(_pendingAgentId))
                r["agent_id"] = _pendingAgentId;
            return r;
        }

#if UNITY_EDITOR_WIN
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
#endif

        internal static void FocusUnityWindow()
        {
#if UNITY_EDITOR_WIN
            var hwnd = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            if (IsIconic(hwnd)) ShowWindow(hwnd, 9); // SW_RESTORE only when minimized
            SetForegroundWindow(hwnd);
#endif
        }

        [Serializable] private class Request { public string uid; public string cmd; public string agent_id; }

        internal struct MainThreadItem
        {
            public Func<object>                 Work;
            public TaskCompletionSource<object> Result;
        }

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
            if (IsScriptAsset(Path.GetExtension(name).ToLowerInvariant()))
            {
                _scriptsDirty    = true;
                _lastScriptWrite = EditorApplication.timeSinceStartup;
            }
        }

        internal static bool IsScriptAsset(string ext) =>
            ext == ".cs" || ext == ".asmdef" || ext == ".asmref";

        // ── Built-in commands ─────────────────────────────────────────────────

        private sealed class StatusCmd : IAgentCommand
        {
            public string Cmd         => "status";
            public string Description => "";
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
            public string Cmd         => "list_commands";
            public string Description => "";
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
                resp["list_commands"] = list;
                return resp;
            }
        }

        private sealed class CompileCmd : IAgentCommand
        {
            public string Cmd         => "compile";
            public string Description => "Trigger script compilation; returns errors and warnings. Fires compile_started then compile_finished or compile_failure notifications.";
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
            public string Description => "Trigger AssetDatabase.Refresh() and wait for completion. Fires refresh_started and refresh_finished notifications.";
            public JsonObject Execute(string uid, string requestJson)
            {
                FocusUnityWindow();
                Notify("refresh_started", new JsonObject());
                AssetDatabase.Refresh();
                return null; // async — OnUpdate responds
            }
        }

        private sealed class FocusCmd : IAgentCommand
        {
            public string Cmd         => "focus";
            public string Description => "Bring the Unity Editor window to the foreground.";
            public JsonObject Execute(string uid, string requestJson)
            {
                FocusUnityWindow();
                return MakeResponse(uid, Cmd, "ok");
            }
        }
    }
}
