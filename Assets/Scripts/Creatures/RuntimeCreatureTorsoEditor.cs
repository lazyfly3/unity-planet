using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public enum RuntimeTorsoHandleKind
{
    Point,
    Segment
}

public sealed class RuntimeTorsoHandle : MonoBehaviour
{
    public RuntimeTorsoHandleKind kind;
    public int pointIndex;
    public Vector3 localAxis;
}

[DisallowMultipleComponent]
public sealed class RuntimeCreatureTorsoEditor : MonoBehaviour
{
    [SerializeField] BioCreatureTestController creatureController;
    [SerializeField] BioCreatureFollowCamera followCamera;
    [SerializeField] Camera editorCamera;
    [SerializeField] GameObject editorPanel;
    [SerializeField] Text statusText;
    [SerializeField] Button insertButton;
    [SerializeField] Button deleteButton;
    [SerializeField] Button resetButton;
    [SerializeField] Button saveButton;
    [SerializeField] Button loadButton;
    [SerializeField] Button applyButton;

    readonly List<GameObject> handles = new List<GameObject>();
    readonly List<CreatureTorsoSpline> undo = new List<CreatureTorsoSpline>();
    readonly List<CreatureTorsoSpline> redo = new List<CreatureTorsoSpline>();

    CreatureTorsoRuntime runtime;
    CreatureTorsoSpline originalSpline;
    Transform handleRoot;
    Material handleMaterial;
    int handleLayer = -1;
    int selectedPoint = -1;
    int selectedSegment = -1;
    RuntimeTorsoHandle activeHandle;
    Vector3 dragAnchorWorld;
    Vector3 dragAxisWorld;
    float dragStartParameter;
    Vector3 directDragStartWorld;
    Vector3[] dragStartPositions;
    float cameraYaw;
    float cameraPitch = 18f;
    float cameraDistance = 10f;

    public bool IsEditing => runtime != null && runtime.IsEditing;

    void Awake()
    {
        ResolveSceneReferences();
        BindButton(insertButton, InsertPointAfterSelection);
        BindButton(deleteButton, DeleteSelectedPoint);
        BindButton(resetButton, ResetShape);
        BindButton(saveButton, SavePreset);
        BindButton(loadButton, LoadPreset);
        BindButton(applyButton, ApplyAndExit);
        if (editorPanel != null) editorPanel.SetActive(false);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.E))
        {
            if (IsEditing) ApplyAndExit();
            else EnterEditMode();
        }
        if (!IsEditing) return;

        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
        {
            if (Input.GetKeyDown(KeyCode.Z)) Undo();
            if (Input.GetKeyDown(KeyCode.Y)) Redo();
        }
        UpdateEditorCamera();
        UpdatePointerInteraction();
        UpdateStatus();
    }

    public void EnterEditMode()
    {
        if (creatureController == null || creatureController.CurrentCreature == null) return;
        runtime = creatureController.CurrentCreature.GetComponent<CreatureTorsoRuntime>();
        if (runtime == null)
        {
            Debug.LogError("Selected bio creature does not contain CreatureTorsoRuntime.", creatureController);
            return;
        }

        runtime.SetEditing(true);
        runtime.CancelPendingRebuild();
        originalSpline = runtime.Genome.torsoSpline.Clone();
        selectedPoint = Mathf.Clamp(selectedPoint, 0, runtime.Genome.torsoSpline.points.Count - 1);
        selectedSegment = -1;
        undo.Clear();
        redo.Clear();
        if (followCamera != null) followCamera.enabled = false;
        if (editorPanel != null) editorPanel.SetActive(true);
        CreateHandleRoot();
        RebuildHandles();
        FrameCreature();
    }

    public void ApplyAndExit()
    {
        if (!IsEditing) return;
        runtime.RebuildNow(CreatureBodyMeshQuality.Final);
        runtime.SetEditing(false);
        ClearHandles();
        if (editorPanel != null) editorPanel.SetActive(false);
        if (followCamera != null)
        {
            followCamera.enabled = true;
            followCamera.SetTarget(runtime.transform, true);
        }
        runtime = null;
        selectedPoint = -1;
    }

    public void InsertPointAfterSelection()
    {
        if (!IsEditing) return;
        CreatureTorsoSpline spline = runtime.Genome.torsoSpline;
        if (spline.points.Count >= CreatureTorsoSpline.MaximumPointCount) return;
        PushUndo();
        int left = selectedSegment >= 0
            ? Mathf.Clamp(selectedSegment, 0, spline.points.Count - 2)
            : Mathf.Clamp(selectedPoint, 0, spline.points.Count - 2);
        CreatureTorsoControlPoint a = spline.points[left];
        CreatureTorsoControlPoint b = spline.points[left + 1];
        spline.points.Insert(left + 1, LerpPoint(a, b, 0.5f));
        selectedPoint = left + 1;
        selectedSegment = -1;
        ShapeChanged(false);
    }

    public void DeleteSelectedPoint()
    {
        if (!IsEditing) return;
        CreatureTorsoSpline spline = runtime.Genome.torsoSpline;
        if (spline.points.Count <= CreatureTorsoSpline.MinimumPointCount || selectedPoint < 0) return;
        PushUndo();
        spline.points.RemoveAt(selectedPoint);
        selectedPoint = Mathf.Clamp(selectedPoint, 0, spline.points.Count - 1);
        ShapeChanged(false);
    }

    public void ResetShape()
    {
        if (!IsEditing || originalSpline == null) return;
        PushUndo();
        runtime.Genome.torsoSpline = originalSpline.Clone();
        selectedPoint = 0;
        ShapeChanged(false);
    }

    public void SavePreset()
    {
        if (!IsEditing) return;
        try
        {
            CreatureTorsoPresetStore.Save(runtime.Genome);
            SetStatus("已保存控制点预设");
        }
        catch (Exception exception)
        {
            Debug.LogError("Could not save creature torso preset: " + exception, this);
            SetStatus("保存失败");
        }
    }

    public void LoadPreset()
    {
        if (!IsEditing) return;
        if (!CreatureTorsoPresetStore.TryLoad(runtime.Genome.seed, out CreatureTorsoPresetData preset, out string error))
        {
            SetStatus(error);
            return;
        }
        PushUndo();
        runtime.Genome.torsoSpline = preset.torsoSpline.Clone();
        selectedPoint = 0;
        ShapeChanged(false);
    }

    public void Undo()
    {
        if (!IsEditing || undo.Count == 0) return;
        redo.Add(runtime.Genome.torsoSpline.Clone());
        runtime.Genome.torsoSpline = undo[undo.Count - 1];
        undo.RemoveAt(undo.Count - 1);
        selectedPoint = Mathf.Clamp(selectedPoint, 0, runtime.Genome.torsoSpline.points.Count - 1);
        ShapeChanged(false, false);
    }

    public void Redo()
    {
        if (!IsEditing || redo.Count == 0) return;
        undo.Add(runtime.Genome.torsoSpline.Clone());
        runtime.Genome.torsoSpline = redo[redo.Count - 1];
        redo.RemoveAt(redo.Count - 1);
        selectedPoint = Mathf.Clamp(selectedPoint, 0, runtime.Genome.torsoSpline.points.Count - 1);
        ShapeChanged(false, false);
    }

    void UpdatePointerInteraction()
    {
        if (editorCamera == null) return;
        Ray ray = editorCamera.ScreenPointToRay(Input.mousePosition);
        if (Input.GetMouseButtonDown(0))
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            int mask = handleLayer >= 0 ? 1 << handleLayer : Physics.AllLayers;
            if (!Physics.Raycast(ray, out RaycastHit hit, 500f, mask, QueryTriggerInteraction.Collide)) return;
            RuntimeTorsoHandle handle = hit.collider.GetComponent<RuntimeTorsoHandle>();
            if (handle == null) return;
            if (handle.kind == RuntimeTorsoHandleKind.Point)
            {
                selectedPoint = handle.pointIndex;
                selectedSegment = -1;
                BeginDrag(handle, ray);
            }
            else if (handle.kind == RuntimeTorsoHandleKind.Segment)
            {
                selectedPoint = Mathf.Clamp(handle.pointIndex + 1, 1,
                    runtime.Genome.torsoSpline.points.Count - 1);
                selectedSegment = handle.pointIndex;
                BeginDrag(handle, ray);
            }
            else
                BeginDrag(handle, ray);
        }
        if (activeHandle != null && Input.GetMouseButton(0)) Drag(ray);
        if (activeHandle != null && Input.GetMouseButtonUp(0))
        {
            activeHandle = null;
            runtime.ScheduleFinalRebuild();
            RebuildHandles();
        }
    }

    void BeginDrag(RuntimeTorsoHandle handle, Ray ray)
    {
        activeHandle = handle;
        if (handle.kind != RuntimeTorsoHandleKind.Segment)
        {
            selectedPoint = handle.pointIndex;
            selectedSegment = -1;
        }
        PushUndo();
        runtime.CancelPendingRebuild();
        CreatureTorsoControlPoint point = runtime.Genome.torsoSpline.points[selectedPoint];
        dragAnchorWorld = runtime.transform.TransformPoint(point.localPosition);
        List<CreatureTorsoControlPoint> points = runtime.Genome.torsoSpline.points;
        dragStartPositions = new Vector3[points.Count];
        for (int i = 0; i < points.Count; i++) dragStartPositions[i] = points[i].localPosition;
        if (handle.kind == RuntimeTorsoHandleKind.Point)
        {
            Vector3 planeNormal = editorCamera != null ? editorCamera.transform.forward : transform.forward;
            if (!TryRayPlane(ray, dragAnchorWorld, planeNormal, out directDragStartWorld))
                directDragStartWorld = dragAnchorWorld;
        }
        else
        {
            dragAxisWorld = runtime.transform.TransformDirection(GetStretchAxisLocal(selectedPoint)).normalized;
            dragStartParameter = ClosestAxisParameter(ray, dragAnchorWorld, dragAxisWorld);
        }
    }

    void Drag(Ray ray)
    {
        if (activeHandle.kind == RuntimeTorsoHandleKind.Point)
        {
            Vector3 planeNormal = editorCamera != null ? editorCamera.transform.forward : transform.forward;
            if (TryRayPlane(ray, dragAnchorWorld, planeNormal, out Vector3 hit))
                ApplyDirectNodeDrag(runtime.transform.InverseTransformVector(hit - directDragStartWorld));
        }
        else if (activeHandle.kind == RuntimeTorsoHandleKind.Segment)
        {
            float delta = ClosestAxisParameter(ray, dragAnchorWorld, dragAxisWorld) - dragStartParameter;
            ApplySegmentStretch(delta);
        }
        ShapeChanged(true, false);
    }

    void ApplyDirectNodeDrag(Vector3 localDelta)
    {
        List<CreatureTorsoControlPoint> points = runtime.Genome.torsoSpline.points;
        Vector3 tangent = GetDragStartTangent(selectedPoint);
        Vector3 stretchDelta = Vector3.Project(localDelta, tangent);
        Vector3 bendDelta = localDelta - stretchDelta;

        for (int i = 0; i < points.Count; i++)
        {
            float bendWeight = Mathf.Clamp01(1f - Mathf.Abs(i - selectedPoint) / 2.5f);
            Vector3 displacement = bendDelta * bendWeight;
            if (selectedPoint == 0)
            {
                if (i == 0) displacement += stretchDelta;
            }
            else if (i >= selectedPoint)
                displacement += stretchDelta;
            points[i].localPosition = dragStartPositions[i] + displacement;
        }
        ConstrainPointSpacing();
    }

    Vector3 GetDragStartTangent(int index)
    {
        if (dragStartPositions == null || dragStartPositions.Length < 2) return Vector3.forward;
        Vector3 tangent = index == 0
            ? dragStartPositions[1] - dragStartPositions[0]
            : index == dragStartPositions.Length - 1
                ? dragStartPositions[index] - dragStartPositions[index - 1]
                : dragStartPositions[index + 1] - dragStartPositions[index - 1];
        return tangent.sqrMagnitude > 0.0001f ? tangent.normalized : Vector3.forward;
    }

    void ShapeChanged(bool preview, bool pushUndo = false)
    {
        if (pushUndo) PushUndo();
        if (preview)
        {
            runtime.ApplyBonePreview();
            RefreshSkeletonHandles();
        }
        else
        {
            RebuildHandles();
            runtime.RebuildAsync(CreatureBodyMeshQuality.Final);
        }
    }

    void PushUndo()
    {
        if (!IsEditing) return;
        undo.Add(runtime.Genome.torsoSpline.Clone());
        if (undo.Count > 32) undo.RemoveAt(0);
        redo.Clear();
    }

    void RebuildHandles()
    {
        ClearHandleObjects();
        if (!IsEditing) return;
        CreateHandleRoot();
        CreatureTorsoSpline spline = runtime.Genome.torsoSpline;
        for (int i = 0; i < spline.points.Count - 1; i++) CreateSegmentHandle(i);
        for (int i = 0; i < spline.points.Count; i++)
            CreatePrimitiveHandle(PrimitiveType.Sphere, RuntimeTorsoHandleKind.Point, i,
                spline.points[i].localPosition, Vector3.zero,
                Mathf.Clamp(0.12f + Mathf.Max(spline.points[i].width, spline.points[i].height) * 0.05f,
                    0.16f, 0.32f),
                i == selectedPoint ? new Color(1f, 0.82f, 0.28f, 0.95f) : new Color(0.72f, 0.9f, 1f, 0.82f));
    }

    GameObject CreatePrimitiveHandle(
        PrimitiveType primitive, RuntimeTorsoHandleKind kind, int index,
        Vector3 localPosition, Vector3 axis, float scale, Color color)
    {
        GameObject handle = GameObject.CreatePrimitive(primitive);
        handle.name = $"TorsoHandle_{kind}_{index}";
        handle.transform.SetParent(handleRoot, false);
        handle.transform.localPosition = localPosition;
        handle.transform.localScale = Vector3.one * scale;
        if (handleLayer >= 0) handle.layer = handleLayer;
        RuntimeTorsoHandle descriptor = handle.AddComponent<RuntimeTorsoHandle>();
        descriptor.kind = kind;
        descriptor.pointIndex = index;
        descriptor.localAxis = axis;
        Renderer renderer = handle.GetComponent<Renderer>();
        renderer.sharedMaterial = GetHandleMaterial();
        var properties = new MaterialPropertyBlock();
        properties.SetColor("_Color", color);
        renderer.SetPropertyBlock(properties);
        handles.Add(handle);
        return handle;
    }

    void CreateSegmentHandle(int segmentIndex)
    {
        List<CreatureTorsoControlPoint> points = runtime.Genome.torsoSpline.points;
        Vector3 start = points[segmentIndex].localPosition;
        Vector3 end = points[segmentIndex + 1].localPosition;
        Vector3 direction = end - start;
        float length = Mathf.Max(0.01f, direction.magnitude);
        GameObject segment = CreatePrimitiveHandle(
            PrimitiveType.Cube,
            RuntimeTorsoHandleKind.Segment,
            segmentIndex,
            (start + end) * 0.5f,
            direction.normalized,
            1f,
            selectedSegment == segmentIndex
                ? new Color(1f, 0.55f, 0.08f, 0.95f)
                : new Color(0.56f, 0.82f, 1f, 0.62f));
        if (direction.sqrMagnitude > 0.0001f)
            segment.transform.localRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        segment.transform.localScale = new Vector3(0.07f, 0.07f, length);
    }

    void RefreshSkeletonHandles()
    {
        if (runtime == null) return;
        List<CreatureTorsoControlPoint> points = runtime.Genome.torsoSpline.points;
        foreach (GameObject handleObject in handles)
        {
            if (handleObject == null) continue;
            RuntimeTorsoHandle handle = handleObject.GetComponent<RuntimeTorsoHandle>();
            if (handle == null) continue;
            if (handle.kind == RuntimeTorsoHandleKind.Point && handle.pointIndex < points.Count)
                handleObject.transform.localPosition = points[handle.pointIndex].localPosition;
            else if (handle.kind == RuntimeTorsoHandleKind.Segment && handle.pointIndex + 1 < points.Count)
            {
                Vector3 start = points[handle.pointIndex].localPosition;
                Vector3 end = points[handle.pointIndex + 1].localPosition;
                Vector3 direction = end - start;
                handleObject.transform.localPosition = (start + end) * 0.5f;
                if (direction.sqrMagnitude > 0.0001f)
                    handleObject.transform.localRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
                handleObject.transform.localScale = new Vector3(
                    0.07f, 0.07f, Mathf.Max(0.01f, direction.magnitude));
            }
        }
    }

    void ApplySegmentStretch(float delta)
    {
        List<CreatureTorsoControlPoint> points = runtime.Genome.torsoSpline.points;
        int firstMovedPoint = selectedPoint == 0 ? 1 : selectedPoint;
        if (firstMovedPoint <= 0 || firstMovedPoint >= points.Count) return;
        Vector3 axis = GetStretchAxisLocal(firstMovedPoint);
        float originalLength = Vector3.Distance(
            dragStartPositions[firstMovedPoint - 1], dragStartPositions[firstMovedPoint]);
        float constrainedDelta = Mathf.Clamp(delta, -(originalLength - 0.12f), 8f);
        Vector3 displacement = axis * constrainedDelta;
        for (int i = firstMovedPoint; i < points.Count; i++)
            points[i].localPosition = dragStartPositions[i] + displacement;
        ConstrainPointSpacing();
    }

    Vector3 GetStretchAxisLocal(int index)
    {
        List<CreatureTorsoControlPoint> points = runtime.Genome.torsoSpline.points;
        int left = Mathf.Clamp(index - 1, 0, points.Count - 2);
        Vector3 axis = points[left + 1].localPosition - points[left].localPosition;
        return axis.sqrMagnitude > 0.0001f ? axis.normalized : Vector3.forward;
    }

    void ConstrainPointSpacing()
    {
        const float minimumSpacing = 0.12f;
        List<CreatureTorsoControlPoint> points = runtime.Genome.torsoSpline.points;
        for (int i = 1; i < points.Count; i++)
        {
            Vector3 segment = points[i].localPosition - points[i - 1].localPosition;
            if (segment.sqrMagnitude >= minimumSpacing * minimumSpacing) continue;
            Vector3 fallback = dragStartPositions != null && i < dragStartPositions.Length
                ? dragStartPositions[i] - dragStartPositions[i - 1] : Vector3.forward;
            Vector3 direction = segment.sqrMagnitude > 0.000001f ? segment.normalized
                : fallback.sqrMagnitude > 0.000001f ? fallback.normalized : Vector3.forward;
            points[i].localPosition = points[i - 1].localPosition + direction * minimumSpacing;
        }
    }

    void UpdateEditorCamera()
    {
        if (editorCamera == null || runtime == null) return;
        if (Input.GetMouseButton(1))
        {
            cameraYaw += Input.GetAxis("Mouse X") * 3f;
            cameraPitch = Mathf.Clamp(cameraPitch - Input.GetAxis("Mouse Y") * 2.4f, -75f, 75f);
        }
        cameraDistance = Mathf.Clamp(cameraDistance - Input.mouseScrollDelta.y * 0.8f, 3f, 30f);
        Quaternion orbit = runtime.transform.rotation * Quaternion.Euler(cameraPitch, cameraYaw, 0f);
        Vector3 target = runtime.transform.TransformPoint(GetSplineCenter());
        editorCamera.transform.position = target - orbit * Vector3.forward * cameraDistance;
        editorCamera.transform.rotation = Quaternion.LookRotation(target - editorCamera.transform.position, runtime.transform.up);
    }

    void FrameCreature()
    {
        float length = 0f;
        CreatureTorsoSpline spline = runtime.Genome.torsoSpline;
        for (int i = 1; i < spline.points.Count; i++)
            length += Vector3.Distance(spline.points[i - 1].localPosition, spline.points[i].localPosition);
        cameraDistance = Mathf.Clamp(length * 1.8f, 6f, 24f);
    }

    Vector3 GetSplineCenter()
    {
        Vector3 center = Vector3.zero;
        foreach (CreatureTorsoControlPoint point in runtime.Genome.torsoSpline.points)
            center += point.localPosition;
        return center / runtime.Genome.torsoSpline.points.Count;
    }

    Vector3 GetPointTangentLocal(int index)
    {
        List<CreatureTorsoControlPoint> points = runtime.Genome.torsoSpline.points;
        Vector3 tangent = index == 0 ? points[1].localPosition - points[0].localPosition
            : index == points.Count - 1 ? points[index].localPosition - points[index - 1].localPosition
            : points[index + 1].localPosition - points[index - 1].localPosition;
        return tangent.sqrMagnitude > 0.0001f ? tangent.normalized : Vector3.forward;
    }

    Vector3 GetPointTangentWorld(int index)
    {
        return runtime.transform.TransformDirection(GetPointTangentLocal(index)).normalized;
    }

    static float ClosestAxisParameter(Ray ray, Vector3 axisOrigin, Vector3 axisDirection)
    {
        Vector3 w0 = ray.origin - axisOrigin;
        float b = Vector3.Dot(ray.direction, axisDirection);
        float d = Vector3.Dot(ray.direction, w0);
        float e = Vector3.Dot(axisDirection, w0);
        float denominator = 1f - b * b;
        return Mathf.Abs(denominator) > 0.0001f ? (e - b * d) / denominator : e;
    }

    static bool TryRayPlane(Ray ray, Vector3 point, Vector3 normal, out Vector3 hit)
    {
        var plane = new Plane(normal, point);
        if (plane.Raycast(ray, out float distance))
        {
            hit = ray.GetPoint(distance);
            return true;
        }
        hit = default;
        return false;
    }

    static CreatureTorsoControlPoint LerpPoint(
        CreatureTorsoControlPoint a, CreatureTorsoControlPoint b, float t)
    {
        return new CreatureTorsoControlPoint
        {
            localPosition = Vector3.LerpUnclamped(a.localPosition, b.localPosition, t),
            width = Mathf.LerpUnclamped(a.width, b.width, t),
            height = Mathf.LerpUnclamped(a.height, b.height, t),
            rollDegrees = Mathf.LerpAngle(a.rollDegrees, b.rollDegrees, t),
            blendRadius = Mathf.LerpUnclamped(a.blendRadius, b.blendRadius, t),
            taper = Mathf.LerpUnclamped(a.taper, b.taper, t)
        };
    }

    void ResolveSceneReferences()
    {
        if (creatureController == null) creatureController = FindObjectOfType<BioCreatureTestController>();
        if (followCamera == null) followCamera = FindObjectOfType<BioCreatureFollowCamera>();
        if (editorCamera == null) editorCamera = Camera.main;
        if (editorPanel == null) editorPanel = GameObject.Find("TorsoEditorPanel");
        Transform panel = editorPanel != null ? editorPanel.transform : null;
        if (panel == null) return;
        if (statusText == null) statusText = FindChild<Text>(panel, "StatusText");
        if (insertButton == null) insertButton = FindChild<Button>(panel, "InsertButton");
        if (deleteButton == null) deleteButton = FindChild<Button>(panel, "DeleteButton");
        if (resetButton == null) resetButton = FindChild<Button>(panel, "ResetButton");
        if (saveButton == null) saveButton = FindChild<Button>(panel, "SaveButton");
        if (loadButton == null) loadButton = FindChild<Button>(panel, "LoadButton");
        if (applyButton == null) applyButton = FindChild<Button>(panel, "ApplyButton");
    }

    static T FindChild<T>(Transform parent, string objectName) where T : Component
    {
        T[] components = parent.GetComponentsInChildren<T>(true);
        foreach (T component in components)
            if (component.name == objectName) return component;
        return null;
    }

    static void BindButton(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button != null) button.onClick.AddListener(action);
    }

    void CreateHandleRoot()
    {
        if (handleRoot != null || runtime == null) return;
        var root = new GameObject("RuntimeTorsoHandles");
        handleLayer = LayerMask.NameToLayer("CreatureEditHandle");
        if (handleLayer < 0)
            Debug.LogError("The CreatureEditHandle layer is missing from TagManager.", this);
        else
            root.layer = handleLayer;
        handleRoot = root.transform;
        handleRoot.SetParent(runtime.transform, false);
    }

    Material GetHandleMaterial()
    {
        if (handleMaterial == null)
        {
            Shader shader = Shader.Find("Creature/HandleXRay");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            handleMaterial = new Material(shader) { name = "RuntimeTorsoHandleXRayMaterial" };
        }
        return handleMaterial;
    }

    void ClearHandleObjects()
    {
        foreach (GameObject handle in handles)
            if (handle != null) Destroy(handle);
        handles.Clear();
    }

    void ClearHandles()
    {
        ClearHandleObjects();
        if (handleRoot != null) Destroy(handleRoot.gameObject);
        handleRoot = null;
    }

    void OnDestroy()
    {
        ClearHandles();
        if (handleMaterial != null) Destroy(handleMaterial);
    }

    void UpdateStatus()
    {
        if (statusText == null || runtime == null) return;
        statusText.text = runtime.RebuildInProgress
            ? "正在后台重建躯干..."
            : $"骨骼 {selectedPoint + 1}/{runtime.Genome.torsoSpline.points.Count} | "
                + "按住节点直接塑形，拖动骨骼线直接拉伸，E 应用";
    }

    void SetStatus(string value)
    {
        if (statusText != null) statusText.text = value;
    }
}

public static class CreatureTorsoPresetStore
{
    public static string DirectoryPath => Path.Combine(Application.persistentDataPath, "BioCreaturePresets");

    public static string GetPath(int seed)
    {
        return Path.Combine(DirectoryPath, $"creature_{seed}.json");
    }

    public static void Save(CreatureGenome genome)
    {
        string error = null;
        if (genome == null || genome.torsoSpline == null || !genome.torsoSpline.Validate(out error))
            throw new InvalidDataException(error ?? "Creature torso is invalid.");
        Directory.CreateDirectory(DirectoryPath);
        var preset = new CreatureTorsoPresetData
        {
            generatorVersion = genome.generatorVersion,
            seed = genome.seed,
            topology = genome.topology,
            torsoSpline = genome.torsoSpline.Clone()
        };
        File.WriteAllText(GetPath(genome.seed), JsonUtility.ToJson(preset, true));
    }

    public static bool TryLoad(int seed, out CreatureTorsoPresetData preset, out string error)
    {
        preset = null;
        error = null;
        string path = GetPath(seed);
        if (!File.Exists(path))
        {
            error = "没有该种子的躯干预设";
            return false;
        }
        try
        {
            preset = JsonUtility.FromJson<CreatureTorsoPresetData>(File.ReadAllText(path));
            if (preset == null || preset.version != 1 || preset.seed != seed
                || preset.torsoSpline == null || !preset.torsoSpline.Validate(out error))
            {
                error = error ?? "躯干预设损坏或版本不兼容";
                preset = null;
                return false;
            }
            error = null;
            return true;
        }
        catch (Exception exception)
        {
            error = "读取预设失败: " + exception.Message;
            preset = null;
            return false;
        }
    }
}
