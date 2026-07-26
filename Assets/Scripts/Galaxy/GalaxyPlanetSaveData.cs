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
    public int terrainConfigurationHash;
    public PlanetTerrainSettings terrainSettings;
    public bool hasFullMeshSnapshot;
    public QuadSphereChunkSaveEntry[] chunks;
    public string[] harvestedResourceIds;
    public bool hasFullResourceSnapshot;
    public int resourceConfigurationHash;
    public GalaxyResourceSaveEntry[] resources;
    public string[] harvestedSurfacePropIds;
    public bool hasFullSurfacePropSnapshot;
    public int surfacePropConfigurationHash;
    public GalaxySurfacePropSaveEntry[] surfaceProps;
    public GalaxyBuildingSaveEntry[] buildings;
    public GalaxyRiverSaveData riverData;
    public PlanetSurfaceGenerationMode surfaceGenerationMode = PlanetSurfaceGenerationMode.LegacyFullSphere;
    public float planetReferenceRadius = PlanetCelestialProfile.CompatibleRadius;
    public float voxelOuterRadius = PlanetCelestialProfile.CompatibleRadius;
    public PlanetSurfaceTopology surfaceTopology = PlanetSurfaceTopology.LegacySphere;
    public Vector3 planarAnchorDirection = Vector3.up;
    public bool hasPlanarPlayerPosition;
    public double planarPlayerX;
    public double planarPlayerZ;
    public GalaxyDroppedObjectSaveEntry[] droppedObjects;
    public GalaxyPlanarCitySaveEntry[] planarCities;
}

[Serializable]
public sealed class GalaxyDroppedObjectSaveEntry
{
    public string objectId;
    public string payloadTypeId;
    public double planarX;
    public double planarZ;
    public float planarY;
    public Quaternion rotation = Quaternion.identity;
    public Vector3 velocity;
    public Vector3 angularVelocity;
    public bool sleeping;
    public string cityDraftId;
    public int cityBoundaryOrder;
    public bool cityBoundaryLocked;
    public string claimedCityId;
    public string claimedDistrictId;
}

[Serializable]
public sealed class GalaxyPlanarCitySaveEntry
{
    public string cityId;
    public GalaxyPlanarPointSaveEntry[] boundary;
    public GalaxyPlanarCityDistrictSaveEntry[] districts;
}

[Serializable]
public sealed class GalaxyPlanarCityDistrictSaveEntry
{
    public string districtId;
    public double anchorX;
    public double anchorZ;
    public double minimumX;
    public double maximumX;
    public double minimumZ;
    public double maximumZ;
    public float minimumGroundHeight;
    public float maximumGroundHeight;
    public float platformTopHeight;
    public float platformSlabThickness;
    public bool constructionComplete;
    public GalaxyCityPolygonSaveEntry[] boundaryRegions;
    public GalaxyCityPolygonSaveEntry[] platformRegions;
    public GalaxyCityRoadSaveEntry[] roads;
    public GalaxyCityPolygonSaveEntry[] blocks;
    public GalaxyCityPolygonSaveEntry[] lots;
    public GalaxyCityBuildingSaveEntry[] buildings;
}

[Serializable]
public struct GalaxyPlanarPointSaveEntry
{
    public double x;
    public double z;

    public GalaxyPlanarPointSaveEntry(double valueX, double valueZ)
    {
        x = valueX;
        z = valueZ;
    }
}

[Serializable]
public sealed class GalaxyCityPolygonSaveEntry
{
    public Vector2[] points;
}

[Serializable]
public sealed class GalaxyCityRoadSaveEntry
{
    public Vector2 start;
    public Vector2 end;
    public float width;
    public bool major;
    public bool connector;
    public float startHeight;
    public float endHeight;
}

[Serializable]
public sealed class GalaxyCityBuildingSaveEntry
{
    public Vector2[] footprint;
    public float height;
    public int materialIndex;
    public int prefabIndex;
}

[Serializable]
public sealed class GalaxyPlanarCitySaveCollection
{
    public GalaxyPlanarCitySaveEntry[] cities;
}

[Serializable]
public sealed class GalaxyBuildingSaveEntry
{
    public string buildingTypeId;
    public Vector3 localOrigin;
    public Vector3 localUp;
    public Vector3 localForward;
    public float cellSize;
    public float slabHeight;
    public float pillarHeight;
    public float pillarSize;
    public Vector2Int[] occupiedCells;
    public bool usesPlanarAddress;
    public double planarOriginX;
    public double planarOriginZ;
    public float planarOriginY;
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

[Serializable]
public sealed class GalaxySurfacePropSaveEntry
{
    public string catalogId;
    public string instanceId;
    public Vector3 localPosition;
    public Quaternion localRotation;
    public Vector3 localScale = Vector3.one;
    public float minimumSpacing;
    public bool harvestable;
}
