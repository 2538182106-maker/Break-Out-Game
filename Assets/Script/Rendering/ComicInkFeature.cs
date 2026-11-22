// =====================================================================================
// ComicInkFeature.cs —— 把 ComicInk.shader 接进 URP 渲染管线的「插槽」
//
// URP 里想在「渲染完所有物体之后、后处理之前」塞一个全屏 Pass，只有两条路：
//   A. 用渲染器资产上的 Renderer Feature（本文件就是这条路）
//   B. 改管线源码（不推荐）
//
// 工作流程：
//   ① AddRenderPasses 里告诉 URP「我要跑一个 Pass」
//   ② Execute 里拿到相机的颜色图 → 用 ComicInk 材质画到临时 RT → 再画回相机颜色图
//
// 注意：这个类必须放在 Assets 下任意位置，Unity 会自动把它识别成一个 Renderer Feature。
// =====================================================================================
#pragma warning disable 0618   // cameraColorTarget 在新版本标了 Obsolete，但为了兼容 URP 12~14 继续用它

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class ComicInkFeature : ScriptableRendererFeature
{
    /// <summary>
    /// 序列化设置：会显示在 Renderer Data 的 Inspector 上，可以直接改。
    /// </summary>
    [System.Serializable]
    public class Settings
    {
        [Tooltip("用哪个材质来画（ComicInk.mat，Shader = BreakOut/ComicInk）")]
        public Material material;

        [Tooltip("在哪个阶段插入。后处理之前最稳")]
        public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;

        [Tooltip("是否在 Scene 视图也生效（调试时很有用，但 Scene 视图没有深度图会出问题）")]
        public bool applyInSceneView = false;
    }

    public Settings settings = new Settings();

    private ComicInkPass pass;   // 真正的渲染 Pass 实例

    /// <summary>
    /// Create() 在 Feature 被加载/参数变化时调用，用来创建 Pass 对象。
    /// 这里只 new，不做任何 GPU 操作。
    /// </summary>
    public override void Create()
    {
        pass = new ComicInkPass(settings);
    }

    /// <summary>
    /// AddRenderPasses 每帧调用一次：决定这一帧要不要把 Pass 排进队列。
    /// </summary>
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (settings.material == null) return;                                  // 没配材质就不跑

        // 默认只在 Game 视图跑：Scene 视图可能没有深度图，跑出来会一片黑
        if (!settings.applyInSceneView && renderingData.cameraData.cameraType != CameraType.Game) return;

        pass.renderPassEvent = settings.renderPassEvent;   // 允许在 Inspector 里改插入时机
        renderer.EnqueuePass(pass);                        // 排队
    }

    // =================================================================================
    // 真正的 Pass
    // =================================================================================
    private class ComicInkPass : ScriptableRenderPass
    {
        private readonly Settings settings;

        // 临时 RT 的 ID。用 Shader.PropertyToID 缓存成静态整数，比每帧传字符串快
        private static readonly int TempId = Shader.PropertyToID("_ComicInkTemp");

        public ComicInkPass(Settings settings)
        {
            this.settings = settings;

            // ★ 关键：告诉 URP「我这个 Pass 需要深度图 + 颜色图」
            //
            //   Depth：没这一句，_CameraDepthTexture 可能是空的 → 描边完全不出现
            //   Color：没这一句，相机可能直接画到 backbuffer（屏幕后备缓冲），
            //          而 backbuffer 作为渲染目标时【不能被当成纹理采样】，
            //          读出来是一片常数灰 → 全屏变成灰底 + 网点，游戏画面消失。
            //          加上 Color 之后 URP 会分配一张中间颜色纹理，cameraColorTarget 才能被采样。
            ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Color);
        }

        /// <summary>
        /// Execute：真正录制 GPU 命令的地方（注意这里只是「录命令」，不是立刻执行）。
        /// </summary>
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (settings.material == null) return;

            // 从池子里借一个 CommandBuffer，用完必须还
            CommandBuffer cmd = CommandBufferPool.Get("ComicInk");

            // 临时 RT 的描述：跟相机目标同尺寸同格式，但不要深度缓冲（省显存）
            RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            cmd.GetTemporaryRT(TempId, desc);

            // 相机当前的颜色目标
            RenderTargetIdentifier source = renderingData.cameraData.renderer.cameraColorTarget;

            // 第 1 步：源图 → 临时 RT，用 ComicInk 材质。
            // 这里用 Blitter 而不是 cmd.Blit：Blitter 会把源图绑到 shader 约定的 _BlitTexture，
            // 并画一个「全屏三角形」；cmd.Blit 的老写法在 URP 里容易踩到 _MainTex 没绑上的坑。
            Blitter.BlitTexture(cmd, source, TempId, settings.material, 0);

            // 第 2 步：临时 RT → 相机目标，纯拷贝（不需要材质）
            cmd.Blit(TempId, source);

            cmd.ReleaseTemporaryRT(TempId);

            context.ExecuteCommandBuffer(cmd);   // 提交给渲染线程
            CommandBufferPool.Release(cmd);      // 归还 CommandBuffer
        }
    }
}
