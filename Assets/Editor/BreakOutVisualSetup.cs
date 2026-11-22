using UnityEditor;                    // MenuItem / AssetDatabase / PrefabUtility / EditorUtility
using UnityEditor.SceneManagement;    // EditorSceneManager（保存场景）
using UnityEngine;                    // Material / GameObject / Scene
using UnityEngine.Rendering;          // VolumeProfile / BlendMode
using UnityEngine.Rendering.Universal; // Bloom / Vignette / ColorAdjustments（URP 后处理）

/// <summary>
/// 【一键视觉配置】只在编辑器里运行，不会打进最终包。
///
/// 它把「子弹发射链路」和「霓虹发光渲染」一次性配好，重复执行也安全（幂等）：
///   1. 生成/更新材质：子弹、拖尾、玩家、砖块（4 色）、围墙、死亡线
///   2. 给子弹预制体补上 Rigidbody + TrailRenderer + Bullet 脚本，并换发光材质
///   3. 场景连线：BulletPool.bulletPrefab、Player 上的 Shoot.firePoint
///   4. 后处理：Bloom 阈值降低 + 强度提高，加一点饱和度/对比度
///
/// 用法：菜单栏 Tools → Break Out → 一键配置发射 + 霓虹发光
/// </summary>
public static class BreakOutVisualSetup
{
    private const string BulletPrefabPath = "Assets/Prefab/Bullet/Sphere.prefab";
    private const string ProfilePath      = "Assets/Settings/SampleSceneProfile.asset";
    private const string MatFolder        = "Assets/Materials";

    [MenuItem("Tools/Break Out/一键配置发射 + 霓虹发光", false, 1)]
    public static void Setup()
    {
        // 运行中改场景/预制体会被回滚，直接拦住
        if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("请先退出 Play 模式", "配置需要在编辑状态下执行。", "知道了");
            return;
        }

        try
        {
            // ---------- 1. 材质 ----------
            Material bulletMat   = Neon("BulletGlow", new Color(0.25f, 0.95f, 1f), 6.0f, 0.9f);
            Material playerMat   = Neon("Player",     new Color(0.15f, 1.00f, 0.95f), 3.5f, 0.85f);
            Material deadlineMat = Neon("Deadline",   new Color(1.00f, 0.16f, 0.28f), 4.0f, 0.7f);
            Material wallMat     = Neon("Wall",       new Color(0.25f, 0.45f, 1.00f), 1.2f, 0.6f);
            Material trailMat    = TrailMaterial();

            // 砖块用 4 种霓虹色循环，视觉上更有层次
            Material[] brickMats =
            {
                Neon("Brick_Neon_Pink",   new Color(1.00f, 0.25f, 0.75f), 2.2f, 0.8f),
                Neon("Brick_Neon_Orange", new Color(1.00f, 0.55f, 0.20f), 2.2f, 0.8f),
                Neon("Brick_Neon_Green",  new Color(0.30f, 1.00f, 0.45f), 2.2f, 0.8f),
                Neon("Brick_Neon_Purple", new Color(0.65f, 0.35f, 1.00f), 2.2f, 0.8f),
            };

            // ---------- 2. 子弹预制体 ----------
            GameObject bulletPrefab = SetupBulletPrefab(bulletMat, trailMat);

            // ---------- 3. 场景连线 ----------
            SetupScene(bulletPrefab, playerMat, deadlineMat, wallMat, brickMats);

            // ---------- 4. 后处理 ----------
            SetupPostProcessing();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.SaveOpenScenes();

            Debug.Log("[BreakOut] 配置完成：子弹预制体已补 Rigidbody/Trail/Bullet，BulletPool 已挂预制体，" +
                      "枪口 FirePoint 已创建并指向 +X，Bloom 已调强。直接 Play 就能发射。");
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            EditorUtility.DisplayDialog("配置失败", "看 Console 的报错详情：\n" + e.Message, "好");
        }
    }

    // ------------------------------------------------------------------
    // 材质
    // ------------------------------------------------------------------

    /// <summary>
    /// 取或建一个 URP Lit 的自发光材质。
    /// 玩法：底色压暗（*0.3）→ 发光色 = 颜色 * emission（HDR，大于 1 才会被 Bloom 捕捉）。
    /// </summary>
    private static Material Neon(string assetName, Color color, float emission, float smoothness)
    {
        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit == null) throw new System.Exception("找不到 URP Lit 着色器，确认工程是 URP 且包已导入");

        string path = MatFolder + "/" + assetName + ".mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(urpLit);
            AssetDatabase.CreateAsset(mat, path);
        }
        else if (mat.shader != urpLit)
        {
            mat.shader = urpLit;
        }

        // 底色：压暗，让发光和 Bloom 更突出
        mat.SetColor("_BaseColor", new Color(color.r * 0.3f, color.g * 0.3f, color.b * 0.3f, 1f));
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetFloat("_Metallic", 0.1f);

        // 自发光：HDR 颜色（分量可以大于 1），配合 Bloom 就有“霓虹灯”感
        mat.SetColor("_EmissionColor", new Color(color.r * emission, color.g * emission, color.b * emission, 1f));
        mat.EnableKeyword("_EMISSION");
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>
    /// 拖尾材质：需要透明混合 + 顶点色（TrailRenderer 用顶点色画渐变）。
    /// 依次尝试 URP 粒子着色器 → 旧版 Additive（URP 也能渲染，因为没有 LightMode 标签的 pass 走 SRPDefaultUnlit）→ Sprites/Default。
    /// </summary>
    private static Material TrailMaterial()
    {
        string path = MatFolder + "/BulletTrail.mat";

        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        bool urpParticles = shader != null;
        if (shader == null) shader = Shader.Find("Legacy Shaders/Particles/Additive");
        if (shader == null) shader = Shader.Find("Sprites/Default");

        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        else if (mat.shader != shader)
        {
            mat.shader = shader;
        }

        Color tint = new Color(0.45f, 0.95f, 1f, 0.9f);

        if (urpParticles)
        {
            // URP 的 ShaderGraph 材质靠属性 + 关键字切透明模式，纯代码要手动设全
            SetFloatIfHas(mat, "_Surface", 1f);          // 1 = Transparent
            SetFloatIfHas(mat, "_Blend", 1f);            // 1 = Additive
            SetFloatIfHas(mat, "_SrcBlend", (float)BlendMode.SrcAlpha);
            SetFloatIfHas(mat, "_DstBlend", (float)BlendMode.One);
            SetFloatIfHas(mat, "_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
        }

        SetColorIfHas(mat, "_BaseColor", tint);
        SetColorIfHas(mat, "_TintColor", tint);   // 旧版粒子着色器用的是 _TintColor
        SetColorIfHas(mat, "_Color", tint);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static void SetColorIfHas(Material m, string prop, Color c)
    {
        if (m.HasProperty(prop)) m.SetColor(prop, c);
    }

    private static void SetFloatIfHas(Material m, string prop, float v)
    {
        if (m.HasProperty(prop)) m.SetFloat(prop, v);
    }

    // ------------------------------------------------------------------
    // 子弹预制体
    // ------------------------------------------------------------------

    /// <summary>
    /// 打开预制体 → 补组件 → 保存。用 LoadPrefabContents 而不是直接改资源，
    /// 这样不会把预制体实例化到场景里，也不需要打开 Prefab 编辑窗口。
    /// </summary>
    private static GameObject SetupBulletPrefab(Material bulletMat, Material trailMat)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(BulletPrefabPath);
        if (root == null) throw new System.Exception("找不到子弹预制体：" + BulletPrefabPath);

        try
        {
            // --- 刚体：没有它就没有碰撞事件，子弹会直接穿过砖块 ---
            Rigidbody rb = root.GetComponent<Rigidbody>();
            if (rb == null) rb = root.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.mass = 0.2f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic; // 防高速穿透
            rb.interpolation = RigidbodyInterpolation.Interpolate;                 // 画面更顺

            // --- Bullet 脚本（必须挂在预制体上，池子生成的就是预制体的克隆）---
            Bullet bullet = root.GetComponent<Bullet>();
            if (bullet == null) bullet = root.AddComponent<Bullet>();
            bullet.speed = 30f;
            bullet.lifeTime = 2.5f;

            // --- 拖尾：让子弹有“光轨” ---
            TrailRenderer trail = root.GetComponent<TrailRenderer>();
            if (trail == null) trail = root.AddComponent<TrailRenderer>();
            ConfigureTrail(trail, trailMat);

            // --- 换发光材质，关阴影（发光小球投阴影很怪）---
            MeshRenderer mr = root.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.sharedMaterial = bulletMat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }

            PrefabUtility.SaveAsPrefabAsset(root, BulletPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);   // 一定要卸载，否则内存里留一份脏的预制体
        }

        return AssetDatabase.LoadAssetAtPath<GameObject>(BulletPrefabPath);
    }

    /// <summary>拖尾参数：短、细、头亮尾淡，配合 Bloom 就是一条光带。</summary>
    private static void ConfigureTrail(TrailRenderer trail, Material mat)
    {
        trail.sharedMaterial   = mat;
        trail.time             = 0.18f;   // 拖尾长度 = 速度 * 时间，30m/s * 0.18 ≈ 5.4 米
        trail.minVertexDistance = 0.02f;
        trail.startWidth       = 0.18f;
        trail.endWidth         = 0f;      // 尾端收成 0，自然变细
        trail.numCapVertices   = 4;       // 圆头
        trail.alignment        = LineAlignment.View;
        trail.textureMode      = LineTextureMode.Stretch;
        trail.shadowCastingMode = ShadowCastingMode.Off;
        trail.receiveShadows   = false;

        // 颜色渐变：青白 → 蓝 → 紫，透明度从 1 衰减到 0
        Gradient g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.75f, 1.00f, 1.00f), 0.0f),
                new GradientColorKey(new Color(0.20f, 0.65f, 1.00f), 0.55f),
                new GradientColorKey(new Color(0.60f, 0.25f, 1.00f), 1.0f),
            },
            new[]
            {
                new GradientAlphaKey(1.0f, 0.0f),
                new GradientAlphaKey(0.55f, 0.5f),
                new GradientAlphaKey(0.0f, 1.0f),
            });
        trail.colorGradient = g;
    }

    // ------------------------------------------------------------------
    // 场景连线
    // ------------------------------------------------------------------

    private static void SetupScene(GameObject bulletPrefab, Material playerMat,
                                   Material deadlineMat, Material wallMat, Material[] brickMats)
    {
        UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();

        // ---------- 对象池 ----------
        BulletPool pool = UnityEngine.Object.FindObjectOfType<BulletPool>();
        if (pool == null)
        {
            GameObject go = new GameObject("BulletPool");
            pool = go.AddComponent<BulletPool>();
            Debug.Log("[BreakOut] 场景里没有 BulletPool，已自动新建一个");
        }

        // 清掉手动拖进场景的测试子弹实例：
        // 它身上单独加过一份 Bullet 组件，预制体补上 Bullet 之后就会变成“两个 Bullet”，必须删
        for (int i = pool.transform.childCount - 1; i >= 0; i--)
        {
            GameObject child = pool.transform.GetChild(i).gameObject;
            if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(child) == BulletPrefabPath)
            {
                Debug.Log("[BreakOut] 删除池子下的测试子弹实例：" + child.name);
                UnityEngine.Object.DestroyImmediate(child);
            }
        }

        pool.bulletPrefab = bulletPrefab;
        pool.poolSize = 30;
        EditorUtility.SetDirty(pool);

        // ---------- 玩家 + 枪口 ----------
        PlayerControl player = UnityEngine.Object.FindObjectOfType<PlayerControl>();
        if (player == null) throw new System.Exception("场景里找不到带 PlayerControl 的玩家对象");

        // 玩家可见模型是 Player 下面的子物体（带 MeshRenderer 的方块）
        Transform paddle = player.transform.childCount > 0 ? player.transform.GetChild(0) : player.transform;

        Transform firePoint = paddle.Find("FirePoint");
        if (firePoint == null)
        {
            GameObject fp = new GameObject("FirePoint");
            firePoint = fp.transform;
            firePoint.SetParent(paddle, false);
        }
        // 放在方块前方 +X 方向（父物体缩放 0.3，所以局部 2 ≈ 世界 0.6 米，刚好在方块外面）
        firePoint.localPosition = new Vector3(2f, 0f, 0f);
        // 绕 Y 转 90° → 让 forward 指向 +X（朝砖块方向）。想改射击方向，只改这个旋转即可
        firePoint.localRotation = Quaternion.Euler(0f, 90f, 0f);

        Shoot shoot = player.GetComponent<Shoot>();
        if (shoot == null) shoot = player.gameObject.AddComponent<Shoot>();
        shoot.firePoint = firePoint;
        shoot.fireRate = 0.25f;
        EditorUtility.SetDirty(shoot);

        // ---------- 材质分配 ----------
        MeshRenderer paddleRenderer = paddle.GetComponentInChildren<MeshRenderer>();
        if (paddleRenderer != null)
        {
            paddleRenderer.sharedMaterial = playerMat;
            EditorUtility.SetDirty(paddleRenderer);
        }

        // 砖块：Bricks 节点下的所有子物体循环上 4 种霓虹色
        GameObject bricksRoot = GameObject.Find("Bricks");
        if (bricksRoot != null)
        {
            MeshRenderer[] bricks = bricksRoot.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < bricks.Length; i++)
            {
                bricks[i].sharedMaterial = brickMats[i % brickMats.Length];
                EditorUtility.SetDirty(bricks[i]);
            }

            // 关键：场景里这块砖的 Collider 被勾了 Is Trigger，
            // Trigger 只走 OnTriggerEnter、不会走 OnCollisionEnter，子弹会直接穿过去。
            // 这里统一取消勾选，让物理碰撞正常工作。
            Collider[] brickColliders = bricksRoot.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < brickColliders.Length; i++)
            {
                if (brickColliders[i].isTrigger)
                {
                    brickColliders[i].isTrigger = false;
                    EditorUtility.SetDirty(brickColliders[i]);
                    Debug.Log("[BreakOut] 已取消 Is Trigger：" + brickColliders[i].name);
                }
            }

            Debug.Log("[BreakOut] 砖块上色数量：" + bricks.Length);
        }

        // 死亡线（红色警示）与围墙（冷色）
        AssignMaterialByName(new[] { "Deadline" }, deadlineMat);
        AssignMaterialByName(new[] { "Cube North", "Cube South", "Wall" }, wallMat);

        // ---------- 保存 ----------
        EditorSceneManager.MarkSceneDirty(scene);
    }

    /// <summary>按物体名给场景里的 MeshRenderer 换材质（找不到就跳过，不报错）。</summary>
    private static void AssignMaterialByName(string[] objectNames, Material mat)
    {
        MeshRenderer[] all = UnityEngine.Object.FindObjectsOfType<MeshRenderer>();
        foreach (MeshRenderer r in all)
        {
            foreach (string n in objectNames)
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

    // ------------------------------------------------------------------
    // 后处理（URP Volume）
    // ------------------------------------------------------------------

    /// <summary>
    /// Bloom 是“发光效果”的关键：只有亮度超过 threshold 的像素才会溢光。
    /// 所以材质自发光要 HDR（值 > 1），阈值要调低一点，两者配合才有霓虹感。
    /// </summary>
    private static void SetupPostProcessing()
    {
        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        if (profile == null)
        {
            Debug.LogWarning("[BreakOut] 找不到 Volume Profile：" + ProfilePath + "，跳过 Bloom 配置");
            return;
        }

        // ---- Bloom ----
        if (!profile.TryGet(out Bloom bloom)) bloom = profile.Add<Bloom>(true);
        bloom.active = true;
        bloom.threshold.overrideState = true;
        bloom.threshold.value = 0.85f;       // 默认 1，降到 0.85 让中等亮度也发光
        bloom.intensity.overrideState = true;
        bloom.intensity.value = 1.6f;        // 默认 1，调强
        bloom.scatter.overrideState = true;
        bloom.scatter.value = 0.72f;         // 光晕扩散范围
        bloom.tint.overrideState = true;
        bloom.tint.value = new Color(0.85f, 0.95f, 1f);  // 冷白偏青
        bloom.highQualityFiltering.overrideState = true;
        bloom.highQualityFiltering.value = true;

        // ---- 颜色校正：提饱和 + 加对比，霓虹感更强 ----
        if (!profile.TryGet(out ColorAdjustments ca)) ca = profile.Add<ColorAdjustments>(true);
        ca.active = true;
        ca.saturation.overrideState = true;
        ca.saturation.value = 18f;
        ca.contrast.overrideState = true;
        ca.contrast.value = 10f;
        ca.colorFilter.overrideState = true;
        ca.colorFilter.value = new Color(0.92f, 0.96f, 1f);

        // ---- 暗角：把注意力压到画面中间 ----
        if (!profile.TryGet(out Vignette vig)) vig = profile.Add<Vignette>(true);
        vig.active = true;
        vig.intensity.overrideState = true;
        vig.intensity.value = 0.32f;
        vig.smoothness.overrideState = true;
        vig.smoothness.value = 0.45f;

        EditorUtility.SetDirty(profile);
        Debug.Log("[BreakOut] 后处理已更新：Bloom / ColorAdjustments / Vignette");
    }
}
