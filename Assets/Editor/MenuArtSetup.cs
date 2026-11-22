using UnityEditor;
using UnityEngine;

/// <summary>
/// 把 Assets/Resources 下的菜单美术资源设成 Sprite，供运行时 Resources.Load&lt;Sprite&gt; 使用。
/// 只跑一次（SessionState 记录）。
/// </summary>
[InitializeOnLoad]
public static class MenuArtSetup
{
    private const string Key = "MenuArtSetup.Done.v2";

    private static readonly string[] Targets =
    {
        "Assets/Resources/MenuTitle.png",
        "Assets/Resources/MenuBackground.png",
    };

    static MenuArtSetup()
    {
        EditorApplication.delayCall += Run;
    }

    [MenuItem("Tools/菜单美术/重新导入为 Sprite")]
    private static void ForceRun()
    {
        SessionState.SetBool(Key, false);
        Run();
        Debug.Log("[MenuArtSetup] 手动重新导入完成");
    }

    private static void Run()
    {
        if (SessionState.GetBool(Key, false)) return;
        SessionState.SetBool(Key, true);

        int changed = 0;
        foreach (string path in Targets)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) continue;

            bool dirty = false;
            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                dirty = true;
            }
            if (importer.spriteImportMode != SpriteImportMode.Single)
            {
                importer.spriteImportMode = SpriteImportMode.Single;
                dirty = true;
            }
            if (importer.mipmapEnabled)
            {
                importer.mipmapEnabled = false;
                dirty = true;
            }
            if (!importer.alphaIsTransparency)
            {
                importer.alphaIsTransparency = true;
                dirty = true;
            }
            if (importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                dirty = true;
            }
            if (importer.maxTextureSize < 2048)
            {
                importer.maxTextureSize = 2048;
                dirty = true;
            }

            if (dirty)
            {
                importer.SaveAndReimport();
                changed++;
            }
        }

        if (changed > 0)
        {
            AssetDatabase.Refresh();
            Debug.Log($"[MenuArtSetup] 已把 {changed} 张菜单图设为 Sprite");
        }
    }
}
