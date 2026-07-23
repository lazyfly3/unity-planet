using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class AsteroidFieldSystem : MonoBehaviour
{
    readonly struct ChunkKey : IEquatable<ChunkKey>
    {
        public readonly long x;
        public readonly long y;
        public readonly long z;

        public ChunkKey(long x, long y, long z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public bool Equals(ChunkKey other) => x == other.x && y == other.y && z == other.z;
        public override bool Equals(object value) => value is ChunkKey other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = x.GetHashCode();
                hash = hash * 397 ^ y.GetHashCode();
                return hash * 397 ^ z.GetHashCode();
            }
        }
    }

    sealed class ChunkRuntime
    {
        public readonly List<AsteroidBody> bodies = new List<AsteroidBody>();
    }

    struct StableRandom
    {
        ulong state;

        public StableRandom(ulong seed)
        {
            state = seed == 0UL ? 0x9E3779B97F4A7C15UL : seed;
        }

        public ulong NextUlong()
        {
            state ^= state >> 12;
            state ^= state << 25;
            state ^= state >> 27;
            return state * 2685821657736338717UL;
        }

        public float Next01() => (NextUlong() >> 40) * (1f / 16777216f);
        public float Range(float minimum, float maximum) => Mathf.Lerp(minimum, maximum, Next01());
        public int Range(int minimum, int maximum) => minimum + (int)(NextUlong() % (uint)(maximum - minimum));
        public Vector3 InsideUnitCube() => new Vector3(Range(-1f, 1f), Range(-1f, 1f), Range(-1f, 1f));
    }

    [SerializeField] InterstellarFlightRuntime runtime;
    [SerializeField] Transform asteroidRoot;
    [SerializeField, Min(100f)] float chunkSize = 700f;
    [SerializeField, Range(1, 2)] int activeChunkRadius = 1;
    [SerializeField, Range(1, 8)] int asteroidsPerChunk = 3;
    [SerializeField, Min(1f)] float minimumRadius = 5f;
    [SerializeField, Min(2f)] float maximumRadius = 22f;
    [SerializeField, Min(0f)] float safeSpawnRadius = 120f;
    [SerializeField] Material asteroidMaterial;

    readonly Dictionary<ChunkKey, ChunkRuntime> activeChunks = new Dictionary<ChunkKey, ChunkRuntime>();
    readonly HashSet<ChunkKey> requiredChunks = new HashSet<ChunkKey>();
    readonly List<ChunkKey> removalBuffer = new List<ChunkKey>();
    Mesh[] renderMeshes;
    Mesh[] colliderMeshes;
    float nextRefresh;
    Material runtimeMaterial;

    void Awake()
    {
        if (runtime == null)
            runtime = FindObjectOfType<InterstellarFlightRuntime>();
        if (asteroidRoot == null)
            asteroidRoot = GameObject.Find("AsteroidRuntimeRoot")?.transform ?? transform;
        LoadMeshes();
        if (asteroidMaterial == null)
        {
            runtimeMaterial = new Material(Shader.Find("Standard"))
            {
                name = "RuntimeLowPolyAsteroid"
            };
            runtimeMaterial.color = new Color(0.29f, 0.3f, 0.32f);
            runtimeMaterial.SetFloat("_Glossiness", 0.08f);
            asteroidMaterial = runtimeMaterial;
        }
    }

    void OnEnable()
    {
        if (runtime != null)
        {
            runtime.OriginShifted += HandleOriginShift;
            runtime.UniverseRelocated += HandleUniverseRelocated;
        }
    }

    void Start()
    {
        RefreshChunks();
    }

    void Update()
    {
        if (Time.unscaledTime < nextRefresh)
            return;
        nextRefresh = Time.unscaledTime + 0.75f;
        RefreshChunks();
    }

    void LoadMeshes()
    {
        Mesh[] allMeshes = Resources.LoadAll<Mesh>("Spaceflight/LowPolyAsteroids");
        var renders = new List<Mesh>();
        var colliders = new List<Mesh>();
        foreach (Mesh mesh in allMeshes)
        {
            if (mesh.name.EndsWith("_RENDER", StringComparison.OrdinalIgnoreCase))
                renders.Add(mesh);
            else if (mesh.name.EndsWith("_COLLIDER", StringComparison.OrdinalIgnoreCase))
                colliders.Add(mesh);
        }
        renders.Sort((left, right) => string.CompareOrdinal(left.name, right.name));
        colliders.Sort((left, right) => string.CompareOrdinal(left.name, right.name));
        renderMeshes = renders.ToArray();
        colliderMeshes = colliders.ToArray();
        if (renderMeshes.Length == 0)
            Debug.LogError("AsteroidFieldSystem: no *_RENDER meshes were found in LowPolyAsteroids.fbx.", this);
        if (colliderMeshes.Length == 0)
            Debug.LogError("AsteroidFieldSystem: no *_COLLIDER meshes were found in LowPolyAsteroids.fbx.", this);
    }

    void RefreshChunks()
    {
        if (runtime == null || renderMeshes == null || renderMeshes.Length == 0)
            return;
        DoubleVector3 shipPosition = runtime.ShipUniversePosition;
        ChunkKey center = GetChunk(shipPosition);
        requiredChunks.Clear();
        for (long z = center.z - activeChunkRadius; z <= center.z + activeChunkRadius; z++)
        for (long y = center.y - activeChunkRadius; y <= center.y + activeChunkRadius; y++)
        for (long x = center.x - activeChunkRadius; x <= center.x + activeChunkRadius; x++)
        {
            var key = new ChunkKey(x, y, z);
            requiredChunks.Add(key);
            if (!activeChunks.ContainsKey(key))
                activeChunks.Add(key, SpawnChunk(key));
        }

        removalBuffer.Clear();
        foreach (KeyValuePair<ChunkKey, ChunkRuntime> entry in activeChunks)
        {
            if (!requiredChunks.Contains(entry.Key))
                removalBuffer.Add(entry.Key);
        }
        foreach (ChunkKey key in removalBuffer)
        {
            ChunkRuntime chunk = activeChunks[key];
            foreach (AsteroidBody body in chunk.bodies)
            {
                if (body != null)
                    Destroy(body.gameObject);
            }
            activeChunks.Remove(key);
        }
    }

    ChunkRuntime SpawnChunk(ChunkKey key)
    {
        var result = new ChunkRuntime();
        int worldSeed = GalaxyTravelManager.Instance == null ? 0 : GalaxyTravelManager.Instance.WorldSeed;
        ulong chunkSeed = Hash(worldSeed, key.x, key.y, key.z);
        var random = new StableRandom(chunkSeed);
        for (int index = 0; index < asteroidsPerChunk; index++)
        {
            DoubleVector3 universePosition = new DoubleVector3(
                (key.x + random.Next01()) * chunkSize,
                (key.y + random.Next01()) * chunkSize,
                (key.z + random.Next01()) * chunkSize);
            if (DistanceSquared(universePosition, runtime.ShipUniversePosition) < safeSpawnRadius * safeSpawnRadius)
                continue;

            int shapeIndex = random.Range(0, renderMeshes.Length);
            Mesh colliderMesh = colliderMeshes.Length == 0
                ? renderMeshes[shapeIndex]
                : colliderMeshes[shapeIndex % colliderMeshes.Length];
            float radius = random.Range(minimumRadius, maximumRadius);
            Vector3 nonUniform = new Vector3(
                random.Range(0.72f, 1.35f),
                random.Range(0.72f, 1.35f),
                random.Range(0.72f, 1.35f));
            Mesh variantMesh = CreateVariantMesh(renderMeshes[shapeIndex], random.NextUlong());
            string stableId = $"a_{key.x}_{key.y}_{key.z}_{index}";
            AsteroidBody body = AsteroidBody.Create(
                asteroidRoot,
                stableId,
                variantMesh,
                colliderMesh,
                asteroidMaterial,
                runtime.ToLocalPosition(universePosition),
                radius,
                nonUniform,
                Quaternion.Euler(random.Range(0f, 360f), random.Range(0f, 360f), random.Range(0f, 360f)),
                random.InsideUnitCube() * random.Range(1f, 11f),
                random.InsideUnitCube() * random.Range(0.08f, 0.8f),
                random.NextUlong());
            result.bodies.Add(body);
        }
        return result;
    }

    static Mesh CreateVariantMesh(Mesh source, ulong seed)
    {
        Mesh mesh = Instantiate(source);
        mesh.name = source.name + "_PCG";
        Vector3[] vertices = mesh.vertices;
        var random = new StableRandom(seed);
        Vector3 stretch = new Vector3(
            random.Range(0.86f, 1.15f),
            random.Range(0.86f, 1.15f),
            random.Range(0.86f, 1.15f));
        Vector3 craterDirection = random.InsideUnitCube().normalized;
        float craterDepth = random.Range(0.04f, 0.17f);
        for (int index = 0; index < vertices.Length; index++)
        {
            Vector3 vertex = Vector3.Scale(vertices[index], stretch);
            Vector3 direction = vertex.sqrMagnitude > 0.0001f ? vertex.normalized : Vector3.up;
            float facetNoise = 0.92f + Hash01(seed ^ (ulong)(index * 7919)) * 0.16f;
            float crater = Mathf.Pow(Mathf.Clamp01(Vector3.Dot(direction, craterDirection)), 8f) * craterDepth;
            vertices[index] = vertex * (facetNoise - crater);
        }
        mesh.vertices = vertices;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    void HandleOriginShift(Vector3 shift)
    {
        foreach (ChunkRuntime chunk in activeChunks.Values)
        {
            foreach (AsteroidBody body in chunk.bodies)
            {
                if (body != null)
                    body.ShiftPosition(shift);
            }
        }
    }

    void HandleUniverseRelocated(DoubleVector3 previousPosition, DoubleVector3 currentPosition)
    {
        ClearAllChunks();
        nextRefresh = 0f;
        RefreshChunks();
    }

    void ClearAllChunks()
    {
        foreach (ChunkRuntime chunk in activeChunks.Values)
        {
            foreach (AsteroidBody body in chunk.bodies)
            {
                if (body != null)
                    Destroy(body.gameObject);
            }
        }
        activeChunks.Clear();
        requiredChunks.Clear();
        removalBuffer.Clear();
    }

    ChunkKey GetChunk(DoubleVector3 position) => new ChunkKey(
        (long)Math.Floor(position.x / chunkSize),
        (long)Math.Floor(position.y / chunkSize),
        (long)Math.Floor(position.z / chunkSize));

    static double DistanceSquared(DoubleVector3 left, DoubleVector3 right)
    {
        double x = left.x - right.x;
        double y = left.y - right.y;
        double z = left.z - right.z;
        return x * x + y * y + z * z;
    }

    static float Hash01(ulong value)
    {
        value ^= value >> 30;
        value *= 0xBF58476D1CE4E5B9UL;
        value ^= value >> 27;
        value *= 0x94D049BB133111EBUL;
        value ^= value >> 31;
        return (value >> 40) * (1f / 16777216f);
    }

    static ulong Hash(int seed, long x, long y, long z)
    {
        ulong value = unchecked((uint)seed) ^ 0xD6E8FEB86659FD93UL;
        value ^= unchecked((ulong)x) * 0x9E3779B97F4A7C15UL;
        value ^= unchecked((ulong)y) * 0xBF58476D1CE4E5B9UL;
        value ^= unchecked((ulong)z) * 0x94D049BB133111EBUL;
        value ^= value >> 29;
        value *= 0x165667B19E3779F9UL;
        return value ^ (value >> 32);
    }

    void OnDisable()
    {
        if (runtime != null)
        {
            runtime.OriginShifted -= HandleOriginShift;
            runtime.UniverseRelocated -= HandleUniverseRelocated;
        }
    }

    void OnDestroy()
    {
        if (runtimeMaterial != null)
            Destroy(runtimeMaterial);
    }
}

[DisallowMultipleComponent]
public sealed class AsteroidBody : MonoBehaviour, ISpaceDamageable, ISpaceWeaponTarget
{
    Rigidbody body;
    Mesh ownedRenderMesh;
    Mesh fragmentMesh;
    Material sharedMaterial;
    ulong fragmentSeed;
    float maximumIntegrity;
    float integrity;
    bool destroyed;

    public string StableId { get; private set; }
    public float PhysicalMass => body == null ? 1f : body.mass;
    public float Integrity => integrity;
    public float MaximumIntegrity => maximumIntegrity;
    public bool IsDestroyed => destroyed;
    public SpaceWeaponTargetKind TargetKind => SpaceWeaponTargetKind.Asteroid;
    public Transform TargetTransform => transform;
    public Vector3 AimPosition => body == null ? transform.position : body.worldCenterOfMass;
    public Vector3 Velocity => body == null ? Vector3.zero : body.GetPointVelocity(AimPosition);
    public bool IsTargetable => isActiveAndEnabled && !destroyed;

    void OnEnable()
    {
        SpaceWeaponTargetRegistry.Register(this);
    }

    void OnDisable()
    {
        SpaceWeaponTargetRegistry.Unregister(this);
    }

    public bool Owns(Transform candidate)
    {
        return candidate != null && (candidate == transform || candidate.IsChildOf(transform));
    }

    public static AsteroidBody Create(
        Transform parent,
        string stableId,
        Mesh renderMesh,
        Mesh colliderMesh,
        Material material,
        Vector3 position,
        float radius,
        Vector3 nonUniformScale,
        Quaternion rotation,
        Vector3 velocity,
        Vector3 angularVelocity,
        ulong fragmentSeed)
    {
        var asteroid = new GameObject(stableId);
        asteroid.transform.SetParent(parent, false);
        asteroid.transform.SetPositionAndRotation(position, rotation);
        asteroid.transform.localScale = nonUniformScale * radius;
        var filter = asteroid.AddComponent<MeshFilter>();
        filter.sharedMesh = renderMesh;
        var renderer = asteroid.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        var collider = asteroid.AddComponent<MeshCollider>();
        collider.sharedMesh = colliderMesh;
        collider.convex = true;
        var rigidbody = asteroid.AddComponent<Rigidbody>();
        rigidbody.useGravity = false;
        rigidbody.mass = Mathf.Max(25f, radius * radius * radius * 0.35f);
        rigidbody.drag = 0f;
        rigidbody.angularDrag = 0f;
        rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        rigidbody.collisionDetectionMode = radius > 13f
            ? CollisionDetectionMode.ContinuousSpeculative
            : CollisionDetectionMode.Discrete;
        rigidbody.velocity = velocity;
        rigidbody.angularVelocity = angularVelocity;

        var result = asteroid.AddComponent<AsteroidBody>();
        result.body = rigidbody;
        result.ownedRenderMesh = renderMesh;
        result.fragmentMesh = colliderMesh;
        result.sharedMaterial = material;
        result.fragmentSeed = fragmentSeed;
        result.StableId = stableId;
        result.maximumIntegrity = Mathf.Max(20f, radius * radius * 2.2f);
        result.integrity = result.maximumIntegrity;
        return result;
    }

    public void ApplyDamage(SpaceDamageInfo damage)
    {
        if (destroyed || damage.amount <= 0f)
            return;
        integrity = Mathf.Max(0f, integrity - damage.amount);
        if (body != null && damage.impulse.sqrMagnitude > 0.001f)
            body.AddForceAtPosition(damage.impulse, damage.point, ForceMode.Impulse);
        if (integrity <= 0f)
            BreakApart(damage);
    }

    public void ShiftPosition(Vector3 shift)
    {
        if (body != null)
            body.position -= shift;
        else
            transform.position -= shift;
    }

    void BreakApart(SpaceDamageInfo damage)
    {
        destroyed = true;
        SpaceCombatVfxPool effects = SpaceCombatVfxPool.Instance;
        if (effects != null && effects.Catalog != null)
        {
            Vector3 direction = damage.impulse.sqrMagnitude > 0.001f
                ? -damage.impulse.normalized
                : transform.forward;
            effects.PlayOneShot(effects.Catalog.AsteroidDestructionPrefab, transform.position,
                Quaternion.LookRotation(direction),
                Mathf.Clamp(transform.lossyScale.magnitude * 0.12f, 0.8f, 3.5f), 3f);
        }
        int count = 3 + (int)(fragmentSeed % 4UL);
        for (int index = 0; index < count; index++)
        {
            ulong seed = fragmentSeed + (ulong)(index * 0x9E37);
            Vector3 direction = new Vector3(
                HashSigned(seed),
                HashSigned(seed ^ 0xA511E9B3UL),
                HashSigned(seed ^ 0x63D83595UL)).normalized;
            GameObject fragment = new GameObject(StableId + "_fragment_" + index);
            fragment.transform.SetPositionAndRotation(
                transform.position + direction * transform.lossyScale.magnitude * 0.08f,
                Quaternion.Euler(
                    Hash01(seed ^ 0x52DCE729UL) * 360f,
                    Hash01(seed ^ 0x38495AB5UL) * 360f,
                    Hash01(seed ^ 0x7B7D159CUL) * 360f));
            fragment.transform.localScale = transform.lossyScale * Mathf.Lerp(0.18f, 0.36f, Hash01(seed));
            fragment.AddComponent<MeshFilter>().sharedMesh = fragmentMesh;
            fragment.AddComponent<MeshRenderer>().sharedMaterial = sharedMaterial;
            var collider = fragment.AddComponent<MeshCollider>();
            collider.sharedMesh = fragmentMesh;
            collider.convex = true;
            var fragmentBody = fragment.AddComponent<Rigidbody>();
            fragmentBody.useGravity = false;
            fragmentBody.mass = Mathf.Max(1f, PhysicalMass / count * 0.35f);
            fragmentBody.velocity = (body == null ? Vector3.zero : body.velocity) + direction * (4f + Hash01(seed ^ 77UL) * 12f);
            fragmentBody.angularVelocity = direction * (1f + Hash01(seed ^ 91UL) * 5f);
            Destroy(fragment, 10f);
        }
        Destroy(gameObject);
    }

    static float HashSigned(ulong value) => Hash01(value) * 2f - 1f;

    static float Hash01(ulong value)
    {
        value ^= value >> 30;
        value *= 0xBF58476D1CE4E5B9UL;
        value ^= value >> 27;
        value *= 0x94D049BB133111EBUL;
        value ^= value >> 31;
        return (value >> 40) * (1f / 16777216f);
    }

    void OnDestroy()
    {
        SpaceWeaponTargetRegistry.Unregister(this);
        if (ownedRenderMesh != null)
            Destroy(ownedRenderMesh);
    }
}
