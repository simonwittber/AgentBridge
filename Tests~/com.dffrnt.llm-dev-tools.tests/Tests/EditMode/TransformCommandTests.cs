using NUnit.Framework;
using UnityEngine;

namespace LLMDevTools.Tests
{
    [TestFixture]
    public class TransformCommandTests
    {
        [TearDown]
        public void TearDown()
        {
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include))
                if (go != null && go.name.StartsWith("[LLM]"))
                    Object.DestroyImmediate(go);
        }

        // ── set_transform ─────────────────────────────────────────────────────

        [Test]
        public void SetTransform_LocalPosition()
        {
            AgentBridge.TestInvoke("object_create", "{\"name\":\"[LLM]TxPos\"}");
            var resp = AgentBridge.TestInvoke("set_transform",
                "{\"path\":\"[LLM]TxPos\",\"position\":{\"x\":1,\"y\":2,\"z\":3}}");

            Assert.That(resp?["status"]?.GetValue<string>(), Is.EqualTo("ok"));
            var go = SceneBridge.FindByPath("[LLM]TxPos");
            Assert.That(go.transform.localPosition.x, Is.EqualTo(1f).Within(0.001f));
            Assert.That(go.transform.localPosition.y, Is.EqualTo(2f).Within(0.001f));
            Assert.That(go.transform.localPosition.z, Is.EqualTo(3f).Within(0.001f));
        }

        [Test]
        public void SetTransform_LocalRotationEuler()
        {
            AgentBridge.TestInvoke("object_create", "{\"name\":\"[LLM]TxRot\"}");
            var resp = AgentBridge.TestInvoke("set_transform",
                "{\"path\":\"[LLM]TxRot\",\"rotation\":{\"x\":45,\"y\":90,\"z\":0}}");

            Assert.That(resp?["status"]?.GetValue<string>(), Is.EqualTo("ok"));
            var go = SceneBridge.FindByPath("[LLM]TxRot");
            Assert.That(go.transform.localEulerAngles.x, Is.EqualTo(45f).Within(0.1f));
            Assert.That(go.transform.localEulerAngles.y, Is.EqualTo(90f).Within(0.1f));
        }

        [Test]
        public void SetTransform_LocalScale()
        {
            AgentBridge.TestInvoke("object_create", "{\"name\":\"[LLM]TxScale\"}");
            var resp = AgentBridge.TestInvoke("set_transform",
                "{\"path\":\"[LLM]TxScale\",\"scale\":{\"x\":2,\"y\":3,\"z\":4}}");

            Assert.That(resp?["status"]?.GetValue<string>(), Is.EqualTo("ok"));
            var go = SceneBridge.FindByPath("[LLM]TxScale");
            Assert.That(go.transform.localScale.x, Is.EqualTo(2f).Within(0.001f));
            Assert.That(go.transform.localScale.y, Is.EqualTo(3f).Within(0.001f));
            Assert.That(go.transform.localScale.z, Is.EqualTo(4f).Within(0.001f));
        }

        [Test]
        public void SetTransform_WorldSpacePosition()
        {
            AgentBridge.TestInvoke("object_create", "{\"name\":\"[LLM]TxWorld\"}");
            var resp = AgentBridge.TestInvoke("set_transform",
                "{\"path\":\"[LLM]TxWorld\",\"position\":{\"x\":5,\"y\":6,\"z\":7},\"space\":\"world\"}");

            Assert.That(resp?["status"]?.GetValue<string>(), Is.EqualTo("ok"));
            var go = SceneBridge.FindByPath("[LLM]TxWorld");
            Assert.That(go.transform.position.x, Is.EqualTo(5f).Within(0.001f));
            Assert.That(go.transform.position.y, Is.EqualTo(6f).Within(0.001f));
            Assert.That(go.transform.position.z, Is.EqualTo(7f).Within(0.001f));
        }

        [Test]
        public void SetTransform_Compound_PositionRotationScale()
        {
            AgentBridge.TestInvoke("object_create", "{\"name\":\"[LLM]TxCompound\"}");
            var resp = AgentBridge.TestInvoke("set_transform",
                "{\"path\":\"[LLM]TxCompound\"," +
                "\"position\":{\"x\":1,\"y\":0,\"z\":0}," +
                "\"rotation\":{\"x\":0,\"y\":45,\"z\":0}," +
                "\"scale\":{\"x\":2,\"y\":2,\"z\":2}}");

            Assert.That(resp?["status"]?.GetValue<string>(), Is.EqualTo("ok"));
            var go = SceneBridge.FindByPath("[LLM]TxCompound");
            Assert.That(go.transform.localPosition.x,    Is.EqualTo(1f).Within(0.001f));
            Assert.That(go.transform.localEulerAngles.y, Is.EqualTo(45f).Within(0.1f));
            Assert.That(go.transform.localScale.x,       Is.EqualTo(2f).Within(0.001f));
        }

        [Test]
        public void SetTransform_NotFound_ReturnsError()
        {
            var resp = AgentBridge.TestInvoke("set_transform",
                "{\"path\":\"DoesNotExist\",\"position\":{\"x\":0,\"y\":0,\"z\":0}}");
            Assert.That(resp?["status"]?.GetValue<string>(), Is.EqualTo("error"));
        }

        // ── duplicate_object ──────────────────────────────────────────────────

        [Test]
        public void DuplicateObject_CreatesCopy()
        {
            AgentBridge.TestInvoke("object_create", "{\"name\":\"[LLM]Original\"}");
            var resp = AgentBridge.TestInvoke("duplicate_object", "{\"path\":\"[LLM]Original\"}");

            Assert.That(resp?["status"]?.GetValue<string>(), Is.EqualTo("ok"));
            var path = resp?["path"]?.GetValue<string>();
            Assert.That(path, Is.Not.Null);
            Assert.That(SceneBridge.FindByPath(path), Is.Not.Null);
        }

        [Test]
        public void DuplicateObject_WithCustomName()
        {
            AgentBridge.TestInvoke("object_create", "{\"name\":\"[LLM]Src\"}");
            var resp = AgentBridge.TestInvoke("duplicate_object",
                "{\"path\":\"[LLM]Src\",\"name\":\"[LLM]Copy\"}");

            Assert.That(resp?["status"]?.GetValue<string>(), Is.EqualTo("ok"));
            Assert.That(resp?["path"]?.GetValue<string>(), Is.EqualTo("[LLM]Copy"));
            Assert.That(SceneBridge.FindByPath("[LLM]Copy"), Is.Not.Null);
        }

        [Test]
        public void DuplicateObject_NotFound_ReturnsError()
        {
            var resp = AgentBridge.TestInvoke("duplicate_object", "{\"path\":\"DoesNotExist\"}");
            Assert.That(resp?["status"]?.GetValue<string>(), Is.EqualTo("error"));
        }

        // ── reparent_object ───────────────────────────────────────────────────

        [Test]
        public void ReparentObject_ToNewParent()
        {
            AgentBridge.TestInvoke("object_create", "{\"name\":\"[LLM]RpParent\"}");
            AgentBridge.TestInvoke("object_create", "{\"name\":\"[LLM]RpChild\"}");
            var resp = AgentBridge.TestInvoke("reparent_object",
                "{\"path\":\"[LLM]RpChild\",\"parent\":\"[LLM]RpParent\"}");

            Assert.That(resp?["status"]?.GetValue<string>(), Is.EqualTo("ok"));
            var child = SceneBridge.FindByPath("[LLM]RpParent/[LLM]RpChild");
            Assert.That(child, Is.Not.Null);
            Assert.That(child.transform.parent.name, Is.EqualTo("[LLM]RpParent"));
        }

        [Test]
        public void ReparentObject_ToSceneRoot()
        {
            AgentBridge.TestInvoke("object_create", "{\"name\":\"[LLM]RpRoot\"}");
            AgentBridge.TestInvoke("object_create",
                "{\"name\":\"[LLM]RpNested\",\"parent\":\"[LLM]RpRoot\"}");
            var resp = AgentBridge.TestInvoke("reparent_object",
                "{\"path\":\"[LLM]RpRoot/[LLM]RpNested\",\"parent\":\"\"}");

            Assert.That(resp?["status"]?.GetValue<string>(), Is.EqualTo("ok"));
            var child = SceneBridge.FindByPath("[LLM]RpNested");
            Assert.That(child, Is.Not.Null);
            Assert.That(child.transform.parent, Is.Null);
        }

        [Test]
        public void ReparentObject_KeepWorldPosition_False()
        {
            AgentBridge.TestInvoke("object_create", "{\"name\":\"[LLM]RpKwpParent\"}");
            AgentBridge.TestInvoke("set_transform",
                "{\"path\":\"[LLM]RpKwpParent\",\"position\":{\"x\":10,\"y\":0,\"z\":0}}");
            AgentBridge.TestInvoke("object_create", "{\"name\":\"[LLM]RpKwpChild\"}");
            AgentBridge.TestInvoke("set_transform",
                "{\"path\":\"[LLM]RpKwpChild\",\"position\":{\"x\":5,\"y\":0,\"z\":0}}");

            AgentBridge.TestInvoke("reparent_object",
                "{\"path\":\"[LLM]RpKwpChild\",\"parent\":\"[LLM]RpKwpParent\",\"keep_world_position\":false}");

            var child = SceneBridge.FindByPath("[LLM]RpKwpParent/[LLM]RpKwpChild");
            Assert.That(child, Is.Not.Null);
            // keep_world_position=false: local position should remain the pre-reparent local value (5,0,0)
            Assert.That(child.transform.localPosition.x, Is.EqualTo(5f).Within(0.001f));
        }

        [Test]
        public void ReparentObject_ParentNotFound_ReturnsError()
        {
            AgentBridge.TestInvoke("object_create", "{\"name\":\"[LLM]RpOrphan\"}");
            var resp = AgentBridge.TestInvoke("reparent_object",
                "{\"path\":\"[LLM]RpOrphan\",\"parent\":\"DoesNotExist\"}");
            Assert.That(resp?["status"]?.GetValue<string>(), Is.EqualTo("error"));
        }
    }
}
