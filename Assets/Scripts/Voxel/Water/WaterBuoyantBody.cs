using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class WaterBuoyantBody : MonoBehaviour
{
    [SerializeField] Transform[] samplePoints;
    [SerializeField, Min(0f)] float buoyancyAcceleration = 14f;
    [SerializeField, Min(0f)] float waterDrag = 2.5f;
    [SerializeField, Min(0f)] float flowInfluence = 3f;

    Rigidbody body;

    void Awake()
    {
        body = GetComponent<Rigidbody>();
    }

    void FixedUpdate()
    {
        int count = samplePoints != null && samplePoints.Length > 0 ? samplePoints.Length : 1;
        for (int i = 0; i < count; i++)
        {
            Vector3 point = samplePoints != null && samplePoints.Length > 0
                ? samplePoints[i].position
                : body.worldCenterOfMass;
            if (!PlanetRiverSystem.TrySampleAny(point, out WaterSample water) || water.signedDistance >= 0f)
                continue;
            float submerged = water.Submersion;
            Vector3 relativeVelocity = body.GetPointVelocity(point) - water.flowVelocity;
            Vector3 force = water.surfaceNormal * buoyancyAcceleration * submerged
                - relativeVelocity * waterDrag * submerged
                + water.flowVelocity * flowInfluence * submerged;
            body.AddForceAtPosition(force / count, point, ForceMode.Acceleration);
            Vector3 reactionImpulse = -force * (body.mass / count) * Time.fixedDeltaTime;
            PlanetRiverSystem.ApplyImpulseAny(point, reactionImpulse);
        }
    }
}
