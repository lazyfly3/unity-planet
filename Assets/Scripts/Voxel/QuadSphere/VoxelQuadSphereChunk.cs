using UnityEngine;

public class VoxelQuadSphereChunk
{
    public QuadSphereChunkKey Key { get; }
    public byte[] Voxels { get; }
    public bool IsDirty { get; private set; }
    public bool IsModified { get; private set; }
    public bool HasMesh => runtimeMesh != null;
    public Collider Collider => meshCollider;

    readonly GameObject viewObject;
    readonly MeshFilter meshFilter;
    readonly MeshCollider meshCollider;
    Mesh runtimeMesh;

    public VoxelQuadSphereChunk(QuadSphereChunkKey key, Transform parent, Material dirtMaterial, Material stoneMaterial)
    {
        Key = key;
        Voxels = new byte[VoxelTypes.ChunkSize * VoxelTypes.ChunkSize * VoxelTypes.ChunkSize];

        viewObject = new GameObject($"QSChunk_{key.Face}_{key.ChunkU}_{key.ChunkV}_{key.ChunkDepth}");
        viewObject.transform.SetParent(parent, false);
        viewObject.transform.localPosition = Vector3.zero;

        meshFilter = viewObject.AddComponent<MeshFilter>();
        MeshRenderer renderer = viewObject.AddComponent<MeshRenderer>();
        meshCollider = viewObject.AddComponent<MeshCollider>();

        if (stoneMaterial != null)
            renderer.sharedMaterials = new[] { dirtMaterial, stoneMaterial };
        else
            renderer.sharedMaterial = dirtMaterial;
    }

    public byte GetLocalVoxel(int x, int y, int z)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(93, (int)x, (int)y, (int)z);
        return Voxels[VoxelTypes.ToIndex(x, y, z)];
    }

    public void SetLocalVoxel(int x, int y, int z, byte value)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(94, (int)x, (int)y, (int)z, (int)value);
        if (x < 0 || y < 0 || z < 0 || x >= VoxelTypes.ChunkSize || y >= VoxelTypes.ChunkSize || z >= VoxelTypes.ChunkSize)
            return;

        int index = VoxelTypes.ToIndex(x, y, z);
        if (Voxels[index] == value)
            return;

        Voxels[index] = value;
        IsDirty = true;
        IsModified = true;
    }

    public void MarkDirty() => IsDirty = true;
    public void MarkModified() => IsModified = true;
    public void ClearModifiedFlag() => IsModified = false;

    public void RebuildMesh(System.Func<QuadSphereVoxelAddress, byte> getVoxel, int gridSize, int maxDepth, float planetRadius, Vector3 planetCenter)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(95, (int)gridSize, (int)maxDepth, (int)planetRadius);
        if (runtimeMesh != null)
            Object.Destroy(runtimeMesh);

        runtimeMesh = VoxelQuadSphereMesher.BuildChunkMesh(getVoxel, Key, gridSize, maxDepth, planetRadius, planetCenter);
        meshFilter.sharedMesh = runtimeMesh;
        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = runtimeMesh;
        IsDirty = false;
    }

    public void AddMeshSnapshot(QuadSphereChunkSaveEntry entry)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(97);
        if (entry == null || runtimeMesh == null)
            return;

        entry.meshVertices = runtimeMesh.vertices;
        entry.meshNormals = runtimeMesh.normals;
        entry.meshSubMeshTriangles = new int[runtimeMesh.subMeshCount][];
        for (int subMesh = 0; subMesh < runtimeMesh.subMeshCount; subMesh++)
            entry.meshSubMeshTriangles[subMesh] = runtimeMesh.GetTriangles(subMesh);
    }

    public bool RestoreMesh(QuadSphereChunkSaveEntry entry)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(98);
        if (entry == null
            || entry.meshVertices == null
            || entry.meshNormals == null
            || entry.meshNormals.Length != entry.meshVertices.Length
            || entry.meshSubMeshTriangles == null
            || entry.meshSubMeshTriangles.Length == 0)
        {
            return false;
        }

        if (runtimeMesh != null)
            Object.Destroy(runtimeMesh);

        runtimeMesh = new Mesh
        {
            name = $"QuadSphereChunk_{Key.Face}_{Key.ChunkU}_{Key.ChunkV}_{Key.ChunkDepth}"
        };
        if (entry.meshVertices.Length > ushort.MaxValue)
            runtimeMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        runtimeMesh.vertices = entry.meshVertices;
        runtimeMesh.normals = entry.meshNormals;
        runtimeMesh.subMeshCount = entry.meshSubMeshTriangles.Length;
        for (int subMesh = 0; subMesh < entry.meshSubMeshTriangles.Length; subMesh++)
            runtimeMesh.SetTriangles(entry.meshSubMeshTriangles[subMesh], subMesh, false);
        runtimeMesh.RecalculateBounds();

        meshFilter.sharedMesh = runtimeMesh;
        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = runtimeMesh;
        IsDirty = false;
        return true;
    }

    public void Destroy()
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(96);
        if (runtimeMesh != null)
            Object.Destroy(runtimeMesh);

        if (viewObject != null)
            Object.Destroy(viewObject);
    }
}
