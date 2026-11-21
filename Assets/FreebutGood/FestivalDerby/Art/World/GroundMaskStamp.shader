// ─────────────────────────────────────────────────────────────────────────────
// Ground Mask Stamp — the rut pressed into the ground mask (snow / sand).
// ─────────────────────────────────────────────────────────────────────────────
// Ported from NinjaSurvivor by the same owner. There the stamp was a black quad with
// "URP/Unlit" drawn to a capture RT and combined with the accumulator by min(capture,
// accumulator) using two blits and a temporary RT, every frame.
//
// Here the min IS the blend (BlendOp Min): the quad is drawn straight onto the
// persistent mask. The capture RT is gone, the two blits are gone, and so is the
// GetTemporary per frame. Same accumulation, zero extra passes.
//
// The edge is RADIAL and soft. The original stamped a hard square and the footprints
// read as pixels instead of presses.
// ─────────────────────────────────────────────────────────────────────────────
Shader "FestivalDerby/GroundMaskStamp"
{
    Properties
    {
        _Softness ("Edge Softness", Range(0, 1)) = 0.45
        _Depth    ("Rut Depth", Range(0, 1)) = 0.55
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "RutStamp"

            // Min against the accumulator: anything pressed NEVER comes back through this
            // path (the optional recovery is the separate, throttled fade pass).
            BlendOp Min
            Blend One One
            ColorMask R
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Softness;
            CBUFFER_END

            // Per instance: one wheel spinning digs deeper than one that is only rolling, and the whole
            // frame is still one instanced draw. It must live in its own instancing buffer: declared
            // inside UnityPerMaterial (as first written) the instanced variant has no
            // "UnityPerMaterialArray" to index and fails to compile, and the draw never reaches the mask.
            UNITY_INSTANCING_BUFFER_START(RutStampProps)
                UNITY_DEFINE_INSTANCED_PROP(float, _Depth)
            UNITY_INSTANCING_BUFFER_END(RutStampProps)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                // The instance id has to reach the FRAGMENT: the depth is a per-instance property, and a
                // fragment without the id reads instance zero and presses every wheel at the same depth.
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                // Radius 0 at the centre, 1 at the quad's edge.
                float2 d = IN.uv * 2.0 - 1.0;
                float r = saturate(length(d));

                // Centre = _Depth (dark = pressed), edge = 1, which leaves the accumulator
                // untouched because min(1, x) = x. The quad's corners come out neutral on
                // their own.
                float depth = UNITY_ACCESS_INSTANCED_PROP(RutStampProps, _Depth);
                float v = lerp(depth, 1.0, smoothstep(1.0 - _Softness, 1.0, r));
                return half4(v, 0, 0, 1);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
