Shader "VoxelPlanet/AtmosphereShell"
{
    Properties
    {
        _HorizonColor ("Horizon", Color) = (0.2,0.55,1,1)
        _ZenithColor ("Zenith", Color) = (0.03,0.12,0.3,1)
        _SunsetColor ("Sunset", Color) = (1,0.25,0.06,1)
        _Scattering ("Scattering", Range(0,2)) = 0.8
        _CloudCoverage ("Cloud Coverage", Range(0,1)) = 0.4
    }
    SubShader
    {
        Tags { "Queue"="Transparent+20" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

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
            float3 _RotationAxis;
            float _PlanetRadius;
            float _AtmosphereRadius;
            float _Scattering;
            float _CloudCoverage;
            float _CloudSpeed;

            v2f vert(appdata input)
            {
                v2f output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                output.worldNormal = UnityObjectToWorldNormal(input.normal);
                return output;
            }

            float HashNoise(float3 p)
            {
                p += _Time.y * _CloudSpeed * float3(19.0, 7.0, 13.0);
                float n = sin(dot(p, float3(0.017, 0.023, 0.019)))
                    + sin(dot(p, float3(-0.031, 0.011, 0.027))) * 0.5;
                return n * 0.333 + 0.5;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float3 radial = normalize(input.worldPosition - _PlanetCenter);
                float3 view = normalize(_WorldSpaceCameraPos.xyz - input.worldPosition);
                float horizon = pow(1.0 - saturate(abs(dot(radial, view))), 2.2);
                float sunAmount = saturate(dot(radial, normalize(_WorldSpaceLightPos0.xyz)) * 0.5 + 0.5);
                float sunset = pow(1.0 - abs(dot(radial, normalize(_WorldSpaceLightPos0.xyz))), 5.0) * sunAmount;
                float height01 = saturate((length(input.worldPosition - _PlanetCenter) - _PlanetRadius)
                    / max(1.0, _AtmosphereRadius - _PlanetRadius));
                float cloud = smoothstep(1.0 - _CloudCoverage, 1.0, HashNoise(radial * _PlanetRadius));
                float3 color = lerp(_ZenithColor.rgb, _HorizonColor.rgb, horizon);
                color = lerp(color, _SunsetColor.rgb, sunset * 0.8);
                color = lerp(color, 1.0, cloud * 0.22 * sunAmount);
                float alpha = saturate((horizon * 0.72 + cloud * 0.12) * _Scattering * (1.0 - height01 * 0.35));
                return float4(color, alpha);
            }
            ENDCG
        }
    }
}
