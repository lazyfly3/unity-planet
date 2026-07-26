using SpacecraftEditor;
using UnityEngine;

[DefaultExecutionOrder(-300)]
[DisallowMultipleComponent]
public sealed class PlanetSurfaceFlightEnvironment : MonoBehaviour
{
    [SerializeField] Rigidbody shipBody;
    [SerializeField] SpacecraftIfcsMotor ifcsMotor;
    [SerializeField] ShipAssembly assembly;
    [SerializeField] ShipHullController hullController;
    [SerializeField, Min(0.01f)] float dragCoefficient = 0.18f;
    [SerializeField, Min(1f)] float maximumDragAcceleration = 45f;
    [SerializeField, Min(0f)] float angularDamping = 0.35f;
    [SerializeField, Min(1f)] float maximumAerodynamicAcceleration = 55f;

    IPlanetSurfaceRuntime surfaceRuntime;
    PlanetCelestialProfile celestial;
    Bounds shipBounds;

    public Vector3 GravityAcceleration { get; private set; }
    public Vector3 DragAcceleration { get; private set; }
    public float AirDensity { get; private set; }
    public float AirSpeed { get; private set; }
    public float Altitude { get; private set; }
    public Vector3 GroundSafetyAcceleration { get; private set; }
    public Vector3 AerodynamicAcceleration { get; private set; }
    public Vector3 AtmosphereVelocity { get; private set; }
    public float DynamicPressure { get; private set; }
    public float AngleOfAttack { get; private set; }
    public bool IsStalling { get; private set; }
    public float TotalLiftForce { get; private set; }
    public float TotalDragForce { get; private set; }

    public void Configure(
        Rigidbody body,
        SpacecraftIfcsMotor motor,
        IPlanetSurfaceRuntime runtime,
        PlanetCelestialProfile profile,
        Bounds localShipBounds)
    {
        shipBody = body;
        ifcsMotor = motor;
        assembly = body == null
            ? null
            : body.GetComponent<ShipAssembly>();
        hullController = body == null
            ? null
            : body.GetComponentInChildren<ShipHullController>(true);
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
        // The current PlanetLab surface has no weather system. The atmosphere
        // co-moves with the planar ground; this remains an explicit velocity so
        // a future wind model can be connected without changing IFCS.
        AtmosphereVelocity = Vector3.zero;
        Vector3 relativeAirVelocity =
            shipBody.velocity - AtmosphereVelocity;
        AirSpeed = relativeAirVelocity.magnitude;
        DynamicPressure =
            0.5f * AirDensity * AirSpeed * AirSpeed;
        ApplyAerodynamics(relativeAirVelocity);
        if (AirDensity > 0.000001f
            && shipBody.angularVelocity.sqrMagnitude > 0.0001f)
        {
            float surfaceDensity = Mathf.Max(
                0.000001f,
                CalculateAtmosphereDensity(celestial, 0f));
            float densityRatio = Mathf.Clamp01(
                AirDensity / surfaceDensity);
            float aerodynamicBlend = Mathf.Clamp01(
                DynamicPressure / 5500f);
            shipBody.AddTorque(
                -shipBody.angularVelocity
                    * angularDamping
                    * densityRatio
                    * Mathf.Lerp(1f, 0.35f, aerodynamicBlend),
                ForceMode.Acceleration);
        }

        ifcsMotor?.SetPlanetaryFlightContext(
            new PlanetaryFlightContext
            {
                active = true,
                upWorld = up,
                gravityAccelerationWorld = GravityAcceleration,
                aerodynamicAccelerationWorld =
                    AerodynamicAcceleration,
                atmosphereVelocityWorld = AtmosphereVelocity,
                altitude = Altitude,
                airDensity = AirDensity,
                airSpeed = AirSpeed,
                dynamicPressure = DynamicPressure,
                angleOfAttack = AngleOfAttack,
                isStalling = IsStalling
            });
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

    void ApplyAerodynamics(Vector3 relativeAirVelocity)
    {
        DragAcceleration = Vector3.zero;
        AerodynamicAcceleration = Vector3.zero;
        AngleOfAttack = 0f;
        IsStalling = false;
        TotalLiftForce = 0f;
        TotalDragForce = 0f;
        if (AirDensity <= 0.000001f
            || relativeAirVelocity.sqrMagnitude <= 0.0025f
            || shipBody == null)
        {
            return;
        }

        float speed = relativeAirVelocity.magnitude;
        Vector3 velocityDirection = relativeAirVelocity / speed;
        Vector3 localVelocity =
            transform.InverseTransformDirection(relativeAirVelocity);
        AngleOfAttack = Mathf.Atan2(
                -localVelocity.y,
                Mathf.Max(0.1f, Mathf.Abs(localVelocity.z)))
            * Mathf.Rad2Deg;

        float mass = Mathf.Max(1f, shipBody.mass);
        Vector3 center = shipBody.worldCenterOfMass;
        Vector3 totalForce = Vector3.zero;
        Vector3 totalTorque = Vector3.zero;

        ShipAerodynamicProfile hullAerodynamics =
            hullController != null && hullController.CurrentHull != null
                ? hullController.CurrentHull.Aerodynamics
                : null;
        float hullDragCoefficient =
            hullAerodynamics != null && hullAerodynamics.IsEnabled
                ? hullAerodynamics.BaseDragCoefficient
                : dragCoefficient;
        Vector3 hullDragAcceleration = CalculateDragAcceleration(
            relativeAirVelocity,
            AirDensity,
            hullDragCoefficient,
            CalculateProjectedArea(relativeAirVelocity),
            mass,
            maximumDragAcceleration);
        DragAcceleration = hullDragAcceleration;
        Vector3 hullDragForce = hullDragAcceleration * mass;
        totalForce += hullDragForce;
        TotalDragForce += hullDragForce.magnitude;

        if (hullAerodynamics != null
            && hullAerodynamics.ProducesSideForce)
        {
            Vector3 hullSideForce = CalculateSideForce(
                relativeAirVelocity,
                transform.right,
                DynamicPressure,
                Mathf.Max(
                    hullAerodynamics.ReferenceArea,
                    CalculateProjectedArea(relativeAirVelocity)),
                hullAerodynamics.SideStabilityCoefficient);
            totalForce += hullSideForce;
        }

        if (assembly != null)
        {
            for (int index = 0; index < assembly.Parts.Count; index++)
            {
                SpacecraftPart part = assembly.Parts[index];
                if (part == null || part.Definition == null)
                    continue;
                ShipAerodynamicProfile profile =
                    part.Definition.Aerodynamics;
                if (profile == null || !profile.IsEnabled)
                    continue;

                float area = profile.ReferenceArea
                    * part.UniformScale
                    * part.UniformScale;
                Vector3 force = CalculatePartAerodynamicForce(
                    profile,
                    area,
                    relativeAirVelocity,
                    velocityDirection,
                    DynamicPressure,
                    out float liftMagnitude,
                    out float dragMagnitude,
                    out bool stalled);
                Vector3 applicationPoint =
                    part.transform.TransformPoint(profile.CenterOffset);
                totalForce += force;
                totalTorque += Vector3.Cross(
                    applicationPoint - center,
                    force);
                TotalLiftForce += liftMagnitude;
                TotalDragForce += dragMagnitude;
                IsStalling |= stalled;
            }
        }

        float maximumForce =
            maximumAerodynamicAcceleration * mass;
        float magnitude = totalForce.magnitude;
        if (magnitude > maximumForce && magnitude > 0.001f)
        {
            float scale = maximumForce / magnitude;
            totalForce *= scale;
            totalTorque *= scale;
            TotalLiftForce *= scale;
            TotalDragForce *= scale;
        }

        AerodynamicAcceleration = totalForce / mass;
        shipBody.AddForce(totalForce, ForceMode.Force);
        shipBody.AddTorque(totalTorque, ForceMode.Force);
    }

    Vector3 CalculatePartAerodynamicForce(
        ShipAerodynamicProfile profile,
        float area,
        Vector3 relativeAirVelocity,
        Vector3 velocityDirection,
        float dynamicPressure,
        out float liftMagnitude,
        out float dragMagnitude,
        out bool stalled)
    {
        float liftCoefficient =
            profile.EvaluateLiftCoefficient(AngleOfAttack);
        float dragCoefficientForPart =
            profile.EvaluateDragCoefficient(liftCoefficient);
        Vector3 drag = -velocityDirection
            * (dynamicPressure * area * dragCoefficientForPart);
        Vector3 lift = Vector3.zero;
        if (profile.ProducesVerticalLift)
        {
            Vector3 liftDirection =
                Vector3.Cross(velocityDirection, transform.right);
            if (liftDirection.sqrMagnitude > 0.0001f)
            {
                lift = liftDirection.normalized
                    * (dynamicPressure * area * liftCoefficient);
            }
        }

        Vector3 sideForce = profile.ProducesSideForce
            ? CalculateSideForce(
                relativeAirVelocity,
                transform.right,
                dynamicPressure,
                area,
                profile.SideStabilityCoefficient)
            : Vector3.zero;
        liftMagnitude = lift.magnitude;
        dragMagnitude = drag.magnitude;
        stalled = profile.ProducesVerticalLift
            && Mathf.Abs(
                AngleOfAttack - profile.ZeroLiftAngleDegrees)
                >= profile.StallAngleDegrees;
        return drag + lift + sideForce;
    }

    static Vector3 CalculateSideForce(
        Vector3 relativeAirVelocity,
        Vector3 right,
        float dynamicPressure,
        float area,
        float coefficient)
    {
        float speed = relativeAirVelocity.magnitude;
        if (speed <= 0.05f
            || coefficient <= 0f
            || right.sqrMagnitude <= 0.0001f)
        {
            return Vector3.zero;
        }

        right.Normalize();
        float normalizedSideSpeed = Mathf.Clamp(
            Vector3.Dot(relativeAirVelocity, right) / speed,
            -1f,
            1f);
        return -right
            * (dynamicPressure
                * Mathf.Max(0f, area)
                * coefficient
                * normalizedSideSpeed);
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
        ifcsMotor?.ClearPlanetaryFlightContext();
        GravityAcceleration = Vector3.zero;
        DragAcceleration = Vector3.zero;
        AerodynamicAcceleration = Vector3.zero;
        GroundSafetyAcceleration = Vector3.zero;
        DynamicPressure = 0f;
        AngleOfAttack = 0f;
        IsStalling = false;
        TotalLiftForce = 0f;
        TotalDragForce = 0f;
    }
}
