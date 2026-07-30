using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityPlanet.CombatMap
{
    /// <summary>
    /// Hard constraints gate commit; soft scores only rank candidates that
    /// already satisfy those constraints.
    /// </summary>
    public static class CombatMapValidator
    {
        public static CombatMapValidationReport Validate(
            AirCombatMapSettings sourceSettings,
            CombatSemanticPlan plan)
        {
            AirCombatMapSettings settings =
                (sourceSettings ?? AirCombatMapSettings.CreateDefault())
                .ValidatedCopy();
            if (plan != null && plan.schemaVersion >= 2)
                return CombatMapValidatorV2.Validate(settings, plan);
            var violations = new List<CombatMapViolation>();
            var report = new CombatMapValidationReport
            {
                minimumCommitScore = settings.minimumCommitScore
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

            CombatSemanticAnchor player = plan.FindAnchor(
                CombatAnchorType.PlayerSpawn);
            CombatSemanticAnchor enemy = plan.FindAnchor(
                CombatAnchorType.EnemySpawn);
            CombatSemanticAnchor conflict = plan.FindAnchor(
                CombatAnchorType.CentralConflict);
            ValidateRequiredAnchor(
                player,
                "anchor.player-spawn",
                plan.mapCenter,
                violations);
            ValidateRequiredAnchor(
                enemy,
                "anchor.enemy-spawn",
                plan.mapCenter,
                violations);
            ValidateRequiredAnchor(
                conflict,
                "anchor.central-conflict",
                plan.mapCenter,
                violations);

            bool spawnBlocked = false;
            bool playerClear = false;
            bool enemyClear = false;
            if (player != null && enemy != null)
            {
                ValidateInsideArena(
                    player,
                    plan,
                    settings,
                    violations);
                ValidateInsideArena(
                    enemy,
                    plan,
                    settings,
                    violations);
                playerClear = ValidateSpawnClearance(
                    player,
                    plan,
                    settings,
                    violations);
                enemyClear = ValidateSpawnClearance(
                    enemy,
                    plan,
                    settings,
                    violations);
                spawnBlocked = !CombatMapGenerator.HasTerrainLineOfSight(
                    settings,
                    plan,
                    player.position,
                    enemy.position);
                if (!spawnBlocked)
                {
                    violations.Add(Hard(
                        "spawn.opening-line-of-sight",
                        "两个出生点之间存在无遮挡的初始视线。",
                        Vector3.Lerp(
                            player.position,
                            enemy.position,
                            0.5f)));
                }
            }

            int primaryRouteCount = 0;
            bool hasMain = false;
            bool hasFlank = false;
            bool hasLongRange = false;
            int retreatCount = 0;
            float worstImbalance = 0f;
            float minimumRouteSeparation = float.PositiveInfinity;
            var primaryRoutes = new List<CombatSemanticRoute>(3);
            if (plan.routes != null)
            {
                for (int i = 0; i < plan.routes.Length; i++)
                {
                    CombatSemanticRoute route = plan.routes[i];
                    if (route == null)
                        continue;
                    ValidateRoute(route, settings, violations);
                    switch (route.type)
                    {
                        case CombatRouteType.Main:
                            hasMain = true;
                            primaryRouteCount++;
                            primaryRoutes.Add(route);
                            break;
                        case CombatRouteType.TerrainMaskedFlank:
                            hasFlank = true;
                            primaryRouteCount++;
                            primaryRoutes.Add(route);
                            break;
                        case CombatRouteType.LongRange:
                            hasLongRange = true;
                            primaryRouteCount++;
                            primaryRoutes.Add(route);
                            break;
                        case CombatRouteType.Retreat:
                            retreatCount++;
                            break;
                    }
                    if (route.type != CombatRouteType.Retreat)
                    {
                        worstImbalance = Mathf.Max(
                            worstImbalance,
                            ComputeRouteImbalance(route));
                    }
                }
            }

            if (!hasMain || !hasFlank || !hasLongRange
                || primaryRouteCount < 3)
            {
                violations.Add(Hard(
                    "routes.missing-archetype",
                    "主路线、地形遮蔽侧翼路线和远程路线缺一不可。",
                    plan.mapCenter));
            }
            if (retreatCount < 2)
            {
                violations.Add(Hard(
                    "routes.insufficient-retreat",
                    "双方都需要各自独立的撤退路线。",
                    plan.mapCenter));
            }
            if (worstImbalance > settings.maximumRouteTimeImbalance)
            {
                violations.Add(Hard(
                    "routes.travel-time-imbalance",
                    "有主要路线超过了允许的对称通行时间差。",
                    plan.mapCenter));
            }

            for (int first = 0;
                 first < primaryRoutes.Count;
                 first++)
            {
                for (int second = first + 1;
                     second < primaryRoutes.Count;
                     second++)
                {
                    minimumRouteSeparation = Mathf.Min(
                        minimumRouteSeparation,
                        ComputeIntermediateSeparation(
                            primaryRoutes[first],
                            primaryRoutes[second]));
                }
            }
            if (primaryRoutes.Count >= 2
                && minimumRouteSeparation < 45f)
            {
                violations.Add(Hard(
                    "routes.not-disjoint",
                    "主要路线在同一中间战斗空间内汇合。",
                    plan.mapCenter));
            }

            int towerCount = 0;
            if (plan.occluders != null)
            {
                for (int i = 0; i < plan.occluders.Length; i++)
                {
                    CombatOccluderData occluder = plan.occluders[i];
                    if (occluder == null)
                        continue;
                    if (!IsFinite(occluder.position)
                        || !IsFinite(occluder.size)
                        || occluder.size.x <= 0f
                        || occluder.size.y <= 0f
                        || occluder.size.z <= 0f)
                    {
                        violations.Add(Hard(
                            "occluder.invalid",
                            "有遮挡物的位置或尺寸无效。",
                            occluder.position));
                    }
                    if (occluder.type == CombatOccluderType.Tower)
                    {
                        towerCount++;
                        Vector2 offset = new Vector2(
                            occluder.position.x - plan.mapCenter.x,
                            occluder.position.z - plan.mapCenter.z);
                        if (offset.magnitude
                            > settings.forfeitRadius - 20f)
                        {
                            violations.Add(Hard(
                                "occluder.outside-arena",
                                "有塔楼位于可玩区域之外。",
                                occluder.position));
                        }
                    }
                }
            }

            ValidateTerrainSamples(settings, plan, violations);

            float chunkSide = Mathf.Ceil(
                settings.mapSize / settings.chunkSize);
            float estimatedChunkCount = chunkSide * chunkSide;
            if (estimatedChunkCount > 100f)
            {
                violations.Add(new CombatMapViolation(
                    "performance.chunk-count",
                    CombatMapViolationSeverity.Warning,
                    "请求的区块数量会使运行时 MeshCollider 场地开销过高。",
                    plan.mapCenter));
            }

            float balanceRatio = settings.maximumRouteTimeImbalance > 0f
                ? Mathf.Clamp01(
                    worstImbalance
                    / settings.maximumRouteTimeImbalance)
                : (worstImbalance <= 0.0001f ? 0f : 1f);
            report.teamBalanceScore = 20f * (1f - balanceRatio);
            report.routeDiversityScore =
                hasMain && hasFlank && hasLongRange ? 15f : 0f;
            report.sightlineAndCoverScore = spawnBlocked
                ? Mathf.Lerp(
                    12f,
                    15f,
                    Mathf.Clamp01(towerCount / 8f))
                : 0f;
            report.objectivePressureScore =
                conflict != null && primaryRouteCount >= 3 ? 15f : 0f;
            report.mobilityCompatibilityScore =
                playerClear && enemyClear ? 15f : 0f;
            report.spawnSafetyScore =
                playerClear && enemyClear && spawnBlocked ? 10f : 0f;
            report.readabilityScore =
                CountAnchors(plan, CombatAnchorType.Landmark) > 0
                    && CountAnchors(
                        plan,
                        CombatAnchorType.PowerPosition) >= 2
                    ? 5f
                    : 2f;
            report.performanceScore = Mathf.Lerp(
                5f,
                2f,
                Mathf.InverseLerp(36f, 100f, estimatedChunkCount));
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
            report.violations = violations.ToArray();
            report.checksum = plan.checksum;
            return report;
        }

        static void ValidateRequiredAnchor(
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
            if (!IsFinite(anchor.position)
                || !IsFinite(anchor.forward)
                || anchor.radius <= 0f)
            {
                violations.Add(Hard(
                    code + ".invalid",
                    "必需的战斗锚点包含无效值。",
                    anchor.position));
            }
        }

        static void ValidateInsideArena(
            CombatSemanticAnchor spawn,
            CombatSemanticPlan plan,
            AirCombatMapSettings settings,
            List<CombatMapViolation> violations)
        {
            Vector2 offset = new Vector2(
                spawn.position.x - plan.mapCenter.x,
                spawn.position.z - plan.mapCenter.z);
            if (offset.magnitude >= settings.warningRadius - 20f)
            {
                violations.Add(Hard(
                    "spawn.outside-safe-radius",
                    "有出生点未安全位于边界警告半径内。",
                    spawn.position));
            }
        }

        static bool ValidateSpawnClearance(
            CombatSemanticAnchor spawn,
            CombatSemanticPlan plan,
            AirCombatMapSettings settings,
            List<CombatMapViolation> violations)
        {
            float ground = CombatMapGenerator.SampleHeight(
                settings,
                plan,
                spawn.position.x,
                spawn.position.z);
            float clearance = spawn.position.y - ground;
            bool valid = IsFinite(clearance)
                && clearance >= settings.minimumGroundClearance
                && clearance <= settings.maximumGroundClearance;
            if (!valid)
            {
                violations.Add(Hard(
                    "spawn.invalid-clearance",
                    "有出生点超出了配置的离地高度范围。",
                    spawn.position));
            }
            return valid;
        }

        static void ValidateRoute(
            CombatSemanticRoute route,
            AirCombatMapSettings settings,
            List<CombatMapViolation> violations)
        {
            if (route.waypoints == null || route.waypoints.Length < 2)
            {
                violations.Add(Hard(
                    "route.empty",
                    "路线至少需要两个路径点。",
                    Vector3.zero));
                return;
            }
            if (route.width < 40f)
            {
                violations.Add(Hard(
                    "route.too-narrow",
                    "飞行路线窄于支持的最小通道宽度。",
                    route.waypoints[0]));
            }
            for (int i = 0; i < route.waypoints.Length; i++)
            {
                if (!IsFinite(route.waypoints[i]))
                {
                    violations.Add(Hard(
                        "route.invalid-waypoint",
                        "路线中有路径点包含非有限数值。",
                        route.waypoints[i]));
                }
            }
            if (route.type != CombatRouteType.Retreat
                && route.Length < settings.spawnDistance * 0.75f)
            {
                violations.Add(Hard(
                    "route.too-short",
                    "主要路线未贯穿双方战斗前线。",
                    route.waypoints[0]));
            }
        }

        static float ComputeRouteImbalance(CombatSemanticRoute route)
        {
            if (route?.waypoints == null
                || route.waypoints.Length < 3)
            {
                return 1f;
            }
            int middle = route.waypoints.Length / 2;
            float first = 0f;
            for (int i = 1; i <= middle; i++)
            {
                first += Vector3.Distance(
                    route.waypoints[i - 1],
                    route.waypoints[i]);
            }
            float second = 0f;
            for (int i = middle + 1;
                 i < route.waypoints.Length;
                 i++)
            {
                second += Vector3.Distance(
                    route.waypoints[i - 1],
                    route.waypoints[i]);
            }
            return Mathf.Abs(first - second)
                / Mathf.Max(1f, Mathf.Max(first, second));
        }

        static float ComputeIntermediateSeparation(
            CombatSemanticRoute first,
            CombatSemanticRoute second)
        {
            if (first?.waypoints == null
                || second?.waypoints == null
                || first.waypoints.Length < 3
                || second.waypoints.Length < 3)
            {
                return 0f;
            }
            float minimum = float.PositiveInfinity;
            for (int a = 1; a < first.waypoints.Length - 1; a++)
            {
                Vector2 firstPoint = new Vector2(
                    first.waypoints[a].x,
                    first.waypoints[a].z);
                for (int b = 1;
                     b < second.waypoints.Length - 1;
                     b++)
                {
                    Vector2 secondPoint = new Vector2(
                        second.waypoints[b].x,
                        second.waypoints[b].z);
                    minimum = Mathf.Min(
                        minimum,
                        Vector2.Distance(firstPoint, secondPoint));
                }
            }
            return minimum;
        }

        static void ValidateTerrainSamples(
            AirCombatMapSettings settings,
            CombatSemanticPlan plan,
            List<CombatMapViolation> violations)
        {
            const int sampleSide = 17;
            float half = settings.mapSize * 0.5f;
            for (int z = 0; z < sampleSide; z++)
            for (int x = 0; x < sampleSide; x++)
            {
                float worldX = plan.mapCenter.x
                    + Mathf.Lerp(-half, half, x / 16f);
                float worldZ = plan.mapCenter.z
                    + Mathf.Lerp(-half, half, z / 16f);
                float height = CombatMapGenerator.SampleHeight(
                    settings,
                    plan,
                    worldX,
                    worldZ);
                if (float.IsNaN(height) || float.IsInfinity(height))
                {
                    violations.Add(Hard(
                        "terrain.non-finite",
                        "地形采样返回了非有限高度值。",
                        new Vector3(worldX, 0f, worldZ)));
                    return;
                }
            }
        }

        static int CountAnchors(
            CombatSemanticPlan plan,
            CombatAnchorType type)
        {
            if (plan.anchors == null)
                return 0;
            int count = 0;
            for (int i = 0; i < plan.anchors.Length; i++)
            {
                if (plan.anchors[i] != null
                    && plan.anchors[i].type == type)
                {
                    count++;
                }
            }
            return count;
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

        static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x)
                && IsFinite(value.y)
                && IsFinite(value.z);
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
