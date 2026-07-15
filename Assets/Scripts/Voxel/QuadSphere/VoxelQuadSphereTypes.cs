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
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(191);}
    try
    {
        return Face == other.Face && U == other.U && V == other.V && Depth == other.Depth;
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public override bool Equals(object obj)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(192);}
    try
    {
        return obj is QuadSphereVoxelAddress other && Equals(other);
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public override int GetHashCode()
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(193);}
    try
    {
        unchecked
        {
            int hash = (int)Face;
            hash = (hash * 397) ^ U;
            hash = (hash * 397) ^ V;
            hash = (hash * 397) ^ Depth;
            return hash;
        }
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}
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
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(194);}
    try
    {
        return Face == other.Face && ChunkU == other.ChunkU && ChunkV == other.ChunkV && ChunkDepth == other.ChunkDepth;
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public override bool Equals(object obj)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(195);}
    try
    {
        return obj is QuadSphereChunkKey other && Equals(other);
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public override int GetHashCode()
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(196);}
    try
    {
        unchecked
        {
            int hash = (int)Face;
            hash = (hash * 397) ^ ChunkU;
            hash = (hash * 397) ^ ChunkV;
            hash = (hash * 397) ^ ChunkDepth;
            return hash;
        }
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}
}
