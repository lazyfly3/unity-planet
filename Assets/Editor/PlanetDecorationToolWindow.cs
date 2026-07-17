using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class PlanetDecorationToolWindow : EditorWindow
{
    const string DefaultPackagePath = @"D:\untiy素材\0072lowpoly森林岛屿沙漠\Lowpoly Style Ultra Pack 1.2.unitypackage";

    string packagePath = DefaultPackagePath;
    PlanetDecorationCatalog catalog;
    GameObject sourceModel;
    PlanetClimateMask climates = PlanetClimateMask.TemperateForest;
    PlanetDecorationRole role = PlanetDecorationRole.Vegetation;
    Vector3 axisCorrection;
    float previewScale = 1f;
    float surfaceOffset;
    bool orientationConfirmed;
    bool makeHarvestable;
    WorldItem rewardPrefab;
    Vector2 scroll;
    
    int currentStep;
    bool showAdvancedOptions;
    bool showPackageOptions;
    static readonly string[] StepNames = { "1  准备素材", "2  添加模型", "3  查看目录", "4  运行检查" };
    static readonly string[] RoleNames = { "植物", "岩石", "贴地小物", "大型地标" };
    string validationMessage;

    [MenuItem("Tools/体素星球/星球装饰工具")]
    static void Open()
    {
        GetWindow<PlanetDecorationToolWindow>("星球装饰工具");
    }

    void OnEnable()
    {
        catalog = AssetDatabase.LoadAssetAtPath<PlanetDecorationCatalog>(PlanetDecorationToolBootstrap.CatalogPath);
    }



    void OnGUI()
    {
        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("星球低多边形装饰工具", new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 17,
            alignment = TextAnchor.MiddleCenter
        });
        EditorGUILayout.HelpBox("按照上方 1 → 4 的顺序操作。普通情况下不需要修改高级参数。", MessageType.Info);
        EditorGUILayout.Space(4f);

        currentStep = GUILayout.Toolbar(currentStep, StepNames, new GUIStyle(EditorStyles.toolbarButton)
        {
            fixedHeight = 30f,
            fontSize = 12
        });

        EditorGUILayout.Space(8f);
        scroll = EditorGUILayout.BeginScrollView(scroll);
        switch (currentStep)
        {
            case 0: DrawImportSection(); break;
            case 1: DrawWrapperSection(); break;
            case 2: DrawCatalogSection(); break;
            default: DrawRuntimeSection(); break;
        }
        EditorGUILayout.EndScrollView();
    }

    void DrawImportSection()
    {
        DrawStepTitle("第一步：准备素材", "素材已经导入时，只需点击“重新建立默认目录”。");

        bool packImported = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Lowpoly Style/ForestPack/Prefabs/Tree1.prefab") != null;
        bool catalogReady = catalog != null && catalog.Entries.Count > 0;

        if (packImported && catalogReady)
            EditorGUILayout.HelpBox($"准备完成：已经导入素材，并建立 {catalog.Entries.Count} 个装饰模型。", MessageType.Info);
        else if (packImported)
            EditorGUILayout.HelpBox("素材已经导入，但装饰目录还没有建立。", MessageType.Warning);
        else
            EditorGUILayout.HelpBox("尚未导入低多边形素材。点击下面的按钮即可自动完成。", MessageType.Warning);

        string mainButton = packImported ? "重新建立默认七气候装饰目录" : "一键导入素材并建立装饰目录";
        if (GUILayout.Button(mainButton, GUILayout.Height(38f)))
        {
            if (!packImported)
            {
                if (!File.Exists(packagePath))
                {
                    validationMessage = "找不到素材包，请展开“更换素材包位置”后重新选择。";
                    return;
                }
                AssetDatabase.ImportPackage(packagePath, false);
            }

            catalog = PlanetDecorationToolBootstrap.SetupProject();
            bool valid = PlanetDecorationToolBootstrap.RunAutomatedValidation(out string report);
            validationMessage = valid ? "准备完成，七种气候的装饰目录可以使用。" : "目录检查未通过，请查看 Unity 控制台中的详细信息。";
        }

        EditorGUILayout.Space(8f);
        showPackageOptions = EditorGUILayout.Foldout(showPackageOptions, "更换素材包位置（一般不需要）", true);
        if (showPackageOptions)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    packagePath = EditorGUILayout.TextField("素材包文件", packagePath);
                    if (GUILayout.Button("选择文件", GUILayout.Width(76f)))
                    {
                        string selected = EditorUtility.OpenFilePanel(
                            "选择低多边形素材包",
                            Path.GetDirectoryName(packagePath),
                            "unitypackage");
                        if (!string.IsNullOrEmpty(selected))
                            packagePath = selected;
                    }
                }
                EditorGUILayout.HelpBox("只使用当前轻量素材包，不会导入 861MB 草原大包，也不会修改素材原文件。", MessageType.None);
            }
        }

        DrawStatusMessage();
        if (catalogReady && GUILayout.Button("下一步：添加自己的模型", GUILayout.Height(30f)))
            currentStep = 1;
    }

    void DrawWrapperSection()
    {
        DrawStepTitle("第二步：添加装饰模型", "拖入模型，选择类型和气候，确认方向，然后加入目录。");

        Rect dropRect = GUILayoutUtility.GetRect(0f, 72f, GUILayout.ExpandWidth(true));
        GUI.Box(
            dropRect,
            sourceModel == null
                ? "把模型拖到这里" + Environment.NewLine + "（预制体或模型文件）"
                : "当前模型：" + sourceModel.name,
            new GUIStyle(EditorStyles.helpBox)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 13
            });
        HandleDrop(dropRect);

        sourceModel = (GameObject)EditorGUILayout.ObjectField("选择模型", sourceModel, typeof(GameObject), false);
        EditorGUILayout.Space(6f);

        EditorGUILayout.LabelField("这个模型是什么？", EditorStyles.boldLabel);
        role = (PlanetDecorationRole)GUILayout.SelectionGrid((int)role, RoleNames, 4, GUILayout.Height(32f));
        DrawRoleExplanation();

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("允许出现在哪些气候？", EditorStyles.boldLabel);
        DrawClimateSelector();

        EditorGUILayout.Space(6f);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            previewScale = Mathf.Max(0.01f, EditorGUILayout.FloatField("大小倍率", previewScale));
            surfaceOffset = Mathf.Clamp(
                EditorGUILayout.FloatField("贴地微调（负数下沉）", surfaceOffset),
                -5f,
                5f);

            showAdvancedOptions = EditorGUILayout.Foldout(showAdvancedOptions, "高级：模型轴向修正", true);
            if (showAdvancedOptions)
                axisCorrection = EditorGUILayout.Vector3Field("旋转修正", axisCorrection);
        }

        DrawPreview();

        string confirmation = role == PlanetDecorationRole.Vegetation
            ? "我已确认：根部在蓝线，树冠朝绿色箭头"
            : "我已确认：模型没有横躺、倒插或悬空";
        orientationConfirmed = EditorGUILayout.ToggleLeft(confirmation, orientationConfirmed);

        bool alreadyHarvestable = sourceModel != null
            && sourceModel.GetComponentInChildren<HarvestableResource>(true) != null;
        if (alreadyHarvestable)
        {
            EditorGUILayout.HelpBox("这个模型已经带有采集组件，将自动作为采集物，不根据名称判断。", MessageType.Info);
            makeHarvestable = false;
        }
        else
        {
            makeHarvestable = EditorGUILayout.ToggleLeft("这是可以被玩家采集的物品", makeHarvestable);
            if (makeHarvestable)
            {
                rewardPrefab = (WorldItem)EditorGUILayout.ObjectField(
                    "采集后获得的物品",
                    rewardPrefab,
                    typeof(WorldItem),
                    false);
                if (rewardPrefab == null)
                    EditorGUILayout.HelpBox("请指定玩家采集后得到的物品。", MessageType.Warning);
            }
        }

        bool canAdd = sourceModel != null
            && climates != PlanetClimateMask.None
            && orientationConfirmed
            && (!makeHarvestable || rewardPrefab != null);
        using (new EditorGUI.DisabledScope(!canAdd))
        {
            if (GUILayout.Button("确认方向并加入星球装饰目录", GUILayout.Height(40f)))
            {
                catalog = PlanetDecorationToolBootstrap.EnsureCatalog();
                string stableId = PlanetDecorationToolBootstrap.CreateStableId(sourceModel);
                GameObject wrapper = PlanetDecorationToolBootstrap.CreateNormalizedWrapper(
                    sourceModel,
                    stableId,
                    role,
                    axisCorrection,
                    orientationConfirmed,
                    makeHarvestable,
                    rewardPrefab,
                    out Bounds bounds,
                    out bool axisClear);
                if (wrapper != null)
                {
                    PlanetDecorationEntry entry = PlanetDecorationToolBootstrap.CreateEntry(
                        stableId,
                        wrapper,
                        climates,
                        role,
                        previewScale,
                        surfaceOffset,
                        orientationConfirmed);
                    PlanetDecorationToolBootstrap.AddOrReplaceEntry(catalog, entry);
                    Selection.activeObject = wrapper;
                    validationMessage = $"已加入“{sourceModel.name}”。模型底部已经贴到地面。";
                    currentStep = 2;
                }
            }
        }

        if (!canAdd)
            EditorGUILayout.HelpBox("需要：选择模型、至少一种气候，并勾选方向确认。采集物还需要指定奖励。", MessageType.None);
        DrawStatusMessage();
    }

    void DrawPreview()
    {
        EditorGUILayout.LabelField("方向预览", EditorStyles.boldLabel);
        Rect rect = GUILayoutUtility.GetRect(160f, 220f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, new Color(0.08f, 0.09f, 0.11f, 1f));
        if (sourceModel != null)
        {
            Texture preview = AssetPreview.GetAssetPreview(sourceModel) ?? AssetPreview.GetMiniThumbnail(sourceModel);
            if (preview != null)
                GUI.DrawTexture(rect, preview, ScaleMode.ScaleToFit, true);
            if (AssetPreview.IsLoadingAssetPreview(sourceModel.GetInstanceID()))
                Repaint();
        }
        else
        {
            GUI.Label(rect, "请先选择模型", new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 13
            });
        }

        Vector2 origin = new Vector2(rect.x + 30f, rect.yMax - 28f);
        Vector2 upEnd = origin + Vector2.up * 100f;
        Handles.BeginGUI();
        Handles.color = Color.green;
        Handles.DrawAAPolyLine(4f, origin, upEnd);
        Handles.DrawAAPolyLine(4f, upEnd, upEnd + new Vector2(-7f, -12f), upEnd, upEnd + new Vector2(7f, -12f));
        Handles.color = new Color(0.25f, 0.75f, 1f, 1f);
        Handles.DrawAAPolyLine(3f, new Vector2(rect.x + 8f, origin.y), new Vector2(rect.xMax - 8f, origin.y));
        Handles.EndGUI();
        GUI.Label(new Rect(origin.x + 9f, upEnd.y - 9f, 125f, 22f), "绿色：朝星球外");
        GUI.Label(new Rect(rect.xMax - 126f, origin.y - 20f, 120f, 22f), "蓝色：地面");
    }

    void DrawCatalogSection()
    {
        DrawStepTitle("第三步：查看装饰目录", "这里只显示容易理解的汇总，不再暴露英文开发字段。");

        if (catalog == null)
        {
            EditorGUILayout.HelpBox("还没有装饰目录，请先完成第一步。", MessageType.Warning);
            if (GUILayout.Button("返回第一步"))
                currentStep = 0;
            return;
        }

        int decorativeCount = 0;
        int harvestableCount = 0;
        foreach (PlanetDecorationEntry entry in catalog.Entries)
        {
            if (entry == null || entry.prefab == null)
                continue;
            if (entry.IsHarvestable) harvestableCount++;
            else decorativeCount++;
        }

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField($"目录中共有 {catalog.Entries.Count} 种模型", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("普通装饰", decorativeCount.ToString());
            EditorGUILayout.LabelField("采集物", harvestableCount.ToString());
        }

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("每颗星球的生成数量", EditorStyles.boldLabel);
        foreach (PlanetClimateProfile profile in catalog.Profiles)
        {
            if (profile == null)
                continue;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(GetClimateName(profile.climate), GUILayout.Width(86f));
                EditorGUILayout.LabelField($"普通 {profile.decorationCount} 个");
                EditorGUILayout.LabelField($"采集 {profile.harvestableCount} 个");
            }
        }

        EditorGUILayout.Space(6f);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("检查目录是否正确", GUILayout.Height(30f)))
            {
                bool valid = PlanetDecorationToolBootstrap.RunAutomatedValidation(out string report);
                validationMessage = valid
                    ? "检查通过：七种气候、模型方向和采集分类都可以使用。"
                    : "检查未通过，请查看 Unity 控制台中的详细信息。";
            }
            if (GUILayout.Button("在项目窗口中找到目录", GUILayout.Height(30f)))
                EditorGUIUtility.PingObject(catalog);
        }

        showAdvancedOptions = EditorGUILayout.Foldout(showAdvancedOptions, "高级：更换装饰配置文件", true);
        if (showAdvancedOptions)
            catalog = (PlanetDecorationCatalog)EditorGUILayout.ObjectField(
                "装饰配置文件",
                catalog,
                typeof(PlanetDecorationCatalog),
                false);

        DrawStatusMessage();
        if (GUILayout.Button("下一步：进入运行检查", GUILayout.Height(30f)))
            currentStep = 3;
    }

    void DrawRuntimeSection()
    {
        DrawStepTitle("第四步：运行检查", "进入 Unity 运行模式并来到星球后，可以重新生成和查看失败原因。");

        VoxelQuadSphereWorld world = Application.isPlaying ? FindObjectOfType<VoxelQuadSphereWorld>() : null;
        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("现在还没有运行。请点击 Unity 顶部的 ▶ 播放按钮，然后进入一颗星球。", MessageType.Info);
            return;
        }
        if (world == null)
        {
            EditorGUILayout.HelpBox("正在运行，但当前场景里没有星球地形。请先进入一颗星球。", MessageType.Warning);
            return;
        }

        PlanetSurfaceDecorationSystem system = world.SurfaceDecorationSystem;
        Transform ordinaryRoot = world.transform.Find("GeneratedDecorations");
        Transform harvestRoot = world.transform.Find("GeneratedHarvestableResources");

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField(
                world.IsGenerationComplete ? "生成状态：已完成" : "生成状态：正在生成……",
                EditorStyles.boldLabel);
            EditorGUILayout.LabelField("普通装饰", ordinaryRoot != null ? ordinaryRoot.childCount + " 个" : "0 个");
            EditorGUILayout.LabelField("采集物", harvestRoot != null ? harvestRoot.childCount + " 个" : "0 个");
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("重新生成当前星球", GUILayout.Height(34f)))
                world.RespawnSurfacePropsForPreview();
            if (GUILayout.Button("清除全部装饰", GUILayout.Height(34f)))
                world.ClearGeneratedSurfaceProps();
        }

        if (system == null)
            return;

        system.ShowRejectedCandidates = EditorGUILayout.ToggleLeft(
            "在场景窗口中显示被拒绝的位置（红点）",
            system.ShowRejectedCandidates);
        if (GUI.changed)
            SceneView.RepaintAll();

        if (system.RejectionCounts.Count > 0)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("没有放置成功的原因", EditorStyles.boldLabel);
            foreach (KeyValuePair<string, int> reason in system.RejectionCounts)
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(GetRejectionName(reason.Key));
                    EditorGUILayout.LabelField(reason.Value + " 次", GUILayout.Width(72f));
                }
            }
        }
    }

    void DrawStepTitle(string title, string description)
    {
        EditorGUILayout.LabelField(title, new GUIStyle(EditorStyles.boldLabel) { fontSize = 15 });
        EditorGUILayout.LabelField(description, EditorStyles.wordWrappedLabel);
        EditorGUILayout.Space(6f);
    }

    void DrawStatusMessage()
    {
        if (string.IsNullOrEmpty(validationMessage))
            return;
        MessageType type = validationMessage.Contains("未通过") || validationMessage.Contains("找不到")
            ? MessageType.Error
            : MessageType.Info;
        EditorGUILayout.HelpBox(validationMessage, type);
    }

    void DrawRoleExplanation()
    {
        string text;
        switch (role)
        {
            case PlanetDecorationRole.Vegetation:
                text = "树木、仙人掌、棕榈等。始终朝星球外生长，只在树干附近产生碰撞。";
                break;
            case PlanetDecorationRole.Rock:
                text = "石块、矿石等。会贴合地面，并使用贴合可见模型的凸碰撞。";
                break;
            case PlanetDecorationRole.GroundCover:
                text = "草、花、蘑菇、灌木等。成簇生成，不产生碰撞。";
                break;
            default:
                text = "大型特殊景物。数量较少，与其他装饰保持更远距离。";
                break;
        }
        EditorGUILayout.HelpBox(text, MessageType.None);
    }

    void DrawClimateSelector()
    {
        PlanetClimate[] values =
        {
            PlanetClimate.Barren,
            PlanetClimate.TemperateForest,
            PlanetClimate.Desert,
            PlanetClimate.Tropical,
            PlanetClimate.Tundra,
            PlanetClimate.Volcanic,
            PlanetClimate.Crystal
        };

        for (int row = 0; row < 4; row++)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                for (int column = 0; column < 2; column++)
                {
                    int index = row * 2 + column;
                    if (index >= values.Length)
                    {
                        GUILayout.FlexibleSpace();
                        continue;
                    }

                    PlanetClimate climate = values[index];
                    PlanetClimateMask mask = (PlanetClimateMask)(1 << (int)climate);
                    bool selected = (climates & mask) != 0;
                    bool next = EditorGUILayout.ToggleLeft(
                        GetClimateName(climate),
                        selected,
                        GUILayout.MinWidth(120f));
                    if (next) climates |= mask;
                    else climates &= ~mask;
                }
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("全部选择")) climates = PlanetClimateMask.All;
            if (GUILayout.Button("全部取消")) climates = PlanetClimateMask.None;
        }
    }

    static string GetClimateName(PlanetClimate climate)
    {
        switch (climate)
        {
            case PlanetClimate.Barren: return "荒芜岩地";
            case PlanetClimate.TemperateForest: return "温带森林";
            case PlanetClimate.Desert: return "沙漠";
            case PlanetClimate.Tropical: return "热带";
            case PlanetClimate.Tundra: return "冻原";
            case PlanetClimate.Volcanic: return "火山";
            case PlanetClimate.Crystal: return "晶体";
            default: return "未知气候";
        }
    }

    static string GetRejectionName(string key)
    {
        if (key.Contains("orientationVerified")) return "植物方向没有确认";
        if (key.Contains("surfaceHit")) return "没有找到地面";
        if (key.Contains("maximumSlope")) return "地面坡度太大";
        if (key.Contains("waterClearance")) return "离河流或水面太近";
        if (key.Contains("playerClearRadius")) return "离玩家出生点太近";
        if (key.Contains("minimumSpacing")) return "离其他装饰太近";
        if (key.Contains("overlaps")) return "与场景里的物体重叠";
        return "其他原因";
    }

    void HandleDrop(Rect rect)
    {
        Event evt = Event.current;
        if (!rect.Contains(evt.mousePosition) || (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform))
            return;
        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        if (evt.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();
            foreach (UnityEngine.Object value in DragAndDrop.objectReferences)
            {
                if (value is GameObject gameObject)
                {
                    sourceModel = gameObject;
                    break;
                }
            }
        }
        evt.Use();
    }
}

[InitializeOnLoad]
public static class PlanetDecorationToolBootstrap
{
    public const string CatalogPath = "Assets/PlanetDecoration/PlanetDecorationCatalog.asset";
    public const string WrapperFolder = "Assets/PlanetDecoration/GeneratedPrefabs";
    public const string StarScenePath = "Assets/Scenes/star.unity";
    const string PackagePath = @"D:\untiy素材\0072lowpoly森林岛屿沙漠\Lowpoly Style Ultra Pack 1.2.unitypackage";
    const string ImportedProbe = "Assets/Lowpoly Style/ForestPack/Prefabs/Tree1.prefab";
    static readonly string SetupKey = "PlanetDecoration.Setup." + Application.dataPath.GetHashCode();
    static readonly string ImportKey = "PlanetDecoration.Import." + Application.dataPath.GetHashCode();
    static bool running;

    sealed class CuratedSpec
    {
        public string id;
        public string path;
        public PlanetClimateMask climates;
        public PlanetDecorationRole role;
        public float weight;
        public float scaleMin;
        public float scaleMax;
        public bool verified;

        public CuratedSpec(
            string valueId,
            string valuePath,
            PlanetClimateMask valueClimates,
            PlanetDecorationRole valueRole,
            float valueWeight = 1f,
            float valueScaleMin = 0.85f,
            float valueScaleMax = 1.2f,
            bool valueVerified = true)
        {
            id = valueId;
            path = valuePath;
            climates = valueClimates;
            role = valueRole;
            weight = valueWeight;
            scaleMin = valueScaleMin;
            scaleMax = valueScaleMax;
            verified = valueVerified;
        }
    }

    static PlanetDecorationToolBootstrap()
    {
        EditorApplication.delayCall += TryAutomaticSetup;
    }

    [MenuItem("Tools/体素星球/重新建立默认装饰目录")]
    public static PlanetDecorationCatalog SetupProject()
    {
        if (running)
            return AssetDatabase.LoadAssetAtPath<PlanetDecorationCatalog>(CatalogPath);
        running = true;
        try
        {
            EnsureFolders();
            PlanetDecorationCatalog valueCatalog = EnsureCatalog();
            List<PlanetDecorationEntry> entries = BuildCuratedEntries();
            valueCatalog.ReplaceContents(BuildProfiles(), entries);
            EditorUtility.SetDirty(valueCatalog);
            AssetDatabase.SaveAssets();
            ConfigureStarScene(valueCatalog);
            EditorPrefs.SetBool(SetupKey, true);
            Debug.Log($"PlanetDecorationTool: configured {entries.Count} normalized entries in {CatalogPath}.");
            return valueCatalog;
        }
        finally
        {
            running = false;
        }
    }

    public static PlanetDecorationCatalog EnsureCatalog()
    {
        EnsureFolders();
        PlanetDecorationCatalog value = AssetDatabase.LoadAssetAtPath<PlanetDecorationCatalog>(CatalogPath);
        if (value != null && MonoScript.FromScriptableObject(value) == null)
        {
            AssetDatabase.DeleteAsset(CatalogPath);
            value = null;
        }
        if (value != null)
            return value;
        value = ScriptableObject.CreateInstance<PlanetDecorationCatalog>();
        AssetDatabase.CreateAsset(value, CatalogPath);
        AssetDatabase.SaveAssets();
        return value;
    }

    public static string CreateStableId(GameObject source)
    {
        string path = source != null ? AssetDatabase.GetAssetPath(source) : string.Empty;
        string guid = AssetDatabase.AssetPathToGUID(path);
        string name = source != null ? source.name.ToLowerInvariant() : "decoration";
        foreach (char invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '-');
        name = name.Replace(' ', '-').Replace('_', '-');
        return string.IsNullOrEmpty(guid) ? name : name + "-" + guid.Substring(0, 8);
    }

    public static GameObject CreateNormalizedWrapper(
        GameObject source,
        string stableId,
        PlanetDecorationRole role,
        Vector3 manualCorrection,
        bool orientationConfirmed,
        bool makeHarvestable,
        WorldItem reward,
        out Bounds normalizedBounds,
        out bool axisClear)
    {
        normalizedBounds = new Bounds(Vector3.zero, Vector3.zero);
        axisClear = role != PlanetDecorationRole.Vegetation;
        if (source == null || string.IsNullOrWhiteSpace(stableId))
            return null;

        EnsureFolders();
        GameObject root = new GameObject(SanitizeFileName(stableId) + "_Normalized");
        try
        {
            GameObject child = PrefabUtility.InstantiatePrefab(source) as GameObject;
            if (child == null)
                child = UnityEngine.Object.Instantiate(source);
            child.name = source.name;
            child.transform.SetParent(root.transform, false);
            child.transform.localPosition = Vector3.zero;
            child.transform.localRotation = Quaternion.Euler(manualCorrection) * child.transform.localRotation;

            Bounds beforeAxis = CalculateLocalRendererBounds(root.transform);
            Quaternion automaticCorrection = Quaternion.identity;
            if (orientationConfirmed)
                axisClear = true;
            else
                automaticCorrection = DeterminePlantAxisCorrection(beforeAxis, role, out axisClear);
            child.transform.localRotation = automaticCorrection * child.transform.localRotation;

            Bounds beforeGrounding = CalculateLocalRendererBounds(root.transform);
            child.transform.localPosition += Vector3.up * -beforeGrounding.min.y;
            normalizedBounds = CalculateLocalRendererBounds(root.transform);

            PlanetDecorationAnchor anchor = root.AddComponent<PlanetDecorationAnchor>();
            anchor.sourcePrefab = source;
            anchor.role = role;
            anchor.orientationVerified = orientationConfirmed;
            anchor.localBounds = normalizedBounds;

            HarvestableResource harvestable = root.GetComponentInChildren<HarvestableResource>(true);
            if (makeHarvestable && harvestable == null)
                harvestable = root.AddComponent<HarvestableResource>();
            if (makeHarvestable && harvestable != null && reward != null)
            {
                SerializedObject serializedHarvestable = new SerializedObject(harvestable);
                serializedHarvestable.FindProperty("rewardItemPrefab").objectReferenceValue = reward;
                serializedHarvestable.ApplyModifiedPropertiesWithoutUndo();
            }

            string path = WrapperFolder + "/" + SanitizeFileName(stableId) + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    public static PlanetDecorationEntry CreateEntry(
        string stableId,
        GameObject wrapper,
        PlanetClimateMask climates,
        PlanetDecorationRole role,
        float baseScale,
        float surfaceOffset,
        bool orientationVerified)
    {
        bool small = role == PlanetDecorationRole.GroundCover;
        bool clustered = role == PlanetDecorationRole.Vegetation || small;
        return new PlanetDecorationEntry
        {
            stableId = stableId,
            prefab = wrapper,
            climates = climates,
            role = role,
            weight = 1f,
            minimumScale = baseScale * 0.85f,
            maximumScale = baseScale * 1.2f,
            surfaceOffset = surfaceOffset,
            minimumSpacing = small ? 1.15f : role == PlanetDecorationRole.Landmark ? 14f : role == PlanetDecorationRole.Rock ? 2.8f : 3.2f,
            playerClearRadius = 8f,
            placementAttempts = 36,
            maximumSlope = role == PlanetDecorationRole.Vegetation ? 28f : small ? 34f : role == PlanetDecorationRole.Landmark ? 18f : 48f,
            waterClearance = role == PlanetDecorationRole.GroundCover ? 0.45f : 1f,
            clusterRadius = clustered ? (small ? 6f : 11f) : 0f,
            clusterSize = clustered ? (small ? 18 : 10) : 1,
            alignment = role == PlanetDecorationRole.Vegetation ? PlanetDecorationAlignment.GravityUp : PlanetDecorationAlignment.SurfaceNormal,
            collision = small ? PlanetDecorationCollision.None : PlanetDecorationCollision.Simplified,
            randomizeYaw = true,
            orientationVerified = orientationVerified
        };
    }

    public static void AddOrReplaceEntry(PlanetDecorationCatalog catalog, PlanetDecorationEntry newEntry)
    {
        if (catalog == null || newEntry == null)
            return;
        var profiles = new List<PlanetClimateProfile>();
        foreach (PlanetClimateProfile profile in catalog.Profiles)
            profiles.Add(profile);
        var entries = new List<PlanetDecorationEntry>();
        bool replaced = false;
        foreach (PlanetDecorationEntry entry in catalog.Entries)
        {
            if (entry != null && string.Equals(entry.stableId, newEntry.stableId, StringComparison.Ordinal))
            {
                entries.Add(newEntry);
                replaced = true;
            }
            else
                entries.Add(entry);
        }
        if (!replaced)
            entries.Add(newEntry);
        catalog.ReplaceContents(profiles, entries);
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Tools/体素星球/检查星球装饰目录")]
    public static void ValidateFromMenu()
    {
        bool valid = RunAutomatedValidation(out string report);
        if (valid)
            Debug.Log("星球装饰工具检查通过：" + report);
        else
            Debug.LogError("星球装饰工具检查未通过：" + report);
    }

    static string GetClimateName(PlanetClimate climate)
    {
        switch (climate)
        {
            case PlanetClimate.Barren: return "荒芜岩地";
            case PlanetClimate.TemperateForest: return "温带森林";
            case PlanetClimate.Desert: return "沙漠";
            case PlanetClimate.Tropical: return "热带";
            case PlanetClimate.Tundra: return "冻原";
            case PlanetClimate.Volcanic: return "火山";
            case PlanetClimate.Crystal: return "晶体";
            default: return "未知气候";
        }
    }


    public static bool RunAutomatedValidation(out string report)
    {
        var errors = new List<string>();
        PlanetDecorationCatalog catalog = AssetDatabase.LoadAssetAtPath<PlanetDecorationCatalog>(CatalogPath);
        if (catalog == null)
        {
            report = "找不到星球装饰目录文件。";
            return false;
        }

        catalog.ValidateCatalog(errors);
        foreach (PlanetClimateProfile profile in catalog.Profiles)
        {
            if (profile.decorationCount < 450 || profile.decorationCount > 700)
                errors.Add($"{GetClimateName(profile.climate)}的普通装饰数量不在 450–700 范围内。");
            if (profile.harvestableCount < 20 || profile.harvestableCount > 50)
                errors.Add($"{GetClimateName(profile.climate)}的采集物数量不在 20–50 范围内。");
        }

        ValidateClassifier(errors);
        foreach (PlanetClimate climate in Enum.GetValues(typeof(PlanetClimate)))
        {
            List<PlanetSurfacePropSpawnSettings> first = catalog.BuildSpawnPlan(climate, 24681357);
            List<PlanetSurfacePropSpawnSettings> second = catalog.BuildSpawnPlan(climate, 24681357);
            if (first.Count != second.Count)
                errors.Add($"{GetClimateName(climate)}使用相同种子时生成结果不一致。");
            for (int i = 0; i < Mathf.Min(first.Count, second.Count); i++)
            {
                if (first[i].catalogId != second[i].catalogId || first[i].count != second[i].count || first[i].seedOffset != second[i].seedOffset)
                    errors.Add($"{GetClimateName(climate)}的第 {i} 个生成条目不一致。");
            }
        }

        foreach (PlanetDecorationEntry entry in catalog.Entries)
        {
            if (entry == null || entry.prefab == null)
                continue;
            PlanetDecorationAnchor anchor = entry.prefab.GetComponent<PlanetDecorationAnchor>();
            if (anchor == null)
                errors.Add(entry.stableId + " 没有使用标准化包装模型。");
            else
            {
                if (Mathf.Abs(anchor.localBounds.min.y) > 0.015f)
                    errors.Add(entry.stableId + " 的底部没有贴到本地 Y=0 地面。");
                if (entry.role == PlanetDecorationRole.Vegetation && (!anchor.orientationVerified || entry.alignment != PlanetDecorationAlignment.GravityUp))
                    errors.Add(entry.stableId + " 的植物向上方向没有确认为径向 +Y。");
            }
            bool componentSaysHarvestable = entry.prefab.GetComponentInChildren<HarvestableResource>(true) != null;
            if (componentSaysHarvestable != entry.IsHarvestable)
                errors.Add(entry.stableId + " 的采集物组件与目录分类不一致。");
        }

        report = errors.Count == 0
            ? $"共 {catalog.Entries.Count} 个模型；七类气候、确定性生成、模型贴地和采集组件分类均检查通过。"
            : string.Join(" | ", errors);
        return errors.Count == 0;
    }

    static void TryAutomaticSetup()
    {
        if (running || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += TryAutomaticSetup;
            return;
        }
        if (AssetDatabase.LoadAssetAtPath<PlanetDecorationCatalog>(CatalogPath) != null && EditorPrefs.GetBool(SetupKey, false))
            return;
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ImportedProbe) == null)
        {
            if (!File.Exists(PackagePath))
            {
                Debug.LogWarning("星球装饰工具：在以下位置找不到素材包：" + PackagePath);
                return;
            }
            if (!SessionState.GetBool(ImportKey, false))
            {
                SessionState.SetBool(ImportKey, true);
                Debug.Log("星球装饰工具：正在导入轻量素材包，不会导入草原大包。");
                AssetDatabase.ImportPackage(PackagePath, false);
                EditorApplication.delayCall += TryAutomaticSetup;
            }
            return;
        }
        SetupProject();
        RunAutomatedValidation(out string report);
        Debug.Log("星球装饰工具自动检查：" + report);
    }

    static List<PlanetClimateProfile> BuildProfiles()
    {
        return new List<PlanetClimateProfile>
        {
            Profile(PlanetClimate.Barren, 450, 24),
            Profile(PlanetClimate.TemperateForest, 700, 20),
            Profile(PlanetClimate.Desert, 500, 22),
            Profile(PlanetClimate.Tropical, 650, 20),
            Profile(PlanetClimate.Tundra, 450, 20),
            Profile(PlanetClimate.Volcanic, 500, 30),
            Profile(PlanetClimate.Crystal, 550, 40)
        };
    }

    static PlanetClimateProfile Profile(PlanetClimate climate, int decorations, int harvestables)
    {
        return new PlanetClimateProfile { climate = climate, decorationCount = decorations, harvestableCount = harvestables };
    }

    static List<PlanetDecorationEntry> BuildCuratedEntries()
    {
        PlanetClimateMask barrenRock = PlanetClimateMask.Barren | PlanetClimateMask.Desert | PlanetClimateMask.Volcanic | PlanetClimateMask.Crystal;
        var specs = new List<CuratedSpec>
        {
            new CuratedSpec("forest-tree-1", "Assets/Lowpoly Style/ForestPack/Prefabs/Tree1.prefab", PlanetClimateMask.TemperateForest, PlanetDecorationRole.Vegetation, 2.2f, 0.8f, 1.2f),
            new CuratedSpec("forest-tree-2", "Assets/Lowpoly Style/ForestPack/Prefabs/Tree2.prefab", PlanetClimateMask.TemperateForest, PlanetDecorationRole.Vegetation, 1.8f, 0.8f, 1.25f),
            new CuratedSpec("forest-bush", "Assets/Lowpoly Style/ForestPack/Prefabs/Bush.prefab", PlanetClimateMask.TemperateForest, PlanetDecorationRole.GroundCover, 2.6f, 0.75f, 1.3f),
            new CuratedSpec("forest-mushrooms", "Assets/Lowpoly Style/ForestPack/Prefabs/Mushrooms.prefab", PlanetClimateMask.TemperateForest, PlanetDecorationRole.GroundCover, 1.4f, 0.75f, 1.25f),
            new CuratedSpec("desert-saguaro", "Assets/Lowpoly Style/Desert/Prefabs/SaguaroCactus1.prefab", PlanetClimateMask.Desert, PlanetDecorationRole.Vegetation, 2f, 0.8f, 1.25f),
            new CuratedSpec("desert-joshua", "Assets/Lowpoly Style/Desert/Prefabs/JoshuaTree1.prefab", PlanetClimateMask.Desert, PlanetDecorationRole.Vegetation, 1.3f, 0.8f, 1.2f),
            new CuratedSpec("desert-agave", "Assets/Lowpoly Style/Desert/Prefabs/AgaveLow.prefab", PlanetClimateMask.Desert, PlanetDecorationRole.GroundCover, 2.4f, 0.75f, 1.3f),
            new CuratedSpec("tropical-palm", "Assets/Lowpoly Style/Tropical Islands/Prefabs/Palm3_straight.prefab", PlanetClimateMask.Tropical, PlanetDecorationRole.Vegetation, 2.3f, 0.8f, 1.25f),
            new CuratedSpec("tropical-coconut", "Assets/Lowpoly Style/Tropical Islands/Prefabs/Coconut_Plant.prefab", PlanetClimateMask.Tropical, PlanetDecorationRole.GroundCover, 1.6f, 0.8f, 1.25f),
            new CuratedSpec("tropical-reed", "Assets/Lowpoly Style/Tropical Islands/Prefabs/ReedBig.prefab", PlanetClimateMask.Tropical, PlanetDecorationRole.GroundCover, 2.4f, 0.75f, 1.25f),
            new CuratedSpec("tropical-flower", "Assets/Lowpoly Style/Tropical Islands/Prefabs/FlowerBlue.prefab", PlanetClimateMask.Tropical, PlanetDecorationRole.GroundCover, 1.3f, 0.8f, 1.3f),
            new CuratedSpec("tundra-snow-tree", "Assets/Lowpoly Style/Arctic Tundra/Prefabs/SnowyTree.prefab", PlanetClimateMask.Tundra, PlanetDecorationRole.Vegetation, 2.2f, 0.8f, 1.2f),
            new CuratedSpec("tundra-dead-tree", "Assets/Lowpoly Style/Arctic Tundra/Prefabs/SnowyDeadTree1.prefab", PlanetClimateMask.Tundra, PlanetDecorationRole.Vegetation, 1f, 0.8f, 1.25f),
            new CuratedSpec("tundra-snow-rock", "Assets/Lowpoly Style/Arctic Tundra/Prefabs/SnowyRock1.prefab", PlanetClimateMask.Tundra | PlanetClimateMask.Crystal, PlanetDecorationRole.Rock, 2f, 0.75f, 1.35f),
            new CuratedSpec("tundra-ice-floe", "Assets/Lowpoly Style/Arctic Tundra/Prefabs/IceFloeLow.prefab", PlanetClimateMask.Tundra, PlanetDecorationRole.GroundCover, 0.7f, 0.8f, 1.3f),
            new CuratedSpec("planet-grey-rock", "Assets/Lowpoly Style/Alpine Woodland/Prefabs/RockGrey1.prefab", barrenRock, PlanetDecorationRole.Rock, 3f, 0.7f, 1.4f),
            new CuratedSpec("planet-sharp-rock", "Assets/Lowpoly Style/Alpine Woodland/Prefabs/SharpRock1.prefab", barrenRock, PlanetDecorationRole.Rock, 2f, 0.75f, 1.35f),
            new CuratedSpec("volcanic-lava", "Assets/Lowpoly Style/Tropical Islands/Prefabs/LavaFlow.prefab", PlanetClimateMask.Volcanic, PlanetDecorationRole.Landmark, 0.35f, 0.8f, 1.2f),
            new CuratedSpec("crystal-amethyst-decoration", "Assets/model/Pure_Amethyst.fbx", PlanetClimateMask.Crystal, PlanetDecorationRole.Rock, 3.5f, 0.7f, 1.35f),
            new CuratedSpec("amethyst-harvestable", "Assets/Prefabs/harvest/Pure_Amethyst.prefab", PlanetClimateMask.All, PlanetDecorationRole.Rock, 1f, 0.85f, 1.2f)
        };

        var entries = new List<PlanetDecorationEntry>();
        foreach (CuratedSpec spec in specs)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(spec.path);
            if (source == null)
            {
                Debug.LogWarning("PlanetDecorationTool: curated source is missing: " + spec.path);
                continue;
            }
            GameObject wrapper = CreateNormalizedWrapper(
                source,
                spec.id,
                spec.role,
                Vector3.zero,
                spec.verified,
                false,
                null,
                out Bounds bounds,
                out bool axisClear);
            if (wrapper == null)
                continue;
            PlanetDecorationEntry entry = CreateEntry(spec.id, wrapper, spec.climates, spec.role, 1f, 0f, spec.verified);
            entry.weight = spec.weight;
            entry.minimumScale = spec.scaleMin;
            entry.maximumScale = spec.scaleMax;
            if (spec.role == PlanetDecorationRole.Landmark)
                entry.collision = PlanetDecorationCollision.KeepPrefab;
            entries.Add(entry);
        }
        return entries;
    }

    static void ConfigureStarScene(PlanetDecorationCatalog catalog)
    {
        Scene activeScene = SceneManager.GetActiveScene();
        Scene star = SceneManager.GetSceneByPath(StarScenePath);
        bool opened = !star.IsValid() || !star.isLoaded;
        if (opened)
            star = EditorSceneManager.OpenScene(StarScenePath, OpenSceneMode.Additive);
        try
        {
            GalaxyTravelManager manager = null;
            foreach (GameObject root in star.GetRootGameObjects())
            {
                manager = root.GetComponentInChildren<GalaxyTravelManager>(true);
                if (manager != null)
                    break;
            }
            if (manager == null)
                throw new InvalidOperationException("GalaxyTravelManager was not found in " + StarScenePath);

            SerializedObject serializedManager = new SerializedObject(manager);
            serializedManager.FindProperty("decorationCatalog").objectReferenceValue = catalog;
            SerializedProperty planets = serializedManager.FindProperty("planets");
            for (int i = 0; i < planets.arraySize; i++)
            {
                SerializedProperty planet = planets.GetArrayElementAtIndex(i);
                string id = planet.FindPropertyRelative("planetId").stringValue;
                if (PlanetClimateClassifier.TryGetFixedClimate(id, out PlanetClimate climate))
                    planet.FindPropertyRelative("climate").enumValueIndex = (int)climate;
            }
            serializedManager.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(manager);
            EditorSceneManager.MarkSceneDirty(star);
            EditorSceneManager.SaveScene(star);
        }
        finally
        {
            if (opened && star.IsValid() && star.isLoaded)
                EditorSceneManager.CloseScene(star, true);
            if (activeScene.IsValid() && activeScene.isLoaded)
                SceneManager.SetActiveScene(activeScene);
        }
    }

    static void ValidateClassifier(List<string> errors)
    {
        CheckClimate(errors, PlanetClimate.Crystal, PlanetClimateClassifier.Classify(0.1f, 0.1f, 0.1f, 0.68f));
        CheckClimate(errors, PlanetClimate.Volcanic, PlanetClimateClassifier.Classify(0.55f, 0.2f, 0.72f, 0f));
        CheckClimate(errors, PlanetClimate.Tundra, PlanetClimateClassifier.Classify(0.28f, 0.8f, 0f, 0f));
        CheckClimate(errors, PlanetClimate.Tropical, PlanetClimateClassifier.Classify(0.62f, 0.58f, 0f, 0f));
        CheckClimate(errors, PlanetClimate.Desert, PlanetClimateClassifier.Classify(0.62f, 0.38f, 0f, 0f));
        CheckClimate(errors, PlanetClimate.TemperateForest, PlanetClimateClassifier.Classify(0.5f, 0.48f, 0f, 0f));
        CheckClimate(errors, PlanetClimate.Barren, PlanetClimateClassifier.Classify(0.5f, 0.47f, 0f, 0f));
    }

    static void CheckClimate(List<string> errors, PlanetClimate expected, PlanetClimate actual)
    {
        if (expected != actual)
            errors.Add($"Climate boundary expected {expected}, got {actual}.");
    }

    static Quaternion DeterminePlantAxisCorrection(Bounds bounds, PlanetDecorationRole role, out bool clear)
    {
        clear = role != PlanetDecorationRole.Vegetation;
        if (role != PlanetDecorationRole.Vegetation || bounds.size.sqrMagnitude < 0.0001f)
            return Quaternion.identity;
        float x = bounds.size.x;
        float y = bounds.size.y;
        float z = bounds.size.z;
        float horizontal = Mathf.Max(x, z);
        if (y >= horizontal * 1.1f)
        {
            clear = true;
            return Quaternion.identity;
        }
        if (x >= Mathf.Max(y, z) * 1.25f)
        {
            clear = true;
            return Quaternion.Euler(0f, 0f, 90f);
        }
        if (z >= Mathf.Max(x, y) * 1.25f)
        {
            clear = true;
            return Quaternion.Euler(-90f, 0f, 0f);
        }
        clear = false;
        return Quaternion.identity;
    }

    static Bounds CalculateLocalRendererBounds(Transform root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return new Bounds(Vector3.zero, Vector3.zero);
        Vector3 minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Vector3 maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        foreach (Renderer value in renderers)
        {
            Bounds bounds = value.localBounds;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 rendererCorner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
                Vector3 world = value.transform.TransformPoint(rendererCorner);
                Vector3 local = root.InverseTransformPoint(world);
                minimum = Vector3.Min(minimum, local);
                maximum = Vector3.Max(maximum, local);
            }
        }
        return new Bounds((minimum + maximum) * 0.5f, maximum - minimum);
    }

    static void EnsureFolders()
    {
        EnsureFolder("Assets", "PlanetDecoration");
        EnsureFolder("Assets/PlanetDecoration", "GeneratedPrefabs");
    }

    static void EnsureFolder(string parent, string name)
    {
        string path = parent + "/" + name;
        if (!AssetDatabase.IsValidFolder(path))
            AssetDatabase.CreateFolder(parent, name);
    }

    static string SanitizeFileName(string value)
    {
        string result = value;
        foreach (char invalid in Path.GetInvalidFileNameChars())
            result = result.Replace(invalid, '-');
        return result.Replace('/', '-').Replace('\\', '-');
    }
}
