using System.Collections;
using SpacecraftEditor;
using UnityEngine;

[DefaultExecutionOrder(-150)]
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class InterstellarCruiseController : MonoBehaviour
{
    const double DefaultExitDistance = 14000d;
    const float DefaultExitSpeed = 120f;
    public const float PortalCrossingProgress = 0.72f;
    const float PortalCenterOvershoot = 1.5f;

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
    [SerializeField, Min(0.1f)] float alignmentDuration = 0.65f;
    [SerializeField, Min(0.1f)] float spoolDuration = 1.15f;
    [SerializeField, Min(0.2f)] float transitDuration = 0.9f;
    [SerializeField, Min(0f)] float exitDuration = 0.65f;
    [SerializeField, Min(0f)] float cooldownDuration = 0.6f;
    [SerializeField, Min(1000f)] float exitDistance = (float)DefaultExitDistance;
    [SerializeField, Min(1f)] float exitSpeed = DefaultExitSpeed;
    [SerializeField, Min(1000f)] float automaticWarpMinimumDistance = 20000f;
    [SerializeField, Min(10f)] float automaticApproachMaximumSpeed = 220f;
    [SerializeField] InterstellarWarpGateController warpGate;
    [SerializeField] InterstellarCameraRig cameraRig;

    InterstellarWarpState warpState = InterstellarWarpState.Unlocked;
    InterstellarWarpCancelReason cancelReason;
    float stateTime;
    float alignmentError;
    bool controlsEnabled = true;
    bool relocated;
    bool automaticLandingRequested;
    bool cinematicInputSuppressed;
    bool surfaceEntryActive;
    GalaxyPlanetDefinition frozenPlanet;
    DoubleVector3 frozenTargetPosition;
    DoubleVector3 frozenDestination;
    UniversePosition frozenTargetAddress;
    UniversePosition frozenDestinationAddress;
    Vector3 frozenTravelDirection;
    Quaternion frozenExitRotation;
    Vector3 frozenExitVelocity;
    Vector3 frozenLandingDirection;
    Vector3 transitStartPosition;
    Vector3 transitPortalPosition;
    Vector3 transitPortalForward;
    bool transitKinematicControlActive;
    bool transitOriginalIsKinematic;
    RigidbodyInterpolation transitOriginalInterpolation;

    public InterstellarWarpState WarpState => warpState;
    public InterstellarWarpCancelReason CancelReason => cancelReason;
    public float AlignmentError => alignmentError;
    public float WarpProgress => CalculateProgress();
    public float ExitDistance => exitDistance;
    public float ExitSpeed => exitSpeed;
    public float WarpVisualIntensity => CalculateVisualIntensity();
    public bool AutomaticLandingRequested => automaticLandingRequested;
    public bool IsSurfaceEntryActive => surfaceEntryActive;
    public bool IsActive => warpState == InterstellarWarpState.Aligning
        || warpState == InterstellarWarpState.Spooling
        || warpState == InterstellarWarpState.Transit
        || warpState == InterstellarWarpState.Exiting;
    public float ArrivalSpeed => exitSpeed;
    [System.Obsolete("Vacuum flight has no absolute maximum speed. Use ArrivalSpeed for warp exit velocity.")]
    public float MaximumCruiseSpeed => float.PositiveInfinity;
    public InterstellarCruiseState State => MapLegacyState(warpState);

    public bool ControlsEnabled
    {
        get => controlsEnabled;
        set
        {
            controlsEnabled = value;
            if (!value && !IsActive)
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
        if (!controlsEnabled
            || surfaceEntryActive
            || IsActive
            || flightInput == null
            || !flightInput.ConsumeCruisePressed())
            return;

        HandlePlanetAction();
    }

    void HandlePlanetAction()
    {
        cancelReason = InterstellarWarpCancelReason.None;
        if (navigation == null
            || !navigation.TryLockReticleTarget(Camera.main, reticleLockAngle)
            || navigation.LockedPlanet == null)
        {
            cancelReason = InterstellarWarpCancelReason.NoReticleTarget;
            return;
        }

        GalaxyTravelManager manager = GalaxyTravelManager.Instance;
        if (manager == null || flightRuntime == null || shipBody == null)
            return;

        if (!navigation.IsNearLockedPlanet)
        {
            BeginCinematicWarp();
            return;
        }

        DoubleVector3 relative = UniversePosition.Delta(
            navigation.LockedUniverseAddress,
            flightRuntime.ShipPhysicalUniversePosition);
        Vector3 landingDirection = relative.ToVector3();
        if (landingDirection.sqrMagnitude < 0.001f)
            landingDirection = -shipBody.transform.forward;
        landingDirection.Normalize();

        flightRuntime.SaveState(true);
        SetAutomaticLandingRequested(false);
        frozenPlanet = navigation.LockedPlanet;
        frozenLandingDirection = landingDirection;
        StartCoroutine(SurfaceEntryRoutine(manager));
    }

    void BeginCinematicWarp()
    {
        if (navigation == null
            || !navigation.HasLockedTarget
            || flightRuntime == null
            || shipBody == null)
        {
            return;
        }

        frozenPlanet = navigation.LockedPlanet;
        frozenTargetAddress = navigation.LockedUniverseAddress;
        frozenTargetPosition = frozenTargetAddress.ToAbsoluteMeters();
        UniversePosition originAddress = flightRuntime.ShipPhysicalUniversePosition;
        frozenTravelDirection = CalculateTravelDirection(originAddress, frozenTargetAddress);
        if (frozenTravelDirection.sqrMagnitude < 0.001f)
            frozenTravelDirection = transform.forward;
        double nearDistance = navigation.GetNearExitDistance(frozenPlanet);
        frozenDestinationAddress = CalculateWarpDestination(
            originAddress,
            frozenTargetAddress,
            nearDistance);
        frozenDestination = frozenDestinationAddress.ToAbsoluteMeters();
        Vector3 up = Vector3.ProjectOnPlane(transform.up, frozenTravelDirection);
        if (up.sqrMagnitude < 0.001f)
            up = Vector3.up;
        frozenExitRotation = Quaternion.LookRotation(
            frozenTravelDirection,
            up.normalized);
        frozenExitVelocity = frozenTravelDirection * exitSpeed;

        if (warpGate == null)
        {
            var gateObject = new GameObject("WarpGateSystem");
            warpGate = gateObject.AddComponent<InterstellarWarpGateController>();
        }
        bool gateReady = warpGate.BeginWarp(
            new WarpGateContext
            {
                planet = frozenPlanet,
                targetUniversePosition = frozenTargetPosition,
                destinationUniversePosition = frozenDestination,
                targetUniverseAddress = frozenTargetAddress,
                destinationUniverseAddress = frozenDestinationAddress,
                hasHierarchicalAddresses = true,
                travelDirection = frozenTravelDirection,
                exitRotation = frozenExitRotation,
                targetProxy = navigation.LockedProxy
            },
            flightRuntime,
            shipBody,
            Camera.main);
        if (!gateReady)
            return;

        flightRuntime.SaveState(true);
        relocated = false;
        SetAutomaticLandingRequested(false);
        SuppressCinematicInput(true);
        cameraRig?.SetCinematicMotionOverride(true);
        SetWarpState(InterstellarWarpState.Aligning);
    }

    IEnumerator SurfaceEntryRoutine(GalaxyTravelManager manager)
    {
        surfaceEntryActive = true;
        SuppressCinematicInput(true);
        cameraRig?.SetCinematicMotionOverride(true);
        cameraRig?.SetCinematicFovOverride(Camera.main == null
            ? 60f
            : Camera.main.fieldOfView);
        PersistentSpaceflightFade fade = PersistentSpaceflightFade.Instance;
        fade.SetBlackout(0f);

        const float duration = 1.2f;
        const float fadeDuration = 0.35f;
        float startFov = Camera.main == null ? 60f : Camera.main.fieldOfView;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            float eased = Mathf.SmoothStep(0f, 1f, progress);
            DoubleVector3 toTarget = UniversePosition.Delta(
                flightRuntime.ShipPhysicalUniversePosition,
                frozenTargetAddress);
            Vector3 direction = toTarget.ToVector3();
            if (direction.sqrMagnitude < 0.001f)
                direction = -frozenLandingDirection;
            direction.Normalize();
            shipBody.rotation = Quaternion.Slerp(
                shipBody.rotation,
                Quaternion.LookRotation(direction, shipBody.transform.up),
                1f - Mathf.Exp(-7f * Time.unscaledDeltaTime));
            shipBody.velocity = direction * Mathf.Lerp(120f, 4500f, eased);
            cameraRig?.SetCinematicFovOverride(Mathf.Lerp(startFov, 52f, eased));

            float fadeProgress = Mathf.InverseLerp(
                duration - fadeDuration,
                duration,
                elapsed);
            fade.SetBlackout(fadeProgress);
            yield return null;
        }

        fade.SetBlackout(1f);
        fade.FadeInAfterNextScene(0.6f, true);
        bool accepted = manager.EnterPlanetSurfaceDirect(
            frozenPlanet,
            frozenLandingDirection,
            shipBody.rotation,
            damageReceiver == null ? 100f : damageReceiver.Integrity);
        if (!accepted)
        {
            fade.CancelPendingTransition();
            surfaceEntryActive = false;
            cameraRig?.SetCinematicFovOverride(null);
            cameraRig?.SetCinematicMotionOverride(false);
            SuppressCinematicInput(false);
        }
    }

    void FixedUpdate()
    {
        if (!controlsEnabled || shipBody == null)
            return;

        stateTime += Time.fixedDeltaTime;
        switch (warpState)
        {
            case InterstellarWarpState.Aligning:
                UpdateCinematicAlignment();
                break;
            case InterstellarWarpState.Spooling:
                UpdateCinematicSpooling();
                break;
            case InterstellarWarpState.Transit:
                UpdateTransit();
                break;
            case InterstellarWarpState.Exiting:
                warpGate?.SetPhase(
                    InterstellarWarpState.Exiting,
                    stateTime / Mathf.Max(0.01f, exitDuration));
                HoldExitPose();
                if (stateTime >= exitDuration)
                    SetWarpState(InterstellarWarpState.Cooldown);
                break;
            case InterstellarWarpState.Cooldown:
                HoldExitPose();
                if (stateTime >= cooldownDuration)
                    SetWarpState(navigation != null && navigation.HasLockedTarget
                        ? InterstellarWarpState.Locked
                        : InterstellarWarpState.Unlocked);
                break;
        }

        if (automaticLandingRequested && warpState == InterstellarWarpState.Locked)
            ApplyAutomaticApproach();
    }

    void UpdateCinematicAlignment()
    {
        Vector3 direction = frozenTravelDirection.sqrMagnitude > 0.001f
            ? frozenTravelDirection.normalized
            : transform.forward;
        alignmentError = Vector3.Angle(transform.forward, direction);
        ApplyCinematicAttitude(direction, Vector3.zero);
        warpGate?.TrackEntrance();
        warpGate?.SetPhase(
            InterstellarWarpState.Aligning,
            stateTime / Mathf.Max(0.01f, alignmentDuration));
        if (stateTime >= alignmentDuration)
            SetWarpState(InterstellarWarpState.Spooling);
    }

    void UpdateCinematicSpooling()
    {
        Vector3 direction = frozenTravelDirection.sqrMagnitude > 0.001f
            ? frozenTravelDirection.normalized
            : transform.forward;
        alignmentError = Vector3.Angle(transform.forward, direction);
        ApplyCinematicAttitude(direction, Vector3.zero);
        warpGate?.TrackEntrance();
        warpGate?.SetPhase(
            InterstellarWarpState.Spooling,
            stateTime / Mathf.Max(0.01f, spoolDuration));
        if (stateTime >= spoolDuration)
            BeginTransit();
    }

    void ApplyCinematicAttitude(Vector3 direction, Vector3 velocity)
    {
        Vector3 up = Vector3.ProjectOnPlane(transform.up, direction);
        if (up.sqrMagnitude < 0.001f)
            up = Vector3.up;
        if (ifcsMotor == null)
            return;
        ifcsMotor.LinearControlEnabled = true;
        ifcsMotor.SetExternalWorldVelocityTarget(velocity);
        ifcsMotor.SetExternalWorldAttitudeTarget(
            Quaternion.LookRotation(direction, up.normalized));
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
        BeginCinematicWarp();
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
        if (frozenPlanet == null || flightRuntime == null)
        {
            CancelWarp(InterstellarWarpCancelReason.TargetLost);
            return;
        }
        flightRuntime.SaveState(true);
        relocated = false;
        BeginTransitKinematicControl();
        warpGate?.FreezeEntrance();
        transitStartPosition = shipBody.position;
        transitPortalForward = warpGate == null
            ? frozenTravelDirection.normalized
            : warpGate.EntranceForward.normalized;
        if (transitPortalForward.sqrMagnitude < 0.001f)
            transitPortalForward = transform.forward;
        transitPortalPosition = warpGate == null
            ? transitStartPosition + transitPortalForward * 45f
            : warpGate.EntrancePosition;
        shipBody.velocity = Vector3.zero;
        SetWarpState(InterstellarWarpState.Transit);
    }

    void UpdateTransit()
    {
        float progress = Mathf.Clamp01(
            stateTime / Mathf.Max(0.01f, transitDuration));
        warpGate?.SetPhase(InterstellarWarpState.Transit, progress);

        if (!relocated)
        {
            float entranceProgress = Mathf.Clamp01(
                progress / PortalCrossingProgress);
            Vector3 desiredPosition = CalculateFixedTransitPosition(
                transitStartPosition,
                transitPortalPosition,
                transitPortalForward,
                entranceProgress);
            Vector3 movement = desiredPosition - shipBody.position;
            Vector3 direction = movement.sqrMagnitude > 0.0001f
                ? movement.normalized
                : transitPortalForward;
            Vector3 transitUp = Vector3.ProjectOnPlane(
                frozenExitRotation * Vector3.up,
                direction);
            if (transitUp.sqrMagnitude < 0.001f)
                transitUp = Vector3.ProjectOnPlane(Vector3.up, direction);
            if (transitUp.sqrMagnitude < 0.001f)
                transitUp = Vector3.right;

            Quaternion desiredRotation = Quaternion.Slerp(
                shipBody.rotation,
                Quaternion.LookRotation(direction, transitUp.normalized),
                1f - Mathf.Exp(-10f * Time.fixedDeltaTime));
            shipBody.MoveRotation(desiredRotation);
            shipBody.MovePosition(desiredPosition);

            if (progress >= PortalCrossingProgress)
            {
                EndTransitKinematicControl();
                PerformRelocation();
                relocated = true;
            }
        }
        else
        {
            HoldExitPose();
        }
        if (relocated && stateTime >= transitDuration)
            SetWarpState(InterstellarWarpState.Exiting);
    }

    void BeginTransitKinematicControl()
    {
        if (shipBody == null || transitKinematicControlActive)
            return;

        transitOriginalIsKinematic = shipBody.isKinematic;
        transitOriginalInterpolation = shipBody.interpolation;
        Vector3 renderedPosition = shipBody.transform.position;
        Quaternion renderedRotation = shipBody.transform.rotation;

        shipBody.interpolation = RigidbodyInterpolation.None;
        shipBody.velocity = Vector3.zero;
        shipBody.angularVelocity = Vector3.zero;
        shipBody.position = renderedPosition;
        shipBody.rotation = renderedRotation;
        shipBody.isKinematic = true;
        shipBody.interpolation = transitOriginalInterpolation;
        transitKinematicControlActive = true;
    }

    void EndTransitKinematicControl()
    {
        if (shipBody == null || !transitKinematicControlActive)
            return;

        shipBody.interpolation = RigidbodyInterpolation.None;
        shipBody.isKinematic = transitOriginalIsKinematic;
        shipBody.velocity = Vector3.zero;
        shipBody.angularVelocity = Vector3.zero;
        shipBody.interpolation = transitOriginalInterpolation;
        transitKinematicControlActive = false;
    }

    public static Vector3 CalculateFixedTransitPosition(
        Vector3 start,
        Vector3 portalCenter,
        Vector3 portalForward,
        float normalizedProgress)
    {
        float progress = Mathf.Clamp01(normalizedProgress);
        float acceleratedProgress = progress * progress * (2f - progress);
        Vector3 forward = portalForward.sqrMagnitude > 0.001f
            ? portalForward.normalized
            : Vector3.forward;
        Vector3 crossingPoint =
            portalCenter + forward * PortalCenterOvershoot;
        return Vector3.LerpUnclamped(
            start,
            crossingPoint,
            acceleratedProgress);
    }

    void PerformRelocation()
    {
        if (flightRuntime == null || frozenPlanet == null)
            return;
        Vector3 orbitalVelocity = GalaxyTravelManager.Instance == null
            ? Vector3.zero
            : GalaxyTravelManager.Instance.GetInterstellarPlanetVelocity(
                frozenPlanet.coordinate3D);
        frozenExitVelocity = orbitalVelocity + frozenTravelDirection * exitSpeed;
        ifcsMotor?.ClearExternalTargets();
        flightRuntime.WarpToUniversePosition(
            frozenDestinationAddress,
            frozenExitVelocity,
            frozenExitRotation);
        navigation?.MarkNearPlanet(frozenPlanet);
        warpGate?.NotifyRelocated();
        ifcsMotor?.StabilizeAfterCinematic(
            frozenExitRotation,
            frozenExitVelocity);
    }

    void HoldExitPose()
    {
        if (shipBody == null)
            return;

        shipBody.rotation = frozenExitRotation;
        shipBody.velocity = frozenExitVelocity;
        shipBody.angularVelocity = Vector3.zero;
    }

    public void Abort()
    {
        CancelWarp(InterstellarWarpCancelReason.Manual);
    }

    void CancelWarp(InterstellarWarpCancelReason reason)
    {
        if (IsActive)
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
            || next == InterstellarWarpState.Exiting
            || next == InterstellarWarpState.Cooldown;
        bool holdExitPose = next == InterstellarWarpState.Transit
            || next == InterstellarWarpState.Exiting
            || next == InterstellarWarpState.Cooldown;
        flightRuntime?.SetWarpCinematic(suppressCombat);
        cameraRig?.SetCinematicThirdPersonOverride(suppressCombat);
        if (weaponSystem != null)
            weaponSystem.ControlsEnabled = controlsEnabled && !suppressCombat;
        if (ifcsMotor != null)
        {
            ifcsMotor.LinearControlEnabled = !holdExitPose;
            ifcsMotor.AngularControlEnabled = !holdExitPose;
            if (!suppressCombat)
            {
                ifcsMotor.ClearExternalTargets();
                ifcsMotor.ResetControllerState();
            }
        }
        if (next == InterstellarWarpState.Cooldown)
        {
            EndTransitKinematicControl();
            HoldExitPose();
            ifcsMotor?.StabilizeAfterCinematic(
                frozenExitRotation,
                frozenExitVelocity);
            warpGate?.EndWarp();
        }
        if (!suppressCombat && cinematicInputSuppressed)
        {
            SuppressCinematicInput(false);
            cameraRig?.SetCinematicFovOverride(null);
            cameraRig?.SetCinematicMotionOverride(false);
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
        if (warpGate == null)
            warpGate = FindObjectOfType<InterstellarWarpGateController>();
        if (cameraRig == null)
            cameraRig = FindObjectOfType<InterstellarCameraRig>();
    }

    void HandleDamaged(float integrity, float maximumIntegrity, SpaceDamageInfo damage)
    {
        // Cinematic portal travel is committed once B is pressed. Damage is still
        // applied by the receiver, but it does not tear down the gate mid-shot.
    }

    void SuppressCinematicInput(bool suppress)
    {
        cinematicInputSuppressed = suppress;
        if (flightInput != null)
        {
            flightInput.CaptureEnabled = controlsEnabled && !suppress;
            if (suppress)
                flightInput.ClearTransientRequests();
        }
        if (weaponSystem != null)
            weaponSystem.ControlsEnabled = controlsEnabled && !suppress;
    }

    void OnDisable()
    {
        if (damageReceiver != null)
            damageReceiver.Damaged -= HandleDamaged;
        StopAllCoroutines();
        surfaceEntryActive = false;
        EndTransitKinematicControl();
        cameraRig?.SetCinematicFovOverride(null);
        cameraRig?.SetCinematicMotionOverride(false);
        cameraRig?.SetCinematicThirdPersonOverride(false);
        flightRuntime?.SetWarpCinematic(false);
        warpGate?.EndWarp();
        SuppressCinematicInput(false);
        warpState = navigation != null && navigation.HasLockedTarget
            ? InterstellarWarpState.Locked
            : InterstellarWarpState.Unlocked;
        if (ifcsMotor != null)
        {
            ifcsMotor.LinearControlEnabled = true;
            ifcsMotor.AngularControlEnabled = true;
            ifcsMotor.ClearExternalTargets();
            ifcsMotor.ResetControllerState();
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

    public static Vector3 CalculateTravelDirection(
        UniversePosition origin,
        UniversePosition target)
    {
        DoubleVector3 delta = UniversePosition.Delta(origin, target);
        double magnitude = System.Math.Sqrt(
            delta.x * delta.x + delta.y * delta.y + delta.z * delta.z);
        return magnitude <= 0.000001d
            ? Vector3.zero
            : new Vector3(
                (float)(delta.x / magnitude),
                (float)(delta.y / magnitude),
                (float)(delta.z / magnitude));
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

    public static UniversePosition CalculateWarpDestination(
        UniversePosition origin,
        UniversePosition target,
        double targetExitDistance)
    {
        Vector3 direction = CalculateTravelDirection(origin, target);
        return target.Add(new DoubleVector3(
            -direction.x * targetExitDistance,
            -direction.y * targetExitDistance,
            -direction.z * targetExitDistance));
    }

    public static double CalculateEntryCorridorDistance(PlanetCelestialProfile profile)
    {
        profile = profile != null ? profile.Clone() : PlanetCelestialProfile.CreateLargeDefault();
        double terrainTop = profile.Physical.radiusMeters + profile.maximumTerrainElevation;
        double atmosphereEntry = profile.HasAtmosphere
            ? profile.Physical.radiusMeters
                + profile.Physical.atmosphereTopAltitudeMeters
                + 180_000d
            : terrainTop + 620_000d;
        return System.Math.Max(terrainTop + 520_000d, atmosphereEntry);
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
