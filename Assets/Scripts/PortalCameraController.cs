using UnityEngine;

/// <summary>
/// 视觉传送门相机：将主相机视角镜像到出口门，渲染到 RenderTexture。
///
/// 挂载方式：
/// 1. 在传送门子物体 PortalCamera 上挂本脚本（同物体需有 Camera 组件）
/// 2. Portal：拖入同一传送门根物体上的 Portal 脚本
/// 3. Player Camera：拖入主相机（留空则自动使用 Camera.main）
/// 4. Render Texture：拖入 Assets/RenderTextures/PortalRT（运行时每个门实例会自动复制独立 RT）
/// 5. 两扇门各拖一个预制体，互相设置 Linked Portal 即可
/// </summary>
[RequireComponent(typeof(Camera))]
public class PortalCameraController : MonoBehaviour
{
    [Header("引用")]
    [SerializeField] Portal portal;
    [SerializeField] Camera playerCamera;
    [SerializeField] RenderTexture renderTexture;

    [Header("渲染设置")]
    [SerializeField] int maxRecursion = 1;
    [SerializeField] bool useObliqueClipping = true;

    Camera portalCamera;
    RenderTexture instanceRenderTexture;
    static int currentRecursionDepth;

    void Awake()
    {
        portalCamera = GetComponent<Camera>();

        if (playerCamera == null)
            playerCamera = Camera.main;

        if (portalCamera != null)
        {
            portalCamera.enabled = false;
            portalCamera.stereoTargetEye = StereoTargetEyeMask.None;
        }

        CreateInstanceRenderTexture();
        BindSurfaceMaterial();
    }

    void CreateInstanceRenderTexture()
    {
        if (renderTexture == null || portalCamera == null)
            return;

        instanceRenderTexture = new RenderTexture(renderTexture.width, renderTexture.height, renderTexture.depth, renderTexture.format)
        {
            name = renderTexture.name + " (Instance)"
        };
        instanceRenderTexture.Create();
        portalCamera.targetTexture = instanceRenderTexture;
    }

    void BindSurfaceMaterial()
    {
        if (portal == null || instanceRenderTexture == null)
            return;

        Transform surface = portal.transform.Find("PortalSurface");
        if (surface == null)
            return;

        Renderer surfaceRenderer = surface.GetComponent<Renderer>();
        if (surfaceRenderer == null)
            return;

        Material matInstance = new Material(surfaceRenderer.sharedMaterial);
        matInstance.mainTexture = instanceRenderTexture;
        surfaceRenderer.material = matInstance;
    }

    void OnDestroy()
    {
        if (instanceRenderTexture != null)
        {
            instanceRenderTexture.Release();
            Destroy(instanceRenderTexture);
        }
    }

    void LateUpdate()
    {
        if (portal == null || portal.LinkedPortal == null || playerCamera == null || portalCamera == null)
            return;

        if (currentRecursionDepth >= maxRecursion)
        {
            portalCamera.enabled = false;
            return;
        }

        currentRecursionDepth++;
        portalCamera.enabled = true;

        Transform entry = portal.Surface;
        Transform exit = portal.LinkedPortal.Surface;

        SyncCameraTransform(entry, exit);
        SyncCameraSettings();
        ApplyObliqueClipping(entry);

        currentRecursionDepth--;
    }

    void SyncCameraTransform(Transform entry, Transform exit)
    {
        Vector3 localPosition = entry.InverseTransformPoint(playerCamera.transform.position);
        Quaternion localRotation = Quaternion.Inverse(entry.rotation) * playerCamera.transform.rotation;

        portalCamera.transform.SetPositionAndRotation(
            exit.TransformPoint(localPosition),
            exit.rotation * localRotation
        );
    }

    void SyncCameraSettings()
    {
        portalCamera.fieldOfView = playerCamera.fieldOfView;
        portalCamera.nearClipPlane = playerCamera.nearClipPlane;
        portalCamera.farClipPlane = playerCamera.farClipPlane;

        if (instanceRenderTexture != null)
            portalCamera.aspect = (float)instanceRenderTexture.width / instanceRenderTexture.height;
        else
            portalCamera.aspect = playerCamera.aspect;

        portalCamera.rect = new Rect(0f, 0f, 1f, 1f);
        portalCamera.ResetProjectionMatrix();
    }

    void ApplyObliqueClipping(Transform entry)
    {
        if (!useObliqueClipping)
            return;

        Vector3 portalNormal = -entry.forward;
        Vector3 portalPosition = entry.position + entry.forward * 0.01f;
        Plane clipPlane = new Plane(portalNormal, portalPosition);

        Vector4 clipPlaneCameraSpace = CameraSpacePlane(portalCamera, clipPlane);
        portalCamera.projectionMatrix = portalCamera.CalculateObliqueMatrix(clipPlaneCameraSpace);
    }

    static Vector4 CameraSpacePlane(Camera cam, Plane plane)
    {
        Vector3 normal = plane.normal;
        Vector3 point = normal * -plane.distance;

        Matrix4x4 viewMatrix = cam.worldToCameraMatrix;
        Vector3 cNormal = viewMatrix.MultiplyVector(normal).normalized;
        Vector3 cPoint = viewMatrix.MultiplyPoint(point);
        float distance = -Vector3.Dot(cPoint, cNormal);

        return new Vector4(cNormal.x, cNormal.y, cNormal.z, distance);
    }
}
