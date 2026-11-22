using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 【启动连接门】进游戏先挡住屏幕，显示"正在连接服务器…"，
/// 等第一次连接有结果（成功 / 失败）才放行。
/// 只会出现一次（重开一局不会重复弹）。
///
/// 层级 250：比公告(210)、设置(205)、主菜单(200)、HUD(100) 都高，
/// 全屏 Image 的 raycastTarget = true 会挡住所有点击，防止提前开始游戏。
/// </summary>
public class BootGate : MonoBehaviour
{
    private static BootGate instance;
    private static bool alreadyShown;

    private GameObject root;
    private Text statusText;
    private float elapsed;
    private bool resolved;
    private float hideCountdown;

    private const float MaxWait = 6f;      // 最多等这么久，超时按失败处理
    private const float ResultHold = 0.7f; // 结果停留时间

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (alreadyShown) return;
        if (instance != null) return;

        GameObject go = new GameObject("~BootGate");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<BootGate>();
        alreadyShown = true;
    }

    private void Awake()
    {
        instance = this;
        Build();
    }

    private void Build()
    {
        GameObject canvasGo = new GameObject("BootGateCanvas");
        canvasGo.transform.SetParent(transform, false);

        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 250;
        canvas.pixelPerfect = true;

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGo.AddComponent<GraphicRaycaster>();

        root = new GameObject("Root", typeof(RectTransform), typeof(Image));
        root.transform.SetParent(canvasGo.transform, false);
        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;

        Image dim = root.GetComponent<Image>();
        dim.color = new Color(0.015f, 0.02f, 0.04f, 1f);   // 全黑底，盖住主菜单
        dim.raycastTarget = true;

        // 标题
        NeonUi.GlowText(root.transform, "Title", "BREAK OUT", 64,
                        new Color(0.45f, 0.9f, 1f), new Vector2(0f, 120f), new Vector2(900f, 90f));

        // 状态文字
        GameObject textGo = new GameObject("Status", typeof(RectTransform), typeof(Text));
        textGo.transform.SetParent(root.transform, false);
        statusText = textGo.GetComponent<Text>();
        statusText.font = UiFont.Get();
        statusText.fontSize = 34;
        statusText.alignment = TextAnchor.MiddleCenter;
        statusText.color = new Color(0.75f, 0.88f, 1f);
        statusText.raycastTarget = false;
        statusText.text = "正在连接服务器…";

        RectTransform textRect = statusText.rectTransform;
        textRect.anchorMin = textRect.anchorMax = new Vector2(0.5f, 0.5f);
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.sizeDelta = new Vector2(900f, 60f);
        textRect.anchoredPosition = new Vector2(0f, 10f);

        // 小提示
        GameObject hintGo = new GameObject("Hint", typeof(RectTransform), typeof(Text));
        hintGo.transform.SetParent(root.transform, false);
        Text hint = hintGo.GetComponent<Text>();
        hint.font = UiFont.Get();
        hint.fontSize = 22;
        hint.alignment = TextAnchor.MiddleCenter;
        hint.color = new Color(0.45f, 0.55f, 0.65f);
        hint.raycastTarget = false;
        hint.text = "首次连接需要一两秒，失败也能离线游玩";

        RectTransform hintRect = hint.rectTransform;
        hintRect.anchorMin = hintRect.anchorMax = new Vector2(0.5f, 0.5f);
        hintRect.pivot = new Vector2(0.5f, 0.5f);
        hintRect.sizeDelta = new Vector2(900f, 40f);
        hintRect.anchoredPosition = new Vector2(0f, -60f);
    }

    private void Update()
    {
        if (resolved)
        {
            hideCountdown -= Time.unscaledDeltaTime;
            if (hideCountdown <= 0f)
            {
                root.SetActive(false);
                Destroy(gameObject);
            }
            return;
        }

        elapsed += Time.unscaledDeltaTime;

        // 等待第一次健康检查有结果
        bool checkedOnce = LeaderboardClient.HasChecked;
        if (checkedOnce || elapsed >= MaxWait)
        {
            resolved = true;
            hideCountdown = ResultHold;

            bool online = LeaderboardClient.IsOnline;
            if (online)
            {
                statusText.text = "连接成功";
                statusText.color = new Color(0.45f, 1f, 0.65f);
            }
            else
            {
                statusText.text = elapsed >= MaxWait ? "连接超时，已进入离线模式" : "连接失败，已进入离线模式";
                statusText.color = new Color(1f, 0.75f, 0.45f);
            }
        }
        else
        {
            // 简单的点点动画，表示还在等
            int dots = Mathf.FloorToInt(elapsed * 3f) % 4;
            statusText.text = "正在连接服务器" + new string('.', dots);
        }
    }
}
