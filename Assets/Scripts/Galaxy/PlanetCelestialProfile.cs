using System;
using UnityEngine;

public enum PlanetSurfaceGenerationMode
{
    LegacyFullSphere = 0,
    StreamingLargeSphere = 1
}

public enum PlanetAtmosphereKind
{
    None = 0,
    Thin = 1,
    Temperate = 2,
    Dense = 3,
    Dusty = 4,
    Crystal = 5
}

[Serializable]
public sealed class AtmosphereVisualProfile
{
    public PlanetAtmosphereKind kind = PlanetAtmosphereKind.Temperate;
    public Color horizonColor = new Color(0.24f, 0.58f, 1f, 1f);
    public Color zenithColor = new Color(0.035f, 0.14f, 0.32f, 1f);
    public Color sunsetColor = new Color(1f, 0.32f, 0.08f, 1f);
    [Range(0f, 2f)] public float scatteringStrength = 0.8f;
    [Range(0f, 1f)] public float cloudCoverage = 0.45f;
    [Range(0f, 2f)] public float cloudRotationMultiplier = 1.08f;

    public AtmosphereVisualProfile Clone() => (AtmosphereVisualProfile)MemberwiseClone();
}

[Serializable]
public sealed class PlanetCelestialProfile
{
    public const float CompatibleRadius = 100f;
    public const float LargePlanetRadius = 2000f;

    public PlanetSurfaceGenerationMode surfaceGenerationMode = PlanetSurfaceGenerationMode.LegacyFullSphere;
    public float radius = CompatibleRadius;
    public float surfaceGravity = 9.8f;
    public float gravitationalParameter = 98000f;
    public Vector3 rotationAxis = Vector3.up;
    public float rotationPeriod = 180f;
    public float atmosphereSurfaceDensity = 0.8f;
    public float atmosphereScaleHeight = 4.5f;
    public float atmosphereTopAltitude = 20f;
    public float maximumTerrainElevation = 12f;
    public float editableDepth = 64f;
    public double rotationEpochSeconds;
    public AtmosphereVisualProfile atmosphereVisual = new AtmosphereVisualProfile();

    public bool HasAtmosphere => atmosphereSurfaceDensity > 0.0001f
        && atmosphereScaleHeight > 0.01f
        && atmosphereTopAltitude > 0.01f;

    public void ClampValues()
    {
        radius = surfaceGenerationMode == PlanetSurfaceGenerationMode.StreamingLargeSphere
            ? Mathf.Clamp(radius, 1000f, 4000f)
            : CompatibleRadius;
        surfaceGravity = Mathf.Clamp(surfaceGravity, 0.5f, 20f);
        gravitationalParameter = surfaceGravity * radius * radius;
        rotationAxis = rotationAxis.sqrMagnitude > 0.0001f ? rotationAxis.normalized : Vector3.up;
        rotationPeriod = surfaceGenerationMode == PlanetSurfaceGenerationMode.StreamingLargeSphere
            ? Mathf.Clamp(rotationPeriod, 600f, 1200f)
            : Mathf.Max(30f, rotationPeriod);
        atmosphereSurfaceDensity = Mathf.Max(0f, atmosphereSurfaceDensity);
        atmosphereScaleHeight = Mathf.Max(0.1f, atmosphereScaleHeight);
        atmosphereTopAltitude = atmosphereSurfaceDensity > 0.0001f
            ? Mathf.Max(1f, atmosphereTopAltitude)
            : 0f;
        if (surfaceGenerationMode == PlanetSurfaceGenerationMode.StreamingLargeSphere && HasAtmosphere)
        {
            // The 2 km planet uses a compressed atmosphere, but it must still cover the
            // 350-500 m terrain-cruise band used by the approach scene.
            atmosphereScaleHeight = Mathf.Max(90f, atmosphereScaleHeight);
            atmosphereTopAltitude = Mathf.Max(700f, atmosphereTopAltitude);
        }
        maximumTerrainElevation = Mathf.Max(2f, maximumTerrainElevation);
        editableDepth = Mathf.Max(16f, editableDepth);
        atmosphereVisual = atmosphereVisual ?? new AtmosphereVisualProfile();
        if (!HasAtmosphere)
            atmosphereVisual.kind = PlanetAtmosphereKind.None;
    }

    public PlanetCelestialProfile Clone()
    {
        var copy = (PlanetCelestialProfile)MemberwiseClone();
        copy.atmosphereVisual = atmosphereVisual != null ? atmosphereVisual.Clone() : new AtmosphereVisualProfile();
        copy.ClampValues();
        return copy;
    }

    public static PlanetCelestialProfile CreateCompatibleDefault()
    {
        var profile = new PlanetCelestialProfile
        {
            surfaceGenerationMode = PlanetSurfaceGenerationMode.LegacyFullSphere,
            radius = CompatibleRadius
        };
        profile.ClampValues();
        return profile;
    }

    public static PlanetCelestialProfile CreateLargeDefault()
    {
        var profile = new PlanetCelestialProfile
        {
            surfaceGenerationMode = PlanetSurfaceGenerationMode.StreamingLargeSphere,
            radius = LargePlanetRadius,
            surfaceGravity = 6.2f,
            rotationPeriod = 900f,
            atmosphereSurfaceDensity = 0.72f,
            atmosphereScaleHeight = 120f,
            atmosphereTopAltitude = 780f,
            maximumTerrainElevation = 160f,
            editableDepth = 96f
        };
        profile.ClampValues();
        return profile;
    }
}

public static class PlanetReferenceFrame
{
    public static Vector3 AngularVelocity(PlanetCelestialProfile profile)
    {
        if (profile == null)
            return Vector3.zero;
        return profile.rotationAxis.normalized
            * (Mathf.PI * 2f / Mathf.Max(1f, profile.rotationPeriod));
    }

    public static Quaternion RotationAtTime(PlanetCelestialProfile profile, double universeTimeSeconds)
    {
        if (profile == null)
            return Quaternion.identity;
        double phaseSeconds = universeTimeSeconds - profile.rotationEpochSeconds;
        float degrees = (float)(phaseSeconds / Math.Max(1d, profile.rotationPeriod) * 360d % 360d);
        return Quaternion.AngleAxis(degrees, profile.rotationAxis);
    }

    public static Vector3 ToBodyVelocity(Vector3 position, Vector3 inertialVelocity, Vector3 angularVelocity)
        => inertialVelocity - Vector3.Cross(angularVelocity, position);

    public static Vector3 ToInertialVelocity(Vector3 position, Vector3 bodyVelocity, Vector3 angularVelocity)
        => bodyVelocity + Vector3.Cross(angularVelocity, position);
}

[Serializable]
public sealed class VisitedPlanetRecord
{
    public string planetId;
    public string displayName;
    public long coordinateX;
    public long coordinateY;
    public long coordinateZ;
    public Vector3 lastLandingDirection = Vector3.up;
    public Vector3 lastPlayerLocalPosition;
    public long lastVisitedUtcTicks;

    public InterstellarCoordinate Coordinate => new InterstellarCoordinate(
        coordinateX,
        coordinateY,
        coordinateZ);
}
