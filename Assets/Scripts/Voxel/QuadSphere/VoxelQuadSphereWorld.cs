using System.Collections.Generic;
using UnityEngine;

public class VoxelQuadSphereWorld : MonoBehaviour
{
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

    readonly Dictionary<QuadSphereChunkKey, VoxelQuadSphereChunk> chunks = new Dictionary<QuadSphereChunkKey, VoxelQuadSphereChunk>();

    public int Seed => seed;
    public bool UsePlanetGeneration => true;
    public float PlanetRadius => planetRadius;
    public float SurfaceGravity => surfaceGravity;
    public float GravitationalParameter => PlanetGravity.ComputeGravitationalParameter(surfaceGravity, planetRadius);
    public int FaceGridSize => faceGridSize;
    public int MaxDepth => maxDepth;
    public int InnerSolidDepthLayers => innerSolidDepthLayers;

    public Vector3 GetPlanetCenterWorld()
    {
        return transform.TransformPoint(planetCenterLocal);
    }

    public Vector3 GetPlanetCenterLocal()
    {
        return planetCenterLocal;
    }

    void Start()
    {
        GenerateEntirePlanet();
        PlacePlayerAtSpawn();
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
    {
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
        chunk.RebuildMesh(SampleVoxelAt, faceGridSize, maxDepth, planetRadius, planetCenterLocal);
        MarkLoadedNeighborsDirty(key);
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
    {
        return SetVoxel(address, VoxelTypes.Air);
    }

    public bool SetVoxel(QuadSphereVoxelAddress address, byte value)
    {
        if (address.U < 0 || address.V < 0 || address.Depth < 0
            || address.U >= faceGridSize || address.V >= faceGridSize || address.Depth >= maxDepth)
            return false;

        QuadSphereChunkKey key = AddressToChunkKey(address);
        if (!chunks.TryGetValue(key, out VoxelQuadSphereChunk chunk))
            return false;

        Vector3Int local = AddressToLocalCoord(address);
        chunk.SetLocalVoxel(local.x, local.y, local.z, value);
        MarkChunkAndNeighborsDirty(key, local);
        return true;
    }

    public bool TryDigAtLocalPoint(Vector3 localPoint)
    {
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

        Vector3 direction = spawnDirectionLocal.sqrMagnitude < 0.001f ? Vector3.up : spawnDirectionLocal.normalized;
        Vector3 localSpawn = planetCenterLocal + direction * (planetRadius + spawnHeightOffset);
        playerSpawn.position = transform.TransformPoint(localSpawn);
        playerSpawn.rotation = PlanetGravity.GetSurfaceRotation(playerSpawn.position, GetPlanetCenterWorld());
    }
}
