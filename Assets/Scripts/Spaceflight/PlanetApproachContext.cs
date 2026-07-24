using UnityEngine;

public enum PlanetApproachEntryMode
{
    LegacyOrbit,
    ControlledAtmosphericEntry
}

public static class PlanetApproachContext
{
    public static GalaxyPlanetDefinition Planet { get; private set; }
    public static Vector3 RelativePosition { get; private set; }
    public static Vector3 InertialVelocity { get; private set; }
    public static Quaternion ShipRotation { get; private set; }
    public static float HullIntegrity { get; private set; }
    public static bool AutomaticLanding { get; private set; }
    public static Vector3 EntryDirection { get; private set; } = Vector3.up;
    public static float TargetCruiseHeight { get; private set; } = 420f;
    public static PlanetApproachEntryMode EntryMode { get; private set; }
    public static bool IsValid => Planet != null;

    public static void Set(
        GalaxyPlanetDefinition planet,
        Vector3 relativePosition,
        Vector3 inertialVelocity,
        Quaternion shipRotation,
        float hullIntegrity,
        bool automaticLanding = false)
    {
        Vector3 direction = relativePosition.sqrMagnitude > 0.001f
            ? relativePosition.normalized
            : Vector3.up;
        Set(
            planet,
            relativePosition,
            inertialVelocity,
            shipRotation,
            hullIntegrity,
            automaticLanding,
            direction,
            420f,
            PlanetApproachEntryMode.ControlledAtmosphericEntry);
    }

    public static void Set(
        GalaxyPlanetDefinition planet,
        Vector3 relativePosition,
        Vector3 inertialVelocity,
        Quaternion shipRotation,
        float hullIntegrity,
        bool automaticLanding,
        Vector3 entryDirection,
        float targetCruiseHeight,
        PlanetApproachEntryMode entryMode)
    {
        Planet = planet;
        RelativePosition = relativePosition;
        InertialVelocity = inertialVelocity;
        ShipRotation = shipRotation;
        HullIntegrity = hullIntegrity;
        AutomaticLanding = automaticLanding;
        EntryDirection = entryDirection.sqrMagnitude > 0.001f
            ? entryDirection.normalized
            : Vector3.up;
        TargetCruiseHeight = Mathf.Clamp(targetCruiseHeight, 250f, 650f);
        EntryMode = entryMode;
    }

    public static void Clear()
    {
        Planet = null;
        RelativePosition = Vector3.zero;
        InertialVelocity = Vector3.zero;
        ShipRotation = Quaternion.identity;
        HullIntegrity = 100f;
        AutomaticLanding = false;
        EntryDirection = Vector3.up;
        TargetCruiseHeight = 420f;
        EntryMode = PlanetApproachEntryMode.LegacyOrbit;
    }
}

[System.Serializable]
public sealed class PendingPlanetLandingContext
{
    static PendingPlanetLandingContext pending;

    public string planetId;
    public InterstellarCoordinate coordinate;
    public Vector3 landingDirection = Vector3.up;
    public Quaternion shipRotation = Quaternion.identity;
    public Vector3 playerLocalPosition;
    public Vector3 landingPointLocal;
    public Vector3 landingGroundNormal = Vector3.up;
    public float hullIntegrity = 100f;
    public PlanetLandingMode landingMode = PlanetLandingMode.Auto;
    public bool restoreSavedSpacecraftState;

    public static bool HasPending => pending != null;
    public static PendingPlanetLandingContext Peek() => pending;
    public static void Set(PendingPlanetLandingContext value) => pending = value;

    public static PendingPlanetLandingContext Consume()
    {
        PendingPlanetLandingContext value = pending;
        pending = null;
        return value;
    }
}
