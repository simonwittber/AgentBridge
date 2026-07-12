using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(FireEffect))]
public class FireEffectEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var fx = (FireEffect)target;
        EditorGUILayout.Space();
        if (fx.outputTexture == null)
        {
            if (GUILayout.Button("Create Output RenderTexture Asset"))
            {
                var rt = new RenderTexture(fx.fireWidth, fx.fireHeight, 0, RenderTextureFormat.ARGB32);
                rt.name = "FireOutput";
                AssetDatabase.CreateAsset(rt, "Assets/FireOutput.renderTexture");
                AssetDatabase.SaveAssets();
                Undo.RecordObject(fx, "Assign FireOutput");
                fx.outputTexture = rt;
                EditorUtility.SetDirty(fx);
            }
        }
    }
}
