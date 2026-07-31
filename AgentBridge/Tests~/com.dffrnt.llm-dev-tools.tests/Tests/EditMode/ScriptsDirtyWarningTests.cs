using NUnit.Framework;

namespace LLMDevTools.Tests
{
    [TestFixture]
    public class ScriptsDirtyTests
    {
        [TearDown]
        public void TearDown()
        {
            AgentBridge.ClearScriptsDirty();
            AgentBridge.ClearRefreshNeeded();
        }

        [Test]
        public void NoAutoFix_WhenClean()
        {
            Assert.That(AgentBridge.CheckBlocked("hierarchy"), Is.Null);
        }

        [Test]
        public void NeedsAutoCompile_WhenScriptsDirty()
        {
            AgentBridge.MarkScriptsDirty();
            Assert.That(AgentBridge.CheckBlocked("hierarchy"), Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void NoAutoFix_AfterScriptsDirtyCleared()
        {
            AgentBridge.MarkScriptsDirty();
            AgentBridge.ClearScriptsDirty();
            Assert.That(AgentBridge.CheckBlocked("hierarchy"), Is.Null);
        }

        [TestCase("compile")]
        [TestCase("refresh")]
        [TestCase("execute_script")]
        [TestCase("run_editor_tests")]
        [TestCase("status")]
        [TestCase("list_commands")]
        [TestCase("console_logs")]
        [TestCase("asset_write_text")]
        [TestCase("asset_create")]
        [TestCase("asset_delete")]
        public void NoAutoFix_WhenScriptsDirty_ForExemptCommands(string cmd)
        {
            AgentBridge.MarkScriptsDirty();
            Assert.That(AgentBridge.CheckBlocked(cmd), Is.Null, $"'{cmd}' should be exempt from auto-compile");
        }

        [TestCase("compile")]
        [TestCase("refresh")]
        [TestCase("status")]
        [TestCase("list_commands")]
        [TestCase("asset_write_text")]
        [TestCase("asset_create")]
        public void NoAutoFix_WhenRefreshNeeded_ForExemptCommands(string cmd)
        {
            AgentBridge.MarkRefreshNeeded();
            Assert.That(AgentBridge.CheckBlocked(cmd), Is.Null, $"'{cmd}' should be exempt from auto-refresh");
        }

        [TestCase("object_create")]
        [TestCase("component_get")]
        [TestCase("hierarchy")]
        [TestCase("scene_info")]
        public void NeedsAutoCompile_WhenScriptsDirty_ForNonExemptCommands(string cmd)
        {
            AgentBridge.MarkScriptsDirty();
            Assert.That(AgentBridge.CheckBlocked(cmd), Is.Not.Null.And.Not.Empty, $"'{cmd}' should trigger auto-compile when scripts are dirty");
        }

        [TestCase("object_create")]
        [TestCase("hierarchy")]
        [TestCase("scene_info")]
        public void NeedsAutoRefresh_WhenRefreshNeeded_ForNonExemptCommands(string cmd)
        {
            AgentBridge.MarkRefreshNeeded();
            Assert.That(AgentBridge.CheckBlocked(cmd), Is.Not.Null.And.Not.Empty, $"'{cmd}' should trigger auto-refresh when assets are dirty");
        }
    }
}
