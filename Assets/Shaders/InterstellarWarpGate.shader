Shader "VoxelPlanet/InterstellarWarpGate"
{
    Properties
    {
        _MainTex ("Exit View", 2D) = "black" {}
        _EdgeColor ("Energy Edge", Color) = (0.12,0.82,1,1)
        _CoreColor ("Energy Core", Color) = (0.65,0.95,1,1)
        _Open ("Open", Range(0,1)) = 0
        _Pulse ("Pulse", Range(0,2)) = 0
        _UseTexture ("Use Exit View", Float) = 1
        _RimOnly ("Rim Only", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent+80"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
        }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _EdgeColor;
            fixed4 _CoreColor;
            float _Open;
            float _Pulse;
            float _UseTexture;
            float _RimOnly;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata input)
            {
                v2f output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float2 centered = (input.uv - 0.5) * 2.0;
                float radius = length(centered);
                clip(1.0 - radius);

                float angle = atan2(centered.y, centered.x);
                float flow = sin(angle * 11.0 - _Time.y * 5.5)
                    * 0.5 + sin(angle * 23.0 + _Time.y * 8.0) * 0.5;
                float rim = smoothstep(0.70, 0.86, radius)
                    * (1.0 - smoothstep(0.965, 1.0, radius));
                rim *= saturate(0.82 + flow * 0.2 + _Pulse * 0.25);

                float2 distortion = normalize(centered + 0.0001)
                    * sin(radius * 28.0 - _Time.y * 4.0)
                    * 0.004 * _Open;
                fixed4 exitView = tex2D(_MainTex, input.uv + distortion);
                float aperture = 1.0 - smoothstep(
                    max(0.0, _Open - 0.12),
                    max(0.001, _Open),
                    radius);
                float interiorAlpha = aperture * saturate(_Open * 1.35);
                fixed3 interior = exitView.rgb * lerp(0.35, 1.0, _UseTexture);
                interior = lerp(_CoreColor.rgb * 0.35, interior, _UseTexture);

                fixed3 color = interior * interiorAlpha
                    + _EdgeColor.rgb * rim * (1.15 + _Pulse * 0.45)
                    + _CoreColor.rgb * rim * rim * 0.65;
                float alpha = _RimOnly > 0.5
                    ? rim * _Open
                    : saturate(interiorAlpha + rim);
                return fixed4(color, alpha);
            }
            ENDCG
        }
    }
    Fallback Off
}
