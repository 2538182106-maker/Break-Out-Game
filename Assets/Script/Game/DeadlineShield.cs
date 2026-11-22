using UnityEngine;

/// <summary>
/// 【失败线护盾】砖块撞到失败线时，如果还有护盾次数，砖块会被弹死、护盾减一；
/// 次数用尽后才算失败。护盾按时间自动恢复。
///
/// 阶数效果（取 PlayerStats.shield）：
///   Ⅰ 1 层 / 120 秒   Ⅱ 1 层 / 90 秒   Ⅲ 2 层 / 90 秒
///   Ⅳ 2 层 / 60 秒    Ⅴ 3 层 / 60 秒
/// </summary>
public class DeadlineShield : MonoBehaviour
{
    public static DeadlineShield Instance { get; private set; }

    /// <summary>各阶护盾层数 / 恢复间隔，可由 config.json 覆盖。</summary>
    public static int[] MaxCharges = { 1, 1, 2, 2, 3 };
    public static float[] RechargeTime = { 120f, 90f, 90f, 60f, 60f };

    public int Charges { get; private set; }
    private float timer;
    private int lastTier = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Instance != null) return;

        GameObject go = new GameObject("~DeadlineShield");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<DeadlineShield>();
    }

    private void OnEnable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    /// <summary>重开一局：护盾状态重置</summary>
    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene,
                               UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        Charges = 0;
        timer = 0f;
        lastTier = -1;      // 让 Update 重新按当前阶数补满
    }

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private static int Tier => PlayerStats.Instance != null ? PlayerStats.Instance.shield : 0;

    /// <summary>当前阶数允许的最大层数</summary>
    public static int MaxForTier
    {
        get
        {
            int t = Mathf.Clamp(Tier, 1, MaxCharges.Length);
            return Tier <= 0 ? 0 : MaxCharges[t - 1];
        }
    }

    /// <summary>当前阶数的恢复间隔（秒）</summary>
    public static float RechargeForTier
    {
        get
        {
            int t = Mathf.Clamp(Tier, 1, RechargeTime.Length);
            return Tier <= 0 ? 999f : RechargeTime[t - 1];
        }
    }

    private void Update()
    {
        int tier = Tier;

        if (tier <= 0)
        {
            Charges = 0;
            timer = 0f;
            lastTier = 0;
            return;
        }

        // 刚拿到技能 / 升阶：直接补满，避免开局 0 层
        if (tier != lastTier)
        {
            lastTier = tier;
            Charges = MaxForTier;
            timer = 0f;
            return;
        }

        if (Charges < MaxForTier)
        {
            timer += Time.deltaTime;
            if (timer >= RechargeForTier)
            {
                timer = 0f;
                Charges = Mathf.Min(MaxForTier, Charges + 1);
                SoundFx.LevelUp();
            }
        }
        else
        {
            timer = 0f;
        }
    }

    /// <summary>砖块撞到失败线时调用：有护盾就消耗一层并返回 true。</summary>
    public static bool TryBlock()
    {
        if (Instance == null) return false;
        if (Instance.Charges <= 0) return false;

        Instance.Charges--;
        Instance.timer = 0f;
        return true;
    }

    /// <summary>距离下一层恢复的进度 0~1</summary>
    public float RechargeProgress01
    {
        get
        {
            if (Tier <= 0 || Charges >= MaxForTier) return 1f;
            return Mathf.Clamp01(timer / Mathf.Max(0.01f, RechargeForTier));
        }
    }
}
