using System;
using System.Collections.Generic;
using UnityEngine;
using UnityPlanet.ModularAssembly;

namespace UnityPlanet.CityPcg
{
    public enum UrbanEnvironmentalFieldKind
    {
        NaturalStreetGale = 0,
        MagneticCourtyard = 1
    }

    public enum UrbanEnvironmentalFieldState
    {
        Dormant = 0,
        Warning = 1,
        Active = 2,
        Cooldown = 3
    }

    public enum UrbanEnvironmentalFieldControlMode
    {
        Automatic = 0,
        ForcedDisabled = 1,
        ForcedActive = 2
    }

    /// <summary>
    /// A real building facade facing into a magnetic courtyard. Positions and
    /// directions are local to the environmental-field transform. The inward
    /// normal always points away from the building and into the courtyard.
    /// </summary>
    public sealed class UrbanMagneticWallSurface
    {
        public string stableId = string.Empty;
        public Vector3 localCenter;
        public Vector3 localInwardNormal = Vector3.forward;
        public float width;
        public float height;
    }

    /// <summary>
    /// Ordered, forward-only combat route into a generated environmental trap.
    /// Points are world-space because the formal EDPCG navigation graph also
    /// operates in world space.  The descriptor retains the owning field so a
    /// lease can be cancelled as soon as the physical opportunity disappears.
    /// </summary>
    public sealed class UrbanEnvironmentalPursuitRouteDescriptor
    {
        public string stableId = string.Empty;
        public UrbanEnvironmentalFieldKind kind;
        public UrbanEnvironmentalFieldVolume field;
        public Vector3[] worldWaypoints = Array.Empty<Vector3>();
        public Vector3 worldImpactPoint;
        public Vector3 localApproachCenter;
        public Vector3 localApproachSize;
        public int capacity = 1;
        public bool allowReverse;

        public bool IsUsable => field != null && field.enabled &&
                                worldWaypoints != null &&
                                worldWaypoints.Length >= 3 && capacity > 0;

        public bool ContainsPlayer(Vector3 worldPosition)
        {
            if (!IsUsable)
                return false;
            Vector3 local = field.transform.InverseTransformPoint(worldPosition) -
                            localApproachCenter;
            Vector3 half = localApproachSize * 0.5f;
            return Mathf.Abs(local.x) <= half.x &&
                   Mathf.Abs(local.y) <= half.y &&
                   Mathf.Abs(local.z) <= half.z;
        }

        public bool IsAvailableForCommit(Vector3 playerPosition)
        {
            if (!ContainsPlayer(playerPosition))
                return false;
            if (kind == UrbanEnvironmentalFieldKind.NaturalStreetGale)
            {
                return field.State == UrbanEnvironmentalFieldState.Warning ||
                       field.State == UrbanEnvironmentalFieldState.Active;
            }
            return field.State == UrbanEnvironmentalFieldState.Dormant ||
                   field.State == UrbanEnvironmentalFieldState.Warning ||
                   field.State == UrbanEnvironmentalFieldState.Active;
        }
    }

    /// <summary>
    /// Centralizes the city pseudo-physical rules so mass, installed wings and the
    /// selected control mode always influence both city hazards consistently.
    /// Forces remain finite and are delivered through the vehicle force ledger.
    /// </summary>
    public static class UrbanEnvironmentalFieldPolicy
    {
        public const float WindWarningSeconds = 2.4f;
        public const float WindActiveSeconds = 5.6f;
        public const float WindCooldownSeconds = 8.5f;
        public const float MagnetWarningSeconds = 2.4f;
        public const float MagnetActiveSeconds = 5.8f;
        public const float MagnetCooldownSeconds = 8.0f;
        public const float MaximumLureDistance = 680f;

        public static bool IsEnvironmentalPursuer(
            int stableIndex,
            HordeEnemyRole role)
        {
            // Only a deterministic subset follows the player into a trap.
            // Gunships retain their readable ranged role and never crowd it.
            return role != HordeEnemyRole.Gunship &&
                   (stableIndex & 1) == 0;
        }

        public static float ResolvePlayerForceScale(
            RobocraftTelemetry telemetry,
            VehicleCoreAssistMode mode,
            UrbanEnvironmentalFieldKind kind)
        {
            return ResolvePlayerForceScale(
                telemetry,
                mode,
                kind,
                Vector3.one.normalized);
        }

        public static float ResolvePlayerForceScale(
            RobocraftTelemetry telemetry,
            VehicleCoreAssistMode mode,
            UrbanEnvironmentalFieldKind kind,
            Vector3 localForceDirection)
        {
            float wingResistance = Mathf.Clamp01(
                telemetry.wingArea / 48f);
            Vector3 direction = localForceDirection.sqrMagnitude > 0.001f
                ? localForceDirection.normalized
                : Vector3.forward;
            float controlAcceleration =
                Mathf.Abs(direction.x) * (direction.x >= 0f
                    ? telemetry.negativeAcceleration.x
                    : telemetry.positiveAcceleration.x) +
                Mathf.Abs(direction.y) * (direction.y >= 0f
                    ? telemetry.negativeAcceleration.y
                    : telemetry.positiveAcceleration.y) +
                Mathf.Abs(direction.z) * (direction.z >= 0f
                    ? telemetry.negativeAcceleration.z
                    : telemetry.positiveAcceleration.z);
            float controlResistance = Mathf.Clamp01(
                controlAcceleration / 32f);
            float installedResistance = 1f -
                wingResistance * (kind ==
                    UrbanEnvironmentalFieldKind.NaturalStreetGale
                        ? 0.42f
                        : 0f) -
                controlResistance * 0.18f;
            float modeScale = mode == VehicleCoreAssistMode.Training
                ? kind == UrbanEnvironmentalFieldKind.NaturalStreetGale
                    ? 0.58f
                    : 0.62f
                : 1f;
            return Mathf.Clamp(installedResistance * modeScale, 0.34f, 1f);
        }

        public static float ResolveWindForce(
            Rigidbody body,
            RobocraftMotionCoordinator motion)
        {
            return ResolveWindForce(body, motion, Vector3.forward);
        }

        public static float ResolveWindForce(
            Rigidbody body,
            RobocraftMotionCoordinator motion,
            Vector3 worldForceDirection)
        {
            float area = 4f;
            float playerScale = 1f;
            if (motion != null)
            {
                RobocraftTelemetry telemetry = motion.Telemetry;
                area = Mathf.Clamp(
                    Mathf.Max(
                        telemetry.dragArea.x,
                        telemetry.dragArea.y,
                        telemetry.dragArea.z,
                        telemetry.exposedArea.x * 0.65f,
                        telemetry.exposedArea.y * 0.65f,
                        telemetry.exposedArea.z * 0.65f),
                    2f,
                    18f);
                playerScale = ResolvePlayerForceScale(
                    telemetry,
                    motion.CoreAssistMode,
                    UrbanEnvironmentalFieldKind.NaturalStreetGale,
                    motion.transform.InverseTransformDirection(
                        worldForceDirection));
            }
            return 52000f * (0.72f + area * 0.13f) * playerScale;
        }

        public static float ResolveMagneticForce(
            RobocraftMotionCoordinator motion,
            Vector3 worldForceDirection)
        {
            if (motion == null)
                return 94000f;
            return 94000f * ResolvePlayerForceScale(
                motion.Telemetry,
                motion.CoreAssistMode,
                UrbanEnvironmentalFieldKind.MagneticCourtyard,
                motion.transform.InverseTransformDirection(
                    worldForceDirection));
        }
    }

    [DisallowMultipleComponent]
    public sealed class UrbanEnvironmentalFieldDirector : MonoBehaviour
    {
        const string FieldRootName = "07_自然风与三面磁场_战斗陷阱";
        static readonly List<UrbanEnvironmentalFieldDirector> ActiveDirectors =
            new List<UrbanEnvironmentalFieldDirector>(4);

        readonly List<UrbanEnvironmentalFieldVolume> fields =
            new List<UrbanEnvironmentalFieldVolume>(4);
        readonly List<UrbanEnvironmentalPursuitRouteDescriptor> pursuitRoutes =
            new List<UrbanEnvironmentalPursuitRouteDescriptor>(4);
        Transform fieldRoot;

        public IReadOnlyList<UrbanEnvironmentalFieldVolume> Fields => fields;
        public IReadOnlyList<UrbanEnvironmentalPursuitRouteDescriptor>
            PursuitRoutes => pursuitRoutes;
        public string ValidationError { get; private set; } = string.Empty;
        public bool HasRequiredCombatTraps
        {
            get
            {
                bool wind = false;
                bool magnet = false;
                for (int index = 0; index < pursuitRoutes.Count; index++)
                {
                    UrbanEnvironmentalPursuitRouteDescriptor route =
                        pursuitRoutes[index];
                    if (route == null || !route.IsUsable)
                        continue;
                    wind |= route.kind ==
                            UrbanEnvironmentalFieldKind.NaturalStreetGale;
                    magnet |= route.kind ==
                              UrbanEnvironmentalFieldKind.MagneticCourtyard;
                }
                return wind && magnet;
            }
        }

        void OnEnable()
        {
            if (!ActiveDirectors.Contains(this))
                ActiveDirectors.Add(this);
        }

        void OnDisable()
        {
            ActiveDirectors.Remove(this);
        }

        public void Configure(
            AirCombatCityPlan plan,
            AirCombatCitySettings settings)
        {
            ClearGeneratedFields();
            ValidationError = string.Empty;
            if (plan == null || settings == null)
            {
                ValidationError = "城市环境陷阱缺少 PCG 计划或设置。";
                return;
            }

            var root = new GameObject(FieldRootName);
            fieldRoot = root.transform;
            fieldRoot.SetParent(transform, false);

            if (TrySelectWindTrapCandidate(
                    plan,
                    out WindTrapCandidate windCandidate))
            {
                BuildNaturalStreetGale(
                    windCandidate,
                    settings,
                    plan.resolvedSeed);
            }

            int magneticCourtyardIndex = 0;
            for (int index = 0; index < plan.volumes.Count; index++)
            {
                AirCombatTacticalVolume volume = plan.volumes[index];
                if (volume == null ||
                    volume.kind != AirCombatVolumeKind.RecoveryPocket)
                {
                    continue;
                }
                BuildMagneticCourtyard(
                    plan,
                    volume,
                    settings,
                    magneticCourtyardIndex++);
            }
            if (!HasRequiredCombatTraps)
            {
                ValidationError =
                    "城市 PCG 没有同时提供可通行的侧入风场路线和三面磁墙路线。";
            }
        }

        public static bool TryResolveEnemyLure(
            Rigidbody playerBody,
            Vector3 enemyPosition,
            int stableIndex,
            HordeEnemyRole role,
            out Vector3 destination)
        {
            destination = Vector3.zero;
            if (!Application.isPlaying || playerBody == null ||
                !UrbanEnvironmentalFieldPolicy.IsEnvironmentalPursuer(
                    stableIndex,
                    role))
            {
                return false;
            }

            for (int directorIndex = 0;
                 directorIndex < ActiveDirectors.Count;
                 directorIndex++)
            {
                UrbanEnvironmentalFieldDirector director =
                    ActiveDirectors[directorIndex];
                if (director == null || !director.isActiveAndEnabled)
                    continue;
                for (int fieldIndex = 0;
                     fieldIndex < director.fields.Count;
                     fieldIndex++)
                {
                    UrbanEnvironmentalFieldVolume field =
                        director.fields[fieldIndex];
                    if (field != null && field.TryResolveEnemyLure(
                            playerBody.worldCenterOfMass,
                            enemyPosition,
                            stableIndex,
                            out destination))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        struct WindTrapCandidate
        {
            public AirCombatRoadStrip road;
            public Vector3 direction;
            public Vector3 sideEntry;
            public float sideSign;
            public Vector3 impactPoint;
        }

        static bool TrySelectWindTrapCandidate(
            AirCombatCityPlan plan,
            out WindTrapCandidate selected)
        {
            selected = default(WindTrapCandidate);
            float selectedScore = float.NegativeInfinity;
            List<AirCombatRoadStrip> continuousRoads =
                BuildContinuousRoadCorridors(plan);
            for (int index = 0; index < continuousRoads.Count; index++)
            {
                AirCombatRoadStrip road = continuousRoads[index];
                Vector3 roadDelta = Vector3.ProjectOnPlane(
                    road.end - road.start,
                    Vector3.up);
                float length = roadDelta.magnitude;
                if (length < 120f)
                    continue;

                Vector3 baseDirection = roadDelta / length;
                for (int orientation = 0; orientation < 2; orientation++)
                {
                    Vector3 direction = orientation == 0
                        ? baseDirection
                        : -baseDirection;
                    Vector3 downwindEnd = orientation == 0
                        ? road.end
                        : road.start;
                    if (!TryFindDownwindImpact(
                            plan,
                            downwindEnd,
                            direction,
                            road.width,
                            out Vector3 impactPoint,
                            out float impactDistance) ||
                        !TryFindPerpendicularFeeder(
                            plan,
                            road,
                            direction,
                            out Vector3 sideEntry,
                            out float sideSign))
                    {
                        continue;
                    }

                    Vector3 center = (road.start + road.end) * 0.5f;
                    float score = length +
                                  Vector3.Distance(
                                      center,
                                      plan.playerSpawn) * 0.22f +
                                  (road.kind == AirCombatRouteKind.Main
                                      ? 80f
                                      : 0f) -
                                  impactDistance * 0.35f;
                    if (score <= selectedScore)
                        continue;
                    selected = new WindTrapCandidate
                    {
                        road = road,
                        direction = direction,
                        sideEntry = sideEntry,
                        sideSign = sideSign,
                        impactPoint = impactPoint
                    };
                    selectedScore = score;
                }
            }
            return selected.road != null;
        }

        static bool TryFindDownwindImpact(
            AirCombatCityPlan plan,
            Vector3 roadEnd,
            Vector3 direction,
            float roadWidth,
            out Vector3 impactPoint,
            out float facadeDistance)
        {
            impactPoint = Vector3.zero;
            facadeDistance = float.PositiveInfinity;
            if (plan == null || plan.buildings == null)
                return false;
            Vector3 side = Vector3.Cross(Vector3.up, direction).normalized;
            AirCombatBuildingLot selected = null;
            for (int index = 0; index < plan.buildings.Count; index++)
            {
                AirCombatBuildingLot building = plan.buildings[index];
                if (building == null || building.size.y < 32f)
                    continue;
                Quaternion rotation = Quaternion.Euler(0f, building.yaw, 0f);
                Vector3 right = rotation * Vector3.right;
                Vector3 forward = rotation * Vector3.forward;
                float extentAlong =
                    Mathf.Abs(Vector3.Dot(right, direction)) *
                    building.size.x * 0.5f +
                    Mathf.Abs(Vector3.Dot(forward, direction)) *
                    building.size.z * 0.5f;
                float extentAcross =
                    Mathf.Abs(Vector3.Dot(right, side)) *
                    building.size.x * 0.5f +
                    Mathf.Abs(Vector3.Dot(forward, side)) *
                    building.size.z * 0.5f;
                Vector3 delta = Vector3.ProjectOnPlane(
                    building.center - roadEnd,
                    Vector3.up);
                float distance = Vector3.Dot(delta, direction) - extentAlong;
                float across = Mathf.Abs(Vector3.Dot(delta, side));
                if (distance < -8f || distance > 72f ||
                    across > roadWidth * 0.45f + extentAcross * 0.55f ||
                    distance >= facadeDistance)
                {
                    continue;
                }
                selected = building;
                facadeDistance = Mathf.Max(0f, distance);
                impactPoint = building.center - direction * extentAlong;
                impactPoint.y = Mathf.Clamp(
                    building.center.y,
                    24f,
                    Mathf.Max(24f, building.size.y - 12f));
            }
            return selected != null;
        }

        static bool TryFindPerpendicularFeeder(
            AirCombatCityPlan plan,
            AirCombatRoadStrip corridor,
            Vector3 direction,
            out Vector3 intersection,
            out float sideSign)
        {
            intersection = Vector3.zero;
            sideSign = 1f;
            if (plan == null || plan.roads == null || corridor == null)
                return false;
            Vector2 p = new Vector2(corridor.start.x, corridor.start.z);
            Vector2 r = new Vector2(
                corridor.end.x - corridor.start.x,
                corridor.end.z - corridor.start.z);
            Vector3 side = Vector3.Cross(Vector3.up, direction).normalized;
            float bestCenterDistance = float.PositiveInfinity;
            bool found = false;
            for (int index = 0; index < plan.roads.Count; index++)
            {
                AirCombatRoadStrip feeder = plan.roads[index];
                if (feeder == null || feeder.dangerLane)
                    continue;
                Vector3 feederDirection = Vector3.ProjectOnPlane(
                    feeder.end - feeder.start,
                    Vector3.up);
                if (feederDirection.sqrMagnitude < 1f ||
                    Mathf.Abs(Vector3.Dot(
                        feederDirection.normalized,
                        direction)) > 0.18f)
                {
                    continue;
                }
                Vector2 q = new Vector2(feeder.start.x, feeder.start.z);
                Vector2 s = new Vector2(
                    feeder.end.x - feeder.start.x,
                    feeder.end.z - feeder.start.z);
                float cross = Cross2D(r, s);
                if (Mathf.Abs(cross) < 0.001f)
                    continue;
                Vector2 qp = q - p;
                float t = Cross2D(qp, s) / cross;
                float u = Cross2D(qp, r) / cross;
                if (t < 0.16f || t > 0.84f || u < 0f || u > 1f)
                    continue;
                Vector2 hit = p + r * t;
                float centerDistance = Mathf.Abs(t - 0.5f);
                if (centerDistance >= bestCenterDistance)
                    continue;
                intersection = new Vector3(hit.x, 0f, hit.y);
                float firstSide = Vector3.Dot(
                    feeder.start - intersection,
                    side);
                float secondSide = Vector3.Dot(
                    feeder.end - intersection,
                    side);
                sideSign = Mathf.Abs(firstSide) >= Mathf.Abs(secondSide)
                    ? Mathf.Sign(firstSide)
                    : Mathf.Sign(secondSide);
                if (Mathf.Abs(sideSign) < 0.5f)
                    sideSign = 1f;
                bestCenterDistance = centerDistance;
                found = true;
            }
            return found;
        }

        static float Cross2D(Vector2 first, Vector2 second)
        {
            return first.x * second.y - first.y * second.x;
        }

        static List<AirCombatRoadStrip> BuildContinuousRoadCorridors(
            AirCombatCityPlan plan)
        {
            const float AxisTolerance = 0.25f;
            const float JoinTolerance = 0.5f;
            var result = new List<AirCombatRoadStrip>(16);
            if (plan == null || plan.roads == null)
                return result;

            bool[] grouped = new bool[plan.roads.Count];
            for (int seedIndex = 0;
                 seedIndex < plan.roads.Count;
                 seedIndex++)
            {
                if (grouped[seedIndex])
                    continue;
                AirCombatRoadStrip seed = plan.roads[seedIndex];
                if (!TryResolveRoadAxis(
                        seed,
                        out bool alongZ,
                        out float coordinate,
                        out _,
                        out _))
                {
                    grouped[seedIndex] = true;
                    continue;
                }

                var intervals = new List<RoadInterval>(8);
                for (int roadIndex = seedIndex;
                     roadIndex < plan.roads.Count;
                     roadIndex++)
                {
                    if (grouped[roadIndex])
                        continue;
                    AirCombatRoadStrip road = plan.roads[roadIndex];
                    if (!TryResolveRoadAxis(
                            road,
                            out bool candidateAlongZ,
                            out float candidateCoordinate,
                            out float from,
                            out float to) ||
                        candidateAlongZ != alongZ ||
                        Mathf.Abs(candidateCoordinate - coordinate) >
                        AxisTolerance)
                    {
                        continue;
                    }

                    grouped[roadIndex] = true;
                    intervals.Add(new RoadInterval
                    {
                        road = road,
                        from = from,
                        to = to
                    });
                }

                intervals.Sort((first, second) =>
                    first.from.CompareTo(second.from));
                if (intervals.Count == 0)
                    continue;

                int runIndex = 0;
                float runFrom = intervals[0].from;
                float runTo = intervals[0].to;
                float runWidth = intervals[0].road.width;
                int runLaneTiles = intervals[0].road.laneTiles;
                AirCombatRouteKind runKind = intervals[0].road.kind;
                for (int intervalIndex = 1;
                     intervalIndex < intervals.Count;
                     intervalIndex++)
                {
                    RoadInterval next = intervals[intervalIndex];
                    if (next.from <= runTo + JoinTolerance)
                    {
                        runTo = Mathf.Max(runTo, next.to);
                        runWidth = Mathf.Min(runWidth, next.road.width);
                        runLaneTiles = Mathf.Min(
                            runLaneTiles,
                            next.road.laneTiles);
                        if (next.road.kind == AirCombatRouteKind.Main)
                            runKind = AirCombatRouteKind.Main;
                        continue;
                    }

                    AddContinuousRoad(
                        result,
                        alongZ,
                        coordinate,
                        runFrom,
                        runTo,
                        runWidth,
                        runLaneTiles,
                        runKind,
                        runIndex++);
                    runFrom = next.from;
                    runTo = next.to;
                    runWidth = next.road.width;
                    runLaneTiles = next.road.laneTiles;
                    runKind = next.road.kind;
                }
                AddContinuousRoad(
                    result,
                    alongZ,
                    coordinate,
                    runFrom,
                    runTo,
                    runWidth,
                    runLaneTiles,
                    runKind,
                    runIndex);
            }
            return result;
        }

        static bool TryResolveRoadAxis(
            AirCombatRoadStrip road,
            out bool alongZ,
            out float coordinate,
            out float from,
            out float to)
        {
            alongZ = false;
            coordinate = 0f;
            from = 0f;
            to = 0f;
            if (road == null || road.dangerLane)
                return false;

            Vector3 delta = Vector3.ProjectOnPlane(
                road.end - road.start,
                Vector3.up);
            if (delta.sqrMagnitude < 1f)
                return false;
            alongZ = Mathf.Abs(delta.z) >= Mathf.Abs(delta.x);
            float crossAxisDelta = alongZ
                ? Mathf.Abs(delta.x)
                : Mathf.Abs(delta.z);
            if (crossAxisDelta > 0.25f)
                return false;

            coordinate = alongZ
                ? (road.start.x + road.end.x) * 0.5f
                : (road.start.z + road.end.z) * 0.5f;
            float first = alongZ ? road.start.z : road.start.x;
            float second = alongZ ? road.end.z : road.end.x;
            from = Mathf.Min(first, second);
            to = Mathf.Max(first, second);
            return true;
        }

        static void AddContinuousRoad(
            List<AirCombatRoadStrip> result,
            bool alongZ,
            float coordinate,
            float from,
            float to,
            float width,
            int laneTiles,
            AirCombatRouteKind kind,
            int runIndex)
        {
            if (to - from < 1f)
                return;
            string axis = alongZ ? "ns" : "ew";
            int coordinateId = Mathf.RoundToInt(coordinate * 10f);
            result.Add(new AirCombatRoadStrip
            {
                stableId = "continuous-gale-road-" + axis + "-" +
                           coordinateId + "-" + runIndex.ToString("D2"),
                start = alongZ
                    ? new Vector3(coordinate, 0f, from)
                    : new Vector3(from, 0f, coordinate),
                end = alongZ
                    ? new Vector3(coordinate, 0f, to)
                    : new Vector3(to, 0f, coordinate),
                width = width,
                laneTiles = laneTiles,
                kind = kind,
                dangerLane = false
            });
        }

        struct RoadInterval
        {
            public AirCombatRoadStrip road;
            public float from;
            public float to;
        }

        void BuildNaturalStreetGale(
            WindTrapCandidate candidate,
            AirCombatCitySettings settings,
            int seed)
        {
            AirCombatRoadStrip road = candidate.road;
            if (road == null)
                return;
            Vector3 delta = Vector3.ProjectOnPlane(
                candidate.direction,
                Vector3.up);
            float length = Vector3.Distance(road.start, road.end);
            if (length < 1f || delta.sqrMagnitude < 0.5f)
                return;
            Vector3 direction = delta.normalized;
            var fieldObject = new GameObject(
                "NaturalStreetGale_自然狂风_" + road.stableId);
            fieldObject.transform.SetParent(fieldRoot, false);
            fieldObject.transform.localPosition = new Vector3(
                (road.start.x + road.end.x) * 0.5f,
                0f,
                (road.start.z + road.end.z) * 0.5f);
            fieldObject.transform.localRotation = Quaternion.LookRotation(
                direction,
                Vector3.up);
            UrbanEnvironmentalFieldVolume field =
                fieldObject.AddComponent<UrbanEnvironmentalFieldVolume>();
            field.SetOwner(this);
            field.ConfigureNaturalWind(
                new Vector3(
                    Mathf.Clamp(road.width * 0.90f, 18f, 104f),
                    // The legal combat ceiling is the finite top of the gale.
                    // A flying player may trade street cover for altitude but
                    // cannot disable the weather by crossing an arbitrary box.
                    settings.maximumAltitude,
                    length),
                seed ^ Mathf.RoundToInt(
                    road.start.x * 17f + road.start.z * 31f +
                    road.end.x * 43f + road.end.z * 59f));
            fields.Add(field);

            float halfWidth = field.LocalSize.x * 0.5f;
            float routeAltitude = Mathf.Clamp(
                settings.maximumAltitude * 0.18f,
                34f,
                72f);
            Vector3 feederWorld = fieldRoot.TransformPoint(
                candidate.sideEntry);
            Vector3 feederLocal = field.transform.InverseTransformPoint(
                feederWorld);
            Vector3 impactWorld = fieldRoot.TransformPoint(
                candidate.impactPoint);
            Vector3 impactLocal = field.transform.InverseTransformPoint(
                impactWorld);
            float entryZ = Mathf.Clamp(
                feederLocal.z,
                -field.LocalSize.z * 0.32f,
                field.LocalSize.z * 0.18f);
            float side = candidate.sideSign < 0f ? -1f : 1f;
            Vector3 staging = new Vector3(
                side * (halfWidth + 76f),
                routeAltitude,
                entryZ);
            Vector3 entry = new Vector3(
                side * (halfWidth + 7f),
                routeAltitude,
                entryZ);
            Vector3 capture = new Vector3(
                side * Mathf.Min(6f, halfWidth * 0.18f),
                routeAltitude,
                entryZ + 18f);
            Vector3 impactApproach = new Vector3(
                Mathf.Clamp(impactLocal.x, -halfWidth * 0.3f, halfWidth * 0.3f),
                routeAltitude,
                Mathf.Clamp(
                    impactLocal.z - 24f,
                    capture.z + 24f,
                    field.LocalSize.z * 0.5f - 10f));
            pursuitRoutes.Add(new UrbanEnvironmentalPursuitRouteDescriptor
            {
                stableId = "environment.wind." + road.stableId,
                kind = UrbanEnvironmentalFieldKind.NaturalStreetGale,
                field = field,
                worldWaypoints = new[]
                {
                    field.transform.TransformPoint(staging),
                    field.transform.TransformPoint(entry),
                    field.transform.TransformPoint(capture),
                    field.transform.TransformPoint(impactApproach)
                },
                worldImpactPoint = impactWorld,
                localApproachCenter = new Vector3(
                    side * (halfWidth + 28f),
                    field.LocalSize.y * 0.38f,
                    entryZ),
                localApproachSize = new Vector3(
                    field.LocalSize.x + 176f,
                    field.LocalSize.y + 120f,
                    Mathf.Min(300f, field.LocalSize.z * 0.58f)),
                capacity = Mathf.Max(
                    1,
                    Mathf.FloorToInt(field.LocalSize.x / 24f)),
                allowReverse = false
            });
        }

        void BuildMagneticCourtyard(
            AirCombatCityPlan plan,
            AirCombatTacticalVolume volume,
            AirCombatCitySettings settings,
            int stableIndex)
        {
            // The authored recovery courtyard opens away from the city centre.
            // Using -volume.center reversed the real entrance and made the
            // runtime field disagree with its three surrounding buildings.
            Vector3 opening = Vector3.ProjectOnPlane(
                volume.center,
                Vector3.up);
            if (opening.sqrMagnitude < 0.01f)
                opening = Vector3.forward;
            opening.Normalize();
            var fieldObject = new GameObject(
                "MagneticCourtyard_三面磁场_" + stableIndex.ToString("D2"));
            fieldObject.transform.SetParent(fieldRoot, false);
            fieldObject.transform.localPosition = new Vector3(
                volume.center.x,
                0f,
                volume.center.z);
            fieldObject.transform.localRotation = Quaternion.LookRotation(
                opening,
                Vector3.up);

            List<UrbanMagneticWallSurface> wallSurfaces =
                ResolveMagneticWallSurfaces(
                    plan,
                    volume,
                    stableIndex,
                    fieldObject.transform);
            if (wallSurfaces.Count != 3)
            {
                Debug.LogWarning(
                    "[UrbanEnvironment] 三面磁场没有找到三栋真实庭院建筑，" +
                    "已拒绝生成抽象结界：" + volume.stableId,
                    this);
                if (Application.isPlaying)
                    Destroy(fieldObject);
                else
                    DestroyImmediate(fieldObject);
                return;
            }

            float tallestWall = 0f;
            for (int index = 0; index < wallSurfaces.Count; index++)
                tallestWall = Mathf.Max(tallestWall, wallSurfaces[index].height);
            UrbanEnvironmentalFieldVolume field =
                fieldObject.AddComponent<UrbanEnvironmentalFieldVolume>();
            field.SetOwner(this);
            field.ConfigureMagneticCourtyard(
                new Vector3(
                    Mathf.Clamp(volume.size.x * 0.90f, 72f, 132f),
                    Mathf.Clamp(tallestWall, 90f, settings.maximumAltitude),
                    Mathf.Clamp(volume.size.z * 0.90f, 68f, 126f)),
                wallSurfaces);
            fields.Add(field);

            float routeAltitude = Mathf.Clamp(
                field.LocalSize.y * 0.32f,
                28f,
                68f);
            float halfDepth = field.LocalSize.z * 0.5f;
            Vector3 staging = new Vector3(
                0f,
                routeAltitude,
                halfDepth + 78f);
            Vector3 entrance = new Vector3(
                0f,
                routeAltitude,
                halfDepth + 8f);
            Vector3 capture = new Vector3(
                0f,
                routeAltitude,
                Mathf.Min(10f, halfDepth * 0.15f));
            int routeCapacity = Mathf.Max(
                1,
                Mathf.FloorToInt(field.LocalSize.x / 24f));
            pursuitRoutes.Add(new UrbanEnvironmentalPursuitRouteDescriptor
            {
                stableId = "environment.magnet." +
                           (volume.stableId ?? stableIndex.ToString("D2")),
                kind = UrbanEnvironmentalFieldKind.MagneticCourtyard,
                field = field,
                worldWaypoints = new[]
                {
                    field.transform.TransformPoint(staging),
                    field.transform.TransformPoint(entrance),
                    field.transform.TransformPoint(capture)
                },
                worldImpactPoint = field.transform.TransformPoint(capture),
                localApproachCenter = new Vector3(
                    0f,
                    field.LocalSize.y * 0.42f,
                    halfDepth * 0.45f),
                localApproachSize = new Vector3(
                    field.LocalSize.x + 100f,
                    field.LocalSize.y + 180f,
                    field.LocalSize.z + 190f),
                capacity = routeCapacity,
                allowReverse = false
            });
        }

        static List<UrbanMagneticWallSurface> ResolveMagneticWallSurfaces(
            AirCombatCityPlan plan,
            AirCombatTacticalVolume volume,
            int courtyardIndex,
            Transform fieldTransform)
        {
            var result = new List<UrbanMagneticWallSurface>(3);
            if (plan == null || volume == null || fieldTransform == null)
                return result;

            string prefix = "building.recovery." + courtyardIndex + ".";
            var buildings = new List<AirCombatBuildingLot>(3);
            for (int index = 0; index < plan.buildings.Count; index++)
            {
                AirCombatBuildingLot building = plan.buildings[index];
                if (building != null &&
                    building.stableId.StartsWith(
                        prefix,
                        StringComparison.Ordinal))
                {
                    buildings.Add(building);
                }
            }

            // Generator cluster ids preserve the same semantic relation for
            // older plans whose stable labels may have been stripped. Never
            // fall back to arbitrary nearby buildings: that recreates a field
            // box instead of three real magnetic facades.
            if (buildings.Count != 3)
            {
                buildings.Clear();
                int clusterId = 970 + courtyardIndex;
                for (int index = 0; index < plan.buildings.Count; index++)
                {
                    AirCombatBuildingLot building = plan.buildings[index];
                    if (building != null && building.clusterId == clusterId)
                        buildings.Add(building);
                }
            }
            if (buildings.Count != 3)
                return result;

            for (int index = 0; index < buildings.Count; index++)
            {
                UrbanMagneticWallSurface surface;
                if (!TryResolveInwardFacade(
                        buildings[index],
                        volume.center,
                        fieldTransform,
                        out surface))
                {
                    result.Clear();
                    return result;
                }
                result.Add(surface);
            }
            return result;
        }

        static bool TryResolveInwardFacade(
            AirCombatBuildingLot building,
            Vector3 courtyardCenter,
            Transform fieldTransform,
            out UrbanMagneticWallSurface surface)
        {
            surface = null;
            if (building == null || fieldTransform == null)
                return false;

            Quaternion rotation = Quaternion.Euler(0f, building.yaw, 0f);
            Vector3 right = rotation * Vector3.right;
            Vector3 forward = rotation * Vector3.forward;
            float halfX = building.size.x * 0.5f;
            float halfZ = building.size.z * 0.5f;
            Vector3[] centers =
            {
                building.center + right * halfX,
                building.center - right * halfX,
                building.center + forward * halfZ,
                building.center - forward * halfZ
            };
            Vector3[] normals = { right, -right, forward, -forward };
            float[] widths =
            {
                building.size.z,
                building.size.z,
                building.size.x,
                building.size.x
            };

            int selected = -1;
            float selectedDistance = float.PositiveInfinity;
            for (int index = 0; index < centers.Length; index++)
            {
                Vector3 toCourtyard = Vector3.ProjectOnPlane(
                    courtyardCenter - centers[index],
                    Vector3.up);
                if (toCourtyard.sqrMagnitude < 0.01f ||
                    Vector3.Dot(normals[index], toCourtyard.normalized) < 0.85f)
                {
                    continue;
                }
                float distance = toCourtyard.sqrMagnitude;
                if (distance >= selectedDistance)
                    continue;
                selected = index;
                selectedDistance = distance;
            }
            if (selected < 0)
                return false;

            Transform cityRoot = fieldTransform.parent;
            Vector3 facadeWorldCenter = cityRoot != null
                ? cityRoot.TransformPoint(centers[selected])
                : centers[selected];
            Vector3 facadeWorldNormal = cityRoot != null
                ? cityRoot.TransformDirection(normals[selected])
                : normals[selected];
            surface = new UrbanMagneticWallSurface
            {
                stableId = building.stableId,
                localCenter = fieldTransform.InverseTransformPoint(
                    facadeWorldCenter),
                localInwardNormal = fieldTransform.InverseTransformDirection(
                    facadeWorldNormal).normalized,
                width = widths[selected],
                height = building.size.y
            };
            return true;
        }

        internal bool CanBeginCycle(UrbanEnvironmentalFieldVolume requester)
        {
            for (int index = 0; index < fields.Count; index++)
            {
                UrbanEnvironmentalFieldVolume field = fields[index];
                if (field == null || field == requester)
                    continue;
                if (field.State == UrbanEnvironmentalFieldState.Warning ||
                    field.State == UrbanEnvironmentalFieldState.Active)
                {
                    return false;
                }
            }
            return true;
        }

        void ClearGeneratedFields()
        {
            fields.Clear();
            pursuitRoutes.Clear();
            Transform existing = transform.Find(FieldRootName);
            if (existing == null)
                return;
            if (Application.isPlaying)
                Destroy(existing.gameObject);
            else
                DestroyImmediate(existing.gameObject);
            fieldRoot = null;
        }
    }

    [DisallowMultipleComponent]
    public sealed class UrbanEnvironmentalFieldVolume : MonoBehaviour
    {
        sealed class CaptureRecord
        {
            public Vector3 lastSafePosition;
            public Quaternion lastSafeRotation;
            public bool hasSafePose;
            public int wallIndex = -1;
            public float contactSlotOffset;
        }

        sealed class MagneticVisualTile
        {
            public Renderer renderer;
            public Vector3 localCenter;
            public Vector3 localInwardNormal;
            public Vector3 localTangent;
            public float halfWidth;
            public float halfHeight;
        }

        const int OverlapCapacity = 192;
        const int RayCapacity = 32;
        readonly Collider[] overlapBuffer = new Collider[OverlapCapacity];
        readonly RaycastHit[] rayBuffer = new RaycastHit[RayCapacity];
        readonly HashSet<int> sampledBodyIds = new HashSet<int>();
        readonly HashSet<int> assignedPursuerBodyIds = new HashSet<int>();
        readonly List<Rigidbody> sampledBodies = new List<Rigidbody>(48);
        readonly List<Renderer> stateRenderers = new List<Renderer>(32);
        readonly List<LineRenderer> windLines = new List<LineRenderer>(9);
        readonly List<BoxCollider> magneticPanels = new List<BoxCollider>(3);
        readonly List<Light> magneticWallLights = new List<Light>(3);
        readonly List<MagneticVisualTile> magneticVisualTiles =
            new List<MagneticVisualTile>(112);
        readonly List<MeshCollider> magneticVisualClipMeshes =
            new List<MeshCollider>(12);
        readonly List<UrbanMagneticWallSurface> magneticWalls =
            new List<UrbanMagneticWallSurface>(3);
        readonly Dictionary<Rigidbody, CaptureRecord> captures =
            new Dictionary<Rigidbody, CaptureRecord>();

        UrbanEnvironmentalFieldKind kind;
        UrbanEnvironmentalFieldState state;
        Vector3 localSize;
        float nextStateAt;
        int stableSeed;
        float forceMultiplier = 1f;
        UrbanEnvironmentalFieldControlMode controlMode;
        ParticleSystem windParticles;
        ParticleSystem windDetailParticles;
        Material fieldMaterial;
        Material particleMaterial;
        Material detailParticleMaterial;
        Material trailParticleMaterial;
        Texture2D particleTexture;
        Texture2D trailParticleTexture;
        bool ownsParticleTexture;
        MaterialPropertyBlock propertyBlock;
        UrbanEnvironmentalFieldDirector owner;
        bool magneticVisualClippingComplete;
        float nextMagneticVisualClipAt;
        float magneticVisualClipDeadline;

        public UrbanEnvironmentalFieldKind Kind => kind;
        public UrbanEnvironmentalFieldState State => state;
        public UrbanEnvironmentalFieldControlMode ControlMode => controlMode;
        public Vector3 LocalSize => localSize;
        public IReadOnlyList<UrbanMagneticWallSurface> MagneticWalls =>
            magneticWalls;
        public int MagneticPanelCount => magneticPanels.Count;
        public float ForceMultiplier => forceMultiplier;
        public float RemainingStateSeconds => float.IsPositiveInfinity(nextStateAt)
            ? float.PositiveInfinity
            : Mathf.Max(0f, nextStateAt - Time.time);

        internal void SetOwner(UrbanEnvironmentalFieldDirector value)
        {
            owner = value;
        }

        public void ConfigureNaturalWind(Vector3 size, int seed)
        {
            kind = UrbanEnvironmentalFieldKind.NaturalStreetGale;
            localSize = SanitizeSize(size);
            stableSeed = seed;
            controlMode = UrbanEnvironmentalFieldControlMode.Automatic;
            state = UrbanEnvironmentalFieldState.Dormant;
            nextStateAt = Time.time + 4.5f +
                          Mathf.Abs(seed % 37) / 37f * 3.5f;
            BuildWindPresentation();
            SetVisualState();
        }

        public void ConfigureMagneticCourtyard(
            Vector3 size,
            IReadOnlyList<UrbanMagneticWallSurface> wallSurfaces)
        {
            kind = UrbanEnvironmentalFieldKind.MagneticCourtyard;
            localSize = SanitizeSize(size);
            magneticWalls.Clear();
            if (wallSurfaces != null)
            {
                for (int index = 0; index < wallSurfaces.Count; index++)
                {
                    UrbanMagneticWallSurface source = wallSurfaces[index];
                    if (source == null || source.width < 2f ||
                        source.height < 2f)
                    {
                        continue;
                    }
                    Vector3 normal = Vector3.ProjectOnPlane(
                        source.localInwardNormal,
                        Vector3.up);
                    if (normal.sqrMagnitude < 0.5f)
                        continue;
                    magneticWalls.Add(new UrbanMagneticWallSurface
                    {
                        stableId = source.stableId,
                        localCenter = source.localCenter,
                        localInwardNormal = normal.normalized,
                        width = source.width,
                        height = source.height
                    });
                }
            }
            controlMode = UrbanEnvironmentalFieldControlMode.Automatic;
            state = UrbanEnvironmentalFieldState.Dormant;
            nextStateAt = float.PositiveInfinity;
            if (magneticWalls.Count == 3)
                BuildMagneticPresentation();
            else
                enabled = false;
            SetVisualState();
        }

        void FixedUpdate()
        {
            if (!Application.isPlaying)
                return;
            SampleCombatBodies();
            if (controlMode == UrbanEnvironmentalFieldControlMode.Automatic)
            {
                UpdateState();
            }
            else
            {
                UrbanEnvironmentalFieldState forcedState =
                    controlMode ==
                    UrbanEnvironmentalFieldControlMode.ForcedActive
                        ? UrbanEnvironmentalFieldState.Active
                        : UrbanEnvironmentalFieldState.Dormant;
                if (state != forcedState)
                    EnterState(forcedState, Time.time);
            }
            if (state != UrbanEnvironmentalFieldState.Active)
                return;
            if (kind == UrbanEnvironmentalFieldKind.NaturalStreetGale)
                ApplyNaturalWind();
            else
                ApplyMagneticAttraction();
        }

        public void SetControlMode(
            UrbanEnvironmentalFieldControlMode mode)
        {
            if (controlMode == mode &&
                (mode == UrbanEnvironmentalFieldControlMode.Automatic ||
                 state == (mode ==
                     UrbanEnvironmentalFieldControlMode.ForcedActive
                         ? UrbanEnvironmentalFieldState.Active
                         : UrbanEnvironmentalFieldState.Dormant)))
            {
                return;
            }

            controlMode = mode;
            if (mode == UrbanEnvironmentalFieldControlMode.ForcedActive)
            {
                EnterState(UrbanEnvironmentalFieldState.Active, Time.time);
                return;
            }

            ReleaseCapturedBodies();
            EnterState(UrbanEnvironmentalFieldState.Dormant, Time.time);
        }

        public void SetForceMultiplier(float multiplier)
        {
            forceMultiplier = Mathf.Clamp(multiplier, 0.25f, 2.5f);
        }

        public void NotifyPursuitAssigned(
            Rigidbody enemyBody,
            float estimatedArrivalSeconds)
        {
            if (enemyBody == null)
                return;
            assignedPursuerBodyIds.Add(enemyBody.GetInstanceID());
            if (kind != UrbanEnvironmentalFieldKind.MagneticCourtyard ||
                state != UrbanEnvironmentalFieldState.Warning)
            {
                return;
            }
            float warning = Mathf.Clamp(
                estimatedArrivalSeconds + 0.4f,
                UrbanEnvironmentalFieldPolicy.MagnetWarningSeconds,
                4.5f);
            nextStateAt = Mathf.Max(nextStateAt, Time.time + warning);
        }

        public void NotifyPursuitReleased(Rigidbody enemyBody)
        {
            if (enemyBody != null)
                assignedPursuerBodyIds.Remove(enemyBody.GetInstanceID());
        }

        void Update()
        {
            if (!Application.isPlaying ||
                kind != UrbanEnvironmentalFieldKind.MagneticCourtyard ||
                fieldMaterial == null)
            {
                return;
            }

            if (!magneticVisualClippingComplete &&
                Time.time >= nextMagneticVisualClipAt)
            {
                TryClipMagneticVisualsToRealFacades();
            }

            float scrollSpeed = state == UrbanEnvironmentalFieldState.Active
                ? 0.17f
                : state == UrbanEnvironmentalFieldState.Warning
                    ? 0.09f
                    : 0.018f;
            Vector2 textureOffset = new Vector2(
                Mathf.Repeat(Time.time * scrollSpeed, 1f),
                Mathf.Repeat(Time.time * scrollSpeed * 0.37f, 1f));
            if (fieldMaterial.HasProperty("_MainTex"))
                fieldMaterial.SetTextureOffset("_MainTex", textureOffset);
            if (fieldMaterial.HasProperty("_BaseMap"))
                fieldMaterial.SetTextureOffset("_BaseMap", textureOffset);

            float pulse = state == UrbanEnvironmentalFieldState.Active
                ? Mathf.Lerp(1.05f, 1.65f,
                    0.5f + 0.5f * Mathf.Sin(Time.time * 5.8f))
                : state == UrbanEnvironmentalFieldState.Warning
                    ? Mathf.Lerp(0.78f, 1.38f,
                        0.5f + 0.5f * Mathf.Sin(Time.time * 8.4f))
                    : Mathf.Lerp(0.56f, 0.76f,
                        0.5f + 0.5f * Mathf.Sin(Time.time * 1.6f));
            ApplyStateRendererColor(ResolveVisualColor(), pulse);
        }

        public bool TryResolveEnemyLure(
            Vector3 playerPosition,
            Vector3 enemyPosition,
            int stableIndex,
            out Vector3 destination)
        {
            destination = Vector3.zero;
            if (state != UrbanEnvironmentalFieldState.Warning &&
                state != UrbanEnvironmentalFieldState.Active)
            {
                return false;
            }
            Vector3 localPlayer = transform.InverseTransformPoint(
                playerPosition);
            float horizontalPadding = kind ==
                UrbanEnvironmentalFieldKind.MagneticCourtyard
                    ? 34f
                    : 26f;
            if (Mathf.Abs(localPlayer.x) > localSize.x * 0.5f +
                horizontalPadding ||
                Mathf.Abs(localPlayer.z) > localSize.z * 0.5f +
                horizontalPadding ||
                localPlayer.y < -20f ||
                localPlayer.y > localSize.y +
                    (kind == UrbanEnvironmentalFieldKind.MagneticCourtyard
                        ? 130f
                        : 45f) ||
                Vector3.Distance(enemyPosition, playerPosition) >
                UrbanEnvironmentalFieldPolicy.MaximumLureDistance)
            {
                return false;
            }

            if (kind == UrbanEnvironmentalFieldKind.NaturalStreetGale)
            {
                destination = transform.TransformPoint(new Vector3(
                    Mathf.Clamp(
                        localPlayer.x,
                        -localSize.x * 0.34f,
                        localSize.x * 0.34f),
                    Mathf.Clamp(localPlayer.y, 18f, localSize.y * 0.72f),
                    Mathf.Clamp(
                        localPlayer.z,
                        -localSize.z * 0.42f,
                        localSize.z * 0.42f)));
            }
            else
            {
                float side = (stableIndex & 2) == 0 ? -1f : 1f;
                destination = transform.TransformPoint(new Vector3(
                    side * Mathf.Min(14f, localSize.x * 0.18f),
                    Mathf.Clamp(localPlayer.y, 22f, localSize.y * 0.58f),
                    localSize.z * 0.05f));
            }
            return true;
        }

        void UpdateState()
        {
            float now = Time.time;
            if (kind == UrbanEnvironmentalFieldKind.MagneticCourtyard &&
                state == UrbanEnvironmentalFieldState.Dormant)
            {
                if (ContainsPlayer() &&
                    (owner == null || owner.CanBeginCycle(this)))
                    EnterState(UrbanEnvironmentalFieldState.Warning, now);
                return;
            }
            if (kind == UrbanEnvironmentalFieldKind.MagneticCourtyard &&
                state == UrbanEnvironmentalFieldState.Warning &&
                ContainsAssignedEnemy())
            {
                EnterState(UrbanEnvironmentalFieldState.Active, now);
                return;
            }
            if (now < nextStateAt)
                return;

            switch (state)
            {
                case UrbanEnvironmentalFieldState.Dormant:
                    if (owner != null && !owner.CanBeginCycle(this))
                    {
                        nextStateAt = now + 1f;
                        break;
                    }
                    EnterState(UrbanEnvironmentalFieldState.Warning, now);
                    break;
                case UrbanEnvironmentalFieldState.Warning:
                    EnterState(UrbanEnvironmentalFieldState.Active, now);
                    break;
                case UrbanEnvironmentalFieldState.Active:
                    ReleaseCapturedBodies();
                    EnterState(UrbanEnvironmentalFieldState.Cooldown, now);
                    break;
                default:
                    if (kind ==
                        UrbanEnvironmentalFieldKind.MagneticCourtyard)
                    {
                        EnterState(UrbanEnvironmentalFieldState.Dormant, now);
                    }
                    else if (owner == null || owner.CanBeginCycle(this))
                    {
                        EnterState(UrbanEnvironmentalFieldState.Warning, now);
                    }
                    else
                    {
                        nextStateAt = now + 1f;
                    }
                    break;
            }
        }

        void EnterState(UrbanEnvironmentalFieldState next, float now)
        {
            state = next;
            if (state == UrbanEnvironmentalFieldState.Cooldown ||
                state == UrbanEnvironmentalFieldState.Dormant)
            {
                assignedPursuerBodyIds.Clear();
            }
            switch (state)
            {
                case UrbanEnvironmentalFieldState.Warning:
                    nextStateAt = now +
                        (kind == UrbanEnvironmentalFieldKind.MagneticCourtyard
                            ? UrbanEnvironmentalFieldPolicy.MagnetWarningSeconds
                            : UrbanEnvironmentalFieldPolicy.WindWarningSeconds);
                    break;
                case UrbanEnvironmentalFieldState.Active:
                    nextStateAt = now +
                        (kind == UrbanEnvironmentalFieldKind.MagneticCourtyard
                            ? UrbanEnvironmentalFieldPolicy.MagnetActiveSeconds
                            : UrbanEnvironmentalFieldPolicy.WindActiveSeconds);
                    break;
                case UrbanEnvironmentalFieldState.Cooldown:
                    nextStateAt = now +
                        (kind == UrbanEnvironmentalFieldKind.MagneticCourtyard
                            ? UrbanEnvironmentalFieldPolicy.MagnetCooldownSeconds
                            : UrbanEnvironmentalFieldPolicy.WindCooldownSeconds);
                    break;
                default:
                    nextStateAt = kind ==
                        UrbanEnvironmentalFieldKind.MagneticCourtyard
                            ? float.PositiveInfinity
                            : now + 5f;
                    break;
            }
            SetVisualState();
        }

        void SampleCombatBodies()
        {
            sampledBodies.Clear();
            sampledBodyIds.Clear();
            Vector3 lossy = transform.lossyScale;
            Vector3 half = Vector3.Scale(
                localSize * 0.5f,
                new Vector3(
                    Mathf.Abs(lossy.x),
                    Mathf.Abs(lossy.y),
                    Mathf.Abs(lossy.z)));
            Vector3 center = transform.TransformPoint(
                Vector3.up * localSize.y * 0.5f);
            int count = Physics.OverlapBoxNonAlloc(
                center,
                half,
                overlapBuffer,
                transform.rotation,
                ~0,
                QueryTriggerInteraction.Ignore);
            for (int index = 0; index < count; index++)
            {
                Collider collider = overlapBuffer[index];
                Rigidbody body = collider != null
                    ? collider.attachedRigidbody
                    : null;
                if (body == null || body.isKinematic ||
                    !body.gameObject.activeInHierarchy ||
                    !sampledBodyIds.Add(body.GetInstanceID()) ||
                    !IsCombatVehicle(body))
                {
                    continue;
                }
                sampledBodies.Add(body);
            }
        }

        static bool IsCombatVehicle(Rigidbody body)
        {
            if (body.GetComponent<VehicleCombatTeamMarker>() != null)
                return true;
            return body.GetComponent<RobocraftMotionCoordinator>() != null ||
                   body.GetComponent<HordeEnemyVehicle>() != null ||
                   body.GetComponent<ModularBossCombatRuntime>() != null;
        }

        bool ContainsPlayer()
        {
            for (int index = 0; index < sampledBodies.Count; index++)
            {
                VehicleCombatTeamMarker marker = sampledBodies[index]
                    .GetComponent<VehicleCombatTeamMarker>();
                if (marker != null && marker.Team == VehicleCombatTeam.Player)
                    return true;
            }
            return false;
        }

        bool ContainsAssignedEnemy()
        {
            for (int index = 0; index < sampledBodies.Count; index++)
            {
                Rigidbody body = sampledBodies[index];
                if (body != null && assignedPursuerBodyIds.Contains(
                        body.GetInstanceID()))
                {
                    return true;
                }
            }
            return false;
        }

        void ApplyNaturalWind()
        {
            Vector3 windDirection = transform.forward;
            for (int index = 0; index < sampledBodies.Count; index++)
            {
                Rigidbody body = sampledBodies[index];
                if (body == null || body.isKinematic)
                    continue;
                Vector3 localPosition = transform.InverseTransformPoint(
                    body.worldCenterOfMass);
                float edgeDistance = Mathf.Min(
                    localSize.x * 0.5f - Mathf.Abs(localPosition.x),
                    localSize.z * 0.5f - Mathf.Abs(localPosition.z));
                float boundary = Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(0f, 14f, edgeDistance));
                float upstreamDistance = Mathf.Clamp(
                    localPosition.z + localSize.z * 0.5f,
                    2f,
                    localSize.z);
                float occlusion = ResolveWindOcclusion(
                                      body,
                                      windDirection,
                                      upstreamDistance) *
                                  boundary;
                if (occlusion <= 0.03f)
                    continue;
                RobocraftMotionCoordinator motion =
                    body.GetComponent<RobocraftMotionCoordinator>();
                float gust = 0.82f + 0.18f * Mathf.Sin(
                    Time.fixedTime * 5.1f +
                    body.GetInstanceID() * 0.017f + stableSeed * 0.001f);
                float force = UrbanEnvironmentalFieldPolicy.ResolveWindForce(
                    body,
                    motion,
                    windDirection) * forceMultiplier * occlusion * gust;
                Vector3 turbulence = transform.up *
                    Mathf.Sin(Time.fixedTime * 3.7f + index) * 0.08f;
                Vector3 impulse = (windDirection + turbulence).normalized *
                                  force * Time.fixedDeltaTime;
                Vector3 applicationPoint = body.worldCenterOfMass +
                    transform.up * Mathf.Clamp(
                        EstimateBodyRadius(body, transform.up) * 0.28f,
                        0.15f,
                        1.8f);
                VehicleExternalForces.ApplyImpulseInCurrentPhysicsStep(
                    body,
                    impulse,
                    applicationPoint);

                float impactScale = motion == null
                    ? 1f
                    : UrbanEnvironmentalFieldPolicy.ResolvePlayerForceScale(
                        motion.Telemetry,
                        motion.CoreAssistMode,
                        UrbanEnvironmentalFieldKind.NaturalStreetGale,
                        motion.transform.InverseTransformDirection(
                            windDirection));
                HordeEnemyVehicle horde =
                    body.GetComponent<HordeEnemyVehicle>();
                horde?.ArmEnvironmentalTrapImpact(
                    impactScale,
                    gameObject);
                UrbanEnvironmentalImpactReceiver receiver =
                    body.GetComponent<UrbanEnvironmentalImpactReceiver>() ??
                    body.gameObject.AddComponent<
                        UrbanEnvironmentalImpactReceiver>();
                receiver.ArmWindImpact(impactScale, gameObject);
            }
        }

        float ResolveWindOcclusion(
            Rigidbody body,
            Vector3 direction,
            float upstreamDistance)
        {
            Vector3 target = body.worldCenterOfMass;
            float probeDistance = Mathf.Max(2f, upstreamDistance);
            Vector3 side = Vector3.Cross(transform.up, direction).normalized;
            float total = 0f;
            for (int sample = -1; sample <= 1; sample++)
            {
                total += ResolveWindOcclusionRay(
                    body,
                    target + side * sample * 7f,
                    direction,
                    probeDistance);
            }
            return total / 3f;
        }

        float ResolveWindOcclusionRay(
            Rigidbody body,
            Vector3 target,
            Vector3 direction,
            float probeDistance)
        {
            Vector3 origin = target - direction * probeDistance;
            int count = Physics.RaycastNonAlloc(
                origin,
                direction,
                rayBuffer,
                probeDistance,
                ~0,
                QueryTriggerInteraction.Ignore);
            float nearestBlock = float.PositiveInfinity;
            for (int index = 0; index < count; index++)
            {
                Collider hit = rayBuffer[index].collider;
                if (hit == null ||
                    hit.attachedRigidbody == body ||
                    hit.transform.IsChildOf(transform) ||
                    (hit.attachedRigidbody != null &&
                     !hit.attachedRigidbody.isKinematic))
                {
                    continue;
                }
                nearestBlock = Mathf.Min(
                    nearestBlock,
                    rayBuffer[index].distance);
            }
            return float.IsPositiveInfinity(nearestBlock) ? 1f : 0.08f;
        }

        void ApplyMagneticAttraction()
        {
            for (int index = 0; index < sampledBodies.Count; index++)
            {
                Rigidbody body = sampledBodies[index];
                if (body == null || body.isKinematic)
                    continue;
                if (!captures.TryGetValue(body, out CaptureRecord record))
                {
                    record = new CaptureRecord
                    {
                        lastSafePosition = body.position,
                        lastSafeRotation = body.rotation,
                        hasSafePose = true,
                        contactSlotOffset =
                            ((body.GetInstanceID() & int.MaxValue) % 5 - 2) *
                            6f
                    };
                    captures.Add(body, record);
                }
                if (!IsPenetratingPanel(body))
                {
                    record.lastSafePosition = body.position;
                    record.lastSafeRotation = body.rotation;
                    record.hasSafePose = true;
                }

                Vector3 localPosition = transform.InverseTransformPoint(
                    body.worldCenterOfMass);
                Vector3 target = ResolveMagneticTarget(
                    body,
                    localPosition,
                    record);
                Vector3 worldTarget = transform.TransformPoint(target);
                Vector3 delta = Vector3.ProjectOnPlane(
                    worldTarget - body.worldCenterOfMass,
                    transform.up);
                float distance = delta.magnitude;
                if (distance < 0.02f)
                    continue;
                Vector3 direction = delta / distance;
                RobocraftMotionCoordinator motion =
                    body.GetComponent<RobocraftMotionCoordinator>();
                float baseForce =
                    UrbanEnvironmentalFieldPolicy.ResolveMagneticForce(
                        motion,
                        direction) * forceMultiplier;
                float spring = Mathf.Clamp01(distance / 18f);
                float inwardSpeed = Vector3.Dot(body.velocity, direction);
                float damping = Mathf.Max(0f, inwardSpeed) * body.mass * 1.35f;
                Vector3 force = direction * Mathf.Max(
                    baseForce * spring - damping,
                    baseForce * 0.12f);

                if (distance < 8f)
                {
                    VehicleCombatTeamMarker marker =
                        body.GetComponent<VehicleCombatTeamMarker>();
                    float hold = marker != null &&
                                 marker.Team == VehicleCombatTeam.Enemy
                        ? 1.15f
                        : motion != null && motion.CoreAssistMode ==
                          VehicleCoreAssistMode.Training
                            ? 0.28f
                            : 0.52f;
                    Vector3 planarVelocity = Vector3.ProjectOnPlane(
                        body.velocity,
                        transform.up);
                    force += -planarVelocity * body.mass * hold;
                }
                force = Vector3.ClampMagnitude(force, baseForce * 1.25f);
                VehicleExternalForces.ApplyImpulseInCurrentPhysicsStep(
                    body,
                    force * Time.fixedDeltaTime,
                    body.worldCenterOfMass);
                body.GetComponent<HordeEnemyVehicle>()?.
                    NotifyEnvironmentalCapture(0.16f);
            }
        }

        Vector3 ResolveMagneticTarget(
            Rigidbody body,
            Vector3 localPosition,
            CaptureRecord record)
        {
            if (magneticWalls.Count != 3)
                return localPosition;
            if (record.wallIndex < 0 ||
                record.wallIndex >= magneticWalls.Count)
            {
                record.wallIndex = SelectMagneticWall(localPosition);
            }

            UrbanMagneticWallSurface wall = magneticWalls[record.wallIndex];
            Vector3 inward = wall.localInwardNormal.normalized;
            Vector3 tangent = Vector3.Cross(Vector3.up, inward).normalized;
            Vector3 worldInward = transform.TransformDirection(inward);
            Vector3 worldTangent = transform.TransformDirection(tangent);
            float normalSupport = EstimateBodyRadius(body, worldInward) + 1.2f;
            float tangentSupport = EstimateBodyRadius(body, worldTangent) + 1.4f;
            float verticalSupport = EstimateBodyRadius(body, transform.up) + 0.8f;
            float tangentLimit = Mathf.Max(
                0f,
                wall.width * 0.5f - tangentSupport);
            float minimumY = wall.localCenter.y - wall.height * 0.5f +
                             verticalSupport;
            float maximumY = wall.localCenter.y + wall.height * 0.5f -
                             verticalSupport;
            if (maximumY < minimumY)
            {
                float centerY = wall.localCenter.y;
                minimumY = centerY;
                maximumY = centerY;
            }

            Vector3 fromWallCenter = localPosition - wall.localCenter;
            float tangentOffset = Mathf.Clamp(
                Vector3.Dot(fromWallCenter, tangent) +
                record.contactSlotOffset,
                -tangentLimit,
                tangentLimit);
            // The target remains in front of the real facade on the courtyard
            // side.  No point is ever placed at the building centre or behind
            // the wall, so protruding modules approach a physical contact band
            // instead of being dragged through a force-field plane.
            return wall.localCenter + inward * normalSupport +
                   tangent * tangentOffset +
                   Vector3.up * Mathf.Clamp(
                       localPosition.y - wall.localCenter.y,
                       minimumY - wall.localCenter.y,
                       maximumY - wall.localCenter.y);
        }

        int SelectMagneticWall(Vector3 localPosition)
        {
            int selected = 0;
            float selectedScore = float.PositiveInfinity;
            for (int index = 0; index < magneticWalls.Count; index++)
            {
                UrbanMagneticWallSurface wall = magneticWalls[index];
                Vector3 inward = wall.localInwardNormal.normalized;
                Vector3 tangent = Vector3.Cross(Vector3.up, inward).normalized;
                Vector3 relative = localPosition - wall.localCenter;
                float planeDistance = Mathf.Abs(Vector3.Dot(relative, inward));
                float tangentOverflow = Mathf.Max(
                    0f,
                    Mathf.Abs(Vector3.Dot(relative, tangent)) -
                    wall.width * 0.5f);
                float verticalOverflow = Mathf.Max(
                    0f,
                    Mathf.Abs(relative.y) - wall.height * 0.5f);
                float score = planeDistance * planeDistance +
                              tangentOverflow * tangentOverflow +
                              verticalOverflow * verticalOverflow;
                if (score >= selectedScore)
                    continue;
                selected = index;
                selectedScore = score;
            }
            // CaptureRecord keeps this index for the entire activation. Three
            // walls are active for different vehicles, but one vehicle never
            // receives three opposing forces or oscillates at the centre.
            return selected;
        }

        static float EstimateBodyRadius(Rigidbody body, Vector3 worldNormal)
        {
            Collider[] colliders = body.GetComponentsInChildren<Collider>();
            Bounds combined = new Bounds(body.worldCenterOfMass, Vector3.zero);
            bool initialized = false;
            for (int index = 0; index < colliders.Length; index++)
            {
                Collider collider = colliders[index];
                if (collider == null || !collider.enabled || collider.isTrigger)
                    continue;
                if (!initialized)
                {
                    combined = collider.bounds;
                    initialized = true;
                }
                else
                {
                    combined.Encapsulate(collider.bounds);
                }
            }
            if (!initialized)
                return 2f;
            Vector3 n = new Vector3(
                Mathf.Abs(worldNormal.x),
                Mathf.Abs(worldNormal.y),
                Mathf.Abs(worldNormal.z));
            return Mathf.Clamp(Vector3.Dot(combined.extents, n), 1f, 12f);
        }

        void ReleaseCapturedBodies()
        {
            foreach (KeyValuePair<Rigidbody, CaptureRecord> item in captures)
            {
                Rigidbody body = item.Key;
                if (body == null || body.isKinematic)
                    continue;
                ResolvePanelPenetration(body, item.Value);
                Vector3 releaseDirection = ResolveReleaseDirection(
                    item.Value.wallIndex);
                float inwardSpeed = Vector3.Dot(
                    body.velocity,
                    releaseDirection);
                float cancelledInwardSpeed = Mathf.Max(0f, -inwardSpeed);
                Vector3 impulse = releaseDirection * body.mass *
                    (cancelledInwardSpeed + 1.8f);
                VehicleExternalForces.ApplyImpulseInCurrentPhysicsStep(
                    body,
                    impulse,
                    body.worldCenterOfMass);
            }
            captures.Clear();
        }

        Vector3 ResolveReleaseDirection(int wallIndex)
        {
            if (wallIndex >= 0 && wallIndex < magneticWalls.Count)
            {
                return transform.TransformDirection(
                    magneticWalls[wallIndex].localInwardNormal).normalized;
            }
            return transform.forward;
        }

        void ResolvePanelPenetration(
            Rigidbody body,
            CaptureRecord record)
        {
            for (int pass = 0; pass < 3; pass++)
            {
                Vector3 correction = Vector3.zero;
                Collider[] bodyColliders =
                    body.GetComponentsInChildren<Collider>();
                for (int bodyIndex = 0;
                     bodyIndex < bodyColliders.Length;
                     bodyIndex++)
                for (int panelIndex = 0;
                     panelIndex < magneticPanels.Count;
                     panelIndex++)
                {
                    Collider source = bodyColliders[bodyIndex];
                    Collider panel = magneticPanels[panelIndex];
                    if (source == null || panel == null ||
                        !source.enabled || source.isTrigger || !panel.enabled)
                    {
                        continue;
                    }
                    if (Physics.ComputePenetration(
                            source,
                            source.transform.position,
                            source.transform.rotation,
                            panel,
                            panel.transform.position,
                            panel.transform.rotation,
                            out Vector3 direction,
                            out float distance))
                    {
                        correction += direction * (distance + 0.08f);
                    }
                }
                if (correction.sqrMagnitude < 0.0001f)
                    return;
                body.position += Vector3.ClampMagnitude(correction, 8f);
                Physics.SyncTransforms();
            }
            if (IsPenetratingPanel(body) && record != null &&
                record.hasSafePose)
            {
                body.position = record.lastSafePosition;
                body.rotation = record.lastSafeRotation;
                Physics.SyncTransforms();
            }
        }

        bool IsPenetratingPanel(Rigidbody body)
        {
            Collider[] bodyColliders = body.GetComponentsInChildren<Collider>();
            for (int bodyIndex = 0;
                 bodyIndex < bodyColliders.Length;
                 bodyIndex++)
            for (int panelIndex = 0;
                 panelIndex < magneticPanels.Count;
                 panelIndex++)
            {
                Collider source = bodyColliders[bodyIndex];
                Collider panel = magneticPanels[panelIndex];
                if (source == null || panel == null ||
                    !source.enabled || source.isTrigger || !panel.enabled)
                {
                    continue;
                }
                if (Physics.ComputePenetration(
                        source,
                        source.transform.position,
                        source.transform.rotation,
                        panel,
                        panel.transform.position,
                        panel.transform.rotation,
                        out _,
                        out _))
                {
                    return true;
                }
            }
            return false;
        }

        void BuildWindPresentation()
        {
            const float TravelSpeed = 104f;
            float travelSeconds = Mathf.Max(1f, localSize.z / TravelSpeed);
            var particleObject = new GameObject(
                "ViolentGaleDustSheets_狂风卷尘");
            particleObject.transform.SetParent(transform, false);
            windParticles = particleObject.AddComponent<ParticleSystem>();
            windParticles.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = windParticles.main;
            main.loop = true;
            main.duration = Mathf.Max(5f, travelSeconds + 0.35f);
            main.prewarm = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = new ParticleSystem.MinMaxCurve(
                travelSeconds + 0.22f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(7f, 12f);
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);
            main.maxParticles = Mathf.Clamp(
                Mathf.CeilToInt(13f * (travelSeconds + 0.22f) * 1.25f),
                90,
                420);
            ParticleSystem.ShapeModule shape = windParticles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(
                localSize.x * 0.92f,
                localSize.y * 0.94f,
                2f);
            shape.position = Vector3.up * localSize.y * 0.50f +
                             Vector3.back * (localSize.z * 0.5f - 1f);
            ParticleSystem.VelocityOverLifetimeModule velocity =
                windParticles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            // Unity requires every Velocity over Lifetime axis to use the same curve mode.
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.z = new ParticleSystem.MinMaxCurve(
                TravelSpeed,
                TravelSpeed);
            ParticleSystem.NoiseModule noise = windParticles.noise;
            noise.enabled = true;
            noise.quality = ParticleSystemNoiseQuality.Medium;
            noise.strength = new ParticleSystem.MinMaxCurve(1.4f, 3.6f);
            noise.frequency = 0.22f;
            noise.scrollSpeed = 0.85f;
            noise.damping = true;
            noise.octaveCount = 2;
            ParticleSystem.ColorOverLifetimeModule colorOverLifetime =
                windParticles.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var windFade = new Gradient();
            windFade.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.72f, 0.16f),
                    new GradientAlphaKey(0.58f, 0.76f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(windFade);
            ParticleSystemRenderer renderer =
                particleObject.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.18f;
            renderer.lengthScale = 1.6f;
            renderer.maxParticleSize = 0.24f;
            particleMaterial = CreateParticleMaterial();
            renderer.sharedMaterial = particleMaterial;
            windParticles.Play();
            BuildWindDetailPresentation();
        }

        void BuildWindDetailPresentation()
        {
            const float TravelSpeed = 160f;
            float travelSeconds = Mathf.Max(0.75f, localSize.z / TravelSpeed);
            var detailObject = new GameObject(
                "ViolentGaleSpeedLines_HighFrequencyDetail");
            detailObject.transform.SetParent(transform, false);
            windDetailParticles = detailObject.AddComponent<ParticleSystem>();
            windDetailParticles.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = windDetailParticles.main;
            main.loop = true;
            main.duration = Mathf.Max(5f, travelSeconds + 0.22f);
            main.prewarm = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = new ParticleSystem.MinMaxCurve(
                travelSeconds + 0.14f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(3f, 6f);
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.10f, 0.10f);
            main.maxParticles = Mathf.Clamp(
                Mathf.CeilToInt(92f * (travelSeconds + 0.14f) * 1.15f),
                280,
                1400);

            ParticleSystem.ShapeModule shape = windDetailParticles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(
                localSize.x * 0.96f,
                localSize.y * 0.96f,
                1.6f);
            shape.position = Vector3.up * localSize.y * 0.50f +
                             Vector3.back * (localSize.z * 0.5f - 0.8f);

            ParticleSystem.VelocityOverLifetimeModule velocity =
                windDetailParticles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            // Keep all axes in TwoConstants mode. Mixing modes logs a Unity error.
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.z = new ParticleSystem.MinMaxCurve(
                TravelSpeed,
                TravelSpeed);

            ParticleSystem.NoiseModule noise = windDetailParticles.noise;
            noise.enabled = true;
            noise.quality = ParticleSystemNoiseQuality.Medium;
            noise.strength = new ParticleSystem.MinMaxCurve(0.25f, 0.90f);
            noise.frequency = 0.38f;
            noise.scrollSpeed = 1.15f;
            noise.damping = true;
            noise.octaveCount = 2;

            ParticleSystem.ColorOverLifetimeModule colorOverLifetime =
                windDetailParticles.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var detailFade = new Gradient();
            detailFade.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.80f, 0.12f),
                    new GradientAlphaKey(0.62f, 0.78f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(detailFade);

            ParticleSystem.TrailModule trails = windDetailParticles.trails;
            trails.enabled = true;
            trails.mode = ParticleSystemTrailMode.PerParticle;
            trails.ratio = 0.38f;
            trails.lifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.34f);
            trails.minVertexDistance = 0.7f;
            trails.dieWithParticles = false;
            trails.inheritParticleColor = true;
            trails.sizeAffectsWidth = true;
            trails.textureMode = ParticleSystemTrailTextureMode.Stretch;
            trails.widthOverTrail = new ParticleSystem.MinMaxCurve(
                1f,
                new AnimationCurve(
                    new Keyframe(0f, 0.18f),
                    new Keyframe(0.34f, 0.10f),
                    new Keyframe(1f, 0f)));

            ParticleSystemRenderer renderer =
                detailObject.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.065f;
            renderer.lengthScale = 1.15f;
            renderer.maxParticleSize = 0.12f;
            detailParticleMaterial = CreateWindDetailMaterial();
            renderer.sharedMaterial = detailParticleMaterial;
            trailParticleMaterial = CreateWindTrailMaterial();
            renderer.trailMaterial = trailParticleMaterial;
            windDetailParticles.Play();
        }

        void BuildMagneticPresentation()
        {
            fieldMaterial = CreateMagneticFieldMaterial();
            propertyBlock = new MaterialPropertyBlock();
            magneticVisualClippingComplete = !Application.isPlaying;
            nextMagneticVisualClipAt = Time.time + 0.05f;
            magneticVisualClipDeadline = Time.time + 2f;
            for (int index = 0; index < magneticWalls.Count; index++)
            {
                CreateMagneticWall(
                    "InnerMagneticFacade_真实内壁_" + index,
                    magneticWalls[index]);
            }
        }

        void CreateMagneticWall(
            string objectName,
            UrbanMagneticWallSurface wall)
        {
            Vector3 inwardNormal = wall.localInwardNormal.normalized;
            Vector3 tangent = Vector3.Cross(Vector3.up, inwardNormal).normalized;
            var collisionObject = new GameObject(
                objectName + "_贴墙连续碰撞代理");
            collisionObject.transform.SetParent(transform, false);
            collisionObject.transform.localPosition = wall.localCenter;
            collisionObject.transform.localRotation = Quaternion.LookRotation(
                inwardNormal,
                Vector3.up);
            BoxCollider panel = collisionObject.AddComponent<BoxCollider>();
            panel.size = new Vector3(wall.width, wall.height, 0.9f);
            // Local +Z points into the courtyard. Keep the whole proxy behind
            // the authored facade so it reinforces the real building collider
            // without creating a free-standing barrier in navigable space.
            panel.center = Vector3.back * 0.45f;
            magneticPanels.Add(panel);

            // A wall-local spot light makes the state change affect the real
            // courtyard lighting instead of reading as tiny coloured decals.
            // It shines only from the authored inner facade into the yard;
            // neither the open entrance nor the open roof receives geometry.
            var lightObject = new GameObject(
                objectName + "_MagneticPressureLight");
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.localPosition =
                wall.localCenter + inwardNormal * 4f;
            lightObject.transform.localRotation = Quaternion.LookRotation(
                inwardNormal,
                Vector3.up);
            Light wallLight = lightObject.AddComponent<Light>();
            wallLight.type = LightType.Spot;
            wallLight.spotAngle = 112f;
            wallLight.innerSpotAngle = 58f;
            wallLight.range = Mathf.Clamp(
                Mathf.Max(wall.width, wall.height) * 0.58f,
                42f,
                96f);
            wallLight.shadows = LightShadows.None;
            wallLight.renderMode = LightRenderMode.ForcePixel;
            magneticWallLights.Add(wallLight);

            // Smaller tiles can be clipped against the actual stepped prefab
            // silhouette. This avoids a lot-sized rectangle floating above a
            // lower roof or across a facade cut-out.
            const int Columns = 5;
            const int Rows = 7;
            float cellWidth = wall.width / Columns;
            float cellHeight = wall.height / Rows;
            float nodeWidth = cellWidth * 0.91f;
            float nodeHeight = cellHeight * 0.91f;

            // Only decals attached to sampled real facade geometry are shown.
            // The opening, courtyard air and open roof never receive a renderer.
            for (int row = 0; row < Rows; row++)
            for (int column = 0; column < Columns; column++)
            {
                float horizontalOffset =
                    (column - (Columns - 1) * 0.5f) * cellWidth;
                float verticalOffset =
                    (row - (Rows - 1) * 0.5f) * cellHeight;
                Vector3 nodePosition = wall.localCenter +
                                       inwardNormal * 0.09f +
                                       tangent * horizontalOffset +
                                       Vector3.up * verticalOffset;

                GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Quad);
                visual.name = objectName + "_MagneticWallSurfaceNode_" +
                              row + "_" + column;
                visual.transform.SetParent(transform, false);
                visual.transform.localPosition = nodePosition;
                visual.transform.localRotation = Quaternion.LookRotation(
                    inwardNormal,
                    Vector3.up);
                visual.transform.localScale = new Vector3(
                    nodeWidth,
                    nodeHeight,
                    1f);
                Collider visualCollider = visual.GetComponent<Collider>();
                if (Application.isPlaying)
                    Destroy(visualCollider);
                else
                    DestroyImmediate(visualCollider);
                Renderer renderer = visual.GetComponent<Renderer>();
                renderer.sharedMaterial = fieldMaterial;
                renderer.enabled = !Application.isPlaying;
                stateRenderers.Add(renderer);
                magneticVisualTiles.Add(new MagneticVisualTile
                {
                    renderer = renderer,
                    localCenter = nodePosition,
                    localInwardNormal = inwardNormal,
                    localTangent = tangent,
                    halfWidth = nodeWidth * 0.5f,
                    halfHeight = nodeHeight * 0.5f
                });
            }
        }

        void TryClipMagneticVisualsToRealFacades()
        {
            if (!BuildMagneticVisualClipMeshes())
            {
                nextMagneticVisualClipAt = Time.time + 0.12f;
                if (Time.time < magneticVisualClipDeadline)
                    return;
                magneticVisualClippingComplete = true;
                Debug.LogWarning(
                    "[UrbanEnvironment] 三面磁墙附近没有可读取的真实楼房网格，" +
                    "已隐藏磁轨以避免悬空结界。",
                    this);
                return;
            }

            int supportedCount = 0;
            for (int index = 0; index < magneticVisualTiles.Count; index++)
            {
                MagneticVisualTile tile = magneticVisualTiles[index];
                bool supported = IsMagneticTileOnRealFacade(tile);
                if (tile.renderer != null)
                    tile.renderer.enabled = supported;
                if (supported)
                    supportedCount++;
            }
            DestroyMagneticVisualClipMeshes();

            if (supportedCount > 0)
            {
                magneticVisualClippingComplete = true;
                return;
            }

            nextMagneticVisualClipAt = Time.time + 0.12f;
            if (Time.time < magneticVisualClipDeadline)
                return;
            magneticVisualClippingComplete = true;
            Debug.LogWarning(
                "[UrbanEnvironment] 三面磁墙没有检测到可贴附的真实楼房外立面，" +
                "已隐藏磁轨以避免悬空结界。",
                this);
        }

        bool BuildMagneticVisualClipMeshes()
        {
            DestroyMagneticVisualClipMeshes();
            MeshFilter[] filters = FindObjectsOfType<MeshFilter>();
            for (int index = 0; index < filters.Length; index++)
            {
                MeshFilter filter = filters[index];
                if (filter == null || filter.sharedMesh == null ||
                    !filter.sharedMesh.isReadable)
                {
                    continue;
                }
                Renderer sourceRenderer = filter.GetComponent<Renderer>();
                if (sourceRenderer == null || !sourceRenderer.enabled ||
                    stateRenderers.Contains(sourceRenderer) ||
                    !IsRendererNearMagneticFacade(sourceRenderer.bounds))
                {
                    continue;
                }

                var helper = new GameObject(
                    "RuntimeMagneticFacadeClipMesh");
                helper.hideFlags = HideFlags.HideAndDontSave;
                helper.transform.SetPositionAndRotation(
                    filter.transform.position,
                    filter.transform.rotation);
                helper.transform.localScale = filter.transform.lossyScale;
                MeshCollider meshCollider = helper.AddComponent<MeshCollider>();
                meshCollider.sharedMesh = filter.sharedMesh;
                if (meshCollider.sharedMesh == null)
                {
                    DestroyRuntimeObject(helper);
                    continue;
                }
                magneticVisualClipMeshes.Add(meshCollider);
            }
            if (magneticVisualClipMeshes.Count > 0)
                Physics.SyncTransforms();
            return magneticVisualClipMeshes.Count > 0;
        }

        bool IsRendererNearMagneticFacade(Bounds bounds)
        {
            for (int index = 0; index < magneticWalls.Count; index++)
            {
                UrbanMagneticWallSurface wall = magneticWalls[index];
                Vector3 wallCenter = transform.TransformPoint(wall.localCenter);
                Vector3 inward = transform.TransformDirection(
                    wall.localInwardNormal).normalized;
                Vector3 tangent = Vector3.Cross(transform.up, inward).normalized;
                Vector3 relative = bounds.center - wallCenter;
                Vector3 extents = bounds.extents;
                float normalExtent = Vector3.Dot(
                    new Vector3(
                        Mathf.Abs(inward.x),
                        Mathf.Abs(inward.y),
                        Mathf.Abs(inward.z)),
                    extents);
                float tangentExtent = Vector3.Dot(
                    new Vector3(
                        Mathf.Abs(tangent.x),
                        Mathf.Abs(tangent.y),
                        Mathf.Abs(tangent.z)),
                    extents);
                if (Mathf.Abs(Vector3.Dot(relative, inward)) >
                        normalExtent + 22f ||
                    Mathf.Abs(Vector3.Dot(relative, tangent)) >
                        wall.width * 0.5f + tangentExtent + 4f ||
                    bounds.max.y < wallCenter.y - wall.height * 0.5f ||
                    bounds.min.y > wallCenter.y + wall.height * 0.5f)
                {
                    continue;
                }
                return true;
            }
            return false;
        }

        void DestroyMagneticVisualClipMeshes()
        {
            for (int index = 0;
                 index < magneticVisualClipMeshes.Count;
                 index++)
            {
                MeshCollider collider = magneticVisualClipMeshes[index];
                if (collider == null)
                    continue;
                collider.enabled = false;
                DestroyRuntimeObject(collider.gameObject);
            }
            magneticVisualClipMeshes.Clear();
        }

        bool IsMagneticTileOnRealFacade(MagneticVisualTile tile)
        {
            if (tile == null || tile.renderer == null)
                return false;
            float horizontal = tile.halfWidth * 0.82f;
            float vertical = tile.halfHeight * 0.82f;
            Vector3[] samples =
            {
                tile.localCenter,
                tile.localCenter + tile.localTangent * horizontal +
                    Vector3.up * vertical,
                tile.localCenter + tile.localTangent * horizontal -
                    Vector3.up * vertical,
                tile.localCenter - tile.localTangent * horizontal +
                    Vector3.up * vertical,
                tile.localCenter - tile.localTangent * horizontal -
                    Vector3.up * vertical
            };
            for (int index = 0; index < samples.Length; index++)
            {
                if (!HasRealFacadeAtSample(
                        samples[index],
                        tile.localInwardNormal))
                {
                    return false;
                }
            }
            return true;
        }

        bool HasRealFacadeAtSample(
            Vector3 localSample,
            Vector3 localInwardNormal)
        {
            Vector3 worldInward = transform.TransformDirection(
                localInwardNormal).normalized;
            Vector3 worldSample = transform.TransformPoint(localSample);
            Vector3 origin = worldSample + worldInward * 18f;
            var ray = new Ray(origin, -worldInward);
            for (int index = 0;
                 index < magneticVisualClipMeshes.Count;
                 index++)
            {
                MeshCollider meshCollider = magneticVisualClipMeshes[index];
                if (meshCollider != null && meshCollider.enabled &&
                    meshCollider.Raycast(ray, out _, 38f))
                    return true;
            }
            return false;
        }

        void SetVisualState()
        {
            Color color = ResolveVisualColor();
            float intensity = state == UrbanEnvironmentalFieldState.Active
                ? 1.38f
                : state == UrbanEnvironmentalFieldState.Warning
                    ? 1.05f
                    : 0.64f;
            ApplyStateRendererColor(color, intensity);
            for (int index = 0; index < windLines.Count; index++)
            {
                if (windLines[index] == null)
                    continue;
                windLines[index].startColor = color;
                windLines[index].endColor = new Color(
                    color.r,
                    color.g,
                    color.b,
                    0.025f);
            }
            if (windParticles == null)
                return;
            ParticleSystem.MainModule main = windParticles.main;
            main.startColor = new ParticleSystem.MinMaxGradient(color);
            ParticleSystem.EmissionModule emission = windParticles.emission;
            float mainRate = state == UrbanEnvironmentalFieldState.Active
                ? 13f
                : state == UrbanEnvironmentalFieldState.Warning
                    ? 7f
                    : state == UrbanEnvironmentalFieldState.Cooldown
                        ? 3f
                        : 1f;
            emission.rateOverTime = mainRate;

            if (windDetailParticles == null)
                return;
            ParticleSystem.MainModule detailMain = windDetailParticles.main;
            Color detailColor = new Color(
                color.r,
                color.g * 0.98f,
                color.b * 0.92f,
                color.a);
            detailMain.startColor = new ParticleSystem.MinMaxGradient(detailColor);
            ParticleSystem.EmissionModule detailEmission =
                windDetailParticles.emission;
            float detailRate = state == UrbanEnvironmentalFieldState.Active
                ? 92f
                : state == UrbanEnvironmentalFieldState.Warning
                    ? 30f
                    : state == UrbanEnvironmentalFieldState.Cooldown
                        ? 10f
                        : 4f;
            detailEmission.rateOverTime = detailRate;
        }

        Color ResolveVisualColor()
        {
            if (kind == UrbanEnvironmentalFieldKind.MagneticCourtyard)
            {
                switch (state)
                {
                    case UrbanEnvironmentalFieldState.Warning:
                        return new Color(1f, 0.34f, 0.015f, 0.92f);
                    case UrbanEnvironmentalFieldState.Active:
                        return new Color(1f, 0.015f, 0.005f, 1f);
                    case UrbanEnvironmentalFieldState.Cooldown:
                        return new Color(0.02f, 0.18f, 0.62f, 0.42f);
                    default:
                        return new Color(0.025f, 0.30f, 1f, 0.58f);
                }
            }

            switch (state)
            {
                case UrbanEnvironmentalFieldState.Warning:
                    return new Color(0.70f, 0.68f, 0.62f, 0.48f);
                case UrbanEnvironmentalFieldState.Active:
                    return new Color(0.86f, 0.83f, 0.75f, 0.62f);
                case UrbanEnvironmentalFieldState.Cooldown:
                    return new Color(0.36f, 0.35f, 0.32f, 0.24f);
                default:
                    return new Color(0.28f, 0.27f, 0.25f, 0.16f);
            }
        }

        void ApplyStateRendererColor(Color color, float intensity)
        {
            if (propertyBlock == null)
                propertyBlock = new MaterialPropertyBlock();
            Color tint = color;
            tint.a *= intensity;
            propertyBlock.SetColor("_Color", tint);
            propertyBlock.SetColor("_TintColor", tint);
            float emissionMultiplier = kind ==
                UrbanEnvironmentalFieldKind.MagneticCourtyard
                    ? state == UrbanEnvironmentalFieldState.Active
                        ? 5.8f
                        : state == UrbanEnvironmentalFieldState.Warning
                            ? 3.8f
                            : 1.7f
                    : 2.1f;
            propertyBlock.SetColor(
                "_EmissionColor",
                color * (emissionMultiplier * intensity));
            for (int index = 0; index < stateRenderers.Count; index++)
                stateRenderers[index]?.SetPropertyBlock(propertyBlock);
            ApplyMagneticWallLights(color, intensity);
        }

        void ApplyMagneticWallLights(Color color, float pulse)
        {
            if (kind != UrbanEnvironmentalFieldKind.MagneticCourtyard)
                return;
            float baseIntensity = state == UrbanEnvironmentalFieldState.Active
                ? 5.2f
                : state == UrbanEnvironmentalFieldState.Warning
                    ? 3.4f
                    : state == UrbanEnvironmentalFieldState.Cooldown
                        ? 0.45f
                        : 0.85f;
            for (int index = 0; index < magneticWallLights.Count; index++)
            {
                Light wallLight = magneticWallLights[index];
                if (wallLight == null)
                    continue;
                wallLight.color = new Color(
                    Mathf.Clamp01(color.r),
                    Mathf.Clamp01(color.g),
                    Mathf.Clamp01(color.b));
                wallLight.intensity = baseIntensity * pulse;
                wallLight.enabled = true;
            }
        }

        static Vector3 SanitizeSize(Vector3 size)
        {
            return new Vector3(
                Mathf.Max(8f, size.x),
                Mathf.Max(12f, size.y),
                Mathf.Max(8f, size.z));
        }

        static Material CreateFieldMaterial(string name, Color color)
        {
            Shader shader = Shader.Find("Standard");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            var material = new Material(shader)
            {
                name = name,
                color = color,
                enableInstancing = true
            };
            if (material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * 0.6f);
            }
            return material;
        }

        Material CreateParticleMaterial()
        {
            Shader shader = Shader.Find("Legacy Shaders/Particles/Additive");
            if (shader == null)
                shader = Shader.Find("Particles/Standard Unlit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            var material = new Material(shader)
            {
                name = "ViolentNaturalGaleBands"
            };
            particleTexture = Resources.Load<Texture2D>(
                "CombatCityPCG/Effects/NaturalGaleDustSheet");
            ownsParticleTexture = particleTexture == null;
            if (particleTexture == null)
                particleTexture = CreateSoftStreakTexture();
            particleTexture.wrapMode = TextureWrapMode.Clamp;
            particleTexture.filterMode = FilterMode.Bilinear;
            if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", particleTexture);
            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", particleTexture);
            // The texture carries neutral concrete dust. Keep the material
            // neutral so it never becomes a neon or magical-looking wind band.
            Color tint = new Color(1f, 1f, 1f, 0.9f);
            if (material.HasProperty("_TintColor"))
                material.SetColor("_TintColor", tint);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", tint);
            return material;
        }

        static Material CreateWindDetailMaterial()
        {
            Shader shader = Shader.Find("Legacy Shaders/Particles/Additive");
            if (shader == null)
                shader = Shader.Find("Particles/Standard Unlit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            var material = new Material(shader)
            {
                name = "ViolentNaturalGaleSpeedLines"
            };
            Texture2D texture = Resources.Load<Texture2D>(
                "CombatCityPCG/Effects/NaturalGaleDustTracers");
            if (texture != null)
            {
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
                if (material.HasProperty("_MainTex"))
                    material.SetTexture("_MainTex", texture);
                if (material.HasProperty("_BaseMap"))
                    material.SetTexture("_BaseMap", texture);
            }
            Color tint = new Color(0.98f, 0.96f, 0.90f, 0.95f);
            if (material.HasProperty("_TintColor"))
                material.SetColor("_TintColor", tint);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", tint);
            return material;
        }

        Material CreateWindTrailMaterial()
        {
            Shader shader = Shader.Find("Legacy Shaders/Particles/Additive");
            if (shader == null)
                shader = Shader.Find("Particles/Standard Unlit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            var material = new Material(shader)
            {
                name = "ViolentNaturalGaleMotionTrails"
            };
            trailParticleTexture = CreateSoftStreakTexture();
            if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", trailParticleTexture);
            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", trailParticleTexture);
            Color tint = new Color(0.88f, 0.86f, 0.80f, 0.42f);
            if (material.HasProperty("_TintColor"))
                material.SetColor("_TintColor", tint);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", tint);
            return material;
        }

        static Material CreateMagneticFieldMaterial()
        {
            Shader shader = Shader.Find("Legacy Shaders/Particles/Additive");
            if (shader == null)
                shader = Shader.Find("Particles/Standard Unlit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            var material = new Material(shader)
            {
                name = "AnimatedMagneticWallSurface",
                enableInstancing = true
            };
            Texture2D texture = Resources.Load<Texture2D>(
                "CombatCityPCG/Effects/MagneticWallFluxTracks");
            if (texture != null)
            {
                texture.wrapMode = TextureWrapMode.Repeat;
                texture.filterMode = FilterMode.Bilinear;
                if (material.HasProperty("_MainTex"))
                    material.SetTexture("_MainTex", texture);
                if (material.HasProperty("_BaseMap"))
                    material.SetTexture("_BaseMap", texture);
                if (material.HasProperty("_MainTex"))
                    material.SetTextureScale(
                        "_MainTex",
                        new Vector2(1.35f, 1.7f));
                if (material.HasProperty("_BaseMap"))
                    material.SetTextureScale(
                        "_BaseMap",
                        new Vector2(1.35f, 1.7f));
            }
            if (material.HasProperty("_Cull"))
                material.SetFloat("_Cull", 0f);
            return material;
        }

        static Texture2D CreateSoftStreakTexture()
        {
            const int Width = 64;
            const int Height = 16;
            var texture = new Texture2D(
                Width,
                Height,
                TextureFormat.RGBA32,
                false)
            {
                name = "RuntimeNaturalWindSoftStreak",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };
            var pixels = new Color32[Width * Height];
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                float longitudinal = Mathf.Sin(
                    (x + 0.5f) / Width * Mathf.PI);
                float lateral = 1f - Mathf.Abs(
                    (y + 0.5f) / Height * 2f - 1f);
                byte alpha = (byte)Mathf.RoundToInt(
                    255f * longitudinal * longitudinal *
                    lateral * lateral * lateral);
                pixels[y * Width + x] = new Color32(
                    220,
                    242,
                    255,
                    alpha);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        void OnDestroy()
        {
            ReleaseCapturedBodies();
            DestroyMagneticVisualClipMeshes();
            DestroyRuntimeObject(fieldMaterial);
            DestroyRuntimeObject(particleMaterial);
            DestroyRuntimeObject(detailParticleMaterial);
            DestroyRuntimeObject(trailParticleMaterial);
            DestroyRuntimeObject(trailParticleTexture);
            if (ownsParticleTexture)
                DestroyRuntimeObject(particleTexture);
        }

        static void DestroyRuntimeObject(UnityEngine.Object value)
        {
            if (value == null)
                return;
            if (Application.isPlaying)
                Destroy(value);
            else
                DestroyImmediate(value);
        }
    }

    [DisallowMultipleComponent]
    public sealed class UrbanEnvironmentalImpactReceiver : MonoBehaviour
    {
        float armedUntil;
        float impactScale;
        float nextDamageAt;
        GameObject source;

        public void ArmWindImpact(float scale, GameObject fieldSource)
        {
            armedUntil = Time.time + 0.32f;
            impactScale = Mathf.Clamp(scale, 0.3f, 1.2f);
            source = fieldSource;
        }

        void OnCollisionEnter(Collision collision)
        {
            if (collision == null || Time.time > armedUntil ||
                Time.time < nextDamageAt ||
                collision.relativeVelocity.magnitude < 13f)
            {
                return;
            }
            if (GetComponent<HordeEnemyVehicle>() != null ||
                GetComponent<ModularBossCombatRuntime>() != null)
            {
                return;
            }

            ContactPoint contact = collision.contactCount > 0
                ? collision.GetContact(0)
                : default;
            VehicleModuleDamageReceiver receiver = contact.thisCollider != null
                ? contact.thisCollider.GetComponentInParent<
                    VehicleModuleDamageReceiver>()
                : null;
            if (receiver == null)
                receiver = FindNearestModuleReceiver(contact.point);
            if (receiver == null || receiver.IsDestroyed)
                return;

            RobocraftMotionCoordinator motion =
                GetComponent<RobocraftMotionCoordinator>();
            float modeScale = motion != null &&
                              motion.CoreAssistMode ==
                              VehicleCoreAssistMode.Training
                ? 0.65f
                : 1f;
            float excessSpeed = collision.relativeVelocity.magnitude - 13f;
            float damage = Mathf.Clamp(
                excessSpeed * excessSpeed * 0.035f * impactScale * modeScale,
                0.5f,
                10f);
            receiver.ApplyDamage(new SpaceDamageInfo(
                damage,
                contact.point,
                collision.impulse,
                SpaceDamageType.Collision,
                source));
            nextDamageAt = Time.time + 0.45f;
        }

        VehicleModuleDamageReceiver FindNearestModuleReceiver(Vector3 point)
        {
            VehicleModuleDamageReceiver[] receivers =
                GetComponentsInChildren<VehicleModuleDamageReceiver>();
            VehicleModuleDamageReceiver selected = null;
            float nearest = float.PositiveInfinity;
            for (int index = 0; index < receivers.Length; index++)
            {
                VehicleModuleDamageReceiver candidate = receivers[index];
                if (candidate == null || candidate.IsDestroyed)
                    continue;
                Collider collider = candidate.GetComponent<Collider>();
                float distance = collider != null
                    ? collider.bounds.SqrDistance(point)
                    : (candidate.transform.position - point).sqrMagnitude;
                if (distance >= nearest)
                    continue;
                nearest = distance;
                selected = candidate;
            }
            return selected;
        }
    }
}
