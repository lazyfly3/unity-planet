using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public sealed class PlanetLabFirstPersonController : MonoBehaviour
{
    [SerializeField] Transform cameraTransform;
    [SerializeField, Min(0.1f)] float moveSpeed = 6f;
    [SerializeField, Min(0.1f)] float lookSpeed = 2f;
    [SerializeField, Min(0.1f)] float jumpHeight = 1.2f;
    [SerializeField, Min(0.1f)] float gravity = 9.81f;
    [SerializeField, Min(1f)] float resetFallDistance = 200f;

    CharacterController characterController;
    Vector3 spawnPosition;
    float verticalVelocity;
    float pitch;
    bool infiniteWorld;

    public Vector3 SpawnPosition => spawnPosition;

    public void Configure(Transform valueCameraTransform, Vector3 valueSpawnPosition)
    {
        cameraTransform = valueCameraTransform;
        SetSpawnPosition(valueSpawnPosition, true);
    }

    public void SetSpawnPosition(Vector3 value, bool resetPlayer)
    {
        spawnPosition = value;
        if (resetPlayer)
            ResetToSpawn();
    }

    public void SetInfiniteWorld(bool value)
    {
        infiniteWorld = value;
    }

    public void ResetToSpawn()
    {
        if (characterController == null)
            characterController = GetComponent<CharacterController>();
        bool wasEnabled = characterController.enabled;
        characterController.enabled = false;
        transform.SetPositionAndRotation(
            spawnPosition,
            Quaternion.LookRotation(Vector3.forward, Vector3.up));
        characterController.enabled = wasEnabled;
        verticalVelocity = 0f;
        pitch = 0f;
        if (cameraTransform != null)
            cameraTransform.localRotation = Quaternion.identity;
    }

    void Awake()
    {
        characterController = GetComponent<CharacterController>();
    }

    void OnEnable()
    {
        if (!Application.isPlaying)
            return;
        LockCursor();
    }

    void OnDisable()
    {
        if (!Application.isPlaying)
            return;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            bool shouldLock = Cursor.lockState != CursorLockMode.Locked;
            Cursor.lockState = shouldLock
                ? CursorLockMode.Locked
                : CursorLockMode.None;
            Cursor.visible = !shouldLock;
        }
        if (Input.GetMouseButtonDown(0)
            && Cursor.lockState != CursorLockMode.Locked)
        {
            LockCursor();
        }
        if (Input.GetKeyDown(KeyCode.R))
            ResetToSpawn();

        HandleLook();
        HandleMovement();

        if (transform.position.y < spawnPosition.y - resetFallDistance
            || (!infiniteWorld
                && (Mathf.Abs(transform.position.x) > PlanetLabPlanarSettings.PatchSize
                    || Mathf.Abs(transform.position.z)
                    > PlanetLabPlanarSettings.PatchSize)))
        {
            ResetToSpawn();
        }
    }

    void HandleLook()
    {
        if (cameraTransform == null
            || Cursor.lockState != CursorLockMode.Locked)
        {
            return;
        }

        float mouseX = Input.GetAxis("Mouse X") * lookSpeed;
        float mouseY = Input.GetAxis("Mouse Y") * lookSpeed;
        transform.Rotate(Vector3.up, mouseX, Space.World);
        pitch = Mathf.Clamp(pitch - mouseY, -80f, 80f);
        cameraTransform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    void HandleMovement()
    {
        Vector2 input = Vector2.ClampMagnitude(new Vector2(
            Input.GetAxisRaw("Horizontal"),
            Input.GetAxisRaw("Vertical")), 1f);
        Vector3 planarVelocity =
            (transform.right * input.x + transform.forward * input.y) * moveSpeed;

        if (characterController.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f;
        if (characterController.isGrounded && Input.GetKeyDown(KeyCode.Space))
            verticalVelocity = Mathf.Sqrt(jumpHeight * 2f * gravity);
        verticalVelocity -= gravity * Time.deltaTime;

        Vector3 velocity = planarVelocity + Vector3.up * verticalVelocity;
        characterController.Move(velocity * Time.deltaTime);
    }

    static void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
}
