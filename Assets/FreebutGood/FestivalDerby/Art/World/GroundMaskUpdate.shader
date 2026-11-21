// ─────────────────────────────────────────────────────────────────────────────
// Ground Mask Update — SCROLL + FADE in a single pass.
// ─────────────────────────────────────────────────────────────────────────────
// Both are "read the mask, write it back transformed", so they share one blit:
// separating them would double the most expensive pass of the system for nothing.
//
//   SCROLL: the window follows the local car. _UVOffset always arrives in EXACT
//           multiples of a texel (the driver snaps the origin to a grid) and the
//           sampling is POINT, so the copy is 1:1 and not one bit is lost. The earlier
//           version in Survival_Game reprojected with sub-texel offsets and bilinear
//           filtering every frame: that resamples, and the prints smeared themselves
//           away until they vanished.
//
//   FADE:   adds _Fade to everything. That is "the snow recovers" and "the sand fills
//           in" — the only difference between the two is how much _Fade is worth.
//
// Whatever comes in from outside the window comes out WHITE (intact ground): it is the
// part of the world nobody has driven over yet.
// ─────────────────────────────────────────────────────────────────────────────
Shader "Hidden/FestivalDerby/GroundMaskUpdate"
{
    Properties
    {
        _MainTex ("Mask", 2D) = "white" {}
        _Fade    ("Fade", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            ZTest Always
            ZWrite Off
            Cull Off
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            // POINT and not the RT's own sampler (which is Bilinear): with the offset already
            // texel-aligned, bilinear still averages the four neighbours through rounding at
            // the texel centre and diffuses the mask a little on every blit. At three blits
            // per second, a minute of driving and there are no prints left.
            SAMPLER(sampler_point_clamp);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float2 _UVOffset;
                float  _Fade;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv + _UVOffset;

                // Out of range = new territory, intact ground. The branch resolves per
                // wavefront except in the border strip, so it is free.
                float value = 1.0;
                if (all(uv == saturate(uv)))
                    value = SAMPLE_TEXTURE2D(_MainTex, sampler_point_clamp, uv).r;

                return half4(saturate(value + _Fade), 0, 0, 1);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
