using UnityEngine;

/// <summary>
/// 【砖块血条】挂在砖块上，用两个薄方块拼出来（背景 + 填充），
/// 受伤后才显示，血量比例实时更新，跟随砖块移动。
/// </summary>
public class BrickHealthBar : MonoBehaviour
{
    private const float Width = 0.78f;    // 比砖块窄一点，别糊成一条杠
    private const float Height = 0.10f;

    private Brick brick;
    private Transform fill;
    private Renderer fillRenderer;
    private Renderer bgRenderer;
    private MaterialPropertyBlock mpb;

    private static Material _barMaterial;

    private static Material BarMaterial()
    {
        if (_barMaterial == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            _barMaterial = new Material(shader) { name = "HealthBar_Runtime" };
            _barMaterial.EnableKeyword("_EMISSION");
            _barMaterial.SetFloat("_Smoothness", 0f);
        }
        return _barMaterial;
    }

    public static BrickHealthBar Attach(Brick owner)
    {
        GameObject go = new GameObject("HealthBar");
        go.transform.SetParent(owner.transform, false);
        go.transform.localPosition = new Vector3(0f, 0.62f, 0f);   // 抬高：明显浮在砖块上方

        BrickHealthBar bar = go.AddComponent<BrickHealthBar>();
        bar.brick = owner;
        bar.Build();
        return bar;
    }

    private void Build()
    {
        mpb = new MaterialPropertyBlock();

        // 背景
        GameObject bg = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Collider bgCol = bg.GetComponent<Collider>();
        if (bgCol != null) { bgCol.enabled = false; Destroy(bgCol); }
        bg.transform.SetParent(transform, false);
        bg.transform.localScale = new Vector3(Width, 0.02f, Height);
        bgRenderer = bg.GetComponent<Renderer>();
        bgRenderer.sharedMaterial = BarMaterial();
        Tint(bgRenderer, new Color(0.05f, 0.05f, 0.08f), 0f);

        // 填充（锚在左边，靠 scale.x 缩短）
        GameObject fg = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Collider fgCol = fg.GetComponent<Collider>();
        if (fgCol != null) { fgCol.enabled = false; Destroy(fgCol); }
        fg.transform.SetParent(transform, false);
        fg.transform.localScale = new Vector3(Width, 0.03f, Height * 0.7f);
        fg.transform.localPosition = new Vector3(0f, 0.01f, 0f);
        fill = fg.transform;
        fillRenderer = fg.GetComponent<Renderer>();
        fillRenderer.sharedMaterial = BarMaterial();

        gameObject.SetActive(false);
    }

    private void Tint(Renderer renderer, Color color, float emission)
    {
        renderer.GetPropertyBlock(mpb);
        mpb.SetColor("_BaseColor", color);
        mpb.SetColor("_EmissionColor", color * emission);
        renderer.SetPropertyBlock(mpb);
    }

    /// <summary>血量变化时调用（不再每帧 LateUpdate，省掉每块砖的持续开销）。</summary>
    public void Refresh()
    {
        if (brick == null || fill == null) return;

        float ratio = brick.HpRatio;
        bool show = ratio < 0.999f && !brick.IsDead;
        if (gameObject.activeSelf != show) gameObject.SetActive(show);
        if (!show) return;

        float w = Mathf.Max(0.001f, Width * ratio);
        fill.localScale = new Vector3(w, 0.03f, Height * 0.7f);
        // 让填充始终从左边开始缩短
        fill.localPosition = new Vector3(-(Width - w) * 0.5f, 0.01f, 0f);

        Color color = ratio > 0.55f ? new Color(0.4f, 1f, 0.6f)
                    : ratio > 0.25f ? new Color(1f, 0.85f, 0.3f)
                                    : new Color(1f, 0.3f, 0.35f);
        Tint(fillRenderer, color, 1.2f);

        // 相机是正上方俯视，血条本来就是平躺的薄片 —— 保持水平即可。
        // （之前用 LookRotation 朝相机会让正下方的砖块血条立起来，看起来是斜的）
        transform.rotation = Quaternion.identity;
    }
}
