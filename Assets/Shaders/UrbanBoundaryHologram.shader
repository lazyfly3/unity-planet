Shader "UnityPlanet/Urban Boundary Hologram"
{
    Properties
    {
        _MainTex ("Warning Graphic", 2D) = "black" {}
        [HDR] _Tint ("Hologram Tint", Color) = (1.0, 0.20, 0.08, 0.92)
        _Intensity ("Emission Intensity", Range(0.0, 8.0)) = 2.4
        _FadeNear ("Fully Visible Distance", Float) = 72.0
        _FadeFar ("Hidden Distance", Float) = 260.0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent+40"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }
        LOD 100
        Cull Off
        ZWrite Off
        Blend SrcAlpha One
        ColorMask RGB

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Tint;
            float _Intensity;
            float _FadeNear;
            float _FadeFar;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPosition : TEXCOORD1;
            };

            v2f vert(appdata input)
            {
                v2f output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.worldPosition = mul(
                    unity_ObjectToWorld,
                    input.vertex).xyz;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                fixed4 graphic = tex2D(_MainTex, input.uv);
                float distanceToCamera = distance(
                    _WorldSpaceCameraPos,
                    input.worldPosition);
                float proximity = 1.0 - smoothstep(
                    _FadeNear,
                    max(_FadeNear + 0.01, _FadeFar),
                    distanceToCamera);
                float signal = max(
                    graphic.r,
                    max(graphic.g, graphic.b));
                float scanPulse = 0.88 + 0.12 * sin(
                    _Time.y * 4.6 + input.worldPosition.y * 0.075);
                float alpha = saturate(
                    signal * proximity * _Tint.a * scanPulse);
                float3 color = graphic.rgb * _Tint.rgb * _Intensity;
                return fixed4(color, alpha);
            }
            ENDCG
        }
    }
    Fallback Off
}
