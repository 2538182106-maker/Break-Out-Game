using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 【特效对象池】火花/碎片用的方块全部从这里借还，运行时不再 Instantiate/Destroy。
/// 这是连续射击时 GC 抖动的主要来源，池化后基本消灭。
///
/// 用法：
///   GameObject bit = FxPool.Get(pos, scale, color, emission);
///   bit.GetComponent&lt;FxBit&gt;().velocity = ...;
///   // FxBit 寿命到了自己 Release，不需要手动管
/// </summary>
public class FxPool : MonoBehaviour
{
    private static FxPool instance;
    private readonly Stack<GameObject> pool = new Stack<GameObject>();

    private const int PrewarmCount = 80;

    /// <summary>同时存在的特效方块上限。后期火花/碎片能叠到 800+ 个，
    /// 每个都是一次 draw call（材质属性块不同无法合批）→ 直接拖垮帧率。</summary>
    private const int MaxActive = 180;

    private readonly List<GameObject> active = new List<GameObject>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        GameObject go = new GameObject("~FxPool");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<FxPool>();
        instance.Prewarm();
    }

    private void Prewarm()
    {
        for (int i = 0; i < PrewarmCount; i++) pool.Push(Create());
    }

    /// <summary>借一个特效方块（已设置好位置/缩放/颜色，并已激活）。</summary>
    public static GameObject Get(Vector3 position, Vector3 scale, Color color, float emission)
    {
        if (instance == null) return null;

        // 池子里可能残留被场景卸载销毁的对象（Unity 的"假 null"），跳过它们
        GameObject go = null;
        while (instance.pool.Count > 0)
        {
            GameObject candidate = instance.pool.Pop();
            if (candidate != null)
            {
                go = candidate;
                break;
            }
        }
        if (go == null) go = instance.Create();

        // 超过上限：强制回收最旧的几块，保证渲染器数量受控
        while (instance.active.Count >= MaxActive)
        {
            GameObject oldest = instance.active[0];
            instance.active.RemoveAt(0);
            if (oldest != null && oldest != go)
            {
                oldest.SetActive(false);
                instance.pool.Push(oldest);
            }
        }
        instance.active.Add(go);

        // 先摆好再激活，这样 FxBit.OnEnable 记录到的初始缩放是正确的
        go.transform.position = position;
        go.transform.rotation = Random.rotation;
        go.transform.localScale = scale;

        Renderer renderer = go.GetComponent<Renderer>();
        if (renderer != null) CombatFx.TintSolid(renderer, color, emission);

        go.SetActive(true);
        return go;
    }

    /// <summary>归还（FxBit 寿命结束时自动调用）。</summary>
    public static void Release(GameObject go)
    {
        if (instance == null || go == null) return;
        instance.active.Remove(go);
        go.SetActive(false);
        instance.pool.Push(go);
    }

    private GameObject Create()
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Collider col = go.GetComponent<Collider>();
        if (col != null) { col.enabled = false; Object.Destroy(col); }
        go.AddComponent<FxBit>();
        go.SetActive(false);

        // 挂到常驻的池对象下面：重载场景时不会被销毁，池引用不会失效
        if (instance != null) go.transform.SetParent(instance.transform, false);
        return go;
    }
}
