Shader "Voxel Planet/Bio Creature Skin"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _Metallic ("Metallic", Range(0,1)) = 0
        _Glossiness ("Smoothness", Range(0,1)) = 0.22
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.0

        fixed4 _Color;
        half _Metallic;
        half _Glossiness;

        struct Input
        {
            fixed4 color : COLOR;
        };

        void vert(inout appdata_full vertex, out Input output)
        {
            UNITY_INITIALIZE_OUTPUT(Input, output);
            output.color = vertex.color;
        }

        void surf(Input input, inout SurfaceOutputStandard output)
        {
            output.Albedo = input.color.rgb * _Color.rgb;
            output.Metallic = _Metallic;
            output.Smoothness = _Glossiness;
            output.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
