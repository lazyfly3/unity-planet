using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class VoxelQuadSphereMeshData
{
    public static readonly VoxelQuadSphereMeshData Empty = new VoxelQuadSphereMeshData(
        new List<Vector3>(0),
        new List<Vector3>(0),
        new[] { new List<int>(0) },
        null);

    public List<Vector3> Vertices { get; }
    public List<Vector3> Normals { get; }
    public List<int>[] SubMeshTriangles { get; }

    public bool IsEmpty => Vertices.Count == 0;

    readonly Action<VoxelQuadSphereMeshData> release;
    bool released;

    public VoxelQuadSphereMeshData(
        List<Vector3> vertices,
        List<Vector3> normals,
        List<int>[] subMeshTriangles,
        Action<VoxelQuadSphereMeshData> release)
    {
        Vertices = vertices ?? new List<Vector3>(0);
        Normals = normals ?? new List<Vector3>(0);
        SubMeshTriangles = subMeshTriangles == null || subMeshTriangles.Length == 0
            ? new[] { new List<int>(0) }
            : subMeshTriangles;
        this.release = release;
    }

    public void Release()
    {
        if (released || release == null)
            return;
        released = true;
        release(this);
    }
}
