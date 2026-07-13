using System;
using System.Collections.Generic;
using UnityEngine;

public static class VoxelQuadSphereMesher
{
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
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(109, (int)gridSize, (int)maxDepth, (int)planetRadius);
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

        return BuildCombinedMesh(chunkKey, dirtVertices, dirtTriangles, stoneVertices, stoneTriangles);
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
