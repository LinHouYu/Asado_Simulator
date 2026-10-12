Shader "Custom/SelectionOutline"
{
    Properties
    {
        _MaskTex ("Mask Texture", 2D) = "black" {}
        [HDR] _OutlineColor ("Outline Color", Color) = (0, 0, 0, 1)
        _OutlineWidth ("Outline Width", Range(1, 10)) = 2.0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        LOD 100
        ZTest Always
        ZWrite Off
        Cull Off

        Pass
        {
            Name "OutlinePass"
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D(_MaskTex);
            SAMPLER(sampler_MaskTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MaskTex_TexelSize;
                float4 _OutlineColor;
                float _OutlineWidth;
            CBUFFER_END

            // 二值化 Mask 采样：只要 R 或 A 通道 > 0.01，就强制按 1.0 处理
            float SampleMaskBinarized(float2 uv)
            {
                float4 col = SAMPLE_TEXTURE2D(_MaskTex, sampler_LinearClamp, uv);
                return (col.r > 0.01 || col.a > 0.01) ? 1.0 : 0.0;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // 中心点原始遮罩 (二值化)
                float maskCenter = SampleMaskBinarized(input.texcoord);

                // 上下左右四周偏移采样，获取边缘 (二值化)
                float2 offset = _MaskTex_TexelSize.xy * _OutlineWidth;
                float maskUp    = SampleMaskBinarized(input.texcoord + float2(0, offset.y));
                float maskDown  = SampleMaskBinarized(input.texcoord - float2(0, offset.y));
                float maskLeft  = SampleMaskBinarized(input.texcoord - float2(offset.x, 0));
                float maskRight = SampleMaskBinarized(input.texcoord + float2(offset.x, 0));

                // 叠加对角线采样提升平滑连续性 (二值化)
                float2 diag = offset * 0.7071;
                float maskUL = SampleMaskBinarized(input.texcoord + float2(-diag.x, diag.y));
                float maskUR = SampleMaskBinarized(input.texcoord + float2(diag.x, diag.y));
                float maskDL = SampleMaskBinarized(input.texcoord + float2(-diag.x, -diag.y));
                float maskDR = SampleMaskBinarized(input.texcoord + float2(diag.x, -diag.y));

                float maxMask = max(maskCenter, max(max(maskUp, maskDown), max(maskLeft, maskRight)));
                maxMask = max(maxMask, max(max(maskUL, maskUR), max(maskDL, maskDR)));

                // 减去中心点的原 Mask，得到纯外边缘 (掏空内部，二值化)
                float edge = saturate(maxMask - maskCenter);

                // 边缘最终输出颜色 100% 等于材质球暴露的 _OutlineColor.rgb，Alpha 为 _OutlineColor.a * 轮廓强度
                float alpha = _OutlineColor.a * edge;
                return float4(_OutlineColor.rgb, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
