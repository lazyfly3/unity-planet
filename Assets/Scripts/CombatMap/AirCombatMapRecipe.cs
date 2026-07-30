using System;
using UnityEngine;

namespace UnityPlanet.CombatMap
{
    /// <summary>
    /// Editable authoring data for the finite CombatMapLab arena. The
    /// generator consumes a validated value-copy so generation does not
    /// depend on a live UnityEngine.Object.
    /// </summary>
    [CreateAssetMenu(
        fileName = "AirCombatMapRecipe",
        menuName = "Unity Planet/战斗地图/空战地图配方")]
    public sealed class AirCombatMapRecipe : ScriptableObject
    {
        [Header("生成标识")]
        [SerializeField, InspectorName("基础 Seed")] int seed = 7319;
        [SerializeField, Min(1), InspectorName("生成器版本")]
        int generatorVersion = 2;
        [SerializeField, Min(1), InspectorName("每批候选数量")]
        int candidateCount = 6;
        [SerializeField, Min(1), InspectorName("最多候选批次")]
        int maximumCandidateBatches = 3;

        [Header("竞技区")]
        [SerializeField, InspectorName("地图中心偏移")]
        Vector3 mapCenterOffset =
            new Vector3(0f, 0f, 1000f);
        [SerializeField, Min(512f), InspectorName("地图边长（米）")]
        float mapSize = 1536f;
        [SerializeField, Min(64f), InspectorName("区块边长（米）")]
        float chunkSize = 256f;
        [SerializeField, Range(8, 64), InspectorName("区块网格分辨率")]
        int chunkResolution = 32;
        [SerializeField, Min(160f), InspectorName("双方出生间距（米）")]
        float spawnDistance = 960f;
        [SerializeField, Min(12f), InspectorName("出生离地高度（米）")]
        float spawnClearance = 45f;

        [Header("飞行器设计包线")]
        [SerializeField, Min(10f), InspectorName("设计交战速度（米/秒）")]
        float designCombatSpeed = 55f;
        [SerializeField, Min(20f), InspectorName("设计转弯半径（米）")]
        float designTurnRadius = 95f;
        [SerializeField, Min(80f), InspectorName("设计有效射程（米）")]
        float designWeaponRange = 480f;
        [SerializeField, Min(2f), InspectorName("参考翼展（米）")]
        float vehicleWingspan = 18f;
        [SerializeField, Min(2f), InspectorName("目标首次接触时间（秒）")]
        float targetFirstContactSeconds = 10f;
        [SerializeField, Min(0.5f), InspectorName("目标连续遮挡时间（秒）")]
        float targetOcclusionSeconds = 5f;
        [SerializeField, Min(1f), InspectorName("目标连续暴露时间（秒）")]
        float targetExposureSeconds = 8f;

        [Header("战斗语义")]
        [SerializeField, Min(20f), InspectorName("中央山体高度（米）")]
        float mountainHeight = 160f;
        [SerializeField, Min(40f), InspectorName("主路线宽度（米）")]
        float mainRouteWidth = 160f;
        [SerializeField, Min(40f), InspectorName("峡谷路线宽度（米）")]
        float canyonRouteWidth = 140f;
        [SerializeField, Min(40f), InspectorName("远射走廊宽度（米）")]
        float longRangeRouteWidth = 180f;
        [SerializeField, Range(0, 64), InspectorName("遮挡塔数量")]
        int occluderTowerCount = 10;
        [SerializeField, Range(0f, 24f), InspectorName("微地形噪声强度")]
        float microNoiseStrength = 4.5f;
        [SerializeField, Min(16f), InspectorName("微地形噪声尺度")]
        float microNoiseScale = 72f;

        [Header("飞行边界")]
        [SerializeField, Min(100f), InspectorName("边界警告半径（米）")]
        float warningRadius = 700f;
        [SerializeField, Min(120f), InspectorName("判负半径（米）")]
        float forfeitRadius = 760f;
        [SerializeField, Min(0.5f), InspectorName("越界判负倒计时（秒）")]
        float forfeitSeconds = 5f;
        [SerializeField, Min(1f), InspectorName("最低离地高度（米）")]
        float minimumGroundClearance = 12f;
        [SerializeField, Min(30f), InspectorName("最高离地高度（米）")]
        float maximumGroundClearance = 220f;

        [Header("验证与评分")]
        [SerializeField, Range(0f, 100f), InspectorName("最低应用分数")]
        float minimumCommitScore = 80f;
        [SerializeField, Range(0f, 0.25f),
         InspectorName("最大路线时间差比例")]
        float maximumRouteTimeImbalance = 0.08f;
        [SerializeField, Range(8, 128), InspectorName("布局优化迭代次数")]
        int layoutOptimizationIterations = 48;
        [SerializeField, Range(5, 17), InspectorName("视线场采样边长")]
        int validationGridResolution = 9;

        public int Seed => seed;
        public int GeneratorVersion => generatorVersion;
        public int CandidateCount => candidateCount;
        public int MaximumCandidateBatches => maximumCandidateBatches;
        public Vector3 MapCenterOffset => mapCenterOffset;
        public float MapSize => mapSize;
        public float ChunkSize => chunkSize;
        public int ChunkResolution => chunkResolution;
        public float SpawnDistance => spawnDistance;
        public float SpawnClearance => spawnClearance;
        public float DesignCombatSpeed => designCombatSpeed;
        public float DesignTurnRadius => designTurnRadius;
        public float DesignWeaponRange => designWeaponRange;
        public float VehicleWingspan => vehicleWingspan;
        public float TargetFirstContactSeconds =>
            targetFirstContactSeconds;
        public float TargetOcclusionSeconds => targetOcclusionSeconds;
        public float TargetExposureSeconds => targetExposureSeconds;
        public float MountainHeight => mountainHeight;
        public float MainRouteWidth => mainRouteWidth;
        public float CanyonRouteWidth => canyonRouteWidth;
        public float LongRangeRouteWidth => longRangeRouteWidth;
        public int OccluderTowerCount => occluderTowerCount;
        public float MicroNoiseStrength => microNoiseStrength;
        public float MicroNoiseScale => microNoiseScale;
        public float WarningRadius => warningRadius;
        public float ForfeitRadius => forfeitRadius;
        public float ForfeitSeconds => forfeitSeconds;
        public float MinimumGroundClearance => minimumGroundClearance;
        public float MaximumGroundClearance => maximumGroundClearance;
        public float MinimumCommitScore => minimumCommitScore;
        public float MaximumRouteTimeImbalance =>
            maximumRouteTimeImbalance;
        public int LayoutOptimizationIterations =>
            layoutOptimizationIterations;
        public int ValidationGridResolution =>
            validationGridResolution;

        public AirCombatMapSettings CreateValidatedSettings(
            int? seedOverride = null)
        {
            var settings = new AirCombatMapSettings
            {
                seed = seedOverride ?? seed,
                generatorVersion = generatorVersion,
                candidateCount = candidateCount,
                maximumCandidateBatches = maximumCandidateBatches,
                mapCenterOffset = mapCenterOffset,
                mapSize = mapSize,
                chunkSize = chunkSize,
                chunkResolution = chunkResolution,
                spawnDistance = spawnDistance,
                spawnClearance = spawnClearance,
                designCombatSpeed = designCombatSpeed,
                designTurnRadius = designTurnRadius,
                designWeaponRange = designWeaponRange,
                vehicleWingspan = vehicleWingspan,
                targetFirstContactSeconds =
                    targetFirstContactSeconds,
                targetOcclusionSeconds = targetOcclusionSeconds,
                targetExposureSeconds = targetExposureSeconds,
                mountainHeight = mountainHeight,
                mainRouteWidth = mainRouteWidth,
                canyonRouteWidth = canyonRouteWidth,
                longRangeRouteWidth = longRangeRouteWidth,
                occluderTowerCount = occluderTowerCount,
                microNoiseStrength = microNoiseStrength,
                microNoiseScale = microNoiseScale,
                warningRadius = warningRadius,
                forfeitRadius = forfeitRadius,
                forfeitSeconds = forfeitSeconds,
                minimumGroundClearance = minimumGroundClearance,
                maximumGroundClearance = maximumGroundClearance,
                minimumCommitScore = minimumCommitScore,
                maximumRouteTimeImbalance =
                    maximumRouteTimeImbalance,
                layoutOptimizationIterations =
                    layoutOptimizationIterations,
                validationGridResolution =
                    validationGridResolution
            };
            settings.Clamp();
            return settings;
        }

        public void SetSeed(int value)
        {
            seed = value;
        }

        public void Clamp()
        {
            AirCombatMapSettings value = CreateValidatedSettings();
            Apply(value);
        }

        void OnValidate()
        {
            Clamp();
        }

        void Reset()
        {
            Apply(AirCombatMapSettings.CreateDefault());
        }

        void Apply(AirCombatMapSettings value)
        {
            seed = value.seed;
            generatorVersion = value.generatorVersion;
            candidateCount = value.candidateCount;
            maximumCandidateBatches = value.maximumCandidateBatches;
            mapCenterOffset = value.mapCenterOffset;
            mapSize = value.mapSize;
            chunkSize = value.chunkSize;
            chunkResolution = value.chunkResolution;
            spawnDistance = value.spawnDistance;
            spawnClearance = value.spawnClearance;
            designCombatSpeed = value.designCombatSpeed;
            designTurnRadius = value.designTurnRadius;
            designWeaponRange = value.designWeaponRange;
            vehicleWingspan = value.vehicleWingspan;
            targetFirstContactSeconds =
                value.targetFirstContactSeconds;
            targetOcclusionSeconds = value.targetOcclusionSeconds;
            targetExposureSeconds = value.targetExposureSeconds;
            mountainHeight = value.mountainHeight;
            mainRouteWidth = value.mainRouteWidth;
            canyonRouteWidth = value.canyonRouteWidth;
            longRangeRouteWidth = value.longRangeRouteWidth;
            occluderTowerCount = value.occluderTowerCount;
            microNoiseStrength = value.microNoiseStrength;
            microNoiseScale = value.microNoiseScale;
            warningRadius = value.warningRadius;
            forfeitRadius = value.forfeitRadius;
            forfeitSeconds = value.forfeitSeconds;
            minimumGroundClearance = value.minimumGroundClearance;
            maximumGroundClearance = value.maximumGroundClearance;
            minimumCommitScore = value.minimumCommitScore;
            maximumRouteTimeImbalance = value.maximumRouteTimeImbalance;
            layoutOptimizationIterations =
                value.layoutOptimizationIterations;
            validationGridResolution =
                value.validationGridResolution;
        }
    }

    [Serializable]
    public sealed class AirCombatMapSettings
    {
        public int seed = 7319;
        public int generatorVersion = 2;
        public int candidateCount = 6;
        public int maximumCandidateBatches = 3;
        public Vector3 mapCenterOffset = new Vector3(0f, 0f, 1000f);
        public float mapSize = 1536f;
        public float chunkSize = 256f;
        public int chunkResolution = 32;
        public float spawnDistance = 960f;
        public float spawnClearance = 45f;
        public float designCombatSpeed = 55f;
        public float designTurnRadius = 95f;
        public float designWeaponRange = 480f;
        public float vehicleWingspan = 18f;
        public float targetFirstContactSeconds = 10f;
        public float targetOcclusionSeconds = 5f;
        public float targetExposureSeconds = 8f;
        public float mountainHeight = 160f;
        public float mainRouteWidth = 160f;
        public float canyonRouteWidth = 140f;
        public float longRangeRouteWidth = 180f;
        public int occluderTowerCount = 10;
        public float microNoiseStrength = 4.5f;
        public float microNoiseScale = 72f;
        public float warningRadius = 700f;
        public float forfeitRadius = 760f;
        public float forfeitSeconds = 5f;
        public float minimumGroundClearance = 12f;
        public float maximumGroundClearance = 220f;
        public float minimumCommitScore = 80f;
        public float maximumRouteTimeImbalance = 0.08f;
        public int layoutOptimizationIterations = 48;
        public int validationGridResolution = 9;

        public static AirCombatMapSettings CreateDefault()
        {
            var result = new AirCombatMapSettings();
            result.Clamp();
            return result;
        }

        public AirCombatMapSettings ValidatedCopy(int? seedOverride = null)
        {
            var copy = (AirCombatMapSettings)MemberwiseClone();
            if (seedOverride.HasValue)
                copy.seed = seedOverride.Value;
            copy.Clamp();
            return copy;
        }

        public void Clamp()
        {
            generatorVersion = Mathf.Max(1, generatorVersion);
            candidateCount = Mathf.Clamp(candidateCount, 1, 18);
            maximumCandidateBatches = Mathf.Clamp(
                maximumCandidateBatches,
                1,
                6);
            mapSize = Mathf.Clamp(mapSize, 512f, 4096f);
            chunkSize = Mathf.Clamp(chunkSize, 64f, 512f);
            chunkSize = Mathf.Min(chunkSize, mapSize);
            chunkResolution = Mathf.Clamp(chunkResolution, 8, 64);

            warningRadius = Mathf.Clamp(
                warningRadius,
                180f,
                mapSize * 0.49f);
            forfeitRadius = Mathf.Clamp(
                forfeitRadius,
                warningRadius + 20f,
                mapSize * 0.5f);
            spawnDistance = Mathf.Clamp(
                spawnDistance,
                160f,
                Mathf.Min(mapSize * 0.82f, warningRadius * 1.6f));
            spawnClearance = Mathf.Clamp(
                spawnClearance,
                12f,
                180f);
            designCombatSpeed = Mathf.Clamp(
                designCombatSpeed,
                10f,
                220f);
            designTurnRadius = Mathf.Clamp(
                designTurnRadius,
                20f,
                800f);
            designWeaponRange = Mathf.Clamp(
                designWeaponRange,
                80f,
                4000f);
            vehicleWingspan = Mathf.Clamp(
                vehicleWingspan,
                2f,
                120f);
            targetFirstContactSeconds = Mathf.Clamp(
                targetFirstContactSeconds,
                2f,
                40f);
            targetOcclusionSeconds = Mathf.Clamp(
                targetOcclusionSeconds,
                0.5f,
                15f);
            targetExposureSeconds = Mathf.Clamp(
                targetExposureSeconds,
                1f,
                30f);

            mountainHeight = Mathf.Clamp(mountainHeight, 20f, 320f);
            mainRouteWidth = Mathf.Clamp(mainRouteWidth, 40f, 480f);
            canyonRouteWidth = Mathf.Clamp(
                canyonRouteWidth,
                40f,
                480f);
            longRangeRouteWidth = Mathf.Clamp(
                longRangeRouteWidth,
                40f,
                520f);
            occluderTowerCount = Mathf.Clamp(
                occluderTowerCount,
                0,
                64);
            microNoiseStrength = Mathf.Clamp(
                microNoiseStrength,
                0f,
                24f);
            microNoiseScale = Mathf.Clamp(microNoiseScale, 16f, 320f);

            forfeitSeconds = Mathf.Clamp(forfeitSeconds, 0.5f, 30f);
            minimumGroundClearance = Mathf.Clamp(
                minimumGroundClearance,
                1f,
                80f);
            maximumGroundClearance = Mathf.Clamp(
                maximumGroundClearance,
                minimumGroundClearance + 20f,
                600f);
            minimumCommitScore = Mathf.Clamp(
                minimumCommitScore,
                0f,
                100f);
            maximumRouteTimeImbalance = Mathf.Clamp(
                maximumRouteTimeImbalance,
                0f,
                0.25f);
            layoutOptimizationIterations = Mathf.Clamp(
                layoutOptimizationIterations,
                8,
                128);
            validationGridResolution = Mathf.Clamp(
                validationGridResolution,
                5,
                17);
            if ((validationGridResolution & 1) == 0)
                validationGridResolution++;
        }
    }
}
