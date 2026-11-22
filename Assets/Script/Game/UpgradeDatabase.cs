using System.Collections.Generic;
using UnityEngine;

/// <summary>一次三选一里的一个选项。</summary>
[System.Serializable]
public struct UpgradeOffer
{
    public SkillId Skill;
    public int Tier;        // 这一选项会升到的阶数
    public string Title;    // 显示名 + 阶级
    public string Desc;     // 效果描述
    public Color Color;     // 卡片颜色
}

/// <summary>
/// 技能表。5 阶技能满级后不再出现；爆伤和射速无上限，永远可抽。
/// </summary>
public static class UpgradeDatabase
{
    public static readonly SkillId[] AllSkills =
    {
        SkillId.MultiShot, SkillId.Pierce, SkillId.Explosion,
        SkillId.Dot, SkillId.Buff, SkillId.Emp,
        SkillId.XpGain, SkillId.RapidFire,
        SkillId.Crit, SkillId.CritDamage, SkillId.Lethal,
        SkillId.Slow, SkillId.Freeze, SkillId.Shield,
    };

    /// <summary>无上限技能：可以一直抽。</summary>
    public static bool IsUncapped(SkillId skill)
    {
        return skill == SkillId.RapidFire || skill == SkillId.CritDamage;
    }

    /// <summary>技能的两字简称（底部技能条显示用）。加新技能时在这里补一条即可。</summary>
    public static string ShortName(SkillId skill)
    {
        switch (skill)
        {
            case SkillId.MultiShot: return "多重";
            case SkillId.Pierce: return "穿透";
            case SkillId.Explosion: return "爆裂";
            case SkillId.Dot: return "腐蚀";
            case SkillId.Buff: return "强化";
            case SkillId.Emp: return "电磁";
            case SkillId.XpGain: return "经验";
            case SkillId.RapidFire: return "射速";
            case SkillId.Crit: return "暴击";
            case SkillId.CritDamage: return "爆伤";
            case SkillId.Lethal: return "致命";
            case SkillId.Slow: return "减速";
            case SkillId.Freeze: return "冰冻";
            case SkillId.Shield: return "护盾";
            default: return skill.ToString();
        }
    }
    /// <summary>每个技能的等级上限（无上限技能返回 int.MaxValue）。</summary>
    public static int MaxLevel(SkillId skill)
    {
        if (IsUncapped(skill)) return int.MaxValue;
        if (skill == SkillId.MultiShot) return 7;   // 多重子弹：5 → 7 级
        return 5;
    }

    /// <summary>随机抽 3 个可升级的技能。</summary>
    public static UpgradeOffer[] RollThree(PlayerStats stats)
    {
        List<SkillId> available = new List<SkillId>();
        foreach (SkillId skill in AllSkills)
        {
            int level = stats != null ? stats.GetLevel(skill) : 0;
            if (level < MaxLevel(skill)) available.Add(skill);
        }

        // 洗牌
        for (int i = available.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (available[i], available[j]) = (available[j], available[i]);
        }

        int count = Mathf.Min(3, available.Count);
        UpgradeOffer[] offers = new UpgradeOffer[count];
        for (int i = 0; i < count; i++)
        {
            int current = stats != null ? stats.GetLevel(available[i]) : 0;
            offers[i] = Build(available[i], current + 1, stats);
        }
        return offers;
    }

    private static string Roman(int n)
    {
        switch (n)
        {
            case 1: return "Ⅰ";
            case 2: return "Ⅱ";
            case 3: return "Ⅲ";
            case 4: return "Ⅳ";
            case 5: return "Ⅴ";
            case 6: return "Ⅵ";
            case 7: return "Ⅶ";
            case 8: return "Ⅷ";
            case 9: return "Ⅸ";
            case 10: return "Ⅹ";
            default: return "Lv." + n;
        }
    }

    private static string Label(SkillId skill, int tier)
    {
        return IsUncapped(skill) ? "Lv." + tier : Roman(tier);
    }

    public static UpgradeOffer Build(SkillId skill, int tier, PlayerStats stats = null)
    {
        UpgradeOffer offer = new UpgradeOffer { Skill = skill, Tier = tier };
        int prev = Mathf.Max(0, tier - 1);

        // 文案里的百分比一律从 PlayerStats 读 —— 这样 config.json 改了数值，卡面也跟着变
        float critStep = stats != null ? stats.critPerTier : 0.20f;
        float critDmgStep = stats != null ? stats.critDamagePerTier : 0.25f;
        float critDmgBase = stats != null ? stats.baseCritDamage : 2f;
        float lethalStep = stats != null ? stats.lethalPerTier : 0.01f;
        float buffDmg = stats != null ? stats.buffDamagePerTier : 0.25f;
        float buffSpd = stats != null ? stats.buffIntervalPerTier : 0.08f;
        float rapidStep = stats != null ? stats.rapidFirePerTier : 0.10f;
        float xpStep = stats != null ? stats.xpPerTier : 0.25f;
        float expBase = stats != null ? stats.explosionBase : 0.6f;
        float expStep = stats != null ? stats.explosionPerTier : 0.15f;
        float expRBase = stats != null ? stats.explosionRadiusBase : 1.8f;
        float expRStep = stats != null ? stats.explosionRadiusPerTier : 0.35f;
        float empBaseDps = stats != null ? stats.empBase : 0.35f;
        float empStep = stats != null ? stats.empPerTier : 0.12f;
        float empDurBase = stats != null ? stats.empDurationBase : 2f;
        float empDurStep = stats != null ? stats.empDurationPerTier : 0.5f;
        float dotStep = stats != null ? stats.dotPerTier : 0.02f;
        float dotDur = stats != null ? stats.dotDuration : 3f;
        float slowStep = stats != null ? stats.slowPerTier : 0.08f;
        float freezeStep = stats != null ? stats.freezeChancePerTier : 0.06f;
        float freezeDur = stats != null ? stats.freezeDuration : 1.2f;
        float lethalBoss = stats != null ? stats.lethalBossPercent : 0.5f;
        float buffCycleBase = stats != null ? stats.buffCycle : 8f;
        float buffDurBase = stats != null ? stats.buffDuration : 3f;
        float buffCycleStep = stats != null ? stats.buffCyclePerTier : 1f;
        float buffDurStep = stats != null ? stats.buffDurationPerTier : 0.5f;
        float buffBurst = stats != null ? stats.buffBurstMultiplier : 1.5f;

        switch (skill)
        {
            case SkillId.MultiShot:
                offer.Title = $"多重子弹 {Label(skill, tier)}";
                offer.Desc = $"每次射击发射 {tier + 1} 颗（+1）";
                offer.Color = new Color(0.35f, 0.85f, 1f);
                break;

            case SkillId.Pierce:
                offer.Title = $"穿透弹 {Label(skill, tier)}";
                offer.Desc = $"子弹可额外穿透 {tier} 个砖块";
                offer.Color = new Color(1f, 0.85f, 0.35f);
                break;

            case SkillId.Explosion:
                offer.Title = $"爆裂弹 {Label(skill, tier)}";
                offer.Desc = $"命中造成半径 {expRBase + expRStep * tier:0.0} 的范围伤害（{expBase + expStep * tier:0.00}× 伤害）";
                offer.Color = new Color(1f, 0.5f, 0.2f);
                break;

            case SkillId.Dot:
                offer.Title = $"腐蚀 {Label(skill, tier)}";
                offer.Desc = $"每秒扣目标 {tier * dotStep * 100f:0.#}% 最大生命，持续 {dotDur:0.#} 秒";
                offer.Color = new Color(0.6f, 1f, 0.4f);
                break;

            case SkillId.Buff:
                // 伤害是加算（+25%/阶），射击间隔是乘算（×0.92/阶）——分开写，别混
                float intervalCut = (1f - Mathf.Pow(1f - buffSpd, tier)) * 100f;
                offer.Title = $"强化状态 {Label(skill, tier)}";
                float cyc = Mathf.Max(2f, buffCycleBase - buffCycleStep * (tier - 1));
                float dur = buffDurBase + buffDurStep * (tier - 1);
                offer.Desc = $"【常驻】全部伤害 +{tier * buffDmg * 100f:0}%"
                           + $"\n　　　　射击间隔 -{intervalCut:0}%"
                           + $"\n【爆发】每 {cyc:0.#} 秒，持续 {dur:0.#} 秒"
                           + $"\n　　　　伤害再 ×{buffBurst:0.##}、子弹放大";
                offer.Color = new Color(1f, 0.4f, 0.8f);
                break;

            case SkillId.Emp:
                // dps 系数是 0.35 + 0.12×阶（不是 0.35×阶）
                offer.Title = $"电磁场 {Label(skill, tier)}";
                int[] caps = EmpField.TierLimits;
                int capIdx = Mathf.Clamp(tier - 1, 0, caps.Length - 1);
                offer.Desc = $"命中处电磁圈，每秒 {empBaseDps + empStep * tier:0.00}×伤害，持续 {empDurBase + empDurStep * tier:0.0} 秒；同时最多 {caps[capIdx]} 个";
                offer.Color = new Color(0.7f, 0.5f, 1f);
                break;

            case SkillId.XpGain:
                offer.Title = $"经验增幅 {Label(skill, tier)}";
                offer.Desc = $"获得经验 +{tier * xpStep * 100f:0}%";
                offer.Color = new Color(0.45f, 1f, 0.75f);
                break;

            case SkillId.RapidFire:
                // 乘算：每阶 ×0.9，所以累计不是线性的
                float totalCut = (1f - Mathf.Pow(1f - rapidStep, tier)) * 100f;
                offer.Title = $"急速射击 {Label(skill, tier)}";
                offer.Desc = $"射击间隔 ×{1f - rapidStep:0.##}（累计缩短 {totalCut:0.#}%）";
                offer.Color = new Color(1f, 0.95f, 0.4f);
                break;

            case SkillId.Crit:
                offer.Title = $"暴击 {Label(skill, tier)}";
                offer.Desc = $"暴击率 +{critStep * 100f:0}%（{prev * critStep * 100f:0}% → {tier * critStep * 100f:0}%）";
                offer.Color = new Color(1f, 0.35f, 0.35f);
                break;

            case SkillId.CritDamage:
                offer.Title = $"爆伤 {Label(skill, tier)}";
                offer.Desc = $"暴击伤害 +{critDmgStep * 100f:0}%（{critDmgBase * 100f + prev * critDmgStep * 100f:0}% → {critDmgBase * 100f + tier * critDmgStep * 100f:0}%）";
                offer.Color = new Color(1f, 0.6f, 0.2f);
                break;

            case SkillId.Lethal:
                offer.Title = $"致命一击 {Label(skill, tier)}";
                offer.Desc = $"{tier * lethalStep * 100f:0.#}% 概率直接斩杀；对 Boss 改为造成 {lethalBoss * 100f:0}% 最大生命伤害";
                offer.Color = new Color(0.85f, 0.15f, 0.45f);
                break;

            case SkillId.Slow:
                float slowTotal = Mathf.Clamp01(tier * slowStep);
                offer.Title = $"减速力场 {Label(skill, tier)}";
                offer.Desc = $"所有砖块移动速度 -{slowTotal * 100f:0.#}%\n"
                           + $"（砖块速度 ×{1f - slowTotal:0.##}）";
                offer.Color = new Color(0.5f, 0.85f, 1f);
                break;

            case SkillId.Shield:
                int[] shieldCharges = DeadlineShield.MaxCharges;
                float[] shieldRecharge = DeadlineShield.RechargeTime;
                int shieldIdx = Mathf.Clamp(tier - 1, 0, shieldCharges.Length - 1);
                int rechargeIdx = Mathf.Clamp(tier - 1, 0, shieldRecharge.Length - 1);
                offer.Title = $"失败线护盾 {Label(skill, tier)}";
                offer.Desc = $"{shieldCharges[shieldIdx]} 层护盾：砖块撞到失败线会被打碎\n"
                           + $"每 {shieldRecharge[rechargeIdx]:0.#} 秒恢复 1 层";
                offer.Color = new Color(0.45f, 0.85f, 1f);
                break;

            case SkillId.Freeze:
                offer.Title = $"冰冻弹 {Label(skill, tier)}";
                // 概率可能是 0.5% 这种小数，用 0.# 格式，否则会被四舍五入成 "0%"
                offer.Desc = $"命中时 {tier * freezeStep * 100f:0.#}% 概率把砖块冻住 {freezeDur:0.#} 秒";
                offer.Color = new Color(0.6f, 0.9f, 1f);
                break;
        }
        return offer;
    }
}
