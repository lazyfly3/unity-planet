using UnityEngine;
using UnityEngine.Rendering;

[DefaultExecutionOrder(700)]
[DisallowMultipleComponent]
public sealed class PlanetSurfaceDayNightController : MonoBehaviour
{
    static Vector3 currentSunDirection = new Vector3(0.35f, 0.7f, 0.55f).normalized;

    VoxelQuadSphereWorld world;
    PlanetCelestialProfile profile;
    PlanetLowPolyVisualProfile visualProfile;
    Light sun;
    GameObject atmosphereShell;
    Material surfaceSkyMaterial;
    Color baseSunColor = Color.white;
    float baseSunIntensity = 1f;
    AmbientMode baseAmbientMode;
    Color baseAmbientColor;
    Color baseAmbientSkyColor;
    Color baseAmbientEquatorColor;
    Color baseAmbientGroundColor;
    Material baseSkybox;
    Camera surfaceCamera;
    CameraClearFlags baseClearFlags;
    Color baseBackgroundColor;
    Light cameraFillLight;
    float baseCameraFillRange;
    float baseCameraFillIntensity;

    public static float SampleDaylight(Vector3 radialUp)
        => Mathf.Clamp01(Vector3.Dot(radialUp.normalized, currentSunDirection) * 0.55f + 0.5f);

    public void Configure(VoxelQuadSphereWorld valueWorld, PlanetCelestialProfile valueProfile)
    {
        Configure(valueWorld, valueProfile, null);
    }

    public void Configure(VoxelQuadSphereWorld valueWorld, PlanetCelestialProfile valueProfile,
        PlanetLowPolyVisualProfile valueVisualProfile)
    {
        world = valueWorld;
        profile = (valueProfile ?? PlanetCelestialProfile.CreateCompatibleDefault()).Clone();
        visualProfile = valueVisualProfile != null ? valueVisualProfile.Clone() : null;
        visualProfile?.ClampValues();

        baseAmbientMode = RenderSettings.ambientMode;
        baseAmbientColor = RenderSettings.ambientLight;
        baseAmbientSkyColor = RenderSettings.ambientSkyColor;
        baseAmbientEquatorColor = RenderSettings.ambientEquatorColor;
        baseAmbientGroundColor = RenderSettings.ambientGroundColor;
        baseSkybox = RenderSettings.skybox;
        sun = FindDirectionalLight();
        if (sun != null)
        {
            baseSunColor = sun.color;
            baseSunIntensity = sun.intensity;
        }

        surfaceCamera = Camera.main;
        if (surfaceCamera != null)
        {
            baseClearFlags = surfaceCamera.clearFlags;
            baseBackgroundColor = surfaceCamera.backgroundColor;
            surfaceCamera.farClipPlane = Mathf.Max(surfaceCamera.farClipPlane, profile.radius * 3f);
            cameraFillLight = surfaceCamera.GetComponent<Light>();
            if (cameraFillLight != null && cameraFillLight.type == LightType.Point)
            {
                baseCameraFillRange = cameraFillLight.range;
                baseCameraFillIntensity = cameraFillLight.intensity;
                cameraFillLight.range = Mathf.Max(
                    cameraFillLight.range,
                    Mathf.Clamp(profile.maximumTerrainElevation * 0.45f, 45f, 80f));
                cameraFillLight.intensity = Mathf.Max(cameraFillLight.intensity, 1.35f);
            }
            if (profile.HasAtmosphere)
            {
                RenderSettings.skybox = null;
                surfaceCamera.clearFlags = CameraClearFlags.SolidColor;
                surfaceCamera.backgroundColor = visualProfile != null
                    ? visualProfile.zenithColor
                    : profile.atmosphereVisual.zenithColor;
            }
        }

        if (visualProfile != null)
            BuildSurfaceSky();
        else
            BuildLegacyAtmosphereShell();
    }

    void LateUpdate()
    {
        if (world == null || profile == null)
            return;

        double time = GalaxyTravelManager.Instance?.UniverseTimeSeconds ?? Time.timeAsDouble;
        Quaternion bodyRotation = PlanetReferenceFrame.RotationAtTime(profile, time);
        Vector3 inertialLightDirection = new Vector3(0.35f, 0.7f, 0.55f).normalized;
        currentSunDirection = Quaternion.Inverse(bodyRotation) * inertialLightDirection;

        Vector3 center = world.GetPlanetCenterWorld();
        if (surfaceSkyMaterial != null)
        {
            surfaceSkyMaterial.SetVector("_PlanetCenter", new Vector4(center.x, center.y, center.z, 1f));
            surfaceSkyMaterial.SetVector("_SunDirection",
                new Vector4(currentSunDirection.x, currentSunDirection.y, currentSunDirection.z, 0f));
        }

        if (sun == null)
            return;

        sun.transform.rotation = Quaternion.LookRotation(-currentSunDirection, profile.rotationAxis);
        Transform player = world.PlayerSpawn;
        Vector3 playerUp = player != null
            ? (player.position - center).normalized
            : Vector3.up;
        float daylight = SampleDaylight(playerUp);
        float sunset = 1f - Mathf.Abs(Vector3.Dot(playerUp, currentSunDirection));

        Color horizon;
        Color zenith;
        Color sunsetColor;
        if (visualProfile != null)
        {
            horizon = visualProfile.horizonColor;
            zenith = visualProfile.zenithColor;
            sunsetColor = visualProfile.sunsetColor;
        }
        else
        {
            AtmosphereVisualProfile atmosphere = profile.atmosphereVisual ?? new AtmosphereVisualProfile();
            horizon = atmosphere.horizonColor;
            zenith = atmosphere.zenithColor;
            sunsetColor = atmosphere.sunsetColor;
        }

        sun.intensity = baseSunIntensity * Mathf.Lerp(0.18f, 1f, daylight);
        sun.color = Color.Lerp(
            Color.Lerp(new Color(0.18f, 0.24f, 0.42f), sunsetColor, sunset * daylight),
            baseSunColor,
            daylight);

        if (visualProfile != null)
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            Color nightSky = Color.Lerp(
                new Color(0.11f, 0.14f, 0.23f, 1f),
                zenith,
                0.32f);
            Color nightEquator = Color.Lerp(
                new Color(0.12f, 0.14f, 0.2f, 1f),
                horizon,
                0.22f);
            Color nightGround = Color.Lerp(
                new Color(0.09f, 0.1f, 0.13f, 1f),
                visualProfile.lowlandColor,
                0.16f);
            RenderSettings.ambientSkyColor = Color.Lerp(nightSky, zenith, daylight);
            RenderSettings.ambientEquatorColor = Color.Lerp(
                nightEquator,
                horizon * 0.72f,
                daylight);
            RenderSettings.ambientGroundColor = Color.Lerp(
                nightGround,
                visualProfile.lowlandColor * 0.38f,
                daylight);
        }
        else
        {
            RenderSettings.ambientLight = Color.Lerp(
                new Color(0.015f, 0.025f, 0.06f),
                Color.Lerp(zenith, Color.white, 0.28f),
                daylight);
        }
    }

    void BuildSurfaceSky()
    {
        if (!profile.HasAtmosphere || world == null)
            return;

        Shader shader = Shader.Find("Voxel Planet/Low Poly Surface Sky");
        if (shader == null)
        {
            Debug.LogError("PlanetSurfaceDayNightController: missing Low Poly Surface Sky shader.", this);
            return;
        }

        atmosphereShell = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        atmosphereShell.name = "RuntimeLowPolySurfaceSky";
        atmosphereShell.transform.SetParent(world.transform, false);
        atmosphereShell.transform.localPosition = world.GetPlanetCenterLocal();
        atmosphereShell.transform.localScale = Vector3.one * (profile.radius * 6f);
        Collider collider = atmosphereShell.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = false;
            Destroy(collider);
        }

        surfaceSkyMaterial = new Material(shader) { name = "RuntimeLowPolySurfaceSky" };
        surfaceSkyMaterial.SetColor("_HorizonColor", visualProfile.horizonColor);
        surfaceSkyMaterial.SetColor("_ZenithColor", visualProfile.zenithColor);
        surfaceSkyMaterial.SetColor("_SunsetColor", visualProfile.sunsetColor);
        surfaceSkyMaterial.SetFloat("_HazeStrength", visualProfile.hazeStrength);
        atmosphereShell.GetComponent<Renderer>().sharedMaterial = surfaceSkyMaterial;
    }

    void BuildLegacyAtmosphereShell()
    {
        if (!profile.HasAtmosphere || world == null)
            return;

        atmosphereShell = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        atmosphereShell.name = "RuntimeSurfaceAtmosphere";
        atmosphereShell.transform.SetParent(world.transform, false);
        atmosphereShell.transform.localPosition = world.GetPlanetCenterLocal();
        atmosphereShell.transform.localScale = Vector3.one
            * ((profile.radius + profile.atmosphereTopAltitude) * 2f);
        Collider collider = atmosphereShell.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = false;
            Destroy(collider);
        }
        Renderer renderer = atmosphereShell.GetComponent<Renderer>();
        PlanetAtmosphereVisualController visual = atmosphereShell.AddComponent<PlanetAtmosphereVisualController>();
        visual.Configure(world.transform, profile, renderer);
    }

    static Light FindDirectionalLight()
    {
        foreach (Light light in FindObjectsOfType<Light>())
            if (light.type == LightType.Directional)
                return light;
        return null;
    }

    void OnDestroy()
    {
        RenderSettings.ambientMode = baseAmbientMode;
        RenderSettings.ambientLight = baseAmbientColor;
        RenderSettings.ambientSkyColor = baseAmbientSkyColor;
        RenderSettings.ambientEquatorColor = baseAmbientEquatorColor;
        RenderSettings.ambientGroundColor = baseAmbientGroundColor;
        RenderSettings.skybox = baseSkybox;
        if (sun != null)
        {
            sun.color = baseSunColor;
            sun.intensity = baseSunIntensity;
        }
        if (surfaceCamera != null)
        {
            surfaceCamera.clearFlags = baseClearFlags;
            surfaceCamera.backgroundColor = baseBackgroundColor;
        }
        if (cameraFillLight != null)
        {
            cameraFillLight.range = baseCameraFillRange;
            cameraFillLight.intensity = baseCameraFillIntensity;
        }
        if (atmosphereShell != null)
            Destroy(atmosphereShell);
        if (surfaceSkyMaterial != null)
            Destroy(surfaceSkyMaterial);
    }
}
