using UnityEngine;

public static class VoxelQuadSphereTerrain
{
    const float CaveThreshold = 0.62f;
    const float CaveScale = 0.06f;

    public static byte GenerateVoxel(
        QuadSphereFace face,
        int cellU,
        int cellV,
        int depth,
        int gridSize,
        int maxDepth,
        int innerSolidDepthLayers,
        int seed,
        Vector3 planetCenter,
        float planetRadius)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(101, (int)cellU, (int)cellV, (int)depth, (int)gridSize, (int)maxDepth, (int)innerSolidDepthLayers, (int)seed);
        if (cellU < 0 || cellV < 0 || cellU >= gridSize || cellV >= gridSize || depth < 0 || depth >= maxDepth)
            return VoxelTypes.Air;

        Vector3 center = VoxelQuadSphereMapping.FaceCellCenterLocal(
            face, cellU, cellV, depth, gridSize, planetRadius, planetCenter);

        float surfaceNoise = GetSurfaceNoise(center, seed);
        float radialDistance = Vector3.Distance(center, planetCenter);
        float density = planetRadius - radialDistance + surfaceNoise;

        if (density <= 0f)
            return VoxelTypes.Air;

        int innerShellLayers = Mathf.Clamp(innerSolidDepthLayers, 1, maxDepth);
        int innerSolidStartDepth = maxDepth - innerShellLayers;

        // 最内层强制实心岩层，洞穴噪声不得穿透，避免洞底连通未生成虚空
        if (depth >= innerSolidStartDepth)
            return VoxelTypes.Stone;

        float depthFromSurface = planetRadius - radialDistance + surfaceNoise;
        byte voxel = GetLayerVoxelByDepth(depthFromSurface);

        if (IsCave(center, seed))
            return VoxelTypes.Air;

        return voxel;
    }

    static byte GetLayerVoxelByDepth(float depthFromSurface)
    {
        if (depthFromSurface <= 1f)
            return VoxelTypes.Dirt;

        if (depthFromSurface >= 4f)
            return VoxelTypes.Stone;

        return VoxelTypes.Dirt;
    }

    static float GetSurfaceNoise(Vector3 localPosition, int seed)
    {
        float offset = seed * 0.017f;
        float noise = Sample3DNoise(
            localPosition.x * 0.04f + offset,
            localPosition.y * 0.04f + offset * 1.3f,
            localPosition.z * 0.04f + offset * 2.1f
        );
        return (noise - 0.5f) * 8f;
    }

    static bool IsCave(Vector3 localPosition, int seed)
    {
        int wx = Mathf.FloorToInt(localPosition.x);
        int wy = Mathf.FloorToInt(localPosition.y);
        int wz = Mathf.FloorToInt(localPosition.z);

        float offsetX = seed * 0.041f + 17.3f;
        float offsetY = seed * 0.023f + 31.7f;
        float offsetZ = seed * 0.037f + 53.1f;

        float noise = Sample3DNoise(
            wx * CaveScale + offsetX,
            wy * CaveScale + offsetY,
            wz * CaveScale + offsetZ
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
