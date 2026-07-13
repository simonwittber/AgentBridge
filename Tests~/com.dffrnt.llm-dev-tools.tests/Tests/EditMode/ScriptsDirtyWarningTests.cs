using System.Text.Json.Nodes;
using NUnit.Framework;

namespace LLMDevTools.Tests
{
    [TestFixture]
    public class ScriptsDirtyWarningTests
    {
        [TearDown]
        public void TearDown() => AgentBridge.ClearScriptsDirty();

        // ── InjectWarnings ────────────────────────────────────────────────────

        [Test]
        public void Warning_AbsentWhenNotDirty()
        {
            var resp = Resp("hierarchy");
            AgentBridge.InjectWarnings(resp);
            Assert.That(resp["_warning"], Is.Null);
        }

        [Test]
        public void Warning_PresentWhenDirty()
        {
            AgentBridge.MarkScriptsDirty();
            var resp = Resp("hierarchy");
            AgentBridge.InjectWarnings(resp);
            Assert.That(resp["_warning"]?.GetValue<string>(), Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void Warning_AbsentAfterClear()
        {
            AgentBridge.MarkScriptsDirty();
            AgentBridge.ClearScriptsDirty();
            var resp = Resp("hierarchy");
            AgentBridge.InjectWarnings(resp);
            Assert.That(resp["_warning"], Is.Null);
        }

        [TestCase("compile")]
        [TestCase("refresh")]
        [TestCase("status")]
        [TestCase("focus")]
        [TestCase("commands")]
        [TestCase("asset_write_text")]
        public void Warning_SuppressedForExcludedCommands(string cmd)
        {
            AgentBridge.MarkScriptsDirty();
            var resp = Resp(cmd);
            AgentBridge.InjectWarnings(resp);
            Assert.That(resp["_warning"], Is.Null, $"expected no warning for '{cmd}'");
        }

        [TestCase("object_create")]
        [TestCase("component_get")]
        [TestCase("hierarchy")]
        public void Warning_PresentForNonExcludedCommands(string cmd)
        {
            AgentBridge.MarkScriptsDirty();
            var resp = Resp(cmd);
            AgentBridge.InjectWarnings(resp);
            Assert.That(resp["_warning"]?.GetValue<string>(), Is.Not.Null.And.Not.Empty,
                $"expected warning for '{cmd}'");
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private static JsonObject Resp(string cmd) => new() { ["uid"] = "test", ["cmd"] = cmd, ["status"] = "ok" };
    }
}
