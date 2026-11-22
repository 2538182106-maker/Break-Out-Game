using System.Collections;
using UnityEngine;

/// <summary>
/// 【子弹】触发器判定 + 穿透 + 命中后延迟消失。
///
/// 为什么改成 Trigger：
///   之前用实体碰撞，穿透时物理引擎会把它「顶」在砖块里（运动学刚体质量无穷大）→ 卡住。
///   改成 Trigger 后子弹不参与物理阻挡，穿透就是纯逻辑：扣血 → pierceLeft-- → 继续飞。
///   同时它也不会再和别的子弹互相撞飞（子弹之间直接忽略）。
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class Bullet : MonoBehaviour
{
    [Header("飞行")]
    public float speed = 25f;
    public float lifeTime = 1.5f;

    [Header("命中")]
    [Tooltip("命中后保持可见的时间（让玩家看清打到了）")]
    public float hitHoldTime = 0.2f;

    [Tooltip("子弹颜色：火花和碎片会用它做混色")]
    public Color bulletColor = new Color(0.4f, 1f, 1f);

    private Rigidbody rb;
    private MeshRenderer meshRenderer;
    private TrailRenderer trail;
    private Vector3 baseScale;

    private float lifeTimer;
    private float damage = 10f;
    private int pierceLeft;
    private bool spent;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        meshRenderer = GetComponent<MeshRenderer>();
        trail = GetComponent<TrailRenderer>();
        baseScale = transform.localScale;

        rb.useGravity = false;
        rb.isKinematic = true;          // Trigger 判定不需要物理模拟，运动学更省
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
    }

    public void Init(float damageValue, int pierceCount)
    {
        damage = damageValue;
        pierceLeft = pierceCount;
    }

    /// <summary>按倍率缩放子弹（保留预制体原本的尺寸比例）。</summary>
    public void ApplyScaleMultiplier(float multiplier)
    {
        transform.localScale = baseScale * Mathf.Max(0.01f, multiplier);
    }

    private void OnEnable()
    {
        spent = false;
        lifeTimer = lifeTime;

        // 用 transform 自己飞（运动学刚体 + 触发器）
        transform.localScale = baseScale;
        if (meshRenderer != null) meshRenderer.enabled = true;
        if (trail != null)
        {
            trail.Clear();
            trail.emitting = true;
        }
    }

    private void Update()
    {
        if (spent) return;

        // 暂停 / 三选一（比如打死 Boss 后）时，别让子弹停在半空看起来像卡住
        if (Time.timeScale <= 0.0001f)
        {
            if (meshRenderer != null && meshRenderer.enabled) meshRenderer.enabled = false;
            return;
        }
        if (meshRenderer != null && !meshRenderer.enabled) meshRenderer.enabled = true;

        // 直线飞行
        transform.position += transform.forward * (speed * Time.deltaTime);

        lifeTimer -= Time.deltaTime;
        if (lifeTimer <= 0f) ReturnToPool();
    }

    // ------------------------------------------------------------------
    // 命中（Trigger）
    // ------------------------------------------------------------------
    private void OnTriggerEnter(Collider other)
    {
        if (spent) return;

        // 1. 忽略其它子弹（多重子弹发射瞬间不该互相判定）
        if (other.GetComponentInParent<Bullet>() != null) return;

        // 1.5 忽略所有特效/经验球/电磁圈的碰撞体。
        // 这些都是 CreatePrimitive 建出来的，Destroy(collider) 要到帧末才生效，
        // 那一帧里子弹会被当成撞墙而消耗 →「子弹凭空消失 / 卡住」的元凶。
        if (other.GetComponentInParent<FxBit>() != null
            || other.GetComponentInParent<FxSphere>() != null
            || other.GetComponentInParent<EmpField>() != null
            || other.GetComponentInParent<XpOrb>() != null)
        {
            return;
        }

        // 2. 砖块
        Brick brick = other.GetComponentInParent<Brick>();
        if (brick != null && !brick.IsDead)
        {
            Vector3 point = other.ClosestPoint(transform.position);
            PlayerStats stats = PlayerStats.Instance;

            // 致命一击优先判定：直接斩杀（Boss 改为扣 50% 最大生命）
            bool lethal = stats != null && stats.LethalChance > 0f && Random.value < stats.LethalChance;

            // Boss 的致命一击有 0.5 秒冷却：冷却中会返回 false，这时退回普通伤害
            if (lethal && brick.LethalStrike())
            {
                // 已生效
            }
            else
            {
                // 暴击判定：伤害快照 × 暴击倍率
                float dmg = damage;
                bool crit = stats != null && Random.value < stats.CritChance;
                if (crit) dmg *= stats.CritDamageMultiplier;

                brick.TakeDamage(dmg, point, bulletColor, crit, true);   // 只有子弹命中会触发冰冻
                ApplySkillEffects(brick, point);
            }

            if (pierceLeft > 0)
            {
                pierceLeft--;
                return;      // 穿透：继续飞
            }

            Spend(point);
            return;
        }

        // 3. 失败线不再阻挡子弹（让它穿过去，别浪费一发）
        if (other.transform.name.Contains("Deadline") || other.transform.root.name.Contains("Deadline"))
        {
            return;
        }

        // 4. 其它触发器（经验球、特效）直接忽略
        if (other.isTrigger) return;

        // 5. 墙 / 玩家 → 停住
        Spend(SafeClosestPoint(other));
    }

    /// <summary>
    /// 安全地取碰撞体上离子弹最近的点。
    /// 非凸 MeshCollider（比如地面）不支持 Collider.ClosestPoint，会抛异常，
    /// 那样 Spend() 就不会执行，子弹不回收 → 池子耗尽 → 打不出子弹。
    /// </summary>
    private Vector3 SafeClosestPoint(Collider other)
    {
        bool supported = other is BoxCollider
                         || other is SphereCollider
                         || other is CapsuleCollider
                         || (other is MeshCollider mesh && mesh.convex);

        if (supported) return other.ClosestPoint(transform.position);

        return other.bounds.ClosestPoint(transform.position);
    }

    private void ApplySkillEffects(Brick brick, Vector3 point)
    {
        PlayerStats stats = PlayerStats.Instance;
        if (stats == null) return;

        if (stats.DotPercentPerSecond > 0f)
            brick.ApplyDot(stats.DotPercentPerSecond, stats.DotDuration);

        if (stats.ExplosionRadius > 0f)
        {
            CombatFx.Explosion(point, new Color(1f, 0.15f, 0.1f), stats.ExplosionRadius);
            Combat.DamageArea(point, stats.ExplosionRadius, stats.ExplosionDamage);
            ScreenShake.Shake(0.12f, 0.12f);
        }

        if (stats.EmpRadius > 0f)
        {
            EmpField.Spawn(point, stats.EmpRadius, stats.EmpDamagePerSecond, stats.EmpDuration,
                           new Color(0.3f, 0.6f, 1f));
        }
    }

    /// <summary>命中后：停住、隐藏网格、拖尾淡出、延迟回收。</summary>
    private void Spend(Vector3 point)
    {
        if (spent) return;
        spent = true;

        if (meshRenderer != null) meshRenderer.enabled = false;
        if (trail != null)
        {
            // 停止发射，让已有拖尾自然淡出（不是立刻抹掉，否则又变成"没打到就消失"）
            trail.emitting = false;
        }

        StartCoroutine(ReturnAfterHold());
    }

    private IEnumerator ReturnAfterHold()
    {
        yield return new WaitForSeconds(hitHoldTime);
        ReturnToPool();
    }

    private void ReturnToPool()
    {
        if (!gameObject.activeInHierarchy) return;
        if (BulletPool.Instance != null) BulletPool.Instance.ReturnBullet(gameObject);
        else gameObject.SetActive(false);
    }
}
