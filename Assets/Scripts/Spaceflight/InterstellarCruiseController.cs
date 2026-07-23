using SpacecraftEditor;
using UnityEngine;

[DefaultExecutionOrder(-150)]
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class InterstellarCruiseController : MonoBehaviour
{
    const double DefaultExitDistance = 14000d;
    const float DefaultExitSpeed = 120f;

    [SerializeField] Rigidbody shipBody;
    [SerializeField] InterstellarNavigationSystem navigation;
    [SerializeField] InterstellarFlightRuntime flightRuntime;
    [SerializeField] SpacecraftIfcsMotor ifcsMotor;
    [SerializeField] KeyboardMouseFlightInput flightInput;
    [SerializeField] SpacecraftDamageReceiver damageReceiver;
    [SerializeField] SpacecraftWeaponSystem weaponSystem;
    [SerializeField, Range(1f, 20f)] float reticleLockAngle = 10f;
    [SerializeField, Range(0.5f, 10f)] float alignmentAngle = 3f;
    [SerializeField, Range(1f, 30f)] float abortAngle = 10f;
    [SerializeField, Min(0.1f)] float spoolDuration = 1.5f;
    [SerializeField, Min(0.2f)] float transitDuration = 1.2f;
    [SerializeField, Min(0f)] float exitDuration = 0.28f;
    [SerializeField, Min(0f)] float cooldownDuration = 1.2f;
    [SerializeField, Min(1000f)] float exitDistance = (float)DefaultExitDistance;
    [SerializeField, Min(1f)] float exitSpeed = DefaultExitSpeed;
    [SerializeField, Min(1000f)] float automaticWarpMinimumDistance = 20000f;
    [SerializeField, Min(10f)] float automaticApproachMaximumSpeed = 220f;

    InterstellarWarpState warpState = InterstellarWarpState.Unlocked;
    InterstellarWarpCancelReason cancelReason;
    float stateTime;
    float alignmentError;
    bool controlsEnabled = true;
    bool relocated;
    bool automaticLandingRequested;

    public InterstellarWarpState WarpState => warpState;
    public InterstellarWarpCancelReason CancelReason => cancelReason;
    public float AlignmentError => alignmentError;
    public float WarpProgress => CalculateProgress();
    public float ExitDistance => exitDistance;
    public float ExitSpeed => exitSpeed;
    public float WarpVisualIntensity => CalculateVisualIntensity();
    public bool AutomaticLandingRequested => automaticLandingRequested;
    public bool IsActive => warpState == InterstellarWarpState.Aligning
        || warpState == InterstellarWarpState.Spooling
        || warpState == InterstellarWarpState.Transit
        || warpState == InterstellarWarpState.Exiting;
    public float MaximumCruiseSpeed => exitSpeed;
    public InterstellarCruiseState State => MapLegacyState(warpState);

    public bool ControlsEnabled
    {
        get => controlsEnabled;
        set
        {
            controlsEnabled = value;
            if (!value)
                CancelWarp(InterstellarWarpCancelReason.ControlsDisabled);
        }
    }

    void Awake()
    {
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
        SynchronizeLockState();
        if (controlsEnabled && Input.GetKeyDown(KeyCode.L))
            ToggleAutomaticLanding();
        if (!controlsEnabled || flightInput == null || !flightInput.ConsumeCruisePressed())
            return;

        if (warpState == InterstellarWarpState.Aligning || warpState == InterstellarWarpState.Spooling)
        {
            SetAutomaticLandingRequested(false);
            CancelWarp(InterstellarWarpCancelReason.Manual);
            return;
        }
        if (warpState == InterstellarWarpState.Transit || warpState == InterstellarWarpState.Exiting
            || warpState == InterstellarWarpState.Cooldown)
            return;

        if (warpState == InterstellarWarpState.Unlocked)
        {
            cancelReason = InterstellarWarpCancelReason.None;
            if (navigation != null && navigation.TryLockReticleTarget(Camera.main, reticleLockAngle))
                SetWarpState(InterstellarWarpState.Locked);
            else
                cancelReason = InterstellarWarpCancelReason.NoReticleTarget;
            return;
        }

        BeginAlignment();
    }

    void FixedUpdate()
    {
        if (!controlsEnabled || shipBody == null)
            return;

        stateTime += Time.fixedDeltaTime;
        switch (warpState)
        {
            case InterstellarWarpState.Aligning:
                UpdateAlignment(false);
                break;
            case InterstellarWarpState.Spooling:
                UpdateAlignment(true);
                break;
            case InterstellarWarpState.Transit:
                UpdateTransit();
                break;
            case InterstellarWarpState.Exiting:
                if (stateTime >= exitDuration)
                    SetWarpState(InterstellarWarpState.Cooldown);
                break;
            case InterstellarWarpState.Cooldown:
                if (stateTime >= cooldownDuration)
                    SetWarpState(navigation != null && navigation.HasLockedTarget
                        ? InterstellarWarpState.Locked
                        : InterstellarWarpState.Unlocked);
                break;
        }

        if (automaticLandingRequested && warpState == InterstellarWarpState.Locked)
            ApplyAutomaticApproach();
    }

    void ToggleAutomaticLanding()
    {
        if (automaticLandingRequested)
        {
            CancelAutomaticLanding();
            return;
        }

        BeginAutomaticLanding();
    }

    public bool BeginAutomaticLanding()
    {
        if (navigation == null)
            return false;
        if (!navigation.HasLockedTarget
            && !navigation.TryLockReticleTarget(Camera.main, reticleLockAngle)
            && !navigation.LockNextTarget())
        {
            cancelReason = InterstellarWarpCancelReason.NoReticleTarget;
            return false;
        }

        SetAutomaticLandingRequested(true);
        SynchronizeLockState();
        if (warpState == InterstellarWarpState.Locked
            && navigation.TargetDistance > automaticWarpMinimumDistance)
            BeginAlignment();
        return true;
    }

    public void CancelAutomaticLanding()
    {
        SetAutomaticLandingRequested(false);
        if (warpState == InterstellarWarpState.Aligning || warpState == InterstellarWarpState.Spooling)
            CancelWarp(InterstellarWarpCancelReason.Manual);
    }

    void ApplyAutomaticApproach()
    {
        if (navigation == null || !navigation.HasLockedTarget || ifcsMotor == null)
        {
            SetAutomaticLandingRequested(false);
            return;
        }

        double remainingDistance = navigation.TargetDistance - navigation.SelectedApproachBoundaryDistance;
        if (remainingDistance > automaticWarpMinimumDistance)
        {
            BeginAlignment();
            return;
        }

        Vector3 direction = navigation.DirectionToTarget;
        if (direction.sqrMagnitude < 0.0001f)
            return;
        direction.Normalize();
        float targetSpeed = Mathf.Clamp(
            (float)remainingDistance * 0.065f + 28f,
            28f,
            automaticApproachMaximumSpeed);
        Vector3 up = Vector3.ProjectOnPlane(transform.up, direction);
        if (up.sqrMagnitude < 0.0001f)
            up = Vector3.ProjectOnPlane(Vector3.up, direction);
        if (up.sqrMagnitude < 0.0001f)
            up = Vector3.right;

        ifcsMotor.LinearControlEnabled = true;
        ifcsMotor.SetExternalWorldVelocityTarget(direction * targetSpeed);
        ifcsMotor.SetExternalWorldAttitudeTarget(Quaternion.LookRotation(direction, up.normalized));
        if (weaponSystem != null)
            weaponSystem.ControlsEnabled = false;
    }

    void SetAutomaticLandingRequested(bool requested)
    {
        automaticLandingRequested = requested;
        navigation?.RequestAutomaticLanding(requested);
        if (requested)
            return;
        if (ifcsMotor != null && !IsActive)
        {
            ifcsMotor.ClearExternalTargets();
            ifcsMotor.ResetControllerState();
        }
        if (weaponSystem != null && !IsActive)
            weaponSystem.ControlsEnabled = controlsEnabled;
    }

    void SynchronizeLockState()
    {
        if (IsActive || warpState == InterstellarWarpState.Cooldown)
            return;
        bool hasTarget = navigation != null && navigation.HasLockedTarget;
        if (hasTarget && warpState == InterstellarWarpState.Unlocked)
            SetWarpState(InterstellarWarpState.Locked);
        else if (!hasTarget && warpState == InterstellarWarpState.Locked)
            SetWarpState(InterstellarWarpState.Unlocked);
    }

    void BeginAlignment()
    {
        if (navigation == null || !navigation.HasLockedTarget)
        {
            cancelReason = InterstellarWarpCancelReason.TargetLost;
            SetWarpState(InterstellarWarpState.Unlocked);
            return;
        }
        cancelReason = InterstellarWarpCancelReason.None;
        SetWarpState(InterstellarWarpState.Aligning);
    }

    void UpdateAlignment(bool spooling)
    {
        Vector3 direction = navigation == null ? Vector3.zero : navigation.DirectionToTarget;
        if (direction.sqrMagnitude < 0.0001f || !navigation.HasLockedTarget)
        {
            CancelWarp(InterstellarWarpCancelReason.TargetLost);
            return;
        }

        direction.Normalize();
        alignmentError = Vector3.Angle(transform.forward, direction);
        if (spooling && alignmentError > abortAngle)
        {
            CancelWarp(InterstellarWarpCancelReason.Misaligned);
            return;
        }

        Vector3 up = Vector3.ProjectOnPlane(transform.up, direction);
        if (up.sqrMagnitude < 0.0001f)
            up = Vector3.ProjectOnPlane(Vector3.up, direction);
        if (up.sqrMagnitude < 0.0001f)
            up = Vector3.right;
        if (ifcsMotor != null)
        {
            ifcsMotor.LinearControlEnabled = true;
            ifcsMotor.SetExternalWorldVelocityTarget(Vector3.zero);
            ifcsMotor.SetExternalWorldAttitudeTarget(Quaternion.LookRotation(direction, up.normalized));
        }

        if (!spooling && alignmentError <= alignmentAngle)
            SetWarpState(InterstellarWarpState.Spooling);
        else if (spooling && stateTime >= spoolDuration)
            BeginTransit();
    }

    void BeginTransit()
    {
        if (navigation == null || !navigation.HasLockedTarget || flightRuntime == null)
        {
            CancelWarp(InterstellarWarpCancelReason.TargetLost);
            return;
        }
        flightRuntime.SaveState(true);
        relocated = false;
        SetWarpState(InterstellarWarpState.Transit);
    }

    void UpdateTransit()
    {
        if (!relocated && stateTime >= transitDuration * 0.55f)
        {
            PerformRelocation();
            relocated = true;
        }
        if (stateTime >= transitDuration)
            SetWarpState(InterstellarWarpState.Exiting);
    }

    void PerformRelocation()
    {
        if (flightRuntime == null || navigation == null || !navigation.HasLockedTarget)
            return;
        DoubleVector3 origin = flightRuntime.ShipUniversePosition;
        DoubleVector3 target = navigation.LockedUniversePosition;
        Vector3 direction = CalculateTravelDirection(origin, target);
        if (direction.sqrMagnitude < 0.0001f)
            direction = transform.forward;
        PlanetCelestialProfile celestial = navigation.LockedPlanet?.celestial
            ?? PlanetCelestialProfile.CreateLargeDefault();
        double corridorDistance = CalculateEntryCorridorDistance(celestial);
        DoubleVector3 destination = CalculateWarpDestination(origin, target, corridorDistance);
        Vector3 up = Vector3.ProjectOnPlane(transform.up, direction);
        if (up.sqrMagnitude < 0.0001f)
            up = Vector3.up;
        Quaternion rotation = Quaternion.LookRotation(direction, up.normalized);
        flightRuntime.WarpToUniversePosition(destination, direction * exitSpeed, rotation);
        if (ifcsMotor != null)
            ifcsMotor.ResetControllerState();
    }

    public void Abort()
    {
        CancelWarp(InterstellarWarpCancelReason.Manual);
    }

    void CancelWarp(InterstellarWarpCancelReason reason)
    {
        if (warpState == InterstellarWarpState.Transit || warpState == InterstellarWarpState.Exiting)
            return;
        cancelReason = reason;
        SetWarpState(navigation != null && navigation.HasLockedTarget
            ? InterstellarWarpState.Locked
            : InterstellarWarpState.Unlocked);
    }

    void SetWarpState(InterstellarWarpState next)
    {
        warpState = next;
        stateTime = 0f;
        bool suppressCombat = next == InterstellarWarpState.Aligning
            || next == InterstellarWarpState.Spooling
            || next == InterstellarWarpState.Transit
            || next == InterstellarWarpState.Exiting;
        if (weaponSystem != null)
            weaponSystem.ControlsEnabled = controlsEnabled && !suppressCombat;
        if (ifcsMotor != null)
        {
            ifcsMotor.LinearControlEnabled = next != InterstellarWarpState.Transit
                && next != InterstellarWarpState.Exiting;
            if (!suppressCombat)
            {
                ifcsMotor.ClearExternalTargets();
                ifcsMotor.ResetControllerState();
            }
        }
    }

    float CalculateProgress()
    {
        switch (warpState)
        {
            case InterstellarWarpState.Aligning:
                return Mathf.Clamp01(1f - alignmentError / Mathf.Max(0.1f, abortAngle));
            case InterstellarWarpState.Spooling:
                return Mathf.Clamp01(stateTime / spoolDuration);
            case InterstellarWarpState.Transit:
                return Mathf.Clamp01(stateTime / transitDuration);
            case InterstellarWarpState.Exiting:
                return 1f;
            default:
                return 0f;
        }
    }

    float CalculateVisualIntensity()
    {
        if (warpState == InterstellarWarpState.Spooling)
            return Mathf.Lerp(0.08f, 0.35f, CalculateProgress());
        if (warpState == InterstellarWarpState.Transit)
            return Mathf.Sin(Mathf.Clamp01(stateTime / transitDuration) * Mathf.PI);
        if (warpState == InterstellarWarpState.Exiting)
            return 1f - Mathf.Clamp01(stateTime / Mathf.Max(0.01f, exitDuration));
        return 0f;
    }

    void ResolveReferences()
    {
        if (shipBody == null)
            shipBody = GetComponent<Rigidbody>();
        if (navigation == null)
            navigation = FindObjectOfType<InterstellarNavigationSystem>();
        if (flightRuntime == null)
            flightRuntime = FindObjectOfType<InterstellarFlightRuntime>();
        if (ifcsMotor == null)
            ifcsMotor = GetComponent<SpacecraftIfcsMotor>();
        if (flightInput == null)
            flightInput = GetComponent<KeyboardMouseFlightInput>();
        if (damageReceiver == null)
            damageReceiver = GetComponent<SpacecraftDamageReceiver>();
        if (weaponSystem == null)
            weaponSystem = GetComponent<SpacecraftWeaponSystem>();
    }

    void HandleDamaged(float integrity, float maximumIntegrity, SpaceDamageInfo damage)
    {
        if (warpState == InterstellarWarpState.Aligning || warpState == InterstellarWarpState.Spooling)
        {
            SetAutomaticLandingRequested(false);
            CancelWarp(InterstellarWarpCancelReason.Damaged);
        }
    }

    void OnDisable()
    {
        if (damageReceiver != null)
            damageReceiver.Damaged -= HandleDamaged;
        warpState = navigation != null && navigation.HasLockedTarget
            ? InterstellarWarpState.Locked
            : InterstellarWarpState.Unlocked;
        if (ifcsMotor != null)
        {
            ifcsMotor.LinearControlEnabled = true;
            ifcsMotor.ClearExternalTargets();
        }
        if (weaponSystem != null)
            weaponSystem.ControlsEnabled = controlsEnabled;
        SetAutomaticLandingRequested(false);
    }

    public static Vector3 CalculateTravelDirection(DoubleVector3 origin, DoubleVector3 target)
    {
        double x = target.x - origin.x;
        double y = target.y - origin.y;
        double z = target.z - origin.z;
        double magnitude = System.Math.Sqrt(x * x + y * y + z * z);
        return magnitude <= 0.000001d
            ? Vector3.zero
            : new Vector3((float)(x / magnitude), (float)(y / magnitude), (float)(z / magnitude));
    }

    public static DoubleVector3 CalculateWarpDestination(
        DoubleVector3 origin,
        DoubleVector3 target,
        double targetExitDistance)
    {
        Vector3 direction = CalculateTravelDirection(origin, target);
        return target - new DoubleVector3(
            direction.x * targetExitDistance,
            direction.y * targetExitDistance,
            direction.z * targetExitDistance);
    }

    public static double CalculateEntryCorridorDistance(PlanetCelestialProfile profile)
    {
        profile = profile != null ? profile.Clone() : PlanetCelestialProfile.CreateLargeDefault();
        float terrainTop = profile.radius + profile.maximumTerrainElevation;
        float atmosphereEntry = profile.HasAtmosphere
            ? profile.radius + profile.atmosphereTopAltitude + 180f
            : terrainTop + 620f;
        return Mathf.Max(terrainTop + 520f, atmosphereEntry);
    }

    static InterstellarCruiseState MapLegacyState(InterstellarWarpState value)
    {
        switch (value)
        {
            case InterstellarWarpState.Aligning:
            case InterstellarWarpState.Spooling:
                return InterstellarCruiseState.Spooling;
            case InterstellarWarpState.Transit:
                return InterstellarCruiseState.Cruising;
            case InterstellarWarpState.Exiting:
                return InterstellarCruiseState.Decelerating;
            case InterstellarWarpState.Cooldown:
                return InterstellarCruiseState.Cooldown;
            default:
                return InterstellarCruiseState.Inactive;
        }
    }
}
