Shader "VoxelPlanet/ProceduralOceanSurface"
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
        GrabPass { "_ProceduralOceanGrab" }
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

            sampler2D _ProceduralOceanGrab;
            sampler2D_float _CameraDepthTexture;
            fixed4 _DeepColor, _ShallowColor;
            half _Smoothness, _WaveStrength, _NormalStrength, _FoamStrength;
            half _RefractionStrength, _Opacity;
            float _WaveScale, _WaveSpeed, _WaveHeight, _OceanRadius;
            float3 _PlanetCenter;
            float3 _SurfaceScanOrigin;
            float _SurfaceScanRadius;
            float _SurfaceScanWidth;
            float _SurfaceScanStrength;
            fixed4 _SurfaceScanColor;

            struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; };
            struct v2f
            {
                float4 pos:SV_POSITION;
                float4 screenPos:TEXCOORD0;
                float4 grabPos:TEXCOORD1;
                float3 worldPosition:TEXCOORD2;
                float3 radial:TEXCOORD3;
                SHADOW_COORDS(4)
                UNITY_FOG_COORDS(5)
            };

            float Wave(float3 radial,float time)
            {
                float scale=lerp(7,28,saturate(_WaveScale/3));
                float3 p=radial*scale;
                float broad=sin(dot(p,float3(.83,.21,.48))+time)
                    +sin(dot(p,float3(-.34,.91,.27))-time*1.17)
                    +sin(dot(p,float3(.24,-.39,.89))+time*.73);
                float fine=sin(dot(p,float3(1.7,.6,-1.1))-time*1.71)
                    *sin(dot(p,float3(-.8,1.5,.7))+time*1.29);
                return (broad*.19+fine*.25)*_WaveStrength;
            }

            float3 RippleNormal(float3 radial,float time)
            {
                float3 axis=abs(radial.y)<.9?float3(0,1,0):float3(1,0,0);
                float3 tangent=normalize(cross(axis,radial));
                float3 bitangent=cross(radial,tangent);
                float frequency=lerp(42,130,saturate(_WaveScale/3));
                float a=dot(radial,float3(.73,.37,.57))*frequency+time*2.1;
                float b=dot(radial,float3(-.31,.91,.24))*frequency*.73-time*1.6;
                float2 slope=float2(cos(a)+.55*cos(b),sin(b)+.45*sin(a*1.31));
                return normalize(radial+(tangent*slope.x+bitangent*slope.y)*.055*_NormalStrength);
            }

            float SurfaceScanMask(float3 worldPosition)
            {
                if(_SurfaceScanStrength<=.001)
                    return 0;
                float3 originDirection=normalize(_SurfaceScanOrigin-_PlanetCenter);
                float3 pixelDirection=normalize(worldPosition-_PlanetCenter);
                float cosine=clamp(dot(originDirection,pixelDirection),-1,1);
                float surfaceDistance=acos(cosine)*max(1,_OceanRadius);
                float width=max(.25,_SurfaceScanWidth);
                float band=1-smoothstep(
                    width,
                    width*1.8,
                    abs(surfaceDistance-_SurfaceScanRadius));
                return band*saturate(_SurfaceScanStrength);
            }

            v2f vert(appdata input)
            {
                v2f o;
                float3 world=mul(unity_ObjectToWorld,input.vertex).xyz;
                float3 radial=normalize(world-_PlanetCenter);
                world+=radial*Wave(radial,_Time.y*_WaveSpeed)*_WaveHeight;
                o.pos=mul(UNITY_MATRIX_VP,float4(world,1));
                o.screenPos=ComputeScreenPos(o.pos);
                o.grabPos=ComputeGrabScreenPos(o.pos);
                o.worldPosition=world;
                o.radial=radial;
                TRANSFER_SHADOW(o);
                UNITY_TRANSFER_FOG(o,o.pos);
                return o;
            }

            fixed4 frag(v2f i):SV_Target
            {
                float t=_Time.y*_WaveSpeed;
                float3 n=RippleNormal(normalize(i.radial),t);
                float3 v=normalize(_WorldSpaceCameraPos.xyz-i.worldPosition);
                float3 l=normalize(_WorldSpaceLightPos0.xyz);
                float2 screenUv=i.screenPos.xy/i.screenPos.w;
                float sceneEye=LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture,screenUv));
                float waterEye=i.screenPos.w;
                float thickness=max(0,sceneEye-waterEye);
                float shore=1-saturate(thickness/max(1.5,_WaveHeight*8+2));
                float fresnel=pow(1-saturate(dot(n,v)),3.5);
                float2 distortion=n.xz*_RefractionStrength*saturate(thickness*.15);
                float4 grab=i.grabPos;
                grab.xy+=distortion*grab.w;
                fixed3 refracted=tex2Dproj(_ProceduralOceanGrab,UNITY_PROJ_COORD(grab)).rgb;
                float depthBlend=saturate(thickness/18);
                fixed3 water=lerp(_ShallowColor.rgb,_DeepColor.rgb,depthBlend);
                float diffuse=saturate(dot(n,l));
                float spec=pow(saturate(dot(n,normalize(l+v))),lerp(42,230,_Smoothness));
                float foamNoise=Wave(n,t*.82)*.5+.5;
                float foam=shore*smoothstep(.28,.72,foamNoise)*_FoamStrength;
                half shadow=SHADOW_ATTENUATION(i);
                half3 ambient=max(ShadeSH9(half4(n,1)),.06h);
                fixed3 lit=water*(ambient+_LightColor0.rgb*(.12+diffuse*.38)*shadow);
                fixed3 color=lerp(refracted,lit,saturate(_Opacity+depthBlend*.12));
                color+=_LightColor0.rgb*spec*(.28+_Smoothness*.72)*.45*shadow;
                color+=_ShallowColor.rgb*fresnel*.25;
                color=lerp(color,fixed3(.82,.94,.92),foam*.75);
                float scan=SurfaceScanMask(i.worldPosition);
                color+=_SurfaceScanColor.rgb*scan*.72;
                fixed4 result=fixed4(color,saturate(_Opacity+fresnel*(1-_Opacity)+foam*.12));
                UNITY_APPLY_FOG(i.fogCoord,result);
                return result;
            }
            ENDCG
        }

        // A dedicated inward-facing pass makes the animated water surface
        // visible as a ceiling while the camera is below mean sea level.
        // It is clipped entirely for cameras outside the ocean shell.
        Pass
        {
            Tags { "LightMode"="Always" }
            Cull Front
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex UnderwaterVert
            #pragma fragment UnderwaterFrag
            #include "UnityCG.cginc"

            fixed4 _DeepColor;
            fixed4 _ShallowColor;
            half _WaveStrength;
            half _NormalStrength;
            half _Opacity;
            float _WaveScale;
            float _WaveSpeed;
            float _WaveHeight;
            float _OceanRadius;
            float3 _PlanetCenter;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPosition : TEXCOORD0;
                float3 radial : TEXCOORD1;
            };

            float UnderwaterWave(float3 radial, float time)
            {
                float scale = lerp(7.0, 28.0, saturate(_WaveScale / 3.0));
                float3 p = radial * scale;
                float broad =
                    sin(dot(p, float3(.83, .21, .48)) + time)
                    + sin(dot(p, float3(-.34, .91, .27)) - time * 1.17)
                    + sin(dot(p, float3(.24, -.39, .89)) + time * .73);
                float fine =
                    sin(dot(p, float3(1.7, .6, -1.1)) - time * 1.71)
                    * sin(dot(p, float3(-.8, 1.5, .7)) + time * 1.29);
                return (broad * .19 + fine * .25) * _WaveStrength;
            }

            v2f UnderwaterVert(appdata input)
            {
                v2f output;
                float3 world = mul(unity_ObjectToWorld, input.vertex).xyz;
                float3 radial = normalize(world - _PlanetCenter);
                world += radial
                    * UnderwaterWave(radial, _Time.y * _WaveSpeed)
                    * _WaveHeight;
                output.pos = mul(UNITY_MATRIX_VP, float4(world, 1));
                output.worldPosition = world;
                output.radial = radial;
                return output;
            }

            fixed4 UnderwaterFrag(v2f input) : SV_Target
            {
                float cameraRadius =
                    distance(_WorldSpaceCameraPos.xyz, _PlanetCenter);
                clip(
                    _OceanRadius
                    + max(0.1, abs(_WaveHeight))
                    + 0.75
                    - cameraRadius);

                float3 viewDirection =
                    normalize(_WorldSpaceCameraPos.xyz - input.worldPosition);
                float facing =
                    saturate(dot(-normalize(input.radial), viewDirection));
                float fresnel = pow(1.0 - facing, 2.2);
                float time = _Time.y * max(0.1, _WaveSpeed);
                float3 p = normalize(input.radial)
                    * lerp(42.0, 96.0, saturate(_WaveScale / 3.0));
                float caustic =
                    pow(
                        saturate(
                            1.0
                            - abs(
                                sin(dot(p, float3(.71, .23, .64)) + time * 1.3)
                                - sin(dot(p, float3(-.36, .87, .31)) - time))),
                        5.0);
                fixed3 ceilingColor =
                    lerp(_DeepColor.rgb, _ShallowColor.rgb, 0.62 + fresnel * 0.3);
                ceilingColor +=
                    lerp(_ShallowColor.rgb, fixed3(.45, 1.0, 1.0), .5)
                    * caustic
                    * .28;
                ceilingColor *= lerp(.62, 1.12, facing);
                return fixed4(
                    ceilingColor,
                    saturate(max(.68, _Opacity) + fresnel * .16));
            }
            ENDCG
        }
    }
    Fallback "VoxelPlanet/ProceduralOcean"
}
