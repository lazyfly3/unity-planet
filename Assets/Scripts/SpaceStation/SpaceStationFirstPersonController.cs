using UnityEngine;

namespace UnityPlanet.SpaceStation
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class SpaceStationFirstPersonController : MonoBehaviour
    {
        [Header("View")]
        [SerializeField] Camera viewCamera;
        [SerializeField, Min(0.1f)] float mouseSensitivity = 2.2f;
        [SerializeField] Vector2 pitchLimits = new Vector2(-82f, 82f);

        [Header("Movement")]
        [SerializeField, Min(0.1f)] float walkSpeed = 4.8f;
        [SerializeField, Min(0.1f)] float sprintSpeed = 7.2f;
        [SerializeField, Min(0f)] float jumpHeight = 1.05f;
        [SerializeField, Min(0.1f)] float gravity = 19.6f;
        [SerializeField] float resetBelowHeight = -8f;

        CharacterController characterController;
        Vector3 spawnPosition;
        Quaternion spawnRotation;
        float verticalSpeed;
        float pitch;
        bool cursorLocked;

        public CharacterController CharacterController => characterController;
        public float MouseSensitivity
        {
            get => mouseSensitivity;
            set => mouseSensitivity = Mathf.Clamp(value, 0.2f, 5f);
        }

        void Awake()
        {
            characterController = GetComponent<CharacterController>();
            if (viewCamera == null)
            {
                viewCamera = GetComponentInChildren<Camera>(true);
            }

            spawnPosition = transform.position;
            spawnRotation = transform.rotation;
            pitch = viewCamera != null ? NormalizedAngle(viewCamera.transform.localEulerAngles.x) : 0f;
            MouseSensitivity = PlayerPrefs.GetFloat(
                "MouseSensitivity",
                mouseSensitivity);
        }

        void OnEnable()
        {
            if (Application.isPlaying)
            {
                SetCursorLocked(true);
            }
        }

        void OnDisable()
        {
            SetCursorLocked(false);
        }

        void Update()
        {
            UpdateCursorState();
            UpdateView();
            UpdateMovement();

            if (Input.GetKeyDown(KeyCode.R) || transform.position.y < resetBelowHeight)
            {
                ResetToSpawn();
            }
        }

        void UpdateCursorState()
        {
            if (SpaceStationPauseMenu.IsOpen)
            {
                return;
            }
            if (Input.GetKeyDown(KeyCode.Escape) &&
                !SpaceStationPauseMenu.IsInstalled)
            {
                SetCursorLocked(!cursorLocked);
            }
            else if (!cursorLocked && Input.GetMouseButtonDown(0))
            {
                SetCursorLocked(true);
            }
        }

        void UpdateView()
        {
            if (!cursorLocked || viewCamera == null)
            {
                return;
            }

            float yawDelta = Input.GetAxisRaw("Mouse X") * mouseSensitivity;
            float pitchDelta = Input.GetAxisRaw("Mouse Y") * mouseSensitivity;
            transform.Rotate(0f, yawDelta, 0f, Space.Self);
            pitch = Mathf.Clamp(pitch - pitchDelta, pitchLimits.x, pitchLimits.y);
            viewCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        void UpdateMovement()
        {
            Vector2 input = Vector2.ClampMagnitude(new Vector2(
                Input.GetAxisRaw("Horizontal"),
                Input.GetAxisRaw("Vertical")), 1f);

            float speed = Input.GetKey(KeyCode.LeftShift) ? sprintSpeed : walkSpeed;
            Vector3 horizontalVelocity = (transform.right * input.x + transform.forward * input.y) * speed;

            if (characterController.isGrounded)
            {
                verticalSpeed = -2f;
                if (Input.GetButtonDown("Jump") && jumpHeight > 0f)
                {
                    verticalSpeed = Mathf.Sqrt(2f * gravity * jumpHeight);
                }
            }
            else
            {
                verticalSpeed -= gravity * Time.deltaTime;
            }

            Vector3 velocity = horizontalVelocity + Vector3.up * verticalSpeed;
            characterController.Move(velocity * Time.deltaTime);
        }

        public void ResetToSpawn()
        {
            bool wasEnabled = characterController.enabled;
            characterController.enabled = false;
            transform.SetPositionAndRotation(spawnPosition, spawnRotation);
            characterController.enabled = wasEnabled;
            verticalSpeed = 0f;
            pitch = 0f;

            if (viewCamera != null)
            {
                viewCamera.transform.localRotation = Quaternion.identity;
            }
        }

        void SetCursorLocked(bool locked)
        {
            cursorLocked = locked;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        static float NormalizedAngle(float angle)
        {
            return angle > 180f ? angle - 360f : angle;
        }
    }
}
