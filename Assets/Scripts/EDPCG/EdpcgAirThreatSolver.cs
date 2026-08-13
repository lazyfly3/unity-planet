using System;
using System.Collections.Generic;
using UnityEngine;
using UnityPlanet.CityPcg;

namespace UnityPlanet.EDPCG
{
    /// <summary>
    /// Deterministic geometry/time solver for the editor air-threat domain.
    /// It deliberately consumes data snapshots instead of Physics or live AI,
    /// so a failed analysis can never fail city loading or mutate a battle.
    /// </summary>
    internal static class EdpcgAirThreatSolver
    {
        const float MaximumRangedContactDistance = 420f;
        const float StrikerActivePositionRadius = 170f;
        const float GunshipActivePositionRadius = 220f;
        const float StrikerMinimumOperationalAltitude = 24f;
        const float GunshipMinimumOperationalAltitude = 34f;
        const float HighestAuthoredNavigationLayer = 160f;
        const float NavigationCorridorRadius = 5f;
        const float ProjectileRadius = 0.08f;
        const float ProjectileSpeed = 240f;
        const float BridgeFallbackRadius = 4.5f;
        const int DirectionCount = 24;
        const int BaseAzimuthSampleCount = 16;
        const int MaximumSilhouetteBuildingCount = 6;
        const int FiringVolumeCandidatesPerDirection = 2;

        static readonly float[] CellSampleOffsets = { -0.28f, 0f, 0.28f };
        static readonly float[] FiringVolumeElevationAngles =
            { -50f, -25f, 0f, 25f, 50f };

        sealed class ThreatAnchor
        {
            public string stableId;
            public string routeId;
            public EdpcgGridFireSourceKind kind;
            public Vector3 localPosition;
            public Vector3 routeEntryLocalPosition;
            public Vector3[] localApproachPoints = Array.Empty<Vector3>();
            public Vector3[] localWindowProbePoints = Array.Empty<Vector3>();
            public int routeSegmentIndex;
            public float routeSegmentT;
            public float approachSeconds;
            public bool throughGap;
            public bool navigationClear;
            public bool robustClear;
        }

        /// <summary>
        /// Spatial lookup for authored route anchors. A firing-volume sample
        /// is only considered reachable when it can connect to one of these
        /// anchors through the same five-metre corridor used by ordinary
        /// enemy navigation.
        /// </summary>
        sealed class RouteAnchorIndex
        {
            const float BucketSize = 96f;
            readonly Dictionary<long, List<ThreatAnchor>> buckets =
                new Dictionary<long, List<ThreatAnchor>>();

            public RouteAnchorIndex(List<ThreatAnchor> anchors)
            {
                if (anchors == null)
                    return;
                for (int index = 0; index < anchors.Count; index++)
                {
                    ThreatAnchor anchor = anchors[index];
                    int x = Coordinate(anchor.localPosition.x);
                    int z = Coordinate(anchor.localPosition.z);
                    long key = Key(x, z);
                    if (!buckets.TryGetValue(key,
                            out List<ThreatAnchor> list))
                    {
                        list = new List<ThreatAnchor>(8);
                        buckets.Add(key, list);
                    }
                    list.Add(anchor);
                }
            }

            public ThreatAnchor FindNearest(
                EdpcgGridFireSourceKind kind,
                Vector3 point,
                float maximumDistance)
            {
                ThreatAnchor best = null;
                float bestDistance = maximumDistance * maximumDistance;
                int minX = Coordinate(point.x - maximumDistance);
                int maxX = Coordinate(point.x + maximumDistance);
                int minZ = Coordinate(point.z - maximumDistance);
                int maxZ = Coordinate(point.z + maximumDistance);
                for (int x = minX; x <= maxX; x++)
                for (int z = minZ; z <= maxZ; z++)
                {
                    if (!buckets.TryGetValue(Key(x, z),
                            out List<ThreatAnchor> list))
                    {
                        continue;
                    }
                    for (int index = 0; index < list.Count; index++)
                    {
                        ThreatAnchor candidate = list[index];
                        if (candidate.kind != kind)
                            continue;
                        float distance = (candidate.localPosition - point)
                            .sqrMagnitude;
                        if (distance >= bestDistance)
                            continue;
                        best = candidate;
                        bestDistance = distance;
                    }
                }
                return best;
            }

            static int Coordinate(float value)
            {
                return Mathf.FloorToInt(value / BucketSize);
            }

            static long Key(int x, int z)
            {
                return ((long)x << 32) ^ (uint)z;
            }
        }

        sealed class GeometryIndex
        {
            const float BucketSize = 96f;
            readonly AirCombatCityRuntimeGeometrySnapshot snapshot;
            readonly Dictionary<long, List<int>> buildingBuckets =
                new Dictionary<long, List<int>>();
            readonly Dictionary<long, List<int>> bridgeBuckets =
                new Dictionary<long, List<int>>();
            readonly int[] buildingStamp;
            readonly int[] bridgeStamp;
            int stamp;

            public GeometryIndex(AirCombatCityRuntimeGeometrySnapshot source)
            {
                snapshot = source;
                int buildingCount = source?.buildings?.Length ?? 0;
                int bridgeCount = source?.skybridges?.Length ?? 0;
                buildingStamp = new int[buildingCount];
                bridgeStamp = new int[bridgeCount];
                for (int index = 0; index < buildingCount; index++)
                    AddToBuckets(buildingBuckets,
                        source.buildings[index].localBounds, index);
                for (int index = 0; index < bridgeCount; index++)
                {
                    AirCombatRuntimeConnectionGeometry bridge =
                        source.skybridges[index];
                    Bounds bounds = new Bounds(bridge.localStart, Vector3.zero);
                    bounds.Encapsulate(bridge.localEnd);
                    bounds.Expand(BridgeFallbackRadius * 2f);
                    AddToBuckets(bridgeBuckets, bounds, index);
                }
            }

            public bool PointBlocked(Vector3 point, float radius)
            {
                Bounds query = new Bounds(point,
                    Vector3.one * Mathf.Max(0.2f, radius * 2f));
                int currentStamp = NextStamp();
                foreach (int index in Query(buildingBuckets, query))
                {
                    if (buildingStamp[index] == currentStamp)
                        continue;
                    buildingStamp[index] = currentStamp;
                    Bounds bounds = snapshot.buildings[index].localBounds;
                    bounds.Expand(radius * 2f);
                    if (bounds.Contains(point))
                        return true;
                }
                foreach (int index in Query(bridgeBuckets, query))
                {
                    if (bridgeStamp[index] == currentStamp)
                        continue;
                    bridgeStamp[index] = currentStamp;
                    AirCombatRuntimeConnectionGeometry bridge =
                        snapshot.skybridges[index];
                    Vector3 closest = ClosestPointOnSegment(
                        bridge.localStart, bridge.localEnd, point);
                    float combined = BridgeFallbackRadius + radius;
                    if ((closest - point).sqrMagnitude <= combined * combined)
                        return true;
                }
                return false;
            }

            public bool IsCorridorClear(Vector3 start, Vector3 end,
                float radius)
            {
                return !TryFirstBlocker(start, end, radius, out _, out _,
                    out _);
            }

            public bool TryFirstBlocker(
                Vector3 start,
                Vector3 end,
                float radius,
                out string blockerId,
                out Vector3 blockerPoint,
                out float blockerDistance)
            {
                blockerId = string.Empty;
                blockerPoint = Vector3.zero;
                blockerDistance = float.PositiveInfinity;
                Vector3 delta = end - start;
                float length = delta.magnitude;
                if (length < 0.01f)
                    return false;
                Vector3 direction = delta / length;
                Ray ray = new Ray(start, direction);
                Bounds query = new Bounds(start, Vector3.zero);
                query.Encapsulate(end);
                query.Expand(Mathf.Max(0.2f, radius * 2f));
                int currentStamp = NextStamp();

                foreach (int index in Query(buildingBuckets, query))
                {
                    if (buildingStamp[index] == currentStamp)
                        continue;
                    buildingStamp[index] = currentStamp;
                    AirCombatRuntimeBuildingGeometry building =
                        snapshot.buildings[index];
                    Bounds bounds = building.localBounds;
                    bounds.Expand(radius * 2f);
                    if (!bounds.IntersectRay(ray, out float distance) ||
                        distance <= 0.05f || distance >= length - 0.05f ||
                        distance >= blockerDistance)
                    {
                        continue;
                    }
                    blockerDistance = distance;
                    blockerPoint = ray.GetPoint(distance);
                    blockerId = string.IsNullOrEmpty(building.stableId)
                        ? "实体建筑 " + (index + 1)
                        : building.stableId;
                }

                foreach (int index in Query(bridgeBuckets, query))
                {
                    if (bridgeStamp[index] == currentStamp)
                        continue;
                    bridgeStamp[index] = currentStamp;
                    AirCombatRuntimeConnectionGeometry bridge =
                        snapshot.skybridges[index];
                    ClosestSegmentParameters(
                        start, end, bridge.localStart, bridge.localEnd,
                        out float fireT, out float bridgeT,
                        out float sqrDistance);
                    float combined = BridgeFallbackRadius + radius;
                    if (sqrDistance > combined * combined ||
                        fireT <= 0.001f || fireT >= 0.999f ||
                        bridgeT < 0f || bridgeT > 1f)
                    {
                        continue;
                    }
                    float distance = fireT * length;
                    if (distance >= blockerDistance)
                        continue;
                    blockerDistance = distance;
                    blockerPoint = Vector3.Lerp(start, end, fireT);
                    blockerId = "空中连廊 " + (index + 1);
                }
                return !string.IsNullOrEmpty(blockerId);
            }

            public bool TryResolveOpposingBuildingGap(
                Vector3 point,
                out Vector3 passageDirection)
            {
                const float probeDistance = 48f;
                passageDirection = Vector3.zero;
                float bestWidth = float.PositiveInfinity;
                for (int axis = 0; axis < 4; axis++)
                {
                    Vector3 across = Quaternion.Euler(
                        0f, axis * 45f, 0f) * Vector3.right;
                    if (!TryFirstBuildingAlong(point, -across,
                            probeDistance, out float negative) ||
                        !TryFirstBuildingAlong(point, across,
                            probeDistance, out float positive))
                    {
                        continue;
                    }
                    float width = negative + positive;
                    // Below 12m is not a robust two-sided aerial passage;
                    // above 88m reads as an open street, not a building gap.
                    if (negative <= 4f || positive <= 4f ||
                        width < 12f || width > 88f || width >= bestWidth)
                    {
                        continue;
                    }
                    bestWidth = width;
                    passageDirection = Vector3.Cross(Vector3.up, across)
                        .normalized;
                }
                return passageDirection.sqrMagnitude > 0.5f;
            }

            public bool HasHoldingVolume(
                Vector3 point,
                Vector3 passageDirection,
                float radius)
            {
                if (passageDirection.sqrMagnitude < 0.5f)
                    return false;
                passageDirection.Normalize();
                float halfLength = 16f + Mathf.Max(5f, radius);
                Vector3 start = point - passageDirection * halfLength;
                Vector3 end = point + passageDirection * halfLength;
                return !PointBlocked(start, radius) &&
                       !PointBlocked(end, radius) &&
                       IsCorridorClear(start, end, radius);
            }

            /// <summary>
            /// Adds directions near the silhouettes of the nearest buildings.
            /// The base spherical samples cover open air; these refinements
            /// are what let the solver see narrow windows beside differently
            /// sized buildings and near their roof lines.
            /// </summary>
            public void AppendBuildingSilhouetteDirections(
                Vector3 target,
                float maximumDistance,
                List<Vector3> output)
            {
                if (output == null || snapshot?.buildings == null ||
                    snapshot.buildings.Length == 0)
                {
                    return;
                }
                var nearest = new int[MaximumSilhouetteBuildingCount];
                var distances = new float[MaximumSilhouetteBuildingCount];
                for (int index = 0; index < nearest.Length; index++)
                {
                    nearest[index] = -1;
                    distances[index] = float.PositiveInfinity;
                }
                Bounds query = new Bounds(target,
                    Vector3.one * maximumDistance * 2f);
                int currentStamp = NextStamp();
                foreach (int buildingIndex in Query(buildingBuckets, query))
                {
                    if (buildingStamp[buildingIndex] == currentStamp)
                        continue;
                    buildingStamp[buildingIndex] = currentStamp;
                    Bounds bounds = snapshot.buildings[buildingIndex]
                        .localBounds;
                    float distance = Vector3.Distance(
                        bounds.ClosestPoint(target), target);
                    if (distance > maximumDistance)
                        continue;
                    for (int slot = 0; slot < nearest.Length; slot++)
                    {
                        if (distance >= distances[slot])
                            continue;
                        for (int shift = nearest.Length - 1;
                             shift > slot;
                             shift--)
                        {
                            nearest[shift] = nearest[shift - 1];
                            distances[shift] = distances[shift - 1];
                        }
                        nearest[slot] = buildingIndex;
                        distances[slot] = distance;
                        break;
                    }
                }

                for (int slot = 0; slot < nearest.Length; slot++)
                {
                    if (nearest[slot] < 0)
                        continue;
                    Bounds bounds = snapshot.buildings[nearest[slot]]
                        .localBounds;
                    float middleY = Mathf.Clamp(
                        target.y, bounds.min.y, bounds.max.y);
                    AppendSilhouetteCorners(
                        target, bounds, middleY, output);
                    if (Mathf.Abs(bounds.max.y - middleY) > 4f)
                    {
                        AppendSilhouetteCorners(
                            target, bounds, bounds.max.y, output);
                    }
                }
            }

            static void AppendSilhouetteCorners(
                Vector3 target,
                Bounds bounds,
                float y,
                List<Vector3> output)
            {
                for (int x = 0; x < 2; x++)
                for (int z = 0; z < 2; z++)
                {
                    Vector3 corner = new Vector3(
                        x == 0 ? bounds.min.x : bounds.max.x,
                        y,
                        z == 0 ? bounds.min.z : bounds.max.z);
                    Vector3 direction = corner - target;
                    if (direction.sqrMagnitude < 1f)
                        continue;
                    direction.Normalize();
                    output.Add(Quaternion.Euler(0f, -2.5f, 0f) * direction);
                    output.Add(Quaternion.Euler(0f, 2.5f, 0f) * direction);
                }
            }

            bool TryFirstBuildingAlong(Vector3 point, Vector3 direction,
                float length, out float distance)
            {
                distance = float.PositiveInfinity;
                Vector3 end = point + direction * length;
                Ray ray = new Ray(point, direction);
                Bounds query = new Bounds(point, Vector3.zero);
                query.Encapsulate(end);
                query.Expand(1f);
                int currentStamp = NextStamp();
                foreach (int index in Query(buildingBuckets, query))
                {
                    if (buildingStamp[index] == currentStamp)
                        continue;
                    buildingStamp[index] = currentStamp;
                    Bounds bounds = snapshot.buildings[index].localBounds;
                    if (bounds.IntersectRay(ray, out float hit) &&
                        hit > 0.05f && hit < distance && hit <= length)
                    {
                        distance = hit;
                    }
                }
                return !float.IsPositiveInfinity(distance);
            }

            int NextStamp()
            {
                stamp++;
                if (stamp != int.MaxValue)
                    return stamp;
                Array.Clear(buildingStamp, 0, buildingStamp.Length);
                Array.Clear(bridgeStamp, 0, bridgeStamp.Length);
                stamp = 1;
                return stamp;
            }

            static void AddToBuckets(Dictionary<long, List<int>> buckets,
                Bounds bounds, int index)
            {
                int minX = Coordinate(bounds.min.x);
                int maxX = Coordinate(bounds.max.x);
                int minZ = Coordinate(bounds.min.z);
                int maxZ = Coordinate(bounds.max.z);
                for (int x = minX; x <= maxX; x++)
                for (int z = minZ; z <= maxZ; z++)
                {
                    long key = Key(x, z);
                    if (!buckets.TryGetValue(key, out List<int> list))
                    {
                        list = new List<int>(8);
                        buckets.Add(key, list);
                    }
                    list.Add(index);
                }
            }

            static IEnumerable<int> Query(
                Dictionary<long, List<int>> buckets, Bounds bounds)
            {
                int minX = Coordinate(bounds.min.x);
                int maxX = Coordinate(bounds.max.x);
                int minZ = Coordinate(bounds.min.z);
                int maxZ = Coordinate(bounds.max.z);
                for (int x = minX; x <= maxX; x++)
                for (int z = minZ; z <= maxZ; z++)
                {
                    if (!buckets.TryGetValue(Key(x, z), out List<int> list))
                        continue;
                    for (int index = 0; index < list.Count; index++)
                        yield return list[index];
                }
            }

            static int Coordinate(float value)
            {
                return Mathf.FloorToInt(value / BucketSize);
            }

            static long Key(int x, int z)
            {
                return ((long)x << 32) ^ (uint)z;
            }
        }

        public static EdpcgGridFireAnalysis Build(
            AirCombatCityPlan plan,
            AirCombatCityRuntimeGeometrySnapshot snapshot,
            AirCombatCitySettings citySettings,
            EdpcgFireAnalysisAltitudeLayer altitudeLayer,
            float referenceShipWidth,
            EdpcgAirThreatAnalysisOptions rawOptions,
            Func<Vector3, Vector3> projector)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            EdpcgAirThreatAnalysisOptions options =
                (rawOptions ?? new EdpcgAirThreatAnalysisOptions())
                .ValidatedCopy();
            var result = new EdpcgGridFireAnalysis
            {
                requestedSeed = plan?.requestedSeed ?? 0,
                resolvedSeed = plan?.resolvedSeed ?? 0,
                altitudeLayer = altitudeLayer,
                altitude = ResolvePlayerAltitude(citySettings, altitudeLayer),
                referenceShipWidth = Mathf.Max(4f,
                    referenceShipWidth > 0f
                        ? referenceShipWidth
                        : citySettings?.wingspan ?? 18f),
                geometryConfidence = snapshot != null &&
                                     snapshot.skybridgeCount > 0
                    ? 0.74f
                    : 0.82f
            };
            if (plan == null || snapshot == null || !snapshot.IsUsable ||
                citySettings == null || projector == null)
            {
                stopwatch.Stop();
                result.buildMilliseconds =
                    (float)stopwatch.Elapsed.TotalMilliseconds;
                return result;
            }

            var geometry = new GeometryIndex(snapshot);
            List<ThreatAnchor> anchors = BuildAnchors(
                plan, snapshot, geometry, options);
            var routeAnchorIndex = new RouteAnchorIndex(anchors);
            result.sourceCount = anchors.Count;
            result.firingVolumeRadius = GunshipActivePositionRadius;
            result.minimumFiringAltitude =
                StrikerMinimumOperationalAltitude;
            result.gunshipMinimumFiringAltitude =
                GunshipMinimumOperationalAltitude;
            result.maximumFiringAltitude = Mathf.Max(
                result.minimumFiringAltitude + 12f,
                Mathf.Min(
                    citySettings.maximumAltitude -
                    RoleRobustRadius(
                        EdpcgGridFireSourceKind.Gunship) - 1f,
                    Mathf.Max(
                        HighestAuthoredNavigationLayer + 20f,
                        result.altitude + 35f)));
            result.authorizedConcurrentDirections = ResolveDirectionBudget(
                options);
            float playerHullRadius = Mathf.Max(
                NavigationCorridorRadius,
                result.referenceShipWidth * 0.5f);

            for (int blockIndex = 0;
                 blockIndex < plan.tacticalBlocks.Count;
                 blockIndex++)
            {
                CombatCityBlockPlan block = plan.tacticalBlocks[blockIndex];
                if (block == null)
                    continue;
                var cell = new EdpcgGridFireCell
                {
                    stableId = block.stableId,
                    gridX = block.gridX,
                    gridZ = block.gridZ,
                    localBounds = block.bounds,
                    worldCorners = BuildWorldCorners(
                        block.bounds, result.altitude, projector)
                };
                List<Vector3> playerSamples = BuildPlayerSamples(
                    block.bounds, result.altitude, playerHullRadius, geometry,
                    out Vector3 representative);
                cell.subSampleCount = 9;
                cell.flyableSubSampleCount = playerSamples.Count;
                cell.flyable = playerSamples.Count > 0;
                cell.localSamplePosition = representative;
                cell.worldSamplePosition = projector(representative);
                result.analyzedSubSampleCount += playerSamples.Count;
                if (cell.flyable)
                {
                    result.sourceCount += AnalyzeCell(
                        cell, playerSamples, anchors, routeAnchorIndex,
                        geometry, options, projector,
                        result.minimumFiringAltitude,
                        result.maximumFiringAltitude,
                        plan.mapSize * 0.5f);
                    result.flyableCellCount++;
                    result.fireWindowCount += cell.fireWindows.Count;
                    result.gapWindowCount += cell.gapWindowCount;
                    result.fleetingWindowCount += cell.fleetingWindowCount;
                    result.incomingLineCount += cell.incomingLineCount;
                    result.blockedLineCount += cell.blockedLineCount;
                }
                result.cells.Add(cell);
            }

            result.RebuildIndex();
            BuildHorizontalEvasions(result, geometry, playerHullRadius,
                options);
            stopwatch.Stop();
            result.buildMilliseconds =
                (float)stopwatch.Elapsed.TotalMilliseconds;
            return result;
        }

        public static void LinkAltitudeLayers(
            EdpcgGridFireAnalysis low,
            EdpcgGridFireAnalysis medium,
            EdpcgGridFireAnalysis high,
            AirCombatCityRuntimeGeometrySnapshot snapshot,
            EdpcgAirThreatAnalysisOptions options)
        {
            if (snapshot == null || !snapshot.IsUsable || low == null ||
                medium == null || high == null)
            {
                return;
            }
            var geometry = new GeometryIndex(snapshot);
            RemoveVerticalLinks(low);
            RemoveVerticalLinks(medium);
            RemoveVerticalLinks(high);
            AddVerticalPair(low, medium, geometry, options);
            AddVerticalPair(medium, high, geometry, options);
            RecountEvasions(low);
            RecountEvasions(medium);
            RecountEvasions(high);
        }

        static List<ThreatAnchor> BuildAnchors(
            AirCombatCityPlan plan,
            AirCombatCityRuntimeGeometrySnapshot snapshot,
            GeometryIndex geometry,
            EdpcgAirThreatAnalysisOptions options)
        {
            var routeAnchors = new List<ThreatAnchor>(512);
            AirCombatRuntimeRouteGeometry[] routes = snapshot.routes;
            if (routes == null || routes.Length == 0)
            {
                routes = new AirCombatRuntimeRouteGeometry[plan.routes.Count];
                for (int index = 0; index < plan.routes.Count; index++)
                {
                    AirCombatFlightRoute route = plan.routes[index];
                    routes[index] = new AirCombatRuntimeRouteGeometry
                    {
                        stableId = route.stableId,
                        kind = route.kind,
                        width = route.width,
                        localPoints = route.points
                    };
                }
            }

            for (int routeIndex = 0; routeIndex < routes.Length; routeIndex++)
            {
                AirCombatRuntimeRouteGeometry route = routes[routeIndex];
                if (!SupportsOrdinaryRangedRole(route.kind) ||
                    route.localPoints == null || route.localPoints.Length < 2)
                {
                    continue;
                }
                for (int segment = 0;
                     segment < route.localPoints.Length - 1;
                     segment++)
                {
                    Vector3 start = route.localPoints[segment];
                    Vector3 end = route.localPoints[segment + 1];
                    float length = Vector3.Distance(start, end);
                    int steps = Mathf.Max(1,
                        Mathf.CeilToInt(length / options.routeSampleSpacing));
                    int firstStep = segment == 0 ? 0 : 1;
                    for (int step = firstStep; step <= steps; step++)
                    {
                        float amount = step / (float)steps;
                        Vector3 point = Vector3.Lerp(start, end, amount);
                        AddRouteAnchor(routeAnchors, geometry, route,
                            segment, amount, point, start, end,
                            EdpcgGridFireSourceKind.Striker, options);
                        AddRouteAnchor(routeAnchors, geometry, route,
                            segment, amount, point, start, end,
                            EdpcgGridFireSourceKind.Gunship, options);
                    }
                }
            }

            return routeAnchors;
        }

        static void AddRouteAnchor(
            List<ThreatAnchor> output,
            GeometryIndex geometry,
            AirCombatRuntimeRouteGeometry route,
            int segmentIndex,
            float segmentT,
            Vector3 point,
            Vector3 segmentStart,
            Vector3 segmentEnd,
            EdpcgGridFireSourceKind kind,
            EdpcgAirThreatAnalysisOptions options)
        {
            float robustRadius = RoleRobustRadius(kind);
            bool robustClear = !geometry.PointBlocked(point, robustRadius);
            bool navigationClear = !geometry.PointBlocked(
                point, NavigationCorridorRadius);
            if (!navigationClear)
                return;
            bool throughGap = geometry.TryResolveOpposingBuildingGap(
                point, out Vector3 passageDirection);
            if (throughGap)
            {
                robustClear &= geometry.HasHoldingVolume(
                    point, passageDirection, robustRadius);
            }
            float length = Vector3.Distance(segmentStart, segmentEnd);
            var probes = new Vector3[5];
            for (int index = 0; index < probes.Length; index++)
            {
                float offset = (index - 2) * options.routeSampleSpacing;
                float amount = Mathf.Clamp01(
                    segmentT + offset / Mathf.Max(1f, length));
                probes[index] = Vector3.Lerp(
                    segmentStart, segmentEnd, amount);
            }
            output.Add(new ThreatAnchor
            {
                stableId = (route.stableId ?? "route") + "." +
                           segmentIndex + "." +
                           Mathf.RoundToInt(segmentT * 1000f) + "." + kind,
                routeId = route.stableId ?? string.Empty,
                kind = kind,
                localPosition = point,
                routeEntryLocalPosition = point,
                localApproachPoints = new[] { point },
                localWindowProbePoints = probes,
                routeSegmentIndex = segmentIndex,
                routeSegmentT = segmentT,
                approachSeconds = 0f,
                throughGap = throughGap,
                navigationClear = true,
                robustClear = robustClear
            });
        }

        static ThreatAnchor CreateFiringVolumeAnchor(
            EdpcgGridFireCell cell,
            int sampleIndex,
            Vector3 point,
            ThreatAnchor entry,
            float distance,
            EdpcgGridFireSourceKind kind,
            bool gap,
            bool robust)
        {
            var approach = new Vector3[5];
            for (int index = 0; index < approach.Length; index++)
                approach[index] = Vector3.Lerp(
                    entry.localPosition, point,
                    index / (float)(approach.Length - 1));
            var probes = new Vector3[7];
            float startAmount = Mathf.Clamp01(1f - 90f / Mathf.Max(1f,
                distance));
            for (int index = 0; index < probes.Length; index++)
            {
                float amount = Mathf.Lerp(startAmount, 1f,
                    index / (float)(probes.Length - 1));
                probes[index] = Vector3.Lerp(
                    entry.localPosition, point, amount);
            }
            return new ThreatAnchor
            {
                stableId = (gap ? "有效包络楼缝枪窗." : "有效包络枪位.") +
                           cell.gridX + "." + cell.gridZ + "." +
                           sampleIndex + "." + kind,
                routeId = entry.routeId,
                kind = kind,
                localPosition = point,
                routeEntryLocalPosition = entry.localPosition,
                localApproachPoints = approach,
                localWindowProbePoints = probes,
                routeSegmentIndex = entry.routeSegmentIndex,
                routeSegmentT = entry.routeSegmentT,
                approachSeconds = TravelSeconds(
                    distance, RoleSpeed(kind), RoleAcceleration(kind)),
                throughGap = gap,
                navigationClear = true,
                robustClear = robust
            };
        }

        static int AnalyzeCell(
            EdpcgGridFireCell cell,
            List<Vector3> playerSamples,
            List<ThreatAnchor> routeAnchors,
            RouteAnchorIndex routeAnchorIndex,
            GeometryIndex geometry,
            EdpcgAirThreatAnalysisOptions options,
            Func<Vector3, Vector3> projector,
            float minimumAltitude,
            float maximumAltitude,
            float mapHalfExtent)
        {
            const int CandidateValidationDepth = 6;
            int slots = 2 * DirectionCount * CandidateValidationDepth;
            var shortlist = new ThreatAnchor[slots];
            var shortlistCost = new float[slots];
            for (int index = 0; index < shortlistCost.Length; index++)
                shortlistCost[index] = float.PositiveInfinity;

            Vector3 target = cell.localSamplePosition;
            for (int index = 0; index < routeAnchors.Count; index++)
            {
                ThreatAnchor anchor = routeAnchors[index];
                if (!IsWithinOperationalAltitude(
                        anchor.kind,
                        anchor.localPosition.y,
                        minimumAltitude,
                        maximumAltitude))
                {
                    continue;
                }
                float distance = Vector3.Distance(anchor.localPosition, target);
                if (distance < 0.5f ||
                    distance > MaximumRangedContactDistance)
                {
                    continue;
                }
                int direction = ThreatDirection(target,
                    anchor.localPosition, out _, out _);
                int group = ((int)anchor.kind * DirectionCount + direction) *
                            CandidateValidationDepth;
                float roughCost = anchor.approachSeconds +
                                  distance / ProjectileSpeed;
                InsertShortlist(shortlist, shortlistCost, group,
                    CandidateValidationDepth, anchor, roughCost);
            }

            List<ThreatAnchor> volumeAnchors = BuildFiringVolumeAnchors(
                cell, routeAnchorIndex, geometry, options,
                minimumAltitude, maximumAltitude, mapHalfExtent,
                projector);
            for (int index = 0; index < volumeAnchors.Count; index++)
            {
                ThreatAnchor anchor = volumeAnchors[index];
                float distance = Vector3.Distance(anchor.localPosition, target);
                int direction = ThreatDirection(target,
                    anchor.localPosition, out _, out _);
                int group = ((int)anchor.kind * DirectionCount + direction) *
                            CandidateValidationDepth;
                float roughCost = anchor.approachSeconds +
                                  distance / ProjectileSpeed;
                InsertShortlist(shortlist, shortlistCost, group,
                    CandidateValidationDepth, anchor, roughCost);
            }

            bool[] threatenedSamples = new bool[playerSamples.Count];
            bool[][] threatenedSamplesByRole =
            {
                new bool[playerSamples.Count],
                new bool[playerSamples.Count]
            };
            for (int slot = 0; slot < shortlist.Length; slot++)
            {
                ThreatAnchor anchor = shortlist[slot];
                if (anchor == null)
                    continue;
                AnalyzeAnchor(cell, playerSamples, threatenedSamples,
                    threatenedSamplesByRole[(int)anchor.kind],
                    anchor, geometry, options, projector);
            }

            int threatenedCount = 0;
            for (int index = 0; index < threatenedSamples.Length; index++)
            {
                if (threatenedSamples[index])
                    threatenedCount++;
            }
            cell.threatenedVolumeRatio = playerSamples.Count == 0
                ? 0f
                : threatenedCount / (float)playerSamples.Count;
            for (int role = 0; role < threatenedSamplesByRole.Length; role++)
            {
                int roleCount = 0;
                for (int sample = 0;
                     sample < threatenedSamplesByRole[role].Length;
                     sample++)
                {
                    if (threatenedSamplesByRole[role][sample])
                        roleCount++;
                }
                cell.threatenedVolumeRatioByRole[role] =
                    playerSamples.Count == 0
                        ? 0f
                        : roleCount / (float)playerSamples.Count;
            }
            SelectAuthorizedWindows(cell, options);
            int authorizedSampleMask = 0;
            cell.authorizedFastestHitSeconds = float.PositiveInfinity;
            for (int windowIndex = 0;
                 windowIndex < cell.fireWindows.Count;
                 windowIndex++)
            {
                EdpcgAirFireWindow window = cell.fireWindows[windowIndex];
                if (!window.authorizedByTier)
                    continue;
                authorizedSampleMask |= window.threatenedSampleMask;
                cell.authorizedFastestHitSeconds = Mathf.Min(
                    cell.authorizedFastestHitSeconds,
                    window.firstHitSeconds);
            }
            cell.authorizedThreatenedVolumeRatio =
                playerSamples.Count == 0
                    ? 0f
                    : CountBits(authorizedSampleMask) /
                      (float)playerSamples.Count;
            cell.potentialDirectionCount = CountDirectionMasks(
                cell.potentialDirectionMaskLow,
                cell.potentialDirectionMaskMid,
                cell.potentialDirectionMaskHigh);
            cell.forecastFourDirectionCount = CountDirectionMasks(
                cell.forecastFourDirectionMaskLow,
                cell.forecastFourDirectionMaskMid,
                cell.forecastFourDirectionMaskHigh);
            cell.forecastEightDirectionCount = CountDirectionMasks(
                cell.forecastEightDirectionMaskLow,
                cell.forecastEightDirectionMaskMid,
                cell.forecastEightDirectionMaskHigh);
            cell.authorizedSpatialChannelCount = CountDirectionMasks(
                cell.authorizedDirectionMaskLow,
                cell.authorizedDirectionMaskMid,
                cell.authorizedDirectionMaskHigh);
            cell.authorizedDirectionCount = CountBits(HorizontalMask(
                cell.authorizedDirectionMaskLow,
                cell.authorizedDirectionMaskMid,
                cell.authorizedDirectionMaskHigh));
            cell.threatDirectionCount = cell.authorizedDirectionCount;
            cell.threatSectorMask = HorizontalMask(
                cell.authorizedDirectionMaskLow,
                cell.authorizedDirectionMaskMid,
                cell.authorizedDirectionMaskHigh);
            cell.crossfire = HasSeparatedDirections(cell.fireWindows, true);
            float urgency = float.IsPositiveInfinity(
                cell.authorizedFastestHitSeconds)
                ? 0f
                : Mathf.Clamp01(
                    (8f - cell.authorizedFastestHitSeconds) / 8f);
            cell.pressureScore = Mathf.Clamp01(
                cell.authorizedThreatenedVolumeRatio * 0.40f +
                Mathf.Clamp01(cell.authorizedDirectionCount / 3f) * 0.28f +
                urgency * 0.22f +
                (cell.crossfire ? 0.10f : 0f));
            return volumeAnchors.Count;
        }

        /// <summary>
        /// Searches the practical active-position envelope around the selected
        /// player cell. Its horizontal radii follow the existing Striker and
        /// Gunship standoff policies plus one telegraph-length of travel. Its
        /// height is clipped to grounded pursuit, the authored 60/110/160m
        /// navigation layers and the existing player-relative reacquire offset.
        /// Authored route anchors are analyzed separately out to the legal
        /// 420m fire limit, so real in-transit long shots are still retained.
        /// </summary>
        static List<ThreatAnchor> BuildFiringVolumeAnchors(
            EdpcgGridFireCell cell,
            RouteAnchorIndex routeAnchorIndex,
            GeometryIndex geometry,
            EdpcgAirThreatAnalysisOptions options,
            float minimumAltitude,
            float maximumAltitude,
            float mapHalfExtent,
            Func<Vector3, Vector3> projector)
        {
            var directions = new List<Vector3>(192);
            BuildBaseFiringVolumeDirections(directions);
            geometry.AppendBuildingSilhouetteDirections(
                cell.localSamplePosition,
                GunshipActivePositionRadius,
                directions);
            DeduplicateDirections(directions);

            int groups = 2 * DirectionCount;
            var best = new ThreatAnchor[
                groups * FiringVolumeCandidatesPerDirection];
            var bestCost = new float[best.Length];
            var nearestOcclusion =
                new EdpcgFiringVolumeOcclusionRay[DirectionCount];
            var nearestOcclusionDistance = new float[DirectionCount];
            for (int index = 0; index < bestCost.Length; index++)
                bestCost[index] = float.PositiveInfinity;
            for (int index = 0;
                 index < nearestOcclusionDistance.Length;
                 index++)
            {
                nearestOcclusionDistance[index] = float.PositiveInfinity;
            }

            Vector3 target = cell.localSamplePosition;
            int sampleSequence = 0;
            for (int directionIndex = 0;
                 directionIndex < directions.Count;
                 directionIndex++)
            {
                Vector3 direction = directions[directionIndex];
                float clearDistance = MaximumDistanceInsideFiringVolume(
                    target, direction, minimumAltitude, maximumAltitude,
                    mapHalfExtent, GunshipActivePositionRadius);
                if (clearDistance < 18f)
                    continue;
                Vector3 end = target + direction * clearDistance;
                if (geometry.TryFirstBlocker(
                        target, end, NavigationCorridorRadius,
                        out string blockerId,
                        out Vector3 blockerPoint,
                        out float blockerDistance))
                {
                    int blockedDirection = ThreatDirection(
                        target, end, out _, out _);
                    if (blockerDistance <
                        nearestOcclusionDistance[blockedDirection])
                    {
                        nearestOcclusionDistance[blockedDirection] =
                            blockerDistance;
                        nearestOcclusion[blockedDirection] =
                            new EdpcgFiringVolumeOcclusionRay
                            {
                                directionIndex = blockedDirection,
                                playerWorldPosition =
                                    cell.worldSamplePosition,
                                sampleWorldPosition = projector(end),
                                blockerWorldPosition =
                                    projector(blockerPoint),
                                blockerId = blockerId,
                                blockerDistance = blockerDistance
                            };
                    }
                    clearDistance = Mathf.Min(
                        clearDistance, blockerDistance -
                        NavigationCorridorRadius - 0.5f);
                }
                if (clearDistance < 18f)
                    continue;

                for (int role = 0; role < 2; role++)
                {
                    EdpcgGridFireSourceKind kind =
                        (EdpcgGridFireSourceKind)role;
                    float activePositionRadius = kind ==
                                                 EdpcgGridFireSourceKind.Striker
                        ? StrikerActivePositionRadius
                        : GunshipActivePositionRadius;
                    float roleClearDistance = Mathf.Min(
                        clearDistance, activePositionRadius);
                    if (roleClearDistance < 18f)
                        continue;
                    float preferredDistance = kind ==
                                              EdpcgGridFireSourceKind.Striker
                        ? 110f
                        : 155f;
                    float[] distances =
                    {
                        Mathf.Min(preferredDistance, roleClearDistance),
                        roleClearDistance * 0.68f,
                        roleClearDistance - 1.5f
                    };
                    for (int distanceIndex = 0;
                         distanceIndex < distances.Length;
                         distanceIndex++)
                    {
                        float firingDistance = Mathf.Clamp(
                            distances[distanceIndex], 18f,
                            roleClearDistance);
                        if (distanceIndex > 0 && Mathf.Abs(
                                firingDistance - distances[distanceIndex - 1]) <
                            8f)
                        {
                            continue;
                        }
                        Vector3 point = target + direction * firingDistance;
                        if (!TryCreateFiringVolumeAnchor(
                                cell, sampleSequence++, point, target, kind,
                                routeAnchorIndex, geometry, options,
                                out ThreatAnchor candidate))
                        {
                            continue;
                        }
                        int threatDirection = ThreatDirection(
                            target, point, out _, out _);
                        int group = ((int)kind * DirectionCount +
                                     threatDirection) *
                                    FiringVolumeCandidatesPerDirection;
                        float cost = candidate.approachSeconds +
                                     firingDistance / ProjectileSpeed +
                                     (candidate.throughGap ? -0.05f : 0f);
                        InsertShortlist(best, bestCost, group,
                            FiringVolumeCandidatesPerDirection,
                            candidate, cost);
                        // A valid point at this distance proves this ray. The
                        // next spherical direction, rather than a denser stack
                        // on the same ray, provides the useful refinement.
                        break;
                    }
                }
            }

            var result = new List<ThreatAnchor>(best.Length);
            for (int index = 0; index < best.Length; index++)
            {
                if (best[index] != null)
                    result.Add(best[index]);
            }
            for (int index = 0; index < nearestOcclusion.Length; index++)
            {
                if (nearestOcclusion[index] != null)
                    cell.firingVolumeOcclusions.Add(
                        nearestOcclusion[index]);
            }
            return result;
        }

        static bool TryCreateFiringVolumeAnchor(
            EdpcgGridFireCell cell,
            int sampleIndex,
            Vector3 point,
            Vector3 target,
            EdpcgGridFireSourceKind kind,
            RouteAnchorIndex routeAnchorIndex,
            GeometryIndex geometry,
            EdpcgAirThreatAnalysisOptions options,
            out ThreatAnchor candidate)
        {
            candidate = null;
            if (!IsWithinOperationalAltitude(
                    kind, point.y,
                    StrikerMinimumOperationalAltitude,
                    float.PositiveInfinity))
            {
                return false;
            }
            float robustRadius = RoleRobustRadius(kind);
            if (geometry.PointBlocked(point, NavigationCorridorRadius))
                return false;
            ThreatAnchor entry = routeAnchorIndex.FindNearest(
                kind, point, options.maximumApproachDistance);
            if (entry == null)
                return false;
            float approachDistance = Vector3.Distance(
                entry.localPosition, point);
            if (approachDistance >= options.routeSampleSpacing * 0.75f &&
                !geometry.IsCorridorClear(
                    entry.localPosition, point, NavigationCorridorRadius))
            {
                return false;
            }
            bool robust = !geometry.PointBlocked(point, robustRadius) &&
                          (approachDistance <
                           options.routeSampleSpacing * 0.75f ||
                           geometry.IsCorridorClear(
                               entry.localPosition, point, robustRadius));
            bool gap = geometry.TryResolveOpposingBuildingGap(
                point, out Vector3 passageDirection);
            if (gap)
            {
                robust &= geometry.HasHoldingVolume(
                    point, passageDirection, robustRadius);
            }
            // Reachability and firing permission are separate contracts. This
            // line verifies that the sampled point belongs to the player's
            // range ball; the later AnalyzeAnchor pass still proves sustained
            // five-metre LOS and the thin projectile corridor independently.
            if (Vector3.Distance(point, target) >
                MaximumRangedContactDistance + 0.01f)
            {
                return false;
            }
            candidate = CreateFiringVolumeAnchor(
                cell, sampleIndex, point, entry, approachDistance,
                kind, gap, robust);
            return true;
        }

        static void BuildBaseFiringVolumeDirections(List<Vector3> output)
        {
            for (int elevationIndex = 0;
                 elevationIndex < FiringVolumeElevationAngles.Length;
                 elevationIndex++)
            {
                float elevation = FiringVolumeElevationAngles[elevationIndex];
                float horizontal = Mathf.Cos(elevation * Mathf.Deg2Rad);
                float vertical = Mathf.Sin(elevation * Mathf.Deg2Rad);
                for (int azimuth = 0;
                     azimuth < BaseAzimuthSampleCount;
                     azimuth++)
                {
                    float angle = (azimuth + 0.5f) *
                                  360f / BaseAzimuthSampleCount;
                    Vector3 planar = Quaternion.Euler(0f, angle, 0f) *
                                     Vector3.forward;
                    output.Add(new Vector3(
                        planar.x * horizontal,
                        vertical,
                        planar.z * horizontal));
                }
            }
            output.Add(Vector3.up);
            output.Add(Vector3.down);
        }

        static void DeduplicateDirections(List<Vector3> directions)
        {
            var seen = new HashSet<int>();
            int write = 0;
            for (int index = 0; index < directions.Count; index++)
            {
                Vector3 direction = directions[index];
                if (direction.sqrMagnitude < 0.5f)
                    continue;
                direction.Normalize();
                float horizontal = new Vector2(
                    direction.x, direction.z).magnitude;
                float azimuth = Mathf.Repeat(
                    Mathf.Atan2(direction.x, direction.z) *
                    Mathf.Rad2Deg, 360f);
                float elevation = Mathf.Atan2(direction.y,
                    Mathf.Max(0.001f, horizontal)) * Mathf.Rad2Deg;
                int azimuthBin = Mathf.RoundToInt(azimuth / 3f) % 120;
                int elevationBin = Mathf.RoundToInt(
                    (elevation + 90f) / 3f);
                int key = elevationBin * 128 + azimuthBin;
                if (!seen.Add(key))
                    continue;
                directions[write++] = direction;
            }
            if (write < directions.Count)
                directions.RemoveRange(write, directions.Count - write);
        }

        static float MaximumDistanceInsideFiringVolume(
            Vector3 target,
            Vector3 direction,
            float minimumAltitude,
            float maximumAltitude,
            float mapHalfExtent,
            float maximumRadius)
        {
            float distance = Mathf.Clamp(
                maximumRadius, 1f, MaximumRangedContactDistance);
            if (direction.y > 0.0001f)
            {
                distance = Mathf.Min(distance,
                    (maximumAltitude - target.y) / direction.y);
            }
            else if (direction.y < -0.0001f)
            {
                distance = Mathf.Min(distance,
                    (minimumAltitude - target.y) / direction.y);
            }
            float safeHalf = Mathf.Max(32f,
                mapHalfExtent - NavigationCorridorRadius - 1f);
            if (direction.x > 0.0001f)
                distance = Mathf.Min(distance,
                    (safeHalf - target.x) / direction.x);
            else if (direction.x < -0.0001f)
                distance = Mathf.Min(distance,
                    (-safeHalf - target.x) / direction.x);
            if (direction.z > 0.0001f)
                distance = Mathf.Min(distance,
                    (safeHalf - target.z) / direction.z);
            else if (direction.z < -0.0001f)
                distance = Mathf.Min(distance,
                    (-safeHalf - target.z) / direction.z);
            return Mathf.Clamp(distance, 0f,
                maximumRadius);
        }

        static bool IsWithinOperationalAltitude(
            EdpcgGridFireSourceKind kind,
            float altitude,
            float sharedMinimum,
            float maximum)
        {
            float roleMinimum = kind ==
                                EdpcgGridFireSourceKind.Gunship
                ? GunshipMinimumOperationalAltitude
                : StrikerMinimumOperationalAltitude;
            float minimum = Mathf.Max(sharedMinimum, roleMinimum);
            return altitude >= minimum - 0.01f &&
                   altitude <= maximum + 0.01f;
        }

        static void AnalyzeAnchor(
            EdpcgGridFireCell cell,
            List<Vector3> playerSamples,
            bool[] threatenedSamples,
            bool[] roleThreatenedSamples,
            ThreatAnchor anchor,
            GeometryIndex geometry,
            EdpcgAirThreatAnalysisOptions options,
            Func<Vector3, Vector3> projector)
        {
            Vector3 target = cell.localSamplePosition;
            float distance = Vector3.Distance(anchor.localPosition, target);
            int direction = ThreatDirection(target, anchor.localPosition,
                out int azimuth, out int elevation);
            bool aiClear = !geometry.TryFirstBlocker(
                anchor.localPosition, target, NavigationCorridorRadius,
                out string blockerId, out Vector3 blockerPoint,
                out _);
            int exposed = 0;
            int exposedMask = 0;
            for (int index = 0; index < playerSamples.Count; index++)
            {
                if (geometry.IsCorridorClear(anchor.localPosition,
                        playerSamples[index], NavigationCorridorRadius))
                {
                    exposed++;
                    exposedMask |= 1 << index;
                }
            }
            float exposureRatio = playerSamples.Count == 0
                ? 0f
                : exposed / (float)playerSamples.Count;
            float visibleSeconds = aiClear
                ? MeasureContinuousVisibleSeconds(anchor, target, geometry)
                : 0f;
            float requiredVisible = Mathf.Max(0.1f,
                (options.tierSettings?.lineOfSightHysteresisSeconds ?? 0.35f) +
                (options.tierSettings?.rangedTelegraphSeconds ?? 0.65f));
            bool fleeting = aiClear && visibleSeconds + 0.001f < requiredVisible;
            float setupSeconds = anchor.approachSeconds + requiredVisible;
            float projectileSeconds = distance / ProjectileSpeed;
            float firstHitSeconds = setupSeconds + projectileSeconds;
            bool projectileClear = geometry.IsCorridorClear(
                anchor.localPosition, target, ProjectileRadius);

            var line = new EdpcgGridFireLine
            {
                sourceId = anchor.stableId,
                routeId = anchor.routeId,
                sourceKind = anchor.kind,
                sourceWorldPosition = projector(anchor.localPosition),
                playerWorldPosition = cell.worldSamplePosition,
                blockerWorldPosition = aiClear
                    ? Vector3.zero
                    : projector(blockerPoint),
                blockerId = aiClear ? string.Empty : blockerId,
                distance = distance,
                exposureRatio = exposureRatio,
                threatenedSampleMask = exposedMask,
                threatSector = azimuth,
                elevationBand = elevation,
                directionIndex = direction,
                visibleWindowSeconds = visibleSeconds,
                setupSeconds = setupSeconds,
                firstHitSeconds = firstHitSeconds,
                throughBuildingGap = anchor.throughGap,
                fleetingVisibility = fleeting,
                robustReachable = anchor.robustClear,
                incoming = aiClear
            };
            cell.fireLines.Add(line);
            if (!aiClear)
            {
                cell.blockedLineCount++;
                return;
            }

            cell.incomingLineCount++;
            SetDirectionBit(ref cell.potentialDirectionMaskLow,
                ref cell.potentialDirectionMaskMid,
                ref cell.potentialDirectionMaskHigh, azimuth, elevation);
            SetRoleDirectionBit(cell, anchor.kind,
                EdpcgAirThreatViewMode.Potential, azimuth, elevation);
            if (fleeting)
            {
                cell.fleetingWindowCount++;
                return;
            }
            if (!projectileClear)
                return;

            bool authorized = RoleCanReceiveFirePermission(
                anchor.kind, options.tierSettings,
                options.challengeSettings);
            var window = new EdpcgAirFireWindow
            {
                stableId = anchor.stableId,
                routeId = anchor.routeId,
                sourceKind = anchor.kind,
                routeSegmentIndex = anchor.routeSegmentIndex,
                routeSegmentT = anchor.routeSegmentT,
                azimuthSector = azimuth,
                elevationBand = elevation,
                directionIndex = direction,
                routeEntryWorldPosition = projector(
                    anchor.routeEntryLocalPosition),
                firingWorldPosition = projector(anchor.localPosition),
                playerWorldPosition = cell.worldSamplePosition,
                approachWorldPoints = ProjectPoints(
                    anchor.localApproachPoints, projector),
                approachSeconds = anchor.approachSeconds,
                visibleWindowSeconds = visibleSeconds,
                setupSeconds = setupSeconds,
                projectileSeconds = projectileSeconds,
                firstHitSeconds = firstHitSeconds,
                threatenedSampleRatio = exposureRatio,
                threatenedSampleMask = exposedMask,
                throughBuildingGap = anchor.throughGap,
                navigationCorridorClear = anchor.navigationClear,
                robustHullClear = anchor.robustClear,
                aiPermissionCorridorClear = true,
                projectileCorridorClear = true,
                authorizedByTier = authorized
            };
            cell.fireWindows.Add(window);
            if (anchor.throughGap)
                cell.gapWindowCount++;
            cell.fastestSetupSeconds = Mathf.Min(
                cell.fastestSetupSeconds, setupSeconds);
            cell.fastestHitSeconds = Mathf.Min(
                cell.fastestHitSeconds, firstHitSeconds);
            cell.fastestHitSecondsByRole[(int)anchor.kind] = Mathf.Min(
                cell.fastestHitSecondsByRole[(int)anchor.kind],
                firstHitSeconds);
            if (firstHitSeconds <= 4f)
            {
                SetDirectionBit(ref cell.forecastFourDirectionMaskLow,
                    ref cell.forecastFourDirectionMaskMid,
                    ref cell.forecastFourDirectionMaskHigh,
                    azimuth, elevation);
                SetRoleDirectionBit(cell, anchor.kind,
                    EdpcgAirThreatViewMode.WithinFourSeconds,
                    azimuth, elevation);
            }
            if (firstHitSeconds <= 8f)
            {
                SetDirectionBit(ref cell.forecastEightDirectionMaskLow,
                    ref cell.forecastEightDirectionMaskMid,
                    ref cell.forecastEightDirectionMaskHigh,
                    azimuth, elevation);
                SetRoleDirectionBit(cell, anchor.kind,
                    EdpcgAirThreatViewMode.WithinEightSeconds,
                    azimuth, elevation);
            }
            for (int index = 0; index < playerSamples.Count; index++)
            {
                if (geometry.IsCorridorClear(anchor.localPosition,
                        playerSamples[index], NavigationCorridorRadius))
                {
                    threatenedSamples[index] = true;
                    roleThreatenedSamples[index] = true;
                }
            }
        }

        static void SelectAuthorizedWindows(EdpcgGridFireCell cell,
            EdpcgAirThreatAnalysisOptions options)
        {
            cell.fireWindows.Sort((left, right) =>
                left.firstHitSeconds.CompareTo(right.firstHitSeconds));
            for (int index = 0; index < cell.fireWindows.Count; index++)
                cell.fireWindows[index].authorizedByTier = false;
            int directionBudget = ResolveDirectionBudget(options);
            int laneBudget = Mathf.Max(0,
                options.tierSettings?.rangedFireLaneCap ?? 1);
            int tokenBudget = Mathf.Max(0,
                options.tierSettings?.attackTokenCap ?? 1);
            int selectedDirectionsLow = 0;
            int selectedDirectionsMid = 0;
            int selectedDirectionsHigh = 0;
            int directionCount = 0;
            int laneCount = 0;
            int strikerRemaining = Mathf.Max(0,
                options.tierSettings?.strikerCount ?? 0);
            int gunshipRemaining = Mathf.Max(0,
                options.tierSettings?.gunshipCount ?? 0);
            // First admit one representative per horizontal direction. Only
            // after all possible directions are represented may another
            // elevation/window in an existing direction consume spare lanes.
            for (int pass = 0; pass < 2; pass++)
            for (int index = 0; index < cell.fireWindows.Count; index++)
            {
                EdpcgAirFireWindow window = cell.fireWindows[index];
                if (window.authorizedByTier ||
                    !window.robustHullClear ||
                    !window.navigationCorridorClear ||
                    !window.aiPermissionCorridorClear ||
                    !window.projectileCorridorClear ||
                    !RoleCanReceiveFirePermission(
                        window.sourceKind, options.tierSettings,
                        options.challengeSettings))
                {
                    continue;
                }
                int tokenCost = window.sourceKind ==
                                EdpcgGridFireSourceKind.Gunship ? 2 : 1;
                if (laneCount >= laneBudget || tokenCost > tokenBudget)
                    continue;
                int horizontalBit = 1 << window.azimuthSector;
                bool alreadySelected = ((selectedDirectionsLow |
                                         selectedDirectionsMid |
                                         selectedDirectionsHigh) &
                                        horizontalBit) != 0;
                if ((pass == 0 && alreadySelected) ||
                    (pass == 1 && !alreadySelected))
                {
                    continue;
                }
                if (!alreadySelected && directionCount >= directionBudget)
                    continue;
                if (window.sourceKind == EdpcgGridFireSourceKind.Striker)
                {
                    if (strikerRemaining <= 0)
                        continue;
                    strikerRemaining--;
                }
                else
                {
                    if (gunshipRemaining <= 0)
                        continue;
                    gunshipRemaining--;
                }
                window.authorizedByTier = true;
                tokenBudget -= tokenCost;
                laneCount++;
                SetDirectionBit(ref selectedDirectionsLow,
                    ref selectedDirectionsMid,
                    ref selectedDirectionsHigh,
                    window.azimuthSector, window.elevationBand);
                if (!alreadySelected)
                    directionCount++;
                SetRoleDirectionBit(cell, window.sourceKind,
                    EdpcgAirThreatViewMode.AuthorizedByEdpcg,
                    window.azimuthSector, window.elevationBand);
            }
            cell.authorizedDirectionMaskLow = selectedDirectionsLow;
            cell.authorizedDirectionMaskMid = selectedDirectionsMid;
            cell.authorizedDirectionMaskHigh = selectedDirectionsHigh;
        }

        static float MeasureContinuousVisibleSeconds(
            ThreatAnchor anchor,
            Vector3 target,
            GeometryIndex geometry)
        {
            Vector3[] probes = anchor.localWindowProbePoints;
            if (probes == null || probes.Length == 0)
                return 0f;
            float speed = RoleSpeed(anchor.kind);
            float current = 0f;
            float longest = 0f;
            Vector3 previous = probes[0];
            bool previousClear = false;
            for (int index = 0; index < probes.Length; index++)
            {
                Vector3 probe = probes[index];
                float segmentDistance = index == 0
                    ? 0f
                    : Vector3.Distance(previous, probe);
                int steps = index == 0
                    ? 1
                    : Mathf.Max(1, Mathf.CeilToInt(segmentDistance / 6f));
                float sampleSeconds = index == 0
                    ? 0.15f
                    : segmentDistance / steps / Mathf.Max(1f, speed);
                for (int step = 1; step <= steps; step++)
                {
                    Vector3 sample = index == 0
                        ? probe
                        : Vector3.Lerp(previous, probe, step / (float)steps);
                    bool clear = Vector3.Distance(sample, target) <=
                                 MaximumRangedContactDistance &&
                                 geometry.IsCorridorClear(
                                     sample, target,
                                     NavigationCorridorRadius);
                    if (clear)
                    {
                        if (!previousClear)
                            current = sampleSeconds;
                        else
                            current += sampleSeconds;
                        longest = Mathf.Max(longest, current);
                    }
                    else
                    {
                        current = 0f;
                    }
                    previousClear = clear;
                }
                previous = probe;
            }
            return longest;
        }

        static void BuildHorizontalEvasions(
            EdpcgGridFireAnalysis analysis,
            GeometryIndex geometry,
            float hullRadius,
            EdpcgAirThreatAnalysisOptions options)
        {
            for (int cellIndex = 0;
                 cellIndex < analysis.cells.Count;
                 cellIndex++)
            {
                EdpcgGridFireCell cell = analysis.cells[cellIndex];
                if (cell == null || !cell.flyable)
                    continue;
                for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dz == 0)
                        continue;
                    if (!analysis.TryGetCell(cell.gridX + dx,
                            cell.gridZ + dz,
                            out EdpcgGridFireCell target) ||
                        target == null || !target.flyable)
                    {
                        continue;
                    }
                    bool reachable = geometry.IsCorridorClear(
                        cell.localSamplePosition,
                        target.localSamplePosition,
                        hullRadius);
                    float distance = Vector3.Distance(
                        cell.localSamplePosition,
                        target.localSamplePosition);
                    float turnSeconds = options.playerDesignTurnRadius /
                                        options.playerDesignSpeed * 0.35f;
                    float travelSeconds = distance /
                                          options.playerDesignSpeed +
                                          turnSeconds;
                    float reduction = cell.pressureScore -
                                      target.pressureScore;
                    float hitTime = float.IsPositiveInfinity(
                        cell.authorizedFastestHitSeconds)
                        ? 9f
                        : cell.authorizedFastestHitSeconds;
                    float margin = hitTime - travelSeconds;
                    bool recommended = reachable && margin >= 0.2f &&
                                       (reduction >= 0.06f ||
                                        target.authorizedDirectionCount <
                                        cell.authorizedDirectionCount) &&
                                       !target.crossfire;
                    cell.evasions.Add(new EdpcgGridEvasionLink
                    {
                        targetCellId = target.stableId,
                        targetGridX = target.gridX,
                        targetGridZ = target.gridZ,
                        targetAltitudeLayer = analysis.altitudeLayer,
                        startWorldPosition = cell.worldSamplePosition,
                        endWorldPosition = target.worldSamplePosition,
                        worldPathPoints = new[]
                        {
                            cell.worldSamplePosition,
                            Vector3.Lerp(cell.worldSamplePosition,
                                target.worldSamplePosition, 0.5f),
                            target.worldSamplePosition
                        },
                        removedThreatSectorMask = cell.threatSectorMask &
                                                  ~target.threatSectorMask,
                        pressureReduction = reduction,
                        travelSeconds = travelSeconds,
                        escapeMarginSeconds = margin,
                        reachable = reachable,
                        recommended = recommended
                    });
                    if (recommended)
                    {
                        cell.safeExitCount++;
                        cell.fastestEscapeSeconds = Mathf.Min(
                            cell.fastestEscapeSeconds, travelSeconds);
                        cell.bestEscapeMarginSeconds = Mathf.Max(
                            cell.bestEscapeMarginSeconds, margin);
                    }
                }
            }
        }

        static void AddVerticalPair(
            EdpcgGridFireAnalysis first,
            EdpcgGridFireAnalysis second,
            GeometryIndex geometry,
            EdpcgAirThreatAnalysisOptions options)
        {
            if (first == null || second == null ||
                !first.IsUsable || !second.IsUsable)
            {
                return;
            }
            float hullRadius = Mathf.Max(NavigationCorridorRadius,
                Mathf.Max(first.referenceShipWidth,
                    second.referenceShipWidth) * 0.5f);
            for (int index = 0; index < first.cells.Count; index++)
            {
                EdpcgGridFireCell source = first.cells[index];
                if (source == null || !source.flyable ||
                    !second.TryGetCell(source.gridX, source.gridZ,
                        out EdpcgGridFireCell target) ||
                    target == null || !target.flyable)
                {
                    continue;
                }
                AddVerticalLink(source, target, first.altitudeLayer,
                    second.altitudeLayer, geometry, hullRadius, options);
                AddVerticalLink(target, source, second.altitudeLayer,
                    first.altitudeLayer, geometry, hullRadius, options);
            }
        }

        static void AddVerticalLink(
            EdpcgGridFireCell source,
            EdpcgGridFireCell target,
            EdpcgFireAnalysisAltitudeLayer sourceLayer,
            EdpcgFireAnalysisAltitudeLayer targetLayer,
            GeometryIndex geometry,
            float hullRadius,
            EdpcgAirThreatAnalysisOptions options)
        {
            bool reachable = geometry.IsCorridorClear(
                source.localSamplePosition, target.localSamplePosition,
                hullRadius);
            float height = Mathf.Abs(target.localSamplePosition.y -
                                     source.localSamplePosition.y);
            float travelSeconds = height / options.playerDesignClimbSpeed +
                                  0.25f;
            float reduction = source.pressureScore - target.pressureScore;
            float hitTime = float.IsPositiveInfinity(
                source.authorizedFastestHitSeconds)
                ? 9f
                : source.authorizedFastestHitSeconds;
            float margin = hitTime - travelSeconds;
            bool recommended = reachable && margin >= 0.2f &&
                               (reduction >= 0.04f ||
                                target.authorizedDirectionCount <
                                source.authorizedDirectionCount) &&
                               !target.crossfire;
            source.evasions.Add(new EdpcgGridEvasionLink
            {
                targetCellId = target.stableId,
                targetGridX = target.gridX,
                targetGridZ = target.gridZ,
                targetAltitudeLayer = targetLayer,
                startWorldPosition = source.worldSamplePosition,
                endWorldPosition = target.worldSamplePosition,
                worldPathPoints = new[]
                {
                    source.worldSamplePosition,
                    Vector3.Lerp(source.worldSamplePosition,
                        target.worldSamplePosition, 0.5f),
                    target.worldSamplePosition
                },
                removedThreatSectorMask = source.threatSectorMask &
                                          ~target.threatSectorMask,
                pressureReduction = reduction,
                travelSeconds = travelSeconds,
                escapeMarginSeconds = margin,
                verticalTransfer = true,
                reachable = reachable,
                recommended = recommended
            });
        }

        static void RemoveVerticalLinks(EdpcgGridFireAnalysis analysis)
        {
            if (analysis == null)
                return;
            for (int index = 0; index < analysis.cells.Count; index++)
            {
                EdpcgGridFireCell cell = analysis.cells[index];
                if (cell == null)
                    continue;
                cell.evasions.RemoveAll(link =>
                    link != null && link.verticalTransfer);
            }
        }

        static void RecountEvasions(EdpcgGridFireAnalysis analysis)
        {
            if (analysis == null)
                return;
            for (int index = 0; index < analysis.cells.Count; index++)
            {
                EdpcgGridFireCell cell = analysis.cells[index];
                if (cell == null)
                    continue;
                cell.safeExitCount = 0;
                cell.fastestEscapeSeconds = float.PositiveInfinity;
                cell.bestEscapeMarginSeconds = float.NegativeInfinity;
                for (int linkIndex = 0;
                     linkIndex < cell.evasions.Count;
                     linkIndex++)
                {
                    EdpcgGridEvasionLink link = cell.evasions[linkIndex];
                    if (link == null || !link.recommended)
                        continue;
                    cell.safeExitCount++;
                    cell.fastestEscapeSeconds = Mathf.Min(
                        cell.fastestEscapeSeconds, link.travelSeconds);
                    cell.bestEscapeMarginSeconds = Mathf.Max(
                        cell.bestEscapeMarginSeconds,
                        link.escapeMarginSeconds);
                }
            }
        }

        static List<Vector3> BuildPlayerSamples(
            Bounds bounds,
            float altitude,
            float hullRadius,
            GeometryIndex geometry,
            out Vector3 representative)
        {
            var samples = new List<Vector3>(9);
            representative = new Vector3(
                bounds.center.x, altitude, bounds.center.z);
            float bestDistance = float.PositiveInfinity;
            for (int x = 0; x < CellSampleOffsets.Length; x++)
            for (int z = 0; z < CellSampleOffsets.Length; z++)
            {
                Vector3 point = new Vector3(
                    bounds.center.x + bounds.size.x * CellSampleOffsets[x],
                    altitude,
                    bounds.center.z + bounds.size.z * CellSampleOffsets[z]);
                if (geometry.PointBlocked(point, hullRadius))
                    continue;
                samples.Add(point);
                float distance = (point - bounds.center).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    representative = point;
                }
            }
            return samples;
        }

        static void InsertShortlist(
            ThreatAnchor[] anchors,
            float[] costs,
            int groupStart,
            int groupSize,
            ThreatAnchor candidate,
            float cost)
        {
            for (int offset = 0;
                 offset < groupSize;
                 offset++)
            {
                int index = groupStart + offset;
                if (cost >= costs[index])
                    continue;
                for (int shift = groupSize - 1;
                     shift > offset;
                     shift--)
                {
                    anchors[groupStart + shift] =
                        anchors[groupStart + shift - 1];
                    costs[groupStart + shift] =
                        costs[groupStart + shift - 1];
                }
                anchors[index] = candidate;
                costs[index] = cost;
                return;
            }
        }

        static bool SupportsOrdinaryRangedRole(AirCombatRouteKind kind)
        {
            return kind == AirCombatRouteKind.Main ||
                   kind == AirCombatRouteKind.MaskedFlank ||
                   kind == AirCombatRouteKind.LongRange ||
                   kind == AirCombatRouteKind.VerticalEscape;
        }

        static bool RoleCanReceiveFirePermission(
            EdpcgGridFireSourceKind kind,
            EdpcgTierSettings tier,
            EdpcgCityTacticalChallengeSettings challenge = null)
        {
            if (tier == null || tier.rangedFireLaneCap <= 0)
                return false;
            if (kind == EdpcgGridFireSourceKind.Striker)
                return tier.strikerCount > 0 && tier.attackTokenCap >= 1 &&
                       (challenge == null ||
                        challenge.allowStrikerReposition);
            return tier.gunshipCount > 0 && tier.attackTokenCap >= 2 &&
                   (challenge == null || challenge.allowGunshipReposition);
        }

        static int ResolveDirectionBudget(
            EdpcgAirThreatAnalysisOptions options)
        {
            int tierBudget = options.tierSettings?.pressureDirectionCap ?? 1;
            int laneBudget = options.tierSettings?.rangedFireLaneCap ?? 1;
            int challengeBudget =
                options.challengeSettings?.maximumPressureDirections ??
                tierBudget;
            return Mathf.Clamp(Mathf.Min(tierBudget,
                Mathf.Min(laneBudget, challengeBudget)), 1, 3);
        }

        static float ResolvePlayerAltitude(
            AirCombatCitySettings settings,
            EdpcgFireAnalysisAltitudeLayer layer)
        {
            if (settings == null)
                return 110f;
            switch (layer)
            {
                case EdpcgFireAnalysisAltitudeLayer.Low:
                    return settings.lowAltitude;
                case EdpcgFireAnalysisAltitudeLayer.High:
                    return Mathf.Min(settings.highAltitude,
                        settings.maximumAltitude - 4f);
                default:
                    return settings.mediumAltitude;
            }
        }

        static int ThreatDirection(Vector3 player, Vector3 source,
            out int azimuth, out int elevation)
        {
            Vector3 direction = source - player;
            azimuth = EdpcgThreatDirectionUtility.HorizontalSector(
                player, source);
            float horizontal = new Vector2(direction.x, direction.z).magnitude;
            float elevationAngle = Mathf.Atan2(direction.y,
                Mathf.Max(0.01f, horizontal)) * Mathf.Rad2Deg;
            elevation = elevationAngle > 15f ? 1 :
                elevationAngle < -15f ? -1 : 0;
            return (elevation + 1) * 8 + azimuth;
        }

        static void SetDirectionBit(ref int low, ref int mid, ref int high,
            int azimuth, int elevation)
        {
            int bit = 1 << Mathf.Clamp(azimuth, 0, 7);
            if (elevation < 0)
                low |= bit;
            else if (elevation > 0)
                high |= bit;
            else
                mid |= bit;
        }

        static void SetRoleDirectionBit(
            EdpcgGridFireCell cell,
            EdpcgGridFireSourceKind kind,
            EdpcgAirThreatViewMode mode,
            int azimuth,
            int elevation)
        {
            bool striker = kind == EdpcgGridFireSourceKind.Striker;
            if (mode == EdpcgAirThreatViewMode.Potential)
            {
                if (striker)
                    SetDirectionBit(
                        ref cell.strikerPotentialDirectionMaskLow,
                        ref cell.strikerPotentialDirectionMaskMid,
                        ref cell.strikerPotentialDirectionMaskHigh,
                        azimuth, elevation);
                else
                    SetDirectionBit(
                        ref cell.gunshipPotentialDirectionMaskLow,
                        ref cell.gunshipPotentialDirectionMaskMid,
                        ref cell.gunshipPotentialDirectionMaskHigh,
                        azimuth, elevation);
                return;
            }
            if (mode == EdpcgAirThreatViewMode.WithinFourSeconds)
            {
                if (striker)
                    SetDirectionBit(
                        ref cell.strikerForecastFourDirectionMaskLow,
                        ref cell.strikerForecastFourDirectionMaskMid,
                        ref cell.strikerForecastFourDirectionMaskHigh,
                        azimuth, elevation);
                else
                    SetDirectionBit(
                        ref cell.gunshipForecastFourDirectionMaskLow,
                        ref cell.gunshipForecastFourDirectionMaskMid,
                        ref cell.gunshipForecastFourDirectionMaskHigh,
                        azimuth, elevation);
                return;
            }
            if (mode == EdpcgAirThreatViewMode.WithinEightSeconds)
            {
                if (striker)
                    SetDirectionBit(
                        ref cell.strikerForecastEightDirectionMaskLow,
                        ref cell.strikerForecastEightDirectionMaskMid,
                        ref cell.strikerForecastEightDirectionMaskHigh,
                        azimuth, elevation);
                else
                    SetDirectionBit(
                        ref cell.gunshipForecastEightDirectionMaskLow,
                        ref cell.gunshipForecastEightDirectionMaskMid,
                        ref cell.gunshipForecastEightDirectionMaskHigh,
                        azimuth, elevation);
                return;
            }
            if (striker)
                SetDirectionBit(
                    ref cell.strikerAuthorizedDirectionMaskLow,
                    ref cell.strikerAuthorizedDirectionMaskMid,
                    ref cell.strikerAuthorizedDirectionMaskHigh,
                    azimuth, elevation);
            else
                SetDirectionBit(
                    ref cell.gunshipAuthorizedDirectionMaskLow,
                    ref cell.gunshipAuthorizedDirectionMaskMid,
                    ref cell.gunshipAuthorizedDirectionMaskHigh,
                    azimuth, elevation);
        }

        static bool HasDirectionBit(int low, int mid, int high,
            int azimuth, int elevation)
        {
            int bit = 1 << Mathf.Clamp(azimuth, 0, 7);
            return elevation < 0 ? (low & bit) != 0 :
                elevation > 0 ? (high & bit) != 0 : (mid & bit) != 0;
        }

        static int CountDirectionMasks(int low, int mid, int high)
        {
            return CountBits(low) + CountBits(mid) + CountBits(high);
        }

        static int HorizontalMask(int low, int mid, int high)
        {
            return low | mid | high;
        }

        static bool HasSeparatedDirections(
            List<EdpcgAirFireWindow> windows,
            bool authorizedOnly)
        {
            for (int first = 0; first < windows.Count; first++)
            {
                EdpcgAirFireWindow left = windows[first];
                if (authorizedOnly && !left.authorizedByTier)
                    continue;
                for (int second = first + 1;
                     second < windows.Count;
                     second++)
                {
                    EdpcgAirFireWindow right = windows[second];
                    if (authorizedOnly && !right.authorizedByTier)
                        continue;
                    int separation = Mathf.Abs(
                        left.azimuthSector - right.azimuthSector);
                    separation = Mathf.Min(separation, 8 - separation);
                    bool verticalSeparation = left.elevationBand !=
                                              right.elevationBand;
                    float leftFireStart = left.setupSeconds;
                    float leftFireEnd = left.approachSeconds +
                                        left.visibleWindowSeconds;
                    float rightFireStart = right.setupSeconds;
                    float rightFireEnd = right.approachSeconds +
                                         right.visibleWindowSeconds;
                    float overlapSeconds = Mathf.Min(
                        leftFireEnd, rightFireEnd) - Mathf.Max(
                        leftFireStart, rightFireStart);
                    bool independentChannel = !string.Equals(
                        left.stableId, right.stableId,
                        StringComparison.Ordinal) &&
                        (!string.Equals(left.routeId, right.routeId,
                            StringComparison.Ordinal) ||
                         Vector3.Distance(left.firingWorldPosition,
                             right.firingWorldPosition) >= 24f);
                    if (independentChannel && overlapSeconds >= 0.25f &&
                        (separation >= 2 ||
                         (separation >= 1 && verticalSeparation)))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        static int CountBits(int value)
        {
            int count = 0;
            while (value != 0)
            {
                count += value & 1;
                value >>= 1;
            }
            return count;
        }

        static float RoleSpeed(EdpcgGridFireSourceKind kind)
        {
            return kind == EdpcgGridFireSourceKind.Striker ? 58f : 48f;
        }

        static float RoleAcceleration(EdpcgGridFireSourceKind kind)
        {
            return kind == EdpcgGridFireSourceKind.Striker ? 14f : 10f;
        }

        static float RoleRobustRadius(EdpcgGridFireSourceKind kind)
        {
            // Conservative yaw-independent horizontal sweep radius derived
            // from the current 7x2.5x10 box collider and role scale.
            return kind == EdpcgGridFireSourceKind.Striker ? 6.6f : 7.65f;
        }

        static float TravelSeconds(float distance, float speed,
            float acceleration)
        {
            distance = Mathf.Max(0f, distance);
            speed = Mathf.Max(1f, speed);
            acceleration = Mathf.Max(0.1f, acceleration);
            float accelerateDistance = speed * speed / (2f * acceleration);
            if (distance <= accelerateDistance)
                return Mathf.Sqrt(2f * distance / acceleration);
            return speed / acceleration +
                   (distance - accelerateDistance) / speed;
        }

        static Vector3[] ProjectPoints(Vector3[] localPoints,
            Func<Vector3, Vector3> projector)
        {
            if (localPoints == null || localPoints.Length == 0)
                return Array.Empty<Vector3>();
            var world = new Vector3[localPoints.Length];
            for (int index = 0; index < localPoints.Length; index++)
                world[index] = projector(localPoints[index]);
            return world;
        }

        static Vector3[] BuildWorldCorners(Bounds bounds, float altitude,
            Func<Vector3, Vector3> projector)
        {
            return new[]
            {
                projector(new Vector3(bounds.min.x, altitude, bounds.min.z)),
                projector(new Vector3(bounds.max.x, altitude, bounds.min.z)),
                projector(new Vector3(bounds.max.x, altitude, bounds.max.z)),
                projector(new Vector3(bounds.min.x, altitude, bounds.max.z))
            };
        }

        static Vector3 ClosestPointOnSegment(Vector3 start, Vector3 end,
            Vector3 point)
        {
            Vector3 segment = end - start;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= Mathf.Epsilon)
                return start;
            float amount = Mathf.Clamp01(
                Vector3.Dot(point - start, segment) / lengthSquared);
            return start + segment * amount;
        }

        static void ClosestSegmentParameters(
            Vector3 firstStart,
            Vector3 firstEnd,
            Vector3 secondStart,
            Vector3 secondEnd,
            out float firstT,
            out float secondT,
            out float sqrDistance)
        {
            Vector3 first = firstEnd - firstStart;
            Vector3 second = secondEnd - secondStart;
            Vector3 offset = firstStart - secondStart;
            float firstLength = Vector3.Dot(first, first);
            float secondLength = Vector3.Dot(second, second);
            float cross = Vector3.Dot(second, offset);
            if (firstLength <= Mathf.Epsilon && secondLength <= Mathf.Epsilon)
            {
                firstT = 0f;
                secondT = 0f;
                sqrDistance = offset.sqrMagnitude;
                return;
            }
            if (firstLength <= Mathf.Epsilon)
            {
                firstT = 0f;
                secondT = Mathf.Clamp01(cross / secondLength);
            }
            else
            {
                float firstOffset = Vector3.Dot(first, offset);
                if (secondLength <= Mathf.Epsilon)
                {
                    secondT = 0f;
                    firstT = Mathf.Clamp01(-firstOffset / firstLength);
                }
                else
                {
                    float coupling = Vector3.Dot(first, second);
                    float denominator = firstLength * secondLength -
                                        coupling * coupling;
                    firstT = denominator != 0f
                        ? Mathf.Clamp01((coupling * cross -
                                         firstOffset * secondLength) /
                                        denominator)
                        : 0f;
                    secondT = (coupling * firstT + cross) / secondLength;
                    if (secondT < 0f)
                    {
                        secondT = 0f;
                        firstT = Mathf.Clamp01(-firstOffset / firstLength);
                    }
                    else if (secondT > 1f)
                    {
                        secondT = 1f;
                        firstT = Mathf.Clamp01(
                            (coupling - firstOffset) / firstLength);
                    }
                }
            }
            Vector3 firstPoint = firstStart + first * firstT;
            Vector3 secondPoint = secondStart + second * secondT;
            sqrDistance = (firstPoint - secondPoint).sqrMagnitude;
        }
    }
}
