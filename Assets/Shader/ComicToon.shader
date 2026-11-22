// =====================================================================================
// ComicToon.shader —— 美式漫画「卡通着色 + 反向外壳描边」
//
// 一个材质两个 Pass：
//   Pass 1  ComicToon：正常的物体着色（2 阶硬光影 + 边缘光）
//   Pass 2  Outline  ：把模型沿法线「吹胖」一圈只画背面，形成黑色轮廓线
//
// 用法：把本材质拖到物体的 MeshRenderer 上即可（不需要额外脚本）。
// =====================================================================================
Shader "BreakOut/ComicToon"
{
    // ---------- Properties：暴露给 Inspector 的参数 ----------
    Properties
    {
        [Header(Base)]
        _BaseColor      ("基础色", Color) = (0.85, 0.25, 0.3, 1)     // 受光面的颜色（平涂的主色块）
        _ShadowColor    ("阴影色（乘算）", Color) = (0.42, 0.36, 0.62, 1) // 背光面 = 基础色 × 这个色，做出偏紫的冷阴影

        [Header(Toon)]
        _Step           ("光影分界", Range(0, 1)) = 0.48             // 在哪里切开明暗（0=全暗 1=全亮）
        _StepSoft       ("分界柔和度", Range(0.001, 0.3)) = 0.03     // 边界宽度；越大越软，漫画要小
        _RimColor       ("边缘光颜色", Color) = (1, 0.92, 0.75, 1)   // 轮廓内侧那圈暖光（漫画常用）
        _RimPower       ("边缘光收窄", Range(1, 8)) = 3.5            // 越大边缘越细
        _RimStrength    ("边缘光强度", Range(0, 2)) = 0.3

        [Header(Outline)]
        _OutlineColor   ("描边颜色", Color) = (0.06, 0.05, 0.09, 1)  // 不要用纯黑，偏紫的深色更像油墨
        _OutlineWidth   ("描边宽度", Range(0, 0.2)) = 0.05           // 世界单位；会再乘以到相机的距离系数
    }

    SubShader
    {
        // URP 识别的标签：告诉管线这是不透明物体、走 UniversalPipeline
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        // =========================================================================
        // Pass 1：卡通着色
        // =========================================================================
        Pass
        {
            Name "ComicToon"
            Tags { "LightMode" = "UniversalForward" }   // URP 靠这个标签决定「这个 Pass 在哪个阶段画」

            HLSLPROGRAM
            #pragma vertex vert          // 顶点着色器入口函数名
            #pragma fragment frag        // 片元着色器入口函数名
            #pragma multi_compile_instancing

            // URP 的基础库：TransformObjectToWorldNormal / GetVertexPositionInputs 等都在里面
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // 光照库：GetMainLight() / SampleSH() 在里面
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // 所有材质参数必须放在这个 CBUFFER 里，SRP Batcher 才能合批
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _ShadowColor;
                float4 _RimColor;
                float  _Step;
                float  _StepSoft;
                float  _RimPower;
                float  _RimStrength;
                float4 _OutlineColor;
                float  _OutlineWidth;
            CBUFFER_END

            // 顶点着色器的输入：来自网格数据
            struct Attributes
            {
                float4 positionOS : POSITION;   // 物体空间坐标
                float3 normalOS   : NORMAL;     // 物体空间法线
                UNITY_VERTEX_INPUT_INSTANCE_ID  // GPU Instancing 用
            };

            // 顶点着色器的输出 → 片元着色器的输入
            struct Varyings
            {
                float4 positionCS : SV_POSITION;  // 裁剪空间坐标（必须叫这个语义，GPU 才知道是屏幕位置）
                float3 normalWS   : TEXCOORD0;    // 世界空间法线（插值后使用）
                float3 positionWS : TEXCOORD1;    // 世界空间坐标（算视线方向用）
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;              // 清零，避免某些平台出现未初始化垃圾值
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);

                VertexPositionInputs pos = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = pos.positionCS;         // 物体 → 世界 → 观察 → 裁剪
                o.positionWS = pos.positionWS;         // 世界坐标（已经是 float3）
                o.normalWS   = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 N = normalize(i.normalWS);                       // 插值会破坏长度，必须重新归一化
                float3 V = normalize(_WorldSpaceCameraPos - i.positionWS); // 指向相机的单位向量

                Light mainLight = GetMainLight();                       // 场景的主平行光（Directional Light）
                float NdotL = dot(N, mainLight.direction);              // 法线与光线的点积：>0 受光，<0 背光

                // ---- 核心：半兰伯特 + 硬阶 ----
                // 半兰伯特把 [-1,1] 映射到 [0,1]，避免背光面死黑（漫画也想要一点暗部信息）
                float halfLambert = NdotL * 0.5 + 0.5;
                // smoothstep(a, b, x) 在 a~b 之间做平滑过渡，超过范围就钳到 0/1 —— 这就是「硬边」的来源
                float ramp = smoothstep(_Step - _StepSoft, _Step + _StepSoft, halfLambert);

                // 阴影色是「乘算」的：基础色 × 阴影色 = 冷调暗部
                float3 albedo = _BaseColor.rgb;
                float3 lit    = albedo;
                float3 shadow = albedo * _ShadowColor.rgb;
                float3 color  = lerp(shadow, lit, ramp);                // ramp=0 全用阴影色，ramp=1 全用受光色

                // ---- 环境光：让背光面不至于纯色一片 ----
                color += SampleSH(N) * albedo * 0.25;

                // ---- 边缘光：轮廓内侧的一圈暖光，漫画/动画常用 ----
                // 注意：俯视相机下所有竖直面都是「掠射角」，dot(N,V) 接近 0，
                //       rim 会算成满值 → 所有侧面都发亮，看着像平涂色块。
                //       所以再乘一个 saturate(NdotL)，只让受到光照的边缘发亮。
                float rim = 1.0 - saturate(dot(N, V));                  // 正对相机=0，边缘=1
                rim = pow(rim, _RimPower);
                rim = smoothstep(0.55, 0.65, rim) * _RimStrength * saturate(NdotL);
                color += _RimColor.rgb * rim;

                return half4(color, 1.0);
            }
            ENDHLSL
        }

        // =========================================================================
        // Pass 2：反向外壳描边（Inverted Hull）
        //
        // 原理：正常渲染只画正面（Cull Back），这里反过来「只画背面」（Cull Front），
        //       并把顶点沿法线往外推一点。于是背面被撑大、露在正面外面，形成一圈轮廓。
        // 注意：这个 Pass 不写 LightMode 标签 → URP 会把它当成 SRPDefaultUnlit 正常绘制。
        // =========================================================================
        Pass
        {
            Name "Outline"
            Cull Front          // 关键：只渲染背面（默认是 Cull Back 渲染正面）
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vertOutline
            #pragma fragment fragOutline

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _ShadowColor;
                float4 _RimColor;
                float  _Step;
                float  _StepSoft;
                float  _RimPower;
                float  _RimStrength;
                float4 _OutlineColor;
                float  _OutlineWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vertOutline(Attributes v)
            {
                Varyings o;

                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 nWS   = TransformObjectToWorldNormal(v.normalOS);   // 已归一化

                // 距离补偿：同样的世界宽度，远景会显得很细、近景会很粗。
                // 乘上「到相机的距离」后，屏幕上的线宽就基本一致了。
                // 系数 0.15：本场景相机在 9 米高空、物体约 1 米大，
                //             0.05 * 9 * 0.15 ≈ 0.07 米，约为物体尺寸的 7%，是比较好看的漫画线。
                float dist = distance(posWS, _WorldSpaceCameraPos);

                posWS += nWS * _OutlineWidth * dist * 0.15;   // 沿法线外扩

                o.positionCS = TransformWorldToHClip(posWS);
                return o;
            }

            half4 fragOutline(Varyings i) : SV_Target
            {
                return half4(_OutlineColor.rgb, 1.0);   // 纯色墨线
            }
            ENDHLSL
        }
    }

    // 如果没有 URP（比如切到 Built-in），至少别显示成粉红错误材质
    FallBack "Universal Render Pipeline/Unlit"
}
