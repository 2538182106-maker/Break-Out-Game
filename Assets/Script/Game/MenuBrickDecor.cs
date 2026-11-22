using UnityEngine;

/// <summary>
/// 主菜单的装饰砖块：从右往左飘，出屏后从右边重新出现。
/// 纯 UI 实现，方便控制「在标题下面」还是「在最上层」——靠父节点的层级顺序决定。
/// </summary>
public class MenuBrickDecor : MonoBehaviour
{
    public float speed = 70f;
    public float spawnRight = 1050f;
    public float killLeft = -1050f;
    public float verticalRange = 480f;

    private RectTransform rect;

    private void Awake()
    {
        rect = GetComponent<RectTransform>();
    }

    private void Update()
    {
        if (rect == null) return;

        Vector2 pos = rect.anchoredPosition;
        pos.x -= speed * Time.unscaledDeltaTime;

        if (pos.x < killLeft)
        {
            pos.x = spawnRight + Random.Range(0f, 300f);
            pos.y = Random.Range(-verticalRange, verticalRange);
        }

        rect.anchoredPosition = pos;
    }
}
