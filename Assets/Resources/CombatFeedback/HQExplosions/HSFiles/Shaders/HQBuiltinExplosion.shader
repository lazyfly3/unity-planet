Shader "UnityPlanet/HQ Explosions/Built-In Explosion"
{
    Properties
    {
        _MainTex("Explosion Flipbook", 2D) = "white" {}
        [HideInInspector] _Maintexture("Legacy Main Texture", 2D) = "white" {}
        _Noise("Noise", 2D) = "white" {}
        [HDR] _Color("Color", Color) = (1, 1, 1, 1)
        [HDR] _GlowColor("Glow Color", Color) = (1, 0.65, 0, 1)
        _Opacity("Opacity", Range(0, 1)) = 1
        _FinalEmission("Final Emission", Float) = 1
        _NoisespeedXYNoisepowerZGlowpowerW(
            "Noise Speed XY / Noise Power Z / Glow Power W",
            Vector) = (0.314, 0.427, 0.001, 4)
        // These prefabs use ParticleSystem Texture Sheet Animation, so the
        // incoming UV is already on the active atlas frame. Keep this neutral
        // unless a future effect explicitly supplies a manual frame in UV0.w.
        _TilingXY("Optional Manual Flipbook Columns / Rows", Vector) =
            (1, 1, 0, 0)
        _Depthpower("Soft Intersection Distance", Range(0.001, 8)) = 0.5
        [HideInInspector] _TintColor("Legacy Tint Color", Color) =
            (1, 1, 1, 1)
        [HideInInspector] _Emission("Legacy Emission", Float) = 1
        [HideInInspector] _InvFade("Legacy Soft Factor", Float) = 3
        [HideInInspector] _SrcBlend("Source Blend", Float) = 1
        [HideInInspector] _DstBlend("Destination Blend", Float) = 10
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

        Blend [_SrcBlend] [_DstBlend]
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
            fixed4 _GlowColor;
            half _Opacity;
            half _FinalEmission;
            float4 _NoisespeedXYNoisepowerZGlowpowerW;
            float4 _TilingXY;
            half _Depthpower;

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

                float2 noiseUv =
                    input.texcoord.xy * _Noise_ST.xy +
                    _Noise_ST.zw +
                    _Time.y *
                    _NoisespeedXYNoisepowerZGlowpowerW.xy;
                half2 noiseOffset =
                    tex2D(_Noise, noiseUv).rg *
                    _NoisespeedXYNoisepowerZGlowpowerW.z;
                // Texture Sheet Animation has already selected the atlas
                // frame in UV0.xy. Applying _TilingXY here would slice that
                // frame a second time; UV0.z remains the authored glow value.
                float2 mainUv =
                    input.texcoord.xy * _MainTex_ST.xy +
                    _MainTex_ST.zw +
                    noiseOffset;
                fixed4 textureSample = tex2D(_MainTex, mainUv);

                half alpha = saturate(
                    textureSample.a *
                    _Color.a *
                    input.color.a *
                    _Opacity *
                    softFade);
                half4 glowMask = pow(
                    abs(textureSample),
                    max(
                        0.01h,
                        (half)
                        _NoisespeedXYNoisepowerZGlowpowerW.w));
                half3 glowColor =
                    _GlowColor.rgb *
                    max(0.0h, (half)input.texcoord.z) *
                    glowMask.rgb;
                half3 rgb =
                    (textureSample.rgb + glowColor) *
                    _Color.rgb *
                    input.color.rgb;
                rgb *= max(0.0h, _FinalEmission);

                // The source materials use One / OneMinusSrcAlpha,
                // so RGB must be premultiplied to keep transparent
                // flipbook texels from bleeding into adjacent frames.
                fixed4 result = fixed4(rgb * alpha, alpha);
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
