// Written by Claude (Anthropic), October 2026; widths from world-space derivatives added after the first playtest,
// and the separate zone texture and blocked discs after the second; to be reviewed by Alexander Stangl per the course
// syllabus.
//
// The movement locus and zones of control, drawn on a quad over the map by EncounterOverlay. Both textures hold one
// texel per map cell. _FieldTex: R the active unit's distance in metres (carried on into cells it cannot stand in),
// G clearance from walls for its body, B inside another unit's body; soft values cross 0.5 at the rules' edge.
// _ZoneTex: R inside a hostile zone that can still react, G inside one that already has. Layers, bottom to top: zone
// tint and edge, locus fill (one point, then two), the outward pulse, the rings at both thresholds, and other units'
// bodies within reach as blocked discs.
Shader "Auto Buster/Encounter Overlay"
{
    Properties
    {
        _FieldTex ("Field data", 2D) = "black" {}
        _ZoneTex ("Zone data", 2D) = "black" {}
        _MapOrigin ("Map origin (x, z)", Vector) = (0, 0, 0, 0)
        _MapSize ("Map size (x, z)", Vector) = (1, 1, 0, 0)
        _Budget ("Budget (m)", Float) = 0
        _Reach ("Reach (m)", Float) = 0
        _OneColour ("One point", Color) = (0.3, 0.8, 1, 1)
        _TwoColour ("Two points", Color) = (1, 0.8, 0.2, 1)
        _ZoneColour ("Zone", Color) = (1, 0.1, 0.6, 1)
        _OccupiedColour ("Blocked by a body", Color) = (1, 0.25, 0.2, 0.35)
        _SpentStrength ("Spent zone strength", Range(0, 1)) = 0.4
        _FillOpacity ("Fill opacity", Range(0, 1)) = 0.12
        _LineWidth ("Line width (pixels)", Float) = 1.5
        _PulseSpeed ("Pulse speed (m/s)", Float) = 3
        _PulseSpacing ("Pulse spacing (m)", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Overlay"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_FieldTex);
            SAMPLER(sampler_FieldTex);
            TEXTURE2D(_ZoneTex);
            SAMPLER(sampler_ZoneTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _FieldTex_ST;
                float4 _ZoneTex_ST;
                float4 _MapOrigin;
                float4 _MapSize;
                float _Budget;
                float _Reach;
                half4 _OneColour;
                half4 _TwoColour;
                half4 _ZoneColour;
                half4 _OccupiedColour;
                half _SpentStrength;
                half _FillOpacity;
                float _LineWidth;
                float _PulseSpeed;
                float _PulseSpacing;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 mapUV : TEXCOORD0;
                float2 worldXZ : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.mapUV = (positionWS.xz - _MapOrigin.xy) / _MapSize.xy;
                output.worldXZ = positionWS.xz;
                return output;
            }

            // 1 on the level set value == level, fading to 0 about _LineWidth pixels away; width is fwidth(value).
            half lineAt(float value, float level, float width)
            {
                return saturate(1.0 - abs(value - level) / (width * _LineWidth));
            }

            // Straight-alpha "over": draws src on top of dst.
            half4 over(half4 dst, half4 src)
            {
                half alpha = src.a + dst.a * (1.0 - src.a);
                half3 rgb = (src.rgb * src.a + dst.rgb * dst.a * (1.0 - src.a)) / max(alpha, 1e-4);
                return half4(rgb, alpha);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float4 data = SAMPLE_TEXTURE2D(_FieldTex, sampler_FieldTex, input.mapUV);
                float2 zones = SAMPLE_TEXTURE2D(_ZoneTex, sampler_ZoneTex, input.mapUV).rg;
                float arrival = data.r;
                // The locus is masked by walls only: other units' bodies are drawn over it instead.
                half passable = smoothstep(0.35, 0.65, data.g);
                half occupied = smoothstep(0.35, 0.65, data.b);

                // Screen-space derivatives first, outside any branch. The field rises one metre per metre walked, so its
                // anti-aliasing width is the metres covered by a pixel; fwidth of the data itself would blow up where
                // the data jumps (the field window's edge, cells beside walls) and draw lines there.
                float arrivalWidth = max(length(fwidth(input.worldXZ)), 1e-4);
                float liveWidth = max(fwidth(zones.r), 1e-4);
                float spentWidth = max(fwidth(zones.g), 1e-4);
                float bodyWidth = max(fwidth(data.b), 1e-4);

                half4 colour = half4(0, 0, 0, 0);

                // Zones: a tint inside, and an edge where coverage crosses one half.
                half zoneFill = saturate(zones.r + zones.g * _SpentStrength) * 0.18;
                half zoneEdge = max(lineAt(zones.r, 0.5, liveWidth), lineAt(zones.g, 0.5, spentWidth) * _SpentStrength);
                colour = over(colour, half4(_ZoneColour.rgb, saturate(zoneFill + zoneEdge * 0.85)));

                // Locus: nothing when the unit cannot move.
                half moves = _Reach > 0.0 ? 1.0 : 0.0;
                half withinBudget = saturate((_Budget - arrival) / arrivalWidth + 0.5) * passable * moves;
                half withinReach = saturate((_Reach - arrival) / arrivalWidth + 0.5) * passable * moves;
                half twoPoints = saturate(withinReach - withinBudget);
                half fillAlpha = (withinBudget + twoPoints) * _FillOpacity;
                half3 fillRgb = (_OneColour.rgb * withinBudget + _TwoColour.rgb * twoPoints) / max(withinBudget + twoPoints, 1e-4);
                colour = over(colour, half4(fillRgb, fillAlpha));

                // Pulse: bands at arrival distance = time * speed (mod spacing), moving outward and fading towards the reach.
                float phase = frac((arrival - _Time.y * _PulseSpeed) / _PulseSpacing);
                half pulse = pow(phase, 8.0) * withinReach * saturate(1.0 - arrival / max(_Reach, 1e-3)) * 0.5;
                half3 pulseRgb = arrival <= _Budget ? _OneColour.rgb : _TwoColour.rgb;
                colour = over(colour, half4(pulseRgb, pulse));

                // Rings at the one-point budget (when two points are possible) and at the full reach.
                half edge = passable * moves;
                half ringOne = _Reach > _Budget + 1e-3 ? lineAt(arrival, _Budget, arrivalWidth) * edge : 0.0;
                half ringTwo = lineAt(arrival, _Reach, arrivalWidth) * edge;
                colour = over(colour, half4(_OneColour.rgb, ringOne * 0.9));
                colour = over(colour, half4(_Reach > _Budget + 1e-3 ? _TwoColour.rgb : _OneColour.rgb, ringTwo * 0.9));

                // Other units' bodies within reach: ground the unit could otherwise walk to, drawn as blocked.
                half blocked = occupied * withinReach * _OccupiedColour.a;
                half blockedEdge = lineAt(data.b, 0.5, bodyWidth) * withinReach * 0.9;
                colour = over(colour, half4(_OccupiedColour.rgb, saturate(blocked + blockedEdge)));
                return colour;
            }
            ENDHLSL
        }
    }
}
