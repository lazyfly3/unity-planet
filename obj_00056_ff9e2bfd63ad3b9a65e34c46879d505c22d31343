using System.Collections;
using SpacecraftEditor;
using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public sealed class PlanarSurfaceLandedSpacecraftRestorer :
    MonoBehaviour
{
    const string WorkshopResourcePath =
        "Spacecraft/SpacecraftWorkshopRoot";

    InfinitePlanarSurfaceWorld world;
    PendingPlanetLandingContext landing;
    Coroutine routine;

    public bool IsPlatformReady { get; private set; }
    public bool IsSpacecraftReady { get; private set; }
    public bool IsPlayerReady { get; private set; }
    public bool IsRestoreComplete { get; private set; }
    public bool HasFailed { get; private set; }
    public string FailureReason { get; private set; }

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

        GameObject prefab =
            Resources.Load<GameObject>(WorkshopResourcePath);
        if (prefab == null)
        {
            Fail($"Missing Resources/{WorkshopResourcePath}.");
            yield break;
        }

        EventSystem[] sceneEventSystems =
            FindObjectsOfType<EventSystem>(true);
        var eventSystemEnabledStates =
            new bool[sceneEventSystems.Length];
        for (int index = 0; index < sceneEventSystems.Length; index++)
        {
            eventSystemEnabledStates[index] =
                sceneEventSystems[index].enabled;
            sceneEventSystems[index].enabled = false;
        }
        GameObject instance = Instantiate(prefab);
        instance.name = "LandedSpacecraft";
        instance.AddComponent<PlanetFloatingOriginParticipant>();
        DisableAll<EventSystem>(instance);
        for (int index = 0; index < sceneEventSystems.Length; index++)
        {
            if (sceneEventSystems[index] != null)
            {
                sceneEventSystems[index].enabled =
                    eventSystemEnabledStates[index];
            }
        }
        SpacecraftApp app =
            instance.GetComponentInChildren<SpacecraftApp>(true);
        ShipAssembly assembly =
            instance.GetComponentInChildren<ShipAssembly>(true);
        GalaxySpacecraftBlueprintStore store =
            instance.GetComponentInChildren<GalaxySpacecraftBlueprintStore>(
                true);
        if (app != null
            && store != null
            && store.TryLoad(out SpacecraftBlueprintData blueprint))
        {
            app.RestoreBlueprint(blueprint);
        }
        DisableWorkshopSystems(instance);
        if (assembly == null)
        {
            Destroy(instance);
            Fail("Restored spacecraft has no ShipAssembly.");
            yield break;
        }

        yield return null;
        Physics.SyncTransforms();
        Bounds bounds = CalculateShipBounds(assembly.transform);
        GalaxyTravelManager manager = GalaxyTravelManager.Instance;
        GalaxyPlanetDefinition planet =
            manager != null ? manager.CurrentPlanet : null;
        SurfaceSpacecraftState restored =
            planet != null
                ? manager.GetSurfaceSpacecraftState(planet.planetId)
                : null;
        bool useRestored =
            restored != null
            && restored.valid
            && restored.surfaceTopology
                == PlanetSurfaceTopology.InfinitePlanar;

        Vector3 near = world.Player != null
            ? world.Player.transform.position
                + Vector3.ProjectOnPlane(
                    world.Player.transform.forward,
                    Vector3.up).normalized * 18f
            : Vector3.zero;
        if (useRestored)
        {
            near = world.FromPersistentAddress(
                new PlanarSurfaceAddress(
                    restored.planarX,
                    restored.planarZ,
                    0f));
        }

        float footprint =
            Mathf.Max(bounds.extents.x, bounds.extents.z) + 2f;
        bool terrainLanding = world.TryFindLandingPoint(
            near,
            footprint,
            12f,
            out PlanetSurfaceSample surface);
        Vector3 groundPoint;
        SurfaceSpacecraftParkingMode parkingMode;
        if (terrainLanding)
        {
            groundPoint = surface.point;
            parkingMode = SurfaceSpacecraftParkingMode.Terrain;
            IsPlatformReady = true;
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
            CreateOceanPlatform(
                groundPoint,
                bounds.size.x + 8f,
                bounds.size.z + 8f);
            parkingMode =
                SurfaceSpacecraftParkingMode.OceanPlatform;
            IsPlatformReady = true;
        }
        else
        {
            Destroy(instance);
            Fail("No safe planar terrain landing site was found.");
            yield break;
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
        Vector3 rootPosition =
            groundPoint
            + Vector3.up
            * Mathf.Max(0.08f, -bounds.min.y + 0.08f);
        SetShipRootPose(assembly, rootPosition, rotation);
        Rigidbody body = assembly.ShipBody;
        if (body != null)
        {
            if (!body.isKinematic)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.isKinematic = true;
            body.useGravity = false;
        }
        Physics.SyncTransforms();
        IsSpacecraftReady = true;

        VoxelPlanetPlayerController player = world.Player;
        if (player != null
            && landing != null
            && world.TryFindLandingPoint(
                groundPoint
                    + Vector3.Cross(Vector3.up, forward)
                    * (footprint + 2f),
                1f,
                45f,
                out PlanetSurfaceSample playerSurface))
        {
            player.TeleportTo(
                playerSurface.point + Vector3.up * 1.2f,
                Quaternion.LookRotation(forward, Vector3.up));
        }
        IsPlayerReady = player != null;
        if (!IsPlayerReady)
        {
            Destroy(instance);
            Fail("Planar surface player was not found.");
            yield break;
        }

        SurfaceMultifunctionController multifunction =
            player.GetComponent<SurfaceMultifunctionController>()
            ?? player.gameObject
                .AddComponent<SurfaceMultifunctionController>();
        multifunction.SetInputBlocked(true);
        SurfaceSpacecraftController controller =
            instance.GetComponent<SurfaceSpacecraftController>()
            ?? instance.AddComponent<SurfaceSpacecraftController>();
        PlanarSurfaceAddress parkedAddress =
            world.ToPersistentAddress(assembly.transform.position);
        SurfaceSpacecraftState state =
            useRestored
            && restored.parkingMode
                == SurfaceSpacecraftParkingMode.Hovering
                ? restored
                : new SurfaceSpacecraftState
                {
                    valid = true,
                    surfaceTopology =
                        PlanetSurfaceTopology.InfinitePlanar,
                    radialDirection = world.AnchorDirection,
                    tangentForward = forward,
                    parkingMode = parkingMode,
                    hoverAltitude = 6f,
                    planarX = parkedAddress.x,
                    planarZ = parkedAddress.z
                };
        state.ClampValues();
        controller.Initialize(world, player, assembly, state);
        if (manager != null && planet != null)
        {
            manager.UpdateSurfaceSpacecraftState(
                planet.planetId,
                state,
                true);
        }
        IsRestoreComplete = true;
        routine = null;
    }

    void CreateOceanPlatform(
        Vector3 surfacePoint,
        float width,
        float depth)
    {
        GameObject platform =
            GameObject.CreatePrimitive(PrimitiveType.Cube);
        platform.name = "PlanarOceanLandingPlatform";
        platform.transform.SetParent(transform, true);
        platform.transform.position =
            surfacePoint - Vector3.up * 0.25f;
        platform.transform.localScale =
            new Vector3(
                Mathf.Max(8f, width),
                0.5f,
                Mathf.Max(8f, depth));
        platform.AddComponent<PlanetFloatingOriginParticipant>();
        Renderer renderer = platform.GetComponent<Renderer>();
        if (renderer != null)
        {
            Material material =
                new Material(Shader.Find("Standard"));
            material.color =
                new Color(0.18f, 0.22f, 0.25f, 1f);
            material.SetFloat("_Metallic", 0.7f);
            material.SetFloat("_Glossiness", 0.75f);
            renderer.material = material;
        }
    }

    static void DisableWorkshopSystems(GameObject instance)
    {
        foreach (Canvas canvas
                 in instance.GetComponentsInChildren<Canvas>(true))
        {
            canvas.gameObject.SetActive(false);
        }
        foreach (Camera camera
                 in instance.GetComponentsInChildren<Camera>(true))
        {
            camera.enabled = false;
        }
        foreach (AudioListener listener
                 in instance.GetComponentsInChildren<AudioListener>(true))
        {
            listener.enabled = false;
        }
        foreach (Light light
                 in instance.GetComponentsInChildren<Light>(true))
        {
            light.enabled = false;
        }
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

    static void DisableAll<T>(GameObject root)
        where T : Behaviour
    {
        foreach (T behaviour
                 in root.GetComponentsInChildren<T>(true))
        {
            behaviour.enabled = false;
        }
    }

    static void SetShipRootPose(
        ShipAssembly assembly,
        Vector3 position,
        Quaternion rotation)
    {
        assembly.transform.SetPositionAndRotation(position, rotation);
        Rigidbody body = assembly.ShipBody;
        if (body != null && body.transform == assembly.transform)
        {
            body.position = position;
            body.rotation = rotation;
        }
    }

    static Bounds CalculateShipBounds(Transform ship)
    {
        Renderer[] renderers =
            ship.GetComponentsInChildren<Renderer>(true);
        Bounds bounds = new Bounds(ship.position, Vector3.one * 2f);
        bool found = false;
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || !renderer.enabled)
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
        Vector3 localCenter =
            ship.InverseTransformPoint(bounds.center);
        Vector3 localSize = ship.InverseTransformVector(bounds.size);
        localSize = new Vector3(
            Mathf.Abs(localSize.x),
            Mathf.Abs(localSize.y),
            Mathf.Abs(localSize.z));
        return new Bounds(localCenter, localSize);
    }

    void Fail(string reason)
    {
        HasFailed = true;
        FailureReason = reason;
        Debug.LogError(
            $"PlanarSurfaceLandedSpacecraftRestorer: {reason}",
            this);
        routine = null;
    }
}
