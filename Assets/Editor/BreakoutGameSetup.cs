// =====================================================================================
// BreakoutGameSetup.cs —— 一键把新玩法系统装配进场景
//
// 做的事：
//   1. 修砖块预制体：加 Brick 脚本 + 运动学刚体、取消 Trigger、换自发光材质
//   2. 场景里生成 GameSystems（GameManager + PlayerStats + GameUI）
//   3. 生成 BrickField 并指向砖块预制体
//   4. 相机加 ScreenShake，玩家模型加 Recoil 并接到 Shoot
//   5. 删掉旧的手摆 Bricks（单块、没有 Brick 脚本，会挡路）
//
// 菜单：Tools → Break Out → 初始化玩法系统
// 编译后会自动跑一次（SessionState 保证每会话只跑一次）
// =====================================================================================
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class BreakoutGameSetup
{
    private const string BrickPrefabPath = "Assets/Prefab/Brick/Brick.prefab";
    private const string BulletPrefabPath = "Assets/Prefab/Bullet/Sphere.prefab";
    private const string BrickMaterialPath = "Assets/Materials/Brick_Neon_Pink.mat";
    /// <summary>把场景里名为 Deadline 的红色竖线摆到真正的失败线位置，并加粗一点。</summary>
    private static void AlignDeadlineVisual(float deadlineX)
    {
        var transforms = Resources.FindObjectsOfTypeAll<Transform>();
        int fixedCount = 0;

        foreach (Transform tr in transforms)
        {
            if (tr == null || tr.name != "Deadline") continue;
            if (tr.gameObject.scene.name == null) continue;          // 跳过 prefab 资源
            if (tr.GetComponent<MeshRenderer>() == null) continue;    // 只处理有模型的那根线

            Vector3 pos = tr.localPosition;
            pos.x = deadlineX;
            pos.y = 0.05f;
            tr.localPosition = pos;

            // 只调粗细，长度(z)保持面板上的值，不再强制拉长
            Vector3 scale = tr.localScale;
            scale.x = 0.12f;
            tr.localScale = scale;

            fixedCount++;
        }

        Debug.Log($"[GameSetup] 失败线视觉已对齐到 x={deadlineX}（处理 {fixedCount} 个）");
    }
    private const string SessionKey = "Breakout.GameSetup.Done.v12";

    static BreakoutGameSetup()
    {
        EditorApplication.delayCall += AutoRun;
    }

    private static void AutoRun()
    {
        if (Application.isBatchMode) return;
        if (SessionState.GetBool(SessionKey, false)) return;
        if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += AutoRun;
            return;
        }

        // 只在游戏场景里跑
        if (UnityEngine.Object.FindObjectOfType<PlayerControl>() == null) return;

        SessionState.SetBool(SessionKey, true);
        Setup();
    }

    [MenuItem("Tools/Break Out/初始化玩法系统", false, 5)]
    public static void Setup()
    {
        try
        {
            SetupBrickPrefab();
            SetupBulletPrefab();
            SetupScene();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            Debug.Log("[GameSetup] 玩法系统装配完成：GameManager / PlayerStats / GameUI / BrickField / ScreenShake / Recoil");
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            EditorUtility.DisplayDialog("装配失败", e.Message, "好");
        }
    }

    // ------------------------------------------------------------------
    // 砖块预制体
    // ------------------------------------------------------------------
    private static void SetupBrickPrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(BrickPrefabPath);
        if (root == null) throw new System.Exception("找不到砖块预制体：" + BrickPrefabPath);

        try
        {
            Rigidbody rb = root.GetComponent<Rigidbody>();
            if (rb == null) rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;      // 砖块靠代码移动，不要重力
            rb.useGravity = false;

            if (root.GetComponent<Brick>() == null) root.AddComponent<Brick>();

            Collider[] colliders = root.GetComponents<Collider>();
            foreach (Collider c in colliders) c.isTrigger = false;   // 必须能产生碰撞

            MeshRenderer mr = root.GetComponent<MeshRenderer>();
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(BrickMaterialPath);
            if (mr != null && mat != null) mr.sharedMaterial = mat;

            PrefabUtility.SaveAsPrefabAsset(root, BrickPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        // 砖块共享材质关掉自带发光：颜色/发光完全由每个砖块的 MaterialPropertyBlock 控制，
        // 否则「材质发光 + MPB 发光」会叠成多层，看起来像过曝。
        Material brickMat = AssetDatabase.LoadAssetAtPath<Material>(BrickMaterialPath);
        if (brickMat != null)
        {
            brickMat.SetColor("_BaseColor", new Color(0.08f, 0.1f, 0.12f, 1f));
            brickMat.SetColor("_EmissionColor", Color.black);
            brickMat.EnableKeyword("_EMISSION");
            EditorUtility.SetDirty(brickMat);
        }
    }

    /// <summary>子弹改成 Trigger：穿透不再被物理顶住，也不会和别的子弹互撞。</summary>
    private static void SetupBulletPrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(BulletPrefabPath);
        if (root == null) return;

        try
        {
            Collider col = root.GetComponent<Collider>();
            if (col != null) col.isTrigger = true;

            Rigidbody rb = root.GetComponent<Rigidbody>();
            if (rb == null) rb = root.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.isKinematic = true;

            Bullet bullet = root.GetComponent<Bullet>();
            if (bullet == null) bullet = root.AddComponent<Bullet>();
            bullet.bulletColor = new Color(0.45f, 1f, 1f);

            PrefabUtility.SaveAsPrefabAsset(root, BulletPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ------------------------------------------------------------------
    // 场景对象
    // ------------------------------------------------------------------
    private static void SetupScene()
    {
        // ---------- 管理器 ----------
        GameObject systems = GameObject.Find("GameSystems");
        if (systems == null)
        {
            systems = new GameObject("GameSystems");
            systems.AddComponent<GameManager>();
            systems.AddComponent<PlayerStats>();
            systems.AddComponent<GameUI>();
            Debug.Log("[GameSetup] 已创建 GameSystems");
        }
        else
        {
            if (systems.GetComponent<GameManager>() == null) systems.AddComponent<GameManager>();
            if (systems.GetComponent<PlayerStats>() == null) systems.AddComponent<PlayerStats>();
            if (systems.GetComponent<GameUI>() == null) systems.AddComponent<GameUI>();
        }

        // ---------- 砖块场 ----------
        BrickField field = UnityEngine.Object.FindObjectOfType<BrickField>();
        bool createdField = false;
        if (field == null)
        {
            GameObject fieldGo = new GameObject("BrickField");
            field = fieldGo.AddComponent<BrickField>();
            createdField = true;
        }
        field.brickPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BrickPrefabPath);

        // 只在「新建组件」时写初始数值。
        // 已有组件一律不动 —— 否则每次进 Unity 都会把面板上调好的参数刷回默认值
        // （之前 zRange 被强制写回 3.8 就是这么来的）。
        if (createdField)
        {
            field.deadlineX = -9.3f;
            field.offscreenMargin = 0.5f;
            field.zRange = 3.2f;
            field.targetAlive = 16;
            field.aliveGrowthPerWave = 1;
            field.maxAlive = 24;
            field.spawnInterval = 0.45f;
            field.baseHp = 12f;
            field.hpPerWave = 0.5f;
            field.speedPerWave = 0.06f;
        }

        // 负缩放会让砖块的 BoxCollider 被翻转并刷警告，必须拉回 1
        field.transform.localScale = Vector3.one;

        // 失败线视觉：对齐真正的 deadlineX(-9.3)。
        // 原来在 -11.5，16:10 窗口下会掉出视野，打包后完全看不见。
        AlignDeadlineVisual(field.deadlineX);

        // 砖块类型表（新增「分裂砖」后必须重写一遍，否则场景里还是旧的 4 种）
        field.catalog = new[]
        {
            new BrickTypeDef { type = BrickType.Normal,    color = new Color(0.25f, 0.90f, 1.00f), hpMultiplier = 1.0f, score = 10, weight = 6.0f, xp = 3 },
            new BrickTypeDef { type = BrickType.Armored,   color = new Color(0.35f, 1.00f, 0.60f), hpMultiplier = 2.5f, score = 25, weight = 2.0f, xp = 5 },
            new BrickTypeDef { type = BrickType.Explosive, color = new Color(1.00f, 0.50f, 0.15f), hpMultiplier = 1.4f, score = 20, weight = 2.0f, xp = 4 },
            new BrickTypeDef { type = BrickType.Emp,       color = new Color(0.70f, 0.45f, 1.00f), hpMultiplier = 1.2f, score = 20, weight = 1.5f, xp = 4 },
            new BrickTypeDef { type = BrickType.Split,     color = new Color(0.95f, 0.90f, 0.30f), hpMultiplier = 1.3f, score = 15, weight = 1.5f, xp = 3 },
        };
        EditorUtility.SetDirty(field);

        // 升级曲线（同样只在新建时写）
        GameManager gm = UnityEngine.Object.FindObjectOfType<GameManager>();
        if (gm != null && createdField)
        {
            gm.baseXpToLevel = 30;
            gm.xpGrowthPerLevel = 20;
            EditorUtility.SetDirty(gm);
        }

        // ---------- 相机震屏 ----------
        Camera cam = Camera.main;
        if (cam != null && cam.GetComponent<ScreenShake>() == null)
            cam.gameObject.AddComponent<ScreenShake>();

        // ---------- 玩家 ----------
        PlayerControl player = UnityEngine.Object.FindObjectOfType<PlayerControl>();
        if (player != null)
        {
            Transform paddle = player.transform.childCount > 0 ? player.transform.GetChild(0) : player.transform;

            Recoil recoil = paddle.GetComponent<Recoil>();
            if (recoil == null) recoil = paddle.gameObject.AddComponent<Recoil>();
            recoil.localDirection = Vector3.left;   // 往 -X 退一下
            recoil.distance = 0.08f;
            recoil.duration = 0.1f;

            Shoot shoot = player.GetComponent<Shoot>();
            if (shoot != null) shoot.recoil = recoil;
        }

        // ---------- 清掉旧的手摆砖块 ----------
        GameObject oldBricks = GameObject.Find("Bricks");
        if (oldBricks != null)
        {
            Debug.Log("[GameSetup] 删除旧的手摆 Bricks（改由 BrickField 生成）");
            UnityEngine.Object.DestroyImmediate(oldBricks);
        }
    }
}
