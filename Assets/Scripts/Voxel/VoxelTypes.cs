using UnityEngine;

public static class VoxelTypes
{
    public const int ChunkSize = 16;

    // 第三阶段：水平方向无限，高度有限
    public const int MinWorldHeight = 0;
    public const int MaxWorldHeight = 128;

    public const byte Air = 0;
    public const byte Dirt = 1;
    public const byte Stone = 2;

    public static int ToIndex(int x, int y, int z)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(147, (int)x, (int)y, (int)z);}
    try
    {
        return x + y * ChunkSize + z * ChunkSize * ChunkSize;
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public static bool IsSolid(byte voxel)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(148, (int)voxel);}
    try
    {
        return voxel != Air;
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public static bool IsInsideHeight(int worldY)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(149, (int)worldY);}
    try
    {
        return worldY >= MinWorldHeight && worldY < MaxWorldHeight;
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public static Vector3Int WorldToChunkCoord(int worldX, int worldY, int worldZ)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(150, (int)worldX, (int)worldY, (int)worldZ);}
    try
    {
        return new Vector3Int(
            FloorDiv(worldX, ChunkSize),
            FloorDiv(worldY, ChunkSize),
            FloorDiv(worldZ, ChunkSize)
        );
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public static Vector3Int WorldToLocalCoord(int worldX, int worldY, int worldZ)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(151, (int)worldX, (int)worldY, (int)worldZ);}
    try
    {
        return new Vector3Int(
            Mod(worldX, ChunkSize),
            Mod(worldY, ChunkSize),
            Mod(worldZ, ChunkSize)
        );
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public static int FloorDiv(int value, int divisor)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(152, (int)value, (int)divisor);}
    try
    {
        if (value >= 0)
            return value / divisor;

        return (value - divisor + 1) / divisor;
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public static int Mod(int value, int divisor)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(153, (int)value, (int)divisor);}
    try
    {
        int result = value % divisor;
        return result < 0 ? result + divisor : result;
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}
}
