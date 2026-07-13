using System;
using System.Collections.Generic;
using UnityEngine;

public static class VoxelMesher
{
    static readonly Vector3Int[] FaceChecks =
    {
        new Vector3Int( 1,  0,  0),
        new Vector3Int(-1,  0,  0),
        new Vector3Int( 0,  1,  0),
        new Vector3Int( 0, -1,  0),
        new Vector3Int( 0,  0,  1),
        new Vector3Int( 0,  0, -1)
    };

    static readonly Vector3[,] FaceVertices =
    {
        { new Vector3(1,0,0), new Vector3(1,1,0), new Vector3(1,1,1), new Vector3(1,0,1) },
        { new Vector3(0,0,1), new Vector3(0,1,1), new Vector3(0,1,0), new Vector3(0,0,0) },
        { new Vector3(0,1,1), new Vector3(1,1,1), new Vector3(1,1,0), new Vector3(0,1,0) },
        { new Vector3(0,0,0), new Vector3(1,0,0), new Vector3(1,0,1), new Vector3(0,0,1) },
        { new Vector3(1,0,1), new Vector3(1,1,1), new Vector3(0,1,1), new Vector3(0,0,1) },
        { new Vector3(0,0,0), new Vector3(0,1,0), new Vector3(1,1,0), new Vector3(1,0,0) }
    };

    public static Mesh BuildChunkMesh(Func<int, int, int, byte> getWorldVoxel, Vector3Int chunkCoord)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(69);
        List<Vector3> dirtVertices = new List<Vector3>();
        List<int> dirtTriangles = new List<int>();
        List<Vector3> stoneVertices = new List<Vector3>();
        List<int> stoneTriangles = new List<int>();

        int originX = chunkCoord.x * VoxelTypes.ChunkSize;
        int originY = chunkCoord.y * VoxelTypes.ChunkSize;
        int originZ = chunkCoord.z * VoxelTypes.ChunkSize;

        for (int y = 0; y < VoxelTypes.ChunkSize; y++)
        {
            for (int z = 0; z < VoxelTypes.ChunkSize; z++)
            {
                for (int x = 0; x < VoxelTypes.ChunkSize; x++)
                {
                    int worldX = originX + x;
                    int worldY = originY + y;
                    int worldZ = originZ + z;

                    byte voxel = getWorldVoxel(worldX, worldY, worldZ);
                    if (!VoxelTypes.IsSolid(voxel))
                        continue;

                    List<Vector3> vertices = voxel == VoxelTypes.Stone ? stoneVertices : dirtVertices;
                    List<int> triangles = voxel == VoxelTypes.Stone ? stoneTriangles : dirtTriangles;

                    for (int face = 0; face < 6; face++)
                    {
                        Vector3Int offset = FaceChecks[face];
                        byte neighbor = getWorldVoxel(worldX + offset.x, worldY + offset.y, worldZ + offset.z);
                        if (VoxelTypes.IsSolid(neighbor))
                            continue;

                        AddFace(vertices, triangles, new Vector3(x, y, z), face);
                    }
                }
            }
        }

        Mesh mesh = new Mesh
        {
            name = $"ChunkMesh_{chunkCoord.x}_{chunkCoord.y}_{chunkCoord.z}"
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

    static void AddFace(List<Vector3> vertices, List<int> triangles, Vector3 blockOrigin, int faceIndex)
    {
        int start = vertices.Count;
        for (int i = 0; i < 4; i++)
            vertices.Add(blockOrigin + FaceVertices[faceIndex, i]);

        triangles.Add(start + 0);
        triangles.Add(start + 1);
        triangles.Add(start + 2);
        triangles.Add(start + 0);
        triangles.Add(start + 2);
        triangles.Add(start + 3);
    }
}
