Shader "CityGeneration/SciFiModularRoad"
{
    Properties
    {
        _MainTex ("Road Albedo", 2D) = "white" {}
        [Normal] _BumpMap ("Road Normal", 2D) = "bump" {}
        _BaseColor ("Base Color", Color) = (0.25, 0.28, 0.31, 1)
        _Metallic ("Metallic", Range(0, 1)) = 0.08
        _Smoothness ("Smoothness", Range(0, 1)) = 0.42
        [HDR] _EmissionColor ("Lane Emission", Color) = (0.02, 1.4, 2.2, 1)
        _EmissionThreshold ("Lane Threshold", Range(0, 1)) = 0.62
        _EmissionSoftness ("Lane Softness", Range(0.01, 0.4)) = 0.16
        _EmissionStrength ("Emission Strength", Range(0, 4)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+8" }
        LOD 300

        CGPROGRAM
        #pragma surface Surf Standard fullforwardshadows addshadow
        #pragma target 3.0
        #pragma multi_compile_instancing

        sampler2D _MainTex;
        sampler2D _BumpMap;
        fixed4 _BaseColor;
        half _Metallic;
        half _Smoothness;
        fixed4 _EmissionColor;
        half _EmissionThreshold;
        half _EmissionSoftness;
        half _EmissionStrength;
        half _CityNightFactor;

        struct Input
        {
            float2 uv_MainTex;
            float2 uv_BumpMap;
        };

        void Surf(Input input, inout SurfaceOutputStandard output)
        {
            fixed4 albedo = tex2D(_MainTex, input.uv_MainTex);
            output.Albedo = albedo.rgb * _BaseColor.rgb;
            output.Normal = UnpackNormal(tex2D(_BumpMap, input.uv_BumpMap));
            output.Metallic = _Metallic;
            output.Smoothness = _Smoothness;
            output.Alpha = 1;

            half luminance = dot(albedo.rgb, half3(0.299, 0.587, 0.114));
            half laneMask = smoothstep(
                _EmissionThreshold,
                _EmissionThreshold + _EmissionSoftness,
                luminance);
            half nightBoost = lerp(0.12, 2.8, saturate(_CityNightFactor));
            output.Emission =
                _EmissionColor.rgb
                * laneMask
                * _EmissionStrength
                * nightBoost;
        }
        ENDCG
    }
    Fallback "Standard"
}
