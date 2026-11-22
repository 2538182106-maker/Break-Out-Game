using UnityEngine;

/// <summary>
/// 【卡肉感 / Hit Stop】命中或击杀时把时间短暂拉慢（不是完全停），再弹回。
/// 这是动作游戏最便宜的打击反馈手段之一。
///
/// 注意：
///  - 用 unscaledDeltaTime 计时，否则 timeScale 被自己拉低后永远恢复不回来
///  - 游戏本身在暂停（Time.timeScale == 0，比如选卡）时不做任何事
///  - 通过 RuntimeInitializeOnLoadMethod 自建，不需要在场景里挂
/// </summary>
public class HitStop : MonoBehaviour
{
    private static HitStop instance;

    /// <summary>
    /// 总开关。连续射击时每次命中都触发会让 timeScale 长期压在 5%，
    /// 表现就是「子弹停滞、游戏卡」——所以默认关掉。想要卡肉改回 true 即可。
    /// </summary>
    public static bool Enabled = false;

    private float remaining;
    private float slowScale = 0.05f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        GameObject go = new GameObject("~HitStop");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<HitStop>();
    }

    /// <summary>触发一次卡肉。duration 是真实时间（秒）。</summary>
    public static void Do(float duration = 0.04f, float scale = 0.05f)
    {
        if (!Enabled) return;
        if (instance == null) return;
        if (Time.timeScale <= 0.01f) return;   // 正在选卡/结算，别掺和

        instance.slowScale = Mathf.Clamp(scale, 0.01f, 0.9f);
        instance.remaining = Mathf.Max(instance.remaining, duration);
        Time.timeScale = instance.slowScale;
    }

    private void Update()
    {
        if (remaining <= 0f) return;

        remaining -= Time.unscaledDeltaTime;
        if (remaining <= 0f)
        {
            // 只在没有被别人改成暂停的情况下恢复
            if (Time.timeScale < 0.5f) Time.timeScale = 1f;
        }
    }
}
