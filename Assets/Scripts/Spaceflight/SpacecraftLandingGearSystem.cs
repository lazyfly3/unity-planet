using UnityEngine;

[DisallowMultipleComponent]
public sealed class SpacecraftLandingGearSystem : MonoBehaviour
{
    [SerializeField] Rigidbody shipBody;
    [SerializeField] LayerMask groundLayers = ~0;
    [SerializeField, Min(0.5f)] float castLength = 5f;
    [SerializeField, Min(0f)] float springStrength = 36f;
    [SerializeField, Min(0f)] float damping = 8f;
    [SerializeField] Vector3[] localContactPoints =
    {
        new Vector3(-1.6f, -0.7f, 1.3f),
        new Vector3(1.6f, -0.7f, 1.3f),
        new Vector3(0f, -0.7f, -1.5f)
    };

    readonly RaycastHit[] hits = new RaycastHit[12];
    Vector3 planetCenter;

    public bool Deployed { get; private set; }
    public int ContactCount { get; private set; }
    public Vector3 AverageGroundNormal { get; private set; } = Vector3.up;

    public void Configure(Rigidbody body, Vector3 center)
    {
        shipBody = body;
        planetCenter = center;
    }

    public void SetDeployed(bool deployed)
    {
        Deployed = deployed;
        if (!deployed)
            ContactCount = 0;
    }

    void FixedUpdate()
    {
        ContactCount = 0;
        if (!Deployed || shipBody == null)
            return;

        Vector3 up = (shipBody.worldCenterOfMass - planetCenter).normalized;
        Vector3 normalSum = Vector3.zero;
        foreach (Vector3 localPoint in localContactPoints)
        {
            Vector3 point = shipBody.transform.TransformPoint(localPoint);
            int count = Physics.RaycastNonAlloc(point + up * 0.35f, -up, hits, castLength, groundLayers, QueryTriggerInteraction.Ignore);
            RaycastHit best = default;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                if (hits[i].collider == null || hits[i].collider.transform.IsChildOf(shipBody.transform))
                    continue;
                if (!found || hits[i].distance < best.distance)
                {
                    best = hits[i];
                    found = true;
                }
            }
            if (!found)
                continue;

            ContactCount++;
            normalSum += best.normal;
            float compression = Mathf.Clamp01((castLength - best.distance) / castLength);
            float normalSpeed = Vector3.Dot(shipBody.GetPointVelocity(point), best.normal);
            float acceleration = Mathf.Max(0f, compression * springStrength - normalSpeed * damping);
            shipBody.AddForceAtPosition(best.normal * acceleration, point, ForceMode.Acceleration);
        }
        if (ContactCount > 0)
            AverageGroundNormal = (normalSum / ContactCount).normalized;
    }
}
