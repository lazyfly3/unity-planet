using UnityEngine;

public class VoxelQuadSphereChunk
{
    public QuadSphereChunkKey Key { get; }
    public byte[] Voxels { get; }
    public bool IsDirty { get; private set; }
    public bool IsModified { get; private set; }

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
    {
        return Voxels[VoxelTypes.ToIndex(x, y, z)];
    }

    public void SetLocalVoxel(int x, int y, int z, byte value)
    {
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
    {
        if (runtimeMesh != null)
            Object.Destroy(runtimeMesh);

        runtimeMesh = VoxelQuadSphereMesher.BuildChunkMesh(getVoxel, Key, gridSize, maxDepth, planetRadius, planetCenter);
        meshFilter.sharedMesh = runtimeMesh;
        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = runtimeMesh;
        IsDirty = false;
    }

    public void Destroy()
    {
        if (runtimeMesh != null)
            Object.Destroy(runtimeMesh);

        if (viewObject != null)
            Object.Destroy(viewObject);
    }
}
