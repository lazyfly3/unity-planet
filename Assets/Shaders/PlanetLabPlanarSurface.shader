Shader "VoxelPlanet/PlanetLabPlanarSurface"
{
    Properties
    {
        _LowlandColor ("Lowland", Color) = (0.22,0.42,0.24,1)
        _HighlandColor ("Highland", Color) = (0.42,0.5,0.3,1)
        _CliffColor ("Cliff", Color) = (0.24,0.24,0.22,1)
        _RockColor ("Rock", Color) = (0.32,0.3,0.28,1)
        _AccentColor ("Accent", Color) = (0.55,0.68,0.34,1)
        _ShoreColor ("Shore", Color) = (0.72,0.68,0.42,1)
        _SnowColor ("Snow", Color) = (0.9,0.92,0.84,1)
        _FacetStrength ("Facet Strength", Range(0,1)) = 0.72
        _LightingBands ("Lighting Bands", Range(3,5)) = 4
        _MacroColorSize ("Macro Color Size", Float) = 18
        _MacroVariation ("Macro Variation", Range(0,1)) = 0.16
        _CliffSlope ("Cliff Slope", Range(0.05,0.95)) = 0.58
        _HeightScale ("Height Scale", Float) = 140
        _SeaHeight ("Sea Height", Float) = 0
        _ShoreWidth ("Shore Width", Float) = 2
        _SnowLine ("Snow Line", Range(0.15,0.95)) = 0.68
        _SnowAmount ("Snow Amount", Range(0,1)) = 0.55
        _MinimumAmbient ("Minimum Ambient", Range(0,0.5)) = 0.16
        [Toggle] _UsePbrLibrary ("Use PBR Texture Library", Float) = 0
        _PbrAlbedoArray ("PBR Albedo Array", 2DArray) = "" {}
        _PbrNormalArray ("PBR Normal Array", 2DArray) = "" {}
        _PbrMaskArray ("PBR Height Roughness AO Array", 2DArray) = "" {}
        _GroundSlice ("Ground Slice", Float) = 0
        _RockSlice ("Rock Slice", Float) = 0
        _ShoreSlice ("Shore Slice", Float) = 0
        _ColdSlice ("Cold Slice", Float) = 0
        _PbrTiling ("PBR Tiling", Float) = 0.22
        _PbrNormalStrength ("PBR Normal Strength", Range(0,1)) = 0.72
        _PbrColorStrength ("PBR Color Strength", Range(0,1)) = 0.42
    }
    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" }
        Cull Back
        ZWrite On

        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma target 3.5
            #pragma require 2darray
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPosition : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float4 color : COLOR;
                SHADOW_COORDS(2)
                UNITY_FOG_COORDS(3)
            };

            float4 _LowlandColor;
            float4 _HighlandColor;
            float4 _CliffColor;
            float4 _RockColor;
            float4 _AccentColor;
            float4 _ShoreColor;
            float4 _SnowColor;
            float _FacetStrength;
            float _LightingBands;
            float _MacroColorSize;
            float _MacroVariation;
            float _CliffSlope;
            float _HeightScale;
            float _SeaHeight;
            float _ShoreWidth;
            float _SnowLine;
            float _SnowAmount;
            float _MinimumAmbient;
            float _UsePbrLibrary;
            UNITY_DECLARE_TEX2DARRAY(_PbrAlbedoArray);
            UNITY_DECLARE_TEX2DARRAY(_PbrNormalArray);
            UNITY_DECLARE_TEX2DARRAY(_PbrMaskArray);
            float _GroundSlice;
            float _RockSlice;
            float _ShoreSlice;
            float _ColdSlice;
            float _PbrTiling;
            float _PbrNormalStrength;
            float _PbrColorStrength;

            float Hash31(float3 value)
            {
                value = frac(value * 0.1031);
                value += dot(value, value.yzx + 33.33);
                return frac((value.x + value.y) * value.z);
            }

            v2f vert(appdata input)
            {
                v2f output;
                output.pos = UnityObjectToClipPos(input.vertex);
                output.worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                output.worldNormal = UnityObjectToWorldNormal(input.normal);
                output.color = input.color;
                TRANSFER_SHADOW(output);
                UNITY_TRANSFER_FOG(output, output.pos);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float3 smoothNormal = normalize(input.worldNormal);
                float3 faceNormal = normalize(cross(ddy(input.worldPosition), ddx(input.worldPosition)));
                if (dot(faceNormal, smoothNormal) < 0.0)
                    faceNormal = -faceNormal;
                float facetStrength = _FacetStrength
                    * lerp(1.0, 0.22, saturate(_UsePbrLibrary));
                float3 normal = normalize(lerp(
                    smoothNormal,
                    faceNormal,
                    facetStrength));
                float altitudeRaw = input.worldPosition.y / max(1.0, _HeightScale);
                float altitude = saturate(altitudeRaw);
                float slope = 1.0 - saturate(dot(normal, float3(0,1,0)));
                float cliff = smoothstep(_CliffSlope * 0.68, _CliffSlope, slope);
                float shore = 1.0 - smoothstep(
                    max(0.05, _ShoreWidth * 0.28),
                    max(0.1, _ShoreWidth),
                    abs(input.worldPosition.y - _SeaHeight));
                float snow = smoothstep(
                    _SnowLine,
                    min(1.0, _SnowLine + 0.18),
                    altitude) * (1.0 - cliff) * _SnowAmount;
                float3 cell = floor(input.worldPosition / max(1.0, _MacroColorSize));
                float macro = (Hash31(cell) - 0.5) * 2.0;
                float accent = smoothstep(0.7, 0.94, Hash31(cell + 17.0));

                float3 terrain = lerp(_LowlandColor.rgb, _HighlandColor.rgb, altitude);
                terrain = lerp(terrain, _CliffColor.rgb, cliff);
                terrain = lerp(terrain, _RockColor.rgb, cliff * altitude);
                terrain = lerp(terrain, _AccentColor.rgb, accent * (1.0 - cliff) * 0.18);
                terrain = lerp(terrain, _ShoreColor.rgb, shore * (1.0 - cliff * 0.65));
                terrain = lerp(terrain, _SnowColor.rgb, snow);
                terrain *= 1.0 + macro * _MacroVariation;

                float roughness = 0.78;
                float occlusion = 1.0;
                if (_UsePbrLibrary > 0.5)
                {
                    float rockWeight = saturate(max(cliff, altitude * 0.34));
                    float shoreWeight = saturate(shore * (1.0 - cliff * 0.75));
                    float coldWeight = saturate(snow);
                    float groundWeight = saturate(
                        1.0 - max(rockWeight, max(shoreWeight, coldWeight)));
                    float4 weights = float4(
                        groundWeight,
                        rockWeight,
                        shoreWeight,
                        coldWeight);

                    float2 pbrUv = input.worldPosition.xz
                        * max(0.001, _PbrTiling);
                    float4 groundMask = UNITY_SAMPLE_TEX2DARRAY(
                        _PbrMaskArray,
                        float3(pbrUv, _GroundSlice));
                    float4 rockMask = UNITY_SAMPLE_TEX2DARRAY(
                        _PbrMaskArray,
                        float3(pbrUv, _RockSlice));
                    float4 shoreMask = UNITY_SAMPLE_TEX2DARRAY(
                        _PbrMaskArray,
                        float3(pbrUv, _ShoreSlice));
                    float4 coldMask = UNITY_SAMPLE_TEX2DARRAY(
                        _PbrMaskArray,
                        float3(pbrUv, _ColdSlice));
                    weights *= lerp(
                        0.86,
                        1.14,
                        float4(
                            groundMask.r,
                            rockMask.r,
                            shoreMask.r,
                            coldMask.r));
                    weights /= max(
                        0.0001,
                        dot(weights, float4(1.0, 1.0, 1.0, 1.0)));

                    float3 groundAlbedo = UNITY_SAMPLE_TEX2DARRAY(
                        _PbrAlbedoArray,
                        float3(pbrUv, _GroundSlice)).rgb;
                    float3 rockAlbedo = UNITY_SAMPLE_TEX2DARRAY(
                        _PbrAlbedoArray,
                        float3(pbrUv, _RockSlice)).rgb;
                    float3 shoreAlbedo = UNITY_SAMPLE_TEX2DARRAY(
                        _PbrAlbedoArray,
                        float3(pbrUv, _ShoreSlice)).rgb;
                    float3 coldAlbedo = UNITY_SAMPLE_TEX2DARRAY(
                        _PbrAlbedoArray,
                        float3(pbrUv, _ColdSlice)).rgb;
                    float3 pbrAlbedo =
                        groundAlbedo * weights.x
                        + rockAlbedo * weights.y
                        + shoreAlbedo * weights.z
                        + coldAlbedo * weights.w;
                    float3 tint =
                        lerp(
                            float3(1.0, 1.0, 1.0),
                            _LowlandColor.rgb * 1.8,
                            _PbrColorStrength)
                            * weights.x
                        + lerp(
                            float3(1.0, 1.0, 1.0),
                            _RockColor.rgb * 1.8,
                            _PbrColorStrength)
                            * weights.y
                        + lerp(
                            float3(1.0, 1.0, 1.0),
                            _ShoreColor.rgb * 1.8,
                            _PbrColorStrength)
                            * weights.z
                        + lerp(
                            float3(1.0, 1.0, 1.0),
                            _SnowColor.rgb * 1.35,
                            _PbrColorStrength)
                            * weights.w;
                    terrain = pbrAlbedo * tint
                        * (1.0 + macro * _MacroVariation * 0.45);

                    float3 groundNormal = UnpackNormal(
                        UNITY_SAMPLE_TEX2DARRAY(
                            _PbrNormalArray,
                            float3(pbrUv, _GroundSlice)));
                    float3 rockNormal = UnpackNormal(
                        UNITY_SAMPLE_TEX2DARRAY(
                            _PbrNormalArray,
                            float3(pbrUv, _RockSlice)));
                    float3 shoreNormal = UnpackNormal(
                        UNITY_SAMPLE_TEX2DARRAY(
                            _PbrNormalArray,
                            float3(pbrUv, _ShoreSlice)));
                    float3 coldNormal = UnpackNormal(
                        UNITY_SAMPLE_TEX2DARRAY(
                            _PbrNormalArray,
                            float3(pbrUv, _ColdSlice)));
                    float3 tangentNormal = normalize(
                        groundNormal * weights.x
                        + rockNormal * weights.y
                        + shoreNormal * weights.z
                        + coldNormal * weights.w);
                    float3 worldDetailNormal = normalize(float3(
                        tangentNormal.x,
                        tangentNormal.z,
                        tangentNormal.y));
                    normal = normalize(lerp(
                        normal,
                        worldDetailNormal,
                        _PbrNormalStrength * (1.0 - cliff * 0.38)));

                    roughness = dot(
                        float4(
                            groundMask.g,
                            rockMask.g,
                            shoreMask.g,
                            coldMask.g),
                        weights);
                    occlusion = dot(
                        float4(
                            groundMask.b,
                            rockMask.b,
                            shoreMask.b,
                            coldMask.b),
                        weights);
                }

                half shadow = SHADOW_ATTENUATION(input);
                half diffuse = saturate(dot(normal, normalize(_WorldSpaceLightPos0.xyz)));
                half bands = max(3.0h, _LightingBands);
                half bandedDiffuse = floor(diffuse * bands + 0.45h) / bands;
                diffuse = lerp(
                    bandedDiffuse,
                    diffuse,
                    saturate(_UsePbrLibrary) * 0.82h);
                half3 lighting = ShadeSH9(half4(normal, 1.0h))
                    + _LightColor0.rgb * (0.2h + diffuse * 0.8h) * shadow;
                lighting = max(lighting, _MinimumAmbient.xxx);
                half3 viewDirection = normalize(
                    _WorldSpaceCameraPos.xyz - input.worldPosition);
                half3 halfDirection = normalize(
                    normalize(_WorldSpaceLightPos0.xyz) + viewDirection);
                half smoothness = 1.0h - saturate(roughness);
                half specularPower = lerp(
                    8.0h,
                    96.0h,
                    smoothness * smoothness);
                half specular = pow(
                    saturate(dot(normal, halfDirection)),
                    specularPower);
                specular *= lerp(0.025h, 0.16h, smoothness)
                    * shadow
                    * saturate(_UsePbrLibrary);
                fixed4 result = fixed4(
                    terrain * lighting * lerp(0.72, 1.0, occlusion)
                        + _LightColor0.rgb * specular,
                    1.0);
                UNITY_APPLY_FOG(input.fogCoord, result);
                return result;
            }
            ENDCG
        }
    }
    Fallback Off
}
