using System;
using System.Collections.Generic;
using UnityEngine;
using UnityPlanet.CityPcg;
using UnityPlanet.ModularAssembly;

namespace UnityPlanet.EDPCG
{
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
    }

    public sealed class EdpcgCityTacticalRuntimeMap
    {
        readonly List<EdpcgRuntimeIngress> ingresses =
            new List<EdpcgRuntimeIngress>(8);
        readonly List<EdpcgRuntimeArea> areas =
            new List<EdpcgRuntimeArea>(16);
        readonly List<EdpcgRuntimeRoute> routes =
            new List<EdpcgRuntimeRoute>(16);
        readonly Dictionary<string, EdpcgRuntimeArea> areaById =
            new Dictionary<string, EdpcgRuntimeArea>(StringComparer.Ordinal);
        readonly Dictionary<string, EdpcgRuntimeRoute> routeById =
            new Dictionary<string, EdpcgRuntimeRoute>(StringComparer.Ordinal);

        public IReadOnlyList<EdpcgRuntimeIngress> Ingresses => ingresses;
        public IReadOnlyList<EdpcgRuntimeArea> Areas => areas;
        public IReadOnlyList<EdpcgRuntimeRoute> Routes => routes;
        public bool HasCityData => areas.Count > 0 || routes.Count > 0;

        public static EdpcgCityTacticalRuntimeMap Build(
            FinitePlanetUrbanCombatRuntime urban)
        {
            var result = new EdpcgCityTacticalRuntimeMap();
            AirCombatCityPlan plan = urban != null ? urban.Plan : null;
            if (plan == null)
                return result;

            for (int index = 0; index < plan.ingresses.Count; index++)
            {
                AirCombatEnemyIngress source = plan.ingresses[index];
                if (source == null)
                    continue;
                result.ingresses.Add(new EdpcgRuntimeIngress
                {
                    StableId = source.stableId ?? string.Empty,
                    Position = ProjectAirPoint(urban, source.position),
                    Target = ProjectAirPoint(urban, source.target),
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
                EdpcgRuntimeArea area = BuildArea(urban, source, kind);
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
                Vector3 center = ProjectAirPoint(urban, volume.center);
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
                    points[pointIndex] = ProjectAirPoint(
                        urban,
                        source.points[pointIndex]);
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

            result.AssignFallbackRoutes();
            return result;
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

        public bool TrySelectRoleDestination(
            HordeEnemyRole role,
            int stableIndex,
            Vector3 playerPosition,
            EdpcgPathIntent intent,
            out Vector3 destination,
            out string areaId,
            out string routeId)
        {
            destination = playerPosition;
            areaId = string.Empty;
            routeId = string.Empty;
            EdpcgTacticalAreaKind[] preferences = role == HordeEnemyRole.Interceptor
                ? SuicidePreferences(intent)
                : RangedPreferences(intent);
            EdpcgRuntimeArea area = SelectArea(
                preferences,
                stableIndex,
                playerPosition);
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
            EdpcgRuntimeRoute route = SelectRouteForIntent(intent, stableIndex);
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
            if (area.Kind == EdpcgTacticalAreaKind.RepairCourtyard ||
                area.Kind == EdpcgTacticalAreaKind.SpawnSafeAirspace)
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
            EdpcgPathIntent intent,
            int stableIndex)
        {
            AirCombatRouteKind desired = intent == EdpcgPathIntent.BreakAway ||
                                         intent == EdpcgPathIntent.BreakLineOfSight
                ? AirCombatRouteKind.MaskedFlank
                : intent == EdpcgPathIntent.RangedPerch ||
                  intent == EdpcgPathIntent.Suppress
                    ? AirCombatRouteKind.LongRange
                    : intent == EdpcgPathIntent.Ingress
                        ? AirCombatRouteKind.EnemyIngress
                        : AirCombatRouteKind.Main;
            int count = 0;
            for (int index = 0; index < routes.Count; index++)
                if (routes[index].SourceKind == desired)
                    count++;
            if (count == 0)
                return null;
            int selected = PositiveModulo(stableIndex, count);
            for (int index = 0; index < routes.Count; index++)
            {
                if (routes[index].SourceKind != desired)
                    continue;
                if (selected-- == 0)
                    return routes[index];
            }
            return null;
        }

        void AssignFallbackRoutes()
        {
            for (int index = 0; index < routes.Count; index++)
            {
                EdpcgRuntimeRoute source = routes[index];
                float bestDistance = float.PositiveInfinity;
                EdpcgRuntimeRoute best = null;
                Vector3 sourceStart = source.Points[0];
                for (int otherIndex = 0; otherIndex < routes.Count; otherIndex++)
                {
                    EdpcgRuntimeRoute other = routes[otherIndex];
                    if (other == source || other.SourceKind != source.SourceKind)
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
            FinitePlanetUrbanCombatRuntime urban,
            TacticalOpportunity source,
            EdpcgTacticalAreaKind kind)
        {
            Vector3 center = ProjectAirPoint(urban, source.bounds.center);
            Vector3[] entrances = ProjectPoints(urban, source.entrances);
            Vector3[] exits = ProjectPoints(urban, source.exits);
            return new EdpcgRuntimeArea
            {
                StableId = source.stableId ?? string.Empty,
                Kind = kind,
                Bounds = new Bounds(center, source.bounds.size),
                Entrances = entrances,
                Exits = exits,
                Connections = source.connectedOpportunityIds ?? Array.Empty<string>(),
                ProjectedPressureUpperBound = PressureUpperBound(kind),
                SafeWindowSeconds = Mathf.Max(0f, source.safeWindowSeconds),
                Risk = Mathf.Clamp01(source.risk),
                Legibility = Mathf.Clamp01(source.legibility)
            };
        }

        static Vector3[] ProjectPoints(
            FinitePlanetUrbanCombatRuntime urban,
            Vector3[] points)
        {
            if (points == null || points.Length == 0)
                return Array.Empty<Vector3>();
            Vector3[] result = new Vector3[points.Length];
            for (int index = 0; index < points.Length; index++)
                result[index] = ProjectAirPoint(urban, points[index]);
            return result;
        }

        static Vector3 ProjectAirPoint(
            FinitePlanetUrbanCombatRuntime urban,
            Vector3 planPosition)
        {
            return urban.ProjectPlanPosition(planPosition) +
                   Vector3.up * planPosition.y;
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
                    kind = EdpcgTacticalAreaKind.RepairCourtyard;
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
                case EdpcgTacticalAreaKind.RepairCourtyard:
                    return 0.05f;
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
            reservationId = string.Empty;
            if (string.IsNullOrEmpty(ownerRosterMemberId) ||
                string.IsNullOrEmpty(routeId) ||
                !map.TryGetRoute(routeId, out EdpcgRuntimeRoute route))
            {
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
                direction = direction
            });
            return true;
        }

        public bool Renew(string reservationId, float now, float leaseSeconds)
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
                return true;
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
