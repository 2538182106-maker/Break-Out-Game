using UnityEngine;

/// <summary>
/// 【经验球】砖块死亡后掉落，飞向【玩家】并结算经验。
/// 走对象池（SimplePool），不再每次 Instantiate/Destroy。
/// </summary>
public class XpOrb : MonoBehaviour
{
    private const string PoolKey = "XpOrb";

    public int value = 1;

    private static Transform cachedPlayer;

    private Vector3 startPosition;
    private Transform target;
    private float t;
    private float duration = 0.45f;

    /// <summary>找玩家（带缓存；场景重载后缓存为 null 会自动重找）。</summary>
    private static Transform FindPlayer()
    {
        if (cachedPlayer != null) return cachedPlayer;

        PlayerControl player = Object.FindObjectOfType<PlayerControl>();
        if (player != null)
        {
            // PlayerControl 在父物体上，可见挡板是子物体
            cachedPlayer = player.transform.childCount > 0 ? player.transform.GetChild(0) : player.transform;
            return cachedPlayer;
        }
        return null;
    }

    private static GameObject CreateOrb()
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Collider col = go.GetComponent<Collider>();
        if (col != null) { col.enabled = false; Object.Destroy(col); }   // 先禁用，Destroy 当帧还生效
        go.name = "XpOrb";

        Renderer r = go.GetComponent<Renderer>();
        if (r != null)
        {
            // 必须换成带 _EMISSION 关键字的材质，否则 MPB 设的发光不生效
            r.sharedMaterial = CombatFx.FxMaterial();

            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor("_BaseColor", new Color(0.25f, 0.7f, 0.45f));
            mpb.SetColor("_EmissionColor", new Color(0.45f, 1f, 0.65f) * 1.5f);
            r.SetPropertyBlock(mpb);
        }

        go.AddComponent<XpOrb>();
        return go;
    }

    public static void Spawn(Vector3 position, int value)
    {
        GameObject go = SimplePool.Get(PoolKey, CreateOrb);

        XpOrb orb = go.GetComponent<XpOrb>();
        orb.value = value;
        orb.startPosition = position;
        orb.target = FindPlayer();
        orb.t = 0f;

        go.transform.position = position;
        go.transform.localScale = Vector3.one * 0.18f;

        // 按距离算飞行时间：远的飞久一点
        if (orb.target != null)
        {
            float distance = Vector3.Distance(position, orb.target.position);
            orb.duration = Mathf.Clamp(distance / 22f, 0.3f, 1.1f);
        }
        else
        {
            orb.duration = 0.5f;
        }
    }

    private void Update()
    {
        t += Time.deltaTime;

        if (target == null) target = FindPlayer();

        if (target != null)
        {
            Vector3 targetPos = target.position + Vector3.up * 0.4f;
            float k = Mathf.Clamp01(t / duration);

            Vector3 pos = Vector3.Lerp(startPosition, targetPos, k * k);
            pos.y += Mathf.Sin(k * Mathf.PI) * 0.35f;
            transform.position = pos;

            if (k >= 1f)
            {
                GameManager.Instance?.AddXp(value);
                SimplePool.Release(PoolKey, gameObject);
                return;
            }
        }
        else if (t > 1f)
        {
            GameManager.Instance?.AddXp(value);
            SimplePool.Release(PoolKey, gameObject);
            return;
        }

        transform.localScale = Vector3.one * Mathf.Lerp(0.18f, 0.06f, Mathf.Clamp01(t / duration));
    }
}
