using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 【UI】全部用代码创建（Canvas + uGUI），不需要在场景里手搭。
/// 包含：分数 / 等级 / 波次 / 经验条 / 技能一览 / 三选一卡片 / 结算面板。
///
/// 选择方式：键盘 1 / 2 / 3（避免依赖 EventSystem 的鼠标模块）。
/// 想改成鼠标点击，加 EventSystem + InputSystemUIInputModule 再给卡片加 Button 即可。
/// </summary>
public class GameUI : MonoBehaviour
{
    private static Font _font;

    // HUD
    private Text scoreText;
    private Text levelText;
    private Text waveText;
    private Text skillText;
    private Image xpFill;
    private RectTransform xpFillRect;
    private Text xpHint;

    // 三选一
    private GameObject upgradeRoot;
    private Text upgradeTitle;
    private readonly Text[] cardTitle = new Text[3];
    private readonly Text[] cardDesc = new Text[3];
    private readonly Image[] cardBg = new Image[3];
    private readonly Image[] cardGlow = new Image[3];
    private readonly Image[] cardBorder = new Image[3];
    private readonly RectTransform[] cardRect = new RectTransform[3];
    private UpgradeOffer[] offers;

    // 提示条（选完卡后显示 1.4 秒）
    private Text toast;
    private float toastTimer;
    private GameObject shieldBadge;
    private Text shieldText;
    private GameObject buffGauge;
    private Image buffFill;
    private Text buffLabel;
    private Button rerollButton;
    private Text rerollButtonLabel;
    private RectTransform canvasRect;

    // 最高分 / Boss 血条 / 横幅
    private Text bestText;
    private GameObject bossRoot;
    private Text bossLabel;
    private Image bossFill;
    private RectTransform bossFillRect;
    private Text banner;
    private float bannerTimer;

    // 联网排行榜
    private Text leaderboardText;
    private ScrollRect leaderboardScroll;
    private Text submitStatus;
    private Text onlineText;

    // 暂停菜单
    private GameObject pauseRoot;

    /// <summary>三选一面板当前是否真的显示着（看门狗用来判断是否卡死）</summary>
    public static bool IsUpgradePanelVisible { get; private set; }

    // 结算
    private GameObject gameOverRoot;
    private Text gameOverInfo;

    private GameManager subscribed;

    private Canvas canvas;
    private RectTransform uiRoot;

    private void Awake()
    {
        Build();
        StartCoroutine(FitUiOnce());
        LeaderboardClient.OnlineChanged += OnOnlineChanged;
    }

    private void OnDestroy()
    {
        LeaderboardClient.OnlineChanged -= OnOnlineChanged;
    }

    private void OnOnlineChanged(bool online)
    {
        RefreshOnlineLabel();
    }

    /// <summary>按当前状态刷新右下角标签（重开一局时也会调用）</summary>
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

    /// <summary>
    /// 保险：如果实际画布尺寸和设计分辨率（1920×1080）差得多，把整个 UI 缩放一次，
    /// 保证三张卡片不会跑到屏幕外面去。正常分辨率下 scale 是 1，不会做任何事。
    /// </summary>
    private System.Collections.IEnumerator FitUiOnce()
    {
        yield return null;   // 等 CanvasScaler 算完

        if (canvas == null || uiRoot == null) yield break;

        RectTransform canvasRect = canvas.transform as RectTransform;
        if (canvasRect == null) yield break;

        float sx = canvasRect.rect.width / 1920f;
        float sy = canvasRect.rect.height / 1080f;
        float scale = Mathf.Min(sx, sy);

        // 只缩小、不放大：画布比设计分辨率大时保持原尺寸（放大反而会溢出）
        scale = Mathf.Min(1f, scale);

        if (scale > 0.05f && Mathf.Abs(scale - 1f) > 0.05f)
        {
            uiRoot.localScale = Vector3.one * scale;
        }
    }

    private void Update()
    {
        // GameManager 可能比 UI 后创建，延迟订阅
        if (subscribed == null && GameManager.Instance != null)
        {
            subscribed = GameManager.Instance;
            subscribed.OnChanged += Refresh;
            subscribed.OnUpgradeRequested += ShowUpgrades;
            subscribed.OnGameOver += ShowGameOver;
            subscribed.OnWaveChanged += OnWaveChanged;
            subscribed.OnBossSpawned += OnBossSpawned;
            Refresh();
        }

        if (Keyboard.current == null) return;

        // Esc：游玩中暂停 / 暂停中继续
        GameManager gm = GameManager.Instance;

        // 设置面板打开时，屏蔽游戏内快捷键（R 刷新技能 / R 重开），
        // 否则会在面板后面偷偷触发，玩家以为点到了别的东西
        bool uiBlocking = SettingsUI.IsOpen;

        if (gm != null && Keyboard.current.escapeKey.wasPressedThisFrame && !uiBlocking)
        {
            if (gm.State == GameState.Playing) gm.Pause();
            else if (gm.State == GameState.Paused) gm.Resume();
        }

        // 主菜单状态 → 整个 HUD 画布关掉（Overlay 画布会盖住相机空间的菜单）
        if (canvas != null)
        {
            bool hudVisible = gm != null && gm.State != GameState.Menu;
            if (canvas.enabled != hudVisible) canvas.enabled = hudVisible;
        }

        // 强化进度条 / 护盾进度需要逐帧刷新（Refresh 只在分数/经验变化时触发，会一跳一跳）
        PlayerStats liveStats = PlayerStats.Instance;
        if (liveStats != null && gm != null && gm.State == GameState.Playing)
        {
            RefreshBuffGauge(liveStats);
            RefreshShieldBadge(liveStats);
        }

        // 暂停面板显隐跟随状态（Time.timeScale 已为 0，Update 仍会执行）
        if (pauseRoot != null && gm != null)
        {
            bool shouldShow = gm.State == GameState.Paused;
            if (pauseRoot.activeSelf != shouldShow) pauseRoot.SetActive(shouldShow);
        }

        // 三选一：1/2/3 选择，R 刷新（每次 1 次）
        if (upgradeRoot.activeSelf && offers != null)
        {
            if (Keyboard.current.digit1Key.wasPressedThisFrame) Choose(0);
            else if (Keyboard.current.digit2Key.wasPressedThisFrame) Choose(1);
            else if (Keyboard.current.digit3Key.wasPressedThisFrame) Choose(2);
            else if (!uiBlocking && Keyboard.current.rKey.wasPressedThisFrame) GameManager.Instance?.Reroll();
        }

        // 结算：R 重开
        if (!uiBlocking && gameOverRoot.activeSelf && Keyboard.current.rKey.wasPressedThisFrame)
        {
            GameManager.Instance?.RestartRun();
        }

        // 提示条倒计时（用 unscaled，免得暂停时卡住）
        if (toastTimer > 0f)
        {
            toastTimer -= Time.unscaledDeltaTime;
            if (toastTimer <= 0f && toast != null) toast.gameObject.SetActive(false);
        }

        UpdateBanner();
        UpdateBossBar();
    }

    private void UpdateBanner()
    {
        if (banner == null || bannerTimer <= 0f) return;

        bannerTimer -= Time.unscaledDeltaTime;
        float k = Mathf.Clamp01(bannerTimer / 1.6f);
        Color c = banner.color;
        c.a = k;
        banner.color = c;
        banner.rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, 1.15f, 1f - k);

        if (bannerTimer <= 0f) banner.gameObject.SetActive(false);
    }

    private void UpdateBossBar()
    {
        if (bossRoot == null) return;

        Brick boss = BrickField.Instance != null ? BrickField.Instance.CurrentBoss : null;
        bool show = boss != null && !boss.IsDead;

        if (bossRoot.activeSelf != show) bossRoot.SetActive(show);
        if (!show) return;

        float ratio = boss.HpRatio;
        bossFillRect.anchorMax = new Vector2(Mathf.Clamp01(ratio), 1f);
        bossLabel.text = $"BOSS   {Mathf.CeilToInt(boss.hp)} / {Mathf.CeilToInt(boss.maxHp)}";
    }

    private void OnWaveChanged(int wave)
    {
        ShowBanner($"WAVE {wave}", new Color(1f, 0.9f, 0.6f));
    }

    private void OnBossSpawned()
    {
        ShowBanner("BOSS", new Color(1f, 0.32f, 0.38f));
    }

    private void ShowBanner(string text, Color color)
    {
        if (banner == null) return;
        banner.text = text;
        banner.color = color;
        banner.gameObject.SetActive(true);
        bannerTimer = 1.6f;
    }

    // ------------------------------------------------------------------
    // 数据刷新
    // ------------------------------------------------------------------
    private void Refresh()
    {
        GameManager gm = GameManager.Instance;
        if (gm == null) return;

        scoreText.text = $"SCORE  {gm.Score}";
        levelText.text = $"LV.{gm.Level}";
        waveText.text = $"WAVE {gm.Wave}";
        if (bestText != null) bestText.text = $"BEST  {gm.BestScore}";

        float ratio = gm.XpToNext > 0 ? Mathf.Clamp01((float)gm.Xp / gm.XpToNext) : 0f;

        // 宽度小于圆角时切片会变形，隐藏它；否则给个最小宽度保证两端是圆的
        if (xpFill != null) xpFill.enabled = ratio > 0.005f;
        float visualRatio = ratio > 0f ? Mathf.Max(ratio, 0.035f) : 0f;
        xpFillRect.anchorMax = new Vector2(visualRatio, 1f);
        xpHint.text = $"XP {gm.Xp}/{gm.XpToNext}";

        PlayerStats st = PlayerStats.Instance;
        skillText.text = st == null ? "" : BuildSkillLine(st);
        RefreshBuffGauge(st);
        RefreshShieldBadge(st);
    }

    private static readonly SkillId[] AllSkills = (SkillId[])System.Enum.GetValues(typeof(SkillId));

    /// <summary>底部技能条：遍历所有技能，拿到几级就显示几级（加新技能不用改这里）。</summary>
    private static string BuildSkillLine(PlayerStats st)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();

        foreach (SkillId skill in AllSkills)
        {
            int lv = st.GetLevel(skill);
            if (lv <= 0) continue;

            if (sb.Length > 0) sb.Append("   ");
            sb.Append(UpgradeDatabase.ShortName(skill)).Append(" ").Append(lv);
        }

        return sb.ToString();
    }
    // ------------------------------------------------------------------
    // 三选一
    // ------------------------------------------------------------------
    private void ShowUpgrades(UpgradeOffer[] list)
    {
        offers = list;
        upgradeRoot.SetActive(true);
        IsUpgradePanelVisible = true;

        // 按画布宽度自适应卡片尺寸/间距，避免窄画幅下第三张被切掉
        float width = canvasRect != null ? canvasRect.rect.width : 1920f;
        float cardW = Mathf.Clamp(width * 0.21f, 240f, 360f);
        float cardH = cardW * 0.78f;
        float gap = cardW * 0.16f;

        for (int i = 0; i < 3; i++)
        {
            bool has = list != null && i < list.Length;
            cardBg[i].gameObject.SetActive(has);
            if (cardGlow[i] != null) cardGlow[i].gameObject.SetActive(has);
            if (cardBorder[i] != null) cardBorder[i].gameObject.SetActive(has);
            if (!has) continue;

            if (cardRect[i] != null)
            {
                Vector2 cardPos = new Vector2((i - 1) * (cardW + gap), 20f);
                cardRect[i].sizeDelta = new Vector2(cardW, cardH);
                cardRect[i].anchoredPosition = cardPos;

                // 发光 / 描边跟着卡片走
                if (cardGlow[i] != null)
                {
                    cardGlow[i].rectTransform.sizeDelta = new Vector2(cardW + 24f, cardH + 24f);
                    cardGlow[i].rectTransform.anchoredPosition = cardPos;
                }
                if (cardBorder[i] != null)
                {
                    cardBorder[i].rectTransform.sizeDelta = new Vector2(cardW + 5f, cardH + 5f);
                    cardBorder[i].rectTransform.anchoredPosition = cardPos;
                    cardBorder[i].color = new Color(list[i].Color.r, list[i].Color.g, list[i].Color.b, 0.75f);
                }

                // 子元素跟着卡片一起缩放，避免文字超出卡片
                cardTitle[i].rectTransform.sizeDelta = new Vector2(cardW - 30f, 60f);
                cardDesc[i].rectTransform.sizeDelta = new Vector2(cardW - 44f, cardH - 90f);
            }

            cardTitle[i].text = $"[{i + 1}]  {list[i].Title}";
            cardDesc[i].text = list[i].Desc;
            cardBg[i].color = list[i].Color * 0.35f;
        }

        // 刷新提示
        if (rerollButton != null)
        {
            int left = GameManager.Instance != null ? GameManager.Instance.RerollsLeft : 0;
            bool canReroll = left > 0;

            if (rerollButtonLabel != null)
                rerollButtonLabel.text = canReroll ? $"刷新选项（剩 {left} 次）　R" : "刷新次数已用完";

            rerollButton.interactable = canReroll;
        }
    }

    private void OnRerollClick()
    {
        GameManager.Instance?.Reroll();
    }

    private void Choose(int index)
    {
        if (offers == null || index >= offers.Length) return;
        UpgradeOffer offer = offers[index];
        offers = null;
        upgradeRoot.SetActive(false);
        IsUpgradePanelVisible = false;
        GameManager.Instance?.ChooseUpgrade(offer);
        ShowToast($"获得：{offer.Title}");
        Refresh();
    }

    private void ShowToast(string message)
    {
        if (toast == null) return;
        toast.text = message;
        toast.gameObject.SetActive(true);
        toastTimer = 1.4f;
    }

    // ------------------------------------------------------------------
    // 结算
    // ------------------------------------------------------------------
    private void ShowGameOver()
    {
        gameOverRoot.SetActive(true);
        GameManager gm = GameManager.Instance;
        gameOverInfo.text = gm == null ? "" : $"分数 {gm.Score}    最高 {gm.BestScore}\n等级 {gm.Level}    波次 {gm.Wave}    击杀 {gm.Kills}";
        FetchLeaderboard(gm);
    }

    /// <summary>提交分数 + 拉取排行榜（失败不影响单机）。</summary>
    private void FetchLeaderboard(GameManager gm)
    {
        if (gm == null) return;

        if (leaderboardText != null) { leaderboardText.text = ""; RefreshScrollHeight(); }
        if (submitStatus != null) submitStatus.text = "正在提交分数…";

        LeaderboardClient client = LeaderboardClient.Instance;
        if (client == null)
        {
            if (submitStatus != null) submitStatus.text = "排行榜未启用";
            return;
        }

        client.Submit(gm.Score, gm.Wave, gm.Level, gm.Kills, (ok, rank) =>
        {
            Debug.Log($"[Leaderboard] 提交结果 ok={ok} rank={rank}");

            if (submitStatus != null)
            {
                submitStatus.text = ok
                    ? $"提交成功　当前第 {rank} 名　玩家：{LeaderboardClient.PlayerName}"
                    : "服务器暂时离开，已进入离线模式（成绩保存在本机）";
            }

            if (!ok)
            {
                ShowLocalHistory(gm);
                return;
            }

            client.FetchTop(LeaderboardClient.DisplayLimit, entries =>
            {
                Debug.Log($"[Leaderboard] 拉取结果 count={(entries == null ? -1 : entries.Length)}");
                if (leaderboardText == null) return;
                if (entries == null || entries.Length == 0)
                {
                    ShowLocalHistory(gm);
                    return;
                }

                // 条目多时自动缩小字号，避免撑破面板
                leaderboardText.fontSize = entries.Length > 16 ? 18 : entries.Length > 10 ? 22 : 26;

                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.Append($"排行榜 TOP {LeaderboardClient.DisplayLimit}\n\n");
                for (int i = 0; i < entries.Length; i++)
                {
                    LeaderboardClient.ScoreEntry e = entries[i];
                    sb.Append($"#{i + 1,2}   {e.name}   {e.score} 分   (W{e.wave} L{e.level})\n");
                }
                leaderboardText.text = sb.ToString();
                RefreshScrollHeight();
            });
        });
    }

    /// <summary>服务器连不上时的兜底：显示本机最近 20 局里分数最高的 10 局。</summary>
    private void ShowLocalHistory(GameManager gm)
    {
        if (leaderboardText == null) return;

        GameManager.RunRecord[] history = gm != null ? gm.History : null;
        Debug.Log($"[Leaderboard] 本地记录 count={(history == null ? -1 : history.Length)}");
        if (history == null || history.Length == 0)
        {
            leaderboardText.text = "（暂无记录）";
            RefreshScrollHeight();
            return;
        }

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.Append("本地记录 TOP 10（离线）\n\n");
        int count = Mathf.Min(10, history.Length);
        for (int i = 0; i < count; i++)
        {
            GameManager.RunRecord r = history[i];
            sb.Append($"#{i + 1,2}   {r.score} 分   (W{r.wave} L{r.level})   {r.date}\n");
        }
        leaderboardText.text = sb.ToString();
        RefreshScrollHeight();
    }

    // ------------------------------------------------------------------
    // 构建 UI
    // ------------------------------------------------------------------
    private void Build()
    {
        if (_font == null)
        {
            // 中文字体：优先系统雅黑，保证中文不出现方块
            _font = Font.CreateDynamicFontFromOSFont("Microsoft YaHei", 32);
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        GameObject canvasGo = new GameObject("GameUI_Canvas");
        canvasGo.transform.SetParent(transform, false);
        canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        canvas.pixelPerfect = true;   // 像素对齐，减少文字模糊
        canvasRect = canvasGo.GetComponent<RectTransform>();
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // 没有它，按钮收不到点击
        canvasGo.AddComponent<GraphicRaycaster>();

        // 所有 UI 挂在 uiRoot 下：如果实际画布尺寸和设计分辨率差得多，统一缩放一次，避免溢出
        GameObject rootGo = new GameObject("UIRoot");
        rootGo.transform.SetParent(canvasGo.transform, false);
        uiRoot = rootGo.AddComponent<RectTransform>();
        Stretch(uiRoot);

        // ---------- HUD ----------
        // 顶部居中：等级（最大字号）
        levelText = MakeText(uiRoot, "Level", "LV.1", 58, TextAnchor.UpperCenter, Color.white);
        Place(levelText.rectTransform, new Vector2(0f, -4f), new Vector2(700f, 76f), TextAnchor.UpperCenter);

        scoreText = MakeText(uiRoot, "Score", "SCORE  0", 60, TextAnchor.UpperLeft,
                             new Color(0.85f, 1f, 1f));
        Place(scoreText.rectTransform, new Vector2(40f, -18f), new Vector2(700f, 76f), TextAnchor.UpperLeft);

        waveText = MakeText(uiRoot, "Wave", "WAVE 1", 52, TextAnchor.UpperRight,
                            new Color(1f, 0.85f, 0.6f));
        Place(waveText.rectTransform, new Vector2(-40f, -18f), new Vector2(500f, 76f), TextAnchor.UpperRight);

        skillText = MakeText(uiRoot, "Skills", "", 26, TextAnchor.LowerLeft,
                             new Color(0.75f, 0.9f, 1f));
        Place(skillText.rectTransform, new Vector2(40f, 26f), new Vector2(1800f, 40f), TextAnchor.LowerLeft);

        BuildShieldBadge();
        BuildBuffGauge();

        // 右下角：服务器在线状态
        onlineText = MakeText(uiRoot, "Online", "● 检测中…", 22, TextAnchor.LowerRight,
                              new Color(0.6f, 0.65f, 0.7f));
        Place(onlineText.rectTransform, new Vector2(-260f, 20f), new Vector2(640f, 34f), TextAnchor.LowerRight);
        RefreshOnlineLabel();

        // 经验条：紧贴等级下方，加宽加高
        Image xpBg = MakeImage(uiRoot, "XpBg", new Color(0.1f, 0.15f, 0.22f, 0.9f));
        xpBg.sprite = NeonUi.Rounded();
        xpBg.type = Image.Type.Sliced;
        Place(xpBg.rectTransform, new Vector2(0f, -84f), new Vector2(1100f, 30f), TextAnchor.UpperCenter);

        xpFill = MakeImage(xpBg.transform, "XpFill", new Color(0.4f, 1f, 0.5f, 0.95f));
        xpFill.sprite = NeonUi.Rounded();
        xpFill.type = Image.Type.Sliced;
        xpFillRect = xpFill.rectTransform;
        xpFillRect.anchorMin = new Vector2(0f, 0f);
        xpFillRect.anchorMax = new Vector2(0f, 1f);
        xpFillRect.offsetMin = new Vector2(3f, 3f);      // 内缩，别盖住底条的圆角
        xpFillRect.offsetMax = new Vector2(-3f, -3f);

        xpHint = MakeText(uiRoot, "XpHint", "XP 0/30", 28, TextAnchor.UpperCenter,
                          new Color(0.8f, 0.9f, 1f));
        Place(xpHint.rectTransform, new Vector2(0f, -120f), new Vector2(600f, 36f), TextAnchor.UpperCenter);

        bestText = MakeText(uiRoot, "Best", "BEST 0", 28, TextAnchor.UpperRight,
                            new Color(1f, 0.75f, 0.45f));
        Place(bestText.rectTransform, new Vector2(-40f, -80f), new Vector2(500f, 40f), TextAnchor.UpperRight);

        // ---------- Boss 血条（默认隐藏） ----------
        bossRoot = new GameObject("BossBar");
        bossRoot.transform.SetParent(uiRoot, false);
        RectTransform bossRect = bossRoot.AddComponent<RectTransform>();
        Place(bossRect, new Vector2(0f, -176f), new Vector2(1000f, 60f), TextAnchor.UpperCenter);

        bossLabel = MakeText(bossRoot.transform, "BossLabel", "BOSS", 26, TextAnchor.UpperCenter,
                             new Color(1f, 0.45f, 0.45f));
        Place(bossLabel.rectTransform, new Vector2(0f, 0f), new Vector2(600f, 32f), TextAnchor.UpperCenter);

        Image bossBg = MakeImage(bossRoot.transform, "BossBg", new Color(0.12f, 0.03f, 0.05f, 0.95f));
        Place(bossBg.rectTransform, new Vector2(0f, -32f), new Vector2(1000f, 24f), TextAnchor.UpperCenter);

        bossFill = MakeImage(bossBg.transform, "BossFill", new Color(1f, 0.3f, 0.3f, 0.95f));
        bossFillRect = bossFill.rectTransform;
        bossFillRect.anchorMin = new Vector2(0f, 0f);
        bossFillRect.anchorMax = new Vector2(1f, 1f);
        bossFillRect.offsetMin = Vector2.zero;
        bossFillRect.offsetMax = Vector2.zero;

        bossRoot.SetActive(false);

        // ---------- 波次 / BOSS 横幅 ----------
        banner = MakeText(uiRoot, "Banner", "", 76, TextAnchor.MiddleCenter, new Color(1f, 0.9f, 0.6f));
        Place(banner.rectTransform, new Vector2(0f, 140f), new Vector2(1400f, 120f), TextAnchor.MiddleCenter);
        banner.gameObject.SetActive(false);

        // ---------- 三选一面板 ----------
        upgradeRoot = new GameObject("UpgradePanel");
        upgradeRoot.transform.SetParent(uiRoot, false);
        RectTransform ur = upgradeRoot.AddComponent<RectTransform>();
        Stretch(ur);
        Image dim = upgradeRoot.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.72f);

        upgradeTitle = MakeText(upgradeRoot.transform, "Title", "升级！选择一项（按 1 / 2 / 3）", 60,
                                TextAnchor.UpperCenter, Color.white);
        Place(upgradeTitle.rectTransform, new Vector2(0f, -110f), new Vector2(1600f, 80f), TextAnchor.UpperCenter);

        // 刷新按钮（也可以按 R）
        rerollButton = NeonUi.NeonButton(upgradeRoot.transform, "刷新选项", new Vector2(0f, -250f),
                                         new Vector2(420f, 80f), new Color(0.72f, 0.62f, 1f), OnRerollClick, 30);
        rerollButtonLabel = rerollButton.GetComponentInChildren<Text>();

        for (int i = 0; i < 3; i++)
        {
            float x = (i - 1) * 370f;

            // 外发光 / 描边（卡片下层，位置尺寸在 ShowUpgrades 里跟卡片同步）
            cardGlow[i] = NeonUi.Panel(upgradeRoot.transform, $"CardGlow{i}", new Vector2(x, 30f),
                                       new Vector2(330f, 250f), new Color(0.35f, 0.85f, 1f, 0.10f));
            cardBorder[i] = NeonUi.Panel(upgradeRoot.transform, $"CardBorder{i}", new Vector2(x, 30f),
                                          new Vector2(330f, 250f), new Color(0.35f, 0.85f, 1f, 0.55f));

            Image card = MakeImage(upgradeRoot.transform, $"Card{i}", new Color(0.2f, 0.2f, 0.3f, 0.95f));
            card.sprite = NeonUi.Rounded();
            card.type = Image.Type.Sliced;
            Place(card.rectTransform, new Vector2(x, 30f), new Vector2(330f, 250f), TextAnchor.MiddleCenter);
            cardBg[i] = card;
            cardRect[i] = card.rectTransform;

            cardTitle[i] = MakeText(card.transform, "Title", "", 36, TextAnchor.UpperCenter, Color.white);
            Place(cardTitle[i].rectTransform, new Vector2(0f, -18f), new Vector2(310f, 50f), TextAnchor.UpperCenter);

            cardDesc[i] = MakeText(card.transform, "Desc", "", 28, TextAnchor.UpperCenter,
                                   new Color(0.9f, 0.95f, 1f));
            Place(cardDesc[i].rectTransform, new Vector2(0f, -70f), new Vector2(290f, 160f), TextAnchor.UpperCenter);

            // 卡片本身可点（鼠标选卡，和按 1/2/3 等价）
            int index = i;
            Button cardButton = card.gameObject.AddComponent<Button>();
            cardButton.targetGraphic = card;
            cardButton.transition = Selectable.Transition.ColorTint;

            ColorBlock colors = cardButton.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.selectedColor = Color.white;
            colors.fadeDuration = 0.08f;
            cardButton.colors = colors;

            cardButton.onClick.AddListener(() => Choose(index));
        }

        upgradeRoot.SetActive(false);
        IsUpgradePanelVisible = false;

        // 选卡后的提示条
        toast = MakeText(uiRoot, "Toast", "", 40, TextAnchor.MiddleCenter, new Color(1f, 0.95f, 0.7f));
        Place(toast.rectTransform, new Vector2(0f, 200f), new Vector2(1200f, 70f), TextAnchor.MiddleCenter);
        toast.gameObject.SetActive(false);

        // ---------- 结算面板 ----------
        gameOverRoot = new GameObject("GameOverPanel");
        gameOverRoot.transform.SetParent(uiRoot, false);
        Stretch(gameOverRoot.AddComponent<RectTransform>());
        Image goDim = gameOverRoot.AddComponent<Image>();
        goDim.color = new Color(0.05f, 0f, 0.02f, 0.85f);

        // 霓虹对话框：左栏（成绩 + 按钮）
        NeonUi.Panel(gameOverRoot.transform, "LeftBorder", new Vector2(-520f, 40f), new Vector2(700f, 700f),
                     new Color(1f, 0.35f, 0.45f, 0.40f));
        NeonUi.Panel(gameOverRoot.transform, "LeftBox", new Vector2(-520f, 40f), new Vector2(692f, 692f),
                     new Color(0.04f, 0.03f, 0.06f, 0.95f));

        // 霓虹对话框：右栏（排行榜）
        NeonUi.Panel(gameOverRoot.transform, "RightBorder", new Vector2(250f, -10f), new Vector2(920f, 600f),
                     new Color(0.35f, 0.95f, 1f, 0.35f));
        NeonUi.Panel(gameOverRoot.transform, "RightBox", new Vector2(250f, -10f), new Vector2(912f, 592f),
                     new Color(0.03f, 0.05f, 0.09f, 0.95f));

        Text goTitle = NeonUi.GlowText(gameOverRoot.transform, "Title", "GAME OVER", 84,
                                       new Color(1f, 0.38f, 0.48f), new Vector2(-520f, 320f), new Vector2(640f, 110f));

        gameOverInfo = MakeText(gameOverRoot.transform, "Info", "", 40, TextAnchor.MiddleCenter, Color.white);
        Place(gameOverInfo.rectTransform, new Vector2(-520f, 200f), new Vector2(640f, 180f), TextAnchor.MiddleCenter);

        Text goHint = MakeText(gameOverRoot.transform, "Hint", "按 R 重新挑战", 30, TextAnchor.MiddleCenter,
                               new Color(0.8f, 0.9f, 1f));
        Place(goHint.rectTransform, new Vector2(-520f, 0f), new Vector2(640f, 40f), TextAnchor.MiddleCenter);

        // 两个按钮：重新挑战（跳过主菜单直接开局）/ 返回主菜单
        // 宽度 280 时两个按钮会重叠 20px，缩到 240 并拉开间距
        NeonUi.NeonButton(gameOverRoot.transform, "重新挑战", new Vector2(-660f, -160f),
                          new Vector2(240f, 76f), new Color(0.4f, 1f, 0.7f), RestartRun, 30);

        NeonUi.NeonButton(gameOverRoot.transform, "返回主菜单", new Vector2(-390f, -160f),
                          new Vector2(240f, 76f), new Color(0.35f, 0.95f, 1f), RestartToMenu, 30);

        // 右侧：联网排行榜
        // 可滚动的排行榜区域
        leaderboardScroll = MakeScrollView(gameOverRoot.transform, new Vector2(250f, -10f),
                                           new Vector2(900f, 580f), 26, out leaderboardText);

        submitStatus = MakeText(gameOverRoot.transform, "SubmitStatus", "", 24, TextAnchor.MiddleCenter,
                                new Color(0.7f, 0.85f, 1f));
        Place(submitStatus.rectTransform, new Vector2(-520f, 80f), new Vector2(640f, 60f), TextAnchor.MiddleCenter);

        gameOverRoot.SetActive(false);

        BuildPausePanel();
    }

    /// <summary>暂停菜单：Esc 呼出，继续 / 重新挑战 / 返回主菜单。</summary>
    /// <summary>失败线上方的护盾数显示（只有拿到护盾技能才出现）。</summary>
    private void BuildShieldBadge()
    {
        shieldBadge = new GameObject("ShieldBadge", typeof(RectTransform));
        shieldBadge.transform.SetParent(uiRoot.transform, false);

        RectTransform rt = shieldBadge.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(240f, 64f);
        rt.anchoredPosition = new Vector2(28f, -150f);

        NeonUi.Panel(shieldBadge.transform, "Bg", new Vector2(120f, -32f), new Vector2(240f, 64f),
                     new Color(0.03f, 0.08f, 0.14f, 0.85f));

        shieldText = MakeText(shieldBadge.transform, "Text", "护盾 1/1", 26, TextAnchor.MiddleCenter,
                              new Color(0.55f, 0.9f, 1f));
        Place(shieldText.rectTransform, new Vector2(120f, -32f), new Vector2(236f, 56f), TextAnchor.MiddleCenter);

        shieldBadge.SetActive(false);
    }

    private void RefreshShieldBadge(PlayerStats st)
    {
        if (shieldBadge == null) return;

        bool has = st != null && st.shield > 0 && DeadlineShield.Instance != null;
        if (shieldBadge.activeSelf != has) shieldBadge.SetActive(has);
        if (!has) return;

        int charges = DeadlineShield.Instance.Charges;
        int max = DeadlineShield.MaxForTier;

        string text;
        Color color;

        if (charges >= max)
        {
            text = $"护盾 {charges}/{max}";
            color = new Color(0.55f, 0.92f, 1f);
        }
        else
        {
            int pct = Mathf.RoundToInt(DeadlineShield.Instance.RechargeProgress01 * 100f);
            text = $"护盾 {charges}/{max}  {pct}%";
            color = new Color(0.45f, 0.7f, 0.85f);
        }

        // 只有内容/颜色真的变了才赋值，避免每帧触发 Canvas 重建
        if (shieldText.text != text) shieldText.text = text;
        if (shieldText.color != color) shieldText.color = color;
    }
    /// <summary>左下角的「强化状态」圆形进度条：冷却时转圈填满，激活时倒计时消耗。</summary>
    private void BuildBuffGauge()
    {
        buffGauge = new GameObject("BuffGauge", typeof(RectTransform));
        buffGauge.transform.SetParent(uiRoot.transform, false);

        RectTransform rootRect = buffGauge.GetComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0f, 0f);
        rootRect.anchorMax = new Vector2(0f, 0f);
        rootRect.pivot = new Vector2(0f, 0f);
        rootRect.sizeDelta = new Vector2(96f, 96f);
        rootRect.anchoredPosition = new Vector2(56f, 96f);

        // 底环
        GameObject trackGo = new GameObject("Track", typeof(RectTransform), typeof(Image));
        trackGo.transform.SetParent(buffGauge.transform, false);
        Image track = trackGo.GetComponent<Image>();
        track.sprite = NeonUi.Ring();
        track.color = new Color(0.15f, 0.22f, 0.30f, 0.85f);
        track.raycastTarget = false;
        Stretch(trackGo.GetComponent<RectTransform>());

        // 进度环
        GameObject fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fillGo.transform.SetParent(buffGauge.transform, false);
        buffFill = fillGo.GetComponent<Image>();
        buffFill.sprite = NeonUi.Ring();
        buffFill.color = new Color(0.4f, 0.9f, 1f, 0.95f);
        buffFill.type = Image.Type.Filled;
        buffFill.fillMethod = Image.FillMethod.Radial360;
        buffFill.fillOrigin = (int)Image.Origin360.Top;
        buffFill.fillClockwise = true;
        buffFill.fillAmount = 0f;
        buffFill.raycastTarget = false;
        Stretch(fillGo.GetComponent<RectTransform>());

        // 文字
        buffLabel = MakeText(buffGauge.transform, "Label", "强化", 20, TextAnchor.MiddleCenter,
                             new Color(0.75f, 0.95f, 1f));
        Place(buffLabel.rectTransform, new Vector2(48f, 48f), new Vector2(96f, 28f), TextAnchor.MiddleCenter);

        buffGauge.SetActive(false);
    }

    private void RefreshBuffGauge(PlayerStats st)
    {
        if (buffGauge == null) return;

        bool has = st != null && st.buff > 0;
        if (buffGauge.activeSelf != has) buffGauge.SetActive(has);
        if (!has) return;

        if (st.BuffActive)
        {
            buffFill.fillAmount = st.BuffRemaining01;
            buffFill.color = new Color(0.55f, 1f, 0.95f, 1f);

            string t = $"强化 {st.BuffRemaining01 * st.BuffDuration:0.0}s";
            if (buffLabel.text != t) buffLabel.text = t;
            if (buffLabel.color != new Color(0.7f, 1f, 1f)) buffLabel.color = new Color(0.7f, 1f, 1f);
        }
        else
        {
            buffFill.fillAmount = st.BuffCooldown01;
            buffFill.color = new Color(0.35f, 0.7f, 0.9f, 0.85f);

            if (buffLabel.text != "强化") buffLabel.text = "强化";
            if (buffLabel.color != new Color(0.6f, 0.8f, 0.9f)) buffLabel.color = new Color(0.6f, 0.8f, 0.9f);
        }
    }
    private void BuildPausePanel()
    {
        pauseRoot = new GameObject("PausePanel", typeof(RectTransform), typeof(Image));
        pauseRoot.transform.SetParent(uiRoot.transform, false);
        Stretch(pauseRoot.GetComponent<RectTransform>());
        pauseRoot.GetComponent<Image>().color = new Color(0.01f, 0.02f, 0.05f, 0.82f);

        // 霓虹对话框：描边 + 底板（加宽加高，四个按钮才不会贴边/溢出）
        NeonUi.Panel(pauseRoot.transform, "DialogBorder", new Vector2(0f, 0f), new Vector2(780f, 700f),
                     new Color(0.35f, 0.95f, 1f, 0.45f));
        NeonUi.Panel(pauseRoot.transform, "Dialog", new Vector2(0f, 0f), new Vector2(772f, 692f),
                     new Color(0.03f, 0.06f, 0.11f, 0.97f));

        Text title = NeonUi.GlowText(pauseRoot.transform, "Title", "已暂停", 82,
                                     new Color(0.5f, 0.95f, 1f), new Vector2(0f, 265f), new Vector2(640f, 100f));

        Text hint = MakeText(pauseRoot.transform, "Hint", "按 Esc 继续游戏", 26, TextAnchor.MiddleCenter,
                             new Color(0.7f, 0.82f, 0.92f));
        Place(hint.rectTransform, new Vector2(0f, 190f), new Vector2(760f, 36f), TextAnchor.MiddleCenter);

        NeonUi.NeonButton(pauseRoot.transform, "继续游戏", new Vector2(0f, 88f), new Vector2(460f, 88f),
                          new Color(0.4f, 1f, 0.7f), ResumeGame, 36);

        NeonUi.NeonButton(pauseRoot.transform, "重新挑战", new Vector2(0f, -20f), new Vector2(460f, 88f),
                          new Color(0.35f, 0.95f, 1f), RestartRun, 36);

        NeonUi.NeonButton(pauseRoot.transform, "返回主菜单", new Vector2(0f, -128f), new Vector2(460f, 88f),
                          new Color(1f, 0.45f, 0.75f), RestartToMenu, 36);

        NeonUi.NeonButton(pauseRoot.transform, "设置", new Vector2(0f, -236f), new Vector2(460f, 80f),
                          new Color(0.75f, 0.85f, 1f), OpenSettings, 34);

        pauseRoot.SetActive(false);
    }

    private void OpenSettings()
    {
        SettingsUI.Open();
    }

    private void ResumeGame()
    {
        GameManager.Instance?.Resume();
    }

    private Text MakeText(Transform parent, string name, string content, int size,
                          TextAnchor anchor, Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);

        Text text = go.AddComponent<Text>();
        text.font = _font;
        text.fontSize = size;
        text.alignment = anchor;
        text.color = color;
        text.text = content;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;

        return text;
    }

    /// <summary>建一个垂直滚动列表（滚轮 / 拖动），文本通过 out 返回。</summary>
    private ScrollRect MakeScrollView(Transform parent, Vector2 pos, Vector2 size,
                                      int fontSize, out Text contentText)
    {
        GameObject rootGo = new GameObject("ScrollView", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        rootGo.transform.SetParent(parent, false);
        Place(rootGo.GetComponent<RectTransform>(), pos, size, TextAnchor.MiddleCenter);
        rootGo.GetComponent<Image>().color = new Color(0.04f, 0.06f, 0.09f, 0.55f);

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
        contentRect.offsetMin = new Vector2(20f, 0f);
        contentRect.offsetMax = new Vector2(-20f, -10f);
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

    /// <summary>按文字实际高度撑开内容区。</summary>
    private void RefreshScrollHeight()
    {
        if (leaderboardScroll == null || leaderboardText == null) return;

        RectTransform content = leaderboardScroll.content;
        float needed = leaderboardText.preferredHeight + 20f;
        float viewportHeight = leaderboardScroll.viewport.rect.height;
        content.sizeDelta = new Vector2(content.sizeDelta.x, Mathf.Max(viewportHeight, needed));
        leaderboardScroll.verticalNormalizedPosition = 1f;
    }
    private static Image MakeImage(Transform parent, string name, Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);

        Image image = go.AddComponent<Image>();
        image.color = color;
        return image;
    }

    /// <summary>创建一个可点击按钮（结算面板的「返回主菜单」用）。</summary>
    private Button MakeButton(Transform parent, string label, Vector2 pos, Vector2 size,
                              Color color, System.Action onClick)
    {
        GameObject go = new GameObject("Btn_" + label);
        go.transform.SetParent(parent, false);

        Image image = go.AddComponent<Image>();
        image.color = color;

        Button button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => onClick());

        Place(go.GetComponent<RectTransform>(), pos, size, TextAnchor.MiddleCenter);

        Text text = MakeText(go.transform, "Label", label, 34, TextAnchor.MiddleCenter, Color.white);
        Place(text.rectTransform, Vector2.zero, size, TextAnchor.MiddleCenter);

        return button;
    }

    private void RestartToMenu()
    {
        GameManager.Instance?.Restart(false);
    }

    private void RestartRun()
    {
        GameManager.Instance?.RestartRun();
    }

    private static void Place(RectTransform rect, Vector2 anchoredPosition, Vector2 size, TextAnchor anchor)
    {
        Vector2 pivot = new Vector2(0.5f, 0.5f);
        Vector2 anchorPoint = new Vector2(0.5f, 0.5f);

        switch (anchor)
        {
            case TextAnchor.UpperLeft: pivot = new Vector2(0f, 1f); anchorPoint = new Vector2(0f, 1f); break;
            case TextAnchor.UpperRight: pivot = new Vector2(1f, 1f); anchorPoint = new Vector2(1f, 1f); break;
            case TextAnchor.UpperCenter: pivot = new Vector2(0.5f, 1f); anchorPoint = new Vector2(0.5f, 1f); break;
            case TextAnchor.LowerLeft: pivot = new Vector2(0f, 0f); anchorPoint = new Vector2(0f, 0f); break;
            case TextAnchor.LowerRight: pivot = new Vector2(1f, 0f); anchorPoint = new Vector2(1f, 0f); break;
            case TextAnchor.LowerCenter: pivot = new Vector2(0.5f, 0f); anchorPoint = new Vector2(0.5f, 0f); break;
            case TextAnchor.MiddleLeft: pivot = new Vector2(0f, 0.5f); anchorPoint = new Vector2(0f, 0.5f); break;
            case TextAnchor.MiddleRight: pivot = new Vector2(1f, 0.5f); anchorPoint = new Vector2(1f, 0.5f); break;
            default: pivot = new Vector2(0.5f, 0.5f); anchorPoint = new Vector2(0.5f, 0.5f); break;
        }

        rect.anchorMin = anchorPoint;
        rect.anchorMax = anchorPoint;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
