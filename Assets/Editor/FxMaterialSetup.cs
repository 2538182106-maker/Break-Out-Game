using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 生成特效用的材质资源（放到 Resources 下），并清理 Always Included Shaders。
///
/// 背景：
///   1. CombatFx 原来用 Shader.Find("Universal Render Pipeline/Unlit")，
///      编辑器能查到，但打包时 shader 没被任何资源引用 → 被剥离 →
///      运行时返回 null → 材质变粉色，特效还因异常不销毁。
///      → 解决：把材质做成 Assets/Resources 下的资源，shader 会跟着打包。
///   2. 曾经把 URP/Lit 加进 Always Included Shaders 当"双保险"，
///      结果 URP/Lit 有 132 万变体，直接导致构建失败。
///      → 解决：自动把它们从 Always Included 里移除（Resources 已能保证打包）。
/// </summary>
[InitializeOnLoad]
public static class FxMaterialSetup
{
    private const string Key = "FxMaterialSetup.Done.v2";
    private const string ResFolder = "Assets/Resources";

    static FxMaterialSetup()
    {
        EditorApplication.delayCall += Run;
    }

    private static void Run()
    {
        if (SessionState.GetBool(Key, false)) return;
        SessionState.SetBool(Key, true);

        if (!AssetDatabase.IsValidFolder(ResFolder))
        {
            AssetDatabase.CreateFolder("Assets", "Resources");
        }

        CreateGlow();
        CreateSolid();
        RemoveFromAlwaysIncluded();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[FxMaterialSetup] 材质已就绪（Assets/Resources），Always Included 已清理");
    }

    private static void CreateGlow()
    {
        string path = ResFolder + "/FX_Glow.mat";
        if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            Debug.LogWarning("[FxMaterialSetup] 找不到 URP/Unlit，跳过 FX_Glow");
            return;
        }

        Material mat = new Material(shader) { name = "FX_Glow" };
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 2f);
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.One);
        mat.SetFloat("_ZWrite", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.renderQueue = (int)RenderQueue.Transparent;
        mat.SetColor("_BaseColor", Color.white);

        AssetDatabase.CreateAsset(mat, path);
    }

    private static void CreateSolid()
    {
        string path = ResFolder + "/FX_Solid.mat";
        if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            Debug.LogWarning("[FxMaterialSetup] 找不到 URP/Lit，跳过 FX_Solid");
            return;
        }

        Material mat = new Material(shader) { name = "FX_Solid" };
        mat.EnableKeyword("_EMISSION");
        mat.SetFloat("_Smoothness", 0.2f);
        mat.SetColor("_EmissionColor", Color.white);

        AssetDatabase.CreateAsset(mat, path);
    }

    /// <summary>把 URP/Lit、URP/Unlit 从 Always Included Shaders 里移除（变体爆炸的元凶）。</summary>
    private static void RemoveFromAlwaysIncluded()
    {
        Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        if (unlit == null && lit == null) return;

        SerializedObject graphics = new SerializedObject(
            AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/GraphicsSettings.asset"));
        SerializedProperty list = graphics.FindProperty("m_AlwaysIncludedShaders");
        if (list == null) return;

        int removed = 0;
        for (int i = list.arraySize - 1; i >= 0; i--)
        {
            Object entry = list.GetArrayElementAtIndex(i).objectReferenceValue;
            if (entry == null) continue;
            if (entry != unlit && entry != lit) continue;

            int before = list.arraySize;
            list.DeleteArrayElementAtIndex(i);
            // 第一次删除有时只是把引用置空，需要再删一次才真正移除
            if (list.arraySize == before) list.DeleteArrayElementAtIndex(i);
            removed++;
        }

        if (removed > 0)
        {
            graphics.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }

        Debug.Log($"[FxMaterialSetup] 从 Always Included Shaders 移除 {removed} 个 URP shader");
    }
}
