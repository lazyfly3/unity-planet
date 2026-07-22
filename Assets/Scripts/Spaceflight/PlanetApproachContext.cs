using UnityEngine;

public static class PlanetApproachContext
{
    public static GalaxyPlanetDefinition Planet { get; private set; }
    public static Vector3 RelativePosition { get; private set; }
    public static Vector3 InertialVelocity { get; private set; }
    public static Quaternion ShipRotation { get; private set; }
    public static float HullIntegrity { get; private set; }
    public static bool IsValid => Planet != null;

    public static void Set(
        GalaxyPlanetDefinition planet,
        Vector3 relativePosition,
        Vector3 inertialVelocity,
        Quaternion shipRotation,
        float hullIntegrity)
    {
        Planet = planet;
        RelativePosition = relativePosition;
        InertialVelocity = inertialVelocity;
        ShipRotation = shipRotation;
        HullIntegrity = hullIntegrity;
    }

    public static void Clear()
    {
        Planet = null;
        RelativePosition = Vector3.zero;
        InertialVelocity = Vector3.zero;
        ShipRotation = Quaternion.identity;
        HullIntegrity = 100f;
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
    public float hullIntegrity = 100f;

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
