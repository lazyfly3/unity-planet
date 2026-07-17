Shader "Voxel Planet/Bio Creature Skin"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _MainTex ("Base Albedo", 2D) = "white" {}
        _PatternTex ("Pattern Layer", 2D) = "gray" {}
        _PatternColor ("Pattern Color", Color) = (0.2,0.3,0.35,1)
        _PatternStrength ("Pattern Strength", Range(0,1)) = 0
        _PatternScale ("Triplanar Pattern Scale", Range(0.25,12)) = 2.5
        [Normal] _NormalMap ("Normal Map", 2D) = "bump" {}
        _NormalStrength ("Normal Strength", Range(0,1)) = 0.7
        _MasksMap ("Masks (R Smoothness, G Metallic, B Skin)", 2D) = "black" {}
        _MaskStrength ("Mask Strength", Range(0,1)) = 0
        _Metallic ("Metallic", Range(0,1)) = 0
        _Glossiness ("Smoothness", Range(0,1)) = 0.22
        _SubsurfaceColor ("Subsurface Color", Color) = (0.4,0.1,0.06,1)
        _SubsurfaceStrength ("Subsurface Strength", Range(0,1)) = 0.1
        _FresnelStrength ("Soft Fresnel", Range(0,1)) = 0.08
        [HDR] _EmissionColor ("Emission Color", Color) = (0,0,0,1)
        _EmissionStrength ("Emission Strength", Range(0,3)) = 0
        [Toggle] _UseTriplanar ("Use Triplanar Pattern", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.0

        sampler2D _MainTex;
        sampler2D _PatternTex;
        sampler2D _NormalMap;
        sampler2D _MasksMap;
        fixed4 _Color;
        fixed4 _PatternColor;
        fixed4 _SubsurfaceColor;
        fixed4 _EmissionColor;
        half _PatternStrength;
        half _PatternScale;
        half _NormalStrength;
        half _MaskStrength;
        half _Metallic;
        half _Glossiness;
        half _SubsurfaceStrength;
        half _FresnelStrength;
        half _EmissionStrength;
        half _UseTriplanar;

        struct Input
        {
            fixed4 color : COLOR;
            float2 uv_MainTex;
            float2 uv_NormalMap;
            float2 uv_MasksMap;
            float3 worldPos;
            float3 worldNormal;
            float3 viewDir;
            INTERNAL_DATA
        };

        void vert(inout appdata_full vertex, out Input output)
        {
            UNITY_INITIALIZE_OUTPUT(Input, output);
            output.color = vertex.color;
        }

        fixed4 SampleTriplanar(sampler2D textureSampler, float3 worldPosition, float3 worldNormal)
        {
            float3 weights = pow(abs(normalize(worldNormal)), 4.0);
            weights /= max(weights.x + weights.y + weights.z, 0.0001);
            float scale = max(_PatternScale, 0.001);
            fixed4 x = tex2D(textureSampler, worldPosition.zy * scale);
            fixed4 y = tex2D(textureSampler, worldPosition.xz * scale);
            fixed4 z = tex2D(textureSampler, worldPosition.xy * scale);
            return x * weights.x + y * weights.y + z * weights.z;
        }

        void surf(Input input, inout SurfaceOutputStandard output)
        {
            fixed4 baseSample = tex2D(_MainTex, input.uv_MainTex);
            float3 geometricWorldNormal = normalize(WorldNormalVector(input, fixed3(0, 0, 1)));
            fixed4 patternSample = _UseTriplanar > 0.5
                ? SampleTriplanar(_PatternTex, input.worldPos, geometricWorldNormal)
                : tex2D(_PatternTex, input.uv_MainTex);
            half patternMask = saturate(dot(patternSample.rgb, fixed3(0.299, 0.587, 0.114)) * 2.0 - 0.5);
            fixed3 baseColor = input.color.rgb * _Color.rgb * baseSample.rgb;
            output.Albedo = lerp(baseColor, baseColor * _PatternColor.rgb,
                patternMask * _PatternStrength);

            fixed4 masks = tex2D(_MasksMap, input.uv_MasksMap);
            output.Metallic = lerp(_Metallic, masks.g, _MaskStrength);
            output.Smoothness = lerp(_Glossiness, masks.r, _MaskStrength);
            if (_UseTriplanar < 0.5)
            {
                fixed3 normalSample = UnpackNormal(tex2D(_NormalMap, input.uv_NormalMap));
                output.Normal = normalize(lerp(fixed3(0, 0, 1), normalSample, _NormalStrength));
            }

            half fresnel = pow(1.0 - saturate(dot(normalize(input.viewDir), output.Normal)), 3.0);
            half skinMask = lerp(1.0, masks.b, _MaskStrength);
            output.Emission = _SubsurfaceColor.rgb * (_SubsurfaceStrength * fresnel * skinMask)
                + _EmissionColor.rgb * _EmissionStrength * patternMask
                + output.Albedo * (_FresnelStrength * fresnel);
            output.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
