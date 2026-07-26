// The road-growth concepts in this file are adapted from the procedural city
// approaches demonstrated by Szuszi/CityGenerator-Unity and
// Bixio999/ProceduralCityGenerator. Both reference projects are MIT licensed.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityGeneration
{
    public sealed class CityGenerator
    {
        readonly struct RoadGrowth
        {
            public readonly Vector2 Start;
            public readonly Vector2 Direction;
            public readonly bool IsMajor;
            public readonly int RemainingSegments;
            public readonly int BranchGeneration;

            public RoadGrowth(
                Vector2 start,
                Vector2 direction,
                bool isMajor,
                int remainingSegments,
                int branchGeneration)
            {
                Start = start;
                Direction = direction.normalized;
                IsMajor = isMajor;
                RemainingSegments = remainingSegments;
                BranchGeneration = branchGeneration;
            }
        }

        public CityGenerationResult Generate(
            IReadOnlyList<Vector2> boundaryPoints,
            CityGenerationSettings settings,
            int seed)
        {
            return Generate(boundaryPoints, settings, seed, null);
        }

        public CityGenerationResult Generate(
            IReadOnlyList<Vector2> boundaryPoints,
            CityGenerationSettings settings,
            int seed,
            ICityTerrainSampler terrainSampler)
        {
            CityGenerationSettings safeSettings =
                settings == null ? new CityGenerationSettings() : settings.ValidatedCopy();

            if (!CityPolygonGeometry.ResolveBoundary(
                    boundaryPoints,
                    safeSettings,
                    out CityBoundaryResolution resolution,
                    out string error))
            {
                return CityGenerationResult.Failed(error);
            }

            var boundary = new List<Vector2>(resolution.SourcePoints);
            var regions = new List<List<Vector2>>(resolution.Regions);

            var result = new CityGenerationResult
            {
                IsSuccess = true,
                Error = string.Empty,
                Boundary = boundary,
                Regions = regions,
                BoundaryResolution = resolution
            };
            result.Diagnostics.BoundaryIntersectionCount =
                resolution.IntersectionCount;
            result.Diagnostics.IgnoredBoundaryRegionCount =
                resolution.IgnoredRegions == null
                    ? 0
                    : resolution.IgnoredRegions.Count;
            result.Diagnostics.BoundaryWasAutoRepaired =
                resolution.WasAutoRepaired;

            var productiveRegions = new List<List<Vector2>>();
            var random = new System.Random(seed);
            CityModularGridBasis modularBasis =
                CityModularRoadPlanner.CreateGlobalBasis(regions);
            result.ModularRoadAxis = modularBasis.AxisX;
            for (int regionIndex = 0; regionIndex < regions.Count; regionIndex++)
            {
                int firstRoad = result.Roads.Count;
                int firstBlock = result.Blocks.Count;
                int firstLot = result.Lots.Count;
                int firstBuilding = result.Buildings.Count;
                int firstRoadModule = result.RoadModules.Count;
                int firstPathwayModule = result.PathwayModules.Count;
                GenerateRegion(
                    regions[regionIndex],
                    regionIndex,
                    seed,
                    safeSettings,
                    random,
                    result,
                    terrainSampler,
                    modularBasis);
                if (result.Roads.Count > firstRoad
                    && result.Blocks.Count > firstBlock
                    && result.Buildings.Count > firstBuilding)
                {
                    productiveRegions.Add(regions[regionIndex]);
                    continue;
                }

                RemoveTail(result.Roads, firstRoad);
                RemoveTail(result.Blocks, firstBlock);
                RemoveTail(result.Lots, firstLot);
                RemoveTail(result.Buildings, firstBuilding);
                RemoveTail(result.RoadModules, firstRoadModule);
                RemoveTail(result.PathwayModules, firstPathwayModule);
            }
            result.Regions = productiveRegions;

            if (result.Roads.Count == 0
                || result.Blocks.Count == 0
                || result.Buildings.Count == 0)
            {
                return CityGenerationResult.Failed(
                    "当前范围太窄，无法容纳道路、街区和建筑。");
            }

            if (safeSettings.useModularRoadLayout)
            {
                CityRoadLayoutValidationResult roadValidation =
                    CityModularRoadPlanner.ValidateLayout(
                        result.RoadModules);
                if (!roadValidation.IsValid)
                {
                    return CityGenerationResult.Failed(
                        "Modular road validation failed: "
                        + roadValidation.Error);
                }
                roadValidation.RepairCount =
                    result.Diagnostics.RoadTopologyRepairCount;
                result.RoadLayoutValidation = roadValidation;
                result.Diagnostics.RoadConnectedComponentCount =
                    roadValidation.ConnectedComponentCount;
            }

            return result;
        }

        public CityGenerationResult Generate(
            CityBoundaryResolution resolution,
            CityGenerationSettings settings,
            int seed)
        {
            if (resolution == null)
                return CityGenerationResult.Failed("没有可用的边界解析结果。");
            return Generate(
                resolution.SourcePoints,
                settings,
                seed,
                null);
        }

        static void RemoveTail<T>(List<T> values, int first)
        {
            if (first < values.Count)
                values.RemoveRange(first, values.Count - first);
        }

        static void GenerateRegion(
            IReadOnlyList<Vector2> region,
            int regionIndex,
            int seed,
            CityGenerationSettings settings,
            System.Random random,
            CityGenerationResult result,
            ICityTerrainSampler terrainSampler,
            CityModularGridBasis modularBasis)
        {
            int firstRoad = result.Roads.Count;
            int firstBlock = result.Blocks.Count;
            int firstLot = result.Lots.Count;
            int firstBuilding = result.Buildings.Count;

            var regionRoads = new List<CityRoadSegment>();
            if (settings.useModularRoadLayout)
            {
                CityModularRoadPlanner.GenerateRoads(
                    region,
                    regionIndex,
                    modularBasis,
                    settings,
                    random,
                    result,
                    regionRoads);
            }
            else
            {
                GenerateRoadNetwork(
                    region,
                    settings,
                    random,
                    result,
                    regionRoads,
                    terrainSampler);
            }
            GenerateRoadsideDevelopment(
                region,
                settings,
                random,
                regionRoads,
                result);

            if (result.Blocks.Count == firstBlock)
            {
                TryAddIsolatedBlock(
                    region,
                    settings,
                    random,
                    regionRoads,
                    result,
                    true);
            }

            if (result.Buildings.Count == firstBuilding)
            {
                for (int i = firstLot; i < result.Lots.Count; i++)
                {
                    if (TryForceBuilding(
                            region,
                            result.Lots[i].Footprint,
                            settings,
                            random,
                            result))
                    {
                        break;
                    }
                }
            }

            if (settings.useModularRoadLayout)
            {
                CityModularRoadPlanner
                    .RemoveBuildingsOverlappingRoadModules(
                        result,
                        regionIndex,
                        firstBuilding,
                        modularBasis,
                        settings.modularRoadCellSize);
            }

            if (!settings.useModularRoadLayout
                && result.Roads.Count == firstRoad)
            {
                Vector2 center = FindInteriorPoint(region, random);
                Vector2 direction = FindPrimaryDirection(region);
                TryAddRoad(
                    region,
                    center - direction * settings.roadSegmentLength * 0.45f,
                    center + direction * settings.roadSegmentLength * 0.45f,
                    settings.majorRoadWidth,
                    true,
                    settings,
                    regionRoads,
                    result,
                    terrainSampler,
                    false,
                    out _);
            }

            if (terrainSampler != null
                && !settings.useModularRoadLayout
                && result.Diagnostics.WaterRoadCount == 0)
            {
                TryAddWaterAccessRoad(
                    region,
                    settings,
                    regionRoads,
                    result,
                    terrainSampler,
                    firstBuilding);
            }

            if (settings.useModularRoadLayout
                && result.Roads.Count > firstRoad
                && result.Blocks.Count > firstBlock)
            {
                CityModularRoadPlanner.GeneratePathways(
                    region,
                    regionIndex,
                    modularBasis,
                    settings,
                    result,
                    firstBlock,
                    firstBuilding,
                    seed);
            }
        }

        static void TryAddWaterAccessRoad(
            IReadOnlyList<Vector2> region,
            CityGenerationSettings settings,
            List<CityRoadSegment> regionRoads,
            CityGenerationResult result,
            ICityTerrainSampler terrainSampler,
            int firstBuilding)
        {
            Vector2 waterPoint = Vector2.zero;
            bool foundWater = false;
            for (int i = firstBuilding; i < result.Buildings.Count; i++)
            {
                Vector2 center = Average(result.Buildings[i].Footprint);
                if (!CityPolygonGeometry.ContainsPoint(region, center)
                    || !terrainSampler.SampleTerrain(center).IsWater)
                {
                    continue;
                }
                waterPoint = center;
                foundWater = true;
                break;
            }

            if (!foundWater)
            {
                float minimumX = float.MaxValue;
                float maximumX = float.MinValue;
                float minimumY = float.MaxValue;
                float maximumY = float.MinValue;
                for (int i = 0; i < region.Count; i++)
                {
                    minimumX = Mathf.Min(minimumX, region[i].x);
                    maximumX = Mathf.Max(maximumX, region[i].x);
                    minimumY = Mathf.Min(minimumY, region[i].y);
                    maximumY = Mathf.Max(maximumY, region[i].y);
                }

                const int samples = 12;
                for (int y = 1; y < samples && !foundWater; y++)
                {
                    for (int x = 1; x < samples; x++)
                    {
                        Vector2 candidate = new Vector2(
                            Mathf.Lerp(minimumX, maximumX, x / (float)samples),
                            Mathf.Lerp(minimumY, maximumY, y / (float)samples));
                        if (!CityPolygonGeometry.ContainsPoint(region, candidate)
                            || !terrainSampler.SampleTerrain(candidate).IsWater)
                        {
                            continue;
                        }
                        waterPoint = candidate;
                        foundWater = true;
                        break;
                    }
                }
            }

            if (!foundWater)
                return;

            Vector2 primary = FindPrimaryDirection(region);
            float halfLength = settings.roadSegmentLength * 0.55f;
            float[] turns = { 0f, 90f, 45f, -45f, 22.5f, -22.5f };
            for (int i = 0;
                 i < turns.Length && result.Diagnostics.WaterRoadCount == 0;
                 i++)
            {
                Vector2 direction = Rotate(primary, turns[i]).normalized;
                TryAddRoad(
                    region,
                    waterPoint - direction * halfLength,
                    waterPoint + direction * halfLength,
                    settings.minorRoadWidth,
                    false,
                    settings,
                    regionRoads,
                    result,
                    terrainSampler,
                    false,
                    out _);
            }

            // A nearby existing road may cause every normal growth attempt to
            // snap back onto land. Add one short, boundary-validated boardwalk
            // directly through the detected water cell as the deterministic
            // fallback; its runtime mesh receives the same deck and piles as
            // any organically grown water road.
            for (int i = 0;
                 i < turns.Length && result.Diagnostics.WaterRoadCount == 0;
                 i++)
            {
                Vector2 direction = Rotate(primary, turns[i]).normalized;
                Vector2 start = waterPoint - direction * halfLength;
                Vector2 end = waterPoint + direction * halfLength;
                var road = new CityRoadSegment(
                    start,
                    end,
                    settings.minorRoadWidth,
                    false);
                if (!CityPolygonGeometry.ContainsPolygon(
                        region,
                        road.GetCorners()))
                {
                    continue;
                }

                EvaluateTerrainProfile(
                    start,
                    end,
                    terrainSampler,
                    out float grade,
                    out bool crossesWater);
                if (!crossesWater)
                    continue;

                road = new CityRoadSegment(
                    start,
                    end,
                    settings.minorRoadWidth,
                    false,
                    Mathf.Min(grade, settings.maximumRoadGrade),
                    true,
                    false);
                regionRoads.Add(road);
                result.Roads.Add(road);
                result.Diagnostics.WaterRoadCount++;
                result.Diagnostics.MaximumRoadGrade = Mathf.Max(
                    result.Diagnostics.MaximumRoadGrade,
                    road.TerrainGrade);
            }
        }

        static void GenerateRoadNetwork(
            IReadOnlyList<Vector2> region,
            CityGenerationSettings settings,
            System.Random random,
            CityGenerationResult result,
            List<CityRoadSegment> regionRoads,
            ICityTerrainSampler terrainSampler)
        {
            Vector2 center = FindInteriorPoint(region, random);
            Vector2 primary = FindPrimaryDirection(region);
            float crossAngle = RandomRange(random, 62f, 108f);
            Vector2 secondary = Rotate(primary, crossAngle);

            GetBounds(region, out Vector2 minimum, out Vector2 maximum);
            float diagonal = Vector2.Distance(minimum, maximum);
            int majorLength = Mathf.Max(
                6,
                Mathf.CeilToInt(diagonal / settings.roadSegmentLength));

            var queue = new Queue<RoadGrowth>();
            queue.Enqueue(new RoadGrowth(center, primary, true, majorLength, 0));
            queue.Enqueue(new RoadGrowth(center, -primary, true, majorLength, 0));
            queue.Enqueue(new RoadGrowth(center, secondary, true, majorLength, 0));
            queue.Enqueue(new RoadGrowth(center, -secondary, true, majorLength, 0));

            int majorCount = 0;
            int minorCount = 0;
            int guard = 0;
            int guardLimit =
                settings.maxMajorRoadSegments + settings.maxMinorRoadSegments + 512;

            while (queue.Count > 0 && guard++ < guardLimit)
            {
                RoadGrowth growth = queue.Dequeue();
                if (growth.RemainingSegments <= 0)
                    continue;
                if (growth.IsMajor && majorCount >= settings.maxMajorRoadSegments)
                    continue;
                if (!growth.IsMajor && minorCount >= settings.maxMinorRoadSegments)
                    continue;

                float curvatureDegrees = growth.IsMajor
                    ? Mathf.Lerp(1.5f, 10f, settings.roadCurvature)
                    : Mathf.Lerp(4f, 24f, settings.roadCurvature);
                float segmentLength = settings.roadSegmentLength
                    * (growth.IsMajor ? 1.18f : 1f)
                    * RandomRange(random, 0.82f, 1.18f);

                bool accepted = false;
                bool terminated = false;
                CityRoadSegment acceptedRoad = null;
                Vector2 acceptedDirection = growth.Direction;

                for (int attempt = 0; attempt < 9 && !accepted; attempt++)
                {
                    Vector2 direction = ChooseTerrainAwareDirection(
                        growth.Start,
                        growth.Direction,
                        curvatureDegrees,
                        attempt,
                        random,
                        terrainSampler);
                    bool switchback = Vector2.Angle(
                        growth.Direction,
                        direction) >= 70f;
                    Vector2 end = growth.Start + direction * segmentLength;

                    accepted = TryAddRoad(
                        region,
                        growth.Start,
                        end,
                        growth.IsMajor
                            ? settings.majorRoadWidth
                            : settings.minorRoadWidth,
                        growth.IsMajor,
                        settings,
                        regionRoads,
                        result,
                        terrainSampler,
                        switchback,
                        out terminated);
                    if (accepted)
                    {
                        acceptedRoad = regionRoads[regionRoads.Count - 1];
                        acceptedDirection =
                            (acceptedRoad.End - acceptedRoad.Start).normalized;
                    }
                }

                if (!accepted)
                    continue;

                if (growth.IsMajor)
                    majorCount++;
                else
                    minorCount++;

                if (!terminated && growth.RemainingSegments > 1)
                {
                    queue.Enqueue(new RoadGrowth(
                        acceptedRoad.End,
                        acceptedDirection,
                        growth.IsMajor,
                        growth.RemainingSegments - 1,
                        growth.BranchGeneration));
                }

                if (growth.IsMajor)
                {
                    TryQueueBranch(1f);
                    TryQueueBranch(-1f);
                }
                else if (growth.BranchGeneration < 2
                         && random.NextDouble()
                         < settings.minorRoadDensity * 0.16f)
                {
                    TryQueueBranch(
                        random.NextDouble() < 0.5d ? 1f : -1f);
                }

                void TryQueueBranch(float side)
                {
                    float chance = growth.IsMajor
                        ? settings.minorRoadDensity * 0.42f
                        : settings.minorRoadDensity * 0.16f;
                    if (random.NextDouble() > chance)
                        return;

                    int branchLength = random.Next(
                        settings.minimumBranchSegments,
                        settings.maximumBranchSegments + 1);
                    float branchAngle =
                        side * RandomRange(random, 68f, 112f);
                    queue.Enqueue(new RoadGrowth(
                        acceptedRoad.End,
                        Rotate(acceptedDirection, branchAngle),
                        false,
                        branchLength,
                        growth.BranchGeneration + 1));
                }
            }
        }

        static bool TryAddRoad(
            IReadOnlyList<Vector2> region,
            Vector2 start,
            Vector2 proposedEnd,
            float width,
            bool isMajor,
            CityGenerationSettings settings,
            List<CityRoadSegment> regionRoads,
            CityGenerationResult result,
            ICityTerrainSampler terrainSampler,
            bool isSwitchback,
            out bool terminated)
        {
            terminated = false;
            Vector2 end = proposedEnd;
            Vector2 proposedDirection = (proposedEnd - start).normalized;
            float proposedLength = Vector2.Distance(start, proposedEnd);
            if (proposedLength < settings.roadSegmentLength * 0.3f)
                return false;

            float nearestIntersection = float.MaxValue;
            Vector2 intersectionPoint = end;
            for (int i = 0; i < regionRoads.Count; i++)
            {
                CityRoadSegment other = regionRoads[i];
                if (!TryGetSegmentIntersection(
                        start,
                        proposedEnd,
                        other.Start,
                        other.End,
                        out float t,
                        out _)
                    || t <= 0.08f
                    || t >= nearestIntersection)
                {
                    continue;
                }

                float angle = Mathf.Abs(Vector2.SignedAngle(
                    proposedDirection,
                    (other.End - other.Start).normalized));
                angle = Mathf.Min(angle, 180f - angle);
                if (angle < 14f)
                    return false;

                nearestIntersection = t;
                intersectionPoint = Vector2.Lerp(start, proposedEnd, t);
            }

            if (nearestIntersection < float.MaxValue)
            {
                end = intersectionPoint;
                terminated = true;
            }
            else
            {
                float snapRadius = settings.roadSegmentLength * 0.58f;
                float bestDistance = snapRadius;
                bool snapped = false;
                for (int i = 0; i < regionRoads.Count; i++)
                {
                    CityRoadSegment other = regionRoads[i];
                    TrySnap(other.Start);
                    TrySnap(other.End);
                }
                if (snapped)
                    terminated = true;

                void TrySnap(Vector2 candidate)
                {
                    Vector2 offset = candidate - start;
                    float distance = Vector2.Distance(candidate, proposedEnd);
                    if (distance >= bestDistance
                        || Vector2.Dot(offset.normalized, proposedDirection) < 0.55f)
                    {
                        return;
                    }

                    bestDistance = distance;
                    end = candidate;
                    snapped = true;
                }
            }

            float finalLength = Vector2.Distance(start, end);
            if (finalLength < settings.roadSegmentLength * 0.32f)
                return false;

            var candidateRoad = new CityRoadSegment(start, end, width, isMajor);
            if (!CityPolygonGeometry.ContainsPolygon(
                    region,
                    candidateRoad.GetCorners()))
            {
                return false;
            }

            Vector2 midpoint = (start + end) * 0.5f;
            Vector2 direction = (end - start).normalized;
            for (int i = 0; i < regionRoads.Count; i++)
            {
                CityRoadSegment other = regionRoads[i];
                if (Vector2.Distance(start, other.Start) < 0.05f
                    || Vector2.Distance(start, other.End) < 0.05f)
                {
                    continue;
                }

                float angle = Mathf.Abs(Vector2.SignedAngle(
                    direction,
                    (other.End - other.Start).normalized));
                angle = Mathf.Min(angle, 180f - angle);
                float clearance = (width + other.Width) * 0.52f;
                if (angle < 18f
                    && CityPolygonGeometry.DistancePointToSegment(
                        midpoint,
                        other.Start,
                        other.End) < clearance)
                {
                    return false;
                }
            }

            EvaluateTerrainProfile(
                start,
                end,
                terrainSampler,
                out float terrainGrade,
                out bool crossesWater);
            if (!crossesWater
                && terrainSampler != null
                && terrainGrade > settings.maximumRoadGrade * 1.05f)
            {
                return false;
            }

            float recordedGrade = crossesWater
                ? Mathf.Min(terrainGrade, settings.maximumRoadGrade)
                : terrainGrade;
            candidateRoad = new CityRoadSegment(
                start,
                end,
                width,
                isMajor,
                recordedGrade,
                crossesWater,
                isSwitchback);
            regionRoads.Add(candidateRoad);
            result.Roads.Add(candidateRoad);
            result.Diagnostics.MaximumRoadGrade = Mathf.Max(
                result.Diagnostics.MaximumRoadGrade,
                recordedGrade);
            if (crossesWater)
                result.Diagnostics.WaterRoadCount++;
            if (isSwitchback)
                result.Diagnostics.SwitchbackRoadCount++;
            return true;
        }

        static Vector2 ChooseTerrainAwareDirection(
            Vector2 start,
            Vector2 desiredDirection,
            float curvatureDegrees,
            int attempt,
            System.Random random,
            ICityTerrainSampler terrainSampler)
        {
            Vector2 desired = desiredDirection.normalized;
            if (terrainSampler == null)
            {
                float flatTurn = attempt == 0
                    ? RandomRange(random, -curvatureDegrees, curvatureDegrees)
                    : (attempt % 2 == 0 ? 1f : -1f)
                      * curvatureDegrees * (0.55f + attempt * 0.22f);
                return Rotate(desired, flatTurn).normalized;
            }

            CityTerrainSample sample = terrainSampler.SampleTerrain(start);
            Vector2 gradient = new Vector2(-sample.Normal.x, -sample.Normal.z);
            if (gradient.sqrMagnitude < 0.0001f)
            {
                float gentleTurn = attempt == 0
                    ? RandomRange(random, -curvatureDegrees, curvatureDegrees)
                    : (attempt % 2 == 0 ? 1f : -1f)
                      * curvatureDegrees * (0.55f + attempt * 0.2f);
                return Rotate(desired, gentleTurn).normalized;
            }

            gradient.Normalize();
            Vector2 contour = new Vector2(-gradient.y, gradient.x);
            if (Vector2.Dot(contour, desired) < 0f)
                contour = -contour;

            switch (attempt)
            {
                case 0:
                    return Rotate(
                        desired,
                        RandomRange(random, -curvatureDegrees, curvatureDegrees))
                        .normalized;
                case 1:
                    return contour;
                case 2:
                    return -contour;
                case 3:
                    return Vector2.Lerp(desired, contour, 0.72f).normalized;
                case 4:
                    return Vector2.Lerp(desired, -contour, 0.72f).normalized;
                case 5:
                    return Rotate(contour, 18f).normalized;
                case 6:
                    return Rotate(contour, -18f).normalized;
                case 7:
                    return Rotate(desired, 132f).normalized;
                default:
                    return Rotate(desired, -132f).normalized;
            }
        }

        static void EvaluateTerrainProfile(
            Vector2 start,
            Vector2 end,
            ICityTerrainSampler terrainSampler,
            out float maximumGrade,
            out bool crossesWater)
        {
            maximumGrade = 0f;
            crossesWater = false;
            if (terrainSampler == null)
                return;

            const int samples = 7;
            CityTerrainSample previous = terrainSampler.SampleTerrain(start);
            crossesWater = previous.IsWater;
            float stepLength = Vector2.Distance(start, end) / (samples - 1f);
            for (int i = 1; i < samples; i++)
            {
                CityTerrainSample current = terrainSampler.SampleTerrain(
                    Vector2.Lerp(start, end, i / (samples - 1f)));
                crossesWater |= current.IsWater;
                if (!previous.IsWater && !current.IsWater)
                {
                    maximumGrade = Mathf.Max(
                        maximumGrade,
                        Mathf.Abs(current.GroundHeight - previous.GroundHeight)
                        / Mathf.Max(0.01f, stepLength));
                }
                previous = current;
            }
        }

        static void GenerateRoadsideDevelopment(
            IReadOnlyList<Vector2> region,
            CityGenerationSettings settings,
            System.Random random,
            IReadOnlyList<CityRoadSegment> regionRoads,
            CityGenerationResult result)
        {
            int firstRegionBuilding = result.Buildings.Count;
            int stride = Mathf.Max(
                2,
                Mathf.RoundToInt(
                    settings.blockSpacing / settings.roadSegmentLength * 0.72f));

            for (int roadIndex = 0; roadIndex < regionRoads.Count; roadIndex += stride)
            {
                CityRoadSegment road = regionRoads[roadIndex];
                for (int side = -1; side <= 1; side += 2)
                {
                    if (random.NextDouble() > settings.blockDensity)
                        continue;

                    TryPlaceRoadsideBlock(
                        region,
                        road,
                        side,
                        settings,
                        random,
                        regionRoads,
                        result,
                        1f,
                        false);
                }
            }

            if (regionRoads.Count == 0)
                return;

            float regionArea = Mathf.Abs(CityPolygonGeometry.SignedArea(region));
            float densityMultiplier = Mathf.Lerp(
                0.55f,
                1.05f,
                settings.buildingDensity);
            int targetBuildings = Mathf.Clamp(
                Mathf.RoundToInt(
                    regionArea / settings.targetLotArea * densityMultiplier),
                1,
                180);
            int maximumAttempts = Mathf.Max(96, targetBuildings * 36);

            // The first pass preserves occasional open lots. This second,
            // area-budgeted pass fills undersupplied regions and retries with
            // smaller footprints instead of letting failed rectangle tests
            // silently determine city density.
            for (int attempt = 0;
                 attempt < maximumAttempts
                 && result.Buildings.Count - firstRegionBuilding < targetBuildings;
                 attempt++)
            {
                CityRoadSegment road = regionRoads[random.Next(0, regionRoads.Count)];
                int side = random.NextDouble() < 0.5d ? -1 : 1;
                float progress = attempt / (float)Mathf.Max(1, maximumAttempts - 1);
                float sizeScale = Mathf.Lerp(
                    0.86f,
                    0.56f,
                    progress) * RandomRange(random, 0.9f, 1.08f);

                TryPlaceRoadsideBlock(
                    region,
                    road,
                    side,
                    settings,
                    random,
                    regionRoads,
                    result,
                    sizeScale,
                    true);
            }

            int isolatedAttempts = Mathf.Max(8, targetBuildings / 2);
            for (int attempt = 0;
                 attempt < isolatedAttempts
                 && result.Buildings.Count - firstRegionBuilding < targetBuildings;
                 attempt++)
            {
                TryAddIsolatedBlock(
                    region,
                    settings,
                    random,
                    regionRoads,
                    result,
                    true);
            }
        }

        static bool TryPlaceRoadsideBlock(
            IReadOnlyList<Vector2> region,
            CityRoadSegment road,
            int side,
            CityGenerationSettings settings,
            System.Random random,
            IReadOnlyList<CityRoadSegment> regionRoads,
            CityGenerationResult result,
            float sizeScale,
            bool forceBuilding)
        {
            Vector2 roadDirection = (road.End - road.Start).normalized;
            Vector2 roadNormal = new Vector2(-roadDirection.y, roadDirection.x);
            float frontage = settings.blockSpacing
                * RandomRange(random, 0.52f, 0.86f)
                * sizeScale;
            float depth = settings.blockSpacing
                * RandomRange(random, 0.46f, 0.78f)
                * sizeScale;
            if (frontage < settings.minimumLotFrontage
                || depth < settings.minimumLotFrontage)
                return false;

            Vector2 center = (road.Start + road.End) * 0.5f;
            center += roadDirection
                * RandomRange(
                    random,
                    -settings.roadSegmentLength * 0.38f,
                    settings.roadSegmentLength * 0.38f);
            center += roadNormal
                * side
                * (road.Width * 0.5f
                   + settings.sidewalkWidth
                   + depth * 0.5f
                   + RandomRange(random, 0.6f, 2.6f));

            Vector2 blockAxis = Rotate(
                roadDirection,
                RandomRange(
                    random,
                    -settings.blockIrregularity * 35f,
                    settings.blockIrregularity * 35f));
            List<Vector2> block = MakeIrregularQuad(
                center,
                blockAxis,
                frontage,
                depth,
                settings.blockIrregularity,
                random);

            if (!CityPolygonGeometry.ContainsPolygon(region, block)
                || OverlapsRoads(block, regionRoads)
                || OverlapsBlocks(block, result.Blocks))
            {
                return false;
            }

            result.Blocks.Add(new CityBlockData(block));
            AddLotsAndBuildings(
                region,
                block,
                settings,
                random,
                result,
                forceBuilding);
            return true;
        }

        static void AddLotsAndBuildings(
            IReadOnlyList<Vector2> region,
            IReadOnlyList<Vector2> block,
            CityGenerationSettings settings,
            System.Random random,
            CityGenerationResult result,
            bool forceBuilding)
        {
            float frontage = Vector2.Distance(block[0], block[1]);
            int splitCount = frontage > 24f && random.NextDouble() < 0.72d ? 2 : 1;
            bool buildingForced = false;

            for (int split = 0; split < splitCount; split++)
            {
                float t0 = split / (float)splitCount;
                float t1 = (split + 1) / (float)splitCount;
                float normalizedGap = settings.lotGap
                    / Mathf.Max(2f, frontage)
                    * 0.5f;
                if (split > 0)
                    t0 += normalizedGap;
                if (split + 1 < splitCount)
                    t1 -= normalizedGap;

                var lot = new List<Vector2>
                {
                    Vector2.Lerp(block[0], block[1], t0),
                    Vector2.Lerp(block[0], block[1], t1),
                    Vector2.Lerp(block[3], block[2], t1),
                    Vector2.Lerp(block[3], block[2], t0)
                };
                if (!CityPolygonGeometry.ContainsPolygon(region, lot))
                    continue;

                result.Lots.Add(new CityLotData(lot));
                bool shouldBuild = forceBuilding && !buildingForced
                    || random.NextDouble() <= settings.buildingDensity;
                if (!shouldBuild)
                    continue;

                if (TryAddBuilding(
                        region,
                        lot,
                        settings,
                        random,
                        result))
                {
                    buildingForced = true;
                }
            }
        }

        static bool TryAddBuilding(
            IReadOnlyList<Vector2> region,
            IReadOnlyList<Vector2> lot,
            CityGenerationSettings settings,
            System.Random random,
            CityGenerationResult result)
        {
            float shortestEdge = float.MaxValue;
            for (int i = 0; i < lot.Count; i++)
            {
                shortestEdge = Mathf.Min(
                    shortestEdge,
                    Vector2.Distance(lot[i], lot[(i + 1) % lot.Count]));
            }

            if (shortestEdge <= settings.buildingSetback * 2f + 2f)
                return false;

            float scale = Mathf.Clamp(
                1f - settings.buildingSetback * 2f / shortestEdge,
                0.56f,
                0.88f);
            List<Vector2> building = ScalePolygon(lot, scale);

            if (random.NextDouble() < settings.buildingChamferChance)
            {
                building = ChamferCorner(
                    building,
                    random.Next(0, building.Count),
                    RandomRange(random, 0.16f, 0.32f));
            }

            if (!CityPolygonGeometry.ContainsPolygon(region, building))
                return false;

            Vector2 regionCenter = CityPolygonGeometry.Centroid(region);
            Vector2 buildingCenter = Average(building);
            float coreInfluence = 1f - Mathf.Clamp01(
                Vector2.Distance(regionCenter, buildingCenter)
                / (settings.blockSpacing * 5f));
            float heightFactor = Mathf.Clamp01(
                0.12f
                + coreInfluence * 0.48f
                + (float)random.NextDouble() * 0.52f);
            float height = Mathf.Lerp(
                settings.minimumBuildingHeight,
                settings.maximumBuildingHeight,
                heightFactor);

            result.Buildings.Add(new CityBuildingData(
                building,
                height,
                random.Next(0, 4),
                random.Next(0, 12)));
            return true;
        }

        static bool TryForceBuilding(
            IReadOnlyList<Vector2> region,
            IReadOnlyList<Vector2> lot,
            CityGenerationSettings settings,
            System.Random random,
            CityGenerationResult result)
        {
            return TryAddBuilding(region, lot, settings, random, result);
        }

        static bool TryAddIsolatedBlock(
            IReadOnlyList<Vector2> region,
            CityGenerationSettings settings,
            System.Random random,
            IReadOnlyList<CityRoadSegment> regionRoads,
            CityGenerationResult result,
            bool forceBuilding)
        {
            GetBounds(region, out Vector2 minimum, out Vector2 maximum);
            for (int attempt = 0; attempt < 96; attempt++)
            {
                Vector2 center = new Vector2(
                    RandomRange(random, minimum.x, maximum.x),
                    RandomRange(random, minimum.y, maximum.y));
                if (!CityPolygonGeometry.ContainsPoint(region, center))
                    continue;

                float size = Mathf.Lerp(
                    9f,
                    Mathf.Min(18f, settings.blockSpacing * 0.58f),
                    (float)random.NextDouble());
                Vector2 direction = Rotate(
                    FindPrimaryDirection(region),
                    RandomRange(random, -35f, 35f));
                List<Vector2> block = MakeIrregularQuad(
                    center,
                    direction,
                    size * RandomRange(random, 0.85f, 1.25f),
                    size,
                    settings.blockIrregularity,
                    random);
                if (!CityPolygonGeometry.ContainsPolygon(region, block)
                    || OverlapsRoads(block, regionRoads)
                    || OverlapsBlocks(block, result.Blocks))
                {
                    continue;
                }

                result.Blocks.Add(new CityBlockData(block));
                AddLotsAndBuildings(
                    region,
                    block,
                    settings,
                    random,
                    result,
                    forceBuilding);
                return true;
            }

            return false;
        }

        static List<Vector2> MakeIrregularQuad(
            Vector2 center,
            Vector2 axis,
            float width,
            float depth,
            float irregularity,
            System.Random random)
        {
            axis.Normalize();
            Vector2 normal = new Vector2(-axis.y, axis.x);
            float halfWidth = width * 0.5f;
            float halfDepth = depth * 0.5f;
            float jitterX = width * irregularity;
            float jitterY = depth * irregularity;

            var polygon = new List<Vector2>
            {
                center
                - axis * (halfWidth + RandomRange(random, -jitterX, jitterX))
                - normal * (halfDepth + RandomRange(random, -jitterY, jitterY)),
                center
                + axis * (halfWidth + RandomRange(random, -jitterX, jitterX))
                - normal * (halfDepth + RandomRange(random, -jitterY, jitterY)),
                center
                + axis * (halfWidth + RandomRange(random, -jitterX, jitterX))
                + normal * (halfDepth + RandomRange(random, -jitterY, jitterY)),
                center
                - axis * (halfWidth + RandomRange(random, -jitterX, jitterX))
                + normal * (halfDepth + RandomRange(random, -jitterY, jitterY))
            };

            if (CityPolygonGeometry.SignedArea(polygon) < 0f)
                polygon.Reverse();
            return polygon;
        }

        static List<Vector2> ScalePolygon(
            IReadOnlyList<Vector2> polygon,
            float scale)
        {
            Vector2 center = Average(polygon);
            var scaled = new List<Vector2>(polygon.Count);
            for (int i = 0; i < polygon.Count; i++)
                scaled.Add(center + (polygon[i] - center) * scale);
            return scaled;
        }

        static List<Vector2> ChamferCorner(
            IReadOnlyList<Vector2> polygon,
            int corner,
            float amount)
        {
            int count = polygon.Count;
            var chamfered = new List<Vector2>(count + 1);
            for (int i = 0; i < count; i++)
            {
                if (i != corner)
                {
                    chamfered.Add(polygon[i]);
                    continue;
                }

                Vector2 previous = polygon[(i - 1 + count) % count];
                Vector2 current = polygon[i];
                Vector2 next = polygon[(i + 1) % count];
                chamfered.Add(Vector2.Lerp(current, previous, amount));
                chamfered.Add(Vector2.Lerp(current, next, amount));
            }
            return chamfered;
        }

        static bool OverlapsRoads(
            IReadOnlyList<Vector2> polygon,
            IReadOnlyList<CityRoadSegment> roads)
        {
            for (int i = 0; i < roads.Count; i++)
            {
                if (PolygonsOverlap(polygon, roads[i].GetCorners()))
                    return true;
            }
            return false;
        }

        static bool OverlapsBlocks(
            IReadOnlyList<Vector2> polygon,
            IReadOnlyList<CityBlockData> blocks)
        {
            for (int i = 0; i < blocks.Count; i++)
            {
                if (PolygonsOverlap(polygon, blocks[i].Footprint))
                    return true;
            }
            return false;
        }

        static bool PolygonsOverlap(
            IReadOnlyList<Vector2> first,
            IReadOnlyList<Vector2> second)
        {
            GetBounds(first, out Vector2 firstMin, out Vector2 firstMax);
            GetBounds(second, out Vector2 secondMin, out Vector2 secondMax);
            if (firstMax.x < secondMin.x
                || secondMax.x < firstMin.x
                || firstMax.y < secondMin.y
                || secondMax.y < firstMin.y)
            {
                return false;
            }

            for (int i = 0; i < first.Count; i++)
            {
                Vector2 a = first[i];
                Vector2 b = first[(i + 1) % first.Count];
                for (int j = 0; j < second.Count; j++)
                {
                    Vector2 c = second[j];
                    Vector2 d = second[(j + 1) % second.Count];
                    if (CityPolygonGeometry.SegmentsIntersect(a, b, c, d))
                        return true;
                }
            }

            return CityPolygonGeometry.ContainsPoint(first, second[0])
                || CityPolygonGeometry.ContainsPoint(second, first[0]);
        }

        static Vector2 FindPrimaryDirection(IReadOnlyList<Vector2> polygon)
        {
            float bestLength = -1f;
            Vector2 bestDirection = Vector2.right;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 direction = polygon[(i + 1) % polygon.Count] - polygon[i];
                if (direction.sqrMagnitude <= bestLength)
                    continue;

                bestLength = direction.sqrMagnitude;
                bestDirection = direction.normalized;
            }
            return bestDirection;
        }

        static Vector2 FindInteriorPoint(
            IReadOnlyList<Vector2> polygon,
            System.Random random)
        {
            Vector2 centroid = CityPolygonGeometry.Centroid(polygon);
            if (CityPolygonGeometry.ContainsPoint(polygon, centroid))
                return centroid;

            GetBounds(polygon, out Vector2 minimum, out Vector2 maximum);
            for (int attempt = 0; attempt < 128; attempt++)
            {
                Vector2 candidate = new Vector2(
                    RandomRange(random, minimum.x, maximum.x),
                    RandomRange(random, minimum.y, maximum.y));
                if (CityPolygonGeometry.ContainsPoint(polygon, candidate))
                    return candidate;
            }

            return (polygon[0] + polygon[1] + polygon[2]) / 3f;
        }

        static Vector2 Average(IReadOnlyList<Vector2> points)
        {
            Vector2 sum = Vector2.zero;
            for (int i = 0; i < points.Count; i++)
                sum += points[i];
            return sum / Mathf.Max(1, points.Count);
        }

        static Vector2 Rotate(Vector2 vector, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(radians);
            float sine = Mathf.Sin(radians);
            return new Vector2(
                vector.x * cosine - vector.y * sine,
                vector.x * sine + vector.y * cosine);
        }

        static float RandomRange(
            System.Random random,
            float minimum,
            float maximum)
        {
            return Mathf.Lerp(minimum, maximum, (float)random.NextDouble());
        }

        static bool TryGetSegmentIntersection(
            Vector2 a,
            Vector2 b,
            Vector2 c,
            Vector2 d,
            out float t,
            out float u)
        {
            Vector2 first = b - a;
            Vector2 second = d - c;
            float denominator = Cross(first, second);
            if (Mathf.Abs(denominator) <= 0.0001f)
            {
                t = 0f;
                u = 0f;
                return false;
            }

            Vector2 offset = c - a;
            t = Cross(offset, second) / denominator;
            u = Cross(offset, first) / denominator;
            return t >= 0f && t <= 1f && u >= 0f && u <= 1f;
        }

        static float Cross(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        static void GetBounds(
            IReadOnlyList<Vector2> points,
            out Vector2 minimum,
            out Vector2 maximum)
        {
            minimum = points[0];
            maximum = points[0];
            for (int i = 1; i < points.Count; i++)
            {
                minimum = Vector2.Min(minimum, points[i]);
                maximum = Vector2.Max(maximum, points[i]);
            }
        }
    }
}
