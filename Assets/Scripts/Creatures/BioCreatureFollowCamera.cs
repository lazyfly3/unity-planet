using UnityEngine;

[DisallowMultipleComponent]
public sealed class BioCreatureFollowCamera : MonoBehaviour
{
    [SerializeField] SphericalGravitySource gravitySource;
    [SerializeField] Transform target;
    [SerializeField, Min(1f)] float followDistance = 7.5f;
    [SerializeField, Min(0f)] float height = 3.2f;
    [SerializeField] float sideOffset = 4.5f;
    [SerializeField, Min(0.1f)] float positionSmoothSpeed = 6f;
    [SerializeField, Min(0.1f)] float rotationSmoothSpeed = 8f;

    public void Configure(SphericalGravitySource source)
    {
gravitySource = source;
    
}

    public void SetTarget(Transform newTarget, bool snap = false)
    {
target = newTarget;
        if (snap && target != null && gravitySource != null)
            ApplyCamera(1f);
    
}

    public void SetFraming(float horizontalSpan, float verticalSpan, bool snap = false)
    {
        Camera view = GetComponent<Camera>();
        float verticalFov = view != null ? view.fieldOfView : 60f;
        float aspect = view != null ? Mathf.Max(0.1f, view.aspect) : 16f / 9f;
        float verticalRadians = verticalFov * Mathf.Deg2Rad;
        float horizontalRadians = 2f * Mathf.Atan(
            Mathf.Tan(verticalRadians * 0.5f) * aspect);
        float verticalDistance = Mathf.Max(0.5f, verticalSpan * 0.6f)
            / Mathf.Max(0.1f, Mathf.Tan(verticalRadians * 0.5f));
        float horizontalDistance = Mathf.Max(0.5f, horizontalSpan * 0.55f)
            / Mathf.Max(0.1f, Mathf.Tan(horizontalRadians * 0.5f));
        float framingDistance = Mathf.Max(verticalDistance, horizontalDistance) * 1.25f + 3.5f;

        followDistance = Mathf.Max(6.5f, framingDistance * 0.9f);
        sideOffset = Mathf.Max(3.2f, framingDistance * 0.45f);
        height = Mathf.Max(3.2f, verticalSpan * 0.95f);
        if (snap && target != null && gravitySource != null)
            ApplyCamera(1f);
    }

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

        Vector3 right = Vector3.Cross(up, forward).normalized;
        Vector3 desiredPosition = target.position - forward * followDistance
            + right * sideOffset + up * height;
        float positionBlend = deltaTime >= 1f ? 1f : 1f - Mathf.Exp(-positionSmoothSpeed * deltaTime);
        transform.position = Vector3.Lerp(transform.position, desiredPosition, positionBlend);

        Vector3 lookDirection = target.position + up * 0.5f - transform.position;
        Quaternion desiredRotation = Quaternion.LookRotation(lookDirection.normalized, up);
        float rotationBlend = deltaTime >= 1f ? 1f : 1f - Mathf.Exp(-rotationSmoothSpeed * deltaTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, rotationBlend);
    }
}
