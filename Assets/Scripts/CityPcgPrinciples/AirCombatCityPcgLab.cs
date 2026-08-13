using System;
using System.Collections.Generic;
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
        // Road strips stop before a junction so sidewalks and markings do not
        // overlap inside it.  The junction surface must reclaim the exact same
        // shoulder or every T/cross intersection exposes a smaller paving
        // square around its centre.
        const float RoadIntersectionShoulder = 7.5f;
        // Reject two-pillar/stilt shells while retaining ordinary stepped
        // podiums.  After unioning every material mesh, audited unsupported
        // Dark City variants measure 0.00-0.17 and ordinary foundations start
        // above 0.52.
        const float MinimumGroundSupportRatio = 0.28f;
        const float MinimumBridgeFacadeWidth = 14f;
        const float BridgeFacadeStabilityHalfHeight = 5f;
        const float MaximumBridgeFacadeSlope = 1.8f;
        const int BuildingProfileSliceCount = 25;
        public const string DefaultDesignProfileResourcePath =
            "CombatCityPCG/CombatCityDefault";
        public const int MinimumCitySkybridges = 150;
        public const int BossCitySkybridgeTarget = 220;
        public const float TacticalChokeClearHeight = 14f;
        public const float VisualBackgroundMaximumOutsideDistance = 3200f;
        public const float VisualBackgroundMaximumBuildingHeight = 470f;
        // The default arcade ship is about 18 m wide and travels roughly 9 m
        // during its 0.16 s velocity-response window at design combat speed.
        // 34 m leaves a readable correction margin without admitting the Boss.
        public const float TacticalChokeClearWidth = 34f;

        [Header("一、空战 PCG 参数")]
        [SerializeField] AirCombatCitySettings settings =
            new AirCombatCitySettings();
        [SerializeField] CombatCityPcgDesignProfile designProfile;

        [Header("二、实验场配色")]
        [SerializeField] AirCombatCityPalette palette =
            new AirCombatCityPalette();

        [Header("标准化楼房模型：底面Y0 / 顶部+Y / 正面+Z / 右侧+X")]
        [SerializeField] NewGenUrbanBuildingCatalog buildingCatalog;
        [SerializeField] DarkCity2UrbanCatalog darkCity2Catalog;
        [SerializeField] GameObject roadJunctionPrefab;

        [Header("NewGen Urban 装饰（运行时先标准化底面、朝向和包围盒）")]
        [SerializeField] GameObject rooftopMechanicalPrefab;
        [SerializeField] GameObject[] rooftopBillboardPrefabs =
            Array.Empty<GameObject>();
        [SerializeField] GameObject streetLightPrefab;

        [Header("三、Scene 可读性")]
        [SerializeField] bool showSemanticGizmos = true;
        [SerializeField] bool showRuntimePanel = true;
        [SerializeField] bool keepBuildingColliders = true;
        [Tooltip("关闭后只生成可玩城市与地面，不生成战斗边界外的低模城市背景。")]
        [SerializeField] bool buildVisualBackground = true;

        [Header("城市建筑破坏（不作用于地面和玩家模块）")]
        [SerializeField] UrbanDestructionSettings destructionSettings =
            new UrbanDestructionSettings();

        AirCombatCityPlan plan;
        AirCombatCityReport report;
        Transform semanticRoot;
        Transform routeRoot;
        Transform roadRoot;
        Transform buildingRoot;
        Transform boundaryRoot;
        Transform connectionRoot;
        Transform decorationRoot;
        Transform environmentalTrapPreviewRoot;
        Transform missionRoot;
        Transform validationRoot;
        Material windPreviewRibbonMaterial;
        Material windPreviewArrowMaterial;
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
        readonly System.Collections.Generic.Dictionary<int, BuildingGeometryProfile>
            buildingGeometryProfiles =
                new System.Collections.Generic.Dictionary<int, BuildingGeometryProfile>();
        readonly System.Collections.Generic.List<BuiltBridgeSegment>
            runtimeBuiltBridgeSegments =
                new System.Collections.Generic.List<BuiltBridgeSegment>(160);
        readonly System.Collections.Generic.List<BuiltCableSegment>
            runtimeBuiltCableSegments =
                new System.Collections.Generic.List<BuiltCableSegment>(32);
        AirCombatCityRuntimeGeometrySnapshot runtimeGeometrySnapshot;
        int lastSkybridgeCandidateCount;
        int lastIntraBlockSkybridgeCandidateCount;
        int lastCrossBlockSkybridgeCandidateCount;
        int lastSkybridgeDegreeRejectCount;
        int lastSkybridgeCrossingRejectCount;
        int lastCrossRoadBlockSkybridgeCount;
        int lastAerialCableCandidateCount;
        int lastAerialCableCount;
        int lastDestructionBridgeCandidateBuildingCount;
        int runtimeDifficultyTier;

        public AirCombatCitySettings Settings => settings;
        public CombatCityPcgDesignProfile DesignProfile => designProfile;
        public AirCombatCityPlan Plan => plan;
        public AirCombatCityReport Report => report;
        public AirCombatCityRuntimeGeometrySnapshot RuntimeGeometrySnapshot =>
            runtimeGeometrySnapshot;
        public bool BuildsVisualBackground => buildVisualBackground;
        public bool HasValidPlan => report != null && report.valid;
        public string LastSummary => report == null
            ? "尚未生成"
            : report.valid || string.IsNullOrWhiteSpace(report.failureReason)
                ? report.Summary
                : report.Summary + " | 原因 " + report.failureReason;
        public int LastSkybridgeCandidateCount => lastSkybridgeCandidateCount;
        public int LastIntraBlockSkybridgeCandidateCount =>
            lastIntraBlockSkybridgeCandidateCount;
        public int LastCrossBlockSkybridgeCandidateCount =>
            lastCrossBlockSkybridgeCandidateCount;
        public int LastSkybridgeDegreeRejectCount => lastSkybridgeDegreeRejectCount;
        public int LastSkybridgeCrossingRejectCount =>
            lastSkybridgeCrossingRejectCount;
        public int LastCrossRoadBlockSkybridgeCount =>
            lastCrossRoadBlockSkybridgeCount;
        public int LastAerialCableCandidateCount =>
            lastAerialCableCandidateCount;
        public int LastAerialCableCount => lastAerialCableCount;
        public int LastDestructionBridgeCandidateBuildingCount =>
            lastDestructionBridgeCandidateBuildingCount;
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
            ConfigureRuntimeMission(
                seed,
                mission,
                difficultyTier,
                null);
        }

        public void ConfigureRuntimeMission(
            int seed,
            AirCombatCityMission mission,
            int difficultyTier,
            CombatCityPcgDesignProfile runtimeDesignProfile)
        {
            if (runtimeDesignProfile != null)
                designProfile = runtimeDesignProfile;
            settings.mission = mission;
            runtimeDifficultyTier = Mathf.Clamp(difficultyTier, 0, 5);
            ApplyDifficultyProfile(mission, runtimeDifficultyTier);
            showRuntimePanel = false;
            // A mission seed is an identity, not a disposable candidate. Let
            // the generator perform its bounded deterministic validation for
            // this one requested seed, then instantiate exactly once. This
            // avoids changing settings.seed while the loading screen is open
            // and prevents an outer retry chain from eventually falling back
            // to the natural terrain with a different city.
            int authoredAttempts = settings.maximumAttempts;
            bool accepted = false;
            try
            {
                settings.seed = seed;
                settings.maximumAttempts = Mathf.Clamp(
                    authoredAttempts,
                    1,
                    24);
                RebuildPlanOnly();
                if (HasValidPlan)
                {
                    RebuildCurrentPlan();
                    accepted = HasValidPlan && report.skybridgeNetworkValid;
                }
            }
            finally
            {
                settings.maximumAttempts = authoredAttempts;
            }
            if (!accepted)
            {
                if (report != null)
                {
                    report.valid = false;
                    if (string.IsNullOrWhiteSpace(report.failureReason))
                    {
                        report.failureReason =
                            "运行时城市候选未通过基础几何或连廊校验。";
                    }
                }
                ClearGenerated();
            }
            HideGeneratedDebugPresentation();
        }

        void OnEnable()
        {
            RebuildPlanOnly();
            if (transform.Find(GeneratedRootName) == null)
                Rebuild();
            HideGeneratedDebugPresentation();
        }

        void OnValidate()
        {
            // The dedicated tuning window writes slider values continuously.
            // Replanning the full city for every mouse-drag event makes the
            // control hitch even though geometry is intentionally rebuilt only
            // by the explicit Apply button. Keep automatic validation for all
            // formal/runtime authoring objects, but let the isolated editor
            // preview commit its accumulated parameters in one bounded pass.
            if (GetComponent<CityPcgEditorPreviewOnly>() != null)
                return;
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
            GameObject targetRooftopMechanicalPrefab = null,
            GameObject[] targetRooftopBillboardPrefabs = null,
            GameObject targetStreetLightPrefab = null,
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
            rooftopMechanicalPrefab = targetRooftopMechanicalPrefab;
            rooftopBillboardPrefabs = targetRooftopBillboardPrefabs ??
                Array.Empty<GameObject>();
            streetLightPrefab = targetStreetLightPrefab;
            RebuildPlanOnly();
        }

        [ContextMenu("重新生成空战城市")]
        public void Rebuild()
        {
            RebuildPlanOnly();
            RebuildCurrentPlan();
        }

        void RebuildCurrentPlan()
        {
            var totalBuildTimer = System.Diagnostics.Stopwatch.StartNew();
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
            boundaryRoot = CreateRoot(
                generated.transform,
                "04C_城市空气墙_位于封边高楼外侧");
            connectionRoot = CreateRoot(
                generated.transform,
                "04A_Skybridges_AuditedSockets_MultiRoute");
            decorationRoot = CreateRoot(
                generated.transform,
                "04B_城市装饰层_楼顶设备_广告牌_路灯_花坛");
            if (!Application.isPlaying &&
                GetComponent<CityPcgEditorPreviewOnly>() != null)
            {
                environmentalTrapPreviewRoot = CreateRoot(
                    generated.transform,
                    "04D_环境陷阱预览_仅视觉无物理");
                environmentalTrapPreviewRoot.gameObject.tag = "EditorOnly";
            }
            missionRoot = CreateRoot(
                generated.transform,
                "05_任务层");
            validationRoot = CreateRoot(
                generated.transform,
                "06_验证层_转弯半径与高度包线");

            destructionCoordinator = generated.AddComponent<
                UrbanDestructionCoordinator>();
            destructionCoordinator.Configure(destructionSettings);

            double stageStarted = totalBuildTimer.Elapsed.TotalMilliseconds;
            BuildFlightRoutes();
            BuildRoads();
            BuildBuildings();
            BuildBoundaryAirWalls();
            BuildBlockInfill();
            BuildTacticalCloseBuildingPairs();
            EnsureDestructionBridgeAnchors();
            float geometryBuildMilliseconds = (float)(
                totalBuildTimer.Elapsed.TotalMilliseconds - stageStarted);

            stageStarted = totalBuildTimer.Elapsed.TotalMilliseconds;
            BuildSkybridges();
            BuildAerialCableLinks();
            float connectionBuildMilliseconds = (float)(
                totalBuildTimer.Elapsed.TotalMilliseconds - stageStarted);

            stageStarted = totalBuildTimer.Elapsed.TotalMilliseconds;
            BuildUrbanDetails();
            BuildEnvironmentalTrapPreview();
            if (buildVisualBackground)
                BuildBackgroundSkyline();
            BuildMissionLayer();
            BuildValidationLayer();
            HideGeneratedDebugPresentation();
            float presentationBuildMilliseconds = (float)(
                totalBuildTimer.Elapsed.TotalMilliseconds - stageStarted);

            BuildRuntimeGeometrySnapshot(
                generated,
                geometryBuildMilliseconds,
                connectionBuildMilliseconds,
                presentationBuildMilliseconds);

            // The dedicated tuning scene persists only its parameters and
            // terrain. Rebuilding the same generated hierarchy on load keeps
            // the scene reviewable without serializing hundreds of thousands
            // of generated YAML lines into the user's trap scene.
            if (!Application.isPlaying &&
                GetComponent<CityPcgEditorPreviewOnly>() != null)
            {
                MarkEditorPreviewHierarchyTransient(generated.transform);
            }

            UrbanCityGlowRuntime cityGlow =
                generated.GetComponent<UrbanCityGlowRuntime>() ??
                generated.AddComponent<UrbanCityGlowRuntime>();
            cityGlow.Configure(1.02f, 0.78f, 2, 2);

            // Runtime slicing must read the original vertex streams and keep
            // each authored material/submesh assignment. Unity's runtime static
            // batching replaces those renderers with non-readable Combined Mesh
            // instances, so batching a destructible building makes a later cut
            // copy a city-wide batch with the wrong facade material. Keep every
            // IUrbanDestructible hierarchy independent and batch only immutable
            // roads/background presentation.
            if (Application.isPlaying)
                ApplySafeRuntimeStaticBatching(generated);

            if (runtimeGeometrySnapshot != null)
            {
                runtimeGeometrySnapshot.totalBuildMilliseconds =
                    (float)totalBuildTimer.Elapsed.TotalMilliseconds;
            }

        }

        void BuildRuntimeGeometrySnapshot(
            GameObject generated,
            float geometryBuildMilliseconds,
            float connectionBuildMilliseconds,
            float presentationBuildMilliseconds)
        {
            var snapshotTimer = System.Diagnostics.Stopwatch.StartNew();
            var buildingGeometry = new AirCombatRuntimeBuildingGeometry[
                generatedBuildings.Count];
            for (int index = 0; index < generatedBuildings.Count; index++)
            {
                GeneratedBuildingRecord source = generatedBuildings[index];
                AirCombatBuildingLot lot = source != null ? source.lot : null;
                buildingGeometry[index] = new AirCombatRuntimeBuildingGeometry
                {
                    stableId = lot?.stableId ?? string.Empty,
                    localBounds = ResolveActualBuildingBounds(source),
                    band = lot != null ? lot.band : AirCombatBuildingBand.Low,
                    archetype = lot != null
                        ? lot.archetype
                        : AirCombatBuildingArchetype.LowBlock,
                    clusterId = lot?.clusterId ?? 0,
                    destructible = source?.destructible != null
                };
            }

            var bridgeGeometry = new AirCombatRuntimeConnectionGeometry[
                runtimeBuiltBridgeSegments.Count];
            for (int index = 0;
                 index < runtimeBuiltBridgeSegments.Count;
                 index++)
            {
                BuiltBridgeSegment source = runtimeBuiltBridgeSegments[index];
                Vector3 start = source.start;
                Vector3 end = source.end;
                start.y = source.centerY;
                end.y = source.centerY;
                bridgeGeometry[index] = new AirCombatRuntimeConnectionGeometry
                {
                    localStart = start,
                    localEnd = end
                };
            }

            var cableGeometry = new AirCombatRuntimeConnectionGeometry[
                runtimeBuiltCableSegments.Count];
            for (int index = 0;
                 index < runtimeBuiltCableSegments.Count;
                 index++)
            {
                BuiltCableSegment source = runtimeBuiltCableSegments[index];
                cableGeometry[index] = new AirCombatRuntimeConnectionGeometry
                {
                    localStart = source.start,
                    localEnd = source.end
                };
            }

            int routeCount = plan?.routes?.Count ?? 0;
            var routeGeometry = new AirCombatRuntimeRouteGeometry[routeCount];
            for (int index = 0; index < routeCount; index++)
            {
                AirCombatFlightRoute source = plan.routes[index];
                Vector3[] points = source?.points == null
                    ? Array.Empty<Vector3>()
                    : (Vector3[])source.points.Clone();
                routeGeometry[index] = new AirCombatRuntimeRouteGeometry
                {
                    stableId = source?.stableId ?? string.Empty,
                    kind = source != null
                        ? source.kind
                        : AirCombatRouteKind.Main,
                    width = source != null ? source.width : 0f,
                    localPoints = points
                };
            }

            int ingressCount = plan?.ingresses?.Count ?? 0;
            var ingressGeometry = new AirCombatRuntimeIngressGeometry[
                ingressCount];
            for (int index = 0; index < ingressCount; index++)
            {
                AirCombatEnemyIngress source = plan.ingresses[index];
                ingressGeometry[index] = new AirCombatRuntimeIngressGeometry
                {
                    stableId = source?.stableId ?? string.Empty,
                    localPosition = source?.position ?? Vector3.zero,
                    localTarget = source?.target ?? Vector3.zero,
                    laneKind = source != null
                        ? source.kind
                        : AirCombatEnemyLaneKind.Ranged,
                    warningSeconds = source != null
                        ? Mathf.Max(0f, source.warningSeconds)
                        : 0f
                };
            }

            Collider[] colliders = generated != null
                ? generated.GetComponentsInChildren<Collider>(true)
                : Array.Empty<Collider>();
            int triggerColliderCount = 0;
            for (int index = 0; index < colliders.Length; index++)
            {
                if (colliders[index] != null && colliders[index].isTrigger)
                    triggerColliderCount++;
            }
            AerialCableSlowHazard[] cableHazards = generated != null
                ? generated.GetComponentsInChildren<AerialCableSlowHazard>(true)
                : Array.Empty<AerialCableSlowHazard>();
            int cableTriggerCount = 0;
            for (int index = 0; index < cableHazards.Length; index++)
            {
                if (cableHazards[index] != null)
                    cableTriggerCount += cableHazards[index].TriggerCount;
            }

            runtimeGeometrySnapshot =
                new AirCombatCityRuntimeGeometrySnapshot
                {
                    requestedSeed = plan?.requestedSeed ?? settings.seed,
                    resolvedSeed = plan?.resolvedSeed ?? settings.seed,
                    plannedBuildingCount = plan?.buildings?.Count ?? 0,
                    instantiatedBuildingCount = buildingGeometry.Length,
                    colliderCount = colliders.Length,
                    triggerColliderCount = triggerColliderCount,
                    skybridgeCount = bridgeGeometry.Length,
                    aerialCableCount = cableGeometry.Length,
                    aerialCableTriggerCount = cableTriggerCount,
                    geometryBuildMilliseconds = geometryBuildMilliseconds,
                    connectionBuildMilliseconds =
                        connectionBuildMilliseconds,
                    presentationBuildMilliseconds =
                        presentationBuildMilliseconds,
                    snapshotBuildMilliseconds =
                        (float)snapshotTimer.Elapsed.TotalMilliseconds,
                    buildings = buildingGeometry,
                    skybridges = bridgeGeometry,
                    aerialCables = cableGeometry,
                    routes = routeGeometry,
                    ingresses = ingressGeometry
                };
        }

        static void ApplySafeRuntimeStaticBatching(GameObject generated)
        {
            if (generated == null)
                return;

            var excludedFilters = new System.Collections.Generic.HashSet<int>();
            MonoBehaviour[] behaviours =
                generated.GetComponentsInChildren<MonoBehaviour>(true);
            for (int behaviourIndex = 0;
                 behaviourIndex < behaviours.Length;
                 behaviourIndex++)
            {
                MonoBehaviour behaviour = behaviours[behaviourIndex];
                if (!(behaviour is IUrbanDestructible))
                    continue;
                MeshFilter[] ownedFilters =
                    behaviour.GetComponentsInChildren<MeshFilter>(true);
                for (int filterIndex = 0;
                     filterIndex < ownedFilters.Length;
                     filterIndex++)
                {
                    MeshFilter filter = ownedFilters[filterIndex];
                    if (filter != null)
                        excludedFilters.Add(filter.GetInstanceID());
                }
            }

            MeshFilter[] filters =
                generated.GetComponentsInChildren<MeshFilter>(true);
            var batchable = new System.Collections.Generic.List<GameObject>(
                filters.Length);
            for (int index = 0; index < filters.Length; index++)
            {
                MeshFilter filter = filters[index];
                if (filter == null ||
                    excludedFilters.Contains(filter.GetInstanceID()) ||
                    !filter.gameObject.activeSelf)
                {
                    continue;
                }
                Renderer renderer = filter.GetComponent<Renderer>();
                if (renderer == null || !renderer.enabled)
                    continue;
                batchable.Add(filter.gameObject);
            }
            if (batchable.Count > 0)
            {
                StaticBatchingUtility.Combine(
                    batchable.ToArray(),
                    generated);
            }
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

        public void ConfigureDesignProfile(
            CombatCityPcgDesignProfile profile,
            AirCombatCityMission mission,
            int difficultyTier,
            int seed,
            bool rebuild = true)
        {
            designProfile = profile;
            settings.mission = mission;
            settings.seed = seed;
            runtimeDifficultyTier = Mathf.Clamp(difficultyTier, 0, 5);
            ApplyDifficultyProfile(mission, runtimeDifficultyTier);
            if (rebuild)
                Rebuild();
            else
                RebuildPlanOnly();
        }

        void ApplyDifficultyProfile(
            AirCombatCityMission mission,
            int difficultyTier)
        {
            if (designProfile == null)
            {
                designProfile = Resources.Load<CombatCityPcgDesignProfile>(
                    DefaultDesignProfileResourcePath);
            }
            settings.combatDifficulty = designProfile != null
                ? designProfile.Resolve(mission, difficultyTier)
                : CombatCityDifficultyProfile.CreateForTier(
                    difficultyTier,
                    mission);
            if (designProfile != null)
                settings.useVisualDistrictThemes =
                    designProfile.useVisualDistrictThemes;
        }

        void RebuildPlanOnly()
        {
            plan = AirCombatCityGenerator.Generate(settings, out report);
        }

        void BuildSemanticVolumes()
        {
            for (int i = 0; i < plan.opportunities.Count; i++)
            {
                TacticalOpportunity opportunity = plan.opportunities[i];
                if (opportunity == null)
                    continue;
                Material material = MaterialForOpportunity(opportunity.kind);
                var marker = new GameObject(
                    "城市战术区_" + CityOpportunityName(opportunity.kind) +
                    "_" + opportunity.stableId);
                marker.transform.SetParent(semanticRoot, false);
                LineRenderer line = marker.AddComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.loop = true;
                line.widthMultiplier = 2.8f;
                line.numCornerVertices = 4;
                line.numCapVertices = 3;
                line.sharedMaterial = material;
                float altitude = OpportunityDisplayAltitude(opportunity);
                if (opportunity.kind == TacticalOpportunityKind.KiteLoop)
                {
                    const int Segments = 32;
                    line.positionCount = Segments;
                    float radius = Mathf.Max(
                        opportunity.loopRadius,
                        Mathf.Min(opportunity.bounds.extents.x,
                            opportunity.bounds.extents.z));
                    for (int point = 0; point < Segments; point++)
                    {
                        float angle = point / (float)Segments * Mathf.PI * 2f;
                        line.SetPosition(point,
                            new Vector3(
                                opportunity.bounds.center.x + Mathf.Cos(angle) * radius,
                                altitude,
                                opportunity.bounds.center.z + Mathf.Sin(angle) * radius));
                    }
                }
                else
                {
                    Bounds bounds = opportunity.bounds;
                    line.positionCount = 4;
                    line.SetPosition(0, new Vector3(bounds.min.x, altitude, bounds.min.z));
                    line.SetPosition(1, new Vector3(bounds.min.x, altitude, bounds.max.z));
                    line.SetPosition(2, new Vector3(bounds.max.x, altitude, bounds.max.z));
                    line.SetPosition(3, new Vector3(bounds.max.x, altitude, bounds.min.z));
                }
                BuildOpportunityNodes(
                    marker.transform,
                    opportunity.entrances,
                    altitude,
                    "入口",
                    material);
                BuildOpportunityNodes(
                    marker.transform,
                    opportunity.exits,
                    altitude,
                    "出口",
                    material);
            }
        }

        void BuildOpportunityNodes(
            Transform parent,
            Vector3[] points,
            float altitude,
            string role,
            Material material)
        {
            if (points == null)
                return;
            for (int i = 0; i < points.Length; i++)
            {
                GameObject node = CreatePrimitive(
                    PrimitiveType.Sphere,
                    parent,
                    role + "_" + i.ToString("D2"),
                    false);
                node.transform.localPosition = new Vector3(
                    points[i].x,
                    altitude,
                    points[i].z);
                node.transform.localScale = Vector3.one * 5f;
                AssignMaterial(node, material);
            }
        }

        void BuildFlightRoutes()
        {
            for (int i = 0; i < plan.routes.Count; i++)
            {
                AirCombatFlightRoute route = plan.routes[i];
                // Enemy spawning remains runtime data only. It is deliberately
                // not authored as a visible route or a tactical city region.
                if (route.kind == AirCombatRouteKind.EnemyIngress)
                    continue;
                Material material = MaterialForRoute(route.kind, i);
                var routeObject = new GameObject(
                    "FlightRoute_" + route.kind + "_" + route.stableId);
                routeObject.transform.SetParent(routeRoot, false);
                LineRenderer line = routeObject.AddComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.loop = false;
                line.positionCount = route.points.Length;
                line.widthMultiplier = 10f;
                line.numCornerVertices = 6;
                line.numCapVertices = 5;
                line.sharedMaterial = material;
                line.SetPositions(route.points);

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
                float halfGap = other.width * 0.5f +
                                RoadIntersectionShoulder;
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
            var intersections = new System.Collections.Generic.Dictionary<
                string,
                RoadIntersectionPlan>();
            for (int a = 0; a < plan.roads.Count; a++)
            for (int b = a + 1; b < plan.roads.Count; b++)
            {
                AirCombatRoadStrip first = plan.roads[a];
                AirCombatRoadStrip second = plan.roads[b];
                if (!TryGetRoadIntersection(first, second, out Vector3 point))
                    continue;
                string intersectionKey =
                    Mathf.RoundToInt(point.x * 10f) + ":" +
                    Mathf.RoundToInt(point.z * 10f);
                AirCombatRoadStrip vertical =
                    Mathf.Abs(first.end.z - first.start.z) >=
                    Mathf.Abs(first.end.x - first.start.x)
                        ? first
                        : second;
                AirCombatRoadStrip horizontal = ReferenceEquals(vertical, first)
                    ? second
                    : first;
                if (!intersections.TryGetValue(
                        intersectionKey,
                        out RoadIntersectionPlan intersection))
                {
                    intersection = new RoadIntersectionPlan
                    {
                        point = point
                    };
                    intersections.Add(intersectionKey, intersection);
                }
                // Several split road records may meet at one coordinate.  The
                // first pair is not necessarily the widest one, so retain the
                // maximum width on both axes before creating a single patch.
                intersection.verticalWidth = Mathf.Max(
                    intersection.verticalWidth,
                    vertical.width);
                intersection.horizontalWidth = Mathf.Max(
                    intersection.horizontalWidth,
                    horizontal.width);
            }

            int index = 0;
            foreach (System.Collections.Generic.KeyValuePair<
                         string,
                         RoadIntersectionPlan> entry in intersections)
            {
                RoadIntersectionPlan intersection = entry.Value;
                Vector3 point = intersection.point;
                float patchWidth = intersection.verticalWidth +
                                   RoadIntersectionShoulder * 2f;
                float patchLength = intersection.horizontalWidth +
                                    RoadIntersectionShoulder * 2f;
                GameObject junction;
                if (roadJunctionPrefab != null && darkCity2Catalog == null)
                {
                    junction = Instantiate(roadJunctionPrefab, roadRoot, false);
                    junction.name = "Junction_NewGen_" + index.ToString("D2");
                    junction.transform.localPosition = point + Vector3.up * 0.045f;
                    junction.transform.localScale = new Vector3(
                        patchWidth / 31.8f,
                        1f,
                        patchLength / 21.2f);
                    RemoveColliders(junction);
                }
                else
                {
                    junction = CreateRoadStrip(
                        "Junction_Fallback_" + index.ToString("D2"),
                        point - Vector3.forward * patchLength * 0.5f,
                        point + Vector3.forward * patchLength * 0.5f,
                        patchWidth,
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
                    "MagneticCourtyard_三面磁场陷阱_" + i,
                    false);
                courtyard.transform.localPosition = new Vector3(
                    volume.center.x,
                    0.035f,
                    volume.center.z);
                courtyard.transform.localScale = new Vector3(
                    Mathf.Clamp(volume.size.x * 0.72f, 70f, 132f),
                    0.04f,
                    Mathf.Clamp(volume.size.z * 0.72f, 70f, 132f));
                AssignMaterial(
                    courtyard,
                    palette.repairCourtyard != null
                        ? palette.repairCourtyard
                        : palette.cityBlockPaving);
            }
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
                    destructible = destructible,
                    sourcePrefab = prefab
                });
            }
        }

        void BuildEnvironmentalTrapPreview()
        {
            if (environmentalTrapPreviewRoot == null || plan == null)
                return;
            if (!TryResolvePlannedWindTrapGeometry(
                    out string[] stableIds,
                    out Vector3[] centers,
                    out Vector3[] directions,
                    out Vector3[] sizes))
            {
                return;
            }
            int previewCount = centers.Length;
            Material ribbonMaterial = palette.longRangeRoute != null
                ? palette.longRangeRoute
                : palette.exposureVolume;
            Material arrowMaterial = palette.dangerLaneMarking != null
                ? palette.dangerLaneMarking
                : ribbonMaterial;
            windPreviewRibbonMaterial = CreateWindPreviewMaterial(
                "WindPreview_Cyan_Unlit",
                new Color(0.08f, 0.82f, 1f, 0.88f));
            windPreviewArrowMaterial = CreateWindPreviewMaterial(
                "WindPreview_Orange_Unlit",
                new Color(1f, 0.28f, 0.025f, 1f));
            if (windPreviewRibbonMaterial != null)
                ribbonMaterial = windPreviewRibbonMaterial;
            if (windPreviewArrowMaterial != null)
                arrowMaterial = windPreviewArrowMaterial;
            for (int index = 0; index < previewCount; index++)
            {
                BuildNaturalWindPreview(
                    stableIds[index],
                    centers[index],
                    directions[index],
                    sizes[index],
                    index,
                    ribbonMaterial,
                    arrowMaterial);
            }
        }

        bool TryResolvePlannedWindTrapGeometry(
            out string[] stableIds,
            out Vector3[] centers,
            out Vector3[] directions,
            out Vector3[] sizes)
        {
            stableIds = Array.Empty<string>();
            centers = Array.Empty<Vector3>();
            directions = Array.Empty<Vector3>();
            sizes = Array.Empty<Vector3>();
            Type directorType = Type.GetType(
                "UnityPlanet.CityPcg.UrbanEnvironmentalFieldDirector, " +
                "Assembly-CSharp");
            if (directorType == null)
                return false;
            System.Reflection.MethodInfo resolver = directorType.GetMethod(
                "ResolvePlannedWindTrapGeometry",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Static);
            if (resolver == null)
                return false;
            object[] arguments =
            {
                plan,
                settings.ValidatedCopy(),
                null,
                null,
                null,
                null
            };
            object result = resolver.Invoke(null, arguments);
            int count = result is int resolvedCount ? resolvedCount : 0;
            stableIds = arguments[2] as string[] ?? Array.Empty<string>();
            centers = arguments[3] as Vector3[] ?? Array.Empty<Vector3>();
            directions = arguments[4] as Vector3[] ?? Array.Empty<Vector3>();
            sizes = arguments[5] as Vector3[] ?? Array.Empty<Vector3>();
            return count > 0 && stableIds.Length == count &&
                   centers.Length == count && directions.Length == count &&
                   sizes.Length == count;
        }

        void BuildNaturalWindPreview(
            string stableId,
            Vector3 center,
            Vector3 direction,
            Vector3 size,
            int stableIndex,
            Material ribbonMaterial,
            Material arrowMaterial)
        {
            var rootObject = new GameObject(
                "NaturalStreetGalePreview_自然风场_" +
                stableIndex.ToString("D2") + "_" + stableId);
            rootObject.tag = "EditorOnly";
            Transform root = rootObject.transform;
            root.SetParent(environmentalTrapPreviewRoot, false);
            root.localPosition = center;
            root.localRotation = Quaternion.LookRotation(
                direction,
                Vector3.up);

            float halfWidth = size.x * 0.5f;
            float halfLength = size.z * 0.5f;
            var outlineObject = new GameObject(
                "风场边界_白色风道范围");
            outlineObject.transform.SetParent(root, false);
            LineRenderer outline = outlineObject.AddComponent<LineRenderer>();
            outline.useWorldSpace = false;
            outline.loop = true;
            outline.positionCount = 4;
            outline.widthMultiplier = 2.4f;
            outline.sharedMaterial = ribbonMaterial;
            outline.SetPosition(0, new Vector3(-halfWidth, 4f, -halfLength));
            outline.SetPosition(1, new Vector3(halfWidth, 4f, -halfLength));
            outline.SetPosition(2, new Vector3(halfWidth, 4f, halfLength));
            outline.SetPosition(3, new Vector3(-halfWidth, 4f, halfLength));

            float[] heightRatios = { 0.12f, 0.28f, 0.46f };
            float[] lateralRatios = { -0.32f, 0f, 0.32f };
            const int RibbonPoints = 15;
            for (int heightIndex = 0;
                 heightIndex < heightRatios.Length;
                 heightIndex++)
            for (int lateralIndex = 0;
                 lateralIndex < lateralRatios.Length;
                 lateralIndex++)
            {
                var ribbonObject = new GameObject(
                    "风向流线_" + heightIndex + "_" + lateralIndex);
                ribbonObject.transform.SetParent(root, false);
                LineRenderer ribbon = ribbonObject.AddComponent<LineRenderer>();
                ribbon.useWorldSpace = false;
                ribbon.positionCount = RibbonPoints;
                ribbon.widthMultiplier = heightIndex == 0 ? 2.2f : 1.45f;
                ribbon.sharedMaterial = ribbonMaterial;
                float height = Mathf.Clamp(
                    size.y * heightRatios[heightIndex],
                    18f,
                    150f);
                float lateral = halfWidth * lateralRatios[lateralIndex];
                for (int pointIndex = 0;
                     pointIndex < RibbonPoints;
                     pointIndex++)
                {
                    float t = pointIndex / (RibbonPoints - 1f);
                    ribbon.SetPosition(
                        pointIndex,
                        new Vector3(
                            lateral + Mathf.Sin(
                                t * Mathf.PI * 4f +
                                lateralIndex * 1.7f) * halfWidth * 0.10f,
                            height + Mathf.Sin(t * Mathf.PI * 3f) * 3f,
                            Mathf.Lerp(-halfLength, halfLength, t)));
                }
            }

            int arrowCount = Mathf.Clamp(
                Mathf.CeilToInt(size.z / 180f),
                4,
                8);
            for (int arrowIndex = 0; arrowIndex < arrowCount; arrowIndex++)
            {
                float z = Mathf.Lerp(
                    -halfLength + 42f,
                    halfLength - 42f,
                    arrowCount == 1
                        ? 0.5f
                        : arrowIndex / (arrowCount - 1f));
                BuildWindDirectionArrow(
                    root,
                    z,
                    size.x,
                    arrowIndex,
                    arrowMaterial);
            }

            int gateCount = Mathf.Clamp(
                Mathf.CeilToInt(size.z / 260f),
                4,
                7);
            for (int gateIndex = 0; gateIndex < gateCount; gateIndex++)
            {
                float z = Mathf.Lerp(
                    -halfLength + 28f,
                    halfLength - 28f,
                    gateCount == 1
                        ? 0.5f
                        : gateIndex / (gateCount - 1f));
                BuildWindPreviewGate(
                    root,
                    z,
                    Mathf.Max(24f, size.x + 10f),
                    gateIndex,
                    ribbonMaterial);
            }

            var labelObject = new GameObject(
                "自然风场标签_风向朝前");
            labelObject.transform.SetParent(root, false);
            labelObject.transform.localPosition = new Vector3(
                0f,
                7f,
                -halfLength + Mathf.Min(72f, size.z * 0.18f));
            labelObject.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            TextMesh label = labelObject.AddComponent<TextMesh>();
            label.text = "自然风场  >>>";
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.fontSize = 64;
            label.characterSize = 1.15f;
            label.fontStyle = FontStyle.Bold;
            label.color = new Color(0.72f, 0.94f, 1f, 1f);

            var centerLabelObject = new GameObject(
                "自然风场中央标签");
            centerLabelObject.transform.SetParent(root, false);
            centerLabelObject.transform.localPosition = new Vector3(
                0f,
                92f,
                0f);
            TextMesh centerLabel = centerLabelObject.AddComponent<TextMesh>();
            centerLabel.text = "自然风场\n风向 >>>";
            centerLabel.anchor = TextAnchor.MiddleCenter;
            centerLabel.alignment = TextAlignment.Center;
            centerLabel.fontSize = 72;
            centerLabel.characterSize = 1.25f;
            centerLabel.fontStyle = FontStyle.Bold;
            centerLabel.color = new Color(0.30f, 0.92f, 1f, 1f);
        }

        void BuildWindPreviewGate(
            Transform parent,
            float localZ,
            float gateWidth,
            int stableIndex,
            Material material)
        {
            const float GateHeight = 88f;
            const float BarThickness = 2.6f;
            var gateObject = new GameObject(
                "风场立体门_" + stableIndex.ToString("D2"));
            Transform gate = gateObject.transform;
            gate.SetParent(parent, false);
            gate.localPosition = new Vector3(0f, 0f, localZ);
            for (int side = -1; side <= 1; side += 2)
            {
                GameObject post = CreatePrimitive(
                    PrimitiveType.Cube,
                    gate,
                    side < 0 ? "左侧风柱" : "右侧风柱",
                    false);
                post.transform.localPosition = new Vector3(
                    side * gateWidth * 0.5f,
                    GateHeight * 0.5f,
                    0f);
                post.transform.localScale = new Vector3(
                    BarThickness,
                    GateHeight,
                    BarThickness);
                AssignMaterial(post, material);
            }
            GameObject top = CreatePrimitive(
                PrimitiveType.Cube,
                gate,
                "顶部风场边界",
                false);
            top.transform.localPosition = new Vector3(0f, GateHeight, 0f);
            top.transform.localScale = new Vector3(
                gateWidth + BarThickness,
                BarThickness,
                BarThickness);
            AssignMaterial(top, material);

            GameObject groundStripe = CreatePrimitive(
                PrimitiveType.Cube,
                gate,
                "地面风场横纹",
                false);
            groundStripe.transform.localPosition = new Vector3(0f, 3.5f, 0f);
            groundStripe.transform.localScale = new Vector3(
                gateWidth,
                0.35f,
                4f);
            AssignMaterial(groundStripe, material);
        }

        static Material CreateWindPreviewMaterial(
            string materialName,
            Color color)
        {
            Shader shader = Shader.Find("Sprites/Default") ??
                            Shader.Find("Unlit/Color");
            if (shader == null)
                return null;
            var material = new Material(shader)
            {
                name = materialName,
                color = color,
                hideFlags = HideFlags.HideAndDontSave,
                renderQueue = 3100
            };
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            return material;
        }

        void BuildWindDirectionArrow(
            Transform parent,
            float localZ,
            float fieldWidth,
            int stableIndex,
            Material material)
        {
            float stemWidth = Mathf.Clamp(fieldWidth * 0.16f, 2.8f, 6f);
            var arrowRootObject = new GameObject(
                "风向箭头_" + stableIndex.ToString("D2"));
            Transform arrowRoot = arrowRootObject.transform;
            arrowRoot.SetParent(parent, false);
            arrowRoot.localPosition = new Vector3(0f, 5f, localZ);

            GameObject stem = CreatePrimitive(
                PrimitiveType.Cube,
                arrowRoot,
                "箭身",
                false);
            stem.transform.localPosition = new Vector3(0f, 0f, -6f);
            stem.transform.localScale = new Vector3(
                stemWidth,
                0.35f,
                24f);
            AssignMaterial(stem, material);

            for (int side = -1; side <= 1; side += 2)
            {
                GameObject head = CreatePrimitive(
                    PrimitiveType.Cube,
                    arrowRoot,
                    side < 0 ? "左箭头" : "右箭头",
                    false);
                head.transform.localPosition = new Vector3(
                    side * 4.6f,
                    0f,
                    7.5f);
                head.transform.localRotation = Quaternion.Euler(
                    0f,
                    side * 38f,
                    0f);
                head.transform.localScale = new Vector3(
                    stemWidth,
                    0.35f,
                    16f);
                AssignMaterial(head, material);
            }
        }

        void BuildBoundaryAirWalls()
        {
            if (boundaryRoot == null || plan == null)
                return;
            for (int i = 0; i < plan.boundaryWalls.Count; i++)
            {
                AirCombatBoundaryWallPlan source = plan.boundaryWalls[i];
                if (source == null)
                    continue;
                var wall = new GameObject(
                    "BoundaryAirWall_" + source.stableId.Replace('.', '_'));
                wall.transform.SetParent(boundaryRoot, false);
                wall.transform.localPosition = source.center;
                wall.layer = gameObject.layer;
                wall.isStatic = true;
                BoxCollider collider = wall.AddComponent<BoxCollider>();
                collider.center = Vector3.zero;
                collider.size = source.size;
                collider.isTrigger = false;
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

            int standardTarget = Mathf.Clamp(
                plan.buildings.Count * 4 / 5,
                120,
                155);
            // Values above 1 are an explicit editor density multiplier. The
            // base parcel lattice is already close to full occupancy at 1, so
            // simply raising its spawn probability would have almost no visual
            // effect. Continue through more valid infill candidates instead.
            float requestedTarget = standardTarget * Mathf.Max(
                1f,
                settings.buildingDensity);
            int target = requestedTarget >= int.MaxValue
                ? int.MaxValue
                : Mathf.CeilToInt(requestedTarget);
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
                    destructible = destructible,
                    sourcePrefab = prefab
                });
                created++;
            }
        }

        GameObject ResolveBuildingPrefab(AirCombatBuildingLot lot)
        {
            if (lot == null)
                return null;
            if (lot.clusterId == 1203 ||
                (lot.clusterId <= -1300 && lot.clusterId > -1400))
            {
                GameObject bridgeAnchor = ResolveReliableBridgeCatalogPrefab(
                    lot.band,
                    lot.visualVariant);
                if (bridgeAnchor != null)
                    return bridgeAnchor;
            }
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
                    GameObject districtPrefab = ResolveSupportedPrefabByRole(
                        darkCity2Catalog.districtBuildings,
                        role,
                        stable);
                    if (districtPrefab != null)
                        return districtPrefab;
                }
            }

            return ResolveSupportedCatalogPrefab(lot.band, lot.visualVariant);
        }

        GameObject ResolveReliableBridgeCatalogPrefab(
            AirCombatBuildingBand band,
            int stableVariant)
        {
            if (buildingCatalog == null)
                return null;
            GameObject[] candidates = band == AirCombatBuildingBand.High
                ? buildingCatalog.high
                : band == AirCombatBuildingBand.Facility
                    ? buildingCatalog.facility
                    : buildingCatalog.medium;
            if (candidates == null || candidates.Length == 0)
                return null;
            int start = PositiveStableModulo(stableVariant, candidates.Length);
            GameObject best = null;
            float bestScore = float.NegativeInfinity;
            for (int offset = 0; offset < candidates.Length; offset++)
            {
                GameObject candidate = candidates[(start + offset) %
                                                  candidates.Length];
                if (candidate == null ||
                    !IsGroundSupportedBuildingPrefab(candidate))
                {
                    continue;
                }
                BuildingGeometryProfile profile =
                    ResolveBuildingGeometryProfile(candidate);
                float score = ResolveStableFacadeBandScore(profile) -
                              offset * 0.01f;
                if (score <= bestScore)
                    continue;
                bestScore = score;
                best = candidate;
            }
            return bestScore >= 5f ? best : null;
        }

        static float ResolveStableFacadeBandScore(
            BuildingGeometryProfile profile)
        {
            if (profile == null || !profile.hasReadableGeometry ||
                profile.slices == null || profile.slices.Length < 3)
            {
                return float.NegativeInfinity;
            }
            float score = 0f;
            for (int index = 1; index < profile.slices.Length; index++)
            {
                BuildingProfileSlice previous = profile.slices[index - 1];
                BuildingProfileSlice current = profile.slices[index];
                float normalizedHeight = current.y /
                    Mathf.Max(0.1f, profile.authoredSize.y);
                if (normalizedHeight < 0.16f || normalizedHeight > 0.76f ||
                    !previous.valid || !current.valid ||
                    current.coverage < profile.maximumBodyCoverage * 0.32f)
                {
                    continue;
                }
                float widthChange = Mathf.Max(
                    Mathf.Abs((current.maxX - current.minX) -
                              (previous.maxX - previous.minX)) /
                    Mathf.Max(0.1f, profile.authoredSize.x),
                    Mathf.Abs((current.maxZ - current.minZ) -
                              (previous.maxZ - previous.minZ)) /
                    Mathf.Max(0.1f, profile.authoredSize.z));
                if (widthChange <= 0.08f)
                    score += 1f;
            }
            return score;
        }

        GameObject ResolveSupportedPrefabByRole(
            GameObject[] candidates,
            DarkCity2PlacementRole role,
            int stableVariant)
        {
            if (candidates == null || candidates.Length == 0)
                return null;
            int start = PositiveStableModulo(stableVariant, candidates.Length);
            for (int offset = 0; offset < candidates.Length; offset++)
            {
                GameObject candidate = candidates[(start + offset) %
                                                  candidates.Length];
                if (candidate == null)
                    continue;
                DarkCity2AssetDescriptor descriptor =
                    candidate.GetComponent<DarkCity2AssetDescriptor>();
                if (descriptor != null && descriptor.PlacementRole == role &&
                    IsGroundSupportedBuildingPrefab(candidate))
                {
                    return candidate;
                }
            }
            return null;
        }

        GameObject ResolveSupportedCatalogPrefab(
            AirCombatBuildingBand band,
            int stableVariant)
        {
            if (buildingCatalog == null)
                return null;
            GameObject[] candidates;
            switch (band)
            {
                case AirCombatBuildingBand.Medium:
                    candidates = buildingCatalog.medium;
                    break;
                case AirCombatBuildingBand.High:
                    candidates = buildingCatalog.high;
                    break;
                case AirCombatBuildingBand.Facility:
                    candidates = buildingCatalog.facility;
                    break;
                default:
                    candidates = buildingCatalog.low;
                    break;
            }
            if (candidates == null || candidates.Length == 0)
                return null;
            int start = PositiveStableModulo(stableVariant, candidates.Length);
            for (int offset = 0; offset < candidates.Length; offset++)
            {
                GameObject candidate = candidates[(start + offset) %
                                                  candidates.Length];
                if (candidate != null &&
                    IsGroundSupportedBuildingPrefab(candidate))
                {
                    return candidate;
                }
            }
            return null;
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
            if (plan.mission == AirCombatCityMission.FacilityAssault)
            {
                float facilityHalf =
                    AirCombatCityGenerator.FacilityPadSize * 0.5f;
                for (int index = 0;
                     index < plan.facilityCores.Count;
                     index++)
                {
                    Vector3 core = plan.facilityCores[index];
                    if (Mathf.Abs(position.x - core.x) <=
                            facilityHalf + size.x * 0.5f + 8f &&
                        Mathf.Abs(position.z - core.z) <=
                            facilityHalf + size.z * 0.5f + 8f)
                    {
                        return true;
                    }
                }
            }
            // The core planner already keeps authored buildings away from
            // these compatibility spawn pads. Block infill, tactical pairs
            // and destruction anchors are instantiated afterwards, so they
            // must repeat the same reservation or they can silently place a
            // real collider over a formally valid enemy entrance.
            if (IntersectsEnemyIngressReservation(
                    position,
                    size,
                    plan.ingresses))
            {
                return true;
            }
            for (int index = 0; index < plan.volumes.Count; index++)
            {
                AirCombatTacticalVolume volume = plan.volumes[index];
                if (volume.kind != AirCombatVolumeKind.SpawnBasin &&
                    volume.kind != AirCombatVolumeKind.RecoveryPocket)
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
            return InfillOverlapsBuilding(
                position,
                size,
                settings.MinimumDefaultPresetBuildingGap);
        }

        bool InfillOverlapsBuilding(
            Vector3 position,
            Vector3 size,
            float requiredGap)
        {
            Bounds candidate = new Bounds(
                new Vector3(position.x, size.y * 0.5f, position.z),
                size);
            requiredGap = Mathf.Max(0f, requiredGap);
            for (int index = 0; index < generatedBuildings.Count; index++)
            {
                Bounds other = ResolveLotBounds(generatedBuildings[index].lot);
                float gapX = Mathf.Max(
                    0f,
                    Mathf.Abs(candidate.center.x - other.center.x) -
                    candidate.extents.x - other.extents.x);
                float gapZ = Mathf.Max(
                    0f,
                    Mathf.Abs(candidate.center.z - other.center.z) -
                    candidate.extents.z - other.extents.z);
                if (Mathf.Sqrt(gapX * gapX + gapZ * gapZ) < requiredGap)
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
                    float minimumPresetGap =
                        settings.MinimumDefaultPresetBuildingGap;
                    float skillGap = gateClass == 0
                        ? minimumPresetGap +
                          PositiveStableModulo(stable / 29 + attempt, 5)
                        : gateClass == 1
                            ? minimumPresetGap + 10f +
                              PositiveStableModulo(stable / 29 + attempt, 7)
                            : minimumPresetGap + 20f +
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
                        InfillOverlapsBuilding(candidate, size, 4f))
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
                        // A close-pair companion may support the collapse
                        // tower, but it is not itself a collapse target.
                        clusterId = anchor.lot.clusterId == 1203
                            ? -1300 - created
                            : anchor.lot.clusterId,
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
                destructible = destructible,
                sourcePrefab = prefab
            };
        }

        void EnsureDestructionBridgeAnchors()
        {
            var collapseBuildings = new System.Collections.Generic.List<
                GeneratedBuildingRecord>();
            for (int index = 0; index < generatedBuildings.Count; index++)
            {
                GeneratedBuildingRecord record = generatedBuildings[index];
                if (record.lot.clusterId == 1203 &&
                    record.lot.stableId.StartsWith(
                        "building.combat-region.collapse-candidate.",
                        StringComparison.Ordinal))
                {
                    collapseBuildings.Add(record);
                }
            }

            Vector3[] directions =
            {
                Vector3.forward,
                Vector3.back,
                Vector3.right,
                Vector3.left
            };
            float[] angleOffsets =
            {
                0f, 10f, -10f, 20f, -20f, 30f, -30f, 40f, -40f, 50f, -50f
            };
            // The collapse towers are authored before the ordinary building
            // art is instantiated. Dense merged blocks can occupy every
            // nearby 32-86 m probe even though a valid facade anchor exists
            // just beyond the next road. Continue the bounded search rather
            // than rejecting an otherwise valid city or weakening the rule
            // that both collapse sites need a physical bridge. The farther
            // probes are later validated against the modular two-span bridge
            // layout, so no catalog model is stretched past its descriptor.
            float[] openSpans =
            {
                32f, 40f, 48f, 58f, 72f, 86f, 104f, 120f, 144f
            };
            for (int collapseIndex = 0;
                 collapseIndex < collapseBuildings.Count;
                 collapseIndex++)
            {
                GeneratedBuildingRecord collapse =
                    collapseBuildings[collapseIndex];
                Bounds collapseBounds = ResolveActualBuildingBounds(collapse);
                bool created = false;
                for (int spanIndex = 0;
                     spanIndex < openSpans.Length && !created;
                     spanIndex++)
                for (int directionOffset = 0;
                     directionOffset < directions.Length && !created;
                     directionOffset++)
                for (int angleIndex = 0;
                     angleIndex < angleOffsets.Length && !created;
                     angleIndex++)
                {
                    int directionIndex = PositiveStableModulo(
                        collapseIndex * 3 + directionOffset,
                        directions.Length);
                    Vector3 direction = Quaternion.Euler(
                        0f,
                        angleOffsets[angleIndex],
                        0f) * directions[directionIndex];
                    // This is a bridge support, not another combat tower. A
                    // 34 m square facade is wide enough for every bridge head
                    // in the catalog while fitting the widened player alleys.
                    Vector3 size = new Vector3(34f, 144f, 34f);
                    float collapseExtent = Mathf.Abs(direction.x) > 0.5f
                        ? collapseBounds.extents.x
                        : collapseBounds.extents.z;
                    Vector3 candidate = collapseBounds.center;
                    candidate.y = 0f;
                    float placementProbeExtent =
                        Mathf.Max(size.x, size.z) * 0.5f;
                    candidate += direction *
                        (collapseExtent + placementProbeExtent +
                         openSpans[spanIndex]);
                    float anchorYaw = Mathf.Atan2(
                        -direction.x,
                        -direction.z) * Mathf.Rad2Deg;
                    Vector3 anchorRight = Quaternion.Euler(
                        0f,
                        anchorYaw,
                        0f) * Vector3.right;
                    Vector3 anchorForward = Quaternion.Euler(
                        0f,
                        anchorYaw,
                        0f) * Vector3.forward;
                    Vector3 footprintSize = new Vector3(
                        Mathf.Abs(anchorRight.x) * size.x +
                        Mathf.Abs(anchorForward.x) * size.z,
                        size.y,
                        Mathf.Abs(anchorRight.z) * size.x +
                        Mathf.Abs(anchorForward.z) * size.z);
                    float mapHalf = settings.mapSize * 0.5f - 52f;
                    if (Mathf.Abs(candidate.x) > mapHalf ||
                        Mathf.Abs(candidate.z) > mapHalf ||
                        InsideReservedGroundVolume(candidate, footprintSize) ||
                        InfillObstructsPlayerRoute(candidate, footprintSize) ||
                        !TryResolveNearestRoad(
                            candidate,
                            out AirCombatRoadStrip nearestRoad,
                            out Vector3 nearestRoadPoint,
                            out float roadDistance) ||
                        RoadClearanceForOrientedAnchor(
                            candidate,
                            nearestRoadPoint,
                            nearestRoad,
                            anchorRight,
                            anchorForward,
                            size,
                            roadDistance) < 5f)
                    {
                        continue;
                    }
                    if (InfillOverlapsBuilding(candidate, footprintSize, 4f) &&
                        !TryClearLowPriorityAnchorFootprint(
                            candidate,
                            footprintSize))
                    {
                        continue;
                    }
                    if (!HasClearDestructionBridgeApproach(
                            collapse,
                            collapseBounds,
                            candidate,
                            footprintSize,
                            size.y))
                    {
                        continue;
                    }

                    int stable = StableHash(
                        collapse.lot.stableId + "|bridge-anchor|" +
                        spanIndex + "|" + directionIndex + "|" + angleIndex);
                    var lot = new AirCombatBuildingLot
                    {
                        stableId = "building.destruction-bridge-anchor." +
                                   collapseIndex,
                        center = new Vector3(
                            candidate.x,
                            size.y * 0.5f,
                            candidate.z),
                        size = size,
                        yaw = anchorYaw,
                        band = AirCombatBuildingBand.High,
                        archetype = AirCombatBuildingArchetype.MidSlab,
                        clusterId = -1400 - collapseIndex,
                        visualVariant = stable
                    };
                    GameObject prefab = ResolveReliableBridgeCatalogPrefab(
                        lot.band,
                        lot.visualVariant);
                    if (prefab == null)
                        continue;
                    GameObject building = Instantiate(
                        prefab,
                        buildingRoot,
                        false);
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
                    building.transform.localRotation = Quaternion.Euler(
                        0f,
                        lot.yaw,
                        0f);
                    var anchorRecord = new GeneratedBuildingRecord
                    {
                        lot = lot,
                        instance = building,
                        sourcePrefab = prefab
                    };
                    if (!HasUsableDestructionBridgeFacadePair(
                            collapse,
                            anchorRecord))
                    {
                        building.SetActive(false);
                        if (Application.isPlaying)
                            Destroy(building);
                        else
                            DestroyImmediate(building);
                        continue;
                    }
                    building.name = "DestructionBridgeAnchor_FlatFacade_" +
                                    collapseIndex + "_For_" +
                                    collapse.lot.stableId;
                    if (!keepBuildingColliders)
                        RemoveColliders(building);
                    UrbanDestructibleBuilding destructible =
                        building.GetComponent<UrbanDestructibleBuilding>() ??
                        building.AddComponent<UrbanDestructibleBuilding>();
                    destructible.Configure(lot, destructionCoordinator);
                    anchorRecord.destructible = destructible;
                    generatedBuildings.Add(anchorRecord);
                    created = true;
                }
            }
        }

        bool HasClearDestructionBridgeApproach(
            GeneratedBuildingRecord collapse,
            Bounds collapseBounds,
            Vector3 anchorPosition,
            Vector3 anchorFootprint,
            float anchorHeight)
        {
            Vector3 direction = anchorPosition - collapseBounds.center;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f)
                return false;
            direction.Normalize();

            Bounds anchorBounds = new Bounds(
                new Vector3(
                    anchorPosition.x,
                    anchorHeight * 0.5f,
                    anchorPosition.z),
                new Vector3(
                    anchorFootprint.x,
                    anchorHeight,
                    anchorFootprint.z));
            float minimumHeight = Mathf.Max(
                40f,
                collapseBounds.min.y + 6f,
                anchorBounds.min.y + 6f);
            float maximumHeight = Mathf.Min(
                collapseBounds.max.y - 8f,
                anchorBounds.max.y - 8f);
            if (maximumHeight < minimumHeight)
                return false;

            Vector3 start = ResolveFacadeSocket(collapseBounds, direction);
            Vector3 end = ResolveFacadeSocket(anchorBounds, -direction);
            float openSpan = Vector2.Distance(
                new Vector2(start.x, start.z),
                new Vector2(end.x, end.z));
            float finalSpan = openSpan + settings.skybridgeFacadeEmbed * 2f;
            if (!TryResolveBridgeLayout(
                    finalSpan,
                    Mathf.Max(2, settings.skybridgeMaximumSegmentCount),
                    out GameObject ignoredBridgePrefab,
                    out int ignoredBridgeSegmentCount))
            {
                return false;
            }
            for (float height = minimumHeight;
                 height <= maximumHeight + 0.001f;
                 height += 4f)
            {
                start.y = height;
                end.y = height;
                if (IntersectsProtectedVolume(start, end) ||
                    ((settings.mission == AirCombatCityMission.BossEncounter ||
                      settings.mission == AirCombatCityMission.FacilityAssault) &&
                     IntersectsMissionObjective(start, end)) ||
                    IntersectsThirdBuilding(
                        collapse,
                        null,
                        start,
                        end,
                        height))
                {
                    continue;
                }
                return true;
            }
            return false;
        }

        public static bool IntersectsEnemyIngressReservation(
            Vector3 position,
            Vector3 size,
            IReadOnlyList<AirCombatEnemyIngress> ingresses)
        {
            if (ingresses == null || ingresses.Count == 0)
                return false;
            float footprintRadius = Mathf.Sqrt(
                size.x * size.x + size.z * size.z) * 0.5f;
            for (int index = 0; index < ingresses.Count; index++)
            {
                Vector3 ingress = ingresses[index].position;
                Vector2 delta = new Vector2(
                    position.x - ingress.x,
                    position.z - ingress.z);
                if (delta.magnitude < 72f + footprintRadius)
                    return true;
            }
            return false;
        }

        bool HasUsableDestructionBridgeFacadePair(
            GeneratedBuildingRecord collapse,
            GeneratedBuildingRecord anchor)
        {
            if (!CanReceiveSkybridge(collapse) ||
                !CanReceiveSkybridge(anchor))
            {
                return false;
            }

            Bounds collapseBounds = ResolveActualBuildingBounds(collapse);
            Bounds anchorBounds = ResolveActualBuildingBounds(anchor);
            Vector3 delta = anchorBounds.center - collapseBounds.center;
            delta.y = 0f;
            float centerDistance = delta.magnitude;
            if (centerDistance < settings.skybridgeMinimumCenterDistance ||
                centerDistance > settings.skybridgeMaximumCenterDistance)
                return false;

            Vector3 direction = delta / centerDistance;
            float commonTop = Mathf.Min(
                collapseBounds.max.y,
                anchorBounds.max.y);
            int pairHash = StableHash(
                ConnectionPairKey(collapse, anchor) + "|bridge-stack|");
            System.Collections.Generic.List<float> bridgeHeights =
                BuildIrregularBridgeHeights(commonTop, pairHash);
            AppendGuaranteedDestructionBridgeHeights(
                bridgeHeights,
                commonTop);

            for (int heightIndex = 0;
                 heightIndex < bridgeHeights.Count;
                 heightIndex++)
            {
                float bridgeCenterY = bridgeHeights[heightIndex];
                if (!BridgeSocketFitsFacade(
                        collapseBounds,
                        bridgeCenterY) ||
                    !BridgeSocketFitsFacade(anchorBounds, bridgeCenterY) ||
                    !TryResolveStableBridgeFacade(
                        collapse,
                        direction,
                        bridgeCenterY,
                        out Vector3 collapseSocket,
                        out float collapseFacadeWidth) ||
                    !TryResolveStableBridgeFacade(
                        anchor,
                        -direction,
                        bridgeCenterY,
                        out Vector3 anchorSocket,
                        out float anchorFacadeWidth))
                {
                    continue;
                }

                float openSpan = Vector2.Distance(
                    new Vector2(collapseSocket.x, collapseSocket.z),
                    new Vector2(anchorSocket.x, anchorSocket.z));
                float finalSpan = openSpan + settings.skybridgeFacadeEmbed * 2f;
                if (!TryResolveBridgeLayout(
                        finalSpan,
                        Mathf.Max(2, settings.skybridgeMaximumSegmentCount),
                        out GameObject bridgePrefab,
                        out int ignoredSegmentCount))
                {
                    continue;
                }
                float requiredFacadeWidth =
                    ResolveRequiredBridgeFacadeWidth(bridgePrefab);
                if (collapseFacadeWidth + 0.001f < requiredFacadeWidth ||
                    anchorFacadeWidth + 0.001f < requiredFacadeWidth)
                {
                    continue;
                }

                if (IntersectsProtectedVolume(collapseSocket, anchorSocket) ||
                    ((settings.mission == AirCombatCityMission.BossEncounter ||
                      settings.mission == AirCombatCityMission.FacilityAssault) &&
                     IntersectsMissionObjective(
                         collapseSocket,
                         anchorSocket)) ||
                    IntersectsThirdBuilding(
                        collapse,
                        anchor,
                        collapseSocket,
                        anchorSocket,
                        bridgeCenterY))
                {
                    continue;
                }
                return true;
            }
            return false;
        }

        bool TryClearLowPriorityAnchorFootprint(
            Vector3 position,
            Vector3 size)
        {
            float padding = Mathf.Clamp(
                Mathf.Min(size.x, size.z) * 0.12f,
                4f,
                12f);
            Bounds candidate = new Bounds(
                new Vector3(position.x, size.y * 0.5f, position.z),
                new Vector3(
                    size.x + padding,
                    size.y,
                    size.z + padding));
            var overlaps = new System.Collections.Generic.List<
                GeneratedBuildingRecord>();
            for (int index = 0; index < generatedBuildings.Count; index++)
            {
                GeneratedBuildingRecord record = generatedBuildings[index];
                if (!candidate.Intersects(ResolveLotBounds(record.lot)))
                    continue;
                // Core PCG buildings and combat-region structures have higher
                // priority.  Only decorative infill/skill-gap companions may
                // yield to a guaranteed ambush bridge anchor.
                if (record.lot.clusterId >= 0)
                    return false;
                overlaps.Add(record);
            }
            if (overlaps.Count == 0)
                return true;
            for (int index = 0; index < overlaps.Count; index++)
            {
                GeneratedBuildingRecord record = overlaps[index];
                generatedBuildings.Remove(record);
                if (record.instance == null)
                    continue;
                if (Application.isPlaying)
                    Destroy(record.instance);
                else
                    DestroyImmediate(record.instance);
            }
            return true;
        }

        static float RoadClearanceForOrientedAnchor(
            Vector3 center,
            Vector3 nearestRoadPoint,
            AirCombatRoadStrip road,
            Vector3 anchorRight,
            Vector3 anchorForward,
            Vector3 size,
            float roadDistance)
        {
            Vector3 towardRoad = nearestRoadPoint - center;
            towardRoad.y = 0f;
            if (towardRoad.sqrMagnitude < 0.0001f)
                return -1f;
            towardRoad.Normalize();
            float projectedHalfExtent =
                Mathf.Abs(Vector3.Dot(towardRoad, anchorRight)) *
                size.x * 0.5f +
                Mathf.Abs(Vector3.Dot(towardRoad, anchorForward)) *
                size.z * 0.5f;
            return roadDistance - road.width * 0.5f - projectedHalfExtent;
        }

        void BuildSkybridges()
        {
            connectedBuildingPairs.Clear();
            runtimeBuiltBridgeSegments.Clear();
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
                if (!CanReceiveSkybridge(first))
                    continue;
                Bounds firstBounds = ResolveActualBuildingBounds(first);
                for (int secondIndex = firstIndex + 1;
                     secondIndex < generatedBuildings.Count;
                     secondIndex++)
                {
                    GeneratedBuildingRecord second = generatedBuildings[secondIndex];
                    if (!CanReceiveSkybridge(second))
                        continue;
                    bool destructionPair = first.lot.clusterId == 1203 ||
                                           second.lot.clusterId == 1203;
                    if (!destructionPair &&
                        protectedFlightGapPairs.Contains(ConnectionPairKey(
                            first,
                            second)))
                    {
                        continue;
                    }
                    Bounds secondBounds = ResolveActualBuildingBounds(second);
                    Vector3 delta = secondBounds.center - firstBounds.center;
                    delta.y = 0f;
                    float centerDistance = delta.magnitude;
                    if (centerDistance < settings.skybridgeMinimumCenterDistance ||
                        centerDistance > settings.skybridgeMaximumCenterDistance)
                        continue;
                    Vector3 direction = delta / centerDistance;
                    float commonTop = Mathf.Min(
                        firstBounds.max.y,
                        secondBounds.max.y);
                    string pairKey = ConnectionPairKey(first, second);
                    int pairHash = StableHash(pairKey + "|bridge-stack|");
                    System.Collections.Generic.List<float> bridgeHeights =
                        BuildIrregularBridgeHeights(commonTop, pairHash);
                    if (settings.mission == AirCombatCityMission.BossEncounter)
                    {
                        // Flyable parcel spacing lowers the number of close
                        // facade pairs.  Give every otherwise eligible Boss
                        // pair a deterministic two-level candidate; selection
                        // still accepts only the tier quota and applies all
                        // crossing, facade, protected-volume and degree rules.
                        AppendBossTacticalBridgeHeights(
                            bridgeHeights,
                            commonTop,
                            pairHash);
                    }
                    if (IsGuaranteedDestructionBridgePair(first, second))
                    {
                        AppendGuaranteedDestructionBridgeHeights(
                            bridgeHeights,
                            commonTop);
                    }
                    if (bridgeHeights.Count == 0)
                        continue;

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
                        if (!TryResolveStableBridgeFacade(
                                first,
                                direction,
                                bridgeCenterY,
                                out Vector3 layerFirstSocket,
                                out float firstFacadeWidth) ||
                            !TryResolveStableBridgeFacade(
                                second,
                                -direction,
                                bridgeCenterY,
                                out Vector3 layerSecondSocket,
                                out float secondFacadeWidth))
                        {
                            continue;
                        }
                        float openSpan = Vector2.Distance(
                            new Vector2(layerFirstSocket.x, layerFirstSocket.z),
                            new Vector2(layerSecondSocket.x, layerSecondSocket.z));
                        float finalSpan = openSpan +
                                          settings.skybridgeFacadeEmbed * 2f;
                        bool guaranteedDestructionPair =
                            IsGuaranteedDestructionBridgePair(first, second);
                        if (!TryResolveBridgeLayout(
                                finalSpan,
                                guaranteedDestructionPair
                                    ? Mathf.Max(
                                        2,
                                        settings.skybridgeMaximumSegmentCount)
                                    : settings.skybridgeMaximumSegmentCount,
                                out GameObject bridgePrefab,
                                out int bridgeSegmentCount))
                        {
                            continue;
                        }
                        float requiredFacadeWidth =
                            ResolveRequiredBridgeFacadeWidth(bridgePrefab);
                        if (firstFacadeWidth + 0.001f < requiredFacadeWidth ||
                            secondFacadeWidth + 0.001f < requiredFacadeWidth)
                        {
                            continue;
                        }
                        Vector3 midpoint =
                            (layerFirstSocket + layerSecondSocket) * 0.5f;
                        if (IntersectsProtectedVolume(
                                layerFirstSocket,
                                layerSecondSocket) ||
                            ((settings.mission == AirCombatCityMission.BossEncounter ||
                              settings.mission == AirCombatCityMission.FacilityAssault) &&
                             IntersectsMissionObjective(
                                 layerFirstSocket,
                                 layerSecondSocket)) ||
                            IntersectsThirdBuilding(
                                first,
                                second,
                                layerFirstSocket,
                                layerSecondSocket,
                                bridgeCenterY))
                        {
                            continue;
                        }

                        float centralBias = new Vector2(
                                midpoint.x,
                                midpoint.z).magnitude <
                                            settings.mapSize * 0.31f
                            ? -32f
                            : 0f;

                        int stableJitter = PositiveStableModulo(
                            pairHash / 17 + layerIndex * 31,
                            17);
                        bool destructionAmbush =
                            first.lot.clusterId == 1203 ||
                            second.lot.clusterId == 1203;
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
                            segmentCount = bridgeSegmentCount,
                            layerIndex = layerIndex,
                            layerCount = bridgeHeights.Count,
                            crossesRoadBlock = crossesRoadBlock,
                            destructionAmbush = destructionAmbush,
                            score = openSpan + heightDifference * 0.16f +
                                    clusterBias + centralBias + stableJitter +
                                    layerIndex * 4.5f -
                                    (guaranteedDestructionPair
                                        ? 640f
                                        : destructionAmbush ? 240f : 0f)
                        });
                    }
                }
            }

            candidates.Sort((left, right) => left.score.CompareTo(right.score));
            lastSkybridgeCandidateCount = candidates.Count;
            lastIntraBlockSkybridgeCandidateCount = 0;
            lastCrossBlockSkybridgeCandidateCount = 0;
            for (int candidateIndex = 0;
                 candidateIndex < candidates.Count;
                 candidateIndex++)
            {
                if (candidates[candidateIndex].crossesRoadBlock)
                    lastCrossBlockSkybridgeCandidateCount++;
                else
                    lastIntraBlockSkybridgeCandidateCount++;
            }
            var destructionCandidateBuildings =
                new System.Collections.Generic.HashSet<GeneratedBuildingRecord>();
            for (int candidateIndex = 0;
                 candidateIndex < candidates.Count;
                 candidateIndex++)
            {
                BridgeCandidate candidate = candidates[candidateIndex];
                if (!candidate.destructionAmbush)
                    continue;
                if (candidate.first.lot.clusterId == 1203)
                    destructionCandidateBuildings.Add(candidate.first);
                if (candidate.second.lot.clusterId == 1203)
                    destructionCandidateBuildings.Add(candidate.second);
            }
            lastDestructionBridgeCandidateBuildingCount =
                destructionCandidateBuildings.Count;
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
            int intraBlockTarget = bossMission
                ? settings.bossIntraBlockSkybridgeTarget
                : settings.intraBlockSkybridgeTarget;
            int crossBlockTarget = bossMission
                ? settings.bossCrossBlockSkybridgeTarget
                : settings.crossBlockSkybridgeTarget;
            int targetCount = intraBlockTarget + crossBlockTarget;
            int tacticalTarget = bossMission
                ? ResolveBossTacticalChokeTarget(runtimeDifficultyTier)
                : 0;
            var tacticalGroups = bossMission
                ? BuildTacticalBridgeGroups(candidates)
                : new System.Collections.Generic.List<TacticalBridgeGroup>();
            int acceptedTacticalGroups = 0;
            int acceptedDestructionBridges = 0;
            float minimumClearHeight = float.PositiveInfinity;
            float minimumClearWidth = float.PositiveInfinity;

            TacticalOpportunity destructionOpportunity = null;
            for (int i = 0; i < plan.opportunities.Count; i++)
            {
                TacticalOpportunity opportunity = plan.opportunities[i];
                if (opportunity != null &&
                    opportunity.kind == TacticalOpportunityKind.DestructionAmbush)
                {
                    destructionOpportunity = opportunity;
                    break;
                }
            }
            int destructionBridgeTarget = destructionOpportunity != null ? 2 : 0;
            var coveredCollapseBuildings = new System.Collections.Generic.HashSet<
                GeneratedBuildingRecord>();

            // A collapse ambush is valid only when each destructible tower is
            // physically tied into the bridge network.  One span connecting
            // two collapse towers binds both endpoints; count the covered
            // towers rather than demanding a redundant second span.
            for (int index = 0;
                 index < candidates.Count &&
                 acceptedDestructionBridges < destructionBridgeTarget;
                 index++)
            {
                BridgeCandidate candidate = candidates[index];
                if (!candidate.destructionAmbush || accepted.Contains(candidate))
                    continue;
                GeneratedBuildingRecord firstCollapse =
                    candidate.first.lot.clusterId == 1203
                        ? candidate.first
                        : null;
                GeneratedBuildingRecord secondCollapse =
                    candidate.second.lot.clusterId == 1203
                        ? candidate.second
                        : null;
                bool coversNewBuilding =
                    (firstCollapse != null &&
                     !coveredCollapseBuildings.Contains(firstCollapse)) ||
                    (secondCollapse != null &&
                     !coveredCollapseBuildings.Contains(secondCollapse));
                if (!coversNewBuilding ||
                    !CanAcceptBridge(candidate, degree, built, bossMission ? 6 : 3))
                {
                    continue;
                }
                GeneratedBuildingRecord labelBuilding = firstCollapse ??
                                                        secondCollapse;
                candidate.tacticalGroupId =
                    "destruction-ambush." + labelBuilding.lot.stableId;
                candidate.tacticalRole =
                    AirCombatSkybridgeRole.DestructionAmbush;
                AcceptBridge(
                    candidate,
                    degree,
                    built,
                    selected,
                    accepted);
                if (firstCollapse != null &&
                    coveredCollapseBuildings.Add(firstCollapse))
                {
                    acceptedDestructionBridges++;
                }
                if (secondCollapse != null &&
                    coveredCollapseBuildings.Add(secondCollapse))
                {
                    acceptedDestructionBridges++;
                }
            }

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

            // Treat same-block and cross-block bridges as independent authored
            // quotas. Classification uses the final merged tactical-block group,
            // not the source road grid or the visual composition cluster.
            for (int pass = 0; pass < 2 && built.Count < targetCount; pass++)
            {
                bool fillingCrossRoadQuota = pass == 0;
                for (int index = 0;
                     index < candidates.Count && built.Count < targetCount;
                     index++)
                {
                    int intraBlockCount = selected.Count -
                                          lastCrossRoadBlockSkybridgeCount;
                    if (fillingCrossRoadQuota
                            ? lastCrossRoadBlockSkybridgeCount >= crossBlockTarget
                            : intraBlockCount >= intraBlockTarget)
                    {
                        break;
                    }
                    BridgeCandidate candidate = candidates[index];
                    if (accepted.Contains(candidate) ||
                        permanentlyRejected.Contains(candidate) ||
                        (candidate.destructionAmbush &&
                         acceptedDestructionBridges >=
                         destructionBridgeTarget &&
                         !fillingCrossRoadQuota) ||
                        candidate.crossesRoadBlock != fillingCrossRoadQuota)
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

            int achievedCrossBlock = lastCrossRoadBlockSkybridgeCount;
            int achievedIntraBlock = selected.Count - achievedCrossBlock;
            bool requestedCategoriesHavePhysicalResult =
                (crossBlockTarget <= 0 || achievedCrossBlock > 0) &&
                (intraBlockTarget <= 0 || achievedIntraBlock > 0);
            // A user-authored quota may exceed every physically legal bridge
            // combination in this generated city. The selection passes above
            // already scan each category to exhaustion when its target is not
            // met, so the achieved count is the maximum legal quota for this
            // layout. Instantiate that maximum instead of hiding every accepted
            // bridge behind an all-or-nothing target check.
            bool networkValid = requestedCategoriesHavePhysicalResult &&
                                acceptedDestructionBridges >=
                                destructionBridgeTarget &&
                                (!bossMission ||
                                 acceptedTacticalGroups >= tacticalTarget);
            int boundTacticalChokes = bossMission && networkValid
                ? BindTacticalChokeOpportunities(selected)
                : 0;
            if (bossMission)
                networkValid &= boundTacticalChokes >= acceptedTacticalGroups;
            if (report != null)
            {
                report.boundTacticalChokeCount = boundTacticalChokes;
                report.destructionAmbushBridgeCount =
                    acceptedDestructionBridges;
            }
            if (destructionOpportunity != null)
            {
                destructionOpportunity.physicalFeatureCount =
                    Mathf.Max(
                        destructionOpportunity.physicalFeatureCount,
                        2 + acceptedDestructionBridges);
                destructionOpportunity.runtimeBindingId =
                    acceptedDestructionBridges > 0
                        ? "destruction-ambush.skybridge-network"
                        : destructionOpportunity.runtimeBindingId;
            }
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
            runtimeBuiltBridgeSegments.AddRange(built);
            for (int index = 0; index < selected.Count; index++)
                CreateSkybridgeAssembly(selected[index], index);
        }

        int BindTacticalChokeOpportunities(
            System.Collections.Generic.List<BridgeCandidate> selected)
        {
            plan.opportunities.RemoveAll(
                opportunity => opportunity != null &&
                               opportunity.kind == TacticalOpportunityKind.TacticalChoke);
            var groups = new System.Collections.Generic.HashSet<string>();
            int bound = 0;
            for (int i = 0; i < selected.Count; i++)
            {
                BridgeCandidate lower = selected[i];
                if (lower.tacticalRole != AirCombatSkybridgeRole.TacticalLower ||
                    string.IsNullOrEmpty(lower.tacticalGroupId) ||
                    !groups.Add(lower.tacticalGroupId))
                {
                    continue;
                }
                BridgeCandidate upper = null;
                for (int candidateIndex = 0;
                     candidateIndex < selected.Count;
                     candidateIndex++)
                {
                    BridgeCandidate candidate = selected[candidateIndex];
                    if (candidate.tacticalRole == AirCombatSkybridgeRole.TacticalUpper &&
                        candidate.tacticalGroupId == lower.tacticalGroupId)
                    {
                        upper = candidate;
                        break;
                    }
                }
                if (upper == null)
                    continue;

                Vector3 start = lower.firstSocket;
                Vector3 end = lower.secondSocket;
                Vector3 bridgeDirection = end - start;
                bridgeDirection.y = 0f;
                float span = bridgeDirection.magnitude;
                if (span <= 0.01f)
                    continue;
                bridgeDirection /= span;
                Vector3 crossingDirection = Vector3.Cross(
                    Vector3.up,
                    bridgeDirection).normalized;
                float lowerTop = lower.centerY +
                                 ResolveBridgeVisualHeight(lower.prefab) * 0.5f;
                float upperBottom = upper.centerY -
                                    ResolveBridgeVisualHeight(upper.prefab) * 0.5f;
                float centerY = (lowerTop + upperBottom) * 0.5f;
                Vector3 center = (start + end) * 0.5f;
                center.y = centerY;
                Vector3 size = new Vector3(
                    Mathf.Abs(end.x - start.x) + lower.tacticalClearWidth,
                    Mathf.Max(0f, upperBottom - lowerTop),
                    Mathf.Abs(end.z - start.z) + lower.tacticalClearWidth);
                float approachDistance = Mathf.Max(
                    28f,
                    lower.tacticalClearWidth * 0.9f);
                plan.opportunities.Add(new TacticalOpportunity
                {
                    stableId = "opportunity.tactical-choke." +
                               lower.tacticalGroupId,
                    kind = TacticalOpportunityKind.TacticalChoke,
                    bounds = new Bounds(center, size),
                    entrances = new[]
                    {
                        center - crossingDirection * approachDistance,
                        center + bridgeDirection * span * 0.34f
                    },
                    exits = new[]
                    {
                        center + crossingDirection * approachDistance,
                        center - bridgeDirection * span * 0.34f
                    },
                    utility = 0.88f,
                    risk = 0.64f,
                    executionDifficulty = 0.66f,
                    legibility = settings.Difficulty.routeLegibility,
                    expectedTraversalSeconds = 3.4f,
                    physicalFeatureCount = 2,
                    runtimeBindingId = lower.tacticalGroupId,
                    connectedOpportunityIds = new[]
                    {
                        "opportunity.exposure.east",
                        "opportunity.kite.center"
                    }
                });
                bound++;
            }
            return bound;
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

        bool IntersectsMissionObjective(Vector3 start, Vector3 end)
        {
            Vector2 objective = new Vector2(
                plan.objective.x,
                plan.objective.z);
            Vector2 segmentStart = new Vector2(start.x, start.z);
            Vector2 segmentEnd = new Vector2(end.x, end.z);
            if (DistanceToSegment(
                    objective,
                    segmentStart,
                    segmentEnd) < 85f)
            {
                return true;
            }
            if (plan.mission != AirCombatCityMission.FacilityAssault)
                return false;
            float clearance =
                AirCombatCityGenerator.FacilityPadSize * 0.5f + 10f;
            for (int index = 0;
                 index < plan.facilityCores.Count;
                 index++)
            {
                Vector3 core = plan.facilityCores[index];
                if (DistanceToSegment(
                        new Vector2(core.x, core.z),
                        segmentStart,
                        segmentEnd) < clearance)
                {
                    return true;
                }
            }
            return false;
        }

        bool CrossesRoadBlock(Vector3 firstCenter, Vector3 secondCenter)
        {
            if (CombatDrivenCityPcgPlanner.TryCrossesMergedBlockBoundary(
                    plan,
                    new Vector2(firstCenter.x, firstCenter.z),
                    new Vector2(secondCenter.x, secondCenter.z),
                    out bool crossesMergedBlockBoundary))
            {
                return crossesMergedBlockBoundary;
            }

            // Generated buildings should normally be covered by the tactical
            // block layout. Retain the old deterministic grid classification
            // only as a defensive fallback for an incomplete external plan.
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
            lastAerialCableCandidateCount = 0;
            lastAerialCableCount = 0;
            runtimeBuiltCableSegments.Clear();
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
                    if (centerDistance <
                            settings.aerialCableMinimumCenterDistance ||
                        centerDistance >
                            settings.aerialCableMaximumCenterDistance)
                        continue;
                    Vector3 direction = delta / centerDistance;
                    Vector3 firstSocket = ResolveFacadeSocket(firstBounds, direction);
                    Vector3 secondSocket = ResolveFacadeSocket(secondBounds, -direction);
                    float openSpan = Vector2.Distance(
                        new Vector2(firstSocket.x, firstSocket.z),
                        new Vector2(secondSocket.x, secondSocket.z));
                if (openSpan < settings.aerialCableMinimumOpenSpan ||
                    openSpan > settings.aerialCableMaximumOpenSpan)
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
                    if (IntersectsProtectedVolume(midpoint) ||
                        (settings.mission ==
                             AirCombatCityMission.FacilityAssault &&
                         IntersectsMissionObjective(
                             firstSocket,
                             secondSocket)))
                        continue;

                    float sag = Mathf.Clamp(
                        openSpan * settings.aerialCableSagRatio,
                        settings.aerialCableMinimumSag,
                        settings.aerialCableMaximumSag);
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

            lastAerialCableCandidateCount = candidates.Count;
            candidates.Sort((left, right) => left.score.CompareTo(right.score));
            var degree = new System.Collections.Generic.Dictionary<
                GeneratedBuildingRecord, int>();
            var built = new System.Collections.Generic.List<BuiltCableSegment>();
            int baseTargetCount = Mathf.Clamp(
                generatedBuildings.Count / 6,
                settings.aerialCableMinimumCount,
                settings.aerialCableMaximumCount);
            float requestedCableCount = baseTargetCount *
                                        settings.aerialCableDensityMultiplier;
            int targetCount = requestedCableCount >= int.MaxValue
                ? int.MaxValue
                : Mathf.CeilToInt(requestedCableCount);
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
            lastAerialCableCount = built.Count;
            runtimeBuiltCableSegments.AddRange(built);
        }

        void CreateAerialCableAssembly(CableCandidate candidate, int stableIndex)
        {
            var assembly = new GameObject(
                "AerialCableLink_" + stableIndex.ToString("D2") + "_" +
                candidate.first.lot.stableId + "_To_" +
                candidate.second.lot.stableId +
                "_FacadeEmbedded_NonBlockingSlowTrigger");
            assembly.transform.SetParent(connectionRoot, false);

            float cableSeparation = settings.aerialCableVerticalSeparation;
            CreateCableTube(
                assembly.transform,
                "Cable_Red_Top",
                candidate.start,
                candidate.end,
                candidate.sag * 0.82f,
                cableSeparation,
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
                -cableSeparation,
                0.48f,
                darkCity2Catalog.cableBlue);
            AerialCableCurve[] curves =
                assembly.GetComponentsInChildren<AerialCableCurve>(true);
            assembly.AddComponent<AerialCableSlowHazard>().Configure(
                curves,
                settings);
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

        int ResolveMaximumCableDegree(AirCombatBuildingLot lot)
        {
            return lot.band == AirCombatBuildingBand.Facility ||
                   lot.archetype == AirCombatBuildingArchetype.Landmark
                ? settings.aerialCableLandmarkMaximumConnections
                : settings.aerialCableMaximumConnectionsPerBuilding;
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

        bool TryResolveBridgeLayout(
            float targetSpan,
            int maximumSegmentCount,
            out GameObject bridgePrefab,
            out int segmentCount)
        {
            int resolvedMaximum = Mathf.Max(1, maximumSegmentCount);
            for (int count = 1; count <= resolvedMaximum; count++)
            {
                bridgePrefab = ResolveBridgeForSpan(targetSpan / count);
                if (bridgePrefab != null)
                {
                    segmentCount = count;
                    return true;
                }
            }

            bridgePrefab = null;
            segmentCount = 0;
            return false;
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
                "_" + settings.skybridgeFacadeEmbed.ToString("0") +
                "mFacadeEmbed_" +
                (candidate.crossesRoadBlock
                    ? "CrossRoadBlock"
                    : "WithinRoadBlock"));
            assembly.transform.SetParent(connectionRoot, false);
            assembly.transform.localPosition = new Vector3(midpoint.x, 0f, midpoint.z);
            assembly.transform.localRotation = Quaternion.LookRotation(
                direction,
                Vector3.up);

            DarkCity2AssetDescriptor descriptor =
                candidate.prefab.GetComponent<DarkCity2AssetDescriptor>();
            Vector3 authoredSize = descriptor != null
                ? descriptor.AuthoredSize
                : new Vector3(8f, 3f, 56f);
            int segmentCount = Mathf.Max(1, candidate.segmentCount);
            float segmentSpan = candidate.finalSpan / segmentCount;
            for (int segmentIndex = 0;
                 segmentIndex < segmentCount;
                 segmentIndex++)
            {
                GameObject bridge = Instantiate(
                    candidate.prefab,
                    assembly.transform,
                    false);
                bridge.name = segmentCount == 1
                    ? "BridgeSpan_+Z_SocketsOutward"
                    : "BridgeSpan_" + (segmentIndex + 1) + "of" +
                      segmentCount + "_+Z_SocketsOutward";
                bridge.transform.localPosition = new Vector3(
                    0f,
                    candidate.centerY - authoredSize.y * 0.5f,
                    -candidate.finalSpan * 0.5f +
                    segmentSpan * (segmentIndex + 0.5f));
                bridge.transform.localScale = new Vector3(
                    1f,
                    1f,
                    segmentSpan / Mathf.Max(0.1f, authoredSize.z));
            }

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
                if (candidate.tacticalRole ==
                        AirCombatSkybridgeRole.TacticalLower ||
                    candidate.tacticalRole ==
                        AirCombatSkybridgeRole.TacticalUpper)
                {
                    AddTacticalBridgeMarker(
                        assembly.transform,
                        candidate,
                        authoredSize);
                }
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

        bool IntersectsProtectedVolume(Vector3 start, Vector3 end)
        {
            Vector2 lineStart = new Vector2(start.x, start.z);
            Vector2 lineEnd = new Vector2(end.x, end.z);
            float minimumY = Mathf.Min(start.y, end.y);
            float maximumY = Mathf.Max(start.y, end.y);
            for (int index = 0; index < plan.volumes.Count; index++)
            {
                AirCombatTacticalVolume volume = plan.volumes[index];
                if (volume.kind != AirCombatVolumeKind.RecoveryPocket &&
                    volume.kind != AirCombatVolumeKind.SpawnBasin)
                {
                    continue;
                }
                Vector3 halfSize = volume.size * 0.55f;
                if (maximumY < volume.center.y - halfSize.y ||
                    minimumY > volume.center.y + halfSize.y)
                {
                    continue;
                }
                Rect protectedFootprint = new Rect(
                    volume.center.x - halfSize.x,
                    volume.center.z - halfSize.z,
                    halfSize.x * 2f,
                    halfSize.z * 2f);
                if (SegmentIntersectsRect(
                        lineStart,
                        lineEnd,
                        protectedFootprint))
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

        bool CanReceiveSkybridge(GeneratedBuildingRecord record)
        {
            AirCombatBuildingLot lot = record != null ? record.lot : null;
            if (lot == null || lot.size.y < 54f ||
                lot.size.x < 20f || lot.size.z < 20f)
            {
                return false;
            }
            if (record.sourcePrefab == null)
                return true;
            BuildingGeometryProfile profile =
                ResolveBuildingGeometryProfile(record.sourcePrefab);
            return profile == null || !profile.hasReadableGeometry ||
                   profile.groundSupportRatio + 0.001f >=
                   MinimumGroundSupportRatio;
        }

        bool IsGroundSupportedBuildingPrefab(GameObject prefab)
        {
            if (prefab == null)
                return false;
            BuildingGeometryProfile profile =
                ResolveBuildingGeometryProfile(prefab);
            // A missing readable mesh should not erase an authored catalog
            // entry.  Derived Dark City buildings are readable and therefore
            // take the strict geometric path; this fallback only protects
            // manually-authored/custom prefabs.
            return profile == null || !profile.hasReadableGeometry ||
                   profile.groundSupportRatio + 0.001f >=
                   MinimumGroundSupportRatio;
        }

        BuildingGeometryProfile ResolveBuildingGeometryProfile(
            GameObject prefab)
        {
            if (prefab == null)
                return null;
            int key = prefab.GetInstanceID();
            if (!buildingGeometryProfiles.TryGetValue(
                    key,
                    out BuildingGeometryProfile profile))
            {
                profile = BuildBuildingGeometryProfile(prefab);
                buildingGeometryProfiles.Add(key, profile);
            }
            return profile;
        }

        static BuildingGeometryProfile BuildBuildingGeometryProfile(
            GameObject prefab)
        {
            NormalizedBuildingModelInfo info =
                prefab.GetComponent<NormalizedBuildingModelInfo>();
            Vector3 authoredSize = info != null
                ? info.AuthoredSize
                : Vector3.one;
            var profile = new BuildingGeometryProfile
            {
                authoredSize = authoredSize,
                slices = new BuildingProfileSlice[BuildingProfileSliceCount]
            };
            float firstY = authoredSize.y * 0.025f;
            float lastY = authoredSize.y * 0.975f;
            float step = (lastY - firstY) /
                         (BuildingProfileSliceCount - 1);
            for (int sliceIndex = 0;
                 sliceIndex < BuildingProfileSliceCount;
                 sliceIndex++)
            {
                profile.slices[sliceIndex] = new BuildingProfileSlice
                {
                    y = firstY + step * sliceIndex
                };
            }

            var sliceSegments = new System.Collections.Generic.List<
                ProfileSliceSegment>[BuildingProfileSliceCount];
            MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            for (int filterIndex = 0; filterIndex < filters.Length; filterIndex++)
            {
                MeshFilter filter = filters[filterIndex];
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null)
                    continue;
                Vector3[] vertices;
                int[] triangles;
                try
                {
                    vertices = mesh.vertices;
                    triangles = mesh.triangles;
                }
                catch (UnityException)
                {
                    continue;
                }
                if (vertices == null || triangles == null ||
                    vertices.Length == 0 || triangles.Length < 3)
                {
                    continue;
                }
                profile.hasReadableGeometry = true;
                Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix *
                                   filter.transform.localToWorldMatrix;
                for (int triangle = 0;
                     triangle + 2 < triangles.Length;
                     triangle += 3)
                {
                    int ia = triangles[triangle];
                    int ib = triangles[triangle + 1];
                    int ic = triangles[triangle + 2];
                    if (ia < 0 || ib < 0 || ic < 0 ||
                        ia >= vertices.Length || ib >= vertices.Length ||
                        ic >= vertices.Length)
                    {
                        continue;
                    }
                    Vector3 a = toRoot.MultiplyPoint3x4(vertices[ia]);
                    Vector3 b = toRoot.MultiplyPoint3x4(vertices[ib]);
                    Vector3 c = toRoot.MultiplyPoint3x4(vertices[ic]);
                    float minimumY = Mathf.Min(a.y, Mathf.Min(b.y, c.y));
                    float maximumY = Mathf.Max(a.y, Mathf.Max(b.y, c.y));
                    int firstSlice = Mathf.Clamp(
                        Mathf.CeilToInt((minimumY - firstY) /
                                        Mathf.Max(0.001f, step)),
                        0,
                        BuildingProfileSliceCount - 1);
                    int lastSlice = Mathf.Clamp(
                        Mathf.FloorToInt((maximumY - firstY) /
                                         Mathf.Max(0.001f, step)),
                        0,
                        BuildingProfileSliceCount - 1);
                    for (int sliceIndex = firstSlice;
                         sliceIndex <= lastSlice;
                         sliceIndex++)
                    {
                        float planeY = profile.slices[sliceIndex].y;
                        if (!TryIntersectTriangleAtHeight(
                                a,
                                b,
                                c,
                                planeY,
                                out Vector2 segmentA,
                                out Vector2 segmentB))
                        {
                            continue;
                        }
                        if (sliceSegments[sliceIndex] == null)
                        {
                            sliceSegments[sliceIndex] =
                                new System.Collections.Generic.List<
                                    ProfileSliceSegment>();
                        }
                        sliceSegments[sliceIndex].Add(new ProfileSliceSegment
                        {
                            a = segmentA,
                            b = segmentB
                        });
                    }
                }
            }

            // A derived Dark City prefab is split into many material children.
            // Rasterize their union once; rasterizing per child would let the
            // final mesh overwrite the actual foundation/body footprint.
            for (int sliceIndex = 0;
                 sliceIndex < BuildingProfileSliceCount;
                 sliceIndex++)
            {
                RasterizeProfileSlice(
                    sliceSegments[sliceIndex],
                    authoredSize,
                    ref profile.slices[sliceIndex]);
            }

            float maximumBodyCoverage = 0f;
            for (int sliceIndex = 0;
                 sliceIndex < profile.slices.Length;
                 sliceIndex++)
            {
                BuildingProfileSlice slice = profile.slices[sliceIndex];
                float normalizedHeight = slice.y /
                                         Mathf.Max(0.1f, authoredSize.y);
                if (normalizedHeight >= 0.14f &&
                    normalizedHeight <= 0.72f)
                {
                    maximumBodyCoverage = Mathf.Max(
                        maximumBodyCoverage,
                        slice.coverage);
                }
            }
            profile.maximumBodyCoverage = maximumBodyCoverage;
            BuildingProfileSlice ground = profile.slices[0];
            profile.groundSupportRatio = maximumBodyCoverage > 0.001f
                ? ground.coverage / maximumBodyCoverage
                : 1f;
            return profile;
        }

        static bool TryIntersectTriangleAtHeight(
            Vector3 a,
            Vector3 b,
            Vector3 c,
            float height,
            out Vector2 segmentA,
            out Vector2 segmentB)
        {
            Vector3 point0 = Vector3.zero;
            Vector3 point1 = Vector3.zero;
            Vector3 point2 = Vector3.zero;
            int count = 0;
            AddHeightIntersection(
                a, b, height, ref point0, ref point1, ref point2, ref count);
            AddHeightIntersection(
                b, c, height, ref point0, ref point1, ref point2, ref count);
            AddHeightIntersection(
                c, a, height, ref point0, ref point1, ref point2, ref count);
            float bestDistance = 0f;
            Vector3 bestA = Vector3.zero;
            Vector3 bestB = Vector3.zero;
            for (int first = 0; first < count; first++)
            for (int second = first + 1; second < count; second++)
            {
                Vector3 firstPoint = first == 0
                    ? point0
                    : first == 1 ? point1 : point2;
                Vector3 secondPoint = second == 0
                    ? point0
                    : second == 1 ? point1 : point2;
                float distance = (firstPoint - secondPoint).sqrMagnitude;
                if (distance <= bestDistance)
                    continue;
                bestDistance = distance;
                bestA = firstPoint;
                bestB = secondPoint;
            }
            segmentA = new Vector2(bestA.x, bestA.z);
            segmentB = new Vector2(bestB.x, bestB.z);
            return bestDistance > 0.000001f;
        }

        static void AddHeightIntersection(
            Vector3 a,
            Vector3 b,
            float height,
            ref Vector3 point0,
            ref Vector3 point1,
            ref Vector3 point2,
            ref int count)
        {
            float denominator = b.y - a.y;
            if (Mathf.Abs(denominator) < 0.00001f)
                return;
            float t = (height - a.y) / denominator;
            if (t < -0.0001f || t > 1.0001f)
                return;
            Vector3 point = Vector3.LerpUnclamped(a, b, Mathf.Clamp01(t));
            if ((count > 0 && (point0 - point).sqrMagnitude < 0.000001f) ||
                (count > 1 && (point1 - point).sqrMagnitude < 0.000001f) ||
                (count > 2 && (point2 - point).sqrMagnitude < 0.000001f))
            {
                return;
            }
            if (count == 0)
                point0 = point;
            else if (count == 1)
                point1 = point;
            else if (count == 2)
                point2 = point;
            else
                return;
            count++;
        }

        static void RasterizeProfileSlice(
            System.Collections.Generic.List<ProfileSliceSegment> segments,
            Vector3 authoredSize,
            ref BuildingProfileSlice slice)
        {
            if (segments == null || segments.Count < 3)
                return;
            const int Grid = 18;
            int occupied = 0;
            int minX = Grid;
            int maxX = -1;
            int minZ = Grid;
            int maxZ = -1;
            for (int z = 0; z < Grid; z++)
            for (int x = 0; x < Grid; x++)
            {
                Vector2 point = new Vector2(
                    Mathf.Lerp(-authoredSize.x * 0.5f,
                        authoredSize.x * 0.5f,
                        (x + 0.5f) / Grid),
                    Mathf.Lerp(-authoredSize.z * 0.5f,
                        authoredSize.z * 0.5f,
                        (z + 0.5f) / Grid));
                int crossings = 0;
                for (int segmentIndex = 0;
                     segmentIndex < segments.Count;
                     segmentIndex++)
                {
                    ProfileSliceSegment segment = segments[segmentIndex];
                    bool straddles = (segment.a.y > point.y) !=
                                     (segment.b.y > point.y);
                    if (!straddles)
                        continue;
                    float intersectionX = segment.a.x +
                        (point.y - segment.a.y) *
                        (segment.b.x - segment.a.x) /
                        (segment.b.y - segment.a.y);
                    if (intersectionX > point.x)
                        crossings++;
                }
                if ((crossings & 1) == 0)
                    continue;
                occupied++;
                minX = Mathf.Min(minX, x);
                maxX = Mathf.Max(maxX, x);
                minZ = Mathf.Min(minZ, z);
                maxZ = Mathf.Max(maxZ, z);
            }
            if (occupied == 0)
                return;
            slice.valid = true;
            slice.coverage = occupied / (float)(Grid * Grid);
            float cellX = authoredSize.x / Grid;
            float cellZ = authoredSize.z / Grid;
            slice.minX = -authoredSize.x * 0.5f + minX * cellX;
            slice.maxX = -authoredSize.x * 0.5f + (maxX + 1) * cellX;
            slice.minZ = -authoredSize.z * 0.5f + minZ * cellZ;
            slice.maxZ = -authoredSize.z * 0.5f + (maxZ + 1) * cellZ;
        }

        bool TryResolveStableBridgeFacade(
            GeneratedBuildingRecord record,
            Vector3 cityDirection,
            float centerY,
            out Vector3 citySocket,
            out float usableWidth)
        {
            citySocket = Vector3.zero;
            usableWidth = 0f;
            if (record == null || record.instance == null)
                return false;
            if (record.sourcePrefab == null)
            {
                Bounds bounds = ResolveActualBuildingBounds(record);
                citySocket = ResolveFacadeSocket(bounds, cityDirection);
                citySocket.y = centerY;
                usableWidth = Mathf.Min(bounds.size.x, bounds.size.z);
                return usableWidth >= MinimumBridgeFacadeWidth;
            }

            BuildingGeometryProfile profile =
                ResolveBuildingGeometryProfile(record.sourcePrefab);
            if (profile == null || !profile.hasReadableGeometry)
                return false;
            Vector3 centerLocal = CityPointToBuildingLocal(
                record,
                centerY);
            Vector3 lowerLocal = CityPointToBuildingLocal(
                record,
                centerY - BridgeFacadeStabilityHalfHeight);
            Vector3 upperLocal = CityPointToBuildingLocal(
                record,
                centerY + BridgeFacadeStabilityHalfHeight);
            if (!TrySampleBuildingSlice(profile, lowerLocal.y,
                    out BuildingProfileSlice lower) ||
                !TrySampleBuildingSlice(profile, centerLocal.y,
                    out BuildingProfileSlice center) ||
                !TrySampleBuildingSlice(profile, upperLocal.y,
                    out BuildingProfileSlice upper) ||
                center.coverage < profile.maximumBodyCoverage * 0.28f)
            {
                return false;
            }

            Vector3 worldDirection = transform.TransformDirection(cityDirection);
            Vector3 localDirection = record.instance.transform
                .InverseTransformVector(worldDirection);
            localDirection.y = 0f;
            if (localDirection.sqrMagnitude < 0.0001f)
                return false;
            localDirection.Normalize();
            if (!TryResolveSliceFacade(
                    lower,
                    localDirection,
                    lowerLocal.y,
                    out Vector3 lowerSocket,
                    out _) ||
                !TryResolveSliceFacade(
                    center,
                    localDirection,
                    centerLocal.y,
                    out Vector3 centerSocket,
                    out float centerHalfWidth) ||
                !TryResolveSliceFacade(
                    upper,
                    localDirection,
                    upperLocal.y,
                    out Vector3 upperSocket,
                    out _))
            {
                return false;
            }

            Vector3 lowerCity = BuildingPointToCityLocal(record, lowerSocket);
            Vector3 centerCity = BuildingPointToCityLocal(record, centerSocket);
            Vector3 upperCity = BuildingPointToCityLocal(record, upperSocket);
            float lowerDrift = Vector2.Distance(
                new Vector2(lowerCity.x, lowerCity.z),
                new Vector2(centerCity.x, centerCity.z));
            float upperDrift = Vector2.Distance(
                new Vector2(upperCity.x, upperCity.z),
                new Vector2(centerCity.x, centerCity.z));
            if (Mathf.Max(lowerDrift, upperDrift) > MaximumBridgeFacadeSlope)
                return false;

            Vector3 localTangent = Mathf.Abs(localDirection.x) >=
                                   Mathf.Abs(localDirection.z)
                ? Vector3.forward
                : Vector3.right;
            Vector3 widthA = centerSocket - localTangent * centerHalfWidth;
            Vector3 widthB = centerSocket + localTangent * centerHalfWidth;
            Vector3 cityWidthA = BuildingPointToCityLocal(record, widthA);
            Vector3 cityWidthB = BuildingPointToCityLocal(record, widthB);
            usableWidth = Vector2.Distance(
                new Vector2(cityWidthA.x, cityWidthA.z),
                new Vector2(cityWidthB.x, cityWidthB.z));
            centerCity.y = centerY;
            citySocket = centerCity;
            return usableWidth >= MinimumBridgeFacadeWidth;
        }

        Vector3 CityPointToBuildingLocal(
            GeneratedBuildingRecord record,
            float cityY)
        {
            Vector3 cityPoint = new Vector3(
                record.lot.center.x,
                cityY,
                record.lot.center.z);
            return record.instance.transform.InverseTransformPoint(
                transform.TransformPoint(cityPoint));
        }

        Vector3 BuildingPointToCityLocal(
            GeneratedBuildingRecord record,
            Vector3 buildingPoint)
        {
            return transform.InverseTransformPoint(
                record.instance.transform.TransformPoint(buildingPoint));
        }

        static bool TrySampleBuildingSlice(
            BuildingGeometryProfile profile,
            float localY,
            out BuildingProfileSlice slice)
        {
            slice = default;
            if (profile == null || profile.slices == null ||
                profile.slices.Length < 2 ||
                localY < profile.slices[0].y ||
                localY > profile.slices[profile.slices.Length - 1].y)
            {
                return false;
            }
            int upperIndex = 1;
            while (upperIndex < profile.slices.Length &&
                   profile.slices[upperIndex].y < localY)
            {
                upperIndex++;
            }
            if (upperIndex >= profile.slices.Length)
                return false;
            BuildingProfileSlice lower = profile.slices[upperIndex - 1];
            BuildingProfileSlice upper = profile.slices[upperIndex];
            if (!lower.valid || !upper.valid)
                return false;
            float t = Mathf.InverseLerp(lower.y, upper.y, localY);
            slice = new BuildingProfileSlice
            {
                valid = true,
                y = localY,
                minX = Mathf.Lerp(lower.minX, upper.minX, t),
                maxX = Mathf.Lerp(lower.maxX, upper.maxX, t),
                minZ = Mathf.Lerp(lower.minZ, upper.minZ, t),
                maxZ = Mathf.Lerp(lower.maxZ, upper.maxZ, t),
                coverage = Mathf.Lerp(lower.coverage, upper.coverage, t)
            };
            return true;
        }

        static bool TryResolveSliceFacade(
            BuildingProfileSlice slice,
            Vector3 direction,
            float localY,
            out Vector3 socket,
            out float halfUsableWidth)
        {
            socket = Vector3.zero;
            halfUsableWidth = 0f;
            if (!slice.valid)
                return false;
            float centerX = (slice.minX + slice.maxX) * 0.5f;
            float centerZ = (slice.minZ + slice.maxZ) * 0.5f;
            float extentX = (slice.maxX - slice.minX) * 0.5f;
            float extentZ = (slice.maxZ - slice.minZ) * 0.5f;
            float xDistance = Mathf.Abs(direction.x) > 0.0001f
                ? extentX / Mathf.Abs(direction.x)
                : float.PositiveInfinity;
            float zDistance = Mathf.Abs(direction.z) > 0.0001f
                ? extentZ / Mathf.Abs(direction.z)
                : float.PositiveInfinity;
            bool hitsXFace = xDistance <= zDistance;
            float distance = Mathf.Min(xDistance, zDistance);
            if (float.IsInfinity(distance))
                return false;
            socket = new Vector3(centerX, localY, centerZ) +
                     direction * distance;
            halfUsableWidth = hitsXFace
                ? Mathf.Min(socket.z - slice.minZ, slice.maxZ - socket.z)
                : Mathf.Min(socket.x - slice.minX, slice.maxX - socket.x);
            return halfUsableWidth > 0.01f;
        }

        float ResolveRequiredBridgeFacadeWidth(GameObject bridgePrefab)
        {
            float width = MinimumBridgeFacadeWidth;
            DarkCity2AssetDescriptor descriptor = bridgePrefab != null
                ? bridgePrefab.GetComponent<DarkCity2AssetDescriptor>()
                : null;
            if (descriptor != null)
                width = Mathf.Max(width, descriptor.AuthoredSize.x + 4f);
            if (darkCity2Catalog != null &&
                darkCity2Catalog.bridgeHeads != null)
            {
                for (int index = 0;
                     index < darkCity2Catalog.bridgeHeads.Length;
                     index++)
                {
                    GameObject head = darkCity2Catalog.bridgeHeads[index];
                    DarkCity2AssetDescriptor headDescriptor = head != null
                        ? head.GetComponent<DarkCity2AssetDescriptor>()
                        : null;
                    if (headDescriptor != null)
                    {
                        width = Mathf.Max(
                            width,
                            headDescriptor.AuthoredSize.x + 2f);
                    }
                }
            }
            return width;
        }

        static bool BridgeSocketFitsFacade(Bounds buildingBounds, float centerY)
        {
            // The bridge deck must enter real facade volume at both ends.
            // Using the shorter building's top alone is insufficient when a
            // source model has a raised origin or an unusually shallow mesh.
            return centerY >= buildingBounds.min.y + 6f &&
                   centerY <= buildingBounds.max.y - 8f;
        }

        int ResolveMaximumBridgeDegree(AirCombatBuildingLot lot)
        {
            return lot.band == AirCombatBuildingBand.Facility ||
                   lot.archetype == AirCombatBuildingArchetype.Landmark
                ? settings.skybridgeLandmarkMaximumConnections
                : settings.skybridgeMaximumConnectionsPerBuilding;
        }

        static bool IsGuaranteedDestructionBridgePair(
            GeneratedBuildingRecord first,
            GeneratedBuildingRecord second)
        {
            if (first == null || second == null ||
                first.lot == null || second.lot == null)
            {
                return false;
            }
            const string CollapsePrefix =
                "building.combat-region.collapse-candidate.";
            const string AnchorPrefix =
                "building.destruction-bridge-anchor.";
            GeneratedBuildingRecord collapse =
                first.lot.stableId.StartsWith(
                    CollapsePrefix,
                    StringComparison.Ordinal)
                    ? first
                    : second.lot.stableId.StartsWith(
                        CollapsePrefix,
                        StringComparison.Ordinal)
                        ? second
                        : null;
            GeneratedBuildingRecord anchor =
                first.lot.stableId.StartsWith(
                    AnchorPrefix,
                    StringComparison.Ordinal)
                    ? first
                    : second.lot.stableId.StartsWith(
                        AnchorPrefix,
                        StringComparison.Ordinal)
                        ? second
                        : null;
            if (collapse == null || anchor == null)
                return false;

            string collapseIndex = collapse.lot.stableId.Substring(
                CollapsePrefix.Length);
            return string.Equals(
                anchor.lot.stableId,
                AnchorPrefix + collapseIndex,
                StringComparison.Ordinal);
        }

        static void AppendGuaranteedDestructionBridgeHeights(
            System.Collections.Generic.List<float> heights,
            float commonTop)
        {
            const float MinimumHeight = 40f;
            const float FacadeTopMargin = 10f;
            const float SampleStep = 4f;
            float maximumHeight = commonTop - FacadeTopMargin;
            if (heights == null || maximumHeight < MinimumHeight)
                return;

            // The ordinary network deliberately samples only a few irregular
            // deck heights.  A guaranteed collapse-ambush pair instead probes
            // its shared facade band from a useful mid-altitude outward.  The
            // normal candidate path still rejects taper, narrow sockets,
            // unsupported spans and third-building intersections.
            float preferred = Mathf.Clamp(
                commonTop * 0.62f,
                MinimumHeight,
                maximumHeight);
            int maximumRings = Mathf.CeilToInt(
                Mathf.Max(
                    preferred - MinimumHeight,
                    maximumHeight - preferred) / SampleStep);
            for (int ring = 0; ring <= maximumRings; ring++)
            {
                if (ring == 0)
                {
                    AddDistinctBridgeHeight(heights, preferred);
                    continue;
                }
                float lower = preferred - ring * SampleStep;
                float upper = preferred + ring * SampleStep;
                if (lower >= MinimumHeight)
                    AddDistinctBridgeHeight(heights, lower);
                if (upper <= maximumHeight)
                    AddDistinctBridgeHeight(heights, upper);
            }
        }

        static void AppendBossTacticalBridgeHeights(
            System.Collections.Generic.List<float> heights,
            float commonTop,
            int pairHash)
        {
            const float MinimumHeight = 42f;
            const float FacadeTopMargin = 10f;
            const float TacticalDeckSeparation = 26f;
            float maximumHeight = commonTop - FacadeTopMargin;
            if (heights == null ||
                maximumHeight < MinimumHeight + TacticalDeckSeparation)
            {
                return;
            }

            // Boss cities need enough real two-deck pairs to fulfil their
            // guaranteed choke quota after facade/collision validation.  Only
            // a stable subset of building pairs receives these probes, keeping
            // the irregular city network and generation cost bounded.
            float lower = Mathf.Clamp(
                50f + PositiveStableModulo(pairHash / 131, 17),
                MinimumHeight,
                maximumHeight - TacticalDeckSeparation);
            AddDistinctBridgeHeight(heights, lower);
            AddDistinctBridgeHeight(
                heights,
                lower + TacticalDeckSeparation);
        }

        static void AddDistinctBridgeHeight(
            System.Collections.Generic.List<float> heights,
            float candidate)
        {
            for (int index = 0; index < heights.Count; index++)
            {
                if (Mathf.Abs(heights[index] - candidate) < 0.5f)
                    return;
            }
            heights.Add(candidate);
        }

        System.Collections.Generic.List<float>
            BuildIrregularBridgeHeights(float commonTop, int pairHash)
        {
            float minimumHeight = settings.skybridgeMinimumHeight;
            const float FacadeTopMargin = 8f;
            const float MinimumVerticalGap = 18f;
            const int MaximumLayersPerPair = 4;

            float maximumHeight = commonTop - FacadeTopMargin;
            float usableSpan = maximumHeight - minimumHeight;
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
                    minimumHeight,
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
            float current = minimumHeight + extra * bottomShare;
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
            if (!settings.useVisualDistrictThemes)
                return DarkCity2DistrictKind.Mixed;
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
            UrbanCityVisualContinuityBuilder.Build(
                buildingRoot,
                plan,
                settings,
                palette,
                buildingCatalog,
                darkCity2Catalog);
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
                "CentralManeuverDistrict_完整机动街区直径_" +
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

        void BuildCombatRegionEnvironmentalCues()
        {
            for (int i = 0; i < plan.opportunities.Count; i++)
            {
                TacticalOpportunity opportunity = plan.opportunities[i];
                if (opportunity == null)
                    continue;
                Material material = MaterialForOpportunity(opportunity.kind);
                Vector3[] entrances = opportunity.entrances ?? Array.Empty<Vector3>();
                for (int point = 0; point < entrances.Length; point++)
                {
                    Vector3 position = entrances[point];
                    position.y = CueAltitude(opportunity, position.y);
                    Vector3 direction = opportunity.bounds.center - position;
                    direction.y = 0f;
                    CreateCombatCueStrip(
                        "战术入口灯带_" + CityOpportunityName(opportunity.kind) +
                        "_" + point.ToString("D2"),
                        position,
                        direction,
                        new Vector3(4f, 0.8f, 14f),
                        material);
                }

                if (opportunity.kind == TacticalOpportunityKind.KiteLoop)
                {
                    const int Segments = 12;
                    for (int segment = 0; segment < Segments; segment++)
                    {
                        float angle = segment / (float)Segments * Mathf.PI * 2f;
                        Vector3 radial = new Vector3(
                            Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                        Vector3 tangent = new Vector3(-radial.z, 0f, radial.x);
                        Vector3 position = opportunity.bounds.center +
                                           radial * opportunity.loopRadius;
                        position.y = 4f;
                        CreateCombatCueStrip(
                            "环绕街区连续转向灯带_" + segment.ToString("D2"),
                            position,
                            tangent,
                            new Vector3(3f, 0.7f, 18f),
                            material);
                    }
                }
            }

            for (int i = 0; i < plan.buildings.Count; i++)
            {
                AirCombatBuildingLot building = plan.buildings[i];
                if (building.clusterId != 1202 && building.clusterId != 1203)
                    continue;
                bool collapseCandidate = building.clusterId == 1203;
                Material material = collapseCandidate
                    ? MaterialForOpportunity(TacticalOpportunityKind.DestructionAmbush)
                    : MaterialForOpportunity(TacticalOpportunityKind.OcclusionChain);
                Vector3 position = new Vector3(
                    building.center.x,
                    Mathf.Min(building.size.y * 0.38f, 58f),
                    building.center.z);
                CreateCombatCueStrip(
                    collapseCandidate
                        ? "可切割倒塌楼_结构弱点灯带_" + i.ToString("D3")
                        : "连续掩体楼_边缘灯带_" + i.ToString("D3"),
                    position,
                    collapseCandidate ? Vector3.right : Vector3.forward,
                    new Vector3(2.2f,
                        collapseCandidate ? 28f : 18f,
                        collapseCandidate ? 7f : 4f),
                    material);
            }
        }

        void CreateCombatCueStrip(
            string objectName,
            Vector3 position,
            Vector3 forward,
            Vector3 scale,
            Material material)
        {
            GameObject strip = CreatePrimitive(
                PrimitiveType.Cube,
                decorationRoot,
                objectName,
                false);
            strip.transform.localPosition = position;
            if (forward.sqrMagnitude > 0.001f)
                strip.transform.localRotation = Quaternion.LookRotation(
                    forward.normalized,
                    Vector3.up);
            strip.transform.localScale = scale;
            AssignMaterial(strip, material);
        }

        static float CueAltitude(
            TacticalOpportunity opportunity,
            float sourceAltitude)
        {
            switch (opportunity.kind)
            {
                case TacticalOpportunityKind.AttackPerch:
                    return opportunity.bounds.max.y + 1.5f;
                case TacticalOpportunityKind.ExposureShortcut:
                    return Mathf.Max(12f, sourceAltitude);
                case TacticalOpportunityKind.VerticalEscape:
                    return Mathf.Max(5f, sourceAltitude);
                default:
                    return 3.5f;
            }
        }

        static float OpportunityDisplayAltitude(TacticalOpportunity opportunity)
        {
            switch (opportunity.kind)
            {
                case TacticalOpportunityKind.RecoveryPocket:
                case TacticalOpportunityKind.DestructionAmbush:
                case TacticalOpportunityKind.TacticalChoke:
                    return Mathf.Max(5f, opportunity.bounds.min.y + 5f);
                case TacticalOpportunityKind.AttackPerch:
                    return opportunity.bounds.max.y + 2f;
                default:
                    return opportunity.bounds.center.y;
            }
        }

        static string CityOpportunityName(TacticalOpportunityKind kind)
        {
            switch (kind)
            {
                case TacticalOpportunityKind.ManeuverBowl:
                    return "中央机动街区";
                case TacticalOpportunityKind.OcclusionChain:
                    return "少道路高楼掩体区";
                case TacticalOpportunityKind.ExposureShortcut:
                    return "开放火力捷径";
                case TacticalOpportunityKind.RecoveryPocket:
                    return "维修庭院";
                case TacticalOpportunityKind.KiteLoop:
                    return "环绕街区";
                case TacticalOpportunityKind.TacticalChoke:
                    return "战术连廊窄口";
                case TacticalOpportunityKind.VerticalEscape:
                    return "低中空换层通道";
                case TacticalOpportunityKind.AttackPerch:
                    return "有掩体攻击平台";
                case TacticalOpportunityKind.DestructionAmbush:
                    return "连廊倒塌伏击区";
                default:
                    return "战斗街区";
            }
        }

        Material MaterialForOpportunity(TacticalOpportunityKind kind)
        {
            switch (kind)
            {
                case TacticalOpportunityKind.OcclusionChain:
                    return palette.occlusionVolume;
                case TacticalOpportunityKind.ExposureShortcut:
                case TacticalOpportunityKind.DestructionAmbush:
                    return palette.exposureVolume;
                case TacticalOpportunityKind.RecoveryPocket:
                    return palette.recoveryVolume;
                case TacticalOpportunityKind.AttackPerch:
                    return palette.longRangeRoute;
                case TacticalOpportunityKind.KiteLoop:
                    return palette.maneuverVolume;
                case TacticalOpportunityKind.TacticalChoke:
                    return palette.objective;
                default:
                    return palette.mainRoute;
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

        void HideGeneratedDebugPresentation()
        {
            // These legacy GameObjects exist only as hierarchy diagnostics.
            // SceneView visualization is drawn by the editor Handles overlay,
            // so keeping renderers active would leak thick routes, spheres and
            // validation rings into Game cameras when entering Play directly
            // from an authored city scene or rebuilding through the designer.
            SetGeneratedLayerActive("01_", false);
            SetGeneratedLayerActive("02_", false);
            SetGeneratedLayerActive("05_", false);
            SetGeneratedLayerActive("06_", false);
        }

        void ClearGenerated()
        {
            runtimeGeometrySnapshot = null;
            runtimeBuiltBridgeSegments.Clear();
            runtimeBuiltCableSegments.Clear();
            Transform generated = transform.Find(GeneratedRootName);
            if (generated != null)
            {
                if (Application.isPlaying)
                    Destroy(generated.gameObject);
                else
                    DestroyImmediate(generated.gameObject);
            }
            DestroyWindPreviewMaterial(ref windPreviewRibbonMaterial);
            DestroyWindPreviewMaterial(ref windPreviewArrowMaterial);
        }

        static void DestroyWindPreviewMaterial(ref Material material)
        {
            if (material == null)
                return;
            if (Application.isPlaying)
                Destroy(material);
            else
                DestroyImmediate(material);
            material = null;
        }

        static void MarkEditorPreviewHierarchyTransient(Transform root)
        {
            if (root == null)
                return;
            root.gameObject.hideFlags |= HideFlags.DontSaveInEditor;
            for (int childIndex = 0; childIndex < root.childCount; childIndex++)
            {
                MarkEditorPreviewHierarchyTransient(root.GetChild(childIndex));
            }
        }

        sealed class GeneratedBuildingRecord
        {
            public AirCombatBuildingLot lot;
            public GameObject instance;
            public GameObject sourcePrefab;
            public UrbanDestructibleBuilding destructible;
            public Bounds actualBounds;
            public bool actualBoundsReady;
        }

        sealed class RoadIntersectionPlan
        {
            public Vector3 point;
            public float verticalWidth;
            public float horizontalWidth;
        }

        sealed class BuildingGeometryProfile
        {
            public Vector3 authoredSize;
            public BuildingProfileSlice[] slices;
            public bool hasReadableGeometry;
            public float maximumBodyCoverage;
            public float groundSupportRatio = 1f;
        }

        struct BuildingProfileSlice
        {
            public bool valid;
            public float y;
            public float minX;
            public float maxX;
            public float minZ;
            public float maxZ;
            public float coverage;
        }

        struct ProfileSliceSegment
        {
            public Vector2 a;
            public Vector2 b;
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
            public int segmentCount = 1;
            public int layerIndex;
            public int layerCount;
            public bool crossesRoadBlock;
            public bool destructionAmbush;
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
                "青=主航路　绿=遮挡侧翼　黄=远射航路");
            GUILayout.EndArea();
        }

        void OnDrawGizmos()
        {
#if UNITY_EDITOR
            // Debug regions belong to the Scene view. The generated semantic
            // GameObjects remain disabled in formal missions, and the Game view
            // must stay free of designer-only outlines even when Gizmos is on.
            if (Camera.current != null &&
                Camera.current.cameraType != CameraType.SceneView)
            {
                return;
            }
#else
            return;
#endif
            if (!showSemanticGizmos || plan == null)
                return;
            Gizmos.matrix = transform.localToWorldMatrix;
        }

        static void DrawOpportunityGizmo(TacticalOpportunity opportunity)
        {
            float y = OpportunityDisplayAltitude(opportunity);
            if (opportunity.kind == TacticalOpportunityKind.KiteLoop)
            {
                const int Segments = 32;
                float radius = Mathf.Max(
                    opportunity.loopRadius,
                    Mathf.Min(opportunity.bounds.extents.x,
                        opportunity.bounds.extents.z));
                Vector3 previous = new Vector3(
                    opportunity.bounds.center.x + radius,
                    y,
                    opportunity.bounds.center.z);
                for (int i = 1; i <= Segments; i++)
                {
                    float angle = i / (float)Segments * Mathf.PI * 2f;
                    Vector3 next = new Vector3(
                        opportunity.bounds.center.x + Mathf.Cos(angle) * radius,
                        y,
                        opportunity.bounds.center.z + Mathf.Sin(angle) * radius);
                    Gizmos.DrawLine(previous, next);
                    previous = next;
                }
            }
            else
            {
                Bounds bounds = opportunity.bounds;
                Vector3 a = new Vector3(bounds.min.x, y, bounds.min.z);
                Vector3 b = new Vector3(bounds.min.x, y, bounds.max.z);
                Vector3 c = new Vector3(bounds.max.x, y, bounds.max.z);
                Vector3 d = new Vector3(bounds.max.x, y, bounds.min.z);
                Gizmos.DrawLine(a, b);
                Gizmos.DrawLine(b, c);
                Gizmos.DrawLine(c, d);
                Gizmos.DrawLine(d, a);
            }

            Vector3[] entrances = opportunity.entrances ?? Array.Empty<Vector3>();
            Vector3[] exits = opportunity.exits ?? Array.Empty<Vector3>();
            for (int i = 0; i < entrances.Length; i++)
                Gizmos.DrawSphere(new Vector3(entrances[i].x, y, entrances[i].z), 4f);
            for (int i = 0; i < exits.Length; i++)
                Gizmos.DrawWireSphere(new Vector3(exits[i].x, y, exits[i].z), 5f);
        }

        static Color ColorForOpportunity(TacticalOpportunityKind kind)
        {
            switch (kind)
            {
                case TacticalOpportunityKind.OcclusionChain:
                    return new Color(0.15f, 1f, 0.35f, 0.9f);
                case TacticalOpportunityKind.ExposureShortcut:
                    return new Color(1f, 0.7f, 0.05f, 0.9f);
                case TacticalOpportunityKind.RecoveryPocket:
                    return new Color(0.05f, 0.9f, 1f, 0.9f);
                case TacticalOpportunityKind.KiteLoop:
                    return new Color(0.65f, 0.25f, 1f, 0.92f);
                case TacticalOpportunityKind.AttackPerch:
                    return new Color(1f, 0.92f, 0.12f, 0.95f);
                case TacticalOpportunityKind.DestructionAmbush:
                    return new Color(1f, 0.22f, 0.06f, 0.95f);
                case TacticalOpportunityKind.TacticalChoke:
                    return new Color(1f, 0.48f, 0.08f, 0.95f);
                case TacticalOpportunityKind.VerticalEscape:
                    return new Color(0.2f, 0.68f, 1f, 0.9f);
                default:
                    return new Color(0.35f, 1f, 0.55f, 0.9f);
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
