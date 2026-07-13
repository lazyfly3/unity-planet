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
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(62, (int)x, (int)y, (int)z);
        return Voxels[VoxelTypes.ToIndex(x, y, z)];
    }

    public void SetLocalVoxel(int x, int y, int z, byte value)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(63, (int)x, (int)y, (int)z, (int)value);
        if (x < 0 || y < 0 || z < 0 || x >= VoxelTypes.ChunkSize || y >= VoxelTypes.ChunkSize || z >= VoxelTypes.ChunkSize)
            return;

        int index = VoxelTypes.ToIndex(x, y, z);
        if (Voxels[index] == value)
            return;

        Voxels[index] = value;
        IsDirty = true;
        IsModified = true;
    }

    public void MarkDirty()
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(64);
        IsDirty = true;
    }

    public void MarkModified()
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(65);
        IsModified = true;
    }

    public void ClearModifiedFlag()
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(66);
        IsModified = false;
    }

    public void RebuildMesh(System.Func<int, int, int, byte> getWorldVoxel)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(67);
        if (runtimeMesh != null)
            Object.Destroy(runtimeMesh);

        runtimeMesh = VoxelMesher.BuildChunkMesh(getWorldVoxel, Coord);
        meshFilter.sharedMesh = runtimeMesh;
        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = runtimeMesh;
        IsDirty = false;
    }

    public void Destroy()
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(68);
        if (runtimeMesh != null)
            Object.Destroy(runtimeMesh);

        if (viewObject != null)
            Object.Destroy(viewObject);
    }
}
