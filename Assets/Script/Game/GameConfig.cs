using System.Collections;
using UnityEngine;

/// <summary>
/// 【远程配置】游戏启动 / 场景加载时从服务器拉一份数值，覆盖 Inspector 默认值。
/// 改服务器上的 config.json 即可调平衡，不用重新打包。
///
/// 协议：GET /config
/// 字段写 -1（或省略、写 null）表示"不覆盖"，保留本地值。
/// 拉取失败就安静地用本地默认值，不影响游戏。
/// </summary>
public class GameConfig : MonoBehaviour
{
    [System.Serializable]
    public class BrickOverride
    {
        public string type;                 // Normal / Armored / Explosive / Emp / Split
        public float hpMultiplier = -1f;    // 血量倍率
        public float score = -1f;           // 击杀分数
        public float weight = -1f;          // 出现权重
        public float xp = -1f;              // 经验
    }

    [System.Serializable]
    public class Balance
    {
        public int version = -1;
        /// <summary>设成 1 就完全忽略远程配置（调试用：让 Inspector 里的值生效）</summary>
        public int disableRemote = 0;

        // ---- 砖块 / 生成 ----
        public float baseHp = -1f;
        public float hpPerWave = -1f;
        public float hpPerWaveQuadratic = -1f;
        public float bossHpMultiplier = -1f;
        public float bossEveryWaves = -1f;
        public float targetAlive = -1f;
        public float aliveGrowthPerWave = -1f;
        public float maxAlive = -1f;
        public float spawnInterval = -1f;
        public float baseSpeed = -1f;
        public float speedPerWave = -1f;
        public float waveInterval = -1f;
        public float speedRampPerSecond = -1f;
        public float maxSpeed = -1f;
        public float maxFinalSpeed = -1f;
        public float lateGameWave = -1f;
        public float lateGameBurst = -1f;

        // ---- 玩家 ----
        public float baseDamage = -1f;
        public float baseFireInterval = -1f;
        public float damagePerLevel = -1f;
        public float fireRatePerLevel = -1f;
        public float minFireInterval = -1f;

        // ---- 技能系数 ----
        public float baseCritChance = -1f;
        public float baseCritDamage = -1f;
        public float critPerTier = -1f;
        public float critDamagePerTier = -1f;
        public float lethalPerTier = -1f;
        public float buffDamagePerTier = -1f;
        public float buffIntervalPerTier = -1f;
        public float rapidFirePerTier = -1f;
        public float xpPerTier = -1f;
        public float explosionBase = -1f;
        public float explosionPerTier = -1f;
        public float explosionRadiusBase = -1f;
        public float explosionRadiusPerTier = -1f;
        public float empBase = -1f;
        public float empPerTier = -1f;
        public float empRadiusBase = -1f;
        public float empRadiusPerTier = -1f;
        public float empDurationBase = -1f;
        public float empDurationPerTier = -1f;
        public float dotPerTier = -1f;
        public float slowPerTier = -1f;
        public float freezeChancePerTier = -1f;
        public float freezeDuration = -1f;
        public float lethalBossPercent = -1f;
        public float buffCycle = -1f;
        public float buffDuration = -1f;
        public float buffBurstMultiplier = -1f;
        public float buffCyclePerTier = -1f;
        public float buffDurationPerTier = -1f;
        public float dotDuration = -1f;

        // ---- 其他 ----
        public int leaderboardLimit = -1;
        public float bossControlCooldown = -1f;
        public int[] empTierLimits;
        public int[] shieldCharges;
        public int[] shieldRecharge;

        // 经验曲线
        public int baseXpToLevel = -1;
        public int xpGrowthPerLevel = -1;
        public int upgradeEveryLevels = -1;
        public int rerollCount = -1;
        public BrickOverride[] bricks;
    }

    /// <summary>配置是否已经拉取完成（成功或失败都算完成）。GameManager 用它决定何时弹首个三选一。</summary>
    public static bool Loaded { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        GameObject go = new GameObject("~GameConfig");
        DontDestroyOnLoad(go);
        go.AddComponent<GameConfig>();
    }

    private void Awake()
    {
        // 场景重载（按 R 重开）时也要重新拉配置
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene,
                               UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        StartCoroutine(FetchOnce());
    }

    private IEnumerator Start()
    {
        yield return FetchOnce();
    }

    private IEnumerator FetchOnce()
    {
        yield return new WaitForSecondsRealtime(0.5f);

        LeaderboardClient client = LeaderboardClient.Instance;
        if (client == null)
        {
            Loaded = true;
            yield break;
        }

        // 玩家的 server.txt 第一行是 127.0.0.1，在自己机器上必然失败；
        // 健康检查要 8 秒后才会切到远程地址，所以这里必须重试，
        // 否则远程配置永远不生效（表现之一：排行榜只显示 10 条）。
        const int maxAttempts = 6;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            bool done = false;
            bool okResult = false;
            string body = null;

            client.FetchRaw("/config", (ok, json) =>
            {
                done = true;
                okResult = ok;
                body = json;
            });

            float waited = 0f;
            while (!done && waited < 3f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            if (okResult && !string.IsNullOrEmpty(body))
            {
                Balance cfg = null;
                try { cfg = JsonUtility.FromJson<Balance>(body); }
                catch (System.Exception e) { Debug.LogWarning($"[GameConfig] 解析失败：{e.Message}"); }

                if (cfg != null)
                {
                    Apply(cfg);
                    Loaded = true;
                    Debug.Log($"[GameConfig] 远程配置已应用（第 {attempt + 1} 次尝试，v{(cfg.version > 0 ? cfg.version : 0)}）");
                    yield break;
                }
            }

            // 第一次失败就先放行游戏流程，避免开局等太久；后面继续重试
            if (attempt == 0) Loaded = true;

            yield return new WaitForSecondsRealtime(2f);
        }

        Debug.LogWarning("[GameConfig] 多次尝试仍无法拉取 /config，使用本地默认数值");
        Loaded = true;
    }

    // ------------------------------------------------------------------
    // 应用
    // ------------------------------------------------------------------
    private static void Apply(Balance cfg)
    {
        if (cfg.disableRemote == 1)
        {
            Debug.Log("[GameConfig] disableRemote=1，忽略远程配置，使用 Inspector 数值");
            return;
        }

        int applied = 0;

        void F(ref float target, float value)
        {
            if (value < 0f) return;
            target = value;
            applied++;
        }

        // ---- 玩家 ----
        PlayerStats stats = PlayerStats.Instance;
        if (stats != null)
        {
            F(ref stats.baseDamage, cfg.baseDamage);
            F(ref stats.baseFireInterval, cfg.baseFireInterval);
            F(ref stats.damagePerLevel, cfg.damagePerLevel);
            F(ref stats.fireRatePerLevel, cfg.fireRatePerLevel);
            F(ref stats.minFireInterval, cfg.minFireInterval);

            F(ref stats.baseCritChance, cfg.baseCritChance);
            F(ref stats.baseCritDamage, cfg.baseCritDamage);
            F(ref stats.critPerTier, cfg.critPerTier);
            F(ref stats.critDamagePerTier, cfg.critDamagePerTier);
            F(ref stats.lethalPerTier, cfg.lethalPerTier);

            F(ref stats.buffDamagePerTier, cfg.buffDamagePerTier);
            F(ref stats.buffIntervalPerTier, cfg.buffIntervalPerTier);
            F(ref stats.rapidFirePerTier, cfg.rapidFirePerTier);
            F(ref stats.xpPerTier, cfg.xpPerTier);

            F(ref stats.explosionBase, cfg.explosionBase);
            F(ref stats.explosionPerTier, cfg.explosionPerTier);
            F(ref stats.explosionRadiusBase, cfg.explosionRadiusBase);
            F(ref stats.explosionRadiusPerTier, cfg.explosionRadiusPerTier);

            F(ref stats.empBase, cfg.empBase);
            F(ref stats.empPerTier, cfg.empPerTier);
            F(ref stats.empRadiusBase, cfg.empRadiusBase);
            F(ref stats.empRadiusPerTier, cfg.empRadiusPerTier);
            F(ref stats.empDurationBase, cfg.empDurationBase);
            F(ref stats.empDurationPerTier, cfg.empDurationPerTier);

            F(ref stats.dotPerTier, cfg.dotPerTier);
            F(ref stats.slowPerTier, cfg.slowPerTier);
            F(ref stats.freezeChancePerTier, cfg.freezeChancePerTier);
            F(ref stats.freezeDuration, cfg.freezeDuration);
            F(ref stats.lethalBossPercent, cfg.lethalBossPercent);
            F(ref stats.buffCycle, cfg.buffCycle);
            F(ref stats.buffDuration, cfg.buffDuration);
            F(ref stats.buffBurstMultiplier, cfg.buffBurstMultiplier);
            F(ref stats.buffCyclePerTier, cfg.buffCyclePerTier);
            F(ref stats.buffDurationPerTier, cfg.buffDurationPerTier);
            F(ref stats.dotDuration, cfg.dotDuration);
        }

        // ---- 砖块 / 生成 ----
        BrickField field = BrickField.Instance;
        if (field != null)
        {
            F(ref field.baseHp, cfg.baseHp);
            F(ref field.hpPerWave, cfg.hpPerWave);
            F(ref field.hpPerWaveQuadratic, cfg.hpPerWaveQuadratic);
            F(ref field.bossHpMultiplier, cfg.bossHpMultiplier);
            F(ref field.spawnInterval, cfg.spawnInterval);
            F(ref field.baseSpeed, cfg.baseSpeed);
            F(ref field.speedPerWave, cfg.speedPerWave);
            F(ref field.waveInterval, cfg.waveInterval);
            F(ref field.speedRampPerSecond, cfg.speedRampPerSecond);
            F(ref field.maxSpeed, cfg.maxSpeed);
            F(ref field.maxFinalSpeed, cfg.maxFinalSpeed);
            F(ref field.lateGameBurst, cfg.lateGameBurst);
            if (cfg.lateGameWave >= 1f) { field.lateGameWave = Mathf.RoundToInt(cfg.lateGameWave); applied++; }

            // 这几个在 BrickField 里是 int
            if (cfg.targetAlive >= 0f) { field.targetAlive = Mathf.RoundToInt(cfg.targetAlive); applied++; }
            if (cfg.aliveGrowthPerWave >= 0f) { field.aliveGrowthPerWave = Mathf.RoundToInt(cfg.aliveGrowthPerWave); applied++; }
            if (cfg.maxAlive >= 0f) { field.maxAlive = Mathf.RoundToInt(cfg.maxAlive); applied++; }
            if (cfg.bossEveryWaves >= 1f) { field.bossEveryWaves = Mathf.RoundToInt(cfg.bossEveryWaves); applied++; }

            applied += ApplyBricks(field, cfg.bricks);
        }

        // ---- 经验曲线 ----
        GameManager gm = GameManager.Instance;
        if (gm != null)
        {
            if (cfg.baseXpToLevel >= 1) { gm.baseXpToLevel = cfg.baseXpToLevel; applied++; }
            if (cfg.xpGrowthPerLevel >= 0) { gm.xpGrowthPerLevel = cfg.xpGrowthPerLevel; applied++; }
            if (cfg.upgradeEveryLevels >= 1) { gm.upgradeEveryLevels = cfg.upgradeEveryLevels; applied++; }
            if (cfg.rerollCount >= 0) { gm.rerollCount = cfg.rerollCount; applied++; }
        }

        if (cfg.bossControlCooldown >= 0f)
            Brick.BossControlCooldown = cfg.bossControlCooldown;

        // 电磁圈上限 / 护盾层数与恢复间隔（同时驱动卡面文案）
        if (cfg.empTierLimits != null && cfg.empTierLimits.Length == 5)
            EmpField.TierLimits = cfg.empTierLimits;

        if (cfg.shieldCharges != null && cfg.shieldCharges.Length == 5)
            DeadlineShield.MaxCharges = cfg.shieldCharges;

        if (cfg.shieldRecharge != null && cfg.shieldRecharge.Length == 5)
        {
            float[] recharge = new float[5];
            for (int i = 0; i < 5; i++) recharge[i] = cfg.shieldRecharge[i];
            DeadlineShield.RechargeTime = recharge;
        }

        if (cfg.leaderboardLimit >= 1)
        {
            LeaderboardClient.SetDisplayLimit(cfg.leaderboardLimit);
            applied++;
        }

        Debug.Log($"[GameConfig] 已应用远程配置 v{cfg.version}，覆盖 {applied} 项（排行榜 {LeaderboardClient.DisplayLimit} 条）");
    }

    private static int ApplyBricks(BrickField field, BrickOverride[] overrides)
    {
        if (overrides == null || overrides.Length == 0) return 0;
        if (field.catalog == null) return 0;

        int count = 0;
        foreach (BrickOverride ov in overrides)
        {
            if (ov == null || string.IsNullOrEmpty(ov.type)) continue;

            foreach (BrickTypeDef def in field.catalog)
            {
                if (!string.Equals(def.type.ToString(), ov.type, System.StringComparison.OrdinalIgnoreCase)) continue;

                if (ov.hpMultiplier >= 0f) { def.hpMultiplier = ov.hpMultiplier; count++; }
                if (ov.score >= 0f) { def.score = Mathf.RoundToInt(ov.score); count++; }
                if (ov.weight >= 0f) { def.weight = ov.weight; count++; }
                if (ov.xp >= 0f) { def.xp = Mathf.RoundToInt(ov.xp); count++; }
                break;
            }
        }
        return count;
    }
}
