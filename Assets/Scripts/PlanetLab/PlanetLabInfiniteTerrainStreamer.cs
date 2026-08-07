using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class PlanetLabInfiniteChunkBuffers
{
    public Vector3[] vertices;
    public Vector3[] normals;
    public Color[] colors;
    public Vector2[] uvs;
    public int[] triangles;
    public float[] heightSamples;

    public void Ensure(int resolution)
    {
        int vertexSide = resolution + 1;
        int vertexCount = vertexSide * vertexSide;
        int sampleSide = vertexSide + 2;
        int triangleIndexCount = resolution * resolution * 6;
        if (vertices == null || vertices.Length != vertexCount)
        {
            vertices = new Vector3[vertexCount];
            normals = new Vector3[vertexCount];
            colors = new Color[vertexCount];
            uvs = new Vector2[vertexCount];
        }
        if (triangles == null || triangles.Length != triangleIndexCount)
            triangles = new int[triangleIndexCount];
        if (heightSamples == null
            || heightSamples.Length != sampleSide * sampleSide)
        {
            heightSamples = new float[sampleSide * sampleSide];
        }
    }
}

[DisallowMultipleComponent]
public sealed class PlanetLabInfiniteTerrainStreamer : MonoBehaviour
{
    const float OceanDisplacementBoundsHeight = 4f;

    sealed class Chunk
    {
        public GameObject root;
        public MeshFilter terrainFilter;
        public MeshRenderer terrainRenderer;
        public MeshCollider terrainCollider;
        public MeshRenderer oceanRenderer;
        public Mesh terrainMesh;
        public PlanetLabInfiniteChunkBuffers buffers =
            new PlanetLabInfiniteChunkBuffers();
        public Vector2Int coordinate;
    }

    readonly Dictionary<Vector2Int, Chunk> activeChunks =
        new Dictionary<Vector2Int, Chunk>();
    readonly Queue<Chunk> pooledChunks = new Queue<Chunk>();
    readonly List<Vector2Int> removalBuffer = new List<Vector2Int>();
    readonly Queue<Vector2Int> buildQueue = new Queue<Vector2Int>();
    readonly HashSet<Vector2Int> queuedCoordinates =
        new HashSet<Vector2Int>();
    readonly HashSet<Vector2Int> desiredCoordinates =
        new HashSet<Vector2Int>();
    readonly HashSet<GameObject> trackedChunkObjects =
        new HashSet<GameObject>();

    GalaxyPlanetDefinition definition;
    Transform target;
    Material terrainMaterial;
    Material oceanMaterial;
    FinitePlanetCombatTerrainPlan combatTerrainPlan;
    Mesh oceanMesh;
    Vector3 anchorDirection = Vector3.up;
    Vector3 east = Vector3.right;
    Vector3 north = Vector3.forward;
    float seaHeight;
    int viewRadius = PlanetLabPlanarSettings.InfiniteViewRadius;
    int chunkResolution = PlanetLabPlanarSettings.InfiniteChunkResolution;
    float chunkSize = PlanetLabPlanarSettings.InfiniteChunkSize;
    bool oceanEnabled;
    bool configured;
    Vector2Int centerCoordinate = new Vector2Int(int.MinValue, int.MinValue);
    int createdChunkCount;
    double lastChunkBuildMilliseconds;
    double maximumChunkBuildMilliseconds;
    double globalOriginX;
    double globalOriginZ;

    public int ActiveChunkCount => activeChunks.Count;
    public int PooledChunkCount => pooledChunks.Count;
    public int CreatedChunkCount => createdChunkCount;
    public Vector2Int CenterCoordinate => centerCoordinate;
    public Vector3 AnchorDirection => anchorDirection;
    public float SeaHeight => seaHeight;
    public double LastChunkBuildMilliseconds => lastChunkBuildMilliseconds;
    public double MaximumChunkBuildMilliseconds => maximumChunkBuildMilliseconds;
    public Mesh InfiniteOceanMesh => oceanMesh;
    public bool IsCenterChunkReady =>
        configured && activeChunks.ContainsKey(centerCoordinate);
    public bool IsFullyReady =>
        configured
        && activeChunks.Count == (viewRadius * 2 + 1) * (viewRadius * 2 + 1)
        && buildQueue.Count == 0;
    public double GlobalOriginX => globalOriginX;
    public double GlobalOriginZ => globalOriginZ;
    public FinitePlanetCombatTerrainPlan CombatTerrainPlan =>
        combatTerrainPlan;

    public event Action<Vector2Int> ChunkActivated;
    public event Action<Vector2Int> ChunkRecycled;

    public void GetActiveCoordinates(List<Vector2Int> destination)
    {
        if (destination == null)
            throw new ArgumentNullException(nameof(destination));
        destination.Clear();
        foreach (Vector2Int coordinate in activeChunks.Keys)
            destination.Add(coordinate);
        destination.Sort((left, right) =>
        {
            int xComparison = left.x.CompareTo(right.x);
            return xComparison != 0
                ? xComparison
                : left.y.CompareTo(right.y);
        });
    }

    public Vector2Int GetChunkCoordinate(
        double planarX,
        double planarZ)
    {
        return new Vector2Int(
            ToChunkIndex(planarX),
            ToChunkIndex(planarZ));
    }

    public void Configure(
        GalaxyPlanetDefinition valueDefinition,
        PlanetLabPlanarSettings settings,
        Transform valueTarget,
        Material valueTerrainMaterial,
        Material valueOceanMaterial,
        bool valueOceanEnabled,
        int valueViewRadius = PlanetLabPlanarSettings.InfiniteViewRadius,
        int valueChunkResolution = PlanetLabPlanarSettings.InfiniteChunkResolution,
        float valueChunkSize = PlanetLabPlanarSettings.InfiniteChunkSize,
        FinitePlanetCombatTerrainPlan valueCombatTerrainPlan = null)
    {
        definition = valueDefinition
            ?? throw new ArgumentNullException(nameof(valueDefinition));
        RemoveOrphanedChunkObjects();
        settings = settings ?? new PlanetLabPlanarSettings();
        settings.ClampValues();
        target = valueTarget;
        terrainMaterial = valueTerrainMaterial;
        oceanMaterial = valueOceanMaterial;
        oceanEnabled = valueOceanEnabled;
        combatTerrainPlan = valueCombatTerrainPlan;
        viewRadius = Mathf.Clamp(valueViewRadius, 1, 4);
        chunkResolution = Mathf.Clamp(valueChunkResolution, 8, 64);
        chunkSize = Mathf.Clamp(valueChunkSize, 32f, 512f);
        anchorDirection = settings.autoAnchor
            ? PlanetLabPlanarPatchMeshBuilder.FindBestLandAnchor(definition)
            : settings.AnchorDirection;
        PlanetLabPlanarPatchMeshBuilder.BuildTangentBasis(
            anchorDirection,
            out east,
            out north);

        PlanetCelestialProfile celestial = definition.celestial
            ?? PlanetCelestialProfile.CreateCompatibleDefault();
        PlanetLowPolyVisualProfile visual = definition.lowPolyVisual
            ?? new PlanetLowPolyVisualProfile();
        seaHeight = visual.oceanLevel
            * Mathf.Max(1f, celestial.maximumTerrainElevation);
        EnsureOceanMesh();
        configured = true;
        RecycleActiveChunks();
        buildQueue.Clear();
        queuedCoordinates.Clear();
        desiredCoordinates.Clear();
        lastChunkBuildMilliseconds = 0d;
        maximumChunkBuildMilliseconds = 0d;
        centerCoordinate = new Vector2Int(int.MinValue, int.MinValue);
        RefreshStreamingCenter(true);
        if (Application.isPlaying)
        {
            if (!activeChunks.ContainsKey(centerCoordinate))
            {
                queuedCoordinates.Remove(centerCoordinate);
                ActivateChunk(centerCoordinate);
            }
        }
        else
        {
            BuildAllQueuedChunks();
        }
    }

    public void SetTarget(Transform valueTarget)
    {
        if (target == valueTarget)
            return;
        target = valueTarget;
        if (configured)
            RefreshStreamingCenter(true);
    }

    public void RefreshAppearance(
        Material valueTerrainMaterial,
        Material valueOceanMaterial,
        bool valueOceanEnabled)
    {
        terrainMaterial = valueTerrainMaterial;
        oceanMaterial = valueOceanMaterial;
        oceanEnabled = valueOceanEnabled;
        foreach (Chunk chunk in activeChunks.Values)
            ApplyChunkAppearance(chunk);
        foreach (Chunk chunk in pooledChunks)
            ApplyChunkAppearance(chunk);
    }

    public float SampleHeight(float worldX, float worldZ)
    {
        return definition == null
            ? 0f
            : SampleInfiniteHeight(
                definition,
                anchorDirection,
                east,
                north,
                worldX,
                worldZ,
                combatTerrainPlan);
    }

    public bool TrySampleSurface(
        double planarX,
        double planarZ,
        out float height,
        out Vector3 normal)
    {
        height = 0f;
        normal = Vector3.up;
        if (definition == null)
            return false;

        float x = (float)planarX;
        float z = (float)planarZ;
        height = SampleHeight(x, z);
        const float sampleStep = 0.5f;
        float west = SampleHeight(x - sampleStep, z);
        float eastHeight = SampleHeight(x + sampleStep, z);
        float south = SampleHeight(x, z - sampleStep);
        float northHeight = SampleHeight(x, z + sampleStep);
        normal = new Vector3(
            -(eastHeight - west) / (sampleStep * 2f),
            1f,
            -(northHeight - south) / (sampleStep * 2f)).normalized;
        return true;
    }

    public void SetGlobalOrigin(double planarX, double planarZ)
    {
        globalOriginX = planarX;
        globalOriginZ = planarZ;
        foreach (KeyValuePair<Vector2Int, Chunk> pair in activeChunks)
            PositionChunk(pair.Value, pair.Key);
        foreach (Chunk chunk in pooledChunks)
            PositionChunk(chunk, chunk.coordinate);
        RefreshStreamingCenter(true);
        if (!Application.isPlaying)
            BuildAllQueuedChunks();
    }

    public void RebuildImmediate()
    {
        if (!configured)
            return;
        RecycleActiveChunks();
        buildQueue.Clear();
        queuedCoordinates.Clear();
        desiredCoordinates.Clear();
        centerCoordinate = new Vector2Int(int.MinValue, int.MinValue);
        RefreshStreamingCenter(true);
        if (Application.isPlaying)
        {
            if (!activeChunks.ContainsKey(centerCoordinate))
            {
                queuedCoordinates.Remove(centerCoordinate);
                ActivateChunk(centerCoordinate);
            }
        }
        else
        {
            BuildAllQueuedChunks();
        }
    }

    public void ClearGeneratedChunks()
    {
        foreach (Chunk chunk in activeChunks.Values)
            DestroyChunk(chunk);
        activeChunks.Clear();
        while (pooledChunks.Count > 0)
            DestroyChunk(pooledChunks.Dequeue());
        if (oceanMesh != null)
            DestroyTransient(oceanMesh);
        oceanMesh = null;
        buildQueue.Clear();
        queuedCoordinates.Clear();
        desiredCoordinates.Clear();
        createdChunkCount = 0;
        centerCoordinate = new Vector2Int(int.MinValue, int.MinValue);
    }

    void Update()
    {
        if (!Application.isPlaying || !configured)
            return;
        RefreshStreamingCenter(false);
        BuildNextQueuedChunk();
    }

    void OnEnable()
    {
        RemoveOrphanedChunkObjects();
    }

    void OnDestroy()
    {
        ClearGeneratedChunks();
    }

    void RemoveOrphanedChunkObjects()
    {
        trackedChunkObjects.Clear();
        foreach (Chunk chunk in activeChunks.Values)
        {
            if (chunk?.root != null)
                trackedChunkObjects.Add(chunk.root);
        }
        foreach (Chunk chunk in pooledChunks)
        {
            if (chunk?.root != null)
                trackedChunkObjects.Add(chunk.root);
        }

        for (int index = transform.childCount - 1; index >= 0; index--)
        {
            GameObject child = transform.GetChild(index).gameObject;
            if (child.name != "Chunk" || trackedChunkObjects.Contains(child))
                continue;
            child.SetActive(false);
            DestroyTransient(child);
        }
    }

    void RefreshStreamingCenter(bool force)
    {
        Vector3 localTarget = target != null
            ? transform.InverseTransformPoint(target.position)
            : Vector3.zero;
        Vector2Int nextCenter = WorldToChunk(
            localTarget.x + (float)globalOriginX,
            localTarget.z + (float)globalOriginZ);
        if (!force && nextCenter == centerCoordinate)
            return;
        centerCoordinate = nextCenter;

        desiredCoordinates.Clear();
        for (int z = -viewRadius; z <= viewRadius; z++)
        for (int x = -viewRadius; x <= viewRadius; x++)
        {
            desiredCoordinates.Add(
                centerCoordinate + new Vector2Int(x, z));
        }

        removalBuffer.Clear();
        foreach (KeyValuePair<Vector2Int, Chunk> pair in activeChunks)
        {
            if (!desiredCoordinates.Contains(pair.Key))
                removalBuffer.Add(pair.Key);
        }
        for (int index = 0; index < removalBuffer.Count; index++)
        {
            Vector2Int coordinate = removalBuffer[index];
            Chunk chunk = activeChunks[coordinate];
            activeChunks.Remove(coordinate);
            ChunkRecycled?.Invoke(coordinate);
            chunk.root.SetActive(false);
            pooledChunks.Enqueue(chunk);
        }

        for (int distance = 0; distance <= viewRadius * 2; distance++)
        for (int z = -viewRadius; z <= viewRadius; z++)
        for (int x = -viewRadius; x <= viewRadius; x++)
        {
            if (Mathf.Abs(x) + Mathf.Abs(z) != distance)
                continue;
            Vector2Int coordinate = centerCoordinate + new Vector2Int(x, z);
            if (!activeChunks.ContainsKey(coordinate)
                && queuedCoordinates.Add(coordinate))
            {
                buildQueue.Enqueue(coordinate);
            }
        }
    }

    void BuildNextQueuedChunk()
    {
        while (buildQueue.Count > 0)
        {
            Vector2Int coordinate = buildQueue.Dequeue();
            queuedCoordinates.Remove(coordinate);
            if (!desiredCoordinates.Contains(coordinate)
                || activeChunks.ContainsKey(coordinate))
            {
                continue;
            }
            ActivateChunk(coordinate);
            return;
        }
    }

    void BuildAllQueuedChunks()
    {
        while (buildQueue.Count > 0)
            BuildNextQueuedChunk();
    }

    void RecycleActiveChunks()
    {
        foreach (Chunk chunk in activeChunks.Values)
        {
            chunk.root.SetActive(false);
            pooledChunks.Enqueue(chunk);
        }
        activeChunks.Clear();
    }

    void ActivateChunk(Vector2Int coordinate)
    {
        long buildStart = System.Diagnostics.Stopwatch.GetTimestamp();
        Chunk chunk = pooledChunks.Count > 0
            ? pooledChunks.Dequeue()
            : CreateChunk();
        chunk.coordinate = coordinate;
        chunk.root.name = "Chunk";
        PositionChunk(chunk, coordinate);
        chunk.root.transform.localRotation = Quaternion.identity;
        chunk.root.transform.localScale = Vector3.one;

        BuildInfiniteChunkMesh(
            definition,
            anchorDirection,
            east,
            north,
            coordinate,
            chunkSize,
            chunkResolution,
            chunk.terrainMesh,
            chunk.buffers,
            combatTerrainPlan);
        chunk.terrainFilter.sharedMesh = chunk.terrainMesh;
        chunk.terrainCollider.sharedMesh = null;
        chunk.terrainCollider.sharedMesh = chunk.terrainMesh;
        ApplyChunkAppearance(chunk);
        chunk.root.SetActive(true);
        activeChunks.Add(coordinate, chunk);
        ChunkActivated?.Invoke(coordinate);
        lastChunkBuildMilliseconds =
            (System.Diagnostics.Stopwatch.GetTimestamp() - buildStart)
            * 1000d
            / System.Diagnostics.Stopwatch.Frequency;
        maximumChunkBuildMilliseconds = Math.Max(
            maximumChunkBuildMilliseconds,
            lastChunkBuildMilliseconds);
    }

    void PositionChunk(Chunk chunk, Vector2Int coordinate)
    {
        if (chunk?.root == null)
            return;
        chunk.root.transform.localPosition = new Vector3(
            (float)(coordinate.x * (double)chunkSize - globalOriginX),
            0f,
            (float)(coordinate.y * (double)chunkSize - globalOriginZ));
    }

    Chunk CreateChunk()
    {
        var chunkRoot = new GameObject("InfiniteChunk")
        {
            hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild
        };
        chunkRoot.transform.SetParent(transform, false);

        var terrainObject = new GameObject("Terrain")
        {
            hideFlags = chunkRoot.hideFlags
        };
        terrainObject.transform.SetParent(chunkRoot.transform, false);
        MeshFilter filter = terrainObject.AddComponent<MeshFilter>();
        MeshRenderer renderer = terrainObject.AddComponent<MeshRenderer>();
        MeshCollider collider = terrainObject.AddComponent<MeshCollider>();
        renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;

        var oceanObject = new GameObject("Ocean")
        {
            hideFlags = chunkRoot.hideFlags
        };
        oceanObject.transform.SetParent(chunkRoot.transform, false);
        MeshFilter oceanFilter = oceanObject.AddComponent<MeshFilter>();
        MeshRenderer oceanRenderer = oceanObject.AddComponent<MeshRenderer>();
        oceanObject.transform.localPosition = new Vector3(0f, seaHeight, 0f);
        oceanFilter.sharedMesh = oceanMesh;
        oceanRenderer.shadowCastingMode = ShadowCastingMode.Off;
        oceanRenderer.receiveShadows = false;

        var mesh = new Mesh
        {
            name = "PlanetLabInfiniteTerrainChunk",
            hideFlags = HideFlags.HideAndDontSave
        };
        createdChunkCount++;
        return new Chunk
        {
            root = chunkRoot,
            terrainFilter = filter,
            terrainRenderer = renderer,
            terrainCollider = collider,
            oceanRenderer = oceanRenderer,
            terrainMesh = mesh
        };
    }

    void ApplyChunkAppearance(Chunk chunk)
    {
        if (chunk == null)
            return;
        if (chunk.terrainRenderer != null)
            chunk.terrainRenderer.sharedMaterial = terrainMaterial;
        if (chunk.oceanRenderer != null)
        {
            chunk.oceanRenderer.transform.localPosition =
                new Vector3(0f, seaHeight, 0f);
            chunk.oceanRenderer.sharedMaterial = oceanMaterial;
            chunk.oceanRenderer.enabled = oceanEnabled && oceanMaterial != null;
        }
    }

    Vector2Int WorldToChunk(float x, float z)
    {
        return GetChunkCoordinate(x, z);
    }

    int ToChunkIndex(double value)
    {
        double coordinate = Math.Floor(
            (value + chunkSize * 0.5d) / chunkSize);
        if (coordinate <= int.MinValue)
            return int.MinValue;
        if (coordinate >= int.MaxValue)
            return int.MaxValue;
        return (int)coordinate;
    }

    void EnsureOceanMesh()
    {
        int oceanResolution = Mathf.Clamp(chunkResolution, 8, 32);
        int vertexSide = oceanResolution + 1;
        int expectedVertexCount = vertexSide * vertexSide;
        if (oceanMesh != null
            && oceanMesh.vertexCount == expectedVertexCount
            && Mathf.Approximately(oceanMesh.bounds.size.x, chunkSize))
        {
            return;
        }
        if (oceanMesh != null)
            DestroyTransient(oceanMesh);

        float half = chunkSize * 0.5f;
        var vertices = new Vector3[expectedVertexCount];
        var normals = new Vector3[expectedVertexCount];
        var uvs = new Vector2[expectedVertexCount];
        var triangles = new int[oceanResolution * oceanResolution * 6];
        for (int zIndex = 0; zIndex <= oceanResolution; zIndex++)
        for (int xIndex = 0; xIndex <= oceanResolution; xIndex++)
        {
            float x01 = xIndex / (float)oceanResolution;
            float z01 = zIndex / (float)oceanResolution;
            int vertexIndex = zIndex * vertexSide + xIndex;
            vertices[vertexIndex] = new Vector3(
                Mathf.Lerp(-half, half, x01),
                0f,
                Mathf.Lerp(-half, half, z01));
            normals[vertexIndex] = Vector3.up;
            uvs[vertexIndex] = new Vector2(x01, z01);
        }

        int triangleIndex = 0;
        for (int zIndex = 0; zIndex < oceanResolution; zIndex++)
        for (int xIndex = 0; xIndex < oceanResolution; xIndex++)
        {
            int a = zIndex * vertexSide + xIndex;
            int b = a + 1;
            int c = a + vertexSide;
            int d = c + 1;
            triangles[triangleIndex++] = a;
            triangles[triangleIndex++] = c;
            triangles[triangleIndex++] = b;
            triangles[triangleIndex++] = b;
            triangles[triangleIndex++] = c;
            triangles[triangleIndex++] = d;
        }

        oceanMesh = new Mesh
        {
            name = "PlanetLabInfiniteOceanChunk",
            hideFlags = HideFlags.HideAndDontSave,
            vertices = vertices,
            normals = normals,
            uv = uvs,
            triangles = triangles
        };
        oceanMesh.bounds = new Bounds(
            Vector3.zero,
            new Vector3(
                chunkSize,
                OceanDisplacementBoundsHeight,
                chunkSize));
        foreach (Chunk chunk in activeChunks.Values)
        {
            MeshFilter filter = chunk.oceanRenderer != null
                ? chunk.oceanRenderer.GetComponent<MeshFilter>()
                : null;
            if (filter != null)
                filter.sharedMesh = oceanMesh;
        }
    }

    static void DestroyChunk(Chunk chunk)
    {
        if (chunk == null)
            return;
        if (chunk.terrainCollider != null)
            chunk.terrainCollider.sharedMesh = null;
        if (chunk.terrainFilter != null)
            chunk.terrainFilter.sharedMesh = null;
        DestroyTransient(chunk.terrainMesh);
        DestroyTransient(chunk.root);
    }

    public static float SampleInfiniteHeight(
        GalaxyPlanetDefinition definition,
        Vector3 anchor,
        Vector3 east,
        Vector3 north,
        float worldX,
        float worldZ,
        FinitePlanetCombatTerrainPlan combatTerrainPlan = null)
    {
        if (definition == null)
            throw new ArgumentNullException(nameof(definition));
        if (combatTerrainPlan != null)
            return combatTerrainPlan.SampleHeight(worldX, worldZ);
        PlanetCelestialProfile celestial = definition.celestial
            ?? PlanetCelestialProfile.CreateCompatibleDefault();
        Vector3 samplePoint = anchor.normalized * Mathf.Max(1f, celestial.radius)
            + east.normalized * worldX
            + north.normalized * worldZ;
        return VoxelQuadSphereTerrain.GetSurfaceNoise(
            samplePoint,
            definition.seed,
            definition.terrain);
    }

    public static Mesh BuildInfiniteChunkMesh(
        GalaxyPlanetDefinition definition,
        Vector3 anchor,
        Vector3 east,
        Vector3 north,
        Vector2Int coordinate,
        float size,
        int resolution,
        Mesh reuse = null,
        PlanetLabInfiniteChunkBuffers buffers = null,
        FinitePlanetCombatTerrainPlan combatTerrainPlan = null)
    {
        if (definition == null)
            throw new ArgumentNullException(nameof(definition));
        size = Mathf.Clamp(size, 32f, 512f);
        resolution = Mathf.Clamp(resolution, 4, 64);
        int vertexSide = resolution + 1;
        int vertexCount = vertexSide * vertexSide;
        buffers = buffers ?? new PlanetLabInfiniteChunkBuffers();
        buffers.Ensure(resolution);
        Vector3[] vertices = buffers.vertices;
        Vector3[] normals = buffers.normals;
        Color[] colors = buffers.colors;
        Vector2[] uvs = buffers.uvs;
        int[] triangles = buffers.triangles;
        int sampleSide = vertexSide + 2;
        float[] heightSamples = buffers.heightSamples;
        float half = size * 0.5f;
        float step = size / resolution;
        float centerX = coordinate.x * size;
        float centerZ = coordinate.y * size;
        PlanetCelestialProfile celestial = definition.celestial
            ?? PlanetCelestialProfile.CreateCompatibleDefault();
        float heightScale = Mathf.Max(
            1f,
            celestial.maximumTerrainElevation,
            combatTerrainPlan != null
                ? combatTerrainPlan.Settings.mountainHeight * 1.65f
                : 0f);

        for (int sampleZ = 0; sampleZ < sampleSide; sampleZ++)
        for (int sampleX = 0; sampleX < sampleSide; sampleX++)
        {
            float worldX = centerX - half + (sampleX - 1) * step;
            float worldZ = centerZ - half + (sampleZ - 1) * step;
            heightSamples[sampleZ * sampleSide + sampleX] =
                SampleInfiniteHeight(
                    definition,
                    anchor,
                    east,
                    north,
                    worldX,
                    worldZ,
                    combatTerrainPlan);
        }

        for (int zIndex = 0; zIndex <= resolution; zIndex++)
        for (int xIndex = 0; xIndex <= resolution; xIndex++)
        {
            int index = zIndex * vertexSide + xIndex;
            float localX = -half + xIndex * step;
            float localZ = -half + zIndex * step;
            float worldX = centerX + localX;
            float worldZ = centerZ + localZ;
            int sampleIndex = (zIndex + 1) * sampleSide + xIndex + 1;
            float height = heightSamples[sampleIndex];
            float west = heightSamples[sampleIndex - 1];
            float eastHeight = heightSamples[sampleIndex + 1];
            float south = heightSamples[sampleIndex - sampleSide];
            float northHeight = heightSamples[sampleIndex + sampleSide];
            vertices[index] = new Vector3(localX, height, localZ);
            normals[index] = new Vector3(
                -(eastHeight - west) / (step * 2f),
                1f,
                -(northHeight - south) / (step * 2f)).normalized;
            colors[index] = Color.Lerp(
                definition.rockColor,
                definition.surfaceColor,
                Mathf.InverseLerp(-heightScale * 0.35f, heightScale, height));
            uvs[index] = new Vector2(worldX / size, worldZ / size);
        }

        int triangleIndex = 0;
        for (int zIndex = 0; zIndex < resolution; zIndex++)
        for (int xIndex = 0; xIndex < resolution; xIndex++)
        {
            int a = zIndex * vertexSide + xIndex;
            int b = a + 1;
            int c = a + vertexSide;
            int d = c + 1;
            triangles[triangleIndex++] = a;
            triangles[triangleIndex++] = c;
            triangles[triangleIndex++] = b;
            triangles[triangleIndex++] = b;
            triangles[triangleIndex++] = c;
            triangles[triangleIndex++] = d;
        }

        Mesh mesh = reuse != null ? reuse : new Mesh();
        mesh.Clear();
        if (reuse == null)
            mesh.name = "PlanetLabInfiniteTerrainChunk";
        mesh.hideFlags = HideFlags.HideAndDontSave;
        if (vertexCount > 65535)
            mesh.indexFormat = IndexFormat.UInt32;
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.colors = colors;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }

    static void DestroyTransient(UnityEngine.Object value)
    {
        if (value == null)
            return;
        if (Application.isPlaying)
            Destroy(value);
        else
            DestroyImmediate(value);
    }
}
