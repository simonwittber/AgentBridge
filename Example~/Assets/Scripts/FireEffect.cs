using UnityEngine;

[RequireComponent(typeof(Camera))]
[ExecuteAlways]
public class FireEffect : MonoBehaviour
{
    [Header("Text")]
    public string fireText = "FIRE";
    public Font font;
    public int fontSize = 48;

    [Header("Palette")]
    public Gradient palette;

    [Header("Simulation")]
    [Range(0.1f, 10f)]
    public float cooling = 1.5f;

    [Header("Resolution")]
    public int fireWidth = 320;
    public int fireHeight = 240;

    [Header("Output")]
    public RenderTexture outputTexture;

    float[] heat;
    float[] textSeed;
    Texture2D fireTex;
    Color[] pixels;
    string cachedText;
    int cachedFontSize;
    Font resolvedFont;

    void OnEnable()
    {
        Application.runInBackground = true;
        Alloc();
    }

    void OnDisable() => Free();

    void Alloc()
    {
        heat = new float[fireWidth * fireHeight];
        textSeed = new float[fireWidth * fireHeight];
        pixels = new Color[fireWidth * fireHeight];
        Free();
        fireTex = new Texture2D(fireWidth, fireHeight, TextureFormat.RGB24, false);
        fireTex.filterMode = FilterMode.Bilinear;
        fireTex.wrapMode = TextureWrapMode.Clamp;
        EnsureDefaultPalette();
        cachedText = null;
        cachedFontSize = -1;
        resolvedFont = null;
    }

    void Free()
    {
        if (fireTex != null) { DestroyImmediate(fireTex); fireTex = null; }
    }

    void EnsureDefaultPalette()
    {
        if (palette != null && palette.colorKeys.Length > 0) return;
        palette = new Gradient();
        palette.SetKeys(
            new[]
            {
                new GradientColorKey(Color.black, 0f),
                new GradientColorKey(new Color(0.5f, 0f, 0f), 0.2f),
                new GradientColorKey(Color.red, 0.45f),
                new GradientColorKey(new Color(1f, 0.5f, 0f), 0.65f),
                new GradientColorKey(Color.yellow, 0.8f),
                new GradientColorKey(Color.white, 1f),
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f),
            }
        );
    }

    Font GetFont()
    {
        if (font != null) return font;
        if (resolvedFont == null)
            resolvedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return resolvedFont;
    }

    // GPU readback — works even if the texture is not CPU-readable
    Color[] ReadTexturePixels(Texture2D src)
    {
        try { return src.GetPixels(); }
        catch { }

        var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(src, rt);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tmp = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
        tmp.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
        tmp.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        var result = tmp.GetPixels();
        DestroyImmediate(tmp);
        return result;
    }

    void Update()
    {
        if (fireTex == null) Alloc();
        if (fireText != cachedText || fontSize != cachedFontSize) BuildTextSeed();
        Seed();
        Step();
        RenderToTexture();
        fireTex.Apply();
        if (outputTexture != null) Graphics.Blit(fireTex, outputTexture);
    }

    void BuildTextSeed()
    {
        System.Array.Clear(textSeed, 0, textSeed.Length);
        if (!TryRasterizeText()) FallbackSeed();
        cachedText = fireText;
        cachedFontSize = fontSize;
    }

    bool TryRasterizeText()
    {
        Font f = GetFont();
        if (f == null || string.IsNullOrEmpty(fireText)) return false;

        f.RequestCharactersInTexture(fireText, fontSize, FontStyle.Normal);
        var fontTex = f.material?.mainTexture as Texture2D;
        if (fontTex == null) return false;

        Color[] fp = ReadTexturePixels(fontTex);
        if (fp == null) return false;

        int fw = fontTex.width, fh = fontTex.height;

        int totalAdv = 0;
        foreach (char c in fireText)
        {
            CharacterInfo ci;
            if (f.GetCharacterInfo(c, out ci, fontSize)) totalAdv += ci.advance;
        }

        // Centre horizontally; place baseline at 20% from bottom
        int curX = (fireWidth - totalAdv) / 2;
        int baselineY = fireHeight / 5;
        bool any = false;

        foreach (char c in fireText)
        {
            CharacterInfo ci;
            if (!f.GetCharacterInfo(c, out ci, fontSize)) { curX += fontSize / 2; continue; }

            // Use Min/Max to handle UV orientations that may be flipped
            int u0 = Mathf.FloorToInt(Mathf.Min(ci.uvBottomLeft.x, ci.uvTopRight.x) * fw);
            int v0 = Mathf.FloorToInt(Mathf.Min(ci.uvBottomLeft.y, ci.uvTopRight.y) * fh);
            int u1 = Mathf.CeilToInt(Mathf.Max(ci.uvBottomLeft.x, ci.uvTopRight.x) * fw);
            int v1 = Mathf.CeilToInt(Mathf.Max(ci.uvBottomLeft.y, ci.uvTopRight.y) * fh);
            int gw = Mathf.Max(0, u1 - u0);
            int gh = Mathf.Max(0, v1 - v0);

            for (int dy = 0; dy < gh; dy++)
            {
                for (int dx = 0; dx < gw; dx++)
                {
                    int fu = u0 + dx, fv = v0 + dy;
                    if ((uint)fu >= (uint)fw || (uint)fv >= (uint)fh) continue;
                    float a = fp[fv * fw + fu].a;
                    if (a < 0.05f) continue;

                    int tx = curX + ci.minX + dx;
                    int ty = baselineY + ci.minY + dy;
                    if ((uint)tx < (uint)fireWidth && (uint)ty < (uint)fireHeight)
                    {
                        textSeed[ty * fireWidth + tx] = Mathf.Max(textSeed[ty * fireWidth + tx], a);
                        any = true;
                    }
                }
            }
            curX += ci.advance;
        }
        return any;
    }

    void FallbackSeed()
    {
        int row = fireHeight / 5;
        for (int x = 0; x < fireWidth; x++) textSeed[row * fireWidth + x] = 1f;
    }

    void Seed()
    {
        for (int i = 0; i < textSeed.Length; i++)
            if (textSeed[i] > 0.05f) heat[i] = 1f;
    }

    void Step()
    {
        float c = cooling * Mathf.Clamp(Time.deltaTime, 0.001f, 0.05f);
        // y=0=bottom, iterate top-down so y-1 reads are from the previous frame
        for (int y = fireHeight - 1; y >= 1; y--)
        {
            int row = y * fireWidth;
            int below = (y - 1) * fireWidth;
            for (int x = 0; x < fireWidth; x++)
            {
                if (textSeed[row + x] > 0.05f) continue;
                int xL = x > 0 ? x - 1 : 0;
                int xR = x < fireWidth - 1 ? x + 1 : fireWidth - 1;
                float avg = (heat[below + xL] + heat[below + x] + heat[below + xR] + heat[row + x]) * 0.25f;
                heat[row + x] = Mathf.Max(0f, avg - c);
            }
        }
    }

    void RenderToTexture()
    {
        var p = palette;
        for (int i = 0; i < heat.Length; i++) pixels[i] = p.Evaluate(heat[i]);
        fireTex.SetPixels(pixels);
    }

    void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        Graphics.Blit(fireTex != null ? (Texture)fireTex : src, dest);
    }
}
