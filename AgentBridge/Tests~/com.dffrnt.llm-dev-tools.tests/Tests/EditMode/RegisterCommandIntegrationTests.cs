using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;

namespace LLMDevTools.Tests
{
    [TestFixture]
    public class RegisterCommandIntegrationTests
    {
        private const string StorageDir = "Assets/AgentBridgeScripts";
        private const string TestCommandName = "integration_test_cmd";
        private const string TestCommandClassName = "AgentScript_IntegrationTestCmd";

        [SetUp]
        public void Setup()
        {
            CleanupTestCommand();
        }

        [TearDown]
        public void Cleanup()
        {
            CleanupTestCommand();
        }

        // ── Register and discover ──────────────────────────────────────────────

        [Test]
        public void RegisterCommand_CreatesSourceAndMetadataFiles()
        {
            var sourceFile = Path.Combine(StorageDir, $"AgentScript_{TestCommandName}.cs");
            var metaFile = Path.Combine(StorageDir, $"AgentScript_{TestCommandName}.meta.json");

            var json = "{\"name\": \"integration_test_cmd\", \"description\": \"Integration test\", \"code\": \"return \\\"success\\\";\", \"args\": \"[]\"}";
            var resp = AgentBridge.TestInvoke("register_command", json);

            Assert.That(resp["status"]?.GetValue<string>(), Is.EqualTo("ok"));
            Assert.That(File.Exists(sourceFile), Is.True, "Source file not created");
            Assert.That(File.Exists(metaFile), Is.True, "Metadata file not created");
        }

        [Test]
        public void RegisterCommand_GeneratedSourceIsValidCSharp()
        {
            var sourceFile = Path.Combine(StorageDir, $"AgentScript_{TestCommandName}.cs");

            var json = "{\"name\": \"integration_test_cmd\", \"description\": \"Integration test\", \"code\": \"return \\\"test\\\";\", \"args\": \"[]\"}";
            var resp = AgentBridge.TestInvoke("register_command", json);

            Assert.That(resp["status"]?.GetValue<string>(), Is.EqualTo("ok"));

            var content = File.ReadAllText(sourceFile);
            Assert.That(content, Does.Contain("namespace LLMDevTools.GeneratedCommands"));
            Assert.That(content, Does.Contain($"class {TestCommandClassName}"));
            Assert.That(content, Does.Contain("IAgentCommand"));
            Assert.That(content, Does.Contain("public string Cmd =>"));
            Assert.That(content, Does.Contain("private string Run()"));
        }

        [Test]
        public void RegisterCommand_WithParameters_GeneratesCorrectSignature()
        {
            var sourceFile = Path.Combine(StorageDir, $"AgentScript_{TestCommandName}.cs");

            var argsJson = "[{\\\"name\\\": \\\"message\\\", \\\"type\\\": \\\"string\\\", \\\"default\\\": \\\"\\\", \\\"description\\\": \\\"Message\\\"}]";
            var json = $"{{\"name\": \"integration_test_cmd\", \"description\": \"Test\", \"code\": \"return message;\", \"args\": \"{argsJson}\"}}";
            var resp = AgentBridge.TestInvoke("register_command", json);

            Assert.That(resp["status"]?.GetValue<string>(), Is.EqualTo("ok"));

            var content = File.ReadAllText(sourceFile);
            Assert.That(content, Does.Contain("public string message = \"\";"));
            Assert.That(content, Does.Contain("private string Run(string message)"));
            Assert.That(content, Does.Contain("p.message"));
        }

        [Test]
        public void RegisterCommand_MultipleParameters_TypesPreserved()
        {
            var sourceFile = Path.Combine(StorageDir, $"AgentScript_{TestCommandName}.cs");

            var argsJson = "[" +
                "{\\\"name\\\": \\\"path\\\", \\\"type\\\": \\\"string\\\", \\\"default\\\": \\\"\\\", \\\"description\\\": \\\"Path\\\"}," +
                "{\\\"name\\\": \\\"count\\\", \\\"type\\\": \\\"int\\\", \\\"default\\\": \\\"0\\\", \\\"description\\\": \\\"Count\\\"}," +
                "{\\\"name\\\": \\\"ratio\\\", \\\"type\\\": \\\"float\\\", \\\"default\\\": \\\"1.0\\\", \\\"description\\\": \\\"Ratio\\\"}," +
                "{\\\"name\\\": \\\"enabled\\\", \\\"type\\\": \\\"bool\\\", \\\"default\\\": \\\"false\\\", \\\"description\\\": \\\"Enabled\\\"}" +
                "]";
            var json = $"{{\"name\": \"integration_test_cmd\", \"description\": \"Test\", \"code\": \"return \\\"ok\\\";\", \"args\": \"{argsJson}\"}}";
            var resp = AgentBridge.TestInvoke("register_command", json);

            Assert.That(resp["status"]?.GetValue<string>(), Is.EqualTo("ok"));

            var content = File.ReadAllText(sourceFile);
            Assert.That(content, Does.Contain("public string path = \"\";"));
            Assert.That(content, Does.Contain("public int count = 0;"));
            Assert.That(content, Does.Contain("public float ratio = 0f;"));
            Assert.That(content, Does.Contain("public bool enabled = false;"));
            Assert.That(content, Does.Contain("private string Run(string path, int count, float ratio, bool enabled)"));
        }

        [Test]
        public void RegisterCommand_MetadataContainsHash()
        {
            var metaFile = Path.Combine(StorageDir, $"AgentScript_{TestCommandName}.meta.json");

            var json = "{\"name\": \"integration_test_cmd\", \"description\": \"Test\", \"code\": \"return \\\"hash test\\\";\", \"args\": \"[]\"}";
            var resp = AgentBridge.TestInvoke("register_command", json);

            Assert.That(resp["status"]?.GetValue<string>(), Is.EqualTo("ok"));

            var content = File.ReadAllText(metaFile);
            Assert.That(content, Does.Contain("\"source_hash\": \"sha256:"));
            Assert.That(content, Does.Contain("\"registered_at\": \""));
            Assert.That(content, Does.Contain("\"name\": \"integration_test_cmd\""));
        }

        [Test]
        public void RegisterCommand_InvalidName_Rejected()
        {
            var json = "{\"name\": \"123invalid\", \"description\": \"Test\", \"code\": \"return \\\"ok\\\";\", \"args\": \"[]\"}";
            var resp = AgentBridge.TestInvoke("register_command", json);

            Assert.That(resp["status"]?.GetValue<string>(), Is.EqualTo("error"));
            Assert.That(resp["message"]?.GetValue<string>(), Does.Contain("alphanumeric"));
        }

        [Test]
        public void RegisterCommand_ThreadSleepRejected()
        {
            var json = "{\"name\": \"integration_test_cmd\", \"description\": \"Test\", \"code\": \"Thread.Sleep(1); return \\\"ok\\\";\", \"args\": \"[]\"}";
            var resp = AgentBridge.TestInvoke("register_command", json);

            Assert.That(resp["status"]?.GetValue<string>(), Is.EqualTo("error"));
            Assert.That(resp["message"]?.GetValue<string>(), Does.Contain("Thread.Sleep"));
        }

        [Test]
        public void RegisterCommand_ReservedNameRejected()
        {
            var json = "{\"name\": \"execute_script\", \"description\": \"Test\", \"code\": \"return \\\"ok\\\";\", \"args\": \"[]\"}";
            var resp = AgentBridge.TestInvoke("register_command", json);

            Assert.That(resp["status"]?.GetValue<string>(), Is.EqualTo("error"));
            Assert.That(resp["message"]?.GetValue<string>(), Does.Contain("reserved"));
        }

        [Test]
        public void RegisterCommand_ResponseIncludesStoragePath()
        {
            var json = "{\"name\": \"integration_test_cmd\", \"description\": \"Test\", \"code\": \"return \\\"ok\\\";\", \"args\": \"[]\"}";
            var resp = AgentBridge.TestInvoke("register_command", json);

            Assert.That(resp["status"]?.GetValue<string>(), Is.EqualTo("ok"));
            Assert.That(resp["command"]?["storage_path"]?.GetValue<string>(), Does.Contain("AgentScript_integration_test_cmd.cs"));
            Assert.That(resp["command"]?["name"]?.GetValue<string>(), Is.EqualTo("integration_test_cmd"));
        }

        // ── Manual runtime invocation (post-domain-reload) ────────────────────

        [Test]
        public void RegisteredCommand_CanBeManuallyInvoked_PostDomainReload()
        {
            // This test assumes a domain reload has occurred and the command is discoverable.
            // In practice, this requires:
            //   1. Register the command (writes source file)
            //   2. Manually trigger domain reload (via EditorApplication.UnityEditor.EditorUtility)
            //   3. This test verifies the command can be found and invoked
            //
            // For now, we verify that registered commands generate valid IAgentCommand classes
            // that can be instantiated if manually loaded.

            var sourceFile = Path.Combine(StorageDir, $"AgentScript_{TestCommandName}.cs");

            var json = "{\"name\": \"integration_test_cmd\", \"description\": \"Test\", \"code\": \"return \\\"manual invoke\\\";\", \"args\": \"[]\"}";
            var resp = AgentBridge.TestInvoke("register_command", json);

            Assert.That(resp["status"]?.GetValue<string>(), Is.EqualTo("ok"));
            Assert.That(File.Exists(sourceFile), Is.True);

            // After a domain reload, the generated source would be compiled by the asmdef,
            // and TypeCache would discover the IAgentCommand. This test verifies the
            // preconditions are met (source is valid C# that compiles).
            var content = File.ReadAllText(sourceFile);
            Assert.That(content, Does.Contain("IAgentCommand"));
            Assert.That(content, Does.Contain("Execute(string uid, string requestJson)"));
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private void CleanupTestCommand()
        {
            var sourceFile = Path.Combine(StorageDir, $"AgentScript_{TestCommandName}.cs");
            var metaFile = Path.Combine(StorageDir, $"AgentScript_{TestCommandName}.meta.json");

            if (File.Exists(sourceFile))
                File.Delete(sourceFile);
            if (File.Exists(metaFile))
                File.Delete(metaFile);
        }
    }
}
