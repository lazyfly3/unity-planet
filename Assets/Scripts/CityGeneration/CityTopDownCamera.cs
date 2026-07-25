using UnityEngine;

namespace CityGeneration
{
    [RequireComponent(typeof(Camera))]
    public sealed class CityTopDownCamera : MonoBehaviour
    {
        [SerializeField, Min(1f)] float panSpeed = 70f;
        [SerializeField, Min(1f)] float zoomSpeed = 24f;
        [SerializeField, Min(5f)] float minimumZoom = 20f;
        [SerializeField, Min(10f)] float maximumZoom = 190f;
        [SerializeField, Min(10f)] float movementBounds = 190f;

        Camera controlledCamera;

        void Awake()
        {
            controlledCamera = GetComponent<Camera>();
            controlledCamera.orthographic = true;
            transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }

        void Update()
        {
            float horizontal = Input.GetAxisRaw("Horizontal");
            float vertical = Input.GetAxisRaw("Vertical");
            Vector3 position = transform.position;
            position.x += horizontal * panSpeed * Time.unscaledDeltaTime;
            position.z += vertical * panSpeed * Time.unscaledDeltaTime;
            position.x = Mathf.Clamp(position.x, -movementBounds, movementBounds);
            position.z = Mathf.Clamp(position.z, -movementBounds, movementBounds);
            transform.position = position;

            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.001f)
            {
                controlledCamera.orthographicSize = Mathf.Clamp(
                    controlledCamera.orthographicSize - scroll * zoomSpeed,
                    minimumZoom,
                    maximumZoom);
            }
        }
    }
}
