Shader "Hidden/Voxel Planet/Underwater Screen"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _Blend ("Blend", Range(0,1)) = 0
        _Tint ("Water Tint", Color) = (0.02,0.23,0.31,1)
        _Submerged ("Fully Submerged", Range(0,1)) = 0
        _WaterDepth ("Water Depth", Float) = 0
        _EntryPulse ("Entry Pulse", Range(0,1)) = 0
        _FogDistance ("Fog Distance", Float) = 36
        _TimeValue ("Unscaled Time", Float) = 0
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D_float _CameraDepthTexture;
            float _Blend;
            float _Submerged;
            float _WaterDepth;
            float _EntryPulse;
            float _FogDistance;
            float _TimeValue;
            fixed4 _Tint;

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            fixed4 Frag(v2f_img input) : SV_Target
            {
                float2 uv = input.uv;
                float nearSurface = 1.0 - saturate(_WaterDepth / 2.5);
                float2 flow = float2(
                    sin(uv.y * 31.0 + _TimeValue * 2.15)
                        + sin(uv.y * 73.0 - _TimeValue * 1.27),
                    cos(uv.x * 27.0 - _TimeValue * 1.73)
                        + sin(uv.x * 61.0 + _TimeValue * 1.09));
                float distortionStrength =
                    lerp(0.0012, 0.0042, _Submerged)
                    * _Blend
                    * lerp(0.55, 1.0, nearSurface);
                float2 distortedUv = saturate(uv + flow * distortionStrength);
                fixed4 source = tex2D(_MainTex, distortedUv);

                float rawDepth = SAMPLE_DEPTH_TEXTURE(
                    _CameraDepthTexture,
                    distortedUv);
                float eyeDepth = LinearEyeDepth(rawDepth);
                float fogDistance = max(8.0, _FogDistance);
                float fog = 1.0 - exp(
                    -eyeDepth
                    / fogDistance
                    * lerp(0.72, 1.35, saturate(_WaterDepth / 12.0)));
                fog *= _Blend * lerp(0.35, 1.0, _Submerged);

                float2 causticUv =
                    distortedUv * float2(10.0, 7.0)
                    + float2(_TimeValue * 0.16, -_TimeValue * 0.11);
                float causticA =
                    abs(sin(causticUv.x + sin(causticUv.y * 1.7)));
                float causticB =
                    abs(sin(causticUv.y * 1.31 - cos(causticUv.x * 1.6)));
                float caustic = pow(saturate(1.0 - abs(causticA - causticB)), 8.0);
                caustic *= nearSurface
                    * saturate(1.0 - eyeDepth / 22.0)
                    * _Blend
                    * _Submerged;

                fixed3 absorbed = source.rgb;
                absorbed.r *= lerp(1.0, 0.32, fog);
                absorbed.g *= lerp(1.0, 0.73, fog);
                absorbed.b *= lerp(1.0, 0.86, fog);
                fixed3 fogColor = _Tint.rgb
                    * lerp(1.18, 0.44, saturate(_WaterDepth / 24.0));
                fixed3 color = lerp(absorbed, fogColor, fog * 0.92);
                color += lerp(_Tint.rgb, fixed3(0.35, 1.0, 1.0), 0.65)
                    * caustic
                    * 0.16;

                float2 centered = input.uv * 2 - 1;
                float vignette = smoothstep(0.18, 1.15, dot(centered, centered));
                color *= lerp(1.0, 0.56, vignette * _Blend);

                float pulseRing = abs(length(centered) - (1.2 - _EntryPulse));
                float pulse = exp(-pulseRing * 18.0)
                    * _EntryPulse
                    * _Submerged;
                color += fixed3(0.18, 0.82, 0.94) * pulse * 0.32;

                // While merely wading, keep the feedback below the waterline
                // instead of tinting the whole screen.
                float wadingMask = smoothstep(0.62, 0.96, 1.0 - uv.y);
                float visibility = lerp(wadingMask, 1.0, _Submerged);
                source.rgb = lerp(source.rgb, color, _Blend * visibility);
                return source;
            }
            ENDCG
        }
    }
}
