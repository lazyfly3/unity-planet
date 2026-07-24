using System;

public enum SpaceflightVelocityReference
{
    SystemBarycentric,
    PlanetInertial,
    AtmosphereRelative,
    TargetRelative
}

public static class SpaceflightUnitFormatter
{
    public static string FormatSpeed(double metersPerSecond)
    {
        double magnitude = Math.Abs(metersPerSecond);
        if (magnitude >= PhysicalConstants.SpeedOfLight * 0.001d)
            return $"{metersPerSecond / PhysicalConstants.SpeedOfLight * 100d:0.###} %c";
        if (magnitude >= 1000d)
            return $"{metersPerSecond / 1000d:0.##} km/s";
        return $"{metersPerSecond:0} m/s";
    }

    public static string FormatDistance(double meters)
    {
        double magnitude = Math.Abs(meters);
        if (magnitude >= PhysicalConstants.LightYear * 0.1d)
            return $"{meters / PhysicalConstants.LightYear:0.##} ly";
        if (magnitude >= PhysicalConstants.AstronomicalUnit * 0.01d)
            return $"{meters / PhysicalConstants.AstronomicalUnit:0.###} AU";
        if (magnitude >= 1000d)
            return $"{meters / 1000d:0.##} km";
        return $"{meters:0} m";
    }

    public static string FormatAltitude(double meters)
        => Math.Abs(meters) >= 1000d
            ? $"{meters / 1000d:0.##} km"
            : $"{meters:0} m";

    public static string FormatMass(double kilograms)
        => Math.Abs(kilograms) >= 1000d
            ? $"{kilograms / 1000d:0.##} t"
            : $"{kilograms:0.##} kg";

    public static string FormatForce(double newtons)
    {
        double magnitude = Math.Abs(newtons);
        if (magnitude >= 1_000_000d)
            return $"{newtons / 1_000_000d:0.##} MN";
        if (magnitude >= 1000d)
            return $"{newtons / 1000d:0.##} kN";
        return $"{newtons:0.##} N";
    }

    public static string FormatAcceleration(double metersPerSecondSquared)
        => $"{metersPerSecondSquared:0.##} m/s²  ({metersPerSecondSquared / PhysicalConstants.StandardGravity:0.##} g)";

    public static string FormatDuration(double seconds)
    {
        double magnitude = Math.Abs(seconds);
        if (magnitude >= 86_400d)
            return $"{seconds / 86_400d:0.##} d";
        if (magnitude >= 3600d)
            return $"{seconds / 3600d:0.##} h";
        if (magnitude >= 60d)
            return $"{seconds / 60d:0.##} min";
        return $"{seconds:0.##} s";
    }

    public static string FormatReference(SpaceflightVelocityReference reference)
    {
        switch (reference)
        {
            case SpaceflightVelocityReference.PlanetInertial:
                return "行星惯性系";
            case SpaceflightVelocityReference.AtmosphereRelative:
                return "大气相对";
            case SpaceflightVelocityReference.TargetRelative:
                return "目标相对";
            default:
                return "恒星系质心";
        }
    }

    public static float AdaptiveTargetSpeedStep(float currentTargetSpeed)
    {
        if (currentTargetSpeed < 500f)
            return 25f;
        if (currentTargetSpeed < 3000f)
            return 100f;
        return 500f;
    }
}
