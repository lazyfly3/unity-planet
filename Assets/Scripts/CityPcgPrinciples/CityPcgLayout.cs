using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityPlanet.CityPcg
{
    public enum CityCellKind
    {
        Building = 0,
        Road = 1,
        Park = 2,
        Facility = 3
    }

    public enum CityRoadShape
    {
        Isolated = 0,
        End = 1,
        Straight = 2,
        Corner = 3,
        Tee = 4,
        Cross = 5
    }

    [Serializable]
    public sealed class CityPcgSettings
    {
        [InspectorName("基础 Seed")]
        public int seed = 7319;

        [InspectorName("城市宽度（格）")]
        [Range(15, 61)]
        public int width = 25;

        [InspectorName("城市长度（格）")]
        [Range(15, 61)]
        public int height = 25;

        [InspectorName("每格世界尺寸")]
        [Range(6f, 40f)]
        public float cellSize = 16f;

        [InspectorName("最小街区跨度")]
        [Range(2, 10)]
        public int minBlockSpan = 3;

        [InspectorName("最大街区跨度")]
        [Range(3, 14)]
        public int maxBlockSpan = 6;

        [InspectorName("安全断路概率")]
        [Range(0f, 0.25f)]
        public float safeRoadClosureChance = 0.08f;

        [InspectorName("公园概率")]
        [Range(0f, 0.25f)]
        public float parkChance = 0.07f;

        [InspectorName("建筑最低高度")]
        [Range(4f, 60f)]
        public float minBuildingHeight = 14f;

        [InspectorName("建筑最高高度")]
        [Range(12f, 120f)]
        public float maxBuildingHeight = 54f;

        [InspectorName("最大候选次数")]
        [Range(1, 32)]
        public int maxAttempts = 12;

        public CityPcgSettings ValidatedCopy()
        {
            return new CityPcgSettings
            {
                seed = seed,
                width = Mathf.Clamp(width, 15, 61),
                height = Mathf.Clamp(height, 15, 61),
                cellSize = Mathf.Clamp(cellSize, 6f, 40f),
                minBlockSpan = Mathf.Clamp(minBlockSpan, 2, 10),
                maxBlockSpan = Mathf.Clamp(
                    maxBlockSpan,
                    Mathf.Clamp(minBlockSpan, 2, 10),
                    14),
                safeRoadClosureChance = Mathf.Clamp(
                    safeRoadClosureChance,
                    0f,
                    0.25f),
                parkChance = Mathf.Clamp(parkChance, 0f, 0.25f),
                minBuildingHeight = Mathf.Clamp(
                    minBuildingHeight,
                    4f,
                    60f),
                maxBuildingHeight = Mathf.Max(
                    Mathf.Clamp(minBuildingHeight, 4f, 60f) + 2f,
                    Mathf.Clamp(maxBuildingHeight, 12f, 120f)),
                maxAttempts = Mathf.Clamp(maxAttempts, 1, 32)
            };
        }
    }

    [Serializable]
    public sealed class CityPcgReport
    {
        public bool valid;
        public int requestedSeed;
        public int resolvedSeed;
        public int attempts;
        public int roadCells;
        public int buildingCells;
        public int parkCells;
        public int intersections;
        public float roadCoverage;
        public bool roadNetworkConnected;
        public bool objectiveReachable;
        public bool alternateRouteAvailable;
        public int primaryRouteLength;
        public int alternateRouteLength;
        public int checksum;
        public string failureReason = string.Empty;

        public string Summary =>
            (valid ? "通过" : "失败")
            + " | Seed " + resolvedSeed
            + " | 尝试 " + attempts
            + " | 道路 " + roadCells
            + " | 建筑 " + buildingCells
            + " | 路口 " + intersections
            + " | 双路线 "
            + (alternateRouteAvailable ? "有" : "无")
            + " | 校验码 " + checksum;
    }

    public sealed class CityPcgLayout
    {
        public readonly int Width;
        public readonly int Height;
        public readonly CityCellKind[,] Cells;
        public readonly float[,] BuildingHeights;

        public Vector2Int playerSpawn;
        public Vector2Int objective;
        public Vector2Int facility;
        public List<Vector2Int> primaryRoute = new List<Vector2Int>();
        public List<Vector2Int> alternateRoute = new List<Vector2Int>();

        public CityPcgLayout(int width, int height)
        {
            Width = width;
            Height = height;
            Cells = new CityCellKind[width, height];
            BuildingHeights = new float[width, height];
        }

        public bool InBounds(Vector2Int cell)
        {
            return cell.x >= 0
                && cell.y >= 0
                && cell.x < Width
                && cell.y < Height;
        }

        public bool IsRoad(Vector2Int cell)
        {
            return InBounds(cell)
                && Cells[cell.x, cell.y] == CityCellKind.Road;
        }

        public CityRoadShape GetRoadShape(
            Vector2Int cell,
            out float yaw)
        {
            yaw = 0f;
            if (!IsRoad(cell))
                return CityRoadShape.Isolated;

            bool north = IsRoad(cell + Vector2Int.up);
            bool east = IsRoad(cell + Vector2Int.right);
            bool south = IsRoad(cell + Vector2Int.down);
            bool west = IsRoad(cell + Vector2Int.left);
            int count = (north ? 1 : 0)
                + (east ? 1 : 0)
                + (south ? 1 : 0)
                + (west ? 1 : 0);

            if (count >= 4)
                return CityRoadShape.Cross;

            if (count == 3)
            {
                if (!north) yaw = 180f;
                else if (!east) yaw = 270f;
                else if (!south) yaw = 0f;
                else yaw = 90f;
                return CityRoadShape.Tee;
            }

            if (count == 2)
            {
                if (north && south)
                    return CityRoadShape.Straight;
                if (east && west)
                {
                    yaw = 90f;
                    return CityRoadShape.Straight;
                }

                if (north && east) yaw = 0f;
                else if (east && south) yaw = 90f;
                else if (south && west) yaw = 180f;
                else yaw = 270f;
                return CityRoadShape.Corner;
            }

            if (count == 1)
            {
                if (east) yaw = 90f;
                else if (south) yaw = 180f;
                else if (west) yaw = 270f;
                return CityRoadShape.End;
            }

            return CityRoadShape.Isolated;
        }
    }

    /// <summary>
    /// A clean-room, dependency-free city layout generator. It recreates the
    /// general PCG pipeline (seed -> layout -> markers -> validation) without
    /// referencing Dungeon Architect or copying its implementation.
    /// </summary>
    public static class CityPcgGenerator
    {
        static readonly Vector2Int[] Directions =
        {
            Vector2Int.up,
            Vector2Int.right,
            Vector2Int.down,
            Vector2Int.left
        };

        struct StableRandom
        {
            uint state;

            public StableRandom(int seed)
            {
                state = (uint)seed;
                if (state == 0u)
                    state = 0xA341316Cu;
            }

            public uint NextUInt()
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                return state;
            }

            public float Value()
            {
                return (NextUInt() & 0x00FFFFFFu) / 16777216f;
            }

            public int Range(int minimum, int maximumExclusive)
            {
                if (maximumExclusive <= minimum)
                    return minimum;
                return minimum
                    + (int)(NextUInt()
                        % (uint)(maximumExclusive - minimum));
            }

            public float Range(float minimum, float maximum)
            {
                return Mathf.Lerp(minimum, maximum, Value());
            }
        }

        public static CityPcgLayout Generate(
            CityPcgSettings source,
            out CityPcgReport report)
        {
            CityPcgSettings settings =
                (source ?? new CityPcgSettings()).ValidatedCopy();
            CityPcgLayout lastLayout = null;
            CityPcgReport lastReport = null;

            for (int attempt = 0;
                 attempt < settings.maxAttempts;
                 attempt++)
            {
                int resolvedSeed = DeriveSeed(settings.seed, attempt);
                var random = new StableRandom(resolvedSeed);
                CityPcgLayout layout = BuildCandidate(settings, ref random);
                CityPcgReport candidateReport = Validate(
                    layout,
                    settings,
                    resolvedSeed,
                    attempt + 1);
                lastLayout = layout;
                lastReport = candidateReport;
                if (candidateReport.valid)
                {
                    report = candidateReport;
                    return layout;
                }
            }

            report = lastReport ?? new CityPcgReport
            {
                valid = false,
                requestedSeed = settings.seed,
                resolvedSeed = settings.seed,
                attempts = settings.maxAttempts,
                failureReason = "没有生成候选布局。"
            };
            return lastLayout;
        }

        static CityPcgLayout BuildCandidate(
            CityPcgSettings settings,
            ref StableRandom random)
        {
            var layout = new CityPcgLayout(
                settings.width,
                settings.height);

            for (int x = 0; x < layout.Width; x++)
            {
                for (int y = 0; y < layout.Height; y++)
                    layout.Cells[x, y] = CityCellKind.Building;
            }

            PaintBorderRoad(layout);
            PaintRoadBands(layout, settings, ref random, true);
            PaintRoadBands(layout, settings, ref random, false);
            PaintCentralCross(layout);
            ApplySafeClosures(layout, settings, ref random);

            layout.playerSpawn = FindNearestRoad(
                layout,
                new Vector2Int(layout.Width / 2, 0));
            layout.objective = FindNearestRoad(
                layout,
                new Vector2Int(layout.Width / 2, layout.Height - 1));
            layout.facility = FindAdjacentLot(
                layout,
                layout.objective,
                new Vector2Int(layout.Width / 2, layout.Height - 3));
            if (layout.InBounds(layout.facility)
                && !layout.IsRoad(layout.facility))
            {
                layout.Cells[layout.facility.x, layout.facility.y] =
                    CityCellKind.Facility;
            }

            DecorateLots(layout, settings, ref random);
            return layout;
        }

        static void PaintBorderRoad(CityPcgLayout layout)
        {
            for (int x = 0; x < layout.Width; x++)
            {
                layout.Cells[x, 0] = CityCellKind.Road;
                layout.Cells[x, layout.Height - 1] = CityCellKind.Road;
            }

            for (int y = 0; y < layout.Height; y++)
            {
                layout.Cells[0, y] = CityCellKind.Road;
                layout.Cells[layout.Width - 1, y] = CityCellKind.Road;
            }
        }

        static void PaintRoadBands(
            CityPcgLayout layout,
            CityPcgSettings settings,
            ref StableRandom random,
            bool vertical)
        {
            int limit = vertical ? layout.Width : layout.Height;
            int cursor = 1 + random.Range(
                settings.minBlockSpan,
                settings.maxBlockSpan + 1);

            while (cursor < limit - 1)
            {
                if (vertical)
                {
                    for (int y = 0; y < layout.Height; y++)
                        layout.Cells[cursor, y] = CityCellKind.Road;
                }
                else
                {
                    for (int x = 0; x < layout.Width; x++)
                        layout.Cells[x, cursor] = CityCellKind.Road;
                }

                cursor += 1 + random.Range(
                    settings.minBlockSpan,
                    settings.maxBlockSpan + 1);
            }
        }

        static void PaintCentralCross(CityPcgLayout layout)
        {
            int centerX = layout.Width / 2;
            int centerY = layout.Height / 2;
            for (int y = 0; y < layout.Height; y++)
                layout.Cells[centerX, y] = CityCellKind.Road;
            for (int x = 0; x < layout.Width; x++)
                layout.Cells[x, centerY] = CityCellKind.Road;
        }

        static void ApplySafeClosures(
            CityPcgLayout layout,
            CityPcgSettings settings,
            ref StableRandom random)
        {
            var candidates = new List<Vector2Int>();
            for (int x = 2; x < layout.Width - 2; x++)
            {
                for (int y = 2; y < layout.Height - 2; y++)
                {
                    var cell = new Vector2Int(x, y);
                    if (RoadDegree(layout, cell) == 2
                        && IsStraight(layout, cell))
                    {
                        candidates.Add(cell);
                    }
                }
            }

            Shuffle(candidates, ref random);
            for (int i = 0; i < candidates.Count; i++)
            {
                if (random.Value() > settings.safeRoadClosureChance)
                    continue;

                Vector2Int cell = candidates[i];
                layout.Cells[cell.x, cell.y] = CityCellKind.Building;
                if (!AllRoadCellsConnected(layout))
                    layout.Cells[cell.x, cell.y] = CityCellKind.Road;
            }
        }

        static void DecorateLots(
            CityPcgLayout layout,
            CityPcgSettings settings,
            ref StableRandom random)
        {
            Vector2 center = new Vector2(
                (layout.Width - 1) * 0.5f,
                (layout.Height - 1) * 0.5f);
            float maxDistance = Mathf.Max(1f, center.magnitude);

            for (int x = 0; x < layout.Width; x++)
            {
                for (int y = 0; y < layout.Height; y++)
                {
                    if (layout.Cells[x, y] != CityCellKind.Building)
                        continue;

                    if (random.Value() < settings.parkChance)
                    {
                        layout.Cells[x, y] = CityCellKind.Park;
                        continue;
                    }

                    float centerWeight = 1f
                        - Vector2.Distance(new Vector2(x, y), center)
                        / maxDistance;
                    float randomHeight = random.Range(
                        settings.minBuildingHeight,
                        settings.maxBuildingHeight);
                    layout.BuildingHeights[x, y] = Mathf.Lerp(
                        randomHeight * 0.72f,
                        randomHeight,
                        Mathf.Clamp01(centerWeight));
                }
            }

            if (layout.InBounds(layout.facility))
            {
                layout.BuildingHeights[
                    layout.facility.x,
                    layout.facility.y] =
                    settings.maxBuildingHeight * 0.82f;
            }
        }

        static CityPcgReport Validate(
            CityPcgLayout layout,
            CityPcgSettings settings,
            int resolvedSeed,
            int attempts)
        {
            var report = new CityPcgReport
            {
                requestedSeed = settings.seed,
                resolvedSeed = resolvedSeed,
                attempts = attempts
            };

            for (int x = 0; x < layout.Width; x++)
            {
                for (int y = 0; y < layout.Height; y++)
                {
                    var cell = new Vector2Int(x, y);
                    switch (layout.Cells[x, y])
                    {
                        case CityCellKind.Road:
                            report.roadCells++;
                            if (RoadDegree(layout, cell) >= 3)
                                report.intersections++;
                            break;
                        case CityCellKind.Building:
                        case CityCellKind.Facility:
                            report.buildingCells++;
                            break;
                        case CityCellKind.Park:
                            report.parkCells++;
                            break;
                    }
                }
            }

            int totalCells = layout.Width * layout.Height;
            report.roadCoverage = totalCells > 0
                ? (float)report.roadCells / totalCells
                : 0f;
            report.roadNetworkConnected = AllRoadCellsConnected(layout);
            layout.primaryRoute = FindPath(
                layout,
                layout.playerSpawn,
                layout.objective,
                null);
            report.objectiveReachable = layout.primaryRoute.Count > 0;

            var blocked = new HashSet<Vector2Int>();
            for (int i = 1; i < layout.primaryRoute.Count - 1; i++)
            {
                if ((i & 1) == 0)
                    blocked.Add(layout.primaryRoute[i]);
            }
            layout.alternateRoute = FindPath(
                layout,
                layout.playerSpawn,
                layout.objective,
                blocked);
            report.alternateRouteAvailable =
                layout.alternateRoute.Count > 0;
            report.primaryRouteLength = layout.primaryRoute.Count;
            report.alternateRouteLength = layout.alternateRoute.Count;
            report.checksum = ComputeChecksum(layout, resolvedSeed);

            bool coverageValid = report.roadCoverage >= 0.16f
                && report.roadCoverage <= 0.52f;
            bool facilityValid = layout.InBounds(layout.facility)
                && layout.Cells[
                    layout.facility.x,
                    layout.facility.y] == CityCellKind.Facility
                && HasAdjacentRoad(layout, layout.facility);

            report.valid = report.roadNetworkConnected
                && report.objectiveReachable
                && report.alternateRouteAvailable
                && report.intersections >= 4
                && coverageValid
                && facilityValid;

            if (!report.valid)
            {
                if (!report.roadNetworkConnected)
                    report.failureReason = "道路网络不连通。";
                else if (!report.objectiveReachable)
                    report.failureReason = "出生点无法到达目标。";
                else if (!report.alternateRouteAvailable)
                    report.failureReason = "缺少备用路线。";
                else if (!coverageValid)
                    report.failureReason = "道路覆盖率不合理。";
                else if (!facilityValid)
                    report.failureReason = "设施没有连接道路。";
                else
                    report.failureReason = "有效交叉路口不足。";
            }

            return report;
        }

        static bool AllRoadCellsConnected(CityPcgLayout layout)
        {
            Vector2Int start = new Vector2Int(-1, -1);
            int roadCount = 0;
            for (int x = 0; x < layout.Width; x++)
            {
                for (int y = 0; y < layout.Height; y++)
                {
                    var cell = new Vector2Int(x, y);
                    if (!layout.IsRoad(cell))
                        continue;
                    roadCount++;
                    if (start.x < 0)
                        start = cell;
                }
            }

            if (roadCount == 0)
                return false;

            var visited = new HashSet<Vector2Int> { start };
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                Vector2Int cell = queue.Dequeue();
                for (int i = 0; i < Directions.Length; i++)
                {
                    Vector2Int next = cell + Directions[i];
                    if (layout.IsRoad(next) && visited.Add(next))
                        queue.Enqueue(next);
                }
            }
            return visited.Count == roadCount;
        }

        static List<Vector2Int> FindPath(
            CityPcgLayout layout,
            Vector2Int start,
            Vector2Int goal,
            HashSet<Vector2Int> blocked)
        {
            var result = new List<Vector2Int>();
            if (!layout.IsRoad(start) || !layout.IsRoad(goal))
                return result;

            var queue = new Queue<Vector2Int>();
            var previous = new Dictionary<Vector2Int, Vector2Int>();
            queue.Enqueue(start);
            previous[start] = start;

            while (queue.Count > 0)
            {
                Vector2Int cell = queue.Dequeue();
                if (cell == goal)
                    break;

                for (int i = 0; i < Directions.Length; i++)
                {
                    Vector2Int next = cell + Directions[i];
                    if (!layout.IsRoad(next)
                        || previous.ContainsKey(next)
                        || (blocked != null
                            && blocked.Contains(next)
                            && next != goal))
                    {
                        continue;
                    }
                    previous[next] = cell;
                    queue.Enqueue(next);
                }
            }

            if (!previous.ContainsKey(goal))
                return result;

            Vector2Int current = goal;
            while (current != start)
            {
                result.Add(current);
                current = previous[current];
            }
            result.Add(start);
            result.Reverse();
            return result;
        }

        static Vector2Int FindNearestRoad(
            CityPcgLayout layout,
            Vector2Int target)
        {
            Vector2Int best = new Vector2Int(-1, -1);
            int bestDistance = int.MaxValue;
            for (int x = 0; x < layout.Width; x++)
            {
                for (int y = 0; y < layout.Height; y++)
                {
                    var cell = new Vector2Int(x, y);
                    if (!layout.IsRoad(cell))
                        continue;
                    int distance = Mathf.Abs(x - target.x)
                        + Mathf.Abs(y - target.y);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = cell;
                    }
                }
            }
            return best;
        }

        static Vector2Int FindAdjacentLot(
            CityPcgLayout layout,
            Vector2Int road,
            Vector2Int preferred)
        {
            Vector2Int best = new Vector2Int(-1, -1);
            int bestDistance = int.MaxValue;
            for (int radius = 1; radius <= 5; radius++)
            {
                for (int x = road.x - radius; x <= road.x + radius; x++)
                {
                    for (int y = road.y - radius; y <= road.y + radius; y++)
                    {
                        var cell = new Vector2Int(x, y);
                        if (!layout.InBounds(cell)
                            || layout.IsRoad(cell)
                            || !HasAdjacentRoad(layout, cell))
                        {
                            continue;
                        }
                        int distance = Mathf.Abs(x - preferred.x)
                            + Mathf.Abs(y - preferred.y);
                        if (distance < bestDistance)
                        {
                            bestDistance = distance;
                            best = cell;
                        }
                    }
                }
                if (best.x >= 0)
                    return best;
            }
            return best;
        }

        static bool HasAdjacentRoad(
            CityPcgLayout layout,
            Vector2Int cell)
        {
            for (int i = 0; i < Directions.Length; i++)
            {
                if (layout.IsRoad(cell + Directions[i]))
                    return true;
            }
            return false;
        }

        static int RoadDegree(
            CityPcgLayout layout,
            Vector2Int cell)
        {
            int count = 0;
            for (int i = 0; i < Directions.Length; i++)
            {
                if (layout.IsRoad(cell + Directions[i]))
                    count++;
            }
            return count;
        }

        static bool IsStraight(
            CityPcgLayout layout,
            Vector2Int cell)
        {
            bool vertical = layout.IsRoad(cell + Vector2Int.up)
                && layout.IsRoad(cell + Vector2Int.down);
            bool horizontal = layout.IsRoad(cell + Vector2Int.left)
                && layout.IsRoad(cell + Vector2Int.right);
            return vertical || horizontal;
        }

        static void Shuffle(
            List<Vector2Int> values,
            ref StableRandom random)
        {
            for (int i = values.Count - 1; i > 0; i--)
            {
                int swap = random.Range(0, i + 1);
                Vector2Int value = values[i];
                values[i] = values[swap];
                values[swap] = value;
            }
        }

        static int DeriveSeed(int seed, int attempt)
        {
            unchecked
            {
                uint value = (uint)seed;
                value ^= (uint)(attempt + 1) * 0x9E3779B9u;
                value ^= value >> 16;
                value *= 0x7FEB352Du;
                value ^= value >> 15;
                value *= 0x846CA68Bu;
                value ^= value >> 16;
                return (int)value;
            }
        }

        static int ComputeChecksum(CityPcgLayout layout, int seed)
        {
            unchecked
            {
                uint hash = 2166136261u;
                hash = (hash ^ (uint)seed) * 16777619u;
                for (int y = 0; y < layout.Height; y++)
                {
                    for (int x = 0; x < layout.Width; x++)
                    {
                        hash = (hash ^ (uint)layout.Cells[x, y])
                            * 16777619u;
                        hash = (hash ^ (uint)Mathf.RoundToInt(
                            layout.BuildingHeights[x, y] * 10f))
                            * 16777619u;
                    }
                }
                return (int)hash;
            }
        }
    }
}
