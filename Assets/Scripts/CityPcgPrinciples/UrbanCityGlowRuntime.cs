using System.Collections.Generic;
using UnityEngine;

namespace UnityPlanet.CityPcg
{
    /// <summary>
    /// Enables the inexpensive camera-side bloom needed by Dark City 2's HDR
    /// emissive windows and neon materials. It is owned by the generated urban
    /// mission root, so natural terrain missions and their cameras are untouched.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UrbanCityGlowRuntime : MonoBehaviour
    {
        [SerializeField, Range(0.5f, 3f)] float threshold = 1.02f;
        [SerializeField, Range(0f, 2f)] float intensity = 0.78f;
        [SerializeField, Range(1, 3)] int blurIterations = 2;
        [SerializeField, Range(1, 3)] int downsample = 2;

        readonly HashSet<UrbanBloomImageEffect> ownedEffects =
            new HashSet<UrbanBloomImageEffect>();
        float nextCameraScanTime;

        public void Configure(
            float bloomThreshold = 1.02f,
            float bloomIntensity = 0.78f,
            int iterations = 2,
            int resolutionDownsample = 2)
        {
            threshold = Mathf.Clamp(bloomThreshold, 0.5f, 3f);
            intensity = Mathf.Clamp(bloomIntensity, 0f, 2f);
            blurIterations = Mathf.Clamp(iterations, 1, 3);
            downsample = Mathf.Clamp(resolutionDownsample, 1, 3);
            RefreshOwnedEffects();
        }

        void OnEnable()
        {
            nextCameraScanTime = 0f;
            if (Application.isPlaying)
                AttachToGameCameras();
        }

        void LateUpdate()
        {
            if (!Application.isPlaying || Time.unscaledTime < nextCameraScanTime)
                return;
            nextCameraScanTime = Time.unscaledTime + 0.75f;
            AttachToGameCameras();
        }

        void OnDisable()
        {
            foreach (UrbanBloomImageEffect effect in ownedEffects)
            {
                if (effect != null)
                    Destroy(effect);
            }
            ownedEffects.Clear();
        }

        void AttachToGameCameras()
        {
            Camera[] cameras = Camera.allCameras;
            for (int index = 0; index < cameras.Length; index++)
            {
                Camera camera = cameras[index];
                if (camera == null || !camera.enabled ||
                    camera.cameraType != CameraType.Game)
                {
                    continue;
                }
                UrbanBloomImageEffect effect =
                    camera.GetComponent<UrbanBloomImageEffect>();
                if (effect == null)
                {
                    effect = camera.gameObject.AddComponent<UrbanBloomImageEffect>();
                    ownedEffects.Add(effect);
                }
                effect.Configure(
                    threshold,
                    intensity,
                    blurIterations,
                    downsample);
                camera.allowHDR = true;
            }
        }

        void RefreshOwnedEffects()
        {
            foreach (UrbanBloomImageEffect effect in ownedEffects)
            {
                if (effect != null)
                    effect.Configure(
                        threshold,
                        intensity,
                        blurIterations,
                        downsample);
            }
        }
    }

    [ExecuteAlways]
    [RequireComponent(typeof(Camera))]
    [DisallowMultipleComponent]
    public sealed class UrbanBloomImageEffect : MonoBehaviour
    {
        const string BloomShaderName = "Hidden/UnityPlanet/CityBloom";

        [SerializeField, Range(0.5f, 3f)] float threshold = 1.02f;
        [SerializeField, Range(0f, 2f)] float intensity = 0.78f;
        [SerializeField, Range(1, 3)] int blurIterations = 2;
        [SerializeField, Range(1, 3)] int downsample = 2;
        Material bloomMaterial;

        public void Configure(
            float bloomThreshold,
            float bloomIntensity,
            int iterations,
            int resolutionDownsample)
        {
            threshold = Mathf.Clamp(bloomThreshold, 0.5f, 3f);
            intensity = Mathf.Clamp(bloomIntensity, 0f, 2f);
            blurIterations = Mathf.Clamp(iterations, 1, 3);
            downsample = Mathf.Clamp(resolutionDownsample, 1, 3);
        }

        void OnDisable()
        {
            if (bloomMaterial == null)
                return;
            if (Application.isPlaying)
                Destroy(bloomMaterial);
            else
                DestroyImmediate(bloomMaterial);
            bloomMaterial = null;
        }

        void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (!EnsureMaterial() || intensity <= 0.001f)
            {
                Graphics.Blit(source, destination);
                return;
            }

            int width = Mathf.Max(2, source.width / downsample);
            int height = Mathf.Max(2, source.height / downsample);
            RenderTextureFormat format =
                SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)
                    ? RenderTextureFormat.ARGBHalf
                    : RenderTextureFormat.Default;
            RenderTexture first = RenderTexture.GetTemporary(
                width,
                height,
                0,
                format,
                RenderTextureReadWrite.Linear);
            RenderTexture second = RenderTexture.GetTemporary(
                width,
                height,
                0,
                format,
                RenderTextureReadWrite.Linear);
            first.filterMode = FilterMode.Bilinear;
            second.filterMode = FilterMode.Bilinear;
            try
            {
                bloomMaterial.SetFloat("_Threshold", threshold);
                bloomMaterial.SetFloat("_Intensity", intensity);
                Graphics.Blit(source, first, bloomMaterial, 0);
                for (int iteration = 0; iteration < blurIterations; iteration++)
                {
                    Graphics.Blit(first, second, bloomMaterial, 1);
                    Graphics.Blit(second, first, bloomMaterial, 2);
                }
                bloomMaterial.SetTexture("_BloomTex", first);
                Graphics.Blit(source, destination, bloomMaterial, 3);
            }
            finally
            {
                RenderTexture.ReleaseTemporary(first);
                RenderTexture.ReleaseTemporary(second);
            }
        }

        bool EnsureMaterial()
        {
            if (bloomMaterial != null)
                return true;
            Shader shader = Shader.Find(BloomShaderName);
            if (shader == null || !shader.isSupported)
                return false;
            bloomMaterial = new Material(shader)
            {
                name = "UrbanCityBloom_Runtime",
                hideFlags = HideFlags.HideAndDontSave
            };
            return true;
        }
    }
}
