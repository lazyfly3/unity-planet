using System;
using UnityEngine;

public enum SurfaceSpacecraftParkingMode
{
    Terrain,
    OceanPlatform,
    Hovering
}

[Serializable]
public sealed class SurfaceSpacecraftState
{
    public bool valid;
    public Vector3 radialDirection = Vector3.up;
    public Vector3 tangentForward = Vector3.forward;
    public SurfaceSpacecraftParkingMode parkingMode;
    public float hoverAltitude = 6f;

    public SurfaceSpacecraftState Clone()
    {
        return (SurfaceSpacecraftState)MemberwiseClone();
    }

    public void ClampValues()
    {
        radialDirection = radialDirection.sqrMagnitude > 0.001f
            ? radialDirection.normalized
            : Vector3.up;
        tangentForward = Vector3.ProjectOnPlane(
            tangentForward,
            radialDirection).normalized;
        if (tangentForward.sqrMagnitude < 0.001f)
        {
            tangentForward = Vector3.Cross(
                radialDirection,
                Mathf.Abs(Vector3.Dot(radialDirection, Vector3.right)) < 0.9f
                    ? Vector3.right
                    : Vector3.forward).normalized;
        }
        hoverAltitude = Mathf.Clamp(hoverAltitude, 2f, 40f);
    }
}

public static class PendingSurfaceDepartureContext
{
    static bool pending;
    static Quaternion rotation;
    static Vector3 velocity;

    public static bool TryConsume(out Quaternion exitRotation, out Vector3 exitVelocity)
    {
        exitRotation = rotation;
        exitVelocity = velocity;
        bool hadPending = pending;
        pending = false;
        rotation = Quaternion.identity;
        velocity = Vector3.zero;
        return hadPending;
    }

    public static void Set(Quaternion exitRotation, Vector3 exitVelocity)
    {
        rotation = exitRotation;
        velocity = exitVelocity;
        pending = true;
    }

    public static void Clear()
    {
        pending = false;
        rotation = Quaternion.identity;
        velocity = Vector3.zero;
    }
}
