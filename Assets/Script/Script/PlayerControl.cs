using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 【玩家移动】WASD 移动 + 被砖块 / 墙 / deadline 阻挡。
///
/// 阻挡用 OverlapBox 预判：先算目标位置，撞了就分轴重试（贴墙时还能顺着墙滑）。
/// 这样不需要给玩家加刚体，也不会被砖块推着走。
/// </summary>
public class PlayerControl : MonoBehaviour
{
    [Header("移动")]
    public float speed = 5f;

    [Tooltip("参与阻挡的层")]
    public LayerMask blockingMask = ~0;

    private float movementX;
    private float movementZ;
    private Transform paddle;         // 可见模型（带碰撞体）
    private Collider paddleCollider;

    private void Awake()
    {
        paddle = transform.childCount > 0 ? transform.GetChild(0) : transform;
        paddleCollider = GetComponentInChildren<Collider>();
    }

    /// <summary>移动输入（Value 类型 Action，Send Messages 能正常转发）。</summary>
    public void OnMove(InputValue value)
    {
        Vector2 v = value.Get<Vector2>();
        movementX = v.x;
        movementZ = v.y;
    }

    private void Update()
    {
        Vector3 input = new Vector3(movementX, 0f, movementZ);
        if (input.sqrMagnitude < 0.0001f) return;

        Vector3 delta = input * (speed * Time.deltaTime);

        // 已经被卡住（比如砖块挤到身上）→ 先允许移动，保证能挣脱出来
        if (Blocked(Vector3.zero)) { transform.position += delta; return; }

        // 先试整段位移，再分轴
        if (!Blocked(delta)) { transform.position += delta; return; }

        Vector3 dx = new Vector3(delta.x, 0f, 0f);
        if (Mathf.Abs(delta.x) > 0.0001f && !Blocked(dx)) transform.position += dx;

        Vector3 dz = new Vector3(0f, 0f, delta.z);
        if (Mathf.Abs(delta.z) > 0.0001f && !Blocked(dz)) transform.position += dz;
    }

    /// <summary>把位移加到碰撞体上，看会不会撞到东西。</summary>
    private bool Blocked(Vector3 delta)
    {
        Vector3 center;
        Vector3 halfExtents;

        if (paddleCollider is BoxCollider box)
        {
            center = paddleCollider.transform.TransformPoint(box.center) + delta;
            Vector3 lossy = paddleCollider.transform.lossyScale;
            halfExtents = Vector3.Scale(box.size * 0.5f, new Vector3(Mathf.Abs(lossy.x), Mathf.Abs(lossy.y), Mathf.Abs(lossy.z)));
        }
        else
        {
            center = (paddle != null ? paddle.position : transform.position) + delta;
            halfExtents = new Vector3(0.15f, 0.2f, 0.25f);
        }

        // 稍微缩小一点，避免自己贴着自己判定
        halfExtents *= 0.92f;

        Collider[] hits = Physics.OverlapBox(center, halfExtents, Quaternion.identity,
                                             blockingMask, QueryTriggerInteraction.Ignore);

        foreach (Collider hit in hits)
        {
            if (hit.transform.IsChildOf(transform)) continue;          // 忽略玩家自己
            if (hit.GetComponentInParent<Brick>() != null) continue;   // 砖块可以穿透
            return true;
        }
        return false;
    }
}
