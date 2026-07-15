using UnityEngine;

public static class PlanetGravity
{
    const float MinDistance = 0.1f;

    public static float ComputeGravitationalParameter(float surfaceGravity, float planetRadius)
    {if(FSPDebuger.EnableLogTrackInternal){FSPDebuger.PushDepth();FSPDebuger.LogTrack(91, (int)surfaceGravity, (int)planetRadius);}
        return surfaceGravity * planetRadius * planetRadius;
    
    if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.PopDepth();}

    public static float GetGravityMagnitude(float distance, float gravitationalParameter)
    {if(FSPDebuger.EnableLogTrackInternal){FSPDebuger.PushDepth();FSPDebuger.LogTrack(92, (int)distance, (int)gravitationalParameter);}
        float r = Mathf.Max(distance, MinDistance);
        return gravitationalParameter / (r * r);
    
    if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.PopDepth();}

    public static Vector3 GetGravitationalAcceleration(
        Vector3 worldPosition,
        Vector3 planetCenterWorld,
        float gravitationalParameter)
    {if(FSPDebuger.EnableLogTrackInternal){FSPDebuger.PushDepth();FSPDebuger.LogTrack(93, (int)gravitationalParameter);}
        Vector3 toCenter = planetCenterWorld - worldPosition;
        float distance = toCenter.magnitude;
        if (distance < MinDistance)
            return Vector3.zero;

        float magnitude = gravitationalParameter / (distance * distance);
        return toCenter.normalized * magnitude;
    
    if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.PopDepth();}

    public static Vector3 GetUp(Vector3 worldPosition, Vector3 planetCenterWorld)
    {if(FSPDebuger.EnableLogTrackInternal){FSPDebuger.PushDepth();FSPDebuger.LogTrack(94);}
        Vector3 up = worldPosition - planetCenterWorld;
        if (up.sqrMagnitude < 0.0001f)
            return Vector3.up;

        return up.normalized;
    
    if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.PopDepth();}

    public static Vector3 GetDown(Vector3 worldPosition, Vector3 planetCenterWorld)
    {if(FSPDebuger.EnableLogTrackInternal){FSPDebuger.PushDepth();FSPDebuger.LogTrack(95);}
        return -GetUp(worldPosition, planetCenterWorld);
    
    if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.PopDepth();}

    public static Quaternion GetSurfaceRotation(Vector3 worldPosition, Vector3 planetCenterWorld)
    {if(FSPDebuger.EnableLogTrackInternal){FSPDebuger.PushDepth();FSPDebuger.LogTrack(96);}
        Vector3 up = GetUp(worldPosition, planetCenterWorld);
        return Quaternion.FromToRotation(Vector3.up, up);
    
    if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.PopDepth();}
}
