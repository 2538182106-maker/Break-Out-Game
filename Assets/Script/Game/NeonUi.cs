using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 霓虹风格 UI 工具：运行时生成圆角/描边精灵，拼出「发光 → 描边 → 底板」三层按钮。
/// 不依赖任何美术资源，9 宫格拉伸，任意尺寸都不糊。
/// </summary>
public static class NeonUi
{
    private static Sprite _ring;
    private static Sprite _rounded;
    private static readonly Dictionary<int, Sprite> _radiusCache = new Dictionary<int, Sprite>();
    private static readonly Dictionary<Color32, Sprite> _glowCache = new Dictionary<Color32, Sprite>();

    /// <summary>环形贴图（128x128，内半径 0.7），配合 Image.Filled/Radial360 做圆形进度条。</summary>
    public static Sprite Ring()
    {
        if (_ring != null) return _ring;

        const int size = 128;
        const float inner = 0.68f;
        float half = size * 0.5f;

        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.name = "NeonRing";
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        Color32[] px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - half) / half;
                float dy = (y + 0.5f - half) / half;
                float d = Mathf.Sqrt(dx * dx + dy * dy);

                float a = Mathf.Clamp01((1f - d) * half);              // 外圈抗锯齿
                a *= Mathf.Clamp01((d - inner) * half);                // 内圈抗锯齿
                px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255));
            }
        }

        tex.SetPixels32(px);
        tex.Apply();

        _ring = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        _ring.name = "NeonRing";
        return _ring;
    }

    /// <summary>圆角矩形（64x64，圆角 16，9 宫格 border 16），可任意拉伸。</summary>
    public static Sprite Rounded()
    {
        return Rounded(16);
    }

    /// <summary>指定圆角的圆角矩形（会缓存）。砖块类小圆角用 6。</summary>
    public static Sprite Rounded(int radius)
    {
        if (radius == 16 && _rounded != null) return _rounded;
        if (_radiusCache.TryGetValue(radius, out Sprite cached)) return cached;

        const int size = 64;
        radius = Mathf.Clamp(radius, 1, 30);

        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.name = "NeonRounded";
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // 到圆角矩形边界的距离 → 抗锯齿 alpha
                float d = RoundedDistance(x + 0.5f, y + 0.5f, size, size, radius);
                float a = Mathf.Clamp01(0.5f - d);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();

        Sprite sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                                      100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        sprite.name = "NeonRounded_" + radius;

        _radiusCache[radius] = sprite;
        if (radius == 16) _rounded = sprite;
        return sprite;
    }

    private static float RoundedDistance(float x, float y, float w, float h, float r)
    {
        float cx = Mathf.Clamp(x, r, w - r);
        float cy = Mathf.Clamp(y, r, h - r);
        return Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy)) - r;
    }

    /// <summary>创建一张切片圆角图（用作底板 / 描边 / 外发光）。</summary>
    public static Image Panel(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;

        Image img = go.GetComponent<Image>();
        img.sprite = Rounded();
        img.type = Image.Type.Sliced;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    /// <summary>
    /// 霓虹按钮：外发光 + 描边 + 深色底板 + 文字。
    /// 返回 Button，父节点下自动排好位置。
    /// </summary>
    public static Button NeonButton(Transform parent, string label, Vector2 pos, Vector2 size,
                                    Color accent, System.Action onClick, int fontSize = 36)
    {
        GameObject rootGo = new GameObject("Btn_" + label, typeof(RectTransform));
        rootGo.transform.SetParent(parent, false);

        RectTransform rt = rootGo.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;

        // 1) 外发光（大一圈，低透明度）
        Image glow = Panel(rootGo.transform, "Glow", Vector2.zero, size + new Vector2(18f, 18f),
                           new Color(accent.r, accent.g, accent.b, 0.16f));

        // 2) 描边（大 3px，高透明度）
        Image border = Panel(rootGo.transform, "Border", Vector2.zero, size + new Vector2(4f, 4f),
                             new Color(accent.r, accent.g, accent.b, 0.85f));

        // 3) 底板（深色半透明，保证文字可读）
        Image body = Panel(rootGo.transform, "Body", Vector2.zero, size,
                           new Color(0.05f, 0.08f, 0.12f, 0.92f));
        body.raycastTarget = true;   // 只让底板接收点击

        Button button = rootGo.AddComponent<Button>();
        button.targetGraphic = body;
        button.transition = Selectable.Transition.ColorTint;

        ColorBlock colors = button.colors;
        colors.normalColor = new Color(1f, 1f, 1f, 1f);
        colors.highlightedColor = new Color(1.35f, 1.35f, 1.35f, 1f);
        colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
        colors.selectedColor = new Color(1f, 1f, 1f, 1f);
        colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        button.onClick.AddListener(() => onClick());

        // 4) 文字
        Text text = MakeText(rootGo.transform, "Label", label, fontSize, accent);
        RectTransform trt = text.rectTransform;
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero;
        trt.offsetMax = Vector2.zero;

        return button;
    }

    /// <summary>给 ScrollRect 加一条竖向滑条（贴右侧、圆角、霓虹色）。</summary>
    public static Scrollbar AddVerticalScrollbar(ScrollRect scroll, float width = 10f)
    {
        if (scroll == null) return null;

        GameObject trackGo = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
        trackGo.transform.SetParent(scroll.transform, false);

        RectTransform rt = trackGo.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 0f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.offsetMin = new Vector2(-width - 2f, 4f);
        rt.offsetMax = new Vector2(-2f, -4f);

        Image track = trackGo.GetComponent<Image>();
        track.sprite = Rounded(5);
        track.type = Image.Type.Sliced;
        track.color = new Color(1f, 1f, 1f, 0.08f);

        GameObject handleGo = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        handleGo.transform.SetParent(trackGo.transform, false);

        RectTransform handleRt = handleGo.GetComponent<RectTransform>();
        handleRt.anchorMin = new Vector2(0f, 0f);
        handleRt.anchorMax = new Vector2(1f, 1f);
        handleRt.offsetMin = new Vector2(1f, 1f);
        handleRt.offsetMax = new Vector2(-1f, -1f);

        Image handle = handleGo.GetComponent<Image>();
        handle.sprite = Rounded(5);
        handle.type = Image.Type.Sliced;
        handle.color = new Color(0.45f, 0.9f, 1f, 0.80f);

        Scrollbar bar = trackGo.GetComponent<Scrollbar>();
        bar.direction = Scrollbar.Direction.BottomToTop;
        bar.handleRect = handleRt;
        bar.targetGraphic = handle;
        bar.transition = Selectable.Transition.ColorTint;

        ColorBlock colors = bar.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.4f, 1.4f, 1.4f, 1f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        bar.colors = colors;

        scroll.verticalScrollbar = bar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

        return bar;
    }

    /// <summary>
    /// 给一段文字加轻微外发光（用 Outline 模拟）。
    /// outline = false 时完全不加描边 —— 小字号正文务必关掉，否则会糊。
    /// </summary>
    public static Text GlowText(Transform parent, string name, string content, int size,
                                Color color, Vector2 pos, Vector2 rectSize, bool outline = true)
    {
        Text text = MakeText(parent, name, content, size, color);
        RectTransform rt = text.rectTransform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = rectSize;
        rt.anchoredPosition = pos;

        if (outline)
        {
            Outline fx = text.gameObject.AddComponent<Outline>();
            fx.effectColor = new Color(color.r, color.g, color.b, 0.30f);
            fx.effectDistance = new Vector2(1f, -1f);   // 1.5 → 1，描边更细更清晰
        }
        return text;
    }

    private static Text MakeText(Transform parent, string name, string content, int size, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);

        Text text = go.GetComponent<Text>();
        text.font = UiFont.Get();
        text.text = content;
        text.fontSize = size;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = color;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }
}

/// <summary>全局字体（微软雅黑，动态字体，支持中文）。</summary>
public static class UiFont
{
    private static Font _font;

    public static Font Get()
    {
        if (_font == null)
        {
            _font = Font.CreateDynamicFontFromOSFont("Microsoft YaHei", 40);
        }
        return _font;
    }
}
