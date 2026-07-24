using System;
using UnityEngine;

public enum ProceduralPlanetLabTemplate
{
    TemperateOcean,
    CrimsonOcean,
    Desert,
    Frozen,
    Crystal
}

[Serializable]
public sealed class ProceduralPlanetLabPreviewSettings
{
    public Color backgroundColor = Color.black;
    public Color ambientColor = new Color(0.16f, 0.18f, 0.22f, 1f);
    public Color sunColor = new Color(1f, 0.94f, 0.82f, 1f);
    public Vector3 sunEuler = new Vector3(35f, -35f, 0f);
    [Range(0f, 4f)] public float sunIntensity = 1.25f;
    public Color fillColor = new Color(0.36f, 0.52f, 1f, 1f);
    public Vector3 fillEuler = new Vector3(18f, 145f, 0f);
    [Range(0f, 2f)] public float fillIntensity = 0.32f;
    [Range(-180f, 180f)] public float cameraYaw = 35f;
    [Range(-85f, 85f)] public float cameraPitch = 20f;
    [Range(1.35f, 8f)] public float cameraDistance = 3.45f;
    public bool autoRotate;
    [Range(-45f, 45f)] public float rotationSpeed = 8f;

    public ProceduralPlanetLabPreviewSettings Clone()
        => (ProceduralPlanetLabPreviewSettings)MemberwiseClone();

    public void ClampValues()
    {
        sunIntensity = Mathf.Clamp(sunIntensity, 0f, 4f);
        fillIntensity = Mathf.Clamp(fillIntensity, 0f, 2f);
        cameraYaw = Mathf.Repeat(cameraYaw + 180f, 360f) - 180f;
        cameraPitch = Mathf.Clamp(cameraPitch, -85f, 85f);
        cameraDistance = Mathf.Clamp(cameraDistance, 1.35f, 8f);
        rotationSpeed = Mathf.Clamp(rotationSpeed, -45f, 45f);
    }
}

[CreateAssetMenu(
    fileName = "ProceduralPlanetPreset",
    menuName = "Voxel Planet/Procedural Planet Preset")]
public sealed class ProceduralPlanetPreset : ScriptableObject
{
    public const int CurrentVersion = 1;

    [HideInInspector] public int version = CurrentVersion;
    public string displayName = "Temperate Ocean";
    public ProceduralPlanetLabTemplate template = ProceduralPlanetLabTemplate.TemperateOcean;

    [Header("Basic")]
    public int seed = 7319;
    [Range(20f, 2000f)] public float radius = 100f;
    [Range(2f, 240f)] public float maximumTerrainElevation = 16f;
    [Range(16, 128)] public int previewResolution = 64;

    [Header("Shape")]
    public PlanetTerrainSettings terrain = new PlanetTerrainSettings
    {
        shapeVersion = PlanetTerrainSettings.CurrentShapeVersion,
        continentScale = 0.018f,
        continentHeight = 11f,
        detailScale = 0.065f,
        detailHeight = 3.6f,
        ridgeHeight = 7f,
        continentThreshold = 0.5f,
        continentWarp = 0.6f,
        continentSharpness = 1.25f,
        mountainMask = 0.52f,
        oceanFloorDepth = 0.58f,
        terraceStrength = 0.08f,
        generateCaves = false
    };

    [Header("Shading And Ocean")]
    public PlanetLowPolyVisualProfile visual = new PlanetLowPolyVisualProfile();

    [Header("Preview")]
    public ProceduralPlanetLabPreviewSettings preview =
        new ProceduralPlanetLabPreviewSettings();

    public GalaxyPlanetDefinition CloneDefinition()
    {
        ClampValues();
        PlanetTerrainSettings terrainCopy = terrain != null
            ? terrain.Clone()
            : new PlanetTerrainSettings();
        PlanetLowPolyVisualProfile visualCopy = visual != null
            ? visual.Clone()
            : new PlanetLowPolyVisualProfile();
        terrainCopy.ClampValues();
        visualCopy.ClampValues();

        var celestial = new PlanetCelestialProfile
        {
            surfaceGenerationMode = PlanetSurfaceGenerationMode.LegacyFullSphere,
            radius = radius,
            surfaceGravity = 9.8f,
            gravitationalParameter = 9.8f * radius * radius,
            rotationAxis = Vector3.up,
            rotationPeriod = 180f,
            atmosphereSurfaceDensity = 0f,
            atmosphereScaleHeight = 1f,
            atmosphereTopAltitude = 0f,
            maximumTerrainElevation = maximumTerrainElevation,
            editableDepth = Mathf.Max(16f, maximumTerrainElevation * 2f),
            atmosphereVisual = new AtmosphereVisualProfile
            {
                kind = PlanetAtmosphereKind.None
            }
        };

        return new GalaxyPlanetDefinition
        {
            planetId = "planet-lab-" + seed,
            displayName = string.IsNullOrWhiteSpace(displayName)
                ? "Procedural Planet"
                : displayName,
            seed = seed,
            climate = PlanetClimate.Barren,
            mapColor = visualCopy.lowlandColor,
            surfaceColor = visualCopy.highlandColor,
            rockColor = visualCopy.rockColor,
            hasExplicitPalette = true,
            tintMapIcon = false,
            terrain = terrainCopy,
            spawnHarvestableResources = false,
            celestial = celestial,
            lowPolyVisual = visualCopy
        };
    }

    public void ResetToTemplate()
    {
        ApplyTemplate(template);
    }

    public void ApplyTemplate(ProceduralPlanetLabTemplate value)
    {
        template = value;
        version = CurrentVersion;
        radius = 100f;
        maximumTerrainElevation = 16f;
        previewResolution = 64;
        terrain = CreateBaseTerrain();
        visual = new PlanetLowPolyVisualProfile();
        preview = new ProceduralPlanetLabPreviewSettings();

        switch (value)
        {
            case ProceduralPlanetLabTemplate.CrimsonOcean:
                displayName = "Crimson Ocean";
                seed = 29411;
                terrain.continentThreshold = 0.515f;
                terrain.continentWarp = 0.82f;
                terrain.continentSharpness = 1.45f;
                terrain.ridgeHeight = 8.5f;
                terrain.oceanFloorDepth = 0.72f;
                visual.lowlandColor = new Color(0.48f, 0.22f, 0.16f);
                visual.highlandColor = new Color(0.62f, 0.35f, 0.25f);
                visual.cliffColor = new Color(0.08f, 0.12f, 0.13f);
                visual.rockColor = new Color(0.12f, 0.18f, 0.19f);
                visual.accentColor = new Color(0.78f, 0.35f, 0.3f);
                visual.shoreColor = new Color(0.75f, 0.48f, 0.4f);
                visual.snowColor = new Color(0.86f, 0.84f, 0.72f);
                visual.deepOceanColor = new Color(0.18f, 0.015f, 0.06f);
                visual.shallowOceanColor = new Color(0.72f, 0.05f, 0.18f);
                visual.oceanLevel = 0.015f;
                visual.snowLine = 0.75f;
                preview.sunColor = new Color(1f, 0.82f, 0.7f);
                break;

            case ProceduralPlanetLabTemplate.Desert:
                displayName = "Desert";
                seed = 8831;
                terrain.continentThreshold = 0.455f;
                terrain.continentWarp = 0.42f;
                terrain.continentSharpness = 0.92f;
                terrain.detailHeight = 2.3f;
                terrain.ridgeHeight = 10f;
                terrain.mountainMask = 0.58f;
                terrain.terraceStrength = 0.34f;
                visual.oceanEnabled = false;
                visual.lowlandColor = new Color(0.58f, 0.27f, 0.09f);
                visual.highlandColor = new Color(0.82f, 0.55f, 0.22f);
                visual.cliffColor = new Color(0.3f, 0.13f, 0.06f);
                visual.rockColor = new Color(0.44f, 0.22f, 0.1f);
                visual.accentColor = new Color(0.96f, 0.68f, 0.26f);
                visual.shoreColor = new Color(0.72f, 0.47f, 0.2f);
                visual.snowAmount = 0f;
                visual.macroVariation = 0.24f;
                preview.sunColor = new Color(1f, 0.78f, 0.52f);
                preview.sunIntensity = 1.55f;
                preview.ambientColor = new Color(0.19f, 0.11f, 0.08f);
                break;

            case ProceduralPlanetLabTemplate.Frozen:
                displayName = "Frozen";
                seed = 51109;
                terrain.continentThreshold = 0.485f;
                terrain.continentWarp = 0.7f;
                terrain.ridgeHeight = 9.5f;
                terrain.mountainMask = 0.44f;
                visual.lowlandColor = new Color(0.22f, 0.36f, 0.42f);
                visual.highlandColor = new Color(0.62f, 0.72f, 0.75f);
                visual.cliffColor = new Color(0.18f, 0.28f, 0.34f);
                visual.rockColor = new Color(0.3f, 0.4f, 0.44f);
                visual.accentColor = new Color(0.5f, 0.88f, 0.95f);
                visual.shoreColor = new Color(0.66f, 0.86f, 0.9f);
                visual.snowColor = new Color(0.94f, 0.98f, 1f);
                visual.deepOceanColor = new Color(0.015f, 0.08f, 0.16f);
                visual.shallowOceanColor = new Color(0.08f, 0.48f, 0.62f);
                visual.snowLine = 0.26f;
                visual.snowAmount = 0.94f;
                visual.oceanSmoothness = 0.95f;
                visual.oceanWaveStrength = 0.12f;
                preview.sunColor = new Color(0.78f, 0.88f, 1f);
                preview.fillColor = new Color(0.26f, 0.55f, 1f);
                break;

            case ProceduralPlanetLabTemplate.Crystal:
                displayName = "Crystal";
                seed = 40427;
                maximumTerrainElevation = 21f;
                terrain.continentThreshold = 0.535f;
                terrain.continentWarp = 1.05f;
                terrain.continentSharpness = 1.9f;
                terrain.detailHeight = 1.8f;
                terrain.ridgeHeight = 15f;
                terrain.mountainMask = 0.38f;
                terrain.terraceStrength = 0.56f;
                visual.lowlandColor = new Color(0.08f, 0.18f, 0.25f);
                visual.highlandColor = new Color(0.18f, 0.55f, 0.68f);
                visual.cliffColor = new Color(0.08f, 0.08f, 0.16f);
                visual.rockColor = new Color(0.18f, 0.12f, 0.32f);
                visual.accentColor = new Color(0.6f, 0.18f, 0.9f);
                visual.shoreColor = new Color(0.18f, 0.72f, 0.8f);
                visual.snowColor = new Color(0.68f, 0.92f, 1f);
                visual.deepOceanColor = new Color(0.025f, 0.02f, 0.12f);
                visual.shallowOceanColor = new Color(0.16f, 0.55f, 0.82f);
                visual.oceanLevel = -0.025f;
                visual.snowLine = 0.78f;
                visual.snowAmount = 0.4f;
                visual.facetStrength = 0.96f;
                visual.lightingBands = 3;
                visual.macroVariation = 0.28f;
                preview.sunColor = new Color(0.72f, 0.88f, 1f);
                preview.fillColor = new Color(0.65f, 0.24f, 1f);
                preview.fillIntensity = 0.5f;
                break;

            default:
                displayName = "Temperate Ocean";
                seed = 7319;
                visual.lowlandColor = new Color(0.12f, 0.42f, 0.12f);
                visual.highlandColor = new Color(0.45f, 0.68f, 0.2f);
                visual.cliffColor = new Color(0.2f, 0.16f, 0.1f);
                visual.rockColor = new Color(0.28f, 0.25f, 0.18f);
                visual.accentColor = new Color(0.6f, 0.82f, 0.22f);
                visual.shoreColor = new Color(0.78f, 0.68f, 0.38f);
                visual.snowColor = new Color(0.94f, 0.95f, 0.86f);
                visual.deepOceanColor = new Color(0.01f, 0.09f, 0.2f);
                visual.shallowOceanColor = new Color(0.02f, 0.48f, 0.62f);
                visual.oceanLevel = 0.005f;
                visual.snowLine = 0.72f;
                visual.snowAmount = 0.65f;
                break;
        }

        ClampValues();
    }

    public void ClampValues()
    {
        version = CurrentVersion;
        radius = Mathf.Clamp(radius, 20f, 2000f);
        maximumTerrainElevation = Mathf.Clamp(
            maximumTerrainElevation,
            2f,
            Mathf.Max(2f, radius * 0.45f));
        previewResolution = SnapResolution(previewResolution);
        terrain = terrain ?? CreateBaseTerrain();
        visual = visual ?? new PlanetLowPolyVisualProfile();
        preview = preview ?? new ProceduralPlanetLabPreviewSettings();
        terrain.shapeVersion = PlanetTerrainSettings.CurrentShapeVersion;
        terrain.generateCaves = false;
        terrain.ClampValues();
        visual.ClampValues();
        preview.ClampValues();
    }

    public static int SnapResolution(int value)
    {
        if (value >= 112)
            return 128;
        if (value >= 80)
            return 96;
        return 64;
    }

    static PlanetTerrainSettings CreateBaseTerrain()
    {
        return new PlanetTerrainSettings
        {
            shapeVersion = PlanetTerrainSettings.CurrentShapeVersion,
            continentScale = 0.018f,
            continentHeight = 11f,
            detailScale = 0.065f,
            detailHeight = 3.6f,
            ridgeHeight = 7f,
            continentThreshold = 0.5f,
            continentWarp = 0.6f,
            continentSharpness = 1.25f,
            mountainMask = 0.52f,
            oceanFloorDepth = 0.58f,
            terraceStrength = 0.08f,
            surfaceLayerDepth = 1f,
            stoneDepth = 4f,
            generateCaves = false
        };
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        ClampValues();
    }
#endif
}
