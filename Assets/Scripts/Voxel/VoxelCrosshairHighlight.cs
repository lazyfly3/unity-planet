using UnityEngine;

public class VoxelCrosshairHighlight : MonoBehaviour
{
    [Header("引用")]
    [SerializeField] VoxelWorld voxelWorld;
    [SerializeField] Camera viewCamera;

    [Header("射线")]
    [SerializeField] float reach = 6f;

    [Header("准星")]
    [SerializeField] bool showCrosshair = true;
    [SerializeField] float crosshairSize = 8f;
    [SerializeField] float crosshairThickness = 2f;
    [SerializeField] Color crosshairColor = Color.white;
    [SerializeField] Color crosshairIdleColor = new Color(1f, 1f, 1f, 0.45f);

    [Header("高亮")]
    [SerializeField] Material highlightMaterial;
    [SerializeField] float highlightScale = 1.002f;

    Transform highlightTransform;
    Renderer highlightRenderer;
    bool hasTarget;
    Vector3Int currentTarget;

    public bool HasTarget => hasTarget;
    public Vector3Int CurrentTarget => currentTarget;

    void Awake()
    {
        if (viewCamera == null)
            viewCamera = Camera.main;

        CreateHighlightObject();
    }

    void CreateHighlightObject()
    {
        GameObject highlight = GameObject.CreatePrimitive(PrimitiveType.Cube);
        highlight.name = "VoxelHighlight";
        Destroy(highlight.GetComponent<Collider>());

        highlightTransform = highlight.transform;
        highlightRenderer = highlight.GetComponent<Renderer>();

        if (highlightMaterial != null)
            highlightRenderer.sharedMaterial = highlightMaterial;

        highlightTransform.localScale = Vector3.one * highlightScale;
        highlightTransform.gameObject.SetActive(false);
    }

    void Update()
    {
        UpdateTarget();
    }

    void UpdateTarget()
    {
        hasTarget = false;

        if (voxelWorld == null || viewCamera == null)
        {
            HideHighlight();
            return;
        }

        Ray ray = new Ray(viewCamera.transform.position, viewCamera.transform.forward);
        if (!VoxelRaycastUtility.TryGetTargetVoxel(ray, reach, voxelWorld.transform, out Vector3Int voxelCoord, out _))
        {
            HideHighlight();
            return;
        }

        if (!VoxelTypes.IsSolid(voxelWorld.GetVoxel(voxelCoord.x, voxelCoord.y, voxelCoord.z)))
        {
            HideHighlight();
            return;
        }

        hasTarget = true;
        currentTarget = voxelCoord;
        ShowHighlight(voxelCoord);
    }

    void ShowHighlight(Vector3Int voxelCoord)
    {
        if (highlightTransform.parent != voxelWorld.transform)
            highlightTransform.SetParent(voxelWorld.transform, false);

        highlightTransform.gameObject.SetActive(true);
        highlightTransform.localPosition = VoxelRaycastUtility.GetVoxelLocalCenter(voxelCoord);
        highlightTransform.localRotation = Quaternion.identity;
        highlightTransform.localScale = Vector3.one * highlightScale;
    }

    void HideHighlight()
    {
        if (highlightTransform != null)
            highlightTransform.gameObject.SetActive(false);
    }

    void OnGUI()
    {
        if (!showCrosshair)
            return;

        Color color = hasTarget ? crosshairColor : crosshairIdleColor;
        DrawCrosshair(Screen.width * 0.5f, Screen.height * 0.5f, crosshairSize, crosshairThickness, color);
    }

    static void DrawCrosshair(float centerX, float centerY, float size, float thickness, Color color)
    {
        GUI.color = color;
        Texture2D tex = Texture2D.whiteTexture;

        GUI.DrawTexture(new Rect(centerX - size, centerY - thickness * 0.5f, size * 2f, thickness), tex);
        GUI.DrawTexture(new Rect(centerX - thickness * 0.5f, centerY - size, thickness, size * 2f), tex);
    }
}
