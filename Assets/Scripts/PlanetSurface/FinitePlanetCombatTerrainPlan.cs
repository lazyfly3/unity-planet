using System;
using System.Collections.Generic;
using UnityEngine;
using UnityPlanet.CombatMap;

public enum FinitePlanetCombatMissionTerrainKind
{
    Clearance = 0,
    Traverse = 1,
    Assault = 2
}

/// <summary>
/// Planet missions are defence spaces rather than mirrored duel arenas. This
/// data is kept separate from the generic combat-map recipe so later enemy
/// directors can consume the perimeter entrances without rebuilding terrain.
/// </summary>
[Serializable]
public sealed class FinitePlanetDefenseLayoutPlan
{
    public Vector3 combatCenter;
    public Vector3 playerSpawn;
    public CombatSemanticAnchor[] enemyIngresses =
        Array.Empty<CombatSemanticAnchor>();
    public CombatSemanticAnchor[] powerPositions =
        Array.Empty<CombatSemanticAnchor>();
    public CombatSemanticAnchor[] retreatPoints =
        Array.Empty<CombatSemanticAnchor>();
    public CombatSemanticRoute[] ingressRoutes =
        Array.Empty<CombatSemanticRoute>();

    public bool IsValid =>
        enemyIngresses != null
        && enemyIngresses.Length >= 4
        && powerPositions != null
        && powerPositions.Length >= 2
        && retreatPoints != null
        && retreatPoints.Length >= 2
        && ingressRoutes != null
        && ingressRoutes.Length >= enemyIngresses.Length;
}

static class FinitePlanetDefenseLayoutPlanner
{
    const int PowerCandidateCount = 56;

    public static FinitePlanetDefenseLayoutPlan Apply(
        CombatSemanticPlan plan,
        AirCombatMapSettings settings,
        FinitePlanetCombatMissionTerrainKind missionKind,
        int seed,
        float combatRadius)
    {
        if (plan == null)
            throw new ArgumentNullException(nameof(plan));
        if (settings == null)
            throw new ArgumentNullException(nameof(settings));

        float focusAngle = Hash01(seed, 17) * Mathf.PI * 2f;
        float focusRadius = combatRadius * Mathf.Lerp(
            0.1f,
            0.24f,
            Noise01(
                new Vector2(
                    Mathf.Cos(focusAngle),
                    Mathf.Sin(focusAngle)) * 2.7f,
                seed,
                23));
        Vector2 focus2 = new Vector2(
            Mathf.Cos(focusAngle),
            Mathf.Sin(focusAngle)) * focusRadius;
        Vector2 rear = new Vector2(
            Mathf.Cos(focusAngle + Mathf.PI),
            Mathf.Sin(focusAngle + Mathf.PI));
        Vector2 player2 = focus2
            + rear * combatRadius * Mathf.Lerp(
                0.08f,
                0.14f,
                Hash01(seed, 31));

        int ingressCount = missionKind ==
            FinitePlanetCombatMissionTerrainKind.Clearance
                ? 4
                : missionKind ==
                    FinitePlanetCombatMissionTerrainKind.Traverse
                    ? 5
                    : 6;
        var ingress = new List<CombatSemanticAnchor>(ingressCount);
        var routes = new List<CombatSemanticRoute>(ingressCount + 2);
        var stamps = new List<CombatTerrainStamp>(48);
        var volumes = new List<CombatTacticalVolume>(ingressCount + 8);
        float phase = Hash01(seed, 43) * Mathf.PI * 2f;

        stamps.Add(Stamp(
            "defence.basin.combat",
            CombatTerrainStampType.Basin,
            focus2,
            focus2,
            combatRadius * 0.17f,
            combatRadius * 0.055f,
            6f,
            plan.mapCenter.y));
        stamps.Add(Stamp(
            "defence.basin.player",
            CombatTerrainStampType.Basin,
            player2,
            player2,
            combatRadius * 0.085f,
            combatRadius * 0.035f,
            4f,
            plan.mapCenter.y));

        for (int index = 0; index < ingressCount; index++)
        {
            float baseAngle = phase
                + index * Mathf.PI * 2f / ingressCount;
            float jitter = Mathf.Lerp(
                -0.24f,
                0.24f,
                Noise01(
                    new Vector2(index * 1.91f, 3.7f),
                    seed,
                    59));
            float angle = baseAngle + jitter;
            float radius = combatRadius * Mathf.Lerp(
                0.64f,
                0.72f,
                Noise01(
                    new Vector2(index * 2.13f, -1.4f),
                    seed,
                    67));
            Vector2 position = new Vector2(
                Mathf.Cos(angle),
                Mathf.Sin(angle)) * radius;
            var anchor = Anchor(
                "ingress.enemy." + index.ToString("D2"),
                CombatAnchorType.EnemyIngress,
                position,
                (focus2 - position).normalized,
                combatRadius * 0.055f,
                plan.mapCenter.y);
            ingress.Add(anchor);
            volumes.Add(Volume(
                "volume.ingress.enemy." + index.ToString("D2"),
                CombatTacticalVolumeType.SpawnBasin,
                position,
                combatRadius * 0.11f,
                70f,
                1,
                plan.mapCenter.y));

            Vector2 direction = (focus2 - position).normalized;
            Vector2 perpendicular = new Vector2(-direction.y, direction.x);
            float bendSign = Noise01(
                    new Vector2(index * 0.73f, 8.1f),
                    seed,
                    71) < 0.5f
                ? -1f
                : 1f;
            float bend = combatRadius * Mathf.Lerp(
                0.055f,
                0.13f,
                Noise01(
                    new Vector2(index * 1.37f, 5.2f),
                    seed,
                    79)) * bendSign;
            Vector2[] controls =
            {
                position,
                Vector2.Lerp(position, focus2, 0.34f)
                    + perpendicular * bend,
                Vector2.Lerp(position, focus2, 0.68f)
                    - perpendicular * bend * 0.42f,
                focus2
            };
            CombatRouteType routeType = index == 0
                ? CombatRouteType.Main
                : index == 1
                    ? CombatRouteType.TerrainMaskedFlank
                    : index == 2
                        ? CombatRouteType.LongRange
                        : CombatRouteType.EnemyIngress;
            routes.Add(Route(
                "route.ingress.enemy." + index.ToString("D2"),
                routeType,
                controls,
                settings,
                plan.mapCenter.y));

            for (int segment = 0; segment < controls.Length - 1; segment++)
            {
                stamps.Add(Stamp(
                    "defence.corridor.ingress."
                    + index.ToString("D2") + "."
                    + segment.ToString("D2"),
                    CombatTerrainStampType.Corridor,
                    controls[segment],
                    controls[segment + 1],
                    settings.canyonRouteWidth * 0.36f,
                    settings.canyonRouteWidth * 0.2f,
                    6f,
                    plan.mapCenter.y));
            }

            // Cover is terrain, not decoration. Ridged noise controls which
            // side of the ingress lane receives each broken masking shoulder.
            for (int cover = 0; cover < 2; cover++)
            {
                float along = cover == 0 ? 0.42f : 0.67f;
                Vector2 lane = Vector2.Lerp(position, focus2, along);
                float coverNoise = Noise01(
                    lane / Mathf.Max(1f, combatRadius * 0.12f),
                    seed,
                    101 + index * 7 + cover);
                float side = coverNoise < 0.5f ? -1f : 1f;
                Vector2 coverCenter = lane + perpendicular
                    * side * combatRadius
                    * Mathf.Lerp(0.065f, 0.095f, coverNoise);
                Vector2 half = direction * combatRadius
                    * Mathf.Lerp(0.035f, 0.06f, coverNoise);
                stamps.Add(Stamp(
                    "defence.cover.ingress."
                    + index.ToString("D2") + "."
                    + cover.ToString("D2"),
                    CombatTerrainStampType.RidgeCapsule,
                    coverCenter - half,
                    coverCenter + half,
                    combatRadius * 0.032f,
                    combatRadius * 0.028f,
                    settings.mountainHeight
                    * Mathf.Lerp(0.78f, 1.08f, coverNoise),
                    plan.mapCenter.y));
            }
        }

        List<CombatSemanticAnchor> powers = SelectPowerPositions(
            focus2,
            player2,
            ingress,
            settings,
            seed,
            combatRadius,
            plan.mapCenter.y,
            stamps,
            volumes);
        List<CombatSemanticAnchor> retreats = BuildRetreatPoints(
            focus2,
            player2,
            phase,
            settings,
            seed,
            combatRadius,
            plan.mapCenter.y,
            stamps,
            volumes,
            routes);

        plan.terrainStamps = stamps.ToArray();
        plan.tacticalVolumes = volumes.ToArray();
        plan.routes = routes.ToArray();

        var anchors = new List<CombatSemanticAnchor>();
        if (plan.anchors != null)
        {
            for (int index = 0; index < plan.anchors.Length; index++)
            {
                CombatSemanticAnchor value = plan.anchors[index];
                if (value == null
                    || value.type == CombatAnchorType.PlayerSpawn
                    || value.type == CombatAnchorType.EnemySpawn
                    || value.type == CombatAnchorType.CentralConflict
                    || value.type == CombatAnchorType.PlayerRetreat
                    || value.type == CombatAnchorType.EnemyRetreat
                    || value.type == CombatAnchorType.PowerPosition
                    || value.type == CombatAnchorType.EnemyIngress)
                {
                    continue;
                }
                anchors.Add(value);
            }
        }
        CombatSemanticAnchor playerAnchor = Anchor(
            "spawn.player.defence",
            CombatAnchorType.PlayerSpawn,
            player2,
            (focus2 - player2).normalized,
            combatRadius * 0.055f,
            plan.mapCenter.y);
        CombatSemanticAnchor conflictAnchor = Anchor(
            "conflict.defence",
            CombatAnchorType.CentralConflict,
            focus2,
            Vector2.up,
            combatRadius * 0.16f,
            plan.mapCenter.y);
        CombatSemanticAnchor compatibilityEnemy = Anchor(
            "spawn.enemy.primary-ingress",
            CombatAnchorType.EnemySpawn,
            new Vector2(ingress[0].position.x, ingress[0].position.z),
            new Vector2(
                ingress[0].forward.x,
                ingress[0].forward.z),
            ingress[0].radius,
            plan.mapCenter.y);
        anchors.Add(playerAnchor);
        anchors.Add(compatibilityEnemy);
        anchors.Add(conflictAnchor);
        anchors.AddRange(ingress);
        anchors.AddRange(powers);
        anchors.AddRange(retreats);
        plan.anchors = anchors.ToArray();

        ResolveVerticalPlacement(plan, settings);

        var result = new FinitePlanetDefenseLayoutPlan
        {
            combatCenter = conflictAnchor.position,
            playerSpawn = playerAnchor.position,
            enemyIngresses = ingress.ToArray(),
            powerPositions = powers.ToArray(),
            retreatPoints = retreats.ToArray(),
            ingressRoutes = routes.ToArray()
        };
        if (!result.IsValid)
        {
            throw new InvalidOperationException(
                "Planet defence PCG did not produce the required ingress, "
                + "power-position and retreat contracts.");
        }
        return result;
    }

    static List<CombatSemanticAnchor> SelectPowerPositions(
        Vector2 focus,
        Vector2 player,
        List<CombatSemanticAnchor> ingress,
        AirCombatMapSettings settings,
        int seed,
        float combatRadius,
        float baseY,
        List<CombatTerrainStamp> stamps,
        List<CombatTacticalVolume> volumes)
    {
        var result = new List<CombatSemanticAnchor>(3);
        for (int rank = 0; rank < 3; rank++)
        {
            Vector2 best = focus;
            float bestScore = float.NegativeInfinity;
            for (int candidate = 0;
                 candidate < PowerCandidateCount;
                 candidate++)
            {
                float angle = Hash01(seed ^ (rank * 977), candidate * 7 + 3)
                    * Mathf.PI * 2f;
                float radius = combatRadius * Mathf.Lerp(
                    0.16f,
                    0.46f,
                    Hash01(seed ^ 0x45D9F3B, candidate * 11 + rank));
                Vector2 point = focus + new Vector2(
                    Mathf.Cos(angle),
                    Mathf.Sin(angle)) * radius;
                if (point.magnitude > combatRadius * 0.63f
                    || Vector2.Distance(point, player)
                    < combatRadius * 0.12f)
                {
                    continue;
                }
                bool separated = true;
                for (int prior = 0; prior < result.Count; prior++)
                {
                    Vector2 priorPoint = new Vector2(
                        result[prior].position.x,
                        result[prior].position.z);
                    if (Vector2.Distance(point, priorPoint)
                        < combatRadius * 0.18f)
                    {
                        separated = false;
                        break;
                    }
                }
                if (!separated)
                    continue;
                float broad = Noise01(
                    point / Mathf.Max(1f, combatRadius * 0.22f),
                    seed,
                    131);
                float ridge = 1f - Mathf.Abs(
                    Noise01(
                        point / Mathf.Max(1f, combatRadius * 0.095f),
                        seed,
                        149) * 2f - 1f);
                float ingressPressure = 0f;
                for (int index = 0; index < ingress.Count; index++)
                {
                    Vector2 ingressPoint = new Vector2(
                        ingress[index].position.x,
                        ingress[index].position.z);
                    ingressPressure = Mathf.Max(
                        ingressPressure,
                        1f - Mathf.Clamp01(
                            Vector2.Distance(point, ingressPoint)
                            / (combatRadius * 0.8f)));
                }
                float score = broad * 0.42f
                    + ridge * 0.38f
                    + ingressPressure * 0.2f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = point;
                }
            }
            float strength = Mathf.Clamp01(bestScore);
            CombatSemanticAnchor anchor = Anchor(
                "power.noise." + rank.ToString("D2"),
                CombatAnchorType.PowerPosition,
                best,
                (focus - best).normalized,
                combatRadius * 0.06f,
                baseY);
            result.Add(anchor);
            float height = settings.mountainHeight
                * Mathf.Lerp(0.62f, 0.82f, strength);
            stamps.Add(Stamp(
                "defence.power.mesa." + rank.ToString("D2"),
                CombatTerrainStampType.MesaCapsule,
                best,
                best,
                combatRadius * 0.058f,
                combatRadius * 0.045f,
                height,
                baseY));
            stamps.Add(Stamp(
                "defence.power.terrace." + rank.ToString("D2"),
                CombatTerrainStampType.Basin,
                best,
                best,
                combatRadius * 0.032f,
                combatRadius * 0.018f,
                height * 0.82f,
                baseY));
            volumes.Add(Volume(
                "volume.power.noise." + rank.ToString("D2"),
                CombatTacticalVolumeType.OcclusionGate,
                best,
                combatRadius * 0.11f,
                42f,
                0,
                baseY));
        }
        return result;
    }

    static List<CombatSemanticAnchor> BuildRetreatPoints(
        Vector2 focus,
        Vector2 player,
        float phase,
        AirCombatMapSettings settings,
        int seed,
        float combatRadius,
        float baseY,
        List<CombatTerrainStamp> stamps,
        List<CombatTacticalVolume> volumes,
        List<CombatSemanticRoute> routes)
    {
        var result = new List<CombatSemanticAnchor>(2);
        for (int index = 0; index < 2; index++)
        {
            float angle = phase + Mathf.PI
                + (index == 0 ? -0.62f : 0.62f)
                + Mathf.Lerp(
                    -0.18f,
                    0.18f,
                    Noise01(
                        new Vector2(index * 2.3f, -4.1f),
                        seed,
                        173));
            float distance = combatRadius * Mathf.Lerp(
                0.18f,
                0.3f,
                1f - Noise01(
                    new Vector2(index * 1.4f, 2.2f),
                    seed,
                    181));
            Vector2 point = focus + new Vector2(
                Mathf.Cos(angle),
                Mathf.Sin(angle)) * distance;
            if (point.magnitude > combatRadius * 0.62f)
                point = Vector2.ClampMagnitude(point, combatRadius * 0.62f);
            CombatSemanticAnchor anchor = Anchor(
                "retreat.player." + index.ToString("D2"),
                CombatAnchorType.PlayerRetreat,
                point,
                (player - point).normalized,
                combatRadius * 0.055f,
                baseY);
            result.Add(anchor);
            stamps.Add(Stamp(
                "defence.retreat.basin." + index.ToString("D2"),
                CombatTerrainStampType.Basin,
                point,
                point,
                combatRadius * 0.068f,
                combatRadius * 0.032f,
                4f,
                baseY));
            Vector2 away = (point - focus).normalized;
            Vector2 tangent = new Vector2(-away.y, away.x);
            Vector2 wallCenter = point + away * combatRadius * 0.055f;
            stamps.Add(Stamp(
                "defence.retreat.cover." + index.ToString("D2"),
                CombatTerrainStampType.RidgeCapsule,
                wallCenter - tangent * combatRadius * 0.055f,
                wallCenter + tangent * combatRadius * 0.055f,
                combatRadius * 0.03f,
                combatRadius * 0.026f,
                settings.mountainHeight * 0.88f,
                baseY));
            volumes.Add(Volume(
                "volume.retreat.player." + index.ToString("D2"),
                CombatTacticalVolumeType.RecoveryPocket,
                point,
                combatRadius * 0.13f,
                36f,
                -1,
                baseY));
            routes.Add(Route(
                "route.retreat.player." + index.ToString("D2"),
                CombatRouteType.Retreat,
                new[] { focus, point, player },
                settings,
                baseY));
        }
        return result;
    }

    static void ResolveVerticalPlacement(
        CombatSemanticPlan plan,
        AirCombatMapSettings settings)
    {
        if (plan.anchors != null)
        {
            for (int index = 0; index < plan.anchors.Length; index++)
            {
                CombatSemanticAnchor anchor = plan.anchors[index];
                if (anchor == null)
                    continue;
                float ground = CombatMapGenerator.SampleHeight(
                    settings,
                    plan,
                    anchor.position.x,
                    anchor.position.z);
                float clearance = anchor.type == CombatAnchorType.PlayerSpawn
                    ? 8f
                    : anchor.type == CombatAnchorType.EnemyIngress
                        || anchor.type == CombatAnchorType.EnemySpawn
                        ? 72f
                        : anchor.type == CombatAnchorType.PowerPosition
                            ? 42f
                            : 34f;
                anchor.position = new Vector3(
                    anchor.position.x,
                    ground + clearance,
                    anchor.position.z);
            }
        }
        if (plan.routes != null)
        {
            for (int routeIndex = 0;
                 routeIndex < plan.routes.Length;
                 routeIndex++)
            {
                CombatSemanticRoute route = plan.routes[routeIndex];
                if (route?.waypoints == null)
                    continue;
                float clearance = route.type == CombatRouteType.Retreat
                    ? 38f
                    : route.type == CombatRouteType.LongRange
                        ? 82f
                        : 56f;
                for (int point = 0; point < route.waypoints.Length; point++)
                {
                    Vector3 value = route.waypoints[point];
                    float ground = CombatMapGenerator.SampleHeight(
                        settings,
                        plan,
                        value.x,
                        value.z);
                    value.y = ground + clearance;
                    route.waypoints[point] = value;
                }
            }
        }
    }

    static CombatSemanticAnchor Anchor(
        string id,
        CombatAnchorType type,
        Vector2 position,
        Vector2 forward,
        float radius,
        float baseY)
    {
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector2.up;
        forward.Normalize();
        return new CombatSemanticAnchor
        {
            stableId = id,
            type = type,
            position = new Vector3(position.x, baseY, position.y),
            forward = new Vector3(forward.x, 0f, forward.y),
            radius = radius
        };
    }

    static CombatSemanticRoute Route(
        string id,
        CombatRouteType type,
        Vector2[] points,
        AirCombatMapSettings settings,
        float baseY)
    {
        var waypoints = new Vector3[points.Length];
        for (int index = 0; index < points.Length; index++)
        {
            waypoints[index] = new Vector3(
                points[index].x,
                baseY + 56f,
                points[index].y);
        }
        return new CombatSemanticRoute
        {
            stableId = id,
            type = type,
            width = type == CombatRouteType.LongRange
                ? settings.longRangeRouteWidth
                : settings.canyonRouteWidth,
            intendedExposure = type == CombatRouteType.Retreat
                ? 0.2f
                : type == CombatRouteType.LongRange ? 0.72f : 0.38f,
            waypoints = waypoints,
            controlVolumeIds = Array.Empty<string>()
        };
    }

    static CombatTerrainStamp Stamp(
        string id,
        CombatTerrainStampType type,
        Vector2 start,
        Vector2 end,
        float radius,
        float falloff,
        float height,
        float baseY)
    {
        return new CombatTerrainStamp
        {
            stableId = id,
            type = type,
            start = new Vector3(start.x, baseY, start.y),
            end = new Vector3(end.x, baseY, end.y),
            radius = radius,
            falloff = falloff,
            height = height
        };
    }

    static CombatTacticalVolume Volume(
        string id,
        CombatTacticalVolumeType type,
        Vector2 position,
        float diameter,
        float clearance,
        int teamBias,
        float baseY)
    {
        return new CombatTacticalVolume
        {
            stableId = id,
            type = type,
            position = new Vector3(position.x, baseY, position.y),
            size = new Vector3(diameter, clearance * 2f, diameter),
            preferredClearance = clearance,
            teamBias = teamBias
        };
    }

    static float Noise01(Vector2 point, int seed, int salt)
    {
        float offsetX = Hash01(seed ^ salt, 193) * 1024f;
        float offsetY = Hash01(seed ^ salt, 211) * 1024f;
        float first = Mathf.PerlinNoise(
            point.x + offsetX,
            point.y + offsetY);
        float second = Mathf.PerlinNoise(
            point.x * 2.07f + offsetY,
            point.y * 2.07f + offsetX);
        return Mathf.Clamp01(first * 0.68f + second * 0.32f);
    }

    static float Hash01(int seed, int salt)
    {
        unchecked
        {
            uint value = (uint)seed;
            value ^= (uint)salt * 0x9E3779B9u;
            value ^= value >> 16;
            value *= 0x85EBCA6Bu;
            value ^= value >> 13;
            value *= 0xC2B2AE35u;
            value ^= value >> 16;
            return (value & 0x00FFFFFFu) / 16777215f;
        }
    }
}

/// <summary>
/// Pure-data height plan for a temporary planet combat instance. Combat
/// semantics shape the playable airspace first; climate noise is applied only
/// where it cannot destroy spawn basins, recovery pockets or protected routes.
/// It deliberately creates no building or decoration GameObjects.
/// </summary>
[Serializable]
public sealed class FinitePlanetCombatTerrainPlan
{
    [SerializeField] AirCombatMapSettings settings;
    [SerializeField] CombatSemanticPlan semanticPlan;
    [SerializeField] CombatMapValidationReport validation;
    [SerializeField] FinitePlanetDefenseLayoutPlan defenseLayout;
    [SerializeField] PlanetClimate climate;
    [SerializeField] FinitePlanetCombatMissionTerrainKind missionKind;
    [SerializeField] string missionId;
    [SerializeField] int seed;
    [SerializeField] float combatRadius;
    [SerializeField] float baseGroundHeight;
    [SerializeField] float recommendedFlightCeilingHeight;
    [SerializeField] float boundaryWallHeight;

    public AirCombatMapSettings Settings => settings;
    public CombatSemanticPlan SemanticPlan => semanticPlan;
    public CombatMapValidationReport Validation => validation;
    public FinitePlanetDefenseLayoutPlan DefenseLayout => defenseLayout;
    public PlanetClimate Climate => climate;
    public FinitePlanetCombatMissionTerrainKind MissionKind => missionKind;
    public string MissionId => missionId;
    public int Seed => seed;
    public float CombatRadius => combatRadius;
    public float BaseGroundHeight => baseGroundHeight;
    public float RecommendedFlightCeilingHeight =>
        recommendedFlightCeilingHeight;
    public float BoundaryWallHeight => boundaryWallHeight;
    public float MaximumTerrainHeightAboveBase =>
        settings == null
            ? Mathf.Max(0f, boundaryWallHeight * 1.12f)
            : Mathf.Max(
                settings.mountainHeight * 1.65f,
                boundaryWallHeight * 1.12f);

    internal FinitePlanetCombatTerrainPlan(
        AirCombatMapSettings valueSettings,
        CombatSemanticPlan valueSemanticPlan,
        CombatMapValidationReport valueValidation,
        FinitePlanetDefenseLayoutPlan valueDefenseLayout,
        PlanetClimate valueClimate,
        FinitePlanetCombatMissionTerrainKind valueMissionKind,
        string valueMissionId,
        int valueSeed,
        float valueCombatRadius,
        float valueBaseGroundHeight)
    {
        settings = valueSettings
            ?? throw new ArgumentNullException(nameof(valueSettings));
        semanticPlan = valueSemanticPlan
            ?? throw new ArgumentNullException(nameof(valueSemanticPlan));
        validation = valueValidation;
        defenseLayout = valueDefenseLayout
            ?? throw new ArgumentNullException(nameof(valueDefenseLayout));
        climate = valueClimate;
        missionKind = valueMissionKind;
        missionId = valueMissionId ?? string.Empty;
        seed = valueSeed;
        combatRadius = Mathf.Max(128f, valueCombatRadius);
        baseGroundHeight = valueBaseGroundHeight;
        // The combat band must intersect the terrain silhouette. When the
        // ceiling sits above every peak, terrain becomes visual decoration and
        // line-of-sight/route choices disappear from play.
        recommendedFlightCeilingHeight = Mathf.Clamp(
            settings.mountainHeight * 0.72f + 50f,
            190f,
            300f);
        boundaryWallHeight = recommendedFlightCeilingHeight
            + Mathf.Max(72f, settings.vehicleWingspan * 4f);
    }

    public float SampleHeight(float planarX, float planarZ)
    {
        if (settings == null || semanticPlan == null)
            return baseGroundHeight;
        float semanticHeight = CombatMapGenerator.SampleHeight(
            settings,
            semanticPlan,
            planarX,
            planarZ);
        float protectedMask = CalculateProtectedMask(planarX, planarZ);
        float freeMask = Mathf.Pow(
            Mathf.Clamp01(1f - protectedMask),
            2f);
        float climateHeight = SampleClimateHeight(planarX, planarZ)
            * freeMask;

        float distance = Mathf.Sqrt(
            planarX * planarX + planarZ * planarZ);
        float edge = Smooth01(Mathf.InverseLerp(
            combatRadius * 0.76f,
            combatRadius * 0.94f,
            distance));
        // The boundary is a continuous collision-backed mountain wall. Noise
        // changes its silhouette but never creates a low saddle that a ship
        // can use as an exit.
        float edgeRidge = Mathf.Lerp(
            0.94f,
            1.08f,
            Ridge01(
                planarX / 260f,
                planarZ / 260f,
                seed ^ 0x5F356495,
                3));
        float boundaryHeight = edge * boundaryWallHeight * edgeRidge;

        float height = semanticHeight + climateHeight + boundaryHeight;
        float minimum = baseGroundHeight
            - (climate == PlanetClimate.Tropical ? 18f : 8f);
        float maximum = baseGroundHeight
            + MaximumTerrainHeightAboveBase;
        return Mathf.Clamp(height, minimum, maximum);
    }

    float CalculateProtectedMask(float planarX, float planarZ)
    {
        if (semanticPlan == null || semanticPlan.terrainStamps == null)
            return 0f;
        var point = new Vector2(planarX, planarZ);
        float result = 0f;
        for (int index = 0;
             index < semanticPlan.terrainStamps.Length;
             index++)
        {
            CombatTerrainStamp stamp = semanticPlan.terrainStamps[index];
            if (stamp == null || !IsProtectedStamp(stamp.type))
                continue;
            float distance = DistanceToSegment(
                point,
                new Vector2(stamp.start.x, stamp.start.z),
                new Vector2(stamp.end.x, stamp.end.z));
            float mask = 1f - Smooth01(Mathf.InverseLerp(
                stamp.radius,
                stamp.radius + Mathf.Max(1f, stamp.falloff),
                distance));
            result = Mathf.Max(result, mask);
        }
        return result;
    }

    float SampleClimateHeight(float x, float z)
    {
        float broad = FractalSigned(
            x / 480f,
            z / 480f,
            seed ^ 0x2C9277B5,
            4);
        float medium = FractalSigned(
            x / 170f,
            z / 170f,
            seed ^ 0x165667B1,
            3);
        float ridge = Ridge01(
            x / 230f,
            z / 230f,
            seed ^ 0x27D4EB2D,
            4);
        float relief = settings.mountainHeight;

        switch (climate)
        {
            case PlanetClimate.Barren:
                return broad * 9f
                    + medium * 4f
                    + Mathf.Pow(ridge, 3f) * relief * 0.12f
                    + SampleCraterField(x, z, 4, relief * 0.16f);

            case PlanetClimate.TemperateForest:
                return broad * 15f
                    + medium * 7f
                    + Mathf.Pow(ridge, 3.4f) * relief * 0.13f;

            case PlanetClimate.Desert:
            {
                float terraced = Mathf.Round(broad * 4f) * 3.2f;
                float dune = FractalSigned(
                    x / 95f,
                    z / 260f,
                    seed ^ 0x68E31DA4,
                    2) * 2.2f;
                return terraced
                    + dune
                    + medium * 4f
                    + Mathf.Pow(ridge, 2.6f) * relief * 0.2f;
            }

            case PlanetClimate.Tropical:
                return broad * 18f
                    + medium * 8f
                    + Mathf.Pow(ridge, 5f) * relief * 0.24f
                    - Mathf.Max(0f, -broad) * 12f;

            case PlanetClimate.Tundra:
            {
                float glacierRidge = Ridge01(
                    x / 410f,
                    z / 145f,
                    seed ^ unchecked((int)0xB5297A4Du),
                    4);
                return broad * 10f
                    + medium * 4f
                    + Mathf.Pow(glacierRidge, 3.6f)
                    * relief * 0.2f;
            }

            case PlanetClimate.Volcanic:
                return broad * 18f
                    + medium * 7f
                    + Mathf.Pow(ridge, 2.4f) * relief * 0.23f
                    + SampleVolcanicField(x, z, 3, relief * 0.28f);

            case PlanetClimate.Crystal:
            {
                float shards = Mathf.Pow(ridge, 5.2f)
                    * relief * 0.31f;
                float facets = Mathf.Round(medium * 3f) * 2.4f;
                return broad * 8f + shards + facets;
            }

            default:
                return broad * 10f + medium * 5f;
        }
    }

    float SampleCraterField(
        float x,
        float z,
        int count,
        float depth)
    {
        float result = 0f;
        for (int index = 0; index < count; index++)
        {
            Vector2 center = FeatureCenter(index, 0x1B873593);
            float radius = Mathf.Lerp(
                72f,
                145f,
                Hash01(
                    seed ^ unchecked((int)0x85EBCA6Bu),
                    index * 7 + 3));
            float normalized = Vector2.Distance(
                new Vector2(x, z),
                center) / radius;
            if (normalized >= 1.28f)
                continue;
            float bowl = -depth
                * (1f - Smooth01(Mathf.InverseLerp(0.08f, 0.82f, normalized)));
            float rimDistance = Mathf.Abs(normalized - 1f);
            float rim = depth * 0.72f
                * (1f - Smooth01(Mathf.InverseLerp(0.03f, 0.28f, rimDistance)));
            result += bowl + rim;
        }
        return result;
    }

    float SampleVolcanicField(
        float x,
        float z,
        int count,
        float height)
    {
        float result = 0f;
        for (int index = 0; index < count; index++)
        {
            Vector2 center = FeatureCenter(index, 0x7FEB352D);
            float radius = Mathf.Lerp(
                120f,
                220f,
                Hash01(
                    seed ^ unchecked((int)0x846CA68Bu),
                    index * 11 + 5));
            float normalized = Vector2.Distance(
                new Vector2(x, z),
                center) / radius;
            if (normalized >= 1f)
                continue;
            float cone = Mathf.Pow(1f - normalized, 1.45f) * height;
            float crater = Mathf.Pow(
                Mathf.Clamp01(1f - normalized / 0.22f),
                2f) * height * 0.82f;
            result += cone - crater;
        }
        return result;
    }

    Vector2 FeatureCenter(int index, int salt)
    {
        float angle = Hash01(seed ^ salt, index * 2 + 1)
            * Mathf.PI * 2f;
        float radius = Mathf.Lerp(
            combatRadius * 0.28f,
            combatRadius * 0.68f,
            Hash01(seed ^ salt, index * 2 + 2));
        return new Vector2(
            Mathf.Cos(angle) * radius,
            Mathf.Sin(angle) * radius);
    }

    static bool IsProtectedStamp(CombatTerrainStampType type)
    {
        return type == CombatTerrainStampType.Basin
            || type == CombatTerrainStampType.Corridor
            || type == CombatTerrainStampType.RoadBed
            || type == CombatTerrainStampType.BuildingPad;
    }

    static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        Vector2 segment = end - start;
        float denominator = segment.sqrMagnitude;
        if (denominator <= 0.0001f)
            return Vector2.Distance(point, start);
        float along = Mathf.Clamp01(
            Vector2.Dot(point - start, segment) / denominator);
        return Vector2.Distance(point, start + segment * along);
    }

    static float FractalSigned(
        float x,
        float z,
        int valueSeed,
        int octaves)
    {
        float offsetX = Hash01(valueSeed, 101) * 2048f;
        float offsetZ = Hash01(valueSeed, 211) * 2048f;
        float frequency = 1f;
        float amplitude = 1f;
        float total = 0f;
        float normalization = 0f;
        for (int octave = 0; octave < octaves; octave++)
        {
            float noise = Mathf.PerlinNoise(
                x * frequency + offsetX + octave * 13.17f,
                z * frequency + offsetZ + octave * 29.31f) * 2f - 1f;
            total += noise * amplitude;
            normalization += amplitude;
            frequency *= 2.03f;
            amplitude *= 0.51f;
        }
        return normalization > 0f ? total / normalization : 0f;
    }

    static float Ridge01(
        float x,
        float z,
        int valueSeed,
        int octaves)
    {
        return 1f - Mathf.Abs(FractalSigned(x, z, valueSeed, octaves));
    }

    static float Hash01(int valueSeed, int salt)
    {
        unchecked
        {
            uint value = (uint)valueSeed;
            value ^= (uint)salt * 0x9E3779B9u;
            value ^= value >> 16;
            value *= 0x85EBCA6Bu;
            value ^= value >> 13;
            value *= 0xC2B2AE35u;
            value ^= value >> 16;
            return (value & 0x00FFFFFFu) / 16777215f;
        }
    }

    static float Smooth01(float value)
    {
        value = Mathf.Clamp01(value);
        return value * value * (3f - 2f * value);
    }

    static float BoundaryReliefMultiplier(PlanetClimate value)
    {
        switch (value)
        {
            case PlanetClimate.Volcanic:
                return 0.58f;
            case PlanetClimate.Crystal:
                return 0.52f;
            case PlanetClimate.Desert:
            case PlanetClimate.Tundra:
                return 0.48f;
            default:
                return 0.42f;
        }
    }
}

public static class FinitePlanetCombatTerrainPlanner
{
    const string TraverseMissionId = "wind_canyon";
    const string AssaultMissionId = "industrial_outpost";
    const string BossMissionId = "modular_boss";

    public static FinitePlanetCombatTerrainPlan Create(
        GalaxyPlanetDefinition definition,
        string missionId,
        int missionSeed,
        float combatRadius,
        float seaHeight,
        bool oceanEnabled)
    {
        if (definition == null)
            throw new ArgumentNullException(nameof(definition));

        FinitePlanetCombatMissionTerrainKind missionKind =
            ResolveMissionKind(missionId);
        int seed = StableSeed(
            definition.seed,
            missionSeed,
            definition.climate,
            missionId);
        AirCombatMapSettings settings = BuildSettings(
            definition.climate,
            missionKind,
            seed,
            combatRadius);
        float baseGroundHeight = oceanEnabled ? seaHeight + 12f : 0f;
        CombatMapGenerationResult result = CombatMapGenerator.GenerateBest(
            settings,
            new Vector3(0f, baseGroundHeight, 0f),
            seed);
        if (result == null || result.plan == null)
        {
            result = CombatMapGenerator.Generate(
                settings,
                new Vector3(0f, baseGroundHeight, 0f),
                seed);
        }
        if (result == null || result.plan == null)
        {
            throw new InvalidOperationException(
                "Combat semantic terrain planning returned no usable plan.");
        }

        if (result.validation == null || !result.validation.CanCommit)
        {
            throw new InvalidOperationException(
                "Combat semantic terrain planning failed its hard tactical "
                + "constraints for climate=" + definition.climate
                + ", mission='" + (missionId ?? string.Empty) + "'.");
        }

        FinitePlanetDefenseLayoutPlan defenseLayout =
            FinitePlanetDefenseLayoutPlanner.Apply(
            result.plan,
            settings,
            missionKind,
            seed,
            combatRadius);
        AppendMissionTerrainStamps(
            result.plan,
            settings,
            missionKind,
            seed,
            combatRadius);
        result.plan.checksum = CombatMapGenerator.ComputeChecksum(
            settings,
            result.plan);
        result.validation.checksum = result.plan.checksum;

        return new FinitePlanetCombatTerrainPlan(
            settings,
            result.plan,
            result.validation,
            defenseLayout,
            definition.climate,
            missionKind,
            missionId,
            seed,
            combatRadius,
            baseGroundHeight);
    }

    /// <summary>
    /// Builds only the deterministic mission contract needed by the formal
    /// city runtime. It deliberately skips generic terrain candidate
    /// generation and validation: the AirCombatCity generator owns the final
    /// battlefield geometry and its own hard validation.
    /// </summary>
    public static FinitePlanetCombatTerrainPlan CreateUrbanFoundation(
        GalaxyPlanetDefinition definition,
        string missionId,
        int missionSeed,
        float combatRadius,
        float seaHeight,
        bool oceanEnabled)
    {
        if (definition == null)
            throw new ArgumentNullException(nameof(definition));

        FinitePlanetCombatMissionTerrainKind missionKind =
            ResolveMissionKind(missionId);
        int seed = StableSeed(
            definition.seed,
            missionSeed,
            definition.climate,
            missionId);
        AirCombatMapSettings settings = BuildSettings(
            definition.climate,
            missionKind,
            seed,
            combatRadius);
        settings.theme = CombatMapTheme.Urban;
        float baseGroundHeight = oceanEnabled ? seaHeight + 12f : 0f;
        var semanticPlan = new CombatSemanticPlan
        {
            generatorVersion = settings.generatorVersion,
            seed = seed,
            mode = AirCombatMapMode.Horde,
            theme = CombatMapTheme.Urban,
            mapCenter = new Vector3(0f, baseGroundHeight, 0f),
            mapSize = settings.mapSize,
            warningRadius = settings.warningRadius,
            forfeitRadius = settings.forfeitRadius,
            flightCeiling = settings.maximumGroundClearance
        };
        FinitePlanetDefenseLayoutPlan defenseLayout =
            FinitePlanetDefenseLayoutPlanner.Apply(
                semanticPlan,
                settings,
                missionKind,
                seed,
                combatRadius);
        var validation = new CombatMapValidationReport
        {
            score = 100f,
            minimumCommitScore = 0f,
            checksum = "urban-foundation-" + seed
        };
        semanticPlan.checksum = validation.checksum;

        return new FinitePlanetCombatTerrainPlan(
            settings,
            semanticPlan,
            validation,
            defenseLayout,
            definition.climate,
            missionKind,
            missionId,
            seed,
            combatRadius,
            baseGroundHeight);
    }

    static AirCombatMapSettings BuildSettings(
        PlanetClimate climate,
        FinitePlanetCombatMissionTerrainKind missionKind,
        int seed,
        float combatRadius)
    {
        AirCombatMapSettings settings = AirCombatMapSettings.CreateDefault();
        // Planet missions receive threats from several perimeter entrances;
        // they are not mirrored 1v1 maps.
        settings.mode = AirCombatMapMode.Horde;
        settings.theme = CombatMapTheme.Natural;
        settings.seed = seed;
        settings.generatorVersion = 3;
        settings.candidateCount = 6;
        settings.maximumCandidateBatches = 3;
        settings.minimumCommitScore = 80f;
        settings.layoutOptimizationIterations = 48;
        settings.validationGridResolution = 9;
        settings.designCombatSpeed = 55f;
        settings.designTurnRadius = 95f;
        settings.designWeaponRange = 480f;
        settings.vehicleWingspan = 18f;

        float edgeSafety = AirCombatMapSettings.RequiredEdgeSafetyMargin(
            settings.designCombatSpeed,
            settings.designTurnRadius,
            settings.vehicleWingspan);
        settings.mapSize = (combatRadius + edgeSafety) * 2f;
        settings.warningRadius = combatRadius * 0.86f;
        settings.forfeitRadius = combatRadius;
        settings.spawnDistance = combatRadius * 1.24f;
        settings.spawnClearance = 48f;
        settings.maximumGroundClearance = 260f;
        settings.occluderTowerCount = 8;

        switch (missionKind)
        {
            case FinitePlanetCombatMissionTerrainKind.Traverse:
                settings.mountainHeight = 280f;
                settings.mainRouteWidth = 228f;
                settings.canyonRouteWidth = 220f;
                settings.longRangeRouteWidth = 246f;
                settings.microNoiseStrength = 3.8f;
                settings.microNoiseScale = 86f;
                settings.occluderTowerCount = 6;
                break;

            case FinitePlanetCombatMissionTerrainKind.Assault:
                settings.mountainHeight = 250f;
                settings.mainRouteWidth = 248f;
                settings.canyonRouteWidth = 205f;
                settings.longRangeRouteWidth = 235f;
                settings.microNoiseStrength = 4.2f;
                settings.microNoiseScale = 78f;
                settings.occluderTowerCount = 10;
                break;

            default:
                settings.mountainHeight = 230f;
                settings.mainRouteWidth = 235f;
                settings.canyonRouteWidth = 198f;
                settings.longRangeRouteWidth = 238f;
                settings.microNoiseStrength = 4.5f;
                settings.microNoiseScale = 82f;
                break;
        }

        settings.mountainHeight *= ClimateReliefMultiplier(climate);
        settings.microNoiseStrength *= ClimateDetailMultiplier(climate);
        settings.Clamp();
        return settings;
    }

    static void AppendMissionTerrainStamps(
        CombatSemanticPlan plan,
        AirCombatMapSettings settings,
        FinitePlanetCombatMissionTerrainKind missionKind,
        int seed,
        float combatRadius)
    {
        var stamps = new List<CombatTerrainStamp>();
        if (plan.terrainStamps != null)
            stamps.AddRange(plan.terrainStamps);

        switch (missionKind)
        {
            case FinitePlanetCombatMissionTerrainKind.Traverse:
                AddTraverseStamps(
                    stamps,
                    settings,
                    seed,
                    combatRadius,
                    plan.mapCenter.y);
                break;

            case FinitePlanetCombatMissionTerrainKind.Assault:
                AddAssaultStamps(
                    stamps,
                    settings,
                    seed,
                    combatRadius,
                    plan.mapCenter.y);
                break;

            default:
                AddClearancePockets(
                    stamps,
                    settings,
                    seed,
                    combatRadius,
                    plan.mapCenter.y);
                break;
        }
        plan.terrainStamps = stamps.ToArray();
    }

    static void AddClearancePockets(
        List<CombatTerrainStamp> stamps,
        AirCombatMapSettings settings,
        int seed,
        float combatRadius,
        float centerY)
    {
        float radius = combatRadius * 0.29f;
        float phase = Hash01(seed, 211) * Mathf.PI * 2f;
        for (int index = 0; index < 3; index++)
        {
            float angle = phase + index * Mathf.PI * 2f / 3f;
            Vector2 radial = new Vector2(
                Mathf.Cos(angle),
                Mathf.Sin(angle));
            Vector2 tangent = new Vector2(-radial.y, radial.x);
            Vector3 point = new Vector3(
                radial.x * radius,
                centerY,
                radial.y * radius);
            stamps.Add(Stamp(
                "mission.clearance.pocket." + index,
                CombatTerrainStampType.Basin,
                point,
                point,
                58f,
                34f,
                5f));

            Vector2 ridgeCenter = radial * combatRadius * 0.43f;
            Vector2 ridgeHalf = tangent * combatRadius * 0.075f;
            stamps.Add(Stamp(
                "mission.clearance.ridge." + index,
                CombatTerrainStampType.RidgeCapsule,
                new Vector3(
                    ridgeCenter.x - ridgeHalf.x,
                    centerY,
                    ridgeCenter.y - ridgeHalf.y),
                new Vector3(
                    ridgeCenter.x + ridgeHalf.x,
                    centerY,
                    ridgeCenter.y + ridgeHalf.y),
                combatRadius * 0.052f,
                combatRadius * 0.035f,
                settings.mountainHeight * 1.12f));
        }
    }

    static void AddTraverseStamps(
        List<CombatTerrainStamp> stamps,
        AirCombatMapSettings settings,
        int seed,
        float combatRadius,
        float centerY)
    {
        const int segmentCount = 7;
        float halfLength = combatRadius * 0.69f;
        float phase = Hash01(seed, 71) * Mathf.PI * 2f;
        float amplitude = combatRadius * Mathf.Lerp(
            0.13f,
            0.2f,
            Hash01(seed, 83));
        // Keep the mountain core beyond the corridor's grading falloff. The
        // old overlapping radii caused whole ridge segments to be carved down
        // together with the safe flight lane.
        float ridgeOffset = settings.canyonRouteWidth;
        float ridgeRadius = settings.canyonRouteWidth * 0.28f;

        for (int index = 0; index < segmentCount; index++)
        {
            float t0 = index / (float)segmentCount;
            float t1 = (index + 1f) / segmentCount;
            Vector2 first = TraverseCenter(t0, halfLength, amplitude, phase);
            Vector2 second = TraverseCenter(t1, halfLength, amplitude, phase);
            Vector2 tangent = (second - first).normalized;
            Vector2 normal = new Vector2(-tangent.y, tangent.x);
            Vector3 start = new Vector3(first.x, centerY, first.y);
            Vector3 end = new Vector3(second.x, centerY, second.y);
            stamps.Add(Stamp(
                "mission.traverse.corridor." + index,
                CombatTerrainStampType.Corridor,
                start,
                end,
                settings.canyonRouteWidth * 0.5f,
                48f,
                4f));
            for (int side = -1; side <= 1; side += 2)
            {
                Vector2 offset = normal * ridgeOffset * side;
                stamps.Add(Stamp(
                    "mission.traverse.ridge." + index + "." + side,
                    CombatTerrainStampType.RidgeCapsule,
                    start + new Vector3(offset.x, 0f, offset.y),
                    end + new Vector3(offset.x, 0f, offset.y),
                    ridgeRadius,
                    56f,
                    settings.mountainHeight * 0.96f));
            }
        }
    }

    static void AddAssaultStamps(
        List<CombatTerrainStamp> stamps,
        AirCombatMapSettings settings,
        int seed,
        float combatRadius,
        float centerY)
    {
        float lateral = combatRadius * 0.27f;
        float forward = combatRadius * Mathf.Lerp(
            0.12f,
            0.2f,
            Hash01(seed, 103));
        Vector2[] positions =
        {
            new Vector2(-lateral, forward * 0.35f),
            new Vector2(0f, forward + combatRadius * 0.12f),
            new Vector2(lateral, forward * 0.35f)
        };
        for (int index = 0; index < positions.Length; index++)
        {
            Vector3 point = new Vector3(
                positions[index].x,
                centerY,
                positions[index].y);
            stamps.Add(Stamp(
                "mission.assault.mesa." + index,
                CombatTerrainStampType.MesaCapsule,
                point,
                point,
                105f,
                58f,
                settings.mountainHeight * 0.98f));
            stamps.Add(Stamp(
                "mission.assault.pad." + index,
                CombatTerrainStampType.Basin,
                point,
                point,
                62f,
                28f,
                settings.mountainHeight * 0.34f));

            Vector2 radial = positions[index].sqrMagnitude > 0.001f
                ? positions[index].normalized
                : Vector2.up;
            Vector2 tangent = new Vector2(-radial.y, radial.x);
            Vector2 ridgeCenter = positions[index]
                + radial * combatRadius * 0.16f;
            Vector2 ridgeHalf = tangent * combatRadius * 0.065f;
            stamps.Add(Stamp(
                "mission.assault.ridge." + index,
                CombatTerrainStampType.RidgeCapsule,
                new Vector3(
                    ridgeCenter.x - ridgeHalf.x,
                    centerY,
                    ridgeCenter.y - ridgeHalf.y),
                new Vector3(
                    ridgeCenter.x + ridgeHalf.x,
                    centerY,
                    ridgeCenter.y + ridgeHalf.y),
                combatRadius * 0.048f,
                combatRadius * 0.034f,
                settings.mountainHeight * 1.12f));
        }
    }

    static Vector2 TraverseCenter(
        float normalized,
        float halfLength,
        float amplitude,
        float phase)
    {
        float z = Mathf.Lerp(-halfLength, halfLength, normalized);
        float x = Mathf.Sin(normalized * Mathf.PI * 1.65f + phase)
            * amplitude;
        return new Vector2(x, z);
    }

    static CombatTerrainStamp Stamp(
        string id,
        CombatTerrainStampType type,
        Vector3 start,
        Vector3 end,
        float radius,
        float falloff,
        float height)
    {
        return new CombatTerrainStamp
        {
            stableId = id,
            type = type,
            start = start,
            end = end,
            radius = radius,
            falloff = falloff,
            height = height
        };
    }

    static FinitePlanetCombatMissionTerrainKind ResolveMissionKind(
        string missionId)
    {
        if (string.Equals(
                missionId,
                TraverseMissionId,
                StringComparison.Ordinal))
        {
            return FinitePlanetCombatMissionTerrainKind.Traverse;
        }
        if (string.Equals(
                missionId,
                AssaultMissionId,
                StringComparison.Ordinal) ||
            string.Equals(
                missionId,
                BossMissionId,
                StringComparison.Ordinal))
        {
            return FinitePlanetCombatMissionTerrainKind.Assault;
        }
        return FinitePlanetCombatMissionTerrainKind.Clearance;
    }

    static float ClimateReliefMultiplier(PlanetClimate climate)
    {
        switch (climate)
        {
            case PlanetClimate.TemperateForest:
                return 0.82f;
            case PlanetClimate.Tropical:
                return 1.08f;
            case PlanetClimate.Tundra:
                return 1.02f;
            case PlanetClimate.Volcanic:
                return 1.28f;
            case PlanetClimate.Crystal:
                return 1.18f;
            case PlanetClimate.Desert:
                return 1.1f;
            default:
                return 0.92f;
        }
    }

    static float ClimateDetailMultiplier(PlanetClimate climate)
    {
        switch (climate)
        {
            case PlanetClimate.TemperateForest:
            case PlanetClimate.Tropical:
                return 1.15f;
            case PlanetClimate.Volcanic:
                return 1.08f;
            case PlanetClimate.Crystal:
            case PlanetClimate.Tundra:
                return 0.82f;
            default:
                return 0.95f;
        }
    }

    static int StableSeed(
        int planetSeed,
        int missionSeed,
        PlanetClimate climate,
        string missionId)
    {
        unchecked
        {
            uint hash = 2166136261u;
            hash = (hash ^ (uint)planetSeed) * 16777619u;
            hash = (hash ^ (uint)missionSeed) * 16777619u;
            hash = (hash ^ (uint)climate) * 16777619u;
            string value = missionId ?? string.Empty;
            for (int index = 0; index < value.Length; index++)
                hash = (hash ^ value[index]) * 16777619u;
            return (int)hash;
        }
    }

    static float Hash01(int valueSeed, int salt)
    {
        unchecked
        {
            uint value = (uint)valueSeed;
            value ^= (uint)salt * 0x9E3779B9u;
            value ^= value >> 16;
            value *= 0x85EBCA6Bu;
            value ^= value >> 13;
            return (value & 0x00FFFFFFu) / 16777215f;
        }
    }
}
