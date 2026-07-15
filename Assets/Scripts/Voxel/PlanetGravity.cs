using UnityEngine;

public static class PlanetGravity
{
    const float MinDistance = 0.1f;

    public static float ComputeGravitationalParameter(float surfaceGravity, float planetRadius)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(125, (int)surfaceGravity, (int)planetRadius);}
    try
    {
        return surfaceGravity * planetRadius * planetRadius;
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public static float GetGravityMagnitude(float distance, float gravitationalParameter)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(126, (int)distance, (int)gravitationalParameter);}
    try
    {
        float r = Mathf.Max(distance, MinDistance);
        return gravitationalParameter / (r * r);
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public static Vector3 GetGravitationalAcceleration(
        Vector3 worldPosition,
        Vector3 planetCenterWorld,
        float gravitationalParameter)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(127, (int)gravitationalParameter);}
    try
    {
        Vector3 toCenter = planetCenterWorld - worldPosition;
        float distance = toCenter.magnitude;
        if (distance < MinDistance)
            return Vector3.zero;

        float magnitude = gravitationalParameter / (distance * distance);
        return toCenter.normalized * magnitude;
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public static Vector3 GetUp(Vector3 worldPosition, Vector3 planetCenterWorld)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(128);}
    try
    {
        Vector3 up = worldPosition - planetCenterWorld;
        if (up.sqrMagnitude < 0.0001f)
            return Vector3.up;

        return up.normalized;
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public static Vector3 GetDown(Vector3 worldPosition, Vector3 planetCenterWorld)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(129);}
    try
    {
        return -GetUp(worldPosition, planetCenterWorld);
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public static Quaternion GetSurfaceRotation(Vector3 worldPosition, Vector3 planetCenterWorld)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(130);}
    try
    {
        Vector3 up = GetUp(worldPosition, planetCenterWorld);
        return Quaternion.FromToRotation(Vector3.up, up);
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}
}
