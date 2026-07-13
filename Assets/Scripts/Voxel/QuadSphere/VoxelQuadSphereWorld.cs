using System.Collections.Generic;
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
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(115);
        count = Mathf.Max(0, count);
        surfaceOffset = Mathf.Max(0f, surfaceOffset);
        minimumSpacing = Mathf.Max(0f, minimumSpacing);
        playerClearRadius = Mathf.Max(0f, playerClearRadius);
        placementAttempts = Mathf.Max(1, placementAttempts);
    }
}

public class VoxelQuadSphereWorld : MonoBehaviour
{
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

    [Header("Surface Resources")]
    [SerializeField] bool spawnHarvestableResources = true;
    [SerializeField] List<HarvestableResourceSpawnSettings> resourceSpawnSettings = new List<HarvestableResourceSpawnSettings>();

    readonly Dictionary<QuadSphereChunkKey, VoxelQuadSphereChunk> chunks = new Dictionary<QuadSphereChunkKey, VoxelQuadSphereChunk>();
    readonly HashSet<Collider> terrainColliders = new HashSet<Collider>();
    readonly List<ResourcePlacement> resourcePlacements = new List<ResourcePlacement>();
    Transform generatedResourcesRoot;

    public int Seed => seed;
    public bool UsePlanetGeneration => true;
    public float PlanetRadius => planetRadius;
    public float SurfaceGravity => surfaceGravity;
    public float GravitationalParameter => PlanetGravity.ComputeGravitationalParameter(surfaceGravity, planetRadius);
    public int FaceGridSize => faceGridSize;
    public int MaxDepth => maxDepth;
    public int InnerSolidDepthLayers => innerSolidDepthLayers;

    public Vector3 GetPlanetCenterWorld()
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(108);
        return transform.TransformPoint(planetCenterLocal);
    }

    public Vector3 GetPlanetCenterLocal()
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(109);
        return planetCenterLocal;
    }

    void Start()
    {
        GenerateEntirePlanet();
        PlacePlayerAtSpawn();
        SpawnHarvestableResources();
    }

    void OnValidate()
    {
        if (resourceSpawnSettings == null)
            return;

        foreach (HarvestableResourceSpawnSettings settings in resourceSpawnSettings)
            settings?.ClampValues();
    }

    void LateUpdate()
    {
        foreach (VoxelQuadSphereChunk chunk in chunks.Values)
        {
            if (chunk.IsDirty)
                chunk.RebuildMesh(SampleVoxelAt, faceGridSize, maxDepth, planetRadius, planetCenterLocal);
        }
    }

    public void GenerateEntirePlanet()
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(110);
        int chunkCountU = Mathf.CeilToInt(faceGridSize / (float)VoxelTypes.ChunkSize);
        int chunkCountV = Mathf.CeilToInt(faceGridSize / (float)VoxelTypes.ChunkSize);
        int chunkCountDepth = Mathf.CeilToInt(maxDepth / (float)VoxelTypes.ChunkSize);

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

        Debug.Log($"Quad Sphere 全量生成完成：6 扇区 × {chunkCountU}×{chunkCountV}×{chunkCountDepth} = {6 * chunkCountU * chunkCountV * chunkCountDepth} 个 Chunk");
        RebuildAllChunkMeshes();
    }

    void RebuildAllChunkMeshes()
    {
        foreach (VoxelQuadSphereChunk chunk in chunks.Values)
            chunk.RebuildMesh(SampleVoxelAt, faceGridSize, maxDepth, planetRadius, planetCenterLocal);
    }

    void LoadChunk(QuadSphereChunkKey key)
    {
        if (chunks.ContainsKey(key))
            return;

        VoxelQuadSphereChunk chunk = new VoxelQuadSphereChunk(key, transform, dirtMaterial, stoneMaterial);
        GenerateChunkData(chunk);
        chunks.Add(key, chunk);
        terrainColliders.Add(chunk.Collider);
        chunk.RebuildMesh(SampleVoxelAt, faceGridSize, maxDepth, planetRadius, planetCenterLocal);
        MarkLoadedNeighborsDirty(key);
    }

    public void SpawnHarvestableResources()
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(114);
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
        Physics.SyncTransforms();

        foreach (HarvestableResourceSpawnSettings settings in resourceSpawnSettings)
        {
            if (settings == null || settings.prefab == null || settings.count <= 0)
                continue;

            settings.ClampValues();
            System.Random random = new System.Random(seed + settings.seedOffset);
            int spawnedCount = 0;
            int maximumAttempts = settings.count * settings.placementAttempts;
            for (int attempt = 0; attempt < maximumAttempts && spawnedCount < settings.count; attempt++)
            {
                Vector3 direction = GetRandomSphereDirection(random);
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

                Instantiate(settings.prefab, spawnPosition, rotation, generatedResourcesRoot);
                resourcePlacements.Add(new ResourcePlacement(spawnPosition, settings.minimumSpacing));
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

    void ClearGeneratedResources()
    {
        resourcePlacements.Clear();
        if (generatedResourcesRoot == null)
            return;

        Destroy(generatedResourcesRoot.gameObject);
        generatedResourcesRoot = null;
    }

    bool TryFindPlanetSurface(Vector3 worldDirection, out RaycastHit closestHit)
    {
        closestHit = default;
        Vector3 center = GetPlanetCenterWorld();
        float worldScale = Mathf.Max(
            Mathf.Abs(transform.lossyScale.x),
            Mathf.Abs(transform.lossyScale.y),
            Mathf.Abs(transform.lossyScale.z));
        float outerDistance = (planetRadius + 10f) * worldScale;
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

    void GenerateChunkData(VoxelQuadSphereChunk chunk)
    {
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
                        faceGridSize, maxDepth, innerSolidDepthLayers, seed, planetCenterLocal, planetRadius);
                    chunk.Voxels[VoxelTypes.ToIndex(x, y, z)] = voxel;
                }
            }
        }

        chunk.ClearModifiedFlag();
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

        return VoxelQuadSphereTerrain.GenerateVoxel(
            address.Face, address.U, address.V, address.Depth,
            faceGridSize, maxDepth, innerSolidDepthLayers, seed, planetCenterLocal, planetRadius);
    }

    public bool DigVoxel(QuadSphereVoxelAddress address)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(111);
        return SetVoxel(address, VoxelTypes.Air);
    }

    public bool SetVoxel(QuadSphereVoxelAddress address, byte value)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(112, (int)value);
        if (address.U < 0 || address.V < 0 || address.Depth < 0
            || address.U >= faceGridSize || address.V >= faceGridSize || address.Depth >= maxDepth)
            return false;

        QuadSphereChunkKey key = AddressToChunkKey(address);
        if (!chunks.TryGetValue(key, out VoxelQuadSphereChunk chunk))
            return false;

        Vector3Int local = AddressToLocalCoord(address);
        chunk.SetLocalVoxel(local.x, local.y, local.z, value);
        MarkChunkAndNeighborsDirty(key, local);
        MarkCrossFaceNeighborsDirty(address);
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
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(113);
        if (!VoxelQuadSphereMapping.TryLocalPointToVoxel(
                localPoint, planetCenterLocal, planetRadius, faceGridSize, maxDepth, out QuadSphereVoxelAddress address))
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
        Vector3 localSpawn = planetCenterLocal + direction * (planetRadius + spawnHeightOffset);
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
        Vector3 surfacePoint = planetCenterLocal + direction.normalized * (planetRadius - 0.5f);
        if (!VoxelQuadSphereMapping.TryLocalPointToVoxel(
                surfacePoint, planetCenterLocal, planetRadius,
                faceGridSize, maxDepth, out QuadSphereVoxelAddress center))
            return false;

        center.Depth = 0;
        if (!VoxelTypes.IsSolid(SampleVoxelAt(center)))
            return false;

        QuadSphereVoxelAddress[] neighbors =
        {
            new QuadSphereVoxelAddress(center.Face, center.U + 1, center.V, 0),
            new QuadSphereVoxelAddress(center.Face, center.U - 1, center.V, 0),
            new QuadSphereVoxelAddress(center.Face, center.U, center.V + 1, 0),
            new QuadSphereVoxelAddress(center.Face, center.U, center.V - 1, 0)
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
