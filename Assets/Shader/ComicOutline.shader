Shader "Custom/ComicOutline"
{
    Properties
    {
        [Header(Original Material Appearance)]
        [MainTexture] _BaseMap("Base Texture", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _MainTex("Base Texture (Alias)", 2D) = "white" {}
        [HideInInspector] _Color("Base Color (Alias)", Color) = (1, 1, 1, 1)

        [Header(Comic Cartoon Black Outline)]
        _OutlineColor("Ink Outline Color", Color) = (0.02, 0.02, 0.02, 1)
        _OutlineWidth("Ink Outline Thickness", Range(0.002, 0.06)) = 0.018
    }

    SubShader
    {
        Tags 
        { 
            "RenderType" = "Opaque" 
            "RenderPipeline" = "UniversalPipeline" 
            "Queue" = "Geometry"
        }
        LOD 200

        // ------------------------------------------------------------------
        // Pass 1: Bold Comic Cartoon Outline (清晰醒目的背向法线外挤黑边)
        // ------------------------------------------------------------------
        Pass
        {
            Name "ComicOutline"
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
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float4 _Color;
                float4 _OutlineColor;
                float _OutlineWidth;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;

                // Transform vertex position and normal to World Space
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);

                // Smooth distance compensation: outline stays clear and bold at all ranges
                float distToCam = length(_WorldSpaceCameraPos - positionWS);
                float thickness = _OutlineWidth * (1.0 + distToCam * 0.06);

                // Extrude along world normal
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

        // ------------------------------------------------------------------
        // Pass 2: Natural Forward Pass (完整保留物体原本贴图与色彩)
        // ------------------------------------------------------------------
        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float4 _Color;
                float4 _OutlineColor;
                float _OutlineWidth;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.normalWS = normInputs.normalWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Sample original texture and multiply by base color tint
                half4 originalTexture = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half4 materialColor = originalTexture * _BaseColor * _Color;
                float3 normalWS = normalize(input.normalWS);

                // Main light
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                float nDotL = saturate(dot(normalWS, mainLight.direction));
                float shadowAtten = mainLight.shadowAttenuation * mainLight.distanceAttenuation;

                // Natural diffuse light
                half3 directLight = mainLight.color * (nDotL * shadowAtten);

                // Ambient light
                half3 ambientLight = SampleSH(normalWS);

                // Combine to form clean, natural scene lighting preserving 100% original texture colors
                half3 finalLight = directLight + ambientLight;
                half3 finalColor = materialColor.rgb * finalLight;

                return half4(finalColor, materialColor.a);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
