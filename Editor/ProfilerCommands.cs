using System.Collections.Generic;
using System.Text.Json.Nodes;
using Unity.Profiling;
using UnityEditor;

namespace LLMDevTools
{
    [InitializeOnLoad]
    internal static class ProfilerCommands
    {
        static readonly Dictionary<string, ProfilerRecorder> s_recorders = new();

        static ProfilerCommands()
        {
            AgentBridge.Register(new ProfilerStartCmd());
            AgentBridge.Register(new ProfilerStopCmd());
            AgentBridge.Register(new ProfilerClearCmd());
            AgentBridge.Register(new ProfilerGetSamplesCmd());
        }

        static void DisposeAll()
        {
            foreach (var kvp in s_recorders)
            {
                var r = kvp.Value;
                r.Dispose();
            }
            s_recorders.Clear();
        }

        // ── commands ──────────────────────────────────────────────────────────

        private sealed class ProfilerStartCmd : IAgentCommand
        {
            public string    Cmd         => "profiler_start";
            public string    Description => "Begin recording named Profiler.BeginSample markers (ProfilerCategory.Scripts).";
            public bool      Core        => true;
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("markers", "any", "", "JSON array of marker name strings to record"),
            };

            public JsonObject Execute(string uid, string requestJson)
            {
                var req         = JsonNode.Parse(requestJson);
                var markersNode = req?["markers"]?.AsArray();

                if (markersNode == null || markersNode.Count == 0)
                {
                    var err = AgentBridge.MakeResponse(uid, Cmd, "error");
                    err["message"] = "markers is required: pass a JSON array of Profiler.BeginSample marker names.";
                    return err;
                }

                var names = new string[markersNode.Count];
                for (int i = 0; i < markersNode.Count; i++)
                    names[i] = markersNode[i]?.GetValue<string>() ?? "";

                DisposeAll();
                foreach (var name in names)
                {
                    if (string.IsNullOrEmpty(name)) continue;
                    s_recorders[name] = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, name, 300);
                }

                var markerArr = new JsonArray();
                foreach (var name in s_recorders.Keys)
                    markerArr.Add(name);

                var resp = AgentBridge.MakeResponse(uid, Cmd, "ok");
                resp["markers"] = markerArr;
                return resp;
            }
        }

        private sealed class ProfilerStopCmd : IAgentCommand
        {
            public string    Cmd         => "profiler_stop";
            public string    Description => "Stop the current profiler recording session.";
            public bool      Core        => true;
            public ArgSpec[] Args        => System.Array.Empty<ArgSpec>();

            public JsonObject Execute(string uid, string requestJson)
            {
                foreach (var key in new List<string>(s_recorders.Keys))
                {
                    var r = s_recorders[key];
                    r.Stop();
                    s_recorders[key] = r;
                }
                return AgentBridge.MakeResponse(uid, Cmd, "ok");
            }
        }

        private sealed class ProfilerClearCmd : IAgentCommand
        {
            public string    Cmd         => "profiler_clear";
            public string    Description => "Stop and dispose all profiler recorders.";
            public bool      Core        => true;
            public ArgSpec[] Args        => System.Array.Empty<ArgSpec>();

            public JsonObject Execute(string uid, string requestJson)
            {
                DisposeAll();
                return AgentBridge.MakeResponse(uid, Cmd, "ok");
            }
        }

        private sealed class ProfilerGetSamplesCmd : IAgentCommand
        {
            public string    Cmd         => "profiler_get_samples";
            public string    Description => "Return summary stats (and optionally raw values) for recorded markers.";
            public bool      Core        => true;
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("marker", "string", "", "Filter to a single named marker; omit for all"),
                new ArgSpec("raw",    "bool",   "false", "If true, include per-sample ms values array"),
            };

            public JsonObject Execute(string uid, string requestJson)
            {
                var req    = JsonNode.Parse(requestJson);
                var filter = req?["marker"]?.GetValue<string>() ?? "";
                var raw    = req?["raw"]?.GetValue<bool>() ?? false;

                if (filter != "" && !s_recorders.ContainsKey(filter))
                {
                    var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                    e["message"] = $"Marker '{filter}' not found. Call profiler_start first.";
                    return e;
                }

                var result = new JsonArray();
                foreach (var kvp in s_recorders)
                {
                    if (filter != "" && kvp.Key != filter) continue;

                    var recorder = kvp.Value;
                    int count    = recorder.Count;

                    double lastMs = 0, sumMs = 0, minMs = double.MaxValue, maxMs = 0;
                    var samplesArr = raw ? new JsonArray() : null;

                    for (int i = 0; i < count; i++)
                    {
                        double ms = recorder.GetSample(i).Value / 1_000_000.0;
                        if (i == count - 1) lastMs = ms;
                        sumMs += ms;
                        if (ms < minMs) minMs = ms;
                        if (ms > maxMs) maxMs = ms;
                        samplesArr?.Add(System.Math.Round(ms, 3));
                    }

                    double avgMs = count > 0 ? sumMs / count : 0;
                    if (count == 0) minMs = 0;

                    var entry = new JsonObject
                    {
                        ["name"]        = kvp.Key,
                        ["valid"]       = recorder.Valid,
                        ["sampleCount"] = count,
                        ["lastMs"]      = System.Math.Round(lastMs, 3),
                        ["avgMs"]       = System.Math.Round(avgMs,  3),
                        ["minMs"]       = System.Math.Round(minMs,  3),
                        ["maxMs"]       = System.Math.Round(maxMs,  3),
                    };
                    if (samplesArr != null)
                        entry["samples"] = samplesArr;

                    result.Add(entry);
                }

                var resp = AgentBridge.MakeResponse(uid, Cmd, "ok");
                resp["markers"] = result;
                return resp;
            }
        }
    }
}
