Shader "Hidden/UnityPlanet/EnemySilhouetteOutline"
{
    Properties
    {
        [HDR] _OutlineColor ("Outline Color", Color) = (1.75, 0.035, 0.018, 0.70)
        _OutlineWidth ("Object-space Width", Range(0.001, 0.5)) = 0.04
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent+20"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "HOSTILE_OUTLINE"
            Cull Front
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha One

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _OutlineColor;
            float _OutlineWidth;

            v2f vert(appdata input)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 expanded = input.vertex.xyz +
                    normalize(input.normal) * _OutlineWidth;
                output.position = UnityObjectToClipPos(float4(expanded, 1.0));
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                return _OutlineColor;
            }
            ENDCG
        }
    }

    Fallback Off
}
