using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlantLabOrbitCamera : MonoBehaviour
{
    [SerializeField] Vector3 target = new Vector3(0f, 25f, 0f);
    [SerializeField] float distance = 28f;
    [SerializeField] float yaw;
    [SerializeField] float pitch = 11f;
    [SerializeField] float rotateSpeed = 0.22f;
    [SerializeField] float zoomSpeed = 4f;
    [SerializeField] float minimumDistance = 8f;
    [SerializeField] float maximumDistance = 70f;
    [SerializeField] float panelWidth = 380f;

    public void Configure(Vector3 valueTarget, float valueDistance)
    {
        target = valueTarget;
        distance = Mathf.Clamp(valueDistance, minimumDistance, maximumDistance);
        ApplyTransform();
    }

    void LateUpdate()
    {
        bool pointerOutsidePanel = Input.mousePosition.x > panelWidth;
        if (pointerOutsidePanel && (Input.GetMouseButton(0) || Input.GetMouseButton(1)))
        {
            yaw += Input.GetAxis("Mouse X") * rotateSpeed * 10f;
            pitch -= Input.GetAxis("Mouse Y") * rotateSpeed * 10f;
            pitch = Mathf.Clamp(pitch, -12f, 72f);
        }
        if (pointerOutsidePanel)
        {
            distance -= Input.mouseScrollDelta.y * zoomSpeed;
            distance = Mathf.Clamp(distance, minimumDistance, maximumDistance);
        }
        ApplyTransform();
    }

    void ApplyTransform()
    {
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        transform.position = target + rotation * (Vector3.back * distance);
        transform.rotation = Quaternion.LookRotation(target - transform.position, Vector3.up);
    }
}
