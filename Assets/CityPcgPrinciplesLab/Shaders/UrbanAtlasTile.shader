Shader "UnityPlanet/CityPCG/UrbanAtlasTile"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _MainTex ("NewGen Urban Atlas", 2D) = "white" {}
        _AtlasRect ("Atlas Rect XYWH", Vector) = (0,0,1,1)
        _WorldTileSize ("World Tile Size", Float) = 8
        _Metallic ("Metallic", Range(0,1)) = 0
        _Glossiness ("Smoothness", Range(0,1)) = 0.2
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 150

        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            sampler2D _MainTex;
            fixed4 _Color;
            float4 _AtlasRect;
            float _WorldTileSize;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float3 worldPosition : TEXCOORD0;
                half3 worldNormal : TEXCOORD1;
                UNITY_FOG_COORDS(2)
            };

            v2f vert(appdata input)
            {
                v2f output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                output.worldNormal = UnityObjectToWorldNormal(input.normal);
                UNITY_TRANSFER_FOG(output, output.position);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float2 repeated = frac(
                    input.worldPosition.xz / max(0.25, _WorldTileSize));
                float2 atlasUv = _AtlasRect.xy + repeated * _AtlasRect.zw;
                fixed4 result = tex2D(_MainTex, atlasUv) * _Color;
                half3 normal = normalize(input.worldNormal);
                half3 lightDirection = normalize(_WorldSpaceLightPos0.xyz);
                half lighting = 0.42h + 0.58h * saturate(
                    dot(normal, lightDirection));
                result.rgb *= lighting;
                UNITY_APPLY_FOG(input.fogCoord, result);
                return result;
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}
