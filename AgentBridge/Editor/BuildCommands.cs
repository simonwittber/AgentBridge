using System;
using System.Linq;
using System.Text.Json.Nodes;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace LLMDevTools
{
    [InitializeOnLoad]
    public static class BuildCommands
    {
        static BuildCommands()
        {
            AgentBridge.Register(new BuildCmd());
        }

        private sealed class BuildCmd : IAgentCommand
        {
            public string    Cmd         => "build";
            public string    Description => "Build the Unity player for the specified target.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("target",      "string", "StandaloneWindows64", ""),
                new ArgSpec("output",      "string", "",                    ""),
                new ArgSpec("scenes",      "string", "",                    "Comma-separated; omit for build settings"),
                new ArgSpec("development", "bool",   "false",               ""),
            };

            public JsonObject Execute(string uid, string requestJson)
            {
                var p = JsonUtility.FromJson<Params>(requestJson);
                if (string.IsNullOrEmpty(p.output))
                {
                    var err = AgentBridge.MakeResponse(uid, Cmd, "error");
                    err["message"] = "output is required";
                    return err;
                }

                string[] sceneList = string.IsNullOrEmpty(p.scenes)
                    ? EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray()
                    : p.scenes.Split(',');

                var opts = new BuildPlayerOptions
                {
                    scenes           = sceneList,
                    locationPathName = p.output,
                    options          = p.development ? BuildOptions.Development : BuildOptions.None,
                };

                try
                {
                    opts.target = (BuildTarget)Enum.Parse(typeof(BuildTarget), p.target, true);
                }
                catch
                {
                    var err = AgentBridge.MakeResponse(uid, Cmd, "error");
                    err["message"] = "Unknown build target: " + p.target;
                    return err;
                }

                var report  = BuildPipeline.BuildPlayer(opts);
                var summary = report.summary;
                string status = summary.result == BuildResult.Succeeded ? "ok" : "error";
                var resp = AgentBridge.MakeResponse(uid, Cmd, status);
                resp["result"]          = summary.result.ToString();
                resp["total_errors"]    = summary.totalErrors;
                resp["total_warnings"]  = summary.totalWarnings;
                resp["duration_s"]      = summary.totalTime.TotalSeconds;
                resp["output_path"]     = summary.outputPath ?? "";
                return resp;
            }

            [Serializable] private class Params
            {
                public string target      = "StandaloneWindows64";
                public string output      = "";
                public string scenes      = "";
                public bool   development = false;
            }
        }
    }
}
