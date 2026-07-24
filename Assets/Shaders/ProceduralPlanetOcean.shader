Shader "VoxelPlanet/ProceduralOcean"
{
    Properties
    {
        _DeepColor ("Deep Ocean", Color) = (0.01,0.08,0.18,1)
        _ShallowColor ("Shallow Ocean", Color) = (0.02,0.55,0.68,1)
        _Smoothness ("Smoothness", Range(0,1)) = 0.86
        _WaveStrength ("Wave Strength", Range(0,1)) = 0.32
        _WaveScale ("Wave Scale", Range(0.1,3)) = 1
        _WaveSpeed ("Wave Speed", Range(0,3)) = 1
        _WaveHeight ("Wave Height", Float) = 0.4
        _NormalStrength ("Normal Strength", Range(0,2)) = 0.85
        _FoamStrength ("Foam Strength", Range(0,1)) = 0.62
        _RefractionStrength ("Refraction Strength", Range(0,0.04)) = 0.012
        _Opacity ("Opacity", Range(0.35,1)) = 0.82
        _OceanRadius ("Ocean Radius", Float) = 100
    }

    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 240
        Cull Back
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

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

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPosition : TEXCOORD0;
                float3 radial : TEXCOORD1;
                SHADOW_COORDS(2)
                UNITY_FOG_COORDS(3)
            };

            fixed4 _DeepColor, _ShallowColor;
            half _Smoothness, _WaveStrength, _NormalStrength, _FoamStrength, _Opacity;
            float _WaveScale, _WaveSpeed, _WaveHeight, _OceanRadius;
            float3 _PlanetCenter;
            float3 _SurfaceScanOrigin;
            float _SurfaceScanRadius;
            float _SurfaceScanWidth;
            float _SurfaceScanStrength;
            fixed4 _SurfaceScanColor;

            float Wave(float3 radial, float time)
            {
                float scale = lerp(7.0, 28.0, saturate(_WaveScale / 3.0));
                float3 p = radial * scale;
                float broad = sin(dot(p, float3(.83,.21,.48)) + time)
                    + sin(dot(p, float3(-.34,.91,.27)) - time * 1.17)
                    + sin(dot(p, float3(.24,-.39,.89)) + time * .73);
                float fine = sin(dot(p, float3(1.7,.6,-1.1)) - time * 1.71)
                    * sin(dot(p, float3(-.8,1.5,.7)) + time * 1.29);
                return (broad * .19 + fine * .25) * _WaveStrength;
            }

            float3 RippleNormal(float3 radial, float time)
            {
                float3 axis = abs(radial.y) < .9 ? float3(0,1,0) : float3(1,0,0);
                float3 tangent = normalize(cross(axis, radial));
                float3 bitangent = cross(radial, tangent);
                float frequency = lerp(42.0, 130.0, saturate(_WaveScale / 3.0));
                float a = dot(radial, float3(.73,.37,.57)) * frequency + time * 2.1;
                float b = dot(radial, float3(-.31,.91,.24)) * frequency * .73 - time * 1.6;
                float2 slope = float2(cos(a) + .55*cos(b), sin(b) + .45*sin(a*1.31));
                return normalize(radial + (tangent*slope.x + bitangent*slope.y) * .055 * _NormalStrength);
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
                float surfaceDistance = acos(cosine) * max(1.0, _OceanRadius);
                float width = max(0.25, _SurfaceScanWidth);
                float band = 1.0 - smoothstep(
                    width,
                    width * 1.8,
                    abs(surfaceDistance - _SurfaceScanRadius));
                return band * saturate(_SurfaceScanStrength);
            }

            v2f vert(appdata input)
            {
                v2f o;
                float3 world = mul(unity_ObjectToWorld, input.vertex).xyz;
                float3 radial = normalize(world - _PlanetCenter);
                float t = _Time.y * _WaveSpeed;
                world += radial * Wave(radial, t) * _WaveHeight;
                o.pos = mul(UNITY_MATRIX_VP, float4(world,1));
                o.worldPosition = world;
                o.radial = radial;
                TRANSFER_SHADOW(o);
                UNITY_TRANSFER_FOG(o,o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float t = _Time.y * _WaveSpeed;
                float3 n = RippleNormal(normalize(i.radial), t);
                float3 v = normalize(_WorldSpaceCameraPos.xyz - i.worldPosition);
                float3 l = normalize(_WorldSpaceLightPos0.xyz);
                float fresnel = pow(1-saturate(dot(n,v)),3.4);
                float diffuse = saturate(dot(n,l));
                float spec = pow(saturate(dot(n,normalize(l+v))),lerp(36,210,_Smoothness));
                half shadow = SHADOW_ATTENUATION(i);
                float shimmer = Wave(n,t*.72)*.5+.5;
                float foam = smoothstep(.77,.96,shimmer) * _FoamStrength * .22;
                fixed3 water = lerp(_DeepColor.rgb,_ShallowColor.rgb,
                    saturate(fresnel*.58+diffuse*.2+shimmer*.13));
                half3 ambient = max(ShadeSH9(half4(n,1)),.055h);
                fixed3 color = water*(ambient+_LightColor0.rgb*(.13+diffuse*.4)*shadow);
                color += _LightColor0.rgb*spec*(.25+_Smoothness*.75)*.4*shadow;
                color += _ShallowColor.rgb*fresnel*.24 + foam;
                float scan = SurfaceScanMask(i.worldPosition);
                color += _SurfaceScanColor.rgb * scan * .66;
                fixed4 result = fixed4(color,saturate(_Opacity+fresnel*(1-_Opacity)));
                UNITY_APPLY_FOG(i.fogCoord,result);
                return result;
            }
            ENDCG
        }
    }
    Fallback Off
}
