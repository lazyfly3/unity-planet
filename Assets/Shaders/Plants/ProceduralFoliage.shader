Shader "Plant/ProceduralFoliage"
{
    Properties
    {
        _LeafColor ("Leaf Color", Color) = (0.18, 0.55, 0.2, 1)
        _AccentColor ("Accent Color", Color) = (0.5, 0.8, 0.25, 1)
        _Cutoff ("Cutoff", Range(0, 0.5)) = 0.06
        _Smoothness ("Smoothness", Range(0, 1)) = 0.2
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "IgnoreProjector"="True" }
        Cull Off
        LOD 180

        CGPROGRAM
        #pragma surface surf Standard alphatest:_Cutoff addshadow fullforwardshadows
        #pragma target 3.0
        #pragma multi_compile_instancing

        struct Input
        {
            float2 uv_MainTex;
            fixed4 color : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        half _Smoothness;
        UNITY_INSTANCING_BUFFER_START(PlantLeafProps)
            UNITY_DEFINE_INSTANCED_PROP(fixed4, _LeafColor)
            UNITY_DEFINE_INSTANCED_PROP(fixed4, _AccentColor)
            UNITY_DEFINE_INSTANCED_PROP(float, _HueVariation)
        UNITY_INSTANCING_BUFFER_END(PlantLeafProps)

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            UNITY_SETUP_INSTANCE_ID(IN);
            fixed4 leaf = UNITY_ACCESS_INSTANCED_PROP(PlantLeafProps, _LeafColor);
            fixed4 accent = UNITY_ACCESS_INSTANCED_PROP(PlantLeafProps, _AccentColor);
            float centerDistance = abs(IN.uv_MainTex.x * 2.0 - 1.0);
            float silhouette = pow(saturate(sin(saturate(IN.uv_MainTex.y) * 3.14159265)), 0.42);
            float alpha = saturate((silhouette - centerDistance) * 8.0 + 0.25);
            float vein = smoothstep(0.08, 0.0, centerDistance) * 0.24;
            fixed3 vertexTint = max(IN.color.rgb, fixed3(0.08, 0.08, 0.08));
            o.Albedo = lerp(leaf.rgb * vertexTint, accent.rgb, vein);
            o.Metallic = 0;
            o.Smoothness = _Smoothness;
            o.Alpha = alpha;
        }
        ENDCG
    }
    FallBack "Transparent/Cutout/Diffuse"
}
