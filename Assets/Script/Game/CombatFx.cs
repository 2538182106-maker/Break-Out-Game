using UnityEngine;

/// <summary>
/// 【特效】火花、碎片、冲击波、爆炸。
/// 火花/碎片走对象池（FxPool），运行时零 Instantiate/Destroy。
/// 发光值刻意压在 Bloom 阈值(1.11)以下 —— 数量多但不炸成白团。
/// </summary>
public static class CombatFx
{
    private static Material _solidMaterial;
    private static Material _glowMaterial;

    // 复用同一个 PropertyBlock，避免每次 new（连续射击时是 GC 大户）
    private static readonly MaterialPropertyBlock SharedBlock = new MaterialPropertyBlock();

    /// <summary>不透明自发光材质（碎片、火花）。优先用 Resources 里的资源，打包后不会丢。</summary>
    public static Material FxMaterial()
    {
        if (_solidMaterial == null)
        {
            _solidMaterial = Resources.Load<Material>("FX_Solid");
            if (_solidMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) shader = Shader.Find("Unlit/Color");

                if (shader == null)
                {
                    Debug.LogError("[CombatFx] 找不到可用 shader，碎片特效会显示为粉色");
                    return null;
                }

                _solidMaterial = new Material(shader) { name = "FX_Solid_Runtime" };
                _solidMaterial.EnableKeyword("_EMISSION");
                _solidMaterial.SetFloat("_Smoothness", 0.2f);
                _solidMaterial.SetColor("_EmissionColor", Color.white);
            }
        }
        return _solidMaterial;
    }

    /// <summary>透明加法发光材质（爆炸球、电磁场）。</summary>
    public static Material GlowMaterial()
    {
        if (_glowMaterial == null)
        {
            _glowMaterial = Resources.Load<Material>("FX_Glow");
            if (_glowMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Unlit/Color");

                if (shader == null)
                {
                    Debug.LogError("[CombatFx] 找不到可用 shader，爆炸球会显示为粉色");
                    return null;
                }

                _glowMaterial = new Material(shader) { name = "FX_Glow_Runtime" };
                _glowMaterial.SetFloat("_Surface", 1f);
                _glowMaterial.SetFloat("_Blend", 2f);
                _glowMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _glowMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
                _glowMaterial.SetFloat("_ZWrite", 0f);
                _glowMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                _glowMaterial.SetOverrideTag("RenderType", "Transparent");
                _glowMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                _glowMaterial.SetColor("_BaseColor", Color.white);
            }
        }
        return _glowMaterial;
    }

    public static void TintSolid(Renderer renderer, Color color, float emission)
    {
        if (renderer == null) return;

        Material mat = FxMaterial();
        if (mat == null) return;

        renderer.sharedMaterial = mat;

        renderer.GetPropertyBlock(SharedBlock);
        SharedBlock.SetColor("_BaseColor", color * 0.5f);
        SharedBlock.SetColor("_EmissionColor", color * emission);
        renderer.SetPropertyBlock(SharedBlock);
    }

    public static void TintGlow(Renderer renderer, Color color, float intensity, float alpha)
    {
        if (renderer == null) return;

        Material mat = GlowMaterial();
        if (mat == null) return;

        renderer.sharedMaterial = mat;

        renderer.GetPropertyBlock(SharedBlock);
        Color c = color * intensity;
        c.a = alpha;
        SharedBlock.SetColor("_BaseColor", c);
        renderer.SetPropertyBlock(SharedBlock);
    }

    /// <summary>从池里借一个方块并配置好。</summary>
    private static FxBit CreateBit(Vector3 position, Vector3 scale, Color color, float emission,
                                   bool additive = false)
    {
        GameObject go = FxPool.Get(position, scale, color, emission);
        if (go == null) return null;

        FxBit bit = go.GetComponent<FxBit>();

        if (additive)
        {
            // 换成透明加法材质：一定发光，不受光照/材质设置影响
            Renderer r = go.GetComponent<Renderer>();
            if (r != null)
            {
                r.sharedMaterial = GlowMaterial();
                TintGlow(r, color, Mathf.Max(1.2f, emission), 0.85f);
            }
            if (bit != null) bit.additive = true;
        }
        else if (bit != null)
        {
            bit.additive = false;
        }

        return bit;
    }

    // ------------------------------------------------------------------
    // 火花：砖块色 + 子弹色混着爆（亮但不过曝）
    // ------------------------------------------------------------------
    public static void Spark(Vector3 position, Color brickColor, Color bulletColor, int count)
    {
        for (int i = 0; i < count; i++)
        {
            Color c = i % 2 == 0 ? brickColor : bulletColor;
            float size = Random.Range(0.035f, 0.075f);

            FxBit fx = CreateBit(position, Vector3.one * size, c, 0.8f);
            if (fx == null) return;

            fx.velocity = Random.onUnitSphere * Random.Range(2f, 5f) + Vector3.up * 1f;
            fx.gravity = -11f;
            fx.life = Random.Range(0.12f, 0.24f);
            fx.shrink = true;
            fx.spin = 0f;
        }
    }

    /// <summary>
    /// 命中火花：蓝色小火星，从命中点向外炸开。
    /// 发光刻意压在 Bloom 阈值(1.11)以下，不会糊成一团白。
    /// </summary>
    public static void HitSpark(Vector3 position, int count = 4)
    {
        Color blue = new Color(0.45f, 0.82f, 1f);

        for (int i = 0; i < count; i++)
        {
            float size = Random.Range(0.03f, 0.065f);
            FxBit fx = CreateBit(position, Vector3.one * size, blue, 0.95f);
            if (fx == null) return;

            fx.velocity = Random.onUnitSphere * Random.Range(2.5f, 6f) + Vector3.up * 1.2f;
            fx.gravity = -12f;
            fx.life = Random.Range(0.14f, 0.26f);
            fx.shrink = true;
            fx.spin = 0f;
        }
    }
    public static void Spark(Vector3 position, Color color, int count)
    {
        Spark(position, color, color, count);
    }

    // ------------------------------------------------------------------
    // 碎片：击杀时炸开（数量多、但每块发光很低）
    // ------------------------------------------------------------------
    public static void Shatter(Vector3 position, Color color, int count, Vector3 brickScale, float emission = 1f)
    {
        for (int i = 0; i < count; i++)
        {
            Vector3 size = new Vector3(
                brickScale.x * Random.Range(0.14f, 0.30f),
                brickScale.y * Random.Range(0.18f, 0.38f),
                brickScale.z * Random.Range(0.14f, 0.30f));

            Vector3 offset = new Vector3(
                Random.Range(-0.30f, 0.30f) * brickScale.x,
                Random.Range(-0.15f, 0.45f) * brickScale.y,
                Random.Range(-0.30f, 0.30f) * brickScale.z);

            FxBit fx = CreateBit(position + offset, size, color, emission);
            if (fx == null) return;

            fx.velocity = new Vector3(
                Random.Range(-1.4f, 1.0f),
                Random.Range(1.2f, 3.6f),
                Random.Range(-1.4f, 1.4f));
            fx.gravity = -14f;
            fx.life = Random.Range(0.5f, 1.0f);
            fx.shrink = true;
            fx.spin = Random.Range(-360f, 360f);
        }
    }

    // ------------------------------------------------------------------
    // 冲击波（低亮度，不会炸成光团）
    // ------------------------------------------------------------------
    /// <summary>发光碎片（加法材质）：主菜单这类需要"一定看得见"的场合用。</summary>
    public static void ShatterGlow(Vector3 position, Color color, int count, Vector3 brickScale)
    {
        for (int i = 0; i < count; i++)
        {
            // 碎片约占砖块的 1/3~1/2，才有"整块炸开"的感觉
            Vector3 size = new Vector3(
                brickScale.x * Random.Range(0.34f, 0.52f),
                brickScale.y * Random.Range(0.34f, 0.55f),
                brickScale.z * Random.Range(0.34f, 0.52f));

            Vector3 offset = new Vector3(
                Random.Range(-0.30f, 0.30f) * brickScale.x,
                Random.Range(-0.15f, 0.45f) * brickScale.y,
                Random.Range(-0.30f, 0.30f) * brickScale.z);

            FxBit fx = CreateBit(position + offset, size, color, 2.5f, true);
            if (fx == null) return;

            fx.velocity = new Vector3(Random.Range(-1.4f, 1.0f),
                                      Random.Range(1.2f, 3.6f),
                                      Random.Range(-1.4f, 1.4f));
            fx.gravity = -14f;
            fx.life = Random.Range(0.5f, 1.0f);
            fx.shrink = true;
            fx.spin = Random.Range(-360f, 360f);
        }
    }
    public static void Ring(Vector3 position, Color color, float radius, float duration)
    {
        FxSphere.Spawn(position, color, radius, duration, 1.2f, 0.5f);
    }

    // ------------------------------------------------------------------
    // 爆炸：球形冲击 + 碎片
    // ------------------------------------------------------------------
    public static void Explosion(Vector3 position, Color color, float radius)
    {
        position.y = Mathf.Max(position.y, 0.35f);
        FxSphere.Spawn(position, color, radius, 0.45f, 3f, 0.7f);
        Shatter(position, color, 16, Vector3.one * radius * 0.3f);
    }
}
