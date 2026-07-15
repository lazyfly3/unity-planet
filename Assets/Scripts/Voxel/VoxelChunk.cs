using UnityEngine;

public class VoxelChunk
{
    public Vector3Int Coord { get; }
    public byte[] Voxels { get; }
    public bool IsDirty { get; private set; }
    public bool IsModified { get; private set; }

    readonly GameObject viewObject;
    readonly MeshFilter meshFilter;
    readonly MeshCollider meshCollider;

    Mesh runtimeMesh;

    public VoxelChunk(Vector3Int coord, Transform parent, Material dirtMaterial, Material stoneMaterial)
    {
        Coord = coord;
        Voxels = new byte[VoxelTypes.ChunkSize * VoxelTypes.ChunkSize * VoxelTypes.ChunkSize];

        viewObject = new GameObject($"Chunk_{coord.x}_{coord.y}_{coord.z}");
        viewObject.transform.SetParent(parent, false);
        viewObject.transform.localPosition = new Vector3(
            coord.x * VoxelTypes.ChunkSize,
            coord.y * VoxelTypes.ChunkSize,
            coord.z * VoxelTypes.ChunkSize
        );

        meshFilter = viewObject.AddComponent<MeshFilter>();
        MeshRenderer renderer = viewObject.AddComponent<MeshRenderer>();
        meshCollider = viewObject.AddComponent<MeshCollider>();

        if (stoneMaterial != null)
            renderer.sharedMaterials = new[] { dirtMaterial, stoneMaterial };
        else
            renderer.sharedMaterial = dirtMaterial;
    }

    public byte GetLocalVoxel(int x, int y, int z)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(131, (int)x, (int)y, (int)z);}
    try
    {
        return Voxels[VoxelTypes.ToIndex(x, y, z)];
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public void SetLocalVoxel(int x, int y, int z, byte value)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(132, (int)x, (int)y, (int)z, (int)value);}
    try
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
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public void MarkDirty()
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(133);}
    try
    {
        IsDirty = true;
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public void MarkModified()
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(134);}
    try
    {
        IsModified = true;
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public void ClearModifiedFlag()
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(135);}
    try
    {
        IsModified = false;
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public void RebuildMesh(System.Func<int, int, int, byte> getWorldVoxel)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(136);}
    try
    {
        if (runtimeMesh != null)
            Object.Destroy(runtimeMesh);

        runtimeMesh = VoxelMesher.BuildChunkMesh(getWorldVoxel, Coord);
        meshFilter.sharedMesh = runtimeMesh;
        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = runtimeMesh;
        IsDirty = false;
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public void Destroy()
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(137);}
    try
    {
        if (runtimeMesh != null)
            Object.Destroy(runtimeMesh);

        if (viewObject != null)
            Object.Destroy(viewObject);
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}
}
