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
    Departing
}

[DisallowMultipleComponent]
public sealed class SurfaceSpacecraftController : MonoBehaviour
{
    const float BoardingDistance = 12f;
    const float MaximumPlausiblePartDistance = 250f;
    const float MaximumHoverBottomOffset = 60f;

    readonly Collider[] clearanceHits = new Collider[64];

    VoxelQuadSphereWorld world;
    VoxelPlanetPlayerController player;
    ShipAssembly assembly;
    Rigidbody body;
    Collider[] shipColliders;
    Bounds localBounds;
    Coroutine activeRoutine;
    SurfaceSpacecraftState savedState;

    public static SurfaceSpacecraftController Current { get; private set; }
    public SurfaceSpacecraftPhase Phase { get; private set; } =
        SurfaceSpacecraftPhase.Parked;
    public string StatusText { get; private set; } = string.Empty;
    public bool IsMoving => Phase == SurfaceSpacecraftPhase.TakingOff
        || Phase == SurfaceSpacecraftPhase.Travelling
        || Phase == SurfaceSpacecraftPhase.Approaching;
    public bool IsDeparting => Phase == SurfaceSpacecraftPhase.Boarding
        || Phase == SurfaceSpacecraftPhase.Departing;
    public bool CanBeCalled => !IsMoving && !IsDeparting;
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
        if (!CanBeCalled || targetPlayer == null || world == null || assembly == null)
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
        GalaxyTravelManager manager = GalaxyTravelManager.Instance;
        if (IsMoving
            || IsDeparting
            || world == null
            || assembly == null
            || manager == null
            || !manager.IsInterstellarGalaxy)
            return false;
        if (activeRoutine != null)
            StopCoroutine(activeRoutine);
        activeRoutine = StartCoroutine(DepartureRoutine());
        return true;
    }

    IEnumerator CallRoutine(VoxelPlanetPlayerController targetPlayer)
    {
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
        Vector3 center = world != null
            ? world.GetPlanetCenterWorld()
            : Vector3.zero;
        Transform ship = assembly != null ? assembly.transform : transform;
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
