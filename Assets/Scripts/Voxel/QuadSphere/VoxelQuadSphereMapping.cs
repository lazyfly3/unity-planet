using UnityEngine;

public static class VoxelQuadSphereMapping
{
    public static Vector3 GetFaceCubePoint(QuadSphereFace face, float u, float v)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(100, (int)u, (int)v);
        // Keep every face in the same handed coordinate system:
        // cross(+U tangent, outward normal) must point along +V.
        switch (face)
        {
            case QuadSphereFace.PosX: return new Vector3(1f, v, u);
            case QuadSphereFace.NegX: return new Vector3(-1f, -v, u);
            case QuadSphereFace.PosY: return new Vector3(u, 1f, v);
            case QuadSphereFace.NegY: return new Vector3(-u, -1f, v);
            case QuadSphereFace.PosZ: return new Vector3(u, -v, 1f);
            default: return new Vector3(u, v, -1f);
        }
    }

    public static Vector3 CubeToSphere(Vector3 cubePoint)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(101);
        float x2 = cubePoint.x * cubePoint.x;
        float y2 = cubePoint.y * cubePoint.y;
        float z2 = cubePoint.z * cubePoint.z;

        float x = cubePoint.x * Mathf.Sqrt(Mathf.Max(0f, 1f - y2 / 2f - z2 / 2f + y2 * z2 / 3f));
        float y = cubePoint.y * Mathf.Sqrt(Mathf.Max(0f, 1f - x2 / 2f - z2 / 2f + x2 * z2 / 3f));
        float z = cubePoint.z * Mathf.Sqrt(Mathf.Max(0f, 1f - x2 / 2f - y2 / 2f + x2 * y2 / 3f));
        return new Vector3(x, y, z);
    }

    public static Vector3 GetRadialDirection(QuadSphereFace face, int cellU, int cellV, int gridSize)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(102, (int)cellU, (int)cellV, (int)gridSize);
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
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(103, (int)cellU, (int)cellV, (int)depth, (int)gridSize, (int)planetRadius);
        Vector3 radial = GetRadialDirection(face, cellU, cellV, gridSize);
        float distance = planetRadius - depth - 0.5f;
        return planetCenter + radial * distance;
    }

    public struct CellHalfExtents
    {
        public float PosU;
        public float NegU;
        public float PosV;
        public float NegV;
        public float PosOut;
        public float NegIn;
    }

    public static CellHalfExtents GetCellHalfExtents(
        QuadSphereFace face,
        int cellU,
        int cellV,
        int depth,
        int gridSize,
        int maxDepth,
        float planetRadius,
        Vector3 planetCenter)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(104, (int)cellU, (int)cellV, (int)depth, (int)gridSize, (int)maxDepth, (int)planetRadius);
        const float seamOverlap = 1.004f;
        Vector3 center = FaceCellCenterLocal(face, cellU, cellV, depth, gridSize, planetRadius, planetCenter);

        bool TryGetNeighbor(int du, int dv, int dd, out Vector3 neighborCenter)
        {
            int nu = cellU + du;
            int nv = cellV + dv;
            int nd = depth + dd;
            if (nu < 0 || nv < 0 || nd < 0 || nu >= gridSize || nv >= gridSize || nd >= maxDepth)
            {
                neighborCenter = default;
                return false;
            }

            neighborCenter = FaceCellCenterLocal(face, nu, nv, nd, gridSize, planetRadius, planetCenter);
            return true;
        }

        return new CellHalfExtents
        {
            PosU = GetOneSidedHalfExtent(center, TryGetNeighbor(1, 0, 0, out Vector3 uPlus), uPlus, TryGetNeighbor(-1, 0, 0, out Vector3 uMinus), uMinus) * seamOverlap,
            NegU = GetOneSidedHalfExtent(center, TryGetNeighbor(-1, 0, 0, out Vector3 uNeg), uNeg, TryGetNeighbor(1, 0, 0, out Vector3 uPos), uPos) * seamOverlap,
            PosV = GetOneSidedHalfExtent(center, TryGetNeighbor(0, 1, 0, out Vector3 vPlus), vPlus, TryGetNeighbor(0, -1, 0, out Vector3 vMinus), vMinus) * seamOverlap,
            NegV = GetOneSidedHalfExtent(center, TryGetNeighbor(0, -1, 0, out Vector3 vNeg), vNeg, TryGetNeighbor(0, 1, 0, out Vector3 vPos), vPos) * seamOverlap,
            PosOut = GetOneSidedHalfExtent(center, TryGetNeighbor(0, 0, -1, out Vector3 outPlus), outPlus, TryGetNeighbor(0, 0, 1, out Vector3 outMinus), outMinus) * seamOverlap,
            NegIn = GetOneSidedHalfExtent(center, TryGetNeighbor(0, 0, 1, out Vector3 inPlus), inPlus, TryGetNeighbor(0, 0, -1, out Vector3 inMinus), inMinus) * seamOverlap
        };
    }

    static float GetOneSidedHalfExtent(
        Vector3 center,
        bool hasNeighbor,
        Vector3 neighborCenter,
        bool hasOpposite,
        Vector3 oppositeCenter)
    {
        if (hasNeighbor)
            return Vector3.Distance(center, neighborCenter) * 0.5f;

        if (hasOpposite)
            return Vector3.Distance(center, oppositeCenter) * 0.5f;

        return 0.5f;
    }

    public static Quaternion GetCellOrientation(QuadSphereFace face, int cellU, int cellV, int gridSize)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(105, (int)cellU, (int)cellV, (int)gridSize);
        GetCellBasis(face, cellU, cellV, gridSize, out Vector3 up, out Vector3 right, out Vector3 forward);
        return Quaternion.LookRotation(forward, up);
    }

    public static void GetCellBasis(
        QuadSphereFace face,
        int cellU,
        int cellV,
        int gridSize,
        out Vector3 up,
        out Vector3 right,
        out Vector3 forward)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(106, (int)cellU, (int)cellV, (int)gridSize);
        float u = CellToNormalized(cellU, gridSize);
        float v = CellToNormalized(cellV, gridSize);
        float du = 2f / gridSize;
        float dv = 2f / gridSize;

        up = GetRadialDirection(face, cellU, cellV, gridSize);

        Vector3 radialUPlus = CubeToSphere(GetFaceCubePoint(face, u + du, v)).normalized;
        Vector3 radialUMinus = CubeToSphere(GetFaceCubePoint(face, u - du, v)).normalized;
        right = (radialUPlus - radialUMinus).normalized;

        Vector3 radialVPlus = CubeToSphere(GetFaceCubePoint(face, u, v + dv)).normalized;
        Vector3 radialVMinus = CubeToSphere(GetFaceCubePoint(face, u, v - dv)).normalized;
        Vector3 vTangent = (radialVPlus - radialVMinus).normalized;

        forward = Vector3.Cross(up, right).normalized;
        if (Vector3.Dot(forward, vTangent) < 0f)
            forward = -forward;
    }

    public static QuadSphereFace GetDominantFace(Vector3 directionFromCenter)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(107);
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

    public static QuadSphereVoxelAddress RemapAcrossFace(
        QuadSphereVoxelAddress address,
        int gridSize)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(108, (int)gridSize);
        if (address.U >= 0 && address.U < gridSize
            && address.V >= 0 && address.V < gridSize)
            return address;

        // Sample the center of the virtual cell just beyond the cube edge,
        // then project it onto the adjacent dominant face.
        float u = CellToNormalized(address.U, gridSize);
        float v = CellToNormalized(address.V, gridSize);
        Vector3 direction = GetFaceCubePoint(address.Face, u, v).normalized;
        QuadSphereFace adjacentFace = GetDominantFace(direction);
        Vector2 adjacentUv = DirectionToFaceUV(direction, adjacentFace);

        return new QuadSphereVoxelAddress(
            adjacentFace,
            NormalizedToCell(adjacentUv.x, gridSize),
            NormalizedToCell(adjacentUv.y, gridSize),
            address.Depth
        );
    }

    public static bool TryLocalPointToVoxel(
        Vector3 localPoint,
        Vector3 planetCenter,
        float planetRadius,
        int gridSize,
        int maxDepth,
        out QuadSphereVoxelAddress address)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(109, (int)planetRadius, (int)gridSize, (int)maxDepth);
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
            case QuadSphereFace.PosZ: return new Vector2(dir.x / dir.z, -dir.y / dir.z);
            default: return new Vector2(-dir.x / dir.z, -dir.y / dir.z);
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
