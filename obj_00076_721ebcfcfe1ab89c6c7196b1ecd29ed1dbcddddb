using UnityEngine;

public struct PlanetSurfaceSample
{
    public Vector3 point;
    public Vector3 normal;
    public float height;
    public bool isWater;
}

public readonly struct PlanarSurfaceAddress
{
    public readonly double x;
    public readonly double z;
    public readonly float y;

    public PlanarSurfaceAddress(double x, double z, float y)
    {
        this.x = x;
        this.z = z;
        this.y = y;
    }
}

public interface IPlanetSurfaceRuntime
{
    PlanetSurfaceTopology Topology { get; }
    Vector3 AnchorDirection { get; }
    bool IsCenterCollisionReady { get; }
    bool IsFullyReady { get; }
    Vector3 GetUp(Vector3 worldPosition);
    Vector3 GetGravity(Vector3 worldPosition);
    bool TryProjectToSurface(Vector3 worldPosition, out PlanetSurfaceSample sample);
    bool TryFindLandingPoint(
        Vector3 nearWorldPosition,
        float footprintRadius,
        float maximumSlopeDegrees,
        out PlanetSurfaceSample sample);
    PlanarSurfaceAddress ToPersistentAddress(Vector3 worldPosition);
    Vector3 FromPersistentAddress(PlanarSurfaceAddress address);
}

public interface IPlanetHarvestPersistenceSink
{
    void MarkHarvested(string stableId);
}

public static class PlanetSurfaceRuntimeRegistry
{
    public static IPlanetSurfaceRuntime Current { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Reset()
    {
        Current = null;
    }

    public static void Register(IPlanetSurfaceRuntime runtime)
    {
        Current = runtime;
    }

    public static void Unregister(IPlanetSurfaceRuntime runtime)
    {
        if (ReferenceEquals(Current, runtime))
            Current = null;
    }
}

[DisallowMultipleComponent]
public sealed class LegacySphereSurfaceRuntime :
    MonoBehaviour,
    IPlanetSurfaceRuntime
{
    VoxelQuadSphereWorld world;

    public PlanetSurfaceTopology Topology =>
        PlanetSurfaceTopology.LegacySphere;
    public Vector3 AnchorDirection => Vector3.up;
    public bool IsCenterCollisionReady =>
        world != null && world.IsGenerationComplete;
    public bool IsFullyReady => IsCenterCollisionReady;

    public void Configure(VoxelQuadSphereWorld value)
    {
        world = value;
        PlanetSurfaceRuntimeRegistry.Register(this);
    }

    public Vector3 GetUp(Vector3 worldPosition)
    {
        return world != null
            ? PlanetGravity.GetUp(worldPosition, world.GetPlanetCenterWorld())
            : Vector3.up;
    }

    public Vector3 GetGravity(Vector3 worldPosition)
    {
        return world != null
            ? PlanetGravity.GetGravitationalAcceleration(
                worldPosition,
                world.GetPlanetCenterWorld(),
                world.GravitationalParameter)
            : Vector3.down * 9.8f;
    }

    public bool TryProjectToSurface(
        Vector3 worldPosition,
        out PlanetSurfaceSample sample)
    {
        sample = default;
        if (world == null)
            return false;
        Vector3 center = world.GetPlanetCenterWorld();
        Vector3 direction = worldPosition - center;
        direction = direction.sqrMagnitude > 0.0001f
            ? direction.normalized
            : Vector3.up;
        float radius = world.GetProceduralSurfaceRadius(direction);
        sample.point = center + direction * radius;
        sample.normal = direction;
        sample.height = radius;
        return true;
    }

    public bool TryFindLandingPoint(
        Vector3 nearWorldPosition,
        float footprintRadius,
        float maximumSlopeDegrees,
        out PlanetSurfaceSample sample)
    {
        return TryProjectToSurface(nearWorldPosition, out sample);
    }

    public PlanarSurfaceAddress ToPersistentAddress(Vector3 worldPosition)
    {
        Vector3 local = world != null
            ? worldPosition - world.GetPlanetCenterWorld()
            : worldPosition;
        return new PlanarSurfaceAddress(local.x, local.z, local.y);
    }

    public Vector3 FromPersistentAddress(PlanarSurfaceAddress address)
    {
        Vector3 center = world != null
            ? world.GetPlanetCenterWorld()
            : Vector3.zero;
        return center + new Vector3(
            (float)address.x,
            address.y,
            (float)address.z);
    }

    void OnDestroy()
    {
        PlanetSurfaceRuntimeRegistry.Unregister(this);
    }
}

public sealed class PlanetFloatingOriginParticipant : MonoBehaviour
{
}
