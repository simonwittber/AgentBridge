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

        private static readonly string[] _messages   = new string[BufferSize];
        private static readonly string[] _types      = new string[BufferSize];
        private static readonly long[]   _timestamps = new long[BufferSize];
        private static int _head;
        private static int _count;

        static ConsoleBridge()
        {
            Application.logMessageReceived += OnLog;
            AgentBridge.Register(new ConsoleLogsCmd());
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
