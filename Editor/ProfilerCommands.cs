using System.Collections.Generic;
using System.Text.Json.Nodes;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;

namespace LLMDevTools
{
    [InitializeOnLoad]
    internal static class ProfilerCommands
    {
        static ProfilerCommands()
        {
            AgentBridge.Register(new ProfilerStartCmd());
            AgentBridge.Register(new ProfilerStopCmd());
            AgentBridge.Register(new ProfilerClearCmd());
            AgentBridge.Register(new ProfilerSetDeepCmd());
            AgentBridge.Register(new ProfilerGetFrameCmd());
            AgentBridge.Register(new ProfilerGetSamplesCmd());
        }

        static int ResolveFrame(JsonNode req)
        {
            var frameNode = req?["frame"];
            if (frameNode != null)
                return frameNode.GetValue<int>();
            return ProfilerDriver.lastFrameIndex;
        }

        static bool HasFrameData(int frame) =>
            frame >= 0
            && frame >= ProfilerDriver.firstFrameIndex
            && frame <= ProfilerDriver.lastFrameIndex;

        // Build a recursive sample node from HierarchyFrameDataView.
        static JsonObject BuildNode(HierarchyFrameDataView view, int id, string prefix)
        {
            var name    = view.GetItemName(id);
            var total   = view.GetItemColumnDataAsDouble(id, HierarchyFrameDataView.columnTotalTime);
            var self    = view.GetItemColumnDataAsDouble(id, HierarchyFrameDataView.columnSelfTime);
            var calls   = (int)view.GetItemColumnDataAsDouble(id, HierarchyFrameDataView.columnCalls);

            var node = new JsonObject
            {
                ["name"]    = name,
                ["totalMs"] = System.Math.Round(total, 3),
                ["selfMs"]  = System.Math.Round(self,  3),
                ["calls"]   = calls,
            };

            if (!view.HasItemChildren(id))
                return node;

            var childIds = new List<int>();
            view.GetItemChildren(id, childIds);
            var arr = new JsonArray();
            foreach (var cid in childIds)
            {
                var child = BuildNode(view, cid, prefix);
                if (prefix.Length == 0 || child["name"]!.GetValue<string>().StartsWith(prefix))
                    arr.Add(child);
                else if (child["children"] != null)
                    arr.Add(child);
            }
            if (arr.Count > 0)
                node["children"] = arr;

            return node;
        }

        // Flatten the hierarchy into a list, optionally filtered by prefix.
        static void CollectSamples(HierarchyFrameDataView view, int id, string prefix, JsonArray into)
        {
            var name  = view.GetItemName(id);
            var total = view.GetItemColumnDataAsDouble(id, HierarchyFrameDataView.columnTotalTime);
            var self  = view.GetItemColumnDataAsDouble(id, HierarchyFrameDataView.columnSelfTime);
            var calls = (int)view.GetItemColumnDataAsDouble(id, HierarchyFrameDataView.columnCalls);

            if (prefix.Length == 0 || name.StartsWith(prefix))
            {
                into.Add(new JsonObject
                {
                    ["name"]    = name,
                    ["totalMs"] = System.Math.Round(total, 3),
                    ["selfMs"]  = System.Math.Round(self,  3),
                    ["calls"]   = calls,
                });
            }

            if (!view.HasItemChildren(id))
                return;

            var childIds = new List<int>();
            view.GetItemChildren(id, childIds);
            foreach (var cid in childIds)
                CollectSamples(view, cid, prefix, into);
        }

        // ── commands ──────────────────────────────────────────────────────────

        private sealed class ProfilerStartCmd : IAgentCommand
        {
            public string    Cmd         => "profiler_start";
            public string    Description => "Begin a profiler capture session.";
            public ArgSpec[] Args        => System.Array.Empty<ArgSpec>();

            public JsonObject Execute(string uid, string requestJson)
            {
                ProfilerDriver.enabled = true;
                return AgentBridge.MakeResponse(uid, Cmd, "ok");
            }
        }

        private sealed class ProfilerStopCmd : IAgentCommand
        {
            public string    Cmd         => "profiler_stop";
            public string    Description => "Stop the current profiler capture session.";
            public ArgSpec[] Args        => System.Array.Empty<ArgSpec>();

            public JsonObject Execute(string uid, string requestJson)
            {
                ProfilerDriver.enabled = false;
                return AgentBridge.MakeResponse(uid, Cmd, "ok");
            }
        }

        private sealed class ProfilerClearCmd : IAgentCommand
        {
            public string    Cmd         => "profiler_clear";
            public string    Description => "Clear all captured profiler frames.";
            public ArgSpec[] Args        => System.Array.Empty<ArgSpec>();

            public JsonObject Execute(string uid, string requestJson)
            {
                ProfilerDriver.ClearAllFrames();
                return AgentBridge.MakeResponse(uid, Cmd, "ok");
            }
        }

        private sealed class ProfilerSetDeepCmd : IAgentCommand
        {
            public string    Cmd         => "profiler_set_deep";
            public string    Description => "Enable or disable deep profiling. Takes effect on the next domain reload.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("enabled", "bool", "true", "True to enable deep profiling, false to disable"),
            };

            public JsonObject Execute(string uid, string requestJson)
            {
                var req     = System.Text.Json.Nodes.JsonNode.Parse(requestJson);
                var enabled = req?["enabled"]?.GetValue<bool>() ?? true;
                ProfilerDriver.deepProfiling = enabled;
                var resp = AgentBridge.MakeResponse(uid, Cmd, "ok");
                resp["deepProfiling"] = enabled;
                return resp;
            }
        }

        private sealed class ProfilerGetFrameCmd : IAgentCommand
        {
            public string    Cmd         => "profiler_get_frame";
            public string    Description => "Return the sample hierarchy for a captured frame as a tree.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("frame",  "int",    "-1", "Frame index to query; -1 or omit for the last captured frame"),
                new ArgSpec("prefix", "string", "",   "Only include samples whose name starts with this prefix"),
            };

            public JsonObject Execute(string uid, string requestJson)
            {
                var req    = System.Text.Json.Nodes.JsonNode.Parse(requestJson);
                var frame  = ResolveFrame(req);
                var prefix = req?["prefix"]?.GetValue<string>() ?? "";

                if (!HasFrameData(frame))
                {
                    var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                    e["message"] = frame < 0
                        ? "No profiler data captured. Run profiler_start, enter play mode, then call profiler_stop."
                        : $"Frame {frame} is out of range [{ProfilerDriver.firstFrameIndex}, {ProfilerDriver.lastFrameIndex}].";
                    return e;
                }

                using var view = ProfilerDriver.GetHierarchyFrameDataView(
                    frame, 0,
                    HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName,
                    HierarchyFrameDataView.columnTotalTime, false);

                if (!view.valid)
                {
                    var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                    e["message"] = $"Frame {frame} data is not available.";
                    return e;
                }

                var root = BuildNode(view, view.GetRootItemID(), prefix);
                var resp = AgentBridge.MakeResponse(uid, Cmd, "ok");
                resp["frame"] = frame;
                resp["root"]  = root;
                return resp;
            }
        }

        private sealed class ProfilerGetSamplesCmd : IAgentCommand
        {
            public string    Cmd         => "profiler_get_samples";
            public string    Description => "Return a flat list of samples from a captured frame, sorted by totalMs descending.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("frame",  "int",    "-1", "Frame index to query; -1 or omit for the last captured frame"),
                new ArgSpec("prefix", "string", "",   "Only include samples whose name starts with this prefix"),
            };

            public JsonObject Execute(string uid, string requestJson)
            {
                var req    = System.Text.Json.Nodes.JsonNode.Parse(requestJson);
                var frame  = ResolveFrame(req);
                var prefix = req?["prefix"]?.GetValue<string>() ?? "";

                if (!HasFrameData(frame))
                {
                    var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                    e["message"] = frame < 0
                        ? "No profiler data captured. Run profiler_start, enter play mode, then call profiler_stop."
                        : $"Frame {frame} is out of range [{ProfilerDriver.firstFrameIndex}, {ProfilerDriver.lastFrameIndex}].";
                    return e;
                }

                using var view = ProfilerDriver.GetHierarchyFrameDataView(
                    frame, 0,
                    HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName,
                    HierarchyFrameDataView.columnTotalTime, false);

                if (!view.valid)
                {
                    var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                    e["message"] = $"Frame {frame} data is not available.";
                    return e;
                }

                var samples = new JsonArray();
                CollectSamples(view, view.GetRootItemID(), prefix, samples);

                var resp = AgentBridge.MakeResponse(uid, Cmd, "ok");
                resp["frame"]   = frame;
                resp["samples"] = samples;
                resp["count"]   = samples.Count;
                return resp;
            }
        }
    }
}
