// URP rewrite of AdultLink/Rimlight (Rimlight.shader, MIT, Copyright (c) 2018 Guillermo Angel; Built-in surface
// shader made with Amplify). Same rim term as the source, added to the lit colour as emission:
//   scale' = Pulsate ? (Scale / 2) * (sin(Frequency * time) + 1) : Scale
//   rim    = Enable * Colour * (Bias + scale' * pow(1 - dot(N, V), Power)) * Opacity * noiseTerm
//   noiseTerm = AddNoise ? snoise(NoiseSpeed * time + NoiseScale * worldPos) + 1 : 1
// The 3D simplex noise below is the source's, copied unchanged. Base shading: URP's PBR (metallic 0) instead of the
// source's Standard Specular with texture inputs; no albedo/normal/specular/occlusion/emission maps.
Shader "GameTech/Rimlight"
{
    Properties
    {
        _BaseColor("Albedo tint color", Color) = (1,1,1,1)
        _Smoothness("Smoothness", Range(0,1)) = 0
        [Toggle]_Enablerimlight("Enable rimlight", Float) = 1
        [HDR]_Rimlightcolor("Rimlight color", Color) = (0,0.8344827,1,1)
        _Rimlightpower("Rimlight power", Float) = 3.5
        _Rimlightscale("Rimlight scale", Float) = 1
        _Rimlightbias("Rimlight bias", Float) = 0
        _Rimlightopacity("Rimlight opacity", Range(0,1)) = 1
        [Toggle]_Pulsate("Pulsate", Range(0,1)) = 0
        _Frequency("Frequency", Float) = 2
        [Toggle]_Addnoise("Add noise", Range(0,1)) = 0
        _Noisescale("Noise scale", Float) = 1
        _Noisespeed("Noise speed", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        CBUFFER_START(UnityPerMaterial)
        half4 _BaseColor; half _Smoothness;
        float _Enablerimlight; float4 _Rimlightcolor; float _Rimlightpower, _Rimlightscale, _Rimlightbias, _Rimlightopacity;
        float _Pulsate, _Frequency, _Addnoise, _Noisescale; float4 _Noisespeed;
        CBUFFER_END

        // ---- from the source, unchanged ----
		float3 mod3D289( float3 x ) { return x - floor( x / 289.0 ) * 289.0; }

		float4 mod3D289( float4 x ) { return x - floor( x / 289.0 ) * 289.0; }

		float4 permute( float4 x ) { return mod3D289( ( x * 34.0 + 1.0 ) * x ); }

		float4 taylorInvSqrt( float4 r ) { return 1.79284291400159 - r * 0.85373472095314; }

		float snoise( float3 v )
		{
			const float2 C = float2( 1.0 / 6.0, 1.0 / 3.0 );
			float3 i = floor( v + dot( v, C.yyy ) );
			float3 x0 = v - i + dot( i, C.xxx );
			float3 g = step( x0.yzx, x0.xyz );
			float3 l = 1.0 - g;
			float3 i1 = min( g.xyz, l.zxy );
			float3 i2 = max( g.xyz, l.zxy );
			float3 x1 = x0 - i1 + C.xxx;
			float3 x2 = x0 - i2 + C.yyy;
			float3 x3 = x0 - 0.5;
			i = mod3D289( i);
			float4 p = permute( permute( permute( i.z + float4( 0.0, i1.z, i2.z, 1.0 ) ) + i.y + float4( 0.0, i1.y, i2.y, 1.0 ) ) + i.x + float4( 0.0, i1.x, i2.x, 1.0 ) );
			float4 j = p - 49.0 * floor( p / 49.0 );  // mod(p,7*7)
			float4 x_ = floor( j / 7.0 );
			float4 y_ = floor( j - 7.0 * x_ );  // mod(j,N)
			float4 x = ( x_ * 2.0 + 0.5 ) / 7.0 - 1.0;
			float4 y = ( y_ * 2.0 + 0.5 ) / 7.0 - 1.0;
			float4 h = 1.0 - abs( x ) - abs( y );
			float4 b0 = float4( x.xy, y.xy );
			float4 b1 = float4( x.zw, y.zw );
			float4 s0 = floor( b0 ) * 2.0 + 1.0;
			float4 s1 = floor( b1 ) * 2.0 + 1.0;
			float4 sh = -step( h, 0.0 );
			float4 a0 = b0.xzyw + s0.xzyw * sh.xxyy;
			float4 a1 = b1.xzyw + s1.xzyw * sh.zzww;
			float3 g0 = float3( a0.xy, h.x );
			float3 g1 = float3( a0.zw, h.y );
			float3 g2 = float3( a1.xy, h.z );
			float3 g3 = float3( a1.zw, h.w );
			float4 norm = taylorInvSqrt( float4( dot( g0, g0 ), dot( g1, g1 ), dot( g2, g2 ), dot( g3, g3 ) ) );
			g0 *= norm.x;
			g1 *= norm.y;
			g2 *= norm.z;
			g3 *= norm.w;
			float4 m = max( 0.6 - float4( dot( x0, x0 ), dot( x1, x1 ), dot( x2, x2 ), dot( x3, x3 ) ), 0.0 );
			m = m* m;
			m = m* m;
			float4 px = float4( dot( x0, g0 ), dot( x1, g1 ), dot( x2, g2 ), dot( x3, g3 ) );
			return 42.0 * dot( m, px);
		}
        // ---- end of source noise ----

        float4 Rim(float3 positionWS, float3 normalWS, float3 viewWS)
        {
            float fresnelScale = ((1.0 - _Pulsate) * _Rimlightscale) + (_Pulsate * ((_Rimlightscale / 2.0) * (sin(_Frequency * _Time.y) + 1.0)));
            float ndotv = dot(normalWS, viewWS);
            float fresnel = _Rimlightbias + fresnelScale * pow(1.0 - ndotv, _Rimlightpower);
            float n = snoise((_Noisespeed.xyz * _Time.y) + (_Noisescale * positionWS));
            return _Enablerimlight * (_Rimlightcolor * fresnel * _Rimlightopacity * (((n + _Addnoise) * _Addnoise) + (1.0 - _Addnoise)));
        }

        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
        struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; };

        Varyings Vert(Attributes i)
        {
            Varyings o;
            o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
            o.positionCS = TransformWorldToHClip(o.positionWS);
            o.normalWS = TransformObjectToWorldNormal(i.normalOS);
            return o;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            half4 Frag(Varyings i) : SV_Target
            {
                InputData d = (InputData)0;
                d.positionWS = i.positionWS;
                d.normalWS = normalize(i.normalWS);
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                d.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                d.bakedGI = SampleSH(d.normalWS);
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                d.shadowMask = half4(1, 1, 1, 1);
                SurfaceData s = (SurfaceData)0;
                s.albedo = _BaseColor.rgb; s.metallic = 0; s.smoothness = _Smoothness; s.occlusion = 1; s.alpha = 1;
                s.emission = Rim(i.positionWS, d.normalWS, d.viewDirectionWS).rgb;
                return UniversalFragmentPBR(d, s);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back
            HLSLPROGRAM
            #pragma vertex SVert
            #pragma fragment SFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection;
            float3 _LightPosition;
            Varyings SVert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 ld = normalize(_LightPosition - o.positionWS);
            #else
                float3 ld = _LightDirection;
            #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(o.positionWS, o.normalWS, ld));
            #if UNITY_REVERSED_Z
                cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
            #else
                cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                o.positionCS = cs;
                return o;
            }
            half4 SFrag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment DFrag
            half4 DFrag(Varyings i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment NFrag
            half4 NFrag(Varyings i) : SV_Target { return half4(normalize(i.normalWS), 0); }
            ENDHLSL
        }
    }
}
