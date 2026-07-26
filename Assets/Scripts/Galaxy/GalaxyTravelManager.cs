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
    [System.NonSerialized]
    public GalaxyCoordinate coordinate;
    [System.NonSerialized]
    public InterstellarCoordinate coordinate3D;
    [System.NonSerialized]
    public bool isProcedural;
    [System.NonSerialized]
    public bool isInterstellar;
    [Header("Stellar System")]
    public string systemId;
    public int orbitIndex;
    public CelestialOrbitDefinition orbit = new CelestialOrbitDefinition();
    public int seed;
    [Header(nameof(PlanetClimate))]
    public PlanetClimate climate = PlanetClimate.Barren;
    public Color mapColor = Color.white;
    public Color surfaceColor = Color.white;
    public Color rockColor = Color.gray;
    public bool hasExplicitPalette;
    public bool tintMapIcon;
    public string iconResourcePath;
    [Header("Terrain")]
    public PlanetTerrainSettings terrain = new PlanetTerrainSettings();
    [Header("Surface Resources")]
    public bool spawnHarvestableResources = true;
    public List<HarvestableResourceSpawnSettings> resourceSpawnSettings = new List<HarvestableResourceSpawnSettings>();
    [Header("Rivers")]
    public PlanetRiverSettings rivers = new PlanetRiverSettings();
    [Header("Weather")]
    public PlanetWeatherSettings weather = new PlanetWeatherSettings();
    [Header("Celestial Physics")]
    public PlanetCelestialProfile celestial = new PlanetCelestialProfile();
    [Header("Low Poly Visuals")]
    public PlanetLowPolyVisualProfile lowPolyVisual;
}

[DefaultExecutionOrder(-1000)]
public sealed class GalaxyTravelManager : MonoBehaviour
{
    const int PlanetSaveMagic = 0x504C4E54;
    const int PlanetSaveVersion = 15;
    const int StarterEquipmentVersion = 1;

    static GalaxyTravelManager instance;

    [Header("Scenes")]
    [SerializeField] string surfaceSceneName = "star";
    [SerializeField] string mapSceneName = "GalaxyMap";
    [SerializeField] string interstellarSceneName = "InterstellarFlight";
    [SerializeField] string approachSceneName = "PlanetApproach";

    [Header("Galaxy Grid")]
    [SerializeField, Min(1)] int gridColumns = 12;
    [SerializeField, Min(1)] int gridRows = 8;
    [SerializeField] int galaxyLayoutSeed = 7319;
    [SerializeField, Min(0)] int planetEdgePadding = 1;
    [SerializeField] List<GalaxyPlanetDefinition> planets = new List<GalaxyPlanetDefinition>();
    [SerializeField] List<GalaxyResourceCatalogEntry> resourceCatalog = new List<GalaxyResourceCatalogEntry>();
    [SerializeField] PlanetDecorationCatalog decorationCatalog;

    string currentPlanetId = "origin";
    Vector2Int shipGridPosition;
    GalaxyCoordinate shipCoordinate;
    GalaxyCoordinate currentPlanetCoordinate;
    InterstellarCoordinate shipCoordinate3D;
    InterstellarCoordinate currentPlanetCoordinate3D;
    GalaxyShipFacing shipFacing = GalaxyShipFacing.Up;
    bool transitionInProgress;
    string galaxyMapReturnSceneName;
    List<InventorySlot> inventorySnapshot;
    int selectedInventorySlot;
    GalaxySaveSlotMetadata activeSlotMetadata;
    readonly Dictionary<GalaxyCoordinate, GalaxyPlanetDefinition> proceduralPlanetCache = new Dictionary<GalaxyCoordinate, GalaxyPlanetDefinition>();
    readonly Queue<GalaxyCoordinate> proceduralCacheOrder = new Queue<GalaxyCoordinate>();
    readonly Dictionary<InterstellarCoordinate, GalaxyPlanetDefinition> interstellarPlanetCache = new Dictionary<InterstellarCoordinate, GalaxyPlanetDefinition>();
    readonly Queue<InterstellarCoordinate> interstellarCacheOrder = new Queue<InterstellarCoordinate>();
    readonly List<GalaxyResourceCatalogEntry> runtimeResourceCatalog = new List<GalaxyResourceCatalogEntry>();
    readonly HashSet<string> frozenPlanetIds = new HashSet<string>();
    ProceduralGalaxyGenerator proceduralGenerator;
    ProceduralInterstellarGenerator interstellarGenerator;

    public static GalaxyTravelManager Instance
    {
        get
        {
            if (instance == null)
                instance = FindObjectOfType<GalaxyTravelManager>();
            return instance;
        }
    }
    public IReadOnlyList<GalaxyPlanetDefinition> Planets => planets;
    public GalaxyPlanetDefinition CurrentPlanet => IsInterstellarGalaxy
        ? GetPlanetAt(currentPlanetCoordinate3D)
        : IsInfiniteGalaxy ? GetPlanetAt(currentPlanetCoordinate) : GetPlanet(currentPlanetId);
    public Vector2Int ShipGridPosition => shipGridPosition;
    public GalaxyCoordinate ShipCoordinate => IsInfiniteGalaxy
        ? shipCoordinate
        : new GalaxyCoordinate(shipGridPosition.x, shipGridPosition.y);
    public bool IsInfiniteGalaxy => activeSlotMetadata != null
        && activeSlotMetadata.galaxyMode == GalaxyMode.InfiniteProcedural;
    public bool IsInterstellarGalaxy => activeSlotMetadata != null
        && activeSlotMetadata.galaxyMode == GalaxyMode.Interstellar3DProcedural;
    public bool UsesInfinitePlanarSurface => activeSlotMetadata != null
        && activeSlotMetadata.surfaceTopology
            == PlanetSurfaceTopology.InfinitePlanar;
    public InterstellarCoordinate ShipCoordinate3D => shipCoordinate3D;
    public InterstellarCoordinate CurrentPlanetCoordinate3D => currentPlanetCoordinate3D;
    public int WorldSeed => activeSlotMetadata != null ? activeSlotMetadata.worldSeed : galaxyLayoutSeed;
    public UniversePosition SavedUniversePosition
    {
        get
        {
            if (activeSlotMetadata == null)
                return default;
            if (activeSlotMetadata.hasHierarchicalSpacePosition)
            {
                return new UniversePosition(
                    new InterstellarCoordinate(
                        activeSlotMetadata.spaceSystemX,
                        activeSlotMetadata.spaceSystemY,
                        activeSlotMetadata.spaceSystemZ),
                    new DoubleVector3(
                        activeSlotMetadata.spaceLocalX,
                        activeSlotMetadata.spaceLocalY,
                        activeSlotMetadata.spaceLocalZ));
            }
            return UniversePosition.FromAbsoluteMeters(new DoubleVector3(
                activeSlotMetadata.spacePositionX,
                activeSlotMetadata.spacePositionY,
                activeSlotMetadata.spacePositionZ));
        }
    }
    public DoubleVector3 SavedSpacePosition => SavedUniversePosition.ToAbsoluteMeters();
    public string NearObservationPlanetId => activeSlotMetadata == null
        ? string.Empty
        : activeSlotMetadata.nearObservationPlanetId ?? string.Empty;
    public float SpacecraftHullIntegrity => activeSlotMetadata != null
        ? Mathf.Clamp(activeSlotMetadata.spacecraftHullIntegrity, 0f, 100f)
        : 100f;
    public GalaxyShipFacing ShipFacing => shipFacing;
    public int GridColumns => gridColumns;
    public int GridRows => gridRows;
    public double WeatherTimeSeconds => activeSlotMetadata != null ? activeSlotMetadata.weatherTimeSeconds : 0d;
    public double UniverseTimeSeconds => activeSlotMetadata != null ? activeSlotMetadata.universeTimeSeconds : 0d;
    public PlanetDecorationCatalog DecorationCatalog => decorationCatalog;
    public IReadOnlyList<VisitedPlanetRecord> VisitedPlanets => activeSlotMetadata != null
        && activeSlotMetadata.visitedPlanets != null
            ? activeSlotMetadata.visitedPlanets
            : System.Array.Empty<VisitedPlanetRecord>();
    public IReadOnlyList<BiotaDiscoveryRecord> BiotaCodex => activeSlotMetadata != null
        && activeSlotMetadata.biotaCodex != null
            ? activeSlotMetadata.biotaCodex
            : System.Array.Empty<BiotaDiscoveryRecord>();
    public bool CanExitGalaxyMapWithoutTravel => !transitionInProgress;

    public bool EnsureStarterScanner(
        PlayerInventory inventory,
        InventoryItem scannerItem)
    {
        if (inventory == null || scannerItem == null)
            return false;
        bool alreadyOwned = inventory.CountById(scannerItem.ItemId) > 0;
        if (alreadyOwned)
        {
            if (activeSlotMetadata != null
                && activeSlotMetadata.starterEquipmentVersion < StarterEquipmentVersion)
            {
                activeSlotMetadata.starterEquipmentVersion = StarterEquipmentVersion;
                CaptureInventory();
                SaveActiveSlotMetadata();
            }
            return true;
        }
        if (activeSlotMetadata != null
            && activeSlotMetadata.starterEquipmentVersion >= StarterEquipmentVersion)
        {
            return false;
        }
        if (!inventory.TryAddUniqueById(scannerItem))
        {
            Debug.LogWarning(
                "GalaxyTravelManager: the scanner could not be granted because the inventory is full.",
                this);
            return false;
        }
        if (activeSlotMetadata != null)
        {
            activeSlotMetadata.starterEquipmentVersion = StarterEquipmentVersion;
            CaptureInventory();
            SaveActiveSlotMetadata();
        }
        return true;
    }

    public bool RegisterBiotaDiscovery(
        string stableId,
        BiotaDiscoveryType discoveryType,
        string displayName,
        string description)
    {
        if (activeSlotMetadata == null || string.IsNullOrWhiteSpace(stableId))
            return false;
        string normalizedId = BiotaScannable.NormalizeId(
            stableId,
            discoveryType,
            displayName);
        BiotaDiscoveryRecord[] existing = activeSlotMetadata.biotaCodex
            ?? System.Array.Empty<BiotaDiscoveryRecord>();
        for (int index = 0; index < existing.Length; index++)
        {
            BiotaDiscoveryRecord record = existing[index];
            if (record != null
                && string.Equals(
                    record.stableId,
                    normalizedId,
                    System.StringComparison.Ordinal))
            {
                return false;
            }
        }

        GalaxyPlanetDefinition planet = CurrentPlanet;
        var discovery = new BiotaDiscoveryRecord
        {
            stableId = normalizedId,
            discoveryType = discoveryType,
            displayName = string.IsNullOrWhiteSpace(displayName)
                ? normalizedId
                : displayName.Trim(),
            description = description != null ? description.Trim() : string.Empty,
            planetId = planet != null ? planet.planetId : currentPlanetId,
            planetName = planet != null
                ? GetPlanetDisplayName(planet)
                : currentPlanetId,
            discoveredUtcTicks = System.DateTime.UtcNow.Ticks
        };
        var updated = new BiotaDiscoveryRecord[existing.Length + 1];
        System.Array.Copy(existing, updated, existing.Length);
        updated[existing.Length] = discovery;
        activeSlotMetadata.biotaCodex = updated;
        SaveActiveSlotMetadata();
        return true;
    }

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
        foreach (GalaxyPlanetDefinition planet in planets)
        {
            if (planet != null && PlanetClimateClassifier.TryGetFixedClimate(planet.planetId, out PlanetClimate fixedClimate))
                planet.climate = fixedClimate;
            if (planet.weather == null || planet.weather.presets == null || planet.weather.presets.Count == 0)
                planet.weather = PlanetWeatherDefaults.Create(planet.planetId);
        }
        LoadActiveSaveSlot();
        shipFacing = NormalizeShipFacing(activeSlotMetadata.shipFacing);
        BuildRuntimeResourceCatalog();
        IndexFrozenPlanetDefinitions();
        ApplyWorldSeed(activeSlotMetadata.worldSeed);
        if (IsInterstellarGalaxy)
        {
            interstellarGenerator = new ProceduralInterstellarGenerator(activeSlotMetadata.worldSeed, runtimeResourceCatalog);
            currentPlanetCoordinate3D = new InterstellarCoordinate(
                activeSlotMetadata.currentPlanetCoordinateX,
                activeSlotMetadata.currentPlanetCoordinateY,
                activeSlotMetadata.currentPlanetCoordinateZ);
            VisitedPlanetRecord visitedCurrent = FindVisitedPlanet(currentPlanetCoordinate3D);
            string currentCoordinateId = visitedCurrent != null
                ? visitedCurrent.planetId
                : ProceduralInterstellarGenerator.EncodePlanetId(currentPlanetCoordinate3D);
            if (visitedCurrent == null && !frozenPlanetIds.Contains(currentCoordinateId)
                && !interstellarGenerator.HasPlanet(currentPlanetCoordinate3D))
                currentPlanetCoordinate3D = InterstellarCoordinate.Zero;
            visitedCurrent = FindVisitedPlanet(currentPlanetCoordinate3D);
            currentPlanetId = visitedCurrent != null
                ? visitedCurrent.planetId
                : ProceduralInterstellarGenerator.EncodePlanetId(currentPlanetCoordinate3D);
        }
        else if (IsInfiniteGalaxy)
        {
            proceduralGenerator = new ProceduralGalaxyGenerator(activeSlotMetadata.worldSeed, runtimeResourceCatalog);
            currentPlanetCoordinate = new GalaxyCoordinate(
                activeSlotMetadata.currentPlanetCoordinateX,
                activeSlotMetadata.currentPlanetCoordinateY);
            string currentCoordinateId = ProceduralGalaxyGenerator.EncodePlanetId(currentPlanetCoordinate);
            if (!frozenPlanetIds.Contains(currentCoordinateId)
                && !proceduralGenerator.HasPlanet(currentPlanetCoordinate))
                currentPlanetCoordinate = GalaxyCoordinate.Zero;
            currentPlanetId = ProceduralGalaxyGenerator.EncodePlanetId(currentPlanetCoordinate);
        }
        else
        {
            currentPlanetId = string.IsNullOrWhiteSpace(activeSlotMetadata.currentPlanetId)
                ? "origin"
                : activeSlotMetadata.currentPlanetId;
        }
        InitializePlanetLayout();
        RestoreShipPositionFromMetadata();
        LoadInventoryFromMetadata();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        if (instance == this)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            instance = null;
        }
    }

    void Start()
    {
        ConfigureSurfaceScene(SceneManager.GetActiveScene());
    }

    void Update()
    {
        if (activeSlotMetadata != null && Time.deltaTime > 0f)
        {
            activeSlotMetadata.weatherTimeSeconds += Time.deltaTime;
            activeSlotMetadata.universeTimeSeconds += Time.deltaTime;
        }
    }

    void OnApplicationQuit()
    {
        if (SceneManager.GetActiveScene().name == surfaceSceneName)
        {
            InfinitePlanarSurfaceWorld planar =
                FindObjectOfType<InfinitePlanarSurfaceWorld>();
            if (planar != null)
                SavePlanet(planar);
            VoxelQuadSphereWorld world = FindObjectOfType<VoxelQuadSphereWorld>();
            if (world != null && planar == null)
                SavePlanet(world);
            CaptureInventory();
        }
        SaveActiveSlotMetadata();
    }

    public void OpenGalaxyMap(VoxelQuadSphereWorld world)
    {
        if (transitionInProgress || (world == null && !IsInterstellarGalaxy))
            return;

        galaxyMapReturnSceneName = SceneManager.GetActiveScene().name;
        if (world != null)
        {
            SavePlanet(world);
            CaptureInventory();
        }
        GalaxyPlanetDefinition current = CurrentPlanet;
        if (current != null)
        {
            if (IsInterstellarGalaxy)
                shipCoordinate3D = current.coordinate3D;
            else if (IsInfiniteGalaxy)
                shipCoordinate = current.coordinate;
            else
                shipGridPosition = current.gridPosition;
        }
        SaveActiveSlotMetadata();

        transitionInProgress = true;
        SceneManager.LoadScene(mapSceneName, LoadSceneMode.Single);
    
}

    public void OpenGalaxyMapFromSurface(
        IPlanetSurfaceRuntime surfaceRuntime)
    {
        if (surfaceRuntime == null)
            return;
        if (surfaceRuntime.Topology == PlanetSurfaceTopology.LegacySphere)
        {
            OpenGalaxyMap(FindObjectOfType<VoxelQuadSphereWorld>());
            return;
        }
        if (transitionInProgress)
            return;

        InfinitePlanarSurfaceWorld planar =
            surfaceRuntime as InfinitePlanarSurfaceWorld;
        if (planar != null)
            SavePlanet(planar);
        CaptureInventory();
        galaxyMapReturnSceneName = SceneManager.GetActiveScene().name;
        GalaxyPlanetDefinition current = CurrentPlanet;
        if (current != null)
        {
            if (IsInterstellarGalaxy)
                shipCoordinate3D = current.coordinate3D;
            else if (IsInfiniteGalaxy)
                shipCoordinate = current.coordinate;
            else
                shipGridPosition = current.gridPosition;
        }
        SaveActiveSlotMetadata();
        transitionInProgress = true;
        SceneManager.LoadScene(mapSceneName, LoadSceneMode.Single);
    }

    public bool ExitGalaxyMapWithoutTravel()
    {
        if (!CanExitGalaxyMapWithoutTravel)
            return false;

        string targetScene = galaxyMapReturnSceneName;
        if (string.IsNullOrWhiteSpace(targetScene) || targetScene == mapSceneName)
            targetScene = IsInterstellarGalaxy ? interstellarSceneName : surfaceSceneName;
        if (string.IsNullOrWhiteSpace(targetScene)
            || !Application.CanStreamedLevelBeLoaded(targetScene))
        {
            Debug.LogError($"GalaxyTravelManager: 无法返回来源场景“{targetScene}”。", this);
            return false;
        }

        transitionInProgress = true;
        SceneManager.LoadScene(targetScene, LoadSceneMode.Single);
        return true;
    }

    public void OpenInterstellarFlight(VoxelQuadSphereWorld world)
    {
        GalaxyPlanetDefinition current = CurrentPlanet;
        Vector3 direction = current != null
            ? GetLastLandingDirection(current.planetId)
            : Vector3.up;
        OpenInterstellarFlight(
            world,
            direction,
            Quaternion.LookRotation(
                Vector3.ProjectOnPlane(Vector3.forward, direction).sqrMagnitude > 0.001f
                    ? Vector3.ProjectOnPlane(Vector3.forward, direction).normalized
                    : Vector3.Cross(direction, Vector3.right).normalized,
                direction),
            direction * 120f);
    }

    public void OpenInterstellarFlight(
        VoxelQuadSphereWorld world,
        Vector3 departureDirection,
        Quaternion departureRotation,
        Vector3 departureVelocity)
    {
        if (transitionInProgress || world == null || !IsInterstellarGalaxy)
            return;

        SavePlanet(world);
        CaptureInventory();
        PendingSurfaceDepartureContext.Clear();
        GalaxyPlanetDefinition current = CurrentPlanet;
        if (current != null)
        {
            Vector3 direction = departureDirection.sqrMagnitude > 0.001f
                ? departureDirection.normalized
                : GetLastLandingDirection(current.planetId);
            shipCoordinate3D = current.coordinate3D;
            activeSlotMetadata.nearObservationPlanetId = current.planetId;
            UniversePosition planetPosition = GetInterstellarPlanetAddress(current.coordinate3D);
            PlanetCelestialProfile celestial = current.celestial ?? PlanetCelestialProfile.CreateCompatibleDefault();
            celestial.ClampValues();
            PlanetPhysicalProfile physical = celestial.Physical;
            double outerRadius = physical.radiusMeters + System.Math.Max(
                celestial.maximumTerrainElevation,
                celestial.HasAtmosphere ? physical.atmosphereTopAltitudeMeters : 0d);
            double departureDistance = System.Math.Max(
                physical.radiusMeters * 4.2d,
                outerRadius + 2_500d);
            StoreUniversePosition(planetPosition.Add(new DoubleVector3(
                direction.x * departureDistance,
                direction.y * departureDistance,
                direction.z * departureDistance)));
            Vector3 orbitalVelocity = GetInterstellarPlanetVelocity(current.coordinate3D);
            Vector3 relativeDepartureVelocity = departureVelocity.sqrMagnitude > 0.001f
                ? departureVelocity
                : direction * 120f;
            PendingSurfaceDepartureContext.Set(
                departureRotation,
                orbitalVelocity + relativeDepartureVelocity);
        }
        SaveActiveSlotMetadata();
        transitionInProgress = true;
        SceneManager.LoadScene(interstellarSceneName, LoadSceneMode.Single);
    }

    public void OpenInterstellarFlightFromSurface(
        IPlanetSurfaceRuntime surfaceRuntime,
        Vector3 planarForward,
        Vector3 departureVelocity)
    {
        if (transitionInProgress
            || surfaceRuntime == null
            || surfaceRuntime.Topology
                != PlanetSurfaceTopology.InfinitePlanar
            || !IsInterstellarGalaxy)
        {
            return;
        }

        InfinitePlanarSurfaceWorld planar =
            surfaceRuntime as InfinitePlanarSurfaceWorld;
        if (planar != null)
            SavePlanet(planar);
        CaptureInventory();
        PendingSurfaceDepartureContext.Clear();

        GalaxyPlanetDefinition current = CurrentPlanet;
        if (current != null)
        {
            Vector3 direction =
                surfaceRuntime.AnchorDirection.sqrMagnitude > 0.001f
                    ? surfaceRuntime.AnchorDirection.normalized
                    : GetLastLandingDirection(current.planetId);
            PlanetLabPlanarPatchMeshBuilder.BuildTangentBasis(
                direction,
                out Vector3 east,
                out Vector3 north);
            Vector3 flatForward = Vector3.ProjectOnPlane(
                planarForward,
                Vector3.up).normalized;
            if (flatForward.sqrMagnitude < 0.001f)
                flatForward = Vector3.forward;
            Vector3 tangentForward =
                (east * flatForward.x + north * flatForward.z)
                .normalized;
            if (tangentForward.sqrMagnitude < 0.001f)
                tangentForward = north;
            Quaternion departureRotation =
                Quaternion.LookRotation(direction, tangentForward);

            shipCoordinate3D = current.coordinate3D;
            activeSlotMetadata.nearObservationPlanetId =
                current.planetId;
            UniversePosition planetPosition =
                GetInterstellarPlanetAddress(current.coordinate3D);
            PlanetCelestialProfile celestial =
                current.celestial
                ?? PlanetCelestialProfile.CreateCompatibleDefault();
            celestial.ClampValues();
            PlanetPhysicalProfile physical = celestial.Physical;
            double outerRadius =
                physical.radiusMeters
                + System.Math.Max(
                    celestial.maximumTerrainElevation,
                    celestial.HasAtmosphere
                        ? physical.atmosphereTopAltitudeMeters
                        : 0d);
            double departureDistance = System.Math.Max(
                physical.radiusMeters * 4.2d,
                outerRadius + 2_500d);
            StoreUniversePosition(
                planetPosition.Add(new DoubleVector3(
                    direction.x * departureDistance,
                    direction.y * departureDistance,
                    direction.z * departureDistance)));
            Vector3 orbitalVelocity =
                GetInterstellarPlanetVelocity(current.coordinate3D);
            float speed = Mathf.Max(
                120f,
                departureVelocity.magnitude);
            PendingSurfaceDepartureContext.Set(
                departureRotation,
                orbitalVelocity + direction * speed);
        }
        SaveActiveSlotMetadata();
        transitionInProgress = true;
        SceneManager.LoadScene(
            interstellarSceneName,
            LoadSceneMode.Single);
    }

    public SurfaceSpacecraftState GetSurfaceSpacecraftState(string planetId)
    {
        VisitedPlanetRecord visited = FindVisitedPlanet(planetId);
        if (visited == null || visited.surfaceSpacecraft == null)
            return null;
        SurfaceSpacecraftState state = visited.surfaceSpacecraft.Clone();
        state.ClampValues();
        return state;
    }

    public void UpdateSurfaceSpacecraftState(
        string planetId,
        SurfaceSpacecraftState state,
        bool flushToDisk)
    {
        if (!IsInterstellarGalaxy
            || activeSlotMetadata == null
            || string.IsNullOrWhiteSpace(planetId)
            || state == null)
        {
            return;
        }

        VisitedPlanetRecord visited = FindVisitedPlanet(planetId);
        if (visited == null)
            return;
        SurfaceSpacecraftState copy = state.Clone();
        copy.ClampValues();
        visited.surfaceSpacecraft = copy;
        if (copy.valid)
            visited.lastLandingDirection = copy.radialDirection;
        if (flushToDisk)
            SaveActiveSlotMetadata();
    }

    public void MoveShip(Vector2Int delta)
    {
MoveShip(new GalaxyCoordinateDelta(delta.x, delta.y));
    
}

    public void MoveShip(GalaxyCoordinateDelta delta)
    {
        bool isCardinalUnit = (delta.x == 0 && (delta.y == -1 || delta.y == 1))
            || (delta.y == 0 && (delta.x == -1 || delta.x == 1));
        if (!isCardinalUnit)
            return;

        shipFacing = GetFacing(delta);
        activeSlotMetadata.shipFacing = shipFacing;
        if (IsInfiniteGalaxy)
        {
            shipCoordinate = shipCoordinate.Offset(delta.x, delta.y);
            activeSlotMetadata.shipCoordinateX = shipCoordinate.x;
            activeSlotMetadata.shipCoordinateY = shipCoordinate.y;
        }
        else
        {
            shipGridPosition = new Vector2Int(
                Mathf.Clamp(shipGridPosition.x + delta.x, 0, GridColumns - 1),
                Mathf.Clamp(shipGridPosition.y + delta.y, 0, GridRows - 1));
            activeSlotMetadata.shipGridX = shipGridPosition.x;
            activeSlotMetadata.shipGridY = shipGridPosition.y;
        }
    
}

    static GalaxyShipFacing GetFacing(GalaxyCoordinateDelta delta)
    {
        if (delta.y > 0)
            return GalaxyShipFacing.Up;
        if (delta.x > 0)
            return GalaxyShipFacing.Right;
        if (delta.y < 0)
            return GalaxyShipFacing.Down;
        return GalaxyShipFacing.Left;
    }

    static GalaxyShipFacing NormalizeShipFacing(GalaxyShipFacing facing)
    {
        return facing >= GalaxyShipFacing.Up && facing <= GalaxyShipFacing.Left
            ? facing
            : GalaxyShipFacing.Up;
    }

    public GalaxyPlanetDefinition GetPlanetAt(GalaxyCoordinate coordinate)
    {
if (!IsInfiniteGalaxy)
        {
            if (coordinate.x < int.MinValue || coordinate.x > int.MaxValue
                || coordinate.y < int.MinValue || coordinate.y > int.MaxValue)
                return null;
            return GetPlanetAt(new Vector2Int((int)coordinate.x, (int)coordinate.y));
        }

        if (proceduralPlanetCache.TryGetValue(coordinate, out GalaxyPlanetDefinition cached))
            return ApplyVisitedDisplayName(cached);

        string planetId = ProceduralGalaxyGenerator.EncodePlanetId(coordinate);
        GalaxyPlanetDefinition planet = frozenPlanetIds.Contains(planetId)
            ? LoadFrozenPlanetDefinition(coordinate)
            : null;
        if (planet == null)
        {
            if (proceduralGenerator == null || !proceduralGenerator.HasPlanet(coordinate))
                return null;
            planet = proceduralGenerator.GeneratePlanet(coordinate);
        }
        planet = ApplyVisitedDisplayName(planet);
        if (planet != null)
            CacheProceduralPlanet(coordinate, planet);
        return planet;
    
}

    public GalaxyPlanetDefinition GetPlanetAt(InterstellarCoordinate coordinate)
    {
        if (!IsInterstellarGalaxy)
            return null;
        EnsureInterstellarGenerator();
        if (interstellarPlanetCache.TryGetValue(coordinate, out GalaxyPlanetDefinition cached))
            return ApplyVisitedDisplayName(cached, coordinate);

        VisitedPlanetRecord visited = FindVisitedPlanet(coordinate);
        string planetId = visited != null
            ? visited.planetId
            : ProceduralInterstellarGenerator.EncodePlanetId(coordinate);
        GalaxyPlanetDefinition planet = frozenPlanetIds.Contains(planetId)
            ? LoadFrozenPlanetDefinition(coordinate)
            : null;
        if (planet == null)
        {
            if (interstellarGenerator == null || !interstellarGenerator.HasPlanet(coordinate))
                return null;
            planet = interstellarGenerator.GeneratePlanet(coordinate);
        }
        planet = ApplyVisitedDisplayName(planet, coordinate);
        if (planet != null)
            CacheProceduralPlanet(coordinate, planet);
        return planet;
    }

    public DoubleVector3 GetInterstellarPlanetPosition(InterstellarCoordinate coordinate)
    {
        EnsureInterstellarGenerator();
        return interstellarGenerator != null
            ? interstellarGenerator.GetPlanetUniversePosition(coordinate, UniverseTimeSeconds)
            : DoubleVector3.Zero;
    }

    public UniversePosition GetInterstellarPlanetAddress(InterstellarCoordinate coordinate)
    {
        EnsureInterstellarGenerator();
        return interstellarGenerator != null
            ? interstellarGenerator.GetPlanetUniverseAddress(coordinate, UniverseTimeSeconds)
            : default;
    }

    public Vector3 GetInterstellarPlanetVelocity(InterstellarCoordinate coordinate)
    {
        EnsureInterstellarGenerator();
        if (interstellarGenerator == null)
            return Vector3.zero;
        CelestialBodyState state = interstellarGenerator.GetPlanetBarycentricState(
            coordinate,
            UniverseTimeSeconds);
        return state.velocityMetersPerSecond.ToVector3();
    }

    public StellarSystemDefinition GetInterstellarSystem(
        InterstellarCoordinate systemCoordinate)
    {
        EnsureInterstellarGenerator();
        return interstellarGenerator?.GenerateSystem(systemCoordinate);
    }

    public bool TryGetCelestialBodyState(
        string bodyId,
        double universeTimeSeconds,
        out CelestialBodyState state)
    {
        state = default;
        if (!IsInterstellarGalaxy || string.IsNullOrEmpty(bodyId))
            return false;
        VisitedPlanetRecord visited = FindVisitedPlanet(bodyId);
        InterstellarCoordinate coordinate;
        if (visited != null)
            coordinate = visited.Coordinate;
        else if (!ProceduralInterstellarGenerator.TryDecodePlanetId(bodyId, out coordinate))
            return false;

        EnsureInterstellarGenerator();
        if (interstellarGenerator == null)
            return false;
        state = interstellarGenerator.GetPlanetBarycentricState(
            coordinate,
            universeTimeSeconds);
        state.universePosition = interstellarGenerator.GetPlanetUniverseAddress(
            coordinate,
            universeTimeSeconds);
        return true;
    }

    public InterstellarCoordinate GetInterstellarScanCoordinate(UniversePosition position)
    {
        EnsureInterstellarGenerator();
        return interstellarGenerator != null
            ? interstellarGenerator.GetRepresentativePlanetCoordinate(position.systemCoordinate)
            : InterstellarCoordinate.Zero;
    }

    void EnsureInterstellarGenerator()
    {
        if (!IsInterstellarGalaxy || interstellarGenerator != null || activeSlotMetadata == null)
            return;

        interstellarGenerator = new ProceduralInterstellarGenerator(
            activeSlotMetadata.worldSeed,
            runtimeResourceCatalog);
    }

    public GalaxyPlanetDefinition GetPlanetAt(Vector2Int gridPosition)
    {
foreach (GalaxyPlanetDefinition planet in planets)
        {
            if (planet.gridPosition == gridPosition)
                return planet;
        }

        return null;
    
}

    public void EnterPlanet(GalaxyPlanetDefinition planet)
    {
if (transitionInProgress || planet == null)
            return;

        if (IsInterstellarGalaxy)
        {
            try
            {
                FreezePlanetDefinition(planet);
            }
            catch (System.Exception exception)
            {
                Debug.LogError($"GalaxyTravelManager: could not freeze interstellar planet definition. {exception.Message}", this);
                return;
            }
            currentPlanetCoordinate3D = planet.coordinate3D;
            shipCoordinate3D = planet.coordinate3D;
        }
        else if (IsInfiniteGalaxy)
        {
            try
            {
                FreezePlanetDefinition(planet);
            }
            catch (System.Exception exception)
            {
                Debug.LogError($"GalaxyTravelManager: could not freeze planet definition. {exception.Message}", this);
                return;
            }
            currentPlanetCoordinate = planet.coordinate;
            shipCoordinate = planet.coordinate;
        }
        else
        {
            shipGridPosition = planet.gridPosition;
        }
        currentPlanetId = planet.planetId;
        SaveActiveSlotMetadata();
        transitionInProgress = true;
        SceneManager.LoadScene(surfaceSceneName, LoadSceneMode.Single);
    
}

    public void BeginPlanetApproach(
        GalaxyPlanetDefinition planet,
        Vector3 relativePosition,
        Vector3 inertialVelocity,
        Quaternion shipRotation,
        bool automaticLanding = false)
    {
        if (transitionInProgress || !IsInterstellarGalaxy || planet == null)
            return;

        try
        {
            FreezePlanetDefinition(planet);
        }
        catch (System.Exception exception)
        {
            Debug.LogError($"GalaxyTravelManager: could not freeze approach target. {exception.Message}", this);
            return;
        }

        currentPlanetCoordinate3D = planet.coordinate3D;
        currentPlanetId = planet.planetId;
        PlanetApproachContext.Set(
            planet,
            relativePosition,
            inertialVelocity,
            shipRotation,
            SpacecraftHullIntegrity,
            automaticLanding);
        SaveActiveSlotMetadata();
        transitionInProgress = true;
        SceneManager.LoadScene(approachSceneName, LoadSceneMode.Single);
    }

    public void CompletePlanetLanding(PendingPlanetLandingContext context)
    {
        if (transitionInProgress || !IsInterstellarGalaxy || context == null || string.IsNullOrWhiteSpace(context.planetId))
            return;

        currentPlanetId = context.planetId;
        currentPlanetCoordinate3D = context.coordinate;
        shipCoordinate3D = context.coordinate;
        AddOrUpdateVisitedPlanet(context);
        PendingPlanetLandingContext.Set(context);
        SaveActiveSlotMetadata();
        transitionInProgress = true;
        SceneManager.LoadScene(surfaceSceneName, LoadSceneMode.Single);
    }

    public bool EnterPlanetSurfaceDirect(
        GalaxyPlanetDefinition planet,
        Vector3 landingDirection,
        Quaternion shipRotation,
        float hullIntegrity)
    {
        if (transitionInProgress || !IsInterstellarGalaxy || planet == null)
            return false;

        try
        {
            FreezePlanetDefinition(planet);
        }
        catch (System.Exception exception)
        {
            Debug.LogError($"GalaxyTravelManager: could not freeze direct-entry planet. {exception.Message}", this);
            return false;
        }

        Vector3 direction = landingDirection.sqrMagnitude > 0.001f
            ? landingDirection.normalized
            : Vector3.up;
        PlanetCelestialProfile celestial = planet.celestial ?? PlanetCelestialProfile.CreateCompatibleDefault();
        celestial.ClampValues();
        PlanetLandingResolution landingSite = PlanetLandingSiteResolver.Resolve(
            planet,
            direction);
        var context = new PendingPlanetLandingContext
        {
            planetId = planet.planetId,
            coordinate = planet.coordinate3D,
            landingDirection = direction,
            shipRotation = shipRotation,
            playerLocalPosition = direction * (celestial.radius + 3f),
            landingPointLocal = direction * celestial.radius,
            landingGroundNormal = direction,
            hullIntegrity = Mathf.Clamp(hullIntegrity, 0f, 100f),
            landingMode = landingSite.mode
        };

        currentPlanetId = planet.planetId;
        currentPlanetCoordinate3D = planet.coordinate3D;
        shipCoordinate3D = planet.coordinate3D;
        AddOrUpdateVisitedPlanet(context);
        PendingPlanetLandingContext.Set(context);
        PlanetApproachContext.Clear();
        SaveActiveSlotMetadata();
        transitionInProgress = true;
        SceneManager.LoadScene(surfaceSceneName, LoadSceneMode.Single);
        return true;
    }

    public bool FastTravelToVisitedPlanet(string planetId)
    {
        if (transitionInProgress || !IsInterstellarGalaxy || string.IsNullOrWhiteSpace(planetId))
            return false;
        VisitedPlanetRecord visited = FindVisitedPlanet(planetId);
        if (visited == null)
            return false;
        currentPlanetId = visited.planetId;
        currentPlanetCoordinate3D = visited.Coordinate;
        shipCoordinate3D = visited.Coordinate;
        PendingPlanetLandingContext.Set(new PendingPlanetLandingContext
        {
            planetId = visited.planetId,
            coordinate = visited.Coordinate,
            landingDirection = visited.lastLandingDirection.sqrMagnitude > 0.001f
                ? visited.lastLandingDirection.normalized
                : Vector3.up,
            playerLocalPosition = visited.lastPlayerLocalPosition,
            hullIntegrity = SpacecraftHullIntegrity,
            landingMode = PlanetLandingMode.Auto,
            restoreSavedSpacecraftState = true
        });
        SaveActiveSlotMetadata();
        transitionInProgress = true;
        SceneManager.LoadScene(surfaceSceneName, LoadSceneMode.Single);
        return true;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        transitionInProgress = false;
        ConfigureSurfaceScene(scene);
    }

    public void UpdateInterstellarFlightState(
        InterstellarCoordinate shipSector,
        DoubleVector3 universePosition,
        float hullIntegrity)
    {
        if (!IsInterstellarGalaxy || activeSlotMetadata == null)
            return;
        shipCoordinate3D = shipSector;
        StoreUniversePosition(UniversePosition.FromAbsoluteMeters(universePosition));
        activeSlotMetadata.spacecraftHullIntegrity = Mathf.Clamp(hullIntegrity, 0f, 100f);
    }

    public void UpdateInterstellarFlightState(
        InterstellarCoordinate shipSector,
        UniversePosition universePosition,
        float hullIntegrity)
    {
        if (!IsInterstellarGalaxy || activeSlotMetadata == null)
            return;
        shipCoordinate3D = shipSector;
        StoreUniversePosition(universePosition);
        activeSlotMetadata.spacecraftHullIntegrity = Mathf.Clamp(hullIntegrity, 0f, 100f);
    }

    void StoreUniversePosition(UniversePosition position)
    {
        if (activeSlotMetadata == null)
            return;
        activeSlotMetadata.hasHierarchicalSpacePosition = true;
        activeSlotMetadata.spaceSystemX = position.systemCoordinate.x;
        activeSlotMetadata.spaceSystemY = position.systemCoordinate.y;
        activeSlotMetadata.spaceSystemZ = position.systemCoordinate.z;
        activeSlotMetadata.spaceLocalX = position.localMeters.x;
        activeSlotMetadata.spaceLocalY = position.localMeters.y;
        activeSlotMetadata.spaceLocalZ = position.localMeters.z;
        DoubleVector3 legacy = position.ToAbsoluteMeters();
        activeSlotMetadata.spacePositionX = legacy.x;
        activeSlotMetadata.spacePositionY = legacy.y;
        activeSlotMetadata.spacePositionZ = legacy.z;
    }

    public void SetNearObservationPlanet(string planetId, bool flushToDisk)
    {
        if (!IsInterstellarGalaxy || activeSlotMetadata == null)
            return;
        activeSlotMetadata.nearObservationPlanetId =
            string.IsNullOrWhiteSpace(planetId) ? string.Empty : planetId;
        if (flushToDisk)
            SaveActiveSlotMetadata();
    }

    public void FlushInterstellarFlightState()
    {
        SaveActiveSlotMetadata();
    }

    public void RecoverFromSpacecraftDestruction()
    {
        if (transitionInProgress || !IsInterstellarGalaxy)
            return;
        activeSlotMetadata.spacecraftHullIntegrity = 50f;
        shipCoordinate3D = currentPlanetCoordinate3D;
        UniversePosition planetPosition = GetInterstellarPlanetAddress(currentPlanetCoordinate3D);
        GalaxyPlanetDefinition current = CurrentPlanet;
        PlanetCelestialProfile celestial = current != null && current.celestial != null
            ? current.celestial
            : PlanetCelestialProfile.CreateCompatibleDefault();
        celestial.ClampValues();
        double recoveryDistance = celestial.Physical.radiusMeters * 4.2d;
        StoreUniversePosition(planetPosition.Add(new DoubleVector3(0d, 0d, recoveryDistance)));
        SaveActiveSlotMetadata();
        transitionInProgress = true;
        SceneManager.LoadScene(surfaceSceneName, LoadSceneMode.Single);
    }

    public void ReturnToMainMenu(string startMenuSceneName)
    {
if (transitionInProgress)
            return;

        InfinitePlanarSurfaceWorld planar =
            FindObjectOfType<InfinitePlanarSurfaceWorld>();
        if (planar != null)
            SavePlanet(planar);
        VoxelQuadSphereWorld world = FindObjectOfType<VoxelQuadSphereWorld>();
        if (world != null && planar == null)
            SavePlanet(world);
        CaptureInventory();
        SaveActiveSlotMetadata();

        transitionInProgress = true;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        instance = null;
        Destroy(gameObject);
        SceneManager.LoadScene(startMenuSceneName, LoadSceneMode.Single);
    
}

    void ConfigureSurfaceScene(Scene scene)
    {
        if (scene.name != surfaceSceneName)
            return;

        GalaxyPlanetDefinition planet = CurrentPlanet;
        if (planet == null)
            return;

        if (IsInfiniteGalaxy || IsInterstellarGalaxy)
        {
            try
            {
                FreezePlanetDefinition(planet);
            }
            catch (System.Exception exception)
            {
                Debug.LogError($"GalaxyTravelManager: could not freeze the current planet definition. {exception.Message}", this);
            }
        }

        if (UsesInfinitePlanarSurface)
        {
            ConfigureInfinitePlanarSurfaceScene(planet);
            return;
        }

        VoxelQuadSphereWorld world = FindObjectOfType<VoxelQuadSphereWorld>();
        if (world == null)
            return;
        LegacySphereSurfaceRuntime sphereRuntime =
            world.GetComponent<LegacySphereSurfaceRuntime>()
            ?? world.gameObject.AddComponent<LegacySphereSurfaceRuntime>();
        sphereRuntime.Configure(world);
        GalaxyPlanetSaveData save = LoadPlanet(planet.planetId);
        GetPlanetPalette(planet, out Color surfaceColor, out Color rockColor);
        List<PlanetSurfacePropSpawnSettings> surfacePropPlan = decorationCatalog != null
            ? decorationCatalog.BuildSpawnPlan(planet.lowPolyVisual, planet.climate, planet.seed)
            : new List<PlanetSurfacePropSpawnSettings>();
        world.ConfigurePlanet(
            planet.seed,
            save,
            surfaceColor,
            rockColor,
            planet.terrain,
            planet.spawnHarvestableResources,
            planet.resourceSpawnSettings,
            surfacePropPlan,
            planet.rivers,
            save != null ? save.riverData : null,
            planet.celestial ?? PlanetCelestialProfile.CreateCompatibleDefault());
        world.ConfigureVisualProfile(planet.lowPolyVisual);
        PlanetSurfaceDayNightController dayNight = world.GetComponent<PlanetSurfaceDayNightController>()
            ?? world.gameObject.AddComponent<PlanetSurfaceDayNightController>();
        dayNight.Configure(
            world,
            planet.celestial ?? PlanetCelestialProfile.CreateCompatibleDefault(),
            planet.lowPolyVisual);
        PlanetSurfaceFarLodController farLod = world.GetComponent<PlanetSurfaceFarLodController>()
            ?? world.gameObject.AddComponent<PlanetSurfaceFarLodController>();
        farLod.Configure(world, planet);
        PendingPlanetLandingContext landing = PendingPlanetLandingContext.Consume();
        bool requiresLandingRestore = landing != null && IsInterstellarGalaxy;
        PlanetLoadingUI loadingUI = FindObjectOfType<PlanetLoadingUI>(true);
        PlanetSurfaceEntryCoordinator entryCoordinator =
            world.GetComponent<PlanetSurfaceEntryCoordinator>()
            ?? world.gameObject.AddComponent<PlanetSurfaceEntryCoordinator>();
        entryCoordinator.Initialize(
            world,
            loadingUI,
            requiresLandingRestore);

        if (requiresLandingRestore)
        {
            world.SetSurfaceSpawnDirection(landing.landingDirection);
            SurfaceLandedSpacecraftRestorer restorer = world.GetComponent<SurfaceLandedSpacecraftRestorer>()
                ?? world.gameObject.AddComponent<SurfaceLandedSpacecraftRestorer>();
            restorer.Initialize(world, landing);
            entryCoordinator.BindRestorer(restorer);
        }
        PlanetWeatherSystem weatherSystem = FindObjectOfType<PlanetWeatherSystem>();
        weatherSystem?.ConfigureVisualProfile(planet.lowPolyVisual);
        weatherSystem?.Configure(world, planet.weather, planet.seed, planet.planetId);
        RestoreBuildings(world, save);
        entryCoordinator.MarkBuildingsRestored();
        RestoreInventory();
    }

    void ConfigureInfinitePlanarSurfaceScene(
        GalaxyPlanetDefinition planet)
    {
        if (FindObjectOfType<InfinitePlanarSurfaceWorld>() != null)
            return;

        VoxelQuadSphereWorld sphere =
            FindObjectOfType<VoxelQuadSphereWorld>();
        if (sphere != null)
        {
            sphere.enabled = false;
            PlanetRiverSystem river =
                sphere.GetComponent<PlanetRiverSystem>();
            if (river != null)
                river.enabled = false;
            PlanetSurfaceDayNightController dayNight =
                sphere.GetComponent<PlanetSurfaceDayNightController>();
            if (dayNight != null)
                dayNight.enabled = false;
            PlanetSurfaceFarLodController farLod =
                sphere.GetComponent<PlanetSurfaceFarLodController>();
            if (farLod != null)
                farLod.enabled = false;
        }

        foreach (VoxelQuadSphereDigTool dig
                 in FindObjectsOfType<VoxelQuadSphereDigTool>(true))
        {
            dig.enabled = false;
        }
        foreach (VoxelDigTool dig in FindObjectsOfType<VoxelDigTool>(true))
            dig.enabled = false;
        foreach (PlanetWeatherSystem weather
                 in FindObjectsOfType<PlanetWeatherSystem>(true))
        {
            weather.gameObject.SetActive(false);
        }
        foreach (Canvas canvas in FindObjectsOfType<Canvas>(true))
        {
            if (canvas != null
                && canvas.name.IndexOf(
                    "Weather",
                    System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                canvas.gameObject.SetActive(false);
            }
        }

        PendingPlanetLandingContext landing =
            PendingPlanetLandingContext.Consume();
        GalaxyPlanetSaveData save = LoadPlanet(planet.planetId);
        VoxelPlanetPlayerController player =
            FindObjectOfType<VoxelPlanetPlayerController>(true);
        if (player == null)
        {
            Debug.LogError(
                "GalaxyTravelManager: infinite planar surface requires VoxelPlanetPlayerController.",
                this);
            return;
        }

        var root = new GameObject("InfinitePlanarSurfaceWorld");
        InfinitePlanarSurfaceWorld planar =
            root.AddComponent<InfinitePlanarSurfaceWorld>();
        // Infinite planar saves deliberately do not run the random tree,
        // vegetation, landmark, or ground-cover generator. Dedicated
        // harvestable resource settings remain enabled.
        List<PlanetSurfacePropSpawnSettings> surfacePropPlan =
            new List<PlanetSurfacePropSpawnSettings>();
        planar.Configure(
            planet,
            save,
            landing,
            player,
            surfacePropPlan);
        InfinitePlanarSurfaceEntryCoordinator coordinator =
            root.AddComponent<InfinitePlanarSurfaceEntryCoordinator>();
        PlanarSurfaceLandedSpacecraftRestorer restorer = null;
        if (IsInterstellarGalaxy)
        {
            restorer = root.AddComponent<
                PlanarSurfaceLandedSpacecraftRestorer>();
            restorer.Initialize(planar, landing);
        }
        coordinator.Initialize(
            planar,
            FindObjectOfType<PlanetLoadingUI>(true));
        if (restorer != null)
            coordinator.BindRestorer(restorer);
        RestorePlanarBuildings(planar, save);
        RestoreInventory();
    }

    void SavePlanet(VoxelQuadSphereWorld world)
    {
        if (!world.IsGenerationComplete)
        {
            Debug.LogWarning("GalaxyTravelManager: skipped saving because planet generation is incomplete.", world);
            return;
        }

        GalaxyPlanetDefinition planet = CurrentPlanet;
        if (planet == null)
            return;

        List<QuadSphereChunkSaveEntry> chunks = world.IsStreamingLargePlanet
            ? world.GetModifiedChunkSnapshots()
            : world.GetCompleteChunkSnapshots();
        GalaxyPlanetSaveData data = new GalaxyPlanetSaveData
        {
            formatVersion = PlanetSaveVersion,
            planetId = planet.planetId,
            seed = planet.seed,
            hasFullVoxelSnapshot = !world.IsStreamingLargePlanet && chunks.Count == world.ExpectedChunkCount,
            faceGridSize = world.FaceGridSize,
            maxDepth = world.MaxDepth,
            chunkSize = VoxelTypes.ChunkSize,
            terrainConfigurationHash = world.TerrainConfigurationHash,
            terrainSettings = world.TerrainSettingsSnapshot,
            hasFullMeshSnapshot = !world.IsStreamingLargePlanet && world.HasCompleteMeshSnapshot,
            chunks = chunks.ToArray(),
            harvestedResourceIds = world.GetHarvestedResourceIds(),
            hasFullResourceSnapshot = true,
            resourceConfigurationHash = world.ResourceConfigurationHash,
            resources = world.GetResourceSnapshots(),
            harvestedSurfacePropIds = world.GetHarvestedSurfacePropIds(),
            hasFullSurfacePropSnapshot = true,
            surfacePropConfigurationHash = world.SurfacePropConfigurationHash,
            surfaceProps = world.GetSurfacePropSnapshots(),
            buildings = CaptureBuildings(world),
            riverData = world.RiverSystem != null ? world.RiverSystem.Snapshot : null,
            surfaceGenerationMode = world.IsStreamingLargePlanet
                ? PlanetSurfaceGenerationMode.StreamingLargeSphere
                : PlanetSurfaceGenerationMode.LegacyFullSphere,
            planetReferenceRadius = world.PlanetRadius,
            voxelOuterRadius = world.VoxelOuterRadius,
            surfaceTopology = PlanetSurfaceTopology.LegacySphere
        };

        string savePath = GetPlanetSavePath(planet.planetId);
        Directory.CreateDirectory(Path.GetDirectoryName(savePath));
        float saveStartedAt = Time.realtimeSinceStartup;
        WritePlanetBinary(savePath, data);
        float elapsedMilliseconds = (Time.realtimeSinceStartup - saveStartedAt) * 1000f;
        float fileMegabytes = new FileInfo(savePath).Length / (1024f * 1024f);
        Debug.Log(
            $"GalaxyTravelManager: saved planet '{planet.planetId}' with voxel and mesh snapshots "
            + $"({fileMegabytes:0.00} MB) in {elapsedMilliseconds:0} ms.",
            this);
    }

    void SavePlanet(InfinitePlanarSurfaceWorld world)
    {
        if (world == null || !world.IsCenterCollisionReady)
        {
            Debug.LogWarning(
                "GalaxyTravelManager: skipped planar save because the center collision is not ready.",
                world);
            return;
        }

        GalaxyPlanetDefinition planet = CurrentPlanet;
        if (planet == null)
            return;
        PlanetCelestialProfile celestial =
            planet.celestial
            ?? PlanetCelestialProfile.CreateCompatibleDefault();
        var data = new GalaxyPlanetSaveData
        {
            formatVersion = PlanetSaveVersion,
            planetId = planet.planetId,
            seed = planet.seed,
            hasFullVoxelSnapshot = false,
            faceGridSize = 0,
            maxDepth = 0,
            chunkSize =
                PlanetLabPlanarSettings.InfiniteChunkResolution,
            terrainSettings = planet.terrain != null
                ? planet.terrain.Clone()
                : new PlanetTerrainSettings(),
            hasFullMeshSnapshot = false,
            chunks = new QuadSphereChunkSaveEntry[0],
            hasFullResourceSnapshot = true,
            resources = new GalaxyResourceSaveEntry[0],
            harvestedSurfacePropIds = new string[0],
            hasFullSurfacePropSnapshot = true,
            surfaceProps = new GalaxySurfacePropSaveEntry[0],
            buildings = CapturePlanarBuildings(world),
            surfaceGenerationMode =
                PlanetSurfaceGenerationMode.InfinitePlanar,
            planetReferenceRadius = (float)celestial.radius,
            voxelOuterRadius = (float)celestial.radius,
            surfaceTopology =
                PlanetSurfaceTopology.InfinitePlanar
        };
        world.CapturePlanarState(data);

        string savePath = GetPlanetSavePath(planet.planetId);
        Directory.CreateDirectory(Path.GetDirectoryName(savePath));
        WritePlanetBinary(savePath, data);

        VisitedPlanetRecord visited = FindVisitedPlanet(planet.planetId);
        if (visited != null && data.hasPlanarPlayerPosition)
        {
            visited.hasPlanarPlayerPosition = true;
            visited.lastPlanarPlayerX = data.planarPlayerX;
            visited.lastPlanarPlayerZ = data.planarPlayerZ;
        }
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
            catch (InvalidDataException)
            {
                return null;
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
            GalaxyPlanetSaveData legacy = JsonUtility.FromJson<GalaxyPlanetSaveData>(File.ReadAllText(legacyPath));
            return legacy != null
                && (legacy.formatVersion == 10
                    || legacy.formatVersion == 11
                    || legacy.formatVersion == 12
                    || legacy.formatVersion == 13
                    || legacy.formatVersion == 14
                    || legacy.formatVersion == PlanetSaveVersion)
                ? legacy
                : null;
        }
        catch (System.Exception exception)
        {
            Debug.LogError($"GalaxyTravelManager: could not load planet '{planetId}'. {exception.Message}");
            return null;
        }
    }

    public bool TryLoadPlanetSurfaceSnapshot(string planetId, out GalaxyPlanetSaveData save)
    {
        save = null;
        if (string.IsNullOrWhiteSpace(planetId))
            return false;
        save = LoadPlanet(planetId);
        return save != null;
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
                writer.Write(building.usesPlanarAddress);
                writer.Write(building.planarOriginX);
                writer.Write(building.planarOriginZ);
                writer.Write(building.planarOriginY);

                Vector2Int[] cells = building.occupiedCells ?? new Vector2Int[0];
                writer.Write(cells.Length);
                foreach (Vector2Int cell in cells)
                {
                    writer.Write(cell.x);
                    writer.Write(cell.y);
                }
            }

            WriteRiverData(writer, data.riverData);

            writer.Write(data.hasFullSurfacePropSnapshot);
            writer.Write(data.surfacePropConfigurationHash);
            string[] harvestedSurfaceProps = data.harvestedSurfacePropIds ?? new string[0];
            writer.Write(harvestedSurfaceProps.Length);
            foreach (string instanceId in harvestedSurfaceProps)
                writer.Write(instanceId ?? string.Empty);

            GalaxySurfacePropSaveEntry[] surfaceProps = data.surfaceProps ?? new GalaxySurfacePropSaveEntry[0];
            writer.Write(surfaceProps.Length);
            foreach (GalaxySurfacePropSaveEntry value in surfaceProps)
            {
                GalaxySurfacePropSaveEntry prop = value ?? new GalaxySurfacePropSaveEntry();
                writer.Write(prop.catalogId ?? string.Empty);
                writer.Write(prop.instanceId ?? string.Empty);
                WriteVector3(writer, prop.localPosition);
                writer.Write(prop.localRotation.x);
                writer.Write(prop.localRotation.y);
                writer.Write(prop.localRotation.z);
                writer.Write(prop.localRotation.w);
                WriteVector3(writer, prop.localScale);
                writer.Write(prop.minimumSpacing);
                writer.Write(prop.harvestable);
            }

            writer.Write((int)data.surfaceGenerationMode);
            writer.Write(data.planetReferenceRadius);
            writer.Write(data.voxelOuterRadius);
            writer.Write((int)data.surfaceTopology);
            WriteVector3(writer, data.planarAnchorDirection);
            writer.Write(data.hasPlanarPlayerPosition);
            writer.Write(data.planarPlayerX);
            writer.Write(data.planarPlayerZ);

            GalaxyDroppedObjectSaveEntry[] droppedObjects =
                data.droppedObjects
                ?? new GalaxyDroppedObjectSaveEntry[0];
            writer.Write(droppedObjects.Length);
            foreach (GalaxyDroppedObjectSaveEntry value
                in droppedObjects)
            {
                GalaxyDroppedObjectSaveEntry dropped =
                    value ?? new GalaxyDroppedObjectSaveEntry();
                writer.Write(dropped.objectId ?? string.Empty);
                writer.Write(dropped.payloadTypeId ?? string.Empty);
                writer.Write(dropped.planarX);
                writer.Write(dropped.planarZ);
                writer.Write(dropped.planarY);
                writer.Write(dropped.rotation.x);
                writer.Write(dropped.rotation.y);
                writer.Write(dropped.rotation.z);
                writer.Write(dropped.rotation.w);
                WriteVector3(writer, dropped.velocity);
                WriteVector3(writer, dropped.angularVelocity);
                writer.Write(dropped.sleeping);
                writer.Write(dropped.cityDraftId ?? string.Empty);
                writer.Write(dropped.cityBoundaryOrder);
                writer.Write(dropped.cityBoundaryLocked);
                writer.Write(dropped.claimedCityId ?? string.Empty);
                writer.Write(dropped.claimedDistrictId ?? string.Empty);
            }

            var cityCollection = new GalaxyPlanarCitySaveCollection
            {
                cities = data.planarCities
                    ?? new GalaxyPlanarCitySaveEntry[0]
            };
            writer.Write(JsonUtility.ToJson(cityCollection));
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
            if (version != 10
                && version != 11
                && version != 12
                && version != 13
                && version != 14
                && version != PlanetSaveVersion)
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
                data.terrainSettings = ReadTerrainSettings(reader, version);
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
                    if (version >= 13)
                    {
                        building.usesPlanarAddress = reader.ReadBoolean();
                        building.planarOriginX = reader.ReadDouble();
                        building.planarOriginZ = reader.ReadDouble();
                        building.planarOriginY = reader.ReadSingle();
                    }
                    int cellCount = ReadBoundedCount(reader, "building cell", 1000000);
                    building.occupiedCells = new Vector2Int[cellCount];
                    for (int cell = 0; cell < cellCount; cell++)
                        building.occupiedCells[cell] = new Vector2Int(reader.ReadInt32(), reader.ReadInt32());
                    data.buildings[i] = building;
                }
            }

            if (version >= 8)
                data.riverData = ReadRiverData(reader, version);

            data.hasFullSurfacePropSnapshot = reader.ReadBoolean();
            data.surfacePropConfigurationHash = reader.ReadInt32();
            int harvestedSurfacePropCount = ReadBoundedCount(reader, nameof(harvestedSurfacePropCount), 100000);
            data.harvestedSurfacePropIds = new string[harvestedSurfacePropCount];
            for (int i = 0; i < harvestedSurfacePropCount; i++)
                data.harvestedSurfacePropIds[i] = reader.ReadString();

            int surfacePropCount = ReadBoundedCount(reader, nameof(surfacePropCount), 100000);
            data.surfaceProps = new GalaxySurfacePropSaveEntry[surfacePropCount];
            for (int i = 0; i < surfacePropCount; i++)
            {
                data.surfaceProps[i] = new GalaxySurfacePropSaveEntry
                {
                    catalogId = reader.ReadString(),
                    instanceId = reader.ReadString(),
                    localPosition = ReadVector3(reader),
                    localRotation = new Quaternion(
                        reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                    localScale = ReadVector3(reader),
                    minimumSpacing = reader.ReadSingle(),
                    harvestable = reader.ReadBoolean()
                };
            }

            if (version >= 11)
            {
                data.surfaceGenerationMode = (PlanetSurfaceGenerationMode)reader.ReadInt32();
                data.planetReferenceRadius = reader.ReadSingle();
                data.voxelOuterRadius = reader.ReadSingle();
            }
            else
            {
                data.surfaceGenerationMode = PlanetSurfaceGenerationMode.LegacyFullSphere;
                data.planetReferenceRadius = PlanetCelestialProfile.CompatibleRadius;
                data.voxelOuterRadius = PlanetCelestialProfile.CompatibleRadius;
            }
            if (version >= 13)
            {
                data.surfaceTopology =
                    (PlanetSurfaceTopology)reader.ReadInt32();
                data.planarAnchorDirection = ReadVector3(reader);
                data.hasPlanarPlayerPosition = reader.ReadBoolean();
                data.planarPlayerX = reader.ReadDouble();
                data.planarPlayerZ = reader.ReadDouble();
            }
            if (version >= 14)
            {
                int droppedObjectCount = ReadBoundedCount(
                    reader,
                    "dropped object",
                    10000000);
                data.droppedObjects =
                    new GalaxyDroppedObjectSaveEntry[droppedObjectCount];
                for (int i = 0; i < droppedObjectCount; i++)
                {
                    data.droppedObjects[i] =
                        new GalaxyDroppedObjectSaveEntry
                        {
                            objectId = reader.ReadString(),
                            payloadTypeId = reader.ReadString(),
                            planarX = reader.ReadDouble(),
                            planarZ = reader.ReadDouble(),
                            planarY = reader.ReadSingle(),
                            rotation = new Quaternion(
                                reader.ReadSingle(),
                                reader.ReadSingle(),
                                reader.ReadSingle(),
                                reader.ReadSingle()),
                            velocity = ReadVector3(reader),
                            angularVelocity = ReadVector3(reader),
                            sleeping = reader.ReadBoolean()
                        };
                    if (version >= 15)
                    {
                        GalaxyDroppedObjectSaveEntry dropped =
                            data.droppedObjects[i];
                        dropped.cityDraftId = reader.ReadString();
                        dropped.cityBoundaryOrder = reader.ReadInt32();
                        dropped.cityBoundaryLocked = reader.ReadBoolean();
                        dropped.claimedCityId = reader.ReadString();
                        dropped.claimedDistrictId = reader.ReadString();
                    }
                }
            }
            else
            {
                data.droppedObjects =
                    new GalaxyDroppedObjectSaveEntry[0];
            }
            if (version >= 15)
            {
                string citiesJson = reader.ReadString();
                GalaxyPlanarCitySaveCollection collection =
                    string.IsNullOrWhiteSpace(citiesJson)
                        ? null
                        : JsonUtility.FromJson<
                            GalaxyPlanarCitySaveCollection>(citiesJson);
                data.planarCities = collection?.cities
                    ?? new GalaxyPlanarCitySaveEntry[0];
            }
            else
            {
                data.planarCities =
                    new GalaxyPlanarCitySaveEntry[0];
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

    static void WriteRiverData(BinaryWriter writer, GalaxyRiverSaveData data)
    {
        writer.Write(data != null);
        if (data == null)
            return;
        writer.Write(data.configurationHash);
        GalaxyRiverPathSaveEntry[] rivers = data.rivers ?? new GalaxyRiverPathSaveEntry[0];
        writer.Write(rivers.Length);
        foreach (GalaxyRiverPathSaveEntry riverValue in rivers)
        {
            GalaxyRiverPathSaveEntry river = riverValue ?? new GalaxyRiverPathSaveEntry();
            writer.Write(river.lakeRadius);
            writer.Write(river.lakeDepth);
            GalaxyRiverNodeSaveEntry[] nodes = river.nodes ?? new GalaxyRiverNodeSaveEntry[0];
            writer.Write(nodes.Length);
            foreach (GalaxyRiverNodeSaveEntry node in nodes)
            {
                WriteVector3(writer, node.direction);
                writer.Write(node.waterRadius);
                writer.Write(node.width);
                writer.Write(node.depth);
                writer.Write(node.flowSpeed);
            }
            WriteFloatArray(writer, river.bedRadii);
            WriteFloatArray(writer, river.waterDepths);
            WriteFloatArray(writer, river.discharges);
            writer.Write(river.lakeWaterDepth);
        }
    }

    static GalaxyRiverSaveData ReadRiverData(BinaryReader reader, int version)
    {
        if (!reader.ReadBoolean())
            return null;
        var data = new GalaxyRiverSaveData
        {
            configurationHash = reader.ReadInt32()
        };
        int riverCount = ReadBoundedCount(reader, "river", 64);
        data.rivers = new GalaxyRiverPathSaveEntry[riverCount];
        for (int i = 0; i < riverCount; i++)
        {
            var river = new GalaxyRiverPathSaveEntry
            {
                lakeRadius = reader.ReadSingle(),
                lakeDepth = reader.ReadSingle()
            };
            int nodeCount = ReadBoundedCount(reader, "river node", 8192);
            river.nodes = new GalaxyRiverNodeSaveEntry[nodeCount];
            for (int node = 0; node < nodeCount; node++)
            {
                river.nodes[node] = new GalaxyRiverNodeSaveEntry
                {
                    direction = ReadVector3(reader),
                    waterRadius = reader.ReadSingle(),
                    width = reader.ReadSingle(),
                    depth = reader.ReadSingle(),
                    flowSpeed = reader.ReadSingle()
                };
            }
            if (version >= 9)
            {
                river.bedRadii = ReadFloatArray(reader, "river bed radius", 8192);
                river.waterDepths = ReadFloatArray(reader, "river water depth", 8192);
                river.discharges = ReadFloatArray(reader, "river discharge", 8192);
                river.lakeWaterDepth = reader.ReadSingle();
            }
            data.rivers[i] = river;
        }
        return data;
    }

    static void WriteFloatArray(BinaryWriter writer, float[] values)
    {
        values = values ?? new float[0];
        writer.Write(values.Length);
        for (int i = 0; i < values.Length; i++)
            writer.Write(values[i]);
    }

    static float[] ReadFloatArray(BinaryReader reader, string valueName, int maximum)
    {
        int count = ReadBoundedCount(reader, valueName, maximum);
        var values = new float[count];
        for (int i = 0; i < count; i++)
            values[i] = reader.ReadSingle();
        return values;
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
        writer.Write(settings.shapeVersion);
        writer.Write(settings.continentThreshold);
        writer.Write(settings.continentWarp);
        writer.Write(settings.continentSharpness);
        writer.Write(settings.mountainMask);
        writer.Write(settings.oceanFloorDepth);
        writer.Write(settings.terraceStrength);
    }

    static PlanetTerrainSettings ReadTerrainSettings(BinaryReader reader, int saveVersion)
    {
        var settings = new PlanetTerrainSettings
        {
            shapeVersion = saveVersion >= 12 ? 0 : 1,
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
        if (saveVersion >= 12)
        {
            settings.shapeVersion = reader.ReadInt32();
            settings.continentThreshold = reader.ReadSingle();
            settings.continentWarp = reader.ReadSingle();
            settings.continentSharpness = reader.ReadSingle();
            settings.mountainMask = reader.ReadSingle();
            settings.oceanFloorDepth = reader.ReadSingle();
            settings.terraceStrength = reader.ReadSingle();
        }
        return settings;
    }

    static void GetPlanetPalette(
        GalaxyPlanetDefinition planet,
        out Color surfaceColor,
        out Color rockColor)
    {
        if (planet.hasExplicitPalette)
        {
            surfaceColor = planet.surfaceColor;
            rockColor = planet.rockColor;
            return;
        }

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

        if (IsInterstellarGalaxy)
        {
            currentPlanetId = ProceduralInterstellarGenerator.EncodePlanetId(currentPlanetCoordinate3D);
            shipCoordinate3D = currentPlanetCoordinate3D;
            return;
        }

        if (IsInfiniteGalaxy)
        {
            currentPlanetId = ProceduralGalaxyGenerator.EncodePlanetId(currentPlanetCoordinate);
            shipCoordinate = currentPlanetCoordinate;
            return;
        }

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

    static GalaxyBuildingSaveEntry[] CapturePlanarBuildings(
        InfinitePlanarSurfaceWorld world)
    {
        if (world == null)
            return new GalaxyBuildingSaveEntry[0];

        var result = new List<GalaxyBuildingSaveEntry>();
        foreach (BuildingAnchor anchor in BuildingAnchor.GetActiveAnchors())
        {
            if (anchor == null || anchor.OccupiedCells.Count == 0)
                continue;
            var cells = new List<Vector2Int>(anchor.OccupiedCells);
            cells.Sort((left, right) =>
            {
                int xComparison = left.x.CompareTo(right.x);
                return xComparison != 0
                    ? xComparison
                    : left.y.CompareTo(right.y);
            });
            PlanarSurfaceAddress address =
                world.ToPersistentAddress(anchor.OriginWorld);
            result.Add(new GalaxyBuildingSaveEntry
            {
                buildingTypeId = "foundation",
                localOrigin = anchor.OriginWorld,
                localUp = anchor.Up,
                localForward = anchor.Forward,
                cellSize = anchor.CellSize,
                slabHeight = anchor.SlabHeight,
                pillarHeight = anchor.PillarHeight,
                pillarSize = anchor.PillarSize,
                occupiedCells = cells.ToArray(),
                usesPlanarAddress = true,
                planarOriginX = address.x,
                planarOriginZ = address.z,
                planarOriginY = address.y
            });
        }
        return result.ToArray();
    }

    static void RestorePlanarBuildings(
        InfinitePlanarSurfaceWorld world,
        GalaxyPlanetSaveData save)
    {
        if (world == null
            || save?.buildings == null
            || save.buildings.Length == 0)
        {
            return;
        }

        BuildingPlacer placer = FindObjectOfType<BuildingPlacer>();
        Material foundationMaterial =
            placer != null ? placer.FoundationMaterial : null;
        int restoredPieces = 0;
        foreach (GalaxyBuildingSaveEntry building in save.buildings)
        {
            if (building == null
                || building.buildingTypeId != "foundation"
                || !building.usesPlanarAddress
                || building.occupiedCells == null
                || building.occupiedCells.Length == 0)
            {
                continue;
            }

            Vector3 origin = world.FromPersistentAddress(
                new PlanarSurfaceAddress(
                    building.planarOriginX,
                    building.planarOriginZ,
                    building.planarOriginY));
            Vector3 up = building.localUp.sqrMagnitude > 0.9f
                ? building.localUp.normalized
                : Vector3.up;
            Vector3 forward = Vector3.ProjectOnPlane(
                building.localForward,
                up).normalized;
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.forward;

            BuildingAnchor anchor = BuildingAnchor.Create(
                origin,
                up,
                forward,
                Mathf.Max(0.1f, building.cellSize),
                Mathf.Max(0.01f, building.slabHeight),
                Mathf.Max(0f, building.pillarHeight),
                Mathf.Max(0.01f, building.pillarSize),
                foundationMaterial);
            anchor.gameObject
                .AddComponent<PlanetFloatingOriginParticipant>();
            restoredPieces +=
                anchor.RestoreCells(building.occupiedCells);
        }
        Debug.Log(
            $"GalaxyTravelManager: restored {restoredPieces} planar building pieces for planet '{save.planetId}'.");
    }

    void CaptureInventory()
    {
        PlayerInventory inventory = FindObjectOfType<PlayerInventory>();
        if (inventory == null)
            return;

        inventorySnapshot = inventory.CreateSnapshot();
        selectedInventorySlot = inventory.SelectedSlotIndex;
        activeSlotMetadata.inventory = SerializeInventory(inventorySnapshot);
        activeSlotMetadata.selectedInventorySlot = selectedInventorySlot;
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
        return GalaxySaveSlotService.GetPlanetsDirectory(activeSlotMetadata.slotId);
    }

    string GetPlanetSavePath(string planetId)
    {
        return IsInfiniteGalaxy || IsInterstellarGalaxy
            ? Path.Combine(GetSaveDirectory(), planetId, "world.planet.gz")
            : Path.Combine(GetSaveDirectory(), planetId + ".planet.gz");
    }

    string GetLegacyPlanetSavePath(string planetId)
    {
        if (IsInfiniteGalaxy || IsInterstellarGalaxy)
            return Path.Combine(GetSaveDirectory(), planetId, "world.json");
        return Path.Combine(GetSaveDirectory(), planetId + ".json");
    }

    void LoadActiveSaveSlot()
    {
        string selectedSlotId = GalaxyLaunchContext.SelectedSlotId;
        if (!string.IsNullOrEmpty(selectedSlotId))
        {
            try
            {
                activeSlotMetadata = GalaxySaveSlotService.LoadMetadata(selectedSlotId);
            }
            catch (System.Exception exception)
            {
                Debug.LogError($"GalaxyTravelManager: could not open save slot '{selectedSlotId}'. {exception.Message}", this);
            }
        }

        if (activeSlotMetadata == null)
        {
            activeSlotMetadata = GalaxySaveSlotService.GetOrCreateDevelopmentSlot();
            GalaxyLaunchContext.SelectSlot(activeSlotMetadata.slotId);
        }
    }

    void ApplyWorldSeed(int worldSeed)
    {
        galaxyLayoutSeed = DeriveSeed(worldSeed, "galaxy-layout");
        foreach (GalaxyPlanetDefinition planet in planets)
            planet.seed = DeriveSeed(worldSeed, planet.planetId);
    }

    void RestoreShipPositionFromMetadata()
    {
        if (IsInterstellarGalaxy)
        {
            shipCoordinate3D = new InterstellarCoordinate(
                activeSlotMetadata.shipCoordinateX,
                activeSlotMetadata.shipCoordinateY,
                activeSlotMetadata.shipCoordinateZ);
            return;
        }

        if (IsInfiniteGalaxy)
        {
            shipCoordinate = new GalaxyCoordinate(
                activeSlotMetadata.shipCoordinateX,
                activeSlotMetadata.shipCoordinateY);
            return;
        }

        Vector2Int savedPosition = new Vector2Int(activeSlotMetadata.shipGridX, activeSlotMetadata.shipGridY);
        if (savedPosition.x >= 0 && savedPosition.x < gridColumns
            && savedPosition.y >= 0 && savedPosition.y < gridRows)
        {
            shipGridPosition = savedPosition;
        }
    }

    void SaveActiveSlotMetadata()
    {
        if (activeSlotMetadata == null)
            return;
        activeSlotMetadata.currentPlanetId = currentPlanetId;
        activeSlotMetadata.shipFacing = shipFacing;
        if (IsInterstellarGalaxy)
        {
            activeSlotMetadata.shipCoordinateX = shipCoordinate3D.x;
            activeSlotMetadata.shipCoordinateY = shipCoordinate3D.y;
            activeSlotMetadata.shipCoordinateZ = shipCoordinate3D.z;
            activeSlotMetadata.currentPlanetCoordinateX = currentPlanetCoordinate3D.x;
            activeSlotMetadata.currentPlanetCoordinateY = currentPlanetCoordinate3D.y;
            activeSlotMetadata.currentPlanetCoordinateZ = currentPlanetCoordinate3D.z;
            activeSlotMetadata.galaxyGeneratorVersion = ProceduralInterstellarGenerator.CurrentVersion;
        }
        else if (IsInfiniteGalaxy)
        {
            activeSlotMetadata.shipCoordinateX = shipCoordinate.x;
            activeSlotMetadata.shipCoordinateY = shipCoordinate.y;
            activeSlotMetadata.currentPlanetCoordinateX = currentPlanetCoordinate.x;
            activeSlotMetadata.currentPlanetCoordinateY = currentPlanetCoordinate.y;
            activeSlotMetadata.galaxyGeneratorVersion = ProceduralGalaxyGenerator.CurrentVersion;
        }
        else
        {
            activeSlotMetadata.shipGridX = shipGridPosition.x;
            activeSlotMetadata.shipGridY = shipGridPosition.y;
        }
        activeSlotMetadata.selectedInventorySlot = selectedInventorySlot;
        if (inventorySnapshot != null)
            activeSlotMetadata.inventory = SerializeInventory(inventorySnapshot);

        try
        {
            GalaxySaveSlotService.SaveMetadata(activeSlotMetadata);
        }
        catch (System.Exception exception)
        {
            Debug.LogError($"GalaxyTravelManager: could not save slot metadata. {exception.Message}", this);
        }
    }

    void LoadInventoryFromMetadata()
    {
        selectedInventorySlot = activeSlotMetadata.selectedInventorySlot;
        if (activeSlotMetadata.inventory == null || activeSlotMetadata.inventory.Length == 0)
            return;

        inventorySnapshot = new List<InventorySlot>(activeSlotMetadata.inventory.Length);
        var definitions = new Dictionary<string, InventoryItem>();
        foreach (GalaxyInventorySaveEntry entry in activeSlotMetadata.inventory)
        {
            var slot = new InventorySlot();
            if (entry != null && !string.IsNullOrEmpty(entry.itemId) && entry.amount > 0)
            {
                if (!definitions.TryGetValue(entry.itemId, out InventoryItem item))
                {
                    Sprite icon = DecodeInventoryIcon(entry.iconPngBase64, entry.itemId);
                    item = InventoryItem.GetOrCreateRuntime(entry.itemId, entry.displayName, icon, entry.maxStack);
                    definitions.Add(entry.itemId, item);
                }
                slot.Set(item, entry.amount);
            }
            inventorySnapshot.Add(slot);
        }
    }

    GalaxyInventorySaveEntry[] SerializeInventory(IReadOnlyList<InventorySlot> slots)
    {
        if (slots == null)
            return System.Array.Empty<GalaxyInventorySaveEntry>();

        var previousIcons = new Dictionary<string, string>();
        if (activeSlotMetadata.inventory != null)
        {
            foreach (GalaxyInventorySaveEntry previous in activeSlotMetadata.inventory)
                if (previous != null && !string.IsNullOrEmpty(previous.itemId) && !string.IsNullOrEmpty(previous.iconPngBase64))
                    previousIcons[previous.itemId] = previous.iconPngBase64;
        }

        var result = new GalaxyInventorySaveEntry[slots.Count];
        for (int i = 0; i < slots.Count; i++)
        {
            InventorySlot slot = slots[i];
            var entry = new GalaxyInventorySaveEntry();
            if (slot != null && !slot.IsEmpty)
            {
                entry.itemId = slot.item.ItemId;
                entry.displayName = slot.item.DisplayName;
                entry.maxStack = slot.item.MaxStack;
                entry.amount = slot.amount;
                if (!previousIcons.TryGetValue(entry.itemId, out entry.iconPngBase64))
                    entry.iconPngBase64 = EncodeInventoryIcon(slot.item.Icon);
            }
            result[i] = entry;
        }
        return result;
    }

    static string EncodeInventoryIcon(Sprite icon)
    {
        if (icon == null || icon.texture == null)
            return string.Empty;
        RenderTexture temporary = RenderTexture.GetTemporary(icon.texture.width, icon.texture.height, 0, RenderTextureFormat.ARGB32);
        RenderTexture previous = RenderTexture.active;
        try
        {
            Graphics.Blit(icon.texture, temporary);
            RenderTexture.active = temporary;
            var readable = new Texture2D(temporary.width, temporary.height, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, temporary.width, temporary.height), 0, 0);
            readable.Apply();
            byte[] png = readable.EncodeToPNG();
            Destroy(readable);
            return System.Convert.ToBase64String(png);
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning("GalaxyTravelManager: could not encode an inventory icon. " + exception.Message);
            return string.Empty;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(temporary);
        }
    }

    static Sprite DecodeInventoryIcon(string base64, string itemId)
    {
        if (string.IsNullOrEmpty(base64))
            return null;
        try
        {
            byte[] png = System.Convert.FromBase64String(base64);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                name = $"SavedIcon_{itemId}",
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.DontUnloadUnusedAsset
            };
            if (!texture.LoadImage(png))
            {
                Destroy(texture);
                return null;
            }
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = $"SavedIcon_{itemId}";
            sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return sprite;
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning("GalaxyTravelManager: could not restore an inventory icon. " + exception.Message);
            return null;
        }
    }

    void BuildRuntimeResourceCatalog()
    {
        runtimeResourceCatalog.Clear();
        if (resourceCatalog != null)
        {
            foreach (GalaxyResourceCatalogEntry entry in resourceCatalog)
                AddRuntimeResource(entry != null ? entry.resourceId : null, entry != null ? entry.prefab : null);
        }

        foreach (GalaxyPlanetDefinition planet in planets)
        {
            if (planet == null || planet.resourceSpawnSettings == null)
                continue;
            foreach (HarvestableResourceSpawnSettings settings in planet.resourceSpawnSettings)
                if (settings != null && settings.prefab != null)
                    AddRuntimeResource(settings.prefab.name, settings.prefab);
        }
    }

    void IndexFrozenPlanetDefinitions()
    {
        frozenPlanetIds.Clear();
        if (!IsInfiniteGalaxy && !IsInterstellarGalaxy)
            return;

        string root = GetSaveDirectory();
        if (!Directory.Exists(root))
            return;
        foreach (string directory in Directory.GetDirectories(root, "*_*", SearchOption.TopDirectoryOnly))
        {
            string id = Path.GetFileName(directory);
            if ((id.StartsWith("p_", System.StringComparison.Ordinal) || id.StartsWith("q_", System.StringComparison.Ordinal))
                && File.Exists(Path.Combine(directory, "definition.json")))
                frozenPlanetIds.Add(Path.GetFileName(directory));
        }
    }

    void AddRuntimeResource(string resourceId, HarvestableResource prefab)
    {
        if (prefab == null)
            return;
        resourceId = string.IsNullOrWhiteSpace(resourceId) ? prefab.name : resourceId.Trim();
        foreach (GalaxyResourceCatalogEntry existing in runtimeResourceCatalog)
            if (existing.resourceId == resourceId || existing.prefab == prefab)
                return;
        runtimeResourceCatalog.Add(new GalaxyResourceCatalogEntry { resourceId = resourceId, prefab = prefab });
    }

    void CacheProceduralPlanet(GalaxyCoordinate coordinate, GalaxyPlanetDefinition planet)
    {
        proceduralPlanetCache[coordinate] = planet;
        proceduralCacheOrder.Enqueue(coordinate);
        while (proceduralPlanetCache.Count > 256 && proceduralCacheOrder.Count > 0)
        {
            GalaxyCoordinate expired = proceduralCacheOrder.Dequeue();
            if (expired != currentPlanetCoordinate)
                proceduralPlanetCache.Remove(expired);
        }
    }

    void CacheProceduralPlanet(InterstellarCoordinate coordinate, GalaxyPlanetDefinition planet)
    {
        interstellarPlanetCache[coordinate] = planet;
        interstellarCacheOrder.Enqueue(coordinate);
        while (interstellarPlanetCache.Count > 256 && interstellarCacheOrder.Count > 0)
        {
            InterstellarCoordinate expired = interstellarCacheOrder.Dequeue();
            if (expired != currentPlanetCoordinate3D)
                interstellarPlanetCache.Remove(expired);
        }
    }

    VisitedPlanetRecord FindVisitedPlanet(InterstellarCoordinate coordinate)
    {
        if (activeSlotMetadata == null || activeSlotMetadata.visitedPlanets == null)
            return null;
        foreach (VisitedPlanetRecord visited in activeSlotMetadata.visitedPlanets)
            if (visited != null && visited.Coordinate == coordinate)
                return visited;
        return null;
    }

    VisitedPlanetRecord FindVisitedPlanet(string planetId)
    {
        if (activeSlotMetadata == null || activeSlotMetadata.visitedPlanets == null)
            return null;
        foreach (VisitedPlanetRecord visited in activeSlotMetadata.visitedPlanets)
            if (visited != null && visited.planetId == planetId)
                return visited;
        return null;
    }

    GalaxyPlanetDefinition ApplyVisitedDisplayName(GalaxyPlanetDefinition planet)
    {
        if (planet == null)
            return null;
        VisitedPlanetRecord visited = FindVisitedPlanet(planet.planetId);
        if (visited != null && !string.IsNullOrWhiteSpace(visited.displayName))
            planet.displayName = visited.displayName;
        return planet;
    }

    GalaxyPlanetDefinition ApplyVisitedDisplayName(
        GalaxyPlanetDefinition planet,
        InterstellarCoordinate coordinate)
    {
        if (planet == null)
            return null;
        VisitedPlanetRecord visited = FindVisitedPlanet(planet.planetId)
            ?? FindVisitedPlanet(coordinate);
        if (visited != null && !string.IsNullOrWhiteSpace(visited.displayName))
            planet.displayName = visited.displayName;
        return planet;
    }

    public string GetPlanetDisplayName(GalaxyPlanetDefinition planet)
    {
        if (planet == null)
            return string.Empty;
        VisitedPlanetRecord visited = FindVisitedPlanet(planet.planetId);
        if (visited != null && !string.IsNullOrWhiteSpace(visited.displayName))
            return visited.displayName;
        return string.IsNullOrWhiteSpace(planet.displayName) ? planet.planetId : planet.displayName;
    }

    public string GetPlanetDisplayName(VisitedPlanetRecord visited)
    {
        if (visited == null)
            return string.Empty;
        if (!string.IsNullOrWhiteSpace(visited.displayName))
            return visited.displayName;
        GalaxyPlanetDefinition planet = GetPlanetAt(visited.Coordinate);
        return planet != null && !string.IsNullOrWhiteSpace(planet.displayName)
            ? planet.displayName
            : visited.planetId;
    }

    public static bool TryNormalizePlanetDisplayName(
        string value,
        out string normalizedName,
        out string error)
    {
        normalizedName = string.Empty;
        error = string.Empty;
        if (value == null)
        {
            error = "星球名称不能为空";
            return false;
        }

        foreach (char character in value)
        {
            if (character == '\r' || character == '\n' || char.IsControl(character))
            {
                error = "星球名称不能包含换行或控制字符";
                return false;
            }
        }

        normalizedName = value.Trim();
        if (normalizedName.Length == 0)
        {
            error = "星球名称不能为空";
            return false;
        }

        int visibleCharacterCount =
            System.Globalization.StringInfo.ParseCombiningCharacters(normalizedName).Length;
        if (visibleCharacterCount > 24)
        {
            error = "星球名称不能超过 24 个字符";
            return false;
        }

        return true;
    }

    public bool RenameVisitedPlanet(string planetId, string newName, out string error)
    {
        error = string.Empty;
        if (!IsInterstellarGalaxy || activeSlotMetadata == null)
        {
            error = "当前存档不支持星球重命名";
            return false;
        }

        VisitedPlanetRecord visited = FindVisitedPlanet(planetId);
        if (visited == null)
        {
            error = "找不到已访问的星球";
            return false;
        }

        if (!TryNormalizePlanetDisplayName(newName, out string normalizedName, out error))
            return false;

        visited.displayName = normalizedName;
        if (interstellarPlanetCache.TryGetValue(visited.Coordinate, out GalaxyPlanetDefinition cached)
            && cached != null)
        {
            cached.displayName = normalizedName;
        }

        if (currentPlanetId == visited.planetId)
        {
            GalaxyPlanetDefinition current = GetPlanetAt(visited.Coordinate);
            if (current != null)
                current.displayName = normalizedName;
        }

        SaveActiveSlotMetadata();
        return true;
    }

    Vector3 GetLastLandingDirection(string planetId)
    {
        VisitedPlanetRecord visited = FindVisitedPlanet(planetId);
        return visited != null && visited.lastLandingDirection.sqrMagnitude > 0.001f
            ? visited.lastLandingDirection.normalized
            : Vector3.forward;
    }

    void AddOrUpdateVisitedPlanet(PendingPlanetLandingContext context)
    {
        var records = new List<VisitedPlanetRecord>(activeSlotMetadata.visitedPlanets
            ?? System.Array.Empty<VisitedPlanetRecord>());
        VisitedPlanetRecord record = records.Find(item => item != null && item.planetId == context.planetId);
        bool isNewRecord = record == null;
        if (record == null)
        {
            record = new VisitedPlanetRecord { planetId = context.planetId };
            records.Add(record);
        }
        GalaxyPlanetDefinition planet = GetPlanetAt(context.coordinate);
        if (isNewRecord || string.IsNullOrWhiteSpace(record.displayName))
            record.displayName = planet != null ? planet.displayName : context.planetId;
        record.coordinateX = context.coordinate.x;
        record.coordinateY = context.coordinate.y;
        record.coordinateZ = context.coordinate.z;
        record.lastLandingDirection = context.landingDirection.sqrMagnitude > 0.001f
            ? context.landingDirection.normalized
            : Vector3.up;
        record.lastPlayerLocalPosition = context.playerLocalPosition;
        record.lastVisitedUtcTicks = System.DateTime.UtcNow.Ticks;
        record.surfaceSpacecraft = new SurfaceSpacecraftState
        {
            valid = true,
            surfaceTopology = UsesInfinitePlanarSurface
                ? PlanetSurfaceTopology.InfinitePlanar
                : PlanetSurfaceTopology.LegacySphere,
            radialDirection = record.lastLandingDirection,
            tangentForward = context.shipRotation * Vector3.forward,
            parkingMode = context.landingMode == PlanetLandingMode.OceanPlatform
                ? SurfaceSpacecraftParkingMode.OceanPlatform
                : SurfaceSpacecraftParkingMode.Terrain,
            hoverAltitude = 6f
        };
        record.surfaceSpacecraft.ClampValues();
        activeSlotMetadata.visitedPlanets = records.ToArray();
    }

    void FreezePlanetDefinition(GalaxyPlanetDefinition planet)
    {
        if ((!IsInfiniteGalaxy && !IsInterstellarGalaxy) || planet == null || !planet.isProcedural)
            return;

        string path = GetPlanetDefinitionPath(planet.planetId);
        if (File.Exists(path) && frozenPlanetIds.Contains(planet.planetId))
            return;
        var record = new GalaxyGeneratedPlanetRecord
        {
            generatorVersion = IsInterstellarGalaxy
                ? ProceduralInterstellarGenerator.CurrentVersion
                : ProceduralGalaxyGenerator.CurrentVersion,
            planetId = planet.planetId,
            displayName = planet.displayName,
            coordinateX = planet.isInterstellar ? planet.coordinate3D.x : planet.coordinate.x,
            coordinateY = planet.isInterstellar ? planet.coordinate3D.y : planet.coordinate.y,
            coordinateZ = planet.coordinate3D.z,
            usesInterstellarCoordinate = planet.isInterstellar,
            systemId = planet.systemId,
            orbitIndex = planet.orbitIndex,
            orbit = planet.orbit,
            seed = planet.seed,
            climate = planet.climate,
            mapColor = planet.mapColor,
            surfaceColor = planet.surfaceColor,
            rockColor = planet.rockColor,
            iconResourcePath = planet.iconResourcePath,
            terrain = planet.terrain,
            rivers = planet.rivers,
            weather = planet.weather,
            celestial = planet.celestial ?? PlanetCelestialProfile.CreateCompatibleDefault(),
            lowPolyVisual = planet.lowPolyVisual
        };

        if (planet.resourceSpawnSettings != null)
        {
            foreach (HarvestableResourceSpawnSettings settings in planet.resourceSpawnSettings)
            {
                string catalogId = GetCatalogId(settings != null ? settings.prefab : null);
                if (settings == null || string.IsNullOrEmpty(catalogId))
                    continue;
                record.resources.Add(new GalaxyGeneratedResourceRecord
                {
                    catalogId = catalogId,
                    count = settings.count,
                    seedOffset = settings.seedOffset,
                    surfaceOffset = settings.surfaceOffset,
                    minimumSpacing = settings.minimumSpacing,
                    playerClearRadius = settings.playerClearRadius,
                    placementAttempts = settings.placementAttempts,
                    clockwiseRotationDegrees = settings.clockwiseRotationDegrees,
                    randomizeYaw = settings.randomizeYaw
                });
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonUtility.ToJson(record, true));
        if (File.Exists(path))
            File.Replace(temporaryPath, path, null);
        else
            File.Move(temporaryPath, path);
        frozenPlanetIds.Add(planet.planetId);
    }

    GalaxyPlanetDefinition LoadFrozenPlanetDefinition(GalaxyCoordinate coordinate)
    {
        string planetId = ProceduralGalaxyGenerator.EncodePlanetId(coordinate);
        string path = GetPlanetDefinitionPath(planetId);
        if (!File.Exists(path))
            return null;

        try
        {
            GalaxyGeneratedPlanetRecord record = JsonUtility.FromJson<GalaxyGeneratedPlanetRecord>(File.ReadAllText(path));
            if (record == null || record.formatVersion < 2 || record.formatVersion > 7
                || record.usesInterstellarCoordinate || record.planetId != planetId
                || record.coordinateX != coordinate.x || record.coordinateY != coordinate.y)
                throw new InvalidDataException("Invalid procedural planet definition.");

            var planet = new GalaxyPlanetDefinition
            {
                planetId = record.planetId,
                displayName = record.displayName,
                coordinate = coordinate,
                isProcedural = true,
                seed = record.seed,
                climate = record.climate,
                mapColor = record.mapColor,
                surfaceColor = record.surfaceColor,
                rockColor = record.rockColor,
                hasExplicitPalette = true,
                tintMapIcon = true,
                iconResourcePath = record.iconResourcePath,
                terrain = record.terrain ?? new PlanetTerrainSettings(),
                rivers = record.rivers ?? new PlanetRiverSettings(),
                weather = record.weather ?? new PlanetWeatherSettings(),
                celestial = record.celestial ?? PlanetCelestialProfile.CreateCompatibleDefault(),
                lowPolyVisual = record.lowPolyVisual,
                resourceSpawnSettings = new List<HarvestableResourceSpawnSettings>()
            };

            if (record.resources != null)
            {
                foreach (GalaxyGeneratedResourceRecord resource in record.resources)
                {
                    HarvestableResource prefab = ResolveCatalogPrefab(resource != null ? resource.catalogId : null);
                    if (resource == null || prefab == null)
                        continue;
                    planet.resourceSpawnSettings.Add(new HarvestableResourceSpawnSettings
                    {
                        prefab = prefab,
                        count = resource.count,
                        seedOffset = resource.seedOffset,
                        surfaceOffset = resource.surfaceOffset,
                        minimumSpacing = resource.minimumSpacing,
                        playerClearRadius = resource.playerClearRadius,
                        placementAttempts = resource.placementAttempts,
                        clockwiseRotationDegrees = resource.clockwiseRotationDegrees,
                        randomizeYaw = resource.randomizeYaw
                    });
                }
            }
            planet.spawnHarvestableResources = planet.resourceSpawnSettings.Count > 0;
            return planet;
        }
        catch (System.Exception exception)
        {
            Debug.LogError($"GalaxyTravelManager: could not load planet definition '{planetId}'. {exception.Message}", this);
            return null;
        }
    }


    GalaxyPlanetDefinition LoadFrozenPlanetDefinition(InterstellarCoordinate coordinate)
    {
        VisitedPlanetRecord visited = FindVisitedPlanet(coordinate);
        string planetId = visited != null
            ? visited.planetId
            : ProceduralInterstellarGenerator.EncodePlanetId(coordinate);
        string path = GetPlanetDefinitionPath(planetId);
        if (!File.Exists(path))
            return null;

        try
        {
            GalaxyGeneratedPlanetRecord record = JsonUtility.FromJson<GalaxyGeneratedPlanetRecord>(File.ReadAllText(path));
            if (record == null)
                throw new InvalidDataException("Missing interstellar planet definition.");
            bool migratedLegacy = visited != null && !record.usesInterstellarCoordinate;
            bool coordinateMatches = migratedLegacy
                || (record.coordinateX == coordinate.x && record.coordinateY == coordinate.y && record.coordinateZ == coordinate.z);
            if (record == null || record.formatVersion < 2 || record.formatVersion > 7
                || record.planetId != planetId || !coordinateMatches)
                throw new InvalidDataException("Invalid interstellar planet definition.");

            var planet = new GalaxyPlanetDefinition
            {
                planetId = record.planetId,
                displayName = record.displayName,
                coordinate3D = coordinate,
                isProcedural = true,
                isInterstellar = true,
                systemId = record.systemId,
                orbitIndex = record.orbitIndex,
                orbit = record.orbit,
                seed = record.seed,
                climate = record.climate,
                mapColor = record.mapColor,
                surfaceColor = record.surfaceColor,
                rockColor = record.rockColor,
                hasExplicitPalette = true,
                tintMapIcon = true,
                iconResourcePath = record.iconResourcePath,
                terrain = record.terrain ?? new PlanetTerrainSettings(),
                rivers = record.rivers ?? new PlanetRiverSettings(),
                weather = record.weather ?? new PlanetWeatherSettings(),
                celestial = record.celestial ?? PlanetCelestialProfile.CreateCompatibleDefault(),
                lowPolyVisual = record.lowPolyVisual,
                resourceSpawnSettings = new List<HarvestableResourceSpawnSettings>()
            };

            if (string.IsNullOrEmpty(planet.systemId) || planet.orbit == null)
            {
                EnsureInterstellarGenerator();
                GalaxyPlanetDefinition generated = interstellarGenerator?.GeneratePlanet(coordinate);
                if (generated != null)
                {
                    planet.systemId = generated.systemId;
                    planet.orbitIndex = generated.orbitIndex;
                    planet.orbit = generated.orbit;
                }
            }

            if (record.resources != null)
            {
                foreach (GalaxyGeneratedResourceRecord resource in record.resources)
                {
                    HarvestableResource prefab = ResolveCatalogPrefab(resource != null ? resource.catalogId : null);
                    if (resource == null || prefab == null)
                        continue;
                    planet.resourceSpawnSettings.Add(new HarvestableResourceSpawnSettings
                    {
                        prefab = prefab,
                        count = resource.count,
                        seedOffset = resource.seedOffset,
                        surfaceOffset = resource.surfaceOffset,
                        minimumSpacing = resource.minimumSpacing,
                        playerClearRadius = resource.playerClearRadius,
                        placementAttempts = resource.placementAttempts,
                        clockwiseRotationDegrees = resource.clockwiseRotationDegrees,
                        randomizeYaw = resource.randomizeYaw
                    });
                }
            }
            planet.spawnHarvestableResources = planet.resourceSpawnSettings.Count > 0;
            return planet;
        }
        catch (System.Exception exception)
        {
            Debug.LogError($"GalaxyTravelManager: could not load interstellar planet definition '{planetId}'. {exception.Message}", this);
            return null;
        }
    }

    string GetCatalogId(HarvestableResource prefab)
    {
        foreach (GalaxyResourceCatalogEntry entry in runtimeResourceCatalog)
            if (entry.prefab == prefab)
                return entry.resourceId;
        return null;
    }

    HarvestableResource ResolveCatalogPrefab(string resourceId)
    {
        foreach (GalaxyResourceCatalogEntry entry in runtimeResourceCatalog)
            if (entry.resourceId == resourceId)
                return entry.prefab;
        if (!string.IsNullOrEmpty(resourceId))
            Debug.LogWarning($"GalaxyTravelManager: resource catalog entry '{resourceId}' is unavailable.", this);
        return null;
    }

    string GetPlanetDefinitionPath(string planetId)
    {
        return Path.Combine(GetSaveDirectory(), planetId, "definition.json");
    }

    static int DeriveSeed(int worldSeed, string key)
    {
        unchecked
        {
            uint hash = 2166136261u ^ (uint)worldSeed;
            for (int i = 0; i < key.Length; i++)
                hash = (hash ^ key[i]) * 16777619u;
            return (int)(hash == 0 ? 1u : hash);
        }
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
        PlanetClimateClassifier.TryGetFixedClimate(id, out PlanetClimate climate);
        return new GalaxyPlanetDefinition
        {
            planetId = id,
            displayName = displayName,
            gridPosition = new Vector2Int(x, y),
            coordinate = new GalaxyCoordinate(x, y),
            seed = planetSeed,
            climate = climate,
            mapColor = color,
            iconResourcePath = iconPath,
            terrain = CreateTerrainPreset(id),
            rivers = CreateRiverPreset(id),
            weather = PlanetWeatherDefaults.Create(id)
        };
    }

    static PlanetRiverSettings CreateRiverPreset(string planetId)
    {
        if (planetId == "verdant")
        {
            return new PlanetRiverSettings
            {
                enabled = true,
                riverCount = 4,
                nodesPerRiver = 48,
                minWidth = 2f,
                maxWidth = 4f,
                minDepth = 0.8f,
                maxDepth = 1.5f,
                flowSpeed = 1.5f,
                lakeRadius = 6f,
                lakeDepth = 2f,
                simulationStep = 0.04f,
                sourceFlowRate = 2.5f,
                manningRoughness = 0.04f,
                shallowColor = new Color(0.06f, 0.76f, 0.63f, 0.58f),
                deepColor = new Color(0.005f, 0.19f, 0.23f, 0.82f)
            };
        }
        if (planetId == "azure")
        {
            return new PlanetRiverSettings
            {
                enabled = true,
                riverCount = 7,
                nodesPerRiver = 56,
                minWidth = 3f,
                maxWidth = 6f,
                minDepth = 1.2f,
                maxDepth = 2.2f,
                flowSpeed = 1.1f,
                lakeRadius = 9f,
                lakeDepth = 3f,
                simulationStep = 0.035f,
                sourceFlowRate = 6f,
                manningRoughness = 0.03f,
                shallowColor = new Color(0.04f, 0.55f, 0.88f, 0.58f),
                deepColor = new Color(0.005f, 0.08f, 0.32f, 0.84f)
            };
        }
        return new PlanetRiverSettings { enabled = false, riverCount = 0 };
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
