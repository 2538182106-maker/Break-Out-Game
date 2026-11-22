using UnityEngine;

/// <summary>
/// 【球形发光特效】爆炸球 / 冲击波共用。
/// 走对象池（SimplePool），不再每次生成/销毁。
/// </summary>
public class FxSphere : MonoBehaviour
{
    private const string PoolKey = "FxSphere";

    public float fromRadius = 0.2f;
    public float toRadius = 2f;
    public float duration = 0.35f;
    public float pulseSpeed = 0f;
    public Color color = Color.red;
    public float intensity = 6f;
    public float startAlpha = 0.85f;

    private float t;
    private Renderer cachedRenderer;

    // ---- 亮度封顶：连锁爆炸时多个球叠加不会糊成白屏 ----
    private const int MaxVisualLayers = 3;
    private static int activeCount;

    private static float VisualScale =>
        Mathf.Clamp01(MaxVisualLayers / (float)Mathf.Clamp(activeCount, 1, 8));

    private void OnEnable()
    {
        activeCount++;
    }

    private void OnDisable()
    {
        activeCount = Mathf.Max(0, activeCount - 1);
    }

    private static GameObject CreateSphere()
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Collider col = go.GetComponent<Collider>();
        if (col != null) { col.enabled = false; Object.Destroy(col); }   // 先禁用，Destroy 当帧还生效
        go.name = "FxSphere";

        FxSphere fx = go.AddComponent<FxSphere>();
        fx.cachedRenderer = go.GetComponent<Renderer>();
        return go;
    }

    public static FxSphere Spawn(Vector3 position, Color color, float radius,
                                 float duration, float intensity, float alpha = 0.85f)
    {
        GameObject go = SimplePool.Get(PoolKey, CreateSphere);

        FxSphere fx = go.GetComponent<FxSphere>();
        if (fx.cachedRenderer == null) fx.cachedRenderer = go.GetComponent<Renderer>();

        fx.toRadius = radius;
        fx.duration = duration;
        fx.color = color;
        fx.intensity = intensity;
        fx.startAlpha = alpha;
        fx.t = 0f;

        go.transform.position = position;
        go.transform.localScale = Vector3.one * 0.2f;

        return fx;
    }

    private void Update()
    {
        // 主菜单 timeScale = 0 时也要能播（否则冲击波会卡在原地）
        float dt = Time.timeScale > 0.0001f ? Time.deltaTime : Time.unscaledDeltaTime;
        t += dt;
        float k = Mathf.Clamp01(t / Mathf.Max(0.01f, duration));

        // 先处理回收：即使渲染那边出错，也绝不留一个球挂在场景里
        if (k >= 1f)
        {
            SimplePool.Release(PoolKey, gameObject);
            return;
        }

        float radius = Mathf.Lerp(fromRadius, toRadius, 1f - (1f - k) * (1f - k));
        if (pulseSpeed > 0f) radius *= 1f + Mathf.Sin(t * pulseSpeed) * 0.06f;

        // 球体默认直径 1，scale 乘 2 才能让表面等于半径
        transform.localScale = Vector3.one * (radius * 2f);

        if (cachedRenderer == null) cachedRenderer = GetComponent<Renderer>();

        float alpha = startAlpha * (1f - k * k);
        // 同时存在越多，每个越暗（最多 3 层亮度）
        if (cachedRenderer != null) CombatFx.TintGlow(cachedRenderer, color, intensity * VisualScale, alpha);
    }
}
