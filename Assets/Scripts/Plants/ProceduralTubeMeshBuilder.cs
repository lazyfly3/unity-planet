using System.Collections.Generic;
using UnityEngine;

public sealed class ProceduralTubeMeshBuilder
{
    readonly List<Vector3> vertices = new List<Vector3>(4096);
    readonly List<Vector3> normals = new List<Vector3>(4096);
    readonly List<Vector2> uv = new List<Vector2>(4096);
    readonly List<Color> colors = new List<Color>(4096);
    readonly List<int> barkTriangles = new List<int>(8192);
    readonly List<int> foliageTriangles = new List<int>(8192);
    Bounds bounds;
    bool hasBounds;

    public void AddBranch(PlantBranch branch, int sides, Color barkColor)
    {
        if (branch == null || branch.Points == null || branch.Points.Length < 2)
            return;
        sides = Mathf.Max(3, sides);
        int pointCount = branch.Points.Length;
        var tangents = new Vector3[pointCount];
        var frameNormals = new Vector3[pointCount];
        var frameBinormals = new Vector3[pointCount];
        float totalLength = 0f;
        var cumulativeLength = new float[pointCount];

        for (int i = 0; i < pointCount; i++)
        {
            if (i == 0)
                tangents[i] = (branch.Points[1] - branch.Points[0]).normalized;
            else if (i == pointCount - 1)
                tangents[i] = (branch.Points[i] - branch.Points[i - 1]).normalized;
            else
                tangents[i] = (branch.Points[i + 1] - branch.Points[i - 1]).normalized;
            if (i > 0)
            {
                totalLength += Vector3.Distance(branch.Points[i - 1], branch.Points[i]);
                cumulativeLength[i] = totalLength;
            }
        }

        frameNormals[0] = Perpendicular(tangents[0]);
        frameBinormals[0] = Vector3.Cross(tangents[0], frameNormals[0]).normalized;
        for (int i = 1; i < pointCount; i++)
        {
            Quaternion transport = Quaternion.FromToRotation(tangents[i - 1], tangents[i]);
            frameNormals[i] = Vector3.ProjectOnPlane(transport * frameNormals[i - 1], tangents[i]).normalized;
            if (frameNormals[i].sqrMagnitude < 0.5f)
                frameNormals[i] = Perpendicular(tangents[i]);
            frameBinormals[i] = Vector3.Cross(tangents[i], frameNormals[i]).normalized;
        }

        int firstRing = vertices.Count;
        for (int ring = 0; ring < pointCount; ring++)
        {
            float t = ring / (float)(pointCount - 1);
            float radius = Mathf.Lerp(branch.StartRadius, branch.EndRadius, t);
            for (int side = 0; side < sides; side++)
            {
                float angle = side / (float)sides * Mathf.PI * 2f;
                Vector3 radial = frameNormals[ring] * Mathf.Cos(angle)
                    + frameBinormals[ring] * Mathf.Sin(angle);
                AddVertex(
                    branch.Points[ring] + radial * radius,
                    radial,
                    new Vector2(side / (float)sides, totalLength > 0f ? cumulativeLength[ring] / totalLength : t),
                    barkColor);
            }
        }

        for (int ring = 0; ring < pointCount - 1; ring++)
        {
            int current = firstRing + ring * sides;
            int next = current + sides;
            for (int side = 0; side < sides; side++)
            {
                int following = (side + 1) % sides;
                AddQuad(
                    barkTriangles,
                    current + side,
                    next + side,
                    next + following,
                    current + following);
            }
        }

        AddCap(branch.Points[0], -tangents[0], firstRing, sides, barkColor, true);
        int lastRing = firstRing + (pointCount - 1) * sides;
        AddCap(branch.Points[pointCount - 1], tangents[pointCount - 1], lastRing, sides, barkColor, false);
    }

    public void AddSolidLeaf(PlantLeafAnchor anchor, Color leafColor, Color accentColor)
    {
        Vector3 forward = SafeNormal(anchor.direction, Vector3.up);
        Vector3 right = SafeNormal(anchor.normal, Perpendicular(forward));
        Vector3 up = SafeNormal(Vector3.Cross(right, forward), Vector3.forward);
        float length = anchor.scale;
        float width = length * 0.48f;
        float thickness = Mathf.Max(0.012f, length * 0.055f);
        Vector3 center = anchor.position + forward * length * 0.48f;

        Vector3[] outline =
        {
            anchor.position,
            center - right * width,
            anchor.position + forward * length,
            center + right * width
        };
        int start = vertices.Count;
        for (int side = 0; side < 2; side++)
        {
            float sign = side == 0 ? 1f : -1f;
            for (int i = 0; i < outline.Length; i++)
            {
                Vector2 texcoord = i == 0 ? new Vector2(0.5f, 0f)
                    : i == 1 ? new Vector2(0f, 0.5f)
                    : i == 2 ? new Vector2(0.5f, 1f)
                    : new Vector2(1f, 0.5f);
                AddVertex(outline[i] + up * thickness * sign, up * sign, texcoord,
                    Color.Lerp(leafColor, accentColor, i == 2 ? 0.28f : 0f));
            }
        }
        foliageTriangles.Add(start);
        foliageTriangles.Add(start + 1);
        foliageTriangles.Add(start + 2);
        foliageTriangles.Add(start);
        foliageTriangles.Add(start + 2);
        foliageTriangles.Add(start + 3);
        foliageTriangles.Add(start + 4);
        foliageTriangles.Add(start + 6);
        foliageTriangles.Add(start + 5);
        foliageTriangles.Add(start + 4);
        foliageTriangles.Add(start + 7);
        foliageTriangles.Add(start + 6);
        for (int i = 0; i < 4; i++)
        {
            int next = (i + 1) % 4;
            AddQuad(foliageTriangles, start + i, start + 4 + i, start + 4 + next, start + next);
        }
    }

    public void AddCardLeaf(PlantLeafAnchor anchor, Color leafColor, Color accentColor, bool crossed)
    {
        Vector3 forward = SafeNormal(anchor.direction, Vector3.up);
        Vector3 baseRight = SafeNormal(anchor.normal, Perpendicular(forward));
        int cardCount = crossed ? 2 : 1;
        for (int card = 0; card < cardCount; card++)
        {
            Vector3 right = card == 0
                ? baseRight
                : Quaternion.AngleAxis(90f, forward) * baseRight;
            Vector3 normal = SafeNormal(Vector3.Cross(right, forward), Vector3.forward);
            float length = anchor.scale * 1.25f;
            float halfWidth = anchor.scale * 0.38f;
            int start = vertices.Count;
            AddVertex(anchor.position - right * halfWidth, normal, new Vector2(0f, 0f), leafColor);
            AddVertex(anchor.position + right * halfWidth, normal, new Vector2(1f, 0f), leafColor);
            AddVertex(anchor.position + forward * length + right * halfWidth * 0.42f,
                normal, new Vector2(1f, 1f), accentColor);
            AddVertex(anchor.position + forward * length - right * halfWidth * 0.42f,
                normal, new Vector2(0f, 1f), accentColor);
            AddQuad(foliageTriangles, start, start + 1, start + 2, start + 3);
        }
    }

    public ProceduralPlantMeshData ToMeshData(int sourceHash)
    {
        return new ProceduralPlantMeshData
        {
            vertices = vertices.ToArray(),
            normals = normals.ToArray(),
            uv = uv.ToArray(),
            colors = colors.ToArray(),
            barkTriangles = barkTriangles.ToArray(),
            foliageTriangles = foliageTriangles.ToArray(),
            bounds = hasBounds ? bounds : new Bounds(Vector3.zero, Vector3.one * 0.01f),
            sourceHash = sourceHash
        };
    }

    void AddCap(
        Vector3 position,
        Vector3 normal,
        int ringStart,
        int sides,
        Color color,
        bool reverse)
    {
        int center = vertices.Count;
        AddVertex(position, normal, new Vector2(0.5f, 0.5f), color);
        for (int side = 0; side < sides; side++)
        {
            int next = (side + 1) % sides;
            if (reverse)
            {
                barkTriangles.Add(center);
                barkTriangles.Add(ringStart + next);
                barkTriangles.Add(ringStart + side);
            }
            else
            {
                barkTriangles.Add(center);
                barkTriangles.Add(ringStart + side);
                barkTriangles.Add(ringStart + next);
            }
        }
    }

    void AddVertex(Vector3 position, Vector3 normal, Vector2 texcoord, Color color)
    {
        vertices.Add(position);
        normals.Add(SafeNormal(normal, Vector3.up));
        uv.Add(texcoord);
        colors.Add(color);
        if (!hasBounds)
        {
            bounds = new Bounds(position, Vector3.zero);
            hasBounds = true;
        }
        else
        {
            bounds.Encapsulate(position);
        }
    }

    static void AddQuad(List<int> triangles, int a, int b, int c, int d)
    {
        triangles.Add(a);
        triangles.Add(b);
        triangles.Add(c);
        triangles.Add(a);
        triangles.Add(c);
        triangles.Add(d);
    }

    static Vector3 Perpendicular(Vector3 direction)
    {
        Vector3 reference = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) < 0.92f
            ? Vector3.up
            : Vector3.right;
        return Vector3.Cross(direction, reference).normalized;
    }

    static Vector3 SafeNormal(Vector3 value, Vector3 fallback)
    {
        return value.sqrMagnitude > 0.000001f ? value.normalized : fallback.normalized;
    }
}
