using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 局部切平面建造锚点：在球面上定义一套方形网格（Right / Up / Forward）。
/// </summary>
public class BuildingAnchor : MonoBehaviour
{
    static readonly Vector2Int[] CardinalOffsets =
    {
        new Vector2Int(1, 0),
        new Vector2Int(-1, 0),
        new Vector2Int(0, 1),
        new Vector2Int(0, -1)
    };

    static readonly List<BuildingAnchor> ActiveAnchors = new List<BuildingAnchor>();

    readonly HashSet<Vector2Int> occupiedCells = new HashSet<Vector2Int>();
    readonly Dictionary<Vector2Int, BuildingFoundationPiece> pieces = new Dictionary<Vector2Int, BuildingFoundationPiece>();

    [SerializeField] float cellSize = 2f;
    [SerializeField] float slabHeight = 0.3f;
    [SerializeField] float pillarHeight = 1.5f;
    [SerializeField] float pillarSize = 0.15f;

    Material foundationMaterial;

    public float CellSize => cellSize;
    public float SlabHeight => slabHeight;
    public float PillarHeight => pillarHeight;
    public float PillarSize => pillarSize;
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
        float pillarHeight,
        float pillarSize,
        Material material)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(2, (int)cellSize, (int)slabHeight, (int)pillarHeight, (int)pillarSize);}
    try
    {
        var root = new GameObject("BuildingAnchor");
        var anchor = root.AddComponent<BuildingAnchor>();
        anchor.Initialize(surfacePoint, up, forward, cellSize, slabHeight, pillarHeight, pillarSize, material);
        return anchor;
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    void OnEnable()
    {
        if (transform != null && Forward.sqrMagnitude > 0.0001f)
            SyncAxesFromTransform();
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
        float pillarHeight,
        float pillarSize,
        Material material)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(3, (int)cellSize, (int)slabHeight, (int)pillarHeight, (int)pillarSize);}
    try
    {
        this.cellSize = cellSize;
        this.slabHeight = slabHeight;
        this.pillarHeight = pillarHeight;
        this.pillarSize = pillarSize;
        foundationMaterial = material;

        ApplyFrame(surfacePoint, up, forward);

        if (!ActiveAnchors.Contains(this))
            ActiveAnchors.Add(this);
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public void ApplyFrame(Vector3 surfacePoint, Vector3 up, Vector3 forward)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(4);}
    try
    {
        Up = up.normalized;
        Forward = Vector3.ProjectOnPlane(forward, Up);
        if (Forward.sqrMagnitude < 0.0001f)
            Forward = Vector3.Cross(Up, Vector3.forward);
        Forward.Normalize();
        OriginWorld = surfacePoint;
        transform.SetPositionAndRotation(OriginWorld, Quaternion.LookRotation(Forward, Up));
        SyncAxesFromTransform();
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    void SyncAxesFromTransform()
    {
        Up = transform.up;
        Forward = transform.forward;
        Right = transform.right;
    }

    public void RotateForward90()
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(5);}
    try
    {
        Forward = (Quaternion.AngleAxis(90f, Up) * Forward).normalized;
        transform.rotation = Quaternion.LookRotation(Forward, Up);
        SyncAxesFromTransform();
        RepositionAllPieces();
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public bool TrySnapWorldPoint(Vector3 worldPoint, out Vector2Int grid)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(6);}
    try
    {
        Vector3 local = WorldToLocal(worldPoint);
        grid = new Vector2Int(
            Mathf.RoundToInt(local.x / cellSize),
            Mathf.RoundToInt(local.z / cellSize)
        );
        return true;
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public Vector3 ProjectPointOntoPlane(Vector3 worldPoint)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(7);}
    try
    {
        Vector3 local = WorldToLocal(worldPoint);
        local.y = 0f;
        return LocalToWorld(local);
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public bool TryResolvePlacementGrid(Vector3 worldPoint, out Vector2Int grid)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(8);}
    try
    {
        TrySnapWorldPoint(worldPoint, out grid);
        if (CanPlace(grid) && IsConnectedPlacement(grid))
            return true;

        if (occupiedCells.Count == 0)
            return CanPlace(grid);

        float bestDistance = float.MaxValue;
        bool found = false;
        Vector2Int bestGrid = grid;

        foreach (Vector2Int occupied in occupiedCells)
        {
            for (int i = 0; i < CardinalOffsets.Length; i++)
            {
                Vector2Int candidate = occupied + CardinalOffsets[i];
                if (!CanPlace(candidate) || !IsConnectedPlacement(candidate))
                    continue;

                float distance = Vector3.Distance(worldPoint, GetCellWorldCenter(candidate));
                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                bestGrid = candidate;
                found = true;
            }
        }

        if (found)
        {
            grid = bestGrid;
            return true;
        }

        return CanPlace(grid) && IsConnectedPlacement(grid);
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public float GetNearestCellDistance(Vector3 worldPoint)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(9);}
    try
    {
        if (occupiedCells.Count == 0)
            return Vector3.Distance(worldPoint, OriginWorld);

        float bestDistance = float.MaxValue;
        foreach (Vector2Int cell in occupiedCells)
        {
            float distance = Vector3.Distance(worldPoint, GetCellWorldCenter(cell));
            if (distance < bestDistance)
                bestDistance = distance;
        }

        return bestDistance;
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public Vector3 GetSlabLocalCenter(Vector2Int grid)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(10);}
    try
    {
        return new Vector3(
            grid.x * cellSize,
            pillarHeight + slabHeight * 0.5f,
            grid.y * cellSize);
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public Vector3 GetCellWorldCenter(Vector2Int grid)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(11);}
    try
    {
        return LocalToWorld(GetSlabLocalCenter(grid));
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public Vector3 GridToLocalPosition(Vector2Int grid)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(12);}
    try
    {
        return GetSlabLocalCenter(grid);
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public Vector3 WorldToLocal(Vector3 worldPoint)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(13);}
    try
    {
        Vector3 offset = worldPoint - OriginWorld;
        return new Vector3(
            Vector3.Dot(offset, Right),
            Vector3.Dot(offset, Up),
            Vector3.Dot(offset, Forward)
        );
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public Vector3 LocalToWorld(Vector3 localPoint)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(14);}
    try
    {
        return OriginWorld + Right * localPoint.x + Up * localPoint.y + Forward * localPoint.z;
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public bool CanPlace(Vector2Int grid)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(15);}
    try
    {
        return !occupiedCells.Contains(grid);
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public bool IsConnectedPlacement(Vector2Int grid)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(16);}
    try
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
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public bool TryPlace(Vector2Int grid, out BuildingFoundationPiece piece)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(17);}
    try
    {
        piece = null;
        if (!CanPlace(grid) || !IsConnectedPlacement(grid))
            return false;

        piece = BuildingFoundationPiece.Create(this, grid, foundationMaterial);
        occupiedCells.Add(grid);
        pieces[grid] = piece;
        return true;
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public bool TryGetPiece(Vector2Int grid, out BuildingFoundationPiece piece)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(18);}
    try
    {
        return pieces.TryGetValue(grid, out piece);
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public int RestoreCells(IReadOnlyList<Vector2Int> cells)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(22);}
    try
    {
        if (cells == null || cells.Count == 0)
            return 0;

        var pending = new HashSet<Vector2Int>(cells);
        int restored = 0;
        bool madeProgress = true;
        while (pending.Count > 0 && madeProgress)
        {
            madeProgress = false;
            var placedThisPass = new List<Vector2Int>();
            foreach (Vector2Int cell in pending)
            {
                if (!TryPlace(cell, out _))
                    continue;
                placedThisPass.Add(cell);
                restored++;
                madeProgress = true;
            }

            foreach (Vector2Int cell in placedThisPass)
                pending.Remove(cell);
        }

        return restored;
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    void RepositionAllPieces()
    {
        foreach (KeyValuePair<Vector2Int, BuildingFoundationPiece> entry in pieces)
        {
            if (entry.Value != null)
                entry.Value.AlignToGrid(entry.Key);
        }
    }

    public static Vector3 GetSlabWorldCenter(
        Vector3 originWorld,
        Vector3 right,
        Vector3 up,
        Vector3 forward,
        float cellSize,
        float slabHeight,
        float pillarHeight,
        Vector2Int grid)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(19, (int)cellSize, (int)slabHeight, (int)pillarHeight);}
    try
    {
        Vector3 local = new Vector3(
            grid.x * cellSize,
            pillarHeight + slabHeight * 0.5f,
            grid.y * cellSize);
        return originWorld + right * local.x + up * local.y + forward * local.z;
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public static bool TrySnapWorldPointToGrid(
        Vector3 worldPoint,
        Vector3 originWorld,
        Vector3 right,
        Vector3 up,
        Vector3 forward,
        float cellSize,
        out Vector2Int grid)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(20, (int)cellSize);}
    try
    {
        Vector3 offset = worldPoint - originWorld;
        Vector3 local = new Vector3(
            Vector3.Dot(offset, right),
            Vector3.Dot(offset, up),
            Vector3.Dot(offset, forward));
        grid = new Vector2Int(
            Mathf.RoundToInt(local.x / cellSize),
            Mathf.RoundToInt(local.z / cellSize)
        );
        return true;
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public static Vector3 ProjectPointOntoFramePlane(
        Vector3 worldPoint,
        Vector3 originWorld,
        Vector3 right,
        Vector3 up,
        Vector3 forward)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(21);}
    try
    {
        Vector3 offset = worldPoint - originWorld;
        float localY = Vector3.Dot(offset, up);
        return worldPoint - up * localY;
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}
}
