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
                schemaVersion = 3,
                generatorVersion = Mathf.Max(
                    2,
                    settings.generatorVersion),
                seed = derivedSeed,
                topologyVariant = layout.variant,
                mode = settings.mode,
                theme = settings.theme,
                mapCenter = center,
                mapSize = settings.mapSize,
                warningRadius = settings.warningRadius,
                forfeitRadius = settings.forfeitRadius
            };

            float turn = settings.designTurnRadius;
            bool horde = settings.mode == AirCombatMapMode.Horde;
            float bowlDiameter = Mathf.Clamp(
                Mathf.Max(
                    turn * (horde ? 4.8f : 4.2f),
                    settings.mainRouteWidth * 1.35f),
                220f,
                settings.mapSize * (horde ? 0.36f : 0.30f));
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
            float bowlZ = horde
                ? layout.bowlZ * 0.82f
                : layout.bowlZ;
            float bowlAmplitude = horde
                ? layout.bowlAmplitude * 1.18f
                : layout.bowlAmplitude;
            float gateZ = horde
                ? bowlZ * 0.38f
                : layout.gateZ;
            float recoveryZ = horde
                ? bowlZ * 0.82f
                : layout.recoveryZ;
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
                    horde ? 0f : -layout.handedness * bowlAmplitude,
                    -bowlZ,
                    bowlDiameter,
                    Mathf.Max(58f, turn * 0.62f),
                    0),
                Volume(
                    "volume.bowl.center",
                    CombatTacticalVolumeType.ManeuverBowl,
                    center,
                    layout.handedness
                        * bowlAmplitude * (horde ? 0.25f : 0.86f),
                    0f,
                    bowlDiameter,
                    Mathf.Max(72f, turn * 0.78f),
                    0),
                Volume(
                    "volume.bowl.north",
                    CombatTacticalVolumeType.ManeuverBowl,
                    center,
                    horde ? 0f : -layout.handedness * bowlAmplitude,
                    bowlZ,
                    bowlDiameter,
                    Mathf.Max(58f, turn * 0.62f),
                    0),
                Volume(
                    "volume.gate.mask.south",
                    CombatTacticalVolumeType.OcclusionGate,
                    center,
                    layout.flankX,
                    -gateZ,
                    gateDiameter,
                    Mathf.Max(30f, turn * 0.34f),
                    0),
                Volume(
                    "volume.gate.mask.north",
                    CombatTacticalVolumeType.OcclusionGate,
                    center,
                    layout.flankX,
                    gateZ,
                    gateDiameter,
                    Mathf.Max(30f, turn * 0.34f),
                    0),
                Volume(
                    "volume.lane.exposed.south",
                    CombatTacticalVolumeType.ExposureLane,
                    center,
                    layout.longX,
                    -gateZ,
                    gateDiameter * 1.08f,
                    Mathf.Max(88f, turn * 0.95f),
                    0),
                Volume(
                    "volume.lane.exposed.north",
                    CombatTacticalVolumeType.ExposureLane,
                    center,
                    layout.longX,
                    gateZ,
                    gateDiameter * 1.08f,
                    Mathf.Max(88f, turn * 0.95f),
                    0),
                Volume(
                    "volume.recovery.player.mask",
                    CombatTacticalVolumeType.RecoveryPocket,
                    center,
                    layout.flankX * 0.66f,
                    -recoveryZ,
                    recoveryDiameter,
                    Mathf.Max(38f, turn * 0.42f),
                    -1),
                Volume(
                    "volume.recovery.player.open",
                    CombatTacticalVolumeType.RecoveryPocket,
                    center,
                    layout.longX * 0.66f,
                    -recoveryZ,
                    recoveryDiameter,
                    Mathf.Max(52f, turn * 0.56f),
                    -1),
                Volume(
                    "volume.recovery.enemy.mask",
                    CombatTacticalVolumeType.RecoveryPocket,
                    center,
                    layout.flankX * 0.66f,
                    recoveryZ,
                    recoveryDiameter,
                    Mathf.Max(38f, turn * 0.42f),
                    1),
                Volume(
                    "volume.recovery.enemy.open",
                    CombatTacticalVolumeType.RecoveryPocket,
                    center,
                    layout.longX * 0.66f,
                    recoveryZ,
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
            BuildUrbanRoadLayout(settings, plan, layout);

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
            BuildUrbanPlotsAndPads(settings, plan);
            plan.flightCeiling = ComputeFlightCeiling(settings, plan);
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
                settings.mapSize * 0.34f);
            float macro = EvenFractalNoise(
                localX / macroScale,
                localZ / macroScale,
                plan.seed) * Mathf.Min(
                    16f,
                    settings.mountainHeight * 0.11f);
            float mesoScale = Mathf.Max(
                36f,
                settings.mapSize * 0.105f);
            float meso = EvenFractalNoise(
                localX / mesoScale,
                localZ / mesoScale,
                plan.seed ^ 0x3C6EF372) * Mathf.Min(
                    9f,
                    settings.mountainHeight * 0.065f);
            float baseHeight = plan.mapCenter.y + 3f + macro + meso;
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

            // Roads and plots are the final grading pass. They can both cut
            // and fill the landscape, which creates believable road beds and
            // compact terraces instead of burying building meshes in slopes.
            // The generous falloff remains natural terrain and hides the
            // transition without a floating platform.
            if (plan.terrainStamps != null)
            {
                for (int i = 0; i < plan.terrainStamps.Length; i++)
                {
                    CombatTerrainStamp stamp = plan.terrainStamps[i];
                    if (stamp == null
                        || (stamp.type != CombatTerrainStampType.RoadBed
                            && stamp.type
                                != CombatTerrainStampType.BuildingPad))
                    {
                        continue;
                    }
                    Vector2 start = new Vector2(
                        stamp.start.x,
                        stamp.start.z);
                    Vector2 end = new Vector2(
                        stamp.end.x,
                        stamp.end.z);
                    Vector2 segment = end - start;
                    float denominator = segment.sqrMagnitude;
                    float along = denominator > 0.0001f
                        ? Mathf.Clamp01(
                            Vector2.Dot(point - start, segment)
                            / denominator)
                        : 0f;
                    float distance = DistanceToSegment(point, start, end);
                    float mask = CompactMask(
                        distance,
                        stamp.radius,
                        stamp.radius + stamp.falloff);
                    float target = Mathf.Lerp(
                        stamp.start.y,
                        stamp.end.y,
                        along);
                    height = Mathf.Lerp(height, target, mask * mask);
                }
            }
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
            Add(ref hash, (int)plan.mode);
            Add(ref hash, (int)plan.theme);
            Add(ref hash, plan.mapCenter);
            Add(ref hash, plan.mapSize);
            Add(ref hash, plan.warningRadius);
            Add(ref hash, plan.forfeitRadius);
            Add(ref hash, plan.flightCeiling);
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
                    Add(ref hash, (int)value.decorationKind);
                    Add(ref hash, value.yaw);
                }
            }
            if (plan.urbanRoads != null)
            {
                for (int i = 0; i < plan.urbanRoads.Length; i++)
                {
                    CombatUrbanRoadData value = plan.urbanRoads[i];
                    if (value == null)
                        continue;
                    Add(ref hash, value.stableId);
                    Add(ref hash, value.start);
                    Add(ref hash, value.end);
                    Add(ref hash, value.width);
                    Add(ref hash, value.shoulder);
                    Add(ref hash, value.arterial ? 1 : 0);
                }
            }
            if (plan.urbanPlots != null)
            {
                for (int i = 0; i < plan.urbanPlots.Length; i++)
                {
                    CombatUrbanPlotData value = plan.urbanPlots[i];
                    if (value == null)
                        continue;
                    Add(ref hash, value.stableId);
                    Add(ref hash, value.roadStableId);
                    Add(ref hash, value.position);
                    Add(ref hash, value.size.x);
                    Add(ref hash, value.size.y);
                    Add(ref hash, value.yaw);
                    Add(ref hash, value.groundHeight);
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
            bool horde = settings.mode == AirCombatMapMode.Horde;
            var current = new Layout
            {
                variant = horde
                    ? 100 + Mathf.Abs(seed % 4)
                    : Mathf.Abs(seed % 6),
                handedness = random.Value() < 0.5f ? -1f : 1f,
                bowlAmplitude = size * random.Range(
                    horde ? 0.09f : 0.07f,
                    horde ? 0.17f : 0.135f),
                bowlZ = size * random.Range(
                    horde ? 0.125f : 0.145f,
                    horde ? 0.19f : 0.225f),
                flankX = 0f,
                longX = 0f,
                gateZ = size * random.Range(
                    horde ? 0.105f : 0.06f,
                    horde ? 0.18f : 0.14f),
                recoveryZ = size * random.Range(
                    horde ? 0.27f : 0.22f,
                    horde ? 0.35f : 0.31f),
                ridgeYaw = random.Range(
                    horde ? -24f : -34f,
                    horde ? 24f : 34f),
                ridgeAmplitude = size
                    * random.Range(
                        horde ? 0.15f : 0.085f,
                        horde ? 0.22f : 0.165f)
            };
            float lane = size * random.Range(
                horde ? 0.22f : 0.245f,
                horde ? 0.31f : 0.33f);
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
            bool horde = settings.mode == AirCombatMapMode.Horde;
            layout.bowlAmplitude = Mathf.Clamp(
                layout.bowlAmplitude,
                size * (horde ? 0.085f : 0.07f),
                size * (horde ? 0.18f : 0.14f));
            layout.bowlZ = Mathf.Clamp(
                layout.bowlZ,
                size * (horde ? 0.115f : 0.14f),
                size * (horde ? 0.20f : 0.23f));
            layout.gateZ = Mathf.Clamp(
                layout.gateZ,
                size * (horde ? 0.095f : 0.055f),
                size * (horde ? 0.19f : 0.15f));
            layout.recoveryZ = Mathf.Clamp(
                layout.recoveryZ,
                size * (horde ? 0.26f : 0.215f),
                size * (horde ? 0.36f : 0.32f));
            float lane = Mathf.Clamp(
                Mathf.Abs(layout.flankX),
                size * (horde ? 0.21f : 0.245f),
                size * (horde ? 0.32f : 0.335f));
            layout.flankX = -layout.handedness * lane;
            layout.longX = layout.handedness * lane;
            layout.ridgeAmplitude = Mathf.Clamp(
                layout.ridgeAmplitude,
                size * (horde ? 0.14f : 0.08f),
                size * (horde ? 0.23f : 0.175f));
            layout.ridgeYaw = Mathf.Clamp(
                layout.ridgeYaw,
                horde ? -28f : -38f,
                horde ? 28f : 38f);
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
            if (settings.mode == AirCombatMapMode.Horde)
                BuildHordePerimeter(settings, plan, layout, result);
            else
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

        static void BuildUrbanRoadLayout(
            AirCombatMapSettings settings,
            CombatSemanticPlan plan,
            Layout layout)
        {
            if (plan.theme != CombatMapTheme.Urban
                || settings.mode != AirCombatMapMode.Horde)
            {
                plan.urbanRoads = Array.Empty<CombatUrbanRoadData>();
                return;
            }

            // A compact trapezoidal street belt follows the four playable
            // districts. Its open centre preserves the aerial conflict bowl;
            // the linked perimeter gives the settlement a legible human road
            // hierarchy and avoids isolated tower islands.
            Vector3 innerSouth = LocalToWorld(
                plan.mapCenter,
                layout.longX,
                -layout.gateZ * 0.72f);
            Vector3 innerNorth = LocalToWorld(
                plan.mapCenter,
                layout.longX,
                layout.gateZ * 0.72f);
            Vector3 outerSouth = LocalToWorld(
                plan.mapCenter,
                -layout.longX * 0.72f,
                -layout.recoveryZ);
            Vector3 outerNorth = LocalToWorld(
                plan.mapCenter,
                -layout.longX * 0.72f,
                layout.recoveryZ);
            float roadElevation = plan.mapCenter.y + 3.5f;
            innerSouth.y = roadElevation;
            innerNorth.y = roadElevation;
            outerSouth.y = roadElevation;
            outerNorth.y = roadElevation;

            float width = Mathf.Clamp(
                settings.vehicleWingspan * 1.65f,
                28f,
                40f);
            float shoulder = Mathf.Clamp(
                Mathf.Max(
                    settings.vehicleWingspan,
                    settings.designTurnRadius * 0.2f),
                16f,
                30f);
            var roads = new List<CombatUrbanRoadData>(5)
            {
                UrbanRoad(
                    "urban.road.inner",
                    innerSouth,
                    innerNorth,
                    width,
                    shoulder),
                UrbanRoad(
                    "urban.road.outer",
                    outerNorth,
                    outerSouth,
                    width,
                    shoulder)
            };
            int topology = Mathf.Abs(layout.variant) % 4;
            if (topology == 0 || topology == 3)
            {
                roads.Add(UrbanRoad(
                    "urban.road.north-link",
                    innerNorth,
                    outerNorth,
                    width,
                    shoulder));
                roads.Add(UrbanRoad(
                    "urban.road.south-link",
                    outerSouth,
                    innerSouth,
                    width,
                    shoulder));
            }
            else if (topology == 1)
            {
                // A protected natural break splits the city into two readable
                // halves. The opposing approach roads stop before the green
                // belt, producing flanking airspace without nonsensical roads
                // climbing across the central landform.
                roads.Add(UrbanRoad(
                    "urban.road.north-approach",
                    innerNorth,
                    Vector3.Lerp(innerNorth, outerNorth, 0.43f),
                    width,
                    shoulder));
                roads.Add(UrbanRoad(
                    "urban.road.south-approach",
                    outerSouth,
                    Vector3.Lerp(outerSouth, innerSouth, 0.43f),
                    width,
                    shoulder));
            }
            else
            {
                Vector3 innerMid = Vector3.Lerp(
                    innerSouth,
                    innerNorth,
                    0.5f);
                Vector3 outerMid = Vector3.Lerp(
                    outerSouth,
                    outerNorth,
                    0.5f);
                roads.Add(UrbanRoad(
                    "urban.road.central-link",
                    innerMid,
                    outerMid,
                    width * 1.08f,
                    shoulder));
                roads.Add(UrbanRoad(
                    "urban.road.north-link",
                    innerNorth,
                    outerNorth,
                    width,
                    shoulder));
            }
            if (topology == 3)
            {
                roads.Add(UrbanRoad(
                    "urban.road.central-link",
                    Vector3.Lerp(innerSouth, innerNorth, 0.5f),
                    Vector3.Lerp(outerSouth, outerNorth, 0.5f),
                    width * 1.08f,
                    shoulder));
            }
            plan.urbanRoads = roads.ToArray();

            var stamps = new List<CombatTerrainStamp>(
                (plan.terrainStamps?.Length ?? 0)
                + plan.urbanRoads.Length);
            if (plan.terrainStamps != null)
                stamps.AddRange(plan.terrainStamps);
            for (int i = 0; i < plan.urbanRoads.Length; i++)
            {
                CombatUrbanRoadData road = plan.urbanRoads[i];
                stamps.Add(Stamp(
                    "terrain." + road.stableId,
                    CombatTerrainStampType.RoadBed,
                    road.start,
                    road.end,
                    road.width * 0.5f,
                    road.shoulder,
                    0f));
            }
            plan.terrainStamps = stamps.ToArray();
        }

        static CombatUrbanRoadData UrbanRoad(
            string stableId,
            Vector3 start,
            Vector3 end,
            float width,
            float shoulder)
        {
            return new CombatUrbanRoadData
            {
                stableId = stableId,
                start = start,
                end = end,
                width = width,
                shoulder = shoulder,
                arterial = true
            };
        }

        static void BuildUrbanPlotsAndPads(
            AirCombatMapSettings settings,
            CombatSemanticPlan plan)
        {
            if (plan.theme != CombatMapTheme.Urban
                || plan.occluders == null)
            {
                plan.urbanPlots = Array.Empty<CombatUrbanPlotData>();
                return;
            }

            var plots = new List<CombatUrbanPlotData>();
            var stamps = new List<CombatTerrainStamp>(
                (plan.terrainStamps?.Length ?? 0)
                + plan.occluders.Length);
            if (plan.terrainStamps != null)
                stamps.AddRange(plan.terrainStamps);
            for (int i = 0; i < plan.occluders.Length; i++)
            {
                CombatOccluderData building = plan.occluders[i];
                if (building == null
                    || building.type != CombatOccluderType.Tower
                    || (building.decorationKind
                            != CombatDecorationKind.Building
                        && building.decorationKind
                            != CombatDecorationKind.Beacon))
                {
                    continue;
                }
                float ground = building.position.y
                    - building.size.y * 0.5f;
                string roadId = NearestRoadId(
                    plan,
                    new Vector2(
                        building.position.x,
                        building.position.z));
                var plot = new CombatUrbanPlotData
                {
                    stableId = "urban.plot." + i.ToString("D2"),
                    roadStableId = roadId,
                    position = new Vector3(
                        building.position.x,
                        ground,
                        building.position.z),
                    size = new Vector2(
                        building.size.x
                            + settings.vehicleWingspan * 0.65f,
                        building.size.z
                            + settings.vehicleWingspan * 0.65f),
                    yaw = building.yaw,
                    groundHeight = ground
                };
                plots.Add(plot);
                float radius = plot.size.magnitude * 0.5f;
                Vector3 pad = plot.position;
                stamps.Add(Stamp(
                    "terrain." + plot.stableId,
                    CombatTerrainStampType.BuildingPad,
                    pad,
                    pad,
                    radius,
                    Mathf.Max(14f, settings.vehicleWingspan),
                    0f));
            }
            plan.urbanPlots = plots.ToArray();
            plan.terrainStamps = stamps.ToArray();
        }

        static string NearestRoadId(
            CombatSemanticPlan plan,
            Vector2 point)
        {
            string result = string.Empty;
            float best = float.PositiveInfinity;
            if (plan.urbanRoads == null)
                return result;
            for (int i = 0; i < plan.urbanRoads.Length; i++)
            {
                CombatUrbanRoadData road = plan.urbanRoads[i];
                if (road == null)
                    continue;
                float distance = DistanceToSegment(
                    point,
                    new Vector2(road.start.x, road.start.z),
                    new Vector2(road.end.x, road.end.z));
                if (distance >= best)
                    continue;
                best = distance;
                result = road.stableId;
            }
            return result;
        }

        static void BuildBrokenRidge(
            AirCombatMapSettings settings,
            CombatSemanticPlan plan,
            Layout layout,
            List<CombatTerrainStamp> result)
        {
            int ridgeCount = 4 + Mathf.Abs(layout.variant % 3);
            int piecesPerRidge = 2 + Mathf.Abs(layout.variant % 2);
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
            float wave;
            switch (Mathf.Abs(layout.variant) % 6)
            {
                case 1:
                    wave = Mathf.Sin(Mathf.PI * s * 1.5f) * 0.72f;
                    break;
                case 2:
                    wave = Mathf.Sign(s) * (1f - Mathf.Abs(s)) * 1.25f;
                    break;
                case 3:
                    wave = Mathf.Sin(Mathf.PI * s * 2f) * 0.58f;
                    break;
                case 4:
                    wave = Mathf.Cos(Mathf.PI * s) * 0.82f;
                    break;
                case 5:
                    wave = Mathf.Sin(Mathf.PI * s) * 0.45f
                        + Mathf.Sin(Mathf.PI * s * 3f) * 0.28f;
                    break;
                default:
                    wave = Mathf.Sin(Mathf.PI * s);
                    break;
            }
            float x = layout.handedness * layout.ridgeAmplitude * wave;
            float z = size * (0.36f + 0.025f * (layout.variant % 3)) * s;
            Vector2 rotated = Rotate(
                new Vector2(x, z),
                layout.ridgeYaw);
            return LocalToWorld(center, rotated.x, rotated.y);
        }

        static void BuildHordePerimeter(
            AirCombatMapSettings settings,
            CombatSemanticPlan plan,
            Layout layout,
            List<CombatTerrainStamp> result)
        {
            // Urban ground is organized around four buildable districts.
            // Broad basin stamps keep towers seated on believable blocks;
            // isolated outcrops replace the old continuous mountain ring.
            Vector2[] districts =
            {
                new Vector2(layout.longX, -layout.gateZ * 0.72f),
                new Vector2(layout.longX, layout.gateZ * 0.72f),
                new Vector2(-layout.longX * 0.72f, -layout.recoveryZ),
                new Vector2(-layout.longX * 0.72f, layout.recoveryZ)
            };
            float districtRadius = Mathf.Max(
                settings.designTurnRadius * 1.75f,
                settings.mainRouteWidth * 0.82f);
            for (int index = 0; index < districts.Length; index++)
            {
                Vector3 center = LocalToWorld(
                    plan.mapCenter,
                    districts[index].x,
                    districts[index].y);
                result.Add(Stamp(
                    "terrain.horde.district." + index.ToString("D2"),
                    CombatTerrainStampType.Basin,
                    center,
                    center,
                    districtRadius,
                    districtRadius * 0.42f,
                    3f));
            }

            int outcropCount = 9 + Mathf.Abs(layout.variant % 2);
            float rotation = layout.ridgeYaw * 0.65f
                + Mathf.Abs(layout.variant % 4) * 9f;
            float stampRadius = settings.mapSize * 0.038f;
            for (int index = 0; index < outcropCount; index++)
            {
                float phase = index * 1.731f + plan.seed * 0.0017f;
                float angle = rotation
                    + index * 360f / outcropCount
                    + Mathf.Sin(phase) * 16f;
                float radialDistance = settings.mapSize
                    * (0.31f + 0.055f * Mathf.Cos(phase * 1.37f));
                Vector2 radial = Rotate(
                    Vector2.up * radialDistance,
                    angle);
                Vector2 tangent = Rotate(
                    Vector2.right
                    * settings.mapSize
                    * (0.018f + 0.012f * (index % 3)),
                    angle + 18f * Mathf.Sin(phase));
                result.Add(Stamp(
                    "terrain.horde.outcrop." + index.ToString("D2"),
                    index % 3 == 0
                        ? CombatTerrainStampType.RidgeCapsule
                        : CombatTerrainStampType.MesaCapsule,
                    LocalToWorld(
                        plan.mapCenter,
                        radial.x - tangent.x,
                        radial.y - tangent.y),
                    LocalToWorld(
                        plan.mapCenter,
                        radial.x + tangent.x,
                        radial.y + tangent.y),
                    stampRadius,
                    stampRadius * 0.75f,
                    settings.mountainHeight
                    * (0.42f + 0.13f * (index % 3))));
            }
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
            // A fourth Chaikin pass keeps the wider topology families inside
            // the measured aircraft curvature envelope. This changes route
            // geometry only; it does not alter vehicle steering or physics.
            for (int iteration = 0; iteration < 4; iteration++)
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
            bool horde = settings.mode == AirCombatMapMode.Horde;
            int requested = Mathf.Clamp(
                settings.occluderTowerCount,
                0,
                horde ? 24 : 14);
            int firstCount = horde
                ? (requested + 3) / 4
                : (requested + 1) / 2;
            int secondCount = horde
                ? (requested + 2) / 4
                : requested - firstCount;
            int thirdCount = horde
                ? (requested + 1) / 4
                : 0;
            int fourthCount = horde
                ? requested - firstCount - secondCount - thirdCount
                : 0;
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
                ref random,
                clusterA,
                firstCount,
                0);
            AddTowerCluster(
                settings,
                plan,
                result,
                ref random,
                clusterB,
                secondCount,
                firstCount);
            if (horde)
            {
                AddTowerCluster(
                    settings,
                    plan,
                    result,
                    ref random,
                    new Vector2(-layout.longX * 0.72f, -layout.recoveryZ),
                    thirdCount,
                    firstCount + secondCount);
                AddTowerCluster(
                    settings,
                    plan,
                    result,
                    ref random,
                    new Vector2(-layout.longX * 0.72f, layout.recoveryZ),
                    fourthCount,
                    firstCount + secondCount + thirdCount);
            }
            return result.ToArray();
        }

        static void AddTowerCluster(
            AirCombatMapSettings settings,
            CombatSemanticPlan plan,
            List<CombatOccluderData> result,
            ref StableRandom random,
            Vector2 localCenter,
            int count,
            int baseIndex)
        {
            float spacingX = Mathf.Max(
                128f,
                settings.designTurnRadius * 1.55f);
            float spacingZ = Mathf.Max(
                118f,
                settings.designTurnRadius * 1.4f);
            int columns = count <= 4 ? 2 : 3;
            int rows = Mathf.Max(1, Mathf.CeilToInt(count / (float)columns));
            for (int i = 0; i < count; i++)
            {
                int column = i % columns;
                int row = i / columns;
                float localX = localCenter.x
                    + (column - (columns - 1) * 0.5f) * spacingX
                    + random.Range(-spacingX * 0.08f, spacingX * 0.08f);
                float localZ = localCenter.y
                    + (row - (rows - 1) * 0.5f) * spacingZ
                    + random.Range(-spacingZ * 0.08f, spacingZ * 0.08f);
                float width = random.Range(26f, 44f);
                float depth = random.Range(28f, 50f);
                float height = settings.mode == AirCombatMapMode.Horde
                    ? Mathf.Clamp(
                        random.Range(0.85f, 1.55f)
                        * settings.mountainHeight,
                        88f,
                        220f)
                    : Mathf.Clamp(
                        random.Range(0.38f, 0.64f)
                        * settings.mountainHeight,
                        48f,
                        150f);
                bool districtLandmark = settings.mode
                    == AirCombatMapMode.Horde && i == 0;
                if (districtLandmark)
                {
                    width = Mathf.Max(width, random.Range(68f, 82f));
                    depth = Mathf.Max(depth, random.Range(64f, 78f));
                    height = Mathf.Max(
                        height,
                        random.Range(180f, 230f));
                }
                Vector2 clearPosition = ResolveClearTowerPosition(
                    settings,
                    plan,
                    result,
                    new Vector2(localX, localZ),
                    localCenter,
                    width,
                    depth,
                    spacingX,
                    spacingZ,
                    ref random);
                float worldX = plan.mapCenter.x + clearPosition.x;
                float worldZ = plan.mapCenter.z + clearPosition.y;
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
                    decorationKind = districtLandmark
                        ? CombatDecorationKind.Beacon
                        : ResolveDecorationKind(
                            settings,
                            baseIndex + i),
                    color = (baseIndex + i) % 2 == 0
                        ? new Color(0.29f, 0.34f, 0.4f)
                        : new Color(0.39f, 0.31f, 0.24f),
                    yaw = NearestRoadYaw(
                        plan,
                        new Vector2(worldX, worldZ))
                });
            }
        }

        static Vector2 ResolveClearTowerPosition(
            AirCombatMapSettings settings,
            CombatSemanticPlan plan,
            List<CombatOccluderData> placed,
            Vector2 preferred,
            Vector2 districtCenter,
            float width,
            float depth,
            float spacingX,
            float spacingZ,
            ref StableRandom random)
        {
            Vector2 candidate = preferred;
            for (int attempt = 0; attempt < 48; attempt++)
            {
                if (attempt > 0)
                {
                    float angle = attempt * 2.399963f
                        + random.Range(-0.08f, 0.08f);
                    float radius = Mathf.Max(spacingX, spacingZ)
                        * (0.72f + 0.24f * Mathf.Sqrt(attempt));
                    candidate = districtCenter + new Vector2(
                        Mathf.Cos(angle) * radius,
                        Mathf.Sin(angle) * radius);
                }
                float half = settings.mapSize * 0.5f
                    - Mathf.Max(width, depth) * 0.5f
                    - settings.vehicleWingspan;
                candidate.x = Mathf.Clamp(candidate.x, -half, half);
                candidate.y = Mathf.Clamp(candidate.y, -half, half);
                if (TowerPlacementIsClear(
                        settings,
                        plan,
                        placed,
                        candidate,
                        width,
                        depth))
                {
                    return candidate;
                }
            }
            return candidate;
        }

        static bool TowerPlacementIsClear(
            AirCombatMapSettings settings,
            CombatSemanticPlan plan,
            List<CombatOccluderData> placed,
            Vector2 local,
            float width,
            float depth)
        {
            float worldX = plan.mapCenter.x + local.x;
            float worldZ = plan.mapCenter.z + local.y;
            float centerHeight = SampleHeight(
                settings,
                plan,
                worldX,
                worldZ);
            float maximumVariation = 0f;
            for (int z = -1; z <= 1; z += 2)
            for (int x = -1; x <= 1; x += 2)
            {
                float corner = SampleHeight(
                    settings,
                    plan,
                    worldX + x * width * 0.5f,
                    worldZ + z * depth * 0.5f);
                maximumVariation = Mathf.Max(
                    maximumVariation,
                    Mathf.Abs(corner - centerHeight));
            }
            if (maximumVariation > Mathf.Max(6f, settings.vehicleWingspan * 0.4f))
                return false;

            float requiredGap = Mathf.Max(
                72f,
                Mathf.Max(
                    settings.designTurnRadius * 0.9f,
                    settings.vehicleWingspan * 2.5f));
            float radius = Mathf.Sqrt(width * width + depth * depth) * 0.5f;
            if (plan.urbanRoads != null)
            {
                float setback = Mathf.Max(
                    12f,
                    settings.vehicleWingspan * 0.75f);
                for (int index = 0;
                     index < plan.urbanRoads.Length;
                     index++)
                {
                    CombatUrbanRoadData road = plan.urbanRoads[index];
                    if (road == null)
                        continue;
                    float distance = DistanceToSegment(
                        new Vector2(worldX, worldZ),
                        new Vector2(road.start.x, road.start.z),
                        new Vector2(road.end.x, road.end.z));
                    if (distance < road.width * 0.5f
                        + setback
                        + radius)
                    {
                        return false;
                    }
                }
            }
            for (int index = 0; index < placed.Count; index++)
            {
                CombatOccluderData other = placed[index];
                if (other == null || other.type != CombatOccluderType.Tower)
                    continue;
                float otherRadius = Mathf.Sqrt(
                    other.size.x * other.size.x
                    + other.size.z * other.size.z) * 0.5f;
                float distance = Vector2.Distance(
                    new Vector2(other.position.x, other.position.z),
                    new Vector2(worldX, worldZ));
                if (distance < radius + otherRadius + requiredGap)
                    return false;
            }
            return true;
        }

        static float NearestRoadYaw(
            CombatSemanticPlan plan,
            Vector2 point)
        {
            float result = 0f;
            float best = float.PositiveInfinity;
            if (plan.urbanRoads == null)
                return result;
            for (int i = 0; i < plan.urbanRoads.Length; i++)
            {
                CombatUrbanRoadData road = plan.urbanRoads[i];
                if (road == null)
                    continue;
                float distance = DistanceToSegment(
                    point,
                    new Vector2(road.start.x, road.start.z),
                    new Vector2(road.end.x, road.end.z));
                if (distance >= best)
                    continue;
                Vector3 direction = road.end - road.start;
                best = distance;
                result = Mathf.Atan2(direction.x, direction.z)
                    * Mathf.Rad2Deg;
            }
            return result;
        }

        static float ComputeFlightCeiling(
            AirCombatMapSettings settings,
            CombatSemanticPlan plan)
        {
            float highest = plan.mapCenter.y;
            const int grid = 17;
            float half = settings.mapSize * 0.5f;
            for (int z = 0; z < grid; z++)
            for (int x = 0; x < grid; x++)
            {
                float worldX = plan.mapCenter.x
                    + Mathf.Lerp(-half, half, x / (float)(grid - 1));
                float worldZ = plan.mapCenter.z
                    + Mathf.Lerp(-half, half, z / (float)(grid - 1));
                highest = Mathf.Max(
                    highest,
                    SampleHeight(settings, plan, worldX, worldZ));
            }
            if (plan.terrainStamps != null)
            {
                for (int i = 0; i < plan.terrainStamps.Length; i++)
                {
                    CombatTerrainStamp stamp = plan.terrainStamps[i];
                    if (stamp == null)
                        continue;
                    highest = Mathf.Max(
                        highest,
                        SampleHeight(
                            settings,
                            plan,
                            stamp.start.x,
                            stamp.start.z),
                        SampleHeight(
                            settings,
                            plan,
                            stamp.end.x,
                            stamp.end.z),
                        SampleHeight(
                            settings,
                            plan,
                            (stamp.start.x + stamp.end.x) * 0.5f,
                            (stamp.start.z + stamp.end.z) * 0.5f));
                }
            }
            if (plan.occluders != null)
            {
                for (int i = 0; i < plan.occluders.Length; i++)
                {
                    CombatOccluderData value = plan.occluders[i];
                    if (value != null)
                        highest = Mathf.Max(
                            highest,
                            value.position.y + value.size.y * 0.5f);
                }
            }
            if (plan.routes != null)
            {
                for (int route = 0; route < plan.routes.Length; route++)
                {
                    Vector3[] points = plan.routes[route]?.waypoints;
                    if (points == null)
                        continue;
                    for (int point = 0; point < points.Length; point++)
                        highest = Mathf.Max(highest, points[point].y);
                }
            }
            float margin = Mathf.Max(
                18f,
                settings.vehicleWingspan * 1.25f,
                settings.designCombatSpeed * 0.32f);
            return highest + margin;
        }

        static CombatDecorationKind ResolveDecorationKind(
            AirCombatMapSettings settings,
            int index)
        {
            switch (settings.theme)
            {
                case CombatMapTheme.Urban:
                    return index % 7 == 0
                        ? CombatDecorationKind.Beacon
                        : CombatDecorationKind.Building;
                case CombatMapTheme.Industrial:
                    return index % 5 == 0
                        ? CombatDecorationKind.Crystal
                        : CombatDecorationKind.Building;
                case CombatMapTheme.Natural:
                    return index % 6 == 0
                        ? CombatDecorationKind.Crystal
                        : CombatDecorationKind.RockSpire;
                default:
                    return index % 3 == 0
                        ? CombatDecorationKind.Building
                        : index % 5 == 0
                            ? CombatDecorationKind.Crystal
                            : CombatDecorationKind.RockSpire;
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
