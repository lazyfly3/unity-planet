Shader "UnityPlanet/NeoXThrusterExhaust"
{
    Properties
    {
        [HDR] _Color ("Tint", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
        }

        Blend SrcAlpha One
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            struct Attributes
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            half4 _Color;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.color = input.color * _Color;
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 centered = input.uv - 0.5;
                half radial = saturate(1.0 - length(centered) * 2.0);
                radial = radial * radial * (3.0 - 2.0 * radial);
                half axial = smoothstep(0.0, 0.12, input.uv.y)
                             * smoothstep(1.0, 0.76, input.uv.y);
                half4 color = input.color * _Color;
                color.a *= radial * axial;
                return color;
            }
            ENDCG
        }
    }
}
