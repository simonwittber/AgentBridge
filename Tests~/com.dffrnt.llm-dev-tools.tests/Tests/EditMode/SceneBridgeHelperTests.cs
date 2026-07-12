using System.Collections.Generic;
using System.Text.Json.Nodes;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LLMDevTools.Tests
{
    // Tests for SceneBridge's internal pure helpers — no bridge I/O needed.
    [TestFixture]
    public class SceneBridgeHelperTests
    {
        private readonly List<GameObject> _created = new();

        private GameObject Make(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent);
            _created.Add(go);
            return go;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _created)
                if (go != null) Object.DestroyImmediate(go);
            _created.Clear();
        }

        // ── FindByPath ────────────────────────────────────────────────────────

        [Test]
        public void FindByPath_Root_FindsObject()
        {
            var go = Make("[LLM]Root");
            Assert.That(SceneBridge.FindByPath("[LLM]Root"), Is.SameAs(go));
        }

        [Test]
        public void FindByPath_Nested_FindsChild()
        {
            var parent = Make("[LLM]Parent");
            var child  = Make("[LLM]Child", parent.transform);
            Assert.That(SceneBridge.FindByPath("[LLM]Parent/[LLM]Child"), Is.SameAs(child));
        }

        [Test]
        public void FindByPath_DeepNest_FindsGrandchild()
        {
            var a = Make("[LLM]A");
            var b = Make("[LLM]B", a.transform);
            var c = Make("[LLM]C", b.transform);
            Assert.That(SceneBridge.FindByPath("[LLM]A/[LLM]B/[LLM]C"), Is.SameAs(c));
        }

        [Test]
        public void FindByPath_Missing_ReturnsNull()
        {
            Assert.That(SceneBridge.FindByPath("[LLM]DoesNotExist_x9z"), Is.Null);
        }

        [Test]
        public void FindByPath_NullOrEmpty_ReturnsNull()
        {
            Assert.That(SceneBridge.FindByPath(null), Is.Null);
            Assert.That(SceneBridge.FindByPath(""),   Is.Null);
        }

        // ── GetPath ───────────────────────────────────────────────────────────

        [Test]
        public void GetPath_Root_ReturnsName()
        {
            var go = Make("[LLM]Root");
            Assert.That(SceneBridge.GetPath(go), Is.EqualTo("[LLM]Root"));
        }

        [Test]
        public void GetPath_Nested_ReturnsSlashDelimitedPath()
        {
            var parent = Make("[LLM]Parent");
            var child  = Make("[LLM]Child", parent.transform);
            Assert.That(SceneBridge.GetPath(child), Is.EqualTo("[LLM]Parent/[LLM]Child"));
        }

        [Test]
        public void GetPath_FindByPath_RoundTrip()
        {
            var parent = Make("[LLM]RT");
            var child  = Make("[LLM]RTChild", parent.transform);

            string path  = SceneBridge.GetPath(child);
            var    found = SceneBridge.FindByPath(path);

            Assert.That(found, Is.SameAs(child));
        }

        // ── GameObjectInfo ────────────────────────────────────────────────────

        [Test]
        public void GameObjectInfo_ContainsExpectedFields()
        {
            var go   = Make("[LLM]InfoTest");
            var info = SceneBridge.GameObjectInfo(go);

            Assert.That(info["name"]?.GetValue<string>(),  Is.EqualTo("[LLM]InfoTest"));
            Assert.That(info["path"]?.GetValue<string>(),  Is.EqualTo("[LLM]InfoTest"));
            Assert.That(info["active"]?.GetValue<bool>(),  Is.True);
            Assert.That(info["components"],                Is.Not.Null);
        }

        [Test]
        public void GameObjectInfo_Components_ContainsTransform()
        {
            var go    = Make("[LLM]InfoComp");
            var comps = SceneBridge.GameObjectInfo(go)["components"]?.AsArray();

            Assert.That(comps, Is.Not.Null);
            Assert.That(comps.Count, Is.GreaterThan(0));
            bool hasTransform = false;
            foreach (var c in comps) if (c?.GetValue<string>() == "Transform") { hasTransform = true; break; }
            Assert.That(hasTransform, Is.True);
        }

        // ── SpToJson ──────────────────────────────────────────────────────────

        [Test]
        public void SpToJson_Vector3_ReturnsXYZ()
        {
            var go = Make("[LLM]SpJson");
            go.transform.localPosition = new Vector3(1f, 2f, 3f);
            var so = new SerializedObject(go.transform);
            var sp = so.FindProperty("m_LocalPosition");
            Assume.That(sp, Is.Not.Null, "m_LocalPosition not found");

            var json = SceneBridge.SpToJson(sp);
            Assert.That(json,                                   Is.Not.Null);
            Assert.That((float)json["x"]!.GetValue<double>(),  Is.EqualTo(1f).Within(0.001f));
            Assert.That((float)json["y"]!.GetValue<double>(),  Is.EqualTo(2f).Within(0.001f));
            Assert.That((float)json["z"]!.GetValue<double>(),  Is.EqualTo(3f).Within(0.001f));
        }

        [Test]
        public void SpToJson_Bool_ReturnsBool()
        {
            var go  = Make("[LLM]SpBool");
            var cam = go.AddComponent<Camera>();
            var so  = new SerializedObject(cam);
            var sp  = so.FindProperty("m_Enabled");
            Assume.That(sp, Is.Not.Null, "m_Enabled not found on Camera");

            var json = SceneBridge.SpToJson(sp);
            Assert.That(json?.GetValue<bool>(), Is.True);
        }

        // ── JsonToSp ──────────────────────────────────────────────────────────

        [Test]
        public void JsonToSp_Vector3_SetsAllComponents()
        {
            var go = Make("[LLM]SpSet");
            var so = new SerializedObject(go.transform);
            var sp = so.FindProperty("m_LocalPosition");
            Assume.That(sp, Is.Not.Null, "m_LocalPosition not found");

            bool ok = SceneBridge.JsonToSp(sp, new JsonObject { ["x"] = 10f, ["y"] = 20f, ["z"] = 30f });
            so.ApplyModifiedProperties();

            Assert.That(ok, Is.True);
            Assert.That(go.transform.localPosition.x, Is.EqualTo(10f).Within(0.001f));
            Assert.That(go.transform.localPosition.y, Is.EqualTo(20f).Within(0.001f));
            Assert.That(go.transform.localPosition.z, Is.EqualTo(30f).Within(0.001f));
        }

        [Test]
        public void JsonToSp_Bool_SetsValue()
        {
            var go  = Make("[LLM]SpBoolSet");
            var cam = go.AddComponent<Camera>();
            var so  = new SerializedObject(cam);
            var sp  = so.FindProperty("m_Enabled");
            Assume.That(sp, Is.Not.Null, "m_Enabled not found on Camera");

            bool ok = SceneBridge.JsonToSp(sp, JsonValue.Create(false));
            so.ApplyModifiedProperties();

            Assert.That(ok, Is.True);
            Assert.That(cam.enabled, Is.False);
        }

        [Test]
        public void JsonToSp_SpToJson_RoundTrip_Vector3()
        {
            var go = Make("[LLM]RT3");
            go.transform.localPosition = new Vector3(5f, 6f, 7f);
            var so = new SerializedObject(go.transform);
            var sp = so.FindProperty("m_LocalPosition");
            Assume.That(sp, Is.Not.Null, "m_LocalPosition not found");

            var json = SceneBridge.SpToJson(sp);

            go.transform.localPosition = Vector3.zero;
            so.Update();
            sp = so.FindProperty("m_LocalPosition");

            SceneBridge.JsonToSp(sp, json);
            so.ApplyModifiedProperties();

            Assert.That(go.transform.localPosition.x, Is.EqualTo(5f).Within(0.001f));
            Assert.That(go.transform.localPosition.y, Is.EqualTo(6f).Within(0.001f));
            Assert.That(go.transform.localPosition.z, Is.EqualTo(7f).Within(0.001f));
        }

        // ── JsonToSp array write ──────────────────────────────────────────────

        [Test]
        public void JsonToSp_Array_SetsElements()
        {
            // MeshRenderer.m_Materials is a Generic array of ObjectReference.
            // We just test that we can resize it and that arraySize matches.
            var go  = Make("[LLM]ArrayWrite");
            var mf  = go.AddComponent<MeshFilter>();
            var mr  = go.AddComponent<MeshRenderer>();
            var so  = new SerializedObject(mr);
            var sp  = so.FindProperty("m_Materials");
            Assume.That(sp,         Is.Not.Null,  "m_Materials not found");
            Assume.That(sp.isArray, Is.True,       "m_Materials is not an array");

            // Write an empty array — just verifying arraySize is set to 0
            bool ok = SceneBridge.JsonToSp(sp, new JsonArray());
            so.ApplyModifiedProperties();

            Assert.That(ok,              Is.True, "JsonToSp returned false");
            Assert.That(mr.sharedMaterials.Length, Is.EqualTo(0));
        }

        [Test]
        public void JsonToSp_Array_SpToJson_RoundTrip_StringArray()
        {
            // Verify SpToJson → JsonToSp round-trip for an object-reference array.
            // A fresh MeshRenderer starts with 1 null-material slot; read it, write
            // it back, then read again and confirm the count is preserved.
            var go = Make("[LLM]ArrayRT");
            go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            var so = new SerializedObject(mr);
            var sp = so.FindProperty("m_Materials");
            Assume.That(sp?.isArray, Is.True, "m_Materials is not an array");

            // Read initial state — SpToJson returns {_items, _total} for non-empty arrays.
            var initial = SceneBridge.SpToJson(sp) as JsonObject;
            Assume.That(initial, Is.Not.Null, "SpToJson returned null for non-empty m_Materials");
            int initialCount = initial["_total"]?.GetValue<int>() ?? -1;

            // Write the same items back (round-trip).
            bool ok = SceneBridge.JsonToSp(sp, initial["_items"]?.AsArray() ?? new JsonArray());
            so.ApplyModifiedProperties();

            Assert.That(ok, Is.True);
            Assert.That(mr.sharedMaterials.Length, Is.EqualTo(initialCount));

            // Read back via the iterator path (same as component_get) to avoid
            // stale-property issues after ApplyModifiedProperties.
            var fields   = SceneBridge.SerializedObjectToJson(mr);
            var readBack = fields["m_Materials"]?.AsObject();
            Assert.That(readBack,                            Is.Not.Null);
            Assert.That(readBack["_total"]?.GetValue<int>(), Is.EqualTo(initialCount));
        }
    }
}
