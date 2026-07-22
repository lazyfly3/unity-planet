using UnityEngine;
using UnityEngine.EventSystems;

namespace SpacecraftEditor
{
    public sealed class OrbitCameraController : MonoBehaviour
    {
        private const float DefaultEditDistance = 9.5f;
        private const float DefaultEditYaw = 35f;
        private const float DefaultEditPitch = 22f;
        private const float DefaultFlightDistance = 11.5f;
        private const float DefaultFlightYaw = 15f;
        private const float DefaultFlightPitch = 16f;

        [SerializeField] private Camera controlledCamera;
        [SerializeField] private Transform target;
        [SerializeField] private RectTransform editorViewport;
        [SerializeField] private float editDistance = DefaultEditDistance;
        [SerializeField] private float editYaw = DefaultEditYaw;
        [SerializeField] private float editPitch = DefaultEditPitch;
        [SerializeField] private float flightDistance = DefaultFlightDistance;
        [SerializeField] private float flightYaw = DefaultFlightYaw;
        [SerializeField] private float flightPitch = DefaultFlightPitch;

        private readonly Vector3[] viewportCorners = new Vector3[4];
        private bool flightMode;
        private float framedEditDistance = DefaultEditDistance;
        private float framedFlightDistance = DefaultFlightDistance;

        public bool IsFlightMode => flightMode;
        public Camera ControlledCamera => controlledCamera;
        public RectTransform EditorViewport => editorViewport;
        public float EditDistance => editDistance;
        public float FlightDistance => flightDistance;

        public void Configure(Camera camera, Transform cameraTarget, RectTransform buildViewport = null)
        {
            controlledCamera = camera;
            target = cameraTarget;
            editorViewport = buildViewport;
            framedEditDistance = editDistance;
            framedFlightDistance = flightDistance;
            ApplyViewportRect();
            SnapToCurrentMode();
        }

        public void SetEditorViewport(RectTransform buildViewport)
        {
            editorViewport = buildViewport;
            ApplyViewportRect();
            if (!flightMode)
                SnapEditView();
        }

        public void FrameHull(Bounds localBounds, bool snapImmediately = true)
        {
            if (controlledCamera == null)
                return;

            ApplyViewportRect();
            var radius = Mathf.Max(0.5f, localBounds.extents.magnitude);
            var verticalHalfFov = controlledCamera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            var aspect = Mathf.Max(0.2f, controlledCamera.aspect);
            var horizontalHalfFov = Mathf.Atan(Mathf.Tan(verticalHalfFov) * aspect);
            var limitingHalfFov = Mathf.Max(10f * Mathf.Deg2Rad, Mathf.Min(verticalHalfFov, horizontalHalfFov));
            framedEditDistance = Mathf.Clamp(radius / Mathf.Sin(limitingHalfFov) * 1.12f, 5.5f, 18f);
            framedFlightDistance = Mathf.Clamp(framedEditDistance * 1.18f, 7f, 18f);
            editDistance = framedEditDistance;
            flightDistance = framedFlightDistance;

            if (snapImmediately)
                SnapToCurrentMode();
        }

        public void SetFlightMode(bool value)
        {
            flightMode = value;
            if (flightMode)
                ResetFlightView(false);
            ApplyViewportRect();
            SnapToCurrentMode();
        }

        public void ResetFlightView(bool snapImmediately = true)
        {
            flightDistance = framedFlightDistance;
            flightYaw = DefaultFlightYaw;
            flightPitch = DefaultFlightPitch;
            if (snapImmediately && flightMode)
                UpdateFlightView(true);
        }

        private void LateUpdate()
        {
            if (controlledCamera == null || target == null)
                return;

            ApplyViewportRect();
            if (flightMode)
            {
                if (Input.GetMouseButton(1))
                {
                    flightYaw += Input.GetAxis("Mouse X") * 3.8f;
                    flightPitch -= Input.GetAxis("Mouse Y") * 3.8f;
                    flightPitch = Mathf.Clamp(flightPitch, -25f, 70f);
                }

                var minimumFlightDistance = Mathf.Max(7f, framedFlightDistance * 0.62f);
                var maximumFlightDistance = Mathf.Max(18f, framedFlightDistance * 1.65f);
                flightDistance = Mathf.Clamp(flightDistance - Input.GetAxis("Mouse ScrollWheel") * 6f,
                    minimumFlightDistance, maximumFlightDistance);
                if (Input.GetKeyDown(KeyCode.C))
                    ResetFlightView(false);
                UpdateFlightView(false);
                return;
            }

            var overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (Input.GetMouseButton(1) && !overUi)
            {
                editYaw += Input.GetAxis("Mouse X") * 4.5f;
                editPitch -= Input.GetAxis("Mouse Y") * 4.5f;
                editPitch = Mathf.Clamp(editPitch, -75f, 75f);
            }
            if (!overUi)
            {
                var minimumEditDistance = Mathf.Max(5.5f, framedEditDistance * 0.58f);
                var maximumEditDistance = Mathf.Max(18f, framedEditDistance * 1.8f);
                editDistance = Mathf.Clamp(editDistance - Input.GetAxis("Mouse ScrollWheel") * 5f,
                    minimumEditDistance, maximumEditDistance);
            }
            SnapEditView();
        }

        private void SnapToCurrentMode()
        {
            if (controlledCamera == null || target == null)
                return;
            if (flightMode)
                UpdateFlightView(true);
            else
                SnapEditView();
        }

        private void SnapEditView()
        {
            var rotation = Quaternion.Euler(editPitch, editYaw, 0f);
            controlledCamera.transform.position = target.position + rotation * new Vector3(0f, 0f, -editDistance);
            controlledCamera.transform.rotation = Quaternion.LookRotation(target.position - controlledCamera.transform.position, Vector3.up);
        }

        private void UpdateFlightView(bool immediate)
        {
            // Flight view is world-stabilized: it follows translation, but the ship is free
            // to rotate inside the frame without dragging or rolling the camera with it.
            var pivot = target.position;
            var orbitRotation = Quaternion.Euler(flightPitch, flightYaw, 0f);
            var desiredPosition = pivot + orbitRotation * new Vector3(0f, 0f, -flightDistance);
            var desiredRotation = Quaternion.LookRotation(pivot - desiredPosition, Vector3.up);

            if (immediate)
            {
                controlledCamera.transform.SetPositionAndRotation(desiredPosition, desiredRotation);
                return;
            }

            controlledCamera.transform.position = Vector3.Lerp(
                controlledCamera.transform.position,
                desiredPosition,
                1f - Mathf.Exp(-5f * Time.deltaTime));
            controlledCamera.transform.rotation = Quaternion.Slerp(
                controlledCamera.transform.rotation,
                desiredRotation,
                1f - Mathf.Exp(-6f * Time.deltaTime));
        }

        private void ApplyViewportRect()
        {
            if (controlledCamera == null)
                return;
            if (flightMode || editorViewport == null || Screen.width <= 0 || Screen.height <= 0)
            {
                controlledCamera.rect = new Rect(0f, 0f, 1f, 1f);
                return;
            }

            editorViewport.GetWorldCorners(viewportCorners);
            var xMin = Mathf.Clamp01(viewportCorners[0].x / Screen.width);
            var yMin = Mathf.Clamp01(viewportCorners[0].y / Screen.height);
            var xMax = Mathf.Clamp01(viewportCorners[2].x / Screen.width);
            var yMax = Mathf.Clamp01(viewportCorners[2].y / Screen.height);
            controlledCamera.rect = new Rect(xMin, yMin, Mathf.Max(0.05f, xMax - xMin), Mathf.Max(0.05f, yMax - yMin));
        }
    }
}
