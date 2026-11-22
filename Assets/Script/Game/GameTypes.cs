using UnityEngine;

/// <summary>砖块类型。</summary>
public enum BrickType
{
    Normal,     // 普通：标准血量，标准分数
    Explosive,  // 爆炸：死亡时对周围造成范围伤害
    Emp,        // 电磁：死亡时原地留下一个持续伤害的电磁场
    Armored,    // 装甲：血更厚、分更高，颜色更硬
    Split,      // 分裂：死亡时裂成两块小砖
}

/// <summary>游戏状态。</summary>
public enum GameState
{
    Menu,             // 主菜单，时间暂停
    Playing,          // 正常游玩
    Paused,           // 暂停（Esc 呼出暂停菜单）
    UpgradeSelecting, // 三选一，时间暂停
    GameOver,
}

/// <summary>技能 ID。每个技能 5 阶。</summary>
public enum SkillId
{
    MultiShot,   // 多重子弹
    Pierce,      // 穿透
    Explosion,   // 爆炸范围伤害
    Dot,         // 百分比持续伤害
    Buff,        // 强化状态：伤害↑ 冷却↓
    Emp,         // 电磁：命中处留下电磁圈
    XpGain,      // 经验倍率
    RapidFire,   // 射速（无限等级）
    Crit,        // 暴击率
    CritDamage,  // 爆伤（无限等级）
    Lethal,      // 致命一击（斩杀概率）
    Slow,        // 减速：所有砖块移动变慢
    Freeze,      // 冰冻：命中概率冻住砖块
    Shield,      // 护盾：失败线可以挡砖块
}

/// <summary>
/// 砖块类型配置。在 BrickField 的 Inspector 里编辑，
/// 这样加新类型不用改代码（美术直接调颜色/血量/权重）。
/// </summary>
[System.Serializable]
public class BrickTypeDef
{
    public BrickType type = BrickType.Normal;
    public Color color = Color.cyan;
    [Tooltip("血量倍率：普通 1、装甲 2.5")]
    public float hpMultiplier = 1f;
    [Tooltip("基础分数")]
    public int score = 10;
    [Tooltip("随机权重：越大越容易出现")]
    public float weight = 1f;
    [Tooltip("经验球数量")]
    public int xp = 3;
}

/// <summary>能被打伤害的东西（目前只有砖块，以后敌人也能复用）。</summary>
public interface IDamageable
{
    void TakeDamage(float amount, Vector3 hitPoint);
    bool IsDead { get; }
}
