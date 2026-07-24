using System;
using UnityEngine;

[Serializable]
public sealed class PlanetTerrainSettings
{
    public const int CurrentShapeVersion = 2;

    [HideInInspector] public int shapeVersion = CurrentShapeVersion;

    [Header("Surface Shape")]
    [Min(0.001f)] public float continentScale = 0.018f;
    [Min(0f)] public float continentHeight = 5f;
    [Min(0.001f)] public float detailScale = 0.065f;
    [Min(0f)] public float detailHeight = 2.5f;
    [Min(0f)] public float ridgeHeight = 1f;
    [Range(0.35f, 0.68f)] public float continentThreshold = 0.5f;
    [Range(0f, 1.5f)] public float continentWarp = 0.55f;
    [Range(0.45f, 2.5f)] public float continentSharpness = 1.2f;
    [Range(0f, 1f)] public float mountainMask = 0.5f;
    [Range(0.1f, 1.25f)] public float oceanFloorDepth = 0.55f;
    [Range(0f, 0.75f)] public float terraceStrength;

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
            shapeVersion = 1,
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
        shapeVersion = Mathf.Max(1, shapeVersion);
        continentScale = Mathf.Max(0.001f, continentScale);
        continentHeight = Mathf.Max(0f, continentHeight);
        detailScale = Mathf.Max(0.001f, detailScale);
        detailHeight = Mathf.Max(0f, detailHeight);
        ridgeHeight = Mathf.Max(0f, ridgeHeight);
        continentThreshold = Mathf.Clamp(continentThreshold, 0.35f, 0.68f);
        continentWarp = Mathf.Clamp(continentWarp, 0f, 1.5f);
        continentSharpness = Mathf.Clamp(continentSharpness, 0.45f, 2.5f);
        mountainMask = Mathf.Clamp01(mountainMask);
        oceanFloorDepth = Mathf.Clamp(oceanFloorDepth, 0.1f, 1.25f);
        terraceStrength = Mathf.Clamp(terraceStrength, 0f, 0.75f);
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
        float voxelOuterRadius,
        float surfaceReferenceRadius,
        PlanetTerrainSettings settings,
        float riverCarveDepth = 0f)
    {
        if (cellU < 0 || cellV < 0 || cellU >= gridSize || cellV >= gridSize || depth < 0 || depth >= maxDepth)
            return VoxelTypes.Air;

        settings = settings ?? PlanetTerrainSettings.CreateLegacy();
        Vector3 center = VoxelQuadSphereMapping.FaceCellCenterLocal(
            face, cellU, cellV, depth, gridSize, voxelOuterRadius, planetCenter);
        Vector3 radial = center - planetCenter;
        radial = radial.sqrMagnitude > 0.0001f ? radial.normalized : Vector3.up;
        float surfaceNoise = GetSurfaceNoise(
            radial * surfaceReferenceRadius,
            seed,
            settings);
        return GenerateVoxelWithSurfaceNoise(
            face,
            cellU,
            cellV,
            depth,
            gridSize,
            maxDepth,
            innerSolidDepthLayers,
            seed,
            planetCenter,
            voxelOuterRadius,
            surfaceReferenceRadius,
            settings,
            surfaceNoise,
            riverCarveDepth);
    }

    public static byte GenerateVoxelWithSurfaceNoise(
        QuadSphereFace face,
        int cellU,
        int cellV,
        int depth,
        int gridSize,
        int maxDepth,
        int innerSolidDepthLayers,
        int seed,
        Vector3 planetCenter,
        float voxelOuterRadius,
        float surfaceReferenceRadius,
        PlanetTerrainSettings settings,
        float surfaceNoise,
        float riverCarveDepth = 0f)
    {
        if (cellU < 0 || cellV < 0 || cellU >= gridSize || cellV >= gridSize
            || depth < 0 || depth >= maxDepth)
        {
            return VoxelTypes.Air;
        }

        settings = settings ?? PlanetTerrainSettings.CreateLegacy();
        Vector3 center = VoxelQuadSphereMapping.FaceCellCenterLocal(
            face, cellU, cellV, depth, gridSize, voxelOuterRadius, planetCenter);
        float radialDistance = Vector3.Distance(center, planetCenter);
        float surfaceRadius = surfaceReferenceRadius + surfaceNoise - Mathf.Max(0f, riverCarveDepth);
        float depthFromSurface = surfaceRadius - radialDistance;
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
        settings = settings ?? new PlanetTerrainSettings();
        if (settings.shapeVersion < 2)
            return GetLegacySurfaceNoise(localPosition, seed, settings);

        // The same continuous 3D shape function is sampled by both the whole-planet
        // visual mesh and the local voxel shell. This keeps LOD transitions aligned
        // and avoids cube-face seams.
        Vector3 continentPoint = localPosition * settings.continentScale;
        Vector3 warp = new Vector3(
            FractalNoise(continentPoint * 0.58f + new Vector3(19.1f, 3.7f, 7.3f), seed + 101, 3),
            FractalNoise(continentPoint * 0.58f + new Vector3(5.3f, 29.7f, 11.9f), seed + 211, 3),
            FractalNoise(continentPoint * 0.58f + new Vector3(13.7f, 17.1f, 37.3f), seed + 307, 3));
        continentPoint += (warp * 2f - Vector3.one) * settings.continentWarp;

        float continentNoise = FractalNoise(continentPoint, seed + 401, 5);
        float signedContinent = (continentNoise - settings.continentThreshold) * 2.45f;
        float landMask = Smooth01(Mathf.InverseLerp(-0.06f, 0.18f, signedContinent));
        float land = Mathf.Pow(Mathf.Max(0f, signedContinent), settings.continentSharpness)
            * settings.continentHeight;
        float ocean = -Mathf.Pow(Mathf.Max(0f, -signedContinent), 0.72f)
            * settings.continentHeight
            * settings.oceanFloorDepth;

        Vector3 detailPoint = localPosition * settings.detailScale;
        float detail = (FractalNoise(detailPoint, seed + 809, 3) - 0.5f)
            * settings.detailHeight
            * Mathf.Lerp(0.22f, 1f, landMask);

        float ridgeSource = FractalNoise(
            detailPoint * 0.36f + continentPoint * 0.18f,
            seed + 1201,
            4);
        float ridge = 1f - Mathf.Abs(ridgeSource * 2f - 1f);
        ridge = ridge * ridge * ridge;
        float highlandMask = Smooth01(Mathf.InverseLerp(
            settings.mountainMask - 0.22f,
            settings.mountainMask + 0.18f,
            continentNoise));
        float mountains = ridge * settings.ridgeHeight * landMask * highlandMask;

        float height = ocean + land + detail + mountains;
        if (settings.terraceStrength > 0.001f && height > 0f)
        {
            float terraceSize = Mathf.Max(0.75f, settings.detailHeight * 0.22f);
            float terraced = Mathf.Round(height / terraceSize) * terraceSize;
            height = Mathf.Lerp(height, terraced, settings.terraceStrength);
        }

        float maximumHeight = settings.continentHeight * 0.82f
            + settings.detailHeight * 0.45f
            + settings.ridgeHeight * 0.8f;
        float maximumDepth = settings.continentHeight * settings.oceanFloorDepth * 0.72f;
        return Mathf.Clamp(height, -maximumDepth, maximumHeight);
    }

    static float GetLegacySurfaceNoise(Vector3 localPosition, int seed, PlanetTerrainSettings settings)
    {
        float offset = seed * 0.017f;
        float continent = SampleSeededNoise(localPosition, settings.continentScale, offset);
        float detail = SampleSeededNoise(localPosition, settings.detailScale, offset + 79.31f);
        float ridgeNoise = SampleSeededNoise(localPosition, settings.detailScale * 0.55f, offset + 157.7f);
        float ridge = 1f - Mathf.Abs(ridgeNoise * 2f - 1f);

        return (continent - 0.5f) * settings.continentHeight
            + (detail - 0.5f) * settings.detailHeight
            + (ridge * ridge - 0.35f) * settings.ridgeHeight;}

    static float FractalNoise(Vector3 point, int seed, int octaves)
    {
        float amplitude = 1f;
        float frequency = 1f;
        float sum = 0f;
        float normalization = 0f;
        for (int octave = 0; octave < octaves; octave++)
        {
            sum += ValueNoise(point * frequency, seed + octave * 1619) * amplitude;
            normalization += amplitude;
            frequency *= 2.03f;
            amplitude *= 0.51f;
        }
        return normalization > 0f ? sum / normalization : 0.5f;
    }

    static float ValueNoise(Vector3 point, int seed)
    {
        int x0 = Mathf.FloorToInt(point.x);
        int y0 = Mathf.FloorToInt(point.y);
        int z0 = Mathf.FloorToInt(point.z);
        float tx = Smooth01(point.x - x0);
        float ty = Smooth01(point.y - y0);
        float tz = Smooth01(point.z - z0);

        float x00 = Mathf.Lerp(Hash01(x0, y0, z0, seed), Hash01(x0 + 1, y0, z0, seed), tx);
        float x10 = Mathf.Lerp(Hash01(x0, y0 + 1, z0, seed), Hash01(x0 + 1, y0 + 1, z0, seed), tx);
        float x01 = Mathf.Lerp(Hash01(x0, y0, z0 + 1, seed), Hash01(x0 + 1, y0, z0 + 1, seed), tx);
        float x11 = Mathf.Lerp(Hash01(x0, y0 + 1, z0 + 1, seed), Hash01(x0 + 1, y0 + 1, z0 + 1, seed), tx);
        return Mathf.Lerp(Mathf.Lerp(x00, x10, ty), Mathf.Lerp(x01, x11, ty), tz);
    }

    static float Hash01(int x, int y, int z, int seed)
    {
        unchecked
        {
            uint hash = (uint)x * 374761393u
                + (uint)y * 668265263u
                + (uint)z * 2246822519u
                + (uint)seed * 3266489917u;
            hash = (hash ^ (hash >> 13)) * 1274126177u;
            hash ^= hash >> 16;
            return (hash & 0x00ffffffu) / 16777215f;
        }
    }

    static float Smooth01(float value)
    {
        value = Mathf.Clamp01(value);
        return value * value * (3f - 2f * value);
    }

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
