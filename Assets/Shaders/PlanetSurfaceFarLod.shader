Shader "VoxelPlanet/SurfaceFarLod"
{
    SubShader
    {
        Tags { "Queue"="Geometry-10" "RenderType"="Opaque" }
        Cull Back
        ZWrite On

        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float3 worldPosition : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float4 color : COLOR;
            };

            float3 _PlanetCenter;
            float3 _HideCenter;
            float _HideRadius;
            float _RadialInset;

            v2f vert(appdata input)
            {
                v2f output;
                float3 world = mul(unity_ObjectToWorld, input.vertex).xyz;
                float3 radial = normalize(world - _PlanetCenter);
                world -= radial * _RadialInset;
                output.position = mul(UNITY_MATRIX_VP, float4(world, 1.0));
                output.worldPosition = world;
                output.worldNormal = UnityObjectToWorldNormal(input.normal);
                output.color = input.color;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                clip(distance(input.worldPosition, _HideCenter) - _HideRadius);
                float3 normal = normalize(input.worldNormal);
                float diffuse = saturate(dot(normal, normalize(_WorldSpaceLightPos0.xyz)));
                float lighting = 0.2 + diffuse * 0.8;
                return fixed4(input.color.rgb * _LightColor0.rgb * lighting, 1.0);
            }
            ENDCG
        }
    }
}
