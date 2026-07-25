using SpacecraftEditor;
using UnityEngine;

[DefaultExecutionOrder(-300)]
[DisallowMultipleComponent]
public sealed class PlanetSurfaceFlightEnvironment : MonoBehaviour
{
    [SerializeField] Rigidbody shipBody;
    [SerializeField] SpacecraftIfcsMotor ifcsMotor;
    [SerializeField, Min(0.01f)] float dragCoefficient = 0.18f;
    [SerializeField, Min(1f)] float maximumDragAcceleration = 45f;
    [SerializeField, Min(0f)] float angularDamping = 0.35f;

    IPlanetSurfaceRuntime surfaceRuntime;
    PlanetCelestialProfile celestial;
    Bounds shipBounds;

    public Vector3 GravityAcceleration { get; private set; }
    public Vector3 DragAcceleration { get; private set; }
    public float AirDensity { get; private set; }
    public float AirSpeed { get; private set; }
    public float Altitude { get; private set; }
    public Vector3 GroundSafetyAcceleration { get; private set; }

    public void Configure(
        Rigidbody body,
        SpacecraftIfcsMotor motor,
        IPlanetSurfaceRuntime runtime,
        PlanetCelestialProfile profile,
        Bounds localShipBounds)
    {
        shipBody = body;
        ifcsMotor = motor;
        surfaceRuntime = runtime;
        celestial = (profile
            ?? PlanetCelestialProfile.CreateCompatibleDefault()).Clone();
        shipBounds = localShipBounds;
    }

    void FixedUpdate()
    {
        if (shipBody == null
            || shipBody.isKinematic
            || surfaceRuntime == null)
        {
            return;
        }

        Vector3 center = shipBody.worldCenterOfMass;
        GravityAcceleration = surfaceRuntime.GetGravity(center);
        ifcsMotor?.SetEnvironmentalAcceleration(GravityAcceleration);
        shipBody.AddForce(
            GravityAcceleration,
            ForceMode.Acceleration);

        Vector3 up = surfaceRuntime.GetUp(center);
        Vector3 floorPoint = center;
        bool hasFloor = surfaceRuntime.TryProjectToSurface(
                center,
                out PlanetSurfaceSample surface);
        if (hasFloor)
        {
            floorPoint = surface.point;
            if (PlanetWaterRegistry.TrySampleAny(
                    center,
                    out WaterSample water)
                && Vector3.Dot(
                    water.surfacePoint - floorPoint,
                    up) > 0f)
            {
                floorPoint = water.surfacePoint;
            }
            Altitude = Vector3.Dot(center - floorPoint, up);
            ApplyGroundSafety(up, floorPoint);
        }
        else
        {
            Altitude = 0f;
            GroundSafetyAcceleration = Vector3.zero;
        }

        AirDensity = CalculateAtmosphereDensity(celestial, Altitude);
        Vector3 relativeAirVelocity = shipBody.velocity;
        AirSpeed = relativeAirVelocity.magnitude;
        DragAcceleration = CalculateDragAcceleration(
            relativeAirVelocity,
            AirDensity,
            dragCoefficient,
            CalculateProjectedArea(relativeAirVelocity),
            shipBody.mass,
            maximumDragAcceleration);
        if (DragAcceleration.sqrMagnitude > 0f)
        {
            shipBody.AddForce(
                DragAcceleration,
                ForceMode.Acceleration);
        }
        if (AirDensity > 0.000001f
            && shipBody.angularVelocity.sqrMagnitude > 0.0001f)
        {
            float surfaceDensity = Mathf.Max(
                0.000001f,
                CalculateAtmosphereDensity(celestial, 0f));
            float densityRatio = Mathf.Clamp01(
                AirDensity / surfaceDensity);
            shipBody.AddTorque(
                -shipBody.angularVelocity
                    * angularDamping
                    * densityRatio,
                ForceMode.Acceleration);
        }
    }

    void ApplyGroundSafety(Vector3 up, Vector3 floorPoint)
    {
        float minimumCenterClearance =
            Mathf.Max(1.5f, -shipBounds.min.y + 1.5f);
        if (Altitude < -10f)
        {
            shipBody.position =
                floorPoint + up * minimumCenterClearance;
            float downwardSpeed =
                Vector3.Dot(shipBody.velocity, up);
            if (downwardSpeed < 0f)
                shipBody.velocity -= up * downwardSpeed;
            Altitude = minimumCenterClearance;
        }

        float deficit = minimumCenterClearance - Altitude;
        if (deficit <= 0f)
        {
            GroundSafetyAcceleration = Vector3.zero;
            return;
        }
        float verticalSpeed = Vector3.Dot(shipBody.velocity, up);
        float acceleration = Mathf.Clamp(
            deficit * 18f - Mathf.Min(0f, verticalSpeed) * 8f,
            0f,
            80f);
        GroundSafetyAcceleration = up * acceleration;
        shipBody.AddForce(
            GroundSafetyAcceleration,
            ForceMode.Acceleration);
    }

    float CalculateProjectedArea(Vector3 velocity)
    {
        if (velocity.sqrMagnitude < 0.0001f)
            return Mathf.Max(1f, shipBounds.size.x * shipBounds.size.y);
        Vector3 direction = velocity.normalized;
        Vector3 size = shipBounds.size;
        float forwardArea = Mathf.Max(1f, size.x * size.y);
        float sideArea = Mathf.Max(1f, size.z * size.y);
        float topArea = Mathf.Max(1f, size.x * size.z);
        return Mathf.Abs(Vector3.Dot(direction, transform.forward))
                * forwardArea
            + Mathf.Abs(Vector3.Dot(direction, transform.right))
                * sideArea
            + Mathf.Abs(Vector3.Dot(direction, transform.up))
                * topArea;
    }

    public static float CalculateAtmosphereDensity(
        PlanetCelestialProfile profile,
        float altitudeMeters)
    {
        if (profile == null || !profile.HasAtmosphere)
            return 0f;
        return (float)profile.Physical.AtmosphereDensityAtAltitude(
            Mathf.Max(0f, altitudeMeters));
    }

    public static Vector3 CalculateDragAcceleration(
        Vector3 relativeAirVelocity,
        float density,
        float coefficient,
        float referenceArea,
        float mass,
        float maximumAcceleration)
    {
        float speed = relativeAirVelocity.magnitude;
        if (density <= 0.000001f || speed <= 0.01f)
            return Vector3.zero;
        float acceleration =
            0.5f
            * density
            * speed
            * speed
            * Mathf.Max(0f, coefficient)
            * Mathf.Max(0.01f, referenceArea)
            / Mathf.Max(1f, mass);
        return -relativeAirVelocity.normalized
            * Mathf.Min(
                acceleration,
                Mathf.Max(0f, maximumAcceleration));
    }

    void OnDisable()
    {
        ifcsMotor?.SetEnvironmentalAcceleration(Vector3.zero);
        GravityAcceleration = Vector3.zero;
        DragAcceleration = Vector3.zero;
        GroundSafetyAcceleration = Vector3.zero;
    }
}
