using System;
using System.Collections.Generic;
using UnityEngine;

public enum GalaxyMode
{
    LegacyFinite = 0,
    InfiniteProcedural = 1
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
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(69, (int)deltaX, (int)deltaY);}
    try
    {
        return new GalaxyCoordinate(SaturatingAdd(x, deltaX), SaturatingAdd(y, deltaY));
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

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
    public int formatVersion = 1;
    public int generatorVersion;
    public string planetId;
    public string displayName;
    public long coordinateX;
    public long coordinateY;
    public int seed;
    public Color mapColor;
    public Color surfaceColor;
    public Color rockColor;
    public string iconResourcePath;
    public PlanetTerrainSettings terrain;
    public List<GalaxyGeneratedResourceRecord> resources = new List<GalaxyGeneratedResourceRecord>();
    public PlanetRiverSettings rivers;
    public PlanetWeatherSettings weather;
}

public sealed class ProceduralGalaxyGenerator
{
    public const int CurrentVersion = 1;
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
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(70);}
    try
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
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public GalaxyPlanetDefinition GeneratePlanet(GalaxyCoordinate coordinate)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(71);}
    try
    {
        if (!HasPlanet(coordinate))
            return null;

        var random = new StableRandom(HashCoordinate(worldSeed, coordinate, 0xA0761D6478BD642FUL));
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
        string planetId = EncodePlanetId(coordinate);
        var definition = new GalaxyPlanetDefinition
        {
            planetId = planetId,
            displayName = CreateName(ref random, coordinate),
            coordinate = coordinate,
            isProcedural = true,
            seed = seed,
            mapColor = mapColor,
            surfaceColor = surfaceColor,
            rockColor = rockColor,
            hasExplicitPalette = true,
            tintMapIcon = true,
            iconResourcePath = IconPaths[Mathf.Clamp((int)(hue * IconPaths.Length), 0, IconPaths.Length - 1)],
            terrain = CreateTerrain(ref random, temperature, moisture, geology),
            rivers = CreateRivers(ref random, moisture, atmosphere, mapColor),
            weather = CreateWeather(ref random, temperature, moisture, geology, atmosphere, crystal)
        };
        definition.resourceSpawnSettings = CreateResources(ref random, geology, crystal);
        definition.spawnHarvestableResources = definition.resourceSpawnSettings.Count > 0;
        return definition;
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    GalaxyCoordinate GetMacroCellCandidate(long macroX, long macroY)
    {
        ulong hash = HashCoordinate(worldSeed, new GalaxyCoordinate(macroX, macroY), 0xE7037ED1A0B428DBUL);
        long offsetX = (long)(hash & 1UL) + 1L;
        long offsetY = (long)((hash >> 1) & 1UL) + 1L;
        return new GalaxyCoordinate(macroX * MacroCellSize + offsetX, macroY * MacroCellSize + offsetY);
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
        return new PlanetTerrainSettings
        {
            continentScale = Mathf.Lerp(0.009f, 0.038f, random.Value()),
            continentHeight = Mathf.Lerp(4f, 12f, Mathf.Lerp(random.Value(), geology, 0.5f)),
            detailScale = Mathf.Lerp(0.035f, 0.12f, random.Value()),
            detailHeight = Mathf.Lerp(0.5f, 6f, geology),
            ridgeHeight = Mathf.Lerp(0.1f, 8f, geology * geology),
            surfaceLayerDepth = Mathf.Lerp(0.6f, 2.2f, moisture),
            stoneDepth = Mathf.Lerp(2.5f, 7f, 1f - temperature * 0.35f),
            generateCaves = geology > 0.18f,
            caveScale = Mathf.Lerp(0.035f, 0.11f, random.Value()),
            caveThreshold = Mathf.Lerp(0.79f, 0.56f, geology),
            caveSurfaceClearance = Mathf.Lerp(5f, 1.2f, geology)
        };
    }

    static PlanetRiverSettings CreateRivers(ref StableRandom random, float moisture, float atmosphere, Color baseColor)
    {
        bool enabled = moisture > 0.38f && atmosphere > 0.25f;
        float abundance = Mathf.InverseLerp(0.38f, 1f, moisture) * atmosphere;
        Color shallow = Color.Lerp(baseColor, new Color(0.05f, 0.72f, 0.86f, 0.58f), 0.72f);
        shallow.a = 0.58f;
        Color deep = Color.Lerp(baseColor, new Color(0.005f, 0.08f, 0.28f, 0.84f), 0.82f);
        deep.a = 0.84f;
        return new PlanetRiverSettings
        {
            enabled = enabled,
            riverCount = enabled ? Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(2f, 8f, abundance)), 2, 8) : 0,
            nodesPerRiver = random.Range(40, 65),
            minWidth = Mathf.Lerp(1.8f, 3.5f, abundance),
            maxWidth = Mathf.Lerp(3.5f, 7f, abundance),
            minDepth = Mathf.Lerp(0.7f, 1.4f, abundance),
            maxDepth = Mathf.Lerp(1.4f, 2.8f, abundance),
            flowSpeed = Mathf.Lerp(0.8f, 1.9f, random.Value()),
            lakeRadius = Mathf.Lerp(5f, 11f, abundance),
            lakeDepth = Mathf.Lerp(1.5f, 3.5f, abundance),
            localRerouteRadius = 16,
            seedOffset = random.NextNonZeroInt(),
            shallowColor = shallow,
            deepColor = deep,
            foamStrength = Mathf.Lerp(0.4f, 0.85f, abundance),
            simulationStep = 0.04f,
            sourceFlowRate = Mathf.Lerp(1f, 6f, abundance),
            manningRoughness = Mathf.Lerp(0.05f, 0.025f, abundance),
            minimumWaterDepth = 0.03f,
            maxSubstepsPerFixedUpdate = 4
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

    static string CreateName(ref StableRandom random, GalaxyCoordinate coordinate)
    {
        string prefix = NameStarts[random.Range(0, NameStarts.Length)];
        string suffix = NameEnds[random.Range(0, NameEnds.Length)];
        return coordinate == GalaxyCoordinate.Zero ? "Aster" : prefix + suffix;
    }

    public static string EncodePlanetId(GalaxyCoordinate coordinate)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(72);}
    try
    {
        return $"p_{ToBase36(ZigZag(coordinate.x))}_{ToBase36(ZigZag(coordinate.y))}";
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

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
        {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(73);}
    try
    {
            state += 0x9E3779B97F4A7C15UL;
            return Mix(state);
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

        public float Value() => (Next() >> 40) * (1f / 16777216f);

        public int Range(int minimum, int maximum)
        {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(74, (int)minimum, (int)maximum);}
    try
    {
            if (maximum <= minimum)
                return minimum;
            return minimum + (int)(Next() % (uint)(maximum - minimum));
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

        public int NextNonZeroInt()
        {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(75);}
    try
    {
            int value = unchecked((int)Next());
            return value == 0 ? 1 : value;
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}
    }
}
