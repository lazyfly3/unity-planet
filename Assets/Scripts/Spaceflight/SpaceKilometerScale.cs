using UnityEngine;

/// <summary>
/// Conversion boundary between authoritative SI values and the astronomical
/// presentation layer. The local flight/weapon physics bubble intentionally
/// remains metre based so PhysX never has to simulate millimetre-sized ships.
/// </summary>
public static class SpaceKilometerScale
{
    public const double MetersPerUnit = 1000d;
    public const double UnitsPerMeter = 1d / MetersPerUnit;
    public const double NewtonsPerKilometerForceUnit = 1000d;
    public const double NewtonMetersPerKilometerTorqueUnit = 1_000_000d;

    public static double ToKilometerUnits(double meters) => meters * UnitsPerMeter;
    public static float ToKilometerUnits(float meters) => meters * (float)UnitsPerMeter;
    public static Vector3 ToKilometerUnits(Vector3 meters)
        => meters * (float)UnitsPerMeter;
    public static Vector3 ToKilometerUnits(DoubleVector3 meters)
        => new Vector3(
            (float)(meters.x * UnitsPerMeter),
            (float)(meters.y * UnitsPerMeter),
            (float)(meters.z * UnitsPerMeter));

    public static double ToMeters(double kilometerUnits) => kilometerUnits * MetersPerUnit;
    public static float ToMeters(float kilometerUnits) => kilometerUnits * (float)MetersPerUnit;
    public static Vector3 ToMeters(Vector3 kilometerUnits)
        => kilometerUnits * (float)MetersPerUnit;

    public static double ToKilometerUnitsPerSecond(double metersPerSecond)
        => metersPerSecond * UnitsPerMeter;
    public static Vector3 ToKilometerUnitsPerSecond(Vector3 metersPerSecond)
        => metersPerSecond * (float)UnitsPerMeter;
    public static Vector3 ToMetersPerSecond(Vector3 kilometerUnitsPerSecond)
        => kilometerUnitsPerSecond * (float)MetersPerUnit;

    public static double ToKilometerUnitsPerSecondSquared(double metersPerSecondSquared)
        => metersPerSecondSquared * UnitsPerMeter;
    public static Vector3 ToKilometerUnitsPerSecondSquared(Vector3 metersPerSecondSquared)
        => metersPerSecondSquared * (float)UnitsPerMeter;

    /// <remarks>
    /// This value is for kilometre-layer kinematics and diagnostics. Do not
    /// pass it to the metre-based local Rigidbody; local AddForce uses newtons.
    /// </remarks>
    public static double ToKilometerForceUnits(double newtons)
        => newtons / NewtonsPerKilometerForceUnit;

    /// <remarks>
    /// This value is for kilometre-layer kinematics and diagnostics. Local
    /// Rigidbody torque remains expressed in physical N*m.
    /// </remarks>
    public static double ToKilometerTorqueUnits(double newtonMeters)
        => newtonMeters / NewtonMetersPerKilometerTorqueUnit;
}

public enum SpaceflightInteractionMode
{
    TacticalPhysics,
    HighSpeedTravel,
    WarpCinematic
}

public enum SpaceflightScaleLayer
{
    MeterPhysicsBubble,
    KilometerHighSpeed,
    WarpCinematic
}
