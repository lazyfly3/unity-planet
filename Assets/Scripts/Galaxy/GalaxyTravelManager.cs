using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using UnityEngine.SceneManagement;

[System.Serializable]
public sealed class GalaxyPlanetDefinition
{
    public string planetId;
    public string displayName;
    [System.NonSerialized]
    public Vector2Int gridPosition;
    public int seed;
    public Color mapColor = Color.white;
    public string iconResourcePath;
    [Header("Terrain")]
    public PlanetTerrainSettings terrain = new PlanetTerrainSettings();
    [Header("Surface Resources")]
    public bool spawnHarvestableResources = true;
    public List<HarvestableResourceSpawnSettings> resourceSpawnSettings = new List<HarvestableResourceSpawnSettings>();
}

public sealed class GalaxyTravelManager : MonoBehaviour
{
    const int PlanetSaveMagic = 0x504C4E54;
    const int PlanetSaveVersion = 7;

    static GalaxyTravelManager instance;

    [Header("Scenes")]
    [SerializeField] string surfaceSceneName = "star";
    [SerializeField] string mapSceneName = "GalaxyMap";

    [Header("Galaxy Grid")]
    [SerializeField, Min(1)] int gridColumns = 12;
    [SerializeField, Min(1)] int gridRows = 8;
    [SerializeField] int galaxyLayoutSeed = 7319;
    [SerializeField, Min(0)] int planetEdgePadding = 1;
    [SerializeField] List<GalaxyPlanetDefinition> planets = new List<GalaxyPlanetDefinition>();

    string currentPlanetId = "origin";
    Vector2Int shipGridPosition;
    bool transitionInProgress;
    List<InventorySlot> inventorySnapshot;
    int selectedInventorySlot;

    public static GalaxyTravelManager Instance => instance;
    public IReadOnlyList<GalaxyPlanetDefinition> Planets => planets;
    public GalaxyPlanetDefinition CurrentPlanet => GetPlanet(currentPlanetId);
    public Vector2Int ShipGridPosition => shipGridPosition;
    public int GridColumns => gridColumns;
    public int GridRows => gridRows;

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        CreateDefaultGalaxy();
        InitializePlanetLayout();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        if (instance == this)
            SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnApplicationQuit()
    {
        if (SceneManager.GetActiveScene().name != surfaceSceneName)
            return;

        VoxelQuadSphereWorld world = FindObjectOfType<VoxelQuadSphereWorld>();
        if (world != null)
            SavePlanet(world);
    }

    public void OpenGalaxyMap(VoxelQuadSphereWorld world)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(27);
        if (transitionInProgress || world == null)
            return;

        SavePlanet(world);
        CaptureInventory();
        GalaxyPlanetDefinition current = CurrentPlanet;
        if (current != null)
            shipGridPosition = current.gridPosition;

        transitionInProgress = true;
        SceneManager.LoadScene(mapSceneName, LoadSceneMode.Single);
    }

    public void MoveShip(Vector2Int delta)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(28);
        shipGridPosition = new Vector2Int(
            Mathf.Clamp(shipGridPosition.x + delta.x, 0, GridColumns - 1),
            Mathf.Clamp(shipGridPosition.y + delta.y, 0, GridRows - 1));
    }

    public GalaxyPlanetDefinition GetPlanetAt(Vector2Int gridPosition)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(29);
        foreach (GalaxyPlanetDefinition planet in planets)
        {
            if (planet.gridPosition == gridPosition)
                return planet;
        }

        return null;
    }

    public void EnterPlanet(GalaxyPlanetDefinition planet)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(30);
        if (transitionInProgress || planet == null)
            return;

        currentPlanetId = planet.planetId;
        shipGridPosition = planet.gridPosition;
        transitionInProgress = true;
        SceneManager.LoadScene(surfaceSceneName, LoadSceneMode.Single);
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        transitionInProgress = false;
        if (scene.name != surfaceSceneName)
            return;

        VoxelQuadSphereWorld world = FindObjectOfType<VoxelQuadSphereWorld>();
        GalaxyPlanetDefinition planet = CurrentPlanet;
        if (world == null || planet == null)
            return;

        GalaxyPlanetSaveData save = LoadPlanet(planet.planetId);
        GetPlanetPalette(planet, out Color surfaceColor, out Color rockColor);
        world.ConfigurePlanet(
            planet.seed,
            save,
            surfaceColor,
            rockColor,
            planet.terrain,
            planet.spawnHarvestableResources,
            planet.resourceSpawnSettings);
        RestoreBuildings(world, save);
        RestoreInventory();
    }

    void SavePlanet(VoxelQuadSphereWorld world)
    {
        GalaxyPlanetDefinition planet = CurrentPlanet;
        if (planet == null)
            return;

        List<QuadSphereChunkSaveEntry> chunks = world.GetCompleteChunkSnapshots();
        GalaxyPlanetSaveData data = new GalaxyPlanetSaveData
        {
            formatVersion = PlanetSaveVersion,
            planetId = planet.planetId,
            seed = planet.seed,
            hasFullVoxelSnapshot = chunks.Count == world.ExpectedChunkCount,
            faceGridSize = world.FaceGridSize,
            maxDepth = world.MaxDepth,
            chunkSize = VoxelTypes.ChunkSize,
            terrainConfigurationHash = world.TerrainConfigurationHash,
            terrainSettings = world.TerrainSettingsSnapshot,
            hasFullMeshSnapshot = world.HasCompleteMeshSnapshot,
            chunks = chunks.ToArray(),
            harvestedResourceIds = world.GetHarvestedResourceIds(),
            hasFullResourceSnapshot = true,
            resourceConfigurationHash = world.ResourceConfigurationHash,
            resources = world.GetResourceSnapshots(),
            buildings = CaptureBuildings(world)
        };

        Directory.CreateDirectory(GetSaveDirectory());
        string savePath = GetPlanetSavePath(planet.planetId);
        float saveStartedAt = Time.realtimeSinceStartup;
        WritePlanetBinary(savePath, data);
        float elapsedMilliseconds = (Time.realtimeSinceStartup - saveStartedAt) * 1000f;
        float fileMegabytes = new FileInfo(savePath).Length / (1024f * 1024f);
        Debug.Log(
            $"GalaxyTravelManager: saved planet '{planet.planetId}' with voxel and mesh snapshots "
            + $"({fileMegabytes:0.00} MB) in {elapsedMilliseconds:0} ms.",
            this);
    }

    GalaxyPlanetSaveData LoadPlanet(string planetId)
    {
        string path = GetPlanetSavePath(planetId);
        if (File.Exists(path))
        {
            try
            {
                return ReadPlanetBinary(path);
            }
            catch (System.Exception exception)
            {
                Debug.LogError($"GalaxyTravelManager: could not load compressed planet '{planetId}'. {exception.Message}");
            }
        }

        string legacyPath = GetLegacyPlanetSavePath(planetId);
        if (!File.Exists(legacyPath))
            return null;

        try
        {
            return JsonUtility.FromJson<GalaxyPlanetSaveData>(File.ReadAllText(legacyPath));
        }
        catch (System.Exception exception)
        {
            Debug.LogError($"GalaxyTravelManager: could not load planet '{planetId}'. {exception.Message}");
            return null;
        }
    }

    static void WritePlanetBinary(string path, GalaxyPlanetSaveData data)
    {
        string temporaryPath = path + ".tmp";
        using (FileStream file = File.Create(temporaryPath))
        using (GZipStream gzip = new GZipStream(file, System.IO.Compression.CompressionLevel.Fastest))
        using (BinaryWriter writer = new BinaryWriter(gzip))
        {
            writer.Write(PlanetSaveMagic);
            writer.Write(PlanetSaveVersion);
            writer.Write(data.planetId ?? string.Empty);
            writer.Write(data.seed);
            writer.Write(data.hasFullVoxelSnapshot);
            writer.Write(data.faceGridSize);
            writer.Write(data.maxDepth);
            writer.Write(data.chunkSize);
            WriteTerrainSettings(writer, data.terrainSettings);
            writer.Write(data.terrainConfigurationHash);
            writer.Write(data.hasFullMeshSnapshot);

            QuadSphereChunkSaveEntry[] chunks = data.chunks ?? new QuadSphereChunkSaveEntry[0];
            writer.Write(chunks.Length);
            foreach (QuadSphereChunkSaveEntry entry in chunks)
            {
                writer.Write(entry.face);
                writer.Write(entry.chunkU);
                writer.Write(entry.chunkV);
                writer.Write(entry.chunkDepth);

                byte[] voxels = entry.voxels;
                if (voxels == null && !string.IsNullOrEmpty(entry.base64))
                    voxels = System.Convert.FromBase64String(entry.base64);
                if (voxels == null)
                    voxels = new byte[0];

                writer.Write(voxels.Length);
                writer.Write(voxels);

                bool hasMesh = entry.meshVertices != null
                    && entry.meshNormals != null
                    && entry.meshNormals.Length == entry.meshVertices.Length
                    && entry.meshSubMeshTriangles != null
                    && entry.meshSubMeshTriangles.Length > 0;
                writer.Write(hasMesh);
                if (!hasMesh)
                    continue;

                writer.Write(entry.meshVertices.Length);
                foreach (Vector3 vertex in entry.meshVertices)
                {
                    writer.Write(vertex.x);
                    writer.Write(vertex.y);
                    writer.Write(vertex.z);
                }

                writer.Write(entry.meshNormals.Length);
                foreach (Vector3 normal in entry.meshNormals)
                {
                    writer.Write(normal.x);
                    writer.Write(normal.y);
                    writer.Write(normal.z);
                }

                writer.Write(entry.meshSubMeshTriangles.Length);
                foreach (int[] triangles in entry.meshSubMeshTriangles)
                {
                    writer.Write(triangles.Length);
                    foreach (int index in triangles)
                        writer.Write(index);
                }
            }

            string[] harvested = data.harvestedResourceIds ?? new string[0];
            writer.Write(harvested.Length);
            foreach (string resourceId in harvested)
                writer.Write(resourceId ?? string.Empty);

            writer.Write(data.hasFullResourceSnapshot);
            writer.Write(data.resourceConfigurationHash);
            GalaxyResourceSaveEntry[] resources = data.resources ?? new GalaxyResourceSaveEntry[0];
            writer.Write(resources.Length);
            foreach (GalaxyResourceSaveEntry resource in resources)
            {
                writer.Write(resource.settingsIndex);
                writer.Write(resource.resourceId ?? string.Empty);
                writer.Write(resource.localPosition.x);
                writer.Write(resource.localPosition.y);
                writer.Write(resource.localPosition.z);
                writer.Write(resource.localRotation.x);
                writer.Write(resource.localRotation.y);
                writer.Write(resource.localRotation.z);
                writer.Write(resource.localRotation.w);
                writer.Write(resource.minimumSpacing);
            }

            GalaxyBuildingSaveEntry[] buildings = data.buildings ?? new GalaxyBuildingSaveEntry[0];
            writer.Write(buildings.Length);
            foreach (GalaxyBuildingSaveEntry buildingValue in buildings)
            {
                GalaxyBuildingSaveEntry building = buildingValue ?? new GalaxyBuildingSaveEntry();
                writer.Write(building.buildingTypeId ?? string.Empty);
                WriteVector3(writer, building.localOrigin);
                WriteVector3(writer, building.localUp);
                WriteVector3(writer, building.localForward);
                writer.Write(building.cellSize);
                writer.Write(building.slabHeight);
                writer.Write(building.pillarHeight);
                writer.Write(building.pillarSize);

                Vector2Int[] cells = building.occupiedCells ?? new Vector2Int[0];
                writer.Write(cells.Length);
                foreach (Vector2Int cell in cells)
                {
                    writer.Write(cell.x);
                    writer.Write(cell.y);
                }
            }
        }

        if (File.Exists(path))
            File.Replace(temporaryPath, path, null);
        else
            File.Move(temporaryPath, path);
    }

    static GalaxyPlanetSaveData ReadPlanetBinary(string path)
    {
        using (FileStream file = File.OpenRead(path))
        using (GZipStream gzip = new GZipStream(file, CompressionMode.Decompress))
        using (BinaryReader reader = new BinaryReader(gzip))
        {
            if (reader.ReadInt32() != PlanetSaveMagic)
                throw new InvalidDataException("Invalid planet save signature.");

            int version = reader.ReadInt32();
            if (version < 2 || version > PlanetSaveVersion)
                throw new InvalidDataException($"Unsupported planet save version {version}.");

            GalaxyPlanetSaveData data = new GalaxyPlanetSaveData
            {
                formatVersion = version,
                planetId = reader.ReadString(),
                seed = reader.ReadInt32(),
                hasFullVoxelSnapshot = reader.ReadBoolean(),
                faceGridSize = reader.ReadInt32(),
                maxDepth = reader.ReadInt32(),
                chunkSize = reader.ReadInt32()
            };
            if (version >= 6)
            {
                data.terrainSettings = ReadTerrainSettings(reader);
                data.terrainConfigurationHash = reader.ReadInt32();
            }
            if (version >= 4)
                data.hasFullMeshSnapshot = reader.ReadBoolean();

            if (data.chunkSize <= 0 || data.chunkSize > 64)
                throw new InvalidDataException($"Invalid chunk size {data.chunkSize}.");

            int chunkCount = reader.ReadInt32();
            if (chunkCount < 0 || chunkCount > 100000)
                throw new InvalidDataException($"Invalid chunk count {chunkCount}.");

            int expectedVoxelCount = data.chunkSize * data.chunkSize * data.chunkSize;
            data.chunks = new QuadSphereChunkSaveEntry[chunkCount];
            for (int i = 0; i < chunkCount; i++)
            {
                QuadSphereChunkSaveEntry entry = new QuadSphereChunkSaveEntry
                {
                    face = reader.ReadInt32(),
                    chunkU = reader.ReadInt32(),
                    chunkV = reader.ReadInt32(),
                    chunkDepth = reader.ReadInt32()
                };

                int voxelCount = reader.ReadInt32();
                if (voxelCount != expectedVoxelCount)
                    throw new InvalidDataException($"Invalid voxel count {voxelCount} in chunk {i}.");

                entry.voxels = reader.ReadBytes(voxelCount);
                if (entry.voxels.Length != voxelCount)
                    throw new EndOfStreamException($"Unexpected end of planet save in chunk {i}.");

                if (version >= 4 && reader.ReadBoolean())
                {
                    int vertexCount = ReadBoundedCount(reader, "mesh vertex", 2000000);
                    entry.meshVertices = new Vector3[vertexCount];
                    for (int vertex = 0; vertex < vertexCount; vertex++)
                    {
                        entry.meshVertices[vertex] = new Vector3(
                            reader.ReadSingle(),
                            reader.ReadSingle(),
                            reader.ReadSingle());
                    }

                    int normalCount = ReadBoundedCount(reader, "mesh normal", 2000000);
                    if (normalCount != vertexCount)
                        throw new InvalidDataException($"Mesh normal count {normalCount} does not match vertex count {vertexCount}.");
                    entry.meshNormals = new Vector3[normalCount];
                    for (int normal = 0; normal < normalCount; normal++)
                    {
                        entry.meshNormals[normal] = new Vector3(
                            reader.ReadSingle(),
                            reader.ReadSingle(),
                            reader.ReadSingle());
                    }

                    int subMeshCount = ReadBoundedCount(reader, "sub-mesh", 8);
                    if (subMeshCount == 0)
                        throw new InvalidDataException("Mesh snapshot has no sub-meshes.");
                    entry.meshSubMeshTriangles = new int[subMeshCount][];
                    for (int subMesh = 0; subMesh < subMeshCount; subMesh++)
                    {
                        int indexCount = ReadBoundedCount(reader, "mesh index", 12000000);
                        if (indexCount % 3 != 0)
                            throw new InvalidDataException($"Mesh index count {indexCount} is not divisible by three.");
                        entry.meshSubMeshTriangles[subMesh] = new int[indexCount];
                        for (int index = 0; index < indexCount; index++)
                            entry.meshSubMeshTriangles[subMesh][index] = reader.ReadInt32();
                    }
                }
                data.chunks[i] = entry;
            }

            int harvestedCount = reader.ReadInt32();
            if (harvestedCount < 0 || harvestedCount > 100000)
                throw new InvalidDataException($"Invalid harvested resource count {harvestedCount}.");

            data.harvestedResourceIds = new string[harvestedCount];
            for (int i = 0; i < harvestedCount; i++)
                data.harvestedResourceIds[i] = reader.ReadString();

            if (version >= 3)
            {
                data.hasFullResourceSnapshot = reader.ReadBoolean();
                if (version >= 5)
                    data.resourceConfigurationHash = reader.ReadInt32();
                int resourceCount = reader.ReadInt32();
                if (resourceCount < 0 || resourceCount > 100000)
                    throw new InvalidDataException($"Invalid resource placement count {resourceCount}.");

                data.resources = new GalaxyResourceSaveEntry[resourceCount];
                for (int i = 0; i < resourceCount; i++)
                {
                    data.resources[i] = new GalaxyResourceSaveEntry
                    {
                        settingsIndex = reader.ReadInt32(),
                        resourceId = reader.ReadString(),
                        localPosition = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                        localRotation = new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                        minimumSpacing = reader.ReadSingle()
                    };
                }
            }

            if (version >= 7)
            {
                int buildingCount = ReadBoundedCount(reader, "building", 100000);
                data.buildings = new GalaxyBuildingSaveEntry[buildingCount];
                for (int i = 0; i < buildingCount; i++)
                {
                    GalaxyBuildingSaveEntry building = new GalaxyBuildingSaveEntry
                    {
                        buildingTypeId = reader.ReadString(),
                        localOrigin = ReadVector3(reader),
                        localUp = ReadVector3(reader),
                        localForward = ReadVector3(reader),
                        cellSize = reader.ReadSingle(),
                        slabHeight = reader.ReadSingle(),
                        pillarHeight = reader.ReadSingle(),
                        pillarSize = reader.ReadSingle()
                    };
                    int cellCount = ReadBoundedCount(reader, "building cell", 1000000);
                    building.occupiedCells = new Vector2Int[cellCount];
                    for (int cell = 0; cell < cellCount; cell++)
                        building.occupiedCells[cell] = new Vector2Int(reader.ReadInt32(), reader.ReadInt32());
                    data.buildings[i] = building;
                }
            }

            return data;
        }
    }

    static void WriteVector3(BinaryWriter writer, Vector3 value)
    {
        writer.Write(value.x);
        writer.Write(value.y);
        writer.Write(value.z);
    }

    static Vector3 ReadVector3(BinaryReader reader)
    {
        return new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    }

    static int ReadBoundedCount(BinaryReader reader, string valueName, int maximum)
    {
        int value = reader.ReadInt32();
        if (value < 0 || value > maximum)
            throw new InvalidDataException($"Invalid {valueName} count {value}.");
        return value;
    }

    static void WriteTerrainSettings(BinaryWriter writer, PlanetTerrainSettings settings)
    {
        settings = settings ?? new PlanetTerrainSettings();
        writer.Write(settings.continentScale);
        writer.Write(settings.continentHeight);
        writer.Write(settings.detailScale);
        writer.Write(settings.detailHeight);
        writer.Write(settings.ridgeHeight);
        writer.Write(settings.surfaceLayerDepth);
        writer.Write(settings.stoneDepth);
        writer.Write(settings.generateCaves);
        writer.Write(settings.caveScale);
        writer.Write(settings.caveThreshold);
        writer.Write(settings.caveSurfaceClearance);
    }

    static PlanetTerrainSettings ReadTerrainSettings(BinaryReader reader)
    {
        return new PlanetTerrainSettings
        {
            continentScale = reader.ReadSingle(),
            continentHeight = reader.ReadSingle(),
            detailScale = reader.ReadSingle(),
            detailHeight = reader.ReadSingle(),
            ridgeHeight = reader.ReadSingle(),
            surfaceLayerDepth = reader.ReadSingle(),
            stoneDepth = reader.ReadSingle(),
            generateCaves = reader.ReadBoolean(),
            caveScale = reader.ReadSingle(),
            caveThreshold = reader.ReadSingle(),
            caveSurfaceClearance = reader.ReadSingle()
        };
    }

    static void GetPlanetPalette(
        GalaxyPlanetDefinition planet,
        out Color surfaceColor,
        out Color rockColor)
    {
        string paletteKey = planet.planetId;
        string iconPath = planet.iconResourcePath ?? string.Empty;
        if (iconPath.EndsWith("planet_amber", System.StringComparison.OrdinalIgnoreCase))
            paletteKey = "origin";
        else if (iconPath.EndsWith("planet_green", System.StringComparison.OrdinalIgnoreCase))
            paletteKey = "verdant";
        else if (iconPath.EndsWith("planet_red", System.StringComparison.OrdinalIgnoreCase))
            paletteKey = "crimson";
        else if (iconPath.EndsWith("planet_blue", System.StringComparison.OrdinalIgnoreCase))
            paletteKey = "azure";
        else if (iconPath.EndsWith("planet_violet", System.StringComparison.OrdinalIgnoreCase))
            paletteKey = "violet";

        switch (paletteKey)
        {
            case "origin":
                surfaceColor = new Color(0.72f, 0.35f, 0.11f, 1f);
                rockColor = new Color(0.28f, 0.13f, 0.07f, 1f);
                return;
            case "verdant":
                surfaceColor = new Color(0.16f, 0.62f, 0.24f, 1f);
                rockColor = new Color(0.06f, 0.27f, 0.18f, 1f);
                return;
            case "crimson":
                surfaceColor = new Color(0.72f, 0.12f, 0.055f, 1f);
                rockColor = new Color(0.24f, 0.035f, 0.025f, 1f);
                return;
            case "azure":
                surfaceColor = new Color(0.07f, 0.38f, 0.78f, 1f);
                rockColor = new Color(0.18f, 0.58f, 0.72f, 1f);
                return;
            case "violet":
                surfaceColor = new Color(0.48f, 0.14f, 0.7f, 1f);
                rockColor = new Color(0.19f, 0.06f, 0.31f, 1f);
                return;
            default:
                surfaceColor = planet.mapColor;
                rockColor = Color.Lerp(planet.mapColor, Color.black, 0.62f);
                return;
        }
    }

    GalaxyPlanetDefinition GetPlanet(string planetId)
    {
        foreach (GalaxyPlanetDefinition planet in planets)
        {
            if (planet.planetId == planetId)
                return planet;
        }

        return null;
    }

    void InitializePlanetLayout()
    {
        gridColumns = Mathf.Max(1, gridColumns);
        gridRows = Mathf.Max(1, gridRows);

        if (planets.Count == 0)
            return;

        GeneratePlanetPositions();

        if (GetPlanet(currentPlanetId) == null)
            currentPlanetId = planets[0].planetId;

        GalaxyPlanetDefinition current = CurrentPlanet;
        if (current != null)
            shipGridPosition = current.gridPosition;
    }

    void GeneratePlanetPositions()
    {
        int paddingX = Mathf.Clamp(planetEdgePadding, 0, (gridColumns - 1) / 2);
        int paddingY = Mathf.Clamp(planetEdgePadding, 0, (gridRows - 1) / 2);
        List<Vector2Int> available = CreateAvailableCells(paddingX, paddingY);

        if (available.Count < planets.Count)
        {
            Debug.LogWarning(
                "GalaxyTravelManager: edge padding leaves too few cells. Planet placement will use the full grid.",
                this);
            available = CreateAvailableCells(0, 0);
        }

        int placeCount = Mathf.Min(planets.Count, available.Count);
        if (placeCount < planets.Count)
        {
            Debug.LogError(
                $"GalaxyTravelManager: the {gridColumns}x{gridRows} grid only has room for {placeCount} planets.",
                this);
        }

        System.Random random = new System.Random(galaxyLayoutSeed);
        List<Vector2Int> placed = new List<Vector2Int>(placeCount);

        if (placeCount > 0)
        {
            Vector2Int center = new Vector2Int(gridColumns / 2, gridRows / 2);
            Vector2Int first = FindClosestCell(available, center);
            AssignPlanetPosition(0, first, available, placed);
        }

        for (int planetIndex = 1; planetIndex < placeCount; planetIndex++)
        {
            int bestMinimumDistance = -1;
            List<Vector2Int> bestCells = new List<Vector2Int>();

            foreach (Vector2Int candidate in available)
            {
                int minimumDistance = int.MaxValue;
                foreach (Vector2Int existing in placed)
                {
                    Vector2Int difference = candidate - existing;
                    minimumDistance = Mathf.Min(minimumDistance, difference.sqrMagnitude);
                }

                if (minimumDistance > bestMinimumDistance)
                {
                    bestMinimumDistance = minimumDistance;
                    bestCells.Clear();
                    bestCells.Add(candidate);
                }
                else if (minimumDistance == bestMinimumDistance)
                {
                    bestCells.Add(candidate);
                }
            }

            Vector2Int selected = bestCells[random.Next(bestCells.Count)];
            AssignPlanetPosition(planetIndex, selected, available, placed);
        }
    }

    List<Vector2Int> CreateAvailableCells(int paddingX, int paddingY)
    {
        List<Vector2Int> result = new List<Vector2Int>();
        for (int y = paddingY; y < gridRows - paddingY; y++)
        {
            for (int x = paddingX; x < gridColumns - paddingX; x++)
                result.Add(new Vector2Int(x, y));
        }

        return result;
    }

    static Vector2Int FindClosestCell(List<Vector2Int> cells, Vector2Int target)
    {
        Vector2Int closest = cells[0];
        int closestDistance = (closest - target).sqrMagnitude;
        for (int i = 1; i < cells.Count; i++)
        {
            int distance = (cells[i] - target).sqrMagnitude;
            if (distance >= closestDistance)
                continue;

            closest = cells[i];
            closestDistance = distance;
        }

        return closest;
    }

    void AssignPlanetPosition(
        int planetIndex,
        Vector2Int position,
        List<Vector2Int> available,
        List<Vector2Int> placed)
    {
        planets[planetIndex].gridPosition = position;
        available.Remove(position);
        placed.Add(position);
    }

    static GalaxyBuildingSaveEntry[] CaptureBuildings(VoxelQuadSphereWorld world)
    {
        if (world == null)
            return new GalaxyBuildingSaveEntry[0];

        var result = new List<GalaxyBuildingSaveEntry>();
        IReadOnlyList<BuildingAnchor> anchors = BuildingAnchor.GetActiveAnchors();
        foreach (BuildingAnchor anchor in anchors)
        {
            if (anchor == null || anchor.OccupiedCells.Count == 0)
                continue;

            var cells = new List<Vector2Int>(anchor.OccupiedCells);
            cells.Sort((left, right) =>
            {
                int xComparison = left.x.CompareTo(right.x);
                return xComparison != 0 ? xComparison : left.y.CompareTo(right.y);
            });
            result.Add(new GalaxyBuildingSaveEntry
            {
                buildingTypeId = "foundation",
                localOrigin = world.transform.InverseTransformPoint(anchor.OriginWorld),
                localUp = world.transform.InverseTransformDirection(anchor.Up).normalized,
                localForward = world.transform.InverseTransformDirection(anchor.Forward).normalized,
                cellSize = anchor.CellSize,
                slabHeight = anchor.SlabHeight,
                pillarHeight = anchor.PillarHeight,
                pillarSize = anchor.PillarSize,
                occupiedCells = cells.ToArray()
            });
        }

        return result.ToArray();
    }

    static void RestoreBuildings(VoxelQuadSphereWorld world, GalaxyPlanetSaveData save)
    {
        if (world == null || save == null || save.buildings == null || save.buildings.Length == 0)
            return;

        BuildingPlacer placer = FindObjectOfType<BuildingPlacer>();
        Material foundationMaterial = placer != null ? placer.FoundationMaterial : null;
        int restoredPieces = 0;
        foreach (GalaxyBuildingSaveEntry building in save.buildings)
        {
            if (building == null
                || building.buildingTypeId != "foundation"
                || building.occupiedCells == null
                || building.occupiedCells.Length == 0)
            {
                continue;
            }

            Vector3 up = world.transform.TransformDirection(building.localUp).normalized;
            Vector3 forward = world.transform.TransformDirection(building.localForward).normalized;
            if (up.sqrMagnitude < 0.9f || forward.sqrMagnitude < 0.9f)
                continue;

            BuildingAnchor anchor = BuildingAnchor.Create(
                world.transform.TransformPoint(building.localOrigin),
                up,
                forward,
                Mathf.Max(0.1f, building.cellSize),
                Mathf.Max(0.01f, building.slabHeight),
                Mathf.Max(0f, building.pillarHeight),
                Mathf.Max(0.01f, building.pillarSize),
                foundationMaterial);
            anchor.transform.SetParent(world.transform, true);
            restoredPieces += anchor.RestoreCells(building.occupiedCells);
        }

        Debug.Log($"GalaxyTravelManager: restored {restoredPieces} building pieces for planet '{save.planetId}'.");
    }

    void CaptureInventory()
    {
        PlayerInventory inventory = FindObjectOfType<PlayerInventory>();
        if (inventory == null)
            return;

        inventorySnapshot = inventory.CreateSnapshot();
        selectedInventorySlot = inventory.SelectedSlotIndex;
    }

    void RestoreInventory()
    {
        if (inventorySnapshot == null)
            return;

        PlayerInventory inventory = FindObjectOfType<PlayerInventory>();
        if (inventory != null)
            inventory.RestoreSnapshot(inventorySnapshot, selectedInventorySlot);
    }

    string GetSaveDirectory()
    {
        return Path.Combine(Application.persistentDataPath, "galaxy_planets");
    }

    string GetPlanetSavePath(string planetId)
    {
        return Path.Combine(GetSaveDirectory(), planetId + ".planet.gz");
    }

    string GetLegacyPlanetSavePath(string planetId)
    {
        return Path.Combine(GetSaveDirectory(), planetId + ".json");
    }

    void CreateDefaultGalaxy()
    {
        if (planets.Count > 0)
            return;

        planets.Add(CreatePlanet("origin", "Aster", 5, 4, 12345, new Color(0.85f, 0.53f, 0.24f), "Galaxy/planet_amber"));
        planets.Add(CreatePlanet("verdant", "Viridia", 2, 2, 24680, new Color(0.3f, 0.82f, 0.5f), "Galaxy/planet_green"));
        planets.Add(CreatePlanet("crimson", "Cinder", 9, 1, 97531, new Color(0.9f, 0.3f, 0.2f), "Galaxy/planet_red"));
        planets.Add(CreatePlanet("azure", "Pelagos", 9, 6, 48127, new Color(0.25f, 0.65f, 0.95f), "Galaxy/planet_blue"));
        planets.Add(CreatePlanet("violet", "Nyx", 3, 6, 86420, new Color(0.65f, 0.35f, 0.9f), "Galaxy/planet_violet"));
        shipGridPosition = planets[0].gridPosition;
    }

    static GalaxyPlanetDefinition CreatePlanet(
        string id,
        string displayName,
        int x,
        int y,
        int planetSeed,
        Color color,
        string iconPath)
    {
        return new GalaxyPlanetDefinition
        {
            planetId = id,
            displayName = displayName,
            gridPosition = new Vector2Int(x, y),
            seed = planetSeed,
            mapColor = color,
            iconResourcePath = iconPath,
            terrain = CreateTerrainPreset(id)
        };
    }

    static PlanetTerrainSettings CreateTerrainPreset(string planetId)
    {
        switch (planetId)
        {
            case "verdant":
                return new PlanetTerrainSettings
                {
                    continentScale = 0.012f, continentHeight = 10f,
                    detailScale = 0.045f, detailHeight = 1.5f, ridgeHeight = 0.5f,
                    surfaceLayerDepth = 1.5f, stoneDepth = 5f,
                    caveScale = 0.045f, caveThreshold = 0.72f, caveSurfaceClearance = 4f
                };
            case "crimson":
                return new PlanetTerrainSettings
                {
                    continentScale = 0.025f, continentHeight = 9f,
                    detailScale = 0.085f, detailHeight = 4.5f, ridgeHeight = 7f,
                    surfaceLayerDepth = 0.6f, stoneDepth = 2.5f,
                    caveScale = 0.075f, caveThreshold = 0.59f, caveSurfaceClearance = 2f
                };
            case "azure":
                return new PlanetTerrainSettings
                {
                    continentScale = 0.01f, continentHeight = 6f,
                    detailScale = 0.035f, detailHeight = 0.8f, ridgeHeight = 0.2f,
                    surfaceLayerDepth = 2f, stoneDepth = 6f,
                    caveScale = 0.035f, caveThreshold = 0.78f, caveSurfaceClearance = 5f
                };
            case "violet":
                return new PlanetTerrainSettings
                {
                    continentScale = 0.03f, continentHeight = 6f,
                    detailScale = 0.11f, detailHeight = 5.5f, ridgeHeight = 6f,
                    surfaceLayerDepth = 0.7f, stoneDepth = 3f,
                    caveScale = 0.095f, caveThreshold = 0.57f, caveSurfaceClearance = 1.5f
                };
            default:
                return new PlanetTerrainSettings
                {
                    continentScale = 0.022f, continentHeight = 7f,
                    detailScale = 0.075f, detailHeight = 3f, ridgeHeight = 2f,
                    surfaceLayerDepth = 1f, stoneDepth = 4f,
                    caveScale = 0.06f, caveThreshold = 0.66f, caveSurfaceClearance = 3f
                };
        }
    }
}
