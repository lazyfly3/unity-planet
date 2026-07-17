using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
public class VoxelPlanetPlayerController : MonoBehaviour
{
    [SerializeField] VoxelWorld voxelWorld;
    [SerializeField] VoxelQuadSphereWorld quadSphereWorld;
    [SerializeField] Transform cameraTransform;

    [Header("Movement")]
    [SerializeField] float moveSpeed = 5f;
    [SerializeField] float groundAcceleration = 35f;
    [SerializeField] float airAcceleration = 8f;
    [SerializeField] float lookSpeed = 2f;
    [SerializeField] float jumpHeight = 1.2f;
    [SerializeField] float upSmoothSpeed = 8f;
    [SerializeField, Range(0f, 1f)] float weatherWindInfluence = 0.18f;

    [Header("Swimming")]
    [SerializeField, Min(0.1f)] float swimSpeed = 3.5f;
    [SerializeField, Min(0f)] float swimAcceleration = 12f;
    [SerializeField, Min(0f)] float swimUpSpeed = 3f;
    [SerializeField, Min(0f)] float waterDrag = 2.2f;
    [SerializeField, Min(0f)] float buoyancyAcceleration = 12f;
    [SerializeField, Min(0.1f)] float swimExitJumpHeight = 1.4f;
    [SerializeField, Min(0f)] float swimExitForwardSpeed = 2.5f;

    [Header("Radial Grounding")]
    [SerializeField, Min(0.01f)] float groundProbeDistance = 0.2f;
    [SerializeField, Range(0.5f, 0.99f)] float groundProbeRadiusScale = 0.9f;
    [SerializeField, Range(0f, 89f)] float maxGroundAngle = 55f;
    [SerializeField, Min(0f)] float groundStickAcceleration = 15f;
    [SerializeField, Min(0f)] float groundDetachSpeed = 0.1f;
    [SerializeField] LayerMask groundLayers = ~0;

    [Header("Space Travel")]
    [SerializeField] bool enableSpaceTravel = true;
    [SerializeField, Min(0.1f)] float galaxyMapAltitude = 8f;
    [SerializeField] bool requireOutwardVelocity = true;

    [Header("Scene Debug")]
    [SerializeField] bool showGravityGizmo = true;
    [SerializeField, Min(0.1f)] float gravityGizmoLength = 3f;

    readonly RaycastHit[] groundHits = new RaycastHit[16];

    Rigidbody body;
    CapsuleCollider capsule;
    Vector3 smoothUp = Vector3.up;
    Vector3 headingForward;
    Vector3 previousUp;
    Vector2 moveInput;
    float pitch;
    bool jumpQueued;
    bool gameplayInputBlocked;
    bool galaxyTransitionRequested;
    float activeGroundTraction = 1f;

    public bool IsGrounded { get; private set; }
    public bool IsSwimming { get; private set; }
    public float LookSpeed
    {
        get => lookSpeed;
        set => lookSpeed = Mathf.Clamp(value, 0.2f, 5f);
    }

    public void TeleportTo(Vector3 worldPosition, Quaternion worldRotation)
    {
if (body == null)
            body = GetComponent<Rigidbody>();

        body.position = worldPosition;
        body.rotation = worldRotation;
        if (!body.isKinematic)
        {
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        smoothUp = GetTargetUp(worldPosition);
        previousUp = smoothUp;
        headingForward = GetTangentForward(worldRotation * Vector3.forward, smoothUp);
        body.rotation = Quaternion.LookRotation(headingForward, smoothUp);
    
}

    void Awake()
    {
        body = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();

        body.useGravity = false;
        body.isKinematic = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.constraints = RigidbodyConstraints.FreezeRotation;
        capsule.enabled = true;
        capsule.direction = 1;

        CharacterController legacyController = GetComponent<CharacterController>();
        if (legacyController != null)
            legacyController.enabled = false;

        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;

        if (cameraTransform == transform)
        {
            Debug.LogError("VoxelPlanetPlayerController: Camera Transform cannot be the player transform.");
            cameraTransform = null;
        }
    }

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (cameraTransform != null)
            pitch = Mathf.Clamp(NormalizeAngle(cameraTransform.localEulerAngles.x), -80f, 80f);

        InitializeOrientation();
    }

    void Update()
    {
        gameplayInputBlocked = InventoryUI.BlocksGameplayInput;
        if (gameplayInputBlocked)
        {
            moveInput = Vector2.zero;
            jumpQueued = false;
            return;
        }

        moveInput = Vector2.ClampMagnitude(new Vector2(
            Input.GetAxisRaw("Horizontal"),
            Input.GetAxisRaw("Vertical")
        ), 1f);

        if (Input.GetKeyDown(KeyCode.Space))
            jumpQueued = true;

        HandleLook();
    }

    void FixedUpdate()
    {
        UpdateSmoothUp(Time.fixedDeltaTime);
        TransportHeadingToCurrentUp();

        Quaternion targetRotation = Quaternion.LookRotation(headingForward, smoothUp);
        body.MoveRotation(targetRotation);

        HandlePhysicsMovement();
        CheckGalaxyTransition();
    }

    void LateUpdate()
    {
        if (cameraTransform != null)
            cameraTransform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    void InitializeOrientation()
    {
        smoothUp = GetTargetUp(body.position);
        previousUp = smoothUp;
        headingForward = GetTangentForward(transform.forward, smoothUp);
        body.rotation = Quaternion.LookRotation(headingForward, smoothUp);
    }

    void UpdateSmoothUp(float deltaTime)
    {
        Vector3 targetUp = GetTargetUp(body.position);
        if (smoothUp.sqrMagnitude < 0.0001f)
            smoothUp = targetUp;

        float blend = 1f - Mathf.Exp(-upSmoothSpeed * deltaTime);
        smoothUp = Vector3.Slerp(smoothUp, targetUp, blend).normalized;
    }

    Vector3 GetTargetUp(Vector3 worldPosition)
    {
        if (quadSphereWorld != null)
            return PlanetGravity.GetUp(worldPosition, quadSphereWorld.GetPlanetCenterWorld());

        if (voxelWorld == null || !voxelWorld.UsePlanetGeneration)
            return Vector3.up;

        return PlanetGravity.GetUp(worldPosition, voxelWorld.GetPlanetCenterWorld());
    }

    Vector3 GetGravityAcceleration(Vector3 worldPosition)
    {
        if (quadSphereWorld != null)
        {
            return PlanetGravity.GetGravitationalAcceleration(
                worldPosition,
                quadSphereWorld.GetPlanetCenterWorld(),
                quadSphereWorld.GravitationalParameter
            );
        }

        if (voxelWorld == null || !voxelWorld.UsePlanetGeneration)
            return Vector3.down * 9.8f;

        return PlanetGravity.GetGravitationalAcceleration(
            worldPosition,
            voxelWorld.GetPlanetCenterWorld(),
            voxelWorld.GravitationalParameter
        );
    }

    void TransportHeadingToCurrentUp()
    {
        if (previousUp.sqrMagnitude < 0.0001f)
            previousUp = smoothUp;
        if (headingForward.sqrMagnitude < 0.0001f)
            headingForward = GetTangentForward(transform.forward, previousUp);

        Quaternion transport = Quaternion.FromToRotation(previousUp, smoothUp);
        headingForward = GetTangentForward(transport * headingForward, smoothUp);
        previousUp = smoothUp;
    }

    static Vector3 GetTangentForward(Vector3 preferredForward, Vector3 up)
    {
        Vector3 tangentForward = Vector3.ProjectOnPlane(preferredForward, up);
        if (tangentForward.sqrMagnitude >= 0.0001f)
            return tangentForward.normalized;

        Vector3 fallbackAxis = Mathf.Abs(Vector3.Dot(up, Vector3.forward)) < 0.9f
            ? Vector3.forward
            : Vector3.right;
        return Vector3.ProjectOnPlane(fallbackAxis, up).normalized;
    }

    static float NormalizeAngle(float angle)
    {
        return angle > 180f ? angle - 360f : angle;
    }

    void HandleLook()
    {
        float mouseX = Input.GetAxis("Mouse X") * lookSpeed;
        float mouseY = Input.GetAxis("Mouse Y") * lookSpeed;

        headingForward = Quaternion.AngleAxis(mouseX, smoothUp) * headingForward;
        headingForward = GetTangentForward(headingForward, smoothUp);
        pitch = Mathf.Clamp(pitch - mouseY, -80f, 80f);
    }

    void HandlePhysicsMovement()
    {
        Vector3 gravity = GetGravityAcceleration(body.position);
        float gravityMagnitude = gravity.magnitude;
        Vector3 up = gravityMagnitude > 0.0001f ? -gravity / gravityMagnitude : smoothUp;

        Vector3 waterProbe = body.position - up * Mathf.Max(0.2f, capsule.height * 0.25f);
        if (PlanetRiverSystem.TrySampleAny(waterProbe, out WaterSample water) && water.signedDistance < 0f)
        {
            IsSwimming = true;
            HandleSwimming(up, gravity, water);
            IsGrounded = false;
            return;
        }
        IsSwimming = false;

        activeGroundTraction = 1f;
        if (PlanetWeatherSystem.TrySample(body.position, out WeatherSnapshot weather))
        {
            activeGroundTraction = weather.groundTractionMultiplier;
            body.AddForce(weather.windVelocity * weatherWindInfluence, ForceMode.Acceleration);
        }

        IsGrounded = CheckRadialGround(up, out RaycastHit groundHit);
        if (IsGrounded && Vector3.Dot(body.velocity, groundHit.normal) > groundDetachSpeed)
            IsGrounded = false;

        Vector3 forward = GetTangentForward(headingForward, up);
        Vector3 right = Vector3.Cross(up, forward).normalized;
        Vector3 desiredDirection = right * moveInput.x + forward * moveInput.y;

        bool shouldJump = jumpQueued && gravityMagnitude > 0.0001f;
        jumpQueued = false;

        if (IsGrounded)
        {
            MoveOnGround(desiredDirection, groundHit.normal);

            if (shouldJump)
            {
                body.velocity += up * Mathf.Sqrt(jumpHeight * 2f * gravityMagnitude);
                IsGrounded = false;
            }
            else
            {
                // Adhesion follows the actual contact normal, so it cannot pull toward world X/Z zero.
                body.AddForce(-groundHit.normal * groundStickAcceleration, ForceMode.Acceleration);
            }
        }
        else
        {
            MoveInAir(desiredDirection, up);
            if (shouldJump)
                body.velocity += up * Mathf.Sqrt(jumpHeight * 2f * gravityMagnitude);

            body.AddForce(gravity, ForceMode.Acceleration);
        }
    }

    void HandleSwimming(Vector3 up, Vector3 gravity, WaterSample water)
    {
        bool exitJumpRequested = jumpQueued;
        jumpQueued = false;
        Vector3 forward = GetTangentForward(headingForward, up);
        Vector3 right = Vector3.Cross(up, forward).normalized;
        Vector3 desiredDirection = right * moveInput.x + forward * moveInput.y;
        Vector3 desiredVelocity = desiredDirection * swimSpeed + water.flowVelocity;
        if (Input.GetKey(KeyCode.Space))
            desiredVelocity += up * swimUpSpeed;

        float submerged = water.Submersion;
        body.velocity = Vector3.MoveTowards(
            body.velocity,
            desiredVelocity,
            swimAcceleration * Mathf.Max(0.25f, submerged) * Time.fixedDeltaTime);
        body.AddForce(-body.velocity * waterDrag * submerged, ForceMode.Acceleration);
        body.AddForce(gravity * (1f - submerged), ForceMode.Acceleration);
        body.AddForce(up * buoyancyAcceleration * submerged, ForceMode.Acceleration);

        if (exitJumpRequested && gravity.sqrMagnitude > 0.0001f)
        {
            float exitSpeed = Mathf.Sqrt(swimExitJumpHeight * 2f * gravity.magnitude);
            float currentExitSpeed = Vector3.Dot(body.velocity, up);
            if (currentExitSpeed < exitSpeed)
                body.velocity += up * (exitSpeed - currentExitSpeed);

            if (desiredDirection.sqrMagnitude > 0.0001f)
            {
                Vector3 exitDirection = desiredDirection.normalized;
                float currentForwardSpeed = Vector3.Dot(body.velocity, exitDirection);
                if (currentForwardSpeed < swimExitForwardSpeed)
                    body.velocity += exitDirection * (swimExitForwardSpeed - currentForwardSpeed);
            }
        }
    }

    void CheckGalaxyTransition()
    {
        if (!enableSpaceTravel || galaxyTransitionRequested || quadSphereWorld == null)
            return;

        Vector3 center = quadSphereWorld.GetPlanetCenterWorld();
        Vector3 fromCenter = body.position - center;
        float worldScale = Mathf.Max(
            Mathf.Abs(quadSphereWorld.transform.lossyScale.x),
            Mathf.Abs(quadSphereWorld.transform.lossyScale.y),
            Mathf.Abs(quadSphereWorld.transform.lossyScale.z));
        float altitude = fromCenter.magnitude - quadSphereWorld.PlanetRadius * worldScale;
        if (altitude < galaxyMapAltitude)
            return;

        Vector3 up = fromCenter.sqrMagnitude > 0.0001f ? fromCenter.normalized : transform.up;
        if (requireOutwardVelocity && Vector3.Dot(body.velocity, up) <= 0f)
            return;

        GalaxyTravelManager manager = GalaxyTravelManager.Instance;
        if (manager == null)
            return;

        galaxyTransitionRequested = true;
        manager.OpenGalaxyMap(quadSphereWorld);
    }

    void MoveOnGround(Vector3 desiredDirection, Vector3 groundNormal)
    {
        Vector3 desiredVelocity = Vector3.ProjectOnPlane(desiredDirection, groundNormal);
        if (desiredVelocity.sqrMagnitude > 0.0001f)
            desiredVelocity = desiredVelocity.normalized * moveSpeed;

        Vector3 surfaceVelocity = Vector3.ProjectOnPlane(body.velocity, groundNormal);
        body.velocity = Vector3.MoveTowards(
            surfaceVelocity,
            desiredVelocity,
            groundAcceleration * activeGroundTraction * Time.fixedDeltaTime
        );
    }

    void MoveInAir(Vector3 desiredDirection, Vector3 up)
    {
        Vector3 radialVelocity = up * Vector3.Dot(body.velocity, up);
        Vector3 lateralVelocity = Vector3.ProjectOnPlane(body.velocity, up);
        Vector3 desiredVelocity = desiredDirection * moveSpeed;

        lateralVelocity = Vector3.MoveTowards(
            lateralVelocity,
            desiredVelocity,
            airAcceleration * Time.fixedDeltaTime
        );
        body.velocity = lateralVelocity + radialVelocity;
    }

    bool CheckRadialGround(Vector3 up, out RaycastHit closestGround)
    {
        closestGround = default;

        Vector3 scale = transform.lossyScale;
        float radiusScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        float radius = capsule.radius * radiusScale;
        float height = Mathf.Max(capsule.height * Mathf.Abs(scale.y), radius * 2f);
        float probeRadius = Mathf.Max(radius * groundProbeRadiusScale, 0.01f);

        Vector3 center = transform.TransformPoint(capsule.center);
        Vector3 bottomSphereCenter = center - up * (height * 0.5f - radius);
        const float startOffset = 0.05f;
        Vector3 origin = bottomSphereCenter + up * startOffset;
        float castDistance = startOffset + groundProbeDistance;

        int hitCount = Physics.SphereCastNonAlloc(
            origin,
            probeRadius,
            -up,
            groundHits,
            castDistance,
            groundLayers,
            QueryTriggerInteraction.Ignore
        );

        float minimumGroundDot = Mathf.Cos(maxGroundAngle * Mathf.Deg2Rad);
        float closestDistance = float.PositiveInfinity;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = groundHits[i];
            Collider hitCollider = hit.collider;
            if (hitCollider == null || hitCollider.transform == transform || hitCollider.transform.IsChildOf(transform))
                continue;
            if (Vector3.Dot(hit.normal, up) < minimumGroundDot || hit.distance >= closestDistance)
                continue;

            closestDistance = hit.distance;
            closestGround = hit;
        }

        return closestDistance < float.PositiveInfinity;
    }

    void OnDrawGizmos()
    {
        if (!showGravityGizmo)
            return;

        Vector3 up = GetTargetUp(transform.position);
        Vector3 origin = transform.position;
        float length = Mathf.Max(gravityGizmoLength, 0.1f);

        Gizmos.color = Color.red;
        DrawGizmoArrow(origin, -up, length);
        Gizmos.color = Color.green;
        DrawGizmoArrow(origin, up, length * 0.5f);
    }

    static void DrawGizmoArrow(Vector3 origin, Vector3 direction, float length)
    {
        Vector3 normalizedDirection = direction.normalized;
        Vector3 end = origin + normalizedDirection * length;
        Gizmos.DrawLine(origin, end);

        Vector3 side = Vector3.Cross(normalizedDirection, Vector3.up);
        if (side.sqrMagnitude < 0.0001f)
            side = Vector3.Cross(normalizedDirection, Vector3.right);

        side.Normalize();
        Vector3 arrowBack = -normalizedDirection * (length * 0.25f);
        Vector3 arrowSide = side * (length * 0.12f);
        Gizmos.DrawLine(end, end + arrowBack + arrowSide);
        Gizmos.DrawLine(end, end + arrowBack - arrowSide);
    }
}
