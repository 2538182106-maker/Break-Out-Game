// =====================================================================================
// ComicInk.shader —— 屏幕空间描边 / 漫画化后处理（URP 14）
//
// 两种用法：
//   ① 霓虹 + 描边：只画墨线（色阶/网点/纸纹 全为 0）
//   ② 美漫模式：墨线 + 色阶量化 + 半调网点 + 纸纹
//
// 描边算法：3×3 邻域 **Sobel 算子**
//   - 对角 4 采样（Roberts Cross）在 90° 拐角会漏检（线断开），且横竖/斜向响应不一致（粗细不一）
//   - Sobel 用 8 邻域梯度，方向无关 → 拐角能连上、线宽均匀
//
// 由 ComicInkFeature.cs 用 Blitter.BlitTexture 调用，源图是 _BlitTexture。
// =====================================================================================
Shader "BreakOut/ComicInk"
{
    Properties
    {
        [Header(Ink)]
        _InkColor        ("墨线颜色", Color) = (0.06, 0.05, 0.09, 1)
        _EdgeStrength    ("线条强度", Range(0, 1)) = 1
        _Thickness       ("采样宽度（像素）", Range(0.5, 4)) = 2.4
        _DepthThreshold  ("深度阈值", Range(0.01, 5)) = 0.25
        _LumaThreshold   ("亮度阈值", Range(0.01, 1)) = 0.08
        _CornerBoost     ("拐角补强", Range(1, 2)) = 1.25

        [Header(Print)]
        _PosterizeSteps   ("色阶数", Range(2, 16)) = 6
        _PosterizeStrength("色阶强度", Range(0, 1)) = 0
        _BlackLift        ("最暗档提亮", Range(0, 1)) = 0
        _HalftoneScale    ("网点密度", Range(20, 400)) = 150
        _HalftoneStrength ("网点强度", Range(0, 1)) = 0
        _PaperStrength    ("纸纹强度", Range(0, 0.5)) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ComicInk"

            // 全屏 Pass 的标准设置：不做深度测试、不写深度、不剔除
            // 注意：ZTest / ZWrite / Cull 这些渲染状态必须写在 Pass 里面
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // 提供 SampleSceneDepth()，用来读相机的深度图 _CameraDepthTexture
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D_X(_BlitTexture);
            SAMPLER(sampler_LinearClamp);
            float4 _BlitScaleBias;

            CBUFFER_START(UnityPerMaterial)
                float4 _InkColor;
                float  _EdgeStrength;
                float  _Thickness;
                float  _DepthThreshold;
                float  _LumaThreshold;
                float  _CornerBoost;
                float  _PosterizeSteps;
                float  _PosterizeStrength;
                float  _BlackLift;
                float  _HalftoneScale;
                float  _HalftoneStrength;
                float  _PaperStrength;
            CBUFFER_END

            // 全屏三角形：Blitter 用 DrawProcedural 画 3 个顶点，没有网格数据
            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = GetFullScreenTriangleVertexPosition(v.vertexID);
                o.uv = GetFullScreenTriangleTexCoord(v.vertexID) * _BlitScaleBias.xy + _BlitScaleBias.zw;
                return o;
            }

            // 亮度：人眼对绿色最敏感（Rec.709 系数）
            float Luma(float3 c)
            {
                return dot(c, float3(0.2126, 0.7152, 0.0722));
            }

            // 采样一张「线性深度」（单位：米）。原始深度是非线性的，必须转一下
            float SampleDepth(float2 uv)
            {
                return LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
            }

            // 采样一张「亮度」
            float SampleLuma(float2 uv)
            {
                return Luma(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = i.uv;
                float2 o  = _Thickness / _ScreenParams.xy;   // 一个「像素步长」对应的 UV 偏移

                // ------------------------------------------------------------------
                // ① 取 3×3 的 8 个邻居
                //    用 (dx, dy) 表示方向：x=左右, y=上下
                // ------------------------------------------------------------------
                float dNW = SampleDepth(uv + float2(-o.x,  o.y));
                float dN  = SampleDepth(uv + float2( 0  ,  o.y));
                float dNE = SampleDepth(uv + float2( o.x,  o.y));
                float dW  = SampleDepth(uv + float2(-o.x,  0  ));
                float dE  = SampleDepth(uv + float2( o.x,  0  ));
                float dSW = SampleDepth(uv + float2(-o.x, -o.y));
                float dS  = SampleDepth(uv + float2( 0  , -o.y));
                float dSE = SampleDepth(uv + float2( o.x, -o.y));

                // ------------------------------------------------------------------
                // ② Sobel：分别算 x 方向和 y 方向的梯度，再取模长
                //    权重要用 1-2-1（中间那排 ×2），这样对噪声更稳、方向更均匀
                // ------------------------------------------------------------------
                float gxDepth = (dNE + 2.0 * dE + dSE) - (dNW + 2.0 * dW + dSW);
                float gyDepth = (dNW + 2.0 * dN + dNE) - (dSW + 2.0 * dS + dSE);
                float edgeDepth = sqrt(gxDepth * gxDepth + gyDepth * gyDepth) / 8.0;   // /8 归一化
                edgeDepth = smoothstep(_DepthThreshold, _DepthThreshold * 2.0, edgeDepth);

                // ------------------------------------------------------------------
                // ③ 亮度也做一遍 Sobel（深度抓不到同一平面上的颜色边界）
                // ------------------------------------------------------------------
                float lNW = SampleLuma(uv + float2(-o.x,  o.y));
                float lN  = SampleLuma(uv + float2( 0  ,  o.y));
                float lNE = SampleLuma(uv + float2( o.x,  o.y));
                float lW  = SampleLuma(uv + float2(-o.x,  0  ));
                float lE  = SampleLuma(uv + float2( o.x,  0  ));
                float lSW = SampleLuma(uv + float2(-o.x, -o.y));
                float lS  = SampleLuma(uv + float2( 0  , -o.y));
                float lSE = SampleLuma(uv + float2( o.x, -o.y));

                float gxLuma = (lNE + 2.0 * lE + lSE) - (lNW + 2.0 * lW + lSW);
                float gyLuma = (lNW + 2.0 * lN + lNE) - (lSW + 2.0 * lS + lSE);
                float edgeLuma = sqrt(gxLuma * gxLuma + gyLuma * gyLuma) / 8.0;
                edgeLuma = smoothstep(_LumaThreshold, _LumaThreshold * 2.0, edgeLuma);

                // ------------------------------------------------------------------
                // ④ 合并 + 拐角补强
                //    在拐角处 gx、gy 同时有值，模长会偏大；但二值化后可能只剩一个像素。
                //    _CornerBoost 让「两个方向同时有梯度」的像素更容易被判成线，
                //    把 L 型缺口补上。
                // ------------------------------------------------------------------
                float edge = max(edgeDepth, edgeLuma);

                float bothAxes = min(abs(gxDepth) + abs(gxLuma), abs(gyDepth) + abs(gyLuma));
                float cornerMask = smoothstep(_DepthThreshold * 0.5, _DepthThreshold, bothAxes);
                edge = saturate(edge * lerp(1.0, _CornerBoost, cornerMask));
                edge = saturate(edge * _EdgeStrength);

                // ------------------------------------------------------------------
                // 取原色 → 可选的漫画印刷效果（霓虹模式下这些参数都是 0）
                // ------------------------------------------------------------------
                float3 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;

                float3 quantized = floor(col * _PosterizeSteps + 0.5) / _PosterizeSteps;
                quantized = max(quantized, _BlackLift / _PosterizeSteps);
                col = lerp(col, quantized, _PosterizeStrength);

                float2 px  = uv * _ScreenParams.xy;
                float2 rot = float2(px.x * 0.7071 - px.y * 0.7071,
                                    px.x * 0.7071 + px.y * 0.7071);
                float2 cell = frac(rot / max(_HalftoneScale, 1.0)) - 0.5;
                float  dist2Center = length(cell);

                float luma2  = Luma(col);
                float radius = saturate(1.0 - luma2) * 0.62;
                float dots   = smoothstep(radius, radius - 0.08, dist2Center);
                dots        *= 1.0 - smoothstep(0.7, 1.0, luma2);
                col *= 1.0 - dots * _HalftoneStrength;

                float grain = frac(sin(dot(px, float2(12.9898, 78.233))) * 43758.5453);
                col *= 1.0 - grain * _PaperStrength;

                // 最后叠墨线（线是干净的纯色，不被网点影响）
                col = lerp(col, _InkColor.rgb, edge);

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
