using System.Collections.Generic;
using System.Text.Json.Nodes;
using Unity.Profiling;
using UnityEngine;

namespace LLMDevTools
{
    internal static class ProfilerCommands
    {
        static readonly Dictionary<string, ProfilerRecorder> s_recorders = new();

        // Parses "Category/MarkerName" → (category, markerName). Defaults to Scripts.
        static (ProfilerCategory category, string markerName) ParseMarker(string entry)
        {
            var slash = entry.IndexOf('/');
            if (slash < 0) return (ProfilerCategory.Scripts, entry);
            var cat  = entry.Substring(0, slash);
            var name = entry.Substring(slash + 1);
            var category = cat switch
            {
                "Memory"    => ProfilerCategory.Memory,
                "Render"    => ProfilerCategory.Render,
                "Physics"   => ProfilerCategory.Physics,
                "Audio"     => ProfilerCategory.Audio,
                "Animation" => ProfilerCategory.Animation,
                "AI"        => ProfilerCategory.Ai,
                "Input"     => ProfilerCategory.Input,
                "UI"        => ProfilerCategory.Gui,
                _           => ProfilerCategory.Scripts,
            };
            return (category, name);
        }

        static void DisposeAll()
        {
            foreach (var kvp in s_recorders)
                kvp.Value.Dispose();
            s_recorders.Clear();
        }

        // ── commands ──────────────────────────────────────────────────────────

        private sealed class ProfilerStartCmd : IAgentCommand
        {
            public string    Cmd         => "profiler_start";
            public string    Description => "Begin recording named profiler markers. Works in edit mode and play mode. Prefix with a category using 'Category/Name' (e.g. 'Render/Camera.Render'). Use execute_script to fire custom markers, then call profiler_get_samples.";
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

                DisposeAll();
                foreach (var node in markersNode)
                {
                    var entry = node?.GetValue<string>() ?? "";
                    if (string.IsNullOrEmpty(entry)) continue;
                    var (category, markerName) = ParseMarker(entry);
                    s_recorders[entry] = ProfilerRecorder.StartNew(category, markerName, 300);
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
            public ArgSpec[] Args        => System.Array.Empty<ArgSpec>();

            public JsonObject Execute(string uid, string requestJson)
            {
                foreach (var key in new List<string>(s_recorders.Keys))
                {
                    var r = s_recorders[key];
                    r.Stop();
                    s_recorders[key] = r; // ProfilerRecorder is a struct; Stop() mutates the local copy, so write it back.
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
                return AgentBridge.MakeResponse(uid, Cmd, "ok");
            }
        }

        private sealed class ProfilerGetSamplesCmd : IAgentCommand
        {
            public string    Cmd         => "profiler_get_samples";
            public string    Description => "Return summary stats (and optionally raw values) for recorded markers. Call profiler_start first, then fire markers via execute_script or by running the game.";
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
                    bool isBytes = recorder.Valid && recorder.UnitType == ProfilerMarkerDataUnit.Bytes;

                    double last = 0, sum = 0, min = double.MaxValue, max = 0;
                    JsonArray samplesArr = null;
                    if (raw) samplesArr = new JsonArray();

                    for (int i = 0; i < count; i++)
                    {
                        double v = isBytes
                            ? recorder.GetSample(i).Value
                            : recorder.GetSample(i).Value / 1_000_000.0;
                        if (i == count - 1) last = v;
                        sum += v;
                        if (v < min) min = v;
                        if (v > max) max = v;
                        samplesArr?.Add(System.Math.Round(v, isBytes ? 0 : 3));
                    }

                    double avg = count > 0 ? sum / count : 0;
                    if (count == 0) min = 0;

                    int    dp    = isBytes ? 0 : 3;
                    double rLast = System.Math.Round(last, dp);
                    double rAvg  = System.Math.Round(avg,  dp);
                    double rMin  = System.Math.Round(min,  dp);
                    double rMax  = System.Math.Round(max,  dp);
                    var entry = new JsonObject
                    {
                        ["name"]        = kvp.Key,
                        ["unit"]        = isBytes ? "bytes" : "ms",
                        ["valid"]       = recorder.Valid,
                        ["sampleCount"] = count,
                        ["last"]        = rLast,
                        ["avg"]         = rAvg,
                        ["min"]         = rMin,
                        ["max"]         = rMax,
                    };
                    if (!isBytes)
                    {
                        entry["lastMs"] = rLast;
                        entry["avgMs"]  = rAvg;
                        entry["minMs"]  = rMin;
                        entry["maxMs"]  = rMax;
                    }
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
