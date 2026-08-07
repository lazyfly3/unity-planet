Shader "Voxel Planet/Skybox/Cosmic Panoramic"
{
    Properties
    {
        _MainTex ("Panorama", 2D) = "black" {}
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Exposure ("Exposure", Range(0, 4)) = 1
        _Rotation ("Rotation", Range(0, 360)) = 0
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off
        ZWrite Off
        ZTest LEqual

        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            half4 _Tint;
            half _Exposure;
            float _Rotation;

            struct AppData
            {
                float4 vertex : POSITION;
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                float3 direction : TEXCOORD0;
            };

            Varyings Vert(AppData input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.vertex);
                #if defined(UNITY_REVERSED_Z)
                    output.position.z = 0.0;
                #else
                    output.position.z = output.position.w;
                #endif
                output.direction = input.vertex.xyz;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                const float InvTwoPi = 0.15915494309;
                const float InvPi = 0.31830988618;
                float3 direction = normalize(input.direction);
                float2 uv;
                uv.x = atan2(direction.x, direction.z) * InvTwoPi + 0.5 + _Rotation / 360.0;
                uv.y = asin(clamp(direction.y, -1.0, 1.0)) * InvPi + 0.5;
                uv = uv * _MainTex_ST.xy + _MainTex_ST.zw;
                half4 color = tex2D(_MainTex, uv) * _Tint * _Exposure;
                color.rgb = max(color.rgb, 0.001);
                return color;
            }
            ENDCG
        }
    }
}
