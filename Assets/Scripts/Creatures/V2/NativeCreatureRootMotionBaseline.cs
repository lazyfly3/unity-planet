using UnityEngine;

[DisallowMultipleComponent]
public sealed class NativeCreatureRootMotionBaseline : MonoBehaviour
{
    [SerializeField] Animator animator;
    [SerializeField, Min(0f)] float forwardSpeed = 1.35f;
    [SerializeField] bool moveRoot = true;
    [Header("Spherical Surface")]
    [SerializeField] bool useSphericalSurface;
    [SerializeField] SphericalGravitySource gravitySource;
    [SerializeField] LayerMask groundLayers = ~0;
    [SerializeField, Min(0f)] float surfaceOffset = 0.035f;
    [SerializeField, Min(1f)] float surfaceProbeHeight = 12f;

    public void Configure(Animator targetAnimator, float speed)
    {
        animator = targetAnimator;
        forwardSpeed = Mathf.Max(0f, speed);
        if (animator == null)
            return;

        animator.enabled = true;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.updateMode = AnimatorUpdateMode.Normal;
    }

    void Awake()
    {
        if (animator != null)
        {
            animator.enabled = true;
            animator.applyRootMotion = false;
        }
    }

    void Start()
    {
        if (useSphericalSurface && gravitySource != null)
            SnapToSurfaceAndAlign();
    }

    void FixedUpdate()
    {
        if (!moveRoot || forwardSpeed <= 0f)
            return;

        if (useSphericalSurface && gravitySource != null)
        {
            MoveAlongSphere(forwardSpeed * Time.fixedDeltaTime);
            return;
        }

        transform.position += transform.forward * (forwardSpeed * Time.fixedDeltaTime);
    }

    void SnapToSurfaceAndAlign()
    {
        Vector3 up = gravitySource.GetUp(transform.position);
        if (TryFindSurface(up, out RaycastHit hit))
            transform.position = hit.point + up * surfaceOffset;

        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, up);
        if (forward.sqrMagnitude <= 0.000001f)
            forward = Vector3.Cross(transform.right, up);
        transform.rotation = Quaternion.LookRotation(forward.normalized, up);
    }

    void MoveAlongSphere(float distance)
    {
        Vector3 center = gravitySource.Center;
        Vector3 currentUp = gravitySource.GetUp(transform.position);
        Vector3 tangentForward = Vector3.ProjectOnPlane(transform.forward, currentUp);
        if (tangentForward.sqrMagnitude <= 0.000001f)
            return;
        tangentForward.Normalize();

        float currentRadius = Mathf.Max(
            gravitySource.Radius,
            Vector3.Distance(transform.position, center));
        Vector3 orbitAxis = Vector3.Cross(currentUp, tangentForward);
        if (orbitAxis.sqrMagnitude <= 0.000001f)
            return;
        orbitAxis.Normalize();

        float angle = distance / currentRadius * Mathf.Rad2Deg;
        Vector3 nextUp = Quaternion.AngleAxis(angle, orbitAxis) * currentUp;
        nextUp.Normalize();

        float targetRadius = currentRadius;
        if (TryFindSurface(nextUp, out RaycastHit hit))
            targetRadius = Vector3.Distance(hit.point, center) + surfaceOffset;

        transform.position = center + nextUp * targetRadius;

        Quaternion transportedRotation =
            Quaternion.FromToRotation(currentUp, nextUp) * transform.rotation;
        Vector3 transportedForward = Vector3.ProjectOnPlane(
            transportedRotation * Vector3.forward,
            nextUp);
        if (transportedForward.sqrMagnitude > 0.000001f)
            transform.rotation = Quaternion.LookRotation(transportedForward.normalized, nextUp);
    }

    bool TryFindSurface(Vector3 radialUp, out RaycastHit hit)
    {
        Vector3 origin = gravitySource.Center
            + radialUp * (gravitySource.Radius + surfaceProbeHeight);
        return Physics.Raycast(
            origin,
            -radialUp,
            out hit,
            surfaceProbeHeight * 2f,
            groundLayers,
            QueryTriggerInteraction.Ignore);
    }
}
