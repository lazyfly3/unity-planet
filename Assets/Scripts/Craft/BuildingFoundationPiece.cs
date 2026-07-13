using UnityEngine;

public class BuildingFoundationPiece : MonoBehaviour
{
    public BuildingAnchor Anchor { get; private set; }
    public Vector2Int Grid { get; private set; }

    public static BuildingFoundationPiece Create(BuildingAnchor anchor, Vector2Int grid, Material material)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(22);
        var pieceObject = new GameObject($"Foundation_{grid.x}_{grid.y}");
        pieceObject.transform.SetParent(anchor.transform, false);

        var piece = pieceObject.AddComponent<BuildingFoundationPiece>();
        piece.Anchor = anchor;
        piece.Grid = grid;
        piece.BuildVisuals(material);
        piece.AlignToGrid(grid);

        return piece;
    }

    public void BuildVisuals(Material material)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(23);
        for (int i = transform.childCount - 1; i >= 0; i--)
            Destroy(transform.GetChild(i).gameObject);

        float cellSize = Anchor.CellSize;
        float slabHeight = Anchor.SlabHeight;
        float pillarHeight = Anchor.PillarHeight;
        float pillarSize = Anchor.PillarSize;
        float cornerOffset = cellSize * 0.4f;
        float pillarCenterY = -(pillarHeight + slabHeight) * 0.5f;

        CreateBox("Slab", Vector3.zero, new Vector3(cellSize * 0.98f, slabHeight, cellSize * 0.98f), material, true);
        CreateBox("Pillar_NE", new Vector3(cornerOffset, pillarCenterY, cornerOffset), new Vector3(pillarSize, pillarHeight, pillarSize), material, false);
        CreateBox("Pillar_NW", new Vector3(-cornerOffset, pillarCenterY, cornerOffset), new Vector3(pillarSize, pillarHeight, pillarSize), material, false);
        CreateBox("Pillar_SE", new Vector3(cornerOffset, pillarCenterY, -cornerOffset), new Vector3(pillarSize, pillarHeight, pillarSize), material, false);
        CreateBox("Pillar_SW", new Vector3(-cornerOffset, pillarCenterY, -cornerOffset), new Vector3(pillarSize, pillarHeight, pillarSize), material, false);
    }

    void CreateBox(string objectName, Vector3 localPosition, Vector3 localScale, Material material, bool enableCollider)
    {
        var boxObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        boxObject.name = objectName;
        boxObject.transform.SetParent(transform, false);
        boxObject.transform.localPosition = localPosition;
        boxObject.transform.localScale = localScale;

        if (material != null)
            boxObject.GetComponent<MeshRenderer>().sharedMaterial = material;

        if (!enableCollider)
            Destroy(boxObject.GetComponent<Collider>());
    }

    public void AlignToGrid(Vector2Int grid)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(24);
        Grid = grid;
        if (Anchor == null)
            return;

        transform.localPosition = Anchor.GridToLocalPosition(grid);
    }
}
