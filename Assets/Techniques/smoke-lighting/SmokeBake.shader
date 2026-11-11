// Own bake for the 6-way smoke post: ray-marches a procedural smoke puff and writes a 4x4 flipbook atlas.
// Pass 0: RGB = puff lit from Right / Left / Top, A = opacity.   Pass 1: RGB = lit from Bottom / Back / Front.
// Pass 2: tangent-space normal (for the normal-map workflow), A = opacity.
// Colours are premultiplied by opacity. Tangent space of a tile: +x right, +y up, +z towards the viewer.
Shader "Hidden/GameTech/SmokeBake"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        float _Bake_Sigma;      // extinction per unit of density and length
        float _Bake_FlipY;

        struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
        V Vert(float4 positionOS : POSITION, float2 uv : TEXCOORD0)
        {
            V o; o.positionCS = TransformObjectToHClip(positionOS.xyz); o.uv = uv; return o;
        }

        float Hash3(float3 p)
        {
            p = frac(p * 0.3183099 + 0.1); p *= 17.0;
            return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
        }
        float VNoise(float3 x)
        {
            float3 i = floor(x), f = frac(x); f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(lerp(Hash3(i), Hash3(i + float3(1, 0, 0)), f.x), lerp(Hash3(i + float3(0, 1, 0)), Hash3(i + float3(1, 1, 0)), f.x), f.y),
                        lerp(lerp(Hash3(i + float3(0, 0, 1)), Hash3(i + float3(1, 0, 1)), f.x), lerp(Hash3(i + float3(0, 1, 1)), Hash3(i + float3(1, 1, 1)), f.x), f.y), f.z);
        }
        float Fbm(float3 p, int oct)
        {
            float a = 0.5, s = 0.0, n = 0.0;
            [loop] for (int k = 0; k < oct; k++) { s += a * VNoise(p); n += a; p = p * 2.03 + 17.7; a *= 0.5; }
            return s / n;
        }

        // Puff density in [0,1]. p in [-1,1]^3, ph = frame / frame count. Later frames are larger and more eroded.
        float Density(float3 p, float ph, int oct)
        {
            float r = length(p);
            if (r > 0.98) return 0.0;
            float3 q = p * 1.9 + float3(3.1, 7.7 - ph * 1.2, 1.3 + ph * 0.5);
            // domain warp: bends the lumps into wisps
            q += 0.6 * (float3(VNoise(q * 1.3 + 5.2), VNoise(q * 1.3 + 17.1), VNoise(q * 1.3 + 29.9)) - 0.5);
            float n = Fbm(q, oct);
            float shape = 1.0 - r / lerp(0.74, 0.96, ph);
            float d = shape + (n - 0.5) * 1.5 - lerp(0.12, 0.40, ph);
            return saturate(d * 1.5) * smoothstep(0.98, 0.78, r);
        }

        void TileSetup(float2 uv, out float3 p0, out float ph)
        {
            if (_Bake_FlipY > 0.5) uv.y = 1.0 - uv.y;
            float2 cell = floor(uv * 4.0);
            float2 local = frac(uv * 4.0) * 2.0 - 1.0;
            ph = (cell.x + cell.y * 4.0) / 16.0;
            p0 = float3(local, 1.0);
        }

        // light that reaches p from direction d (unit axis), with a soft floor so shadows do not go to pure black
        float LightReach(float3 p, float3 d, float ph)
        {
            float tau = 0.0;
            [loop] for (int k = 0; k < 8; k++) tau += Density(p + d * ((k + 0.5) * 0.11), ph, 3) * 0.11;
            tau *= _Bake_Sigma;
            return max(exp(-tau), 0.32 * exp(-0.22 * tau));
        }

        half4 March(float2 uv, float3 d0, float3 d1, float3 d2)
        {
            float3 p0; float ph; TileSetup(uv, p0, ph);
            const int N = 44; float dz = 2.0 / N;
            float T = 1.0; float3 c = 0;
            [loop] for (int s = 0; s < N; s++)
            {
                float3 p = float3(p0.xy, 1.0 - (s + 0.5) * dz);
                float d = Density(p, ph, 5);
                if (d > 0.003)
                {
                    float a = 1.0 - exp(-_Bake_Sigma * d * dz);
                    float3 lit = float3(LightReach(p, d0, ph), LightReach(p, d1, ph), LightReach(p, d2, ph));
                    c += T * a * lit; T *= 1.0 - a;
                    if (T < 0.01) break;
                }
            }
            return half4(c, 1.0 - T);
        }
        ENDHLSL

        Pass
        {
            Name "RLT"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(V i) : SV_Target { return March(i.uv, float3(1, 0, 0), float3(-1, 0, 0), float3(0, 1, 0)); }
            ENDHLSL
        }
        Pass
        {
            Name "BBF"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(V i) : SV_Target { return March(i.uv, float3(0, -1, 0), float3(0, 0, -1), float3(0, 0, 1)); }
            ENDHLSL
        }
        Pass
        {
            Name "Normal"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(V i) : SV_Target
            {
                float3 p0; float ph; TileSetup(i.uv, p0, ph);
                const int N = 44; float dz = 2.0 / N;
                float T = 1.0; float3 n = 0;
                [loop] for (int s = 0; s < N; s++)
                {
                    float3 p = float3(p0.xy, 1.0 - (s + 0.5) * dz);
                    float d = Density(p, ph, 4);
                    if (d > 0.003)
                    {
                        float a = 1.0 - exp(-_Bake_Sigma * d * dz);
                        float e = 0.10;
                        float3 g = float3(Density(p + float3(e, 0, 0), ph, 3), Density(p + float3(0, e, 0), ph, 3), Density(p + float3(0, 0, e), ph, 3)) - Density(p, ph, 3);
                        float gl = length(g);
                        if (gl > 1e-4) n += T * a * (-g / gl);
                        T *= 1.0 - a;
                        if (T < 0.01) break;
                    }
                }
                float r2 = saturate(dot(p0.xy, p0.xy));
                float3 sphere = float3(p0.xy, sqrt(1.0 - r2));
                float3 nn = n + sphere * 1.0 * (1.0 - T);
                nn.z = max(nn.z, 0.05);
                nn = dot(nn, nn) > 1e-6 ? normalize(nn) : float3(0, 0, 1);
                return half4(nn * 0.5 + 0.5, 1.0 - T);
            }
            ENDHLSL
        }
    }
}
