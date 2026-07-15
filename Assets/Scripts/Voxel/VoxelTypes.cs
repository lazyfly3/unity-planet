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
    {if(FSPDebuger.EnableLogTrackInternal){FSPDebuger.PushDepth();FSPDebuger.LogTrack(113, (int)x, (int)y, (int)z);}
        return x + y * ChunkSize + z * ChunkSize * ChunkSize;
    
    if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.PopDepth();}

    public static bool IsSolid(byte voxel)
    {if(FSPDebuger.EnableLogTrackInternal){FSPDebuger.PushDepth();FSPDebuger.LogTrack(114, (int)voxel);}
        return voxel != Air;
    
    if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.PopDepth();}

    public static bool IsInsideHeight(int worldY)
    {if(FSPDebuger.EnableLogTrackInternal){FSPDebuger.PushDepth();FSPDebuger.LogTrack(115, (int)worldY);}
        return worldY >= MinWorldHeight && worldY < MaxWorldHeight;
    
    if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.PopDepth();}

    public static Vector3Int WorldToChunkCoord(int worldX, int worldY, int worldZ)
    {if(FSPDebuger.EnableLogTrackInternal){FSPDebuger.PushDepth();FSPDebuger.LogTrack(116, (int)worldX, (int)worldY, (int)worldZ);}
        return new Vector3Int(
            FloorDiv(worldX, ChunkSize),
            FloorDiv(worldY, ChunkSize),
            FloorDiv(worldZ, ChunkSize)
        );
    
    if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.PopDepth();}

    public static Vector3Int WorldToLocalCoord(int worldX, int worldY, int worldZ)
    {if(FSPDebuger.EnableLogTrackInternal){FSPDebuger.PushDepth();FSPDebuger.LogTrack(117, (int)worldX, (int)worldY, (int)worldZ);}
        return new Vector3Int(
            Mod(worldX, ChunkSize),
            Mod(worldY, ChunkSize),
            Mod(worldZ, ChunkSize)
        );
    
    if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.PopDepth();}

    public static int FloorDiv(int value, int divisor)
    {if(FSPDebuger.EnableLogTrackInternal){FSPDebuger.PushDepth();FSPDebuger.LogTrack(118, (int)value, (int)divisor);}
        if (value >= 0)
            return value / divisor;

        return (value - divisor + 1) / divisor;
    
    if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.PopDepth();}

    public static int Mod(int value, int divisor)
    {if(FSPDebuger.EnableLogTrackInternal){FSPDebuger.PushDepth();FSPDebuger.LogTrack(119, (int)value, (int)divisor);}
        int result = value % divisor;
        return result < 0 ? result + divisor : result;
    
    if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.PopDepth();}
}
