using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

/// <summary>
/// 把 Assets/Icon 下的图设为应用图标（Windows / Android）。
///
/// 注意：Unity 每个平台的图标槽位数量固定（Standalone 8 个、Android 6 个），
/// 打包时是按尺寸从槽位里挑图，所以只改「Default Icon」而其它槽位还留着旧图，
/// 打出来仍是旧图 —— 这里两个菜单都是一次性填满所有槽位。
///
/// - 菜单「应用游戏图标」    ：用带星野背景那套（icon_1024/512/256/128）
/// - 菜单「应用透明图标」    ：用透明那套（icon_transparent_*）
/// - 菜单「清除游戏图标」    ：清空所有槽位
/// </summary>
public static class AppIconSetup
{
    private static readonly string[] WithBackground =
    {
        "Assets/Icon/icon_1024.png",
        "Assets/Icon/icon_512.png",
        "Assets/Icon/icon_256.png",
        "Assets/Icon/icon_128.png",
    };

    private static readonly string[] Transparent =
    {
        "Assets/Icon/icon_transparent_1024.png",
        "Assets/Icon/icon_transparent_512.png",
        "Assets/Icon/icon_transparent_256.png",
    };

    [MenuItem("Tools/菜单美术/图标 - 应用透明版")]
    private static void ApplyTransparent()
    {
        int applied = SetAll(Transparent, "透明");
        if (applied > 0) AssetDatabase.SaveAssets();
        Debug.Log($"[AppIconSetup] 已应用透明图标（{applied} 个平台），请重新打包");
    }

    [MenuItem("Tools/菜单美术/图标 - 应用带背景版")]
    private static void ApplyWithBackground()
    {
        int applied = SetAll(WithBackground, "带背景");
        if (applied > 0) AssetDatabase.SaveAssets();
        Debug.Log($"[AppIconSetup] 已应用带背景图标（{applied} 个平台），请重新打包");
    }

    [MenuItem("Tools/菜单美术/图标 - 清除（恢复默认）")]
    private static void ClearAll()
    {
        int cleared = 0;
        cleared += ClearFor(NamedBuildTarget.Standalone);
        cleared += ClearFor(NamedBuildTarget.Android);

        AssetDatabase.SaveAssets();
        Debug.Log($"[AppIconSetup] 已清除 {cleared} 个平台的图标");
    }

    // ------------------------------------------------------------------
    private static int SetAll(string[] paths, string label)
    {
        Texture2D[] textures = Load(paths);
        if (textures.Length == 0)
        {
            Debug.LogWarning($"[AppIconSetup] 找不到{label}图标文件");
            return 0;
        }

        int ok = 0;
        ok += SetFor(NamedBuildTarget.Standalone, textures);
        ok += SetFor(NamedBuildTarget.Android, textures);
        return ok;
    }

    private static int SetFor(NamedBuildTarget target, Texture2D[] textures)
    {
        try
        {
            int[] sizes = PlayerSettings.GetIconSizes(target, IconKind.Application);
            if (sizes == null || sizes.Length == 0) return 0;

            var icons = new Texture2D[sizes.Length];
            for (int i = 0; i < sizes.Length; i++) icons[i] = Closest(textures, sizes[i]);

            PlayerSettings.SetIcons(target, icons, IconKind.Application);
            return 1;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[AppIconSetup] {target} 设置失败：{e.Message}");
            return 0;
        }
    }

    private static int ClearFor(NamedBuildTarget target)
    {
        try
        {
            int[] sizes = PlayerSettings.GetIconSizes(target, IconKind.Application);
            if (sizes == null || sizes.Length == 0) return 0;

            PlayerSettings.SetIcons(target, new Texture2D[sizes.Length], IconKind.Application);
            return 1;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[AppIconSetup] {target} 清除失败：{e.Message}");
            return 0;
        }
    }

    private static Texture2D Closest(Texture2D[] textures, int size)
    {
        Texture2D best = textures[0];
        int bestDelta = Mathf.Abs(best.width - size);

        foreach (Texture2D tex in textures)
        {
            int delta = Mathf.Abs(tex.width - size);
            if (delta < bestDelta)
            {
                best = tex;
                bestDelta = delta;
            }
        }
        return best;
    }

    private static Texture2D[] Load(string[] paths)
    {
        var list = new System.Collections.Generic.List<Texture2D>();
        foreach (string path in paths)
        {
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null) list.Add(tex);
        }
        return list.ToArray();
    }
}
