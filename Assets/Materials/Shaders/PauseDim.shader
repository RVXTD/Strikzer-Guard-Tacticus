// Written by Claude (Anthropic), October 2026; to be reviewed by Alexander Stangl per the course syllabus.
//
// The pause dimming: a full-screen pass, run by URP's Full Screen Pass renderer feature (Fetch Color Buffer on, which
// fills _BlitTexture; Requirements: None), that desaturates the frame towards its linear Rec. 709 luminance,
// Y = 0.2126 R + 0.7152 G + 0.0722 B, and darkens it.
// The global _PauseDim, eased by PauseMenu, runs from 0 (the frame passes through unchanged) to 1 (fully dimmed).
Shader "Auto Buster/Pause Dim"
{
    Properties
    {
        _Desaturate ("Desaturation when fully dimmed", Range(0, 1)) = 1
        _Darken ("Brightness when fully dimmed", Range(0, 1)) = 0.45
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off
        Blend Off

        Pass
        {
            Name "PauseDim"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _PauseDim; // global, set by PauseMenu

            CBUFFER_START(UnityPerMaterial)
                half _Desaturate;
                half _Darken;
            CBUFFER_END

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 colour = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                half luminance = dot(colour.rgb, half3(0.2126, 0.7152, 0.0722));
                half3 dimmed = lerp(colour.rgb, luminance.xxx, _Desaturate * _PauseDim) * lerp(1.0, _Darken, _PauseDim);
                return half4(dimmed, colour.a);
            }
            ENDHLSL
        }
    }
}
