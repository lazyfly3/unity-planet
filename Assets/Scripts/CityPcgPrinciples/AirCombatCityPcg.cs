using System;
using System.Collections.Generic;
using System.Threading;
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
        DominancePerch = 7
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

    /// <summary>
    /// 单个战术方格内部使用的确定性街区骨架。道路和合并缝先完成，
    /// 骨架只负责在最终街区内部提供固定楼位、统一朝向与永久净空。
    /// </summary>
    public enum CombatCityBlockLayoutPattern
    {
        StreetPerimeterCourt = 0,
        UCourtyard = 1,
        ParallelStreetWalls = 2,
        TwinTowerGate = 3,
        OpenManeuverBasin = 4,
        TerracedBuildingGroup = 5
    }

    /// <summary>
    /// 从正式楼房目录逐个审计得到的规划尺寸。PCG 不持有 Prefab，
    /// 只消费这一份无 Unity 对象引用的几何契约。
    /// </summary>
    [Serializable]
    public sealed class AirCombatBuildingModelMetric
    {
        public AirCombatBuildingBand band;
        public int catalogIndex;
        public Vector3 authoredSize = Vector3.one;
        public Vector3 colliderSize = Vector3.one;
        public Vector3 colliderCenter;
        [Range(0f, 1f)] public float groundSupportRatio = 1f;
        [Range(0f, 1f)] public float bodyCoverage = 1f;

        public float FootprintAspect => authoredSize.x /
                                        Mathf.Max(0.1f, authoredSize.z);
        public float Slenderness => authoredSize.y /
                                   Mathf.Max(0.1f,
                                       Mathf.Sqrt(authoredSize.x *
                                                  authoredSize.z));
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
        [Min(0.5f)]
        public float buildingDensity = 0.91f;

        [InspectorName("最多候选次数")]
        [Min(1)]
        public int maximumAttempts = 10;

        [Header("空中连廊")]
        [Min(0)] public int intraBlockSkybridgeTarget = 138;
        [Min(0)] public int crossBlockSkybridgeTarget = 12;
        [Min(0)] public int bossIntraBlockSkybridgeTarget = 202;
        [Min(0)] public int bossCrossBlockSkybridgeTarget = 18;
        [Min(1f)] public float skybridgeMinimumCenterDistance = 22f;
        [Min(1f)] public float skybridgeMaximumCenterDistance = 240f;
        [Min(1f)] public float skybridgeMinimumHeight = 38f;
        [Min(0f)] public float skybridgeFacadeEmbed = 9f;
        [Min(1)] public int skybridgeMaximumSegmentCount = 3;
        [Min(1)] public int skybridgeMaximumConnectionsPerBuilding = 7;
        [Min(1)] public int skybridgeLandmarkMaximumConnections = 10;

        [Header("空中电线与减速")]
        [Min(0f)] public float aerialCableDensityMultiplier = 1f;
        [Min(0)] public int aerialCableMinimumCount = 14;
        [Min(0)] public int aerialCableMaximumCount = 26;
        [Min(1f)] public float aerialCableMinimumCenterDistance = 76f;
        [Min(1f)] public float aerialCableMaximumCenterDistance = 310f;
        [Min(1f)] public float aerialCableMinimumOpenSpan = 54f;
        [Min(1f)] public float aerialCableMaximumOpenSpan = 248f;
        [Min(0f)] public float aerialCableSagRatio = 0.075f;
        [Min(0f)] public float aerialCableMinimumSag = 6f;
        [Min(0f)] public float aerialCableMaximumSag = 18f;
        [Min(1)] public int aerialCableMaximumConnectionsPerBuilding = 3;
        [Min(1)] public int aerialCableLandmarkMaximumConnections = 4;
        [Min(0f)] public float aerialCableVerticalSeparation = 2.7f;
        [Min(0.05f)] public float cableTriggerRadius = 1.05f;
        [Range(1, 16)] public int cableTriggerSegmentsPerCurve = 6;
        [Min(0f)] public float cableMinimumAffectedSpeed = 8f;
        [Range(0f, 0.9f)] public float playerCableSlowdown = 0.28f;
        [Range(0f, 0.9f)] public float enemyCableSlowdown = 0.14f;
        [Min(0.05f)] public float cableRepeatCooldown = 0.45f;
        [Min(0f)] public float playerCableSwayAmplitude = 1.55f;
        [Min(0f)] public float enemyCableSwayAmplitude = 0.85f;
        [Min(0.05f)] public float cableSwayDuration = 1.05f;

        [Header("环境陷阱分布")]
        [Min(1)] public int naturalStreetGaleCount = 1;
        [Range(0.25f, 1.5f)] public float naturalStreetGaleStrength = 1f;
        [Min(1)] public int magneticCourtyardCount = 2;
        [Range(0f, 1f)] public float environmentalTrapRandomness = 0.72f;
        [Range(0f, 1f)] public float environmentalTrapEdgeBias = 0.35f;
        [Min(0f)] public float environmentalTrapPreferredMinimumRadius = 250f;
        [Min(1f)] public float environmentalTrapPreferredMaximumRadius = 590f;
        [Min(0f)] public float environmentalTrapEdgeClearance = 120f;
        [Min(0f)] public float environmentalTrapMinimumSeparation = 260f;

        [Header("Combat-driven PCG")]
        public bool useVisualDistrictThemes = true;
        [HideInInspector]
        public bool removeUpperParameterLimits;
        public CombatCityDifficultyProfile combatDifficulty =
            new CombatCityDifficultyProfile();
        public CombatCityDifficultyProfile Difficulty =>
            combatDifficulty ?? (combatDifficulty =
                new CombatCityDifficultyProfile());

        // 由 AirCombatCityPcgLab 在每次规划前从正式目录重新审计。
        // 非序列化可避免把 Prefab 派生数据写进关卡资产；直接调用纯
        // 生成器的测试仍会走保守的通用模型回退。
        [NonSerialized]
        public AirCombatBuildingModelMetric[] buildingModelMetrics =
            Array.Empty<AirCombatBuildingModelMetric>();

        public float MainCorridorWidth => Mathf.Max(
            152f,
            turnRadius * 1.52f + wingspan * 0.4f) *
            Mathf.Lerp(1.18f, 0.82f, Difficulty.navigationChallenge);

        public float FlankCorridorWidth => Mathf.Max(
            118f,
            turnRadius * 1.22f + wingspan * 0.25f) *
            Mathf.Lerp(1.16f, 0.78f, Difficulty.navigationChallenge);

        public float LongRangeCorridorWidth => Mathf.Max(
            136f,
            turnRadius * 1.38f + wingspan * 0.25f) *
            Mathf.Lerp(1.20f, 0.80f, Difficulty.navigationChallenge);

        public float ManeuverDiameter => Mathf.Max(350f, turnRadius * 3.55f) *
            Mathf.Lerp(1.16f, 0.84f, Difficulty.navigationChallenge);

        public float RecoveryDiameter => Mathf.Max(200f, turnRadius * 2.1f) *
            Mathf.Lerp(0.85f, 1.25f, Difficulty.recoveryGenerosity);

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
                mapSize = removeUpperParameterLimits
                    ? Mathf.Max(1200f, mapSize)
                    : Mathf.Clamp(mapSize, 1200f, 2600f),
                combatSpeed = removeUpperParameterLimits
                    ? Mathf.Max(20f, combatSpeed)
                    : Mathf.Clamp(combatSpeed, 20f, 160f),
                turnRadius = removeUpperParameterLimits
                    ? Mathf.Max(40f, turnRadius)
                    : Mathf.Clamp(turnRadius, 40f, 300f),
                weaponRange = removeUpperParameterLimits
                    ? Mathf.Max(120f, weaponRange)
                    : Mathf.Clamp(weaponRange, 120f, 1200f),
                wingspan = removeUpperParameterLimits
                    ? Mathf.Max(4f, wingspan)
                    : Mathf.Clamp(wingspan, 4f, 80f),
                lowAltitude = removeUpperParameterLimits
                    ? Mathf.Max(30f, lowAltitude)
                    : Mathf.Clamp(lowAltitude, 30f, 120f),
                mediumAltitude = removeUpperParameterLimits
                    ? Mathf.Max(70f, mediumAltitude)
                    : Mathf.Clamp(mediumAltitude, 70f, 220f),
                highAltitude = removeUpperParameterLimits
                    ? Mathf.Max(120f, highAltitude)
                    : Mathf.Clamp(highAltitude, 120f, 300f),
                maximumAltitude = removeUpperParameterLimits
                    ? Mathf.Max(180f, maximumAltitude)
                    : Mathf.Clamp(maximumAltitude, 180f, 400f),
                buildingSpacing = removeUpperParameterLimits
                    ? Mathf.Max(48f, buildingSpacing)
                    : Mathf.Clamp(buildingSpacing, 48f, 96f),
                buildingDensity = removeUpperParameterLimits
                    ? Mathf.Max(0.5f, buildingDensity)
                    : Mathf.Clamp(buildingDensity, 0.5f, 0.95f),
                maximumAttempts = removeUpperParameterLimits
                    ? Mathf.Max(1, maximumAttempts)
                    : Mathf.Clamp(maximumAttempts, 1, 24),
                intraBlockSkybridgeTarget = removeUpperParameterLimits
                    ? Mathf.Max(0, intraBlockSkybridgeTarget)
                    : Mathf.Clamp(intraBlockSkybridgeTarget, 0, 200),
                crossBlockSkybridgeTarget = removeUpperParameterLimits
                    ? Mathf.Max(0, crossBlockSkybridgeTarget)
                    : Mathf.Clamp(crossBlockSkybridgeTarget, 0, 200),
                bossIntraBlockSkybridgeTarget = removeUpperParameterLimits
                    ? Mathf.Max(0, bossIntraBlockSkybridgeTarget)
                    : Mathf.Clamp(bossIntraBlockSkybridgeTarget, 0, 300),
                bossCrossBlockSkybridgeTarget = removeUpperParameterLimits
                    ? Mathf.Max(0, bossCrossBlockSkybridgeTarget)
                    : Mathf.Clamp(bossCrossBlockSkybridgeTarget, 0, 300),
                skybridgeMinimumCenterDistance = Mathf.Max(
                    1f,
                    skybridgeMinimumCenterDistance),
                skybridgeMaximumCenterDistance = Mathf.Max(
                    skybridgeMinimumCenterDistance + 1f,
                    skybridgeMaximumCenterDistance),
                skybridgeMinimumHeight = Mathf.Max(1f, skybridgeMinimumHeight),
                skybridgeFacadeEmbed = Mathf.Max(0f, skybridgeFacadeEmbed),
                skybridgeMaximumSegmentCount = Mathf.Max(
                    1,
                    skybridgeMaximumSegmentCount),
                skybridgeMaximumConnectionsPerBuilding = Mathf.Max(
                    1,
                    skybridgeMaximumConnectionsPerBuilding),
                skybridgeLandmarkMaximumConnections = Mathf.Max(
                    1,
                    skybridgeLandmarkMaximumConnections),
                aerialCableDensityMultiplier = removeUpperParameterLimits
                    ? Mathf.Max(0f, aerialCableDensityMultiplier)
                    : Mathf.Clamp(aerialCableDensityMultiplier, 0f, 4f),
                aerialCableMinimumCount = removeUpperParameterLimits
                    ? Mathf.Max(0, aerialCableMinimumCount)
                    : Mathf.Clamp(aerialCableMinimumCount, 0, 64),
                aerialCableMaximumCount = removeUpperParameterLimits
                    ? Mathf.Max(0, aerialCableMaximumCount)
                    : Mathf.Clamp(aerialCableMaximumCount, 0, 96),
                aerialCableMinimumCenterDistance = Mathf.Max(
                    1f,
                    aerialCableMinimumCenterDistance),
                aerialCableMaximumCenterDistance = Mathf.Max(
                    aerialCableMinimumCenterDistance + 1f,
                    aerialCableMaximumCenterDistance),
                aerialCableMinimumOpenSpan = Mathf.Max(
                    1f,
                    aerialCableMinimumOpenSpan),
                aerialCableMaximumOpenSpan = Mathf.Max(
                    aerialCableMinimumOpenSpan + 1f,
                    aerialCableMaximumOpenSpan),
                aerialCableSagRatio = Mathf.Max(0f, aerialCableSagRatio),
                aerialCableMinimumSag = Mathf.Max(0f, aerialCableMinimumSag),
                aerialCableMaximumSag = Mathf.Max(
                    aerialCableMinimumSag,
                    aerialCableMaximumSag),
                aerialCableMaximumConnectionsPerBuilding = Mathf.Max(
                    1,
                    aerialCableMaximumConnectionsPerBuilding),
                aerialCableLandmarkMaximumConnections = Mathf.Max(
                    1,
                    aerialCableLandmarkMaximumConnections),
                aerialCableVerticalSeparation = Mathf.Max(
                    0f,
                    aerialCableVerticalSeparation),
                cableTriggerRadius = removeUpperParameterLimits
                    ? Mathf.Max(0.05f, cableTriggerRadius)
                    : Mathf.Clamp(cableTriggerRadius, 0.05f, 4f),
                cableTriggerSegmentsPerCurve = Mathf.Clamp(
                    cableTriggerSegmentsPerCurve,
                    1,
                    16),
                cableMinimumAffectedSpeed = Mathf.Max(
                    0f,
                    cableMinimumAffectedSpeed),
                playerCableSlowdown = Mathf.Clamp(
                    playerCableSlowdown,
                    0f,
                    0.9f),
                enemyCableSlowdown = Mathf.Clamp(
                    enemyCableSlowdown,
                    0f,
                    0.9f),
                cableRepeatCooldown = Mathf.Max(0.05f, cableRepeatCooldown),
                playerCableSwayAmplitude = Mathf.Max(
                    0f,
                    playerCableSwayAmplitude),
                enemyCableSwayAmplitude = Mathf.Max(
                    0f,
                    enemyCableSwayAmplitude),
                cableSwayDuration = Mathf.Max(0.05f, cableSwayDuration),
                naturalStreetGaleCount = Mathf.Max(1, naturalStreetGaleCount),
                naturalStreetGaleStrength = Mathf.Clamp(
                    naturalStreetGaleStrength <= 0f
                        ? 1f
                        : naturalStreetGaleStrength,
                    0.25f,
                    1.5f),
                magneticCourtyardCount = Mathf.Max(1, magneticCourtyardCount),
                environmentalTrapRandomness = Mathf.Clamp01(
                    environmentalTrapRandomness),
                environmentalTrapEdgeBias = Mathf.Clamp01(
                    environmentalTrapEdgeBias),
                environmentalTrapPreferredMinimumRadius = Mathf.Max(
                    0f,
                    environmentalTrapPreferredMinimumRadius),
                environmentalTrapPreferredMaximumRadius = Mathf.Max(
                    1f,
                    environmentalTrapPreferredMaximumRadius),
                environmentalTrapEdgeClearance = Mathf.Max(
                    0f,
                    environmentalTrapEdgeClearance),
                environmentalTrapMinimumSeparation = Mathf.Max(
                    0f,
                    environmentalTrapMinimumSeparation),
                useVisualDistrictThemes = useVisualDistrictThemes,
                removeUpperParameterLimits = removeUpperParameterLimits,
                buildingModelMetrics = CloneBuildingModelMetrics(
                    buildingModelMetrics),
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
            copy.skybridgeMaximumCenterDistance = Mathf.Max(
                copy.skybridgeMinimumCenterDistance + 1f,
                copy.skybridgeMaximumCenterDistance);
            copy.skybridgeLandmarkMaximumConnections = Mathf.Max(
                copy.skybridgeMaximumConnectionsPerBuilding,
                copy.skybridgeLandmarkMaximumConnections);
            copy.aerialCableMaximumCount = Mathf.Max(
                copy.aerialCableMinimumCount,
                copy.aerialCableMaximumCount);
            copy.aerialCableMaximumCenterDistance = Mathf.Max(
                copy.aerialCableMinimumCenterDistance + 1f,
                copy.aerialCableMaximumCenterDistance);
            copy.aerialCableMaximumOpenSpan = Mathf.Max(
                copy.aerialCableMinimumOpenSpan + 1f,
                copy.aerialCableMaximumOpenSpan);
            copy.aerialCableMaximumSag = Mathf.Max(
                copy.aerialCableMinimumSag,
                copy.aerialCableMaximumSag);
            copy.aerialCableLandmarkMaximumConnections = Mathf.Max(
                copy.aerialCableMaximumConnectionsPerBuilding,
                copy.aerialCableLandmarkMaximumConnections);
            copy.environmentalTrapPreferredMaximumRadius = Mathf.Max(
                copy.environmentalTrapPreferredMinimumRadius + 1f,
                copy.environmentalTrapPreferredMaximumRadius);
            float minimumMap = Mathf.Max(
                1200f,
                copy.turnRadius * 12f,
                copy.weaponRange * 3.2f);
            copy.mapSize = copy.removeUpperParameterLimits
                ? Mathf.Max(copy.mapSize, minimumMap)
                : Mathf.Clamp(
                    Mathf.Max(copy.mapSize, minimumMap),
                    1200f,
                    2600f);
            return copy;
        }

        static AirCombatBuildingModelMetric[] CloneBuildingModelMetrics(
            AirCombatBuildingModelMetric[] source)
        {
            if (source == null || source.Length == 0)
                return Array.Empty<AirCombatBuildingModelMetric>();

            var copy = new AirCombatBuildingModelMetric[source.Length];
            for (int index = 0; index < source.Length; index++)
            {
                AirCombatBuildingModelMetric metric = source[index];
                if (metric == null)
                    continue;
                copy[index] = new AirCombatBuildingModelMetric
                {
                    band = metric.band,
                    catalogIndex = metric.catalogIndex,
                    authoredSize = metric.authoredSize,
                    colliderSize = metric.colliderSize,
                    colliderCenter = metric.colliderCenter,
                    groundSupportRatio = metric.groundSupportRatio,
                    bodyCoverage = metric.bodyCoverage
                };
            }
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
        [Range(0f, 1f)] public float targetDifficulty = 0.4f;
        [Range(0f, 1f)] public float generatedDifficulty;
        [Range(0f, 1f)] public float greedyRequestedDifficulty;
        [Range(0f, 1f)] public float greedyCandidateOpenness;
        public float greedyRemainingBudgetBefore;
        public float greedyRemainingBudgetAfter;
        public float greedyCandidateScore;
        public float greedyPredictedCityBefore;
        public float greedyPredictedCityAfter;
        public bool greedyDirectionSatisfied = true;
        public int localCorrectionCount;
        public int generationOrder = -1;
        public bool generatedForDifficulty;
        public bool excludedFromDifficulty;
        public bool mergeEast;
        public bool mergeNorth;
        public bool layoutResolved;
        public CombatCityBlockLayoutPattern layoutPattern;
        public float layoutYaw;
        public int layoutSlotCount;
        public int layoutOccupiedSlotCount;
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
        public int layoutSlotIndex = -1;
        public CombatCityBlockLayoutPattern layoutPattern;
        public Vector3 authoredModelSize;
        public float horizontalModelScale = 1f;
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
        public bool boundaryAirWallsValid;
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
        public bool cityDifficultyEvaluated;
        public bool cityDifficultyTargetMet;
        public bool cityDifficultyCoverageValid;
        public bool cityDifficultyFallbackUsed;
        public float targetAverageDifficulty;
        public float controlAverageDifficulty;
        public float plannedAverageDifficulty;
        public float plannedDifficultyAbsoluteError;
        public float plannedSafeCellRatio;
        public float plannedHighRiskCellRatio;
        public float plannedDifficultyFitError;
        public int plannedMaximumCellsToLowerThreat;
        public bool finalDifficultyEvaluated;
        public bool finalDifficultyTargetMet;
        public bool finalDifficultyCoverageValid;
        public bool finalDifficultyFallbackUsed;
        public float finalAverageDifficulty;
        public float finalDifficultyAbsoluteError;
        public float finalSafeCellRatio;
        public float finalHighRiskCellRatio;
        public float finalDifficultyFitError;
        public bool hardPlayabilityValid;
        public bool designTargetsMet;
        public bool degraded;
        public string degradationWarning = string.Empty;
        public string failureReason = string.Empty;

        public string Summary =>
            (valid ? (degraded ? "通过（降级）" : "通过") : "失败")
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
        // The formal assault objective is a modular runtime facility, not a
        // decorative city tower.  The planner owns a real empty pad for it so
        // post-instantiation systems never have to hide a clipping failure.
        public const float FacilityPadSize = 54f;
        public const float FacilityPadHeight = 44f;
        public const float FacilityPadMinimumSeparation = 92f;
        // Recovery courtyards are selected from parcel interiors rather than
        // arbitrary coordinates. This keeps their three real wall buildings
        // away from the street grid while still allowing stable seed variation.
        static readonly Vector2[] RecoveryParcelSlots =
        {
            new Vector2(-1.56f, -0.58f),
            new Vector2(-1.56f, 0.58f),
            new Vector2(-2.56f, -0.58f),
            new Vector2(-2.56f, 0.58f),
            new Vector2(-3.56f, -0.58f),
            new Vector2(-3.56f, 0.58f),
            new Vector2(1.56f, -0.58f),
            new Vector2(2.56f, -0.58f),
            new Vector2(2.56f, 0.58f),
            new Vector2(3.56f, -0.58f),
            new Vector2(3.56f, 0.58f)
        };

        struct RecoveryPocketCandidate
        {
            public Vector3 point;
            public float score;
            public int slotIndex;
        }

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
            return Generate(
                source,
                out report,
                CancellationToken.None);
        }

        public static AirCombatCityPlan Generate(
            AirCombatCitySettings source,
            out AirCombatCityReport report,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AirCombatCitySettings settings =
                (source ?? new AirCombatCitySettings()).ValidatedCopy();
            // 一个章节任务 Seed 只对应一座城市。maximumAttempts 现在表示
            // 同一座城市可执行的局部修正上限，不再派生多个 Seed 抽选整城。
            var random = new StableRandom(settings.seed);
            AirCombatCityPlan plan = BuildCandidate(
                settings,
                settings.seed,
                ref random,
                cancellationToken);
            // 仍然只生成这一座城市。难度反馈已经在逐格候选提交前
            // 完成；这里不再对整城执行“偏难就增高、偏易就降低”的
            // 事后修改，避免覆盖贪心选择的空间题目。
            report = Validate(settings, plan, 1);
            cancellationToken.ThrowIfCancellationRequested();
            AirCombatCityDifficultyEvaluation difficulty =
                AirCombatCityDifficultyPcg.Evaluate(settings, plan);
            cancellationToken.ThrowIfCancellationRequested();
            ApplyGeneratedDifficulty(plan, difficulty);
            AirCombatCityDifficultyPcg.ApplyToReport(
                difficulty,
                report,
                false);
            bool difficultyHardValid = difficulty != null &&
                                       difficulty.IsUsable &&
                                       difficulty.coverageValid;
            report.valid = report.valid && difficultyHardValid;
            if (!difficultyHardValid &&
                string.IsNullOrWhiteSpace(report.failureReason))
            {
                report.hardPlayabilityValid = false;
                report.failureReason =
                    "城市可飞区块覆盖不足，无法进行可靠的难度与通行性复核。";
            }
            else if (difficulty.closestResultFallback)
            {
                report.degraded = true;
                string warning =
                    "危险度与目标相差 " +
                    difficulty.absoluteTargetError.ToString("P1") +
                    "，超过15个百分点；已采用当前任务Seed有界修正后的最接近结果。";
                report.degradationWarning = string.IsNullOrWhiteSpace(
                    report.degradationWarning)
                    ? warning
                    : report.degradationWarning + " " + warning;
            }
            return plan;
        }

        static void ApplyGeneratedDifficulty(
            AirCombatCityPlan plan,
            AirCombatCityDifficultyEvaluation evaluation)
        {
            if (plan == null || evaluation == null)
                return;
            int count = Mathf.Min(plan.tacticalBlocks.Count,
                evaluation.cells.Count);
            for (int index = 0; index < count; index++)
            {
                CombatCityBlockPlan block = plan.tacticalBlocks[index];
                AirCombatCityDifficultyCell cell = evaluation.cells[index];
                if (block != null && cell != null)
                    block.generatedDifficulty = cell.survivalDifficulty;
            }
        }

        static bool IsDifficultyAdjustableBuilding(
            AirCombatBuildingLot building)
        {
            if (building == null || string.IsNullOrEmpty(building.stableId))
                return false;
            return building.stableId.StartsWith("building.large.",
                       StringComparison.Ordinal) ||
                   building.stableId.StartsWith("building.standard.",
                       StringComparison.Ordinal) ||
                   building.stableId.StartsWith("building.small-gapfill.",
                       StringComparison.Ordinal);
        }

        static AirCombatCityPlan BuildCandidate(
            AirCombatCitySettings settings,
            int resolvedSeed,
            ref StableRandom random,
            CancellationToken cancellationToken)
        {
            var plan = new AirCombatCityPlan
            {
                requestedSeed = settings.seed,
                resolvedSeed = resolvedSeed,
                mission = settings.mission,
                mapSize = settings.mapSize
            };

            cancellationToken.ThrowIfCancellationRequested();
            BuildTacticalSpace(settings, plan);
            CombatDrivenCityPcgPlanner.Populate(settings, plan);
            CombatDrivenCityPcgPlanner.BuildTacticalBlockLayout(settings, plan);
            BuildRoadNetwork(settings, plan);
            cancellationToken.ThrowIfCancellationRequested();
            BuildEnemyIngresses(settings, plan, ref random);
            if (settings.mission == AirCombatCityMission.FacilityAssault)
                BuildFacility(settings, plan);
            // 固定战术构件必须先进入几何快照，逐格候选才能真实看见
            // 恢复庭院、任务设施和法定遮挡塔对旧格枪线的影响。
            BuildLowUrbanIslands(settings, plan, ref random);
            BuildRecoveryDistricts(settings, plan, ref random);
            float targetDifficulty = AirCombatCityDifficultyPcg
                .ResolveTarget(settings.Difficulty).averageDifficulty;
            if (targetDifficulty < 0.66f)
            {
                EnsureCentralTacticalCover(settings, plan);
                EnsureCentralLowCover(settings, plan);
                PromoteCentralMediumCover(settings, plan, ref random);
                EnsureCentralCoverContinuity(settings, plan);
            }
            else
            {
                // 高危城市可以让大部分中心成为暴露盆地，但仍要预留
                // 少量低/中层参照物，否则三层空战会退化成没有高度
                // 选择的纯平地。它们在贪心开始前写入，因此后续格子
                // 会用更开放的候选抵消其危险度影响，而不是事后补楼。
                EnsureCentralLowCover(settings, plan, 9);
                PromoteCentralMediumCover(settings, plan, ref random);
            }
            EnsureMinimumQuadrantHeightMix(settings, plan);
            BuildBuildings(
                settings,
                plan,
                ref random,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            // 区域实体会优先绑定贪心器已经提交的楼体；后续只允许
            // 为法定战术角色补充少量构件，并由最终难度验收重新计入。
            CombatDrivenCityPcgPlanner.BuildPhysicalRegionFeatures(
                settings,
                plan);
            PromoteSkylineAnchors(settings, plan);
            CombatDrivenCityPcgPlanner.BindGeneratedFeatures(settings, plan);
            CombatDrivenCityPcgPlanner
                .RepairOcclusionContractsAfterFeaturePlacement(
                    settings,
                    plan);
            // 所有档位保留最小的低/中层轮廓，让三层空战仍有参照物；
            // 高危档只跳过连续掩体修补，不会因此把暴露盆地填满。
            EnsureFinalCentralHeightMix(settings, plan, ref random);
            if (targetDifficulty < 0.66f)
            {
                EnsureCentralCoverContinuity(settings, plan);
            }
            RefreshGrammarModelAssignments(settings, plan);
            if (settings.mission == AirCombatCityMission.FacilityAssault &&
                !FacilitySitesAreValid(settings, plan))
            {
                // 物理战术构件是在最初设施预留之后补入的。若它们占用
                // 了旧候选，不重抽整城，只在最终建筑快照上重新搜索
                // 三个合法设施点；找不到时仍由正式验证明确失败。
                plan.facilityCores.Clear();
                BuildFacility(settings, plan);
            }
            return plan;
        }

        static void RefreshGrammarModelAssignments(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan)
        {
            for (int index = 0; index < plan.buildings.Count; index++)
            {
                AirCombatBuildingLot lot = plan.buildings[index];
                if (lot == null || lot.layoutSlotIndex < 0)
                    continue;
                int stable = StableLayoutHash(
                    lot.stableId,
                    settings.seed,
                    lot.layoutSlotIndex);
                AirCombatBuildingModelMetric model = ResolveBestBuildingModel(
                    settings,
                    lot.band,
                    lot.size.x,
                    lot.size.z,
                    lot.size.y,
                    stable);
                if (model == null)
                    continue;
                ResolveModelConstrainedFootprint(
                    model,
                    lot.size.x,
                    lot.size.z,
                    out float width,
                    out float depth,
                    out float horizontalScale);
                lot.size = new Vector3(width, lot.size.y, depth);
                lot.visualVariant = model.catalogIndex;
                lot.authoredModelSize = model.authoredSize;
                lot.horizontalModelScale = horizontalScale;
            }
        }

        static void EnsureFinalCentralHeightMix(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan,
            ref StableRandom random)
        {
            PromoteCentralMediumCover(settings, plan, ref random);
            float centralRadius = settings.ManeuverDiameter * 0.5f;
            int low = 0;
            int medium = 0;
            for (int index = 0; index < plan.buildings.Count; index++)
            {
                AirCombatBuildingLot building = plan.buildings[index];
                if (new Vector2(building.center.x,
                        building.center.z).magnitude >= centralRadius)
                {
                    continue;
                }
                if (building.band == AirCombatBuildingBand.Low)
                    low++;
                else if (building.band == AirCombatBuildingBand.Medium)
                    medium++;
            }
            for (int index = 0;
                 medium < 3 && low > 3 && index < plan.buildings.Count;
                 index++)
            {
                AirCombatBuildingLot building = plan.buildings[index];
                Vector2 point = new Vector2(building.center.x,
                    building.center.z);
                if (point.magnitude >= centralRadius ||
                    building.band != AirCombatBuildingBand.Low ||
                    !IsDifficultyAdjustableBuilding(building))
                {
                    continue;
                }
                float height = Mathf.Clamp(settings.lowAltitude + 24f,
                    86f, settings.mediumAltitude - 18f);
                if (IsReservedForFlight(settings, plan, point, height,
                        new Vector2(building.size.x, building.size.z),
                        building.yaw))
                {
                    continue;
                }
                building.band = AirCombatBuildingBand.Medium;
                building.size = new Vector3(building.size.x,
                    height, building.size.z);
                building.center = new Vector3(building.center.x,
                    height * 0.5f, building.center.z);
                building.archetype = AirCombatBuildingArchetype.MidSlab;
                medium++;
                low--;
            }
            for (int index = 0;
                 low < 3 && medium > 3 && index < plan.buildings.Count;
                 index++)
            {
                AirCombatBuildingLot building = plan.buildings[index];
                Vector2 point = new Vector2(building.center.x,
                    building.center.z);
                if (point.magnitude >= centralRadius ||
                    building.band != AirCombatBuildingBand.Medium ||
                    (!IsDifficultyAdjustableBuilding(building) &&
                     !building.stableId.StartsWith(
                         "building.central-tactical-infill.",
                         StringComparison.Ordinal) &&
                     !building.stableId.StartsWith(
                         "building.central-cluster.",
                         StringComparison.Ordinal)))
                {
                    continue;
                }
                float height = 46f + low * 4f;
                building.band = AirCombatBuildingBand.Low;
                building.size = new Vector3(building.size.x,
                    height, building.size.z);
                building.center = new Vector3(building.center.x,
                    height * 0.5f, building.center.z);
                building.archetype = AirCombatBuildingArchetype.LowBlock;
                low++;
                medium--;
            }
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
                1.30f,
                2.05f,
                settings.Difficulty.navigationChallenge);
            maskedDetour = ClampMaskedDetourToShortcutBudget(
                settings,
                sideX,
                connectorZ,
                upperCombatAltitude,
                maskedDetour);
            float maskedX = -sideX * maskedDetour;
            float exposureX = sideX * 0.20f;
            Vector3 kiteCenter = new Vector3(
                streetPitch * 1.50f,
                settings.mediumAltitude,
                streetPitch * 0.50f);
            Vector3 recoveryPocketSize = new Vector3(
                streetPitch * 0.67f * Mathf.Lerp(
                    0.78f, 1.28f, settings.Difficulty.recoveryGenerosity),
                settings.maximumAltitude * 0.72f,
                streetPitch * 0.62f * Mathf.Lerp(
                    0.78f, 1.28f, settings.Difficulty.recoveryGenerosity));
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
                        5.0f, 10.5f, settings.Difficulty.exposurePressure)));

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
                1.68f,
                1.14f,
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

            List<Vector3> recoveryPockets = ResolveRecoveryPocketLocations(
                settings,
                plan,
                plan.resolvedSeed,
                streetPitch,
                recoveryPocketSize,
                kiteCenter);
            AddVolume(
                plan,
                "volume.recovery.west",
                AirCombatVolumeKind.RecoveryPocket,
                recoveryPockets[0],
                recoveryPocketSize);
            AddVolume(
                plan,
                "volume.recovery.east",
                AirCombatVolumeKind.RecoveryPocket,
                recoveryPockets[1],
                recoveryPocketSize);
            for (int recoveryIndex = 2;
                 recoveryIndex < recoveryPockets.Count;
                 recoveryIndex++)
            {
                AddVolume(
                    plan,
                    "volume.recovery.extra." +
                    (recoveryIndex - 2).ToString("D2"),
                    AirCombatVolumeKind.RecoveryPocket,
                    recoveryPockets[recoveryIndex],
                    recoveryPocketSize);
            }
        }

        static float ClampMaskedDetourToShortcutBudget(
            AirCombatCitySettings settings,
            float sideX,
            float connectorZ,
            float upperCombatAltitude,
            float requestedDetour)
        {
            float half = settings.mapSize * 0.5f;
            Vector3 south = new Vector3(
                0f,
                settings.lowAltitude,
                -half + 90f);
            Vector3 north = new Vector3(
                0f,
                settings.mediumAltitude,
                half - 90f);
            float exposureX = sideX * 0.20f;
            float exposureLength = SixPointRouteLength(
                south,
                new Vector3(
                    exposureX * 0.52f,
                    settings.mediumAltitude,
                    -connectorZ),
                new Vector3(
                    exposureX,
                    upperCombatAltitude,
                    -connectorZ * 0.55f),
                new Vector3(
                    exposureX,
                    upperCombatAltitude,
                    connectorZ * 0.55f),
                new Vector3(
                    exposureX * 0.52f,
                    settings.mediumAltitude,
                    connectorZ),
                north);
            float maximumSaving =
                CombatDrivenCityPcgPlanner
                    .MaximumExposureShortcutSavingRatio - 0.002f;
            float maximumMaskedLength = exposureLength /
                                        Mathf.Max(0.01f, 1f - maximumSaving);
            if (MaskedRouteLength(
                    settings,
                    sideX,
                    connectorZ,
                    requestedDetour,
                    south,
                    north) <= maximumMaskedLength)
            {
                return requestedDetour;
            }

            float lower = 0f;
            float upper = requestedDetour;
            for (int iteration = 0; iteration < 16; iteration++)
            {
                float candidate = (lower + upper) * 0.5f;
                if (MaskedRouteLength(
                        settings,
                        sideX,
                        connectorZ,
                        candidate,
                        south,
                        north) <= maximumMaskedLength)
                {
                    lower = candidate;
                }
                else
                {
                    upper = candidate;
                }
            }
            return lower;
        }

        static float MaskedRouteLength(
            AirCombatCitySettings settings,
            float sideX,
            float connectorZ,
            float detour,
            Vector3 south,
            Vector3 north)
        {
            float maskedX = -sideX * detour;
            return SixPointRouteLength(
                south,
                new Vector3(
                    maskedX * 0.52f,
                    settings.lowAltitude,
                    -connectorZ),
                new Vector3(
                    maskedX,
                    settings.mediumAltitude,
                    -connectorZ * 0.55f),
                new Vector3(
                    maskedX,
                    settings.mediumAltitude,
                    connectorZ * 0.55f),
                new Vector3(
                    maskedX * 0.52f,
                    settings.mediumAltitude,
                    connectorZ),
                north);
        }

        static float SixPointRouteLength(
            Vector3 first,
            Vector3 second,
            Vector3 third,
            Vector3 fourth,
            Vector3 fifth,
            Vector3 sixth)
        {
            return Vector3.Distance(first, second) +
                   Vector3.Distance(second, third) +
                   Vector3.Distance(third, fourth) +
                   Vector3.Distance(fourth, fifth) +
                   Vector3.Distance(fifth, sixth);
        }

        static List<Vector3> ResolveRecoveryPocketLocations(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan,
            int resolvedSeed,
            float streetPitch,
            Vector3 pocketSize,
            Vector3 kiteCenter)
        {
            ResolveRecoveryPocketPair(
                settings,
                plan,
                resolvedSeed,
                streetPitch,
                pocketSize,
                kiteCenter,
                out Vector3 westRecovery,
                out Vector3 eastRecovery);
            westRecovery.y = settings.mediumAltitude;
            eastRecovery.y = settings.mediumAltitude;
            int requested = Mathf.Max(2, settings.magneticCourtyardCount);
            var selected = new List<Vector3>(Mathf.Min(
                requested,
                RecoveryParcelSlots.Length))
            {
                westRecovery,
                eastRecovery
            };
            if (requested <= 2)
                return selected;

            float minimumRadius = settings.environmentalTrapPreferredMinimumRadius;
            float maximumRadius = Mathf.Max(
                minimumRadius + 1f,
                settings.environmentalTrapPreferredMaximumRadius);
            float targetRadius = Mathf.Lerp(
                minimumRadius,
                maximumRadius,
                settings.environmentalTrapEdgeBias);
            float half = settings.mapSize * 0.5f;
            float pocketExtent = Mathf.Max(pocketSize.x, pocketSize.z) * 0.5f;
            var candidates = new List<RecoveryPocketCandidate>(
                RecoveryParcelSlots.Length);
            for (int slotIndex = 0;
                 slotIndex < RecoveryParcelSlots.Length;
                 slotIndex++)
            {
                Vector2 slot = RecoveryParcelSlots[slotIndex] * streetPitch;
                if (!IsSafeRecoverySlot(
                        slot,
                        pocketSize,
                        kiteCenter,
                        settings,
                        plan))
                    continue;
                Vector3 point = new Vector3(
                    slot.x,
                    settings.mediumAltitude,
                    slot.y);
                if (Vector3.Distance(point, westRecovery) < 1f ||
                    Vector3.Distance(point, eastRecovery) < 1f)
                {
                    continue;
                }
                float edgeGap = half -
                                Mathf.Max(Mathf.Abs(slot.x), Mathf.Abs(slot.y)) -
                                pocketExtent;
                float randomScore = StableNoise01(
                                        resolvedSeed,
                                        slotIndex * 263 + 1709) * 2f - 1f;
                candidates.Add(new RecoveryPocketCandidate
                {
                    point = point,
                    slotIndex = slotIndex,
                    score = ScoreTrapRadius(
                                slot.magnitude,
                                minimumRadius,
                                maximumRadius,
                                targetRadius) -
                            Mathf.Max(
                                0f,
                                settings.environmentalTrapEdgeClearance -
                                edgeGap) * 5f +
                            randomScore * streetPitch * 1.25f *
                            settings.environmentalTrapRandomness
                });
            }
            candidates.Sort((first, second) =>
            {
                int byScore = second.score.CompareTo(first.score);
                return byScore != 0
                    ? byScore
                    : first.slotIndex.CompareTo(second.slotIndex);
            });

            AddRecoveryCandidates(
                selected,
                candidates,
                requested,
                settings.environmentalTrapMinimumSeparation);
            // Separation is a preference. If the requested count cannot fit at
            // that spacing, fill remaining distinct legal parcels rather than
            // silently reducing the authored trap count.
            AddRecoveryCandidates(selected, candidates, requested, 0f);
            return selected;
        }

        static void AddRecoveryCandidates(
            List<Vector3> selected,
            List<RecoveryPocketCandidate> candidates,
            int requested,
            float minimumSeparation)
        {
            for (int candidateIndex = 0;
                 candidateIndex < candidates.Count && selected.Count < requested;
                 candidateIndex++)
            {
                Vector3 point = candidates[candidateIndex].point;
                bool available = true;
                for (int selectedIndex = 0;
                     selectedIndex < selected.Count;
                     selectedIndex++)
                {
                    float distance = Vector3.Distance(
                        Vector3.ProjectOnPlane(point, Vector3.up),
                        Vector3.ProjectOnPlane(
                            selected[selectedIndex],
                            Vector3.up));
                    if (distance < 1f || distance < minimumSeparation)
                    {
                        available = false;
                        break;
                    }
                }
                if (available)
                    selected.Add(point);
            }
        }

        static void ResolveRecoveryPocketPair(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan,
            int resolvedSeed,
            float streetPitch,
            Vector3 pocketSize,
            Vector3 kiteCenter,
            out Vector3 westRecovery,
            out Vector3 eastRecovery)
        {
            float minimumRadius = settings.environmentalTrapPreferredMinimumRadius;
            float maximumRadius = Mathf.Max(
                minimumRadius + 1f,
                settings.environmentalTrapPreferredMaximumRadius);
            float targetRadius = Mathf.Lerp(
                minimumRadius,
                maximumRadius,
                settings.environmentalTrapEdgeBias);
            float half = settings.mapSize * 0.5f;
            float pocketExtent = Mathf.Max(pocketSize.x, pocketSize.z) * 0.5f;
            float bestScore = float.NegativeInfinity;
            westRecovery = new Vector3(
                -streetPitch * 2.56f,
                0f,
                streetPitch * 0.58f);
            eastRecovery = new Vector3(
                streetPitch * 2.56f,
                0f,
                -streetPitch * 0.58f);

            for (int westIndex = 0; westIndex < RecoveryParcelSlots.Length;
                 westIndex++)
            {
                Vector2 westSlot = RecoveryParcelSlots[westIndex];
                if (westSlot.x >= 0f)
                    continue;
                Vector2 west = westSlot * streetPitch;
                if (!IsSafeRecoverySlot(
                        west,
                        pocketSize,
                        kiteCenter,
                        settings,
                        plan))
                {
                    continue;
                }

                for (int eastIndex = 0;
                     eastIndex < RecoveryParcelSlots.Length;
                     eastIndex++)
                {
                    Vector2 eastSlot = RecoveryParcelSlots[eastIndex];
                    if (eastSlot.x <= 0f)
                        continue;
                    Vector2 east = eastSlot * streetPitch;
                    if (!IsSafeRecoverySlot(
                            east,
                            pocketSize,
                            kiteCenter,
                            settings,
                            plan))
                    {
                        continue;
                    }

                    float westRadius = west.magnitude;
                    float eastRadius = east.magnitude;
                    float westEdgeGap = half -
                                        Mathf.Max(Mathf.Abs(west.x), Mathf.Abs(west.y)) -
                                        pocketExtent;
                    float eastEdgeGap = half -
                                        Mathf.Max(Mathf.Abs(east.x), Mathf.Abs(east.y)) -
                                        pocketExtent;
                    float separation = Vector2.Distance(west, east);
                    float score = ScoreTrapRadius(
                                      westRadius,
                                      minimumRadius,
                                      maximumRadius,
                                      targetRadius) +
                                  ScoreTrapRadius(
                                      eastRadius,
                                      minimumRadius,
                                      maximumRadius,
                                      targetRadius);
                    score -= Mathf.Max(
                                 0f,
                                 settings.environmentalTrapEdgeClearance -
                                 westEdgeGap) * 5f;
                    score -= Mathf.Max(
                                 0f,
                                 settings.environmentalTrapEdgeClearance -
                                 eastEdgeGap) * 5f;
                    score -= Mathf.Max(
                                 0f,
                                 settings.environmentalTrapMinimumSeparation -
                                 separation) * 6f;
                    float randomScore = StableNoise01(
                                            resolvedSeed,
                                            westIndex * 97 + eastIndex * 193 + 41) *
                                        2f - 1f;
                    score += randomScore * streetPitch * 1.25f *
                             settings.environmentalTrapRandomness;
                    if (score <= bestScore)
                        continue;
                    bestScore = score;
                    westRecovery = new Vector3(west.x, 0f, west.y);
                    eastRecovery = new Vector3(east.x, 0f, east.y);
                }
            }
        }

        static bool IsSafeRecoverySlot(
            Vector2 point,
            Vector3 pocketSize,
            Vector3 kiteCenter,
            AirCombatCitySettings settings,
            AirCombatCityPlan plan)
        {
            float half = settings.mapSize * 0.5f;
            float extent = Mathf.Max(pocketSize.x, pocketSize.z) * 0.5f;
            if (Mathf.Max(Mathf.Abs(point.x), Mathf.Abs(point.y)) + extent >=
                half - 24f)
            {
                return false;
            }

            Vector2 kite = new Vector2(kiteCenter.x, kiteCenter.z);
            float kiteClearance = settings.turnRadius * 1.72f + extent;
            return Vector2.Distance(point, kite) >= kiteClearance &&
                   RecoveryWallsClearFlightRoutes(point, plan);
        }

        static bool RecoveryWallsClearFlightRoutes(
            Vector2 center,
            AirCombatCityPlan plan)
        {
            if (plan == null || plan.routes == null)
                return true;
            float openSign = center.x < 0f ? -1f : 1f;
            Vector2[] wallCenters =
            {
                center + new Vector2(-openSign * 44f, 0f),
                center + new Vector2(-openSign * 12f, -42f),
                center + new Vector2(-openSign * 12f, 42f)
            };
            Vector2[] wallFootprints =
            {
                new Vector2(110f, 18f),
                new Vector2(78f, 18f),
                new Vector2(78f, 18f)
            };
            float[] wallYaws =
            {
                openSign > 0f ? 90f : -90f,
                0f,
                180f
            };
            for (int routeIndex = 0;
                 routeIndex < plan.routes.Count;
                 routeIndex++)
            {
                AirCombatFlightRoute route = plan.routes[routeIndex];
                if (route == null || route.points == null)
                    continue;
                for (int segment = 1;
                     segment < route.points.Length;
                     segment++)
                {
                    Vector2 start = new Vector2(
                        route.points[segment - 1].x,
                        route.points[segment - 1].z);
                    Vector2 end = new Vector2(
                        route.points[segment].x,
                        route.points[segment].z);
                    for (int wallIndex = 0;
                         wallIndex < wallCenters.Length;
                         wallIndex++)
                    {
                        if (FootprintIntersectsCorridor(
                                wallCenters[wallIndex],
                                wallFootprints[wallIndex],
                                wallYaws[wallIndex],
                                start,
                                end,
                                route.width * 0.5f,
                                out _))
                        {
                            return false;
                        }
                    }
                }
            }
            return true;
        }

        static float ScoreTrapRadius(
            float radius,
            float minimumRadius,
            float maximumRadius,
            float targetRadius)
        {
            float outside = radius < minimumRadius
                ? minimumRadius - radius
                : radius > maximumRadius
                    ? radius - maximumRadius
                    : 0f;
            return -Mathf.Abs(radius - targetRadius) - outside * 3f;
        }

        static float StableNoise01(int seed, int salt)
        {
            unchecked
            {
                uint value = (uint)seed ^ ((uint)salt + 0x9E3779B9u);
                value ^= value >> 16;
                value *= 0x7FEB352Du;
                value ^= value >> 15;
                value *= 0x846CA68Bu;
                value ^= value >> 16;
                return (value & 0x00FFFFFFu) / 16777216f;
            }
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
                        east,
                        northSouthKind);
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
                        north,
                        eastWestKind);
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
            CombatCityBlockPlan second,
            AirCombatRouteKind roadKind)
        {
            float firstScale = first != null ? first.roadWidthScale : 1f;
            float secondScale = second != null ? second.roadWidthScale : firstScale;
            float localScale = (firstScale + secondScale) * 0.5f;
            float legibility = settings.Difficulty.routeLegibility;
            float hierarchyScale = roadKind == AirCombatRouteKind.Main
                ? Mathf.Lerp(0.94f, 1.18f, legibility)
                : roadKind == AirCombatRouteKind.MaskedFlank
                    ? Mathf.Lerp(1.04f, 0.88f, legibility)
                    : Mathf.Lerp(1.02f, 0.92f, legibility);
            return Mathf.Clamp(
                baseWidth * localScale * hierarchyScale *
                settings.Difficulty.roadWidthScale,
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
                float authoredAngle =
                    missionOffset + i * (360f / ingressDirectionCount) +
                    random.Range(-4f, 4f);
                AirCombatEnemyLaneKind kind = i % 3 == 0
                    ? AirCombatEnemyLaneKind.Suicide
                    : AirCombatEnemyLaneKind.Ranged;
                float altitude = kind == AirCombatEnemyLaneKind.Suicide
                    ? settings.lowAltitude
                    : (i & 1) == 0
                        ? settings.highAltitude
                        : settings.mediumAltitude;
                Vector3 position = Vector3.zero;
                for (int candidateIndex = 0;
                     candidateIndex < 17;
                     candidateIndex++)
                {
                    float offset = candidateIndex == 0
                        ? 0f
                        : (candidateIndex % 2 == 1 ? 1f : -1f) *
                          Mathf.Ceil(candidateIndex * 0.5f) * 7.5f;
                    float angle = authoredAngle + offset;
                    float radians = angle * Mathf.Deg2Rad;
                    Vector3 candidate = new Vector3(
                        Mathf.Sin(radians) * radius,
                        altitude,
                        Mathf.Cos(radians) * radius);
                    if (EnemyIngressIntersectsRecoveryDistrict(
                            plan,
                            candidate))
                    {
                        continue;
                    }
                    position = candidate;
                    break;
                }
                if (position.sqrMagnitude < 0.001f)
                {
                    // The angular search is deliberately broad enough for the
                    // authored two-courtyard layout. Preserve a deterministic
                    // final candidate if a future profile consumes the whole
                    // perimeter; validation will still reject an unsafe plan.
                    float radians = authoredAngle * Mathf.Deg2Rad;
                    position = new Vector3(
                        Mathf.Sin(radians) * radius,
                        altitude,
                        Mathf.Cos(radians) * radius);
                }
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
            ref StableRandom random,
            CancellationToken cancellationToken)
        {
            float half = settings.mapSize * 0.5f;
            float spacing = settings.buildingSpacing;
            int count = Mathf.FloorToInt((settings.mapSize - spacing) / spacing);
            float start = -0.5f * (count - 1) * spacing;
            int buildingIndex = 0;
            for (int index = 0; index < plan.tacticalBlocks.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CombatCityBlockPlan block = plan.tacticalBlocks[index];
                block.generatedForDifficulty = false;
                block.generationOrder = -1;
                block.generatedDifficulty = 0f;
                block.greedyRequestedDifficulty = block.targetDifficulty;
                block.greedyCandidateOpenness = 0f;
                block.greedyRemainingBudgetBefore = 0f;
                block.greedyRemainingBudgetAfter = 0f;
                block.greedyCandidateScore = 0f;
                block.greedyPredictedCityBefore = 0f;
                block.greedyPredictedCityAfter = 0f;
                block.greedyDirectionSatisfied = true;
                block.localCorrectionCount = 0;
            }

            // 外围是固定的最高楼城市边界，不参与贪心难度平均；先把它
            // 放进几何上下文，避免内城完成后才出现的外围楼反向改变枪线。
            var perimeterBlocks = new List<CombatCityBlockPlan>(28);
            for (int index = 0; index < plan.tacticalBlocks.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CombatCityBlockPlan block = plan.tacticalBlocks[index];
                if (!block.excludedFromDifficulty)
                    continue;
                perimeterBlocks.Add(block);
                BuildBuildingsForBlock(settings, plan, block,
                    block.buildingDensityScale,
                    block.buildingHeightScale,
                    ref random, ref buildingIndex, half, spacing, count,
                    start);
            }

            int generationOrder = 0;
            AirCombatCityDifficultyEvaluation committedEvaluation = null;
            int greedyBlockCount = CountGreedyBlocks(plan);
            for (int generatedCount = 0;
                 generatedCount < greedyBlockCount;
                 generatedCount++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CombatCityBlockPlan tacticalBlock =
                    SelectNextGreedyFrontierBlock(plan,
                        ResolveFrontierPressureDirection(
                            settings, committedEvaluation));
                if (tacticalBlock == null)
                    break;

                GreedyBudget budget = ResolveGreedyBudget(
                    settings, plan, committedEvaluation, tacticalBlock);
                GreedyBlockCandidate chosen = SelectGreedyBlockCandidate(
                    settings, plan, tacticalBlock, budget,
                    random, buildingIndex, half, spacing, count, start,
                    false, cancellationToken);
                if (chosen == null)
                    continue;

                plan.buildings.AddRange(chosen.buildings);
                random = chosen.randomAfter;
                buildingIndex = chosen.buildingIndexAfter;
                tacticalBlock.buildingDensityScale = chosen.densityScale;
                tacticalBlock.buildingHeightScale = chosen.heightScale;
                tacticalBlock.layoutPattern = chosen.layoutPattern;
                tacticalBlock.layoutYaw = chosen.layoutYaw;
                tacticalBlock.layoutResolved = true;
                tacticalBlock.layoutSlotCount = chosen.layoutSlotCount;
                tacticalBlock.greedyRequestedDifficulty =
                    budget.requestedDifficulty;
                tacticalBlock.greedyCandidateOpenness = chosen.openness;
                tacticalBlock.greedyRemainingBudgetBefore =
                    budget.requiredRemainingAverage;
                tacticalBlock.greedyRemainingBudgetAfter =
                    chosen.remainingRequiredAverage;
                tacticalBlock.greedyCandidateScore = chosen.score;
                tacticalBlock.greedyPredictedCityBefore =
                    budget.predictedWholeCityBefore;
                tacticalBlock.greedyPredictedCityAfter =
                    chosen.predictedWholeCityDifficulty;
                tacticalBlock.greedyDirectionSatisfied =
                    chosen.directionSatisfied;
                tacticalBlock.localCorrectionCount = chosen.candidatesTested;
                tacticalBlock.layoutOccupiedSlotCount =
                    chosen.layoutOccupiedSlotCount;
                tacticalBlock.generationOrder = generationOrder++;
                tacticalBlock.generatedForDifficulty = true;
                committedEvaluation = chosen.evaluation;
                ApplyGeneratedDifficulty(plan, committedEvaluation);
            }

            TryBacktrackHighestImpactGreedyBlocks(settings, plan,
                ref random, ref buildingIndex, half, spacing, count, start,
                ref committedEvaluation, cancellationToken);
            TryRepairGreedyCity(settings, plan,
                ref random, ref buildingIndex, half, spacing, count, start,
                ref committedEvaluation, cancellationToken);

            // 外围只记录在内部贪心完成之后，方便编辑器按真实扩张顺序
            // 显示；它的几何从第一步起已经参与全部候选枪线判断。
            perimeterBlocks.Sort(CompareBlocksCenterOut);
            for (int index = 0; index < perimeterBlocks.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CombatCityBlockPlan block = perimeterBlocks[index];
                block.generationOrder = generationOrder++;
                block.generatedForDifficulty = true;
            }
            ApplyGeneratedDifficulty(plan,
                AirCombatCityDifficultyPcg.Evaluate(settings, plan));
            cancellationToken.ThrowIfCancellationRequested();
        }

        sealed class GreedyBlockCandidate
        {
            public readonly List<AirCombatBuildingLot> buildings =
                new List<AirCombatBuildingLot>(16);
            public StableRandom randomAfter;
            public int buildingIndexAfter;
            public float densityScale;
            public float heightScale;
            public float openness;
            public float requestedOpenness;
            public float score;
            public float predictedWholeCityDifficulty;
            public bool directionSatisfied;
            public float remainingRequiredAverage;
            public int candidatesTested;
            public CombatCityBlockLayoutPattern layoutPattern;
            public float layoutYaw;
            public int layoutSlotCount;
            public int layoutOccupiedSlotCount;
            public int requestedOccupiedSlotCount;
            public int rejectedFlightSlotCount;
            public int rejectedOverlapSlotCount;
            public AirCombatCityDifficultyEvaluation evaluation;
        }

        struct BlockBuildRealization
        {
            public int slotCount;
            public int requestedOccupied;
            public int attemptedSlots;
            public int created;
            public int rejectedBounds;
            public int rejectedFlight;
            public int rejectedOverlap;

            public float Fulfillment => requestedOccupied > 0
                ? created / (float)requestedOccupied
                : 1f;
        }

        struct GreedyBudget
        {
            public float requestedDifficulty;
            public float requiredRemainingAverage;
            public float totalTarget;
            public int totalCount;
            public float targetAverage;
            public float forecastBaseline;
            public float predictedWholeCityBefore;
        }

        static int CountGreedyBlocks(AirCombatCityPlan plan)
        {
            int count = 0;
            for (int index = 0; index < plan.tacticalBlocks.Count; index++)
            {
                if (!plan.tacticalBlocks[index].excludedFromDifficulty)
                    count++;
            }
            return count;
        }

        static GreedyBudget ResolveGreedyBudget(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan,
            AirCombatCityDifficultyEvaluation evaluation,
            CombatCityBlockPlan current)
        {
            float remainingBaseTarget = 0f;
            float generatedActual = 0f;
            int totalCount = 0;
            int remainingCount = 0;
            for (int index = 0; index < plan.tacticalBlocks.Count; index++)
            {
                CombatCityBlockPlan block = plan.tacticalBlocks[index];
                if (block == null || block.excludedFromDifficulty)
                    continue;
                totalCount++;
                if (!block.generatedForDifficulty)
                {
                    remainingCount++;
                    remainingBaseTarget += block.targetDifficulty;
                    continue;
                }
                if (evaluation != null && index < evaluation.cells.Count &&
                    evaluation.cells[index].flyable)
                {
                    generatedActual +=
                        evaluation.cells[index].survivalDifficulty;
                }
            }
            // 角色目标只负责把危险度分配到不同区块，不能偷偷改变玩家
            // 输入的全城总预算。此前直接累加带偏移的角色目标，会让20%
            // 城市实际只剩更低的总预算，生成到中途便出现负数欠账。
            float globalTarget = settings != null
                ? AirCombatCityDifficultyPcg
                    .ResolveEffectiveGenerationTarget(settings.Difficulty)
                : 0.5f;
            float totalTarget = globalTarget * totalCount;
            float forecastBaseline = settings != null
                ? AirCombatCityDifficultyPcg
                    .ResolveUngeneratedForecastBaseline(settings.Difficulty)
                : globalTarget;
            float predictedWholeCityBefore = totalCount > 0
                ? (generatedActual + remainingCount * forecastBaseline) /
                  totalCount
                : globalTarget;
            float requiredRemainingAverage = remainingCount > 0
                ? (totalTarget - generatedActual) / remainingCount
                : settings != null
                    ? AirCombatCityDifficultyPcg.ResolveTarget(
                        settings.Difficulty).averageDifficulty
                    : 0.5f;
            float remainingBaseAverage = remainingCount > 0
                ? remainingBaseTarget / remainingCount
                : current.targetDifficulty;
            float roleOffset = current.targetDifficulty -
                               remainingBaseAverage;
            float maximum = AirCombatCityDifficultyPcg
                .ResolveMaximumRepresentableDifficulty(
                    settings?.Difficulty);
            // 全城已经偏高时，下一格不允许角色偏移再次把请求推高；
            // 已经偏低时同理不允许继续请求更安全的格子。角色只负责在
            // 当前欠账方向内塑造题目，不能与全城贪心控制器反向对抗。
            float budgetError = requiredRemainingAverage - globalTarget;
            if (Mathf.Abs(budgetError) > 0.01f)
            {
                // 一旦全城出现可测欠账，下一格完全服从欠账；这正是
                // “本轮偏高，下一格补安全候选”的贪心语义。
                roleOffset = 0f;
            }
            else if (budgetError < -0.0005f)
                roleOffset = Mathf.Min(0f, roleOffset);
            else if (budgetError > 0.0005f)
                roleOffset = Mathf.Max(0f, roleOffset);
            float boundedRequired = Mathf.Clamp(requiredRemainingAverage,
                0.02f, Mathf.Max(0.02f, maximum));
            float roleAllowance = Mathf.Min(
                Mathf.Max(0f, boundedRequired - 0.02f),
                Mathf.Max(0f, maximum - boundedRequired));
            // 角色差异是空间题目的软调味，不应制造超过3.5个百分点
            // 的单格逆向脉冲；否则20%目标会因一个暴露角色突然请求
            // 34%，破坏逐格反馈的单调性。
            roleAllowance = Mathf.Min(roleAllowance, 0.035f);
            return new GreedyBudget
            {
                requestedDifficulty = Mathf.Clamp(
                    boundedRequired + Mathf.Clamp(roleOffset,
                        -roleAllowance, roleAllowance),
                    0.02f,
                    Mathf.Max(0.02f, maximum)),
                requiredRemainingAverage = requiredRemainingAverage,
                totalTarget = totalTarget,
                totalCount = totalCount,
                targetAverage = globalTarget,
                forecastBaseline = forecastBaseline,
                predictedWholeCityBefore = predictedWholeCityBefore
            };
        }

        static GreedyBlockCandidate SelectGreedyBlockCandidate(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan,
            CombatCityBlockPlan block,
            GreedyBudget budget,
            StableRandom randomBefore,
            int buildingIndexBefore,
            float half,
            float spacing,
            int parcelCount,
            float start,
            bool scoreFinalCity,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CombatCityBlockLayoutPattern previousPattern =
                block.layoutPattern;
            float previousYaw = block.layoutYaw;
            bool previousLayoutResolved = block.layoutResolved;
            int previousSlotCount = block.layoutSlotCount;
            int previousOccupiedSlotCount = block.layoutOccupiedSlotCount;

            // 单格不再生成五套整体密度方案。先确定唯一的规则化街区
            // 骨架，再按固定楼位逐栋比较：留空、低楼、中楼、高楼。
            ResolveBlockLayout(settings, plan, block);
            CombatCityBlockLayoutPattern resolvedPattern =
                block.layoutPattern;
            float resolvedYaw = block.layoutYaw;
            var slots = new List<BlockBuildingSlot>(9);
            BuildBlockLayoutSlots(resolvedPattern, slots);
            block.layoutSlotCount = slots.Count;
            block.layoutOccupiedSlotCount = 0;
            block.generatedForDifficulty = true;

            int firstBuilding = plan.buildings.Count;
            int candidateBuildingIndex = buildingIndexBefore;
            int optionTests = 0;
            var realization = new BlockBuildRealization
            {
                slotCount = slots.Count,
                requestedOccupied = slots.Count
            };
            AirCombatCityDifficultyEvaluation currentEvaluation =
                AirCombatCityDifficultyPcg.Evaluate(settings, plan);
            cancellationToken.ThrowIfCancellationRequested();
            float currentForecast = PredictGreedyWholeCityDifficulty(
                plan, currentEvaluation, budget.forecastBaseline);

            for (int slotIndex = 0; slotIndex < slots.Count; slotIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                BlockBuildingSlot slot = slots[slotIndex];
                var bestOption = new GreedyBuildingOption
                {
                    evaluation = currentEvaluation,
                    predictedWholeCityDifficulty = currentForecast,
                    currentCellDifficulty = FindGreedyCellDifficulty(
                        currentEvaluation, block),
                    score = ScoreCityFit(currentEvaluation,
                        budget.targetAverage),
                    bandOrder = -1
                };
                optionTests++;

                for (int bandOrder = 0; bandOrder < 3; bandOrder++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    realization.attemptedSlots++;
                    if (!TryCreateBuildingForBlockSlot(
                            settings, plan, block, slot, slotIndex,
                            (AirCombatBuildingBand)bandOrder,
                            candidateBuildingIndex, half, spacing,
                            out AirCombatBuildingLot lot,
                            out int rejectionReason))
                    {
                        if (rejectionReason == 1)
                            realization.rejectedBounds++;
                        else if (rejectionReason == 2)
                            realization.rejectedFlight++;
                        else if (rejectionReason == 3)
                            realization.rejectedOverlap++;
                        continue;
                    }

                    optionTests++;
                    plan.buildings.Add(lot);
                    AirCombatCityDifficultyEvaluation optionEvaluation =
                        AirCombatCityDifficultyPcg.Evaluate(settings, plan);
                    cancellationToken.ThrowIfCancellationRequested();
                    float optionForecast = PredictGreedyWholeCityDifficulty(
                        plan, optionEvaluation, budget.forecastBaseline);
                    var option = new GreedyBuildingOption
                    {
                        lot = lot,
                        evaluation = optionEvaluation,
                        predictedWholeCityDifficulty = optionForecast,
                        currentCellDifficulty = FindGreedyCellDifficulty(
                            optionEvaluation, block),
                        score = ScoreCityFit(optionEvaluation,
                                    budget.targetAverage) +
                                Mathf.Abs(optionForecast -
                                          budget.targetAverage) * 0.45f,
                        bandOrder = bandOrder
                    };
                    plan.buildings.RemoveAt(plan.buildings.Count - 1);
                    if (IsBetterGreedyBuildingOption(option, bestOption,
                            budget.predictedWholeCityBefore,
                            budget.targetAverage,
                            budget.requestedDifficulty))
                    {
                        bestOption = option;
                    }
                }

                if (bestOption.lot == null)
                    continue;
                plan.buildings.Add(bestOption.lot);
                candidateBuildingIndex++;
                realization.created++;
                block.layoutOccupiedSlotCount = realization.created;
                currentEvaluation = bestOption.evaluation;
                currentForecast = bestOption.predictedWholeCityDifficulty;
            }

            bool needsSaferBlock = budget.predictedWholeCityBefore >
                                   budget.targetAverage + 0.002f;

            float remainingAfter;
            float predictedWholeCityDifficulty;
            float realizedOpenness = slots.Count > 0
                ? 1f - realization.created / (float)slots.Count
                : 1f;
            float score = ScoreGreedyCandidate(settings, plan, block,
                budget, currentEvaluation, realizedOpenness, realization,
                out remainingAfter, out predictedWholeCityDifficulty);
            if (scoreFinalCity)
            {
                score = ScoreFinalCityCandidate(currentEvaluation,
                    realizedOpenness, realization);
            }
            var candidate = new GreedyBlockCandidate
            {
                randomAfter = randomBefore,
                buildingIndexAfter = candidateBuildingIndex,
                densityScale = slots.Count > 0
                    ? Mathf.Clamp(realization.created /
                                  (float)slots.Count, 0.02f, 1f)
                    : 0.02f,
                heightScale = 1f,
                openness = realizedOpenness,
                requestedOpenness = realizedOpenness,
                score = score,
                predictedWholeCityDifficulty =
                    predictedWholeCityDifficulty,
                remainingRequiredAverage = remainingAfter,
                candidatesTested = optionTests,
                layoutPattern = resolvedPattern,
                layoutYaw = resolvedYaw,
                layoutSlotCount = slots.Count,
                layoutOccupiedSlotCount = realization.created,
                requestedOccupiedSlotCount = slots.Count,
                rejectedFlightSlotCount = realization.rejectedFlight,
                rejectedOverlapSlotCount = realization.rejectedOverlap,
                evaluation = currentEvaluation
            };
            for (int index = firstBuilding;
                 index < plan.buildings.Count;
                 index++)
            {
                candidate.buildings.Add(plan.buildings[index]);
            }
            plan.buildings.RemoveRange(firstBuilding,
                plan.buildings.Count - firstBuilding);
            block.generatedForDifficulty = false;
            block.layoutPattern = previousPattern;
            block.layoutYaw = previousYaw;
            block.layoutResolved = previousLayoutResolved;
            block.layoutSlotCount = previousSlotCount;
            block.layoutOccupiedSlotCount = previousOccupiedSlotCount;

            bool needsSafer = needsSaferBlock;
            bool needsMoreDanger = budget.predictedWholeCityBefore <
                                   budget.targetAverage - 0.002f;
            candidate.directionSatisfied = !needsSafer && !needsMoreDanger ||
                needsSafer && candidate.predictedWholeCityDifficulty <=
                budget.predictedWholeCityBefore + 0.0001f ||
                needsMoreDanger && candidate.predictedWholeCityDifficulty >=
                budget.predictedWholeCityBefore - 0.0001f;
            return candidate;
        }

        sealed class GreedyBuildingOption
        {
            public AirCombatBuildingLot lot;
            public AirCombatCityDifficultyEvaluation evaluation;
            public float predictedWholeCityDifficulty;
            public float currentCellDifficulty;
            public float score;
            public int bandOrder;
        }

        static bool IsBetterGreedyBuildingOption(
            GreedyBuildingOption candidate,
            GreedyBuildingOption currentBest,
            float directionBaseline,
            float target,
            float desiredCellDifficulty)
        {
            const float tolerance = 0.0001f;
            bool needsSafer = directionBaseline > target + 0.002f;
            bool needsMoreDanger = directionBaseline < target - 0.002f;
            if (needsSafer || needsMoreDanger)
            {
                float candidateGlobalError = Mathf.Abs(
                    candidate.predictedWholeCityDifficulty - target);
                float bestGlobalError = Mathf.Abs(
                    currentBest.predictedWholeCityDifficulty - target);
                if (Mathf.Abs(candidateGlobalError - bestGlobalError) >
                    tolerance)
                {
                    // 一旦全城出现欠账，每一栋楼先偿还全城误差；当前格
                    // 的角色目标只能在全城同样接近时作平局裁决。
                    return candidateGlobalError < bestGlobalError;
                }
            }

            float candidateError = Mathf.Abs(
                    candidate.predictedWholeCityDifficulty - target) * 0.42f +
                Mathf.Abs(candidate.currentCellDifficulty -
                          desiredCellDifficulty) * 0.58f;
            float bestError = Mathf.Abs(
                    currentBest.predictedWholeCityDifficulty - target) * 0.42f +
                Mathf.Abs(currentBest.currentCellDifficulty -
                          desiredCellDifficulty) * 0.58f;
            if (Mathf.Abs(candidateError - bestError) > tolerance)
                return candidateError < bestError;
            if (candidate.score < currentBest.score - tolerance)
                return true;
            if (Mathf.Abs(candidate.score - currentBest.score) > tolerance)
                return false;
            // 完全同效时优先低楼，再优先留空，保证确定性且避免无收益
            // 的高楼对象。真正需要高楼时，枪线重算会使其先胜出。
            return candidate.bandOrder >= 0 &&
                   (currentBest.bandOrder < 0 ||
                    candidate.bandOrder < currentBest.bandOrder);
        }

        static float PredictGreedyWholeCityDifficulty(
            AirCombatCityPlan plan,
            AirCombatCityDifficultyEvaluation evaluation,
            float ungeneratedBaseline)
        {
            float total = 0f;
            int count = 0;
            for (int index = 0; index < plan.tacticalBlocks.Count; index++)
            {
                CombatCityBlockPlan block = plan.tacticalBlocks[index];
                if (block == null || block.excludedFromDifficulty)
                    continue;
                count++;
                if (block.generatedForDifficulty && evaluation != null &&
                    index < evaluation.cells.Count &&
                    evaluation.cells[index].flyable)
                {
                    total += evaluation.cells[index].survivalDifficulty;
                }
                else
                {
                    total += ungeneratedBaseline;
                }
            }
            return count > 0 ? total / count : ungeneratedBaseline;
        }

        static float FindGreedyCellDifficulty(
            AirCombatCityDifficultyEvaluation evaluation,
            CombatCityBlockPlan block)
        {
            if (evaluation == null || block == null)
                return 1f;
            for (int index = 0; index < evaluation.cells.Count; index++)
            {
                AirCombatCityDifficultyCell cell = evaluation.cells[index];
                if (cell != null && cell.stableId == block.stableId)
                    return cell.survivalDifficulty;
            }
            return 1f;
        }

        sealed class GreedyBlockSnapshot
        {
            public CombatCityBlockPlan block;
            public float densityScale;
            public float heightScale;
            public float generatedDifficulty;
            public float requestedDifficulty;
            public float openness;
            public float remainingBefore;
            public float remainingAfter;
            public float candidateScore;
            public float predictedCityBefore;
            public float predictedCityAfter;
            public bool directionSatisfied;
            public int candidatesTested;
            public bool generated;
            public CombatCityBlockLayoutPattern layoutPattern;
            public float layoutYaw;
            public bool layoutResolved;
            public int layoutSlotCount;
            public int layoutOccupiedSlotCount;
        }

        static void TryBacktrackHighestImpactGreedyBlocks(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan,
            ref StableRandom random,
            ref int buildingIndex,
            float half,
            float spacing,
            int parcelCount,
            float start,
            ref AirCombatCityDifficultyEvaluation committedEvaluation,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (committedEvaluation == null ||
                !NeedsGreedyRepair(settings, committedEvaluation))
                return;
            // 只回看最后几格无法修复早期区块已经造成的全城偏差。
            // 回溯仍然发生在同一座城市上，但选择的是对当前偏差贡献
            // 最大的区块：城市偏危险时重做最高危区块，偏安全时重做
            // 最低危区块。之后仍按原始中心向外顺序重新生成，保证演示
            // 和正式生成使用同一条确定性流程。
            int rewindCount = Mathf.Clamp(
                settings.maximumAttempts + 6,
                8,
                Mathf.Min(24, CountGreedyBlocks(plan)));
            var latest = new List<CombatCityBlockPlan>(rewindCount);
            var eligible = new List<CombatCityBlockPlan>(
                CountGreedyBlocks(plan));
            for (int index = 0; index < plan.tacticalBlocks.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CombatCityBlockPlan block = plan.tacticalBlocks[index];
                if (block == null || block.excludedFromDifficulty ||
                    !block.generatedForDifficulty)
                    continue;
                eligible.Add(block);
            }
            bool needsSaferCity = committedEvaluation.averageDifficulty >
                                  AirCombatCityDifficultyPcg
                                      .ResolveEffectiveGenerationTarget(
                                          settings.Difficulty);
            eligible.Sort((left, right) =>
            {
                int difficultyOrder = needsSaferCity
                    ? right.generatedDifficulty.CompareTo(
                        left.generatedDifficulty)
                    : left.generatedDifficulty.CompareTo(
                        right.generatedDifficulty);
                if (difficultyOrder != 0)
                    return difficultyOrder;
                // 同等危险时先修实际为空的区块；空格在低目标下尤其
                // 容易暴露全向枪线，却曾被“请求了安全候选”误判为安全。
                int leftEmpty = left.layoutSlotCount > 0 &&
                                left.layoutOccupiedSlotCount <= 0 ? 1 : 0;
                int rightEmpty = right.layoutSlotCount > 0 &&
                                 right.layoutOccupiedSlotCount <= 0 ? 1 : 0;
                int emptyOrder = rightEmpty.CompareTo(leftEmpty);
                if (emptyOrder != 0)
                    return emptyOrder;
                return right.generationOrder.CompareTo(left.generationOrder);
            });
            for (int index = 0;
                 index < Mathf.Min(rewindCount, eligible.Count);
                 index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                latest.Add(eligible[index]);
            }
            if (latest.Count == 0)
                return;
            latest.Sort((left, right) =>
                left.generationOrder.CompareTo(right.generationOrder));

            var originalBuildings = new List<AirCombatBuildingLot>(
                plan.buildings);
            var snapshots = new List<GreedyBlockSnapshot>(latest.Count);
            for (int index = 0; index < latest.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CombatCityBlockPlan block = latest[index];
                snapshots.Add(new GreedyBlockSnapshot
                {
                    block = block,
                    densityScale = block.buildingDensityScale,
                    heightScale = block.buildingHeightScale,
                    generatedDifficulty = block.generatedDifficulty,
                    requestedDifficulty = block.greedyRequestedDifficulty,
                    openness = block.greedyCandidateOpenness,
                    remainingBefore = block.greedyRemainingBudgetBefore,
                    remainingAfter = block.greedyRemainingBudgetAfter,
                    candidateScore = block.greedyCandidateScore,
                    predictedCityBefore = block.greedyPredictedCityBefore,
                    predictedCityAfter = block.greedyPredictedCityAfter,
                    directionSatisfied = block.greedyDirectionSatisfied,
                    candidatesTested = block.localCorrectionCount,
                    generated = block.generatedForDifficulty,
                    layoutPattern = block.layoutPattern,
                    layoutYaw = block.layoutYaw,
                    layoutResolved = block.layoutResolved,
                    layoutSlotCount = block.layoutSlotCount,
                    layoutOccupiedSlotCount =
                        block.layoutOccupiedSlotCount
                });
            }
            StableRandom originalRandom = random;
            int originalBuildingIndex = buildingIndex;
            float effectiveTarget = AirCombatCityDifficultyPcg
                .ResolveEffectiveGenerationTarget(settings.Difficulty);

            for (int building = plan.buildings.Count - 1;
                 building >= 0;
                 building--)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AirCombatBuildingLot lot = plan.buildings[building];
                if (!IsDifficultyAdjustableBuilding(lot))
                    continue;
                for (int blockIndex = 0;
                     blockIndex < latest.Count;
                     blockIndex++)
                {
                    if (!latest[blockIndex].bounds.Contains(new Vector3(
                            lot.center.x,
                            latest[blockIndex].bounds.center.y,
                            lot.center.z)))
                        continue;
                    plan.buildings.RemoveAt(building);
                    break;
                }
            }
            for (int index = 0; index < latest.Count; index++)
                latest[index].generatedForDifficulty = false;
            AirCombatCityDifficultyEvaluation trialEvaluation =
                AirCombatCityDifficultyPcg.Evaluate(settings, plan);
            cancellationToken.ThrowIfCancellationRequested();

            for (int index = 0; index < latest.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CombatCityBlockPlan block = latest[index];
                GreedyBudget budget = ResolveGreedyBudget(
                    settings, plan, trialEvaluation, block);
                GreedyBlockCandidate chosen = SelectGreedyBlockCandidate(
                    settings, plan, block, budget, random, buildingIndex,
                    half, spacing, parcelCount, start, false,
                    cancellationToken);
                if (chosen == null)
                    continue;
                plan.buildings.AddRange(chosen.buildings);
                random = chosen.randomAfter;
                buildingIndex = chosen.buildingIndexAfter;
                block.buildingDensityScale = chosen.densityScale;
                block.buildingHeightScale = chosen.heightScale;
                block.layoutPattern = chosen.layoutPattern;
                block.layoutYaw = chosen.layoutYaw;
                block.layoutResolved = true;
                block.layoutSlotCount = chosen.layoutSlotCount;
                block.greedyRequestedDifficulty = budget.requestedDifficulty;
                block.greedyCandidateOpenness = chosen.openness;
                block.greedyRemainingBudgetBefore =
                    budget.requiredRemainingAverage;
                block.greedyRemainingBudgetAfter =
                    chosen.remainingRequiredAverage;
                block.greedyCandidateScore = chosen.score;
                block.greedyPredictedCityBefore =
                    budget.predictedWholeCityBefore;
                block.greedyPredictedCityAfter =
                    chosen.predictedWholeCityDifficulty;
                block.greedyDirectionSatisfied = chosen.directionSatisfied;
                block.localCorrectionCount = chosen.candidatesTested;
                block.layoutOccupiedSlotCount =
                    chosen.layoutOccupiedSlotCount;
                block.generatedForDifficulty = true;
                trialEvaluation = chosen.evaluation;
                ApplyGeneratedDifficulty(plan, trialEvaluation);
            }

            if (IsCloserCityFit(
                    trialEvaluation,
                    committedEvaluation,
                    effectiveTarget))
            {
                committedEvaluation = trialEvaluation;
                return;
            }

            plan.buildings.Clear();
            plan.buildings.AddRange(originalBuildings);
            random = originalRandom;
            buildingIndex = originalBuildingIndex;
            for (int index = 0; index < snapshots.Count; index++)
            {
                GreedyBlockSnapshot snapshot = snapshots[index];
                CombatCityBlockPlan block = snapshot.block;
                block.buildingDensityScale = snapshot.densityScale;
                block.buildingHeightScale = snapshot.heightScale;
                block.generatedDifficulty = snapshot.generatedDifficulty;
                block.greedyRequestedDifficulty = snapshot.requestedDifficulty;
                block.greedyCandidateOpenness = snapshot.openness;
                block.greedyRemainingBudgetBefore = snapshot.remainingBefore;
                block.greedyRemainingBudgetAfter = snapshot.remainingAfter;
                block.greedyCandidateScore = snapshot.candidateScore;
                block.greedyPredictedCityBefore = snapshot.predictedCityBefore;
                block.greedyPredictedCityAfter = snapshot.predictedCityAfter;
                block.greedyDirectionSatisfied = snapshot.directionSatisfied;
                block.localCorrectionCount = snapshot.candidatesTested;
                block.generatedForDifficulty = snapshot.generated;
                block.layoutPattern = snapshot.layoutPattern;
                block.layoutYaw = snapshot.layoutYaw;
                block.layoutResolved = snapshot.layoutResolved;
                block.layoutSlotCount = snapshot.layoutSlotCount;
                block.layoutOccupiedSlotCount =
                    snapshot.layoutOccupiedSlotCount;
            }
        }

        static void TryRepairGreedyCity(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan,
            ref StableRandom random,
            ref int buildingIndex,
            float half,
            float spacing,
            int parcelCount,
            float start,
            ref AirCombatCityDifficultyEvaluation committedEvaluation,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (committedEvaluation == null ||
                !NeedsGreedyRepair(settings, committedEvaluation))
                return;

            int repairLimit = Mathf.Clamp(settings.maximumAttempts + 10,
                8, Mathf.Min(24, CountGreedyBlocks(plan)));
            var repaired = new HashSet<CombatCityBlockPlan>();
            for (int repair = 0; repair < repairLimit; repair++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!NeedsGreedyRepair(settings, committedEvaluation))
                    break;
                ApplyGeneratedDifficulty(plan, committedEvaluation);
                bool needsSaferCity = committedEvaluation.averageDifficulty >
                    AirCombatCityDifficultyPcg
                        .ResolveEffectiveGenerationTarget(
                            settings.Difficulty);
                CombatCityBlockPlan block = null;
                for (int index = 0;
                     index < plan.tacticalBlocks.Count;
                     index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    CombatCityBlockPlan candidate =
                        plan.tacticalBlocks[index];
                    if (candidate == null ||
                        candidate.excludedFromDifficulty ||
                        !candidate.generatedForDifficulty ||
                        repaired.Contains(candidate))
                        continue;
                    if (block == null || IsHigherRepairPriority(candidate,
                            block, needsSaferCity))
                        block = candidate;
                }
                if (block == null)
                    break;
                repaired.Add(block);

                var originalBuildings = new List<AirCombatBuildingLot>(
                    plan.buildings);
                var snapshot = new GreedyBlockSnapshot
                {
                    block = block,
                    densityScale = block.buildingDensityScale,
                    heightScale = block.buildingHeightScale,
                    generatedDifficulty = block.generatedDifficulty,
                    requestedDifficulty = block.greedyRequestedDifficulty,
                    openness = block.greedyCandidateOpenness,
                    remainingBefore = block.greedyRemainingBudgetBefore,
                    remainingAfter = block.greedyRemainingBudgetAfter,
                    candidateScore = block.greedyCandidateScore,
                    predictedCityBefore = block.greedyPredictedCityBefore,
                    predictedCityAfter = block.greedyPredictedCityAfter,
                    directionSatisfied = block.greedyDirectionSatisfied,
                    candidatesTested = block.localCorrectionCount,
                    generated = block.generatedForDifficulty,
                    layoutPattern = block.layoutPattern,
                    layoutYaw = block.layoutYaw,
                    layoutResolved = block.layoutResolved,
                    layoutSlotCount = block.layoutSlotCount,
                    layoutOccupiedSlotCount =
                        block.layoutOccupiedSlotCount
                };
                StableRandom originalRandom = random;
                int originalBuildingIndex = buildingIndex;
                float effectiveTarget = AirCombatCityDifficultyPcg
                    .ResolveEffectiveGenerationTarget(settings.Difficulty);

                for (int lotIndex = plan.buildings.Count - 1;
                     lotIndex >= 0;
                     lotIndex--)
                {
                    AirCombatBuildingLot lot = plan.buildings[lotIndex];
                    if (!IsDifficultyAdjustableBuilding(lot) ||
                        !block.bounds.Contains(new Vector3(lot.center.x,
                            block.bounds.center.y, lot.center.z)))
                        continue;
                    plan.buildings.RemoveAt(lotIndex);
                }
                block.generatedForDifficulty = false;
                AirCombatCityDifficultyEvaluation withoutBlock =
                    AirCombatCityDifficultyPcg.Evaluate(settings, plan);
                cancellationToken.ThrowIfCancellationRequested();
                GreedyBudget budget = ResolveGreedyBudget(settings, plan,
                    withoutBlock, block);
                GreedyBlockCandidate chosen = SelectGreedyBlockCandidate(
                    settings, plan, block, budget, random, buildingIndex,
                    half, spacing, parcelCount, start, true,
                    cancellationToken);

                if (chosen != null && IsCloserCityFit(
                        chosen.evaluation,
                        committedEvaluation,
                        effectiveTarget))
                {
                    plan.buildings.AddRange(chosen.buildings);
                    random = chosen.randomAfter;
                    buildingIndex = chosen.buildingIndexAfter;
                    block.buildingDensityScale = chosen.densityScale;
                    block.buildingHeightScale = chosen.heightScale;
                    block.layoutPattern = chosen.layoutPattern;
                    block.layoutYaw = chosen.layoutYaw;
                    block.layoutResolved = true;
                    block.layoutSlotCount = chosen.layoutSlotCount;
                    block.layoutOccupiedSlotCount =
                        chosen.layoutOccupiedSlotCount;
                    block.greedyRequestedDifficulty =
                        budget.requestedDifficulty;
                    block.greedyCandidateOpenness = chosen.openness;
                    block.greedyRemainingBudgetBefore =
                        budget.requiredRemainingAverage;
                    block.greedyRemainingBudgetAfter =
                        chosen.remainingRequiredAverage;
                    block.greedyCandidateScore = chosen.score;
                    block.greedyPredictedCityBefore =
                        budget.predictedWholeCityBefore;
                    block.greedyPredictedCityAfter =
                        chosen.predictedWholeCityDifficulty;
                    block.greedyDirectionSatisfied =
                        chosen.directionSatisfied;
                    block.localCorrectionCount += chosen.candidatesTested;
                    block.generatedForDifficulty = true;
                    committedEvaluation = chosen.evaluation;
                    ApplyGeneratedDifficulty(plan, committedEvaluation);
                    continue;
                }

                plan.buildings.Clear();
                plan.buildings.AddRange(originalBuildings);
                random = originalRandom;
                buildingIndex = originalBuildingIndex;
                RestoreGreedyBlockSnapshot(snapshot);
                ApplyGeneratedDifficulty(plan, committedEvaluation);
            }
        }

        static bool IsHigherRepairPriority(CombatCityBlockPlan candidate,
            CombatCityBlockPlan current, bool needsSaferCity)
        {
            float candidateValue = candidate.generatedDifficulty;
            float currentValue = current.generatedDifficulty;
            if (Mathf.Abs(candidateValue - currentValue) > 0.0001f)
                return needsSaferCity
                    ? candidateValue > currentValue
                    : candidateValue < currentValue;
            bool candidateEmpty = candidate.layoutSlotCount > 0 &&
                                  candidate.layoutOccupiedSlotCount <= 0;
            bool currentEmpty = current.layoutSlotCount > 0 &&
                                current.layoutOccupiedSlotCount <= 0;
            if (candidateEmpty != currentEmpty)
                return candidateEmpty;
            return candidate.generationOrder > current.generationOrder;
        }

        static void RestoreGreedyBlockSnapshot(GreedyBlockSnapshot snapshot)
        {
            CombatCityBlockPlan block = snapshot.block;
            block.buildingDensityScale = snapshot.densityScale;
            block.buildingHeightScale = snapshot.heightScale;
            block.generatedDifficulty = snapshot.generatedDifficulty;
            block.greedyRequestedDifficulty = snapshot.requestedDifficulty;
            block.greedyCandidateOpenness = snapshot.openness;
            block.greedyRemainingBudgetBefore = snapshot.remainingBefore;
            block.greedyRemainingBudgetAfter = snapshot.remainingAfter;
            block.greedyCandidateScore = snapshot.candidateScore;
            block.greedyPredictedCityBefore = snapshot.predictedCityBefore;
            block.greedyPredictedCityAfter = snapshot.predictedCityAfter;
            block.greedyDirectionSatisfied = snapshot.directionSatisfied;
            block.localCorrectionCount = snapshot.candidatesTested;
            block.generatedForDifficulty = snapshot.generated;
            block.layoutPattern = snapshot.layoutPattern;
            block.layoutYaw = snapshot.layoutYaw;
            block.layoutResolved = snapshot.layoutResolved;
            block.layoutSlotCount = snapshot.layoutSlotCount;
            block.layoutOccupiedSlotCount =
                snapshot.layoutOccupiedSlotCount;
        }

        static float ScoreCityFit(
            AirCombatCityDifficultyEvaluation evaluation,
            float effectiveTarget)
        {
            if (evaluation == null || evaluation.target == null)
                return float.PositiveInfinity;
            return evaluation.targetFitError +
                   Mathf.Abs(evaluation.averageDifficulty -
                             effectiveTarget) * 0.60f;
        }

        static bool IsCloserCityFit(
            AirCombatCityDifficultyEvaluation candidate,
            AirCombatCityDifficultyEvaluation current,
            float effectiveTarget)
        {
            if (candidate == null || !candidate.IsUsable)
                return false;
            if (current == null || !current.IsUsable)
                return true;
            if (candidate.coverageValid != current.coverageValid)
                return candidate.coverageValid;
            float candidateError = Mathf.Abs(
                candidate.averageDifficulty - effectiveTarget);
            float currentError = Mathf.Abs(
                current.averageDifficulty - effectiveTarget);
            if (candidateError < currentError - 0.0001f)
                return true;
            if (Mathf.Abs(candidateError - currentError) > 0.0001f)
                return false;
            // 只有在平均危险度同样接近时，才用安全/高危分布等复合拟合
            // 作为平局裁决。这样“最终最接近”不会被分布项反向覆盖。
            return ScoreCityFit(candidate, effectiveTarget) <
                   ScoreCityFit(current, effectiveTarget) - 0.0005f;
        }

        static bool NeedsGreedyRepair(
            AirCombatCitySettings settings,
            AirCombatCityDifficultyEvaluation evaluation)
        {
            if (evaluation == null)
                return false;
            float target = AirCombatCityDifficultyPcg
                .ResolveEffectiveGenerationTarget(settings?.Difficulty);
            return Mathf.Abs(evaluation.averageDifficulty - target) > 0.025f;
        }

        static float ScoreFinalCityCandidate(
            AirCombatCityDifficultyEvaluation evaluation,
            float requestedOpenness,
            BlockBuildRealization realization)
        {
            float effectiveTarget = evaluation != null
                ? evaluation.controlAverageDifficulty
                : 0.5f;
            float score = ScoreCityFit(evaluation, effectiveTarget);
            bool needsSaferCity = evaluation != null &&
                                  evaluation.averageDifficulty >
                                  effectiveTarget;
            if (needsSaferCity && realization.slotCount > 0 &&
                realization.created <= 0)
                score += 0.008f;
            return score + Mathf.Abs(requestedOpenness - 0.5f) * 0.00001f;
        }

        static float ScoreGreedyCandidate(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan,
            CombatCityBlockPlan current,
            GreedyBudget budget,
            AirCombatCityDifficultyEvaluation evaluation,
            float requestedOpenness,
            BlockBuildRealization realization,
            out float remainingRequiredAverage,
            out float predictedFinalAverage)
        {
            float actualSum = 0f;
            float currentDifficulty = 1f;
            int generatedCount = 0;
            int remainingCount = 0;
            for (int index = 0; index < plan.tacticalBlocks.Count; index++)
            {
                CombatCityBlockPlan block = plan.tacticalBlocks[index];
                if (block == null || block.excludedFromDifficulty)
                    continue;
                if (!block.generatedForDifficulty)
                {
                    remainingCount++;
                    continue;
                }
                generatedCount++;
                if (index >= evaluation.cells.Count ||
                    !evaluation.cells[index].flyable)
                {
                    actualSum += 1f;
                    continue;
                }
                float difficulty = evaluation.cells[index]
                    .survivalDifficulty;
                actualSum += difficulty;
                if (ReferenceEquals(block, current))
                    currentDifficulty = difficulty;
            }

            remainingRequiredAverage = remainingCount > 0
                ? (budget.totalTarget - actualSum) / remainingCount
                : 0f;
            float maximum = AirCombatCityDifficultyPcg
                .ResolveMaximumRepresentableDifficulty(
                    settings.Difficulty);
            float feasibilityError = remainingCount > 0
                ? Mathf.Max(0f, -remainingRequiredAverage) +
                  Mathf.Max(0f, remainingRequiredAverage - maximum)
                : Mathf.Abs(actualSum - budget.totalTarget) /
                  Mathf.Max(1, budget.totalCount);
            float targetAverage = budget.targetAverage;
            // 未生成格使用三层模型的保守基线，而不是假定未来每一格都
            // 能完美命中目标。这样低目标不会在中途虚假“达标”，当前
            // 候选造成的超支也无法再被未来理想格抵消。
            predictedFinalAverage = (actualSum +
                remainingCount * budget.forecastBaseline) /
                Mathf.Max(1, budget.totalCount);
            float predictedBeforeAverage = (actualSum - currentDifficulty +
                (remainingCount + 1) * budget.forecastBaseline) /
                Mathf.Max(1, budget.totalCount);
            float directionPenalty = 0f;
            if (predictedBeforeAverage > targetAverage + 0.002f &&
                predictedFinalAverage > predictedBeforeAverage + 0.0001f)
            {
                // 全城已经偏高时，继续升高的候选违反反馈方向。只在
                // 所有候选都无法降压时才会退化为“选择增幅最小者”。
                directionPenalty = 0.08f +
                    (predictedFinalAverage - predictedBeforeAverage) * 8f;
            }
            else if (predictedBeforeAverage < targetAverage - 0.002f &&
                     predictedFinalAverage <
                     predictedBeforeAverage - 0.0001f)
            {
                directionPenalty = 0.08f +
                    (predictedBeforeAverage - predictedFinalAverage) * 8f;
            }
            float progress = generatedCount /
                             (float)Mathf.Max(1, budget.totalCount);
            float distributionError =
                Mathf.Abs(evaluation.safeCellRatio -
                          evaluation.target.safeCellRatio) * 0.55f +
                Mathf.Abs(evaluation.highRiskCellRatio -
                          evaluation.target.highRiskCellRatio) * 0.45f;
            bool needsSaferGeometry = budget.requestedDifficulty <
                                      targetAverage - 0.005f;
            float realizationPenalty = 0f;
            if (realization.requestedOccupied > 0)
            {
                realizationPenalty = (1f - realization.Fulfillment) *
                                     (needsSaferGeometry ? 0.08f : 0.03f);
                if (realization.created <= 0)
                    realizationPenalty += needsSaferGeometry ? 0.12f : 0.04f;
            }
            else if (needsSaferGeometry)
            {
                // 开放候选可以服务高危目标，但不能在需要降压时仅凭
                // “没有发生异常”被误标成安全候选。
                realizationPenalty += 0.06f;
            }
            return Mathf.Abs(currentDifficulty -
                             budget.requestedDifficulty) * 0.46f +
                   Mathf.Abs(predictedFinalAverage - targetAverage) * 0.24f +
                   feasibilityError * 1.30f +
                   evaluation.averageTargetError * 0.14f +
                   distributionError * progress * 0.12f +
                   directionPenalty +
                   realizationPenalty +
                   // 完全同分时轻微偏向中性候选；这不是难度假设，
                   // 只用于避免浮点相等时总选择极端几何。
                   Mathf.Abs(requestedOpenness - 0.5f) * 0.0002f;
        }

        static int ResolveFrontierPressureDirection(
            AirCombatCitySettings settings,
            AirCombatCityDifficultyEvaluation evaluation)
        {
            if (evaluation == null || evaluation.target == null)
                return 0;
            float delta = evaluation.averageDifficulty -
                          AirCombatCityDifficultyPcg
                              .ResolveEffectiveGenerationTarget(
                                  settings?.Difficulty);
            if (delta > 0.005f)
                return -1;
            if (delta < -0.005f)
                return 1;
            return 0;
        }

        static CombatCityBlockPlan SelectNextGreedyFrontierBlock(
            AirCombatCityPlan plan,
            int pressureDirection)
        {
            CombatCityBlockPlan best = null;
            bool anyGenerated = false;
            for (int index = 0; index < plan.tacticalBlocks.Count; index++)
            {
                CombatCityBlockPlan block = plan.tacticalBlocks[index];
                if (block.generatedForDifficulty &&
                    !block.excludedFromDifficulty)
                {
                    anyGenerated = true;
                    break;
                }
            }
            for (int index = 0; index < plan.tacticalBlocks.Count; index++)
            {
                CombatCityBlockPlan candidate = plan.tacticalBlocks[index];
                if (candidate == null || candidate.excludedFromDifficulty ||
                    candidate.generatedForDifficulty ||
                    (anyGenerated && !HasGeneratedGreedyNeighbor(
                        plan, candidate.gridX, candidate.gridZ)))
                {
                    continue;
                }
                if (best == null || CompareFrontierBlocks(candidate, best,
                        pressureDirection) < 0)
                    best = candidate;
            }
            return best;
        }

        static int CompareFrontierBlocks(CombatCityBlockPlan left,
            CombatCityBlockPlan right, int pressureDirection)
        {
            float leftRing = Mathf.Max(Mathf.Abs(left.gridX - 3.5f),
                Mathf.Abs(left.gridZ - 3.5f));
            float rightRing = Mathf.Max(Mathf.Abs(right.gridX - 3.5f),
                Mathf.Abs(right.gridZ - 3.5f));
            int ringCompare = leftRing.CompareTo(rightRing);
            if (ringCompare != 0)
                return ringCompare;
            float leftRadius = Mathf.Abs(left.gridX - 3.5f) +
                               Mathf.Abs(left.gridZ - 3.5f);
            float rightRadius = Mathf.Abs(right.gridX - 3.5f) +
                                Mathf.Abs(right.gridZ - 3.5f);
            int radiusCompare = leftRadius.CompareTo(rightRadius);
            if (radiusCompare != 0)
                return radiusCompare;
            if (pressureDirection != 0)
            {
                int difficultyCompare = left.targetDifficulty.CompareTo(
                    right.targetDifficulty);
                if (difficultyCompare != 0)
                    return pressureDirection < 0
                        ? difficultyCompare
                        : -difficultyCompare;
            }
            int xCompare = left.gridX.CompareTo(right.gridX);
            return xCompare != 0
                ? xCompare
                : left.gridZ.CompareTo(right.gridZ);
        }

        static bool HasGeneratedGreedyNeighbor(
            AirCombatCityPlan plan,
            int gridX,
            int gridZ)
        {
            int[] dx = { -1, 1, 0, 0 };
            int[] dz = { 0, 0, -1, 1 };
            for (int index = 0; index < dx.Length; index++)
            {
                CombatCityBlockPlan neighbor =
                    CombatDrivenCityPcgPlanner.GetTacticalBlock(
                        plan, gridX + dx[index], gridZ + dz[index]);
                if (neighbor != null && neighbor.generatedForDifficulty &&
                    !neighbor.excludedFromDifficulty)
                {
                    return true;
                }
            }
            return false;
        }

        static int CompareBlocksCenterOut(
            CombatCityBlockPlan left,
            CombatCityBlockPlan right)
        {
            float leftRing = Mathf.Max(
                Mathf.Abs(left.gridX - 3.5f),
                Mathf.Abs(left.gridZ - 3.5f));
            float rightRing = Mathf.Max(
                Mathf.Abs(right.gridX - 3.5f),
                Mathf.Abs(right.gridZ - 3.5f));
            int ringCompare = leftRing.CompareTo(rightRing);
            if (ringCompare != 0)
                return ringCompare;
            float leftRadius = Mathf.Abs(left.gridX - 3.5f) +
                               Mathf.Abs(left.gridZ - 3.5f);
            float rightRadius = Mathf.Abs(right.gridX - 3.5f) +
                                Mathf.Abs(right.gridZ - 3.5f);
            int radiusCompare = leftRadius.CompareTo(rightRadius);
            if (radiusCompare != 0)
                return radiusCompare;
            int xCompare = left.gridX.CompareTo(right.gridX);
            return xCompare != 0
                ? xCompare
                : left.gridZ.CompareTo(right.gridZ);
        }

        static BlockBuildRealization BuildBuildingsForBlock(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan,
            CombatCityBlockPlan tacticalBlock,
            float densityScale,
            float heightScale,
            ref StableRandom random,
            ref int buildingIndex,
            float half,
            float spacing,
            int count,
            float start)
        {
            if (tacticalBlock == null)
                return default;
            ResolveBlockLayout(settings, plan, tacticalBlock);
            var slots = new List<BlockBuildingSlot>(9);
            BuildBlockLayoutSlots(tacticalBlock.layoutPattern, slots);
            tacticalBlock.layoutSlotCount = slots.Count;

            Vector2 blockCenter = new Vector2(
                tacticalBlock.bounds.center.x,
                tacticalBlock.bounds.center.z);
            float normalizedRadius = blockCenter.magnitude /
                                     Mathf.Max(1f, half);
            float localDensity = normalizedRadius < 0.46f
                ? Mathf.Min(0.99f, settings.buildingDensity + 0.15f)
                : normalizedRadius < 0.75f
                    ? Mathf.Min(0.98f, settings.buildingDensity + 0.14f)
                    : Mathf.Max(0.68f, settings.buildingDensity - 0.16f);
            if (tacticalBlock.excludedFromDifficulty)
                localDensity = Mathf.Max(localDensity, 0.96f);
            localDensity = Mathf.Clamp01(localDensity * densityScale);
            int requestedOccupied = tacticalBlock.excludedFromDifficulty
                ? slots.Count
                : Mathf.Clamp(
                    Mathf.RoundToInt(slots.Count * localDensity),
                    localDensity >= 0.08f ? 1 : 0,
                    slots.Count);
            var realization = new BlockBuildRealization
            {
                slotCount = slots.Count,
                requestedOccupied = requestedOccupied
            };
            float maximumFlyableParcelSpan = Mathf.Max(
                18f,
                spacing - settings.MinimumDefaultPresetBuildingGap);
            Quaternion blockRotation = Quaternion.Euler(
                0f, tacticalBlock.layoutYaw, 0f);
            int created = 0;
            for (int slotIndex = 0;
                 slotIndex < slots.Count && created < requestedOccupied;
                 slotIndex++)
            {
                realization.attemptedSlots++;
                BlockBuildingSlot slot = slots[slotIndex];
                Vector3 localOffset = new Vector3(
                    slot.position.x * tacticalBlock.bounds.size.x,
                    0f,
                    slot.position.y * tacticalBlock.bounds.size.z);
                Vector3 rotatedOffset = blockRotation * localOffset;
                Vector2 point = blockCenter + new Vector2(
                    rotatedOffset.x, rotatedOffset.z);
                if (Mathf.Abs(point.x) > half - 44f ||
                    Mathf.Abs(point.y) > half - 44f)
                {
                    realization.rejectedBounds++;
                    continue;
                }

                float desiredWidth = Mathf.Min(
                    maximumFlyableParcelSpan,
                    tacticalBlock.bounds.size.x * slot.size.x);
                float desiredDepth = Mathf.Min(
                    maximumFlyableParcelSpan,
                    tacticalBlock.bounds.size.z * slot.size.y);
                desiredWidth = Mathf.Max(16f, desiredWidth);
                desiredDepth = Mathf.Max(16f, desiredDepth);
                float protectedRoofLimit = ResolveProtectedRoofLimit(
                    settings, plan, point);
                bool protectedVolume =
                    !float.IsPositiveInfinity(protectedRoofLimit);
                int localPattern = PositiveModulo(
                    StableLayoutHash(
                        tacticalBlock.stableId, settings.seed,
                        slotIndex * 37 + slot.heightBias * 13),
                    16);
                AirCombatBuildingBand band = ResolveBuildingBand(
                    settings, point, protectedRoofLimit, localPattern);
                band = AdjustSlotBand(
                    band, slot.heightBias, heightScale,
                    tacticalBlock.excludedFromDifficulty);
                float desiredHeight = ResolveSlotHeight(
                    settings,
                    band,
                    protectedVolume,
                    protectedRoofLimit,
                    heightScale,
                    localPattern,
                    tacticalBlock.excludedFromDifficulty);
                AirCombatBuildingModelMetric model = ResolveBestBuildingModel(
                    settings,
                    band,
                    desiredWidth,
                    desiredDepth,
                    desiredHeight,
                    localPattern);
                ResolveModelConstrainedFootprint(
                    model,
                    desiredWidth,
                    desiredDepth,
                    out float footprint,
                    out float depth,
                    out float horizontalScale);
                float height = ResolveModelConstrainedHeight(
                    settings,
                    model,
                    band,
                    desiredHeight,
                    horizontalScale,
                    protectedRoofLimit,
                    tacticalBlock.excludedFromDifficulty);
                float yaw = tacticalBlock.layoutYaw + slot.yawOffset;
                if (IsReservedForFlight(
                        settings,
                        plan,
                        point,
                        height,
                        new Vector2(footprint, depth),
                        yaw))
                {
                    realization.rejectedFlight++;
                    continue;
                }
                if (OverlapsBuilding(
                        plan.buildings, point, footprint, depth))
                {
                    realization.rejectedOverlap++;
                    continue;
                }

                string parcelLabel = slot.size.x * slot.size.y >= 0.020f
                    ? "large"
                    : slot.size.x * slot.size.y >= 0.014f
                        ? "standard"
                        : "small-gapfill";
                int clusterId = 10000 + tacticalBlock.gridX * 100 +
                                tacticalBlock.gridZ;
                int visualVariant = model != null
                    ? model.catalogIndex
                    : PositiveModulo(localPattern * 61 + slotIndex * 17, 97);
                plan.buildings.Add(new AirCombatBuildingLot
                {
                    stableId = "building." + parcelLabel + "." +
                               buildingIndex++.ToString("D3") + "." +
                               LayoutPatternLabel(tacticalBlock.layoutPattern) +
                               ".slot" + slotIndex.ToString("D2"),
                    center = new Vector3(point.x, height * 0.5f, point.y),
                    size = new Vector3(footprint, height, depth),
                    yaw = yaw,
                    band = band,
                    archetype = ArchetypeForBand(band),
                    clusterId = clusterId,
                    visualVariant = visualVariant,
                    layoutSlotIndex = slotIndex,
                    layoutPattern = tacticalBlock.layoutPattern,
                    authoredModelSize = model != null
                        ? model.authoredSize
                        : Vector3.zero,
                    horizontalModelScale = horizontalScale
                });
                created++;
            }
            tacticalBlock.layoutOccupiedSlotCount = created;
            realization.created = created;
            return realization;
        }

        static bool TryCreateBuildingForBlockSlot(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan,
            CombatCityBlockPlan tacticalBlock,
            BlockBuildingSlot slot,
            int slotIndex,
            AirCombatBuildingBand forcedBand,
            int buildingIndex,
            float half,
            float spacing,
            out AirCombatBuildingLot lot,
            out int rejectionReason)
        {
            lot = null;
            rejectionReason = 0;
            Vector2 blockCenter = new Vector2(
                tacticalBlock.bounds.center.x,
                tacticalBlock.bounds.center.z);
            Quaternion blockRotation = Quaternion.Euler(
                0f, tacticalBlock.layoutYaw, 0f);
            Vector3 localOffset = new Vector3(
                slot.position.x * tacticalBlock.bounds.size.x,
                0f,
                slot.position.y * tacticalBlock.bounds.size.z);
            Vector3 rotatedOffset = blockRotation * localOffset;
            Vector2 point = blockCenter + new Vector2(
                rotatedOffset.x, rotatedOffset.z);
            if (Mathf.Abs(point.x) > half - 44f ||
                Mathf.Abs(point.y) > half - 44f)
            {
                rejectionReason = 1;
                return false;
            }

            float maximumFlyableParcelSpan = Mathf.Max(
                18f,
                spacing - settings.MinimumDefaultPresetBuildingGap);
            float desiredWidth = Mathf.Max(16f, Mathf.Min(
                maximumFlyableParcelSpan,
                tacticalBlock.bounds.size.x * slot.size.x));
            float desiredDepth = Mathf.Max(16f, Mathf.Min(
                maximumFlyableParcelSpan,
                tacticalBlock.bounds.size.z * slot.size.y));
            float protectedRoofLimit = ResolveProtectedRoofLimit(
                settings, plan, point);
            bool protectedVolume =
                !float.IsPositiveInfinity(protectedRoofLimit);
            int localPattern = PositiveModulo(
                StableLayoutHash(
                    tacticalBlock.stableId, settings.seed,
                    slotIndex * 37 + slot.heightBias * 13),
                16);
            AirCombatBuildingBand band = forcedBand;
            float desiredHeight = ResolveSlotHeight(
                settings,
                band,
                protectedVolume,
                protectedRoofLimit,
                1f,
                localPattern,
                tacticalBlock.excludedFromDifficulty);
            AirCombatBuildingModelMetric model = ResolveBestBuildingModel(
                settings,
                band,
                desiredWidth,
                desiredDepth,
                desiredHeight,
                localPattern);
            ResolveModelConstrainedFootprint(
                model,
                desiredWidth,
                desiredDepth,
                out float footprint,
                out float depth,
                out float horizontalScale);
            float height = ResolveModelConstrainedHeight(
                settings,
                model,
                band,
                desiredHeight,
                horizontalScale,
                protectedRoofLimit,
                tacticalBlock.excludedFromDifficulty);
            float yaw = tacticalBlock.layoutYaw + slot.yawOffset;
            if (IsReservedForFlight(
                    settings,
                    plan,
                    point,
                    height,
                    new Vector2(footprint, depth),
                    yaw))
            {
                rejectionReason = 2;
                return false;
            }
            if (OverlapsBuilding(plan.buildings, point, footprint, depth))
            {
                rejectionReason = 3;
                return false;
            }

            string parcelLabel = slot.size.x * slot.size.y >= 0.020f
                ? "large"
                : slot.size.x * slot.size.y >= 0.014f
                    ? "standard"
                    : "small-gapfill";
            int clusterId = 10000 + tacticalBlock.gridX * 100 +
                            tacticalBlock.gridZ;
            int visualVariant = model != null
                ? model.catalogIndex
                : PositiveModulo(localPattern * 61 + slotIndex * 17, 97);
            lot = new AirCombatBuildingLot
            {
                stableId = "building." + parcelLabel + "." +
                           buildingIndex.ToString("D3") + "." +
                           LayoutPatternLabel(tacticalBlock.layoutPattern) +
                           ".slot" + slotIndex.ToString("D2"),
                center = new Vector3(point.x, height * 0.5f, point.y),
                size = new Vector3(footprint, height, depth),
                yaw = yaw,
                band = band,
                archetype = ArchetypeForBand(band),
                clusterId = clusterId,
                visualVariant = visualVariant,
                layoutSlotIndex = slotIndex,
                layoutPattern = tacticalBlock.layoutPattern,
                authoredModelSize = model != null
                    ? model.authoredSize
                    : Vector3.zero,
                horizontalModelScale = horizontalScale
            };
            return true;
        }

        struct BlockBuildingSlot
        {
            public Vector2 position;
            public Vector2 size;
            public float yawOffset;
            public int heightBias;

            public BlockBuildingSlot(
                float x,
                float z,
                float width,
                float depth,
                float yawOffset,
                int heightBias)
            {
                position = new Vector2(x, z);
                size = new Vector2(width, depth);
                this.yawOffset = yawOffset;
                this.heightBias = heightBias;
            }
        }

        static void ResolveBlockLayout(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan,
            CombatCityBlockPlan block)
        {
            string group = string.IsNullOrEmpty(block.mergedGroupId)
                ? block.stableId
                : block.mergedGroupId;
            int stable = StableLayoutHash(group, settings.seed, 0);
            CombatCityBlockLayoutPattern pattern;
            switch (block.role)
            {
                case CombatCityBlockRole.Occlusion:
                    switch (PositiveModulo(stable, 3))
                    {
                        case 0:
                            pattern = CombatCityBlockLayoutPattern
                                .ParallelStreetWalls;
                            break;
                        case 1:
                            pattern = CombatCityBlockLayoutPattern
                                .TwinTowerGate;
                            break;
                        default:
                            pattern = CombatCityBlockLayoutPattern
                                .StreetPerimeterCourt;
                            break;
                    }
                    break;
                case CombatCityBlockRole.Exposure:
                    switch (PositiveModulo(stable / 7, 4))
                    {
                        case 0:
                            pattern = CombatCityBlockLayoutPattern
                                .TwinTowerGate;
                            break;
                        case 1:
                            pattern = CombatCityBlockLayoutPattern
                                .ParallelStreetWalls;
                            break;
                        default:
                            pattern = CombatCityBlockLayoutPattern
                                .OpenManeuverBasin;
                            break;
                    }
                    break;
                case CombatCityBlockRole.Vertical:
                    pattern = PositiveModulo(stable / 11, 3) == 0
                        ? CombatCityBlockLayoutPattern.TerracedBuildingGroup
                        : CombatCityBlockLayoutPattern.OpenManeuverBasin;
                    break;
                case CombatCityBlockRole.Recovery:
                    switch (PositiveModulo(stable / 13, 3))
                    {
                        case 0:
                            pattern = CombatCityBlockLayoutPattern
                                .StreetPerimeterCourt;
                            break;
                        case 1:
                            pattern = CombatCityBlockLayoutPattern.UCourtyard;
                            break;
                        default:
                            pattern = CombatCityBlockLayoutPattern
                                .TerracedBuildingGroup;
                            break;
                    }
                    break;
                case CombatCityBlockRole.Kite:
                    pattern = PositiveModulo(stable / 23, 2) == 0
                        ? CombatCityBlockLayoutPattern.OpenManeuverBasin
                        : CombatCityBlockLayoutPattern.TerracedBuildingGroup;
                    break;
                case CombatCityBlockRole.Attack:
                case CombatCityBlockRole.TacticalChoke:
                    pattern = PositiveModulo(stable / 19, 2) == 0
                        ? CombatCityBlockLayoutPattern.TwinTowerGate
                        : CombatCityBlockLayoutPattern.ParallelStreetWalls;
                    break;
                case CombatCityBlockRole.Destruction:
                    pattern = CombatCityBlockLayoutPattern.TerracedBuildingGroup;
                    break;
                case CombatCityBlockRole.CombatBoundary:
                    pattern = CombatCityBlockLayoutPattern.StreetPerimeterCourt;
                    break;
                default:
                    pattern = (CombatCityBlockLayoutPattern)
                        PositiveModulo(stable, 6);
                    break;
            }
            block.layoutPattern = pattern;
            // 同一合并街区共享0/90度结构轴；正面再整体翻转180度，
            // 不允许每栋楼分别追逐最近道路。
            block.layoutYaw = PositiveModulo(stable / 17, 2) * 90f +
                              PositiveModulo(stable / 101, 2) * 180f;
            block.layoutResolved = true;
        }

        static void BuildBlockLayoutSlots(
            CombatCityBlockLayoutPattern pattern,
            List<BlockBuildingSlot> slots)
        {
            slots.Clear();
            switch (pattern)
            {
                case CombatCityBlockLayoutPattern.UCourtyard:
                    AddSlot(slots, -0.24f, -0.24f, 0.135f, 0.145f, 0f, 0);
                    AddSlot(slots, 0f, -0.24f, 0.13f, 0.145f, 0f, -1);
                    AddSlot(slots, 0.24f, -0.24f, 0.135f, 0.145f, 0f, 0);
                    AddSlot(slots, -0.24f, 0f, 0.135f, 0.14f, 90f, 1);
                    AddSlot(slots, 0.24f, 0f, 0.135f, 0.14f, -90f, 1);
                    AddSlot(slots, -0.24f, 0.24f, 0.13f, 0.13f, 90f, 0);
                    AddSlot(slots, 0.24f, 0.24f, 0.13f, 0.13f, -90f, 0);
                    break;
                case CombatCityBlockLayoutPattern.ParallelStreetWalls:
                    AddSlot(slots, -0.24f, -0.24f, 0.13f, 0.145f, 90f, 1);
                    AddSlot(slots, -0.24f, 0f, 0.13f, 0.145f, 90f, 0);
                    AddSlot(slots, -0.24f, 0.24f, 0.13f, 0.145f, 90f, 1);
                    AddSlot(slots, 0.24f, -0.24f, 0.13f, 0.145f, -90f, 1);
                    AddSlot(slots, 0.24f, 0f, 0.13f, 0.145f, -90f, 0);
                    AddSlot(slots, 0.24f, 0.24f, 0.13f, 0.145f, -90f, 1);
                    break;
                case CombatCityBlockLayoutPattern.TwinTowerGate:
                    AddSlot(slots, -0.22f, -0.17f, 0.145f, 0.135f, 0f, 1);
                    AddSlot(slots, 0.22f, -0.17f, 0.145f, 0.135f, 0f, 1);
                    AddSlot(slots, -0.24f, 0.23f, 0.13f, 0.135f, 90f, 0);
                    AddSlot(slots, 0.24f, 0.23f, 0.13f, 0.135f, -90f, 0);
                    AddSlot(slots, -0.24f, 0.04f, 0.12f, 0.12f, 90f, -1);
                    AddSlot(slots, 0.24f, 0.04f, 0.12f, 0.12f, -90f, -1);
                    break;
                case CombatCityBlockLayoutPattern.OpenManeuverBasin:
                    AddSlot(slots, -0.24f, -0.24f, 0.13f, 0.13f, 45f, 0);
                    AddSlot(slots, 0.24f, -0.24f, 0.13f, 0.13f, -45f, 1);
                    AddSlot(slots, -0.24f, 0.24f, 0.13f, 0.13f, 135f, 1);
                    AddSlot(slots, 0.24f, 0.24f, 0.13f, 0.13f, -135f, 0);
                    AddSlot(slots, 0f, 0.30f, 0.12f, 0.12f, 180f, -1);
                    break;
                case CombatCityBlockLayoutPattern.TerracedBuildingGroup:
                    AddSlot(slots, -0.24f, -0.23f, 0.14f, 0.135f, 0f, -1);
                    AddSlot(slots, 0f, -0.23f, 0.125f, 0.135f, 0f, 0);
                    AddSlot(slots, 0.24f, -0.23f, 0.14f, 0.135f, 0f, 1);
                    AddSlot(slots, -0.24f, 0.05f, 0.135f, 0.13f, 90f, 0);
                    AddSlot(slots, 0f, 0.06f, 0.12f, 0.12f, 0f, 1);
                    AddSlot(slots, 0.24f, 0.05f, 0.135f, 0.13f, -90f, 0);
                    AddSlot(slots, 0f, 0.30f, 0.135f, 0.12f, 180f, -1);
                    break;
                default:
                    AddSlot(slots, -0.24f, -0.24f, 0.135f, 0.135f, 0f, 0);
                    AddSlot(slots, 0f, -0.24f, 0.125f, 0.135f, 0f, -1);
                    AddSlot(slots, 0.24f, -0.24f, 0.135f, 0.135f, 0f, 0);
                    AddSlot(slots, -0.24f, 0f, 0.135f, 0.125f, 90f, 1);
                    AddSlot(slots, 0.24f, 0f, 0.135f, 0.125f, -90f, 1);
                    AddSlot(slots, -0.24f, 0.24f, 0.135f, 0.135f, 180f, 0);
                    AddSlot(slots, 0f, 0.24f, 0.125f, 0.135f, 180f, -1);
                    AddSlot(slots, 0.24f, 0.24f, 0.135f, 0.135f, 180f, 0);
                    break;
            }
        }

        static void AddSlot(
            List<BlockBuildingSlot> slots,
            float x,
            float z,
            float width,
            float depth,
            float yaw,
            int heightBias)
        {
            slots.Add(new BlockBuildingSlot(
                x, z, width, depth, yaw, heightBias));
        }

        static AirCombatBuildingBand AdjustSlotBand(
            AirCombatBuildingBand band,
            int heightBias,
            float heightScale,
            bool perimeter)
        {
            if (perimeter)
                return AirCombatBuildingBand.High;
            int value = (int)band + heightBias;
            if (heightScale < 0.68f)
                value--;
            else if (heightScale > 1.16f)
                value++;
            return (AirCombatBuildingBand)Mathf.Clamp(value, 0, 2);
        }

        static float ResolveSlotHeight(
            AirCombatCitySettings settings,
            AirCombatBuildingBand band,
            bool protectedVolume,
            float protectedRoofLimit,
            float heightScale,
            int localPattern,
            bool perimeter)
        {
            float variation = ((localPattern % 5) - 2) * 4f;
            float height;
            switch (band)
            {
                case AirCombatBuildingBand.High:
                    height = settings.highAltitude * 0.96f + variation * 2f;
                    break;
                case AirCombatBuildingBand.Medium:
                    height = settings.mediumAltitude * 0.88f + variation;
                    break;
                default:
                    height = settings.lowAltitude * 0.74f + variation * 0.5f;
                    break;
            }
            height *= Mathf.Clamp(heightScale, 0.38f, 1.42f);
            if (perimeter)
            {
                height = Mathf.Max(
                    height,
                    Mathf.Min(settings.maximumAltitude - 20f,
                        settings.highAltitude * 1.28f));
            }
            if (protectedVolume)
                height = Mathf.Min(height, protectedRoofLimit - 3f);
            return Mathf.Clamp(height, 28f, settings.maximumAltitude - 18f);
        }

        static AirCombatBuildingModelMetric ResolveBestBuildingModel(
            AirCombatCitySettings settings,
            AirCombatBuildingBand band,
            float desiredWidth,
            float desiredDepth,
            float desiredHeight,
            int stable)
        {
            AirCombatBuildingModelMetric[] metrics =
                settings.buildingModelMetrics;
            if (metrics == null || metrics.Length == 0)
                return null;
            float targetAspect = desiredWidth / Mathf.Max(0.1f, desiredDepth);
            float targetSlenderness = desiredHeight / Mathf.Max(
                0.1f, Mathf.Sqrt(desiredWidth * desiredDepth));
            AirCombatBuildingModelMetric best = null;
            float bestScore = float.PositiveInfinity;
            for (int index = 0; index < metrics.Length; index++)
            {
                AirCombatBuildingModelMetric metric = metrics[index];
                if (metric == null || metric.band != band ||
                    metric.groundSupportRatio < 0.28f)
                {
                    continue;
                }
                float aspectError = Mathf.Abs(Mathf.Log(
                    Mathf.Max(0.05f, metric.FootprintAspect) /
                    Mathf.Max(0.05f, targetAspect)));
                float slenderError = Mathf.Abs(Mathf.Log(
                    Mathf.Max(0.05f, metric.Slenderness) /
                    Mathf.Max(0.05f, targetSlenderness)));
                float supportPenalty = (1f - metric.groundSupportRatio) * 0.35f +
                                       (1f - metric.bodyCoverage) * 0.10f;
                float tieBreak = PositiveModulo(
                    stable + metric.catalogIndex * 17, 997) * 0.000001f;
                float score = aspectError * 0.68f + slenderError * 0.32f +
                              supportPenalty + tieBreak;
                if (score >= bestScore)
                    continue;
                bestScore = score;
                best = metric;
            }
            return best;
        }

        static void ResolveModelConstrainedFootprint(
            AirCombatBuildingModelMetric model,
            float desiredWidth,
            float desiredDepth,
            out float width,
            out float depth,
            out float horizontalScale)
        {
            if (model == null)
            {
                width = desiredWidth;
                depth = desiredDepth;
                horizontalScale = 1f;
                return;
            }
            float authoredWidth = Mathf.Max(0.1f, model.authoredSize.x);
            float authoredDepth = Mathf.Max(0.1f, model.authoredSize.z);
            horizontalScale = Mathf.Min(
                desiredWidth / authoredWidth,
                desiredDepth / authoredDepth);
            horizontalScale = Mathf.Max(0.1f, horizontalScale);
            width = authoredWidth * horizontalScale;
            depth = authoredDepth * horizontalScale;
        }

        static float ResolveModelConstrainedHeight(
            AirCombatCitySettings settings,
            AirCombatBuildingModelMetric model,
            AirCombatBuildingBand band,
            float desiredHeight,
            float horizontalScale,
            float protectedRoofLimit,
            bool perimeter)
        {
            float naturalHeight = model != null
                ? model.authoredSize.y * horizontalScale
                : desiredHeight;
            float height = Mathf.Lerp(desiredHeight, naturalHeight, 0.24f);
            float minimum = band == AirCombatBuildingBand.High
                ? Mathf.Max(150f, settings.mediumAltitude + 18f)
                : band == AirCombatBuildingBand.Medium
                    ? Mathf.Max(82f, settings.lowAltitude + 12f)
                    : 28f;
            float maximum = band == AirCombatBuildingBand.Low
                ? Mathf.Min(88f, settings.mediumAltitude - 22f)
                : band == AirCombatBuildingBand.Medium
                    ? Mathf.Min(184f, settings.highAltitude - 18f)
                    : settings.maximumAltitude - 18f;
            if (perimeter)
                minimum = Mathf.Max(minimum, settings.highAltitude * 1.18f);
            height = Mathf.Clamp(height, minimum, maximum);
            if (!float.IsPositiveInfinity(protectedRoofLimit))
                height = Mathf.Min(height, protectedRoofLimit - 3f);
            return Mathf.Clamp(height, 24f, settings.maximumAltitude - 18f);
        }

        static string LayoutPatternLabel(
            CombatCityBlockLayoutPattern pattern)
        {
            switch (pattern)
            {
                case CombatCityBlockLayoutPattern.UCourtyard:
                    return "u-courtyard";
                case CombatCityBlockLayoutPattern.ParallelStreetWalls:
                    return "parallel-street-walls";
                case CombatCityBlockLayoutPattern.TwinTowerGate:
                    return "twin-tower-gate";
                case CombatCityBlockLayoutPattern.OpenManeuverBasin:
                    return "open-maneuver-basin";
                case CombatCityBlockLayoutPattern.TerracedBuildingGroup:
                    return "terraced-group";
                default:
                    return "street-perimeter-court";
            }
        }

        static int StableLayoutHash(string value, int seed, int salt)
        {
            unchecked
            {
                uint hash = 2166136261u;
                string text = value ?? string.Empty;
                for (int index = 0; index < text.Length; index++)
                {
                    hash ^= text[index];
                    hash *= 16777619u;
                }
                hash ^= (uint)seed;
                hash *= 16777619u;
                hash ^= (uint)salt;
                hash *= 16777619u;
                return (int)(hash & 0x7FFFFFFF);
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
            float targetDifficulty = AirCombatCityDifficultyPcg
                .ResolveTarget(settings.Difficulty).averageDifficulty;
            float fixedCoverFactor = Mathf.Clamp01(
                Mathf.InverseLerp(0.80f, 0.30f, targetDifficulty));
            if (fixedCoverFactor <= 0.03f)
                return;
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
                int authoredSamples = cluster == 1 || cluster >= 3
                    ? 12
                    : 10;
                int samples = Mathf.Clamp(
                    Mathf.RoundToInt(authoredSamples * fixedCoverFactor),
                    1,
                    authoredSamples);
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
            int gateletCount = Mathf.Clamp(
                Mathf.RoundToInt(gatelets.Length * fixedCoverFactor),
                0,
                gatelets.Length);
            for (int i = 0; i < gateletCount; i++)
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
                // Candidate parcels stay on the east/west bands so the three
                // physical wall buildings can remain aligned with the real
                // street grid. Rotating these walls freely makes their corners
                // clip adjacent sidewalks even when the courtyard centre is in
                // a legal parcel.
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
            const int TargetMediumCover = 3;
            float centralRadius = settings.ManeuverDiameter * 0.5f;
            int usefulCover = 0;
            int mediumCover = 0;
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
                    if (existing.band == AirCombatBuildingBand.Medium)
                        mediumCover++;
                }
            }
            if (usefulCover >= TargetUsefulCover &&
                mediumCover >= TargetMediumCover)
                return;

            const float Width = 22f;
            const float Depth = 24f;
            int added = 0;
            while (usefulCover < TargetUsefulCover ||
                   mediumCover < TargetMediumCover)
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
                mediumCover++;
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
                if (volume.kind != AirCombatVolumeKind.RecoveryPocket)
                    continue;
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

        static void EnsureMinimumQuadrantHeightMix(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan)
        {
            // 这是城市的固定可读性约束，不是难度修补：四个象限各自
            // 至少保留两个低层与两个中层轮廓。先写入这些小体量锚点，
            // 后面的逐格贪心会在全局预算里完整计算它们造成的遮挡。
            const int RequiredPerBand = 2;
            const float LowWidth = 18f;
            const float LowDepth = 20f;
            const float MediumWidth = 20f;
            const float MediumDepth = 22f;
            float searchRadius = Mathf.Min(
                settings.mapSize * 0.5f - 120f,
                settings.ManeuverDiameter * 0.72f);
            float preferredRadius = searchRadius * 0.70f;

            for (int quadrant = 0; quadrant < 4; quadrant++)
            for (int bandIndex = 0; bandIndex < 2; bandIndex++)
            {
                AirCombatBuildingBand band = bandIndex == 0
                    ? AirCombatBuildingBand.Low
                    : AirCombatBuildingBand.Medium;
                int count = 0;
                for (int index = 0; index < plan.buildings.Count; index++)
                {
                    AirCombatBuildingLot building = plan.buildings[index];
                    if (building.band == band &&
                        ResolveQuadrant(new Vector2(
                            building.center.x,
                            building.center.z)) == quadrant)
                    {
                        count++;
                    }
                }

                while (count < RequiredPerBand)
                {
                    float width = bandIndex == 0
                        ? LowWidth
                        : MediumWidth;
                    float depth = bandIndex == 0
                        ? LowDepth
                        : MediumDepth;
                    float height = bandIndex == 0
                        ? 46f + count * 4f
                        : Mathf.Clamp(
                            settings.lowAltitude + 28f + count * 6f,
                            88f,
                            settings.mediumAltitude - 14f);
                    Vector2 bestPoint = Vector2.zero;
                    float bestYaw = 0f;
                    float bestScore = float.NegativeInfinity;
                    float xSign = (quadrant & 1) != 0 ? 1f : -1f;
                    float zSign = (quadrant & 2) != 0 ? 1f : -1f;
                    for (float xAbs = 48f; xAbs <= searchRadius; xAbs += 12f)
                    for (float zAbs = 48f; zAbs <= searchRadius; zAbs += 12f)
                    {
                        Vector2 point = new Vector2(
                            xAbs * xSign,
                            zAbs * zSign);
                        if (point.magnitude > searchRadius)
                            continue;
                        float yaw = ResolveFacadeYaw(plan, point);
                        Vector2 footprint = new Vector2(width, depth);
                        if (IsInsideProtectedGroundVolume(
                                plan,
                                point,
                                footprint) ||
                            IsReservedForFlight(
                                settings,
                                plan,
                                point,
                                height,
                                footprint,
                                yaw) ||
                            OverlapsBuilding(
                                plan.buildings,
                                point,
                                width,
                                depth))
                        {
                            continue;
                        }

                        float nearestSameBand = 300f;
                        for (int buildingIndex = 0;
                             buildingIndex < plan.buildings.Count;
                             buildingIndex++)
                        {
                            AirCombatBuildingLot other =
                                plan.buildings[buildingIndex];
                            if (other.band != band)
                                continue;
                            nearestSameBand = Mathf.Min(
                                nearestSameBand,
                                Vector2.Distance(
                                    point,
                                    new Vector2(
                                        other.center.x,
                                        other.center.z)));
                        }
                        float score = nearestSameBand -
                                      Mathf.Abs(point.magnitude -
                                                preferredRadius) * 0.08f;
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
                        stableId = "building.quadrant-height-mix." +
                                   quadrant + "." + bandIndex + "." +
                                   count,
                        center = new Vector3(
                            bestPoint.x,
                            height * 0.5f,
                            bestPoint.y),
                        size = new Vector3(width, height, depth),
                        yaw = bestYaw,
                        band = band,
                        archetype = bandIndex == 0
                            ? AirCombatBuildingArchetype.LowBlock
                            : AirCombatBuildingArchetype.MidSlab,
                        clusterId = 970 + quadrant * 2 + bandIndex,
                        visualVariant = PositiveModulo(
                            settings.seed + quadrant * 113 +
                            bandIndex * 37 + count * 17,
                            97)
                    });
                    count++;
                }
            }
        }

        static int ResolveQuadrant(Vector2 point)
        {
            return (point.x >= 0f ? 1 : 0) +
                   (point.y >= 0f ? 2 : 0);
        }

        static void EnsureCentralLowCover(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan,
            int targetLowCover = 5)
        {
            // 低层门齿承担贴地绕行与短暂断锁；它们优先占用中层楼因
            // 垂直净空而无法使用的地块，所以不会和中层补楼争夺战术位。
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
            while (lowCount < Mathf.Max(3, targetLowCover))
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

            // Dense high-tier layouts can exhaust every empty low-cover lot.
            // Lowering an already legal central medium building is safer than
            // adding an overlapping object: its footprint is unchanged and
            // its vertical obstruction only becomes smaller. Keep at least
            // three medium silhouettes so both sides of the validator's height
            // mix contract remain constructively guaranteed.
            int centralMedium = 0;
            for (int index = 0; index < plan.buildings.Count; index++)
            {
                AirCombatBuildingLot building = plan.buildings[index];
                Vector2 point = new Vector2(
                    building.center.x,
                    building.center.z);
                if (point.magnitude < centralRadius &&
                    building.band == AirCombatBuildingBand.Medium)
                {
                    centralMedium++;
                }
            }
            for (int index = 0;
                 lowCount < 3 && centralMedium > 3 &&
                 index < plan.buildings.Count;
                 index++)
            {
                AirCombatBuildingLot building = plan.buildings[index];
                Vector2 point = new Vector2(
                    building.center.x,
                    building.center.z);
                if (point.magnitude >= centralRadius ||
                    building.band != AirCombatBuildingBand.Medium ||
                    (building.clusterId >= 900 &&
                     building.clusterId < 2000))
                {
                    continue;
                }
                float height = 46f + lowCount * 4f;
                building.band = AirCombatBuildingBand.Low;
                building.size = new Vector3(
                    building.size.x,
                    height,
                    building.size.z);
                building.center = new Vector3(
                    building.center.x,
                    height * 0.5f,
                    building.center.z);
                building.archetype = AirCombatBuildingArchetype.LowBlock;
                lowCount++;
                centralMedium--;
            }
        }

        public static bool EnemyIngressIntersectsRecoveryDistrict(
            AirCombatCityPlan plan,
            Vector3 ingressPosition)
        {
            if (plan == null)
                return false;
            Vector2 point = new Vector2(
                ingressPosition.x,
                ingressPosition.z);
            for (int index = 0; index < plan.volumes.Count; index++)
            {
                AirCombatTacticalVolume volume = plan.volumes[index];
                if (volume.kind != AirCombatVolumeKind.RecoveryPocket)
                    continue;
                Vector2 center = new Vector2(
                    volume.center.x,
                    volume.center.z);
                float openSign = center.x < 0f ? -1f : 1f;
                Vector2[] wallCenters =
                {
                    center + new Vector2(-openSign * 44f, 0f),
                    center + new Vector2(-openSign * 12f, -42f),
                    center + new Vector2(-openSign * 12f, 42f)
                };
                Vector2[] wallFootprints =
                {
                    new Vector2(110f, 18f),
                    new Vector2(78f, 18f),
                    new Vector2(78f, 18f)
                };
                for (int wall = 0; wall < wallCenters.Length; wall++)
                {
                    float wallRadius = Mathf.Sqrt(
                        wallFootprints[wall].x * wallFootprints[wall].x +
                        wallFootprints[wall].y * wallFootprints[wall].y) *
                        0.5f;
                    if (Vector2.Distance(point, wallCenters[wall]) <
                        72f + wallRadius)
                    {
                        return true;
                    }
                }
            }
            return false;
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
            // 连续掩体修补必须能落进已经固定的道路与航线之间。
            // 这里使用窄塔而不是普通楼宽；它仍能切断枪线，却不会
            // 为了通过连续性验收反过来侵占先生成的道路。
            const float Width = 14f;
            const float Depth = 18f;
            const float Height = 94f;
            const float SearchStep = 10f;
            const int SearchRings = 8;
            const int DirectionsPerRing = 16;
            float sampleRadius = settings.ManeuverDiameter * 0.74f;
            float sampleStep = settings.buildingSpacing * 0.5f;
            float maximumAllowedGap = Mathf.Max(
                1f,
                settings.combatSpeed * 2.6f - 1.5f);

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
            if (plan.facilityCores.Count > 0)
            {
                float radians = yaw * Mathf.Deg2Rad;
                float cosine = Mathf.Abs(Mathf.Cos(radians));
                float sine = Mathf.Abs(Mathf.Sin(radians));
                float extentX =
                    (footprint.x * cosine + footprint.y * sine) * 0.5f;
                float extentZ =
                    (footprint.x * sine + footprint.y * cosine) * 0.5f;
                float padHalf = FacilityPadSize * 0.5f + 8f;
                for (int coreIndex = 0;
                     coreIndex < plan.facilityCores.Count;
                     coreIndex++)
                {
                    Vector3 core = plan.facilityCores[coreIndex];
                    if (Mathf.Abs(point.x - core.x) <= extentX + padHalf &&
                        Mathf.Abs(point.y - core.z) <= extentZ + padHalf)
                    {
                        return true;
                    }
                }
            }
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

        /// <summary>
        /// Shared read-only footprint/corridor query used by the planner,
        /// validation and editor tactical annotations. It performs no scene or
        /// physics mutation.
        /// </summary>
        public static bool FootprintIntersectsCorridor(
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
            Vector2 objective = new Vector2(
                plan.objective.x,
                plan.objective.z);
            float challenge = Mathf.Clamp01(
                settings.Difficulty.navigationChallenge);
            // The authored objective can be close to the boundary. Anchor the
            // three real facilities toward the combat interior, then increase
            // their separation with difficulty. Buildings are generated only
            // after these pads exist and must therefore route around them.
            Vector2 placementCenter = Vector2.Lerp(
                Vector2.zero,
                objective,
                Mathf.Lerp(0.10f, 0.28f, challenge));
            float preferredRadius = Mathf.Lerp(138f, 252f, challenge);
            float seedYaw = PositiveModulo(plan.resolvedSeed, 31) - 15f;
            for (int slot = 0; slot < 3; slot++)
            {
                float targetYaw = seedYaw + slot * 120f;
                bool found = false;
                // Search a bounded deterministic fan around each authored
                // sector.  Difficulty expands the triangle, but legality is
                // always decided by the same physical contract.
                for (int ring = 0; ring < 7 && !found; ring++)
                for (int turn = 0; turn < 17 && !found; turn++)
                {
                    int signedStep = turn == 0
                        ? 0
                        : ((turn + 1) / 2) * (turn % 2 == 1 ? 1 : -1);
                    float yaw = targetYaw + signedStep * 7f;
                    float radius = preferredRadius +
                                   (ring - 2) * 24f;
                    Vector2 direction = new Vector2(
                        Mathf.Sin(yaw * Mathf.Deg2Rad),
                        Mathf.Cos(yaw * Mathf.Deg2Rad));
                    Vector2 point = placementCenter + direction * radius;
                    Vector3 candidate = new Vector3(point.x, 0f, point.y);
                    if (!FacilitySiteIsLegal(settings, plan, candidate))
                        continue;
                    plan.facilityCores.Add(candidate);
                    found = true;
                }
            }
        }

        static bool FacilitySiteIsLegal(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan,
            Vector3 candidate)
        {
            float half = FacilityPadSize * 0.5f;
            float mapHalf = settings.mapSize * 0.5f - half - 18f;
            if (Mathf.Abs(candidate.x) > mapHalf ||
                Mathf.Abs(candidate.z) > mapHalf)
            {
                return false;
            }

            Vector2 center = new Vector2(candidate.x, candidate.z);
            for (int index = 0; index < plan.facilityCores.Count; index++)
            {
                Vector2 other = new Vector2(
                    plan.facilityCores[index].x,
                    plan.facilityCores[index].z);
                if (Vector2.Distance(center, other) <
                    FacilityPadMinimumSeparation)
                {
                    return false;
                }
            }

            // Physical combat-region geometry is instantiated after the base
            // building pass. Reserve its authored footprint now, otherwise a
            // later kite-loop tower or destruction pair can invalidate an
            // otherwise legal facility pad.
            for (int index = 0; index < plan.opportunities.Count; index++)
            {
                TacticalOpportunity opportunity = plan.opportunities[index];
                if (opportunity == null ||
                    (opportunity.kind != TacticalOpportunityKind.KiteLoop &&
                     opportunity.kind !=
                     TacticalOpportunityKind.DestructionAmbush))
                {
                    continue;
                }
                Bounds bounds = opportunity.bounds;
                float opportunityHalfX = opportunity.kind ==
                                             TacticalOpportunityKind.KiteLoop
                    ? 31f
                    : bounds.extents.x;
                float opportunityHalfZ = opportunity.kind ==
                                             TacticalOpportunityKind.KiteLoop
                    ? 31f
                    : bounds.extents.z;
                if (Mathf.Abs(candidate.x - bounds.center.x) <=
                        half + opportunityHalfX + 8f &&
                    Mathf.Abs(candidate.z - bounds.center.z) <=
                        half + opportunityHalfZ + 8f)
                {
                    return false;
                }
            }

            for (int index = 0; index < plan.buildings.Count; index++)
            {
                AirCombatBuildingLot building = plan.buildings[index];
                float radians = building.yaw * Mathf.Deg2Rad;
                float cos = Mathf.Abs(Mathf.Cos(radians));
                float sin = Mathf.Abs(Mathf.Sin(radians));
                float extentX =
                    (building.size.x * cos + building.size.z * sin) * 0.5f;
                float extentZ =
                    (building.size.x * sin + building.size.z * cos) * 0.5f;
                if (Mathf.Abs(candidate.x - building.center.x) <=
                        half + extentX + 8f &&
                    Mathf.Abs(candidate.z - building.center.z) <=
                        half + extentZ + 8f)
                {
                    return false;
                }
            }

            bool nearApproachRoad = false;
            for (int index = 0; index < plan.roads.Count; index++)
            {
                AirCombatRoadStrip road = plan.roads[index];
                Vector2 start = new Vector2(road.start.x, road.start.z);
                Vector2 end = new Vector2(road.end.x, road.end.z);
                float distance = DistanceToSegment(center, start, end);
                if (distance <= road.width * 0.5f + half + 8f)
                    return false;
                if (distance <= road.width * 0.5f + half + 92f)
                    nearApproachRoad = true;
            }
            if (!nearApproachRoad)
                return false;

            float verticalSafety = settings.wingspan * 0.45f + 8f;
            for (int routeIndex = 0;
                 routeIndex < plan.routes.Count;
                 routeIndex++)
            {
                AirCombatFlightRoute route = plan.routes[routeIndex];
                if (route.kind == AirCombatRouteKind.EnemyIngress ||
                    route.points == null)
                    continue;
                for (int pointIndex = 1;
                     pointIndex < route.points.Length;
                     pointIndex++)
                {
                    if (!FootprintIntersectsCorridor(
                            center,
                            Vector2.one * FacilityPadSize,
                            0f,
                            new Vector2(
                                route.points[pointIndex - 1].x,
                                route.points[pointIndex - 1].z),
                            new Vector2(
                                route.points[pointIndex].x,
                                route.points[pointIndex].z),
                            route.width * 0.5f,
                            out float segmentT))
                    {
                        continue;
                    }
                    float altitude = Mathf.Lerp(
                        route.points[pointIndex - 1].y,
                        route.points[pointIndex].y,
                        segmentT);
                    if (FacilityPadHeight + verticalSafety >= altitude)
                        return false;
                }
            }
            return true;
        }

        static bool FacilitySitesAreValid(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan)
        {
            if (plan.facilityCores.Count != 3)
                return false;
            var accepted = new List<Vector3>(3);
            for (int index = 0; index < plan.facilityCores.Count; index++)
            {
                Vector3 candidate = plan.facilityCores[index];
                plan.facilityCores.RemoveAt(index);
                bool valid = FacilitySiteIsLegal(settings, plan, candidate);
                plan.facilityCores.Insert(index, candidate);
                if (!valid)
                    return false;
                accepted.Add(candidate);
            }
            return accepted.Count == 3;
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
                    FacilitySitesAreValid(settings, plan)
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
            float spatialTarget = AirCombatCityDifficultyPcg
                .ResolveTarget(settings.Difficulty).averageDifficulty;
            float minimumEquivalentBuildingCount = Mathf.Lerp(
                80f,
                72f,
                Mathf.InverseLerp(0.66f, 0.90f, spatialTarget));
            bool buildingDensityTargetMet = equivalentBuildingCount >=
                                            minimumEquivalentBuildingCount;
            bool buildingPerformanceBudgetValid =
                equivalentBuildingCount <= 650f &&
                report.buildingCount <= 520;
            bool contactValid = settings.mission !=
                                AirCombatCityMission.FacilityAssault
                ? report.firstContactSeconds >= 4.5f
                : report.firstContactSeconds >= 1.5f;
            bool exposureDominantLayout = spatialTarget >= 0.66f;
            bool facilityHardValid = settings.mission !=
                                     AirCombatCityMission.FacilityAssault ||
                                     report.facilityReachable;
            // report.valid 只代表能否安全实例化并进入战斗。空间题目、美术
            // 配额和难度拟合全部记录为软目标，不能再把玩家挡在加载界面。
            report.hardPlayabilityValid = turnValid &&
                                          buildingPerformanceBudgetValid &&
                                          report.routesClear &&
                                          report.boundaryAirWallsValid &&
                                          facilityHardValid &&
                                          report.buildingRoadOverlapCount == 0 &&
                                          report.ingressCount >= 1;
            report.valid = report.hardPlayabilityValid;

            var designWarnings = new List<string>(16);
            if (!mapScaleValid)
                designWarnings.Add("地图交战尺度不足三倍武器射程");
            if (!buildingDensityTargetMet)
                designWarnings.Add("建筑密度低于设计目标");
            if (!report.alternateRouteAvailable)
                designWarnings.Add("替代战术航路不足");
            if (!report.threeAltitudeLayersUseful)
                designWarnings.Add("三层空域利用不足");
            if (!report.heightMixDistributed && !exposureDominantLayout)
                designWarnings.Add("局部低中高建筑混合不足");
            if (!report.centralHeightMixValid && !exposureDominantLayout)
                designWarnings.Add("中央低中层遮挡不足");
            if (!report.highAltitudeBypassControlled)
                designWarnings.Add("高空绕顶控制不足");
            if (!report.roadGridAligned || report.roadIntersectionCount < 32)
                designWarnings.Add("道路模数或路口数量未达设计值");
            if (!report.roadWidthsVaried)
                designWarnings.Add("道路宽度变化不足");
            if (!report.tacticalBlockCoverageValid)
                designWarnings.Add("战术区块语义覆盖不足");
            if (!report.coverContinuityValid)
                designWarnings.Add("中央掩体连续性不足");
            if (report.recoveryDistrictCount != 2)
                designWarnings.Add("恢复区数量未达设计值");
            if (!report.tacticalRolesComplete)
                designWarnings.Add("战术角色分布不完整");
            if (!report.tacticalOpportunityNetworkValid)
                designWarnings.Add("战术机会网络不足");
            if (!report.combatRegionsPhysical)
                designWarnings.Add("战术区域实体特征不足");
            if (report.dominantRouteDetected)
                designWarnings.Add("单一路线优势过强");
            if (report.ingressCount < 6)
                designWarnings.Add("敌机入口数量低于设计值");
            if (!contactValid)
                designWarnings.Add("敌机首次接触预警偏短");
            report.designTargetsMet = designWarnings.Count == 0;
            if (!report.designTargetsMet)
            {
                report.degraded = true;
                report.degradationWarning = "城市设计目标降级：" +
                    string.Join("、", designWarnings) + "。";
            }

            if (!report.hardPlayabilityValid)
            {
                if (!turnValid)
                    report.failureReason = "航路转弯半径小于飞机设计值。";
                else if (!buildingPerformanceBudgetValid)
                    report.failureReason = "建筑数量超出运行性能预算。";
                else if (!report.routesClear)
                    report.failureReason = "建筑侵入了安全飞行走廊。";
                else if (!report.boundaryAirWallsValid)
                    report.failureReason = "城市四面实体空战边界不完整。";
                else if (!facilityHardValid)
                    report.failureReason = "设施突袭目标不完整。";
                else if (report.buildingRoadOverlapCount != 0)
                    report.failureReason = "建筑侵入了道路或人行道。";
                else
                    report.failureReason = "没有可用的敌机入口。";
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
            // 这里仅防止中央出现完全失去遮挡拓扑的巨型空洞。真正的
            // 区块难度由“到低火力格的最小累计暴露”判定；因此保留
            // 目标相关的粗几何上限，避免它与生存路径求解器重复惩罚。
            // 高危关允许贪心器构造大暴露盆地；低危关仍要求连续掩体。
            float targetDifficulty = AirCombatCityDifficultyPcg
                .ResolveTarget(settings.Difficulty).averageDifficulty;
            float exposureAllowance = Mathf.InverseLerp(
                0.46f, 0.78f, targetDifficulty);
            float maximumCoverSeconds = Mathf.Lerp(
                2.6f, 7.5f, exposureAllowance);
            report.coverContinuityValid =
                maximumGap <= settings.combatSpeed * maximumCoverSeconds;
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
                kite && assault && occlusion;
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
