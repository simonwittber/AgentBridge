using System.Collections.Generic;
using System.Text.Json.Nodes;
using Unity.Profiling;
using UnityEditor;

namespace LLMDevTools
{
    [InitializeOnLoad]
    internal static class ProfilerCommands
    {
        const string SessionKey = "AgentBridge.ProfilerMarkers";

        static readonly Dictionary<string, ProfilerRecorder> s_recorders = new();

        static ProfilerCommands()
        {
            AgentBridge.Register(new ProfilerStartCmd());
            AgentBridge.Register(new ProfilerStopCmd());
            AgentBridge.Register(new ProfilerClearCmd());
            AgentBridge.Register(new ProfilerGetSamplesCmd());
            AgentBridge.Register(new ProfilerBenchmarkCmd());

            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            var json = SessionState.GetString(SessionKey, "");
            if (string.IsNullOrEmpty(json)) return;
            var names = JsonNode.Parse(json)?.AsArray();
            if (names == null) return;
            DisposeAll();
            foreach (var node in names)
            {
                var name = node?.GetValue<string>() ?? "";
                if (!string.IsNullOrEmpty(name))
                    s_recorders[name] = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, name, 300);
            }
        }

        static void StartRecorders(string[] names)
        {
            DisposeAll();
            var arr = new JsonArray();
            foreach (var name in names)
            {
                if (string.IsNullOrEmpty(name)) continue;
                s_recorders[name] = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, name, 300);
                arr.Add(name);
            }
            SessionState.SetString(SessionKey, arr.ToJsonString());
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
            public string    Description => "Begin recording named profiler markers (ProfilerCategory.Scripts). If domain reload on play mode entry is enabled (the default), recorders are recreated automatically when play mode enters using the same marker names.";
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
                    err["message"] = "markers is required: pass a JSON array of marker name strings.";
                    return err;
                }

                var names = new string[markersNode.Count];
                for (int i = 0; i < markersNode.Count; i++)
                    names[i] = markersNode[i]?.GetValue<string>() ?? "";

                StartRecorders(names);

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
            public ArgSpec[] Args        => System.Array.Empty<ArgSpec>();

            public JsonObject Execute(string uid, string requestJson)
            {
                DisposeAll();
                SessionState.EraseString(SessionKey);
                return AgentBridge.MakeResponse(uid, Cmd, "ok");
            }
        }

        private sealed class ProfilerGetSamplesCmd : IAgentCommand
        {
            public string    Cmd         => "profiler_get_samples";
            public string    Description => "Return summary stats (and optionally raw values) for recorded markers.";
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
        private sealed class ProfilerBenchmarkCmd : IAgentCommand
        {
            public string    Cmd         => "profiler_benchmark";
            public string    Description => "Fire a named ProfilerMarker with real CPU work to generate measurable timing samples. Call profiler_start with the same marker name first.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("marker",     "string", "AgentBridge.Benchmark", "Marker name to fire"),
                new ArgSpec("iterations", "int",    "5",                     "Number of Begin/End cycles"),
                new ArgSpec("work",       "int",    "500000",                "Inner loop iterations per cycle"),
            };

            public JsonObject Execute(string uid, string requestJson)
            {
                var req        = JsonNode.Parse(requestJson);
                var markerName = req?["marker"]?.GetValue<string>() ?? "AgentBridge.Benchmark";
                int iterations = req?["iterations"]?.GetValue<int>() ?? 5;
                int work       = req?["work"]?.GetValue<int>() ?? 500000;

                var m = new ProfilerMarker(ProfilerCategory.Scripts, markerName);
                for (int i = 0; i < iterations; i++)
                {
                    m.Begin();
                    double x = 0;
                    for (int j = 0; j < work; j++) x += j;
                    UnityEngine.Debug.Log(x); // prevent JIT dead-code elimination
                    m.End();
                }

                var resp = AgentBridge.MakeResponse(uid, Cmd, "ok");
                resp["fired"] = iterations;
                return resp;
            }
        }
    }
}

