using UnityEngine;

/// <summary>
/// 【程序化音效】没有音频素材也能有打击感。
/// 启动时用代码合成波形（正弦 + 噪声 + 包络），生成 AudioClip，再用几个 AudioSource 轮播。
/// 想换成真实音效时，只要把 GetClip 里的对象换成 Resources 加载即可。
/// </summary>
public class SoundFx : MonoBehaviour
{
    private const int SampleRate = 44100;

    private static SoundFx instance;

    private AudioClip shootClip;
    private AudioClip hitClip;
    private AudioClip killClip;
    private AudioClip boomClip;
    private AudioClip empClip;
    private AudioClip levelClip;

    private readonly AudioSource[] sources = new AudioSource[4];
    private int cursor;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        GameObject go = new GameObject("~SoundFx");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<SoundFx>();
        instance.Init();
    }

    private void Init()
    {
        for (int i = 0; i < sources.Length; i++)
        {
            AudioSource s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f;      // 2D
            s.volume = 0.5f;
            sources[i] = s;
        }

        shootClip = MakeShoot();
        hitClip = MakeHit();
        killClip = MakeKill();
        boomClip = MakeBoom();
        empClip = MakeEmp();
        levelClip = MakeLevelUp();
    }

    private void Play(AudioClip clip, float volume, float pitch = 1f)
    {
        if (clip == null) return;

        AudioSource s = sources[cursor];
        cursor = (cursor + 1) % sources.Length;
        s.pitch = pitch;
        s.PlayOneShot(clip, Mathf.Clamp01(volume * AudioSettings.SfxVolume));
    }

    // ---------------- 对外的接口 ----------------
    public static void Shoot() => instance?.Play(instance.shootClip, 0.18f, Random.Range(0.95f, 1.06f));
    public static void Hit() => instance?.Play(instance.hitClip, 0.28f, Random.Range(0.9f, 1.1f));
    public static void Kill() => instance?.Play(instance.killClip, 0.35f, Random.Range(0.92f, 1.08f));
    public static void Boom() => instance?.Play(instance.boomClip, 0.5f, Random.Range(0.9f, 1.05f));
    public static void Emp() => instance?.Play(instance.empClip, 0.32f, Random.Range(0.95f, 1.05f));
    public static void LevelUp() => instance?.Play(instance.levelClip, 0.4f, 1f);

    // ---------------- 波形合成 ----------------
    private static AudioClip Build(string name, float seconds, System.Func<float, float> sample)
    {
        int count = Mathf.Max(1, Mathf.RoundToInt(seconds * SampleRate));
        float[] data = new float[count];
        for (int i = 0; i < count; i++)
        {
            float t = (float)i / SampleRate;
            data[i] = Mathf.Clamp(sample(t), -1f, 1f);
        }

        AudioClip clip = AudioClip.Create(name, count, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    /// <summary>开火：短促的下滑方波 + 一点噪声。</summary>
    private static AudioClip MakeShoot()
    {
        return Build("sfx_shoot", 0.08f, t =>
        {
            float env = Mathf.Exp(-t * 55f);
            float freq = Mathf.Lerp(1200f, 500f, t / 0.08f);
            float wave = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * freq * t)) * 0.35f;
            float noise = (Random.value * 2f - 1f) * 0.25f;
            return (wave + noise) * env * 0.9f;
        });
    }

    /// <summary>命中：噪声爆破。</summary>
    private static AudioClip MakeHit()
    {
        return Build("sfx_hit", 0.09f, t =>
        {
            float env = Mathf.Exp(-t * 45f);
            float noise = (Random.value * 2f - 1f);
            float tone = Mathf.Sin(2f * Mathf.PI * 220f * t) * 0.25f;
            return (noise * 0.55f + tone) * env;
        });
    }

    /// <summary>击杀：向下扫频 + 噪声尾巴。</summary>
    private static AudioClip MakeKill()
    {
        return Build("sfx_kill", 0.28f, t =>
        {
            float env = Mathf.Exp(-t * 10f);
            float freq = Mathf.Lerp(700f, 120f, Mathf.Clamp01(t / 0.28f));
            float tone = Mathf.Sin(2f * Mathf.PI * freq * t) * 0.5f;
            float noise = (Random.value * 2f - 1f) * 0.3f * Mathf.Exp(-t * 25f);
            return (tone + noise) * env;
        });
    }

    /// <summary>爆炸：低频轰。</summary>
    private static AudioClip MakeBoom()
    {
        return Build("sfx_boom", 0.45f, t =>
        {
            float env = Mathf.Exp(-t * 7f);
            float freq = Mathf.Lerp(90f, 35f, t / 0.45f);
            float tone = Mathf.Sin(2f * Mathf.PI * freq * t) * 0.8f;
            float noise = (Random.value * 2f - 1f) * 0.45f * Mathf.Exp(-t * 12f);
            return (tone + noise) * env;
        });
    }

    /// <summary>电磁：金属感的颤音。</summary>
    private static AudioClip MakeEmp()
    {
        return Build("sfx_emp", 0.35f, t =>
        {
            float env = Mathf.Exp(-t * 6f);
            float mod = Mathf.Sin(2f * Mathf.PI * 28f * t) * 0.5f + 0.5f;
            float tone = Mathf.Sin(2f * Mathf.PI * 420f * t) * 0.4f * mod;
            float hiss = (Random.value * 2f - 1f) * 0.12f;
            return (tone + hiss) * env;
        });
    }

    /// <summary>升级：两段上行音。</summary>
    private static AudioClip MakeLevelUp()
    {
        return Build("sfx_level", 0.4f, t =>
        {
            float k = t / 0.4f;
            float freq = k < 0.5f ? 520f : 780f;
            float env = Mathf.Min(1f, t * 40f) * Mathf.Exp(-t * 3.5f);
            return Mathf.Sin(2f * Mathf.PI * freq * t) * 0.45f * env;
        });
    }
}
