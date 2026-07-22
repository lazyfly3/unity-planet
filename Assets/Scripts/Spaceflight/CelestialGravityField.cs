using UnityEngine;

[System.Serializable]
public struct OrbitalState
{
    public float altitude;
    public float speed;
    public float circularSpeed;
    public float escapeSpeed;
    public float specificEnergy;
    public float eccentricity;
    public float periapsisAltitude;
    public float apoapsisAltitude;
    public bool isBound;
}

[DisallowMultipleComponent]
public sealed class CelestialGravityField : MonoBehaviour
{
    [SerializeField] Transform bodyCenter;
    [SerializeField] PlanetCelestialProfile profile = new PlanetCelestialProfile();

    public Vector3 Center => bodyCenter == null ? transform.position : bodyCenter.position;
    public PlanetCelestialProfile Profile => profile;

    public void Configure(Transform center, PlanetCelestialProfile value)
    {
        bodyCenter = center;
        profile = (value ?? PlanetCelestialProfile.CreateCompatibleDefault()).Clone();
    }

    public Vector3 SampleGravity(Vector3 position)
    {
        Vector3 offset = position - Center;
        float squaredRadius = Mathf.Max(1f, offset.sqrMagnitude);
        return -offset.normalized * (profile.gravitationalParameter / squaredRadius);
    }

    public Vector3 SampleAtmosphereVelocity(Vector3 position)
    {
        float radiansPerSecond = Mathf.PI * 2f / Mathf.Max(1f, profile.rotationPeriod);
        return Vector3.Cross(profile.rotationAxis * radiansPerSecond, position - Center);
    }

    public float SampleAtmosphereDensity(Vector3 position)
    {
        if (!profile.HasAtmosphere)
            return 0f;
        float altitude = (position - Center).magnitude - profile.radius;
        if (altitude >= profile.atmosphereTopAltitude)
            return 0f;
        return profile.atmosphereSurfaceDensity
            * Mathf.Exp(-Mathf.Max(0f, altitude) / profile.atmosphereScaleHeight);
    }

    public OrbitalState CalculateOrbit(Vector3 position, Vector3 inertialVelocity)
    {
        Vector3 radiusVector = position - Center;
        float radius = Mathf.Max(0.01f, radiusVector.magnitude);
        float mu = profile.gravitationalParameter;
        float speedSquared = inertialVelocity.sqrMagnitude;
        float energy = speedSquared * 0.5f - mu / radius;
        Vector3 angularMomentum = Vector3.Cross(radiusVector, inertialVelocity);
        Vector3 eccentricityVector = Vector3.Cross(inertialVelocity, angularMomentum) / mu
            - radiusVector / radius;
        float eccentricity = eccentricityVector.magnitude;
        bool bound = energy < 0f;
        float semiMajorAxis = bound ? -mu / (2f * energy) : float.PositiveInfinity;
        return new OrbitalState
        {
            altitude = radius - profile.radius,
            speed = Mathf.Sqrt(speedSquared),
            circularSpeed = Mathf.Sqrt(mu / radius),
            escapeSpeed = Mathf.Sqrt(2f * mu / radius),
            specificEnergy = energy,
            eccentricity = eccentricity,
            periapsisAltitude = bound ? semiMajorAxis * (1f - eccentricity) - profile.radius : float.NegativeInfinity,
            apoapsisAltitude = bound ? semiMajorAxis * (1f + eccentricity) - profile.radius : float.PositiveInfinity,
            isBound = bound
        };
    }
}
