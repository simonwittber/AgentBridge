using System.Text.Json.Nodes;
using NUnit.Framework;
using UnityEngine;

namespace LLMDevTools.Tests
{
    // Integration tests using AgentBridge.TestInvoke — synchronous, no file I/O.
    // Validates that command handlers correctly translate requests to Unity state changes.
    [TestFixture]
    public class SceneBridgeCommandTests
    {
        [TearDown]
        public void TearDown()
        {
            // Destroy all test objects without switching scenes (which would trigger save dialog).
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include))
                if (go != null && go.name.StartsWith("[LLM]"))
                    Object.DestroyImmediate(go);
        }

        // ── scene_info ────────────────────────────────────────────────────────

        [Test]
        public void SceneInfo_ReturnsOk()
        {
            var resp = AgentBridge.TestInvoke("scene_info");
            Assert.That(resp,                               Is.Not.Null);
            Assert.That(resp["status"]?.GetValue<string>(), Is.EqualTo("ok"));
            Assert.That(resp["scene_name"],                 Is.Not.Null);
        }

        // ── hierarchy ─────────────────────────────────────────────────────────

        [Test]
        public void Hierarchy_ReturnsOkWithObjectsArray()
        {
            var resp = AgentBridge.TestInvoke("hierarchy", "{\"depth\":1}");
            Assert.That(resp,                               Is.Not.Null);
            Assert.That(resp["status"]?.GetValue<string>(), Is.EqualTo("ok"));
            Assert.That(resp["objects"]?.AsArray(),         Is.Not.Null);
        }

        // ── object_create ─────────────────────────────────────────────────────

        [Test]
        public void ObjectCreate_RootObject_ReturnsPath()
        {
            var resp = AgentBridge.TestInvoke("object_create", "{\"name\":\"[LLM]CmdRoot\"}");
            Assert.That(resp,                               Is.Not.Null);
            Assert.That(resp["status"]?.GetValue<string>(), Is.EqualTo("ok"));
            Assert.That(resp["path"]?.GetValue<string>(),   Is.EqualTo("[LLM]CmdRoot"));
        }

        [Test]
        public void ObjectCreate_WithParent_ReturnsNestedPath()
        {
            var r1 = AgentBridge.TestInvoke("object_create", "{\"name\":\"[LLM]CmdParent\"}");
            Assume.That(r1?["status"]?.GetValue<string>(), Is.EqualTo("ok"));

            var resp = AgentBridge.TestInvoke("object_create",
                "{\"name\":\"[LLM]CmdChild\",\"parent\":\"[LLM]CmdParent\"}");
            Assert.That(resp,                               Is.Not.Null);
            Assert.That(resp["status"]?.GetValue<string>(), Is.EqualTo("ok"));
            Assert.That(resp["path"]?.GetValue<string>(),   Is.EqualTo("[LLM]CmdParent/[LLM]CmdChild"));
        }

        // ── object_find ───────────────────────────────────────────────────────

        [Test]
        public void ObjectFind_CreatedObject_ReturnsInfo()
        {
            AgentBridge.TestInvoke("object_create", "{\"name\":\"[LLM]FindMe\"}");

            var resp = AgentBridge.TestInvoke("object_find", "{\"path\":\"[LLM]FindMe\"}");
            Assert.That(resp,                                        Is.Not.Null);
            Assert.That(resp["status"]?.GetValue<string>(),          Is.EqualTo("ok"));
            Assert.That(resp["object"]?["name"]?.GetValue<string>(), Is.EqualTo("[LLM]FindMe"));
        }

        [Test]
        public void ObjectFind_Missing_ReturnsError()
        {
            var resp = AgentBridge.TestInvoke("object_find", "{\"path\":\"[LLM]DoesNotExist_x9z\"}");
            Assert.That(resp,                               Is.Not.Null);
            Assert.That(resp["status"]?.GetValue<string>(), Is.EqualTo("error"));
        }

        // ── object_delete ─────────────────────────────────────────────────────

        [Test]
        public void ObjectDelete_CreatedObject_Succeeds()
        {
            AgentBridge.TestInvoke("object_create", "{\"name\":\"[LLM]DeleteMe\"}");

            var resp = AgentBridge.TestInvoke("object_delete", "{\"path\":\"[LLM]DeleteMe\"}");
            Assert.That(resp,                               Is.Not.Null);
            Assert.That(resp["status"]?.GetValue<string>(), Is.EqualTo("ok"));

            var findResp = AgentBridge.TestInvoke("object_find", "{\"path\":\"[LLM]DeleteMe\"}");
            Assert.That(findResp?["status"]?.GetValue<string>(), Is.EqualTo("error"));
        }

        // ── object_active ─────────────────────────────────────────────────────

        [Test]
        public void ObjectActive_Deactivate_ObjectBecomesInactive()
        {
            AgentBridge.TestInvoke("object_create", "{\"name\":\"[LLM]Deactivate\"}");

            var resp = AgentBridge.TestInvoke("object_active", "{\"path\":\"[LLM]Deactivate\",\"active\":false}");
            Assert.That(resp,                               Is.Not.Null);
            Assert.That(resp["status"]?.GetValue<string>(), Is.EqualTo("ok"));

            var findResp = AgentBridge.TestInvoke("object_find", "{\"path\":\"[LLM]Deactivate\"}");
            Assert.That(findResp?["object"]?["active_self"]?.GetValue<bool>(), Is.False);
        }

        // ── object_rename ─────────────────────────────────────────────────────

        [Test]
        public void ObjectRename_ReturnsNewPath()
        {
            AgentBridge.TestInvoke("object_create", "{\"name\":\"[LLM]OldName\"}");

            var resp = AgentBridge.TestInvoke("object_rename",
                "{\"path\":\"[LLM]OldName\",\"name\":\"[LLM]NewName\"}");
            Assert.That(resp,                               Is.Not.Null);
            Assert.That(resp["status"]?.GetValue<string>(), Is.EqualTo("ok"));
            Assert.That(resp["path"]?.GetValue<string>(),   Is.EqualTo("[LLM]NewName"));
        }

        // ── component_get ─────────────────────────────────────────────────────

        [Test]
        public void ComponentGet_Transform_ContainsLocalPosition()
        {
            AgentBridge.TestInvoke("object_create", "{\"name\":\"[LLM]CompGet\"}");

            var resp = AgentBridge.TestInvoke("component_get",
                "{\"path\":\"[LLM]CompGet\",\"component\":\"Transform\"}");
            Assert.That(resp,                               Is.Not.Null);
            Assert.That(resp["status"]?.GetValue<string>(), Is.EqualTo("ok"));
            Assert.That(resp["fields"]?.AsObject()?["m_LocalPosition"], Is.Not.Null);
        }

        // ── component_set ─────────────────────────────────────────────────────

        [Test]
        public void ComponentSet_TransformPosition_UpdatesValue()
        {
            AgentBridge.TestInvoke("object_create", "{\"name\":\"[LLM]CompSet\"}");

            var setResp = AgentBridge.TestInvoke("component_set",
                "{\"path\":\"[LLM]CompSet\",\"component\":\"Transform\"," +
                "\"field\":\"m_LocalPosition\",\"value\":{\"x\":5.0,\"y\":10.0,\"z\":15.0}}");
            Assert.That(setResp,                               Is.Not.Null);
            Assert.That(setResp["status"]?.GetValue<string>(), Is.EqualTo("ok"));

            var go = SceneBridge.FindByPath("[LLM]CompSet");
            Assert.That(go,                           Is.Not.Null);
            Assert.That(go.transform.localPosition.x, Is.EqualTo(5f).Within(0.01f));
            Assert.That(go.transform.localPosition.y, Is.EqualTo(10f).Within(0.01f));
            Assert.That(go.transform.localPosition.z, Is.EqualTo(15f).Within(0.01f));
        }

        // ── component_add ─────────────────────────────────────────────────────

        [Test]
        public void ComponentAdd_AudioSource_AppearsOnGameObject()
        {
            AgentBridge.TestInvoke("object_create", "{\"name\":\"[LLM]CompAdd\"}");

            var resp = AgentBridge.TestInvoke("component_add",
                "{\"path\":\"[LLM]CompAdd\",\"type\":\"AudioSource\"}");
            Assert.That(resp,                               Is.Not.Null);
            Assert.That(resp["status"]?.GetValue<string>(), Is.EqualTo("ok"));

            var go = SceneBridge.FindByPath("[LLM]CompAdd");
            Assert.That(go,                             Is.Not.Null);
            Assert.That(go.GetComponent<AudioSource>(), Is.Not.Null);
        }

        // ── objects_find ──────────────────────────────────────────────────────

        [Test]
        public void ObjectsFind_ByComponent_ReturnsMatches()
        {
            AgentBridge.TestInvoke("object_create", "{\"name\":\"[LLM]Cam1\"}");
            AgentBridge.TestInvoke("object_create", "{\"name\":\"[LLM]Cam2\"}");
            AgentBridge.TestInvoke("component_add", "{\"path\":\"[LLM]Cam1\",\"type\":\"Camera\"}");
            AgentBridge.TestInvoke("component_add", "{\"path\":\"[LLM]Cam2\",\"type\":\"Camera\"}");

            var resp = AgentBridge.TestInvoke("objects_find", "{\"component\":\"Camera\"}");
            Assert.That(resp,                               Is.Not.Null);
            Assert.That(resp["status"]?.GetValue<string>(), Is.EqualTo("ok"));

            var objects = resp["objects"]?.AsArray();
            Assert.That(objects, Is.Not.Null);

            bool foundCam1 = false, foundCam2 = false;
            foreach (var obj in objects)
            {
                string name = obj?["name"]?.GetValue<string>();
                if (name == "[LLM]Cam1") foundCam1 = true;
                if (name == "[LLM]Cam2") foundCam2 = true;
            }
            Assert.That(foundCam1, Is.True, "[LLM]Cam1 not in results");
            Assert.That(foundCam2, Is.True, "[LLM]Cam2 not in results");
        }

        [Test]
        public void ObjectsFind_WithFields_IncludesFieldData()
        {
            AgentBridge.TestInvoke("object_create", "{\"name\":\"[LLM]CamF\"}");
            AgentBridge.TestInvoke("component_add", "{\"path\":\"[LLM]CamF\",\"type\":\"Camera\"}");

            var resp = AgentBridge.TestInvoke("objects_find", "{\"component\":\"Camera\",\"fields\":true}");
            Assert.That(resp,                               Is.Not.Null);
            Assert.That(resp["status"]?.GetValue<string>(), Is.EqualTo("ok"));

            var objects = resp["objects"]?.AsArray();
            Assert.That(objects, Is.Not.Null);

            System.Text.Json.Nodes.JsonObject match = null;
            foreach (var obj in objects)
                if (obj?["name"]?.GetValue<string>() == "[LLM]CamF") { match = obj.AsObject(); break; }

            Assert.That(match,          Is.Not.Null, "[LLM]CamF not found");
            Assert.That(match["fields"], Is.Not.Null, "fields not included");
            Assert.That(match["fields"]?.AsObject()?["field of view"], Is.Not.Null, "\"field of view\" missing");
        }

        [Test]
        public void ObjectsFind_ExcludesInactive_ByDefault()
        {
            AgentBridge.TestInvoke("object_create", "{\"name\":\"[LLM]Inactive\"}");
            AgentBridge.TestInvoke("component_add",  "{\"path\":\"[LLM]Inactive\",\"type\":\"Camera\"}");
            AgentBridge.TestInvoke("object_active",  "{\"path\":\"[LLM]Inactive\",\"active\":false}");

            var resp = AgentBridge.TestInvoke("objects_find", "{\"component\":\"Camera\"}");
            var objects = resp?["objects"]?.AsArray();
            bool found = false;
            if (objects != null)
                foreach (var obj in objects)
                    if (obj?["name"]?.GetValue<string>() == "[LLM]Inactive") { found = true; break; }
            Assert.That(found, Is.False, "inactive object should not appear without include_inactive");

            var respInactive = AgentBridge.TestInvoke("objects_find",
                "{\"component\":\"Camera\",\"include_inactive\":true}");
            var objectsInactive = respInactive?["objects"]?.AsArray();
            bool foundInactive = false;
            if (objectsInactive != null)
                foreach (var obj in objectsInactive)
                    if (obj?["name"]?.GetValue<string>() == "[LLM]Inactive") { foundInactive = true; break; }
            Assert.That(foundInactive, Is.True, "inactive object should appear with include_inactive:true");
        }

        [Test]
        public void ObjectsFind_UnknownComponent_ReturnsError()
        {
            var resp = AgentBridge.TestInvoke("objects_find", "{\"component\":\"NoSuchComponent_x9z\"}");
            Assert.That(resp,                               Is.Not.Null);
            Assert.That(resp["status"]?.GetValue<string>(), Is.EqualTo("error"));
        }
    }
}
