using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlanetAtmosphericFlightModel : MonoBehaviour
{
    [SerializeField] Rigidbody shipBody;
    [SerializeField] CelestialGravityField gravityField;
    [SerializeField, Min(0.1f)] float referenceArea = 12f;
    [SerializeField, Min(0f)] float dragCoefficient = 0.085f;
    [SerializeField, Min(0f)] float liftCoefficient = 0.22f;
    [SerializeField, Min(0f)] float sideSlipDamping = 0.045f;
    [SerializeField, Range(5f, 55f)] float stallAngle = 24f;
    [SerializeField, Min(1f)] float maximumAerodynamicAcceleration = 38f;

    public float AirSpeed { get; private set; }
    public float AngleOfAttack { get; private set; }
    public float Density { get; private set; }
    public bool IsStalling { get; private set; }
    public Vector3 RelativeAirVelocity { get; private set; }

    public void Configure(Rigidbody body, CelestialGravityField field, float area = 12f)
    {
        shipBody = body;
        gravityField = field;
        referenceArea = Mathf.Max(0.1f, area);
    }

    public void StepPhysics()
    {
        if (shipBody == null || gravityField == null)
            return;

        Density = gravityField.SampleAtmosphereDensity(shipBody.worldCenterOfMass);
        RelativeAirVelocity = shipBody.velocity
            - gravityField.SampleAtmosphereVelocity(shipBody.worldCenterOfMass);
        AirSpeed = RelativeAirVelocity.magnitude;
        if (Density <= 0.000001f || AirSpeed <= 0.05f)
        {
            AngleOfAttack = 0f;
            IsStalling = false;
            return;
        }

        Vector3 flightDirection = RelativeAirVelocity / AirSpeed;
        AngleOfAttack = CalculateAngleOfAttack(
            shipBody.transform.forward,
            flightDirection,
            shipBody.transform.right);
        IsStalling = Mathf.Abs(AngleOfAttack) >= stallAngle;
        float dynamicPressure = 0.5f * Density * AirSpeed * AirSpeed;
        float mass = Mathf.Max(1f, shipBody.mass);

        Vector3 dragAcceleration = -flightDirection
            * (dynamicPressure * dragCoefficient * referenceArea / mass);

        Vector3 radialUp = (shipBody.worldCenterOfMass - gravityField.Center).normalized;
        Vector3 liftDirection = Vector3.ProjectOnPlane(radialUp, flightDirection).normalized;
        float liftFactor = CalculateLiftFactor(AngleOfAttack, stallAngle);
        Vector3 liftAcceleration = liftDirection.sqrMagnitude > 0.001f
            ? liftDirection * (dynamicPressure * liftCoefficient * referenceArea * liftFactor / mass)
            : Vector3.zero;

        Vector3 side = shipBody.transform.right;
        float sideSpeed = Vector3.Dot(RelativeAirVelocity, side);
        Vector3 sideAcceleration = -side * sideSpeed * Mathf.Abs(sideSpeed)
            * (Density * sideSlipDamping * referenceArea / mass);
        Vector3 acceleration = Vector3.ClampMagnitude(
            dragAcceleration + liftAcceleration + sideAcceleration,
            maximumAerodynamicAcceleration);
        shipBody.AddForce(acceleration, ForceMode.Acceleration);
    }

    public static float CalculateAngleOfAttack(
        Vector3 forward,
        Vector3 flightDirection,
        Vector3 right)
    {
        if (forward.sqrMagnitude < 0.0001f || flightDirection.sqrMagnitude < 0.0001f)
            return 0f;
        if (right.sqrMagnitude < 0.0001f)
            return Vector3.Angle(forward.normalized, flightDirection.normalized);
        return Vector3.SignedAngle(forward.normalized, flightDirection.normalized, right.normalized);
    }

    public static float CalculateLiftFactor(float angleOfAttack, float stallAngleDegrees)
    {
        float absoluteAngle = Mathf.Abs(angleOfAttack);
        float sign = Mathf.Sign(angleOfAttack);
        float linear = Mathf.Clamp01(absoluteAngle / Mathf.Max(1f, stallAngleDegrees));
        if (absoluteAngle <= stallAngleDegrees)
            return sign * linear;
        float stalled = 1f - Mathf.Clamp01((absoluteAngle - stallAngleDegrees) / 35f);
        return sign * Mathf.Max(0.08f, stalled);
    }
}
