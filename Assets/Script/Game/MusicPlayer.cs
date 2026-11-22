using UnityEngine;

/// <summary>
/// 【背景音乐】常驻单例，从 Assets/Resources/Music 加载所有音频，随机顺序循环播放。
/// 音量走 AudioSettings.MusicVolume；菜单暂停（timeScale=0）时音乐照常播放。
/// </summary>
public class MusicPlayer : MonoBehaviour
{
    public static MusicPlayer Instance { get; private set; }

    /// <summary>音乐相对音效的基础音量系数（避免音乐盖过打击音）</summary>
    private const float BaseVolume = 0.65f;

    private AudioSource source;
    private AudioClip[] clips;
    private int index;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        if (Instance != null) return;

        GameObject go = new GameObject("~MusicPlayer");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<MusicPlayer>();
    }

    private void Awake()
    {
        Instance = this;
        AudioSettings.EnsureLoaded();   // 防止初始化顺序导致读到默认值

        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f;
        source.volume = AudioSettings.MusicVolume * BaseVolume;

        // 音乐放在 Resources 里同步加载：只要 audio clip 的 Load Type 是 Streaming，
        // 这里就只是拿到引用，不会把整首曲子解压进内存（那会卡住启动好几秒）。
        clips = Resources.LoadAll<AudioClip>("Music");
        if (clips == null || clips.Length == 0)
        {
            Debug.LogWarning("[MusicPlayer] Resources/Music 下没有音频文件");
            return;
        }

        Shuffle();
        PlayCurrent();

        Debug.Log($"[MusicPlayer] 已载入 {clips.Length} 首背景音乐");

        AudioSettings.Changed += ApplyVolume;
    }

    private void OnDestroy()
    {
        AudioSettings.Changed -= ApplyVolume;
        if (Instance == this) Instance = null;
    }

    private void ApplyVolume()
    {
        if (source != null) source.volume = AudioSettings.MusicVolume * BaseVolume;
    }

    private void Update()
    {
        if (clips == null || clips.Length == 0 || source == null) return;

        // 一首播完切下一首
        if (!source.isPlaying)
        {
            index++;
            if (index >= clips.Length)
            {
                index = 0;
                Shuffle();
            }
            PlayCurrent();
        }
    }

    private void PlayCurrent()
    {
        if (clips == null || clips.Length == 0) return;

        source.clip = clips[Mathf.Clamp(index, 0, clips.Length - 1)];
        source.volume = AudioSettings.MusicVolume * BaseVolume;
        source.Play();
    }

    private void Shuffle()
    {
        for (int i = clips.Length - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (clips[i], clips[j]) = (clips[j], clips[i]);
        }
    }
}
