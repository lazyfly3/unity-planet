using UnityEngine;

public class VoxelDigTool : MonoBehaviour
{
    [SerializeField] VoxelWorld voxelWorld;
    [SerializeField] Camera digCamera;
    [SerializeField] float reach = 6f;
    [SerializeField] float digCooldown = 0.1f;

    float lastDigTime = -999f;

    void Awake()
    {
        if (digCamera == null)
            digCamera = Camera.main;
    }

    void Update()
    {
        if (InventoryUI.BlocksGameplayInput)
            return;

        if (voxelWorld == null || digCamera == null)
            return;

        if (Input.GetMouseButton(0)
            && SurfaceToolInputRouter.CanWorldInteractionUsePrimary)
            TryDig();
    }

    void TryDig()
    {
        if (Time.time - lastDigTime < digCooldown)
            return;

        Ray ray = new Ray(digCamera.transform.position, digCamera.transform.forward);
        if (!VoxelRaycastUtility.TryGetTargetVoxel(ray, reach, voxelWorld.transform, out Vector3Int voxelCoord, out _))
            return;

        if (voxelWorld.DigVoxel(voxelCoord.x, voxelCoord.y, voxelCoord.z))
            lastDigTime = Time.time;
    }
}
