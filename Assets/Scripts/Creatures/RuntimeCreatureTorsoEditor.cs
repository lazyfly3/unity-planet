using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public enum RuntimeTorsoHandleKind
{
    Point,
    Move,
    Width,
    Height,
    Roll
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
    int selectedPoint = -1;
    RuntimeTorsoHandle activeHandle;
    Vector3 dragAnchorWorld;
    Vector3 dragAxisWorld;
    float dragStartParameter;
    float dragStartValue;
    Vector3 dragStartPosition;
    Vector3 dragStartDirection;
    float nextPreviewTime;
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
        originalSpline = runtime.Genome.torsoSpline.Clone();
        selectedPoint = Mathf.Clamp(selectedPoint, 0, runtime.Genome.torsoSpline.points.Count - 1);
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
        int left = Mathf.Clamp(selectedPoint, 0, spline.points.Count - 2);
        CreatureTorsoControlPoint a = spline.points[left];
        CreatureTorsoControlPoint b = spline.points[left + 1];
        spline.points.Insert(left + 1, LerpPoint(a, b, 0.5f));
        selectedPoint = left + 1;
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
            if (!Physics.Raycast(ray, out RaycastHit hit, 500f)) return;
            RuntimeTorsoHandle handle = hit.collider.GetComponent<RuntimeTorsoHandle>();
            if (handle == null) return;
            if (handle.kind == RuntimeTorsoHandleKind.Point)
            {
                selectedPoint = handle.pointIndex;
                RebuildHandles();
                return;
            }
            BeginDrag(handle, ray);
        }
        if (activeHandle != null && Input.GetMouseButton(0)) Drag(ray);
        if (activeHandle != null && Input.GetMouseButtonUp(0))
        {
            activeHandle = null;
            runtime.RebuildAsync(CreatureBodyMeshQuality.Final);
            RebuildHandles();
        }
    }

    void BeginDrag(RuntimeTorsoHandle handle, Ray ray)
    {
        activeHandle = handle;
        selectedPoint = handle.pointIndex;
        PushUndo();
        CreatureTorsoControlPoint point = runtime.Genome.torsoSpline.points[selectedPoint];
        dragAnchorWorld = runtime.transform.TransformPoint(point.localPosition);
        dragAxisWorld = runtime.transform.TransformDirection(handle.localAxis).normalized;
        dragStartParameter = ClosestAxisParameter(ray, dragAnchorWorld, dragAxisWorld);
        dragStartPosition = point.localPosition;
        dragStartValue = handle.kind == RuntimeTorsoHandleKind.Width ? point.width
            : handle.kind == RuntimeTorsoHandleKind.Height ? point.height : point.rollDegrees;
        if (handle.kind == RuntimeTorsoHandleKind.Roll
            && TryRayPlane(ray, dragAnchorWorld, GetPointTangentWorld(selectedPoint), out Vector3 hit))
            dragStartDirection = (hit - dragAnchorWorld).normalized;
    }

    void Drag(Ray ray)
    {
        CreatureTorsoControlPoint point = runtime.Genome.torsoSpline.points[selectedPoint];
        if (activeHandle.kind == RuntimeTorsoHandleKind.Roll)
        {
            Vector3 tangent = GetPointTangentWorld(selectedPoint);
            if (TryRayPlane(ray, dragAnchorWorld, tangent, out Vector3 hit))
            {
                Vector3 direction = (hit - dragAnchorWorld).normalized;
                point.rollDegrees = dragStartValue + Vector3.SignedAngle(dragStartDirection, direction, tangent);
            }
        }
        else
        {
            float delta = ClosestAxisParameter(ray, dragAnchorWorld, dragAxisWorld) - dragStartParameter;
            if (activeHandle.kind == RuntimeTorsoHandleKind.Move)
            {
                Vector3 localDelta = runtime.transform.InverseTransformVector(dragAxisWorld * delta);
                point.localPosition = dragStartPosition + localDelta;
                KeepPointSeparated(selectedPoint, point);
            }
            else if (activeHandle.kind == RuntimeTorsoHandleKind.Width)
                point.width = Mathf.Clamp(dragStartValue + delta * 2f, 0.16f, 8f);
            else if (activeHandle.kind == RuntimeTorsoHandleKind.Height)
                point.height = Mathf.Clamp(dragStartValue + delta * 2f, 0.16f, 8f);
        }
        ShapeChanged(true, false);
    }

    void ShapeChanged(bool preview, bool pushUndo = false)
    {
        if (pushUndo) PushUndo();
        if (preview)
        {
            if (Time.unscaledTime < nextPreviewTime) return;
            nextPreviewTime = Time.unscaledTime + 0.1f;
            runtime.RebuildAsync(CreatureBodyMeshQuality.Preview);
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
        for (int i = 0; i < spline.points.Count; i++)
            CreatePrimitiveHandle(PrimitiveType.Sphere, RuntimeTorsoHandleKind.Point, i,
                spline.points[i].localPosition, Vector3.zero, 0.18f,
                i == selectedPoint ? new Color(1f, 0.72f, 0.12f) : new Color(0.1f, 0.8f, 1f));
        if (selectedPoint < 0 || selectedPoint >= spline.points.Count) return;
        CreatureTorsoControlPoint selected = spline.points[selectedPoint];
        CreatePrimitiveHandle(PrimitiveType.Cube, RuntimeTorsoHandleKind.Move, selectedPoint,
            selected.localPosition + Vector3.right * 0.72f, Vector3.right, 0.18f, Color.red);
        CreatePrimitiveHandle(PrimitiveType.Cube, RuntimeTorsoHandleKind.Move, selectedPoint,
            selected.localPosition + Vector3.up * 0.72f, Vector3.up, 0.18f, Color.green);
        CreatePrimitiveHandle(PrimitiveType.Cube, RuntimeTorsoHandleKind.Move, selectedPoint,
            selected.localPosition + Vector3.forward * 0.72f, Vector3.forward, 0.18f, Color.blue);
        CreatePrimitiveHandle(PrimitiveType.Cube, RuntimeTorsoHandleKind.Width, selectedPoint,
            selected.localPosition + Vector3.right * selected.width * 0.5f, Vector3.right, 0.24f, Color.magenta);
        CreatePrimitiveHandle(PrimitiveType.Cube, RuntimeTorsoHandleKind.Height, selectedPoint,
            selected.localPosition + Vector3.up * selected.height * 0.5f, Vector3.up, 0.24f, Color.yellow);
        CreateRollRing(selected);
    }

    void CreateRollRing(CreatureTorsoControlPoint point)
    {
        Vector3 tangent = GetPointTangentLocal(selectedPoint);
        Vector3 axisX = Vector3.Cross(Mathf.Abs(Vector3.Dot(tangent, Vector3.up)) > 0.9f ? Vector3.forward : Vector3.up, tangent).normalized;
        Vector3 axisY = Vector3.Cross(tangent, axisX).normalized;
        float radius = Mathf.Max(point.width, point.height) * 0.7f;
        for (int i = 0; i < 16; i++)
        {
            float angle = i / 16f * Mathf.PI * 2f;
            Vector3 offset = (axisX * Mathf.Cos(angle) + axisY * Mathf.Sin(angle)) * radius;
            CreatePrimitiveHandle(PrimitiveType.Sphere, RuntimeTorsoHandleKind.Roll, selectedPoint,
                point.localPosition + offset, tangent, 0.08f, new Color(1f, 0.45f, 0.1f));
        }
    }

    void CreatePrimitiveHandle(
        PrimitiveType primitive, RuntimeTorsoHandleKind kind, int index,
        Vector3 localPosition, Vector3 axis, float scale, Color color)
    {
        GameObject handle = GameObject.CreatePrimitive(primitive);
        handle.name = $"TorsoHandle_{kind}_{index}";
        handle.transform.SetParent(handleRoot, false);
        handle.transform.localPosition = localPosition;
        handle.transform.localScale = Vector3.one * scale;
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

    void KeepPointSeparated(int index, CreatureTorsoControlPoint point)
    {
        List<CreatureTorsoControlPoint> points = runtime.Genome.torsoSpline.points;
        if (index > 0 && Vector3.Distance(point.localPosition, points[index - 1].localPosition) < 0.12f)
            point.localPosition = Vector3.MoveTowards(points[index - 1].localPosition, dragStartPosition, 0.12f);
        if (index < points.Count - 1 && Vector3.Distance(point.localPosition, points[index + 1].localPosition) < 0.12f)
            point.localPosition = Vector3.MoveTowards(points[index + 1].localPosition, dragStartPosition, 0.12f);
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
        handleRoot = root.transform;
        handleRoot.SetParent(runtime.transform, false);
    }

    Material GetHandleMaterial()
    {
        if (handleMaterial == null)
            handleMaterial = new Material(Shader.Find("Standard")) { name = "RuntimeTorsoHandleMaterial" };
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
            ? "正在重建躯干..."
            : $"塑形点 {selectedPoint + 1}/{runtime.Genome.torsoSpline.points.Count}  |  E 应用并返回";
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
