using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
public sealed class CreatureSphereMotor : MonoBehaviour
{
    readonly RaycastHit[] groundHits = new RaycastHit[16];

    SphericalGravitySource gravitySource;
    Rigidbody body;
    CapsuleCollider capsule;
    LayerMask groundLayers;
    float moveSpeed;
    float movementAcceleration;
    float adhesionAcceleration;
    float bodyClearance;
    bool directTangentialDrive = true;
    Vector3 orbitAxis = Vector3.forward;
    Vector3 smoothUp = Vector3.up;
    Vector3 previousUp = Vector3.up;
    Vector3 headingForward = Vector3.right;
    RaycastHit groundHit;

    public bool IsGrounded { get; private set; }
    public Vector3 GravityAcceleration { get; private set; }
    public Vector3 SurfaceForward => headingForward;
    public bool DirectTangentialDriveEnabled => directTangentialDrive;

    public void Configure(
        SphericalGravitySource source,
        Rigidbody targetBody,
        CapsuleCollider targetCapsule,
        LayerMask layers,
        float speed,
        float acceleration,
        float adhesion,
        Vector3 axis,
        float clearance,
        bool enableDirectTangentialDrive = true)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(36, (int)speed, (int)acceleration, (int)adhesion, (int)clearance, (enableDirectTangentialDrive?1:0));}
    try
    {
        gravitySource = source;
        body = targetBody;
        capsule = targetCapsule;
        groundLayers = layers;
        moveSpeed = Mathf.Max(0f, speed);
        movementAcceleration = Mathf.Max(0f, acceleration);
        adhesionAcceleration = Mathf.Max(0f, adhesion);
        orbitAxis = axis.sqrMagnitude > 0.0001f ? axis.normalized : Vector3.forward;
        bodyClearance = Mathf.Max(0.1f, clearance);
        directTangentialDrive = enableDirectTangentialDrive;
        InitializeOrientation();
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    void Awake()
    {
        if (body == null)
            body = GetComponent<Rigidbody>();
        if (capsule == null)
            capsule = GetComponent<CapsuleCollider>();
        body.useGravity = false;
        body.isKinematic = false;
        body.constraints = RigidbodyConstraints.FreezeRotation;
    }

    public void UpdateBodyGeometry(CapsuleCollider targetCapsule, float clearance)
    {
        if (targetCapsule != null) capsule = targetCapsule;
        bodyClearance = Mathf.Max(0.1f, clearance);
    }

    void FixedUpdate()
    {
        if (gravitySource == null || body == null || capsule == null)
            return;

        Vector3 targetUp = gravitySource.GetUp(body.position);
        GravityAcceleration = gravitySource.GetGravity(body.position);
        body.AddForce(GravityAcceleration, ForceMode.Acceleration);

        TransportHeading(targetUp);
        Vector3 orbitForward = Vector3.Cross(orbitAxis, targetUp);
        if (orbitForward.sqrMagnitude < 0.0001f)
            orbitForward = Vector3.ProjectOnPlane(headingForward, targetUp);
        orbitForward.Normalize();

        float turnBlend = 1f - Mathf.Exp(-8f * Time.fixedDeltaTime);
        headingForward = Vector3.Slerp(headingForward, orbitForward, turnBlend);
        headingForward = Vector3.ProjectOnPlane(headingForward, targetUp).normalized;
        smoothUp = Vector3.Slerp(smoothUp, targetUp, 1f - Mathf.Exp(-10f * Time.fixedDeltaTime)).normalized;
        body.MoveRotation(Quaternion.LookRotation(headingForward, smoothUp));

        IsGrounded = CheckGround(targetUp, out groundHit);
        Vector3 movementPlaneNormal = IsGrounded ? groundHit.normal : targetUp;
        if (directTangentialDrive)
        {
            Vector3 desiredVelocity = Vector3.ProjectOnPlane(headingForward, movementPlaneNormal).normalized * moveSpeed;
            Vector3 tangentialVelocity = Vector3.ProjectOnPlane(body.velocity, movementPlaneNormal);
            Vector3 velocityError = desiredVelocity - tangentialVelocity;
            body.AddForce(Vector3.ClampMagnitude(
                velocityError / Mathf.Max(Time.fixedDeltaTime, 0.001f), movementAcceleration),
                ForceMode.Acceleration);
        }

        if (IsGrounded)
        {
            body.AddForce(-groundHit.normal * adhesionAcceleration, ForceMode.Acceleration);
            float separatingSpeed = Vector3.Dot(body.velocity, groundHit.normal);
            if (separatingSpeed > 0f)
                body.AddForce(-groundHit.normal * separatingSpeed * 8f, ForceMode.Acceleration);
        }
    }

    void InitializeOrientation()
    {
        if (gravitySource == null || body == null)
            return;

        smoothUp = gravitySource.GetUp(body.position);
        previousUp = smoothUp;
        headingForward = Vector3.Cross(orbitAxis, smoothUp);
        if (headingForward.sqrMagnitude < 0.0001f)
            headingForward = Vector3.ProjectOnPlane(Vector3.forward, smoothUp);
        headingForward.Normalize();
        body.rotation = Quaternion.LookRotation(headingForward, smoothUp);
    }

    void TransportHeading(Vector3 targetUp)
    {
        if (previousUp.sqrMagnitude < 0.0001f)
            previousUp = targetUp;
        Quaternion transport = Quaternion.FromToRotation(previousUp, targetUp);
        headingForward = Vector3.ProjectOnPlane(transport * headingForward, targetUp);
        if (headingForward.sqrMagnitude < 0.0001f)
            headingForward = Vector3.Cross(orbitAxis, targetUp);
        headingForward.Normalize();
        previousUp = targetUp;
    }

    bool CheckGround(Vector3 up, out RaycastHit nearestHit)
    {
        float radius = Mathf.Max(0.08f, capsule.radius * 0.68f);
        Vector3 origin = body.position + up * 0.25f;
        int count = Physics.SphereCastNonAlloc(
            origin,
            radius,
            -up,
            groundHits,
            bodyClearance + 1f,
            groundLayers,
            QueryTriggerInteraction.Ignore);

        nearestHit = default;
        float nearestDistance = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = groundHits[i];
            if (hit.collider == null || hit.collider.attachedRigidbody == body || hit.distance >= nearestDistance)
                continue;
            nearestDistance = hit.distance;
            nearestHit = hit;
        }
        return nearestDistance < float.PositiveInfinity;
    }

    void OnDrawGizmosSelected()
    {
        if (gravitySource == null)
            return;

        Vector3 position = transform.position;
        Vector3 up = gravitySource.GetUp(position);
        Gizmos.color = Color.red;
        Gizmos.DrawLine(position, position - up * 3f);
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(position, position + up * 2f);
        Gizmos.color = Color.green;
        Gizmos.DrawLine(position, position + headingForward * 3f);
        if (IsGrounded)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(groundHit.point, 0.2f);
        }
    }
}
