using System;
using UnityEngine;

[Serializable]
public sealed class PlanetCelestialProfile
{
    public const float CompatibleRadius = 100f;

    public float radius = CompatibleRadius;
    public float surfaceGravity = 9.8f;
    public float gravitationalParameter = 98000f;
    public Vector3 rotationAxis = Vector3.up;
    public float rotationPeriod = 180f;
    public float atmosphereSurfaceDensity = 0.8f;
    public float atmosphereScaleHeight = 4.5f;
    public float atmosphereTopAltitude = 20f;

    public bool HasAtmosphere => atmosphereSurfaceDensity > 0.0001f
        && atmosphereScaleHeight > 0.01f
        && atmosphereTopAltitude > 0.01f;

    public void ClampValues()
    {
        // The voxel address space is radius-dependent, so version 1 keeps the
        // established 100 m radius for every old and newly generated planet.
        radius = CompatibleRadius;
        surfaceGravity = Mathf.Clamp(surfaceGravity, 0.5f, 20f);
        gravitationalParameter = surfaceGravity * radius * radius;
        rotationAxis = rotationAxis.sqrMagnitude > 0.0001f ? rotationAxis.normalized : Vector3.up;
        rotationPeriod = Mathf.Max(30f, rotationPeriod);
        atmosphereSurfaceDensity = Mathf.Max(0f, atmosphereSurfaceDensity);
        atmosphereScaleHeight = Mathf.Max(0.1f, atmosphereScaleHeight);
        atmosphereTopAltitude = atmosphereSurfaceDensity > 0.0001f
            ? Mathf.Max(1f, atmosphereTopAltitude)
            : 0f;
    }

    public PlanetCelestialProfile Clone()
    {
        var copy = (PlanetCelestialProfile)MemberwiseClone();
        copy.ClampValues();
        return copy;
    }

    public static PlanetCelestialProfile CreateCompatibleDefault()
    {
        var profile = new PlanetCelestialProfile();
        profile.ClampValues();
        return profile;
    }
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
