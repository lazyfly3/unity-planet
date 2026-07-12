using UnityEngine;

public class BuildingFoundationPiece : MonoBehaviour
{
    public BuildingAnchor Anchor { get; private set; }
    public Vector2Int Grid { get; private set; }

    public static BuildingFoundationPiece Create(BuildingAnchor anchor, Vector2Int grid, Material material)
    {
        var pieceObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pieceObject.name = $"Foundation_{grid.x}_{grid.y}";
        pieceObject.transform.SetParent(anchor.transform, false);

        var piece = pieceObject.AddComponent<BuildingFoundationPiece>();
        piece.Anchor = anchor;
        piece.Grid = grid;
        piece.AlignToGrid(grid);

        float cellSize = anchor.CellSize;
        float slabHeight = anchor.SlabHeight;
        pieceObject.transform.localScale = new Vector3(cellSize * 0.98f, slabHeight, cellSize * 0.98f);

        var renderer = pieceObject.GetComponent<MeshRenderer>();
        if (material != null)
            renderer.sharedMaterial = material;

        return piece;
    }

    public void AlignToGrid(Vector2Int grid)
    {
        Grid = grid;
        if (Anchor == null)
            return;

        transform.localPosition = Anchor.GridToLocalPosition(grid);
    }
}
