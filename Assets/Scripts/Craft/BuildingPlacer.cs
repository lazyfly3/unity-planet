using UnityEngine;

/// <summary>
/// 在球面切平面上放置方形地基。B 切换建造模式，左键放置，N 强制新锚点，R 旋转网格。
/// </summary>
public class BuildingPlacer : MonoBehaviour
{
    struct PlacementSnapshot
    {
        public bool HasHit;
        public bool CanPlace;
        public bool UsesExistingAnchor;
        public BuildingAnchor ExistingAnchor;
        public Vector2Int Grid;
        public Vector3 Center;
        public Quaternion Rotation;
        public Vector3 DraftOrigin;
        public Vector3 DraftUp;
        public Vector3 DraftForward;
    }

    [Header("世界")]
    [SerializeField] VoxelQuadSphereWorld quadSphereWorld;
    [SerializeField] Camera buildCamera;

    [Header("网格")]
    [SerializeField] float cellSize = 2f;
    [SerializeField] float slabHeight = 0.3f;
    [SerializeField] float pillarHeight = 1.5f;
    [SerializeField] float pillarSize = 0.15f;
    [SerializeField] float reach = 10f;
    [SerializeField] float joinRadius = 32f;
    [SerializeField] Material foundationMaterial;

    [Header("预览")]
    [SerializeField] Color previewValidColor = new Color(0.2f, 0.85f, 0.35f, 0.45f);
    [SerializeField] Color previewInvalidColor = new Color(0.9f, 0.25f, 0.2f, 0.45f);

    public bool IsBuildMode => buildMode;

    bool buildMode;
    bool forceNewAnchor;
    bool hasDraftFrame;
    Vector3 draftOrigin;
    Vector3 draftUp;
    Vector3 draftForward;
    PlacementSnapshot currentSnapshot;
    Transform previewRoot;
    Material previewMaterial;

    void Awake()
    {
        if (buildCamera == null)
            buildCamera = Camera.main;

        CreatePreviewObject();
    }

    void LateUpdate()
    {
        if (!buildMode)
            return;

        currentSnapshot = ComputePlacementSnapshot();
        ApplyPreview(currentSnapshot);

        if (Input.GetMouseButtonDown(0))
            PlaceFromSnapshot(currentSnapshot);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.B))
        {
            buildMode = !buildMode;
            if (!buildMode)
            {
                ClearDraftFrame();
                SetPreviewVisible(false);
            }
        }

        if (!buildMode)
            return;

        if (Input.GetKeyDown(KeyCode.N))
        {
            forceNewAnchor = true;
            ClearDraftFrame();
        }

        if (Input.GetKeyDown(KeyCode.R))
            RotateDraftOrEmptyAnchor();
    }

    void OnGUI()
    {
        if (!buildMode)
            return;

        GUI.Label(new Rect(12f, 12f, 560f, 24f), "建造模式：左键放置 | N 新地基 | R 旋转 | B 退出");
    }

    void RotateDraftOrEmptyAnchor()
    {
        if (hasDraftFrame)
        {
            draftForward = (Quaternion.AngleAxis(90f, draftUp) * draftForward).normalized;
            return;
        }

        if (currentSnapshot.UsesExistingAnchor
            && currentSnapshot.ExistingAnchor != null
            && currentSnapshot.ExistingAnchor.OccupiedCells.Count == 0)
        {
            currentSnapshot.ExistingAnchor.RotateForward90();
        }
    }

    void ClearDraftFrame()
    {
        hasDraftFrame = false;
        forceNewAnchor = false;
    }

    PlacementSnapshot ComputePlacementSnapshot()
    {
        PlacementSnapshot snapshot = default;

        if (buildCamera == null || quadSphereWorld == null)
            return snapshot;

        Ray ray = new Ray(buildCamera.transform.position, buildCamera.transform.forward);
        if (!Physics.Raycast(ray, out RaycastHit hit, reach))
        {
            ClearDraftFrame();
            return snapshot;
        }

        snapshot.HasHit = true;
        Vector3 planetCenter = quadSphereWorld.GetPlanetCenterWorld();
        Vector3 hitUp = PlanetGravity.GetUp(hit.point, planetCenter);

        var hitPiece = hit.collider.GetComponentInParent<BuildingFoundationPiece>();
        if (hitPiece != null)
        {
            BuildingAnchor anchor = hitPiece.Anchor;
            Vector3 projectedPoint = anchor.ProjectPointOntoPlane(hit.point);
            anchor.TryResolvePlacementGrid(projectedPoint, out Vector2Int grid);
            snapshot.UsesExistingAnchor = true;
            snapshot.ExistingAnchor = anchor;
            snapshot.Grid = grid;
            snapshot.Center = anchor.GetCellWorldCenter(grid);
            snapshot.Rotation = anchor.transform.rotation;
            snapshot.CanPlace = anchor.CanPlace(grid) && anchor.IsConnectedPlacement(grid);
            ClearDraftFrame();
            return snapshot;
        }

        BuildingAnchor joinAnchor = forceNewAnchor ? null : FindJoinableAnchor(hit.point);
        if (joinAnchor != null)
        {
            Vector3 projectedPoint = joinAnchor.ProjectPointOntoPlane(hit.point);
            joinAnchor.TryResolvePlacementGrid(projectedPoint, out Vector2Int grid);
            snapshot.UsesExistingAnchor = true;
            snapshot.ExistingAnchor = joinAnchor;
            snapshot.Grid = grid;
            snapshot.Center = joinAnchor.GetCellWorldCenter(grid);
            snapshot.Rotation = joinAnchor.transform.rotation;
            snapshot.CanPlace = joinAnchor.CanPlace(grid) && joinAnchor.IsConnectedPlacement(grid);
            ClearDraftFrame();
            return snapshot;
        }

        if (!hasDraftFrame || forceNewAnchor)
        {
            draftOrigin = hit.point;
            draftUp = hitUp;
            draftForward = Vector3.ProjectOnPlane(transform.forward, draftUp).normalized;
            if (draftForward.sqrMagnitude < 0.0001f)
                draftForward = Vector3.Cross(draftUp, Vector3.forward).normalized;
            hasDraftFrame = true;
            forceNewAnchor = false;
        }
        else
        {
            draftOrigin = hit.point;
            draftUp = hitUp;
        }

        Vector3 draftRight = Vector3.Cross(draftUp, draftForward).normalized;
        Vector3 projectedDraftPoint = BuildingAnchor.ProjectPointOntoFramePlane(
            hit.point, draftOrigin, draftRight, draftUp, draftForward);
        BuildingAnchor.TrySnapWorldPointToGrid(
            projectedDraftPoint, draftOrigin, draftRight, draftUp, draftForward, cellSize, out Vector2Int draftGrid);

        snapshot.UsesExistingAnchor = false;
        snapshot.Grid = draftGrid;
        snapshot.DraftOrigin = draftOrigin;
        snapshot.DraftUp = draftUp;
        snapshot.DraftForward = draftForward;
        snapshot.Center = BuildingAnchor.GetSlabWorldCenter(
            draftOrigin, draftRight, draftUp, draftForward, cellSize, slabHeight, pillarHeight, draftGrid);
        snapshot.Rotation = Quaternion.LookRotation(draftForward, draftUp);
        snapshot.CanPlace = true;
        return snapshot;
    }

    void PlaceFromSnapshot(PlacementSnapshot snapshot)
    {
        if (!snapshot.HasHit || !snapshot.CanPlace)
            return;

        if (snapshot.UsesExistingAnchor)
        {
            if (snapshot.ExistingAnchor == null)
                return;

            snapshot.ExistingAnchor.TryPlace(snapshot.Grid, out _);
            return;
        }

        BuildingAnchor anchor = BuildingAnchor.Create(
            snapshot.DraftOrigin,
            snapshot.DraftUp,
            snapshot.DraftForward,
            cellSize,
            slabHeight,
            pillarHeight,
            pillarSize,
            foundationMaterial);
        anchor.TryPlace(snapshot.Grid, out _);
        ClearDraftFrame();
    }

    void ApplyPreview(PlacementSnapshot snapshot)
    {
        if (!snapshot.HasHit)
        {
            SetPreviewVisible(false);
            return;
        }

        SetPreviewVisible(true);
        previewRoot.SetPositionAndRotation(snapshot.Center, snapshot.Rotation);
        previewMaterial.color = snapshot.CanPlace ? previewValidColor : previewInvalidColor;
    }

    BuildingAnchor FindJoinableAnchor(Vector3 worldPoint)
    {
        BuildingAnchor bestAnchor = null;
        float bestScore = float.MaxValue;

        foreach (BuildingAnchor candidate in BuildingAnchor.GetActiveAnchors())
        {
            if (candidate.GetNearestCellDistance(worldPoint) > joinRadius)
                continue;

            Vector3 projectedPoint = candidate.ProjectPointOntoPlane(worldPoint);
            if (!candidate.TryResolvePlacementGrid(projectedPoint, out Vector2Int grid))
                continue;

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
        previewRoot = new GameObject("FoundationPreview").transform;

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

        CreatePreviewBox("Slab", Vector3.zero, new Vector3(cellSize * 0.98f, slabHeight, cellSize * 0.98f));

        float cornerOffset = cellSize * 0.4f;
        float pillarCenterY = -(pillarHeight + slabHeight) * 0.5f;
        Vector3 pillarScale = new Vector3(pillarSize, pillarHeight, pillarSize);
        CreatePreviewBox("Pillar_NE", new Vector3(cornerOffset, pillarCenterY, cornerOffset), pillarScale);
        CreatePreviewBox("Pillar_NW", new Vector3(-cornerOffset, pillarCenterY, cornerOffset), pillarScale);
        CreatePreviewBox("Pillar_SE", new Vector3(cornerOffset, pillarCenterY, -cornerOffset), pillarScale);
        CreatePreviewBox("Pillar_SW", new Vector3(-cornerOffset, pillarCenterY, -cornerOffset), pillarScale);

        SetPreviewVisible(false);
    }

    void CreatePreviewBox(string objectName, Vector3 localPosition, Vector3 localScale)
    {
        var boxObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        boxObject.name = objectName;
        boxObject.transform.SetParent(previewRoot, false);
        boxObject.transform.localPosition = localPosition;
        boxObject.transform.localScale = localScale;
        Destroy(boxObject.GetComponent<Collider>());
        boxObject.GetComponent<MeshRenderer>().sharedMaterial = previewMaterial;
    }

    void SetPreviewVisible(bool visible)
    {
        if (previewRoot != null)
            previewRoot.gameObject.SetActive(visible);
    }

    void OnDestroy()
    {
        if (previewMaterial != null)
            Destroy(previewMaterial);
    }
}
