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
        if (CanAppend(records))
        {
            foreach (GridModuleRecord record in records)
            {
                if (views.TryGetValue(record.RuntimeId, out GridModuleView existing) &&
                    existing != null)
                {
                    existing.Initialize(record);
                    continue;
                }
                CreateView(record);
            }
            ApplyValidationTint(validation);
            assembly?.Recalculate();
            Rebuilt?.Invoke();
            return;
        }

        RebuildAll(records, validation);
    }

    bool CanAppend(IReadOnlyList<GridModuleRecord> records)
    {
        if (views.Count == 0 || records.Count < views.Count)
            return false;
        var current = records.ToDictionary(record => record.RuntimeId);
        foreach (KeyValuePair<string, GridModuleView> pair in views)
        {
            GridModuleView view = pair.Value;
            if (view == null ||
                view.Record == null ||
                !current.TryGetValue(pair.Key, out GridModuleRecord record) ||
                !string.Equals(
                    view.Record.Definition.ModuleId,
                    record.Definition.ModuleId,
                    StringComparison.Ordinal) ||
                view.Record.Pose.Origin != record.Pose.Origin ||
                view.Record.Pose.orientation != record.Pose.orientation ||
                !string.Equals(
                    view.Record.Pose.mirrorGroupId,
                    record.Pose.mirrorGroupId,
                    StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }

    void RebuildAll(
        IReadOnlyList<GridModuleRecord> records,
        GridAssemblyValidation validation)
    {
        foreach (GridModuleView view in views.Values)
            if (view != null)
                view.gameObject.SetActive(false);
        views.Clear();
        if (assembly != null)
            assembly.RestoreStates(Array.Empty<PlacedPartState>());
        if (coreRoot != null)
        {
            for (int index = coreRoot.childCount - 1; index >= 0; index--)
            {
                coreRoot.GetChild(index).gameObject.SetActive(false);
                Destroy(coreRoot.GetChild(index).gameObject);
            }
        }

        foreach (GridModuleRecord record in records)
            CreateView(record);
        ApplyValidationTint(validation);
        assembly?.Recalculate();
        Rebuilt?.Invoke();
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
        foreach (GridModuleView view in views.Values)
            if (view != null)
                Destroy(view.gameObject);
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
    Camera targetCamera;
    Transform target;
    bool flightMode;
    float yaw = 38f;
    float pitch = 24f;
    float distance = 17f;
    Vector3 panOffset;
    float flightAimYaw;
    float flightAimPitch;
    float freeLookYaw;
    float freeLookPitch;
    float flightFocusHeight = 5f;
    float flightLookAhead = 2f;
    Bounds flightLocalBounds;
    bool hasFlightLocalBounds;
    float nextFlightBoundsRefresh;
    readonly RaycastHit[] cameraHits = new RaycastHit[16];

    public Vector3 FlightAimForward =>
        Quaternion.Euler(flightAimPitch, flightAimYaw, 0f) *
        Vector3.forward;

    public void Initialize(Camera camera, Transform focus)
    {
        targetCamera = camera;
        target = focus;
        ApplyImmediate();
    }

    public void SetFlightMode(bool value)
    {
        flightMode = value;
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
            ResolveFlightFraming();
            CaptureFlightBounds();
            nextFlightBoundsRefresh = Time.unscaledTime + 0.5f;
        }
        else
            distance = 17f;
        panOffset = Vector3.zero;
    }

    void LateUpdate()
    {
        if (targetCamera == null || target == null)
            return;
        if (flightMode &&
            Time.unscaledTime >= nextFlightBoundsRefresh)
        {
            CaptureFlightBounds();
            nextFlightBoundsRefresh = Time.unscaledTime + 0.5f;
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
        else if (Input.GetKey(KeyCode.LeftAlt) ||
                 Input.GetKey(KeyCode.RightAlt))
        {
            freeLookYaw += Input.GetAxisRaw("Mouse X") * 3f;
            freeLookPitch = Mathf.Clamp(
                freeLookPitch -
                Input.GetAxisRaw("Mouse Y") * 2.5f,
                -55f,
                70f);
        }
        else if (flightMode)
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
        ApplyImmediate();
    }

    void CaptureFlightBounds()
    {
        hasFlightLocalBounds = false;
        if (target == null)
            return;

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
                if (!hasFlightLocalBounds)
                {
                    flightLocalBounds =
                        new Bounds(localCorner, Vector3.zero);
                    hasFlightLocalBounds = true;
                }
                else
                    flightLocalBounds.Encapsulate(localCorner);
            }
        }

        if (!hasFlightLocalBounds)
        {
            flightLocalBounds =
                new Bounds(Vector3.zero, Vector3.one * 2f);
            hasFlightLocalBounds = true;
            return;
        }

        Vector3 safeMinimum = Vector3.Max(
            flightLocalBounds.min,
            new Vector3(-18f, -18f, -18f));
        Vector3 safeMaximum = Vector3.Min(
            flightLocalBounds.max,
            new Vector3(18f, 18f, 18f));
        if (safeMinimum.x <= safeMaximum.x &&
            safeMinimum.y <= safeMaximum.y &&
            safeMinimum.z <= safeMaximum.z)
        {
            flightLocalBounds.SetMinMax(
                safeMinimum,
                safeMaximum);
        }
        else
            flightLocalBounds =
                new Bounds(Vector3.zero, Vector3.one * 2f);
    }

    Vector3 ResolveFlightFocus(
        Vector3 viewForward,
        Vector3 viewUp)
    {
        if (!hasFlightLocalBounds)
            return target.position +
                   viewUp * flightFocusHeight +
                   viewForward * flightLookAhead;

        Vector3 localCenter = flightLocalBounds.center;
        Vector3 localExtents = flightLocalBounds.extents;
        Vector3 worldCenter = target.TransformPoint(localCenter);
        float topProjection = 0f;
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
            topProjection = Mathf.Max(
                topProjection,
                Vector3.Dot(worldCorner - worldCenter, viewUp));
        }

        float halfViewHeight =
            distance *
            Mathf.Tan(
                targetCamera.fieldOfView *
                0.5f *
                Mathf.Deg2Rad);
        float sightClearance =
            Mathf.Max(1.25f, halfViewHeight * 0.32f);
        topProjection = Mathf.Min(
            topProjection,
            Mathf.Max(2f, halfViewHeight * 0.8f));
        return worldCenter +
               viewUp * (topProjection + sightClearance) +
               viewForward * flightLookAhead;
    }

    void ApplyImmediate()
    {
        if (targetCamera == null || target == null)
            return;
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
        Vector3 viewForward = orbit * Vector3.forward;
        Vector3 viewUp = orbit * Vector3.up;
        Vector3 focus = flightMode
            ? ResolveFlightFocus(viewForward, viewUp)
            : target.position + panOffset;
        Vector3 desired = focus + orbit * new Vector3(0f, 0f, -distance);
        if (flightMode)
            desired = ResolveCameraCollision(focus, desired);
        float blend = Application.isPlaying ? 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime) : 1f;
        targetCamera.transform.position = Vector3.Lerp(targetCamera.transform.position, desired, blend);
        targetCamera.transform.rotation = Quaternion.Slerp(
            targetCamera.transform.rotation,
            Quaternion.LookRotation(
                focus - desired,
                flightMode ? viewUp : target.up),
            blend);
    }

    void ResolveFlightFraming()
    {
        Collider[] colliders = target == null
            ? Array.Empty<Collider>()
            : target.GetComponentsInChildren<Collider>(true);
        bool initialized = false;
        Bounds bounds = new Bounds(
            target == null ? Vector3.zero : target.position,
            Vector3.one);
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
        distance = Mathf.Clamp(bounds.size.magnitude * 1.15f, 10f, 45f);
        flightFocusHeight = Mathf.Clamp(
            distance * 0.32f + bounds.extents.y * 0.25f,
            5f,
            16f);
        flightLookAhead = Mathf.Clamp(bounds.extents.z * 0.35f, 1.5f, 8f);
    }

    Vector3 ResolveCameraCollision(Vector3 focus, Vector3 desired)
    {
        Vector3 offset = desired - focus;
        float length = offset.magnitude;
        if (length <= 0.01f)
            return desired;
        int count = Physics.SphereCastNonAlloc(
            focus,
            0.35f,
            offset / length,
            cameraHits,
            length,
            ~0,
            QueryTriggerInteraction.Ignore);
        float nearest = length;
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
        return nearest < length
            ? focus + offset.normalized * Mathf.Max(1.5f, nearest - 0.3f)
            : desired;
    }
}

public sealed class GridFlightBridge : MonoBehaviour
{
    Rigidbody body;
    ShipAssembly assembly;
    SpacecraftIfcsMotor ifcs;
    KeyboardMouseFlightInput input;
    GridLabCameraController cameraController;
    Vector3 buildPosition;
    Quaternion buildRotation;
    Vector3 spawnPosition;
    Coroutine enterRoutine;
    Coroutine resetRoutine;
    UnityPlanet.ModularAssembly.PlanetLabFlightEnvironmentController environment;
    UnityPlanet.ModularAssembly.HybridVehicleModeController hybrid;
    UnityPlanet.ModularAssembly.VehicleMotionCoordinatorV2 motionV2;
    UnityPlanet.ModularAssembly.VehicleMotionCoordinatorV3 motionV3;
    UnityPlanet.ModularAssembly.RobocraftMotionCoordinator motionRc1;

    public GridFlightState State { get; private set; } = GridFlightState.Build;
    public bool IsFlying => State == GridFlightState.Flight;
    public Rigidbody Body => body;
    public event Action<GridFlightState, string> StateChanged;

    public void Initialize(
        Rigidbody shipBody,
        ShipAssembly shipAssembly,
        SpacecraftIfcsMotor motor,
        KeyboardMouseFlightInput commandInput,
        GridLabCameraController cameraRig)
    {
        body = shipBody;
        assembly = shipAssembly;
        ifcs = motor;
        input = commandInput;
        cameraController = cameraRig;
        hybrid = GetComponent<
            UnityPlanet.ModularAssembly.HybridVehicleModeController>();
        if (hybrid != null)
            hybrid.enabled = false;
        motionV2 = GetComponent<
                       UnityPlanet.ModularAssembly.VehicleMotionCoordinatorV2>()
                   ?? gameObject.AddComponent<
                       UnityPlanet.ModularAssembly.VehicleMotionCoordinatorV2>();
        if (motionV2.IsActive)
            motionV2.EndFlight();
        motionV2.enabled = false;
        motionV3 = GetComponent<
            UnityPlanet.ModularAssembly.VehicleMotionCoordinatorV3>();
        if (motionV3 != null)
        {
            motionV3.EndFlight();
            motionV3.enabled = false;
        }
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
        ifcs.Configure(
            body,
            assembly,
            null,
            input,
            true,
            true);
        ifcs.SetAssistMode(SpacecraftAssistMode.Coupled);
        ifcs.ControlsEnabled = false;
        ifcs.enabled = false;
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
        SetState(
            GridFlightState.LoadingTerrain,
            "正在生成PlanetLab无限试飞地形……");
        body.isKinematic = true;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        cameraController.SetFlightMode(true);
        environment = FindObjectOfType<
            UnityPlanet.ModularAssembly.PlanetLabFlightEnvironmentController>();
        if (environment == null)
        {
            UnityPlanet.ModularAssembly.LabZoneManager zones =
                FindObjectOfType<UnityPlanet.ModularAssembly.LabZoneManager>();
            environment = zones != null
                ? zones.FlightEnvironment
                : null;
        }
        if (environment == null)
        {
            CompleteFlightPreparation(
                true,
                SafeSpawnAbove(buildPosition, body),
                "未找到PlanetLab环境控制器，已使用安全出生点。");
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
            ExitFlight();
            StateChanged?.Invoke(GridFlightState.Build, message);
            return;
        }

        spawnPosition = position;
        body.position = position;
        body.rotation = Quaternion.identity;
        Physics.SyncTransforms();
        body.isKinematic = false;
        body.velocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.WakeUp();
        ifcs.ControlsEnabled = false;
        ifcs.enabled = false;
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
        ifcs.ControlsEnabled = false;
        ifcs.ResetControllerState();
        ifcs.enabled = false;
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
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        cameraController.SetFlightMode(false);
        SetState(GridFlightState.Build, "已返回建造台。");
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
        ifcs.ControlsEnabled = false;
        ifcs.ResetControllerState();
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
            spawnPosition = position;
        body.position = spawnPosition;
        body.rotation = Quaternion.identity;
        Physics.SyncTransforms();
        body.isKinematic = false;
        body.velocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.WakeUp();
        ifcs.ControlsEnabled = false;
        ifcs.enabled = false;
        motionRc1?.BeginFlight();
    }

    void SetState(GridFlightState value, string message)
    {
        State = value;
        StateChanged?.Invoke(value, message);
    }

    public float DistanceFromSpawn => Vector3.Distance(body.position, spawnPosition);
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
        if (flight == null || !flight.IsFlying || !Input.GetMouseButton(0) || Time.time < nextShot)
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
        UnityPlanet.ModularAssembly.RobocraftMotionCoordinator rc1 =
            flight.Body.GetComponent<
                UnityPlanet.ModularAssembly.RobocraftMotionCoordinator>();
        if (rc1 != null && rc1.IsActive)
        {
            rc1.QueueVisualRecoil(-direction * 250f, position);
        }
        else
        {
            flight.Body.AddForceAtPosition(
                -direction * 250f,
                position,
                ForceMode.Impulse);
        }
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
                hit.rigidbody.AddForceAtPosition(impulse, hit.point, ForceMode.Impulse);
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
            DestroyTarget(damage);
            return;
        }
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
        previewMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
        model.Changed += HandleModelChanged;
        if (store.TryLoad(out ModularBlueprintData blueprint, out string error)
            && model.RestoreBlueprint(blueprint, out string restoreError))
        {
            Status("已自动载入模块蓝图。");
        }
        else if (!string.IsNullOrEmpty(error))
        {
            Status(error);
        }
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
        if (history.Undo(model))
            Status("已撤销。");
    }

    public void Redo()
    {
        if (history.Redo(model))
            Status("已重做。");
    }

    public void Save()
    {
        GridAssemblyValidation validation = model.Validate();
        if (!validation.IsValid)
        {
            Status("无法保存：" + validation.Message);
            return;
        }
        try
        {
            store.Save(model.CaptureBlueprint());
            Status("已保存 " + DateTime.Now.ToString("HH:mm:ss"));
        }
        catch (Exception exception)
        {
            Status("保存失败：" + exception.Message);
        }
    }

    public void Load()
    {
        if (!store.TryLoad(out ModularBlueprintData blueprint, out string error))
        {
            Status(string.IsNullOrEmpty(error) ? "没有已保存的模块蓝图。" : error);
            return;
        }
        if (!model.RestoreBlueprint(blueprint, out error))
        {
            Status("载入失败：" + error);
            return;
        }
        history.Clear();
        selectedRuntimeId = string.Empty;
        Status("蓝图已载入。");
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
        if (controller == null || statsText == null)
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
