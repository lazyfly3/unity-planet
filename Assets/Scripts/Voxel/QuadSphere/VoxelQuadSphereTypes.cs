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
    {if(FSPDebuger.EnableLogTrackInternal){FSPDebuger.PushDepth();FSPDebuger.LogTrack(151);}
        return Face == other.Face && U == other.U && V == other.V && Depth == other.Depth;
    
    if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.PopDepth();}

    public override bool Equals(object obj)
    {if(FSPDebuger.EnableLogTrackInternal){FSPDebuger.PushDepth();FSPDebuger.LogTrack(152);}
        return obj is QuadSphereVoxelAddress other && Equals(other);
    
    if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.PopDepth();}

    public override int GetHashCode()
    {if(FSPDebuger.EnableLogTrackInternal){FSPDebuger.PushDepth();FSPDebuger.LogTrack(153);}
        unchecked
        {
            int hash = (int)Face;
            hash = (hash * 397) ^ U;
            hash = (hash * 397) ^ V;
            hash = (hash * 397) ^ Depth;
            return hash;
        }
    
    if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.PopDepth();}
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
    {if(FSPDebuger.EnableLogTrackInternal){FSPDebuger.PushDepth();FSPDebuger.LogTrack(154);}
        return Face == other.Face && ChunkU == other.ChunkU && ChunkV == other.ChunkV && ChunkDepth == other.ChunkDepth;
    
    if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.PopDepth();}

    public override bool Equals(object obj)
    {if(FSPDebuger.EnableLogTrackInternal){FSPDebuger.PushDepth();FSPDebuger.LogTrack(155);}
        return obj is QuadSphereChunkKey other && Equals(other);
    
    if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.PopDepth();}

    public override int GetHashCode()
    {if(FSPDebuger.EnableLogTrackInternal){FSPDebuger.PushDepth();FSPDebuger.LogTrack(156);}
        unchecked
        {
            int hash = (int)Face;
            hash = (hash * 397) ^ ChunkU;
            hash = (hash * 397) ^ ChunkV;
            hash = (hash * 397) ^ ChunkDepth;
            return hash;
        }
    
    if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.PopDepth();}
}
