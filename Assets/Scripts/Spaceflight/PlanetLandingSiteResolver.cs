using UnityEngine;

public enum PlanetLandingMode
{
    Auto,
    Terrain,
    OceanPlatform
}

public readonly struct PlanetLandingResolution
{
    public readonly PlanetLandingMode mode;
    public readonly Vector3 direction;
    public readonly float oceanRadius;
    public readonly float terrainRadius;
    public readonly float maximumVisualWaveHeight;

    public PlanetLandingResolution(
        PlanetLandingMode landingMode,
        Vector3 landingDirection,
        float meanOceanRadius,
        float sampledTerrainRadius,
        float maximumWaveHeight)
    {
        mode = landingMode;
        direction = landingDirection;
        oceanRadius = meanOceanRadius;
        terrainRadius = sampledTerrainRadius;
        maximumVisualWaveHeight = maximumWaveHeight;
    }
}

public static class PlanetLandingSiteResolver
{
    const float DefaultFootprintRadius = 12f;
    const int FootprintSamples = 8;
    const float LoadedWaterTolerance = 0.15f;

    public static PlanetLandingResolution Resolve(
        GalaxyPlanetDefinition planet,
        Vector3 requestedDirection,
        float footprintRadius = DefaultFootprintRadius)
    {
        Vector3 direction = requestedDirection.sqrMagnitude > 0.001f
            ? requestedDirection.normalized
            : Vector3.up;
        if (planet == null)
            return new PlanetLandingResolution(
                PlanetLandingMode.Terrain,
                direction,
                0f,
                0f,
                0f);

        PlanetCelestialProfile celestial = (planet.celestial
            ?? PlanetCelestialProfile.CreateCompatibleDefault()).Clone();
        PlanetLowPolyVisualProfile visual = planet.lowPolyVisual != null
            ? planet.lowPolyVisual.Clone()
            : null;
        if (visual == null)
            return TerrainResult(planet, celestial, direction, 0f, 0f);
        visual.ClampValues();

        float oceanRadius = ProceduralPlanetOcean.CalculateOceanRadius(
            celestial,
            visual);
        float maximumWaveHeight = ProceduralPlanetOcean.CalculateMaximumWaveHeight(
            celestial,
            visual);
        if (!visual.oceanEnabled)
            return TerrainResult(
                planet,
                celestial,
                direction,
                oceanRadius,
                maximumWaveHeight);

        float centerTerrainRadius = GetTerrainRadius(planet, celestial, direction);
        if (centerTerrainRadius <= oceanRadius + 0.05f)
        {
            return new PlanetLandingResolution(
                PlanetLandingMode.OceanPlatform,
                direction,
                oceanRadius,
                centerTerrainRadius,
                maximumWaveHeight);
        }

        Vector3 tangent = Vector3.Cross(
            direction,
            Mathf.Abs(Vector3.Dot(direction, Vector3.up)) < 0.9f
                ? Vector3.up
                : Vector3.right).normalized;
        Vector3 bitangent = Vector3.Cross(direction, tangent).normalized;
        float angle = Mathf.Clamp(
            Mathf.Max(0f, footprintRadius) / Mathf.Max(1f, oceanRadius),
            0f,
            0.2f);
        float sin = Mathf.Sin(angle);
        float cos = Mathf.Cos(angle);
        for (int i = 0; i < FootprintSamples; i++)
        {
            float phase = i * Mathf.PI * 2f / FootprintSamples;
            Vector3 ringDirection = (
                direction * cos
                + (tangent * Mathf.Cos(phase) + bitangent * Mathf.Sin(phase)) * sin
            ).normalized;
            float radius = GetTerrainRadius(planet, celestial, ringDirection);
            if (radius <= oceanRadius + 0.05f)
            {
                return new PlanetLandingResolution(
                    PlanetLandingMode.OceanPlatform,
                    direction,
                    oceanRadius,
                    Mathf.Min(centerTerrainRadius, radius),
                    maximumWaveHeight);
            }
        }

        return new PlanetLandingResolution(
            PlanetLandingMode.Terrain,
            direction,
            oceanRadius,
            centerTerrainRadius,
            maximumWaveHeight);
    }

    public static PlanetLandingResolution ResolveLoadedSurface(
        VoxelQuadSphereWorld world,
        GalaxyPlanetDefinition planet,
        Vector3 requestedDirection,
        Bounds shipBounds,
        PlanetLandingMode requestedMode)
    {
        Vector3 direction = requestedDirection.sqrMagnitude > 0.001f
            ? requestedDirection.normalized
            : Vector3.up;
        float footprintRadius = CalculateFootprintRadius(
            shipBounds,
            direction);
        PlanetLandingResolution procedural = Resolve(
            planet,
            direction,
            footprintRadius);

        if (requestedMode == PlanetLandingMode.OceanPlatform)
        {
            return new PlanetLandingResolution(
                PlanetLandingMode.OceanPlatform,
                direction,
                procedural.oceanRadius,
                procedural.terrainRadius,
                procedural.maximumVisualWaveHeight);
        }

        float minimumTerrainRadius = 0f;
        bool loadedFootprintTouchesOcean = IsLoadedFootprintInOcean(
                world,
                direction,
                footprintRadius,
                procedural.oceanRadius,
                out minimumTerrainRadius);
        if (procedural.mode == PlanetLandingMode.OceanPlatform
            || loadedFootprintTouchesOcean)
        {
            return new PlanetLandingResolution(
                PlanetLandingMode.OceanPlatform,
                direction,
                procedural.oceanRadius,
                minimumTerrainRadius > 0f
                    ? minimumTerrainRadius
                    : procedural.terrainRadius,
                procedural.maximumVisualWaveHeight);
        }

        return new PlanetLandingResolution(
            PlanetLandingMode.Terrain,
            direction,
            procedural.oceanRadius,
            procedural.terrainRadius,
            procedural.maximumVisualWaveHeight);
    }

    public static float CalculateFootprintRadius(
        Bounds shipBounds,
        Vector3 radialUp)
    {
        radialUp = radialUp.sqrMagnitude > 0.001f
            ? radialUp.normalized
            : Vector3.up;
        Vector3 extents = shipBounds.extents;
        float radius = 0f;
        for (int x = -1; x <= 1; x += 2)
        for (int y = -1; y <= 1; y += 2)
        for (int z = -1; z <= 1; z += 2)
        {
            Vector3 cornerOffset = Vector3.Scale(
                extents,
                new Vector3(x, y, z));
            radius = Mathf.Max(
                radius,
                Vector3.ProjectOnPlane(cornerOffset, radialUp).magnitude);
        }
        return Mathf.Max(DefaultFootprintRadius, radius + 4f);
    }

    static bool IsLoadedFootprintInOcean(
        VoxelQuadSphereWorld world,
        Vector3 direction,
        float footprintRadius,
        float oceanRadius,
        out float minimumTerrainRadius)
    {
        minimumTerrainRadius = float.PositiveInfinity;
        if (world == null || oceanRadius <= 0f)
            return false;

        Vector3 center = world.GetPlanetCenterWorld();
        Vector3 tangent = Vector3.Cross(
            direction,
            Mathf.Abs(Vector3.Dot(direction, Vector3.up)) < 0.9f
                ? Vector3.up
                : Vector3.right).normalized;
        Vector3 bitangent = Vector3.Cross(direction, tangent).normalized;
        float angle = Mathf.Clamp(
            Mathf.Max(0f, footprintRadius)
                / Mathf.Max(1f, world.PlanetRadius),
            0f,
            0.2f);

        for (int sampleIndex = -1;
             sampleIndex < FootprintSamples;
             sampleIndex++)
        {
            Vector3 sampleDirection;
            if (sampleIndex < 0)
            {
                sampleDirection = direction;
            }
            else
            {
                float phase =
                    sampleIndex * Mathf.PI * 2f / FootprintSamples;
                sampleDirection = (
                    direction * Mathf.Cos(angle)
                    + (tangent * Mathf.Cos(phase)
                        + bitangent * Mathf.Sin(phase))
                    * Mathf.Sin(angle)).normalized;
            }

            float terrainRadius = world.GetProceduralSurfaceRadius(
                sampleDirection);
            if (world.TryFindLoadedSurfacePoseNearDirection(
                sampleDirection,
                out VoxelTerrainSurfacePose surface))
            {
                terrainRadius = Vector3.Distance(surface.Point, center);
                Vector3 probe = surface.Point
                    + sampleDirection * LoadedWaterTolerance;
                if (PlanetWaterRegistry.TrySampleAny(
                    probe,
                    out WaterSample water)
                    && water.kind == PlanetWaterKind.Ocean
                    && water.signedDistance <= LoadedWaterTolerance)
                {
                    minimumTerrainRadius = Mathf.Min(
                        minimumTerrainRadius,
                        terrainRadius);
                    return true;
                }
            }

            minimumTerrainRadius = Mathf.Min(
                minimumTerrainRadius,
                terrainRadius);
            if (terrainRadius <= oceanRadius + LoadedWaterTolerance)
                return true;
        }

        if (!float.IsFinite(minimumTerrainRadius))
            minimumTerrainRadius = 0f;
        return false;
    }

    static PlanetLandingResolution TerrainResult(
        GalaxyPlanetDefinition planet,
        PlanetCelestialProfile celestial,
        Vector3 direction,
        float oceanRadius,
        float maximumWaveHeight)
    {
        return new PlanetLandingResolution(
            PlanetLandingMode.Terrain,
            direction,
            oceanRadius,
            GetTerrainRadius(planet, celestial, direction),
            maximumWaveHeight);
    }

    static float GetTerrainRadius(
        GalaxyPlanetDefinition planet,
        PlanetCelestialProfile celestial,
        Vector3 direction)
    {
        return celestial.radius + VoxelQuadSphereTerrain.GetSurfaceNoise(
            direction * celestial.radius,
            planet.seed,
            planet.terrain ?? new PlanetTerrainSettings());
    }
}
