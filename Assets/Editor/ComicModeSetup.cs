// =====================================================================================
// ComicModeSetup.cs —— 编辑器一键切换：美漫描边模式 / 霓虹发光模式
//
// 菜单：
//   Tools → Break Out → 切换到美漫描边模式
//   Tools → Break Out → 还原霓虹发光模式
//
// 做的事：
//   1. 用 BreakOut/ComicToon 生成 6 个漫画材质
//   2. 把场景里玩家 / 砖块 / 死亡线 / 围墙的材质换掉（子弹预制体也换）
//   3. 把 ComicInkFeature 挂到 URP 渲染器上并启用，同时打开 URP 的 Depth Texture
//   4. 把 Bloom 压到很低（漫画不需要发光溢出，反而会糊掉墨线）
//   5. 保存场景与资产
//
// 反复点也安全；「还原」会把霓虹材质换回去并关掉 Feature。
// =====================================================================================
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class ComicModeSetup
{
    private const string ComicMatFolder = "Assets/Materials/Comic";
    private const string InkMaterialPath = "Assets/Materials/Comic/ComicInk.mat";
    private const string BulletPrefabPath = "Assets/Prefab/Bullet/Sphere.prefab";
    private const string ProfilePath = "Assets/Settings/SampleSceneProfile.asset";
    private const string NeonMatFolder = "Assets/Materials";

    // 漫画配色：底色 / 阴影色（乘算） / 边缘光
    private static readonly Color InkColor = new Color(0.06f, 0.05f, 0.09f, 1f);

    // ---------------------------------------------------------------------------------
    // 菜单入口
    // ---------------------------------------------------------------------------------
    [MenuItem("Tools/Break Out/霓虹 + 描边：开启", false, 10)]
    public static void EnableNeonOutline()
    {
        if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("请先退出 Play 模式", "开关描边需要在编辑状态下执行。", "知道了");
            return;
        }

        try
        {
            Shader inkShader = Shader.Find("BreakOut/ComicInk");
            if (inkShader == null) throw new System.Exception("找不到 BreakOut/ComicInk，检查 Shader 是否编译报错");

            EnsureFolder(ComicMatFolder);

            Material inkMat = AssetDatabase.LoadAssetAtPath<Material>(InkMaterialPath);
            if (inkMat == null)
            {
                inkMat = new Material(inkShader);
                AssetDatabase.CreateAsset(inkMat, InkMaterialPath);
            }
            else
            {
                inkMat.shader = inkShader;
            }

            // 只要描边：把印刷效果全部关掉（色阶/网点/纸纹 = 0）
            inkMat.SetColor("_InkColor", InkColor);
            inkMat.SetFloat("_EdgeStrength", 1f);
            inkMat.SetFloat("_Thickness", 2.4f);
            inkMat.SetFloat("_DepthThreshold", 0.25f);
            inkMat.SetFloat("_LumaThreshold", 0.08f);
            inkMat.SetFloat("_PosterizeStrength", 0f);
            inkMat.SetFloat("_BlackLift", 0f);
            inkMat.SetFloat("_HalftoneStrength", 0f);
            inkMat.SetFloat("_PaperStrength", 0f);
            EditorUtility.SetDirty(inkMat);

            // 材质换回霓虹（如果之前开过漫画模式）
            RestoreNeonMaterials();

            // 深度图 + Feature
            EnableDepthTexture();
            ComicInkFeature feature = EnsureFeature(inkMat);
            if (feature != null)
            {
                // 描边画在「后处理之后」：不会被 Bloom 糊掉，线条更锐
                feature.settings.renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
                feature.SetActive(true);
                EditorUtility.SetDirty(feature);
            }

            SetBloomIntensity(1.6f);

            SaveAll();
            Debug.Log("[Outline] 霓虹风 + 描边已开启（只画墨线，不含网点/色阶）");
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            EditorUtility.DisplayDialog("开启失败", "看 Console 的报错：\n" + e.Message, "好");
        }
    }

    [MenuItem("Tools/Break Out/霓虹 + 描边：关闭", false, 11)]
    public static void DisableNeonOutline()
    {
        if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("请先退出 Play 模式", "开关描边需要在编辑状态下执行。", "知道了");
            return;
        }

        ComicInkFeature feature = FindFeature();
        if (feature != null)
        {
            feature.SetActive(false);
            EditorUtility.SetDirty(feature);
            SaveAll();
            Debug.Log("[Outline] 描边已关闭");
        }
    }

    [MenuItem("Tools/Break Out/切换到美漫描边模式", false, 20)]
    public static void EnableComicMode()
    {
        if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("请先退出 Play 模式", "切换材质/管线需要在编辑状态下执行。", "知道了");
            return;
        }

        try
        {
            Shader toonShader = Shader.Find("BreakOut/ComicToon");
            Shader inkShader = Shader.Find("BreakOut/ComicInk");
            if (toonShader == null || inkShader == null)
                throw new System.Exception("找不到 BreakOut/ComicToon 或 BreakOut/ComicInk，检查 Shader 是否编译报错");

            EnsureFolder(ComicMatFolder);

            // ---- 1. 生成漫画材质 ----
            Material brickRed    = MakeToon("ComicBrick_Red",    new Color(0.87f, 0.26f, 0.26f), new Color(0.45f, 0.30f, 0.55f), new Color(1f, 0.93f, 0.78f));
            Material brickYellow = MakeToon("ComicBrick_Yellow", new Color(0.96f, 0.78f, 0.25f), new Color(0.52f, 0.38f, 0.50f), new Color(1f, 0.95f, 0.85f));
            Material brickBlue   = MakeToon("ComicBrick_Blue",   new Color(0.28f, 0.55f, 0.92f), new Color(0.24f, 0.26f, 0.55f), new Color(0.85f, 0.95f, 1f));
            Material brickGreen  = MakeToon("ComicBrick_Green",  new Color(0.35f, 0.78f, 0.42f), new Color(0.24f, 0.38f, 0.45f), new Color(0.90f, 1f, 0.85f));
            Material playerMat   = MakeToon("ComicPlayer",       new Color(0.18f, 0.85f, 0.92f), new Color(0.20f, 0.30f, 0.55f), new Color(1f, 1f, 0.85f));
            Material deadlineMat = MakeToon("ComicDeadline",     new Color(0.85f, 0.15f, 0.25f), new Color(0.35f, 0.12f, 0.30f), new Color(1f, 0.85f, 0.6f));
            Material wallMat     = MakeToon("ComicWall",         new Color(0.55f, 0.55f, 0.68f), new Color(0.30f, 0.30f, 0.48f), new Color(1f, 1f, 1f));
            Material floorMat    = MakeToon("ComicFloor",        new Color(0.52f, 0.55f, 0.66f), new Color(0.34f, 0.36f, 0.50f), new Color(0.9f, 0.95f, 1f));
            Material bulletMat   = MakeToon("ComicBullet",       new Color(1f, 0.95f, 0.55f),    new Color(0.60f, 0.35f, 0.20f), new Color(1f, 1f, 1f));

            // ---- 2. 全屏墨线材质 ----
            Material inkMat = AssetDatabase.LoadAssetAtPath<Material>(InkMaterialPath);
            if (inkMat == null)
            {
                inkMat = new Material(inkShader);
                AssetDatabase.CreateAsset(inkMat, InkMaterialPath);
            }
            else
            {
                inkMat.shader = inkShader;
            }
            inkMat.SetColor("_InkColor", InkColor);
            // 印刷质感别开太满，否则整屏都是点，看着像噪点错误
            inkMat.SetFloat("_PosterizeStrength", 0.7f);
            inkMat.SetFloat("_BlackLift", 0.6f);
            inkMat.SetFloat("_HalftoneStrength", 0.18f);
            EditorUtility.SetDirty(inkMat);

            // ---- 3. 场景物体换材质 ----
            Material[] brickMats = { brickRed, brickYellow, brickBlue, brickGreen };
            ApplyToScene(brickMats, playerMat, deadlineMat, wallMat, floorMat);

            // ---- 4. 子弹预制体 ----
            ApplyToBulletPrefab(bulletMat, disableTrail: true);

            // ---- 5. 管线：装 Feature + 开深度图 ----
            EnableDepthTexture();
            ComicInkFeature feature = EnsureFeature(inkMat);
            if (feature != null) feature.SetActive(true);

            // ---- 6. 压掉 Bloom ----
            SetBloomIntensity(0.15f);

            SaveAll();
            Debug.Log("[ComicMode] 已切换到美漫描边模式：材质已替换，ComicInk Feature 已启用，Depth Texture 已打开。");
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            EditorUtility.DisplayDialog("切换失败", "看 Console 的报错：\n" + e.Message, "好");
        }
    }

    [MenuItem("Tools/Break Out/还原霓虹发光模式", false, 21)]
    public static void DisableComicMode()
    {
        if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("请先退出 Play 模式", "切换材质需要在编辑状态下执行。", "知道了");
            return;
        }

        try
        {
            // ---- 1. 换回霓虹材质 ----
            RestoreNeonMaterials();

            // ---- 2. 关掉 Feature ----
            ComicInkFeature feature = FindFeature();
            if (feature != null) feature.SetActive(false);

            // ---- 3. Bloom 恢复 ----
            SetBloomIntensity(1.6f);

            SaveAll();
            Debug.Log("[ComicMode] 已还原为霓虹发光模式。");
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
        }
    }

    // ---------------------------------------------------------------------------------
    // 场景材质分配
    // ---------------------------------------------------------------------------------

    /// <summary>把场景里的材质全部换回霓虹版（子弹预制体 + 场景物体）。</summary>
    private static void RestoreNeonMaterials()
    {
        Material bullet = AssetDatabase.LoadAssetAtPath<Material>(NeonMatFolder + "/BulletGlow.mat");
        Material player = AssetDatabase.LoadAssetAtPath<Material>(NeonMatFolder + "/Player.mat");
        Material deadline = AssetDatabase.LoadAssetAtPath<Material>(NeonMatFolder + "/Deadline.mat");
        Material wall = AssetDatabase.LoadAssetAtPath<Material>(NeonMatFolder + "/Wall.mat");
        Material floor = AssetDatabase.LoadAssetAtPath<Material>(NeonMatFolder + "/Plane.mat");

        Material[] neonBricks =
        {
            AssetDatabase.LoadAssetAtPath<Material>(NeonMatFolder + "/Brick_Neon_Pink.mat"),
            AssetDatabase.LoadAssetAtPath<Material>(NeonMatFolder + "/Brick_Neon_Orange.mat"),
            AssetDatabase.LoadAssetAtPath<Material>(NeonMatFolder + "/Brick_Neon_Green.mat"),
            AssetDatabase.LoadAssetAtPath<Material>(NeonMatFolder + "/Brick_Neon_Purple.mat"),
        };

        ApplyToScene(neonBricks, player, deadline, wall, floor);
        ApplyToBulletPrefab(bullet, disableTrail: false);
    }
    private static void ApplyToScene(Material[] brickMats, Material playerMat, Material deadlineMat, Material wallMat, Material floorMat)
    {
        // 玩家：PlayerControl 所在的物体，第一个子物体是可见模型
        PlayerControl player = UnityEngine.Object.FindObjectOfType<PlayerControl>();
        if (player != null)
        {
            Transform paddle = player.transform.childCount > 0 ? player.transform.GetChild(0) : player.transform;
            MeshRenderer paddleRenderer = paddle.GetComponentInChildren<MeshRenderer>();
            if (paddleRenderer != null && playerMat != null)
            {
                paddleRenderer.sharedMaterial = playerMat;
                EditorUtility.SetDirty(paddleRenderer);
            }
        }

        // 砖块：循环上色
        GameObject bricksRoot = GameObject.Find("Bricks");
        if (bricksRoot != null && brickMats != null)
        {
            MeshRenderer[] bricks = bricksRoot.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < bricks.Length; i++)
            {
                Material m = brickMats[i % brickMats.Length];
                if (m == null) continue;
                bricks[i].sharedMaterial = m;
                EditorUtility.SetDirty(bricks[i]);
            }
        }

        AssignByName(new[] { "Deadline" }, deadlineMat);
        AssignByName(new[] { "Cube North", "Cube South", "Wall" }, wallMat);
        // 地面：和墙分开给一个亮一点的颜色，否则在漫画后处理里会被压成黑块
        AssignByName(new[] { "Main Plane", "Plane" }, floorMat);
    }

    private static void AssignByName(string[] names, Material mat)
    {
        if (mat == null) return;
        MeshRenderer[] all = UnityEngine.Object.FindObjectsOfType<MeshRenderer>();
        foreach (MeshRenderer r in all)
        {
            foreach (string n in names)
            {
                if (r.gameObject.name == n)
                {
                    r.sharedMaterial = mat;
                    EditorUtility.SetDirty(r);
                    break;
                }
            }
        }
    }

    // ---------------------------------------------------------------------------------
    // 子弹预制体
    // ---------------------------------------------------------------------------------
    private static void ApplyToBulletPrefab(Material mat, bool disableTrail)
    {
        if (mat == null) return;

        GameObject root = PrefabUtility.LoadPrefabContents(BulletPrefabPath);
        if (root == null) return;
        try
        {
            MeshRenderer mr = root.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = mat;

            TrailRenderer trail = root.GetComponent<TrailRenderer>();
            if (trail != null) trail.enabled = !disableTrail;   // 漫画模式关掉霓虹拖尾

            PrefabUtility.SaveAsPrefabAsset(root, BulletPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ---------------------------------------------------------------------------------
    // 材质生成
    // ---------------------------------------------------------------------------------
    private static Material MakeToon(string name, Color baseColor, Color shadowColor, Color rimColor)
    {
        Shader shader = Shader.Find("BreakOut/ComicToon");
        if (shader == null) throw new System.Exception("找不到 BreakOut/ComicToon");

        string path = ComicMatFolder + "/" + name + ".mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        else
        {
            mat.shader = shader;
        }

        mat.SetColor("_BaseColor", baseColor);
        mat.SetColor("_ShadowColor", shadowColor);
        mat.SetColor("_RimColor", rimColor);
        mat.SetColor("_OutlineColor", InkColor);
        mat.SetFloat("_OutlineWidth", 0.05f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // ---------------------------------------------------------------------------------
    // 管线相关
    // ---------------------------------------------------------------------------------

    /// <summary>在 URP 资产上勾上 Depth Texture —— 屏幕空间描边必须要有它。</summary>
    private static void EnableDepthTexture()
    {
        UniversalRenderPipelineAsset urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urp == null) return;

        SerializedObject so = new SerializedObject(urp);
        SerializedProperty prop = so.FindProperty("m_RequireDepthTexture");
        if (prop != null && !prop.boolValue)
        {
            prop.boolValue = true;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(urp);
            Debug.Log("[ComicMode] 已打开 URP 的 Depth Texture");
        }
    }

    /// <summary>把 ComicInkFeature 挂到当前渲染器数据上（没有就创建）。</summary>
    private static ComicInkFeature EnsureFeature(Material inkMat)
    {
        ScriptableRendererData data = GetActiveRendererData();
        if (data == null)
        {
            Debug.LogWarning("[ComicMode] 找不到渲染器数据，请手动在 URP-HighFidelity-Renderer 上加 ComicInk Feature");
            return null;
        }

        ComicInkFeature feature = null;
        foreach (ScriptableRendererFeature f in data.rendererFeatures)
        {
            if (f is ComicInkFeature existing) { feature = existing; break; }
        }

        if (feature == null)
        {
            feature = ScriptableObject.CreateInstance<ComicInkFeature>();
            feature.name = "ComicInk";
            AssetDatabase.AddObjectToAsset(feature, data);   // 把子资产存进渲染器资产里
            data.rendererFeatures.Add(feature);
            data.SetDirty();
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            Debug.Log("[ComicMode] 已在渲染器上创建 ComicInk Feature");
        }

        feature.settings.material = inkMat;
        EditorUtility.SetDirty(feature);
        return feature;
    }

    private static ComicInkFeature FindFeature()
    {
        ScriptableRendererData data = GetActiveRendererData();
        if (data == null) return null;

        foreach (ScriptableRendererFeature f in data.rendererFeatures)
            if (f is ComicInkFeature existing) return existing;
        return null;
    }

    /// <summary>从当前 URP 资产里取出第 0 个渲染器数据。</summary>
    private static ScriptableRendererData GetActiveRendererData()
    {
        UniversalRenderPipelineAsset urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urp != null)
        {
            SerializedObject so = new SerializedObject(urp);
            SerializedProperty list = so.FindProperty("m_RendererDataList");
            if (list != null && list.arraySize > 0)
            {
                ScriptableRendererData data = list.GetArrayElementAtIndex(0).objectReferenceValue as ScriptableRendererData;
                if (data != null) return data;
            }
        }
        return AssetDatabase.LoadAssetAtPath<ScriptableRendererData>("Assets/Settings/URP-HighFidelity-Renderer.asset");
    }

    private static void SetBloomIntensity(float value)
    {
        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        if (profile == null) return;
        if (!profile.TryGet(out Bloom bloom)) return;

        bloom.intensity.overrideState = true;
        bloom.intensity.value = value;
        EditorUtility.SetDirty(profile);
    }

    // ---------------------------------------------------------------------------------
    // 杂项
    // ---------------------------------------------------------------------------------
    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        string leaf = System.IO.Path.GetFileName(path);
        AssetDatabase.CreateFolder(parent, leaf);
    }

    private static void SaveAll()
    {
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
    }
}
