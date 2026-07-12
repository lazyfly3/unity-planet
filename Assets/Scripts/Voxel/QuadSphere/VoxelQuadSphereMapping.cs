using UnityEngine;

public static class VoxelQuadSphereMapping
{
    public static Vector3 GetFaceCubePoint(QuadSphereFace face, float u, float v)
    {
        switch (face)
        {
            case QuadSphereFace.PosX: return new Vector3(1f, v, u);
            case QuadSphereFace.NegX: return new Vector3(-1f, v, u);
            case QuadSphereFace.PosY: return new Vector3(u, 1f, v);
            case QuadSphereFace.NegY: return new Vector3(u, -1f, v);
            case QuadSphereFace.PosZ: return new Vector3(u, v, 1f);
            default: return new Vector3(u, v, -1f);
        }
    }

    public static Vector3 CubeToSphere(Vector3 cubePoint)
    {
        float x2 = cubePoint.x * cubePoint.x;
        float y2 = cubePoint.y * cubePoint.y;
        float z2 = cubePoint.z * cubePoint.z;

        float x = cubePoint.x * Mathf.Sqrt(Mathf.Max(0f, 1f - y2 / 2f - z2 / 2f + y2 * z2 / 3f));
        float y = cubePoint.y * Mathf.Sqrt(Mathf.Max(0f, 1f - x2 / 2f - z2 / 2f + x2 * z2 / 3f));
        float z = cubePoint.z * Mathf.Sqrt(Mathf.Max(0f, 1f - x2 / 2f - y2 / 2f + x2 * y2 / 3f));
        return new Vector3(x, y, z);
    }

    public static Vector3 GetRadialDirection(QuadSphereFace face, int cellU, int cellV, int gridSize)
    {
        float u = CellToNormalized(cellU, gridSize);
        float v = CellToNormalized(cellV, gridSize);
        Vector3 cubePoint = GetFaceCubePoint(face, u, v);
        return CubeToSphere(cubePoint).normalized;
    }

    public static Vector3 FaceCellCenterLocal(
        QuadSphereFace face,
        int cellU,
        int cellV,
        int depth,
        int gridSize,
        float planetRadius,
        Vector3 planetCenter)
    {
        Vector3 radial = GetRadialDirection(face, cellU, cellV, gridSize);
        float distance = planetRadius - depth - 0.5f;
        return planetCenter + radial * distance;
    }

    public static Quaternion GetCellOrientation(QuadSphereFace face, int cellU, int cellV, int gridSize)
    {
        Vector3 up = GetRadialDirection(face, cellU, cellV, gridSize);
        Vector3 referenceForward = Vector3.Cross(up, Vector3.up);
        if (referenceForward.sqrMagnitude < 0.0001f)
            referenceForward = Vector3.Cross(up, Vector3.forward);
        referenceForward.Normalize();

        float u = CellToNormalized(cellU, gridSize);
        float v = CellToNormalized(cellV, gridSize);
        Vector3 cubePoint = GetFaceCubePoint(face, u, v);
        Vector3 du = GetFaceCubePoint(face, u + 0.02f, v) - cubePoint;
        Vector3 tangent = Vector3.ProjectOnPlane(CubeToSphere(cubePoint + du).normalized - up, up);
        if (tangent.sqrMagnitude < 0.0001f)
            tangent = Vector3.ProjectOnPlane(referenceForward, up);
        tangent.Normalize();
        return Quaternion.LookRotation(tangent, up);
    }

    public static QuadSphereFace GetDominantFace(Vector3 directionFromCenter)
    {
        Vector3 d = directionFromCenter.normalized;
        float ax = Mathf.Abs(d.x);
        float ay = Mathf.Abs(d.y);
        float az = Mathf.Abs(d.z);

        if (ax >= ay && ax >= az)
            return d.x >= 0f ? QuadSphereFace.PosX : QuadSphereFace.NegX;
        if (ay >= ax && ay >= az)
            return d.y >= 0f ? QuadSphereFace.PosY : QuadSphereFace.NegY;
        return d.z >= 0f ? QuadSphereFace.PosZ : QuadSphereFace.NegZ;
    }

    public static bool TryLocalPointToVoxel(
        Vector3 localPoint,
        Vector3 planetCenter,
        float planetRadius,
        int gridSize,
        int maxDepth,
        out QuadSphereVoxelAddress address)
    {
        Vector3 offset = localPoint - planetCenter;
        float distance = offset.magnitude;
        if (distance < 0.001f)
        {
            address = default;
            return false;
        }

        Vector3 dir = offset / distance;
        QuadSphereFace face = GetDominantFace(dir);
        Vector2 uv = DirectionToFaceUV(dir, face);

        if (Mathf.Abs(uv.x) > 1.01f || Mathf.Abs(uv.y) > 1.01f)
        {
            address = default;
            return false;
        }

        int u = NormalizedToCell(uv.x, gridSize);
        int v = NormalizedToCell(uv.y, gridSize);
        int depth = Mathf.Clamp(Mathf.RoundToInt(planetRadius - distance - 0.5f), 0, maxDepth - 1);

        if (u < 0 || v < 0 || u >= gridSize || v >= gridSize)
        {
            address = default;
            return false;
        }

        address = new QuadSphereVoxelAddress(face, u, v, depth);
        return true;
    }

    static Vector2 DirectionToFaceUV(Vector3 dir, QuadSphereFace face)
    {
        dir = dir.normalized;
        switch (face)
        {
            case QuadSphereFace.PosX: return new Vector2(dir.z / dir.x, dir.y / dir.x);
            case QuadSphereFace.NegX: return new Vector2(-dir.z / dir.x, dir.y / dir.x);
            case QuadSphereFace.PosY: return new Vector2(dir.x / dir.y, dir.z / dir.y);
            case QuadSphereFace.NegY: return new Vector2(dir.x / dir.y, -dir.z / dir.y);
            case QuadSphereFace.PosZ: return new Vector2(dir.x / dir.z, dir.y / dir.z);
            default: return new Vector2(-dir.x / dir.z, dir.y / dir.z);
        }
    }

    static float CellToNormalized(int cell, int gridSize)
    {
        return (cell + 0.5f) / gridSize * 2f - 1f;
    }

    static int NormalizedToCell(float normalized, int gridSize)
    {
        return Mathf.Clamp(Mathf.FloorToInt((normalized * 0.5f + 0.5f) * gridSize), 0, gridSize - 1);
    }
}
