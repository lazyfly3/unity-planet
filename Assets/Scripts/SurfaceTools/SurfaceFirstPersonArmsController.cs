using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
[RequireComponent(typeof(VoxelPlanetPlayerController))]
public sealed class SurfaceFirstPersonArmsController : MonoBehaviour
{
    const string ArmsResourcePath = "SurfaceTools/SurfaceFirstPersonArms";
    const string ViewLayerName = "FirstPersonView";
    const float ViewFieldOfView = 62f;

    sealed class ArmChain
    {
        public Transform upper;
        public Transform upperTwist;
        public Transform forearm;
        public Transform forearmTwist;
        public Transform hand;
        public Transform hint;
        public Transform emptyTarget;
        public readonly List<Transform> fingers = new List<Transform>();
    }

    readonly Dictionary<Transform, Quaternion> restRotations =
        new Dictionary<Transform, Quaternion>();
    readonly List<Renderer> viewRenderers = new List<Renderer>();
    readonly MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
    VoxelPlanetPlayerController player;
    Rigidbody playerBody;
    Camera worldCamera;
    Camera overlayCamera;
    Transform motionRoot;
    GameObject armsInstance;
    GameObject toolInstance;
    ArmChain leftArm;
    ArmChain rightArm;
    Transform primaryGrip;
    Transform supportGrip;
    Renderer scannerEmitter;
    SurfaceToolDefinition activeTool;
    WaterCameraEffects waterEffects;
    Vector3 previousCameraEuler;
    Vector3 lookSpring;
    Vector3 lookSpringVelocity;
    Vector3 actionSpring;
    Vector3 actionSpringVelocity;
    float actionRemaining;
    float actionDuration;
    float equipBlend;
    bool inputBlocked;
    bool initialized;

    public SurfaceToolDefinition ActiveTool => activeTool;
    public bool IsActionPlaying => actionRemaining > 0f;
    public Camera OverlayCamera => overlayCamera;
    public GameObject ArmsInstance => armsInstance;

    void Awake()
    {
        player = GetComponent<VoxelPlanetPlayerController>();
        playerBody = GetComponent<Rigidbody>();
        worldCamera = Camera.main;
        Initialize();
    }

    void OnEnable()
    {
        if (!initialized)
            Initialize();
        SetVisible(true);
    }

    void Update()
    {
        if (!initialized)
            Initialize();
        if (!initialized)
            return;

        float deltaTime = Mathf.Max(0.0001f, Time.deltaTime);
        if (actionRemaining > 0f)
            actionRemaining = Mathf.Max(0f, actionRemaining - deltaTime);
        float targetEquip = activeTool != null ? 1f : 0f;
        equipBlend = Mathf.MoveTowards(equipBlend, targetEquip, deltaTime * 5.5f);
        UpdateMotionSprings(deltaTime);
        UpdateScannerEmitter();
        UpdateUnderwaterTint();
    }

    void LateUpdate()
    {
        if (!initialized || armsInstance == null)
            return;
        RestoreRigPose();
        ApplyViewMotion();
        ApplyFingerCurl(leftArm, activeTool != null ? activeTool.SupportGripCurl : 0.18f);
        ApplyFingerCurl(rightArm, activeTool != null ? activeTool.PrimaryGripCurl : 0.16f);

        Transform rightTarget = primaryGrip != null
            ? primaryGrip
            : rightArm.emptyTarget;
        Transform leftTarget = supportGrip != null
            ? supportGrip
            : leftArm.emptyTarget;
        SolveArm(rightArm, rightTarget);
        SolveArm(leftArm, leftTarget);
        SyncOverlayCamera();
    }

    public void Configure(
        VoxelPlanetPlayerController configuredPlayer,
        Camera configuredCamera)
    {
        if (configuredPlayer != null)
            player = configuredPlayer;
        if (configuredCamera != null)
            worldCamera = configuredCamera;
        Initialize();
    }

    public void EquipTool(SurfaceToolDefinition definition)
    {
        if (!initialized)
            Initialize();
        if (activeTool == definition && toolInstance != null)
            return;

        DestroyToolInstance();
        activeTool = definition;
        actionRemaining = 0f;
        if (definition == null || definition.ViewPrefab == null || motionRoot == null)
            return;

        toolInstance = Instantiate(definition.ViewPrefab, motionRoot);
        toolInstance.name = definition.ViewPrefab.name + "_View";
        toolInstance.transform.localPosition = definition.ViewPosition;
        toolInstance.transform.localRotation = definition.ViewRotation;
        toolInstance.transform.localScale = Vector3.one;
        int layer = LayerMask.NameToLayer(ViewLayerName);
        SetLayerRecursively(toolInstance.transform, layer);
        primaryGrip = FindDeep(toolInstance.transform, "PrimaryGrip");
        supportGrip = FindDeep(toolInstance.transform, "SupportGrip");
        scannerEmitter = FindDeep(toolInstance.transform, "ScannerEmitterRing")
            ?.GetComponent<Renderer>();
        CollectViewRenderers();
        equipBlend = 0f;
    }

    public void BeginToolAction(float duration)
    {
        actionDuration = Mathf.Max(0.1f, duration);
        actionRemaining = actionDuration;
        actionSpringVelocity += new Vector3(0f, 0.32f, -0.45f);
    }

    public void SetInputBlocked(bool blocked)
    {
        inputBlocked = blocked;
        if (blocked)
        {
            actionRemaining = 0f;
            actionSpring = Vector3.zero;
            actionSpringVelocity = Vector3.zero;
        }
    }

    public void SetVisible(bool visible)
    {
        if (overlayCamera != null)
            overlayCamera.enabled = visible;
        if (motionRoot != null)
            motionRoot.gameObject.SetActive(visible);
    }

    void Initialize()
    {
        if (initialized)
            return;
        if (worldCamera == null)
            worldCamera = Camera.main;
        if (worldCamera == null)
            return;

        int layer = LayerMask.NameToLayer(ViewLayerName);
        if (layer < 0)
        {
            Debug.LogError(
                "SurfaceFirstPersonArmsController: FirstPersonView layer is missing.",
                this);
            return;
        }

        BuildOverlayCamera(layer);
        GameObject prefab = Resources.Load<GameObject>(ArmsResourcePath);
        if (prefab == null)
        {
            Debug.LogError(
                "SurfaceFirstPersonArmsController: generated arms prefab is missing.",
                this);
            return;
        }

        GameObject motionObject = new GameObject("SurfaceFirstPersonMotionRoot");
        motionObject.transform.SetParent(worldCamera.transform, false);
        motionRoot = motionObject.transform;
        SetLayerRecursively(motionRoot, layer);
        armsInstance = Instantiate(prefab, motionRoot);
        armsInstance.name = "SurfaceFirstPersonArms_View";
        armsInstance.transform.localPosition = Vector3.zero;
        armsInstance.transform.localRotation = Quaternion.identity;
        armsInstance.transform.localScale = Vector3.one;
        SetLayerRecursively(armsInstance.transform, layer);
        ConfigureRenderers(armsInstance);
        BindRig();
        waterEffects = worldCamera.GetComponent<WaterCameraEffects>();
        previousCameraEuler = worldCamera.transform.localEulerAngles;
        worldCamera.cullingMask &= ~(1 << layer);
        initialized = leftArm != null && rightArm != null;
        SetVisible(initialized);
    }

    void BuildOverlayCamera(int layer)
    {
        Transform existing = worldCamera.transform.Find("FirstPersonOverlayCamera");
        if (existing != null)
            overlayCamera = existing.GetComponent<Camera>();
        if (overlayCamera == null)
        {
            GameObject cameraObject = new GameObject("FirstPersonOverlayCamera");
            cameraObject.transform.SetParent(worldCamera.transform, false);
            overlayCamera = cameraObject.AddComponent<Camera>();
        }

        overlayCamera.CopyFrom(worldCamera);
        overlayCamera.transform.localPosition = Vector3.zero;
        overlayCamera.transform.localRotation = Quaternion.identity;
        overlayCamera.clearFlags = CameraClearFlags.Depth;
        overlayCamera.depth = worldCamera.depth + 2f;
        overlayCamera.cullingMask = 1 << layer;
        overlayCamera.nearClipPlane = 0.01f;
        overlayCamera.farClipPlane = 4f;
        overlayCamera.fieldOfView = ViewFieldOfView;
        overlayCamera.useOcclusionCulling = false;
        AudioListener listener = overlayCamera.GetComponent<AudioListener>();
        if (listener != null)
            Destroy(listener);
    }

    void BindRig()
    {
        leftArm = BuildArmChain("L");
        rightArm = BuildArmChain("R");
        if (leftArm == null || rightArm == null)
        {
            Debug.LogError(
                "SurfaceFirstPersonArmsController: generated arm bones are incomplete.",
                this);
            return;
        }

        CaptureRestRotations(leftArm);
        CaptureRestRotations(rightArm);
        leftArm.emptyTarget = CreateTarget("LeftEmptyHandTarget", leftArm.hand);
        rightArm.emptyTarget = CreateTarget("RightEmptyHandTarget", rightArm.hand);
        leftArm.hint = CreateHint("LeftElbowHint", leftArm, -1f);
        rightArm.hint = CreateHint("RightElbowHint", rightArm, 1f);
        CollectViewRenderers();
    }

    ArmChain BuildArmChain(string side)
    {
        var value = new ArmChain
        {
            upper = FindDeep(armsInstance.transform, side + "_UpperArm"),
            upperTwist = FindDeep(armsInstance.transform, side + "_UpperArmTwist"),
            forearm = FindDeep(armsInstance.transform, side + "_Forearm"),
            forearmTwist = FindDeep(armsInstance.transform, side + "_ForearmTwist"),
            hand = FindDeep(armsInstance.transform, side + "_Hand")
        };
        if (value.upper == null || value.forearm == null || value.hand == null)
            return null;
        string[] fingers = { "Thumb", "Index", "Middle", "Ring", "Little" };
        for (int finger = 0; finger < fingers.Length; finger++)
        for (int segment = 1; segment <= 3; segment++)
        {
            Transform bone = FindDeep(
                armsInstance.transform,
                $"{side}_{fingers[finger]}_{segment}");
            if (bone != null)
                value.fingers.Add(bone);
        }
        return value;
    }

    Transform CreateTarget(string targetName, Transform source)
    {
        GameObject value = new GameObject(targetName);
        value.transform.SetParent(motionRoot, false);
        value.transform.SetPositionAndRotation(source.position, source.rotation);
        return value.transform;
    }

    Transform CreateHint(string hintName, ArmChain chain, float side)
    {
        GameObject value = new GameObject(hintName);
        value.transform.SetParent(motionRoot, false);
        Vector3 elbow = chain.forearm.position;
        value.transform.position = elbow
            + motionRoot.right * side * 0.18f
            + motionRoot.up * -0.08f
            + motionRoot.forward * -0.05f;
        return value.transform;
    }

    void CaptureRestRotations(ArmChain chain)
    {
        Capture(chain.upper);
        Capture(chain.upperTwist);
        Capture(chain.forearm);
        Capture(chain.forearmTwist);
        Capture(chain.hand);
        for (int index = 0; index < chain.fingers.Count; index++)
            Capture(chain.fingers[index]);
    }

    void Capture(Transform value)
    {
        if (value != null && !restRotations.ContainsKey(value))
            restRotations.Add(value, value.localRotation);
    }

    void RestoreRigPose()
    {
        foreach (KeyValuePair<Transform, Quaternion> pair in restRotations)
            if (pair.Key != null)
                pair.Key.localRotation = pair.Value;
    }

    void ApplyFingerCurl(ArmChain chain, float curl)
    {
        if (chain == null)
            return;
        float clamped = Mathf.Clamp01(curl);
        for (int index = 0; index < chain.fingers.Count; index++)
        {
            Transform finger = chain.fingers[index];
            if (finger == null || !restRotations.TryGetValue(finger, out Quaternion rest))
                continue;
            int segment = index % 3;
            float angle = clamped * (segment == 0 ? 38f : segment == 1 ? 52f : 42f);
            if (finger.name.IndexOf("Thumb", StringComparison.Ordinal) >= 0)
                angle *= 0.72f;
            finger.localRotation = rest * Quaternion.AngleAxis(angle, Vector3.right);
        }
    }

    static void SolveArm(ArmChain chain, Transform target)
    {
        if (chain == null || target == null)
            return;
        Vector3 rootPosition = chain.upper.position;
        Vector3 midPosition = chain.forearm.position;
        Vector3 tipPosition = chain.hand.position;
        float upperLength = Mathf.Max(0.001f, Vector3.Distance(rootPosition, midPosition));
        float lowerLength = Mathf.Max(0.001f, Vector3.Distance(midPosition, tipPosition));
        Vector3 targetOffset = target.position - rootPosition;
        float distance = Mathf.Clamp(
            targetOffset.magnitude,
            Mathf.Abs(upperLength - lowerLength) + 0.001f,
            upperLength + lowerLength - 0.001f);
        Vector3 direction = targetOffset.sqrMagnitude > 0.000001f
            ? targetOffset.normalized
            : chain.upper.forward;
        Vector3 hintDirection = chain.hint != null
            ? chain.hint.position - rootPosition
            : Vector3.up;
        Vector3 bendNormal = Vector3.Cross(direction, hintDirection);
        if (bendNormal.sqrMagnitude < 0.000001f)
            bendNormal = Vector3.Cross(direction, Vector3.up);
        if (bendNormal.sqrMagnitude < 0.000001f)
            bendNormal = Vector3.Cross(direction, Vector3.right);
        bendNormal.Normalize();
        Vector3 bendDirection = Vector3.Cross(bendNormal, direction).normalized;

        float along = (
            upperLength * upperLength
            - lowerLength * lowerLength
            + distance * distance) / (2f * distance);
        float height = Mathf.Sqrt(Mathf.Max(
            0f,
            upperLength * upperLength - along * along));
        Vector3 desiredMid = rootPosition + direction * along + bendDirection * height;
        chain.upper.rotation =
            Quaternion.FromToRotation(midPosition - rootPosition, desiredMid - rootPosition)
            * chain.upper.rotation;
        chain.forearm.rotation =
            Quaternion.FromToRotation(
                chain.hand.position - chain.forearm.position,
                target.position - chain.forearm.position)
            * chain.forearm.rotation;
        chain.hand.rotation = target.rotation;
    }

    void UpdateMotionSprings(float deltaTime)
    {
        Vector3 currentEuler = worldCamera.transform.localEulerAngles;
        Vector3 deltaEuler = new Vector3(
            Mathf.DeltaAngle(previousCameraEuler.x, currentEuler.x),
            Mathf.DeltaAngle(previousCameraEuler.y, currentEuler.y),
            Mathf.DeltaAngle(previousCameraEuler.z, currentEuler.z));
        previousCameraEuler = currentEuler;
        Vector3 lookTarget = new Vector3(
            Mathf.Clamp(-deltaEuler.y * 0.0022f, -0.025f, 0.025f),
            Mathf.Clamp(deltaEuler.x * 0.0018f, -0.018f, 0.018f),
            0f);
        lookSpring = Vector3.SmoothDamp(
            lookSpring,
            lookTarget,
            ref lookSpringVelocity,
            0.075f,
            1f,
            deltaTime);
        actionSpring = Vector3.SmoothDamp(
            actionSpring,
            Vector3.zero,
            ref actionSpringVelocity,
            0.11f,
            3f,
            deltaTime);
    }

    void ApplyViewMotion()
    {
        float time = Time.time;
        Vector3 velocity = playerBody != null ? playerBody.velocity : Vector3.zero;
        Vector3 up = transform.up;
        float planarSpeed = Vector3.ProjectOnPlane(velocity, up).magnitude;
        float walk = Mathf.Clamp01(planarSpeed / 5f);
        float bobPhase = time * Mathf.Lerp(2f, 9f, walk);
        Vector3 bob = new Vector3(
            Mathf.Sin(bobPhase) * 0.010f,
            Mathf.Abs(Mathf.Cos(bobPhase)) * -0.012f,
            0f) * walk;
        Vector3 breathing = new Vector3(
            0f,
            Mathf.Sin(time * 1.35f) * 0.004f,
            Mathf.Cos(time * 1.1f) * 0.003f);
        float radialVelocity = Vector3.Dot(velocity, up);
        Vector3 jumpOffset = new Vector3(
            0f,
            Mathf.Clamp(-radialVelocity * 0.004f, -0.025f, 0.025f),
            0f);
        Vector3 swimOffset = player != null && player.IsSwimming
            ? new Vector3(
                Mathf.Sin(time * 2.1f) * 0.018f,
                Mathf.Cos(time * 1.7f) * 0.010f,
                -0.018f)
            : Vector3.zero;
        float actionProgress = actionDuration > 0f
            ? 1f - Mathf.Clamp01(actionRemaining / actionDuration)
            : 0f;
        float actionLift = IsActionPlaying
            ? Mathf.Sin(actionProgress * Mathf.PI) * 0.045f
            : 0f;
        motionRoot.localPosition =
            bob + breathing + lookSpring + actionSpring + jumpOffset + swimOffset
            + new Vector3(0f, actionLift, 0f);
        motionRoot.localRotation = Quaternion.Euler(
            -bob.y * 120f - actionLift * 18f,
            bob.x * 150f,
            -bob.x * 210f);
        if (toolInstance != null)
        {
            toolInstance.transform.localPosition = activeTool.ViewPosition
                + Vector3.down * (1f - equipBlend) * 0.22f;
            toolInstance.transform.localRotation = activeTool.ViewRotation
                * Quaternion.Euler((1f - equipBlend) * 28f, 0f, 0f);
        }
    }

    void UpdateScannerEmitter()
    {
        if (scannerEmitter == null)
            return;
        SurfaceBiotaScannerTool scanner = GetComponent<SurfaceBiotaScannerTool>();
        float progress = scanner != null ? scanner.ScanProgress : 0f;
        float pulse = scanner != null && scanner.IsScanActive
            ? 1.2f + Mathf.Sin(progress * Mathf.PI * 12f) * 0.65f
            : 0.55f;
        scannerEmitter.GetPropertyBlock(propertyBlock);
        Color emission = new Color(0f, 0.75f, 1f) * pulse;
        propertyBlock.SetColor("_EmissionColor", emission);
        propertyBlock.SetColor("_Color", new Color(0.02f, 0.55f, 0.72f, 1f));
        scannerEmitter.SetPropertyBlock(propertyBlock);
    }

    void UpdateUnderwaterTint()
    {
        if (waterEffects == null && worldCamera != null)
            waterEffects = worldCamera.GetComponent<WaterCameraEffects>();
        float blend = waterEffects != null ? waterEffects.Blend : 0f;
        Color tint = Color.Lerp(
            Color.white,
            new Color(0.25f, 0.72f, 0.82f, 1f),
            blend * 0.72f);
        for (int index = 0; index < viewRenderers.Count; index++)
        {
            Renderer renderer = viewRenderers[index];
            if (renderer == null || renderer == scannerEmitter)
                continue;
            renderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor("_Color", tint);
            renderer.SetPropertyBlock(propertyBlock);
        }
    }

    void SyncOverlayCamera()
    {
        if (overlayCamera == null || worldCamera == null)
            return;
        overlayCamera.aspect = worldCamera.aspect;
        overlayCamera.rect = worldCamera.rect;
        overlayCamera.targetTexture = worldCamera.targetTexture;
        overlayCamera.depth = worldCamera.depth + 2f;
        overlayCamera.fieldOfView = ViewFieldOfView;
    }

    void ConfigureRenderers(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int index = 0; index < renderers.Length; index++)
        {
            Renderer renderer = renderers[index];
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }

    void CollectViewRenderers()
    {
        viewRenderers.Clear();
        if (armsInstance != null)
            viewRenderers.AddRange(armsInstance.GetComponentsInChildren<Renderer>(true));
        if (toolInstance != null)
            viewRenderers.AddRange(toolInstance.GetComponentsInChildren<Renderer>(true));
    }

    void DestroyToolInstance()
    {
        primaryGrip = null;
        supportGrip = null;
        scannerEmitter = null;
        if (toolInstance != null)
            Destroy(toolInstance);
        toolInstance = null;
        activeTool = null;
        CollectViewRenderers();
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

    static void SetLayerRecursively(Transform root, int layer)
    {
        if (root == null || layer < 0)
            return;
        root.gameObject.layer = layer;
        for (int index = 0; index < root.childCount; index++)
            SetLayerRecursively(root.GetChild(index), layer);
    }

    void OnDisable()
    {
        SetVisible(false);
    }

    void OnDestroy()
    {
        if (worldCamera != null)
        {
            int layer = LayerMask.NameToLayer(ViewLayerName);
            if (layer >= 0)
                worldCamera.cullingMask |= 1 << layer;
        }
    }
}
