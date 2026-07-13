using System;
using System.Collections.Generic;
using UnityEngine;

public class VoxelWorld : MonoBehaviour
{
    [Header("世界设置")]
    [SerializeField] int seed = 12345;
    [SerializeField] bool loadSaveOnStart = true;

    [Header("星球")]
    [SerializeField] bool usePlanetGeneration = true;
    [SerializeField] Vector3 planetCenterLocal = Vector3.zero;
    [SerializeField] float planetRadius = 100f;
    [SerializeField] float surfaceGravity = 9.8f;
    [SerializeField] Vector3 spawnDirectionLocal = Vector3.up;
    [SerializeField] float spawnHeightOffset = 2f;

    [Header("流式加载")]
    [SerializeField] int loadRadius = 2;
    [Tooltip("1 = 3x3x3 个 Chunk")]
    [SerializeField] bool streamVerticalChunks = true;

    [Header("材质")]
    [SerializeField] Material dirtMaterial;
    [SerializeField] Material stoneMaterial;

    [Header("玩家出生")]
    [SerializeField] Transform playerSpawn;
    [SerializeField] bool autoPlacePlayerOnStart = true;

    readonly Dictionary<Vector3Int, VoxelChunk> chunks = new Dictionary<Vector3Int, VoxelChunk>();
    readonly Dictionary<Vector3Int, byte[]> modifiedChunkCache = new Dictionary<Vector3Int, byte[]>();

    VoxelSaveSystem saveSystem;

    public int Seed => seed;
    public bool UsePlanetGeneration => usePlanetGeneration;
    public float PlanetRadius => planetRadius;
    public float SurfaceGravity => surfaceGravity;
    public float GravitationalParameter => PlanetGravity.ComputeGravitationalParameter(surfaceGravity, planetRadius);

    public Vector3 GetPlanetCenterWorld()
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(77);
        return transform.TransformPoint(planetCenterLocal);
    }

    public Vector3 GetPlanetCenterLocal()
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(78);
        return planetCenterLocal;
    }

    void Awake()
    {
        saveSystem = GetComponent<VoxelSaveSystem>();
    }

    void Start()
    {
        bool loadedSave = false;
        if (loadSaveOnStart && saveSystem != null && saveSystem.HasSaveFile())
        {
            saveSystem.Load();
            loadedSave = true;
        }
        else
        {
            modifiedChunkCache.Clear();
        }

        if (usePlanetGeneration && !loadedSave)
            GenerateEntirePlanet();

        PlacePlayerAtSpawn();
    }

    void LateUpdate()
    {
        RebuildDirtyChunks();
    }

    public void GenerateEntirePlanet()
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(79);
        if (!usePlanetGeneration)
            return;

        int bound = Mathf.CeilToInt(planetRadius);
        int minX = Mathf.FloorToInt(planetCenterLocal.x - bound);
        int minY = Mathf.FloorToInt(planetCenterLocal.y - bound);
        int minZ = Mathf.FloorToInt(planetCenterLocal.z - bound);
        int maxX = Mathf.FloorToInt(planetCenterLocal.x + bound);
        int maxY = Mathf.FloorToInt(planetCenterLocal.y + bound);
        int maxZ = Mathf.FloorToInt(planetCenterLocal.z + bound);

        Vector3Int minChunk = VoxelTypes.WorldToChunkCoord(minX, minY, minZ);
        Vector3Int maxChunk = VoxelTypes.WorldToChunkCoord(maxX, maxY, maxZ);

        for (int cy = minChunk.y; cy <= maxChunk.y; cy++)
        {
            for (int cz = minChunk.z; cz <= maxChunk.z; cz++)
            {
                for (int cx = minChunk.x; cx <= maxChunk.x; cx++)
                    LoadChunk(new Vector3Int(cx, cy, cz));
            }
        }

        Debug.Log($"星球全量生成完成：{(maxChunk.x - minChunk.x + 1) * (maxChunk.y - minChunk.y + 1) * (maxChunk.z - minChunk.z + 1)} 个 Chunk");
    }

    public void UpdateStreaming(Vector3 worldPosition)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(80);
        if (usePlanetGeneration)
            return;

        Vector3 localPosition = transform.InverseTransformPoint(worldPosition);
        Vector3Int centerChunk = VoxelTypes.WorldToChunkCoord(
            Mathf.FloorToInt(localPosition.x),
            Mathf.FloorToInt(localPosition.y),
            Mathf.FloorToInt(localPosition.z)
        );

        HashSet<Vector3Int> desiredChunks = BuildDesiredChunks(centerChunk);

        List<Vector3Int> unloadList = new List<Vector3Int>();
        foreach (Vector3Int coord in chunks.Keys)
        {
            if (!desiredChunks.Contains(coord))
                unloadList.Add(coord);
        }

        foreach (Vector3Int coord in unloadList)
            UnloadChunk(coord);

        foreach (Vector3Int coord in desiredChunks)
            LoadChunk(coord);
    }

    HashSet<Vector3Int> BuildDesiredChunks(Vector3Int centerChunk)
    {
        HashSet<Vector3Int> desired = new HashSet<Vector3Int>();
        int verticalRadius = streamVerticalChunks ? loadRadius : 0;

        for (int y = -verticalRadius; y <= verticalRadius; y++)
        {
            for (int z = -loadRadius; z <= loadRadius; z++)
            {
                for (int x = -loadRadius; x <= loadRadius; x++)
                {
                    desired.Add(new Vector3Int(
                        centerChunk.x + x,
                        centerChunk.y + y,
                        centerChunk.z + z
                    ));
                }
            }
        }

        return desired;
    }

    void LoadChunk(Vector3Int coord)
    {
        if (chunks.ContainsKey(coord))
            return;

        VoxelChunk chunk = new VoxelChunk(coord, transform, dirtMaterial, stoneMaterial);
        GenerateChunkData(chunk);

        if (modifiedChunkCache.TryGetValue(coord, out byte[] saved))
        {
            Array.Copy(saved, chunk.Voxels, saved.Length);
            chunk.MarkModified();
        }

        chunks.Add(coord, chunk);
        chunk.RebuildMesh(SampleVoxelAt);
        MarkLoadedNeighborsDirty(coord);
    }

    void UnloadChunk(Vector3Int coord, bool force = false)
    {
        if (usePlanetGeneration && !force)
            return;

        if (!chunks.TryGetValue(coord, out VoxelChunk chunk))
            return;

        if (chunk.IsModified)
            modifiedChunkCache[coord] = (byte[])chunk.Voxels.Clone();

        chunk.Destroy();
        chunks.Remove(coord);
    }

    void GenerateChunkData(VoxelChunk chunk)
    {
        Vector3Int coord = chunk.Coord;
        int originX = coord.x * VoxelTypes.ChunkSize;
        int originY = coord.y * VoxelTypes.ChunkSize;
        int originZ = coord.z * VoxelTypes.ChunkSize;

        for (int y = 0; y < VoxelTypes.ChunkSize; y++)
        {
            for (int z = 0; z < VoxelTypes.ChunkSize; z++)
            {
                for (int x = 0; x < VoxelTypes.ChunkSize; x++)
                {
                    Vector3 localCenter = new Vector3(originX + x + 0.5f, originY + y + 0.5f, originZ + z + 0.5f);
                    byte voxel = VoxelTerrainGenerator.GenerateVoxel(
                        localCenter,
                        seed,
                        usePlanetGeneration,
                        planetCenterLocal,
                        planetRadius
                    );
                    chunk.Voxels[VoxelTypes.ToIndex(x, y, z)] = voxel;
                }
            }
        }

        chunk.ClearModifiedFlag();
        chunk.MarkDirty();
    }

    public byte GetVoxel(int worldX, int worldY, int worldZ)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(81, (int)worldX, (int)worldY, (int)worldZ);
        return SampleVoxelAt(worldX, worldY, worldZ);
    }

    byte SampleVoxelAt(int worldX, int worldY, int worldZ)
    {
        if (!usePlanetGeneration && !VoxelTypes.IsInsideHeight(worldY))
            return VoxelTypes.Air;

        Vector3Int chunkCoord = VoxelTypes.WorldToChunkCoord(worldX, worldY, worldZ);
        Vector3Int localCoord = VoxelTypes.WorldToLocalCoord(worldX, worldY, worldZ);

        if (chunks.TryGetValue(chunkCoord, out VoxelChunk chunk))
            return chunk.GetLocalVoxel(localCoord.x, localCoord.y, localCoord.z);

        if (modifiedChunkCache.TryGetValue(chunkCoord, out byte[] cached))
            return cached[VoxelTypes.ToIndex(localCoord.x, localCoord.y, localCoord.z)];

        Vector3 localCenter = new Vector3(worldX + 0.5f, worldY + 0.5f, worldZ + 0.5f);
        return VoxelTerrainGenerator.GenerateVoxel(
            localCenter,
            seed,
            usePlanetGeneration,
            planetCenterLocal,
            planetRadius
        );
    }

    public bool SetVoxel(int worldX, int worldY, int worldZ, byte value)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(82, (int)worldX, (int)worldY, (int)worldZ, (int)value);
        if (!usePlanetGeneration && !VoxelTypes.IsInsideHeight(worldY))
            return false;

        Vector3Int chunkCoord = VoxelTypes.WorldToChunkCoord(worldX, worldY, worldZ);
        EnsureChunkLoaded(chunkCoord);

        if (!chunks.TryGetValue(chunkCoord, out VoxelChunk chunk))
            return false;

        Vector3Int localCoord = VoxelTypes.WorldToLocalCoord(worldX, worldY, worldZ);
        chunk.SetLocalVoxel(localCoord.x, localCoord.y, localCoord.z, value);
        MarkChunkAndNeighborsDirty(chunkCoord, localCoord);
        return true;
    }

    public bool DigVoxel(int worldX, int worldY, int worldZ)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(83, (int)worldX, (int)worldY, (int)worldZ);
        return SetVoxel(worldX, worldY, worldZ, VoxelTypes.Air);
    }

    void EnsureChunkLoaded(Vector3Int chunkCoord)
    {
        if (usePlanetGeneration)
            return;

        if (!chunks.ContainsKey(chunkCoord))
            LoadChunk(chunkCoord);
    }

    public List<ChunkSaveEntry> GetModifiedChunkSnapshots()
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(84);
        List<ChunkSaveEntry> result = new List<ChunkSaveEntry>();
        HashSet<Vector3Int> added = new HashSet<Vector3Int>();

        foreach (VoxelChunk chunk in chunks.Values)
        {
            if (!chunk.IsModified)
                continue;

            result.Add(CreateSaveEntry(chunk.Coord, chunk.Voxels));
            added.Add(chunk.Coord);
        }

        foreach (KeyValuePair<Vector3Int, byte[]> pair in modifiedChunkCache)
        {
            if (added.Contains(pair.Key))
                continue;

            result.Add(CreateSaveEntry(pair.Key, pair.Value));
        }

        return result;
    }

    static ChunkSaveEntry CreateSaveEntry(Vector3Int coord, byte[] voxels)
    {
        return new ChunkSaveEntry
        {
            x = coord.x,
            y = coord.y,
            z = coord.z,
            base64 = Convert.ToBase64String(voxels)
        };
    }

    public void ApplySaveData(VoxelWorldSaveData data)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(85);
        if (data == null)
            return;

        seed = data.seed;
        modifiedChunkCache.Clear();

        UnloadAllChunks();

        if (data.chunks != null)
        {
            foreach (ChunkSaveEntry entry in data.chunks)
            {
                Vector3Int coord = new Vector3Int(entry.x, entry.y, entry.z);
                byte[] saved = Convert.FromBase64String(entry.base64);
                if (saved.Length != VoxelTypes.ChunkSize * VoxelTypes.ChunkSize * VoxelTypes.ChunkSize)
                    continue;

                modifiedChunkCache[coord] = saved;
            }
        }
    }

    void UnloadAllChunks()
    {
        List<Vector3Int> coords = new List<Vector3Int>(chunks.Keys);
        foreach (Vector3Int coord in coords)
            UnloadChunk(coord, force: true);
    }

    void RebuildDirtyChunks()
    {
        foreach (VoxelChunk chunk in chunks.Values)
        {
            if (chunk.IsDirty)
                chunk.RebuildMesh(SampleVoxelAt);
        }
    }

    void MarkChunkAndNeighborsDirty(Vector3Int chunkCoord, Vector3Int localCoord)
    {
        if (chunks.TryGetValue(chunkCoord, out VoxelChunk chunk))
            chunk.MarkDirty();

        if (localCoord.x == 0)
            MarkChunkDirty(new Vector3Int(chunkCoord.x - 1, chunkCoord.y, chunkCoord.z));
        if (localCoord.x == VoxelTypes.ChunkSize - 1)
            MarkChunkDirty(new Vector3Int(chunkCoord.x + 1, chunkCoord.y, chunkCoord.z));

        if (localCoord.y == 0)
            MarkChunkDirty(new Vector3Int(chunkCoord.x, chunkCoord.y - 1, chunkCoord.z));
        if (localCoord.y == VoxelTypes.ChunkSize - 1)
            MarkChunkDirty(new Vector3Int(chunkCoord.x, chunkCoord.y + 1, chunkCoord.z));

        if (localCoord.z == 0)
            MarkChunkDirty(new Vector3Int(chunkCoord.x, chunkCoord.y, chunkCoord.z - 1));
        if (localCoord.z == VoxelTypes.ChunkSize - 1)
            MarkChunkDirty(new Vector3Int(chunkCoord.x, chunkCoord.y, chunkCoord.z + 1));
    }

    void MarkChunkDirty(Vector3Int chunkCoord)
    {
        if (chunks.TryGetValue(chunkCoord, out VoxelChunk neighbor))
            neighbor.MarkDirty();
    }

    void MarkLoadedNeighborsDirty(Vector3Int chunkCoord)
    {
        MarkChunkDirty(new Vector3Int(chunkCoord.x - 1, chunkCoord.y, chunkCoord.z));
        MarkChunkDirty(new Vector3Int(chunkCoord.x + 1, chunkCoord.y, chunkCoord.z));
        MarkChunkDirty(new Vector3Int(chunkCoord.x, chunkCoord.y - 1, chunkCoord.z));
        MarkChunkDirty(new Vector3Int(chunkCoord.x, chunkCoord.y + 1, chunkCoord.z));
        MarkChunkDirty(new Vector3Int(chunkCoord.x, chunkCoord.y, chunkCoord.z - 1));
        MarkChunkDirty(new Vector3Int(chunkCoord.x, chunkCoord.y, chunkCoord.z + 1));
    }

    void PlacePlayerAtSpawn()
    {
        if (!autoPlacePlayerOnStart || playerSpawn == null)
            return;

        if (usePlanetGeneration)
        {
            Vector3 direction = spawnDirectionLocal.sqrMagnitude < 0.001f ? Vector3.up : spawnDirectionLocal.normalized;
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
            }
            else
            {
                playerSpawn.SetPositionAndRotation(worldPosition, worldRotation);
            }
            return;
        }

        int x = 0;
        int z = 0;
        int y = GetFlatSurfaceHeight(x, z) + Mathf.RoundToInt(spawnHeightOffset);
        Vector3 flatSpawn = new Vector3(x + 0.5f, y, z + 0.5f);
        playerSpawn.position = transform.TransformPoint(flatSpawn);
    }

    int GetFlatSurfaceHeight(int worldX, int worldZ)
    {
        for (int y = VoxelTypes.MaxWorldHeight - 1; y >= VoxelTypes.MinWorldHeight; y--)
        {
            if (VoxelTypes.IsSolid(SampleVoxelAt(worldX, y, worldZ)))
                return y;
        }

        return VoxelTypes.MinWorldHeight;
    }
}
