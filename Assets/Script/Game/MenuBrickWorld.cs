using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 主菜单里用「游戏内的砖块预制体」做装饰：从右往左飘，出屏后回到右边。
///
/// 分层靠相机距离：
///   depth 小（近）→ 画在 UI 上面
///   depth 大（远）→ 画在背景与 UI 之间
/// 因为菜单 Canvas 用的是 ScreenSpaceCamera，会参与深度排序。
/// </summary>
public class MenuBrickWorld : MonoBehaviour
{
    private class Item
    {
        public Transform tr;
        public float depth;     // 距相机多远
        public float x;         // -1..1 归一化横向位置
        public float y;         // -1..1 归一化纵向位置
        public float speed;     // 1/秒，归一化单位
        public float tilt;
        public float bobPhase;
        public Color color;
        public float respawnAt;
    }

    private readonly List<Item> items = new List<Item>();
    private Camera cam;
    private float halfFovTan;

    /// <summary>当前存活的装饰砖块数量（0 表示被清空了，需要重建）。</summary>
    public int Count
    {
        get
        {
            int n = 0;
            foreach (Item it in items) if (it.tr != null) n++;
            return n;
        }
    }

    public bool IsReady => cam != null && halfFovTan > 0f;

    private static readonly Color[] Palette =
    {
        new Color(0.25f, 0.90f, 1.00f),   // 普通
        new Color(0.35f, 1.00f, 0.60f),   // 装甲
        new Color(1.00f, 0.50f, 0.15f),   // 爆炸
        new Color(0.70f, 0.45f, 1.00f),   // 电磁
        new Color(0.95f, 0.90f, 0.30f),   // 分裂
    };

    public void Build(GameObject prefab, int frontCount, int backCount,
                      float frontDepth, float backDepth)
    {
        cam = Camera.main;
        if (cam == null) return;

        halfFovTan = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);

        for (int i = 0; i < frontCount; i++) Spawn(prefab, frontDepth, 0.9f);
        for (int i = 0; i < backCount; i++) Spawn(prefab, backDepth, 1.0f);
    }

    private void Spawn(GameObject prefab, float depth, float opacity)
    {
        GameObject go;
        if (prefab != null)
        {
            go = Instantiate(prefab, transform);
            // 关掉游戏逻辑：不要让它自己往左跑、不要物理、不要碰撞
            Brick brick = go.GetComponent<Brick>();
            if (brick != null) brick.enabled = false;
            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;
            Collider[] cols = go.GetComponentsInChildren<Collider>();
            foreach (Collider c in cols) c.enabled = false;
            foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>())
            {
                if (mb is Brick) mb.enabled = false;
            }
        }
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            go.transform.localScale = new Vector3(1f, 0.5f, 1.5f);
        }

        go.name = "MenuBrick";

        Color tint = Palette[Random.Range(0, Palette.Length)];
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
        MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        foreach (Renderer r in renderers)
        {
            r.GetPropertyBlock(mpb);
            mpb.SetColor("_BaseColor", tint * 0.35f);
            mpb.SetColor("_EmissionColor", tint * 1.6f * opacity);
            r.SetPropertyBlock(mpb);
        }

        // 形状随机：菜单里单独用更"敦实"的比例，不再照搬游戏里 (1, 0.5, 1.5) 的细长条
        //   宽 1.0 / 厚 0.75 / 长 1.15 为基准，再做 0.85~1.2 的轻微随机
        float k = Random.Range(0.10f, 0.16f) * depth * 0.55f;
        Vector3 jitter = new Vector3(
            Random.Range(0.85f, 1.20f),   // 宽
            Random.Range(0.85f, 1.20f),   // 高（厚度）
            Random.Range(0.80f, 1.15f));  // 长
        Vector3 baseScale = new Vector3(1.0f, 0.75f, 1.15f);

        go.transform.localScale = Vector3.Scale(baseScale, jitter) * (k * 2f);

        // 朝向相机，再随机绕自身转一点，避免全是一个角度
        go.transform.rotation = Quaternion.LookRotation(-cam.transform.forward, cam.transform.up)
                                * Quaternion.Euler(0f, Random.Range(-30f, 30f), Random.Range(-20f, 20f));

        items.Add(new Item
        {
            tr = go.transform,
            depth = depth,
            x = Random.Range(-1.15f, 1.15f),
            y = Random.Range(-0.55f, 0.55f),
            speed = Random.Range(0.06f, 0.16f),
            tilt = Random.Range(-14f, 14f),
            bobPhase = Random.Range(0f, 6.28f),
            color = tint,
        });
    }

    // ------------------------------------------------------------------
    // 点击打碎
    // ------------------------------------------------------------------
    private void HandleClicks()
    {
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;

        // 点在 UI 按钮/输入框上时不碎砖块
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;

        if (cam == null) return;

        Vector2 mousePos = mouse.position.ReadValue();

        foreach (Item it in items)
        {
            if (it.tr == null || !it.tr.gameObject.activeSelf) continue;

            Vector3 screenPoint = cam.WorldToScreenPoint(it.tr.position);
            if (screenPoint.z <= 0f) continue;

            // 用砖块在屏幕上的实际半径做判定（原来的固定 46px 只能点到中心）
            Vector3 edge = cam.WorldToScreenPoint(
                it.tr.position + cam.transform.right * (it.tr.localScale.x * 0.5f));
            float radius = Mathf.Max(40f, Vector2.Distance(
                new Vector2(screenPoint.x, screenPoint.y),
                new Vector2(edge.x, edge.y)));

            float dist = Vector2.Distance(new Vector2(screenPoint.x, screenPoint.y), mousePos);
            if (dist > radius) continue;

            BreakBrick(it);
            return;   // 一次点击只碎一块
        }
    }

    private void BreakBrick(Item it)
    {
        // 碎片大小以砖块本体为基准：Shatter 内部会再乘 0.14~0.38
        Vector3 fragScale = it.tr.localScale;      // 碎片尺寸由 ShatterGlow 内部按比例计算

        CombatFx.ShatterGlow(it.tr.position, it.color, 16, fragScale);   // 加法材质，保证能看见发光
        CombatFx.Ring(it.tr.position, it.color, 0.9f, 0.18f);   // 碎裂闪光
        CombatFx.Spark(it.tr.position, it.color, 6);
        SoundFx.Kill();

        it.tr.gameObject.SetActive(false);
        it.respawnAt = Time.unscaledTime + 1.2f;
    }
    private void Update()
    {
        // 场景重载会销毁旧相机，这里重新抓一次
        if (cam == null)
        {
            cam = Camera.main;
            if (cam != null) halfFovTan = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            else return;
        }

        if (halfFovTan <= 0f) return;

        float t = Time.unscaledTime;

        HandleClicks();

        foreach (Item it in items)
        {
            if (it.tr == null) continue;

            // 被点碎的砖块：到时间后从右边重新飘出来
            if (!it.tr.gameObject.activeSelf)
            {
                if (Time.unscaledTime < it.respawnAt) continue;

                it.tr.gameObject.SetActive(true);
                it.x = 1.2f;
                it.y = Random.Range(-0.55f, 0.55f);
            }

            it.x -= it.speed * Time.unscaledDeltaTime;
            if (it.x < -1.25f) it.x = 1.25f;

            float d = it.depth;
            float halfH = d * halfFovTan;
            float halfW = halfH * cam.aspect;

            float bob = Mathf.Sin(t * 0.9f + it.bobPhase) * 0.03f;

            Vector3 pos = cam.transform.position
                          + cam.transform.forward * d
                          + cam.transform.right * (it.x * halfW)
                          + cam.transform.up * ((it.y + bob) * halfH);

            it.tr.position = pos;
            it.tr.rotation = Quaternion.LookRotation(-cam.transform.forward, cam.transform.up)
                             * Quaternion.Euler(0f, 0f, it.tilt);
        }
    }
}
