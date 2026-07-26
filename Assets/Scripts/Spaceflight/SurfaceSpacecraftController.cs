using System.Collections;
using SpacecraftEditor;
using UnityEngine;

public enum SurfaceSpacecraftPhase
{
    Parked,
    TakingOff,
    Travelling,
    Approaching,
    Landed,
    Hovering,
    Boarding,
    Departing,
    Piloting
}

[DisallowMultipleComponent]
public sealed class SurfaceSpacecraftController : MonoBehaviour
{
    const float BoardingDistance = 12f;
    const float MaximumPlausiblePartDistance = 250f;
    const float MaximumHoverBottomOffset = 60f;

    readonly Collider[] clearanceHits = new Collider[64];

    VoxelQuadSphereWorld world;
    IPlanetSurfaceRuntime surfaceRuntime;
    VoxelPlanetPlayerController player;
    ShipAssembly assembly;
    Rigidbody body;
    Collider[] shipColliders;
    Bounds localBounds;
    Coroutine activeRoutine;
    SurfaceSpacecraftState savedState;
    Camera pilotCamera;
    int pilotEnteredFrame;
    KeyboardMouseFlightInput planarFlightInput;
    SpacecraftIfcsMotor planarIfcs;
    PlanetSurfaceFlightEnvironment planarEnvironment;
    SurfaceFlightCameraRig surfaceCameraRig;
    PlanetaryGravitySupportVfx gravitySupportVfx;
    float pilotSaveTimer;
    bool pilotMenuOpen;
    bool cityPresentationActive;

    public static SurfaceSpacecraftController Current { get; private set; }
    public SurfaceSpacecraftPhase Phase { get; private set; } =
        SurfaceSpacecraftPhase.Parked;
    public string StatusText { get; private set; } = string.Empty;
    public bool IsMoving => Phase == SurfaceSpacecraftPhase.TakingOff
        || Phase == SurfaceSpacecraftPhase.Travelling
        || Phase == SurfaceSpacecraftPhase.Approaching;
    public bool IsDeparting => Phase == SurfaceSpacecraftPhase.Boarding
        || Phase == SurfaceSpacecraftPhase.Departing;
    public bool IsPiloting => Phase == SurfaceSpacecraftPhase.Piloting;
    public SurfaceFlightCameraRig SurfaceCameraRig => surfaceCameraRig;
    public bool CanBeCalled => !IsMoving && !IsDeparting && !IsPiloting;
    public string BoardPromptText =>
        surfaceRuntime != null
        && surfaceRuntime.Topology == PlanetSurfaceTopology.InfinitePlanar
            ? "按 F 登上飞船并驾驶"
            : "按 F 登上飞船并返回宇宙";
    public Vector3 TrackingPosition =>
        assembly != null ? assembly.transform.position : transform.position;
    public string TrackingStatusText
    {
        get
        {
            switch (Phase)
            {
                case SurfaceSpacecraftPhase.TakingOff:
                    return "正在起飞";
                case SurfaceSpacecraftPhase.Travelling:
                    return "正在赶来";
                case SurfaceSpacecraftPhase.Approaching:
                    return "正在降落";
                case SurfaceSpacecraftPhase.Hovering:
                    return "悬停中";
                case SurfaceSpacecraftPhase.Boarding:
                    return "正在登船";
                case SurfaceSpacecraftPhase.Departing:
                    return "正在离开";
                case SurfaceSpacecraftPhase.Piloting:
                    return "驾驶中";
                case SurfaceSpacecraftPhase.Landed:
                    return "已着陆";
                default:
                    return "已停泊";
            }
        }
    }

    public void Initialize(
        VoxelQuadSphereWorld targetWorld,
        VoxelPlanetPlayerController surfacePlayer,
        ShipAssembly shipAssembly,
        SurfaceSpacecraftState restoredState)
    {
        world = targetWorld;
        player = surfacePlayer;
        assembly = shipAssembly;
        body = assembly != null ? assembly.ShipBody : null;
        shipColliders = GetComponentsInChildren<Collider>(true);
        localBounds = CalculateLocalBounds(
            assembly != null ? assembly.transform : transform);
        savedState = restoredState != null
            ? restoredState.Clone()
            : CaptureState(SurfaceSpacecraftParkingMode.Terrain, 6f);
        savedState.ClampValues();
        Current = this;

        if (savedState.valid
            && savedState.parkingMode == SurfaceSpacecraftParkingMode.Hovering)
        {
            RestoreHoverState(savedState);
        }
        else
        {
            Phase = savedState.parkingMode == SurfaceSpacecraftParkingMode.Terrain
                || savedState.parkingMode == SurfaceSpacecraftParkingMode.OceanPlatform
                ? SurfaceSpacecraftPhase.Landed
                : SurfaceSpacecraftPhase.Parked;
        }
    }

    public void Initialize(
        IPlanetSurfaceRuntime targetRuntime,
        VoxelPlanetPlayerController surfacePlayer,
        ShipAssembly shipAssembly,
        SurfaceSpacecraftState restoredState)
    {
        surfaceRuntime = targetRuntime;
        world = null;
        player = surfacePlayer;
        assembly = shipAssembly;
        body = assembly != null ? assembly.ShipBody : null;
        shipColliders = GetComponentsInChildren<Collider>(true);
        localBounds = CalculateLocalBounds(
            assembly != null ? assembly.transform : transform);
        savedState = restoredState != null
            ? restoredState.Clone()
            : CaptureState(
                SurfaceSpacecraftParkingMode.Terrain,
                6f);
        savedState.ClampValues();
        Current = this;
        ResolvePlanarFlightSystems();

        if (savedState.valid
            && savedState.parkingMode
                == SurfaceSpacecraftParkingMode.Hovering)
        {
            RestoreHoverState(savedState);
        }
        else
        {
            Phase = savedState.parkingMode
                    == SurfaceSpacecraftParkingMode.Terrain
                || savedState.parkingMode
                    == SurfaceSpacecraftParkingMode.OceanPlatform
                    ? SurfaceSpacecraftPhase.Landed
                    : SurfaceSpacecraftPhase.Parked;
        }
    }

    public bool IsWithinDistance(Vector3 position, float distance)
    {
        Transform ship = assembly != null ? assembly.transform : transform;
        return (ship.position - position).sqrMagnitude <= distance * distance;
    }

    public bool IsBoardingReachable(Vector3 position, float distance = BoardingDistance)
    {
        float maximumSqrDistance = distance * distance;
        if (shipColliders != null)
        {
            for (int i = 0; i < shipColliders.Length; i++)
            {
                Collider collider = shipColliders[i];
                if (collider == null
                    || !collider.enabled
                    || !collider.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if ((collider.ClosestPoint(position) - position).sqrMagnitude
                    <= maximumSqrDistance)
                {
                    return true;
                }
            }
        }

        Transform ship = assembly != null ? assembly.transform : transform;
        Renderer[] renderers = ship.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (IsShipGeometryRenderer(renderers[i])
                && renderers[i].bounds.SqrDistance(position)
                    <= maximumSqrDistance)
            {
                return true;
            }
        }
        return false;
    }

    public bool CallToPlayer(VoxelPlanetPlayerController targetPlayer)
    {
        if (!CanBeCalled
            || targetPlayer == null
            || (world == null && surfaceRuntime == null)
            || assembly == null)
            return false;
        if (IsBoardingReachable(targetPlayer.transform.position))
            return false;

        if (activeRoutine != null)
            StopCoroutine(activeRoutine);
        activeRoutine = StartCoroutine(CallRoutine(targetPlayer));
        return true;
    }

    public bool CanBoardFrom(Camera camera, float distance)
    {
        if (camera == null
            || IsMoving
            || IsDeparting
            || (Phase != SurfaceSpacecraftPhase.Landed
                && Phase != SurfaceSpacecraftPhase.Parked
                && Phase != SurfaceSpacecraftPhase.Hovering))
        {
            return false;
        }

        Ray ray = new Ray(
            camera.transform.position,
            camera.transform.forward);
        if (!Physics.Raycast(
            ray,
            out RaycastHit hit,
            Mathf.Max(0.1f, distance),
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Collide))
        {
            return false;
        }
        return hit.collider != null
            && hit.collider.GetComponentInParent<SurfaceSpacecraftController>() == this;
    }

    public bool BeginSurfaceDeparture()
    {
        if (surfaceRuntime != null
            && surfaceRuntime.Topology
                == PlanetSurfaceTopology.InfinitePlanar)
        {
            return BeginPlanarPilot();
        }

        GalaxyTravelManager manager = GalaxyTravelManager.Instance;
        if (IsMoving
            || IsDeparting
            || (world == null && surfaceRuntime == null)
            || assembly == null
            || manager == null
            || !manager.IsInterstellarGalaxy)
            return false;
        if (activeRoutine != null)
            StopCoroutine(activeRoutine);
        activeRoutine = StartCoroutine(DepartureRoutine());
        return true;
    }

    public bool BeginInterstellarDeparture()
    {
        GalaxyTravelManager manager = GalaxyTravelManager.Instance;
        if (surfaceRuntime == null
            || surfaceRuntime.Topology
                != PlanetSurfaceTopology.InfinitePlanar
            || IsMoving
            || IsDeparting
            || assembly == null
            || manager == null
            || !manager.IsInterstellarGalaxy)
        {
            return false;
        }

        if (activeRoutine != null)
            StopCoroutine(activeRoutine);
        activeRoutine = StartCoroutine(PlanarDepartureRoutine());
        return true;
    }

    bool BeginPlanarPilot()
    {
        if (IsMoving
            || IsDeparting
            || IsPiloting
            || surfaceRuntime == null
            || assembly == null
            || player == null)
        {
            return false;
        }

        SurfaceMultifunctionController multifunction =
            player.GetComponent<SurfaceMultifunctionController>();
        multifunction?.SetPilotMenuContext(true);
        player.CaptureFirstPersonCameraState();
        player.SetGameplayInputBlocked(true);
        player.SetSurfacePhysicsReady(false);
        player.enabled = false;

        Transform ship = assembly.transform;
        pilotCamera = Camera.main;
        if (pilotCamera != null)
        {
            Vector3 chasePosition = new Vector3(
                0f,
                Mathf.Max(4.5f, localBounds.max.y + 3f),
                Mathf.Min(-10f, localBounds.min.z - 8f));
            pilotCamera.transform.SetParent(null, true);
            pilotCamera.transform.position =
                ship.TransformPoint(chasePosition);
            pilotCamera.transform.rotation = Quaternion.LookRotation(
                ship.TransformPoint(localBounds.center)
                    - pilotCamera.transform.position,
                Vector3.up);
            pilotCamera.clearFlags = CameraClearFlags.Skybox;
            surfaceCameraRig =
                pilotCamera.GetComponent<SurfaceFlightCameraRig>()
                ?? pilotCamera.gameObject.AddComponent<SurfaceFlightCameraRig>();
            surfaceCameraRig.Activate(
                pilotCamera,
                ship,
                body,
                surfaceRuntime,
                localBounds);
        }

        pilotEnteredFrame = Time.frameCount;
        pilotSaveTimer = 0f;
        SetPlanarPilotPhysics(true);
        (surfaceRuntime as InfinitePlanarSurfaceWorld)
            ?.SetMovementTarget(ship);
        Phase = SurfaceSpacecraftPhase.Piloting;
        StatusText =
            "IFCS驾驶｜WASD平移 空格/Ctrl升降 鼠标转向 "
            + "Q/E滚转 X制动 Shift加速 F着陆 L离开星球";
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        return true;
    }

    void Update()
    {
        if (!IsPiloting || assembly == null)
            return;

        bool acceptsPilotActions =
            !pilotMenuOpen && !cityPresentationActive;
        if (acceptsPilotActions
            && Time.frameCount > pilotEnteredFrame
            && Input.GetKeyDown(KeyCode.F))
        {
            TryBeginPlanarLanding();
            return;
        }
        if (acceptsPilotActions
            && Input.GetKeyDown(KeyCode.L))
        {
            BeginInterstellarDeparture();
            return;
        }
        if (acceptsPilotActions
            && planarFlightInput != null
            && planarFlightInput.ConsumeSecondaryActionPressed())
        {
            InfinitePlanarSurfaceWorld planar =
                surfaceRuntime as InfinitePlanarSurfaceWorld;
            string failureReason = string.Empty;
            bool dropped = planar != null
                && planar.TryDropPlaceholderCube(
                    body,
                    shipColliders,
                    out failureReason);
            if (dropped)
            {
                StatusText = "已投放物理方块";
            }
            else
            {
                StatusText = string.IsNullOrWhiteSpace(failureReason)
                    ? "无法投放物理方块"
                    : failureReason;
            }
            return;
        }

        UpdatePilotStatus();
        pilotSaveTimer += Time.unscaledDeltaTime;
        if (pilotSaveTimer >= 2f)
        {
            pilotSaveTimer = 0f;
            SaveState(
                SurfaceSpacecraftParkingMode.Hovering,
                planarEnvironment != null
                    ? Mathf.Max(0f, planarEnvironment.Altitude)
                    : 6f,
                false);
        }
    }

    public void SetPilotMenuOpen(bool open)
    {
        pilotMenuOpen = open;
        RefreshPlanarInputCapture();
    }

    public void SetCityPresentationActive(bool active)
    {
        cityPresentationActive = active;
        RefreshPlanarInputCapture();
    }

    void RefreshPlanarInputCapture()
    {
        if (planarFlightInput == null)
            return;
        bool capture = IsPiloting
            && !pilotMenuOpen
            && !cityPresentationActive;
        planarFlightInput.CaptureEnabled = capture;
        if (!capture)
            planarFlightInput.ClearTransientRequests();
    }

    void TryBeginPlanarLanding()
    {
        float footprint =
            Mathf.Max(localBounds.extents.x, localBounds.extents.z) + 2f;
        if (!surfaceRuntime.TryFindLandingPoint(
                assembly.transform.position,
                footprint,
                12f,
                out PlanetSurfaceSample surface))
        {
            StatusText =
                "附近没有安全陆地｜继续驾驶，或按 L 离开星球";
            return;
        }

        Vector3 forward = Vector3.ProjectOnPlane(
            assembly.transform.forward,
            Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.forward;
        Quaternion landingRotation =
            Quaternion.LookRotation(forward, Vector3.up);
        Vector3 landingPosition =
            RootPositionOnSurface(surface.point, landingRotation);
        activeRoutine = StartCoroutine(
            PlanarLandingRoutine(
                landingPosition,
                landingRotation));
    }

    IEnumerator PlanarLandingRoutine(
        Vector3 landingPosition,
        Quaternion landingRotation)
    {
        Transform ship = assembly.transform;
        SetPlanarPilotPhysics(false);
        SetFlightPhysics(true);
        Vector3 startPosition = ship.position;
        Quaternion startRotation = ship.rotation;
        Phase = SurfaceSpacecraftPhase.Approaching;
        StatusText = "正在着陆";
        float elapsed = 0f;
        const float duration = 1.25f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float ratio = Smooth01(elapsed / duration);
            Vector3 position =
                Vector3.Lerp(startPosition, landingPosition, ratio);
            position.y += Mathf.Sin(ratio * Mathf.PI) * 4f;
            SetShipPose(
                position,
                Quaternion.Slerp(
                    startRotation,
                    landingRotation,
                    ratio));
            yield return null;
        }

        SetShipPose(landingPosition, landingRotation);
        SetFlightPhysics(false);
        Phase = SurfaceSpacecraftPhase.Landed;
        StatusText = string.Empty;
        SaveState(
            SurfaceSpacecraftParkingMode.Terrain,
            0f,
            true);
        RestorePlayerAfterPilot();
        activeRoutine = null;
    }

    void RestorePlayerAfterPilot()
    {
        if (player == null || assembly == null)
            return;

        Transform ship = assembly.transform;
        float sideDistance =
            Mathf.Max(localBounds.extents.x, localBounds.extents.z) + 3f;
        Vector3 near = ship.position + ship.right * sideDistance;
        Vector3 playerPoint = near;
        if (surfaceRuntime.TryFindLandingPoint(
                near,
                1f,
                45f,
                out PlanetSurfaceSample surface))
        {
            playerPoint = surface.point + Vector3.up * 1.2f;
        }
        player.SetSurfacePhysicsReady(true);
        player.EnterFirstPersonSurfaceMode();
        player.TeleportTo(
            playerPoint,
            Quaternion.LookRotation(
                Vector3.ProjectOnPlane(ship.forward, Vector3.up).normalized,
                Vector3.up));
        player.SetGameplayInputBlocked(false);
        SurfaceMultifunctionController multifunction =
            player.GetComponent<SurfaceMultifunctionController>();
        multifunction?.SetPilotMenuContext(false);
        multifunction?.SetInputBlocked(false);
        pilotMenuOpen = false;
        cityPresentationActive = false;
        (surfaceRuntime as InfinitePlanarSurfaceWorld)
            ?.SetMovementTarget(player.transform);
        surfaceCameraRig?.Deactivate();
        surfaceCameraRig = null;
        pilotCamera = null;
    }

    void LateUpdate()
    {
        if (surfaceCameraRig != null && surfaceCameraRig.IsActive)
            return;
        if (!IsPiloting || pilotCamera == null || assembly == null)
            return;
        Transform ship = assembly.transform;
        float distance = Mathf.Max(
            12f,
            localBounds.extents.magnitude * 2.1f);
        float height = Mathf.Max(5f, localBounds.extents.y + 3f);
        Vector3 desiredPosition =
            ship.position - ship.forward * distance + Vector3.up * height;
        pilotCamera.transform.position = Vector3.Lerp(
            pilotCamera.transform.position,
            desiredPosition,
            1f - Mathf.Exp(-7f * Time.deltaTime));
        pilotCamera.transform.rotation = Quaternion.Slerp(
            pilotCamera.transform.rotation,
            Quaternion.LookRotation(
                ship.position + Vector3.up * 1.5f
                    - pilotCamera.transform.position,
                Vector3.up),
            1f - Mathf.Exp(-9f * Time.deltaTime));
    }

    void UpdatePilotStatus()
    {
        if (planarEnvironment == null || body == null)
            return;
        SpacecraftControlTelemetry telemetry =
            planarIfcs != null ? planarIfcs.Telemetry : default;
        string warning = planarEnvironment.IsStalling
            ? "  失速"
            : telemetry.gravitySupportLoad > 0.94f
                ? "  高重力支撑接近上限"
                : telemetry.controlAuthority < 0.45f
                    ? "  推进器/质量导致控制余量不足"
                    : string.Empty;
        StatusText = string.Format(
            "IFCS驾驶｜空速 {0:0} m/s  升降 {1:+0.0;-0.0;0.0} m/s  高度 {2:0} m  "
            + "重力 {3:0.0} m/s²  支撑 {4:0}%  控制 {5:0}%  迎角 {6:+0;-0;0}°{7}｜F着陆 L离开",
            planarEnvironment.AirSpeed,
            telemetry.planetaryFlightActive
                ? telemetry.verticalSpeed
                : Vector3.Dot(body.velocity, Vector3.up),
            planarEnvironment.Altitude,
            planarEnvironment.GravityAcceleration.magnitude,
            telemetry.gravitySupportLoad * 100f,
            telemetry.controlAuthority * 100f,
            planarEnvironment.AngleOfAttack,
            warning);
    }

    void ResolvePlanarFlightSystems()
    {
        if (surfaceRuntime == null
            || surfaceRuntime.Topology
                != PlanetSurfaceTopology.InfinitePlanar
            || assembly == null
            || body == null)
        {
            return;
        }

        GameObject flightRoot = assembly.gameObject;
        planarFlightInput =
            flightRoot.GetComponent<KeyboardMouseFlightInput>()
            ?? flightRoot.AddComponent<KeyboardMouseFlightInput>();
        planarIfcs =
            flightRoot.GetComponent<SpacecraftIfcsMotor>()
            ?? flightRoot.AddComponent<SpacecraftIfcsMotor>();
        ShipHullController hull =
            flightRoot.GetComponentInChildren<ShipHullController>(true);
        planarFlightInput.enabled = true;
        planarIfcs.enabled = true;
        planarIfcs.Configure(
            body,
            assembly,
            hull,
            planarFlightInput,
            false);
        planarIfcs.SetAssistMode(SpacecraftAssistMode.Coupled);
        planarIfcs.SetTargetSpeed(45f);
        planarIfcs.ControlsEnabled = false;

        planarEnvironment =
            flightRoot.GetComponent<PlanetSurfaceFlightEnvironment>()
            ?? flightRoot.AddComponent<PlanetSurfaceFlightEnvironment>();
        InfinitePlanarSurfaceWorld planar =
            surfaceRuntime as InfinitePlanarSurfaceWorld;
        planarEnvironment.Configure(
            body,
            planarIfcs,
            surfaceRuntime,
            planar != null && planar.Definition != null
                ? planar.Definition.celestial
                : PlanetCelestialProfile.CreateCompatibleDefault(),
            localBounds);
        planarEnvironment.enabled = false;
        gravitySupportVfx =
            flightRoot.GetComponent<PlanetaryGravitySupportVfx>()
            ?? flightRoot.AddComponent<PlanetaryGravitySupportVfx>();
        gravitySupportVfx.Configure(planarIfcs, localBounds);
        gravitySupportVfx.enabled = false;
    }

    void SetPlanarPilotPhysics(bool enabled)
    {
        if (body != null)
        {
            if (!body.isKinematic)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.isKinematic = !enabled;
            body.useGravity = false;
            body.interpolation = enabled
                ? RigidbodyInterpolation.Interpolate
                : RigidbodyInterpolation.None;
            body.collisionDetectionMode = enabled
                ? CollisionDetectionMode.ContinuousDynamic
                : CollisionDetectionMode.Discrete;
        }
        if (shipColliders != null)
        {
            for (int index = 0; index < shipColliders.Length; index++)
            {
                if (shipColliders[index] != null)
                    shipColliders[index].enabled = true;
            }
        }
        if (planarEnvironment != null)
            planarEnvironment.enabled = enabled;
        if (gravitySupportVfx != null)
            gravitySupportVfx.enabled = enabled;
        if (planarIfcs != null)
        {
            planarIfcs.SetEnvironmentalAcceleration(Vector3.zero);
            planarIfcs.ResetControllerState();
            planarIfcs.ControlsEnabled = enabled;
        }
        if (planarFlightInput != null)
        {
            planarFlightInput.CaptureEnabled = enabled
                && !pilotMenuOpen
                && !cityPresentationActive;
        }
    }

    IEnumerator CallRoutine(VoxelPlanetPlayerController targetPlayer)
    {
        if (surfaceRuntime != null
            && surfaceRuntime.Topology
                == PlanetSurfaceTopology.InfinitePlanar)
        {
            yield return PlanarCallRoutine(targetPlayer);
            yield break;
        }

        player = targetPlayer;
        Transform ship = assembly.transform;
        Vector3 center = world.GetPlanetCenterWorld();
        Vector3 startPosition = ship.position;
        Quaternion startRotation = ship.rotation;
        Vector3 startDirection = SafeDirection(startPosition - center, Vector3.up);

        StatusText = "飞船起飞";
        Phase = SurfaceSpacecraftPhase.TakingOff;
        SetFlightPhysics(true);

        bool hasLanding = TryFindLandingPose(targetPlayer, out FlightDestination destination);
        if (!hasLanding)
            destination = BuildHoverDestination(targetPlayer);

        float angle = Vector3.Angle(startDirection, destination.direction);
        float totalDuration = CalculateRecallDuration(angle);
        float departureDuration = 1f;
        float approachDuration = 1.35f;
        float travelDuration = Mathf.Max(1.65f, totalDuration - departureDuration - approachDuration);
        float startRadius = (startPosition - center).magnitude;
        float targetRadius = (destination.rootPosition - center).magnitude;
        float cruiseRadius = Mathf.Max(
            Mathf.Max(startRadius, targetRadius) + 30f,
            world.PlanetRadius + 80f);
        Vector3 departurePoint = center + startDirection * cruiseRadius;
        Vector3 arrivalPoint = center + destination.direction * cruiseRadius;

        yield return AnimatePose(
            startPosition,
            departurePoint,
            startRotation,
            Quaternion.LookRotation(
                GreatCircleTangent(startDirection, destination.direction, ship.forward),
                startDirection),
            departureDuration,
            true);

        Phase = SurfaceSpacecraftPhase.Travelling;
        StatusText = "飞船正在赶来";
        float elapsed = 0f;
        while (elapsed < travelDuration)
        {
            elapsed += Time.deltaTime;
            float ratio = Smooth01(elapsed / travelDuration);
            Vector3 direction = GreatCircleDirection(
                startDirection,
                destination.direction,
                ratio,
                ship.forward);
            Vector3 tangent = GreatCircleTangent(
                direction,
                destination.direction,
                ship.forward);
            SetShipPose(
                center + direction * cruiseRadius,
                Quaternion.LookRotation(tangent, direction));
            yield return null;
        }

        Phase = SurfaceSpacecraftPhase.Approaching;
        StatusText = hasLanding ? "搜索着陆点" : "附近无安全着陆点";
        Quaternion targetRotation = destination.rotation;
        yield return AnimatePose(
            arrivalPoint,
            destination.rootPosition,
            ship.rotation,
            targetRotation,
            approachDuration,
            false);

        SetShipPose(destination.rootPosition, targetRotation);
        SetFlightPhysics(false);
        Physics.SyncTransforms();
        if (!hasLanding
            && !IsBoardingReachable(
                targetPlayer.transform.position,
                BoardingDistance))
        {
            Debug.LogWarning(
                "SurfaceSpacecraftController: the calculated hover destination " +
                "was not reachable from the player. Applying the emergency " +
                "near-player hover pose.",
                this);
            destination = BuildHoverDestination(
                targetPlayer,
                6f,
                3.5f);
            targetRotation = destination.rotation;
            SetShipPose(destination.rootPosition, targetRotation);
            Physics.SyncTransforms();
        }
        Phase = hasLanding
            ? SurfaceSpacecraftPhase.Landed
            : SurfaceSpacecraftPhase.Hovering;
        StatusText = "飞船已抵达";
        SaveState(
            hasLanding
                ? SurfaceSpacecraftParkingMode.Terrain
                : SurfaceSpacecraftParkingMode.Hovering,
            hasLanding ? 6f : destination.hoverAltitude,
            true);
        activeRoutine = null;
        yield return new WaitForSeconds(1.5f);
        StatusText = string.Empty;
    }

    IEnumerator DepartureRoutine()
    {
        if (surfaceRuntime != null
            && surfaceRuntime.Topology
                == PlanetSurfaceTopology.InfinitePlanar)
        {
            yield return PlanarDepartureRoutine();
            yield break;
        }

        Phase = SurfaceSpacecraftPhase.Boarding;
        StatusText = "正在登船";
        SurfaceMultifunctionController multifunction =
            player != null ? player.GetComponent<SurfaceMultifunctionController>() : null;
        multifunction?.SetInputBlocked(true);
        if (player != null)
        {
            player.SetGameplayInputBlocked(true);
            player.SetSurfacePhysicsReady(false);
            player.enabled = false;
        }

        Camera camera = Camera.main;
        Transform cameraTransform = camera != null ? camera.transform : null;
        Transform oldCameraParent = cameraTransform != null ? cameraTransform.parent : null;
        Vector3 oldCameraLocalPosition = cameraTransform != null
            ? cameraTransform.localPosition
            : Vector3.zero;
        Quaternion oldCameraLocalRotation = cameraTransform != null
            ? cameraTransform.localRotation
            : Quaternion.identity;
        float oldFov = camera != null ? camera.fieldOfView : 60f;
        Transform ship = assembly.transform;
        if (cameraTransform != null)
        {
            cameraTransform.SetParent(ship, true);
            cameraTransform.localPosition = new Vector3(7.5f, 4.2f, -12f);
            cameraTransform.localRotation = Quaternion.LookRotation(
                new Vector3(-7.5f, -2.5f, 12f).normalized,
                Vector3.up);
        }

        Phase = SurfaceSpacecraftPhase.Departing;
        StatusText = "离开星球";
        SetFlightPhysics(true);
        Vector3 center = world.GetPlanetCenterWorld();
        Vector3 start = ship.position;
        Vector3 radial = SafeDirection(start - center, ship.up);
        Vector3 tangent = Vector3.ProjectOnPlane(ship.forward, radial).normalized;
        if (tangent.sqrMagnitude < 0.001f)
            tangent = Vector3.Cross(radial, Vector3.right).normalized;
        Quaternion exitRotation = Quaternion.LookRotation(radial, tangent);
        Vector3 end = start + radial * 160f;
        float duration = 3f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float ratio = Mathf.Clamp01(elapsed / duration);
            float eased = ratio * ratio * (3f - 2f * ratio);
            SetShipPose(
                Vector3.Lerp(start, end, eased),
                Quaternion.Slerp(ship.rotation, exitRotation, eased));
            if (camera != null)
                camera.fieldOfView = Mathf.Lerp(oldFov, 52f, eased);
            if (ratio >= 0.8667f)
            {
                float fade = Mathf.InverseLerp(0.8667f, 1f, ratio);
                PersistentSpaceflightFade.Instance.SetBlackout(fade);
            }
            yield return null;
        }

        SaveState(
            savedState != null
                && savedState.parkingMode == SurfaceSpacecraftParkingMode.Hovering
                ? SurfaceSpacecraftParkingMode.Hovering
                : SurfaceSpacecraftParkingMode.Terrain,
            savedState != null ? savedState.hoverAltitude : 6f,
            true);
        PersistentSpaceflightFade.Instance.FadeInAfterNextScene(0.6f);
        GalaxyTravelManager manager = GalaxyTravelManager.Instance;
        if (manager != null && manager.IsInterstellarGalaxy)
        {
            manager.OpenInterstellarFlight(
                world,
                radial,
                exitRotation,
                radial * 120f);
        }
        else
        {
            PersistentSpaceflightFade.Instance.CancelPendingTransition();
            if (cameraTransform != null)
            {
                cameraTransform.SetParent(oldCameraParent, false);
                cameraTransform.localPosition = oldCameraLocalPosition;
                cameraTransform.localRotation = oldCameraLocalRotation;
            }
            if (camera != null)
                camera.fieldOfView = oldFov;
        }
        activeRoutine = null;
    }

    bool TryFindLandingPose(
        VoxelPlanetPlayerController targetPlayer,
        out FlightDestination destination)
    {
        destination = default;
        if (surfaceRuntime != null
            && surfaceRuntime.Topology
                == PlanetSurfaceTopology.InfinitePlanar)
        {
            Vector3 planarPlayerForward = Vector3.ProjectOnPlane(
                targetPlayer.transform.forward,
                Vector3.up).normalized;
            if (planarPlayerForward.sqrMagnitude < 0.001f)
                planarPlayerForward = Vector3.forward;
            Vector3 near =
                targetPlayer.transform.position
                + planarPlayerForward * 18f;
            if (!surfaceRuntime.TryFindLandingPoint(
                    near,
                    Mathf.Max(localBounds.extents.x, localBounds.extents.z)
                        + 2f,
                    12f,
                    out PlanetSurfaceSample surface))
            {
                return false;
            }
            Vector3 forward = Vector3.ProjectOnPlane(
                targetPlayer.transform.position - surface.point,
                Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.001f)
                forward = -planarPlayerForward;
            Quaternion rotation =
                Quaternion.LookRotation(forward, Vector3.up);
            Vector3 rootPosition =
                RootPositionOnSurface(surface.point, rotation);
            if (!HasClearance(
                    rootPosition,
                    rotation,
                    targetPlayer.transform))
            {
                return false;
            }
            destination = new FlightDestination
            {
                direction = Vector3.up,
                rootPosition = rootPosition,
                rotation = rotation,
                hoverAltitude = 0f
            };
            return true;
        }

        Vector3 center = world.GetPlanetCenterWorld();
        Vector3 playerDirection = SafeDirection(
            targetPlayer.transform.position - center,
            Vector3.up);
        Vector3 playerForward = Vector3.ProjectOnPlane(
            targetPlayer.transform.forward,
            playerDirection).normalized;
        if (playerForward.sqrMagnitude < 0.001f)
            playerForward = Vector3.Cross(playerDirection, Vector3.right).normalized;
        Vector3 right = Vector3.Cross(playerDirection, playerForward).normalized;
        float[] radii = { 14f, 19f, 24f, 30f };
        float planetRadius = Mathf.Max(1f, world.PlanetRadius);

        for (int ring = 0; ring < radii.Length; ring++)
        {
            for (int index = 0; index < 12; index++)
            {
                float phase = (index / 12f) * Mathf.PI * 2f + Mathf.PI;
                Vector3 offset = (
                    right * Mathf.Sin(phase)
                    + playerForward * Mathf.Cos(phase)) * radii[ring];
                Vector3 candidateDirection = (
                    playerDirection
                    + offset / planetRadius).normalized;
                if (!world.TryFindLoadedSurfacePoseNearDirection(
                    candidateDirection,
                    out VoxelTerrainSurfacePose surface))
                {
                    continue;
                }

                Vector3 up = SafeDirection(surface.Point - center, candidateDirection);
                if (Vector3.Angle(surface.Normal, up) > 12f)
                    continue;
                if (PlanetWaterRegistry.TrySampleAny(
                    surface.Point + up * 0.1f,
                    out WaterSample water)
                    && water.signedDistance <= 0.5f)
                {
                    continue;
                }

                Vector3 forward = Vector3.ProjectOnPlane(
                    targetPlayer.transform.position - surface.Point,
                    up).normalized;
                if (forward.sqrMagnitude < 0.001f)
                    forward = playerForward;
                Quaternion rotation = Quaternion.LookRotation(forward, up);
                Vector3 rootPosition = RootPositionOnSurface(
                    surface.Point,
                    rotation);
                if (!HasClearance(rootPosition, rotation, targetPlayer.transform))
                    continue;

                destination = new FlightDestination
                {
                    direction = up,
                    rootPosition = rootPosition,
                    rotation = rotation,
                    hoverAltitude = 0f
                };
                return true;
            }
        }
        return false;
    }

    FlightDestination BuildHoverDestination(
        VoxelPlanetPlayerController targetPlayer,
        float lateralDistance = 10f,
        float altitude = 6f)
    {
        if (surfaceRuntime != null
            && surfaceRuntime.Topology
                == PlanetSurfaceTopology.InfinitePlanar)
        {
            Vector3 planarForward = Vector3.ProjectOnPlane(
                targetPlayer.transform.forward,
                Vector3.up).normalized;
            if (planarForward.sqrMagnitude < 0.001f)
                planarForward = Vector3.forward;
            Vector3 planarRight = Vector3.Cross(
                Vector3.up,
                planarForward).normalized;
            Quaternion planarRotation =
                Quaternion.LookRotation(
                    planarForward,
                    Vector3.up);
            Vector3 planarRoot = CalculateHoverRootPosition(
                targetPlayer.transform.position,
                planarRight,
                Vector3.up,
                localBounds.min.y,
                lateralDistance,
                altitude);
            return new FlightDestination
            {
                direction = Vector3.up,
                rootPosition = planarRoot,
                rotation = planarRotation,
                hoverAltitude = altitude
            };
        }

        Vector3 center = world.GetPlanetCenterWorld();
        Vector3 direction = SafeDirection(
            targetPlayer.transform.position - center,
            Vector3.up);
        Vector3 forward = Vector3.ProjectOnPlane(
            targetPlayer.transform.forward,
            direction).normalized;
        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.Cross(direction, Vector3.right).normalized;
        Vector3 right = Vector3.Cross(direction, forward).normalized;
        Quaternion rotation = Quaternion.LookRotation(forward, direction);
        Vector3 root = CalculateHoverRootPosition(
            targetPlayer.transform.position,
            right,
            direction,
            localBounds.min.y,
            lateralDistance,
            altitude);
        return new FlightDestination
        {
            direction = SafeDirection(root - center, direction),
            rootPosition = root,
            rotation = rotation,
            hoverAltitude = altitude
        };
    }

    bool HasClearance(
        Vector3 rootPosition,
        Quaternion rotation,
        Transform playerTransform)
    {
        Vector3 halfExtents = localBounds.extents + new Vector3(2f, 0.25f, 2f);
        Vector3 center = rootPosition + rotation * localBounds.center;
        int count = Physics.OverlapBoxNonAlloc(
            center,
            halfExtents,
            clearanceHits,
            rotation,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider hit = clearanceHits[i];
            clearanceHits[i] = null;
            if (hit == null
                || hit.transform.IsChildOf(transform)
                || (playerTransform != null && hit.transform.IsChildOf(playerTransform)))
            {
                continue;
            }

            Vector3 closest = hit.ClosestPoint(center);
            Vector3 local = Quaternion.Inverse(rotation) * (closest - center);
            if (local.y > -halfExtents.y + 0.35f)
                return false;
        }
        return true;
    }

    IEnumerator AnimatePose(
        Vector3 fromPosition,
        Vector3 toPosition,
        Quaternion fromRotation,
        Quaternion toRotation,
        float duration,
        bool easeOut)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float ratio = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, duration));
            float eased = easeOut
                ? 1f - Mathf.Pow(1f - ratio, 3f)
                : ratio * ratio * (3f - 2f * ratio);
            SetShipPose(
                Vector3.Lerp(fromPosition, toPosition, eased),
                Quaternion.Slerp(fromRotation, toRotation, eased));
            yield return null;
        }
    }

    void RestoreHoverState(SurfaceSpacecraftState state)
    {
        if (surfaceRuntime != null
            && surfaceRuntime.Topology
                == PlanetSurfaceTopology.InfinitePlanar)
        {
            Vector3 surfaceProbe = surfaceRuntime.FromPersistentAddress(
                new PlanarSurfaceAddress(
                    state.planarX,
                    state.planarZ,
                    0f));
            surfaceRuntime.TryProjectToSurface(
                surfaceProbe,
                out PlanetSurfaceSample surface);
            Vector3 forward = Vector3.ProjectOnPlane(
                state.tangentForward,
                Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.forward;
            Quaternion planarRotation =
                Quaternion.LookRotation(forward, Vector3.up);
            Vector3 planarBottom =
                surface.point + Vector3.up * state.hoverAltitude;
            SetShipPose(
                planarBottom
                    - planarRotation
                    * new Vector3(0f, localBounds.min.y, 0f),
                planarRotation);
            Phase = SurfaceSpacecraftPhase.Hovering;
            SetFlightPhysics(false);
            return;
        }

        Vector3 center = world.GetPlanetCenterWorld();
        float surfaceRadius = world.GetProceduralSurfaceRadius(state.radialDirection);
        GalaxyTravelManager manager = GalaxyTravelManager.Instance;
        GalaxyPlanetDefinition planet =
            manager != null ? manager.CurrentPlanet : null;
        if (planet != null)
        {
            PlanetLandingResolution waterCheck =
                PlanetLandingSiteResolver.Resolve(
                    planet,
                    state.radialDirection,
                    0f);
            if (waterCheck.mode == PlanetLandingMode.OceanPlatform)
            {
                surfaceRadius = Mathf.Max(
                    surfaceRadius,
                    waterCheck.oceanRadius
                        + waterCheck.maximumVisualWaveHeight);
            }
        }
        Quaternion rotation = Quaternion.LookRotation(
            state.tangentForward,
            state.radialDirection);
        Vector3 bottom = center
            + state.radialDirection * (surfaceRadius + state.hoverAltitude);
        SetShipPose(
            bottom - rotation * new Vector3(0f, localBounds.min.y, 0f),
            rotation);
        Phase = SurfaceSpacecraftPhase.Hovering;
        SetFlightPhysics(false);
    }

    void SaveState(
        SurfaceSpacecraftParkingMode parkingMode,
        float hoverAltitude,
        bool flush)
    {
        savedState = CaptureState(parkingMode, hoverAltitude);
        GalaxyTravelManager manager = GalaxyTravelManager.Instance;
        GalaxyPlanetDefinition planet = manager != null ? manager.CurrentPlanet : null;
        manager?.UpdateSurfaceSpacecraftState(
            planet != null ? planet.planetId : string.Empty,
            savedState,
            flush);
    }

    SurfaceSpacecraftState CaptureState(
        SurfaceSpacecraftParkingMode parkingMode,
        float hoverAltitude)
    {
        Transform ship =
            assembly != null ? assembly.transform : transform;
        if (surfaceRuntime != null
            && surfaceRuntime.Topology
                == PlanetSurfaceTopology.InfinitePlanar)
        {
            PlanarSurfaceAddress address =
                surfaceRuntime.ToPersistentAddress(ship.position);
            Vector3 forward = Vector3.ProjectOnPlane(
                ship.forward,
                Vector3.up).normalized;
            return new SurfaceSpacecraftState
            {
                valid = true,
                surfaceTopology =
                    PlanetSurfaceTopology.InfinitePlanar,
                radialDirection = surfaceRuntime.AnchorDirection,
                tangentForward = forward.sqrMagnitude > 0.001f
                    ? forward
                    : Vector3.forward,
                parkingMode = parkingMode,
                hoverAltitude = hoverAltitude,
                planarX = address.x,
                planarZ = address.z
            };
        }

        Vector3 center = world != null
            ? world.GetPlanetCenterWorld()
            : Vector3.zero;
        Vector3 direction = SafeDirection(ship.position - center, ship.up);
        return new SurfaceSpacecraftState
        {
            valid = true,
            radialDirection = direction,
            tangentForward = Vector3.ProjectOnPlane(ship.forward, direction).normalized,
            parkingMode = parkingMode,
            hoverAltitude = hoverAltitude
        };
    }

    IEnumerator PlanarCallRoutine(
        VoxelPlanetPlayerController targetPlayer)
    {
        player = targetPlayer;
        Transform ship = assembly.transform;
        Vector3 start = ship.position;
        Quaternion startRotation = ship.rotation;
        bool hasLanding = TryFindLandingPose(
            targetPlayer,
            out FlightDestination destination);
        if (!hasLanding)
            destination = BuildHoverDestination(targetPlayer);

        StatusText = "飞船正在起飞";
        Phase = SurfaceSpacecraftPhase.TakingOff;
        SetFlightPhysics(true);
        Vector3 departurePoint = start + Vector3.up * 28f;
        yield return AnimatePose(
            start,
            departurePoint,
            startRotation,
            Quaternion.LookRotation(
                Vector3.ProjectOnPlane(
                    destination.rootPosition - start,
                    Vector3.up).normalized,
                Vector3.up),
            1f,
            true);

        Phase = SurfaceSpacecraftPhase.Travelling;
        StatusText = "飞船正在赶来";
        Vector3 arrivalPoint =
            destination.rootPosition + Vector3.up * 28f;
        float elapsed = 0f;
        const float travelDuration = 2.2f;
        while (elapsed < travelDuration)
        {
            elapsed += Time.deltaTime;
            float ratio = Smooth01(elapsed / travelDuration);
            Vector3 linear =
                Vector3.Lerp(departurePoint, arrivalPoint, ratio);
            linear.y += Mathf.Sin(ratio * Mathf.PI) * 18f;
            Vector3 direction =
                destination.rootPosition - ship.position;
            Vector3 forward = Vector3.ProjectOnPlane(
                direction,
                Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.001f)
                forward = ship.forward;
            SetShipPose(
                linear,
                Quaternion.LookRotation(forward, Vector3.up));
            yield return null;
        }

        Phase = SurfaceSpacecraftPhase.Approaching;
        StatusText = hasLanding
            ? "正在降落"
            : "附近无安全着陆点";
        yield return AnimatePose(
            arrivalPoint,
            destination.rootPosition,
            ship.rotation,
            destination.rotation,
            1.35f,
            false);
        SetShipPose(destination.rootPosition, destination.rotation);
        SetFlightPhysics(false);
        Phase = hasLanding
            ? SurfaceSpacecraftPhase.Landed
            : SurfaceSpacecraftPhase.Hovering;
        SaveState(
            hasLanding
                ? SurfaceSpacecraftParkingMode.Terrain
                : SurfaceSpacecraftParkingMode.Hovering,
            hasLanding ? 6f : destination.hoverAltitude,
            true);
        activeRoutine = null;
    }

    IEnumerator PlanarDepartureRoutine()
    {
        SetPlanarPilotPhysics(false);
        surfaceCameraRig?.Deactivate();
        surfaceCameraRig = null;
        Phase = SurfaceSpacecraftPhase.Boarding;
        StatusText = "正在登船";
        SurfaceMultifunctionController multifunction =
            player != null
                ? player.GetComponent<SurfaceMultifunctionController>()
                : null;
        multifunction?.SetInputBlocked(true);
        multifunction?.SetPilotMenuContext(false);
        if (player != null)
        {
            player.SetGameplayInputBlocked(true);
            player.SetSurfacePhysicsReady(false);
            player.enabled = false;
        }

        Camera camera = Camera.main;
        Transform cameraTransform =
            camera != null ? camera.transform : null;
        Transform ship = assembly.transform;
        if (cameraTransform != null)
        {
            cameraTransform.SetParent(ship, true);
            cameraTransform.localPosition =
                new Vector3(7.5f, 4.2f, -12f);
            cameraTransform.localRotation = Quaternion.LookRotation(
                new Vector3(-7.5f, -2.5f, 12f).normalized,
                Vector3.up);
        }

        Phase = SurfaceSpacecraftPhase.Departing;
        StatusText = "离开星球";
        SetFlightPhysics(true);
        Vector3 start = ship.position;
        Vector3 end = start + Vector3.up * 160f;
        Quaternion exitRotation =
            Quaternion.LookRotation(Vector3.up, ship.forward);
        float elapsed = 0f;
        const float duration = 3f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float ratio = Mathf.Clamp01(elapsed / duration);
            float eased = Smooth01(ratio);
            SetShipPose(
                Vector3.Lerp(start, end, eased),
                Quaternion.Slerp(
                    ship.rotation,
                    exitRotation,
                    eased));
            yield return null;
        }

        SaveState(
            savedState != null
                && savedState.parkingMode
                    == SurfaceSpacecraftParkingMode.Hovering
                    ? SurfaceSpacecraftParkingMode.Hovering
                    : SurfaceSpacecraftParkingMode.Terrain,
            savedState != null ? savedState.hoverAltitude : 6f,
            true);
        GalaxyTravelManager manager = GalaxyTravelManager.Instance;
        if (manager != null && manager.IsInterstellarGalaxy)
        {
            manager.OpenInterstellarFlightFromSurface(
                surfaceRuntime,
                ship.forward,
                Vector3.up * 120f);
        }
        activeRoutine = null;
    }

    void SetFlightPhysics(bool moving)
    {
        if (body != null)
        {
            if (!body.isKinematic)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.isKinematic = true;
            body.useGravity = false;
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;
        }
        if (shipColliders == null)
            return;
        for (int i = 0; i < shipColliders.Length; i++)
        {
            if (shipColliders[i] != null)
                shipColliders[i].enabled = !moving;
        }
    }

    void SetShipPose(Vector3 position, Quaternion rotation)
    {
        if (assembly != null)
        {
            Transform root = assembly.transform;
            root.SetPositionAndRotation(position, rotation);
            if (body != null && body.transform == root)
            {
                body.position = position;
                body.rotation = rotation;
            }
        }
        else if (body != null)
        {
            body.position = position;
            body.rotation = rotation;
        }
        else
        {
            transform.SetPositionAndRotation(position, rotation);
        }
    }

    Vector3 RootPositionOnSurface(Vector3 surfacePoint, Quaternion rotation)
    {
        return surfacePoint
            - rotation * new Vector3(0f, localBounds.min.y, 0f)
            + rotation * Vector3.up * 0.08f;
    }

    public static Vector3 CalculateHoverRootPosition(
        Vector3 playerPosition,
        Vector3 right,
        Vector3 up,
        float hullBottomLocalY,
        float lateralDistance = 10f,
        float altitude = 6f)
    {
        right = SafeDirection(right, Vector3.right);
        up = SafeDirection(up, Vector3.up);
        float bottomOffset = Mathf.Clamp(
            -hullBottomLocalY,
            0f,
            MaximumHoverBottomOffset);
        return playerPosition
            - right * Mathf.Max(0f, lateralDistance)
            + up * (Mathf.Max(0f, altitude) + bottomOffset);
    }

    static Bounds CalculateLocalBounds(Transform ship)
    {
        Renderer[] renderers = ship.GetComponentsInChildren<Renderer>(true);
        bool found = false;
        Bounds result = new Bounds(Vector3.zero, Vector3.one * 8f);
        foreach (Renderer renderer in renderers)
        {
            if (!IsShipGeometryRenderer(renderer)
                || !TryGetLocalGeometryBounds(renderer, out Bounds geometryBounds))
            {
                continue;
            }

            Vector3 min = geometryBounds.min;
            Vector3 max = geometryBounds.max;
            Bounds rendererInShipSpace = default;
            bool rendererFound = false;
            for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
            for (int z = 0; z < 2; z++)
            {
                Vector3 geometryCorner = new Vector3(
                    x == 0 ? min.x : max.x,
                    y == 0 ? min.y : max.y,
                    z == 0 ? min.z : max.z);
                Vector3 local = ship.InverseTransformPoint(
                    renderer.transform.TransformPoint(geometryCorner));
                if (!rendererFound)
                {
                    rendererInShipSpace = new Bounds(local, Vector3.zero);
                    rendererFound = true;
                }
                else
                {
                    rendererInShipSpace.Encapsulate(local);
                }
            }

            if (!rendererFound
                || rendererInShipSpace.center.magnitude
                    > MaximumPlausiblePartDistance
                || rendererInShipSpace.size.x
                    > MaximumPlausiblePartDistance * 2f
                || rendererInShipSpace.size.y
                    > MaximumPlausiblePartDistance * 2f
                || rendererInShipSpace.size.z
                    > MaximumPlausiblePartDistance * 2f)
            {
                continue;
            }

            Vector3 rendererMin = rendererInShipSpace.min;
            Vector3 rendererMax = rendererInShipSpace.max;
            for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
            for (int z = 0; z < 2; z++)
            {
                Vector3 local = new Vector3(
                    x == 0 ? rendererMin.x : rendererMax.x,
                    y == 0 ? rendererMin.y : rendererMax.y,
                    z == 0 ? rendererMin.z : rendererMax.z);
                if (!found)
                {
                    result = new Bounds(local, Vector3.zero);
                    found = true;
                }
                else
                {
                    result.Encapsulate(local);
                }
            }
        }
        return result;
    }

    static bool IsShipGeometryRenderer(Renderer renderer)
    {
        return renderer != null
            && renderer.enabled
            && renderer.gameObject.activeInHierarchy
            && (renderer is MeshRenderer || renderer is SkinnedMeshRenderer);
    }

    static bool TryGetLocalGeometryBounds(
        Renderer renderer,
        out Bounds bounds)
    {
        if (renderer is SkinnedMeshRenderer skinned)
        {
            bounds = skinned.localBounds;
            return skinned.sharedMesh != null;
        }

        if (renderer is MeshRenderer)
        {
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                bounds = filter.sharedMesh.bounds;
                return true;
            }
        }

        bounds = default;
        return false;
    }

    static Vector3 GreatCircleDirection(
        Vector3 from,
        Vector3 to,
        float ratio,
        Vector3 fallbackForward)
    {
        from.Normalize();
        to.Normalize();
        float angle = Vector3.Angle(from, to);
        if (angle < 0.01f)
            return to;
        Vector3 axis = Vector3.Cross(from, to);
        if (axis.sqrMagnitude < 0.0001f)
            axis = Vector3.Cross(from, fallbackForward);
        if (axis.sqrMagnitude < 0.0001f)
            axis = Vector3.Cross(from, Vector3.right);
        return Quaternion.AngleAxis(angle * ratio, axis.normalized) * from;
    }

    public static float CalculateRecallDuration(float angularDistance)
    {
        return Mathf.Lerp(4f, 8f, Mathf.Clamp01(angularDistance / 180f));
    }

    public static Vector3 SampleGreatCircleDirection(
        Vector3 from,
        Vector3 to,
        float ratio,
        Vector3 fallbackForward)
    {
        return GreatCircleDirection(
            from,
            to,
            Mathf.Clamp01(ratio),
            fallbackForward).normalized;
    }

    static Vector3 GreatCircleTangent(
        Vector3 current,
        Vector3 destination,
        Vector3 fallbackForward)
    {
        Vector3 axis = Vector3.Cross(current, destination);
        if (axis.sqrMagnitude < 0.0001f)
            return Vector3.ProjectOnPlane(fallbackForward, current).normalized;
        return Vector3.Cross(axis.normalized, current).normalized;
    }

    static Vector3 SafeDirection(Vector3 value, Vector3 fallback)
    {
        return value.sqrMagnitude > 0.001f
            ? value.normalized
            : fallback.normalized;
    }

    static float Smooth01(float value)
    {
        value = Mathf.Clamp01(value);
        return value * value * (3f - 2f * value);
    }

    void OnDestroy()
    {
        surfaceCameraRig?.Deactivate();
        if (Current == this)
            Current = null;
    }

    struct FlightDestination
    {
        public Vector3 direction;
        public Vector3 rootPosition;
        public Quaternion rotation;
        public float hoverAltitude;
    }
}
