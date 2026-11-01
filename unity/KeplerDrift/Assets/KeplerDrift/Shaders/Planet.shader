// Planet: URP 2D sprite shader. A sphere normal is generated from the quad sprite's UV; light comes from _SunDirection, not a
// 2D Light. Why not 2D lights: the scene has a single sun and its direction is mission data; the 2D light system drew every
// body with its own light volume and needed an extra pass on mobile, here a single pass is enough.
// Lighting: "wrap" diffuse (light wraps slightly past the terminator; a thin-atmosphere body's night side is not pitch black),
// soft terminator (smoothstep; a hard line stair-stepped at low resolution), rim glow (_AtmosphereColor; strong on the sun
// side), a faint halo outside the disk. The colour triple is the prototype's `base` array: lit, mid, night.
// The disk is _DiskRatio of the quad; the rest goes to the halo. Edge anti-aliasing via fwidth (1-1.5 px at every scale).
Shader "KeplerDrift/Planet"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Lit ("Lit", Color) = (0.93, 0.65, 0.49, 1)
        _Mid ("Mid", Color) = (0.71, 0.33, 0.18, 1)
        _Night ("Night", Color) = (0.29, 0.11, 0.06, 1)
        _SunDirection ("Sun direction (UV space, z = out of the screen)", Vector) = (-0.62, 0.62, 0.48, 0)
        _Wrap ("Wrap", Range(0, 1)) = 0.3
        _Terminator ("Terminator softness", Range(0.01, 0.5)) = 0.16
        _AtmosphereColor ("Atmosphere colour", Color) = (0.55, 0.75, 1, 1)
        _AtmosphereStrength ("Atmosphere strength", Range(0, 2)) = 0.6
        _DiskRatio ("Disk / quad", Range(0.5, 1)) = 0.82
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" "CanUseSpriteAtlas" = "True" }
        // Premultiplied alpha: so no dark fringe stays at the edge where the halo (outside the disk) and the disk overlap in one pass.
        Blend One OneMinusSrcAlpha
        Cull Off ZWrite Off

        Pass
        {
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float3 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            // SRP Batcher compatibility: all material properties in a single CBUFFER.
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Lit, _Mid, _Night, _AtmosphereColor;
                float4 _SunDirection;
                half _Wrap, _Terminator, _AtmosphereStrength, _DiskRatio;
            CBUFFER_END

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = (i.uv * 2.0 - 1.0) / _DiskRatio;   // disk edge = 1
                float r = length(p);
                float3 L = normalize(_SunDirection.xyz);

                // Disk: sphere normal from UV (z = sqrt(1 - r^2)).
                float z = sqrt(saturate(1.0 - r * r));
                float3 n = float3(p, z);
                float ndl = dot(n, L);
                float wrapped = saturate((ndl + _Wrap) / (1.0 + _Wrap));
                half3 day = lerp(_Mid.rgb, _Lit.rgb, wrapped);
                half night = smoothstep(0.0, _Terminator, wrapped);   // soft terminator
                half3 col = lerp(_Night.rgb, day, night);
                // Rim glow: grows as the surface turns toward the edge (z -> 0), strong on the sun side, fading on the night side.
                float2 dir = r > 1e-4 ? p / r : float2(0, 0);
                half sunSide = saturate(dot(dir, normalize(L.xy + 1e-4)) * 0.5 + 0.5);
                half rim = pow(1.0 - z, 3.0) * _AtmosphereStrength * sunSide;
                col += _AtmosphereColor.rgb * rim;
                half aw = max(fwidth(r) * 1.5, 1e-4);
                half disk = saturate((1.0 - r) / aw);

                // Halo: faint light fading from the disk edge to the quad edge (none on an airless body: _AtmosphereStrength = 0).
                half margin = max(1.0 / _DiskRatio - 1.0, 1e-3);
                half h = saturate(1.0 - (r - 1.0) / margin);
                half halo = h * h * h * 0.35 * _AtmosphereStrength * sunSide * step(1.0, r + aw);

                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half a = disk + halo * (1.0 - disk);
                half3 rgb = col * disk + _AtmosphereColor.rgb * halo * (1.0 - disk);   // premultiplied
                half k = tex.a * i.color.a;
                return half4(rgb * i.color.rgb * k, a * k);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
