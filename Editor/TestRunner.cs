using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace LLMDevTools
{
    [InitializeOnLoad]
    public static class TestRunner
    {
        static TestRunner() => AgentBridge.Register(new RunTestsCommand());

        // ── Command ───────────────────────────────────────────────────────────

        private sealed class RunTestsCommand : IAgentCommand
        {
            public string    Cmd         => "run_tests";
            public string    Description => "Run Unity edit-mode or play-mode tests.";
            public bool      Core        => true;
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("mode",     "string", "EditMode", "EditMode or PlayMode"),
                new ArgSpec("filter",   "string", "",         "Exact full test name"),
                new ArgSpec("group",    "string", "",         "Suite or namespace prefix"),
                new ArgSpec("assembly", "string", "",         ""),
            };

            public JsonObject Execute(string uid, string requestJson)
            {
                var p = JsonUtility.FromJson<Params>(requestJson);

                for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                {
                    var s = EditorSceneManager.GetSceneAt(i);
                    if (s.isDirty && !string.IsNullOrEmpty(s.path))
                        EditorSceneManager.SaveScene(s);
                }

                var api      = ScriptableObject.CreateInstance<TestRunnerApi>();
                var listener = new Listener(uid, api);
                api.RegisterCallbacks(listener);

                var filter = new Filter
                {
                    testMode = p.mode == "PlayMode" ? TestMode.PlayMode : TestMode.EditMode
                };

                if (!string.IsNullOrEmpty(p.filter))   filter.testNames     = new[] { p.filter };
                if (!string.IsNullOrEmpty(p.group))    filter.groupNames    = new[] { p.group };
                if (!string.IsNullOrEmpty(p.assembly)) filter.assemblyNames = new[] { p.assembly };

                try
                {
                    api.Execute(new ExecutionSettings(filter));
                }
                catch (Exception ex)
                {
                    listener.Unsubscribe();
                    ScriptableObject.DestroyImmediate(api);
                    var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                    e["message"] = ex.Message;
                    AgentBridge.Respond(e);
                }

                // async — Listener calls AgentBridge.Respond() when done
                return null;
            }

            [Serializable]
            private class Params
            {
                public string mode     = "EditMode";
                public string filter   = "";
                public string group    = "";
                public string assembly = "";
            }
        }

        // ── Callbacks ─────────────────────────────────────────────────────────

        private sealed class Listener : ICallbacks
        {
            private readonly string        _uid;
            private readonly TestRunnerApi _api;
            private int                    _passed, _failed, _skipped;
            private readonly List<(string name, string message)> _failures = new();
            private bool                   _done;

            public Listener(string uid, TestRunnerApi api)
            {
                _uid = uid;
                _api = api;
                AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            }

            internal void Unsubscribe() => AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;

            private void OnBeforeAssemblyReload()
            {
                AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
                if (_done) return;
                _done = true;
                ScriptableObject.DestroyImmediate(_api);
                var e = AgentBridge.MakeResponse(_uid, "run_tests", "interrupted");
                e["message"] = "Domain reload interrupted test run.";
                AgentBridge.Respond(e);
            }

            public void RunStarted(ITestAdaptor tests) { }
            public void TestStarted(ITestAdaptor test)  { }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (result.Test.IsSuite) return;

                if      (result.FailCount        > 0) { _failed++; AddFailure(result); }
                else if (result.SkipCount        > 0 ||
                         result.InconclusiveCount > 0)  _skipped++;
                else                                    _passed++;
            }

            public void RunFinished(ITestResultAdaptor _)
            {
                if (_done) return;
                _done = true;
                AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
                ScriptableObject.DestroyImmediate(_api);

                var failuresArray = new JsonArray();
                foreach (var f in _failures)
                    failuresArray.Add(new JsonObject { ["name"] = f.name, ["message"] = f.message });

                var resp = AgentBridge.MakeResponse(_uid, "run_tests", _failed > 0 ? "error" : "ok");
                resp["passed"]   = _passed;
                resp["failed"]   = _failed;
                resp["skipped"]  = _skipped;
                resp["total"]    = _passed + _failed + _skipped;
                resp["failures"] = failuresArray;
                AgentBridge.Respond(resp);
            }

            private void AddFailure(ITestResultAdaptor r) =>
                _failures.Add((r.Test.FullName, r.Message));
        }
    }
}
