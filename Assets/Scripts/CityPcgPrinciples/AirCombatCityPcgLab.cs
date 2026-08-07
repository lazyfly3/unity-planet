using System;
using UnityEngine;

namespace UnityPlanet.CityPcg
{
    [Serializable]
    public sealed class AirCombatCityPalette
    {
        [Header("城市")]
        public Material road;
        public Material asphalt;
        public Material cityBlockPaving;
        public Material sidewalk;
        public Material curb;
        public Material laneMarking;
        public Material dangerLaneMarking;
        public Material repairCourtyard;
        public Material parkSurface;
        public Material lowBuilding;
        public Material mediumBuilding;
        public Material highBuilding;
        public Material facility;

        [Header("空战语义")]
        public Material mainRoute;
        public Material maskedRoute;
        public Material longRangeRoute;
        public Material suicideRoute;
        public Material rangedRoute;
        public Material spawn;
        public Material objective;
        public Material maneuverVolume;
        public Material recoveryVolume;
        public Material occlusionVolume;
        public Material exposureVolume;
    }

    /// <summary>
    /// Edit-mode visualizer for the air-combat-first city generator. The
    /// generated hierarchy deliberately exposes every semantic layer.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class AirCombatCityPcgLab : MonoBehaviour
    {
        const string GeneratedRootName =
            "Generated_AirCombatCity_空战语义先行";
        // The Dark City bridge meshes have visually recessed/open end caps.
        // Push the structural span through each facade so the visible deck,
        // collision proxy and bridge head all terminate inside real building
        // geometry rather than on an abstract lot boundary.
        const float SkybridgeFacadeEmbed = 6f;
        public const int MinimumCitySkybridges = 150;
        public const int BossCitySkybridgeTarget = 220;
        public const float TacticalChokeClearHeight = 14f;
        public const float TacticalChokeClearWidth = 22f;

        [Header("一、空战 PCG 参数")]
        [SerializeField] AirCombatCitySettings settings =
            new AirCombatCitySettings();

        [Header("二、实验场配色")]
        [SerializeField] AirCombatCityPalette palette =
            new AirCombatCityPalette();

        [Header("标准化楼房模型：底面Y0 / 顶部+Y / 正面+Z / 右侧+X")]
        [SerializeField] NewGenUrbanBuildingCatalog buildingCatalog;
        [SerializeField] DarkCity2UrbanCatalog darkCity2Catalog;
        [SerializeField] GameObject roadJunctionPrefab;
        [SerializeField] GameObject parkTreePrefab;

        [Header("NewGen Urban 装饰（运行时先标准化底面、朝向和包围盒）")]
        [SerializeField] GameObject rooftopMechanicalPrefab;
        [SerializeField] GameObject[] rooftopBillboardPrefabs =
            Array.Empty<GameObject>();
        [SerializeField] GameObject streetLightPrefab;
        [SerializeField] GameObject parkPlanterPrefab;

        [Header("三、Scene 可读性")]
        [SerializeField] bool showSemanticGizmos = true;
        [SerializeField] bool showLabels = true;
        [SerializeField] bool showRuntimePanel = true;
        [SerializeField] bool keepBuildingColliders = true;

        [Header("城市建筑破坏（不作用于地面和玩家模块）")]
        [SerializeField] UrbanDestructionSettings destructionSettings =
            new UrbanDestructionSettings();

        AirCombatCityPlan plan;
        AirCombatCityReport report;
        Transform semanticRoot;
        Transform routeRoot;
        Transform roadRoot;
        Transform buildingRoot;
        Transform connectionRoot;
        Transform decorationRoot;
        Transform missionRoot;
        Transform validationRoot;
        UrbanDestructionCoordinator destructionCoordinator;
        readonly System.Collections.Generic.List<GeneratedBuildingRecord>
            generatedBuildings =
                new System.Collections.Generic.List<GeneratedBuildingRecord>(128);
        readonly System.Collections.Generic.HashSet<string>
            connectedBuildingPairs =
                new System.Collections.Generic.HashSet<string>();
        readonly System.Collections.Generic.HashSet<string>
            protectedFlightGapPairs =
                new System.Collections.Generic.HashSet<string>();
        int lastSkybridgeCandidateCount;
        int lastSkybridgeDegreeRejectCount;
        int lastSkybridgeCrossingRejectCount;
        int lastCrossRoadBlockSkybridgeCount;
        int runtimeDifficultyTier;

        public AirCombatCitySettings Settings => settings;
        public AirCombatCityPlan Plan => plan;
        public AirCombatCityReport Report => report;
        public bool HasValidPlan => report != null && report.valid;
        public string LastSummary => report?.Summary ?? "尚未生成";
        public int LastSkybridgeCandidateCount => lastSkybridgeCandidateCount;
        public int LastSkybridgeDegreeRejectCount => lastSkybridgeDegreeRejectCount;
        public int LastSkybridgeCrossingRejectCount =>
            lastSkybridgeCrossingRejectCount;
        public int LastCrossRoadBlockSkybridgeCount =>
            lastCrossRoadBlockSkybridgeCount;
        public int LastSkybridgeCount => report?.skybridgeCount ?? 0;
        public int LastTacticalSkybridgeGroupCount =>
            report?.tacticalSkybridgeGroupCount ?? 0;

        /// <summary>
        /// Builds only the playable city art/collision layers for a formal
        /// planet mission. Mission objectives and enemy waves remain owned by
        /// the existing planet combat controller.
        /// </summary>
        public void ConfigureRuntimeMission(
            int seed,
            AirCombatCityMission mission,
            int difficultyTier = 0)
        {
            settings.mission = mission;
            runtimeDifficultyTier = Mathf.Clamp(difficultyTier, 0, 5);
            showSemanticGizmos = false;
            showLabels = false;
            showRuntimePanel = false;
            // A formal mission cannot silently enter an invalid city. Search
            // a small deterministic sequence of neighbouring seeds before
            // committing GameObjects; the same mission always resolves to the
            // same first valid candidate.
            const int RuntimeCandidateCount = 24;
            int acceptedCandidateIndex = RuntimeCandidateCount - 1;
            for (int candidate = 0;
                 candidate < RuntimeCandidateCount;
                 candidate++)
            {
                settings.seed = unchecked(seed + candidate * 7919);
                RebuildPlanOnly();
                if (HasValidPlan)
                {
                    acceptedCandidateIndex = candidate;
                    break;
                }
            }
            // Buildings expose prefab-correct facade bounds only after their
            // visual plan is committed. Bridge candidates are still fully
            // selected and quota-validated before any bridge GameObject is
            // instantiated, but rebuilding all city art for every seed would
            // multiply mission load time by up to 24. Base-city seed retries
            // therefore remain plan-only and the accepted seed is built once.
            Rebuild();
            if (!HasValidPlan || !report.skybridgeNetworkValid)
            {
                for (int candidate = acceptedCandidateIndex + 1;
                     candidate < RuntimeCandidateCount;
                     candidate++)
                {
                    settings.seed = unchecked(seed + candidate * 7919);
                    RebuildPlanOnly();
                    if (!HasValidPlan)
                        continue;
                    Rebuild();
                    if (HasValidPlan && report.skybridgeNetworkValid)
                        break;
                }
            }
            SetGeneratedLayerActive("01_", false);
            SetGeneratedLayerActive("02_", false);
            SetGeneratedLayerActive("05_", false);
            SetGeneratedLayerActive("06_", false);
        }

        void OnEnable()
        {
            RebuildPlanOnly();
            if (transform.Find(GeneratedRootName) == null)
                Rebuild();
        }

        void OnValidate()
        {
            RebuildPlanOnly();
        }

        void Update()
        {
            if (!Application.isPlaying)
                return;
            if (Input.GetKeyDown(KeyCode.N))
                NextSeed();
            if (Input.GetKeyDown(KeyCode.P))
                PreviousSeed();
            if (Input.GetKeyDown(KeyCode.R))
                Rebuild();
            if (Input.GetKeyDown(KeyCode.G))
                showSemanticGizmos = !showSemanticGizmos;
            if (Input.GetKeyDown(KeyCode.M))
                ToggleMission();
        }

        public void ConfigureDemo(
            int seed,
            AirCombatCityPalette targetPalette,
            NewGenUrbanBuildingCatalog targetBuildingCatalog = null,
            GameObject targetRoadJunctionPrefab = null,
            GameObject targetParkTreePrefab = null,
            GameObject targetRooftopMechanicalPrefab = null,
            GameObject[] targetRooftopBillboardPrefabs = null,
            GameObject targetStreetLightPrefab = null,
            GameObject targetParkPlanterPrefab = null,
            DarkCity2UrbanCatalog targetDarkCity2Catalog = null)
        {
            settings.seed = seed;
            settings.mission = AirCombatCityMission.Clearance;
            settings.mapSize = 1664f;
            settings.combatSpeed = 55f;
            settings.turnRadius = 95f;
            settings.weaponRange = 480f;
            settings.wingspan = 18f;
            settings.lowAltitude = 70f;
            settings.mediumAltitude = 135f;
            settings.highAltitude = 220f;
            settings.maximumAltitude = 350f;
            settings.buildingSpacing = 63.6f;
            settings.buildingDensity = 0.91f;
            palette = targetPalette ?? new AirCombatCityPalette();
            buildingCatalog = targetBuildingCatalog;
            darkCity2Catalog = targetDarkCity2Catalog;
            if (darkCity2Catalog != null && darkCity2Catalog.buildings != null)
                buildingCatalog = darkCity2Catalog.buildings;
            roadJunctionPrefab = targetRoadJunctionPrefab;
            parkTreePrefab = targetParkTreePrefab;
            rooftopMechanicalPrefab = targetRooftopMechanicalPrefab;
            rooftopBillboardPrefabs = targetRooftopBillboardPrefabs ??
                Array.Empty<GameObject>();
            streetLightPrefab = targetStreetLightPrefab;
            parkPlanterPrefab = targetParkPlanterPrefab;
            RebuildPlanOnly();
        }

        [ContextMenu("重新生成空战城市")]
        public void Rebuild()
        {
            RebuildPlanOnly();
            ClearGenerated();
            if (plan == null)
                return;

            var generated = new GameObject(GeneratedRootName);
            generated.transform.SetParent(transform, false);
            semanticRoot = CreateRoot(
                generated.transform,
                "01_空战语义层_先定可玩空域");
            routeRoot = CreateRoot(
                generated.transform,
                "02_三层飞行航路_低60_中110_高160");
            roadRoot = CreateRoot(
                generated.transform,
                "03_道路层_服从飞行走廊");
            buildingRoot = CreateRoot(
                generated.transform,
                "04_建筑遮挡层_低中高分层");
            connectionRoot = CreateRoot(
                generated.transform,
                "04A_Skybridges_AuditedSockets_MultiRoute");
            decorationRoot = CreateRoot(
                generated.transform,
                "04B_城市装饰层_楼顶设备_广告牌_路灯_花坛");
            missionRoot = CreateRoot(
                generated.transform,
                "05_任务与敌机入口层");
            validationRoot = CreateRoot(
                generated.transform,
                "06_验证层_转弯半径与高度包线");

            destructionCoordinator = generated.AddComponent<
                UrbanDestructionCoordinator>();
            destructionCoordinator.Configure(destructionSettings);

            BuildSemanticVolumes();
            BuildFlightRoutes();
            BuildRoads();
            BuildBuildings();
            BuildBlockInfill();
            BuildTacticalCloseBuildingPairs();
            BuildSkybridges();
            BuildAerialCableLinks();
            BuildUrbanDetails();
            BuildBackgroundSkyline();
            BuildMissionLayer();
            BuildValidationLayer();

            UrbanCityGlowRuntime cityGlow =
                generated.GetComponent<UrbanCityGlowRuntime>() ??
                generated.AddComponent<UrbanCityGlowRuntime>();
            cityGlow.Configure(1.02f, 0.78f, 2, 2);
        }

        public void NextSeed()
        {
            settings.seed++;
            Rebuild();
        }

        public void PreviousSeed()
        {
            settings.seed--;
            Rebuild();
        }

        public void ToggleMission()
        {
            settings.mission = settings.mission ==
                AirCombatCityMission.Clearance
                ? AirCombatCityMission.FacilityAssault
                : AirCombatCityMission.Clearance;
            Rebuild();
        }

        void RebuildPlanOnly()
        {
            plan = AirCombatCityGenerator.Generate(settings, out report);
        }

        void BuildSemanticVolumes()
        {
            for (int i = 0; i < plan.volumes.Count; i++)
            {
                AirCombatTacticalVolume volume = plan.volumes[i];
                Material material = MaterialForVolume(volume.kind);
                GameObject marker = CreatePrimitive(
                    PrimitiveType.Cube,
                    semanticRoot,
                    "Volume_" + volume.kind + "_" + volume.stableId,
                    false);
                marker.transform.localPosition = volume.center;
                marker.transform.localScale = volume.size;
                AssignMaterial(marker, material);
            }
        }

        void BuildFlightRoutes()
        {
            for (int i = 0; i < plan.routes.Count; i++)
            {
                AirCombatFlightRoute route = plan.routes[i];
                Material material = MaterialForRoute(route.kind, i);
                var routeObject = new GameObject(
                    "FlightRoute_" + route.kind + "_" + route.stableId);
                routeObject.transform.SetParent(routeRoot, false);
                LineRenderer line = routeObject.AddComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.loop = false;
                line.positionCount = route.points.Length;
                line.widthMultiplier = route.kind ==
                    AirCombatRouteKind.EnemyIngress ? 5f : 10f;
                line.numCornerVertices = 6;
                line.numCapVertices = 5;
                line.sharedMaterial = material;
                line.SetPositions(route.points);

                if (route.kind == AirCombatRouteKind.EnemyIngress)
                    continue;
                for (int p = 0; p < route.points.Length; p++)
                {
                    GameObject node = CreatePrimitive(
                        PrimitiveType.Sphere,
                        routeObject.transform,
                        "Node_" + p.ToString("D2"),
                        false);
                    node.transform.localPosition = route.points[p];
                    node.transform.localScale = Vector3.one * 18f;
                    AssignMaterial(node, material);
                }
            }
        }

        void BuildRoads()
        {
            Material cityMaterial = darkCity2Catalog != null &&
                                    darkCity2Catalog.cityPaving != null
                ? darkCity2Catalog.cityPaving
                : palette.cityBlockPaving != null
                    ? palette.cityBlockPaving
                    : palette.road;
            float surfaceHalf = settings.mapSize * 0.66f;
            CreateRoadStrip(
                "CitySurface_DarkCity2_UVTiled_NoOverlap",
                new Vector3(0f, 0f, -surfaceHalf),
                new Vector3(0f, 0f, surfaceHalf),
                settings.mapSize * 1.32f,
                0.02f,
                -0.025f,
                cityMaterial);

            for (int i = 0; i < plan.roads.Count; i++)
            {
                AirCombatRoadStrip road = plan.roads[i];
                Vector3 delta = road.end - road.start;
                float length = delta.magnitude;
                if (length <= 0.01f)
                    continue;
                Vector3 direction = delta / length;
                Vector3 midpoint = (road.start + road.end) * 0.5f;
                System.Collections.Generic.List<Vector2> intervals =
                    ResolveRoadIntervals(road, direction, length);
                for (int segment = 0; segment < intervals.Count; segment++)
                {
                    float from = intervals[segment].x;
                    float to = intervals[segment].y;
                    if (to - from <= 1f)
                        continue;
                    CreateRoadStrip(
                        "Asphalt_" + road.stableId + "_" + segment,
                        midpoint + direction * from,
                        midpoint + direction * to,
                        road.width,
                        0.055f,
                        darkCity2Catalog != null
                            ? darkCity2Catalog.roadSurfaceY
                            : 0.018f,
                        darkCity2Catalog != null &&
                        darkCity2Catalog.asphalt != null
                            ? darkCity2Catalog.asphalt
                            : palette.asphalt != null
                                ? palette.asphalt
                                : palette.road);
                }
                BuildSidewalkAndMarkingSegments(road);
            }

            BuildRoadIntersections();
            BuildRecoveryCourtyardSurfaces();
            BuildTacticalParkSurface();
        }

        void BuildSidewalkAndMarkingSegments(AirCombatRoadStrip road)
        {
            Vector3 delta = road.end - road.start;
            float length = delta.magnitude;
            if (length <= 0.01f)
                return;
            Vector3 direction = delta / length;
            Vector3 side = Vector3.Cross(Vector3.up, direction).normalized;
            Vector3 midpoint = (road.start + road.end) * 0.5f;
            System.Collections.Generic.List<Vector2> intervals =
                ResolveRoadIntervals(road, direction, length);
            const float SidewalkWidth = 6.2f;

            for (int i = 0; i < intervals.Count; i++)
            {
                float from = intervals[i].x;
                float to = intervals[i].y;
                float segmentLength = to - from;
                if (segmentLength <= 2f)
                    continue;
                Vector3 segmentCenter = midpoint +
                    direction * ((from + to) * 0.5f);
                float edgeOffset = road.width * 0.5f +
                                   SidewalkWidth * 0.5f;
                CreateRoadStrip(
                    "Sidewalk_L_" + road.stableId + "_" + i,
                    segmentCenter - direction * segmentLength * 0.5f +
                    side * edgeOffset,
                    segmentCenter + direction * segmentLength * 0.5f +
                    side * edgeOffset,
                    SidewalkWidth,
                    0.12f,
                    darkCity2Catalog != null
                        ? darkCity2Catalog.sidewalkSurfaceY
                        : 0.055f,
                    darkCity2Catalog != null && darkCity2Catalog.sidewalk != null
                        ? darkCity2Catalog.sidewalk
                        : palette.sidewalk != null ? palette.sidewalk : palette.road);
                CreateRoadStrip(
                    "Sidewalk_R_" + road.stableId + "_" + i,
                    segmentCenter - direction * segmentLength * 0.5f -
                    side * edgeOffset,
                    segmentCenter + direction * segmentLength * 0.5f -
                    side * edgeOffset,
                    SidewalkWidth,
                    0.12f,
                    darkCity2Catalog != null
                        ? darkCity2Catalog.sidewalkSurfaceY
                        : 0.055f,
                    darkCity2Catalog != null && darkCity2Catalog.sidewalk != null
                        ? darkCity2Catalog.sidewalk
                        : palette.sidewalk != null ? palette.sidewalk : palette.road);

                int dividerCount = road.laneTiles >= 3 ? 2 : 1;
                for (int divider = 0; divider < dividerCount; divider++)
                {
                    float lateral = dividerCount == 1
                        ? 0f
                        : (divider == 0 ? -road.width / 6f : road.width / 6f);
                    Material marking = road.dangerLane
                        ? palette.dangerLaneMarking
                        : palette.laneMarking;
                    CreateRoadStrip(
                        "LaneMark_" + road.stableId + "_" + i + "_" + divider,
                        segmentCenter - direction * segmentLength * 0.5f +
                        side * lateral,
                        segmentCenter + direction * segmentLength * 0.5f +
                        side * lateral,
                        road.dangerLane ? 1.4f : 0.72f,
                        0.012f,
                        darkCity2Catalog != null
                            ? darkCity2Catalog.markingSurfaceY
                            : 0.052f,
                        darkCity2Catalog != null && darkCity2Catalog.roadLines != null
                            ? darkCity2Catalog.roadLines
                            : marking != null ? marking : palette.road);
                }
            }
        }

        System.Collections.Generic.List<Vector2> ResolveRoadIntervals(
            AirCombatRoadStrip road,
            Vector3 direction,
            float length)
        {
            var exclusions = new System.Collections.Generic.List<Vector2>();
            Vector3 midpoint = (road.start + road.end) * 0.5f;
            for (int i = 0; i < plan.roads.Count; i++)
            {
                AirCombatRoadStrip other = plan.roads[i];
                if (ReferenceEquals(road, other) ||
                    !TryGetRoadIntersection(road, other, out Vector3 point))
                {
                    continue;
                }
                float coordinate = Vector3.Dot(point - midpoint, direction);
                float halfGap = other.width * 0.5f + 7.5f;
                exclusions.Add(new Vector2(
                    Mathf.Max(-length * 0.5f, coordinate - halfGap),
                    Mathf.Min(length * 0.5f, coordinate + halfGap)));
            }
            exclusions.Sort((a, b) => a.x.CompareTo(b.x));

            var intervals = new System.Collections.Generic.List<Vector2>();
            float cursor = -length * 0.5f;
            for (int i = 0; i < exclusions.Count; i++)
            {
                Vector2 cut = exclusions[i];
                if (cut.x > cursor + 1f)
                    intervals.Add(new Vector2(cursor, cut.x));
                cursor = Mathf.Max(cursor, cut.y);
            }
            if (cursor < length * 0.5f - 1f)
                intervals.Add(new Vector2(cursor, length * 0.5f));
            return intervals;
        }

        void BuildRoadIntersections()
        {
            int index = 0;
            for (int a = 0; a < plan.roads.Count; a++)
            for (int b = a + 1; b < plan.roads.Count; b++)
            {
                AirCombatRoadStrip first = plan.roads[a];
                AirCombatRoadStrip second = plan.roads[b];
                if (!TryGetRoadIntersection(first, second, out Vector3 point))
                    continue;
                AirCombatRoadStrip vertical =
                    Mathf.Abs(first.end.z - first.start.z) >=
                    Mathf.Abs(first.end.x - first.start.x)
                        ? first
                        : second;
                AirCombatRoadStrip horizontal = ReferenceEquals(vertical, first)
                    ? second
                    : first;
                GameObject junction;
                if (roadJunctionPrefab != null && darkCity2Catalog == null)
                {
                    junction = Instantiate(roadJunctionPrefab, roadRoot, false);
                    junction.name = "Junction_NewGen_" + index.ToString("D2");
                    junction.transform.localPosition = point + Vector3.up * 0.045f;
                    junction.transform.localScale = new Vector3(
                        vertical.width / 31.8f,
                        1f,
                        horizontal.width / 21.2f);
                    RemoveColliders(junction);
                }
                else
                {
                    junction = CreateRoadStrip(
                        "Junction_Fallback_" + index.ToString("D2"),
                        point - Vector3.forward * horizontal.width * 0.5f,
                        point + Vector3.forward * horizontal.width * 0.5f,
                        vertical.width,
                        0.04f,
                        darkCity2Catalog != null
                            ? darkCity2Catalog.roadSurfaceY
                            : 0.04f,
                        darkCity2Catalog != null &&
                        darkCity2Catalog.asphalt != null
                            ? darkCity2Catalog.asphalt
                            : palette.asphalt != null
                                ? palette.asphalt
                                : palette.road);
                }
                index++;
            }
        }

        void BuildRecoveryCourtyardSurfaces()
        {
            for (int i = 0; i < plan.volumes.Count; i++)
            {
                AirCombatTacticalVolume volume = plan.volumes[i];
                if (volume.kind != AirCombatVolumeKind.RecoveryPocket)
                    continue;
                GameObject courtyard = CreatePrimitive(
                    PrimitiveType.Cube,
                    roadRoot,
                    "RepairCourtyard_隐蔽维修区_" + i,
                    false);
                courtyard.transform.localPosition = new Vector3(
                    volume.center.x,
                    0.035f,
                    volume.center.z);
                courtyard.transform.localScale = new Vector3(92f, 0.04f, 92f);
                AssignMaterial(
                    courtyard,
                    palette.repairCourtyard != null
                        ? palette.repairCourtyard
                        : palette.cityBlockPaving);
            }
        }

        void BuildTacticalParkSurface()
        {
            for (int i = 0; i < plan.volumes.Count; i++)
            {
                AirCombatTacticalVolume volume = plan.volumes[i];
                if (volume.kind != AirCombatVolumeKind.DangerPlaza)
                    continue;
                GameObject park = CreatePrimitive(
                    PrimitiveType.Cube,
                    roadRoot,
                    "TacticalPark_高危开放公园",
                    false);
                park.transform.localPosition = new Vector3(
                    volume.center.x,
                    0.035f,
                    volume.center.z);
                park.transform.localScale = new Vector3(
                    volume.size.x,
                    0.04f,
                    volume.size.z);
                AssignMaterial(
                    park,
                    palette.parkSurface != null
                        ? palette.parkSurface
                        : palette.cityBlockPaving);

                GameObject greeneryPrefab = parkPlanterPrefab != null
                    ? parkPlanterPrefab
                    : parkTreePrefab;
                if (greeneryPrefab == null)
                    continue;
                const int TreeCountPerLongSide = 6;
                for (int side = -1; side <= 1; side += 2)
                for (int tree = 0; tree < TreeCountPerLongSide; tree++)
                {
                    float t = TreeCountPerLongSide == 1
                        ? 0.5f
                        : tree / (float)(TreeCountPerLongSide - 1);
                    Vector3 position = new Vector3(
                        volume.center.x + Mathf.Lerp(
                            -volume.size.x * 0.42f,
                            volume.size.x * 0.42f,
                            t),
                        0.07f,
                        volume.center.z + side * volume.size.z * 0.38f);
                    CreateNormalizedDecoration(
                        greeneryPrefab,
                        "ParkPlanter_NewGen_" + side + "_" + tree,
                        position,
                        ResolvePlanterYaw(side),
                        new Vector3(8f, 12f, 8f));
                }
            }
        }

        static float ResolvePlanterYaw(int side)
        {
            // 两排树池沿公园长边整齐排列，并朝公园内部；这里不使用随机角度。
            return side < 0 ? 0f : 180f;
        }

        GameObject CreateRoadStrip(
            string objectName,
            Vector3 start,
            Vector3 end,
            float width,
            float thickness,
            float y,
            Material material)
        {
            Vector3 delta = end - start;
            float length = delta.magnitude;
            if (darkCity2Catalog != null)
            {
                Vector3 direction = length > 0.01f
                    ? delta / length
                    : Vector3.forward;
                Vector3 side = Vector3.Cross(Vector3.up, direction).normalized;
                Vector3 first = start + Vector3.up * y;
                Vector3 last = end + Vector3.up * y;
                float halfWidth = Mathf.Max(0.05f, width * 0.5f);
                var mesh = new Mesh
                {
                    name = objectName + "_GeneratedUVSurface"
                };
                mesh.vertices = new[]
                {
                    first - side * halfWidth,
                    first + side * halfWidth,
                    last + side * halfWidth,
                    last - side * halfWidth
                };
                float uvMeters = Mathf.Max(
                    2f,
                    darkCity2Catalog.roadTextureMeters);
                mesh.uv = new[]
                {
                    new Vector2(0f, 0f),
                    new Vector2(width / uvMeters, 0f),
                    new Vector2(width / uvMeters, length / uvMeters),
                    new Vector2(0f, length / uvMeters)
                };
                mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();

                var surface = new GameObject(objectName);
                surface.transform.SetParent(roadRoot, false);
                surface.AddComponent<MeshFilter>().sharedMesh = mesh;
                surface.AddComponent<MeshRenderer>().sharedMaterial = material;
                surface.AddComponent<CityPcgGeneratedMeshOwner>().Configure(mesh);
                return surface;
            }
            GameObject strip = CreatePrimitive(
                PrimitiveType.Cube,
                roadRoot,
                objectName,
                false);
            strip.transform.localPosition =
                (start + end) * 0.5f + Vector3.up * y;
            strip.transform.localRotation = Quaternion.LookRotation(
                delta.sqrMagnitude > 0.01f ? delta.normalized : Vector3.forward,
                Vector3.up);
            strip.transform.localScale = new Vector3(
                Mathf.Max(0.1f, width),
                Mathf.Max(0.005f, thickness),
                Mathf.Max(0.1f, length));
            AssignMaterial(strip, material);
            return strip;
        }

        static bool TryGetRoadIntersection(
            AirCombatRoadStrip first,
            AirCombatRoadStrip second,
            out Vector3 point)
        {
            bool firstVertical = Mathf.Abs(first.end.z - first.start.z) >=
                                 Mathf.Abs(first.end.x - first.start.x);
            bool secondVertical = Mathf.Abs(second.end.z - second.start.z) >=
                                  Mathf.Abs(second.end.x - second.start.x);
            point = Vector3.zero;
            if (firstVertical == secondVertical)
                return false;
            AirCombatRoadStrip vertical = firstVertical ? first : second;
            AirCombatRoadStrip horizontal = firstVertical ? second : first;
            float x = vertical.start.x;
            float z = horizontal.start.z;
            float verticalMin = Mathf.Min(vertical.start.z, vertical.end.z) - 0.01f;
            float verticalMax = Mathf.Max(vertical.start.z, vertical.end.z) + 0.01f;
            float horizontalMin = Mathf.Min(horizontal.start.x, horizontal.end.x) - 0.01f;
            float horizontalMax = Mathf.Max(horizontal.start.x, horizontal.end.x) + 0.01f;
            if (z < verticalMin || z > verticalMax ||
                x < horizontalMin || x > horizontalMax)
            {
                return false;
            }
            point = new Vector3(x, 0f, z);
            return true;
        }

        void BuildBuildings()
        {
            generatedBuildings.Clear();
            for (int i = 0; i < plan.buildings.Count; i++)
            {
                AirCombatBuildingLot lot = plan.buildings[i];
                float top = lot.center.y + lot.size.y * 0.5f;
                float verticalSafety = settings.wingspan * 0.45f + 8f;
                bool skylineAnchor =
                    lot.band == AirCombatBuildingBand.High &&
                    top + verticalSafety >= settings.maximumAltitude;
                GameObject prefab = ResolveBuildingPrefab(lot);
                bool usesNormalizedPrefab = prefab != null;
                GameObject building;
                if (prefab != null)
                {
                    building = Instantiate(prefab, buildingRoot, false);
                    NormalizedBuildingModelInfo modelInfo =
                        building.GetComponent<NormalizedBuildingModelInfo>();
                    Vector3 authoredSize = modelInfo != null
                        ? modelInfo.AuthoredSize
                        : Vector3.one;
                    building.transform.localScale = new Vector3(
                        lot.size.x / Mathf.Max(0.1f, authoredSize.x),
                        lot.size.y / Mathf.Max(0.1f, authoredSize.y),
                        lot.size.z / Mathf.Max(0.1f, authoredSize.z));
                    if (!keepBuildingColliders)
                        RemoveColliders(building);
                }
                else
                {
                    building = CreatePrimitive(
                        lot.band == AirCombatBuildingBand.Facility
                            ? PrimitiveType.Cylinder
                            : PrimitiveType.Cube,
                        buildingRoot,
                        "FallbackBuilding",
                        keepBuildingColliders);
                    building.transform.localScale = lot.size;
                    AssignMaterial(building, MaterialForBuilding(lot.band));
                }

                building.name =
                    (skylineAnchor ? "SkylineAnchor_" : "Building_") +
                    "District" + DistrictLabel(ResolveDistrict(lot)) + "_" +
                    lot.archetype + "_Cluster" + lot.clusterId + "_" +
                    lot.stableId + "_FrontFacesRoad";
                // 标准化根节点放在地面，而不是放在几何中心。
                building.transform.localPosition = new Vector3(
                    lot.center.x,
                    usesNormalizedPrefab ? 0f : lot.center.y,
                    lot.center.z);
                building.transform.localRotation = Quaternion.Euler(
                    0f,
                    lot.yaw,
                    0f);

                // 破坏组件只读取 PCG 地块尺寸、朝向和稳定 ID。它不会改写
                // NewGen 网格，也不会给飞船或自然地形增加组件。
                UrbanDestructibleBuilding destructible =
                    building.GetComponent<UrbanDestructibleBuilding>() ??
                    building.AddComponent<UrbanDestructibleBuilding>();
                destructible.Configure(lot, destructionCoordinator);
                generatedBuildings.Add(new GeneratedBuildingRecord
                {
                    lot = lot,
                    instance = building,
                    destructible = destructible
                });
            }
        }

        void BuildBlockInfill()
        {
            if (buildingCatalog == null || buildingCatalog.low == null ||
                buildingCatalog.low.Length == 0)
            {
                return;
            }

            // Roads remain on their authored tile grid, while building
            // parcels use a denser candidate lattice plus stable offsets.
            // Parcel size, not random world position, decides whether a block
            // becomes a compact cluster, a normal plot or a super-block.
            const float Step = 38f;
            float half = settings.mapSize * 0.5f - 58f;
            int gridRadius = Mathf.FloorToInt(half / Step);
            var candidates = new System.Collections.Generic.List<Vector3>();
            for (int xIndex = -gridRadius; xIndex <= gridRadius; xIndex++)
            for (int zIndex = -gridRadius; zIndex <= gridRadius; zIndex++)
            {
                if (xIndex == 0 && zIndex == 0)
                    continue;
                int candidateStable = StableHash(
                    settings.seed + "|parcel-candidate|" +
                    xIndex + "|" + zIndex);
                float offsetX =
                    PositiveStableModulo(candidateStable / 7, 21) - 10f;
                float offsetZ =
                    PositiveStableModulo(candidateStable / 19, 21) - 10f;
                candidates.Add(new Vector3(
                    xIndex * Step + offsetX,
                    0f,
                    zIndex * Step + offsetZ));
            }
            candidates.Sort((left, right) =>
            {
                int leftRing = Mathf.FloorToInt(left.magnitude / 180f);
                int rightRing = Mathf.FloorToInt(right.magnitude / 180f);
                int ring = leftRing.CompareTo(rightRing);
                if (ring != 0)
                    return ring;

                int leftStable = StableHash(
                    settings.seed + "|parcel|" +
                    Mathf.RoundToInt(left.x) + "|" +
                    Mathf.RoundToInt(left.z));
                int rightStable = StableHash(
                    settings.seed + "|parcel|" +
                    Mathf.RoundToInt(right.x) + "|" +
                    Mathf.RoundToInt(right.z));
                int leftRoll = PositiveStableModulo(leftStable, 100);
                int rightRoll = PositiveStableModulo(rightStable, 100);
                int leftClass = leftRoll < 10 ? 0 : leftRoll < 35 ? 1 : 2;
                int rightClass = rightRoll < 10 ? 0 : rightRoll < 35 ? 1 : 2;
                int parcelPriority = rightClass.CompareTo(leftClass);
                if (parcelPriority != 0)
                    return parcelPriority;

                int distance = left.sqrMagnitude.CompareTo(right.sqrMagnitude);
                if (distance != 0)
                    return distance;
                return leftStable.CompareTo(rightStable);
            });

            int target = Mathf.Clamp(plan.buildings.Count * 4 / 5, 120, 155);
            int created = 0;
            for (int index = 0; index < candidates.Count && created < target; index++)
            {
                Vector3 candidate = candidates[index];
                int stable = StableHash(
                    settings.seed + "|parcel|" +
                    Mathf.RoundToInt(candidate.x) + "|" +
                    Mathf.RoundToInt(candidate.z));
                int parcelRoll = PositiveStableModulo(stable, 100);
                // This is an authored distribution, not three equal random
                // buckets: large plots form the city body, standard plots
                // articulate streets, and small plots only close residual gaps.
                int parcelClass = parcelRoll < 10
                    ? 0
                    : parcelRoll < 35
                        ? 1
                        : 2;
                string parcelLabel = parcelClass == 0
                    ? "SmallClusterParcel"
                    : parcelClass == 1
                        ? "StandardParcel"
                        : "LargeCompositeParcel";
                float width = parcelClass == 0
                    ? 22f + PositiveStableModulo(stable / 7, 13)
                    : parcelClass == 1
                        ? 38f + PositiveStableModulo(stable / 11, 17)
                        : 56f + PositiveStableModulo(stable / 17, 25);
                float depth = parcelClass == 0
                    ? 20f + PositiveStableModulo(stable / 23, 15)
                    : parcelClass == 1
                        ? 36f + PositiveStableModulo(stable / 29, 19)
                        : 54f + PositiveStableModulo(stable / 31, 27);
                bool central = new Vector2(candidate.x, candidate.z).magnitude <
                               settings.mapSize * 0.34f;
                float height = parcelClass == 0
                    ? 40f + PositiveStableModulo(stable / 37, 43)
                    : parcelClass == 1
                        ? 60f + PositiveStableModulo(stable / 41, 69)
                        : 72f + PositiveStableModulo(stable / 47, 113);
                if (!central)
                    height -= parcelClass == 2 ? 14f : 8f;
                Vector3 size = new Vector3(width, height, depth);
                if (!TryResolveNearestRoad(
                        candidate,
                        out AirCombatRoadStrip nearestRoad,
                        out Vector3 nearestPoint,
                        out float roadDistance))
                {
                    continue;
                }
                float requiredRoadClearance = nearestRoad.width * 0.5f +
                                              Mathf.Max(width, depth) * 0.5f + 8f;
                if (roadDistance < requiredRoadClearance || roadDistance > 220f)
                    continue;
                if (InsideReservedGroundVolume(candidate, size) ||
                    InfillObstructsPlayerRoute(candidate, size) ||
                    InfillOverlapsBuilding(candidate, size))
                {
                    continue;
                }

                Vector3 toRoad = nearestPoint - candidate;
                toRoad.y = 0f;
                float yaw = Mathf.Atan2(toRoad.x, toRoad.z) * Mathf.Rad2Deg;
                var lot = new AirCombatBuildingLot
                {
                    stableId = "infill-" + parcelLabel + "-" +
                               settings.seed + "-" +
                               created.ToString("D3"),
                    center = new Vector3(candidate.x, height * 0.5f, candidate.z),
                    size = size,
                    yaw = yaw,
                    band = height >= 132f
                        ? AirCombatBuildingBand.High
                        : height >= 72f
                            ? AirCombatBuildingBand.Medium
                            : AirCombatBuildingBand.Low,
                    archetype = height >= 132f
                        ? AirCombatBuildingArchetype.CombatTower
                        : height >= 72f
                            ? AirCombatBuildingArchetype.MidSlab
                            : AirCombatBuildingArchetype.LowBlock,
                    clusterId = -1000 - created,
                    visualVariant = stable + created * 7
                };
                GameObject prefab = ResolveBuildingPrefab(lot);
                if (prefab == null)
                    continue;
                GameObject building = Instantiate(prefab, buildingRoot, false);
                NormalizedBuildingModelInfo modelInfo =
                    building.GetComponent<NormalizedBuildingModelInfo>();
                Vector3 authoredSize = modelInfo != null
                    ? modelInfo.AuthoredSize
                    : Vector3.one;
                building.transform.localScale = new Vector3(
                    size.x / Mathf.Max(0.1f, authoredSize.x),
                    size.y / Mathf.Max(0.1f, authoredSize.y),
                    size.z / Mathf.Max(0.1f, authoredSize.z));
                building.transform.localPosition = new Vector3(
                    candidate.x,
                    0f,
                    candidate.z);
                building.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                building.name = "InfillBuilding_District" +
                    DistrictLabel(ResolveDistrict(lot)) +
                    "_" + parcelLabel +
                    "_RoadFacing_TacticalCover_" + lot.band + "_" +
                    lot.stableId;
                UrbanDestructibleBuilding destructible =
                    building.GetComponent<UrbanDestructibleBuilding>() ??
                    building.AddComponent<UrbanDestructibleBuilding>();
                destructible.Configure(lot, destructionCoordinator);
                generatedBuildings.Add(new GeneratedBuildingRecord
                {
                    lot = lot,
                    instance = building,
                    destructible = destructible
                });
                created++;
            }
        }

        GameObject ResolveBuildingPrefab(AirCombatBuildingLot lot)
        {
            if (lot == null)
                return null;
            if (darkCity2Catalog != null &&
                darkCity2Catalog.districtBuildings != null &&
                darkCity2Catalog.districtBuildings.Length > 0)
            {
                DarkCity2DistrictKind district = ResolveDistrict(lot);
                int stable = StableHash(lot.stableId + "|district-building");
                DarkCity2PlacementRole role = DarkCity2PlacementRole.None;
                if (district == DarkCity2DistrictKind.Industrial)
                {
                    if (lot.band == AirCombatBuildingBand.Facility)
                        role = DarkCity2PlacementRole.IndustrialSilo;
                    else if ((lot.band == AirCombatBuildingBand.Low ||
                              lot.band == AirCombatBuildingBand.Medium) &&
                             PositiveStableModulo(stable, 4) == 0)
                        role = DarkCity2PlacementRole.IndustrialWarehouse;
                }
                else if (district == DarkCity2DistrictKind.Transit &&
                         lot.band == AirCombatBuildingBand.Low &&
                         PositiveStableModulo(stable, 5) == 0)
                {
                    role = DarkCity2PlacementRole.TransitGarage;
                }

                if (role != DarkCity2PlacementRole.None)
                {
                    GameObject districtPrefab = darkCity2Catalog.ResolveByRole(
                        darkCity2Catalog.districtBuildings,
                        role,
                        stable);
                    if (districtPrefab != null)
                        return districtPrefab;
                }
            }

            return buildingCatalog != null
                ? buildingCatalog.Resolve(lot.band, lot.visualVariant)
                : null;
        }

        bool TryResolveNearestRoad(
            Vector3 position,
            out AirCombatRoadStrip nearestRoad,
            out Vector3 nearestPoint,
            out float nearestDistance)
        {
            nearestRoad = null;
            nearestPoint = position;
            nearestDistance = float.PositiveInfinity;
            Vector2 point = new Vector2(position.x, position.z);
            for (int index = 0; index < plan.roads.Count; index++)
            {
                AirCombatRoadStrip road = plan.roads[index];
                Vector2 start = new Vector2(road.start.x, road.start.z);
                Vector2 end = new Vector2(road.end.x, road.end.z);
                Vector2 segment = end - start;
                float denominator = Mathf.Max(0.001f, segment.sqrMagnitude);
                float t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / denominator);
                Vector2 closest = start + segment * t;
                float distance = Vector2.Distance(point, closest);
                if (distance >= nearestDistance)
                    continue;
                nearestDistance = distance;
                nearestRoad = road;
                nearestPoint = new Vector3(closest.x, 0f, closest.y);
            }
            return nearestRoad != null;
        }

        bool InsideReservedGroundVolume(Vector3 position, Vector3 size)
        {
            for (int index = 0; index < plan.volumes.Count; index++)
            {
                AirCombatTacticalVolume volume = plan.volumes[index];
                if (volume.kind != AirCombatVolumeKind.SpawnBasin &&
                    volume.kind != AirCombatVolumeKind.RecoveryPocket &&
                    volume.kind != AirCombatVolumeKind.DangerPlaza)
                {
                    continue;
                }
                if (Mathf.Abs(position.x - volume.center.x) <=
                        volume.size.x * 0.5f + size.x * 0.5f + 12f &&
                    Mathf.Abs(position.z - volume.center.z) <=
                        volume.size.z * 0.5f + size.z * 0.5f + 12f)
                {
                    return true;
                }
            }
            return false;
        }

        bool InfillOverlapsBuilding(Vector3 position, Vector3 size)
        {
            float parcelPadding = Mathf.Clamp(
                Mathf.Min(size.x, size.z) * 0.12f,
                4f,
                12f);
            Bounds candidate = new Bounds(
                new Vector3(position.x, size.y * 0.5f, position.z),
                new Vector3(
                    size.x + parcelPadding,
                    size.y,
                    size.z + parcelPadding));
            for (int index = 0; index < generatedBuildings.Count; index++)
            {
                Bounds other = ResolveLotBounds(generatedBuildings[index].lot);
                if (candidate.Intersects(other))
                    return true;
            }
            return false;
        }

        bool InfillObstructsPlayerRoute(Vector3 position, Vector3 size)
        {
            if (plan == null)
                return false;
            Vector2 center = new Vector2(position.x, position.z);
            float footprintRadius = Mathf.Sqrt(
                size.x * size.x + size.z * size.z) * 0.5f;
            float requiredClearance = footprintRadius +
                                      settings.wingspan * 0.62f;
            float buildingTop = size.y;
            for (int routeIndex = 0;
                 routeIndex < plan.routes.Count;
                 routeIndex++)
            {
                AirCombatFlightRoute route = plan.routes[routeIndex];
                if (route.kind == AirCombatRouteKind.EnemyIngress ||
                    route.points == null || route.points.Length < 2)
                {
                    continue;
                }
                for (int segment = 0;
                     segment < route.points.Length - 1;
                     segment++)
                {
                    Vector3 start3 = route.points[segment];
                    Vector3 end3 = route.points[segment + 1];
                    if (Mathf.Min(start3.y, end3.y) >
                        buildingTop + settings.wingspan * 0.5f)
                    {
                        continue;
                    }
                    Vector2 start = new Vector2(start3.x, start3.z);
                    Vector2 end = new Vector2(end3.x, end3.z);
                    if (DistanceToSegment(center, start, end) < requiredClearance)
                        return true;
                }
            }
            return false;
        }

        void BuildTacticalCloseBuildingPairs()
        {
            protectedFlightGapPairs.Clear();
            var anchors = new System.Collections.Generic.List<
                GeneratedBuildingRecord>(generatedBuildings);
            anchors.Sort((left, right) =>
            {
                float leftRadius = new Vector2(
                    left.lot.center.x,
                    left.lot.center.z).sqrMagnitude;
                float rightRadius = new Vector2(
                    right.lot.center.x,
                    right.lot.center.z).sqrMagnitude;
                int radius = leftRadius.CompareTo(rightRadius);
                if (radius != 0)
                    return radius;
                return StableHash(left.lot.stableId + settings.seed)
                    .CompareTo(StableHash(right.lot.stableId + settings.seed));
            });

            const int TargetPairs = 15;
            int created = 0;
            Vector3[] cardinalDirections =
            {
                Vector3.right,
                Vector3.forward,
                Vector3.left,
                Vector3.back
            };
            for (int anchorIndex = 0;
                 anchorIndex < anchors.Count && created < TargetPairs;
                 anchorIndex++)
            {
                GeneratedBuildingRecord anchor = anchors[anchorIndex];
                Bounds anchorBounds = ResolveActualBuildingBounds(anchor);
                if (anchorBounds.size.y < 76f ||
                    Mathf.Min(anchorBounds.size.x, anchorBounds.size.z) < 28f)
                {
                    continue;
                }
                int stable = StableHash(
                    settings.seed + "|skill-gap|" + anchor.lot.stableId);
                int directionStart = PositiveStableModulo(stable, 4);
                for (int attempt = 0;
                     attempt < 4 && created < TargetPairs;
                     attempt++)
                {
                    Vector3 direction = cardinalDirections[
                        (directionStart + attempt) % cardinalDirections.Length];
                    bool separatedOnX = Mathf.Abs(direction.x) > 0.5f;
                    float parallelSize = Mathf.Clamp(
                        separatedOnX
                            ? anchorBounds.size.z * 0.92f
                            : anchorBounds.size.x * 0.92f,
                        42f,
                        58f);
                    float separatedSize = 34f +
                        PositiveStableModulo(stable / 7 + attempt * 13, 4) * 2f;
                    Vector3 size = separatedOnX
                        ? new Vector3(separatedSize, 0f, parallelSize)
                        : new Vector3(parallelSize, 0f, separatedSize);
                    float heightFactor = 0.82f +
                        PositiveStableModulo(stable / 17 + attempt, 4) * 0.09f;
                    size.y = Mathf.Clamp(
                        anchorBounds.size.y * heightFactor,
                        104f,
                        210f);
                    int gateClass = created % 3;
                    float skillGap = gateClass == 0
                        ? 11f + PositiveStableModulo(stable / 29 + attempt, 6)
                        : gateClass == 1
                            ? settings.wingspan + 4f +
                              PositiveStableModulo(stable / 29 + attempt, 7)
                            : settings.wingspan + 16f +
                              PositiveStableModulo(stable / 29 + attempt, 9);
                    string gateLabel = gateClass == 0
                        ? "CompactHullGate"
                        : gateClass == 1
                            ? "StandardSkillGate"
                            : "HeavyHullGate";
                    Vector3 candidate = anchorBounds.center;
                    candidate.y = 0f;
                    if (direction.x > 0.5f)
                        candidate.x = anchorBounds.max.x + skillGap + size.x * 0.5f;
                    else if (direction.x < -0.5f)
                        candidate.x = anchorBounds.min.x - skillGap - size.x * 0.5f;
                    else if (direction.z > 0.5f)
                        candidate.z = anchorBounds.max.z + skillGap + size.z * 0.5f;
                    else
                        candidate.z = anchorBounds.min.z - skillGap - size.z * 0.5f;

                    float mapHalf = settings.mapSize * 0.5f - 46f;
                    if (Mathf.Abs(candidate.x) > mapHalf ||
                        Mathf.Abs(candidate.z) > mapHalf ||
                        InsideReservedGroundVolume(candidate, size) ||
                        InfillObstructsPlayerRoute(candidate, size) ||
                        InfillOverlapsBuilding(candidate, size))
                    {
                        continue;
                    }
                    if (!TryResolveNearestRoad(
                            candidate,
                            out AirCombatRoadStrip nearestRoad,
                            out Vector3 nearestPoint,
                            out float roadDistance))
                    {
                        continue;
                    }
                    float roadClearance = nearestRoad.width * 0.5f +
                                          Mathf.Min(size.x, size.z) * 0.5f + 3f;
                    if (roadDistance < roadClearance)
                        continue;

                    Vector3 toRoad = nearestPoint - candidate;
                    float yaw = Mathf.Atan2(toRoad.x, toRoad.z) * Mathf.Rad2Deg;
                    var lot = new AirCombatBuildingLot
                    {
                        stableId = "skill-gap-" + settings.seed + "-" +
                                   created.ToString("D2"),
                        center = new Vector3(candidate.x, size.y * 0.5f, candidate.z),
                        size = size,
                        yaw = yaw,
                        band = size.y >= 170f
                            ? AirCombatBuildingBand.High
                            : AirCombatBuildingBand.Medium,
                        archetype = size.y >= 170f
                            ? AirCombatBuildingArchetype.CombatTower
                            : AirCombatBuildingArchetype.MidSlab,
                        clusterId = anchor.lot.clusterId,
                        visualVariant = stable + created * 19
                    };
                    GeneratedBuildingRecord companion =
                        CreateTacticalCompanionBuilding(
                            lot,
                            skillGap,
                            gateLabel);
                    if (companion == null)
                        continue;
                    protectedFlightGapPairs.Add(ConnectionPairKey(
                        anchor,
                        companion));
                    generatedBuildings.Add(companion);
                    created++;
                }
            }
        }

        GeneratedBuildingRecord CreateTacticalCompanionBuilding(
            AirCombatBuildingLot lot,
            float skillGap,
            string gateLabel)
        {
            GameObject prefab = ResolveBuildingPrefab(lot);
            if (prefab == null)
                return null;
            GameObject building = Instantiate(prefab, buildingRoot, false);
            NormalizedBuildingModelInfo modelInfo =
                building.GetComponent<NormalizedBuildingModelInfo>();
            Vector3 authoredSize = modelInfo != null
                ? modelInfo.AuthoredSize
                : Vector3.one;
            building.transform.localScale = new Vector3(
                lot.size.x / Mathf.Max(0.1f, authoredSize.x),
                lot.size.y / Mathf.Max(0.1f, authoredSize.y),
                lot.size.z / Mathf.Max(0.1f, authoredSize.z));
            building.transform.localPosition = new Vector3(
                lot.center.x,
                0f,
                lot.center.z);
            building.transform.localRotation = Quaternion.Euler(0f, lot.yaw, 0f);
            building.name = "TacticalClosePair_District" +
                            DistrictLabel(ResolveDistrict(lot)) +
                            "_" + gateLabel + "_Gap" +
                            skillGap.ToString("0") +
                            "m_AlternateRouteExists_" + lot.stableId;
            UrbanDestructibleBuilding destructible =
                building.GetComponent<UrbanDestructibleBuilding>() ??
                building.AddComponent<UrbanDestructibleBuilding>();
            destructible.Configure(lot, destructionCoordinator);
            return new GeneratedBuildingRecord
            {
                lot = lot,
                instance = building,
                destructible = destructible
            };
        }

        void BuildSkybridges()
        {
            connectedBuildingPairs.Clear();
            if (darkCity2Catalog == null || connectionRoot == null ||
                darkCity2Catalog.straightSkybridges == null ||
                darkCity2Catalog.straightSkybridges.Length == 0)
            {
                RecordSkybridgeReport(0, 0, 0, 0f, 0f, false);
                return;
            }

            var candidates = new System.Collections.Generic.List<BridgeCandidate>();
            for (int firstIndex = 0;
                 firstIndex < generatedBuildings.Count;
                 firstIndex++)
            {
                GeneratedBuildingRecord first = generatedBuildings[firstIndex];
                if (!CanReceiveSkybridge(first.lot))
                    continue;
                Bounds firstBounds = ResolveActualBuildingBounds(first);
                for (int secondIndex = firstIndex + 1;
                     secondIndex < generatedBuildings.Count;
                     secondIndex++)
                {
                    GeneratedBuildingRecord second = generatedBuildings[secondIndex];
                    if (!CanReceiveSkybridge(second.lot))
                        continue;
                    if (protectedFlightGapPairs.Contains(ConnectionPairKey(
                            first,
                            second)))
                    {
                        continue;
                    }
                    Bounds secondBounds = ResolveActualBuildingBounds(second);
                    Vector3 delta = secondBounds.center - firstBounds.center;
                    delta.y = 0f;
                    float centerDistance = delta.magnitude;
                    if (centerDistance < 22f || centerDistance > 240f)
                        continue;
                    Vector3 direction = delta / centerDistance;
                    Vector3 firstSocket = ResolveFacadeSocket(
                        firstBounds,
                        direction);
                    Vector3 secondSocket = ResolveFacadeSocket(
                        secondBounds,
                        -direction);
                    float openSpan = Vector3.Distance(
                        new Vector3(firstSocket.x, 0f, firstSocket.z),
                        new Vector3(secondSocket.x, 0f, secondSocket.z));
                    float finalSpan = openSpan + SkybridgeFacadeEmbed * 2f;
                    GameObject bridgePrefab = ResolveBridgeForSpan(finalSpan);
                    if (bridgePrefab == null)
                        continue;

                    float commonTop = Mathf.Min(
                        firstBounds.max.y,
                        secondBounds.max.y);
                    string pairKey = ConnectionPairKey(first, second);
                    int pairHash = StableHash(pairKey + "|bridge-stack|");
                    System.Collections.Generic.List<float> bridgeHeights =
                        BuildIrregularBridgeHeights(commonTop, pairHash);
                    if (bridgeHeights.Count == 0)
                        continue;

                    Vector3 planarMidpoint = (firstSocket + secondSocket) * 0.5f;
                    float centralBias = new Vector2(
                            planarMidpoint.x,
                            planarMidpoint.z).magnitude <
                                        settings.mapSize * 0.31f
                        ? -32f
                        : 0f;
                    float clusterBias = first.lot.clusterId == second.lot.clusterId
                        ? 8f
                        : -22f;
                    float heightDifference = Mathf.Abs(
                        firstBounds.max.y - secondBounds.max.y);
                    bool crossesRoadBlock = CrossesRoadBlock(
                        firstBounds.center,
                        secondBounds.center);
                    for (int layerIndex = 0;
                         layerIndex < bridgeHeights.Count;
                         layerIndex++)
                    {
                        float bridgeCenterY = bridgeHeights[layerIndex];
                        if (!BridgeSocketFitsFacade(firstBounds, bridgeCenterY) ||
                            !BridgeSocketFitsFacade(secondBounds, bridgeCenterY))
                        {
                            continue;
                        }
                        Vector3 layerFirstSocket = firstSocket;
                        Vector3 layerSecondSocket = secondSocket;
                        layerFirstSocket.y = layerSecondSocket.y = bridgeCenterY;
                        Vector3 midpoint =
                            (layerFirstSocket + layerSecondSocket) * 0.5f;
                        if (IntersectsProtectedVolume(midpoint) ||
                            (settings.mission == AirCombatCityMission.BossEncounter &&
                             IntersectsMissionObjective(midpoint)) ||
                            IntersectsThirdBuilding(
                                first,
                                second,
                                layerFirstSocket,
                                layerSecondSocket,
                                bridgeCenterY))
                        {
                            continue;
                        }

                        int stableJitter = PositiveStableModulo(
                            pairHash / 17 + layerIndex * 31,
                            17);
                        candidates.Add(new BridgeCandidate
                        {
                            first = first,
                            second = second,
                            firstSocket = layerFirstSocket,
                            secondSocket = layerSecondSocket,
                            centerY = bridgeCenterY,
                            openSpan = openSpan,
                            finalSpan = finalSpan,
                            prefab = bridgePrefab,
                            layerIndex = layerIndex,
                            layerCount = bridgeHeights.Count,
                            crossesRoadBlock = crossesRoadBlock,
                            score = openSpan + heightDifference * 0.16f +
                                    clusterBias + centralBias + stableJitter +
                                    layerIndex * 4.5f
                        });
                    }
                }
            }

            candidates.Sort((left, right) => left.score.CompareTo(right.score));
            lastSkybridgeCandidateCount = candidates.Count;
            lastSkybridgeDegreeRejectCount = 0;
            lastSkybridgeCrossingRejectCount = 0;
            lastCrossRoadBlockSkybridgeCount = 0;
            var degree = new System.Collections.Generic.Dictionary<
                GeneratedBuildingRecord, int>();
            var built = new System.Collections.Generic.List<BuiltBridgeSegment>();
            var selected = new System.Collections.Generic.List<BridgeCandidate>();
            var accepted = new System.Collections.Generic.HashSet<BridgeCandidate>();
            var permanentlyRejected =
                new System.Collections.Generic.HashSet<BridgeCandidate>();
            bool bossMission = settings.mission ==
                               AirCombatCityMission.BossEncounter;
            int targetCount = bossMission
                ? BossCitySkybridgeTarget
                : MinimumCitySkybridges;
            int minimumCrossRoadBlockSkybridges =
                Mathf.CeilToInt(targetCount * 0.5f);
            int tacticalTarget = bossMission
                ? ResolveBossTacticalChokeTarget(runtimeDifficultyTier)
                : 0;
            var tacticalGroups = bossMission
                ? BuildTacticalBridgeGroups(candidates)
                : new System.Collections.Generic.List<TacticalBridgeGroup>();
            int acceptedTacticalGroups = 0;
            float minimumClearHeight = float.PositiveInfinity;
            float minimumClearWidth = float.PositiveInfinity;

            // Reserve the guaranteed player-sized slots before filling the
            // decorative network. Quadrant round-robin keeps the opportunity
            // distributed around the combat ring instead of clustering it.
            for (int round = 0;
                 bossMission && round < tacticalGroups.Count &&
                 acceptedTacticalGroups < tacticalTarget;
                 round++)
            {
                for (int quadrant = 0;
                     quadrant < 4 && acceptedTacticalGroups < tacticalTarget;
                     quadrant++)
                {
                    TacticalBridgeGroup group = FindNextTacticalGroup(
                        tacticalGroups,
                        quadrant);
                    if (group == null)
                        continue;
                    group.considered = true;
                    if (accepted.Contains(group.lower) ||
                        accepted.Contains(group.upper) ||
                        !CanAcceptBridge(
                            group.lower,
                            degree,
                            built,
                            6) ||
                        !CanAcceptBridgePair(
                            group.lower,
                            group.upper,
                            degree,
                            built,
                            6))
                    {
                        continue;
                    }
                    group.lower.tacticalGroupId = group.stableId;
                    group.lower.tacticalRole =
                        AirCombatSkybridgeRole.TacticalLower;
                    group.upper.tacticalGroupId = group.stableId;
                    group.upper.tacticalRole =
                        AirCombatSkybridgeRole.TacticalUpper;
                    group.lower.tacticalClearHeight =
                        group.upper.tacticalClearHeight = group.clearHeight;
                    group.lower.tacticalClearWidth =
                        group.upper.tacticalClearWidth = group.clearWidth;
                    AcceptBridge(
                        group.lower,
                        degree,
                        built,
                        selected,
                        accepted);
                    AcceptBridge(
                        group.upper,
                        degree,
                        built,
                        selected,
                        accepted);
                    acceptedTacticalGroups++;
                    minimumClearHeight = Mathf.Min(
                        minimumClearHeight,
                        group.clearHeight);
                    minimumClearWidth = Mathf.Min(
                        minimumClearWidth,
                        group.clearWidth);
                }
            }

            // First reserve half of the physical network for bridges that really
            // cross a road-separated city block.  clusterId describes a visual
            // composition group and is not a street-block boundary, so it cannot
            // satisfy this gameplay requirement by itself.
            for (int pass = 0; pass < 2 && built.Count < targetCount; pass++)
            {
                bool fillingCrossRoadQuota = pass == 0;
                for (int index = 0;
                     index < candidates.Count && built.Count < targetCount;
                     index++)
                {
                    if (fillingCrossRoadQuota &&
                        lastCrossRoadBlockSkybridgeCount >=
                        minimumCrossRoadBlockSkybridges)
                    {
                        break;
                    }
                    BridgeCandidate candidate = candidates[index];
                    if (accepted.Contains(candidate) ||
                        permanentlyRejected.Contains(candidate) ||
                        (fillingCrossRoadQuota && !candidate.crossesRoadBlock))
                    {
                        continue;
                    }
                    int firstDegree = degree.TryGetValue(
                        candidate.first,
                        out int firstValue)
                        ? firstValue
                        : 0;
                    int secondDegree = degree.TryGetValue(
                        candidate.second,
                        out int secondValue)
                        ? secondValue
                        : 0;
                    int crossRoadQuotaAllowance =
                        fillingCrossRoadQuota && candidate.crossesRoadBlock
                            ? (bossMission ? 8 : 4)
                            : 0;
                    int bossDegreeAllowance = bossMission ? 5 : 0;
                    if (firstDegree >= ResolveMaximumBridgeDegree(candidate.first.lot) +
                                       bossDegreeAllowance +
                                       crossRoadQuotaAllowance ||
                        secondDegree >= ResolveMaximumBridgeDegree(candidate.second.lot) +
                                        bossDegreeAllowance +
                                        crossRoadQuotaAllowance)
                    {
                        lastSkybridgeDegreeRejectCount++;
                        permanentlyRejected.Add(candidate);
                        continue;
                    }
                    if (CrossesExistingBridge(candidate, built))
                    {
                        lastSkybridgeCrossingRejectCount++;
                        permanentlyRejected.Add(candidate);
                        continue;
                    }

                    AcceptBridge(
                        candidate,
                        degree,
                        built,
                        selected,
                        accepted);
                }
            }

            bool networkValid = selected.Count >= targetCount &&
                                lastCrossRoadBlockSkybridgeCount >=
                                minimumCrossRoadBlockSkybridges &&
                                (!bossMission ||
                                 acceptedTacticalGroups >= tacticalTarget);
            RecordSkybridgeReport(
                selected.Count,
                lastCrossRoadBlockSkybridgeCount,
                acceptedTacticalGroups,
                float.IsPositiveInfinity(minimumClearHeight)
                    ? 0f
                    : minimumClearHeight,
                float.IsPositiveInfinity(minimumClearWidth)
                    ? 0f
                    : minimumClearWidth,
                networkValid);
            if (!networkValid)
                return;
            for (int index = 0; index < selected.Count; index++)
                CreateSkybridgeAssembly(selected[index], index);
        }

        public static int ResolveBossTacticalChokeTarget(int difficultyTier)
        {
            return Mathf.RoundToInt(Mathf.Lerp(
                14f,
                8f,
                Mathf.Clamp01(difficultyTier / 5f)));
        }

        void RecordSkybridgeReport(
            int bridgeCount,
            int crossRoadCount,
            int tacticalGroupCount,
            float minimumClearHeight,
            float minimumClearWidth,
            bool valid)
        {
            if (report == null)
                return;
            report.skybridgeCount = bridgeCount;
            report.crossRoadSkybridgeCount = crossRoadCount;
            report.tacticalSkybridgeGroupCount = tacticalGroupCount;
            report.minimumTacticalClearHeight = minimumClearHeight;
            report.minimumTacticalClearWidth = minimumClearWidth;
            report.skybridgeNetworkValid = valid;
            unchecked
            {
                report.checksum = report.checksum * 31 + bridgeCount;
                report.checksum = report.checksum * 31 + crossRoadCount;
                report.checksum = report.checksum * 31 + tacticalGroupCount;
                report.checksum = report.checksum * 31 +
                                  Mathf.RoundToInt(minimumClearHeight * 100f);
                report.checksum = report.checksum * 31 +
                                  Mathf.RoundToInt(minimumClearWidth * 100f);
            }
            if (valid)
                return;
            report.valid = false;
            report.failureReason = settings.mission ==
                                   AirCombatCityMission.BossEncounter
                ? "Boss city could not satisfy its bridge/choke quotas."
                : "City could not satisfy its physical skybridge quota.";
        }

        System.Collections.Generic.List<TacticalBridgeGroup>
            BuildTacticalBridgeGroups(
                System.Collections.Generic.List<BridgeCandidate> candidates)
        {
            var byPair = new System.Collections.Generic.Dictionary<
                string,
                System.Collections.Generic.List<BridgeCandidate>>();
            for (int index = 0; index < candidates.Count; index++)
            {
                BridgeCandidate candidate = candidates[index];
                string key = ConnectionPairKey(
                    candidate.first,
                    candidate.second);
                if (!byPair.TryGetValue(
                        key,
                        out System.Collections.Generic.List<BridgeCandidate>
                            layers))
                {
                    layers = new System.Collections.Generic.List<BridgeCandidate>();
                    byPair.Add(key, layers);
                }
                layers.Add(candidate);
            }

            var result = new System.Collections.Generic.List<
                TacticalBridgeGroup>();
            foreach (System.Collections.Generic.KeyValuePair<
                         string,
                         System.Collections.Generic.List<BridgeCandidate>> pair
                     in byPair)
            {
                pair.Value.Sort((left, right) =>
                    left.centerY.CompareTo(right.centerY));
                for (int layer = 1; layer < pair.Value.Count; layer++)
                {
                    BridgeCandidate lower = pair.Value[layer - 1];
                    BridgeCandidate upper = pair.Value[layer];
                    if (lower.openSpan < TacticalChokeClearWidth ||
                        lower.centerY > settings.mediumAltitude + 20f ||
                        upper.centerY > settings.highAltitude + 20f)
                    {
                        continue;
                    }
                    float clearHeight = upper.centerY - lower.centerY -
                        (ResolveBridgeVisualHeight(lower.prefab) +
                         ResolveBridgeVisualHeight(upper.prefab)) * 0.5f;
                    float clearWidth = Mathf.Min(
                        lower.openSpan,
                        upper.openSpan);
                    if (clearHeight + 0.001f < TacticalChokeClearHeight ||
                        clearWidth + 0.001f < TacticalChokeClearWidth)
                    {
                        continue;
                    }
                    Vector3 midpoint = (lower.firstSocket +
                                        lower.secondSocket) * 0.5f;
                    int quadrant = (midpoint.x >= 0f ? 1 : 0) +
                                   (midpoint.z >= 0f ? 2 : 0);
                    string id = "boss-choke-" + pair.Key + "-" +
                                Mathf.RoundToInt(lower.centerY);
                    result.Add(new TacticalBridgeGroup
                    {
                        stableId = id,
                        lower = lower,
                        upper = upper,
                        clearHeight = clearHeight,
                        clearWidth = clearWidth,
                        quadrant = quadrant,
                        score = midpoint.magnitude +
                                PositiveStableModulo(StableHash(id), 37)
                    });
                }
            }
            result.Sort((left, right) => left.score.CompareTo(right.score));
            return result;
        }

        static TacticalBridgeGroup FindNextTacticalGroup(
            System.Collections.Generic.List<TacticalBridgeGroup> groups,
            int quadrant)
        {
            for (int index = 0; index < groups.Count; index++)
            {
                TacticalBridgeGroup group = groups[index];
                if (!group.considered && group.quadrant == quadrant)
                    return group;
            }
            return null;
        }

        static float ResolveBridgeVisualHeight(GameObject prefab)
        {
            DarkCity2AssetDescriptor descriptor = prefab != null
                ? prefab.GetComponent<DarkCity2AssetDescriptor>()
                : null;
            float height = descriptor != null
                ? Mathf.Max(0.1f, descriptor.AuthoredSize.y)
                : 3f;
            if (prefab == null)
                return height;
            Collider[] prefabColliders =
                prefab.GetComponentsInChildren<Collider>(true);
            bool initialized = false;
            Bounds rootLocalBounds = new Bounds();
            for (int index = 0; index < prefabColliders.Length; index++)
            {
                Collider collider = prefabColliders[index];
                if (collider == null || !collider.enabled ||
                    !TryResolveColliderLocalBounds(
                        collider,
                        out Bounds colliderBounds))
                {
                    continue;
                }
                EncapsulateBoundsInRootSpace(
                    prefab.transform,
                    collider.transform,
                    colliderBounds,
                    ref rootLocalBounds,
                    ref initialized);
            }
            return initialized
                ? Mathf.Max(height, rootLocalBounds.size.y)
                : height;
        }

        static bool TryResolveColliderLocalBounds(
            Collider collider,
            out Bounds bounds)
        {
            if (collider is BoxCollider box)
            {
                bounds = new Bounds(box.center, box.size);
                return true;
            }
            if (collider is SphereCollider sphere)
            {
                bounds = new Bounds(
                    sphere.center,
                    Vector3.one * sphere.radius * 2f);
                return true;
            }
            if (collider is CapsuleCollider capsule)
            {
                Vector3 size = Vector3.one * capsule.radius * 2f;
                size[capsule.direction] = capsule.height;
                bounds = new Bounds(capsule.center, size);
                return true;
            }
            if (collider is MeshCollider mesh && mesh.sharedMesh != null)
            {
                bounds = mesh.sharedMesh.bounds;
                return true;
            }
            bounds = default;
            return false;
        }

        static void EncapsulateBoundsInRootSpace(
            Transform root,
            Transform source,
            Bounds sourceBounds,
            ref Bounds result,
            ref bool initialized)
        {
            Vector3 minimum = sourceBounds.min;
            Vector3 maximum = sourceBounds.max;
            for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
            for (int z = 0; z < 2; z++)
            {
                Vector3 point = root.InverseTransformPoint(
                    source.TransformPoint(new Vector3(
                        x == 0 ? minimum.x : maximum.x,
                        y == 0 ? minimum.y : maximum.y,
                        z == 0 ? minimum.z : maximum.z)));
                if (!initialized)
                {
                    result = new Bounds(point, Vector3.zero);
                    initialized = true;
                }
                else
                {
                    result.Encapsulate(point);
                }
            }
        }

        bool CanAcceptBridge(
            BridgeCandidate candidate,
            System.Collections.Generic.Dictionary<GeneratedBuildingRecord, int>
                degree,
            System.Collections.Generic.List<BuiltBridgeSegment> built,
            int degreeAllowance)
        {
            int firstDegree = degree.TryGetValue(
                candidate.first,
                out int firstValue) ? firstValue : 0;
            int secondDegree = degree.TryGetValue(
                candidate.second,
                out int secondValue) ? secondValue : 0;
            return firstDegree < ResolveMaximumBridgeDegree(
                       candidate.first.lot) + degreeAllowance &&
                   secondDegree < ResolveMaximumBridgeDegree(
                       candidate.second.lot) + degreeAllowance &&
                   !CrossesExistingBridge(candidate, built);
        }

        bool CanAcceptBridgePair(
            BridgeCandidate lower,
            BridgeCandidate upper,
            System.Collections.Generic.Dictionary<GeneratedBuildingRecord, int>
                degree,
            System.Collections.Generic.List<BuiltBridgeSegment> built,
            int degreeAllowance)
        {
            int firstDegree = degree.TryGetValue(
                upper.first,
                out int firstValue) ? firstValue : 0;
            int secondDegree = degree.TryGetValue(
                upper.second,
                out int secondValue) ? secondValue : 0;
            if (firstDegree + 1 >= ResolveMaximumBridgeDegree(
                    upper.first.lot) + degreeAllowance ||
                secondDegree + 1 >= ResolveMaximumBridgeDegree(
                    upper.second.lot) + degreeAllowance)
            {
                return false;
            }
            var withLower = new System.Collections.Generic.List<
                BuiltBridgeSegment>(built)
            {
                new BuiltBridgeSegment
                {
                    start = lower.firstSocket,
                    end = lower.secondSocket,
                    centerY = lower.centerY
                }
            };
            return !CrossesExistingBridge(upper, withLower);
        }

        void AcceptBridge(
            BridgeCandidate candidate,
            System.Collections.Generic.Dictionary<GeneratedBuildingRecord, int>
                degree,
            System.Collections.Generic.List<BuiltBridgeSegment> built,
            System.Collections.Generic.List<BridgeCandidate> selected,
            System.Collections.Generic.HashSet<BridgeCandidate> accepted)
        {
            if (!accepted.Add(candidate))
                return;
            int firstDegree = degree.TryGetValue(
                candidate.first,
                out int firstValue) ? firstValue : 0;
            int secondDegree = degree.TryGetValue(
                candidate.second,
                out int secondValue) ? secondValue : 0;
            connectedBuildingPairs.Add(ConnectionPairKey(
                candidate.first,
                candidate.second));
            degree[candidate.first] = firstDegree + 1;
            degree[candidate.second] = secondDegree + 1;
            built.Add(new BuiltBridgeSegment
            {
                start = candidate.firstSocket,
                end = candidate.secondSocket,
                centerY = candidate.centerY
            });
            selected.Add(candidate);
            if (candidate.crossesRoadBlock)
                lastCrossRoadBlockSkybridgeCount++;
        }

        bool IntersectsMissionObjective(Vector3 midpoint)
        {
            Vector2 point = new Vector2(midpoint.x, midpoint.z);
            Vector2 objective = new Vector2(
                plan.objective.x,
                plan.objective.z);
            return Vector2.Distance(point, objective) < 85f;
        }

        bool CrossesRoadBlock(Vector3 firstCenter, Vector3 secondCenter)
        {
            float streetPitch = Mathf.Max(1f, settings.buildingSpacing * 3f);
            return ResolveRoadBlockIndex(firstCenter.x, streetPitch) !=
                   ResolveRoadBlockIndex(secondCenter.x, streetPitch) ||
                   ResolveRoadBlockIndex(firstCenter.z, streetPitch) !=
                   ResolveRoadBlockIndex(secondCenter.z, streetPitch);
        }

        static int ResolveRoadBlockIndex(float coordinate, float streetPitch)
        {
            // Roads are centred at integer multiples of the pitch. Buildings
            // have already been kept off those strips, so the interval between
            // two consecutive axes is the actual city block.
            return Mathf.FloorToInt(coordinate / streetPitch);
        }

        void BuildAerialCableLinks()
        {
            if (darkCity2Catalog == null || connectionRoot == null ||
                darkCity2Catalog.cableRed == null ||
                darkCity2Catalog.cableDark == null ||
                darkCity2Catalog.cableBlue == null)
            {
                return;
            }

            var candidates = new System.Collections.Generic.List<CableCandidate>();
            for (int firstIndex = 0;
                 firstIndex < generatedBuildings.Count;
                 firstIndex++)
            {
                GeneratedBuildingRecord first = generatedBuildings[firstIndex];
                if (!CanReceiveCable(first.lot))
                    continue;
                Bounds firstBounds = ResolveActualBuildingBounds(first);
                for (int secondIndex = firstIndex + 1;
                     secondIndex < generatedBuildings.Count;
                     secondIndex++)
                {
                    GeneratedBuildingRecord second = generatedBuildings[secondIndex];
                    if (!CanReceiveCable(second.lot) ||
                        connectedBuildingPairs.Contains(ConnectionPairKey(first, second)))
                    {
                        continue;
                    }

                    Bounds secondBounds = ResolveActualBuildingBounds(second);
                    Vector3 delta = secondBounds.center - firstBounds.center;
                    delta.y = 0f;
                    float centerDistance = delta.magnitude;
                    if (centerDistance < 76f || centerDistance > 310f)
                        continue;
                    Vector3 direction = delta / centerDistance;
                    Vector3 firstSocket = ResolveFacadeSocket(firstBounds, direction);
                    Vector3 secondSocket = ResolveFacadeSocket(secondBounds, -direction);
                    float openSpan = Vector2.Distance(
                        new Vector2(firstSocket.x, firstSocket.z),
                        new Vector2(secondSocket.x, secondSocket.z));
                    if (openSpan < 54f || openSpan > 248f)
                        continue;

                    float commonTop = Mathf.Min(firstBounds.max.y, secondBounds.max.y);
                    float centerY = Mathf.Clamp(
                        commonTop * 0.58f,
                        46f,
                        settings.mediumAltitude + 26f);
                    if (centerY > commonTop - 11f)
                        centerY = commonTop - 11f;
                    if (centerY < 40f)
                        continue;

                    // Each end is deliberately embedded in the real rendered
                    // facade. This hides the line cap and makes the cable read
                    // as building infrastructure, not as a floating spline.
                    firstSocket += -direction * 2.5f;
                    secondSocket += direction * 2.5f;
                    firstSocket.y = secondSocket.y = centerY;
                    Vector3 midpoint = (firstSocket + secondSocket) * 0.5f;
                    if (IntersectsProtectedVolume(midpoint))
                        continue;

                    float sag = Mathf.Clamp(openSpan * 0.075f, 6f, 18f);
                    if (CableIntersectsThirdBuilding(
                            first,
                            second,
                            firstSocket,
                            secondSocket,
                            sag))
                    {
                        continue;
                    }

                    bool crossCluster = first.lot.clusterId != second.lot.clusterId;
                    float centralBias = new Vector2(midpoint.x, midpoint.z).magnitude <
                                        settings.mapSize * 0.34f
                        ? -30f
                        : 0f;
                    int stableJitter = PositiveStableModulo(
                        StableHash(first.lot.stableId + "|cable|" +
                                   second.lot.stableId),
                        19);
                    candidates.Add(new CableCandidate
                    {
                        first = first,
                        second = second,
                        start = firstSocket,
                        end = secondSocket,
                        sag = sag,
                        score = openSpan + centralBias +
                                (crossCluster ? -26f : 12f) + stableJitter
                    });
                }
            }

            candidates.Sort((left, right) => left.score.CompareTo(right.score));
            var degree = new System.Collections.Generic.Dictionary<
                GeneratedBuildingRecord, int>();
            var built = new System.Collections.Generic.List<BuiltCableSegment>();
            int targetCount = Mathf.Clamp(generatedBuildings.Count / 6, 14, 26);
            for (int index = 0;
                 index < candidates.Count && built.Count < targetCount;
                 index++)
            {
                CableCandidate candidate = candidates[index];
                int firstDegree = degree.TryGetValue(candidate.first, out int firstValue)
                    ? firstValue
                    : 0;
                int secondDegree = degree.TryGetValue(candidate.second, out int secondValue)
                    ? secondValue
                    : 0;
                if (firstDegree >= ResolveMaximumCableDegree(candidate.first.lot) ||
                    secondDegree >= ResolveMaximumCableDegree(candidate.second.lot) ||
                    CrossesExistingCable(candidate, built))
                {
                    continue;
                }

                CreateAerialCableAssembly(candidate, built.Count);
                connectedBuildingPairs.Add(ConnectionPairKey(
                    candidate.first,
                    candidate.second));
                degree[candidate.first] = firstDegree + 1;
                degree[candidate.second] = secondDegree + 1;
                built.Add(new BuiltCableSegment
                {
                    start = candidate.start,
                    end = candidate.end
                });
            }
        }

        void CreateAerialCableAssembly(CableCandidate candidate, int stableIndex)
        {
            var assembly = new GameObject(
                "AerialCableLink_" + stableIndex.ToString("D2") + "_" +
                candidate.first.lot.stableId + "_To_" +
                candidate.second.lot.stableId +
                "_FacadeEmbedded_NoCollider");
            assembly.transform.SetParent(connectionRoot, false);

            CreateCableTube(
                assembly.transform,
                "Cable_Red_Top",
                candidate.start,
                candidate.end,
                candidate.sag * 0.82f,
                2.7f,
                0.48f,
                darkCity2Catalog.cableRed);
            CreateCableTube(
                assembly.transform,
                "Cable_Dark_Center",
                candidate.start,
                candidate.end,
                candidate.sag,
                0f,
                0.62f,
                darkCity2Catalog.cableDark);
            CreateCableTube(
                assembly.transform,
                "Cable_Blue_Bottom",
                candidate.start,
                candidate.end,
                candidate.sag * 1.14f,
                -2.7f,
                0.48f,
                darkCity2Catalog.cableBlue);
        }

        static void CreateCableTube(
            Transform parent,
            string objectName,
            Vector3 start,
            Vector3 end,
            float sag,
            float verticalOffset,
            float width,
            Material material)
        {
            const int Segments = 16;
            const int RadialSegments = 8;
            var cableObject = new GameObject(objectName);
            cableObject.transform.SetParent(parent, false);
            var points = new Vector3[Segments + 1];
            for (int segment = 0; segment <= Segments; segment++)
            {
                float t = segment / (float)Segments;
                float catenary = 4f * t * (1f - t);
                Vector3 position = Vector3.Lerp(start, end, t);
                position.y += verticalOffset - sag * catenary;
                points[segment] = position;
            }

            float radius = width * 0.5f;
            var vertices = new Vector3[points.Length * RadialSegments];
            var normals = new Vector3[vertices.Length];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[Segments * RadialSegments * 6];
            for (int pointIndex = 0; pointIndex < points.Length; pointIndex++)
            {
                Vector3 tangent = pointIndex == 0
                    ? points[1] - points[0]
                    : pointIndex == points.Length - 1
                        ? points[pointIndex] - points[pointIndex - 1]
                        : points[pointIndex + 1] - points[pointIndex - 1];
                tangent.Normalize();
                Vector3 side = Vector3.Cross(Vector3.up, tangent).normalized;
                if (side.sqrMagnitude < 0.001f)
                    side = Vector3.right;
                Vector3 localUp = Vector3.Cross(tangent, side).normalized;
                for (int radial = 0; radial < RadialSegments; radial++)
                {
                    float angle = radial * Mathf.PI * 2f / RadialSegments;
                    Vector3 normal = side * Mathf.Cos(angle) +
                                     localUp * Mathf.Sin(angle);
                    int vertex = pointIndex * RadialSegments + radial;
                    vertices[vertex] = points[pointIndex] + normal * radius;
                    normals[vertex] = normal;
                    uv[vertex] = new Vector2(
                        radial / (float)RadialSegments,
                        pointIndex / (float)Segments);
                }
            }
            int triangle = 0;
            for (int segment = 0; segment < Segments; segment++)
            for (int radial = 0; radial < RadialSegments; radial++)
            {
                int nextRadial = (radial + 1) % RadialSegments;
                int a = segment * RadialSegments + radial;
                int b = segment * RadialSegments + nextRadial;
                int c = (segment + 1) * RadialSegments + radial;
                int d = (segment + 1) * RadialSegments + nextRadial;
                triangles[triangle++] = a;
                triangles[triangle++] = c;
                triangles[triangle++] = b;
                triangles[triangle++] = b;
                triangles[triangle++] = c;
                triangles[triangle++] = d;
            }

            var mesh = new Mesh
            {
                name = objectName + "_DynamicTubeMesh",
                vertices = vertices,
                normals = normals,
                uv = uv,
                triangles = triangles
            };
            mesh.RecalculateBounds();
            cableObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            cableObject.AddComponent<MeshRenderer>().sharedMaterial = material;
            cableObject.AddComponent<AerialCableCurve>().Configure(points, mesh);
        }

        bool CableIntersectsThirdBuilding(
            GeneratedBuildingRecord first,
            GeneratedBuildingRecord second,
            Vector3 start,
            Vector3 end,
            float sag)
        {
            const int Segments = 12;
            Vector3 previous = start;
            for (int segment = 1; segment <= Segments; segment++)
            {
                float t = segment / (float)Segments;
                Vector3 current = Vector3.Lerp(start, end, t);
                current.y -= sag * 4f * t * (1f - t);
                for (int index = 0; index < generatedBuildings.Count; index++)
                {
                    GeneratedBuildingRecord other = generatedBuildings[index];
                    if (ReferenceEquals(other, first) ||
                        ReferenceEquals(other, second))
                    {
                        continue;
                    }
                    Bounds bounds = ResolveActualBuildingBounds(other);
                    float segmentMinY = Mathf.Min(previous.y, current.y);
                    float segmentMaxY = Mathf.Max(previous.y, current.y);
                    if (segmentMaxY < bounds.min.y - 3f ||
                        segmentMinY > bounds.max.y + 3f)
                    {
                        continue;
                    }
                    Rect rectangle = new Rect(
                        bounds.min.x - 3f,
                        bounds.min.z - 3f,
                        bounds.size.x + 6f,
                        bounds.size.z + 6f);
                    if (SegmentIntersectsRect(
                            new Vector2(previous.x, previous.z),
                            new Vector2(current.x, current.z),
                            rectangle))
                    {
                        return true;
                    }
                }
                previous = current;
            }
            return false;
        }

        static bool CrossesExistingCable(
            CableCandidate candidate,
            System.Collections.Generic.List<BuiltCableSegment> existing)
        {
            Vector2 a = new Vector2(candidate.start.x, candidate.start.z);
            Vector2 b = new Vector2(candidate.end.x, candidate.end.z);
            for (int index = 0; index < existing.Count; index++)
            {
                BuiltCableSegment other = existing[index];
                Vector2 c = new Vector2(other.start.x, other.start.z);
                Vector2 d = new Vector2(other.end.x, other.end.z);
                if (SegmentsIntersect(a, b, c, d))
                    return true;
            }
            return false;
        }

        static bool CanReceiveCable(AirCombatBuildingLot lot)
        {
            return lot != null && lot.size.y >= 58f &&
                   lot.size.x >= 20f && lot.size.z >= 20f;
        }

        static int ResolveMaximumCableDegree(AirCombatBuildingLot lot)
        {
            return lot.band == AirCombatBuildingBand.Facility ||
                   lot.archetype == AirCombatBuildingArchetype.Landmark
                ? 4
                : 3;
        }

        static string ConnectionPairKey(
            GeneratedBuildingRecord first,
            GeneratedBuildingRecord second)
        {
            string a = first != null && first.lot != null
                ? first.lot.stableId
                : string.Empty;
            string b = second != null && second.lot != null
                ? second.lot.stableId
                : string.Empty;
            return string.CompareOrdinal(a, b) <= 0
                ? a + "|" + b
                : b + "|" + a;
        }

        GameObject ResolveBridgeForSpan(float targetSpan)
        {
            GameObject best = null;
            float bestError = float.PositiveInfinity;
            for (int index = 0;
                 index < darkCity2Catalog.straightSkybridges.Length;
                 index++)
            {
                GameObject candidate = darkCity2Catalog.straightSkybridges[index];
                if (candidate == null)
                    continue;
                DarkCity2AssetDescriptor descriptor =
                    candidate.GetComponent<DarkCity2AssetDescriptor>();
                if (descriptor == null || descriptor.AuthoredSize.z <= 0.1f)
                    continue;
                float minimum = descriptor.AuthoredSize.z *
                                descriptor.AllowedStretch.x;
                float maximum = descriptor.AuthoredSize.z *
                                descriptor.AllowedStretch.y;
                if (targetSpan < minimum || targetSpan > maximum)
                    continue;
                float scale = targetSpan / descriptor.AuthoredSize.z;
                float error = Mathf.Abs(1f - scale);
                if (error < bestError)
                {
                    bestError = error;
                    best = candidate;
                }
            }
            return best;
        }

        void CreateSkybridgeAssembly(BridgeCandidate candidate, int stableIndex)
        {
            Vector3 direction = candidate.secondSocket - candidate.firstSocket;
            direction.y = 0f;
            direction.Normalize();
            Vector3 midpoint = (candidate.firstSocket + candidate.secondSocket) * 0.5f;
            var assembly = new GameObject(
                "Skybridge_" + stableIndex.ToString("D2") +
                "_" + candidate.first.lot.stableId +
                "_To_" + candidate.second.lot.stableId +
                "_Layer" + (candidate.layerIndex + 1) +
                "of" + candidate.layerCount +
                "_Height" + Mathf.RoundToInt(candidate.centerY) +
                "_6mFacadeEmbed_" +
                (candidate.crossesRoadBlock
                    ? "CrossRoadBlock"
                    : "WithinRoadBlock"));
            assembly.transform.SetParent(connectionRoot, false);
            assembly.transform.localPosition = new Vector3(midpoint.x, 0f, midpoint.z);
            assembly.transform.localRotation = Quaternion.LookRotation(
                direction,
                Vector3.up);

            GameObject bridge = Instantiate(candidate.prefab, assembly.transform, false);
            bridge.name = "BridgeSpan_+Z_SocketsOutward";
            DarkCity2AssetDescriptor descriptor =
                bridge.GetComponent<DarkCity2AssetDescriptor>();
            Vector3 authoredSize = descriptor != null
                ? descriptor.AuthoredSize
                : new Vector3(8f, 3f, 56f);
            bridge.transform.localPosition = new Vector3(
                0f,
                candidate.centerY - authoredSize.y * 0.5f,
                0f);
            bridge.transform.localScale = new Vector3(
                1f,
                1f,
                candidate.finalSpan / Mathf.Max(0.1f, authoredSize.z));

            AddBridgeHead(
                assembly.transform,
                -candidate.openSpan * 0.5f + 0.8f,
                candidate.centerY,
                stableIndex * 2,
                false);
            AddBridgeHead(
                assembly.transform,
                candidate.openSpan * 0.5f - 0.8f,
                candidate.centerY,
                stableIndex * 2 + 1,
                true);

            UrbanDestructibleBridge destructible =
                assembly.AddComponent<UrbanDestructibleBridge>();
            if (candidate.tacticalRole != AirCombatSkybridgeRole.Ordinary)
            {
                AirCombatTacticalSkybridge tactical =
                    assembly.AddComponent<AirCombatTacticalSkybridge>();
                tactical.Configure(
                    candidate.tacticalGroupId,
                    candidate.tacticalRole,
                    candidate.tacticalClearHeight,
                    candidate.tacticalClearWidth,
                    runtimeDifficultyTier);
                AddTacticalBridgeMarker(
                    assembly.transform,
                    candidate,
                    authoredSize);
            }
            destructible.Configure(destructionCoordinator, 92f, 18f);
        }

        void AddTacticalBridgeMarker(
            Transform parent,
            BridgeCandidate candidate,
            Vector3 authoredSize)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                GameObject strip = CreatePrimitive(
                    PrimitiveType.Cube,
                    parent,
                    "TacticalChokeEdge_" +
                    (side < 0 ? "Left" : "Right"),
                    false);
                strip.transform.localPosition = new Vector3(
                    side * Mathf.Max(1f, authoredSize.x * 0.43f),
                    candidate.centerY + authoredSize.y * 0.55f,
                    0f);
                strip.transform.localScale = new Vector3(
                    0.16f,
                    0.12f,
                    Mathf.Max(4f, candidate.openSpan * 0.72f));
                RemoveColliders(strip);
                AssignMaterial(strip, palette.longRangeRoute);
            }
        }

        void AddBridgeHead(
            Transform parent,
            float localZ,
            float centerY,
            int stableVariant,
            bool positiveEnd)
        {
            GameObject source = darkCity2Catalog.ResolveBridgeHead(stableVariant);
            if (source == null)
                return;
            GameObject head = Instantiate(source, parent, false);
            head.name = positiveEnd
                ? "BridgeHead_B_EmbeddedInFacade"
                : "BridgeHead_A_EmbeddedInFacade";
            DarkCity2AssetDescriptor descriptor =
                head.GetComponent<DarkCity2AssetDescriptor>();
            Vector3 size = descriptor != null
                ? descriptor.AuthoredSize
                : new Vector3(4f, 4f, 4f);
            head.transform.localPosition = new Vector3(
                0f,
                centerY - size.y * 0.5f,
                localZ);
            head.transform.localRotation = Quaternion.Euler(
                0f,
                positiveEnd ? 180f : 0f,
                0f);
            RemoveColliders(head);
        }

        bool IntersectsProtectedVolume(Vector3 midpoint)
        {
            for (int index = 0; index < plan.volumes.Count; index++)
            {
                AirCombatTacticalVolume volume = plan.volumes[index];
                if (volume.kind != AirCombatVolumeKind.RecoveryPocket &&
                    volume.kind != AirCombatVolumeKind.SpawnBasin)
                {
                    continue;
                }
                Vector3 delta = midpoint - volume.center;
                if (Mathf.Abs(delta.x) <= volume.size.x * 0.55f &&
                    Mathf.Abs(delta.y) <= volume.size.y * 0.55f &&
                    Mathf.Abs(delta.z) <= volume.size.z * 0.55f)
                {
                    return true;
                }
            }
            return false;
        }

        bool IntersectsThirdBuilding(
            GeneratedBuildingRecord first,
            GeneratedBuildingRecord second,
            Vector3 start,
            Vector3 end,
            float height)
        {
            Vector2 lineStart = new Vector2(start.x, start.z);
            Vector2 lineEnd = new Vector2(end.x, end.z);
            for (int index = 0; index < generatedBuildings.Count; index++)
            {
                GeneratedBuildingRecord other = generatedBuildings[index];
                if (ReferenceEquals(other, first) || ReferenceEquals(other, second))
                    continue;
                Bounds bounds = ResolveLotBounds(other.lot);
                if (height < bounds.min.y - 4f || height > bounds.max.y + 4f)
                    continue;
                Rect rectangle = new Rect(
                    bounds.min.x - 5f,
                    bounds.min.z - 5f,
                    bounds.size.x + 10f,
                    bounds.size.z + 10f);
                if (SegmentIntersectsRect(lineStart, lineEnd, rectangle))
                    return true;
            }
            return false;
        }

        static bool CrossesExistingBridge(
            BridgeCandidate candidate,
            System.Collections.Generic.List<BuiltBridgeSegment> existing)
        {
            Vector2 a = new Vector2(candidate.firstSocket.x, candidate.firstSocket.z);
            Vector2 b = new Vector2(candidate.secondSocket.x, candidate.secondSocket.z);
            for (int index = 0; index < existing.Count; index++)
            {
                BuiltBridgeSegment other = existing[index];
                // Bridges may cross as a deliberate two-level route choice.
                // Ten metres keeps the meshes and an 18 m ship envelope
                // visually distinguishable without banning the second tier.
                if (Mathf.Abs(candidate.centerY - other.centerY) >= 10f)
                    continue;
                Vector2 c = new Vector2(other.start.x, other.start.z);
                Vector2 d = new Vector2(other.end.x, other.end.z);
                if (SegmentsIntersect(a, b, c, d))
                    return true;
            }
            return false;
        }

        static bool CanReceiveSkybridge(AirCombatBuildingLot lot)
        {
            return lot != null && lot.size.y >= 54f &&
                   lot.size.x >= 20f && lot.size.z >= 20f;
        }

        static bool BridgeSocketFitsFacade(Bounds buildingBounds, float centerY)
        {
            // The bridge deck must enter real facade volume at both ends.
            // Using the shorter building's top alone is insufficient when a
            // source model has a raised origin or an unusually shallow mesh.
            return centerY >= buildingBounds.min.y + 6f &&
                   centerY <= buildingBounds.max.y - 8f;
        }

        static int ResolveMaximumBridgeDegree(AirCombatBuildingLot lot)
        {
            return lot.band == AirCombatBuildingBand.Facility ||
                   lot.archetype == AirCombatBuildingArchetype.Landmark
                ? 10
                : 7;
        }

        static System.Collections.Generic.List<float>
            BuildIrregularBridgeHeights(float commonTop, int pairHash)
        {
            const float MinimumHeight = 38f;
            const float FacadeTopMargin = 8f;
            const float MinimumVerticalGap = 18f;
            const int MaximumLayersPerPair = 4;

            float maximumHeight = commonTop - FacadeTopMargin;
            float usableSpan = maximumHeight - MinimumHeight;
            var heights = new System.Collections.Generic.List<float>(
                MaximumLayersPerPair);
            if (usableSpan < 0f)
                return heights;

            int capacity = Mathf.Clamp(
                1 + Mathf.FloorToInt(usableSpan / MinimumVerticalGap),
                1,
                MaximumLayersPerPair);
            int roll = PositiveStableModulo(pairHash / 13, 100);
            int layerCount = 1;
            if (capacity >= 4 && commonTop >= 178f && roll < 38)
                layerCount = 4;
            else if (capacity >= 3 && commonTop >= 130f && roll < 68)
                layerCount = 3;
            else if (capacity >= 2 && commonTop >= 96f && roll < 88)
                layerCount = 2;

            if (layerCount == 1)
            {
                float fraction = 0.24f +
                    PositiveStableModulo(pairHash / 29, 46) * 0.01f;
                heights.Add(Mathf.Lerp(
                    MinimumHeight,
                    maximumHeight,
                    fraction));
                return heights;
            }

            // First reserve a ship-readable minimum clearance between decks,
            // then distribute remaining facade height with unequal stable
            // weights. The result looks authored instead of a fixed floor grid.
            float minimumRequired =
                (layerCount - 1) * MinimumVerticalGap;
            float extra = Mathf.Max(0f, usableSpan - minimumRequired);
            float bottomShare = 0.08f +
                PositiveStableModulo(pairHash / 37, 15) * 0.01f;
            float gapPoolShare = 0.52f +
                PositiveStableModulo(pairHash / 43, 17) * 0.01f;
            float current = MinimumHeight + extra * bottomShare;
            heights.Add(current);

            var weights = new float[layerCount - 1];
            float weightTotal = 0f;
            for (int gapIndex = 0; gapIndex < weights.Length; gapIndex++)
            {
                weights[gapIndex] = 0.65f +
                    PositiveStableModulo(
                        pairHash / (53 + gapIndex * 6) + gapIndex * 41,
                        71) * 0.01f;
                weightTotal += weights[gapIndex];
            }
            if (weights.Length >= 2)
            {
                // Guarantee an authored cadence instead of allowing two
                // independently hashed gaps to accidentally look identical.
                int compressedGap = PositiveStableModulo(
                    pairHash / 71,
                    weights.Length);
                int expandedGap = (compressedGap + 1) % weights.Length;
                weights[compressedGap] = 0.55f;
                weights[expandedGap] = 1.45f;
                weightTotal = 0f;
                for (int gapIndex = 0;
                     gapIndex < weights.Length;
                     gapIndex++)
                {
                    weightTotal += weights[gapIndex];
                }
            }
            float gapExtraPool = extra * gapPoolShare;
            for (int gapIndex = 0; gapIndex < weights.Length; gapIndex++)
            {
                current += MinimumVerticalGap +
                    gapExtraPool * weights[gapIndex] /
                    Mathf.Max(0.001f, weightTotal);
                heights.Add(Mathf.Min(current, maximumHeight));
            }
            return heights;
        }

        static Bounds ResolveLotBounds(AirCombatBuildingLot lot)
        {
            float normalizedYaw = Mathf.Repeat(lot.yaw, 180f);
            bool swap = normalizedYaw > 45f && normalizedYaw < 135f;
            Vector3 size = swap
                ? new Vector3(lot.size.z, lot.size.y, lot.size.x)
                : lot.size;
            return new Bounds(lot.center, size);
        }

        Bounds ResolveActualBuildingBounds(GeneratedBuildingRecord record)
        {
            if (record != null && record.actualBoundsReady)
                return record.actualBounds;
            if (record == null)
                return new Bounds();
            if (record.destructible == null)
                return ResolveLotBounds(record.lot);

            Bounds world = record.destructible.DestructionBounds;
            Vector3 min = world.min;
            Vector3 max = world.max;
            Vector3 first = transform.InverseTransformPoint(new Vector3(
                min.x,
                min.y,
                min.z));
            Bounds local = new Bounds(first, Vector3.zero);
            for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
            for (int z = 0; z < 2; z++)
            {
                local.Encapsulate(transform.InverseTransformPoint(new Vector3(
                    x == 0 ? min.x : max.x,
                    y == 0 ? min.y : max.y,
                    z == 0 ? min.z : max.z)));
            }
            record.actualBounds = local;
            record.actualBoundsReady = true;
            return local;
        }

        static Vector3 ResolveFacadeSocket(Bounds bounds, Vector3 direction)
        {
            float xDistance = Mathf.Abs(direction.x) > 0.0001f
                ? bounds.extents.x / Mathf.Abs(direction.x)
                : float.PositiveInfinity;
            float zDistance = Mathf.Abs(direction.z) > 0.0001f
                ? bounds.extents.z / Mathf.Abs(direction.z)
                : float.PositiveInfinity;
            float distance = Mathf.Min(xDistance, zDistance);
            Vector3 point = bounds.center + direction * distance;
            point.y = bounds.center.y;
            return point;
        }

        static bool SegmentIntersectsRect(Vector2 start, Vector2 end, Rect rect)
        {
            if (rect.Contains(start) || rect.Contains(end))
                return true;
            Vector2 bottomLeft = new Vector2(rect.xMin, rect.yMin);
            Vector2 bottomRight = new Vector2(rect.xMax, rect.yMin);
            Vector2 topRight = new Vector2(rect.xMax, rect.yMax);
            Vector2 topLeft = new Vector2(rect.xMin, rect.yMax);
            return SegmentsIntersect(start, end, bottomLeft, bottomRight) ||
                   SegmentsIntersect(start, end, bottomRight, topRight) ||
                   SegmentsIntersect(start, end, topRight, topLeft) ||
                   SegmentsIntersect(start, end, topLeft, bottomLeft);
        }

        static bool SegmentsIntersect(
            Vector2 a,
            Vector2 b,
            Vector2 c,
            Vector2 d)
        {
            float d1 = Cross2D(b - a, c - a);
            float d2 = Cross2D(b - a, d - a);
            float d3 = Cross2D(d - c, a - c);
            float d4 = Cross2D(d - c, b - c);
            return d1 * d2 < -0.001f && d3 * d4 < -0.001f;
        }

        static float Cross2D(Vector2 first, Vector2 second)
        {
            return first.x * second.y - first.y * second.x;
        }

        static int StableHash(string value)
        {
            unchecked
            {
                int hash = 17;
                for (int index = 0; index < value.Length; index++)
                    hash = hash * 31 + value[index];
                return hash;
            }
        }

        DarkCity2DistrictKind ResolveDistrict(AirCombatBuildingLot lot)
        {
            return ResolveDistrict(
                lot != null ? lot.center : Vector3.zero,
                lot);
        }

        DarkCity2DistrictKind ResolveDistrict(
            Vector3 position,
            AirCombatBuildingLot lot = null)
        {
            if (PointInsideTacticalVolume(
                    position,
                    AirCombatVolumeKind.RecoveryPocket,
                    0.22f))
            {
                return DarkCity2DistrictKind.Service;
            }
            if (lot != null && lot.band == AirCombatBuildingBand.Facility)
                return DarkCity2DistrictKind.Industrial;

            // A 320 m zoning cell keeps neighbouring blocks visually related.
            // Randomness chooses the district palette, never individual world
            // positions, so the result reads as a city rather than scatter.
            const float DistrictCellSize = 320f;
            int cellX = Mathf.FloorToInt(
                (position.x + settings.mapSize * 0.5f) / DistrictCellSize);
            int cellZ = Mathf.FloorToInt(
                (position.z + settings.mapSize * 0.5f) / DistrictCellSize);
            int stable = StableHash(
                settings.seed + "|district|" + cellX + "|" + cellZ);

            if (TryResolveNearestRoad(
                    position,
                    out AirCombatRoadStrip road,
                    out Vector3 _,
                    out float roadDistance) &&
                road.kind == AirCombatRouteKind.Main &&
                roadDistance <= road.width * 0.5f + 72f &&
                PositiveStableModulo(stable / 11, 100) < 54)
            {
                return DarkCity2DistrictKind.Transit;
            }

            int bucket = PositiveStableModulo(stable, 100);
            float radius = new Vector2(position.x, position.z).magnitude;
            if (radius < settings.mapSize * 0.30f && bucket < 42)
                return DarkCity2DistrictKind.Commercial;
            if (bucket < 31)
                return DarkCity2DistrictKind.Industrial;
            if (bucket < 56)
                return DarkCity2DistrictKind.Commercial;
            if (bucket < 74)
                return DarkCity2DistrictKind.Transit;
            return DarkCity2DistrictKind.Mixed;
        }

        bool PointInsideTacticalVolume(
            Vector3 position,
            AirCombatVolumeKind kind,
            float expansion)
        {
            if (plan == null)
                return false;
            for (int index = 0; index < plan.volumes.Count; index++)
            {
                AirCombatTacticalVolume volume = plan.volumes[index];
                if (volume.kind != kind)
                    continue;
                Vector3 delta = position - volume.center;
                if (Mathf.Abs(delta.x) <= volume.size.x * (0.5f + expansion) &&
                    Mathf.Abs(delta.z) <= volume.size.z * (0.5f + expansion))
                {
                    return true;
                }
            }
            return false;
        }

        static string DistrictLabel(DarkCity2DistrictKind district)
        {
            switch (district)
            {
                case DarkCity2DistrictKind.Commercial:
                    return "Commercial";
                case DarkCity2DistrictKind.Industrial:
                    return "Industrial";
                case DarkCity2DistrictKind.Transit:
                    return "Transit";
                case DarkCity2DistrictKind.Service:
                    return "Service";
                default:
                    return "Mixed";
            }
        }

        void BuildUrbanDetails()
        {
            BuildRooftopDetails();
            BuildFacadeDetails();
            BuildStreetLights();
            BuildStreetUtilities();
        }

        void BuildRooftopDetails()
        {
            for (int i = 0; i < plan.buildings.Count; i++)
            {
                AirCombatBuildingLot lot = plan.buildings[i];
                if (lot.size.x < 24f || lot.size.z < 22f)
                    continue;

                float roof = lot.center.y + lot.size.y * 0.5f;
                Quaternion rotation = Quaternion.Euler(0f, lot.yaw, 0f);
                Vector3 forward = rotation * Vector3.forward;
                Vector3 right = rotation * Vector3.right;
                DarkCity2DistrictKind district = ResolveDistrict(lot);
                int stable = StableHash(lot.stableId + "|roof");
                int chance = district == DarkCity2DistrictKind.Industrial
                    ? 84
                    : district == DarkCity2DistrictKind.Service
                        ? 92
                        : district == DarkCity2DistrictKind.Commercial
                            ? 68
                            : district == DarkCity2DistrictKind.Transit
                                ? 56
                                : 44;
                if (PositiveStableModulo(stable, 100) >= chance)
                    continue;

                int equipmentCount =
                    (district == DarkCity2DistrictKind.Industrial ||
                     district == DarkCity2DistrictKind.Service) &&
                    lot.size.x >= 44f && lot.size.z >= 38f
                        ? 2
                        : 1;
                for (int slot = 0; slot < equipmentCount; slot++)
                {
                    DarkCity2PlacementRole role = ResolveRooftopRole(
                        district,
                        stable,
                        slot);
                    GameObject source = darkCity2Catalog != null
                        ? darkCity2Catalog.ResolveByRole(
                            darkCity2Catalog.rooftopDecorations,
                            role,
                            stable + slot * 37)
                        : rooftopMechanicalPrefab;
                    if (source == null)
                        continue;

                    float rearOffset = Mathf.Max(3f, lot.size.z * 0.20f);
                    float sideOffset = equipmentCount == 2
                        ? Mathf.Min(lot.size.x * 0.20f, 12f) *
                          (slot == 0 ? -1f : 1f)
                        : 0f;
                    Vector3 position = new Vector3(
                        lot.center.x,
                        roof + 0.06f,
                        lot.center.z) - forward * rearOffset +
                        right * sideOffset;
                    Vector3 maximumSize = RooftopMaximumSize(role, lot);
                    CreateNormalizedDecoration(
                        source,
                        "RoofEquipment_" + DistrictLabel(district) + "_" +
                        role + "_Slot" + slot + "_" + lot.stableId,
                        position,
                        lot.yaw,
                        maximumSize);
                }
            }
        }

        static DarkCity2PlacementRole ResolveRooftopRole(
            DarkCity2DistrictKind district,
            int stable,
            int slot)
        {
            int variant = PositiveStableModulo(stable + slot * 7, 3);
            switch (district)
            {
                case DarkCity2DistrictKind.Industrial:
                    return variant == 0
                        ? DarkCity2PlacementRole.RoofGenerator
                        : variant == 1
                            ? DarkCity2PlacementRole.RoofPipeFrame
                            : DarkCity2PlacementRole.RoofMechanical;
                case DarkCity2DistrictKind.Service:
                    return variant == 0
                        ? DarkCity2PlacementRole.RoofGenerator
                        : DarkCity2PlacementRole.RoofMechanical;
                case DarkCity2DistrictKind.Commercial:
                    return variant == 0
                        ? DarkCity2PlacementRole.RoofAntenna
                        : variant == 1
                            ? DarkCity2PlacementRole.RoofEnergy
                            : DarkCity2PlacementRole.RoofMechanical;
                case DarkCity2DistrictKind.Transit:
                    return variant == 0
                        ? DarkCity2PlacementRole.RoofEnergy
                        : DarkCity2PlacementRole.RoofAntenna;
                default:
                    return variant == 0
                        ? DarkCity2PlacementRole.RoofAntenna
                        : DarkCity2PlacementRole.RoofMechanical;
            }
        }

        static Vector3 RooftopMaximumSize(
            DarkCity2PlacementRole role,
            AirCombatBuildingLot lot)
        {
            float width = Mathf.Clamp(lot.size.x * 0.30f, 7f, 16f);
            float depth = Mathf.Clamp(lot.size.z * 0.26f, 6f, 14f);
            if (role == DarkCity2PlacementRole.RoofAntenna)
                return new Vector3(Mathf.Min(width, 9f), 20f, Mathf.Min(depth, 9f));
            if (role == DarkCity2PlacementRole.RoofEnergy)
                return new Vector3(width, 4.5f, depth);
            if (role == DarkCity2PlacementRole.RoofPipeFrame)
                return new Vector3(width, 9f, depth);
            return new Vector3(width, 7.5f, depth);
        }

        void BuildFacadeDetails()
        {
            if (darkCity2Catalog == null ||
                darkCity2Catalog.facadeDecorations == null ||
                darkCity2Catalog.facadeDecorations.Length == 0)
            {
                return;
            }

            for (int index = 0; index < plan.buildings.Count; index++)
            {
                AirCombatBuildingLot lot = plan.buildings[index];
                if (lot.size.y < 24f || lot.size.x < 18f)
                    continue;

                DarkCity2DistrictKind district = ResolveDistrict(lot);
                int stable = StableHash(lot.stableId + "|facade");
                int chance = district == DarkCity2DistrictKind.Commercial
                    ? 84
                    : district == DarkCity2DistrictKind.Industrial
                        ? 76
                        : district == DarkCity2DistrictKind.Service
                            ? 92
                            : district == DarkCity2DistrictKind.Transit
                                ? 70
                                : 62;
                if (PositiveStableModulo(stable, 100) >= chance)
                    continue;

                DarkCity2PlacementRole role = ResolveFacadeRole(
                    district,
                    lot,
                    stable);
                if (!TryCreateFacadeDetail(lot, district, role, stable, 0))
                    continue;

                bool addSecondary = lot.size.y >= 105f &&
                    PositiveStableModulo(stable / 17, 4) == 0;
                if (!addSecondary)
                    continue;
                DarkCity2PlacementRole secondary =
                    district == DarkCity2DistrictKind.Commercial
                        ? DarkCity2PlacementRole.FacadeNeon
                        : district == DarkCity2DistrictKind.Industrial
                            ? DarkCity2PlacementRole.FacadeCable
                            : DarkCity2PlacementRole.FacadeMechanical;
                if (secondary != role)
                    TryCreateFacadeDetail(lot, district, secondary, stable + 41, 1);
            }
        }

        static DarkCity2PlacementRole ResolveFacadeRole(
            DarkCity2DistrictKind district,
            AirCombatBuildingLot lot,
            int stable)
        {
            int variant = PositiveStableModulo(stable, 4);
            switch (district)
            {
                case DarkCity2DistrictKind.Commercial:
                    if (lot.band == AirCombatBuildingBand.Low)
                        return DarkCity2PlacementRole.FacadeShop;
                    return lot.band == AirCombatBuildingBand.High ||
                           lot.archetype == AirCombatBuildingArchetype.Landmark ||
                           variant < 2
                        ? DarkCity2PlacementRole.FacadeBillboard
                        : DarkCity2PlacementRole.FacadeNeon;
                case DarkCity2DistrictKind.Industrial:
                    return variant == 0
                        ? DarkCity2PlacementRole.FacadePipe
                        : variant == 1
                            ? DarkCity2PlacementRole.FacadeCable
                            : DarkCity2PlacementRole.FacadeMechanical;
                case DarkCity2DistrictKind.Transit:
                    return variant == 0
                        ? DarkCity2PlacementRole.FacadeBillboard
                        : variant == 1
                            ? DarkCity2PlacementRole.FacadeCable
                            : DarkCity2PlacementRole.FacadeMechanical;
                case DarkCity2DistrictKind.Service:
                    return variant < 2
                        ? DarkCity2PlacementRole.FacadeMechanical
                        : DarkCity2PlacementRole.FacadePipe;
                default:
                    if (lot.size.y < 46f)
                        return DarkCity2PlacementRole.FacadeBalcony;
                    return variant == 0
                        ? DarkCity2PlacementRole.FacadeFireEscape
                        : variant == 1
                            ? DarkCity2PlacementRole.FacadeBalcony
                            : DarkCity2PlacementRole.FacadeCable;
            }
        }

        bool TryCreateFacadeDetail(
            AirCombatBuildingLot lot,
            DarkCity2DistrictKind district,
            DarkCity2PlacementRole role,
            int stable,
            int slot)
        {
            GameObject source = darkCity2Catalog.ResolveByRole(
                darkCity2Catalog.facadeDecorations,
                role,
                stable);
            if (source == null)
                return false;

            Quaternion rotation = Quaternion.Euler(0f, lot.yaw, 0f);
            if (!TryResolveNearestRoad(
                    lot.center,
                    out AirCombatRoadStrip _,
                    out Vector3 nearestRoadPoint,
                    out float roadDistance) ||
                roadDistance > 150f)
            {
                return false;
            }

            Vector3 localToRoad = Quaternion.Inverse(rotation) *
                                  (nearestRoadPoint - lot.center);
            Vector3 localOutward;
            float faceDepth;
            float faceWidth;
            if (Mathf.Abs(localToRoad.x) > Mathf.Abs(localToRoad.z))
            {
                localOutward = localToRoad.x >= 0f ? Vector3.right : Vector3.left;
                faceDepth = lot.size.x;
                faceWidth = lot.size.z;
            }
            else
            {
                localOutward = localToRoad.z >= 0f ? Vector3.forward : Vector3.back;
                faceDepth = lot.size.z;
                faceWidth = lot.size.x;
            }
            Vector3 outward = rotation * localOutward;
            Vector3 tangent = Vector3.Cross(Vector3.up, outward).normalized;
            float yaw = Mathf.Atan2(outward.x, outward.z) * Mathf.Rad2Deg;

            float width;
            float height;
            float bottom;
            switch (role)
            {
                case DarkCity2PlacementRole.FacadeFireEscape:
                    width = Mathf.Clamp(faceWidth * 0.34f, 9f, 18f);
                    height = Mathf.Clamp(lot.size.y * 0.56f, 24f, 68f);
                    bottom = 5f;
                    break;
                case DarkCity2PlacementRole.FacadeBalcony:
                    width = Mathf.Clamp(faceWidth * 0.62f, 12f, 30f);
                    height = Mathf.Clamp(lot.size.y * 0.22f, 8f, 24f);
                    bottom = Mathf.Clamp(lot.size.y * 0.34f, 10f, lot.size.y - height - 7f);
                    break;
                case DarkCity2PlacementRole.FacadePipe:
                    width = Mathf.Clamp(faceWidth * 0.28f, 8f, 16f);
                    height = Mathf.Clamp(lot.size.y * 0.62f, 24f, 74f);
                    bottom = 3f;
                    break;
                case DarkCity2PlacementRole.FacadeCable:
                    width = Mathf.Clamp(faceWidth * 0.68f, 14f, 34f);
                    height = 9f;
                    bottom = Mathf.Clamp(lot.size.y * (slot == 0 ? 0.54f : 0.24f),
                        10f, lot.size.y - height - 5f);
                    break;
                case DarkCity2PlacementRole.FacadeMechanical:
                    width = Mathf.Clamp(faceWidth * 0.36f, 9f, 19f);
                    height = Mathf.Clamp(lot.size.y * 0.25f, 9f, 24f);
                    bottom = Mathf.Clamp(lot.size.y * 0.28f, 8f, lot.size.y - height - 5f);
                    break;
                case DarkCity2PlacementRole.FacadeShop:
                    width = Mathf.Clamp(faceWidth * 0.72f, 15f, 34f);
                    height = Mathf.Clamp(lot.size.y * 0.34f, 9f, 14f);
                    bottom = 0.12f;
                    break;
                case DarkCity2PlacementRole.FacadeNeon:
                    width = Mathf.Clamp(faceWidth * 0.54f, 12f, 30f);
                    height = Mathf.Clamp(lot.size.y * 0.13f, 8f, 17f);
                    bottom = Mathf.Clamp(lot.size.y * (slot == 0 ? 0.30f : 0.18f),
                        9f, lot.size.y - height - 6f);
                    break;
                default:
                    width = Mathf.Clamp(faceWidth * 0.64f, 15f, 38f);
                    height = Mathf.Clamp(lot.size.y * 0.18f, 11f, 27f);
                    bottom = Mathf.Clamp(lot.size.y * (slot == 0 ? 0.44f : 0.20f),
                        14f, lot.size.y - height - 8f);
                    break;
            }

            float sideOffset = slot == 0
                ? 0f
                : Mathf.Min(faceWidth * 0.20f, 9f) *
                  (PositiveStableModulo(stable, 2) == 0 ? -1f : 1f);
            Vector3 position = new Vector3(lot.center.x, bottom, lot.center.z) +
                               outward * (faceDepth * 0.5f + 0.34f) +
                               tangent * sideOffset;
            string prefix = role == DarkCity2PlacementRole.FacadeBillboard
                ? "FacadeBillboard"
                : role == DarkCity2PlacementRole.FacadeNeon
                    ? "FacadeNeon"
                    : "FacadeAttachment";
            return CreateNormalizedDecoration(
                source,
                prefix + "_" + DistrictLabel(district) + "_" + role +
                "_Slot" + slot + "_" + lot.stableId,
                position,
                yaw,
                new Vector3(width, height, 3.2f)) != null;
        }

        bool ShouldPlaceRooftopBillboard(AirCombatBuildingLot lot)
        {
            if (lot.archetype == AirCombatBuildingArchetype.Landmark ||
                lot.band == AirCombatBuildingBand.Facility)
            {
                return true;
            }
            if (lot.band != AirCombatBuildingBand.High || lot.size.x < 38f)
                return false;

            Vector2 point = new Vector2(lot.center.x, lot.center.z);
            for (int i = 0; i < plan.roads.Count; i++)
            {
                AirCombatRoadStrip road = plan.roads[i];
                if (road.laneTiles < 2)
                    continue;
                Vector2 start = new Vector2(road.start.x, road.start.z);
                Vector2 end = new Vector2(road.end.x, road.end.z);
                if (DistanceToSegment(point, start, end) <=
                    road.width * 0.5f + 58f)
                {
                    return true;
                }
            }
            return false;
        }

        static float DistanceToSegment(
            Vector2 point,
            Vector2 start,
            Vector2 end)
        {
            Vector2 segment = end - start;
            float denominator = Mathf.Max(0.0001f, segment.sqrMagnitude);
            float t = Mathf.Clamp01(
                Vector2.Dot(point - start, segment) / denominator);
            return Vector2.Distance(point, start + segment * t);
        }

        void BuildStreetLights()
        {
            if (streetLightPrefab == null &&
                (darkCity2Catalog == null ||
                 darkCity2Catalog.streetDecorations == null ||
                 darkCity2Catalog.streetDecorations.Length == 0))
                return;
            const float Spacing = 155f;
            const float SidewalkInset = 3.15f;
            int stable = 0;
            for (int r = 0; r < plan.roads.Count; r++)
            {
                AirCombatRoadStrip road = plan.roads[r];
                Vector3 delta = road.end - road.start;
                float length = delta.magnitude;
                if (length < Spacing)
                    continue;
                Vector3 direction = delta / length;
                Vector3 side = Vector3.Cross(Vector3.up, direction).normalized;
                int sampleCount = Mathf.Max(1, Mathf.FloorToInt(length / Spacing));
                for (int sample = 0; sample < sampleCount; sample++)
                {
                    float t = (sample + 1f) / (sampleCount + 1f);
                    float sign = ((sample + r) & 1) == 0 ? 1f : -1f;
                    Vector3 position = Vector3.Lerp(road.start, road.end, t) +
                                       side * sign *
                                       (road.width * 0.5f + SidewalkInset);
                    position.y = 0.13f;
                    if (IsNearRoadIntersection(position, 46f))
                        continue;
                    float yaw = Mathf.Atan2(direction.x, direction.z) *
                                Mathf.Rad2Deg;
                    GameObject source = streetLightPrefab;
                    if (darkCity2Catalog != null)
                    {
                        source = darkCity2Catalog.ResolveByRole(
                            darkCity2Catalog.streetDecorations,
                            DarkCity2PlacementRole.StreetLight,
                            stable + r * 17);
                    }
                    if (source == null)
                        continue;
                    CreateNormalizedDecoration(
                        source,
                        "StreetLight_CurbAligned_" + stable++.ToString("D3"),
                        position,
                        yaw,
                        new Vector3(4.8f, 15f, 4.8f));
                }
            }
        }

        void BuildStreetUtilities()
        {
            if (darkCity2Catalog == null ||
                darkCity2Catalog.streetDecorations == null)
            {
                return;
            }

            GameObject barrierSource = darkCity2Catalog.ResolveByRole(
                darkCity2Catalog.streetDecorations,
                DarkCity2PlacementRole.StreetBarrier,
                settings.seed);
            int stable = 0;
            for (int roadIndex = 0; roadIndex < plan.roads.Count; roadIndex++)
            {
                AirCombatRoadStrip road = plan.roads[roadIndex];
                Vector3 delta = road.end - road.start;
                float length = delta.magnitude;
                if (length < 180f)
                    continue;
                Vector3 direction = delta / length;
                Vector3 side = Vector3.Cross(Vector3.up, direction).normalized;
                float yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;

                int utilityCount = length >= 360f ? 2 : 1;
                for (int utilityIndex = 0;
                     utilityIndex < utilityCount;
                     utilityIndex++)
                {
                    float t = utilityCount == 1
                        ? 0.40f
                        : utilityIndex == 0 ? 0.30f : 0.70f;
                    float sideSign = ((roadIndex + utilityIndex) & 1) == 0
                        ? 1f
                        : -1f;
                    Vector3 position = Vector3.Lerp(road.start, road.end, t) +
                                       side * sideSign *
                                       (road.width * 0.5f + 9.2f);
                    position.y = darkCity2Catalog.sidewalkSurfaceY + 0.02f;
                    if (IsNearRoadIntersection(position, 54f) ||
                        PointInsideGeneratedBuilding(position, 4f))
                        continue;

                    DarkCity2DistrictKind district = ResolveDistrict(position);
                    int variant = StableHash(
                        settings.seed + "|street|" + roadIndex + "|" + utilityIndex);
                    DarkCity2PlacementRole role = ResolveStreetRole(
                        district,
                        road,
                        variant);
                    GameObject source = darkCity2Catalog.ResolveByRole(
                        darkCity2Catalog.streetDecorations,
                        role,
                        variant);
                    if (source == null)
                        continue;
                    CreateNormalizedDecoration(
                        source,
                        "StreetUtility_" + DistrictLabel(district) + "_" +
                        role + "_BackOfSidewalk_" + stable++.ToString("D3"),
                        position,
                        yaw,
                        StreetMaximumSize(role));
                }

                if (!road.dangerLane || barrierSource == null)
                    continue;
                for (int end = 0; end < 2; end++)
                {
                    float t = end == 0 ? 0.14f : 0.86f;
                    Vector3 position = Vector3.Lerp(road.start, road.end, t) +
                                       side * (road.width * 0.5f + 3.4f);
                    position.y = darkCity2Catalog.sidewalkSurfaceY + 0.02f;
                    if (IsNearRoadIntersection(position, 46f))
                        continue;
                    CreateNormalizedDecoration(
                        barrierSource,
                        "StreetBarrier_DangerApproach_" + stable++.ToString("D3"),
                        position,
                        yaw + 90f,
                        new Vector3(8f, 3.2f, 2.6f));
                }
            }
        }

        static DarkCity2PlacementRole ResolveStreetRole(
            DarkCity2DistrictKind district,
            AirCombatRoadStrip road,
            int stable)
        {
            switch (district)
            {
                case DarkCity2DistrictKind.Industrial:
                    return DarkCity2PlacementRole.StreetIndustrial;
                case DarkCity2DistrictKind.Transit:
                    return DarkCity2PlacementRole.StreetTransit;
                case DarkCity2DistrictKind.Service:
                    return DarkCity2PlacementRole.StreetUtility;
                case DarkCity2DistrictKind.Commercial:
                    return PositiveStableModulo(stable, 3) == 0
                        ? DarkCity2PlacementRole.StreetWarning
                        : DarkCity2PlacementRole.StreetUtility;
                default:
                    return road.dangerLane
                        ? DarkCity2PlacementRole.StreetWarning
                        : DarkCity2PlacementRole.StreetUtility;
            }
        }

        static Vector3 StreetMaximumSize(DarkCity2PlacementRole role)
        {
            switch (role)
            {
                case DarkCity2PlacementRole.StreetTransit:
                    return new Vector3(13f, 7f, 9f);
                case DarkCity2PlacementRole.StreetIndustrial:
                    return new Vector3(14f, 11f, 11f);
                case DarkCity2PlacementRole.StreetWarning:
                    return new Vector3(8f, 8f, 4f);
                default:
                    return new Vector3(7f, 9f, 6f);
            }
        }

        bool PointInsideGeneratedBuilding(Vector3 position, float padding)
        {
            for (int index = 0; index < generatedBuildings.Count; index++)
            {
                Bounds bounds = ResolveLotBounds(generatedBuildings[index].lot);
                if (position.x >= bounds.min.x - padding &&
                    position.x <= bounds.max.x + padding &&
                    position.z >= bounds.min.z - padding &&
                    position.z <= bounds.max.z + padding)
                {
                    return true;
                }
            }
            return false;
        }

        bool IsNearRoadIntersection(Vector3 position, float clearance)
        {
            float clearanceSquared = clearance * clearance;
            for (int a = 0; a < plan.roads.Count; a++)
            for (int b = a + 1; b < plan.roads.Count; b++)
            {
                if (TryGetRoadIntersection(
                        plan.roads[a],
                        plan.roads[b],
                        out Vector3 point) &&
                    (point - position).sqrMagnitude < clearanceSquared)
                {
                    return true;
                }
            }
            return false;
        }

        GameObject CreateNormalizedDecoration(
            GameObject sourcePrefab,
            string objectName,
            Vector3 localPosition,
            float yaw,
            Vector3 maximumSize)
        {
            if (sourcePrefab == null || decorationRoot == null)
                return null;

            var anchor = new GameObject(objectName);
            anchor.transform.SetParent(decorationRoot, false);
            anchor.transform.localPosition = localPosition;
            anchor.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            GameObject visual = Instantiate(
                sourcePrefab,
                anchor.transform,
                false);
            visual.name = "Visual_SourceAxis_Up+Y_Front+Z_" +
                          sourcePrefab.name;
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;
            RemoveColliders(visual);

            if (!TryCalculateBounds(anchor.transform, out Bounds bounds))
            {
                if (Application.isPlaying)
                    Destroy(anchor);
                else
                    DestroyImmediate(anchor);
                return null;
            }

            float scale = Mathf.Min(
                maximumSize.x / Mathf.Max(0.01f, bounds.size.x),
                Mathf.Min(
                    maximumSize.y / Mathf.Max(0.01f, bounds.size.y),
                    maximumSize.z / Mathf.Max(0.01f, bounds.size.z)));
            scale = Mathf.Clamp(scale, 0.01f, 100f);
            visual.transform.localScale = Vector3.one * scale;
            visual.transform.localPosition = new Vector3(
                -bounds.center.x * scale,
                -bounds.min.y * scale,
                -bounds.center.z * scale);

            // 装饰仍然没有碰撞体。爆炸使用登记后的渲染包围盒命中，
            // 不会在飞行航线上产生看不见的障碍。
            UrbanDestructibleDecoration destructible =
                anchor.AddComponent<UrbanDestructibleDecoration>();
            destructible.Configure(destructionCoordinator);
            return anchor;
        }

        static bool TryCalculateBounds(Transform root, out Bounds bounds)
        {
            bounds = new Bounds();
            bool initialized = false;
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Bounds world = renderers[i].bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = new Vector3(
                        (corner & 1) == 0 ? world.min.x : world.max.x,
                        (corner & 2) == 0 ? world.min.y : world.max.y,
                        (corner & 4) == 0 ? world.min.z : world.max.z);
                    point = root.InverseTransformPoint(point);
                    if (!initialized)
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        initialized = true;
                    }
                    else
                    {
                        bounds.Encapsulate(point);
                    }
                }
            }
            return initialized && bounds.size.sqrMagnitude > 0.0001f;
        }

        static int PositiveStableModulo(int value, int modulo)
        {
            if (modulo <= 0)
                return 0;
            int result = value % modulo;
            return result < 0 ? result + modulo : result;
        }

        void BuildBackgroundSkyline()
        {
            if (buildingCatalog == null && darkCity2Catalog == null)
                return;
            Transform background = CreateRoot(
                buildingRoot,
                "BackgroundSkyline_无碰撞城市延伸_边界之外");
            const int BuildingsPerSide = 14;
            float outer = settings.mapSize * 0.5f + 108f;
            float span = settings.mapSize * 0.94f;
            int stable = 0;
            for (int side = 0; side < 4; side++)
            for (int index = 0; index < BuildingsPerSide; index++)
            {
                AirCombatBuildingBand band = (index + side) % 3 == 0
                    ? AirCombatBuildingBand.High
                    : AirCombatBuildingBand.Medium;
                GameObject prefab = darkCity2Catalog != null
                    ? darkCity2Catalog.ResolveBackground(
                        index * 17 + side * 31 + settings.seed)
                    : buildingCatalog.Resolve(
                        band,
                        index * 17 + side * 31 + settings.seed);
                if (prefab == null)
                    continue;
                GameObject building = Instantiate(prefab, background, false);
                NormalizedBuildingModelInfo modelInfo =
                    building.GetComponent<NormalizedBuildingModelInfo>();
                Vector3 authoredSize = modelInfo != null
                    ? modelInfo.AuthoredSize
                    : Vector3.one;
                float width = 40f + ((index * 13 + side * 7) % 5) * 5f;
                float depth = 38f + ((index * 19 + side * 11) % 4) * 6f;
                float height = band == AirCombatBuildingBand.High
                    ? 268f + ((index * 29 + side * 23) % 5) * 24f
                    : 148f + ((index * 31 + side * 17) % 5) * 18f;
                building.transform.localScale = new Vector3(
                    width / Mathf.Max(0.1f, authoredSize.x),
                    height / Mathf.Max(0.1f, authoredSize.y),
                    depth / Mathf.Max(0.1f, authoredSize.z));
                float across = Mathf.Lerp(
                    -span * 0.5f,
                    span * 0.5f,
                    index / (float)(BuildingsPerSide - 1));
                float stagger = (index & 1) == 0 ? 0f : 54f;
                Vector3 position;
                float yaw;
                switch (side)
                {
                    case 0:
                        position = new Vector3(across, 0f, outer + stagger);
                        yaw = 180f;
                        break;
                    case 1:
                        position = new Vector3(across, 0f, -outer - stagger);
                        yaw = 0f;
                        break;
                    case 2:
                        position = new Vector3(outer + stagger, 0f, across);
                        yaw = -90f;
                        break;
                    default:
                        position = new Vector3(-outer - stagger, 0f, across);
                        yaw = 90f;
                        break;
                }
                building.name = "BackgroundBuilding_NoCollision_" +
                                stable++.ToString("D2");
                building.transform.localPosition = position;
                building.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                RemoveColliders(building);
            }
        }

        static void RemoveColliders(GameObject target)
        {
            Collider[] colliders = target.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
                if (Application.isPlaying)
                    Destroy(colliders[i]);
                else
                    DestroyImmediate(colliders[i]);
            }
        }

        void BuildMissionLayer()
        {
            GameObject spawn = CreatePrimitive(
                PrimitiveType.Cylinder,
                missionRoot,
                "PlayerSpawn_真实飞机出生包线",
                false);
            spawn.transform.localPosition = plan.playerSpawn;
            spawn.transform.localScale = new Vector3(34f, 6f, 34f);
            AssignMaterial(spawn, palette.spawn);

            GameObject objective = CreatePrimitive(
                PrimitiveType.Sphere,
                missionRoot,
                settings.mission == AirCombatCityMission.Clearance
                    ? "ClearanceConflict_清剿中心"
                    : "FacilityAssaultObjective_设施突袭中心",
                false);
            objective.transform.localPosition = plan.objective;
            objective.transform.localScale = Vector3.one * 30f;
            AssignMaterial(objective, palette.objective);

            for (int i = 0; i < plan.ingresses.Count; i++)
            {
                AirCombatEnemyIngress ingress = plan.ingresses[i];
                Material material = ingress.kind == AirCombatEnemyLaneKind.Suicide
                    ? palette.suicideRoute
                    : palette.rangedRoute;
                GameObject marker = CreatePrimitive(
                    PrimitiveType.Cylinder,
                    missionRoot,
                    "EnemyIngress_" + ingress.kind + "_" + i.ToString("D2"),
                    false);
                marker.transform.localPosition = ingress.position;
                marker.transform.localScale = new Vector3(22f, 18f, 22f);
                AssignMaterial(marker, material);
            }

            for (int i = 0; i < plan.facilityCores.Count; i++)
            {
                GameObject core = CreatePrimitive(
                    PrimitiveType.Sphere,
                    missionRoot,
                    "FacilityCore_可破坏能源核心_" + (i + 1),
                    false);
                core.transform.localPosition =
                    plan.facilityCores[i] + Vector3.up * 42f;
                core.transform.localScale = Vector3.one * 24f;
                AssignMaterial(core, palette.objective);
            }
        }

        void BuildValidationLayer()
        {
            CreateCircle(
                "TurnRadius_中心最小转弯半径_" +
                settings.turnRadius.ToString("0") + "m",
                new Vector3(0f, settings.mediumAltitude, 0f),
                settings.turnRadius,
                palette.objective);
            CreateCircle(
                "ManeuverBowl_完整机动直径_" +
                settings.ManeuverDiameter.ToString("0") + "m",
                new Vector3(0f, settings.mediumAltitude, 0f),
                settings.ManeuverDiameter * 0.5f,
                palette.maneuverVolume);
            CreateAltitudeFrame(
                settings.lowAltitude,
                palette.mainRoute,
                "Low_" + settings.lowAltitude.ToString("0") + "m");
            CreateAltitudeFrame(
                settings.mediumAltitude,
                palette.maskedRoute,
                "Medium_" + settings.mediumAltitude.ToString("0") + "m");
            CreateAltitudeFrame(
                settings.highAltitude,
                palette.longRangeRoute,
                "High_" + settings.highAltitude.ToString("0") + "m");
            CreateAltitudeFrame(
                settings.maximumAltitude,
                palette.suicideRoute,
                "SoftCeiling_" + settings.maximumAltitude.ToString("0") +
                "m_NoVerticalBypass");
        }

        void CreateAltitudeFrame(float altitude, Material material, string label)
        {
            float half = settings.mapSize * 0.5f;
            var frame = new GameObject("AltitudeFrame_" + label);
            frame.transform.SetParent(validationRoot, false);
            LineRenderer line = frame.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = 4;
            line.widthMultiplier = 2.5f;
            line.sharedMaterial = material;
            line.SetPosition(0, new Vector3(-half, altitude, -half));
            line.SetPosition(1, new Vector3(-half, altitude, half));
            line.SetPosition(2, new Vector3(half, altitude, half));
            line.SetPosition(3, new Vector3(half, altitude, -half));
        }

        void CreateCircle(
            string objectName,
            Vector3 center,
            float radius,
            Material material)
        {
            const int Segments = 64;
            var circle = new GameObject(objectName);
            circle.transform.SetParent(validationRoot, false);
            LineRenderer line = circle.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = Segments;
            line.widthMultiplier = 4f;
            line.sharedMaterial = material;
            for (int i = 0; i < Segments; i++)
            {
                float angle = i * Mathf.PI * 2f / Segments;
                line.SetPosition(
                    i,
                    center + new Vector3(
                        Mathf.Cos(angle) * radius,
                        0f,
                        Mathf.Sin(angle) * radius));
            }
        }

        Material MaterialForBuilding(AirCombatBuildingBand band)
        {
            switch (band)
            {
                case AirCombatBuildingBand.High:
                    return palette.highBuilding;
                case AirCombatBuildingBand.Medium:
                    return palette.mediumBuilding;
                case AirCombatBuildingBand.Facility:
                    return palette.facility;
                default:
                    return palette.lowBuilding;
            }
        }

        Material MaterialForRoute(AirCombatRouteKind kind, int index)
        {
            switch (kind)
            {
                case AirCombatRouteKind.MaskedFlank:
                    return palette.maskedRoute;
                case AirCombatRouteKind.LongRange:
                    return palette.longRangeRoute;
                case AirCombatRouteKind.EnemyIngress:
                    int ingressIndex = Mathf.Clamp(index - 3, 0, plan.ingresses.Count - 1);
                    return plan.ingresses.Count > 0 &&
                           plan.ingresses[ingressIndex].kind ==
                           AirCombatEnemyLaneKind.Suicide
                        ? palette.suicideRoute
                        : palette.rangedRoute;
                default:
                    return palette.mainRoute;
            }
        }

        Material MaterialForVolume(AirCombatVolumeKind kind)
        {
            switch (kind)
            {
                case AirCombatVolumeKind.OcclusionGate:
                    return palette.occlusionVolume;
                case AirCombatVolumeKind.ExposureLane:
                    return palette.exposureVolume;
                case AirCombatVolumeKind.RecoveryPocket:
                    return palette.recoveryVolume;
                case AirCombatVolumeKind.AssaultBreach:
                    return palette.exposureVolume;
                case AirCombatVolumeKind.DominancePerch:
                    return palette.longRangeRoute;
                case AirCombatVolumeKind.DangerPlaza:
                    return palette.exposureVolume;
                default:
                    return palette.maneuverVolume;
            }
        }

        static GameObject CreatePrimitive(
            PrimitiveType type,
            Transform parent,
            string objectName,
            bool keepCollider)
        {
            GameObject result = GameObject.CreatePrimitive(type);
            result.name = objectName;
            result.transform.SetParent(parent, false);
            if (!keepCollider)
            {
                Collider collider = result.GetComponent<Collider>();
                if (collider != null)
                {
                    if (Application.isPlaying)
                        Destroy(collider);
                    else
                        DestroyImmediate(collider);
                }
            }
            return result;
        }

        static void AssignMaterial(GameObject target, Material material)
        {
            if (target == null || material == null)
                return;
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                renderers[i].sharedMaterial = material;
        }

        static Transform CreateRoot(Transform parent, string objectName)
        {
            var root = new GameObject(objectName);
            root.transform.SetParent(parent, false);
            return root.transform;
        }

        void SetGeneratedLayerActive(string prefix, bool active)
        {
            Transform generated = transform.Find(GeneratedRootName);
            if (generated == null)
                return;
            for (int index = 0; index < generated.childCount; index++)
            {
                Transform child = generated.GetChild(index);
                if (child.name.StartsWith(
                        prefix,
                        System.StringComparison.Ordinal))
                {
                    child.gameObject.SetActive(active);
                }
            }
        }

        void ClearGenerated()
        {
            Transform generated = transform.Find(GeneratedRootName);
            if (generated == null)
                return;
            if (Application.isPlaying)
                Destroy(generated.gameObject);
            else
                DestroyImmediate(generated.gameObject);
        }

        sealed class GeneratedBuildingRecord
        {
            public AirCombatBuildingLot lot;
            public GameObject instance;
            public UrbanDestructibleBuilding destructible;
            public Bounds actualBounds;
            public bool actualBoundsReady;
        }

        sealed class BridgeCandidate
        {
            public GeneratedBuildingRecord first;
            public GeneratedBuildingRecord second;
            public Vector3 firstSocket;
            public Vector3 secondSocket;
            public float centerY;
            public float openSpan;
            public float finalSpan;
            public GameObject prefab;
            public int layerIndex;
            public int layerCount;
            public bool crossesRoadBlock;
            public float score;
            public string tacticalGroupId = string.Empty;
            public AirCombatSkybridgeRole tacticalRole =
                AirCombatSkybridgeRole.Ordinary;
            public float tacticalClearHeight;
            public float tacticalClearWidth;
        }

        sealed class TacticalBridgeGroup
        {
            public string stableId;
            public BridgeCandidate lower;
            public BridgeCandidate upper;
            public float clearHeight;
            public float clearWidth;
            public int quadrant;
            public float score;
            public bool considered;
        }

        sealed class CableCandidate
        {
            public GeneratedBuildingRecord first;
            public GeneratedBuildingRecord second;
            public Vector3 start;
            public Vector3 end;
            public float sag;
            public float score;
        }

        struct BuiltBridgeSegment
        {
            public Vector3 start;
            public Vector3 end;
            public float centerY;
        }

        struct BuiltCableSegment
        {
            public Vector3 start;
            public Vector3 end;
        }

        void OnGUI()
        {
            if (!Application.isPlaying || !showRuntimePanel)
                return;
            GUILayout.BeginArea(new Rect(18f, 18f, 650f, 330f), GUI.skin.box);
            GUILayout.Label("空战语义先行的城市 PCG 实验场");
            GUILayout.Label(
                "任务：" +
                (settings.mission == AirCombatCityMission.Clearance
                    ? "清剿（两分钟割草入口布局）"
                    : "设施突袭（三能源核心）"));
            GUILayout.Label(
                "正式包线：55m/s　转弯半径 95m　射程 480m　" +
                "空域 " + settings.lowAltitude.ToString("0") + "/" +
                settings.mediumAltitude.ToString("0") + "/" +
                settings.highAltitude.ToString("0") + "m");
            GUILayout.Label(LastSummary);
            GUILayout.Label(
                "实体连廊：150　跨道路街区：" +
                lastCrossRoadBlockSkybridgeCount +
                "（硬性要求≥75）");
            if (report != null)
            {
                GUILayout.Label(
                    "主走廊 " + report.mainCorridorWidth.ToString("0") +
                    "m　首次接触 " + report.firstContactSeconds.ToString("0.0") +
                    "s　航路无建筑侵入：" + (report.routesClear ? "是" : "否"));
                if (!report.valid)
                    GUILayout.Label("失败原因：" + report.failureReason);
            }
            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("上一 Seed（P）")) PreviousSeed();
            if (GUILayout.Button("重新生成（R）")) Rebuild();
            if (GUILayout.Button("下一 Seed（N）")) NextSeed();
            GUILayout.EndHorizontal();
            if (GUILayout.Button("切换清剿/设施突袭（M）")) ToggleMission();
            if (GUILayout.Button("显示/隐藏空战语义（G）"))
                showSemanticGizmos = !showSemanticGizmos;
            GUILayout.Label(
                "青=主航路　绿=遮挡侧翼　黄=远射航路　" +
                "红紫=自爆入口　橙=远程入口");
            GUILayout.EndArea();
        }

        void OnDrawGizmos()
        {
            if (!showSemanticGizmos || plan == null)
                return;
            Gizmos.matrix = transform.localToWorldMatrix;

            for (int i = 0; i < plan.volumes.Count; i++)
            {
                AirCombatTacticalVolume volume = plan.volumes[i];
                Gizmos.color = ColorForVolume(volume.kind);
                Gizmos.DrawWireCube(volume.center, volume.size);
#if UNITY_EDITOR
                if (showLabels)
                {
                    UnityEditor.Handles.Label(
                        transform.TransformPoint(
                            volume.center + Vector3.up * volume.size.y * 0.52f),
                        ChineseVolumeName(volume.kind));
                }
#endif
            }

            for (int i = 0; i < plan.ingresses.Count; i++)
            {
                AirCombatEnemyIngress ingress = plan.ingresses[i];
                Gizmos.color = ingress.kind == AirCombatEnemyLaneKind.Suicide
                    ? new Color(1f, 0.05f, 0.2f, 0.9f)
                    : new Color(1f, 0.55f, 0.05f, 0.9f);
                Gizmos.DrawWireSphere(ingress.position, 30f);
#if UNITY_EDITOR
                if (showLabels)
                {
                    UnityEditor.Handles.Label(
                        transform.TransformPoint(ingress.position + Vector3.up * 32f),
                        ingress.kind == AirCombatEnemyLaneKind.Suicide
                            ? "自爆机低空入口"
                            : "远程机入口");
                }
#endif
            }
        }

        static Color ColorForVolume(AirCombatVolumeKind kind)
        {
            switch (kind)
            {
                case AirCombatVolumeKind.OcclusionGate:
                    return new Color(0.15f, 1f, 0.35f, 0.8f);
                case AirCombatVolumeKind.ExposureLane:
                    return new Color(1f, 0.7f, 0.05f, 0.8f);
                case AirCombatVolumeKind.RecoveryPocket:
                    return new Color(0.05f, 0.9f, 1f, 0.8f);
                case AirCombatVolumeKind.SpawnBasin:
                    return new Color(0.2f, 0.65f, 1f, 0.8f);
                case AirCombatVolumeKind.KiteLoop:
                    return new Color(0.65f, 0.25f, 1f, 0.82f);
                case AirCombatVolumeKind.AssaultBreach:
                    return new Color(1f, 0.18f, 0.08f, 0.85f);
                case AirCombatVolumeKind.DominancePerch:
                    return new Color(1f, 0.92f, 0.12f, 0.9f);
                case AirCombatVolumeKind.DangerPlaza:
                    return new Color(1f, 0.28f, 0.08f, 0.85f);
                default:
                    return new Color(0.35f, 1f, 0.55f, 0.8f);
            }
        }

        static string ChineseVolumeName(AirCombatVolumeKind kind)
        {
            switch (kind)
            {
                case AirCombatVolumeKind.SpawnBasin:
                    return "出生安全空域";
                case AirCombatVolumeKind.ManeuverBowl:
                    return "完整转弯机动区";
                case AirCombatVolumeKind.OcclusionGate:
                    return "视线遮挡门";
                case AirCombatVolumeKind.ExposureLane:
                    return "高空远射暴露区";
                case AirCombatVolumeKind.KiteLoop:
                    return "中央拉扯环线";
                case AirCombatVolumeKind.AssaultBreach:
                    return "主轴强攻突破口";
                case AirCombatVolumeKind.DominancePerch:
                    return "制空优势屋顶";
                case AirCombatVolumeKind.DangerPlaza:
                    return "高危开放公园";
                default:
                    return "脱离与恢复区";
            }
        }
    }

    [DisallowMultipleComponent]
    sealed class CityPcgGeneratedMeshOwner : MonoBehaviour
    {
        Mesh ownedMesh;

        public void Configure(Mesh mesh)
        {
            ownedMesh = mesh;
        }

        void OnDestroy()
        {
            if (ownedMesh == null)
                return;
            if (Application.isPlaying)
                Destroy(ownedMesh);
            else
                DestroyImmediate(ownedMesh);
            ownedMesh = null;
        }
    }
}
