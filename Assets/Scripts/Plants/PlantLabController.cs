using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlantLabController : MonoBehaviour
{
    const int PresetSchema = 1;
    const float PreviewDebounceSeconds = 0.18f;

    [Serializable]
    sealed class PlantPreset
    {
        public int schemaVersion = PresetSchema;
        public string speciesId;
        public int seed;
        public PlantPaletteMode palette;
        public PlantGenerationParameters parameters;
        public Color customBark;
        public Color customLeaf;
        public Color customAccent;
        public int scatterCount;
        public int variantPoolSize;
    }

    [Header("场景引用")]
    [SerializeField] ProceduralPlantSpecies[] species = Array.Empty<ProceduralPlantSpecies>();
    [SerializeField] AnalyticSphereSurfacePlacementContext surfaceContext;
    [SerializeField] PlanetSurfaceDecorationSystem decorationSystem;
    [SerializeField] PlantLabOrbitCamera orbitCamera;
    [SerializeField] Transform previewContainer;

    [Header("实验参数")]
    [SerializeField] int selectedSpecies;
    [SerializeField] int seed = 222;
    [SerializeField] PlantPaletteMode palette = PlantPaletteMode.Natural;
    [SerializeField] int scatterCount = 300;
    [SerializeField, Range(1, 24)] int variantPoolSize = 12;
    [SerializeField] Color customBark = new Color(0.16f, 0.25f, 0.34f);
    [SerializeField] Color customLeaf = new Color(0.06f, 0.82f, 0.65f);
    [SerializeField] Color customAccent = new Color(0.86f, 0.2f, 0.94f);

    PlantGenerationParameters editing;
    ProceduralPlantFactory previewFactory;
    Transform previewRoot;
    Transform stagesRoot;
    Coroutine scatterRoutine;
    Vector2 scroll;
    bool previewDirty;
    float previewDirtyAt;
    string presetName = "我的植物";
    string status = "准备生成";
    GUIStyle panelStyle;
    GUIStyle titleStyle;
    readonly List<ProceduralPlantSpecies> runtimeSpecies = new List<ProceduralPlantSpecies>();

    public IReadOnlyList<ProceduralPlantSpecies> Species => species;
    public int SelectedSpecies => selectedSpecies;
    public int Seed => seed;
    public int ScatterCount => scatterCount;
    public int VariantPoolSize => variantPoolSize;

    void Start()
    {
        EnsureReferences();
        LoadEditingFromSpecies();
        RebuildPreviewAndStages();
        ApplyToScatter();
        if (orbitCamera != null && surfaceContext != null)
        {
            Vector3 target = surfaceContext.PlanetCenter + Vector3.up * (surfaceContext.PlanetRadius + 3f);
            orbitCamera.Configure(target, 28f);
        }
    }

    void Update()
    {
        if (previewDirty && Time.unscaledTime - previewDirtyAt >= PreviewDebounceSeconds)
        {
            previewDirty = false;
            RebuildPreviewAndStages();
        }
    }

    public void RegeneratePreview()
    {
        SchedulePreview();
    }

    public void ApplyToScatter()
    {
        if (surfaceContext == null || decorationSystem == null || species == null || species.Length == 0)
        {
            status = "错误：场景引用不完整";
            return;
        }

        List<PlanetSurfacePropSpawnSettings> settings = BuildScatterSettings();
        try
        {
            status = "正在构建共享变体池...";
            decorationSystem.Configure(surfaceContext, settings);
            if (scatterRoutine != null)
                StopCoroutine(scatterRoutine);
            scatterRoutine = StartCoroutine(GenerateScatterRoutine());
        }
        catch (Exception exception)
        {
            status = "散布构建失败：" + exception.Message;
            Debug.LogException(exception, this);
        }
    }

    public void SetStressMode(bool enabled)
    {
        scatterCount = enabled ? 1000 : 300;
        ApplyToScatter();
    }

    IEnumerator GenerateScatterRoutine()
    {
        yield return decorationSystem.GenerateIncremental(4f);
        status = $"完成：{decorationSystem.GetSnapshots().Length} 株，共享变体池 {variantPoolSize}";
        scatterRoutine = null;
    }

    List<PlanetSurfacePropSpawnSettings> BuildScatterSettings()
    {
        for (int i = 0; i < runtimeSpecies.Count; i++)
            if (runtimeSpecies[i] != null)
                Destroy(runtimeSpecies[i]);
        runtimeSpecies.Clear();
        var result = new List<PlanetSurfacePropSpawnSettings>();
        int baseCount = scatterCount / species.Length;
        int remainder = scatterCount % species.Length;
        for (int i = 0; i < species.Length; i++)
        {
            ProceduralPlantSpecies source = CreateRuntimeSpecies(species[i], i == selectedSpecies);
            runtimeSpecies.Add(source);
            int count = baseCount + (i < remainder ? 1 : 0);
            float spacing = scatterCount >= 1000 ? 1.05f : 1.8f;
            result.Add(new PlanetSurfacePropSpawnSettings
            {
                catalogId = "plant-lab-" + source.speciesId,
                proceduralPlantSpecies = source,
                variantPoolSize = variantPoolSize,
                role = PlanetDecorationRole.Vegetation,
                count = count,
                seedOffset = PlanetDecorationCatalog.StableHash(source.speciesId) ^ seed,
                minimumScale = 0.32f,
                maximumScale = 0.68f,
                surfaceOffset = 0.02f,
                minimumSpacing = spacing,
                playerClearRadius = 11f,
                placementAttempts = 40,
                maximumSlope = 90f,
                waterClearance = 0f,
                clusterRadius = i == 2 ? 7f : 12f,
                clusterSize = i == 2 ? 8 : 16,
                alignment = PlanetDecorationAlignment.GravityUp,
                collision = PlanetDecorationCollision.None,
                randomizeYaw = true,
                orientationVerified = true
            });
        }
        return result;
    }

    ProceduralPlantSpecies CreateRuntimeSpecies(ProceduralPlantSpecies source, bool useEditing)
    {
        ProceduralPlantSpecies copy = Instantiate(source);
        copy.name = source.name + "_Runtime";
        copy.hideFlags = HideFlags.DontSave;
        if (useEditing)
            copy.parameters = editing.Clone();
        if (palette == PlantPaletteMode.Alien)
        {
            copy.naturalBark = source.alienBark;
            copy.naturalLeaf = source.alienLeaf;
            copy.naturalAccent = source.alienAccent;
        }
        else if (palette == PlantPaletteMode.Custom)
        {
            copy.naturalBark = customBark;
            copy.naturalLeaf = customLeaf;
            copy.naturalAccent = customAccent;
        }
        return copy;
    }

    void RebuildPreviewAndStages()
    {
        if (species == null || species.Length == 0)
            return;
        selectedSpecies = Mathf.Clamp(selectedSpecies, 0, species.Length - 1);
        editing.Clamp();
        DestroyPreviewObjects();
        previewFactory = new ProceduralPlantFactory();
        ProceduralPlantSpecies source = species[selectedSpecies];
        try
        {
            previewFactory.RegisterPool(
                source,
                seed,
                "main-preview",
                1,
                palette,
                editing,
                customBark,
                customLeaf,
                customAccent,
                seed);
            previewRoot = previewFactory.CreateInstance("main-preview", seed, "main", previewContainer).transform;
            PlaceOnSphere(previewRoot, 0f, 4.2f, 1.12f);

            stagesRoot = new GameObject("SixGrowthStages").transform;
            stagesRoot.SetParent(previewContainer, false);
            float[] ages = { 0.08f, 0.24f, 0.42f, 0.6f, 0.8f, 1f };
            for (int i = 0; i < ages.Length; i++)
            {
                PlantGenerationParameters stageParameters = editing.Clone();
                stageParameters.age = ages[i];
                string id = "stage-" + i;
                previewFactory.RegisterPool(
                    source,
                    seed,
                    id,
                    1,
                    palette,
                    stageParameters,
                    customBark,
                    customLeaf,
                    customAccent,
                    seed);
                Transform stage = previewFactory.CreateInstance(id, seed, "age-" + i, stagesRoot).transform;
                PlaceOnSphere(stage, (i - 2.5f) * 3.15f, -4.2f, 0.68f);
            }
            status = $"预览完成：{source.speciesId}，种子 {seed}";
        }
        catch (Exception exception)
        {
            status = "预览生成失败：" + exception.Message;
            Debug.LogException(exception, this);
        }
    }

    void PlaceOnSphere(Transform plant, float tangentX, float tangentZ, float scale)
    {
        if (plant == null || surfaceContext == null)
            return;
        Vector3 direction = (Vector3.up
            + Vector3.right * (tangentX / surfaceContext.PlanetRadius)
            + Vector3.forward * (tangentZ / surfaceContext.PlanetRadius)).normalized;
        plant.position = surfaceContext.PlanetCenter + direction * (surfaceContext.PlanetRadius + 0.02f);
        plant.rotation = Quaternion.FromToRotation(Vector3.up, direction);
        plant.localScale = Vector3.one * scale;
    }

    void SchedulePreview()
    {
        previewDirty = true;
        previewDirtyAt = Time.unscaledTime;
    }

    void SelectSpecies(int index)
    {
        if (index == selectedSpecies)
            return;
        selectedSpecies = index;
        LoadEditingFromSpecies();
        SchedulePreview();
    }

    void LoadEditingFromSpecies()
    {
        if (species == null || species.Length == 0)
        {
            editing = new PlantGenerationParameters();
            return;
        }
        selectedSpecies = Mathf.Clamp(selectedSpecies, 0, species.Length - 1);
        editing = (species[selectedSpecies].parameters ?? new PlantGenerationParameters()).Clone();
        editing.Clamp();
    }

    void EnsureReferences()
    {
        if (surfaceContext == null)
            surfaceContext = FindObjectOfType<AnalyticSphereSurfacePlacementContext>();
        if (decorationSystem == null)
            decorationSystem = FindObjectOfType<PlanetSurfaceDecorationSystem>();
        if (orbitCamera == null && Camera.main != null)
            orbitCamera = Camera.main.GetComponent<PlantLabOrbitCamera>();
        if (previewContainer == null)
        {
            var root = new GameObject("PlantPreviewArea");
            root.transform.SetParent(transform, false);
            previewContainer = root.transform;
        }
    }

    void DestroyPreviewObjects()
    {
        previewFactory?.Dispose();
        previewFactory = null;
        DestroyTransform(ref previewRoot);
        DestroyTransform(ref stagesRoot);
    }

    void OnDestroy()
    {
        DestroyPreviewObjects();
        for (int i = 0; i < runtimeSpecies.Count; i++)
            if (runtimeSpecies[i] != null)
                Destroy(runtimeSpecies[i]);
        runtimeSpecies.Clear();
    }

    void DestroyTransform(ref Transform value)
    {
        if (value == null)
            return;
        if (Application.isPlaying)
            Destroy(value.gameObject);
        else
            DestroyImmediate(value.gameObject);
        value = null;
    }

    void OnGUI()
    {
        if (editing == null)
            return;
        EnsureGuiStyles();
        GUILayout.BeginArea(new Rect(14f, 14f, 356f, Screen.height - 28f), panelStyle);
        scroll = GUILayout.BeginScrollView(scroll);
        GUILayout.Label("程序化植物实验室", titleStyle);
        GUILayout.Label(status);
        GUILayout.Space(6f);

        GUILayout.Label("树种");
        GUILayout.BeginHorizontal();
        for (int i = 0; i < species.Length; i++)
        {
            string label = species[i] != null ? FamilyLabel(species[i].family) : "缺失";
            GUI.enabled = i != selectedSpecies;
            if (GUILayout.Button(label))
                SelectSpecies(i);
        }
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        int previousSeed = seed;
        seed = Mathf.RoundToInt(Slider("种子", seed, 1f, 99999f, "0"));
        editing.age = Slider("年龄", editing.age, 0f, 1f);
        editing.branchDepth = Mathf.RoundToInt(Slider("分枝深度", editing.branchDepth, 0f, 5f, "0"));
        editing.budsPerBranch = Mathf.RoundToInt(Slider("芽点数量", editing.budsPerBranch, 1f, 6f, "0"));
        editing.branchAngle = Slider("分枝角度", editing.branchAngle, 5f, 80f, "0°");
        editing.phototropism = Slider("向光性", editing.phototropism, 0f, 1f);
        editing.curvature = Slider("弯曲", editing.curvature, 0f, 1f);
        editing.taper = Slider("锥度", editing.taper, 0.35f, 0.95f);

        GUILayout.Space(5f);
        GUILayout.Label("叶片");
        GUILayout.BeginHorizontal();
        LeafButton("实体叶", PlantLeafMode.SolidLeaf);
        LeafButton("透明叶卡", PlantLeafMode.CardLeaf);
        LeafButton("无叶", PlantLeafMode.None);
        GUILayout.EndHorizontal();
        editing.leafDensity = Slider("叶量", editing.leafDensity, 0f, 3f);
        editing.leafSize = Slider("叶片大小", editing.leafSize, 0.08f, 1.4f);

        GUILayout.Space(5f);
        GUILayout.Label("调色板");
        GUILayout.BeginHorizontal();
        PaletteButton("自然", PlantPaletteMode.Natural);
        PaletteButton("外星", PlantPaletteMode.Alien);
        PaletteButton("自定义", PlantPaletteMode.Custom);
        GUILayout.EndHorizontal();
        if (palette == PlantPaletteMode.Custom)
        {
            DrawColorSliders("树干", ref customBark);
            DrawColorSliders("叶片", ref customLeaf);
            DrawColorSliders("强调", ref customAccent);
        }

        scatterCount = Mathf.RoundToInt(Slider("散布数量", scatterCount, 50f, 1000f, "0"));
        variantPoolSize = Mathf.RoundToInt(Slider("变体池", variantPoolSize, 1f, 24f, "0"));

        if (GUI.changed || seed != previousSeed)
            SchedulePreview();

        GUILayout.Space(8f);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("下一个种子", GUILayout.Height(28f)))
        {
            seed++;
            SchedulePreview();
        }
        if (GUILayout.Button("重新生成", GUILayout.Height(28f)))
            RebuildPreviewAndStages();
        GUILayout.EndHorizontal();
        if (GUILayout.Button("应用到球面散布", GUILayout.Height(32f)))
            ApplyToScatter();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("300 株"))
            SetStressMode(false);
        if (GUILayout.Button("1000 株压力测试"))
            SetStressMode(true);
        GUILayout.EndHorizontal();

        GUILayout.Space(8f);
        GUILayout.Label("JSON 配方");
        presetName = GUILayout.TextField(presetName);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("保存"))
            SavePreset();
        if (GUILayout.Button("载入"))
            LoadPreset();
        if (GUILayout.Button("删除"))
            DeletePreset();
        GUILayout.EndHorizontal();

        GUILayout.Space(6f);
        GUILayout.Label("鼠标左键/右键拖动：环绕相机\n滚轮：缩放");
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    float Slider(string label, float value, float minimum, float maximum, string format = "0.00")
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(92f));
        float result = GUILayout.HorizontalSlider(value, minimum, maximum);
        GUILayout.Label(result.ToString(format), GUILayout.Width(54f));
        GUILayout.EndHorizontal();
        return result;
    }

    void LeafButton(string label, PlantLeafMode value)
    {
        GUI.enabled = editing.leafMode != value;
        if (GUILayout.Button(label))
        {
            editing.leafMode = value;
            SchedulePreview();
        }
        GUI.enabled = true;
    }

    void PaletteButton(string label, PlantPaletteMode value)
    {
        GUI.enabled = palette != value;
        if (GUILayout.Button(label))
        {
            palette = value;
            SchedulePreview();
        }
        GUI.enabled = true;
    }

    void DrawColorSliders(string label, ref Color color)
    {
        GUILayout.Label(label);
        color.r = Slider("  红", color.r, 0f, 1f);
        color.g = Slider("  绿", color.g, 0f, 1f);
        color.b = Slider("  蓝", color.b, 0f, 1f);
        color.a = 1f;
    }

    void SavePreset()
    {
        try
        {
            Directory.CreateDirectory(PresetDirectory);
            var preset = new PlantPreset
            {
                speciesId = species[selectedSpecies].speciesId,
                seed = seed,
                palette = palette,
                parameters = editing.Clone(),
                customBark = customBark,
                customLeaf = customLeaf,
                customAccent = customAccent,
                scatterCount = scatterCount,
                variantPoolSize = variantPoolSize
            };
            File.WriteAllText(PresetPath, JsonUtility.ToJson(preset, true));
            status = "已保存：" + PresetPath;
        }
        catch (Exception exception)
        {
            status = "保存失败：" + exception.Message;
        }
    }

    void LoadPreset()
    {
        try
        {
            if (!File.Exists(PresetPath))
            {
                status = "找不到配方：" + PresetPath;
                return;
            }
            PlantPreset preset = JsonUtility.FromJson<PlantPreset>(File.ReadAllText(PresetPath));
            if (preset == null || preset.schemaVersion != PresetSchema)
            {
                status = "拒绝载入：不支持的配方版本";
                return;
            }
            for (int i = 0; i < species.Length; i++)
                if (species[i] != null && species[i].speciesId == preset.speciesId)
                    selectedSpecies = i;
            seed = preset.seed;
            palette = preset.palette;
            editing = preset.parameters ?? species[selectedSpecies].parameters.Clone();
            customBark = preset.customBark;
            customLeaf = preset.customLeaf;
            customAccent = preset.customAccent;
            scatterCount = Mathf.Clamp(preset.scatterCount, 50, 1000);
            variantPoolSize = Mathf.Clamp(preset.variantPoolSize, 1, 24);
            editing.Clamp();
            RebuildPreviewAndStages();
            status = "已载入：" + PresetPath;
        }
        catch (Exception exception)
        {
            status = "载入失败：" + exception.Message;
        }
    }

    void DeletePreset()
    {
        try
        {
            if (File.Exists(PresetPath))
                File.Delete(PresetPath);
            status = "已删除配方：" + SanitizeFileName(presetName);
        }
        catch (Exception exception)
        {
            status = "删除失败：" + exception.Message;
        }
    }

    string PresetDirectory => Path.Combine(Application.persistentDataPath, "PlantPresets");
    string PresetPath => Path.Combine(PresetDirectory, SanitizeFileName(presetName) + ".json");

    static string SanitizeFileName(string value)
    {
        string result = string.IsNullOrWhiteSpace(value) ? "plant" : value.Trim();
        foreach (char invalid in Path.GetInvalidFileNameChars())
            result = result.Replace(invalid, '_');
        return result;
    }

    void EnsureGuiStyles()
    {
        if (panelStyle != null)
            return;
        panelStyle = new GUIStyle(GUI.skin.box)
        {
            padding = new RectOffset(14, 14, 12, 12),
            alignment = TextAnchor.UpperLeft
        };
        titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 21,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft
        };
    }

    static string FamilyLabel(ProceduralPlantFamily family)
    {
        switch (family)
        {
            case ProceduralPlantFamily.AlienBroadleaf: return "外星宽叶";
            case ProceduralPlantFamily.CanopyTree: return "树冠乔木";
            default: return "珊瑚多肉";
        }
    }
}
