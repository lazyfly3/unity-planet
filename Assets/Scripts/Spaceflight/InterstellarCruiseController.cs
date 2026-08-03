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
    [SerializeField] InterstellarShipController shipController;
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
    [SerializeField] InterstellarWarpShipPresentation shipPresentation;

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
    Vector3 frozenLandingDirection;
    Vector3 transitStartPosition;
    Vector3 transitPortalPosition;
    Vector3 transitPortalForward;
    bool physicsSnapshotActive;
    bool snapshotOriginalIsKinematic;
    bool snapshotOriginalDetectCollisions;
    RigidbodyInterpolation snapshotOriginalInterpolation;
    Quaternion snapshotRotation;
    Vector3 snapshotAbsoluteVelocity;
    Vector3 snapshotRelativeVelocity;
    Vector3 snapshotAngularVelocity;
    Vector3 restoredAbsoluteVelocity;
    bool snapshotIfcsControlsEnabled;
    bool snapshotLinearControlEnabled;
    bool snapshotAngularControlEnabled;

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
        || warpState == InterstellarWarpState.Exiting
        || warpState == InterstellarWarpState.Cooldown;
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
            if (!value && IsActive)
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
            || !ConsumeCruisePressed())
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
        BeginVisualPresentation();
        CaptureAndPausePhysics();
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
                UpdateExitPresentation();
                if (stateTime >= exitDuration)
                    SetWarpState(InterstellarWarpState.Cooldown);
                break;
            case InterstellarWarpState.Cooldown:
                MatchPresentationToPhysicalShip();
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
        float progress = Mathf.Clamp01(
            stateTime / Mathf.Max(0.01f, alignmentDuration));
        float eased = Mathf.SmoothStep(0f, 1f, progress);
        alignmentError = Mathf.Lerp(
            Vector3.Angle(snapshotRotation * Vector3.forward, direction),
            0f,
            eased);
        SetPresentationPose(
            shipBody.position,
            Quaternion.Slerp(snapshotRotation, frozenExitRotation, eased));
        warpGate?.TrackEntrance();
        warpGate?.SetPhase(
            InterstellarWarpState.Aligning,
            progress);
        if (stateTime >= alignmentDuration)
            SetWarpState(InterstellarWarpState.Spooling);
    }

    void UpdateCinematicSpooling()
    {
        alignmentError = 0f;
        SetPresentationPose(shipBody.position, frozenExitRotation);
        warpGate?.TrackEntrance();
        warpGate?.SetPhase(
            InterstellarWarpState.Spooling,
            stateTime / Mathf.Max(0.01f, spoolDuration));
        if (stateTime >= spoolDuration)
            BeginTransit();
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
        if (navigation == null || !navigation.HasLockedTarget || shipController == null)
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

        shipController.LinearControlEnabled = true;
        shipController.SetExternalWorldVelocityTarget(direction * targetSpeed);
        shipController.SetExternalWorldAttitudeTarget(Quaternion.LookRotation(direction, up.normalized));
        shipController.SetWeaponControlsEnabled(false);
    }

    void SetAutomaticLandingRequested(bool requested)
    {
        automaticLandingRequested = requested;
        navigation?.RequestAutomaticLanding(requested);
        if (requested)
            return;
        if (shipController != null && !IsActive)
        {
            shipController.ClearExternalTargets();
            shipController.ResetFlightController();
        }
        if (shipController != null && !IsActive)
            shipController.SetWeaponControlsEnabled(controlsEnabled);
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

    void BeginTransit()
    {
        if (frozenPlanet == null || flightRuntime == null)
        {
            CancelWarp(InterstellarWarpCancelReason.TargetLost);
            return;
        }
        flightRuntime.SaveState(true);
        relocated = false;
        warpGate?.FreezeEntrance();
        transitStartPosition = PresentationTransform == null
            ? shipBody.position
            : PresentationTransform.position;
        transitPortalForward = warpGate == null
            ? frozenTravelDirection.normalized
            : warpGate.EntranceForward.normalized;
        if (transitPortalForward.sqrMagnitude < 0.001f)
            transitPortalForward = transform.forward;
        transitPortalPosition = warpGate == null
            ? transitStartPosition + transitPortalForward * 45f
            : warpGate.EntrancePosition;
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
            Vector3 currentPosition = PresentationTransform == null
                ? transitStartPosition
                : PresentationTransform.position;
            Quaternion currentRotation = PresentationTransform == null
                ? frozenExitRotation
                : PresentationTransform.rotation;
            Vector3 movement = desiredPosition - currentPosition;
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
                currentRotation,
                Quaternion.LookRotation(direction, transitUp.normalized),
                1f - Mathf.Exp(-10f * Time.fixedDeltaTime));
            SetPresentationPose(desiredPosition, desiredRotation);

            if (progress >= PortalCrossingProgress)
            {
                PerformRelocation();
                relocated = true;
            }
        }
        else
        {
            MatchPresentationToPhysicalShip();
        }
        if (relocated && stateTime >= transitDuration)
            SetWarpState(InterstellarWarpState.Exiting);
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
        restoredAbsoluteVelocity = CalculateRelocatedWorldVelocity(
            snapshotRelativeVelocity,
            orbitalVelocity);
        SetPresentationPose(Vector3.zero, frozenExitRotation);
        flightRuntime.WarpToUniversePosition(
            frozenDestinationAddress,
            restoredAbsoluteVelocity,
            snapshotRotation,
            snapshotAngularVelocity);
        navigation?.MarkNearPlanet(frozenPlanet);
        shipController?.SetVelocityReference(
            flightRuntime.CurrentIfcsVelocityReferenceWorld);
        MatchPresentationToPhysicalShip(frozenExitRotation);
        warpGate?.NotifyRelocated();
    }

    void UpdateExitPresentation()
    {
        float progress = Mathf.Clamp01(
            stateTime / Mathf.Max(0.01f, exitDuration));
        warpGate?.SetPhase(InterstellarWarpState.Exiting, progress);
        if (shipBody == null || PresentationTransform == null)
            return;

        SetPresentationPose(
            shipBody.position,
            Quaternion.Slerp(
                frozenExitRotation,
                snapshotRotation,
                Mathf.SmoothStep(0f, 1f, progress)));
    }

    void MatchPresentationToPhysicalShip()
    {
        MatchPresentationToPhysicalShip(snapshotRotation);
    }

    void MatchPresentationToPhysicalShip(Quaternion rotation)
    {
        if (shipBody != null)
            SetPresentationPose(shipBody.position, rotation);
    }

    Transform PresentationTransform => shipPresentation != null
        && shipPresentation.IsActive
            ? shipPresentation.PresentationTransform
            : null;

    void BeginVisualPresentation()
    {
        if (shipBody == null)
            return;
        if (shipPresentation == null)
        {
            shipPresentation =
                GetComponent<InterstellarWarpShipPresentation>()
                ?? gameObject.AddComponent<InterstellarWarpShipPresentation>();
        }

        bool hasVisualProxy = shipPresentation.Begin(shipBody.transform);
        Transform presentationTransform = hasVisualProxy
            ? shipPresentation.PresentationTransform
            : shipBody.transform;
        warpGate?.SetShipPresentation(presentationTransform);
        if (hasVisualProxy)
            cameraRig?.PushPresentationTarget(presentationTransform);
    }

    void EndVisualPresentation()
    {
        cameraRig?.PopPresentationTarget();
        warpGate?.SetShipPresentation(shipBody == null ? null : shipBody.transform);
        shipPresentation?.Restore();
    }

    void SetPresentationPose(Vector3 position, Quaternion rotation)
    {
        shipPresentation?.SetPose(position, rotation);
    }

    void CaptureAndPausePhysics()
    {
        if (physicsSnapshotActive || shipBody == null)
            return;

        snapshotOriginalIsKinematic = shipBody.isKinematic;
        snapshotOriginalDetectCollisions = shipBody.detectCollisions;
        snapshotOriginalInterpolation = shipBody.interpolation;
        snapshotRotation = shipBody.rotation;
        snapshotAbsoluteVelocity = flightRuntime == null
            ? shipBody.velocity
            : flightRuntime.ToBarycentricVelocity(shipBody.velocity);
        snapshotRelativeVelocity = flightRuntime == null
            ? shipBody.velocity - (shipController == null
                ? Vector3.zero
                : shipController.VelocityReferenceWorld)
            : flightRuntime.ShipRelativeVelocityMetersPerSecond;
        snapshotAngularVelocity = shipBody.angularVelocity;
        restoredAbsoluteVelocity = snapshotAbsoluteVelocity;

        if (shipController != null)
        {
            snapshotIfcsControlsEnabled = shipController.FlightControlForcesEnabled;
            snapshotLinearControlEnabled = shipController.LinearControlEnabled;
            snapshotAngularControlEnabled = shipController.AngularControlEnabled;
            shipController.ClearExternalTargets();
            shipController.SetFlightControlForcesEnabled(false);
        }

        shipBody.interpolation = RigidbodyInterpolation.None;
        shipBody.isKinematic = true;
        physicsSnapshotActive = true;
    }

    void RestorePhysicsAfterCinematic()
    {
        if (!physicsSnapshotActive)
        {
            EndVisualPresentation();
            return;
        }

        if (shipBody != null)
        {
            shipBody.interpolation = RigidbodyInterpolation.None;
            shipBody.isKinematic = snapshotOriginalIsKinematic;
            shipBody.rotation = snapshotRotation;
            shipBody.velocity = flightRuntime == null
                ? restoredAbsoluteVelocity
                : flightRuntime.ToActiveReferenceFrameVelocity(
                    restoredAbsoluteVelocity);
            shipBody.angularVelocity = snapshotAngularVelocity;
            shipBody.detectCollisions = snapshotOriginalDetectCollisions;
            Physics.SyncTransforms();
            shipBody.interpolation = snapshotOriginalInterpolation;
        }

        if (shipController != null)
        {
            shipController.ResetFlightController();
            shipController.LinearControlEnabled = snapshotLinearControlEnabled;
            shipController.AngularControlEnabled = snapshotAngularControlEnabled;
            shipController.SetFlightControlForcesEnabled(snapshotIfcsControlsEnabled);
        }

        physicsSnapshotActive = false;
        EndVisualPresentation();
        flightRuntime?.SaveState(true);
    }

    public void Abort()
    {
        CancelWarp(InterstellarWarpCancelReason.Manual);
    }

    void CancelWarp(InterstellarWarpCancelReason reason)
    {
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
        if (suppressCombat)
        {
            flightRuntime?.SetWarpCinematic(true);
            cameraRig?.SetCinematicThirdPersonOverride(true);
        }
        shipController?.SetWeaponControlsEnabled(controlsEnabled && !suppressCombat);
        if (next == InterstellarWarpState.Cooldown)
        {
            MatchPresentationToPhysicalShip();
            warpGate?.EndWarp();
        }
        if (!suppressCombat)
        {
            bool restoredCinematicPhysics = physicsSnapshotActive;
            warpGate?.EndWarp();
            RestorePhysicsAfterCinematic();
            flightRuntime?.SetWarpCinematic(false);
            cameraRig?.SetCinematicThirdPersonOverride(false);
            if (shipController != null)
            {
                shipController.ClearExternalTargets();
                if (!restoredCinematicPhysics)
                    shipController.ResetFlightController();
            }
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
                return Mathf.Clamp01(
                    1f
                    - Mathf.Max(0f, alignmentError - alignmentAngle)
                    / Mathf.Max(0.1f, abortAngle - alignmentAngle));
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
        if (shipController == null)
            shipController = GetComponent<InterstellarShipController>();
        if (warpGate == null)
            warpGate = FindObjectOfType<InterstellarWarpGateController>();
        if (cameraRig == null)
            cameraRig = FindObjectOfType<InterstellarCameraRig>();
        if (shipPresentation == null)
            shipPresentation = GetComponent<InterstellarWarpShipPresentation>();
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
        if (shipController != null)
        {
            shipController.SetInputCaptureEnabled(controlsEnabled && !suppress);
            shipController.SetWeaponControlsEnabled(controlsEnabled && !suppress);
        }
    }

    void OnDisable()
    {
        if (damageReceiver != null)
            damageReceiver.Damaged -= HandleDamaged;
        StopAllCoroutines();
        surfaceEntryActive = false;
        RestorePhysicsAfterCinematic();
        cameraRig?.SetCinematicFovOverride(null);
        cameraRig?.SetCinematicMotionOverride(false);
        cameraRig?.SetCinematicThirdPersonOverride(false);
        flightRuntime?.SetWarpCinematic(false);
        warpGate?.EndWarp();
        SuppressCinematicInput(false);
        warpState = navigation != null && navigation.HasLockedTarget
            ? InterstellarWarpState.Locked
            : InterstellarWarpState.Unlocked;
        if (shipController != null)
        {
            shipController.ClearExternalTargets();
            shipController.ResetFlightController();
        }
        shipController?.SetWeaponControlsEnabled(controlsEnabled);
        SetAutomaticLandingRequested(false);
    }

    bool ConsumeCruisePressed()
    {
        if (shipController != null && shipController.IsModular)
            return Input.GetKeyDown(KeyCode.B);
        return flightInput != null && flightInput.ConsumeCruisePressed();
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

    public static Vector3 CalculateRelocatedWorldVelocity(
        Vector3 savedRelativeVelocity,
        Vector3 targetVelocityReference)
    {
        return targetVelocityReference + savedRelativeVelocity;
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
