Shader "Voxel Planet/NMS Creature Uber"
{
    Properties
    {
        _Color ("Legacy Tint", Color) = (1,1,1,1)
        _BaseColor ("Legacy Base Color", Color) = (1,1,1,1)
        _PrimaryColor ("Primary Color", Color) = (0.55,0.65,0.7,1)
        _SecondaryColor ("Secondary Color", Color) = (0.3,0.38,0.42,1)
        _AccentColor ("Accent Color", Color) = (0.35,0.28,0.2,1)
        _MainTex ("Albedo", 2D) = "white" {}
        [Normal] _NormalMap ("Normal Map", 2D) = "bump" {}
        _BumpMap ("Legacy Normal Map", 2D) = "bump" {}
        _MaskMap ("Mask Map (R Secondary, G Accent, B Smooth, A Emission)", 2D) = "black" {}
        _MasksMap ("Legacy Mask Map", 2D) = "black" {}
        _EmissionMap ("Emission Map", 2D) = "black" {}
        [HDR] _EmissionColor ("Emission Color", Color) = (0.1,0.7,1,1)
        _NormalStrength ("Normal Strength", Range(0,2)) = 0.85
        _MaskStrength ("Mask Strength", Range(0,1)) = 1
        _EmissionStrength ("Emission Strength", Range(0,4)) = 0
        _PaletteStrength ("Palette Tint Strength", Range(0,1)) = 0.35
        _VertexColorStrength ("Vertex Color Strength", Range(0,1)) = 0
        _Metallic ("Metallic", Range(0,1)) = 0
        _Glossiness ("Smoothness", Range(0,1)) = 0.28
        _SurfaceNoiseStrength ("Fine Surface Noise", Range(0,0.2)) = 0.035
        _AlphaCutoff ("Alpha Cutoff", Range(0,1)) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 250

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

        sampler2D _MainTex;
        sampler2D _NormalMap;
        sampler2D _BumpMap;
        sampler2D _MaskMap;
        sampler2D _MasksMap;
        sampler2D _EmissionMap;
        fixed4 _Color;
        fixed4 _BaseColor;
        fixed4 _PrimaryColor;
        fixed4 _SecondaryColor;
        fixed4 _AccentColor;
        fixed4 _EmissionColor;
        half _NormalStrength;
        half _MaskStrength;
        half _EmissionStrength;
        half _PaletteStrength;
        half _VertexColorStrength;
        half _Metallic;
        half _Glossiness;
        half _SurfaceNoiseStrength;
        half _AlphaCutoff;

        struct Input
        {
            float2 uv_MainTex;
            float2 uv_NormalMap;
            float2 uv_MaskMap;
            float2 uv_EmissionMap;
            float3 worldPos;
            fixed4 color : COLOR;
        };

        half HashNoise(float3 value)
        {
            return frac(sin(dot(value, float3(12.9898, 78.233, 37.719))) * 43758.5453);
        }

        void surf(Input input, inout SurfaceOutputStandard output)
        {
            fixed4 albedo = tex2D(_MainTex, input.uv_MainTex);
            clip(albedo.a - _AlphaCutoff);
            fixed4 mask = tex2D(_MaskMap, input.uv_MaskMap);
            fixed4 legacyMask = tex2D(_MasksMap, input.uv_MaskMap);
            mask = max(mask, legacyMask);

            fixed3 palette = _PrimaryColor.rgb;
            palette = lerp(palette, _SecondaryColor.rgb, saturate(mask.r * _MaskStrength));
            palette = lerp(palette, _AccentColor.rgb, saturate(mask.g * _MaskStrength));

            fixed3 vertexTint = lerp(fixed3(1, 1, 1), input.color.rgb, _VertexColorStrength);
            fixed3 colorTint = _Color.rgb * _BaseColor.rgb;
            half noise = (HashNoise(input.worldPos * 9.17) - 0.5) * _SurfaceNoiseStrength;
            fixed3 paletteTint = lerp(fixed3(1, 1, 1), palette, _PaletteStrength);
            output.Albedo = saturate(albedo.rgb * paletteTint * vertexTint * colorTint + noise);

            fixed3 normalSample = UnpackNormal(tex2D(_NormalMap, input.uv_NormalMap));
            fixed3 legacyNormalSample = UnpackNormal(tex2D(_BumpMap, input.uv_NormalMap));
            normalSample = normalize(normalSample + legacyNormalSample - fixed3(0, 0, 1));
            output.Normal = normalize(lerp(fixed3(0, 0, 1), normalSample, _NormalStrength));

            output.Metallic = _Metallic;
            output.Smoothness = saturate(lerp(_Glossiness, mask.b, _MaskStrength));

            fixed4 emission = tex2D(_EmissionMap, input.uv_EmissionMap);
            half emissionMask = saturate(max(mask.a, max(emission.r, max(emission.g, emission.b))));
            output.Emission = _EmissionColor.rgb * emissionMask * _EmissionStrength;
            output.Alpha = albedo.a;
        }
        ENDCG
    }

    FallBack "Standard"
}
