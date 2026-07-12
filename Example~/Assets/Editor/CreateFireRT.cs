using UnityEngine;
using UnityEditor;

public static class CreateFireRT
{
    [MenuItem("Tools/Fire Effect/Create Output RenderTexture")]
    public static void Create()
    {
        var rt = new RenderTexture(320, 240, 0, RenderTextureFormat.ARGB32);
        rt.name = "FireOutput";
        AssetDatabase.CreateAsset(rt, "Assets/FireOutput.renderTexture");
        AssetDatabase.SaveAssets();
        Debug.Log("Created Assets/FireOutput.renderTexture");
    }
}
