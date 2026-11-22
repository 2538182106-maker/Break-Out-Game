using System.Collections;
using UnityEngine;

/// <summary>
/// 【砖块】血量、类型、受击闪光、血条、伤害数字、死亡效果。
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class Brick : MonoBehaviour, IDamageable
{
    [Header("运行时状态（只读）")]
    public BrickType type;
    public bool isBoss;
    public float maxHp = 3f;
    public float hp = 3f;
    public float moveSpeed = 0.2f;
    public int scoreValue = 10;
    public int xpValue = 3;
    public float deadlineX = -9.35f;

    [Header("表现开关")]
    [Tooltip("受击火花：默认关闭。开着的话会被 Bloom 扩散成「命中点一小团光」，想要就勾上")]
    public bool showHitSpark = true;

    [Tooltip("受击鼓包：默认关闭。放大一块发光物体时，Bloom 光晕会跟着膨胀")]
    public bool showHitPunch = true;

    [Tooltip("伤害数字：默认关闭（用 A/B 排除法定位闪光）")]
    public bool showDamageNumber = true;

    [Tooltip("血条：默认关闭（它会在第一次受击时突然出现，也可能被误认成闪光）")]
    public bool showHealthBar = true;

    private Rigidbody rb;
    private float frozenTimer;

    /// <summary>
    /// Boss 的冰冻 / 致命一击冷却：这段时间内最多生效一次，避免被连续控死或秒杀。
    /// 可由 config.json 的 bossControlCooldown 覆盖。
    /// </summary>
    public static float BossControlCooldown = 0.5f;
    private float nextBossFreezeTime;
    private float nextBossLethalTime;
    private MeshRenderer meshRenderer;
    private MaterialPropertyBlock mpb;
    private Vector3 baseScale;
    private Color brickColor = Color.cyan;

    private float dotPerSecond;
    private float dotRemaining;
    private bool dead;
    private BrickHealthBar healthBar;

    public bool IsDead => dead;
    public float HpRatio => maxHp <= 0f ? 0f : Mathf.Clamp01(hp / maxHp);

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        meshRenderer = GetComponentInChildren<MeshRenderer>();
        mpb = new MaterialPropertyBlock();
        baseScale = transform.localScale;
    }

    public void Setup(BrickTypeDef def, float hpValue, float speed, float deadline, Color color, bool boss = false)
    {
        type = def.type;
        isBoss = boss;
        maxHp = Mathf.Max(1f, hpValue);
        hp = maxHp;
        moveSpeed = speed;
        scoreValue = boss ? def.score * 8 : def.score;
        xpValue = boss ? def.xp * 4 : def.xp;
        deadlineX = deadline;
        brickColor = color;

        baseScale = transform.localScale;
        ApplyColor(color);

        if (showHealthBar && healthBar == null) healthBar = BrickHealthBar.Attach(this);
    }

    // ---------------- 受击 ----------------
    public void TakeDamage(float amount, Vector3 hitPoint)
    {
        TakeDamage(amount, hitPoint, brickColor);
    }

    public void TakeDamage(float amount, Vector3 hitPoint, Color sourceColor)
    {
        TakeDamage(amount, hitPoint, sourceColor, false);
    }

    public void TakeDamage(float amount, Vector3 hitPoint, Color sourceColor, bool crit,
                          bool canFreeze = false, bool showNumber = true)
    {
        if (dead || amount <= 0f) return;

        hp -= amount;

        // 血条只在受伤时刷新（原来是每帧 LateUpdate）
        if (healthBar != null) healthBar.Refresh();

        // 伤害数字 +（可选）火花；暴击用金色大字
        if (showDamageNumber && showNumber) DamageNumber.Spawn(hitPoint + Vector3.up * 0.25f, amount, brickColor, crit);
        if (showHitSpark) CombatFx.HitSpark(hitPoint, crit ? 7 : 4);   // 蓝色火花
        SoundFx.Hit();

        // 卡肉：命中瞬间轻微拖慢（默认关闭）
        HitStop.Do(0.03f, 0.12f);

        if (showHitPunch) Punch();

        // 冰冻技能：只有「子弹命中」才判定（爆炸/电磁的每次结算不触发，否则会永久冻住）
        if (canFreeze)
        {
            PlayerStats st = PlayerStats.Instance;
            if (st != null && st.FreezeChance > 0f && Random.value < st.FreezeChance)
            {
                Freeze(st.freezeDuration);
            }
        }

        if (hp <= 0f) Die();
    }

    /// <summary>冻住一段时间（期间不移动）。</summary>
    public void Freeze(float duration)
    {
        if (dead) return;

        // Boss：0.5 秒内只会被冻住一次，否则多重子弹 + 高冰冻概率 = 永久控制
        if (isBoss)
        {
            if (Time.time < nextBossFreezeTime) return;
            nextBossFreezeTime = Time.time + BossControlCooldown;
        }

        frozenTimer = Mathf.Max(frozenTimer, duration);
    }

    /// <summary>
    /// 致命一击：小怪直接斩杀，Boss 扣一定比例最大生命。
    /// 返回 false 表示这次没生效（Boss 冷却中），调用方应退回普通伤害逻辑。
    /// </summary>
    public bool LethalStrike()
    {
        if (dead) return false;

        if (isBoss)
        {
            // Boss：0.5 秒内只吃一次致命一击，否则高致命率会瞬间蒸发 Boss
            if (Time.time < nextBossLethalTime) return false;
            nextBossLethalTime = Time.time + BossControlCooldown;

            float percent = PlayerStats.Instance != null ? PlayerStats.Instance.lethalBossPercent : 0.5f;
            TakeDamage(maxHp * Mathf.Clamp01(percent), transform.position, new Color(1f, 0.3f, 0.5f), true);
        }
        else
        {
            if (showDamageNumber)
                DamageNumber.Spawn(transform.position + Vector3.up * 0.3f, maxHp, new Color(1f, 0.3f, 0.5f), true);
            hp = 0f;
            Die();
        }

        return true;
    }

    public void ApplyDot(float percentPerSecond, float duration)
    {
        if (dead) return;
        dotPerSecond = Mathf.Max(dotPerSecond, percentPerSecond);
        dotRemaining = Mathf.Max(dotRemaining, duration);
    }

    private void Update()
    {
        if (dead) return;

        if (dotRemaining > 0f)
        {
            dotRemaining -= Time.deltaTime;
            hp -= dotPerSecond * maxHp * Time.deltaTime;
            if (hp <= 0f) { Die(); return; }
        }

        // 冰冻 → 不动；减速技能 → 全局降速
        if (frozenTimer > 0f) frozenTimer -= Time.deltaTime;

        float speedMul = 1f;
        if (PlayerStats.Instance != null) speedMul = PlayerStats.Instance.SlowMultiplier;
        if (frozenTimer > 0f) speedMul = 0f;

        Vector3 pos = transform.position;
        pos.x -= moveSpeed * speedMul * Time.deltaTime;
        transform.position = pos;

        if (pos.x <= deadlineX)
        {
            // 失败线有护盾 → 这块砖直接被打碎，护盾减一
            if (DeadlineShield.TryBlock())
            {
                CombatFx.Ring(transform.position, new Color(0.5f, 0.85f, 1f), 2.2f, 0.25f);
                CombatFx.Shatter(transform.position, new Color(0.5f, 0.85f, 1f), 10, transform.localScale);
                SoundFx.Kill();
                ScreenShake.Shake(0.12f, 0.2f);
                Die();
                return;
            }

            GameManager.Instance?.GameOver();
        }
    }

    // ---------------- 死亡 ----------------
    private void Die()
    {
        if (dead) return;
        dead = true;

        GameManager.Instance?.AddScore(scoreValue);

        CombatFx.Shatter(transform.position, brickColor, 16, transform.localScale);
        CombatFx.Ring(transform.position, brickColor, 1.8f, 0.22f);
        SoundFx.Kill();
        ScreenShake.Shake(0.1f, 0.14f);
        HitStop.Do(0.06f, 0.05f);      // 击杀卡肉更明显

        if (isBoss)
        {
            // Boss：更大的爆炸、更强的震屏、更多碎片
            CombatFx.Shatter(transform.position, brickColor, 30, transform.localScale);
            CombatFx.Explosion(transform.position, new Color(1f, 0.25f, 0.3f), 3.2f);
            ScreenShake.Shake(0.45f, 0.4f);
            SoundFx.Boom();
            HitStop.Do(0.08f, 0.05f);

            // 击杀 Boss 奖励一次三选一
            GameManager.Instance?.GrantBossReward();
        }
        else if (type == BrickType.Explosive)
        {
            float radius = 2.4f;
            CombatFx.Explosion(transform.position, new Color(1f, 0.15f, 0.1f), radius);
            SoundFx.Boom();
            Combat.DamageArea(transform.position, radius, maxHp * 0.35f);
            ScreenShake.Shake(0.28f, 0.25f);
        }

        if (type == BrickType.Emp)
        {
            EmpField.Spawn(transform.position, 2.4f, maxHp * 0.12f, 3f, new Color(0.3f, 0.6f, 1f));
        }

        if (type == BrickType.Split)
        {
            SpawnSplit();
        }

        if (isBoss)
        {
            BrickField.Instance?.ClearBoss(this);
        }

        for (int i = 0; i < xpValue; i++)
        {
            Vector3 offset = new Vector3(Random.Range(-0.25f, 0.25f), 0.1f, Random.Range(-0.25f, 0.25f));
            XpOrb.Spawn(transform.position + offset, 1);
        }

        Destroy(gameObject);
    }

    /// <summary>分裂砖：裂成两块 60% 大小、35% 血的小砖（小砖是普通砖，不会无限分裂）。</summary>
    private void SpawnSplit()
    {
        BrickField field = BrickField.Instance;
        if (field == null || field.BrickPrefab == null) return;

        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? -1f : 1f;
            Vector3 offset = new Vector3(0f, 0f, side * transform.localScale.z * 0.5f);

            GameObject go = Instantiate(field.BrickPrefab, transform.position + offset,
                                        Quaternion.identity, field.transform);
            go.transform.localScale = transform.localScale * 0.6f;

            Brick child = go.GetComponent<Brick>();
            if (child == null) child = go.AddComponent<Brick>();

            BrickTypeDef def = new BrickTypeDef
            {
                type = BrickType.Normal,
                color = brickColor,
                hpMultiplier = 1f,
                score = 8,
                weight = 1f,
                xp = 2,
            };

            child.Setup(def, maxHp * 0.35f, moveSpeed * 1.15f, deadlineX, brickColor);
            field.Register(child);
        }
    }

    // ---------------- 表现 ----------------
    private void ApplyColor(Color color)
    {
        if (meshRenderer == null) return;
        meshRenderer.GetPropertyBlock(mpb);
        mpb.SetColor("_BaseColor", color * 0.35f);
        mpb.SetColor("_EmissionColor", color * 2.2f);
        meshRenderer.SetPropertyBlock(mpb);
    }

    /// <summary>
    /// 受击闪光已移除。
    /// 原因：基线发光是 ×2.2，而闪光是 ×1.5，比基线还暗 → 命中瞬间先变暗再变亮，
    /// 观感就是一次「闪」。现在受击只保留鼓包（Punch）+ 火花 + 伤害数字。
    /// </summary>

    private void Punch()
    {
        if (!gameObject.activeInHierarchy) return;
        if (punchRoutine != null) StopCoroutine(punchRoutine);
        punchRoutine = StartCoroutine(PunchRoutine());
    }

    private Coroutine punchRoutine;

    private IEnumerator PunchRoutine()
    {
        float t = 0f;
        const float duration = 0.12f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = t / duration;
            float s = 1f + 0.06f * Mathf.Sin(k * Mathf.PI);
            transform.localScale = baseScale * s;
            yield return null;
        }
        transform.localScale = baseScale;
    }
}
