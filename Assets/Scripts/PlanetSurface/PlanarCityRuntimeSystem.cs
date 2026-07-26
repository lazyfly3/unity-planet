using System;
using System.Collections;
using System.Collections.Generic;
using CityGeneration;
using UnityEngine;

[DefaultExecutionOrder(-150)]
[DisallowMultipleComponent]
public sealed class PlanarCityRuntimeSystem : MonoBehaviour
{
    const string GenerateActionId = "planar_city_generate";
    const string UndoActionId = "planar_city_undo";
    const string CancelActionId = "planar_city_cancel";
    const float MaximumDraftSpan = 512f;
    const float ExpansionDistance = 20f;
    const float DifferenceSubdivisionSize = 4f;

    sealed class RuntimeDistrict
    {
        public GalaxyPlanarCitySaveEntry city;
        public GalaxyPlanarCityDistrictSaveEntry data;
        public GameObject root;
        public CityConstructionAnimator animator;
    }

    sealed class PlanetTerrainSampler : ICityTerrainSampler
    {
        readonly InfinitePlanarSurfaceWorld world;
        readonly double anchorX;
        readonly double anchorZ;

        public PlanetTerrainSampler(
            InfinitePlanarSurfaceWorld valueWorld,
            double valueAnchorX,
            double valueAnchorZ)
        {
            world = valueWorld;
            anchorX = valueAnchorX;
            anchorZ = valueAnchorZ;
        }

        public CityTerrainSample SampleTerrain(Vector2 localXZ)
        {
            if (world == null || world.Streamer == null)
                return CityTerrainSample.Flat();
            world.Streamer.TrySampleSurface(
                anchorX + localXZ.x,
                anchorZ + localXZ.y,
                out float height,
                out Vector3 normal);
            bool water =
                world.OceanEnabled && height < world.SeaHeight;
            float surface = water
                ? world.SeaHeight
                : height;
            float grade = Mathf.Sqrt(
                Mathf.Max(0f, 1f - normal.y * normal.y))
                / Mathf.Max(0.001f, Mathf.Abs(normal.y));
            return new CityTerrainSample(
                height,
                surface,
                world.SeaHeight,
                water
                    ? Mathf.Max(0f, world.SeaHeight - height)
                    : 0f,
                normal,
                grade,
                water
                    ? CitySurfaceKind.Lake
                    : CitySurfaceKind.Land);
        }
    }

    readonly List<GalaxyPlanarCitySaveEntry> cities =
        new List<GalaxyPlanarCitySaveEntry>();
    readonly Dictionary<string, RuntimeDistrict> runtimeDistricts =
        new Dictionary<string, RuntimeDistrict>(
            StringComparer.Ordinal);
    readonly HashSet<Vector2Int> activeChunks =
        new HashSet<Vector2Int>();
    readonly List<Vector2Int> coordinateBuffer =
        new List<Vector2Int>(25);
    readonly List<GalaxyDroppedObjectSaveEntry> draftBuffer =
        new List<GalaxyDroppedObjectSaveEntry>();
    readonly List<Vector2> polygonBuffer = new List<Vector2>();

    InfinitePlanarSurfaceWorld world;
    PlanetLabInfiniteTerrainStreamer terrain;
    PlanarDroppedObjectSystem droppedObjects;
    VoxelPlanetPlayerController player;
    SurfaceMultifunctionController multifunction;
    bool menuActionsRegistered;
    PlanarCityRuntimeAssets assets;
    bool ownsAssets;
    Material roadMaterial;
    Material blockMaterial;
    Material foundationMaterial;
    Material[] buildingMaterials;
    Coroutine constructionRoutine;
    RuntimeDistrict constructingDistrict;
    string status = string.Empty;
    bool configured;

    public bool IsConstructing => constructionRoutine != null;
    public string Status => status;
    public int CityCount => cities.Count;

    public void Configure(
        InfinitePlanarSurfaceWorld valueWorld,
        PlanetLabInfiniteTerrainStreamer valueTerrain,
        PlanarDroppedObjectSystem valueDroppedObjects,
        VoxelPlanetPlayerController player,
        GalaxyPlanarCitySaveEntry[] restored)
    {
        Unsubscribe();
        world = valueWorld;
        terrain = valueTerrain;
        droppedObjects = valueDroppedObjects;
        this.player = player;
        multifunction = null;
        menuActionsRegistered = false;
        ownsAssets = false;
        assets = Resources.Load<PlanarCityRuntimeAssets>(
            "PlanetSurface/PlanarCityRuntimeAssets");
        if (assets == null)
        {
            assets = ScriptableObject.CreateInstance<
                PlanarCityRuntimeAssets>();
            ownsAssets = true;
        }
        EnsureMaterials();

        cities.Clear();
        if (restored != null)
        {
            for (int i = 0; i < restored.Length; i++)
            {
                if (restored[i] != null
                    && !string.IsNullOrWhiteSpace(
                        restored[i].cityId))
                {
                    NormalizeCity(restored[i]);
                    cities.Add(restored[i]);
                }
            }
        }

        configured = world != null
            && terrain != null
            && droppedObjects != null;
        if (!configured)
            return;
        terrain.ChunkActivated += HandleChunkActivated;
        terrain.ChunkRecycled += HandleChunkRecycled;
        droppedObjects.DraftChanged += HandleDraftChanged;
        terrain.GetActiveCoordinates(coordinateBuffer);
        for (int i = 0; i < coordinateBuffer.Count; i++)
            activeChunks.Add(coordinateBuffer[i]);
        RegisterMenuActions();
        RefreshStreaming();
    }

    void Update()
    {
        // The planar world is configured from GalaxyTravelManager.Awake, while
        // SurfaceMultifunctionController is added by the player's Start method.
        // Retry until that component exists so the city actions are not lost
        // because of Unity's Awake/Start ordering.
        if (configured
            && (!menuActionsRegistered || multifunction == null))
        {
            RegisterMenuActions();
        }
    }

    public GalaxyPlanarCitySaveEntry[] CaptureSnapshots()
    {
        var collection = new GalaxyPlanarCitySaveCollection
        {
            cities = cities.ToArray()
        };
        string json = JsonUtility.ToJson(collection);
        GalaxyPlanarCitySaveCollection clone =
            JsonUtility.FromJson<
                GalaxyPlanarCitySaveCollection>(json);
        return clone?.cities
            ?? new GalaxyPlanarCitySaveEntry[0];
    }

    public bool CanGenerateCurrentDraft()
    {
        return TryResolveDraft(
            out _,
            out _,
            out _,
            false);
    }

    public bool TryGenerateCurrentDraft()
    {
        if (!TryResolveDraft(
                out List<GalaxyDroppedObjectSaveEntry> markers,
                out List<Vector2> localBoundary,
                out DraftAddress draft,
                true))
        {
            return false;
        }

        GalaxyPlanarCitySaveEntry expansionCity =
            FindExpansionCity(markers);
        string cityId = expansionCity != null
            ? expansionCity.cityId
            : Guid.NewGuid().ToString("N");
        string districtId = Guid.NewGuid().ToString("N");
        var sampler = new PlanetTerrainSampler(
            world,
            draft.anchorX,
            draft.anchorZ);
        CityGenerationSettings settings =
            assets.generationSettings != null
                ? assets.generationSettings.ValidatedCopy()
                : new CityGenerationSettings();
        int seed = StableSeed(
            world.Definition != null
                ? world.Definition.seed
                : 0,
            cityId,
            districtId);
        CityGenerationResult result =
            new CityGenerator().Generate(
                localBoundary,
                settings,
                seed,
                sampler);
        if (!result.IsSuccess)
        {
            status = result.Error;
            return false;
        }

        FilterGeneratedContent(
            result,
            draft.anchorX,
            draft.anchorZ);
        if (result.Roads.Count == 0
            || result.Blocks.Count == 0
            || result.Buildings.Count == 0)
        {
            status = "新增区域没有足够空间生成道路、街区和建筑。";
            return false;
        }

        List<List<Vector2>> platformRegions =
            BuildPlatformDifferenceRegions(
                result.Regions,
                draft.anchorX,
                draft.anchorZ);
        if (platformRegions.Count == 0)
        {
            status = "选区已被现有城市完全覆盖。";
            return false;
        }

        CityPlatformLayout platform =
            CityElevatedPlatformPlanner.Create(
                platformRegions,
                sampler,
                assets.platformTopClearance,
                assets.platformSlabThickness);
        GalaxyCityRoadSaveEntry connector = null;
        if (expansionCity != null
            && !TryCreateConnector(
                expansionCity,
                result,
                draft.anchorX,
                draft.anchorZ,
                platform.TopHeight,
                settings.maximumRoadGrade,
                out connector))
        {
            status = "新区与旧城之间没有坡度安全的道路连接。";
            return false;
        }

        GalaxyPlanarCityDistrictSaveEntry district =
            BuildDistrictSnapshot(
                districtId,
                draft,
                result,
                platform,
                platformRegions,
                connector);
        GalaxyPlanarCitySaveEntry city = expansionCity
            ?? new GalaxyPlanarCitySaveEntry
            {
                cityId = cityId,
                boundary = ToGlobalPoints(markers),
                districts =
                    new GalaxyPlanarCityDistrictSaveEntry[0]
            };
        city.districts = Append(city.districts, district);
        if (expansionCity == null)
            cities.Add(city);

        if (!droppedObjects.ClaimCurrentDraft(
                cityId,
                districtId))
        {
            city.districts = RemoveLast(city.districts);
            if (expansionCity == null)
                cities.Remove(city);
            status = "边界点状态已变化，请重新确认。";
            return false;
        }
        if (expansionCity != null)
        {
            city.boundary = AppendRange(
                city.boundary,
                ToGlobalPoints(markers));
        }

        RuntimeDistrict runtime = SpawnDistrict(
            city,
            district,
            true);
        if (runtime == null)
        {
            district.constructionComplete = true;
            status = "城市已生成，但施工展示无法启动。";
        }
        else
        {
            constructionRoutine =
                StartCoroutine(PlayConstruction(runtime));
        }
        return true;
    }

    public bool UndoLastDraftPoint()
    {
        bool removed =
            droppedObjects != null
            && droppedObjects.TryUndoLastDraftPoint();
        status = removed
            ? "已撤销最后一个城市占点。"
            : "当前没有可撤销的城市占点。";
        return removed;
    }

    public int CancelCurrentDraft()
    {
        int removed = droppedObjects != null
            ? droppedObjects.CancelCurrentDraft()
            : 0;
        status = removed > 0
            ? "已取消当前城市选区。"
            : "当前没有城市选区。";
        return removed;
    }

    bool TryResolveDraft(
        out List<GalaxyDroppedObjectSaveEntry> markers,
        out List<Vector2> localBoundary,
        out DraftAddress draft,
        bool updateStatus)
    {
        markers = new List<GalaxyDroppedObjectSaveEntry>();
        localBoundary = new List<Vector2>();
        draft = default;
        if (!configured || IsConstructing)
        {
            if (updateStatus)
                status = "城市施工正在进行。";
            return false;
        }
        droppedObjects.GetCurrentDraftRecords(markers);
        int locked = 0;
        int pending = 0;
        for (int i = 0; i < markers.Count; i++)
        {
            if (markers[i].cityBoundaryLocked)
                locked++;
            else
                pending++;
        }
        if (locked < 3 || pending > 0)
        {
            if (updateStatus)
            {
                status = pending > 0
                    ? $"等待 {pending} 个方块停稳。"
                    : "至少需要 3 个已锁定的城市占点。";
            }
            return false;
        }

        double sumX = 0d;
        double sumZ = 0d;
        for (int i = 0; i < markers.Count; i++)
        {
            sumX += markers[i].planarX;
            sumZ += markers[i].planarZ;
        }
        draft.anchorX = sumX / markers.Count;
        draft.anchorZ = sumZ / markers.Count;
        double minimumX = double.MaxValue;
        double maximumX = double.MinValue;
        double minimumZ = double.MaxValue;
        double maximumZ = double.MinValue;
        for (int i = 0; i < markers.Count; i++)
        {
            GalaxyDroppedObjectSaveEntry marker = markers[i];
            localBoundary.Add(new Vector2(
                (float)(marker.planarX - draft.anchorX),
                (float)(marker.planarZ - draft.anchorZ)));
            minimumX = Math.Min(minimumX, marker.planarX);
            maximumX = Math.Max(maximumX, marker.planarX);
            minimumZ = Math.Min(minimumZ, marker.planarZ);
            maximumZ = Math.Max(maximumZ, marker.planarZ);
        }
        draft.minimumX = minimumX;
        draft.maximumX = maximumX;
        draft.minimumZ = minimumZ;
        draft.maximumZ = maximumZ;
        if (maximumX - minimumX > MaximumDraftSpan
            || maximumZ - minimumZ > MaximumDraftSpan)
        {
            if (updateStatus)
                status = "单次城市选区跨度不能超过 512 米。";
            return false;
        }

        CityGenerationSettings settings =
            assets != null && assets.generationSettings != null
                ? assets.generationSettings
                : new CityGenerationSettings();
        if (!CityPolygonGeometry.ValidateBoundary(
                localBoundary,
                settings,
                out string error))
        {
            if (updateStatus)
                status = error;
            return false;
        }
        return true;
    }

    GalaxyPlanarCitySaveEntry FindExpansionCity(
        List<GalaxyDroppedObjectSaveEntry> markers)
    {
        GalaxyPlanarCitySaveEntry best = null;
        double bestDistance = double.MaxValue;
        for (int cityIndex = 0;
             cityIndex < cities.Count;
             cityIndex++)
        {
            GalaxyPlanarCitySaveEntry city = cities[cityIndex];
            double distance = DistanceToCity(markers, city);
            if (distance > ExpansionDistance)
                continue;
            if (distance < bestDistance - 0.0001d
                || (Math.Abs(distance - bestDistance) <= 0.0001d
                    && string.CompareOrdinal(
                        city.cityId,
                        best?.cityId) < 0))
            {
                best = city;
                bestDistance = distance;
            }
        }
        return best;
    }

    double DistanceToCity(
        List<GalaxyDroppedObjectSaveEntry> markers,
        GalaxyPlanarCitySaveEntry city)
    {
        double best = double.MaxValue;
        GalaxyPlanarCityDistrictSaveEntry[] districts =
            city?.districts;
        if (districts == null)
            return best;
        for (int districtIndex = 0;
             districtIndex < districts.Length;
             districtIndex++)
        {
            GalaxyPlanarCityDistrictSaveEntry district =
                districts[districtIndex];
            if (district?.boundaryRegions == null)
                continue;
            for (int regionIndex = 0;
                 regionIndex < district.boundaryRegions.Length;
                 regionIndex++)
            {
                Vector2[] region =
                    district.boundaryRegions[regionIndex]?.points;
                if (region == null || region.Length < 2)
                    continue;
                for (int markerIndex = 0;
                     markerIndex < markers.Count;
                     markerIndex++)
                {
                    Vector2 markerLocal = new Vector2(
                        (float)(markers[markerIndex].planarX
                            - district.anchorX),
                        (float)(markers[markerIndex].planarZ
                            - district.anchorZ));
                    if (CityPolygonGeometry.ContainsPoint(
                            region,
                            markerLocal))
                    {
                        return 0d;
                    }
                    for (int edge = 0;
                         edge < region.Length;
                         edge++)
                    {
                        best = Math.Min(
                            best,
                            DistancePointToSegment(
                                markerLocal,
                                region[edge],
                                region[
                                    (edge + 1)
                                    % region.Length]));
                    }
                }
            }
        }
        return best;
    }

    void FilterGeneratedContent(
        CityGenerationResult result,
        double anchorX,
        double anchorZ)
    {
        result.Roads.RemoveAll(road =>
            IsInsideAnyCity(
                anchorX + road.Start.x,
                anchorZ + road.Start.y)
            || IsInsideAnyCity(
                anchorX + road.End.x,
                anchorZ + road.End.y)
            || IsInsideAnyCity(
                anchorX
                    + (road.Start.x + road.End.x) * 0.5d,
                anchorZ
                    + (road.Start.y + road.End.y) * 0.5d));
        result.Blocks.RemoveAll(block =>
            PolygonOverlapsAnyCity(
                block.Footprint,
                anchorX,
                anchorZ));
        result.Lots.RemoveAll(lot =>
            PolygonOverlapsAnyCity(
                lot.Footprint,
                anchorX,
                anchorZ));
        result.Buildings.RemoveAll(building =>
            PolygonOverlapsAnyCity(
                building.Footprint,
                anchorX,
                anchorZ));
    }

    List<List<Vector2>> BuildPlatformDifferenceRegions(
        IReadOnlyList<List<Vector2>> sourceRegions,
        double anchorX,
        double anchorZ)
    {
        var result = new List<List<Vector2>>();
        if (sourceRegions == null)
            return result;
        for (int regionIndex = 0;
             regionIndex < sourceRegions.Count;
             regionIndex++)
        {
            IReadOnlyList<Vector2> region =
                sourceRegions[regionIndex];
            if (!CityPolygonGeometry.TryTriangulate(
                    region,
                    out List<int> triangles))
            {
                continue;
            }
            for (int index = 0;
                 index < triangles.Count;
                 index += 3)
            {
                SubdivideDifferenceTriangle(
                    region[triangles[index]],
                    region[triangles[index + 1]],
                    region[triangles[index + 2]],
                    anchorX,
                    anchorZ,
                    result,
                    0);
            }
        }
        return result;
    }

    void SubdivideDifferenceTriangle(
        Vector2 a,
        Vector2 b,
        Vector2 c,
        double anchorX,
        double anchorZ,
        List<List<Vector2>> output,
        int depth)
    {
        float longest = Mathf.Max(
            Vector2.Distance(a, b),
            Vector2.Distance(b, c),
            Vector2.Distance(c, a));
        Vector2 center = (a + b + c) / 3f;
        bool inside = IsInsideAnyCity(
            anchorX + center.x,
            anchorZ + center.y);
        bool overlaps = TriangleOverlapsAnyCity(
            a,
            b,
            c,
            anchorX,
            anchorZ);
        if (!overlaps)
        {
            if (!inside)
                output.Add(new List<Vector2> { a, b, c });
            return;
        }
        if (longest <= DifferenceSubdivisionSize
            || depth >= 8)
        {
            return;
        }
        Vector2 ab = (a + b) * 0.5f;
        Vector2 bc = (b + c) * 0.5f;
        Vector2 ca = (c + a) * 0.5f;
        SubdivideDifferenceTriangle(
            a, ab, ca, anchorX, anchorZ, output, depth + 1);
        SubdivideDifferenceTriangle(
            ab, b, bc, anchorX, anchorZ, output, depth + 1);
        SubdivideDifferenceTriangle(
            ca, bc, c, anchorX, anchorZ, output, depth + 1);
        SubdivideDifferenceTriangle(
            ab, bc, ca, anchorX, anchorZ, output, depth + 1);
    }

    bool TriangleOverlapsAnyCity(
        Vector2 a,
        Vector2 b,
        Vector2 c,
        double anchorX,
        double anchorZ)
    {
        for (int cityIndex = 0;
             cityIndex < cities.Count;
             cityIndex++)
        {
            GalaxyPlanarCityDistrictSaveEntry[] districts =
                cities[cityIndex].districts;
            for (int districtIndex = 0;
                 districtIndex < districts.Length;
                 districtIndex++)
            {
                GalaxyPlanarCityDistrictSaveEntry district =
                    districts[districtIndex];
                var triangle = new[]
                {
                    new Vector2(
                        (float)(anchorX + a.x
                            - district.anchorX),
                        (float)(anchorZ + a.y
                            - district.anchorZ)),
                    new Vector2(
                        (float)(anchorX + b.x
                            - district.anchorX),
                        (float)(anchorZ + b.y
                            - district.anchorZ)),
                    new Vector2(
                        (float)(anchorX + c.x
                            - district.anchorX),
                        (float)(anchorZ + c.y
                            - district.anchorZ))
                };
                GalaxyCityPolygonSaveEntry[] regions =
                    district.platformRegions;
                for (int regionIndex = 0;
                     regionIndex < regions.Length;
                     regionIndex++)
                {
                    Vector2[] region =
                        regions[regionIndex]?.points;
                    if (region == null || region.Length < 3)
                        continue;
                    for (int triangleIndex = 0;
                         triangleIndex < 3;
                         triangleIndex++)
                    {
                        if (CityPolygonGeometry.ContainsPoint(
                                region,
                                triangle[triangleIndex]))
                        {
                            return true;
                        }
                    }
                    for (int pointIndex = 0;
                         pointIndex < region.Length;
                         pointIndex++)
                    {
                        if (CityPolygonGeometry.ContainsPoint(
                                triangle,
                                region[pointIndex]))
                        {
                            return true;
                        }
                    }
                    for (int triangleEdge = 0;
                         triangleEdge < 3;
                         triangleEdge++)
                    {
                        Vector2 triangleStart =
                            triangle[triangleEdge];
                        Vector2 triangleEnd =
                            triangle[(triangleEdge + 1) % 3];
                        for (int regionEdge = 0;
                             regionEdge < region.Length;
                             regionEdge++)
                        {
                            if (CityPolygonGeometry
                                .SegmentsIntersect(
                                    triangleStart,
                                    triangleEnd,
                                    region[regionEdge],
                                    region[
                                        (regionEdge + 1)
                                        % region.Length]))
                            {
                                return true;
                            }
                        }
                    }
                }
            }
        }
        return false;
    }

    bool TryCreateConnector(
        GalaxyPlanarCitySaveEntry city,
        CityGenerationResult newResult,
        double newAnchorX,
        double newAnchorZ,
        float newHeight,
        float maximumGrade,
        out GalaxyCityRoadSaveEntry connector)
    {
        connector = null;
        double bestDistance = double.MaxValue;
        Vector2 bestOldGlobal = default;
        float bestOldHeight = 0f;
        Vector2 bestNewLocal = default;
        GalaxyPlanarCityDistrictSaveEntry[] districts =
            city?.districts;
        if (districts == null)
            return false;
        for (int districtIndex = 0;
             districtIndex < districts.Length;
             districtIndex++)
        {
            GalaxyPlanarCityDistrictSaveEntry old =
                districts[districtIndex];
            if (old?.roads == null)
                continue;
            for (int oldRoadIndex = 0;
                 oldRoadIndex < old.roads.Length;
                 oldRoadIndex++)
            {
                GalaxyCityRoadSaveEntry oldRoad =
                    old.roads[oldRoadIndex];
                Vector2[] oldPoints =
                    { oldRoad.start, oldRoad.end };
                for (int oldPointIndex = 0;
                     oldPointIndex < oldPoints.Length;
                     oldPointIndex++)
                {
                    Vector2 oldGlobal = new Vector2(
                        (float)(old.anchorX
                            + oldPoints[oldPointIndex].x),
                        (float)(old.anchorZ
                            + oldPoints[oldPointIndex].y));
                    for (int newRoadIndex = 0;
                         newRoadIndex < newResult.Roads.Count;
                         newRoadIndex++)
                    {
                        CityRoadSegment newRoad =
                            newResult.Roads[newRoadIndex];
                        Vector2[] newPoints =
                            { newRoad.Start, newRoad.End };
                        for (int newPointIndex = 0;
                             newPointIndex < newPoints.Length;
                             newPointIndex++)
                        {
                            Vector2 newGlobal = new Vector2(
                                (float)(newAnchorX
                                    + newPoints[newPointIndex].x),
                                (float)(newAnchorZ
                                    + newPoints[newPointIndex].y));
                            double distance =
                                Vector2.Distance(
                                    oldGlobal,
                                    newGlobal);
                            float grade = Mathf.Abs(
                                    newHeight
                                    - old.platformTopHeight)
                                / Mathf.Max(0.1f, (float)distance);
                            if (grade > maximumGrade
                                || distance >= bestDistance)
                            {
                                continue;
                            }
                            bestDistance = distance;
                            bestOldGlobal = oldGlobal;
                            bestOldHeight =
                                old.platformTopHeight + 0.06f;
                            bestNewLocal =
                                newPoints[newPointIndex];
                        }
                    }
                }
            }
        }
        if (bestDistance == double.MaxValue
            || bestDistance > 96d)
        {
            return false;
        }
        connector = new GalaxyCityRoadSaveEntry
        {
            start = new Vector2(
                (float)(bestOldGlobal.x - newAnchorX),
                (float)(bestOldGlobal.y - newAnchorZ)),
            end = bestNewLocal,
            width = Mathf.Max(
                3f,
                assets.generationSettings.majorRoadWidth),
            major = true,
            connector = true,
            startHeight = bestOldHeight,
            endHeight = newHeight + 0.06f
        };
        return true;
    }

    GalaxyPlanarCityDistrictSaveEntry BuildDistrictSnapshot(
        string districtId,
        DraftAddress draft,
        CityGenerationResult result,
        CityPlatformLayout platform,
        List<List<Vector2>> platformRegions,
        GalaxyCityRoadSaveEntry connector)
    {
        var roads = new List<GalaxyCityRoadSaveEntry>(
            result.Roads.Count + (connector != null ? 1 : 0));
        for (int i = 0; i < result.Roads.Count; i++)
        {
            CityRoadSegment road = result.Roads[i];
            roads.Add(new GalaxyCityRoadSaveEntry
            {
                start = road.Start,
                end = road.End,
                width = road.Width,
                major = road.IsMajor,
                startHeight = platform.TopHeight + 0.05f,
                endHeight = platform.TopHeight + 0.05f
            });
        }
        if (connector != null)
            roads.Add(connector);

        var buildings =
            new GalaxyCityBuildingSaveEntry[
                result.Buildings.Count];
        for (int i = 0; i < buildings.Length; i++)
        {
            CityBuildingData building =
                result.Buildings[i];
            buildings[i] =
                new GalaxyCityBuildingSaveEntry
                {
                    footprint = CopyPoints(
                        building.Footprint),
                    height = building.Height,
                    materialIndex =
                        building.MaterialIndex,
                    prefabIndex = building.PrefabIndex
                };
        }
        double minimumX = draft.minimumX;
        double maximumX = draft.maximumX;
        double minimumZ = draft.minimumZ;
        double maximumZ = draft.maximumZ;
        if (connector != null)
        {
            minimumX = Math.Min(
                minimumX,
                draft.anchorX
                    + Math.Min(
                        connector.start.x,
                        connector.end.x));
            maximumX = Math.Max(
                maximumX,
                draft.anchorX
                    + Math.Max(
                        connector.start.x,
                        connector.end.x));
            minimumZ = Math.Min(
                minimumZ,
                draft.anchorZ
                    + Math.Min(
                        connector.start.y,
                        connector.end.y));
            maximumZ = Math.Max(
                maximumZ,
                draft.anchorZ
                    + Math.Max(
                        connector.start.y,
                        connector.end.y));
        }
        return new GalaxyPlanarCityDistrictSaveEntry
        {
            districtId = districtId,
            anchorX = draft.anchorX,
            anchorZ = draft.anchorZ,
            minimumX = minimumX,
            maximumX = maximumX,
            minimumZ = minimumZ,
            maximumZ = maximumZ,
            minimumGroundHeight =
                platform.MinimumGroundHeight,
            maximumGroundHeight =
                platform.MaximumGroundHeight,
            platformTopHeight = platform.TopHeight,
            platformSlabThickness =
                platform.SlabThickness,
            constructionComplete = false,
            boundaryRegions =
                ToPolygonEntries(result.Regions),
            platformRegions =
                ToPolygonEntries(platformRegions),
            roads = roads.ToArray(),
            blocks = ToPolygonEntries(result.Blocks),
            lots = ToPolygonEntries(result.Lots),
            buildings = buildings
        };
    }

    RuntimeDistrict SpawnDistrict(
        GalaxyPlanarCitySaveEntry city,
        GalaxyPlanarCityDistrictSaveEntry district,
        bool animate)
    {
        if (district == null
            || string.IsNullOrWhiteSpace(district.districtId))
        {
            return null;
        }
        if (runtimeDistricts.TryGetValue(
                district.districtId,
                out RuntimeDistrict existing)
            && existing.root != null)
        {
            return existing;
        }

        GameObject rootObject = new GameObject(
            "PlanarCityDistrict_"
            + district.districtId);
        rootObject.transform.SetParent(transform, true);
        rootObject.transform.position =
            world.FromPersistentAddress(
                new PlanarSurfaceAddress(
                    district.anchorX,
                    district.anchorZ,
                    0f));
        rootObject.transform.rotation = Quaternion.identity;
        rootObject.AddComponent<
            PlanetFloatingOriginParticipant>();
        var runtime = new RuntimeDistrict
        {
            city = city,
            data = district,
            root = rootObject
        };
        runtimeDistricts[district.districtId] = runtime;
        if (animate)
            PrepareAnimatedDistrict(runtime);
        else
            BuildCompletedDistrict(runtime);
        return runtime;
    }

    void BuildCompletedDistrict(RuntimeDistrict runtime)
    {
        DistrictRoots roots = CreateDistrictRoots(
            runtime.root.transform);
        CreateFoundation(runtime.data, roots.foundations);
        for (int i = 0;
             i < runtime.data.roads.Length;
             i++)
        {
            CreateRoad(
                runtime.data,
                runtime.data.roads[i],
                i,
                roots.roads);
        }
        for (int i = 0;
             i < runtime.data.blocks.Length;
             i++)
        {
            CreateBlock(
                runtime.data,
                runtime.data.blocks[i],
                i,
                roots.blocks);
        }
        for (int i = 0;
             i < runtime.data.buildings.Length;
             i++)
        {
            CreateBuilding(
                runtime.data,
                runtime.data.buildings[i],
                i,
                roots.buildings);
        }
    }

    void PrepareAnimatedDistrict(RuntimeDistrict runtime)
    {
        DistrictRoots roots = CreateDistrictRoots(
            runtime.root.transform);
        runtime.animator =
            runtime.root.AddComponent<
                CityConstructionAnimator>();
        runtime.animator.Configure(roots.effects);
        List<Vector2> boundary =
            FirstBoundary(runtime.data);
        Vector2 center = Average(boundary);
        var job = new CityConstructionJob(
            boundary,
            center,
            runtime.data.minimumGroundHeight - 0.25f,
            runtime.data.platformTopHeight
                - runtime.data.platformSlabThickness,
            runtime.data.platformTopHeight,
            runtime.root.transform);
        job.Foundations.Add(new CityConstructionItem(
            0,
            center,
            () => CreateFoundation(
                runtime.data,
                roots.foundations)));
        for (int i = 0;
             i < runtime.data.roads.Length;
             i++)
        {
            int index = i;
            GalaxyCityRoadSaveEntry road =
                runtime.data.roads[i];
            job.Roads.Add(new CityConstructionItem(
                index,
                (road.start + road.end) * 0.5f,
                () => CreateRoad(
                    runtime.data,
                    road,
                    index,
                    roots.roads)));
        }
        for (int i = 0;
             i < runtime.data.blocks.Length;
             i++)
        {
            int index = i;
            GalaxyCityPolygonSaveEntry block =
                runtime.data.blocks[i];
            job.Blocks.Add(new CityConstructionItem(
                index,
                Average(block.points),
                () => CreateBlock(
                    runtime.data,
                    block,
                    index,
                    roots.blocks)));
        }
        for (int i = 0;
             i < runtime.data.buildings.Length;
             i++)
        {
            int index = i;
            GalaxyCityBuildingSaveEntry building =
                runtime.data.buildings[i];
            job.Buildings.Add(new CityConstructionItem(
                index,
                Average(building.footprint),
                () => CreateBuilding(
                    runtime.data,
                    building,
                    index,
                    roots.buildings)));
        }
        runtime.root.name += "_Constructing";
        runtimeJob = job;
    }

    CityConstructionJob runtimeJob;

    IEnumerator PlayConstruction(RuntimeDistrict runtime)
    {
        constructingDistrict = runtime;
        SurfaceSpacecraftController ship =
            SurfaceSpacecraftController.Current;
        bool piloting = ship != null && ship.IsPiloting;
        if (piloting)
            ship.SetCityPresentationActive(true);
        else
            multifunction?.SetInputBlocked(true);

        Camera camera = Camera.main;
        SurfaceFlightCameraRig rig =
            ship != null && ship.SurfaceCameraRig != null
                ? ship.SurfaceCameraRig
                : camera != null
                    ? camera.GetComponent<
                          SurfaceFlightCameraRig>()
                        ?? camera.gameObject.AddComponent<
                            SurfaceFlightCameraRig>()
                    : null;
        Bounds bounds = CalculatePresentationBounds(
            runtime.data);
        bool hasCamera = rig != null
            && rig.BeginCityPresentation(
                camera,
                runtime.root.transform,
                bounds);
        float cameraWait = 0f;
        while (hasCamera
            && !rig.IsCityPresentationSettled
            && cameraWait < 2f)
        {
            cameraWait += Time.unscaledDeltaTime;
            yield return null;
        }

        bool completed = false;
        if (runtime.animator != null
            && runtimeJob != null)
        {
            runtime.animator.Begin(
                runtimeJob,
                HandleConstructionProgress,
                () => completed = true);
            while (runtime.animator != null
                && runtime.animator.IsRunning)
            {
                yield return null;
            }
        }
        else
        {
            completed = true;
        }
        runtime.data.constructionComplete = completed;
        runtime.root.name = "PlanarCityDistrict_"
            + runtime.data.districtId;

        if (hasCamera)
        {
            rig.EndCityPresentation();
            float restoreWait = 0f;
            while (!rig.IsCityPresentationRestored
                && restoreWait < 2f)
            {
                restoreWait += Time.unscaledDeltaTime;
                yield return null;
            }
        }
        if (piloting && ship != null)
            ship.SetCityPresentationActive(false);
        else
            multifunction?.SetInputBlocked(false);
        status = completed
            ? "城市施工完成。"
            : "城市施工展示已中断。";
        constructingDistrict = null;
        runtimeJob = null;
        constructionRoutine = null;
    }

    GameObject CreateFoundation(
        GalaxyPlanarCityDistrictSaveEntry district,
        Transform parent)
    {
        List<List<Vector2>> regions =
            FromPolygonEntries(district.platformRegions);
        if (regions.Count == 0)
            return null;
        return CityRuntimeMeshFactory
            .CreateElevatedCityPlatforms(
                "ElevatedPlatform",
                regions,
                district.platformTopHeight,
                district.platformSlabThickness,
                assets.platformSupportSpacing,
                assets.platformSupportWidth,
                assets.platformMinimumSupportHeight,
                new PlanetTerrainSampler(
                    world,
                    district.anchorX,
                    district.anchorZ),
                foundationMaterial,
                parent,
                out _,
                out _);
    }

    GameObject CreateRoad(
        GalaxyPlanarCityDistrictSaveEntry district,
        GalaxyCityRoadSaveEntry road,
        int index,
        Transform parent)
    {
        if (road.connector
            || Mathf.Abs(
                road.startHeight - road.endHeight) > 0.01f)
        {
            return CreateSlopedRoad(
                "ConnectorRoad_" + index,
                road,
                parent);
        }
        Vector2 direction =
            (road.end - road.start).normalized;
        Vector2 normal = new Vector2(
            -direction.y,
            direction.x) * (road.width * 0.5f);
        return CityRuntimeMeshFactory.CreateFlatObject(
            (road.major ? "MajorRoad_" : "MinorRoad_")
                + index,
            new[]
            {
                road.start + normal,
                road.end + normal,
                road.end - normal,
                road.start - normal
            },
            road.startHeight,
            roadMaterial,
            parent);
    }

    GameObject CreateBlock(
        GalaxyPlanarCityDistrictSaveEntry district,
        GalaxyCityPolygonSaveEntry block,
        int index,
        Transform parent)
    {
        return block?.points == null
            ? null
            : CityRuntimeMeshFactory.CreateExtrudedObject(
                "Block_" + index,
                block.points,
                district.platformTopHeight + 0.08f,
                0.14f,
                blockMaterial,
                parent);
    }

    GameObject CreateBuilding(
        GalaxyPlanarCityDistrictSaveEntry district,
        GalaxyCityBuildingSaveEntry building,
        int index,
        Transform parent)
    {
        if (building?.footprint == null)
            return null;
        Material material =
            buildingMaterials[
                Mathf.Abs(building.materialIndex)
                % buildingMaterials.Length];
        GameObject prefab = assets.useBuildingPrefabs
            && assets.buildingPrefabs != null
            && assets.buildingPrefabs.Length > 0
                ? assets.buildingPrefabs[
                    Mathf.Abs(building.prefabIndex)
                    % assets.buildingPrefabs.Length]
                : null;
        if (prefab != null)
        {
            return CityRuntimeMeshFactory
                .CreatePrefabBuildingOnPlane(
                    "Building_" + index,
                    prefab,
                    building.footprint,
                    district.platformTopHeight,
                    material,
                    assets.overridePrefabMaterials,
                    parent);
        }
        return CityRuntimeMeshFactory.CreateExtrudedObject(
            "Building_" + index,
            building.footprint,
            district.platformTopHeight + 0.03f,
            building.height,
            material,
            parent);
    }

    GameObject CreateSlopedRoad(
        string objectName,
        GalaxyCityRoadSaveEntry road,
        Transform parent)
    {
        Vector2 direction =
            (road.end - road.start).normalized;
        Vector2 normal = new Vector2(
            -direction.y,
            direction.x) * (road.width * 0.5f);
        var vertices = new[]
        {
            new Vector3(
                road.start.x + normal.x,
                road.startHeight,
                road.start.y + normal.y),
            new Vector3(
                road.end.x + normal.x,
                road.endHeight,
                road.end.y + normal.y),
            new Vector3(
                road.end.x - normal.x,
                road.endHeight,
                road.end.y - normal.y),
            new Vector3(
                road.start.x - normal.x,
                road.startHeight,
                road.start.y - normal.y)
        };
        var mesh = new Mesh
        {
            name = objectName + " Mesh",
            vertices = vertices,
            triangles = new[] { 0, 1, 2, 0, 2, 3 }
        };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        var roadObject = new GameObject(objectName);
        roadObject.transform.SetParent(parent, false);
        roadObject.AddComponent<MeshFilter>()
            .sharedMesh = mesh;
        roadObject.AddComponent<MeshRenderer>()
            .sharedMaterial = roadMaterial;
        roadObject.AddComponent<MeshCollider>()
            .sharedMesh = mesh;
        return roadObject;
    }

    void HandleConstructionProgress(
        CityConstructionPhase phase,
        float progress,
        float secondsRemaining)
    {
        status = $"城市施工 {progress * 100f:0}% · "
            + $"{secondsRemaining:0.0}s";
    }

    void HandleChunkActivated(Vector2Int coordinate)
    {
        activeChunks.Add(coordinate);
        RefreshStreaming();
    }

    void HandleChunkRecycled(Vector2Int coordinate)
    {
        activeChunks.Remove(coordinate);
        RefreshStreaming();
    }

    void RefreshStreaming()
    {
        for (int cityIndex = 0;
             cityIndex < cities.Count;
             cityIndex++)
        {
            GalaxyPlanarCitySaveEntry city =
                cities[cityIndex];
            GalaxyPlanarCityDistrictSaveEntry[] districts =
                city.districts;
            for (int districtIndex = 0;
                 districtIndex < districts.Length;
                 districtIndex++)
            {
                GalaxyPlanarCityDistrictSaveEntry district =
                    districts[districtIndex];
                bool shouldBeActive =
                    IsDistrictInActiveWindow(district)
                    || constructingDistrict?.data == district;
                runtimeDistricts.TryGetValue(
                    district.districtId,
                    out RuntimeDistrict runtime);
                if (shouldBeActive
                    && (runtime == null
                        || runtime.root == null))
                {
                    SpawnDistrict(
                        city,
                        district,
                        false);
                }
                else if (!shouldBeActive
                    && runtime?.root != null)
                {
                    Destroy(runtime.root);
                    runtime.root = null;
                    runtime.animator = null;
                }
            }
        }
    }

    bool IsDistrictInActiveWindow(
        GalaxyPlanarCityDistrictSaveEntry district)
    {
        if (district == null)
            return false;
        Vector2Int minimum = terrain.GetChunkCoordinate(
            district.minimumX,
            district.minimumZ);
        Vector2Int maximum = terrain.GetChunkCoordinate(
            district.maximumX,
            district.maximumZ);
        foreach (Vector2Int active in activeChunks)
        {
            if (active.x >= minimum.x
                && active.x <= maximum.x
                && active.y >= minimum.y
                && active.y <= maximum.y)
            {
                return true;
            }
        }
        return false;
    }

    bool IsInsideAnyCity(double globalX, double globalZ)
    {
        for (int cityIndex = 0;
             cityIndex < cities.Count;
             cityIndex++)
        {
            GalaxyPlanarCityDistrictSaveEntry[] districts =
                cities[cityIndex].districts;
            for (int districtIndex = 0;
                 districtIndex < districts.Length;
                 districtIndex++)
            {
                GalaxyPlanarCityDistrictSaveEntry district =
                    districts[districtIndex];
                Vector2 local = new Vector2(
                    (float)(globalX - district.anchorX),
                    (float)(globalZ - district.anchorZ));
                GalaxyCityPolygonSaveEntry[] regions =
                    district.platformRegions;
                for (int regionIndex = 0;
                     regionIndex < regions.Length;
                     regionIndex++)
                {
                    Vector2[] points =
                        regions[regionIndex]?.points;
                    if (points != null
                        && CityPolygonGeometry.ContainsPoint(
                            points,
                            local))
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    bool PolygonOverlapsAnyCity(
        IReadOnlyList<Vector2> polygon,
        double anchorX,
        double anchorZ)
    {
        if (polygon == null || polygon.Count == 0)
            return false;
        for (int i = 0; i < polygon.Count; i++)
        {
            if (IsInsideAnyCity(
                    anchorX + polygon[i].x,
                    anchorZ + polygon[i].y))
            {
                return true;
            }
        }
        Vector2 center = Average(polygon);
        return IsInsideAnyCity(
            anchorX + center.x,
            anchorZ + center.y);
    }

    void RegisterMenuActions()
    {
        if (menuActionsRegistered && multifunction != null)
            return;

        if (player == null && world != null)
            player = world.Player;
        SurfaceMultifunctionController candidate =
            player != null
                ? player.GetComponent<SurfaceMultifunctionController>()
                : null;
        if (candidate == null)
            return;

        if (multifunction != null && multifunction != candidate)
            UnregisterMenuActions();
        multifunction = candidate;

        // Clear stale entries left by a domain-reload-disabled play session
        // before installing the callbacks owned by this runtime instance.
        multifunction.UnregisterAction(GenerateActionId);
        multifunction.UnregisterAction(UndoActionId);
        multifunction.UnregisterAction(CancelActionId);

        bool generateRegistered =
            multifunction.RegisterDynamicAction(
            GenerateActionId,
            GetGenerateLabel,
            () => TryGenerateCurrentDraft(),
            CanGenerateCurrentDraft);
        bool undoRegistered = multifunction.RegisterAction(
            UndoActionId,
            "撤销最后占点",
            () => UndoLastDraftPoint(),
            () => droppedObjects != null
                && droppedObjects.CurrentDraftLockedCount
                    + droppedObjects.CurrentDraftPendingCount > 0);
        bool cancelRegistered = multifunction.RegisterAction(
            CancelActionId,
            "取消当前选区",
            () => CancelCurrentDraft(),
            () => droppedObjects != null
                && droppedObjects.CurrentDraftLockedCount
                    + droppedObjects.CurrentDraftPendingCount > 0);
        menuActionsRegistered = generateRegistered
            && undoRegistered
            && cancelRegistered;
    }

    void UnregisterMenuActions()
    {
        if (multifunction != null)
        {
            multifunction.UnregisterAction(GenerateActionId);
            multifunction.UnregisterAction(UndoActionId);
            multifunction.UnregisterAction(CancelActionId);
        }
        menuActionsRegistered = false;
    }

    string GetGenerateLabel()
    {
        if (droppedObjects == null)
            return "生成城市";
        return $"生成城市（{droppedObjects.CurrentDraftLockedCount}"
            + $"/等待{droppedObjects.CurrentDraftPendingCount}）";
    }

    void HandleDraftChanged()
    {
        if (string.IsNullOrWhiteSpace(status)
            || !IsConstructing)
        {
            status = string.Empty;
        }
    }

    void EnsureMaterials()
    {
        roadMaterial ??=
            CityRuntimeMeshFactory.CreateMaterial(
                "Planar City Roads",
                assets.roadColor,
                0.15f);
        blockMaterial ??=
            CityRuntimeMeshFactory.CreateMaterial(
                "Planar City Blocks",
                assets.blockColor,
                0.1f);
        foundationMaterial ??=
            CityRuntimeMeshFactory.CreateMaterial(
                "Planar City Foundation",
                assets.foundationColor,
                0.3f);
        Color[] colors = assets.buildingColors;
        if (colors == null || colors.Length == 0)
            colors = new[] { Color.gray };
        buildingMaterials = new Material[colors.Length];
        for (int i = 0; i < colors.Length; i++)
        {
            buildingMaterials[i] =
                CityRuntimeMeshFactory.CreateMaterial(
                    "Planar City Building " + i,
                    colors[i],
                    0.36f);
        }
    }

    void Unsubscribe()
    {
        if (terrain != null)
        {
            terrain.ChunkActivated -= HandleChunkActivated;
            terrain.ChunkRecycled -= HandleChunkRecycled;
        }
        if (droppedObjects != null)
            droppedObjects.DraftChanged -= HandleDraftChanged;
        UnregisterMenuActions();
    }

    void NormalizeCity(GalaxyPlanarCitySaveEntry city)
    {
        city.boundary ??=
            new GalaxyPlanarPointSaveEntry[0];
        city.districts ??=
            new GalaxyPlanarCityDistrictSaveEntry[0];
        for (int i = 0; i < city.districts.Length; i++)
        {
            GalaxyPlanarCityDistrictSaveEntry district =
                city.districts[i];
            if (district == null)
                continue;
            district.boundaryRegions ??=
                new GalaxyCityPolygonSaveEntry[0];
            district.platformRegions ??=
                new GalaxyCityPolygonSaveEntry[0];
            district.roads ??=
                new GalaxyCityRoadSaveEntry[0];
            district.blocks ??=
                new GalaxyCityPolygonSaveEntry[0];
            district.lots ??=
                new GalaxyCityPolygonSaveEntry[0];
            district.buildings ??=
                new GalaxyCityBuildingSaveEntry[0];
        }
    }

    static int StableSeed(
        int planetSeed,
        string cityId,
        string districtId)
    {
        unchecked
        {
            uint hash = 2166136261u;
            hash = (hash ^ (uint)planetSeed) * 16777619u;
            string value = cityId + ":" + districtId;
            for (int i = 0; i < value.Length; i++)
                hash = (hash ^ value[i]) * 16777619u;
            return (int)hash;
        }
    }

    static GalaxyPlanarPointSaveEntry[] ToGlobalPoints(
        List<GalaxyDroppedObjectSaveEntry> markers)
    {
        var points =
            new GalaxyPlanarPointSaveEntry[markers.Count];
        for (int i = 0; i < points.Length; i++)
        {
            points[i] = new GalaxyPlanarPointSaveEntry(
                markers[i].planarX,
                markers[i].planarZ);
        }
        return points;
    }

    static GalaxyCityPolygonSaveEntry[] ToPolygonEntries(
        IReadOnlyList<List<Vector2>> values)
    {
        if (values == null)
            return new GalaxyCityPolygonSaveEntry[0];
        var result =
            new GalaxyCityPolygonSaveEntry[values.Count];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = new GalaxyCityPolygonSaveEntry
            {
                points = CopyPoints(values[i])
            };
        }
        return result;
    }

    static GalaxyCityPolygonSaveEntry[] ToPolygonEntries(
        List<CityBlockData> values)
    {
        var result =
            new GalaxyCityPolygonSaveEntry[values.Count];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = new GalaxyCityPolygonSaveEntry
            {
                points = CopyPoints(values[i].Footprint)
            };
        }
        return result;
    }

    static GalaxyCityPolygonSaveEntry[] ToPolygonEntries(
        List<CityLotData> values)
    {
        var result =
            new GalaxyCityPolygonSaveEntry[values.Count];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = new GalaxyCityPolygonSaveEntry
            {
                points = CopyPoints(values[i].Footprint)
            };
        }
        return result;
    }

    static List<List<Vector2>> FromPolygonEntries(
        GalaxyCityPolygonSaveEntry[] values)
    {
        var result = new List<List<Vector2>>();
        if (values == null)
            return result;
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i]?.points != null
                && values[i].points.Length >= 3)
            {
                result.Add(
                    new List<Vector2>(
                        values[i].points));
            }
        }
        return result;
    }

    static Vector2[] CopyPoints(
        IReadOnlyList<Vector2> source)
    {
        var result = new Vector2[source.Count];
        for (int i = 0; i < result.Length; i++)
            result[i] = source[i];
        return result;
    }

    static List<Vector2> FirstBoundary(
        GalaxyPlanarCityDistrictSaveEntry district)
    {
        if (district.boundaryRegions != null
            && district.boundaryRegions.Length > 0
            && district.boundaryRegions[0]?.points != null)
        {
            return new List<Vector2>(
                district.boundaryRegions[0].points);
        }
        return new List<Vector2>
        {
            new Vector2(-5f, -5f),
            new Vector2(5f, -5f),
            new Vector2(5f, 5f),
            new Vector2(-5f, 5f)
        };
    }

    static Vector2 Average(
        IReadOnlyList<Vector2> points)
    {
        if (points == null || points.Count == 0)
            return Vector2.zero;
        Vector2 sum = Vector2.zero;
        for (int i = 0; i < points.Count; i++)
            sum += points[i];
        return sum / points.Count;
    }

    static double DistancePointToSegment(
        Vector2 point,
        Vector2 start,
        Vector2 end)
    {
        Vector2 edge = end - start;
        float denominator = edge.sqrMagnitude;
        if (denominator <= 0.000001f)
            return Vector2.Distance(point, start);
        float ratio = Mathf.Clamp01(
            Vector2.Dot(point - start, edge)
            / denominator);
        return Vector2.Distance(
            point,
            start + edge * ratio);
    }

    static T[] Append<T>(T[] source, T value)
    {
        source ??= new T[0];
        var result = new T[source.Length + 1];
        Array.Copy(source, result, source.Length);
        result[source.Length] = value;
        return result;
    }

    static T[] AppendRange<T>(T[] source, T[] values)
    {
        source ??= new T[0];
        values ??= new T[0];
        var result =
            new T[source.Length + values.Length];
        Array.Copy(source, result, source.Length);
        Array.Copy(
            values,
            0,
            result,
            source.Length,
            values.Length);
        return result;
    }

    static T[] RemoveLast<T>(T[] source)
    {
        if (source == null || source.Length <= 1)
            return new T[0];
        var result = new T[source.Length - 1];
        Array.Copy(source, result, result.Length);
        return result;
    }

    static Bounds CalculatePresentationBounds(
        GalaxyPlanarCityDistrictSaveEntry district)
    {
        float minimumX =
            (float)(district.minimumX - district.anchorX);
        float maximumX =
            (float)(district.maximumX - district.anchorX);
        float minimumZ =
            (float)(district.minimumZ - district.anchorZ);
        float maximumZ =
            (float)(district.maximumZ - district.anchorZ);
        float maximumBuildingHeight = 0f;
        for (int i = 0; i < district.buildings.Length; i++)
        {
            maximumBuildingHeight = Mathf.Max(
                maximumBuildingHeight,
                district.buildings[i]?.height ?? 0f);
        }
        float minimumY =
            district.minimumGroundHeight - 0.25f;
        float maximumY =
            district.platformTopHeight
            + maximumBuildingHeight
            + 1f;
        for (int i = 0; i < district.roads.Length; i++)
        {
            GalaxyCityRoadSaveEntry road =
                district.roads[i];
            if (road == null)
                continue;
            minimumY = Mathf.Min(
                minimumY,
                road.startHeight,
                road.endHeight);
            maximumY = Mathf.Max(
                maximumY,
                road.startHeight,
                road.endHeight);
        }
        var bounds = new Bounds();
        bounds.SetMinMax(
            new Vector3(minimumX, minimumY, minimumZ),
            new Vector3(maximumX, maximumY, maximumZ));
        return bounds;
    }

    static DistrictRoots CreateDistrictRoots(
        Transform parent)
    {
        return new DistrictRoots
        {
            foundations = CreateRoot(
                "Foundations",
                parent),
            roads = CreateRoot("Roads", parent),
            blocks = CreateRoot("Blocks", parent),
            buildings = CreateRoot(
                "Buildings",
                parent),
            effects = CreateRoot(
                "ConstructionEffects",
                parent)
        };
    }

    static Transform CreateRoot(
        string name,
        Transform parent)
    {
        var root = new GameObject(name);
        root.transform.SetParent(parent, false);
        return root.transform;
    }

    void OnDisable()
    {
        if (constructionRoutine != null)
        {
            StopCoroutine(constructionRoutine);
            constructionRoutine = null;
        }
        SurfaceSpacecraftController.Current
            ?.SetCityPresentationActive(false);
        multifunction?.SetInputBlocked(false);
        Camera camera = Camera.main;
        camera?.GetComponent<SurfaceFlightCameraRig>()
            ?.ForceEndCityPresentation();
    }

    void OnDestroy()
    {
        Unsubscribe();
        DestroyTransient(roadMaterial);
        DestroyTransient(blockMaterial);
        DestroyTransient(foundationMaterial);
        if (buildingMaterials != null)
        {
            for (int i = 0;
                 i < buildingMaterials.Length;
                 i++)
            {
                DestroyTransient(buildingMaterials[i]);
            }
        }
        if (ownsAssets)
            DestroyTransient(assets);
    }

    static void DestroyTransient(UnityEngine.Object value)
    {
        if (value == null)
            return;
        if (Application.isPlaying)
            Destroy(value);
        else
            DestroyImmediate(value);
    }

    struct DraftAddress
    {
        public double anchorX;
        public double anchorZ;
        public double minimumX;
        public double maximumX;
        public double minimumZ;
        public double maximumZ;
    }

    struct DistrictRoots
    {
        public Transform foundations;
        public Transform roads;
        public Transform blocks;
        public Transform buildings;
        public Transform effects;
    }
}
