using UnityEngine;

namespace UnityPlanet.ModularAssembly
{
    public static class GridBuildBounds
    {
        public const int Min = -16;
        public const int MaxExclusive = 16;
        public const int Size = MaxExclusive - Min;
        public const int EdgeWarningCells = 2;

        public static bool Contains(Vector3Int cell)
        {
            return cell.x >= Min && cell.x < MaxExclusive &&
                   cell.y >= Min && cell.y < MaxExclusive &&
                   cell.z >= Min && cell.z < MaxExclusive;
        }

        public static bool NearEdge(Vector3Int cell)
        {
            return cell.x < Min + EdgeWarningCells ||
                   cell.x >= MaxExclusive - EdgeWarningCells ||
                   cell.y < Min + EdgeWarningCells ||
                   cell.y >= MaxExclusive - EdgeWarningCells ||
                   cell.z < Min + EdgeWarningCells ||
                   cell.z >= MaxExclusive - EdgeWarningCells;
        }

        public static string OutOfBoundsMessage =>
            $"超出{Size}×{Size}×{Size}建造边界。";
    }
}
