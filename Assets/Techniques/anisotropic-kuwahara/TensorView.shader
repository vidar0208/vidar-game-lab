// Debug view of the anisotropic pass's structure tensor output (t.x, t.y, phi, A):
// hue = edge direction (phi, 180 degrees per hue turn), brightness = anisotropy A times edge strength (from the unblurred tensor _ST).
Shader "Hidden/GT/TensorView"
{
    Properties { _MainTex ("Tensor", 2D) = "black" {} }
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex, _ST;
            float _Gain;

            float3 Hue(float h)
            {
                float3 k = abs(frac(h + float3(0, 2.0 / 3.0, 1.0 / 3.0)) * 6 - 3) - 1;
                return saturate(k);
            }

            float4 frag(v2f_img i) : SV_Target
            {
                float4 t = tex2D(_MainTex, i.uv);
                float hue = frac(t.z / UNITY_PI + 1.0);
                float4 e = tex2D(_ST, i.uv);
                float a = saturate(t.w) * saturate(sqrt(e.x + e.y) * _Gain);
                return float4(Hue(hue) * a, 1);
            }
            ENDCG
        }
    }
}
