using UnityEngine;
using UnityEngine.Rendering;

namespace SpacecraftEditor
{
    [DisallowMultipleComponent]
    public sealed class WorkshopLightingRig : MonoBehaviour
    {
        [SerializeField] private Color ambientSky =
            new Color(0.34f, 0.42f, 0.50f, 1f);
        [SerializeField] private Color ambientEquator =
            new Color(0.24f, 0.29f, 0.34f, 1f);
        [SerializeField] private Color ambientGround =
            new Color(0.12f, 0.14f, 0.17f, 1f);
        [SerializeField, Range(0f, 3f)] private float ambientIntensity = 1.25f;
        [SerializeField, Range(0f, 3f)] private float reflectionIntensity = 1.35f;

        private void Awake()
        {
            ApplyEnvironment();
            ConfigureLights();
        }

        private void Start()
        {
            ReflectionProbe probe = GetComponentInChildren<ReflectionProbe>(true);
            if (probe != null && probe.mode == ReflectionProbeMode.Realtime)
                probe.RenderProbe();
        }

        public void ApplyEnvironment()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = ambientSky;
            RenderSettings.ambientEquatorColor = ambientEquator;
            RenderSettings.ambientGroundColor = ambientGround;
            RenderSettings.ambientIntensity = ambientIntensity;
            RenderSettings.reflectionIntensity = reflectionIntensity;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;

            Camera camera = Camera.main;
            if (camera != null)
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.018f, 0.035f, 0.052f, 1f);
                camera.allowHDR = true;
            }
        }

        private void ConfigureLights()
        {
            ConfigureDirectional(
                "KeyLight",
                new Color(1f, 0.91f, 0.80f),
                1.75f,
                new Vector3(36f, -28f, 0f),
                true);
            ConfigureDirectional(
                "FillLight",
                new Color(0.65f, 0.82f, 1f),
                0.82f,
                new Vector3(325f, 145f, 0f),
                false);
            ConfigurePoint(
                "TopLight",
                new Color(0.82f, 0.91f, 1f),
                5.2f,
                18f,
                new Vector3(0f, 6.5f, -1.5f));
            ConfigurePoint(
                "RimLight",
                new Color(0.30f, 0.72f, 1f),
                4.8f,
                18f,
                new Vector3(-4.5f, 2.5f, 3.5f));
        }

        private void ConfigureDirectional(
            string name,
            Color color,
            float intensity,
            Vector3 euler,
            bool shadows)
        {
            Light light = FindOrCreateLight(name);
            light.type = LightType.Directional;
            light.color = color;
            light.intensity = intensity;
            light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            light.transform.localPosition = Vector3.zero;
            light.transform.localRotation = Quaternion.Euler(euler);
        }

        private void ConfigurePoint(
            string name,
            Color color,
            float intensity,
            float range,
            Vector3 localPosition)
        {
            Light light = FindOrCreateLight(name);
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
            light.transform.localPosition = localPosition;
            light.transform.localRotation = Quaternion.identity;
        }

        private Light FindOrCreateLight(string objectName)
        {
            Transform child = transform.Find(objectName);
            if (child == null)
            {
                child = new GameObject(objectName).transform;
                child.SetParent(transform, false);
            }
            Light light = child.GetComponent<Light>();
            if (light == null)
                light = child.gameObject.AddComponent<Light>();
            return light;
        }
    }
}
