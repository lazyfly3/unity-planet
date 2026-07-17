using System;
using System.Collections.Generic;
using UnityEngine;

public enum PlanetClimate
{
    Barren = 0,
    TemperateForest = 1,
    Desert = 2,
    Tropical = 3,
    Tundra = 4,
    Volcanic = 5,
    Crystal = 6
}

[Flags]
public enum PlanetClimateMask
{
    None = 0,
    Barren = 1 << (int)PlanetClimate.Barren,
    TemperateForest = 1 << (int)PlanetClimate.TemperateForest,
    Desert = 1 << (int)PlanetClimate.Desert,
    Tropical = 1 << (int)PlanetClimate.Tropical,
    Tundra = 1 << (int)PlanetClimate.Tundra,
    Volcanic = 1 << (int)PlanetClimate.Volcanic,
    Crystal = 1 << (int)PlanetClimate.Crystal,
    All = Barren | TemperateForest | Desert | Tropical | Tundra | Volcanic | Crystal
}

public enum PlanetDecorationRole
{
    Vegetation,
    Rock,
    GroundCover,
    Landmark
}

public enum PlanetDecorationAlignment
{
    GravityUp,
    SurfaceNormal
}

public enum PlanetDecorationCollision
{
    None,
    KeepPrefab,
    Simplified
}

[Serializable]
public sealed class PlanetClimateProfile
{
    public PlanetClimate climate;
    [Min(0)] public int decorationCount = 500;
    [Min(0)] public int harvestableCount = 24;

    public void ClampValues()
    {
        decorationCount = Mathf.Clamp(decorationCount, 0, 1500);
        harvestableCount = Mathf.Clamp(harvestableCount, 0, 200);
    }
}

[Serializable]
public sealed class PlanetDecorationEntry
{
    public string stableId;
    public GameObject prefab;
    public PlanetClimateMask climates = PlanetClimateMask.All;
    public PlanetDecorationRole role = PlanetDecorationRole.Vegetation;
    [Min(0.01f)] public float weight = 1f;
    [Min(0.01f)] public float minimumScale = 0.85f;
    [Min(0.01f)] public float maximumScale = 1.2f;
    public float surfaceOffset;
    [Min(0f)] public float minimumSpacing = 2.5f;
    [Min(0f)] public float playerClearRadius = 8f;
    [Min(1)] public int placementAttempts = 24;
    [Range(0f, 90f)] public float maximumSlope = 28f;
    [Min(0f)] public float waterClearance = 1f;
    [Min(0f)] public float clusterRadius = 9f;
    [Min(1)] public int clusterSize = 12;
    public PlanetDecorationAlignment alignment = PlanetDecorationAlignment.GravityUp;
    public PlanetDecorationCollision collision = PlanetDecorationCollision.Simplified;
    public bool randomizeYaw = true;
    [Tooltip("Vegetation is not generated until its natural up axis and ground pivot have been checked.")]
    public bool orientationVerified;

    public bool IsHarvestable => prefab != null
        && prefab.GetComponentInChildren<HarvestableResource>(true) != null;

    public bool Matches(PlanetClimate climate)
    {
        return (climates & (PlanetClimateMask)(1 << (int)climate)) != 0;
    }

    public void ClampValues()
    {
        stableId = stableId != null ? stableId.Trim() : string.Empty;
        weight = Mathf.Max(0.01f, weight);
        minimumScale = Mathf.Max(0.01f, minimumScale);
        maximumScale = Mathf.Max(minimumScale, maximumScale);
        surfaceOffset = Mathf.Clamp(surfaceOffset, -5f, 5f);
        minimumSpacing = Mathf.Max(0f, minimumSpacing);
        playerClearRadius = Mathf.Max(0f, playerClearRadius);
        placementAttempts = Mathf.Clamp(placementAttempts, 1, 100);
        maximumSlope = Mathf.Clamp(maximumSlope, 0f, 90f);
        waterClearance = Mathf.Max(0f, waterClearance);
        clusterRadius = Mathf.Max(0f, clusterRadius);
        clusterSize = Mathf.Max(1, clusterSize);
    }
}

[Serializable]
public sealed class PlanetSurfacePropSpawnSettings
{
    public string catalogId;
    public GameObject prefab;
    public PlanetDecorationRole role;
    public int count;
    public int seedOffset;
    public float minimumScale = 1f;
    public float maximumScale = 1f;
    public float surfaceOffset;
    public float minimumSpacing;
    public float playerClearRadius = 8f;
    public int placementAttempts = 24;
    public float maximumSlope = 30f;
    public float waterClearance = 1f;
    public float clusterRadius;
    public int clusterSize = 1;
    public PlanetDecorationAlignment alignment;
    public PlanetDecorationCollision collision;
    public bool randomizeYaw = true;
    public bool orientationVerified;

    public bool IsHarvestable => prefab != null
        && prefab.GetComponentInChildren<HarvestableResource>(true) != null;

    public void ClampValues()
    {
        catalogId = catalogId != null ? catalogId.Trim() : string.Empty;
        count = Mathf.Max(0, count);
        minimumScale = Mathf.Max(0.01f, minimumScale);
        maximumScale = Mathf.Max(minimumScale, maximumScale);
        surfaceOffset = Mathf.Clamp(surfaceOffset, -5f, 5f);
        minimumSpacing = Mathf.Max(0f, minimumSpacing);
        playerClearRadius = Mathf.Max(0f, playerClearRadius);
        placementAttempts = Mathf.Clamp(placementAttempts, 1, 100);
        maximumSlope = Mathf.Clamp(maximumSlope, 0f, 90f);
        waterClearance = Mathf.Max(0f, waterClearance);
        clusterRadius = Mathf.Max(0f, clusterRadius);
        clusterSize = Mathf.Max(1, clusterSize);
    }
}

[CreateAssetMenu(menuName = "Voxel Planet/Planet Decoration Catalog", fileName = "PlanetDecorationCatalog")]
public abstract class PlanetDecorationCatalogData : ScriptableObject
{
    [SerializeField] List<PlanetClimateProfile> profiles = new List<PlanetClimateProfile>();
    [SerializeField] List<PlanetDecorationEntry> entries = new List<PlanetDecorationEntry>();

    public IReadOnlyList<PlanetClimateProfile> Profiles => profiles;
    public IReadOnlyList<PlanetDecorationEntry> Entries => entries;

    public void ReplaceContents(
        List<PlanetClimateProfile> valueProfiles,
        List<PlanetDecorationEntry> valueEntries)
    {
        profiles = valueProfiles ?? new List<PlanetClimateProfile>();
        entries = valueEntries ?? new List<PlanetDecorationEntry>();
        foreach (PlanetClimateProfile profile in profiles)
            profile?.ClampValues();
        foreach (PlanetDecorationEntry entry in entries)
            entry?.ClampValues();
    }

    public List<PlanetSurfacePropSpawnSettings> BuildSpawnPlan(PlanetClimate climate, int planetSeed)
    {
        PlanetClimateProfile profile = FindProfile(climate);
        int decorationTarget = profile != null ? profile.decorationCount : GetDefaultDecorationCount(climate);
        int harvestableTarget = profile != null ? profile.harvestableCount : GetDefaultHarvestableCount(climate);

        var decorative = new List<PlanetDecorationEntry>();
        var harvestable = new List<PlanetDecorationEntry>();
        foreach (PlanetDecorationEntry entry in entries)
        {
            if (entry == null || entry.prefab == null || string.IsNullOrWhiteSpace(entry.stableId)
                || !entry.Matches(climate))
                continue;

            if (entry.role == PlanetDecorationRole.Vegetation && !entry.orientationVerified)
                continue;

            (entry.IsHarvestable ? harvestable : decorative).Add(entry);
        }

        var result = new List<PlanetSurfacePropSpawnSettings>(decorative.Count + harvestable.Count);
        AppendWeightedEntries(result, decorative, decorationTarget, planetSeed);
        AppendWeightedEntries(result, harvestable, harvestableTarget, planetSeed);
        return result;
    }

    public int CalculateConfigurationHash(PlanetClimate climate)
    {
        unchecked
        {
            int hash = 486187739;
            PlanetClimateProfile profile = FindProfile(climate);
            hash = hash * 31 + (int)climate;
            hash = hash * 31 + (profile != null ? profile.decorationCount : GetDefaultDecorationCount(climate));
            hash = hash * 31 + (profile != null ? profile.harvestableCount : GetDefaultHarvestableCount(climate));
            foreach (PlanetDecorationEntry entry in entries)
            {
                if (entry == null || !entry.Matches(climate))
                    continue;
                hash = hash * 31 + StableHash(entry.stableId ?? string.Empty);
                hash = hash * 31 + StableHash(entry.prefab != null ? entry.prefab.name : string.Empty);
                hash = hash * 31 + (int)entry.role;
                hash = hash * 31 + entry.weight.GetHashCode();
                hash = hash * 31 + entry.minimumScale.GetHashCode();
                hash = hash * 31 + entry.maximumScale.GetHashCode();
                hash = hash * 31 + entry.surfaceOffset.GetHashCode();
                hash = hash * 31 + entry.minimumSpacing.GetHashCode();
                hash = hash * 31 + entry.maximumSlope.GetHashCode();
                hash = hash * 31 + entry.waterClearance.GetHashCode();
                hash = hash * 31 + entry.clusterRadius.GetHashCode();
                hash = hash * 31 + entry.clusterSize;
                hash = hash * 31 + (int)entry.alignment;
                hash = hash * 31 + (int)entry.collision;
                hash = hash * 31 + (entry.IsHarvestable ? 1 : 0);
            }
            return hash;
        }
    }

    public bool ValidateCatalog(List<string> errors)
    {
        if (errors == null)
            throw new ArgumentNullException(nameof(errors));

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (PlanetClimate climate in Enum.GetValues(typeof(PlanetClimate)))
        {
            if (FindProfile(climate) == null)
                errors.Add($"Missing climate profile: {climate}.");
            bool hasDecoration = false;
            bool hasHarvestable = false;
            foreach (PlanetDecorationEntry entry in entries)
            {
                if (entry == null || entry.prefab == null || !entry.Matches(climate))
                    continue;
                hasHarvestable |= entry.IsHarvestable;
                hasDecoration |= !entry.IsHarvestable;
            }
            if (!hasDecoration)
                errors.Add($"Climate {climate} has no decorative prefab.");
            if (!hasHarvestable)
                errors.Add($"Climate {climate} has no HarvestableResource prefab.");
        }

        foreach (PlanetDecorationEntry entry in entries)
        {
            if (entry == null)
            {
                errors.Add("Catalog contains a null entry.");
                continue;
            }
            entry.ClampValues();
            if (string.IsNullOrEmpty(entry.stableId))
                errors.Add("Catalog entry has no stable ID.");
            else if (!ids.Add(entry.stableId))
                errors.Add($"Duplicate catalog ID: {entry.stableId}.");
            if (entry.prefab == null)
                errors.Add($"Catalog entry {entry.stableId} has no prefab.");
            if (entry.role == PlanetDecorationRole.Vegetation && !entry.orientationVerified)
                errors.Add($"Vegetation {entry.stableId} has not had its up axis verified.");
        }
        return errors.Count == 0;
    }

    public PlanetClimateProfile FindProfile(PlanetClimate climate)
    {
        foreach (PlanetClimateProfile profile in profiles)
            if (profile != null && profile.climate == climate)
                return profile;
        return null;
    }

    public PlanetDecorationEntry FindEntry(string stableId)
    {
        foreach (PlanetDecorationEntry entry in entries)
            if (entry != null && string.Equals(entry.stableId, stableId, StringComparison.Ordinal))
                return entry;
        return null;
    }

    static void AppendWeightedEntries(
        List<PlanetSurfacePropSpawnSettings> output,
        List<PlanetDecorationEntry> source,
        int targetCount,
        int planetSeed)
    {
        if (targetCount <= 0 || source.Count == 0)
            return;

        float totalWeight = 0f;
        foreach (PlanetDecorationEntry entry in source)
            totalWeight += Mathf.Max(0.01f, entry.weight);

        double cumulative = 0d;
        int assigned = 0;
        for (int i = 0; i < source.Count; i++)
        {
            PlanetDecorationEntry entry = source[i];
            entry.ClampValues();
            cumulative += targetCount * (double)Mathf.Max(0.01f, entry.weight) / totalWeight;
            int nextAssigned = i == source.Count - 1 ? targetCount : Mathf.FloorToInt((float)cumulative);
            int count = Mathf.Max(0, nextAssigned - assigned);
            assigned += count;
            if (count == 0)
                continue;

            output.Add(new PlanetSurfacePropSpawnSettings
            {
                catalogId = entry.stableId,
                prefab = entry.prefab,
                role = entry.role,
                count = count,
                seedOffset = StableHash(entry.stableId) ^ planetSeed,
                minimumScale = entry.minimumScale,
                maximumScale = entry.maximumScale,
                surfaceOffset = entry.surfaceOffset,
                minimumSpacing = entry.minimumSpacing,
                playerClearRadius = entry.playerClearRadius,
                placementAttempts = entry.placementAttempts,
                maximumSlope = entry.maximumSlope,
                waterClearance = entry.waterClearance,
                clusterRadius = entry.clusterRadius,
                clusterSize = entry.clusterSize,
                alignment = entry.alignment,
                collision = entry.collision,
                randomizeYaw = entry.randomizeYaw,
                orientationVerified = entry.orientationVerified
            });
        }
    }

    static int GetDefaultDecorationCount(PlanetClimate climate)
    {
        switch (climate)
        {
            case PlanetClimate.TemperateForest: return 700;
            case PlanetClimate.Tropical: return 650;
            case PlanetClimate.Desert: return 500;
            case PlanetClimate.Tundra: return 450;
            case PlanetClimate.Volcanic: return 500;
            case PlanetClimate.Crystal: return 550;
            default: return 450;
        }
    }

    static int GetDefaultHarvestableCount(PlanetClimate climate)
    {
        switch (climate)
        {
            case PlanetClimate.Crystal: return 40;
            case PlanetClimate.Volcanic: return 28;
            case PlanetClimate.Barren: return 24;
            case PlanetClimate.Desert: return 22;
            default: return 20;
        }
    }

    public static int StableHash(string value)
    {
        unchecked
        {
            int hash = 23;
            if (value == null)
                return hash;
            for (int i = 0; i < value.Length; i++)
                hash = hash * 31 + value[i];
            return hash;
        }
    }
}

public static class PlanetClimateClassifier
{
    public static PlanetClimate Classify(float temperature, float moisture, float geology, float crystal)
    {
        if (crystal >= 0.68f)
            return PlanetClimate.Crystal;
        if (geology >= 0.72f && temperature >= 0.55f)
            return PlanetClimate.Volcanic;
        if (temperature <= 0.28f)
            return PlanetClimate.Tundra;
        if (temperature >= 0.62f && moisture >= 0.58f)
            return PlanetClimate.Tropical;
        if (temperature >= 0.62f && moisture <= 0.38f)
            return PlanetClimate.Desert;
        if (moisture >= 0.48f)
            return PlanetClimate.TemperateForest;
        return PlanetClimate.Barren;
    }

    public static bool TryGetFixedClimate(string planetId, out PlanetClimate climate)
    {
        switch (planetId)
        {
            case "origin": climate = PlanetClimate.Barren; return true;
            case "verdant": climate = PlanetClimate.TemperateForest; return true;
            case "crimson": climate = PlanetClimate.Volcanic; return true;
            case "azure": climate = PlanetClimate.Tropical; return true;
            case "violet": climate = PlanetClimate.Crystal; return true;
            default: climate = PlanetClimate.Barren; return false;
        }
    }
}

[DisallowMultipleComponent]
public sealed partial class PlanetDecorationAnchor : MonoBehaviour
{
    public GameObject sourcePrefab;
    public PlanetDecorationRole role;
    public bool orientationVerified;
    public Bounds localBounds = new Bounds(Vector3.up * 0.5f, Vector3.one);
}

[DisallowMultipleComponent]
public sealed partial class PlanetSurfacePropInstance : MonoBehaviour
{
    [SerializeField] string catalogId;
    [SerializeField] string instanceId;
    [SerializeField] bool harvestable;

    public string CatalogId => catalogId;
    public string InstanceId => instanceId;
    public bool IsHarvestable => harvestable;

    public void Configure(string valueCatalogId, string valueInstanceId, bool valueHarvestable)
    {
        catalogId = valueCatalogId;
        instanceId = valueInstanceId;
        harvestable = valueHarvestable;
    }
}
