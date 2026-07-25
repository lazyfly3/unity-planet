Shader "CityGeneration/HolographicConstruction"
{
    Properties
    {
        [HDR] _HologramColor ("Hologram Color", Color) = (0.05, 0.9, 1, 1)
        [HDR] _SolidColor ("Constructed Surface", Color) = (0.72, 0.95, 1, 1)
        _RevealProgress ("Reveal Progress", Range(0, 1)) = 0
        _RevealMode ("Reveal Mode", Float) = 0
        _BuildMinY ("Build Minimum Y", Float) = 0
        _BuildMaxY ("Build Maximum Y", Float) = 1
        _BuildOrigin ("Build Origin", Vector) = (0, 0, 0, 0)
        _GridScale ("Grid Scale", Float) = 1.5
        _ScanWidth ("Scan Width", Float) = 1.2
        _SolidLag ("Solid Surface Lag", Range(0.02, 0.4)) = 0.16
        _WireWidth ("Wire Width", Range(0.25, 3)) = 1.15
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+20" }
        Cull Off
        ZWrite On

        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
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
                UNITY_FOG_COORDS(2)
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            UNITY_INSTANCING_BUFFER_START(ConstructionProperties)
                UNITY_DEFINE_INSTANCED_PROP(float, _RevealProgress)
                UNITY_DEFINE_INSTANCED_PROP(float, _RevealMode)
                UNITY_DEFINE_INSTANCED_PROP(float, _BuildMinY)
                UNITY_DEFINE_INSTANCED_PROP(float, _BuildMaxY)
                UNITY_DEFINE_INSTANCED_PROP(float4, _BuildOrigin)
            UNITY_INSTANCING_BUFFER_END(ConstructionProperties)

            fixed4 _HologramColor;
            fixed4 _SolidColor;
            float _GridScale;
            float _ScanWidth;
            float _SolidLag;

            float Hash31(float3 value)
            {
                value = frac(value * 0.1031);
                value += dot(value, value.yzx + 33.33);
                return frac((value.x + value.y) * value.z);
            }

            VertexToFragment Vert(AppData input)
            {
                VertexToFragment output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.position = UnityObjectToClipPos(input.vertex);
                output.worldPosition =
                    mul(unity_ObjectToWorld, input.vertex).xyz;
                output.worldNormal = UnityObjectToWorldNormal(input.normal);
                UNITY_TRANSFER_FOG(output, output.position);
                return output;
            }

            fixed4 Frag(VertexToFragment input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float progress = UNITY_ACCESS_INSTANCED_PROP(
                    ConstructionProperties,
                    _RevealProgress);
                float mode = UNITY_ACCESS_INSTANCED_PROP(
                    ConstructionProperties,
                    _RevealMode);
                float minimumY = UNITY_ACCESS_INSTANCED_PROP(
                    ConstructionProperties,
                    _BuildMinY);
                float maximumY = UNITY_ACCESS_INSTANCED_PROP(
                    ConstructionProperties,
                    _BuildMaxY);

                float solidProgress = saturate(
                    (progress - _SolidLag)
                    / max(0.01, 1 - _SolidLag));
                float scan = 0;
                if (mode < 0.5)
                {
                    float revealHeight = lerp(
                        minimumY - 0.05,
                        maximumY + 0.05,
                        solidProgress);
                    clip(revealHeight - input.worldPosition.y);
                    scan = 1 - saturate(
                        abs(input.worldPosition.y - revealHeight)
                        / max(0.01, _ScanWidth));
                }
                else
                {
                    float cell = Hash31(
                        floor(input.worldPosition * _GridScale));
                    clip(solidProgress - cell);
                    scan = saturate(
                        1 - abs(solidProgress - cell) * 7);
                }

                float3 gridPosition =
                    abs(frac(input.worldPosition * _GridScale) - 0.5);
                float grid = saturate(
                    1 - min(
                        min(gridPosition.x, gridPosition.y),
                        gridPosition.z) * 18);
                float fresnel = pow(
                    1 - saturate(dot(
                        normalize(_WorldSpaceCameraPos - input.worldPosition),
                        normalize(input.worldNormal))),
                    2);
                float pulse = 0.94
                    + 0.06 * sin(
                        _Time.y * 18
                        + input.worldPosition.y * 2.5);
                fixed3 color = _SolidColor.rgb
                    * (0.62 + fresnel * 0.22)
                    * pulse;
                color += _HologramColor.rgb * grid * 0.28;
                color += _HologramColor.rgb * scan * 2.4;
                fixed4 output = fixed4(color, 1);
                UNITY_APPLY_FOG(input.fogCoord, output);
                return output;
            }
            ENDCG
        }

        Pass
        {
            Blend One One
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma target 4.0
            #pragma vertex WireVert
            #pragma geometry WireGeom
            #pragma fragment WireFrag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct WireInput
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct WireVertex
            {
                float4 position : SV_POSITION;
                float3 worldPosition : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct WireFragment
            {
                float4 position : SV_POSITION;
                float3 worldPosition : TEXCOORD0;
                float3 barycentric : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            UNITY_INSTANCING_BUFFER_START(WireConstructionProperties)
                UNITY_DEFINE_INSTANCED_PROP(float, _RevealProgress)
                UNITY_DEFINE_INSTANCED_PROP(float, _RevealMode)
                UNITY_DEFINE_INSTANCED_PROP(float, _BuildMinY)
                UNITY_DEFINE_INSTANCED_PROP(float, _BuildMaxY)
            UNITY_INSTANCING_BUFFER_END(WireConstructionProperties)

            fixed4 _HologramColor;
            float _GridScale;
            float _SolidLag;
            float _WireWidth;

            float WireHash31(float3 value)
            {
                value = frac(value * 0.1031);
                value += dot(value, value.yzx + 33.33);
                return frac((value.x + value.y) * value.z);
            }

            WireVertex WireVert(WireInput input)
            {
                WireVertex output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.position = UnityObjectToClipPos(input.vertex);
                output.worldPosition =
                    mul(unity_ObjectToWorld, input.vertex).xyz;
                return output;
            }

            [maxvertexcount(3)]
            void WireGeom(
                triangle WireVertex input[3],
                inout TriangleStream<WireFragment> stream)
            {
                const float3 barycentrics[3] =
                {
                    float3(1, 0, 0),
                    float3(0, 1, 0),
                    float3(0, 0, 1)
                };

                for (int index = 0; index < 3; index++)
                {
                    WireFragment output;
                    UNITY_TRANSFER_INSTANCE_ID(input[index], output);
                    output.position = input[index].position;
                    output.worldPosition = input[index].worldPosition;
                    output.barycentric = barycentrics[index];
                    stream.Append(output);
                }
            }

            fixed4 WireFrag(WireFragment input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float progress = UNITY_ACCESS_INSTANCED_PROP(
                    WireConstructionProperties,
                    _RevealProgress);
                float mode = UNITY_ACCESS_INSTANCED_PROP(
                    WireConstructionProperties,
                    _RevealMode);
                float minimumY = UNITY_ACCESS_INSTANCED_PROP(
                    WireConstructionProperties,
                    _BuildMinY);
                float maximumY = UNITY_ACCESS_INSTANCED_PROP(
                    WireConstructionProperties,
                    _BuildMaxY);
                float solidProgress = saturate(
                    (progress - _SolidLag)
                    / max(0.01, 1 - _SolidLag));

                float leadingEdge = 0;
                if (mode < 0.5)
                {
                    float wireHeight = lerp(
                        minimumY - 0.05,
                        maximumY + 0.05,
                        progress);
                    float solidHeight = lerp(
                        minimumY - 0.05,
                        maximumY + 0.05,
                        solidProgress);
                    clip(wireHeight - input.worldPosition.y);
                    clip(input.worldPosition.y - solidHeight + 0.08);
                    leadingEdge = 1 - saturate(
                        abs(input.worldPosition.y - wireHeight)
                        / max(0.1, (maximumY - minimumY) * 0.08));
                }
                else
                {
                    float cell = WireHash31(
                        floor(input.worldPosition * _GridScale));
                    clip(progress - cell);
                    clip(cell - solidProgress + 0.04);
                    leadingEdge = saturate(
                        1 - abs(progress - cell) * 8);
                }

                float3 derivatives =
                    fwidth(input.barycentric) * _WireWidth;
                float3 edgeDistance = smoothstep(
                    float3(0, 0, 0),
                    derivatives,
                    input.barycentric);
                float wire = 1 - min(
                    min(edgeDistance.x, edgeDistance.y),
                    edgeDistance.z);
                clip(wire - 0.05);
                float intensity =
                    (0.8 + leadingEdge * 2.4)
                    * (0.94 + 0.06 * sin(_Time.y * 22));
                return fixed4(
                    _HologramColor.rgb * wire * intensity,
                    1);
            }
            ENDCG
        }
    }
    Fallback "Unlit/Color"
}
