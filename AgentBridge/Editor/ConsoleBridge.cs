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
                if (t == "error") _errorCount++;
            }

            endMethod.Invoke(null, null);
        }

        // Bitmask values from Unity's internal ConsoleWindow.Mode enum (accessed via reflection).
        private const int ModeError          = 1;
        private const int ModeFatal          = 16;
        private const int ModeScriptingError = 256;
        private const int ModeCompileError   = 2048;
        private const int ModeStickyError    = 8192;
        private const int ModeImportWarning  = 128;
        private const int ModeScriptWarning  = 512;
        private const int ModeCompileWarning = 4096;
        private const int ModeDontShowAgain  = 131072; // Unity sets this on dismissed errors; treated as error here.
        private const int ModeAssert         = 2;
        private const int ModeAssertMessage  = 2097152;

        private static string ModeToType(int mode)
        {
            if ((mode & (ModeError | ModeFatal | ModeScriptingError | ModeCompileError | ModeStickyError)) != 0) return "error";
            if ((mode & (ModeImportWarning | ModeScriptWarning | ModeCompileWarning))                      != 0) return "warning";
            if ((mode & ModeDontShowAgain)                                                                  != 0) return "error";
            if ((mode & (ModeAssert | ModeAssertMessage))                                                   != 0) return "assert";
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
            public string    Description => "Return recent Unity console messages, newest first.";
            public ArgSpec[] Args        => System.Array.Empty<ArgSpec>();

            public JsonObject Execute(string uid, string requestJson)
            {
                var logs = new JsonArray();
                for (int i = 1; i <= _count; i++)
                {
                    int idx = ((_head - i) % BufferSize + BufferSize) % BufferSize;
                    var entry = new JsonObject
                    {
                        ["type"]    = _types[idx],
                        ["message"] = _messages[idx],
                        ["time_ms"] = _timestamps[idx],
                    };
                    if (!string.IsNullOrEmpty(_stackTraces[idx]))
                        entry["stack_trace"] = _stackTraces[idx];
                    logs.Add(entry);
                }
                var resp = AgentBridge.MakeResponse(uid, Cmd, "ok");
                resp["logs"] = logs;
                return resp;
            }
        }
    }
}
