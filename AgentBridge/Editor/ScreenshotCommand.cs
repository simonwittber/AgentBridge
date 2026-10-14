using System.Collections;
using System.IO;
using System.Text.Json.Nodes;
using UnityEditor;
using UnityEngine;

namespace LLMDevTools
{
    internal sealed class ScreenshotCmd : IAgentCommand
    {
        public string    Cmd         => "screenshot";
        public string    Description => "Capture the current view to a PNG under Temp/agent/screenshots/. Returns the absolute file path immediately and fires a screenshot_ready notification when the file is written.";
        public ArgSpec[] Args        => new[]
        {
            new ArgSpec("max_size", "int", "0", "Downscale so the longest edge is at most this many pixels"),
        };

        private const string ScreenshotDir = "Temp/agent/screenshots";

        private string _pendingPath;
        private int    _pendingMaxSize;
        private bool   _busy;

        public JsonObject Execute(string uid, string requestJson)
        {
            var p = JsonUtility.FromJson<Params>(requestJson);

            if (_busy)
            {
                var err = AgentBridge.MakeResponse(uid, Cmd, "error");
                err["message"] = "Screenshot capture already in progress";
                return err;
            }

            // The output path is always chosen here, because Unity refuses to write outside certain folders.
            if (!string.IsNullOrEmpty(p.path))
            {
                var err = AgentBridge.MakeResponse(uid, Cmd, "error");
                err["message"] = "The 'path' argument was removed. The screenshot path is generated and returned in the response.";
                return err;
            }

            _pendingPath    = UniqueScreenshotPath(uid);
            _pendingMaxSize = p.max_size;

            if (Application.isPlaying)
            {
                _busy = true;
                var go = new GameObject("__AgentScreenshot");
                Object.DontDestroyOnLoad(go);
                var helper = go.AddComponent<CaptureHelper>();
                helper.PendingPath    = _pendingPath;
                helper.PendingMaxSize = _pendingMaxSize;
                helper.OnComplete     = () => _busy = false;
            }
            else
            {
                CaptureEditMode(_pendingPath, _pendingMaxSize);
            }

            var resp = AgentBridge.MakeResponse(uid, Cmd, "ok");
            resp["path"] = _pendingPath;
            return resp;
        }

        private static string UniqueScreenshotPath(string uid) =>
            Path.GetFullPath(Path.Combine(ScreenshotDir, $"{System.DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{uid}.png"));

        // Edit mode: no player loop, so render the scene view camera synchronously.
        private static void CaptureEditMode(string outPath, int maxSize)
        {
            Camera cam = null;
            if (SceneView.lastActiveSceneView != null)
                cam = SceneView.lastActiveSceneView.camera;
            if (cam == null)
                cam = Object.FindAnyObjectByType<Camera>();

            if (cam == null)
            {
                AgentBridge.Notify("screenshot_ready", new JsonObject { ["error"] = "No camera found in scene" });
                return;
            }

            int w = Mathf.Max(1, cam.pixelWidth);
            int h = Mathf.Max(1, cam.pixelHeight);

            if (maxSize > 0 && (w > maxSize || h > maxSize))
            {
                float scale = (float)maxSize / Mathf.Max(w, h);
                w = Mathf.Max(1, Mathf.RoundToInt(w * scale));
                h = Mathf.Max(1, Mathf.RoundToInt(h * scale));
            }

            var rt      = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            var prevTgt = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = prevTgt;

            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            Object.DestroyImmediate(rt);

            var fullPath = Path.GetFullPath(outPath);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                File.WriteAllBytes(fullPath, tex.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(tex);
            }

            AgentBridge.Notify("screenshot_ready", new JsonObject
            {
                ["path"]   = fullPath,
                ["width"]  = w,
                ["height"] = h,
            });
        }

        // Play mode: coroutine waits for end of frame then uses ScreenCapture API.
        private class CaptureHelper : MonoBehaviour
        {
            public string         PendingPath;
            public int            PendingMaxSize;
            public System.Action  OnComplete;

            private IEnumerator Start()
            {
                yield return new WaitForEndOfFrame();

                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                if (tex == null)
                {
                    AgentBridge.Notify("screenshot_ready", new JsonObject { ["error"] = "CaptureScreenshotAsTexture returned null" });
                    Done();
                    yield break;
                }

                int w = tex.width;
                int h = tex.height;

                if (PendingMaxSize > 0 && (w > PendingMaxSize || h > PendingMaxSize))
                {
                    float scale     = (float)PendingMaxSize / Mathf.Max(w, h);
                    int   newW      = Mathf.Max(1, Mathf.RoundToInt(w * scale));
                    int   newH      = Mathf.Max(1, Mathf.RoundToInt(h * scale));
                    var   srt       = new RenderTexture(newW, newH, 0, RenderTextureFormat.ARGB32);
                    Graphics.Blit(tex, srt);
                    RenderTexture.active = srt;
                    var scaled = new Texture2D(newW, newH, TextureFormat.RGB24, false);
                    scaled.ReadPixels(new Rect(0, 0, newW, newH), 0, 0);
                    scaled.Apply();
                    RenderTexture.active = null;
                    Destroy(srt);
                    Destroy(tex);
                    tex = scaled;
                    w   = newW;
                    h   = newH;
                }

                var fullPath = Path.GetFullPath(PendingPath);
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                    File.WriteAllBytes(fullPath, tex.EncodeToPNG());
                }
                finally
                {
                    Destroy(tex);
                }

                AgentBridge.Notify("screenshot_ready", new JsonObject
                {
                    ["path"]   = fullPath,
                    ["width"]  = w,
                    ["height"] = h,
                });

                Done();
            }

            private void Done()
            {
                OnComplete?.Invoke();
                Destroy(gameObject);
            }
        }

        [System.Serializable]
        private class Params
        {
            public string path     = "";
            public int    max_size = 0;
        }
    }
}
