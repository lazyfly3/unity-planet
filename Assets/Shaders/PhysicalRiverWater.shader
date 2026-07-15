Shader "Voxel Planet/Water/Physical River Water"
{
    Properties
    {
        _NormalMap ("Ripple Normal", 2D) = "bump" {}
        _FlowMap ("Flow Distortion", 2D) = "gray" {}
        _FoamTex ("Foam", 2D) = "white" {}
        _ShallowColor ("Shallow Color", Color) = (0.05, 0.65, 0.72, 0.58)
        _DeepColor ("Deep Color", Color) = (0.005, 0.1, 0.3, 0.82)
        _NormalStrength ("Normal Strength", Range(0, 2)) = 0.75
        _FlowSpeed ("Flow Speed", Range(0, 2)) = 0.22
        _Refraction ("Refraction", Range(0, 0.05)) = 0.012
        _DepthDistance ("Depth Distance", Range(0.1, 8)) = 3
        _FoamDistance ("Foam Distance", Range(0.01, 2)) = 0.45
        _FoamStrength ("Foam Strength", Range(0, 1)) = 0.65
        _Smoothness ("Smoothness", Range(0, 1)) = 0.88
    }

    SubShader
    {
        Tags { "Queue"="Transparent+20" "RenderType"="Transparent" "IgnoreProjector"="True" }
        GrabPass { "_PlanetRiverGrab" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            sampler2D _NormalMap;
            sampler2D _FlowMap;
            sampler2D _FoamTex;
            sampler2D _PlanetRiverGrab;
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            float4 _NormalMap_ST;
            float4 _FlowMap_ST;
            float4 _FoamTex_ST;
            fixed4 _ShallowColor;
            fixed4 _DeepColor;
            float _NormalStrength;
            float _FlowSpeed;
            float _Refraction;
            float _DepthDistance;
            float _FoamDistance;
            float _FoamStrength;
            float _Smoothness;

            struct AppData
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                float4 screenPos : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float3 worldNormal : TEXCOORD2;
                float2 uv : TEXCOORD3;
                fixed4 color : COLOR;
            };

            Varyings Vert(AppData input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.screenPos = ComputeGrabScreenPos(output.position);
                output.worldPos = mul(unity_ObjectToWorld, input.vertex).xyz;
                output.worldNormal = UnityObjectToWorldNormal(input.normal);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                float flowRate = _FlowSpeed * max(0.25, input.color.r);
                float2 flowNoise = tex2D(_FlowMap, input.uv * _FlowMap_ST.xy + _Time.y * float2(0.015, flowRate * 0.12)).rg * 2 - 1;
                float2 uvA = input.uv * _NormalMap_ST.xy + float2(flowNoise.x, -_Time.y * flowRate);
                float2 uvB = input.uv.yx * (_NormalMap_ST.xy * 0.72) + float2(_Time.y * flowRate * 0.63, flowNoise.y);
                float3 normalA = UnpackNormal(tex2D(_NormalMap, uvA));
                float3 normalB = UnpackNormal(tex2D(_NormalMap, uvB));
                float2 ripple = (normalA.xy + normalB.xy) * 0.5 * _NormalStrength;

                float3 geometricNormal = normalize(input.worldNormal);
                float3 tangent = normalize(cross(abs(geometricNormal.y) < 0.95 ? float3(0,1,0) : float3(1,0,0), geometricNormal));
                float3 bitangent = normalize(cross(geometricNormal, tangent));
                float3 worldNormal = normalize(geometricNormal + tangent * ripple.x + bitangent * ripple.y);
                float3 viewDirection = normalize(_WorldSpaceCameraPos - input.worldPos);

                float2 screenUv = input.screenPos.xy / input.screenPos.w;
                float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, screenUv);
                float sceneDepth = LinearEyeDepth(rawDepth);
                float waterDepth = max(0, sceneDepth - input.screenPos.w);
                float depthBlend = saturate(waterDepth / _DepthDistance);

                float4 refractPos = input.screenPos;
                refractPos.xy += ripple * _Refraction * input.screenPos.w;
                fixed3 refracted = tex2Dproj(_PlanetRiverGrab, UNITY_PROJ_COORD(refractPos)).rgb;
                fixed4 waterColor = lerp(_ShallowColor, _DeepColor, depthBlend);
                float fresnel = pow(1 - saturate(dot(viewDirection, worldNormal)), 4);
                float3 lightDirection = normalize(_WorldSpaceLightPos0.xyz);
                float3 halfDirection = normalize(lightDirection + viewDirection);
                float specular = pow(saturate(dot(worldNormal, halfDirection)), lerp(24, 180, _Smoothness));

                float shoreline = 1 - saturate(waterDepth / _FoamDistance);
                float foamTexture = tex2D(_FoamTex, input.uv * _FoamTex_ST.xy + float2(0, -_Time.y * flowRate * 0.4)).r;
                float foam = shoreline * smoothstep(0.35, 0.75, foamTexture) * _FoamStrength;
                float3 baseColor = lerp(refracted, waterColor.rgb, waterColor.a);
                baseColor = lerp(baseColor, _LightColor0.rgb, fresnel * 0.2 + specular * 0.55);
                baseColor = lerp(baseColor, float3(0.82, 0.96, 1), foam);
                float alpha = saturate(lerp(_ShallowColor.a, _DeepColor.a, depthBlend) + fresnel * 0.16 + foam * 0.45);
                return fixed4(baseColor, alpha);
            }
            ENDCG
        }
    }
    Fallback Off
}
