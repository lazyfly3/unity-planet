using System;

public enum QuadSphereFace
{
    PosX = 0,
    NegX = 1,
    PosY = 2,
    NegY = 3,
    PosZ = 4,
    NegZ = 5
}

public struct QuadSphereVoxelAddress : IEquatable<QuadSphereVoxelAddress>
{
    public QuadSphereFace Face;
    public int U;
    public int V;
    public int Depth;

    public QuadSphereVoxelAddress(QuadSphereFace face, int u, int v, int depth)
    {
        Face = face;
        U = u;
        V = v;
        Depth = depth;
    }

    public bool Equals(QuadSphereVoxelAddress other)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(115);
        return Face == other.Face && U == other.U && V == other.V && Depth == other.Depth;
    }

    public override bool Equals(object obj)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(116);
        return obj is QuadSphereVoxelAddress other && Equals(other);
    }

    public override int GetHashCode()
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(117);
        unchecked
        {
            int hash = (int)Face;
            hash = (hash * 397) ^ U;
            hash = (hash * 397) ^ V;
            hash = (hash * 397) ^ Depth;
            return hash;
        }
    }
}

public struct QuadSphereChunkKey : IEquatable<QuadSphereChunkKey>
{
    public QuadSphereFace Face;
    public int ChunkU;
    public int ChunkV;
    public int ChunkDepth;

    public QuadSphereChunkKey(QuadSphereFace face, int chunkU, int chunkV, int chunkDepth)
    {
        Face = face;
        ChunkU = chunkU;
        ChunkV = chunkV;
        ChunkDepth = chunkDepth;
    }

    public bool Equals(QuadSphereChunkKey other)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(118);
        return Face == other.Face && ChunkU == other.ChunkU && ChunkV == other.ChunkV && ChunkDepth == other.ChunkDepth;
    }

    public override bool Equals(object obj)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(119);
        return obj is QuadSphereChunkKey other && Equals(other);
    }

    public override int GetHashCode()
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(120);
        unchecked
        {
            int hash = (int)Face;
            hash = (hash * 397) ^ ChunkU;
            hash = (hash * 397) ^ ChunkV;
            hash = (hash * 397) ^ ChunkDepth;
            return hash;
        }
    }
}
