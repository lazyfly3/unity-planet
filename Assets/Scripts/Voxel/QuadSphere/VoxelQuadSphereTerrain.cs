using System;
using UnityEngine;

[Serializable]
public sealed class PlanetTerrainSettings
{
    [Header("Surface Shape")]
    [Min(0.001f)] public float continentScale = 0.018f;
    [Min(0f)] public float continentHeight = 5f;
    [Min(0.001f)] public float detailScale = 0.065f;
    [Min(0f)] public float detailHeight = 2.5f;
    [Min(0f)] public float ridgeHeight = 1f;

    [Header("Ground Layers")]
    [Min(0f)] public float surfaceLayerDepth = 1f;
    [Min(0f)] public float stoneDepth = 4f;

    [Header("Caves")]
    public bool generateCaves = true;
    [Min(0.001f)] public float caveScale = 0.06f;
    [Range(0f, 1f)] public float caveThreshold = 0.62f;
    [Min(0f)] public float caveSurfaceClearance = 2f;

    public static PlanetTerrainSettings CreateLegacy()
    {
        return new PlanetTerrainSettings
        {
            continentScale = 0.04f,
            continentHeight = 8f,
            detailHeight = 0f,
            ridgeHeight = 0f,
            surfaceLayerDepth = 1f,
            stoneDepth = 4f,
            generateCaves = true,
            caveScale = 0.06f,
            caveThreshold = 0.62f,
            caveSurfaceClearance = 0f
        };}

    public PlanetTerrainSettings Clone()
    {
        return (PlanetTerrainSettings)MemberwiseClone();}

    public void ClampValues()
    {
        continentScale = Mathf.Max(0.001f, continentScale);
        continentHeight = Mathf.Max(0f, continentHeight);
        detailScale = Mathf.Max(0.001f, detailScale);
        detailHeight = Mathf.Max(0f, detailHeight);
        ridgeHeight = Mathf.Max(0f, ridgeHeight);
        surfaceLayerDepth = Mathf.Max(0f, surfaceLayerDepth);
        stoneDepth = Mathf.Max(surfaceLayerDepth, stoneDepth);
        caveScale = Mathf.Max(0.001f, caveScale);
        caveThreshold = Mathf.Clamp01(caveThreshold);
        caveSurfaceClearance = Mathf.Max(0f, caveSurfaceClearance);}
}

public static class VoxelQuadSphereTerrain
{
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
        float planetRadius,
        PlanetTerrainSettings settings,
        float riverCarveDepth = 0f)
    {
        if (cellU < 0 || cellV < 0 || cellU >= gridSize || cellV >= gridSize || depth < 0 || depth >= maxDepth)
            return VoxelTypes.Air;

        settings = settings ?? PlanetTerrainSettings.CreateLegacy();
        Vector3 center = VoxelQuadSphereMapping.FaceCellCenterLocal(
            face, cellU, cellV, depth, gridSize, planetRadius, planetCenter);

        float radialDistance = Vector3.Distance(center, planetCenter);
        float surfaceNoise = GetSurfaceNoise(center - planetCenter, seed, settings);
        float depthFromSurface = planetRadius - radialDistance + surfaceNoise - Mathf.Max(0f, riverCarveDepth);
        if (depthFromSurface <= 0f)
            return VoxelTypes.Air;

        int innerShellLayers = Mathf.Clamp(innerSolidDepthLayers, 1, maxDepth);
        if (depth >= maxDepth - innerShellLayers)
            return VoxelTypes.Stone;

        byte voxel = GetLayerVoxelByDepth(depthFromSurface, settings);
        if (settings.generateCaves
            && depthFromSurface > settings.caveSurfaceClearance
            && IsCave(center - planetCenter, seed, settings))
        {
            return VoxelTypes.Air;
        }

        return voxel;}

    static byte GetLayerVoxelByDepth(float depthFromSurface, PlanetTerrainSettings settings)
    {
        if (depthFromSurface <= settings.surfaceLayerDepth)
            return VoxelTypes.Dirt;
        return depthFromSurface >= settings.stoneDepth ? VoxelTypes.Stone : VoxelTypes.Dirt;
    }

    public static float GetSurfaceNoise(Vector3 localPosition, int seed, PlanetTerrainSettings settings)
    {
        float offset = seed * 0.017f;
        float continent = SampleSeededNoise(localPosition, settings.continentScale, offset);
        float detail = SampleSeededNoise(localPosition, settings.detailScale, offset + 79.31f);
        float ridgeNoise = SampleSeededNoise(localPosition, settings.detailScale * 0.55f, offset + 157.7f);
        float ridge = 1f - Mathf.Abs(ridgeNoise * 2f - 1f);

        return (continent - 0.5f) * settings.continentHeight
            + (detail - 0.5f) * settings.detailHeight
            + (ridge * ridge - 0.35f) * settings.ridgeHeight;}

    static bool IsCave(Vector3 localPosition, int seed, PlanetTerrainSettings settings)
    {
        int wx = Mathf.FloorToInt(localPosition.x);
        int wy = Mathf.FloorToInt(localPosition.y);
        int wz = Mathf.FloorToInt(localPosition.z);
        float offsetX = seed * 0.041f + 17.3f;
        float offsetY = seed * 0.023f + 31.7f;
        float offsetZ = seed * 0.037f + 53.1f;
        float noise = Sample3DNoise(
            wx * settings.caveScale + offsetX,
            wy * settings.caveScale + offsetY,
            wz * settings.caveScale + offsetZ);
        return noise > settings.caveThreshold;
    }

    static float SampleSeededNoise(Vector3 position, float scale, float offset)
    {
        return Sample3DNoise(
            position.x * scale + offset,
            position.y * scale + offset * 1.3f,
            position.z * scale + offset * 2.1f);
    }

    static float Sample3DNoise(float x, float y, float z)
    {
        float xy = Mathf.PerlinNoise(x, y);
        float xz = Mathf.PerlinNoise(x, z);
        float yz = Mathf.PerlinNoise(y, z);
        return (xy + xz + yz) / 3f;
    }
}
