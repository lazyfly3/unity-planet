using System;
using System.Collections.Generic;
using UnityEngine;

public static class VoxelQuadSphereMesher
{
    // 本地轴：+X=U，+Y=径向向外，+Z=V（与 GetCellBasis / LookRotation 一致）
    static readonly Vector3[,] UnitFaceVertices =
    {
        { new Vector3(0.5f,0,0), new Vector3(0.5f,1,0), new Vector3(0.5f,1,1), new Vector3(0.5f,0,1) },
        { new Vector3(-0.5f,0,1), new Vector3(-0.5f,1,1), new Vector3(-0.5f,1,0), new Vector3(-0.5f,0,0) },
        { new Vector3(0,1,1), new Vector3(1,1,1), new Vector3(1,1,0), new Vector3(0,1,0) },
        { new Vector3(0,0,0), new Vector3(1,0,0), new Vector3(1,0,1), new Vector3(0,0,1) },
        { new Vector3(1,0,1), new Vector3(1,1,1), new Vector3(0,1,1), new Vector3(0,0,1) },
        { new Vector3(0,0,0), new Vector3(0,1,0), new Vector3(1,1,0), new Vector3(1,0,0) }
    };

    // 邻居方向（U/V/Depth）→ 本地立方体面索引
    static readonly int[] NeighborToUnitFace = { 0, 1, 4, 5, 3, 2 };

    public static Mesh BuildChunkMesh(
        Func<QuadSphereVoxelAddress, byte> getVoxel,
        QuadSphereChunkKey chunkKey,
        int gridSize,
        float planetRadius,
        Vector3 planetCenter)
    {
        List<Vector3> dirtVertices = new List<Vector3>();
        List<int> dirtTriangles = new List<int>();
        List<Vector3> stoneVertices = new List<Vector3>();
        List<int> stoneTriangles = new List<int>();

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

                    Vector3 center = VoxelQuadSphereMapping.FaceCellCenterLocal(
                        chunkKey.Face, cellU, cellV, depth, gridSize, planetRadius, planetCenter);
                    Quaternion orientation = VoxelQuadSphereMapping.GetCellOrientation(
                        chunkKey.Face, cellU, cellV, gridSize);

                    for (int neighborFace = 0; neighborFace < 6; neighborFace++)
                    {
                        var neighborAddress = GetNeighborAddress(address, neighborFace);
                        byte neighbor = getVoxel(neighborAddress);
                        if (VoxelTypes.IsSolid(neighbor))
                            continue;

                        int unitFace = NeighborToUnitFace[neighborFace];
                        AddOrientedFace(vertices, triangles, center, orientation, unitFace);
                    }
                }
            }
        }

        return BuildCombinedMesh(chunkKey, dirtVertices, dirtTriangles, stoneVertices, stoneTriangles);
    }

    static QuadSphereVoxelAddress GetNeighborAddress(QuadSphereVoxelAddress address, int neighborFace)
    {
        Vector3Int offset = NeighborFaceOffset(neighborFace);
        return new QuadSphereVoxelAddress(
            address.Face,
            address.U + offset.x,
            address.V + offset.y,
            address.Depth + offset.z
        );
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

    static void AddOrientedFace(
        List<Vector3> vertices,
        List<int> triangles,
        Vector3 center,
        Quaternion orientation,
        int unitFaceIndex)
    {
        int start = vertices.Count;
        for (int i = 0; i < 4; i++)
        {
            Vector3 local = UnitFaceVertices[unitFaceIndex, i] - new Vector3(0.5f, 0.5f, 0.5f);
            vertices.Add(center + orientation * local);
        }

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
