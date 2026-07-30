using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityPlanet.CombatMap
{
    /// <summary>
    /// Evidence-driven validation for the semantic V2 arena. The checks use
    /// the same analytic terrain field as generation; no Physics query can
    /// make editor state affect a candidate's result.
    /// </summary>
    internal static class CombatMapValidatorV2
    {
        static readonly float[] LosBands =
        {
            30f,
            60f,
            120f,
            200f
        };

        sealed class Graph
        {
            public readonly Dictionary<string, HashSet<string>> adjacency =
                new Dictionary<string, HashSet<string>>(
                    StringComparer.Ordinal);
            public int uniqueEdges;
        }

        sealed class Rhythm
        {
            public bool hasContact;
            public bool hasOcclusion;
            public int transitions;
            public float maximumExposure;
            public float meanOcclusion;
        }

        public static CombatMapValidationReport Validate(
            AirCombatMapSettings settings,
            CombatSemanticPlan plan)
        {
            var violations = new List<CombatMapViolation>();
            var report = new CombatMapValidationReport
            {
                minimumCommitScore = settings.minimumCommitScore,
                lineOfSightBands = (float[])LosBands.Clone()
            };
            if (plan == null)
            {
                violations.Add(Hard(
                    "plan.missing",
                    "缺少战斗语义方案。",
                    Vector3.zero));
                report.violations = violations.ToArray();
                return report;
            }

            ValidateStableIds(plan, violations);
            CombatSemanticAnchor player = plan.FindAnchor(
                CombatAnchorType.PlayerSpawn);
            CombatSemanticAnchor enemy = plan.FindAnchor(
                CombatAnchorType.EnemySpawn);
            CombatSemanticAnchor conflict = plan.FindAnchor(
                CombatAnchorType.CentralConflict);
            ValidateAnchor(
                player,
                "anchor.player-spawn",
                plan.mapCenter,
                violations);
            ValidateAnchor(
                enemy,
                "anchor.enemy-spawn",
                plan.mapCenter,
                violations);
            ValidateAnchor(
                conflict,
                "anchor.central-conflict",
                plan.mapCenter,
                violations);

            bool spawnClear = ValidateSpawn(
                player,
                plan,
                settings,
                violations)
                & ValidateSpawn(
                    enemy,
                    plan,
                    settings,
                    violations);
            bool openingBlocked = false;
            if (player != null && enemy != null)
            {
                openingBlocked =
                    !CombatMapGenerator.HasTerrainLineOfSight(
                        settings,
                        plan,
                        player.position,
                        enemy.position,
                        3f,
                        Mathf.CeilToInt(
                            Vector3.Distance(
                                player.position,
                                enemy.position)
                            / Mathf.Max(
                                4f,
                                settings.mapSize
                                / Mathf.Max(
                                    8f,
                                    settings.chunkResolution * 4f))));
                if (!openingBlocked)
                {
                    violations.Add(Hard(
                        "spawn.opening-line-of-sight",
                        "两个出生区之间存在无遮挡的开局射线。",
                        Vector3.Lerp(
                            player.position,
                            enemy.position,
                            0.5f)));
                }
            }

            float recommendedDiameter = Mathf.Max(
                settings.designWeaponRange * 2.5f,
                settings.designTurnRadius * 8f);
            float scaleRatio = settings.mapSize
                / Mathf.Max(1f, recommendedDiameter);
            report.firstContactSeconds = settings.spawnDistance
                / Mathf.Max(1f, settings.designCombatSpeed * 2f);
            if (scaleRatio < 0.9f)
            {
                violations.Add(Hard(
                    "scale.weapon-turn-envelope",
                    "地图尺度不足以同时容纳设计射程和持续转弯包线。",
                    plan.mapCenter));
            }
            if (report.firstContactSeconds
                < Mathf.Max(
                    6f,
                    settings.targetFirstContactSeconds * 0.55f))
            {
                violations.Add(Hard(
                    "spawn.contact-too-early",
                    "按设计速度计算，双方会过早进入首次接触。",
                    plan.mapCenter));
            }
            else if (report.firstContactSeconds > 25f)
            {
                violations.Add(Hard(
                    "spawn.contact-too-late",
                    "按设计速度计算，双方首次接触等待过长。",
                    plan.mapCenter));
            }

            float requiredPassage = settings.designTurnRadius * 1.15f
                + settings.vehicleWingspan;
            float minimumTurnRadius = float.PositiveInfinity;
            float worstHalfImbalance = 0f;
            bool routeClear = true;
            int primaryRoutes = 0;
            var primary = new List<CombatSemanticRoute>(3);
            if (plan.routes != null)
            {
                for (int i = 0; i < plan.routes.Length; i++)
                {
                    CombatSemanticRoute route = plan.routes[i];
                    if (route == null)
                        continue;
                    bool valid = ValidateRoute(
                        route,
                        settings,
                        plan,
                        requiredPassage,
                        violations,
                        out float routeRadius);
                    routeClear &= valid;
                    if (route.type != CombatRouteType.Retreat)
                    {
                        primaryRoutes++;
                        primary.Add(route);
                        minimumTurnRadius = Mathf.Min(
                            minimumTurnRadius,
                            routeRadius);
                        worstHalfImbalance = Mathf.Max(
                            worstHalfImbalance,
                            ComputeHalfImbalance(route));
                    }
                }
            }
            if (primaryRoutes < 3
                || plan.FindRoute(CombatRouteType.Main) == null
                || plan.FindRoute(
                    CombatRouteType.TerrainMaskedFlank) == null
                || plan.FindRoute(CombatRouteType.LongRange) == null)
            {
                violations.Add(Hard(
                    "routes.missing-archetype",
                    "缺少主路线、地形遮蔽路线或远程路线。",
                    plan.mapCenter));
            }
            report.minimumTurnRadius =
                float.IsPositiveInfinity(minimumTurnRadius)
                    ? 0f
                    : minimumTurnRadius;
            if (report.minimumTurnRadius
                < settings.designTurnRadius * 0.78f)
            {
                violations.Add(Hard(
                    "route.turn-radius",
                    "至少一条主要航路的局部曲率小于设计转弯半径。",
                    plan.mapCenter));
            }

            float minimumBowl = float.PositiveInfinity;
            int maneuverBowlCount = 0;
            if (plan.tacticalVolumes != null)
            {
                for (int i = 0;
                     i < plan.tacticalVolumes.Length;
                     i++)
                {
                    CombatTacticalVolume volume =
                        plan.tacticalVolumes[i];
                    if (volume == null
                        || volume.type
                        != CombatTacticalVolumeType.ManeuverBowl)
                    {
                        continue;
                    }
                    maneuverBowlCount++;
                    minimumBowl = Mathf.Min(
                        minimumBowl,
                        volume.HorizontalDiameter);
                    if (volume.HorizontalDiameter
                        < settings.designTurnRadius * 2.5f)
                    {
                        violations.Add(Hard(
                            "volume.bowl-too-small",
                            "交战盆地无法容纳一个完整的持续转弯圆。",
                            volume.position));
                    }
                }
            }
            report.minimumManeuverDiameter =
                float.IsPositiveInfinity(minimumBowl)
                    ? 0f
                    : minimumBowl;
            if (maneuverBowlCount < 3)
            {
                violations.Add(Hard(
                    "volume.insufficient-bowls",
                    "至少需要三个错位的可回旋交战体积。",
                    plan.mapCenter));
            }

            Graph graph = BuildGraph(plan, violations);
            int connectedComponents = CountComponents(graph);
            int cycleRank = graph.uniqueEdges
                - graph.adjacency.Count
                + connectedComponents;
            report.minimumExitCount = MinimumNonSpawnDegree(
                graph,
                plan);
            report.articulationPointCount =
                CountCriticalArticulations(
                    graph,
                    "volume.spawn.player",
                    "volume.spawn.enemy");
            int disjointPrimary = CountInternallyDisjointPrimaryRoutes(
                primary);
            if (connectedComponents != 1)
            {
                violations.Add(Hard(
                    "topology.disconnected",
                    "战术体积图存在无法进入的孤立部分。",
                    plan.mapCenter));
            }
            if (report.minimumExitCount < 2)
            {
                violations.Add(Hard(
                    "topology.dead-end",
                    "存在只有一个出口的非出生战术体积。",
                    plan.mapCenter));
            }
            if (cycleRank < 2)
            {
                violations.Add(Hard(
                    "topology.insufficient-cycles",
                    "战术图缺少至少两条恢复或重新接敌回路。",
                    plan.mapCenter));
            }
            if (report.articulationPointCount > 0)
            {
                violations.Add(Hard(
                    "topology.single-choke",
                    "移除一个战术体积就会切断双方，形成单一必经点。",
                    plan.mapCenter));
            }
            if (disjointPrimary < 2)
            {
                violations.Add(Hard(
                    "topology.no-disjoint-choice",
                    "双方出生区之间缺少两条内部节点不相交路线。",
                    plan.mapCenter));
            }

            ComputeRouteChoice(
                primary,
                settings,
                out float entropy,
                out float dominance);
            report.routeUsageEntropy = entropy;
            report.maximumRouteDominance = dominance;
            if (dominance > 0.82f)
            {
                violations.Add(Hard(
                    "routes.dominant-choice",
                    "一条路线在时间与暴露代价上过度支配其他路线。",
                    plan.mapCenter));
            }

            var rhythms = new List<Rhythm>();
            for (int i = 0; i < primary.Count; i++)
            {
                rhythms.Add(AnalyzeRhythm(
                    settings,
                    plan,
                    primary[i]));
            }
            SummarizeRhythm(
                rhythms,
                out float meanOcclusion,
                out float maximumExposure,
                out int rhythmRoutes);
            report.meanOcclusionSeconds = meanOcclusion;
            report.maximumExposureSeconds = maximumExposure;
            if (rhythmRoutes < 2)
            {
                violations.Add(Hard(
                    "los.no-reengagement-rhythm",
                    "主要路线没有形成足够的“遮断—重新暴露”节奏。",
                    plan.mapCenter));
            }
            if (maximumExposure
                > settings.targetExposureSeconds * 2.25f)
            {
                violations.Add(Hard(
                    "los.continuous-exposure",
                    "存在持续时间过长的无遮挡交战窗口。",
                    plan.mapCenter));
            }

            report.lineOfSightOpenFractions =
                ComputeLosLayerFractions(settings, plan);
            report.globalEyePointCount = CountGlobalEyes(
                settings,
                plan);
            int grid = settings.validationGridResolution;
            if (report.globalEyePointCount
                > Mathf.Max(2, Mathf.RoundToInt(grid * grid * 0.14f)))
            {
                violations.Add(Hard(
                    "los.global-eye",
                    "高空存在过多能够观察大部分战术体积的全图眼位。",
                    plan.mapCenter));
            }

            ValidateTerrain(settings, plan, violations);
            float estimatedChunkSide = Mathf.Ceil(
                settings.mapSize / settings.chunkSize);
            float estimatedChunks =
                estimatedChunkSide * estimatedChunkSide;
            if (estimatedChunks > 100f)
            {
                violations.Add(Warning(
                    "performance.chunk-count",
                    "区块数量较高，MeshCollider 构建成本可能过大。",
                    plan.mapCenter));
            }
            if (settings.occluderTowerCount > 12)
            {
                violations.Add(Warning(
                    "readability.tower-budget",
                    "塔数量超过语义遮挡预算；V2 只使用前 12 座组成两个塔簇。",
                    plan.mapCenter));
            }

            float scaleQuality = ScoreNearOne(scaleRatio, 0.9f, 1.7f);
            float contactQuality = ScoreTarget(
                report.firstContactSeconds,
                settings.targetFirstContactSeconds,
                Mathf.Max(4f, settings.targetFirstContactSeconds * 0.6f));
            report.scaleCompatibilityScore =
                100f * (scaleQuality * 0.6f + contactQuality * 0.4f);
            float radiusQuality = Mathf.Clamp01(
                report.minimumTurnRadius
                / Mathf.Max(1f, settings.designTurnRadius * 1.1f));
            float bowlQuality = Mathf.Clamp01(
                report.minimumManeuverDiameter
                / Mathf.Max(1f, settings.designTurnRadius * 2.8f));
            report.kinematicScore = 100f
                * (radiusQuality * 0.45f
                + bowlQuality * 0.35f
                + (routeClear ? 0.2f : 0f));
            report.topologyScore = 100f * Mathf.Clamp01(
                0.25f * Mathf.Clamp01(disjointPrimary / 3f)
                + 0.2f * Mathf.Clamp01(cycleRank / 4f)
                + 0.2f * Mathf.Clamp01(report.minimumExitCount / 3f)
                + 0.2f * entropy
                + 0.15f
                    * (report.articulationPointCount == 0 ? 1f : 0f));
            float occlusionQuality = ScoreTarget(
                meanOcclusion,
                settings.targetOcclusionSeconds,
                Mathf.Max(2f, settings.targetOcclusionSeconds));
            float exposureQuality = maximumExposure <= 0f
                ? 0f
                : ScoreTarget(
                    maximumExposure,
                    settings.targetExposureSeconds,
                    Mathf.Max(3f, settings.targetExposureSeconds));
            float layerDiversity = LosLayerDiversity(
                report.lineOfSightOpenFractions);
            report.coverRhythmScore = 100f
                * (0.35f * occlusionQuality
                + 0.3f * exposureQuality
                + 0.2f * layerDiversity
                + 0.15f * Mathf.Clamp01(rhythmRoutes / 3f));

            float fairness = 1f - Mathf.Clamp01(
                worstHalfImbalance
                / Mathf.Max(
                    0.01f,
                    settings.maximumRouteTimeImbalance * 1.5f));
            report.teamBalanceScore = fairness * 10f;
            report.routeDiversityScore =
                report.topologyScore * 0.2f;
            report.sightlineAndCoverScore =
                report.coverRhythmScore * 0.2f;
            report.objectivePressureScore =
                Mathf.Clamp01(maneuverBowlCount / 3f) * 10f;
            report.mobilityCompatibilityScore =
                report.kinematicScore * 0.2f;
            report.spawnSafetyScore =
                spawnClear && openingBlocked ? 10f : 0f;
            report.readabilityScore =
                plan.terrainStamps != null
                && plan.terrainStamps.Length >= 12
                && plan.topologyVariant >= 0
                    ? 5f
                    : 2f;
            report.performanceScore = Mathf.Lerp(
                5f,
                2f,
                Mathf.InverseLerp(36f, 100f, estimatedChunks));
            report.score = Mathf.Clamp(
                report.teamBalanceScore
                + report.routeDiversityScore
                + report.sightlineAndCoverScore
                + report.objectivePressureScore
                + report.mobilityCompatibilityScore
                + report.spawnSafetyScore
                + report.readabilityScore
                + report.performanceScore,
                0f,
                100f);

            if (report.kinematicScore < 60f)
            {
                violations.Add(Hard(
                    "quality.kinematic-floor",
                    "机动可行性分项低于应用下限。",
                    plan.mapCenter));
            }
            if (report.topologyScore < 60f)
            {
                violations.Add(Hard(
                    "quality.topology-floor",
                    "拓扑韧性分项低于应用下限。",
                    plan.mapCenter));
            }
            if (report.coverRhythmScore < 48f)
            {
                violations.Add(Hard(
                    "quality.cover-rhythm-floor",
                    "遮挡与重新接敌节奏低于应用下限。",
                    plan.mapCenter));
            }

            report.violations = violations.ToArray();
            report.checksum = plan.checksum;
            return report;
        }

        static void ValidateStableIds(
            CombatSemanticPlan plan,
            List<CombatMapViolation> violations)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            AddIds(plan.anchors, value => value?.stableId, ids, plan, violations);
            AddIds(
                plan.tacticalVolumes,
                value => value?.stableId,
                ids,
                plan,
                violations);
            AddIds(plan.routes, value => value?.stableId, ids, plan, violations);
            AddIds(
                plan.terrainStamps,
                value => value?.stableId,
                ids,
                plan,
                violations);
            AddIds(
                plan.occluders,
                value => value?.stableId,
                ids,
                plan,
                violations);
        }

        static void AddIds<T>(
            T[] values,
            Func<T, string> selector,
            HashSet<string> ids,
            CombatSemanticPlan plan,
            List<CombatMapViolation> violations)
        {
            if (values == null)
                return;
            for (int i = 0; i < values.Length; i++)
            {
                string id = selector(values[i]);
                if (string.IsNullOrWhiteSpace(id))
                {
                    violations.Add(Hard(
                        "stable-id.empty",
                        "语义数据包含空的稳定 ID。",
                        plan.mapCenter));
                }
                else if (!ids.Add(id))
                {
                    violations.Add(Hard(
                        "stable-id.duplicate",
                        "语义数据包含重复的稳定 ID：" + id,
                        plan.mapCenter));
                }
            }
        }

        static void ValidateAnchor(
            CombatSemanticAnchor anchor,
            string code,
            Vector3 position,
            List<CombatMapViolation> violations)
        {
            if (anchor == null)
            {
                violations.Add(Hard(
                    code,
                    "缺少必需的战斗锚点。",
                    position));
                return;
            }
            if (!Finite(anchor.position)
                || !Finite(anchor.forward)
                || anchor.radius <= 0f)
            {
                violations.Add(Hard(
                    code + ".invalid",
                    "战斗锚点包含无效位置、方向或半径。",
                    anchor.position));
            }
        }

        static bool ValidateSpawn(
            CombatSemanticAnchor spawn,
            CombatSemanticPlan plan,
            AirCombatMapSettings settings,
            List<CombatMapViolation> violations)
        {
            if (spawn == null)
                return false;
            Vector2 offset = new Vector2(
                spawn.position.x - plan.mapCenter.x,
                spawn.position.z - plan.mapCenter.z);
            if (offset.magnitude
                + settings.designTurnRadius
                + settings.vehicleWingspan
                >= settings.warningRadius)
            {
                violations.Add(Hard(
                    "spawn.turn-margin",
                    "出生盆地没有保留完成转弯所需的边界余量。",
                    spawn.position));
            }
            float ground = CombatMapGenerator.SampleHeight(
                settings,
                plan,
                spawn.position.x,
                spawn.position.z);
            float clearance = spawn.position.y - ground;
            bool valid = Finite(clearance)
                && clearance >= settings.minimumGroundClearance
                && clearance <= settings.maximumGroundClearance;
            if (!valid)
            {
                violations.Add(Hard(
                    "spawn.invalid-clearance",
                    "出生点离地高度超出设计包线。",
                    spawn.position));
            }
            return valid;
        }

        static bool ValidateRoute(
            CombatSemanticRoute route,
            AirCombatMapSettings settings,
            CombatSemanticPlan plan,
            float requiredPassage,
            List<CombatMapViolation> violations,
            out float minimumRadius)
        {
            minimumRadius = float.PositiveInfinity;
            if (route.waypoints == null
                || route.waypoints.Length < 3)
            {
                violations.Add(Hard(
                    "route.empty",
                    "航路至少需要三个采样点。",
                    plan.mapCenter));
                minimumRadius = 0f;
                return false;
            }
            bool valid = true;
            if (route.width < requiredPassage)
            {
                valid = false;
                violations.Add(Hard(
                    "route.passage-width",
                    "航路宽度小于转弯半径与翼展共同要求的净宽。",
                    route.waypoints[0]));
            }
            for (int i = 0; i < route.waypoints.Length; i++)
            {
                Vector3 point = route.waypoints[i];
                if (!Finite(point))
                {
                    valid = false;
                    violations.Add(Hard(
                        "route.invalid-waypoint",
                        "航路包含非有限坐标。",
                        point));
                    continue;
                }
                float ground = CombatMapGenerator.SampleHeight(
                    settings,
                    plan,
                    point.x,
                    point.z);
                float clearance = point.y - ground;
                if (clearance
                        < settings.minimumGroundClearance
                            + settings.vehicleWingspan * 0.2f
                    || clearance
                        > settings.maximumGroundClearance)
                {
                    valid = false;
                    violations.Add(Hard(
                        "route.clearance",
                        "航路采样点的实体净空超出飞行包线。",
                        point));
                    break;
                }
            }
            for (int i = 1; i < route.waypoints.Length - 1; i++)
            {
                float radius = CircumradiusXZ(
                    route.waypoints[i - 1],
                    route.waypoints[i],
                    route.waypoints[i + 1]);
                if (!float.IsPositiveInfinity(radius))
                    minimumRadius = Mathf.Min(minimumRadius, radius);
            }
            if (float.IsPositiveInfinity(minimumRadius))
                minimumRadius = 100000f;
            return valid;
        }

        static float CircumradiusXZ(
            Vector3 a3,
            Vector3 b3,
            Vector3 c3)
        {
            var a = new Vector2(a3.x, a3.z);
            var b = new Vector2(b3.x, b3.z);
            var c = new Vector2(c3.x, c3.z);
            float ab = Vector2.Distance(a, b);
            float bc = Vector2.Distance(b, c);
            float ca = Vector2.Distance(c, a);
            float twiceArea = Mathf.Abs(
                (b.x - a.x) * (c.y - a.y)
                - (b.y - a.y) * (c.x - a.x));
            if (twiceArea < 0.001f
                || ab < 0.01f
                || bc < 0.01f
                || ca < 0.01f)
            {
                return float.PositiveInfinity;
            }
            return ab * bc * ca / (2f * twiceArea);
        }

        static float ComputeHalfImbalance(
            CombatSemanticRoute route)
        {
            if (route?.waypoints == null
                || route.waypoints.Length < 3)
            {
                return 1f;
            }
            float total = route.Length;
            float target = total * 0.5f;
            float accumulated = 0f;
            for (int i = 1; i < route.waypoints.Length; i++)
            {
                float segment = Vector3.Distance(
                    route.waypoints[i - 1],
                    route.waypoints[i]);
                if (accumulated + segment >= target)
                {
                    float first = accumulated + segment;
                    float second = total - first;
                    return Mathf.Abs(first - second)
                        / Mathf.Max(1f, total);
                }
                accumulated += segment;
            }
            return 1f;
        }

        static Graph BuildGraph(
            CombatSemanticPlan plan,
            List<CombatMapViolation> violations)
        {
            var graph = new Graph();
            if (plan.tacticalVolumes != null)
            {
                for (int i = 0;
                     i < plan.tacticalVolumes.Length;
                     i++)
                {
                    CombatTacticalVolume volume =
                        plan.tacticalVolumes[i];
                    if (volume != null
                        && !graph.adjacency.ContainsKey(
                            volume.stableId))
                    {
                        graph.adjacency.Add(
                            volume.stableId,
                            new HashSet<string>(
                                StringComparer.Ordinal));
                    }
                }
            }
            var edges = new HashSet<string>(StringComparer.Ordinal);
            if (plan.routes != null)
            {
                for (int routeIndex = 0;
                     routeIndex < plan.routes.Length;
                     routeIndex++)
                {
                    CombatSemanticRoute route =
                        plan.routes[routeIndex];
                    if (route?.controlVolumeIds == null)
                        continue;
                    for (int i = 1;
                         i < route.controlVolumeIds.Length;
                         i++)
                    {
                        string a = route.controlVolumeIds[i - 1];
                        string b = route.controlVolumeIds[i];
                        if (!graph.adjacency.ContainsKey(a)
                            || !graph.adjacency.ContainsKey(b))
                        {
                            violations.Add(Hard(
                                "topology.unknown-volume",
                                "路线引用了不存在的战术体积。",
                                plan.mapCenter));
                            continue;
                        }
                        if (string.Equals(
                            a,
                            b,
                            StringComparison.Ordinal))
                        {
                            continue;
                        }
                        graph.adjacency[a].Add(b);
                        graph.adjacency[b].Add(a);
                        string key = string.CompareOrdinal(a, b) < 0
                            ? a + "\n" + b
                            : b + "\n" + a;
                        if (edges.Add(key))
                            graph.uniqueEdges++;
                    }
                }
            }
            return graph;
        }

        static int CountComponents(Graph graph)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            int components = 0;
            foreach (string node in graph.adjacency.Keys)
            {
                if (visited.Contains(node))
                    continue;
                components++;
                var queue = new Queue<string>();
                queue.Enqueue(node);
                visited.Add(node);
                while (queue.Count > 0)
                {
                    string current = queue.Dequeue();
                    foreach (string next
                             in graph.adjacency[current])
                    {
                        if (visited.Add(next))
                            queue.Enqueue(next);
                    }
                }
            }
            return components;
        }

        static int MinimumNonSpawnDegree(
            Graph graph,
            CombatSemanticPlan plan)
        {
            int minimum = int.MaxValue;
            foreach (KeyValuePair<string, HashSet<string>> pair
                     in graph.adjacency)
            {
                CombatTacticalVolume volume =
                    plan.FindVolume(pair.Key);
                if (volume == null
                    || volume.type
                    == CombatTacticalVolumeType.SpawnBasin)
                {
                    continue;
                }
                minimum = Mathf.Min(minimum, pair.Value.Count);
            }
            return minimum == int.MaxValue ? 0 : minimum;
        }

        static int CountCriticalArticulations(
            Graph graph,
            string start,
            string goal)
        {
            int count = 0;
            foreach (string removed in graph.adjacency.Keys)
            {
                if (string.Equals(removed, start, StringComparison.Ordinal)
                    || string.Equals(
                        removed,
                        goal,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                if (!Reachable(graph, start, goal, removed))
                    count++;
            }
            return count;
        }

        static bool Reachable(
            Graph graph,
            string start,
            string goal,
            string removed)
        {
            if (!graph.adjacency.ContainsKey(start)
                || !graph.adjacency.ContainsKey(goal))
            {
                return false;
            }
            var visited = new HashSet<string>(
                StringComparer.Ordinal)
            {
                start
            };
            var queue = new Queue<string>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                if (string.Equals(
                    current,
                    goal,
                    StringComparison.Ordinal))
                {
                    return true;
                }
                foreach (string next in graph.adjacency[current])
                {
                    if (string.Equals(
                        next,
                        removed,
                        StringComparison.Ordinal))
                    {
                        continue;
                    }
                    if (visited.Add(next))
                        queue.Enqueue(next);
                }
            }
            return false;
        }

        static int CountInternallyDisjointPrimaryRoutes(
            List<CombatSemanticRoute> routes)
        {
            var accepted = new List<HashSet<string>>();
            for (int i = 0; i < routes.Count; i++)
            {
                CombatSemanticRoute route = routes[i];
                if (route?.controlVolumeIds == null
                    || route.controlVolumeIds.Length < 2)
                {
                    continue;
                }
                var internalIds = new HashSet<string>(
                    StringComparer.Ordinal);
                for (int id = 1;
                     id < route.controlVolumeIds.Length - 1;
                     id++)
                {
                    internalIds.Add(route.controlVolumeIds[id]);
                }
                bool disjoint = true;
                for (int set = 0; set < accepted.Count; set++)
                {
                    if (accepted[set].Overlaps(internalIds))
                    {
                        disjoint = false;
                        break;
                    }
                }
                if (disjoint)
                    accepted.Add(internalIds);
            }
            return accepted.Count;
        }

        static void ComputeRouteChoice(
            List<CombatSemanticRoute> routes,
            AirCombatMapSettings settings,
            out float entropy,
            out float dominance)
        {
            if (routes.Count == 0)
            {
                entropy = 0f;
                dominance = 1f;
                return;
            }
            var costs = new float[routes.Count];
            float minimum = float.PositiveInfinity;
            for (int i = 0; i < routes.Count; i++)
            {
                costs[i] = routes[i].Length
                    / Mathf.Max(1f, settings.designCombatSpeed)
                    + routes[i].intendedExposure
                    * settings.targetExposureSeconds * 0.7f;
                minimum = Mathf.Min(minimum, costs[i]);
            }
            float scale = Mathf.Max(
                2f,
                settings.targetExposureSeconds * 0.8f);
            float total = 0f;
            var weights = new float[costs.Length];
            for (int i = 0; i < costs.Length; i++)
            {
                weights[i] = Mathf.Exp(
                    -(costs[i] - minimum) / scale);
                total += weights[i];
            }
            float h = 0f;
            dominance = 0f;
            for (int i = 0; i < weights.Length; i++)
            {
                float probability = weights[i]
                    / Mathf.Max(0.0001f, total);
                dominance = Mathf.Max(dominance, probability);
                if (probability > 0.0001f)
                    h -= probability * Mathf.Log(probability);
            }
            entropy = routes.Count > 1
                ? h / Mathf.Log(routes.Count)
                : 0f;
        }

        static Rhythm AnalyzeRhythm(
            AirCombatMapSettings settings,
            CombatSemanticPlan plan,
            CombatSemanticRoute route)
        {
            var result = new Rhythm();
            if (route?.waypoints == null
                || route.waypoints.Length < 4)
            {
                return result;
            }
            bool? previous = null;
            float runSeconds = 0f;
            float occlusionTotal = 0f;
            int occlusionRuns = 0;
            for (int i = 0; i < route.waypoints.Length; i++)
            {
                int opposite = route.waypoints.Length - 1 - i;
                Vector3 a = route.waypoints[i];
                Vector3 b = route.waypoints[opposite];
                float distance = Vector3.Distance(a, b);
                bool attackable = distance
                    <= settings.designWeaponRange
                    && CombatMapGenerator.HasTerrainLineOfSight(
                        settings,
                        plan,
                        a,
                        b,
                        3f,
                        Mathf.CeilToInt(
                            distance
                            / Mathf.Max(
                                5f,
                                settings.mapSize
                                / Mathf.Max(
                                    8f,
                                    settings.chunkResolution * 4f))));
                float step = i + 1 < route.waypoints.Length
                    ? Vector3.Distance(
                        route.waypoints[i],
                        route.waypoints[i + 1])
                        / Mathf.Max(
                            1f,
                            settings.designCombatSpeed)
                    : 0.5f;
                if (!previous.HasValue)
                {
                    previous = attackable;
                    runSeconds = step;
                    continue;
                }
                if (previous.Value == attackable)
                {
                    runSeconds += step;
                    continue;
                }
                CommitRhythmRun(
                    result,
                    previous.Value,
                    runSeconds,
                    ref occlusionTotal,
                    ref occlusionRuns);
                result.transitions++;
                previous = attackable;
                runSeconds = step;
            }
            if (previous.HasValue)
            {
                CommitRhythmRun(
                    result,
                    previous.Value,
                    runSeconds,
                    ref occlusionTotal,
                    ref occlusionRuns);
            }
            result.meanOcclusion = occlusionRuns > 0
                ? occlusionTotal / occlusionRuns
                : 0f;
            return result;
        }

        static void CommitRhythmRun(
            Rhythm rhythm,
            bool visible,
            float seconds,
            ref float occlusionTotal,
            ref int occlusionRuns)
        {
            if (visible)
            {
                rhythm.hasContact = true;
                rhythm.maximumExposure = Mathf.Max(
                    rhythm.maximumExposure,
                    seconds);
            }
            else if (rhythm.hasContact)
            {
                rhythm.hasOcclusion = true;
                occlusionTotal += seconds;
                occlusionRuns++;
            }
        }

        static void SummarizeRhythm(
            List<Rhythm> rhythms,
            out float meanOcclusion,
            out float maximumExposure,
            out int qualifyingRoutes)
        {
            float occlusion = 0f;
            int occlusionCount = 0;
            maximumExposure = 0f;
            qualifyingRoutes = 0;
            for (int i = 0; i < rhythms.Count; i++)
            {
                Rhythm rhythm = rhythms[i];
                maximumExposure = Mathf.Max(
                    maximumExposure,
                    rhythm.maximumExposure);
                if (rhythm.meanOcclusion > 0f)
                {
                    occlusion += rhythm.meanOcclusion;
                    occlusionCount++;
                }
                if (rhythm.hasContact
                    && rhythm.hasOcclusion
                    && rhythm.transitions >= 2)
                {
                    qualifyingRoutes++;
                }
            }
            meanOcclusion = occlusionCount > 0
                ? occlusion / occlusionCount
                : 0f;
        }

        static float[] ComputeLosLayerFractions(
            AirCombatMapSettings settings,
            CombatSemanticPlan plan)
        {
            var result = new float[LosBands.Length];
            if (plan.tacticalVolumes == null)
                return result;
            for (int band = 0; band < LosBands.Length; band++)
            {
                float height = Mathf.Min(
                    LosBands[band],
                    settings.maximumGroundClearance);
                int visible = 0;
                int total = 0;
                for (int first = 0;
                     first < plan.tacticalVolumes.Length;
                     first++)
                for (int second = first + 1;
                     second < plan.tacticalVolumes.Length;
                     second++)
                {
                    CombatTacticalVolume a =
                        plan.tacticalVolumes[first];
                    CombatTacticalVolume b =
                        plan.tacticalVolumes[second];
                    if (a == null || b == null)
                        continue;
                    Vector3 from = AtAgl(
                        settings,
                        plan,
                        a.position,
                        height);
                    Vector3 to = AtAgl(
                        settings,
                        plan,
                        b.position,
                        height);
                    total++;
                    if (CombatMapGenerator.HasTerrainLineOfSight(
                        settings,
                        plan,
                        from,
                        to,
                        3f,
                        40))
                    {
                        visible++;
                    }
                }
                result[band] = total > 0
                    ? visible / (float)total
                    : 0f;
            }
            return result;
        }

        static int CountGlobalEyes(
            AirCombatMapSettings settings,
            CombatSemanticPlan plan)
        {
            if (plan.tacticalVolumes == null
                || plan.tacticalVolumes.Length == 0)
            {
                return 0;
            }
            int side = settings.validationGridResolution;
            float radius = settings.warningRadius * 0.82f;
            float band = Mathf.Min(
                120f,
                settings.maximumGroundClearance);
            int result = 0;
            for (int z = 0; z < side; z++)
            for (int x = 0; x < side; x++)
            {
                float nx = Mathf.Lerp(-1f, 1f, x / (float)(side - 1));
                float nz = Mathf.Lerp(-1f, 1f, z / (float)(side - 1));
                if (nx * nx + nz * nz > 1f)
                    continue;
                Vector3 sample = new Vector3(
                    plan.mapCenter.x + nx * radius,
                    0f,
                    plan.mapCenter.z + nz * radius);
                sample = AtAgl(settings, plan, sample, band);
                int visible = 0;
                int total = 0;
                for (int i = 0;
                     i < plan.tacticalVolumes.Length;
                     i++)
                {
                    CombatTacticalVolume volume =
                        plan.tacticalVolumes[i];
                    if (volume == null
                        || volume.type
                        == CombatTacticalVolumeType.ExposureLane)
                    {
                        continue;
                    }
                    total++;
                    Vector3 target = AtAgl(
                        settings,
                        plan,
                        volume.position,
                        Mathf.Min(
                            volume.preferredClearance,
                            band));
                    if (CombatMapGenerator.HasTerrainLineOfSight(
                        settings,
                        plan,
                        sample,
                        target,
                        3f,
                        36)
                        && Vector3.Distance(sample, target)
                        <= settings.designWeaponRange * 1.5f)
                    {
                        visible++;
                    }
                }
                if (total > 0
                    && visible / (float)total > 0.7f)
                {
                    result++;
                }
            }
            return result;
        }

        static Vector3 AtAgl(
            AirCombatMapSettings settings,
            CombatSemanticPlan plan,
            Vector3 point,
            float agl)
        {
            float ground = CombatMapGenerator.SampleHeight(
                settings,
                plan,
                point.x,
                point.z);
            return new Vector3(point.x, ground + agl, point.z);
        }

        static void ValidateTerrain(
            AirCombatMapSettings settings,
            CombatSemanticPlan plan,
            List<CombatMapViolation> violations)
        {
            const int side = 21;
            float half = settings.mapSize * 0.5f;
            float step = settings.mapSize / (side - 1);
            for (int z = 0; z < side; z++)
            for (int x = 0; x < side; x++)
            {
                float worldX = plan.mapCenter.x
                    + Mathf.Lerp(-half, half, x / (float)(side - 1));
                float worldZ = plan.mapCenter.z
                    + Mathf.Lerp(-half, half, z / (float)(side - 1));
                float height = CombatMapGenerator.SampleHeight(
                    settings,
                    plan,
                    worldX,
                    worldZ);
                if (!Finite(height))
                {
                    violations.Add(Hard(
                        "terrain.non-finite",
                        "地形采样返回非有限高度。",
                        new Vector3(worldX, 0f, worldZ)));
                    return;
                }
                if (x < side - 1)
                {
                    float right = CombatMapGenerator.SampleHeight(
                        settings,
                        plan,
                        worldX + step,
                        worldZ);
                    float slope = Mathf.Abs(right - height)
                        / Mathf.Max(1f, step);
                    if (slope > 2.4f)
                    {
                        violations.Add(Warning(
                            "terrain.extreme-slope",
                            "局部地形坡度很陡，建议检查灰盒轮廓。",
                            new Vector3(worldX, height, worldZ)));
                        return;
                    }
                }
            }
        }

        static float LosLayerDiversity(float[] fractions)
        {
            if (fractions == null || fractions.Length < 2)
                return 0f;
            float low = fractions[0];
            float high = fractions[fractions.Length - 1];
            float progression = Mathf.Clamp01(
                (high - low + 0.08f) / 0.35f);
            float mixed = 0f;
            for (int i = 0; i < fractions.Length; i++)
            {
                mixed += 1f
                    - Mathf.Clamp01(
                        Mathf.Abs(fractions[i] - 0.5f) * 2f);
            }
            return Mathf.Clamp01(
                progression * 0.55f
                + mixed / fractions.Length * 0.45f);
        }

        static float ScoreNearOne(
            float value,
            float minimum,
            float maximum)
        {
            if (value < minimum)
                return Mathf.Clamp01(value / Mathf.Max(0.001f, minimum));
            if (value <= maximum)
                return 1f;
            return Mathf.Clamp01(
                1f - (value - maximum)
                / Mathf.Max(0.001f, maximum));
        }

        static float ScoreTarget(
            float value,
            float target,
            float tolerance)
        {
            return 1f - Mathf.Clamp01(
                Mathf.Abs(value - target)
                / Mathf.Max(0.001f, tolerance));
        }

        static CombatMapViolation Hard(
            string code,
            string message,
            Vector3 position)
        {
            return new CombatMapViolation(
                code,
                CombatMapViolationSeverity.HardError,
                message,
                position);
        }

        static CombatMapViolation Warning(
            string code,
            string message,
            Vector3 position)
        {
            return new CombatMapViolation(
                code,
                CombatMapViolationSeverity.Warning,
                message,
                position);
        }

        static bool Finite(Vector3 value)
        {
            return Finite(value.x)
                && Finite(value.y)
                && Finite(value.z);
        }

        static bool Finite(float value)
        {
            return !float.IsNaN(value)
                && !float.IsInfinity(value);
        }
    }
}
