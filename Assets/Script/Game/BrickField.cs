using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 【砖块场】持续生成，不再一波一波。
///  - 出生点在画面右侧外面（origin.x = 23.5，比之前再往右 8 个单位）
///  - 场上维持 targetAlive 块砖，缺一块就按 spawnInterval 补一块
///  - 难度随时间上涨：血量、速度都在涨，速度还有额外的持续加速
/// </summary>
public class BrickField : MonoBehaviour
{
    [Header("预制体")]
    public GameObject brickPrefab;

    [Header("出生区域")]
    [Tooltip("不写死 x，而是每帧从相机算屏幕右边缘：相机怎么改都不会「在画面里凭空出现」")]
    public Vector3 origin = new Vector3(23.5f, 0.3f, 0f);

    [Tooltip("超出屏幕右边缘多少米生成（越小越靠前）")]
    public float offscreenMargin = 0.5f;

    [Tooltip("z 方向随机范围（±）")]
    public float zRange = 3.8f;

    [Header("持续生成")]
    [Tooltip("开局场上同时存在多少块")]
    public int targetAlive = 16;

    [Tooltip("每升一档难度再多几块（上限 maxAlive）")]
    public int aliveGrowthPerWave = 1;
    public int maxAlive = 24;

    [Tooltip("每隔多久补一块（秒）")]
    public float spawnInterval = 0.45f;

    [Header("随机性")]
    public float positionJitter = 0.45f;
    public Vector2 scaleRange = new Vector2(0.75f, 1.3f);
    public Vector2 speedJitter = new Vector2(0.85f, 1.2f);

    [Header("难度曲线")]
    public float baseHp = 12f;
    [Tooltip("每波线性增量")]
    public float hpPerWave = 0.5f;
    [Tooltip("每波二次增量：前期平缓后期加速")]
    public float hpPerWaveQuadratic = 0.06f;
    [Tooltip("从第几波开始额外爆发式加血")]
    public int lateGameWave = 50;
    [Tooltip("爆发系数：血量 × (1 + 系数 × (波次-起点)²)")]
    public float lateGameBurst = 0.02f;
    public float baseSpeed = 0.12f;
    public float speedPerWave = 0.06f;
    public float maxSpeed = 1.4f;
    [Tooltip("实际移动速度的上限（在减速技能生效之前钳制）")]
    public float maxFinalSpeed = 13f;
    public float speedRampPerSecond = 0.02f;

    [Tooltip("多少秒算一「波」（只用于难度增长）")]
    public float waveInterval = 20f;

    [Header("Boss（每 N 波一只）")]
    public int bossEveryWaves = 5;
    public float bossHpMultiplier = 10f;
    public float bossScale = 2.2f;
    [Tooltip("Boss 移速倍率（越小越慢）")]
    public float bossSpeedMultiplier = 0.32f;
    public Color bossColor = new Color(1f, 0.25f, 0.3f);

    [Header("失败线")]
    public float deadlineX = -9.3f;

    [Header("类型配置（权重越大越常见）")]
    public BrickTypeDef[] catalog =
    {
        new BrickTypeDef { type = BrickType.Normal,    color = new Color(0.25f, 0.9f, 1f),  hpMultiplier = 1f,   score = 10, weight = 6f, xp = 3 },
        new BrickTypeDef { type = BrickType.Armored,   color = new Color(0.35f, 1f, 0.6f),  hpMultiplier = 2.5f, score = 25, weight = 2f, xp = 5 },
        new BrickTypeDef { type = BrickType.Explosive, color = new Color(1f, 0.5f, 0.15f),  hpMultiplier = 1.4f, score = 20, weight = 2f, xp = 4 },
        new BrickTypeDef { type = BrickType.Emp,       color = new Color(0.7f, 0.45f, 1f),  hpMultiplier = 1.2f, score = 20, weight = 1.5f, xp = 4 },
        new BrickTypeDef { type = BrickType.Split,     color = new Color(0.95f, 0.9f, 0.3f),  hpMultiplier = 1.3f, score = 15, weight = 1.5f, xp = 3 },
    };

    private readonly List<Brick> alive = new List<Brick>();
    private float spawnTimer;
    private float waveTimer;
    private int lastBossWave = -1;

    public static BrickField Instance { get; private set; }

    /// <summary>场上当前的 Boss（没有就是 null）</summary>
    public Brick CurrentBoss { get; private set; }

    /// <summary>分裂砖需要用它克隆</summary>
    public GameObject BrickPrefab => brickPrefab;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>分裂出来的小砖登记进来，参与存活计数。</summary>
    public void Register(Brick brick)
    {
        if (brick != null) alive.Add(brick);
    }

    /// <summary>Boss 死亡时清掉引用。</summary>
    public void ClearBoss(Brick boss)
    {
        if (CurrentBoss == boss) CurrentBoss = null;
    }

    private void Start()
    {
        // 开局先铺一批，避免开场空场
        for (int i = 0; i < Mathf.Min(targetAlive, 6); i++) SpawnOne(true);
    }

    private void Update()
    {
        alive.RemoveAll(b => b == null);

        // 难度随时间上升
        waveTimer += Time.deltaTime;
        if (waveTimer >= waveInterval)
        {
            waveTimer = 0f;
            GameManager.Instance?.AdvanceWave();
        }

        // 持续补砖：难度越高，场上允许的砖块越多
        int wave = GameManager.Instance != null ? GameManager.Instance.Wave : 1;
        int want = Mathf.Min(maxAlive, targetAlive + (wave - 1) * aliveGrowthPerWave);

        spawnTimer -= Time.deltaTime;
        if (spawnTimer <= 0f && alive.Count < want)
        {
            spawnTimer = spawnInterval;
            SpawnOne(false);
        }

        // 每 N 波来一只 Boss（同一波只来一次）
        if (bossEveryWaves > 0 && wave % bossEveryWaves == 0 && lastBossWave != wave)
        {
            lastBossWave = wave;
            SpawnBoss(wave);
        }
    }

    /// <summary>生成一只 Boss：大、慢、血厚。</summary>
    private void SpawnBoss(int wave)
    {
        if (brickPrefab == null) return;

        float hpScale = HpScale(wave);
        float speed = Mathf.Min(maxSpeed, baseSpeed + speedPerWave * (wave - 1));
        float globalRamp = 1f + Time.timeSinceLevelLoad * speedRampPerSecond;

        Vector3 pos = new Vector3(
            ComputeSpawnX() + Random.Range(0.5f, 1.5f),
            0.3f,
            Random.Range(-2.5f, 2.5f));

        GameObject go = Instantiate(brickPrefab, pos, Quaternion.identity, transform);

        // 又大又扁：看起来像一堵墙
        Vector3 s = go.transform.localScale;
        go.transform.localScale = new Vector3(s.x * bossScale, s.y * bossScale * 0.9f, s.z * bossScale * 1.15f);

        Brick brick = go.GetComponent<Brick>();
        if (brick == null) brick = go.AddComponent<Brick>();

        BrickTypeDef def = new BrickTypeDef
        {
            type = BrickType.Armored,
            color = bossColor,
            hpMultiplier = 1f,
            score = 60,
            weight = 1f,
            xp = 8,
        };

        float hp = baseHp * hpScale * bossHpMultiplier;
        float bossSpeed = Mathf.Min(speed * globalRamp, maxFinalSpeed) * bossSpeedMultiplier;

        brick.Setup(def, hp, bossSpeed, deadlineX, bossColor, true);
        alive.Add(brick);
        CurrentBoss = brick;

        ScreenShake.Shake(0.2f, 0.3f);
        SoundFx.Emp();
        GameManager.Instance?.NotifyBossSpawned();
        Debug.Log($"[Boss] 第 {wave} 波 Boss 出现：{hp:0} 血 / 速度 {bossSpeed:0.00}");
    }

    /// <summary>
    /// 血量成长曲线：线性 + 二次项。
    ///   wave 1  → 1.00
    ///   wave 5  → 3.96
    ///   wave 10 → 10.36
    ///   wave 20 → 32.16
    /// 二次项让后期不再被玩家成型的 DPS 碾压，前期又不会太难。
    /// </summary>
    private float HpScale(int wave)
    {
        float t = Mathf.Max(0f, wave - 1);
        float scale = 1f + hpPerWave * t + hpPerWaveQuadratic * t * t;

        // 第 50 波之后额外加速：越往后涨得越快
        float late = Mathf.Max(0f, wave - lateGameWave);
        if (late > 0f) scale *= 1f + lateGameBurst * late * late;

        return scale;
    }

    /// <summary>生成一块砖。</summary>
    private void SpawnOne(bool immediate)
    {
        if (brickPrefab == null)
        {
            Debug.LogError("[BrickField] 没有设置 brickPrefab", this);
            return;
        }

        int wave = GameManager.Instance != null ? GameManager.Instance.Wave : 1;
        float hpScale = HpScale(wave);
        float speed = Mathf.Min(maxSpeed, baseSpeed + speedPerWave * (wave - 1));
        float globalRamp = 1f + Time.timeSinceLevelLoad * speedRampPerSecond;

        BrickTypeDef def = PickType();

        // x 从「屏幕右边缘之外」算出来，而不是写死；偏移量小一点，砖块更快进入画面
        float spawnX = ComputeSpawnX() + Random.Range(0f, 1.0f);

        Vector3 pos = new Vector3(
            spawnX,
            0.3f,
            Random.Range(-zRange, zRange));

        pos = FindFreePosition(pos);

        // immediate = 开局那一批，稍微往前放一点，别让玩家等太久
        if (immediate) pos.x -= 3f;

        GameObject go = Instantiate(brickPrefab, pos, Quaternion.identity, transform);

        float scaleMul = Random.Range(scaleRange.x, scaleRange.y);
        go.transform.localScale = Vector3.Scale(go.transform.localScale, new Vector3(1f, 1f, scaleMul));

        Brick brick = go.GetComponent<Brick>();
        if (brick == null) brick = go.AddComponent<Brick>();

        float brickSpeed = speed * globalRamp * Random.Range(speedJitter.x, speedJitter.y);
        // 减速技能是在移动时再乘的，所以这里钳的是「减速前」的速度
        brickSpeed = Mathf.Min(brickSpeed, maxFinalSpeed);

        brick.Setup(def, baseHp * hpScale * def.hpMultiplier * scaleMul, brickSpeed, deadlineX, def.color);
        alive.Add(brick);
    }

    /// <summary>算出「屏幕右边缘之外」的世界 x 坐标。</summary>
    private float ComputeSpawnX()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            Debug.LogError("[BrickField] 找不到 MainCamera，砖块会生成在场地中央 → 表现为子弹一打就消失");
            return origin.x;
        }

        // 相机到砖块平面（y≈0.3）的距离；视角是俯视，forward 朝下
        float distance = Mathf.Abs(cam.transform.position.y - 0.3f);
        if (distance < 0.01f) return origin.x;

        // viewport x = 1 就是屏幕右边缘；再往外一点点，保证是「飞进来」而不是「突然出现」
        Vector3 rightEdge = cam.ViewportToWorldPoint(new Vector3(1f, 0.5f, distance));
        return rightEdge.x + offscreenMargin;
    }

    /// <summary>避免两块砖叠在一起（叠一起会像「多层发光」）。</summary>
    private Vector3 FindFreePosition(Vector3 desired)
    {
        for (int attempt = 0; attempt < 6; attempt++)
        {
            Collider[] hits = Physics.OverlapSphere(desired, 0.7f, ~0, QueryTriggerInteraction.Ignore);
            bool blocked = false;
            foreach (Collider c in hits)
            {
                if (c.GetComponentInParent<Brick>() != null) { blocked = true; break; }
            }
            if (!blocked) return desired;

            desired.x += Random.Range(0.5f, 1.4f);
            desired.z += Random.Range(-0.8f, 0.8f);
        }
        return desired;
    }

    private BrickTypeDef PickType()
    {
        if (catalog == null || catalog.Length == 0)
            return new BrickTypeDef { type = BrickType.Normal, color = Color.cyan, hpMultiplier = 1f, score = 10, xp = 3 };

        float total = 0f;
        foreach (BrickTypeDef d in catalog) total += Mathf.Max(0f, d.weight);

        float roll = Random.Range(0f, total);
        foreach (BrickTypeDef d in catalog)
        {
            roll -= Mathf.Max(0f, d.weight);
            if (roll <= 0f) return d;
        }
        return catalog[0];
    }
}
