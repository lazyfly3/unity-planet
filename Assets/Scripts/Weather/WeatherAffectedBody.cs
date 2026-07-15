using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class WeatherAffectedBody : MonoBehaviour
{
    [SerializeField, Min(0f)] float windResponse = 0.22f;
    [SerializeField, Min(0f)] float maximumAcceleration = 5f;

    Rigidbody body;

    void Awake()
    {
        body = GetComponent<Rigidbody>();
    }

    void FixedUpdate()
    {
        if (!PlanetWeatherSystem.TrySample(body.worldCenterOfMass, out WeatherSnapshot weather))
            return;
        Vector3 relativeWind = weather.windVelocity - body.velocity;
        body.AddForce(Vector3.ClampMagnitude(relativeWind * windResponse, maximumAcceleration),
            ForceMode.Acceleration);
    }
}
