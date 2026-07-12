using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LLMDevTools
{
    public static class SceneBridge
    {
        internal static GameObject FindByPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string[] parts = path.Split('/');

            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            GameObject[] roots = prefabStage != null
                ? new[] { prefabStage.prefabContentsRoot }
                : SceneManager.GetActiveScene().GetRootGameObjects();

            GameObject root = Array.Find(roots, r => r.name == parts[0]);
            if (root == null) return null;
            Transform t = root.transform;
            for (int i = 1; i < parts.Length; i++)
            {
                t = t.Find(parts[i]);
                if (t == null) return null;
            }
            return t.gameObject;
        }

        internal static string GetPath(GameObject go)
        {
            var parts = new Stack<string>();
            for (var t = go.transform; t != null; t = t.parent)
                parts.Push(t.name);
            return string.Join("/", parts);
        }

        internal static Component FindComponent(GameObject go, string typeName)
        {
            foreach (var c in go.GetComponents<Component>())
                if (c != null && (c.GetType().Name == typeName || c.GetType().FullName == typeName))
                    return c;
            return null;
        }

        internal static JsonObject GameObjectInfo(GameObject go)
        {
            var comps = new JsonArray();
            foreach (var c in go.GetComponents<Component>())
                if (c != null) comps.Add(c.GetType().Name);

            return new JsonObject
            {
                ["name"]        = go.name,
                ["path"]        = GetPath(go),
                ["active"]      = go.activeInHierarchy,
                ["active_self"] = go.activeSelf,
                ["layer"]       = go.layer,
                ["tag"]         = go.tag,
                ["components"]  = comps,
            };
        }

        internal static JsonObject BuildTree(GameObject go, int depth)
        {
            var node = GameObjectInfo(go);
            var children = new JsonArray();
            if (depth > 0)
                foreach (Transform child in go.transform)
                    children.Add(BuildTree(child.gameObject, depth - 1));
            node["children"] = children;
            return node;
        }

        internal static JsonObject SerializedObjectToJson(UnityEngine.Object target)
        {
            var result = new JsonObject();
            var so     = new SerializedObject(target);
            var sp     = so.GetIterator();
            if (sp.NextVisible(true))
                while (sp.NextVisible(false))
                    result[sp.name] = SpToJson(sp);
            return result;
        }

        internal static bool TryParseRequest(string json, out JsonNode node)
        {
            try { node = JsonNode.Parse(json); return true; }
            catch { node = null; return false; }
        }

        internal static void MarkDirty(GameObject go)
        {
            EditorUtility.SetDirty(go);
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null)
                EditorSceneManager.MarkSceneDirty(prefabStage.scene);
            else if (go.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(go.scene);
        }

        internal static void SilentlyClearDirtyScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (!s.isDirty) continue;
                if (!string.IsNullOrEmpty(s.path))
                {
                    EditorSceneManager.SaveScene(s);
                }
                else
                {
                    string tmp = $"Assets/~discard_{i}.unity";
                    EditorSceneManager.SaveScene(s, tmp, false);
                    AssetDatabase.DeleteAsset(tmp);
                }
            }
        }

        // ── SerializedProperty ↔ JSON ─────────────────────────────────────────

        internal static JsonNode SpToJson(SerializedProperty sp, int depth = 3)
        {
            try
            {
                switch (sp.propertyType)
                {
                    case SerializedPropertyType.Boolean:   return sp.boolValue;
                    case SerializedPropertyType.Integer:
                    case SerializedPropertyType.LayerMask: return sp.intValue;
                    case SerializedPropertyType.Float:     return (double)sp.floatValue;
                    case SerializedPropertyType.String:    return sp.stringValue;

                    case SerializedPropertyType.Enum:
                        return sp.enumValueIndex >= 0 && sp.enumValueIndex < sp.enumDisplayNames.Length
                            ? (JsonNode)sp.enumDisplayNames[sp.enumValueIndex]
                            : sp.enumValueIndex;

                    case SerializedPropertyType.Color:
                        return ColorToJson(sp.colorValue);

                    case SerializedPropertyType.Vector2:
                    { var v = sp.vector2Value; return new JsonObject { ["x"] = (double)v.x, ["y"] = (double)v.y }; }

                    case SerializedPropertyType.Vector3:
                    { var v = sp.vector3Value; return new JsonObject { ["x"] = (double)v.x, ["y"] = (double)v.y, ["z"] = (double)v.z }; }

                    case SerializedPropertyType.Vector4:
                        return VectorToJson(sp.vector4Value);

                    case SerializedPropertyType.Quaternion:
                    { var q = sp.quaternionValue; return new JsonObject { ["x"] = (double)q.x, ["y"] = (double)q.y, ["z"] = (double)q.z, ["w"] = (double)q.w }; }

                    case SerializedPropertyType.Vector2Int:
                    { var v = sp.vector2IntValue; return new JsonObject { ["x"] = v.x, ["y"] = v.y }; }

                    case SerializedPropertyType.Vector3Int:
                    { var v = sp.vector3IntValue; return new JsonObject { ["x"] = v.x, ["y"] = v.y, ["z"] = v.z }; }

                    case SerializedPropertyType.Rect:
                    { var r = sp.rectValue; return new JsonObject { ["x"] = (double)r.x, ["y"] = (double)r.y, ["width"] = (double)r.width, ["height"] = (double)r.height }; }

                    case SerializedPropertyType.Bounds:
                    {
                        var b = sp.boundsValue;
                        return new JsonObject
                        {
                            ["center"] = new JsonObject { ["x"] = (double)b.center.x, ["y"] = (double)b.center.y, ["z"] = (double)b.center.z },
                            ["size"]   = new JsonObject { ["x"] = (double)b.size.x,   ["y"] = (double)b.size.y,   ["z"] = (double)b.size.z   },
                        };
                    }

                    case SerializedPropertyType.ObjectReference:
                    {
                        var obj = sp.objectReferenceValue;
                        if (obj == null) return null;
                        string assetPath = AssetDatabase.GetAssetPath(obj);
                        return new JsonObject
                        {
                            ["name"] = obj.name,
                            ["type"] = obj.GetType().Name,
                            ["path"] = string.IsNullOrEmpty(assetPath) ? null : (JsonNode)assetPath,
                        };
                    }

                    case SerializedPropertyType.Generic when sp.isArray:
                    {
                        int total = sp.arraySize;
                        int count = Math.Min(total, 200);
                        var arr = new JsonArray();
                        for (int i = 0; i < count; i++)
                            arr.Add(SpToJson(sp.GetArrayElementAtIndex(i), depth));
                        var wrapped = new JsonObject { ["_items"] = arr, ["_total"] = total };
                        if (total > 200) wrapped["_truncated"] = true;
                        return wrapped;
                    }

                    case SerializedPropertyType.Generic:
                    {
                        if (depth <= 0) return "[...]";
                        var obj = new JsonObject();
                        var copy = sp.Copy();
                        var end  = sp.GetEndProperty();
                        if (copy.NextVisible(true))
                            while (!SerializedProperty.EqualContents(copy, end))
                            {
                                obj[copy.name] = SpToJson(copy, depth - 1);
                                if (!copy.NextVisible(false)) break;
                            }
                        return obj;
                    }

                    default: return null;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SceneBridge] SpToJson({sp.name}, {sp.propertyType}): {ex.Message}");
                return null;
            }
        }

        internal static bool JsonToSp(SerializedProperty sp, JsonNode value)
        {
            try
            {
                switch (sp.propertyType)
                {
                    case SerializedPropertyType.Boolean:
                        sp.boolValue = value.GetValue<bool>(); return true;

                    case SerializedPropertyType.Integer:
                    case SerializedPropertyType.LayerMask:
                        sp.intValue = value.GetValue<int>(); return true;

                    case SerializedPropertyType.Float:
                        sp.floatValue = (float)value.GetValue<double>(); return true;

                    case SerializedPropertyType.String:
                        sp.stringValue = value.GetValue<string>(); return true;

                    case SerializedPropertyType.Enum:
                        if (value is JsonValue jv && jv.TryGetValue<string>(out var enumStr))
                        {
                            int idx = Array.IndexOf(sp.enumDisplayNames, enumStr);
                            if (idx < 0) idx = Array.IndexOf(sp.enumNames, enumStr);
                            if (idx >= 0) { sp.enumValueIndex = idx; return true; }
                            return false; // unknown enum string
                        }
                        {
                            var intIdx = value.GetValue<int>();
                            if (intIdx < 0 || intIdx >= sp.enumNames.Length) return false;
                            sp.enumValueIndex = intIdx;
                            return true;
                        }

                    case SerializedPropertyType.Color:
                    {
                        var o = value.AsObject();
                        sp.colorValue = new Color(F(o["r"]), F(o["g"]), F(o["b"]), o["a"] != null ? F(o["a"]) : 1f);
                        return true;
                    }

                    case SerializedPropertyType.Vector2:
                    { var o = value.AsObject(); sp.vector2Value = new Vector2(F(o["x"]), F(o["y"])); return true; }

                    case SerializedPropertyType.Vector3:
                    { var o = value.AsObject(); sp.vector3Value = new Vector3(F(o["x"]), F(o["y"]), F(o["z"])); return true; }

                    case SerializedPropertyType.Vector4:
                    { var o = value.AsObject(); sp.vector4Value = new Vector4(F(o["x"]), F(o["y"]), F(o["z"]), F(o["w"])); return true; }

                    case SerializedPropertyType.Quaternion:
                    { var o = value.AsObject(); sp.quaternionValue = new Quaternion(F(o["x"]), F(o["y"]), F(o["z"]), F(o["w"])); return true; }

                    case SerializedPropertyType.Vector2Int:
                    { var o = value.AsObject(); sp.vector2IntValue = new Vector2Int(o["x"]?.GetValue<int>() ?? 0, o["y"]?.GetValue<int>() ?? 0); return true; }

                    case SerializedPropertyType.Vector3Int:
                    { var o = value.AsObject(); sp.vector3IntValue = new Vector3Int(o["x"]?.GetValue<int>() ?? 0, o["y"]?.GetValue<int>() ?? 0, o["z"]?.GetValue<int>() ?? 0); return true; }

                    case SerializedPropertyType.ObjectReference:
                    {
                        if (value == null || value.GetValueKind() == System.Text.Json.JsonValueKind.Null)
                        { sp.objectReferenceValue = null; return true; }

                        var o = value.AsObject();
                        if (o.TryGetPropertyValue("path", out var pathNode) && pathNode != null)
                        {
                            string assetPath = pathNode.GetValue<string>();
                            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                            if (asset == null) return false;
                            sp.objectReferenceValue = asset;
                            return true;
                        }
                        if (o.TryGetPropertyValue("scene", out var sceneNode) && sceneNode != null)
                        {
                            var go = FindByPath(sceneNode.GetValue<string>());
                            if (go == null) return false;
                            sp.objectReferenceValue = go;
                            return true;
                        }
                        return false;
                    }

                    case SerializedPropertyType.Generic when sp.isArray:
                    {
                        if (value is not JsonArray arr) return false;
                        sp.arraySize = arr.Count;
                        for (int i = 0; i < arr.Count; i++)
                            if (!JsonToSp(sp.GetArrayElementAtIndex(i), arr[i]))
                                return false;
                        return true;
                    }

                    case SerializedPropertyType.Generic:
                    {
                        if (value is not JsonObject obj) return false;
                        var copy = sp.Copy();
                        var end  = sp.GetEndProperty();
                        if (!copy.NextVisible(true)) return true;
                        while (!SerializedProperty.EqualContents(copy, end))
                        {
                            if (obj.TryGetPropertyValue(copy.name, out var fieldVal) && fieldVal != null)
                                if (!JsonToSp(copy, fieldVal))
                                    Debug.LogWarning($"[SceneBridge] JsonToSp: failed to set field '{copy.name}' ({copy.propertyType})");
                            if (!copy.NextVisible(false)) break;
                        }
                        return true;
                    }

                    default: return false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SceneBridge] JsonToSp({sp.name}, {sp.propertyType}): {ex.Message}");
                return false;
            }
        }

        internal static float F(JsonNode n)
        {
            if (n is not JsonValue jv) return 0f;
            if (jv.TryGetValue<double>(out var d)) return (float)d;
            if (jv.TryGetValue<float>(out var f))  return f;
            if (jv.TryGetValue<int>(out var i))    return i;
            return 0f;
        }

        // ── Type resolution ───────────────────────────────────────────────────

        private static Dictionary<string, Type> _typeCache;
        private static HashSet<string>          _ambiguousNames;

        internal static Type ResolveType(string name)
        {
            var t = Type.GetType(name);
            if (t != null) return t;

            if (_typeCache == null)
                BuildTypeCache();

            if (_ambiguousNames!.Contains(name))
                return null;

            return _typeCache!.TryGetValue(name, out var match) ? match : null;
        }

        internal static bool IsAmbiguousTypeName(string name)
        {
            if (_typeCache == null) BuildTypeCache();
            return _ambiguousNames!.Contains(name);
        }

        private static void BuildTypeCache()
        {
            _typeCache      = new Dictionary<string, Type>(StringComparer.Ordinal);
            _ambiguousNames = new HashSet<string>(StringComparer.Ordinal);
            var shortNameSeen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var tp in TypeCache.GetTypesDerivedFrom<object>())
            {
                if (tp.FullName != null && !_typeCache.ContainsKey(tp.FullName))
                    _typeCache[tp.FullName] = tp;
                if (tp.AssemblyQualifiedName != null && !_typeCache.ContainsKey(tp.AssemblyQualifiedName))
                    _typeCache[tp.AssemblyQualifiedName] = tp;
                // Use a separate seen-set so types without a namespace (FullName==Name) don't
                // falsely mark themselves as ambiguous via the FullName insertion above.
                if (!shortNameSeen.Add(tp.Name))
                    _ambiguousNames.Add(tp.Name);
                else if (!_typeCache.ContainsKey(tp.Name))
                    _typeCache[tp.Name] = tp;
            }
        }

        // ── Value helpers ─────────────────────────────────────────────────────

        internal static JsonNode ColorToJson(Color c)    => new JsonObject { ["r"] = (double)c.r, ["g"] = (double)c.g, ["b"] = (double)c.b, ["a"] = (double)c.a };
        internal static JsonNode VectorToJson(Vector4 v) => new JsonObject { ["x"] = (double)v.x, ["y"] = (double)v.y, ["z"] = (double)v.z, ["w"] = (double)v.w };
        internal static JsonNode MatTextureToJson(Texture t)
        {
            if (t == null) return null;
            string path = AssetDatabase.GetAssetPath(t);
            return new JsonObject { ["name"] = t.name, ["path"] = string.IsNullOrEmpty(path) ? null : (JsonNode)path };
        }
    }
}
