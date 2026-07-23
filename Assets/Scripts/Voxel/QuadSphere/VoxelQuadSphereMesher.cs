using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;

public static class VoxelQuadSphereMesher
{
    public const int PaddedChunkSize = VoxelTypes.ChunkSize + 2;

    static readonly ConcurrentBag<List<Vector3>> VectorListPool = new ConcurrentBag<List<Vector3>>();
    static readonly ConcurrentBag<List<int>> IndexListPool = new ConcurrentBag<List<int>>();

    // 邻居方向（U/V/Depth）→ 本地立方体面索引
    static readonly int[] NeighborToUnitFace = { 0, 1, 4, 5, 3, 2 };

    // Corner bits are U, radial and V. Adjacent voxels calculate the same
    // spherical boundary points instead of overlapping independent boxes.
    static readonly int[,] FaceCornerIndices =
    {
        { 4, 6, 7, 5 },
        { 1, 3, 2, 0 },
        { 3, 7, 6, 2 },
        { 0, 4, 5, 1 },
        { 5, 7, 3, 1 },
        { 0, 2, 6, 4 }
    };

    public static Mesh BuildChunkMesh(
        Func<QuadSphereVoxelAddress, byte> getVoxel,
        QuadSphereChunkKey chunkKey,
        int gridSize,
        int maxDepth,
        float planetRadius,
        Vector3 planetCenter)
    {
        List<Vector3> dirtVertices = new List<Vector3>();
        List<int> dirtTriangles = new List<int>();
        List<Vector3> stoneVertices = new List<Vector3>();
        List<int> stoneTriangles = new List<int>();
        Vector3[] corners = new Vector3[8];

        int originU = chunkKey.ChunkU * VoxelTypes.ChunkSize;
        int originV = chunkKey.ChunkV * VoxelTypes.ChunkSize;
        int originDepth = chunkKey.ChunkDepth * VoxelTypes.ChunkSize;

        for (int z = 0; z < VoxelTypes.ChunkSize; z++)
        {
            for (int y = 0; y < VoxelTypes.ChunkSize; y++)
            {
                for (int x = 0; x < VoxelTypes.ChunkSize; x++)
                {
                    int cellU = originU + x;
                    int cellV = originV + y;
                    int depth = originDepth + z;
                    var address = new QuadSphereVoxelAddress(chunkKey.Face, cellU, cellV, depth);

                    byte voxel = getVoxel(address);
                    if (!VoxelTypes.IsSolid(voxel))
                        continue;

                    List<Vector3> vertices = voxel == VoxelTypes.Stone ? stoneVertices : dirtVertices;
                    List<int> triangles = voxel == VoxelTypes.Stone ? stoneTriangles : dirtTriangles;

                    FillCellCorners(
                        corners, chunkKey.Face, cellU, cellV, depth,
                        gridSize, planetRadius, planetCenter);

                    for (int neighborFace = 0; neighborFace < 6; neighborFace++)
                    {
                        var neighborAddress = GetNeighborAddress(address, neighborFace, gridSize);
                        byte neighbor = getVoxel(neighborAddress);
                        if (VoxelTypes.IsSolid(neighbor))
                            continue;

                        int unitFace = NeighborToUnitFace[neighborFace];
                        AddFace(vertices, triangles, corners, unitFace);
                    }
                }
            }
        }

        return BuildCombinedMesh(chunkKey, dirtVertices, dirtTriangles, stoneVertices, stoneTriangles);}

    public static VoxelQuadSphereMeshData BuildChunkMeshDataFromPaddedVoxels(
        byte[] paddedVoxels,
        QuadSphereChunkKey chunkKey,
        int gridSize,
        float planetRadius,
        Vector3 planetCenter)
    {
        int paddedSize = PaddedChunkSize;
        int requiredLength = paddedSize * paddedSize * paddedSize;
        if (paddedVoxels == null || paddedVoxels.Length != requiredLength)
            throw new ArgumentException("Padded voxel data has an invalid size.", nameof(paddedVoxels));

        List<Vector3> dirtVertices = RentVectorList(2048);
        List<int> dirtTriangles = RentIndexList(3072);
        List<Vector3> stoneVertices = RentVectorList(2048);
        List<int> stoneTriangles = RentIndexList(3072);
        var corners = new Vector3[8];
        bool ownershipTransferred = false;

        try
        {
            int originU = chunkKey.ChunkU * VoxelTypes.ChunkSize;
            int originV = chunkKey.ChunkV * VoxelTypes.ChunkSize;
            int originDepth = chunkKey.ChunkDepth * VoxelTypes.ChunkSize;

            for (int z = 0; z < VoxelTypes.ChunkSize; z++)
            {
                for (int y = 0; y < VoxelTypes.ChunkSize; y++)
                {
                    for (int x = 0; x < VoxelTypes.ChunkSize; x++)
                    {
                        byte voxel = GetPaddedVoxel(paddedVoxels, x + 1, y + 1, z + 1, paddedSize);
                        if (!VoxelTypes.IsSolid(voxel))
                            continue;

                        List<Vector3> vertices = voxel == VoxelTypes.Stone ? stoneVertices : dirtVertices;
                        List<int> triangles = voxel == VoxelTypes.Stone ? stoneTriangles : dirtTriangles;
                        FillCellCorners(
                            corners,
                            chunkKey.Face,
                            originU + x,
                            originV + y,
                            originDepth + z,
                            gridSize,
                            planetRadius,
                            planetCenter);

                        for (int neighborFace = 0; neighborFace < 6; neighborFace++)
                        {
                            Vector3Int offset = NeighborFaceOffset(neighborFace);
                            byte neighbor = GetPaddedVoxel(
                                paddedVoxels,
                                x + 1 + offset.x,
                                y + 1 + offset.y,
                                z + 1 + offset.z,
                                paddedSize);
                            if (VoxelTypes.IsSolid(neighbor))
                                continue;

                            AddFace(vertices, triangles, corners, NeighborToUnitFace[neighborFace]);
                        }
                    }
                }
            }

            ownershipTransferred = true;
            return BuildMeshData(dirtVertices, dirtTriangles, stoneVertices, stoneTriangles);
        }
        catch
        {
            if (!ownershipTransferred)
            {
                ReturnVectorList(dirtVertices);
                ReturnIndexList(dirtTriangles);
                ReturnVectorList(stoneVertices);
                ReturnIndexList(stoneTriangles);
            }
            throw;
        }
    }

    static byte GetPaddedVoxel(byte[] voxels, int x, int y, int z, int size)
    {
        return voxels[x + y * size + z * size * size];
    }

    static VoxelQuadSphereMeshData BuildMeshData(
        List<Vector3> dirtVertices,
        List<int> dirtTriangles,
        List<Vector3> stoneVertices,
        List<int> stoneTriangles)
    {
        int dirtCount = dirtVertices.Count;
        int totalCount = dirtCount + stoneVertices.Count;
        if (totalCount == 0)
        {
            ReturnVectorList(dirtVertices);
            ReturnIndexList(dirtTriangles);
            ReturnVectorList(stoneVertices);
            ReturnIndexList(stoneTriangles);
            return VoxelQuadSphereMeshData.Empty;
        }

        List<Vector3> vertices = RentVectorList(totalCount);
        vertices.AddRange(dirtVertices);
        vertices.AddRange(stoneVertices);
        ReturnVectorList(dirtVertices);
        ReturnVectorList(stoneVertices);

        List<int>[] subMeshes;
        if (stoneTriangles.Count == 0)
        {
            ReturnIndexList(stoneTriangles);
            subMeshes = new[] { dirtTriangles };
        }
        else if (dirtCount == 0)
        {
            ReturnIndexList(dirtTriangles);
            subMeshes = new[] { stoneTriangles };
        }
        else
        {
            for (int i = 0; i < stoneTriangles.Count; i++)
                stoneTriangles[i] += dirtCount;
            subMeshes = new[] { dirtTriangles, stoneTriangles };
        }

        List<Vector3> normals = RentVectorList(totalCount);
        for (int i = 0; i < totalCount; i++)
            normals.Add(Vector3.zero);
        for (int subMesh = 0; subMesh < subMeshes.Length; subMesh++)
        {
            List<int> triangles = subMeshes[subMesh];
            for (int i = 0; i + 2 < triangles.Count; i += 3)
            {
                int a = triangles[i];
                int b = triangles[i + 1];
                int c = triangles[i + 2];
                Vector3 normal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                normals[a] += normal;
                normals[b] += normal;
                normals[c] += normal;
            }
        }
        for (int i = 0; i < normals.Count; i++)
            normals[i] = normals[i].sqrMagnitude > 0.000001f ? normals[i].normalized : Vector3.up;

        return new VoxelQuadSphereMeshData(vertices, normals, subMeshes, ReleaseMeshData);
    }

    static List<Vector3> RentVectorList(int capacity)
    {
        if (!VectorListPool.TryTake(out List<Vector3> list))
            return new List<Vector3>(capacity);
        list.Clear();
        if (list.Capacity < capacity)
            list.Capacity = capacity;
        return list;
    }

    static List<int> RentIndexList(int capacity)
    {
        if (!IndexListPool.TryTake(out List<int> list))
            return new List<int>(capacity);
        list.Clear();
        if (list.Capacity < capacity)
            list.Capacity = capacity;
        return list;
    }

    static void ReleaseMeshData(VoxelQuadSphereMeshData data)
    {
        ReturnVectorList(data.Vertices);
        ReturnVectorList(data.Normals);
        for (int i = 0; i < data.SubMeshTriangles.Length; i++)
            ReturnIndexList(data.SubMeshTriangles[i]);
    }

    static void ReturnVectorList(List<Vector3> list)
    {
        if (list == null)
            return;
        list.Clear();
        VectorListPool.Add(list);
    }

    static void ReturnIndexList(List<int> list)
    {
        if (list == null)
            return;
        list.Clear();
        IndexListPool.Add(list);
    }

    static QuadSphereVoxelAddress GetNeighborAddress(
        QuadSphereVoxelAddress address,
        int neighborFace,
        int gridSize)
    {
        Vector3Int offset = NeighborFaceOffset(neighborFace);
        var neighbor = new QuadSphereVoxelAddress(
            address.Face,
            address.U + offset.x,
            address.V + offset.y,
            address.Depth + offset.z
        );
        return VoxelQuadSphereMapping.RemapAcrossFace(neighbor, gridSize);
    }

    static Vector3Int NeighborFaceOffset(int neighborFace)
    {
        switch (neighborFace)
        {
            case 0: return new Vector3Int(1, 0, 0);   // +U
            case 1: return new Vector3Int(-1, 0, 0);  // -U
            case 2: return new Vector3Int(0, 1, 0);   // +V
            case 3: return new Vector3Int(0, -1, 0);  // -V
            case 4: return new Vector3Int(0, 0, 1);  // +Depth（向球心）
            default: return new Vector3Int(0, 0, -1); // -Depth（向太空）
        }
    }

    static void FillCellCorners(
        Vector3[] corners,
        QuadSphereFace face,
        int cellU,
        int cellV,
        int depth,
        int gridSize,
        float planetRadius,
        Vector3 planetCenter)
    {
        float uMin = CellBoundaryToNormalized(cellU, gridSize);
        float uMax = CellBoundaryToNormalized(cellU + 1, gridSize);
        float vMin = CellBoundaryToNormalized(cellV, gridSize);
        float vMax = CellBoundaryToNormalized(cellV + 1, gridSize);
        float innerRadius = planetRadius - depth - 1f;
        float outerRadius = planetRadius - depth;

        for (int uBit = 0; uBit <= 1; uBit++)
        {
            float u = uBit == 0 ? uMin : uMax;
            for (int radialBit = 0; radialBit <= 1; radialBit++)
            {
                float radius = radialBit == 0 ? innerRadius : outerRadius;
                for (int vBit = 0; vBit <= 1; vBit++)
                {
                    float v = vBit == 0 ? vMin : vMax;
                    int index = (uBit << 2) | (radialBit << 1) | vBit;
                    Vector3 cubePoint = VoxelQuadSphereMapping.GetFaceCubePoint(face, u, v);
                    Vector3 radial = VoxelQuadSphereMapping.CubeToSphere(cubePoint).normalized;
                    corners[index] = planetCenter + radial * radius;
                }
            }
        }

    }

    static float CellBoundaryToNormalized(int boundary, int gridSize)
    {
        return boundary / (float)gridSize * 2f - 1f;
    }

    static void AddFace(
        List<Vector3> vertices,
        List<int> triangles,
        Vector3[] corners,
        int unitFaceIndex)
    {
        int start = vertices.Count;
        for (int i = 0; i < 4; i++)
            vertices.Add(corners[FaceCornerIndices[unitFaceIndex, i]]);

        triangles.Add(start + 0);
        triangles.Add(start + 1);
        triangles.Add(start + 2);
        triangles.Add(start + 0);
        triangles.Add(start + 2);
        triangles.Add(start + 3);
    }

    static Mesh BuildCombinedMesh(
        QuadSphereChunkKey chunkKey,
        List<Vector3> dirtVertices,
        List<int> dirtTriangles,
        List<Vector3> stoneVertices,
        List<int> stoneTriangles)
    {
        Mesh mesh = new Mesh
        {
            name = $"QuadSphereChunk_{chunkKey.Face}_{chunkKey.ChunkU}_{chunkKey.ChunkV}_{chunkKey.ChunkDepth}"
        };

        int totalVertexCount = dirtVertices.Count + stoneVertices.Count;
        if (totalVertexCount > ushort.MaxValue)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        if (dirtVertices.Count == 0 && stoneVertices.Count == 0)
            return mesh;

        if (stoneVertices.Count == 0)
        {
            mesh.SetVertices(dirtVertices);
            mesh.SetTriangles(dirtTriangles, 0);
        }
        else if (dirtVertices.Count == 0)
        {
            mesh.SetVertices(stoneVertices);
            mesh.SetTriangles(stoneTriangles, 0);
        }
        else
        {
            List<Vector3> allVertices = new List<Vector3>(dirtVertices.Count + stoneVertices.Count);
            allVertices.AddRange(dirtVertices);
            allVertices.AddRange(stoneVertices);

            List<int> stoneOffsetTriangles = new List<int>(stoneTriangles.Count);
            for (int i = 0; i < stoneTriangles.Count; i++)
                stoneOffsetTriangles.Add(stoneTriangles[i] + dirtVertices.Count);

            mesh.subMeshCount = 2;
            mesh.SetVertices(allVertices);
            mesh.SetTriangles(dirtTriangles, 0);
            mesh.SetTriangles(stoneOffsetTriangles, 1);
        }

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
