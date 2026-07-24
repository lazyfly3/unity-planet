Shader "VoxelPlanet/AtmosphereShell"
{
    Properties
    {
        _HorizonColor ("Horizon", Color) = (0.2,0.55,1,1)
        _ZenithColor ("Zenith", Color) = (0.03,0.12,0.3,1)
        _SunsetColor ("Sunset", Color) = (1,0.25,0.06,1)
        _Scattering ("Scattering", Range(0,2)) = 0.8
    }
    SubShader
    {
        Tags { "Queue"="Transparent+20" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Back
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f
            {
                float4 position : SV_POSITION;
                float3 worldPosition : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
            };

            float4 _HorizonColor;
            float4 _ZenithColor;
            float4 _SunsetColor;
            float3 _PlanetCenter;
            float _Scattering;

            v2f vert(appdata input)
            {
                v2f output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                output.worldNormal = UnityObjectToWorldNormal(input.normal);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float3 normal = normalize(input.worldNormal);
                float3 toCamera = normalize(_WorldSpaceCameraPos.xyz - input.worldPosition);
                float rim = pow(1.0 - saturate(dot(normal, toCamera)), 2.8);
                float3 radial = normalize(input.worldPosition - _PlanetCenter);
                float sunFacing = saturate(dot(radial, normalize(_WorldSpaceLightPos0.xyz)));
                float sunset = pow(1.0 - abs(dot(radial, normalize(_WorldSpaceLightPos0.xyz))), 6.0)
                    * sunFacing;
                float3 color = lerp(_ZenithColor.rgb, _HorizonColor.rgb, rim);
                color = lerp(color, _SunsetColor.rgb, sunset * 0.75);
                return fixed4(color, saturate(rim * _Scattering * 0.62));
            }
            ENDCG
        }
    }
    Fallback Off
}
