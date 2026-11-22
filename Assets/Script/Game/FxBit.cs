using UnityEngine;

/// <summary>特效小碎片：抛物线飞出去 + 缩小 + 发光渐暗，寿命到了归还对象池。</summary>
public class FxBit : MonoBehaviour
{
    public Vector3 velocity;
    public float gravity = -12f;
    public float life = 0.4f;
    public bool shrink = true;
    public float spin = 0f;
    /// <summary>true = 用的是加法发光材质，淡出要改 _BaseColor</summary>
    public bool additive;

    private float t;
    private Vector3 startScale;
    private Vector3 startEuler;
    private Renderer cachedRenderer;
    private MaterialPropertyBlock mpb;

    private void Awake()
    {
        cachedRenderer = GetComponent<Renderer>();
        mpb = new MaterialPropertyBlock();
    }

    private void OnEnable()
    {
        // 每次从池里取出都重置
        t = 0f;
        startScale = transform.localScale;
        startEuler = transform.eulerAngles;
    }

    private void Update()
    {
        // 主菜单把 timeScale 设成 0，若用 deltaTime 碎片会永远停在原地
        float dt = Time.timeScale > 0.0001f ? Time.deltaTime : Time.unscaledDeltaTime;
        t += dt;
        if (t >= life)
        {
            FxPool.Release(gameObject);
            return;
        }

        velocity += Vector3.up * gravity * dt;
        transform.position += velocity * dt;

        if (spin != 0f) transform.eulerAngles = startEuler + Vector3.one * (spin * t);

        float k = t / life;
        if (shrink)
        {
            // 前 35% 保持大小（有"整块炸开"的体量感），之后快速缩没
            float sk = Mathf.Clamp01((k - 0.35f) / 0.65f);
            float s = Mathf.Max(0.001f, 1f - sk * sk);
            transform.localScale = startScale * s;
        }

        // 发光渐暗：碎片落地前"烧完"，比突然消失自然
        if (cachedRenderer != null)
        {
            float fade = Mathf.Clamp01(1f - k);
            cachedRenderer.GetPropertyBlock(mpb);

            if (additive)
            {
                Color c = mpb.GetColor("_BaseColor");
                c *= fade;
                mpb.SetColor("_BaseColor", c);
            }
            else
            {
                Color e = mpb.GetVector("_EmissionColor");
                mpb.SetColor("_EmissionColor", e * fade);
            }

            cachedRenderer.SetPropertyBlock(mpb);
        }
    }
}
