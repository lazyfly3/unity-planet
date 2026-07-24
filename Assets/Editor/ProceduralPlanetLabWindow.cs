using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class ProceduralPlanetLabWindow : EditorWindow
{
    static readonly string[] Tabs = { "基础", "地形", "材质", "海洋", "灯光" };
    static readonly string[] ResolutionLabels = { "64 实时", "96 高质量", "128 展示" };
    static readonly int[] ResolutionValues = { 64, 96, 128 };

    ProceduralPlanetLabController controller;
    ProceduralPlanetPreset preset;
    SerializedObject serializedPreset;
    int selectedTab;
    Vector2 scroll;
    double nextRepaint;

    [MenuItem("Tools/Voxel Planet/Procedural Planet Lab")]
    public static void OpenWindow()
    {
        if (!ProceduralPlanetLabAssetBuilder.OpenLaboratoryScene())
            return;
        var window = GetWindow<ProceduralPlanetLabWindow>();
        window.titleContent = new GUIContent("Procedural Planet Lab");
        window.minSize = new Vector2(470f, 680f);
        window.BindScene();
        window.Show();
    }

    void OnEnable()
    {
        titleContent = new GUIContent("Procedural Planet Lab");
        minSize = new Vector2(470f, 680f);
        EditorApplication.update -= EditorUpdate;
        EditorApplication.update += EditorUpdate;
        EditorSceneManager.sceneOpened -= OnSceneOpened;
        EditorSceneManager.sceneOpened += OnSceneOpened;
        if (SceneManager.GetActiveScene().path
            == ProceduralPlanetLabAssetBuilder.ScenePath)
        {
            BindScene();
        }
    }

    void OnDisable()
    {
        EditorApplication.update -= EditorUpdate;
        EditorSceneManager.sceneOpened -= OnSceneOpened;
    }

    void OnSceneOpened(Scene scene, OpenSceneMode mode)
    {
        if (scene.path == ProceduralPlanetLabAssetBuilder.ScenePath)
            BindScene();
    }

    void EditorUpdate()
    {
        if (EditorApplication.timeSinceStartup < nextRepaint)
            return;
        nextRepaint = EditorApplication.timeSinceStartup + 0.05d;
        if (preset != null && preset.preview != null && preset.preview.autoRotate)
            Repaint();
    }

    void BindScene()
    {
        controller = ProceduralPlanetLabAssetBuilder.FindController();
        if (controller == null)
            return;
        SetPreset(controller.Preset != null
            ? controller.Preset
            : ProceduralPlanetLabAssetBuilder.GetExamplePreset(
                ProceduralPlanetLabTemplate.TemperateOcean));
    }

    void SetPreset(ProceduralPlanetPreset value)
    {
        preset = value;
        serializedPreset = preset != null ? new SerializedObject(preset) : null;
        if (controller != null && preset != null)
        {
            bool changed = controller.Preset != preset;
            if (changed)
                Undo.RecordObject(controller, "Change Planet Lab Preset");
            controller.ApplyPreset(preset, controller.TerrainMesh == null);
            if (changed)
            {
                EditorUtility.SetDirty(controller);
                EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
            }
        }
        Repaint();
    }

    void OnGUI()
    {
        DrawHeader();
        if (SceneManager.GetActiveScene().path
            != ProceduralPlanetLabAssetBuilder.ScenePath)
        {
            EditorGUILayout.HelpBox(
                "当前不是 PlanetLab 场景。实验室不会加载玩家、体素区块、天气或星系系统。",
                MessageType.Info);
            if (GUILayout.Button("打开 PlanetLab 场景", GUILayout.Height(32f))
                && ProceduralPlanetLabAssetBuilder.OpenLaboratoryScene())
            {
                BindScene();
            }
            return;
        }

        if (controller == null)
            BindScene();
        if (controller == null)
        {
            EditorGUILayout.HelpBox(
                "场景中缺少 ProceduralPlanetLabController，请重建实验室场景。",
                MessageType.Error);
            if (GUILayout.Button("重建实验室场景"))
                ProceduralPlanetLabAssetBuilder.RebuildLaboratoryAssets();
            return;
        }

        DrawPresetToolbar();
        DrawPreview();
        selectedTab = GUILayout.Toolbar(selectedTab, Tabs, GUILayout.Height(25f));
        scroll = EditorGUILayout.BeginScrollView(scroll);
        DrawSelectedTab();
        EditorGUILayout.EndScrollView();
        DrawBottomToolbar();
    }

    void DrawHeader()
    {
        Rect rect = EditorGUILayout.GetControlRect(false, 38f);
        EditorGUI.DrawRect(rect, new Color(0.055f, 0.065f, 0.085f));
        GUI.Label(
            new Rect(rect.x + 12f, rect.y + 5f, rect.width - 24f, 22f),
            "程序化星球实验室",
            new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 17,
                normal = { textColor = new Color(0.72f, 0.88f, 1f) }
            });
    }

    void DrawPresetToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            ProceduralPlanetPreset picked = (ProceduralPlanetPreset)
                EditorGUILayout.ObjectField(
                    preset,
                    typeof(ProceduralPlanetPreset),
                    false,
                    GUILayout.MinWidth(130f));
            if (picked != preset)
                SetPreset(picked);
            if (GUILayout.Button("新建", EditorStyles.toolbarButton, GUILayout.Width(44f)))
                CreatePreset();
            if (GUILayout.Button("载入", EditorStyles.toolbarButton, GUILayout.Width(44f)))
                LoadPreset();
            if (GUILayout.Button("另存", EditorStyles.toolbarButton, GUILayout.Width(44f)))
                SavePresetAs();
        }

        if (preset == null)
            return;
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.PrefixLabel("示例类型");
            EditorGUI.BeginChangeCheck();
            ProceduralPlanetLabTemplate template =
                (ProceduralPlanetLabTemplate)EditorGUILayout.EnumPopup(preset.template);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(preset, "Apply Planet Lab Template");
                preset.ApplyTemplate(template);
                EditorUtility.SetDirty(preset);
                serializedPreset = new SerializedObject(preset);
                controller.ApplyPreset(preset, true);
            }
            if (GUILayout.Button("恢复该类型默认", GUILayout.Width(110f)))
            {
                Undo.RecordObject(preset, "Reset Planet Lab Preset");
                preset.ResetToTemplate();
                EditorUtility.SetDirty(preset);
                serializedPreset = new SerializedObject(preset);
                controller.ApplyPreset(preset, true);
            }
        }
    }

    void DrawPreview()
    {
        float previewHeight = Mathf.Clamp(position.height * 0.38f, 220f, 390f);
        Rect rect = GUILayoutUtility.GetRect(
            100f,
            previewHeight,
            GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, Color.black);
        if (controller != null)
        {
            RenderTexture texture = controller.RenderPreview(
                Mathf.Max(64, Mathf.RoundToInt(rect.width)),
                Mathf.Max(64, Mathf.RoundToInt(rect.height)));
            if (texture != null)
                GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, false);
        }
        GUI.Box(rect, GUIContent.none);
        GUI.Label(
            new Rect(rect.x + 9f, rect.yMax - 24f, rect.width - 18f, 18f),
            "拖动旋转 · 滚轮缩放",
            EditorStyles.miniLabel);
        HandlePreviewInput(rect);
    }

    void HandlePreviewInput(Rect rect)
    {
        Event current = Event.current;
        int controlId = GUIUtility.GetControlID(
            "ProceduralPlanetLabPreview".GetHashCode(),
            FocusType.Passive,
            rect);
        switch (current.GetTypeForControl(controlId))
        {
            case EventType.MouseDown:
                if (current.button == 0 && rect.Contains(current.mousePosition))
                {
                    GUIUtility.hotControl = controlId;
                    current.Use();
                }
                break;
            case EventType.MouseDrag:
                if (GUIUtility.hotControl == controlId && current.button == 0)
                {
                    controller.OrbitPreview(current.delta);
                    MarkPresetDirty();
                    Repaint();
                    current.Use();
                }
                break;
            case EventType.MouseUp:
                if (GUIUtility.hotControl == controlId && current.button == 0)
                {
                    GUIUtility.hotControl = 0;
                    current.Use();
                }
                break;
            case EventType.ScrollWheel:
                if (rect.Contains(current.mousePosition))
                {
                    controller.ZoomPreview(current.delta.y);
                    MarkPresetDirty();
                    Repaint();
                    current.Use();
                }
                break;
        }
    }

    void DrawSelectedTab()
    {
        if (preset == null || serializedPreset == null)
        {
            EditorGUILayout.HelpBox("请选择或新建一个星球预设。", MessageType.Info);
            return;
        }

        serializedPreset.Update();
        EditorGUI.BeginChangeCheck();
        bool rebuildShape = false;
        switch (selectedTab)
        {
            case 1:
                rebuildShape = true;
                DrawShapeTab();
                break;
            case 2:
                DrawSurfaceTab();
                break;
            case 3:
                DrawOceanTab();
                break;
            case 4:
                DrawLightingTab();
                break;
            default:
                rebuildShape = true;
                DrawBasicTab();
                break;
        }

        if (!EditorGUI.EndChangeCheck())
            return;
        serializedPreset.ApplyModifiedProperties();
        preset.ClampValues();
        EditorUtility.SetDirty(preset);
        if (rebuildShape)
            controller.RequestRebuild();
        else
            controller.RefreshAppearance();
        Repaint();
    }

    void DrawBasicTab()
    {
        EditorGUILayout.LabelField("星球定义", EditorStyles.boldLabel);
        DrawProperty("displayName", "名称");
        DrawProperty("seed", "种子");
        DrawProperty("radius", "星球半径");
        DrawProperty("maximumTerrainElevation", "最大地形高度");

        SerializedProperty resolution =
            serializedPreset.FindProperty("previewResolution");
        int index = Array.IndexOf(ResolutionValues, resolution.intValue);
        index = Mathf.Max(0, index);
        int next = EditorGUILayout.Popup("预览精度", index, ResolutionLabels);
        resolution.intValue = ResolutionValues[next];

        EditorGUILayout.Space(5f);
        EditorGUILayout.HelpBox(
            "64 用于实时调节；96/128 只建议在手动高质量预览或截图时使用。",
            MessageType.None);
    }

    void DrawShapeTab()
    {
        EditorGUILayout.LabelField("大陆与海床", EditorStyles.boldLabel);
        DrawProperty("terrain.continentThreshold", "大陆比例");
        DrawProperty("terrain.continentScale", "大陆噪声尺度");
        DrawProperty("terrain.continentHeight", "大陆高度");
        DrawProperty("terrain.continentWarp", "域扭曲");
        DrawProperty("terrain.continentSharpness", "大陆锐度");
        DrawProperty("terrain.oceanFloorDepth", "海底深度");
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("细节与山脉", EditorStyles.boldLabel);
        DrawProperty("terrain.detailScale", "细节尺度");
        DrawProperty("terrain.detailHeight", "细节高度");
        DrawProperty("terrain.ridgeHeight", "山脉高度");
        DrawProperty("terrain.mountainMask", "山脉覆盖");
        DrawProperty("terrain.terraceStrength", "阶梯侵蚀");
    }

    void DrawSurfaceTab()
    {
        EditorGUILayout.LabelField("地表颜色", EditorStyles.boldLabel);
        DrawProperty("visual.lowlandColor", "陆地");
        DrawProperty("visual.highlandColor", "高地");
        DrawProperty("visual.cliffColor", "悬崖");
        DrawProperty("visual.rockColor", "岩石");
        DrawProperty("visual.accentColor", "点缀");
        DrawProperty("visual.shoreColor", "岸线");
        DrawProperty("visual.snowColor", "积雪");
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("着色", EditorStyles.boldLabel);
        DrawProperty("visual.facetStrength", "低多边形强度");
        DrawProperty("visual.lightingBands", "光照色阶");
        DrawProperty("visual.macroColorSize", "宏观色块尺寸");
        DrawProperty("visual.macroVariation", "色块变化");
        DrawProperty("visual.cliffSlope", "悬崖坡度");
        DrawProperty("visual.snowLine", "雪线");
        DrawProperty("visual.snowAmount", "积雪量");
    }

    void DrawOceanTab()
    {
        EditorGUILayout.LabelField("海洋", EditorStyles.boldLabel);
        DrawProperty("visual.oceanEnabled", "启用海洋");
        DrawProperty("visual.oceanLevel", "海平面");
        DrawProperty("visual.deepOceanColor", "深水颜色");
        DrawProperty("visual.shallowOceanColor", "浅水颜色");
        DrawProperty("visual.shoreWidth", "岸线宽度");
        DrawProperty("visual.oceanSmoothness", "光滑度");
        DrawProperty("visual.oceanWaveStrength", "波浪强度");
        DrawProperty("visual.oceanWaveScale", "Wave Scale");
        DrawProperty("visual.oceanWaveSpeed", "Wave Speed");
        DrawProperty("visual.oceanNormalStrength", "Normal Strength");
        DrawProperty("visual.oceanFoamStrength", "Foam Strength");
        DrawProperty("visual.oceanRefractionStrength", "Refraction");
        DrawProperty("visual.oceanOpacity", "Opacity");
    }

    void DrawLightingTab()
    {
        EditorGUILayout.LabelField("环境与相机", EditorStyles.boldLabel);
        DrawProperty("preview.backgroundColor", "背景");
        DrawProperty("preview.ambientColor", "环境光");
        DrawProperty("preview.cameraYaw", "相机水平角");
        DrawProperty("preview.cameraPitch", "相机俯仰角");
        DrawProperty("preview.cameraDistance", "相机距离");
        DrawProperty("preview.autoRotate", "自动旋转");
        DrawProperty("preview.rotationSpeed", "旋转速度");
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("主光", EditorStyles.boldLabel);
        DrawProperty("preview.sunColor", "颜色");
        DrawProperty("preview.sunEuler", "角度");
        DrawProperty("preview.sunIntensity", "强度");
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("补光", EditorStyles.boldLabel);
        DrawProperty("preview.fillColor", "颜色");
        DrawProperty("preview.fillEuler", "角度");
        DrawProperty("preview.fillIntensity", "强度");
        if (GUILayout.Button("重置预览视角"))
        {
            controller.ResetPreviewOrbit();
            MarkPresetDirty();
        }
    }

    void DrawBottomToolbar()
    {
        if (preset == null)
            return;
        EditorGUILayout.Space(3f);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("随机种子", GUILayout.Height(27f)))
            {
                Undo.RecordObject(preset, "Randomize Planet Seed");
                preset.seed = Guid.NewGuid().GetHashCode();
                MarkPresetDirty();
                controller.RequestRebuild();
            }
            if (GUILayout.Button("重新生成", GUILayout.Height(27f)))
                controller.RebuildNow(preset.previewResolution);
            if (GUILayout.Button("96 高质量", GUILayout.Height(27f)))
                controller.RebuildNow(96);
            if (GUILayout.Button("128 展示", GUILayout.Height(27f)))
                controller.RebuildNow(128);
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("截图 1920×1080", GUILayout.Height(27f)))
                Capture(1920, 1080);
            if (GUILayout.Button("截图 3840×2160", GUILayout.Height(27f)))
                Capture(3840, 2160);
        }
    }

    void DrawProperty(string path, string label)
    {
        SerializedProperty property = serializedPreset.FindProperty(path);
        if (property != null)
            EditorGUILayout.PropertyField(property, new GUIContent(label), true);
    }

    void CreatePreset()
    {
        string path = EditorUtility.SaveFilePanelInProject(
            "新建程序化星球预设",
            "NewPlanetPreset",
            "asset",
            "选择预设保存位置",
            ProceduralPlanetLabAssetBuilder.PresetFolder);
        if (string.IsNullOrEmpty(path))
            return;

        ProceduralPlanetPreset created =
            ScriptableObject.CreateInstance<ProceduralPlanetPreset>();
        created.ApplyTemplate(ProceduralPlanetLabTemplate.TemperateOcean);
        AssetDatabase.CreateAsset(created, path);
        AssetDatabase.SaveAssets();
        SetPreset(created);
    }

    void LoadPreset()
    {
        string fullPath = EditorUtility.OpenFilePanel(
            "载入程序化星球预设",
            Path.GetFullPath(ProceduralPlanetLabAssetBuilder.PresetFolder),
            "asset");
        if (string.IsNullOrEmpty(fullPath))
            return;
        string normalized = fullPath.Replace('\\', '/');
        string dataPath = Application.dataPath.Replace('\\', '/');
        if (!normalized.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase))
        {
            EditorUtility.DisplayDialog(
                "无法载入",
                "预设必须位于当前 Unity 项目的 Assets 目录中。",
                "确定");
            return;
        }
        string assetPath = "Assets" + normalized.Substring(dataPath.Length);
        ProceduralPlanetPreset loaded =
            AssetDatabase.LoadAssetAtPath<ProceduralPlanetPreset>(assetPath);
        if (loaded != null)
            SetPreset(loaded);
    }

    void SavePresetAs()
    {
        if (preset == null)
            return;
        string path = EditorUtility.SaveFilePanelInProject(
            "另存程序化星球预设",
            preset.name + " Copy",
            "asset",
            "选择预设保存位置",
            ProceduralPlanetLabAssetBuilder.PresetFolder);
        if (string.IsNullOrEmpty(path))
            return;
        ProceduralPlanetPreset copy = Instantiate(preset);
        copy.name = Path.GetFileNameWithoutExtension(path);
        AssetDatabase.CreateAsset(copy, path);
        AssetDatabase.SaveAssets();
        SetPreset(copy);
    }

    void Capture(int width, int height)
    {
        int restoreResolution = preset.previewResolution;
        string fileName = string.Format(
            "{0}_{1}_{2}x{3}.png",
            SanitizeFileName(preset.displayName),
            DateTime.Now.ToString("yyyyMMdd_HHmmss"),
            width,
            height);
        string assetPath = ProceduralPlanetLabAssetBuilder.ScreenshotFolder
            + "/" + fileName;
        try
        {
            controller.RebuildNow(128);
            controller.CapturePreview(assetPath, width, height);
        }
        finally
        {
            controller.RebuildNow(restoreResolution);
        }
        AssetDatabase.Refresh();
        UnityEngine.Object screenshot =
            AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
        Selection.activeObject = screenshot;
        EditorGUIUtility.PingObject(screenshot);
        Debug.Log("Planet Lab screenshot: " + assetPath);
    }

    void MarkPresetDirty()
    {
        if (preset == null)
            return;
        preset.ClampValues();
        EditorUtility.SetDirty(preset);
        serializedPreset = new SerializedObject(preset);
    }

    static string SanitizeFileName(string value)
    {
        string result = string.IsNullOrWhiteSpace(value) ? "Planet" : value;
        foreach (char invalid in Path.GetInvalidFileNameChars())
            result = result.Replace(invalid, '_');
        return result;
    }
}
