using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 【性能面板】F1 开关。复盘卡顿时用：直接看到 FPS、子弹池、砖块数、电磁圈数等。
/// 只在面板显示时每 0.5 秒采样一次，平时零开销。
/// </summary>
public class PerfHud : MonoBehaviour
{
    private static PerfHud instance;

    private GameObject root;
    private Text label;

    private float timer;
    private float fps = 60f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (instance != null) return;

        GameObject go = new GameObject("~PerfHud");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<PerfHud>();
    }

    private void Awake()
    {
        instance = this;
        Build();
    }

    private void Build()
    {
        GameObject canvasGo = new GameObject("PerfCanvas");
        canvasGo.transform.SetParent(transform, false);

        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 300;          // 最上层，任何面板都盖不住
        canvas.pixelPerfect = true;

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject bg = new GameObject("Bg", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(canvasGo.transform, false);
        RectTransform bgRect = bg.GetComponent<RectTransform>();
        bgRect.anchorMin = new Vector2(0f, 1f);
        bgRect.anchorMax = new Vector2(0f, 1f);
        bgRect.pivot = new Vector2(0f, 1f);
        bgRect.sizeDelta = new Vector2(420f, 210f);
        bgRect.anchoredPosition = new Vector2(12f, -12f);
        Image bgImage = bg.GetComponent<Image>();
        bgImage.color = new Color(0f, 0f, 0f, 0.72f);
        bgImage.raycastTarget = false;

        GameObject textGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
        textGo.transform.SetParent(bg.transform, false);
        label = textGo.GetComponent<Text>();
        label.font = UiFont.Get();
        label.fontSize = 22;
        label.alignment = TextAnchor.UpperLeft;
        label.color = new Color(0.7f, 1f, 0.85f);
        label.raycastTarget = false;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;

        RectTransform textRect = label.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10f, 8f);
        textRect.offsetMax = new Vector2(-10f, -8f);

        root = canvasGo;
        root.SetActive(false);
    }

    private void Update()
    {
        if (UnityEngine.InputSystem.Keyboard.current != null &&
            UnityEngine.InputSystem.Keyboard.current.f1Key.wasPressedThisFrame)
        {
            root.SetActive(!root.activeSelf);
        }

        if (!root.activeSelf) return;

        float dt = Time.unscaledDeltaTime;
        if (dt > 0.0001f) fps = Mathf.Lerp(fps, 1f / dt, 0.15f);

        timer -= dt;
        if (timer > 0f) return;
        timer = 0.5f;

        int brickCount = FindObjectsOfType<Brick>().Length;
        int empCount = FindObjectsOfType<EmpField>().Length;
        int fxCount = FindObjectsOfType<FxBit>().Length;
        int bulletCount = FindObjectsOfType<Bullet>().Length;

        label.text = $"FPS {fps:0}   ({1000f / Mathf.Max(1f, fps):0.0} ms)\n"
                   + $"子弹 {bulletCount}\n"
                   + $"砖块 {brickCount}   电磁圈 {empCount}\n"
                   + $"特效方块 {fxCount}\n"
                   + $"时间缩放 {Time.timeScale:0.##}";
    }
}
