Shader "UnityPlanet/BossEnergyShield"
{
    Properties
    {
        _MainTex ("Energy Pattern", 2D) = "black" {}
        [HDR] _Color ("Shield Color", Color) = (0.05, 1.4, 2.8, 1)
        [HDR] _CriticalColor ("Critical Color", Color) = (2.8, 0.35, 0.04, 1)
        _Intensity ("Intensity", Range(0, 5)) = 1.5
        _Opacity ("Opacity", Range(0, 1)) = 0.65
        _Integrity ("Integrity", Range(0, 1)) = 1
        _HitFlash ("Hit Flash", Range(0, 1)) = 0
        _Scroll ("Scroll", Vector) = (0.018, 0.007, -0.012, 0.015)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent+35"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Blend SrcAlpha One
        Cull Off
        ZWrite Off
        ZTest LEqual

        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            struct Attributes
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
                float3 worldPosition : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            half4 _Color;
            half4 _CriticalColor;
            half _Intensity;
            half _Opacity;
            half _Integrity;
            half _HitFlash;
            float4 _Scroll;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.worldNormal = UnityObjectToWorldNormal(input.normal);
                output.worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normal = normalize(input.worldNormal);
                float3 viewDirection = normalize(
                    _WorldSpaceCameraPos.xyz - input.worldPosition);
                half rim = pow(1.0h - saturate(abs(dot(normal, viewDirection))), 2.15h);

                float2 firstUv = input.uv * 1.65 + _Time.y * _Scroll.xy;
                float2 secondUv = input.uv.yx * 2.1 + _Time.y * _Scroll.zw;
                half3 firstPattern = tex2D(_MainTex, firstUv).rgb;
                half3 secondPattern = tex2D(_MainTex, secondUv).rgb;
                half pattern = max(
                    dot(firstPattern, half3(0.18h, 0.62h, 0.20h)),
                    dot(secondPattern, half3(0.12h, 0.68h, 0.20h)) * 0.58h);
                pattern = smoothstep(0.035h, 0.72h, pattern);

                half critical = saturate((0.42h - _Integrity) / 0.42h);
                half3 tint = lerp(_Color.rgb, _CriticalColor.rgb, critical);
                half pulse = 0.88h + 0.12h * sin(_Time.y * 3.4h + input.worldPosition.y * 0.08h);
                half energy = rim * 0.68h + pattern * (0.72h + critical * 0.5h);
                energy += _HitFlash * (0.72h + rim * 0.8h);
                half alpha = saturate(energy * _Opacity * pulse);
                half3 color = tint * energy * _Intensity;
                return half4(color, alpha);
            }
            ENDCG
        }
    }
    Fallback Off
}
