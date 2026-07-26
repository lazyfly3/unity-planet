using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class PlanetaryDroppedBody : MonoBehaviour
{
    const float StandardWaterDensity = 1000f;

    [SerializeField, Min(0.01f)] float cubeSize = 1f;
    [SerializeField, Min(0f)] float dragCoefficient = 1.05f;
    [SerializeField, Min(0f)] float airAngularDamping = 0.08f;
    [SerializeField, Min(0f)] float waterLinearDamping = 2.5f;
    [SerializeField, Min(0f)] float waterAngularDamping = 1.8f;

    Rigidbody body;
    IPlanetSurfaceRuntime surfaceRuntime;
    PlanetCelestialProfile celestial;

    public Rigidbody Body => body;

    public void Configure(
        Rigidbody valueBody,
        IPlanetSurfaceRuntime runtime,
        PlanetCelestialProfile profile,
        float size,
        float aerodynamicDragCoefficient)
    {
        body = valueBody != null ? valueBody : GetComponent<Rigidbody>();
        surfaceRuntime = runtime;
        celestial = profile
            ?? PlanetCelestialProfile.CreateCompatibleDefault();
        cubeSize = Mathf.Max(0.01f, size);
        dragCoefficient = Mathf.Max(
            0f,
            aerodynamicDragCoefficient);
        if (body != null)
            body.useGravity = false;
    }

    void Awake()
    {
        body = GetComponent<Rigidbody>();
    }

    void FixedUpdate()
    {
        if (body == null
            || body.isKinematic
            || surfaceRuntime == null)
        {
            return;
        }

        Vector3 center = body.worldCenterOfMass;
        Vector3 gravity = surfaceRuntime.GetGravity(center);
        body.AddForce(gravity, ForceMode.Acceleration);

        Vector3 up = surfaceRuntime.GetUp(center);
        float altitude = 0f;
        if (surfaceRuntime.TryProjectToSurface(
                center,
                out PlanetSurfaceSample surface))
        {
            altitude = Mathf.Max(
                0f,
                Vector3.Dot(center - surface.point, up));
        }

        float airDensity =
            PlanetSurfaceFlightEnvironment.CalculateAtmosphereDensity(
                celestial,
                altitude);
        ApplyAtmosphericDrag(airDensity);
        ApplyWaterPhysics(gravity);
    }

    void ApplyAtmosphericDrag(float airDensity)
    {
        if (airDensity <= 0.000001f)
            return;

        Vector3 relativeVelocity = body.velocity;
        Vector3 drag = CalculateAerodynamicDrag(
            relativeVelocity,
            transform.rotation,
            cubeSize,
            airDensity,
            dragCoefficient);
        body.AddForce(drag, ForceMode.Force);
        body.AddTorque(
            -body.angularVelocity
                * airAngularDamping
                * airDensity,
            ForceMode.Acceleration);
    }

    void ApplyWaterPhysics(Vector3 gravity)
    {
        if (!PlanetWaterRegistry.TrySampleAny(
                body.worldCenterOfMass,
                out WaterSample water))
        {
            return;
        }

        float halfExtent = CalculateProjectedHalfExtent(
            transform.rotation,
            water.surfaceNormal,
            cubeSize);
        float submersion = CalculateSubmersion(
            water.signedDistance,
            halfExtent);
        if (submersion <= 0f)
            return;

        float volume = cubeSize * cubeSize * cubeSize;
        float mass = Mathf.Max(0.01f, body.mass);
        float buoyancyAcceleration =
            StandardWaterDensity
            * volume
            / mass
            * gravity.magnitude
            * submersion;
        Vector3 relativeVelocity =
            body.velocity - water.flowVelocity;
        body.AddForce(
            water.surfaceNormal * buoyancyAcceleration
                - relativeVelocity
                    * waterLinearDamping
                    * submersion,
            ForceMode.Acceleration);
        body.AddTorque(
            -body.angularVelocity
                * waterAngularDamping
                * submersion,
            ForceMode.Acceleration);
    }

    public static Vector3 CalculateAerodynamicDrag(
        Vector3 relativeVelocity,
        Quaternion rotation,
        float size,
        float airDensity,
        float coefficient)
    {
        float speed = relativeVelocity.magnitude;
        if (speed <= 0.01f
            || airDensity <= 0.000001f
            || coefficient <= 0f)
        {
            return Vector3.zero;
        }

        Vector3 direction = relativeVelocity / speed;
        float area = CalculateProjectedArea(
            rotation,
            direction,
            size);
        return -direction
            * (0.5f
                * airDensity
                * speed
                * speed
                * coefficient
                * area);
    }

    public static float CalculateProjectedArea(
        Quaternion rotation,
        Vector3 direction,
        float size)
    {
        if (direction.sqrMagnitude <= 0.000001f)
            return 0f;
        direction.Normalize();
        Vector3 right = rotation * Vector3.right;
        Vector3 up = rotation * Vector3.up;
        Vector3 forward = rotation * Vector3.forward;
        float faceArea = Mathf.Max(0.0001f, size * size);
        return faceArea
            * (Mathf.Abs(Vector3.Dot(direction, right))
                + Mathf.Abs(Vector3.Dot(direction, up))
                + Mathf.Abs(Vector3.Dot(direction, forward)));
    }

    public static float CalculateProjectedHalfExtent(
        Quaternion rotation,
        Vector3 direction,
        float size)
    {
        if (direction.sqrMagnitude <= 0.000001f)
            return Mathf.Max(0.005f, size * 0.5f);
        direction.Normalize();
        float half = Mathf.Max(0.005f, size * 0.5f);
        return half
            * (Mathf.Abs(Vector3.Dot(
                    direction,
                    rotation * Vector3.right))
                + Mathf.Abs(Vector3.Dot(
                    direction,
                    rotation * Vector3.up))
                + Mathf.Abs(Vector3.Dot(
                    direction,
                    rotation * Vector3.forward)));
    }

    public static float CalculateSubmersion(
        float centerSignedDistance,
        float projectedHalfExtent)
    {
        projectedHalfExtent = Mathf.Max(
            0.005f,
            projectedHalfExtent);
        return Mathf.Clamp01(
            (projectedHalfExtent - centerSignedDistance)
            / (projectedHalfExtent * 2f));
    }
}
