using System.IO;
using System.Text.Json.Nodes;
using UnityEditor;
using UnityEngine;

namespace LLMDevTools
{
    [InitializeOnLoad]
    public static class ScreenshotCommand
    {
        static ScreenshotCommand()
        {
            AgentBridge.Register(new ScreenshotCmd());
        }

        private sealed class ScreenshotCmd : IAgentCommand
        {
            public string    Cmd         => "screenshot";
            public string    Description => "Render the scene view (or main camera) to a PNG. Returns the file path — read the file to view the image.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("path",     "string", "", "Defaults to Temp/agent/screenshot.png"),
                new ArgSpec("width",    "int",    "0", "0 = scene view or 1920"),
                new ArgSpec("height",   "int",    "0", "0 = scene view or 1080"),
                new ArgSpec("max_size", "int",    "0", "Downscale so longest edge is at most this many pixels"),
            };

            public JsonObject Execute(string uid, string requestJson)
            {
                var p = JsonUtility.FromJson<Params>(requestJson);

                Camera cam = Application.isPlaying ? Camera.main : null;
                var sv = SceneView.lastActiveSceneView;
                if (cam == null) cam = sv?.camera;
                if (cam == null) cam = Camera.main;
                if (cam == null) cam = UnityEngine.Object.FindAnyObjectByType<Camera>();
                if (cam == null)
                {
                    var err = AgentBridge.MakeResponse(uid, Cmd, "error");
                    err["message"] = "No camera available";
                    return err;
                }

                int w = p.width  > 0 ? p.width  : (sv != null ? (int)sv.position.width  : 1920);
                int h = p.height > 0 ? p.height : (sv != null ? (int)sv.position.height : 1080);
                if (w <= 0) w = 1920;
                if (h <= 0) h = 1080;

                string savePath = string.IsNullOrEmpty(p.path) ? "Temp/agent/screenshot.png" : p.path;
                if (!PathUtils.IsWritable(savePath))
                {
                    var err = AgentBridge.MakeResponse(uid, Cmd, "error");
                    err["message"] = $"Path '{savePath}' is outside allowed write locations (Assets/ or Temp/)";
                    return err;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(savePath)));

                var rt         = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
                var prevTarget = cam.targetTexture;
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = prevTarget;

                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                UnityEngine.Object.DestroyImmediate(rt);

                if (p.max_size > 0 && (w > p.max_size || h > p.max_size))
                {
                    float scale  = (float)p.max_size / Mathf.Max(w, h);
                    int   newW   = Mathf.RoundToInt(w * scale);
                    int   newH   = Mathf.RoundToInt(h * scale);
                    var   srt    = new RenderTexture(newW, newH, 0, RenderTextureFormat.ARGB32);
                    Graphics.Blit(tex, srt);
                    RenderTexture.active = srt;
                    var scaledTex = new Texture2D(newW, newH, TextureFormat.RGB24, false);
                    scaledTex.ReadPixels(new Rect(0, 0, newW, newH), 0, 0);
                    scaledTex.Apply();
                    RenderTexture.active = null;
                    UnityEngine.Object.DestroyImmediate(srt);
                    UnityEngine.Object.DestroyImmediate(tex);
                    tex = scaledTex;
                    w   = newW;
                    h   = newH;
                }

                try
                {
                    File.WriteAllBytes(savePath, tex.EncodeToPNG());
                }
                finally { UnityEngine.Object.DestroyImmediate(tex); }

                var resp = AgentBridge.MakeResponse(uid, Cmd, "ok");
                resp["path"]   = Path.GetFullPath(savePath);
                resp["width"]  = w;
                resp["height"] = h;
                return resp;
            }

            [System.Serializable]
            private class Params
            {
                public string path     = "";
                public int    width    = 0;
                public int    height   = 0;
                public int    max_size = 0;
            }
        }
    }
}
