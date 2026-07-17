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
    {
return x + y * ChunkSize + z * ChunkSize * ChunkSize;
    
}

    public static bool IsSolid(byte voxel)
    {
return voxel != Air;
    
}

    public static bool IsInsideHeight(int worldY)
    {
return worldY >= MinWorldHeight && worldY < MaxWorldHeight;
    
}

    public static Vector3Int WorldToChunkCoord(int worldX, int worldY, int worldZ)
    {
return new Vector3Int(
            FloorDiv(worldX, ChunkSize),
            FloorDiv(worldY, ChunkSize),
            FloorDiv(worldZ, ChunkSize)
        );
    
}

    public static Vector3Int WorldToLocalCoord(int worldX, int worldY, int worldZ)
    {
return new Vector3Int(
            Mod(worldX, ChunkSize),
            Mod(worldY, ChunkSize),
            Mod(worldZ, ChunkSize)
        );
    
}

    public static int FloorDiv(int value, int divisor)
    {
if (value >= 0)
            return value / divisor;

        return (value - divisor + 1) / divisor;
    
}

    public static int Mod(int value, int divisor)
    {
int result = value % divisor;
        return result < 0 ? result + divisor : result;
    
}
}
