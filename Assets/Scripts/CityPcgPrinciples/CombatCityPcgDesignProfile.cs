using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityPlanet.CityPcg
{
    public enum TacticalOpportunityKind
    {
        ManeuverBowl,
        OcclusionChain,
        ExposureShortcut,
        RecoveryPocket,
        KiteLoop,
        TacticalChoke,
        VerticalEscape,
        AttackPerch,
        DestructionAmbush
    }

    [Serializable]
    public sealed class TacticalOpportunity
    {
        public string stableId = string.Empty;
        public TacticalOpportunityKind kind;
        public Bounds bounds;
        public Vector3[] entrances = Array.Empty<Vector3>();
        public Vector3[] exits = Array.Empty<Vector3>();
        [Range(0f, 1f)] public float utility = 0.5f;
        [Range(0f, 1f)] public float risk = 0.5f;
        [Range(0f, 1f)] public float executionDifficulty = 0.5f;
        [Range(0f, 1f)] public float legibility = 0.5f;
        [Min(0f)] public float expectedTraversalSeconds;
        [Min(0f)] public float safeWindowSeconds;
        [Min(0f)] public float loopRadius;
        [Min(0)] public int physicalFeatureCount;
        public string runtimeBindingId = string.Empty;
        public string[] connectedOpportunityIds = Array.Empty<string>();
    }

    [Serializable]
    public struct LocalDifficultyBudget
    {
        [Range(0f, 1f)] public float navigation;
        [Range(0f, 1f)] public float enemies;
        [Range(0f, 1f)] public float exposure;
        [Range(0f, 1f)] public float resourceDenial;
        public float Total => navigation + enemies + exposure + resourceDenial;
    }

    [Serializable]
    public sealed class CombatCityDifficultyProfile
    {
        [Range(0f, 1f)] public float navigationChallenge = 0.42f;
        [Range(0f, 1f)] public float combatPressure = 0.45f;
        [Range(0f, 1f)] public float exposurePressure = 0.42f;
        [Range(0f, 1f)] public float recoveryGenerosity = 0.72f;
        [Range(0f, 1f)] public float tacticalOpportunityDensity = 0.72f;
        [Range(0f, 1f)] public float routeLegibility = 0.78f;
        [Range(0f, 1f)] public float bossPursuitPressure = 0.45f;
        [Range(0f, 1f)] public float destructionUtility = 0.72f;
        [Range(0f, 1f)] public float decisionComplexity = 0.48f;
        [Range(0.7f, 1.4f)] public float roadWidthScale = 1f;
        [Range(0f, 1f)] public float blockMergeStrength = 0.32f;

        public CombatCityDifficultyProfile ValidatedCopy()
        {
            return new CombatCityDifficultyProfile
            {
                navigationChallenge = Mathf.Clamp01(navigationChallenge),
                combatPressure = Mathf.Clamp01(combatPressure),
                exposurePressure = Mathf.Clamp01(exposurePressure),
                recoveryGenerosity = Mathf.Clamp01(recoveryGenerosity),
                tacticalOpportunityDensity = Mathf.Clamp01(tacticalOpportunityDensity),
                routeLegibility = Mathf.Clamp01(routeLegibility),
                bossPursuitPressure = Mathf.Clamp01(bossPursuitPressure),
                destructionUtility = Mathf.Clamp01(destructionUtility),
                decisionComplexity = Mathf.Clamp01(decisionComplexity),
                roadWidthScale = Mathf.Clamp(
                    roadWidthScale <= 0.01f ? 1f : roadWidthScale,
                    0.7f,
                    1.4f),
                blockMergeStrength = Mathf.Clamp01(blockMergeStrength)
            };
        }

        public static CombatCityDifficultyProfile CreateForTier(
            int difficultyTier,
            AirCombatCityMission mission)
        {
            float t = Mathf.Clamp(difficultyTier, 0, 5) / 5f;
            var result = new CombatCityDifficultyProfile
            {
                navigationChallenge = Mathf.Lerp(0.20f, 0.82f, t),
                combatPressure = Mathf.Lerp(0.25f, 0.90f, t),
                exposurePressure = Mathf.Lerp(0.20f, 0.84f, t),
                recoveryGenerosity = Mathf.Lerp(0.90f, 0.36f, t),
                tacticalOpportunityDensity = Mathf.Lerp(0.86f, 0.56f, t),
                routeLegibility = Mathf.Lerp(0.94f, 0.58f, t),
                bossPursuitPressure = Mathf.Lerp(0.24f, 0.88f, t),
                destructionUtility = Mathf.Lerp(0.86f, 0.58f, t),
                decisionComplexity = Mathf.Lerp(0.30f, 0.82f, t),
                roadWidthScale = Mathf.Lerp(1.14f, 0.90f, t),
                blockMergeStrength = Mathf.Lerp(0.46f, 0.68f, t)
            };
            if (mission == AirCombatCityMission.BossEncounter)
            {
                result.bossPursuitPressure = Mathf.Clamp01(
                    result.bossPursuitPressure + 0.08f);
                result.destructionUtility = Mathf.Clamp01(
                    result.destructionUtility + 0.08f);
            }
            return result;
        }
    }

    [Serializable]
    public sealed class CombatCityMissionProfile
    {
        public CombatCityDifficultyProfile[] difficultyTiers =
            Array.Empty<CombatCityDifficultyProfile>();

        public void EnsureInitialized(AirCombatCityMission mission)
        {
            if (difficultyTiers == null || difficultyTiers.Length != 6)
                difficultyTiers = new CombatCityDifficultyProfile[6];
            for (int i = 0; i < difficultyTiers.Length; i++)
            {
                if (difficultyTiers[i] == null)
                {
                    difficultyTiers[i] =
                        CombatCityDifficultyProfile.CreateForTier(i, mission);
                }
            }
        }

        public CombatCityDifficultyProfile Resolve(
            int difficultyTier,
            AirCombatCityMission mission)
        {
            EnsureInitialized(mission);
            return difficultyTiers[Mathf.Clamp(difficultyTier, 0, 5)]
                .ValidatedCopy();
        }
    }

    [Serializable]
    public sealed class PlanetDifficultyBinding
    {
        [Range(1, 6)] public int planetIndex = 1;
        [Range(0f, 1f)] public float difficultyMultiplier;
        public int lockedSeed;
        public int lockedChecksum;
    }

    [CreateAssetMenu(
        fileName = "CombatCityPcgDesignProfile",
        menuName = "Planet Combat/Combat City PCG Design Profile")]
    public sealed class CombatCityPcgDesignProfile : ScriptableObject
    {
        public const int CurrentSchemaVersion = 1;
        public int schemaVersion = CurrentSchemaVersion;
        public string profileId = "combat-city-default";
        public string displayName = "战斗城市默认配置";
        public bool useVisualDistrictThemes = true;
        public CombatCityMissionProfile clearance = new CombatCityMissionProfile();
        public CombatCityMissionProfile assault = new CombatCityMissionProfile();
        public CombatCityMissionProfile boss = new CombatCityMissionProfile();
        public List<PlanetDifficultyBinding> planetBindings =
            new List<PlanetDifficultyBinding>();
        public int lockedSeed;
        public int lockedChecksum;

        public void EnsureInitialized()
        {
            schemaVersion = CurrentSchemaVersion;
            clearance = clearance ?? new CombatCityMissionProfile();
            assault = assault ?? new CombatCityMissionProfile();
            boss = boss ?? new CombatCityMissionProfile();
            clearance.EnsureInitialized(AirCombatCityMission.Clearance);
            assault.EnsureInitialized(AirCombatCityMission.FacilityAssault);
            boss.EnsureInitialized(AirCombatCityMission.BossEncounter);
            planetBindings = planetBindings ?? new List<PlanetDifficultyBinding>();
            for (int i = planetBindings.Count; i < 6; i++)
            {
                planetBindings.Add(new PlanetDifficultyBinding
                {
                    planetIndex = i + 1,
                    difficultyMultiplier = i / 5f
                });
            }
        }

        public CombatCityDifficultyProfile Resolve(
            AirCombatCityMission mission,
            int difficultyTier)
        {
            EnsureInitialized();
            switch (mission)
            {
                case AirCombatCityMission.FacilityAssault:
                    return assault.Resolve(difficultyTier, mission);
                case AirCombatCityMission.BossEncounter:
                    return boss.Resolve(difficultyTier, mission);
                default:
                    return clearance.Resolve(difficultyTier, mission);
            }
        }

        void OnValidate()
        {
            EnsureInitialized();
        }
    }

    public static class CombatDrivenCityPcgPlanner
    {
        const int TacticalBlockGridSize = 8;

        public static void Populate(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan)
        {
            CombatCityDifficultyProfile d = settings.Difficulty;
            float pitch = settings.buildingSpacing * 3f;
            float side = pitch * 2f;
            float connector = pitch * 3f;
            float upperCombatAltitude = Mathf.Lerp(
                settings.mediumAltitude,
                settings.highAltitude,
                0.35f);
            float loopRadius = settings.turnRadius * Mathf.Lerp(
                1.55f,
                1.22f,
                d.navigationChallenge);
            float safeWindow = Mathf.Lerp(10f, 13f, d.recoveryGenerosity);

            Add(plan, "opportunity.maneuver.center",
                TacticalOpportunityKind.ManeuverBowl, Volume(plan, "volume.center"),
                0.72f, 0.38f + d.combatPressure * 0.24f,
                d.navigationChallenge * 0.58f, d.routeLegibility, 0f, 0f, 0f,
                Points(
                    new Vector3(0f, settings.mediumAltitude, -connector),
                    new Vector3(0f, settings.mediumAltitude, connector),
                    new Vector3(-side, settings.mediumAltitude, 0f),
                    new Vector3(side, settings.mediumAltitude, 0f)),
                Points(
                    new Vector3(0f, settings.mediumAltitude, -connector),
                    new Vector3(0f, settings.mediumAltitude, connector),
                    new Vector3(-side, settings.mediumAltitude, 0f),
                    new Vector3(side, settings.mediumAltitude, 0f)),
                "opportunity.occlusion.west", "opportunity.exposure.east",
                "opportunity.kite.center");

            Bounds southGate = Volume(plan, "volume.mask.gate.south");
            Bounds northGate = Volume(plan, "volume.mask.gate.north");
            float maskedX = (southGate.center.x + northGate.center.x) * 0.5f;
            Bounds occlusion = new Bounds(
                new Vector3(maskedX, settings.mediumAltitude, 0f),
                new Vector3(settings.FlankCorridorWidth,
                    settings.maximumAltitude * 0.75f,
                    Mathf.Abs(northGate.center.z - southGate.center.z) +
                    Mathf.Max(southGate.size.z, northGate.size.z)));
            Add(plan, "opportunity.occlusion.west",
                TacticalOpportunityKind.OcclusionChain, occlusion,
                0.78f, 0.26f + d.combatPressure * 0.18f,
                d.navigationChallenge * 0.62f, d.routeLegibility * 0.94f,
                connector * 1.1f / settings.combatSpeed, 0f, 0f,
                Points(new Vector3(maskedX, settings.mediumAltitude, -connector),
                    new Vector3(maskedX, settings.mediumAltitude, connector)),
                Points(new Vector3(maskedX * 0.48f, settings.mediumAltitude, -connector),
                    new Vector3(maskedX * 0.48f, settings.mediumAltitude, connector)),
                "opportunity.maneuver.center", "opportunity.recovery.west",
                "opportunity.kite.center");

            Bounds exposureBounds = Volume(plan, "volume.exposure.east");
            float exposureX = exposureBounds.center.x;
            Add(plan, "opportunity.exposure.east",
                TacticalOpportunityKind.ExposureShortcut,
                exposureBounds,
                Mathf.Lerp(0.88f, 0.68f, d.exposurePressure),
                Mathf.Lerp(0.48f, 0.92f, d.exposurePressure),
                d.navigationChallenge * 0.46f, d.routeLegibility,
                Mathf.Lerp(3f, 5.5f, d.exposurePressure), 0f, 0f,
                Points(new Vector3(exposureX, upperCombatAltitude, -connector * 0.55f),
                    new Vector3(exposureX, upperCombatAltitude, connector * 0.55f)),
                Points(new Vector3(exposureX * 0.48f, settings.mediumAltitude, -connector),
                    new Vector3(exposureX * 0.48f, settings.mediumAltitude, connector)),
                "opportunity.maneuver.center", "opportunity.recovery.east",
                "opportunity.kite.center");

            AddRecovery(settings, plan, "opportunity.recovery.west",
                Volume(plan, "volume.recovery.west"), safeWindow, true);
            AddRecovery(settings, plan, "opportunity.recovery.east",
                Volume(plan, "volume.recovery.east"), safeWindow, false);

            Bounds kiteBounds = Volume(plan, "volume.kite-loop.center");
            Vector3 kiteCenter = kiteBounds.center;
            Add(plan, "opportunity.kite.center", TacticalOpportunityKind.KiteLoop,
                kiteBounds, 0.86f,
                0.42f + d.combatPressure * 0.24f,
                Mathf.Lerp(0.22f, 0.82f, d.navigationChallenge),
                d.routeLegibility * 0.92f,
                Mathf.PI * 2f * loopRadius / settings.combatSpeed, 0f, loopRadius,
                Points(kiteCenter + Vector3.left * loopRadius,
                    kiteCenter + Vector3.right * loopRadius,
                    kiteCenter + Vector3.back * loopRadius,
                    kiteCenter + Vector3.forward * loopRadius),
                Points(kiteCenter + Vector3.left * loopRadius * 1.35f,
                    kiteCenter + Vector3.right * loopRadius * 1.35f,
                    kiteCenter + Vector3.back * loopRadius * 1.35f,
                    kiteCenter + Vector3.forward * loopRadius * 1.35f),
                "opportunity.maneuver.center", "opportunity.occlusion.west",
                "opportunity.exposure.east", "opportunity.recovery.west",
                "opportunity.recovery.east");

            if (d.tacticalOpportunityDensity >= 0.42f)
            {
                Add(plan, "opportunity.vertical.center",
                    TacticalOpportunityKind.VerticalEscape,
                    new Bounds(new Vector3(0f, settings.mediumAltitude, connector * 0.42f),
                        new Vector3(settings.MainCorridorWidth * 0.82f,
                            upperCombatAltitude - settings.lowAltitude + 48f,
                            settings.combatSpeed * 2.4f)),
                    0.64f, 0.46f, d.navigationChallenge, d.routeLegibility,
                    2.6f, 0f, 0f,
                    Points(new Vector3(0f, settings.lowAltitude, connector * 0.30f),
                        new Vector3(0f, upperCombatAltitude, connector * 0.55f)),
                    Points(new Vector3(-settings.MainCorridorWidth,
                            settings.mediumAltitude, connector * 0.55f),
                        new Vector3(settings.MainCorridorWidth,
                            settings.mediumAltitude, connector * 0.55f)),
                    "opportunity.maneuver.center", "opportunity.kite.center");
            }

            if (settings.mission == AirCombatCityMission.BossEncounter ||
                d.destructionUtility >= 0.72f)
            {
                Add(plan, "opportunity.destruction.center",
                    TacticalOpportunityKind.DestructionAmbush,
                    new Bounds(new Vector3(0f, settings.mediumAltitude, -pitch * 1.50f),
                        new Vector3(settings.MainCorridorWidth * 1.2f,
                            settings.maximumAltitude * 0.74f, pitch * 0.86f)),
                    d.destructionUtility, 0.58f, 0.62f,
                    d.routeLegibility * 0.84f, 4f, 0f, 0f,
                    Points(new Vector3(0f, settings.lowAltitude, -pitch * 2f),
                        new Vector3(-side * 0.3f, settings.mediumAltitude, -pitch * 1.5f)),
                    Points(new Vector3(side * 0.3f, settings.mediumAltitude, -pitch * 1.5f),
                        new Vector3(0f, settings.highAltitude, -pitch)),
                    "opportunity.maneuver.center", "opportunity.kite.center");
            }

        }

        /// <summary>
        /// Converts semantic opportunities into guaranteed city geometry. These
        /// features are generated before the validation pass, so a region is not
        /// accepted merely because it has a name and a Bounds value.
        /// </summary>
        public static void BuildPhysicalRegionFeatures(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan)
        {
            BuildKiteLoopCore(settings, plan);
            BindOcclusionBuildings(settings, plan);
            BuildCombatBoundaryWall(settings, plan);
            BuildDestructionAmbush(settings, plan);
        }

        public static void BuildTacticalBlockLayout(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan)
        {
            plan.tacticalBlocks.Clear();
            float pitch = settings.buildingSpacing * 3f;
            float cityHalf = Mathf.Min(
                settings.mapSize * 0.5f - 5.2f,
                settings.buildingSpacing * 13f);
            float[] boundaries =
            {
                -cityHalf,
                -pitch * 3f,
                -pitch * 2f,
                -pitch,
                0f,
                pitch,
                pitch * 2f,
                pitch * 3f,
                cityHalf
            };

            for (int x = 0; x < TacticalBlockGridSize; x++)
            for (int z = 0; z < TacticalBlockGridSize; z++)
            {
                Vector3 center = new Vector3(
                    (boundaries[x] + boundaries[x + 1]) * 0.5f,
                    settings.maximumAltitude * 0.5f,
                    (boundaries[z] + boundaries[z + 1]) * 0.5f);
                Vector3 size = new Vector3(
                    boundaries[x + 1] - boundaries[x],
                    settings.maximumAltitude,
                    boundaries[z + 1] - boundaries[z]);
                bool perimeter = x == 0 || z == 0 ||
                                 x == TacticalBlockGridSize - 1 ||
                                 z == TacticalBlockGridSize - 1;
                TacticalOpportunity owner = FindNearestOpportunity(plan, center);
                CombatCityBlockRole role = perimeter
                    ? CombatCityBlockRole.CombatBoundary
                    : owner != null
                        ? BlockRoleFor(owner.kind)
                        : CombatCityBlockRole.Maneuver;
                ResolveBlockEffects(
                    role,
                    out float roadScale,
                    out float densityScale,
                    out float heightScale);
                plan.tacticalBlocks.Add(new CombatCityBlockPlan
                {
                    stableId = "tactical-block." + x.ToString("D2") + "." +
                               z.ToString("D2"),
                    gridX = x,
                    gridZ = z,
                    bounds = new Bounds(center, size),
                    role = role,
                    primaryOpportunityId = perimeter
                        ? "combat-boundary.perimeter"
                        : owner != null
                            ? owner.stableId
                            : "opportunity.maneuver.center",
                    roadWidthScale = roadScale,
                    buildingDensityScale = densityScale,
                    buildingHeightScale = heightScale
                });
            }

            AssignTacticalRegions(plan);
            SelectMergedBlockSeams(settings, plan, boundaries);
        }

        public static CombatCityBlockPlan GetTacticalBlock(
            AirCombatCityPlan plan,
            int gridX,
            int gridZ)
        {
            if (plan == null || gridX < 0 || gridZ < 0 ||
                gridX >= TacticalBlockGridSize ||
                gridZ >= TacticalBlockGridSize)
            {
                return null;
            }
            int index = gridX * TacticalBlockGridSize + gridZ;
            return index >= 0 && index < plan.tacticalBlocks.Count
                ? plan.tacticalBlocks[index]
                : null;
        }

        public static CombatCityBlockPlan FindTacticalBlock(
            AirCombatCityPlan plan,
            Vector2 position)
        {
            if (plan == null)
                return null;
            for (int i = 0; i < plan.tacticalBlocks.Count; i++)
            {
                CombatCityBlockPlan block = plan.tacticalBlocks[i];
                if (block.bounds.Contains(new Vector3(
                        position.x,
                        block.bounds.center.y,
                        position.y)))
                {
                    return block;
                }
            }
            return null;
        }

        static TacticalOpportunity FindNearestOpportunity(
            AirCombatCityPlan plan,
            Vector3 point)
        {
            TacticalOpportunity best = null;
            float bestScore = float.PositiveInfinity;
            for (int i = 0; i < plan.opportunities.Count; i++)
            {
                TacticalOpportunity opportunity = plan.opportunities[i];
                if (opportunity == null)
                    continue;
                Vector2 delta = new Vector2(
                    point.x - opportunity.bounds.center.x,
                    point.z - opportunity.bounds.center.z);
                float influence = Mathf.Max(
                    48f,
                    Mathf.Max(opportunity.bounds.extents.x,
                        opportunity.bounds.extents.z));
                float score = delta.sqrMagnitude / (influence * influence);
                if (score >= bestScore)
                    continue;
                best = opportunity;
                bestScore = score;
            }
            return best;
        }

        static CombatCityBlockRole BlockRoleFor(TacticalOpportunityKind kind)
        {
            switch (kind)
            {
                case TacticalOpportunityKind.OcclusionChain:
                    return CombatCityBlockRole.Occlusion;
                case TacticalOpportunityKind.ExposureShortcut:
                    return CombatCityBlockRole.Exposure;
                case TacticalOpportunityKind.RecoveryPocket:
                    return CombatCityBlockRole.Recovery;
                case TacticalOpportunityKind.KiteLoop:
                    return CombatCityBlockRole.Kite;
                case TacticalOpportunityKind.TacticalChoke:
                    return CombatCityBlockRole.TacticalChoke;
                case TacticalOpportunityKind.VerticalEscape:
                    return CombatCityBlockRole.Vertical;
                case TacticalOpportunityKind.AttackPerch:
                    return CombatCityBlockRole.Attack;
                case TacticalOpportunityKind.DestructionAmbush:
                    return CombatCityBlockRole.Destruction;
                default:
                    return CombatCityBlockRole.Maneuver;
            }
        }

        static void ResolveBlockEffects(
            CombatCityBlockRole role,
            out float roadWidth,
            out float density,
            out float height)
        {
            switch (role)
            {
                case CombatCityBlockRole.Occlusion:
                    roadWidth = 0.86f; density = 1.12f; height = 1.12f; return;
                case CombatCityBlockRole.Exposure:
                    roadWidth = 1.28f; density = 0.72f; height = 0.82f; return;
                case CombatCityBlockRole.Recovery:
                    roadWidth = 0.82f; density = 0.78f; height = 0.76f; return;
                case CombatCityBlockRole.Kite:
                    roadWidth = 1.02f; density = 0.94f; height = 1.05f; return;
                case CombatCityBlockRole.TacticalChoke:
                    roadWidth = 0.84f; density = 1.08f; height = 1.08f; return;
                case CombatCityBlockRole.Vertical:
                    roadWidth = 1.18f; density = 0.80f; height = 0.88f; return;
                case CombatCityBlockRole.Attack:
                    roadWidth = 0.92f; density = 1.02f; height = 1.18f; return;
                case CombatCityBlockRole.Destruction:
                    roadWidth = 0.90f; density = 1.04f; height = 1.12f; return;
                case CombatCityBlockRole.CombatBoundary:
                    roadWidth = 0.88f; density = 1.26f; height = 1.24f; return;
                default:
                    roadWidth = 1.16f; density = 0.82f; height = 0.86f; return;
            }
        }

        static void AssignTacticalRegions(AirCombatCityPlan plan)
        {
            var visited = new bool[plan.tacticalBlocks.Count];
            int region = 0;
            for (int start = 0; start < plan.tacticalBlocks.Count; start++)
            {
                if (visited[start])
                    continue;
                CombatCityBlockRole role = plan.tacticalBlocks[start].role;
                string regionId = "tactical-region." +
                                  region++.ToString("D2") + "." + role;
                var queue = new Queue<int>();
                queue.Enqueue(start);
                visited[start] = true;
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    CombatCityBlockPlan block = plan.tacticalBlocks[current];
                    block.tacticalRegionId = regionId;
                    int x = block.gridX;
                    int z = block.gridZ;
                    VisitRegionNeighbour(plan, visited, queue, x - 1, z, role);
                    VisitRegionNeighbour(plan, visited, queue, x + 1, z, role);
                    VisitRegionNeighbour(plan, visited, queue, x, z - 1, role);
                    VisitRegionNeighbour(plan, visited, queue, x, z + 1, role);
                }
            }
        }

        static void VisitRegionNeighbour(
            AirCombatCityPlan plan,
            bool[] visited,
            Queue<int> queue,
            int x,
            int z,
            CombatCityBlockRole role)
        {
            CombatCityBlockPlan block = GetTacticalBlock(plan, x, z);
            if (block == null)
                return;
            int index = x * TacticalBlockGridSize + z;
            if (visited[index] || block.role != role)
                return;
            visited[index] = true;
            queue.Enqueue(index);
        }

        static void SelectMergedBlockSeams(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan,
            float[] boundaries)
        {
            int count = plan.tacticalBlocks.Count;
            var parent = new int[count];
            var fallbackSeams = new List<MergeSeamCandidate>(32);
            for (int i = 0; i < count; i++)
                parent[i] = i;
            float chance = Mathf.Lerp(
                0.28f,
                0.70f,
                settings.Difficulty.blockMergeStrength);
            int selectedSeams = 0;
            int selectedOcclusionSeams = 0;
            for (int x = 0; x < TacticalBlockGridSize; x++)
            for (int z = 0; z < TacticalBlockGridSize; z++)
            {
                CombatCityBlockPlan block = GetTacticalBlock(plan, x, z);
                if (x + 1 < TacticalBlockGridSize)
                {
                    CombatCityBlockPlan east = GetTacticalBlock(plan, x + 1, z);
                    float seamX = boundaries[x + 1];
                    if (CanMergeBlocks(block, east, seamX, settings, true))
                    {
                        float roll = StableChance(settings.seed, x, z, 17);
                        fallbackSeams.Add(new MergeSeamCandidate
                        {
                            block = block,
                            firstIndex = x * TacticalBlockGridSize + z,
                            secondIndex = (x + 1) * TacticalBlockGridSize + z,
                            east = true,
                            role = block.role,
                            priority = roll
                        });
                        float roleChance = block.role == CombatCityBlockRole.Occlusion
                            ? Mathf.Max(0.88f, chance)
                            : block.role == CombatCityBlockRole.CombatBoundary
                                ? Mathf.Max(0.62f, chance)
                                : chance;
                        if (roll < roleChance)
                        {
                            block.mergeEast = true;
                            Union(parent,
                                x * TacticalBlockGridSize + z,
                                (x + 1) * TacticalBlockGridSize + z);
                            selectedSeams++;
                            if (block.role == CombatCityBlockRole.Occlusion)
                                selectedOcclusionSeams++;
                        }
                    }
                }
                if (z + 1 < TacticalBlockGridSize)
                {
                    CombatCityBlockPlan north = GetTacticalBlock(plan, x, z + 1);
                    float seamZ = boundaries[z + 1];
                    if (CanMergeBlocks(block, north, seamZ, settings, false))
                    {
                        float roll = StableChance(settings.seed, x, z, 43);
                        fallbackSeams.Add(new MergeSeamCandidate
                        {
                            block = block,
                            firstIndex = x * TacticalBlockGridSize + z,
                            secondIndex = x * TacticalBlockGridSize + z + 1,
                            east = false,
                            role = block.role,
                            priority = roll
                        });
                        float roleChance = block.role == CombatCityBlockRole.Occlusion
                            ? Mathf.Max(0.88f, chance)
                            : block.role == CombatCityBlockRole.CombatBoundary
                                ? Mathf.Max(0.62f, chance)
                                : chance;
                        if (roll < roleChance)
                        {
                            block.mergeNorth = true;
                            Union(parent,
                                x * TacticalBlockGridSize + z,
                                x * TacticalBlockGridSize + z + 1);
                            selectedSeams++;
                            if (block.role == CombatCityBlockRole.Occlusion)
                                selectedOcclusionSeams++;
                        }
                    }
                }
            }

            // A low merge slider controls frequency, not whether the feature
            // exists at all. Guarantee a few readable super-blocks whenever
            // compatible neighbours exist so every accepted city demonstrates
            // real removal of internal road segments.
            fallbackSeams.Sort((a, b) => a.priority.CompareTo(b.priority));
            int occlusionCandidateCount = 0;
            for (int i = 0; i < fallbackSeams.Count; i++)
            {
                if (fallbackSeams[i].role == CombatCityBlockRole.Occlusion)
                    occlusionCandidateCount++;
            }
            int guaranteedOcclusionSeams = Mathf.CeilToInt(
                occlusionCandidateCount * 0.75f);
            for (int i = 0; i < fallbackSeams.Count &&
                            selectedOcclusionSeams < guaranteedOcclusionSeams; i++)
            {
                MergeSeamCandidate seam = fallbackSeams[i];
                if (seam.role != CombatCityBlockRole.Occlusion ||
                    (seam.east ? seam.block.mergeEast : seam.block.mergeNorth))
                {
                    continue;
                }
                if (seam.east)
                    seam.block.mergeEast = true;
                else
                    seam.block.mergeNorth = true;
                Union(parent, seam.firstIndex, seam.secondIndex);
                selectedSeams++;
                selectedOcclusionSeams++;
            }

            int guaranteedSeams = Mathf.Min(
                Mathf.RoundToInt(Mathf.Lerp(
                    10f,
                    20f,
                    settings.Difficulty.blockMergeStrength)),
                fallbackSeams.Count);
            for (int i = 0; i < fallbackSeams.Count &&
                            selectedSeams < guaranteedSeams; i++)
            {
                MergeSeamCandidate seam = fallbackSeams[i];
                if (seam.east ? seam.block.mergeEast : seam.block.mergeNorth)
                    continue;
                if (seam.east)
                    seam.block.mergeEast = true;
                else
                    seam.block.mergeNorth = true;
                Union(parent, seam.firstIndex, seam.secondIndex);
                selectedSeams++;
            }
            var groupSizes = new Dictionary<int, int>();
            for (int i = 0; i < count; i++)
            {
                int root = FindRoot(parent, i);
                groupSizes[root] = groupSizes.TryGetValue(root, out int size)
                    ? size + 1
                    : 1;
            }
            for (int i = 0; i < count; i++)
            {
                int root = FindRoot(parent, i);
                plan.tacticalBlocks[i].mergedGroupId = groupSizes[root] > 1
                    ? "merged-block-group." + root.ToString("D2")
                    : plan.tacticalBlocks[i].stableId;
            }
        }

        struct MergeSeamCandidate
        {
            public CombatCityBlockPlan block;
            public int firstIndex;
            public int secondIndex;
            public bool east;
            public CombatCityBlockRole role;
            public float priority;
        }

        static bool CanMergeBlocks(
            CombatCityBlockPlan first,
            CombatCityBlockPlan second,
            float seamCoordinate,
            AirCombatCitySettings settings,
            bool verticalSeam)
        {
            if (first == null || second == null || first.role != second.role)
                return false;
            float pitch = settings.buildingSpacing * 3f;
            if (Mathf.Abs(seamCoordinate) < 0.01f)
                return false;
            if (verticalSeam && Mathf.Abs(seamCoordinate - pitch * 2f) < 0.01f)
                return false;
            return true;
        }

        static float StableChance(int seed, int x, int z, int salt)
        {
            unchecked
            {
                int value = seed;
                value = value * 486187739 + x * 92821;
                value = value * 16777619 + z * 68917 + salt;
                return (value & 0x7fffffff) / (float)int.MaxValue;
            }
        }

        static int FindRoot(int[] parent, int index)
        {
            while (parent[index] != index)
            {
                parent[index] = parent[parent[index]];
                index = parent[index];
            }
            return index;
        }

        static void Union(int[] parent, int first, int second)
        {
            int a = FindRoot(parent, first);
            int b = FindRoot(parent, second);
            if (a != b)
                parent[Mathf.Max(a, b)] = Mathf.Min(a, b);
        }

        /// <summary>
        /// Binds opportunities that can only be resolved after the building pass
        /// (for example a firing position on a real generated roof).
        /// </summary>
        public static void BindGeneratedFeatures(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan)
        {
            plan.opportunities.RemoveAll(
                opportunity => opportunity != null &&
                               opportunity.kind == TacticalOpportunityKind.AttackPerch);
            plan.volumes.RemoveAll(volume =>
                volume != null &&
                volume.kind == AirCombatVolumeKind.DominancePerch);

            BindOpportunityToRoute(
                plan,
                TacticalOpportunityKind.VerticalEscape,
                "route.vertical-escape.central-avenue");
            BindOpportunityToRoute(
                plan,
                TacticalOpportunityKind.ExposureShortcut,
                "route.long-range");
            BindOpportunityToRoute(
                plan,
                TacticalOpportunityKind.KiteLoop,
                "route.kite-loop.city-block");

            SetPhysicalCount(
                plan,
                TacticalOpportunityKind.KiteLoop,
                CountCluster(plan, 1201));
            SetPhysicalCount(
                plan,
                TacticalOpportunityKind.OcclusionChain,
                CountCluster(plan, 1202));
            SetPhysicalCount(
                plan,
                TacticalOpportunityKind.DestructionAmbush,
                CountCluster(plan, 1203));
        }

        static void BuildKiteLoopCore(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan)
        {
            TacticalOpportunity opportunity = FindOpportunity(
                plan,
                TacticalOpportunityKind.KiteLoop);
            if (opportunity == null)
                return;

            const float Width = 62f;
            const float Depth = 62f;
            float height = Mathf.Clamp(
                settings.mediumAltitude + 38f,
                150f,
                settings.highAltitude - 12f);
            Vector2 point = new Vector2(
                opportunity.bounds.center.x,
                opportunity.bounds.center.z);
            RemoveBuildingsAt(plan, point, new Vector2(Width + 16f, Depth + 16f));
            plan.buildings.Add(new AirCombatBuildingLot
            {
                stableId = "building.combat-region.kite-core",
                center = new Vector3(point.x, height * 0.5f, point.y),
                size = new Vector3(Width, height, Depth),
                yaw = 0f,
                band = AirCombatBuildingBand.Medium,
                archetype = AirCombatBuildingArchetype.MidSlab,
                clusterId = 1201,
                visualVariant = PositiveModulo(settings.seed + 1201, 97)
            });
        }

        static void BindOcclusionBuildings(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan)
        {
            AirCombatFlightRoute route = FindRoute(plan, "route.masked-flank");
            if (route == null || route.points == null || route.points.Length < 4)
                return;

            float minimumZ = Mathf.Min(route.points[2].z, route.points[3].z);
            float maximumZ = Mathf.Max(route.points[2].z, route.points[3].z);
            float routeX = (route.points[2].x + route.points[3].x) * 0.5f;
            float lateralOffset = route.width * 0.5f + 54f;
            var used = new HashSet<AirCombatBuildingLot>();
            const int TowersPerSide = 6;
            for (int sideIndex = 0; sideIndex < 2; sideIndex++)
            for (int feature = 0; feature < TowersPerSide; feature++)
            {
                float targetZ = Mathf.Lerp(minimumZ, maximumZ,
                    (feature + 0.5f) / TowersPerSide);
                float targetX = routeX + (sideIndex == 0 ? -1f : 1f) *
                                lateralOffset;
                AirCombatBuildingLot best = null;
                float bestScore = float.PositiveInfinity;
                for (int i = 0; i < plan.buildings.Count; i++)
                {
                    AirCombatBuildingLot building = plan.buildings[i];
                    if (building.band == AirCombatBuildingBand.Facility ||
                        building.clusterId >= 1200 ||
                        used.Contains(building) ||
                        BuildingIntersectsRouteFootprint(building, route))
                    {
                        continue;
                    }
                    float score = Vector2.Distance(
                        new Vector2(building.center.x, building.center.z),
                        new Vector2(targetX, targetZ));
                    if (score > settings.buildingSpacing * 1.65f)
                        continue;
                    if (score >= bestScore)
                        continue;
                    best = building;
                    bestScore = score;
                }
                if (best == null)
                    continue;
                used.Add(best);
                float height = Mathf.Max(
                    best.size.y,
                    settings.maximumAltitude +
                    Mathf.Lerp(18f, 42f,
                        (feature + sideIndex * 2f) /
                        (TowersPerSide + 1f)));
                best.size = new Vector3(best.size.x, height, best.size.z);
                best.center = new Vector3(
                    best.center.x,
                    height * 0.5f,
                    best.center.z);
                best.band = AirCombatBuildingBand.High;
                best.archetype = AirCombatBuildingArchetype.CombatTower;
                best.clusterId = 1202;
            }

            // The visual wall also has to perform its advertised job.  Select
            // real, road-aligned buildings that intersect representative
            // player-to-threat sight lines until at least four samples are
            // physically broken.  Promoting existing lots preserves the road
            // and parcel rules; excluding every authored player route prevents
            // the extra height from turning a safe route into a hidden blocker.
            EnsureMeasuredOcclusionBreaks(settings, plan, route, used, 4);

            TacticalOpportunity opportunity = FindOpportunity(
                plan,
                TacticalOpportunityKind.OcclusionChain);
            if (opportunity != null)
            {
                opportunity.physicalFeatureCount = used.Count;
                opportunity.runtimeBindingId = "cluster.1202.occlusion-wall";
            }
        }

        static AirCombatBuildingLot TryCreateOcclusionSightTower(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan,
            Vector2 player,
            Vector2 threat,
            int sample)
        {
            Vector2 footprint = new Vector2(32f, 32f);
            for (int step = 0; step < 20; step++)
            {
                float lineT = Mathf.Lerp(0.24f, 0.76f, step / 19f);
                Vector2 point = Vector2.Lerp(player, threat, lineT);
                if (Mathf.Abs(point.x) > settings.mapSize * 0.46f ||
                    Mathf.Abs(point.y) > settings.mapSize * 0.46f ||
                    OcclusionTowerHitsRoad(plan, point, footprint) ||
                    OcclusionTowerHitsBuilding(plan, point, footprint))
                {
                    continue;
                }

                var candidate = new AirCombatBuildingLot
                {
                    stableId = "building.combat-region.occlusion-sight." +
                               sample.ToString("D2"),
                    center = new Vector3(point.x, 1f, point.y),
                    size = new Vector3(footprint.x, 2f, footprint.y),
                    yaw = 0f,
                    band = AirCombatBuildingBand.Low,
                    archetype = AirCombatBuildingArchetype.LowBlock,
                    clusterId = 0,
                    visualVariant = PositiveModulo(
                        settings.seed + 1601 + sample * 41,
                        97)
                };
                if (BuildingIntersectsAnyPlayerRoute(candidate, plan))
                    continue;
                plan.buildings.Add(candidate);
                return candidate;
            }
            return null;
        }

        static bool OcclusionTowerHitsRoad(
            AirCombatCityPlan plan,
            Vector2 point,
            Vector2 footprint)
        {
            const float SidewalkWidth = 6.2f;
            for (int i = 0; i < plan.roads.Count; i++)
            {
                AirCombatRoadStrip road = plan.roads[i];
                if (AirCombatCityGenerator.FootprintIntersectsCorridor(
                        point,
                        footprint,
                        0f,
                        new Vector2(road.start.x, road.start.z),
                        new Vector2(road.end.x, road.end.z),
                        road.width * 0.5f + SidewalkWidth,
                        out _))
                {
                    return true;
                }
            }
            return false;
        }

        static bool OcclusionTowerHitsBuilding(
            AirCombatCityPlan plan,
            Vector2 point,
            Vector2 footprint)
        {
            for (int i = 0; i < plan.buildings.Count; i++)
            {
                AirCombatBuildingLot building = plan.buildings[i];
                float yaw = Mathf.Abs(Mathf.DeltaAngle(building.yaw, 90f));
                bool quarterTurn = yaw < 45f || yaw > 135f;
                Vector2 size = quarterTurn
                    ? new Vector2(building.size.z, building.size.x)
                    : new Vector2(building.size.x, building.size.z);
                if (Mathf.Abs(building.center.x - point.x) <=
                        (size.x + footprint.x) * 0.5f + 4f &&
                    Mathf.Abs(building.center.z - point.y) <=
                        (size.y + footprint.y) * 0.5f + 4f)
                {
                    return true;
                }
            }
            return false;
        }

        static void EnsureMeasuredOcclusionBreaks(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan,
            AirCombatFlightRoute route,
            HashSet<AirCombatBuildingLot> used,
            int requiredBreaks)
        {
            if (route == null || route.points == null || route.points.Length < 4)
                return;

            Vector2 threat = new Vector2(plan.objective.x, plan.objective.z);
            const int Samples = 12;
            int breaks = 0;
            for (int sample = 0; sample < Samples; sample++)
            {
                float t = (sample + 0.5f) / Samples;
                Vector3 point3 = Vector3.Lerp(route.points[2], route.points[3], t);
                Vector2 player = new Vector2(point3.x, point3.z);
                if (SightLineBlockedByCluster(plan, player, threat, 1202))
                {
                    breaks++;
                    continue;
                }
                if (breaks >= requiredBreaks)
                    continue;

                AirCombatBuildingLot best = null;
                float bestScore = float.PositiveInfinity;
                Vector2 preferred = Vector2.Lerp(player, threat, 0.48f);
                for (int i = 0; i < plan.buildings.Count; i++)
                {
                    AirCombatBuildingLot building = plan.buildings[i];
                    if (building.band == AirCombatBuildingBand.Facility ||
                        building.clusterId >= 900 ||
                        used.Contains(building) ||
                        BuildingIntersectsAnyPlayerRoute(building, plan) ||
                        !AirCombatCityGenerator.FootprintIntersectsCorridor(
                            new Vector2(building.center.x, building.center.z),
                            new Vector2(building.size.x, building.size.z),
                            building.yaw,
                            player,
                            threat,
                            1f,
                            out _))
                    {
                        continue;
                    }

                    float score = Vector2.Distance(
                        new Vector2(building.center.x, building.center.z),
                        preferred);
                    if (score >= bestScore)
                        continue;
                    best = building;
                    bestScore = score;
                }

                if (best == null)
                {
                    best = TryCreateOcclusionSightTower(
                        settings,
                        plan,
                        player,
                        threat,
                        sample);
                    if (best == null)
                        continue;
                }
                used.Add(best);
                float height = Mathf.Max(
                    best.size.y,
                    settings.maximumAltitude + 26f + sample * 3f);
                best.size = new Vector3(best.size.x, height, best.size.z);
                best.center = new Vector3(
                    best.center.x,
                    height * 0.5f,
                    best.center.z);
                best.band = AirCombatBuildingBand.High;
                best.archetype = AirCombatBuildingArchetype.CombatTower;
                best.clusterId = 1202;
                breaks++;
            }
        }

        static bool SightLineBlockedByCluster(
            AirCombatCityPlan plan,
            Vector2 player,
            Vector2 threat,
            int clusterId)
        {
            for (int i = 0; i < plan.buildings.Count; i++)
            {
                AirCombatBuildingLot building = plan.buildings[i];
                if (building.clusterId != clusterId)
                    continue;
                if (AirCombatCityGenerator.FootprintIntersectsCorridor(
                        new Vector2(building.center.x, building.center.z),
                        new Vector2(building.size.x, building.size.z),
                        building.yaw,
                        player,
                        threat,
                        1f,
                        out _))
                {
                    return true;
                }
            }
            return false;
        }

        static bool BuildingIntersectsRouteFootprint(
            AirCombatBuildingLot building,
            AirCombatFlightRoute route)
        {
            Vector2 point = new Vector2(building.center.x, building.center.z);
            for (int i = 1; i < route.points.Length; i++)
            {
                if (AirCombatCityGenerator.FootprintIntersectsCorridor(
                        point,
                        new Vector2(building.size.x, building.size.z),
                        building.yaw,
                        new Vector2(route.points[i - 1].x,
                            route.points[i - 1].z),
                        new Vector2(route.points[i].x,
                            route.points[i].z),
                        route.width * 0.5f + 4f,
                        out _))
                {
                    return true;
                }
            }
            return false;
        }

        static void BuildCombatBoundaryWall(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan)
        {
            for (int i = 0; i < plan.buildings.Count; i++)
            {
                AirCombatBuildingLot building = plan.buildings[i];
                if (building.band == AirCombatBuildingBand.Facility ||
                    building.clusterId >= 1200)
                {
                    continue;
                }
                CombatCityBlockPlan block = FindTacticalBlock(
                    plan,
                    new Vector2(building.center.x, building.center.z));
                if (block == null ||
                    block.role != CombatCityBlockRole.CombatBoundary ||
                    BuildingIntersectsAnyPlayerRoute(building, plan))
                {
                    continue;
                }
                float height = Mathf.Max(
                    building.size.y,
                    settings.maximumAltitude + 24f +
                    PositiveModulo(settings.seed + i * 17, 24));
                building.size = new Vector3(
                    building.size.x,
                    height,
                    building.size.z);
                building.center = new Vector3(
                    building.center.x,
                    height * 0.5f,
                    building.center.z);
                building.band = AirCombatBuildingBand.High;
                building.archetype = AirCombatBuildingArchetype.CombatTower;
                building.clusterId = 1205;
            }

            plan.boundaryWalls.Clear();
            float innerHalfExtent = settings.mapSize * 0.5f - 5.2f;
            float thickness = Mathf.Max(12f, settings.wingspan * 0.72f);
            float boundaryHeight = settings.maximumAltitude +
                                   Mathf.Max(140f, settings.highAltitude * 0.64f);
            float centerY = boundaryHeight * 0.5f - 12f;
            float span = innerHalfExtent * 2f + thickness * 2f;
            AddBoundaryAirWall(plan, "north",
                new Vector3(0f, centerY, innerHalfExtent + thickness * 0.5f),
                new Vector3(span, boundaryHeight, thickness));
            AddBoundaryAirWall(plan, "south",
                new Vector3(0f, centerY, -innerHalfExtent - thickness * 0.5f),
                new Vector3(span, boundaryHeight, thickness));
            AddBoundaryAirWall(plan, "east",
                new Vector3(innerHalfExtent + thickness * 0.5f, centerY, 0f),
                new Vector3(thickness, boundaryHeight, span));
            AddBoundaryAirWall(plan, "west",
                new Vector3(-innerHalfExtent - thickness * 0.5f, centerY, 0f),
                new Vector3(thickness, boundaryHeight, span));
        }

        static void AddBoundaryAirWall(
            AirCombatCityPlan plan,
            string side,
            Vector3 center,
            Vector3 size)
        {
            plan.boundaryWalls.Add(new AirCombatBoundaryWallPlan
            {
                stableId = "boundary.air-wall." + side,
                center = center,
                size = size
            });
        }

        static bool BuildingIntersectsAnyPlayerRoute(
            AirCombatBuildingLot building,
            AirCombatCityPlan plan)
        {
            for (int i = 0; i < plan.routes.Count; i++)
            {
                AirCombatFlightRoute route = plan.routes[i];
                if (route.kind != AirCombatRouteKind.EnemyIngress &&
                    BuildingIntersectsRouteFootprint(building, route))
                {
                    return true;
                }
            }
            return false;
        }

        static void BuildDestructionAmbush(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan)
        {
            TacticalOpportunity opportunity = FindOpportunity(
                plan,
                TacticalOpportunityKind.DestructionAmbush);
            if (opportunity == null)
                return;

            float pitch = settings.buildingSpacing * 3f;
            float z = -pitch * 1.50f;
            float[] xPositions =
            {
                -Mathf.Max(122f, settings.MainCorridorWidth * 0.72f),
                pitch + Mathf.Max(62f, settings.LongRangeCorridorWidth * 0.48f)
            };
            opportunity.bounds = new Bounds(
                new Vector3((xPositions[0] + xPositions[1]) * 0.5f,
                    settings.mediumAltitude, z),
                new Vector3(xPositions[1] - xPositions[0] + 82f,
                    settings.maximumAltitude * 0.74f, pitch * 0.86f));
            for (int i = 0; i < xPositions.Length; i++)
            {
                Vector2 point = new Vector2(xPositions[i], z);
                Vector2 footprint = new Vector2(38f, 54f);
                RemoveBuildingsAt(plan, point, footprint + Vector2.one * 14f);
                float height = 168f + i * 12f;
                plan.buildings.Add(new AirCombatBuildingLot
                {
                    stableId = "building.combat-region.collapse-candidate." + i,
                    center = new Vector3(point.x, height * 0.5f, point.y),
                    size = new Vector3(footprint.x, height, footprint.y),
                    yaw = i == 0 ? 90f : -90f,
                    band = AirCombatBuildingBand.High,
                    archetype = AirCombatBuildingArchetype.CombatTower,
                    clusterId = 1203,
                    visualVariant = PositiveModulo(settings.seed + 1217 + i * 31, 97)
                });
            }
        }

        static void RemoveBuildingsAt(
            AirCombatCityPlan plan,
            Vector2 point,
            Vector2 footprint)
        {
            for (int i = plan.buildings.Count - 1; i >= 0; i--)
            {
                AirCombatBuildingLot building = plan.buildings[i];
                if (building.band == AirCombatBuildingBand.Facility)
                    continue;
                if (Mathf.Abs(building.center.x - point.x) <=
                        (building.size.x + footprint.x) * 0.5f &&
                    Mathf.Abs(building.center.z - point.y) <=
                        (building.size.z + footprint.y) * 0.5f)
                {
                    plan.buildings.RemoveAt(i);
                }
            }
        }

        static void BindOpportunityToRoute(
            AirCombatCityPlan plan,
            TacticalOpportunityKind kind,
            string routeId)
        {
            TacticalOpportunity opportunity = FindOpportunity(plan, kind);
            AirCombatFlightRoute route = FindRoute(plan, routeId);
            if (opportunity == null || route == null)
                return;
            opportunity.runtimeBindingId = routeId;
            opportunity.physicalFeatureCount = Mathf.Max(
                opportunity.physicalFeatureCount, 1);
        }

        static void SetPhysicalCount(
            AirCombatCityPlan plan,
            TacticalOpportunityKind kind,
            int count)
        {
            TacticalOpportunity opportunity = FindOpportunity(plan, kind);
            if (opportunity == null)
                return;
            opportunity.physicalFeatureCount = count;
        }

        static TacticalOpportunity FindOpportunity(
            AirCombatCityPlan plan,
            TacticalOpportunityKind kind)
        {
            for (int i = 0; i < plan.opportunities.Count; i++)
            {
                TacticalOpportunity opportunity = plan.opportunities[i];
                if (opportunity != null && opportunity.kind == kind)
                    return opportunity;
            }
            return null;
        }

        static AirCombatFlightRoute FindRoute(
            AirCombatCityPlan plan,
            string stableId)
        {
            for (int i = 0; i < plan.routes.Count; i++)
            {
                if (plan.routes[i].stableId == stableId)
                    return plan.routes[i];
            }
            return null;
        }

        static int CountCluster(AirCombatCityPlan plan, int clusterId)
        {
            int count = 0;
            for (int i = 0; i < plan.buildings.Count; i++)
            {
                if (plan.buildings[i].clusterId == clusterId)
                    count++;
            }
            return count;
        }

        static int CountOcclusionSightBreaks(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan)
        {
            AirCombatFlightRoute route = FindRoute(plan, "route.masked-flank");
            if (route == null || route.points == null || route.points.Length < 4)
                return 0;
            int breaks = 0;
            Vector2 threat = new Vector2(plan.objective.x, plan.objective.z);
            const int Samples = 12;
            for (int sample = 0; sample < Samples; sample++)
            {
                float t = (sample + 0.5f) / Samples;
                Vector3 routePoint = Vector3.Lerp(
                    route.points[2],
                    route.points[3],
                    t);
                Vector2 player = new Vector2(routePoint.x, routePoint.z);
                bool blocked = false;
                for (int i = 0; i < plan.buildings.Count; i++)
                {
                    AirCombatBuildingLot building = plan.buildings[i];
                    float top = building.center.y + building.size.y * 0.5f;
                    if (building.clusterId != 1202 ||
                        top < settings.mediumAltitude + 8f)
                    {
                        continue;
                    }
                    if (AirCombatCityGenerator.FootprintIntersectsCorridor(
                            new Vector2(building.center.x, building.center.z),
                            new Vector2(building.size.x, building.size.z),
                            building.yaw,
                            player,
                            threat,
                            1f,
                            out _))
                    {
                        blocked = true;
                        break;
                    }
                }
                if (blocked)
                    breaks++;
            }
            return breaks;
        }

        static int CountRecoveryPocketsWithBlockedOutput(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan)
        {
            int count = 0;
            Vector2 threat = new Vector2(plan.objective.x, plan.objective.z);
            for (int volumeIndex = 0;
                 volumeIndex < plan.volumes.Count;
                 volumeIndex++)
            {
                AirCombatTacticalVolume volume = plan.volumes[volumeIndex];
                if (volume.kind != AirCombatVolumeKind.RecoveryPocket)
                    continue;
                Vector2 pocket = new Vector2(volume.center.x, volume.center.z);
                bool blocked = false;
                for (int buildingIndex = 0;
                     buildingIndex < plan.buildings.Count;
                     buildingIndex++)
                {
                    AirCombatBuildingLot building = plan.buildings[buildingIndex];
                    if (building.clusterId < 970 || building.clusterId > 971)
                        continue;
                    float top = building.center.y + building.size.y * 0.5f;
                    if (top < settings.mediumAltitude + 8f)
                        continue;
                    if (AirCombatCityGenerator.FootprintIntersectsCorridor(
                            new Vector2(building.center.x, building.center.z),
                            new Vector2(building.size.x, building.size.z),
                            building.yaw,
                            pocket,
                            threat,
                            1f,
                            out _))
                    {
                        blocked = true;
                        break;
                    }
                }
                if (blocked)
                    count++;
            }
            return count;
        }

        static bool BoundaryAirWallsAreValid(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan)
        {
            if (plan.boundaryWalls.Count != 4)
                return false;
            bool north = false;
            bool south = false;
            bool east = false;
            bool west = false;
            float minimumHeight = settings.maximumAltitude + 100f;
            float minimumSpan = settings.mapSize * 0.95f;
            for (int i = 0; i < plan.boundaryWalls.Count; i++)
            {
                AirCombatBoundaryWallPlan wall = plan.boundaryWalls[i];
                if (wall == null || wall.size.y < minimumHeight)
                    return false;
                bool horizontal = wall.size.x >= minimumSpan &&
                                  wall.size.z >= 10f;
                bool vertical = wall.size.z >= minimumSpan &&
                                wall.size.x >= 10f;
                if (horizontal && wall.center.z > 0f)
                    north = true;
                else if (horizontal && wall.center.z < 0f)
                    south = true;
                else if (vertical && wall.center.x > 0f)
                    east = true;
                else if (vertical && wall.center.x < 0f)
                    west = true;
                else
                    return false;
            }
            return north && south && east && west;
        }

        static int PositiveModulo(int value, int modulo)
        {
            int result = value % modulo;
            return result < 0 ? result + modulo : result;
        }

        public static void Validate(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan,
            AirCombatCityReport report)
        {
            report.tacticalOpportunityCount = plan.opportunities.Count;
            report.minimumTacticalChoices = int.MaxValue;
            report.nearestRecoverySeconds = float.PositiveInfinity;
            var ids = new HashSet<string>();
            bool valid = true;
            bool exposureValid = false;
            bool kiteValid = false;
            float firstUtility = 0f;
            float secondUtility = 0f;
            foreach (TacticalOpportunity opportunity in plan.opportunities)
            {
                valid &= opportunity != null && !string.IsNullOrEmpty(opportunity.stableId) &&
                         ids.Add(opportunity.stableId);
                if (opportunity.utility >= firstUtility)
                {
                    secondUtility = firstUtility;
                    firstUtility = opportunity.utility;
                }
                else if (opportunity.utility > secondUtility)
                    secondUtility = opportunity.utility;
            }
            foreach (TacticalOpportunity opportunity in plan.opportunities)
            {
                if (opportunity == null)
                    continue;
                int choices = opportunity.connectedOpportunityIds?.Length ?? 0;
                report.minimumTacticalChoices = Mathf.Min(
                    report.minimumTacticalChoices, choices);
                valid &= opportunity.exits != null && opportunity.exits.Length >= 2;
                if (opportunity.connectedOpportunityIds != null)
                foreach (string target in opportunity.connectedOpportunityIds)
                    valid &= ids.Contains(target);
                if (opportunity.kind == TacticalOpportunityKind.ExposureShortcut)
                {
                    report.exposureShortcutCount++;
                    report.longestExposureSeconds = Mathf.Max(
                        report.longestExposureSeconds, opportunity.expectedTraversalSeconds);
                    exposureValid |= opportunity.expectedTraversalSeconds >= 2.5f &&
                                     opportunity.expectedTraversalSeconds <= 6f &&
                                     opportunity.risk >= 0.45f;
                }
                else if (opportunity.kind == TacticalOpportunityKind.RecoveryPocket)
                {
                    report.recoveryOpportunityCount++;
                    valid &= opportunity.safeWindowSeconds >= 9.8f;
                    report.nearestRecoverySeconds = Mathf.Min(
                        report.nearestRecoverySeconds,
                        Vector3.Distance(plan.playerSpawn, opportunity.bounds.center) /
                        Mathf.Max(1f, settings.combatSpeed));
                }
                else if (opportunity.kind == TacticalOpportunityKind.KiteLoop)
                {
                    report.kiteLoopOpportunityCount++;
                    kiteValid |= opportunity.loopRadius >= settings.turnRadius * 1.10f &&
                                 opportunity.exits.Length >= 3;
                }
            }
            if (report.minimumTacticalChoices == int.MaxValue)
                report.minimumTacticalChoices = 0;
            if (float.IsPositiveInfinity(report.nearestRecoverySeconds))
                report.nearestRecoverySeconds = 0f;

            AirCombatFlightRoute masked = FindRoute(plan, "route.masked-flank");
            AirCombatFlightRoute exposure = FindRoute(plan, "route.long-range");
            float maskedLength = RouteLength(masked);
            float exposureLength = RouteLength(exposure);
            report.exposureShortcutSavingRatio = maskedLength > 0.01f
                ? 1f - exposureLength / maskedLength
                : 0f;
            bool shortcutSavingValid =
                report.exposureShortcutSavingRatio >= 0.20f &&
                report.exposureShortcutSavingRatio <= 0.35f;

            report.occlusionBoundaryTowerCount = CountCluster(plan, 1202);
            report.occlusionBreakCount = CountOcclusionSightBreaks(
                settings,
                plan);
            report.combatBoundaryTowerCount = CountCluster(plan, 1205);
            report.combatBoundaryAirWallCount = plan.boundaryWalls.Count;
            report.recoveryPocketOutputBlockedCount =
                CountRecoveryPocketsWithBlockedOutput(settings, plan);
            report.kiteLoopObstructionCount = CountCluster(plan, 1201);
            report.destructionAmbushFeatureCount = CountCluster(plan, 1203);
            report.coveredAttackPerchCount = 0;
            report.physicalAttackPerchCount = 0;
            TacticalOpportunity vertical = FindOpportunity(
                plan,
                TacticalOpportunityKind.VerticalEscape);
            report.verticalEscapePhysical = vertical != null &&
                vertical.physicalFeatureCount > 0 &&
                FindRoute(plan, vertical.runtimeBindingId) != null;
            bool destructionRequired = FindOpportunity(
                plan,
                TacticalOpportunityKind.DestructionAmbush) != null;
            float navigationChallenge = settings.Difficulty.navigationChallenge;
            int minimumOcclusionBreaks = Mathf.RoundToInt(Mathf.Lerp(
                4f,
                2f,
                navigationChallenge));
            int minimumOcclusionTowers = Mathf.RoundToInt(Mathf.Lerp(
                8f,
                6f,
                navigationChallenge));
            report.combatRegionsPhysical = shortcutSavingValid &&
                report.occlusionBreakCount >= minimumOcclusionBreaks &&
                report.occlusionBoundaryTowerCount >= minimumOcclusionTowers &&
                report.combatBoundaryTowerCount >= 16 &&
                BoundaryAirWallsAreValid(settings, plan) &&
                report.recoveryPocketOutputBlockedCount >= 2 &&
                report.kiteLoopObstructionCount >= 1 &&
                report.verticalEscapePhysical &&
                (!destructionRequired ||
                 report.destructionAmbushFeatureCount >= 2);
            report.dominantRouteDetected = firstUtility - secondUtility > 0.28f;
            report.tacticalOpportunityNetworkValid = valid && exposureValid && kiteValid &&
                report.tacticalOpportunityCount >= 6 &&
                report.recoveryOpportunityCount >= 2 &&
                report.minimumTacticalChoices >= 2;
        }

        static float RouteLength(AirCombatFlightRoute route)
        {
            if (route == null || route.points == null || route.points.Length < 2)
                return 0f;
            float length = 0f;
            for (int i = 1; i < route.points.Length; i++)
                length += Vector3.Distance(route.points[i - 1], route.points[i]);
            return length;
        }

        static void AddRecovery(
            AirCombatCitySettings settings,
            AirCombatCityPlan plan,
            string id,
            Bounds bounds,
            float safeWindow,
            bool west)
        {
            float side = west ? -1f : 1f;
            Vector3 center = bounds.center;
            Add(plan, id, TacticalOpportunityKind.RecoveryPocket, bounds,
                0.78f, Mathf.Lerp(0.18f, 0.38f, settings.Difficulty.combatPressure),
                settings.Difficulty.navigationChallenge * 0.42f,
                settings.Difficulty.routeLegibility, 2.4f, safeWindow, 0f,
                Points(center + new Vector3(-side * bounds.size.x * 0.62f, 0f,
                        -bounds.size.z * 0.24f),
                    center + new Vector3(0f, 0f, bounds.size.z * 0.62f)),
                Points(center + new Vector3(side * bounds.size.x * 0.62f, 0f,
                        bounds.size.z * 0.24f),
                    center + new Vector3(0f, 0f, -bounds.size.z * 0.62f)),
                west ? "opportunity.occlusion.west" : "opportunity.exposure.east",
                "opportunity.kite.center");
        }

        static Bounds Volume(AirCombatCityPlan plan, string id)
        {
            foreach (AirCombatTacticalVolume volume in plan.volumes)
            {
                if (volume.stableId == id)
                    return new Bounds(volume.center, volume.size);
            }
            throw new InvalidOperationException("Missing tactical volume: " + id);
        }

        static Vector3[] Points(params Vector3[] points)
        {
            return points ?? Array.Empty<Vector3>();
        }

        static void Add(
            AirCombatCityPlan plan,
            string id,
            TacticalOpportunityKind kind,
            Bounds bounds,
            float utility,
            float risk,
            float execution,
            float legibility,
            float traversal,
            float safeWindow,
            float loopRadius,
            Vector3[] entrances,
            Vector3[] exits,
            params string[] connections)
        {
            plan.opportunities.Add(new TacticalOpportunity
            {
                stableId = id,
                kind = kind,
                bounds = bounds,
                utility = Mathf.Clamp01(utility),
                risk = Mathf.Clamp01(risk),
                executionDifficulty = Mathf.Clamp01(execution),
                legibility = Mathf.Clamp01(legibility),
                expectedTraversalSeconds = Mathf.Max(0f, traversal),
                safeWindowSeconds = Mathf.Max(0f, safeWindow),
                loopRadius = Mathf.Max(0f, loopRadius),
                entrances = entrances ?? Array.Empty<Vector3>(),
                exits = exits ?? Array.Empty<Vector3>(),
                connectedOpportunityIds = connections ?? Array.Empty<string>()
            });
        }
    }
}
