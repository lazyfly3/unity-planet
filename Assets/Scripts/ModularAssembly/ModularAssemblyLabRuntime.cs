using System;
using System.Collections.Generic;
using System.Linq;
using ModularAssembly;
using SpacecraftEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class GridModuleView : MonoBehaviour
{
    public GridModuleRecord Record { get; private set; }
    public void Initialize(GridModuleRecord record)
    {
        Record = record;
        foreach (UnityPlanet.ModularAssembly.NeoXBehaviorModule behavior in
                 GetComponentsInChildren<UnityPlanet.ModularAssembly.NeoXBehaviorModule>(true))
        {
            behavior.SetLogicalRoot(transform);
        }
    }
}

public sealed class GridAssemblyPresenter : MonoBehaviour
{
    GridAssemblyModel model;
    ShipAssembly assembly;
    Transform coreRoot;
    readonly Dictionary<string, GridModuleView> views = new Dictionary<string, GridModuleView>();

    public event Action Rebuilt;
    public IReadOnlyDictionary<string, GridModuleView> Views => views;

    public void Initialize(GridAssemblyModel source, ShipAssembly shipAssembly, Transform coreVisualRoot)
    {
        model = source;
        assembly = shipAssembly;
        coreRoot = coreVisualRoot;
        model.Changed += Rebuild;
        Rebuild();
    }

    public GridModuleView Find(string runtimeId) =>
        views.TryGetValue(runtimeId, out GridModuleView view) ? view : null;

    public void Rebuild()
    {
        GridModuleRecord[] records = model.Records.ToArray();
        GridAssemblyValidation validation = model.Validate();
        ReconcileViews(records, validation);
    }

    void ReconcileViews(
        IReadOnlyList<GridModuleRecord> records,
        GridAssemblyValidation validation)
    {
        var current = records.ToDictionary(
            record => record.RuntimeId,
            StringComparer.Ordinal);
        ReindexHierarchyViews(current);
        var removeIds = new List<string>();
        foreach (KeyValuePair<string, GridModuleView> pair in views)
        {
            if (pair.Value == null ||
                !current.TryGetValue(
                    pair.Key,
                    out GridModuleRecord record) ||
                pair.Value.Record == null ||
                !string.Equals(
                    pair.Value.Record.Definition.ModuleId,
                    record.Definition.ModuleId,
                    StringComparison.Ordinal))
            {
                removeIds.Add(pair.Key);
            }
        }
        RemoveViews(removeIds);

        foreach (GridModuleRecord record in records)
        {
            if (views.TryGetValue(
                    record.RuntimeId,
                    out GridModuleView existing) &&
                existing != null)
            {
                existing.gameObject.SetActive(true);
                ApplyPose(existing.transform, record);
                existing.Initialize(record);
                continue;
            }
            CreateView(record);
        }
        ApplyValidationTint(validation);
        assembly?.Recalculate();
        Rebuilt?.Invoke();
    }

    void ReindexHierarchyViews(
        IReadOnlyDictionary<string, GridModuleRecord> current)
    {
        GridModuleView[] hierarchyViews =
            GetComponentsInChildren<GridModuleView>(true)
                .Where(view =>
                    view != null &&
                    view.GetComponentInParent<GridModuleView>() == view)
                .ToArray();
        var keep = new Dictionary<string, GridModuleView>(
            StringComparer.Ordinal);
        var discard = new List<GridModuleView>();

        foreach (IGrouping<string, GridModuleView> group in
                 hierarchyViews
                     .Where(view =>
                         view.Record != null &&
                         !string.IsNullOrEmpty(view.Record.RuntimeId))
                     .GroupBy(
                         view => view.Record.RuntimeId,
                         StringComparer.Ordinal))
        {
            if (!current.TryGetValue(
                    group.Key,
                    out GridModuleRecord record))
            {
                discard.AddRange(group);
                continue;
            }

            GridModuleView canonical = group.FirstOrDefault(view =>
                view.gameObject.activeInHierarchy &&
                view.Record != null &&
                string.Equals(
                    view.Record.Definition.ModuleId,
                    record.Definition.ModuleId,
                    StringComparison.Ordinal));
            if (canonical == null)
            {
                discard.AddRange(group);
                continue;
            }

            keep[group.Key] = canonical;
            discard.AddRange(group.Where(view => view != canonical));
        }

        discard.AddRange(hierarchyViews.Where(view =>
            view.Record == null ||
            string.IsNullOrEmpty(view.Record.RuntimeId)));
        views.Clear();
        foreach (KeyValuePair<string, GridModuleView> pair in keep)
            views[pair.Key] = pair.Value;
        RemoveViewInstances(discard);
    }

    void RemoveViews(IEnumerable<string> runtimeIds)
    {
        var removedViews = new List<GridModuleView>();
        foreach (string runtimeId in runtimeIds)
        {
            if (!views.TryGetValue(
                    runtimeId,
                    out GridModuleView view))
                continue;
            views.Remove(runtimeId);
            if (view == null)
                continue;
            removedViews.Add(view);
        }
        RemoveViewInstances(removedViews);
    }

    void RemoveViewInstances(IEnumerable<GridModuleView> removedViews)
    {
        var removedParts = new List<SpacecraftPart>();
        var visited = new HashSet<GridModuleView>();
        foreach (GridModuleView view in
                 removedViews ?? Array.Empty<GridModuleView>())
        {
            if (view == null || !visited.Add(view))
                continue;
            view.gameObject.SetActive(false);
            SpacecraftPart part =
                view.GetComponent<SpacecraftPart>();
            if (assembly != null && part != null)
                removedParts.Add(part);
            else
                DestroyRuntimeObject(view.gameObject);
        }
        assembly?.RemoveParts(removedParts, false);
    }

    public void ForceRebuildFromModel()
    {
        if (model == null)
            return;
        RebuildAll(model.Records.ToArray(), model.Validate());
    }

    void RebuildAll(
        IReadOnlyList<GridModuleRecord> records,
        GridAssemblyValidation validation)
    {
        RemoveViewInstances(
            GetComponentsInChildren<GridModuleView>(true)
                .Where(view =>
                    view != null &&
                    view.GetComponentInParent<GridModuleView>() == view)
                .ToArray());
        views.Clear();
        if (assembly != null)
            assembly.RestoreStates(Array.Empty<PlacedPartState>());
        if (coreRoot != null)
        {
            for (int index = coreRoot.childCount - 1; index >= 0; index--)
            {
                coreRoot.GetChild(index).gameObject.SetActive(false);
                DestroyRuntimeObject(coreRoot.GetChild(index).gameObject);
            }
        }

        foreach (GridModuleRecord record in records)
            CreateView(record);
        ApplyValidationTint(validation);
        assembly?.Recalculate();
        Rebuilt?.Invoke();
    }

    static void DestroyRuntimeObject(UnityEngine.Object value)
    {
        if (value == null)
            return;
        if (Application.isPlaying)
            Destroy(value);
        else
            DestroyImmediate(value);
    }

    GridModuleView CreateView(GridModuleRecord record)
    {
        GameObject instance;
        if (record.Definition.Category == GridModuleCategory.Core)
        {
            instance = Instantiate(record.Definition.Prefab, coreRoot);
            ApplyPose(instance.transform, record);
        }
        else if (assembly != null && record.Definition.RuntimePartDefinition != null)
        {
            SpacecraftPart part = assembly.AddGenericPart(
                record.Definition.RuntimePartDefinition,
                GridAssemblyModel.ModuleCenter(record),
                GridOrientation.Rotation(record.Pose.orientation),
                1f,
                record.RuntimeId,
                record.Pose.mirrorGroupId);
            instance = part == null ? null : part.gameObject;
        }
        else
        {
            instance = Instantiate(record.Definition.Prefab, transform);
            ApplyPose(instance.transform, record);
        }
        if (instance == null)
            return null;
        instance.name = record.Definition.DisplayName + "_" +
                        record.RuntimeId.Substring(
                            0,
                            Mathf.Min(6, record.RuntimeId.Length));
        GridModuleView view = instance.GetComponent<GridModuleView>() ??
                              instance.AddComponent<GridModuleView>();
        view.Initialize(record);
        views[record.RuntimeId] = view;
        return view;
    }

    void ApplyValidationTint(GridAssemblyValidation validation)
    {
        var disconnected = new HashSet<string>(validation.DisconnectedIds);
        foreach (KeyValuePair<string, GridModuleView> pair in views)
        {
            if (pair.Value == null)
                continue;
            SetTint(
                pair.Value.gameObject,
                disconnected.Contains(pair.Key)
                    ? new Color(1f, 0.18f, 0.12f)
                    : Color.white);
        }
    }

    public void ClearVisuals()
    {
        RemoveViewInstances(
            GetComponentsInChildren<GridModuleView>(true)
                .Where(view =>
                    view != null &&
                    view.GetComponentInParent<GridModuleView>() == view)
                .ToArray());
        views.Clear();
    }

    static void ApplyPose(Transform target, GridModuleRecord record)
    {
        target.localPosition = GridAssemblyModel.ModuleCenter(record);
        target.localRotation = GridOrientation.Rotation(record.Pose.orientation);
        target.localScale = Vector3.one;
    }

    static void SetTint(GameObject target, Color color)
    {
        var block = new MaterialPropertyBlock();
        block.SetColor("_BaseColor", color);
        block.SetColor("_Color", color);
        foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true))
            renderer.SetPropertyBlock(color == Color.white ? null : block);
    }

    void OnDestroy()
    {
        if (model != null)
            model.Changed -= Rebuild;
    }
}

public enum GridFlightState
{
    Build,
    LoadingTerrain,
    Flight
}

public sealed class GridLabCameraController : MonoBehaviour
{
    static readonly Vector2 FlightAnchor = new Vector2(0.42f, 0.22f);
    static readonly Vector2 AimAnchor = new Vector2(0.34f, 0.21f);
    const float FlightBaseFieldOfView = 65f;
    const float BoostFieldOfView = 75f;
    const float BoostEnterSmoothTime = 0.12f;
    const float BoostExitSmoothTime = 0.28f;

    Camera targetCamera;
    Transform target;
    Rigidbody targetBody;
    UnityPlanet.ModularAssembly.RobocraftMotionCoordinator motionController;
    bool flightMode;
    float yaw = 38f;
    float pitch = 24f;
    float distance = 17f;
    Vector3 panOffset;
    float flightAimYaw;
    float flightAimPitch;
    float freeLookYaw;
    float freeLookPitch;
    Bounds flightLocalBounds;
    Bounds flightTargetLocalBounds;
    bool hasFlightLocalBounds;
    float nextFlightBoundsRefresh;
    float baseFieldOfView = 60f;
    float requestedFieldOfView = 60f;
    float boostFieldOfViewOffset;
    float boostFieldOfViewVelocity;
    Vector3 previousVelocity;
    bool hasPreviousVelocity;
    bool aimPresentation;
    bool precisionAim;
    float damageImpulseStartedAt;
    float damageImpulseDuration;
    float damageImpulseStrength;
    Vector2 damageImpulseDirection = Vector2.up;
    int damageImpulseSequence;
    readonly RaycastHit[] cameraHits = new RaycastHit[16];

    public Vector3 FlightAimForward =>
        Quaternion.Euler(flightAimPitch, flightAimYaw, 0f) *
        Vector3.forward;

    public void Initialize(Camera camera, Transform focus)
    {
        targetCamera = camera;
        target = focus;
        targetBody = target != null
            ? target.GetComponent<Rigidbody>()
            : null;
        motionController = target != null
            ? target.GetComponent<
                UnityPlanet.ModularAssembly.RobocraftMotionCoordinator>()
            : null;
        if (targetCamera != null)
        {
            baseFieldOfView = targetCamera.fieldOfView;
            requestedFieldOfView = baseFieldOfView;
            targetCamera.nearClipPlane = Mathf.Min(
                targetCamera.nearClipPlane,
                0.08f);
        }
        ApplyImmediate();
    }

    public void SetAimPresentation(bool aiming, bool precision)
    {
        aimPresentation = aiming;
        precisionAim = aiming && precision;
        requestedFieldOfView = ResolveRequestedFieldOfView();
    }

    public void AddDamageImpulse(
        Vector3 worldIncomingDirection,
        float severity)
    {
        if (targetCamera == null)
            return;
        Vector3 local = worldIncomingDirection.sqrMagnitude > 0.0001f
            ? targetCamera.transform.InverseTransformDirection(
                worldIncomingDirection.normalized)
            : Vector3.forward;
        Vector2 direction = new Vector2(local.x, -local.y);
        if (local.z < 0f)
            direction = -direction;
        damageImpulseDirection = direction.sqrMagnitude > 0.0001f
            ? direction.normalized
            : Vector2.up;
        float clampedSeverity = Mathf.Clamp01(severity);
        damageImpulseStrength = Mathf.Max(
            damageImpulseStrength * 0.55f,
            clampedSeverity);
        damageImpulseStartedAt = Time.unscaledTime;
        damageImpulseDuration = Mathf.Lerp(
            0.17f,
            0.32f,
            clampedSeverity);
        damageImpulseSequence++;
    }

    public void SetFlightMode(bool value)
    {
        flightMode = value;
        boostFieldOfViewOffset = 0f;
        boostFieldOfViewVelocity = 0f;
        hasPreviousVelocity = false;
        if (targetBody != null)
            previousVelocity = targetBody.velocity;
        requestedFieldOfView = ResolveRequestedFieldOfView();
        yaw = value ? 0f : 38f;
        pitch = value ? 12f : 24f;
        if (value)
        {
            Vector3 horizontalForward =
                Vector3.ProjectOnPlane(target.forward, Vector3.up);
            if (horizontalForward.sqrMagnitude > 0.0001f)
            {
                horizontalForward.Normalize();
                flightAimYaw = Mathf.Atan2(
                    horizontalForward.x,
                    horizontalForward.z) * Mathf.Rad2Deg;
            }
            flightAimPitch = Mathf.Clamp(
                -Mathf.Asin(Mathf.Clamp(
                    target.forward.y,
                    -1f,
                    1f)) * Mathf.Rad2Deg,
                -55f,
                55f);
            freeLookYaw = 0f;
            freeLookPitch = 0f;
            CaptureFlightBounds();
            ResolveFlightFraming();
            nextFlightBoundsRefresh = Time.unscaledTime + 0.25f;
        }
        else
        {
            distance = 17f;
            SetAimPresentation(false, false);
        }
        panOffset = Vector3.zero;
    }

    void LateUpdate()
    {
        if (targetCamera == null || target == null)
            return;
        bool tuningInputCaptured =
            UnityPlanet.ModularAssembly.
                ArcadeFlightRuntimeTuningOverlay.IsInputCaptured;
        if (flightMode &&
            Time.unscaledTime >= nextFlightBoundsRefresh)
        {
            CaptureFlightBounds();
            nextFlightBoundsRefresh = Time.unscaledTime + 0.25f;
        }
        if (!flightMode)
        {
            if (Input.GetMouseButton(1))
            {
                yaw += Input.GetAxisRaw("Mouse X") * 4f;
                pitch = Mathf.Clamp(pitch - Input.GetAxisRaw("Mouse Y") * 3f, -80f, 80f);
            }
            if (Input.GetMouseButton(2))
            {
                panOffset += (-targetCamera.transform.right * Input.GetAxisRaw("Mouse X")
                    - targetCamera.transform.up * Input.GetAxisRaw("Mouse Y")) * 0.15f;
            }
            distance = Mathf.Clamp(distance - Input.mouseScrollDelta.y * 1.2f, 5f, 58f);
        }
        else if (!tuningInputCaptured &&
                 (Input.GetKey(KeyCode.LeftAlt) ||
                  Input.GetKey(KeyCode.RightAlt)))
        {
            freeLookYaw += Input.GetAxisRaw("Mouse X") * 3f;
            freeLookPitch = Mathf.Clamp(
                freeLookPitch -
                Input.GetAxisRaw("Mouse Y") * 2.5f,
                -55f,
                70f);
        }
        else if (flightMode && !tuningInputCaptured)
        {
            flightAimYaw += Input.GetAxisRaw("Mouse X") * 2.2f;
            flightAimPitch = Mathf.Clamp(
                flightAimPitch -
                Input.GetAxisRaw("Mouse Y") * 1.8f,
                -55f,
                55f);
            float recenter =
                1f - Mathf.Exp(-7f * Time.unscaledDeltaTime);
            freeLookYaw = Mathf.LerpAngle(
                freeLookYaw,
                0f,
                recenter);
            freeLookPitch = Mathf.Lerp(
                freeLookPitch,
                0f,
                recenter);
        }
        UpdateSmoothedFlightBounds();
        UpdateSpeedFieldOfView();
        ApplyImmediate();
    }

    void UpdateSpeedFieldOfView()
    {
        float deltaTime = Mathf.Max(0.0001f, Time.unscaledDeltaTime);
        if (!flightMode || targetBody == null)
        {
            boostFieldOfViewOffset = Mathf.SmoothDamp(
                boostFieldOfViewOffset,
                0f,
                ref boostFieldOfViewVelocity,
                BoostExitSmoothTime,
                Mathf.Infinity,
                deltaTime);
            hasPreviousVelocity = false;
            requestedFieldOfView = ResolveRequestedFieldOfView();
            return;
        }

        Vector3 velocity = targetBody.velocity;
        Vector3 acceleration = hasPreviousVelocity
            ? (velocity - previousVelocity) / deltaTime
            : Vector3.zero;
        previousVelocity = velocity;
        hasPreviousVelocity = true;

        bool tuningInputCaptured =
            UnityPlanet.ModularAssembly.
                ArcadeFlightRuntimeTuningOverlay.IsInputCaptured;
        bool boostRequested = !tuningInputCaptured &&
            (Input.GetKey(KeyCode.LeftShift) ||
             Input.GetKey(KeyCode.RightShift));
        Vector3 movementDirection = Vector3.zero;
        float presentationWeight = 0f;
        bool hasMovement = !tuningInputCaptured &&
            TryGetBoostDirection(
                out movementDirection,
                out presentationWeight);

        float targetOffset = 0f;
        if (boostRequested &&
            hasMovement &&
            motionController != null &&
            motionController.IsActive)
        {
            Vector3 actualControlForce =
                motionController.Telemetry.actualControlForceWorld;
            float mass = Mathf.Max(1f, targetBody.mass);
            float directionalForceAcceleration = Mathf.Max(
                0f,
                Vector3.Dot(actualControlForce, movementDirection) / mass);
            float directionalAcceleration = Mathf.Max(
                0f,
                Vector3.Dot(acceleration, movementDirection));
            float directionalSpeed = Mathf.Max(
                0f,
                Vector3.Dot(velocity, movementDirection));

            // Speed alone must not trigger boost presentation. At least some
            // measured propulsion or acceleration is required.
            bool hasPhysicalOutput =
                directionalForceAcceleration > 0.1f ||
                directionalAcceleration > 0.25f;
            if (hasPhysicalOutput)
            {
                float propulsionFactor = Mathf.Clamp01(
                    directionalForceAcceleration / 12f);
                float accelerationFactor = Mathf.Clamp01(
                    directionalAcceleration / 16f);
                float speedFactor = Mathf.Clamp01(
                    directionalSpeed / 80f);
                float physicalIntensity = Mathf.Clamp01(
                    propulsionFactor * 0.5f +
                    accelerationFactor * 0.3f +
                    speedFactor * 0.2f);
                targetOffset =
                    (BoostFieldOfView - FlightBaseFieldOfView) *
                    physicalIntensity *
                    presentationWeight;
            }
        }

        float smoothTime = targetOffset > boostFieldOfViewOffset
            ? BoostEnterSmoothTime
            : BoostExitSmoothTime;
        boostFieldOfViewOffset = Mathf.SmoothDamp(
            boostFieldOfViewOffset,
            targetOffset,
            ref boostFieldOfViewVelocity,
            smoothTime,
            Mathf.Infinity,
            deltaTime);
        requestedFieldOfView = ResolveRequestedFieldOfView();
    }

    bool TryGetBoostDirection(
        out Vector3 worldDirection,
        out float presentationWeight)
    {
        float forwardInput =
            (Input.GetKey(KeyCode.W) ? 1f : 0f) -
            (Input.GetKey(KeyCode.S) ? 1f : 0f);
        float lateralInput =
            (Input.GetKey(KeyCode.D) ? 1f : 0f) -
            (Input.GetKey(KeyCode.A) ? 1f : 0f);
        float verticalInput =
            (Input.GetKey(KeyCode.Space) ? 1f : 0f) -
            ((Input.GetKey(KeyCode.LeftControl) ||
              Input.GetKey(KeyCode.RightControl)) ? 1f : 0f);

        Vector3 horizontalForward = Vector3.ProjectOnPlane(
            FlightAimForward,
            Vector3.up);
        if (horizontalForward.sqrMagnitude < 0.0001f)
            horizontalForward = Vector3.forward;
        else
            horizontalForward.Normalize();
        Vector3 horizontalRight = Vector3.Cross(
            Vector3.up,
            horizontalForward).normalized;

        worldDirection =
            horizontalForward * forwardInput +
            horizontalRight * lateralInput +
            Vector3.up * verticalInput;
        if (worldDirection.sqrMagnitude < 0.0001f)
        {
            presentationWeight = 0f;
            return false;
        }
        worldDirection.Normalize();

        float forwardAmount = Mathf.Max(0f, forwardInput);
        float reverseAmount = Mathf.Max(0f, -forwardInput);
        float lateralAmount = Mathf.Abs(lateralInput);
        float verticalAmount = Mathf.Abs(verticalInput);
        float total =
            forwardAmount +
            reverseAmount +
            lateralAmount +
            verticalAmount;
        presentationWeight = Mathf.Clamp01(
            (forwardAmount +
             reverseAmount * 0.6f +
             lateralAmount * 0.45f +
             verticalAmount * 0.35f) /
            Mathf.Max(1f, total));
        return true;
    }

    float ResolveRequestedFieldOfView()
    {
        if (!flightMode)
            return baseFieldOfView;
        if (aimPresentation)
            return precisionAim ? 22f : 38f;
        return FlightBaseFieldOfView + boostFieldOfViewOffset;
    }

    void CaptureFlightBounds()
    {
        if (target == null)
            return;

        bool captured = false;
        Bounds capturedBounds = default;
        Renderer[] renderers =
            target.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null ||
                renderer is ParticleSystemRenderer ||
                renderer is LineRenderer ||
                renderer is TrailRenderer ||
                !renderer.enabled ||
                !renderer.gameObject.activeInHierarchy)
                continue;

            GridModuleView moduleView =
                renderer.GetComponentInParent<GridModuleView>();
            if (moduleView == null ||
                (moduleView.transform != target &&
                 !moduleView.transform.IsChildOf(target)))
                continue;

            Bounds worldBounds = renderer.bounds;
            Vector3 center = worldBounds.center;
            Vector3 extents = worldBounds.extents;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 worldCorner = center + new Vector3(
                    extents.x * x,
                    extents.y * y,
                    extents.z * z);
                Vector3 localCorner =
                    target.InverseTransformPoint(worldCorner);
                if (!captured)
                {
                    capturedBounds =
                        new Bounds(localCorner, Vector3.zero);
                    captured = true;
                }
                else
                    capturedBounds.Encapsulate(localCorner);
            }
        }

        if (!captured)
            capturedBounds =
                new Bounds(Vector3.zero, Vector3.one * 2f);

        Vector3 safeMinimum = Vector3.Max(
            capturedBounds.min,
            new Vector3(-24f, -24f, -24f));
        Vector3 safeMaximum = Vector3.Min(
            capturedBounds.max,
            new Vector3(24f, 24f, 24f));
        if (safeMinimum.x <= safeMaximum.x &&
            safeMinimum.y <= safeMaximum.y &&
            safeMinimum.z <= safeMaximum.z)
        {
            capturedBounds.SetMinMax(
                safeMinimum,
                safeMaximum);
        }
        else
            capturedBounds =
                new Bounds(Vector3.zero, Vector3.one * 2f);

        flightTargetLocalBounds = capturedBounds;
        if (!hasFlightLocalBounds)
        {
            flightLocalBounds = capturedBounds;
            hasFlightLocalBounds = true;
        }
    }

    void UpdateSmoothedFlightBounds()
    {
        if (!hasFlightLocalBounds)
            return;
        float deltaTime = Mathf.Max(0.0001f, Time.unscaledDeltaTime);
        bool expanding =
            flightTargetLocalBounds.size.sqrMagnitude >
            flightLocalBounds.size.sqrMagnitude;
        float sizeBlend = 1f - Mathf.Exp(
            -(expanding ? 12f : 2.8f) * deltaTime);
        float centerBlend = 1f - Mathf.Exp(-5f * deltaTime);
        flightLocalBounds.center = Vector3.Lerp(
            flightLocalBounds.center,
            flightTargetLocalBounds.center,
            centerBlend);
        flightLocalBounds.size = Vector3.Max(
            Vector3.one * 0.5f,
            Vector3.Lerp(
                flightLocalBounds.size,
                flightTargetLocalBounds.size,
                sizeBlend));
    }

    void ResolveFlightGeometry(
        Quaternion viewRotation,
        out Vector3 worldCenter,
        out float halfWidth,
        out float halfHeight,
        out float halfDepth,
        out float radius)
    {
        Vector3 localCenter = flightLocalBounds.center;
        Vector3 localExtents = flightLocalBounds.extents;
        worldCenter = target.TransformPoint(localCenter);
        Vector3 viewRight = viewRotation * Vector3.right;
        Vector3 viewUp = viewRotation * Vector3.up;
        Vector3 viewForward = viewRotation * Vector3.forward;
        halfWidth = 0f;
        halfHeight = 0f;
        halfDepth = 0f;
        radius = 0f;
        for (int x = -1; x <= 1; x += 2)
        for (int y = -1; y <= 1; y += 2)
        for (int z = -1; z <= 1; z += 2)
        {
            Vector3 localCorner = localCenter + new Vector3(
                localExtents.x * x,
                localExtents.y * y,
                localExtents.z * z);
            Vector3 worldCorner =
                target.TransformPoint(localCorner);
            Vector3 relative = worldCorner - worldCenter;
            halfWidth = Mathf.Max(
                halfWidth,
                Mathf.Abs(Vector3.Dot(relative, viewRight)));
            halfHeight = Mathf.Max(
                halfHeight,
                Mathf.Abs(Vector3.Dot(relative, viewUp)));
            halfDepth = Mathf.Max(
                halfDepth,
                Mathf.Abs(Vector3.Dot(relative, viewForward)));
            radius = Mathf.Max(radius, relative.magnitude);
        }
    }

    float ResolvePresentationDistance(Quaternion viewRotation)
    {
        if (!hasFlightLocalBounds || targetCamera == null)
            return 14f;
        ResolveFlightGeometry(
            viewRotation,
            out _,
            out float halfWidth,
            out float halfHeight,
            out float halfDepth,
            out float radius);
        Vector2 anchor = aimPresentation ? AimAnchor : FlightAnchor;
        float safeTop = aimPresentation ? 0.405f : 0.415f;
        float allowedHalfHeight = Mathf.Max(
            0.105f,
            Mathf.Min(anchor.y - 0.04f, safeTop - anchor.y));
        float allowedHalfWidth = Mathf.Max(
            0.2f,
            Mathf.Min(anchor.x - 0.03f, 0.97f - anchor.x));
        float verticalTangent = Mathf.Tan(
            Mathf.Clamp(targetCamera.fieldOfView, 18f, 80f) *
            0.5f *
            Mathf.Deg2Rad);
        float horizontalTangent =
            verticalTangent * Mathf.Max(0.5f, targetCamera.aspect);
        float verticalDistance =
            halfHeight /
            Mathf.Max(0.01f, 2f * allowedHalfHeight * verticalTangent);
        float horizontalDistance =
            halfWidth /
            Mathf.Max(0.01f, 2f * allowedHalfWidth * horizontalTangent);
        return Mathf.Clamp(
            Mathf.Max(verticalDistance, horizontalDistance) +
            halfDepth +
            0.75f,
            Mathf.Max(8f, radius * 1.15f),
            180f);
    }

    void ApplyImmediate()
    {
        if (targetCamera == null || target == null)
            return;
        float fovBlend = Application.isPlaying
            ? 1f - Mathf.Exp(-9f * Time.unscaledDeltaTime)
            : 1f;
        targetCamera.fieldOfView = Mathf.Lerp(
            targetCamera.fieldOfView,
            requestedFieldOfView,
            fovBlend);
        Quaternion aimRotation = Quaternion.Euler(
            flightAimPitch,
            flightAimYaw,
            0f);
        Quaternion orbit = flightMode
            ? aimRotation * Quaternion.Euler(
                freeLookPitch,
                freeLookYaw,
                0f)
            : Quaternion.Euler(pitch, yaw, 0f);
        float blend = Application.isPlaying
            ? 1f - Mathf.Exp(-9f * Time.unscaledDeltaTime)
            : 1f;
        if (!flightMode)
        {
            Vector3 focus = target.position + panOffset;
            Vector3 desired =
                focus + orbit * new Vector3(0f, 0f, -distance);
            targetCamera.transform.position = Vector3.Lerp(
                targetCamera.transform.position,
                desired,
                blend);
            targetCamera.transform.rotation = Quaternion.Slerp(
                targetCamera.transform.rotation,
                Quaternion.LookRotation(focus - desired, target.up) *
                ResolveDamageKickRotation(),
                blend);
            return;
        }

        ResolveFlightGeometry(
            orbit,
            out Vector3 worldCenter,
            out float halfWidth,
            out float halfHeight,
            out _,
            out float radius);
        float targetDistance = ResolvePresentationDistance(orbit);
        float distanceBlend =
            1f - Mathf.Exp(-6f * Time.unscaledDeltaTime);
        distance = Mathf.Lerp(distance, targetDistance, distanceBlend);
        Vector2 anchor = aimPresentation ? AimAnchor : FlightAnchor;
        float verticalTangent = Mathf.Tan(
            targetCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float horizontalTangent =
            verticalTangent * Mathf.Max(0.5f, targetCamera.aspect);
        float localX =
            (anchor.x - 0.5f) * 2f * distance * horizontalTangent;
        float localY =
            (anchor.y - 0.5f) * 2f * distance * verticalTangent;
        Vector3 desiredCamera = worldCenter -
            orbit * new Vector3(localX, localY, distance);
        desiredCamera = ResolveCameraCollision(
            worldCenter,
            desiredCamera,
            orbit,
            Mathf.Max(2.2f, radius + 0.65f),
            halfWidth,
            halfHeight);
        targetCamera.transform.position = Vector3.Lerp(
            targetCamera.transform.position,
            desiredCamera,
            blend);
        targetCamera.transform.rotation = Quaternion.Slerp(
            targetCamera.transform.rotation,
            orbit * ResolveDamageKickRotation(),
            1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));
    }

    Quaternion ResolveDamageKickRotation()
    {
        if (damageImpulseDuration <= 0.001f)
            return Quaternion.identity;
        float progress = Mathf.Clamp01(
            (Time.unscaledTime - damageImpulseStartedAt) /
            damageImpulseDuration);
        if (progress >= 1f)
        {
            damageImpulseStrength = 0f;
            damageImpulseDuration = 0f;
            return Quaternion.identity;
        }
        float envelope = (1f - progress) * (1f - progress);
        float oscillation = Mathf.Sin(
            progress * Mathf.PI * 2.6f +
            damageImpulseSequence * 1.37f);
        float strength = damageImpulseStrength * envelope;
        float pitchKick =
            (-damageImpulseDirection.y * 1.35f +
             oscillation * 0.28f) * strength;
        float yawKick =
            (-damageImpulseDirection.x * 1.9f +
             oscillation * 0.22f) * strength;
        float rollKick =
            (-damageImpulseDirection.x * 1.25f) * strength;
        return Quaternion.Euler(pitchKick, yawKick, rollKick);
    }

    void ResolveFlightFraming()
    {
        Quaternion rotation = Quaternion.Euler(
            flightAimPitch,
            flightAimYaw,
            0f);
        distance = ResolvePresentationDistance(rotation);
    }

    Vector3 ResolveCameraCollision(
        Vector3 focus,
        Vector3 desired,
        Quaternion viewRotation,
        float minimumDistance,
        float halfWidth,
        float halfHeight)
    {
        if (!TryGetCameraObstruction(focus, desired, out float nearest))
            return desired;

        Vector3 viewUp = viewRotation * Vector3.up;
        Vector3 viewRight = viewRotation * Vector3.right;
        float lift = Mathf.Clamp(halfHeight * 0.45f, 1.2f, 7f);
        float side = Mathf.Clamp(halfWidth * 0.35f, 1f, 6f);
        Vector3[] alternatives =
        {
            desired + viewUp * lift,
            desired + viewUp * lift + viewRight * side,
            desired + viewUp * lift - viewRight * side
        };
        foreach (Vector3 alternative in alternatives)
            if (!TryGetCameraObstruction(focus, alternative, out _))
                return alternative;

        Vector3 offset = desired - focus;
        float length = offset.magnitude;
        if (length <= 0.01f || nearest >= length)
            return desired;
        float resolvedDistance = Mathf.Max(1.5f, nearest - 0.4f);
        if (resolvedDistance < minimumDistance)
        {
            Vector3 elevated =
                focus +
                viewUp * Mathf.Max(lift, minimumDistance * 0.35f) +
                offset.normalized * minimumDistance;
            if (!TryGetCameraObstruction(focus, elevated, out _))
                return elevated;
        }
        return focus + offset.normalized * resolvedDistance;
    }

    bool TryGetCameraObstruction(
        Vector3 focus,
        Vector3 desired,
        out float nearest)
    {
        Vector3 offset = desired - focus;
        float length = offset.magnitude;
        nearest = length;
        if (length <= 0.01f)
            return false;
        int count = Physics.SphereCastNonAlloc(
            focus,
            0.38f,
            offset / length,
            cameraHits,
            length,
            ~0,
            QueryTriggerInteraction.Ignore);
        for (int index = 0; index < count; index++)
        {
            Collider collider = cameraHits[index].collider;
            if (collider == null
                || (target != null
                    && collider.transform.IsChildOf(target)))
            {
                continue;
            }
            nearest = Mathf.Min(nearest, cameraHits[index].distance);
        }
        return nearest < length;
    }
}

public sealed class GridFlightBridge : MonoBehaviour,
    UnityPlanet.ModularAssembly.IGridFlightSession
{
    Rigidbody body;
    ShipAssembly assembly;
    GridLabCameraController cameraController;
    Vector3 buildPosition;
    Quaternion buildRotation;
    Vector3 spawnPosition;
    Quaternion spawnRotation = Quaternion.identity;
    Coroutine enterRoutine;
    Coroutine resetRoutine;
    UnityPlanet.ModularAssembly.IGridFlightEnvironment environment;
    UnityPlanet.ModularAssembly.RobocraftMotionCoordinator motionRc1;

    public GridFlightState State { get; private set; } = GridFlightState.Build;
    public bool IsFlying => State == GridFlightState.Flight;
    public Rigidbody Body => body;
    public event Action<GridFlightState, string> StateChanged;

    public void Initialize(
        Rigidbody shipBody,
        ShipAssembly shipAssembly,
        GridLabCameraController cameraRig)
    {
        body = shipBody;
        assembly = shipAssembly;
        cameraController = cameraRig;
        motionRc1 = GetComponent<
                        UnityPlanet.ModularAssembly.RobocraftMotionCoordinator>()
                    ?? gameObject.AddComponent<
                        UnityPlanet.ModularAssembly.RobocraftMotionCoordinator>();
        motionRc1.Configure(body, assembly);
        buildPosition = body.position;
        buildRotation = body.rotation;
        spawnPosition = buildPosition;
        body.isKinematic = true;
        body.useGravity = false;
    }

public void EnterFlight()
    {
        if (State == GridFlightState.LoadingTerrain)
        {
            ExitFlight();
            return;
        }
        if (State == GridFlightState.Flight)
            return;

        environment = FindFlightEnvironment(gameObject.scene);
        bool expectsCombatMap =
            UnityPlanet.ModularAssembly.ModularLabSceneProfile
                .AllowsCombatMapFlightEnvironment(gameObject.scene);
        string loadingMessage =
            environment is UnityPlanet.CombatMap
                .CombatMapFlightEnvironmentController
                ? "正在隔离测量飞船盘旋半径并生成战斗试飞地形……"
                : "正在准备试飞环境……";
        SetState(GridFlightState.LoadingTerrain, loadingMessage);
        body.isKinematic = true;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        cameraController.SetFlightMode(true);

        if (environment == null)
        {
            if (expectsCombatMap)
            {
                ReturnToBuild(
                    "战斗地图试飞环境缺失，已阻止从建造台错误起飞。");
                return;
            }
            CompleteFlightPreparation(
                true,
                SafeSpawnAbove(buildPosition, body),
                "未找到场景飞行环境，已使用兼容安全出生点。");
            return;
        }
        enterRoutine = StartCoroutine(environment.PrepareFlight(
            body,
            CompleteFlightPreparation));
    }

void CompleteFlightPreparation(
        bool success,
        Vector3 position,
        string message)
    {
        enterRoutine = null;
        if (State != GridFlightState.LoadingTerrain)
            return;
        if (!success)
        {
            ReturnToBuild(message);
            return;
        }

        spawnPosition = position;
        spawnRotation = environment != null
            ? environment.PreparedRotation
            : Quaternion.identity;
        body.position = position;
        body.rotation = spawnRotation;
        Physics.SyncTransforms();
        cameraController.SetFlightMode(true);
        body.isKinematic = false;
        body.velocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.WakeUp();
        motionRc1.BeginFlight();
        SetState(GridFlightState.Flight, message);
    }

    static Vector3 SafeSpawnAbove(Vector3 origin, Rigidbody target)
    {
        float clearance = 4f;
        Collider[] colliders = target != null
            ? target.GetComponentsInChildren<Collider>(true)
            : Array.Empty<Collider>();
        bool initialized = false;
        Bounds bounds = new Bounds();
        foreach (Collider collider in colliders)
        {
            if (collider == null || !collider.enabled || collider.isTrigger)
                continue;
            if (!initialized)
            {
                bounds = collider.bounds;
                initialized = true;
            }
            else
                bounds.Encapsulate(collider.bounds);
        }
        if (initialized)
            clearance = Mathf.Max(clearance, bounds.extents.y + 2f);
        return origin + Vector3.up * clearance;
    }

public void ExitFlight()
    {
        if (State == GridFlightState.Build)
            return;
        if (enterRoutine != null)
        {
            StopCoroutine(enterRoutine);
            enterRoutine = null;
        }
        if (resetRoutine != null)
        {
            StopCoroutine(resetRoutine);
            resetRoutine = null;
        }
        ReturnToBuild("已返回建造台。");
    }

    public void ResetFlight()
    {
        if (State != GridFlightState.Flight)
            return;
        if (resetRoutine != null)
            StopCoroutine(resetRoutine);
        motionRc1?.EndFlight();
        if (!body.isKinematic)
        {
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        body.isKinematic = true;
        if (environment == null)
        {
            CompleteReset(true, spawnPosition);
            return;
        }
        resetRoutine = StartCoroutine(
            environment.ResetFlight(body, CompleteReset));
    }

void CompleteReset(bool success, Vector3 position)
    {
        resetRoutine = null;
        if (State != GridFlightState.Flight)
            return;
        if (success)
        {
            spawnPosition = position;
            if (environment != null)
                spawnRotation = environment.PreparedRotation;
        }
        body.position = spawnPosition;
        body.rotation = spawnRotation;
        Physics.SyncTransforms();
        cameraController.SetFlightMode(true);
        body.isKinematic = false;
        body.velocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.WakeUp();
        motionRc1?.BeginFlight();
    }

    void SetState(GridFlightState value, string message)
    {
        State = value;
        StateChanged?.Invoke(value, message);
    }

    public float DistanceFromSpawn => Vector3.Distance(body.position, spawnPosition);


void ReturnToBuild(string message)
    {
        motionRc1?.EndFlight();
        environment?.ExitFlight();
        if (!body.isKinematic)
        {
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        body.isKinematic = true;
        body.position = buildPosition;
        body.rotation = buildRotation;
        Physics.SyncTransforms();
        environment = null;
        spawnRotation = Quaternion.identity;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        cameraController.SetFlightMode(false);
        SetState(GridFlightState.Build, message);
    }

    static UnityPlanet.ModularAssembly.IGridFlightEnvironment
        FindFlightEnvironment(UnityEngine.SceneManagement.Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return null;
        UnityPlanet.ModularAssembly.IGridFlightEnvironment best = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        foreach (MonoBehaviour behaviour in
                 root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null || !behaviour.isActiveAndEnabled ||
                !(behaviour is UnityPlanet.ModularAssembly
                    .IGridFlightEnvironment candidate))
            {
                continue;
            }
            if (candidate is UnityPlanet.ModularAssembly
                    .PlanetLabFlightEnvironmentController &&
                !UnityPlanet.ModularAssembly.ModularLabSceneProfile
                    .AllowsPlanetLabFlightEnvironment(scene))
            {
                continue;
            }
            if (candidate is UnityPlanet.CombatMap
                    .CombatMapFlightEnvironmentController &&
                !UnityPlanet.ModularAssembly.ModularLabSceneProfile
                    .AllowsCombatMapFlightEnvironment(scene))
            {
                continue;
            }
            if (best == null || candidate.Priority > best.Priority)
                best = candidate;
        }
        return best;
    }
}

public sealed class GridKineticWeaponSystem : MonoBehaviour
{
    const int PoolSize = 64;
    readonly Queue<GridKineticProjectile> available = new Queue<GridKineticProjectile>();
    readonly List<GridKineticProjectile> all = new List<GridKineticProjectile>();
    GridAssemblyPresenter presenter;
    GridFlightBridge flight;
    float nextShot;
    Material projectileMaterial;

    public void Initialize(GridAssemblyPresenter source, GridFlightBridge flightBridge)
    {
        presenter = source;
        flight = flightBridge;
        projectileMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
        projectileMaterial.color = new Color(1f, 0.72f, 0.18f);
        for (int index = 0; index < PoolSize; index++)
        {
            var projectile = CreateProjectile(index);
            projectile.gameObject.SetActive(false);
            available.Enqueue(projectile);
            all.Add(projectile);
        }
    }

    void Update()
    {
        if (GetComponent<
                UnityPlanet.ModularAssembly.WeaponSystemCoordinator>() != null)
        {
            enabled = false;
            return;
        }
        if (UnityPlanet.ModularAssembly.
                ArcadeFlightRuntimeTuningOverlay.IsInputCaptured ||
            flight == null ||
            !flight.IsFlying ||
            !Input.GetMouseButton(0) ||
            Time.time < nextShot)
            return;
        List<GridModuleView> weapons = presenter.Views.Values
            .Where(view => view != null && view.Record.Definition.Category == GridModuleCategory.KineticWeapon)
            .ToList();
        if (weapons.Count == 0)
            return;
        nextShot = Time.time + 0.25f;
        foreach (GridModuleView weapon in weapons)
            Fire(weapon);
    }

    void Fire(GridModuleView weapon)
    {
        if (available.Count == 0)
            all[0].Release();
        GridKineticProjectile projectile = available.Dequeue();
        UnityPlanet.ModularAssembly.NeoXBehaviorModule semantics =
            weapon.GetComponentInChildren<UnityPlanet.ModularAssembly.NeoXBehaviorModule>(true);
        Vector3 direction = semantics != null
            ? semantics.WorldMuzzleDirection
            : weapon.transform.forward;
        Vector3 position = semantics != null
            ? semantics.WorldMuzzlePosition
            : weapon.transform.position + direction * 1.2f;
        projectile.Launch(position, direction * 180f, transform, Release);
    }

    GridKineticProjectile CreateProjectile(int index)
    {
        GameObject target = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        target.name = "KineticProjectile_" + index.ToString("00");
        target.transform.SetParent(transform, false);
        target.transform.localScale = Vector3.one * 0.14f;
        Destroy(target.GetComponent<Collider>());
        target.GetComponent<Renderer>().sharedMaterial = projectileMaterial;
        var trail = target.AddComponent<TrailRenderer>();
        trail.time = 0.18f;
        trail.startWidth = 0.08f;
        trail.endWidth = 0f;
        trail.material = projectileMaterial;
        return target.AddComponent<GridKineticProjectile>();
    }

    void Release(GridKineticProjectile projectile)
    {
        if (projectile == null || available.Contains(projectile))
            return;
        projectile.gameObject.SetActive(false);
        available.Enqueue(projectile);
    }
}

public sealed class GridKineticProjectile : MonoBehaviour
{
    readonly RaycastHit[] hits = new RaycastHit[16];
    Vector3 velocity;
    Transform owner;
    Action<GridKineticProjectile> release;
    float expiresAt;
    Vector3 previousPosition;

    public void Launch(Vector3 position, Vector3 launchVelocity, Transform source, Action<GridKineticProjectile> callback)
    {
        transform.position = position;
        previousPosition = position;
        velocity = launchVelocity;
        owner = source;
        release = callback;
        expiresAt = Time.time + 5f;
        gameObject.SetActive(true);
        TrailRenderer trail = GetComponent<TrailRenderer>();
        if (trail != null)
            trail.Clear();
    }

    void FixedUpdate()
    {
        if (Time.time >= expiresAt)
        {
            Release();
            return;
        }
        Vector3 next = transform.position + velocity * Time.fixedDeltaTime;
        Vector3 delta = next - transform.position;
        int count = Physics.SphereCastNonAlloc(
            transform.position,
            0.08f,
            delta.normalized,
            hits,
            delta.magnitude,
            ~0,
            QueryTriggerInteraction.Ignore);
        RaycastHit? nearest = null;
        for (int index = 0; index < count; index++)
        {
            RaycastHit hit = hits[index];
            if (hit.collider == null || (owner != null && hit.collider.transform.IsChildOf(owner)))
                continue;
            if (!nearest.HasValue || hit.distance < nearest.Value.distance)
                nearest = hit;
        }
        if (nearest.HasValue)
        {
            RaycastHit hit = nearest.Value;
            ISpaceDamageable damageable = FindDamageable(hit.collider.transform);
            Vector3 impulse = velocity.normalized * 120f;
            damageable?.ApplyDamage(new SpaceDamageInfo(
                35f,
                hit.point,
                impulse,
                SpaceDamageType.Projectile,
                gameObject));
            if (damageable == null && hit.rigidbody != null)
                UnityPlanet.ModularAssembly.VehicleExternalForces.ApplyImpulse(
                    hit.rigidbody,
                    impulse,
                    hit.point);
            transform.position = hit.point;
            Release();
            return;
        }
        previousPosition = transform.position;
        transform.position = next;
    }

    static ISpaceDamageable FindDamageable(Transform start)
    {
        for (Transform current = start; current != null; current = current.parent)
        {
            var found = current.GetComponent(typeof(ISpaceDamageable)) as ISpaceDamageable;
            if (found != null)
                return found;
        }
        return null;
    }

    public void Release() => release?.Invoke(this);
}

public sealed class GridModuleDamageReceiver : MonoBehaviour, ISpaceDamageable
{
    GridTargetController owner;
    string runtimeId;

    public float Integrity => owner == null ? 0f : owner.GetIntegrity(runtimeId);
    public float MaximumIntegrity => owner == null ? 0f : owner.GetMaximumIntegrity(runtimeId);
    public bool IsDestroyed => owner == null || owner.IsModuleDestroyed(runtimeId);

    public void Initialize(GridTargetController controller, string id)
    {
        owner = controller;
        runtimeId = id;
    }

    public void ApplyDamage(SpaceDamageInfo damage) => owner?.ApplyDamage(runtimeId, damage);
}

public sealed class GridTargetController : MonoBehaviour
{
    const int MaxDebris = 24;
    GridAssemblyModel model;
    GridAssemblyPresenter presenter;
    readonly Dictionary<string, float> health = new Dictionary<string, float>();
    readonly Queue<GameObject> debris = new Queue<GameObject>();
    GridModuleDefinition[] definitions;
    bool destroyed;

    public void Initialize(GridModuleDefinition[] sourceDefinitions)
    {
        definitions = sourceDefinitions;
        BuildTarget();
    }

    public void ResetTarget()
    {
        while (debris.Count > 0)
            if (debris.Dequeue() is GameObject item && item != null)
                Destroy(item);
        if (presenter != null)
            Destroy(presenter.gameObject);
        destroyed = false;
        BuildTarget();
    }

    void BuildTarget()
    {
        model = new GridAssemblyModel(definitions);
        GridModuleDefinition armor = definitions.First(item => item.Category == GridModuleCategory.Armor);
        GridModuleDefinition structure = definitions.First(item => item.Category == GridModuleCategory.Structure);
        Vector3Int[] front =
        {
            new Vector3Int(-1,-1,-2), new Vector3Int(0,-1,-2),
            new Vector3Int(-1,0,-2), new Vector3Int(0,0,-2),
            new Vector3Int(-2,-1,-2), new Vector3Int(1,-1,-2),
            new Vector3Int(-2,0,-2), new Vector3Int(1,0,-2),
            new Vector3Int(-1,-2,-2), new Vector3Int(0,-2,-2),
            new Vector3Int(-1,1,-2), new Vector3Int(0,1,-2),
            new Vector3Int(-2,-2,-2), new Vector3Int(1,-2,-2),
            new Vector3Int(-2,1,-2), new Vector3Int(1,1,-2)
        };
        foreach (Vector3Int cell in front)
            model.TryPlace(armor.ModuleId, new GridModulePose(cell, 0), false, out _, out _);
        Vector3Int[] rear =
        {
            new Vector3Int(-1,-1,1), new Vector3Int(0,-1,1),
            new Vector3Int(-1,0,1), new Vector3Int(0,0,1),
            new Vector3Int(-2,-1,0), new Vector3Int(1,-1,0), new Vector3Int(0,1,0)
        };
        foreach (Vector3Int cell in rear)
            model.TryPlace(structure.ModuleId, new GridModulePose(cell, 0), false, out _, out _);

        GameObject visual = new GameObject("TargetAssembly");
        visual.transform.SetParent(transform, false);
        var body = visual.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        Transform parts = new GameObject("Parts").transform;
        parts.SetParent(visual.transform, false);
        Transform core = new GameObject("Core").transform;
        core.SetParent(visual.transform, false);
        var assembly = visual.AddComponent<ShipAssembly>();
        assembly.Configure(body, parts, null, 1000f);
        presenter = visual.AddComponent<GridAssemblyPresenter>();
        presenter.Initialize(model, assembly, core);
        presenter.Rebuilt += BindReceivers;
        health.Clear();
        foreach (GridModuleRecord record in model.Records)
            health[record.RuntimeId] = record.Definition.MaxIntegrity;
        BindReceivers();
    }

    void BindReceivers()
    {
        if (presenter == null)
            return;
        foreach (KeyValuePair<string, GridModuleView> pair in presenter.Views)
        {
            GridModuleDamageReceiver receiver =
                pair.Value.GetComponent<GridModuleDamageReceiver>()
                ?? pair.Value.gameObject.AddComponent<GridModuleDamageReceiver>();
            receiver.Initialize(this, pair.Key);
            if (!health.ContainsKey(pair.Key))
                health[pair.Key] = pair.Value.Record.Definition.MaxIntegrity;
        }
    }

    public void ApplyDamage(string runtimeId, SpaceDamageInfo damage)
    {
        if (destroyed || !health.TryGetValue(runtimeId, out float value))
            return;
        value -= Mathf.Max(0f, damage.amount);
        health[runtimeId] = value;
        if (value > 0f)
            return;
        GridModuleRecord record = model.Find(runtimeId);
        if (record == null)
            return;
        if (record.Definition.Category == GridModuleCategory.Core)
        {
            PlayDestructionEffect(runtimeId, record, damage, true);
            DestroyTarget(damage);
            return;
        }
        PlayDestructionEffect(runtimeId, record, damage, false);
        model.TryRemove(runtimeId, out _);
        health.Remove(runtimeId);
        List<List<GridModuleRecord>> disconnected = model.GetDisconnectedComponents();
        foreach (List<GridModuleRecord> component in disconnected)
        {
            SpawnDebris(component, damage.impulse);
            model.RemoveIds(component.Select(item => item.RuntimeId));
            foreach (GridModuleRecord item in component)
                health.Remove(item.RuntimeId);
        }
    }

    void PlayDestructionEffect(
        string runtimeId,
        GridModuleRecord record,
        SpaceDamageInfo damage,
        bool isCore)
    {
        Bounds bounds = new Bounds(damage.point, Vector3.one);
        if (presenter != null &&
            presenter.Views.TryGetValue(
                runtimeId,
                out GridModuleView view) &&
            view != null)
        {
            Renderer[] renderers =
                view.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
            {
                bounds = renderers[0].bounds;
                for (int index = 1; index < renderers.Length; index++)
                    bounds.Encapsulate(renderers[index].bounds);
            }
            else
            {
                bounds = new Bounds(
                    view.transform.position,
                    Vector3.one);
            }
        }
        Vector3 point = bounds.SqrDistance(damage.point) <= 1f
            ? damage.point
            : bounds.center;
        Vector3 normal = damage.impulse.sqrMagnitude > 0.0001f
            ? -damage.impulse.normalized
            : Vector3.up;
        UnityPlanet.ModularAssembly.CombatFeedbackController
            .GetOrCreate()
            .PlayDestruction(
            new UnityPlanet.ModularAssembly.ModuleDestructionFeedbackContext(
                runtimeId,
                point,
                normal,
                record.Definition.Category,
                isCore,
                bounds,
                0));
    }

    public float GetIntegrity(string runtimeId) =>
        health.TryGetValue(runtimeId, out float value) ? Mathf.Max(0f, value) : 0f;

    public float GetMaximumIntegrity(string runtimeId)
    {
        GridModuleRecord record = model == null ? null : model.Find(runtimeId);
        return record == null ? 0f : record.Definition.MaxIntegrity;
    }

    public bool IsModuleDestroyed(string runtimeId) =>
        destroyed || !health.ContainsKey(runtimeId) || GetIntegrity(runtimeId) <= 0f;

    void DestroyTarget(SpaceDamageInfo damage)
    {
        destroyed = true;
        List<GridModuleRecord> remaining = model.Records
            .Where(item => item.Definition.Category != GridModuleCategory.Core)
            .Select(item => item.Clone())
            .ToList();
        if (remaining.Count > 0)
            SpawnDebris(remaining, damage.impulse);
        presenter.ClearVisuals();
    }

    void SpawnDebris(List<GridModuleRecord> records, Vector3 impulse)
    {
        if (records == null || records.Count == 0)
            return;
        var root = new GameObject("DetachedModuleCluster");
        root.transform.SetPositionAndRotation(transform.position, transform.rotation);
        float totalMass = 0f;
        foreach (GridModuleRecord record in records)
        {
            GameObject instance = Instantiate(record.Definition.Prefab, root.transform);
            instance.transform.localPosition = GridAssemblyModel.ModuleCenter(record);
            instance.transform.localRotation = GridOrientation.Rotation(record.Pose.orientation);
            foreach (MonoBehaviour behaviour in instance.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour is GridModuleDamageReceiver || behaviour is ThrusterPart)
                    behaviour.enabled = false;
            totalMass += record.Definition.MassKg;
        }
        var body = root.AddComponent<Rigidbody>();
        body.useGravity = false;
        body.mass = Mathf.Max(1f, totalMass);
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        body.AddForce(impulse, ForceMode.Impulse);
        debris.Enqueue(root);
        while (debris.Count > MaxDebris)
            if (debris.Dequeue() is GameObject oldest && oldest != null)
                Destroy(oldest);
    }
}

public sealed class ModularAssemblyLabController : MonoBehaviour
{
    GridAssemblyModel model;
    GridAssemblyPresenter presenter;
    GridAssemblyHistory history;
    ModularBlueprintStore store;
    ModularBlueprintStore presetStore;
    ModularPresetLibrary presetLibrary;
    GridFlightBridge flight;
    GridLabCameraController cameraController;
    GridTargetController target;
    Camera sceneCamera;
    Transform shipRoot;
    ModularAssemblyLabUI ui;
    GridModuleDefinition activeDefinition;
    string selectedRuntimeId;
    string movingRuntimeId;
    int placementRoll;
    GridModulePose previewPose;
    bool previewValid;
    string previewError;
    readonly List<GameObject> previewCells = new List<GameObject>();
    Material previewMaterial;
    bool mirrorEnabled = true;

    public GridAssemblyModel Model => model;
    public GridAssemblyPresenter Presenter => presenter;
    public GridAssemblyHistory History => history;
    public bool MirrorEnabled => mirrorEnabled;
    public bool IsFlying =>
        flight != null && flight.State != GridFlightState.Build;
    public GridFlightState FlightState =>
        flight == null ? GridFlightState.Build : flight.State;
    public string SelectedRuntimeId => selectedRuntimeId;

    public void Initialize(
        GridAssemblyModel assemblyModel,
        GridAssemblyPresenter assemblyPresenter,
        GridFlightBridge flightBridge,
        GridLabCameraController cameraRig,
        GridTargetController targetController,
        Camera camera,
        Transform root)
    {
        model = assemblyModel;
        presenter = assemblyPresenter;
        flight = flightBridge;
        flight.StateChanged += HandleFlightStateChanged;
        cameraController = cameraRig;
        target = targetController;
        sceneCamera = camera;
        shipRoot = root;
        history = new GridAssemblyHistory();
        store = new ModularBlueprintStore();
        presetStore = new ModularBlueprintStore("modular_preset.json");
        presetLibrary = new ModularPresetLibrary();
        previewMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
        model.Changed += HandleModelChanged;
        Status("正在载入模块目录与蓝图……");
        history.Clear();
    }

    public void SetUI(ModularAssemblyLabUI value)
    {
        ui = value;
        RefreshUI();
    }

    public void SelectDefinition(GridModuleDefinition definition)
    {
        if (IsFlying)
            return;
        activeDefinition = definition;
        movingRuntimeId = string.Empty;
        placementRoll = 0;
        selectedRuntimeId = string.Empty;
        HidePreview();
        Status(definition == null ? "已取消放置。" : "已选择：" + definition.DisplayName);
        RefreshUI();
    }

    public void ToggleMirror()
    {
        mirrorEnabled = !mirrorEnabled;
        RefreshUI();
    }

    public void BeginMoveSelected()
    {
        GridModuleRecord record = model.Find(selectedRuntimeId);
        if (record == null || record.Definition.Category == GridModuleCategory.Core)
        {
            Status("请选择可移动模块。");
            return;
        }
        activeDefinition = record.Definition;
        movingRuntimeId = record.RuntimeId;
        placementRoll = 0;
        Status("正在重新放置：" + record.Definition.DisplayName);
    }

    public void DeleteSelected()
    {
        if (string.IsNullOrEmpty(selectedRuntimeId))
            return;
        ModularBlueprintData before = model.CaptureBlueprint();
        if (model.TryRemove(selectedRuntimeId, out string error))
        {
            history.Record(before);
            selectedRuntimeId = string.Empty;
            Status("模块已删除。");
        }
        else Status(error);
    }

    public void UnlinkSelected()
    {
        ModularBlueprintData before = model.CaptureBlueprint();
        if (model.TryUnlinkMirror(selectedRuntimeId))
        {
            history.Record(before);
            Status("镜像配对已解除。");
        }
        else Status("所选模块没有镜像配对。");
    }

    public void SetSelectedBehaviorSettings(string settings)
    {
        if (string.IsNullOrEmpty(selectedRuntimeId))
            return;
        ModularBlueprintData before = model.CaptureBlueprint();
        if (model.TrySetBehaviorSettings(
                selectedRuntimeId,
                settings,
                out string error))
        {
            history.Record(before);
            Status("模块功能设置已更新。");
        }
        else
            Status(error);
    }

    public void Undo()
    {
        if (history == null || model == null)
            return;

        if (history.Undo(model))
            Status("已撤销。");
    }

    public void Redo()
    {
        if (history == null || model == null)
            return;

        if (history.Redo(model))
            Status("已重做。");
    }

    public void FinalizePostCombatBuild(
        ModularBlueprintData beforeCombat)
    {
        activeDefinition = null;
        movingRuntimeId = string.Empty;
        selectedRuntimeId = string.Empty;
        previewValid = false;
        previewError = string.Empty;
        HidePreview();

        HashSet<string> currentIds = model.Records
            .Select(item => item.RuntimeId)
            .ToHashSet(StringComparer.Ordinal);
        bool hasCombatLoss = beforeCombat?.modules != null &&
            beforeCombat.modules.Any(item =>
                item != null &&
                item.runtimeId != GridAssemblyModel.CoreRuntimeId &&
                !currentIds.Contains(item.runtimeId));
        if (hasCombatLoss)
        {
            history.Record(beforeCombat);
            mirrorEnabled = false;
            Status(
                "战损已同步：损坏占格已释放，镜像已关闭，可单侧修补。Ctrl+Z 撤销，R 重做。");
        }
        else
        {
            Status("已返回改装室。");
        }
        RefreshUI();
    }

    public void Save()
    {
        TrySaveCanonical(out _);
    }

    public bool TrySaveCanonical(out string message)
    {
        GridAssemblyValidation validation = model.Validate();
        if (!validation.IsValid)
        {
            message = "无法保存：" + validation.Message;
            Status(message);
            return false;
        }
        try
        {
            store.Save(model.CaptureBlueprint());
            message = "已保存 " + DateTime.Now.ToString("HH:mm:ss");
            Status(message);
            return true;
        }
        catch (Exception exception)
        {
            message = "保存失败：" + exception.Message;
            Status(message);
            return false;
        }
    }

    public void Load()
    {
        LoadCanonicalForBuild(out _);
    }

    public bool LoadCanonicalForBuild(out string message)
    {
        if (!store.TryLoad(out ModularBlueprintData blueprint, out string error))
        {
            message = string.IsNullOrEmpty(error)
                ? "没有已保存的模块蓝图，已创建空白设计。"
                : error;
            Status(message);
            return string.IsNullOrEmpty(error);
        }
        if (!model.RestoreBlueprint(blueprint, out error))
        {
            message = "载入失败：" + error;
            Status(message);
            return false;
        }
        history.Clear();
        selectedRuntimeId = string.Empty;
        message = "已自动载入模块蓝图。";
        Status(message);
        return true;
    }

    public IReadOnlyList<ModularPresetEntry> ListNamedPresets()
    {
        return presetLibrary.ListPresets(model);
    }

    public bool SaveNamedPreset(
        string name,
        bool overwrite,
        out string message)
    {
        GridAssemblyValidation validation = model.Validate();
        if (!validation.IsValid)
        {
            message = "无法保存预制：" + validation.Message;
            Status(message);
            return false;
        }
        if (!presetLibrary.SaveUserPreset(
                name,
                model,
                model.CaptureBlueprint(),
                overwrite,
                out _,
                out message))
        {
            Status(message);
            return false;
        }
        message = "预制“" + name.Trim() + "”已保存。";
        Status(message);
        return true;
    }

    public bool LoadNamedPresetForBuild(
        string presetId,
        out string message)
    {
        if (flight.State != GridFlightState.Build)
        {
            message = "请先返回改装模式。";
            Status(message);
            return false;
        }
        if (!presetLibrary.TryLoadPreset(
                presetId,
                model,
                out ModularBlueprintData blueprint,
                out message))
        {
            Status(message);
            return false;
        }

        ModularBlueprintData before = model.CaptureBlueprint();
        if (!model.RestoreBlueprint(blueprint, out string restoreError))
        {
            model.RestoreBlueprint(before, out _);
            message = "载入预制失败：" + restoreError;
            Status(message);
            return false;
        }
        GridAssemblyValidation validation = model.Validate();
        if (!validation.IsValid)
        {
            model.RestoreBlueprint(before, out _);
            message = "载入预制失败：" + validation.Message;
            Status(message);
            return false;
        }

        history.Record(before);
        selectedRuntimeId = string.Empty;
        activeDefinition = null;
        message = "预制已载入到改装界面。";
        Status(message);
        return true;
    }

    public bool DeleteNamedPreset(
        string presetId,
        out string message)
    {
        if (!presetLibrary.DeleteUserPreset(
                presetId,
                model,
                out message))
        {
            Status(message);
            return false;
        }
        message = "预制已删除。";
        Status(message);
        return true;
    }

    public bool SavePreset(out string message)
    {
        GridAssemblyValidation validation = model.Validate();
        if (!validation.IsValid)
        {
            message = "无法保存预制：" + validation.Message;
            Status(message);
            return false;
        }

        try
        {
            presetStore.Save(model.CaptureBlueprint());
            message = "预制已保存，可随时直接试飞。";
            Status(message);
            return true;
        }
        catch (Exception exception)
        {
            message = "保存预制失败：" + exception.Message;
            Status(message);
            return false;
        }
    }

    public bool LoadPresetForBuild(out string message)
    {
        if (flight.State != GridFlightState.Build)
        {
            message = "请先返回改装模式。";
            Status(message);
            return false;
        }

        if (!presetStore.TryLoad(out ModularBlueprintData blueprint, out string error))
        {
            message = string.IsNullOrEmpty(error)
                ? "还没有保存载具预制。"
                : "载入预制失败：" + error;
            Status(message);
            return false;
        }

        if (!model.RestoreBlueprint(blueprint, out error))
        {
            message = "载入预制失败：" + error;
            Status(message);
            return false;
        }

        history.Clear();
        selectedRuntimeId = string.Empty;
        activeDefinition = null;
        movingRuntimeId = string.Empty;
        HidePreview();

        message = "预制已载入，可选择开始试飞或战斗测试。";
        Status(message);
        RefreshUI();
        return true;
    }

    public void NewBlueprint()
    {
        model.InitializeCore();
        history.Clear();
        selectedRuntimeId = string.Empty;
        activeDefinition = null;
        movingRuntimeId = string.Empty;
        Status("已新建空白蓝图，磁盘存档尚未覆盖。");
    }

    public void ToggleFlight()
    {
        if (flight.State != GridFlightState.Build)
        {
            flight.ExitFlight();
            RefreshUI();
            return;
        }
        GridAssemblyValidation validation = model.Validate();
        if (!validation.IsValid)
        {
            Status("无法试驾：" + validation.Message);
            return;
        }
        activeDefinition = null;
        movingRuntimeId = string.Empty;
        HidePreview();
        Status("正在生成PlanetLab无限试飞地形……");
        flight.EnterFlight();
        RefreshUI();
    }

    void HandleFlightStateChanged(
        GridFlightState state,
        string message)
    {
        Status(message);
        RefreshUI();
    }

    public void ResetTarget()
    {
        target?.ResetTarget();
        Status("靶船已重置。");
    }

    void Update()
    {
        if (flight.State == GridFlightState.Flight &&
            UnityPlanet.ModularAssembly.
                ArcadeFlightRuntimeTuningOverlay.IsInputCaptured)
        {
            return;
        }
        if (Input.GetKeyDown(KeyCode.F5))
            ToggleFlight();
        if (flight.State == GridFlightState.Flight)
        {
            if (Input.GetKeyDown(KeyCode.R))
                flight.ResetFlight();
            if (Input.GetKeyDown(KeyCode.T))
                ResetTarget();
            return;
        }

        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        if (ctrl && Input.GetKeyDown(KeyCode.Z)) Undo();
        if (ctrl && Input.GetKeyDown(KeyCode.Y)) Redo();
        if (Input.GetKeyDown(KeyCode.Delete)) DeleteSelected();
        if (Input.GetKeyDown(KeyCode.G)) BeginMoveSelected();
        if (Input.GetKeyDown(KeyCode.U)) UnlinkSelected();
        if (Input.GetKeyDown(KeyCode.R) && activeDefinition != null)
            placementRoll = (placementRoll + 1) & 3;
        if (Input.GetMouseButtonDown(1) && activeDefinition != null)
        {
            activeDefinition = null;
            movingRuntimeId = string.Empty;
            HidePreview();
            Status("已取消放置。");
        }

        bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        if (activeDefinition != null && !overUi)
            UpdatePreview();
        else
            HidePreview();
        if (Input.GetMouseButtonDown(0) && !overUi)
        {
            if (activeDefinition != null)
                CommitPreview();
            else
                SelectAtPointer();
        }
    }

    void UpdatePreview()
    {
        Ray ray = sceneCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, 300f, ~0, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        RaycastHit? selectedHit = null;
        foreach (RaycastHit hit in hits)
        {
            GridModuleView view = hit.collider.GetComponentInParent<GridModuleView>();
            if (view == null || !hit.collider.transform.IsChildOf(shipRoot))
                continue;
            selectedHit = hit;
            break;
        }
        if (!selectedHit.HasValue)
        {
            HidePreview();
            previewValid = false;
            return;
        }

        RaycastHit surface = selectedHit.Value;
        Vector3 localNormalValue = shipRoot.InverseTransformDirection(surface.normal);
        Vector3Int normal = Cardinal(localNormalValue);
        Vector3 localPoint = shipRoot.InverseTransformPoint(surface.point) + (Vector3)normal * 0.01f;
        Vector3Int targetCell = new Vector3Int(
            Mathf.FloorToInt(localPoint.x),
            Mathf.FloorToInt(localPoint.y),
            Mathf.FloorToInt(localPoint.z));
        int orientation = GridOrientation.FromOutwardNormal(normal, placementRoll);
        List<Vector3Int> normalized = GridOrientation.NormalizedCells(activeDefinition.Footprint, orientation);
        int minimumProjection = normalized.Min(cell => Mathf.RoundToInt(Vector3.Dot(cell, normal)));
        Vector3Int attachment = normalized.First(
            cell => Mathf.RoundToInt(Vector3.Dot(cell, normal)) == minimumProjection);
        previewPose = new GridModulePose(targetCell - attachment, orientation);
        bool mirror = string.IsNullOrEmpty(movingRuntimeId)
            ? mirrorEnabled
            : !string.IsNullOrEmpty(model.Find(movingRuntimeId)?.Pose.mirrorGroupId);
        previewValid = model.CanPlacePose(activeDefinition, previewPose, mirror, movingRuntimeId, out previewError);
        ShowPreview(activeDefinition, previewPose, mirror, previewValid);
    }

    void CommitPreview()
    {
        if (!previewValid)
        {
            Status(previewError);
            return;
        }
        ModularBlueprintData before = model.CaptureBlueprint();
        bool success;
        string error;
        if (!string.IsNullOrEmpty(movingRuntimeId))
        {
            success = model.TryMove(movingRuntimeId, previewPose, out error);
            if (success)
            {
                selectedRuntimeId = movingRuntimeId;
                movingRuntimeId = string.Empty;
                activeDefinition = null;
            }
        }
        else
        {
            success = model.TryPlace(
                activeDefinition.ModuleId,
                previewPose,
                mirrorEnabled,
                out selectedRuntimeId,
                out error);
        }
        if (success)
        {
            history.Record(before);
            Status("模块已放置。");
        }
        else Status(error);
    }

    void SelectAtPointer()
    {
        if (!Physics.Raycast(sceneCamera.ScreenPointToRay(Input.mousePosition), out RaycastHit hit, 300f))
        {
            selectedRuntimeId = string.Empty;
            RefreshUI();
            return;
        }
        GridModuleView view = hit.collider.GetComponentInParent<GridModuleView>();
        selectedRuntimeId = view != null && hit.collider.transform.IsChildOf(shipRoot)
            ? view.Record.RuntimeId
            : string.Empty;
        RefreshUI();
    }

    void ShowPreview(GridModuleDefinition definition, GridModulePose pose, bool mirrored, bool valid)
    {
        HidePreview();
        var poses = new List<GridModulePose> { pose };
        if (mirrored)
        {
            GridModulePose mirror = model.MirrorPose(definition, pose, "preview");
            var first = new HashSet<Vector3Int>(GridAssemblyModel.GetCells(definition, pose));
            if (!first.SetEquals(GridAssemblyModel.GetCells(definition, mirror)))
                poses.Add(mirror);
        }
        previewMaterial.color = valid
            ? new Color(0.12f, 1f, 0.42f, 0.55f)
            : new Color(1f, 0.12f, 0.08f, 0.55f);
        foreach (GridModulePose item in poses)
        foreach (Vector3Int cell in GridAssemblyModel.GetCells(definition, item))
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "PlacementPreviewCell";
            cube.layer = 2;
            cube.transform.SetParent(shipRoot, false);
            cube.transform.localPosition = (Vector3)cell + Vector3.one * 0.5f;
            cube.transform.localScale = Vector3.one * 0.98f;
            cube.GetComponent<Renderer>().sharedMaterial = previewMaterial;
            Destroy(cube.GetComponent<Collider>());
            previewCells.Add(cube);
        }
    }

    void HidePreview()
    {
        foreach (GameObject item in previewCells)
            if (item != null)
                Destroy(item);
        previewCells.Clear();
    }

    static Vector3Int Cardinal(Vector3 value)
    {
        value.Normalize();
        float ax = Mathf.Abs(value.x);
        float ay = Mathf.Abs(value.y);
        float az = Mathf.Abs(value.z);
        if (ax >= ay && ax >= az) return value.x >= 0f ? Vector3Int.right : Vector3Int.left;
        if (ay >= ax && ay >= az) return value.y >= 0f ? Vector3Int.up : Vector3Int.down;
        return value.z >= 0f ? new Vector3Int(0, 0, 1) : new Vector3Int(0, 0, -1);
    }

    void HandleModelChanged()
    {
        if (!string.IsNullOrEmpty(selectedRuntimeId) && model.Find(selectedRuntimeId) == null)
            selectedRuntimeId = string.Empty;
        RefreshUI();
    }

    void Status(string message)
    {
        if (ui != null)
            ui.SetStatus(message);
        else if (!string.IsNullOrEmpty(message))
            Debug.Log(message, this);
    }

    void RefreshUI() => ui?.Refresh();
}

public sealed class ModularAssemblyLabUI : MonoBehaviour
{
    ModularAssemblyLabController controller;
    Text statsText;
    Text selectionText;
    Text statusText;
    Text mirrorText;
    Text modeText;
    Button moveButton;
    Button deleteButton;
    Button unlinkButton;
    GameObject confirmation;
    Font font;

    public void Initialize(ModularAssemblyLabController owner, GridModuleDefinition[] definitions)
    {
        controller = owner;
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Build(definitions);
        Refresh();
    }

    public void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message ?? string.Empty;
    }

    public void Refresh()
    {
        if (controller == null ||
            controller.Model == null ||
            statsText == null ||
            selectionText == null ||
            mirrorText == null ||
            modeText == null ||
            moveButton == null ||
            deleteButton == null ||
            unlinkButton == null)
            return;
        GridAssemblyMetrics metrics = controller.Model.CalculateMetrics();
        GridAssemblyValidation validation = controller.Model.Validate();
        statsText.text =
            $"模块  {metrics.moduleCount}/{GridAssemblyModel.ModuleLimit}\n"
            + $"质量  {metrics.totalMass:0} kg\n"
            + $"能源  {metrics.energyCost:0}/{metrics.energyCapacity:0}\n"
            + $"总推力  {metrics.totalThrust:0} N\n\n"
            + $"正向控制  X {metrics.positiveAuthority.x:0}  Y {metrics.positiveAuthority.y:0}  Z {metrics.positiveAuthority.z:0}\n"
            + $"反向控制  X {metrics.negativeAuthority.x:0}  Y {metrics.negativeAuthority.y:0}  Z {metrics.negativeAuthority.z:0}\n"
            + $"预计加速度  {metrics.estimatedAcceleration.x:0.0}, {metrics.estimatedAcceleration.y:0.0}, {metrics.estimatedAcceleration.z:0.0} m/s²\n\n"
            + (validation.IsValid ? "<color=#65F29A>设计有效</color>" : "<color=#FF665C>" + validation.Message + "</color>");
        GridModuleRecord selected = controller.Model.Find(controller.SelectedRuntimeId);
        selectionText.text = selected == null
            ? "未选择模块\n\n点击模块进行选择\nG 拾取移动\nDelete 删除\nU 解除镜像"
            : $"{selected.Definition.DisplayName}\n"
              + $"质量 {selected.Definition.MassKg:0} kg\n"
              + $"耐久 {selected.Definition.MaxIntegrity:0}\n"
              + $"占格 {selected.Definition.Footprint.x}×{selected.Definition.Footprint.y}×{selected.Definition.Footprint.z}";
        mirrorText.text = controller.MirrorEnabled ? "镜像：开" : "镜像：关";
        modeText.text = controller.IsFlying ? "返回建造  F5" : "开始试驾  F5";
        bool editable = selected != null && selected.Definition.Category != GridModuleCategory.Core;
        moveButton.interactable = editable;
        deleteButton.interactable = editable;
        unlinkButton.interactable = editable && !string.IsNullOrEmpty(selected.Pose.mirrorGroupId);
    }

    void Build(GridModuleDefinition[] definitions)
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        gameObject.AddComponent<GraphicRaycaster>();

        RectTransform top = Panel("TopBar", transform, new Color(0.025f, 0.05f, 0.075f, 0.96f));
        Anchor(top, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -74f), Vector2.zero);
        var topLayout = top.gameObject.AddComponent<HorizontalLayoutGroup>();
        topLayout.padding = new RectOffset(18, 18, 12, 12);
        topLayout.spacing = 8f;
        topLayout.childForceExpandWidth = false;
        topLayout.childControlWidth = false;
        AddLabel(top, "模块拼装实验场", 24, 260f);
        AddButton(top, "新建", 92f, () => confirmation.SetActive(true));
        AddButton(top, "载入", 92f, controller.Load);
        AddButton(top, "保存", 92f, controller.Save);
        AddButton(top, "撤销", 92f, controller.Undo);
        AddButton(top, "重做", 92f, controller.Redo);
        Button mirror = AddButton(top, string.Empty, 120f, controller.ToggleMirror);
        mirrorText = mirror.GetComponentInChildren<Text>();
        Button mode = AddButton(top, string.Empty, 150f, controller.ToggleFlight);
        modeText = mode.GetComponentInChildren<Text>();
        AddButton(top, "重置靶船 T", 140f, controller.ResetTarget);

        RectTransform left = Panel("ModuleLibrary", transform, new Color(0.025f, 0.05f, 0.075f, 0.94f));
        Anchor(left, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(18f, 18f), new Vector2(286f, -92f));
        var leftLayout = left.gameObject.AddComponent<VerticalLayoutGroup>();
        leftLayout.padding = new RectOffset(14, 14, 16, 16);
        leftLayout.spacing = 9f;
        leftLayout.childForceExpandHeight = false;
        AddLabel(left, "模块库", 26, 48f);
        foreach (GridModuleDefinition definition in definitions.Where(item => item.Category != GridModuleCategory.Core))
        {
            GridModuleDefinition captured = definition;
            AddButton(left, definition.DisplayName, 240f, () => controller.SelectDefinition(captured), 48f);
        }
        AddLabel(left, "\n放置：左键\n旋转：R\n取消：右键\n移动：G\n删除：Delete\n解除镜像：U", 17, 190f);

        RectTransform right = Panel("Inspector", transform, new Color(0.025f, 0.05f, 0.075f, 0.94f));
        Anchor(right, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-350f, 18f), new Vector2(-18f, -92f));
        var rightLayout = right.gameObject.AddComponent<VerticalLayoutGroup>();
        rightLayout.padding = new RectOffset(16, 16, 16, 16);
        rightLayout.spacing = 10f;
        rightLayout.childForceExpandHeight = false;
        AddLabel(right, "工程统计", 25, 44f);
        statsText = AddLabel(right, string.Empty, 18, 310f);
        AddLabel(right, "选中模块", 23, 42f);
        selectionText = AddLabel(right, string.Empty, 18, 150f);
        moveButton = AddButton(right, "拾取移动  G", 290f, controller.BeginMoveSelected, 44f);
        deleteButton = AddButton(right, "删除模块  Delete", 290f, controller.DeleteSelected, 44f);
        unlinkButton = AddButton(right, "解除镜像配对  U", 290f, controller.UnlinkSelected, 44f);

        RectTransform bottom = Panel("Status", transform, new Color(0.01f, 0.025f, 0.04f, 0.92f));
        Anchor(bottom, new Vector2(0.16f, 0f), new Vector2(0.82f, 0f), new Vector2(0f, 18f), new Vector2(0f, 64f));
        statusText = AddLabel(bottom, "准备就绪。", 18, 44f);
        statusText.alignment = TextAnchor.MiddleCenter;

        confirmation = Panel("NewConfirmation", transform, new Color(0.02f, 0.04f, 0.06f, 0.98f)).gameObject;
        RectTransform confirmRect = confirmation.GetComponent<RectTransform>();
        Anchor(confirmRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-230f, -100f), new Vector2(230f, 100f));
        var confirmLayout = confirmation.AddComponent<VerticalLayoutGroup>();
        confirmLayout.padding = new RectOffset(20, 20, 18, 18);
        confirmLayout.spacing = 12f;
        confirmLayout.childForceExpandHeight = false;
        AddLabel(confirmRect, "清空当前未保存设计并恢复固定核心？", 21, 58f);
        AddButton(confirmRect, "确认新建", 420f, () => { confirmation.SetActive(false); controller.NewBlueprint(); }, 44f);
        AddButton(confirmRect, "取消", 420f, () => confirmation.SetActive(false), 44f);
        confirmation.SetActive(false);
    }

    RectTransform Panel(string name, Transform parent, Color color)
    {
        GameObject target = new GameObject(name, typeof(RectTransform), typeof(Image));
        target.transform.SetParent(parent, false);
        target.GetComponent<Image>().color = color;
        return target.GetComponent<RectTransform>();
    }

    Text AddLabel(Transform parent, string value, int size, float height)
    {
        GameObject target = new GameObject("Label", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
        target.transform.SetParent(parent, false);
        var text = target.GetComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.color = new Color(0.88f, 0.94f, 1f);
        text.text = value;
        text.alignment = TextAnchor.UpperLeft;
        text.supportRichText = true;
        target.GetComponent<LayoutElement>().preferredHeight = height;
        return text;
    }

    Button AddButton(Transform parent, string label, float width, UnityEngine.Events.UnityAction action, float height = 48f)
    {
        GameObject target = new GameObject("Button_" + label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        target.transform.SetParent(parent, false);
        target.GetComponent<Image>().color = new Color(0.08f, 0.22f, 0.30f, 0.98f);
        Button button = target.GetComponent<Button>();
        button.onClick.AddListener(action);
        LayoutElement layout = target.GetComponent<LayoutElement>();
        layout.preferredWidth = width;
        layout.preferredHeight = height;
        Text text = AddLabel(target.transform, label, 18, height);
        text.alignment = TextAnchor.MiddleCenter;
        RectTransform textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        return button;
    }

    static void Anchor(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }
}
