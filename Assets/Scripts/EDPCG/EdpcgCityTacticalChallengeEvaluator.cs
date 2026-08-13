using System;

namespace UnityPlanet.EDPCG
{
    [Serializable]
    public sealed class EdpcgCityTacticalChallengeReport
    {
        public bool evaluable;
        public bool passed;
        public string summary = string.Empty;
        public int flyableCellCount;
        public int singleDirectionCellCount;
        public int crossfireCellCount;
        public int solvableCrossfireCellCount;
        public int coverCellCount;
        public int safeTransferCellCount;
        public int openPressureCellCount;
        public int verticalDifferenceCellCount;
        public int environmentalTrapRouteCount;
        public int establishedFireWindowCellCount;
        public int buildingGapWindowCellCount;
        public int fleetingVisibilityCellCount;
        public int positiveEscapeMarginCellCount;
        public float averageThreatenedVolumeRatio;
        public float averageFastestHitSeconds;
    }

    /// <summary>
    /// Pure read-only acceptance report for the authored city puzzle. This is
    /// deliberately diagnostic: it does not start another generation loop or
    /// silently replace the mission seed while a level is loading.
    /// </summary>
    public static class EdpcgCityTacticalChallengeEvaluator
    {
        public static EdpcgCityTacticalChallengeReport Evaluate(
            EdpcgCityTacticalChallengeSettings settings,
            EdpcgGridFireAnalysis low,
            EdpcgGridFireAnalysis medium,
            EdpcgGridFireAnalysis high,
            EdpcgCityTacticalRuntimeMap tacticalMap)
        {
            var report = new EdpcgCityTacticalChallengeReport();
            if (settings == null || medium == null || !medium.IsUsable)
            {
                report.summary = "缺少中空层方格枪线数据。";
                return report;
            }

            report.evaluable = true;
            float threatenedVolumeTotal = 0f;
            float fastestHitTotal = 0f;
            int fastestHitSamples = 0;
            for (int index = 0; index < medium.cells.Count; index++)
            {
                EdpcgGridFireCell cell = medium.cells[index];
                if (cell == null || !cell.flyable)
                    continue;
                report.flyableCellCount++;
                if (cell.authorizedDirectionCount == 1)
                    report.singleDirectionCellCount++;
                if (cell.crossfire)
                {
                    report.crossfireCellCount++;
                    if (cell.safeExitCount >= settings.minimumSafeExitCount)
                        report.solvableCrossfireCellCount++;
                }
                if (cell.potentialDirectionCount > 0 &&
                    cell.threatenedVolumeRatio < 0.86f &&
                    cell.blockedLineCount > 0)
                    report.coverCellCount++;
                if (cell.safeExitCount >= settings.minimumSafeExitCount)
                    report.safeTransferCellCount++;
                if (cell.fireWindows.Count > 0)
                    report.establishedFireWindowCellCount++;
                if (cell.gapWindowCount > 0)
                    report.buildingGapWindowCellCount++;
                if (cell.fleetingWindowCount > 0)
                    report.fleetingVisibilityCellCount++;
                if (cell.bestEscapeMarginSeconds > 0f)
                    report.positiveEscapeMarginCellCount++;
                threatenedVolumeTotal += cell.threatenedVolumeRatio;
                if (!float.IsPositiveInfinity(cell.fastestHitSeconds))
                {
                    fastestHitTotal += cell.fastestHitSeconds;
                    fastestHitSamples++;
                }
                if (cell.pressureScore >= 0.42f &&
                    cell.authorizedDirectionCount > 0 &&
                    cell.safeExitCount > 0)
                {
                    report.openPressureCellCount++;
                }
            }
            if (report.flyableCellCount > 0)
                report.averageThreatenedVolumeRatio =
                    threatenedVolumeTotal / report.flyableCellCount;
            if (fastestHitSamples > 0)
                report.averageFastestHitSeconds =
                    fastestHitTotal / fastestHitSamples;

            if (low != null && low.IsUsable && high != null && high.IsUsable)
            {
                for (int index = 0; index < medium.cells.Count; index++)
                {
                    EdpcgGridFireCell middle = medium.cells[index];
                    if (middle == null ||
                        !low.TryGetCell(middle.gridX, middle.gridZ,
                            out EdpcgGridFireCell lower) ||
                        !high.TryGetCell(middle.gridX, middle.gridZ,
                            out EdpcgGridFireCell upper) ||
                        lower == null || upper == null)
                    {
                        continue;
                    }
                    float minimum = Math.Min(lower.pressureScore,
                        Math.Min(middle.pressureScore, upper.pressureScore));
                    float maximum = Math.Max(lower.pressureScore,
                        Math.Max(middle.pressureScore, upper.pressureScore));
                    if (maximum - minimum >= 0.18f)
                        report.verticalDifferenceCellCount++;
                }
            }

            if (tacticalMap != null)
            {
                for (int index = 0; index < tacticalMap.Routes.Count; index++)
                {
                    EdpcgRuntimeRoute route = tacticalMap.Routes[index];
                    if (route != null && route.IsEnvironmentalTrap)
                        report.environmentalTrapRouteCount++;
                }
            }

            int minimumCrossfire = settings.minimumCrossfireCellCount;
            int maximumCrossfire = settings.maximumCrossfireCellCount;
            bool crossfireRange = report.crossfireCellCount >=
                                  minimumCrossfire &&
                                  report.crossfireCellCount <=
                                  maximumCrossfire;
            int minimumSafeCells = Math.Max(6,
                settings.minimumSafeExitCount * 4);
            switch (settings.puzzleKind)
            {
                case EdpcgCityTacticalPuzzleKind.SingleSidePressure:
                    report.passed = report.singleDirectionCellCount >= 8 &&
                                    crossfireRange &&
                                    report.safeTransferCellCount >=
                                    minimumSafeCells;
                    report.summary = report.passed
                        ? "按本难度令牌与火线配额，单侧建线和正余量转移满足诊断目标。"
                        : "单侧可执行建线或正余量转移不足；不要用潜在枪位数量替代实际并发。";
                    break;
                case EdpcgCityTacticalPuzzleKind.CrossfireBreak:
                    report.passed = crossfireRange &&
                                    report.solvableCrossfireCellCount >=
                                    Math.Max(2, minimumCrossfire / 2);
                    report.summary = report.passed
                        ? "时间窗重叠且本档可授权的交叉火力在目标区间，并保留通往更低火力格的路径。"
                        : "可执行交叉火力数量或通往更低火力格的正余量路径未达到目标。";
                    break;
                case EdpcgCityTacticalPuzzleKind.CoverRelay:
                    report.passed = report.coverCellCount >= 16 &&
                                    report.safeTransferCellCount >=
                                    minimumSafeCells && crossfireRange;
                    report.summary = report.passed
                        ? "掩体持续率和带时间余量的连续转移达到诊断目标。"
                        : "建筑可能很多，但没有形成足够的持续遮挡与正余量转移链。";
                    break;
                case EdpcgCityTacticalPuzzleKind.TrapLure:
                    report.passed = report.environmentalTrapRouteCount >= 2 &&
                                    report.safeTransferCellCount >=
                                    minimumSafeCells;
                    report.summary = report.environmentalTrapRouteCount == 0
                        ? "编辑预览没有真实环境陷阱路线；只能在正式城市运行时完成此题目验收。"
                        : report.passed
                            ? "诱敌路线与玩家转入更低火力格的路径均已满足。"
                            : "陷阱追击路线或更低火力转移路径不足。";
                    break;
                case EdpcgCityTacticalPuzzleKind.VerticalPressure:
                    report.passed = low != null && low.IsUsable &&
                                    high != null && high.IsUsable &&
                                    report.verticalDifferenceCellCount >= 8 &&
                                    report.safeTransferCellCount >=
                                    minimumSafeCells;
                    report.summary = report.passed
                        ? "低中高空之间存在可读的压力差，并保留转层余量。"
                        : "三个高度层的压力差或转层余量不足。";
                    break;
                default:
                    report.passed = report.openPressureCellCount >= 6 &&
                                    report.safeTransferCellCount >=
                                    minimumSafeCells && crossfireRange;
                    report.summary = report.passed
                        ? "开放捷径具有火力代价，同时仍保留主动退出选择。"
                        : "开放捷径尚未形成清楚的风险与退出选择。";
                    break;
            }
            return report;
        }
    }
}
