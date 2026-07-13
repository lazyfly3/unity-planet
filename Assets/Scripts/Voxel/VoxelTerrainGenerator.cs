using UnityEngine;

public static class VoxelTerrainGenerator
{
    const float CaveThreshold = 0.62f;
    const float CaveScale = 0.06f;

    public static byte GenerateVoxel(Vector3 localVoxelCenter, int seed, bool planetMode, Vector3 planetCenterLocal, float planetRadius)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(77, (int)seed, (planetMode?1:0), (int)planetRadius);
        if (planetMode)
            return GeneratePlanetVoxel(localVoxelCenter, seed, planetCenterLocal, planetRadius);

        return GenerateFlatVoxel(localVoxelCenter, seed);
    }

    static byte GenerateFlatVoxel(Vector3 localVoxelCenter, int seed)
    {
        int worldY = Mathf.FloorToInt(localVoxelCenter.y);
        if (!VoxelTypes.IsInsideHeight(worldY))
            return VoxelTypes.Air;

        int worldX = Mathf.FloorToInt(localVoxelCenter.x);
        int worldZ = Mathf.FloorToInt(localVoxelCenter.z);

        float offsetX = seed * 0.0137f;
        float offsetZ = seed * 0.0291f;
        float height = Mathf.PerlinNoise(worldX * 0.08f + offsetX, worldZ * 0.08f + offsetZ) * 20f + 30f;
        int surfaceHeight = Mathf.FloorToInt(height);

        if (worldY > surfaceHeight)
            return VoxelTypes.Air;

        byte voxel = GetLayerVoxel(worldY, surfaceHeight);

        if (IsCave(worldX, worldY, worldZ, seed))
            return VoxelTypes.Air;

        return voxel;
    }

    static byte GeneratePlanetVoxel(Vector3 localVoxelCenter, int seed, Vector3 planetCenterLocal, float planetRadius)
    {
        float surfaceNoise = GetPlanetSurfaceNoise(localVoxelCenter, seed);
        float distance = Vector3.Distance(localVoxelCenter, planetCenterLocal);
        float density = planetRadius - distance + surfaceNoise;

        if (density <= 0f)
            return VoxelTypes.Air;

        float depth = planetRadius - distance + surfaceNoise;
        byte voxel = GetLayerVoxelByDepth(depth);

        int wx = Mathf.FloorToInt(localVoxelCenter.x);
        int wy = Mathf.FloorToInt(localVoxelCenter.y);
        int wz = Mathf.FloorToInt(localVoxelCenter.z);

        if (IsCave(wx, wy, wz, seed))
            return VoxelTypes.Air;

        return voxel;
    }

    static float GetPlanetSurfaceNoise(Vector3 localPosition, int seed)
    {
        float offset = seed * 0.017f;
        float noise = Sample3DNoise(
            localPosition.x * 0.04f + offset,
            localPosition.y * 0.04f + offset * 1.3f,
            localPosition.z * 0.04f + offset * 2.1f
        );
        return (noise - 0.5f) * 8f;
    }

    static byte GetLayerVoxel(int worldY, int surfaceHeight)
    {
        if (worldY == surfaceHeight)
            return VoxelTypes.Dirt;

        if (worldY <= surfaceHeight - 3)
            return VoxelTypes.Stone;

        return VoxelTypes.Dirt;
    }

    static byte GetLayerVoxelByDepth(float depthFromSurface)
    {
        if (depthFromSurface <= 1f)
            return VoxelTypes.Dirt;

        if (depthFromSurface >= 4f)
            return VoxelTypes.Stone;

        return VoxelTypes.Dirt;
    }

    static bool IsCave(int worldX, int worldY, int worldZ, int seed)
    {
        float offsetX = seed * 0.041f + 17.3f;
        float offsetY = seed * 0.023f + 31.7f;
        float offsetZ = seed * 0.037f + 53.1f;

        float noise = Sample3DNoise(
            worldX * CaveScale + offsetX,
            worldY * CaveScale + offsetY,
            worldZ * CaveScale + offsetZ
        );

        return noise > CaveThreshold;
    }

    static float Sample3DNoise(float x, float y, float z)
    {
        float xy = Mathf.PerlinNoise(x, y);
        float xz = Mathf.PerlinNoise(x, z);
        float yz = Mathf.PerlinNoise(y, z);
        return (xy + xz + yz) / 3f;
    }
}
