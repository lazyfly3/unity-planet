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
        if (offset.sqrMagnitude < 0.000001f)
            return Vector3.zero;
        double altitude = PlanetScaleMapping.PhysicalAltitudeFromPresentation(
            profile,
            offset.magnitude);
        double acceleration = profile.Physical.GravityAtAltitude(altitude);
        return -offset.normalized * (float)acceleration;
    }

    public Vector3 SampleAtmosphereVelocity(Vector3 position)
    {
        Vector3 offset = position - Center;
        if (offset.sqrMagnitude < 0.000001f)
            return Vector3.zero;
        return Vector3.Cross(
            PlanetReferenceFrame.AngularVelocity(profile),
            offset);
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
        Vector3 presentationRadiusVector = position - Center;
        float presentationRadius = Mathf.Max(0.01f, presentationRadiusVector.magnitude);
        double altitude = PlanetScaleMapping.PhysicalAltitudeFromPresentation(
            profile,
            presentationRadius);
        float radius = (float)System.Math.Max(
            1d,
            profile.Physical.radiusMeters + altitude);
        Vector3 radiusVector = presentationRadiusVector.normalized * radius;
        float mu = (float)profile.Physical.gravitationalParameter;
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
            altitude = (float)altitude,
            speed = Mathf.Sqrt(speedSquared),
            circularSpeed = Mathf.Sqrt(mu / radius),
            escapeSpeed = Mathf.Sqrt(2f * mu / radius),
            specificEnergy = energy,
            eccentricity = eccentricity,
            periapsisAltitude = bound
                ? semiMajorAxis * (1f - eccentricity) - (float)profile.Physical.radiusMeters
                : float.NegativeInfinity,
            apoapsisAltitude = bound
                ? semiMajorAxis * (1f + eccentricity) - (float)profile.Physical.radiusMeters
                : float.PositiveInfinity,
            isBound = bound
        };
    }
}
