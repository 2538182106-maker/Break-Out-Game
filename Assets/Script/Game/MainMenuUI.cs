using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 【主菜单】运行时生成，不需要手动搭 UI。
/// 开始游戏 / 查看排行榜 / 设置玩家名。
/// </summary>
public class MainMenuUI : MonoBehaviour
{
    private Canvas canvas;
    private Canvas backgroundCanvas;
    private GameObject brickWorldRoot;
    private MenuBrickWorld brickWorld;
    private GameObject menuRoot;
    private GameObject root;
    private GameObject leaderboardPanel;
    private Text leaderboardText;
    private Text leaderboardTitle;
    private ScrollRect leaderboardScroll;
    private InputField nameInput;
    private Text playerLabel;
    private Text onlineText;
    private Font font;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (FindObjectOfType<MainMenuUI>() != null) return;
        GameObject go = new GameObject("~MainMenuUI");
        DontDestroyOnLoad(go);
        go.AddComponent<MainMenuUI>();
    }

    private void Awake()
    {
        font = Font.CreateDynamicFontFromOSFont("Microsoft YaHei", 40);
        EnsureEventSystem();
        Build();
        LeaderboardClient.OnlineChanged += OnOnlineChanged;
        Debug.Log("[MainMenu] 主菜单已创建");
    }

    private void OnDestroy()
    {
        LeaderboardClient.OnlineChanged -= OnOnlineChanged;
    }

    private void OnOnlineChanged(bool online)
    {
        RefreshOnlineLabel();
    }

    private void RefreshOnlineLabel()
    {
        if (onlineText == null) return;

        if (!LeaderboardClient.HasChecked)
        {
            onlineText.text = "● 检测中…";
            onlineText.color = new Color(0.6f, 0.65f, 0.7f);
            return;
        }

        bool online = LeaderboardClient.IsOnline;
        onlineText.text = online ? "● 排行榜在线" : "● 服务器暂时离开（离线模式）";
        onlineText.color = online ? new Color(0.45f, 1f, 0.6f) : new Color(0.6f, 0.65f, 0.7f);
    }

    private void Update()
    {
        // 场景重载 / 状态变化时自动同步显示
        GameManager gm = GameManager.Instance;
        if (gm == null || root == null) return;

        bool shouldShow = gm.State == GameState.Menu;
        if (shouldShow) RefreshOnlineLabel();
        if (menuRoot != null && menuRoot.activeSelf != shouldShow) menuRoot.SetActive(shouldShow);
        if (backgroundCanvas != null && backgroundCanvas.gameObject.activeSelf != shouldShow)
            backgroundCanvas.gameObject.SetActive(shouldShow);
        if (brickWorldRoot != null && brickWorldRoot.activeSelf != shouldShow)
            brickWorldRoot.SetActive(shouldShow);

        // 场景重载后相机换了 → 重新绑定，否则 3D 砖块/背景不再更新
        Camera cam = Camera.main;
        if (cam != null)
        {
            if (canvas != null && canvas.worldCamera != cam) canvas.worldCamera = cam;
            if (backgroundCanvas != null && backgroundCanvas.worldCamera != cam) backgroundCanvas.worldCamera = cam;
        }

        // 装饰砖块被清空（比如场景重载销毁）→ 重建
        if (shouldShow && brickWorld != null && brickWorld.Count == 0)
        {
            RebuildBrickWorld();
        }
    }

    // ------------------------------------------------------------------
    // 构建
    // ------------------------------------------------------------------
    /// <summary>用游戏内的砖块预制体做装饰：远处一批在 UI 后面，近处一批在 UI 上面。</summary>
    private void BuildBrickWorld()
    {
        brickWorldRoot = new GameObject("MenuBrickWorld");
        brickWorldRoot.transform.SetParent(transform, false);
        brickWorld = brickWorldRoot.AddComponent<MenuBrickWorld>();

        GameObject prefab = BrickField.Instance != null ? BrickField.Instance.BrickPrefab : null;
        if (prefab == null)
        {
            Debug.LogWarning("[MainMenu] 没拿到砖块预制体，用默认方块代替");
        }

        // Canvas 在 2.0：2.8 在「背景 3.8」和「UI 2.0」之间，1.2 在 UI 前面
        brickWorld.Build(prefab, 4, 8, 1.2f, 2.8f);
    }

    /// <summary>砖块被场景重载清掉后，重新生成一批。</summary>
    private void RebuildBrickWorld()
    {
        if (brickWorld == null) return;

        GameObject prefab = BrickField.Instance != null ? BrickField.Instance.BrickPrefab : null;
        brickWorld.Build(prefab, 4, 8, 1.2f, 2.8f);
        Debug.Log("[MainMenu] 装饰砖块已重建");
    }

    /// <summary>
    /// 从 Resources 载入贴图并包装成 Sprite。
    /// 不依赖 Import 设置：即使贴图是 Default 类型也能用。
    /// </summary>
    private static Sprite LoadSprite(string resourceName)
    {
        Sprite sprite = Resources.Load<Sprite>(resourceName);
        if (sprite != null) return sprite;

        Texture2D tex = Resources.Load<Texture2D>(resourceName);
        if (tex == null)
        {
            Debug.LogWarning($"[MainMenu] 找不到 Resources/{resourceName}");
            return null;
        }
        return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
    }
    private void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;

        GameObject es = new GameObject("EventSystem");
        DontDestroyOnLoad(es);
        es.AddComponent<EventSystem>();
        InputSystemUIInputModule module = es.AddComponent<InputSystemUIInputModule>();
        module.AssignDefaultActions();
    }

    private void Build()
    {
        GameObject canvasGo = new GameObject("MainMenuCanvas");
        canvasGo.transform.SetParent(transform, false);
        canvas = canvasGo.AddComponent<Canvas>();

        // 相机空间：这样 3D 砖块可以靠距离和 UI 分层（Overlay 模式下 3D 永远在最底层）
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = Camera.main;
        canvas.planeDistance = 2f;
        canvas.sortingOrder = 200;
        canvas.pixelPerfect = true;

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        // ---------- 背景：单独一层画布，距离更远（3.8）----------
        GameObject bgCanvasGo = new GameObject("MainMenuBackground");
        bgCanvasGo.transform.SetParent(transform, false);
        backgroundCanvas = bgCanvasGo.AddComponent<Canvas>();
        backgroundCanvas.renderMode = RenderMode.ScreenSpaceCamera;
        backgroundCanvas.worldCamera = Camera.main;
        backgroundCanvas.planeDistance = 3.8f;
        backgroundCanvas.sortingOrder = 90;
        backgroundCanvas.pixelPerfect = true;

        CanvasScaler bgScaler = bgCanvasGo.AddComponent<CanvasScaler>();
        bgScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        bgScaler.referenceResolution = new Vector2(1920f, 1080f);
        bgScaler.matchWidthOrHeight = 0.5f;

        Sprite bgSprite = LoadSprite("MenuBackground");
        if (bgSprite != null)
        {
            GameObject bgGo = new GameObject("Background", typeof(RectTransform), typeof(Image));
            bgGo.transform.SetParent(bgCanvasGo.transform, false);
            Stretch(bgGo.GetComponent<RectTransform>());

            Image bg = bgGo.GetComponent<Image>();
            bg.sprite = bgSprite;
            bg.color = Color.white;
            bg.raycastTarget = false;

            AspectRatioFitter fitter = bgGo.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = bgSprite.rect.width / bgSprite.rect.height;
        }

        // ---------- 容器：菜单内容 ----------
        menuRoot = new GameObject("MenuRoot", typeof(RectTransform));
        menuRoot.transform.SetParent(canvasGo.transform, false);
        Stretch(menuRoot.GetComponent<RectTransform>());

        // ---------- 装饰砖块：用游戏内预制体，分前后两层 ----------
        BuildBrickWorld();

        // 背景遮罩
        root = new GameObject("Root", typeof(RectTransform), typeof(Image));
        root.transform.SetParent(menuRoot.transform, false);
        Image dim = root.GetComponent<Image>();
        dim.color = new Color(0.02f, 0.03f, 0.06f, 0.42f);   // 压暗背景图，保证按钮文字可读
        dim.raycastTarget = false;                          // 不挡射线：否则点不到装饰砖块
        Stretch(root.GetComponent<RectTransform>());

        Sprite titleSprite = LoadSprite("MenuTitle");
        if (titleSprite != null)
        {
            GameObject titleGo = new GameObject("TitleImage", typeof(RectTransform), typeof(Image));
            titleGo.transform.SetParent(root.transform, false);
            Anchor(titleGo.GetComponent<RectTransform>(), new Vector2(0f, 300f), new Vector2(660f, 493f));

            Image img = titleGo.GetComponent<Image>();
            img.sprite = titleSprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
        }
        else
        {
            Text title = MakeText(root.transform, "Title", "BREAK OUT", 120, TextAnchor.MiddleCenter,
                                  new Color(0.5f, 0.95f, 1f));
            Anchor(title.rectTransform, new Vector2(0f, 300f), new Vector2(1400f, 160f));
        }

        NeonUi.NeonButton(root.transform, "开始游戏", new Vector2(0f, 10f), new Vector2(470f, 92f),
                          new Color(0.35f, 0.95f, 1f), StartGame, 38);
        NeonUi.NeonButton(root.transform, "排行榜", new Vector2(0f, -105f), new Vector2(470f, 92f),
                          new Color(0.72f, 0.62f, 1f), ShowLeaderboard, 38);
        NeonUi.NeonButton(root.transform, "退出游戏", new Vector2(0f, -220f), new Vector2(470f, 92f),
                          new Color(1f, 0.45f, 0.75f), QuitGame, 38);

        // 右上角：公告按钮（万一没看到公告，可以随时再打开）
        Button noticeButton = NeonUi.NeonButton(root.transform, "公告", new Vector2(-120f, -60f),
                                                new Vector2(170f, 64f), new Color(0.6f, 0.85f, 1f),
                                                () => NoticeUI.ShowManual(), 28);
        RectTransform noticeRect = noticeButton.GetComponent<RectTransform>();
        noticeRect.anchorMin = new Vector2(1f, 1f);
        noticeRect.anchorMax = new Vector2(1f, 1f);
        noticeRect.pivot = new Vector2(1f, 1f);
        noticeRect.anchoredPosition = new Vector2(-36f, -36f);

        // 设置按钮：放在公告下面
        Button settingsButton = NeonUi.NeonButton(root.transform, "设置", new Vector2(-120f, -110f),
                                                  new Vector2(170f, 64f), new Color(0.75f, 0.85f, 1f),
                                                  () => SettingsUI.Open(), 28);
        RectTransform settingsRect = settingsButton.GetComponent<RectTransform>();
        settingsRect.anchorMin = new Vector2(1f, 1f);
        settingsRect.anchorMax = new Vector2(1f, 1f);
        settingsRect.pivot = new Vector2(1f, 1f);
        settingsRect.anchoredPosition = new Vector2(-36f, -110f);

        // 左上角：服务器在线状态
        onlineText = MakeText(root.transform, "Online", "● 检测中…", 24, TextAnchor.MiddleLeft,
                              new Color(0.6f, 0.65f, 0.7f));
        Anchor(onlineText.rectTransform, Vector2.zero, new Vector2(600f, 40f));
        RectTransform onlineRect = onlineText.rectTransform;
        onlineRect.anchorMin = new Vector2(0f, 1f);
        onlineRect.anchorMax = new Vector2(0f, 1f);
        onlineRect.pivot = new Vector2(0f, 1f);
        onlineRect.anchoredPosition = new Vector2(36f, -36f);
        RefreshOnlineLabel();

        // ---------- 玩家名 ----------
        playerLabel = MakeText(root.transform, "PlayerLabel",
                               $"玩家：{LeaderboardClient.PlayerName}", 32, TextAnchor.MiddleCenter,
                               new Color(0.9f, 0.85f, 0.6f));
        Anchor(playerLabel.rectTransform, new Vector2(0f, -330f), new Vector2(900f, 44f));

        BuildNameInput();

        // ---------- 排行榜面板 ----------
        BuildLeaderboardPanel();
    }

    private void BuildNameInput()
    {
        GameObject inputGo = new GameObject("NameInput", typeof(RectTransform), typeof(Image), typeof(InputField));
        inputGo.transform.SetParent(root.transform, false);

        RectTransform rt = inputGo.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(420f, 74f);
        rt.anchoredPosition = new Vector2(-110f, -405f);

        Image bg = inputGo.GetComponent<Image>();
        bg.color = new Color(0.08f, 0.11f, 0.16f, 0.95f);

        Text text = MakeText(inputGo.transform, "Text", "", 32, TextAnchor.MiddleLeft, Color.white);
        Stretch(text.rectTransform, 18f, 6f);

        Text placeholder = MakeText(inputGo.transform, "Placeholder", "输入玩家名（最多 16 字）", 30,
                                    TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.32f));
        Stretch(placeholder.rectTransform, 18f, 6f);

        nameInput = inputGo.GetComponent<InputField>();
        nameInput.textComponent = text;
        nameInput.placeholder = placeholder;
        nameInput.targetGraphic = bg;
        nameInput.characterLimit = 16;
        nameInput.text = LeaderboardClient.PlayerName;

        NeonUi.NeonButton(root.transform, "保存", new Vector2(230f, -405f), new Vector2(160f, 74f),
                          new Color(0.5f, 1f, 0.75f), SaveName, 32);
    }

    private void BuildLeaderboardPanel()
    {
        leaderboardPanel = new GameObject("LeaderboardPanel", typeof(RectTransform), typeof(Image));
        leaderboardPanel.transform.SetParent(menuRoot.transform, false);
        Image bg = leaderboardPanel.GetComponent<Image>();
        bg.color = new Color(0.02f, 0.03f, 0.06f, 0.97f);
        bg.sprite = null;
        Stretch(leaderboardPanel.GetComponent<RectTransform>());

        leaderboardTitle = MakeText(leaderboardPanel.transform, "Title", "排行榜", 64,
                                     TextAnchor.MiddleCenter, new Color(0.5f, 0.95f, 1f));
        Anchor(leaderboardTitle.rectTransform, new Vector2(0f, 380f), new Vector2(1400f, 100f));

        // 可滚动的列表区域
        leaderboardScroll = MakeScrollView(leaderboardPanel.transform, new Vector2(0f, -20f),
                                           new Vector2(1200f, 640f), 30, out leaderboardText);

        NeonUi.NeonButton(leaderboardPanel.transform, "返回", new Vector2(0f, -400f), new Vector2(360f, 90f),
                          new Color(0.72f, 0.62f, 1f), () => leaderboardPanel.SetActive(false), 36);

        leaderboardPanel.SetActive(false);


    }

    // ------------------------------------------------------------------
    // 交互
    // ------------------------------------------------------------------
    private void StartGame()
    {
        if (menuRoot != null) menuRoot.SetActive(false);
        if (backgroundCanvas != null) backgroundCanvas.gameObject.SetActive(false);
        if (brickWorldRoot != null) brickWorldRoot.SetActive(false);
        GameManager.Instance?.BeginRun();
    }

    private void SaveName()
    {
        if (nameInput == null) return;

        LeaderboardClient.SetPlayerName(nameInput.text);
        nameInput.text = LeaderboardClient.PlayerName;
        playerLabel.text = $"玩家：{LeaderboardClient.PlayerName}";

        // 换名字 = 换一份最高分记录
        GameManager.Instance?.ReloadBest();
        SoundFx.Hit();
    }

    private void ShowLeaderboard()
    {
        leaderboardPanel.SetActive(true);

        // 标题在这里刷新：GameConfig 是启动后才拉到配置的，Awake 时还不知道真实条数
        if (leaderboardTitle != null)
            leaderboardTitle.text = $"排行榜 TOP {LeaderboardClient.DisplayLimit}";

        leaderboardText.text = "加载中…";
        RefreshScrollHeight();

        if (LeaderboardClient.Instance == null)
        {
            leaderboardText.text = "排行榜未启用";
            RefreshScrollHeight();
            return;
        }

        LeaderboardClient.Instance.FetchTop(LeaderboardClient.DisplayLimit, entries =>
        {
            if (entries == null)
            {
                leaderboardText.text = "服务器暂时离开，暂时无法获取排行榜。\n离线模式下仍可正常游戏。";
                RefreshScrollHeight();
                return;
            }
            if (entries.Length == 0)
            {
                leaderboardText.text = "暂无记录，快去当第一个吧。";
                RefreshScrollHeight();
                return;
            }

            StringBuilder sb = new StringBuilder();
            leaderboardText.fontSize = entries.Length > 16 ? 20 : entries.Length > 10 ? 24 : 30;

            for (int i = 0; i < entries.Length; i++)
            {
                LeaderboardClient.ScoreEntry e = entries[i];
                sb.Append($"#{i + 1,2}   {e.name}   {e.score} 分   (W{e.wave} L{e.level})\n");
            }
            leaderboardText.text = sb.ToString();
            RefreshScrollHeight();
        });
    }

    private void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ------------------------------------------------------------------
    // 小工具
    // ------------------------------------------------------------------
    private Text MakeText(Transform parent, string name, string content, int size,
                          TextAnchor anchor, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);

        Text text = go.GetComponent<Text>();
        text.font = font;
        text.text = content;
        text.fontSize = size;
        text.alignment = anchor;
        text.color = color;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    /// <summary>
    /// 建一个垂直滚动列表（滚轮 / 拖动）。
    /// 返回 ScrollRect，列表文本通过 out 拿。
    /// </summary>
    private ScrollRect MakeScrollView(Transform parent, Vector2 pos, Vector2 size,
                                      int fontSize, out Text contentText)
    {
        GameObject rootGo = new GameObject("ScrollView", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        rootGo.transform.SetParent(parent, false);
        Anchor(rootGo.GetComponent<RectTransform>(), pos, size);
        rootGo.GetComponent<Image>().color = new Color(0.05f, 0.07f, 0.10f, 0.55f);

        GameObject viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewportGo.transform.SetParent(rootGo.transform, false);
        RectTransform vpRect = viewportGo.GetComponent<RectTransform>();
        Stretch(vpRect);

        GameObject contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(viewportGo.transform, false);
        RectTransform contentRect = contentGo.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.offsetMin = new Vector2(24f, 0f);
        contentRect.offsetMax = new Vector2(-24f, -12f);
        contentRect.sizeDelta = new Vector2(contentRect.sizeDelta.x, size.y);

        contentText = MakeText(contentGo.transform, "List", "", fontSize,
                               TextAnchor.UpperLeft, new Color(0.85f, 0.95f, 1f));
        Stretch(contentText.rectTransform);
        contentText.verticalOverflow = VerticalWrapMode.Overflow;

        ScrollRect scroll = rootGo.GetComponent<ScrollRect>();
        scroll.viewport = vpRect;
        scroll.content = contentRect;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 0.1f;   // 一格滚轮约 48px（120×0.4），调节手感就改这个
        scroll.inertia = false;
        NeonUi.AddVerticalScrollbar(scroll);

        return scroll;
    }

    /// <summary>按文字实际高度撑开内容区，超出的部分靠滚动看。</summary>
    private void RefreshScrollHeight()
    {
        if (leaderboardScroll == null || leaderboardText == null) return;

        RectTransform content = leaderboardScroll.content;
        float needed = leaderboardText.preferredHeight + 24f;
        float viewportHeight = leaderboardScroll.viewport.rect.height;
        content.sizeDelta = new Vector2(content.sizeDelta.x, Mathf.Max(viewportHeight, needed));
        leaderboardScroll.verticalNormalizedPosition = 1f;   // 回到顶部
    }
    private Button MakeButton(Transform parent, string label, Vector2 pos, Vector2 size,                              Color color, System.Action onClick)
    {
        GameObject go = new GameObject("Btn_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;

        Image img = go.GetComponent<Image>();
        img.color = color;

        Button button = go.GetComponent<Button>();
        button.targetGraphic = img;
        button.onClick.AddListener(() => onClick());

        Text text = MakeText(go.transform, "Label", label, 38, TextAnchor.MiddleCenter, Color.white);
        Stretch(text.rectTransform);

        return button;
    }

    private static void Anchor(RectTransform rt, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
    }

    private static void Stretch(RectTransform rt, float padX = 0f, float padY = 0f)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(padX, padY);
        rt.offsetMax = new Vector2(-padX, -padY);
    }
}
