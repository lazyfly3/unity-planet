Shader "UnityPlanet/CombatMap/TerrainBlend"
{
    Properties
    {
        _GrassTex ("Grass", 2D) = "white" {}
        _MudTex ("Mud", 2D) = "white" {}
        _DirtTex ("Dirt", 2D) = "white" {}
        _RockTex ("Rock", 2D) = "white" {}
        _TextureScale ("World Texture Scale", Float) = 14
        _Tint ("Tint", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 250
        Cull Off

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.0

        sampler2D _GrassTex;
        sampler2D _MudTex;
        sampler2D _DirtTex;
        sampler2D _RockTex;
        half _TextureScale;
        fixed4 _Tint;

        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
            float4 color : COLOR;
        };

        fixed3 Triplanar(
            sampler2D source,
            float3 worldPosition,
            float3 worldNormal)
        {
            float3 weights = pow(abs(worldNormal), 4.0);
            weights /= max(0.0001, weights.x + weights.y + weights.z);
            float scale = max(1.0, _TextureScale);
            fixed3 x = tex2D(source, worldPosition.zy / scale).rgb;
            fixed3 y = tex2D(source, worldPosition.xz / scale).rgb;
            fixed3 z = tex2D(source, worldPosition.xy / scale).rgb;
            return x * weights.x + y * weights.y + z * weights.z;
        }

        void surf(Input IN, inout SurfaceOutputStandard output)
        {
            float3 normal = normalize(IN.worldNormal);
            fixed3 grass = Triplanar(_GrassTex, IN.worldPos, normal);
            fixed3 mud = Triplanar(_MudTex, IN.worldPos, normal);
            fixed3 dirt = Triplanar(_DirtTex, IN.worldPos, normal);
            fixed3 rock = Triplanar(_RockTex, IN.worldPos, normal);
            fixed3 albedo = lerp(grass, mud, saturate(IN.color.b));
            albedo = lerp(albedo, dirt, saturate(IN.color.g));
            albedo = lerp(albedo, rock, saturate(IN.color.r));
            output.Albedo = albedo * _Tint.rgb;
            output.Metallic = 0.0;
            output.Smoothness = 0.18;
            output.Occlusion = 1.0;
            output.Alpha = 1.0;
        }
        ENDCG
    }
    FallBack "Standard"
}
