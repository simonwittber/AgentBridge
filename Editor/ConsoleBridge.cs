using System;
using System.Reflection;
using System.Text.Json.Nodes;
using UnityEditor;
using UnityEngine;

namespace LLMDevTools
{
    [InitializeOnLoad]
    public static class ConsoleBridge
    {
        private const int BufferSize = 200;

        private static readonly string[] _messages    = new string[BufferSize];
        private static readonly string[] _stackTraces = new string[BufferSize];
        private static readonly string[] _types       = new string[BufferSize];
        private static readonly long[]   _timestamps  = new long[BufferSize];
        private static int _head;
        private static int _count;
        private static int _errorCount;

        // Count of error/exception log messages since Unity started (or last domain reload).
        // Exposed so AgentBridge can inject it into every response alongside compile_errors.
        internal static int ErrorCount => _errorCount;

        static ConsoleBridge()
        {
            LoadExistingEntries();
            Application.logMessageReceived += OnLog;
            AgentBridge.Register(new ConsoleLogsCmd());
        }

        private static void LoadExistingEntries()
        {
            var logEntriesType = Type.GetType("UnityEditor.LogEntries,UnityEditor");
            var logEntryType   = Type.GetType("UnityEditor.LogEntry,UnityEditor");
            if (logEntriesType == null || logEntryType == null) return;

            var startMethod = logEntriesType.GetMethod("StartGettingEntries",   BindingFlags.Static | BindingFlags.Public);
            var endMethod   = logEntriesType.GetMethod("EndGettingEntries",     BindingFlags.Static | BindingFlags.Public);
            var getMethod   = logEntriesType.GetMethod("GetEntryInternal",      BindingFlags.Static | BindingFlags.Public);
            var msgField    = logEntryType.GetField("message",                  BindingFlags.Instance | BindingFlags.Public);
            var modeField   = logEntryType.GetField("mode",                     BindingFlags.Instance | BindingFlags.Public);
            if (startMethod == null || endMethod == null || getMethod == null) return;

            int total = (int)startMethod.Invoke(null, null);
            var entry = Activator.CreateInstance(logEntryType);
            long now  = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            for (int i = 0; i < total; i++)
            {
                getMethod.Invoke(null, new object[] { i, entry });
                string msg  = msgField?.GetValue(entry) as string ?? "";
                int    mode = modeField != null ? (int)modeField.GetValue(entry) : 0;
                string t           = ModeToType(mode);
                _messages[_head]    = msg;
                _stackTraces[_head] = "";
                _types[_head]       = t;
                _timestamps[_head]  = now;
                _head = (_head + 1) % BufferSize;
                if (_count < BufferSize) _count++;
                if (t == "error" || t == "exception") _errorCount++;
            }

            endMethod.Invoke(null, null);
        }

        private static string ModeToType(int mode)
        {
            if ((mode & (1 | 16 | 256 | 2048 | 8192)) != 0) return "error";
            if ((mode & (128 | 512 | 4096))            != 0) return "warning";
            if ((mode & 131072)                        != 0) return "exception";
            if ((mode & (2 | 2097152))                 != 0) return "assert";
            return "log";
        }

        private static void OnLog(string message, string stackTrace, LogType type)
        {
            _messages[_head]    = message;
            _stackTraces[_head] = stackTrace;
            _types[_head]       = (type == LogType.Exception) ? "error" : type.ToString().ToLower();
            _timestamps[_head]  = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _head = (_head + 1) % BufferSize;
            if (_count < BufferSize) _count++;
            if (type == LogType.Error || type == LogType.Exception)
                _errorCount++;
        }

        private sealed class ConsoleLogsCmd : IAgentCommand
        {
            public string    Cmd         => "console_logs";
            public string    Description => "Return recent Unity console messages (newest first) from the in-editor ring buffer.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("limit", "int",    "50", "Maximum messages to return (max 200)"),
                new ArgSpec("type",  "string", "",   "Filter: error, warning, log, assert — omit for all. Exceptions are stored as error."),
            };

            public JsonObject Execute(string uid, string requestJson)
            {
                var p     = JsonUtility.FromJson<Params>(requestJson);
                int limit = Math.Max(1, Math.Min(BufferSize, p.limit));
                var logs  = new JsonArray();
                int added = 0;

                for (int i = 1; i <= _count && added < limit; i++)
                {
                    int idx = ((_head - i) % BufferSize + BufferSize) % BufferSize;
                    if (!string.IsNullOrEmpty(p.type) && _types[idx] != p.type) continue;
                    var entry = new JsonObject
                    {
                        ["type"]    = _types[idx],
                        ["message"] = _messages[idx],
                        ["time_ms"] = _timestamps[idx],
                    };
                    if (!string.IsNullOrEmpty(_stackTraces[idx]))
                        entry["stack_trace"] = _stackTraces[idx];
                    logs.Add(entry);
                    added++;
                }

                var resp = AgentBridge.MakeResponse(uid, Cmd, "ok");
                resp["logs"] = logs;
                return resp;
            }

            [Serializable] private class Params { public int limit = 50; public string type = ""; }
        }
    }
}
