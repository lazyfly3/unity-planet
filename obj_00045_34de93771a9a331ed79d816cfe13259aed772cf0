using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityGeneration
{
    public static class CityPolygonGeometry
    {
        const float Epsilon = 0.0001f;

        readonly struct EdgeKey : IEquatable<EdgeKey>
        {
            public readonly int A;
            public readonly int B;

            public EdgeKey(int a, int b)
            {
                A = Mathf.Min(a, b);
                B = Mathf.Max(a, b);
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

        public static bool ValidateBoundary(
            IReadOnlyList<Vector2> points,
            CityGenerationSettings settings,
            out string error)
        {
            return ResolveClosedRegions(points, settings, out _, out error);
        }

        public static bool ResolveClosedRegions(
            IReadOnlyList<Vector2> points,
            CityGenerationSettings settings,
            out List<List<Vector2>> regions,
            out string error)
        {
            regions = new List<List<Vector2>>();
            if (points == null || points.Count < 3)
            {
                error = "至少需要选择 3 个边界点。";
                return false;
            }

            CityGenerationSettings safeSettings =
                settings == null ? new CityGenerationSettings() : settings.ValidatedCopy();

            for (int i = 0; i < points.Count; i++)
            {
                Vector2 a = points[i];
                Vector2 b = points[(i + 1) % points.Count];
                if (Vector2.Distance(a, b) < safeSettings.minimumBoundaryEdge)
                {
                    error = "边界点距离太近，请扩大选区。";
                    return false;
                }

                for (int j = i + 1; j < points.Count; j++)
                {
                    if (Vector2.Distance(a, points[j]) < Epsilon)
                    {
                        error = "边界中存在重复点。";
                        return false;
                    }
                }
            }

            int segmentCount = points.Count;
            var splitParameters = new List<float>[segmentCount];
            for (int i = 0; i < segmentCount; i++)
            {
                splitParameters[i] = new List<float> { 0f, 1f };
            }

            for (int i = 0; i < segmentCount; i++)
            {
                Vector2 a = points[i];
                Vector2 b = points[(i + 1) % segmentCount];
                for (int j = i + 1; j < segmentCount; j++)
                {
                    int nextI = (i + 1) % segmentCount;
                    int nextJ = (j + 1) % segmentCount;
                    if (i == j || nextI == j || i == nextJ)
                        continue;

                    Vector2 c = points[j];
                    Vector2 d = points[nextJ];
                    if (AreCollinearAndOverlapping(a, b, c, d))
                    {
                        error = "边界中存在重叠线段，无法确定封闭区域。";
                        return false;
                    }

                    if (!TryGetSegmentIntersection(a, b, c, d, out float t, out float u))
                        continue;

                    AddUniqueParameter(splitParameters[i], t);
                    AddUniqueParameter(splitParameters[j], u);
                }
            }

            var graphPoints = new List<Vector2>();
            var graphEdges = new HashSet<EdgeKey>();
            for (int i = 0; i < segmentCount; i++)
            {
                splitParameters[i].Sort();
                Vector2 start = points[i];
                Vector2 end = points[(i + 1) % segmentCount];
                for (int split = 0; split + 1 < splitParameters[i].Count; split++)
                {
                    Vector2 fromPoint = Vector2.Lerp(
                        start,
                        end,
                        splitParameters[i][split]);
                    Vector2 toPoint = Vector2.Lerp(
                        start,
                        end,
                        splitParameters[i][split + 1]);
                    if (Vector2.Distance(fromPoint, toPoint) <= Epsilon)
                        continue;

                    int from = GetOrAddGraphPoint(graphPoints, fromPoint);
                    int to = GetOrAddGraphPoint(graphPoints, toPoint);
                    if (from != to)
                        graphEdges.Add(new EdgeKey(from, to));
                }
            }

            ExtractBoundedFaces(
                graphPoints,
                graphEdges,
                safeSettings.minimumBoundaryArea,
                regions);

            if (regions.Count == 0)
            {
                error = "选中的闭合区域面积太小，无法生成城市。";
                return false;
            }

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
                    int previous = remaining[
                        (i - 1 + remaining.Count) % remaining.Count];
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
                        if (PointInTriangle(
                                polygon[candidate],
                                a,
                                b,
                                c))
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
            {
                Vector2 average = Vector2.zero;
                for (int i = 0; i < polygon.Count; i++)
                    average += polygon[i];
                return average / Mathf.Max(1, polygon.Count);
            }

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
            return new Vector2((float)(x / denominator), (float)(y / denominator));
        }

        public static bool ContainsPoint(IReadOnlyList<Vector2> polygon, Vector2 point)
        {
            if (polygon == null || polygon.Count < 3)
                return false;

            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                Vector2 a = polygon[j];
                Vector2 b = polygon[i];
                if (DistancePointToSegment(point, a, b) <= 0.001f)
                    return true;

                bool crosses = (a.y > point.y) != (b.y > point.y);
                if (crosses)
                {
                    float intersectionX =
                        (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x;
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
            if (!ContainsPoint(polygon, start) || !ContainsPoint(polygon, end))
                return false;

            const int sampleCount = 12;
            for (int i = 1; i < sampleCount; i++)
            {
                Vector2 sample = Vector2.Lerp(start, end, i / (float)sampleCount);
                if (!ContainsPoint(polygon, sample))
                    return false;
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
                if (!ContainsPoint(boundary, a) || !ContainsSegment(boundary, a, b))
                    return false;
            }

            Vector2 center = Vector2.zero;
            for (int i = 0; i < candidate.Count; i++)
                center += candidate[i];
            return ContainsPoint(boundary, center / candidate.Count);
        }

        public static bool HasSelfIntersection(IReadOnlyList<Vector2> polygon)
        {
            if (polygon == null || polygon.Count < 4)
                return false;

            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 a1 = polygon[i];
                Vector2 a2 = polygon[(i + 1) % polygon.Count];
                for (int j = i + 1; j < polygon.Count; j++)
                {
                    int nextJ = (j + 1) % polygon.Count;
                    if (i == j || (i + 1) % polygon.Count == j || i == nextJ)
                        continue;

                    if (SegmentsIntersect(a1, a2, polygon[j], polygon[nextJ]))
                        return true;
                }
            }

            return false;
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

        static void ExtractBoundedFaces(
            IReadOnlyList<Vector2> points,
            IReadOnlyCollection<EdgeKey> edges,
            float minimumArea,
            List<List<Vector2>> regions)
        {
            var adjacency = new List<int>[points.Count];
            for (int i = 0; i < adjacency.Length; i++)
                adjacency[i] = new List<int>();

            foreach (EdgeKey edge in edges)
            {
                adjacency[edge.A].Add(edge.B);
                adjacency[edge.B].Add(edge.A);
            }

            for (int i = 0; i < adjacency.Length; i++)
            {
                int centerIndex = i;
                adjacency[i].Sort((left, right) =>
                {
                    Vector2 leftDirection = points[left] - points[centerIndex];
                    Vector2 rightDirection = points[right] - points[centerIndex];
                    float leftAngle = Mathf.Atan2(leftDirection.y, leftDirection.x);
                    float rightAngle = Mathf.Atan2(rightDirection.y, rightDirection.x);
                    return leftAngle.CompareTo(rightAngle);
                });
            }

            var visited = new HashSet<DirectedEdgeKey>();
            int traversalLimit = Mathf.Max(16, edges.Count * 4);

            foreach (EdgeKey edge in edges)
            {
                TraceFace(edge.A, edge.B);
                TraceFace(edge.B, edge.A);
            }

            void TraceFace(int startFrom, int startTo)
            {
                var firstEdge = new DirectedEdgeKey(startFrom, startTo);
                if (visited.Contains(firstEdge))
                    return;

                var faceIndices = new List<int>();
                int from = startFrom;
                int to = startTo;
                int guard = 0;

                while (guard++ < traversalLimit)
                {
                    var directed = new DirectedEdgeKey(from, to);
                    if (visited.Contains(directed))
                    {
                        if (from != startFrom || to != startTo)
                            return;
                        break;
                    }

                    visited.Add(directed);
                    faceIndices.Add(from);

                    List<int> neighbors = adjacency[to];
                    int incomingIndex = neighbors.IndexOf(from);
                    if (incomingIndex < 0 || neighbors.Count < 2)
                        return;

                    int nextIndex = (incomingIndex - 1 + neighbors.Count) % neighbors.Count;
                    int next = neighbors[nextIndex];
                    from = to;
                    to = next;

                    if (from == startFrom && to == startTo)
                        break;
                }

                if (guard >= traversalLimit || faceIndices.Count < 3)
                    return;

                var face = new List<Vector2>(faceIndices.Count);
                for (int i = 0; i < faceIndices.Count; i++)
                    face.Add(points[faceIndices[i]]);
                RemoveCollinearVertices(face);

                float area = SignedArea(face);
                if (area >= minimumArea)
                    regions.Add(face);
            }
        }

        static void RemoveCollinearVertices(List<Vector2> polygon)
        {
            bool changed = true;
            while (changed && polygon.Count > 3)
            {
                changed = false;
                for (int i = 0; i < polygon.Count; i++)
                {
                    Vector2 previous = polygon[(i - 1 + polygon.Count) % polygon.Count];
                    Vector2 current = polygon[i];
                    Vector2 next = polygon[(i + 1) % polygon.Count];
                    if (Mathf.Abs(Cross(current - previous, next - current)) > Epsilon)
                        continue;

                    polygon.RemoveAt(i);
                    changed = true;
                    break;
                }
            }
        }

        static int GetOrAddGraphPoint(List<Vector2> points, Vector2 point)
        {
            for (int i = 0; i < points.Count; i++)
            {
                if (Vector2.Distance(points[i], point) <= Epsilon)
                    return i;
            }

            points.Add(point);
            return points.Count - 1;
        }

        static void AddUniqueParameter(List<float> parameters, float value)
        {
            value = Mathf.Clamp01(value);
            for (int i = 0; i < parameters.Count; i++)
            {
                if (Mathf.Abs(parameters[i] - value) <= Epsilon)
                    return;
            }
            parameters.Add(value);
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
            if (Mathf.Abs(denominator) <= Epsilon)
            {
                t = 0f;
                u = 0f;
                return false;
            }

            Vector2 offset = c - a;
            t = Cross(offset, second) / denominator;
            u = Cross(offset, first) / denominator;
            return t >= -Epsilon
                && t <= 1f + Epsilon
                && u >= -Epsilon
                && u <= 1f + Epsilon;
        }

        static bool AreCollinearAndOverlapping(
            Vector2 a,
            Vector2 b,
            Vector2 c,
            Vector2 d)
        {
            Vector2 ab = b - a;
            if (Mathf.Abs(Cross(ab, c - a)) > Epsilon
                || Mathf.Abs(Cross(ab, d - a)) > Epsilon)
            {
                return false;
            }

            float abLengthSquared = ab.sqrMagnitude;
            if (abLengthSquared <= Epsilon)
                return false;

            float cProjection = Vector2.Dot(c - a, ab) / abLengthSquared;
            float dProjection = Vector2.Dot(d - a, ab) / abLengthSquared;
            float overlapStart = Mathf.Max(0f, Mathf.Min(cProjection, dProjection));
            float overlapEnd = Mathf.Min(1f, Mathf.Max(cProjection, dProjection));
            return overlapEnd - overlapStart > Epsilon;
        }

        public static bool SegmentsIntersect(
            Vector2 a,
            Vector2 b,
            Vector2 c,
            Vector2 d)
        {
            float abC = Cross(b - a, c - a);
            float abD = Cross(b - a, d - a);
            float cdA = Cross(d - c, a - c);
            float cdB = Cross(d - c, b - c);

            if (((abC > Epsilon && abD < -Epsilon) || (abC < -Epsilon && abD > Epsilon))
                && ((cdA > Epsilon && cdB < -Epsilon) || (cdA < -Epsilon && cdB > Epsilon)))
            {
                return true;
            }

            return Mathf.Abs(abC) <= Epsilon && IsOnSegment(a, b, c)
                || Mathf.Abs(abD) <= Epsilon && IsOnSegment(a, b, d)
                || Mathf.Abs(cdA) <= Epsilon && IsOnSegment(c, d, a)
                || Mathf.Abs(cdB) <= Epsilon && IsOnSegment(c, d, b);
        }

        public static float DistancePointToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 segment = b - a;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= Epsilon)
                return Vector2.Distance(point, a);

            float t = Mathf.Clamp01(Vector2.Dot(point - a, segment) / lengthSquared);
            return Vector2.Distance(point, a + segment * t);
        }

        static float Cross(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        static bool IsOnSegment(Vector2 a, Vector2 b, Vector2 point)
        {
            return point.x >= Mathf.Min(a.x, b.x) - Epsilon
                && point.x <= Mathf.Max(a.x, b.x) + Epsilon
                && point.y >= Mathf.Min(a.y, b.y) - Epsilon
                && point.y <= Mathf.Max(a.y, b.y) + Epsilon;
        }
    }
}
