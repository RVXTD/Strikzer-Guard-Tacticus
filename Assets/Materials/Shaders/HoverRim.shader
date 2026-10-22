// Written by Claude (Anthropic), October 2026; to be reviewed by Alexander Stangl per the course syllabus.
//
// The hover highlight: an additive fresnel rim drawn over a unit's own material. UnitView appends one material with
// this shader to the hovered unit's renderers and sets _RimColor through its MaterialPropertyBlock. The pass has no
// LightMode tag, so URP draws it as SRPDefaultUnlit after the unit's normal pass.
Shader "Auto Buster/Hover Rim"
{
    Properties
    {
        _RimColor ("Rim colour", Color) = (1, 1, 1, 1)
        _RimPower ("Rim power", Range(0.5, 8)) = 2.5
        _RimIntensity ("Rim intensity", Range(0, 4)) = 1.5
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Rim"
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _RimColor;
                half _RimPower;
                half _RimIntensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewDirectionWS : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewDirectionWS = GetWorldSpaceViewDir(positionWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 normal = normalize(input.normalWS);
                float3 toCamera = normalize(input.viewDirectionWS);
                half rim = pow(1.0 - saturate(dot(normal, toCamera)), _RimPower) * _RimIntensity;
                return half4(_RimColor.rgb * rim, 1.0);
            }
            ENDHLSL
        }
    }
}
