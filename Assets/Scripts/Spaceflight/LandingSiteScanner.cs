using UnityEngine;

public enum LandingSiteSafety
{
    Unknown,
    Safe,
    Caution,
    Dangerous
}

[DisallowMultipleComponent]
public sealed class LandingSiteScanner : MonoBehaviour
{
    [SerializeField] Rigidbody shipBody;
    [SerializeField] Transform planetCenter;
    [SerializeField] VoxelQuadSphereWorld terrainWorld;
    [SerializeField, Min(0.1f)] float predictionSeconds = 3f;

    public bool HasGround { get; private set; }
    public float RadarAltitude { get; private set; }
    public float GroundSlope { get; private set; }
    public Vector3 GroundPoint { get; private set; }
    public Vector3 GroundNormal { get; private set; } = Vector3.up;
    public Vector3 PredictedTouchdownPoint { get; private set; }
    public LandingSiteSafety Safety { get; private set; }

    public void Configure(Rigidbody body, Transform center, VoxelQuadSphereWorld world)
    {
        shipBody = body;
        planetCenter = center;
        terrainWorld = world;
    }

    public void Sample()
    {
        HasGround = false;
        Safety = LandingSiteSafety.Unknown;
        if (shipBody == null || planetCenter == null || terrainWorld == null)
            return;

        Vector3 offset = shipBody.worldCenterOfMass - planetCenter.position;
        if (offset.sqrMagnitude < 0.01f)
            return;
        Vector3 radialUp = offset.normalized;
        if (!terrainWorld.TryFindPlanetSurface(radialUp, out RaycastHit hit))
            return;

        HasGround = true;
        GroundPoint = hit.point;
        GroundNormal = hit.normal;
        RadarAltitude = Mathf.Max(0f, Vector3.Dot(shipBody.worldCenterOfMass - hit.point, radialUp));
        GroundSlope = Vector3.Angle(radialUp, hit.normal);

        Vector3 predicted = shipBody.worldCenterOfMass + shipBody.velocity * predictionSeconds;
        Vector3 predictedDirection = predicted - planetCenter.position;
        if (predictedDirection.sqrMagnitude > 0.01f
            && terrainWorld.TryFindPlanetSurface(predictedDirection.normalized, out RaycastHit predictedHit))
            PredictedTouchdownPoint = predictedHit.point;
        else
            PredictedTouchdownPoint = hit.point;

        Safety = GroundSlope <= 12f
            ? LandingSiteSafety.Safe
            : GroundSlope <= 20f
                ? LandingSiteSafety.Caution
                : LandingSiteSafety.Dangerous;
    }
}
