Shader "UnityPlanet/UrbanBuildingCut"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _Metallic ("Metallic", Range(0,1)) = 0
        _Glossiness ("Smoothness", Range(0,1)) = 0.35
        _MetallicGlossMap ("Metallic Gloss", 2D) = "black" {}
        _GlossMapScale ("Gloss Map Scale", Range(0,1)) = 0
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Float) = 1
        _OcclusionMap ("Occlusion", 2D) = "white" {}
        _OcclusionStrength ("Occlusion Strength", Range(0,1)) = 1
        _EmissionMap ("Emission", 2D) = "black" {}
        _EmissionColor ("Emission Color", Color) = (0,0,0,0)
        [HideInInspector] _CutPlane ("Cut Plane", Vector) = (0,1,0,0)
        [HideInInspector] _KeepAbove ("Keep Above", Float) = 1
        [HideInInspector] _CutSeed ("Cut Seed", Float) = 0
        [HideInInspector] _CutAmplitude ("Cut Amplitude", Float) = 2
        [HideInInspector] _CutLocalScale ("Cut Local Scale", Vector) = (1,1,1,1)
        [HideInInspector] _DamageMode ("Damage Mode", Float) = 0
        [HideInInspector] _DamageLocalSpace ("Damage Local Space", Float) = 0
        [HideInInspector] _DamageHoleCount ("Damage Hole Count", Float) = 0
        [HideInInspector] _DamageHole0 ("Damage Hole 0", Vector) = (0,0,0,0)
        [HideInInspector] _DamageHole1 ("Damage Hole 1", Vector) = (0,0,0,0)
        [HideInInspector] _DamageHole2 ("Damage Hole 2", Vector) = (0,0,0,0)
        [HideInInspector] _DamageHole3 ("Damage Hole 3", Vector) = (0,0,0,0)
        [HideInInspector] _DamageHole4 ("Damage Hole 4", Vector) = (0,0,0,0)
        [HideInInspector] _DamageHole5 ("Damage Hole 5", Vector) = (0,0,0,0)
        [HideInInspector] _DamageHole6 ("Damage Hole 6", Vector) = (0,0,0,0)
        [HideInInspector] _DamageHole7 ("Damage Hole 7", Vector) = (0,0,0,0)
        [HideInInspector] _DamageExtent0 ("Damage Extent 0", Vector) = (0,0,0,0)
        [HideInInspector] _DamageExtent1 ("Damage Extent 1", Vector) = (0,0,0,0)
        [HideInInspector] _DamageExtent2 ("Damage Extent 2", Vector) = (0,0,0,0)
        [HideInInspector] _DamageExtent3 ("Damage Extent 3", Vector) = (0,0,0,0)
        [HideInInspector] _DamageExtent4 ("Damage Extent 4", Vector) = (0,0,0,0)
        [HideInInspector] _DamageExtent5 ("Damage Extent 5", Vector) = (0,0,0,0)
        [HideInInspector] _DamageExtent6 ("Damage Extent 6", Vector) = (0,0,0,0)
        [HideInInspector] _DamageExtent7 ("Damage Extent 7", Vector) = (0,0,0,0)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 300
        Cull Back

        Pass
        {
        Tags { "LightMode"="ForwardBase" }

        CGPROGRAM
        #pragma vertex vert
        #pragma fragment frag
        #pragma multi_compile_fwdbase
        #pragma target 3.0

        #include "UnityCG.cginc"
        #include "Lighting.cginc"

        sampler2D _MainTex;
        sampler2D _BumpMap;
        sampler2D _MetallicGlossMap;
        sampler2D _OcclusionMap;
        sampler2D _EmissionMap;
        float4 _MainTex_ST;
        float4 _BumpMap_ST;
        float4 _EmissionMap_ST;
        fixed4 _Color;
        half _Metallic;
        half _Glossiness;
        half _GlossMapScale;
        half _BumpScale;
        half _OcclusionStrength;
        fixed4 _EmissionColor;
        float4 _CutPlane;
        float4 _CutLocalScale;
        float _KeepAbove;
        float _CutSeed;
        float _CutAmplitude;
        float _DamageMode;
        float _DamageLocalSpace;
        float _DamageHoleCount;
        float4 _DamageHole0, _DamageHole1, _DamageHole2, _DamageHole3;
        float4 _DamageHole4, _DamageHole5, _DamageHole6, _DamageHole7;
        float4 _DamageExtent0, _DamageExtent1, _DamageExtent2, _DamageExtent3;
        float4 _DamageExtent4, _DamageExtent5, _DamageExtent6, _DamageExtent7;

        struct AppData
        {
            float4 vertex : POSITION;
            float3 normal : NORMAL;
            float2 uv : TEXCOORD0;
        };

        struct Interpolators
        {
            float4 position : SV_POSITION;
            float2 uv : TEXCOORD0;
            float3 localPosition : TEXCOORD1;
            float3 worldPosition : TEXCOORD2;
            float3 worldNormal : TEXCOORD3;
        };

        Interpolators vert(AppData input)
        {
            Interpolators output;
            output.position = UnityObjectToClipPos(input.vertex);
            output.uv = TRANSFORM_TEX(input.uv, _MainTex);
            output.localPosition = input.vertex.xyz;
            output.worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
            output.worldNormal = UnityObjectToWorldNormal(input.normal);
            return output;
        }

        float Hash21(float2 value)
        {
            value = frac(value * float2(123.34, 456.21));
            value += dot(value, value + 45.32 + _CutSeed * 0.013);
            return frac(value.x * value.y);
        }

        float DamageBoxDistance(float3 worldPosition, float4 hole, float4 extent)
        {
            float3 safeExtent = max(extent.xyz, 0.01);
            float rounding = min(safeExtent.x,
                             min(safeExtent.y, safeExtent.z)) * 0.16;
            float3 delta = abs(worldPosition - hole.xyz) -
                           max(safeExtent - rounding, 0.01);
            return length(max(delta, 0.0)) +
                   min(max(delta.x, max(delta.y, delta.z)), 0.0) -
                   rounding;
        }

        fixed4 frag(Interpolators input) : SV_Target
        {
            float2 pattern = input.localPosition.xz *
                             max(abs(_CutLocalScale.xz), float2(0.01, 0.01));
            float coarse = Hash21(floor(pattern * 0.18) + _CutSeed);
            float fine = Hash21(floor(pattern * 0.47) - _CutSeed * 0.37);
            float jaggedEdge = ((coarse - 0.5) * 0.72 +
                                (fine - 0.5) * 0.28) * _CutAmplitude;
            if (_DamageMode > 0.5)
            {
                float3 damagePosition = _DamageLocalSpace > 0.5
                    ? input.localPosition
                    : input.worldPosition;
                float damageDistance = 100000.0;
                if (_DamageHoleCount > 0.5) damageDistance = min(damageDistance,
                    DamageBoxDistance(damagePosition, _DamageHole0, _DamageExtent0));
                if (_DamageHoleCount > 1.5) damageDistance = min(damageDistance,
                    DamageBoxDistance(damagePosition, _DamageHole1, _DamageExtent1));
                if (_DamageHoleCount > 2.5) damageDistance = min(damageDistance,
                    DamageBoxDistance(damagePosition, _DamageHole2, _DamageExtent2));
                if (_DamageHoleCount > 3.5) damageDistance = min(damageDistance,
                    DamageBoxDistance(damagePosition, _DamageHole3, _DamageExtent3));
                if (_DamageHoleCount > 4.5) damageDistance = min(damageDistance,
                    DamageBoxDistance(damagePosition, _DamageHole4, _DamageExtent4));
                if (_DamageHoleCount > 5.5) damageDistance = min(damageDistance,
                    DamageBoxDistance(damagePosition, _DamageHole5, _DamageExtent5));
                if (_DamageHoleCount > 6.5) damageDistance = min(damageDistance,
                    DamageBoxDistance(damagePosition, _DamageHole6, _DamageExtent6));
                if (_DamageHoleCount > 7.5) damageDistance = min(damageDistance,
                    DamageBoxDistance(damagePosition, _DamageHole7, _DamageExtent7));
                float chipXY = Hash21(floor(damagePosition.xy * 0.27) +
                                      _CutSeed);
                float chipYZ = Hash21(floor(damagePosition.yz * 0.29) -
                                      _CutSeed * 0.37);
                float chipXZ = Hash21(floor(damagePosition.xz * 0.23) +
                                      _CutSeed * 0.61);
                float chippedEdge = (chipXY * 0.42 + chipYZ * 0.36 +
                                     chipXZ * 0.22 - 0.5) * 0.78;
                if (_DamageMode > 1.5)
                    clip(-damageDistance - chippedEdge * 0.32);
                else
                    clip(damageDistance + chippedEdge);
            }
            else
            {
                float planeDistance = dot(_CutPlane.xyz, input.localPosition) +
                                      _CutPlane.w - jaggedEdge;
                clip(planeDistance * _KeepAbove);
            }

            fixed4 albedo = tex2D(_MainTex, input.uv) * _Color;
            half occlusion = tex2D(_OcclusionMap, input.uv).g;
            float3 normal = normalize(input.worldNormal);
            float3 ambient = ShadeSH9(float4(normal, 1.0));
            float diffuse = saturate(dot(normal, _WorldSpaceLightPos0.xyz));
            float3 lighting = ambient + _LightColor0.rgb * diffuse;
            float3 emission = tex2D(_EmissionMap, input.uv).rgb *
                              _EmissionColor.rgb * 0.35;
            float ao = lerp(1.0, occlusion, _OcclusionStrength);
            return fixed4(albedo.rgb * lighting * ao + emission, albedo.a);
        }
        ENDCG
        }
    }
    FallBack "Diffuse"
}
