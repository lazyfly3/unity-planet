using System;
using System.Collections.Generic;
using UnityEngine;
using UnityPlanet.CityPcg;

namespace UnityPlanet.EDPCG
{
    public enum EdpcgFireAnalysisAltitudeLayer
    {
        Low,
        Medium,
        High
    }

    public enum EdpcgGridFireSourceKind
    {
        Striker,
        Gunship
    }

    public enum EdpcgAirThreatViewMode
    {
        Potential,
        WithinFourSeconds,
        WithinEightSeconds,
        AuthorizedByEdpcg
    }

    public enum EdpcgAirThreatRoleFilter
    {
        All,
        Striker,
        Gunship
    }

    public static class EdpcgThreatDirectionUtility
    {
        /// <summary>
        /// Shared eight-sector boundary used by both live Horde pressure and
        /// the planning overlay. Sector zero begins at north and spans the
        /// first clockwise 45-degree interval.
        /// </summary>
        public static int HorizontalSector(Vector3 origin, Vector3 source)
        {
            Vector3 direction = source - origin;
            float angle = Mathf.Repeat(
                Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg,
                360f);
            return Mathf.Clamp(Mathf.FloorToInt(angle / 45f), 0, 7);
        }
    }

    /// <summary>
    /// Editor/runtime-neutral inputs used by the read-only air-threat solver.
    /// These values describe the existing game rules; the solver never writes
    /// them back to the player ship, enemies or city.
    /// </summary>
    [Serializable]
    public sealed class EdpcgAirThreatAnalysisOptions
    {
        public EdpcgTierSettings tierSettings;
        public EdpcgCityTacticalChallengeSettings challengeSettings;
        public float playerDesignSpeed = 55f;
        public float playerDesignTurnRadius = 95f;
        public float playerDesignClimbSpeed = 22f;
        public float routeSampleSpacing = 18f;
        public float maximumApproachDistance = 180f;

        public EdpcgAirThreatAnalysisOptions ValidatedCopy()
        {
            return new EdpcgAirThreatAnalysisOptions
            {
                tierSettings = tierSettings?.ValidatedCopy(),
                challengeSettings = challengeSettings?.ValidatedCopy(),
                playerDesignSpeed = Mathf.Max(10f, playerDesignSpeed),
                playerDesignTurnRadius = Mathf.Max(10f,
                    playerDesignTurnRadius),
                playerDesignClimbSpeed = Mathf.Max(5f,
                    playerDesignClimbSpeed),
                routeSampleSpacing = Mathf.Clamp(routeSampleSpacing, 12f, 30f),
                maximumApproachDistance = Mathf.Clamp(
                    maximumApproachDistance, 80f, 260f)
            };
        }

        public static EdpcgAirThreatAnalysisOptions CreateDefault(
            AirCombatCitySettings citySettings,
            int zeroBasedTier = 2)
        {
            EdpcgCityTacticalChallengeProfile challengeProfile =
                EdpcgCityTacticalChallengeProfile.LoadOrCreateMemoryDefault();
            EdpcgDifficultyProfile enemyProfile =
                challengeProfile.ordinaryEnemyProfile != null
                    ? challengeProfile.ordinaryEnemyProfile
                    : EdpcgDifficultyProfile.LoadOrCreateMemoryDefault();
            return new EdpcgAirThreatAnalysisOptions
            {
                tierSettings = enemyProfile.Resolve(zeroBasedTier),
                challengeSettings = challengeProfile.Resolve(zeroBasedTier),
                playerDesignSpeed = citySettings?.combatSpeed ?? 55f,
                playerDesignTurnRadius = citySettings?.turnRadius ?? 95f,
                playerDesignClimbSpeed = Mathf.Max(18f,
                    (citySettings?.combatSpeed ?? 55f) * 0.4f)
            };
        }
    }

    /// <summary>
    /// One continuous opportunity in which a role can reach a position, keep
    /// the authoritative five-metre firing corridor clear for long enough,
    /// finish telegraphing, and launch at least one projectile.
    /// </summary>
    [Serializable]
    public sealed class EdpcgAirFireWindow
    {
        public string stableId = string.Empty;
        public string routeId = string.Empty;
        public EdpcgGridFireSourceKind sourceKind;
        public int routeSegmentIndex = -1;
        public float routeSegmentT;
        public int azimuthSector;
        public int elevationBand;
        public int directionIndex;
        public Vector3 routeEntryWorldPosition;
        public Vector3 firingWorldPosition;
        public Vector3 playerWorldPosition;
        public Vector3[] approachWorldPoints = Array.Empty<Vector3>();
        public float approachSeconds;
        public float visibleWindowSeconds;
        public float setupSeconds;
        public float projectileSeconds;
        public float firstHitSeconds;
        public float threatenedSampleRatio;
        public int threatenedSampleMask;
        public bool throughBuildingGap;
        public bool fleetingVisibility;
        public bool navigationCorridorClear;
        public bool robustHullClear;
        public bool aiPermissionCorridorClear;
        public bool projectileCorridorClear;
        public bool authorizedByTier;
    }

    [Serializable]
    public sealed class EdpcgGridFireLine
    {
        public string sourceId = string.Empty;
        public string routeId = string.Empty;
        public EdpcgGridFireSourceKind sourceKind;
        public Vector3 sourceWorldPosition;
        public Vector3 playerWorldPosition;
        public Vector3 blockerWorldPosition;
        public string blockerId = string.Empty;
        public float distance;
        public float exposureRatio;
        public int threatenedSampleMask;
        public int threatSector;
        public int elevationBand;
        public int directionIndex;
        public float visibleWindowSeconds;
        public float setupSeconds;
        public float firstHitSeconds;
        public bool throughBuildingGap;
        public bool fleetingVisibility;
        public bool robustReachable;
        public bool incoming;
    }

    /// <summary>
    /// One representative ray showing where the practical active-position
    /// envelope is clipped by instantiated city geometry. This is diagnostic
    /// geometry only: it never grants an enemy route or fire permission.
    /// </summary>
    [Serializable]
    public sealed class EdpcgFiringVolumeOcclusionRay
    {
        public int directionIndex;
        public Vector3 playerWorldPosition;
        public Vector3 sampleWorldPosition;
        public Vector3 blockerWorldPosition;
        public string blockerId = string.Empty;
        public float blockerDistance;
    }

    [Serializable]
    public sealed class EdpcgGridEvasionLink
    {
        public string targetCellId = string.Empty;
        public int targetGridX;
        public int targetGridZ;
        public EdpcgFireAnalysisAltitudeLayer targetAltitudeLayer;
        public Vector3 startWorldPosition;
        public Vector3 endWorldPosition;
        public Vector3[] worldPathPoints = Array.Empty<Vector3>();
        public int removedThreatSectorMask;
        public int removedThreatDirectionMaskLow;
        public float pressureReduction;
        public float travelSeconds;
        public float escapeMarginSeconds;
        public bool verticalTransfer;
        public bool reachable;
        public bool recommended;
    }

    [Serializable]
    public struct EdpcgGridDifficultyBreakdown
    {
        public float score;
        public float fireThreat;
        public float escapeDifficulty;
        public float maneuverDifficulty;
        public float responseDemand;
        public float stayDifficulty;
        public float minimumRiskPathExposure;
        public float minimumRiskPathSeconds;
        public int minimumRiskPathCellCount;
        public int reachableTransitionCount;
        public int recommendedExitCount;

        public bool ResponseRequired => responseDemand > 0.001f;

        public string ChineseLevel
        {
            get
            {
                if (score >= 0.58f)
                    return "高危";
                if (score >= 0.40f)
                    return "困难";
                if (score >= 0.18f)
                    return "中等";
                return "容易";
            }
        }
    }

    /// <summary>
    /// Read-only combat difficulty projection for one grid cell. This is kept
    /// separate from pressureScore: pressureScore remains the EDPCG fire-lane
    /// control signal, while this score answers the editor-facing question
    /// "how difficult is it for the player to survive and leave this cell?".
    /// </summary>
    public static class EdpcgGridDifficultyEvaluator
    {
        public static EdpcgGridDifficultyBreakdown Evaluate(
            EdpcgGridFireCell cell,
            int directionCount,
            float threatenedVolume,
            float fastestHitSeconds,
            bool crossfire)
        {
            var result = new EdpcgGridDifficultyBreakdown();
            if (cell == null || !cell.flyable)
            {
                result.score = 1f;
                result.maneuverDifficulty = 1f;
                return result;
            }
            if (cell.excludedFromDifficulty)
            {
                result.score = 0f;
                result.stayDifficulty = 0f;
                return result;
            }

            directionCount = Mathf.Max(0, directionCount);
            threatenedVolume = Mathf.Clamp01(threatenedVolume);
            float urgency = float.IsNaN(fastestHitSeconds) ||
                            float.IsPositiveInfinity(fastestHitSeconds)
                ? 0f
                : Mathf.Clamp01((8f - Mathf.Max(0f, fastestHitSeconds)) / 8f);
            result.fireThreat = AirCombatCityDifficultyPcg
                .EvaluateFireThreatMetrics(
                    threatenedVolume,
                    directionCount,
                    urgency,
                    crossfire,
                    cell.difficultyCombatPressure);
            result.stayDifficulty = result.fireThreat;
            bool responseRequired = result.fireThreat >
                                    AirCombatGridSurvivalSolver
                                        .SafeThreatThreshold;
            result.responseDemand = responseRequired ? 1f : 0f;
            int reachableCount = 0;
            for (int index = 0; index < cell.evasions.Count; index++)
            {
                EdpcgGridEvasionLink link = cell.evasions[index];
                if (link == null)
                    continue;
                if (link.reachable)
                    reachableCount++;
            }
            result.reachableTransitionCount = reachableCount;
            result.recommendedExitCount = cell.safeExitCount;
            result.maneuverDifficulty = 0f;
            if (cell.survivalDifficultyComputed &&
                Mathf.Abs(result.fireThreat - cell.difficultyFireThreat) <=
                0.025f)
            {
                result.score = cell.survivalDifficulty;
                result.escapeDifficulty = cell.minimumRiskPathExposure;
                result.minimumRiskPathExposure =
                    cell.minimumRiskPathExposure;
                result.minimumRiskPathSeconds =
                    cell.minimumRiskPathSeconds;
                result.minimumRiskPathCellCount =
                    cell.minimumRiskPathCellCount;
            }
            else
            {
                // 非默认筛选模式没有整张图的重算上下文，保守显示该筛选
                // 下的留守风险；不会再用“出口少”给安全格凭空加分。
                result.score = result.fireThreat;
                result.escapeDifficulty = 0f;
            }
            return result;
        }

        public static void RebuildSurvivalCosts(
            params EdpcgGridFireAnalysis[] analyses)
        {
            var cells = new List<EdpcgGridFireCell>(192);
            var keys = new Dictionary<string, int>(192);
            if (analyses == null)
                return;
            for (int analysisIndex = 0;
                 analysisIndex < analyses.Length;
                 analysisIndex++)
            {
                EdpcgGridFireAnalysis analysis = analyses[analysisIndex];
                if (analysis == null)
                    continue;
                for (int cellIndex = 0;
                     cellIndex < analysis.cells.Count;
                     cellIndex++)
                {
                    EdpcgGridFireCell cell = analysis.cells[cellIndex];
                    if (cell == null)
                        continue;
                    int index = cells.Count;
                    cells.Add(cell);
                    keys[DifficultyKey(cell.stableId,
                        analysis.altitudeLayer)] = index;
                }
            }
            float[] threats = new float[cells.Count];
            bool[] flyable = new bool[cells.Count];
            var edges = new List<AirCombatGridSurvivalSolver.Edge>(
                cells.Count * 8);
            for (int index = 0; index < cells.Count; index++)
            {
                EdpcgGridFireCell cell = cells[index];
                float fastest = cell.authorizedFastestHitSeconds;
                float urgency = float.IsPositiveInfinity(fastest)
                    ? 0f
                    : Mathf.Clamp01((8f - fastest) / 8f);
                float fireThreat = AirCombatCityDifficultyPcg
                    .EvaluateFireThreatMetrics(
                        cell.authorizedThreatenedVolumeRatio,
                        cell.authorizedDirectionCount,
                        urgency,
                        cell.crossfire,
                        cell.difficultyCombatPressure);
                threats[index] = fireThreat;
                flyable[index] = cell.flyable &&
                                  !cell.excludedFromDifficulty;
                cell.difficultyFireThreat = fireThreat;
                cell.survivalDifficultyComputed = false;
            }
            for (int analysisIndex = 0;
                 analysisIndex < analyses.Length;
                 analysisIndex++)
            {
                EdpcgGridFireAnalysis analysis = analyses[analysisIndex];
                if (analysis == null)
                    continue;
                for (int cellIndex = 0;
                     cellIndex < analysis.cells.Count;
                     cellIndex++)
                {
                    EdpcgGridFireCell cell = analysis.cells[cellIndex];
                    if (cell == null || !keys.TryGetValue(DifficultyKey(
                            cell.stableId, analysis.altitudeLayer),
                            out int source))
                    {
                        continue;
                    }
                    for (int linkIndex = 0;
                         linkIndex < cell.evasions.Count;
                         linkIndex++)
                    {
                        EdpcgGridEvasionLink link = cell.evasions[linkIndex];
                        if (link == null || !link.reachable ||
                            !keys.TryGetValue(DifficultyKey(
                                link.targetCellId,
                                link.targetAltitudeLayer),
                                out int target))
                        {
                            continue;
                        }
                        edges.Add(new AirCombatGridSurvivalSolver.Edge
                        {
                            from = source,
                            to = target,
                            travelSeconds = Mathf.Max(0.01f,
                                link.travelSeconds)
                        });
                    }
                }
            }
            AirCombatGridSurvivalSolver.Result[] results =
                AirCombatGridSurvivalSolver.Solve(threats, flyable, edges);
            for (int index = 0; index < cells.Count; index++)
            {
                EdpcgGridFireCell cell = cells[index];
                AirCombatGridSurvivalSolver.Result result = results[index];
                if (cell.excludedFromDifficulty)
                {
                    cell.survivalDifficulty = 0f;
                    cell.minimumRiskPathExposure = 0f;
                    cell.minimumRiskPathSeconds = 0f;
                    cell.minimumRiskPathCellCount = 0;
                    cell.lowerThreatCellId = string.Empty;
                    cell.survivalDifficultyComputed = true;
                    continue;
                }
                cell.survivalDifficulty = result.difficulty;
                cell.minimumRiskPathExposure = result.pathExposure;
                cell.minimumRiskPathSeconds = result.pathSeconds;
                cell.minimumRiskPathCellCount = result.pathCellCount;
                cell.lowerThreatCellId = result.targetIndex >= 0 &&
                                         result.targetIndex < cells.Count
                    ? cells[result.targetIndex].stableId
                    : string.Empty;
                cell.survivalDifficultyComputed = true;
            }
        }

        static string DifficultyKey(
            string stableId,
            EdpcgFireAnalysisAltitudeLayer altitude)
        {
            return (stableId ?? string.Empty) + "#" + (int)altitude;
        }
    }

    [Serializable]
    public sealed class EdpcgGridFireCell
    {
        public string stableId = string.Empty;
        public int gridX;
        public int gridZ;
        public Bounds localBounds;
        public Vector3 localSamplePosition;
        public Vector3 worldSamplePosition;
        public Vector3[] worldCorners = Array.Empty<Vector3>();
        public bool flyable;
        public bool excludedFromDifficulty;
        [Range(0f, 1f)] public float targetDifficulty;
        [Range(0f, 1f)] public float difficultyCombatPressure;
        public int subSampleCount;
        public int flyableSubSampleCount;
        public float threatenedVolumeRatio;
        public float authorizedThreatenedVolumeRatio;
        public float pressureScore;
        public float fastestSetupSeconds = float.PositiveInfinity;
        public float fastestHitSeconds = float.PositiveInfinity;
        public float authorizedFastestHitSeconds = float.PositiveInfinity;
        public float fastestEscapeSeconds = float.PositiveInfinity;
        public float bestEscapeMarginSeconds = float.NegativeInfinity;
        public bool survivalDifficultyComputed;
        public float difficultyFireThreat;
        public float survivalDifficulty;
        public float minimumRiskPathExposure;
        public float minimumRiskPathSeconds;
        public int minimumRiskPathCellCount;
        public string lowerThreatCellId = string.Empty;
        public int incomingLineCount;
        public int blockedLineCount;
        public int gapWindowCount;
        public int fleetingWindowCount;
        public int threatSectorMask;
        public int potentialDirectionMaskLow;
        public int potentialDirectionMaskMid;
        public int potentialDirectionMaskHigh;
        public int forecastFourDirectionMaskLow;
        public int forecastFourDirectionMaskMid;
        public int forecastFourDirectionMaskHigh;
        public int forecastEightDirectionMaskLow;
        public int forecastEightDirectionMaskMid;
        public int forecastEightDirectionMaskHigh;
        public int authorizedDirectionMaskLow;
        public int authorizedDirectionMaskMid;
        public int authorizedDirectionMaskHigh;
        public int strikerPotentialDirectionMaskLow;
        public int strikerPotentialDirectionMaskMid;
        public int strikerPotentialDirectionMaskHigh;
        public int gunshipPotentialDirectionMaskLow;
        public int gunshipPotentialDirectionMaskMid;
        public int gunshipPotentialDirectionMaskHigh;
        public int strikerForecastFourDirectionMaskLow;
        public int strikerForecastFourDirectionMaskMid;
        public int strikerForecastFourDirectionMaskHigh;
        public int gunshipForecastFourDirectionMaskLow;
        public int gunshipForecastFourDirectionMaskMid;
        public int gunshipForecastFourDirectionMaskHigh;
        public int strikerForecastEightDirectionMaskLow;
        public int strikerForecastEightDirectionMaskMid;
        public int strikerForecastEightDirectionMaskHigh;
        public int gunshipForecastEightDirectionMaskLow;
        public int gunshipForecastEightDirectionMaskMid;
        public int gunshipForecastEightDirectionMaskHigh;
        public int strikerAuthorizedDirectionMaskLow;
        public int strikerAuthorizedDirectionMaskMid;
        public int strikerAuthorizedDirectionMaskHigh;
        public int gunshipAuthorizedDirectionMaskLow;
        public int gunshipAuthorizedDirectionMaskMid;
        public int gunshipAuthorizedDirectionMaskHigh;
        public int potentialDirectionCount;
        public int forecastFourDirectionCount;
        public int forecastEightDirectionCount;
        public int authorizedDirectionCount;
        public int authorizedSpatialChannelCount;
        // Backward-compatible display fields. They now describe EDPCG-
        // authorized directions rather than every geometrically visible ray.
        public int threatDirectionCount;
        public int safeExitCount;
        public bool crossfire;
        public readonly float[] threatenedVolumeRatioByRole = new float[2];
        public readonly float[] fastestHitSecondsByRole =
            { float.PositiveInfinity, float.PositiveInfinity };
        public readonly List<EdpcgAirFireWindow> fireWindows =
            new List<EdpcgAirFireWindow>(32);
        public readonly List<EdpcgGridFireLine> fireLines =
            new List<EdpcgGridFireLine>(48);
        public readonly List<EdpcgFiringVolumeOcclusionRay>
            firingVolumeOcclusions =
                new List<EdpcgFiringVolumeOcclusionRay>(24);
        public readonly List<EdpcgGridEvasionLink> evasions =
            new List<EdpcgGridEvasionLink>(12);

        public int DirectionCount(EdpcgAirThreatViewMode mode)
        {
            switch (mode)
            {
                case EdpcgAirThreatViewMode.Potential:
                    return potentialDirectionCount;
                case EdpcgAirThreatViewMode.WithinFourSeconds:
                    return forecastFourDirectionCount;
                case EdpcgAirThreatViewMode.WithinEightSeconds:
                    return forecastEightDirectionCount;
                default:
                    return authorizedDirectionCount;
            }
        }
    }

    [Serializable]
    public sealed class EdpcgGridFireAnalysis
    {
        readonly Dictionary<string, EdpcgGridFireCell> cellById =
            new Dictionary<string, EdpcgGridFireCell>(StringComparer.Ordinal);

        public int requestedSeed;
        public int resolvedSeed;
        public int algorithmVersion = 3;
        public EdpcgFireAnalysisAltitudeLayer altitudeLayer;
        public float altitude;
        public float referenceShipWidth;
        public float firingVolumeRadius = 220f;
        public float minimumFiringAltitude;
        public float gunshipMinimumFiringAltitude;
        public float maximumFiringAltitude;
        public float buildMilliseconds;
        public float geometryConfidence;
        public int sourceCount;
        public int fireWindowCount;
        public int gapWindowCount;
        public int fleetingWindowCount;
        public int incomingLineCount;
        public int blockedLineCount;
        public int flyableCellCount;
        public int analyzedSubSampleCount;
        public int authorizedConcurrentDirections;
        public readonly List<EdpcgGridFireCell> cells =
            new List<EdpcgGridFireCell>(64);

        public bool IsUsable => cells.Count > 0;

        internal void RebuildIndex()
        {
            cellById.Clear();
            for (int index = 0; index < cells.Count; index++)
            {
                EdpcgGridFireCell cell = cells[index];
                if (cell != null && !string.IsNullOrEmpty(cell.stableId))
                    cellById[cell.stableId] = cell;
            }
        }

        public bool TryGetCell(int gridX, int gridZ,
            out EdpcgGridFireCell cell)
        {
            return cellById.TryGetValue(
                "tactical-block." + gridX.ToString("D2") + "." +
                gridZ.ToString("D2"), out cell);
        }

        public EdpcgGridFireCell FindNearestCell(Vector3 worldPosition)
        {
            EdpcgGridFireCell best = null;
            float bestDistance = float.PositiveInfinity;
            for (int index = 0; index < cells.Count; index++)
            {
                EdpcgGridFireCell candidate = cells[index];
                if (candidate == null)
                    continue;
                Vector3 delta = candidate.worldSamplePosition - worldPosition;
                delta.y = 0f;
                float distance = delta.sqrMagnitude;
                if (distance >= bestDistance)
                    continue;
                best = candidate;
                bestDistance = distance;
            }
            return best;
        }
    }

    /// <summary>
    /// Public facade for deterministic, read-only three-dimensional air-
    /// threat analysis. The heavy solver lives in a separate file so EDPCG
    /// orchestration, city PCG and editor drawing remain separate owners.
    /// </summary>
    public static class EdpcgGridFireAnalyzer
    {
        public static EdpcgGridFireAnalysis Build(
            AirCombatCityPcgLab lab,
            EdpcgFireAnalysisAltitudeLayer altitudeLayer)
        {
            return Build(lab, altitudeLayer, -1f, null);
        }

        public static EdpcgGridFireAnalysis Build(
            AirCombatCityPcgLab lab,
            EdpcgFireAnalysisAltitudeLayer altitudeLayer,
            float referenceShipWidth)
        {
            return Build(lab, altitudeLayer, referenceShipWidth, null);
        }

        public static EdpcgGridFireAnalysis Build(
            AirCombatCityPcgLab lab,
            EdpcgFireAnalysisAltitudeLayer altitudeLayer,
            float referenceShipWidth,
            EdpcgAirThreatAnalysisOptions options)
        {
            if (lab == null)
                return new EdpcgGridFireAnalysis();
            return EdpcgAirThreatSolver.Build(
                lab.Plan, lab.RuntimeGeometrySnapshot, lab.Settings,
                altitudeLayer, referenceShipWidth,
                options ?? EdpcgAirThreatAnalysisOptions.CreateDefault(
                    lab.Settings),
                local => lab.transform.TransformPoint(local));
        }

        public static EdpcgGridFireAnalysis Build(
            FinitePlanetUrbanCombatRuntime urban,
            EdpcgFireAnalysisAltitudeLayer altitudeLayer)
        {
            return Build(urban, altitudeLayer, -1f, null);
        }

        public static EdpcgGridFireAnalysis Build(
            FinitePlanetUrbanCombatRuntime urban,
            EdpcgFireAnalysisAltitudeLayer altitudeLayer,
            float referenceShipWidth)
        {
            return Build(urban, altitudeLayer, referenceShipWidth, null);
        }

        public static EdpcgGridFireAnalysis Build(
            FinitePlanetUrbanCombatRuntime urban,
            EdpcgFireAnalysisAltitudeLayer altitudeLayer,
            float referenceShipWidth,
            EdpcgAirThreatAnalysisOptions options)
        {
            if (urban == null)
                return new EdpcgGridFireAnalysis();
            return EdpcgAirThreatSolver.Build(
                urban.Plan, urban.RuntimeGeometrySnapshot, urban.CitySettings,
                altitudeLayer, referenceShipWidth,
                options ?? EdpcgAirThreatAnalysisOptions.CreateDefault(
                    urban.CitySettings),
                local => urban.ProjectPlanPosition(local) +
                         Vector3.up * local.y);
        }

        public static void LinkAltitudeLayers(
            EdpcgGridFireAnalysis low,
            EdpcgGridFireAnalysis medium,
            EdpcgGridFireAnalysis high,
            AirCombatCityRuntimeGeometrySnapshot snapshot,
            EdpcgAirThreatAnalysisOptions options)
        {
            EdpcgAirThreatSolver.LinkAltitudeLayers(
                low, medium, high, snapshot,
                (options ?? new EdpcgAirThreatAnalysisOptions()).ValidatedCopy());
        }
    }
}
