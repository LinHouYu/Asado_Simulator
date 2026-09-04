Shader "Custom/ComicOutlineOnly"
{
    Properties
    {
        _OutlineColor("Ink Outline Color", Color) = (0.02, 0.02, 0.02, 1)
        _OutlineWidth("Ink Outline Thickness", Range(0.002, 0.06)) = 0.018
    }

    SubShader
    {
        Tags 
        { 
            "RenderType" = "Opaque" 
            "RenderPipeline" = "UniversalPipeline" 
            "Queue" = "Geometry+10"
        }
        LOD 200

        Pass
        {
            Name "OutlineOnly"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On
            ZTest LEqual
            Offset 1, 1

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _OutlineColor;
                float _OutlineWidth;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;

                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);

                float distToCam = length(_WorldSpaceCameraPos - positionWS);
                float thickness = _OutlineWidth * (1.0 + distToCam * 0.06);
                positionWS += normalize(normalWS) * thickness;

                output.positionCS = TransformWorldToHClip(positionWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
