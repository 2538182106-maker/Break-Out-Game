using UnityEngine;

/// <summary>
/// 【电磁场】蓝色半透明球形力场，持续对范围内砖块造成伤害。
/// 走对象池（SimplePool）；球体只在第一次创建时生成。
///
/// 阶数限制：同时存在的数量按阶数封顶（3 / 10 / 30 / 100 / 300），超出销毁最旧的。
/// </summary>
public class EmpField : MonoBehaviour
{
    private const string PoolKey = "EmpField";

    public float radius = 2f;
    public float damagePerSecond = 1f;
    public float duration = 3f;
    public float tickInterval = 0.25f;

    private float remaining;
    private float tickTimer;
    private float t;
    private Transform ball;
    private Renderer ballRenderer;
    private Color ballColor = new Color(0.3f, 0.6f, 1f);
    private int fieldId;

    // ---- 视觉亮度封顶 ----
    /// <summary>最多同时渲染多少个电磁球（伤害照常结算）。
    /// 每个球都是半径 2 的透明球，几十个叠在一起过绘制极重。</summary>
    private const int MaxVisibleBalls = 12;

    private const int MaxVisualLayers = 3;
    private static int activeFields;

    // 每个阶数允许同时存在的电磁圈数量
    /// <summary>每个阶数允许同时存在的电磁圈数量。可由 config.json 的 empTierLimits 覆盖。</summary>
    public static int[] TierLimits = { 3, 10, 30, 100, 300 };
    private static readonly System.Collections.Generic.List<EmpField> ActiveFields =
        new System.Collections.Generic.List<EmpField>();

    // 分母封顶 10：即使有 300 个圈，每个也只压到 30%，不会集体隐身
    private static float VisualScale =>
        Mathf.Clamp01(MaxVisualLayers / (float)Mathf.Clamp(activeFields, 1, 10));

    /// <summary>当前阶数允许的最大电磁圈数量。</summary>
    public static int MaxConcurrent
    {
        get
        {
            int tier = PlayerStats.Instance != null ? PlayerStats.Instance.emp : 1;
            tier = Mathf.Clamp(tier, 1, TierLimits.Length);
            return TierLimits[tier - 1];
        }
    }

    // ------------------------------------------------------------------
    private static GameObject CreateField()
    {
        GameObject go = new GameObject("EmpField");
        EmpField field = go.AddComponent<EmpField>();

        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Collider col = sphere.GetComponent<Collider>();
        if (col != null) { col.enabled = false; Object.Destroy(col); }   // 先禁用，Destroy 当帧还生效
        sphere.transform.SetParent(go.transform, false);

        field.fieldId = go.GetInstanceID();
        field.ball = sphere.transform;
        field.ballRenderer = sphere.GetComponent<Renderer>();

        return go;
    }

    public static void Spawn(Vector3 position, float radius, float dps, float duration, Color color)
    {
        // 超过当前阶数允许的数量 → 销毁最早生成的那个
        int limit = MaxConcurrent;
        while (ActiveFields.Count >= limit && ActiveFields.Count > 0)
        {
            EmpField oldest = ActiveFields[0];
            ActiveFields.RemoveAt(0);
            if (oldest != null)
            {
                if (oldest.ball != null) oldest.ball.gameObject.SetActive(true);
                SimplePool.Release(PoolKey, oldest.gameObject);
            }
        }

        GameObject go = SimplePool.Get(PoolKey, CreateField);
        go.transform.position = new Vector3(position.x, 0f, position.z);

        EmpField field = go.GetComponent<EmpField>();
        field.radius = radius;
        field.damagePerSecond = dps;
        field.duration = duration;
        field.ballColor = color;

        field.ResetState();
        RefreshBallVisibility();
        // 错开结算帧：否则同屏几十上百个电磁圈会在同一帧集中做物理查询，产生尖峰卡顿
        field.tickTimer = Random.Range(0f, Mathf.Max(0.05f, field.tickInterval));
        ActiveFields.Add(field);
        SoundFx.Emp();
    }

    /// <summary>只让最新的 12 个电磁圈渲染球体，其余隐藏（伤害不受影响）。</summary>
    private static void RefreshBallVisibility()
    {
        int count = ActiveFields.Count;
        int firstVisible = Mathf.Max(0, count - MaxVisibleBalls);

        for (int i = 0; i < count; i++)
        {
            EmpField field = ActiveFields[i];
            if (field == null || field.ball == null) continue;

            bool visible = i >= firstVisible;
            if (field.ball.gameObject.activeSelf != visible)
                field.ball.gameObject.SetActive(visible);
        }
    }

    private void ResetState()
    {
        remaining = duration;
        tickTimer = 0f;
        t = 0f;

        if (ball != null)
        {
            // 球心压到地下 → 只露出顶部穹顶
            ball.localPosition = new Vector3(0f, -radius * 0.6f, 0f);
            ball.localScale = Vector3.one * (radius * 2f);
        }

        if (ballRenderer != null) CombatFx.TintGlow(ballRenderer, ballColor, 3f * VisualScale, 0.99f);
    }

    private void OnEnable()
    {
        activeFields++;
    }

    private void OnDisable()
    {
        activeFields = Mathf.Max(0, activeFields - 1);
        ActiveFields.Remove(this);
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        remaining -= dt;
        t += dt;

        // 先处理回收
        if (remaining <= 0f)
        {
            SimplePool.Release(PoolKey, gameObject);
            return;
        }

        tickTimer -= dt;
        if (tickTimer <= 0f)
        {
            tickTimer = tickInterval;
            // 电磁每 0.25 秒结算一次，绝对不要弹伤害数字/火花，否则后期 UI 重建会卡死
            Combat.DamageArea(transform.position, radius, damagePerSecond * tickInterval, false, false);
        }

        if (ball != null)
        {
            float pulse = 1f + Mathf.Sin(t * 5f) * 0.05f;
            ball.localScale = Vector3.one * (radius * 2f * pulse);

            if (ballRenderer == null) ballRenderer = ball.GetComponent<Renderer>();

            float fade = Mathf.Clamp01(remaining / Mathf.Max(0.01f, duration));
            float alpha = (0.32f + Mathf.Sin(t * 6f) * 0.07f) * Mathf.Min(1f, fade * 2f);
            float intensity = 2f * VisualScale;
            // 每 4 帧才写一次属性块：300 个圈 × 每帧写 MPB 是后期另一大开销
            if (ballRenderer != null && (Time.frameCount + fieldId) % 4 == 0)
                CombatFx.TintGlow(ballRenderer, ballColor, intensity, alpha);
        }
    }
}
