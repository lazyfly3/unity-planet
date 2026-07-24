using UnityEngine;

public enum SurfaceToolActionType
{
    None = 0,
    BiotaScanner = 1,
    TerrainCannon = 2,
    Firearm = 3,
    Staff = 4
}

[CreateAssetMenu(
    menuName = "Voxel Planet/Surface Tool Definition",
    fileName = "New Surface Tool")]
public sealed class SurfaceToolDefinition : ScriptableObject
{
    [SerializeField] string itemId = "tool";
    [SerializeField] GameObject viewPrefab;
    [SerializeField] SurfaceToolActionType actionType;
    [SerializeField] Vector3 viewPosition = new Vector3(0.08f, -0.24f, 0.58f);
    [SerializeField] Vector3 viewEulerAngles = new Vector3(4f, -3f, 0f);
    [SerializeField, Range(0f, 1f)] float primaryGripCurl = 0.78f;
    [SerializeField, Range(0f, 1f)] float supportGripCurl = 0.62f;
    [SerializeField, Min(0.1f)] float actionDuration = 1.2f;
    [SerializeField, Min(1f)] float actionRange = 80f;

    public string ItemId => itemId != null ? itemId.Trim() : string.Empty;
    public GameObject ViewPrefab => viewPrefab;
    public SurfaceToolActionType ActionType => actionType;
    public Vector3 ViewPosition => viewPosition;
    public Quaternion ViewRotation => Quaternion.Euler(viewEulerAngles);
    public float PrimaryGripCurl => Mathf.Clamp01(primaryGripCurl);
    public float SupportGripCurl => Mathf.Clamp01(supportGripCurl);
    public float ActionDuration => Mathf.Max(0.1f, actionDuration);
    public float ActionRange => Mathf.Max(1f, actionRange);

    public bool Validate(out string error)
    {
        if (string.IsNullOrWhiteSpace(ItemId))
        {
            error = "The surface tool item ID is empty.";
            return false;
        }
        if (viewPrefab == null)
        {
            error = $"Surface tool '{ItemId}' has no view prefab.";
            return false;
        }
        string[] requiredNodes = { "PrimaryGrip", "AimPoint", "EffectOrigin" };
        for (int index = 0; index < requiredNodes.Length; index++)
        {
            if (FindDeep(viewPrefab.transform, requiredNodes[index]) != null)
                continue;
            error = $"Surface tool '{ItemId}' is missing '{requiredNodes[index]}'.";
            return false;
        }
        error = string.Empty;
        return true;
    }

    static Transform FindDeep(Transform root, string targetName)
    {
        if (root == null)
            return null;
        if (root.name == targetName)
            return root;
        for (int index = 0; index < root.childCount; index++)
        {
            Transform found = FindDeep(root.GetChild(index), targetName);
            if (found != null)
                return found;
        }
        return null;
    }
}
