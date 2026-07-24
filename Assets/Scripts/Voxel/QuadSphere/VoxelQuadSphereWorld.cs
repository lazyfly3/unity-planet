using System.Collections;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;
using UnityEngine;

public readonly struct VoxelTerrainSurfacePose
{
    public Vector3 Point { get; }
    public Vector3 Normal { get; }
    public Collider Collider { get; }
    public bool IsValid => Collider != null;

    public VoxelTerrainSurfacePose(Vector3 point, Vector3 normal, Collider collider)
    {
        Point = point;
        Normal = normal;
        Collider = collider;
    }

    public static VoxelTerrainSurfacePose FromRaycastHit(RaycastHit hit)
    {
        return new VoxelTerrainSurfacePose(hit.point, hit.normal, hit.collider);
    }
}

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

    sealed class GroundChunkBuildWork
    {
        public QuadSphereChunkKey Key;
        public int DataRevision;
        public bool HadCollider;
        public byte[] PaddedVoxels;
        public int NextSample;
        public Task<VoxelQuadSphereMeshData> MeshTask;
    }

    sealed class GroundColliderBakeWork
    {
        public QuadSphereChunkKey Key;
        public int DataRevision;
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
    readonly Dictionary<QuadSphereChunkKey, Vector2Int> generatedSurfaceDepthRangeCache =
        new Dictionary<QuadSphereChunkKey, Vector2Int>();
    readonly HashSet<string> harvestedResourceIds = new HashSet<string>();
    readonly HashSet<Collider> terrainColliders = new HashSet<Collider>();
    readonly List<Collider> terrainColliderProbeBuffer = new List<Collider>();
    readonly List<ResourcePlacement> resourcePlacements = new List<ResourcePlacement>();
    readonly List<GalaxyResourceSaveEntry> resourceSnapshots = new List<GalaxyResourceSaveEntry>();
    Transform generatedResourcesRoot;
    GalaxyResourceSaveEntry[] savedResourceSnapshot;
    bool loadingResourceSnapshot;
    bool loadingCompleteSnapshot;
    bool loadingCompleteMeshSnapshot;
    bool savedVoxelCacheMatchesCurrentGrid;
    bool bulkLoadingPlanet;
    bool generationComplete;
    bool restoreLogTrackAfterGeneration;
    bool migrateSavedTerrainChanges;
    PlanetTerrainSettings previousTerrainSettings;
    Material runtimeDirtMaterial;
    Material runtimeStoneMaterial;
    PlanetLowPolyVisualProfile visualProfile;
    PlanetCelestialProfile celestialProfile = PlanetCelestialProfile.CreateCompatibleDefault();
    bool streamingLargePlanet;
    bool streamingPassRunning;
    bool startupGenerationStarted;
    bool startupGenerationFinished;
    bool initialSurfaceGenerationFailed;
    bool initialPlayerPlaced;
    bool activeSurfaceRegionReady;
    bool waterReady;
    bool scenePlacementReady;
    bool initialSurfaceStreamingReady;
    bool hasInitialSurfaceHit;
    RaycastHit initialSurfaceHit;
    bool hasInitialSurfacePose;
    VoxelTerrainSurfacePose initialSurfacePose;
    bool hasReadySurfaceCutoutCenter;
    Vector3 readySurfaceCutoutCenterWorld;
    float readySurfaceCutoutRadius;
    Coroutine initialSurfaceRecoveryRoutine;
    bool streamingSurfaceCompletionStarted;
    float nextStreamingUpdateTime;
    QuadSphereChunkKey lastStreamingAnchor;
    readonly HashSet<QuadSphereChunkKey> desiredStreamingChunks = new HashSet<QuadSphereChunkKey>();
    readonly HashSet<QuadSphereChunkKey> streamingPassChunks = new HashSet<QuadSphereChunkKey>();
    readonly List<QuadSphereChunkKey> streamingBuildQueue = new List<QuadSphereChunkKey>();
    readonly HashSet<QuadSphereChunkKey> retainedGroundChunks = new HashSet<QuadSphereChunkKey>();
    readonly HashSet<QuadSphereChunkKey> readinessChunks = new HashSet<QuadSphereChunkKey>();
    readonly HashSet<QuadSphereChunkKey> initialSurfaceChunks = new HashSet<QuadSphereChunkKey>();
    readonly List<QuadSphereChunkKey> initialSurfaceBuildQueue = new List<QuadSphereChunkKey>();
    readonly HashSet<QuadSphereChunkKey> pinnedLandingChunks = new HashSet<QuadSphereChunkKey>();
    readonly HashSet<QuadSphereChunkKey> retainedFlightChunks = new HashSet<QuadSphereChunkKey>();
    readonly List<QuadSphereChunkKey> flightBuildQueue = new List<QuadSphereChunkKey>();
    readonly HashSet<QuadSphereChunkKey> flightScheduledChunks = new HashSet<QuadSphereChunkKey>();
    readonly List<FlightChunkBuildWork> flightMeshBuilds = new List<FlightChunkBuildWork>();
    readonly List<FlightColliderBakeWork> flightColliderBakes = new List<FlightColliderBakeWork>();
    readonly Stack<byte[]> flightVoxelBufferPool = new Stack<byte[]>();
    readonly List<QuadSphereChunkKey> groundBuildQueue = new List<QuadSphereChunkKey>();
    readonly HashSet<QuadSphereChunkKey> groundScheduledChunks = new HashSet<QuadSphereChunkKey>();
    readonly List<GroundChunkBuildWork> groundMeshBuilds = new List<GroundChunkBuildWork>();
    readonly List<GroundColliderBakeWork> groundColliderBakes = new List<GroundColliderBakeWork>();
    readonly float[] chunkSurfaceNoiseCache =
        new float[VoxelTypes.ChunkSize * VoxelTypes.ChunkSize];
    readonly float[] chunkRiverCarveCache =
        new float[VoxelTypes.ChunkSize * VoxelTypes.ChunkSize];
    FlightChunkBuildWork activeFlightVoxelWork;
    GroundChunkBuildWork activeGroundVoxelWork;
    bool readOnlyFlightStreaming;
    Transform flightStreamingTarget;
    Vector3 predictedFlightDirection;
    float requestedFlightDistanceAhead;
    float readyDistanceAhead;

    const int LargePlanetFaceGridSize = 2048;
    const int InitialSpawnChunkRadius = 2;
    const int ActiveStreamingChunkRadius = 4;
    const float FarLodChunkEdgeSafety = 0.25f;
    const float FarLodMaximumTransitionWidth = 4.5f;
    const float StreamingRefreshSeconds = 0.35f;
    const int FlightLocalChunkRadius = 5;
    static readonly float[] InitialSurfaceProbeAngles =
    {
        0.02f,
        0.08f,
        0.25f,
        0.50f
    };
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
    const int GroundMaxDirtyMeshRebuildsPerFrame = 1;
    const int GroundMaxMeshWorkers = 2;
    const int GroundMaxMeshAppliesPerFrame = 1;
    const int GroundMaxColliderAppliesPerFrame = 1;
    const int GroundColliderApplyIntervalFrames = 3;
    const float GroundStreamingFrameBudgetSeconds = 0.0025f;
    int nextFlightColliderApplyFrame;
    int nextGroundColliderApplyFrame;
    float groundWorkFrameBudgetSeconds = GroundStreamingFrameBudgetSeconds;

    public event Action<QuadSphereVoxelAddress> VoxelChanged;

    public int Seed => seed;
    public bool UsePlanetGeneration => true;
    public bool IsGenerationComplete => generationComplete
        && (readOnlyFlightStreaming
            || (activeSurfaceRegionReady
                && waterReady
                && scenePlacementReady
                && (surfaceDecorationSystem == null
                    || surfaceDecorationSystem.IsGenerationComplete)));
    public bool IsInitialSurfaceReady => !streamingLargePlanet || HasLiveInitialSurface();
    public bool IsInitialPlayerPlaced => initialPlayerPlaced;
    public bool IsActiveSurfaceRegionReady => readOnlyFlightStreaming
        ? IsFlightRegionReady
        : activeSurfaceRegionReady;
    public bool IsWaterReady => readOnlyFlightStreaming || waterReady;
    public bool IsScenePlacementReady => readOnlyFlightStreaming || scenePlacementReady;
    public bool IsSurfaceEntryVisualReady
    {
        get
        {
            if (!startupGenerationFinished || !IsInitialSurfaceReady)
                return false;
            if (!streamingLargePlanet)
                return true;

            return IsGroundStreamingRegionReady(
                spawnDirectionLocal,
                InitialSpawnChunkRadius,
                true);
        }
    }
    public bool HasStartedSurfaceGeneration => startupGenerationStarted;
    public bool HasFinishedSurfaceGeneration => startupGenerationFinished;
    public bool InitialSurfaceGenerationFailed => initialSurfaceGenerationFailed;
    public bool IsStreamingPassRunning => streamingPassRunning;
    public bool IsInitialSurfaceRecoveryRunning => initialSurfaceRecoveryRoutine != null;
    public int PinnedLandingChunkCount => pinnedLandingChunks.Count;
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

    public bool TryGetReadySurfaceCutoutCenter(out Vector3 center)
    {
        return TryGetReadySurfaceCutout(out center, out _);
    }

    public bool TryGetReadySurfaceCutout(out Vector3 center, out float radius)
    {
        if (!streamingLargePlanet)
        {
            center = playerSpawn != null
                ? playerSpawn.position
                : GetPlanetCenterWorld() + Vector3.up * planetRadius;
            radius = Mathf.Max(96f, planetRadius * 0.055f);
            return true;
        }

        center = readySurfaceCutoutCenterWorld;
        radius = readySurfaceCutoutRadius;
        return IsInitialSurfaceReady && hasReadySurfaceCutoutCenter;
    }
    public PlanetSurfaceDecorationSystem SurfaceDecorationSystem => surfaceDecorationSystem;
    public PlanetLowPolyVisualProfile VisualProfile => visualProfile;
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

    public void ConfigureVisualProfile(PlanetLowPolyVisualProfile profile)
    {
        visualProfile = profile != null ? profile.Clone() : null;
        if (visualProfile == null)
            return;

        visualProfile.ClampValues();
        Shader shader = Shader.Find("Voxel Planet/Low Poly Surface");
        if (shader == null)
        {
            Debug.LogError("VoxelQuadSphereWorld: missing Voxel Planet/Low Poly Surface shader.", this);
            return;
        }
        ApplyLowPolyMaterial(runtimeDirtMaterial, shader, false);
        ApplyLowPolyMaterial(runtimeStoneMaterial, shader, true);
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
        startupGenerationStarted = false;
        startupGenerationFinished = false;
        initialSurfaceGenerationFailed = false;
        initialPlayerPlaced = false;
        activeSurfaceRegionReady = false;
        waterReady = false;
        scenePlacementReady = false;
        initialSurfaceStreamingReady = false;
        hasInitialSurfaceHit = false;
        initialSurfaceHit = default;
        hasReadySurfaceCutoutCenter = false;
        readySurfaceCutoutCenterWorld = Vector3.zero;
        readySurfaceCutoutRadius = 0f;
        streamingSurfaceCompletionStarted = false;
        pinnedLandingChunks.Clear();
        initialSurfaceChunks.Clear();
        initialSurfaceBuildQueue.Clear();
        if (initialSurfaceRecoveryRoutine != null)
        {
            StopCoroutine(initialSurfaceRecoveryRoutine);
            initialSurfaceRecoveryRoutine = null;
        }
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
        generatedSurfaceDepthRangeCache.Clear();
        harvestedResourceIds.Clear();
        loadingCompleteSnapshot = false;
        loadingCompleteMeshSnapshot = false;
        savedVoxelCacheMatchesCurrentGrid = false;
        migrateSavedTerrainChanges = false;
        previousTerrainSettings = null;
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

        savedVoxelCacheMatchesCurrentGrid = save.seed == planetSeed
            && save.faceGridSize == faceGridSize
            && save.maxDepth == maxDepth
            && save.chunkSize == VoxelTypes.ChunkSize;
        bool snapshotDimensionsMatch = save.hasFullVoxelSnapshot
            && savedVoxelCacheMatchesCurrentGrid
            && modifiedChunkCache.Count == GetExpectedChunkCount();
        bool riverSnapshotMatches = planetRiverSettings == null
            || !planetRiverSettings.enabled
            || (savedRiverData != null
                && savedRiverData.configurationHash == planetRiverSettings.CalculateHash());
        loadingCompleteSnapshot = snapshotDimensionsMatch
            && save.terrainConfigurationHash == CalculateTerrainConfigurationHash(terrainSettings)
            && riverSnapshotMatches;
        migrateSavedTerrainChanges = savedVoxelCacheMatchesCurrentGrid
            && !loadingCompleteSnapshot
            && modifiedChunkCache.Count > 0;
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

        if (!savedVoxelCacheMatchesCurrentGrid)
        {
            modifiedChunkCache.Clear();
            savedMeshCache.Clear();
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
            hash = hash * 31 + settings.shapeVersion;
            hash = hash * 31 + settings.continentScale.GetHashCode();
            hash = hash * 31 + settings.continentHeight.GetHashCode();
            hash = hash * 31 + settings.detailScale.GetHashCode();
            hash = hash * 31 + settings.detailHeight.GetHashCode();
            hash = hash * 31 + settings.ridgeHeight.GetHashCode();
            hash = hash * 31 + settings.continentThreshold.GetHashCode();
            hash = hash * 31 + settings.continentWarp.GetHashCode();
            hash = hash * 31 + settings.continentSharpness.GetHashCode();
            hash = hash * 31 + settings.mountainMask.GetHashCode();
            hash = hash * 31 + settings.oceanFloorDepth.GetHashCode();
            hash = hash * 31 + settings.terraceStrength.GetHashCode();
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

    void ApplyLowPolyMaterial(Material material, Shader shader, bool rockLayer)
    {
        if (material == null)
            return;
        material.shader = shader;
        material.SetColor("_Color", Color.white);
        material.SetColor("_LowlandColor", rockLayer
            ? Color.Lerp(visualProfile.lowlandColor, visualProfile.rockColor, 0.65f)
            : visualProfile.lowlandColor);
        material.SetColor("_HighlandColor", rockLayer
            ? Color.Lerp(visualProfile.highlandColor, visualProfile.rockColor, 0.72f)
            : visualProfile.highlandColor);
        material.SetColor("_CliffColor", visualProfile.cliffColor);
        material.SetColor("_RockColor", visualProfile.rockColor);
        material.SetColor("_AccentColor", visualProfile.accentColor);
        material.SetColor("_ShoreColor", visualProfile.shoreColor);
        material.SetColor("_SnowColor", visualProfile.snowColor);
        material.SetFloat("_FacetStrength", visualProfile.facetStrength);
        material.SetFloat("_LightingBands", visualProfile.lightingBands);
        material.SetFloat("_MacroColorSize", visualProfile.macroColorSize);
        material.SetFloat("_MacroVariation", visualProfile.macroVariation);
        material.SetFloat("_CliffSlope", visualProfile.cliffSlope);
        material.SetFloat("_PlanetRadius", planetRadius);
        material.SetFloat("_HeightScale", celestialProfile != null
            ? Mathf.Max(1f, celestialProfile.maximumTerrainElevation)
            : 140f);
        material.SetFloat("_SeaLevel", visualProfile.oceanLevel);
        material.SetFloat("_ShoreWidth", visualProfile.shoreWidth);
        material.SetFloat("_SnowLine", visualProfile.snowLine);
        material.SetFloat("_SnowAmount", visualProfile.snowAmount);
        material.SetFloat("_MinimumAmbient", 0.26f);
        Vector3 center = GetPlanetCenterWorld();
        material.SetVector("_PlanetCenter", new Vector4(center.x, center.y, center.z, 1f));
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

    public void SetSurfaceSpawnDirection(Vector3 worldDirection)
    {
        if (worldDirection.sqrMagnitude < 0.0001f)
            return;

        spawnDirectionLocal = transform.InverseTransformDirection(worldDirection).normalized;
    }

    public void RequestInitialSurfaceRegion(Vector3 worldDirection)
    {
        SetSurfaceSpawnDirection(worldDirection);
        if (!streamingLargePlanet || !startupGenerationFinished || IsInitialSurfaceReady
            || initialSurfaceRecoveryRoutine != null || !isActiveAndEnabled)
        {
            return;
        }

        initialSurfaceRecoveryRoutine = StartCoroutine(RecoverInitialSurfaceRegion());
    }

    public bool PrepareLandingSurface(Vector3 worldDirection, out RaycastHit hit)
    {
        Vector3 localDirection = worldDirection.sqrMagnitude > 0.0001f
            ? transform.InverseTransformDirection(worldDirection).normalized
            : (spawnDirectionLocal.sqrMagnitude > 0.0001f
                ? spawnDirectionLocal.normalized
                : Vector3.up);
        spawnDirectionLocal = localDirection;

        if (!streamingLargePlanet)
        {
            bool found = TryFindPlanetSurface(
                transform.TransformDirection(localDirection).normalized,
                out hit);
            if (found)
            {
                initialSurfaceHit = hit;
                hasInitialSurfaceHit = true;
                initialSurfacePose = VoxelTerrainSurfacePose.FromRaycastHit(hit);
                hasInitialSurfacePose = true;
                initialSurfaceStreamingReady = true;
            }
            return found;
        }

        if (!BuildInitialSurfacePatchSynchronously(localDirection))
        {
            hit = default;
            return false;
        }

        Physics.SyncTransforms();
        if (!TryFindLoadedSurfaceNearLocalDirection(localDirection, out hit))
            return false;

        initialSurfaceHit = hit;
        hasInitialSurfaceHit = true;
        initialSurfacePose = VoxelTerrainSurfacePose.FromRaycastHit(hit);
        hasInitialSurfacePose = true;
        initialSurfaceStreamingReady = true;
        CommitReadySurfaceCutout(
            hit,
            GetReadySpawnCutoutChunkRadius(localDirection));
        return true;
    }

    public bool PrepareLandingSurfacePose(
        Vector3 worldDirection,
        out VoxelTerrainSurfacePose pose)
    {
        Vector3 localDirection = worldDirection.sqrMagnitude > 0.0001f
            ? transform.InverseTransformDirection(worldDirection).normalized
            : (spawnDirectionLocal.sqrMagnitude > 0.0001f
                ? spawnDirectionLocal.normalized
                : Vector3.up);
        spawnDirectionLocal = localDirection;

        if (streamingLargePlanet && !BuildInitialSurfacePatchSynchronously(localDirection))
        {
            pose = default;
            return false;
        }

        Physics.SyncTransforms();
        if (!TryFindLoadedSurfacePoseNearLocalDirection(localDirection, out pose))
            return false;

        initialSurfacePose = pose;
        hasInitialSurfacePose = true;
        initialSurfaceStreamingReady = true;
        if (TryFindLoadedSurfaceNearLocalDirection(localDirection, out RaycastHit physicsHit))
        {
            initialSurfaceHit = physicsHit;
            hasInitialSurfaceHit = true;
        }
        CommitReadySurfaceCutout(
            pose,
            GetReadySpawnCutoutChunkRadius(localDirection));
        return true;
    }

    public bool TryFindInitialSurface(out RaycastHit hit)
    {
        if (streamingLargePlanet && !IsInitialSurfaceReady)
        {
            hit = default;
            return false;
        }

        if (hasInitialSurfaceHit && IsTerrainCollider(initialSurfaceHit.collider))
        {
            hit = initialSurfaceHit;
            return true;
        }

        Vector3 localDirection = spawnDirectionLocal.sqrMagnitude > 0.0001f
            ? spawnDirectionLocal.normalized
            : Vector3.up;
        Physics.SyncTransforms();
        bool found = TryFindLoadedSurfaceNearLocalDirection(localDirection, out hit);
        if (found)
        {
            initialSurfaceHit = hit;
            hasInitialSurfaceHit = true;
            initialSurfacePose = VoxelTerrainSurfacePose.FromRaycastHit(hit);
            hasInitialSurfacePose = true;
        }
        return found;
    }

    public bool TryFindInitialSurfacePose(out VoxelTerrainSurfacePose pose)
    {
        if (streamingLargePlanet && !IsInitialSurfaceReady)
        {
            pose = default;
            return false;
        }

        if (hasInitialSurfacePose
            && initialSurfacePose.IsValid
            && IsTerrainCollider(initialSurfacePose.Collider))
        {
            pose = initialSurfacePose;
            return true;
        }

        Vector3 localDirection = spawnDirectionLocal.sqrMagnitude > 0.0001f
            ? spawnDirectionLocal.normalized
            : Vector3.up;
        Physics.SyncTransforms();
        bool found = TryFindLoadedSurfacePoseNearLocalDirection(localDirection, out pose);
        if (found)
        {
            initialSurfacePose = pose;
            hasInitialSurfacePose = true;
        }
        return found;
    }

    bool HasLiveInitialSurface()
    {
        return initialSurfaceStreamingReady
            && hasInitialSurfacePose
            && initialSurfacePose.IsValid
            && IsTerrainCollider(initialSurfacePose.Collider)
            && hasReadySurfaceCutoutCenter;
    }

    public bool TryFindLoadedSurfaceNearDirection(Vector3 worldDirection, out RaycastHit hit)
    {
        Vector3 localDirection = worldDirection.sqrMagnitude > 0.0001f
            ? transform.InverseTransformDirection(worldDirection).normalized
            : (spawnDirectionLocal.sqrMagnitude > 0.0001f
                ? spawnDirectionLocal.normalized
                : Vector3.up);
        EnsureSurfaceChunkReady(localDirection);
        Physics.SyncTransforms();
        return TryFindLoadedSurfaceNearLocalDirection(localDirection, out hit);
    }

    public bool TryFindLoadedSurfacePoseNearDirection(
        Vector3 worldDirection,
        out VoxelTerrainSurfacePose pose)
    {
        Vector3 localDirection = worldDirection.sqrMagnitude > 0.0001f
            ? transform.InverseTransformDirection(worldDirection).normalized
            : (spawnDirectionLocal.sqrMagnitude > 0.0001f
                ? spawnDirectionLocal.normalized
                : Vector3.up);
        EnsureSurfaceChunkReady(localDirection);
        Physics.SyncTransforms();
        return TryFindLoadedSurfacePoseNearLocalDirection(localDirection, out pose);
    }

    public void ConfigureRiverSystem(PlanetRiverSettings settings, GalaxyRiverSaveData save)
    {
if (riverSystem == null)
            riverSystem = GetComponent<PlanetRiverSystem>();
        riverSystem?.Configure(this, settings, save);
    
}

    IEnumerator Start()
    {
        startupGenerationStarted = true;
        startupGenerationFinished = false;
        initialSurfaceGenerationFailed = false;

        if (readOnlyFlightStreaming)
        {
            yield return null;
            generationComplete = true;
            startupGenerationFinished = true;
            RefreshFlightStreamingPlan();
            yield break;
        }

        if (loadingUI == null)
            loadingUI = FindObjectOfType<PlanetLoadingUI>(true);
        PlanetSurfaceEntryCoordinator entryCoordinator =
            GetComponent<PlanetSurfaceEntryCoordinator>();
        if (entryCoordinator == null)
            entryCoordinator = gameObject.AddComponent<PlanetSurfaceEntryCoordinator>();
        if (!entryCoordinator.IsInitialized)
        {
            entryCoordinator.Initialize(this, loadingUI, false);
            entryCoordinator.MarkBuildingsRestored();
        }

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
            if (!playerBody.isKinematic)
            {
                playerBody.velocity = Vector3.zero;
                playerBody.angularVelocity = Vector3.zero;
            }
            playerBody.isKinematic = true;
        }

        SuspendLogTrackForGeneration();
        try
        {
            // Let the newly loaded scene render before expensive terrain work begins.
            yield return null;
            bool riversEnabled = riverSystem != null && riverSystem.GenerationEnabled;
            ReportLegacyLoadingProgress(
                0.03f,
                riversEnabled ? "正在恢复河网与湖泊" : "正在准备星球地形");
            if (riversEnabled)
                riverSystem?.PrepareHydrology();
            yield return null;
            if (streamingLargePlanet)
                yield return GenerateStreamingRegionIncremental(
                    spawnDirectionLocal,
                    InitialSpawnChunkRadius,
                    false,
                    true);
            else
                yield return GenerateEntirePlanetIncremental();
        }
        finally
        {
            RestoreLogTrackAfterGeneration();
        }

        ReportLegacyLoadingProgress(0.96f, "正在部署资源与玩家基地");
        yield return null;

        bool initialTerrainReady = false;
        initialPlayerPlaced = false;
        if (streamingLargePlanet)
        {
            Vector3 worldSpawnDirection = transform.TransformDirection(
                spawnDirectionLocal.sqrMagnitude > 0.0001f
                    ? spawnDirectionLocal.normalized
                    : Vector3.up);
            VoxelTerrainSurfacePose spawnSurface = default;
            initialTerrainReady = PrepareLandingSurfacePose(
                worldSpawnDirection,
                out spawnSurface);
            if (!initialTerrainReady)
            {
                // Initial terrain has one owner. A single physics-step retry lets
                // newly assigned MeshColliders enter the physics scene.
                yield return new WaitForFixedUpdate();
                Physics.SyncTransforms();
                initialTerrainReady = PrepareLandingSurfacePose(
                    worldSpawnDirection,
                    out spawnSurface);
            }

            const int startupRecoveryAttempts = 2;
            for (int attempt = 0;
                attempt < startupRecoveryAttempts && !initialTerrainReady;
                attempt++)
            {
                ReportLegacyLoadingProgress(0.74f, "正在构建着陆区域高精度地形");
                yield return GenerateStreamingRegionIncremental(
                    spawnDirectionLocal,
                    InitialSpawnChunkRadius,
                    false,
                    true);
                yield return new WaitForFixedUpdate();
                Physics.SyncTransforms();
                initialTerrainReady = PrepareLandingSurfacePose(
                    worldSpawnDirection,
                    out spawnSurface);
            }

            initialPlayerPlaced = initialTerrainReady && PlacePlayerAtSurface(spawnSurface);
        }
        else
        {
            initialPlayerPlaced = PlacePlayerAtSpawn(false);
            initialTerrainReady = initialPlayerPlaced;
        }

        if (!initialPlayerPlaced && !streamingLargePlanet)
        {
            initialPlayerPlaced = PlacePlayerAtSpawn(true);
        }

        initialSurfaceGenerationFailed = !initialTerrainReady;
        startupGenerationFinished = true;
        if (initialSurfaceGenerationFailed)
        {
            Debug.LogError(
                $"VoxelQuadSphereWorld: initial high-detail terrain generation failed. " +
                $"LoadedChunks={chunks.Count}, TerrainColliders={terrainColliders.Count}, " +
                $"PinnedLandingChunks={pinnedLandingChunks.Count}, " +
                $"SpawnDirection={spawnDirectionLocal}. The player remains locked.",
                this);
        }

        if (initialTerrainReady && !streamingLargePlanet)
        {
            activeSurfaceRegionReady = true;
            if (riverSystem != null && riverSystem.GenerationEnabled)
                riverSystem?.BuildWaterSurface();
            waterReady = true;
            if (surfaceDecorationSystem != null)
                yield return surfaceDecorationSystem.GenerateIncremental(4f);
            else
                SpawnHarvestableResources();
            scenePlacementReady = true;
            generationComplete = true;
        }

        if (playerController != null && initialTerrainReady)
            playerController.enabled = controllerWasEnabled;
        if (playerBody != null && playerController == null && initialTerrainReady)
            playerBody.isKinematic = bodyWasKinematic;

        if (initialTerrainReady && streamingLargePlanet)
        {
            lastStreamingAnchor = GetSurfaceAnchor(spawnDirectionLocal);
            StartStreamingSurfaceCompletion(spawnDirectionLocal);
        }
    }

    IEnumerator RecoverInitialSurfaceRegion()
    {
        if (!startupGenerationFinished)
        {
            initialSurfaceRecoveryRoutine = null;
            yield break;
        }

        const int maxRecoveryAttempts = 2;
        for (int attempt = 0; attempt < maxRecoveryAttempts && !IsInitialSurfaceReady; attempt++)
        {
            yield return GenerateStreamingRegionIncremental(
                spawnDirectionLocal,
                InitialSpawnChunkRadius,
                false,
                true);
            Vector3 worldSpawnDirection = transform.TransformDirection(
                spawnDirectionLocal.sqrMagnitude > 0.0001f
                    ? spawnDirectionLocal.normalized
                    : Vector3.up);
            bool terrainReady = PrepareLandingSurfacePose(
                worldSpawnDirection,
                out VoxelTerrainSurfacePose spawnSurface);
            if (terrainReady)
                PlacePlayerAtSurface(spawnSurface);
            if (!terrainReady)
                yield return new WaitForSecondsRealtime(0.05f);
        }

        initialSurfaceGenerationFailed = !IsInitialSurfaceReady;
        initialSurfaceRecoveryRoutine = null;
    }

    void StartStreamingSurfaceCompletion(Vector3 direction)
    {
        if (streamingSurfaceCompletionStarted)
            return;

        streamingSurfaceCompletionStarted = true;
        StartCoroutine(CompleteStreamingSurfaceInBackground(direction));
    }

    IEnumerator CompleteStreamingSurfaceInBackground(Vector3 direction)
    {
        yield return GenerateStreamingRegionIncremental(
            direction,
            ActiveStreamingChunkRadius,
            true);
        activeSurfaceRegionReady = IsGroundStreamingRegionReady(
            direction,
            ActiveStreamingChunkRadius);

        if (riverSystem != null && riverSystem.GenerationEnabled)
            riverSystem.BuildWaterSurface();
        waterReady = true;
        if (surfaceDecorationSystem != null)
            yield return surfaceDecorationSystem.GenerateIncremental(4f);
        else
            SpawnHarvestableResources();
        scenePlacementReady = true;
        generationComplete = activeSurfaceRegionReady;
    }

    void ReportLegacyLoadingProgress(float progress, string status)
    {
        if (GetComponent<PlanetSurfaceEntryCoordinator>() == null)
            loadingUI?.SetProgress(progress, status);
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
        CompleteGroundBackgroundWork();
        CompleteFlightBackgroundWork();
        RestoreLogTrackAfterGeneration();
        if (runtimeDirtMaterial != null)
            Destroy(runtimeDirtMaterial);
        if (runtimeStoneMaterial != null)
            Destroy(runtimeStoneMaterial);
    }

    void LateUpdate()
    {
        if (readOnlyFlightStreaming)
            return;

        UpdateGroundBackgroundWork();
        if (bulkLoadingPlanet || streamingPassRunning)
            return;

        int queued = 0;
        foreach (VoxelQuadSphereChunk chunk in chunks.Values)
        {
            if (chunk.IsDirty
                && !groundScheduledChunks.Contains(chunk.Key))
            {
                QueueGroundChunkBuild(chunk.Key);
                queued++;
                if (queued >= GroundMaxDirtyMeshRebuildsPerFrame)
                    break;
            }
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
        if (!streamingLargePlanet || !generationComplete || !startupGenerationFinished
            || streamingPassRunning
            || streamingTarget == null || Time.unscaledTime < nextStreamingUpdateTime)
            return;

        if (!IsInitialSurfaceReady)
        {
            RequestInitialSurfaceRegion(
                transform.TransformDirection(spawnDirectionLocal).normalized);
            return;
        }

        nextStreamingUpdateTime = Time.unscaledTime
            + (readOnlyFlightStreaming ? FlightStreamingRefreshSeconds : StreamingRefreshSeconds);
        Vector3 localPlayer = transform.InverseTransformPoint(streamingTarget.position);
        Vector3 direction = localPlayer - planetCenterLocal;
        if (direction.sqrMagnitude < 0.001f)
            return;

        QuadSphereChunkKey anchor = GetSurfaceAnchor(direction);
        if (anchor.Equals(lastStreamingAnchor) && !readOnlyFlightStreaming)
            return;

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

    bool HasGroundBackgroundWork =>
        activeGroundVoxelWork != null
        || groundBuildQueue.Count > 0
        || groundMeshBuilds.Count > 0
        || groundColliderBakes.Count > 0;

    void QueueGroundChunkBuild(QuadSphereChunkKey key)
    {
        if (groundScheduledChunks.Contains(key)
            || !chunks.TryGetValue(key, out VoxelQuadSphereChunk chunk)
            || (!chunk.IsDirty && chunk.HasMesh))
        {
            return;
        }

        groundScheduledChunks.Add(key);
        groundBuildQueue.Add(key);
    }

    void UpdateGroundBackgroundWork()
    {
        double deadline = Time.realtimeSinceStartupAsDouble
            + Mathf.Max(
                GroundStreamingFrameBudgetSeconds,
                groundWorkFrameBudgetSeconds);
        AdvanceGroundVoxelSampling(deadline);
        ApplyCompletedGroundMeshes();
        ApplyCompletedGroundColliders();
    }

    void AdvanceGroundVoxelSampling(double deadline)
    {
        int paddedSize = VoxelQuadSphereMesher.PaddedChunkSize;
        while (Time.realtimeSinceStartupAsDouble < deadline)
        {
            if (activeGroundVoxelWork == null)
            {
                if (groundMeshBuilds.Count >= GroundMaxMeshWorkers
                    || groundBuildQueue.Count == 0)
                {
                    return;
                }

                QuadSphereChunkKey key = groundBuildQueue[0];
                groundBuildQueue.RemoveAt(0);
                if (!chunks.TryGetValue(key, out VoxelQuadSphereChunk chunk)
                    || (!chunk.IsDirty && chunk.HasMesh))
                {
                    groundScheduledChunks.Remove(key);
                    continue;
                }

                activeGroundVoxelWork = new GroundChunkBuildWork
                {
                    Key = key,
                    DataRevision = chunk.DataRevision,
                    HadCollider = chunk.HasCollider,
                    PaddedVoxels = RentFlightVoxelBuffer()
                };
            }

            GroundChunkBuildWork work = activeGroundVoxelWork;
            if (!chunks.TryGetValue(
                    work.Key,
                    out VoxelQuadSphereChunk currentChunk)
                || currentChunk.DataRevision != work.DataRevision)
            {
                ReturnFlightVoxelBuffer(work.PaddedVoxels);
                groundScheduledChunks.Remove(work.Key);
                activeGroundVoxelWork = null;
                if (currentChunk != null)
                    QueueGroundChunkBuild(work.Key);
                continue;
            }

            int sampleCount = work.PaddedVoxels.Length;
            while (work.NextSample < sampleCount)
            {
                int index = work.NextSample++;
                int x = index % paddedSize;
                int yz = index / paddedSize;
                int y = yz % paddedSize;
                int z = yz / paddedSize;
                work.PaddedVoxels[index] = SampleGroundVoxel(
                    work.Key,
                    x - 1,
                    y - 1,
                    z - 1);

                if ((work.NextSample
                        & (FlightVoxelBudgetCheckInterval - 1)) == 0
                    && Time.realtimeSinceStartupAsDouble >= deadline)
                {
                    return;
                }
            }

            if (!chunks.TryGetValue(work.Key, out currentChunk)
                || currentChunk.DataRevision != work.DataRevision)
            {
                ReturnFlightVoxelBuffer(work.PaddedVoxels);
                groundScheduledChunks.Remove(work.Key);
                activeGroundVoxelWork = null;
                if (currentChunk != null)
                    QueueGroundChunkBuild(work.Key);
                continue;
            }

            byte[] voxelData = work.PaddedVoxels;
            QuadSphereChunkKey meshKey = work.Key;
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
            groundMeshBuilds.Add(work);
            activeGroundVoxelWork = null;
        }
    }

    byte SampleGroundVoxel(
        QuadSphereChunkKey key,
        int localU,
        int localV,
        int localDepth)
    {
        QuadSphereVoxelAddress address = new QuadSphereVoxelAddress(
            key.Face,
            key.ChunkU * VoxelTypes.ChunkSize + localU,
            key.ChunkV * VoxelTypes.ChunkSize + localV,
            key.ChunkDepth * VoxelTypes.ChunkSize + localDepth);
        if (address.Depth < 0 || address.Depth >= maxDepth)
            return VoxelTypes.Air;

        address = VoxelQuadSphereMapping.RemapAcrossFace(
            address,
            faceGridSize);
        if (address.U < 0
            || address.V < 0
            || address.U >= faceGridSize
            || address.V >= faceGridSize)
        {
            return VoxelTypes.Air;
        }
        return SampleVoxelAt(address);
    }

    void ApplyCompletedGroundMeshes()
    {
        int applied = 0;
        for (int i = 0;
             i < groundMeshBuilds.Count
             && applied < GroundMaxMeshAppliesPerFrame;)
        {
            GroundChunkBuildWork work = groundMeshBuilds[i];
            if (work.MeshTask == null || !work.MeshTask.IsCompleted)
            {
                i++;
                continue;
            }

            groundMeshBuilds.RemoveAt(i);
            if (work.MeshTask.IsFaulted)
            {
                groundScheduledChunks.Remove(work.Key);
                ReturnFlightVoxelBuffer(work.PaddedVoxels);
                Debug.LogError(
                    "VoxelQuadSphereWorld: background ground mesh failed for " +
                    $"{work.Key.Face}:{work.Key.ChunkU}:" +
                    $"{work.Key.ChunkV}:{work.Key.ChunkDepth}. " +
                    work.MeshTask.Exception?.GetBaseException().Message,
                    this);
                continue;
            }

            VoxelQuadSphereMeshData meshData = work.MeshTask.Result;
            if (!chunks.TryGetValue(
                    work.Key,
                    out VoxelQuadSphereChunk chunk)
                || chunk.DataRevision != work.DataRevision)
            {
                meshData.Release();
                ReturnFlightVoxelBuffer(work.PaddedVoxels);
                groundScheduledChunks.Remove(work.Key);
                if (chunk != null)
                    QueueGroundChunkBuild(work.Key);
                continue;
            }

            bool meshIsEmpty = meshData.IsEmpty;
            int meshRevision;
            try
            {
                meshRevision = chunk.ApplyMeshData(meshData, false);
            }
            finally
            {
                meshData.Release();
            }
            ReturnFlightVoxelBuffer(work.PaddedVoxels);
            if (meshIsEmpty)
            {
                groundScheduledChunks.Remove(work.Key);
            }
            else if (work.HadCollider)
            {
                // Existing terrain under the player must not lose collision for
                // several frames after a dig/edit. Its CPU mesh work is still
                // asynchronous, but swap and recook remain atomic this frame.
                chunk.BakeAndApplyCollider(meshRevision);
                groundScheduledChunks.Remove(work.Key);
            }
            else
            {
                groundColliderBakes.Add(new GroundColliderBakeWork
                {
                    Key = work.Key,
                    DataRevision = work.DataRevision,
                    MeshRevision = meshRevision
                });
            }
            applied++;
        }
    }

    void ApplyCompletedGroundColliders()
    {
        if (Time.frameCount < nextGroundColliderApplyFrame)
            return;

        int applied = 0;
        while (groundColliderBakes.Count > 0
            && applied < GroundMaxColliderAppliesPerFrame)
        {
            GroundColliderBakeWork work = groundColliderBakes[0];
            groundColliderBakes.RemoveAt(0);
            groundScheduledChunks.Remove(work.Key);
            if (chunks.TryGetValue(
                    work.Key,
                    out VoxelQuadSphereChunk chunk)
                && chunk.DataRevision == work.DataRevision
                && chunk.MeshRevision == work.MeshRevision)
            {
                chunk.BakeAndApplyCollider(work.MeshRevision);
            }
            else if (chunk != null && chunk.IsDirty)
            {
                QueueGroundChunkBuild(work.Key);
            }
            applied++;
        }

        if (applied > 0)
        {
            nextGroundColliderApplyFrame =
                Time.frameCount + GroundColliderApplyIntervalFrames;
        }
    }

    void CompleteGroundBackgroundWork()
    {
        var tasks = new List<Task>(groundMeshBuilds.Count);
        for (int i = 0; i < groundMeshBuilds.Count; i++)
        {
            if (groundMeshBuilds[i].MeshTask != null)
                tasks.Add(groundMeshBuilds[i].MeshTask);
        }
        if (tasks.Count > 0)
        {
            try
            {
                Task.WaitAll(tasks.ToArray());
            }
            catch (AggregateException)
            {
                // Normal update reports individual failures while the world lives.
            }
        }

        if (activeGroundVoxelWork != null)
        {
            ReturnFlightVoxelBuffer(
                activeGroundVoxelWork.PaddedVoxels);
            activeGroundVoxelWork = null;
        }
        for (int i = 0; i < groundMeshBuilds.Count; i++)
        {
            Task<VoxelQuadSphereMeshData> task =
                groundMeshBuilds[i].MeshTask;
            if (task != null
                && task.Status == TaskStatus.RanToCompletion)
            {
                task.Result.Release();
            }
            ReturnFlightVoxelBuffer(
                groundMeshBuilds[i].PaddedVoxels);
        }
        groundBuildQueue.Clear();
        groundMeshBuilds.Clear();
        groundColliderBakes.Clear();
        groundScheduledChunks.Clear();
    }

    IEnumerator GenerateStreamingRegionIncremental(
        Vector3 direction,
        int horizontalRadius,
        bool unloadOutsideRegion,
        bool spawnSurfaceOnly = false)
    {
        while (streamingPassRunning)
            yield return null;

        streamingPassRunning = true;
        try
        {
            float frameStartedAt = Time.realtimeSinceStartup;
            float frameBudgetSeconds = readOnlyFlightStreaming
                ? FlightStreamingFrameBudgetSeconds
                : spawnSurfaceOnly
                    ? Mathf.Clamp(generationFrameBudgetMilliseconds, 5f, 100f) * 0.001f
                    : 0.0025f;
            if (readOnlyFlightStreaming)
                BuildFlightDesiredStreamingChunks(direction, streamingPassChunks);
            else if (spawnSurfaceOnly)
                BuildSpawnSurfaceStreamingChunks(
                    direction,
                    Mathf.Max(InitialSpawnChunkRadius, horizontalRadius),
                    streamingPassChunks);
            else
                BuildDesiredStreamingChunks(direction, horizontalRadius, streamingPassChunks);

            QuadSphereChunkKey prioritySurfaceChunk = default;
            bool hasPrioritySurfaceChunk = spawnSurfaceOnly
                && TryGetSurfaceChunkAtDirection(direction, out prioritySurfaceChunk);
            if (hasPrioritySurfaceChunk)
            {
                int chunkDepthCount = Mathf.CeilToInt(maxDepth / (float)VoxelTypes.ChunkSize);
                for (int depthOffset = -1; depthOffset <= 1; depthOffset++)
                {
                    int chunkDepth = prioritySurfaceChunk.ChunkDepth + depthOffset;
                    if (chunkDepth < 0 || chunkDepth >= chunkDepthCount)
                        continue;
                    streamingPassChunks.Add(new QuadSphereChunkKey(
                        prioritySurfaceChunk.Face,
                        prioritySurfaceChunk.ChunkU,
                        prioritySurfaceChunk.ChunkV,
                        chunkDepth));
                }
            }

            // Never enumerate the mutable working set across yielded frames.
            // The exact spawn surface is first so the player gets collision
            // before the wider visual region finishes streaming.
            streamingBuildQueue.Clear();
            if (hasPrioritySurfaceChunk)
                streamingBuildQueue.Add(prioritySurfaceChunk);
            foreach (QuadSphereChunkKey key in streamingPassChunks)
            {
                if (!hasPrioritySurfaceChunk || !key.Equals(prioritySurfaceChunk))
                    streamingBuildQueue.Add(key);
            }

            if (hasPrioritySurfaceChunk)
            {
                bulkLoadingPlanet = true;
                if (!chunks.ContainsKey(prioritySurfaceChunk))
                    LoadChunk(prioritySurfaceChunk);
                bulkLoadingPlanet = false;

                if (chunks.TryGetValue(prioritySurfaceChunk, out VoxelQuadSphereChunk priorityChunk))
                {
                    if (priorityChunk.IsDirty || !priorityChunk.HasMesh)
                    {
                        priorityChunk.RebuildMesh(
                            SampleVoxelAt,
                            faceGridSize,
                            maxDepth,
                            voxelOuterRadius,
                            planetCenterLocal);
                    }
                    priorityChunk.EnsureColliderReady();
                }
                Physics.SyncTransforms();
            }

            bulkLoadingPlanet = true;
            for (int i = 0; i < streamingBuildQueue.Count; i++)
            {
                QuadSphereChunkKey key = streamingBuildQueue[i];
                if (!chunks.ContainsKey(key))
                    LoadChunk(key);
                if (Time.realtimeSinceStartup - frameStartedAt < frameBudgetSeconds)
                    continue;
                ReportLegacyLoadingProgress(0.12f, "正在加载着陆区域地形");
                yield return null;
                frameStartedAt = Time.realtimeSinceStartup;
            }
            bulkLoadingPlanet = false;

            groundWorkFrameBudgetSeconds = Mathf.Max(
                GroundStreamingFrameBudgetSeconds,
                frameBudgetSeconds);
            for (int i = 0; i < streamingBuildQueue.Count; i++)
            {
                QuadSphereChunkKey key = streamingBuildQueue[i];
                if (chunks.TryGetValue(key, out VoxelQuadSphereChunk chunk)
                    && (chunk.IsDirty || !chunk.HasMesh))
                {
                    QueueGroundChunkBuild(key);
                }
            }
            while (HasGroundBackgroundWork)
            {
                ReportLegacyLoadingProgress(
                    0.70f,
                    "正在后台构建地表网格");
                yield return null;
            }

            for (int i = 0; i < streamingBuildQueue.Count; i++)
            {
                QuadSphereChunkKey key = streamingBuildQueue[i];
                if (!chunks.TryGetValue(key, out VoxelQuadSphereChunk chunk))
                    continue;

                if (chunk.IsDirty || !chunk.HasMesh)
                {
                    chunk.RebuildMesh(
                        SampleVoxelAt,
                        faceGridSize,
                        maxDepth,
                        voxelOuterRadius,
                        planetCenterLocal);
                }

                if (!readOnlyFlightStreaming && chunk.MeshVertexCount > 0)
                    chunk.EnsureColliderReady();
                // The async pipeline already paced sampling, mesh upload and
                // collider cooking. Do not spend one extra frame per completed
                // chunk in this legacy fallback loop.
                if (chunk.IsCollisionReady)
                    continue;
                if (Time.realtimeSinceStartup - frameStartedAt < frameBudgetSeconds)
                    continue;
                ReportLegacyLoadingProgress(0.70f, "正在构建无缝地表");
                yield return null;
                frameStartedAt = Time.realtimeSinceStartup;
            }

            if (unloadOutsideRegion)
            {
                if (readOnlyFlightStreaming)
                {
                    UnloadChunksOutside(streamingPassChunks);
                }
                else
                {
                    TryUnloadGroundChunks(direction, horizontalRadius, streamingPassChunks);
                }
            }

            generationComplete = true;
            if (readOnlyFlightStreaming)
                readyDistanceAhead = CalculateReadyDistanceAhead(direction);
            else if (!spawnSurfaceOnly
                && IsGroundStreamingRegionReady(direction, horizontalRadius)
                && PlayerStillInsideStreamingAnchor(direction))
            {
                lastStreamingAnchor = GetSurfaceAnchor(direction);
                CommitReadySurfaceCutoutCenter(horizontalRadius);
            }
        }
        finally
        {
            groundWorkFrameBudgetSeconds =
                GroundStreamingFrameBudgetSeconds;
            bulkLoadingPlanet = false;
            streamingPassRunning = false;
        }
    }

    bool TryGetSurfaceChunkAtDirection(
        Vector3 direction,
        out QuadSphereChunkKey surfaceChunk)
    {
        Vector3 radial = direction.sqrMagnitude > 0.0001f
            ? direction.normalized
            : Vector3.up;
        VoxelQuadSphereMapping.DirectionToFaceCell(
            radial,
            faceGridSize,
            out QuadSphereFace face,
            out int cellU,
            out int cellV);
        // The analytic surface radius is only an LOD estimate. Saved voxel edits,
        // caves and quantized terrain can move the real air/solid boundary into a
        // different radial chunk, so spawn collision must use the actual voxels.
        if (!TryFindFirstSolidDepth(face, cellU, cellV, out int surfaceDepth))
        {
            surfaceChunk = default;
            return false;
        }

        surfaceChunk = new QuadSphereChunkKey(
            face,
            cellU / VoxelTypes.ChunkSize,
            cellV / VoxelTypes.ChunkSize,
            surfaceDepth / VoxelTypes.ChunkSize);
        return true;
    }

    bool EnsureSurfaceChunkReady(Vector3 direction)
    {
        BuildSpawnSurfaceStreamingChunks(direction, 1, initialSurfaceChunks);
        if (initialSurfaceChunks.Count == 0)
            return false;

        pinnedLandingChunks.UnionWith(initialSurfaceChunks);
        initialSurfaceBuildQueue.Clear();
        foreach (QuadSphereChunkKey key in initialSurfaceChunks)
            initialSurfaceBuildQueue.Add(key);

        bool previousBulkLoading = bulkLoadingPlanet;
        bulkLoadingPlanet = true;
        try
        {
            for (int i = 0; i < initialSurfaceBuildQueue.Count; i++)
            {
                QuadSphereChunkKey key = initialSurfaceBuildQueue[i];
                if (!chunks.ContainsKey(key))
                    LoadChunk(key);
            }
        }
        finally
        {
            bulkLoadingPlanet = previousBulkLoading;
        }

        bool hasCollider = false;
        for (int i = 0; i < initialSurfaceBuildQueue.Count; i++)
        {
            QuadSphereChunkKey key = initialSurfaceBuildQueue[i];
            if (!chunks.TryGetValue(key, out VoxelQuadSphereChunk chunk))
                continue;
            if (chunk.IsDirty || !chunk.HasMesh)
            {
                chunk.RebuildMesh(
                    SampleVoxelAt,
                    faceGridSize,
                    maxDepth,
                    voxelOuterRadius,
                    planetCenterLocal);
            }
            hasCollider |= chunk.EnsureColliderReady();
        }
        Physics.SyncTransforms();
        // Empty peripheral chunks are valid. Startup readiness is owned by the
        // baked collider and generated triangles intersected by the requested
        // landing radial, not by a timing-sensitive PhysX query.
        return hasCollider
            && TryFindLoadedSurfacePoseNearLocalDirection(direction, out _);
    }

    bool BuildInitialSurfacePatchSynchronously(Vector3 direction)
    {
        Vector3 radial = direction.sqrMagnitude > 0.0001f
            ? direction.normalized
            : Vector3.up;
        // Build only the immediate 3x3 landing neighborhood synchronously.
        // The previous 5x5 all-chunks gate allowed an empty or late peripheral
        // chunk to suppress a valid high-detail collider under the player.
        // The wider active region is filled by background streaming after the
        // player is safely placed.
        return EnsureSurfaceChunkReady(radial);
    }

    bool IsSpawnSurfaceColliderReady(Vector3 direction)
    {
        if (!streamingLargePlanet)
            return true;
        if (!EnsureSurfaceChunkReady(direction))
            return false;

        return TryFindLoadedSurfacePoseNearLocalDirection(direction, out _);
    }

    void BuildDesiredStreamingChunks(
        Vector3 direction,
        int horizontalRadius,
        HashSet<QuadSphereChunkKey> destination)
    {
        destination.Clear();
        AddDesiredStreamingChunks(direction, horizontalRadius, destination, false, true);
    }

    void BuildSpawnSurfaceStreamingChunks(
        Vector3 direction,
        int horizontalRadius,
        HashSet<QuadSphereChunkKey> destination)
    {
        destination.Clear();
        VoxelQuadSphereMapping.DirectionToFaceCell(
            direction,
            faceGridSize,
            out QuadSphereFace anchorFace,
            out int anchorU,
            out int anchorV);

        int step = VoxelTypes.ChunkSize;
        int radius = Mathf.Max(1, horizontalRadius);
        for (int dv = -radius; dv <= radius; dv++)
        {
            for (int du = -radius; du <= radius; du++)
            {
                AddSurfaceColumnChunks(
                    anchorFace,
                    anchorU + du * step,
                    anchorV + dv * step,
                    destination,
                    true,
                    true);
            }
        }
    }

    bool IsGroundStreamingRegionReady(
        Vector3 direction,
        int horizontalRadius,
        bool spawnSurfaceOnly = false)
    {
        if (!streamingLargePlanet)
            return true;

        if (spawnSurfaceOnly)
        {
            BuildSpawnSurfaceStreamingChunks(
                direction,
                Mathf.Max(1, horizontalRadius),
                readinessChunks);
        }
        else
        {
            BuildDesiredStreamingChunks(
                direction,
                Mathf.Max(1, horizontalRadius),
                readinessChunks);
        }
        foreach (QuadSphereChunkKey key in readinessChunks)
        {
            if (!chunks.TryGetValue(key, out VoxelQuadSphereChunk chunk)
                || !chunk.HasMesh
                || !chunk.IsCollisionReady)
            {
                return false;
            }
        }

        return TryFindLoadedSurfacePoseNearLocalDirection(direction, out _);
    }

    int GetReadySpawnCutoutChunkRadius(Vector3 direction)
    {
        return IsGroundStreamingRegionReady(
            direction,
            InitialSpawnChunkRadius,
            true)
            ? InitialSpawnChunkRadius
            : 1;
    }

    bool PlayerStillInsideStreamingAnchor(Vector3 requestedDirection)
    {
        if (playerSpawn == null)
            return false;

        Vector3 localPlayer = transform.InverseTransformPoint(playerSpawn.position);
        Vector3 currentDirection = localPlayer - planetCenterLocal;
        return currentDirection.sqrMagnitude > 0.001f
            && GetSurfaceAnchor(currentDirection).Equals(
                GetSurfaceAnchor(requestedDirection));
    }

    void CommitReadySurfaceCutoutCenter(int horizontalRadius)
    {
        if (playerSpawn == null)
            return;

        Vector3 localPlayer = transform.InverseTransformPoint(playerSpawn.position);
        Vector3 localDirection = localPlayer - planetCenterLocal;
        if (localDirection.sqrMagnitude < 0.001f
            || !TryFindLoadedSurfacePoseNearLocalDirection(
                localDirection,
                out VoxelTerrainSurfacePose surfacePose))
        {
            return;
        }

        CommitReadySurfaceCutout(surfacePose, horizontalRadius);
    }

    void CommitReadySurfaceCutout(RaycastHit surfaceHit, int horizontalRadius)
    {
        CommitReadySurfaceCutout(
            VoxelTerrainSurfacePose.FromRaycastHit(surfaceHit),
            horizontalRadius);
    }

    void CommitReadySurfaceCutout(
        VoxelTerrainSurfacePose surfacePose,
        int horizontalRadius)
    {
        readySurfaceCutoutCenterWorld = surfacePose.Point;
        float chunkWorldSpan = planetRadius * 2f
            / Mathf.Max(1, faceGridSize)
            * VoxelTypes.ChunkSize;
        // A cutout centered anywhere inside the anchor chunk is only guaranteed
        // to have horizontalRadius full chunk spans before reaching the nearest
        // edge of the prepared square. Keep both a fractional chunk and the
        // shader's maximum dither transition inside that contiguous coverage so
        // the far shell can never reveal an unbuilt rectangular chunk.
        float guaranteedCoveredRadius = chunkWorldSpan
            * Mathf.Max(0.1f, Mathf.Max(1, horizontalRadius) - FarLodChunkEdgeSafety);
        readySurfaceCutoutRadius = Mathf.Max(
            1f,
            guaranteedCoveredRadius - FarLodMaximumTransitionWidth);
        hasReadySurfaceCutoutCenter = true;
    }

    void TryUnloadGroundChunks(
        Vector3 requestedDirection,
        int horizontalRadius,
        HashSet<QuadSphereChunkKey> completedRegion)
    {
        if (playerSpawn == null || completedRegion == null)
            return;

        Vector3 localPlayer = transform.InverseTransformPoint(playerSpawn.position);
        Vector3 currentDirection = localPlayer - planetCenterLocal;
        if (currentDirection.sqrMagnitude < 0.001f)
            return;

        // A streaming pass can span several frames. Never unload from beneath a
        // player who moved to another chunk while that pass was being built.
        if (!GetSurfaceAnchor(currentDirection).Equals(GetSurfaceAnchor(requestedDirection)))
            return;

        foreach (QuadSphereChunkKey key in completedRegion)
        {
            if (!chunks.TryGetValue(key, out VoxelQuadSphereChunk chunk) || !chunk.IsCollisionReady)
                return;
        }

        // Keep a collision hysteresis ring around the active region. This avoids
        // exposing a seam while the next streaming pass is being prepared.
        BuildDesiredStreamingChunks(
            currentDirection,
            horizontalRadius + 2,
            retainedGroundChunks);
        UnloadChunksOutside(retainedGroundChunks);
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
        AddDesiredStreamingChunks(radial, FlightLocalChunkRadius, destination, true, true);
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
                true,
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

        AddDesiredStreamingChunks(radial, FlightLocalChunkRadius + 2, destination, true, true);
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
                true,
                true);
        }
    }

    void AddDesiredStreamingChunks(
        Vector3 direction,
        int horizontalRadius,
        HashSet<QuadSphereChunkKey> destination,
        bool surfaceOnly,
        bool scanGeneratedSurface)
    {
        VoxelQuadSphereMapping.DirectionToFaceCell(
            direction, faceGridSize, out QuadSphereFace anchorFace, out int anchorU, out int anchorV);

        for (int dv = -horizontalRadius; dv <= horizontalRadius; dv++)
        {
            for (int du = -horizontalRadius; du <= horizontalRadius; du++)
            {
                int sampleU = anchorU + du * VoxelTypes.ChunkSize;
                int sampleV = anchorV + dv * VoxelTypes.ChunkSize;
                AddSurfaceColumnChunks(
                    anchorFace,
                    sampleU,
                    sampleV,
                    destination,
                    surfaceOnly,
                    scanGeneratedSurface);
            }
        }
    }

    void AddSurfaceColumnChunks(
        QuadSphereFace sourceFace,
        int sampleU,
        int sampleV,
        HashSet<QuadSphereChunkKey> destination,
        bool surfaceOnly,
        bool scanGeneratedSurface = false)
    {
        QuadSphereVoxelAddress remapped = VoxelQuadSphereMapping.RemapAcrossFace(
            new QuadSphereVoxelAddress(sourceFace, sampleU, sampleV, 0),
            faceGridSize);
        int chunkU = remapped.U / VoxelTypes.ChunkSize;
        int chunkV = remapped.V / VoxelTypes.ChunkSize;
        float minimumSurfaceDepth;
        float maximumSurfaceDepth;
        if (!scanGeneratedSurface
            || !TryGetGeneratedSurfaceDepthRange(
                remapped.Face,
                chunkU,
                chunkV,
                remapped.U,
                remapped.V,
                out minimumSurfaceDepth,
                out maximumSurfaceDepth))
        {
            GetSurfaceDepthRange(
                remapped.Face,
                chunkU,
                chunkV,
                remapped.U,
                remapped.V,
                out minimumSurfaceDepth,
                out maximumSurfaceDepth);
        }

        int chunkDepthCount = Mathf.CeilToInt(maxDepth / (float)VoxelTypes.ChunkSize);
        int shallowSurfaceChunk = Mathf.Clamp(
            Mathf.FloorToInt(minimumSurfaceDepth / VoxelTypes.ChunkSize),
            0,
            chunkDepthCount - 1);
        int deepSurfaceChunk = Mathf.Clamp(
            Mathf.FloorToInt(maximumSurfaceDepth / VoxelTypes.ChunkSize),
            0,
            chunkDepthCount - 1);
        int belowSurfaceChunks = Mathf.CeilToInt(
            celestialProfile.editableDepth / VoxelTypes.ChunkSize) + 1;
        int firstDepth = Mathf.Max(0, shallowSurfaceChunk - 1);
        int lastDepth = Mathf.Min(
            chunkDepthCount - 1,
            deepSurfaceChunk + (surfaceOnly ? 1 : belowSurfaceChunks));
        for (int chunkDepth = firstDepth; chunkDepth <= lastDepth; chunkDepth++)
        {
            destination.Add(new QuadSphereChunkKey(
                remapped.Face,
                chunkU,
                chunkV,
                chunkDepth));
        }
    }

    bool TryGetGeneratedSurfaceDepthRange(
        QuadSphereFace face,
        int chunkU,
        int chunkV,
        int requestedU,
        int requestedV,
        out float minimumDepth,
        out float maximumDepth)
    {
        QuadSphereChunkKey cacheKey = new QuadSphereChunkKey(face, chunkU, chunkV, 0);
        int startU = chunkU * VoxelTypes.ChunkSize;
        int startV = chunkV * VoxelTypes.ChunkSize;
        int endU = Mathf.Min(faceGridSize - 1, startU + VoxelTypes.ChunkSize - 1);
        int endV = Mathf.Min(faceGridSize - 1, startV + VoxelTypes.ChunkSize - 1);
        int middleU = (startU + endU) / 2;
        int middleV = (startV + endV) / 2;
        bool foundSurface = generatedSurfaceDepthRangeCache.TryGetValue(
            cacheKey,
            out Vector2Int cachedRange);
        minimumDepth = foundSurface ? cachedRange.x : float.PositiveInfinity;
        maximumDepth = foundSurface ? cachedRange.y : float.NegativeInfinity;

        if (!foundSurface)
        {
            for (int v = 0; v < 3; v++)
            {
                int cellV = v == 0 ? startV : v == 1 ? middleV : endV;
                for (int u = 0; u < 3; u++)
                {
                    int cellU = u == 0 ? startU : u == 1 ? middleU : endU;
                    if (!TryFindFirstSolidDepth(face, cellU, cellV, out int depth))
                        continue;
                    minimumDepth = Mathf.Min(minimumDepth, depth);
                    maximumDepth = Mathf.Max(maximumDepth, depth);
                    foundSurface = true;
                }
            }

            if (foundSurface)
            {
                generatedSurfaceDepthRangeCache[cacheKey] = new Vector2Int(
                    Mathf.FloorToInt(minimumDepth),
                    Mathf.CeilToInt(maximumDepth));
            }
        }

        int clampedU = Mathf.Clamp(requestedU, startU, endU);
        int clampedV = Mathf.Clamp(requestedV, startV, endV);
        if (TryFindFirstSolidDepth(face, clampedU, clampedV, out int requestedDepth))
        {
            minimumDepth = Mathf.Min(minimumDepth, requestedDepth);
            maximumDepth = Mathf.Max(maximumDepth, requestedDepth);
            foundSurface = true;
        }

        return foundSurface;
    }

    bool TryFindFirstSolidDepth(
        QuadSphereFace face,
        int cellU,
        int cellV,
        out int surfaceDepth)
    {
        for (int depth = 0; depth < maxDepth; depth++)
        {
            byte voxel = SampleVoxelAt(new QuadSphereVoxelAddress(
                face,
                cellU,
                cellV,
                depth));
            if (!VoxelTypes.IsSolid(voxel))
                continue;

            surfaceDepth = depth;
            return true;
        }

        surfaceDepth = -1;
        return false;
    }

    void GetSurfaceDepthRange(
        QuadSphereFace face,
        int chunkU,
        int chunkV,
        int requestedU,
        int requestedV,
        out float minimumDepth,
        out float maximumDepth)
    {
        int startU = chunkU * VoxelTypes.ChunkSize;
        int startV = chunkV * VoxelTypes.ChunkSize;
        int endU = Mathf.Min(faceGridSize - 1, startU + VoxelTypes.ChunkSize - 1);
        int endV = Mathf.Min(faceGridSize - 1, startV + VoxelTypes.ChunkSize - 1);
        int middleU = (startU + endU) / 2;
        int middleV = (startV + endV) / 2;
        minimumDepth = float.PositiveInfinity;
        maximumDepth = float.NegativeInfinity;
        for (int v = 0; v < 3; v++)
        {
            int sampleV = v == 0 ? startV : v == 1 ? middleV : endV;
            for (int u = 0; u < 3; u++)
            {
                int sampleU = u == 0 ? startU : u == 1 ? middleU : endU;
                Vector3 radial = VoxelQuadSphereMapping.GetRadialDirection(
                    face,
                    sampleU,
                    sampleV,
                    faceGridSize);
                float depth = voxelOuterRadius - GetProceduralSurfaceRadius(radial);
                minimumDepth = Mathf.Min(minimumDepth, depth);
                maximumDepth = Mathf.Max(maximumDepth, depth);
            }
        }

        Vector3 requestedRadial = VoxelQuadSphereMapping.GetRadialDirection(
            face,
            Mathf.Clamp(requestedU, startU, endU),
            Mathf.Clamp(requestedV, startV, endV),
            faceGridSize);
        float requestedDepth = voxelOuterRadius - GetProceduralSurfaceRadius(requestedRadial);
        minimumDepth = Mathf.Min(minimumDepth, requestedDepth);
        maximumDepth = Mathf.Max(maximumDepth, requestedDepth);
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
                true,
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
        if (TryGetSurfaceChunkAtDirection(direction, out QuadSphereChunkKey generatedSurfaceChunk))
            return generatedSurfaceChunk;

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
            if (!keep.Contains(pair.Key)
                && !pinnedLandingChunks.Contains(pair.Key)
                && !flightScheduledChunks.Contains(pair.Key))
            {
                remove.Add(pair.Key);
            }
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

                        ReportLegacyLoadingProgress(
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

                ReportLegacyLoadingProgress(
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

                ReportLegacyLoadingProgress(
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
        Vector3 center = GetPlanetCenterWorld();
        float worldScale = Mathf.Max(
            Mathf.Abs(transform.lossyScale.x),
            Mathf.Abs(transform.lossyScale.y),
            Mathf.Abs(transform.lossyScale.z));
        float outerDistance = (voxelOuterRadius + 10f) * worldScale;
        float castDistance = (maxDepth + 20f) * worldScale;
        Vector3 rayDirection = worldDirection.normalized;
        Vector3 origin = center + rayDirection * outerDistance;
        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            -rayDirection,
            castDistance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        if (TrySelectClosestTerrainHit(hits, out closestHit))
            return true;
        if (TryRaycastTerrainCollidersDirectly(
            origin,
            -rayDirection,
            castDistance,
            false,
            out closestHit))
        {
            return true;
        }

        // Some generated cube faces can have the opposite winding at a seam.
        // Retry only on a miss so normal surface queries retain their fast path.
        bool previousBackfaceSetting = Physics.queriesHitBackfaces;
        try
        {
            Physics.queriesHitBackfaces = true;
            hits = Physics.RaycastAll(
                origin,
                -rayDirection,
                castDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
        }
        finally
        {
            Physics.queriesHitBackfaces = previousBackfaceSetting;
        }

        if (TrySelectClosestTerrainHit(hits, out closestHit))
            return true;

        return TryRaycastTerrainCollidersDirectly(
            origin,
            -rayDirection,
            castDistance,
            true,
            out closestHit);
    }

    bool TrySelectClosestTerrainHit(
        RaycastHit[] hits,
        out RaycastHit closestHit)
    {
        closestHit = default;
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

    bool TryRaycastTerrainCollidersDirectly(
        Vector3 origin,
        Vector3 direction,
        float distance,
        bool hitBackfaces,
        out RaycastHit closestHit)
    {
        closestHit = default;
        terrainColliderProbeBuffer.Clear();
        foreach (Collider terrainCollider in terrainColliders)
        {
            if (terrainCollider != null)
                terrainColliderProbeBuffer.Add(terrainCollider);
        }

        float closestDistance = float.PositiveInfinity;
        Ray ray = new Ray(origin, direction);
        bool previousBackfaceSetting = Physics.queriesHitBackfaces;
        try
        {
            Physics.queriesHitBackfaces = hitBackfaces;
            for (int i = 0; i < terrainColliderProbeBuffer.Count; i++)
            {
                Collider terrainCollider = terrainColliderProbeBuffer[i];
                if (!terrainCollider.enabled
                    || !terrainCollider.gameObject.activeInHierarchy
                    || !terrainCollider.Raycast(ray, out RaycastHit hit, distance)
                    || hit.distance >= closestDistance)
                {
                    continue;
                }

                closestDistance = hit.distance;
                closestHit = hit;
            }
        }
        finally
        {
            Physics.queriesHitBackfaces = previousBackfaceSetting;
            terrainColliderProbeBuffer.Clear();
        }

        return closestDistance < float.PositiveInfinity;
    }

    bool TryFindLoadedSurfaceNearLocalDirection(
        Vector3 localDirection,
        out RaycastHit closestHit)
    {
        Vector3 direction = localDirection.sqrMagnitude > 0.0001f
            ? localDirection.normalized
            : Vector3.up;
        Vector3 worldDirection = transform.TransformDirection(direction).normalized;
        if (TryFindPlanetSurface(worldDirection, out closestHit))
            return true;

        Vector3 tangentX = Vector3.Cross(
            direction,
            Mathf.Abs(Vector3.Dot(direction, Vector3.up)) < 0.95f
                ? Vector3.up
                : Vector3.right).normalized;
        Vector3 tangentY = Vector3.Cross(direction, tangentX).normalized;
        float bestAngularError = float.PositiveInfinity;
        bool found = false;

        for (int radiusIndex = 0; radiusIndex < InitialSurfaceProbeAngles.Length; radiusIndex++)
        {
            float angleRadians = InitialSurfaceProbeAngles[radiusIndex] * Mathf.Deg2Rad;
            float radialWeight = Mathf.Cos(angleRadians);
            float tangentWeight = Mathf.Sin(angleRadians);
            for (int sampleIndex = 0; sampleIndex < 8; sampleIndex++)
            {
                float azimuth = sampleIndex * (Mathf.PI * 0.25f);
                Vector3 tangent = tangentX * Mathf.Cos(azimuth)
                    + tangentY * Mathf.Sin(azimuth);
                Vector3 candidateDirection = (
                    direction * radialWeight
                    + tangent * tangentWeight).normalized;
                Vector3 candidateWorldDirection =
                    transform.TransformDirection(candidateDirection).normalized;
                if (!TryFindPlanetSurface(candidateWorldDirection, out RaycastHit hit))
                    continue;

                float angularError = Vector3.Angle(direction, candidateDirection);
                if (angularError >= bestAngularError)
                    continue;

                bestAngularError = angularError;
                closestHit = hit;
                found = true;
            }

            if (found)
                return true;
        }

        closestHit = default;
        return false;
    }

    bool TryFindLoadedSurfacePoseNearLocalDirection(
        Vector3 localDirection,
        out VoxelTerrainSurfacePose pose)
    {
        Vector3 direction = localDirection.sqrMagnitude > 0.0001f
            ? localDirection.normalized
            : Vector3.up;
        if (TryFindLoadedSurfaceNearLocalDirection(direction, out RaycastHit physicsHit))
        {
            pose = VoxelTerrainSurfacePose.FromRaycastHit(physicsHit);
            return true;
        }

        if (TryFindGeneratedSurfacePose(direction, out pose))
            return true;

        Vector3 tangentX = Vector3.Cross(
            direction,
            Mathf.Abs(Vector3.Dot(direction, Vector3.up)) < 0.95f
                ? Vector3.up
                : Vector3.right).normalized;
        Vector3 tangentY = Vector3.Cross(direction, tangentX).normalized;
        for (int radiusIndex = 0; radiusIndex < InitialSurfaceProbeAngles.Length; radiusIndex++)
        {
            float angleRadians = InitialSurfaceProbeAngles[radiusIndex] * Mathf.Deg2Rad;
            float radialWeight = Mathf.Cos(angleRadians);
            float tangentWeight = Mathf.Sin(angleRadians);
            for (int sampleIndex = 0; sampleIndex < 8; sampleIndex++)
            {
                float azimuth = sampleIndex * (Mathf.PI * 0.25f);
                Vector3 tangent = tangentX * Mathf.Cos(azimuth)
                    + tangentY * Mathf.Sin(azimuth);
                Vector3 candidateDirection = (
                    direction * radialWeight
                    + tangent * tangentWeight).normalized;
                if (TryFindGeneratedSurfacePose(candidateDirection, out pose))
                    return true;
            }
        }

        pose = default;
        return false;
    }

    bool TryFindGeneratedSurfacePose(
        Vector3 localDirection,
        out VoxelTerrainSurfacePose pose)
    {
        pose = default;
        Vector3 direction = localDirection.sqrMagnitude > 0.0001f
            ? localDirection.normalized
            : Vector3.up;
        Vector3 worldDirection = transform.TransformDirection(direction).normalized;
        Vector3 center = GetPlanetCenterWorld();
        float worldScale = Mathf.Max(
            Mathf.Abs(transform.lossyScale.x),
            Mathf.Abs(transform.lossyScale.y),
            Mathf.Abs(transform.lossyScale.z));
        float outerDistance = (voxelOuterRadius + 10f) * worldScale;
        float castDistance = (maxDepth + 20f) * worldScale;
        Vector3 origin = center + worldDirection * outerDistance;
        Ray ray = new Ray(origin, -worldDirection);
        float nearestDistance = float.PositiveInfinity;

        foreach (VoxelQuadSphereChunk chunk in chunks.Values)
        {
            Collider terrainCollider = chunk.Collider;
            if (!chunk.HasBakedCollider
                || terrainCollider == null
                || !terrainColliders.Contains(terrainCollider)
                || !terrainCollider.bounds.IntersectRay(ray, out float boundsDistance)
                || boundsDistance > castDistance
                || !chunk.TryRaycastSurface(
                    ray,
                    castDistance,
                    out Vector3 point,
                    out Vector3 normal,
                    out float distance)
                || distance >= nearestDistance)
            {
                continue;
            }

            Vector3 outward = point - center;
            if (outward.sqrMagnitude > 0.0001f && Vector3.Dot(normal, outward) < 0f)
                normal = -normal;
            nearestDistance = distance;
            pose = new VoxelTerrainSurfacePose(point, normal, terrainCollider);
        }

        return pose.IsValid;
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

        // Shape is a function of direction, so all radial voxels in one U/V
        // column share the same expensive layered-noise result. Caching these
        // 256 values avoids recalculating the planet shape for all 4096 voxels.
        for (int y = 0; y < VoxelTypes.ChunkSize; y++)
        {
            for (int x = 0; x < VoxelTypes.ChunkSize; x++)
            {
                int cellU = originU + x;
                int cellV = originV + y;
                int column = x + y * VoxelTypes.ChunkSize;
                Vector3 radial = VoxelQuadSphereMapping.GetRadialDirection(
                    key.Face,
                    cellU,
                    cellV,
                    faceGridSize);
                chunkSurfaceNoiseCache[column] = VoxelQuadSphereTerrain.GetSurfaceNoise(
                    radial * planetRadius,
                    seed,
                    terrainSettings);
                chunkRiverCarveCache[column] = riverSystem != null
                    ? riverSystem.GetCarveDepth(key.Face, cellU, cellV)
                    : 0f;
            }
        }

        for (int z = 0; z < VoxelTypes.ChunkSize; z++)
        {
            for (int y = 0; y < VoxelTypes.ChunkSize; y++)
            {
                for (int x = 0; x < VoxelTypes.ChunkSize; x++)
                {
                    int cellU = originU + x;
                    int cellV = originV + y;
                    int depth = originDepth + z;
                    int column = x + y * VoxelTypes.ChunkSize;
                    byte voxel = VoxelQuadSphereTerrain.GenerateVoxelWithSurfaceNoise(
                        key.Face, cellU, cellV, depth,
                        faceGridSize, maxDepth, innerSolidDepthLayers, seed, planetCenterLocal,
                        voxelOuterRadius, planetRadius, terrainSettings,
                        chunkSurfaceNoiseCache[column],
                        chunkRiverCarveCache[column]);
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

        if (savedVoxelCacheMatchesCurrentGrid
            && modifiedChunkCache.TryGetValue(key, out byte[] saved)
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
        generatedSurfaceDepthRangeCache.Clear();
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

    bool PlacePlayerAtSpawn(bool logFailure = true)
    {
        if (!autoPlacePlayerOnStart || playerSpawn == null)
            return false;

        Vector3 preferredDirection = spawnDirectionLocal.sqrMagnitude < 0.001f
            ? Vector3.up
            : spawnDirectionLocal.normalized;
        // Streaming worlds only have collision around the requested entry direction.
        // Searching farther away can select a procedurally solid but unloaded patch.
        Vector3 direction = streamingLargePlanet
            ? preferredDirection
            : FindSafeSpawnDirection(preferredDirection);
        if (streamingLargePlanet)
        {
            EnsureSurfaceChunkReady(direction);
            Physics.SyncTransforms();
        }
        Vector3 worldDirection = transform.TransformDirection(direction).normalized;
        VoxelTerrainSurfacePose surfacePose = default;
        bool hasSurfaceCollider;
        if (streamingLargePlanet)
        {
            hasSurfaceCollider = TryFindLoadedSurfacePoseNearLocalDirection(
                direction,
                out surfacePose);
        }
        else
        {
            hasSurfaceCollider = TryFindPlanetSurface(
                worldDirection,
                out RaycastHit surfaceHit);
            if (hasSurfaceCollider)
                surfacePose = VoxelTerrainSurfacePose.FromRaycastHit(surfaceHit);
        }

        Vector3 worldUp;
        Vector3 worldPosition;
        if (hasSurfaceCollider)
        {
            worldUp = surfacePose.Normal.sqrMagnitude > 0.0001f
                ? surfacePose.Normal.normalized
                : worldDirection;
            worldPosition = surfacePose.Point + worldUp * spawnHeightOffset;
        }
        else
        {
            float surfaceRadius = GetProceduralSurfaceRadius(direction);
            Vector3 localSpawn = planetCenterLocal + direction * (surfaceRadius + spawnHeightOffset);
            worldPosition = transform.TransformPoint(localSpawn);
            worldUp = worldDirection;
            if (logFailure)
            {
                int meshChunkCount = 0;
                int colliderChunkCount = 0;
                int meshVertexCount = 0;
                foreach (VoxelQuadSphereChunk chunk in chunks.Values)
                {
                    if (chunk.MeshVertexCount <= 0)
                        continue;
                    meshChunkCount++;
                    meshVertexCount += chunk.MeshVertexCount;
                    if (chunk.HasBakedCollider)
                        colliderChunkCount++;
                }
                Debug.LogError(
                    "VoxelQuadSphereWorld: the initial spawn region has no ready terrain collider; " +
                    "the player will remain locked instead of falling through the planet. " +
                    $"LoadedChunks={chunks.Count}, MeshChunks={meshChunkCount}, " +
                    $"ColliderChunks={colliderChunkCount}, Vertices={meshVertexCount}, " +
                    $"SpawnDirection={direction}.",
                    this);
            }
        }

        Vector3 worldForward = Vector3.ProjectOnPlane(transform.forward, worldUp).normalized;
        if (worldForward.sqrMagnitude < 0.0001f)
        {
            Vector3 reference = Mathf.Abs(Vector3.Dot(worldUp, Vector3.right)) < 0.9f
                ? Vector3.right
                : Vector3.forward;
            worldForward = Vector3.Cross(worldUp, reference).normalized;
        }
        Quaternion worldRotation = Quaternion.LookRotation(worldForward, worldUp);
        VoxelPlanetPlayerController playerController = playerSpawn.GetComponent<VoxelPlanetPlayerController>();
        if (playerController != null)
        {
            playerController.TeleportTo(worldPosition, worldRotation);
            playerController.SetSurfacePhysicsReady(
                hasSurfaceCollider
                && GetComponent<PlanetSurfaceEntryCoordinator>() == null);
            return hasSurfaceCollider;
        }

        Rigidbody playerBody = playerSpawn.GetComponent<Rigidbody>();

        if (playerBody != null)
        {
            playerBody.position = worldPosition;
            playerBody.rotation = worldRotation;
            if (!playerBody.isKinematic)
            {
                playerBody.velocity = Vector3.zero;
                playerBody.angularVelocity = Vector3.zero;
            }
            return hasSurfaceCollider;
        }

        playerSpawn.SetPositionAndRotation(worldPosition, worldRotation);
        return hasSurfaceCollider;
    }

    bool PlacePlayerAtSurface(RaycastHit surfaceHit)
    {
        return PlacePlayerAtSurface(
            VoxelTerrainSurfacePose.FromRaycastHit(surfaceHit));
    }

    bool PlacePlayerAtSurface(VoxelTerrainSurfacePose surfacePose)
    {
        if (!autoPlacePlayerOnStart
            || playerSpawn == null
            || !surfacePose.IsValid
            || !IsTerrainCollider(surfacePose.Collider))
        {
            return false;
        }

        Vector3 worldDirection = surfacePose.Point - GetPlanetCenterWorld();
        if (worldDirection.sqrMagnitude < 0.0001f)
            worldDirection = transform.TransformDirection(spawnDirectionLocal).normalized;
        else
            worldDirection.Normalize();

        Vector3 worldUp = surfacePose.Normal.sqrMagnitude > 0.0001f
            ? surfacePose.Normal.normalized
            : worldDirection;
        Vector3 worldForward = Vector3.ProjectOnPlane(transform.forward, worldUp).normalized;
        if (worldForward.sqrMagnitude < 0.0001f)
        {
            Vector3 reference = Mathf.Abs(Vector3.Dot(worldUp, Vector3.right)) < 0.9f
                ? Vector3.right
                : Vector3.forward;
            worldForward = Vector3.Cross(worldUp, reference).normalized;
        }

        Vector3 worldPosition = surfacePose.Point + worldUp * spawnHeightOffset;
        Quaternion worldRotation = Quaternion.LookRotation(worldForward, worldUp);
        VoxelPlanetPlayerController playerController =
            playerSpawn.GetComponent<VoxelPlanetPlayerController>();
        if (playerController != null)
        {
            playerController.TeleportTo(worldPosition, worldRotation);
            playerController.SetSurfacePhysicsReady(
                GetComponent<PlanetSurfaceEntryCoordinator>() == null);
            return true;
        }

        Rigidbody playerBody = playerSpawn.GetComponent<Rigidbody>();
        if (playerBody != null)
        {
            playerBody.position = worldPosition;
            playerBody.rotation = worldRotation;
            if (!playerBody.isKinematic)
            {
                playerBody.velocity = Vector3.zero;
                playerBody.angularVelocity = Vector3.zero;
            }
            return true;
        }

        playerSpawn.SetPositionAndRotation(worldPosition, worldRotation);
        return true;
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
