using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 局部切平面建造锚点：在球面上定义一套方形网格（Right / Up / Forward）。
/// </summary>
public class BuildingAnchor : MonoBehaviour
{
    static readonly List<BuildingAnchor> ActiveAnchors = new List<BuildingAnchor>();

    readonly HashSet<Vector2Int> occupiedCells = new HashSet<Vector2Int>();
    readonly Dictionary<Vector2Int, BuildingFoundationPiece> pieces = new Dictionary<Vector2Int, BuildingFoundationPiece>();

    [SerializeField] float cellSize = 2f;
    [SerializeField] float slabHeight = 0.3f;

    Material foundationMaterial;

    public float CellSize => cellSize;
    public float SlabHeight => slabHeight;
    public Vector3 Up { get; private set; }
    public Vector3 Right { get; private set; }
    public Vector3 Forward { get; private set; }
    public Vector3 OriginWorld { get; private set; }
    public IReadOnlyCollection<Vector2Int> OccupiedCells => occupiedCells;

    public static IReadOnlyList<BuildingAnchor> GetActiveAnchors() => ActiveAnchors;

    public static BuildingAnchor Create(
        Vector3 surfacePoint,
        Vector3 up,
        Vector3 forward,
        float cellSize,
        float slabHeight,
        Material material)
    {
        var root = new GameObject("BuildingAnchor");
        var anchor = root.AddComponent<BuildingAnchor>();
        anchor.Initialize(surfacePoint, up, forward, cellSize, slabHeight, material);
        return anchor;
    }

    void OnDestroy()
    {
        ActiveAnchors.Remove(this);
    }

    public void Initialize(
        Vector3 surfacePoint,
        Vector3 up,
        Vector3 forward,
        float cellSize,
        float slabHeight,
        Material material)
    {
        this.cellSize = cellSize;
        this.slabHeight = slabHeight;
        foundationMaterial = material;

        Up = up.normalized;
        Forward = Vector3.ProjectOnPlane(forward, Up);
        if (Forward.sqrMagnitude < 0.0001f)
            Forward = Vector3.Cross(Up, Vector3.forward);
        Forward.Normalize();
        Right = Vector3.Cross(Forward, Up).normalized;

        OriginWorld = surfacePoint;
        transform.SetPositionAndRotation(OriginWorld, Quaternion.LookRotation(Forward, Up));

        if (!ActiveAnchors.Contains(this))
            ActiveAnchors.Add(this);
    }

    public void RotateForward90()
    {
        Forward = Vector3.Cross(Up, Right).normalized;
        Right = Vector3.Cross(Forward, Up).normalized;
        transform.rotation = Quaternion.LookRotation(Forward, Up);
        RepositionAllPieces();
    }

    public bool TrySnapWorldPoint(Vector3 worldPoint, out Vector2Int grid)
    {
        Vector3 local = WorldToLocal(worldPoint);
        grid = new Vector2Int(
            Mathf.RoundToInt(local.x / cellSize),
            Mathf.RoundToInt(local.z / cellSize)
        );
        return true;
    }

    public Vector3 GetCellWorldCenter(Vector2Int grid)
    {
        return LocalToWorld(new Vector3(grid.x * cellSize, slabHeight * 0.5f, grid.y * cellSize));
    }

    public Vector3 GridToLocalPosition(Vector2Int grid)
    {
        return new Vector3(grid.x * cellSize, slabHeight * 0.5f, grid.y * cellSize);
    }

    public Vector3 WorldToLocal(Vector3 worldPoint)
    {
        Vector3 offset = worldPoint - OriginWorld;
        return new Vector3(
            Vector3.Dot(offset, Right),
            Vector3.Dot(offset, Up),
            Vector3.Dot(offset, Forward)
        );
    }

    public Vector3 LocalToWorld(Vector3 localPoint)
    {
        return OriginWorld + Right * localPoint.x + Up * localPoint.y + Forward * localPoint.z;
    }

    public bool CanPlace(Vector2Int grid)
    {
        return !occupiedCells.Contains(grid);
    }

    public bool IsConnectedPlacement(Vector2Int grid)
    {
        if (occupiedCells.Count == 0)
            return true;

        foreach (Vector2Int cell in occupiedCells)
        {
            if (Mathf.Abs(cell.x - grid.x) + Mathf.Abs(cell.y - grid.y) == 1)
                return true;
        }

        return false;
    }

    public bool TryPlace(Vector2Int grid, out BuildingFoundationPiece piece)
    {
        piece = null;
        if (!CanPlace(grid) || !IsConnectedPlacement(grid))
            return false;

        piece = BuildingFoundationPiece.Create(this, grid, foundationMaterial);
        occupiedCells.Add(grid);
        pieces[grid] = piece;
        return true;
    }

    public bool TryGetPiece(Vector2Int grid, out BuildingFoundationPiece piece)
    {
        return pieces.TryGetValue(grid, out piece);
    }

    void RepositionAllPieces()
    {
        foreach (KeyValuePair<Vector2Int, BuildingFoundationPiece> entry in pieces)
        {
            if (entry.Value != null)
                entry.Value.AlignToGrid(entry.Key);
        }
    }
}
