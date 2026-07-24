using UnityEngine;

[DefaultExecutionOrder(750)]
[DisallowMultipleComponent]
public sealed class PlanetAtmosphereVisualController : MonoBehaviour
{
    Transform planetCenter;
    PlanetCelestialProfile profile;
    PlanetLowPolyVisualProfile visualProfile;
    Renderer targetRenderer;
    Material runtimeMaterial;
    bool capturedFog;
    bool originalFog;
    Color originalFogColor;
    float originalFogDensity;
    FogMode originalFogMode;
    float originalFogStartDistance;
    float originalFogEndDistance;

    public void Configure(Transform center, PlanetCelestialProfile value, Renderer renderer)
    {
        Configure(center, value, null, renderer);
    }

    public void Configure(Transform center, PlanetCelestialProfile value,
        PlanetLowPolyVisualProfile visual)
    {
        Configure(center, value, visual, GetComponent<Renderer>());
    }

    public void Configure(Transform center, PlanetCelestialProfile value,
        PlanetLowPolyVisualProfile visual, Renderer renderer)
    {
        planetCenter = center;
        profile = (value ?? PlanetCelestialProfile.CreateCompatibleDefault()).Clone();
        visualProfile = visual != null ? visual.Clone() : null;
        targetRenderer = renderer != null ? renderer : GetComponent<Renderer>();
        if (targetRenderer == null)
            return;

        Shader shader = Shader.Find("VoxelPlanet/AtmosphereShell") ?? Shader.Find("Sprites/Default");
        runtimeMaterial = new Material(shader) { name = "RuntimePlanetAtmosphere" };
        targetRenderer.sharedMaterial = runtimeMaterial;
        ApplyMaterialProperties();
    }

    void LateUpdate()
    {
        if (profile == null || planetCenter == null)
            return;
        ApplyMaterialProperties();
        UpdateCameraFog();
    }

    void ApplyMaterialProperties()
    {
        if (runtimeMaterial == null || profile == null || planetCenter == null)
            return;
        AtmosphereVisualProfile atmosphere = profile.atmosphereVisual ?? new AtmosphereVisualProfile();
        Vector3 center = planetCenter.position;
        runtimeMaterial.SetVector("_PlanetCenter", new Vector4(center.x, center.y, center.z, 1f));
        runtimeMaterial.SetFloat("_PlanetRadius", profile.radius);
        runtimeMaterial.SetFloat("_AtmosphereRadius", profile.radius + profile.atmosphereTopAltitude);
        runtimeMaterial.SetColor("_HorizonColor",
            visualProfile != null ? visualProfile.horizonColor : atmosphere.horizonColor);
        runtimeMaterial.SetColor("_ZenithColor",
            visualProfile != null ? visualProfile.zenithColor : atmosphere.zenithColor);
        runtimeMaterial.SetColor("_SunsetColor",
            visualProfile != null ? visualProfile.sunsetColor : atmosphere.sunsetColor);
        runtimeMaterial.SetFloat("_Scattering",
            visualProfile != null ? visualProfile.atmosphereThickness : atmosphere.scatteringStrength);
        Vector3 axis = profile.rotationAxis;
        runtimeMaterial.SetVector("_RotationAxis", new Vector4(axis.x, axis.y, axis.z, 0f));
    }

    void UpdateCameraFog()
    {
        Camera camera = Camera.main;
        if (camera == null)
            return;
        float altitude = Vector3.Distance(camera.transform.position, planetCenter.position) - profile.radius;
        bool insideAtmosphere = profile.HasAtmosphere && altitude < profile.atmosphereTopAltitude;
        if (!insideAtmosphere)
        {
            RestoreFog();
            return;
        }

        CaptureFog();
        float density01 = Mathf.Exp(-Mathf.Max(0f, altitude) / profile.atmosphereScaleHeight);
        AtmosphereVisualProfile visual = profile.atmosphereVisual ?? new AtmosphereVisualProfile();
        Vector3 radialUp = (camera.transform.position - planetCenter.position).normalized;
        float daylight = PlanetSurfaceDayNightController.SampleDaylight(radialUp);
        Color dayColor = Color.Lerp(visual.horizonColor, visual.zenithColor, 0.35f);
        Color nightColor = Color.Lerp(visual.zenithColor, Color.black, 0.78f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = visualProfile != null ? FogMode.Linear : FogMode.ExponentialSquared;
        RenderSettings.fogColor = Color.Lerp(nightColor, dayColor, daylight);
        float atmosphericFog = Mathf.Lerp(0.00004f, 0.0011f, density01)
            * Mathf.Max(0.25f, visual.scatteringStrength);
        if (visualProfile != null)
        {
            RenderSettings.fogStartDistance = 100f;
            RenderSettings.fogEndDistance = Mathf.Lerp(900f, 360f, density01 * visualProfile.hazeStrength);
        }
        else
        {
            RenderSettings.fogDensity = Mathf.Max(RenderSettings.fogDensity, atmosphericFog);
        }
        if (camera.clearFlags == CameraClearFlags.SolidColor)
            camera.backgroundColor = RenderSettings.fogColor;
    }

    void CaptureFog()
    {
        if (capturedFog)
            return;
        capturedFog = true;
        originalFog = RenderSettings.fog;
        originalFogColor = RenderSettings.fogColor;
        originalFogDensity = RenderSettings.fogDensity;
        originalFogMode = RenderSettings.fogMode;
        originalFogStartDistance = RenderSettings.fogStartDistance;
        originalFogEndDistance = RenderSettings.fogEndDistance;
    }

    void RestoreFog()
    {
        if (!capturedFog)
            return;
        RenderSettings.fog = originalFog;
        RenderSettings.fogColor = originalFogColor;
        RenderSettings.fogDensity = originalFogDensity;
        RenderSettings.fogMode = originalFogMode;
        RenderSettings.fogStartDistance = originalFogStartDistance;
        RenderSettings.fogEndDistance = originalFogEndDistance;
        capturedFog = false;
    }

    void OnDestroy()
    {
        RestoreFog();
        if (runtimeMaterial != null)
            Destroy(runtimeMaterial);
    }
}
