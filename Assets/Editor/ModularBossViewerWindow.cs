using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class ModularBossViewerWindow : EditorWindow
{
    const string ExpectedSceneName = "UrbanEnvironmentalTrapTest";
    const string MenuRoot = "Tools/Boss/";
    const string FlightTestRequestKey =
        "UnityPlanet.ModularBossFlightTest.Requested";
    static readonly Vector3 OffsitePreviewPosition =
        new Vector3(5000f, 1500f, 5000f);
    static readonly string[] TierLabels =
    {
        "0  矛头·基准", "1  矛头·强化", "2  锤头·封锁",
        "3  锤头·压迫", "4  堡垒·立体", "5  堡垒·终局"
    };

    ModularBossFunctionalTestHarness harness;
    ModularBossFlightTestCourse flightCourse;
    int tier;
    int seed = 7319;
    bool showShield = true;
    Vector2 reportScroll;

    [MenuItem(MenuRoot + "Boss可视化与功能检测", priority = 1)]
    public static void OpenWindow()
    {
        ModularBossViewerWindow window = GetWindow<ModularBossViewerWindow>();
        window.titleContent = new GUIContent("Boss 检测");
        window.minSize = new Vector2(430f, 520f);
        window.Show();
        window.BindOrCreateHarness(true);
    }

    [MenuItem(MenuRoot + "在当前测试场景创建或刷新检测Boss", priority = 20)]
    public static void CreateOrRefreshSceneBoss()
    {
        ModularBossFunctionalTestHarness target = EnsureHarness(true);
        if (target == null)
            return;
        target.RebuildPreview();
        FocusHarness(target);
        Selection.activeGameObject = target.gameObject;
        Debug.Log("Boss 检测节点已创建/刷新；未修改测试场景原有对象。", target);
    }

    [MenuItem(MenuRoot + "导出三种Boss截图", priority = 21)]
    public static void ExportArchetypeScreenshotsMenu()
    {
        ModularBossFunctionalTestHarness target = EnsureHarness(true);
        if (target == null)
            return;
        string[] paths = ExportArchetypeScreenshots(target, 7319, true);
        Debug.Log("Boss 三种船型截图已导出：\n" + string.Join("\n", paths), target);
    }

    [MenuItem(MenuRoot + "在Boss模型原位创建飞行与撞楼检测", priority = 30)]
    public static void CreateFlightTestCourseMenu()
    {
        ModularBossFunctionalTestHarness target = EnsureHarness(true);
        if (target == null)
            return;
        ModularBossFlightTestCourse course = EnsureFlightCourse(target);
        FocusHarness(target);
        Selection.activeGameObject = course.gameObject;
        Debug.Log(
            "Boss 原位试飞已就绪；真实 Boss 将从当前预览模型位置起飞。",
            course);
    }

    [MenuItem(MenuRoot + "开始飞行与撞楼测试", priority = 31)]
    public static void StartFlightTestMenu()
    {
        ModularBossFunctionalTestHarness target = EnsureHarness(true);
        if (target == null)
            return;
        StartFlightTest(
            EnsureFlightCourse(target),
            target.DifficultyTier,
            target.Seed);
    }

    [InitializeOnLoadMethod]
    static void RegisterFlightTestPlayModeHook()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    static void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode &&
            SessionState.GetBool(FlightTestRequestKey, false))
        {
            SessionState.SetBool(FlightTestRequestKey, false);
            ModularBossFlightTestCourse course =
                FindObjectOfType<ModularBossFlightTestCourse>();
            course?.BeginTest();
        }
        else if (state == PlayModeStateChange.ExitingPlayMode)
        {
            SessionState.SetBool(FlightTestRequestKey, false);
        }
    }

    void OnEnable()
    {
        BindOrCreateHarness(false);
    }

    void OnHierarchyChange()
    {
        if (harness == null)
            BindOrCreateHarness(false);
        Repaint();
    }

    void OnInspectorUpdate()
    {
        if (EditorApplication.isPlaying)
            Repaint();
    }

    void OnGUI()
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("真实 Boss PCG 可视化", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "只生成无碰撞、EditorOnly 的诊断预览。它不会加入战斗，不会影响楼房、廊桥、风场、磁场或玩家物理。",
            MessageType.Info);

        bool correctScene = SceneManager.GetActiveScene().name == ExpectedSceneName;
        if (!correctScene)
        {
            EditorGUILayout.HelpBox(
                $"请先打开 {ExpectedSceneName}.unity。工具不会在其他场景自动落节点。",
                MessageType.Warning);
        }

        using (new EditorGUI.DisabledScope(!correctScene))
        {
            if (GUILayout.Button(harness == null ? "创建独立 Boss 检测节点" : "重新绑定检测节点"))
                BindOrCreateHarness(true);

            EditorGUI.BeginChangeCheck();
            tier = EditorGUILayout.Popup("难度 / 船型", tier, TierLabels);
            seed = EditorGUILayout.IntField("外形种子", seed);
            showShield = EditorGUILayout.Toggle("显示动态护盾", showShield);
            bool changed = EditorGUI.EndChangeCheck();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("重新生成") || changed)
                Rebuild();
            if (GUILayout.Button("聚焦 Scene 视图") && harness != null)
                FocusHarness(harness);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("导出当前截图") && harness != null)
            {
                string path = ExportCurrentScreenshot(harness);
                Debug.Log("Boss 截图已导出：" + path, harness);
            }
            if (GUILayout.Button("导出三类截图") && harness != null)
            {
                string[] paths = ExportArchetypeScreenshots(harness, seed, showShield);
                Debug.Log("Boss 三类截图已导出：\n" + string.Join("\n", paths), harness);
            }
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField(
            harness == null ? "未绑定" : harness.ArchetypeName,
            EditorStyles.boldLabel);
        if (harness != null)
        {
            MessageType state = harness.AllChecksPassed
                ? MessageType.Info
                : MessageType.Error;
            EditorGUILayout.HelpBox(harness.TacticalHint, MessageType.None);
            reportScroll = EditorGUILayout.BeginScrollView(reportScroll);
            EditorGUILayout.HelpBox(harness.LastReport, state);
            EditorGUILayout.EndScrollView();
        }

        EditorGUILayout.Space(10f);
        EditorGUILayout.LabelField("Boss 模型原位飞行与撞楼检测", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "这是 UrbanEnvironmentalTrapTest 场景内的常驻循环，和风场、磁场测试同时存在。直接进入 Play Mode 后，真实 Boss 会从模型锚点自动完成追击、升降、受损飞行和撞楼，然后原地复位进入下一轮；不会新建或切换场景。",
            MessageType.Info);
        using (new EditorGUI.DisabledScope(!correctScene))
        {
            if (GUILayout.Button("创建 / 刷新原位检测航线") && harness != null)
            {
                flightCourse = EnsureFlightCourse(harness);
                flightCourse.Configure(tier, seed);
                EditorUtility.SetDirty(flightCourse);
                EditorSceneManager.MarkSceneDirty(flightCourse.gameObject.scene);
                Selection.activeGameObject = flightCourse.gameObject;
                FocusHarness(harness);
            }
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
            {
                if (GUILayout.Button("进入 Play Mode（自动循环）") && harness != null)
                    StartFlightTest(
                        flightCourse ?? EnsureFlightCourse(harness),
                        tier,
                        seed);
            }
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
            {
                bool loopRunning = flightCourse != null &&
                                   flightCourse.RunContinuously;
                string loopButton = loopRunning
                    ? "暂停 Boss 循环"
                    : "恢复 Boss 循环";
                if (GUILayout.Button(loopButton) && flightCourse != null)
                {
                    if (loopRunning)
                        flightCourse.StopLoop();
                    else
                        flightCourse.ConfigureLoop(true);
                }
            }
            EditorGUILayout.EndHorizontal();
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying ||
                                               flightCourse == null))
            {
                if (GUILayout.Button("立即从原位重新开始本轮"))
                    flightCourse.BeginTest();
            }
        }
        if (flightCourse == null)
            flightCourse = FindObjectOfType<ModularBossFlightTestCourse>();
        if (flightCourse != null)
        {
            MessageType courseState = flightCourse.Phase ==
                                      ModularBossFlightTestPhase.Failed
                ? MessageType.Error
                : MessageType.Info;
            EditorGUILayout.HelpBox(flightCourse.LastReport, courseState);
        }
    }

    void BindOrCreateHarness(bool create)
    {
        harness = create ? EnsureHarness(true) : FindHarness();
        if (harness == null)
            return;
        tier = harness.DifficultyTier;
        seed = harness.Seed;
        showShield = harness.ShowShield;
        flightCourse = harness.GetComponent<ModularBossFlightTestCourse>();
        Repaint();
    }

    void Rebuild()
    {
        if (harness == null)
            harness = EnsureHarness(true);
        if (harness == null)
            return;
        Undo.RecordObject(harness, "Configure Boss diagnostic preview");
        harness.ConfigurePreview(tier, seed, showShield);
        EditorUtility.SetDirty(harness);
        EditorSceneManager.MarkSceneDirty(harness.gameObject.scene);
        Repaint();
    }

    static ModularBossFunctionalTestHarness FindHarness()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
            return null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            ModularBossFunctionalTestHarness result =
                root.GetComponent<ModularBossFunctionalTestHarness>();
            if (result != null)
                return result;
        }
        return null;
    }

    static ModularBossFunctionalTestHarness EnsureHarness(bool logWrongScene)
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded || scene.name != ExpectedSceneName)
        {
            if (logWrongScene)
                Debug.LogWarning(
                    $"Boss 检测工具仅会写入 {ExpectedSceneName}.unity；当前场景未改动。");
            return null;
        }

        ModularBossFunctionalTestHarness existing = FindHarness();
        if (existing != null)
            return existing;

        GameObject root = new GameObject(
            ModularBossFunctionalTestHarness.SceneObjectName);
        Undo.RegisterCreatedObjectUndo(root, "Create Boss diagnostic harness");
        root.tag = "EditorOnly";
        root.layer = ModularBossFunctionalTestHarness.PreviewLayer;
        // Keep the diagnostic model completely outside the city's building,
        // bridge, wind and magnetic-field test volume.
        root.transform.position = OffsitePreviewPosition;
        ModularBossFunctionalTestHarness result =
            Undo.AddComponent<ModularBossFunctionalTestHarness>(root);
        EditorSceneManager.MarkSceneDirty(scene);
        result.RebuildPreview();
        return result;
    }

    static ModularBossFlightTestCourse EnsureFlightCourse(
        ModularBossFunctionalTestHarness target)
    {
        if (target == null)
            return null;
        ModularBossFlightTestCourse existing =
            target.GetComponent<ModularBossFlightTestCourse>();
        if (existing != null)
            return existing;
        Undo.RecordObject(target.gameObject, "Create Boss flight test course");
        ModularBossFlightTestCourse result =
            Undo.AddComponent<ModularBossFlightTestCourse>(target.gameObject);
        result.Configure(target.DifficultyTier, target.Seed);
        result.ConfigureLoop(true);
        EditorUtility.SetDirty(result);
        EditorSceneManager.MarkSceneDirty(target.gameObject.scene);
        return result;
    }

    static void StartFlightTest(
        ModularBossFlightTestCourse course,
        int tier,
        int testSeed)
    {
        if (course == null)
            return;
        course.Configure(tier, testSeed);
        course.ConfigureLoop(true);
        EditorUtility.SetDirty(course);
        if (EditorApplication.isPlaying)
        {
            course.BeginTest();
            return;
        }
        SessionState.SetBool(FlightTestRequestKey, true);
        EditorApplication.isPlaying = true;
    }

    static void FocusHarness(ModularBossFunctionalTestHarness target)
    {
        if (target == null || SceneView.lastActiveSceneView == null)
            return;
        SceneView.lastActiveSceneView.Frame(target.PreviewBounds, false);
        SceneView.lastActiveSceneView.Repaint();
    }

    static string ExportCurrentScreenshot(ModularBossFunctionalTestHarness target)
    {
        if (target.CurrentBuild == null)
            target.RebuildPreview();
        string archetype = target.CurrentBuild == null
            ? "generation_failed"
            : target.CurrentBuild.HullArchetype.ToString().ToLowerInvariant();
        string path = Path.Combine(
            ResolveScreenshotDirectory(),
            $"boss_{archetype}_tier{target.DifficultyTier}_seed{target.Seed}.png");
        RenderScreenshot(target, path);
        return path.Replace('\\', '/');
    }

    static string[] ExportArchetypeScreenshots(
        ModularBossFunctionalTestHarness target,
        int baseSeed,
        bool shieldVisible)
    {
        int originalTier = target.DifficultyTier;
        int originalSeed = target.Seed;
        bool originalShield = target.ShowShield;
        int[] tiers = { 0, 2, 5 };
        string[] paths = new string[tiers.Length];
        try
        {
            for (int index = 0; index < tiers.Length; index++)
            {
                int previewSeed = baseSeed + index * 101;
                target.ConfigurePreview(tiers[index], previewSeed, shieldVisible);
                paths[index] = ExportCurrentScreenshot(target);
            }
        }
        finally
        {
            target.ConfigurePreview(originalTier, originalSeed, originalShield);
        }
        return paths;
    }

    static string ResolveScreenshotDirectory()
    {
        string directory = Path.GetFullPath(Path.Combine(
            Application.dataPath, "..", "Artifacts", "BossPreviews"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    static void RenderScreenshot(
        ModularBossFunctionalTestHarness target,
        string outputPath)
    {
        Bounds bounds = target.PreviewBounds;
        float radius = Mathf.Max(12f, bounds.extents.magnitude);
        Vector3 viewVector = new Vector3(1.18f, 0.72f, -1.42f).normalized;
        if (target.CurrentBuild != null &&
            target.CurrentBuild.HullArchetype == ModularBossHullArchetype.Hammerhead)
            viewVector = new Vector3(0.92f, 0.66f, -1.55f).normalized;
        else if (target.CurrentBuild != null &&
                 target.CurrentBuild.HullArchetype == ModularBossHullArchetype.Citadel)
            viewVector = new Vector3(1.22f, 0.52f, -1.28f).normalized;

        GameObject cameraObject = new GameObject("BossPreviewCaptureCamera")
        {
            hideFlags = HideFlags.HideAndDontSave,
            layer = ModularBossFunctionalTestHarness.PreviewLayer
        };
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.008f, 0.022f, 0.048f, 1f);
        camera.cullingMask = 1 << ModularBossFunctionalTestHarness.PreviewLayer;
        camera.fieldOfView = 34f;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = radius * 12f;
        camera.allowHDR = true;
        camera.transform.position = bounds.center + viewVector * radius * 2.65f;
        camera.transform.LookAt(bounds.center, Vector3.up);

        GameObject keyObject = CreateCaptureLight(
            "BossPreviewKey", LightType.Directional,
            new Color(0.72f, 0.86f, 1f), 1.65f);
        keyObject.transform.rotation = Quaternion.Euler(38f, -42f, 0f);
        GameObject rimObject = CreateCaptureLight(
            "BossPreviewRim", LightType.Point,
            new Color(0.05f, 0.62f, 1f), 4.4f);
        rimObject.transform.position = bounds.center - viewVector * radius * 0.35f +
                                       Vector3.up * radius * 0.7f;
        rimObject.GetComponent<Light>().range = radius * 5f;

        RenderTexture renderTexture = new RenderTexture(1280, 720, 24,
            RenderTextureFormat.ARGB32)
        {
            antiAliasing = 4,
            hideFlags = HideFlags.HideAndDontSave
        };
        Texture2D image = new Texture2D(1280, 720, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        RenderTexture previous = RenderTexture.active;
        try
        {
            camera.targetTexture = renderTexture;
            camera.Render();
            RenderTexture.active = renderTexture;
            image.ReadPixels(new Rect(0f, 0f, 1280f, 720f), 0, 0);
            image.Apply(false, false);
            File.WriteAllBytes(outputPath, image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            camera.targetTexture = null;
            DestroyImmediate(image);
            DestroyImmediate(renderTexture);
            DestroyImmediate(cameraObject);
            DestroyImmediate(keyObject);
            DestroyImmediate(rimObject);
        }
    }

    static GameObject CreateCaptureLight(
        string objectName,
        LightType type,
        Color color,
        float intensity)
    {
        GameObject result = new GameObject(objectName)
        {
            hideFlags = HideFlags.HideAndDontSave,
            layer = ModularBossFunctionalTestHarness.PreviewLayer
        };
        Light light = result.AddComponent<Light>();
        light.type = type;
        light.color = color;
        light.intensity = intensity;
        light.cullingMask = 1 << ModularBossFunctionalTestHarness.PreviewLayer;
        light.shadows = LightShadows.Soft;
        return result;
    }
}
