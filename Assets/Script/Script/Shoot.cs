using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 【射击】读输入 → 按数值发子弹（多重子弹带扇形散布）→ 触发回弹。
/// 输入直接读 Mouse.current，原因见旧版注释（Button 类型 Action 收不到 canceled）。
/// </summary>
public class Shoot : MonoBehaviour
{
    [Header("枪口")]
    [Tooltip("子弹的出生点：位置 = 枪口，forward = 射击方向")]
    public Transform firePoint;

    [Header("基础")]
    [Tooltip("基础射击间隔（会被 PlayerStats.FireInterval 覆盖）")]
    public float fireRate = 0.25f;

    [Tooltip("多发时的扇形角间隔（度）")]
    public float spreadDegrees = 4f;

    [Header("射击")]
    [Tooltip("自动开火：勾上就一直射，不勾要按住鼠标")]
    public bool autoFire = true;

    [Header("表现")]
    [Tooltip("开火回弹（挂在玩家可见模型上）")]
    public Recoil recoil;

    private float nextFireTime;

    private void Update()
    {
        // 暂停/选卡/结束时不能开火
        if (GameManager.Instance != null && GameManager.Instance.State != GameState.Playing) return;

        // 自动开火：不用按住鼠标，按射速节奏持续射击
        if (!autoFire)
        {
            bool pressed = Mouse.current != null && Mouse.current.leftButton.isPressed;
            if (!pressed) return;
        }

        float interval = PlayerStats.Instance != null ? PlayerStats.Instance.FireInterval : fireRate;
        if (Time.time < nextFireTime) return;

        nextFireTime = Time.time + interval;
        Fire();
    }

    private void Fire()
    {
        if (firePoint == null)
        {
            Debug.LogError("[Shoot] firePoint 未赋值", this);
            return;
        }
        if (BulletPool.Instance == null)
        {
            Debug.LogError("[Shoot] 场景里没有 BulletPool", this);
            return;
        }

        PlayerStats stats = PlayerStats.Instance;
        int count = stats != null ? stats.BulletCount : 1;
        float damage = stats != null ? stats.Damage : 10f;
        int pierce = stats != null ? stats.PierceCount : 0;

        for (int i = 0; i < count; i++)
        {
            // 多发时呈扇形对称散开：3 发 = -4°, 0°, +4°
            float offset = (i - (count - 1) * 0.5f) * spreadDegrees;
            Quaternion rotation = firePoint.rotation * Quaternion.Euler(0f, offset, 0f);

            GameObject go = BulletPool.Instance.GetBullet(firePoint.position, rotation);
            if (go == null) continue;

            Bullet bullet = go.GetComponent<Bullet>();
            if (bullet != null) bullet.Init(damage, pierce);

            // 强化爆发期间子弹放大（乘在预制体原始尺寸上，不能直接覆盖）
            bool buffed = PlayerStats.Instance != null && PlayerStats.Instance.BuffActive;
            bullet.ApplyScaleMultiplier(buffed ? 1.6f : 1f);
        }

        // 枪口火花（已经很暗，2 颗，不会再炸成白闪）+ 回弹 + 音效
        CombatFx.Spark(firePoint.position, new Color(0.5f, 1f, 1f), 3);
        if (recoil != null) recoil.Kick();
        SoundFx.Shoot();
    }
}
