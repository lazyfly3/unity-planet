Shader "Voxel Planet/Low Poly Surface Sky"
{
    Properties
    {
        _HorizonColor ("Horizon", Color) = (0.42,0.68,0.9,1)
        _ZenithColor ("Zenith", Color) = (0.08,0.22,0.42,1)
        _SunsetColor ("Sunset", Color) = (0.95,0.34,0.16,1)
        _HazeStrength ("Haze", Range(0,1)) = 0.2
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "IgnoreProjector"="True" }
        Cull Front
        ZWrite Off
        ZTest LEqual

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; };
            struct v2f
            {
                float4 position : SV_POSITION;
                float3 worldPosition : TEXCOORD0;
            };

            float4 _HorizonColor;
            float4 _ZenithColor;
            float4 _SunsetColor;
            float3 _PlanetCenter;
            float3 _SunDirection;
            float _HazeStrength;

            v2f vert(appdata input)
            {
                v2f output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float3 cameraUp = normalize(_WorldSpaceCameraPos.xyz - _PlanetCenter);
                float3 viewDirection = normalize(input.worldPosition - _WorldSpaceCameraPos.xyz);
                float elevation = saturate(dot(viewDirection, cameraUp));
                float zenithBlend = pow(elevation, 0.42);
                float horizonBand = pow(1.0 - elevation, 3.0);
                float sunFacing = saturate(dot(viewDirection, normalize(_SunDirection)));
                float sunset = pow(sunFacing, 5.0) * horizonBand;
                float3 color = lerp(_HorizonColor.rgb, _ZenithColor.rgb, zenithBlend);
                color = lerp(color, _SunsetColor.rgb, sunset * 0.82);
                color = lerp(color, _HorizonColor.rgb, horizonBand * _HazeStrength * 0.24);
                return fixed4(color, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
