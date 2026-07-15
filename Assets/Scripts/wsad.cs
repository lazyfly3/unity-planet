using UnityEngine;

public class wsad : MonoBehaviour
{
    [SerializeField] Transform cameraTransform;
    [SerializeField] float moveSpeed = 5f;
    [SerializeField] float lookSpeed = 2f;

    float pitch;
    Rigidbody rb;
    CharacterController characterController;

    public Transform CameraTransform => cameraTransform;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        characterController = GetComponent<CharacterController>();

        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;

        if (cameraTransform == transform)
        {
            Debug.LogError("wsad: Camera Transform 不能填玩家自身！请把 Main Camera 拖成 Player 的子物体，再把相机拖到 Camera Transform。");
            cameraTransform = null;
        }
        else if (cameraTransform != null && !cameraTransform.IsChildOf(transform))
        {
            Debug.LogWarning("wsad: Main Camera 不是玩家的子物体，上下看可能异常。建议层级：Player → Main Camera。");
        }

        if (rb != null)
        {
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
        }
    }

    void Update()
    {
        HandleLook();

        if (rb == null)
            HandleMove(Time.deltaTime);
    }

    void FixedUpdate()
    {
        if (rb != null)
            HandleMove(Time.fixedDeltaTime);
    }

    public void SetPitch(float value)
    {if(FSPDebuger.EnableLogTrackInternal){FSPDebuger.PushDepth();FSPDebuger.LogTrack(1, (int)value);}
        pitch = Mathf.Clamp(value, -80f, 80f);
        ApplyCameraPitch();
    
    if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.PopDepth();}

    void HandleMove(float deltaTime)
    {
        float speed = moveSpeed * deltaTime;
        Vector3 move = Vector3.zero;

        if (Input.GetKey(KeyCode.W))
            move += transform.forward;
        if (Input.GetKey(KeyCode.S))
            move -= transform.forward;
        if (Input.GetKey(KeyCode.A))
            move -= transform.right;
        if (Input.GetKey(KeyCode.D))
            move += transform.right;
        if (Input.GetKey(KeyCode.Space))
            move += Vector3.up;

        if (move.sqrMagnitude < 0.0001f)
            return;

        move = move.normalized * speed;

        if (characterController != null)
            characterController.Move(move);
        else if (rb != null)
            rb.MovePosition(rb.position + move);
        else
            transform.position += move;
    }

    void HandleLook()
    {
        if (!Input.GetMouseButton(1) || cameraTransform == null)
            return;

        float mouseX = Input.GetAxis("Mouse X") * lookSpeed;
        float mouseY = Input.GetAxis("Mouse Y") * lookSpeed;

        ApplyYaw(mouseX);

        pitch -= mouseY;
        pitch = Mathf.Clamp(pitch, -80f, 80f);
        ApplyCameraPitch();
    }

    void ApplyYaw(float mouseX)
    {
        if (Mathf.Abs(mouseX) < 0.0001f)
            return;

        if (rb != null)
            rb.MoveRotation(rb.rotation * Quaternion.Euler(0f, mouseX, 0f));
        else
            transform.Rotate(0f, mouseX, 0f, Space.World);
    }

    void ApplyCameraPitch()
    {
        if (cameraTransform == null || cameraTransform == transform)
            return;

        cameraTransform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }
}
