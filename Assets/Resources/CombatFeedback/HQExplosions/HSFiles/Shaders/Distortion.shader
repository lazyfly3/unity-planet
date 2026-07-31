Shader "Hovl/Particles/Distortion"
{
    Properties
    {
        _InvFade ("Soft Particles Factor", Range(0.01,3.0)) = 1.0
        _NormalMap("Normal Map", 2D) = "bump" {}
        _Distortionpower("Distortion power", Float) = 1
        [Toggle]_Enablesimpleopacity("Enable simple opacity", Float) = 0
        [HideInInspector] _texcoord("", 2D) = "white" {}
    }

    Category
    {
        SubShader
        {
            LOD 0

            Tags
            {
                "Queue"="Transparent"
                "IgnoreProjector"="True"
                "RenderType"="Transparent"
                "PreviewType"="Plane"
            }
            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask RGB
            Cull Off
            Lighting Off
            ZWrite Off
            ZTest LEqual
            Fog { Mode Off }
            GrabPass { }

            Pass
            {
                CGPROGRAM
                #if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
                #define ASE_DECLARE_SCREENSPACE_TEXTURE(tex) UNITY_DECLARE_SCREENSPACE_TEXTURE(tex);
                #else
                #define ASE_DECLARE_SCREENSPACE_TEXTURE(tex) UNITY_DECLARE_SCREENSPACE_TEXTURE(tex)
                #endif

                #ifndef UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX
                #define UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input)
                #endif

                #pragma vertex vert
                #pragma fragment frag
                #pragma fragmentoption ARB_precision_hint_fastest
                #pragma target 2.0
                #pragma multi_compile_instancing
                #pragma multi_compile_particles
                #pragma multi_compile_fog

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
                    UNITY_VERTEX_INPUT_INSTANCE_ID
                    UNITY_VERTEX_OUTPUT_STEREO
                    float4 ase_texcoord3 : TEXCOORD3;
                };

                #if UNITY_VERSION >= 560
                UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
                #else
                uniform sampler2D_float _CameraDepthTexture;
                #endif

                uniform float _InvFade;
                ASE_DECLARE_SCREENSPACE_TEXTURE(_GrabTexture)
                uniform sampler2D _NormalMap;
                uniform float4 _NormalMap_ST;
                uniform float _Distortionpower;
                uniform float _Enablesimpleopacity;

                inline float4 ASE_ComputeGrabScreenPos(float4 pos)
                {
                    #if UNITY_UV_STARTS_AT_TOP
                    float scale = -1.0;
                    #else
                    float scale = 1.0;
                    #endif
                    float4 output = pos;
                    output.y = pos.w * 0.5f;
                    output.y =
                        (pos.y - output.y) *
                        _ProjectionParams.x *
                        scale +
                        output.y;
                    return output;
                }

                v2f vert(appdata_t input)
                {
                    v2f output;
                    UNITY_SETUP_INSTANCE_ID(input);
                    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                    UNITY_TRANSFER_INSTANCE_ID(input, output);
                    float4 clipPosition =
                        UnityObjectToClipPos(input.vertex);
                    output.ase_texcoord3 =
                        ComputeScreenPos(clipPosition);
                    output.vertex = clipPosition;
                    #ifdef SOFTPARTICLES_ON
                    output.projPos =
                        ComputeScreenPos(output.vertex);
                    output.projPos.z =
                        -UnityObjectToViewPos(input.vertex.xyz).z;
                    #endif
                    output.color = input.color;
                    output.texcoord = input.texcoord;
                    UNITY_TRANSFER_FOG(output, output.vertex);
                    return output;
                }

                fixed4 frag(v2f input) : SV_Target
                {
                    UNITY_SETUP_INSTANCE_ID(input);
                    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                    #ifdef SOFTPARTICLES_ON
                    float sceneDepth = LinearEyeDepth(
                        SAMPLE_DEPTH_TEXTURE_PROJ(
                            _CameraDepthTexture,
                            UNITY_PROJ_COORD(input.projPos)));
                    float particleDepth = input.projPos.z;
                    float fade = saturate(
                        _InvFade *
                        (sceneDepth - particleDepth));
                    input.color.a *= fade;
                    #endif

                    float4 grabPosition =
                        ASE_ComputeGrabScreenPos(
                            input.ase_texcoord3);
                    float4 normalizedGrabPosition =
                        grabPosition / grabPosition.w;
                    float2 normalUv =
                        input.texcoord.xy * _NormalMap_ST.xy +
                        _NormalMap_ST.zw;
                    float3 normal =
                        UnpackNormal(tex2D(_NormalMap, normalUv));
                    float opacity =
                        _Enablesimpleopacity > 0.5f
                            ? 1.0f
                            : input.color.a;
                    float distortion =
                        _Distortionpower / 1000.0f * opacity;
                    float4 sceneColor =
                        UNITY_SAMPLE_SCREENSPACE_TEXTURE(
                            _GrabTexture,
                            normalizedGrabPosition.xy -
                            normal.xy * distortion);
                    float edgeAlpha = saturate(
                        (abs(normal.r) + abs(normal.g)) *
                        30.0f -
                        0.3f);
                    if (_Enablesimpleopacity > 0.5f)
                        edgeAlpha *= input.color.a;
                    return saturate(sceneColor) *
                           float4(1.0f, 1.0f, 1.0f, edgeAlpha);
                }
                ENDCG
            }
        }
    }
    Fallback Off
}
