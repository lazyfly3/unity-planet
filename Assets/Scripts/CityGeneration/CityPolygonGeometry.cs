using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityGeneration
{
    public static class CityPolygonGeometry
    {
        const float Epsilon = 0.0001f;

        readonly struct EdgeKey : IEquatable<EdgeKey>, IComparable<EdgeKey>
        {
            public readonly int A;
            public readonly int B;

            public EdgeKey(int a, int b)
            {
                A = Mathf.Min(a, b);
                B = Mathf.Max(a, b);
            }

            public int CompareTo(EdgeKey other)
            {
                int first = A.CompareTo(other.A);
                return first != 0 ? first : B.CompareTo(other.B);
            }

            public bool Equals(EdgeKey other)
            {
                return A == other.A && B == other.B;
            }

            public override bool Equals(object obj)
            {
                return obj is EdgeKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return A * 397 ^ B;
            }
        }

        readonly struct DirectedEdgeKey : IEquatable<DirectedEdgeKey>
        {
            public readonly int From;
            public readonly int To;

            public DirectedEdgeKey(int from, int to)
            {
                From = from;
                To = to;
            }

            public bool Equals(DirectedEdgeKey other)
            {
                return From == other.From && To == other.To;
            }

            public override bool Equals(object obj)
            {
                return obj is DirectedEdgeKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return From * 397 ^ To;
            }
        }

        readonly struct PointKey : IEquatable<PointKey>
        {
            readonly long x;
            readonly long y;

            public PointKey(Vector2 point, float tolerance)
            {
                double scale = 1d / Math.Max(0.000001d, tolerance);
                x = (long)Math.Round(point.x * scale);
                y = (long)Math.Round(point.y * scale);
            }

            public bool Equals(PointKey other)
            {
                return x == other.x && y == other.y;
            }

            public override bool Equals(object obj)
            {
                return obj is PointKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return x.GetHashCode() * 397 ^ y.GetHashCode();
            }
        }

        public static bool ValidateBoundary(
            IReadOnlyList<Vector2> points,
            CityGenerationSettings settings,
            out string error)
        {
            return ResolveBoundary(points, settings, out _, out error);
        }

        public static bool ResolveClosedRegions(
            IReadOnlyList<Vector2> points,
            CityGenerationSettings settings,
            out List<List<Vector2>> regions,
            out string error)
        {
            bool success = ResolveBoundary(
                points,
                settings,
                out CityBoundaryResolution resolution,
                out error);
            regions = success
                ? new List<List<Vector2>>(resolution.Regions)
                : new List<List<Vector2>>();
            return success;
        }

        public static bool ResolveBoundary(
            IReadOnlyList<Vector2> points,
            CityGenerationSettings settings,
            out CityBoundaryResolution resolution,
            out string error)
        {
            CityGenerationSettings safeSettings =
                settings == null
                    ? new CityGenerationSettings()
                    : settings.ValidatedCopy();
            var source = points == null
                ? new List<Vector2>()
                : new List<Vector2>(points);
            var accepted = new List<List<Vector2>>();
            var ignored = new List<List<Vector2>>();
            resolution = new CityBoundaryResolution
            {
                SourcePoints = source,
                Regions = accepted,
                IgnoredRegions = ignored,
                Message = string.Empty
            };

            if (source.Count < 3)
            {
                error = "至少需要选择 3 个边界点。";
                return false;
            }
            if (source.Count > safeSettings.maximumBoundaryPoints)
            {
                error =
                    $"边界点不能超过 {safeSettings.maximumBoundaryPoints} 个，请撤销部分点。";
                return false;
            }

            List<Vector2> clean = SanitizePath(
                source,
                safeSettings.boundarySnapTolerance,
                out int collapsedCount);
            resolution.CollapsedPointCount = collapsedCount;
            if (CountUnique(clean, safeSettings.boundarySnapTolerance) < 3)
            {
                error = "有效边界点不足 3 个。";
                return false;
            }

            BuildPlanarGraph(
                clean,
                safeSettings.boundarySnapTolerance,
                out List<Vector2> graphPoints,
                out List<EdgeKey> graphEdges,
                out int intersectionCount);
            resolution.IntersectionCount = intersectionCount;

            var faces = new List<List<Vector2>>();
            ExtractBoundedFaces(
                graphPoints,
                graphEdges,
                safeSettings.boundarySnapTolerance,
                faces);
            RemoveDuplicateAndNestedFaces(
                faces,
                safeSettings.boundarySnapTolerance);

            float requiredClearance = Mathf.Max(
                safeSettings.majorRoadWidth,
                safeSettings.minimumLotFrontage) * 0.5f
                + safeSettings.sidewalkWidth
                + safeSettings.buildingSetback;
            for (int i = 0; i < faces.Count; i++)
            {
                List<Vector2> face = faces[i];
                if (IsDevelopable(
                        face,
                        safeSettings.minimumBoundaryArea,
                        requiredClearance))
                {
                    accepted.Add(face);
                }
                else
                {
                    ignored.Add(face);
                }
            }

            SortRegions(accepted);
            SortRegions(ignored);
            if (accepted.Count == 0)
            {
                List<Vector2> convexHull = BuildConvexHull(
                    clean,
                    safeSettings.boundarySnapTolerance);
                List<Vector2> repaired = BuildConcaveHull(
                    clean,
                    convexHull,
                    safeSettings.boundarySnapTolerance);

                if (IsDevelopable(
                        repaired,
                        safeSettings.minimumBoundaryArea,
                        requiredClearance))
                {
                    accepted.Add(repaired);
                    resolution.RepairMode =
                        repaired.Count > convexHull.Count
                            ? CityBoundaryRepairMode.ConcaveHull
                            : CityBoundaryRepairMode.ConvexHull;
                }
                else if (IsDevelopable(
                             convexHull,
                             safeSettings.minimumBoundaryArea,
                             requiredClearance))
                {
                    accepted.Add(convexHull);
                    resolution.RepairMode = CityBoundaryRepairMode.ConvexHull;
                }
            }

            if (accepted.Count == 0)
            {
                error = ignored.Count > 0
                    ? "识别出的闭合区域都过小或过窄，无法容纳城市。"
                    : "这些边界点无法形成具有足够面积的建设范围。";
                return false;
            }

            float totalArea = 0f;
            for (int i = 0; i < accepted.Count; i++)
                totalArea += Mathf.Abs(SignedArea(accepted[i]));
            resolution.TotalArea = totalArea;
            resolution.Message = resolution.WasAutoRepaired
                ? "边界未形成可建闭合区，已自动生成建设范围。"
                : ignored.Count > 0
                    ? $"识别 {accepted.Count} 个可建区域，忽略 {ignored.Count} 个狭小区域。"
                    : $"识别 {accepted.Count} 个可建闭合区域。";
            error = string.Empty;
            return true;
        }

        public static float SignedArea(IReadOnlyList<Vector2> polygon)
        {
            if (polygon == null || polygon.Count < 3)
                return 0f;

            double sum = 0d;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[(i + 1) % polygon.Count];
                sum += (double)a.x * b.y - (double)b.x * a.y;
            }
            return (float)(sum * 0.5d);
        }

        public static bool TryTriangulate(
            IReadOnlyList<Vector2> polygon,
            out List<int> triangles)
        {
            triangles = new List<int>();
            if (polygon == null
                || polygon.Count < 3
                || HasSelfIntersection(polygon)
                || Mathf.Abs(SignedArea(polygon)) <= Epsilon)
            {
                return false;
            }

            var remaining = new List<int>(polygon.Count);
            if (SignedArea(polygon) > 0f)
            {
                for (int i = 0; i < polygon.Count; i++)
                    remaining.Add(i);
            }
            else
            {
                for (int i = polygon.Count - 1; i >= 0; i--)
                    remaining.Add(i);
            }

            int guard = polygon.Count * polygon.Count;
            while (remaining.Count > 3 && guard-- > 0)
            {
                bool clipped = false;
                for (int i = 0; i < remaining.Count; i++)
                {
                    int previous =
                        remaining[(i - 1 + remaining.Count) % remaining.Count];
                    int current = remaining[i];
                    int next = remaining[(i + 1) % remaining.Count];
                    Vector2 a = polygon[previous];
                    Vector2 b = polygon[current];
                    Vector2 c = polygon[next];
                    if (Cross(b - a, c - b) <= Epsilon)
                        continue;

                    bool containsVertex = false;
                    for (int candidateIndex = 0;
                         candidateIndex < remaining.Count;
                         candidateIndex++)
                    {
                        int candidate = remaining[candidateIndex];
                        if (candidate == previous
                            || candidate == current
                            || candidate == next)
                        {
                            continue;
                        }
                        if (PointInTriangle(polygon[candidate], a, b, c))
                        {
                            containsVertex = true;
                            break;
                        }
                    }
                    if (containsVertex)
                        continue;

                    triangles.Add(previous);
                    triangles.Add(current);
                    triangles.Add(next);
                    remaining.RemoveAt(i);
                    clipped = true;
                    break;
                }

                if (!clipped)
                {
                    triangles.Clear();
                    return false;
                }
            }

            if (remaining.Count != 3)
            {
                triangles.Clear();
                return false;
            }
            triangles.Add(remaining[0]);
            triangles.Add(remaining[1]);
            triangles.Add(remaining[2]);
            return true;
        }

        public static Vector2 Centroid(IReadOnlyList<Vector2> polygon)
        {
            float signedArea = SignedArea(polygon);
            if (Mathf.Abs(signedArea) < Epsilon)
                return Average(polygon);

            double x = 0d;
            double y = 0d;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[(i + 1) % polygon.Count];
                double cross = (double)a.x * b.y - (double)b.x * a.y;
                x += (a.x + b.x) * cross;
                y += (a.y + b.y) * cross;
            }

            double denominator = 6d * signedArea;
            return new Vector2(
                (float)(x / denominator),
                (float)(y / denominator));
        }

        public static bool ContainsPoint(
            IReadOnlyList<Vector2> polygon,
            Vector2 point)
        {
            if (polygon == null || polygon.Count < 3)
                return false;

            bool inside = false;
            for (int i = 0, j = polygon.Count - 1;
                 i < polygon.Count;
                 j = i++)
            {
                Vector2 a = polygon[j];
                Vector2 b = polygon[i];
                if (DistancePointToSegment(point, a, b) <= 0.001f)
                    return true;

                bool crosses = (a.y > point.y) != (b.y > point.y);
                if (crosses)
                {
                    float intersectionX =
                        (b.x - a.x) * (point.y - a.y)
                        / (b.y - a.y)
                        + a.x;
                    if (point.x < intersectionX)
                        inside = !inside;
                }
            }
            return inside;
        }

        public static bool ContainsSegment(
            IReadOnlyList<Vector2> polygon,
            Vector2 start,
            Vector2 end)
        {
            if (!ContainsPoint(polygon, start)
                || !ContainsPoint(polygon, end))
            {
                return false;
            }

            int sampleCount = Mathf.Max(
                12,
                Mathf.CeilToInt(Vector2.Distance(start, end) / 2f));
            for (int i = 1; i < sampleCount; i++)
            {
                if (!ContainsPoint(
                        polygon,
                        Vector2.Lerp(start, end, i / (float)sampleCount)))
                {
                    return false;
                }
            }
            return true;
        }

        public static bool ContainsPolygon(
            IReadOnlyList<Vector2> boundary,
            IReadOnlyList<Vector2> candidate)
        {
            if (candidate == null || candidate.Count < 3)
                return false;

            for (int i = 0; i < candidate.Count; i++)
            {
                Vector2 a = candidate[i];
                Vector2 b = candidate[(i + 1) % candidate.Count];
                if (!ContainsPoint(boundary, a)
                    || !ContainsSegment(boundary, a, b))
                {
                    return false;
                }
            }
            return ContainsPoint(boundary, Average(candidate));
        }

        public static bool HasSelfIntersection(
            IReadOnlyList<Vector2> polygon)
        {
            if (polygon == null || polygon.Count < 4)
                return false;

            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[(i + 1) % polygon.Count];
                for (int j = i + 1; j < polygon.Count; j++)
                {
                    int nextJ = (j + 1) % polygon.Count;
                    if (i == j
                        || (i + 1) % polygon.Count == j
                        || i == nextJ)
                    {
                        continue;
                    }
                    if (SegmentsIntersect(a, b, polygon[j], polygon[nextJ]))
                        return true;
                }
            }
            return false;
        }

        public static bool SegmentsIntersect(
            Vector2 a,
            Vector2 b,
            Vector2 c,
            Vector2 d)
        {
            double abC = CrossDouble(b - a, c - a);
            double abD = CrossDouble(b - a, d - a);
            double cdA = CrossDouble(d - c, a - c);
            double cdB = CrossDouble(d - c, b - c);

            if (((abC > Epsilon && abD < -Epsilon)
                 || (abC < -Epsilon && abD > Epsilon))
                && ((cdA > Epsilon && cdB < -Epsilon)
                    || (cdA < -Epsilon && cdB > Epsilon)))
            {
                return true;
            }

            return Math.Abs(abC) <= Epsilon && IsOnSegment(a, b, c)
                || Math.Abs(abD) <= Epsilon && IsOnSegment(a, b, d)
                || Math.Abs(cdA) <= Epsilon && IsOnSegment(c, d, a)
                || Math.Abs(cdB) <= Epsilon && IsOnSegment(c, d, b);
        }

        public static float DistancePointToSegment(
            Vector2 point,
            Vector2 a,
            Vector2 b)
        {
            Vector2 segment = b - a;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= Epsilon)
                return Vector2.Distance(point, a);
            float t = Mathf.Clamp01(
                Vector2.Dot(point - a, segment) / lengthSquared);
            return Vector2.Distance(point, a + segment * t);
        }

        static List<Vector2> SanitizePath(
            IReadOnlyList<Vector2> source,
            float tolerance,
            out int collapsedCount)
        {
            var clean = new List<Vector2>(source.Count);
            collapsedCount = 0;
            for (int i = 0; i < source.Count; i++)
            {
                Vector2 point = source[i];
                for (int existing = 0; existing < clean.Count; existing++)
                {
                    if (Vector2.Distance(clean[existing], point) <= tolerance)
                    {
                        point = clean[existing];
                        break;
                    }
                }

                if (clean.Count > 0
                    && Vector2.Distance(clean[clean.Count - 1], point)
                    <= tolerance)
                {
                    collapsedCount++;
                    continue;
                }
                clean.Add(point);
            }

            if (clean.Count > 1
                && Vector2.Distance(clean[0], clean[clean.Count - 1])
                <= tolerance)
            {
                clean.RemoveAt(clean.Count - 1);
                collapsedCount++;
            }
            return clean;
        }

        static int CountUnique(
            IReadOnlyList<Vector2> points,
            float tolerance)
        {
            var unique = new List<Vector2>();
            for (int i = 0; i < points.Count; i++)
            {
                bool found = false;
                for (int j = 0; j < unique.Count; j++)
                {
                    if (Vector2.Distance(points[i], unique[j]) <= tolerance)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                    unique.Add(points[i]);
            }
            return unique.Count;
        }

        static void BuildPlanarGraph(
            IReadOnlyList<Vector2> path,
            float tolerance,
            out List<Vector2> graphPoints,
            out List<EdgeKey> graphEdges,
            out int intersectionCount)
        {
            int segmentCount = path.Count;
            var splitParameters = new List<double>[segmentCount];
            for (int i = 0; i < segmentCount; i++)
                splitParameters[i] = new List<double> { 0d, 1d };

            var intersectionKeys = new HashSet<PointKey>();
            for (int i = 0; i < segmentCount; i++)
            {
                Vector2 a = path[i];
                Vector2 b = path[(i + 1) % segmentCount];
                if (Vector2.Distance(a, b) <= tolerance * 0.25f)
                    continue;

                for (int j = i + 1; j < segmentCount; j++)
                {
                    Vector2 c = path[j];
                    Vector2 d = path[(j + 1) % segmentCount];
                    if (Vector2.Distance(c, d) <= tolerance * 0.25f)
                        continue;

                    AddSegmentNoding(
                        a,
                        b,
                        c,
                        d,
                        splitParameters[i],
                        splitParameters[j],
                        tolerance,
                        intersectionKeys);
                }
            }

            graphPoints = new List<Vector2>();
            var edgeSet = new HashSet<EdgeKey>();
            for (int i = 0; i < segmentCount; i++)
            {
                splitParameters[i].Sort();
                Vector2 start = path[i];
                Vector2 end = path[(i + 1) % segmentCount];
                for (int split = 0;
                     split + 1 < splitParameters[i].Count;
                     split++)
                {
                    Vector2 fromPoint = Vector2.Lerp(
                        start,
                        end,
                        (float)splitParameters[i][split]);
                    Vector2 toPoint = Vector2.Lerp(
                        start,
                        end,
                        (float)splitParameters[i][split + 1]);
                    if (Vector2.Distance(fromPoint, toPoint)
                        <= tolerance * 0.25f)
                    {
                        continue;
                    }

                    int from = GetOrAddGraphPoint(
                        graphPoints,
                        fromPoint,
                        tolerance);
                    int to = GetOrAddGraphPoint(
                        graphPoints,
                        toPoint,
                        tolerance);
                    if (from != to)
                        edgeSet.Add(new EdgeKey(from, to));
                }
            }

            graphEdges = new List<EdgeKey>(edgeSet);
            graphEdges.Sort();
            intersectionCount = intersectionKeys.Count;
        }

        static void AddSegmentNoding(
            Vector2 a,
            Vector2 b,
            Vector2 c,
            Vector2 d,
            List<double> firstParameters,
            List<double> secondParameters,
            float tolerance,
            HashSet<PointKey> intersections)
        {
            Vector2 first = b - a;
            Vector2 second = d - c;
            double denominator = CrossDouble(first, second);
            double scale = Math.Max(first.magnitude, second.magnitude);
            double parallelTolerance = Math.Max(
                1e-10d,
                tolerance * 1e-5d * scale);
            if (Math.Abs(denominator) > parallelTolerance)
            {
                Vector2 offset = c - a;
                double t = CrossDouble(offset, second) / denominator;
                double u = CrossDouble(offset, first) / denominator;
                double parameterTolerance =
                    tolerance / Math.Max(0.001d, scale);
                if (t < -parameterTolerance
                    || t > 1d + parameterTolerance
                    || u < -parameterTolerance
                    || u > 1d + parameterTolerance)
                {
                    return;
                }

                t = Clamp01(t);
                u = Clamp01(u);
                AddUniqueParameter(firstParameters, t);
                AddUniqueParameter(secondParameters, u);
                bool firstInterior = t > parameterTolerance
                    && t < 1d - parameterTolerance;
                bool secondInterior = u > parameterTolerance
                    && u < 1d - parameterTolerance;
                if (firstInterior || secondInterior)
                {
                    intersections.Add(new PointKey(
                        a + first * (float)t,
                        tolerance));
                }
                return;
            }

            double firstLengthSquared = first.sqrMagnitude;
            if (firstLengthSquared <= 1e-12d
                || Math.Abs(CrossDouble(first, c - a))
                > parallelTolerance
                || Math.Abs(CrossDouble(first, d - a))
                > parallelTolerance)
            {
                return;
            }

            double secondLengthSquared = second.sqrMagnitude;
            AddProjectedEndpoint(
                c,
                a,
                first,
                firstLengthSquared,
                firstParameters,
                tolerance,
                intersections);
            AddProjectedEndpoint(
                d,
                a,
                first,
                firstLengthSquared,
                firstParameters,
                tolerance,
                intersections);
            AddProjectedEndpoint(
                a,
                c,
                second,
                secondLengthSquared,
                secondParameters,
                tolerance,
                intersections);
            AddProjectedEndpoint(
                b,
                c,
                second,
                secondLengthSquared,
                secondParameters,
                tolerance,
                intersections);
        }

        static void AddProjectedEndpoint(
            Vector2 point,
            Vector2 start,
            Vector2 direction,
            double lengthSquared,
            List<double> parameters,
            float tolerance,
            HashSet<PointKey> intersections)
        {
            if (lengthSquared <= 1e-12d)
                return;
            double t = Vector2.Dot(point - start, direction) / lengthSquared;
            double parameterTolerance =
                tolerance / Math.Max(0.001d, Math.Sqrt(lengthSquared));
            if (t < -parameterTolerance || t > 1d + parameterTolerance)
                return;
            t = Clamp01(t);
            AddUniqueParameter(parameters, t);
            if (t > parameterTolerance && t < 1d - parameterTolerance)
                intersections.Add(new PointKey(point, tolerance));
        }

        static void ExtractBoundedFaces(
            IReadOnlyList<Vector2> points,
            IReadOnlyList<EdgeKey> edges,
            float tolerance,
            List<List<Vector2>> faces)
        {
            var adjacency = new List<int>[points.Count];
            for (int i = 0; i < adjacency.Length; i++)
                adjacency[i] = new List<int>();
            for (int i = 0; i < edges.Count; i++)
            {
                EdgeKey edge = edges[i];
                adjacency[edge.A].Add(edge.B);
                adjacency[edge.B].Add(edge.A);
            }

            for (int i = 0; i < adjacency.Length; i++)
            {
                int center = i;
                adjacency[i].Sort((left, right) =>
                {
                    Vector2 leftDirection = points[left] - points[center];
                    Vector2 rightDirection = points[right] - points[center];
                    int angle = Math.Atan2(leftDirection.y, leftDirection.x)
                        .CompareTo(Math.Atan2(
                            rightDirection.y,
                            rightDirection.x));
                    if (angle != 0)
                        return angle;
                    int length = leftDirection.sqrMagnitude.CompareTo(
                        rightDirection.sqrMagnitude);
                    return length != 0 ? length : left.CompareTo(right);
                });
            }

            var visited = new HashSet<DirectedEdgeKey>();
            int traversalLimit = Mathf.Max(32, edges.Count * 4 + 8);
            for (int edgeIndex = 0; edgeIndex < edges.Count; edgeIndex++)
            {
                TraceFace(edges[edgeIndex].A, edges[edgeIndex].B);
                TraceFace(edges[edgeIndex].B, edges[edgeIndex].A);
            }

            void TraceFace(int startFrom, int startTo)
            {
                var firstEdge = new DirectedEdgeKey(startFrom, startTo);
                if (visited.Contains(firstEdge))
                    return;

                var indices = new List<int>();
                int from = startFrom;
                int to = startTo;
                bool closed = false;
                for (int guard = 0; guard < traversalLimit; guard++)
                {
                    var directed = new DirectedEdgeKey(from, to);
                    if (visited.Contains(directed))
                    {
                        closed = from == startFrom && to == startTo;
                        break;
                    }

                    visited.Add(directed);
                    indices.Add(from);
                    List<int> neighbors = adjacency[to];
                    int incoming = neighbors.IndexOf(from);
                    if (incoming < 0 || neighbors.Count < 2)
                        return;
                    int nextIndex =
                        (incoming - 1 + neighbors.Count) % neighbors.Count;
                    int next = neighbors[nextIndex];
                    from = to;
                    to = next;
                    if (from == startFrom && to == startTo)
                    {
                        closed = true;
                        break;
                    }
                }
                if (!closed || indices.Count < 3)
                    return;
                SplitRepeatedVertexWalk(indices, AddSimpleFace);
            }

            void AddSimpleFace(List<int> indices)
            {
                if (indices.Count < 3)
                    return;
                var face = new List<Vector2>(indices.Count);
                for (int i = 0; i < indices.Count; i++)
                    face.Add(points[indices[i]]);
                RemoveCollinearVertices(face, tolerance);
                if (face.Count < 3
                    || SignedArea(face) <= Epsilon
                    || HasSelfIntersection(face))
                {
                    return;
                }
                faces.Add(face);
            }
        }

        static void SplitRepeatedVertexWalk(
            List<int> walk,
            Action<List<int>> addCycle)
        {
            for (int first = 0; first < walk.Count; first++)
            {
                for (int second = first + 1; second < walk.Count; second++)
                {
                    if (walk[first] != walk[second])
                        continue;

                    var inner = walk.GetRange(first, second - first);
                    var outer = new List<int>(walk.Count - inner.Count);
                    for (int i = 0; i <= first; i++)
                        outer.Add(walk[i]);
                    for (int i = second + 1; i < walk.Count; i++)
                        outer.Add(walk[i]);
                    SplitRepeatedVertexWalk(inner, addCycle);
                    SplitRepeatedVertexWalk(outer, addCycle);
                    return;
                }
            }
            addCycle(walk);
        }

        static void RemoveDuplicateAndNestedFaces(
            List<List<Vector2>> faces,
            float tolerance)
        {
            SortRegions(faces);
            var unique = new List<List<Vector2>>();
            var keys = new HashSet<string>();
            for (int i = 0; i < faces.Count; i++)
            {
                string key = BuildCanonicalPolygonKey(faces[i], tolerance);
                if (keys.Add(key))
                    unique.Add(faces[i]);
            }

            unique.Sort((left, right) =>
                Mathf.Abs(SignedArea(right)).CompareTo(
                    Mathf.Abs(SignedArea(left))));
            faces.Clear();
            for (int i = 0; i < unique.Count; i++)
            {
                bool nested = false;
                Vector2 probe = FindInteriorProbe(unique[i]);
                for (int kept = 0; kept < faces.Count; kept++)
                {
                    if (ContainsPoint(faces[kept], probe))
                    {
                        nested = true;
                        break;
                    }
                }
                if (!nested)
                    faces.Add(unique[i]);
            }
            SortRegions(faces);
        }

        static string BuildCanonicalPolygonKey(
            IReadOnlyList<Vector2> polygon,
            float tolerance)
        {
            int first = 0;
            for (int i = 1; i < polygon.Count; i++)
            {
                if (polygon[i].x < polygon[first].x
                    || Mathf.Approximately(polygon[i].x, polygon[first].x)
                    && polygon[i].y < polygon[first].y)
                {
                    first = i;
                }
            }

            var key = new System.Text.StringBuilder(polygon.Count * 16);
            float scale = 1f / Mathf.Max(0.0001f, tolerance);
            for (int offset = 0; offset < polygon.Count; offset++)
            {
                Vector2 point = polygon[(first + offset) % polygon.Count];
                key.Append(Mathf.RoundToInt(point.x * scale));
                key.Append(',');
                key.Append(Mathf.RoundToInt(point.y * scale));
                key.Append(';');
            }
            return key.ToString();
        }

        static bool IsDevelopable(
            IReadOnlyList<Vector2> polygon,
            float minimumArea,
            float requiredClearance)
        {
            if (polygon == null
                || polygon.Count < 3
                || Mathf.Abs(SignedArea(polygon)) < minimumArea
                || HasSelfIntersection(polygon))
            {
                return false;
            }

            requiredClearance = Mathf.Max(0.5f, requiredClearance);
            Vector2 centroid = Centroid(polygon);
            if (ContainsPoint(polygon, centroid)
                && DistanceToBoundary(centroid, polygon) >= requiredClearance)
            {
                return true;
            }

            if (TryTriangulate(polygon, out List<int> triangles))
            {
                for (int i = 0; i < triangles.Count; i += 3)
                {
                    Vector2 probe = (
                        polygon[triangles[i]]
                        + polygon[triangles[i + 1]]
                        + polygon[triangles[i + 2]]) / 3f;
                    if (DistanceToBoundary(probe, polygon)
                        >= requiredClearance)
                    {
                        return true;
                    }
                }
            }

            GetBounds(polygon, out Vector2 minimum, out Vector2 maximum);
            float step = Mathf.Max(1f, requiredClearance * 0.5f);
            for (float y = minimum.y + step * 0.5f;
                 y <= maximum.y;
                 y += step)
            {
                for (float x = minimum.x + step * 0.5f;
                     x <= maximum.x;
                     x += step)
                {
                    var probe = new Vector2(x, y);
                    if (ContainsPoint(polygon, probe)
                        && DistanceToBoundary(probe, polygon)
                        >= requiredClearance)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        static float DistanceToBoundary(
            Vector2 point,
            IReadOnlyList<Vector2> polygon)
        {
            float distance = float.MaxValue;
            for (int i = 0; i < polygon.Count; i++)
            {
                distance = Mathf.Min(
                    distance,
                    DistancePointToSegment(
                        point,
                        polygon[i],
                        polygon[(i + 1) % polygon.Count]));
            }
            return distance;
        }

        static List<Vector2> BuildConvexHull(
            IReadOnlyList<Vector2> source,
            float tolerance)
        {
            var points = new List<Vector2>();
            for (int i = 0; i < source.Count; i++)
            {
                bool duplicate = false;
                for (int j = 0; j < points.Count; j++)
                {
                    if (Vector2.Distance(source[i], points[j]) <= tolerance)
                    {
                        duplicate = true;
                        break;
                    }
                }
                if (!duplicate)
                    points.Add(source[i]);
            }
            points.Sort((left, right) =>
            {
                int x = left.x.CompareTo(right.x);
                return x != 0 ? x : left.y.CompareTo(right.y);
            });
            if (points.Count < 3)
                return new List<Vector2>();

            var lower = new List<Vector2>();
            for (int i = 0; i < points.Count; i++)
            {
                while (lower.Count >= 2
                       && CrossDouble(
                           lower[lower.Count - 1] - lower[lower.Count - 2],
                           points[i] - lower[lower.Count - 1]) <= 0d)
                {
                    lower.RemoveAt(lower.Count - 1);
                }
                lower.Add(points[i]);
            }
            var upper = new List<Vector2>();
            for (int i = points.Count - 1; i >= 0; i--)
            {
                while (upper.Count >= 2
                       && CrossDouble(
                           upper[upper.Count - 1] - upper[upper.Count - 2],
                           points[i] - upper[upper.Count - 1]) <= 0d)
                {
                    upper.RemoveAt(upper.Count - 1);
                }
                upper.Add(points[i]);
            }
            lower.RemoveAt(lower.Count - 1);
            upper.RemoveAt(upper.Count - 1);
            lower.AddRange(upper);
            return lower;
        }

        static List<Vector2> BuildConcaveHull(
            IReadOnlyList<Vector2> source,
            IReadOnlyList<Vector2> convexHull,
            float tolerance)
        {
            var hull = new List<Vector2>(convexHull);
            if (hull.Count < 3)
                return hull;

            var interior = new List<Vector2>();
            for (int i = 0; i < source.Count; i++)
            {
                bool onHull = false;
                for (int j = 0; j < hull.Count; j++)
                {
                    if (Vector2.Distance(source[i], hull[j]) <= tolerance)
                    {
                        onHull = true;
                        break;
                    }
                }
                if (!onHull)
                    interior.Add(source[i]);
            }
            interior.Sort((left, right) =>
            {
                int x = left.x.CompareTo(right.x);
                return x != 0 ? x : left.y.CompareTo(right.y);
            });

            while (interior.Count > 0)
            {
                float bestScore = float.MaxValue;
                int bestPoint = -1;
                int bestEdge = -1;
                for (int pointIndex = 0;
                     pointIndex < interior.Count;
                     pointIndex++)
                {
                    Vector2 point = interior[pointIndex];
                    for (int edge = 0; edge < hull.Count; edge++)
                    {
                        int next = (edge + 1) % hull.Count;
                        float score =
                            Vector2.Distance(hull[edge], point)
                            + Vector2.Distance(point, hull[next])
                            - Vector2.Distance(hull[edge], hull[next]);
                        if (score >= bestScore)
                            continue;

                        var candidate = new List<Vector2>(hull);
                        candidate.Insert(next, point);
                        if (HasSelfIntersection(candidate))
                            continue;
                        bestScore = score;
                        bestPoint = pointIndex;
                        bestEdge = edge;
                    }
                }

                if (bestPoint < 0)
                    break;
                int insertAt = (bestEdge + 1) % hull.Count;
                if (insertAt == 0)
                    hull.Add(interior[bestPoint]);
                else
                    hull.Insert(insertAt, interior[bestPoint]);
                interior.RemoveAt(bestPoint);
            }

            RemoveCollinearVertices(hull, tolerance);
            if (SignedArea(hull) < 0f)
                hull.Reverse();
            return hull;
        }

        static Vector2 FindInteriorProbe(IReadOnlyList<Vector2> polygon)
        {
            Vector2 centroid = Centroid(polygon);
            if (ContainsPoint(polygon, centroid))
                return centroid;
            if (TryTriangulate(polygon, out List<int> triangles)
                && triangles.Count >= 3)
            {
                return (
                    polygon[triangles[0]]
                    + polygon[triangles[1]]
                    + polygon[triangles[2]]) / 3f;
            }
            return Average(polygon);
        }

        static void SortRegions(List<List<Vector2>> regions)
        {
            regions.Sort((left, right) =>
            {
                Vector2 leftCenter = FindInteriorProbe(left);
                Vector2 rightCenter = FindInteriorProbe(right);
                int x = leftCenter.x.CompareTo(rightCenter.x);
                if (x != 0)
                    return x;
                int y = leftCenter.y.CompareTo(rightCenter.y);
                if (y != 0)
                    return y;
                return Mathf.Abs(SignedArea(right)).CompareTo(
                    Mathf.Abs(SignedArea(left)));
            });
        }

        static void RemoveCollinearVertices(
            List<Vector2> polygon,
            float tolerance)
        {
            bool changed = true;
            while (changed && polygon.Count > 3)
            {
                changed = false;
                for (int i = 0; i < polygon.Count; i++)
                {
                    Vector2 previous =
                        polygon[(i - 1 + polygon.Count) % polygon.Count];
                    Vector2 current = polygon[i];
                    Vector2 next = polygon[(i + 1) % polygon.Count];
                    float distance = DistancePointToSegment(
                        current,
                        previous,
                        next);
                    if (distance > tolerance * 0.25f)
                        continue;
                    polygon.RemoveAt(i);
                    changed = true;
                    break;
                }
            }
        }

        static int GetOrAddGraphPoint(
            List<Vector2> points,
            Vector2 point,
            float tolerance)
        {
            for (int i = 0; i < points.Count; i++)
            {
                if (Vector2.Distance(points[i], point) <= tolerance)
                    return i;
            }
            points.Add(point);
            return points.Count - 1;
        }

        static void AddUniqueParameter(
            List<double> parameters,
            double value)
        {
            value = Clamp01(value);
            for (int i = 0; i < parameters.Count; i++)
            {
                if (Math.Abs(parameters[i] - value) <= 1e-8d)
                    return;
            }
            parameters.Add(value);
        }

        static bool PointInTriangle(
            Vector2 point,
            Vector2 a,
            Vector2 b,
            Vector2 c)
        {
            float first = Cross(b - a, point - a);
            float second = Cross(c - b, point - b);
            float third = Cross(a - c, point - c);
            return first >= -Epsilon
                && second >= -Epsilon
                && third >= -Epsilon;
        }

        static Vector2 Average(IReadOnlyList<Vector2> points)
        {
            if (points == null || points.Count == 0)
                return Vector2.zero;
            Vector2 total = Vector2.zero;
            for (int i = 0; i < points.Count; i++)
                total += points[i];
            return total / points.Count;
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

        static float Cross(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        static double CrossDouble(Vector2 a, Vector2 b)
        {
            return (double)a.x * b.y - (double)a.y * b.x;
        }

        static bool IsOnSegment(Vector2 a, Vector2 b, Vector2 point)
        {
            return point.x >= Mathf.Min(a.x, b.x) - Epsilon
                && point.x <= Mathf.Max(a.x, b.x) + Epsilon
                && point.y >= Mathf.Min(a.y, b.y) - Epsilon
                && point.y <= Mathf.Max(a.y, b.y) + Epsilon;
        }

        static double Clamp01(double value)
        {
            return Math.Max(0d, Math.Min(1d, value));
        }
    }
}
