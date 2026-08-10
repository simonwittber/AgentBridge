using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json.Nodes;
using UnityEditor;
using UnityEngine;

namespace LLMDevTools
{
    [InitializeOnLoad]
    public static class ConsoleBridge
    {
        private const int DefaultLimit = 200;

        private static int _errorCount;

        internal static int ErrorCount => _errorCount;

        static ConsoleBridge()
        {
            Application.logMessageReceived += (_, _, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception)
                    _errorCount++;
            };
        }

        private static List<LogEntry> GetConsoleEntries(string filter)
        {
            var results = new List<LogEntry>();

            try
            {
                var logEntriesType = Type.GetType("UnityEditor.LogEntries, UnityEditor");
                var logEntryType = Type.GetType("UnityEditor.LogEntry, UnityEditor");

                if (logEntriesType == null || logEntryType == null)
                    return results;

                var startMethod = logEntriesType.GetMethod("StartGettingEntries", BindingFlags.Static | BindingFlags.Public);
                var endMethod = logEntriesType.GetMethod("EndGettingEntries", BindingFlags.Static | BindingFlags.Public);
                var getCountMethod = logEntriesType.GetMethod("GetCount", BindingFlags.Static | BindingFlags.Public);
                var getEntryMethod = logEntriesType.GetMethod("GetEntryInternal", BindingFlags.Static | BindingFlags.Public);

                if (startMethod == null || endMethod == null || getCountMethod == null || getEntryMethod == null)
                    return results;

                startMethod.Invoke(null, null);

                try
                {
                    int count = (int)getCountMethod.Invoke(null, null);
                    var entry = Activator.CreateInstance(logEntryType);

                    for (int i = 0; i < count; i++)
                    {
                        getEntryMethod.Invoke(null, new[] { i, entry });

                        var message = (string)(logEntryType.GetField("message", BindingFlags.Public | BindingFlags.Instance)?.GetValue(entry) ?? "");
                        var stackTrace = (string)(logEntryType.GetField("stackTrace", BindingFlags.Public | BindingFlags.Instance)?.GetValue(entry) ?? "");
                        var mode = (int)(logEntryType.GetField("mode", BindingFlags.Public | BindingFlags.Instance)?.GetValue(entry) ?? 0);

                        if (!string.IsNullOrEmpty(filter) &&
                            message.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                            continue;

                        string type = DetectType(mode, message, stackTrace);
                        results.Add(new LogEntry(type, message, stackTrace));
                    }
                }
                finally
                {
                    endMethod.Invoke(null, null);
                }
            }
            catch
            {
                return results;
            }

            return results;
        }

        private static string DetectType(int mode, string message, string stackTrace)
        {
            if ((mode & 1) != 0) return "error";
            if ((mode & 2) != 0) return "warning";
            if ((mode & 4) != 0) return "error";
            return "log";
        }

        private readonly struct LogEntry
        {
            public readonly string Type, Message, Stack;
            public LogEntry(string type, string message, string stack)
            { Type = type; Message = message; Stack = stack; }
        }

        private sealed class ConsoleLogsCmd : IAgentCommand
        {
            public string    Cmd         => "console_logs";
            public string    Description => "Return the current Unity console window messages, newest first.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("filter", "string", "", "Only return entries whose message contains this string (case-insensitive). Omit for all current entries."),
            };

            public JsonObject Execute(string uid, string requestJson)
            {
                var p      = JsonUtility.FromJson<Params>(requestJson);
                var filter = p?.filter ?? "";

                var entries = GetConsoleEntries(filter);

                int start = string.IsNullOrEmpty(filter)
                    ? Math.Max(0, entries.Count - DefaultLimit)
                    : 0;

                var logs = new JsonArray();
                for (int i = entries.Count - 1; i >= start; i--)
                {
                    var e   = entries[i];
                    var obj = new JsonObject { ["type"] = e.Type, ["message"] = e.Message };
                    if (!string.IsNullOrEmpty(e.Stack))
                        obj["stack_trace"] = e.Stack;
                    logs.Add(obj);
                }

                var resp = AgentBridge.MakeResponse(uid, Cmd, "ok");
                resp["logs"] = logs;
                return resp;
            }

            [Serializable]
            private class Params { public string filter = ""; }
        }
    }
}
