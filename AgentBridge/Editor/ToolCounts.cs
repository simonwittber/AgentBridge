using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using UnityEditor;
using UnityEngine;

namespace LLMDevTools
{
    // Counts how often each command is called, per project, to help find unused commands.
    // Every known command is listed, so unused ones show up with a count of 0.
    internal static class ToolCounts
    {
        internal const string CountsPath = "UserSettings/AgentBridge/tool_counts.json";
        private const double SaveDelay  = 2.0;

        private static readonly SortedDictionary<string, long> _counts = new(StringComparer.Ordinal);
        private static double _saveAt = double.MaxValue;

        internal static void Init(IEnumerable<string> knownCmds)
        {
            Load();
            bool added = false;
            foreach (var cmd in knownCmds)
                if (_counts.TryAdd(cmd, 0)) added = true;
            if (added) Save();

            EditorApplication.update               += OnUpdate;
            EditorApplication.quitting             += SaveIfPending;
            AssemblyReloadEvents.beforeAssemblyReload += SaveIfPending;
        }

        internal static void Increment(string cmd)
        {
            if (string.IsNullOrEmpty(cmd)) return;
            _counts.TryGetValue(cmd, out var n);
            _counts[cmd] = n + 1;
            _saveAt = EditorApplication.timeSinceStartup + SaveDelay;
        }

        private static void OnUpdate()
        {
            if (EditorApplication.timeSinceStartup >= _saveAt) SaveIfPending();
        }

        private static void SaveIfPending()
        {
            if (_saveAt == double.MaxValue) return;
            Save();
        }

        private static void Load()
        {
            try
            {
                if (!File.Exists(CountsPath)) return;
                if (JsonNode.Parse(File.ReadAllText(CountsPath)) is not JsonObject obj) return;
                foreach (var kv in obj)
                    if (kv.Value is JsonValue v && v.TryGetValue<long>(out var n))
                        _counts[kv.Key] = n;
            }
            catch (Exception ex) { Debug.LogWarning($"[AgentBridge] Failed to read tool counts: {ex.Message}"); }
        }

        private static void Save()
        {
            _saveAt = double.MaxValue;
            try
            {
                var obj = new JsonObject();
                foreach (var kv in _counts) obj[kv.Key] = kv.Value;
                Directory.CreateDirectory(Path.GetDirectoryName(CountsPath));
                File.WriteAllText(CountsPath, obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) { Debug.LogWarning($"[AgentBridge] Failed to write tool counts: {ex.Message}"); }
        }
    }
}
