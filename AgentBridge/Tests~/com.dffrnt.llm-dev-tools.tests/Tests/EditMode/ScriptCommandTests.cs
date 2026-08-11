using System;
using System.IO;
using NUnit.Framework;

namespace LLMDevTools.Tests
{
    [TestFixture]
    public class ScriptCommandTests
    {
        // ── toolchain discovery ───────────────────────────────────────────────

        [Test]
        public void FindDotnet_ReturnsExistingPath()
        {
            var path = ScriptCommand.FindDotnet();
            Assert.That(path, Is.Not.Null, "dotnet not found in Unity install");
            Assert.That(File.Exists(path), Is.True, $"dotnet path does not exist: {path}");
        }

        [Test]
        public void FindCsc_ReturnsExistingPath()
        {
            var path = ScriptCommand.FindCsc();
            Assert.That(path, Is.Not.Null, "csc.dll not found in Unity install");
            Assert.That(File.Exists(path), Is.True, $"csc.dll path does not exist: {path}");
        }

        // ── WrapCode ──────────────────────────────────────────────────────────

        [Test]
        public void WrapCode_ContainsAgentScriptClass()
        {
            var wrapped = ScriptCommand.WrapCode("return \"hi\";");
            Assert.That(wrapped, Does.Contain("public static class AgentScript"));
            Assert.That(wrapped, Does.Contain("public static string Run()"));
            Assert.That(wrapped, Does.Contain("return \"hi\";"));
        }

        [Test]
        public void WrapCode_IncludesUnityUsings()
        {
            var wrapped = ScriptCommand.WrapCode("");
            Assert.That(wrapped, Does.Contain("using UnityEngine;"));
            Assert.That(wrapped, Does.Contain("using UnityEditor;"));
        }

        // ── CompileAndLoad ────────────────────────────────────────────────────

        [Test]
        public void CompileAndLoad_SimpleExpression_Succeeds()
        {
            var source   = ScriptCommand.WrapCode("return \"hello\";");
            var assembly = ScriptCommand.CompileAndLoad(source, out var error);
            Assert.That(error,    Is.Null,     $"Unexpected compile error: {error}");
            Assert.That(assembly, Is.Not.Null, "Assembly is null after successful compile");
        }

        [Test]
        public void CompileAndLoad_SyntaxError_ReturnsNullWithError()
        {
            var source   = ScriptCommand.WrapCode("this is not valid C#");
            var assembly = ScriptCommand.CompileAndLoad(source, out var error);
            Assert.That(assembly, Is.Null,     "Expected null assembly for invalid code");
            Assert.That(error,    Is.Not.Null, "Expected error message for invalid code");
        }

        [Test]
        public void CompileAndLoad_UnityApiAccess_Succeeds()
        {
            var source   = ScriptCommand.WrapCode("return UnityEngine.Application.productName;");
            var assembly = ScriptCommand.CompileAndLoad(source, out var error);
            Assert.That(error,    Is.Null,     $"Unexpected compile error: {error}");
            Assert.That(assembly, Is.Not.Null);
        }

        // ── execute_script command ────────────────────────────────────────────

        [Test]
        public void ExecuteScript_MissingCode_ReturnsError()
        {
            var resp = AgentBridge.TestInvoke("execute_script", "{}");
            Assert.That(resp,                               Is.Not.Null);
            Assert.That(resp["status"]?.GetValue<string>(), Is.EqualTo("error"));
        }

        [Test]
        public void ExecuteScript_ReturnString_ReturnsValue()
        {
            var resp = AgentBridge.TestInvoke("execute_script", "{\"code\":\"return \\\"hello world\\\";\"}");
            Assert.That(resp,                                   Is.Not.Null);
            Assert.That(resp["status"]?.GetValue<string>(),     Is.EqualTo("ok"));
            Assert.That(resp["returnValue"]?.GetValue<string>(), Is.EqualTo("hello world"));
        }

        [Test]
        public void ExecuteScript_ReturnNull_ReturnsOkWithNullValue()
        {
            var resp = AgentBridge.TestInvoke("execute_script", "{\"code\":\"return null;\"}");
            Assert.That(resp,                               Is.Not.Null);
            Assert.That(resp["status"]?.GetValue<string>(), Is.EqualTo("ok"));
            Assert.That(resp["returnValue"]?.GetValue<string>(), Is.Null.Or.Empty);
        }

        [Test]
        public void ExecuteScript_DebugLog_ResponseHasLogsField()
        {
            // Log callbacks require a free main thread update cycle to fire, which
            // doesn't happen inside TestInvoke's polling loop. We verify structure
            // only here; content capture is covered by the integration test.
            var resp = AgentBridge.TestInvoke("execute_script",
                "{\"code\":\"MT.Run(() => { UnityEngine.Debug.Log(\\\"captured\\\"); return 0; }); return null;\"}");
            Assert.That(resp,                               Is.Not.Null);
            Assert.That(resp["status"]?.GetValue<string>(), Is.EqualTo("ok"));
            Assert.That(resp["logs"],                       Is.Not.Null);
        }

        [Test]
        public void ExecuteScript_SyntaxError_ReturnsError()
        {
            var resp = AgentBridge.TestInvoke("execute_script", "{\"code\":\"not valid C# !!!\"}");
            Assert.That(resp,                               Is.Not.Null);
            Assert.That(resp["status"]?.GetValue<string>(), Is.EqualTo("error"));
            Assert.That(resp["message"]?.GetValue<string>(), Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void ExecuteScript_RuntimeException_ReturnsError()
        {
            var resp = AgentBridge.TestInvoke("execute_script",
                "{\"code\":\"throw new System.Exception(\\\"boom\\\");\"}");
            Assert.That(resp,                                Is.Not.Null);
            Assert.That(resp["status"]?.GetValue<string>(),  Is.EqualTo("error"));
            Assert.That(resp["message"]?.GetValue<string>(), Does.Contain("boom"));
        }

        [Test]
        public void ExecuteScript_UnityApiCall_ReturnsOk()
        {
            // Unity API must be wrapped in MT.Run — scripts run on a background thread.
            var resp = AgentBridge.TestInvoke("execute_script",
                "{\"code\":\"return MT.Run(() => UnityEngine.Application.productName);\"}");
            Assert.That(resp,                               Is.Not.Null);
            Assert.That(resp["status"]?.GetValue<string>(), Is.EqualTo("ok"));
        }

        // ── async / MainThread ────────────────────────────────────────────────

        [Test]
        public void WrapCode_ContainsMTAlias()
        {
            var wrapped = ScriptCommand.WrapCode("return null;");
            Assert.That(wrapped, Does.Contain("using MT = LLMDevTools.MainThread;"));
        }

        [Test]
        public void ExecuteScript_MainThreadRun_ReturnsValue()
        {
            var resp = AgentBridge.TestInvoke("execute_script",
                "{\"code\":\"return MT.Run(() => UnityEngine.Application.unityVersion);\"}");
            Assert.That(resp,                                    Is.Not.Null);
            Assert.That(resp["status"]?.GetValue<string>(),      Is.EqualTo("ok"));
            Assert.That(resp["returnValue"]?.GetValue<string>(), Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void ExecuteScript_MainThreadRun_MultipleCallsAllExecute()
        {
            var code = "int a = MT.Run(() => 1); int b = MT.Run(() => 2); return (a + b).ToString();";
            var resp = AgentBridge.TestInvoke("execute_script",
                $"{{\"code\":\"{code}\"}}");
            Assert.That(resp,                                    Is.Not.Null);
            Assert.That(resp["status"]?.GetValue<string>(),      Is.EqualTo("ok"));
            Assert.That(resp["returnValue"]?.GetValue<string>(), Is.EqualTo("3"));
        }

        [Test]
        public void RunOnMainThread_FromMainThread_ExecutesDirectly()
        {
            int result = AgentBridge.RunOnMainThread(() => 42);
            Assert.That(result, Is.EqualTo(42));
        }

        [Test]
        public void RunOnMainThread_FromMainThread_ExceptionPropagates()
        {
            Assert.Throws<InvalidOperationException>(() =>
                AgentBridge.RunOnMainThread<int>(() => throw new InvalidOperationException("test")));
        }

        // ── EnrichThreadException ─────────────────────────────────────────────

        [Test]
        public void EnrichThreadException_NullInput_ReturnsNull()
        {
            Assert.That(ScriptCommand.EnrichThreadException(null), Is.Null);
        }

        [Test]
        public void EnrichThreadException_UnrelatedMessage_Unchanged()
        {
            var ex     = new Exception("something went wrong");
            var result = ScriptCommand.EnrichThreadException(ex);
            Assert.That(result, Is.SameAs(ex));
        }

        [Test]
        public void EnrichThreadException_MainThreadMessage_AppendsMTHint()
        {
            var ex     = new Exception("get_productName can only be called from the main thread.");
            var result = ScriptCommand.EnrichThreadException(ex);
            Assert.That(result,         Is.Not.SameAs(ex));
            Assert.That(result.Message, Does.Contain("MT.Run(() => ...)"));
            Assert.That(result.Message, Does.Contain("main thread"));
        }

        [Test]
        public void ExecuteScript_MainThreadViolation_ErrorContainsMTHint()
        {
            var resp = AgentBridge.TestInvoke("execute_script",
                "{\"code\":\"throw new System.Exception(\\\"main thread err\\\");\"}");
            Assert.That(resp,                                Is.Not.Null);
            Assert.That(resp["status"]?.GetValue<string>(),  Is.EqualTo("error"));
            Assert.That(resp["message"]?.GetValue<string>(), Does.Contain("MT.Run(() => ...)"));
        }
    }
}
