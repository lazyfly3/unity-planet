Shader "Hidden/Voxel Planet/Underwater Screen"
{
    Properties { _MainTex ("Source", 2D) = "white" {} _Blend ("Blend", Range(0,1)) = 0 }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float _Blend;
            fixed4 Frag(v2f_img input) : SV_Target
            {
                fixed4 source = tex2D(_MainTex, input.uv);
                float2 centered = input.uv * 2 - 1;
                float vignette = saturate(dot(centered, centered) * 0.42);
                fixed3 tint = lerp(fixed3(0.02, 0.23, 0.31), fixed3(0.01, 0.08, 0.15), vignette);
                source.rgb = lerp(source.rgb, source.rgb * 0.45 + tint, _Blend * (0.55 + vignette * 0.25));
                return source;
            }
            ENDCG
        }
    }
}
