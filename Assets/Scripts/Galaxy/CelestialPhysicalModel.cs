using System;
using UnityEngine;

[Serializable]
public sealed class PlanetPhysicalProfile
{
    public const double GravitationalConstant = 6.67430e-11d;
    public const double EarthRadiusMeters = 6_371_000d;
    public const double EarthGravity = 9.80665d;

    [Min(1f)] public double radiusMeters = EarthRadiusMeters;
    [Min(1f)] public double meanDensityKgPerCubicMeter = 5514d;
    [Min(1f)] public double massKg = 5.97219e24d;
    [Min(1f)] public double gravitationalParameter = 3.986004418e14d;
    [Min(0f)] public double surfaceGravity = EarthGravity;
    [Min(1f)] public double rotationPeriodSeconds = 86164d;
    [Min(0f)] public double atmosphereSurfaceDensityKgPerCubicMeter = 1.225d;
    [Min(1f)] public double atmosphereScaleHeightMeters = 8500d;
    [Min(0f)] public double atmosphereTopAltitudeMeters = 100_000d;
    [Min(0f)] public double visualExosphereAltitudeMeters = 600_000d;

    public bool HasAtmosphere => atmosphereSurfaceDensityKgPerCubicMeter > 0.000001d
        && atmosphereScaleHeightMeters > 0d
        && atmosphereTopAltitudeMeters > 0d;

    public double GravityAtAltitude(double altitudeMeters)
    {
        double radius = Math.Max(1d, radiusMeters + altitudeMeters);
        return gravitationalParameter / (radius * radius);
    }

    public double CircularOrbitSpeed(double altitudeMeters)
    {
        double radius = Math.Max(1d, radiusMeters + altitudeMeters);
        return Math.Sqrt(gravitationalParameter / radius);
    }

    public double EscapeVelocity(double altitudeMeters)
    {
        double radius = Math.Max(1d, radiusMeters + altitudeMeters);
        return Math.Sqrt(2d * gravitationalParameter / radius);
    }

    public double AtmosphereDensityAtAltitude(double altitudeMeters)
    {
        if (!HasAtmosphere || altitudeMeters >= atmosphereTopAltitudeMeters)
            return 0d;
        return atmosphereSurfaceDensityKgPerCubicMeter
            * Math.Exp(-Math.Max(0d, altitudeMeters) / atmosphereScaleHeightMeters);
    }

    public void ClampValues()
    {
        radiusMeters = Clamp(radiusMeters, 1_500_000d, 10_000_000d);
        meanDensityKgPerCubicMeter = Clamp(meanDensityKgPerCubicMeter, 2_500d, 7_500d);
        massKg = 4d / 3d * Math.PI * radiusMeters * radiusMeters * radiusMeters
            * meanDensityKgPerCubicMeter;
        gravitationalParameter = GravitationalConstant * massKg;
        surfaceGravity = gravitationalParameter / (radiusMeters * radiusMeters);
        rotationPeriodSeconds = Clamp(rotationPeriodSeconds, 6d * 3600d, 120d * 3600d);
        atmosphereSurfaceDensityKgPerCubicMeter = Math.Max(
            0d,
            atmosphereSurfaceDensityKgPerCubicMeter);
        atmosphereScaleHeightMeters = Math.Max(1d, atmosphereScaleHeightMeters);
        atmosphereTopAltitudeMeters = HasAtmosphere
            ? Clamp(atmosphereTopAltitudeMeters, 50_000d, 600_000d)
            : 0d;
        visualExosphereAltitudeMeters = HasAtmosphere
            ? Math.Max(atmosphereTopAltitudeMeters, visualExosphereAltitudeMeters)
            : 0d;
    }

    public PlanetPhysicalProfile Clone()
    {
        var copy = (PlanetPhysicalProfile)MemberwiseClone();
        copy.ClampValues();
        return copy;
    }

    public static PlanetPhysicalProfile CreateEarthLike()
    {
        var profile = new PlanetPhysicalProfile();
        profile.ClampValues();
        return profile;
    }

    public static PlanetPhysicalProfile FromLegacy(
        float surfaceGravity,
        float rotationPeriodSeconds,
        float atmosphereDensity)
    {
        double normalizedGravity = Clamp(
            surfaceGravity / EarthGravity,
            0.15d,
            1.8d);
        double radius = EarthRadiusMeters * Math.Sqrt(normalizedGravity);
        radius = Clamp(radius, 1_500_000d, 10_000_000d);
        double density = normalizedGravity * EarthGravity * 3d
            / (4d * Math.PI * GravitationalConstant * radius);
        var profile = new PlanetPhysicalProfile
        {
            radiusMeters = radius,
            meanDensityKgPerCubicMeter = Clamp(density, 2_500d, 7_500d),
            rotationPeriodSeconds = Math.Max(6d * 3600d, rotationPeriodSeconds),
            atmosphereSurfaceDensityKgPerCubicMeter = Math.Max(0d, atmosphereDensity),
            atmosphereScaleHeightMeters = 8500d,
            atmosphereTopAltitudeMeters = atmosphereDensity > 0.0001f ? 100_000d : 0d,
            visualExosphereAltitudeMeters = atmosphereDensity > 0.0001f ? 600_000d : 0d
        };
        profile.ClampValues();
        return profile;
    }

    static double Clamp(double value, double minimum, double maximum)
        => Math.Max(minimum, Math.Min(maximum, value));
}

[Serializable]
public sealed class PlanetPresentationProfile
{
    [Min(1f)] public float surfaceProxyRadius = PlanetCelestialProfile.LargePlanetRadius;
    [Min(1f)] public float atmosphereProxyTopAltitude = 780f;
    [Min(30f)] public float visualRotationPeriodSeconds = 900f;
    [Min(1f)] public float preferredOrbitalProxyDistance = 8000f;
    [Min(0.000001f)] public float minimumProxyRadius = 0.00001f;
    [Min(1f)] public float maximumProxyRadius = 3200f;

    public void ClampValues(PlanetSurfaceGenerationMode generationMode)
    {
        surfaceProxyRadius = generationMode == PlanetSurfaceGenerationMode.StreamingLargeSphere
            ? Mathf.Clamp(surfaceProxyRadius, 1000f, 4000f)
            : PlanetCelestialProfile.CompatibleRadius;
        atmosphereProxyTopAltitude = Mathf.Max(1f, atmosphereProxyTopAltitude);
        visualRotationPeriodSeconds = Mathf.Clamp(visualRotationPeriodSeconds, 180f, 7200f);
        preferredOrbitalProxyDistance = Mathf.Clamp(preferredOrbitalProxyDistance, 1000f, 30000f);
        minimumProxyRadius = Mathf.Clamp(minimumProxyRadius, 0.000001f, 0.01f);
        maximumProxyRadius = Mathf.Max(minimumProxyRadius, maximumProxyRadius);
    }

    public PlanetPresentationProfile Clone()
        => (PlanetPresentationProfile)MemberwiseClone();
}

[Serializable]
public sealed class CelestialOrbitDefinition
{
    public double semiMajorAxisMeters = PhysicalConstants.AstronomicalUnit;
    [Range(0f, 0.35f)] public float eccentricity;
    [Range(-30f, 30f)] public float inclinationDegrees;
    [Range(0f, 360f)] public float longitudeAscendingNodeDegrees;
    [Range(0f, 360f)] public float argumentOfPeriapsisDegrees;
    [Range(0f, 360f)] public float meanAnomalyAtEpochDegrees;
    public double epochSeconds;
}

[Serializable]
public sealed class StellarSystemDefinition
{
    public string systemId;
    public InterstellarCoordinate systemCoordinate;
    public double stellarMassKg = PhysicalConstants.SolarMass;
    public double stellarRadiusMeters = PhysicalConstants.SolarRadius;
    public double luminositySolar = 1d;
    public DoubleVector3 barycenterOffsetMeters;
}

public struct CelestialBodyState
{
    public UniversePosition universePosition;
    public DoubleVector3 positionMeters;
    public DoubleVector3 velocityMetersPerSecond;
}

public static class CelestialEphemeris
{
    public static CelestialBodyState GetBodyState(
        string bodyId,
        double universeTimeSeconds)
    {
        GalaxyTravelManager manager = GalaxyTravelManager.Instance;
        return manager != null
            && manager.TryGetCelestialBodyState(
                bodyId,
                universeTimeSeconds,
                out CelestialBodyState state)
            ? state
            : default;
    }

    public static CelestialBodyState GetBodyState(
        CelestialOrbitDefinition orbit,
        double primaryMassKg,
        double universeTimeSeconds)
    {
        if (orbit == null)
            return default;

        double semiMajorAxis = Math.Max(1d, orbit.semiMajorAxisMeters);
        double eccentricity = Math.Max(0d, Math.Min(0.35d, orbit.eccentricity));
        double mu = PlanetPhysicalProfile.GravitationalConstant * Math.Max(1d, primaryMassKg);
        double meanMotion = Math.Sqrt(mu / (semiMajorAxis * semiMajorAxis * semiMajorAxis));
        double meanAnomaly = DegreesToRadians(orbit.meanAnomalyAtEpochDegrees)
            + meanMotion * (universeTimeSeconds - orbit.epochSeconds);
        meanAnomaly = RepeatRadians(meanAnomaly);

        double eccentricAnomaly = meanAnomaly;
        for (int iteration = 0; iteration < 8; iteration++)
        {
            double denominator = Math.Max(0.000001d, 1d - eccentricity * Math.Cos(eccentricAnomaly));
            eccentricAnomaly -= (eccentricAnomaly - eccentricity * Math.Sin(eccentricAnomaly) - meanAnomaly)
                / denominator;
        }

        double x = semiMajorAxis * (Math.Cos(eccentricAnomaly) - eccentricity);
        double z = semiMajorAxis * Math.Sqrt(1d - eccentricity * eccentricity)
            * Math.Sin(eccentricAnomaly);
        double denominatorVelocity = Math.Max(
            0.000001d,
            1d - eccentricity * Math.Cos(eccentricAnomaly));
        double vx = -semiMajorAxis * meanMotion * Math.Sin(eccentricAnomaly)
            / denominatorVelocity;
        double vz = semiMajorAxis * meanMotion * Math.Sqrt(1d - eccentricity * eccentricity)
            * Math.Cos(eccentricAnomaly) / denominatorVelocity;

        DoubleVector3 position = RotateOrbitalVector(
            new DoubleVector3(x, 0d, z),
            orbit.argumentOfPeriapsisDegrees,
            orbit.inclinationDegrees,
            orbit.longitudeAscendingNodeDegrees);
        DoubleVector3 velocity = RotateOrbitalVector(
            new DoubleVector3(vx, 0d, vz),
            orbit.argumentOfPeriapsisDegrees,
            orbit.inclinationDegrees,
            orbit.longitudeAscendingNodeDegrees);
        return new CelestialBodyState
        {
            positionMeters = position,
            velocityMetersPerSecond = velocity
        };
    }

    static DoubleVector3 RotateOrbitalVector(
        DoubleVector3 value,
        double argumentOfPeriapsisDegrees,
        double inclinationDegrees,
        double longitudeAscendingNodeDegrees)
    {
        value = RotateAroundY(value, DegreesToRadians(argumentOfPeriapsisDegrees));
        value = RotateAroundX(value, DegreesToRadians(inclinationDegrees));
        return RotateAroundY(value, DegreesToRadians(longitudeAscendingNodeDegrees));
    }

    static DoubleVector3 RotateAroundX(DoubleVector3 value, double radians)
    {
        double cosine = Math.Cos(radians);
        double sine = Math.Sin(radians);
        return new DoubleVector3(
            value.x,
            value.y * cosine - value.z * sine,
            value.y * sine + value.z * cosine);
    }

    static DoubleVector3 RotateAroundY(DoubleVector3 value, double radians)
    {
        double cosine = Math.Cos(radians);
        double sine = Math.Sin(radians);
        return new DoubleVector3(
            value.x * cosine + value.z * sine,
            value.y,
            -value.x * sine + value.z * cosine);
    }

    static double DegreesToRadians(double degrees) => degrees * Math.PI / 180d;

    static double RepeatRadians(double radians)
    {
        double result = radians % (Math.PI * 2d);
        return result < 0d ? result + Math.PI * 2d : result;
    }
}

[Serializable]
public struct UniversePosition : IEquatable<UniversePosition>
{
    public InterstellarCoordinate systemCoordinate;
    public DoubleVector3 localMeters;

    public UniversePosition(InterstellarCoordinate systemCoordinate, DoubleVector3 localMeters)
    {
        this.systemCoordinate = systemCoordinate;
        this.localMeters = localMeters;
    }

    public bool Equals(UniversePosition other)
        => systemCoordinate == other.systemCoordinate
        && localMeters.x.Equals(other.localMeters.x)
        && localMeters.y.Equals(other.localMeters.y)
        && localMeters.z.Equals(other.localMeters.z);

    public override bool Equals(object value)
        => value is UniversePosition other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = systemCoordinate.GetHashCode();
            hash = hash * 397 ^ localMeters.x.GetHashCode();
            hash = hash * 397 ^ localMeters.y.GetHashCode();
            return hash * 397 ^ localMeters.z.GetHashCode();
        }
    }

    public DoubleVector3 ToAbsoluteMeters()
    {
        double spacing = ProceduralInterstellarGenerator.SystemSpacingMeters;
        return new DoubleVector3(
            systemCoordinate.x * spacing + localMeters.x,
            systemCoordinate.y * spacing + localMeters.y,
            systemCoordinate.z * spacing + localMeters.z);
    }

    public static UniversePosition FromAbsoluteMeters(DoubleVector3 value)
    {
        double spacing = ProceduralInterstellarGenerator.SystemSpacingMeters;
        long x = FloorToLong((value.x + spacing * 0.5d) / spacing);
        long y = FloorToLong((value.y + spacing * 0.5d) / spacing);
        long z = FloorToLong((value.z + spacing * 0.5d) / spacing);
        return new UniversePosition(
            new InterstellarCoordinate(x, y, z),
            new DoubleVector3(
                value.x - x * spacing,
                value.y - y * spacing,
                value.z - z * spacing));
    }

    public UniversePosition Add(DoubleVector3 delta)
    {
        DoubleVector3 absoluteLocal = localMeters + delta;
        double spacing = ProceduralInterstellarGenerator.SystemSpacingMeters;
        long dx = FloorToLong((absoluteLocal.x + spacing * 0.5d) / spacing);
        long dy = FloorToLong((absoluteLocal.y + spacing * 0.5d) / spacing);
        long dz = FloorToLong((absoluteLocal.z + spacing * 0.5d) / spacing);
        return new UniversePosition(
            new InterstellarCoordinate(
                systemCoordinate.x + dx,
                systemCoordinate.y + dy,
                systemCoordinate.z + dz),
            new DoubleVector3(
                absoluteLocal.x - dx * spacing,
                absoluteLocal.y - dy * spacing,
                absoluteLocal.z - dz * spacing));
    }

    public static DoubleVector3 Delta(UniversePosition from, UniversePosition to)
    {
        double spacing = ProceduralInterstellarGenerator.SystemSpacingMeters;
        return new DoubleVector3(
            ((double)to.systemCoordinate.x - from.systemCoordinate.x) * spacing
                + to.localMeters.x - from.localMeters.x,
            ((double)to.systemCoordinate.y - from.systemCoordinate.y) * spacing
                + to.localMeters.y - from.localMeters.y,
            ((double)to.systemCoordinate.z - from.systemCoordinate.z) * spacing
                + to.localMeters.z - from.localMeters.z);
    }

    public static double Distance(UniversePosition left, UniversePosition right)
    {
        DoubleVector3 delta = Delta(left, right);
        return Math.Sqrt(delta.x * delta.x + delta.y * delta.y + delta.z * delta.z);
    }

    static long FloorToLong(double value)
    {
        if (value >= long.MaxValue)
            return long.MaxValue;
        if (value <= long.MinValue)
            return long.MinValue;
        return (long)Math.Floor(value);
    }
}

public static class PlanetScaleMapping
{
    public static double PhysicalAltitudeFromPresentation(
        PlanetCelestialProfile profile,
        float presentationRadius)
    {
        if (profile == null)
            return presentationRadius;
        return presentationRadius - profile.radius;
    }

    public static float CalculateProxyRadius(
        PlanetCelestialProfile profile,
        double physicalCenterDistance,
        float presentationDistance)
    {
        if (profile == null)
            return 1f;
        PlanetPhysicalProfile physical = profile.Physical;
        PlanetPresentationProfile presentation = profile.Presentation;
        double radius = Math.Max(1d, physical.radiusMeters);
        double distance = Math.Max(radius + 1d, physicalCenterDistance);
        double angularRadius = Math.Asin(Math.Min(0.999999d, radius / distance));
        float proxyRadius = (float)(Math.Tan(angularRadius) * Math.Max(1f, presentationDistance));
        return Mathf.Clamp(
            proxyRadius,
            presentation.minimumProxyRadius,
            presentation.maximumProxyRadius);
    }
}
