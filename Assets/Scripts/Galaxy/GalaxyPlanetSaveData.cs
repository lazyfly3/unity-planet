using System;
using UnityEngine;

[Serializable]
public sealed class GalaxyPlanetSaveData
{
    public int formatVersion;
    public string planetId;
    public int seed;
    public bool hasFullVoxelSnapshot;
    public int faceGridSize;
    public int maxDepth;
    public int chunkSize;
    public bool hasFullMeshSnapshot;
    public QuadSphereChunkSaveEntry[] chunks;
    public string[] harvestedResourceIds;
    public bool hasFullResourceSnapshot;
    public int resourceConfigurationHash;
    public GalaxyResourceSaveEntry[] resources;
}

[Serializable]
public sealed class QuadSphereChunkSaveEntry
{
    public int face;
    public int chunkU;
    public int chunkV;
    public int chunkDepth;
    public string base64;
    [NonSerialized] public byte[] voxels;
    [NonSerialized] public Vector3[] meshVertices;
    [NonSerialized] public Vector3[] meshNormals;
    [NonSerialized] public int[][] meshSubMeshTriangles;
}

[Serializable]
public sealed class GalaxyResourceSaveEntry
{
    public int settingsIndex;
    public string resourceId;
    public Vector3 localPosition;
    public Quaternion localRotation;
    public float minimumSpacing;
}
