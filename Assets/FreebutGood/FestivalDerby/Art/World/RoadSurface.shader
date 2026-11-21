// Road surface blend for the baked road sweep.
//
// One pass, mobile-friendly, no Shader Graph: the swept mesh's vertex colour carries the two surface
// ids (R = primary, G = secondary) and the blend weight (B) the bake resolved, and the fragment
// shader samples a Texture2DArray of albedos at those two slices. Two texture fetches is the whole
// budget, which is why a surface is encoded per vertex instead of blended per texel by an artist.
//
// The art contract lives beside this file in README.md: five layers in the order Street, Dirt, Snow,
// Mud, Gravel, each a square power-of-two RGBA texture, same size for all five.
Shader "FestivalDerby/RoadSurface"
{
    Properties
    {
        // Assigned by the road art builder; the array slices are the layers in README.md order.
        _SurfaceAlbedos("Surface Albedos", 2DArray) = "" {}
        // Metres one texture tile covers on both axes. The bake bakes the same scale into UVs; this
        // only lets a material re-tile without re-baking.
        _Tiling("Tiles Per Metre", Range(0.01, 1)) = 0.25
        _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        // Floors the shading so a surface reads in a corner the main light does not reach, which is
        // the whole lighting model this cheap road needs.
        _Ambient("Ambient", Range(0, 1)) = 0.45

        // ── Snow / sand ruts ──────────────────────────────────────────────────────────────
        // Enabled by the _RUTS keyword, which the snow and sand materials turn on (see
        // Documentation/Design/SNOW_RUTS.md). With it off the shader is the same two-fetch road it
        // was: the whole block below is compiled out, not branched around.
        [Toggle(_RUTS)] _RutsEnabled("Tyre Ruts", Float) = 0
        _RutTint("Rut Tint", Color) = (0.72, 0.78, 0.88, 1)
        // How dark the pressed ground goes. Snow holds a shaded groove; sand barely does.
        _RutDarkness("Rut Darkening", Range(0, 1)) = 0.16
        _RutSharpness("Rut Sharpness", Range(1, 12)) = 3
        // Width of the window's fade-out at its border, in mask UV. Must stay outside the frustum:
        // if the window edge is on screen the prints end in a straight line.
        _RutFeather("Window Feather (UV)", Range(0.01, 0.3)) = 0.09
        // Extra mask taps used to rebuild a slope from the depth gradient, in texels.
        _RutDentSpread("Dent Sample Spread (texels)", Range(0.5, 8)) = 2
        _RutDentStrength("Dent Tilt", Range(0, 24)) = 8
        _RutAO("Rut Occlusion", Range(0, 1)) = 0.45
        _RutRim("Rut Lip Highlight", Range(0, 12)) = 3
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            // Texture arrays need ES 3.0 / shader model 3.5; the project is Android-first GLES3.
            #pragma target 3.5
            // Local keyword: a road material without ruts compiles without the mask sampling at all.
            #pragma shader_feature_local _RUTS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D_ARRAY(_SurfaceAlbedos);
            SAMPLER(sampler_SurfaceAlbedos);

            // The ground mask, published as a global by World/Runtime/GroundMaskDriver: one shared
            // texture, so a rut is written once and every road material reads it at the same place.
            // Declared unconditionally so both variants keep one CBUFFER layout (SRP Batcher).
            TEXTURE2D(_GroundMaskRT);
            // Declared here and not taken from an include: URP only ships the point sampler, and this
            // mask must read smooth (the grooves are six texels wide; point sampling would stair-step
            // them). The mask's own filter mode is bilinear, so this is the same filtering it asks for.
            SAMPLER(sampler_linear_clamp);

            CBUFFER_START(UnityPerMaterial)
                float4 _SurfaceAlbedos_ST;
                float _Tiling;
                float4 _BaseColor;
                float _Ambient;
                // The toggle's own float is a material property like any other, so it belongs in the
                // CBUFFER: leave it out and the shader drops out of the SRP Batcher entirely.
                float _RutsEnabled;
                float4 _RutTint;
                float _RutDarkness;
                float _RutSharpness;
                float _RutFeather;
                float _RutDentSpread;
                float _RutDentStrength;
                float _RutAO;
                float _RutRim;
            CBUFFER_END

            // Globals owned by GroundMaskDriver. Never inside the material CBUFFER: they are shared
            // by every rutted material and must not be per-material constants.
            float4 _GroundMaskOrigin;   // world XZ of the window's lower-left corner, .y and .w unused
            float _GroundMaskSize;      // side of the window in world units, 0 when no driver is live
            float _GroundMaskTexelUV;   // 1 / resolution

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 color : COLOR;
                // World position is only needed by the rut path, but interpolating it in one extra
                // register is cheaper than a second vertex/fragment program for one keyword.
                float3 positionWS : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = input.color;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Decode the baked ids and weight. +0.5 rounds to the nearest byte after the *255,
                // so a colour that went through 8-bit quantization lands on the id the bake wrote.
                int primary = (int)(input.color.r * 255.0 + 0.5);
                int secondary = (int)(input.color.g * 255.0 + 0.5);
                float blend = saturate(input.color.b);

                float2 uv = input.uv * _Tiling;
                half3 primaryAlbedo = SAMPLE_TEXTURE2D_ARRAY(
                    _SurfaceAlbedos, sampler_SurfaceAlbedos, uv, primary).rgb;
                half3 secondaryAlbedo = SAMPLE_TEXTURE2D_ARRAY(
                    _SurfaceAlbedos, sampler_SurfaceAlbedos, uv, secondary).rgb;
                half3 albedo = lerp(primaryAlbedo, secondaryAlbedo, blend);

                Light mainLight = GetMainLight();
                float3 normalWS = normalize(input.normalWS);
                half3 rutRim = half3(0.0, 0.0, 0.0);

            #if defined(_RUTS)
                // ── Ruts ──────────────────────────────────────────────────────────────────────
                // The mask is sampled by WORLD XZ and not by mesh UV: the road is a long swept ribbon
                // whose UVs wrap every _Tiling metres, so a UV lookup would put the same print in
                // every tile and drag the pattern with the texture.
                float2 rutUv = (input.positionWS.xz - _GroundMaskOrigin.xz) / max(_GroundMaskSize, 0.001);

                // Fade the mask out towards the window border, so the end of the tracked area never
                // shows as a straight line. Multiplying by the window size zeroes the whole path while
                // no driver is live, which is what makes an unconfigured build look exactly like a
                // build without this shader path.
                float2 border = min(rutUv, 1.0 - rutUv);
                float feather = saturate(min(border.x, border.y) / max(_RutFeather, 1.0e-4));
                feather *= saturate(_GroundMaskSize - 1.0);

                // Three taps: the mask at the pixel and one texel-ish step along each axis. The two
                // extra taps are what turn a flat dark patch into a groove with a slope, and they are
                // the entire reason this shader knows a direction at all.
                float spread = _GroundMaskTexelUV * _RutDentSpread;
                float mask = SAMPLE_TEXTURE2D(_GroundMaskRT, sampler_linear_clamp, saturate(rutUv)).r;
                float maskX = SAMPLE_TEXTURE2D(_GroundMaskRT, sampler_linear_clamp, saturate(rutUv + float2(spread, 0.0))).r;
                float maskZ = SAMPLE_TEXTURE2D(_GroundMaskRT, sampler_linear_clamp, saturate(rutUv + float2(0.0, spread))).r;
                float2 gradient = float2(maskX - mask, maskZ - mask) * feather;

                float sampled = lerp(1.0, mask, feather);
                // Sharpness narrows the transition around the middle of the range: at 3 a soft radial
                // stamp reads as a pressed groove with a lip, at 1 it reads as a grey smear.
                float intact = saturate((sampled - 0.5) * _RutSharpness + 0.5);

                // Albedo: darker and cooler inside the rut. Lerping to a TINT and not to black keeps
                // the snow reading as snow in shadow instead of as a hole in the ground.
                half3 pressed = albedo * _RutTint.rgb * (1.0 - _RutDarkness);
                albedo = lerp(pressed, albedo, intact);
                // Contact occlusion: the pressed floor of the groove sees less sky.
                albedo *= lerp(1.0 - _RutAO, 1.0, intact);

                // Fake the slope the geometry does not have: tilt the normal against the depth
                // gradient, so the light model below shades the two walls of a rut differently.
                normalWS = normalize(normalWS + float3(-gradient.x, 0.0, -gradient.y) * _RutDentStrength);

                // A hair of extra light on the lip, where the snow is squeezed up and catches the sun.
                rutRim = mainLight.color * (saturate(length(gradient) * _RutRim) * (1.0 - intact));
            #endif

                float ndotl = saturate(dot(normalWS, mainLight.direction));
                half3 lit = albedo * (mainLight.color * ndotl + _Ambient) * _BaseColor.rgb;
                lit += rutRim * ndotl * _BaseColor.rgb;
                return half4(lit, 1.0h);
            }
            ENDHLSL
        }
    }

    // A plain URP unlit keeps the road visible if the array is ever missing, instead of a magenta hole.
    FallBack "Universal Render Pipeline/Unlit"
}
