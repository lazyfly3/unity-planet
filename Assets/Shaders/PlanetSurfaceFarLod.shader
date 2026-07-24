Shader "VoxelPlanet/SurfaceFarLod"
{
    Properties
    {
        _UseLowPolyVisual ("Use Low Poly Visual", Float) = 0
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
        _TransitionWidth ("Transition Width", Float) = 36
        _TransitionOuterRadius ("Transition Ring Outer Radius", Float) = 160
        _TransitionDepression ("Transition Depression", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _CullMode ("Cull Mode", Float) = 2
        _EnableNearTerrainCutout ("Enable Near Terrain Cutout", Float) = 1
        _MinimumAmbient ("Minimum Ambient", Range(0,0.5)) = 0.16
        _HeightScale ("Height Scale", Float) = 140
        _SeaLevel ("Sea Level", Range(-0.08,0.08)) = 0
        _ShoreWidth ("Shore Width", Range(0.002,0.08)) = 0.018
        _SnowLine ("Snow Line", Range(0.15,0.95)) = 0.68
        _SnowAmount ("Snow Amount", Range(0,1)) = 0.55
    }
    SubShader
    {
        Tags { "Queue"="Geometry-10" "RenderType"="Opaque" }
        Cull [_CullMode]
        ZWrite On

        Pass
        {
            Tags { "LightMode"="ForwardBase" }
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
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPosition : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float lodDistance : TEXCOORD2;
                float4 color : COLOR;
                SHADOW_COORDS(3)
                UNITY_FOG_COORDS(4)
            };

            float3 _PlanetCenter;
            float3 _HideCenter;
            float _HideRadius;
            float _RadialInset;
            float _UseLowPolyVisual;
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
            float _PlanetRadius;
            float _TransitionWidth;
            float _TransitionOuterRadius;
            float _TransitionDepression;
            float _EnableNearTerrainCutout;
            float _MinimumAmbient;
            float _HeightScale;
            float _SeaLevel;
            float _ShoreWidth;
            float _SnowLine;
            float _SnowAmount;

            float Hash31(float3 value)
            {
                value = frac(value * 0.1031);
                value += dot(value, value.yzx + 33.33);
                return frac((value.x + value.y) * value.z);
            }

            v2f vert(appdata input)
            {
                v2f output;
                float3 world = mul(unity_ObjectToWorld, input.vertex).xyz;
                float3 radial = normalize(world - _PlanetCenter);
                float lodDistance = distance(world, _HideCenter);
                float transitionOuter = max(
                    _HideRadius + max(1.0, _TransitionWidth),
                    _TransitionOuterRadius);
                float depression = 1.0 - smoothstep(
                    max(0.0, _HideRadius - _TransitionWidth),
                    transitionOuter,
                    lodDistance);
                depression *= saturate(_EnableNearTerrainCutout);
                world -= radial * (
                    _RadialInset
                    + depression * max(0.0, _TransitionDepression));
                output.pos = mul(UNITY_MATRIX_VP, float4(world, 1.0));
                output.worldPosition = world;
                output.worldNormal = UnityObjectToWorldNormal(input.normal);
                output.lodDistance = lodDistance;
                output.color = input.color;
                TRANSFER_SHADOW(output);
                UNITY_TRANSFER_FOG(output, output.pos);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float distanceToPlayer = input.lodDistance;
                float transition = saturate(
                    (distanceToPlayer - (_HideRadius - _TransitionWidth))
                    / max(1.0, _TransitionWidth * 2.0));
                // Anchor the narrow dissolve band to the terrain itself. The old
                // screen-space hash produced a large, camera-facing halftone ring.
                float worldDither = Hash31(
                    floor(input.worldPosition * 0.85) + 41.0);
                float cutout = transition - worldDither;
                clip(lerp(1.0, cutout, saturate(_EnableNearTerrainCutout)));

                float3 smoothNormal = normalize(input.worldNormal);
                float3 faceNormal = normalize(cross(ddy(input.worldPosition), ddx(input.worldPosition)));
                if (dot(faceNormal, smoothNormal) < 0.0)
                    faceNormal = -faceNormal;
                float3 normal = normalize(lerp(
                    smoothNormal,
                    faceNormal,
                    _FacetStrength * saturate(_UseLowPolyVisual)));
                float3 radial = normalize(input.worldPosition - _PlanetCenter);

                fixed3 terrain = input.color.rgb;
                if (_UseLowPolyVisual > 0.5)
                {
                    float altitudeRaw =
                        (distance(input.worldPosition, _PlanetCenter) - _PlanetRadius)
                        / max(1.0, _HeightScale);
                    float altitude = saturate(altitudeRaw);
                    float slope = 1.0 - saturate(dot(normal, radial));
                    float cliff = smoothstep(_CliffSlope * 0.68, _CliffSlope, slope);
                    float shore = (1.0 - smoothstep(
                        _ShoreWidth * 0.28,
                        _ShoreWidth,
                        abs(altitudeRaw - _SeaLevel)))
                        * step(_SeaLevel - _ShoreWidth * 0.3, altitudeRaw);
                    float snow = smoothstep(
                        _SnowLine,
                        min(1.0, _SnowLine + 0.18),
                        altitude)
                        * (1.0 - cliff)
                        * _SnowAmount;
                    float3 cell = floor(input.worldPosition / max(1.0, _MacroColorSize));
                    float macro = (Hash31(cell) - 0.5) * 2.0;
                    float accent = smoothstep(0.7, 0.94, Hash31(cell + 17.0));
                    terrain = lerp(_LowlandColor.rgb, _HighlandColor.rgb, altitude);
                    terrain = lerp(terrain, _CliffColor.rgb, cliff);
                    terrain = lerp(terrain, _RockColor.rgb, cliff * altitude);
                    terrain = lerp(terrain, _AccentColor.rgb, accent * (1.0 - cliff) * 0.18);
                    terrain = lerp(terrain, _ShoreColor.rgb, shore * (1.0 - cliff * 0.65));
                    terrain = lerp(terrain, _SnowColor.rgb, snow);
                    terrain *= 1.0 + macro * _MacroVariation;
                }

                half shadow = SHADOW_ATTENUATION(input);
                half diffuse = saturate(dot(normal, normalize(_WorldSpaceLightPos0.xyz)));
                half bands = max(3.0h, _LightingBands);
                diffuse = _UseLowPolyVisual > 0.5
                    ? floor(diffuse * bands + 0.45h) / bands
                    : diffuse;
                half3 lighting = ShadeSH9(half4(normal, 1.0h))
                    + _LightColor0.rgb * (0.2h + diffuse * 0.8h) * shadow;
                lighting = max(lighting, _MinimumAmbient.xxx);
                fixed4 result = fixed4(terrain * lighting, 1.0);
                UNITY_APPLY_FOG(input.fogCoord, result);
                return result;
            }
            ENDCG
        }
    }
    Fallback Off
}
