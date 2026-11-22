using UnityEngine;

/// <summary>冲击波：一个扁球在很短时间内膨胀到目标半径然后消失。</summary>
public class FxRing : MonoBehaviour
{
    public float targetRadius = 1.5f;
    public float duration = 0.25f;

    private float t;
    private Vector3 startScale;
    private Vector3 flat = new Vector3(1f, 0.08f, 1f);

    private void Awake()
    {
        startScale = transform.localScale;
    }

    private void Update()
    {
        t += Time.deltaTime;
        float k = Mathf.Clamp01(t / duration);

        // 先快后慢地膨胀
        float radius = Mathf.Lerp(0.05f, targetRadius, 1f - (1f - k) * (1f - k));
        transform.localScale = new Vector3(radius, radius * flat.y, radius);

        if (k >= 1f) Destroy(gameObject);
    }
}
