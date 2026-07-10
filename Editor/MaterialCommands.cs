using System;
using System.Text.Json.Nodes;
using UnityEditor;
using UnityEngine;

namespace LLMDevTools
{
    [InitializeOnLoad]
    internal static class MaterialCommands
    {
        static MaterialCommands()
        {
            AgentBridge.Register(new MaterialGetCmd());
            AgentBridge.Register(new MaterialSetCmd());
        }

        private sealed class MaterialGetCmd : IAgentCommand
        {
            public string    Cmd         => "material_get";
            public string    Description => "Get all shader properties of a material asset.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("path", "string", "", "Asset path to the .mat file, e.g. Assets/Materials/MyMat.mat"),
            };

            public JsonObject Execute(string uid, string requestJson)
            {
                var p   = JsonUtility.FromJson<Params>(requestJson);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(p.path);
                if (mat == null)
                {
                    var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                    e["message"] = $"Material not found: {p.path}";
                    return e;
                }
                var shader = mat.shader;
                var props  = new JsonObject();
                int count  = shader.GetPropertyCount();
                for (int i = 0; i < count; i++)
                {
                    string name     = shader.GetPropertyName(i);
                    var    propType = shader.GetPropertyType(i);
                    JsonNode val    = propType switch
                    {
                        UnityEngine.Rendering.ShaderPropertyType.Float or
                        UnityEngine.Rendering.ShaderPropertyType.Range   => (double)mat.GetFloat(name),
                        UnityEngine.Rendering.ShaderPropertyType.Color   => SceneBridge.ColorToJson(mat.GetColor(name)),
                        UnityEngine.Rendering.ShaderPropertyType.Vector  => SceneBridge.VectorToJson(mat.GetVector(name)),
                        UnityEngine.Rendering.ShaderPropertyType.Texture => SceneBridge.MatTextureToJson(mat.GetTexture(name)),
                        _                                                 => null,
                    };
                    props[name] = new JsonObject { ["type"] = propType.ToString(), ["value"] = val };
                }
                var resp = AgentBridge.MakeResponse(uid, Cmd, "ok");
                resp["shader"]     = shader.name;
                resp["properties"] = props;
                return resp;
            }

            [Serializable] private class Params { public string path = ""; }
        }

        private sealed class MaterialSetCmd : IAgentCommand
        {
            public string    Cmd         => "material_set";
            public string    Description => "Set a shader property on a material asset and save it.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("path",     "string", "", "Asset path to the .mat file"),
                new ArgSpec("property", "string", "", "Shader property name, e.g. _Color, _Metallic"),
                new ArgSpec("value",    "any",    "", "float | {r,g,b,a} | {x,y,z,w} | {\"path\":\"Assets/...\"} for Texture | null"),
            };

            public JsonObject Execute(string uid, string requestJson)
            {
                if (!SceneBridge.TryParseRequest(requestJson, out var req))
                {
                    var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                    e["message"] = "Invalid request JSON";
                    return e;
                }

                string   assetPath = req["path"]?    .GetValue<string>() ?? "";
                string   property  = req["property"]?.GetValue<string>() ?? "";
                JsonNode value     = req["value"];

                var mat = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
                if (mat == null)
                {
                    var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                    e["message"] = $"Material not found: {assetPath}";
                    return e;
                }
                var shader  = mat.shader;
                int propIdx = shader.FindPropertyIndex(property);
                if (propIdx < 0)
                {
                    var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                    e["message"] = $"Property not found: {property}";
                    return e;
                }
                var propType = shader.GetPropertyType(propIdx);
                Undo.RecordObject(mat, $"Set {property}");
                try
                {
                    switch (propType)
                    {
                        case UnityEngine.Rendering.ShaderPropertyType.Float:
                        case UnityEngine.Rendering.ShaderPropertyType.Range:
                            mat.SetFloat(property, SceneBridge.F(value)); break;

                        case UnityEngine.Rendering.ShaderPropertyType.Color:
                        {
                            var o = value.AsObject();
                            mat.SetColor(property, new Color(SceneBridge.F(o["r"]), SceneBridge.F(o["g"]), SceneBridge.F(o["b"]), o["a"] != null ? SceneBridge.F(o["a"]) : 1f));
                            break;
                        }

                        case UnityEngine.Rendering.ShaderPropertyType.Vector:
                        {
                            var o = value.AsObject();
                            mat.SetVector(property, new Vector4(SceneBridge.F(o["x"]), SceneBridge.F(o["y"]), SceneBridge.F(o["z"]), o["w"] != null ? SceneBridge.F(o["w"]) : 0f));
                            break;
                        }

                        case UnityEngine.Rendering.ShaderPropertyType.Texture:
                        {
                            if (value == null || value.GetValueKind() == System.Text.Json.JsonValueKind.Null)
                            { mat.SetTexture(property, null); break; }
                            string texPath = value.AsObject()["path"]?.GetValue<string>() ?? "";
                            var    tex     = AssetDatabase.LoadAssetAtPath<Texture>(texPath);
                            if (tex == null)
                            {
                                var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                                e["message"] = $"Texture not found: {texPath}";
                                return e;
                            }
                            mat.SetTexture(property, tex);
                            break;
                        }

                        default:
                        {
                            var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                            e["message"] = $"Unsupported property type: {propType}";
                            return e;
                        }
                    }
                }
                catch (Exception ex)
                {
                    var e = AgentBridge.MakeResponse(uid, Cmd, "error");
                    e["message"] = ex.Message;
                    return e;
                }
                EditorUtility.SetDirty(mat);
                AssetDatabase.SaveAssetIfDirty(mat);
                return AgentBridge.MakeResponse(uid, Cmd, "ok");
            }
        }
    }
}
