Shader "Voxel Planet/Low Poly Surface"
{
    Properties
    {
        _Color ("Base Color", Color) = (1,1,1,1)
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
        _SeaLevel ("Sea Level", Range(-0.08,0.08)) = 0
        _ShoreWidth ("Shore Width", Range(0.002,0.08)) = 0.018
        _SnowLine ("Snow Line", Range(0.15,0.95)) = 0.68
        _SnowAmount ("Snow Amount", Range(0,1)) = 0.55
        _MinimumAmbient ("Minimum Ambient", Range(0,0.5)) = 0.26
        _Glossiness ("Smoothness", Range(0,1)) = 0.12
        _DustColor ("Dust Color", Color) = (0.62,0.28,0.1,1)
        _CrystalColor ("Crystal Color", Color) = (0.62,0.18,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 220

        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            Cull Back
            ZWrite On

            CGPROGRAM
            #pragma target 3.0
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
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPosition : TEXCOORD0;
                float3 smoothNormal : TEXCOORD1;
                SHADOW_COORDS(2)
                UNITY_FOG_COORDS(3)
            };

            fixed4 _Color;
            fixed4 _LowlandColor;
            fixed4 _HighlandColor;
            fixed4 _CliffColor;
            fixed4 _RockColor;
            fixed4 _AccentColor;
            fixed4 _ShoreColor;
            fixed4 _SnowColor;
            fixed4 _DustColor;
            fixed4 _CrystalColor;
            half _FacetStrength;
            half _LightingBands;
            float _MacroColorSize;
            half _MacroVariation;
            half _CliffSlope;
            float _HeightScale;
            float _SeaLevel;
            float _ShoreWidth;
            float _SnowLine;
            float _SnowAmount;
            half _MinimumAmbient;
            half _Glossiness;
            float3 _PlanetCenter;
            float _PlanetRadius;
            float _WeatherWetness;
            float _WeatherDust;
            float _WeatherCrystal;
            float3 _SurfaceScanOrigin;
            float _SurfaceScanRadius;
            float _SurfaceScanWidth;
            float _SurfaceScanStrength;
            fixed4 _SurfaceScanColor;

            float Hash31(float3 value)
            {
                value = frac(value * 0.1031);
                value += dot(value, value.yzx + 33.33);
                return frac((value.x + value.y) * value.z);
            }

            float SurfaceScanMask(float3 worldPosition)
            {
                if (_SurfaceScanStrength <= 0.001)
                    return 0.0;
                float3 originDirection = normalize(
                    _SurfaceScanOrigin - _PlanetCenter);
                float3 pixelDirection = normalize(
                    worldPosition - _PlanetCenter);
                float cosine = clamp(
                    dot(originDirection, pixelDirection),
                    -1.0,
                    1.0);
                float surfaceDistance = acos(cosine) * max(1.0, _PlanetRadius);
                float width = max(0.25, _SurfaceScanWidth);
                float band = 1.0 - smoothstep(
                    width,
                    width * 1.8,
                    abs(surfaceDistance - _SurfaceScanRadius));
                float trail = step(surfaceDistance, _SurfaceScanRadius)
                    * saturate(1.0 - surfaceDistance / max(1.0, _SurfaceScanRadius))
                    * 0.08;
                return saturate(band + trail) * saturate(_SurfaceScanStrength);
            }

            v2f vert(appdata input)
            {
                v2f output;
                output.pos = UnityObjectToClipPos(input.vertex);
                output.worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                output.smoothNormal = UnityObjectToWorldNormal(input.normal);
                TRANSFER_SHADOW(output);
                UNITY_TRANSFER_FOG(output, output.pos);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float3 smoothNormal = normalize(input.smoothNormal);
                float3 faceNormal = normalize(cross(ddy(input.worldPosition), ddx(input.worldPosition)));
                if (dot(faceNormal, smoothNormal) < 0.0)
                    faceNormal = -faceNormal;
                float3 normal = normalize(lerp(smoothNormal, faceNormal, _FacetStrength));
                float3 radialUp = normalize(input.worldPosition - _PlanetCenter);

                float altitude = (distance(input.worldPosition, _PlanetCenter) - _PlanetRadius)
                    / max(1.0, _HeightScale);
                float slope = 1.0 - saturate(dot(normal, radialUp));
                float cliff = smoothstep(_CliffSlope * 0.68, _CliffSlope, slope);
                float highland = smoothstep(0.18, 0.82, altitude);
                float shore = (1.0 - smoothstep(
                    _ShoreWidth * 0.28,
                    _ShoreWidth,
                    abs(altitude - _SeaLevel)))
                    * step(_SeaLevel - _ShoreWidth * 0.3, altitude);
                float snow = smoothstep(
                    _SnowLine,
                    min(1.0, _SnowLine + 0.18),
                    altitude)
                    * (1.0 - cliff)
                    * _SnowAmount;
                float3 cell = floor(input.worldPosition / max(1.0, _MacroColorSize));
                float macro = (Hash31(cell) - 0.5) * 2.0;
                float accentMask = smoothstep(0.68, 0.94, Hash31(cell + 17.0));

                fixed3 terrain = lerp(_LowlandColor.rgb, _HighlandColor.rgb, highland);
                terrain = lerp(terrain, _CliffColor.rgb, cliff);
                terrain = lerp(terrain, _RockColor.rgb, cliff * smoothstep(0.35, 0.8, highland));
                terrain = lerp(terrain, _AccentColor.rgb, accentMask * (1.0 - cliff) * 0.22);
                terrain = lerp(terrain, _ShoreColor.rgb, shore * (1.0 - cliff * 0.65));
                terrain = lerp(terrain, _SnowColor.rgb, snow);
                terrain *= 1.0 + macro * _MacroVariation;

                float upwardSurface = saturate(dot(normal, radialUp) * 0.75 + 0.25);
                float deposit = upwardSurface
                    * smoothstep(0.18, 0.82, Hash31(cell * 1.73 + 3.0) + 0.3);
                float dust = saturate(_WeatherDust * deposit);
                float crystal = saturate(_WeatherCrystal * deposit);
                float wet = saturate(_WeatherWetness * (0.55 + upwardSurface * 0.35));
                terrain = lerp(terrain, terrain * 0.72, wet);
                terrain = lerp(terrain, _DustColor.rgb, dust * 0.58);
                terrain = lerp(terrain, _CrystalColor.rgb, crystal * 0.45);

                half shadow = SHADOW_ATTENUATION(input);
                half diffuse = saturate(dot(normal, normalize(_WorldSpaceLightPos0.xyz)));
                half bands = max(3.0h, _LightingBands);
                diffuse = floor(diffuse * bands + 0.45h) / bands;
                half3 ambient = ShadeSH9(half4(normal, 1.0h));
                half3 lighting = ambient
                    + _LightColor0.rgb * (0.18h + diffuse * 0.82h) * shadow;
                lighting = max(lighting, _MinimumAmbient.xxx);
                half3 viewDirection = normalize(_WorldSpaceCameraPos.xyz - input.worldPosition);
                half3 halfVector = normalize(normalize(_WorldSpaceLightPos0.xyz) + viewDirection);
                half specular = pow(saturate(dot(normal, halfVector)), lerp(18.0h, 72.0h, _Glossiness));

                fixed3 color = terrain * _Color.rgb * lighting;
                color += _LightColor0.rgb * specular * (_Glossiness + wet * 0.2h) * 0.18h * shadow;
                color += _CrystalColor.rgb * crystal * 0.07h;
                float scan = SurfaceScanMask(input.worldPosition);
                color += _SurfaceScanColor.rgb
                    * scan
                    * (1.05 + slope * 0.42 + upwardSurface * 0.18);
                fixed4 result = fixed4(color, 1.0);
                UNITY_APPLY_FOG(input.fogCoord, result);
                return result;
            }
            ENDCG
        }

        UsePass "Legacy Shaders/VertexLit/SHADOWCASTER"
    }
    FallBack "Diffuse"
}
