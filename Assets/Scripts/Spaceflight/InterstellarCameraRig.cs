using System;
using SpacecraftEditor;
using UnityEngine;

[DefaultExecutionOrder(800)]
[DisallowMultipleComponent]
public sealed class InterstellarCameraRig : MonoBehaviour
{
    const string CameraModePreferenceKey = "spaceflight.cameraMode";
    static readonly Vector2 ModularFlightAnchor =
        new Vector2(0.42f, 0.22f);

    [Header("References")]
    [SerializeField] Transform target;
    [SerializeField] InterstellarShipController ship;
    [SerializeField] InterstellarFlightRuntime runtime;
    [SerializeField] SpacecraftDamageReceiver damageReceiver;
    [SerializeField] ShipHullController hullController;
    [SerializeField] Camera targetCamera;
    [SerializeField] SpaceflightCockpitController cockpit;

    [Header("Third Person")]
    [SerializeField] Vector3 localOffset = new Vector3(0f, 4.2f, -13f);
    [SerializeField] Vector3 localLookOffset = new Vector3(0f, 1.2f, 5f);
    [SerializeField] Vector2 fieldOfViewRange = new Vector2(60f, 76f);
    [SerializeField, Range(0f, 1f)] float rollFollow = 0.75f;

    [Header("Cockpit")]
    [SerializeField, Min(0.05f)] float cockpitTransitionDuration = 0.28f;
    [SerializeField, Range(45f, 100f)] float cockpitFieldOfView = 66f;
    [SerializeField, Range(0f, 12f)] float cockpitSpeedFovAddition = 2f;
    [SerializeField] Vector3 cockpitRecoilLimits = new Vector3(0.03f, 0.03f, 0.08f);
    [SerializeField] Vector2 cockpitHorizontalLookLimits = new Vector2(-110f, 110f);
    [SerializeField] Vector2 cockpitVerticalLookLimits = new Vector2(-40f, 55f);

    [Header("Motion")]
    [SerializeField, Min(0f)] float rotationSharpness = 14f;
    [SerializeField, Min(0f)] float arcadeRotationSharpness = 32f;
    [SerializeField, Min(0f)] float accelerationSetback = 0.006f;
    [SerializeField, Min(0f)] float maximumSetback = 0.8f;
    [SerializeField, Min(0f)] float localRecoilSharpness = 8f;
    [SerializeField, Min(1f)] float maximumFovSpeed = 1800f;
    [SerializeField, Min(1f)] float arcadeMaximumFovSpeed = 140f;
    [SerializeField, Min(0f)] float freeLookSensitivity = 2.2f;

    Rigidbody targetBody;
    UnityPlanet.ModularAssembly.IRobocraftPilotAimSource
        modularPilotAim;
    Transform defaultTarget;
    Transform cinematicPresentationTarget;
    Vector3 previousVelocity;
    Vector3 localRecoil;
    Vector3 collisionKick;
    Vector2 freeLookAngles;
    Vector3 cockpitAnchorLocal;
    Vector3 transitionStartLocalPosition;
    Quaternion transitionStartLocalRotation = Quaternion.identity;
    Bounds modularLocalBounds;
    Bounds modularTargetLocalBounds;
    bool hasModularLocalBounds;
    float modularPresentationDistance;
    float nextModularBoundsRefresh;
    bool velocityInitialized;
    bool cinematicFovOverrideActive;
    float cinematicFovOverride;
    bool cinematicMotionOverrideActive;
    bool cinematicThirdPersonOverride;
    bool localPoseTransitionActive;
    bool cockpitPresentationVisible;
    float cockpitBlend;
    SpaceflightCameraMode requestedMode = SpaceflightCameraMode.ThirdPerson;
    SpaceflightCameraMode effectiveMode = SpaceflightCameraMode.ThirdPerson;

    public event Action<SpaceflightCameraMode> ModeChanged;

    public SpaceflightCameraMode Mode => effectiveMode;
    public SpaceflightCameraMode RequestedMode => requestedMode;
    public float CockpitBlend => cockpitBlend;
    public Vector3 CockpitAnchorLocal => cockpitAnchorLocal;
    public Camera TargetCamera => targetCamera;
    public bool CinematicThirdPersonOverride => cinematicThirdPersonOverride;
    public Transform PresentationTarget => target;

    void Awake()
    {
        ResolveReferences();
        requestedMode = PlayerPrefs.GetInt(
            CameraModePreferenceKey,
            (int)SpaceflightCameraMode.ThirdPerson) == (int)SpaceflightCameraMode.Cockpit
                ? SpaceflightCameraMode.Cockpit
                : SpaceflightCameraMode.ThirdPerson;
        effectiveMode = requestedMode;
        cockpitBlend = requestedMode == SpaceflightCameraMode.Cockpit ? 1f : 0f;
        RecalculateCockpitAnchor();
    }

    void OnEnable()
    {
        ResolveReferences();
        if (runtime != null)
        {
            runtime.OriginShifted += HandleOriginShift;
            runtime.UniverseRelocated += HandleUniverseRelocated;
        }
        if (damageReceiver != null)
            damageReceiver.Damaged += HandleDamaged;
        if (hullController != null)
            hullController.HullChanged += HandleHullChanged;
    }

    void Start()
    {
        ResolveCockpit();
        ApplyEffectiveMode(true);
        SnapToTarget();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F4)
            && !cinematicThirdPersonOverride
            && ship != null
            && ship.ControlsEnabled)
        {
            SetMode(
                requestedMode == SpaceflightCameraMode.Cockpit
                    ? SpaceflightCameraMode.ThirdPerson
                    : SpaceflightCameraMode.Cockpit,
                false);
        }
    }

    void LateUpdate()
    {
        if (target == null)
            return;

        UpdateLocalEffects();
        UpdateCockpitBlend();

        Quaternion freeLookRotation = Quaternion.Euler(freeLookAngles.x, freeLookAngles.y, 0f);
        Vector3 thirdPosition;
        Quaternion thirdRotation;
        if (UsesModularFlightFraming())
        {
            UpdateModularFlightBounds(false);
            ResolveModularThirdPersonPose(
                freeLookRotation,
                false,
                out thirdPosition,
                out thirdRotation);
        }
        else
        {
            Vector3 thirdOffset = localOffset + localRecoil + collisionKick;
            thirdPosition = target.position +
                            target.rotation *
                            (freeLookRotation * thirdOffset);
            Vector3 lookPoint = target.TransformPoint(localLookOffset);
            Vector3 lookDirection = lookPoint - thirdPosition;
            if (lookDirection.sqrMagnitude < 0.0001f)
                lookDirection = target.forward;
            Vector3 blendedUp = Vector3.Slerp(
                transform.up,
                target.up,
                rollFollow).normalized;
            thirdRotation = Quaternion.LookRotation(
                lookDirection.normalized,
                blendedUp);
        }

        Vector3 cockpitMotion = new Vector3(
            Mathf.Clamp(
                (localRecoil.x + collisionKick.x) * 0.22f,
                -cockpitRecoilLimits.x,
                cockpitRecoilLimits.x),
            Mathf.Clamp(
                (localRecoil.y + collisionKick.y) * 0.22f,
                -cockpitRecoilLimits.y,
                cockpitRecoilLimits.y),
            Mathf.Clamp(
                (localRecoil.z + collisionKick.z) * 0.35f,
                -cockpitRecoilLimits.z,
                cockpitRecoilLimits.z));
        Vector3 cockpitLocalPosition = cockpitAnchorLocal + cockpitMotion;
        Quaternion cockpitLocalRotation = freeLookRotation;

        if (localPoseTransitionActive)
        {
            float transitionProgress = effectiveMode == SpaceflightCameraMode.Cockpit
                ? cockpitBlend
                : 1f - cockpitBlend;
            float easedProgress = SmoothStep01(transitionProgress);
            Vector3 destinationLocalPosition;
            Quaternion destinationLocalRotation;
            if (effectiveMode == SpaceflightCameraMode.Cockpit)
            {
                destinationLocalPosition = cockpitLocalPosition;
                destinationLocalRotation = cockpitLocalRotation;
            }
            else
            {
                destinationLocalPosition = target.InverseTransformPoint(thirdPosition);
                destinationLocalRotation =
                    Quaternion.Inverse(target.rotation) * thirdRotation;
            }

            Vector3 localPosition = Vector3.Lerp(
                transitionStartLocalPosition,
                destinationLocalPosition,
                easedProgress);
            Quaternion localRotation = Quaternion.Slerp(
                transitionStartLocalRotation,
                destinationLocalRotation,
                easedProgress);
            transform.SetPositionAndRotation(
                target.TransformPoint(localPosition),
                target.rotation * localRotation);
        }
        else if (effectiveMode == SpaceflightCameraMode.Cockpit)
        {
            // A cockpit view is a rigid local pose, not a damped world-space chase
            // camera. Smoothing the world position causes an unbounded visual gap
            // between the ship-mounted cockpit and the camera at high speed.
            transform.SetPositionAndRotation(
                target.TransformPoint(cockpitLocalPosition),
                target.rotation * cockpitLocalRotation);
        }
        else
        {
            float effectiveRotationSharpness = UsesArcadeAssist()
                ? Mathf.Max(rotationSharpness, arcadeRotationSharpness)
                : rotationSharpness;
            float poseBlend = 1f - Mathf.Exp(
                -effectiveRotationSharpness * Time.unscaledDeltaTime);
            if (UsesModularFlightFraming())
            {
                // Keep the modular ship rigidly framed while its physics body
                // rotates. Smoothing world position and rotation independently
                // lets the two poses disagree during a turn and can push a
                // large assembly below the viewport. The view direction still
                // eases, then the position is solved from that exact direction.
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    thirdRotation,
                    poseBlend);
                ResolveModularThirdPersonPosition(
                    transform.rotation,
                    false,
                    out Vector3 framedPosition);
                transform.position = framedPosition;
            }
            else
            {
                transform.position = Vector3.Lerp(
                    transform.position,
                    thirdPosition,
                    poseBlend);
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    thirdRotation,
                    poseBlend);
            }
        }

        if (targetCamera != null)
        {
            float speed = CurrentPresentationVelocity().magnitude;
            float fovSpeedReference = UsesArcadeAssist()
                ? Mathf.Max(1f, arcadeMaximumFovSpeed)
                : maximumFovSpeed;
            float speedRatio = Mathf.Clamp01(speed / fovSpeedReference);
            float thirdFov = Mathf.Lerp(fieldOfViewRange.x, fieldOfViewRange.y, speedRatio);
            float cockpitFov = cockpitFieldOfView
                + Mathf.Lerp(0f, cockpitSpeedFovAddition, speedRatio);
            float warpExpansion = (fieldOfViewRange.y - fieldOfViewRange.x)
                * 0.7f
                * (ship == null ? 0f : ship.WarpVisualIntensity);
            float easedBlend = SmoothStep01(cockpitBlend);
            float desiredFov = cinematicFovOverrideActive
                ? cinematicFovOverride
                : Mathf.Lerp(thirdFov, cockpitFov, easedBlend) + warpExpansion;
            targetCamera.fieldOfView = Mathf.Lerp(
                targetCamera.fieldOfView,
                desiredFov,
                1f - Mathf.Exp(-5f * Time.unscaledDeltaTime));
        }
    }

    public void SetMode(SpaceflightCameraMode mode, bool immediate)
    {
        requestedMode = mode;
        PlayerPrefs.SetInt(CameraModePreferenceKey, (int)requestedMode);
        PlayerPrefs.Save();
        if (!cinematicThirdPersonOverride)
            ApplyEffectiveMode(immediate);
    }

    public void SetCinematicThirdPersonOverride(bool active)
    {
        if (cinematicThirdPersonOverride == active)
            return;
        cinematicThirdPersonOverride = active;
        ApplyEffectiveMode(false);
    }

    public void SetCinematicFovOverride(float? value)
    {
        cinematicFovOverrideActive = value.HasValue;
        if (value.HasValue)
            cinematicFovOverride = Mathf.Clamp(value.Value, 25f, 110f);
    }

    public void SetCinematicMotionOverride(bool active)
    {
        cinematicMotionOverrideActive = active;
        localRecoil = Vector3.zero;
        previousVelocity = CurrentPresentationVelocity();
        velocityInitialized = true;
    }

    public void PushPresentationTarget(Transform presentationTarget)
    {
        ResolveReferences();
        if (presentationTarget == null)
            return;
        if (cinematicPresentationTarget == null)
            defaultTarget = target;
        cinematicPresentationTarget = presentationTarget;
        target = presentationTarget;
        SnapToTarget();
    }

    public void PopPresentationTarget()
    {
        if (cinematicPresentationTarget == null)
            return;
        cinematicPresentationTarget = null;
        target = defaultTarget != null
            ? defaultTarget
            : ship == null ? null : ship.transform;
        defaultTarget = null;
        SnapToTarget();
    }

    public void SnapToTarget()
    {
        ResolveReferences();
        if (target == null)
            return;
        RecalculateCockpitAnchor();
        localRecoil = Vector3.zero;
        collisionKick = Vector3.zero;
        freeLookAngles = Vector2.zero;
        localPoseTransitionActive = false;
        cockpitBlend = effectiveMode == SpaceflightCameraMode.Cockpit ? 1f : 0f;

        if (effectiveMode == SpaceflightCameraMode.Cockpit
            && !cinematicThirdPersonOverride)
        {
            transform.position = target.TransformPoint(cockpitAnchorLocal);
            transform.rotation = target.rotation;
        }
        else
        {
            if (UsesModularFlightFraming())
            {
                UpdateModularFlightBounds(true);
                ResolveModularThirdPersonPose(
                    Quaternion.identity,
                    true,
                    out Vector3 thirdPosition,
                    out Quaternion thirdRotation);
                transform.SetPositionAndRotation(
                    thirdPosition,
                    thirdRotation);
            }
            else
            {
                transform.position = target.TransformPoint(localOffset);
                Vector3 lookPoint = target.TransformPoint(localLookOffset);
                transform.rotation = Quaternion.LookRotation(
                    lookPoint - transform.position,
                    target.up);
            }
        }
        UpdateCockpitPresentation(true);
        previousVelocity = CurrentPresentationVelocity();
        velocityInitialized = true;
    }

    void ApplyEffectiveMode(bool immediate)
    {
        SpaceflightCameraMode next = cinematicThirdPersonOverride
            ? SpaceflightCameraMode.ThirdPerson
            : requestedMode;
        bool changed = effectiveMode != next;
        if (!immediate && target != null)
        {
            transitionStartLocalPosition = target.InverseTransformPoint(transform.position);
            transitionStartLocalRotation =
                Quaternion.Inverse(target.rotation) * transform.rotation;
            localPoseTransitionActive = true;
        }
        effectiveMode = next;
        if (immediate)
        {
            cockpitBlend = next == SpaceflightCameraMode.Cockpit ? 1f : 0f;
            localPoseTransitionActive = false;
        }
        ResolveCockpit();
        UpdateCockpitPresentation(immediate);
        if (changed)
            ModeChanged?.Invoke(next);
    }

    void UpdateCockpitBlend()
    {
        float targetBlend = effectiveMode == SpaceflightCameraMode.Cockpit ? 1f : 0f;
        float duration = Mathf.Max(0.01f, cockpitTransitionDuration);
        cockpitBlend = Mathf.MoveTowards(
            cockpitBlend,
            targetBlend,
            Time.unscaledDeltaTime / duration);
        if (Mathf.Approximately(cockpitBlend, targetBlend))
            localPoseTransitionActive = false;
        UpdateCockpitPresentation(false);
    }

    void UpdateCockpitPresentation(bool immediate)
    {
        bool shouldShowCockpit;
        if (immediate)
        {
            shouldShowCockpit = effectiveMode == SpaceflightCameraMode.Cockpit;
        }
        else if (effectiveMode == SpaceflightCameraMode.Cockpit)
        {
            shouldShowCockpit = cockpitBlend >= 0.82f;
        }
        else
        {
            shouldShowCockpit = cockpitBlend > 0.18f;
        }

        if (cockpitPresentationVisible == shouldShowCockpit)
            return;
        cockpitPresentationVisible = shouldShowCockpit;
        cockpit?.SetVisible(shouldShowCockpit);
        InterstellarFlightHud hud = FindObjectOfType<InterstellarFlightHud>();
        if (hud != null)
        {
            hud.SetPresentationMode(
                shouldShowCockpit
                    ? SpaceflightHudPresentationMode.Cockpit
                    : SpaceflightHudPresentationMode.ThirdPerson);
        }
    }

    void UpdateLocalEffects()
    {
        float deltaTime = Mathf.Max(0.0001f, Time.unscaledDeltaTime);
        Vector3 velocity = CurrentPresentationVelocity();
        Vector3 localAcceleration = Vector3.zero;
        if (velocityInitialized && !cinematicMotionOverrideActive)
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

        if (ship != null && ship.FreeLookHeld)
        {
            freeLookAngles.y += Input.GetAxisRaw("Mouse X") * freeLookSensitivity;
            freeLookAngles.x -= Input.GetAxisRaw("Mouse Y") * freeLookSensitivity;
            if (effectiveMode == SpaceflightCameraMode.Cockpit)
            {
                freeLookAngles.y = Mathf.Clamp(
                    freeLookAngles.y,
                    cockpitHorizontalLookLimits.x,
                    cockpitHorizontalLookLimits.y);
                freeLookAngles.x = Mathf.Clamp(
                    freeLookAngles.x,
                    cockpitVerticalLookLimits.x,
                    cockpitVerticalLookLimits.y);
            }
            else
            {
                freeLookAngles.x = Mathf.Clamp(freeLookAngles.x, -65f, 65f);
                freeLookAngles.y = Mathf.Repeat(freeLookAngles.y + 180f, 360f) - 180f;
            }
        }
        else
        {
            freeLookAngles = Vector2.Lerp(
                freeLookAngles,
                Vector2.zero,
                1f - Mathf.Exp(-7f * deltaTime));
        }
    }

    Vector3 CurrentPresentationVelocity()
    {
        if (runtime != null)
            return runtime.ShipRelativeVelocityMetersPerSecond;
        return targetBody == null ? Vector3.zero : targetBody.velocity;
    }

    void ResolveReferences()
    {
        if (runtime == null)
            runtime = FindObjectOfType<InterstellarFlightRuntime>();
        InterstellarShipController runtimeShip = runtime == null
            ? null
            : runtime.ShipController;
        if (runtimeShip != null && ship != runtimeShip)
        {
            ship = runtimeShip;
            target = ship.transform;
            defaultTarget = target;
            targetBody = ship.ShipBody;
            damageReceiver = ship.GetComponent<SpacecraftDamageReceiver>();
            hullController = ship.GetComponent<ShipHullController>();
            modularPilotAim = ship.GetComponent(
                typeof(UnityPlanet.ModularAssembly
                    .IRobocraftPilotAimSource))
                as UnityPlanet.ModularAssembly
                    .IRobocraftPilotAimSource;
            hasModularLocalBounds = false;
            modularPresentationDistance = 0f;
        }
        else if (ship == null)
            ship = FindObjectOfType<InterstellarShipController>();
        if (target == null && ship != null)
            target = ship.transform;
        if (defaultTarget == null && cinematicPresentationTarget == null)
            defaultTarget = target;
        if (targetBody == null && ship != null)
            targetBody = ship.GetComponent<Rigidbody>();
        if (damageReceiver == null && ship != null)
            damageReceiver = ship.GetComponent<SpacecraftDamageReceiver>();
        if (hullController == null && ship != null)
            hullController = ship.GetComponent<ShipHullController>();
        if (modularPilotAim == null && ship != null)
        {
            modularPilotAim = ship.GetComponent(
                typeof(UnityPlanet.ModularAssembly
                    .IRobocraftPilotAimSource))
                as UnityPlanet.ModularAssembly
                    .IRobocraftPilotAimSource;
        }
        if (targetCamera == null)
            targetCamera = GetComponentInChildren<Camera>(true) ?? Camera.main;
        RecalculateCockpitAnchor();
    }

    bool UsesModularFlightFraming()
    {
        return ship != null &&
               ship.IsModular &&
               target == ship.transform &&
               cinematicPresentationTarget == null &&
               targetCamera != null;
    }

    bool UsesArcadeAssist()
    {
        return UsesModularFlightFraming() &&
               modularPilotAim is ModularInterstellarControlAdapter adapter &&
               adapter.ArcadeAssistActive;
    }

    void UpdateModularFlightBounds(bool immediate)
    {
        if (!UsesModularFlightFraming())
            return;
        if (!immediate &&
            Time.unscaledTime < nextModularBoundsRefresh)
        {
            SmoothModularFlightBounds();
            return;
        }

        nextModularBoundsRefresh = Time.unscaledTime + 0.25f;
        if (!TryCaptureModularLocalBounds(out Bounds captured))
            return;
        modularTargetLocalBounds = captured;
        if (!hasModularLocalBounds || immediate)
        {
            modularLocalBounds = captured;
            hasModularLocalBounds = true;
            modularPresentationDistance = 0f;
            return;
        }
        SmoothModularFlightBounds();
    }

    void SmoothModularFlightBounds()
    {
        if (!hasModularLocalBounds)
            return;
        float deltaTime = Mathf.Max(0.0001f, Time.unscaledDeltaTime);
        bool expanding =
            modularTargetLocalBounds.size.sqrMagnitude >
            modularLocalBounds.size.sqrMagnitude;
        float sizeBlend = 1f - Mathf.Exp(
            -(expanding ? 12f : 2.8f) * deltaTime);
        float centerBlend = 1f - Mathf.Exp(-5f * deltaTime);
        modularLocalBounds.center = Vector3.Lerp(
            modularLocalBounds.center,
            modularTargetLocalBounds.center,
            centerBlend);
        modularLocalBounds.size = Vector3.Max(
            Vector3.one * 0.5f,
            Vector3.Lerp(
                modularLocalBounds.size,
                modularTargetLocalBounds.size,
                sizeBlend));
    }

    bool TryCaptureModularLocalBounds(out Bounds captured)
    {
        captured = default;
        if (target == null)
            return false;

        bool found = false;
        Renderer[] renderers =
            target.GetComponentsInChildren<Renderer>(true);
        for (int index = 0; index < renderers.Length; index++)
        {
            Renderer renderer = renderers[index];
            if (renderer == null ||
                renderer is ParticleSystemRenderer ||
                renderer is LineRenderer ||
                renderer is TrailRenderer ||
                !renderer.enabled ||
                !renderer.gameObject.activeInHierarchy)
            {
                continue;
            }

            GridModuleView moduleView =
                renderer.GetComponentInParent<GridModuleView>();
            if (moduleView == null ||
                (moduleView.transform != target &&
                 !moduleView.transform.IsChildOf(target)))
            {
                continue;
            }

            // Renderer.bounds is a world-axis-aligned box. Converting its
            // corners back to ship space makes the measured ship grow and
            // shrink as the Rigidbody rotates. localBounds follows the actual
            // renderer hierarchy and therefore gives stable modular framing.
            Bounds rendererLocalBounds = renderer.localBounds;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 rendererLocalCorner =
                    rendererLocalBounds.center +
                    Vector3.Scale(
                        rendererLocalBounds.extents,
                        new Vector3(x, y, z));
                Vector3 worldCorner =
                    renderer.transform.TransformPoint(
                        rendererLocalCorner);
                Vector3 localCorner =
                    target.InverseTransformPoint(worldCorner);
                if (!found)
                {
                    captured = new Bounds(
                        localCorner,
                        Vector3.zero);
                    found = true;
                }
                else
                    captured.Encapsulate(localCorner);
            }
        }

        if (!found)
            return false;

        Vector3 safeMinimum = Vector3.Max(
            captured.min,
            new Vector3(-24f, -24f, -24f));
        Vector3 safeMaximum = Vector3.Min(
            captured.max,
            new Vector3(24f, 24f, 24f));
        if (safeMinimum.x > safeMaximum.x ||
            safeMinimum.y > safeMaximum.y ||
            safeMinimum.z > safeMaximum.z)
        {
            return false;
        }
        captured.SetMinMax(safeMinimum, safeMaximum);
        return true;
    }

    void ResolveModularThirdPersonPose(
        Quaternion freeLookRotation,
        bool immediate,
        out Vector3 position,
        out Quaternion rotation)
    {
        Quaternion rawRotation;
        if (modularPilotAim != null &&
            modularPilotAim.TryGetPilotAim(out Vector3 pilotAim) &&
            pilotAim.sqrMagnitude > 0.0001f)
        {
            rawRotation = Quaternion.LookRotation(
                pilotAim.normalized,
                Vector3.up) * freeLookRotation;
        }
        else
        {
            rawRotation = target.rotation * freeLookRotation;
        }
        Vector3 forward = rawRotation * Vector3.forward;
        Vector3 rolledUp = rawRotation * Vector3.up;
        Vector3 viewUp = Vector3.Slerp(
            Vector3.up,
            rolledUp,
            rollFollow).normalized;
        if (Mathf.Abs(Vector3.Dot(forward, viewUp)) > 0.985f)
            viewUp = rolledUp;
        rotation = Quaternion.LookRotation(forward, viewUp);

        ResolveModularThirdPersonPosition(
            rotation,
            immediate,
            out position);
    }

    void ResolveModularThirdPersonPosition(
        Quaternion viewRotation,
        bool immediate,
        out Vector3 position)
    {

        Bounds bounds = hasModularLocalBounds
            ? modularLocalBounds
            : new Bounds(Vector3.zero, new Vector3(3f, 2.2f, 6f));
        ResolveModularViewGeometry(
            bounds,
            viewRotation,
            out Vector3 worldCenter,
            out float halfWidth,
            out float halfHeight,
            out float halfDepth,
            out float radius);
        float targetDistance = ResolveModularPresentationDistance(
            halfWidth,
            halfHeight,
            halfDepth,
            radius);
        if (immediate || modularPresentationDistance <= 0.01f)
            modularPresentationDistance = targetDistance;
        else
        {
            modularPresentationDistance = Mathf.Lerp(
                modularPresentationDistance,
                targetDistance,
                1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));
        }

        float verticalTangent = Mathf.Tan(
            targetCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float horizontalTangent =
            verticalTangent * Mathf.Max(0.5f, targetCamera.aspect);
        float localX =
            (ModularFlightAnchor.x - 0.5f) *
            2f * modularPresentationDistance * horizontalTangent;
        float localY =
            (ModularFlightAnchor.y - 0.5f) *
            2f * modularPresentationDistance * verticalTangent;
        position = worldCenter -
                   viewRotation * new Vector3(
                       localX,
                       localY,
                       modularPresentationDistance);
        position += viewRotation * (localRecoil + collisionKick);
    }

    void ResolveModularViewGeometry(
        Bounds localBounds,
        Quaternion viewRotation,
        out Vector3 worldCenter,
        out float halfWidth,
        out float halfHeight,
        out float halfDepth,
        out float radius)
    {
        worldCenter = target.TransformPoint(localBounds.center);
        halfWidth = 0f;
        halfHeight = 0f;
        halfDepth = 0f;
        radius = 0f;
        Vector3 viewRight = viewRotation * Vector3.right;
        Vector3 viewUp = viewRotation * Vector3.up;
        Vector3 viewForward = viewRotation * Vector3.forward;
        for (int x = -1; x <= 1; x += 2)
        for (int y = -1; y <= 1; y += 2)
        for (int z = -1; z <= 1; z += 2)
        {
            Vector3 localCorner = localBounds.center +
                                  Vector3.Scale(
                                      localBounds.extents,
                                      new Vector3(x, y, z));
            Vector3 relative =
                target.TransformPoint(localCorner) - worldCenter;
            halfWidth = Mathf.Max(
                halfWidth,
                Mathf.Abs(Vector3.Dot(relative, viewRight)));
            halfHeight = Mathf.Max(
                halfHeight,
                Mathf.Abs(Vector3.Dot(relative, viewUp)));
            halfDepth = Mathf.Max(
                halfDepth,
                Mathf.Abs(Vector3.Dot(relative, viewForward)));
            radius = Mathf.Max(radius, relative.magnitude);
        }
    }

    float ResolveModularPresentationDistance(
        float halfWidth,
        float halfHeight,
        float halfDepth,
        float radius)
    {
        float allowedHalfHeight = Mathf.Max(
            0.105f,
            Mathf.Min(
                ModularFlightAnchor.y - 0.04f,
                0.415f - ModularFlightAnchor.y));
        float allowedHalfWidth = Mathf.Max(
            0.2f,
            Mathf.Min(
                ModularFlightAnchor.x - 0.03f,
                0.97f - ModularFlightAnchor.x));
        float verticalTangent = Mathf.Tan(
            Mathf.Clamp(targetCamera.fieldOfView, 18f, 80f) *
            0.5f * Mathf.Deg2Rad);
        float horizontalTangent =
            verticalTangent * Mathf.Max(0.5f, targetCamera.aspect);
        float verticalDistance =
            halfHeight /
            Mathf.Max(
                0.01f,
                2f * allowedHalfHeight * verticalTangent);
        float horizontalDistance =
            halfWidth /
            Mathf.Max(
                0.01f,
                2f * allowedHalfWidth * horizontalTangent);
        return Mathf.Clamp(
            Mathf.Max(verticalDistance, horizontalDistance) +
            halfDepth +
            0.75f,
            Mathf.Max(8f, radius * 1.15f),
            180f);
    }

    void ResolveCockpit()
    {
        if (cockpit == null)
            cockpit = GetComponent<SpaceflightCockpitController>();
        if (cockpit == null)
            cockpit = gameObject.AddComponent<SpaceflightCockpitController>();
        cockpit.Configure(ship, this, FindObjectOfType<InterstellarFlightHud>());
    }

    void RecalculateCockpitAnchor()
    {
        Vector3 dimensions = hullController != null && hullController.CurrentHull != null
            ? hullController.CurrentHull.Dimensions
            : hullController != null
                ? hullController.LocalBounds.size
                : ship != null
                    ? ship.VisualBounds.size
                    : new Vector3(3f, 2.2f, 6f);
        cockpitAnchorLocal = new Vector3(
            0f,
            Mathf.Clamp(dimensions.y * 0.32f, 0.42f, 0.9f),
            Mathf.Clamp(dimensions.z * 0.16f, 0.62f, 1.25f));
        if (cockpit != null)
            cockpit.RefreshShipFit();
    }

    void HandleHullChanged(ShipHullDefinition definition)
    {
        RecalculateCockpitAnchor();
        cockpit?.RefreshShipFit();
    }

    void HandleOriginShift(Vector3 shift)
    {
        transform.position -= shift;
        SnapToTarget();
    }

    void HandleUniverseRelocated(DoubleVector3 previousPosition, DoubleVector3 currentPosition)
    {
        SnapToTarget();
    }

    void HandleDamaged(float integrity, float maximumIntegrity, SpaceDamageInfo damage)
    {
        float strength = Mathf.Clamp(damage.amount * 0.025f, 0.05f, 0.45f);
        collisionKick += UnityEngine.Random.insideUnitSphere * strength;
    }

    static float SmoothStep01(float value)
    {
        value = Mathf.Clamp01(value);
        return value * value * (3f - 2f * value);
    }

    void OnDisable()
    {
        if (runtime != null)
        {
            runtime.OriginShifted -= HandleOriginShift;
            runtime.UniverseRelocated -= HandleUniverseRelocated;
        }
        if (damageReceiver != null)
            damageReceiver.Damaged -= HandleDamaged;
        if (hullController != null)
            hullController.HullChanged -= HandleHullChanged;
        cockpit?.SetVisible(false);
        cockpitPresentationVisible = false;
    }
}
