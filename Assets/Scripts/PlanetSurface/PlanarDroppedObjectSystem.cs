using System;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-200)]
[DisallowMultipleComponent]
public sealed class PlanarDroppedObjectSystem : MonoBehaviour
{
    public const string PlaceholderCubeTypeId =
        "placeholder.white_cube.v1";
    public const float PlaceholderCubeSize = 1f;
    public const float PlaceholderCubeMass = 100f;
    public const float PlaceholderCubeDragCoefficient = 1.05f;

    sealed class RuntimeEntry
    {
        public GalaxyDroppedObjectSaveEntry record;
        public Vector2Int chunk;
        public PlanetaryDroppedBody runtimeBody;
        public float settledDuration;
    }

    readonly Dictionary<string, RuntimeEntry> entries =
        new Dictionary<string, RuntimeEntry>(StringComparer.Ordinal);
    readonly Dictionary<Vector2Int, HashSet<string>> chunkEntries =
        new Dictionary<Vector2Int, HashSet<string>>();
    readonly HashSet<Vector2Int> activeChunks =
        new HashSet<Vector2Int>();
    readonly HashSet<string> activeIds =
        new HashSet<string>(StringComparer.Ordinal);
    readonly List<Vector2Int> coordinateBuffer =
        new List<Vector2Int>(25);
    readonly List<RuntimeEntry> entryBuffer =
        new List<RuntimeEntry>();
    readonly List<string> activeIdBuffer =
        new List<string>();
    readonly Collider[] overlapBuffer = new Collider[64];

    InfinitePlanarSurfaceWorld world;
    PlanetLabInfiniteTerrainStreamer terrain;
    PlanetCelestialProfile celestial;
    Material whiteMaterial;
    PhysicMaterial physicsMaterial;
    bool configured;
    string currentDraftId;
    int nextBoundaryOrder;

    public int RecordCount => entries.Count;
    public int ActiveBodyCount => activeIds.Count;
    public string CurrentDraftId => currentDraftId;
    public int CurrentDraftLockedCount =>
        CountCurrentDraft(true);
    public int CurrentDraftPendingCount =>
        CountCurrentDraft(false);
    public event Action DraftChanged;
    public event Action<GalaxyDroppedObjectSaveEntry> BoundaryLocked;

    public void Configure(
        InfinitePlanarSurfaceWorld valueWorld,
        PlanetLabInfiniteTerrainStreamer valueTerrain,
        PlanetCelestialProfile valueCelestial,
        GalaxyDroppedObjectSaveEntry[] restored)
    {
        if (configured && terrain != null)
        {
            terrain.ChunkActivated -= HandleChunkActivated;
            terrain.ChunkRecycled -= HandleChunkRecycled;
        }

        world = valueWorld;
        terrain = valueTerrain;
        celestial = valueCelestial
            ?? PlanetCelestialProfile.CreateCompatibleDefault();
        configured = world != null && terrain != null;
        entries.Clear();
        chunkEntries.Clear();
        activeChunks.Clear();
        activeIds.Clear();

        if (!configured)
            return;

        RestoreRecords(restored);
        ResolveCurrentDraft();
        terrain.ChunkActivated += HandleChunkActivated;
        terrain.ChunkRecycled += HandleChunkRecycled;
        terrain.GetActiveCoordinates(coordinateBuffer);
        for (int i = 0; i < coordinateBuffer.Count; i++)
            HandleChunkActivated(coordinateBuffer[i]);
    }

    public bool TryDropPlaceholderCube(
        Rigidbody shipBody,
        Collider[] shipColliders,
        out string failureReason)
    {
        failureReason = string.Empty;
        if (!configured || shipBody == null)
        {
            failureReason = "投放系统尚未就绪";
            return false;
        }

        Vector3 center = shipBody.worldCenterOfMass;
        Vector3 up = world.GetUp(center);
        if (up.sqrMagnitude <= 0.0001f)
            up = Vector3.up;
        up.Normalize();
        Vector3 down = -up;
        Quaternion rotation = shipBody.rotation;
        Vector3 releasePoint = CalculateReleasePoint(
            shipBody,
            shipColliders,
            up,
            PlaceholderCubeSize);

        int overlapCount = Physics.OverlapBoxNonAlloc(
            releasePoint,
            Vector3.one * (PlaceholderCubeSize * 0.49f),
            overlapBuffer,
            rotation,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        if (overlapCount > 0)
        {
            failureReason = "飞船下方空间不足，无法投放";
            return false;
        }

        PlanarSurfaceAddress address =
            world.ToPersistentAddress(releasePoint);
        Vector2Int chunk = terrain.GetChunkCoordinate(
            address.x,
            address.z);
        if (!activeChunks.Contains(chunk))
        {
            failureReason = "投放位置的地形碰撞尚未就绪";
            return false;
        }

        var record = new GalaxyDroppedObjectSaveEntry
        {
            objectId = Guid.NewGuid().ToString("N"),
            payloadTypeId = PlaceholderCubeTypeId,
            planarX = address.x,
            planarZ = address.z,
            planarY = address.y,
            rotation = rotation,
            velocity = shipBody.GetPointVelocity(releasePoint),
            angularVelocity = shipBody.angularVelocity,
            sleeping = false,
            cityDraftId = currentDraftId,
            cityBoundaryOrder = nextBoundaryOrder++,
            cityBoundaryLocked = false
        };
        var entry = new RuntimeEntry
        {
            record = record,
            chunk = chunk
        };
        entries.Add(record.objectId, entry);
        AddToChunk(entry);
        Spawn(entry);
        DraftChanged?.Invoke();
        return entry.runtimeBody != null;
    }

    public void GetCurrentDraftRecords(
        List<GalaxyDroppedObjectSaveEntry> destination)
    {
        if (destination == null)
            return;
        destination.Clear();
        foreach (RuntimeEntry entry in entries.Values)
        {
            if (entry?.record == null
                || entry.record.cityDraftId != currentDraftId
                || !string.IsNullOrEmpty(entry.record.claimedCityId))
            {
                continue;
            }
            Snapshot(entry);
            destination.Add(Clone(entry.record));
        }
        destination.Sort((left, right) =>
            left.cityBoundaryOrder.CompareTo(
                right.cityBoundaryOrder));
    }

    public bool TryUndoLastDraftPoint()
    {
        RuntimeEntry latest = null;
        foreach (RuntimeEntry entry in entries.Values)
        {
            if (entry?.record == null
                || entry.record.cityDraftId != currentDraftId
                || !string.IsNullOrEmpty(entry.record.claimedCityId))
            {
                continue;
            }
            if (latest == null
                || entry.record.cityBoundaryOrder
                    > latest.record.cityBoundaryOrder)
            {
                latest = entry;
            }
        }
        if (latest == null)
            return false;
        RemoveEntry(latest);
        nextBoundaryOrder = Mathf.Max(
            0,
            nextBoundaryOrder - 1);
        DraftChanged?.Invoke();
        return true;
    }

    public int CancelCurrentDraft()
    {
        entryBuffer.Clear();
        foreach (RuntimeEntry entry in entries.Values)
        {
            if (entry?.record != null
                && entry.record.cityDraftId == currentDraftId
                && string.IsNullOrEmpty(
                    entry.record.claimedCityId))
            {
                entryBuffer.Add(entry);
            }
        }
        for (int i = 0; i < entryBuffer.Count; i++)
            RemoveEntry(entryBuffer[i]);
        int removed = entryBuffer.Count;
        BeginNewDraft();
        DraftChanged?.Invoke();
        return removed;
    }

    public bool ClaimCurrentDraft(
        string cityId,
        string districtId)
    {
        if (string.IsNullOrWhiteSpace(cityId)
            || string.IsNullOrWhiteSpace(districtId)
            || CurrentDraftPendingCount > 0
            || CurrentDraftLockedCount < 3)
        {
            return false;
        }
        foreach (RuntimeEntry entry in entries.Values)
        {
            if (entry?.record == null
                || entry.record.cityDraftId != currentDraftId)
            {
                continue;
            }
            entry.record.claimedCityId = cityId;
            entry.record.claimedDistrictId = districtId;
        }
        BeginNewDraft();
        DraftChanged?.Invoke();
        return true;
    }

    public GalaxyDroppedObjectSaveEntry[] CaptureSnapshots()
    {
        entryBuffer.Clear();
        foreach (RuntimeEntry entry in entries.Values)
        {
            Snapshot(entry);
            entryBuffer.Add(entry);
        }
        entryBuffer.Sort((left, right) =>
            string.CompareOrdinal(
                left.record.objectId,
                right.record.objectId));

        var result =
            new GalaxyDroppedObjectSaveEntry[entryBuffer.Count];
        for (int i = 0; i < entryBuffer.Count; i++)
            result[i] = Clone(entryBuffer[i].record);
        return result;
    }

    void FixedUpdate()
    {
        if (!configured)
            return;

        activeIdBuffer.Clear();
        foreach (string id in activeIds)
            activeIdBuffer.Add(id);
        for (int i = 0; i < activeIdBuffer.Count; i++)
        {
            string id = activeIdBuffer[i];
            if (!entries.TryGetValue(id, out RuntimeEntry entry)
                || entry.runtimeBody == null)
            {
                activeIds.Remove(id);
                continue;
            }
            Vector2Int previousChunk = entry.chunk;
            Snapshot(entry);
            if (entry.chunk != previousChunk)
            {
                MoveChunk(entry, previousChunk);
                if (!activeChunks.Contains(entry.chunk))
                    Despawn(entry);
            }
            if (entry.runtimeBody != null
                && !entry.record.cityBoundaryLocked)
            {
                UpdateBoundarySettlement(entry);
            }
        }
    }

    void RestoreRecords(GalaxyDroppedObjectSaveEntry[] restored)
    {
        if (restored == null)
            return;
        for (int i = 0; i < restored.Length; i++)
        {
            GalaxyDroppedObjectSaveEntry source = restored[i];
            if (source == null)
                continue;
            string id = string.IsNullOrWhiteSpace(source.objectId)
                ? Guid.NewGuid().ToString("N")
                : source.objectId;
            if (entries.ContainsKey(id))
                continue;

            GalaxyDroppedObjectSaveEntry record = Clone(source);
            record.objectId = id;
            if (string.IsNullOrWhiteSpace(record.payloadTypeId))
                record.payloadTypeId = PlaceholderCubeTypeId;
            Normalize(record);
            var entry = new RuntimeEntry
            {
                record = record,
                chunk = terrain.GetChunkCoordinate(
                    record.planarX,
                    record.planarZ)
            };
            entries.Add(id, entry);
            AddToChunk(entry);
        }
    }

    void HandleChunkActivated(Vector2Int coordinate)
    {
        activeChunks.Add(coordinate);
        if (!chunkEntries.TryGetValue(
                coordinate,
                out HashSet<string> ids))
        {
            return;
        }

        string[] snapshot = new string[ids.Count];
        ids.CopyTo(snapshot);
        for (int i = 0; i < snapshot.Length; i++)
        {
            if (entries.TryGetValue(
                    snapshot[i],
                    out RuntimeEntry entry))
            {
                Spawn(entry);
            }
        }
    }

    void HandleChunkRecycled(Vector2Int coordinate)
    {
        activeChunks.Remove(coordinate);
        if (!chunkEntries.TryGetValue(
                coordinate,
                out HashSet<string> ids))
        {
            return;
        }

        string[] snapshot = new string[ids.Count];
        ids.CopyTo(snapshot);
        for (int i = 0; i < snapshot.Length; i++)
        {
            if (entries.TryGetValue(
                    snapshot[i],
                    out RuntimeEntry entry))
            {
                Despawn(entry);
            }
        }
    }

    void Spawn(RuntimeEntry entry)
    {
        if (entry == null
            || entry.runtimeBody != null
            || !activeChunks.Contains(entry.chunk)
            || entry.record.payloadTypeId
                != PlaceholderCubeTypeId)
        {
            return;
        }

        EnsureMaterials();
        GameObject cube =
            GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "DroppedWhiteCube";
        cube.transform.SetParent(transform, true);
        cube.transform.position = world.FromPersistentAddress(
            new PlanarSurfaceAddress(
                entry.record.planarX,
                entry.record.planarZ,
                entry.record.planarY));
        cube.transform.rotation = entry.record.rotation;
        cube.transform.localScale =
            Vector3.one * PlaceholderCubeSize;
        cube.AddComponent<PlanetFloatingOriginParticipant>();

        Renderer renderer = cube.GetComponent<Renderer>();
        if (renderer != null)
            renderer.sharedMaterial = whiteMaterial;
        Collider collider = cube.GetComponent<Collider>();
        if (collider != null)
            collider.sharedMaterial = physicsMaterial;

        Rigidbody body = cube.AddComponent<Rigidbody>();
        body.mass = PlaceholderCubeMass;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode =
            CollisionDetectionMode.ContinuousDynamic;
        body.drag = 0f;
        body.angularDrag = 0.05f;

        PlanetaryDroppedBody droppedBody =
            cube.AddComponent<PlanetaryDroppedBody>();
        droppedBody.Configure(
            body,
            world,
            celestial,
            PlaceholderCubeSize,
            PlaceholderCubeDragCoefficient);
        body.isKinematic = entry.record.cityBoundaryLocked;
        if (!body.isKinematic)
        {
            body.velocity = entry.record.velocity;
            body.angularVelocity = entry.record.angularVelocity;
        }
        if (entry.record.cityBoundaryLocked)
        {
            entry.record.sleeping = true;
        }
        else if (entry.record.sleeping)
            body.Sleep();
        else
            body.WakeUp();
        entry.runtimeBody = droppedBody;
        activeIds.Add(entry.record.objectId);
    }

    void Snapshot(RuntimeEntry entry)
    {
        if (entry?.runtimeBody == null)
            return;
        Rigidbody body = entry.runtimeBody.Body;
        if (body == null)
            return;
        PlanarSurfaceAddress address =
            world.ToPersistentAddress(body.position);
        entry.record.planarX = address.x;
        entry.record.planarZ = address.z;
        entry.record.planarY = address.y;
        entry.record.rotation = body.rotation;
        entry.record.velocity = body.isKinematic
            ? Vector3.zero
            : body.velocity;
        entry.record.angularVelocity = body.isKinematic
            ? Vector3.zero
            : body.angularVelocity;
        entry.record.sleeping =
            body.isKinematic || body.IsSleeping();
        entry.chunk = terrain.GetChunkCoordinate(
            address.x,
            address.z);
    }

    void Despawn(RuntimeEntry entry)
    {
        if (entry?.runtimeBody == null)
            return;
        Vector2Int previousChunk = entry.chunk;
        Snapshot(entry);
        if (entry.chunk != previousChunk)
            MoveChunk(entry, previousChunk);
        if (activeChunks.Contains(entry.chunk))
            return;
        GameObject instance = entry.runtimeBody.gameObject;
        entry.runtimeBody = null;
        activeIds.Remove(entry.record.objectId);
        instance.SetActive(false);
        Destroy(instance);
    }

    void AddToChunk(RuntimeEntry entry)
    {
        if (!chunkEntries.TryGetValue(
                entry.chunk,
                out HashSet<string> ids))
        {
            ids = new HashSet<string>(StringComparer.Ordinal);
            chunkEntries.Add(entry.chunk, ids);
        }
        ids.Add(entry.record.objectId);
    }

    void MoveChunk(
        RuntimeEntry entry,
        Vector2Int previousChunk)
    {
        if (chunkEntries.TryGetValue(
                previousChunk,
                out HashSet<string> previousIds))
        {
            previousIds.Remove(entry.record.objectId);
            if (previousIds.Count == 0)
                chunkEntries.Remove(previousChunk);
        }
        AddToChunk(entry);
    }

    void UpdateBoundarySettlement(RuntimeEntry entry)
    {
        Rigidbody body = entry.runtimeBody.Body;
        if (body == null || body.isKinematic)
            return;

        bool slow = body.IsSleeping()
            || (body.velocity.sqrMagnitude <= 0.08f * 0.08f
                && body.angularVelocity.sqrMagnitude
                    <= 0.12f * 0.12f);
        bool supported = false;
        Vector3 center = body.worldCenterOfMass;
        if (world.TryProjectToSurface(
                center,
                out PlanetSurfaceSample surface))
        {
            Vector3 up = world.GetUp(center);
            supported = Vector3.Dot(
                    center - surface.point,
                    up)
                <= PlaceholderCubeSize * 0.75f;
        }
        if (!supported
            && PlanetWaterRegistry.TrySampleAny(
                center,
                out WaterSample water))
        {
            supported = Mathf.Abs(water.signedDistance)
                <= PlaceholderCubeSize * 0.75f;
        }

        entry.settledDuration = slow && supported
            ? entry.settledDuration + Time.fixedDeltaTime
            : 0f;
        if (entry.settledDuration < 1.5f)
            return;

        Snapshot(entry);
        entry.record.cityBoundaryLocked = true;
        entry.record.velocity = Vector3.zero;
        entry.record.angularVelocity = Vector3.zero;
        entry.record.sleeping = true;
        body.velocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.isKinematic = true;
        BoundaryLocked?.Invoke(Clone(entry.record));
        DraftChanged?.Invoke();
    }

    int CountCurrentDraft(bool locked)
    {
        int count = 0;
        foreach (RuntimeEntry entry in entries.Values)
        {
            GalaxyDroppedObjectSaveEntry record =
                entry?.record;
            if (record != null
                && record.cityDraftId == currentDraftId
                && string.IsNullOrEmpty(record.claimedCityId)
                && record.cityBoundaryLocked == locked)
            {
                count++;
            }
        }
        return count;
    }

    void ResolveCurrentDraft()
    {
        currentDraftId = null;
        nextBoundaryOrder = 0;
        foreach (RuntimeEntry entry in entries.Values)
        {
            GalaxyDroppedObjectSaveEntry record =
                entry?.record;
            if (record == null
                || !string.IsNullOrEmpty(record.claimedCityId)
                || string.IsNullOrWhiteSpace(record.cityDraftId))
            {
                continue;
            }
            if (currentDraftId == null)
                currentDraftId = record.cityDraftId;
            if (record.cityDraftId == currentDraftId)
            {
                nextBoundaryOrder = Mathf.Max(
                    nextBoundaryOrder,
                    record.cityBoundaryOrder + 1);
            }
        }
        if (string.IsNullOrWhiteSpace(currentDraftId))
            BeginNewDraft();
    }

    void BeginNewDraft()
    {
        currentDraftId = Guid.NewGuid().ToString("N");
        nextBoundaryOrder = 0;
    }

    void RemoveEntry(RuntimeEntry entry)
    {
        if (entry == null || entry.record == null)
            return;
        string id = entry.record.objectId;
        if (entry.runtimeBody != null)
        {
            GameObject instance =
                entry.runtimeBody.gameObject;
            entry.runtimeBody = null;
            activeIds.Remove(id);
            Destroy(instance);
        }
        if (chunkEntries.TryGetValue(
                entry.chunk,
                out HashSet<string> ids))
        {
            ids.Remove(id);
            if (ids.Count == 0)
                chunkEntries.Remove(entry.chunk);
        }
        entries.Remove(id);
    }

    void EnsureMaterials()
    {
        if (whiteMaterial == null)
        {
            Shader shader =
                Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            whiteMaterial = new Material(shader)
            {
                name = "DroppedWhiteCubeMaterial",
                color = Color.white
            };
            if (whiteMaterial.HasProperty("_BaseColor"))
            {
                whiteMaterial.SetColor(
                    "_BaseColor",
                    Color.white);
            }
        }
        if (physicsMaterial == null)
        {
            physicsMaterial =
                new PhysicMaterial("DroppedWhiteCubePhysics")
                {
                    dynamicFriction = 0.6f,
                    staticFriction = 0.6f,
                    bounciness = 0.05f,
                    frictionCombine =
                        PhysicMaterialCombine.Average,
                    bounceCombine =
                        PhysicMaterialCombine.Average
                };
        }
    }

    static float CalculateShipSupport(
        Vector3 center,
        Vector3 direction,
        Collider[] colliders)
    {
        float support = 1f;
        if (colliders == null)
            return support;
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null
                || !collider.enabled
                || !collider.gameObject.activeInHierarchy)
            {
                continue;
            }
            Bounds bounds = collider.bounds;
            Vector3 extents = bounds.extents;
            float projectedExtent =
                Mathf.Abs(direction.x) * extents.x
                + Mathf.Abs(direction.y) * extents.y
                + Mathf.Abs(direction.z) * extents.z;
            float projectedCenter =
                Vector3.Dot(bounds.center - center, direction);
            support = Mathf.Max(
                support,
                projectedCenter + projectedExtent);
        }
        return support;
    }

    public static Vector3 CalculateReleasePoint(
        Rigidbody shipBody,
        Collider[] shipColliders,
        Vector3 surfaceUp,
        float cubeSize)
    {
        if (shipBody == null)
            return Vector3.zero;
        if (surfaceUp.sqrMagnitude <= 0.0001f)
            surfaceUp = Vector3.up;
        surfaceUp.Normalize();
        Vector3 down = -surfaceUp;
        Vector3 center = shipBody.worldCenterOfMass;
        float shipSupport = CalculateShipSupport(
            center,
            down,
            shipColliders);
        float cubeSupport =
            PlanetaryDroppedBody.CalculateProjectedHalfExtent(
                shipBody.rotation,
                down,
                cubeSize);
        return center
            + down * (shipSupport + cubeSupport + 0.15f);
    }

    static GalaxyDroppedObjectSaveEntry Clone(
        GalaxyDroppedObjectSaveEntry value)
    {
        return new GalaxyDroppedObjectSaveEntry
        {
            objectId = value.objectId,
            payloadTypeId = value.payloadTypeId,
            planarX = value.planarX,
            planarZ = value.planarZ,
            planarY = value.planarY,
            rotation = value.rotation,
            velocity = value.velocity,
            angularVelocity = value.angularVelocity,
            sleeping = value.sleeping,
            cityDraftId = value.cityDraftId,
            cityBoundaryOrder = value.cityBoundaryOrder,
            cityBoundaryLocked = value.cityBoundaryLocked,
            claimedCityId = value.claimedCityId,
            claimedDistrictId = value.claimedDistrictId
        };
    }

    static void Normalize(GalaxyDroppedObjectSaveEntry record)
    {
        if (double.IsNaN(record.planarX)
            || double.IsInfinity(record.planarX))
        {
            record.planarX = 0d;
        }
        if (double.IsNaN(record.planarZ)
            || double.IsInfinity(record.planarZ))
        {
            record.planarZ = 0d;
        }
        float quaternionLength =
            record.rotation.x * record.rotation.x
            + record.rotation.y * record.rotation.y
            + record.rotation.z * record.rotation.z
            + record.rotation.w * record.rotation.w;
        if (quaternionLength <= 0.0001f)
            record.rotation = Quaternion.identity;
        else
            record.rotation = Quaternion.Normalize(record.rotation);
        if (!IsFinite(record.velocity))
            record.velocity = Vector3.zero;
        if (!IsFinite(record.angularVelocity))
            record.angularVelocity = Vector3.zero;
        if (float.IsNaN(record.planarY)
            || float.IsInfinity(record.planarY))
        {
            record.planarY = 0f;
        }
    }

    static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x)
            && !float.IsInfinity(value.x)
            && !float.IsNaN(value.y)
            && !float.IsInfinity(value.y)
            && !float.IsNaN(value.z)
            && !float.IsInfinity(value.z);
    }

    void OnDestroy()
    {
        if (terrain != null)
        {
            terrain.ChunkActivated -= HandleChunkActivated;
            terrain.ChunkRecycled -= HandleChunkRecycled;
        }
        DestroyTransient(whiteMaterial);
        DestroyTransient(physicsMaterial);
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
