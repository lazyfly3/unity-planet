Shader "Voxel Planet/Weather/Cloud Shell"
{
    Properties
    {
        _MainTex ("Cloud Noise", 2D) = "white" {}
        _CloudColor ("Cloud Color", Color) = (0.7,0.8,0.9,1)
        _Coverage ("Coverage", Range(0,1)) = 0.5
        _Softness ("Softness", Range(0.01,0.5)) = 0.16
        _Opacity ("Opacity", Range(0,1)) = 0.72
        _ScrollSpeed ("Scroll Speed", Float) = 0.008
    }
    SubShader
    {
        Tags { "Queue"="Transparent+5" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Front
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _CloudColor;
            float _Coverage;
            float _Softness;
            float _Opacity;
            float _ScrollSpeed;
            float4 _Wind;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float3 direction : TEXCOORD0; };

            v2f vert(appdata input)
            {
                v2f output;
                output.pos = UnityObjectToClipPos(input.vertex);
                output.direction = normalize(input.normal);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float3 direction = normalize(input.direction);
                float2 uv = float2(atan2(direction.z, direction.x) / 6.2831853 + 0.5,
                    asin(direction.y) / 3.1415926 + 0.5);
                float windPhase = _Time.y * _ScrollSpeed * max(0.25, _Wind.w);
                float noiseA = tex2D(_MainTex, uv * 2.2 + float2(windPhase, windPhase * 0.31)).r;
                float noiseB = tex2D(_MainTex, uv.yx * 4.7 - float2(windPhase * 0.37, windPhase * 0.19)).r;
                float density = noiseA * 0.72 + noiseB * 0.28;
                float threshold = 1.02 - _Coverage;
                float alpha = smoothstep(threshold - _Softness, threshold + _Softness, density)
                    * _Opacity * saturate(_Coverage * 1.6);
                return fixed4(_CloudColor.rgb, alpha);
            }
            ENDCG
        }
    }
    Fallback Off
}
