using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityPlanet.CityPcg
{
    /// <summary>
    /// 城市生成阶段使用的难度目标。它只描述城市空间，不包含敌人数量、
    /// 刷新间隔、攻击令牌或 Boss 数据。
    /// </summary>
    [Serializable]
    public sealed class AirCombatCityDifficultyTarget
    {
        [Range(0f, 1f)] public float averageDifficulty;
        [Range(0f, 1f)] public float safeCellRatio;
        [Range(0f, 1f)] public float highRiskCellRatio;
        [Min(1)] public int preferredMaximumCellsToLowerThreat;
    }

    [Serializable]
    public sealed class AirCombatCityDifficultyCell
    {
        public string stableId = string.Empty;
        public int gridX;
        public int gridZ;
        public CombatCityBlockRole role;
        public Vector3 samplePosition;
        public bool flyable;
        public bool generated;
        public bool excludedFromDifficulty;
        [Range(0f, 1f)] public float fireThreat;
        [Range(0f, 1f)] public float targetDifficulty;
        [Range(0f, 1f)] public float survivalDifficulty;
        public int minimumRiskPathCellCount;
        public float minimumRiskPathSeconds;
        public float minimumRiskPathExposure;
        public string lowerThreatCellId = string.Empty;
    }

    [Serializable]
    public sealed class AirCombatCityDifficultyEvaluation
    {
        public int requestedSeed;
        public int resolvedSeed;
        public AirCombatCityDifficultyTarget target =
            new AirCombatCityDifficultyTarget();
        public readonly List<AirCombatCityDifficultyCell> cells =
            new List<AirCombatCityDifficultyCell>(64);
        public int flyableCellCount;
        public float averageDifficulty;
        public float safeCellRatio;
        public float highRiskCellRatio;
        public float averageTargetError;
        public float targetFitError;
        public int maximumCellsToLowerThreat;
        public bool targetMet;

        public bool IsUsable => cells.Count > 0 && flyableCellCount > 0;
    }

    /// <summary>
    /// 共享的“留守或转移”生存成本求解器。城市 PCG 和 EDPCG 编辑器诊断
    /// 都可以把自己的格子与边转换为该中立契约；求解器不知道敌人对象、
    /// 玩家刚体、Boss 或 Unity 编辑器窗口。
    /// </summary>
    public static class AirCombatGridSurvivalSolver
    {
        public const float SafeThreatThreshold = 0.18f;
        public const float RequiredThreatReduction = 0.16f;
        public const float ExposureBudgetSeconds = 4.5f;

        public struct Edge
        {
            public int from;
            public int to;
            public float travelSeconds;
        }

        public struct Result
        {
            public float difficulty;
            public float pathExposure;
            public float pathSeconds;
            public int pathCellCount;
            public int targetIndex;
        }

        public static Result[] Solve(
            float[] fireThreat,
            bool[] flyable,
            IList<Edge> edges)
        {
            int count = fireThreat?.Length ?? 0;
            var results = new Result[count];
            if (count == 0 || flyable == null || flyable.Length != count)
                return results;

            var adjacency = new List<Edge>[count];
            for (int index = 0; index < count; index++)
                adjacency[index] = new List<Edge>(8);
            if (edges != null)
            {
                for (int index = 0; index < edges.Count; index++)
                {
                    Edge edge = edges[index];
                    if (edge.from < 0 || edge.from >= count ||
                        edge.to < 0 || edge.to >= count ||
                        !flyable[edge.from] || !flyable[edge.to])
                    {
                        continue;
                    }
                    edge.travelSeconds = Mathf.Max(0.01f,
                        edge.travelSeconds);
                    adjacency[edge.from].Add(edge);
                }
            }

            for (int source = 0; source < count; source++)
            {
                float stayRisk = Mathf.Clamp01(fireThreat[source]);
                results[source] = new Result
                {
                    difficulty = flyable[source] ? stayRisk : 1f,
                    pathExposure = 0f,
                    pathSeconds = 0f,
                    pathCellCount = 0,
                    targetIndex = source
                };
                if (!flyable[source] || stayRisk <= SafeThreatThreshold)
                    continue;

                float targetThreshold = Mathf.Max(
                    SafeThreatThreshold,
                    stayRisk - RequiredThreatReduction);
                float[] exposure = new float[count];
                float[] seconds = new float[count];
                int[] steps = new int[count];
                bool[] visited = new bool[count];
                for (int index = 0; index < count; index++)
                {
                    exposure[index] = float.PositiveInfinity;
                    seconds[index] = float.PositiveInfinity;
                    steps[index] = int.MaxValue;
                }
                exposure[source] = 0f;
                seconds[source] = 0f;
                steps[source] = 0;

                int resolvedTarget = -1;
                for (int iteration = 0; iteration < count; iteration++)
                {
                    int current = -1;
                    float best = float.PositiveInfinity;
                    for (int index = 0; index < count; index++)
                    {
                        if (visited[index] || !flyable[index] ||
                            exposure[index] >= best)
                        {
                            continue;
                        }
                        current = index;
                        best = exposure[index];
                    }
                    if (current < 0)
                        break;
                    visited[current] = true;
                    if (current != source &&
                        fireThreat[current] <= targetThreshold)
                    {
                        resolvedTarget = current;
                        break;
                    }

                    List<Edge> links = adjacency[current];
                    for (int linkIndex = 0;
                         linkIndex < links.Count;
                         linkIndex++)
                    {
                        Edge edge = links[linkIndex];
                        if (visited[edge.to])
                            continue;
                        float averageThreat = (
                            Mathf.Clamp01(fireThreat[current]) +
                            Mathf.Clamp01(fireThreat[edge.to])) * 0.5f;
                        float candidateExposure = exposure[current] +
                                                  averageThreat *
                                                  edge.travelSeconds;
                        float candidateSeconds = seconds[current] +
                                                 edge.travelSeconds;
                        int candidateSteps = steps[current] + 1;
                        bool better = candidateExposure <
                                      exposure[edge.to] - 0.0001f;
                        if (!better && Mathf.Abs(candidateExposure -
                                exposure[edge.to]) <= 0.0001f)
                        {
                            better = candidateSeconds < seconds[edge.to] ||
                                     (Mathf.Approximately(candidateSeconds,
                                          seconds[edge.to]) &&
                                      candidateSteps < steps[edge.to]);
                        }
                        if (!better)
                            continue;
                        exposure[edge.to] = candidateExposure;
                        seconds[edge.to] = candidateSeconds;
                        steps[edge.to] = candidateSteps;
                    }
                }

                if (resolvedTarget < 0)
                    continue;
                // 起点受到的第一轮威胁不可完全抹去；之后按整条路径的
                // 时间积分计算风险。玩家可以在“留守”和“转移”中选择
                // 生存成本更低的一项。
                float escapeRisk = Mathf.Clamp01(
                    stayRisk * 0.20f +
                    exposure[resolvedTarget] / ExposureBudgetSeconds);
                if (escapeRisk >= stayRisk)
                    continue;
                results[source] = new Result
                {
                    difficulty = escapeRisk,
                    pathExposure = exposure[resolvedTarget],
                    pathSeconds = seconds[resolvedTarget],
                    pathCellCount = steps[resolvedTarget],
                    targetIndex = resolvedTarget
                };
            }
            return results;
        }
    }

    /// <summary>
    /// 城市 PCG 的只读难度估算器。它把计划中的建筑包围盒、飞行路线与
    /// 8×8 战术街区转换成静态枪线和可飞图，再调用共享生存成本求解器。
    /// </summary>
    public static class AirCombatCityDifficultyPcg
    {
        public const float MinimumTargetDifficulty = 0.10f;
        public const float MaximumTargetDifficulty = 0.90f;

        const float EnemyWeaponRange = 420f;
        const float ProjectileSpeed = 240f;
        const float FireCorridorRadius = 5f;
        const float RouteSampleSpacing = 42f;
        const float StrikerFiringRadius = 115f;
        const float GunshipFiringRadius = 165f;
        const float MaximumFiringApproachDistance = 420f;

        struct Anchor
        {
            public Vector3 position;
        }

        sealed class BuildingSpatialIndex
        {
            const float CellSize = 96f;
            readonly List<Bounds> bounds;
            readonly AirCombatRuntimeConnectionGeometry[] bridges;
            readonly Dictionary<long, List<int>> buckets =
                new Dictionary<long, List<int>>(256);
            readonly int[] visited;
            int queryStamp;

            public BuildingSpatialIndex(
                List<Bounds> source,
                AirCombatRuntimeConnectionGeometry[] bridgeSource = null)
            {
                bounds = source ?? new List<Bounds>();
                bridges = bridgeSource ??
                          Array.Empty<AirCombatRuntimeConnectionGeometry>();
                visited = new int[bounds.Count];
                for (int index = 0; index < bounds.Count; index++)
                {
                    Bounds item = bounds[index];
                    int minX = Bucket(item.min.x);
                    int maxX = Bucket(item.max.x);
                    int minZ = Bucket(item.min.z);
                    int maxZ = Bucket(item.max.z);
                    for (int x = minX; x <= maxX; x++)
                    for (int z = minZ; z <= maxZ; z++)
                    {
                        long key = Key(x, z);
                        if (!buckets.TryGetValue(key,
                                out List<int> list))
                        {
                            list = new List<int>(8);
                            buckets.Add(key, list);
                        }
                        list.Add(index);
                    }
                }
            }

            public bool PointBlocked(Vector3 point, float radius)
            {
                BeginQuery();
                int minX = Bucket(point.x - radius);
                int maxX = Bucket(point.x + radius);
                int minZ = Bucket(point.z - radius);
                int maxZ = Bucket(point.z + radius);
                for (int x = minX; x <= maxX; x++)
                for (int z = minZ; z <= maxZ; z++)
                {
                    if (!buckets.TryGetValue(Key(x, z),
                            out List<int> list))
                        continue;
                    for (int item = 0; item < list.Count; item++)
                    {
                        int index = list[item];
                        if (visited[index] == queryStamp)
                            continue;
                        visited[index] = queryStamp;
                        Bounds expanded = bounds[index];
                        expanded.Expand(radius * 2f);
                        if (expanded.Contains(point))
                            return true;
                    }
                }
                for (int index = 0; index < bridges.Length; index++)
                {
                    AirCombatRuntimeConnectionGeometry bridge =
                        bridges[index];
                    float bridgeRadius = bridge.physicalRadius > 0f
                        ? bridge.physicalRadius
                        : 4.5f;
                    int pointCount = ConnectionPointCount(bridge);
                    for (int segment = 1; segment < pointCount; segment++)
                    {
                        if (SqrDistancePointSegment(point,
                                ConnectionPointAt(bridge, segment - 1),
                                ConnectionPointAt(bridge, segment)) <=
                            Mathf.Pow(radius + bridgeRadius, 2f))
                        {
                            return true;
                        }
                    }
                }
                return false;
            }

            public bool CorridorClear(
                Vector3 start,
                Vector3 end,
                float radius)
            {
                Vector3 direction = end - start;
                float distance = direction.magnitude;
                if (distance <= 0.001f)
                    return !PointBlocked(start, radius);
                Ray ray = new Ray(start, direction / distance);
                BeginQuery();
                int minX = Bucket(Mathf.Min(start.x, end.x) - radius);
                int maxX = Bucket(Mathf.Max(start.x, end.x) + radius);
                int minZ = Bucket(Mathf.Min(start.z, end.z) - radius);
                int maxZ = Bucket(Mathf.Max(start.z, end.z) + radius);
                for (int x = minX; x <= maxX; x++)
                for (int z = minZ; z <= maxZ; z++)
                {
                    if (!buckets.TryGetValue(Key(x, z),
                            out List<int> list))
                        continue;
                    for (int item = 0; item < list.Count; item++)
                    {
                        int index = list[item];
                        if (visited[index] == queryStamp)
                            continue;
                        visited[index] = queryStamp;
                        Bounds expanded = bounds[index];
                        expanded.Expand(radius * 2f);
                        if (expanded.Contains(start) ||
                            expanded.Contains(end) ||
                            expanded.IntersectRay(ray, out float hit) &&
                            hit <= distance)
                        {
                            return false;
                        }
                    }
                }
                for (int index = 0; index < bridges.Length; index++)
                {
                    AirCombatRuntimeConnectionGeometry bridge =
                        bridges[index];
                    float bridgeRadius = bridge.physicalRadius > 0f
                        ? bridge.physicalRadius
                        : 4.5f;
                    float combined = radius + bridgeRadius;
                    int pointCount = ConnectionPointCount(bridge);
                    for (int segment = 1; segment < pointCount; segment++)
                    {
                        if (SqrDistanceSegments(start, end,
                                ConnectionPointAt(bridge, segment - 1),
                                ConnectionPointAt(bridge, segment)) <=
                            combined * combined)
                        {
                            return false;
                        }
                    }
                }
                return true;
            }

            void BeginQuery()
            {
                if (queryStamp == int.MaxValue)
                {
                    for (int index = 0; index < visited.Length; index++)
                        visited[index] = 0;
                    queryStamp = 1;
                    return;
                }
                queryStamp++;
            }

            static int Bucket(float value)
            {
                return Mathf.FloorToInt(value / CellSize);
            }

            static long Key(int x, int z)
            {
                return ((long)x << 32) ^ (uint)z;
            }
        }

        public static AirCombatCityDifficultyTarget ResolveTarget(
            CombatCityDifficultyProfile difficulty)
        {
            CombatCityDifficultyProfile d = difficulty ??
                                             new CombatCityDifficultyProfile();
            float challenge = Mathf.Clamp01(
                d.exposurePressure * 0.72f +
                d.combatPressure * 0.28f);
            return new AirCombatCityDifficultyTarget
            {
                // 静态城市只负责空间题目，不把敌群数量和攻击令牌提前
                // 算进来。因此最低档也不是“零枪线”，最高档也不要求
                // 靠纯几何达到运行时总压力的红线。
                // 目标必须覆盖真正安全到真正暴露的空间，而不是把六档
                // 都压在中等难度附近。上限仍低于纯开放空间的理论值，
                // 给道路、恢复区和固定任务设施保留可实现余量。
                averageDifficulty = d.useExplicitAverageDifficulty
                    ? Mathf.Clamp(d.explicitAverageDifficulty,
                        MinimumTargetDifficulty,
                        MaximumTargetDifficulty)
                    : Mathf.Lerp(0.18f, 0.82f, challenge),
                safeCellRatio = Mathf.Clamp01(
                    Mathf.Lerp(0.18f, 0.04f, challenge) *
                    Mathf.Lerp(0.88f, 1.12f, d.recoveryGenerosity)),
                // 红区比例与新的宽目标域同步：低档仍以绿黄为主，
                // 高档允许连续暴露盆地占据大部分内部格，而不是把
                // “实际已经很危险”误判成超过一个过低的红区配额。
                highRiskCellRatio = Mathf.Clamp01(
                    0.95f * Mathf.Pow(challenge, 2f)),
                preferredMaximumCellsToLowerThreat = Mathf.RoundToInt(
                    Mathf.Lerp(2f, 5f, challenge))
            };
        }

        public static float ResolveMaximumRepresentableDifficulty(
            CombatCityDifficultyProfile difficulty)
        {
            return EvaluateFireThreatMetrics(
                1f,
                8,
                1f,
                true,
                difficulty?.combatPressure ?? 0.45f);
        }

        public static AirCombatCityDifficultyEvaluation Evaluate(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan,
            AirCombatCityRuntimeGeometrySnapshot snapshot = null)
        {
            var evaluation = new AirCombatCityDifficultyEvaluation
            {
                requestedSeed = plan?.requestedSeed ?? settings?.seed ?? 0,
                resolvedSeed = plan?.resolvedSeed ?? settings?.seed ?? 0,
                target = ResolveTarget(settings?.Difficulty)
            };
            if (settings == null || plan == null ||
                plan.tacticalBlocks == null ||
                plan.tacticalBlocks.Count == 0)
            {
                return evaluation;
            }

            List<Bounds> buildings = BuildBuildingBounds(plan, snapshot);
            var geometry = new BuildingSpatialIndex(buildings,
                snapshot?.skybridges);
            List<Anchor> anchors = BuildAnchors(plan, snapshot);
            int count = plan.tacticalBlocks.Count;
            bool incrementalGeneration = false;
            for (int index = 0; index < count; index++)
            {
                if (plan.tacticalBlocks[index] != null &&
                    plan.tacticalBlocks[index].generatedForDifficulty)
                {
                    incrementalGeneration = true;
                    break;
                }
            }
            float[] threats = new float[count];
            bool[] flyable = new bool[count];
            var edges = new List<AirCombatGridSurvivalSolver.Edge>(
                count * 8);
            var indexByCoordinate = new Dictionary<int, int>(count);
            float hullRadius = Mathf.Max(FireCorridorRadius,
                settings.wingspan * 0.5f);

            for (int index = 0; index < count; index++)
            {
                CombatCityBlockPlan block = plan.tacticalBlocks[index];
                var cell = new AirCombatCityDifficultyCell
                {
                    stableId = block?.stableId ?? string.Empty,
                    gridX = block?.gridX ?? 0,
                    gridZ = block?.gridZ ?? 0,
                    role = block?.role ?? CombatCityBlockRole.Maneuver,
                    generated = block != null &&
                                (!incrementalGeneration ||
                                 block.generatedForDifficulty),
                    excludedFromDifficulty = block != null &&
                                               block.excludedFromDifficulty,
                    targetDifficulty = ResolveBlockTarget(
                        evaluation.target.averageDifficulty,
                        block?.role ?? CombatCityBlockRole.Maneuver)
                };
                if (cell.generated && !cell.excludedFromDifficulty &&
                    block != null && EvaluateCellFireThreat(
                        block.bounds,
                        settings,
                        hullRadius,
                        anchors,
                        geometry,
                        out Vector3 sample,
                        out float fireThreat))
                {
                    cell.flyable = true;
                    cell.samplePosition = sample;
                    cell.fireThreat = fireThreat;
                    flyable[index] = true;
                    threats[index] = cell.fireThreat;
                    evaluation.flyableCellCount++;
                }
                else
                {
                    cell.flyable = false;
                    cell.fireThreat = 1f;
                    threats[index] = 1f;
                }
                evaluation.cells.Add(cell);
                indexByCoordinate[CoordinateKey(cell.gridX, cell.gridZ)] =
                    index;
            }

            for (int index = 0; index < count; index++)
            {
                if (!flyable[index])
                    continue;
                AirCombatCityDifficultyCell source = evaluation.cells[index];
                for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dz == 0)
                        continue;
                    if (!indexByCoordinate.TryGetValue(CoordinateKey(
                            source.gridX + dx, source.gridZ + dz),
                            out int targetIndex) ||
                        !flyable[targetIndex])
                    {
                        continue;
                    }
                    Vector3 target = evaluation.cells[targetIndex]
                        .samplePosition;
                    if (!TryResolveTraversalSeconds(
                            source.samplePosition,
                            target,
                            settings,
                            hullRadius,
                            geometry,
                            snapshot,
                            out float travelSeconds))
                        continue;
                    edges.Add(new AirCombatGridSurvivalSolver.Edge
                    {
                        from = index,
                        to = targetIndex,
                        travelSeconds = travelSeconds
                    });
                }
            }

            AirCombatGridSurvivalSolver.Result[] survival =
                AirCombatGridSurvivalSolver.Solve(threats, flyable, edges);
            float difficultyTotal = 0f;
            float errorTotal = 0f;
            int safeCount = 0;
            int highCount = 0;
            int considered = 0;
            int evaluableCount = 0;
            for (int index = 0; index < count; index++)
            {
                AirCombatCityDifficultyCell cell = evaluation.cells[index];
                AirCombatGridSurvivalSolver.Result result = survival[index];
                cell.survivalDifficulty = result.difficulty;
                cell.minimumRiskPathCellCount = result.pathCellCount;
                cell.minimumRiskPathSeconds = result.pathSeconds;
                cell.minimumRiskPathExposure = result.pathExposure;
                if (result.targetIndex >= 0 && result.targetIndex < count)
                {
                    cell.lowerThreatCellId = evaluation.cells[
                        result.targetIndex].stableId;
                }
                if (!cell.excludedFromDifficulty && cell.generated)
                    evaluableCount++;
                if (!cell.flyable || cell.excludedFromDifficulty ||
                    !cell.generated)
                    continue;
                considered++;
                difficultyTotal += cell.survivalDifficulty;
                errorTotal += Mathf.Abs(cell.survivalDifficulty -
                                        cell.targetDifficulty);
                evaluation.maximumCellsToLowerThreat = Mathf.Max(
                    evaluation.maximumCellsToLowerThreat,
                    cell.minimumRiskPathCellCount);
                if (cell.survivalDifficulty < 0.18f)
                    safeCount++;
                if (cell.survivalDifficulty >= 0.58f)
                    highCount++;
            }
            if (considered <= 0)
                return evaluation;

            evaluation.averageDifficulty = difficultyTotal / considered;
            evaluation.safeCellRatio = safeCount / (float)considered;
            evaluation.highRiskCellRatio = highCount / (float)considered;
            evaluation.averageTargetError = errorTotal / considered;
            float flyableFailure = 1f - considered /
                                   (float)Mathf.Max(1, evaluableCount);
            float globalAverageError = Mathf.Abs(
                evaluation.averageDifficulty -
                evaluation.target.averageDifficulty);
            evaluation.targetFitError = Mathf.Clamp01(
                globalAverageError * 0.45f +
                evaluation.averageTargetError * 0.20f +
                Mathf.Abs(evaluation.safeCellRatio -
                          evaluation.target.safeCellRatio) * 0.18f +
                Mathf.Abs(evaluation.highRiskCellRatio -
                          evaluation.target.highRiskCellRatio) * 0.12f +
                flyableFailure * 0.05f);
            // 区块角色目标是候选选择的软先验，不再作为整城硬门。
            // 权威关卡要求是全城危险总量和安全/高危分布；否则即使
            // 贪心器精确命中全局目标，也会被开局固定的角色偏移否决。
            evaluation.targetMet = globalAverageError <= 0.10f &&
                                   considered >= Mathf.CeilToInt(
                                       evaluableCount * 0.86f);
            return evaluation;
        }

        public static void ApplyToReport(
            AirCombatCityDifficultyEvaluation evaluation,
            AirCombatCityReport report,
            bool finalGeometry)
        {
            if (evaluation == null || report == null || !evaluation.IsUsable)
                return;
            if (finalGeometry)
            {
                report.finalDifficultyEvaluated = true;
                report.finalDifficultyTargetMet = evaluation.targetMet;
                report.finalAverageDifficulty = evaluation.averageDifficulty;
                report.finalSafeCellRatio = evaluation.safeCellRatio;
                report.finalHighRiskCellRatio = evaluation.highRiskCellRatio;
                report.finalDifficultyFitError = evaluation.targetFitError;
                return;
            }
            report.cityDifficultyEvaluated = true;
            report.cityDifficultyTargetMet = evaluation.targetMet;
            report.targetAverageDifficulty =
                evaluation.target.averageDifficulty;
            report.plannedAverageDifficulty = evaluation.averageDifficulty;
            report.plannedSafeCellRatio = evaluation.safeCellRatio;
            report.plannedHighRiskCellRatio = evaluation.highRiskCellRatio;
            report.plannedDifficultyFitError = evaluation.targetFitError;
            report.plannedMaximumCellsToLowerThreat =
                evaluation.maximumCellsToLowerThreat;
        }

        static List<Bounds> BuildBuildingBounds(
            AirCombatCityPlan plan,
            AirCombatCityRuntimeGeometrySnapshot snapshot)
        {
            var result = new List<Bounds>(512);
            if (snapshot != null && snapshot.IsUsable &&
                snapshot.buildings != null)
            {
                for (int index = 0;
                     index < snapshot.buildings.Length;
                     index++)
                {
                    result.Add(snapshot.buildings[index].localBounds);
                }
                return result;
            }
            for (int index = 0; index < plan.buildings.Count; index++)
            {
                AirCombatBuildingLot lot = plan.buildings[index];
                if (lot != null)
                    result.Add(new Bounds(lot.center, lot.size));
            }
            return result;
        }

        static List<Anchor> BuildAnchors(
            AirCombatCityPlan plan,
            AirCombatCityRuntimeGeometrySnapshot snapshot)
        {
            var result = new List<Anchor>(192);
            if (snapshot != null && snapshot.routes != null &&
                snapshot.routes.Length > 0)
            {
                for (int index = 0; index < snapshot.routes.Length; index++)
                {
                    AirCombatRuntimeRouteGeometry route =
                        snapshot.routes[index];
                    AddRouteAnchors(route.kind, route.localPoints, result);
                }
            }
            else
            {
                for (int index = 0; index < plan.routes.Count; index++)
                {
                    AirCombatFlightRoute route = plan.routes[index];
                    if (route != null)
                        AddRouteAnchors(route.kind, route.points, result);
                }
            }
            for (int index = 0; index < plan.ingresses.Count; index++)
            {
                AirCombatEnemyIngress ingress = plan.ingresses[index];
                if (ingress != null)
                    result.Add(new Anchor { position = ingress.position });
            }
            return result;
        }

        static void AddRouteAnchors(
            AirCombatRouteKind kind,
            Vector3[] points,
            List<Anchor> result)
        {
            if (points == null || points.Length == 0 ||
                (kind != AirCombatRouteKind.Main &&
                 kind != AirCombatRouteKind.MaskedFlank &&
                 kind != AirCombatRouteKind.LongRange &&
                 kind != AirCombatRouteKind.EnemyIngress))
            {
                return;
            }
            if (points.Length == 1)
            {
                result.Add(new Anchor { position = points[0] });
                return;
            }
            for (int index = 1; index < points.Length; index++)
            {
                Vector3 start = points[index - 1];
                Vector3 end = points[index];
                float distance = Vector3.Distance(start, end);
                int samples = Mathf.Max(1,
                    Mathf.CeilToInt(distance / RouteSampleSpacing));
                for (int sample = 0; sample <= samples; sample++)
                {
                    result.Add(new Anchor
                    {
                        position = Vector3.Lerp(start, end,
                            sample / (float)samples)
                    });
                }
            }
        }

        static bool TryResolveCellSample(
            Bounds bounds,
            float altitude,
            float radius,
            List<Bounds> buildings,
            out Vector3 sample)
        {
            Vector3 extent = bounds.extents;
            Vector2[] offsets =
            {
                Vector2.zero,
                new Vector2(-0.28f, -0.28f),
                new Vector2(0.28f, -0.28f),
                new Vector2(-0.28f, 0.28f),
                new Vector2(0.28f, 0.28f),
                new Vector2(-0.34f, 0f),
                new Vector2(0.34f, 0f),
                new Vector2(0f, -0.34f),
                new Vector2(0f, 0.34f)
            };
            for (int index = 0; index < offsets.Length; index++)
            {
                Vector3 candidate = new Vector3(
                    bounds.center.x + extent.x * offsets[index].x * 2f,
                    altitude,
                    bounds.center.z + extent.z * offsets[index].y * 2f);
                if (PointBlocked(candidate, radius, buildings))
                    continue;
                sample = candidate;
                return true;
            }
            sample = new Vector3(bounds.center.x, altitude, bounds.center.z);
            return false;
        }

        static bool EvaluateCellFireThreat(
            Bounds bounds,
            AirCombatCitySettings settings,
            float hullRadius,
            List<Anchor> anchors,
            BuildingSpatialIndex geometry,
            out Vector3 representative,
            out float fireThreat)
        {
            // 一个200米级宏观格不能由单个道路中心点代表。这里用中空层
            // 的中心与四个内角，再补低/高空中心，共7个体积采样点。
            // 建筑内部不计入可飞体积；威胁按可飞样本平均，因此一条
            // 楼缝不会把整格染红，大片无掩体也不会被少量楼体洗绿。
            Vector2[] horizontalOffsets =
            {
                Vector2.zero,
                new Vector2(-0.30f, -0.30f),
                new Vector2(0.30f, -0.30f),
                new Vector2(-0.30f, 0.30f),
                new Vector2(0.30f, 0.30f)
            };
            representative = new Vector3(bounds.center.x,
                settings.mediumAltitude, bounds.center.z);
            bool representativeResolved = false;
            float total = 0f;
            int flyableSamples = 0;
            for (int index = 0; index < horizontalOffsets.Length; index++)
            {
                Vector2 offset = horizontalOffsets[index];
                Vector3 sample = new Vector3(
                    bounds.center.x + bounds.extents.x * offset.x * 2f,
                    settings.mediumAltitude,
                    bounds.center.z + bounds.extents.z * offset.y * 2f);
                if (geometry.PointBlocked(sample, hullRadius))
                    continue;
                if (!representativeResolved)
                {
                    representative = sample;
                    representativeResolved = true;
                }
                total += EvaluateFireThreatAt(sample, settings, hullRadius,
                    anchors, geometry);
                flyableSamples++;
            }
            float[] extraAltitudes =
            {
                settings.lowAltitude,
                settings.highAltitude
            };
            for (int index = 0; index < extraAltitudes.Length; index++)
            {
                Vector3 sample = new Vector3(bounds.center.x,
                    extraAltitudes[index], bounds.center.z);
                if (geometry.PointBlocked(sample, hullRadius))
                    continue;
                total += EvaluateFireThreatAt(sample, settings, hullRadius,
                    anchors, geometry);
                flyableSamples++;
            }
            fireThreat = flyableSamples > 0
                ? Mathf.Clamp01(total / flyableSamples)
                : 1f;
            return flyableSamples > 0 && representativeResolved;
        }

        static float EvaluateFireThreatAt(
            Vector3 target,
            AirCombatCitySettings settings,
            float hullRadius,
            List<Anchor> anchors,
            BuildingSpatialIndex geometry)
        {
            int sectorMask = 0;
            float fastestHit = float.PositiveInfinity;
            for (int index = 0; index < anchors.Count; index++)
            {
                Vector3 source = anchors[index].position;
                float distance = Vector3.Distance(source, target);
                if (distance < 18f || distance > EnemyWeaponRange ||
                    !geometry.CorridorClear(source, target,
                        FireCorridorRadius))
                {
                    continue;
                }
                sectorMask |= 1 << HorizontalSector(target, source);
                fastestHit = Mathf.Min(fastestHit,
                    0.90f + distance / ProjectileSpeed);
            }
            AddReachableFreeFlightDirections(target, settings, hullRadius,
                anchors, geometry, ref sectorMask, ref fastestHit);
            int directions = CountBits(sectorMask);
            if (directions <= 0)
                return 0f;
            float urgency = float.IsPositiveInfinity(fastestHit)
                ? 0f
                : Mathf.Clamp01((8f - fastestHit) / 8f);
            bool crossfire = directions >= 2 &&
                             HasSeparatedDirections(sectorMask);
            // “存在一条射线”只代表潜在接触，不等于高难度。真正抬高
            // 格子难度的是：可建立方向覆盖、当前难度允许的并发方向、
            // 建线速度以及交叉火力。这样单侧可读枪线保持黄绿，多向
            // 开放空间才会进入红色。
            return EvaluateFireThreatMetrics(1f, directions, urgency,
                crossfire, settings.Difficulty.combatPressure);
        }

        static void AddReachableFreeFlightDirections(
            Vector3 target,
            AirCombatCitySettings settings,
            float hullRadius,
            List<Anchor> routeAnchors,
            BuildingSpatialIndex geometry,
            ref int sectorMask,
            ref float fastestHit)
        {
            // 枪位不是420米整球上任意一点，也不能只取路线折点。这里在
            // 突击机/炮艇常用射距带内搜索8个水平来向，并尝试同层、上层、
            // 下层三个高度。候选必须同时满足射击走廊清晰、机体包络可占据，
            // 且能从正式路线锚点经真实机体通道抵达。
            float[] ranges = { StrikerFiringRadius, GunshipFiringRadius };
            float verticalStep = Mathf.Max(34f,
                (settings.mediumAltitude - settings.lowAltitude) * 0.72f);
            float[] heightOffsets = { 0f, verticalStep, -verticalStep };
            float maximumHeight = Mathf.Max(settings.highAltitude,
                settings.maximumAltitude - 12f);
            float outerLimit = settings.mapSize * 0.5f +
                               MaximumFiringApproachDistance;
            for (int sector = 0; sector < 8; sector++)
            {
                float radians = sector * 45f * Mathf.Deg2Rad;
                Vector3 radial = new Vector3(
                    Mathf.Sin(radians), 0f, Mathf.Cos(radians));
                bool established = false;
                for (int rangeIndex = 0;
                     rangeIndex < ranges.Length && !established;
                     rangeIndex++)
                for (int heightIndex = 0;
                     heightIndex < heightOffsets.Length && !established;
                     heightIndex++)
                {
                    Vector3 source = target + radial * ranges[rangeIndex];
                    source.y = Mathf.Clamp(target.y +
                        heightOffsets[heightIndex], 18f, maximumHeight);
                    if (Mathf.Abs(source.x) > outerLimit ||
                        Mathf.Abs(source.z) > outerLimit ||
                        geometry.PointBlocked(source, hullRadius) ||
                        !geometry.CorridorClear(source, target,
                            FireCorridorRadius) ||
                        !CanReachFiringPosition(source, hullRadius,
                            routeAnchors, geometry))
                    {
                        continue;
                    }
                    sectorMask |= 1 << HorizontalSector(target, source);
                    float distance = Vector3.Distance(source, target);
                    fastestHit = Mathf.Min(fastestHit,
                        0.90f + distance / ProjectileSpeed);
                    established = true;
                }
            }
        }

        static bool CanReachFiringPosition(
            Vector3 firingPosition,
            float hullRadius,
            List<Anchor> routeAnchors,
            BuildingSpatialIndex geometry)
        {
            if (routeAnchors == null || routeAnchors.Count == 0)
                return false;
            // 只对距离最近的四个正式路线点做昂贵的走廊校验。路线本身已
            // 按约42米采样，四个候选足以覆盖道路两端和相邻高度，同时避免
            // 贪心器每个候选都扫描数百条完整通道。
            int first = -1;
            int second = -1;
            int third = -1;
            int fourth = -1;
            float firstDistance = float.PositiveInfinity;
            float secondDistance = float.PositiveInfinity;
            float thirdDistance = float.PositiveInfinity;
            float fourthDistance = float.PositiveInfinity;
            float maximumSqr = MaximumFiringApproachDistance *
                               MaximumFiringApproachDistance;
            for (int index = 0; index < routeAnchors.Count; index++)
            {
                float sqr = (routeAnchors[index].position - firingPosition)
                    .sqrMagnitude;
                if (sqr > maximumSqr)
                    continue;
                if (sqr < firstDistance)
                {
                    fourth = third; fourthDistance = thirdDistance;
                    third = second; thirdDistance = secondDistance;
                    second = first; secondDistance = firstDistance;
                    first = index; firstDistance = sqr;
                }
                else if (sqr < secondDistance)
                {
                    fourth = third; fourthDistance = thirdDistance;
                    third = second; thirdDistance = secondDistance;
                    second = index; secondDistance = sqr;
                }
                else if (sqr < thirdDistance)
                {
                    fourth = third; fourthDistance = thirdDistance;
                    third = index; thirdDistance = sqr;
                }
                else if (sqr < fourthDistance)
                {
                    fourth = index; fourthDistance = sqr;
                }
            }
            return ReachableFromAnchor(first, firingPosition, hullRadius,
                       routeAnchors, geometry) ||
                   ReachableFromAnchor(second, firingPosition, hullRadius,
                       routeAnchors, geometry) ||
                   ReachableFromAnchor(third, firingPosition, hullRadius,
                       routeAnchors, geometry) ||
                   ReachableFromAnchor(fourth, firingPosition, hullRadius,
                       routeAnchors, geometry);
        }

        static bool ReachableFromAnchor(
            int anchorIndex,
            Vector3 firingPosition,
            float hullRadius,
            List<Anchor> routeAnchors,
            BuildingSpatialIndex geometry)
        {
            if (anchorIndex < 0 || anchorIndex >= routeAnchors.Count)
                return false;
            Vector3 routePosition = routeAnchors[anchorIndex].position;
            return !geometry.PointBlocked(routePosition, hullRadius) &&
                   geometry.CorridorClear(routePosition, firingPosition,
                       hullRadius);
        }

        static bool TryResolveTraversalSeconds(
            Vector3 source,
            Vector3 target,
            AirCombatCitySettings settings,
            float hullRadius,
            BuildingSpatialIndex geometry,
            AirCombatCityRuntimeGeometrySnapshot snapshot,
            out float travelSeconds)
        {
            travelSeconds = float.PositiveInfinity;
            float[] altitudes =
            {
                source.y,
                Mathf.Max(18f, settings.lowAltitude * 0.55f),
                settings.lowAltitude,
                settings.mediumAltitude,
                settings.highAltitude
            };
            for (int index = 0; index < altitudes.Length; index++)
            {
                Vector3 from = source;
                Vector3 to = target;
                from.y = altitudes[index];
                to.y = altitudes[index];
                if (geometry.PointBlocked(from, hullRadius) ||
                    geometry.PointBlocked(to, hullRadius) ||
                    !geometry.CorridorClear(from, to, hullRadius))
                {
                    continue;
                }
                float speed = Mathf.Max(10f, settings.combatSpeed);
                float candidate = Vector3.Distance(from, to) / speed +
                                  settings.turnRadius / speed * 0.20f;
                candidate = ApplyCableTraversalDelay(from, to,
                    hullRadius, candidate, snapshot?.aerialCables);
                candidate = ApplyWindTraversalModifier(from, to,
                    candidate, snapshot?.winds);
                travelSeconds = Mathf.Min(travelSeconds, candidate);
            }
            return !float.IsInfinity(travelSeconds) &&
                   !float.IsNaN(travelSeconds);
        }

        public static float ApplyCableTraversalDelay(
            Vector3 start,
            Vector3 end,
            float hullRadius,
            float baseSeconds,
            AirCombatRuntimeConnectionGeometry[] cables)
        {
            if (cables == null || cables.Length == 0)
                return Mathf.Max(0.01f, baseSeconds);
            int contacts = 0;
            float delay = 0f;
            for (int index = 0; index < cables.Length; index++)
            {
                AirCombatRuntimeConnectionGeometry cable = cables[index];
                float triggerRadius = cable.physicalRadius > 0f
                    ? cable.physicalRadius
                    : 1.05f;
                float combined = hullRadius + triggerRadius;
                bool intersects = false;
                int pointCount = ConnectionPointCount(cable);
                for (int segment = 1; segment < pointCount; segment++)
                {
                    if (SqrDistanceSegments(start, end,
                            ConnectionPointAt(cable, segment - 1),
                            ConnectionPointAt(cable, segment)) >
                        combined * combined)
                        continue;
                    intersects = true;
                    break;
                }
                if (!intersects)
                    continue;
                contacts++;
                float slowdown = cable.playerSlowdown > 0f
                    ? cable.playerSlowdown
                    : 0.28f;
                delay += Mathf.Lerp(0.16f, 0.62f,
                    Mathf.Clamp01(slowdown));
                if (contacts >= 3)
                    break;
            }
            return Mathf.Max(0.01f, baseSeconds + delay);
        }

        public static float ApplyWindTraversalModifier(
            Vector3 start,
            Vector3 end,
            float baseSeconds,
            AirCombatRuntimeWindGeometry[] winds)
        {
            if (winds == null || winds.Length == 0)
                return Mathf.Max(0.01f, baseSeconds);
            Vector3 movement = end - start;
            if (movement.sqrMagnitude <= 0.001f)
                return Mathf.Max(0.01f, baseSeconds);
            movement.Normalize();
            float modifier = 1f;
            for (int windIndex = 0; windIndex < winds.Length; windIndex++)
            {
                AirCombatRuntimeWindGeometry wind = winds[windIndex];
                if (!TryResolveWindSegmentCoverage(start, end, wind,
                        out float coverage))
                    continue;
                Vector3 windDirection = wind.localDirection.sqrMagnitude >
                                        0.001f
                    ? wind.localDirection.normalized
                    : Vector3.forward;
                float alignment = Vector3.Dot(movement, windDirection);
                float headwind = Mathf.Max(0f, -alignment);
                float tailwind = Mathf.Max(0f, alignment);
                float crosswind = 1f - Mathf.Abs(alignment);
                // 风场并非永久激活。约三分之一的有效占空比用于静态
                // PCG 期望值，避免把偶发阵风当成全程强制移动。
                const float expectedDuty = 0.34f;
                float strength = Mathf.Clamp(wind.strength, 0f, 1.5f);
                float local = expectedDuty * coverage * strength *
                              (headwind * 0.32f + crosswind * 0.10f -
                               tailwind * 0.16f);
                modifier *= Mathf.Clamp(1f + local, 0.72f, 1.55f);
            }
            return Mathf.Max(0.01f, baseSeconds *
                Mathf.Clamp(modifier, 0.65f, 1.75f));
        }

        static bool TryResolveWindSegmentCoverage(
            Vector3 start,
            Vector3 end,
            AirCombatRuntimeWindGeometry wind,
            out float coverage)
        {
            // 覆盖率是“这条有向边有多少长度真正处于风场内”，不是风场
            // 占了多少格子。用线段与定向盒的精确裁剪，避免固定采样点漏掉
            // 窄风道；A→B 与 B→A 共用覆盖率，但方向点积符号相反。
            Vector3 horizontalForward = Vector3.ProjectOnPlane(
                wind.localDirection, Vector3.up);
            Vector3 forward = horizontalForward.sqrMagnitude > 0.001f
                ? horizontalForward.normalized
                : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 startDelta = start - wind.localCenter;
            Vector3 endDelta = end - wind.localCenter;
            Vector3 localStart = new Vector3(
                Vector3.Dot(startDelta, right),
                startDelta.y,
                Vector3.Dot(startDelta, forward));
            Vector3 localEnd = new Vector3(
                Vector3.Dot(endDelta, right),
                endDelta.y,
                Vector3.Dot(endDelta, forward));
            Vector3 half = new Vector3(
                Mathf.Abs(wind.size.x),
                Mathf.Abs(wind.size.y),
                Mathf.Abs(wind.size.z)) * 0.5f;
            float enter = 0f;
            float exit = 1f;
            if (!ClipSegmentAxis(localStart.x, localEnd.x, half.x,
                    ref enter, ref exit) ||
                !ClipSegmentAxis(localStart.y, localEnd.y, half.y,
                    ref enter, ref exit) ||
                !ClipSegmentAxis(localStart.z, localEnd.z, half.z,
                    ref enter, ref exit))
            {
                coverage = 0f;
                return false;
            }
            coverage = Mathf.Clamp01(exit - enter);
            return coverage > 0.0001f;
        }

        static bool ClipSegmentAxis(
            float start,
            float end,
            float halfExtent,
            ref float enter,
            ref float exit)
        {
            float delta = end - start;
            if (Mathf.Abs(delta) <= 0.00001f)
                return Mathf.Abs(start) <= halfExtent;
            float first = (-halfExtent - start) / delta;
            float second = (halfExtent - start) / delta;
            if (first > second)
            {
                float temporary = first;
                first = second;
                second = temporary;
            }
            enter = Mathf.Max(enter, first);
            exit = Mathf.Min(exit, second);
            return enter <= exit;
        }

        static int ConnectionPointCount(
            AirCombatRuntimeConnectionGeometry connection)
        {
            return connection.localPoints != null &&
                   connection.localPoints.Length >= 2
                ? connection.localPoints.Length
                : 2;
        }

        static Vector3 ConnectionPointAt(
            AirCombatRuntimeConnectionGeometry connection,
            int index)
        {
            if (connection.localPoints != null &&
                connection.localPoints.Length >= 2)
                return connection.localPoints[Mathf.Clamp(index, 0,
                    connection.localPoints.Length - 1)];
            return index <= 0 ? connection.localStart : connection.localEnd;
        }

        static float SqrDistancePointSegment(
            Vector3 point,
            Vector3 start,
            Vector3 end)
        {
            Vector3 segment = end - start;
            float denominator = segment.sqrMagnitude;
            if (denominator <= 0.000001f)
                return (point - start).sqrMagnitude;
            float t = Mathf.Clamp01(Vector3.Dot(point - start, segment) /
                                    denominator);
            return (point - (start + segment * t)).sqrMagnitude;
        }

        static float SqrDistanceSegments(
            Vector3 firstStart,
            Vector3 firstEnd,
            Vector3 secondStart,
            Vector3 secondEnd)
        {
            // 两条有限线段的稳健最近点解；退化线段会自然落到端点。
            Vector3 u = firstEnd - firstStart;
            Vector3 v = secondEnd - secondStart;
            Vector3 w = firstStart - secondStart;
            float a = Vector3.Dot(u, u);
            float b = Vector3.Dot(u, v);
            float c = Vector3.Dot(v, v);
            float d = Vector3.Dot(u, w);
            float e = Vector3.Dot(v, w);
            float denominator = a * c - b * b;
            float sNumerator = 0f;
            float sDenominator = denominator;
            float tNumerator = 0f;
            float tDenominator = denominator;
            if (denominator < 0.000001f)
            {
                sNumerator = 0f;
                sDenominator = 1f;
                tNumerator = e;
                tDenominator = c;
            }
            else
            {
                sNumerator = b * e - c * d;
                tNumerator = a * e - b * d;
                if (sNumerator < 0f)
                {
                    sNumerator = 0f;
                    tNumerator = e;
                    tDenominator = c;
                }
                else if (sNumerator > sDenominator)
                {
                    sNumerator = sDenominator;
                    tNumerator = e + b;
                    tDenominator = c;
                }
            }
            if (tNumerator < 0f)
            {
                tNumerator = 0f;
                if (-d < 0f)
                    sNumerator = 0f;
                else if (-d > a)
                    sNumerator = sDenominator;
                else
                {
                    sNumerator = -d;
                    sDenominator = a;
                }
            }
            else if (tNumerator > tDenominator)
            {
                tNumerator = tDenominator;
                if (-d + b < 0f)
                    sNumerator = 0f;
                else if (-d + b > a)
                    sNumerator = sDenominator;
                else
                {
                    sNumerator = -d + b;
                    sDenominator = a;
                }
            }
            float sc = Mathf.Abs(sNumerator) < 0.000001f
                ? 0f
                : sNumerator / Mathf.Max(0.000001f, sDenominator);
            float tc = Mathf.Abs(tNumerator) < 0.000001f
                ? 0f
                : tNumerator / Mathf.Max(0.000001f, tDenominator);
            Vector3 delta = w + sc * u - tc * v;
            return delta.sqrMagnitude;
        }

        public static float EvaluateFireThreatMetrics(
            float threatenedVolumeRatio,
            int directionCount,
            float urgency,
            bool crossfire,
            float combatPressure)
        {
            threatenedVolumeRatio = Mathf.Clamp01(
                threatenedVolumeRatio);
            directionCount = Mathf.Max(0, directionCount);
            urgency = Mathf.Clamp01(urgency);
            combatPressure = Mathf.Clamp01(combatPressure);
            if (threatenedVolumeRatio <= 0f || directionCount <= 0)
                return 0f;
            int effectiveDirections = Mathf.Min(directionCount, 3);
            // 多方向同时成立的危险必须由本档实际远程并发能力放大。
            // 低档只有几何视线时不能天然获得半档并发，否则再密的
            // 掩体城市也无法落入低目标；高档则会把开放空间推近上限。
            float concurrencyWeight = combatPressure;
            float threatenedSampleRisk =
                0.02f +
                Mathf.Clamp01(directionCount / 8f) * 0.16f +
                Mathf.Clamp01(effectiveDirections / 3f) * 0.24f *
                concurrencyWeight +
                urgency * 0.12f +
                (crossfire && effectiveDirections >= 2
                    ? 0.36f * combatPressure
                    : 0f);
            return Mathf.Clamp01(threatenedVolumeRatio *
                                 threatenedSampleRisk);
        }

        static bool CorridorClear(
            Vector3 start,
            Vector3 end,
            float radius,
            List<Bounds> buildings)
        {
            Vector3 direction = end - start;
            float distance = direction.magnitude;
            if (distance <= 0.001f)
                return !PointBlocked(start, radius, buildings);
            Ray ray = new Ray(start, direction / distance);
            for (int index = 0; index < buildings.Count; index++)
            {
                Bounds expanded = buildings[index];
                expanded.Expand(radius * 2f);
                if (expanded.Contains(start) || expanded.Contains(end))
                    return false;
                if (expanded.IntersectRay(ray, out float hit) &&
                    hit <= distance)
                {
                    return false;
                }
            }
            return true;
        }

        static bool PointBlocked(
            Vector3 point,
            float radius,
            List<Bounds> buildings)
        {
            for (int index = 0; index < buildings.Count; index++)
            {
                Bounds expanded = buildings[index];
                expanded.Expand(radius * 2f);
                if (expanded.Contains(point))
                    return true;
            }
            return false;
        }

        public static float ResolveBlockTarget(
            float average,
            CombatCityBlockRole role)
        {
            float bias;
            switch (role)
            {
                case CombatCityBlockRole.Recovery:
                    bias = -0.18f;
                    break;
                case CombatCityBlockRole.Occlusion:
                    bias = -0.08f;
                    break;
                case CombatCityBlockRole.Maneuver:
                case CombatCityBlockRole.Vertical:
                    bias = -0.04f;
                    break;
                case CombatCityBlockRole.Exposure:
                    bias = 0.16f;
                    break;
                case CombatCityBlockRole.Attack:
                case CombatCityBlockRole.TacticalChoke:
                    bias = 0.12f;
                    break;
                case CombatCityBlockRole.Destruction:
                case CombatCityBlockRole.Kite:
                    bias = 0.07f;
                    break;
                case CombatCityBlockRole.CombatBoundary:
                    bias = 0.04f;
                    break;
                default:
                    bias = 0f;
                    break;
            }
            return Mathf.Clamp(average + bias, 0.06f, 0.90f);
        }

        static int HorizontalSector(Vector3 origin, Vector3 source)
        {
            Vector3 direction = source - origin;
            float angle = Mathf.Repeat(
                Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg,
                360f);
            return Mathf.Clamp(Mathf.FloorToInt(angle / 45f), 0, 7);
        }

        static bool HasSeparatedDirections(int mask)
        {
            for (int first = 0; first < 8; first++)
            {
                if ((mask & (1 << first)) == 0)
                    continue;
                for (int second = first + 1; second < 8; second++)
                {
                    if ((mask & (1 << second)) == 0)
                        continue;
                    int separation = Mathf.Abs(second - first);
                    separation = Mathf.Min(separation, 8 - separation);
                    if (separation >= 2)
                        return true;
                }
            }
            return false;
        }

        static int CountBits(int value)
        {
            int count = 0;
            while (value != 0)
            {
                value &= value - 1;
                count++;
            }
            return count;
        }

        static int CoordinateKey(int x, int z)
        {
            return (x + 32) * 128 + z + 32;
        }
    }
}
