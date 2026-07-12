using UnityEngine;

/// <summary>
/// 在球面切平面上放置方形地基。B 切换建造模式，左键放置，N 强制新锚点，R 旋转网格。
/// </summary>
public class BuildingPlacer : MonoBehaviour
{
    [Header("世界")]
    [SerializeField] VoxelQuadSphereWorld quadSphereWorld;
    [SerializeField] Camera buildCamera;

    [Header("网格")]
    [SerializeField] float cellSize = 2f;
    [SerializeField] float slabHeight = 0.3f;
    [SerializeField] float reach = 10f;
    [SerializeField] float joinRadius = 24f;
    [SerializeField] Material foundationMaterial;

    [Header("预览")]
    [SerializeField] Color previewValidColor = new Color(0.2f, 0.85f, 0.35f, 0.45f);
    [SerializeField] Color previewInvalidColor = new Color(0.9f, 0.25f, 0.2f, 0.45f);

    bool buildMode;
    bool forceNewAnchor;
    BuildingAnchor pendingAnchor;
    Transform previewTransform;
    Material previewMaterial;
    Renderer previewRenderer;

    void Awake()
    {
        if (buildCamera == null)
            buildCamera = Camera.main;

        CreatePreviewObject();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.B))
            buildMode = !buildMode;

        if (!buildMode)
        {
            SetPreviewVisible(false);
            return;
        }

        if (Input.GetKeyDown(KeyCode.N))
        {
            forceNewAnchor = true;
            if (pendingAnchor != null && pendingAnchor.OccupiedCells.Count == 0)
            {
                Destroy(pendingAnchor.gameObject);
                pendingAnchor = null;
            }
        }

        if (Input.GetKeyDown(KeyCode.R))
            HandleRotate();

        UpdatePreview();

        if (Input.GetMouseButtonDown(0))
            TryPlaceFoundation();
    }

    void OnGUI()
    {
        if (!buildMode)
            return;

        GUI.Label(new Rect(12f, 12f, 520f, 24f), "建造模式：左键放置 | N 新地基 | R 旋转 | B 退出");
    }

    void HandleRotate()
    {
        if (pendingAnchor != null)
        {
            pendingAnchor.RotateForward90();
            return;
        }

        if (TryGetPlacement(out BuildingAnchor anchor, out _, out _, out _) && anchor != null && anchor.OccupiedCells.Count == 0)
            anchor.RotateForward90();
    }

    void TryPlaceFoundation()
    {
        if (!TryGetPlacement(out BuildingAnchor anchor, out Vector2Int grid, out _, out bool canPlace))
            return;

        if (!canPlace)
            return;

        if (anchor == null)
        {
            anchor = pendingAnchor;
            pendingAnchor = null;
        }

        if (anchor == null)
            return;

        if (anchor.TryPlace(grid, out _))
        {
            forceNewAnchor = false;
            pendingAnchor = null;
        }
    }

    void UpdatePreview()
    {
        if (!TryGetPlacement(out BuildingAnchor anchor, out Vector2Int grid, out Vector3 center, out bool canPlace))
        {
            SetPreviewVisible(false);
            return;
        }

        SetPreviewVisible(true);
        previewTransform.position = center;
        if (anchor != null)
            previewTransform.rotation = anchor.transform.rotation;
        else if (pendingAnchor != null)
            previewTransform.rotation = pendingAnchor.transform.rotation;

        previewMaterial.color = canPlace ? previewValidColor : previewInvalidColor;
    }

    bool TryGetPlacement(
        out BuildingAnchor anchor,
        out Vector2Int grid,
        out Vector3 cellCenter,
        out bool canPlace)
    {
        anchor = null;
        grid = default;
        cellCenter = default;
        canPlace = false;

        if (buildCamera == null || quadSphereWorld == null)
            return false;

        Ray ray = new Ray(buildCamera.transform.position, buildCamera.transform.forward);
        if (!Physics.Raycast(ray, out RaycastHit hit, reach))
            return false;

        Vector3 planetCenter = quadSphereWorld.GetPlanetCenterWorld();
        Vector3 up = PlanetGravity.GetUp(hit.point, planetCenter);

        var hitPiece = hit.collider.GetComponentInParent<BuildingFoundationPiece>();
        if (hitPiece != null)
        {
            anchor = hitPiece.Anchor;
            anchor.TrySnapWorldPoint(hit.point, out grid);
            cellCenter = anchor.GetCellWorldCenter(grid);
            canPlace = anchor.CanPlace(grid) && anchor.IsConnectedPlacement(grid);
            return true;
        }

        if (!forceNewAnchor)
            anchor = FindJoinableAnchor(hit.point, up);

        if (anchor == null)
        {
            if (pendingAnchor == null || forceNewAnchor)
            {
                Vector3 forward = Vector3.ProjectOnPlane(transform.forward, up).normalized;
                pendingAnchor = BuildingAnchor.Create(
                    hit.point,
                    up,
                    forward,
                    cellSize,
                    slabHeight,
                    foundationMaterial);
                forceNewAnchor = false;
            }

            anchor = pendingAnchor;
            anchor.TrySnapWorldPoint(hit.point, out grid);
            cellCenter = anchor.GetCellWorldCenter(grid);
            canPlace = anchor.CanPlace(grid) && anchor.IsConnectedPlacement(grid);
            return true;
        }

        anchor.TrySnapWorldPoint(hit.point, out grid);
        cellCenter = anchor.GetCellWorldCenter(grid);
        canPlace = anchor.CanPlace(grid) && anchor.IsConnectedPlacement(grid);
        pendingAnchor = null;
        return true;
    }

    BuildingAnchor FindJoinableAnchor(Vector3 worldPoint, Vector3 up)
    {
        BuildingAnchor bestAnchor = null;
        float bestScore = float.MaxValue;

        foreach (BuildingAnchor candidate in BuildingAnchor.GetActiveAnchors())
        {
            if (Vector3.Distance(worldPoint, candidate.OriginWorld) > joinRadius)
                continue;

            if (Vector3.Dot(up, candidate.Up) < 0.95f)
                continue;

            candidate.TrySnapWorldPoint(worldPoint, out Vector2Int grid);
            if (!candidate.CanPlace(grid) || !candidate.IsConnectedPlacement(grid))
                continue;

            float score = Vector3.Distance(worldPoint, candidate.GetCellWorldCenter(grid));
            if (score < bestScore)
            {
                bestScore = score;
                bestAnchor = candidate;
            }
        }

        return bestAnchor;
    }

    void CreatePreviewObject()
    {
        previewTransform = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
        previewTransform.name = "FoundationPreview";
        Destroy(previewTransform.GetComponent<Collider>());

        previewRenderer = previewTransform.GetComponent<MeshRenderer>();
        previewMaterial = new Material(Shader.Find("Standard"))
        {
            renderQueue = 3000
        };
        previewMaterial.SetFloat("_Mode", 3f);
        previewMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        previewMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        previewMaterial.SetInt("_ZWrite", 0);
        previewMaterial.DisableKeyword("_ALPHATEST_ON");
        previewMaterial.EnableKeyword("_ALPHABLEND_ON");
        previewMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        previewMaterial.color = previewValidColor;
        previewRenderer.sharedMaterial = previewMaterial;
        previewTransform.localScale = new Vector3(cellSize * 0.98f, slabHeight, cellSize * 0.98f);
        SetPreviewVisible(false);
    }

    void SetPreviewVisible(bool visible)
    {
        if (previewTransform != null)
            previewTransform.gameObject.SetActive(visible);
    }

    void OnDestroy()
    {
        if (previewMaterial != null)
            Destroy(previewMaterial);
    }
}
