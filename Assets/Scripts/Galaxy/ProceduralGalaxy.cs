using System;
using System.Collections.Generic;
using UnityEngine;

public enum GalaxyMode
{
    LegacyFinite = 0,
    InfiniteProcedural = 1,
    Interstellar3DProcedural = 2
}

public enum GalaxyShipFacing
{
    Up = 0,
    Right = 1,
    Down = 2,
    Left = 3
}

[Serializable]
public struct GalaxyCoordinate : IEquatable<GalaxyCoordinate>
{
    public long x;
    public long y;

    public static readonly GalaxyCoordinate Zero = new GalaxyCoordinate(0L, 0L);

    public GalaxyCoordinate(long x, long y)
    {
        this.x = x;
        this.y = y;
    }

    public GalaxyCoordinate Offset(int deltaX, int deltaY)
    {
return new GalaxyCoordinate(SaturatingAdd(x, deltaX), SaturatingAdd(y, deltaY));

}

    public bool Equals(GalaxyCoordinate other) => x == other.x && y == other.y;
    public override bool Equals(object value) => value is GalaxyCoordinate other && Equals(other);
    public override int GetHashCode() => unchecked((x.GetHashCode() * 397) ^ y.GetHashCode());
    public override string ToString() => $"{x}:{y}";

    public static bool operator ==(GalaxyCoordinate left, GalaxyCoordinate right) => left.Equals(right);
    public static bool operator !=(GalaxyCoordinate left, GalaxyCoordinate right) => !left.Equals(right);

    static long SaturatingAdd(long value, int delta)
    {
        if (delta > 0 && value > long.MaxValue - delta)
            return long.MaxValue;
        if (delta < 0 && value < long.MinValue - delta)
            return long.MinValue;
        return value + delta;
    }
}

[Serializable]
public struct GalaxyCoordinateDelta
{
    public int x;
    public int y;

    public GalaxyCoordinateDelta(int x, int y)
    {
        this.x = x;
        this.y = y;
    }
}

[Serializable]
public sealed class GalaxyResourceCatalogEntry
{
    public string resourceId;
    public HarvestableResource prefab;
}

[Serializable]
public sealed class GalaxyGeneratedResourceRecord
{
    public string catalogId;
    public int count;
    public int seedOffset;
    public float surfaceOffset;
    public float minimumSpacing;
    public float playerClearRadius;
    public int placementAttempts;
    public float clockwiseRotationDegrees;
    public bool randomizeYaw;
}

[Serializable]
public sealed class GalaxyGeneratedPlanetRecord
{
    public int formatVersion = 7;
    public int generatorVersion;
    public string planetId;
    public string displayName;
    public long coordinateX;
    public long coordinateY;
    public long coordinateZ;
    public bool usesInterstellarCoordinate;
    public string systemId;
    public int orbitIndex;
    public CelestialOrbitDefinition orbit;
    public int seed;
    public PlanetClimate climate;
    public Color mapColor;
    public Color surfaceColor;
    public Color rockColor;
    public string iconResourcePath;
    public PlanetTerrainSettings terrain;
    public List<GalaxyGeneratedResourceRecord> resources = new List<GalaxyGeneratedResourceRecord>();
    public PlanetRiverSettings rivers;
    public PlanetWeatherSettings weather;
    public PlanetCelestialProfile celestial;
    public PlanetLowPolyVisualProfile lowPolyVisual;
}

public sealed class ProceduralGalaxyGenerator
{
    public const int CurrentVersion = 2;
    const long MacroCellSize = 4L;

    static readonly string[] NameStarts =
    {
        "Astra", "Ceryn", "Dara", "Eos", "Helion", "Ilyr", "Kaelis", "Luma",
        "Neris", "Orin", "Pyra", "Quillon", "Rhea", "Solis", "Teth", "Veyra",
        "Xanthe", "Ymir", "Zeph"
    };

    static readonly string[] NameEnds =
    {
        "-I", "-II", " Prime", " Reach", " Minor", " Major", " Haven", " Rift",
        " Delta", " Verge", " Nova", " Echo"
    };

    static readonly string[] IconPaths =
    {
        "Galaxy/planet_amber", "Galaxy/planet_green", "Galaxy/planet_red",
        "Galaxy/planet_blue", "Galaxy/planet_violet"
    };

    readonly int worldSeed;
    readonly IReadOnlyList<GalaxyResourceCatalogEntry> resources;

    public ProceduralGalaxyGenerator(int worldSeed, IReadOnlyList<GalaxyResourceCatalogEntry> resources)
    {
        this.worldSeed = worldSeed;
        this.resources = resources ?? Array.Empty<GalaxyResourceCatalogEntry>();
    }

    public bool HasPlanet(GalaxyCoordinate coordinate)
    {
if (coordinate == GalaxyCoordinate.Zero)
            return true;

        long macroX = FloorDivide(coordinate.x, MacroCellSize);
        long macroY = FloorDivide(coordinate.y, MacroCellSize);
        GalaxyCoordinate candidate = GetMacroCellCandidate(macroX, macroY);
        if (candidate != coordinate)
            return false;

        // Keep the guaranteed origin separated from generated neighbours.
        return AbsDistanceFromZero(coordinate.x) >= 3L || AbsDistanceFromZero(coordinate.y) >= 3L;
    
}

    public GalaxyPlanetDefinition GeneratePlanet(GalaxyCoordinate coordinate)
    {
if (!HasPlanet(coordinate))
            return null;

        GalaxyPlanetDefinition definition = GeneratePlanetFromStableHash(
            HashCoordinate(worldSeed, coordinate, 0xA0761D6478BD642FUL),
            EncodePlanetId(coordinate),
            coordinate == GalaxyCoordinate.Zero);
        definition.coordinate = coordinate;
        return definition;

}

    public GalaxyPlanetDefinition GeneratePlanetFromStableHash(ulong stableHash, string planetId, bool isOrigin)
    {
        var random = new StableRandom(stableHash);
        float temperature = random.Value();
        float moisture = random.Value();
        float geology = random.Value();
        float atmosphere = random.Value();
        float crystal = random.Value();
        float hue = Mathf.Repeat(0.58f + temperature * 0.36f + crystal * 0.22f - moisture * 0.18f, 1f);
        float saturation = Mathf.Lerp(0.48f, 0.9f, Mathf.Max(geology, crystal));
        float value = Mathf.Lerp(0.58f, 0.92f, atmosphere);
        Color mapColor = Color.HSVToRGB(hue, saturation, value);
        Color surfaceColor = Color.Lerp(mapColor, temperature > 0.72f ? new Color(0.55f, 0.18f, 0.06f) : new Color(0.12f, 0.35f, 0.22f), 0.32f);
        surfaceColor.a = 1f;
        Color rockColor = Color.Lerp(surfaceColor, Color.black, Mathf.Lerp(0.48f, 0.72f, geology));
        rockColor.a = 1f;

        int seed = random.NextNonZeroInt();
        PlanetCelestialProfile celestial = CreateCelestialProfile(ref random, atmosphere);
        PlanetTerrainSettings terrain = CreateTerrain(
            ref random,
            temperature,
            moisture,
            geology);
        var definition = new GalaxyPlanetDefinition
        {
            planetId = planetId,
            displayName = CreateName(ref random, isOrigin),
            isProcedural = true,
            seed = seed,
            climate = PlanetClimateClassifier.Classify(temperature, moisture, geology, crystal),
            mapColor = mapColor,
            surfaceColor = surfaceColor,
            rockColor = rockColor,
            hasExplicitPalette = true,
            tintMapIcon = true,
            iconResourcePath = IconPaths[Mathf.Clamp((int)(hue * IconPaths.Length), 0, IconPaths.Length - 1)],
            terrain = terrain,
            // Interstellar PCG planets deliberately omit generated rivers. Existing
            // frozen definitions still load their original river data unchanged.
            rivers = CreateDisabledRivers(),
            weather = CreateWeather(ref random, temperature, moisture, geology, atmosphere, crystal),
            celestial = celestial,
            lowPolyVisual = CreateVisualProfile(
                seed,
                temperature,
                moisture,
                geology,
                atmosphere,
                crystal,
                surfaceColor,
                rockColor,
                celestial.atmosphereVisual,
                terrain)
        };
        definition.resourceSpawnSettings = CreateResources(ref random, geology, crystal);
        definition.spawnHarvestableResources = definition.resourceSpawnSettings.Count > 0;
        return definition;
    
}

    GalaxyCoordinate GetMacroCellCandidate(long macroX, long macroY)
    {
        ulong hash = HashCoordinate(worldSeed, new GalaxyCoordinate(macroX, macroY), 0xE7037ED1A0B428DBUL);
        long offsetX = (long)(hash & 1UL) + 1L;
        long offsetY = (long)((hash >> 1) & 1UL) + 1L;
        return new GalaxyCoordinate(macroX * MacroCellSize + offsetX, macroY * MacroCellSize + offsetY);
    }

    static PlanetRiverSettings CreateDisabledRivers()
    {
        return new PlanetRiverSettings
        {
            enabled = false,
            riverCount = 0
        };
    }

    static PlanetCelestialProfile CreateCelestialProfile(ref StableRandom random, float atmosphere)
    {
        double physicalRadius = Mathf.Lerp(1_500_000f, 10_000_000f, Mathf.Pow(random.Value(), 0.72f));
        double minimumDensity = 2500d;
        double maximumDensity = 7500d;
        double minimumGravity = 4d / 3d * Math.PI
            * PlanetPhysicalProfile.GravitationalConstant * physicalRadius * minimumDensity;
        double maximumGravity = 4d / 3d * Math.PI
            * PlanetPhysicalProfile.GravitationalConstant * physicalRadius * maximumDensity;
        double requestedGravity = Mathf.Lerp(0.15f, 1.8f, random.Value())
            * PhysicalConstants.StandardGravity;
        double physicalGravity = Math.Max(
            minimumGravity,
            Math.Min(maximumGravity, requestedGravity));
        double physicalDensity = physicalGravity * 3d
            / (4d * Math.PI * PlanetPhysicalProfile.GravitationalConstant * physicalRadius);
        double rotationPeriodSeconds = Mathf.Lerp(6f, 120f, random.Value()) * 3600d;
        Vector3 axis = new Vector3(
            random.Value() * 2f - 1f,
            Mathf.Lerp(0.35f, 1f, random.Value()),
            random.Value() * 2f - 1f).normalized;
        bool hasAtmosphere = atmosphere > 0.16f;
        float visualRotationPeriodSeconds = Mathf.Lerp(720f, 1500f, random.Value());
        var physical = new PlanetPhysicalProfile
        {
            radiusMeters = physicalRadius,
            meanDensityKgPerCubicMeter = physicalDensity,
            rotationPeriodSeconds = rotationPeriodSeconds,
            atmosphereSurfaceDensityKgPerCubicMeter = hasAtmosphere
                ? Mathf.Lerp(0.08f, 1.8f, atmosphere)
                : 0d,
            atmosphereScaleHeightMeters = hasAtmosphere
                ? Mathf.Lerp(5000f, 20_000f, random.Value())
                : 1d,
            atmosphereTopAltitudeMeters = hasAtmosphere
                ? Mathf.Lerp(50_000f, 300_000f, atmosphere)
                : 0d,
            visualExosphereAltitudeMeters = hasAtmosphere
                ? Mathf.Lerp(100_000f, 600_000f, atmosphere)
                : 0d
        };
        physical.ClampValues();
        var profile = new PlanetCelestialProfile
        {
            surfaceGenerationMode = PlanetSurfaceGenerationMode.StreamingLargeSphere,
            radius = PlanetCelestialProfile.LargePlanetRadius,
            surfaceGravity = (float)physical.surfaceGravity,
            rotationAxis = axis,
            rotationPeriod = visualRotationPeriodSeconds,
            atmosphereSurfaceDensity = hasAtmosphere ? Mathf.Lerp(0.18f, 1.2f, atmosphere) : 0f,
            atmosphereScaleHeight = hasAtmosphere ? Mathf.Lerp(95f, 150f, random.Value()) : 1f,
            atmosphereTopAltitude = hasAtmosphere ? Mathf.Lerp(700f, 900f, atmosphere) : 0f,
            maximumTerrainElevation = Mathf.Lerp(90f, 160f, random.Value()),
            editableDepth = 96f,
            rotationEpochSeconds = random.Value() * rotationPeriodSeconds,
            atmosphereVisual = CreateAtmosphereVisual(ref random, atmosphere, hasAtmosphere),
            physical = physical,
            presentation = new PlanetPresentationProfile
            {
                surfaceProxyRadius = PlanetCelestialProfile.LargePlanetRadius,
                atmosphereProxyTopAltitude = hasAtmosphere
                    ? Mathf.Lerp(700f, 900f, atmosphere)
                    : 0f,
                visualRotationPeriodSeconds = visualRotationPeriodSeconds,
                preferredOrbitalProxyDistance = 8000f
            }
        };
        profile.ClampValues();
        return profile;
    }

    static AtmosphereVisualProfile CreateAtmosphereVisual(
        ref StableRandom random,
        float atmosphere,
        bool hasAtmosphere)
    {
        if (!hasAtmosphere)
            return new AtmosphereVisualProfile { kind = PlanetAtmosphereKind.None, scatteringStrength = 0f, cloudCoverage = 0f };

        PlanetAtmosphereKind kind = atmosphere > 0.82f
            ? PlanetAtmosphereKind.Dense
            : atmosphere < 0.3f ? PlanetAtmosphereKind.Thin : PlanetAtmosphereKind.Temperate;
        float hue = Mathf.Lerp(0.52f, 0.64f, random.Value());
        Color horizon = Color.HSVToRGB(hue, Mathf.Lerp(0.35f, 0.72f, atmosphere), 1f);
        Color zenith = Color.Lerp(horizon, new Color(0.01f, 0.025f, 0.08f), 0.72f);
        return new AtmosphereVisualProfile
        {
            kind = kind,
            horizonColor = horizon,
            zenithColor = zenith,
            sunsetColor = Color.Lerp(new Color(1f, 0.18f, 0.04f), horizon, 0.18f),
            scatteringStrength = Mathf.Lerp(0.35f, 1.25f, atmosphere),
            cloudCoverage = Mathf.Lerp(0.08f, 0.72f, atmosphere),
            cloudRotationMultiplier = Mathf.Lerp(1.03f, 1.14f, random.Value())
        };
    }

    public static PlanetLowPolyVisualProfile CreateVisualProfile(
        int planetSeed,
        float temperature,
        float moisture,
        float geology,
        float atmosphere,
        float crystal,
        Color surfaceColor,
        Color rockColor,
        AtmosphereVisualProfile atmosphereVisual,
        PlanetTerrainSettings terrain = null)
    {
        ulong seed = Mix(unchecked((ulong)(uint)planetSeed) ^ 0xD6E8FEB86659FD93UL);
        var random = new StableRandom(seed);
        float heat = Mathf.Clamp01(temperature);
        float wet = Mathf.Clamp01(moisture);
        float geo = Mathf.Clamp01(geology);
        float air = Mathf.Clamp01(atmosphere);
        float crystals = Mathf.Clamp01(crystal);
        float tropical = wet * Mathf.SmoothStep(0.35f, 1f, heat);
        float forest = wet * (1f - Mathf.Abs(heat - 0.58f) * 1.3f);
        float desert = (1f - wet) * Mathf.Lerp(0.45f, 1f, heat);
        float tundra = (1f - heat) * Mathf.Lerp(0.35f, 1f, 1f - wet * 0.4f);
        float volcanic = geo * geo * Mathf.Lerp(0.55f, 1f, heat);

        Color lowland = Color.Lerp(surfaceColor, new Color(0.08f, 0.16f, 0.1f), 0.16f + wet * 0.08f);
        Color highland = Color.Lerp(surfaceColor, Color.white, Mathf.Lerp(0.08f, 0.24f, 1f - heat));
        Color cliff = Color.Lerp(rockColor, surfaceColor, 0.12f + (1f - geo) * 0.12f);
        Color.RGBToHSV(surfaceColor, out float surfaceHue, out _, out _);
        Color accent = Color.HSVToRGB(
            Mathf.Repeat(surfaceHue + 0.08f + random.Value() * 0.16f, 1f),
            Mathf.Lerp(0.42f, 0.82f, random.Value()),
            Mathf.Lerp(0.62f, 0.96f, random.Value()));
        AtmosphereVisualProfile sky = atmosphereVisual ?? new AtmosphereVisualProfile();
        float alienOcean = Mathf.Clamp01(volcanic * 0.62f + crystals * 0.44f);
        float oceanHue = Mathf.Repeat(
            Mathf.Lerp(0.56f, surfaceHue + 0.34f, alienOcean)
            + (random.Value() - 0.5f) * 0.18f,
            1f);
        Color deepOcean = Color.HSVToRGB(
            oceanHue,
            Mathf.Lerp(0.68f, 0.92f, alienOcean),
            Mathf.Lerp(0.16f, 0.34f, crystals));
        Color shallowOcean = Color.HSVToRGB(
            Mathf.Repeat(oceanHue + Mathf.Lerp(0.015f, 0.055f, random.Value()), 1f),
            Mathf.Lerp(0.48f, 0.86f, alienOcean),
            Mathf.Lerp(0.55f, 0.82f, air));
        bool hasOcean = air > 0.12f
            && (wet > 0.13f || random.Value() > 0.48f)
            && volcanic < 0.97f;
        float snowLine = Mathf.Lerp(0.38f, 0.9f, heat);
        snowLine = Mathf.Clamp01(snowLine + volcanic * 0.12f - tundra * 0.08f);

        var profile = new PlanetLowPolyVisualProfile
        {
            lowlandColor = lowland,
            highlandColor = highland,
            cliffColor = cliff,
            rockColor = rockColor,
            accentColor = accent,
            facetStrength = Mathf.Lerp(0.58f, 0.86f, random.Value()),
            lightingBands = random.Range(3, 6),
            macroColorSize = Mathf.Lerp(12f, 34f, random.Value()),
            cliffSlope = Mathf.Lerp(0.48f, 0.72f, 1f - geo * 0.45f),
            macroVariation = Mathf.Lerp(0.09f, 0.2f, random.Value()),
            oceanEnabled = hasOcean,
            deepOceanColor = deepOcean,
            shallowOceanColor = shallowOcean,
            shoreColor = Color.Lerp(
                lowland,
                desert > forest
                    ? new Color(0.72f, 0.48f, 0.31f, 1f)
                    : new Color(0.78f, 0.72f, 0.48f, 1f),
                0.62f),
            snowColor = Color.Lerp(Color.white, sky.horizonColor, alienOcean * 0.12f),
            oceanLevel = 0f,
            shoreWidth = Mathf.Lerp(0.008f, 0.035f, 1f - geo),
            snowLine = snowLine,
            snowAmount = Mathf.Clamp01((1f - heat) * 0.8f + geo * 0.28f),
            oceanSmoothness = Mathf.Lerp(0.72f, 0.94f, air),
            oceanWaveStrength = Mathf.Lerp(0.12f, 0.58f, air * (0.5f + wet * 0.5f)),
            forestWeight = Mathf.Clamp01(forest),
            desertWeight = Mathf.Clamp01(desert),
            tundraWeight = Mathf.Clamp01(tundra),
            volcanicWeight = Mathf.Clamp01(volcanic),
            crystalWeight = crystals,
            tropicalWeight = Mathf.Clamp01(tropical),
            decorationSeed = random.NextNonZeroInt(),
            decorationDensity = Mathf.Lerp(0.55f, 1.4f, Mathf.Max(wet, crystals * 0.7f)),
            decorationScale = Mathf.Lerp(0.82f, 1.28f, random.Value()),
            clusterStrength = Mathf.Lerp(0.25f, 0.85f, random.Value()),
            landmarkStyle = random.Range(0, 5),
            horizonColor = sky.horizonColor,
            zenithColor = sky.zenithColor,
            sunsetColor = sky.sunsetColor,
            groundAmbientColor = Color.Lerp(rockColor, Color.black, 0.68f),
            atmosphereThickness = Mathf.Clamp01(air),
            cloudCoverage = sky.cloudCoverage,
            hazeStrength = Mathf.Lerp(0.04f, 0.42f, air),
            cloudSeed = random.NextNonZeroInt()
        };
        profile.ClampValues();
        return profile;
    }

    List<HarvestableResourceSpawnSettings> CreateResources(ref StableRandom random, float geology, float crystal)
    {
        var available = new List<GalaxyResourceCatalogEntry>();
        foreach (GalaxyResourceCatalogEntry entry in resources)
            if (entry != null && entry.prefab != null && !string.IsNullOrWhiteSpace(entry.resourceId))
                available.Add(entry);

        var result = new List<HarvestableResourceSpawnSettings>();
        int count = Mathf.Min(available.Count, 1 + random.Range(0, Mathf.Clamp(Mathf.CeilToInt(geology * 3f + crystal * 2f), 1, 4)));
        for (int i = 0; i < count && available.Count > 0; i++)
        {
            int selected = random.Range(0, available.Count);
            GalaxyResourceCatalogEntry catalog = available[selected];
            available.RemoveAt(selected);
            result.Add(new HarvestableResourceSpawnSettings
            {
                prefab = catalog.prefab,
                count = random.Range(20, 61),
                seedOffset = random.NextNonZeroInt(),
                surfaceOffset = Mathf.Lerp(0.1f, 0.7f, random.Value()),
                minimumSpacing = Mathf.Lerp(2.5f, 7f, random.Value()),
                playerClearRadius = 7f,
                placementAttempts = random.Range(30, 81),
                clockwiseRotationDegrees = random.Range(0, 4) * 90f,
                randomizeYaw = true
            });
        }
        return result;
    }

    static PlanetTerrainSettings CreateTerrain(ref StableRandom random, float temperature, float moisture, float geology)
    {
        float oceanBias = Mathf.Clamp01(
            moisture * 0.52f
            + random.Value() * 0.38f
            + (1f - temperature) * 0.1f);
        return new PlanetTerrainSettings
        {
            shapeVersion = PlanetTerrainSettings.CurrentShapeVersion,
            continentScale = Mathf.Lerp(0.00055f, 0.0018f, random.Value()),
            continentHeight = Mathf.Lerp(55f, 125f, Mathf.Lerp(random.Value(), geology, 0.5f)),
            detailScale = Mathf.Lerp(0.004f, 0.014f, random.Value()),
            detailHeight = Mathf.Lerp(8f, 34f, geology),
            ridgeHeight = Mathf.Lerp(2f, 48f, geology * geology),
            continentThreshold = Mathf.Lerp(0.43f, 0.61f, oceanBias),
            continentWarp = Mathf.Lerp(0.2f, 1.15f, random.Value()),
            continentSharpness = Mathf.Lerp(0.72f, 1.8f, geology),
            mountainMask = Mathf.Lerp(0.38f, 0.66f, random.Value()),
            oceanFloorDepth = Mathf.Lerp(0.32f, 0.92f, geology),
            terraceStrength = random.Value() > 0.72f
                ? Mathf.Lerp(0.08f, 0.46f, geology)
                : 0f,
            surfaceLayerDepth = Mathf.Lerp(0.6f, 2.2f, moisture),
            stoneDepth = Mathf.Lerp(4f, 12f, 1f - temperature * 0.35f),
            generateCaves = geology > 0.18f,
            caveScale = Mathf.Lerp(0.012f, 0.045f, random.Value()),
            caveThreshold = Mathf.Lerp(0.79f, 0.56f, geology),
            caveSurfaceClearance = Mathf.Lerp(12f, 3f, geology)
        };
    }

    static PlanetWeatherSettings CreateWeather(
        ref StableRandom random,
        float temperature,
        float moisture,
        float geology,
        float atmosphere,
        float crystal)
    {
        var settings = new PlanetWeatherSettings
        {
            enabled = atmosphere > 0.12f,
            seedOffset = random.NextNonZeroInt(),
            transitionDuration = 20f
        };
        settings.presets.Add(Preset(WeatherType.Clear, "Clear", 3f, 180f, 420f, Mathf.Lerp(1f, 5f, atmosphere)));
        if (moisture > 0.35f && atmosphere > 0.3f)
        {
            settings.presets.Add(Preset(WeatherType.Rain, "Rain", 2.5f, 150f, 330f, 7f, moisture, 0.015f, 0.85f, 0.68f, 0.82f, moisture * 3f, 2200));
            if (moisture > 0.7f)
                settings.presets.Add(Preset(WeatherType.Thunderstorm, "Thunderstorm", 0.8f, 60f, 160f, 14f, 1f, 0.03f, 1f, 0.38f, 0.7f, 5f, 3600, true));
        }
        if (geology > 0.62f)
            settings.presets.Add(Preset(WeatherType.Ashfall, "Ashfall", 1.8f, 120f, 280f, 8f, 0f, 0.022f, 0.72f, 0.56f, 1f, 0f, 1700));
        if (moisture < 0.3f && temperature > 0.48f)
            settings.presets.Add(Preset(WeatherType.Sandstorm, "Dust Storm", 2f, 90f, 220f, 16f, 0f, 0.045f, 0.85f, 0.42f, 0.72f, 0f, 3200));
        if (crystal > 0.65f)
            settings.presets.Add(Preset(WeatherType.CrystalDust, "Crystal Dust", 1.4f, 100f, 240f, 9f, 0f, 0.018f, 0.62f, 0.62f, 0.92f, 0f, 1800));
        if (atmosphere > 0.55f && random.Value() > 0.5f)
            settings.presets.Add(Preset(WeatherType.DenseFog, "Dense Fog", 1.2f, 100f, 230f, 3f, 0f, 0.045f, 0.7f, 0.58f));
        settings.ClampValues();
        return settings;
    }

    static WeatherPreset Preset(
        WeatherType type, string name, float weight, float minDuration, float maxDuration, float wind,
        float precipitation = 0f, float fog = 0f, float cloud = 0f, float light = 1f,
        float traction = 1f, float rainfall = 0f, int particles = 0, bool lightning = false)
    {
        return new WeatherPreset
        {
            type = type,
            displayName = name,
            weight = weight,
            minimumDuration = minDuration,
            maximumDuration = maxDuration,
            minimumIntensity = 0.5f,
            maximumIntensity = 1f,
            windSpeed = wind,
            precipitation = precipitation,
            fogDensity = fog,
            cloudCoverage = cloud,
            lightMultiplier = light,
            wetness = precipitation,
            dustCoverage = type == WeatherType.Sandstorm || type == WeatherType.Ashfall ? 0.8f : 0f,
            crystalCoverage = type == WeatherType.CrystalDust ? 0.8f : 0f,
            tractionMultiplier = traction,
            riverRainfallRate = rainfall,
            particleCount = particles,
            lightning = lightning
        };
    }

    static string CreateName(ref StableRandom random, bool isOrigin)
    {
        string prefix = NameStarts[random.Range(0, NameStarts.Length)];
        string suffix = NameEnds[random.Range(0, NameEnds.Length)];
        return isOrigin ? "Aster" : prefix + suffix;
    }

    public static string EncodePlanetId(GalaxyCoordinate coordinate)
    {
return $"p_{ToBase36(ZigZag(coordinate.x))}_{ToBase36(ZigZag(coordinate.y))}";
    
}

    static ulong ZigZag(long value) => unchecked((ulong)((value << 1) ^ (value >> 63)));

    static string ToBase36(ulong value)
    {
        const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        if (value == 0UL)
            return "0";
        char[] buffer = new char[13];
        int index = buffer.Length;
        while (value > 0UL)
        {
            buffer[--index] = digits[(int)(value % 36UL)];
            value /= 36UL;
        }
        return new string(buffer, index, buffer.Length - index);
    }

    static long FloorDivide(long value, long divisor)
    {
        long quotient = value / divisor;
        long remainder = value % divisor;
        return remainder < 0L ? quotient - 1L : quotient;
    }

    static long AbsDistanceFromZero(long value)
    {
        return value == long.MinValue ? long.MaxValue : Math.Abs(value);
    }

    static ulong HashCoordinate(int seed, GalaxyCoordinate coordinate, ulong salt)
    {
        ulong value = unchecked((uint)seed) ^ salt;
        value = Mix(value ^ unchecked((ulong)coordinate.x));
        return Mix(value ^ RotateLeft(unchecked((ulong)coordinate.y), 32));
    }

    static ulong RotateLeft(ulong value, int shift) => (value << shift) | (value >> (64 - shift));

    static ulong Mix(ulong value)
    {
        value ^= value >> 30;
        value *= 0xBF58476D1CE4E5B9UL;
        value ^= value >> 27;
        value *= 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }

    struct StableRandom
    {
        ulong state;

        public StableRandom(ulong seed)
        {
            state = seed == 0UL ? 0x9E3779B97F4A7C15UL : seed;
        }

        public ulong Next()
        {
state += 0x9E3779B97F4A7C15UL;
            return Mix(state);
    
}

        public float Value() => (Next() >> 40) * (1f / 16777216f);

        public int Range(int minimum, int maximum)
        {
if (maximum <= minimum)
                return minimum;
            return minimum + (int)(Next() % (uint)(maximum - minimum));
    
}

        public int NextNonZeroInt()
        {
int value = unchecked((int)Next());
            return value == 0 ? 1 : value;
    
}
    }
}
