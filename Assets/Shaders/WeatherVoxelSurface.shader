Shader "Voxel Planet/Weather/Surface"
{
    Properties
    {
        _Color ("Base Color", Color) = (1,1,1,1)
        _Metallic ("Metallic", Range(0,1)) = 0
        _Glossiness ("Smoothness", Range(0,1)) = 0.12
        _DustColor ("Dust Color", Color) = (0.62,0.28,0.1,1)
        _CrystalColor ("Crystal Color", Color) = (0.62,0.18,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 220
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

        fixed4 _Color;
        half _Metallic;
        half _Glossiness;
        fixed4 _DustColor;
        fixed4 _CrystalColor;
        float3 _PlanetCenter;
        float _WeatherWetness;
        float _WeatherDust;
        float _WeatherCrystal;

        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
        };

        void surf(Input input, inout SurfaceOutputStandard output)
        {
            float3 radialUp = normalize(input.worldPos - _PlanetCenter);
            float upwardSurface = saturate(dot(normalize(input.worldNormal), radialUp) * 0.75 + 0.25);
            float breakup = frac(sin(dot(floor(input.worldPos * 1.7), float3(12.9898,78.233,37.719))) * 43758.5453);
            float deposit = upwardSurface * smoothstep(0.2, 0.8, breakup + 0.35);
            float dust = saturate(_WeatherDust * deposit);
            float crystal = saturate(_WeatherCrystal * deposit);
            float wet = saturate(_WeatherWetness * (0.6 + upwardSurface * 0.4));

            fixed3 color = _Color.rgb;
            color = lerp(color, color * 0.48, wet);
            color = lerp(color, _DustColor.rgb, dust * 0.72);
            color = lerp(color, _CrystalColor.rgb, crystal * 0.58);
            output.Albedo = color;
            output.Metallic = saturate(_Metallic + crystal * 0.18);
            output.Smoothness = saturate(_Glossiness + wet * 0.72 + crystal * 0.35);
            output.Emission = _CrystalColor.rgb * crystal * (0.08 + breakup * 0.12);
            output.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Standard"
}
