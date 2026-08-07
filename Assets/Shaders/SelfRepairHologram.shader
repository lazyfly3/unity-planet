Shader "ModularAssembly/SelfRepairHologram"
{
    Properties
    {
        [HDR] _HologramColor ("Hologram Color", Color) = (0.02, 0.72, 2.4, 1)
        [HDR] _SolidColor ("Hologram Surface", Color) = (0.02, 0.42, 1.4, 0.52)
        _RevealProgress ("Reveal Progress", Range(0, 1)) = 0
        _RevealMode ("Reveal Mode", Float) = 0
        _BuildMinY ("Build Minimum Y", Float) = 0
        _BuildMaxY ("Build Maximum Y", Float) = 1
        _BuildOrigin ("Build Origin", Vector) = (0, 0, 0, 0)
        _GridScale ("Grid Scale", Float) = 3.2
        _ScanWidth ("Scan Width", Float) = 0.12
        _WireWidth ("Edge Strength", Range(0.25, 3)) = 2.25
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent+60"
            "IgnoreProjector"="True"
        }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct AppData
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct VertexToFragment
            {
                float4 position : SV_POSITION;
                float3 worldPosition : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            UNITY_INSTANCING_BUFFER_START(SelfRepairProperties)
                UNITY_DEFINE_INSTANCED_PROP(float, _RevealProgress)
                UNITY_DEFINE_INSTANCED_PROP(float, _RevealMode)
                UNITY_DEFINE_INSTANCED_PROP(float, _BuildMinY)
                UNITY_DEFINE_INSTANCED_PROP(float, _BuildMaxY)
                UNITY_DEFINE_INSTANCED_PROP(float4, _BuildOrigin)
            UNITY_INSTANCING_BUFFER_END(SelfRepairProperties)

            fixed4 _HologramColor;
            fixed4 _SolidColor;
            float _GridScale;
            float _ScanWidth;
            float _WireWidth;

            VertexToFragment Vert(AppData input)
            {
                VertexToFragment output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.position = UnityObjectToClipPos(input.vertex);
                output.worldPosition =
                    mul(unity_ObjectToWorld, input.vertex).xyz;
                output.worldNormal = UnityObjectToWorldNormal(input.normal);
                return output;
            }

            fixed4 Frag(VertexToFragment input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float progress = saturate(UNITY_ACCESS_INSTANCED_PROP(
                    SelfRepairProperties,
                    _RevealProgress));
                float minimumY = UNITY_ACCESS_INSTANCED_PROP(
                    SelfRepairProperties,
                    _BuildMinY);
                float maximumY = UNITY_ACCESS_INSTANCED_PROP(
                    SelfRepairProperties,
                    _BuildMaxY);
                float heightRange = max(0.01, maximumY - minimumY);
                float revealHeight = lerp(
                    minimumY - 0.015,
                    maximumY + 0.015,
                    progress);
                clip(revealHeight - input.worldPosition.y);

                float scan = 1 - saturate(
                    abs(input.worldPosition.y - revealHeight)
                    / max(0.015, _ScanWidth));
                float3 gridPosition =
                    abs(frac(input.worldPosition * _GridScale) - 0.5);
                float grid = saturate(
                    1 - min(
                        min(gridPosition.x, gridPosition.y),
                        gridPosition.z) * 15);
                float3 viewDirection = normalize(
                    _WorldSpaceCameraPos - input.worldPosition);
                float fresnel = pow(
                    1 - saturate(dot(
                        viewDirection,
                        normalize(input.worldNormal))),
                    1.65);
                float lowerFade = saturate(
                    (input.worldPosition.y - minimumY) /
                    max(0.04, heightRange * 0.08));
                float surfaceAlpha = _SolidColor.a *
                    (0.72 + fresnel * 0.62 + grid * 0.35) * lowerFade;
                float3 color = _SolidColor.rgb *
                    (0.78 + fresnel * 0.82);
                color += _HologramColor.rgb *
                    (grid * 0.46 * _WireWidth + scan * 1.7);
                float alpha = saturate(
                    surfaceAlpha + grid * 0.2 + scan * 0.46);
                return fixed4(color, alpha);
            }
            ENDCG
        }
    }
    Fallback "Unlit/Transparent"
}
