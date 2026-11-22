using UnityEngine;

/// <summary>
/// 【玩家数值】所有技能等级和推导数值集中在这里。
///
/// 伤害结构（重要）：
///   总增伤倍率 = 等级(每级 +6%) × 强化状态(每阶 +25%)
///   所有伤害（子弹/爆炸/电磁）都吃这个倍率；腐蚀是「目标最大生命百分比」，不吃。
///   暴击再乘一次暴击伤害；致命一击是独立的斩杀判定。
/// </summary>
public class PlayerStats : MonoBehaviour
{
    public static PlayerStats Instance { get; private set; }

    [Header("基础数值")]
    public float baseDamage = 10f;
    public float baseFireInterval = 0.25f;

    [Header("等级成长")]
    [Tooltip("每升 1 级：全部伤害 +6%")]
    public float damagePerLevel = 0.06f;
    [Tooltip("每升 1 级：射击间隔 -2%")]
    public float fireRatePerLevel = 0.02f;

    [Header("暴击")]
    [Tooltip("基础暴击率（0 = 必须靠技能）")]
    public float baseCritChance = 0f;
    [Tooltip("基础暴击伤害倍率（2 = 200%）")]
    public float baseCritDamage = 2f;
    [Tooltip("爆伤每阶加成（爆伤技能无限等级）")]
    public float critDamagePerTier = 0.25f;

    [Header("技能等级（0 = 未获得）")]
    [Range(0, 7)] public int multiShot;   // 多重子弹（上限 7，见 UpgradeDatabase.MaxLevel）
    [Range(0, 5)] public int pierce;      // 穿透
    [Range(0, 5)] public int explosion;   // 爆炸
    [Range(0, 5)] public int dot;         // 腐蚀
    [Range(0, 5)] public int buff;        // 强化状态
    [Range(0, 5)] public int emp;         // 电磁
    [Range(0, 5)] public int xpGain;      // 经验增幅
    public int rapidFire;                 // 射速（无限等级）
    [Range(0, 5)] public int crit;        // 暴击率 每阶 +20%
    public int critDamage;                // 爆伤（无限等级）每阶 +25%
    [Range(0, 5)] public int lethal;      // 致命一击 每阶 +1%
    [Range(0, 5)] public int slow;        // 减速：每阶砖块速度 -8%
    [Range(0, 5)] public int freeze;      // 冰冻：每阶 +6% 概率冻住砖块
    [Range(0, 5)] public int shield;      // 护盾：失败线可挡砖块

    [Header("技能系数（可由 config.json 覆盖）")]
    public float critPerTier = 0.2f;
    public float lethalPerTier = 0.01f;
    [Tooltip("致命一击打 Boss 时扣除的最大生命比例（0.5 = 50%）")]
    public float lethalBossPercent = 0.5f;

    [Header("强化状态：自动循环")]
    [Tooltip("1 阶时的冷却秒数")]
    public float buffCycle = 8f;
    [Tooltip("1 阶时的持续秒数")]
    public float buffDuration = 3f;
    [Tooltip("每多一阶，冷却减少多少秒")]
    public float buffCyclePerTier = 1f;
    [Tooltip("每多一阶，持续增加多少秒")]
    public float buffDurationPerTier = 0.5f;
    [Tooltip("强化期间额外伤害倍率")]
    public float buffBurstMultiplier = 1.5f;
    public float buffDamagePerTier = 0.25f;
    public float buffIntervalPerTier = 0.08f;
    public float rapidFirePerTier = 0.10f;
    public float xpPerTier = 0.25f;
    public float explosionBase = 0.6f;
    public float explosionPerTier = 0.15f;
    public float explosionRadiusBase = 1.8f;
    public float explosionRadiusPerTier = 0.35f;
    public float empBase = 0.35f;
    public float empPerTier = 0.12f;
    public float empRadiusBase = 1.6f;
    public float empRadiusPerTier = 0.3f;
    public float empDurationBase = 2f;
    public float empDurationPerTier = 0.5f;
    public float dotPerTier = 0.02f;
    public float dotDuration = 3f;
    public float minFireInterval = 0.05f;
    public float slowPerTier = 0.08f;
    public float freezeChancePerTier = 0.06f;
    public float freezeDuration = 1.2f;

    // ---------------- 等级相关 ----------------
    public int CurrentLevel => GameManager.Instance != null ? GameManager.Instance.Level : 1;

    /// <summary>等级带来的全伤害增伤</summary>
    public float LevelDamageMultiplier => 1f + damagePerLevel * Mathf.Max(0, CurrentLevel - 1);

    /// <summary>总增伤倍率：等级 × 强化状态</summary>
    public float TotalDamageMultiplier => LevelDamageMultiplier
                                            * (1f + buffDamagePerTier * buff)
                                            * (BuffActive ? buffBurstMultiplier : 1f);

    // ---------------- 伤害 ----------------
    /// <summary>子弹基础伤害（未算暴击）</summary>
    public float Damage => baseDamage * TotalDamageMultiplier;

    public float ExplosionDamage => explosion > 0 ? Damage * (explosionBase + explosionPerTier * explosion) : 0f;

    public float EmpDamagePerSecond => emp > 0 ? Damage * (empBase + empPerTier * emp) : 0f;

    public float ExplosionRadius => explosion > 0 ? explosionRadiusBase + explosionRadiusPerTier * explosion : 0f;
    public float DotPercentPerSecond => dot * dotPerTier;
    public float DotDuration => dotDuration;
    public float EmpRadius => emp > 0 ? empRadiusBase + empRadiusPerTier * emp : 0f;
    public float EmpDuration => emp > 0 ? empDurationBase + empDurationPerTier * emp : 0f;

    // ---------------- 射速 ----------------
    public float FireInterval
    {
        get
        {
            float levelBonus = 1f - fireRatePerLevel * Mathf.Max(0, CurrentLevel - 1);
            float skillBonus = 1f - buffIntervalPerTier * buff;
            float rapidBonus = 1f - rapidFirePerTier * rapidFire;
            return Mathf.Max(minFireInterval, baseFireInterval * levelBonus * skillBonus * rapidBonus);
        }
    }

    // ---------------- 暴击 / 斩杀 ----------------
    /// <summary>暴击率：基础 + 每阶 20%</summary>
    public float CritChance => Mathf.Clamp01(baseCritChance + crit * critPerTier);

    /// <summary>暴击伤害倍率：200% + 每阶 25%</summary>
    public float CritDamageMultiplier => baseCritDamage + critDamage * critDamagePerTier;

    /// <summary>致命一击概率：每阶 1%</summary>
    public float LethalChance => Mathf.Clamp01(lethal * lethalPerTier);

    /// <summary>砖块速度倍率（减速技能）</summary>
    public float SlowMultiplier => Mathf.Clamp(1f - slowPerTier * slow, 0.25f, 1f);

    /// <summary>命中时冻住砖块的概率</summary>
    public float FreezeChance => Mathf.Clamp01(freezeChancePerTier * freeze);

    // ---------------- 强化状态循环 ----------------
    private float buffTimer;
    private float buffRemaining;

    /// <summary>强化是否处于激活窗口</summary>
    /// <summary>当前阶数下的冷却时长（阶数越高越短）</summary>
    public float BuffCycle => Mathf.Max(2f, buffCycle - buffCyclePerTier * Mathf.Max(0, buff - 1));

    /// <summary>当前阶数下的持续时长（阶数越长越久）</summary>
    public float BuffDuration => buffDuration + buffDurationPerTier * Mathf.Max(0, buff - 1);

    public bool BuffActive => buffRemaining > 0f;

    /// <summary>冷却进度 0~1（1 = 即将触发）</summary>
    public float BuffCooldown01 => buff > 0 ? Mathf.Clamp01(buffTimer / Mathf.Max(0.01f, BuffCycle)) : 0f;

    /// <summary>激活窗口剩余比例 1~0</summary>
    public float BuffRemaining01 => Mathf.Clamp01(buffRemaining / Mathf.Max(0.01f, BuffDuration));

    private void Update()
    {
        if (buff <= 0)
        {
            buffTimer = 0f;
            buffRemaining = 0f;
            return;
        }

        if (buffRemaining > 0f)
        {
            buffRemaining -= Time.deltaTime;
            if (buffRemaining <= 0f)
            {
                buffRemaining = 0f;
                buffTimer = 0f;      // 激活结束，重新开始计冷却
            }
            return;
        }

        buffTimer += Time.deltaTime;
        if (buffTimer >= BuffCycle)
        {
            buffTimer = 0f;
            buffRemaining = BuffDuration;
        }
    }

    public int BulletCount => 1 + multiShot;
    public int PierceCount => pierce;
    public float XpMultiplier => 1f + xpPerTier * xpGain;

    private void Awake()
    {
        Instance = this;
    }

    /// <summary>升一阶。返回 false 表示已满级（爆伤/射速无上限，永远返回 true）。</summary>
    public bool Upgrade(SkillId skill)
    {
        switch (skill)
        {
            case SkillId.MultiShot: if (multiShot >= UpgradeDatabase.MaxLevel(skill)) return false; multiShot++; break;
            case SkillId.Pierce: if (pierce >= UpgradeDatabase.MaxLevel(skill)) return false; pierce++; break;
            case SkillId.Explosion: if (explosion >= UpgradeDatabase.MaxLevel(skill)) return false; explosion++; break;
            case SkillId.Dot: if (dot >= UpgradeDatabase.MaxLevel(skill)) return false; dot++; break;
            case SkillId.Buff: if (buff >= UpgradeDatabase.MaxLevel(skill)) return false; buff++; break;
            case SkillId.Emp: if (emp >= UpgradeDatabase.MaxLevel(skill)) return false; emp++; break;
            case SkillId.XpGain: if (xpGain >= UpgradeDatabase.MaxLevel(skill)) return false; xpGain++; break;
            case SkillId.RapidFire: rapidFire++; break;      // 无限
            case SkillId.Crit: if (crit >= UpgradeDatabase.MaxLevel(skill)) return false; crit++; break;
            case SkillId.CritDamage: critDamage++; break;    // 无限
            case SkillId.Lethal: if (lethal >= UpgradeDatabase.MaxLevel(skill)) return false; lethal++; break;
            case SkillId.Slow: if (slow >= UpgradeDatabase.MaxLevel(skill)) return false; slow++; break;
            case SkillId.Freeze: if (freeze >= UpgradeDatabase.MaxLevel(skill)) return false; freeze++; break;
            case SkillId.Shield: if (shield >= UpgradeDatabase.MaxLevel(skill)) return false; shield++; break;
        }
        return true;
    }

    public int GetLevel(SkillId skill)
    {
        switch (skill)
        {
            case SkillId.MultiShot: return multiShot;
            case SkillId.Pierce: return pierce;
            case SkillId.Explosion: return explosion;
            case SkillId.Dot: return dot;
            case SkillId.Buff: return buff;
            case SkillId.Emp: return emp;
            case SkillId.XpGain: return xpGain;
            case SkillId.RapidFire: return rapidFire;
            case SkillId.Crit: return crit;
            case SkillId.CritDamage: return critDamage;
            case SkillId.Lethal: return lethal;
            case SkillId.Slow: return slow;
            case SkillId.Freeze: return freeze;
            case SkillId.Shield: return shield;
            default: return 0;
        }
    }
}
