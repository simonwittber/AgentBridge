using System.Text.Json.Nodes;
using UnityEditor;
using UnityEngine;

namespace LLMDevTools
{
    [InitializeOnLoad]
    public static class TagLayerCommands
    {
        static TagLayerCommands()
        {
            AgentBridge.Register(new TagsLayersCmd());
            AgentBridge.Register(new TagAddCmd());
            AgentBridge.Register(new LayerAddCmd());
        }

        private sealed class TagsLayersCmd : IAgentCommand
        {
            public string    Cmd         => "tags_layers";
            public string    Description => "";
            public ArgSpec[] Args        => new ArgSpec[0];

            public JsonObject Execute(string uid, string requestJson)
            {
                var tagsArray = new JsonArray();
                foreach (var tag in UnityEditorInternal.InternalEditorUtility.tags)
                    tagsArray.Add(tag);

                var layersArray = new JsonArray();
                for (int i = 0; i < 32; i++)
                {
                    string name = LayerMask.LayerToName(i);
                    if (string.IsNullOrEmpty(name)) continue;
                    layersArray.Add(new JsonObject { ["index"] = i, ["name"] = name });
                }

                var resp = AgentBridge.MakeResponse(uid, Cmd, "ok");
                resp["tags"]   = tagsArray;
                resp["layers"] = layersArray;
                return resp;
            }
        }

        private sealed class TagAddCmd : IAgentCommand
        {
            public string    Cmd         => "tag_add";
            public string    Description => "";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("name", "string", "", ""),
            };

            public JsonObject Execute(string uid, string requestJson)
            {
                var p = JsonUtility.FromJson<Params>(requestJson);
                if (string.IsNullOrEmpty(p.name))
                {
                    var err = AgentBridge.MakeResponse(uid, Cmd, "error");
                    err["message"] = "name is required";
                    return err;
                }

                var tagManager = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("ProjectSettings/TagManager.asset");
                var so         = new SerializedObject(tagManager);
                var tagsProp   = so.FindProperty("tags");

                for (int i = 0; i < tagsProp.arraySize; i++)
                {
                    if (tagsProp.GetArrayElementAtIndex(i).stringValue == p.name)
                    {
                        var err = AgentBridge.MakeResponse(uid, Cmd, "error");
                        err["message"] = "Tag already exists";
                        return err;
                    }
                }

                tagsProp.InsertArrayElementAtIndex(tagsProp.arraySize);
                tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1).stringValue = p.name;
                so.ApplyModifiedProperties();

                var resp = AgentBridge.MakeResponse(uid, Cmd, "ok");
                resp["name"] = p.name;
                return resp;
            }

            [System.Serializable] private class Params { public string name = ""; }
        }

        private sealed class LayerAddCmd : IAgentCommand
        {
            public string    Cmd         => "layer_add";
            public string    Description => "";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("name", "string", "", ""),
            };

            public JsonObject Execute(string uid, string requestJson)
            {
                var p = JsonUtility.FromJson<Params>(requestJson);
                if (string.IsNullOrEmpty(p.name))
                {
                    var err = AgentBridge.MakeResponse(uid, Cmd, "error");
                    err["message"] = "name is required";
                    return err;
                }

                var tagManager = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("ProjectSettings/TagManager.asset");
                var so         = new SerializedObject(tagManager);
                var layersProp = so.FindProperty("layers");

                for (int i = 8; i < layersProp.arraySize; i++)
                {
                    var elem = layersProp.GetArrayElementAtIndex(i);
                    if (string.IsNullOrEmpty(elem.stringValue))
                    {
                        elem.stringValue = p.name;
                        so.ApplyModifiedProperties();
                        var resp = AgentBridge.MakeResponse(uid, Cmd, "ok");
                        resp["index"] = i;
                        resp["name"]  = p.name;
                        return resp;
                    }
                }

                var errResp = AgentBridge.MakeResponse(uid, Cmd, "error");
                errResp["message"] = "No available layer slots (max 32 user layers)";
                return errResp;
            }

            [System.Serializable] private class Params { public string name = ""; }
        }
    }
}
