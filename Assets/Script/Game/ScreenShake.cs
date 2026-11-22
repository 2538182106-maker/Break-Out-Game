using UnityEngine;

/// <summary>
/// 【屏幕震动】挂在 Main Camera 上，其它脚本用 ScreenShake.Shake(...) 调用。
/// 俯视相机只抖水平面，幅度会随时间衰减。
/// </summary>
public class ScreenShake : MonoBehaviour
{
    private static ScreenShake instance;

    private Vector3 basePosition;
    private float magnitude;
    private float remaining;
    private float total;

    private void Awake()
    {
        instance = this;
        basePosition = transform.localPosition;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    /// <summary>叠加一次震动（取较大的幅度/时长，不会互相覆盖）。</summary>
    public static void Shake(float strength, float duration)
    {
        if (instance == null) return;
        instance.magnitude = Mathf.Max(instance.magnitude, strength);
        instance.remaining = Mathf.Max(instance.remaining, duration);
        instance.total = Mathf.Max(instance.total, duration);
    }

    private void Update()
    {
        if (remaining <= 0f) return;

        remaining -= Time.deltaTime;
        if (remaining <= 0f)
        {
            transform.localPosition = basePosition;
            magnitude = 0f;
            return;
        }

        float k = total > 0f ? Mathf.Clamp01(remaining / total) : 0f;
        Vector3 offset = Random.insideUnitSphere * (magnitude * k);
        offset.y = 0f;   // 俯视相机：只抖平面
        transform.localPosition = basePosition + offset;
    }
}
