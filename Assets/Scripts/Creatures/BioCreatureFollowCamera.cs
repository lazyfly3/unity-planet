using UnityEngine;

[DisallowMultipleComponent]
public sealed class BioCreatureFollowCamera : MonoBehaviour
{
    [SerializeField] SphericalGravitySource gravitySource;
    [SerializeField] Transform target;
    [SerializeField, Min(1f)] float followDistance = 11f;
    [SerializeField, Min(0f)] float height = 6f;
    [SerializeField, Min(0.1f)] float positionSmoothSpeed = 6f;
    [SerializeField, Min(0.1f)] float rotationSmoothSpeed = 8f;

    public void Configure(SphericalGravitySource source)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(26);}
    try
    {
        gravitySource = source;
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public void SetTarget(Transform newTarget, bool snap = false)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(27, (snap?1:0));}
    try
    {
        target = newTarget;
        if (snap && target != null && gravitySource != null)
            ApplyCamera(1f);
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    void LateUpdate()
    {
        if (target == null || gravitySource == null)
            return;
        ApplyCamera(Time.deltaTime);
    }

    void ApplyCamera(float deltaTime)
    {
        Vector3 up = gravitySource.GetUp(target.position);
        Vector3 forward = Vector3.ProjectOnPlane(target.forward, up);
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.Cross(Vector3.forward, up);
        forward.Normalize();

        Vector3 desiredPosition = target.position - forward * followDistance + up * height;
        float positionBlend = deltaTime >= 1f ? 1f : 1f - Mathf.Exp(-positionSmoothSpeed * deltaTime);
        transform.position = Vector3.Lerp(transform.position, desiredPosition, positionBlend);

        Vector3 lookDirection = target.position + up * 0.5f - transform.position;
        Quaternion desiredRotation = Quaternion.LookRotation(lookDirection.normalized, up);
        float rotationBlend = deltaTime >= 1f ? 1f : 1f - Mathf.Exp(-rotationSmoothSpeed * deltaTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, rotationBlend);
    }
}
