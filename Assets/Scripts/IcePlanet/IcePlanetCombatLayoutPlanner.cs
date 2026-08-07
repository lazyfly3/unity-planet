using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityPlanet.IcePlanet
{
    public static class IcePlanetCombatLayoutPlanner
    {
        const string AbandonedMine = "abandoned_mine";
        const string WindCanyon = "wind_canyon";
        const string IndustrialOutpost = "industrial_outpost";

        public static IcePlanetCombatLayoutPlan Create(
            string missionId,
            int seed)
        {
            switch (missionId)
            {
                case WindCanyon:
                    return CreateWindCanyon(seed);
                case IndustrialOutpost:
                    return CreateIndustrialOutpost(seed);
                default:
                    return CreateAbandonedMine(seed);
            }
        }

        static IcePlanetCombatLayoutPlan CreateAbandonedMine(int seed)
        {
            var random = new System.Random(seed);
            var plan = new IcePlanetCombatLayoutPlan(
                AbandonedMine,
                seed,
                new Vector2(210f, 176f),
                150f);

            AddAnchor(plan, "approach", IcePlanetCombatAnchorRole.PlayerApproach,
                new Vector2(0f, -104f), 0f, 0);
            AddAnchor(plan, "objective_a", IcePlanetCombatAnchorRole.Objective,
                new Vector2(-42f, -8f), 30f, 0);
            AddAnchor(plan, "objective_b", IcePlanetCombatAnchorRole.Objective,
                new Vector2(37f, 18f), 210f, 1);
            AddAnchor(plan, "objective_c", IcePlanetCombatAnchorRole.Objective,
                new Vector2(0f, 54f), 180f, 2);
            AddAnchor(plan, "extraction", IcePlanetCombatAnchorRole.Extraction,
                new Vector2(0f, -96f), 180f, 0);

            AddEnemyRing(plan, random, new Vector2(-42f, -8f), 0, 4, 18f);
            AddEnemyRing(plan, random, new Vector2(37f, 18f), 1, 4, 20f);
            AddEnemyRing(plan, random, new Vector2(0f, 54f), 2, 5, 22f);

            int boundaryIndex = 0;
            for (int index = 0; index < 22; index++)
            {
                float angle = index * 360f / 22f;
                if (AngleDistance(angle, 270f) < 18f
                    || AngleDistance(angle, 25f) < 12f
                    || AngleDistance(angle, 155f) < 12f)
                {
                    continue;
                }
                float radians = angle * Mathf.Deg2Rad;
                Vector2 position = new Vector2(
                    Mathf.Cos(radians) * RandomRange(random, 91f, 99f),
                    Mathf.Sin(radians) * RandomRange(random, 73f, 81f));
                AddElement(plan, "boundary_" + boundaryIndex++,
                    IcePlanetCombatVisualRole.BoundaryCliff,
                    position, 90f - angle + RandomRange(random, -12f, 12f),
                    new Vector3(24f, 16f, 12f), true);
            }

            Vector2[] ruinPositions =
            {
                new Vector2(-24f, 11f),
                new Vector2(18f, -16f),
                new Vector2(-55f, 34f),
                new Vector2(51f, 49f),
                new Vector2(0f, 31f)
            };
            for (int index = 0; index < ruinPositions.Length; index++)
            {
                AddElement(plan, "ruin_" + index,
                    IcePlanetCombatVisualRole.Ruin,
                    ruinPositions[index] + Jitter(random, 3f),
                    RandomRange(random, 0f, 360f),
                    index == 4
                        ? new Vector3(15f, 13f, 8f)
                        : new Vector3(11f, 8f, 6f),
                    true);
            }

            List<Vector2> occupied = AnchorPositions(plan);
            AddScatteredElements(
                plan, random, occupied,
                IcePlanetCombatVisualRole.CoverRock,
                "cover", 22, 18f, 72f, 12f,
                new Vector3(8f, 4.5f, 6f), true);
            AddScatteredElements(
                plan, random, occupied,
                IcePlanetCombatVisualRole.IceCrystal,
                "crystal", 11, 34f, 79f, 8f,
                new Vector3(4f, 8f, 4f), false);
            AddObjectiveLights(plan, random);
            AddElement(plan, "mine_ice_a",
                IcePlanetCombatVisualRole.IcePatch,
                new Vector2(-13f, -42f), 12f,
                new Vector3(34f, 0.22f, 18f), true);
            AddElement(plan, "mine_ice_b",
                IcePlanetCombatVisualRole.IcePatch,
                new Vector2(28f, 51f), -18f,
                new Vector3(28f, 0.22f, 16f), true);
            AddSnowFx(plan, 0, new Vector2(0f, 6f));
            return plan;
        }

        static IcePlanetCombatLayoutPlan CreateWindCanyon(int seed)
        {
            var random = new System.Random(seed);
            var plan = new IcePlanetCombatLayoutPlan(
                WindCanyon,
                seed,
                new Vector2(154f, 236f),
                140f);

            AddAnchor(plan, "approach", IcePlanetCombatAnchorRole.PlayerApproach,
                new Vector2(0f, -130f), 0f, 0);
            AddAnchor(plan, "scan_a", IcePlanetCombatAnchorRole.Objective,
                RouteCenter(-66f), 0f, 0);
            AddAnchor(plan, "scan_b", IcePlanetCombatAnchorRole.Objective,
                RouteCenter(1f), 0f, 1);
            AddAnchor(plan, "scan_c", IcePlanetCombatAnchorRole.Objective,
                RouteCenter(72f), 0f, 2);
            AddAnchor(plan, "extraction", IcePlanetCombatAnchorRole.Extraction,
                RouteCenter(111f), 180f, 0);

            for (int segment = 0; segment < 13; segment++)
            {
                float z = -108f + segment * 18f;
                Vector2 center = RouteCenter(z);
                Vector2 next = RouteCenter(Mathf.Min(z + 4f, 112f));
                Vector2 tangent = (next - center).normalized;
                Vector2 right = new Vector2(tangent.y, -tangent.x);
                float tangentYaw = Mathf.Atan2(tangent.x, tangent.y)
                    * Mathf.Rad2Deg;
                float halfWidth = RandomRange(random, 31f, 40f);
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector2 position = center + right * halfWidth * side
                        + Jitter(random, 2.5f);
                    float yaw = tangentYaw
                        + RandomRange(random, -8f, 8f);
                    AddElement(plan,
                        "canyon_" + segment + "_" + (side < 0 ? "l" : "r"),
                        IcePlanetCombatVisualRole.BoundaryCliff,
                        position, yaw,
                        new Vector3(26f, 20f, 13f), true);
                }

                if (segment > 0 && segment < 12)
                {
                    Vector2 cover = center + right
                        * (segment % 2 == 0 ? 14f : -14f)
                        + Jitter(random, 2f);
                    AddElement(plan, "cover_" + segment,
                        IcePlanetCombatVisualRole.CoverRock,
                        cover, RandomRange(random, 0f, 360f),
                        new Vector3(8f, 4.5f, 6f), true);
                    AddAnchor(plan, "patrol_" + segment,
                        IcePlanetCombatAnchorRole.Patrol,
                        center + Jitter(random, 3f),
                        tangentYaw,
                        segment % 3);
                }
            }

            float[] bridgeZ = { -32f, 42f };
            for (int index = 0; index < bridgeZ.Length; index++)
            {
                Vector2 center = RouteCenter(bridgeZ[index]);
                AddElement(plan, "bridge_" + index,
                    IcePlanetCombatVisualRole.Bridge,
                    center, 90f,
                    new Vector3(28f, 5f, 9f), true);
                AddAnchor(plan, "vantage_" + index,
                    IcePlanetCombatAnchorRole.Vantage,
                    center + new Vector2(index == 0 ? 25f : -25f, 0f),
                    index == 0 ? 270f : 90f, index);
            }

            for (int group = 0; group < 3; group++)
            {
                float z = -51f + group * 68f;
                Vector2 center = RouteCenter(z + 16f);
                AddEnemyRing(plan, random, center, group, 4 + group, 15f);
            }
            AddObjectiveLights(plan, random);
            AddElement(plan, "crystal_landmark_a",
                IcePlanetCombatVisualRole.IceCrystal,
                RouteCenter(18f) + new Vector2(24f, 0f), 18f,
                new Vector3(7f, 15f, 7f), false);
            AddElement(plan, "crystal_landmark_b",
                IcePlanetCombatVisualRole.IceCrystal,
                RouteCenter(86f) + new Vector2(-23f, 0f), -12f,
                new Vector3(6f, 13f, 6f), false);
            AddElement(plan, "canyon_ice_a",
                IcePlanetCombatVisualRole.IcePatch,
                RouteCenter(-74f), -8f,
                new Vector3(23f, 0.22f, 42f), true);
            AddElement(plan, "canyon_ice_b",
                IcePlanetCombatVisualRole.IcePatch,
                RouteCenter(57f), 11f,
                new Vector3(25f, 0.22f, 46f), true);
            AddSnowFx(plan, 0, new Vector2(0f, -40f));
            AddSnowFx(plan, 1, new Vector2(0f, 58f));
            return plan;
        }

        static IcePlanetCombatLayoutPlan CreateIndustrialOutpost(int seed)
        {
            var random = new System.Random(seed);
            var plan = new IcePlanetCombatLayoutPlan(
                IndustrialOutpost,
                seed,
                new Vector2(196f, 170f),
                150f);

            AddAnchor(plan, "approach", IcePlanetCombatAnchorRole.PlayerApproach,
                new Vector2(0f, -103f), 0f, 0);
            AddAnchor(plan, "core_a", IcePlanetCombatAnchorRole.Objective,
                new Vector2(-45f, 11f), 0f, 0);
            AddAnchor(plan, "core_b", IcePlanetCombatAnchorRole.Objective,
                new Vector2(0f, 49f), 180f, 1);
            AddAnchor(plan, "core_c", IcePlanetCombatAnchorRole.Objective,
                new Vector2(45f, 11f), 0f, 2);
            AddAnchor(plan, "extraction", IcePlanetCombatAnchorRole.Extraction,
                new Vector2(0f, -94f), 180f, 0);

            AddOutpostPerimeter(plan, random);
            Vector2[] towerPositions =
            {
                new Vector2(-82f, -63f),
                new Vector2(82f, -63f),
                new Vector2(-82f, 65f),
                new Vector2(82f, 65f)
            };
            for (int index = 0; index < towerPositions.Length; index++)
            {
                AddElement(plan, "tower_" + index,
                    IcePlanetCombatVisualRole.Tower,
                    towerPositions[index], index * 90f,
                    new Vector3(12f, 18f, 12f), true);
                AddAnchor(plan, "vantage_" + index,
                    IcePlanetCombatAnchorRole.Vantage,
                    towerPositions[index] * 0.88f,
                    180f + index * 90f, index);
            }

            float[] laneX = { -44f, 0f, 44f };
            for (int lane = 0; lane < laneX.Length; lane++)
            {
                for (int row = 0; row < 4; row++)
                {
                    float z = -43f + row * 26f;
                    if (row == 2 && lane == 1)
                        continue;
                    Vector2 position = new Vector2(
                        laneX[lane] + RandomRange(random, -4f, 4f),
                        z + RandomRange(random, -3f, 3f));
                    IcePlanetCombatVisualRole role =
                        (row + lane) % 2 == 0
                            ? IcePlanetCombatVisualRole.Wall
                            : IcePlanetCombatVisualRole.CoverRock;
                    AddElement(plan, "lane_" + lane + "_" + row,
                        role, position,
                        role == IcePlanetCombatVisualRole.Wall
                            ? (lane == 1 ? 90f : 0f)
                            : RandomRange(random, 0f, 360f),
                        role == IcePlanetCombatVisualRole.Wall
                            ? new Vector3(13f, 6f, 3.5f)
                            : new Vector3(8f, 5f, 6f),
                        true);
                    AddAnchor(plan, "patrol_" + lane + "_" + row,
                        IcePlanetCombatAnchorRole.Patrol,
                        position + new Vector2(0f, 8f),
                        180f, lane);
                }
            }

            AddElement(plan, "command_arch",
                IcePlanetCombatVisualRole.Ruin,
                new Vector2(0f, 67f), 180f,
                new Vector3(18f, 14f, 8f), true);
            AddElement(plan, "gate_bridge",
                IcePlanetCombatVisualRole.Bridge,
                new Vector2(0f, -69f), 0f,
                new Vector3(20f, 5f, 9f), true);

            for (int group = 0; group < 3; group++)
            {
                Vector2 center = new Vector2(laneX[group], 25f);
                AddEnemyRing(plan, random, center, group, 5, 17f);
            }
            AddAnchor(plan, "reinforcement_left",
                IcePlanetCombatAnchorRole.EnemySpawn,
                new Vector2(-75f, 0f), 90f, 3);
            AddAnchor(plan, "reinforcement_right",
                IcePlanetCombatAnchorRole.EnemySpawn,
                new Vector2(75f, 0f), 270f, 3);

            AddObjectiveLights(plan, random);
            AddElement(plan, "outpost_ice_courtyard",
                IcePlanetCombatVisualRole.IcePatch,
                new Vector2(0f, 12f), 0f,
                new Vector3(58f, 0.22f, 34f), true);
            for (int index = 0; index < 6; index++)
            {
                float x = index % 2 == 0 ? -66f : 66f;
                float z = -43f + (index / 2) * 43f;
                AddElement(plan, "perimeter_light_" + index,
                    IcePlanetCombatVisualRole.Light,
                    new Vector2(x, z), x < 0f ? 90f : 270f,
                    new Vector3(2.5f, 9f, 2.5f), false);
            }
            AddSnowFx(plan, 0, new Vector2(0f, 0f));
            return plan;
        }

        static void AddOutpostPerimeter(
            IcePlanetCombatLayoutPlan plan,
            System.Random random)
        {
            int id = 0;
            for (float x = -72f; x <= 72f; x += 12f)
            {
                if (Mathf.Abs(x) > 13f)
                {
                    AddElement(plan, "south_wall_" + id++,
                        IcePlanetCombatVisualRole.Wall,
                        new Vector2(x, -65f),
                        RandomRange(random, -2f, 2f),
                        new Vector3(13f, 7f, 3.5f), true);
                }
                AddElement(plan, "north_wall_" + id++,
                    IcePlanetCombatVisualRole.Wall,
                    new Vector2(x, 67f),
                    180f + RandomRange(random, -2f, 2f),
                    new Vector3(13f, 7f, 3.5f), true);
            }
            for (float z = -53f; z <= 55f; z += 12f)
            {
                AddElement(plan, "west_wall_" + id++,
                    IcePlanetCombatVisualRole.Wall,
                    new Vector2(-84f, z), 90f,
                    new Vector3(13f, 7f, 3.5f), true);
                AddElement(plan, "east_wall_" + id++,
                    IcePlanetCombatVisualRole.Wall,
                    new Vector2(84f, z), 270f,
                    new Vector3(13f, 7f, 3.5f), true);
            }
        }

        static Vector2 RouteCenter(float z)
        {
            float normalized = Mathf.InverseLerp(-112f, 112f, z);
            float x = Mathf.Sin((normalized * 1.55f - 0.25f) * Mathf.PI)
                * 18f;
            return new Vector2(x, z);
        }

        static void AddEnemyRing(
            IcePlanetCombatLayoutPlan plan,
            System.Random random,
            Vector2 center,
            int group,
            int count,
            float radius)
        {
            for (int index = 0; index < count; index++)
            {
                float angle = index * 360f / count
                    + RandomRange(random, -10f, 10f);
                float radians = angle * Mathf.Deg2Rad;
                Vector2 position = center + new Vector2(
                    Mathf.Cos(radians),
                    Mathf.Sin(radians)) * RandomRange(
                        random, radius * 0.82f, radius * 1.12f);
                AddAnchor(plan,
                    "enemy_" + group + "_" + index,
                    IcePlanetCombatAnchorRole.EnemySpawn,
                    position,
                    angle + 180f,
                    group);
            }
        }

        static void AddObjectiveLights(
            IcePlanetCombatLayoutPlan plan,
            System.Random random)
        {
            int index = 0;
            foreach (IcePlanetCombatLayoutAnchor anchor in plan.Anchors)
            {
                if (anchor.role != IcePlanetCombatAnchorRole.Objective)
                    continue;
                Vector2 offset = new Vector2(
                    index % 2 == 0 ? -7f : 7f,
                    index == 1 ? -5f : 5f);
                AddElement(plan, "objective_light_" + index,
                    IcePlanetCombatVisualRole.Light,
                    anchor.position + offset,
                    RandomRange(random, 0f, 360f),
                    new Vector3(2.2f, 8f, 2.2f), false);
                index++;
            }
        }

        static void AddSnowFx(
            IcePlanetCombatLayoutPlan plan,
            int index,
            Vector2 position)
        {
            AddElement(plan, "snow_fx_" + index,
                IcePlanetCombatVisualRole.SnowFx,
                position, 0f, Vector3.one, false);
        }

        static void AddScatteredElements(
            IcePlanetCombatLayoutPlan plan,
            System.Random random,
            List<Vector2> occupied,
            IcePlanetCombatVisualRole role,
            string idPrefix,
            int count,
            float minimumRadius,
            float maximumRadius,
            float minimumSpacing,
            Vector3 targetSize,
            bool blocking)
        {
            int placed = 0;
            int attempts = count * 40;
            while (placed < count && attempts-- > 0)
            {
                float angle = RandomRange(random, 0f, Mathf.PI * 2f);
                float radius = Mathf.Sqrt(RandomRange(
                    random,
                    minimumRadius * minimumRadius,
                    maximumRadius * maximumRadius));
                Vector2 position = new Vector2(
                    Mathf.Cos(angle) * radius,
                    Mathf.Sin(angle) * radius);
                bool separated = true;
                for (int index = 0; index < occupied.Count; index++)
                {
                    if ((occupied[index] - position).sqrMagnitude
                        < minimumSpacing * minimumSpacing)
                    {
                        separated = false;
                        break;
                    }
                }
                if (!separated)
                    continue;
                occupied.Add(position);
                AddElement(plan, idPrefix + "_" + placed++,
                    role, position,
                    RandomRange(random, 0f, 360f),
                    targetSize * RandomRange(random, 0.82f, 1.18f),
                    blocking);
            }
        }

        static List<Vector2> AnchorPositions(IcePlanetCombatLayoutPlan plan)
        {
            var values = new List<Vector2>();
            foreach (IcePlanetCombatLayoutAnchor anchor in plan.Anchors)
            {
                if (anchor.role == IcePlanetCombatAnchorRole.PlayerApproach
                    || anchor.role == IcePlanetCombatAnchorRole.Objective)
                {
                    values.Add(anchor.position);
                }
            }
            return values;
        }

        static void AddElement(
            IcePlanetCombatLayoutPlan plan,
            string id,
            IcePlanetCombatVisualRole role,
            Vector2 position,
            float yaw,
            Vector3 targetSize,
            bool blocking)
        {
            plan.AddElement(new IcePlanetCombatLayoutElement(
                id, role, position, yaw, targetSize, blocking));
        }

        static void AddAnchor(
            IcePlanetCombatLayoutPlan plan,
            string id,
            IcePlanetCombatAnchorRole role,
            Vector2 position,
            float yaw,
            int group)
        {
            plan.AddAnchor(new IcePlanetCombatLayoutAnchor(
                id, role, position, yaw, group));
        }

        static Vector2 Jitter(System.Random random, float radius)
        {
            float angle = RandomRange(random, 0f, Mathf.PI * 2f);
            float distance = Mathf.Sqrt(RandomRange(random, 0f, 1f))
                * radius;
            return new Vector2(
                Mathf.Cos(angle) * distance,
                Mathf.Sin(angle) * distance);
        }

        static float RandomRange(
            System.Random random,
            float minimum,
            float maximum)
        {
            return Mathf.Lerp(
                minimum,
                maximum,
                (float)random.NextDouble());
        }

        static float AngleDistance(float first, float second)
        {
            return Mathf.Abs(Mathf.DeltaAngle(first, second));
        }
    }

    public static class IcePlanetCombatLayoutValidator
    {
        public static List<string> Validate(IcePlanetCombatLayoutPlan plan)
        {
            var errors = new List<string>();
            if (plan == null)
            {
                errors.Add("Layout plan is null.");
                return errors;
            }
            int playerApproaches = 0;
            int objectives = 0;
            int enemySpawns = 0;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            Vector2 half = plan.ArenaSize * 0.5f;
            for (int index = 0; index < plan.Anchors.Count; index++)
            {
                IcePlanetCombatLayoutAnchor anchor = plan.Anchors[index];
                if (!ids.Add("a:" + anchor.stableId))
                    errors.Add("Duplicate anchor id: " + anchor.stableId);
                if (anchor.role == IcePlanetCombatAnchorRole.PlayerApproach)
                    playerApproaches++;
                else if (anchor.role == IcePlanetCombatAnchorRole.Objective)
                    objectives++;
                else if (anchor.role == IcePlanetCombatAnchorRole.EnemySpawn)
                    enemySpawns++;
                if (Mathf.Abs(anchor.position.x) > half.x + 32f
                    || Mathf.Abs(anchor.position.y) > half.y + 32f)
                {
                    errors.Add("Anchor outside combat footprint: " + anchor.stableId);
                }
            }
            if (playerApproaches != 1)
                errors.Add("Layout must have exactly one player approach anchor.");
            if (objectives != 3)
                errors.Add("Layout must have exactly three objective anchors.");
            if (enemySpawns < 8)
                errors.Add("Layout must provide at least eight enemy spawn anchors.");

            int coverCount = 0;
            int boundaryCount = 0;
            for (int index = 0; index < plan.Elements.Count; index++)
            {
                IcePlanetCombatLayoutElement element = plan.Elements[index];
                if (!ids.Add("e:" + element.stableId))
                    errors.Add("Duplicate element id: " + element.stableId);
                if (element.role == IcePlanetCombatVisualRole.CoverRock
                    || element.role == IcePlanetCombatVisualRole.Wall
                    || element.role == IcePlanetCombatVisualRole.Ruin)
                {
                    coverCount++;
                }
                if (element.role == IcePlanetCombatVisualRole.BoundaryCliff
                    || element.role == IcePlanetCombatVisualRole.Wall)
                {
                    boundaryCount++;
                }
                if (Mathf.Abs(element.position.x) > half.x + 8f
                    || Mathf.Abs(element.position.y) > half.y + 8f)
                {
                    errors.Add("Decoration outside combat footprint: " + element.stableId);
                }
            }
            if (coverCount < 10)
                errors.Add("Layout must provide at least ten cover elements.");
            if (boundaryCount < 12)
                errors.Add("Layout must provide a readable combat boundary.");
            return errors;
        }
    }
}
