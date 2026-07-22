using SpacecraftEditor;
using UnityEngine;

[DefaultExecutionOrder(800)]
[DisallowMultipleComponent]
public sealed class InterstellarCameraRig : MonoBehaviour
{
    [SerializeField] Transform target;
    [SerializeField] InterstellarShipController ship;
    [SerializeField] InterstellarFlightRuntime runtime;
    [SerializeField] SpacecraftDamageReceiver damageReceiver;
    [SerializeField] Camera targetCamera;
    [SerializeField] Vector3 localOffset = new Vector3(0f, 4.2f, -13f);
    [SerializeField] Vector3 localLookOffset = new Vector3(0f, 1.2f, 5f);
    [SerializeField, Min(0f)] float rotationSharpness = 14f;
    [SerializeField, Range(0f, 1f)] float rollFollow = 0.75f;
    [SerializeField, Min(0f)] float accelerationSetback = 0.006f;
    [SerializeField, Min(0f)] float maximumSetback = 0.8f;
    [SerializeField, Min(0f)] float localRecoilSharpness = 8f;
    [SerializeField] Vector2 fieldOfViewRange = new Vector2(60f, 76f);
    [SerializeField, Min(1f)] float maximumFovSpeed = 1800f;
    [SerializeField, Min(0f)] float freeLookSensitivity = 2.2f;

    Rigidbody targetBody;
    Vector3 previousVelocity;
    Vector3 localRecoil;
    Vector3 collisionKick;
    Vector2 freeLookAngles;
    bool velocityInitialized;

    void Awake()
    {
        ResolveReferences();
    }

    void OnEnable()
    {
        ResolveReferences();
        if (runtime != null)
            runtime.OriginShifted += HandleOriginShift;
        if (damageReceiver != null)
            damageReceiver.Damaged += HandleDamaged;
    }

    void Start()
    {
        SnapToTarget();
    }

    void LateUpdate()
    {
        if (target == null)
            return;

        UpdateLocalEffects();
        Quaternion freeLookRotation = Quaternion.Euler(freeLookAngles.x, freeLookAngles.y, 0f);
        Vector3 offset = localOffset + localRecoil + collisionKick;
        Vector3 cameraPosition = target.position + target.rotation * (freeLookRotation * offset);
        transform.position = cameraPosition;

        Vector3 lookPoint = target.TransformPoint(localLookOffset);
        Vector3 lookDirection = lookPoint - cameraPosition;
        if (lookDirection.sqrMagnitude < 0.0001f)
            lookDirection = target.forward;
        Vector3 blendedUp = Vector3.Slerp(transform.up, target.up, rollFollow).normalized;
        Quaternion desiredRotation = Quaternion.LookRotation(lookDirection.normalized, blendedUp);
        float blend = 1f - Mathf.Exp(-rotationSharpness * Time.unscaledDeltaTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, blend);

        if (targetCamera != null)
        {
            float speed = targetBody == null ? 0f : targetBody.velocity.magnitude;
            float ratio = Mathf.Clamp01(speed / maximumFovSpeed);
            float desiredFov = Mathf.Lerp(fieldOfViewRange.x, fieldOfViewRange.y, ratio);
            targetCamera.fieldOfView = Mathf.Lerp(
                targetCamera.fieldOfView,
                desiredFov,
                1f - Mathf.Exp(-5f * Time.unscaledDeltaTime));
        }
    }

    void UpdateLocalEffects()
    {
        float deltaTime = Mathf.Max(0.0001f, Time.unscaledDeltaTime);
        Vector3 velocity = targetBody == null ? Vector3.zero : targetBody.velocity;
        Vector3 localAcceleration = Vector3.zero;
        if (velocityInitialized)
            localAcceleration = target.InverseTransformDirection((velocity - previousVelocity) / deltaTime);
        previousVelocity = velocity;
        velocityInitialized = true;

        Vector3 desiredRecoil = new Vector3(
            Mathf.Clamp(-localAcceleration.x * accelerationSetback * 0.2f, -maximumSetback * 0.25f, maximumSetback * 0.25f),
            Mathf.Clamp(-localAcceleration.y * accelerationSetback * 0.2f, -maximumSetback * 0.25f, maximumSetback * 0.25f),
            Mathf.Clamp(-localAcceleration.z * accelerationSetback, -maximumSetback, maximumSetback));
        localRecoil = Vector3.Lerp(
            localRecoil,
            desiredRecoil,
            1f - Mathf.Exp(-localRecoilSharpness * deltaTime));
        collisionKick = Vector3.Lerp(collisionKick, Vector3.zero, 1f - Mathf.Exp(-12f * deltaTime));

        KeyboardMouseFlightInput input = ship == null ? null : ship.FlightInput;
        if (input != null && input.FreeLookHeld)
        {
            freeLookAngles.y += Input.GetAxisRaw("Mouse X") * freeLookSensitivity;
            freeLookAngles.x = Mathf.Clamp(
                freeLookAngles.x - Input.GetAxisRaw("Mouse Y") * freeLookSensitivity,
                -65f,
                65f);
        }
        else
        {
            freeLookAngles = Vector2.Lerp(
                freeLookAngles,
                Vector2.zero,
                1f - Mathf.Exp(-7f * deltaTime));
        }
    }

    public void SnapToTarget()
    {
        ResolveReferences();
        if (target == null)
            return;
        localRecoil = Vector3.zero;
        collisionKick = Vector3.zero;
        freeLookAngles = Vector2.zero;
        transform.position = target.TransformPoint(localOffset);
        Vector3 lookPoint = target.TransformPoint(localLookOffset);
        transform.rotation = Quaternion.LookRotation(lookPoint - transform.position, target.up);
        previousVelocity = targetBody == null ? Vector3.zero : targetBody.velocity;
        velocityInitialized = true;
    }

    void ResolveReferences()
    {
        if (ship == null)
            ship = FindObjectOfType<InterstellarShipController>();
        if (target == null && ship != null)
            target = ship.transform;
        if (targetBody == null && ship != null)
            targetBody = ship.GetComponent<Rigidbody>();
        if (runtime == null)
            runtime = FindObjectOfType<InterstellarFlightRuntime>();
        if (damageReceiver == null && ship != null)
            damageReceiver = ship.GetComponent<SpacecraftDamageReceiver>();
        if (targetCamera == null)
            targetCamera = GetComponentInChildren<Camera>(true) ?? Camera.main;
    }

    void HandleOriginShift(Vector3 shift)
    {
        transform.position -= shift;
        SnapToTarget();
    }

    void HandleDamaged(float integrity, float maximumIntegrity, SpaceDamageInfo damage)
    {
        float strength = Mathf.Clamp(damage.amount * 0.025f, 0.05f, 0.45f);
        collisionKick += Random.insideUnitSphere * strength;
    }

    void OnDisable()
    {
        if (runtime != null)
            runtime.OriginShifted -= HandleOriginShift;
        if (damageReceiver != null)
            damageReceiver.Damaged -= HandleDamaged;
    }
}
