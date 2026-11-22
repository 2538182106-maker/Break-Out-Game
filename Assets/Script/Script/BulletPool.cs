using System.Collections.Generic;   // Queue<T>：先进先出队列，天然适合对象池
using UnityEngine;                  // MonoBehaviour / GameObject / Instantiate

/// <summary>
/// 【对象池】场景里只有一个（单例）。开局先造好 poolSize 发子弹并全部 SetActive(false)，
/// 之后打出去的子弹只是「从队列取出 + 激活」，打完「失活 + 放回队列」。
/// 好处：运行时几乎没有 Instantiate/Destroy，不会产生 GC 和卡顿。
///
/// 场景结构建议：
///   BulletPool  ← 挂本脚本（bulletPrefab 指向 Sphere.prefab）
///     ├─ 预热的子弹（运行时会出现在这里，全都在 Hierarchy 里隐藏）
///     └─ ...
/// </summary>
public class BulletPool : MonoBehaviour
{
    [Header("配置")]
    [Tooltip("子弹预制体。必须挂了 Bullet 脚本 + Rigidbody，否则子弹不会飞")]
    public GameObject bulletPrefab;

    [Tooltip("开局预先生成多少发。够玩家同时打出去的数量就行，太大会增加启动开销")]
    public int poolSize = 30;

    // 空闲子弹队列：Dequeue 取（出池），Enqueue 放（回池）
    private readonly Queue<GameObject> pool = new Queue<GameObject>();

    /// <summary>
    /// 单例：其他脚本用 BulletPool.Instance 直接访问，不用到处拖引用。
    /// private set 保证外部只能读、不能改。
    /// </summary>
    public static BulletPool Instance { get; private set; }

    /// <summary>
    /// Awake 在场景加载时、所有 Start 之前执行，适合做池子的初始化。
    /// </summary>
    private void Awake()
    {
        // 单例守卫：场景里出现第二个池子时，销毁自己并【立刻 return】
        // （旧代码忘了 return，导致重复的池子还会白造 30 发子弹）
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        // 配置缺失时早点报错：否则每次开火都要抛一次 UnassignedReferenceException
        if (bulletPrefab == null)
        {
            Debug.LogError("[BulletPool] bulletPrefab 未赋值：请把 Sphere.prefab 拖到 Inspector 上", this);
            enabled = false;   // 关掉自己，避免继续制造异常
            return;
        }

        Prewarm();             // 预热：一次性造满
    }

    /// <summary>
    /// 场景卸载/销毁时清空单例引用。
    /// 否则下次进场景 Instance 会指向一个「已销毁」的对象，出现真假 null 的怪问题。
    /// </summary>
    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// 预热：按 poolSize 批量生成，生成后立刻失活并挂到池子节点下。
    /// </summary>
    private void Prewarm()
    {
        for (int i = 0; i < poolSize; i++)
        {
            // 第三个参数 false = 不要保留世界坐标，直接作为池子的子物体（Hierarchy 干净）
            GameObject bullet = Instantiate(bulletPrefab, transform, false);
            bullet.SetActive(false);       // 失活：不渲染、不执行 Update、不参与物理
            pool.Enqueue(bullet);          // 放进空闲队列
        }
    }

    /// <summary>
    /// 从池里取一发子弹并激活。
    /// 顺序很重要：先 SetPositionAndRotation，再 SetActive(true) ——
    /// 因为 Bullet.OnEnable 会读 transform.forward 来定初速度，顺序反了子弹就会朝错方向飞。
    /// </summary>
    public GameObject GetBullet(Vector3 position, Quaternion rotation)
    {
        // 池子空了（玩家同时打出去的超过 poolSize）就临时补一发，它会正常参与后续回收
        GameObject bullet = pool.Count > 0
            ? pool.Dequeue()
            : Instantiate(bulletPrefab, transform, false);

        bullet.transform.SetPositionAndRotation(position, rotation);  // 摆到枪口
        bullet.SetActive(true);                                       // 激活 → 触发 Bullet.OnEnable
        return bullet;
    }

    /// <summary>
    /// 回收：失活 + 放回队列。
    /// 这里的 activeInHierarchy 判断是防重入的关键：
    /// 物体 SetActive(false) 时会触发 OnBecameInvisible 等回调，可能再次走到这里，
    /// 没有这道锁的话同一颗子弹会被 Enqueue 两次，池子里出现重复引用，后面就会“一发变两发”。
    /// </summary>
    public void ReturnBullet(GameObject bullet)
    {
        if (bullet == null) return;                // 已经被销毁
        if (!bullet.activeInHierarchy) return;     // 已经失活 = 已经在池里，直接忽略

        bullet.SetActive(false);                   // 失活（会触发 Bullet.OnDisable 清速度）
        bullet.transform.SetParent(transform, false); // 收回统一挂到池子节点下，Hierarchy 不越来越乱
        pool.Enqueue(bullet);                      // 回到空闲队列
    }
}
