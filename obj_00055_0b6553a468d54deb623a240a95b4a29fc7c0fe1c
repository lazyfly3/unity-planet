using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlanarSurfaceContentStreamer : MonoBehaviour
{
    readonly Dictionary<Vector2Int, Transform> activeRoots =
        new Dictionary<Vector2Int, Transform>();
    readonly Queue<Transform> pooledRoots = new Queue<Transform>();
    readonly List<Vector2Int> coordinateBuffer =
        new List<Vector2Int>(25);
    readonly List<Vector3> placementBuffer = new List<Vector3>();

    InfinitePlanarSurfaceWorld world;
    PlanetLabInfiniteTerrainStreamer terrain;
    List<HarvestableResourceSpawnSettings> resourceSettings;
    List<PlanetSurfacePropSpawnSettings> propSettings;
    ProceduralPlantFactory plantFactory;

    public bool IsInitialWindowReady =>
        terrain != null
        && terrain.IsFullyReady
        && activeRoots.Count == terrain.ActiveChunkCount;
    public int ActiveChunkCount => activeRoots.Count;
    public int PooledChunkRootCount => pooledRoots.Count;

    public void Configure(
        InfinitePlanarSurfaceWorld valueWorld,
        PlanetLabInfiniteTerrainStreamer valueTerrain,
        List<HarvestableResourceSpawnSettings> valueResources,
        List<PlanetSurfacePropSpawnSettings> valueProps)
    {
        world = valueWorld;
        terrain = valueTerrain;
        resourceSettings =
            valueResources ?? new List<HarvestableResourceSpawnSettings>();
        propSettings =
            valueProps ?? new List<PlanetSurfacePropSpawnSettings>();
        BuildPlantPools();

        terrain.ChunkActivated += ActivateChunk;
        terrain.ChunkRecycled += RecycleChunk;
        terrain.GetActiveCoordinates(coordinateBuffer);
        for (int index = 0; index < coordinateBuffer.Count; index++)
            ActivateChunk(coordinateBuffer[index]);
    }

    void BuildPlantPools()
    {
        plantFactory?.Dispose();
        plantFactory = new ProceduralPlantFactory();
        foreach (PlanetSurfacePropSpawnSettings settings in propSettings)
        {
            if (settings?.proceduralPlantSpecies == null
                || string.IsNullOrWhiteSpace(settings.catalogId))
            {
                continue;
            }
            try
            {
                plantFactory.RegisterPool(
                    settings.proceduralPlantSpecies,
                    world.Definition.seed,
                    settings.catalogId,
                    settings.variantPoolSize);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"PlanarSurfaceContentStreamer: failed to create plant pool '{settings.catalogId}': {exception.Message}",
                    this);
            }
        }
    }

    void ActivateChunk(Vector2Int coordinate)
    {
        if (world == null || activeRoots.ContainsKey(coordinate))
            return;
        Transform root = AcquireRoot();
        root.name = $"PlanarContent_{coordinate.x}_{coordinate.y}";
        root.gameObject.SetActive(true);
        activeRoots.Add(coordinate, root);
        placementBuffer.Clear();
        SpawnResources(coordinate, root);
        SpawnProps(coordinate, root);
    }

    void RecycleChunk(Vector2Int coordinate)
    {
        if (!activeRoots.TryGetValue(coordinate, out Transform root))
            return;
        activeRoots.Remove(coordinate);
        for (int index = root.childCount - 1; index >= 0; index--)
            Destroy(root.GetChild(index).gameObject);
        root.gameObject.SetActive(false);
        pooledRoots.Enqueue(root);
    }

    Transform AcquireRoot()
    {
        if (pooledRoots.Count > 0)
            return pooledRoots.Dequeue();
        var root = new GameObject("PlanarContentChunk");
        root.transform.SetParent(transform, true);
        root.AddComponent<PlanetFloatingOriginParticipant>();
        return root.transform;
    }

    void SpawnResources(Vector2Int coordinate, Transform root)
    {
        if (!world.Definition.spawnHarvestableResources)
            return;
        for (int settingsIndex = 0;
             settingsIndex < resourceSettings.Count;
             settingsIndex++)
        {
            HarvestableResourceSpawnSettings settings =
                resourceSettings[settingsIndex];
            if (settings?.prefab == null || settings.count <= 0)
                continue;
            settings.ClampValues();
            int count = GetChunkCount(
                settings.count,
                coordinate,
                settings.seedOffset ^ settingsIndex * 7919);
            var random = CreateRandom(
                coordinate,
                settings.seedOffset ^ settingsIndex * 7919);
            for (int itemIndex = 0; itemIndex < count; itemIndex++)
            {
                string stableId = BuildStableInstanceId(
                    "resource-" + settingsIndex,
                    coordinate,
                    itemIndex);
                if (world.IsHarvested(stableId)
                    || !TryFindPlacement(
                        coordinate,
                        random,
                        settings.minimumSpacing,
                        55f,
                        settings.surfaceOffset,
                        out Vector3 position,
                        out Vector3 normal))
                {
                    continue;
                }
                GameObject instance = Instantiate(
                    settings.prefab.gameObject,
                    position,
                    BuildRotation(
                        normal,
                        random,
                        settings.randomizeYaw,
                        settings.clockwiseRotationDegrees),
                    root);
                HarvestableResource harvestable =
                    instance.GetComponentInChildren<HarvestableResource>(true);
                harvestable?.AssignStableResourceId(stableId);
                PlanetSurfacePropInstance marker =
                    instance.GetComponent<PlanetSurfacePropInstance>()
                    ?? instance.AddComponent<PlanetSurfacePropInstance>();
                marker.Configure(
                    $"resource-{settingsIndex}",
                    stableId,
                    true);
                placementBuffer.Add(position);
            }
        }
    }

    void SpawnProps(Vector2Int coordinate, Transform root)
    {
        for (int settingsIndex = 0;
             settingsIndex < propSettings.Count;
             settingsIndex++)
        {
            PlanetSurfacePropSpawnSettings settings =
                propSettings[settingsIndex];
            if (settings == null
                || !settings.HasValidSource
                || settings.count <= 0
                || string.IsNullOrWhiteSpace(settings.catalogId))
            {
                continue;
            }
            settings.ClampValues();
            int salt = settings.seedOffset ^ settingsIndex * 104729;
            int count = GetChunkCount(settings.count, coordinate, salt);
            var random = CreateRandom(coordinate, salt);
            for (int itemIndex = 0; itemIndex < count; itemIndex++)
            {
                string stableId = BuildStableInstanceId(
                    settings.catalogId,
                    coordinate,
                    itemIndex);
                bool harvestable = settings.IsHarvestable;
                if (harvestable && world.IsHarvested(stableId))
                    continue;
                if (!TryFindPlacement(
                        coordinate,
                        random,
                        settings.minimumSpacing,
                        settings.maximumSlope,
                        settings.surfaceOffset,
                        out Vector3 position,
                        out Vector3 normal))
                {
                    continue;
                }

                GameObject instance = CreateProp(
                    settings,
                    stableId,
                    root);
                if (instance == null)
                    continue;
                Vector3 up =
                    settings.alignment == PlanetDecorationAlignment.SurfaceNormal
                        ? normal
                        : Vector3.up;
                instance.transform.SetPositionAndRotation(
                    position,
                    BuildRotation(
                        up,
                        random,
                        settings.randomizeYaw,
                        0f));
                float scale = Mathf.Lerp(
                    settings.minimumScale,
                    settings.maximumScale,
                    (float)random.NextDouble());
                instance.transform.localScale *= scale;
                PlanetSurfacePropInstance marker =
                    instance.GetComponent<PlanetSurfacePropInstance>()
                    ?? instance.AddComponent<PlanetSurfacePropInstance>();
                marker.Configure(
                    settings.catalogId,
                    stableId,
                    harvestable);
                HarvestableResource resource =
                    instance.GetComponentInChildren<HarvestableResource>(true);
                resource?.AssignStableResourceId(stableId);
                ConfigureBiota(instance, settings);
                placementBuffer.Add(position);
            }
        }
    }

    GameObject CreateProp(
        PlanetSurfacePropSpawnSettings settings,
        string stableId,
        Transform parent)
    {
        if (settings.prefab != null)
            return Instantiate(settings.prefab, parent);
        if (settings.proceduralPlantSpecies == null
            || plantFactory == null
            || !plantFactory.ContainsPool(settings.catalogId))
        {
            return null;
        }
        return plantFactory.CreateInstance(
            settings.catalogId,
            world.Definition.seed,
            stableId,
            parent);
    }

    bool TryFindPlacement(
        Vector2Int coordinate,
        System.Random random,
        float minimumSpacing,
        float maximumSlope,
        float surfaceOffset,
        out Vector3 position,
        out Vector3 normal)
    {
        float half = InfinitePlanarSurfaceWorld.ChunkSize * 0.5f;
        for (int attempt = 0; attempt < 24; attempt++)
        {
            double x =
                coordinate.x * (double)InfinitePlanarSurfaceWorld.ChunkSize
                + Mathf.Lerp(
                    -half,
                    half,
                    (float)random.NextDouble());
            double z =
                coordinate.y * (double)InfinitePlanarSurfaceWorld.ChunkSize
                + Mathf.Lerp(
                    -half,
                    half,
                    (float)random.NextDouble());
            Vector3 probe = world.FromPersistentAddress(
                new PlanarSurfaceAddress(x, z, 0f));
            if (!world.TryProjectToSurface(
                    probe,
                    out PlanetSurfaceSample sample)
                || sample.isWater
                || Vector3.Angle(Vector3.up, sample.normal)
                    > maximumSlope)
            {
                continue;
            }

            position =
                sample.point + sample.normal * surfaceOffset;
            bool spaced = true;
            float spacingSquared =
                minimumSpacing * minimumSpacing;
            for (int index = 0; index < placementBuffer.Count; index++)
            {
                if ((placementBuffer[index] - position).sqrMagnitude
                    < spacingSquared)
                {
                    spaced = false;
                    break;
                }
            }
            if (!spaced)
                continue;
            normal = sample.normal;
            return true;
        }
        position = default;
        normal = Vector3.up;
        return false;
    }

    int GetChunkCount(
        int windowTarget,
        Vector2Int coordinate,
        int salt)
    {
        int baseCount = Mathf.Max(0, windowTarget) / 25;
        int remainder = Mathf.Max(0, windowTarget) % 25;
        int bucket = PositiveHash(
            world.Definition.seed,
            coordinate.x,
            coordinate.y,
            salt) % 25;
        return baseCount + (bucket < remainder ? 1 : 0);
    }

    System.Random CreateRandom(Vector2Int coordinate, int salt)
    {
        return new System.Random(PositiveHash(
            world.Definition.seed,
            coordinate.x,
            coordinate.y,
            salt));
    }

    static int PositiveHash(int seed, int x, int z, int salt)
    {
        unchecked
        {
            uint value = (uint)seed;
            value = (value ^ (uint)x) * 0x9E3779B9u;
            value = (value ^ (uint)z) * 0x85EBCA6Bu;
            value = (value ^ (uint)salt) * 0xC2B2AE35u;
            value ^= value >> 16;
            return (int)(value & 0x7FFFFFFF);
        }
    }

    public static string BuildStableInstanceId(
        string configurationId,
        Vector2Int coordinate,
        int itemIndex)
    {
        return $"{configurationId}:{coordinate.x}:{coordinate.y}:{itemIndex}";
    }

    static Quaternion BuildRotation(
        Vector3 up,
        System.Random random,
        bool randomizeYaw,
        float fixedYaw)
    {
        Quaternion rotation = Quaternion.FromToRotation(
            Vector3.up,
            up.sqrMagnitude > 0.001f ? up.normalized : Vector3.up);
        float yaw = randomizeYaw
            ? (float)random.NextDouble() * 360f
            : fixedYaw;
        return Quaternion.AngleAxis(yaw, up) * rotation;
    }

    static void ConfigureBiota(
        GameObject instance,
        PlanetSurfacePropSpawnSettings settings)
    {
        if (settings.role != PlanetDecorationRole.Vegetation
            && settings.role != PlanetDecorationRole.GroundCover)
        {
            return;
        }
        string speciesId =
            settings.proceduralPlantSpecies != null
            && !string.IsNullOrWhiteSpace(
                settings.proceduralPlantSpecies.speciesId)
                ? settings.proceduralPlantSpecies.speciesId
                : settings.catalogId;
        string displayName =
            settings.proceduralPlantSpecies != null
                ? settings.proceduralPlantSpecies.name
                : settings.prefab != null
                    ? settings.prefab.name
                    : settings.catalogId;
        BiotaScannable.Ensure(
            instance,
            BiotaDiscoveryType.Plant,
            speciesId,
            displayName,
            $"Planet flora · {settings.role}");
    }

    void OnDestroy()
    {
        if (terrain != null)
        {
            terrain.ChunkActivated -= ActivateChunk;
            terrain.ChunkRecycled -= RecycleChunk;
        }
        plantFactory?.Dispose();
        plantFactory = null;
    }
}
