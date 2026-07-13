using UnityEngine;

public static class PlanetGravity
{
    const float MinDistance = 0.1f;

    public static float ComputeGravitationalParameter(float surfaceGravity, float planetRadius)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(48, (int)surfaceGravity, (int)planetRadius);
        return surfaceGravity * planetRadius * planetRadius;
    }

    public static float GetGravityMagnitude(float distance, float gravitationalParameter)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(49, (int)distance, (int)gravitationalParameter);
        float r = Mathf.Max(distance, MinDistance);
        return gravitationalParameter / (r * r);
    }

    public static Vector3 GetGravitationalAcceleration(
        Vector3 worldPosition,
        Vector3 planetCenterWorld,
        float gravitationalParameter)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(50, (int)gravitationalParameter);
        Vector3 toCenter = planetCenterWorld - worldPosition;
        float distance = toCenter.magnitude;
        if (distance < MinDistance)
            return Vector3.zero;

        float magnitude = gravitationalParameter / (distance * distance);
        return toCenter.normalized * magnitude;
    }

    public static Vector3 GetUp(Vector3 worldPosition, Vector3 planetCenterWorld)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(51);
        Vector3 up = worldPosition - planetCenterWorld;
        if (up.sqrMagnitude < 0.0001f)
            return Vector3.up;

        return up.normalized;
    }

    public static Vector3 GetDown(Vector3 worldPosition, Vector3 planetCenterWorld)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(52);
        return -GetUp(worldPosition, planetCenterWorld);
    }

    public static Quaternion GetSurfaceRotation(Vector3 worldPosition, Vector3 planetCenterWorld)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(53);
        Vector3 up = GetUp(worldPosition, planetCenterWorld);
        return Quaternion.FromToRotation(Vector3.up, up);
    }
}
