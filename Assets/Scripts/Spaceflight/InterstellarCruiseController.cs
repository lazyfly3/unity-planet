using SpacecraftEditor;
using UnityEngine;

[DefaultExecutionOrder(-150)]
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class InterstellarCruiseController : MonoBehaviour
{
    [SerializeField] Rigidbody shipBody;
    [SerializeField] InterstellarNavigationSystem navigation;
    [SerializeField] SpacecraftIfcsMotor ifcsMotor;
    [SerializeField] KeyboardMouseFlightInput flightInput;
    [SerializeField] SpacecraftDamageReceiver damageReceiver;
    [SerializeField, Min(0.1f)] float spoolDuration = 1.15f;
    [SerializeField, Min(1f)] float cruiseAcceleration = 240f;
    [SerializeField, Min(10f)] float maximumCruiseSpeed = 4200f;
    [SerializeField, Min(1f)] float finalApproachSpeed = 180f;
    [SerializeField, Range(0f, 30f)] float activationAngle = 7f;
    [SerializeField, Range(1f, 45f)] float abortAngle = 10f;
    [SerializeField, Min(0f)] float cooldownDuration = 1.2f;

    InterstellarCruiseState state;
    float stateTime;
    bool controlsEnabled = true;

    public InterstellarCruiseState State => state;
    public bool IsActive => state != InterstellarCruiseState.Inactive
        && state != InterstellarCruiseState.Cooldown;
    public float MaximumCruiseSpeed => maximumCruiseSpeed;
    public bool ControlsEnabled
    {
        get => controlsEnabled;
        set
        {
            controlsEnabled = value;
            if (!value)
                Abort();
        }
    }

    void Awake()
    {
        finalApproachSpeed = Mathf.Min(finalApproachSpeed, 35f);
        ResolveReferences();
    }

    void OnEnable()
    {
        ResolveReferences();
        if (damageReceiver != null)
            damageReceiver.Damaged += HandleDamaged;
    }

    void Update()
    {
        if (!controlsEnabled || flightInput == null)
            return;
        if (!flightInput.ConsumeCruisePressed())
            return;

        if (IsActive)
        {
            Abort();
            return;
        }
        TryBeginCruise();
    }

    void FixedUpdate()
    {
        if (!controlsEnabled || shipBody == null)
            return;

        stateTime += Time.fixedDeltaTime;
        switch (state)
        {
            case InterstellarCruiseState.Inactive:
                return;
            case InterstellarCruiseState.Spooling:
                if (stateTime >= spoolDuration)
                    SetState(InterstellarCruiseState.Accelerating);
                return;
            case InterstellarCruiseState.Accelerating:
            case InterstellarCruiseState.Cruising:
            case InterstellarCruiseState.Decelerating:
                ApplyCruisePhysics();
                return;
            case InterstellarCruiseState.Cooldown:
                if (stateTime >= cooldownDuration)
                    SetState(InterstellarCruiseState.Inactive);
                return;
        }
    }

    void ApplyCruisePhysics()
    {
        if (navigation == null || !navigation.HasTarget)
        {
            Abort();
            return;
        }

        Vector3 direction = navigation.DirectionToTarget;
        if (direction.sqrMagnitude < 0.001f)
        {
            Abort();
            return;
        }
        direction.Normalize();
        float angle = Vector3.Angle(transform.forward, direction);
        if (angle > abortAngle)
        {
            Abort();
            return;
        }

        float forwardSpeed = Vector3.Dot(shipBody.velocity, direction);
        float stoppingDistance = Mathf.Max(0f,
            (forwardSpeed * forwardSpeed - finalApproachSpeed * finalApproachSpeed)
            / (2f * Mathf.Max(1f, cruiseAcceleration)));
        bool shouldBrake = navigation.TargetDistance <= stoppingDistance + 900f;

        if (shouldBrake && state != InterstellarCruiseState.Decelerating)
            SetState(InterstellarCruiseState.Decelerating);
        else if (!shouldBrake && state == InterstellarCruiseState.Accelerating
                 && forwardSpeed >= maximumCruiseSpeed * 0.98f)
            SetState(InterstellarCruiseState.Cruising);

        float acceleration = 0f;
        if (state == InterstellarCruiseState.Accelerating)
            acceleration = forwardSpeed < maximumCruiseSpeed ? cruiseAcceleration : 0f;
        else if (state == InterstellarCruiseState.Cruising)
            acceleration = Mathf.Clamp((maximumCruiseSpeed - forwardSpeed) * 0.8f, -cruiseAcceleration, cruiseAcceleration);
        else if (state == InterstellarCruiseState.Decelerating)
        {
            Vector3 relativeVelocity = shipBody.velocity - direction * finalApproachSpeed;
            if (relativeVelocity.magnitude <= 3f || forwardSpeed <= finalApproachSpeed)
            {
                SetState(InterstellarCruiseState.Cooldown);
                return;
            }
            shipBody.AddForce(-relativeVelocity.normalized * cruiseAcceleration, ForceMode.Acceleration);
            return;
        }

        if (Mathf.Abs(acceleration) > 0.001f)
            shipBody.AddForce(direction * acceleration, ForceMode.Acceleration);
    }

    void TryBeginCruise()
    {
        if (state == InterstellarCruiseState.Cooldown || navigation == null || !navigation.HasTarget)
            return;
        Vector3 direction = navigation.DirectionToTarget;
        if (direction.sqrMagnitude < 0.001f
            || Vector3.Angle(transform.forward, direction) > activationAngle)
            return;
        SetState(InterstellarCruiseState.Spooling);
    }

    public void Abort()
    {
        if (state == InterstellarCruiseState.Inactive)
            return;
        SetState(InterstellarCruiseState.Cooldown);
    }

    void SetState(InterstellarCruiseState next)
    {
        state = next;
        stateTime = 0f;
        if (ifcsMotor != null)
            ifcsMotor.LinearControlEnabled = next == InterstellarCruiseState.Inactive
                || next == InterstellarCruiseState.Cooldown;
    }

    void ResolveReferences()
    {
        if (shipBody == null)
            shipBody = GetComponent<Rigidbody>();
        if (navigation == null)
            navigation = FindObjectOfType<InterstellarNavigationSystem>();
        if (ifcsMotor == null)
            ifcsMotor = GetComponent<SpacecraftIfcsMotor>();
        if (flightInput == null)
            flightInput = GetComponent<KeyboardMouseFlightInput>();
        if (damageReceiver == null)
            damageReceiver = GetComponent<SpacecraftDamageReceiver>();
    }

    void HandleDamaged(float integrity, float maximumIntegrity, SpaceDamageInfo damage)
    {
        Abort();
    }

    void OnDisable()
    {
        if (damageReceiver != null)
            damageReceiver.Damaged -= HandleDamaged;
        state = InterstellarCruiseState.Inactive;
        if (ifcsMotor != null)
            ifcsMotor.LinearControlEnabled = true;
    }
}
