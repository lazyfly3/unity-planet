using System.Collections;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;
using UnityEngine;

[System.Serializable]
public sealed class HarvestableResourceSpawnSettings
{
    [Tooltip("Prefab containing HarvestableResource.")]
    public HarvestableResource prefab;
    [Min(0)] public int count = 20;
    public int seedOffset = 1000;
    [Min(0f)] public float surfaceOffset = 0.1f;
    [Min(0f)] public float minimumSpacing = 3f;
    [Min(0f)] public float playerClearRadius = 6f;
    [Min(1)] public int placementAttempts = 30;
    [Range(0f, 360f)] public float clockwiseRotationDegrees;
    public bool randomizeYaw = true;

    public void ClampValues()
    {
count = Mathf.Max(0, count);
        surfaceOffset = Mathf.Max(0f, surfaceOffset);
        minimumSpacing = Mathf.Max(0f, minimumSpacing);
        playerClearRadius = Mathf.Max(0f, playerClearRadius);
        placementAttempts = Mathf.Max(1, placementAttempts);
    
}
}

public class VoxelQuadSphereWorld : MonoBehaviour
{
    sealed class FlightChunkBuildWork
    {
        public QuadSphereChunkKey Key;
        public byte[] PaddedVoxels;
        public int NextSample;
        public Task<VoxelQuadSphereMeshData> MeshTask;
    }

    sealed class FlightColliderBakeWork
    {
        public QuadSphereChunkKey Key;
        public int MeshRevision;
    }

    struct ResourcePlacement
    {
        public Vector3 Position;
        public float MinimumSpacing;

        public ResourcePlacement(Vector3 position, float minimumSpacing)
        {
            Position = position;
            MinimumSpacing = minimumSpacing;
        }
    }

    [Header("星球")]
    [SerializeField] int seed = 12345;
    [SerializeField] Vector3 planetCenterLocal = Vector3.zero;
    [SerializeField] float planetRadius = 100f;
    [SerializeField] float voxelOuterRadius = 100f;
    [SerializeField] float surfaceGravity = 9.8f;
    [SerializeField] int faceGridSize = 100;
    [SerializeField] int maxDepth = 64;
    [Tooltip("最内若干层强制实心石头，天然洞穴不得穿透，防止洞底漏到未生成区域")]
    [SerializeField] int innerSolidDepthLayers = 8;

    [Header("材质")]
    [SerializeField] Material dirtMaterial;
    [SerializeField] Material stoneMaterial;

    [Header("玩家出生")]
    [SerializeField] Transform playerSpawn;
    [SerializeField] bool autoPlacePlayerOnStart = true;
    [SerializeField] Vector3 spawnDirectionLocal = Vector3.up;
    [SerializeField] float spawnHeightOffset = 2f;

    [Header("Loading")]
    [SerializeField, Range(5f, 100f)] float generationFrameBudgetMilliseconds = 50f;
    [SerializeField] PlanetLoadingUI loadingUI;
    [SerializeField] bool logTrackDuringGeneration = true;
    [Header("Water")]
    [SerializeField] PlanetRiverSystem riverSystem;
    PlanetSurfaceDecorationSystem surfaceDecorationSystem;

    bool spawnHarvestableResources;
    List<HarvestableResourceSpawnSettings> resourceSpawnSettings = new List<HarvestableResourceSpawnSettings>();
    PlanetTerrainSettings terrainSettings = new PlanetTerrainSettings();

    readonly Dictionary<QuadSphereChunkKey, VoxelQuadSphereChunk> chunks = new Dictionary<QuadSphereChunkKey, VoxelQuadSphereChunk>();
    readonly Dictionary<QuadSphereChunkKey, byte[]> modifiedChunkCache = new Dictionary<QuadSphereChunkKey, byte[]>();
    readonly Dictionary<QuadSphereChunkKey, QuadSphereChunkSaveEntry> savedMeshCache = new Dictionary<QuadSphereChunkKey, QuadSphereChunkSaveEntry>();
    readonly HashSet<string> harvestedResourceIds = new HashSet<string>();
    readonly HashSet<Collider> terrainColliders = new HashSet<Collider>();
    readonly List<ResourcePlacement> resourcePlacements = new List<ResourcePlacement>();
    readonly List<GalaxyResourceSaveEntry> resourceSnapshots = new List<GalaxyResourceSaveEntry>();
    Transform generatedResourcesRoot;
    GalaxyResourceSaveEntry[] savedResourceSnapshot;
    bool loadingResourceSnapshot;
    bool loadingCompleteSnapshot;
    bool loadingCompleteMeshSnapshot;
    bool bulkLoadingPlanet;
    bool generationComplete;
    bool restoreLogTrackAfterGeneration;
    bool migrateSavedTerrainChanges;
    PlanetTerrainSettings previousTerrainSettings;
    Material runtimeDirtMaterial;
    Material runtimeStoneMaterial;
    PlanetCelestialProfile celestialProfile = PlanetCelestialProfile.CreateCompatibleDefault();
    bool streamingLargePlanet;
    bool streamingPassRunning;
    float nextStreamingUpdateTime;
    QuadSphereChunkKey lastStreamingAnchor;
    readonly HashSet<QuadSphereChunkKey> desiredStreamingChunks = new HashSet<QuadSphereChunkKey>();
    readonly HashSet<QuadSphereChunkKey> readinessChunks = new HashSet<QuadSphereChunkKey>();
    readonly HashSet<QuadSphereChunkKey> retainedFlightChunks = new HashSet<QuadSphereChunkKey>();
    readonly List<QuadSphereChunkKey> flightBuildQueue = new List<QuadSphereChunkKey>();
    readonly HashSet<QuadSphereChunkKey> flightScheduledChunks = new HashSet<QuadSphereChunkKey>();
    readonly List<FlightChunkBuildWork> flightMeshBuilds = new List<FlightChunkBuildWork>();
    readonly List<FlightColliderBakeWork> flightColliderBakes = new List<FlightColliderBakeWork>();
    readonly Stack<byte[]> flightVoxelBufferPool = new Stack<byte[]>();
    FlightChunkBuildWork activeFlightVoxelWork;
    bool readOnlyFlightStreaming;
    Transform flightStreamingTarget;
    Vector3 predictedFlightDirection;
    float requestedFlightDistanceAhead;
    float readyDistanceAhead;

    const int LargePlanetFaceGridSize = 2048;
    const int InitialStreamingChunkRadius = 3;
    const int ActiveStreamingChunkRadius = 4;
    const float StreamingRefreshSeconds = 0.35f;
    const int FlightLocalChunkRadius = 5;
    const int FlightCorridorChunkRadius = 1;
    const float FlightStreamingRefreshSeconds = 0.18f;
    const float FlightStreamingFrameBudgetSeconds = 0.004f;
    const float FlightMinimumReadyDistance = 180f;
    const float FlightMaximumReadyDistance = 1200f;
    const float FlightCorridorStepDistance = 150f;
    const int FlightVoxelBudgetCheckInterval = 32;
    const int FlightMaxMeshWorkers = 2;
    const int FlightMaxMeshAppliesPerFrame = 1;
    const int FlightMaxColliderAppliesPerFrame = 1;
    const int FlightColliderApplyIntervalFrames = 2;
    const int FlightVoxelBufferPoolCapacity = 6;
    int nextFlightColliderApplyFrame;

    public event Action<QuadSphereVoxelAddress> VoxelChanged;

    public int Seed => seed;
    public bool UsePlanetGeneration => true;
    public bool IsGenerationComplete => generationComplete
        && (surfaceDecorationSystem == null || surfaceDecorationSystem.IsGenerationComplete);
    public float PlanetRadius => planetRadius;
    public float VoxelOuterRadius => voxelOuterRadius;
    public bool IsStreamingLargePlanet => streamingLargePlanet;
    public bool IsReadOnlyFlightStreaming => readOnlyFlightStreaming;
    public bool IsFlightRegionReady => readOnlyFlightStreaming
        && generationComplete
        && readyDistanceAhead >= FlightMinimumReadyDistance;
    public float ReadyDistanceAhead => readyDistanceAhead;
    public PlanetCelestialProfile CelestialProfile => celestialProfile != null
        ? celestialProfile.Clone()
        : PlanetCelestialProfile.CreateCompatibleDefault();
    public float SurfaceGravity => surfaceGravity;
    public float GravitationalParameter => PlanetGravity.ComputeGravitationalParameter(surfaceGravity, planetRadius);
    public int FaceGridSize => faceGridSize;
    public int MaxDepth => maxDepth;
    public int InnerSolidDepthLayers => innerSolidDepthLayers;
    public int ExpectedChunkCount => GetExpectedChunkCount();
    public int ResourceConfigurationHash => CalculateResourceConfigurationHash();
    public int SurfacePropConfigurationHash => surfaceDecorationSystem != null
        ? surfaceDecorationSystem.ConfigurationHash
        : 0;
    public int TerrainConfigurationHash => CalculateTerrainConfigurationHash(terrainSettings);
    public PlanetTerrainSettings TerrainSettingsSnapshot => terrainSettings.Clone();
    public PlanetRiverSystem RiverSystem => riverSystem;
    public Transform PlayerSpawn => playerSpawn;
    public PlanetSurfaceDecorationSystem SurfaceDecorationSystem => surfaceDecorationSystem;
    public bool HasCompleteMeshSnapshot
    {
        get
        {
            if (chunks.Count != GetExpectedChunkCount())
                return false;
            foreach (VoxelQuadSphereChunk chunk in chunks.Values)
                if (!chunk.HasMesh)
                    return false;
            return true;
        }
    }

    public void SetBaseTerrainMaterials(Material surfaceMaterial, Material rockMaterial)
    {
        dirtMaterial = surfaceMaterial;
        stoneMaterial = rockMaterial;
    }

    public void BeginReadOnlyFlightStreaming(
        int planetSeed,
        GalaxyPlanetSaveData save,
        Color surfaceColor,
        Color rockColor,
        PlanetTerrainSettings planetTerrainSettings,
        PlanetCelestialProfile physicsProfile,
        Transform target,
        Vector3 predictedDirection)
    {
        readOnlyFlightStreaming = true;
        flightStreamingTarget = target;
        playerSpawn = target;
        autoPlacePlayerOnStart = false;
        spawnHarvestableResources = false;
        this.predictedFlightDirection = predictedDirection;
        requestedFlightDistanceAhead = FlightMinimumReadyDistance;
        readyDistanceAhead = 0f;
        ConfigurePlanet(
            planetSeed,
            save,
            surfaceColor,
            rockColor,
            planetTerrainSettings,
            false,
            new List<HarvestableResourceSpawnSettings>(),
            new List<PlanetSurfacePropSpawnSettings>(),
            null,
            null,
            physicsProfile);
    }

    public void SetFlightStreamingTarget(Transform target, Vector3 predictedDirection)
    {
        flightStreamingTarget = target;
        if (target != null)
            playerSpawn = target;
        this.predictedFlightDirection = predictedDirection;
    }

    public void ConfigurePlanet(
        int planetSeed,
        GalaxyPlanetSaveData save,
        Color surfaceColor,
        Color rockColor,
        PlanetTerrainSettings planetTerrainSettings,
        bool shouldSpawnHarvestableResources,
        List<HarvestableResourceSpawnSettings> planetResourceSpawnSettings,
        List<PlanetSurfacePropSpawnSettings> planetSurfacePropSettings,
        PlanetRiverSettings planetRiverSettings,
        GalaxyRiverSaveData savedRiverData,
        PlanetCelestialProfile celestialProfile)
    {
generationComplete = false;
        seed = planetSeed;
        PlanetCelestialProfile physicsProfile = celestialProfile ?? PlanetCelestialProfile.CreateCompatibleDefault();
        physicsProfile.ClampValues();
        this.celestialProfile = physicsProfile.Clone();
        planetRadius = physicsProfile.radius;
        streamingLargePlanet = physicsProfile.surfaceGenerationMode == PlanetSurfaceGenerationMode.StreamingLargeSphere;
        if (streamingLargePlanet)
        {
            faceGridSize = LargePlanetFaceGridSize;
            voxelOuterRadius = planetRadius + physicsProfile.maximumTerrainElevation + 4f;
            float editableShellDepth = physicsProfile.maximumTerrainElevation + physicsProfile.editableDepth + 16f;
            maxDepth = Mathf.CeilToInt(editableShellDepth / VoxelTypes.ChunkSize) * VoxelTypes.ChunkSize;
        }
        else
        {
            faceGridSize = 100;
            maxDepth = 64;
            voxelOuterRadius = planetRadius;
        }
        surfaceGravity = physicsProfile.surfaceGravity;
        PendingPlanetLandingContext landing = PendingPlanetLandingContext.Peek();
        if (landing != null && landing.landingDirection.sqrMagnitude > 0.001f)
            spawnDirectionLocal = landing.landingDirection.normalized;
        modifiedChunkCache.Clear();
        savedMeshCache.Clear();
        harvestedResourceIds.Clear();
        ApplyPlanetMaterials(surfaceColor, rockColor);
        terrainSettings = planetTerrainSettings != null ? planetTerrainSettings.Clone() : new PlanetTerrainSettings();
        terrainSettings.ClampValues();
        spawnHarvestableResources = shouldSpawnHarvestableResources
            && (planetSurfacePropSettings == null || planetSurfacePropSettings.Count == 0);
        resourceSpawnSettings = planetResourceSpawnSettings ?? new List<HarvestableResourceSpawnSettings>();
        if (riverSystem == null)
            riverSystem = GetComponent<PlanetRiverSystem>();
        if (streamingLargePlanet && planetRiverSettings != null)
            planetRiverSettings.enabled = false;
        riverSystem?.Configure(this, planetRiverSettings, savedRiverData);
        if (!readOnlyFlightStreaming && surfaceDecorationSystem == null)
            surfaceDecorationSystem = GetComponent<PlanetSurfaceDecorationSystem>();
        if (!readOnlyFlightStreaming && surfaceDecorationSystem == null)
            surfaceDecorationSystem = gameObject.AddComponent<PlanetSurfaceDecorationSystem>();
        if (!readOnlyFlightStreaming)
            surfaceDecorationSystem.Configure(this, planetSurfacePropSettings, save);
        savedResourceSnapshot = save != null ? save.resources : null;
        loadingResourceSnapshot = save != null
            && save.hasFullResourceSnapshot
            && save.resourceConfigurationHash == CalculateResourceConfigurationHash();

        if (save != null && save.harvestedResourceIds != null)
        {
            foreach (string resourceId in save.harvestedResourceIds)
            {
                if (!string.IsNullOrEmpty(resourceId))
                    harvestedResourceIds.Add(resourceId);
            }
        }

        if (save == null || save.chunks == null)
            return;

        int expectedLength = VoxelTypes.ChunkSize * VoxelTypes.ChunkSize * VoxelTypes.ChunkSize;
        foreach (QuadSphereChunkSaveEntry entry in save.chunks)
        {
            if (entry == null || !IsValidChunkEntry(entry))
                continue;

            byte[] saved = entry.voxels;
            if (saved == null && !string.IsNullOrEmpty(entry.base64))
            {
                try
                {
                    saved = System.Convert.FromBase64String(entry.base64);
                }
                catch (System.FormatException)
                {
                    Debug.LogWarning($"VoxelQuadSphereWorld: ignored corrupt saved chunk {entry.face}:{entry.chunkU}:{entry.chunkV}:{entry.chunkDepth}.", this);
                    continue;
                }
            }

            if (saved == null || saved.Length != expectedLength)
                continue;

            QuadSphereChunkKey key = new QuadSphereChunkKey(
                (QuadSphereFace)entry.face,
                entry.chunkU,
                entry.chunkV,
                entry.chunkDepth);
            modifiedChunkCache[key] = saved;
            if (HasValidMeshSnapshot(entry))
                savedMeshCache[key] = entry;
        }

        bool snapshotDimensionsMatch = save.hasFullVoxelSnapshot
            && save.seed == planetSeed
            && save.faceGridSize == faceGridSize
            && save.maxDepth == maxDepth
            && save.chunkSize == VoxelTypes.ChunkSize
            && modifiedChunkCache.Count == GetExpectedChunkCount();
        bool riverSnapshotMatches = planetRiverSettings == null
            || !planetRiverSettings.enabled
            || (savedRiverData != null
                && savedRiverData.configurationHash == planetRiverSettings.CalculateHash());
        loadingCompleteSnapshot = snapshotDimensionsMatch
            && save.terrainConfigurationHash == CalculateTerrainConfigurationHash(terrainSettings)
            && riverSnapshotMatches;
        migrateSavedTerrainChanges = snapshotDimensionsMatch && !loadingCompleteSnapshot;
        previousTerrainSettings = save.terrainSettings != null
            ? save.terrainSettings.Clone()
            : PlanetTerrainSettings.CreateLegacy();
        loadingCompleteMeshSnapshot = loadingCompleteSnapshot
            && save.hasFullMeshSnapshot
            && savedMeshCache.Count == GetExpectedChunkCount();

        if (save.hasFullVoxelSnapshot && !loadingCompleteSnapshot)
        {
            string action = migrateSavedTerrainChanges
                ? "regenerating the terrain while preserving player voxel changes"
                : "falling back to procedural generation";
            Debug.LogWarning($"VoxelQuadSphereWorld: terrain settings or generation dimensions changed; {action}.", this);
        }
    
}

    public List<QuadSphereChunkSaveEntry> GetCompleteChunkSnapshots()
    {
List<QuadSphereChunkSaveEntry> result = new List<QuadSphereChunkSaveEntry>(chunks.Count);
        foreach (VoxelQuadSphereChunk chunk in chunks.Values)
        {
            QuadSphereChunkSaveEntry entry = CreateChunkSaveEntry(chunk.Key, chunk.Voxels);
            chunk.AddMeshSnapshot(entry);
            result.Add(entry);
        }
        return result;
    
}

    public string[] GetHarvestedResourceIds()
    {
string[] result = new string[harvestedResourceIds.Count];
        harvestedResourceIds.CopyTo(result);
        System.Array.Sort(result, System.StringComparer.Ordinal);
        return result;
    
}

    public GalaxyResourceSaveEntry[] GetResourceSnapshots()
    {
return resourceSnapshots.ToArray();
    
}

    public void MarkResourceHarvested(string resourceId)
    {
if (!string.IsNullOrEmpty(resourceId))
            harvestedResourceIds.Add(resourceId);
        surfaceDecorationSystem?.MarkHarvested(resourceId);
    
}

    public string[] GetHarvestedSurfacePropIds()
    {
        return surfaceDecorationSystem != null
            ? surfaceDecorationSystem.GetHarvestedIds()
            : Array.Empty<string>();
    }

    public GalaxySurfacePropSaveEntry[] GetSurfacePropSnapshots()
    {
        return surfaceDecorationSystem != null
            ? surfaceDecorationSystem.GetSnapshots()
            : Array.Empty<GalaxySurfacePropSaveEntry>();
    }

    public List<QuadSphereChunkSaveEntry> GetModifiedChunkSnapshots()
    {
List<QuadSphereChunkSaveEntry> result = new List<QuadSphereChunkSaveEntry>();
        HashSet<QuadSphereChunkKey> added = new HashSet<QuadSphereChunkKey>();

        foreach (VoxelQuadSphereChunk chunk in chunks.Values)
        {
            if (!chunk.IsModified)
                continue;

            result.Add(CreateChunkSaveEntry(chunk.Key, chunk.Voxels));
            added.Add(chunk.Key);
        }

        foreach (KeyValuePair<QuadSphereChunkKey, byte[]> pair in modifiedChunkCache)
        {
            if (!added.Contains(pair.Key))
                result.Add(CreateChunkSaveEntry(pair.Key, pair.Value));
        }

        return result;
    
}

    static QuadSphereChunkSaveEntry CreateChunkSaveEntry(QuadSphereChunkKey key, byte[] voxels)
    {
        return new QuadSphereChunkSaveEntry
        {
            face = (int)key.Face,
            chunkU = key.ChunkU,
            chunkV = key.ChunkV,
            chunkDepth = key.ChunkDepth,
            voxels = voxels
        };
    }

    bool IsValidChunkEntry(QuadSphereChunkSaveEntry entry)
    {
        int chunkCountU = Mathf.CeilToInt(faceGridSize / (float)VoxelTypes.ChunkSize);
        int chunkCountV = Mathf.CeilToInt(faceGridSize / (float)VoxelTypes.ChunkSize);
        int chunkCountDepth = Mathf.CeilToInt(maxDepth / (float)VoxelTypes.ChunkSize);
        return entry.face >= 0 && entry.face < 6
            && entry.chunkU >= 0 && entry.chunkU < chunkCountU
            && entry.chunkV >= 0 && entry.chunkV < chunkCountV
            && entry.chunkDepth >= 0 && entry.chunkDepth < chunkCountDepth;
    }

    static bool HasValidMeshSnapshot(QuadSphereChunkSaveEntry entry)
    {
        if (entry.meshVertices == null
            || entry.meshNormals == null
            || entry.meshNormals.Length != entry.meshVertices.Length
            || entry.meshSubMeshTriangles == null
            || entry.meshSubMeshTriangles.Length == 0)
        {
            return false;
        }

        foreach (int[] triangles in entry.meshSubMeshTriangles)
            if (triangles == null || triangles.Length % 3 != 0)
                return false;
        return true;
    }

    int GetExpectedChunkCount()
    {
        int chunkCountU = Mathf.CeilToInt(faceGridSize / (float)VoxelTypes.ChunkSize);
        int chunkCountV = Mathf.CeilToInt(faceGridSize / (float)VoxelTypes.ChunkSize);
        int chunkCountDepth = Mathf.CeilToInt(maxDepth / (float)VoxelTypes.ChunkSize);
        return 6 * chunkCountU * chunkCountV * chunkCountDepth;
    }

    int CalculateResourceConfigurationHash()
    {
        unchecked
        {
            int hash = spawnHarvestableResources ? 486187739 : 17;
            hash = hash * 31 + CalculateTerrainConfigurationHash(terrainSettings);
            hash = hash * 31 + resourceSpawnSettings.Count;
            foreach (HarvestableResourceSpawnSettings settings in resourceSpawnSettings)
            {
                if (settings == null)
                {
                    hash *= 31;
                    continue;
                }

                hash = hash * 31 + StableStringHash(settings.prefab != null ? settings.prefab.name : string.Empty);
                hash = hash * 31 + settings.count;
                hash = hash * 31 + settings.seedOffset;
                hash = hash * 31 + settings.surfaceOffset.GetHashCode();
                hash = hash * 31 + settings.minimumSpacing.GetHashCode();
                hash = hash * 31 + settings.playerClearRadius.GetHashCode();
                hash = hash * 31 + settings.placementAttempts;
                hash = hash * 31 + settings.clockwiseRotationDegrees.GetHashCode();
                hash = hash * 31 + (settings.randomizeYaw ? 1 : 0);
            }

            return hash;
        }
    }

    static int CalculateTerrainConfigurationHash(PlanetTerrainSettings settings)
    {
        settings = settings ?? new PlanetTerrainSettings();
        unchecked
        {
            int hash = 486187739;
            hash = hash * 31 + settings.continentScale.GetHashCode();
            hash = hash * 31 + settings.continentHeight.GetHashCode();
            hash = hash * 31 + settings.detailScale.GetHashCode();
            hash = hash * 31 + settings.detailHeight.GetHashCode();
            hash = hash * 31 + settings.ridgeHeight.GetHashCode();
            hash = hash * 31 + settings.surfaceLayerDepth.GetHashCode();
            hash = hash * 31 + settings.stoneDepth.GetHashCode();
            hash = hash * 31 + (settings.generateCaves ? 1 : 0);
            hash = hash * 31 + settings.caveScale.GetHashCode();
            hash = hash * 31 + settings.caveThreshold.GetHashCode();
            hash = hash * 31 + settings.caveSurfaceClearance.GetHashCode();
            return hash;
        }
    }

    static int StableStringHash(string value)
    {
        unchecked
        {
            int hash = 23;
            for (int i = 0; i < value.Length; i++)
                hash = hash * 31 + value[i];
            return hash;
        }
    }

    void ApplyPlanetMaterials(Color surfaceColor, Color rockColor)
    {
        runtimeDirtMaterial = CreatePlanetMaterial(dirtMaterial, surfaceColor, "Surface");
        runtimeStoneMaterial = CreatePlanetMaterial(stoneMaterial, rockColor, "Rock");
        if (runtimeDirtMaterial != null)
            dirtMaterial = runtimeDirtMaterial;
        if (runtimeStoneMaterial != null)
            stoneMaterial = runtimeStoneMaterial;
    }

    Material CreatePlanetMaterial(Material source, Color color, string layerName)
    {
        if (source == null)
            return null;

        Material material = new Material(source)
        {
            name = $"{name}_{layerName}_{seed}"
        };
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
        return material;
    }

    public Vector3 GetPlanetCenterWorld()
    {
return transform.TransformPoint(planetCenterLocal);
    
}

    public Vector3 GetPlanetCenterLocal()
    {
return planetCenterLocal;
    
}

    public float GetProceduralSurfaceRadius(Vector3 direction)
    {
Vector3 normalized = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.up;
        return planetRadius + VoxelQuadSphereTerrain.GetSurfaceNoise(
            normalized * planetRadius, seed, terrainSettings);
    
}

    public void ConfigureRiverSystem(PlanetRiverSettings settings, GalaxyRiverSaveData save)
    {
if (riverSystem == null)
            riverSystem = GetComponent<PlanetRiverSystem>();
        riverSystem?.Configure(this, settings, save);
    
}

    IEnumerator Start()
    {
        if (readOnlyFlightStreaming)
        {
            yield return null;
            generationComplete = true;
            RefreshFlightStreamingPlan();
            yield break;
        }

        if (loadingUI == null)
            loadingUI = FindObjectOfType<PlanetLoadingUI>(true);
        loadingUI?.Show("正在分析星球数据");

        VoxelPlanetPlayerController playerController = playerSpawn != null
            ? playerSpawn.GetComponentInParent<VoxelPlanetPlayerController>()
            : null;
        Rigidbody playerBody = playerSpawn != null
            ? playerSpawn.GetComponentInParent<Rigidbody>()
            : null;
        bool controllerWasEnabled = playerController != null && playerController.enabled;
        bool bodyWasKinematic = playerBody != null && playerBody.isKinematic;

        if (playerController != null)
            playerController.enabled = false;
        if (playerBody != null)
        {
            playerBody.velocity = Vector3.zero;
            playerBody.angularVelocity = Vector3.zero;
            playerBody.isKinematic = true;
        }

        SuspendLogTrackForGeneration();
        try
        {
            // Let the newly loaded scene render before expensive terrain work begins.
            yield return null;
            bool riversEnabled = riverSystem != null && riverSystem.GenerationEnabled;
            loadingUI?.SetProgress(
                0.03f,
                riversEnabled ? "正在恢复河网与湖泊" : "正在准备星球地形");
            if (riversEnabled)
                riverSystem?.PrepareHydrology();
            yield return null;
            if (streamingLargePlanet)
                yield return GenerateStreamingRegionIncremental(spawnDirectionLocal, InitialStreamingChunkRadius, false);
            else
                yield return GenerateEntirePlanetIncremental();
        }
        finally
        {
            RestoreLogTrackAfterGeneration();
        }
        if (playerBody != null)
            playerBody.isKinematic = bodyWasKinematic;

        loadingUI?.SetProgress(0.96f, "正在部署资源与玩家基地");
        yield return null;
        PlacePlayerAtSpawn();
        if (riverSystem != null && riverSystem.GenerationEnabled)
            riverSystem?.BuildWaterSurface();
        if (surfaceDecorationSystem != null)
            yield return surfaceDecorationSystem.GenerateIncremental(4f);
        else
            SpawnHarvestableResources();
        loadingUI?.SetProgress(1f, "星球构筑完成");
        yield return null;
        loadingUI?.Complete();

        if (playerController != null)
            playerController.enabled = controllerWasEnabled;
    }

    void OnValidate()
    {
        if (resourceSpawnSettings == null)
            return;

        foreach (HarvestableResourceSpawnSettings settings in resourceSpawnSettings)
            settings?.ClampValues();
    }

    void OnDestroy()
    {
        CompleteFlightBackgroundWork();
        RestoreLogTrackAfterGeneration();
        if (runtimeDirtMaterial != null)
            Destroy(runtimeDirtMaterial);
        if (runtimeStoneMaterial != null)
            Destroy(runtimeStoneMaterial);
    }

    void LateUpdate()
    {
        if (bulkLoadingPlanet || readOnlyFlightStreaming)
            return;

        foreach (VoxelQuadSphereChunk chunk in chunks.Values)
        {
            if (chunk.IsDirty)
                chunk.RebuildMesh(SampleVoxelAt, faceGridSize, maxDepth, voxelOuterRadius, planetCenterLocal);
        }
    }

    void Update()
    {
        if (readOnlyFlightStreaming)
        {
            UpdateReadOnlyFlightStreaming();
            return;
        }

        Transform streamingTarget = readOnlyFlightStreaming ? flightStreamingTarget : playerSpawn;
        if (!streamingLargePlanet || !generationComplete || streamingPassRunning
            || streamingTarget == null || Time.unscaledTime < nextStreamingUpdateTime)
            return;

        nextStreamingUpdateTime = Time.unscaledTime
            + (readOnlyFlightStreaming ? FlightStreamingRefreshSeconds : StreamingRefreshSeconds);
        Vector3 localPlayer = transform.InverseTransformPoint(streamingTarget.position);
        Vector3 direction = localPlayer - planetCenterLocal;
        if (direction.sqrMagnitude < 0.001f)
            return;

        QuadSphereChunkKey anchor = GetSurfaceAnchor(direction);
        if (anchor.Equals(lastStreamingAnchor) && !readOnlyFlightStreaming)
            return;

        lastStreamingAnchor = anchor;
        StartCoroutine(GenerateStreamingRegionIncremental(
            direction,
            readOnlyFlightStreaming ? FlightLocalChunkRadius : ActiveStreamingChunkRadius,
            true));
    }

    void UpdateReadOnlyFlightStreaming()
    {
        ApplyCompletedFlightMeshes();
        ApplyCompletedFlightColliders();

        if (!streamingLargePlanet || !generationComplete || flightStreamingTarget == null)
            return;

        if (Time.unscaledTime >= nextStreamingUpdateTime)
        {
            nextStreamingUpdateTime = Time.unscaledTime + FlightStreamingRefreshSeconds;
            RefreshFlightStreamingPlan();
        }

        AdvanceFlightVoxelSampling();

        Vector3 localTarget = transform.InverseTransformPoint(flightStreamingTarget.position);
        Vector3 direction = localTarget - planetCenterLocal;
        readyDistanceAhead = direction.sqrMagnitude > 0.001f
            ? CalculateReadyDistanceAhead(direction)
            : 0f;
    }

    void RefreshFlightStreamingPlan()
    {
        if (flightStreamingTarget == null)
            return;

        Vector3 localTarget = transform.InverseTransformPoint(flightStreamingTarget.position);
        Vector3 direction = localTarget - planetCenterLocal;
        if (direction.sqrMagnitude < 0.001f)
            return;

        lastStreamingAnchor = GetSurfaceAnchor(direction);
        BuildFlightDesiredStreamingChunks(direction, desiredStreamingChunks);
        BuildFlightRetainedStreamingChunks(direction, retainedFlightChunks);
        flightBuildQueue.Clear();
        foreach (QuadSphereChunkKey key in desiredStreamingChunks)
        {
            if (!chunks.ContainsKey(key) && !flightScheduledChunks.Contains(key))
                flightBuildQueue.Add(key);
        }

        Vector3 radial = direction.normalized;
        flightBuildQueue.Sort((left, right) =>
            GetFlightChunkPriority(left, radial).CompareTo(GetFlightChunkPriority(right, radial)));
        UnloadChunksOutside(retainedFlightChunks);
    }

    float GetFlightChunkPriority(QuadSphereChunkKey key, Vector3 targetRadial)
    {
        int centerU = Mathf.Min(
            faceGridSize - 1,
            key.ChunkU * VoxelTypes.ChunkSize + VoxelTypes.ChunkSize / 2);
        int centerV = Mathf.Min(
            faceGridSize - 1,
            key.ChunkV * VoxelTypes.ChunkSize + VoxelTypes.ChunkSize / 2);
        Vector3 radial = VoxelQuadSphereMapping.GetRadialDirection(
            key.Face, centerU, centerV, faceGridSize);
        float arcDistance = Vector3.Angle(targetRadial, radial) * Mathf.Deg2Rad * planetRadius;
        return arcDistance + key.ChunkDepth * 0.25f;
    }

    void AdvanceFlightVoxelSampling()
    {
        double deadline = Time.realtimeSinceStartupAsDouble + FlightStreamingFrameBudgetSeconds;
        int paddedSize = VoxelQuadSphereMesher.PaddedChunkSize;

        while (Time.realtimeSinceStartupAsDouble < deadline)
        {
            if (activeFlightVoxelWork == null)
            {
                if (flightMeshBuilds.Count >= FlightMaxMeshWorkers || flightBuildQueue.Count == 0)
                    return;

                QuadSphereChunkKey key = flightBuildQueue[0];
                flightBuildQueue.RemoveAt(0);
                if (chunks.ContainsKey(key) || flightScheduledChunks.Contains(key))
                    continue;

                activeFlightVoxelWork = new FlightChunkBuildWork
                {
                    Key = key,
                    PaddedVoxels = RentFlightVoxelBuffer()
                };
                flightScheduledChunks.Add(key);
            }

            if (!desiredStreamingChunks.Contains(activeFlightVoxelWork.Key))
            {
                flightScheduledChunks.Remove(activeFlightVoxelWork.Key);
                ReturnFlightVoxelBuffer(activeFlightVoxelWork.PaddedVoxels);
                activeFlightVoxelWork = null;
                continue;
            }

            FlightChunkBuildWork work = activeFlightVoxelWork;
            int sampleCount = work.PaddedVoxels.Length;
            while (work.NextSample < sampleCount)
            {
                int index = work.NextSample++;
                int x = index % paddedSize;
                int yz = index / paddedSize;
                int y = yz % paddedSize;
                int z = yz / paddedSize;
                work.PaddedVoxels[index] = SampleFlightVoxel(
                    work.Key,
                    x - 1,
                    y - 1,
                    z - 1);

                if ((work.NextSample & (FlightVoxelBudgetCheckInterval - 1)) == 0
                    && Time.realtimeSinceStartupAsDouble >= deadline)
                {
                    return;
                }
            }

            QuadSphereChunkKey meshKey = work.Key;
            byte[] voxelData = work.PaddedVoxels;
            int meshGridSize = faceGridSize;
            float meshRadius = voxelOuterRadius;
            Vector3 meshCenter = planetCenterLocal;
            work.MeshTask = Task.Run(() =>
                VoxelQuadSphereMesher.BuildChunkMeshDataFromPaddedVoxels(
                    voxelData,
                    meshKey,
                    meshGridSize,
                    meshRadius,
                    meshCenter));
            flightMeshBuilds.Add(work);
            activeFlightVoxelWork = null;
        }
    }

    byte SampleFlightVoxel(QuadSphereChunkKey key, int localU, int localV, int localDepth)
    {
        QuadSphereVoxelAddress address = new QuadSphereVoxelAddress(
            key.Face,
            key.ChunkU * VoxelTypes.ChunkSize + localU,
            key.ChunkV * VoxelTypes.ChunkSize + localV,
            key.ChunkDepth * VoxelTypes.ChunkSize + localDepth);
        if (address.Depth < 0 || address.Depth >= maxDepth)
            return VoxelTypes.Air;

        address = VoxelQuadSphereMapping.RemapAcrossFace(address, faceGridSize);
        if (address.U < 0 || address.V < 0
            || address.U >= faceGridSize || address.V >= faceGridSize)
        {
            return VoxelTypes.Air;
        }

        QuadSphereChunkKey savedKey = AddressToChunkKey(address);
        Vector3Int savedLocal = AddressToLocalCoord(address);
        int savedIndex = VoxelTypes.ToIndex(savedLocal.x, savedLocal.y, savedLocal.z);
        bool hasSavedVoxel = modifiedChunkCache.TryGetValue(savedKey, out byte[] saved)
            && saved.Length == VoxelTypes.ChunkSize * VoxelTypes.ChunkSize * VoxelTypes.ChunkSize;

        byte generated = VoxelQuadSphereTerrain.GenerateVoxel(
            address.Face,
            address.U,
            address.V,
            address.Depth,
            faceGridSize,
            maxDepth,
            innerSolidDepthLayers,
            seed,
            planetCenterLocal,
            voxelOuterRadius,
            planetRadius,
            terrainSettings);
        if (!hasSavedVoxel)
            return generated;
        if (!migrateSavedTerrainChanges)
            return saved[savedIndex];

        byte previousGenerated = VoxelQuadSphereTerrain.GenerateVoxel(
            address.Face,
            address.U,
            address.V,
            address.Depth,
            faceGridSize,
            maxDepth,
            innerSolidDepthLayers,
            seed,
            planetCenterLocal,
            voxelOuterRadius,
            planetRadius,
            previousTerrainSettings ?? PlanetTerrainSettings.CreateLegacy());
        return saved[savedIndex] == previousGenerated ? generated : saved[savedIndex];
    }

    void ApplyCompletedFlightMeshes()
    {
        int applied = 0;
        for (int i = 0;
             i < flightMeshBuilds.Count && applied < FlightMaxMeshAppliesPerFrame;)
        {
            FlightChunkBuildWork work = flightMeshBuilds[i];
            if (work.MeshTask == null || !work.MeshTask.IsCompleted)
            {
                i++;
                continue;
            }

            flightMeshBuilds.RemoveAt(i);
            if (work.MeshTask.IsFaulted)
            {
                flightScheduledChunks.Remove(work.Key);
                ReturnFlightVoxelBuffer(work.PaddedVoxels);
                Debug.LogError(
                    $"VoxelQuadSphereWorld: background flight mesh failed for {work.Key.Face}:" +
                    $"{work.Key.ChunkU}:{work.Key.ChunkV}:{work.Key.ChunkDepth}. " +
                    work.MeshTask.Exception?.GetBaseException().Message,
                    this);
                continue;
            }

            if (!desiredStreamingChunks.Contains(work.Key))
            {
                flightScheduledChunks.Remove(work.Key);
                work.MeshTask.Result.Release();
                ReturnFlightVoxelBuffer(work.PaddedVoxels);
                continue;
            }

            if (!chunks.TryGetValue(work.Key, out VoxelQuadSphereChunk chunk))
            {
                chunk = new VoxelQuadSphereChunk(work.Key, transform, dirtMaterial, stoneMaterial);
                CopyFlightChunkVoxels(work.PaddedVoxels, chunk.Voxels);
                if (modifiedChunkCache.ContainsKey(work.Key))
                    chunk.MarkModified();
                else
                    chunk.ClearModifiedFlag();
                chunks.Add(work.Key, chunk);
                terrainColliders.Add(chunk.Collider);
            }

            VoxelQuadSphereMeshData meshData = work.MeshTask.Result;
            bool meshIsEmpty = meshData.IsEmpty;
            int revision;
            try
            {
                revision = chunk.ApplyMeshData(meshData, false);
            }
            finally
            {
                meshData.Release();
            }
            if (meshIsEmpty)
            {
                flightScheduledChunks.Remove(work.Key);
            }
            else
            {
                var bakeWork = new FlightColliderBakeWork
                {
                    Key = work.Key,
                    MeshRevision = revision
                };
                flightColliderBakes.Add(bakeWork);
            }
            ReturnFlightVoxelBuffer(work.PaddedVoxels);
            applied++;
        }
    }

    byte[] RentFlightVoxelBuffer()
    {
        if (flightVoxelBufferPool.Count > 0)
            return flightVoxelBufferPool.Pop();
        int size = VoxelQuadSphereMesher.PaddedChunkSize;
        return new byte[size * size * size];
    }

    void ReturnFlightVoxelBuffer(byte[] buffer)
    {
        if (buffer == null || flightVoxelBufferPool.Count >= FlightVoxelBufferPoolCapacity)
            return;
        flightVoxelBufferPool.Push(buffer);
    }

    static void CopyFlightChunkVoxels(byte[] padded, byte[] destination)
    {
        int paddedSize = VoxelQuadSphereMesher.PaddedChunkSize;
        for (int z = 0; z < VoxelTypes.ChunkSize; z++)
        {
            for (int y = 0; y < VoxelTypes.ChunkSize; y++)
            {
                for (int x = 0; x < VoxelTypes.ChunkSize; x++)
                {
                    int sourceIndex = (x + 1)
                        + (y + 1) * paddedSize
                        + (z + 1) * paddedSize * paddedSize;
                    destination[VoxelTypes.ToIndex(x, y, z)] = padded[sourceIndex];
                }
            }
        }
    }

    void ApplyCompletedFlightColliders()
    {
        if (Time.frameCount < nextFlightColliderApplyFrame)
            return;

        int applied = 0;
        while (flightColliderBakes.Count > 0 && applied < FlightMaxColliderAppliesPerFrame)
        {
            FlightColliderBakeWork work = flightColliderBakes[0];
            flightColliderBakes.RemoveAt(0);
            flightScheduledChunks.Remove(work.Key);
            if (desiredStreamingChunks.Contains(work.Key)
                && chunks.TryGetValue(work.Key, out VoxelQuadSphereChunk chunk))
            {
                chunk.BakeAndApplyCollider(work.MeshRevision);
            }
            applied++;
        }

        if (applied > 0)
            nextFlightColliderApplyFrame = Time.frameCount + FlightColliderApplyIntervalFrames;
    }

    void CompleteFlightBackgroundWork()
    {
        var tasks = new List<Task>(flightMeshBuilds.Count);
        for (int i = 0; i < flightMeshBuilds.Count; i++)
            if (flightMeshBuilds[i].MeshTask != null)
                tasks.Add(flightMeshBuilds[i].MeshTask);
        if (tasks.Count > 0)
        {
            try
            {
                Task.WaitAll(tasks.ToArray());
            }
            catch (AggregateException)
            {
                // Individual task failures are reported during normal application.
            }
        }

        if (activeFlightVoxelWork != null)
        {
            ReturnFlightVoxelBuffer(activeFlightVoxelWork.PaddedVoxels);
            activeFlightVoxelWork = null;
        }
        for (int i = 0; i < flightMeshBuilds.Count; i++)
        {
            Task<VoxelQuadSphereMeshData> task = flightMeshBuilds[i].MeshTask;
            if (task != null && task.Status == TaskStatus.RanToCompletion)
                task.Result.Release();
            ReturnFlightVoxelBuffer(flightMeshBuilds[i].PaddedVoxels);
        }
    }

    IEnumerator GenerateStreamingRegionIncremental(Vector3 direction, int horizontalRadius, bool unloadOutsideRegion)
    {
        streamingPassRunning = true;
        try
        {
            float frameStartedAt = Time.realtimeSinceStartup;
            float frameBudgetSeconds = readOnlyFlightStreaming
                ? FlightStreamingFrameBudgetSeconds
                : 0.006f;
            if (readOnlyFlightStreaming)
                BuildFlightDesiredStreamingChunks(direction, desiredStreamingChunks);
            else
                BuildDesiredStreamingChunks(direction, horizontalRadius, desiredStreamingChunks);

            bulkLoadingPlanet = true;
            foreach (QuadSphereChunkKey key in desiredStreamingChunks)
            {
                if (!chunks.ContainsKey(key))
                    LoadChunk(key);
                if (Time.realtimeSinceStartup - frameStartedAt < frameBudgetSeconds)
                    continue;
                loadingUI?.SetProgress(0.12f, "正在加载着陆区域地形");
                yield return null;
                frameStartedAt = Time.realtimeSinceStartup;
            }
            bulkLoadingPlanet = false;

            foreach (QuadSphereChunkKey key in desiredStreamingChunks)
            {
                if (!chunks.TryGetValue(key, out VoxelQuadSphereChunk chunk) || !chunk.IsDirty)
                    continue;
                chunk.RebuildMesh(SampleVoxelAt, faceGridSize, maxDepth, voxelOuterRadius, planetCenterLocal);
                if (Time.realtimeSinceStartup - frameStartedAt < frameBudgetSeconds)
                    continue;
                loadingUI?.SetProgress(0.70f, "正在构建无缝地表");
                yield return null;
                frameStartedAt = Time.realtimeSinceStartup;
            }

            if (unloadOutsideRegion)
                UnloadChunksOutside(desiredStreamingChunks);

            generationComplete = true;
            if (readOnlyFlightStreaming)
                readyDistanceAhead = CalculateReadyDistanceAhead(direction);
        }
        finally
        {
            bulkLoadingPlanet = false;
            streamingPassRunning = false;
        }
    }

    void BuildDesiredStreamingChunks(
        Vector3 direction,
        int horizontalRadius,
        HashSet<QuadSphereChunkKey> destination)
    {
        destination.Clear();
        AddDesiredStreamingChunks(direction, horizontalRadius, destination, false);
    }

    void BuildFlightDesiredStreamingChunks(
        Vector3 direction,
        HashSet<QuadSphereChunkKey> destination)
    {
        destination.Clear();
        Vector3 radial = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector3.up;
        Vector3 localForward = transform.InverseTransformDirection(predictedFlightDirection);
        localForward = Vector3.ProjectOnPlane(localForward, radial);
        if (localForward.sqrMagnitude < 0.001f)
            localForward = Vector3.Cross(radial, Vector3.right);
        if (localForward.sqrMagnitude < 0.001f)
            localForward = Vector3.Cross(radial, Vector3.forward);
        localForward.Normalize();

        float speed = flightStreamingTarget == null
            ? 0f
            : Vector3.ProjectOnPlane(predictedFlightDirection, transform.TransformDirection(radial)).magnitude;
        requestedFlightDistanceAhead = Mathf.Clamp(
            speed * 5f + 220f,
            FlightMinimumReadyDistance,
            FlightMaximumReadyDistance);
        AddDesiredStreamingChunks(radial, FlightLocalChunkRadius, destination, true);
        for (float distance = FlightCorridorStepDistance;
             distance <= requestedFlightDistanceAhead + 0.01f;
             distance += FlightCorridorStepDistance)
        {
            Vector3 corridorDirection = (radial
                + localForward * (distance / Mathf.Max(1f, planetRadius))).normalized;
            AddDesiredStreamingChunks(
                corridorDirection,
                FlightCorridorChunkRadius,
                destination,
                true);
        }
    }

    void BuildFlightRetainedStreamingChunks(
        Vector3 direction,
        HashSet<QuadSphereChunkKey> destination)
    {
        destination.Clear();
        Vector3 radial = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector3.up;
        Vector3 localForward = transform.InverseTransformDirection(predictedFlightDirection);
        localForward = Vector3.ProjectOnPlane(localForward, radial);
        if (localForward.sqrMagnitude < 0.001f)
            localForward = Vector3.Cross(radial, Vector3.right);
        if (localForward.sqrMagnitude < 0.001f)
            localForward = Vector3.Cross(radial, Vector3.forward);
        localForward.Normalize();

        AddDesiredStreamingChunks(radial, FlightLocalChunkRadius + 2, destination, true);
        float retainedDistance = Mathf.Min(
            FlightMaximumReadyDistance,
            requestedFlightDistanceAhead + FlightCorridorStepDistance);
        for (float distance = FlightCorridorStepDistance;
             distance <= retainedDistance + 0.01f;
             distance += FlightCorridorStepDistance)
        {
            Vector3 corridorDirection = (radial
                + localForward * (distance / Mathf.Max(1f, planetRadius))).normalized;
            AddDesiredStreamingChunks(
                corridorDirection,
                FlightCorridorChunkRadius + 1,
                destination,
                true);
        }
    }

    void AddDesiredStreamingChunks(
        Vector3 direction,
        int horizontalRadius,
        HashSet<QuadSphereChunkKey> destination,
        bool surfaceOnly)
    {
        VoxelQuadSphereMapping.DirectionToFaceCell(
            direction, faceGridSize, out QuadSphereFace anchorFace, out int anchorU, out int anchorV);
        int chunkDepthCount = Mathf.CeilToInt(maxDepth / (float)VoxelTypes.ChunkSize);
        int belowSurfaceChunks = Mathf.CeilToInt(
            celestialProfile.editableDepth / VoxelTypes.ChunkSize) + 1;

        for (int dv = -horizontalRadius; dv <= horizontalRadius; dv++)
        {
            for (int du = -horizontalRadius; du <= horizontalRadius; du++)
            {
                int sampleU = anchorU + du * VoxelTypes.ChunkSize;
                int sampleV = anchorV + dv * VoxelTypes.ChunkSize;
                QuadSphereVoxelAddress remapped = VoxelQuadSphereMapping.RemapAcrossFace(
                    new QuadSphereVoxelAddress(anchorFace, sampleU, sampleV, 0), faceGridSize);
                int chunkU = remapped.U / VoxelTypes.ChunkSize;
                int chunkV = remapped.V / VoxelTypes.ChunkSize;
                int centerU = Mathf.Min(faceGridSize - 1, chunkU * VoxelTypes.ChunkSize + VoxelTypes.ChunkSize / 2);
                int centerV = Mathf.Min(faceGridSize - 1, chunkV * VoxelTypes.ChunkSize + VoxelTypes.ChunkSize / 2);
                Vector3 radial = VoxelQuadSphereMapping.GetRadialDirection(remapped.Face, centerU, centerV, faceGridSize);
                float surfaceDepth = voxelOuterRadius - GetProceduralSurfaceRadius(radial);
                int surfaceChunkDepth = Mathf.Clamp(
                    Mathf.FloorToInt(surfaceDepth / VoxelTypes.ChunkSize), 0, chunkDepthCount - 1);
                int firstDepth = Mathf.Max(0, surfaceChunkDepth - 1);
                int lastDepth = Mathf.Min(
                    chunkDepthCount - 1,
                    surfaceChunkDepth + (surfaceOnly ? 1 : belowSurfaceChunks));
                for (int chunkDepth = firstDepth; chunkDepth <= lastDepth; chunkDepth++)
                {
                    destination.Add(new QuadSphereChunkKey(
                        remapped.Face, chunkU, chunkV, chunkDepth));
                }
            }
        }
    }

    float CalculateReadyDistanceAhead(Vector3 direction)
    {
        Vector3 radial = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector3.up;
        Vector3 localForward = transform.InverseTransformDirection(predictedFlightDirection);
        localForward = Vector3.ProjectOnPlane(localForward, radial);
        if (localForward.sqrMagnitude < 0.001f)
            return 0f;
        localForward.Normalize();

        float ready = 0f;
        for (float distance = FlightCorridorStepDistance;
             distance <= requestedFlightDistanceAhead + 0.01f;
             distance += FlightCorridorStepDistance)
        {
            Vector3 corridorDirection = (radial
                + localForward * (distance / Mathf.Max(1f, planetRadius))).normalized;
            readinessChunks.Clear();
            AddDesiredStreamingChunks(
                corridorDirection,
                FlightCorridorChunkRadius,
                readinessChunks,
                true);
            bool complete = true;
            foreach (QuadSphereChunkKey key in readinessChunks)
            {
                if (!chunks.TryGetValue(key, out VoxelQuadSphereChunk chunk) || !chunk.IsCollisionReady)
                {
                    complete = false;
                    break;
                }
            }
            if (!complete)
                break;
            ready = distance;
        }
        return ready;
    }

    QuadSphereChunkKey GetSurfaceAnchor(Vector3 direction)
    {
        VoxelQuadSphereMapping.DirectionToFaceCell(
            direction, faceGridSize, out QuadSphereFace face, out int cellU, out int cellV);
        float surfaceDepth = voxelOuterRadius - GetProceduralSurfaceRadius(direction);
        return new QuadSphereChunkKey(
            face,
            cellU / VoxelTypes.ChunkSize,
            cellV / VoxelTypes.ChunkSize,
            Mathf.Clamp(Mathf.FloorToInt(surfaceDepth / VoxelTypes.ChunkSize), 0, maxDepth / VoxelTypes.ChunkSize - 1));
    }

    void UnloadChunksOutside(HashSet<QuadSphereChunkKey> keep)
    {
        var remove = new List<QuadSphereChunkKey>();
        foreach (KeyValuePair<QuadSphereChunkKey, VoxelQuadSphereChunk> pair in chunks)
        {
            if (!keep.Contains(pair.Key) && !flightScheduledChunks.Contains(pair.Key))
                remove.Add(pair.Key);
        }

        foreach (QuadSphereChunkKey key in remove)
        {
            VoxelQuadSphereChunk chunk = chunks[key];
            if (chunk.IsModified)
            {
                byte[] saved = new byte[chunk.Voxels.Length];
                Array.Copy(chunk.Voxels, saved, saved.Length);
                modifiedChunkCache[key] = saved;
            }
            terrainColliders.Remove(chunk.Collider);
            chunk.Destroy();
            chunks.Remove(key);
        }
    }

    public void GenerateEntirePlanet()
    {
float loadStartedAt = Time.realtimeSinceStartup;
        int chunkCountU = Mathf.CeilToInt(faceGridSize / (float)VoxelTypes.ChunkSize);
        int chunkCountV = Mathf.CeilToInt(faceGridSize / (float)VoxelTypes.ChunkSize);
        int chunkCountDepth = Mathf.CeilToInt(maxDepth / (float)VoxelTypes.ChunkSize);

        bulkLoadingPlanet = true;
        for (int faceIndex = 0; faceIndex < 6; faceIndex++)
        {
            QuadSphereFace face = (QuadSphereFace)faceIndex;
            for (int cd = 0; cd < chunkCountDepth; cd++)
            {
                for (int cv = 0; cv < chunkCountV; cv++)
                {
                    for (int cu = 0; cu < chunkCountU; cu++)
                        LoadChunk(new QuadSphereChunkKey(face, cu, cv, cd));
                }
            }
        }
        bulkLoadingPlanet = false;

        bool restoredFromSnapshot = loadingCompleteSnapshot;
        bool restoredMeshSnapshot = loadingCompleteMeshSnapshot && RestoreAllChunkMeshesFromSnapshot();
        modifiedChunkCache.Clear();
        savedMeshCache.Clear();
        loadingCompleteSnapshot = false;
        loadingCompleteMeshSnapshot = false;
        migrateSavedTerrainChanges = false;
        previousTerrainSettings = null;

        if (!restoredMeshSnapshot)
            RebuildAllChunkMeshes();

        generationComplete = true;

        float elapsedMilliseconds = (Time.realtimeSinceStartup - loadStartedAt) * 1000f;
        string source = restoredMeshSnapshot
            ? "complete mesh snapshot"
            : restoredFromSnapshot ? "complete voxel snapshot" : "procedural generation";
        Debug.Log(
            $"VoxelQuadSphereWorld: loaded {chunks.Count} chunks from {source} in {elapsedMilliseconds:0} ms.",
            this);
    
}

    void SuspendLogTrackForGeneration()
    {
        if (logTrackDuringGeneration)
        {
            restoreLogTrackAfterGeneration = false;
            return;
        }

        restoreLogTrackAfterGeneration = FSPDebuger.EnableLogTrackInternal;
        if (restoreLogTrackAfterGeneration)
            FSPDebuger.EnableLogTrackInternal = false;
    }

    void RestoreLogTrackAfterGeneration()
    {
        if (!restoreLogTrackAfterGeneration)
            return;

        FSPDebuger.EnableLogTrackInternal = true;
        restoreLogTrackAfterGeneration = false;
    }

    IEnumerator GenerateEntirePlanetIncremental()
    {
        float loadStartedAt = Time.realtimeSinceStartup;
        float frameStartedAt = loadStartedAt;
        float frameBudgetSeconds = Mathf.Max(0.001f, generationFrameBudgetMilliseconds * 0.001f);
        int chunkCountU = Mathf.CeilToInt(faceGridSize / (float)VoxelTypes.ChunkSize);
        int chunkCountV = Mathf.CeilToInt(faceGridSize / (float)VoxelTypes.ChunkSize);
        int chunkCountDepth = Mathf.CeilToInt(maxDepth / (float)VoxelTypes.ChunkSize);
        int expectedChunks = 6 * chunkCountU * chunkCountV * chunkCountDepth;
        int generatedChunks = 0;

        bulkLoadingPlanet = true;
        for (int faceIndex = 0; faceIndex < 6; faceIndex++)
        {
            QuadSphereFace face = (QuadSphereFace)faceIndex;
            for (int cd = 0; cd < chunkCountDepth; cd++)
            {
                for (int cv = 0; cv < chunkCountV; cv++)
                {
                    for (int cu = 0; cu < chunkCountU; cu++)
                    {
                        LoadChunk(new QuadSphereChunkKey(face, cu, cv, cd));
                        generatedChunks++;
                        if (Time.realtimeSinceStartup - frameStartedAt < frameBudgetSeconds)
                            continue;

                        loadingUI?.SetProgress(
                            0.05f + 0.53f * generatedChunks / Mathf.Max(1f, expectedChunks),
                            "正在生成星球体素");
                        yield return null;
                        frameStartedAt = Time.realtimeSinceStartup;
                    }
                }
            }
        }
        bulkLoadingPlanet = false;

        bool restoredFromSnapshot = loadingCompleteSnapshot;
        bool restoredMeshSnapshot = loadingCompleteMeshSnapshot;
        int processedMeshes = 0;
        if (restoredMeshSnapshot)
        {
            foreach (KeyValuePair<QuadSphereChunkKey, VoxelQuadSphereChunk> pair in chunks)
            {
                if (!savedMeshCache.TryGetValue(pair.Key, out QuadSphereChunkSaveEntry entry)
                    || !pair.Value.RestoreMesh(entry))
                {
                    restoredMeshSnapshot = false;
                    Debug.LogWarning("VoxelQuadSphereWorld: mesh snapshot is incomplete; rebuilding meshes from voxels.", this);
                    break;
                }

                processedMeshes++;

                if (Time.realtimeSinceStartup - frameStartedAt < frameBudgetSeconds)
                    continue;

                loadingUI?.SetProgress(
                    0.58f + 0.36f * processedMeshes / Mathf.Max(1f, expectedChunks),
                    "正在恢复星球网格与碰撞体");
                yield return null;
                frameStartedAt = Time.realtimeSinceStartup;
            }
        }

        if (!restoredMeshSnapshot)
        {
            foreach (VoxelQuadSphereChunk chunk in chunks.Values)
            {
                chunk.RebuildMesh(SampleVoxelAt, faceGridSize, maxDepth, voxelOuterRadius, planetCenterLocal);
                processedMeshes++;
                if (Time.realtimeSinceStartup - frameStartedAt < frameBudgetSeconds)
                    continue;

                loadingUI?.SetProgress(
                    0.58f + 0.36f * processedMeshes / Mathf.Max(1f, expectedChunks),
                    "正在构建星球网格与碰撞体");
                yield return null;
                frameStartedAt = Time.realtimeSinceStartup;
            }
        }

        modifiedChunkCache.Clear();
        savedMeshCache.Clear();
        loadingCompleteSnapshot = false;
        loadingCompleteMeshSnapshot = false;
        migrateSavedTerrainChanges = false;
        previousTerrainSettings = null;
        generationComplete = true;

        float elapsedMilliseconds = (Time.realtimeSinceStartup - loadStartedAt) * 1000f;
        string source = restoredMeshSnapshot
            ? "complete mesh snapshot"
            : restoredFromSnapshot ? "complete voxel snapshot" : "procedural generation";
        Debug.Log(
            $"VoxelQuadSphereWorld: incrementally loaded {chunks.Count} chunks from {source} in {elapsedMilliseconds:0} ms.",
            this);
    }

    void RebuildAllChunkMeshes()
    {
        foreach (VoxelQuadSphereChunk chunk in chunks.Values)
            chunk.RebuildMesh(SampleVoxelAt, faceGridSize, maxDepth, voxelOuterRadius, planetCenterLocal);
    }

    bool RestoreAllChunkMeshesFromSnapshot()
    {
        foreach (KeyValuePair<QuadSphereChunkKey, VoxelQuadSphereChunk> pair in chunks)
        {
            if (!savedMeshCache.TryGetValue(pair.Key, out QuadSphereChunkSaveEntry entry)
                || !pair.Value.RestoreMesh(entry))
            {
                Debug.LogWarning("VoxelQuadSphereWorld: mesh snapshot is incomplete; rebuilding meshes from voxels.", this);
                return false;
            }
        }

        return true;
    }

    void LoadChunk(QuadSphereChunkKey key)
    {
        if (chunks.ContainsKey(key))
            return;

        VoxelQuadSphereChunk chunk = new VoxelQuadSphereChunk(key, transform, dirtMaterial, stoneMaterial);
        GenerateChunkData(chunk);
        chunks.Add(key, chunk);
        terrainColliders.Add(chunk.Collider);
        if (!bulkLoadingPlanet)
        {
            chunk.RebuildMesh(SampleVoxelAt, faceGridSize, maxDepth, voxelOuterRadius, planetCenterLocal);
            MarkLoadedNeighborsDirty(key);
        }
    }

    public void SpawnHarvestableResources()
    {
ClearGeneratedResources();

        if (!spawnHarvestableResources || resourceSpawnSettings == null || resourceSpawnSettings.Count == 0)
            return;

        bool hasValidSettings = false;
        foreach (HarvestableResourceSpawnSettings settings in resourceSpawnSettings)
            hasValidSettings |= settings != null && settings.prefab != null && settings.count > 0;
        if (!hasValidSettings)
            return;

        generatedResourcesRoot = new GameObject("GeneratedHarvestableResources").transform;
        generatedResourcesRoot.SetParent(transform, false);
        resourcePlacements.Clear();
        resourceSnapshots.Clear();
        Physics.SyncTransforms();

        if (TryRestoreResourceSnapshot())
            return;

        for (int settingsIndex = 0; settingsIndex < resourceSpawnSettings.Count; settingsIndex++)
        {
            HarvestableResourceSpawnSettings settings = resourceSpawnSettings[settingsIndex];
            if (settings == null || settings.prefab == null || settings.count <= 0)
                continue;

            settings.ClampValues();
            System.Random random = new System.Random(seed + settings.seedOffset);
            int spawnedCount = 0;
            int maximumAttempts = settings.count * settings.placementAttempts;
            for (int attempt = 0; attempt < maximumAttempts && spawnedCount < settings.count; attempt++)
            {
                Vector3 direction = streamingLargePlanet
                    ? GetStreamingSurfaceDirection(random)
                    : GetRandomSphereDirection(random);
                if (!TryFindPlanetSurface(direction, out RaycastHit surfaceHit))
                    continue;

                Vector3 up = (surfaceHit.point - GetPlanetCenterWorld()).normalized;
                Vector3 spawnPosition = surfaceHit.point + up * settings.surfaceOffset;
                if (!IsResourcePositionValid(spawnPosition, settings))
                    continue;

                Quaternion rotation = Quaternion.FromToRotation(Vector3.up, up);
                float yaw = -settings.clockwiseRotationDegrees;
                if (settings.randomizeYaw)
                    yaw += (float)random.NextDouble() * 360f;
                rotation = Quaternion.AngleAxis(yaw, up) * rotation;

                string resourceId = $"{settingsIndex}:{spawnedCount}";
                if (!harvestedResourceIds.Contains(resourceId))
                {
                    HarvestableResource resource = Instantiate(
                        settings.prefab,
                        spawnPosition,
                        rotation,
                        generatedResourcesRoot);
                    resource.AssignStableResourceId(resourceId);
                }

                resourcePlacements.Add(new ResourcePlacement(spawnPosition, settings.minimumSpacing));
                resourceSnapshots.Add(new GalaxyResourceSaveEntry
                {
                    settingsIndex = settingsIndex,
                    resourceId = resourceId,
                    localPosition = transform.InverseTransformPoint(spawnPosition),
                    localRotation = Quaternion.Inverse(transform.rotation) * rotation,
                    minimumSpacing = settings.minimumSpacing
                });
                spawnedCount++;
            }

            if (spawnedCount < settings.count)
            {
                Debug.LogWarning(
                    $"VoxelQuadSphereWorld: spawned {spawnedCount}/{settings.count} of {settings.prefab.name}. " +
                    "Reduce its minimum spacing or increase its placement attempts.",
                    this);
            }
        }
    
}

    bool TryRestoreResourceSnapshot()
    {
        if (!loadingResourceSnapshot || savedResourceSnapshot == null)
            return false;

        foreach (GalaxyResourceSaveEntry entry in savedResourceSnapshot)
        {
            if (entry == null
                || entry.settingsIndex < 0
                || entry.settingsIndex >= resourceSpawnSettings.Count
                || resourceSpawnSettings[entry.settingsIndex] == null
                || resourceSpawnSettings[entry.settingsIndex].prefab == null
                || string.IsNullOrEmpty(entry.resourceId))
            {
                Debug.LogWarning(
                    "VoxelQuadSphereWorld: resource snapshot no longer matches the spawn settings; regenerating resource placement.",
                    this);
                loadingResourceSnapshot = false;
                savedResourceSnapshot = null;
                return false;
            }
        }

        foreach (GalaxyResourceSaveEntry entry in savedResourceSnapshot)
        {
            Vector3 worldPosition = transform.TransformPoint(entry.localPosition);
            Quaternion worldRotation = transform.rotation * entry.localRotation;
            resourcePlacements.Add(new ResourcePlacement(worldPosition, entry.minimumSpacing));
            resourceSnapshots.Add(entry);

            if (harvestedResourceIds.Contains(entry.resourceId))
                continue;

            HarvestableResource resource = Instantiate(
                resourceSpawnSettings[entry.settingsIndex].prefab,
                worldPosition,
                worldRotation,
                generatedResourcesRoot);
            resource.AssignStableResourceId(entry.resourceId);
        }

        Debug.Log($"VoxelQuadSphereWorld: restored {resourceSnapshots.Count} resource placements from the planet snapshot.", this);
        loadingResourceSnapshot = false;
        savedResourceSnapshot = null;
        return true;
    }

    void ClearGeneratedResources()
    {
        resourcePlacements.Clear();
        if (generatedResourcesRoot == null)
            return;

        Destroy(generatedResourcesRoot.gameObject);
        generatedResourcesRoot = null;
    }

    public bool TryFindPlanetSurface(Vector3 worldDirection, out RaycastHit closestHit)
    {
        closestHit = default;
        Vector3 center = GetPlanetCenterWorld();
        float worldScale = Mathf.Max(
            Mathf.Abs(transform.lossyScale.x),
            Mathf.Abs(transform.lossyScale.y),
            Mathf.Abs(transform.lossyScale.z));
        float outerDistance = (voxelOuterRadius + 10f) * worldScale;
        float castDistance = (maxDepth + 20f) * worldScale;
        Vector3 origin = center + worldDirection.normalized * outerDistance;
        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            -worldDirection.normalized,
            castDistance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        float closestDistance = float.PositiveInfinity;
        foreach (RaycastHit hit in hits)
        {
            if (!terrainColliders.Contains(hit.collider) || hit.distance >= closestDistance)
                continue;

            closestDistance = hit.distance;
            closestHit = hit;
        }

        return closestDistance < float.PositiveInfinity;
    }

    public bool IsTerrainCollider(Collider value)
    {
        return value != null && terrainColliders.Contains(value);
    }

    public void RespawnSurfacePropsForPreview()
    {
        surfaceDecorationSystem?.RespawnForPreview();
    }

    public void ClearGeneratedSurfaceProps()
    {
        surfaceDecorationSystem?.ClearGeneratedSurfaceProps();
    }

    bool IsResourcePositionValid(Vector3 position, HarvestableResourceSpawnSettings settings)
    {
        if (playerSpawn != null && settings.playerClearRadius > 0f
            && (position - playerSpawn.position).sqrMagnitude < settings.playerClearRadius * settings.playerClearRadius)
            return false;

        for (int i = 0; i < resourcePlacements.Count; i++)
        {
            ResourcePlacement existing = resourcePlacements[i];
            float requiredSpacing = Mathf.Max(settings.minimumSpacing, existing.MinimumSpacing);
            if ((position - existing.Position).sqrMagnitude < requiredSpacing * requiredSpacing)
                return false;
        }

        return true;
    }

    static Vector3 GetRandomSphereDirection(System.Random random)
    {
        float y = (float)(random.NextDouble() * 2.0 - 1.0);
        float angle = (float)(random.NextDouble() * Mathf.PI * 2.0);
        float horizontal = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
        return new Vector3(horizontal * Mathf.Cos(angle), y, horizontal * Mathf.Sin(angle));
    }

    public Vector3 GetStreamingSurfaceDirection(System.Random random, float angularRadiusDegrees = 5f)
    {
        Vector3 center = spawnDirectionLocal.sqrMagnitude > 0.001f
            ? spawnDirectionLocal.normalized
            : Vector3.up;
        Vector3 tangentX = Vector3.Cross(
            center, Mathf.Abs(Vector3.Dot(center, Vector3.up)) < 0.9f ? Vector3.up : Vector3.forward).normalized;
        Vector3 tangentY = Vector3.Cross(center, tangentX).normalized;
        float radius = Mathf.Sqrt((float)random.NextDouble()) * angularRadiusDegrees * Mathf.Deg2Rad;
        float azimuth = (float)random.NextDouble() * Mathf.PI * 2f;
        Vector3 tangent = tangentX * Mathf.Cos(azimuth) + tangentY * Mathf.Sin(azimuth);
        return (center * Mathf.Cos(radius) + tangent * Mathf.Sin(radius)).normalized;
    }

    void GenerateChunkData(VoxelQuadSphereChunk chunk)
    {
        if (loadingCompleteSnapshot
            && modifiedChunkCache.TryGetValue(chunk.Key, out byte[] completeSaved)
            && completeSaved.Length == chunk.Voxels.Length)
        {
            System.Array.Copy(completeSaved, chunk.Voxels, completeSaved.Length);
            chunk.ClearModifiedFlag();
            chunk.MarkDirty();
            return;
        }

        QuadSphereChunkKey key = chunk.Key;
        int originU = key.ChunkU * VoxelTypes.ChunkSize;
        int originV = key.ChunkV * VoxelTypes.ChunkSize;
        int originDepth = key.ChunkDepth * VoxelTypes.ChunkSize;

        for (int z = 0; z < VoxelTypes.ChunkSize; z++)
        {
            for (int y = 0; y < VoxelTypes.ChunkSize; y++)
            {
                for (int x = 0; x < VoxelTypes.ChunkSize; x++)
                {
                    int cellU = originU + x;
                    int cellV = originV + y;
                    int depth = originDepth + z;
                    byte voxel = VoxelQuadSphereTerrain.GenerateVoxel(
                        key.Face, cellU, cellV, depth,
                        faceGridSize, maxDepth, innerSolidDepthLayers, seed, planetCenterLocal,
                        voxelOuterRadius, planetRadius, terrainSettings,
                        riverSystem != null ? riverSystem.GetCarveDepth(key.Face, cellU, cellV) : 0f);
                    chunk.Voxels[VoxelTypes.ToIndex(x, y, z)] = voxel;
                }
            }
        }

        if (modifiedChunkCache.TryGetValue(chunk.Key, out byte[] saved)
            && saved.Length == chunk.Voxels.Length)
        {
            bool appliedChanges = false;
            if (migrateSavedTerrainChanges)
            {
                for (int z = 0; z < VoxelTypes.ChunkSize; z++)
                {
                    for (int y = 0; y < VoxelTypes.ChunkSize; y++)
                    {
                        for (int x = 0; x < VoxelTypes.ChunkSize; x++)
                        {
                            int cellU = originU + x;
                            int cellV = originV + y;
                            int depth = originDepth + z;
                            int index = VoxelTypes.ToIndex(x, y, z);
                            byte previousGenerated = VoxelQuadSphereTerrain.GenerateVoxel(
                                key.Face, cellU, cellV, depth,
                                faceGridSize, maxDepth, innerSolidDepthLayers, seed,
                                planetCenterLocal, voxelOuterRadius, planetRadius, previousTerrainSettings);
                            if (saved[index] == previousGenerated)
                                continue;
                            chunk.Voxels[index] = saved[index];
                            appliedChanges = true;
                        }
                    }
                }
            }
            else
            {
                System.Array.Copy(saved, chunk.Voxels, saved.Length);
                appliedChanges = true;
            }

            if (appliedChanges)
                chunk.MarkModified();
            else
                chunk.ClearModifiedFlag();
        }
        else
        {
            chunk.ClearModifiedFlag();
        }
        chunk.MarkDirty();
    }

    byte SampleVoxelAt(QuadSphereVoxelAddress address)
    {
        if (address.U < 0 || address.V < 0 || address.Depth < 0
            || address.U >= faceGridSize || address.V >= faceGridSize || address.Depth >= maxDepth)
            return VoxelTypes.Air;

        QuadSphereChunkKey key = AddressToChunkKey(address);
        Vector3Int local = AddressToLocalCoord(address);

        if (chunks.TryGetValue(key, out VoxelQuadSphereChunk chunk))
            return chunk.GetLocalVoxel(local.x, local.y, local.z);

        if (modifiedChunkCache.TryGetValue(key, out byte[] saved)
            && saved.Length == VoxelTypes.ChunkSize * VoxelTypes.ChunkSize * VoxelTypes.ChunkSize)
        {
            return saved[VoxelTypes.ToIndex(local.x, local.y, local.z)];
        }

        return VoxelQuadSphereTerrain.GenerateVoxel(
            address.Face, address.U, address.V, address.Depth,
            faceGridSize, maxDepth, innerSolidDepthLayers, seed, planetCenterLocal,
            voxelOuterRadius, planetRadius, terrainSettings,
            riverSystem != null ? riverSystem.GetCarveDepth(address.Face, address.U, address.V) : 0f);
    }

    public bool DigVoxel(QuadSphereVoxelAddress address)
    {
return SetVoxel(address, VoxelTypes.Air);
    
}

    public bool SetVoxel(QuadSphereVoxelAddress address, byte value)
    {
if (readOnlyFlightStreaming)
            return false;
        if (address.U < 0 || address.V < 0 || address.Depth < 0
            || address.U >= faceGridSize || address.V >= faceGridSize || address.Depth >= maxDepth)
            return false;

        QuadSphereChunkKey key = AddressToChunkKey(address);
        if (!chunks.TryGetValue(key, out VoxelQuadSphereChunk chunk))
        {
            if (!streamingLargePlanet)
                return false;
            LoadChunk(key);
            if (!chunks.TryGetValue(key, out chunk))
                return false;
        }

        Vector3Int local = AddressToLocalCoord(address);
        chunk.SetLocalVoxel(local.x, local.y, local.z, value);
        MarkChunkAndNeighborsDirty(key, local);
        MarkCrossFaceNeighborsDirty(address);
        VoxelChanged?.Invoke(address);
        riverSystem?.NotifyVoxelChanged(address);
        return true;
    
}

    void MarkCrossFaceNeighborsDirty(QuadSphereVoxelAddress address)
    {
        if (address.U == 0)
            MarkRemappedNeighborDirty(new QuadSphereVoxelAddress(address.Face, -1, address.V, address.Depth));
        if (address.U == faceGridSize - 1)
            MarkRemappedNeighborDirty(new QuadSphereVoxelAddress(address.Face, faceGridSize, address.V, address.Depth));
        if (address.V == 0)
            MarkRemappedNeighborDirty(new QuadSphereVoxelAddress(address.Face, address.U, -1, address.Depth));
        if (address.V == faceGridSize - 1)
            MarkRemappedNeighborDirty(new QuadSphereVoxelAddress(address.Face, address.U, faceGridSize, address.Depth));
    }

    void MarkRemappedNeighborDirty(QuadSphereVoxelAddress address)
    {
        QuadSphereVoxelAddress remapped = VoxelQuadSphereMapping.RemapAcrossFace(address, faceGridSize);
        MarkChunkDirty(AddressToChunkKey(remapped));
    }

    public bool TryDigAtLocalPoint(Vector3 localPoint)
    {
if (!VoxelQuadSphereMapping.TryLocalPointToVoxel(
                localPoint, planetCenterLocal, voxelOuterRadius, faceGridSize, maxDepth, out QuadSphereVoxelAddress address))
            return false;

        return DigVoxel(address);
    
}

    static QuadSphereChunkKey AddressToChunkKey(QuadSphereVoxelAddress address)
    {
        return new QuadSphereChunkKey(
            address.Face,
            VoxelTypes.FloorDiv(address.U, VoxelTypes.ChunkSize),
            VoxelTypes.FloorDiv(address.V, VoxelTypes.ChunkSize),
            VoxelTypes.FloorDiv(address.Depth, VoxelTypes.ChunkSize)
        );
    }

    static Vector3Int AddressToLocalCoord(QuadSphereVoxelAddress address)
    {
        return new Vector3Int(
            VoxelTypes.Mod(address.U, VoxelTypes.ChunkSize),
            VoxelTypes.Mod(address.V, VoxelTypes.ChunkSize),
            VoxelTypes.Mod(address.Depth, VoxelTypes.ChunkSize)
        );
    }

    void MarkChunkAndNeighborsDirty(QuadSphereChunkKey key, Vector3Int local)
    {
        if (chunks.TryGetValue(key, out VoxelQuadSphereChunk chunk))
            chunk.MarkDirty();

        if (local.x == 0)
            MarkChunkDirty(new QuadSphereChunkKey(key.Face, key.ChunkU - 1, key.ChunkV, key.ChunkDepth));
        if (local.x == VoxelTypes.ChunkSize - 1)
            MarkChunkDirty(new QuadSphereChunkKey(key.Face, key.ChunkU + 1, key.ChunkV, key.ChunkDepth));

        if (local.y == 0)
            MarkChunkDirty(new QuadSphereChunkKey(key.Face, key.ChunkU, key.ChunkV - 1, key.ChunkDepth));
        if (local.y == VoxelTypes.ChunkSize - 1)
            MarkChunkDirty(new QuadSphereChunkKey(key.Face, key.ChunkU, key.ChunkV + 1, key.ChunkDepth));

        if (local.z == 0)
            MarkChunkDirty(new QuadSphereChunkKey(key.Face, key.ChunkU, key.ChunkV, key.ChunkDepth - 1));
        if (local.z == VoxelTypes.ChunkSize - 1)
            MarkChunkDirty(new QuadSphereChunkKey(key.Face, key.ChunkU, key.ChunkV, key.ChunkDepth + 1));
    }

    void MarkChunkDirty(QuadSphereChunkKey key)
    {
        if (chunks.TryGetValue(key, out VoxelQuadSphereChunk neighbor))
            neighbor.MarkDirty();
    }

    void MarkLoadedNeighborsDirty(QuadSphereChunkKey key)
    {
        MarkChunkDirty(new QuadSphereChunkKey(key.Face, key.ChunkU - 1, key.ChunkV, key.ChunkDepth));
        MarkChunkDirty(new QuadSphereChunkKey(key.Face, key.ChunkU + 1, key.ChunkV, key.ChunkDepth));
        MarkChunkDirty(new QuadSphereChunkKey(key.Face, key.ChunkU, key.ChunkV - 1, key.ChunkDepth));
        MarkChunkDirty(new QuadSphereChunkKey(key.Face, key.ChunkU, key.ChunkV + 1, key.ChunkDepth));
        MarkChunkDirty(new QuadSphereChunkKey(key.Face, key.ChunkU, key.ChunkV, key.ChunkDepth - 1));
        MarkChunkDirty(new QuadSphereChunkKey(key.Face, key.ChunkU, key.ChunkV, key.ChunkDepth + 1));
    }

    void PlacePlayerAtSpawn()
    {
        if (!autoPlacePlayerOnStart || playerSpawn == null)
            return;

        Vector3 preferredDirection = spawnDirectionLocal.sqrMagnitude < 0.001f
            ? Vector3.up
            : spawnDirectionLocal.normalized;
        Vector3 direction = FindSafeSpawnDirection(preferredDirection);
        float surfaceRadius = GetProceduralSurfaceRadius(direction);
        Vector3 localSpawn = planetCenterLocal + direction * (surfaceRadius + spawnHeightOffset);
        Vector3 worldPosition = transform.TransformPoint(localSpawn);
        Quaternion worldRotation = PlanetGravity.GetSurfaceRotation(worldPosition, GetPlanetCenterWorld());
        VoxelPlanetPlayerController playerController = playerSpawn.GetComponent<VoxelPlanetPlayerController>();
        if (playerController != null)
        {
            playerController.TeleportTo(worldPosition, worldRotation);
            return;
        }

        Rigidbody playerBody = playerSpawn.GetComponent<Rigidbody>();

        if (playerBody != null)
        {
            playerBody.position = worldPosition;
            playerBody.rotation = worldRotation;
            playerBody.velocity = Vector3.zero;
            playerBody.angularVelocity = Vector3.zero;
            return;
        }

        playerSpawn.SetPositionAndRotation(worldPosition, worldRotation);
    }

    Vector3 FindSafeSpawnDirection(Vector3 preferredDirection)
    {
        if (HasSolidSpawnPatch(preferredDirection))
            return preferredDirection;

        Vector3 tangentX = Vector3.Cross(
            preferredDirection,
            Mathf.Abs(Vector3.Dot(preferredDirection, Vector3.up)) < 0.9f
                ? Vector3.up
                : Vector3.forward
        ).normalized;
        Vector3 tangentY = Vector3.Cross(preferredDirection, tangentX).normalized;

        const int samplesPerRing = 12;
        for (int ring = 1; ring <= 12; ring++)
        {
            float angle = ring * 2.5f * Mathf.Deg2Rad;
            for (int sample = 0; sample < samplesPerRing; sample++)
            {
                float azimuth = sample / (float)samplesPerRing * Mathf.PI * 2f;
                Vector3 tangent = tangentX * Mathf.Cos(azimuth) + tangentY * Mathf.Sin(azimuth);
                Vector3 candidate = (preferredDirection * Mathf.Cos(angle) + tangent * Mathf.Sin(angle)).normalized;
                if (HasSolidSpawnPatch(candidate))
                    return candidate;
            }
        }

        Debug.LogWarning("VoxelQuadSphereWorld: No safe spawn patch found; using the requested direction.");
        return preferredDirection;
    }

    bool HasSolidSpawnPatch(Vector3 direction)
    {
        float surfaceRadius = GetProceduralSurfaceRadius(direction);
        Vector3 surfacePoint = planetCenterLocal + direction.normalized * (surfaceRadius - 0.5f);
        if (!VoxelQuadSphereMapping.TryLocalPointToVoxel(
                surfacePoint, planetCenterLocal, voxelOuterRadius,
                faceGridSize, maxDepth, out QuadSphereVoxelAddress center))
            return false;

        if (!VoxelTypes.IsSolid(SampleVoxelAt(center)))
            return false;

        QuadSphereVoxelAddress[] neighbors =
        {
            new QuadSphereVoxelAddress(center.Face, center.U + 1, center.V, center.Depth),
            new QuadSphereVoxelAddress(center.Face, center.U - 1, center.V, center.Depth),
            new QuadSphereVoxelAddress(center.Face, center.U, center.V + 1, center.Depth),
            new QuadSphereVoxelAddress(center.Face, center.U, center.V - 1, center.Depth)
        };

        foreach (QuadSphereVoxelAddress neighbor in neighbors)
        {
            QuadSphereVoxelAddress remapped = VoxelQuadSphereMapping.RemapAcrossFace(neighbor, faceGridSize);
            if (!VoxelTypes.IsSolid(SampleVoxelAt(remapped)))
                return false;
        }

        return true;
    }
}
