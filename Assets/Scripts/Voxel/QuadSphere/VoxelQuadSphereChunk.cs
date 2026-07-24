using UnityEngine;

public class VoxelQuadSphereChunk
{
    public QuadSphereChunkKey Key { get; }
    public byte[] Voxels { get; }
    public bool IsDirty { get; private set; }
    public bool IsModified { get; private set; }
    public bool HasMesh => runtimeMesh != null;
    public int MeshVertexCount => runtimeMesh != null ? runtimeMesh.vertexCount : 0;
    public bool HasCollider => meshCollider != null && meshCollider.enabled && meshCollider.sharedMesh != null;
    public bool HasBakedCollider => HasCollider && bakedColliderRevision == MeshRevision;
    public bool IsCollisionReady => runtimeMesh != null && (runtimeMesh.vertexCount == 0 || HasBakedCollider);
    public Collider Collider => meshCollider;
    public int MeshRevision { get; private set; }
    public int DataRevision { get; private set; }

    readonly GameObject viewObject;
    readonly MeshFilter meshFilter;
    readonly MeshCollider meshCollider;
    Mesh runtimeMesh;
    Vector3[] surfaceRaycastVertices;
    int[] surfaceRaycastTriangles;
    int bakedColliderRevision = -1;
    int reportedColliderFailureRevision = -1;

    public VoxelQuadSphereChunk(QuadSphereChunkKey key, Transform parent, Material dirtMaterial, Material stoneMaterial)
    {
        Key = key;
        Voxels = new byte[VoxelTypes.ChunkSize * VoxelTypes.ChunkSize * VoxelTypes.ChunkSize];

        viewObject = new GameObject($"QSChunk_{key.Face}_{key.ChunkU}_{key.ChunkV}_{key.ChunkDepth}");
        // Thousands of runtime chunk objects changing every frame can corrupt the
        // Unity 2022 Hierarchy tree state and add substantial editor-only overhead.
        viewObject.hideFlags = HideFlags.HideInHierarchy;
        viewObject.transform.SetParent(parent, false);
        viewObject.transform.localPosition = Vector3.zero;

        meshFilter = viewObject.AddComponent<MeshFilter>();
        MeshRenderer renderer = viewObject.AddComponent<MeshRenderer>();
        meshCollider = viewObject.AddComponent<MeshCollider>();
        meshCollider.cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation
            | MeshColliderCookingOptions.EnableMeshCleaning
            | MeshColliderCookingOptions.WeldColocatedVertices
            | MeshColliderCookingOptions.UseFastMidphase;

        if (stoneMaterial != null)
            renderer.sharedMaterials = new[] { dirtMaterial, stoneMaterial };
        else
            renderer.sharedMaterial = dirtMaterial;
    }

    public byte GetLocalVoxel(int x, int y, int z)
    {
        return Voxels[VoxelTypes.ToIndex(x, y, z)];}

    public void SetLocalVoxel(int x, int y, int z, byte value)
    {
        if (x < 0 || y < 0 || z < 0 || x >= VoxelTypes.ChunkSize || y >= VoxelTypes.ChunkSize || z >= VoxelTypes.ChunkSize)
            return;

        int index = VoxelTypes.ToIndex(x, y, z);
        if (Voxels[index] == value)
            return;

        Voxels[index] = value;
        IsDirty = true;
        DataRevision++;
        IsModified = true;}

    public void MarkDirty()
    {
        IsDirty = true;
        DataRevision++;
    }
    public void MarkModified() => IsModified = true;
    public void ClearModifiedFlag() => IsModified = false;

    public void RebuildMesh(System.Func<QuadSphereVoxelAddress, byte> getVoxel, int gridSize, int maxDepth, float planetRadius, Vector3 planetCenter)
    {
        if (runtimeMesh != null)
            Object.Destroy(runtimeMesh);

        runtimeMesh = VoxelQuadSphereMesher.BuildChunkMesh(getVoxel, Key, gridSize, maxDepth, planetRadius, planetCenter);
        meshFilter.sharedMesh = runtimeMesh;
        meshCollider.sharedMesh = null;
        MeshRevision++;
        meshCollider.enabled = runtimeMesh != null && runtimeMesh.vertexCount > 0;
        if (meshCollider.enabled)
            meshCollider.sharedMesh = runtimeMesh;
        CacheSurfaceRaycastGeometry();
        // Assigning sharedMesh already performs Unity's synchronous collider cook.
        // Record that revision so EnsureColliderReady does not immediately bake
        // and assign the exact same mesh a second time.
        bakedColliderRevision = HasCollider ? MeshRevision : -1;
        reportedColliderFailureRevision = -1;
        IsDirty = false;}

    public int ApplyMeshData(VoxelQuadSphereMeshData data, bool enableCollider)
    {
        data = data ?? VoxelQuadSphereMeshData.Empty;
        if (runtimeMesh == null)
        {
            runtimeMesh = new Mesh
            {
                name = $"QuadSphereChunk_{Key.Face}_{Key.ChunkU}_{Key.ChunkV}_{Key.ChunkDepth}"
            };
            runtimeMesh.MarkDynamic();
        }
        else
        {
            runtimeMesh.Clear(false);
        }

        runtimeMesh.indexFormat = data.Vertices.Count > ushort.MaxValue
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        runtimeMesh.SetVertices(data.Vertices);
        if (data.Normals.Count == data.Vertices.Count)
            runtimeMesh.SetNormals(data.Normals);
        runtimeMesh.subMeshCount = data.SubMeshTriangles.Length;
        for (int subMesh = 0; subMesh < data.SubMeshTriangles.Length; subMesh++)
            runtimeMesh.SetTriangles(data.SubMeshTriangles[subMesh], subMesh, false);
        runtimeMesh.RecalculateBounds();
        meshFilter.sharedMesh = runtimeMesh;
        if (enableCollider)
            CacheSurfaceRaycastGeometry();
        else
        {
            surfaceRaycastVertices = null;
            surfaceRaycastTriangles = null;
        }

        meshCollider.sharedMesh = null;
        MeshRevision++;
        meshCollider.enabled = enableCollider && !data.IsEmpty;
        if (meshCollider.enabled)
            meshCollider.sharedMesh = runtimeMesh;

        bakedColliderRevision = HasCollider ? MeshRevision : -1;
        reportedColliderFailureRevision = -1;
        IsDirty = false;
        return MeshRevision;
    }

    public bool EnsureColliderReady()
    {
        if (runtimeMesh == null || runtimeMesh.vertexCount == 0)
            return false;
        if (HasBakedCollider)
            return true;
        return BakeAndApplyCollider(MeshRevision);
    }

    public bool BakeAndApplyCollider(int revision)
    {
        if (runtimeMesh == null || revision != MeshRevision || runtimeMesh.vertexCount == 0)
            return false;
        if (HasBakedCollider)
            return true;

        try
        {
            Physics.BakeMesh(runtimeMesh.GetInstanceID(), false, meshCollider.cookingOptions);
            meshCollider.sharedMesh = null;
            meshCollider.enabled = true;
            meshCollider.sharedMesh = runtimeMesh;
            if (!HasCollider)
                return false;

            CacheSurfaceRaycastGeometry();
            bakedColliderRevision = revision;
            return true;
        }
        catch (System.Exception exception)
        {
            meshCollider.sharedMesh = null;
            meshCollider.enabled = false;
            if (reportedColliderFailureRevision != revision)
            {
                reportedColliderFailureRevision = revision;
                Debug.LogError(
                    $"VoxelQuadSphereChunk: failed to bake terrain collider for " +
                    $"{Key.Face}:{Key.ChunkU}:{Key.ChunkV}:{Key.ChunkDepth}. " +
                    exception.Message,
                    viewObject);
            }
            return false;
        }
    }

    public bool TryRaycastSurface(
        Ray worldRay,
        float maxDistance,
        out Vector3 point,
        out Vector3 normal,
        out float distance)
    {
        point = default;
        normal = default;
        distance = float.PositiveInfinity;
        if (!HasBakedCollider || maxDistance <= 0f || viewObject == null)
            return false;

        if (surfaceRaycastVertices == null || surfaceRaycastTriangles == null)
            CacheSurfaceRaycastGeometry();
        if (surfaceRaycastVertices == null
            || surfaceRaycastTriangles == null
            || surfaceRaycastTriangles.Length < 3)
        {
            return false;
        }

        Vector3 worldDirection = worldRay.direction.normalized;
        if (worldDirection.sqrMagnitude < 0.999f)
            return false;

        Transform meshTransform = viewObject.transform;
        Vector3 localOrigin = meshTransform.InverseTransformPoint(worldRay.origin);
        Vector3 localDirection = meshTransform.InverseTransformVector(worldDirection).normalized;
        float nearestWorldDistance = float.PositiveInfinity;
        Vector3 nearestPoint = default;
        Vector3 nearestNormal = default;

        for (int triangle = 0; triangle <= surfaceRaycastTriangles.Length - 3; triangle += 3)
        {
            int indexA = surfaceRaycastTriangles[triangle];
            int indexB = surfaceRaycastTriangles[triangle + 1];
            int indexC = surfaceRaycastTriangles[triangle + 2];
            if ((uint)indexA >= surfaceRaycastVertices.Length
                || (uint)indexB >= surfaceRaycastVertices.Length
                || (uint)indexC >= surfaceRaycastVertices.Length)
            {
                continue;
            }

            Vector3 a = surfaceRaycastVertices[indexA];
            Vector3 b = surfaceRaycastVertices[indexB];
            Vector3 c = surfaceRaycastVertices[indexC];
            if (!TryIntersectTriangle(localOrigin, localDirection, a, b, c, out float localDistance))
                continue;

            Vector3 localPoint = localOrigin + localDirection * localDistance;
            Vector3 worldPoint = meshTransform.TransformPoint(localPoint);
            float worldDistance = Vector3.Dot(worldPoint - worldRay.origin, worldDirection);
            if (worldDistance <= 0.0001f
                || worldDistance > maxDistance
                || worldDistance >= nearestWorldDistance)
            {
                continue;
            }

            Vector3 worldA = meshTransform.TransformPoint(a);
            Vector3 worldB = meshTransform.TransformPoint(b);
            Vector3 worldC = meshTransform.TransformPoint(c);
            Vector3 worldNormal = Vector3.Cross(worldB - worldA, worldC - worldA).normalized;
            if (worldNormal.sqrMagnitude < 0.0001f)
                continue;

            nearestWorldDistance = worldDistance;
            nearestPoint = worldPoint;
            nearestNormal = worldNormal;
        }

        if (!float.IsFinite(nearestWorldDistance))
            return false;

        point = nearestPoint;
        normal = nearestNormal;
        distance = nearestWorldDistance;
        return true;
    }

    void CacheSurfaceRaycastGeometry()
    {
        if (runtimeMesh == null || runtimeMesh.vertexCount == 0)
        {
            surfaceRaycastVertices = null;
            surfaceRaycastTriangles = null;
            return;
        }

        surfaceRaycastVertices = runtimeMesh.vertices;
        surfaceRaycastTriangles = runtimeMesh.triangles;
    }

    static bool TryIntersectTriangle(
        Vector3 origin,
        Vector3 direction,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        out float distance)
    {
        const float epsilon = 0.000001f;
        Vector3 edgeAB = b - a;
        Vector3 edgeAC = c - a;
        Vector3 perpendicular = Vector3.Cross(direction, edgeAC);
        float determinant = Vector3.Dot(edgeAB, perpendicular);
        if (Mathf.Abs(determinant) < epsilon)
        {
            distance = 0f;
            return false;
        }

        float inverseDeterminant = 1f / determinant;
        Vector3 originOffset = origin - a;
        float u = Vector3.Dot(originOffset, perpendicular) * inverseDeterminant;
        if (u < 0f || u > 1f)
        {
            distance = 0f;
            return false;
        }

        Vector3 cross = Vector3.Cross(originOffset, edgeAB);
        float v = Vector3.Dot(direction, cross) * inverseDeterminant;
        if (v < 0f || u + v > 1f)
        {
            distance = 0f;
            return false;
        }

        distance = Vector3.Dot(edgeAC, cross) * inverseDeterminant;
        return distance > epsilon;
    }

    public void AddMeshSnapshot(QuadSphereChunkSaveEntry entry)
    {
        if (entry == null || runtimeMesh == null)
            return;

        entry.meshVertices = runtimeMesh.vertices;
        entry.meshNormals = runtimeMesh.normals;
        entry.meshSubMeshTriangles = new int[runtimeMesh.subMeshCount][];
        for (int subMesh = 0; subMesh < runtimeMesh.subMeshCount; subMesh++)
            entry.meshSubMeshTriangles[subMesh] = runtimeMesh.GetTriangles(subMesh);}

    public bool RestoreMesh(QuadSphereChunkSaveEntry entry)
    {
        if (entry == null
            || entry.meshVertices == null
            || entry.meshNormals == null
            || entry.meshNormals.Length != entry.meshVertices.Length
            || entry.meshSubMeshTriangles == null
            || entry.meshSubMeshTriangles.Length == 0)
        {
            return false;
        }

        if (runtimeMesh != null)
            Object.Destroy(runtimeMesh);

        runtimeMesh = new Mesh
        {
            name = $"QuadSphereChunk_{Key.Face}_{Key.ChunkU}_{Key.ChunkV}_{Key.ChunkDepth}"
        };
        if (entry.meshVertices.Length > ushort.MaxValue)
            runtimeMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        runtimeMesh.vertices = entry.meshVertices;
        runtimeMesh.normals = entry.meshNormals;
        runtimeMesh.subMeshCount = entry.meshSubMeshTriangles.Length;
        for (int subMesh = 0; subMesh < entry.meshSubMeshTriangles.Length; subMesh++)
            runtimeMesh.SetTriangles(entry.meshSubMeshTriangles[subMesh], subMesh, false);
        runtimeMesh.RecalculateBounds();
        CacheSurfaceRaycastGeometry();

        meshFilter.sharedMesh = runtimeMesh;
        meshCollider.sharedMesh = null;
        MeshRevision++;
        meshCollider.enabled = runtimeMesh.vertexCount > 0;
        if (meshCollider.enabled)
            meshCollider.sharedMesh = runtimeMesh;
        bakedColliderRevision = HasCollider ? MeshRevision : -1;
        reportedColliderFailureRevision = -1;
        IsDirty = false;
        return true;}

    public void Destroy()
    {
        if (runtimeMesh != null)
            Object.Destroy(runtimeMesh);
        surfaceRaycastVertices = null;
        surfaceRaycastTriangles = null;

        if (viewObject != null)
            Object.Destroy(viewObject);}
}
