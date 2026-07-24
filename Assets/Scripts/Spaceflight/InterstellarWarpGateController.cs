using SpacecraftEditor;
using UnityEngine;

public struct WarpGateContext
{
    public GalaxyPlanetDefinition planet;
    public DoubleVector3 targetUniversePosition;
    public DoubleVector3 destinationUniversePosition;
    public Vector3 travelDirection;
    public Quaternion exitRotation;
    public SpacePlanetProxy targetProxy;
}

[DefaultExecutionOrder(650)]
[DisallowMultipleComponent]
public sealed class InterstellarWarpGateController : MonoBehaviour
{
    const float GateForwardDistance = 45f;
    const int PreviewWidth = 1024;
    const int PreviewHeight = 576;

    GameObject gateRoot;
    MeshRenderer surfaceRenderer;
    MeshRenderer ringRenderer;
    Mesh ringMesh;
    Material surfaceMaterial;
    Material ringMaterial;
    RenderTexture previewTexture;
    Camera portalCamera;

    WarpGateContext context;
    InterstellarFlightRuntime runtime;
    Rigidbody shipBody;
    Camera playerCamera;
    Vector3 cameraOffsetLocal;
    Vector2 gateSize;
    Vector3 previousShipPosition;
    float previousPlaneDistance;
    bool running;
    bool relocated;
    bool entranceFrozen;
    bool hasCrossingSample;

    public bool IsRunning => running;
    public bool HasRelocated => relocated;
    public Vector3 PreviewCameraPosition => portalCamera == null
        ? Vector3.zero
        : portalCamera.transform.position;
    public Vector2 GateSize => gateSize;
    public Vector3 EntrancePosition => gateRoot == null
        ? Vector3.zero
        : gateRoot.transform.position;
    public Vector3 EntranceForward => gateRoot == null
        ? Vector3.forward
        : gateRoot.transform.forward;

    public bool BeginWarp(WarpGateContext value)
    {
        InterstellarFlightRuntime valueRuntime =
            FindObjectOfType<InterstellarFlightRuntime>();
        InterstellarShipController ship =
            FindObjectOfType<InterstellarShipController>();
        Rigidbody valueShipBody = ship == null
            ? null
            : ship.GetComponent<Rigidbody>();
        return BeginWarp(
            value,
            valueRuntime,
            valueShipBody,
            Camera.main);
    }

    public bool BeginWarp(
        WarpGateContext value,
        InterstellarFlightRuntime valueRuntime,
        Rigidbody valueShipBody,
        Camera valuePlayerCamera)
    {
        if (value.planet == null
            || valueRuntime == null
            || valueShipBody == null
            || valuePlayerCamera == null)
        {
            return false;
        }

        EnsureRuntimeObjects();
        if (gateRoot == null || surfaceMaterial == null || portalCamera == null)
            return false;
        context = value;
        runtime = valueRuntime;
        shipBody = valueShipBody;
        playerCamera = valuePlayerCamera;
        cameraOffsetLocal = shipBody.transform.InverseTransformPoint(
            playerCamera.transform.position);
        gateSize = CalculateGateSize(shipBody.transform);
        relocated = false;
        entranceFrozen = false;
        hasCrossingSample = false;
        running = true;
        context.targetProxy?.SetDetail(PlanetProxyDetail.Near);

        UpdateEntrancePose();
        gateRoot.transform.localScale = Vector3.zero;
        gateRoot.SetActive(false);
        surfaceMaterial.SetTexture("_MainTex", previewTexture);
        surfaceMaterial.SetFloat("_UseTexture", 1f);
        surfaceMaterial.SetFloat("_RimOnly", 0f);
        ringMaterial.SetFloat("_UseTexture", 0f);
        ringMaterial.SetFloat("_RimOnly", 1f);
        UpdatePreviewPose();
        return true;
    }

    public void TrackEntrance()
    {
        if (!running || relocated || entranceFrozen)
            return;
        UpdateEntrancePose();
    }

    public void FreezeEntrance()
    {
        if (!running || relocated || gateRoot == null || shipBody == null)
            return;
        UpdateEntrancePose();
        entranceFrozen = true;
        ResetCrossingSample();
    }

    public Vector3 GetEntranceAimDirection(Vector3 fallback)
    {
        if (!running || relocated || gateRoot == null || shipBody == null)
            return fallback.sqrMagnitude > 0.001f
                ? fallback.normalized
                : Vector3.forward;
        Vector3 toCenter = gateRoot.transform.position - shipBody.position;
        return toCenter.sqrMagnitude > 0.001f
            ? toCenter.normalized
            : gateRoot.transform.forward;
    }

    public float EntranceDistance => gateRoot == null || shipBody == null
        ? 0f
        : Vector3.Distance(shipBody.position, gateRoot.transform.position);

    public void SetPhase(InterstellarWarpState phase, float progress)
    {
        if (!running || gateRoot == null)
            return;

        progress = Mathf.Clamp01(progress);
        if (phase == InterstellarWarpState.Aligning)
        {
            gateRoot.SetActive(false);
            SetMaterialProgress(0f, 0f);
            return;
        }

        gateRoot.SetActive(true);
        float open;
        if (phase == InterstellarWarpState.Spooling)
        {
            open = Mathf.SmoothStep(0f, 1f, progress);
            gateRoot.transform.localScale =
                new Vector3(gateSize.x * open, gateSize.y * open, 1f);
        }
        else if (phase == InterstellarWarpState.Transit)
        {
            open = 1f;
            gateRoot.transform.localScale =
                new Vector3(gateSize.x, gateSize.y, 1f);
        }
        else if (phase == InterstellarWarpState.Exiting)
        {
            open = 1f - Mathf.SmoothStep(0f, 1f, progress);
            gateRoot.transform.localScale =
                new Vector3(gateSize.x * open, gateSize.y * open, 1f);
        }
        else
        {
            EndWarp();
            return;
        }

        float pulse = phase == InterstellarWarpState.Transit
            ? 1f + Mathf.Sin(Time.unscaledTime * 16f) * 0.25f
            : 0.35f + open * 0.65f;
        SetMaterialProgress(open, pulse);
    }

    public bool HasShipCrossedEntrance()
    {
        if (!running
            || relocated
            || !entranceFrozen
            || gateRoot == null
            || shipBody == null)
            return false;

        Vector3 currentPosition = shipBody.position;
        float currentPlaneDistance = Vector3.Dot(
            currentPosition - gateRoot.transform.position,
            gateRoot.transform.forward);
        if (!hasCrossingSample)
        {
            previousShipPosition = currentPosition;
            previousPlaneDistance = currentPlaneDistance;
            hasCrossingSample = true;
            return false;
        }

        bool crossedPlane = previousPlaneDistance < 0f
            && currentPlaneDistance >= 0f;
        bool crossedAperture = false;
        if (crossedPlane)
        {
            float denominator = previousPlaneDistance - currentPlaneDistance;
            float crossingProgress = Mathf.Abs(denominator) <= 0.0001f
                ? 1f
                : Mathf.Clamp01(previousPlaneDistance / denominator);
            Vector3 crossingPoint = Vector3.Lerp(
                previousShipPosition,
                currentPosition,
                crossingProgress);
            Vector3 offset = crossingPoint - gateRoot.transform.position;
            var gateLocalOffset = new Vector2(
                Vector3.Dot(offset, gateRoot.transform.right),
                Vector3.Dot(offset, gateRoot.transform.up));
            crossedAperture = IsInsideEntranceEllipse(
                gateLocalOffset,
                gateSize,
                0.82f);
        }

        previousShipPosition = currentPosition;
        previousPlaneDistance = currentPlaneDistance;
        return crossedPlane && crossedAperture;
    }

    public static bool IsInsideEntranceEllipse(
        Vector2 localOffset,
        Vector2 size,
        float normalizedRadius = 1f)
    {
        float radius = Mathf.Clamp(normalizedRadius, 0.05f, 1f);
        float halfWidth = Mathf.Max(0.001f, size.x * 0.5f * radius);
        float halfHeight = Mathf.Max(0.001f, size.y * 0.5f * radius);
        float x = localOffset.x / halfWidth;
        float y = localOffset.y / halfHeight;
        return x * x + y * y <= 1f;
    }

    public void NotifyRelocated()
    {
        if (!running || shipBody == null || gateRoot == null)
            return;
        relocated = true;
        portalCamera.enabled = false;
        Vector3 direction = context.travelDirection.sqrMagnitude > 0.001f
            ? context.travelDirection.normalized
            : shipBody.transform.forward;
        Vector3 up = context.exitRotation * Vector3.up;
        gateRoot.transform.SetPositionAndRotation(
            shipBody.position - direction * 8f,
            Quaternion.LookRotation(direction, up));
        gateRoot.transform.localScale = new Vector3(gateSize.x, gateSize.y, 1f);
        surfaceMaterial.SetFloat("_UseTexture", 0f);
    }

    public void EndWarp()
    {
        running = false;
        relocated = false;
        entranceFrozen = false;
        hasCrossingSample = false;
        if (portalCamera != null)
            portalCamera.enabled = false;
        if (gateRoot != null)
            gateRoot.SetActive(false);
    }

    void LateUpdate()
    {
        if (!running || relocated || portalCamera == null)
            return;
        if (!entranceFrozen)
            UpdateEntrancePose(true);
        UpdatePreviewPose();
        portalCamera.Render();
    }

    void UpdatePreviewPose()
    {
        if (portalCamera == null || runtime == null || playerCamera == null)
            return;

        Vector3 destination =
            runtime.ToLocalPosition(context.destinationUniversePosition);
        Vector3 target = runtime.ToLocalPosition(context.targetUniversePosition);
        Vector3 position = destination + context.exitRotation * cameraOffsetLocal;
        Vector3 lookDirection = target - position;
        if (lookDirection.sqrMagnitude < 0.001f)
            lookDirection = context.travelDirection;
        portalCamera.transform.SetPositionAndRotation(
            position,
            Quaternion.LookRotation(
                lookDirection.normalized,
                context.exitRotation * Vector3.up));
        portalCamera.fieldOfView = playerCamera.fieldOfView;
        portalCamera.nearClipPlane = Mathf.Max(0.1f, playerCamera.nearClipPlane);
        portalCamera.farClipPlane = Mathf.Max(100000f, playerCamera.farClipPlane);
        portalCamera.aspect = PreviewWidth / (float)PreviewHeight;
    }

    void UpdateEntrancePose(bool useRenderedShipPose = false)
    {
        if (gateRoot == null || shipBody == null)
            return;
        Vector3 direction = context.travelDirection.sqrMagnitude > 0.001f
            ? context.travelDirection.normalized
            : shipBody.transform.forward;
        Vector3 up = Vector3.ProjectOnPlane(shipBody.transform.up, direction);
        if (up.sqrMagnitude < 0.001f)
            up = Vector3.ProjectOnPlane(Vector3.up, direction);
        if (up.sqrMagnitude < 0.001f)
            up = Vector3.right;
        Vector3 shipPosition = useRenderedShipPose
            ? shipBody.transform.position
            : shipBody.position;
        gateRoot.transform.SetPositionAndRotation(
            shipPosition + direction * GateForwardDistance,
            Quaternion.LookRotation(direction, up.normalized));
    }

    void ResetCrossingSample()
    {
        if (gateRoot == null || shipBody == null)
        {
            hasCrossingSample = false;
            return;
        }
        previousShipPosition = shipBody.position;
        previousPlaneDistance = Vector3.Dot(
            previousShipPosition - gateRoot.transform.position,
            gateRoot.transform.forward);
        hasCrossingSample = true;
    }

    void EnsureRuntimeObjects()
    {
        if (gateRoot != null)
            return;

        Shader shader = Shader.Find("VoxelPlanet/InterstellarWarpGate");
        if (shader == null)
        {
            Debug.LogError(
                "InterstellarWarpGateController: missing warp gate shader.",
                this);
            return;
        }

        previewTexture = new RenderTexture(
            PreviewWidth,
            PreviewHeight,
            24,
            RenderTextureFormat.ARGB32)
        {
            name = "InterstellarWarpGatePreview",
            antiAliasing = 2,
            useMipMap = false,
            autoGenerateMips = false
        };
        previewTexture.Create();

        surfaceMaterial = new Material(shader)
        {
            name = "RuntimeWarpGateSurface"
        };
        ringMaterial = new Material(shader)
        {
            name = "RuntimeWarpGateRing"
        };

        gateRoot = new GameObject("RuntimeInterstellarWarpGate");
        gateRoot.transform.SetParent(transform, false);

        GameObject surface = GameObject.CreatePrimitive(PrimitiveType.Quad);
        surface.name = "ExitView";
        surface.transform.SetParent(gateRoot.transform, false);
        Collider collider = surface.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);
        surfaceRenderer = surface.GetComponent<MeshRenderer>();
        surfaceRenderer.sharedMaterial = surfaceMaterial;
        surfaceRenderer.shadowCastingMode =
            UnityEngine.Rendering.ShadowCastingMode.Off;
        surfaceRenderer.receiveShadows = false;

        var ring = new GameObject("EnergyRim");
        ring.transform.SetParent(gateRoot.transform, false);
        ring.transform.localPosition = new Vector3(0f, 0f, -0.015f);
        MeshFilter ringFilter = ring.AddComponent<MeshFilter>();
        ringRenderer = ring.AddComponent<MeshRenderer>();
        ringMesh = BuildRingMesh(96, 0.78f);
        ringFilter.sharedMesh = ringMesh;
        ringRenderer.sharedMaterial = ringMaterial;
        ringRenderer.shadowCastingMode =
            UnityEngine.Rendering.ShadowCastingMode.Off;
        ringRenderer.receiveShadows = false;

        var cameraObject = new GameObject("WarpGatePortalCamera");
        cameraObject.transform.SetParent(transform, false);
        portalCamera = cameraObject.AddComponent<Camera>();
        portalCamera.enabled = false;
        portalCamera.clearFlags = CameraClearFlags.SolidColor;
        portalCamera.backgroundColor = Color.black;
        portalCamera.targetTexture = previewTexture;
        portalCamera.allowHDR = true;
        portalCamera.allowMSAA = true;
        portalCamera.stereoTargetEye = StereoTargetEyeMask.None;
        portalCamera.cullingMask &= ~(1 << LayerMask.NameToLayer("UI"));
        gateRoot.SetActive(false);
    }

    void SetMaterialProgress(float open, float pulse)
    {
        if (surfaceMaterial != null)
        {
            surfaceMaterial.SetFloat("_Open", open);
            surfaceMaterial.SetFloat("_Pulse", pulse);
        }
        if (ringMaterial != null)
        {
            ringMaterial.SetFloat("_Open", open);
            ringMaterial.SetFloat("_Pulse", pulse);
        }
    }

    static Vector2 CalculateGateSize(Transform ship)
    {
        Renderer[] renderers = ship.GetComponentsInChildren<Renderer>(false);
        Bounds bounds = new Bounds(ship.position, new Vector3(8f, 5f, 12f));
        bool found = false;
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null
                || !renderer.enabled
                || !renderer.gameObject.activeInHierarchy
                || renderer is ParticleSystemRenderer
                || renderer is TrailRenderer
                || renderer is LineRenderer)
                continue;
            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }
        float height = Mathf.Max(18f, bounds.size.y * 2.8f);
        float width = Mathf.Max(28.8f, bounds.size.x * 2.5f, height * 1.6f);
        return new Vector2(
            Mathf.Min(width, 80f),
            Mathf.Min(height, 50f));
    }

    static Mesh BuildRingMesh(int segments, float innerRadius)
    {
        segments = Mathf.Max(16, segments);
        var vertices = new Vector3[(segments + 1) * 2];
        var uvs = new Vector2[vertices.Length];
        var triangles = new int[segments * 6];
        for (int index = 0; index <= segments; index++)
        {
            float angle = index / (float)segments * Mathf.PI * 2f;
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            int vertex = index * 2;
            vertices[vertex] = direction * 0.5f;
            vertices[vertex + 1] = direction * (0.5f * innerRadius);
            uvs[vertex] = Vector2.one * 0.5f + direction * 0.5f;
            uvs[vertex + 1] = Vector2.one * 0.5f
                + direction * (0.5f * innerRadius);
            if (index == segments)
                continue;
            int triangle = index * 6;
            triangles[triangle] = vertex;
            triangles[triangle + 1] = vertex + 2;
            triangles[triangle + 2] = vertex + 1;
            triangles[triangle + 3] = vertex + 1;
            triangles[triangle + 4] = vertex + 2;
            triangles[triangle + 5] = vertex + 3;
        }
        var mesh = new Mesh { name = "InterstellarWarpGateRing" };
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }

    void OnDestroy()
    {
        if (previewTexture != null)
        {
            previewTexture.Release();
            Destroy(previewTexture);
        }
        if (surfaceMaterial != null)
            Destroy(surfaceMaterial);
        if (ringMaterial != null)
            Destroy(ringMaterial);
        if (ringMesh != null)
            Destroy(ringMesh);
    }
}
