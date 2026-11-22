using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 【更新公告】游戏启动时从服务器拉 /notice，弹一次公告面板。
/// 内容全部在服务器 Server/notice.json 里编辑，玩家不用更新客户端。
///
/// 只在 version 比上次看到的大时弹一次（PlayerPrefs 记录）。
/// </summary>
public class NoticeUI : MonoBehaviour
{
    [System.Serializable]
    public class Notice
    {
        public int enabled = 0;
        public int version = 0;
        public string minVersion = "";
        public string downloadUrl = "";
        public string title = "更新公告";
        public string date = "";
        public string content = "";
    }

    private const string SeenKey = "Notice.SeenVersion";

    private GameObject root;
    private Text titleText;
    private Text contentText;
    private ScrollRect contentScroll;
    private Button downloadButton;
    private string downloadUrl;
    private int currentVersion;
    private Notice lastNotice;
    private bool lastUpdateRequired;
    private bool forceNextShow;
    private static NoticeUI instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        GameObject go = new GameObject("~NoticeUI");
        DontDestroyOnLoad(go);
        go.AddComponent<NoticeUI>();
    }

    private void Awake()
    {
        instance = this;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void Start()
    {
        StartCoroutine(Fetch());
    }

    /// <summary>主菜单「公告」按钮调用：随时重新打开上一次的公告。</summary>
    public static void ShowManual()
    {
        if (instance == null) return;

        if (instance.lastNotice == null)
        {
            // 还没拉到过（比如启动时服务器不可达），重新试一次
            instance.forceNextShow = true;
            instance.StartCoroutine(instance.Fetch());
            return;
        }

        instance.Show(instance.lastNotice, instance.lastUpdateRequired);
    }

    private IEnumerator Fetch()
    {
        yield return new WaitForSecondsRealtime(0.8f);

        LeaderboardClient client = LeaderboardClient.Instance;
        if (client == null) yield break;

        // 远程玩家第一次会先试 server.txt 里的 127.0.0.1（必然失败），
        // 等健康检查把地址切到花生壳之后再重试，否则公告永远拉不到。
        for (int attempt = 0; attempt < 4; attempt++)
        {
            bool done = false;
            bool okResult = false;
            string body = null;

            client.FetchRaw("/notice", (ok, json) =>
            {
                done = true;
                okResult = ok;
                body = json;
            });

            float waited = 0f;
            while (!done && waited < 4f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            if (okResult && !string.IsNullOrEmpty(body))
            {
                HandleNotice(body);
                yield break;
            }

            // 失败：等健康检查切换地址后再试
            yield return new WaitForSecondsRealtime(2f);
        }

        Debug.LogWarning("[Notice] 多次尝试仍未拿到公告（服务器不可达）");
    }

    private void HandleNotice(string json)
    {
        if (string.IsNullOrEmpty(json)) return;

        Notice notice;
        try { notice = JsonUtility.FromJson<Notice>(json); }
        catch { return; }

        if (notice == null || notice.enabled != 1) return;

        bool updateRequired = IsOlderThan(Application.version, notice.minVersion);
        int seen = PlayerPrefs.GetInt(SeenKey, 0);

        // 已经看过这个版本、且没有强制更新 → 不再自动弹（手动点按钮除外）
        if (!forceNextShow && notice.version <= seen && !updateRequired) return;

        forceNextShow = false;

        currentVersion = notice.version;
        downloadUrl = notice.downloadUrl;
        lastNotice = notice;
        lastUpdateRequired = updateRequired;
        Show(notice, updateRequired);
    }

    private void RefreshContentHeight()
    {
        if (contentScroll == null || contentText == null) return;

        RectTransform content = contentScroll.content;
        float needed = contentText.preferredHeight + 16f;
        float viewHeight = contentScroll.viewport.rect.height;

        content.sizeDelta = new Vector2(content.sizeDelta.x, Mathf.Max(viewHeight, needed));
        contentText.rectTransform.sizeDelta = new Vector2(contentText.rectTransform.sizeDelta.x, needed);
        contentScroll.verticalNormalizedPosition = 1f;   // 回到顶部
    }

    private void Show(Notice notice, bool updateRequired)
    {
        if (root == null) Build();

        titleText.text = string.IsNullOrEmpty(notice.date)
            ? notice.title
            : $"{notice.title}　{notice.date}";

        string body = notice.content ?? "";
        if (updateRequired)
        {
            body = $"⚠ 检测到新版本（当前 {Application.version} → 需要 {notice.minVersion}）\n"
                 + "请下载最新版本后再游玩。\n\n" + body;
        }
        contentText.text = body;
        RefreshContentHeight();

        bool canDownload = updateRequired && !string.IsNullOrEmpty(downloadUrl);
        if (downloadButton != null) downloadButton.gameObject.SetActive(canDownload);

        root.SetActive(true);
        Time.timeScale = 0f;
    }

    private void Build()
    {
        GameObject canvasGo = new GameObject("NoticeCanvas");
        canvasGo.transform.SetParent(transform, false);

        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 210;
        canvas.pixelPerfect = true;   // 文字对齐像素，减少模糊

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        // 遮罩
        root = new GameObject("Root", typeof(RectTransform), typeof(Image));
        root.transform.SetParent(canvasGo.transform, false);
        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;
        root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);

        // 对话框
        NeonUi.Panel(root.transform, "Dialog", Vector2.zero, new Vector2(920f, 620f),
                     new Color(0.03f, 0.06f, 0.11f, 0.97f));
        NeonUi.Panel(root.transform, "DialogBorder", Vector2.zero, new Vector2(926f, 626f),
                     new Color(0.35f, 0.95f, 1f, 0.55f));

        titleText = NeonUi.GlowText(root.transform, "Title", "更新公告", 46,
                                    new Color(0.45f, 0.95f, 1f), new Vector2(0f, 240f), new Vector2(820f, 70f));

        // 内容区：可滚动
        GameObject scrollGo = new GameObject("ContentScroll", typeof(RectTransform), typeof(ScrollRect));
        scrollGo.transform.SetParent(root.transform, false);
        RectTransform scrollRect = scrollGo.GetComponent<RectTransform>();
        scrollRect.anchorMin = new Vector2(0.5f, 0.5f);
        scrollRect.anchorMax = new Vector2(0.5f, 0.5f);
        scrollRect.pivot = new Vector2(0.5f, 0.5f);
        scrollRect.sizeDelta = new Vector2(830f, 400f);
        scrollRect.anchoredPosition = new Vector2(0f, 10f);

        // 视口必须有一张能接收射线的图，否则滚轮/拖拽事件不会传给 ScrollRect
        GameObject viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D), typeof(Image));
        viewportGo.transform.SetParent(scrollGo.transform, false);
        RectTransform vpRect = viewportGo.GetComponent<RectTransform>();
        vpRect.anchorMin = Vector2.zero;
        vpRect.anchorMax = Vector2.one;
        vpRect.offsetMin = new Vector2(10f, 0f);
        vpRect.offsetMax = new Vector2(-10f, 0f);

        Image vpImage = viewportGo.GetComponent<Image>();
        vpImage.color = new Color(1f, 1f, 1f, 0f);   // 完全透明，但能接射线
        vpImage.raycastTarget = true;

        GameObject contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(viewportGo.transform, false);
        RectTransform contentRect = contentGo.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.offsetMin = Vector2.zero;
        contentRect.offsetMax = Vector2.zero;
        contentRect.sizeDelta = new Vector2(contentRect.sizeDelta.x, 400f);

        contentText = NeonUi.GlowText(contentGo.transform, "Content", "", 28,
                                      new Color(0.88f, 0.95f, 1f), Vector2.zero, Vector2.zero, false);
        contentText.alignment = TextAnchor.UpperLeft;
        RectTransform textRect = contentText.rectTransform;
        textRect.anchorMin = new Vector2(0f, 1f);
        textRect.anchorMax = new Vector2(1f, 1f);
        textRect.pivot = new Vector2(0.5f, 1f);
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        textRect.sizeDelta = new Vector2(textRect.sizeDelta.x, 400f);

        contentScroll = scrollGo.GetComponent<ScrollRect>();
        contentScroll.viewport = vpRect;
        contentScroll.content = contentRect;
        contentScroll.horizontal = false;
        contentScroll.vertical = true;
        contentScroll.movementType = ScrollRect.MovementType.Clamped;
        contentScroll.scrollSensitivity = 0.1f;   // 一格滚轮约 48px（120×0.4），调节手感就改这个
        contentScroll.inertia = false;
        NeonUi.AddVerticalScrollbar(contentScroll);

        downloadButton = NeonUi.NeonButton(root.transform, "去下载", new Vector2(-170f, -240f),
                                            new Vector2(260f, 84f), new Color(0.5f, 1f, 0.7f), OpenDownload, 34);

        NeonUi.NeonButton(root.transform, "知道了", new Vector2(140f, -240f), new Vector2(260f, 84f),
                          new Color(0.35f, 0.95f, 1f), Close, 34);

        root.SetActive(false);
    }

    private void OpenDownload()
    {
        if (!string.IsNullOrEmpty(downloadUrl)) Application.OpenURL(downloadUrl);
    }

    /// <summary>local 是否比 required 旧（"1.0" vs "1.1"）。任一为空则不强制。</summary>
    private static bool IsOlderThan(string local, string required)
    {
        if (string.IsNullOrWhiteSpace(required)) return false;

        string[] a = (local ?? "0").Split('.');
        string[] b = required.Split('.');

        int len = Mathf.Max(a.Length, b.Length);
        for (int i = 0; i < len; i++)
        {
            int va = i < a.Length && int.TryParse(a[i], out int pa) ? pa : 0;
            int vb = i < b.Length && int.TryParse(b[i], out int pb) ? pb : 0;
            if (va != vb) return va < vb;
        }
        return false;
    }
    private void Close()
    {
        root.SetActive(false);
        PlayerPrefs.SetInt(SeenKey, currentVersion);
        PlayerPrefs.Save();

        // 关掉公告后恢复时间（主菜单本来就暂停，交给 GameManager 决定）
        if (GameManager.Instance != null && GameManager.Instance.State == GameState.Playing)
        {
            Time.timeScale = 1f;
        }
    }
}
