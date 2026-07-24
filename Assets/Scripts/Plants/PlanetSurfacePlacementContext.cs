using System;
using UnityEngine;

public interface IPlanetSurfacePlacementContext
{
    int Seed { get; }
    float PlanetRadius { get; }
    Vector3 PlanetCenter { get; }
    Transform PlayerSpawn { get; }
    int TerrainConfigurationHash { get; }
    bool IsStreamingLargePlanet { get; }
    bool TryFindPlanetSurface(Vector3 direction, out RaycastHit hit);
    bool IsTerrainCollider(Collider value);
    Vector3 GetStreamingSurfaceDirection(System.Random random);
}

public sealed class VoxelPlanetSurfacePlacementContext : IPlanetSurfacePlacementContext
{
    readonly VoxelQuadSphereWorld world;

    public VoxelPlanetSurfacePlacementContext(VoxelQuadSphereWorld value)
    {
        world = value != null ? value : throw new ArgumentNullException(nameof(value));
    }

    public int Seed => world.Seed;
    public float PlanetRadius => world.PlanetRadius;
    public Vector3 PlanetCenter => world.GetPlanetCenterWorld();
    public Transform PlayerSpawn => world.PlayerSpawn;
    public int TerrainConfigurationHash => world.TerrainConfigurationHash;
    public bool IsStreamingLargePlanet => world.IsStreamingLargePlanet;
    public bool TryFindPlanetSurface(Vector3 direction, out RaycastHit hit)
        => world.TryFindPlanetSurface(direction, out hit);
    public bool IsTerrainCollider(Collider value) => world.IsTerrainCollider(value);
    public Vector3 GetStreamingSurfaceDirection(System.Random random)
        => world.GetStreamingSurfaceDirection(random);
}

[DisallowMultipleComponent]
public sealed class AnalyticSphereSurfacePlacementContext : MonoBehaviour, IPlanetSurfacePlacementContext
{
    [SerializeField] int seed = 41277;
    [SerializeField, Min(1f)] float radius = 22f;
    [SerializeField] Transform sphereCenter;
    [SerializeField] Transform playerSpawn;
    [SerializeField] SphereCollider surfaceCollider;
    [SerializeField] LayerMask surfaceMask = ~0;

    public int Seed => seed;
    public float PlanetRadius => radius;
    public Vector3 PlanetCenter => sphereCenter != null ? sphereCenter.position : transform.position;
    public Transform PlayerSpawn => playerSpawn;
    public int TerrainConfigurationHash
    {
        get
        {
            unchecked
            {
                int hash = seed;
                hash = hash * 31 + radius.GetHashCode();
                hash = hash * 31 + PlanetCenter.GetHashCode();
                return hash;
            }
        }
    }
    public bool IsStreamingLargePlanet => false;

    public void Configure(int valueSeed, float valueRadius, SphereCollider valueCollider)
    {
        seed = valueSeed;
        radius = Mathf.Max(1f, valueRadius);
        surfaceCollider = valueCollider;
        if (valueCollider != null)
            sphereCenter = valueCollider.transform;
    }

    public bool TryFindPlanetSurface(Vector3 direction, out RaycastHit hit)
    {
        direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.up;
        Vector3 origin = PlanetCenter + direction * (radius + 5f);
        float distance = radius * 2f + 10f;
        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            -direction,
            distance,
            surfaceMask,
            QueryTriggerInteraction.Ignore);
        float closest = float.PositiveInfinity;
        hit = default;
        bool found = false;
        for (int i = 0; i < hits.Length; i++)
        {
            if (!IsTerrainCollider(hits[i].collider) || hits[i].distance >= closest)
                continue;
            closest = hits[i].distance;
            hit = hits[i];
            found = true;
        }
        return found;
    }

    public bool IsTerrainCollider(Collider value)
    {
        if (value == null)
            return false;
        return surfaceCollider != null
            ? value == surfaceCollider
            : value.transform == sphereCenter || value.transform.IsChildOf(sphereCenter);
    }

    public Vector3 GetStreamingSurfaceDirection(System.Random random)
    {
        return RandomSphereDirection(random);
    }

    static Vector3 RandomSphereDirection(System.Random random)
    {
        float y = (float)(random.NextDouble() * 2d - 1d);
        float angle = (float)random.NextDouble() * Mathf.PI * 2f;
        float horizontal = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
        return new Vector3(horizontal * Mathf.Cos(angle), y, horizontal * Mathf.Sin(angle));
    }
}
