using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 【伤害数字】世界坐标弹出 → 屏幕上显示 → 先放大再缩小淡出。
/// 自建一个 ScreenSpaceOverlay 画布 + Text 池，不需要场景配置。
/// </summary>
public class DamageNumber : MonoBehaviour
{
    private class Item
    {
        public Text Text;
        public Vector3 WorldPoint;
        public float Time;
        public float Life;
        public float Scale;
        public bool Crit;
    }

    private static DamageNumber instance;
    private static Font font;

    private Canvas canvas;
    private readonly List<Item> items = new List<Item>();
    private readonly Stack<Text> pool = new Stack<Text>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        GameObject go = new GameObject("~DamageNumbers");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<DamageNumber>();
        instance.Init();
    }

    private void Init()
    {
        GameObject canvasGo = new GameObject("Canvas");
        canvasGo.transform.SetParent(transform, false);
        canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90;

        font = Font.CreateDynamicFontFromOSFont("Microsoft YaHei", 32);
        if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    /// <summary>在世界坐标处弹一个伤害数字。crit = 暴击（金色、更大）</summary>
    /// <summary>同屏最多显示多少个伤害数字（超了回收最旧的，避免后期 UI 重建风暴）。</summary>
    private const int MaxItems = 20;
    private static Camera cachedCamera;

    public static void Spawn(Vector3 worldPoint, float amount, Color color, bool crit = false)
    {
        if (instance == null) return;

        // 数量封顶：超出就立刻回收最旧的那个
        while (instance.items.Count >= MaxItems)
        {
            Item oldest = instance.items[0];
            instance.items.RemoveAt(0);
            if (oldest != null && oldest.Text != null)
            {
                oldest.Text.gameObject.SetActive(false);
                instance.pool.Push(oldest.Text);
            }
        }

        Text text = instance.pool.Count > 0 ? instance.pool.Pop() : instance.CreateText();
        text.gameObject.SetActive(true);
        text.text = Mathf.RoundToInt(amount).ToString();
        text.color = crit ? new Color(1f, 0.85f, 0.25f) : color;
        text.fontSize = crit ? 44 : 34;

        instance.items.Add(new Item
        {
            Text = text,
            WorldPoint = worldPoint + Random.insideUnitSphere * 0.15f,
            Time = 0f,
            Life = crit ? 0.8f : 0.62f,
            Scale = 1f,
            Crit = crit,
        });
    }

    private Text CreateText()
    {
        GameObject go = new GameObject("Dmg");
        go.transform.SetParent(canvas.transform, false);

        Text text = go.AddComponent<Text>();
        text.font = font;
        text.fontSize = 34;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;

        RectTransform rect = text.rectTransform;
        rect.sizeDelta = new Vector2(240f, 60f);
        return text;
    }

    private void Update()
    {
        // Camera.main 内部是标签查找，伤害数字多时每帧都查不值当
        if (cachedCamera == null) cachedCamera = Camera.main;
        Camera cam = cachedCamera;
        if (cam == null) return;

        for (int i = items.Count - 1; i >= 0; i--)
        {
            Item item = items[i];
            item.Time += Time.unscaledDeltaTime;

            float k = Mathf.Clamp01(item.Time / item.Life);

            // 先放大（前 20%），再慢慢缩小；暴击弹得更大
            float pop = item.Crit ? 1.8f : 1.2f;
            float end = item.Crit ? 0.75f : 0.55f;
            float scale = k < 0.2f
                ? Mathf.Lerp(pop, 1f, k / 0.2f)
                : Mathf.Lerp(1f, end, (k - 0.2f) / 0.8f);

            Color c = item.Text.color;
            c.a = 1f - Mathf.Pow(k, 2f);
            item.Text.color = c;

            Vector3 world = item.WorldPoint + Vector3.up * (k * 1.2f);
            Vector3 screen = cam.WorldToScreenPoint(world);
            item.Text.rectTransform.position = screen;
            item.Text.rectTransform.localScale = Vector3.one * scale;

            if (k >= 1f)
            {
                item.Text.gameObject.SetActive(false);
                pool.Push(item.Text);
                items.RemoveAt(i);
            }
        }
    }
}
