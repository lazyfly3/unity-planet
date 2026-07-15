using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public enum CreatureBodyMeshQuality
{
    Preview,
    Final
}

public sealed class CreatureImplicitMeshData
{
    public int revision;
    public float cellSize;
    public Vector3[] vertices;
    public Vector3[] normals;
    public int[] triangles;
    public Bounds bounds;
}

public static class CreatureImplicitBodyMesher
{
    const int MaximumGridDimension = 128;
    const int MaximumVertexCount = 20000;
    static readonly int[,] Tetrahedra =
    {
        { 0, 5, 1, 6 }, { 0, 1, 2, 6 }, { 0, 2, 3, 6 },
        { 0, 3, 7, 6 }, { 0, 7, 4, 6 }, { 0, 4, 5, 6 }
    };
    static readonly int[,] CubeOffsets =
    {
        { 0, 0, 0 }, { 1, 0, 0 }, { 1, 1, 0 }, { 0, 1, 0 },
        { 0, 0, 1 }, { 1, 0, 1 }, { 1, 1, 1 }, { 0, 1, 1 }
    };
    static readonly int[,] TetrahedronEdges =
    {
        { 0, 1 }, { 0, 2 }, { 0, 3 }, { 1, 2 }, { 1, 3 }, { 2, 3 }
    };

    public static Task<CreatureImplicitMeshData> BuildAsync(
        CreatureTorsoSpline spline,
        CreatureBodyMeshQuality quality,
        int revision)
    {
        CreatureTorsoSpline snapshot = spline.Clone();
        return Task.Run(() => Build(snapshot, quality, revision));
    }

    public static CreatureImplicitMeshData Build(
        CreatureTorsoSpline spline,
        CreatureBodyMeshQuality quality,
        int revision = 0)
    {
        string error = null;
        if (spline == null || !spline.Validate(out error))
            throw new ArgumentException(error ?? "A valid torso spline is required.", nameof(spline));

        float requestedCell = quality == CreatureBodyMeshQuality.Preview ? 0.22f : 0.1f;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            CreatureImplicitMeshData result = BuildAtCellSize(spline, requestedCell, revision);
            if (result.vertices.Length <= MaximumVertexCount)
                return result;
            requestedCell *= 1.22f;
        }
        return BuildAtCellSize(spline, requestedCell, revision);
    }

    static CreatureImplicitMeshData BuildAtCellSize(CreatureTorsoSpline spline, float cellSize, int revision)
    {
        CalculateBounds(spline, out Vector3 minimum, out Vector3 maximum);
        Vector3 size = maximum - minimum;
        int nx = Math.Max(3, (int)Math.Ceiling(size.x / cellSize) + 1);
        int ny = Math.Max(3, (int)Math.Ceiling(size.y / cellSize) + 1);
        int nz = Math.Max(3, (int)Math.Ceiling(size.z / cellSize) + 1);
        int largest = Math.Max(nx, Math.Max(ny, nz));
        if (largest > MaximumGridDimension)
        {
            cellSize *= largest / (float)MaximumGridDimension;
            nx = Math.Max(3, (int)Math.Ceiling(size.x / cellSize) + 1);
            ny = Math.Max(3, (int)Math.Ceiling(size.y / cellSize) + 1);
            nz = Math.Max(3, (int)Math.Ceiling(size.z / cellSize) + 1);
        }

        maximum = minimum + new Vector3((nx - 1) * cellSize, (ny - 1) * cellSize, (nz - 1) * cellSize);
        var field = new float[nx * ny * nz];
        for (int z = 0; z < nz; z++)
        for (int y = 0; y < ny; y++)
        for (int x = 0; x < nx; x++)
        {
            Vector3 point = minimum + new Vector3(x * cellSize, y * cellSize, z * cellSize);
            field[Index(x, y, z, nx, ny)] = SampleDistance(spline, point);
        }

        var vertices = new List<Vector3>(8192);
        var normals = new List<Vector3>(8192);
        var triangles = new List<int>(16384);
        var corners = new Vector3[8];
        var values = new float[8];
        var polygon = new Vector3[4];
        var polygonAngles = new float[4];
        for (int z = 0; z < nz - 1; z++)
        for (int y = 0; y < ny - 1; y++)
        for (int x = 0; x < nx - 1; x++)
        {
            FillCube(minimum, cellSize, x, y, z, nx, ny, field, corners, values);
            bool hasInside = false;
            bool hasOutside = false;
            for (int i = 0; i < 8; i++)
            {
                hasInside |= values[i] <= 0f;
                hasOutside |= values[i] > 0f;
            }
            if (!hasInside || !hasOutside) continue;

            for (int tetrahedron = 0; tetrahedron < 6; tetrahedron++)
                PolygonizeTetrahedron(spline, corners, values, tetrahedron, cellSize,
                    polygon, polygonAngles, vertices, normals, triangles);
        }

        WeldVertices(vertices, normals, triangles,
            out Vector3[] weldedVertices, out Vector3[] weldedNormals, out int[] weldedTriangles);
        return new CreatureImplicitMeshData
        {
            revision = revision,
            cellSize = cellSize,
            vertices = weldedVertices,
            normals = weldedNormals,
            triangles = weldedTriangles,
            bounds = new Bounds((minimum + maximum) * 0.5f, maximum - minimum)
        };
    }

    static void WeldVertices(
        IReadOnlyList<Vector3> sourceVertices,
        IReadOnlyList<Vector3> sourceNormals,
        IReadOnlyList<int> sourceTriangles,
        out Vector3[] vertices,
        out Vector3[] normals,
        out int[] triangles)
    {
        const float precision = 10000f;
        var lookup = new Dictionary<Vector3Int, int>(sourceVertices.Count);
        var weldedVertices = new List<Vector3>(sourceVertices.Count / 2);
        var normalSums = new List<Vector3>(sourceVertices.Count / 2);
        var remap = new int[sourceVertices.Count];
        for (int i = 0; i < sourceVertices.Count; i++)
        {
            Vector3 point = sourceVertices[i];
            var key = new Vector3Int(
                Mathf.RoundToInt(point.x * precision),
                Mathf.RoundToInt(point.y * precision),
                Mathf.RoundToInt(point.z * precision));
            if (!lookup.TryGetValue(key, out int index))
            {
                index = weldedVertices.Count;
                lookup.Add(key, index);
                weldedVertices.Add(point);
                normalSums.Add(sourceNormals[i]);
            }
            else
                normalSums[index] += sourceNormals[i];
            remap[i] = index;
        }

        var weldedTriangles = new List<int>(sourceTriangles.Count);
        for (int i = 0; i + 2 < sourceTriangles.Count; i += 3)
        {
            int a = remap[sourceTriangles[i]];
            int b = remap[sourceTriangles[i + 1]];
            int c = remap[sourceTriangles[i + 2]];
            if (a == b || b == c || c == a) continue;
            weldedTriangles.Add(a);
            weldedTriangles.Add(b);
            weldedTriangles.Add(c);
        }

        var weldedNormals = new Vector3[normalSums.Count];
        for (int i = 0; i < normalSums.Count; i++)
            weldedNormals[i] = normalSums[i].sqrMagnitude > 0.000001f ? normalSums[i].normalized : Vector3.up;
        vertices = weldedVertices.ToArray();
        normals = weldedNormals;
        triangles = weldedTriangles.ToArray();
    }

    static void FillCube(
        Vector3 minimum, float cellSize, int x, int y, int z, int nx, int ny,
        float[] field, Vector3[] corners, float[] values)
    {
        for (int i = 0; i < 8; i++)
        {
            int px = x + CubeOffsets[i, 0];
            int py = y + CubeOffsets[i, 1];
            int pz = z + CubeOffsets[i, 2];
            corners[i] = minimum + new Vector3(px * cellSize, py * cellSize, pz * cellSize);
            values[i] = field[Index(px, py, pz, nx, ny)];
        }
    }

    static void PolygonizeTetrahedron(
        CreatureTorsoSpline spline,
        Vector3[] cubeCorners,
        float[] cubeValues,
        int tetrahedron,
        float cellSize,
        Vector3[] polygon,
        float[] polygonAngles,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<int> triangles)
    {
        int polygonCount = 0;
        for (int edge = 0; edge < 6; edge++)
        {
            int ia = Tetrahedra[tetrahedron, TetrahedronEdges[edge, 0]];
            int ib = Tetrahedra[tetrahedron, TetrahedronEdges[edge, 1]];
            float a = cubeValues[ia];
            float b = cubeValues[ib];
            if ((a <= 0f) == (b <= 0f)) continue;
            float t = a / (a - b);
            polygon[polygonCount++] = Vector3.LerpUnclamped(cubeCorners[ia], cubeCorners[ib], t);
        }
        if (polygonCount < 3) return;

        Vector3 center = Vector3.zero;
        for (int i = 0; i < polygonCount; i++) center += polygon[i];
        center /= polygonCount;
        Vector3 outward = SampleGradient(spline, center, cellSize * 0.35f);
        Vector3 axisX = Vector3.Cross(Math.Abs(outward.y) > 0.85f ? Vector3.forward : Vector3.up, outward).normalized;
        Vector3 axisY = Vector3.Cross(outward, axisX).normalized;
        for (int i = 0; i < polygonCount; i++)
        {
            Vector3 offset = polygon[i] - center;
            polygonAngles[i] = (float)Math.Atan2(Vector3.Dot(offset, axisY), Vector3.Dot(offset, axisX));
        }
        for (int i = 1; i < polygonCount; i++)
        {
            Vector3 point = polygon[i];
            float angle = polygonAngles[i];
            int insertion = i - 1;
            while (insertion >= 0 && polygonAngles[insertion] > angle)
            {
                polygon[insertion + 1] = polygon[insertion];
                polygonAngles[insertion + 1] = polygonAngles[insertion];
                insertion--;
            }
            polygon[insertion + 1] = point;
            polygonAngles[insertion + 1] = angle;
        }

        for (int i = 1; i < polygonCount - 1; i++)
        {
            Vector3 a = polygon[0];
            Vector3 b = polygon[i];
            Vector3 c = polygon[i + 1];
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f)
            {
                Vector3 swap = b;
                b = c;
                c = swap;
            }
            int start = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            normals.Add(SampleGradient(spline, a, cellSize * 0.35f));
            normals.Add(SampleGradient(spline, b, cellSize * 0.35f));
            normals.Add(SampleGradient(spline, c, cellSize * 0.35f));
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
        }
    }

    static float SampleDistance(CreatureTorsoSpline spline, Vector3 point)
    {
        float distance = float.PositiveInfinity;
        for (int i = 0; i < spline.points.Count - 1; i++)
        {
            CreatureTorsoControlPoint a = spline.points[i];
            CreatureTorsoControlPoint b = spline.points[i + 1];
            float segmentDistance = SampleSegment(a, b, point);
            float smoothing = Math.Max(0.015f, (a.blendRadius + b.blendRadius) * 0.5f);
            distance = SmoothMinimum(distance, segmentDistance, smoothing);
        }
        return distance;
    }

    static float SampleSegment(CreatureTorsoControlPoint a, CreatureTorsoControlPoint b, Vector3 point)
    {
        Vector3 segment = b.localPosition - a.localPosition;
        float length = Math.Max(0.0001f, segment.magnitude);
        Vector3 tangent = segment / length;
        float projection = Vector3.Dot(point - a.localPosition, tangent);
        float t = Math.Max(0f, Math.Min(1f, projection / length));
        Vector3 center = Vector3.LerpUnclamped(a.localPosition, b.localPosition, t);
        float width = Math.Max(0.04f, Mathf.LerpUnclamped(a.width * a.taper, b.width * b.taper, t) * 0.5f);
        float height = Math.Max(0.04f, Mathf.LerpUnclamped(a.height * a.taper, b.height * b.taper, t) * 0.5f);
        float roll = Mathf.LerpAngle(a.rollDegrees, b.rollDegrees, t);
        BuildFrame(tangent, roll, out Vector3 axisX, out Vector3 axisY);
        Vector3 offset = point - center;
        float x = Vector3.Dot(offset, axisX) / width;
        float y = Vector3.Dot(offset, axisY) / height;
        float outside = projection < 0f ? -projection : projection > length ? projection - length : 0f;
        float axialRadius = Math.Max(0.04f, Math.Min(width, height));
        float normalized = (float)Math.Sqrt(x * x + y * y + outside * outside / (axialRadius * axialRadius));
        return (normalized - 1f) * Math.Min(width, height);
    }

    static Vector3 SampleGradient(CreatureTorsoSpline spline, Vector3 point, float epsilon)
    {
        epsilon = Math.Max(0.001f, epsilon);
        float x = SampleDistance(spline, point + Vector3.right * epsilon)
            - SampleDistance(spline, point - Vector3.right * epsilon);
        float y = SampleDistance(spline, point + Vector3.up * epsilon)
            - SampleDistance(spline, point - Vector3.up * epsilon);
        float z = SampleDistance(spline, point + Vector3.forward * epsilon)
            - SampleDistance(spline, point - Vector3.forward * epsilon);
        Vector3 gradient = new Vector3(x, y, z);
        return gradient.sqrMagnitude > 0.000001f ? gradient.normalized : Vector3.up;
    }

    static void BuildFrame(Vector3 tangent, float rollDegrees, out Vector3 axisX, out Vector3 axisY)
    {
        Vector3 reference = Math.Abs(Vector3.Dot(tangent, Vector3.up)) > 0.9f ? Vector3.forward : Vector3.up;
        axisX = Vector3.Cross(reference, tangent).normalized;
        axisY = Vector3.Cross(tangent, axisX).normalized;
        float radians = rollDegrees * ((float)Math.PI / 180f);
        float cosine = (float)Math.Cos(radians);
        float sine = (float)Math.Sin(radians);
        Vector3 originalX = axisX;
        axisX = originalX * cosine + axisY * sine;
        axisY = axisY * cosine - originalX * sine;
    }

    static float SmoothMinimum(float a, float b, float smoothing)
    {
        if (float.IsPositiveInfinity(a)) return b;
        float h = Math.Max(0f, Math.Min(1f, 0.5f + 0.5f * (b - a) / smoothing));
        return Mathf.LerpUnclamped(b, a, h) - smoothing * h * (1f - h);
    }

    static void CalculateBounds(CreatureTorsoSpline spline, out Vector3 minimum, out Vector3 maximum)
    {
        minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        foreach (CreatureTorsoControlPoint point in spline.points)
        {
            float radius = Math.Max(point.width, point.height) * Math.Max(1f, point.taper) * 0.5f
                + point.blendRadius + 0.44f;
            Vector3 extent = Vector3.one * radius;
            minimum = Vector3.Min(minimum, point.localPosition - extent);
            maximum = Vector3.Max(maximum, point.localPosition + extent);
        }
    }

    static int Index(int x, int y, int z, int nx, int ny)
    {
        return x + nx * (y + ny * z);
    }
}

public static class CreatureSkinWeightSolver
{
    public static BoneWeight[] Calculate(
        IReadOnlyList<Vector3> vertices,
        CreatureTorsoSpline spline,
        IReadOnlyList<int> spineBoneIndices)
    {
        var result = new BoneWeight[vertices.Count];
        for (int vertexIndex = 0; vertexIndex < vertices.Count; vertexIndex++)
        {
            Vector3 vertex = vertices[vertexIndex];
            int bestSegment = 0;
            float bestDistance = float.PositiveInfinity;
            float bestT = 0f;
            for (int segmentIndex = 0; segmentIndex < spline.points.Count - 1; segmentIndex++)
            {
                Vector3 a = spline.points[segmentIndex].localPosition;
                Vector3 segment = spline.points[segmentIndex + 1].localPosition - a;
                float denominator = Mathf.Max(0.0001f, segment.sqrMagnitude);
                float t = Mathf.Clamp01(Vector3.Dot(vertex - a, segment) / denominator);
                float distance = (vertex - (a + segment * t)).sqrMagnitude;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                bestSegment = segmentIndex;
                bestT = t;
            }

            float splineT = (bestSegment + bestT) / Mathf.Max(1f, spline.points.Count - 1f);
            float boneT = splineT * Mathf.Max(1, spineBoneIndices.Count - 1);
            int firstPosition = Mathf.Clamp(Mathf.FloorToInt(boneT), 0, spineBoneIndices.Count - 1);
            int secondPosition = Mathf.Min(firstPosition + 1, spineBoneIndices.Count - 1);
            int first = spineBoneIndices[firstPosition];
            int second = spineBoneIndices[secondPosition];
            bestT = Mathf.SmoothStep(0f, 1f, boneT - firstPosition);
            result[vertexIndex] = new BoneWeight
            {
                boneIndex0 = first,
                weight0 = 1f - bestT,
                boneIndex1 = second,
                weight1 = bestT
            };
        }
        return result;
    }
}
