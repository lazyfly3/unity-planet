Shader "Plant/ProceduralBark"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.24, 0.13, 0.07, 1)
        _Roughness ("Roughness", Range(0, 1)) = 0.78
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 180

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0
        #pragma multi_compile_instancing

        struct Input
        {
            float2 uv_MainTex;
            fixed4 color : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        half _Roughness;
        UNITY_INSTANCING_BUFFER_START(PlantProps)
            UNITY_DEFINE_INSTANCED_PROP(fixed4, _BaseColor)
        UNITY_INSTANCING_BUFFER_END(PlantProps)

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            UNITY_SETUP_INSTANCE_ID(IN);
            fixed4 tint = UNITY_ACCESS_INSTANCED_PROP(PlantProps, _BaseColor);
            float grain = 0.88 + 0.12 * sin(IN.uv_MainTex.y * 53.0 + IN.uv_MainTex.x * 11.0);
            o.Albedo = tint.rgb * IN.color.rgb * grain;
            o.Metallic = 0;
            o.Smoothness = 1.0 - _Roughness;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
