Shader "Hidden/UnityPlanet/CityBloom"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _BloomTex ("Bloom", 2D) = "black" {}
        _Threshold ("Threshold", Float) = 1.02
        _Intensity ("Intensity", Float) = 0.78
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex;
        sampler2D _BloomTex;
        float4 _MainTex_TexelSize;
        half _Threshold;
        half _Intensity;

        half4 FragBright(v2f_img input) : SV_Target
        {
            half3 color = tex2D(_MainTex, input.uv).rgb;
            half brightness = max(color.r, max(color.g, color.b));
            half contribution = saturate(
                (brightness - _Threshold) / max(brightness, 0.0001h));
            return half4(color * contribution, 1.0h);
        }

        half4 FragHorizontal(v2f_img input) : SV_Target
        {
            float2 offset = float2(_MainTex_TexelSize.x, 0.0);
            half3 color = tex2D(_MainTex, input.uv).rgb * 0.227027h;
            color += tex2D(_MainTex, input.uv + offset * 1.384615).rgb * 0.316216h;
            color += tex2D(_MainTex, input.uv - offset * 1.384615).rgb * 0.316216h;
            color += tex2D(_MainTex, input.uv + offset * 3.230769).rgb * 0.070270h;
            color += tex2D(_MainTex, input.uv - offset * 3.230769).rgb * 0.070270h;
            return half4(color, 1.0h);
        }

        half4 FragVertical(v2f_img input) : SV_Target
        {
            float2 offset = float2(0.0, _MainTex_TexelSize.y);
            half3 color = tex2D(_MainTex, input.uv).rgb * 0.227027h;
            color += tex2D(_MainTex, input.uv + offset * 1.384615).rgb * 0.316216h;
            color += tex2D(_MainTex, input.uv - offset * 1.384615).rgb * 0.316216h;
            color += tex2D(_MainTex, input.uv + offset * 3.230769).rgb * 0.070270h;
            color += tex2D(_MainTex, input.uv - offset * 3.230769).rgb * 0.070270h;
            return half4(color, 1.0h);
        }

        half4 FragComposite(v2f_img input) : SV_Target
        {
            half4 source = tex2D(_MainTex, input.uv);
            half3 bloom = tex2D(_BloomTex, input.uv).rgb;
            return half4(source.rgb + bloom * _Intensity, source.a);
        }
        ENDCG

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment FragBright
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment FragHorizontal
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment FragVertical
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment FragComposite
            ENDCG
        }
    }
    Fallback Off
}
