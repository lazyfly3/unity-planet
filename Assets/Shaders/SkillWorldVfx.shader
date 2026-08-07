Shader "UnityPlanet/SkillWorldVfx"
{
    Properties
    {
        _MainTex ("Effect Texture", 2D) = "white" {}
        _Tint ("Tint", Color) = (1,1,1,1)
        _Intensity ("Intensity", Range(0, 5)) = 1.4
        _RimStrength ("Rim Strength", Range(0, 3)) = 0
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.5
        _ScrollX ("Scroll X", Float) = 0
        _ScrollY ("Scroll Y", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent+20"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Tint;
            float _Intensity;
            float _RimStrength;
            float _RimPower;
            float _ScrollX;
            float _ScrollY;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float3 viewDirection : TEXCOORD2;
            };

            v2f vert(appdata input)
            {
                v2f output;
                float4 worldPosition = mul(unity_ObjectToWorld, input.vertex);
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.uv += float2(_ScrollX, _ScrollY) * _Time.y;
                output.worldNormal = UnityObjectToWorldNormal(input.normal);
                output.viewDirection = _WorldSpaceCameraPos.xyz - worldPosition.xyz;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                fixed4 textureColor = tex2D(_MainTex, input.uv);
                float normalFacing = abs(dot(
                    normalize(input.worldNormal),
                    normalize(input.viewDirection)));
                float rim = pow(saturate(1.0 - normalFacing), _RimPower) *
                            _RimStrength;
                float alpha = saturate(textureColor.a + rim) * _Tint.a;
                fixed3 color = textureColor.rgb * _Tint.rgb * _Intensity +
                               _Tint.rgb * rim;
                return fixed4(color, alpha);
            }
            ENDCG
        }
    }
    Fallback Off
}
