Shader "Hidden/PlanetLab/PackTerrainMask"
{
    Properties
    {
        _HeightTex ("Height", 2D) = "black" {}
        _RoughnessTex ("Roughness", 2D) = "white" {}
        _OcclusionTex ("Occlusion", 2D) = "white" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _HeightTex;
            sampler2D _RoughnessTex;
            sampler2D _OcclusionTex;

            fixed4 frag(v2f_img input) : SV_Target
            {
                return fixed4(
                    tex2D(_HeightTex, input.uv).r,
                    tex2D(_RoughnessTex, input.uv).r,
                    tex2D(_OcclusionTex, input.uv).r,
                    1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
