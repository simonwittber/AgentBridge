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

        private static readonly string[] _messages   = new string[BufferSize];
        private static readonly string[] _types      = new string[BufferSize];
        private static readonly long[]   _timestamps = new long[BufferSize];
        private static int _head;
        private static int _count;

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
                _messages[_head]   = msg;
                _types[_head]      = ModeToType(mode);
                _timestamps[_head] = now;
                _head = (_head + 1) % BufferSize;
                if (_count < BufferSize) _count++;
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

        private static void OnLog(string message, string _, LogType type)
        {
            _messages[_head]   = message;
            _types[_head]      = type.ToString().ToLower();
            _timestamps[_head] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _head = (_head + 1) % BufferSize;
            if (_count < BufferSize) _count++;
        }

        private sealed class ConsoleLogsCmd : IAgentCommand
        {
            public string    Cmd         => "console_logs";
            public string    Description => "Return recent Unity console messages (newest first) from the in-editor ring buffer.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("limit", "int",    "50", "Maximum messages to return (max 200)"),
                new ArgSpec("type",  "string", "",   "Filter: error, warning, log, exception, assert — omit for all"),
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
                    logs.Add(new JsonObject
                    {
                        ["type"]    = _types[idx],
                        ["message"] = _messages[idx],
                        ["time_ms"] = _timestamps[idx],
                    });
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
