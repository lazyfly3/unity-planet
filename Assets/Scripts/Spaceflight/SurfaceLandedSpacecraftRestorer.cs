using System.Collections;
using SpacecraftEditor;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class SurfaceLandedSpacecraftRestorer : MonoBehaviour
{
    const string WorkshopResourcePath = "Spacecraft/SpacecraftWorkshopRoot";

    VoxelQuadSphereWorld world;
    PendingPlanetLandingContext landing;

    public void Initialize(VoxelQuadSphereWorld targetWorld, PendingPlanetLandingContext context)
    {
        world = targetWorld;
        landing = context;
        StartCoroutine(RestoreWhenReady());
    }

    IEnumerator RestoreWhenReady()
    {
        while (world != null && !world.IsGenerationComplete)
            yield return null;
        if (world == null || landing == null)
            yield break;

        Vector3 direction = landing.landingDirection.sqrMagnitude > 0.001f
            ? landing.landingDirection.normalized
            : Vector3.up;
        if (!world.TryFindPlanetSurface(direction, out RaycastHit hit))
        {
            Vector3 center = world.GetPlanetCenterWorld();
            hit.point = center + direction * world.GetProceduralSurfaceRadius(direction);
            hit.normal = direction;
        }

        GameObject prefab = Resources.Load<GameObject>(WorkshopResourcePath);
        if (prefab == null)
        {
            Debug.LogError($"SurfaceLandedSpacecraftRestorer: missing Resources/{WorkshopResourcePath}.", this);
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
            yield break;

        Vector3 up = hit.normal.sqrMagnitude > 0.001f ? hit.normal.normalized : direction;
        Vector3 forward = Vector3.ProjectOnPlane(landing.shipRotation * Vector3.forward, up).normalized;
        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.Cross(up, Mathf.Abs(Vector3.Dot(up, Vector3.right)) < 0.9f ? Vector3.right : Vector3.forward).normalized;
        assembly.transform.SetPositionAndRotation(hit.point + up * 2f, Quaternion.LookRotation(forward, up));

        Rigidbody body = assembly.ShipBody;
        if (body != null)
        {
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
            body.useGravity = false;
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;
        }

        yield return null;
        PlaceBottomOnGround(assembly.transform, hit.point, up);
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

    static void PlaceBottomOnGround(Transform ship, Vector3 groundPoint, Vector3 up)
    {
        Renderer[] renderers = ship.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return;
        float lowest = float.PositiveInfinity;
        foreach (Renderer renderer in renderers)
        {
            Bounds bounds = renderer.bounds;
            Vector3 extents = bounds.extents;
            float projectedExtent = Mathf.Abs(up.x) * extents.x
                + Mathf.Abs(up.y) * extents.y
                + Mathf.Abs(up.z) * extents.z;
            lowest = Mathf.Min(lowest, Vector3.Dot(bounds.center - groundPoint, up) - projectedExtent);
        }
        if (float.IsFinite(lowest))
            ship.position += up * (0.08f - lowest);
    }
}
