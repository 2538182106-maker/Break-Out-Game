using UnityEngine;

/// <summary>
/// 【回弹】给任意 Transform 做「打一下再弹回去」的位移动画。
/// 玩家开火时后退一下、被打中时抖一下都用它。
/// </summary>
public class Recoil : MonoBehaviour
{
    [Tooltip("回弹方向（本地坐标）。玩家开火建议用 -forward 的反方向")]
    public Vector3 localDirection = Vector3.left;

    [Tooltip("回弹距离（米）")]
    public float distance = 0.06f;

    [Tooltip("一次回弹的总时长（秒）")]
    public float duration = 0.12f;

    private Vector3 basePosition;
    private float t = -1f;

    private void Awake()
    {
        basePosition = transform.localPosition;
    }

    /// <summary>触发一次回弹。</summary>
    public void Kick()
    {
        t = 0f;
    }

    private void Update()
    {
        if (t < 0f) return;

        t += Time.deltaTime;
        float k = Mathf.Clamp01(t / Mathf.Max(0.01f, duration));

        // 0 → 1 → 0 的位移曲线
        float offset = Mathf.Sin(k * Mathf.PI) * distance;
        transform.localPosition = basePosition + localDirection.normalized * offset;

        if (k >= 1f)
        {
            transform.localPosition = basePosition;
            t = -1f;
        }
    }
}
