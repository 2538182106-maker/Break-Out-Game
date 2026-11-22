using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// 【设置界面】音乐音量 / 音效音量。运行时自建，独立 Canvas（sortingOrder 205）：
/// 在主菜单与暂停菜单之上，但在更新公告（210）之下。
///
/// 打开时会全屏挡住点击，避免误触后面的按钮；关闭后不影响任何游戏状态。
/// </summary>
public class SettingsUI : MonoBehaviour
{
    private static SettingsUI instance;

    private GameObject root;
    private Slider musicSlider;
    private Slider sfxSlider;
    private Text musicValue;
    private Text sfxValue;

    public static bool IsOpen => instance != null && instance.root != null && instance.root.activeSelf;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (instance != null) return;

        GameObject go = new GameObject("~SettingsUI");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<SettingsUI>();
    }

    private void Awake()
    {
        instance = this;
        Build();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    /// <summary>
    /// 注意放在 LateUpdate：
    /// GameUI.Update 会先看到 IsOpen == true 从而跳过「暂停/继续」，
    /// 然后这里才关闭界面 —— 否则同一帧既关界面又恢复游戏。
    /// </summary>
    private void LateUpdate()
    {
        if (!IsOpen) return;

        if (UnityEngine.InputSystem.Keyboard.current != null &&
            UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Close();
        }
    }

    public static void Open()
    {
        if (instance == null) return;

        instance.root.SetActive(true);
        instance.RefreshValues();
    }

    public static void Close()
    {
        if (instance == null || instance.root == null) return;
        instance.root.SetActive(false);
    }

    // ------------------------------------------------------------------
    // 构建
    // ------------------------------------------------------------------
    private void Build()
    {
        GameObject canvasGo = new GameObject("SettingsCanvas");
        canvasGo.transform.SetParent(transform, false);

        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 205;
        canvas.pixelPerfect = true;

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGo.AddComponent<GraphicRaycaster>();

        // 全屏遮罩：挡住背后的点击
        root = new GameObject("Root", typeof(RectTransform), typeof(Image));
        root.transform.SetParent(canvasGo.transform, false);
        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;

        Image dim = root.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.72f);
        dim.raycastTarget = true;

        // 对话框
        NeonUi.Panel(root.transform, "Border", Vector2.zero, new Vector2(900f, 620f),
                     new Color(0.35f, 0.95f, 1f, 0.45f));
        NeonUi.Panel(root.transform, "Dialog", Vector2.zero, new Vector2(892f, 612f),
                     new Color(0.03f, 0.06f, 0.11f, 0.97f));

        Text title = NeonUi.GlowText(root.transform, "Title", "设置", 56,
                                     new Color(0.5f, 0.95f, 1f), new Vector2(0f, 230f), new Vector2(600f, 80f));

        // 音乐音量
        MakeLabel(root.transform, "音乐音量", new Vector2(-300f, 100f));
        musicSlider = MakeSlider(root.transform, new Vector2(0f, 100f), AudioSettings.MusicVolume, OnMusicChanged);
        musicValue = MakeValue(root.transform, new Vector2(300f, 100f));

        // 音效音量
        MakeLabel(root.transform, "音效音量", new Vector2(-300f, 0f));
        sfxSlider = MakeSlider(root.transform, new Vector2(0f, 0f), AudioSettings.SfxVolume, OnSfxChanged);
        sfxValue = MakeValue(root.transform, new Vector2(300f, 0f));

        // 关闭
        NeonUi.NeonButton(root.transform, "关闭", new Vector2(0f, -190f), new Vector2(300f, 84f),
                          new Color(0.35f, 0.95f, 1f), Close, 34);

        RefreshValues();
        root.SetActive(false);
    }

    private void RefreshValues()
    {
        if (musicSlider != null)
        {
            musicSlider.SetValueWithoutNotify(AudioSettings.MusicVolume);
            musicValue.text = $"{Mathf.RoundToInt(AudioSettings.MusicVolume * 100f)}%";
        }
        if (sfxSlider != null)
        {
            sfxSlider.SetValueWithoutNotify(AudioSettings.SfxVolume);
            sfxValue.text = $"{Mathf.RoundToInt(AudioSettings.SfxVolume * 100f)}%";
        }
    }

    private void OnMusicChanged(float value)
    {
        AudioSettings.SetMusic(value);
        if (musicValue != null) musicValue.text = $"{Mathf.RoundToInt(value * 100f)}%";
    }

    private void OnSfxChanged(float value)
    {
        AudioSettings.SetSfx(value);
        if (sfxValue != null) sfxValue.text = $"{Mathf.RoundToInt(value * 100f)}%";

        // 拖动时给个即时反馈
        if (value > 0.01f) SoundFx.Hit();
    }

    // ------------------------------------------------------------------
    // 小部件
    // ------------------------------------------------------------------
    private static Text MakeLabel(Transform parent, string content, Vector2 pos)
    {
        GameObject go = new GameObject("Label", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);

        Text text = go.GetComponent<Text>();
        text.font = UiFont.Get();
        text.text = content;
        text.fontSize = 30;
        text.alignment = TextAnchor.MiddleLeft;
        text.color = new Color(0.85f, 0.95f, 1f);
        text.raycastTarget = false;

        RectTransform rt = text.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(240f, 44f);
        rt.anchoredPosition = pos;
        return text;
    }

    private static Text MakeValue(Transform parent, Vector2 pos)
    {
        GameObject go = new GameObject("Value", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);

        Text text = go.GetComponent<Text>();
        text.font = UiFont.Get();
        text.text = "0%";
        text.fontSize = 28;
        text.alignment = TextAnchor.MiddleRight;
        text.color = new Color(0.6f, 0.85f, 1f);
        text.raycastTarget = false;

        RectTransform rt = text.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(120f, 44f);
        rt.anchoredPosition = pos;
        return text;
    }

    private static Slider MakeSlider(Transform parent, Vector2 pos, float initial, UnityAction<float> onChanged)
    {
        // 根节点必须有一张能接射线的图，否则点滑轨不会跳值（只有拖手柄有效）
        GameObject rootGo = new GameObject("Slider", typeof(RectTransform), typeof(Image), typeof(Slider));
        rootGo.transform.SetParent(parent, false);
        Image hitArea = rootGo.GetComponent<Image>();
        hitArea.color = new Color(1f, 1f, 1f, 0f);
        hitArea.raycastTarget = true;

        RectTransform rt = rootGo.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(340f, 28f);
        rt.anchoredPosition = pos;

        // 底轨
        Image track = NeonUi.Panel(rootGo.transform, "Track", Vector2.zero, new Vector2(340f, 16f),
                                   new Color(1f, 1f, 1f, 0.12f));

        // 填充
        GameObject fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fillGo.transform.SetParent(rootGo.transform, false);
        Image fill = fillGo.GetComponent<Image>();
        fill.sprite = NeonUi.Rounded(6);
        fill.type = Image.Type.Sliced;
        fill.color = new Color(0.45f, 0.9f, 1f, 0.9f);
        fill.raycastTarget = false;

        RectTransform fillRt = fillGo.GetComponent<RectTransform>();
        fillRt.anchorMin = new Vector2(0f, 0.5f);
        fillRt.anchorMax = new Vector2(1f, 0.5f);
        fillRt.sizeDelta = new Vector2(0f, 16f);
        fillRt.anchoredPosition = Vector2.zero;

        // 手柄
        GameObject handleGo = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        handleGo.transform.SetParent(rootGo.transform, false);
        Image handle = handleGo.GetComponent<Image>();
        handle.sprite = NeonUi.Rounded(12);
        handle.type = Image.Type.Sliced;
        handle.color = Color.white;

        RectTransform handleRt = handleGo.GetComponent<RectTransform>();
        handleRt.sizeDelta = new Vector2(26f, 26f);

        Slider slider = rootGo.GetComponent<Slider>();
        slider.fillRect = fillRt;
        slider.handleRect = handleRt;
        slider.targetGraphic = handle;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
        slider.value = initial;
        slider.onValueChanged.AddListener(onChanged);

        // 让点击滑块任意位置都能跳过去
        track.raycastTarget = true;

        return slider;
    }
}
