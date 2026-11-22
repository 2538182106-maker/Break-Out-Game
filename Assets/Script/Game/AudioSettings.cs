using System;
using UnityEngine;

/// <summary>
/// 【音频设置】音乐 / 音效音量，存 PlayerPrefs，改动会广播 Changed 事件。
/// 所有音频播放都乘这两个值，实现全局音量控制。
/// </summary>
public static class AudioSettings
{
    private const string MusicKey = "Audio.MusicVolume";
    private const string SfxKey = "Audio.SfxVolume";

    public const float DefaultMusic = 0.55f;
    public const float DefaultSfx = 0.85f;

    public static float MusicVolume { get; private set; } = DefaultMusic;
    public static float SfxVolume { get; private set; } = DefaultSfx;

    /// <summary>音量变化（0~1 已改变并保存）</summary>
    public static event Action Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        Load();
    }

    /// <summary>确保已从 PlayerPrefs 读取（其它单例初始化时调用）。</summary>
    public static void EnsureLoaded()
    {
        Load();
    }

    private static void Load()
    {
        MusicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicKey, DefaultMusic));
        SfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(SfxKey, DefaultSfx));
    }

    public static void SetMusic(float value)
    {
        MusicVolume = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(MusicKey, MusicVolume);
        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    public static void SetSfx(float value)
    {
        SfxVolume = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(SfxKey, SfxVolume);
        PlayerPrefs.Save();
        Changed?.Invoke();
    }
}
