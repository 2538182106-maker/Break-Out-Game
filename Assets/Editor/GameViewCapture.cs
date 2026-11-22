// =====================================================================================
// GameViewCapture.cs —— 小工具：最大化 / 还原 Game 视图
//
// 用途：录 GIF、截预览图时把 Game 视图铺满屏幕（作品集里的效果 GIF 需要这种图）。
// 只影响编辑器布局，不改任何工程资源。
//
// 菜单：
//   Tools → Break Out → 最大化 Game 视图（录 GIF 用）
//   Tools → Break Out → 还原 Game 视图布局
// =====================================================================================
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class GameViewCapture
{
    private const string RestoreKey = "GameViewCapture.RestoredOnce_v2";

    // 编辑器重编译后自动还原一次布局
    static GameViewCapture()
    {
        EditorApplication.delayCall += RestoreOnce;
    }

    private static void RestoreOnce()
    {
        if (SessionState.GetBool(RestoreKey, false)) return;
        SessionState.SetBool(RestoreKey, true);
        SetMaximized(false);
    }

    [MenuItem("Tools/Break Out/最大化 Game 视图（录 GIF 用）", false, 40)]
    private static void MaximizeGameView()
    {
        SetMaximized(true);
    }

    [MenuItem("Tools/Break Out/还原 Game 视图布局", false, 41)]
    private static void RestoreGameView()
    {
        SetMaximized(false);
    }

    private static void SetMaximized(bool maximized)
    {
        System.Type gameViewType = System.Type.GetType("UnityEditor.GameView, UnityEditor");
        if (gameViewType == null) return;

        EditorWindow gameView = EditorWindow.GetWindow(gameViewType);
        if (gameView == null) return;

        gameView.maximized = maximized;
        if (maximized) gameView.Focus();
        gameView.Repaint();
    }
}
