using UnityEngine;

public class VoxelQuadSphereDigTool : MonoBehaviour
{
    [SerializeField] VoxelQuadSphereWorld quadSphereWorld;
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
        if (quadSphereWorld == null || digCamera == null || PauseMenuController.IsPaused)
            return;

        if (Input.GetMouseButton(0))
            TryDig();
    }

    void TryDig()
    {
        if (Time.time - lastDigTime < digCooldown)
            return;

        Ray ray = new Ray(digCamera.transform.position, digCamera.transform.forward);
        if (!Physics.Raycast(ray, out RaycastHit hit, reach))
            return;

        Vector3 localPoint = hit.point + hit.normal * -0.01f;
        localPoint = quadSphereWorld.transform.InverseTransformPoint(localPoint);

        if (quadSphereWorld.TryDigAtLocalPoint(localPoint))
            lastDigTime = Time.time;
    }
}
