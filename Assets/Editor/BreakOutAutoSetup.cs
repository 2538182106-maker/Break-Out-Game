using UnityEditor;              // InitializeOnLoad / EditorApplication / SessionState
using UnityEngine;              // Object / Application
using UnityEngine.SceneManagement;

/// <summary>
/// 【自动配置】让「一键配置」在编辑器重新编译后自动跑一次，不需要手动点菜单。
///
/// 为什么需要它：Unity 只有在窗口获得焦点时才会导入外部改动。
/// 脚本改完、你切回 Unity 的那一刻会触发重新编译 → 这里注册的 delayCall 被调用 →
/// 发现配置缺失就自动执行 BreakOutVisualSetup.Setup()，顺手把场景保存好。
///
/// 安全措施：
///   - 每个编辑器会话只跑一次（SessionState 记录）
///   - Play 模式不跑（退出 Play 时会再试一次）
///   - 只在「当前场景里有玩家对象」时才跑，避免误改别的场景
///   - 已经配好（bulletPrefab 有值）就直接跳过
/// </summary>
[InitializeOnLoad]
public static class BreakOutAutoSetup
{
    private const string SessionKey = "BreakOut.AutoSetup.AlreadyTried";

    // 静态构造函数：编辑器编译完成、域重载后自动执行
    static BreakOutAutoSetup()
    {
        EditorApplication.delayCall += TryRun;                 // 等编辑器空闲一帧再跑，避免在导入过程中改资源
        EditorApplication.delayCall += TryApplyOutline;        // 霓虹 + 描边：每个编辑器会话自动开一次
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    /// <summary>
    /// 自动开启「霓虹 + 描边」：每个编辑器会话只做一次。
    /// 如果你手动在菜单里点了「描边：关闭」，重启编辑器后还会再开一次——
    /// 想要长期关闭就改这里，或在菜单里关掉后不重启。
    /// </summary>
    private static void TryApplyOutline()
    {
        if (Application.isBatchMode) return;
        if (SessionState.GetBool("BreakOut.OutlineAutoApplied", false)) return;

        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += TryApplyOutline;
            return;
        }
        if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (UnityEngine.Object.FindObjectOfType<PlayerControl>() == null) return;

        SessionState.SetBool("BreakOut.OutlineAutoApplied", true);
        ComicModeSetup.EnableNeonOutline();
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
            EditorApplication.delayCall += TryRun;             // 退出 Play 后再补一次
    }

    private static void TryRun()
    {
        // 批处理模式（命令行构建）里不碰场景
        if (Application.isBatchMode) return;

        // 当前会话已经试过就不再打扰
        if (SessionState.GetBool(SessionKey, false)) return;

        // 正在编译 / 正在导入资源 → 等下一次
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += TryRun;
            return;
        }

        // Play 中不能改场景和预制体
        if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) return;

        // 只在游戏场景里跑：必须有玩家，否则可能是别的场景
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.isLoaded) return;
        if (UnityEngine.Object.FindObjectOfType<PlayerControl>() == null) return;

        // 已经配好了？直接标记完成
        BulletPool pool = UnityEngine.Object.FindObjectOfType<BulletPool>();
        if (pool != null && pool.bulletPrefab != null)
        {
            SessionState.SetBool(SessionKey, true);
            return;
        }

        SessionState.SetBool(SessionKey, true);   // 先标记，避免异常时反复重试刷屏
        Debug.Log("[BreakOut] 检测到子弹链路未配置，开始自动配置……");
        BreakOutVisualSetup.Setup();
    }
}
