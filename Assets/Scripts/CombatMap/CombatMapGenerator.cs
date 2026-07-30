using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityPlanet.CombatMap
{
    /// <summary>
    /// Deterministic, pure-data generator for the CombatMapLab layout.
    /// It creates combat semantics first. Geometry is a later projection of
    /// the plan through <see cref="SampleHeight"/>.
    /// </summary>
    public static class CombatMapGenerator
    {
        const float PlayerSide = -1f;
        const float EnemySide = 1f;

        public static CombatMapGenerationResult Generate(
            AirCombatMapSettings sourceSettings,
            Vector3 mapCenter,
            int seed,
            int candidateIndex = 0)
        {
            AirCombatMapSettings settings =
                (sourceSettings ?? AirCombatMapSettings.CreateDefault())
                .ValidatedCopy(seed);
            int derivedSeed = DeriveCandidateSeed(
                seed,
                Mathf.Max(0, candidateIndex));
            CombatSemanticPlan plan = BuildPlan(
                settings,
                mapCenter,
                derivedSeed);
            plan.checksum = ComputeChecksum(settings, plan);
            CombatMapValidationReport validation =
                CombatMapValidator.Validate(settings, plan);
            validation.checksum = plan.checksum;

            return new CombatMapGenerationResult
            {
                candidateIndex = candidateIndex,
                derivedSeed = derivedSeed,
                plan = plan,
                validation = validation
            };
        }

        public static CombatMapGenerationResult GenerateBest(
            AirCombatMapSettings sourceSettings,
            Vector3 mapCenter,
            int baseSeed)
        {
            AirCombatMapSettings settings =
                (sourceSettings ?? AirCombatMapSettings.CreateDefault())
                .ValidatedCopy(baseSeed);
            CombatMapGenerationResult best = null;
            int candidateIndex = 0;
            int total = settings.candidateCount
                * settings.maximumCandidateBatches;
            for (int index = 0; index < total; index++)
            {
                CombatMapGenerationResult candidate = Generate(
                    settings,
                    mapCenter,
                    baseSeed,
                    candidateIndex++);
                if (best == null
                    || CompareCandidates(candidate, best) > 0)
                {
                    best = candidate;
                }

                if (index + 1 >= settings.candidateCount
                    && (index + 1) % settings.candidateCount == 0
                    && best != null
                    && best.CanCommit
                    && best.validation.score >= 85f
                    && MinimumCriticalScore(best.validation) >= 60f)
                {
                    break;
                }
            }
            return best;
        }

        public static int DeriveCandidateSeed(
            int baseSeed,
            int candidateIndex)
        {
            unchecked
            {
                uint value = (uint)baseSeed;
                value ^= (uint)(candidateIndex + 1) * 0x9E3779B9u;
                value ^= value >> 16;
                value *= 0x85EBCA6Bu;
                value ^= value >> 13;
                value *= 0xC2B2AE35u;
                value ^= value >> 16;
                return (int)value;
            }
        }

        public static float SampleHeight(
            AirCombatMapSettings sourceSettings,
            CombatSemanticPlan plan,
            float worldX,
            float worldZ)
        {
            AirCombatMapSettings settings =
                sourceSettings ?? AirCombatMapSettings.CreateDefault();
            if (plan == null)
                return 0f;
            if (plan.schemaVersion >= 2)
            {
                return CombatMapGeneratorV2.SampleHeight(
                    settings,
                    plan,
                    worldX,
                    worldZ);
            }

            float x = worldX - plan.mapCenter.x;
            float z = worldZ - plan.mapCenter.z;
            float half = settings.mapSize * 0.5f;
            float baseHeight = plan.mapCenter.y;

            // The central ridge exists to break the 1400 m opening
            // sightline. A shallow saddle retains a risky high route.
            float ridgeRadiusX = settings.mapSize * 0.19f;
            float ridgeRadiusZ = settings.mapSize * 0.115f;
            float centralRidge = settings.mountainHeight
                * Gaussian2D(x, z, ridgeRadiusX, ridgeRadiusZ);
            float saddle = settings.mountainHeight * 0.28f
                * Gaussian2D(
                    x,
                    z,
                    settings.mainRouteWidth * 0.34f,
                    ridgeRadiusZ * 0.8f);
            float height = baseHeight
                + Mathf.Max(0f, centralRidge - saddle);

            // West: a terrain-masked low route between two ridges.
            float canyonX = -settings.mapSize * 0.255f;
            float canyonLengthMask = Gaussian1D(
                z,
                settings.mapSize * 0.44f);
            float canyonHalfWidth = settings.canyonRouteWidth * 0.5f;
            float canyonCut = 16f
                * Gaussian1D(x - canyonX, canyonHalfWidth * 0.62f)
                * canyonLengthMask;
            float westRidgeA = settings.mountainHeight * 0.38f
                * Gaussian2D(
                    x - (canyonX - canyonHalfWidth * 0.78f),
                    z,
                    canyonHalfWidth * 0.38f,
                    settings.mapSize * 0.34f);
            float westRidgeB = settings.mountainHeight * 0.34f
                * Gaussian2D(
                    x - (canyonX + canyonHalfWidth * 0.82f),
                    z,
                    canyonHalfWidth * 0.4f,
                    settings.mapSize * 0.34f);
            height += westRidgeA + westRidgeB - canyonCut;

            // East: a deliberately readable, flatter long-range lane.
            float longRangeX = settings.mapSize * 0.255f;
            float corridorMask = Gaussian1D(
                x - longRangeX,
                settings.longRangeRouteWidth * 0.48f)
                * Gaussian1D(z, settings.mapSize * 0.46f);
            height = Mathf.Lerp(
                height,
                baseHeight + 4f,
                Mathf.Clamp01(corridorMask * 0.86f));

            // Spawn bowls provide predictable clearances and prevent mesh
            // noise from creating unfair opening collision hazards.
            Vector2 playerLocal = new Vector2(
                0f,
                PlayerSide * settings.spawnDistance * 0.5f);
            Vector2 enemyLocal = new Vector2(
                0f,
                EnemySide * settings.spawnDistance * 0.5f);
            float bowlRadius = Mathf.Max(90f, settings.mainRouteWidth);
            float spawnMask = Mathf.Max(
                RadialMask(new Vector2(x, z), playerLocal, bowlRadius),
                RadialMask(new Vector2(x, z), enemyLocal, bowlRadius));
            height = Mathf.Lerp(height, baseHeight + 2f, spawnMask);

            float routeProtection = ComputeRouteProtection(
                plan,
                worldX,
                worldZ);
            float edgeMask = 1f - Mathf.SmoothStep(
                half * 0.76f,
                half * 0.98f,
                Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)));
            float detailMask = Mathf.Clamp01(
                (1f - routeProtection)
                * (1f - spawnMask)
                * edgeMask);
            float detail = FractalValueNoise(
                x / settings.microNoiseScale,
                z / settings.microNoiseScale,
                plan.seed);
            height += detail
                * settings.microNoiseStrength
                * detailMask;

            return IsFinite(height) ? height : baseHeight;
        }

        public static bool HasTerrainLineOfSight(
            AirCombatMapSettings settings,
            CombatSemanticPlan plan,
            Vector3 from,
            Vector3 to,
            float terrainMargin = 3f,
            int sampleCount = 64)
        {
            if (plan == null)
                return false;
            int count = Mathf.Clamp(sampleCount, 8, 256);
            for (int i = 1; i < count; i++)
            {
                float t = i / (float)count;
                Vector3 point = Vector3.LerpUnclamped(from, to, t);
                float ground = SampleHeight(
                    settings,
                    plan,
                    point.x,
                    point.z);
                if (ground + terrainMargin >= point.y)
                    return false;
            }

            if (plan.occluders != null)
            {
                for (int i = 0; i < plan.occluders.Length; i++)
                {
                    CombatOccluderData occluder = plan.occluders[i];
                    if (occluder == null
                        || occluder.type != CombatOccluderType.Tower)
                    {
                        continue;
                    }
                    Bounds bounds = new Bounds(
                        occluder.position,
                        occluder.size);
                    if (SegmentIntersectsBounds(from, to, bounds))
                        return false;
                }
            }
            return true;
        }

        public static string ComputeChecksum(
            AirCombatMapSettings settings,
            CombatSemanticPlan plan)
        {
            if (plan != null && plan.schemaVersion >= 2)
            {
                return CombatMapGeneratorV2.ComputeChecksum(
                    settings,
                    plan);
            }
            ulong hash = 14695981039346656037UL;
            Add(ref hash, plan.schemaVersion);
            Add(ref hash, plan.generatorVersion);
            Add(ref hash, plan.seed);
            Add(ref hash, plan.mapCenter);
            Add(ref hash, plan.mapSize);
            Add(ref hash, settings.chunkSize);
            Add(ref hash, settings.chunkResolution);
            Add(ref hash, settings.mountainHeight);
            Add(ref hash, settings.microNoiseStrength);

            if (plan.anchors != null)
            {
                for (int i = 0; i < plan.anchors.Length; i++)
                {
                    CombatSemanticAnchor value = plan.anchors[i];
                    if (value == null)
                        continue;
                    Add(ref hash, value.stableId);
                    Add(ref hash, (int)value.type);
                    Add(ref hash, value.position);
                    Add(ref hash, value.forward);
                    Add(ref hash, value.radius);
                }
            }

            if (plan.routes != null)
            {
                for (int i = 0; i < plan.routes.Length; i++)
                {
                    CombatSemanticRoute value = plan.routes[i];
                    if (value == null)
                        continue;
                    Add(ref hash, value.stableId);
                    Add(ref hash, (int)value.type);
                    Add(ref hash, value.width);
                    if (value.waypoints == null)
                        continue;
                    for (int point = 0;
                         point < value.waypoints.Length;
                         point++)
                    {
                        Add(ref hash, value.waypoints[point]);
                    }
                }
            }

            if (plan.occluders != null)
            {
                for (int i = 0; i < plan.occluders.Length; i++)
                {
                    CombatOccluderData value = plan.occluders[i];
                    if (value == null)
                        continue;
                    Add(ref hash, value.stableId);
                    Add(ref hash, (int)value.type);
                    Add(ref hash, value.position);
                    Add(ref hash, value.size);
                }
            }
            return hash.ToString("X16");
        }

        static CombatSemanticPlan BuildPlan(
            AirCombatMapSettings settings,
            Vector3 center,
            int derivedSeed)
        {
            return CombatMapGeneratorV2.BuildPlan(
                settings,
                center,
                derivedSeed);
        }

        static CombatSemanticPlan BuildPlanLegacy(
            AirCombatMapSettings settings,
            Vector3 center,
            int derivedSeed)
        {
            var random = new DeterministicRandom(derivedSeed);
            float halfSpawn = settings.spawnDistance * 0.5f;
            float lateralVariation = random.Range(-24f, 24f);
            float canyonX = -settings.mapSize * 0.255f
                + random.Range(-18f, 18f);
            float longRangeX = settings.mapSize * 0.255f
                + random.Range(-18f, 18f);

            var plan = new CombatSemanticPlan
            {
                schemaVersion = 1,
                generatorVersion = settings.generatorVersion,
                seed = derivedSeed,
                mapCenter = center,
                mapSize = settings.mapSize,
                warningRadius = settings.warningRadius,
                forfeitRadius = settings.forfeitRadius
            };

            Vector3 playerGround = new Vector3(
                center.x + lateralVariation,
                center.y,
                center.z - halfSpawn);
            Vector3 enemyGround = new Vector3(
                center.x - lateralVariation,
                center.y,
                center.z + halfSpawn);
            playerGround.y = SampleHeight(
                settings,
                plan,
                playerGround.x,
                playerGround.z);
            enemyGround.y = SampleHeight(
                settings,
                plan,
                enemyGround.x,
                enemyGround.z);
            Vector3 playerSpawn = playerGround
                + Vector3.up * settings.spawnClearance;
            Vector3 enemySpawn = enemyGround
                + Vector3.up * settings.spawnClearance;

            plan.anchors = new[]
            {
                Anchor(
                    "spawn.player",
                    CombatAnchorType.PlayerSpawn,
                    playerSpawn,
                    DirectionOnPlane(center - playerSpawn),
                    64f),
                Anchor(
                    "spawn.enemy",
                    CombatAnchorType.EnemySpawn,
                    enemySpawn,
                    DirectionOnPlane(center - enemySpawn),
                    64f),
                Anchor(
                    "conflict.central",
                    CombatAnchorType.CentralConflict,
                    new Vector3(
                        center.x,
                        SampleHeight(
                            settings,
                            plan,
                            center.x,
                            center.z)
                        + settings.minimumGroundClearance + 20f,
                        center.z),
                    Vector3.forward,
                    settings.mainRouteWidth * 0.5f),
                Anchor(
                    "retreat.player",
                    CombatAnchorType.PlayerRetreat,
                    OffsetWithGroundClearance(
                        settings,
                        plan,
                        center,
                        -settings.mapSize * 0.22f,
                        -settings.mapSize * 0.35f,
                        settings.spawnClearance),
                    Vector3.forward,
                    70f),
                Anchor(
                    "retreat.enemy",
                    CombatAnchorType.EnemyRetreat,
                    OffsetWithGroundClearance(
                        settings,
                        plan,
                        center,
                        settings.mapSize * 0.22f,
                        settings.mapSize * 0.35f,
                        settings.spawnClearance),
                    Vector3.back,
                    70f),
                Anchor(
                    "power.west",
                    CombatAnchorType.PowerPosition,
                    OffsetWithGroundClearance(
                        settings,
                        plan,
                        center,
                        canyonX,
                        0f,
                        52f),
                    Vector3.right,
                    55f),
                Anchor(
                    "power.east",
                    CombatAnchorType.PowerPosition,
                    OffsetWithGroundClearance(
                        settings,
                        plan,
                        center,
                        longRangeX,
                        0f,
                        72f),
                    Vector3.left,
                    55f),
                Anchor(
                    "landmark.central-ridge",
                    CombatAnchorType.Landmark,
                    new Vector3(
                        center.x,
                        center.y + settings.mountainHeight,
                        center.z),
                    Vector3.up,
                    90f)
            };

            plan.routes = new[]
            {
                Route(
                    "route.main",
                    CombatRouteType.Main,
                    settings.mainRouteWidth,
                    0.72f,
                    playerSpawn,
                    OffsetWithGroundClearance(
                        settings,
                        plan,
                        center,
                        0f,
                        -settings.mapSize * 0.16f,
                        62f),
                    OffsetWithGroundClearance(
                        settings,
                        plan,
                        center,
                        0f,
                        0f,
                        66f),
                    OffsetWithGroundClearance(
                        settings,
                        plan,
                        center,
                        0f,
                        settings.mapSize * 0.16f,
                        62f),
                    enemySpawn),
                Route(
                    "route.flank.west",
                    CombatRouteType.TerrainMaskedFlank,
                    settings.canyonRouteWidth,
                    0.28f,
                    playerSpawn,
                    OffsetWithGroundClearance(
                        settings,
                        plan,
                        center,
                        canyonX * 0.75f,
                        -settings.mapSize * 0.24f,
                        36f),
                    OffsetWithGroundClearance(
                        settings,
                        plan,
                        center,
                        canyonX,
                        0f,
                        34f),
                    OffsetWithGroundClearance(
                        settings,
                        plan,
                        center,
                        canyonX * 0.75f,
                        settings.mapSize * 0.24f,
                        36f),
                    enemySpawn),
                Route(
                    "route.long-range.east",
                    CombatRouteType.LongRange,
                    settings.longRangeRouteWidth,
                    0.86f,
                    playerSpawn,
                    OffsetWithGroundClearance(
                        settings,
                        plan,
                        center,
                        longRangeX * 0.78f,
                        -settings.mapSize * 0.24f,
                        72f),
                    OffsetWithGroundClearance(
                        settings,
                        plan,
                        center,
                        longRangeX,
                        0f,
                        78f),
                    OffsetWithGroundClearance(
                        settings,
                        plan,
                        center,
                        longRangeX * 0.78f,
                        settings.mapSize * 0.24f,
                        72f),
                    enemySpawn),
                Route(
                    "route.retreat.player",
                    CombatRouteType.Retreat,
                    settings.canyonRouteWidth,
                    0.18f,
                    playerSpawn,
                    OffsetWithGroundClearance(
                        settings,
                        plan,
                        center,
                        -settings.mapSize * 0.22f,
                        -settings.mapSize * 0.35f,
                        settings.spawnClearance),
                    OffsetWithGroundClearance(
                        settings,
                        plan,
                        center,
                        -settings.mapSize * 0.34f,
                        -settings.mapSize * 0.2f,
                        settings.spawnClearance + 4f)),
                Route(
                    "route.retreat.enemy",
                    CombatRouteType.Retreat,
                    settings.canyonRouteWidth,
                    0.18f,
                    enemySpawn,
                    OffsetWithGroundClearance(
                        settings,
                        plan,
                        center,
                        settings.mapSize * 0.22f,
                        settings.mapSize * 0.35f,
                        settings.spawnClearance),
                    OffsetWithGroundClearance(
                        settings,
                        plan,
                        center,
                        settings.mapSize * 0.34f,
                        settings.mapSize * 0.2f,
                        settings.spawnClearance + 4f))
            };

            var occluders = new List<CombatOccluderData>(
                settings.occluderTowerCount + 1)
            {
                new CombatOccluderData
                {
                    stableId = "occluder.central-ridge",
                    type = CombatOccluderType.Ridge,
                    position = new Vector3(
                        center.x,
                        center.y + settings.mountainHeight * 0.5f,
                        center.z),
                    size = new Vector3(
                        settings.mapSize * 0.38f,
                        settings.mountainHeight,
                        settings.mapSize * 0.23f),
                    color = new Color(0.32f, 0.38f, 0.28f)
                }
            };

            for (int index = 0;
                 index < settings.occluderTowerCount;
                 index++)
            {
                float laneOffset = random.Range(
                    -settings.longRangeRouteWidth * 0.44f,
                    settings.longRangeRouteWidth * 0.44f);
                float x = center.x + longRangeX + laneOffset;
                float z = center.z + Mathf.Lerp(
                    -settings.mapSize * 0.31f,
                    settings.mapSize * 0.31f,
                    (index + 0.5f)
                    / Mathf.Max(1f, settings.occluderTowerCount));
                z += random.Range(-38f, 38f);
                float width = random.Range(20f, 34f);
                float depth = random.Range(20f, 38f);
                float towerHeight = random.Range(52f, 92f);
                float ground = SampleHeight(settings, plan, x, z);
                occluders.Add(new CombatOccluderData
                {
                    stableId = "occluder.tower."
                        + index.ToString("D2"),
                    type = CombatOccluderType.Tower,
                    position = new Vector3(
                        x,
                        ground + towerHeight * 0.5f,
                        z),
                    size = new Vector3(width, towerHeight, depth),
                    color = index % 2 == 0
                        ? new Color(0.28f, 0.34f, 0.4f)
                        : new Color(0.38f, 0.3f, 0.24f)
                });
            }
            plan.occluders = occluders.ToArray();
            return plan;
        }

        static CombatSemanticAnchor Anchor(
            string id,
            CombatAnchorType type,
            Vector3 position,
            Vector3 forward,
            float radius)
        {
            return new CombatSemanticAnchor
            {
                stableId = id,
                type = type,
                position = position,
                forward = forward.sqrMagnitude > 0.001f
                    ? forward.normalized
                    : Vector3.forward,
                radius = radius
            };
        }

        static CombatSemanticRoute Route(
            string id,
            CombatRouteType type,
            float width,
            float exposure,
            params Vector3[] waypoints)
        {
            return new CombatSemanticRoute
            {
                stableId = id,
                type = type,
                width = width,
                intendedExposure = exposure,
                waypoints = waypoints ?? Array.Empty<Vector3>()
            };
        }

        static Vector3 OffsetWithGroundClearance(
            AirCombatMapSettings settings,
            CombatSemanticPlan plan,
            Vector3 center,
            float localX,
            float localZ,
            float clearance)
        {
            float x = center.x + localX;
            float z = center.z + localZ;
            return new Vector3(
                x,
                SampleHeight(settings, plan, x, z) + clearance,
                z);
        }

        static Vector3 DirectionOnPlane(Vector3 value)
        {
            value.y = 0f;
            return value.sqrMagnitude > 0.001f
                ? value.normalized
                : Vector3.forward;
        }

        static int CompareCandidates(
            CombatMapGenerationResult a,
            CombatMapGenerationResult b)
        {
            bool aValid = a != null && a.CanCommit;
            bool bValid = b != null && b.CanCommit;
            if (aValid != bValid)
                return aValid ? 1 : -1;
            float aCritical = MinimumCriticalScore(
                a?.validation);
            float bCritical = MinimumCriticalScore(
                b?.validation);
            int critical = aCritical.CompareTo(bCritical);
            if (critical != 0)
                return critical;
            float aScore = a?.validation?.score ?? float.MinValue;
            float bScore = b?.validation?.score ?? float.MinValue;
            int score = aScore.CompareTo(bScore);
            if (score != 0)
                return score;
            return -(a?.candidateIndex ?? int.MaxValue).CompareTo(
                b?.candidateIndex ?? int.MaxValue);
        }

        static float MinimumCriticalScore(
            CombatMapValidationReport report)
        {
            if (report == null)
                return float.MinValue;
            if (report.topologyScore <= 0f
                && report.kinematicScore <= 0f
                && report.coverRhythmScore <= 0f)
            {
                return report.score;
            }
            return Mathf.Min(
                report.topologyScore,
                Mathf.Min(
                    report.kinematicScore,
                    Mathf.Min(
                        report.coverRhythmScore,
                        report.scaleCompatibilityScore)));
        }

        static float ComputeRouteProtection(
            CombatSemanticPlan plan,
            float worldX,
            float worldZ)
        {
            if (plan.routes == null)
                return 0f;
            var point = new Vector2(worldX, worldZ);
            float protection = 0f;
            for (int routeIndex = 0;
                 routeIndex < plan.routes.Length;
                 routeIndex++)
            {
                CombatSemanticRoute route = plan.routes[routeIndex];
                if (route?.waypoints == null
                    || route.waypoints.Length < 2)
                {
                    continue;
                }
                float distance = float.PositiveInfinity;
                for (int i = 1; i < route.waypoints.Length; i++)
                {
                    Vector3 a3 = route.waypoints[i - 1];
                    Vector3 b3 = route.waypoints[i];
                    distance = Mathf.Min(
                        distance,
                        DistanceToSegment(
                            point,
                            new Vector2(a3.x, a3.z),
                            new Vector2(b3.x, b3.z)));
                }
                float mask = 1f - Mathf.SmoothStep(
                    route.width * 0.35f,
                    route.width * 0.72f,
                    distance);
                protection = Mathf.Max(protection, mask);
            }
            return Mathf.Clamp01(protection);
        }

        static float DistanceToSegment(
            Vector2 point,
            Vector2 start,
            Vector2 end)
        {
            Vector2 delta = end - start;
            float lengthSquared = delta.sqrMagnitude;
            if (lengthSquared < 0.0001f)
                return Vector2.Distance(point, start);
            float t = Mathf.Clamp01(
                Vector2.Dot(point - start, delta) / lengthSquared);
            return Vector2.Distance(point, start + delta * t);
        }

        static float Gaussian1D(float value, float radius)
        {
            float safeRadius = Mathf.Max(0.001f, radius);
            float normalized = value / safeRadius;
            return Mathf.Exp(-0.5f * normalized * normalized);
        }

        static float Gaussian2D(
            float x,
            float z,
            float radiusX,
            float radiusZ)
        {
            float nx = x / Mathf.Max(0.001f, radiusX);
            float nz = z / Mathf.Max(0.001f, radiusZ);
            return Mathf.Exp(-0.5f * (nx * nx + nz * nz));
        }

        static float RadialMask(
            Vector2 point,
            Vector2 center,
            float radius)
        {
            float distance = Vector2.Distance(point, center);
            return 1f - Mathf.SmoothStep(
                radius * 0.45f,
                radius,
                distance);
        }

        static float FractalValueNoise(float x, float z, int seed)
        {
            float first = ValueNoise(x, z, seed);
            float second = ValueNoise(
                x * 2.07f + 19.31f,
                z * 2.07f - 7.17f,
                seed ^ 0x2C9277B5);
            return (first * 0.68f + second * 0.32f) * 2f - 1f;
        }

        static float ValueNoise(float x, float z, int seed)
        {
            int x0 = Mathf.FloorToInt(x);
            int z0 = Mathf.FloorToInt(z);
            int x1 = x0 + 1;
            int z1 = z0 + 1;
            float tx = Smooth(x - x0);
            float tz = Smooth(z - z0);
            float a = Hash01(x0, z0, seed);
            float b = Hash01(x1, z0, seed);
            float c = Hash01(x0, z1, seed);
            float d = Hash01(x1, z1, seed);
            return Mathf.Lerp(
                Mathf.Lerp(a, b, tx),
                Mathf.Lerp(c, d, tx),
                tz);
        }

        static float Smooth(float value)
        {
            return value * value * (3f - 2f * value);
        }

        static float Hash01(int x, int z, int seed)
        {
            unchecked
            {
                uint value = (uint)seed;
                value ^= (uint)x * 0x9E3779B1u;
                value ^= (uint)z * 0x85EBCA77u;
                value ^= value >> 16;
                value *= 0x7FEB352Du;
                value ^= value >> 15;
                value *= 0x846CA68Bu;
                value ^= value >> 16;
                return (value & 0x00FFFFFFu) / 16777215f;
            }
        }

        static bool SegmentIntersectsBounds(
            Vector3 from,
            Vector3 to,
            Bounds bounds)
        {
            Vector3 direction = to - from;
            float tMinimum = 0f;
            float tMaximum = 1f;
            for (int axis = 0; axis < 3; axis++)
            {
                float origin = from[axis];
                float delta = direction[axis];
                float minimum = bounds.min[axis];
                float maximum = bounds.max[axis];
                if (Mathf.Abs(delta) < 0.00001f)
                {
                    if (origin < minimum || origin > maximum)
                        return false;
                    continue;
                }
                float inverse = 1f / delta;
                float first = (minimum - origin) * inverse;
                float second = (maximum - origin) * inverse;
                if (first > second)
                {
                    float swap = first;
                    first = second;
                    second = swap;
                }
                tMinimum = Mathf.Max(tMinimum, first);
                tMaximum = Mathf.Min(tMaximum, second);
                if (tMinimum > tMaximum)
                    return false;
            }
            return true;
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        static void Add(ref ulong hash, string value)
        {
            if (value == null)
            {
                Add(ref hash, -1);
                return;
            }
            for (int i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash *= 1099511628211UL;
            }
        }

        static void Add(ref ulong hash, int value)
        {
            unchecked
            {
                uint bits = (uint)value;
                for (int i = 0; i < 4; i++)
                {
                    hash ^= (byte)(bits >> (i * 8));
                    hash *= 1099511628211UL;
                }
            }
        }

        static void Add(ref ulong hash, float value)
        {
            Add(ref hash, Mathf.RoundToInt(value * 1000f));
        }

        static void Add(ref ulong hash, Vector3 value)
        {
            Add(ref hash, value.x);
            Add(ref hash, value.y);
            Add(ref hash, value.z);
        }

        struct DeterministicRandom
        {
            uint state;

            public DeterministicRandom(int seed)
            {
                state = (uint)seed;
                if (state == 0u)
                    state = 0x6D2B79F5u;
            }

            public float Range(float minimum, float maximum)
            {
                uint value = Next();
                float unit = (value & 0x00FFFFFFu) / 16777215f;
                return Mathf.Lerp(minimum, maximum, unit);
            }

            uint Next()
            {
                uint value = state;
                value ^= value << 13;
                value ^= value >> 17;
                value ^= value << 5;
                state = value;
                return value;
            }
        }
    }
}
