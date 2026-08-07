using System;
using System.Collections;
using System.Collections.Generic;
using ModularAssembly;
using SpacecraftEditor;
using UnityEngine;
using UnityPlanet.ModularAssembly;
using UnityPlanet.SpaceStation;

[DisallowMultipleComponent]
public sealed class PlanarSurfaceLandedSpacecraftRestorer : MonoBehaviour
{
    InfinitePlanarSurfaceWorld world;
    PendingPlanetLandingContext landing;
    PlanarSurfaceModularVehicleLoader loader;
    Coroutine routine;

    public bool IsPlatformReady { get; private set; }
    public bool IsSpacecraftReady { get; private set; }
    public bool IsPlayerReady { get; private set; }
    public bool IsRestoreComplete { get; private set; }
    public bool HasFailed { get; private set; }
    public string FailureReason { get; private set; }
    public bool IsVehicleOnlyMode => true;

    public void Initialize(
        InfinitePlanarSurfaceWorld targetWorld,
        PendingPlanetLandingContext context)
    {
        world = targetWorld;
        landing = context;
        IsPlatformReady = false;
        IsSpacecraftReady = false;
        IsPlayerReady = false;
        IsRestoreComplete = false;
        HasFailed = false;
        FailureReason = string.Empty;
        if (routine != null)
            StopCoroutine(routine);
        routine = StartCoroutine(RestoreWhenReady());
    }

    public void SetGameplayReady(bool ready)
    {
        loader?.SetGameplayReady(ready);
    }

    IEnumerator RestoreWhenReady()
    {
        while (world != null && !world.IsCenterCollisionReady)
            yield return null;
        while (world != null && !world.IsInitialPlayerPlaced)
            yield return null;
        if (world == null)
        {
            Fail("Infinite planar world was destroyed during entry.");
            yield break;
        }

        loader = gameObject.GetComponent<PlanarSurfaceModularVehicleLoader>()
                 ?? gameObject.AddComponent<
                     PlanarSurfaceModularVehicleLoader>();
        loader.Prepare(world, landing);
        bool finished = false;
        bool succeeded = false;
        string result = string.Empty;
        yield return loader.Build((success, message) =>
        {
            succeeded = success;
            result = message;
            finished = true;
        });
        while (!finished)
            yield return null;
        if (!succeeded)
        {
            Fail(string.IsNullOrWhiteSpace(result)
                ? "The saved Modular spacecraft could not be restored."
                : result);
            yield break;
        }

        IsPlatformReady = loader.IsSpawnReady;
        IsSpacecraftReady = loader.IsBuilt;
        // The entry coordinator still exposes its historical readiness fields.
        // In vehicle-only mode this means the walking player has been safely
        // retired after its camera was transferred to the Modular ship.
        IsPlayerReady = loader.IsWalkingPlayerDisabled;
        IsRestoreComplete = IsPlatformReady
                            && IsSpacecraftReady
                            && IsPlayerReady;
        if (!IsRestoreComplete)
        {
            Fail("Modular surface entry did not reach a complete ready state.");
            yield break;
        }
        routine = null;
    }

    void Fail(string reason)
    {
        loader?.Abort();
        HasFailed = true;
        FailureReason = reason;
        Debug.LogError(
            $"PlanarSurfaceLandedSpacecraftRestorer: {reason}",
            this);
        routine = null;
    }
}

public sealed class PlanarSurfaceGridFlightSession :
    MonoBehaviour,
    IGridFlightSession
{
    public GridFlightState State { get; private set; } =
        GridFlightState.LoadingTerrain;
    public bool IsFlying => State == GridFlightState.Flight;
    public event Action<GridFlightState, string> StateChanged;

    public void BeginSession()
    {
        if (State == GridFlightState.Flight)
            return;
        State = GridFlightState.Flight;
        StateChanged?.Invoke(State, "Modular spacecraft surface flight ready.");
    }

    public void ExitFlight()
    {
        if (State == GridFlightState.Build)
            return;
        State = GridFlightState.Build;
        StateChanged?.Invoke(State, "Modular spacecraft surface flight ended.");
    }
}

[DisallowMultipleComponent]
public sealed class PlanarSurfacePilotAimSource :
    MonoBehaviour,
    IRobocraftPilotAimSource
{
    GridLabCameraController cameraController;

    public bool GameplayReady { get; set; }

    public void Configure(GridLabCameraController value)
    {
        cameraController = value;
        GameplayReady = false;
    }

    public bool TryGetPilotAim(out Vector3 worldForward)
    {
        worldForward = cameraController != null
            ? cameraController.FlightAimForward
            : transform.forward;
        return GameplayReady
               && worldForward.sqrMagnitude > 0.0001f;
    }
}

[DisallowMultipleComponent]
public sealed class PlanarSurfaceModularFlightController : MonoBehaviour
{
    InfinitePlanarSurfaceWorld world;
    Rigidbody body;
    RobocraftMotionCoordinator motion;
    PlanarSurfaceGridFlightSession session;
    PlanarSurfacePilotAimSource aimSource;
    WeaponSystemCoordinator weapons;
    GridLabCameraController cameraController;
    bool gameplayReady;
    bool transitionInProgress;

    public bool GameplayReady => gameplayReady;

    public void Initialize(
        InfinitePlanarSurfaceWorld targetWorld,
        Rigidbody targetBody,
        RobocraftMotionCoordinator motionController,
        PlanarSurfaceGridFlightSession flightSession,
        PlanarSurfacePilotAimSource pilotAim,
        WeaponSystemCoordinator weaponController,
        GridLabCameraController cameraRig)
    {
        world = targetWorld;
        body = targetBody;
        motion = motionController;
        session = flightSession;
        aimSource = pilotAim;
        weapons = weaponController;
        cameraController = cameraRig;
        SetGameplayReady(false);
    }

    public void SetGameplayReady(bool ready)
    {
        gameplayReady = ready && !transitionInProgress;
        if (motion != null)
            motion.ControlsEnabled = gameplayReady;
        if (aimSource != null)
            aimSource.GameplayReady = gameplayReady;
        if (weapons != null)
            weapons.ControlsEnabled = gameplayReady;

        if (!gameplayReady)
            return;

        if (body != null)
        {
            body.isKinematic = false;
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.WakeUp();
        }
        if (cameraController != null)
        {
            cameraController.enabled = true;
            cameraController.SetFlightMode(true);
        }
        session?.BeginSession();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        if (!gameplayReady || transitionInProgress)
            return;
        if (Input.GetKeyDown(KeyCode.L))
            LeavePlanet();
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus || !gameplayReady || transitionInProgress)
            return;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void LeavePlanet()
    {
        GalaxyTravelManager manager = GalaxyTravelManager.Instance;
        if (manager == null || !manager.IsInterstellarGalaxy || world == null)
            return;

        transitionInProgress = true;
        Vector3 departureVelocity = body != null
            ? body.velocity
            : Vector3.zero;
        Vector3 forward = Vector3.ProjectOnPlane(
            transform.forward,
            Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.forward;
        if (!world.IsFiniteCombatArea)
            SaveSurfaceState(manager, forward, true);
        SetGameplayReady(false);
        session?.ExitFlight();
        manager.OpenInterstellarFlightFromSurface(
            world,
            forward,
            departureVelocity);
    }

    void SaveSurfaceState(
        GalaxyTravelManager manager,
        Vector3 forward,
        bool flush)
    {
        GalaxyPlanetDefinition planet = manager.CurrentPlanet;
        if (planet == null)
            return;
        PlanarSurfaceAddress address =
            world.ToPersistentAddress(transform.position);
        var state = new SurfaceSpacecraftState
        {
            valid = true,
            surfaceTopology = PlanetSurfaceTopology.InfinitePlanar,
            radialDirection = world.AnchorDirection,
            tangentForward = forward,
            parkingMode = SurfaceSpacecraftParkingMode.Hovering,
            hoverAltitude = 6f,
            planarX = address.x,
            planarZ = address.z
        };
        state.ClampValues();
        manager.UpdateSurfaceSpacecraftState(
            planet.planetId,
            state,
            flush);
    }
}

[DisallowMultipleComponent]
public sealed class PlanarSurfaceModularVehicleLoader : MonoBehaviour
{
    const string DefinitionResourcePath =
        "ModularAssembly/Definitions";

    InfinitePlanarSurfaceWorld world;
    PendingPlanetLandingContext landing;
    GameObject modularRoot;
    ModularContentService contentService;
    RobocraftMotionCoordinator motion;
    PlanarSurfaceGridFlightSession session;
    PlanarSurfacePilotAimSource aimSource;
    WeaponSystemCoordinator weapons;
    GridLabCameraController cameraController;
    PlanarSurfaceModularFlightController flightController;
    FinitePlanetHordeCombatController hordeCombat;
    VoxelPlanetPlayerController walkingPlayer;

    public Rigidbody Body { get; private set; }
    public bool IsSpawnReady { get; private set; }
    public bool IsBuilt { get; private set; }
    public bool IsWalkingPlayerDisabled { get; private set; }

    public void Prepare(
        InfinitePlanarSurfaceWorld targetWorld,
        PendingPlanetLandingContext context)
    {
        world = targetWorld;
        landing = context;
        walkingPlayer = world != null ? world.Player : null;
        IsSpawnReady = false;
        IsBuilt = false;
        IsWalkingPlayerDisabled = false;

        modularRoot = new GameObject("ModularSurfaceShip");
        modularRoot.SetActive(false);
        modularRoot.AddComponent<PlanetFloatingOriginParticipant>();
        Body = modularRoot.AddComponent<Rigidbody>();
        Body.useGravity = false;
        Body.drag = 0f;
        Body.angularDrag = 0f;
        Body.isKinematic = true;
        Body.collisionDetectionMode =
            CollisionDetectionMode.ContinuousSpeculative;
        Body.interpolation = RigidbodyInterpolation.Interpolate;
        modularRoot.AddComponent<SpacecraftDamageReceiver>();
        aimSource = modularRoot.AddComponent<
            PlanarSurfacePilotAimSource>();
        flightController = modularRoot.AddComponent<
            PlanarSurfaceModularFlightController>();
    }

    public IEnumerator Build(Action<bool, string> completed)
    {
        if (world == null || modularRoot == null || Body == null)
        {
            completed?.Invoke(false, "Modular surface runtime was not prepared.");
            yield break;
        }

        contentService = GetComponent<ModularContentService>()
                         ?? gameObject.AddComponent<ModularContentService>();
        yield return contentService.Initialize();
        if (contentService.Catalog == null
            || contentService.Catalog.Count == 0)
        {
            completed?.Invoke(
                false,
                "Modular content catalog is unavailable: "
                + (contentService.LastError ?? "catalog is empty."));
            yield break;
        }

        GridModuleDefinition[] baseDefinitions =
            Resources.LoadAll<GridModuleDefinition>(
                DefinitionResourcePath);
        if (baseDefinitions == null || baseDefinitions.Length == 0)
        {
            completed?.Invoke(
                false,
                "Runtime Modular definitions are missing.");
            yield break;
        }

        var model = new GridAssemblyModel(baseDefinitions);
        IReadOnlyDictionary<string, ModularContentRecord> records =
            NeoXCatalogIntegration.RegisterDefinitions(
                model,
                contentService.Catalog);
        var store = new ModularBlueprintStore();
        ModularBlueprintData blueprint;
        string loadError = string.Empty;
        bool hasExpeditionBlueprint =
            SpaceStationFlowContext.TryGetActiveExpeditionBlueprint(
                out blueprint);
        if (!hasExpeditionBlueprint &&
            !store.TryLoad(out blueprint, out loadError))
        {
            completed?.Invoke(
                false,
                string.IsNullOrWhiteSpace(loadError)
                    ? "No saved Modular spacecraft is available for the planet surface."
                    : loadError);
            yield break;
        }
        if (!model.RestoreBlueprint(blueprint, out string restoreError))
        {
            completed?.Invoke(
                false,
                "Saved Modular spacecraft restore failed: " + restoreError);
            yield break;
        }

        Transform parts = new GameObject("Parts").transform;
        parts.SetParent(modularRoot.transform, false);
        Transform core = new GameObject("CoreVisual").transform;
        core.SetParent(modularRoot.transform, false);
        ShipAssembly assembly = modularRoot.AddComponent<ShipAssembly>();
        assembly.Configure(Body, parts, null, 1000f);
        GridAssemblyPresenter presenter =
            modularRoot.AddComponent<GridAssemblyPresenter>();
        presenter.Initialize(model, assembly, core);

        PlanetEnvironmentProvider environment =
            modularRoot.AddComponent<PlanetEnvironmentProvider>();
        PlanetCelestialProfile celestial =
            world.Definition != null && world.Definition.celestial != null
                ? world.Definition.celestial
                : PlanetCelestialProfile.CreateCompatibleDefault();
        environment.Configure(world, celestial.Physical);

        motion = modularRoot.AddComponent<RobocraftMotionCoordinator>();
        motion.ConfigureExplicit(Body, assembly, model, presenter);
        motion.SetEnvironmentProvider(environment);
        motion.ControlsEnabled = false;

        session = modularRoot.AddComponent<
            PlanarSurfaceGridFlightSession>();
        NeoXCatalogIntegration integration =
            modularRoot.AddComponent<NeoXCatalogIntegration>();
        modularRoot.SetActive(true);
        integration.InitializeRuntime(contentService, presenter, records);
        while (!integration.IsReady || contentService.IsLoadingAssets)
            yield return null;

        if (!TryResolveSpawn(out string spawnError))
        {
            completed?.Invoke(false, spawnError);
            yield break;
        }

        Camera sceneCamera = Camera.main;
        if (sceneCamera == null)
        {
            completed?.Invoke(
                false,
                "Planet surface has no gameplay camera for Modular flight.");
            yield break;
        }
        cameraController = GetComponent<GridLabCameraController>()
                           ?? gameObject.AddComponent<
                               GridLabCameraController>();
        cameraController.Initialize(sceneCamera, modularRoot.transform);
        cameraController.SetFlightMode(true);
        cameraController.enabled = false;
        aimSource.Configure(cameraController);

        weapons = modularRoot.AddComponent<WeaponSystemCoordinator>();
        weapons.Initialize(
            presenter,
            session,
            cameraController,
            model,
            null,
            sceneCamera);
        weapons.SetHudVisible(false);
        weapons.ControlsEnabled = false;
        VehicleStructureGraph graph = weapons.StructureGraph;
        graph.SetAutomaticReturnToBuild(false);
        graph.SetDamageEnabled(true);

        SpacecraftDamageReceiver aggregate =
            modularRoot.GetComponent<SpacecraftDamageReceiver>();
        ModularInterstellarDamageBridge damageBridge =
            modularRoot.AddComponent<ModularInterstellarDamageBridge>();
        damageBridge.Initialize(model, graph, aggregate, Body);

        if (!motion.TryBeginFlight(false, out string physicsMessage))
        {
            completed?.Invoke(
                false,
                "RC3 planet physics ownership check failed: "
                + physicsMessage);
            yield break;
        }

        TransferCameraAndDisableWalkingPlayer(sceneCamera);
        world.SetMovementTarget(modularRoot.transform);
        flightController.Initialize(
            world,
            Body,
            motion,
            session,
            aimSource,
            weapons,
            cameraController);
        if (world.IsFiniteCombatArea)
        {
            GameObject combatRoot = new GameObject(
                "FinitePlanetHordeCombat");
            combatRoot.transform.SetParent(world.transform, false);
            hordeCombat = combatRoot.AddComponent<
                FinitePlanetHordeCombatController>();
            yield return hordeCombat.Prepare(
                world,
                flightController,
                Body,
                weapons,
                graph,
                model,
                contentService,
                records);
            if (!hordeCombat.IsPrepared)
            {
                completed?.Invoke(
                    false,
                    string.IsNullOrWhiteSpace(hordeCombat.PreparationError)
                        ? "Finite planet enemy combat preparation failed."
                        : hordeCombat.PreparationError);
                yield break;
            }
        }
        IsBuilt = true;
        completed?.Invoke(true, physicsMessage);
    }

    public void SetGameplayReady(bool ready)
    {
        if (!IsBuilt)
            return;
        flightController?.SetGameplayReady(ready);
        hordeCombat?.SetGameplayReady(ready);
    }

    public void Abort()
    {
        hordeCombat?.SetGameplayReady(false);
        if (hordeCombat != null)
            Destroy(hordeCombat.gameObject);
        if (modularRoot != null)
            Destroy(modularRoot);
        if (cameraController != null)
            cameraController.enabled = false;
    }

    bool TryResolveSpawn(out string failureReason)
    {
        Bounds bounds = CalculateLocalBounds(modularRoot.transform);
        GalaxyTravelManager manager = GalaxyTravelManager.Instance;
        GalaxyPlanetDefinition planet = manager != null
            ? manager.CurrentPlanet
            : world.Definition;
        SurfaceSpacecraftState restored =
            manager != null && planet != null
                ? manager.GetSurfaceSpacecraftState(planet.planetId)
                : null;
        bool useRestored = !world.IsFiniteCombatArea
                           && restored != null
                           && restored.valid
                           && restored.surfaceTopology
                           == PlanetSurfaceTopology.InfinitePlanar;

        Vector3 near = walkingPlayer != null
            ? walkingPlayer.transform.position
            : Vector3.zero;
        FinitePlanetDefenseLayoutPlan defence =
            world.IsFiniteCombatArea
                ? world.FiniteCombatTerrainPlan?.DefenseLayout
                : null;
        if (defence != null && defence.IsValid)
        {
            near = world.FromPersistentAddress(
                new PlanarSurfaceAddress(
                    defence.playerSpawn.x,
                    defence.playerSpawn.z,
                    0f));
        }
        else if (useRestored)
        {
            near = world.FromPersistentAddress(
                new PlanarSurfaceAddress(
                    restored.planarX,
                    restored.planarZ,
                    0f));
        }

        float footprint =
            Mathf.Max(bounds.extents.x, bounds.extents.z) + 2f;
        FinitePlanetUrbanCombatRuntime urbanCombat =
            world.GetComponent<FinitePlanetUrbanCombatRuntime>();
        PlanetSurfaceSample surface;
        bool terrainSpawn;
        if (urbanCombat != null && urbanCombat.IsReady)
        {
            Vector3 urbanGround = urbanCombat.ProjectToGround(near);
            surface = new PlanetSurfaceSample
            {
                point = urbanGround,
                normal = Vector3.up,
                height = urbanCombat.GroundHeight,
                isWater = false
            };
            terrainSpawn = true;
        }
        else
        {
            terrainSpawn = world.TryFindLandingPoint(
                near,
                footprint,
                18f,
                out surface);
        }
        Vector3 groundPoint;
        if (terrainSpawn)
        {
            groundPoint = surface.point;
        }
        else if (world.OceanEnabled)
        {
            PlanarSurfaceAddress address =
                world.ToPersistentAddress(near);
            groundPoint = world.FromPersistentAddress(
                new PlanarSurfaceAddress(
                    address.x,
                    address.z,
                    world.SeaHeight));
        }
        else
        {
            failureReason =
                "No safe terrain spawn was found for the Modular spacecraft.";
            return false;
        }

        Vector3 forward = useRestored
            ? Vector3.ProjectOnPlane(
                restored.tangentForward,
                Vector3.up).normalized
            : landing != null
                ? Vector3.ProjectOnPlane(
                    landing.shipRotation * Vector3.forward,
                    Vector3.up).normalized
                : Vector3.forward;
        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.forward;
        Quaternion rotation =
            Quaternion.LookRotation(forward, Vector3.up);
        Vector3 position = groundPoint
                           + Vector3.up
                           * Mathf.Max(2.5f, -bounds.min.y + 2.5f);
        modularRoot.transform.SetPositionAndRotation(position, rotation);
        Body.position = position;
        Body.rotation = rotation;
        Physics.SyncTransforms();
        IsSpawnReady = true;
        failureReason = string.Empty;
        return true;
    }

    void TransferCameraAndDisableWalkingPlayer(Camera sceneCamera)
    {
        Transform cameraTransform = sceneCamera.transform;
        cameraTransform.SetParent(null, true);
        sceneCamera.enabled = true;
        AudioListener listener =
            sceneCamera.GetComponent<AudioListener>();
        if (listener != null)
            listener.enabled = true;

        if (walkingPlayer == null)
        {
            IsWalkingPlayerDisabled = true;
            return;
        }
        walkingPlayer.SetGameplayInputBlocked(true);
        walkingPlayer.SetSurfacePhysicsReady(false);
        walkingPlayer.SetExternalCameraControl(true);
        walkingPlayer.enabled = false;
        Collider playerCollider =
            walkingPlayer.GetComponent<Collider>();
        if (playerCollider != null)
            playerCollider.enabled = false;
        Rigidbody playerBody = walkingPlayer.GetComponent<Rigidbody>();
        if (playerBody != null)
            playerBody.isKinematic = true;
        walkingPlayer.gameObject.SetActive(false);
        IsWalkingPlayerDisabled = true;
    }

    static Bounds CalculateLocalBounds(Transform root)
    {
        Bounds worldBounds = new Bounds(root.position, Vector3.one * 2f);
        bool found = false;
        foreach (Collider collider
                 in root.GetComponentsInChildren<Collider>(true))
        {
            if (collider == null || !collider.enabled)
                continue;
            if (!found)
            {
                worldBounds = collider.bounds;
                found = true;
            }
            else
            {
                worldBounds.Encapsulate(collider.bounds);
            }
        }
        if (!found)
        {
            foreach (Renderer renderer
                     in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || !renderer.enabled)
                    continue;
                if (!found)
                {
                    worldBounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    worldBounds.Encapsulate(renderer.bounds);
                }
            }
        }
        Vector3 localCenter =
            root.InverseTransformPoint(worldBounds.center);
        Vector3 localSize =
            root.InverseTransformVector(worldBounds.size);
        localSize = new Vector3(
            Mathf.Abs(localSize.x),
            Mathf.Abs(localSize.y),
            Mathf.Abs(localSize.z));
        return new Bounds(localCenter, localSize);
    }
}
