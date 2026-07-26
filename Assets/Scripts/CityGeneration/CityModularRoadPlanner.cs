using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityGeneration
{
    public readonly struct CityModularGridBasis
    {
        public readonly Vector2 Origin;
        public readonly Vector2 AxisX;
        public readonly Vector2 AxisY;

        public CityModularGridBasis(Vector2 origin, Vector2 axisX)
        {
            Origin = origin;
            AxisX = axisX.sqrMagnitude < 0.001f
                ? Vector2.right
                : axisX.normalized;
            AxisY = new Vector2(-AxisX.y, AxisX.x);
        }

        public Vector2 ToWorld(int x, int y, float cellSize)
        {
            return Origin
                + AxisX * (x * cellSize)
                + AxisY * (y * cellSize);
        }

        public Vector2 ToGrid(Vector2 world, float cellSize)
        {
            Vector2 delta = world - Origin;
            return new Vector2(
                Vector2.Dot(delta, AxisX) / cellSize,
                Vector2.Dot(delta, AxisY) / cellSize);
        }
    }

    public static class CityModularRoadPlanner
    {
        const int North = 1;
        const int East = 2;
        const int South = 4;
        const int West = 8;

        static readonly GridCoord[] Directions =
        {
            new GridCoord(0, 1),
            new GridCoord(1, 0),
            new GridCoord(0, -1),
            new GridCoord(-1, 0)
        };
        static readonly ModernCityRoadModuleLibrary ModuleDefinitions =
            new ModernCityRoadModuleLibrary();

        public static CityModularGridBasis CreateGlobalBasis(
            IReadOnlyList<List<Vector2>> regions)
        {
            if (regions == null || regions.Count == 0)
                return new CityModularGridBasis(Vector2.zero, Vector2.right);

            IReadOnlyList<Vector2> largest = regions[0];
            float largestArea = Mathf.Abs(
                CityPolygonGeometry.SignedArea(largest));
            for (int i = 1; i < regions.Count; i++)
            {
                float area = Mathf.Abs(
                    CityPolygonGeometry.SignedArea(regions[i]));
                if (area > largestArea)
                {
                    largest = regions[i];
                    largestArea = area;
                }
            }

            Vector2 origin = Average(largest);
            Vector2 axis = FindLongestEdge(largest);
            return new CityModularGridBasis(origin, axis);
        }

        public static bool GenerateRoads(
            IReadOnlyList<Vector2> region,
            int regionIndex,
            CityModularGridBasis basis,
            CityGenerationSettings settings,
            System.Random random,
            CityGenerationResult result,
            List<CityRoadSegment> regionRoads)
        {
            float cellSize = settings.modularRoadCellSize;
            HashSet<GridCoord> candidates =
                FindContainedCells(region, basis, cellSize, 0.5f);
            if (!ContainsTwoByTwo(candidates))
                return false;

            GridCoord seed = FindNearestCandidate(
                candidates,
                basis.ToGrid(Average(region), cellSize));
            var occupied = new HashSet<GridCoord>();
            var major = new HashSet<GridCoord>();
            occupied.Add(seed);
            major.Add(seed);

            for (int direction = 0; direction < Directions.Length; direction++)
                GrowLine(seed, Directions[direction], true, int.MaxValue);

            GridCoord[] majorSnapshot = major
                .OrderBy(value => value.X)
                .ThenBy(value => value.Y)
                .ToArray();
            for (int i = 0; i < majorSnapshot.Length; i++)
            {
                if (i % 2 != 0
                    || random.NextDouble() > settings.minorRoadDensity)
                {
                    continue;
                }

                GridCoord cell = majorSnapshot[i];
                GridCoord direction = Math.Abs(cell.X - seed.X)
                    >= Math.Abs(cell.Y - seed.Y)
                    ? (random.NextDouble() < 0.5d
                        ? Directions[0]
                        : Directions[2])
                    : (random.NextDouble() < 0.5d
                        ? Directions[1]
                        : Directions[3]);
                int length = random.Next(
                    settings.minimumBranchSegments,
                    settings.maximumBranchSegments + 1);
                GrowBranch(cell, direction, length);
            }

            result.Diagnostics.RoadTopologyRepairCount +=
                EnsureSingleConnectedComponent(
                    occupied,
                    candidates,
                    seed);
            RemoveIsolatedCells(occupied);
            if (occupied.Count < 4)
                return false;

            var maskByCell = new Dictionary<GridCoord, int>();
            foreach (GridCoord cell in occupied)
                maskByCell[cell] = GetNeighborMask(cell, occupied);

            GridCoord[] ordered = occupied
                .OrderBy(value => value.X)
                .ThenBy(value => value.Y)
                .ToArray();
            int firstRoadModule = result.RoadModules.Count;
            for (int i = 0; i < ordered.Length; i++)
            {
                GridCoord cell = ordered[i];
                int mask = maskByCell[cell];
                if (CountBits(mask) == 0)
                    continue;

                if (!ResolveModule(
                    cell,
                    mask,
                    maskByCell,
                    out CityRoadModuleType type,
                    out int quarterTurns,
                    out CityRoadConnectionMask connections))
                {
                    result.RoadModules.RemoveRange(
                        firstRoadModule,
                        result.RoadModules.Count - firstRoadModule);
                    return false;
                }
                result.RoadModules.Add(new CityRoadModulePlacement(
                    result.RoadModules.Count,
                    regionIndex,
                    cell.X,
                    cell.Y,
                    basis.ToWorld(cell.X, cell.Y, cellSize),
                    type,
                    quarterTurns,
                    major.Contains(cell),
                    1,
                    connections,
                    ModuleDefinitions.GetSocketTags(
                        type,
                        quarterTurns)));
            }

            foreach (GridCoord cell in ordered)
            {
                AddConnection(cell, Directions[0]);
                AddConnection(cell, Directions[1]);
            }
            return result.RoadModules.Count > 0 && regionRoads.Count > 0;

            void GrowLine(
                GridCoord start,
                GridCoord direction,
                bool isMajor,
                int maximumLength)
            {
                GridCoord current = start;
                int length = 0;
                while (length < maximumLength)
                {
                    GridCoord next = current + direction;
                    if (!candidates.Contains(next))
                        break;
                    occupied.Add(next);
                    if (isMajor)
                        major.Add(next);
                    current = next;
                    length++;
                }
            }

            void GrowBranch(
                GridCoord start,
                GridCoord initialDirection,
                int maximumLength)
            {
                GridCoord current = start;
                GridCoord direction = initialDirection;
                int turnAt = maximumLength >= 4
                    ? random.Next(2, maximumLength - 1)
                    : -1;
                int turnSign = random.NextDouble() < 0.5d ? -1 : 1;
                for (int step = 0; step < maximumLength; step++)
                {
                    if (step == turnAt)
                    {
                        GridCoord turned = Rotate(direction, turnSign);
                        if (candidates.Contains(current + turned))
                            direction = turned;
                    }

                    GridCoord next = current + direction;
                    if (!candidates.Contains(next))
                        break;
                    occupied.Add(next);
                    current = next;
                }
            }

            void AddConnection(GridCoord start, GridCoord direction)
            {
                GridCoord end = start + direction;
                if (!occupied.Contains(end))
                    return;
                bool isMajor = major.Contains(start) && major.Contains(end);
                var road = new CityRoadSegment(
                    basis.ToWorld(start.X, start.Y, cellSize),
                    basis.ToWorld(end.X, end.Y, cellSize),
                    cellSize,
                    isMajor);
                regionRoads.Add(road);
                result.Roads.Add(road);
            }
        }

        public static CityRoadLayoutValidationResult ValidateLayout(
            IReadOnlyList<CityRoadModulePlacement> modules)
        {
            var validation = new CityRoadLayoutValidationResult
            {
                IsValid = true
            };
            if (modules == null || modules.Count == 0)
            {
                validation.IsValid = false;
                validation.Error = "The modular road layout is empty.";
                return validation;
            }

            foreach (IGrouping<int, CityRoadModulePlacement> region
                     in modules.GroupBy(value => value.RegionIndex)
                         .OrderBy(value => value.Key))
            {
                var byCell =
                    new Dictionary<Vector2Int, CityRoadModulePlacement>();
                foreach (CityRoadModulePlacement module in region)
                {
                    var coordinate =
                        new Vector2Int(module.GridX, module.GridY);
                    if (byCell.ContainsKey(coordinate))
                    {
                        validation.IsValid = false;
                        validation.Error =
                            $"Region {region.Key} contains duplicate road "
                            + $"cell {coordinate}.";
                        return validation;
                    }
                    byCell.Add(coordinate, module);
                }

                foreach (KeyValuePair<Vector2Int, CityRoadModulePlacement>
                         pair in byCell)
                {
                    CityRoadModulePlacement module = pair.Value;
                    if (module.SocketTags.RoadConnectionMask
                        != module.ConnectionMask)
                    {
                        validation.IsValid = false;
                        validation.Error =
                            $"Region {region.Key} road cell {pair.Key} "
                            + $"has socket mask "
                            + $"{module.SocketTags.RoadConnectionMask} but "
                            + $"topology requires {module.ConnectionMask}.";
                        return validation;
                    }
                    for (int direction = 0;
                         direction < Directions.Length;
                         direction++)
                    {
                        CityRoadConnectionMask mask =
                            (CityRoadConnectionMask)(1 << direction);
                        GridCoord offset = Directions[direction];
                        var neighborCoordinate = new Vector2Int(
                            pair.Key.x + offset.X,
                            pair.Key.y + offset.Y);
                        CityRoadSocketTag socket =
                            module.SocketTags.Get(mask);
                        bool hasNeighbor = byCell.TryGetValue(
                                neighborCoordinate,
                                out CityRoadModulePlacement neighbor);
                        if (!hasNeighbor)
                        {
                            if (socket
                                == CityRoadSocketTag.Road2LaneA)
                            {
                                validation.IsValid = false;
                                validation.Error =
                                    $"Region {region.Key} road cell "
                                    + $"{pair.Key} has a dangling {mask} "
                                    + $"{socket} socket.";
                                return validation;
                            }
                            continue;
                        }
                        CityRoadConnectionMask opposite =
                            ModernCityRoadModuleLibrary.Opposite(mask);
                        CityRoadSocketTag neighborSocket =
                            neighbor.SocketTags.Get(opposite);
                        if (!ModernCityRoadModuleLibrary
                                .AreOpposingSocketsCompatible(
                                    socket,
                                    neighborSocket))
                        {
                            validation.IsValid = false;
                            validation.Error =
                                $"Region {region.Key} road cells {pair.Key} "
                                + $"({mask}:{socket}) and "
                                + $"{neighborCoordinate} "
                                + $"({opposite}:{neighborSocket}) have "
                                + "incompatible opposing sockets.";
                            return validation;
                        }
                    }
                }

                int componentCount = CountConnectedComponents(byCell);
                validation.ConnectedComponentCount += componentCount;
                if (componentCount != 1)
                {
                    validation.IsValid = false;
                    validation.Error =
                        $"Region {region.Key} contains {componentCount} "
                        + "disconnected road networks.";
                    return validation;
                }
            }

            return validation;
        }

        public static void GeneratePathways(
            IReadOnlyList<Vector2> region,
            int regionIndex,
            CityModularGridBasis basis,
            CityGenerationSettings settings,
            CityGenerationResult result,
            int firstBlock,
            int firstBuilding,
            int seed)
        {
            float unit = settings.modularPathwayUnitSize;
            var occupiedRoadCells = new HashSet<GridCoord>(
                result.RoadModules
                    .Where(value =>
                        value.RegionIndex == regionIndex)
                    .Select(value =>
                        new GridCoord(value.GridX, value.GridY)));
            var available = new HashSet<GridCoord>();
            for (int blockIndex = firstBlock;
                 blockIndex < result.Blocks.Count;
                 blockIndex++)
            {
                IReadOnlyList<Vector2> block =
                    result.Blocks[blockIndex].Footprint;
                GetGridBounds(block, basis, unit, out int minX, out int maxX,
                    out int minY, out int maxY);
                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        GridCoord cell = new GridCoord(x, y);
                        Vector2[] square = GetCellPolygon(
                            basis,
                            cell,
                            unit,
                            0.5f);
                        if (!CityPolygonGeometry.ContainsPolygon(block, square)
                            || !CityPolygonGeometry.ContainsPolygon(region, square)
                            || OverlapsRoadGrid(
                                square,
                                basis,
                                settings.modularRoadCellSize,
                                occupiedRoadCells)
                            || OverlapsBuildings(
                                square,
                                result.Buildings,
                                firstBuilding))
                        {
                            continue;
                        }
                        available.Add(cell);
                    }
                }
            }

            GridCoord[] anchors = available
                .OrderBy(value => value.Y)
                .ThenBy(value => value.X)
                .ToArray();
            for (int anchorIndex = 0;
                 anchorIndex < anchors.Length;
                 anchorIndex++)
            {
                GridCoord anchor = anchors[anchorIndex];
                if (!available.Contains(anchor))
                    continue;

                int sizeX = 1;
                int sizeY = 1;
                if (CanTake(anchor, 4, 4))
                {
                    sizeX = 4;
                    sizeY = 4;
                }
                else if (CanTake(anchor, 2, 2))
                {
                    sizeX = 2;
                    sizeY = 2;
                }
                else if (CanTake(anchor, 2, 1))
                {
                    sizeX = 2;
                }
                else if (CanTake(anchor, 1, 2))
                {
                    sizeY = 2;
                }

                for (int y = 0; y < sizeY; y++)
                {
                    for (int x = 0; x < sizeX; x++)
                        available.Remove(new GridCoord(anchor.X + x, anchor.Y + y));
                }

                Vector2 center = basis.ToWorld(
                    anchor.X,
                    anchor.Y,
                    unit)
                    + basis.AxisX * ((sizeX - 1) * unit * 0.5f)
                    + basis.AxisY * ((sizeY - 1) * unit * 0.5f);
                int stableHash = StableHash(
                    seed,
                    regionIndex,
                    anchor.X,
                    anchor.Y);
                result.PathwayModules.Add(
                    new CityPathwayModulePlacement(
                        result.PathwayModules.Count,
                        regionIndex,
                        anchor.X,
                        anchor.Y,
                        center,
                        sizeX,
                        sizeY,
                        stableHash,
                        sizeY > sizeX ? 1 : 0));
            }

            bool CanTake(GridCoord anchor, int width, int height)
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        if (!available.Contains(
                                new GridCoord(anchor.X + x, anchor.Y + y)))
                        {
                            return false;
                        }
                    }
                }
                return true;
            }
        }

        public static void RemoveBuildingsOverlappingRoadModules(
            CityGenerationResult result,
            int regionIndex,
            int firstBuilding,
            CityModularGridBasis basis,
            float roadCellSize)
        {
            for (int i = result.Buildings.Count - 1;
                 i >= firstBuilding;
                 i--)
            {
                if (OverlapsRoadModules(
                        result.Buildings[i].Footprint,
                        result.RoadModules,
                        regionIndex,
                        basis,
                        roadCellSize))
                {
                    result.Buildings.RemoveAt(i);
                }
            }
        }

        static HashSet<GridCoord> FindContainedCells(
            IReadOnlyList<Vector2> region,
            CityModularGridBasis basis,
            float cellSize,
            float halfExtentScale)
        {
            GetGridBounds(region, basis, cellSize,
                out int minimumX, out int maximumX,
                out int minimumY, out int maximumY);
            var cells = new HashSet<GridCoord>();
            for (int y = minimumY; y <= maximumY; y++)
            {
                for (int x = minimumX; x <= maximumX; x++)
                {
                    var cell = new GridCoord(x, y);
                    if (CityPolygonGeometry.ContainsPolygon(
                            region,
                            GetCellPolygon(
                                basis,
                                cell,
                                cellSize,
                                halfExtentScale)))
                    {
                        cells.Add(cell);
                    }
                }
            }
            return cells;
        }

        static Vector2[] GetCellPolygon(
            CityModularGridBasis basis,
            GridCoord cell,
            float size,
            float halfExtentScale)
        {
            Vector2 center = basis.ToWorld(cell.X, cell.Y, size);
            Vector2 x = basis.AxisX * (size * halfExtentScale);
            Vector2 y = basis.AxisY * (size * halfExtentScale);
            return new[]
            {
                center - x - y,
                center + x - y,
                center + x + y,
                center - x + y
            };
        }

        static void GetGridBounds(
            IReadOnlyList<Vector2> polygon,
            CityModularGridBasis basis,
            float size,
            out int minimumX,
            out int maximumX,
            out int minimumY,
            out int maximumY)
        {
            minimumX = int.MaxValue;
            maximumX = int.MinValue;
            minimumY = int.MaxValue;
            maximumY = int.MinValue;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 grid = basis.ToGrid(polygon[i], size);
                minimumX = Mathf.Min(minimumX, Mathf.FloorToInt(grid.x) - 1);
                maximumX = Mathf.Max(maximumX, Mathf.CeilToInt(grid.x) + 1);
                minimumY = Mathf.Min(minimumY, Mathf.FloorToInt(grid.y) - 1);
                maximumY = Mathf.Max(maximumY, Mathf.CeilToInt(grid.y) + 1);
            }
        }

        static bool ContainsTwoByTwo(HashSet<GridCoord> cells)
        {
            foreach (GridCoord cell in cells)
            {
                if (cells.Contains(cell + Directions[0])
                    && cells.Contains(cell + Directions[1])
                    && cells.Contains(
                        cell + Directions[0] + Directions[1]))
                {
                    return true;
                }
            }
            return false;
        }

        static GridCoord FindNearestCandidate(
            IEnumerable<GridCoord> candidates,
            Vector2 target)
        {
            GridCoord best = default;
            float bestDistance = float.MaxValue;
            foreach (GridCoord cell in candidates)
            {
                float distance = (new Vector2(cell.X, cell.Y) - target)
                    .sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = cell;
                }
            }
            return best;
        }

        static int GetNeighborMask(
            GridCoord cell,
            HashSet<GridCoord> occupied)
        {
            int mask = 0;
            if (occupied.Contains(cell + Directions[0]))
                mask |= North;
            if (occupied.Contains(cell + Directions[1]))
                mask |= East;
            if (occupied.Contains(cell + Directions[2]))
                mask |= South;
            if (occupied.Contains(cell + Directions[3]))
                mask |= West;
            return mask;
        }

        static bool ResolveModule(
            GridCoord cell,
            int mask,
            IReadOnlyDictionary<GridCoord, int> masks,
            out CityRoadModuleType type,
            out int quarterTurns,
            out CityRoadConnectionMask connections)
        {
            connections = (CityRoadConnectionMask)mask;
            CityRoadConnectionMask preferredCrosswalkEdge =
                CityRoadConnectionMask.None;
            int degree = CountBits(mask);
            if (degree <= 1)
            {
                type = CityRoadModuleType.End;
            }
            else if (degree == 2)
            {
                if (mask == (North | South) || mask == (East | West))
                {
                    type = HasAdjacentIntersection(cell, mask, masks)
                        ? CityRoadModuleType.StraightCrossing
                        : CityRoadModuleType.Straight;
                    if (type == CityRoadModuleType.StraightCrossing)
                    {
                        preferredCrosswalkEdge =
                            FindAdjacentIntersectionDirection(
                                cell,
                                mask,
                                masks);
                    }
                }
                else
                {
                    type = CityRoadModuleType.CurveSmall;
                }
            }
            else if (degree == 3)
            {
                type = CityRoadModuleType.TIntersection;
            }
            else
            {
                type = CityRoadModuleType.CrossIntersection;
            }

            return ModuleDefinitions.TrySolveQuarterTurns(
                type,
                connections,
                preferredCrosswalkEdge,
                out quarterTurns);
        }

        static bool HasAdjacentIntersection(
            GridCoord cell,
            int mask,
            IReadOnlyDictionary<GridCoord, int> masks)
        {
            for (int i = 0; i < Directions.Length; i++)
            {
                int bit = 1 << i;
                if ((mask & bit) == 0)
                    continue;
                GridCoord neighbor = cell + Directions[i];
                if (masks.TryGetValue(neighbor, out int neighborMask)
                    && CountBits(neighborMask) >= 3)
                {
                    return true;
                }
            }
            return false;
        }

        static CityRoadConnectionMask FindAdjacentIntersectionDirection(
            GridCoord cell,
            int mask,
            IReadOnlyDictionary<GridCoord, int> masks)
        {
            for (int i = 0; i < Directions.Length; i++)
            {
                int bit = 1 << i;
                if ((mask & bit) == 0)
                    continue;
                GridCoord neighbor = cell + Directions[i];
                if (masks.TryGetValue(neighbor, out int neighborMask)
                    && CountBits(neighborMask) >= 3)
                {
                    return (CityRoadConnectionMask)bit;
                }
            }
            return CityRoadConnectionMask.None;
        }

        static int CountBits(int value)
        {
            int count = 0;
            while (value != 0)
            {
                count += value & 1;
                value >>= 1;
            }
            return count;
        }

        static GridCoord Rotate(GridCoord direction, int sign)
        {
            return sign < 0
                ? new GridCoord(direction.Y, -direction.X)
                : new GridCoord(-direction.Y, direction.X);
        }

        static void RemoveIsolatedCells(HashSet<GridCoord> occupied)
        {
            GridCoord[] isolated = occupied
                .Where(value => GetNeighborMask(value, occupied) == 0)
                .ToArray();
            for (int i = 0; i < isolated.Length; i++)
                occupied.Remove(isolated[i]);
        }

        static int EnsureSingleConnectedComponent(
            HashSet<GridCoord> occupied,
            HashSet<GridCoord> candidates,
            GridCoord seed)
        {
            int repairCount = 0;
            while (occupied.Count > 0)
            {
                HashSet<GridCoord> main =
                    CollectOccupiedComponent(
                        occupied.Contains(seed)
                            ? seed
                            : occupied
                                .OrderBy(value => value.X)
                                .ThenBy(value => value.Y)
                                .First(),
                        occupied);
                if (main.Count == occupied.Count)
                    return repairCount;

                var queue = new Queue<GridCoord>();
                var visited = new HashSet<GridCoord>();
                var parent = new Dictionary<GridCoord, GridCoord>();
                foreach (GridCoord cell in main
                             .OrderBy(value => value.X)
                             .ThenBy(value => value.Y))
                {
                    queue.Enqueue(cell);
                    visited.Add(cell);
                }

                GridCoord target = default;
                bool found = false;
                while (queue.Count > 0 && !found)
                {
                    GridCoord current = queue.Dequeue();
                    for (int direction = 0;
                         direction < Directions.Length;
                         direction++)
                    {
                        GridCoord next = current + Directions[direction];
                        if (!candidates.Contains(next)
                            || !visited.Add(next))
                        {
                            continue;
                        }
                        parent[next] = current;
                        if (occupied.Contains(next)
                            && !main.Contains(next))
                        {
                            target = next;
                            found = true;
                            break;
                        }
                        queue.Enqueue(next);
                    }
                }

                if (!found)
                {
                    GridCoord[] disconnected = occupied
                        .Where(value => !main.Contains(value))
                        .ToArray();
                    for (int i = 0; i < disconnected.Length; i++)
                    {
                        occupied.Remove(disconnected[i]);
                        repairCount++;
                    }
                    return repairCount;
                }

                GridCoord pathCell = target;
                while (!main.Contains(pathCell))
                {
                    if (occupied.Add(pathCell))
                        repairCount++;
                    pathCell = parent[pathCell];
                }
            }

            return repairCount;
        }

        static HashSet<GridCoord> CollectOccupiedComponent(
            GridCoord start,
            HashSet<GridCoord> occupied)
        {
            var component = new HashSet<GridCoord>();
            var queue = new Queue<GridCoord>();
            component.Add(start);
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                GridCoord current = queue.Dequeue();
                for (int i = 0; i < Directions.Length; i++)
                {
                    GridCoord neighbor = current + Directions[i];
                    if (occupied.Contains(neighbor)
                        && component.Add(neighbor))
                    {
                        queue.Enqueue(neighbor);
                    }
                }
            }
            return component;
        }

        static int CountConnectedComponents(
            IReadOnlyDictionary<Vector2Int, CityRoadModulePlacement> modules)
        {
            var visited = new HashSet<Vector2Int>();
            int count = 0;
            foreach (Vector2Int start in modules.Keys
                         .OrderBy(value => value.x)
                         .ThenBy(value => value.y))
            {
                if (!visited.Add(start))
                    continue;
                count++;
                var queue = new Queue<Vector2Int>();
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    Vector2Int current = queue.Dequeue();
                    CityRoadModulePlacement module = modules[current];
                    for (int direction = 0;
                         direction < Directions.Length;
                         direction++)
                    {
                        CityRoadConnectionMask mask =
                            (CityRoadConnectionMask)(1 << direction);
                        if ((module.ConnectionMask & mask) == 0)
                            continue;
                        GridCoord offset = Directions[direction];
                        var neighbor = new Vector2Int(
                            current.x + offset.X,
                            current.y + offset.Y);
                        if (modules.ContainsKey(neighbor)
                            && visited.Add(neighbor))
                        {
                            queue.Enqueue(neighbor);
                        }
                    }
                }
            }
            return count;
        }

        static bool OverlapsBuildings(
            IReadOnlyList<Vector2> square,
            IReadOnlyList<CityBuildingData> buildings,
            int firstBuilding)
        {
            for (int i = firstBuilding; i < buildings.Count; i++)
            {
                if (PolygonsOverlap(square, buildings[i].Footprint))
                    return true;
            }
            return false;
        }

        static bool OverlapsRoadModules(
            IReadOnlyList<Vector2> polygon,
            IReadOnlyList<CityRoadModulePlacement> roadModules,
            int regionIndex,
            CityModularGridBasis basis,
            float cellSize)
        {
            for (int i = 0; i < roadModules.Count; i++)
            {
                CityRoadModulePlacement module = roadModules[i];
                if (module.RegionIndex != regionIndex)
                    continue;
                float half = Mathf.Max(
                    0.01f,
                    cellSize * module.FootprintCells * 0.5f - 0.01f);
                Vector2 x = basis.AxisX * half;
                Vector2 y = basis.AxisY * half;
                Vector2[] roadFootprint =
                {
                    module.Position - x - y,
                    module.Position + x - y,
                    module.Position + x + y,
                    module.Position - x + y
                };
                if (PolygonsOverlap(polygon, roadFootprint))
                    return true;
            }
            return false;
        }

        static bool OverlapsRoadGrid(
            IReadOnlyList<Vector2> polygon,
            CityModularGridBasis basis,
            float roadCellSize,
            HashSet<GridCoord> occupiedRoadCells)
        {
            float minimumX = float.MaxValue;
            float maximumX = float.MinValue;
            float minimumY = float.MaxValue;
            float maximumY = float.MinValue;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 grid =
                    basis.ToGrid(polygon[i], roadCellSize);
                minimumX = Mathf.Min(minimumX, grid.x);
                maximumX = Mathf.Max(maximumX, grid.x);
                minimumY = Mathf.Min(minimumY, grid.y);
                maximumY = Mathf.Max(maximumY, grid.y);
            }

            int firstX = Mathf.FloorToInt(minimumX + 0.5f);
            int lastX = Mathf.CeilToInt(maximumX - 0.5f);
            int firstY = Mathf.FloorToInt(minimumY + 0.5f);
            int lastY = Mathf.CeilToInt(maximumY - 0.5f);
            for (int y = firstY; y <= lastY; y++)
            {
                for (int x = firstX; x <= lastX; x++)
                {
                    if (occupiedRoadCells.Contains(
                            new GridCoord(x, y)))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        static bool PolygonsOverlap(
            IReadOnlyList<Vector2> first,
            IReadOnlyList<Vector2> second)
        {
            for (int i = 0; i < first.Count; i++)
            {
                Vector2 a0 = first[i];
                Vector2 a1 = first[(i + 1) % first.Count];
                for (int j = 0; j < second.Count; j++)
                {
                    Vector2 b0 = second[j];
                    Vector2 b1 = second[(j + 1) % second.Count];
                    if (SegmentsIntersect(a0, a1, b0, b1))
                        return true;
                }
            }
            return CityPolygonGeometry.ContainsPoint(first, second[0])
                || CityPolygonGeometry.ContainsPoint(second, first[0]);
        }

        static bool SegmentsIntersect(
            Vector2 a,
            Vector2 b,
            Vector2 c,
            Vector2 d)
        {
            float denominator = Cross(b - a, d - c);
            if (Mathf.Abs(denominator) < 0.00001f)
                return false;
            float t = Cross(c - a, d - c) / denominator;
            float u = Cross(c - a, b - a) / denominator;
            return t >= 0f && t <= 1f && u >= 0f && u <= 1f;
        }

        static float Cross(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        static Vector2 Average(IReadOnlyList<Vector2> points)
        {
            Vector2 sum = Vector2.zero;
            for (int i = 0; i < points.Count; i++)
                sum += points[i];
            return points.Count == 0 ? Vector2.zero : sum / points.Count;
        }

        static Vector2 FindLongestEdge(IReadOnlyList<Vector2> polygon)
        {
            Vector2 best = Vector2.right;
            float bestLength = 0f;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 edge =
                    polygon[(i + 1) % polygon.Count] - polygon[i];
                if (edge.sqrMagnitude > bestLength)
                {
                    bestLength = edge.sqrMagnitude;
                    best = edge.normalized;
                }
            }
            if (best.x < -0.0001f
                || (Mathf.Abs(best.x) <= 0.0001f && best.y < 0f))
            {
                best = -best;
            }
            return best;
        }

        static int StableHash(int seed, int region, int x, int y)
        {
            unchecked
            {
                int hash = seed;
                hash = hash * 397 ^ region;
                hash = hash * 397 ^ x;
                hash = hash * 397 ^ y;
                return hash == int.MinValue ? 0 : Math.Abs(hash);
            }
        }

        readonly struct GridCoord : IEquatable<GridCoord>
        {
            public readonly int X;
            public readonly int Y;

            public GridCoord(int x, int y)
            {
                X = x;
                Y = y;
            }

            public bool Equals(GridCoord other)
            {
                return X == other.X && Y == other.Y;
            }

            public override bool Equals(object obj)
            {
                return obj is GridCoord other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (X * 397) ^ Y;
                }
            }

            public static GridCoord operator +(
                GridCoord left,
                GridCoord right)
            {
                return new GridCoord(
                    left.X + right.X,
                    left.Y + right.Y);
            }
        }
    }
}
