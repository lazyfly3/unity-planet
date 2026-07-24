using UnityEngine;

public enum PlanetOceanRenderMode
{
    Orbital,
    Surface,
    Laboratory
}

[DisallowMultipleComponent]
public sealed class ProceduralPlanetOcean : MonoBehaviour, IPlanetWaterSampler
{
    GameObject oceanObject;
    Mesh runtimeMesh;
    Material runtimeMaterial;
    Vector3 localCenter;
    GalaxyPlanetDefinition definition;
    PlanetLowPolyVisualProfile visual;
    PlanetCelestialProfile celestial;
    PlanetTerrainSettings terrain;
    float oceanRadius;
    float maximumVisualWaveHeight;

    public float OceanRadius => oceanRadius;
    public float MaximumVisualWaveHeight => maximumVisualWaveHeight;
    public PlanetOceanRenderMode RenderMode { get; private set; }

    void OnEnable() => PlanetWaterRegistry.Register(this);
    void OnDisable() => PlanetWaterRegistry.Unregister(this);

    public void Configure(
        GalaxyPlanetDefinition planetDefinition,
        Vector3 centerLocal,
        int resolution = 48,
        PlanetOceanRenderMode renderMode = PlanetOceanRenderMode.Orbital)
    {
        Cleanup();
        definition = planetDefinition;
        localCenter = centerLocal;
        RenderMode = renderMode;
        if (definition == null)
            return;

        visual = definition.lowPolyVisual != null
            ? definition.lowPolyVisual.Clone()
            : null;
        if (visual == null)
            return;
        visual.ClampValues();
        if (!visual.oceanEnabled)
            return;

        celestial = (definition.celestial
            ?? PlanetCelestialProfile.CreateCompatibleDefault()).Clone();
        terrain = definition.terrain ?? new PlanetTerrainSettings();
        oceanRadius = CalculateOceanRadius(celestial, visual);
        maximumVisualWaveHeight = CalculateMaximumWaveHeight(celestial, visual);
        runtimeMesh = PlanetLodMeshBuilder.BuildOcean(
            oceanRadius,
            Mathf.Clamp(resolution, 8, 128));

        string shaderName = renderMode == PlanetOceanRenderMode.Surface
            ? "VoxelPlanet/ProceduralOceanSurface"
            : "VoxelPlanet/ProceduralOcean";
        Shader shader = Shader.Find(shaderName);
        if (shader == null && renderMode == PlanetOceanRenderMode.Surface)
            shader = Shader.Find("VoxelPlanet/ProceduralOcean");
        if (shader == null)
        {
            Debug.LogError($"ProceduralPlanetOcean: missing {shaderName} shader.", this);
            return;
        }

        runtimeMaterial = new Material(shader)
        {
            name = $"ProceduralOcean_{definition.planetId}",
            hideFlags = HideFlags.DontSave
        };
        ApplyMaterialProperties();

        oceanObject = new GameObject("RuntimeProceduralOcean")
        {
            hideFlags = HideFlags.DontSave
        };
        oceanObject.transform.SetParent(transform, false);
        oceanObject.transform.localPosition = localCenter;
        MeshFilter filter = oceanObject.AddComponent<MeshFilter>();
        MeshRenderer renderer = oceanObject.AddComponent<MeshRenderer>();
        filter.sharedMesh = runtimeMesh;
        renderer.sharedMaterial = runtimeMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = true;
        ApplyCenter();
    }

    public bool TrySample(Vector3 worldPosition, out WaterSample sample)
    {
        sample = default;
        if (definition == null || visual == null || !visual.oceanEnabled || oceanRadius <= 0f)
            return false;

        Vector3 center = transform.TransformPoint(localCenter);
        Vector3 offset = worldPosition - center;
        float radius = offset.magnitude;
        if (radius <= 0.001f)
            return false;
        Vector3 direction = offset / radius;
        float terrainRadius = celestial.radius + VoxelQuadSphereTerrain.GetSurfaceNoise(
            direction * celestial.radius,
            definition.seed,
            terrain);
        float waterDepth = oceanRadius - terrainRadius;
        if (waterDepth <= 0.05f)
            return false;

        float signedDistance = radius - oceanRadius;
        float samplingMargin = Mathf.Max(6f, maximumVisualWaveHeight + 2f);
        if (signedDistance > samplingMargin || signedDistance < -waterDepth - 2f)
            return false;

        sample = new WaterSample
        {
            surfacePoint = center + direction * oceanRadius,
            surfaceNormal = direction,
            flowVelocity = Vector3.zero,
            depth = waterDepth,
            signedDistance = signedDistance,
            tint = Color.Lerp(visual.deepOceanColor, visual.shallowOceanColor, 0.28f),
            kind = PlanetWaterKind.Ocean
        };
        return true;
    }

    public static float CalculateOceanRadius(
        PlanetCelestialProfile celestialProfile,
        PlanetLowPolyVisualProfile visualProfile)
    {
        PlanetCelestialProfile body = celestialProfile
            ?? PlanetCelestialProfile.CreateCompatibleDefault();
        return body.radius
            + (visualProfile != null ? visualProfile.oceanLevel : 0f)
            * Mathf.Max(1f, body.maximumTerrainElevation);
    }

    public static float CalculateMaximumWaveHeight(
        PlanetCelestialProfile celestialProfile,
        PlanetLowPolyVisualProfile visualProfile)
    {
        PlanetCelestialProfile body = celestialProfile
            ?? PlanetCelestialProfile.CreateCompatibleDefault();
        float strength = visualProfile != null
            ? Mathf.Clamp01(visualProfile.oceanWaveStrength)
            : 0f;
        return Mathf.Lerp(
            0.08f,
            Mathf.Max(0.18f, body.maximumTerrainElevation * 0.012f),
            strength);
    }

    void ApplyMaterialProperties()
    {
        runtimeMaterial.SetColor("_DeepColor", visual.deepOceanColor);
        runtimeMaterial.SetColor("_ShallowColor", visual.shallowOceanColor);
        runtimeMaterial.SetFloat("_Smoothness", visual.oceanSmoothness);
        runtimeMaterial.SetFloat("_WaveStrength", visual.oceanWaveStrength);
        runtimeMaterial.SetFloat("_WaveScale", visual.oceanWaveScale);
        runtimeMaterial.SetFloat("_WaveSpeed", visual.oceanWaveSpeed);
        runtimeMaterial.SetFloat("_WaveHeight", maximumVisualWaveHeight);
        runtimeMaterial.SetFloat("_NormalStrength", visual.oceanNormalStrength);
        runtimeMaterial.SetFloat("_FoamStrength", visual.oceanFoamStrength);
        runtimeMaterial.SetFloat("_RefractionStrength", visual.oceanRefractionStrength);
        runtimeMaterial.SetFloat("_Opacity", visual.oceanOpacity);
        runtimeMaterial.SetFloat("_OceanRadius", oceanRadius);
    }

    void LateUpdate() => ApplyCenter();

    void ApplyCenter()
    {
        if (runtimeMaterial == null)
            return;
        Vector3 center = transform.TransformPoint(localCenter);
        runtimeMaterial.SetVector(
            "_PlanetCenter",
            new Vector4(center.x, center.y, center.z, 1f));
    }

    void Cleanup()
    {
        if (oceanObject != null)
            DestroyObject(oceanObject);
        if (runtimeMesh != null)
            DestroyObject(runtimeMesh);
        if (runtimeMaterial != null)
            DestroyObject(runtimeMaterial);
        oceanObject = null;
        runtimeMesh = null;
        runtimeMaterial = null;
        oceanRadius = 0f;
        maximumVisualWaveHeight = 0f;
    }

    static void DestroyObject(Object value)
    {
        if (value == null)
            return;
        if (Application.isPlaying)
            Destroy(value);
        else
            DestroyImmediate(value);
    }

    void OnDestroy()
    {
        PlanetWaterRegistry.Unregister(this);
        Cleanup();
    }
}
