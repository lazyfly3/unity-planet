using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityPlanet.CombatMap
{
    /// <summary>
    /// Semantic-first V2 generator. It solves a small constrained tactical
    /// layout, projects that graph to curvature-friendly routes, and only
    /// then fits a compact-support 2.5D SDF terrain around the airspace.
    /// </summary>
    internal static class CombatMapGeneratorV2
    {
        const float PlayerSide = -1f;
        const float EnemySide = 1f;

        sealed class Layout
        {
            public int variant;
            public float handedness;
            public float bowlAmplitude;
            public float bowlZ;
            public float flankX;
            public float longX;
            public float gateZ;
            public float recoveryZ;
            public float ridgeYaw;
            public float ridgeAmplitude;
        }

        struct StableRandom
        {
            uint state;

            public StableRandom(int seed)
            {
                state = (uint)seed;
                if (state == 0u)
                    state = 0xA341316Cu;
            }

            public float Value()
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                return (state & 0x00FFFFFFu) / 16777216f;
            }

            public float Range(float minimum, float maximum)
            {
                return Mathf.Lerp(minimum, maximum, Value());
            }
        }

        public static CombatSemanticPlan BuildPlan(
            AirCombatMapSettings settings,
            Vector3 center,
            int derivedSeed)
        {
            Layout layout = SolveLayout(settings, derivedSeed);
            var plan = new CombatSemanticPlan
            {
                schemaVersion = 2,
                generatorVersion = Mathf.Max(
                    2,
                    settings.generatorVersion),
                seed = derivedSeed,
                topologyVariant = layout.variant,
                mapCenter = center,
                mapSize = settings.mapSize,
                warningRadius = settings.warningRadius,
                forfeitRadius = settings.forfeitRadius
            };

            float turn = settings.designTurnRadius;
            float bowlDiameter = Mathf.Clamp(
                Mathf.Max(
                    turn * 4.2f,
                    settings.mainRouteWidth * 1.35f),
                220f,
                settings.mapSize * 0.30f);
            float spawnDiameter = Mathf.Clamp(
                Mathf.Max(
                    turn * 2.8f,
                    settings.mainRouteWidth * 1.2f),
                180f,
                settings.mapSize * 0.24f);
            float recoveryDiameter = Mathf.Clamp(
                Mathf.Max(
                    turn * 3.3f,
                    settings.canyonRouteWidth * 1.45f),
                200f,
                settings.mapSize * 0.27f);
            float gateDiameter = Mathf.Clamp(
                Mathf.Max(
                    turn * 1.65f,
                    settings.canyonRouteWidth),
                140f,
                settings.mapSize * 0.20f);

            float halfSpawn = settings.spawnDistance * 0.5f;
            var volumes = new List<CombatTacticalVolume>
            {
                Volume(
                    "volume.spawn.player",
                    CombatTacticalVolumeType.SpawnBasin,
                    center,
                    0f,
                    -halfSpawn,
                    spawnDiameter,
                    settings.spawnClearance,
                    -1),
                Volume(
                    "volume.spawn.enemy",
                    CombatTacticalVolumeType.SpawnBasin,
                    center,
                    0f,
                    halfSpawn,
                    spawnDiameter,
                    settings.spawnClearance,
                    1),
                Volume(
                    "volume.bowl.south",
                    CombatTacticalVolumeType.ManeuverBowl,
                    center,
                    -layout.handedness * layout.bowlAmplitude,
                    -layout.bowlZ,
                    bowlDiameter,
                    Mathf.Max(58f, turn * 0.62f),
                    0),
                Volume(
                    "volume.bowl.center",
                    CombatTacticalVolumeType.ManeuverBowl,
                    center,
                    layout.handedness
                        * layout.bowlAmplitude * 0.86f,
                    0f,
                    bowlDiameter,
                    Mathf.Max(72f, turn * 0.78f),
                    0),
                Volume(
                    "volume.bowl.north",
                    CombatTacticalVolumeType.ManeuverBowl,
                    center,
                    -layout.handedness * layout.bowlAmplitude,
                    layout.bowlZ,
                    bowlDiameter,
                    Mathf.Max(58f, turn * 0.62f),
                    0),
                Volume(
                    "volume.gate.mask.south",
                    CombatTacticalVolumeType.OcclusionGate,
                    center,
                    layout.flankX,
                    -layout.gateZ,
                    gateDiameter,
                    Mathf.Max(30f, turn * 0.34f),
                    0),
                Volume(
                    "volume.gate.mask.north",
                    CombatTacticalVolumeType.OcclusionGate,
                    center,
                    layout.flankX,
                    layout.gateZ,
                    gateDiameter,
                    Mathf.Max(30f, turn * 0.34f),
                    0),
                Volume(
                    "volume.lane.exposed.south",
                    CombatTacticalVolumeType.ExposureLane,
                    center,
                    layout.longX,
                    -layout.gateZ,
                    gateDiameter * 1.08f,
                    Mathf.Max(88f, turn * 0.95f),
                    0),
                Volume(
                    "volume.lane.exposed.north",
                    CombatTacticalVolumeType.ExposureLane,
                    center,
                    layout.longX,
                    layout.gateZ,
                    gateDiameter * 1.08f,
                    Mathf.Max(88f, turn * 0.95f),
                    0),
                Volume(
                    "volume.recovery.player.mask",
                    CombatTacticalVolumeType.RecoveryPocket,
                    center,
                    layout.flankX * 0.66f,
                    -layout.recoveryZ,
                    recoveryDiameter,
                    Mathf.Max(38f, turn * 0.42f),
                    -1),
                Volume(
                    "volume.recovery.player.open",
                    CombatTacticalVolumeType.RecoveryPocket,
                    center,
                    layout.longX * 0.66f,
                    -layout.recoveryZ,
                    recoveryDiameter,
                    Mathf.Max(52f, turn * 0.56f),
                    -1),
                Volume(
                    "volume.recovery.enemy.mask",
                    CombatTacticalVolumeType.RecoveryPocket,
                    center,
                    layout.flankX * 0.66f,
                    layout.recoveryZ,
                    recoveryDiameter,
                    Mathf.Max(38f, turn * 0.42f),
                    1),
                Volume(
                    "volume.recovery.enemy.open",
                    CombatTacticalVolumeType.RecoveryPocket,
                    center,
                    layout.longX * 0.66f,
                    layout.recoveryZ,
                    recoveryDiameter,
                    Mathf.Max(52f, turn * 0.56f),
                    1)
            };
            plan.tacticalVolumes = volumes.ToArray();

            string[] mainIds =
            {
                "volume.spawn.player",
                "volume.bowl.south",
                "volume.bowl.center",
                "volume.bowl.north",
                "volume.spawn.enemy"
            };
            string[] maskedIds =
            {
                "volume.spawn.player",
                "volume.recovery.player.mask",
                "volume.gate.mask.south",
                "volume.gate.mask.north",
                "volume.recovery.enemy.mask",
                "volume.spawn.enemy"
            };
            string[] longIds =
            {
                "volume.spawn.player",
                "volume.recovery.player.open",
                "volume.lane.exposed.south",
                "volume.lane.exposed.north",
                "volume.recovery.enemy.open",
                "volume.spawn.enemy"
            };
            string[] playerRetreatIds = layout.variant == 1
                ? new[]
                {
                    "volume.spawn.player",
                    "volume.recovery.player.mask",
                    "volume.gate.mask.south",
                    "volume.bowl.south",
                    "volume.recovery.player.open",
                    "volume.spawn.player"
                }
                : new[]
                {
                    "volume.spawn.player",
                    "volume.recovery.player.mask",
                    "volume.bowl.south",
                    "volume.recovery.player.open",
                    "volume.spawn.player"
                };
            string[] enemyRetreatIds = layout.variant == 2
                ? new[]
                {
                    "volume.spawn.enemy",
                    "volume.recovery.enemy.mask",
                    "volume.gate.mask.north",
                    "volume.bowl.north",
                    "volume.recovery.enemy.open",
                    "volume.spawn.enemy"
                }
                : new[]
                {
                    "volume.spawn.enemy",
                    "volume.recovery.enemy.mask",
                    "volume.bowl.north",
                    "volume.recovery.enemy.open",
                    "volume.spawn.enemy"
                };

            plan.terrainStamps = BuildTerrainStamps(
                settings,
                plan,
                layout,
                mainIds,
                maskedIds,
                longIds,
                playerRetreatIds,
                enemyRetreatIds);

            ResolveVolumeHeights(settings, plan);
            plan.routes = new[]
            {
                BuildRoute(
                    settings,
                    plan,
                    "route.main",
                    CombatRouteType.Main,
                    settings.mainRouteWidth,
                    0.62f,
                    mainIds),
                BuildRoute(
                    settings,
                    plan,
                    "route.flank.masked",
                    CombatRouteType.TerrainMaskedFlank,
                    settings.canyonRouteWidth,
                    0.34f,
                    maskedIds),
                BuildRoute(
                    settings,
                    plan,
                    "route.long-range",
                    CombatRouteType.LongRange,
                    settings.longRangeRouteWidth,
                    0.78f,
                    longIds),
                BuildRoute(
                    settings,
                    plan,
                    "route.retreat.player",
                    CombatRouteType.Retreat,
                    settings.canyonRouteWidth,
                    0.24f,
                    playerRetreatIds),
                BuildRoute(
                    settings,
                    plan,
                    "route.retreat.enemy",
                    CombatRouteType.Retreat,
                    settings.canyonRouteWidth,
                    0.24f,
                    enemyRetreatIds)
            };

            CombatTacticalVolume player = plan.FindVolume(
                "volume.spawn.player");
            CombatTacticalVolume enemy = plan.FindVolume(
                "volume.spawn.enemy");
            CombatTacticalVolume centerBowl = plan.FindVolume(
                "volume.bowl.center");
            CombatTacticalVolume playerRecovery = plan.FindVolume(
                "volume.recovery.player.mask");
            CombatTacticalVolume enemyRecovery = plan.FindVolume(
                "volume.recovery.enemy.mask");
            CombatTacticalVolume maskGate = plan.FindVolume(
                "volume.gate.mask.south");
            CombatTacticalVolume exposedLane = plan.FindVolume(
                "volume.lane.exposed.south");

            plan.anchors = new[]
            {
                Anchor(
                    "spawn.player",
                    CombatAnchorType.PlayerSpawn,
                    player.position,
                    DirectionOnPlane(center - player.position),
                    player.HorizontalDiameter * 0.28f),
                Anchor(
                    "spawn.enemy",
                    CombatAnchorType.EnemySpawn,
                    enemy.position,
                    DirectionOnPlane(center - enemy.position),
                    enemy.HorizontalDiameter * 0.28f),
                Anchor(
                    "conflict.central",
                    CombatAnchorType.CentralConflict,
                    centerBowl.position,
                    Vector3.forward,
                    centerBowl.HorizontalDiameter * 0.5f),
                Anchor(
                    "retreat.player",
                    CombatAnchorType.PlayerRetreat,
                    playerRecovery.position,
                    Vector3.forward,
                    playerRecovery.HorizontalDiameter * 0.3f),
                Anchor(
                    "retreat.enemy",
                    CombatAnchorType.EnemyRetreat,
                    enemyRecovery.position,
                    Vector3.back,
                    enemyRecovery.HorizontalDiameter * 0.3f),
                Anchor(
                    "power.masked-gate",
                    CombatAnchorType.PowerPosition,
                    maskGate.position,
                    Vector3.right,
                    maskGate.HorizontalDiameter * 0.28f),
                Anchor(
                    "power.exposed-lane",
                    CombatAnchorType.PowerPosition,
                    exposedLane.position,
                    Vector3.left,
                    exposedLane.HorizontalDiameter * 0.28f),
                Anchor(
                    "landmark.broken-ridge",
                    CombatAnchorType.Landmark,
                    new Vector3(
                        center.x,
                        center.y + settings.mountainHeight,
                        center.z),
                    Vector3.up,
                    settings.mapSize * 0.07f)
            };

            plan.occluders = BuildTowerClusters(
                settings,
                plan,
                layout,
                derivedSeed);
            return plan;
        }

        public static float SampleHeight(
            AirCombatMapSettings settings,
            CombatSemanticPlan plan,
            float worldX,
            float worldZ)
        {
            if (plan == null)
                return 0f;
            Vector2 point = new Vector2(worldX, worldZ);
            float localX = worldX - plan.mapCenter.x;
            float localZ = worldZ - plan.mapCenter.z;
            float macroScale = Mathf.Max(
                48f,
                settings.mapSize * 0.23f);
            float macro = EvenFractalNoise(
                localX / macroScale,
                localZ / macroScale,
                plan.seed) * Mathf.Min(
                    9f,
                    settings.mountainHeight * 0.055f);
            float baseHeight = plan.mapCenter.y + 3f + macro;
            float raised = baseHeight;

            if (plan.terrainStamps != null)
            {
                for (int i = 0; i < plan.terrainStamps.Length; i++)
                {
                    CombatTerrainStamp stamp = plan.terrainStamps[i];
                    if (stamp == null
                        || (stamp.type
                            != CombatTerrainStampType.RidgeCapsule
                            && stamp.type
                            != CombatTerrainStampType.MesaCapsule))
                    {
                        continue;
                    }
                    float distance = DistanceToSegment(
                        point,
                        new Vector2(stamp.start.x, stamp.start.z),
                        new Vector2(stamp.end.x, stamp.end.z));
                    float mask = CompactMask(
                        distance,
                        stamp.radius * 0.16f,
                        stamp.radius + stamp.falloff);
                    float power = stamp.type
                        == CombatTerrainStampType.MesaCapsule
                            ? 1.05f
                            : 1.42f;
                    float feature = baseHeight
                        + stamp.height
                        * Mathf.Pow(mask, power);
                    raised = Mathf.Max(raised, feature);
                }
            }

            float carveWeight = 0f;
            float carveTarget = 0f;
            float carveMask = 0f;
            if (plan.terrainStamps != null)
            {
                for (int i = 0; i < plan.terrainStamps.Length; i++)
                {
                    CombatTerrainStamp stamp = plan.terrainStamps[i];
                    if (stamp == null
                        || (stamp.type
                            != CombatTerrainStampType.Basin
                            && stamp.type
                            != CombatTerrainStampType.Corridor))
                    {
                        continue;
                    }
                    float distance = DistanceToSegment(
                        point,
                        new Vector2(stamp.start.x, stamp.start.z),
                        new Vector2(stamp.end.x, stamp.end.z));
                    float mask = CompactMask(
                        distance,
                        stamp.radius,
                        stamp.radius + stamp.falloff);
                    float weight = mask * mask;
                    carveWeight += weight;
                    carveTarget += weight
                        * (plan.mapCenter.y + stamp.height);
                    carveMask = Mathf.Max(carveMask, mask);
                }
            }
            float height = raised;
            if (carveWeight > 0.0001f)
            {
                float target = carveTarget / carveWeight;
                height = Mathf.Lerp(
                    raised,
                    Mathf.Min(raised, target),
                    carveMask);
            }

            float half = settings.mapSize * 0.5f;
            float squareRadius = Mathf.Max(
                Mathf.Abs(localX),
                Mathf.Abs(localZ));
            float edge = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(
                    half * 0.86f,
                    half,
                    squareRadius));
            height += edge * settings.mountainHeight * 0.08f;

            float detail = EvenFractalNoise(
                localX / settings.microNoiseScale,
                localZ / settings.microNoiseScale,
                plan.seed ^ 0x632BE59B);
            float detailMask = Mathf.Pow(
                Mathf.Clamp01(1f - carveMask),
                2f);
            height += detail
                * settings.microNoiseStrength
                * detailMask;
            return IsFinite(height) ? height : plan.mapCenter.y;
        }

        public static string ComputeChecksum(
            AirCombatMapSettings settings,
            CombatSemanticPlan plan)
        {
            ulong hash = 14695981039346656037UL;
            Add(ref hash, plan.schemaVersion);
            Add(ref hash, plan.generatorVersion);
            Add(ref hash, plan.seed);
            Add(ref hash, plan.topologyVariant);
            Add(ref hash, plan.mapCenter);
            Add(ref hash, plan.mapSize);
            Add(ref hash, plan.warningRadius);
            Add(ref hash, plan.forfeitRadius);
            Add(ref hash, settings.chunkSize);
            Add(ref hash, settings.chunkResolution);
            Add(ref hash, settings.spawnDistance);
            Add(ref hash, settings.spawnClearance);
            Add(ref hash, settings.designCombatSpeed);
            Add(ref hash, settings.designTurnRadius);
            Add(ref hash, settings.designWeaponRange);
            Add(ref hash, settings.vehicleWingspan);
            Add(ref hash, settings.targetFirstContactSeconds);
            Add(ref hash, settings.targetOcclusionSeconds);
            Add(ref hash, settings.targetExposureSeconds);
            Add(ref hash, settings.mountainHeight);
            Add(ref hash, settings.mainRouteWidth);
            Add(ref hash, settings.canyonRouteWidth);
            Add(ref hash, settings.longRangeRouteWidth);
            Add(ref hash, settings.occluderTowerCount);
            Add(ref hash, settings.microNoiseStrength);
            Add(ref hash, settings.microNoiseScale);

            if (plan.tacticalVolumes != null)
            {
                for (int i = 0; i < plan.tacticalVolumes.Length; i++)
                {
                    CombatTacticalVolume value =
                        plan.tacticalVolumes[i];
                    if (value == null)
                        continue;
                    Add(ref hash, value.stableId);
                    Add(ref hash, (int)value.type);
                    Add(ref hash, value.position);
                    Add(ref hash, value.size);
                    Add(ref hash, value.preferredClearance);
                    Add(ref hash, value.teamBias);
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
                    Add(ref hash, value.intendedExposure);
                    if (value.controlVolumeIds != null)
                    {
                        for (int id = 0;
                             id < value.controlVolumeIds.Length;
                             id++)
                        {
                            Add(ref hash, value.controlVolumeIds[id]);
                        }
                    }
                    if (value.waypoints != null)
                    {
                        for (int point = 0;
                             point < value.waypoints.Length;
                             point++)
                        {
                            Add(ref hash, value.waypoints[point]);
                        }
                    }
                }
            }
            if (plan.terrainStamps != null)
            {
                for (int i = 0; i < plan.terrainStamps.Length; i++)
                {
                    CombatTerrainStamp value = plan.terrainStamps[i];
                    if (value == null)
                        continue;
                    Add(ref hash, value.stableId);
                    Add(ref hash, (int)value.type);
                    Add(ref hash, value.start);
                    Add(ref hash, value.end);
                    Add(ref hash, value.radius);
                    Add(ref hash, value.falloff);
                    Add(ref hash, value.height);
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

        static Layout SolveLayout(
            AirCombatMapSettings settings,
            int seed)
        {
            var random = new StableRandom(seed);
            float size = settings.mapSize;
            var current = new Layout
            {
                variant = Mathf.Abs(seed % 3),
                handedness = random.Value() < 0.5f ? -1f : 1f,
                bowlAmplitude = size * random.Range(0.075f, 0.115f),
                bowlZ = size * random.Range(0.165f, 0.205f),
                flankX = 0f,
                longX = 0f,
                gateZ = size * random.Range(0.075f, 0.115f),
                recoveryZ = size * random.Range(0.245f, 0.285f),
                ridgeYaw = random.Range(-9f, 9f),
                ridgeAmplitude = size
                    * random.Range(0.105f, 0.135f)
            };
            float lane = size * random.Range(0.255f, 0.305f);
            current.flankX = -current.handedness * lane;
            current.longX = current.handedness * lane;
            ProjectLayout(settings, current);
            float currentEnergy = LayoutEnergy(settings, current);
            Layout best = Clone(current);
            float bestEnergy = currentEnergy;

            int iterations = settings.layoutOptimizationIterations;
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                Layout proposal = Clone(current);
                float temperature = Mathf.Lerp(
                    1f,
                    0.035f,
                    iteration / (float)Mathf.Max(1, iterations - 1));
                float step = size * Mathf.Lerp(0.028f, 0.003f, 1f - temperature);
                switch (iteration % 7)
                {
                    case 0:
                        proposal.bowlAmplitude +=
                            random.Range(-step, step);
                        break;
                    case 1:
                        proposal.bowlZ += random.Range(-step, step);
                        break;
                    case 2:
                        proposal.gateZ += random.Range(-step, step);
                        break;
                    case 3:
                        proposal.recoveryZ += random.Range(-step, step);
                        break;
                    case 4:
                    {
                        float magnitude = Mathf.Abs(proposal.flankX)
                            + random.Range(-step, step);
                        proposal.flankX =
                            -proposal.handedness * magnitude;
                        proposal.longX =
                            proposal.handedness * magnitude;
                        break;
                    }
                    case 5:
                        proposal.ridgeAmplitude +=
                            random.Range(-step, step);
                        break;
                    default:
                        proposal.ridgeYaw +=
                            random.Range(-2.5f, 2.5f);
                        break;
                }
                ProjectLayout(settings, proposal);
                float energy = LayoutEnergy(settings, proposal);
                float delta = currentEnergy - energy;
                bool accept = delta >= 0f
                    || random.Value()
                    < Mathf.Exp(
                        delta
                        / Mathf.Max(0.001f, temperature * 2.5f));
                if (accept)
                {
                    current = proposal;
                    currentEnergy = energy;
                }
                if (energy < bestEnergy)
                {
                    best = Clone(proposal);
                    bestEnergy = energy;
                }
            }
            return best;
        }

        static void ProjectLayout(
            AirCombatMapSettings settings,
            Layout layout)
        {
            float size = settings.mapSize;
            layout.bowlAmplitude = Mathf.Clamp(
                layout.bowlAmplitude,
                size * 0.07f,
                size * 0.13f);
            layout.bowlZ = Mathf.Clamp(
                layout.bowlZ,
                size * 0.155f,
                size * 0.215f);
            layout.gateZ = Mathf.Clamp(
                layout.gateZ,
                size * 0.065f,
                size * 0.125f);
            layout.recoveryZ = Mathf.Clamp(
                layout.recoveryZ,
                size * 0.235f,
                size * 0.295f);
            float lane = Mathf.Clamp(
                Mathf.Abs(layout.flankX),
                size * 0.245f,
                size * 0.315f);
            layout.flankX = -layout.handedness * lane;
            layout.longX = layout.handedness * lane;
            layout.ridgeAmplitude = Mathf.Clamp(
                layout.ridgeAmplitude,
                size * 0.095f,
                size * 0.145f);
            layout.ridgeYaw = Mathf.Clamp(
                layout.ridgeYaw,
                -12f,
                12f);
        }

        static float LayoutEnergy(
            AirCombatMapSettings settings,
            Layout layout)
        {
            float size = settings.mapSize;
            float requiredPassage = settings.designTurnRadius * 1.15f
                + settings.vehicleWingspan;
            float laneGap = Mathf.Abs(layout.flankX)
                - layout.bowlAmplitude;
            float energy = SquarePenalty(
                Mathf.Max(0f, requiredPassage - laneGap)
                / Mathf.Max(1f, requiredPassage));
            float desiredBowlZ = size * 0.185f;
            energy += 0.45f
                * SquarePenalty(
                    (layout.bowlZ - desiredBowlZ)
                    / Mathf.Max(1f, desiredBowlZ));
            float desiredContact = settings.targetFirstContactSeconds;
            float actualContact = settings.spawnDistance
                / Mathf.Max(1f, settings.designCombatSpeed * 2f);
            energy += 0.7f
                * SquarePenalty(
                    (actualContact - desiredContact)
                    / Mathf.Max(1f, desiredContact));
            float desiredRecovery = size * 0.265f;
            energy += 0.25f
                * SquarePenalty(
                    (layout.recoveryZ - desiredRecovery)
                    / Mathf.Max(1f, desiredRecovery));
            float desiredRidge = size * 0.12f;
            energy += 0.2f
                * SquarePenalty(
                    (layout.ridgeAmplitude - desiredRidge)
                    / Mathf.Max(1f, desiredRidge));
            return energy;
        }

        static float SquarePenalty(float value)
        {
            return value * value;
        }

        static Layout Clone(Layout source)
        {
            return new Layout
            {
                variant = source.variant,
                handedness = source.handedness,
                bowlAmplitude = source.bowlAmplitude,
                bowlZ = source.bowlZ,
                flankX = source.flankX,
                longX = source.longX,
                gateZ = source.gateZ,
                recoveryZ = source.recoveryZ,
                ridgeYaw = source.ridgeYaw,
                ridgeAmplitude = source.ridgeAmplitude
            };
        }

        static CombatTerrainStamp[] BuildTerrainStamps(
            AirCombatMapSettings settings,
            CombatSemanticPlan plan,
            Layout layout,
            string[] mainIds,
            string[] maskedIds,
            string[] longIds,
            string[] playerRetreatIds,
            string[] enemyRetreatIds)
        {
            var result = new List<CombatTerrainStamp>(48);
            BuildBrokenRidge(settings, plan, layout, result);

            if (plan.tacticalVolumes != null)
            {
                for (int i = 0; i < plan.tacticalVolumes.Length; i++)
                {
                    CombatTacticalVolume volume =
                        plan.tacticalVolumes[i];
                    if (volume == null)
                        continue;
                    float target = volume.type
                        == CombatTacticalVolumeType.SpawnBasin
                            ? 2f
                            : volume.type
                                == CombatTacticalVolumeType.ManeuverBowl
                                    ? 5f
                                    : 6f;
                    float radius = volume.HorizontalDiameter
                        * (volume.type
                            == CombatTacticalVolumeType.ManeuverBowl
                                ? 0.34f
                                : 0.38f);
                    // A maneuver bowl is an airspace guarantee, not a demand
                    // to flatten the whole ground footprint. Capping only the
                    // terrain carve preserves the central LOS-breaking ridge.
                    if (volume.type ==
                        CombatTacticalVolumeType.ManeuverBowl)
                    {
                        radius = Mathf.Min(
                            radius,
                            settings.designTurnRadius * 0.78f);
                    }
                    result.Add(Stamp(
                        "terrain.basin." + volume.stableId,
                        CombatTerrainStampType.Basin,
                        volume.position,
                        volume.position,
                        radius,
                        radius * 0.38f,
                        target));
                }
            }

            AddCorridorStamps(
                plan,
                result,
                maskedIds,
                settings.canyonRouteWidth,
                4f,
                "terrain.corridor.masked");
            AddCorridorStamps(
                plan,
                result,
                longIds,
                settings.longRangeRouteWidth,
                7f,
                "terrain.corridor.long");
            AddCorridorStamps(
                plan,
                result,
                playerRetreatIds,
                settings.canyonRouteWidth,
                5f,
                "terrain.corridor.retreat.player");
            AddCorridorStamps(
                plan,
                result,
                enemyRetreatIds,
                settings.canyonRouteWidth,
                5f,
                "terrain.corridor.retreat.enemy");

            // The main route is intentionally not carved. It is a faster,
            // more exposed overflight with a real altitude cost.
            return result.ToArray();
        }

        static void BuildBrokenRidge(
            AirCombatMapSettings settings,
            CombatSemanticPlan plan,
            Layout layout,
            List<CombatTerrainStamp> result)
        {
            const int ridgeCount = 5;
            const int piecesPerRidge = 3;
            float startS = -0.92f;
            float endS = 0.92f;
            float gap = Mathf.Clamp(
                settings.designTurnRadius * 1.3f
                / Mathf.Max(1f, settings.mapSize * 0.8f),
                0.085f,
                0.15f);
            float ridgeSpan = (endS - startS
                - gap * (ridgeCount - 1)) / ridgeCount;
            float cursor = startS;
            float radius = settings.mapSize * 0.042f;
            float falloff = settings.mapSize * 0.028f;
            for (int ridge = 0; ridge < ridgeCount; ridge++)
            {
                float segmentStart = cursor;
                float segmentEnd = cursor + ridgeSpan;
                for (int piece = 0;
                     piece < piecesPerRidge;
                     piece++)
                {
                    float a = Mathf.Lerp(
                        segmentStart,
                        segmentEnd,
                        piece / (float)piecesPerRidge);
                    float b = Mathf.Lerp(
                        segmentStart,
                        segmentEnd,
                        (piece + 1f) / piecesPerRidge);
                    Vector3 start = RidgePoint(
                        plan.mapCenter,
                        settings.mapSize,
                        layout,
                        a);
                    Vector3 end = RidgePoint(
                        plan.mapCenter,
                        settings.mapSize,
                        layout,
                        b);
                    float heightScale = 0.86f
                        + 0.12f
                        * Mathf.Sin(
                            (ridge + 1) * 1.731f
                            + plan.seed * 0.001f);
                    result.Add(Stamp(
                        "terrain.ridge."
                        + ridge.ToString("D2")
                        + "."
                        + piece.ToString("D2"),
                        CombatTerrainStampType.RidgeCapsule,
                        start,
                        end,
                        radius,
                        falloff,
                        settings.mountainHeight
                        * Mathf.Clamp(
                            heightScale,
                            0.76f,
                            1f)));
                }
                cursor = segmentEnd + gap;
            }

            // Two short shoulders put recovery loops behind a second layer
            // of cover without creating a boundary wall.
            float shoulderZ = settings.mapSize * 0.225f;
            float shoulderX = settings.mapSize * 0.2f;
            Vector3 playerA = LocalToWorld(
                plan.mapCenter,
                -shoulderX,
                -shoulderZ);
            Vector3 playerB = LocalToWorld(
                plan.mapCenter,
                shoulderX * 0.15f,
                -shoulderZ * 1.08f);
            Vector3 enemyA = LocalToWorld(
                plan.mapCenter,
                -playerB.x + plan.mapCenter.x,
                -playerB.z + plan.mapCenter.z);
            Vector3 enemyB = LocalToWorld(
                plan.mapCenter,
                -playerA.x + plan.mapCenter.x,
                -playerA.z + plan.mapCenter.z);
            result.Add(Stamp(
                "terrain.shoulder.player",
                CombatTerrainStampType.MesaCapsule,
                playerA,
                playerB,
                radius * 0.8f,
                falloff,
                settings.mountainHeight * 0.42f));
            result.Add(Stamp(
                "terrain.shoulder.enemy",
                CombatTerrainStampType.MesaCapsule,
                enemyA,
                enemyB,
                radius * 0.8f,
                falloff,
                settings.mountainHeight * 0.42f));
        }

        static Vector3 RidgePoint(
            Vector3 center,
            float size,
            Layout layout,
            float s)
        {
            float x = layout.handedness
                * layout.ridgeAmplitude
                * Mathf.Sin(Mathf.PI * s);
            float z = size * 0.4f * s;
            Vector2 rotated = Rotate(
                new Vector2(x, z),
                layout.ridgeYaw);
            return LocalToWorld(center, rotated.x, rotated.y);
        }

        static void AddCorridorStamps(
            CombatSemanticPlan plan,
            List<CombatTerrainStamp> result,
            string[] volumeIds,
            float width,
            float floor,
            string prefix)
        {
            if (volumeIds == null)
                return;
            for (int i = 1; i < volumeIds.Length; i++)
            {
                CombatTacticalVolume a = plan.FindVolume(
                    volumeIds[i - 1]);
                CombatTacticalVolume b = plan.FindVolume(
                    volumeIds[i]);
                if (a == null || b == null)
                    continue;
                result.Add(Stamp(
                    prefix + "." + (i - 1).ToString("D2"),
                    CombatTerrainStampType.Corridor,
                    a.position,
                    b.position,
                    width * 0.42f,
                    width * 0.25f,
                    floor));
            }
        }

        static void ResolveVolumeHeights(
            AirCombatMapSettings settings,
            CombatSemanticPlan plan)
        {
            if (plan.tacticalVolumes == null)
                return;
            for (int i = 0; i < plan.tacticalVolumes.Length; i++)
            {
                CombatTacticalVolume volume =
                    plan.tacticalVolumes[i];
                if (volume == null)
                    continue;
                float ground = SampleHeight(
                    settings,
                    plan,
                    volume.position.x,
                    volume.position.z);
                volume.position = new Vector3(
                    volume.position.x,
                    ground + volume.preferredClearance,
                    volume.position.z);
            }
        }

        static CombatSemanticRoute BuildRoute(
            AirCombatMapSettings settings,
            CombatSemanticPlan plan,
            string id,
            CombatRouteType type,
            float width,
            float exposure,
            string[] volumeIds)
        {
            var controls = new List<Vector3>();
            if (volumeIds != null)
            {
                for (int i = 0; i < volumeIds.Length; i++)
                {
                    CombatTacticalVolume volume =
                        plan.FindVolume(volumeIds[i]);
                    if (volume != null)
                        controls.Add(volume.position);
                }
            }
            Vector3[] points = SmoothAndResample(
                controls,
                Mathf.Clamp(
                    settings.designTurnRadius * 0.28f,
                    20f,
                    46f));
            for (int i = 0; i < points.Length; i++)
            {
                float ground = SampleHeight(
                    settings,
                    plan,
                    points[i].x,
                    points[i].z);
                float clearance = RouteClearance(
                    settings,
                    type);
                points[i].y = ground + clearance;
            }
            return new CombatSemanticRoute
            {
                stableId = id,
                type = type,
                width = width,
                intendedExposure = exposure,
                controlVolumeIds = volumeIds
                    ?? Array.Empty<string>(),
                waypoints = points
            };
        }

        static float RouteClearance(
            AirCombatMapSettings settings,
            CombatRouteType type)
        {
            switch (type)
            {
                case CombatRouteType.TerrainMaskedFlank:
                    return Mathf.Clamp(
                        settings.designTurnRadius * 0.36f,
                        settings.minimumGroundClearance + 8f,
                        settings.maximumGroundClearance * 0.42f);
                case CombatRouteType.LongRange:
                    return Mathf.Clamp(
                        settings.designTurnRadius,
                        70f,
                        settings.maximumGroundClearance * 0.75f);
                case CombatRouteType.Retreat:
                    return Mathf.Clamp(
                        settings.designTurnRadius * 0.48f,
                        settings.minimumGroundClearance + 10f,
                        settings.maximumGroundClearance * 0.5f);
                default:
                    return Mathf.Clamp(
                        settings.designTurnRadius * 0.76f,
                        58f,
                        settings.maximumGroundClearance * 0.65f);
            }
        }

        static Vector3[] SmoothAndResample(
            List<Vector3> controls,
            float spacing)
        {
            if (controls == null || controls.Count < 2)
                return controls?.ToArray() ?? Array.Empty<Vector3>();
            var smoothed = new List<Vector3>(controls);
            bool closed = Vector3.Distance(
                controls[0],
                controls[controls.Count - 1]) < 0.01f;
            for (int iteration = 0; iteration < 3; iteration++)
            {
                var next = new List<Vector3>(
                    smoothed.Count * 2);
                next.Add(smoothed[0]);
                for (int i = 1; i < smoothed.Count; i++)
                {
                    Vector3 a = smoothed[i - 1];
                    Vector3 b = smoothed[i];
                    next.Add(Vector3.LerpUnclamped(a, b, 0.25f));
                    next.Add(Vector3.LerpUnclamped(a, b, 0.75f));
                }
                next.Add(smoothed[smoothed.Count - 1]);
                smoothed = next;
            }

            var result = new List<Vector3> { smoothed[0] };
            float carry = 0f;
            for (int i = 1; i < smoothed.Count; i++)
            {
                Vector3 start = smoothed[i - 1];
                Vector3 end = smoothed[i];
                float length = Vector3.Distance(start, end);
                if (length <= 0.0001f)
                    continue;
                float cursor = spacing - carry;
                while (cursor < length)
                {
                    result.Add(Vector3.LerpUnclamped(
                        start,
                        end,
                        cursor / length));
                    cursor += spacing;
                }
                carry = Mathf.Max(0f, length - (cursor - spacing));
                carry = carry >= spacing ? 0f : carry;
            }
            Vector3 last = smoothed[smoothed.Count - 1];
            if (Vector3.Distance(result[result.Count - 1], last)
                > 0.01f)
            {
                result.Add(last);
            }
            if (closed && result.Count > 1)
                result[result.Count - 1] = result[0];
            return result.ToArray();
        }

        static CombatOccluderData[] BuildTowerClusters(
            AirCombatMapSettings settings,
            CombatSemanticPlan plan,
            Layout layout,
            int seed)
        {
            int requested = Mathf.Clamp(
                settings.occluderTowerCount,
                0,
                12);
            int firstCount = (requested + 1) / 2;
            int secondCount = requested - firstCount;
            var random = new StableRandom(seed ^ 0x51ED270B);
            var result = new List<CombatOccluderData>(requested + 1)
            {
                new CombatOccluderData
                {
                    stableId = "occluder.broken-ridge",
                    type = CombatOccluderType.Ridge,
                    position = new Vector3(
                        plan.mapCenter.x,
                        plan.mapCenter.y
                        + settings.mountainHeight * 0.5f,
                        plan.mapCenter.z),
                    size = new Vector3(
                        settings.mapSize * 0.34f,
                        settings.mountainHeight,
                        settings.mapSize * 0.8f),
                    color = new Color(0.34f, 0.37f, 0.28f)
                }
            };
            Vector2 clusterA = new Vector2(
                layout.longX,
                -layout.gateZ * 0.72f);
            Vector2 clusterB = new Vector2(
                layout.longX,
                layout.gateZ * 0.72f);
            AddTowerCluster(
                settings,
                plan,
                result,
                random,
                clusterA,
                firstCount,
                0);
            AddTowerCluster(
                settings,
                plan,
                result,
                random,
                clusterB,
                secondCount,
                firstCount);
            return result.ToArray();
        }

        static void AddTowerCluster(
            AirCombatMapSettings settings,
            CombatSemanticPlan plan,
            List<CombatOccluderData> result,
            StableRandom random,
            Vector2 localCenter,
            int count,
            int baseIndex)
        {
            float spreadX = Mathf.Max(
                42f,
                settings.designTurnRadius * 0.64f);
            float spreadZ = Mathf.Max(
                34f,
                settings.designTurnRadius * 0.46f);
            for (int i = 0; i < count; i++)
            {
                float angle = (i + 0.35f) * 2.399963f
                    + random.Range(-0.18f, 0.18f);
                float radius = Mathf.Sqrt((i + 0.5f)
                    / Mathf.Max(1f, count));
                float localX = localCenter.x
                    + Mathf.Cos(angle) * spreadX * radius;
                float localZ = localCenter.y
                    + Mathf.Sin(angle) * spreadZ * radius;
                float worldX = plan.mapCenter.x + localX;
                float worldZ = plan.mapCenter.z + localZ;
                float width = random.Range(22f, 36f);
                float depth = random.Range(22f, 40f);
                float height = Mathf.Clamp(
                    random.Range(0.38f, 0.64f)
                    * settings.mountainHeight,
                    48f,
                    150f);
                float ground = SampleHeight(
                    settings,
                    plan,
                    worldX,
                    worldZ);
                result.Add(new CombatOccluderData
                {
                    stableId = "occluder.tower."
                        + (baseIndex + i).ToString("D2"),
                    type = CombatOccluderType.Tower,
                    position = new Vector3(
                        worldX,
                        ground + height * 0.5f,
                        worldZ),
                    size = new Vector3(width, height, depth),
                    color = (baseIndex + i) % 2 == 0
                        ? new Color(0.29f, 0.34f, 0.4f)
                        : new Color(0.39f, 0.31f, 0.24f)
                });
            }
        }

        static CombatTacticalVolume Volume(
            string id,
            CombatTacticalVolumeType type,
            Vector3 center,
            float localX,
            float localZ,
            float diameter,
            float clearance,
            int teamBias)
        {
            return new CombatTacticalVolume
            {
                stableId = id,
                type = type,
                position = LocalToWorld(center, localX, localZ),
                size = new Vector3(
                    diameter,
                    Mathf.Max(80f, diameter * 0.58f),
                    diameter),
                preferredClearance = clearance,
                teamBias = teamBias
            };
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
                radius = Mathf.Max(1f, radius),
                falloff = Mathf.Max(1f, falloff),
                height = height
            };
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
                radius = Mathf.Max(8f, radius)
            };
        }

        static Vector3 LocalToWorld(
            Vector3 center,
            float localX,
            float localZ)
        {
            return new Vector3(
                center.x + localX,
                center.y,
                center.z + localZ);
        }

        static Vector2 Rotate(Vector2 value, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(radians);
            float sine = Mathf.Sin(radians);
            return new Vector2(
                value.x * cosine - value.y * sine,
                value.x * sine + value.y * cosine);
        }

        static Vector3 DirectionOnPlane(Vector3 value)
        {
            value.y = 0f;
            return value.sqrMagnitude > 0.001f
                ? value.normalized
                : Vector3.forward;
        }

        static float CompactMask(
            float distance,
            float inner,
            float outer)
        {
            float t = Mathf.Clamp01(
                (distance - inner)
                / Mathf.Max(0.001f, outer - inner));
            float smooth = t * t * t
                * (t * (t * 6f - 15f) + 10f);
            return 1f - smooth;
        }

        static float DistanceToSegment(
            Vector2 point,
            Vector2 start,
            Vector2 end)
        {
            Vector2 segment = end - start;
            float denominator = segment.sqrMagnitude;
            if (denominator <= 0.000001f)
                return Vector2.Distance(point, start);
            float t = Mathf.Clamp01(
                Vector2.Dot(point - start, segment)
                / denominator);
            return Vector2.Distance(
                point,
                start + segment * t);
        }

        static float EvenFractalNoise(
            float x,
            float z,
            int seed)
        {
            return 0.5f
                * (FractalNoise(x, z, seed)
                + FractalNoise(-x, -z, seed));
        }

        static float FractalNoise(
            float x,
            float z,
            int seed)
        {
            float total = 0f;
            float amplitude = 0.58f;
            float frequency = 1f;
            float normalization = 0f;
            for (int octave = 0; octave < 4; octave++)
            {
                total += ValueNoise(
                    x * frequency,
                    z * frequency,
                    seed + octave * 1013) * amplitude;
                normalization += amplitude;
                amplitude *= 0.52f;
                frequency *= 2.03f;
            }
            return normalization > 0f
                ? total / normalization
                : 0f;
        }

        static float ValueNoise(float x, float z, int seed)
        {
            int x0 = Mathf.FloorToInt(x);
            int z0 = Mathf.FloorToInt(z);
            int x1 = x0 + 1;
            int z1 = z0 + 1;
            float tx = Smooth01(x - x0);
            float tz = Smooth01(z - z0);
            float a = Mathf.Lerp(
                HashSigned(x0, z0, seed),
                HashSigned(x1, z0, seed),
                tx);
            float b = Mathf.Lerp(
                HashSigned(x0, z1, seed),
                HashSigned(x1, z1, seed),
                tx);
            return Mathf.Lerp(a, b, tz);
        }

        static float Smooth01(float value)
        {
            return value * value * (3f - 2f * value);
        }

        static float HashSigned(int x, int z, int seed)
        {
            unchecked
            {
                uint value = (uint)seed;
                value ^= (uint)x * 0x9E3779B9u;
                value ^= (uint)z * 0x85EBCA6Bu;
                value ^= value >> 16;
                value *= 0x7FEB352Du;
                value ^= value >> 15;
                value *= 0x846CA68Bu;
                value ^= value >> 16;
                return (value & 0x00FFFFFFu) / 8388607.5f - 1f;
            }
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value)
                && !float.IsInfinity(value);
        }

        static void Add(ref ulong hash, string value)
        {
            if (value == null)
                value = string.Empty;
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
                hash ^= (uint)value;
                hash *= 1099511628211UL;
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
    }
}
