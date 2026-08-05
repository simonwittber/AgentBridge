using System;
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
            Application.logMessageReceived += OnLog;
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
