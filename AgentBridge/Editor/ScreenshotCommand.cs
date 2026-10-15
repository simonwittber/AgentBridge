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
        public string    Description => "Capture the current view to a PNG under Temp/agent/screenshots/. Returns the absolute file path, width and height once the file has been written. Fails if play mode is paused.";
        public ArgSpec[] Args        => new[]
        {
            new ArgSpec("max_size", "int", "0", "Downscale so the longest edge is at most this many pixels"),
        };

        private const string ScreenshotDir = "Temp/agent/screenshots";

        public JsonObject Execute(string uid, string requestJson)
        {
            var p = JsonUtility.FromJson<Params>(requestJson);

            // The output path is always chosen here, because Unity refuses to write outside certain folders.
            if (!string.IsNullOrEmpty(p.path))
                return Error(uid, "The 'path' argument was removed. The screenshot path is generated and returned in the response.");

            var path = UniqueScreenshotPath(uid);

            if (!Application.isPlaying)
                return CaptureEditMode(uid, path, p.max_size);

            // The capture waits for the end of a frame, which never comes while play mode is paused.
            if (EditorApplication.isPaused)
                return Error(uid, "Cannot capture a screenshot while play mode is paused");

            var go = new GameObject("__AgentScreenshot");
            Object.DontDestroyOnLoad(go);
            var helper = go.AddComponent<CaptureHelper>();
            helper.Uid            = uid;
            helper.PendingPath    = path;
            helper.PendingMaxSize = p.max_size;
            return null; // async: CaptureHelper calls Respond() once the file is written
        }

        private static string UniqueScreenshotPath(string uid) =>
            Path.GetFullPath(Path.Combine(ScreenshotDir, $"{System.DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{uid}.png"));

        private static JsonObject Error(string uid, string message)
        {
            var err = AgentBridge.MakeResponse(uid, "screenshot", "error");
            err["message"] = message;
            return err;
        }

        private static JsonObject Saved(string uid, string path, int width, int height)
        {
            var resp = AgentBridge.MakeResponse(uid, "screenshot", "ok");
            resp["path"]   = path;
            resp["width"]  = width;
            resp["height"] = height;
            return resp;
        }

        // Edit mode: no player loop, so render the scene view camera synchronously.
        private static JsonObject CaptureEditMode(string uid, string outPath, int maxSize)
        {
            Camera cam = null;
            if (SceneView.lastActiveSceneView != null)
                cam = SceneView.lastActiveSceneView.camera;
            if (cam == null)
                cam = Object.FindAnyObjectByType<Camera>();

            if (cam == null)
                return Error(uid, "No camera found in scene");

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

            return Saved(uid, fullPath, w, h);
        }

        // Play mode: coroutine waits for end of frame then uses ScreenCapture API.
        private class CaptureHelper : MonoBehaviour
        {
            public string Uid;
            public string PendingPath;
            public int    PendingMaxSize;

            private bool _responded;

            private IEnumerator Start()
            {
                yield return new WaitForEndOfFrame();

                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                if (tex == null)
                {
                    Finish(Error(Uid, "CaptureScreenshotAsTexture returned null"));
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
                JsonObject resp;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                    File.WriteAllBytes(fullPath, tex.EncodeToPNG());
                    resp = Saved(Uid, fullPath, w, h);
                }
                catch (System.Exception ex)
                {
                    resp = Error(Uid, ex.Message);
                }
                finally
                {
                    Destroy(tex);
                }

                Finish(resp);
            }

            private void Finish(JsonObject resp)
            {
                if (_responded) return;
                _responded = true;
                AgentBridge.Respond(resp);
                Destroy(gameObject);
            }

            // Leaving play mode destroys this object before the capture runs, so reply instead of leaving the bridge busy.
            private void OnDestroy()
            {
                if (_responded) return;
                _responded = true;
                AgentBridge.Respond(Error(Uid, "Play mode ended before the screenshot was captured"));
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
