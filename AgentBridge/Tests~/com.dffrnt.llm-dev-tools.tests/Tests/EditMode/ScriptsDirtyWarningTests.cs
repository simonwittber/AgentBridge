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
        public void NotBlocked_WhenClean()
        {
            Assert.That(AgentBridge.CheckBlocked("hierarchy"), Is.Null);
        }

        [Test]
        public void Blocked_WhenScriptsDirty()
        {
            AgentBridge.MarkScriptsDirty();
            Assert.That(AgentBridge.CheckBlocked("hierarchy"), Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void NotBlocked_AfterClear()
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
        public void NotBlocked_WhenScriptsDirty_ForExemptCommands(string cmd)
        {
            AgentBridge.MarkScriptsDirty();
            Assert.That(AgentBridge.CheckBlocked(cmd), Is.Null, $"'{cmd}' should be exempt from compile block");
        }

        [TestCase("compile")]
        [TestCase("refresh")]
        [TestCase("status")]
        [TestCase("list_commands")]
        [TestCase("asset_write_text")]
        [TestCase("asset_create")]
        public void NotBlocked_WhenRefreshNeeded_ForExemptCommands(string cmd)
        {
            AgentBridge.MarkRefreshNeeded();
            Assert.That(AgentBridge.CheckBlocked(cmd), Is.Null, $"'{cmd}' should be exempt from refresh block");
        }

        [TestCase("object_create")]
        [TestCase("component_get")]
        [TestCase("hierarchy")]
        [TestCase("scene_info")]
        public void Blocked_WhenScriptsDirty_ForNonExemptCommands(string cmd)
        {
            AgentBridge.MarkScriptsDirty();
            Assert.That(AgentBridge.CheckBlocked(cmd), Is.Not.Null.And.Not.Empty, $"'{cmd}' should be blocked when scripts are dirty");
        }

        [TestCase("object_create")]
        [TestCase("hierarchy")]
        [TestCase("scene_info")]
        public void Blocked_WhenRefreshNeeded_ForNonExemptCommands(string cmd)
        {
            AgentBridge.MarkRefreshNeeded();
            Assert.That(AgentBridge.CheckBlocked(cmd), Is.Not.Null.And.Not.Empty, $"'{cmd}' should be blocked when refresh is needed");
        }
    }
}
