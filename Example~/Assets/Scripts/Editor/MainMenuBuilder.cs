using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public static class MainMenuBuilder
{
    [MenuItem("Tools/Build Main Menu")]
    static void Build()
    {
        var roots = new List<GameObject>();
        SceneManager.GetActiveScene().GetRootGameObjects(roots);
        foreach (var go in roots)
            Object.DestroyImmediate(go);

        // Camera
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        camGo.AddComponent<AudioListener>();

        BuildStarfield();

        var esGo = new GameObject("EventSystem");
        esGo.AddComponent<EventSystem>();
        // StandaloneInputModule omitted: in UGUI 2.0 (Unity 6) AddComponent<StandaloneInputModule>()
        // triggers BaseInputModule.OnEnable before EventSystem finishes initialising, causing NPE.
        // EventSystem selects an available module automatically at runtime.

        var canvasGo = new GameObject("Canvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGo.AddComponent<GraphicRaycaster>();
        var ctrl = canvasGo.AddComponent<MainMenuController>();
        var audio = canvasGo.AddComponent<AudioSource>();
        audio.playOnAwake = false;

        var panel = CreatePanel(canvasGo);
        CreateTitle(panel);
        ctrl.startButton     = CreateButton(panel, "StartButton",     "START");
        ctrl.highScoreButton = CreateButton(panel, "HighScoreButton", "HIGH SCORE");
        ctrl.settingsButton  = CreateButton(panel, "SettingsButton",  "SETTINGS");
        ctrl.quitButton      = CreateButton(panel, "QuitButton",      "QUIT");

        EditorUtility.SetDirty(ctrl);

        if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            AssetDatabase.CreateFolder("Assets", "Scenes");

        EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), "Assets/Scenes/MainMenu.unity");
        Debug.Log("[MainMenuBuilder] Scene built and saved.");
    }

    static Texture2D GetOrCreateStarTexture()
    {
        const string assetPath = "Assets/StarTex.png";
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        if (existing != null) return existing;

        int size = 32;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float c = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(c, c));
                float a = Mathf.Clamp01(1f - d / c);
                a = a * a;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();

        File.WriteAllBytes(Path.Combine(Application.dataPath, "StarTex.png"), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(assetPath);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
    }

    static void BuildStarfield()
    {
        var go = new GameObject("Starfield");

        var ps = go.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.loop            = true;
        main.prewarm         = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles    = 400;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(5f, 8f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(0f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.04f, 0.14f);
        main.startColor      = new ParticleSystem.MinMaxGradient(Color.white);

        var emission = ps.emission;
        emission.rateOverTime = new ParticleSystem.MinMaxCurve(35f);

        var shape = ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale     = new Vector3(26f, 0.1f, 0.1f);
        shape.position  = new Vector3(0f, 8f, 0f);

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space   = ParticleSystemSimulationSpace.World;
        vel.x       = new ParticleSystem.MinMaxCurve(0f);
        vel.y       = new ParticleSystem.MinMaxCurve(-2f);
        vel.z       = new ParticleSystem.MinMaxCurve(0f);

        var rend = go.GetComponent<ParticleSystemRenderer>();
        rend.renderMode   = ParticleSystemRenderMode.Billboard;
        rend.sortingOrder = -10;

        var shader = Shader.Find("Legacy Shaders/Particles/Additive");
        if (shader == null) shader = Shader.Find("Particles/Additive");
        if (shader != null)
        {
            var mat = new Material(shader);
            var tex = GetOrCreateStarTexture();
            if (tex != null) mat.mainTexture = tex;
            rend.material = mat;
        }
    }

    static GameObject CreatePanel(GameObject canvas)
    {
        var go = new GameObject("MenuPanel");
        go.transform.SetParent(canvas.transform, false);

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0.5f, 0.5f);
        rt.anchorMax        = new Vector2(0.5f, 0.5f);
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.sizeDelta        = new Vector2(360f, 460f);
        rt.anchoredPosition = Vector2.zero;

        var img = go.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0.12f, 0.82f);

        var vlg = go.AddComponent<VerticalLayoutGroup>();
        vlg.spacing              = 14f;
        vlg.childAlignment       = TextAnchor.MiddleCenter;
        vlg.childControlWidth    = true;
        vlg.childControlHeight   = false;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;
        vlg.padding = new RectOffset(28, 28, 28, 28);

        return go;
    }

    static void CreateTitle(GameObject panel)
    {
        var go = new GameObject("Title");
        go.transform.SetParent(panel.transform, false);

        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0f, 70f);

        var t = go.AddComponent<Text>();
        t.text      = "MAIN MENU";
        t.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize  = 42;
        t.fontStyle = FontStyle.Bold;
        t.alignment = TextAnchor.MiddleCenter;
        t.color     = Color.white;
    }

    static Button CreateButton(GameObject panel, string name, string label)
    {
        var go = new GameObject(name);
        go.transform.SetParent(panel.transform, false);

        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0f, 54f);

        var img = go.AddComponent<Image>();
        img.color = new Color(0.12f, 0.12f, 0.32f, 1f);

        var btn = go.AddComponent<Button>();
        var colors              = btn.colors;
        colors.highlightedColor = new Color(0.3f, 0.3f, 0.65f, 1f);
        colors.pressedColor     = new Color(0.05f, 0.05f, 0.18f, 1f);
        btn.colors = colors;

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(go.transform, false);

        var trt = textGo.AddComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero;
        trt.offsetMax = Vector2.zero;

        var t = textGo.AddComponent<Text>();
        t.text      = label;
        t.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize  = 26;
        t.fontStyle = FontStyle.Bold;
        t.alignment = TextAnchor.MiddleCenter;
        t.color     = Color.white;

        return btn;
    }
}
