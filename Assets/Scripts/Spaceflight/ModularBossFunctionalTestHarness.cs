using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ModularAssembly;
using UnityEngine;
using UnityPlanet.ModularAssembly;

/// <summary>
/// Editor-facing, collision-free view of the real modular Boss generator.
/// The host is tagged EditorOnly in the dedicated urban trap test scene, so
/// it cannot participate in combat, physics, wind, magnetism or a player build.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class ModularBossFunctionalTestHarness : MonoBehaviour
{
    public const string SceneObjectName = "Boss功能检测工具_EditorOnly";
    public const int PreviewLayer = 31;
    public const float ModuleVisualScale = 5f;

    [SerializeField, Range(0, 5)] int difficultyTier;
    [SerializeField] int seed = 7319;
    [SerializeField] bool showShield = true;

    [NonSerialized] GameObject previewRoot;
    [NonSerialized] ModularBossBuildResult currentBuild;
    [NonSerialized] readonly List<GridModuleDefinition> definitions =
        new List<GridModuleDefinition>();
    [NonSerialized] readonly List<Material> materials = new List<Material>();
    [NonSerialized] Bounds previewBounds;
    [NonSerialized] string lastReport = "尚未生成。";
    [NonSerialized] string lastError = string.Empty;
    [NonSerialized] bool allChecksPassed;

    public int DifficultyTier => difficultyTier;
    public int Seed => seed;
    public bool ShowShield => showShield;
    public ModularBossBuildResult CurrentBuild => currentBuild;
    public Bounds PreviewBounds => previewBounds;
    public string LastReport => lastReport;
    public string LastError => lastError;
    public bool AllChecksPassed => allChecksPassed;

    public string ArchetypeName
    {
        get
        {
            if (currentBuild == null)
                return "未生成";
            switch (currentBuild.HullArchetype)
            {
                case ModularBossHullArchetype.Spearhead:
                    return "突击矛头型";
                case ModularBossHullArchetype.Hammerhead:
                    return "封锁锤头型";
                default:
                    return "立体堡垒型";
            }
        }
    }

    public string TacticalHint
    {
        get
        {
            if (currentBuild == null)
                return "先生成 Boss。";
            switch (currentBuild.HullArchetype)
            {
                case ModularBossHullArchetype.Spearhead:
                    return "解法：利用楼角和错位街口诱导冲锋，借撞楼削盾，再从宽侧面集火武器。";
                case ModularBossHullArchetype.Hammerhead:
                    return "解法：避免在窄巷与其宽正面换血；绕廊桥支点诱导撞桥，打开侧向火力死角。";
                default:
                    return "解法：拉高低差、绕高楼切断多层火力；优先拆炮，再诱导重型舰体撞楼或廊桥。";
            }
        }
    }

    void OnEnable()
    {
        if (!Application.isPlaying)
            RebuildPreview();
    }

    void OnDisable()
    {
        ClearPreview();
    }

    void OnValidate()
    {
        difficultyTier = Mathf.Clamp(difficultyTier, 0, 5);
    }

    public void ConfigurePreview(int tier, int previewSeed, bool shieldVisible)
    {
        difficultyTier = Mathf.Clamp(tier, 0, 5);
        seed = previewSeed;
        showShield = shieldVisible;
        RebuildPreview();
    }

    public bool RebuildPreview()
    {
        ClearPreview();
        CreateDefinitions();
        if (!ModularBossPcgGenerator.TryBuild(
                definitions,
                difficultyTier,
                seed,
                out currentBuild,
                out lastError))
        {
            lastReport = "生成失败：" + lastError;
            allChecksPassed = false;
            return false;
        }

        BuildVisuals();
        EvaluateBuild();
        return allChecksPassed;
    }

    public void ClearPreview()
    {
        currentBuild = null;
        if (previewRoot != null)
            DestroyOwnedObject(previewRoot);
        previewRoot = null;
        // Recover cleanly after a domain reload or an interrupted editor
        // callback, where the non-serialized previewRoot reference is lost.
        for (int index = transform.childCount - 1; index >= 0; index--)
        {
            Transform child = transform.GetChild(index);
            if (child != null &&
                child.name == "Generated_BossPreview_DontSave")
                DestroyOwnedObject(child.gameObject);
        }
        for (int index = materials.Count - 1; index >= 0; index--)
            if (materials[index] != null)
                DestroyOwnedObject(materials[index]);
        materials.Clear();
        for (int index = definitions.Count - 1; index >= 0; index--)
            if (definitions[index] != null)
                DestroyOwnedObject(definitions[index]);
        definitions.Clear();
        previewBounds = new Bounds(transform.position, Vector3.one);
    }

    void CreateDefinitions()
    {
        definitions.Add(CreateDefinition(
            "core", GridModuleCategory.Core,
            new Vector3Int(2, 2, 2), 100f, 100f, 0f, 1000f));
        definitions.Add(CreateDefinition(
            "neox@block:common:block_111", GridModuleCategory.Structure,
            Vector3Int.one, 75f, 0f, 0f, 140f));
        definitions.Add(CreateDefinition(
            "neox@block:speed:speed_rocketsmall_112",
            GridModuleCategory.MainThruster,
            new Vector3Int(1, 1, 2), 180f, 0f, 0f, 140f, 3000f));
        definitions.Add(CreateDefinition(
            "neox@block:common:machinegun_111",
            GridModuleCategory.KineticWeapon,
            Vector3Int.one, 220f, 0f, 5f, 140f));
    }

    static GridModuleDefinition CreateDefinition(
        string id,
        GridModuleCategory category,
        Vector3Int footprint,
        float mass,
        float capacity,
        float cost,
        float integrity,
        float thrust = 0f)
    {
        GridModuleDefinition definition =
            ScriptableObject.CreateInstance<GridModuleDefinition>();
        definition.hideFlags = HideFlags.HideAndDontSave;
        definition.Configure(
            id, id, category, null, footprint, mass, capacity, cost,
            integrity, thrust, null);
        return definition;
    }

    void BuildVisuals()
    {
        previewRoot = new GameObject("Generated_BossPreview_DontSave");
        previewRoot.hideFlags = HideFlags.DontSaveInEditor;
        previewRoot.layer = PreviewLayer;
        previewRoot.transform.SetParent(transform, false);

        Material structure = CreateMaterial(
            "BossPreview_Structure", new Color(0.16f, 0.23f, 0.30f),
            0.78f, 0.48f, false);
        Material core = CreateMaterial(
            "BossPreview_Core", new Color(1.00f, 0.38f, 0.06f),
            0.60f, 0.62f, true);
        Material thruster = CreateMaterial(
            "BossPreview_Thruster", new Color(0.02f, 0.74f, 1.00f),
            0.38f, 0.76f, true);
        Material weapon = CreateMaterial(
            "BossPreview_Weapon", new Color(0.92f, 0.08f, 0.035f),
            0.70f, 0.58f, true);

        foreach (GridModuleRecord record in currentBuild.Model.Records)
        {
            Material material = ResolveMaterial(
                record.Definition.Category, structure, core, thruster, weapon);
            GameObject module = CreateCube(record.RuntimeId, material);
            module.transform.SetParent(previewRoot.transform, false);
            module.transform.localPosition =
                GridAssemblyModel.ModuleCenter(record) * ModuleVisualScale;
            module.transform.localRotation =
                GridOrientation.Rotation(record.Pose.orientation);
            Vector3 footprint = record.Definition.Footprint;
            module.transform.localScale =
                footprint * (ModuleVisualScale * 0.92f);
            if (record.Definition.Category == GridModuleCategory.KineticWeapon)
                AddWeaponBarrel(module.transform, weapon);
            else if (record.Definition.Category == GridModuleCategory.MainThruster)
                AddThrusterGlow(module.transform, thruster);
        }

        previewBounds = CalculateRendererBounds(previewRoot);
        if (showShield)
            AddDynamicShield(previewBounds);
    }

    static Material ResolveMaterial(
        GridModuleCategory category,
        Material structure,
        Material core,
        Material thruster,
        Material weapon)
    {
        switch (category)
        {
            case GridModuleCategory.Core:
                return core;
            case GridModuleCategory.MainThruster:
            case GridModuleCategory.RcsThruster:
                return thruster;
            case GridModuleCategory.KineticWeapon:
                return weapon;
            default:
                return structure;
        }
    }

    GameObject CreateCube(string objectName, Material material)
    {
        GameObject result = GameObject.CreatePrimitive(PrimitiveType.Cube);
        result.name = objectName;
        result.layer = PreviewLayer;
        Collider collider = result.GetComponent<Collider>();
        if (collider != null)
            DestroyOwnedObject(collider);
        result.GetComponent<Renderer>().sharedMaterial = material;
        return result;
    }

    void AddWeaponBarrel(Transform module, Material material)
    {
        GameObject barrel = CreateCube("WeaponDirection", material);
        barrel.transform.SetParent(module, false);
        barrel.transform.localPosition = new Vector3(0f, 0f, 0.72f);
        barrel.transform.localScale = new Vector3(0.22f, 0.22f, 0.85f);
    }

    void AddThrusterGlow(Transform module, Material material)
    {
        GameObject glow = CreateCube("ThrusterExhaust", material);
        glow.transform.SetParent(module, false);
        glow.transform.localPosition = new Vector3(0f, 0f, 0.72f);
        glow.transform.localScale = new Vector3(0.52f, 0.52f, 0.36f);
    }

    void AddDynamicShield(Bounds bodyBounds)
    {
        Material shield = CreateShieldLineMaterial();
        Vector3 localCenter =
            previewRoot.transform.InverseTransformPoint(bodyBounds.center);
        Vector3 radii = bodyBounds.extents + Vector3.one * 4f;
        float width = Mathf.Max(0.35f, radii.magnitude * 0.012f);
        AddShieldRing("ShieldRing_XY", localCenter, radii, 0, width, shield);
        AddShieldRing("ShieldRing_XZ", localCenter, radii, 1, width, shield);
        AddShieldRing("ShieldRing_YZ", localCenter, radii, 2, width, shield);
    }

    Material CreateShieldLineMaterial()
    {
        Shader shader = Shader.Find("Sprites/Default") ??
                        Shader.Find("Universal Render Pipeline/Unlit") ??
                        Shader.Find("Unlit/Color");
        Material material = new Material(shader)
        {
            name = "BossPreview_DynamicShield",
            hideFlags = HideFlags.HideAndDontSave,
            renderQueue = 3100
        };
        SetMaterialColor(material, new Color(0.01f, 0.72f, 1f, 0.72f));
        materials.Add(material);
        return material;
    }

    void AddShieldRing(
        string ringName,
        Vector3 center,
        Vector3 radii,
        int plane,
        float width,
        Material material)
    {
        const int SegmentCount = 96;
        GameObject ringObject = new GameObject(ringName);
        ringObject.layer = PreviewLayer;
        ringObject.transform.SetParent(previewRoot.transform, false);
        LineRenderer line = ringObject.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = true;
        line.positionCount = SegmentCount;
        line.startWidth = width;
        line.endWidth = width;
        line.numCornerVertices = 2;
        line.numCapVertices = 2;
        line.alignment = LineAlignment.View;
        line.textureMode = LineTextureMode.Stretch;
        line.sharedMaterial = material;
        Color color = new Color(0.01f, 0.72f, 1f, 0.72f);
        line.startColor = color;
        line.endColor = color;
        for (int index = 0; index < SegmentCount; index++)
        {
            float angle = index * Mathf.PI * 2f / SegmentCount;
            float cosine = Mathf.Cos(angle);
            float sine = Mathf.Sin(angle);
            Vector3 point;
            switch (plane)
            {
                case 0:
                    point = new Vector3(radii.x * cosine, radii.y * sine, 0f);
                    break;
                case 1:
                    point = new Vector3(radii.x * cosine, 0f, radii.z * sine);
                    break;
                default:
                    point = new Vector3(0f, radii.y * cosine, radii.z * sine);
                    break;
            }
            line.SetPosition(index, center + point);
        }
    }

    Material CreateMaterial(
        string materialName,
        Color color,
        float metallic,
        float smoothness,
        bool emission)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ??
                        Shader.Find("Standard") ??
                        Shader.Find("Sprites/Default");
        Material material = new Material(shader)
        {
            name = materialName,
            hideFlags = HideFlags.HideAndDontSave
        };
        SetMaterialColor(material, color);
        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", metallic);
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", smoothness);
        if (material.HasProperty("_Glossiness"))
            material.SetFloat("_Glossiness", smoothness);
        if (emission)
        {
            material.EnableKeyword("_EMISSION");
            if (material.HasProperty("_EmissionColor"))
                material.SetColor("_EmissionColor", color * 2.2f);
        }
        materials.Add(material);
        return material;
    }

    Material CreateTransparentMaterial(string materialName, Color color)
    {
        Material material = CreateMaterial(
            materialName, color, 0.08f, 0.82f, true);
        material.renderQueue = 3000;
        if (material.HasProperty("_Surface"))
            material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_ZWrite"))
            material.SetFloat("_ZWrite", 0f);
        if (material.HasProperty("_SrcBlend"))
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend"))
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        return material;
    }

    static void SetMaterialColor(Material material, Color color)
    {
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
    }

    static Bounds CalculateRendererBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return new Bounds(root.transform.position, Vector3.one);
        Bounds bounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
            bounds.Encapsulate(renderers[index].bounds);
        return bounds;
    }

    void EvaluateBuild()
    {
        GridAssemblyValidation validation = currentBuild.Model.Validate();
        GridAssemblyMetrics metrics = currentBuild.Model.CalculateMetrics();
        Vector3Int span = ResolveStructureSpan(currentBuild);
        int expectedWeapons = currentBuild.Profile.WeaponCount;
        bool sixAxis = Enum.GetValues(typeof(ModularBossThrusterDirection))
            .Cast<ModularBossThrusterDirection>()
            .All(direction => currentBuild.CountThrusters(direction) ==
                              currentBuild.Profile.ThrustersPerDirection);
        bool nonCubic = span.x != span.y || span.y != span.z;
        bool moduleBudget = currentBuild.Model.Records.Count <=
                            currentBuild.Model.EffectiveModuleLimit;
        bool cpuBudget = validation.CpuCost <= currentBuild.Model.EffectiveCpuLimit;
        bool centered = metrics.localCenterOfMass.sqrMagnitude < 0.0001f;
        bool weaponCount = currentBuild.WeaponCount == expectedWeapons;
        allChecksPassed = validation.IsValid && sixAxis && nonCubic &&
                          moduleBudget && cpuBudget && centered && weaponCount;

        float shield = ModularBossCombatPolicy.ResolveShieldCapacity(difficultyTier);
        float buildingLow = ModularBossCombatPolicy.ResolveBuildingShieldDamageFraction(14f);
        float buildingHigh = ModularBossCombatPolicy.ResolveBuildingShieldDamageFraction(52f);
        float bridgeLow = ModularBossCombatPolicy.ResolveBridgeShieldDamageFraction(14f);
        float bridgeHigh = ModularBossCombatPolicy.ResolveBridgeShieldDamageFraction(52f);
        var report = new StringBuilder(640);
        report.AppendLine(allChecksPassed ? "[通过] Boss 生成与基础功能检查" : "[失败] Boss 检查未全部通过");
        report.AppendLine($"类型：{ArchetypeName}  难度：{difficultyTier}  种子：{seed}");
        report.AppendLine($"船体跨度：{span.x} × {span.y} × {span.z} 格（非立方体：{Pass(nonCubic)}）");
        report.AppendLine($"模块：{currentBuild.Model.Records.Count}/{currentBuild.Model.EffectiveModuleLimit}  CPU：{validation.CpuCost}/{currentBuild.Model.EffectiveCpuLimit}");
        report.AppendLine($"武器：{currentBuild.WeaponCount}/{expectedWeapons}  武器耐久倍率：×{currentBuild.Profile.WeaponIntegrityMultiplier:0.##}");
        report.AppendLine($"六轴推进：{Pass(sixAxis)}  重心：{metrics.localCenterOfMass:F4}（居中：{Pass(centered)}）");
        report.AppendLine($"护盾：{shield:0}  充能次数：{ModularBossCombatPolicy.ResolveShieldRechargeCount(difficultyTier)}");
        report.AppendLine($"撞楼削盾：{buildingLow:P0}–{buildingHigh:P0}  撞廊桥削盾：{bridgeLow:P0}–{bridgeHigh:P0}");
        report.AppendLine(TacticalHint);
        lastReport = report.ToString();
        lastError = validation.IsValid ? string.Empty : validation.Message;
    }

    static string Pass(bool value) => value ? "通过" : "失败";

    static Vector3Int ResolveStructureSpan(ModularBossBuildResult build)
    {
        List<Vector3Int> cells = build.Model.Records
            .Where(record => record.Definition.Category == GridModuleCategory.Structure)
            .SelectMany(build.Model.GetCells)
            .ToList();
        if (cells.Count == 0)
            return Vector3Int.zero;
        Vector3Int min = cells[0];
        Vector3Int max = cells[0];
        foreach (Vector3Int cell in cells)
        {
            min = Vector3Int.Min(min, cell);
            max = Vector3Int.Max(max, cell);
        }
        return max - min + Vector3Int.one;
    }

    static void DestroyOwnedObject(UnityEngine.Object target)
    {
        if (target == null)
            return;
        if (Application.isPlaying)
            Destroy(target);
        else
            DestroyImmediate(target);
    }
}
