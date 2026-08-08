using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityPlanet.CityPcg
{
    public enum AirCombatCityMission
    {
        Clearance = 0,
        FacilityAssault = 1,
        BossEncounter = 2
    }

    public enum AirCombatSkybridgeRole
    {
        Ordinary = 0,
        TacticalLower = 1,
        TacticalUpper = 2,
        DestructionAmbush = 3
    }

    [DisallowMultipleComponent]
    public sealed class AirCombatTacticalSkybridge : MonoBehaviour
    {
        [SerializeField] string groupId = string.Empty;
        [SerializeField] AirCombatSkybridgeRole role;
        [SerializeField] float clearHeight;
        [SerializeField] float clearWidth;
        [SerializeField] int difficultyTier;

        public string GroupId => groupId;
        public AirCombatSkybridgeRole Role => role;
        public float ClearHeight => clearHeight;
        public float ClearWidth => clearWidth;
        public int DifficultyTier => difficultyTier;

        public void Configure(
            string stableGroupId,
            AirCombatSkybridgeRole targetRole,
            float measuredClearHeight,
            float measuredClearWidth,
            int targetDifficultyTier)
        {
            groupId = stableGroupId ?? string.Empty;
            role = targetRole;
            clearHeight = Mathf.Max(0f, measuredClearHeight);
            clearWidth = Mathf.Max(0f, measuredClearWidth);
            difficultyTier = Mathf.Clamp(targetDifficultyTier, 0, 5);
        }
    }

    public enum AirCombatRouteKind
    {
        Main = 0,
        MaskedFlank = 1,
        LongRange = 2,
        EnemyIngress = 3,
        KiteLoop = 4,
        VerticalEscape = 5
    }

    public enum AirCombatVolumeKind
    {
        SpawnBasin = 0,
        ManeuverBowl = 1,
        OcclusionGate = 2,
        ExposureLane = 3,
        RecoveryPocket = 4,
        KiteLoop = 5,
        AssaultBreach = 6,
        DominancePerch = 7,
        DangerPlaza = 8
    }

    public enum AirCombatEnemyLaneKind
    {
        Suicide = 0,
        Ranged = 1
    }

    public enum AirCombatBuildingBand
    {
        Low = 0,
        Medium = 1,
        High = 2,
        Facility = 3
    }

    public enum AirCombatBuildingArchetype
    {
        LowBlock = 0,
        MidSlab = 1,
        CombatTower = 2,
        Landmark = 3,
        Facility = 4
    }

    public enum CombatCityBlockRole
    {
        Maneuver = 0,
        Occlusion = 1,
        Exposure = 2,
        Recovery = 3,
        Kite = 4,
        TacticalChoke = 5,
        Vertical = 6,
        Attack = 7,
        Destruction = 8,
        CombatBoundary = 9
    }

    [Serializable]
    public sealed class AirCombatCitySettings
    {
        [Header("确定性")]
        [InspectorName("基础 Seed")]
        public int seed = 7319;

        [InspectorName("任务模式")]
        public AirCombatCityMission mission = AirCombatCityMission.Clearance;

        [Header("正式空战设计包线")]
        [InspectorName("地图边长（米）")]
        [Range(1200f, 2600f)]
        public float mapSize = 1664f;

        [InspectorName("设计交战速度（米/秒）")]
        [Range(20f, 160f)]
        public float combatSpeed = 55f;

        [InspectorName("设计转弯半径（米）")]
        [Range(40f, 300f)]
        public float turnRadius = 95f;

        [InspectorName("武器有效射程（米）")]
        [Range(120f, 1200f)]
        public float weaponRange = 480f;

        [InspectorName("参考翼展（米）")]
        [Range(4f, 80f)]
        public float wingspan = 18f;

        [Header("三层空域")]
        [InspectorName("低空层（米）")]
        [Range(30f, 120f)]
        public float lowAltitude = 70f;

        [InspectorName("中空层（米）")]
        [Range(70f, 220f)]
        public float mediumAltitude = 135f;

        [InspectorName("高空层（米）")]
        [Range(120f, 300f)]
        public float highAltitude = 220f;

        [InspectorName("最高战斗高度（米）")]
        [Range(180f, 400f)]
        public float maximumAltitude = 350f;

        [Header("城市密度")]
        [InspectorName("建筑采样间距（米）")]
        [Range(48f, 96f)]
        public float buildingSpacing = 63.6f;

        [InspectorName("建筑密度")]
        [Range(0.5f, 0.95f)]
        public float buildingDensity = 0.91f;

        [InspectorName("最多候选次数")]
        [Range(1, 24)]
        public int maximumAttempts = 10;

        [Header("Combat-driven PCG")]
        public bool useVisualDistrictThemes = true;
        public CombatCityDifficultyProfile combatDifficulty =
            new CombatCityDifficultyProfile();
        public CombatCityDifficultyProfile Difficulty =>
            combatDifficulty ?? (combatDifficulty =
                new CombatCityDifficultyProfile());

        public float MainCorridorWidth => Mathf.Max(
            152f,
            turnRadius * 1.52f + wingspan * 0.4f) *
            Mathf.Lerp(1.10f, 0.92f, Difficulty.navigationChallenge);

        public float FlankCorridorWidth => Mathf.Max(
            118f,
            turnRadius * 1.22f + wingspan * 0.25f) *
            Mathf.Lerp(1.08f, 0.86f, Difficulty.navigationChallenge);

        public float LongRangeCorridorWidth => Mathf.Max(
            136f,
            turnRadius * 1.38f + wingspan * 0.25f) *
            Mathf.Lerp(1.12f, 0.88f, Difficulty.navigationChallenge);

        public float ManeuverDiameter => Mathf.Max(350f, turnRadius * 3.55f) *
            Mathf.Lerp(1.10f, 0.92f, Difficulty.navigationChallenge);

        public float RecoveryDiameter => Mathf.Max(200f, turnRadius * 2.1f) *
            Mathf.Lerp(0.92f, 1.16f, Difficulty.recoveryGenerosity);

        // `wingspan` is the gameplay envelope used by the city planner rather
        // than the bare renderer width.  A few metres of lateral allowance are
        // still required because arcade steering yaws and drifts while the
        // player lines up an alley.  Ordinary building gaps must therefore be
        // wider than the reference hull instead of merely avoiding overlap.
        public float MinimumDefaultPresetBuildingGap => Mathf.Max(
            14f,
            wingspan + 4f);

        public AirCombatCitySettings ValidatedCopy()
        {
            var copy = new AirCombatCitySettings
            {
                seed = seed,
                mission = mission,
                mapSize = Mathf.Clamp(mapSize, 1200f, 2600f),
                combatSpeed = Mathf.Clamp(combatSpeed, 20f, 160f),
                turnRadius = Mathf.Clamp(turnRadius, 40f, 300f),
                weaponRange = Mathf.Clamp(weaponRange, 120f, 1200f),
                wingspan = Mathf.Clamp(wingspan, 4f, 80f),
                lowAltitude = Mathf.Clamp(lowAltitude, 30f, 120f),
                mediumAltitude = Mathf.Clamp(mediumAltitude, 70f, 220f),
                highAltitude = Mathf.Clamp(highAltitude, 120f, 300f),
                maximumAltitude = Mathf.Clamp(maximumAltitude, 180f, 400f),
                buildingSpacing = Mathf.Clamp(buildingSpacing, 48f, 96f),
                buildingDensity = Mathf.Clamp(buildingDensity, 0.5f, 0.95f),
                maximumAttempts = Mathf.Clamp(maximumAttempts, 1, 24),
                useVisualDistrictThemes = useVisualDistrictThemes,
                combatDifficulty = (combatDifficulty ??
                    new CombatCityDifficultyProfile()).ValidatedCopy()
            };
            copy.mediumAltitude = Mathf.Max(
                copy.lowAltitude + 30f,
                copy.mediumAltitude);
            copy.highAltitude = Mathf.Max(
                copy.mediumAltitude + 30f,
                copy.highAltitude);
            copy.maximumAltitude = Mathf.Max(
                copy.highAltitude + 40f,
                copy.maximumAltitude);
            float minimumMap = Mathf.Max(
                1200f,
                copy.turnRadius * 12f,
                copy.weaponRange * 3.2f);
            copy.mapSize = Mathf.Clamp(
                Mathf.Max(copy.mapSize, minimumMap),
                1200f,
                2600f);
            return copy;
        }
    }

    [Serializable]
    public sealed class AirCombatRoadStrip
    {
        public string stableId = string.Empty;
        public Vector3 start;
        public Vector3 end;
        public float width;
        public AirCombatRouteKind kind;
        public int laneTiles = 1;
        public bool dangerLane;
    }

    [Serializable]
    public sealed class CombatCityBlockPlan
    {
        public string stableId = string.Empty;
        public int gridX;
        public int gridZ;
        public Bounds bounds;
        public CombatCityBlockRole role;
        public string primaryOpportunityId = string.Empty;
        public string tacticalRegionId = string.Empty;
        public string mergedGroupId = string.Empty;
        public float roadWidthScale = 1f;
        public float buildingDensityScale = 1f;
        public float buildingHeightScale = 1f;
        public bool mergeEast;
        public bool mergeNorth;
    }

    [Serializable]
    public sealed class AirCombatFlightRoute
    {
        public string stableId = string.Empty;
        public AirCombatRouteKind kind;
        public float width;
        public Vector3[] points = Array.Empty<Vector3>();
    }

    [Serializable]
    public sealed class AirCombatTacticalVolume
    {
        public string stableId = string.Empty;
        public AirCombatVolumeKind kind;
        public Vector3 center;
        public Vector3 size;
    }

    [Serializable]
    public sealed class AirCombatBuildingLot
    {
        public string stableId = string.Empty;
        public Vector3 center;
        public Vector3 size;
        /// <summary>标准化预制体的 +Z 正面绕 +Y 旋转到临街方向。</summary>
        public float yaw;
        public AirCombatBuildingBand band;
        public AirCombatBuildingArchetype archetype;
        public int clusterId;
        public int visualVariant;
    }

    [Serializable]
    public sealed class AirCombatBoundaryWallPlan
    {
        public string stableId = string.Empty;
        public Vector3 center;
        public Vector3 size;
    }

    [Serializable]
    public sealed class AirCombatEnemyIngress
    {
        public string stableId = string.Empty;
        public Vector3 position;
        public Vector3 target;
        public AirCombatEnemyLaneKind kind;
        public float warningSeconds;
    }

    public sealed class AirCombatCityPlan
    {
        public int requestedSeed;
        public int resolvedSeed;
        public AirCombatCityMission mission;
        public float mapSize;
        public Vector3 playerSpawn;
        public Vector3 objective;
        public readonly List<AirCombatRoadStrip> roads =
            new List<AirCombatRoadStrip>(16);
        public readonly List<AirCombatFlightRoute> routes =
            new List<AirCombatFlightRoute>(16);
        public readonly List<AirCombatTacticalVolume> volumes =
            new List<AirCombatTacticalVolume>(16);
        public readonly List<AirCombatBuildingLot> buildings =
            new List<AirCombatBuildingLot>(512);
        public readonly List<AirCombatBoundaryWallPlan> boundaryWalls =
            new List<AirCombatBoundaryWallPlan>(4);
        public readonly List<AirCombatEnemyIngress> ingresses =
            new List<AirCombatEnemyIngress>(8);
        public readonly List<Vector3> facilityCores =
            new List<Vector3>(3);
        public readonly List<TacticalOpportunity> opportunities =
            new List<TacticalOpportunity>(16);
        public readonly List<CombatCityBlockPlan> tacticalBlocks =
            new List<CombatCityBlockPlan>(64);
    }

    [Serializable]
    public sealed class AirCombatCityReport
    {
        public bool valid;
        public int requestedSeed;
        public int resolvedSeed;
        public int attempts;
        public int buildingCount;
        public int mediumBuildingCount;
        public int highBuildingCount;
        public int routeCount;
        public int ingressCount;
        public bool routesClear;
        public bool alternateRouteAvailable;
        public bool threeAltitudeLayersUseful;
        public bool heightMixDistributed;
        public bool centralHeightMixValid;
        public bool highAltitudeBypassControlled;
        public int skylineAnchorQuadrants;
        public float maximumTowerHeight;
        public float verticalOverflightGap;
        public bool facilityReachable;
        public bool roadGridAligned;
        public int roadIntersectionCount;
        public int buildingRoadOverlapCount;
        public float maximumCentralCoverGap;
        public bool coverContinuityValid;
        public int recoveryDistrictCount;
        public bool tacticalRolesComplete;
        public float minimumTurnRadius;
        public float firstContactSeconds;
        public float mainCorridorWidth;
        public int checksum;
        public int skybridgeCount;
        public int crossRoadSkybridgeCount;
        public int tacticalSkybridgeGroupCount;
        public float minimumTacticalClearHeight;
        public float minimumTacticalClearWidth;
        public bool skybridgeNetworkValid;
        public int tacticalOpportunityCount;
        public int exposureShortcutCount;
        public int recoveryOpportunityCount;
        public int kiteLoopOpportunityCount;
        public int minimumTacticalChoices;
        public float longestExposureSeconds;
        public float nearestRecoverySeconds;
        public bool tacticalOpportunityNetworkValid;
        public bool dominantRouteDetected;
        public float exposureShortcutSavingRatio;
        public int occlusionBreakCount;
        public int kiteLoopObstructionCount;
        public int destructionAmbushFeatureCount;
        public int destructionAmbushBridgeCount;
        public int physicalAttackPerchCount;
        public int coveredAttackPerchCount;
        public int occlusionBoundaryTowerCount;
        public int combatBoundaryTowerCount;
        public int combatBoundaryAirWallCount;
        public int recoveryPocketOutputBlockedCount;
        public int occlusionMergedRoadSegments;
        public bool verticalEscapePhysical;
        public bool combatRegionsPhysical;
        public int boundTacticalChokeCount;
        public int tacticalBlockCount;
        public int tacticalRegionCount;
        public int unassignedTacticalBlockCount;
        public int mergedBlockGroupCount;
        public int removedInternalRoadSegments;
        public float minimumRoadWidth;
        public float maximumRoadWidth;
        public bool roadWidthsVaried;
        public bool tacticalBlockCoverageValid;
        public string failureReason = string.Empty;

        public string Summary =>
            (valid ? "通过" : "失败")
            + " | Seed " + resolvedSeed
            + " | 建筑 " + buildingCount
            + " | 高遮挡 " + highBuildingCount
            + " | 航路 " + routeCount
            + " | 战术街区 " + tacticalBlockCount
            + " | 合并道路 " + removedInternalRoadSegments
            + " | 路宽 " + minimumRoadWidth.ToString("0") + "-" +
              maximumRoadWidth.ToString("0") + "m"
            + " | 最小转弯 " + minimumTurnRadius.ToString("0") + "m"
            + " | 校验码 " + checksum;
    }

    /// <summary>
    /// Dependency-free air-combat city PCG. Playable airspace is authored
    /// first; roads and buildings are fitted around it afterwards.
    /// </summary>
    public static class AirCombatCityGenerator
    {
        struct StableRandom
        {
            uint state;

            public StableRandom(int seed)
            {
                state = (uint)seed;
                if (state == 0u)
                    state = 0x9E3779B9u;
            }

            public uint NextUInt()
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                return state;
            }

            public float Value()
            {
                return (NextUInt() & 0x00FFFFFFu) / 16777216f;
            }

            public float Range(float minimum, float maximum)
            {
                return Mathf.Lerp(minimum, maximum, Value());
            }
        }

        public static AirCombatCityPlan Generate(
            AirCombatCitySettings source,
            out AirCombatCityReport report)
        {
            AirCombatCitySettings settings =
                (source ?? new AirCombatCitySettings()).ValidatedCopy();
            AirCombatCityPlan lastPlan = null;
            AirCombatCityReport lastReport = null;
            for (int attempt = 0;
                 attempt < settings.maximumAttempts;
                 attempt++)
            {
                int resolvedSeed = DeriveSeed(settings.seed, attempt);
                var random = new StableRandom(resolvedSeed);
                AirCombatCityPlan plan = BuildCandidate(
                    settings,
                    resolvedSeed,
                    ref random);
                AirCombatCityReport candidate = Validate(
                    settings,
                    plan,
                    attempt + 1);
                lastPlan = plan;
                lastReport = candidate;
                if (candidate.valid)
                {
                    report = candidate;
                    return plan;
                }
            }

            report = lastReport ?? new AirCombatCityReport
            {
                requestedSeed = settings.seed,
                resolvedSeed = settings.seed,
                attempts = settings.maximumAttempts,
                failureReason = "没有生成候选布局。"
            };
            return lastPlan;
        }

        static AirCombatCityPlan BuildCandidate(
            AirCombatCitySettings settings,
            int resolvedSeed,
            ref StableRandom random)
        {
            var plan = new AirCombatCityPlan
            {
                requestedSeed = settings.seed,
                resolvedSeed = resolvedSeed,
                mission = settings.mission,
                mapSize = settings.mapSize
            };

            BuildTacticalSpace(settings, plan);
            CombatDrivenCityPcgPlanner.Populate(settings, plan);
            CombatDrivenCityPcgPlanner.BuildTacticalBlockLayout(settings, plan);
            BuildRoadNetwork(settings, plan);
            BuildEnemyIngresses(settings, plan, ref random);
            BuildBuildings(settings, plan, ref random);
            CombatDrivenCityPcgPlanner.BuildPhysicalRegionFeatures(
                settings,
                plan);
            BuildLowUrbanIslands(settings, plan, ref random);
            BuildRecoveryDistricts(settings, plan, ref random);
            BuildTacticalPark(settings, plan);
            EnsureCentralTacticalCover(settings, plan);
            EnsureCentralLowCover(settings, plan);
            PromoteCentralMediumCover(settings, plan, ref random);
            EnsureCentralCoverContinuity(settings, plan);
            PromoteSkylineAnchors(settings, plan);
            if (settings.mission == AirCombatCityMission.FacilityAssault)
                BuildFacility(settings, plan);
            CombatDrivenCityPcgPlanner.BindGeneratedFeatures(settings, plan);
            return plan;
        }

        static float ResolveStreetPitch(AirCombatCitySettings settings)
        {
            return settings.buildingSpacing * 3f;
        }

        static void BuildTacticalSpace(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan)
        {
            float half = settings.mapSize * 0.5f;
            float streetPitch = ResolveStreetPitch(settings);
            float sideX = streetPitch * 2f;
            float connectorZ = streetPitch * 3f;
            float upperCombatAltitude = Mathf.Lerp(
                settings.mediumAltitude,
                settings.highAltitude,
                0.35f);
            float maskedDetour = Mathf.Lerp(
                1.45f,
                1.85f,
                settings.Difficulty.navigationChallenge);
            float maskedX = -sideX * maskedDetour;
            float exposureX = sideX * 0.20f;
            Vector3 kiteCenter = new Vector3(
                streetPitch * 1.50f,
                settings.mediumAltitude,
                streetPitch * 0.50f);
            // Recovery pockets live in the outer parcels, not beside the kite
            // loop.  The previous mirrored placement put the eastern pocket's
            // tall back wall directly through the loop and its north wall into
            // a 94 m avenue.  Because that relationship was seed-independent,
            // retrying seeds could never produce a valid city.
            float westRecoveryX = -streetPitch * 2.5f;
            float eastRecoveryX = streetPitch * 3.5f;
            float recoveryZ = streetPitch * 0.62f;
            Vector3 recoveryPocketSize = new Vector3(
                streetPitch * 0.67f * Mathf.Lerp(
                    0.88f, 1.14f, settings.Difficulty.recoveryGenerosity),
                settings.maximumAltitude * 0.72f,
                streetPitch * 0.62f * Mathf.Lerp(
                    0.88f, 1.14f, settings.Difficulty.recoveryGenerosity));
            Vector3 westRecovery = new Vector3(
                westRecoveryX,
                settings.mediumAltitude,
                recoveryZ);
            Vector3 eastRecovery = new Vector3(
                eastRecoveryX,
                settings.mediumAltitude,
                -recoveryZ);
            bool clearanceStyle = settings.mission !=
                                  AirCombatCityMission.FacilityAssault;
            plan.playerSpawn = clearanceStyle
                ? new Vector3(0f, settings.lowAltitude, 0f)
                : new Vector3(0f, settings.lowAltitude, -half + 160f);
            plan.objective = clearanceStyle
                ? new Vector3(0f, settings.mediumAltitude, 0f)
                : new Vector3(0f, settings.mediumAltitude, half - 180f);

            AddVolume(
                plan,
                "volume.spawn",
                AirCombatVolumeKind.SpawnBasin,
                plan.playerSpawn,
                new Vector3(
                    settings.RecoveryDiameter,
                    settings.maximumAltitude,
                    settings.RecoveryDiameter));
            AddVolume(
                plan,
                "volume.center",
                AirCombatVolumeKind.ManeuverBowl,
                new Vector3(0f, settings.mediumAltitude, 0f),
                new Vector3(
                    settings.ManeuverDiameter,
                    settings.maximumAltitude,
                    settings.ManeuverDiameter));
            AddVolume(
                plan,
                "volume.recovery.west",
                AirCombatVolumeKind.RecoveryPocket,
                westRecovery,
                recoveryPocketSize);
            AddVolume(
                plan,
                "volume.recovery.east",
                AirCombatVolumeKind.RecoveryPocket,
                eastRecovery,
                recoveryPocketSize);
            AddVolume(
                plan,
                "volume.kite-loop.center",
                AirCombatVolumeKind.KiteLoop,
                kiteCenter,
                new Vector3(
                    settings.turnRadius * Mathf.Lerp(
                        3.30f, 2.72f, settings.Difficulty.navigationChallenge),
                    settings.maximumAltitude * 0.82f,
                    settings.turnRadius * Mathf.Lerp(
                        3.30f, 2.72f, settings.Difficulty.navigationChallenge)));
            AddVolume(
                plan,
                "volume.assault-breach.main",
                AirCombatVolumeKind.AssaultBreach,
                new Vector3(0f, settings.mediumAltitude, 0f),
                new Vector3(
                    66.78f,
                    settings.maximumAltitude,
                    streetPitch * 4.2f));
            AddVolume(
                plan,
                "volume.mask.gate.south",
                AirCombatVolumeKind.OcclusionGate,
                new Vector3(maskedX, settings.mediumAltitude, -connectorZ * 0.55f),
                new Vector3(
                    settings.FlankCorridorWidth,
                    settings.maximumAltitude * 0.75f,
                    settings.combatSpeed * 3.2f));
            AddVolume(
                plan,
                "volume.mask.gate.north",
                AirCombatVolumeKind.OcclusionGate,
                new Vector3(maskedX, settings.mediumAltitude, connectorZ * 0.55f),
                new Vector3(
                    settings.FlankCorridorWidth,
                    settings.maximumAltitude * 0.75f,
                    settings.combatSpeed * 3.2f));
            AddVolume(
                plan,
                "volume.exposure.east",
                AirCombatVolumeKind.ExposureLane,
                new Vector3(exposureX, upperCombatAltitude, 0f),
                new Vector3(
                    settings.LongRangeCorridorWidth,
                    settings.maximumAltitude,
                    settings.combatSpeed * Mathf.Lerp(
                        6.2f, 9f, settings.Difficulty.exposurePressure)));

            Vector3 south = new Vector3(0f, settings.lowAltitude, -half + 90f);
            Vector3 north = new Vector3(0f, settings.mediumAltitude, half - 90f);
            AddRoute(
                plan,
                "route.main",
                AirCombatRouteKind.Main,
                settings.MainCorridorWidth,
                south,
                new Vector3(0f, settings.lowAltitude, -connectorZ),
                new Vector3(0f, settings.mediumAltitude, 0f),
                new Vector3(0f, settings.mediumAltitude, connectorZ),
                north);
            AddRoute(
                plan,
                "route.masked-flank",
                AirCombatRouteKind.MaskedFlank,
                settings.FlankCorridorWidth,
                south,
                new Vector3(maskedX * 0.52f, settings.lowAltitude, -connectorZ),
                new Vector3(maskedX, settings.mediumAltitude, -connectorZ * 0.55f),
                new Vector3(maskedX, settings.mediumAltitude, connectorZ * 0.55f),
                new Vector3(maskedX * 0.52f, settings.mediumAltitude, connectorZ),
                north);
            AddRoute(
                plan,
                "route.long-range",
                AirCombatRouteKind.LongRange,
                settings.LongRangeCorridorWidth,
                south,
                new Vector3(exposureX * 0.52f, settings.mediumAltitude, -connectorZ),
                new Vector3(exposureX, upperCombatAltitude, -connectorZ * 0.55f),
                new Vector3(exposureX, upperCombatAltitude, connectorZ * 0.55f),
                new Vector3(exposureX * 0.52f, settings.mediumAltitude, connectorZ),
                north);

            float loopRadius = settings.turnRadius * Mathf.Lerp(
                1.55f,
                1.22f,
                settings.Difficulty.navigationChallenge);
            var loopPoints = new Vector3[13];
            for (int point = 0; point < loopPoints.Length; point++)
            {
                float angle = point / 12f * Mathf.PI * 2f;
                loopPoints[point] = kiteCenter + new Vector3(
                    Mathf.Cos(angle) * loopRadius,
                    0f,
                    Mathf.Sin(angle) * loopRadius);
            }
            AddRoute(
                plan,
                "route.kite-loop.city-block",
                AirCombatRouteKind.KiteLoop,
                Mathf.Max(58f, settings.wingspan * 2f + 14f),
                loopPoints);
            AddRoute(
                plan,
                "route.vertical-escape.central-avenue",
                AirCombatRouteKind.VerticalEscape,
                settings.MainCorridorWidth * 0.58f,
                new Vector3(0f, settings.lowAltitude, -connectorZ * 0.35f),
                new Vector3(0f, settings.mediumAltitude, 0f),
                new Vector3(0f, upperCombatAltitude, connectorZ * 0.35f));
        }

        static void BuildRoadNetwork(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan)
        {
            // NewGen Urban 的基础道路模数为 21.2m。建筑间距固定为
            // 3 个模数，街区间距再取 3 倍，确保道路、路口和楼房共网格。
            const float RoadTileWidth = 22.26f;
            float pitch = ResolveStreetPitch(settings);
            float cityHalf = Mathf.Min(
                settings.mapSize * 0.5f - 5.2f,
                settings.buildingSpacing * 13f);
            float[] boundaries =
            {
                -cityHalf,
                -pitch * 3f,
                -pitch * 2f,
                -pitch,
                0f,
                pitch,
                pitch * 2f,
                pitch * 3f,
                cityHalf
            };
            for (int boundaryIndex = 1; boundaryIndex < boundaries.Length - 1;
                 boundaryIndex++)
            {
                int roadIndex = boundaryIndex - 4;
                float coordinate = boundaries[boundaryIndex];
                int northSouthBaseLanes = roadIndex == 0
                    ? 3
                    : Mathf.Abs(roadIndex) == 2 ? 2 : 1;
                AirCombatRouteKind northSouthKind = roadIndex < 0
                    ? AirCombatRouteKind.MaskedFlank
                    : roadIndex > 0
                        ? AirCombatRouteKind.LongRange
                        : AirCombatRouteKind.Main;
                for (int row = 0; row < 8; row++)
                {
                    CombatCityBlockPlan west =
                        CombatDrivenCityPcgPlanner.GetTacticalBlock(
                            plan, boundaryIndex - 1, row);
                    CombatCityBlockPlan east =
                        CombatDrivenCityPcgPlanner.GetTacticalBlock(
                            plan, boundaryIndex, row);
                    if (west != null && west.mergeEast)
                        continue;
                    float width = ResolveSegmentRoadWidth(
                        settings,
                        RoadTileWidth * northSouthBaseLanes,
                        west,
                        east);
                    AddRoad(
                        plan,
                        "road.grid.ns." + (roadIndex + 3).ToString("D2") +
                        ".segment." + row.ToString("D2"),
                        northSouthKind,
                        new Vector3(coordinate, 0f, boundaries[row]),
                        new Vector3(coordinate, 0f, boundaries[row + 1]),
                        width,
                        Mathf.Clamp(Mathf.RoundToInt(width / RoadTileWidth), 1, 4),
                        roadIndex == 2);
                }

                int eastWestBaseLanes = roadIndex == 0
                    ? 3
                    : Mathf.Abs(roadIndex) == 3 ? 2 : 1;
                AirCombatRouteKind eastWestKind = roadIndex < 0
                    ? AirCombatRouteKind.MaskedFlank
                    : roadIndex > 0
                        ? AirCombatRouteKind.LongRange
                        : AirCombatRouteKind.Main;
                for (int column = 0; column < 8; column++)
                {
                    CombatCityBlockPlan south =
                        CombatDrivenCityPcgPlanner.GetTacticalBlock(
                            plan, column, boundaryIndex - 1);
                    CombatCityBlockPlan north =
                        CombatDrivenCityPcgPlanner.GetTacticalBlock(
                            plan, column, boundaryIndex);
                    if (south != null && south.mergeNorth)
                        continue;
                    float width = ResolveSegmentRoadWidth(
                        settings,
                        RoadTileWidth * eastWestBaseLanes,
                        south,
                        north);
                    AddRoad(
                        plan,
                        "road.grid.ew." + (roadIndex + 3).ToString("D2") +
                        ".segment." + column.ToString("D2"),
                        eastWestKind,
                        new Vector3(boundaries[column], 0f, coordinate),
                        new Vector3(boundaries[column + 1], 0f, coordinate),
                        width,
                        Mathf.Clamp(Mathf.RoundToInt(width / RoadTileWidth), 1, 4),
                        false);
                }
            }
        }

        static float ResolveSegmentRoadWidth(
            AirCombatCitySettings settings,
            float baseWidth,
            CombatCityBlockPlan first,
            CombatCityBlockPlan second)
        {
            float firstScale = first != null ? first.roadWidthScale : 1f;
            float secondScale = second != null ? second.roadWidthScale : firstScale;
            float localScale = (firstScale + secondScale) * 0.5f;
            return Mathf.Clamp(
                baseWidth * localScale * settings.Difficulty.roadWidthScale,
                16f,
                94f);
        }

        static void BuildEnemyIngresses(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan,
            ref StableRandom random)
        {
            // Kept only as non-visual compatibility samples for the current
            // horde spawner. They do not author routes, regions, labels or
            // encounter semantics; the spawning policy can replace them later.
            float radius = Mathf.Min(
                settings.mapSize * 0.5f - 82f,
                500f);
            float missionOffset = settings.mission ==
                AirCombatCityMission.FacilityAssault ? 22.5f : 0f;
            int ingressDirectionCount = 6 + Mathf.RoundToInt(
                settings.Difficulty.combatPressure * 2f);
            for (int i = 0; i < ingressDirectionCount; i++)
            {
                float angle = missionOffset + i * (360f / ingressDirectionCount) +
                              random.Range(-4f, 4f);
                float radians = angle * Mathf.Deg2Rad;
                AirCombatEnemyLaneKind kind = i % 3 == 0
                    ? AirCombatEnemyLaneKind.Suicide
                    : AirCombatEnemyLaneKind.Ranged;
                float altitude = kind == AirCombatEnemyLaneKind.Suicide
                    ? settings.lowAltitude
                    : (i & 1) == 0
                        ? settings.highAltitude
                        : settings.mediumAltitude;
                Vector3 position = new Vector3(
                    Mathf.Sin(radians) * radius,
                    altitude,
                    Mathf.Cos(radians) * radius);
                Vector3 target = Vector3.Lerp(
                    new Vector3(0f, altitude, 0f),
                    plan.playerSpawn,
                    kind == AirCombatEnemyLaneKind.Suicide ? 0.35f : 0.12f);
                float warning = Vector3.Distance(position, plan.playerSpawn)
                    / Mathf.Max(1f, settings.combatSpeed * 2f);
                plan.ingresses.Add(new AirCombatEnemyIngress
                {
                    stableId = "ingress." + i.ToString("D2"),
                    position = position,
                    target = target,
                    kind = kind,
                    warningSeconds = warning
                });
            }
        }

        static void BuildBuildings(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan,
            ref StableRandom random)
        {
            float half = settings.mapSize * 0.5f;
            float spacing = settings.buildingSpacing;
            int count = Mathf.FloorToInt((settings.mapSize - spacing) / spacing);
            float start = -0.5f * (count - 1) * spacing;
            int buildingIndex = 0;
            for (int x = 0; x < count; x++)
            for (int z = 0; z < count; z++)
            {
                Vector2 point = new Vector2(start + x * spacing, start + z * spacing);
                float footprint;
                float depth;
                string parcelLabel;
                int parcelStyle = PositiveModulo(
                    x * 92821 + z * 68917 + settings.seed * 31,
                    100);
                if (parcelStyle < 65)
                {
                    footprint = spacing * random.Range(0.70f, 0.88f);
                    depth = spacing * random.Range(0.68f, 0.88f);
                    parcelLabel = "large";
                }
                else if (parcelStyle < 90)
                {
                    footprint = spacing * random.Range(0.55f, 0.70f);
                    depth = spacing * random.Range(0.54f, 0.72f);
                    parcelLabel = "standard";
                }
                else
                {
                    footprint = spacing * random.Range(0.36f, 0.52f);
                    depth = spacing * random.Range(0.34f, 0.54f);
                    parcelLabel = "small-gapfill";
                }
                float normalizedRadius = point.magnitude / Mathf.Max(1f, half);
                float localDensity = normalizedRadius < 0.46f
                    ? Mathf.Min(0.99f, settings.buildingDensity + 0.15f)
                    : normalizedRadius < 0.75f
                        ? Mathf.Min(0.98f, settings.buildingDensity + 0.14f)
                        : Mathf.Max(0.68f, settings.buildingDensity - 0.16f);
                CombatCityBlockPlan tacticalBlock =
                    CombatDrivenCityPcgPlanner.FindTacticalBlock(plan, point);
                if (tacticalBlock != null)
                {
                    localDensity = Mathf.Clamp01(
                        localDensity * tacticalBlock.buildingDensityScale);
                    if (tacticalBlock.mergedGroupId != tacticalBlock.stableId)
                    {
                        footprint = Mathf.Min(
                            spacing * 0.93f,
                            footprint * 1.08f);
                        depth = Mathf.Min(
                            spacing * 0.93f,
                            depth * 1.08f);
                    }
                }
                float maximumFlyableParcelSpan = Mathf.Max(
                    18f,
                    spacing - settings.MinimumDefaultPresetBuildingGap);
                footprint = Mathf.Min(footprint, maximumFlyableParcelSpan);
                depth = Mathf.Min(depth, maximumFlyableParcelSpan);
                if (Mathf.Abs(point.x) > half - 44f ||
                    Mathf.Abs(point.y) > half - 44f ||
                    random.Value() > localDensity)
                {
                    continue;
                }
                float protectedRoofLimit = ResolveProtectedRoofLimit(
                    settings,
                    plan,
                    point);
                bool protectedVolume =
                    !float.IsPositiveInfinity(protectedRoofLimit);
                int clusterX = x / 4;
                int clusterZ = z / 4;
                int clusterId = clusterX * 100 + clusterZ;
                int localPattern = PositiveModulo(
                    x * 7 + z * 3 + clusterId + settings.seed,
                    16);
                AirCombatBuildingBand band = ResolveBuildingBand(
                    settings,
                    point,
                    protectedRoofLimit,
                    localPattern);
                float height;
                switch (band)
                {
                    case AirCombatBuildingBand.High:
                        height = random.Range(
                            Mathf.Max(180f, settings.highAltitude * 0.82f),
                            Mathf.Min(
                                settings.maximumAltitude - 22f,
                                settings.highAltitude * 1.45f));
                        break;
                    case AirCombatBuildingBand.Medium:
                        height = protectedVolume
                            ? random.Range(
                                Mathf.Max(86f, settings.lowAltitude + 16f),
                                Mathf.Max(90f, protectedRoofLimit - 3f))
                            : random.Range(
                                Mathf.Max(96f, settings.lowAltitude + 20f),
                                Mathf.Min(182f, settings.highAltitude - 28f));
                        break;
                    default:
                        height = protectedVolume
                            ? random.Range(
                                30f,
                                Mathf.Min(72f, protectedRoofLimit - 3f))
                            : random.Range(36f, 84f);
                        break;
                }
                if (tacticalBlock != null)
                    height *= tacticalBlock.buildingHeightScale;
                if (!float.IsPositiveInfinity(protectedRoofLimit))
                    height = Mathf.Min(height, protectedRoofLimit - 3f);
                height = Mathf.Min(height, settings.maximumAltitude - 18f);
                float yaw = ResolveFacadeYaw(plan, point);
                if (IsReservedForFlight(
                        settings,
                        plan,
                        point,
                        height,
                        new Vector2(footprint, depth),
                        yaw))
                    continue;
                plan.buildings.Add(new AirCombatBuildingLot
                {
                    stableId = "building." + parcelLabel + "." +
                               buildingIndex++.ToString("D3"),
                    center = new Vector3(point.x, height * 0.5f, point.y),
                    size = new Vector3(footprint, height, depth),
                    yaw = yaw,
                    band = band,
                    archetype = ArchetypeForBand(band),
                    clusterId = clusterId,
                    visualVariant = PositiveModulo(
                        x * 92821 + z * 68917 + settings.seed,
                        97)
                });
            }
        }

        static AirCombatBuildingBand ResolveBuildingBand(
            AirCombatCitySettings settings,
            Vector2 point,
            float protectedRoofLimit,
            int localPattern)
        {
            float verticalSafety = settings.wingspan * 0.45f + 8f;
            float safeLowRoof = settings.lowAltitude - verticalSafety;
            if (!float.IsPositiveInfinity(protectedRoofLimit))
            {
                if (protectedRoofLimit <= safeLowRoof + 2f)
                    return AirCombatBuildingBand.Low;
                return localPattern < 5
                    ? AirCombatBuildingBand.Low
                    : AirCombatBuildingBand.Medium;
            }

            float sideX = ResolveStreetPitch(settings) * 2f;
            float westDistance = Mathf.Abs(point.x + sideX);
            float eastDistance = Mathf.Abs(point.x - sideX);
            if (westDistance < settings.FlankCorridorWidth * 0.85f)
            {
                if (localPattern < 2)
                    return AirCombatBuildingBand.Low;
                return localPattern < 9
                    ? AirCombatBuildingBand.Medium
                    : AirCombatBuildingBand.High;
            }
            if (eastDistance < settings.LongRangeCorridorWidth * 0.95f)
            {
                if (localPattern < 6)
                    return AirCombatBuildingBand.Low;
                return localPattern < 13
                    ? AirCombatBuildingBand.Medium
                    : AirCombatBuildingBand.High;
            }
            float centerDistance = point.magnitude;
            if (centerDistance < settings.ManeuverDiameter * 0.9f)
            {
                if (localPattern < 4)
                    return AirCombatBuildingBand.Low;
                return localPattern < 12
                    ? AirCombatBuildingBand.Medium
                    : AirCombatBuildingBand.High;
            }
            if (localPattern < 5)
                return AirCombatBuildingBand.Low;
            return localPattern < 12
                ? AirCombatBuildingBand.Medium
                : AirCombatBuildingBand.High;
        }

        static void BuildLowUrbanIslands(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan,
            ref StableRandom random)
        {
            // 中央不能是同心圆广场，也不能用一整片低楼“假装有城市”。
            // 五个不对称战斗簇分别形成近地穿行、急转遮挡和脱离恢复边界；
            // 每个簇内部固定穿插低/中楼，任何一个簇都不能被单一高度占满。
            int stableIndex = plan.buildings.Count;
            float rotation = random.Range(-28f, 28f) * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rotation);
            float sin = Mathf.Sin(rotation);
            Vector2[] normalizedAnchors =
            {
                new Vector2(-0.37f, 0.24f),
                new Vector2(0.38f, 0.25f),
                new Vector2(0.25f, -0.36f),
                new Vector2(-0.86f, -0.28f),
                new Vector2(0.82f, -0.12f)
            };
            for (int cluster = 0; cluster < normalizedAnchors.Length; cluster++)
            {
                Vector2 source = normalizedAnchors[cluster] *
                                 settings.ManeuverDiameter;
                Vector2 anchor = new Vector2(
                    source.x * cos - source.y * sin,
                    source.x * sin + source.y * cos);
                int samples = cluster == 1 || cluster >= 3 ? 12 : 10;
                float spacing = cluster == 2 ? 42f : 38f;
                for (int i = 0; i < samples; i++)
                {
                    int columns = cluster == 1 ? 4 : 5;
                    int rows = Mathf.CeilToInt(samples / (float)columns);
                    float localX = (i % columns - (columns - 1) * 0.5f) * spacing;
                    float localZ = (i / columns - (rows - 1) * 0.5f) * spacing;
                    Vector2 local = new Vector2(
                        localX + random.Range(-5f, 5f),
                        localZ + random.Range(-5f, 5f));
                    Vector2 point = anchor + new Vector2(
                        local.x * cos - local.y * sin,
                        local.x * sin + local.y * cos);
                    float width = random.Range(24f, 34f);
                    float depth = random.Range(24f, 36f);
                    float roofLimit = ResolveProtectedRoofLimit(
                        settings,
                        plan,
                        point);
                    float mediumRoofLimit = float.IsPositiveInfinity(roofLimit)
                        ? settings.mediumAltitude -
                          (settings.wingspan * 0.45f + 11f)
                        : roofLimit - 3f;
                    bool medium = mediumRoofLimit >= 72f &&
                                   PositiveModulo(i + cluster * 2, 3) != 0;
                    float height = medium
                        ? random.Range(
                            Mathf.Min(92f, mediumRoofLimit - 1f),
                            Mathf.Min(118f, mediumRoofLimit))
                        : random.Range(34f, 64f);
                    float yaw = ResolveFacadeYaw(plan, point);
                    if (IsReservedForFlight(
                            settings,
                            plan,
                            point,
                            height,
                            new Vector2(width, depth),
                            yaw) ||
                        OverlapsBuilding(plan.buildings, point, width, depth))
                    {
                        continue;
                    }
                    plan.buildings.Add(new AirCombatBuildingLot
                    {
                        stableId = "building.central-cluster." + cluster + "." +
                                   stableIndex++.ToString("D3"),
                        center = new Vector3(point.x, height * 0.5f, point.y),
                        size = new Vector3(width, height, depth),
                        yaw = yaw,
                        band = medium
                            ? AirCombatBuildingBand.Medium
                            : AirCombatBuildingBand.Low,
                        archetype = medium
                            ? AirCombatBuildingArchetype.MidSlab
                            : AirCombatBuildingArchetype.LowBlock,
                        clusterId = 900 + cluster,
                        visualVariant = PositiveModulo(
                            stableIndex * 37 + cluster * 11 + settings.seed,
                            97)
                    });
                }
            }

            // 三个簇各向出生盆地伸出两座低矮“门齿”。它们不构成圆环，
            // 只负责打断中心大平面的视线，同时给 60m 低空航线保留净空。
            Vector2[] gatelets =
            {
                new Vector2(-0.16f, 0.14f),
                new Vector2(0.18f, 0.17f),
                new Vector2(0.23f, -0.25f),
                new Vector2(-0.27f, -0.17f),
                new Vector2(0.06f, -0.31f),
                new Vector2(-0.31f, 0.02f)
            };
            for (int i = 0; i < gatelets.Length; i++)
            {
                Vector2 source = gatelets[i] * settings.ManeuverDiameter;
                Vector2 point = new Vector2(
                    source.x * cos - source.y * sin,
                    source.x * sin + source.y * cos);
                point += new Vector2(
                    random.Range(-4f, 4f),
                    random.Range(-4f, 4f));
                float width = random.Range(23f, 29f);
                float depth = random.Range(23f, 30f);
                float height = random.Range(38f, 62f);
                float yaw = ResolveFacadeYaw(plan, point);
                if (IsReservedForFlight(
                        settings,
                        plan,
                        point,
                        height,
                        new Vector2(width, depth),
                        yaw) ||
                    OverlapsBuilding(plan.buildings, point, width, depth))
                {
                    continue;
                }
                plan.buildings.Add(new AirCombatBuildingLot
                {
                    stableId = "building.central-gatelet." +
                               stableIndex++.ToString("D3"),
                    center = new Vector3(point.x, height * 0.5f, point.y),
                    size = new Vector3(width, height, depth),
                    yaw = yaw,
                    band = AirCombatBuildingBand.Low,
                    archetype = AirCombatBuildingArchetype.LowBlock,
                    clusterId = 900 + i % 3,
                    visualVariant = PositiveModulo(
                        stableIndex * 53 + settings.seed,
                        97)
                });
            }
        }

        static void BuildRecoveryDistricts(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan,
            ref StableRandom random)
        {
            int stableIndex = plan.buildings.Count;
            int districtIndex = 0;
            for (int i = 0; i < plan.volumes.Count; i++)
            {
                AirCombatTacticalVolume volume = plan.volumes[i];
                if (volume.kind != AirCombatVolumeKind.RecoveryPocket)
                    continue;

                Vector2 center = new Vector2(volume.center.x, volume.center.z);
                float clearX = volume.size.x * 0.5f + 5f;
                float clearZ = volume.size.z * 0.5f + 5f;
                for (int b = plan.buildings.Count - 1; b >= 0; b--)
                {
                    AirCombatBuildingLot existing = plan.buildings[b];
                    if (existing.band == AirCombatBuildingBand.Facility)
                        continue;
                    if (Mathf.Abs(existing.center.x - center.x) <= clearX &&
                        Mathf.Abs(existing.center.z - center.y) <= clearZ)
                    {
                        plan.buildings.RemoveAt(b);
                    }
                }

                // 维修庭院背向交战中心开口。玩家进入后能换取完整的
                // 十秒维修窗口，但后墙也会切断其对中心战场的输出线；
                // 想继续射击就必须离开庭院，避免安全掩体成为永久炮台。
                float openSign = center.x < 0f ? -1f : 1f;
                float backHeight = Mathf.Max(
                    random.Range(108f, 116f),
                    settings.mediumAltitude + 28f);
                float nearHeight = Mathf.Max(
                    random.Range(94f, 104f),
                    settings.mediumAltitude + 16f);
                float farHeight = Mathf.Max(
                    random.Range(101f, 112f),
                    settings.mediumAltitude + 20f);
                AddRecoveryBuilding(
                    settings,
                    plan,
                    "building.recovery." + districtIndex + ".back." +
                    stableIndex++.ToString("D3"),
                    center + new Vector2(-openSign * 44f, 0f),
                    new Vector2(110f, 18f),
                    backHeight,
                    openSign > 0f ? 90f : -90f,
                    970 + districtIndex,
                    stableIndex);
                AddRecoveryBuilding(
                    settings,
                    plan,
                    "building.recovery." + districtIndex + ".south." +
                    stableIndex++.ToString("D3"),
                    center + new Vector2(-openSign * 12f, -42f),
                    new Vector2(78f, 18f),
                    nearHeight,
                    0f,
                    970 + districtIndex,
                    stableIndex);
                AddRecoveryBuilding(
                    settings,
                    plan,
                    "building.recovery." + districtIndex + ".north." +
                    stableIndex++.ToString("D3"),
                    center + new Vector2(-openSign * 12f, 42f),
                    new Vector2(78f, 18f),
                    farHeight,
                    180f,
                    970 + districtIndex,
                    stableIndex);
                districtIndex++;
            }
        }

        static void AddRecoveryBuilding(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan,
            string id,
            Vector2 point,
            Vector2 footprint,
            float height,
            float yaw,
            int clusterId,
            int visualVariant)
        {
            // These three walls are the physical definition of the recovery
            // pocket, not ordinary filler buildings. Their authored opening
            // faces away from the city centre and their footprints are validated later
            // against both roads and flight corridors. Silently dropping a
            // wall here turns a named recovery area into empty scenery.
            AirCombatBuildingBand band = height >= 68f
                ? AirCombatBuildingBand.Medium
                : AirCombatBuildingBand.Low;
            plan.buildings.Add(new AirCombatBuildingLot
            {
                stableId = id,
                center = new Vector3(point.x, height * 0.5f, point.y),
                size = new Vector3(footprint.x, height, footprint.y),
                yaw = yaw,
                band = band,
                archetype = band == AirCombatBuildingBand.Medium
                    ? AirCombatBuildingArchetype.MidSlab
                    : AirCombatBuildingArchetype.LowBlock,
                clusterId = clusterId,
                visualVariant = PositiveModulo(visualVariant * 61, 97)
            });
        }

        static void BuildTacticalPark(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan)
        {
            const float RoadTileWidth = 22.26f;
            const float SidewalkWidth = 6.2f;
            float pitch = ResolveStreetPitch(settings);
            float nearCenter = 0.5f * (
                RoadTileWidth * 1.5f + SidewalkWidth +
                (pitch - RoadTileWidth * 0.5f - SidewalkWidth));
            float outerCenter = pitch * 2.5f;
            Vector2[] candidates =
            {
                new Vector2(-nearCenter, outerCenter),
                new Vector2(nearCenter, outerCenter),
                new Vector2(-nearCenter, -outerCenter),
                new Vector2(nearCenter, -outerCenter),
                new Vector2(-outerCenter, nearCenter),
                new Vector2(outerCenter, -nearCenter)
            };
            Vector2 center = candidates[0];
            float bestScore = float.NegativeInfinity;
            for (int c = 0; c < candidates.Length; c++)
            {
                float score = 0f;
                for (int b = 0; b < plan.buildings.Count; b++)
                {
                    AirCombatBuildingLot building = plan.buildings[b];
                    float distance = Vector2.Distance(
                        candidates[c],
                        new Vector2(building.center.x, building.center.z));
                    if (distance < 88f || distance > 270f)
                        continue;
                    score += building.band == AirCombatBuildingBand.High
                        ? 2.4f
                        : building.band == AirCombatBuildingBand.Medium
                            ? 1.5f
                            : 0.7f;
                }
                for (int v = 0; v < plan.volumes.Count; v++)
                {
                    if (plan.volumes[v].kind !=
                        AirCombatVolumeKind.RecoveryPocket)
                    {
                        continue;
                    }
                    Vector2 recovery = new Vector2(
                        plan.volumes[v].center.x,
                        plan.volumes[v].center.z);
                    if (Vector2.Distance(candidates[c], recovery) < 260f)
                        score -= 1000f;
                }
                if (score <= bestScore)
                    continue;
                bestScore = score;
                center = candidates[c];
            }
            Vector2 size = new Vector2(112f, 112f);
            for (int i = plan.buildings.Count - 1; i >= 0; i--)
            {
                AirCombatBuildingLot building = plan.buildings[i];
                float radius = Mathf.Sqrt(
                    building.size.x * building.size.x +
                    building.size.z * building.size.z) * 0.5f;
                if (Mathf.Abs(building.center.x - center.x) <=
                        size.x * 0.5f + radius &&
                    Mathf.Abs(building.center.z - center.y) <=
                        size.y * 0.5f + radius)
                {
                    plan.buildings.RemoveAt(i);
                }
            }
            AddVolume(
                plan,
                "volume.danger-plaza.park",
                AirCombatVolumeKind.DangerPlaza,
                new Vector3(center.x, settings.lowAltitude, center.y),
                new Vector3(size.x, settings.mediumAltitude, size.y));
        }

        static bool OverlapsBuilding(
            List<AirCombatBuildingLot> buildings,
            Vector2 point,
            float width,
            float depth)
        {
            float radius = Mathf.Sqrt(width * width + depth * depth) * 0.5f;
            for (int i = 0; i < buildings.Count; i++)
            {
                AirCombatBuildingLot building = buildings[i];
                float otherRadius = Mathf.Sqrt(
                    building.size.x * building.size.x +
                    building.size.z * building.size.z) * 0.5f;
                Vector2 other = new Vector2(
                    building.center.x,
                    building.center.z);
                if (Vector2.Distance(point, other) < radius + otherRadius + 8f)
                    return true;
            }
            return false;
        }

        static void EnsureCentralTacticalCover(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan)
        {
            // 中央增补不是随机撒楼：候选点是一圈不对称的街角地块。
            // 只有不侵占公园/维修区/出生盆地、不碰道路、不切安全航路的
            // 地块才会落楼；因此视觉密度提高，但飞机既有净空规则不变。
            const int TargetUsefulCover = 10;
            float centralRadius = settings.ManeuverDiameter * 0.5f;
            int usefulCover = 0;
            for (int i = 0; i < plan.buildings.Count; i++)
            {
                AirCombatBuildingLot existing = plan.buildings[i];
                Vector2 existingPoint = new Vector2(
                    existing.center.x,
                    existing.center.z);
                if (existingPoint.magnitude < centralRadius &&
                    existing.size.y >= settings.lowAltitude + 12f)
                {
                    usefulCover++;
                }
            }
            if (usefulCover >= TargetUsefulCover)
                return;

            const float Width = 22f;
            const float Depth = 24f;
            int added = 0;
            while (usefulCover < TargetUsefulCover)
            {
                Vector2 bestPoint = Vector2.zero;
                float bestYaw = 0f;
                float bestScore = float.NegativeInfinity;
                float height = 96f + added % 3 * 5f;
                int row = 0;
                for (float z = -160f; z <= 160f; z += 10f, row++)
                for (float x = -160f + (row & 1) * 5f;
                     x <= 160f;
                     x += 10f)
                {
                    Vector2 point = new Vector2(x, z);
                    float radius = point.magnitude;
                    if (radius < 72f || radius > centralRadius - 12f)
                        continue;
                    float yaw = ResolveFacadeYaw(plan, point);
                    if (IsInsideProtectedGroundVolume(
                            plan,
                            point,
                            new Vector2(Width, Depth)) ||
                        IsReservedForFlight(
                            settings,
                            plan,
                            point,
                            height,
                            new Vector2(Width, Depth),
                            yaw) ||
                        OverlapsBuilding(plan.buildings, point, Width, Depth))
                    {
                        continue;
                    }

                    // 优先填补现有遮挡之间最大的空洞，并轻微偏好 125m
                    // 战术环；相同 Seed 不会因运行平台改变摆放结果。
                    float nearest = float.PositiveInfinity;
                    for (int b = 0; b < plan.buildings.Count; b++)
                    {
                        Vector2 other = new Vector2(
                            plan.buildings[b].center.x,
                            plan.buildings[b].center.z);
                        nearest = Mathf.Min(
                            nearest,
                            Vector2.Distance(point, other));
                    }
                    float score = nearest - Mathf.Abs(radius - 125f) * 0.08f;
                    if (score <= bestScore)
                        continue;
                    bestScore = score;
                    bestPoint = point;
                    bestYaw = yaw;
                }

                if (float.IsNegativeInfinity(bestScore))
                    break;

                plan.buildings.Add(new AirCombatBuildingLot
                {
                    stableId = "building.central-tactical-infill." +
                               added.ToString("D2"),
                    center = new Vector3(
                        bestPoint.x,
                        height * 0.5f,
                        bestPoint.y),
                    size = new Vector3(Width, height, Depth),
                    yaw = bestYaw,
                    band = AirCombatBuildingBand.Medium,
                    archetype = AirCombatBuildingArchetype.MidSlab,
                    clusterId = 940 + added / 4,
                    visualVariant = PositiveModulo(
                        settings.seed + added * 41,
                        97)
                });
                usefulCover++;
                added++;
            }
        }

        static bool IsInsideProtectedGroundVolume(
            AirCombatCityPlan plan,
            Vector2 point,
            Vector2 footprint)
        {
            for (int i = 0; i < plan.volumes.Count; i++)
            {
                AirCombatTacticalVolume volume = plan.volumes[i];
                if (volume.kind != AirCombatVolumeKind.RecoveryPocket &&
                    volume.kind != AirCombatVolumeKind.DangerPlaza)
                {
                    continue;
                }
                float halfX = volume.size.x * 0.5f + footprint.x * 0.5f + 6f;
                float halfZ = volume.size.z * 0.5f + footprint.y * 0.5f + 6f;
                if (Mathf.Abs(point.x - volume.center.x) <= halfX &&
                    Mathf.Abs(point.y - volume.center.z) <= halfZ)
                {
                    return true;
                }
            }
            return false;
        }

        static void EnsureCentralLowCover(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan)
        {
            // 低层门齿承担贴地绕行与短暂断锁；它们优先占用中层楼因
            // 垂直净空而无法使用的地块，所以不会和中层补楼争夺战术位。
            const int TargetLowCover = 5;
            const float Width = 18f;
            const float Depth = 20f;
            float centralRadius = settings.ManeuverDiameter * 0.5f;
            int lowCount = 0;
            for (int i = 0; i < plan.buildings.Count; i++)
            {
                AirCombatBuildingLot building = plan.buildings[i];
                if (building.band == AirCombatBuildingBand.Low &&
                    new Vector2(building.center.x, building.center.z).magnitude <
                    centralRadius)
                {
                    lowCount++;
                }
            }
            int added = 0;
            while (lowCount < TargetLowCover)
            {
                Vector2 bestPoint = Vector2.zero;
                float bestYaw = 0f;
                float bestScore = float.NegativeInfinity;
                float height = 46f + added % 3 * 4f;
                int row = 0;
                for (float z = -160f; z <= 160f; z += 8f, row++)
                for (float x = -160f + (row & 1) * 4f;
                     x <= 160f;
                     x += 8f)
                {
                    Vector2 point = new Vector2(x, z);
                    float radius = point.magnitude;
                    if (radius < 38f || radius > centralRadius - 10f)
                        continue;
                    float yaw = ResolveFacadeYaw(plan, point);
                    if (IsInsideProtectedGroundVolume(
                            plan,
                            point,
                            new Vector2(Width, Depth)) ||
                        IsReservedForFlight(
                            settings,
                            plan,
                            point,
                            height,
                            new Vector2(Width, Depth),
                            yaw) ||
                        OverlapsBuilding(plan.buildings, point, Width, Depth))
                    {
                        continue;
                    }

                    float nearest = float.PositiveInfinity;
                    for (int b = 0; b < plan.buildings.Count; b++)
                    {
                        Vector2 other = new Vector2(
                            plan.buildings[b].center.x,
                            plan.buildings[b].center.z);
                        nearest = Mathf.Min(
                            nearest,
                            Vector2.Distance(point, other));
                    }
                    float score = nearest - Mathf.Abs(radius - 92f) * 0.06f;
                    if (score <= bestScore)
                        continue;
                    bestScore = score;
                    bestPoint = point;
                    bestYaw = yaw;
                }

                if (float.IsNegativeInfinity(bestScore))
                    break;
                plan.buildings.Add(new AirCombatBuildingLot
                {
                    stableId = "building.central-low-infill." +
                               added.ToString("D2"),
                    center = new Vector3(
                        bestPoint.x,
                        height * 0.5f,
                        bestPoint.y),
                    size = new Vector3(Width, height, Depth),
                    yaw = bestYaw,
                    band = AirCombatBuildingBand.Low,
                    archetype = AirCombatBuildingArchetype.LowBlock,
                    clusterId = 960 + added / 3,
                    visualVariant = PositiveModulo(
                        settings.seed + added * 67,
                        97)
                });
                lowCount++;
                added++;
            }
        }

        static void EnsureCentralCoverContinuity(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan)
        {
            // Continuity is a construction invariant, not a reason to throw
            // away the player's seed. Fill only pathological holes that are
            // larger than roughly two seconds of flight; authored exposure
            // plazas, roads and flight corridors remain protected.
            const int MaximumRepairs = 6;
            const float Width = 22f;
            const float Depth = 24f;
            const float Height = 94f;
            const float SearchStep = 10f;
            const int SearchRings = 6;
            const int DirectionsPerRing = 16;
            float sampleRadius = settings.ManeuverDiameter * 0.74f;
            float sampleStep = settings.buildingSpacing * 0.5f;
            float maximumAllowedGap = Mathf.Max(
                1f,
                settings.combatSpeed * 2.4f - 1.5f);

            for (int repair = 0; repair < MaximumRepairs; repair++)
            {
                Vector2 worstSample = Vector2.zero;
                float worstGap = 0f;
                for (float x = -sampleRadius;
                     x <= sampleRadius;
                     x += sampleStep)
                for (float z = -sampleRadius;
                     z <= sampleRadius;
                     z += sampleStep)
                {
                    Vector2 sample = new Vector2(x, z);
                    if (sample.sqrMagnitude > sampleRadius * sampleRadius)
                        continue;
                    float nearest = DistanceToNearestUsefulCover(
                        settings,
                        plan,
                        sample);
                    if (nearest <= worstGap)
                        continue;
                    worstGap = nearest;
                    worstSample = sample;
                }
                if (worstGap <= maximumAllowedGap)
                    return;

                Vector2 bestPoint = Vector2.zero;
                float bestYaw = 0f;
                float bestGap = float.PositiveInfinity;
                for (int ring = 0; ring <= SearchRings; ring++)
                {
                    int directionCount = ring == 0
                        ? 1
                        : DirectionsPerRing;
                    for (int directionIndex = 0;
                         directionIndex < directionCount;
                         directionIndex++)
                    {
                        float angle = directionIndex /
                                      (float)DirectionsPerRing *
                                      Mathf.PI * 2f;
                        Vector2 point = worstSample + new Vector2(
                            Mathf.Cos(angle),
                            Mathf.Sin(angle)) * ring * SearchStep;
                        if (point.magnitude > sampleRadius + 24f)
                            continue;
                        float yaw = ResolveFacadeYaw(plan, point);
                        if (IsInsideProtectedGroundVolume(
                                plan,
                                point,
                                new Vector2(Width, Depth)) ||
                            IsReservedForFlight(
                                settings,
                                plan,
                                point,
                                Height,
                                new Vector2(Width, Depth),
                                yaw) ||
                            OverlapsBuilding(
                                plan.buildings,
                                point,
                                Width,
                                Depth))
                        {
                            continue;
                        }

                        var probe = new AirCombatBuildingLot
                        {
                            center = new Vector3(point.x, Height * 0.5f, point.y),
                            size = new Vector3(Width, Height, Depth),
                            yaw = yaw,
                            band = AirCombatBuildingBand.Medium
                        };
                        float repairedGap = DistanceToBuildingFootprint(
                            worstSample,
                            probe);
                        if (repairedGap >= bestGap)
                            continue;
                        bestGap = repairedGap;
                        bestPoint = point;
                        bestYaw = yaw;
                    }
                }
                if (float.IsPositiveInfinity(bestGap) || bestGap >= worstGap)
                    return;

                plan.buildings.Add(new AirCombatBuildingLot
                {
                    stableId = "building.cover-continuity-repair." +
                               repair.ToString("D2"),
                    center = new Vector3(
                        bestPoint.x,
                        Height * 0.5f,
                        bestPoint.y),
                    size = new Vector3(Width, Height, Depth),
                    yaw = bestYaw,
                    band = AirCombatBuildingBand.Medium,
                    archetype = AirCombatBuildingArchetype.MidSlab,
                    clusterId = 980 + repair,
                    visualVariant = PositiveModulo(
                        settings.seed + repair * 83,
                        97)
                });
            }
        }

        static float DistanceToNearestUsefulCover(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan,
            Vector2 sample)
        {
            float nearest = float.PositiveInfinity;
            for (int index = 0; index < plan.buildings.Count; index++)
            {
                AirCombatBuildingLot building = plan.buildings[index];
                if (building.band == AirCombatBuildingBand.Facility ||
                    building.size.y < settings.lowAltitude * 0.80f)
                {
                    continue;
                }
                nearest = Mathf.Min(
                    nearest,
                    DistanceToBuildingFootprint(sample, building));
            }
            return nearest;
        }

        static int PositiveModulo(int value, int modulo)
        {
            int result = value % modulo;
            return result < 0 ? result + modulo : result;
        }

        static AirCombatBuildingArchetype ArchetypeForBand(
            AirCombatBuildingBand band)
        {
            switch (band)
            {
                case AirCombatBuildingBand.Medium:
                    return AirCombatBuildingArchetype.MidSlab;
                case AirCombatBuildingBand.High:
                    return AirCombatBuildingArchetype.CombatTower;
                case AirCombatBuildingBand.Facility:
                    return AirCombatBuildingArchetype.Facility;
                default:
                    return AirCombatBuildingArchetype.LowBlock;
            }
        }

        static float ResolveFacadeYaw(
            AirCombatCityPlan plan,
            Vector2 buildingPosition)
        {
            float bestDistance = float.PositiveInfinity;
            Vector2 bestDirection = Vector2.up;
            for (int i = 0; i < plan.roads.Count; i++)
            {
                AirCombatRoadStrip road = plan.roads[i];
                Vector2 start = new Vector2(road.start.x, road.start.z);
                Vector2 end = new Vector2(road.end.x, road.end.z);
                Vector2 segment = end - start;
                float denominator = Mathf.Max(0.0001f, segment.sqrMagnitude);
                float t = Mathf.Clamp01(
                    Vector2.Dot(buildingPosition - start, segment) /
                    denominator);
                Vector2 closest = start + segment * t;
                Vector2 toRoad = closest - buildingPosition;
                float distance = toRoad.sqrMagnitude;
                if (distance >= bestDistance || distance < 0.0001f)
                    continue;
                bestDistance = distance;
                bestDirection = toRoad.normalized;
            }

            // Unity 本地 +Z 是正面：atan2(x, z) 得到绕 +Y 的临街角。
            return Mathf.Atan2(bestDirection.x, bestDirection.y) *
                   Mathf.Rad2Deg;
        }

        static void PromoteCentralMediumCover(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan,
            ref StableRandom random)
        {
            int[] promotedPerQuadrant = new int[4];
            float maneuverRadius = settings.ManeuverDiameter * 0.5f;
            float protectedSpawnRadius = settings.RecoveryDiameter * 0.23f;
            int remainingLow = 0;
            int existingMedium = 0;
            for (int i = 0; i < plan.buildings.Count; i++)
            {
                AirCombatBuildingLot candidate = plan.buildings[i];
                Vector2 candidatePoint = new Vector2(
                    candidate.center.x,
                    candidate.center.z);
                if (candidatePoint.magnitude >= maneuverRadius)
                    continue;
                if (candidate.band == AirCombatBuildingBand.Low)
                    remainingLow++;
                else if (candidate.band == AirCombatBuildingBand.Medium)
                    existingMedium++;
            }
            // 中层遮挡已经足够时必须保留低楼，不能把中心的低层门齿
            // 全部晋升，导致高度混合重新退化为单一中层。
            if (existingMedium >= 8)
                return;
            for (int i = 0; i < plan.buildings.Count; i++)
            {
                AirCombatBuildingLot building = plan.buildings[i];
                if (building.band != AirCombatBuildingBand.Low)
                    continue;
                Vector2 point = new Vector2(
                    building.center.x,
                    building.center.z);
                float distance = point.magnitude;
                if (distance >= maneuverRadius ||
                    distance <= protectedSpawnRadius)
                {
                    continue;
                }
                int quadrant = (point.x >= 0f ? 1 : 0) +
                               (point.y >= 0f ? 2 : 0);
                if (promotedPerQuadrant[quadrant] >= 2)
                    continue;
                if (remainingLow <= 6 || existingMedium >= 8)
                    break;
                float height = random.Range(
                    settings.lowAltitude + 24f,
                    settings.mediumAltitude - 18f);
                if (IsReservedForFlight(
                        settings,
                        plan,
                        point,
                        height,
                        new Vector2(building.size.x, building.size.z),
                        building.yaw))
                    continue;
                building.band = AirCombatBuildingBand.Medium;
                building.size = new Vector3(
                    building.size.x,
                    height,
                    building.size.z);
                building.center = new Vector3(
                    building.center.x,
                    height * 0.5f,
                    building.center.z);
                building.archetype = AirCombatBuildingArchetype.MidSlab;
                promotedPerQuadrant[quadrant]++;
                remainingLow--;
                existingMedium++;
            }
        }

        static void PromoteSkylineAnchors(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan)
        {
            int[] candidateIndices = { -1, -1, -1, -1 };
            float[] candidateScores =
            {
                float.PositiveInfinity,
                float.PositiveInfinity,
                float.PositiveInfinity,
                float.PositiveInfinity
            };
            float targetRadius = settings.ManeuverDiameter * 0.95f;
            float ceilingGap = Mathf.Clamp(
                settings.wingspan * 0.35f,
                8f,
                20f);
            float targetHeight = settings.maximumAltitude - ceilingGap;

            for (int i = 0; i < plan.buildings.Count; i++)
            {
                AirCombatBuildingLot building = plan.buildings[i];
                if (building.band != AirCombatBuildingBand.High)
                    continue;
                Vector2 point = new Vector2(
                    building.center.x,
                    building.center.z);
                if (IsReservedForFlight(
                        settings,
                        plan,
                        point,
                        Mathf.Max(building.size.y, targetHeight),
                        new Vector2(building.size.x, building.size.z),
                        building.yaw))
                {
                    continue;
                }
                int quadrant = (point.x >= 0f ? 1 : 0) +
                               (point.y >= 0f ? 2 : 0);
                float score = Mathf.Abs(point.magnitude - targetRadius);
                if (score >= candidateScores[quadrant])
                    continue;
                candidateScores[quadrant] = score;
                candidateIndices[quadrant] = i;
            }

            for (int quadrant = 0; quadrant < candidateIndices.Length; quadrant++)
            {
                int index = candidateIndices[quadrant];
                if (index < 0)
                    continue;
                AirCombatBuildingLot building = plan.buildings[index];
                float height = Mathf.Max(building.size.y, targetHeight);
                building.size = new Vector3(
                    building.size.x,
                    height,
                    building.size.z);
                building.center = new Vector3(
                    building.center.x,
                    height * 0.5f,
                    building.center.z);
                building.archetype = AirCombatBuildingArchetype.Landmark;
            }
        }

        static bool IsReservedForFlight(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan,
            Vector2 point,
            float buildingHeight,
            Vector2 footprint,
            float yaw)
        {
            const float RoadsideClearance = 6.2f;
            footprint.x = Mathf.Max(2f, footprint.x);
            footprint.y = Mathf.Max(2f, footprint.y);
            for (int i = 0; i < plan.roads.Count; i++)
            {
                AirCombatRoadStrip road = plan.roads[i];
                if (FootprintIntersectsCorridor(
                    point,
                    footprint,
                    yaw,
                    new Vector2(road.start.x, road.start.z),
                    new Vector2(road.end.x, road.end.z),
                    road.width * 0.5f + RoadsideClearance,
                    out _))
                {
                    return true;
                }
            }
            // Compatibility samples are spawn pads, not empty radial corridors.
            // Reserve only a compact formation footprint around each pad so
            // enemies do not materialize inside a tower while the surrounding
            // district remains dense.
            float footprintRadius = Mathf.Sqrt(
                footprint.x * footprint.x +
                footprint.y * footprint.y) * 0.5f;
            for (int i = 0; i < plan.ingresses.Count; i++)
            {
                Vector2 entrance = new Vector2(
                    plan.ingresses[i].position.x,
                    plan.ingresses[i].position.z);
                if (Vector2.Distance(point, entrance) <
                    72f + footprintRadius)
                {
                    return true;
                }
            }
            for (int i = 0; i < plan.routes.Count; i++)
            {
                AirCombatFlightRoute route = plan.routes[i];
                // 内部刷新兼容采样点不是永久无建筑走廊；AI 可按
                // 建筑高度改变末段航向。只有玩家三条战略航路雕刻硬净空。
                if (route.kind == AirCombatRouteKind.EnemyIngress)
                    continue;
                for (int p = 1; p < route.points.Length; p++)
                {
                    if (!FootprintIntersectsCorridor(
                        point,
                        footprint,
                        yaw,
                        new Vector2(route.points[p - 1].x, route.points[p - 1].z),
                        new Vector2(route.points[p].x, route.points[p].z),
                        route.width * 0.5f,
                        out float segmentT))
                    {
                        continue;
                    }
                    float routeAltitude = Mathf.Lerp(
                        route.points[p - 1].y,
                        route.points[p].y,
                        segmentT);
                    float verticalSafety = settings.wingspan * 0.45f + 8f;
                    if (buildingHeight + verticalSafety >= routeAltitude)
                        return true;
                }
            }
            float protectedRoofLimit = ResolveProtectedRoofLimit(
                settings,
                plan,
                point);
            if (!float.IsPositiveInfinity(protectedRoofLimit) &&
                buildingHeight > protectedRoofLimit)
            {
                return true;
            }
            if (settings.mission == AirCombatCityMission.FacilityAssault)
            {
                Vector2 objective = new Vector2(plan.objective.x, plan.objective.z);
                if (Vector2.Distance(point, objective) < 145f)
                    return true;
            }
            return false;
        }

        internal static bool FootprintIntersectsCorridor(
            Vector2 center,
            Vector2 footprint,
            float yaw,
            Vector2 segmentStart,
            Vector2 segmentEnd,
            float corridorHalfWidth,
            out float segmentT)
        {
            float radians = yaw * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);
            Vector2 localStart = WorldToFootprintLocal(
                segmentStart - center,
                cos,
                sin);
            Vector2 localEnd = WorldToFootprintLocal(
                segmentEnd - center,
                cos,
                sin);
            Vector2 half = footprint * 0.5f +
                           Vector2.one * Mathf.Max(0f, corridorHalfWidth);
            return SegmentIntersectsAabb(
                localStart,
                localEnd,
                half,
                out segmentT);
        }

        static Vector2 WorldToFootprintLocal(
            Vector2 delta,
            float cos,
            float sin)
        {
            return new Vector2(
                cos * delta.x - sin * delta.y,
                sin * delta.x + cos * delta.y);
        }

        static bool SegmentIntersectsAabb(
            Vector2 start,
            Vector2 end,
            Vector2 half,
            out float segmentT)
        {
            float minimum = 0f;
            float maximum = 1f;
            Vector2 delta = end - start;
            if (!ClipSegmentAxis(
                    start.x, delta.x, half.x, ref minimum, ref maximum) ||
                !ClipSegmentAxis(
                    start.y, delta.y, half.y, ref minimum, ref maximum))
            {
                segmentT = 0f;
                return false;
            }
            segmentT = Mathf.Clamp01((minimum + maximum) * 0.5f);
            return true;
        }

        static bool ClipSegmentAxis(
            float origin,
            float delta,
            float halfExtent,
            ref float minimum,
            ref float maximum)
        {
            if (Mathf.Abs(delta) <= 0.0001f)
                return origin >= -halfExtent && origin <= halfExtent;
            float inverse = 1f / delta;
            float entry = (-halfExtent - origin) * inverse;
            float exit = (halfExtent - origin) * inverse;
            if (entry > exit)
            {
                float swap = entry;
                entry = exit;
                exit = swap;
            }
            minimum = Mathf.Max(minimum, entry);
            maximum = Mathf.Min(maximum, exit);
            return minimum <= maximum;
        }

        static float ResolveProtectedRoofLimit(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan,
            Vector2 point)
        {
            float verticalSafety = settings.wingspan * 0.45f + 8f;
            float safeLowRoof = settings.lowAltitude - verticalSafety;
            float safeMediumRoof = settings.mediumAltitude - verticalSafety;
            float result = float.PositiveInfinity;
            for (int i = 0; i < plan.volumes.Count; i++)
            {
                AirCombatTacticalVolume volume = plan.volumes[i];
                if (volume.kind != AirCombatVolumeKind.SpawnBasin &&
                    volume.kind != AirCombatVolumeKind.ManeuverBowl &&
                    volume.kind != AirCombatVolumeKind.RecoveryPocket)
                {
                    continue;
                }
                Vector2 center = new Vector2(volume.center.x, volume.center.z);
                float radius = Mathf.Max(volume.size.x, volume.size.z) * 0.5f;
                float distance = Vector2.Distance(point, center);
                if (distance >= radius)
                    continue;
                float limit = safeMediumRoof;
                if (volume.kind == AirCombatVolumeKind.SpawnBasin &&
                    distance < radius * 0.46f)
                {
                    limit = safeLowRoof;
                }
                result = Mathf.Min(result, limit);
            }
            return result;
        }

        static void BuildFacility(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan)
        {
            Vector3 center = new Vector3(
                plan.objective.x,
                0f,
                plan.objective.z);
            plan.facilityCores.Add(center + new Vector3(-78f, 0f, -24f));
            plan.facilityCores.Add(center + new Vector3(78f, 0f, -24f));
            plan.facilityCores.Add(center + new Vector3(0f, 0f, 76f));
            for (int i = 0; i < plan.facilityCores.Count; i++)
            {
                Vector3 core = plan.facilityCores[i];
                float height = i == 2 ? 176f : 142f;
                plan.buildings.Add(new AirCombatBuildingLot
                {
                    stableId = "facility.core-building." + i,
                    center = core + Vector3.up * (height * 0.5f),
                    size = new Vector3(46f, height, 46f),
                    yaw = ResolveFacadeYaw(
                        plan,
                        new Vector2(core.x, core.z)),
                    band = AirCombatBuildingBand.Facility,
                    archetype = AirCombatBuildingArchetype.Facility,
                    clusterId = 990,
                    visualVariant = i
                });
            }
        }

        static AirCombatCityReport Validate(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan,
            int attempts)
        {
            var report = new AirCombatCityReport
            {
                requestedSeed = settings.seed,
                resolvedSeed = plan.resolvedSeed,
                attempts = attempts,
                buildingCount = plan.buildings.Count,
                routeCount = plan.routes.Count,
                ingressCount = plan.ingresses.Count,
                mainCorridorWidth = settings.MainCorridorWidth,
                routesClear = true,
                alternateRouteAvailable = false,
                facilityReachable = settings.mission !=
                    AirCombatCityMission.FacilityAssault ||
                    plan.facilityCores.Count == 3
            };
            ValidateRoadGrid(settings, plan, report);
            ValidateCoverContinuity(settings, plan, report);
            ValidateTacticalRoles(plan, report);
            CombatDrivenCityPcgPlanner.Validate(settings, plan, report);
            int[,] quadrantBands = new int[4, 3];
            int centerLow = 0;
            int centerMedium = 0;
            bool[] skylineAnchors = new bool[4];
            float verticalSafety = settings.wingspan * 0.45f + 8f;
            float maneuverRadius = settings.ManeuverDiameter * 0.5f;
            for (int i = 0; i < plan.buildings.Count; i++)
            {
                AirCombatBuildingLot building = plan.buildings[i];
                switch (building.band)
                {
                    case AirCombatBuildingBand.High:
                        report.highBuildingCount++;
                        break;
                    case AirCombatBuildingBand.Medium:
                        report.mediumBuildingCount++;
                        break;
                }
                if (building.band != AirCombatBuildingBand.Facility)
                {
                    int quadrant = (building.center.x >= 0f ? 1 : 0) +
                                   (building.center.z >= 0f ? 2 : 0);
                    int bandIndex = building.band == AirCombatBuildingBand.Low
                        ? 0
                        : building.band == AirCombatBuildingBand.Medium ? 1 : 2;
                    quadrantBands[quadrant, bandIndex]++;
                    Vector2 position = new Vector2(
                        building.center.x,
                        building.center.z);
                    if (position.magnitude < maneuverRadius)
                    {
                        if (building.band == AirCombatBuildingBand.Low)
                            centerLow++;
                        else if (building.band == AirCombatBuildingBand.Medium)
                            centerMedium++;
                    }
                    if (building.band == AirCombatBuildingBand.High)
                    {
                        float top = building.center.y +
                                    building.size.y * 0.5f;
                        report.maximumTowerHeight = Mathf.Max(
                            report.maximumTowerHeight,
                            top);
                        if (top + verticalSafety >= settings.maximumAltitude)
                            skylineAnchors[quadrant] = true;
                    }
                }
            }
            for (int quadrant = 0; quadrant < skylineAnchors.Length; quadrant++)
            {
                if (skylineAnchors[quadrant])
                    report.skylineAnchorQuadrants++;
            }
            report.verticalOverflightGap = settings.maximumAltitude -
                                           report.maximumTowerHeight;
            report.highAltitudeBypassControlled =
                report.skylineAnchorQuadrants == 4 &&
                report.verticalOverflightGap < verticalSafety;
            report.heightMixDistributed = true;
            for (int quadrant = 0; quadrant < 4; quadrant++)
            {
                // Two low and two medium silhouettes already make the local
                // layer choice readable. Requiring three of each rejected
                // otherwise sound merged blocks without adding gameplay.
                if (quadrantBands[quadrant, 0] < 2 ||
                    quadrantBands[quadrant, 1] < 2 ||
                    quadrantBands[quadrant, 2] < 1)
                {
                    report.heightMixDistributed = false;
                }
            }
            report.centralHeightMixValid = centerLow >= 3 &&
                                           centerMedium >= 3;

            float minimumRadius = float.PositiveInfinity;
            int strategicRoutes = 0;
            for (int i = 0; i < plan.routes.Count; i++)
            {
                AirCombatFlightRoute route = plan.routes[i];
                if (route.kind == AirCombatRouteKind.MaskedFlank ||
                    route.kind == AirCombatRouteKind.LongRange)
                {
                    strategicRoutes++;
                }
                if (route.kind == AirCombatRouteKind.EnemyIngress)
                    continue;
                float routeRadius = MinimumTurnRadius(route.points);
                if (!float.IsPositiveInfinity(routeRadius))
                    minimumRadius = Mathf.Min(minimumRadius, routeRadius);
                if (!RouteClearOfBuildings(settings, route, plan.buildings))
                    report.routesClear = false;
            }
            report.alternateRouteAvailable = strategicRoutes >= 2;
            report.minimumTurnRadius = float.IsPositiveInfinity(minimumRadius)
                ? 100000f
                : minimumRadius;
            report.threeAltitudeLayersUseful =
                report.mediumBuildingCount >= 12 &&
                report.highBuildingCount >= 10;

            float minimumContact = float.PositiveInfinity;
            for (int i = 0; i < plan.ingresses.Count; i++)
            {
                minimumContact = Mathf.Min(
                    minimumContact,
                    plan.ingresses[i].warningSeconds);
            }
            report.firstContactSeconds = float.IsPositiveInfinity(minimumContact)
                ? 0f
                : minimumContact;
            report.checksum = ComputeChecksum(plan);

            bool mapScaleValid = settings.mapSize >= settings.weaponRange * 3f;
            bool turnValid = report.minimumTurnRadius >= settings.turnRadius * 0.9f;
            // A merged super-block replaces two to four former grid objects.
            // Validate occupied parcel mass instead of rejecting a denser city
            // merely because it uses fewer, larger building GameObjects.
            float equivalentBuildingCount = 0f;
            float referenceFootprint = Mathf.Max(
                1f,
                settings.buildingSpacing * settings.buildingSpacing * 0.46f);
            for (int buildingIndex = 0;
                 buildingIndex < plan.buildings.Count;
                 buildingIndex++)
            {
                AirCombatBuildingLot building = plan.buildings[buildingIndex];
                equivalentBuildingCount += Mathf.Max(
                    0.35f,
                    building.size.x * building.size.z / referenceFootprint);
            }
            bool buildingBudgetValid = equivalentBuildingCount >= 80f &&
                                       equivalentBuildingCount <= 650f &&
                                       report.buildingCount <= 520;
            bool contactValid = settings.mission !=
                                AirCombatCityMission.FacilityAssault
                ? report.firstContactSeconds >= 4.5f
                : report.firstContactSeconds >= 1.5f;
            report.valid = mapScaleValid
                && turnValid
                && buildingBudgetValid
                && report.routesClear
                && report.alternateRouteAvailable
                && report.threeAltitudeLayersUseful
                && report.heightMixDistributed
                && report.centralHeightMixValid
                && report.highAltitudeBypassControlled
                && report.facilityReachable
                && report.roadGridAligned
                && report.roadIntersectionCount >= 32
                && report.roadWidthsVaried
                && report.tacticalBlockCoverageValid
                && report.buildingRoadOverlapCount == 0
                && report.coverContinuityValid
                && report.recoveryDistrictCount == 2
                && report.tacticalRolesComplete
                && report.tacticalOpportunityNetworkValid
                && report.combatRegionsPhysical
                && !report.dominantRouteDetected
                && report.ingressCount >= 6
                && contactValid;

            if (!report.valid)
            {
                if (!mapScaleValid)
                    report.failureReason = "地图小于三倍武器射程。";
                else if (!turnValid)
                    report.failureReason = "航路转弯半径小于飞机设计值。";
                else if (!buildingBudgetValid)
                    report.failureReason = "建筑数量超出实验场预算。";
                else if (!report.routesClear)
                    report.failureReason = "建筑侵入了安全飞行走廊。";
                else if (!report.alternateRouteAvailable)
                    report.failureReason = "缺少遮挡侧路或远射路线。";
                else if (!report.threeAltitudeLayersUseful)
                    report.failureReason = "建筑高度不足以影响三层空域。";
                else if (!report.heightMixDistributed)
                    report.failureReason = "局部街区缺少低中高建筑混合。";
                else if (!report.centralHeightMixValid)
                    report.failureReason = "中央缺少能影响低空层的中等高度建筑。";
                else if (!report.highAltitudeBypassControlled)
                    report.failureReason =
                        "High-altitude layer can bypass all skyline cover.";
                else if (!report.facilityReachable)
                    report.failureReason = "设施突袭目标不完整。";
                else if (!report.roadGridAligned ||
                         report.roadIntersectionCount < 32)
                    report.failureReason = "城市道路没有严格对齐到道路模数。";
                else if (!report.roadWidthsVaried)
                    report.failureReason =
                        "City roads do not express tactical width variation.";
                else if (!report.tacticalBlockCoverageValid)
                    report.failureReason =
                        "Some city blocks have no tactical purpose or physical tuning.";
                else if (report.buildingRoadOverlapCount != 0)
                    report.failureReason = "建筑侵入了道路或人行道。";
                else if (!report.coverContinuityValid)
                    report.failureReason = "中央交战区到最近有效掩体的距离过长。";
                else if (report.recoveryDistrictCount != 2)
                    report.failureReason = "维修庭院没有形成完整的三面掩护。";
                else if (!report.tacticalRolesComplete)
                    report.failureReason = "城市缺少拉扯、强攻、掩体或危险区。";
                else if (!report.tacticalOpportunityNetworkValid)
                    report.failureReason =
                        "Tactical opportunities do not form a valid multi-exit network.";
                else if (!report.combatRegionsPhysical)
                    report.failureReason =
                        "Combat regions are missing measurable city geometry or route advantages.";
                else if (report.dominantRouteDetected)
                    report.failureReason =
                        "One tactical route dominates all alternatives.";
                else if (!contactValid)
                    report.failureReason = "敌机首次接触过早。";
                else
                    report.failureReason = "内部刷新兼容采样点数量不足。";
            }
            return report;
        }

        static void ValidateRoadGrid(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan,
            AirCombatCityReport report)
        {
            const float SidewalkWidth = 6.2f;
            float pitch = ResolveStreetPitch(settings);
            var northSouth = new List<AirCombatRoadStrip>(64);
            var eastWest = new List<AirCombatRoadStrip>(64);
            report.roadGridAligned = plan.roads.Count > 0;
            report.minimumRoadWidth = float.PositiveInfinity;
            for (int i = 0; i < plan.roads.Count; i++)
            {
                AirCombatRoadStrip road = plan.roads[i];
                report.minimumRoadWidth = Mathf.Min(
                    report.minimumRoadWidth,
                    road.width);
                report.maximumRoadWidth = Mathf.Max(
                    report.maximumRoadWidth,
                    road.width);
                bool vertical = Mathf.Abs(road.start.x - road.end.x) < 0.01f;
                bool horizontal = Mathf.Abs(road.start.z - road.end.z) < 0.01f;
                if (vertical == horizontal)
                {
                    report.roadGridAligned = false;
                    continue;
                }
                float coordinate = vertical ? road.start.x : road.start.z;
                float snapped = Mathf.Round(coordinate / pitch) * pitch;
                if (Mathf.Abs(coordinate - snapped) > 0.02f ||
                    road.width < 16f)
                {
                    report.roadGridAligned = false;
                }
                if (vertical)
                    northSouth.Add(road);
                else
                    eastWest.Add(road);
            }
            var intersections = new HashSet<string>();
            for (int x = 0; x < northSouth.Count; x++)
            for (int z = 0; z < eastWest.Count; z++)
            {
                AirCombatRoadStrip vertical = northSouth[x];
                AirCombatRoadStrip horizontal = eastWest[z];
                float intersectionX = vertical.start.x;
                float intersectionZ = horizontal.start.z;
                if (intersectionZ >= Mathf.Min(vertical.start.z, vertical.end.z) &&
                    intersectionZ <= Mathf.Max(vertical.start.z, vertical.end.z) &&
                    intersectionX >= Mathf.Min(horizontal.start.x, horizontal.end.x) &&
                    intersectionX <= Mathf.Max(horizontal.start.x, horizontal.end.x))
                {
                    intersections.Add(
                        Mathf.RoundToInt(intersectionX * 10f) + ":" +
                        Mathf.RoundToInt(intersectionZ * 10f));
                }
            }
            report.roadIntersectionCount = intersections.Count;
            if (float.IsPositiveInfinity(report.minimumRoadWidth))
                report.minimumRoadWidth = 0f;
            report.roadWidthsVaried = report.maximumRoadWidth -
                                      report.minimumRoadWidth >= 4f;

            report.tacticalBlockCount = plan.tacticalBlocks.Count;
            var regions = new HashSet<string>();
            var mergedGroups = new HashSet<string>();
            for (int i = 0; i < plan.tacticalBlocks.Count; i++)
            {
                CombatCityBlockPlan block = plan.tacticalBlocks[i];
                if (string.IsNullOrEmpty(block.primaryOpportunityId) ||
                    string.IsNullOrEmpty(block.tacticalRegionId))
                {
                    report.unassignedTacticalBlockCount++;
                }
                else
                {
                    regions.Add(block.tacticalRegionId);
                }
                if (block.mergeEast)
                {
                    report.removedInternalRoadSegments++;
                    if (block.role == CombatCityBlockRole.Occlusion)
                        report.occlusionMergedRoadSegments++;
                }
                if (block.mergeNorth)
                {
                    report.removedInternalRoadSegments++;
                    if (block.role == CombatCityBlockRole.Occlusion)
                        report.occlusionMergedRoadSegments++;
                }
                if (!string.IsNullOrEmpty(block.mergedGroupId) &&
                    block.mergedGroupId.StartsWith(
                        "merged-block-group.",
                        StringComparison.Ordinal))
                {
                    mergedGroups.Add(block.mergedGroupId);
                }
            }
            report.tacticalRegionCount = regions.Count;
            report.mergedBlockGroupCount = mergedGroups.Count;
            report.tacticalBlockCoverageValid =
                report.tacticalBlockCount == 64 &&
                report.unassignedTacticalBlockCount == 0 &&
                report.tacticalRegionCount >= 6 &&
                report.removedInternalRoadSegments >= 8 &&
                report.occlusionMergedRoadSegments >= 1;

            for (int b = 0; b < plan.buildings.Count; b++)
            {
                AirCombatBuildingLot building = plan.buildings[b];
                if (building.band == AirCombatBuildingBand.Facility)
                    continue;
                Vector2 center = new Vector2(building.center.x, building.center.z);
                Vector2 footprint = new Vector2(building.size.x, building.size.z);
                for (int r = 0; r < plan.roads.Count; r++)
                {
                    AirCombatRoadStrip road = plan.roads[r];
                    if (!FootprintIntersectsCorridor(
                            center,
                            footprint,
                            building.yaw,
                            new Vector2(road.start.x, road.start.z),
                            new Vector2(road.end.x, road.end.z),
                            road.width * 0.5f + SidewalkWidth,
                            out _))
                    {
                        continue;
                    }
                    report.buildingRoadOverlapCount++;
                    break;
                }
            }
        }

        static void ValidateCoverContinuity(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan,
            AirCombatCityReport report)
        {
            float radius = settings.ManeuverDiameter * 0.74f;
            float sampleStep = settings.buildingSpacing * 0.5f;
            float maximumGap = 0f;
            for (float x = -radius; x <= radius; x += sampleStep)
            for (float z = -radius; z <= radius; z += sampleStep)
            {
                Vector2 sample = new Vector2(x, z);
                if (sample.sqrMagnitude > radius * radius)
                    continue;
                float nearest = float.PositiveInfinity;
                for (int b = 0; b < plan.buildings.Count; b++)
                {
                    AirCombatBuildingLot building = plan.buildings[b];
                    if (building.band == AirCombatBuildingBand.Facility ||
                        building.size.y < settings.lowAltitude * 0.80f)
                    {
                        continue;
                    }
                    nearest = Mathf.Min(
                        nearest,
                        DistanceToBuildingFootprint(sample, building));
                }
                if (!float.IsPositiveInfinity(nearest))
                    maximumGap = Mathf.Max(maximumGap, nearest);
            }
            report.maximumCentralCoverGap = maximumGap;
            // The city is non-linear and the arcade ship needs room to turn.
            // A 2.4 second worst-case cover transition is still tactically
            // useful while avoiding seed churn over a few empty metres.
            report.coverContinuityValid =
                maximumGap <= settings.combatSpeed * 2.4f;
        }

        static float DistanceToBuildingFootprint(
            Vector2 point,
            AirCombatBuildingLot building)
        {
            float radians = building.yaw * Mathf.Deg2Rad;
            Vector2 local = WorldToFootprintLocal(
                point - new Vector2(building.center.x, building.center.z),
                Mathf.Cos(radians),
                Mathf.Sin(radians));
            float deltaX = Mathf.Max(
                Mathf.Abs(local.x) - building.size.x * 0.5f,
                0f);
            float deltaZ = Mathf.Max(
                Mathf.Abs(local.y) - building.size.z * 0.5f,
                0f);
            return Mathf.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
        }

        static void ValidateTacticalRoles(
            AirCombatCityPlan plan,
            AirCombatCityReport report)
        {
            bool kite = false;
            bool assault = false;
            bool occlusion = false;
            bool dangerPlaza = false;
            for (int i = 0; i < plan.volumes.Count; i++)
            {
                switch (plan.volumes[i].kind)
                {
                    case AirCombatVolumeKind.KiteLoop:
                        kite = true;
                        break;
                    case AirCombatVolumeKind.AssaultBreach:
                        assault = true;
                        break;
                    case AirCombatVolumeKind.OcclusionGate:
                        occlusion = true;
                        break;
                    case AirCombatVolumeKind.DangerPlaza:
                        dangerPlaza = true;
                        break;
                }
            }
            int[] recoveryParts = new int[2];
            for (int i = 0; i < plan.buildings.Count; i++)
            {
                int district = plan.buildings[i].clusterId - 970;
                if (district >= 0 && district < recoveryParts.Length)
                    recoveryParts[district]++;
            }
            for (int i = 0; i < recoveryParts.Length; i++)
            {
                if (recoveryParts[i] >= 3)
                    report.recoveryDistrictCount++;
            }
            report.tacticalRolesComplete =
                kite && assault && occlusion && dangerPlaza;
        }

        static bool RouteClearOfBuildings(
            AirCombatCitySettings settings,
            AirCombatFlightRoute route,
            List<AirCombatBuildingLot> buildings)
        {
            if (route == null || route.points == null)
                return false;
            for (int b = 0; b < buildings.Count; b++)
            {
                AirCombatBuildingLot building = buildings[b];
                if (building.band == AirCombatBuildingBand.Facility)
                    continue;
                Vector2 center = new Vector2(building.center.x, building.center.z);
                Vector2 footprint = new Vector2(
                    building.size.x,
                    building.size.z);
                for (int p = 1; p < route.points.Length; p++)
                {
                    if (!FootprintIntersectsCorridor(
                        center,
                        footprint,
                        building.yaw,
                        new Vector2(route.points[p - 1].x, route.points[p - 1].z),
                        new Vector2(route.points[p].x, route.points[p].z),
                        route.width * 0.5f,
                        out float segmentT))
                    {
                        continue;
                    }
                    float routeAltitude = Mathf.Lerp(
                        route.points[p - 1].y,
                        route.points[p].y,
                        segmentT);
                    float verticalSafety = settings.wingspan * 0.45f + 8f;
                    if (building.center.y + building.size.y * 0.5f +
                        verticalSafety >= routeAltitude)
                        return false;
                }
            }
            return true;
        }

        static float MinimumTurnRadius(Vector3[] points)
        {
            if (points == null || points.Length < 3)
                return float.PositiveInfinity;
            float result = float.PositiveInfinity;
            for (int i = 1; i < points.Length - 1; i++)
            {
                Vector2 a = new Vector2(points[i - 1].x, points[i - 1].z);
                Vector2 b = new Vector2(points[i].x, points[i].z);
                Vector2 c = new Vector2(points[i + 1].x, points[i + 1].z);
                float ab = Vector2.Distance(a, b);
                float bc = Vector2.Distance(b, c);
                float ca = Vector2.Distance(c, a);
                float twiceArea = Mathf.Abs(
                    (b.x - a.x) * (c.y - a.y) -
                    (b.y - a.y) * (c.x - a.x));
                if (twiceArea <= 0.01f)
                    continue;
                float radius = ab * bc * ca / (2f * twiceArea);
                result = Mathf.Min(result, radius);
            }
            return result;
        }

        static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
        {
            return DistanceToSegment(point, start, end, out _);
        }

        static float DistanceToSegment(
            Vector2 point,
            Vector2 start,
            Vector2 end,
            out float segmentT)
        {
            Vector2 delta = end - start;
            float lengthSquared = delta.sqrMagnitude;
            if (lengthSquared <= 0.0001f)
            {
                segmentT = 0f;
                return Vector2.Distance(point, start);
            }
            segmentT = Mathf.Clamp01(
                Vector2.Dot(point - start, delta) / lengthSquared);
            return Vector2.Distance(point, start + delta * segmentT);
        }

        static void AddRoad(
            AirCombatCityPlan plan,
            string id,
            AirCombatRouteKind kind,
            Vector3 start,
            Vector3 end,
            float width,
            int laneTiles = 1,
            bool dangerLane = false)
        {
            plan.roads.Add(new AirCombatRoadStrip
            {
                stableId = id,
                kind = kind,
                start = start,
                end = end,
                width = width,
                laneTiles = Mathf.Max(1, laneTiles),
                dangerLane = dangerLane
            });
        }

        static void AddRoute(
            AirCombatCityPlan plan,
            string id,
            AirCombatRouteKind kind,
            float width,
            params Vector3[] points)
        {
            plan.routes.Add(new AirCombatFlightRoute
            {
                stableId = id,
                kind = kind,
                width = width,
                points = points ?? Array.Empty<Vector3>()
            });
        }

        static void AddVolume(
            AirCombatCityPlan plan,
            string id,
            AirCombatVolumeKind kind,
            Vector3 center,
            Vector3 size)
        {
            plan.volumes.Add(new AirCombatTacticalVolume
            {
                stableId = id,
                kind = kind,
                center = center,
                size = size
            });
        }

        static int DeriveSeed(int seed, int attempt)
        {
            unchecked
            {
                uint value = (uint)seed + (uint)(attempt + 1) * 0x9E3779B9u;
                value ^= value >> 16;
                value *= 0x7FEB352Du;
                value ^= value >> 15;
                value *= 0x846CA68Bu;
                value ^= value >> 16;
                return (int)value;
            }
        }

        static int ComputeChecksum(AirCombatCityPlan plan)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + plan.resolvedSeed;
                hash = hash * 31 + (int)plan.mission;
                hash = hash * 31 + Mathf.RoundToInt(plan.mapSize * 10f);
                for (int i = 0; i < plan.buildings.Count; i++)
                {
                    AirCombatBuildingLot value = plan.buildings[i];
                    hash = hash * 31 + Mathf.RoundToInt(value.center.x * 10f);
                    hash = hash * 31 + Mathf.RoundToInt(value.center.z * 10f);
                    hash = hash * 31 + Mathf.RoundToInt(value.size.y * 10f);
                    hash = hash * 31 + (int)value.band;
                    hash = hash * 31 + value.clusterId;
                    hash = hash * 31 + value.visualVariant;
                    hash = hash * 31 + Mathf.RoundToInt(value.yaw * 10f);
                }
                for (int i = 0; i < plan.ingresses.Count; i++)
                {
                    AirCombatEnemyIngress value = plan.ingresses[i];
                    hash = hash * 31 + Mathf.RoundToInt(value.position.x * 10f);
                    hash = hash * 31 + Mathf.RoundToInt(value.position.z * 10f);
                    hash = hash * 31 + (int)value.kind;
                }
                for (int i = 0; i < plan.boundaryWalls.Count; i++)
                {
                    AirCombatBoundaryWallPlan value = plan.boundaryWalls[i];
                    hash = hash * 31 + Mathf.RoundToInt(value.center.x * 10f);
                    hash = hash * 31 + Mathf.RoundToInt(value.center.z * 10f);
                    hash = hash * 31 + Mathf.RoundToInt(value.size.x * 10f);
                    hash = hash * 31 + Mathf.RoundToInt(value.size.y * 10f);
                    hash = hash * 31 + Mathf.RoundToInt(value.size.z * 10f);
                }
                return hash;
            }
        }
    }
}
