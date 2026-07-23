using UnityEngine;

[DefaultExecutionOrder(750)]
[DisallowMultipleComponent]
public sealed class PlanetAtmosphereVisualController : MonoBehaviour
{
    Transform planetCenter;
    PlanetCelestialProfile profile;
    Renderer targetRenderer;
    Material runtimeMaterial;
    bool capturedFog;
    bool originalFog;
    Color originalFogColor;
    float originalFogDensity;
    FogMode originalFogMode;

    public void Configure(Transform center, PlanetCelestialProfile value, Renderer renderer)
    {
        planetCenter = center;
        profile = (value ?? PlanetCelestialProfile.CreateCompatibleDefault()).Clone();
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
        AtmosphereVisualProfile visual = profile.atmosphereVisual ?? new AtmosphereVisualProfile();
        Vector3 center = planetCenter.position;
        runtimeMaterial.SetVector("_PlanetCenter", new Vector4(center.x, center.y, center.z, 1f));
        runtimeMaterial.SetFloat("_PlanetRadius", profile.radius);
        runtimeMaterial.SetFloat("_AtmosphereRadius", profile.radius + profile.atmosphereTopAltitude);
        runtimeMaterial.SetColor("_HorizonColor", visual.horizonColor);
        runtimeMaterial.SetColor("_ZenithColor", visual.zenithColor);
        runtimeMaterial.SetColor("_SunsetColor", visual.sunsetColor);
        runtimeMaterial.SetFloat("_Scattering", visual.scatteringStrength);
        runtimeMaterial.SetFloat("_CloudCoverage", visual.cloudCoverage);
        Vector3 axis = profile.rotationAxis;
        runtimeMaterial.SetVector("_RotationAxis", new Vector4(axis.x, axis.y, axis.z, 0f));
        runtimeMaterial.SetFloat("_CloudSpeed", visual.cloudRotationMultiplier / Mathf.Max(1f, profile.rotationPeriod));
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
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = Color.Lerp(nightColor, dayColor, daylight);
        float atmosphericFog = Mathf.Lerp(0.00008f, 0.0022f, density01)
            * Mathf.Max(0.25f, visual.scatteringStrength);
        RenderSettings.fogDensity = Mathf.Max(RenderSettings.fogDensity, atmosphericFog);
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
    }

    void RestoreFog()
    {
        if (!capturedFog)
            return;
        RenderSettings.fog = originalFog;
        RenderSettings.fogColor = originalFogColor;
        RenderSettings.fogDensity = originalFogDensity;
        RenderSettings.fogMode = originalFogMode;
        capturedFog = false;
    }

    void OnDestroy()
    {
        RestoreFog();
        if (runtimeMaterial != null)
            Destroy(runtimeMaterial);
    }
}
