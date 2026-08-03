Shader "UnityPlanet/HQ Explosions/Built-In Blend"
{
    Properties
    {
        _MainTex("Particle Texture", 2D) = "white" {}
        _Noise("Noise", 2D) = "white" {}
        [HideInInspector] _Flow("Flow", 2D) = "white" {}
        [HideInInspector] _Mask("Mask", 2D) = "white" {}
        [HDR] _Color("Color", Color) = (1, 1, 1, 1)
        _Opacity("Opacity", Range(0, 1)) = 1
        _Emission("Emission", Float) = 1
        _SpeedMainTexUVNoiseZW(
            "Main UV Speed XY / Noise UV Speed ZW",
            Vector) = (0, 0, 0, 0)
        [HideInInspector] _DistortionSpeedXYPowerZ(
            "Distortion Speed XY / Power Z",
            Vector) = (0, 0, 0, 0)
        [HideInInspector] _TilingMainTexUVNoiseZW(
            "Main / Noise Tiling",
            Vector) = (1, 1, 1, 1)
        _Depthpower("Soft Intersection Distance", Range(0.001, 8)) = 0.5
        [HideInInspector] _TintColor("Legacy Tint Color", Color) =
            (1, 1, 1, 1)
        [HideInInspector] _InvFade("Legacy Soft Factor", Float) = 3
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
                float4 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float4 texcoord : TEXCOORD0;
                UNITY_FOG_COORDS(1)
                #ifdef SOFTPARTICLES_ON
                float4 projPos : TEXCOORD2;
                #endif
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _Noise;
            float4 _Noise_ST;
            fixed4 _Color;
            half _Opacity;
            half _Emission;
            float4 _SpeedMainTexUVNoiseZW;
            half _Depthpower;
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
                output.texcoord = input.texcoord;
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
                    (sceneDepth - input.projPos.z) /
                    max(0.001h, _Depthpower));
                #endif

                float2 mainUv =
                    input.texcoord.xy * _MainTex_ST.xy +
                    _MainTex_ST.zw +
                    _Time.y * _SpeedMainTexUVNoiseZW.xy;
                float2 noiseUv =
                    input.texcoord.xy * _Noise_ST.xy +
                    _Noise_ST.zw +
                    _Time.y * _SpeedMainTexUVNoiseZW.zw;
                fixed4 mainSample = tex2D(_MainTex, mainUv);
                fixed4 noiseSample = tex2D(_Noise, noiseUv);
                fixed4 textureSample = mainSample * noiseSample;
                half alpha = saturate(
                    textureSample.a *
                    _Color.a *
                    input.color.a *
                    _Opacity *
                    softFade);
                half centerGlow =
                    saturate(
                        textureSample.r *
                        (1.0h - saturate(input.texcoord.z))) *
                    saturate(_Usecenterglow);
                half3 rgb =
                    textureSample.rgb *
                    _Color.rgb *
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
