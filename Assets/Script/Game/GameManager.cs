using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 【游戏总控】分数、经验、等级、波次、状态机、失败判定。
/// 其它系统只通过这里的事件和接口通信，不互相直接引用。
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("升级曲线")]
    [Tooltip("1 级升 2 级需要的经验")]
    public int baseXpToLevel = 30;
    [Tooltip("每级额外增加的经验")]
    public int xpGrowthPerLevel = 20;
    [Tooltip("每隔多少级给一次三选一（1 级开局也给）")]
    public int upgradeEveryLevels = 5;

    public GameState State { get; private set; } = GameState.Playing;
    public int Score { get; private set; }
    public int Level { get; private set; } = 1;
    public int Xp { get; private set; }
    public int Wave { get; private set; } = 1;
    public int Kills { get; private set; }

    /// <summary>历史最高分（PlayerPrefs 持久化）</summary>
    public int BestScore { get; private set; }

    /// <summary>本地历史记录（最多 20 局，服务器连不上时用它当排行榜）</summary>
    [Serializable]
    public class RunRecord
    {
        public int score;
        public int wave;
        public int level;
        public int kills;
        public string date;
    }

    [Serializable]
    private class RunHistory
    {
        public RunRecord[] items;
    }

    public RunRecord[] History { get; private set; } = new RunRecord[0];

    private const string BestScoreKey = "BreakOut.BestScore";
    private const string HistoryKey = "BreakOut.History";

    /// <summary>最高分按玩家名分开存，换名字就是换一份记录。</summary>
    private static string BestKeyFor(string playerName) => BestScoreKey + "." + playerName;

    /// <summary>玩家名变了之后调用：重新读这一名玩家的最高分。</summary>
    public void ReloadBest()
    {
        BestScore = PlayerPrefs.GetInt(BestKeyFor(LeaderboardClient.PlayerName), 0);
        OnChanged?.Invoke();
    }

    /// <summary>当前等级升到下一级需要的经验</summary>
    public int XpToNext => baseXpToLevel + (Level - 1) * xpGrowthPerLevel;

    /// <summary>数值/进度变化（UI 监听）</summary>
    public event Action OnChanged;
    /// <summary>需要弹出三选一（UI 监听）</summary>
    public event Action<UpgradeOffer[]> OnUpgradeRequested;
    /// <summary>游戏结束（UI 监听）</summary>
    public event Action OnGameOver;
    /// <summary>波次变化（UI 监听，用来弹横幅）</summary>
    public event Action<int> OnWaveChanged;
    /// <summary>Boss 出现（UI 监听）</summary>
    public event Action OnBossSpawned;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BestScore = PlayerPrefs.GetInt(BestKeyFor(LeaderboardClient.PlayerName), 0);
        LoadHistory();
    }

    private void LoadHistory()
    {
        string raw = PlayerPrefs.GetString(HistoryKey, "");
        if (string.IsNullOrEmpty(raw)) return;

        try
        {
            RunHistory history = JsonUtility.FromJson<RunHistory>(raw);
            if (history != null && history.items != null) History = history.items;
        }
        catch
        {
            History = new RunRecord[0];
        }
    }

    /// <summary>把这一局写入本地历史（按分数降序，保留前 20）。</summary>
    private void SaveRun()
    {
        System.Collections.Generic.List<RunRecord> list =
            new System.Collections.Generic.List<RunRecord>(History);

        list.Add(new RunRecord
        {
            score = Score,
            wave = Wave,
            level = Level,
            kills = Kills,
            date = DateTime.Now.ToString("MM-dd HH:mm"),
        });

        list.Sort((a, b) => b.score.CompareTo(a.score));
        if (list.Count > 20) list.RemoveRange(20, list.Count - 20);

        History = list.ToArray();
        PlayerPrefs.SetString(HistoryKey, JsonUtility.ToJson(new RunHistory { items = History }));
        PlayerPrefs.Save();
    }

    private void Start()
    {
        if (AutoStartNextRun)
        {
            // 「重新挑战」：跳过主菜单，直接开局
            AutoStartNextRun = false;
            State = GameState.Playing;
            Time.timeScale = 1f;
            OnChanged?.Invoke();
            StartCoroutine(OpenFirstUpgradeWhenReady());
            Debug.Log("[GameManager] 重新挑战：跳过主菜单直接开局");
            return;
        }

        // 启动先进主菜单，时间暂停；点「开始游戏」才真正开局
        State = GameState.Menu;
        Time.timeScale = 0f;
        Debug.Log("[GameManager] 进入主菜单状态，等待 BeginRun()");
    }

    /// <summary>菜单没出来时的兜底：按回车/空格也能开始，避免卡在暂停状态。</summary>
    private void Update()
    {
        if (State != GameState.Menu) return;
        if (UnityEngine.InputSystem.Keyboard.current == null) return;

        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)
        {
            Debug.Log("[GameManager] 回车/空格开始游戏（主菜单兜底）");
            BeginRun();
        }
    }

    /// <summary>由主菜单调用：开始一局。</summary>
    public void BeginRun()
    {
        if (State != GameState.Menu) return;

        State = GameState.Playing;
        Time.timeScale = 1f;
        OnChanged?.Invoke();

        // 开局 1 级也给一次三选一，但等远程配置拉完再弹（否则卡面数值是旧的）
        StartCoroutine(OpenFirstUpgradeWhenReady());
    }

    private System.Collections.IEnumerator OpenFirstUpgradeWhenReady()
    {
        float waited = 0f;
        while (!GameConfig.Loaded && waited < 3f)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }
        yield return new WaitForSeconds(0.2f);

        OpenUpgradeSelection();
    }

    // ---------------- 分数 / 击杀 ----------------
    public void AddScore(int amount)
    {
        if (State == GameState.GameOver) return;
        Score += amount;
        Kills++;
        OnChanged?.Invoke();
    }

    // ---------------- 经验 / 等级 ----------------
    public void AddXp(int amount)
    {
        if (State == GameState.GameOver || amount <= 0) return;

        // 经验倍率技能
        float mult = PlayerStats.Instance != null ? PlayerStats.Instance.XpMultiplier : 1f;
        amount = Mathf.Max(1, Mathf.RoundToInt(amount * mult));

        Xp += amount;
        while (Xp >= XpToNext)
        {
            Xp -= XpToNext;
            Level++;
            OnChanged?.Invoke();

            // 1 级开局给一次，之后每 5 级一次
            if (Level % upgradeEveryLevels == 0)
            {
                OpenUpgradeSelection();
                return;   // 暂停中，剩下的经验下次再结算
            }
        }
        OnChanged?.Invoke();
    }

    // ---------------- 三选一 ----------------
    /// <summary>每次三选一可用的刷新次数（服务器可调）</summary>
    public int rerollCount = 2;

    /// <summary>本次选择还剩几次刷新</summary>
    public int RerollsLeft { get; private set; }

    /// <summary>本次选择是否还能刷新</summary>
    public bool CanReroll => RerollsLeft > 0;

    /// <summary>弹出三选一（时间暂停）。技能全满时会自动失败退出。</summary>
    public void OpenUpgradeSelection()
    {
        if (State == GameState.GameOver) return;

        UpgradeOffer[] offers = UpgradeDatabase.RollThree(PlayerStats.Instance);
        if (offers == null || offers.Length == 0)
        {
            return;   // 所有技能都满级，不弹
        }

        State = GameState.UpgradeSelecting;
        RerollsLeft = Mathf.Max(0, rerollCount);
        Time.timeScale = 0f;
        SoundFx.LevelUp();

        try
        {
            OnUpgradeRequested?.Invoke(offers);
        }
        catch (System.Exception e)
        {
            // 面板构建炸了就会永远卡在 timeScale=0（表现：子弹停在半空、无法继续）
            // 这里兜底恢复，绝不让游戏卡死
            Debug.LogError($"[GameManager] 三选一面板弹出失败，已自动恢复：{e}");
            State = GameState.Playing;
            Time.timeScale = 1f;
            OnChanged?.Invoke();
        }

        // 二次保险：如果 0.5 秒后仍在选卡状态但没有任何面板，强制恢复
        StartCoroutine(UpgradeWatchdog());
    }

    private System.Collections.IEnumerator UpgradeWatchdog()
    {
        yield return new WaitForSecondsRealtime(0.5f);

        if (State == GameState.UpgradeSelecting && !GameUI.IsUpgradePanelVisible)
        {
            Debug.LogError("[GameManager] 检测到选卡状态但没有面板显示，强制恢复游戏");
            State = GameState.Playing;
            Time.timeScale = 1f;
            OnChanged?.Invoke();
        }
    }

    /// <summary>刷新一次选项（每次选择只能用一次）。</summary>
    public void Reroll()
    {
        if (State != GameState.UpgradeSelecting || RerollsLeft <= 0) return;

        UpgradeOffer[] offers = UpgradeDatabase.RollThree(PlayerStats.Instance);
        if (offers == null || offers.Length == 0) return;

        RerollsLeft--;
        OnUpgradeRequested?.Invoke(offers);
    }

    /// <summary>Boss 击杀奖励：额外一次三选一。</summary>
    public void GrantBossReward()
    {
        OpenUpgradeSelection();
    }

    /// <summary>玩家选择了某个升级（UI 调用）。</summary>
    public void ChooseUpgrade(UpgradeOffer offer)
    {
        PlayerStats.Instance?.Upgrade(offer.Skill);

        State = GameState.Playing;
        Time.timeScale = 1f;
        OnChanged?.Invoke();
    }

    // ---------------- 波次 ----------------
    public void AdvanceWave()
    {
        Wave++;
        OnChanged?.Invoke();
        OnWaveChanged?.Invoke(Wave);
    }

    /// <summary>BrickField 生成 Boss 时调用。</summary>
    public void NotifyBossSpawned()
    {
        OnBossSpawned?.Invoke();
    }

    // ---------------- 暂停 ----------------
    /// <summary>Esc 暂停（只在游玩中有效）。</summary>
    public void Pause()
    {
        if (State != GameState.Playing) return;
        State = GameState.Paused;
        Time.timeScale = 0f;
        OnChanged?.Invoke();
    }

    /// <summary>继续游戏。</summary>
    public void Resume()
    {
        if (State != GameState.Paused) return;
        State = GameState.Playing;
        Time.timeScale = 1f;
        OnChanged?.Invoke();
    }

    // ---------------- 失败 / 重开 ----------------
    public void GameOver()
    {
        if (State == GameState.GameOver) return;
        State = GameState.GameOver;
        Time.timeScale = 0f;

        // 记录最高分
        if (Score > BestScore)
        {
            BestScore = Score;
            PlayerPrefs.SetInt(BestKeyFor(LeaderboardClient.PlayerName), BestScore);
            PlayerPrefs.Save();
        }

        SaveRun();
        OnGameOver?.Invoke();
    }

    /// <summary>重开场景。skipMenu = true 时直接开新一局，不经过主菜单。</summary>
    public void Restart(bool skipMenu = false)
    {
        AutoStartNextRun = skipMenu;
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    /// <summary>「重新挑战」用：重开并跳过主菜单。</summary>
    public void RestartRun()
    {
        Restart(true);
    }

    /// <summary>场景重载后是否直接开局（由「重新挑战」按钮设置）。</summary>
    private static bool AutoStartNextRun;

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        Time.timeScale = 1f;   // 防止退出 Play 时卡在暂停
    }
}
