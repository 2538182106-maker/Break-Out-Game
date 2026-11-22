using System;
using System.Collections;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 【联网排行榜】提交分数 / 拉取 Top N。
/// 运行时自建（不占场景对象），失败不影响单机游玩。
///
/// 改服务器地址：直接改下面的 DefaultUrl，或者在运行前写 PlayerPrefs：
///   PlayerPrefs.SetString("LB.Url", "http://你的服务器:8000");
/// </summary>
public class LeaderboardClient : MonoBehaviour
{
    // ======== 部署后改这两个 ========
    private const string DefaultUrl = "http://127.0.0.1:8000";
    private const string DefaultSecret = "change-me";   // 必须和服务端 LB_SECRET 一致
    // ================================

    public static LeaderboardClient Instance { get; private set; }

    public string BaseUrl { get; private set; }
    private string secret;
    private const int Timeout = 6;

    // 多地址：server.txt 可以写多行，客户端依次尝试，用第一个通的
    private readonly System.Collections.Generic.List<string> endpointUrls = new System.Collections.Generic.List<string>();
    private readonly System.Collections.Generic.List<string> endpointSecrets = new System.Collections.Generic.List<string>();
    private int endpointIndex;

    /// <summary>排行榜显示条数（可由服务器 config.json 的 leaderboardLimit 覆盖，最大 100）</summary>
    /// <summary>排行榜最多能拉取多少条（改这里要同步服务端的 limit 上限）</summary>
    public const int MaxLimit = 100;

    public static int DisplayLimit { get; private set; } = 10;

    /// <summary>设置显示条数（1~50，服务端上限也是 50）。</summary>
    public static void SetDisplayLimit(int limit)
    {
        DisplayLimit = Mathf.Clamp(limit, 1, MaxLimit);
    }

    /// <summary>当前是否连得上服务器（每 8 秒探测一次 /health）</summary>
    public static bool IsOnline { get; private set; }
    /// <summary>是否已经完成过第一次探测（UI 用来区分"检测中"和"离线"）</summary>
    public static bool HasChecked { get; private set; }
    /// <summary>在线状态变化（UI 监听）</summary>
    public static event Action<bool> OnlineChanged;

    [Serializable]
    public class ScoreEntry
    {
        public string name;
        public int score;
        public int wave;
        public int level;
        public int kills;
        public string created_at;
    }

    [Serializable]
    private class ScoreList
    {
        public ScoreEntry[] items;
    }

    [Serializable]
    private class SubmitResult
    {
        public bool ok;
        public int rank;
    }

    [Serializable]
    private class SubmitPayload
    {
        public string name;
        public int score;
        public int wave;
        public int level;
        public int kills;
        public string checksum;
    }

    /// <summary>玩家名（首次启动自动生成，存在 PlayerPrefs）。主菜单里可以改。</summary>
    public static string PlayerName
    {
        get
        {
            string name = PlayerPrefs.GetString("LB.PlayerName", "");
            if (string.IsNullOrEmpty(name))
            {
                name = "玩家" + Guid.NewGuid().ToString("N").Substring(0, 4).ToUpper();
                PlayerPrefs.SetString("LB.PlayerName", name);
                PlayerPrefs.Save();
            }
            return name;
        }
    }

    /// <summary>设置玩家名（主菜单登录用）。空字符串会被忽略。</summary>
    public static void SetPlayerName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;

        name = name.Trim();
        if (name.Length > 16) name = name.Substring(0, 16);

        PlayerPrefs.SetString("LB.PlayerName", name);
        PlayerPrefs.Save();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        GameObject go = new GameObject("~LeaderboardClient");
        DontDestroyOnLoad(go);
        go.AddComponent<LeaderboardClient>();
    }

    private void Awake()
    {
        Instance = this;
        BaseUrl = PlayerPrefs.GetString("LB.Url", DefaultUrl).TrimEnd('/');
        secret = PlayerPrefs.GetString("LB.Secret", DefaultSecret);

        LoadConfigFile();
    }

    /// <summary>
    /// 优先读 StreamingAssets/server.txt，这样换服务器地址不用重新打包。
    ///
    /// 支持两种写法：
    /// 【多地址】每行 "地址|密钥"，客户端依次尝试，用第一个通的（推荐）
    ///     http://127.0.0.1:8000|change-me
    ///     http://1326jp29pe913.vicp.fun|change-me
    /// 【单地址】第 1 行地址、第 2 行密钥（老格式，仍然兼容）
    ///     http://127.0.0.1:8000
    ///     change-me
    /// </summary>
    private void LoadConfigFile()
    {
        endpointUrls.Clear();
        endpointSecrets.Clear();

        try
        {
            string path = System.IO.Path.Combine(Application.streamingAssetsPath, "server.txt");
            if (!System.IO.File.Exists(path)) return;

            string[] lines = System.IO.File.ReadAllLines(path);

            if (System.Array.Exists(lines, l => l.Contains("|")))
            {
                foreach (string raw in lines)
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;

                    int bar = line.IndexOf('|');
                    if (bar <= 0) continue;

                    string url = line.Substring(0, bar).Trim().TrimEnd('/');
                    string sec = line.Substring(bar + 1).Trim();
                    if (url.Length == 0) continue;

                    endpointUrls.Add(url);
                    endpointSecrets.Add(sec);
                }
            }
            else
            {
                // 老格式：第 1 行地址、第 2 行密钥
                string url = lines.Length > 0 ? lines[0].Trim().TrimEnd('/') : "";
                string sec = lines.Length > 1 ? lines[1].Trim() : "change-me";
                if (url.Length > 0)
                {
                    endpointUrls.Add(url);
                    endpointSecrets.Add(sec);
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[Leaderboard] 读取 server.txt 失败：{e.Message}");
        }

        if (endpointUrls.Count == 0)
        {
            endpointUrls.Add(BaseUrl);
            endpointSecrets.Add(secret);
        }

        endpointIndex = 0;
        ApplyEndpoint(0);
        Debug.Log($"[Leaderboard] 共 {endpointUrls.Count} 个候选地址，当前使用：{BaseUrl}");
    }

    private void ApplyEndpoint(int index)
    {
        endpointIndex = Mathf.Clamp(index, 0, endpointUrls.Count - 1);
        BaseUrl = endpointUrls[endpointIndex];
        secret = endpointSecrets[endpointIndex];
    }

    private void NextEndpoint()
    {
        if (endpointUrls.Count <= 1) return;
        int next = (endpointIndex + 1) % endpointUrls.Count;
        ApplyEndpoint(next);
        Debug.Log($"[Leaderboard] 切换到备用地址：{BaseUrl}");
    }

    private void Start()
    {
        StartCoroutine(HealthLoop());
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ------------------------------------------------------------------
    // 在线状态探测
    // ------------------------------------------------------------------
    private IEnumerator HealthLoop()
    {
        WaitForSecondsRealtime wait = new WaitForSecondsRealtime(8f);
        while (true)
        {
            yield return PingHealth();

            // 连不上且还有备用地址 → 换下一个再试
            if (!IsOnline && endpointUrls.Count > 1) NextEndpoint();

            yield return wait;
        }
    }

    private IEnumerator PingHealth()
    {
        using (UnityWebRequest req = UnityWebRequest.Get(BaseUrl + "/health"))
        {
            req.timeout = 2;
            req.certificateHandler = new TrustConfiguredHost();
            yield return req.SendWebRequest();
            SetOnline(req.result == UnityWebRequest.Result.Success);
        }
    }

    private static void SetOnline(bool online)
    {
        bool changed = IsOnline != online;
        bool firstCheck = !HasChecked;

        IsOnline = online;
        HasChecked = true;

        // 第一次探测也要通知一次，否则重开一局后 UI 会一直停在"检测中"
        if (changed || firstCheck) OnlineChanged?.Invoke(online);
    }

    // ------------------------------------------------------------------
    // 对外接口
    // ------------------------------------------------------------------
    /// <summary>提交分数。回调 (成功, 排名)。</summary>
    public void Submit(int score, int wave, int level, int kills, Action<bool, int> onDone)
    {
        SubmitPayload payload = new SubmitPayload
        {
            name = PlayerName,
            score = score,
            wave = wave,
            level = level,
            kills = kills,
            checksum = Checksum(PlayerName, score, wave, kills),
        };
        StartCoroutine(SubmitRoutine(payload, onDone));
    }

    /// <summary>拉取任意 JSON 接口（/config、/notice 等）。失败回调 false。</summary>
    public void FetchRaw(string path, Action<bool, string> onDone)
    {
        StartCoroutine(RawRoutine(path, onDone));
    }

    private IEnumerator RawRoutine(string path, Action<bool, string> onDone)
    {
        using (UnityWebRequest req = UnityWebRequest.Get(BaseUrl + path))
        {
            req.timeout = Timeout;
            req.certificateHandler = new TrustConfiguredHost();
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                onDone?.Invoke(false, null);
                yield break;
            }
            onDone?.Invoke(true, req.downloadHandler.text);
        }
    }

    /// <summary>拉取排行榜前 limit 名。失败回调 null。</summary>
    public void FetchTop(int limit, Action<ScoreEntry[]> onDone)
    {
        StartCoroutine(FetchRoutine(limit, onDone));
    }

    private string Checksum(string name, int score, int wave, int kills)
    {
        string raw = $"{name}|{score}|{wave}|{kills}|{secret}";
        using (SHA256 sha = SHA256.Create())
        {
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
            StringBuilder sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }

    /// <summary>
    /// 放宽 HTTPS 证书校验。
    /// 免费内网穿透（花生壳/natapp）的证书链在 Unity 的部分后端不被信任，
    /// 开着严格校验会直接连不上。地址来自玩家本地的 server.txt，属于"用户自己配置的可信源"。
    /// 注意：这会让中间人攻击成为可能——本项目是单机小游戏，可接受。
    /// </summary>
    private class TrustConfiguredHost : CertificateHandler
    {
        protected override bool ValidateCertificate(byte[] certificateData)
        {
            return true;
        }
    }

    // ------------------------------------------------------------------
    // 网络
    // ------------------------------------------------------------------
    private IEnumerator SubmitRoutine(SubmitPayload payload, Action<bool, int> onDone)
    {
        string url = BaseUrl + "/scores";
        byte[] body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload));

        using (UnityWebRequest req = new UnityWebRequest(url, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = Timeout;
            req.certificateHandler = new TrustConfiguredHost();

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[Leaderboard] 提交失败：{req.responseCode} {req.error}");
                SetOnline(false);
                onDone?.Invoke(false, -1);
                yield break;
            }

            SubmitResult result = null;
            try { result = JsonUtility.FromJson<SubmitResult>(req.downloadHandler.text); }
            catch (Exception e) { Debug.LogWarning($"[Leaderboard] 解析失败：{e.Message}"); }

            SetOnline(true);
            onDone?.Invoke(result != null && result.ok, result != null ? result.rank : -1);
        }
    }

    private IEnumerator FetchRoutine(int limit, Action<ScoreEntry[]> onDone)
    {
        string url = BaseUrl + "/scores?limit=" + Mathf.Clamp(limit, 1, MaxLimit);

        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            req.timeout = Timeout;
            req.certificateHandler = new TrustConfiguredHost();
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[Leaderboard] 拉取失败：{req.responseCode} {req.error}");
                SetOnline(false);
                onDone?.Invoke(null);
                yield break;
            }

            ScoreList list = null;
            try { list = JsonUtility.FromJson<ScoreList>(req.downloadHandler.text); }
            catch (Exception e) { Debug.LogWarning($"[Leaderboard] 解析失败：{e.Message}"); }

            onDone?.Invoke(list != null ? list.items : null);
        }
    }
}
