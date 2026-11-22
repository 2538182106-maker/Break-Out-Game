using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 【通用对象池】给那些"临时生成一次就销毁"的特效/经验球用。
/// 用字符串 key 区分不同类型；工厂方法只在第一次创建时调用。
/// </summary>
public static class SimplePool
{
    private static readonly Dictionary<string, Stack<GameObject>> Pools =
        new Dictionary<string, Stack<GameObject>>();

    /// <summary>取一个（已激活，位置由调用者设置）。</summary>
    public static GameObject Get(string key, Func<GameObject> factory)
    {
        if (!Pools.TryGetValue(key, out Stack<GameObject> stack))
        {
            stack = new Stack<GameObject>();
            Pools[key] = stack;
        }

        while (stack.Count > 0)
        {
            GameObject cached = stack.Pop();
            if (cached != null)
            {
                cached.SetActive(true);
                return cached;
            }
        }

        return factory();
    }

    /// <summary>归还（失活并放回池子）。</summary>
    public static void Release(string key, GameObject go)
    {
        if (go == null) return;

        go.SetActive(false);

        if (!Pools.TryGetValue(key, out Stack<GameObject> stack))
        {
            stack = new Stack<GameObject>();
            Pools[key] = stack;
        }
        stack.Push(go);
    }
}
