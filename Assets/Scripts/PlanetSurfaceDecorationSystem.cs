using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlanetSurfaceDecorationSystem : MonoBehaviour
{
    struct Placement
    {
        public Vector3 position;
        public float spacing;

        public Placement(Vector3 valuePosition, float valueSpacing)
        {
            position = valuePosition;
            spacing = valueSpacing;
        }
    }

    VoxelQuadSphereWorld world;
    List<PlanetSurfacePropSpawnSettings> settings = new List<PlanetSurfacePropSpawnSettings>();
    readonly HashSet<string> harvestedIds = new HashSet<string>(StringComparer.Ordinal);
    readonly List<Placement> placements = new List<Placement>();
    readonly List<GalaxySurfacePropSaveEntry> snapshots = new List<GalaxySurfacePropSaveEntry>();
    readonly List<Vector3> rejectedCandidates = new List<Vector3>();
    readonly Dictionary<string, int> rejectionCounts = new Dictionary<string, int>(StringComparer.Ordinal);
    GalaxySurfacePropSaveEntry[] savedSnapshot;
    Transform decorationsRoot;
    Transform harvestablesRoot;
    Coroutine previewRoutine;
    bool loadingSnapshot;
    bool showRejectedCandidates;
    bool generationComplete;
    int configurationHash;

    public bool IsGenerationComplete => generationComplete;
    public int ConfigurationHash => configurationHash;
    public IReadOnlyDictionary<string, int> RejectionCounts => rejectionCounts;
    public bool ShowRejectedCandidates
    {
        get => showRejectedCandidates;
        set => showRejectedCandidates = value;
    }

    public void Configure(
        VoxelQuadSphereWorld valueWorld,
        List<PlanetSurfacePropSpawnSettings> valueSettings,
        GalaxyPlanetSaveData save)
    {
        world = valueWorld;
        settings = valueSettings ?? new List<PlanetSurfacePropSpawnSettings>();
        foreach (PlanetSurfacePropSpawnSettings item in settings)
            item?.ClampValues();

        configurationHash = CalculateConfigurationHash(settings);
        harvestedIds.Clear();
        if (save?.harvestedSurfacePropIds != null)
        {
            foreach (string instanceId in save.harvestedSurfacePropIds)
                if (!string.IsNullOrWhiteSpace(instanceId))
                    harvestedIds.Add(instanceId);
        }

        savedSnapshot = save != null ? save.surfaceProps : null;
        loadingSnapshot = save != null
            && save.hasFullSurfacePropSnapshot
            && save.surfacePropConfigurationHash == configurationHash;
        generationComplete = settings.Count == 0;
    }

    public IEnumerator GenerateIncremental(float frameBudgetMilliseconds = 4f)
    {
        generationComplete = false;
        ClearGeneratedSurfaceProps();
        rejectedCandidates.Clear();
        rejectionCounts.Clear();

        if (world == null || settings == null || settings.Count == 0)
        {
            generationComplete = true;
            yield break;
        }

        CreateRoots();
        Physics.SyncTransforms();
        if (TryRestoreSnapshot())
        {
            generationComplete = true;
            yield break;
        }

        float budgetSeconds = Mathf.Max(0.001f, frameBudgetMilliseconds * 0.001f);
        float frameStartedAt = Time.realtimeSinceStartup;
        foreach (PlanetSurfacePropSpawnSettings item in settings)
        {
            if (item == null || item.prefab == null || item.count <= 0
                || string.IsNullOrWhiteSpace(item.catalogId))
                continue;
            if (item.role == PlanetDecorationRole.Vegetation && !item.orientationVerified)
            {
                AddRejection(nameof(item.orientationVerified), Vector3.zero);
                continue;
            }

            item.ClampValues();
            var random = new System.Random(unchecked(world.Seed + item.seedOffset));
            List<Vector3> clusterCenters = CreateClusterCenters(item, random);
            int spawnedCount = 0;
            int maximumAttempts = item.count * item.placementAttempts;
            for (int attempt = 0; attempt < maximumAttempts && spawnedCount < item.count; attempt++)
            {
                Vector3 direction = world.IsStreamingLargePlanet
                    ? world.GetStreamingSurfaceDirection(random)
                    : GetCandidateDirection(item, clusterCenters, random);
                if (!world.TryFindPlanetSurface(direction, out RaycastHit surfaceHit))
                {
                    AddRejection(nameof(surfaceHit), world.GetPlanetCenterWorld() + direction * world.PlanetRadius);
                    continue;
                }

                Vector3 radialUp = (surfaceHit.point - world.GetPlanetCenterWorld()).normalized;
                float slope = Vector3.Angle(surfaceHit.normal, radialUp);
                if (slope > item.maximumSlope)
                {
                    AddRejection(nameof(item.maximumSlope), surfaceHit.point);
                    continue;
                }
                if (item.waterClearance > 0f
                    && PlanetRiverSystem.TrySampleAny(surfaceHit.point, out WaterSample water)
                    && water.signedDistance <= item.waterClearance)
                {
                    AddRejection(nameof(item.waterClearance), surfaceHit.point);
                    continue;
                }

                Vector3 spawnPosition = surfaceHit.point + radialUp * item.surfaceOffset;
                if (!IsPositionValid(spawnPosition, radialUp, item))
                    continue;

                float uniformScale = Mathf.Lerp(
                    item.minimumScale,
                    item.maximumScale,
                    (float)random.NextDouble());
                Vector3 alignmentUp = item.role == PlanetDecorationRole.Vegetation
                    || item.alignment == PlanetDecorationAlignment.GravityUp
                    ? radialUp
                    : surfaceHit.normal.normalized;
                Quaternion rotation = Quaternion.FromToRotation(Vector3.up, alignmentUp);
                if (item.randomizeYaw)
                    rotation *= Quaternion.AngleAxis((float)random.NextDouble() * 360f, Vector3.up);

                string instanceId = item.catalogId + ":" + spawnedCount.ToString("D4");
                bool harvestable = item.IsHarvestable;
                Transform parent = harvestable ? harvestablesRoot : decorationsRoot;
                GameObject instance = null;
                if (!harvestable || !harvestedIds.Contains(instanceId))
                {
                    instance = Instantiate(item.prefab, spawnPosition, rotation, parent);
                    instance.transform.localScale *= uniformScale;
                    PlanetSurfacePropInstance marker = instance.GetComponent<PlanetSurfacePropInstance>();
                    if (marker == null)
                        marker = instance.AddComponent<PlanetSurfacePropInstance>();
                    marker.Configure(item.catalogId, instanceId, harvestable);

                    HarvestableResource resource = instance.GetComponentInChildren<HarvestableResource>(true);
                    if (resource != null)
                        resource.AssignStableResourceId(instanceId);
                    else
                        ApplyCollisionPolicy(instance, item);
                }

                Vector3 localPosition = transform.InverseTransformPoint(spawnPosition);
                Quaternion localRotation = Quaternion.Inverse(transform.rotation) * rotation;
                Vector3 localScale = instance != null
                    ? instance.transform.localScale
                    : item.prefab.transform.localScale * uniformScale;
                placements.Add(new Placement(spawnPosition, item.minimumSpacing));
                snapshots.Add(new GalaxySurfacePropSaveEntry
                {
                    catalogId = item.catalogId,
                    instanceId = instanceId,
                    localPosition = localPosition,
                    localRotation = localRotation,
                    localScale = localScale,
                    minimumSpacing = item.minimumSpacing,
                    harvestable = harvestable
                });
                spawnedCount++;

                if (Time.realtimeSinceStartup - frameStartedAt < budgetSeconds)
                    continue;
                yield return null;
                frameStartedAt = Time.realtimeSinceStartup;
            }

            if (spawnedCount < item.count)
            {
                Debug.LogWarning(
                    $"PlanetSurfaceDecorationSystem: spawned {spawnedCount}/{item.count} of {item.catalogId}. "
                    + "Review spacing, slope, water clearance, or placement attempts.",
                    this);
            }
        }

        generationComplete = true;
        Debug.Log(
            $"PlanetSurfaceDecorationSystem: generated {snapshots.Count} surface props "
            + $"({decorationsRoot.childCount} decorative, {harvestablesRoot.childCount} harvestable).",
            this);
    }

    public void RespawnForPreview()
    {
        if (!Application.isPlaying || world == null)
            return;
        if (previewRoutine != null)
            StopCoroutine(previewRoutine);
        loadingSnapshot = false;
        savedSnapshot = null;
        previewRoutine = StartCoroutine(PreviewRoutine());
    }

    IEnumerator PreviewRoutine()
    {
        yield return GenerateIncremental();
        previewRoutine = null;
    }

    public void ClearGeneratedSurfaceProps()
    {
        placements.Clear();
        snapshots.Clear();
        DestroyRoot(ref decorationsRoot);
        DestroyRoot(ref harvestablesRoot);
    }

    public void MarkHarvested(string instanceId)
    {
        if (!string.IsNullOrWhiteSpace(instanceId))
            harvestedIds.Add(instanceId);
    }

    public string[] GetHarvestedIds()
    {
        string[] result = new string[harvestedIds.Count];
        harvestedIds.CopyTo(result);
        Array.Sort(result, StringComparer.Ordinal);
        return result;
    }

    public GalaxySurfacePropSaveEntry[] GetSnapshots()
    {
        return snapshots.ToArray();
    }

    bool TryRestoreSnapshot()
    {
        if (!loadingSnapshot || savedSnapshot == null)
            return false;

        var settingsById = new Dictionary<string, PlanetSurfacePropSpawnSettings>(StringComparer.Ordinal);
        foreach (PlanetSurfacePropSpawnSettings item in settings)
            if (item != null && item.prefab != null && !string.IsNullOrWhiteSpace(item.catalogId))
                settingsById[item.catalogId] = item;

        foreach (GalaxySurfacePropSaveEntry entry in savedSnapshot)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.catalogId)
                || string.IsNullOrWhiteSpace(entry.instanceId)
                || !settingsById.TryGetValue(entry.catalogId, out PlanetSurfacePropSpawnSettings item))
            {
                loadingSnapshot = false;
                savedSnapshot = null;
                snapshots.Clear();
                placements.Clear();
                return false;
            }
        }

        foreach (GalaxySurfacePropSaveEntry entry in savedSnapshot)
        {
            PlanetSurfacePropSpawnSettings item = settingsById[entry.catalogId];
            Vector3 worldPosition = transform.TransformPoint(entry.localPosition);
            placements.Add(new Placement(worldPosition, entry.minimumSpacing));
            snapshots.Add(entry);
            if (entry.harvestable && harvestedIds.Contains(entry.instanceId))
                continue;

            Transform parent = entry.harvestable ? harvestablesRoot : decorationsRoot;
            GameObject instance = Instantiate(item.prefab, parent);
            instance.transform.localPosition = entry.localPosition;
            instance.transform.localRotation = entry.localRotation;
            instance.transform.localScale = entry.localScale;
            PlanetSurfacePropInstance marker = instance.GetComponent<PlanetSurfacePropInstance>();
            if (marker == null)
                marker = instance.AddComponent<PlanetSurfacePropInstance>();
            marker.Configure(entry.catalogId, entry.instanceId, entry.harvestable);
            HarvestableResource resource = instance.GetComponentInChildren<HarvestableResource>(true);
            if (resource != null)
                resource.AssignStableResourceId(entry.instanceId);
            else
                ApplyCollisionPolicy(instance, item);
        }

        loadingSnapshot = false;
        savedSnapshot = null;
        Debug.Log($"PlanetSurfaceDecorationSystem: restored {snapshots.Count} surface props.", this);
        return true;
    }

    void CreateRoots()
    {
        decorationsRoot = new GameObject("GeneratedDecorations").transform;
        decorationsRoot.SetParent(transform, false);
        harvestablesRoot = new GameObject("GeneratedHarvestableResources").transform;
        harvestablesRoot.SetParent(transform, false);
    }

    bool IsPositionValid(Vector3 position, Vector3 radialUp, PlanetSurfacePropSpawnSettings item)
    {
        Transform player = world.PlayerSpawn;
        if (player != null && item.playerClearRadius > 0f
            && (position - player.position).sqrMagnitude < item.playerClearRadius * item.playerClearRadius)
        {
            AddRejection(nameof(item.playerClearRadius), position);
            return false;
        }

        foreach (Placement placement in placements)
        {
            float spacing = Mathf.Max(item.minimumSpacing, placement.spacing);
            if ((position - placement.position).sqrMagnitude < spacing * spacing)
            {
                AddRejection(nameof(item.minimumSpacing), position);
                return false;
            }
        }

        float overlapRadius = Mathf.Clamp(item.minimumSpacing * 0.2f, 0.15f, 0.8f);
        Collider[] overlaps = Physics.OverlapSphere(
            position + radialUp * overlapRadius,
            overlapRadius,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        foreach (Collider overlap in overlaps)
        {
            if (overlap == null || world.IsTerrainCollider(overlap)
                || overlap.GetComponentInParent<PlanetSurfacePropInstance>() != null)
                continue;
            AddRejection(nameof(overlaps), position);
            return false;
        }
        return true;
    }

    List<Vector3> CreateClusterCenters(PlanetSurfacePropSpawnSettings item, System.Random random)
    {
        var result = new List<Vector3>();
        if (item.clusterRadius <= 0f || item.clusterSize <= 1)
            return result;
        int count = Mathf.Max(1, Mathf.CeilToInt(item.count / (float)item.clusterSize));
        for (int i = 0; i < count; i++)
            result.Add(GetRandomSphereDirection(random));
        return result;
    }

    Vector3 GetCandidateDirection(
        PlanetSurfacePropSpawnSettings item,
        List<Vector3> clusterCenters,
        System.Random random)
    {
        if (clusterCenters == null || clusterCenters.Count == 0)
            return GetRandomSphereDirection(random);

        Vector3 center = clusterCenters[random.Next(0, clusterCenters.Count)];
        Vector3 reference = Mathf.Abs(center.y) < 0.9f ? Vector3.up : Vector3.right;
        Vector3 tangent = Vector3.Cross(reference, center).normalized;
        Vector3 bitangent = Vector3.Cross(center, tangent).normalized;
        float angle = (float)random.NextDouble() * Mathf.PI * 2f;
        float distance = item.clusterRadius * Mathf.Sqrt((float)random.NextDouble());
        float angularOffset = distance / Mathf.Max(1f, world.PlanetRadius);
        Vector3 offset = (Mathf.Cos(angle) * tangent + Mathf.Sin(angle) * bitangent) * angularOffset;
        return (center + offset).normalized;
    }

    static Vector3 GetRandomSphereDirection(System.Random random)
    {
        float y = (float)(random.NextDouble() * 2d - 1d);
        float angle = (float)random.NextDouble() * Mathf.PI * 2f;
        float horizontal = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
        return new Vector3(horizontal * Mathf.Cos(angle), y, horizontal * Mathf.Sin(angle));
    }

    static void ApplyCollisionPolicy(GameObject instance, PlanetSurfacePropSpawnSettings item)
    {
        if (instance == null || item.collision == PlanetDecorationCollision.KeepPrefab)
            return;

        Collider[] colliders = instance.GetComponentsInChildren<Collider>(true);
        foreach (Collider value in colliders)
        {
            if (Application.isPlaying)
                Destroy(value);
            else
                DestroyImmediate(value);
        }
        if (item.collision == PlanetDecorationCollision.None)
            return;

        Bounds bounds = CalculateLocalRendererBounds(instance.transform);
        if (bounds.size.sqrMagnitude < 0.0001f)
            return;
        if (item.role == PlanetDecorationRole.Vegetation)
        {
            CapsuleCollider capsule = instance.AddComponent<CapsuleCollider>();
            capsule.direction = 1;
            float visibleRadius = Mathf.Min(bounds.extents.x, bounds.extents.z);
            capsule.radius = Mathf.Clamp(
                visibleRadius * 0.22f,
                0.08f,
                Mathf.Min(0.65f, bounds.size.y * 0.12f));
            capsule.height = Mathf.Clamp(bounds.size.y * 0.68f, capsule.radius * 2f, bounds.size.y);
            capsule.center = new Vector3(
                bounds.center.x,
                bounds.min.y + capsule.height * 0.5f,
                bounds.center.z);
        }
        else
        {
            if (TryAddConvexMeshColliders(instance))
                return;
            if (instance.GetComponentsInChildren<MeshFilter>(true).Length > 8)
            {
                AddFittedCapsuleCollider(instance, bounds);
                return;
            }
            BoxCollider box = instance.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = Vector3.Max(Vector3.Scale(bounds.size, Vector3.one * 0.82f), Vector3.one * 0.05f);
        }
    }

    static bool TryAddConvexMeshColliders(GameObject instance)
    {
        bool added = false;
        MeshFilter[] filters = instance.GetComponentsInChildren<MeshFilter>(true);
        if (filters.Length > 8)
            return false;
        foreach (MeshFilter filter in filters)
        {
            if (filter == null || filter.sharedMesh == null || filter.sharedMesh.vertexCount < 4)
                continue;
            MeshCollider collider = filter.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = filter.sharedMesh;
            collider.convex = true;
            added = true;
        }
        return added;
    }

    static void AddFittedCapsuleCollider(GameObject instance, Bounds bounds)
    {
        CapsuleCollider capsule = instance.AddComponent<CapsuleCollider>();
        capsule.center = bounds.center;
        Vector3 size = Vector3.Max(bounds.size, Vector3.one * 0.05f);
        if (size.x >= size.y && size.x >= size.z)
        {
            capsule.direction = 0;
            capsule.radius = Mathf.Max(0.05f, Mathf.Min(size.y, size.z) * 0.38f);
            capsule.height = Mathf.Max(capsule.radius * 2f, size.x * 0.9f);
        }
        else if (size.z >= size.x && size.z >= size.y)
        {
            capsule.direction = 2;
            capsule.radius = Mathf.Max(0.05f, Mathf.Min(size.x, size.y) * 0.38f);
            capsule.height = Mathf.Max(capsule.radius * 2f, size.z * 0.9f);
        }
        else
        {
            capsule.direction = 1;
            capsule.radius = Mathf.Max(0.05f, Mathf.Min(size.x, size.z) * 0.38f);
            capsule.height = Mathf.Max(capsule.radius * 2f, size.y * 0.9f);
        }
    }

    static Bounds CalculateLocalRendererBounds(Transform root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return new Bounds(Vector3.zero, Vector3.zero);

        Vector3 minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Vector3 maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        foreach (Renderer rendererValue in renderers)
        {
            Bounds bounds = rendererValue.localBounds;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 rendererCorner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
                Vector3 local = root.InverseTransformPoint(rendererValue.transform.TransformPoint(rendererCorner));
                minimum = Vector3.Min(minimum, local);
                maximum = Vector3.Max(maximum, local);
            }
        }
        return new Bounds((minimum + maximum) * 0.5f, maximum - minimum);
    }

    int CalculateConfigurationHash(List<PlanetSurfacePropSpawnSettings> values)
    {
        unchecked
        {
            int hash = 486187739;
            hash = hash * 31 + (world != null ? world.TerrainConfigurationHash : 0);
            foreach (PlanetSurfacePropSpawnSettings item in values)
            {
                if (item == null)
                {
                    hash *= 31;
                    continue;
                }
                hash = hash * 31 + PlanetDecorationCatalog.StableHash(item.catalogId);
                hash = hash * 31 + PlanetDecorationCatalog.StableHash(item.prefab != null ? item.prefab.name : string.Empty);
                hash = hash * 31 + item.count;
                hash = hash * 31 + item.seedOffset;
                hash = hash * 31 + item.minimumScale.GetHashCode();
                hash = hash * 31 + item.maximumScale.GetHashCode();
                hash = hash * 31 + item.surfaceOffset.GetHashCode();
                hash = hash * 31 + item.minimumSpacing.GetHashCode();
                hash = hash * 31 + item.maximumSlope.GetHashCode();
                hash = hash * 31 + item.clusterRadius.GetHashCode();
                hash = hash * 31 + item.clusterSize;
                hash = hash * 31 + (int)item.alignment;
                hash = hash * 31 + (int)item.collision;
            }
            return hash;
        }
    }

    void AddRejection(string reason, Vector3 position)
    {
        if (!rejectionCounts.ContainsKey(reason))
            rejectionCounts[reason] = 0;
        rejectionCounts[reason]++;
        if (rejectedCandidates.Count < 256 && position.sqrMagnitude > 0.001f)
            rejectedCandidates.Add(position);
    }

    void DestroyRoot(ref Transform root)
    {
        if (root == null)
            return;
        if (Application.isPlaying)
            Destroy(root.gameObject);
        else
            DestroyImmediate(root.gameObject);
        root = null;
    }

    void OnDrawGizmosSelected()
    {
        if (!showRejectedCandidates)
            return;
        Gizmos.color = new Color(1f, 0.15f, 0.08f, 0.75f);
        foreach (Vector3 point in rejectedCandidates)
            Gizmos.DrawSphere(point, 0.16f);
    }
}
