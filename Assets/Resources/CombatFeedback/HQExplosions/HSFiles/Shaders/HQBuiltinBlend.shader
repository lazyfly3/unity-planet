Shader "UnityPlanet/HQ Explosions/Built-In Blend"
{
    Properties
    {
        _MainTex("Particle Texture", 2D) = "white" {}
        [HDR] _TintColor("Tint Color", Color) = (1, 1, 1, 1)
        _Opacity("Opacity", Range(0, 1)) = 1
        _Emission("Emission", Float) = 1
        _InvFade("Soft Particles Factor", Range(0.01, 8)) = 3
        [HideInInspector] _SrcBlend("Source Blend", Float) = 5
        [HideInInspector] _DstBlend("Destination Blend", Float) = 10
        [HideInInspector] _BUILTIN_SrcBlend(
            "Built-In Source Blend",
            Float) = 5
        [HideInInspector] _BUILTIN_DstBlend(
            "Built-In Destination Blend",
            Float) = 10
        [HideInInspector] _Usecenterglow("Use Center Glow", Float) = 0
        [HideInInspector] _Usedepth("Legacy Use Depth", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
        }

        // Shader Graph serializes the authoritative Built-in blend state
        // separately. Using it preserves additive particles and alpha-blended
        // beams even when the SRP-facing _SrcBlend field differs.
        Blend [_BUILTIN_SrcBlend] [_BUILTIN_DstBlend]
        ColorMask RGB
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest LEqual

        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_particles
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_FOG_COORDS(1)
                #ifdef SOFTPARTICLES_ON
                float4 projPos : TEXCOORD2;
                #endif
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _TintColor;
            half _Opacity;
            half _Emission;
            half _InvFade;
            half _Usecenterglow;

            #if UNITY_VERSION >= 560
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            #else
            sampler2D_float _CameraDepthTexture;
            #endif

            v2f vert(appdata_t input)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.vertex = UnityObjectToClipPos(input.vertex);
                output.color = input.color;
                output.texcoord =
                    TRANSFORM_TEX(input.texcoord, _MainTex);
                UNITY_TRANSFER_FOG(output, output.vertex);

                #ifdef SOFTPARTICLES_ON
                output.projPos = ComputeScreenPos(output.vertex);
                output.projPos.z =
                    -UnityObjectToViewPos(input.vertex.xyz).z;
                #endif

                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half softFade = 1.0h;
                #ifdef SOFTPARTICLES_ON
                float sceneDepth = LinearEyeDepth(
                    SAMPLE_DEPTH_TEXTURE_PROJ(
                        _CameraDepthTexture,
                        UNITY_PROJ_COORD(input.projPos)));
                softFade = saturate(
                    _InvFade * (sceneDepth - input.projPos.z));
                #endif

                fixed4 textureSample =
                    tex2D(_MainTex, input.texcoord);
                half alpha = saturate(
                    textureSample.a *
                    _TintColor.a *
                    input.color.a *
                    _Opacity *
                    softFade);
                half centerGlow =
                    pow(saturate(textureSample.r), 4.0h) *
                    saturate(_Usecenterglow);
                half3 rgb =
                    textureSample.rgb *
                    _TintColor.rgb *
                    input.color.rgb *
                    max(0.0h, _Emission) *
                    (1.0h + centerGlow);

                fixed4 result = fixed4(rgb, alpha);
                UNITY_APPLY_FOG_COLOR(
                    input.fogCoord,
                    result,
                    fixed4(0, 0, 0, 0));
                return result;
            }
            ENDCG
        }
    }

    Fallback Off
}
