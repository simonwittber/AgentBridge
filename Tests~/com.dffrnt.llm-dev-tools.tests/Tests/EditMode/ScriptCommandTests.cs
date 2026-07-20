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
        public void ExecuteScript_VoidReturn_ReturnsNullValue()
        {
            var resp = AgentBridge.TestInvoke("execute_script", "{\"code\":\"var x = 1 + 1;\"}");
            Assert.That(resp,                               Is.Not.Null);
            Assert.That(resp["status"]?.GetValue<string>(), Is.EqualTo("ok"));
            Assert.That(resp["returnValue"]?.GetValue<string>(), Is.Null.Or.Empty);
        }

        [Test]
        public void ExecuteScript_DebugLog_CapturedInLogs()
        {
            var resp = AgentBridge.TestInvoke("execute_script",
                "{\"code\":\"UnityEngine.Debug.Log(\\\"captured\\\"); return null;\"}");
            Assert.That(resp,                               Is.Not.Null);
            Assert.That(resp["status"]?.GetValue<string>(), Is.EqualTo("ok"));

            var logs = resp["logs"]?.AsArray();
            Assert.That(logs, Is.Not.Null);

            bool found = false;
            foreach (var entry in logs)
                if (entry?["message"]?.GetValue<string>()?.Contains("captured") == true) { found = true; break; }
            Assert.That(found, Is.True, "Expected 'captured' in logs");
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
            var resp = AgentBridge.TestInvoke("execute_script",
                "{\"code\":\"return UnityEngine.Application.productName;\"}");
            Assert.That(resp,                               Is.Not.Null);
            Assert.That(resp["status"]?.GetValue<string>(), Is.EqualTo("ok"));
        }
    }
}
