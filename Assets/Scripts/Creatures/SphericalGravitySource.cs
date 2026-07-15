using UnityEngine;

[DisallowMultipleComponent]
public sealed class SphericalGravitySource : MonoBehaviour
{
    [SerializeField, Min(0.1f)] float radius = 100f;
    [SerializeField, Min(0f)] float surfaceGravity = 9.8f;

    public Vector3 Center => transform.position;
    public float Radius => radius;
    public float SurfaceGravity => surfaceGravity;
    public float GravitationalParameter => PlanetGravity.ComputeGravitationalParameter(surfaceGravity, radius);

    public Vector3 GetUp(Vector3 worldPosition)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(47);}
    try
    {
        return PlanetGravity.GetUp(worldPosition, Center);
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public Vector3 GetGravity(Vector3 worldPosition)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(48);}
    try
    {
        return PlanetGravity.GetGravitationalAcceleration(worldPosition, Center, GravitationalParameter);
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public Vector3 GetSurfacePoint(Vector3 direction)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(49);}
    try
    {
        Vector3 normalized = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.up;
        return Center + normalized * radius;
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public void Configure(float newRadius, float newSurfaceGravity)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(50, (int)newRadius, (int)newSurfaceGravity);}
    try
    {
        radius = Mathf.Max(0.1f, newRadius);
        surfaceGravity = Mathf.Max(0f, newSurfaceGravity);
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    void OnValidate()
    {
        radius = Mathf.Max(0.1f, radius);
        surfaceGravity = Mathf.Max(0f, surfaceGravity);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.75f, 1f, 0.35f);
        Gizmos.DrawWireSphere(Center, radius);
    }
}
