Shader "VoxelPlanet/PlanetLabPlanarOcean"
{
    Properties
    {
        _DeepColor ("Deep Color", Color) = (0.01,0.09,0.2,0.8)
        _ShallowColor ("Shallow Color", Color) = (0.02,0.48,0.62,0.8)
        _WaveStrength ("Wave Strength", Range(0,1)) = 0.3
        _WaveScale ("Wave Scale", Float) = 0.06
        _WaveSpeed ("Wave Speed", Float) = 0.3
        _Opacity ("Opacity", Range(0,1)) = 0.72
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPosition : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };

            float4 _DeepColor;
            float4 _ShallowColor;
            float _WaveStrength;
            float _WaveScale;
            float _WaveSpeed;
            float _Opacity;

            v2f vert(appdata input)
            {
                v2f output;
                float4 vertex = input.vertex;
                float3 baseWorldPosition =
                    mul(unity_ObjectToWorld, input.vertex).xyz;
                float wave = sin(
                        (baseWorldPosition.x + _Time.y * _WaveSpeed * 18.0)
                        * _WaveScale)
                    + cos(
                        (baseWorldPosition.z - _Time.y * _WaveSpeed * 13.0)
                        * _WaveScale * 1.27);
                vertex.y += wave * _WaveStrength * 0.45;
                output.pos = UnityObjectToClipPos(vertex);
                output.worldPosition = mul(unity_ObjectToWorld, vertex).xyz;
                output.worldNormal = UnityObjectToWorldNormal(input.normal);
                output.uv = input.uv;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float viewBlend = saturate(1.0 - dot(
                    normalize(_WorldSpaceCameraPos.xyz - input.worldPosition),
                    normalize(input.worldNormal)));
                float3 color = lerp(_ShallowColor.rgb, _DeepColor.rgb, viewBlend);
                float diffuse = 0.35 + 0.65 * saturate(dot(
                    normalize(input.worldNormal),
                    normalize(_WorldSpaceLightPos0.xyz)));
                return fixed4(color * (_LightColor0.rgb * diffuse + 0.22), _Opacity);
            }
            ENDCG
        }
    }
    Fallback Off
}
