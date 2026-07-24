using System.Collections;
using SpacecraftEditor;
using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public sealed class SurfaceLandedSpacecraftRestorer : MonoBehaviour
{
    const string WorkshopResourcePath = "Spacecraft/SpacecraftWorkshopRoot";

    VoxelQuadSphereWorld world;
    PendingPlanetLandingContext landing;
    Coroutine restoreRoutine;
    ProceduralOceanLandingPlatform generatedPlatform;

    public bool IsPlatformReady { get; private set; }
    public bool IsSpacecraftReady { get; private set; }
    public bool IsPlayerReady { get; private set; }
    public bool IsRestoreComplete { get; private set; }
    public bool HasFailed { get; private set; }
    public string FailureReason { get; private set; }
    public PlanetLandingMode EffectiveLandingMode { get; private set; } =
        PlanetLandingMode.Auto;

    public void Initialize(VoxelQuadSphereWorld targetWorld, PendingPlanetLandingContext context)
    {
        world = targetWorld;
        landing = context;
        IsPlatformReady = false;
        IsSpacecraftReady = false;
        IsPlayerReady = false;
        IsRestoreComplete = false;
        HasFailed = false;
        FailureReason = string.Empty;
        EffectiveLandingMode = PlanetLandingMode.Auto;
        generatedPlatform = null;
        if (restoreRoutine != null)
            StopCoroutine(restoreRoutine);
        restoreRoutine = StartCoroutine(RestoreWhenReady());
    }

    IEnumerator RestoreWhenReady()
    {
        if (world == null || landing == null)
        {
            Fail("缺少星球世界或着陆上下文");
            yield break;
        }

        GalaxyTravelManager travelManager = GalaxyTravelManager.Instance;
        GalaxyPlanetDefinition currentPlanet =
            travelManager != null ? travelManager.CurrentPlanet : null;
        SurfaceSpacecraftState restoredState =
            currentPlanet != null
                ? travelManager.GetSurfaceSpacecraftState(currentPlanet.planetId)
                : null;
        bool restoringExistingParking =
            landing.restoreSavedSpacecraftState;
        Vector3 direction = restoringExistingParking
            && restoredState != null
            && restoredState.valid
            ? restoredState.radialDirection
            : landing.landingDirection.sqrMagnitude > 0.001f
            ? landing.landingDirection.normalized
            : Vector3.up;
        PlanetLandingMode requestedMode = landing.landingMode;
        if (restoringExistingParking
            && requestedMode == PlanetLandingMode.Auto
            && restoredState != null
            && restoredState.valid)
        {
            requestedMode =
                restoredState.parkingMode
                    == SurfaceSpacecraftParkingMode.OceanPlatform
                ? PlanetLandingMode.OceanPlatform
                : restoredState.parkingMode
                    == SurfaceSpacecraftParkingMode.Terrain
                    ? PlanetLandingMode.Terrain
                    : PlanetLandingMode.Auto;
        }

        VoxelPlanetPlayerController surfacePlayer = FindObjectOfType<VoxelPlanetPlayerController>();
        if (surfacePlayer != null)
        {
            surfacePlayer.CaptureFirstPersonCameraState();
            surfacePlayer.SetSurfacePhysicsReady(false);
        }

        world.SetSurfaceSpawnDirection(direction);
        while (world != null && !world.HasStartedSurfaceGeneration)
            yield return null;
        while (world != null && !world.HasFinishedSurfaceGeneration)
            yield return null;
        if (world == null)
        {
            Fail("地形生成期间星球世界被销毁");
            yield break;
        }

        if (!world.IsInitialSurfaceReady)
        {
            world.RequestInitialSurfaceRegion(direction);
            while (world != null && world.IsInitialSurfaceRecoveryRunning)
                yield return null;
        }
        if (world == null)
        {
            Fail("着陆区恢复期间星球世界被销毁");
            yield break;
        }

        if (!world.IsInitialSurfaceReady
            || !world.TryFindInitialSurfacePose(out VoxelTerrainSurfacePose surface))
        {
            Fail(
                "着陆区缺少高精度地形碰撞体",
                $"LoadedChunks={world.PinnedLandingChunkCount}, " +
                $"GenerationFailed={world.InitialSurfaceGenerationFailed}");
            yield break;
        }

        GameObject prefab = Resources.Load<GameObject>(WorkshopResourcePath);
        if (prefab == null)
        {
            Fail($"缺少 Resources/{WorkshopResourcePath} 飞船预制体");
            yield break;
        }

        GameObject instance = Instantiate(prefab);
        instance.name = "LandedSpacecraft";
        SpacecraftApp app = instance.GetComponentInChildren<SpacecraftApp>(true);
        ShipAssembly assembly = instance.GetComponentInChildren<ShipAssembly>(true);
        GalaxySpacecraftBlueprintStore store = instance.GetComponentInChildren<GalaxySpacecraftBlueprintStore>(true);
        if (app != null && store != null && store.TryLoad(out SpacecraftBlueprintData blueprint))
            app.RestoreBlueprint(blueprint);

        DisableWorkshopSystems(instance);
        if (assembly == null)
        {
            Fail("恢复后的飞船缺少 ShipAssembly");
            Destroy(instance);
            yield break;
        }

        // Blueprint restoration creates renderers and colliders immediately, but
        // their world bounds are not guaranteed to be current until the next frame.
        yield return null;
        Physics.SyncTransforms();

        Bounds actualShipBounds = CalculateShipBounds(assembly.transform);
        GalaxyPlanetDefinition planet = travelManager != null
            ? travelManager.CurrentPlanet
            : null;
        PlanetLandingResolution resolved =
            PlanetLandingSiteResolver.ResolveLoadedSurface(
                world,
                planet,
                direction,
                actualShipBounds,
                requestedMode);
        EffectiveLandingMode = resolved.mode;
        bool oceanPlatformLanding =
            EffectiveLandingMode == PlanetLandingMode.OceanPlatform;
        Vector3 up = oceanPlatformLanding
            ? direction
            : surface.Normal.sqrMagnitude > 0.001f
                ? surface.Normal.normalized
                : direction;
        Vector3 localForward = restoringExistingParking
            && restoredState != null
            && restoredState.valid
            ? restoredState.tangentForward
            : landing.shipRotation * Vector3.forward;
        Vector3 forward = Vector3.ProjectOnPlane(
            world.transform.TransformDirection(localForward),
            up).normalized;
        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.Cross(up, Mathf.Abs(Vector3.Dot(up, Vector3.right)) < 0.9f ? Vector3.right : Vector3.forward).normalized;
        Vector3 groundPoint = surface.Point;
        ProceduralOceanLandingPlatform landingPlatform = null;
        if (oceanPlatformLanding)
        {
            landingPlatform = ProceduralOceanLandingPlatform.Build(
                actualShipBounds,
                new OceanLandingPose(
                    world.GetPlanetCenterWorld(),
                    up,
                    forward,
                    resolved.oceanRadius,
                    resolved.maximumVisualWaveHeight));
            generatedPlatform = landingPlatform;
            if (landingPlatform == null)
            {
                Fail("海上着陆平台生成失败");
                Destroy(instance);
                yield break;
            }
            groundPoint = landingPlatform.DeckSurfacePoint;
        }
        Physics.SyncTransforms();
        IsPlatformReady = !oceanPlatformLanding || landingPlatform != null;
        if (!IsPlatformReady)
        {
            Fail("海上着陆平台生成失败");
            Destroy(instance);
            yield break;
        }

        Rigidbody body = assembly.ShipBody;
        Vector3 parkedPosition = groundPoint + up * 2f;
        Quaternion parkedRotation = Quaternion.LookRotation(forward, up);
        if (body != null)
        {
            if (!body.isKinematic)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.None;
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;
        }
        SetShipRootPose(assembly, parkedPosition, parkedRotation);

        yield return new WaitForEndOfFrame();
        Physics.SyncTransforms();

        // A modular blueprint can update its final renderer bounds one frame
        // after the first restore pass. Recheck the now-placed ship before
        // grounding so a late footprint expansion is corrected behind the
        // loading screen instead of exposing a ship standing in water.
        if (landingPlatform == null)
        {
            Bounds finalShipBounds = CalculateShipBounds(assembly.transform);
            PlanetLandingResolution finalResolution =
                PlanetLandingSiteResolver.ResolveLoadedSurface(
                    world,
                    planet,
                    direction,
                    finalShipBounds,
                    PlanetLandingMode.Terrain);
            if (finalResolution.mode == PlanetLandingMode.OceanPlatform)
            {
                EffectiveLandingMode = PlanetLandingMode.OceanPlatform;
                oceanPlatformLanding = true;
                up = direction;
                forward = Vector3.ProjectOnPlane(forward, up).normalized;
                if (forward.sqrMagnitude < 0.001f)
                {
                    forward = Vector3.Cross(
                        up,
                        Mathf.Abs(Vector3.Dot(up, Vector3.right)) < 0.9f
                            ? Vector3.right
                            : Vector3.forward).normalized;
                }

                landingPlatform = ProceduralOceanLandingPlatform.Build(
                    finalShipBounds,
                    new OceanLandingPose(
                        world.GetPlanetCenterWorld(),
                        up,
                        forward,
                        finalResolution.oceanRadius,
                        finalResolution.maximumVisualWaveHeight));
                generatedPlatform = landingPlatform;
                if (landingPlatform == null)
                {
                    IsPlatformReady = false;
                    Fail("海上着陆平台二次修正失败");
                    Destroy(instance);
                    yield break;
                }

                groundPoint = landingPlatform.DeckSurfacePoint;
                IsPlatformReady = true;
                parkedPosition = groundPoint + up * 2f;
                parkedRotation = Quaternion.LookRotation(forward, up);
                SetShipRootPose(assembly, parkedPosition, parkedRotation);
                Physics.SyncTransforms();
                yield return new WaitForEndOfFrame();
                Physics.SyncTransforms();
            }
        }

        const int maximumGroundingPasses = 4;
        bool groundingSucceeded = false;
        float bottomGap = float.PositiveInfinity;
        for (int pass = 0; pass < maximumGroundingPasses; pass++)
        {
            if (!TryGetBottomGap(
                assembly.transform,
                groundPoint,
                up,
                out bottomGap))
            {
                break;
            }

            float error = 0.08f - bottomGap;
            if (Mathf.Abs(error) <= 0.15f)
            {
                groundingSucceeded = true;
                break;
            }

            TranslateShipRoot(assembly, up * error);
            Physics.SyncTransforms();
            yield return new WaitForEndOfFrame();
        }

        Physics.SyncTransforms();
        groundingSucceeded = TryGetBottomGap(
            assembly.transform,
            groundPoint,
            up,
            out bottomGap)
            && Mathf.Abs(bottomGap - 0.08f) <= 0.15f;
        if (!groundingSucceeded)
        {
            if (!float.IsFinite(bottomGap))
            {
                Fail("飞船甲板高度校验失败", "无法取得有效的船体最低点");
                Destroy(instance);
                yield break;
            }

            // Height disagreement is recoverable. Renderer/physics updates can
            // arrive one frame apart for restored modular ships, so apply the
            // measured residual directly and do not strand the player behind
            // the loading screen for a small visual placement error.
            float residualCorrection = 0.08f - bottomGap;
            TranslateShipRoot(assembly, up * residualCorrection);
            Physics.SyncTransforms();
            yield return new WaitForEndOfFrame();
            Physics.SyncTransforms();

            if (!TryGetBottomGap(
                assembly.transform,
                groundPoint,
                up,
                out float correctedGap))
            {
                Fail("飞船甲板高度校验失败", "补偿后无法读取船体最低点");
                Destroy(instance);
                yield break;
            }

            if (Mathf.Abs(correctedGap - 0.08f) > 0.15f)
            {
                Debug.LogWarning(
                    "SurfaceLandedSpacecraftRestorer: ship grounding retained a " +
                    $"residual visual gap of {correctedGap:0.000}m after correction. " +
                    "The landing is still safe, so surface entry will continue.",
                    this);
            }
        }
        IsSpacecraftReady = true;

        if (world == null)
        {
            Fail("玩家安置前星球世界被销毁");
            Destroy(instance);
            yield break;
        }

        bool playerPlaced = landingPlatform != null
            ? PlacePlayerOnPlatform(landingPlatform, up, forward)
            : PlacePlayerBesideShip(assembly, groundPoint, up, forward);
        if (!playerPlaced)
        {
            Fail("玩家出生点无效或与平台、飞船发生重叠");
            Destroy(instance);
            yield break;
        }
        IsPlayerReady = true;

        VoxelPlanetPlayerController activePlayer =
            FindObjectOfType<VoxelPlanetPlayerController>();
        SurfaceMultifunctionController multifunction = null;
        if (activePlayer != null)
        {
            multifunction =
                activePlayer.GetComponent<SurfaceMultifunctionController>()
                ?? activePlayer.gameObject.AddComponent<SurfaceMultifunctionController>();
            multifunction.SetInputBlocked(true);
        }
        SurfaceSpacecraftController surfaceController =
            instance.GetComponent<SurfaceSpacecraftController>()
            ?? instance.AddComponent<SurfaceSpacecraftController>();
        bool preserveSavedHover =
            restoringExistingParking
            && restoredState != null
            && restoredState.valid
            && restoredState.parkingMode
                == SurfaceSpacecraftParkingMode.Hovering;
        SurfaceSpacecraftState effectiveState = preserveSavedHover
            ? restoredState
            : new SurfaceSpacecraftState
            {
                valid = true,
                radialDirection = direction,
                tangentForward = forward,
                parkingMode = oceanPlatformLanding
                    ? SurfaceSpacecraftParkingMode.OceanPlatform
                    : SurfaceSpacecraftParkingMode.Terrain,
                hoverAltitude = 6f
            };
        effectiveState.ClampValues();
        surfaceController.Initialize(
            world,
            activePlayer,
            assembly,
            effectiveState);
        if (!preserveSavedHover && travelManager != null)
        {
            travelManager.UpdateSurfaceSpacecraftState(
                currentPlanet != null
                    ? currentPlanet.planetId
                    : landing.planetId,
                effectiveState,
                true);
        }
        IsRestoreComplete = true;
        restoreRoutine = null;
    }

    static void DisableWorkshopSystems(GameObject instance)
    {
        foreach (Canvas canvas in instance.GetComponentsInChildren<Canvas>(true))
            canvas.gameObject.SetActive(false);
        foreach (Camera camera in instance.GetComponentsInChildren<Camera>(true))
            camera.enabled = false;
        foreach (AudioListener listener in instance.GetComponentsInChildren<AudioListener>(true))
            listener.enabled = false;
        foreach (Light light in instance.GetComponentsInChildren<Light>(true))
            light.enabled = false;
        DisableAll<EventSystem>(instance);

        DisableAll<SpacecraftApp>(instance);
        DisableAll<BuildModeController>(instance);
        DisableAll<ShipFlightController>(instance);
        DisableAll<OrbitCameraController>(instance);
        DisableAll<EditorUIController>(instance);
        DisableAll<ForceVisualizer>(instance);
        DisableAll<SpacecraftPersistenceCoordinator>(instance);
        DisableAll<SpacecraftIfcsMotor>(instance);
        DisableAll<KeyboardMouseFlightInput>(instance);
        DisableAll<InterstellarShipController>(instance);
        DisableAll<InterstellarFlightRuntime>(instance);
    }

    static void DisableAll<T>(GameObject root) where T : Behaviour
    {
        foreach (T behaviour in root.GetComponentsInChildren<T>(true))
            behaviour.enabled = false;
    }

    static void SetShipRootPose(
        ShipAssembly assembly,
        Vector3 position,
        Quaternion rotation)
    {
        Transform root = assembly.transform;
        root.SetPositionAndRotation(position, rotation);

        Rigidbody body = assembly.ShipBody;
        if (body != null && body.transform == root)
        {
            body.position = position;
            body.rotation = rotation;
        }
    }

    static void TranslateShipRoot(ShipAssembly assembly, Vector3 delta)
    {
        Transform root = assembly.transform;
        Vector3 position = root.position + delta;
        root.position = position;

        Rigidbody body = assembly.ShipBody;
        if (body != null && body.transform == root)
            body.position = position;
    }

    static bool TryGetBottomGap(
        Transform ship,
        Vector3 groundPoint,
        Vector3 up,
        out float lowest)
    {
        lowest = float.PositiveInfinity;
        Renderer[] renderers = ship.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            if (!IsShipGeometryRenderer(renderer))
                continue;

            if (!TryGetLocalGeometryBounds(renderer, out Bounds localBounds))
                continue;

            Vector3 center = localBounds.center;
            Vector3 extents = localBounds.extents;
            Transform geometryTransform = renderer.transform;
            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 localCorner = center + Vector3.Scale(
                            extents,
                            new Vector3(x, y, z));
                        Vector3 worldCorner =
                            geometryTransform.TransformPoint(localCorner);
                        lowest = Mathf.Min(
                            lowest,
                            Vector3.Dot(worldCorner - groundPoint, up));
                    }
                }
            }
        }

        return float.IsFinite(lowest);
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

    bool PlacePlayerBesideShip(
        ShipAssembly assembly,
        Vector3 shipGroundPoint,
        Vector3 up,
        Vector3 forward)
    {
        VoxelPlanetPlayerController player = FindObjectOfType<VoxelPlanetPlayerController>();
        if (player == null)
            return false;

        Vector3 side = Vector3.Cross(up, forward).normalized;
        if (side.sqrMagnitude < 0.001f)
            side = Vector3.Cross(up, Vector3.right).normalized;

        float sideClearance = CalculateShipSideClearance(assembly.transform, side) + 2f;
        Vector3 center = world.GetPlanetCenterWorld();
        Vector3[] offsets =
        {
            side * sideClearance,
            -side * sideClearance,
            side * Mathf.Max(3f, sideClearance * 0.6f),
            -side * Mathf.Max(3f, sideClearance * 0.6f),
            -forward * Mathf.Max(3f, sideClearance * 0.6f)
        };

        VoxelTerrainSurfacePose fallbackSurface = default;
        Vector3 fallbackDirection = up;
        bool foundPlayerGround = false;
        for (int i = 0; i < offsets.Length; i++)
        {
            Vector3 playerDirection =
                (shipGroundPoint + offsets[i] - center).normalized;
            if (!world.TryFindLoadedSurfacePoseNearDirection(
                playerDirection,
                out VoxelTerrainSurfacePose playerSurface))
            {
                continue;
            }

            foundPlayerGround = true;
            fallbackSurface = playerSurface;
            fallbackDirection = playerDirection;
            Vector3 playerGroundPoint = playerSurface.Point;
            Vector3 playerUp = playerSurface.Normal.sqrMagnitude > 0.001f
                ? playerSurface.Normal.normalized
                : playerDirection;
            Vector3 playerForward =
                Vector3.ProjectOnPlane(forward, playerUp).normalized;
            if (playerForward.sqrMagnitude < 0.001f)
                playerForward = Vector3.Cross(playerUp, side).normalized;

            player.TeleportTo(
                playerGroundPoint + playerUp * 1.2f,
                Quaternion.LookRotation(playerForward, playerUp));
            player.SetSurfacePhysicsReady(false);
            player.EnterFirstPersonSurfaceMode();
            Physics.SyncTransforms();
            if (IsPlayerCapsuleClear(
                player,
                playerUp,
                playerSurface.Collider))
            {
                return true;
            }
        }

        if (!foundPlayerGround)
        {
            Debug.LogError(
                "SurfaceLandedSpacecraftRestorer: no loaded terrain collider was found beside the ship.",
                this);
            return false;
        }

        // A building or restored prop can temporarily overlap every normal
        // candidate. Start above the best loaded ground and let the player
        // settle after the loading screen, instead of treating this as a
        // fatal surface-entry failure.
        Vector3 fallbackUp = fallbackSurface.Normal.sqrMagnitude > 0.001f
            ? fallbackSurface.Normal.normalized
            : fallbackDirection;
        Vector3 fallbackForward =
            Vector3.ProjectOnPlane(forward, fallbackUp).normalized;
        if (fallbackForward.sqrMagnitude < 0.001f)
            fallbackForward = Vector3.Cross(fallbackUp, side).normalized;
        player.TeleportTo(
            fallbackSurface.Point + fallbackUp * 3f,
            Quaternion.LookRotation(fallbackForward, fallbackUp));
        player.SetSurfacePhysicsReady(false);
        player.EnterFirstPersonSurfaceMode();
        Physics.SyncTransforms();
        Debug.LogWarning(
            "SurfaceLandedSpacecraftRestorer: no obstruction-free ground spawn " +
            "was found beside the ship. The player was placed above the best " +
            "loaded ground and surface entry will continue.",
            this);
        return true;
    }

    static float CalculateShipSideClearance(Transform ship, Vector3 side)
    {
        Renderer[] renderers = ship.GetComponentsInChildren<Renderer>(true);
        float extent = 0f;
        foreach (Renderer renderer in renderers)
        {
            if (!IsShipGeometryRenderer(renderer))
                continue;

            Bounds bounds = renderer.bounds;
            Vector3 offset = bounds.center - ship.position;
            Vector3 projected = new Vector3(
                Mathf.Abs(side.x) * bounds.extents.x,
                Mathf.Abs(side.y) * bounds.extents.y,
                Mathf.Abs(side.z) * bounds.extents.z);
            extent = Mathf.Max(extent, Mathf.Abs(Vector3.Dot(offset, side)) + projected.x + projected.y + projected.z);
        }

        return Mathf.Max(3f, extent);
    }

    static Bounds CalculateShipBounds(Transform ship)
    {
        Renderer[] renderers = ship.GetComponentsInChildren<Renderer>(true);
        bool found = false;
        Bounds bounds = new Bounds(ship.position, Vector3.one * 8f);
        foreach (Renderer renderer in renderers)
        {
            if (!IsShipGeometryRenderer(renderer))
                continue;
            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }
        return bounds;
    }

    static bool IsShipGeometryRenderer(Renderer renderer)
    {
        return renderer != null
            && renderer.gameObject.activeInHierarchy
            && renderer.enabled
            && (renderer is MeshRenderer || renderer is SkinnedMeshRenderer);
    }

    static bool PlacePlayerOnPlatform(
        ProceduralOceanLandingPlatform platform,
        Vector3 up,
        Vector3 forward)
    {
        VoxelPlanetPlayerController player =
            FindObjectOfType<VoxelPlanetPlayerController>();
        if (player == null || platform == null)
            return false;

        Vector3 side = Vector3.Cross(up, forward).normalized;
        if (side.sqrMagnitude < 0.001f)
            side = Vector3.Cross(up, Vector3.right).normalized;
        Vector3[] offsets =
        {
            Vector3.zero,
            side * 1.5f,
            -side * 1.5f,
            side * 3f,
            -side * 3f,
            forward * 2f,
            -forward * 2f,
            side * 1.5f + forward * 2f,
            -side * 1.5f - forward * 2f,
            up * 0.5f
        };

        for (int i = 0; i < offsets.Length; i++)
        {
            player.TeleportTo(
                platform.PlayerSpawnPoint + offsets[i],
                Quaternion.LookRotation(forward, up));
            player.SetSurfacePhysicsReady(false);
            player.EnterFirstPersonSurfaceMode();
            Physics.SyncTransforms();
            if (IsPlayerCapsuleClear(
                player,
                up,
                platform.DeckCollider))
                return true;
        }

        player.TeleportTo(
            platform.PlayerSpawnPoint + up * 2.5f,
            Quaternion.LookRotation(forward, up));
        player.SetSurfacePhysicsReady(false);
        player.EnterFirstPersonSurfaceMode();
        Physics.SyncTransforms();
        Debug.LogWarning(
            "SurfaceLandedSpacecraftRestorer: every normal platform spawn was " +
            "obstructed. The player was placed above the side walkway and " +
            "surface entry will continue.",
            platform);
        return true;
    }

    static bool IsPlayerCapsuleClear(
        VoxelPlanetPlayerController player,
        Vector3 up,
        Collider allowedSupport)
    {
        CapsuleCollider capsule = player.GetComponent<CapsuleCollider>();
        if (capsule == null || !capsule.enabled)
            return false;

        Vector3 scale = player.transform.lossyScale;
        float radius = capsule.radius * Mathf.Max(
            Mathf.Abs(scale.x),
            Mathf.Abs(scale.z));
        float height = Mathf.Max(
            radius * 2f,
            capsule.height * Mathf.Abs(scale.y));
        Vector3 center = player.transform.TransformPoint(capsule.center);
        float halfSegment = Mathf.Max(0f, height * 0.5f - radius);
        Vector3 pointA = center + up * halfSegment;
        Vector3 pointB = center - up * halfSegment;
        Collider[] overlaps = Physics.OverlapCapsule(
            pointA,
            pointB,
            Mathf.Max(0.01f, radius - 0.02f),
            ~0,
            QueryTriggerInteraction.Ignore);
        foreach (Collider overlap in overlaps)
        {
            if (overlap == null
                || overlap == capsule
                || overlap == allowedSupport
                || overlap.transform.IsChildOf(player.transform))
            {
                continue;
            }
            return false;
        }

        return true;
    }

    void Fail(string reason, string diagnostics = null)
    {
        HasFailed = true;
        FailureReason = string.IsNullOrWhiteSpace(diagnostics)
            ? reason
            : $"{reason}: {diagnostics}";
        if (generatedPlatform != null)
        {
            Destroy(generatedPlatform.gameObject);
            generatedPlatform = null;
        }
        restoreRoutine = null;
        Debug.LogError(
            $"SurfaceLandedSpacecraftRestorer: {FailureReason}. " +
            "The player remains locked behind the loading screen.",
            this);
    }
}
