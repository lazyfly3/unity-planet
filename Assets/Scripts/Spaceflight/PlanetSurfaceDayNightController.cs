using UnityEngine;

[DefaultExecutionOrder(700)]
[DisallowMultipleComponent]
public sealed class PlanetSurfaceDayNightController : MonoBehaviour
{
    static Vector3 currentSunDirection = new Vector3(0.35f, 0.7f, 0.55f).normalized;

    VoxelQuadSphereWorld world;
    PlanetCelestialProfile profile;
    Light sun;
    GameObject atmosphereShell;
    Color baseSunColor = Color.white;
    float baseSunIntensity = 1f;
    Color baseAmbientColor;
    Material baseSkybox;
    Camera surfaceCamera;
    CameraClearFlags baseClearFlags;
    Color baseBackgroundColor;

    public static float SampleDaylight(Vector3 radialUp)
        => Mathf.Clamp01(Vector3.Dot(radialUp.normalized, currentSunDirection) * 0.55f + 0.5f);

    public void Configure(VoxelQuadSphereWorld valueWorld, PlanetCelestialProfile valueProfile)
    {
        world = valueWorld;
        profile = (valueProfile ?? PlanetCelestialProfile.CreateCompatibleDefault()).Clone();
        baseAmbientColor = RenderSettings.ambientLight;
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
            if (profile.HasAtmosphere)
            {
                RenderSettings.skybox = null;
                surfaceCamera.clearFlags = CameraClearFlags.SolidColor;
            }
        }
        BuildAtmosphereShell();
    }

    void LateUpdate()
    {
        if (world == null || profile == null)
            return;
        double time = GalaxyTravelManager.Instance?.UniverseTimeSeconds ?? Time.timeAsDouble;
        Quaternion bodyRotation = PlanetReferenceFrame.RotationAtTime(profile, time);
        Vector3 inertialLightDirection = new Vector3(0.35f, 0.7f, 0.55f).normalized;
        currentSunDirection = Quaternion.Inverse(bodyRotation) * inertialLightDirection;
        if (sun == null)
            return;

        sun.transform.rotation = Quaternion.LookRotation(-currentSunDirection, profile.rotationAxis);
        Transform player = world.PlayerSpawn;
        Vector3 playerUp = player != null
            ? (player.position - world.GetPlanetCenterWorld()).normalized
            : Vector3.up;
        float daylight = SampleDaylight(playerUp);
        float sunset = 1f - Mathf.Abs(Vector3.Dot(playerUp, currentSunDirection));
        AtmosphereVisualProfile visual = profile.atmosphereVisual ?? new AtmosphereVisualProfile();
        sun.intensity = baseSunIntensity * Mathf.Lerp(0.04f, 1f, daylight);
        sun.color = Color.Lerp(
            Color.Lerp(new Color(0.18f, 0.24f, 0.42f), visual.sunsetColor, sunset * daylight),
            baseSunColor,
            daylight);
        RenderSettings.ambientLight = Color.Lerp(
            new Color(0.015f, 0.025f, 0.06f),
            Color.Lerp(visual.zenithColor, Color.white, 0.28f),
            daylight);
    }

    void BuildAtmosphereShell()
    {
        if (!profile.HasAtmosphere || world == null)
            return;
        atmosphereShell = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        atmosphereShell.name = "RuntimeSurfaceAtmosphere";
        atmosphereShell.transform.SetParent(world.transform, false);
        atmosphereShell.transform.localPosition = Vector3.zero;
        atmosphereShell.transform.localScale = Vector3.one
            * ((profile.radius + profile.atmosphereTopAltitude) * 2f);
        Collider collider = atmosphereShell.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);
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
        RenderSettings.ambientLight = baseAmbientColor;
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
        if (atmosphereShell != null)
            Destroy(atmosphereShell);
    }
}
