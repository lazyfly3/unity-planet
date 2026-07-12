using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class VoxelPlanetPlayerController : MonoBehaviour
{
    [SerializeField] VoxelWorld voxelWorld;
    [SerializeField] VoxelQuadSphereWorld quadSphereWorld;
    [SerializeField] BuildingPlacer buildingPlacer;
    [SerializeField] Transform cameraTransform;
    [SerializeField] float moveSpeed = 5f;
    [SerializeField] float lookSpeed = 2f;
    [SerializeField] float jumpHeight = 1.2f;
    [SerializeField] float upSmoothSpeed = 8f;

    CharacterController controller;
    Vector3 smoothUp = Vector3.up;
    Vector3 buildModeLockedUp;
    float yaw;
    float pitch;
    float radialVelocity;

    void Awake()
    {
        controller = GetComponent<CharacterController>();

        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;

        if (cameraTransform == transform)
        {
            Debug.LogError("VoxelPlanetPlayerController: Camera Transform 不能是玩家自身。");
            cameraTransform = null;
        }
    }

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        InitializeOrientation();
    }

    void Update()
    {
        UpdateBuildModeLock();
        UpdateSmoothUp();
        HandleLook();
        ApplyOrientation();
        HandleMove();
    }

    void UpdateBuildModeLock()
    {
        if (buildingPlacer == null)
            buildingPlacer = GetComponent<BuildingPlacer>();

        if (buildingPlacer != null && buildingPlacer.IsBuildMode)
        {
            if (buildModeLockedUp.sqrMagnitude < 0.0001f)
                buildModeLockedUp = smoothUp.normalized;
            return;
        }

        buildModeLockedUp = Vector3.zero;
    }

    void InitializeOrientation()
    {
        smoothUp = GetTargetUp();
        InitializeYawFromForward();
        ApplyOrientation();
    }

    void UpdateSmoothUp()
    {
        if (buildingPlacer != null && buildingPlacer.IsBuildMode)
        {
            if (buildModeLockedUp.sqrMagnitude > 0.0001f)
                smoothUp = buildModeLockedUp;
            return;
        }

        Vector3 targetUp = GetTargetUp();
        if (smoothUp.sqrMagnitude < 0.0001f)
            smoothUp = targetUp;

        float blend = 1f - Mathf.Exp(-upSmoothSpeed * Time.deltaTime);
        smoothUp = Vector3.Slerp(smoothUp, targetUp, blend).normalized;
    }

    Vector3 GetTargetUp()
    {
        if (quadSphereWorld != null)
            return PlanetGravity.GetUp(transform.position, quadSphereWorld.GetPlanetCenterWorld());

        if (voxelWorld == null || !voxelWorld.UsePlanetGeneration)
            return Vector3.up;

        return PlanetGravity.GetUp(transform.position, voxelWorld.GetPlanetCenterWorld());
    }

    void InitializeYawFromForward()
    {
        Vector3 up = smoothUp;
        Vector3 referenceForward = GetReferenceForward(up);
        Vector3 projectedForward = Vector3.ProjectOnPlane(transform.forward, up);

        if (projectedForward.sqrMagnitude < 0.0001f)
            yaw = 0f;
        else
            yaw = Vector3.SignedAngle(referenceForward, projectedForward.normalized, up);
    }

    static Vector3 GetReferenceForward(Vector3 up)
    {
        Vector3 referenceForward = Vector3.Cross(up, Vector3.up);
        if (referenceForward.sqrMagnitude < 0.0001f)
            referenceForward = Vector3.Cross(up, Vector3.forward);

        return referenceForward.normalized;
    }

    void ApplyOrientation()
    {
        Vector3 up = smoothUp;
        Vector3 forward = Quaternion.AngleAxis(yaw, up) * GetReferenceForward(up);
        transform.rotation = Quaternion.LookRotation(forward, up);

        if (cameraTransform != null)
            cameraTransform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    Vector3 GetGravityAcceleration()
    {
        if (quadSphereWorld != null)
        {
            return PlanetGravity.GetGravitationalAcceleration(
                transform.position,
                quadSphereWorld.GetPlanetCenterWorld(),
                quadSphereWorld.GravitationalParameter
            );
        }

        if (voxelWorld == null || !voxelWorld.UsePlanetGeneration)
            return Vector3.down * 9.8f;

        return PlanetGravity.GetGravitationalAcceleration(
            transform.position,
            voxelWorld.GetPlanetCenterWorld(),
            voxelWorld.GravitationalParameter
        );
    }

    void HandleLook()
    {
        float mouseX = Input.GetAxis("Mouse X") * lookSpeed;
        float mouseY = Input.GetAxis("Mouse Y") * lookSpeed;

        yaw += mouseX;
        pitch = Mathf.Clamp(pitch - mouseY, -80f, 80f);
    }

    void HandleMove()
    {
        Vector3 gravityAccel = GetGravityAcceleration();
        float g = gravityAccel.magnitude;
        Vector3 down = g > 0.0001f ? gravityAccel / g : Vector3.down;
        Vector3 up = smoothUp;

        if (Input.GetKeyDown(KeyCode.Space) && g > 0.0001f)
            radialVelocity = Mathf.Sqrt(jumpHeight * 2f * g);

        if (controller.isGrounded && radialVelocity <= 0f)
            radialVelocity = 0f;
        else if (g > 0.0001f)
            radialVelocity -= g * Time.deltaTime;

        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");
        Vector3 tangentMove = transform.right * horizontal + transform.forward * vertical;
        tangentMove = Vector3.ProjectOnPlane(tangentMove, up);

        if (tangentMove.sqrMagnitude > 1f)
            tangentMove.Normalize();

        tangentMove *= moveSpeed;
        Vector3 move = tangentMove + up * radialVelocity;

        controller.Move(move * Time.deltaTime);
    }
}
