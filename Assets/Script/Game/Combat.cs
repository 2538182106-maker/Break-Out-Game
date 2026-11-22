using System.Collections.Generic;
using UnityEngine;

/// <summary>范围伤害工具：爆炸砖、爆裂弹、EMP 都用它。</summary>
public static class Combat
{
    private static readonly Collider[] buffer = new Collider[128];
    private static readonly HashSet<Brick> damaged = new HashSet<Brick>();
    private static int numFound;

    /// <summary>对圆形范围内所有砖块造成伤害（每个目标只结算一次）。</summary>
    public static void DamageArea(Vector3 center, float radius, float damage,
                                bool spawnSparks = true, bool spawnNumbers = true)
    {
        if (radius <= 0f || damage <= 0f) return;

        // 复用缓冲区：原来的 Physics.OverlapSphere 每次都会分配数组，
        // 后期电磁圈一秒要调用上千次 → GC 抖动 → 卡顿
        numFound = Physics.OverlapSphereNonAlloc(center, radius, buffer);

        damaged.Clear();

        for (int i = 0; i < numFound; i++)
        {
            Collider col = buffer[i];
            if (col == null) continue;

            Brick brick = col.GetComponentInParent<Brick>();
            if (brick == null || brick.IsDead) continue;
            if (!damaged.Add(brick)) continue;

            Vector3 point = brick.transform.position;
            brick.TakeDamage(damage, point, Color.white, false, false, spawnNumbers);
            if (spawnSparks) CombatFx.Spark(point, Color.white, 3);
        }
    }
}
