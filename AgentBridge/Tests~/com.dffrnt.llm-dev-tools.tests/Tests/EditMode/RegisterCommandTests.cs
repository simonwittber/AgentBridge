using System;
using System.IO;
using NUnit.Framework;

namespace LLMDevTools.Tests
{
    [TestFixture]
    public class RegisterCommandTests
    {
        private RegisterCommandCmd _cmd;

        [SetUp]
        public void Setup()
        {
            _cmd = new RegisterCommandCmd();
        }

        // ── Validation: name ──────────────────────────────────────────────────

        [Test]
        public void Execute_EmptyName_ReturnsError()
        {
            var json = "{\"name\": \"\", \"description\": \"test\", \"code\": \"return \\\"ok\\\";\", \"args\": \"[]\"}";
            var resp = _cmd.Execute("uid1", json);

            Assert.That(resp["status"].GetValue<string>(), Is.EqualTo("error"));
            Assert.That(resp["message"].GetValue<string>(), Does.Contain("name is required"));
        }

        [Test]
        public void Execute_NameStartsWithDigit_ReturnsError()
        {
            var json = "{\"name\": \"1invalid\", \"description\": \"test\", \"code\": \"return \\\"ok\\\";\", \"args\": \"[]\"}";
            var resp = _cmd.Execute("uid1", json);

            Assert.That(resp["status"].GetValue<string>(), Is.EqualTo("error"));
            Assert.That(resp["message"].GetValue<string>(), Does.Contain("alphanumeric"));
        }

        [Test]
        public void Execute_NameWithInvalidChars_ReturnsError()
        {
            var json = "{\"name\": \"my-command\", \"description\": \"test\", \"code\": \"return \\\"ok\\\";\", \"args\": \"[]\"}";
            var resp = _cmd.Execute("uid1", json);

            Assert.That(resp["status"].GetValue<string>(), Is.EqualTo("error"));
            Assert.That(resp["message"].GetValue<string>(), Does.Contain("alphanumeric"));
        }

        [Test]
        public void Execute_ReservedName_ReturnsError()
        {
            var json = "{\"name\": \"execute_script\", \"description\": \"test\", \"code\": \"return \\\"ok\\\";\", \"args\": \"[]\"}";
            var resp = _cmd.Execute("uid1", json);

            Assert.That(resp["status"].GetValue<string>(), Is.EqualTo("error"));
            Assert.That(resp["message"].GetValue<string>(), Does.Contain("reserved"));
        }

        [Test]
        public void Execute_ValidName_Succeeds()
        {
            var json = "{\"name\": \"my_command\", \"description\": \"test\", \"code\": \"return \\\"ok\\\";\", \"args\": \"[]\"}";
            var resp = _cmd.Execute("uid1", json);

            Assert.That(resp["status"].GetValue<string>(), Is.EqualTo("ok"));
        }

        // ── Validation: code ──────────────────────────────────────────────────

        [Test]
        public void Execute_EmptyCode_ReturnsError()
        {
            var json = "{\"name\": \"test_cmd\", \"description\": \"test\", \"code\": \"\", \"args\": \"[]\"}";
            var resp = _cmd.Execute("uid1", json);

            Assert.That(resp["status"].GetValue<string>(), Is.EqualTo("error"));
            Assert.That(resp["message"].GetValue<string>(), Does.Contain("code is required"));
        }

        [Test]
        public void Execute_CodeWithThreadSleep_ReturnsError()
        {
            var json = "{\"name\": \"test_cmd\", \"description\": \"test\", \"code\": \"Thread.Sleep(100); return \\\"ok\\\";\", \"args\": \"[]\"}";
            var resp = _cmd.Execute("uid1", json);

            Assert.That(resp["status"].GetValue<string>(), Is.EqualTo("error"));
            Assert.That(resp["message"].GetValue<string>(), Does.Contain("Thread.Sleep"));
        }

        // ── Validation: args ──────────────────────────────────────────────────

        [Test]
        public void Execute_InvalidArgsJson_ReturnsError()
        {
            var json = "{\"name\": \"test_cmd\", \"description\": \"test\", \"code\": \"return \\\"ok\\\";\", \"args\": \"{invalid json\"}";
            var resp = _cmd.Execute("uid1", json);

            Assert.That(resp["status"].GetValue<string>(), Is.EqualTo("error"));
            Assert.That(resp["message"].GetValue<string>(), Does.Contain("Invalid args JSON"));
        }

        [Test]
        public void Execute_ArgWithoutName_ReturnsError()
        {
            var argsJson = "[{\\\"type\\\": \\\"string\\\", \\\"default\\\": \\\"\\\", \\\"description\\\": \\\"test\\\"}]";
            var json = $"{{\"name\": \"test_cmd\", \"description\": \"test\", \"code\": \"return \\\"ok\\\";\", \"args\": \"{argsJson}\"}}";
            var resp = _cmd.Execute("uid1", json);

            Assert.That(resp["status"].GetValue<string>(), Is.EqualTo("error"));
            Assert.That(resp["message"].GetValue<string>(), Does.Contain("name"));
        }

        [Test]
        public void Execute_InvalidArgType_ReturnsError()
        {
            var argsJson = "[{\\\"name\\\": \\\"param\\\", \\\"type\\\": \\\"invalid\\\", \\\"default\\\": \\\"\\\", \\\"description\\\": \\\"test\\\"}]";
            var json = $"{{\"name\": \"test_cmd\", \"description\": \"test\", \"code\": \"return \\\"ok\\\";\", \"args\": \"{argsJson}\"}}";
            var resp = _cmd.Execute("uid1", json);

            Assert.That(resp["status"].GetValue<string>(), Is.EqualTo("error"));
            Assert.That(resp["message"].GetValue<string>(), Does.Contain("Invalid arg type"));
        }

        [Test]
        public void Execute_ValidArgs_Succeeds()
        {
            var argsJson = "[{\\\"name\\\": \\\"path\\\", \\\"type\\\": \\\"string\\\", \\\"default\\\": \\\"\\\", \\\"description\\\": \\\"Asset path\\\"}]";
            var json = $"{{\"name\": \"test_cmd\", \"description\": \"test\", \"code\": \"return \\\"ok\\\";\", \"args\": \"{argsJson}\"}}";
            var resp = _cmd.Execute("uid1", json);

            Assert.That(resp["status"].GetValue<string>(), Is.EqualTo("ok"));
        }

        // ── File generation ──────────────────────────────────────────────────

        [Test]
        public void Execute_CreatesSourceFile()
        {
            var storageDir = Path.Combine("Assets", "AgentBridgeScripts");
            var sourceFile = Path.Combine(storageDir, "AgentScript_test_cmd.cs");

            if (File.Exists(sourceFile))
                File.Delete(sourceFile);

            var json = "{\"name\": \"test_cmd\", \"description\": \"Test command\", \"code\": \"return \\\"result\\\";\", \"args\": \"[]\"}";
            var resp = _cmd.Execute("uid1", json);

            Assert.That(resp["status"].GetValue<string>(), Is.EqualTo("ok"));
            Assert.That(File.Exists(sourceFile), Is.True, $"Source file not created at {sourceFile}");

            var content = File.ReadAllText(sourceFile);
            Assert.That(content, Does.Contain("AgentScript_TestCmd"));
            Assert.That(content, Does.Contain("public string Cmd => \"test_cmd\""));
            Assert.That(content, Does.Contain("return \"result\";"));
        }

        [Test]
        public void Execute_CreatesMetadataFile()
        {
            var storageDir = Path.Combine("Assets", "AgentBridgeScripts");
            var metaFile = Path.Combine(storageDir, "AgentScript_meta_test.meta.json");

            if (File.Exists(metaFile))
                File.Delete(metaFile);

            var json = "{\"name\": \"meta_test\", \"description\": \"Meta test\", \"code\": \"return \\\"ok\\\";\", \"args\": \"[]\"}";
            var resp = _cmd.Execute("uid1", json);

            Assert.That(resp["status"].GetValue<string>(), Is.EqualTo("ok"));
            Assert.That(File.Exists(metaFile), Is.True, $"Metadata file not created at {metaFile}");

            var content = File.ReadAllText(metaFile);
            Assert.That(content, Does.Contain("\"name\": \"meta_test\""));
            Assert.That(content, Does.Contain("\"source_hash\": \"sha256:"));
        }

        [Test]
        public void Execute_SourceFileContainsParameterMarshaling()
        {
            var storageDir = Path.Combine("Assets", "AgentBridgeScripts");
            var sourceFile = Path.Combine(storageDir, "AgentScript_param_test.cs");

            if (File.Exists(sourceFile))
                File.Delete(sourceFile);

            var argsJson = "[{\\\"name\\\": \\\"count\\\", \\\"type\\\": \\\"int\\\", \\\"default\\\": \\\"0\\\", \\\"description\\\": \\\"Count\\\"}]";
            var json = $"{{\"name\": \"param_test\", \"description\": \"Param test\", \"code\": \"return $\\\"Count: {{count}}\\\";\", \"args\": \"{argsJson}\"}}";
            var resp = _cmd.Execute("uid1", json);

            Assert.That(resp["status"].GetValue<string>(), Is.EqualTo("ok"));
            var content = File.ReadAllText(sourceFile);
            Assert.That(content, Does.Contain("public int count = 0;"));
            Assert.That(content, Does.Contain("private string Run(int count)"));
        }

        // ── Cleanup ───────────────────────────────────────────────────────────

        [TearDown]
        public void Cleanup()
        {
            var storageDir = Path.Combine("Assets", "AgentBridgeScripts");
            var filesToDelete = new[] { "test_cmd", "meta_test", "param_test", "my_command" };

            foreach (var name in filesToDelete)
            {
                var sourceFile = Path.Combine(storageDir, $"AgentScript_{name}.cs");
                var metaFile = Path.Combine(storageDir, $"AgentScript_{name}.meta.json");

                if (File.Exists(sourceFile))
                    File.Delete(sourceFile);
                if (File.Exists(metaFile))
                    File.Delete(metaFile);
            }
        }
    }
}
