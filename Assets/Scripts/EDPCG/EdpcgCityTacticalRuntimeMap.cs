using System;
using System.Collections.Generic;
using UnityEngine;
using UnityPlanet.CityPcg;
using UnityPlanet.ModularAssembly;

namespace UnityPlanet.EDPCG
{
    public enum EdpcgTacticalArrowKind
    {
        Movement = 0,
        VerifiedBlockedFireLine = 1,
        ExpectedFireExchange = 2,
        TrapApproach = 3,
        VerticalTransition = 4,
        SpawnDeparture = 5
    }

    public sealed class EdpcgRuntimeTacticalArrow
    {
        public string StableId = string.Empty;
        public string AreaId = string.Empty;
        public EdpcgTacticalArrowKind Kind;
        public Vector3 Start;
        public Vector3 End;
        public Vector3 Marker;
        public bool Bidirectional;
        public bool Verified;
    }

    public sealed class EdpcgRuntimeIngress
    {
        public string StableId;
        public Vector3 Position;
        public Vector3 Target;
        public HordeEnemyAttackKind AttackKind;
        public float WarningSeconds;
    }

    public sealed class EdpcgRuntimeArea
    {
        public string StableId;
        public EdpcgTacticalAreaKind Kind;
        public Bounds Bounds;
        public Vector3[] Entrances = Array.Empty<Vector3>();
        public Vector3[] Exits = Array.Empty<Vector3>();
        public string[] Connections = Array.Empty<string>();
        public float ProjectedPressureUpperBound;
        public float SafeWindowSeconds;
        public float Risk;
        public float Legibility;
    }

    public sealed class EdpcgRuntimeRoute
    {
        public string StableId;
        public AirCombatRouteKind SourceKind;
        public Vector3[] Points = Array.Empty<Vector3>();
        public float Width;
        public int Capacity;
        public float EstimatedTravelSeconds;
        public string FallbackRouteId = string.Empty;
        public bool AllowReverse = true;
        public bool IsEnvironmentalTrap;
        public UrbanEnvironmentalPursuitRouteDescriptor EnvironmentalTrap;
    }

    public sealed class EdpcgCityTacticalRuntimeMap
    {
        readonly List<EdpcgRuntimeIngress> ingresses =
            new List<EdpcgRuntimeIngress>(8);
        readonly List<EdpcgRuntimeArea> areas =
            new List<EdpcgRuntimeArea>(16);
        readonly List<EdpcgRuntimeRoute> routes =
            new List<EdpcgRuntimeRoute>(16);
        readonly List<EdpcgRuntimeTacticalArrow> tacticalArrows =
            new List<EdpcgRuntimeTacticalArrow>(32);
        readonly Dictionary<string, EdpcgRuntimeArea> areaById =
            new Dictionary<string, EdpcgRuntimeArea>(StringComparer.Ordinal);
        readonly Dictionary<string, EdpcgRuntimeRoute> routeById =
            new Dictionary<string, EdpcgRuntimeRoute>(StringComparer.Ordinal);

        public IReadOnlyList<EdpcgRuntimeIngress> Ingresses => ingresses;
        public IReadOnlyList<EdpcgRuntimeArea> Areas => areas;
        public IReadOnlyList<EdpcgRuntimeRoute> Routes => routes;
        public IReadOnlyList<EdpcgRuntimeTacticalArrow> TacticalArrows =>
            tacticalArrows;
        public bool HasCityData => areas.Count > 0 || routes.Count > 0;

        public static EdpcgCityTacticalRuntimeMap Build(
            FinitePlanetUrbanCombatRuntime urban)
        {
            if (urban == null)
                return new EdpcgCityTacticalRuntimeMap();
            return Build(
                urban.Plan,
                planPosition => urban.ProjectPlanPosition(planPosition) +
                                Vector3.up * planPosition.y,
                urban.EnvironmentalFields);
        }

        /// <summary>
        /// Builds the same read-only tactical map for the isolated city PCG lab.
        /// This is editor-preview support only; no EDPCG session or gameplay
        /// component is created.
        /// </summary>
        public static EdpcgCityTacticalRuntimeMap Build(AirCombatCityPcgLab lab)
        {
            if (lab == null)
                return new EdpcgCityTacticalRuntimeMap();
            return Build(
                lab.Plan,
                planPosition => lab.transform.TransformPoint(planPosition),
                null);
        }

        static EdpcgCityTacticalRuntimeMap Build(
            AirCombatCityPlan plan,
            Func<Vector3, Vector3> projectAirPoint,
            UrbanEnvironmentalFieldDirector environmental)
        {
            var result = new EdpcgCityTacticalRuntimeMap();
            if (plan == null || projectAirPoint == null)
                return result;

            for (int index = 0; index < plan.ingresses.Count; index++)
            {
                AirCombatEnemyIngress source = plan.ingresses[index];
                if (source == null)
                    continue;
                result.ingresses.Add(new EdpcgRuntimeIngress
                {
                    StableId = source.stableId ?? string.Empty,
                    Position = projectAirPoint(source.position),
                    Target = projectAirPoint(source.target),
                    AttackKind = source.kind == AirCombatEnemyLaneKind.Suicide
                        ? HordeEnemyAttackKind.Suicide
                        : HordeEnemyAttackKind.Ranged,
                    WarningSeconds = Mathf.Max(0f, source.warningSeconds)
                });
            }

            for (int index = 0; index < plan.opportunities.Count; index++)
            {
                TacticalOpportunity source = plan.opportunities[index];
                if (source == null ||
                    !TryMapAreaKind(source.kind, out EdpcgTacticalAreaKind kind))
                {
                    continue;
                }
                EdpcgRuntimeArea area = BuildArea(projectAirPoint, source, kind);
                result.areas.Add(area);
                if (!string.IsNullOrEmpty(area.StableId))
                    result.areaById[area.StableId] = area;
            }

            // SpawnBasin is a tactical volume instead of an opportunity in the
            // existing city plan, so expose it explicitly to the Director.
            for (int index = 0; index < plan.volumes.Count; index++)
            {
                AirCombatTacticalVolume volume = plan.volumes[index];
                if (volume == null || volume.kind != AirCombatVolumeKind.SpawnBasin)
                    continue;
                Vector3 center = projectAirPoint(volume.center);
                var area = new EdpcgRuntimeArea
                {
                    StableId = volume.stableId ?? "area.spawn-basin",
                    Kind = EdpcgTacticalAreaKind.SpawnSafeAirspace,
                    Bounds = new Bounds(center, volume.size),
                    ProjectedPressureUpperBound = 0.03f,
                    Risk = 0f,
                    Legibility = 1f
                };
                result.areas.Add(area);
                result.areaById[area.StableId] = area;
            }

            for (int index = 0; index < plan.routes.Count; index++)
            {
                AirCombatFlightRoute source = plan.routes[index];
                if (source == null || source.points == null ||
                    source.points.Length < 2)
                {
                    continue;
                }
                Vector3[] points = new Vector3[source.points.Length];
                float length = 0f;
                for (int pointIndex = 0;
                     pointIndex < source.points.Length;
                     pointIndex++)
                {
                    points[pointIndex] = projectAirPoint(source.points[pointIndex]);
                    if (pointIndex > 0)
                        length += Vector3.Distance(points[pointIndex - 1], points[pointIndex]);
                }
                var route = new EdpcgRuntimeRoute
                {
                    StableId = source.stableId ?? string.Empty,
                    SourceKind = source.kind,
                    Points = points,
                    Width = Mathf.Max(1f, source.width),
                    Capacity = Mathf.Clamp(
                        Mathf.FloorToInt(Mathf.Max(1f, source.width) / 24f),
                        1,
                        6),
                    EstimatedTravelSeconds = length / 55f
                };
                result.routes.Add(route);
                if (!string.IsNullOrEmpty(route.StableId))
                    result.routeById[route.StableId] = route;
            }

            if (environmental != null)
            {
                IReadOnlyList<UrbanEnvironmentalPursuitRouteDescriptor>
                    trapRoutes = environmental.PursuitRoutes;
                for (int index = 0; index < trapRoutes.Count; index++)
                {
                    UrbanEnvironmentalPursuitRouteDescriptor source =
                        trapRoutes[index];
                    if (source == null || !source.IsUsable)
                        continue;
                    float length = 0f;
                    for (int point = 1;
                         point < source.worldWaypoints.Length;
                         point++)
                    {
                        length += Vector3.Distance(
                            source.worldWaypoints[point - 1],
                            source.worldWaypoints[point]);
                    }
                    var route = new EdpcgRuntimeRoute
                    {
                        StableId = source.stableId,
                        SourceKind = AirCombatRouteKind.MaskedFlank,
                        Points = (Vector3[])source.worldWaypoints.Clone(),
                        Width = Mathf.Max(1f, source.localApproachSize.x),
                        Capacity = Mathf.Max(1, source.capacity),
                        EstimatedTravelSeconds = Mathf.Max(0.5f, length / 55f),
                        AllowReverse = source.allowReverse,
                        IsEnvironmentalTrap = true,
                        EnvironmentalTrap = source
                    };
                    result.routes.Add(route);
                    result.routeById[route.StableId] = route;
                }
            }

            result.AssignFallbackRoutes();
            result.BuildTacticalArrows(projectAirPoint, plan);
            return result;
        }

        void BuildTacticalArrows(
            Func<Vector3, Vector3> projectAirPoint,
            AirCombatCityPlan plan)
        {
            tacticalArrows.Clear();
            if (projectAirPoint == null || plan == null)
                return;

            for (int index = 0; index < areas.Count; index++)
            {
                EdpcgRuntimeArea area = areas[index];
                switch (area.Kind)
                {
                    case EdpcgTacticalAreaKind.SpawnSafeAirspace:
                        AddSpawnDepartureArrows(area);
                        break;
                    case EdpcgTacticalAreaKind.CentralManeuverDistrict:
                        AddInboundArrows(
                            area,
                            EdpcgTacticalArrowKind.ExpectedFireExchange,
                            4);
                        break;
                    case EdpcgTacticalAreaKind.HighRiseOcclusionChain:
                        AddMovementArrows(area, 2);
                        AddVerifiedOcclusionFireLines(
                            projectAirPoint, plan, area);
                        break;
                    case EdpcgTacticalAreaKind.ExposedFireShortcut:
                        AddRouteArrow(
                            area,
                            AirCombatRouteKind.LongRange,
                            EdpcgTacticalArrowKind.ExpectedFireExchange,
                            true);
                        break;
                    case EdpcgTacticalAreaKind.MagneticCourtyard:
                        AddInboundArrows(
                            area,
                            EdpcgTacticalArrowKind.TrapApproach,
                            2);
                        break;
                    case EdpcgTacticalAreaKind.LowMidVerticalTransition:
                        AddRouteArrow(
                            area,
                            AirCombatRouteKind.VerticalEscape,
                            EdpcgTacticalArrowKind.VerticalTransition,
                            false);
                        break;
                }
            }
        }

        void AddSpawnDepartureArrows(EdpcgRuntimeArea area)
        {
            float radius = Mathf.Max(
                25f,
                Mathf.Min(area.Bounds.extents.x, area.Bounds.extents.z) * 0.55f);
            Vector3[] directions =
            {
                Vector3.forward,
                Vector3.right,
                Vector3.back,
                Vector3.left
            };
            for (int index = 0; index < directions.Length; index++)
            {
                Vector3 start = area.Bounds.center + directions[index] * radius * 0.2f;
                AddTacticalArrow(area, EdpcgTacticalArrowKind.SpawnDeparture,
                    start, area.Bounds.center + directions[index] * radius,
                    Vector3.zero, false, true, "spawn-" + index);
            }
        }

        void AddInboundArrows(
            EdpcgRuntimeArea area,
            EdpcgTacticalArrowKind kind,
            int maximumCount)
        {
            Vector3[] sources = area.Entrances != null &&
                                area.Entrances.Length > 0
                ? area.Entrances
                : area.Exits;
            if (sources == null || sources.Length == 0)
            {
                AddTacticalArrow(area, kind,
                    area.Bounds.center - Vector3.forward * area.Bounds.extents.z,
                    area.Bounds.center, Vector3.zero, false, true, "inbound-fallback");
                return;
            }
            int count = Mathf.Min(maximumCount, sources.Length);
            for (int index = 0; index < count; index++)
            {
                AddTacticalArrow(area, kind, sources[index], area.Bounds.center,
                    Vector3.zero, false, true, "inbound-" + index);
            }
        }

        void AddMovementArrows(EdpcgRuntimeArea area, int maximumCount)
        {
            if (area.Entrances == null || area.Entrances.Length == 0 ||
                area.Exits == null || area.Exits.Length == 0)
            {
                return;
            }
            int count = Mathf.Min(maximumCount,
                Mathf.Min(area.Entrances.Length, area.Exits.Length));
            for (int index = 0; index < count; index++)
            {
                AddTacticalArrow(area, EdpcgTacticalArrowKind.Movement,
                    area.Entrances[index], area.Exits[index], Vector3.zero,
                    false, true, "movement-" + index);
            }
        }

        void AddRouteArrow(
            EdpcgRuntimeArea area,
            AirCombatRouteKind routeKind,
            EdpcgTacticalArrowKind arrowKind,
            bool bidirectional)
        {
            EdpcgRuntimeRoute best = null;
            float bestDistance = float.PositiveInfinity;
            for (int index = 0; index < routes.Count; index++)
            {
                EdpcgRuntimeRoute route = routes[index];
                if (route == null || route.IsEnvironmentalTrap ||
                    route.SourceKind != routeKind || route.Points == null ||
                    route.Points.Length < 2)
                {
                    continue;
                }
                for (int point = 0; point < route.Points.Length; point++)
                {
                    float distance = area.Bounds.SqrDistance(route.Points[point]);
                    if (distance >= bestDistance)
                        continue;
                    best = route;
                    bestDistance = distance;
                }
            }
            if (best == null)
            {
                AddInboundArrows(area, arrowKind, 2);
                return;
            }
            int startIndex = best.Points.Length >= 4 ? 2 : 0;
            int endIndex = best.Points.Length >= 4
                ? best.Points.Length - 3
                : best.Points.Length - 1;
            if (startIndex >= endIndex)
            {
                startIndex = 0;
                endIndex = best.Points.Length - 1;
            }
            AddTacticalArrow(area, arrowKind, best.Points[startIndex],
                best.Points[endIndex], Vector3.zero, bidirectional, true,
                "route-" + routeKind);
        }

        void AddVerifiedOcclusionFireLines(
            Func<Vector3, Vector3> projectAirPoint,
            AirCombatCityPlan plan,
            EdpcgRuntimeArea area)
        {
            AirCombatFlightRoute route = null;
            for (int index = 0; index < plan.routes.Count; index++)
            {
                if (plan.routes[index] != null &&
                    plan.routes[index].stableId == "route.masked-flank")
                {
                    route = plan.routes[index];
                    break;
                }
            }
            if (route == null || route.points == null || route.points.Length < 4)
                return;

            Vector2 threat = new Vector2(plan.objective.x, plan.objective.z);
            int added = 0;
            const int Samples = 12;
            for (int sample = 0; sample < Samples && added < 3; sample++)
            {
                float t = (sample + 0.5f) / Samples;
                Vector3 routePoint = Vector3.Lerp(
                    route.points[2], route.points[3], t);
                Vector2 origin = new Vector2(routePoint.x, routePoint.z);
                for (int buildingIndex = 0;
                     buildingIndex < plan.buildings.Count;
                     buildingIndex++)
                {
                    AirCombatBuildingLot building = plan.buildings[buildingIndex];
                    float top = building.center.y + building.size.y * 0.5f;
                    if (building.clusterId != 1202 || top < routePoint.y + 8f ||
                        !AirCombatCityGenerator.FootprintIntersectsCorridor(
                            new Vector2(building.center.x, building.center.z),
                            new Vector2(building.size.x, building.size.z),
                            building.yaw, origin, threat, 1f, out _))
                    {
                        continue;
                    }

                    Vector3 localMarker = new Vector3(
                        building.center.x,
                        routePoint.y,
                        building.center.z);
                    AddTacticalArrow(
                        area,
                        EdpcgTacticalArrowKind.VerifiedBlockedFireLine,
                        projectAirPoint(routePoint),
                        projectAirPoint(new Vector3(
                            plan.objective.x,
                            routePoint.y,
                            plan.objective.z)),
                        projectAirPoint(localMarker),
                        false,
                        true,
                        "blocked-" + sample);
                    added++;
                    break;
                }
            }
        }

        void AddTacticalArrow(
            EdpcgRuntimeArea area,
            EdpcgTacticalArrowKind kind,
            Vector3 start,
            Vector3 end,
            Vector3 marker,
            bool bidirectional,
            bool verified,
            string suffix)
        {
            if ((end - start).sqrMagnitude < 1f)
                return;
            tacticalArrows.Add(new EdpcgRuntimeTacticalArrow
            {
                StableId = (area.StableId ?? "area") + ".arrow." + suffix,
                AreaId = area.StableId ?? string.Empty,
                Kind = kind,
                Start = start,
                End = end,
                Marker = marker,
                Bidirectional = bidirectional,
                Verified = verified
            });
        }

        public bool TryGetArea(
            Vector3 worldPosition,
            out EdpcgRuntimeArea area)
        {
            area = null;
            float bestVolume = float.PositiveInfinity;
            for (int index = 0; index < areas.Count; index++)
            {
                EdpcgRuntimeArea candidate = areas[index];
                if (!candidate.Bounds.Contains(worldPosition))
                    continue;
                float volume = candidate.Bounds.size.x *
                               candidate.Bounds.size.y *
                               candidate.Bounds.size.z;
                if (volume >= bestVolume)
                    continue;
                area = candidate;
                bestVolume = volume;
            }
            return area != null;
        }

        public bool TryGetArea(string stableId, out EdpcgRuntimeArea area)
        {
            if (string.IsNullOrEmpty(stableId))
            {
                area = null;
                return false;
            }
            return areaById.TryGetValue(stableId, out area);
        }

        public bool TryGetRoute(string stableId, out EdpcgRuntimeRoute route)
        {
            if (string.IsNullOrEmpty(stableId))
            {
                route = null;
                return false;
            }
            return routeById.TryGetValue(stableId, out route);
        }

        public bool TrySelectEnvironmentalTrapRoute(
            Vector3 playerPosition,
            Vector3 enemyPosition,
            out EdpcgRuntimeRoute route)
        {
            route = null;
            float bestScore = float.PositiveInfinity;
            for (int index = 0; index < routes.Count; index++)
            {
                EdpcgRuntimeRoute candidate = routes[index];
                UrbanEnvironmentalPursuitRouteDescriptor descriptor =
                    candidate.EnvironmentalTrap;
                if (!candidate.IsEnvironmentalTrap || descriptor == null ||
                    !descriptor.IsAvailableForCommit(playerPosition) ||
                    Vector3.Distance(enemyPosition, playerPosition) >
                    UrbanEnvironmentalFieldPolicy.MaximumLureDistance)
                {
                    continue;
                }
                float score = Vector3.Distance(
                    enemyPosition,
                    candidate.Points[0]);
                if (descriptor.field.State ==
                    UrbanEnvironmentalFieldState.Active)
                {
                    score -= 70f;
                }
                else if (descriptor.field.State ==
                         UrbanEnvironmentalFieldState.Warning)
                {
                    score -= 35f;
                }
                if (score >= bestScore)
                    continue;
                route = candidate;
                bestScore = score;
            }
            return route != null;
        }

        public bool HasEnvironmentalTrapOpportunity(Vector3 playerPosition)
        {
            for (int index = 0; index < routes.Count; index++)
            {
                EdpcgRuntimeRoute candidate = routes[index];
                UrbanEnvironmentalPursuitRouteDescriptor descriptor =
                    candidate.EnvironmentalTrap;
                if (candidate.IsEnvironmentalTrap && descriptor != null &&
                    descriptor.IsAvailableForCommit(playerPosition))
                {
                    return true;
                }
            }
            return false;
        }

        public bool TrySelectRoleDestination(
            HordeEnemyRole role,
            int stableIndex,
            Vector3 playerPosition,
            EdpcgPathIntent intent,
            out Vector3 destination,
            out string areaId,
            out string routeId)
        {
            return TrySelectRoleDestination(
                role,
                stableIndex,
                playerPosition,
                intent,
                false,
                out destination,
                out areaId,
                out routeId);
        }

        public bool TrySelectRoleDestination(
            HordeEnemyRole role,
            int stableIndex,
            Vector3 playerPosition,
            EdpcgPathIntent intent,
            bool useRoleAssignments,
            out Vector3 destination,
            out string areaId,
            out string routeId)
        {
            destination = playerPosition;
            areaId = string.Empty;
            routeId = string.Empty;
            EdpcgTacticalAreaKind[] preferences = RolePreferences(
                role,
                intent,
                useRoleAssignments);
            EdpcgRuntimeArea area = useRoleAssignments
                ? SelectRoleArea(preferences, role, stableIndex, playerPosition)
                : SelectArea(preferences, stableIndex, playerPosition);
            if (area == null)
                return false;

            Vector3[] candidates = intent == EdpcgPathIntent.BreakAway ||
                                   intent == EdpcgPathIntent.BreakLineOfSight ||
                                   intent == EdpcgPathIntent.BreakContact
                ? area.Exits
                : area.Entrances;
            if (candidates == null || candidates.Length == 0)
            {
                destination = area.Bounds.center;
            }
            else
            {
                int index = PositiveModulo(stableIndex * 31 +
                                           (int)intent * 7,
                                           candidates.Length);
                destination = candidates[index];
            }
            areaId = area.StableId;
            EdpcgRuntimeRoute route = SelectRouteForIntent(
                role,
                intent,
                stableIndex,
                area,
                useRoleAssignments);
            if (route != null)
                routeId = route.StableId;
            return true;
        }

        public float EstimateMeasuredEnvironmentPressure(
            Vector3 playerPosition,
            float playerSpeed,
            float collisionStrain,
            out EdpcgRuntimeArea area)
        {
            if (!TryGetArea(playerPosition, out area))
                return Mathf.Clamp01(collisionStrain * 0.25f);
            float projected = area.ProjectedPressureUpperBound;
            float speedFactor = Mathf.InverseLerp(15f, 65f, playerSpeed);
            float realized = projected * Mathf.Lerp(0.4f, 1f, speedFactor);
            if (area.Kind == EdpcgTacticalAreaKind.SpawnSafeAirspace)
            {
                realized *= 0.35f;
            }
            return Mathf.Clamp01(realized + collisionStrain * 0.25f);
        }

        EdpcgRuntimeArea SelectArea(
            EdpcgTacticalAreaKind[] preferences,
            int stableIndex,
            Vector3 playerPosition)
        {
            for (int preference = 0; preference < preferences.Length; preference++)
            {
                int candidateCount = 0;
                for (int index = 0; index < areas.Count; index++)
                {
                    if (areas[index].Kind == preferences[preference])
                        candidateCount++;
                }
                if (candidateCount == 0)
                    continue;
                int selected = PositiveModulo(
                    stableIndex + Mathf.FloorToInt(playerPosition.x * 0.01f) +
                    Mathf.FloorToInt(playerPosition.z * 0.01f),
                    candidateCount);
                for (int index = 0; index < areas.Count; index++)
                {
                    if (areas[index].Kind != preferences[preference])
                        continue;
                    if (selected-- == 0)
                        return areas[index];
                }
            }
            return null;
        }

        EdpcgRuntimeRoute SelectRouteForIntent(
            HordeEnemyRole role,
            EdpcgPathIntent intent,
            int stableIndex,
            EdpcgRuntimeArea area,
            bool useRoleAssignments)
        {
            AirCombatRouteKind desired = intent == EdpcgPathIntent.BreakAway ||
                                         intent == EdpcgPathIntent.BreakLineOfSight ||
                                         intent == EdpcgPathIntent.BreakContact ||
                                         intent == EdpcgPathIntent.Regroup ||
                                         intent == EdpcgPathIntent.MaskedFlank
                ? AirCombatRouteKind.MaskedFlank
                : intent == EdpcgPathIntent.RangedPerch ||
                  intent == EdpcgPathIntent.Suppress
                    ? useRoleAssignments && role == HordeEnemyRole.Striker
                        ? AirCombatRouteKind.MaskedFlank
                        : AirCombatRouteKind.LongRange
                    : intent == EdpcgPathIntent.Ingress
                        ? AirCombatRouteKind.EnemyIngress
                        : AirCombatRouteKind.Main;
            EdpcgRuntimeRoute selected = null;
            float bestScore = float.PositiveInfinity;
            for (int index = 0; index < routes.Count; index++)
            {
                EdpcgRuntimeRoute candidate = routes[index];
                if (candidate == null || candidate.IsEnvironmentalTrap ||
                    candidate.SourceKind != desired ||
                    candidate.Points == null || candidate.Points.Length < 2)
                {
                    continue;
                }
                float routeDistance = float.PositiveInfinity;
                if (area != null)
                {
                    for (int point = 0;
                         point < candidate.Points.Length;
                         point++)
                    {
                        routeDistance = Mathf.Min(
                            routeDistance,
                            area.Bounds.SqrDistance(candidate.Points[point]));
                    }
                }
                else
                {
                    routeDistance = 0f;
                }
                // Keep deterministic variety only as a tie-breaker. Spatial
                // compatibility between the selected area and route remains
                // the primary criterion.
                float tieBreaker = PositiveModulo(
                    stableIndex * 31 + index * 17,
                    997) * 0.0001f;
                float score = routeDistance + tieBreaker;
                if (score >= bestScore)
                    continue;
                selected = candidate;
                bestScore = score;
            }
            return selected;
        }

        EdpcgRuntimeArea SelectRoleArea(
            EdpcgTacticalAreaKind[] preferences,
            HordeEnemyRole role,
            int stableIndex,
            Vector3 playerPosition)
        {
            float preferredDistance = role == HordeEnemyRole.Interceptor
                ? 110f
                : role == HordeEnemyRole.Striker ? 135f : 190f;
            EdpcgRuntimeArea selected = null;
            float bestScore = float.PositiveInfinity;
            for (int preference = 0; preference < preferences.Length; preference++)
            {
                for (int index = 0; index < areas.Count; index++)
                {
                    EdpcgRuntimeArea candidate = areas[index];
                    if (candidate.Kind != preferences[preference])
                        continue;
                    float distance = Vector3.Distance(
                        candidate.Bounds.ClosestPoint(playerPosition),
                        playerPosition);
                    float rangeError = Mathf.Abs(distance - preferredDistance);
                    float stableTieBreaker = PositiveModulo(
                        stableIndex * 31 + index * 17,
                        997) * 0.001f;
                    float score = preference * 70f + rangeError +
                                  stableTieBreaker;
                    if (score >= bestScore)
                        continue;
                    selected = candidate;
                    bestScore = score;
                }
            }
            return selected;
        }

        void AssignFallbackRoutes()
        {
            for (int index = 0; index < routes.Count; index++)
            {
                EdpcgRuntimeRoute source = routes[index];
                if (source.IsEnvironmentalTrap)
                    continue;
                float bestDistance = float.PositiveInfinity;
                EdpcgRuntimeRoute best = null;
                Vector3 sourceStart = source.Points[0];
                for (int otherIndex = 0; otherIndex < routes.Count; otherIndex++)
                {
                    EdpcgRuntimeRoute other = routes[otherIndex];
                    if (other == source || other.IsEnvironmentalTrap ||
                        other.SourceKind != source.SourceKind)
                        continue;
                    float distance = Vector3.Distance(sourceStart, other.Points[0]);
                    if (distance >= bestDistance)
                        continue;
                    best = other;
                    bestDistance = distance;
                }
                source.FallbackRouteId = best != null ? best.StableId : string.Empty;
            }
        }

        static EdpcgRuntimeArea BuildArea(
            Func<Vector3, Vector3> projectAirPoint,
            TacticalOpportunity source,
            EdpcgTacticalAreaKind kind)
        {
            Vector3 center = projectAirPoint(source.bounds.center);
            Vector3[] entrances = ProjectPoints(projectAirPoint, source.entrances);
            Vector3[] exits = ProjectPoints(projectAirPoint, source.exits);
            return new EdpcgRuntimeArea
            {
                StableId = source.stableId ?? string.Empty,
                Kind = kind,
                Bounds = new Bounds(center, source.bounds.size),
                Entrances = entrances,
                Exits = exits,
                Connections = source.connectedOpportunityIds ?? Array.Empty<string>(),
                ProjectedPressureUpperBound = PressureUpperBound(kind),
                // Recovery pockets are magnetic combat traps now.  Preserve
                // the authored value in the PCG design profile for backward
                // compatibility, but do not turn it into a hidden runtime
                // cease-fire or repair window.
                SafeWindowSeconds = kind ==
                    EdpcgTacticalAreaKind.MagneticCourtyard
                        ? 0f
                        : Mathf.Max(0f, source.safeWindowSeconds),
                Risk = Mathf.Clamp01(source.risk),
                Legibility = Mathf.Clamp01(source.legibility)
            };
        }

        static Vector3[] ProjectPoints(
            Func<Vector3, Vector3> projectAirPoint,
            Vector3[] points)
        {
            if (points == null || points.Length == 0)
                return Array.Empty<Vector3>();
            Vector3[] result = new Vector3[points.Length];
            for (int index = 0; index < points.Length; index++)
                result[index] = projectAirPoint(points[index]);
            return result;
        }

        static bool TryMapAreaKind(
            TacticalOpportunityKind source,
            out EdpcgTacticalAreaKind kind)
        {
            switch (source)
            {
                case TacticalOpportunityKind.ManeuverBowl:
                    kind = EdpcgTacticalAreaKind.CentralManeuverDistrict;
                    return true;
                case TacticalOpportunityKind.OcclusionChain:
                    kind = EdpcgTacticalAreaKind.HighRiseOcclusionChain;
                    return true;
                case TacticalOpportunityKind.ExposureShortcut:
                    kind = EdpcgTacticalAreaKind.ExposedFireShortcut;
                    return true;
                case TacticalOpportunityKind.RecoveryPocket:
                    kind = EdpcgTacticalAreaKind.MagneticCourtyard;
                    return true;
                case TacticalOpportunityKind.VerticalEscape:
                    kind = EdpcgTacticalAreaKind.LowMidVerticalTransition;
                    return true;
                default:
                    // Boss-only kite, choke and destruction opportunities are
                    // deliberately excluded from this non-Boss runtime map.
                    kind = EdpcgTacticalAreaKind.None;
                    return false;
            }
        }

        static float PressureUpperBound(EdpcgTacticalAreaKind kind)
        {
            switch (kind)
            {
                case EdpcgTacticalAreaKind.SpawnSafeAirspace:
                    return 0.03f;
                case EdpcgTacticalAreaKind.MagneticCourtyard:
                    // A magnetic courtyard creates a player-controlled combat
                    // opportunity; it is not intrinsically safe and therefore
                    // carries ordinary occlusion-district pressure.
                    return 0.16f;
                case EdpcgTacticalAreaKind.CentralManeuverDistrict:
                    return 0.12f;
                case EdpcgTacticalAreaKind.HighRiseOcclusionChain:
                    return 0.16f;
                case EdpcgTacticalAreaKind.LowMidVerticalTransition:
                    return 0.20f;
                case EdpcgTacticalAreaKind.ExposedFireShortcut:
                    return 0.28f;
                default:
                    return 0f;
            }
        }

        static EdpcgTacticalAreaKind[] SuicidePreferences(EdpcgPathIntent intent)
        {
            if (intent == EdpcgPathIntent.BreakAway ||
                intent == EdpcgPathIntent.BreakContact ||
                intent == EdpcgPathIntent.Regroup)
            {
                return new[]
                {
                    EdpcgTacticalAreaKind.HighRiseOcclusionChain,
                    EdpcgTacticalAreaKind.CentralManeuverDistrict
                };
            }
            return new[]
            {
                EdpcgTacticalAreaKind.CentralManeuverDistrict,
                EdpcgTacticalAreaKind.ExposedFireShortcut
            };
        }

        static EdpcgTacticalAreaKind[] RangedPreferences(EdpcgPathIntent intent)
        {
            if (intent == EdpcgPathIntent.BreakLineOfSight ||
                intent == EdpcgPathIntent.BreakContact ||
                intent == EdpcgPathIntent.Regroup)
            {
                return new[]
                {
                    EdpcgTacticalAreaKind.HighRiseOcclusionChain,
                    EdpcgTacticalAreaKind.CentralManeuverDistrict
                };
            }
            return new[]
            {
                EdpcgTacticalAreaKind.HighRiseOcclusionChain,
                EdpcgTacticalAreaKind.ExposedFireShortcut,
                EdpcgTacticalAreaKind.CentralManeuverDistrict
            };
        }

        static EdpcgTacticalAreaKind[] RolePreferences(
            HordeEnemyRole role,
            EdpcgPathIntent intent,
            bool useRoleAssignments)
        {
            if (role == HordeEnemyRole.Interceptor)
                return SuicidePreferences(intent);
            if (!useRoleAssignments)
                return RangedPreferences(intent);
            if (role == HordeEnemyRole.Striker)
                return StrikerPreferences(intent);
            return GunshipPreferences(intent);
        }

        static EdpcgTacticalAreaKind[] StrikerPreferences(EdpcgPathIntent intent)
        {
            if (intent == EdpcgPathIntent.BreakLineOfSight ||
                intent == EdpcgPathIntent.BreakContact ||
                intent == EdpcgPathIntent.Regroup)
            {
                return new[]
                {
                    EdpcgTacticalAreaKind.HighRiseOcclusionChain,
                    EdpcgTacticalAreaKind.CentralManeuverDistrict
                };
            }
            return new[]
            {
                EdpcgTacticalAreaKind.HighRiseOcclusionChain,
                EdpcgTacticalAreaKind.CentralManeuverDistrict,
                EdpcgTacticalAreaKind.ExposedFireShortcut
            };
        }

        static EdpcgTacticalAreaKind[] GunshipPreferences(EdpcgPathIntent intent)
        {
            if (intent == EdpcgPathIntent.BreakLineOfSight ||
                intent == EdpcgPathIntent.BreakContact ||
                intent == EdpcgPathIntent.Regroup)
            {
                return new[]
                {
                    EdpcgTacticalAreaKind.CentralManeuverDistrict,
                    EdpcgTacticalAreaKind.HighRiseOcclusionChain
                };
            }
            return new[]
            {
                EdpcgTacticalAreaKind.ExposedFireShortcut,
                EdpcgTacticalAreaKind.CentralManeuverDistrict,
                EdpcgTacticalAreaKind.HighRiseOcclusionChain
            };
        }

        static int PositiveModulo(int value, int modulus)
        {
            if (modulus <= 0)
                return 0;
            int result = value % modulus;
            return result < 0 ? result + modulus : result;
        }
    }

    public sealed class EdpcgRouteReservationService
    {
        readonly List<EdpcgRouteReservation> reservations =
            new List<EdpcgRouteReservation>(64);
        readonly EdpcgCityTacticalRuntimeMap map;
        int sequence;

        public IReadOnlyList<EdpcgRouteReservation> Reservations => reservations;

        public EdpcgRouteReservationService(EdpcgCityTacticalRuntimeMap tacticalMap)
        {
            map = tacticalMap ?? new EdpcgCityTacticalRuntimeMap();
        }

        public bool TryReserve(
            string ownerRosterMemberId,
            string routeId,
            float now,
            float travelSeconds,
            float leaseSeconds,
            int priority,
            int direction,
            out string reservationId)
        {
            return TryReserveInternal(
                ownerRosterMemberId, routeId, now, travelSeconds,
                leaseSeconds, priority, direction,
                false, -1, 0f, Vector3.zero, string.Empty,
                out reservationId);
        }

        public bool TryReserveToProgress(
            string ownerRosterMemberId,
            string routeId,
            float now,
            float travelSeconds,
            float leaseSeconds,
            int priority,
            int direction,
            int targetSegmentIndex,
            float targetSegmentT,
            Vector3 targetWorldPosition,
            string targetStableId,
            out string reservationId)
        {
            return TryReserveInternal(
                ownerRosterMemberId, routeId, now, travelSeconds,
                leaseSeconds, priority, direction,
                true, targetSegmentIndex, targetSegmentT,
                targetWorldPosition, targetStableId,
                out reservationId);
        }

        bool TryReserveInternal(
            string ownerRosterMemberId,
            string routeId,
            float now,
            float travelSeconds,
            float leaseSeconds,
            int priority,
            int direction,
            bool hasTargetProgress,
            int targetSegmentIndex,
            float targetSegmentT,
            Vector3 targetWorldPosition,
            string targetStableId,
            out string reservationId)
        {
            reservationId = string.Empty;
            if (string.IsNullOrEmpty(ownerRosterMemberId) ||
                string.IsNullOrEmpty(routeId) ||
                !IsFinite(now) || !IsFinite(travelSeconds) ||
                !IsFinite(leaseSeconds) ||
                !map.TryGetRoute(routeId, out EdpcgRuntimeRoute route))
            {
                return false;
            }
            if (!route.AllowReverse && direction < 0)
                return false;
            if (hasTargetProgress &&
                (route.Points == null || route.Points.Length < 2 ||
                 !IsFinite(targetSegmentT) ||
                 !IsFinite(targetWorldPosition) ||
                 targetSegmentIndex < 0 ||
                 targetSegmentIndex >= route.Points.Length - 1))
            {
                return false;
            }
            if (hasTargetProgress)
            {
                Vector3 routeTarget = Vector3.Lerp(
                    route.Points[targetSegmentIndex],
                    route.Points[targetSegmentIndex + 1],
                    Mathf.Clamp01(targetSegmentT));
                if (Vector3.Distance(routeTarget, targetWorldPosition) > 1.5f)
                    return false;
            }

            ReleaseExpired(now);
            int overlap = 0;
            float enterAt = now;
            float exitAt = now + Mathf.Max(0.25f, travelSeconds);
            for (int index = 0; index < reservations.Count; index++)
            {
                EdpcgRouteReservation existing = reservations[index];
                if (!string.Equals(existing.routeId, routeId, StringComparison.Ordinal) ||
                    existing.exitAt <= enterAt || existing.enterAt >= exitAt)
                {
                    continue;
                }
                overlap++;
            }
            if (overlap >= route.Capacity)
                return false;

            reservationId = "reservation-" + (++sequence).ToString("D6");
            reservations.Add(new EdpcgRouteReservation
            {
                reservationId = reservationId,
                ownerRosterMemberId = ownerRosterMemberId,
                routeId = routeId,
                enterAt = enterAt,
                exitAt = exitAt,
                expiresAt = now + Mathf.Max(1f, leaseSeconds),
                priority = priority,
                direction = route.AllowReverse ? direction : 1,
                waypointIndex = route.AllowReverse && direction < 0
                    ? Mathf.Max(0, route.Points.Length - 1)
                    : 0,
                hasTargetProgress = hasTargetProgress,
                targetSegmentIndex = hasTargetProgress
                    ? targetSegmentIndex
                    : -1,
                targetSegmentT = hasTargetProgress
                    ? Mathf.Clamp01(targetSegmentT)
                    : 0f,
                targetWorldPosition = hasTargetProgress
                    ? targetWorldPosition
                    : Vector3.zero,
                targetStableId = hasTargetProgress
                    ? targetStableId ?? string.Empty
                    : string.Empty
            });
            return true;
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        public bool TryGetOwnerReservation(
            string ownerRosterMemberId,
            out EdpcgRouteReservation reservation)
        {
            reservation = null;
            if (string.IsNullOrEmpty(ownerRosterMemberId))
                return false;
            for (int index = reservations.Count - 1; index >= 0; index--)
            {
                EdpcgRouteReservation candidate = reservations[index];
                if (!string.Equals(
                        candidate.ownerRosterMemberId,
                        ownerRosterMemberId,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                reservation = candidate;
                return true;
            }
            return false;
        }

        public bool Renew(string reservationId, float now, float leaseSeconds)
        {
            return Renew(reservationId, now, leaseSeconds, -1f);
        }

        public bool Renew(
            string reservationId,
            float now,
            float leaseSeconds,
            float remainingTravelSeconds)
        {
            for (int index = 0; index < reservations.Count; index++)
            {
                EdpcgRouteReservation item = reservations[index];
                if (!string.Equals(
                        item.reservationId,
                        reservationId,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                item.expiresAt = now + Mathf.Max(1f, leaseSeconds);
                if (remainingTravelSeconds >= 0f)
                {
                    item.exitAt = now + Mathf.Max(
                        0.25f, remainingTravelSeconds);
                }
                return true;
            }
            return false;
        }

        public bool HasReservation(string reservationId)
        {
            if (string.IsNullOrEmpty(reservationId))
                return false;
            for (int index = 0; index < reservations.Count; index++)
            {
                if (string.Equals(
                        reservations[index].reservationId,
                        reservationId,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        public int ReleaseOwner(string ownerRosterMemberId)
        {
            int released = 0;
            for (int index = reservations.Count - 1; index >= 0; index--)
            {
                if (!string.Equals(
                        reservations[index].ownerRosterMemberId,
                        ownerRosterMemberId,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                reservations.RemoveAt(index);
                released++;
            }
            return released;
        }

        public bool Release(string reservationId)
        {
            for (int index = reservations.Count - 1; index >= 0; index--)
            {
                if (!string.Equals(
                        reservations[index].reservationId,
                        reservationId,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                reservations.RemoveAt(index);
                return true;
            }
            return false;
        }

        public int ReleaseExpired(float now)
        {
            int released = 0;
            for (int index = reservations.Count - 1; index >= 0; index--)
            {
                if (reservations[index].expiresAt > now)
                    continue;
                reservations.RemoveAt(index);
                released++;
            }
            return released;
        }

        public void Clear()
        {
            reservations.Clear();
        }
    }
}
