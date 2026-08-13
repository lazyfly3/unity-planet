using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityPlanet.ModularAssembly;

/// <summary>
/// Immutable, mission-local tuning for one facility-assault objective.
/// It deliberately does not modify GridModuleDefinition assets: the same
/// player module art is reused while facility durability remains independent.
/// </summary>
[Serializable]
public sealed class FacilityAssaultDifficultySpec
{
    public const int MinimumTier = 0;
    public const int MaximumTier = 5;
    public const int ModulesPerLayer =
        FacilityAssaultShellLayout.DirectionsPerLayer;
    public const int MaximumModulesPerFacility = 78;

    FacilityAssaultDifficultySpec(
        int tier,
        int shellLayers,
        float moduleWorldSize,
        float moduleIntegrity,
        float coreIntegrity)
    {
        Tier = tier;
        ShellLayers = shellLayers;
        ModuleWorldSize = moduleWorldSize;
        ModuleIntegrity = moduleIntegrity;
        CoreIntegrity = coreIntegrity;
        ModuleCount = Mathf.Min(
            MaximumModulesPerFacility,
            ShellLayers * ModulesPerLayer);
        RequiredHalfExtents = Vector3.one *
            (ShellLayers * ModuleWorldSize + ModuleWorldSize * 0.5f);
    }

    public int Tier { get; }
    public int ShellLayers { get; }
    public int ModuleCount { get; }
    public float ModuleWorldSize { get; }
    public float ModuleIntegrity { get; }
    public float CoreIntegrity { get; }

    /// <summary>
    /// Conservative half extents for the future city placement validator.
    /// </summary>
    public Vector3 RequiredHalfExtents { get; }

    public static FacilityAssaultDifficultySpec Resolve(int difficultyTier)
    {
        int tier = Mathf.Clamp(
            difficultyTier,
            MinimumTier,
            MaximumTier);
        int layers = 1 + tier / 2;
        return new FacilityAssaultDifficultySpec(
            tier,
            layers,
            5f,
            100f + tier * 30f,
            350f + tier * 90f);
    }
}

/// <summary>
/// Builds a compact, face-connected armor cage around the objective core.
/// The first layer is the complete 3x3x3 surface without its centre. Every
/// later module grows exactly one grid step from its matching previous-layer
/// module, so no visual cell can float away from the structure.
/// </summary>
public static class FacilityAssaultShellLayout
{
    public const int DirectionsPerLayer = 26;
    public const int BreachLaneCount = 5;
    static readonly int[] BreachDirectionIndices = { 0, 1, 3, 4, 5 };

    // Cardinal directions are intentionally first: one module from every
    // layer with the same slot forms an authored straight breach lane.
    static readonly Vector3Int[] BaseDirections =
    {
        new Vector3Int(-1, 0, 0), new Vector3Int(1, 0, 0),
        new Vector3Int(0, -1, 0), new Vector3Int(0, 1, 0),
        new Vector3Int(0, 0, -1), new Vector3Int(0, 0, 1),
        new Vector3Int(-1, -1, 0), new Vector3Int(-1, 1, 0),
        new Vector3Int(1, -1, 0), new Vector3Int(1, 1, 0),
        new Vector3Int(-1, 0, -1), new Vector3Int(-1, 0, 1),
        new Vector3Int(1, 0, -1), new Vector3Int(1, 0, 1),
        new Vector3Int(0, -1, -1), new Vector3Int(0, -1, 1),
        new Vector3Int(0, 1, -1), new Vector3Int(0, 1, 1),
        new Vector3Int(-1, -1, -1), new Vector3Int(-1, -1, 1),
        new Vector3Int(-1, 1, -1), new Vector3Int(-1, 1, 1),
        new Vector3Int(1, -1, -1), new Vector3Int(1, -1, 1),
        new Vector3Int(1, 1, -1), new Vector3Int(1, 1, 1)
    };

    public static IReadOnlyList<Vector3Int> CreatePositions(
        int requestedLayers,
        int objectiveIndex)
    {
        int layers = Mathf.Clamp(requestedLayers, 1, 3);
        var positions = new List<Vector3Int>(
            layers * DirectionsPerLayer);
        for (int layer = 1; layer <= layers; layer++)
        {
            for (int directionIndex = 0;
                 directionIndex < BaseDirections.Length;
                 directionIndex++)
            {
                positions.Add(ResolveLayerPosition(
                    BaseDirections[directionIndex],
                    layer,
                    objectiveIndex));
            }
        }
        return positions;
    }

    public static int ResolveLayer(int moduleIndex)
    {
        return Mathf.Max(0, moduleIndex) / DirectionsPerLayer + 1;
    }

    public static int ResolveDirectionIndex(int moduleIndex)
    {
        return PositiveModulo(moduleIndex, DirectionsPerLayer);
    }

    public static bool IsBreachDirection(int directionIndex)
    {
        for (int index = 0; index < BreachDirectionIndices.Length; index++)
        {
            if (BreachDirectionIndices[index] == directionIndex)
                return true;
        }
        return false;
    }

    public static int ResolveBreachDirectionIndex(int breachLaneIndex)
    {
        if (breachLaneIndex < 0 ||
            breachLaneIndex >= BreachDirectionIndices.Length)
        {
            return -1;
        }
        return BreachDirectionIndices[breachLaneIndex];
    }

    static Vector3Int ResolveLayerPosition(
        Vector3Int direction,
        int layer,
        int objectiveIndex)
    {
        Vector3Int position = direction;
        if (layer <= 1)
            return position;

        int[] axes = new int[3];
        int axisCount = 0;
        if (direction.x != 0)
            axes[axisCount++] = 0;
        if (direction.y != 0)
            axes[axisCount++] = 1;
        if (direction.z != 0)
            axes[axisCount++] = 2;

        Vector3Int canonical = CanonicalPairDirection(direction);
        int stable = canonical.x * 73856093 ^
                     canonical.y * 19349663 ^
                     canonical.z * 83492791 ^
                     objectiveIndex * 486187739;
        int firstAxis = PositiveModulo(stable, axisCount);
        for (int step = 1; step < layer; step++)
        {
            int axis = axes[(firstAxis + step - 1) % axisCount];
            int sign = axis == 0
                ? Math.Sign(direction.x)
                : axis == 1
                    ? Math.Sign(direction.y)
                    : Math.Sign(direction.z);
            if (axis == 0)
                position.x += sign;
            else if (axis == 1)
                position.y += sign;
            else
                position.z += sign;
        }
        return position;
    }

    static Vector3Int CanonicalPairDirection(Vector3Int direction)
    {
        int first = direction.x != 0
            ? direction.x
            : direction.y != 0
                ? direction.y
                : direction.z;
        return first < 0 ? -direction : direction;
    }

    static int PositiveModulo(int value, int modulus)
    {
        if (modulus <= 0)
            return 0;
        int result = value % modulus;
        return result < 0 ? result + modulus : result;
    }
}

/// <summary>
/// A stationary facility assembled from production Modular visuals. This is
/// not a vehicle: it owns no Rigidbody, ShipAssembly, GridAssemblyModel or
/// VehicleStructureGraph, so it cannot change the player's flight physics or
/// participate in vehicle connectivity/save logic.
/// </summary>
[DisallowMultipleComponent]
public sealed class FinitePlanetFacilityAssaultObjective : MonoBehaviour
{
    public const string ArmorSourceId = "block:common:block_111";
    public const string CoreSourceId = "block:core:core_energy_111";

    const float LogicalColliderFill = 0.94f;
    static readonly List<FinitePlanetFacilityAssaultObjective>
        activeObjectives =
            new List<FinitePlanetFacilityAssaultObjective>(3);

    readonly List<FinitePlanetFacilityArmorModule> armorModules =
        new List<FinitePlanetFacilityArmorModule>(
            FacilityAssaultDifficultySpec.MaximumModulesPerFacility);

    FinitePlanetEnergyCoreObjective core;
    GameObject coreVisual;
    Transform generatedRoot;
    bool building;
    bool built;
    bool destroyedRaised;
    float observedCoreIntegrity;

    public event Action<FinitePlanetFacilityAssaultObjective> Destroyed;
    public event Action<FinitePlanetFacilityAssaultObjective, float> Damaged;
    public event Action<FinitePlanetFacilityArmorModule>
        ArmorModuleDestroyed;

    public int ObjectiveIndex { get; private set; } = -1;
    public static IReadOnlyList<FinitePlanetFacilityAssaultObjective>
        ActiveObjectives => activeObjectives;
    public Vector3 GroundAnchorWorldPosition { get; private set; }
    public FacilityAssaultDifficultySpec DifficultySpec { get; private set; }
    public FinitePlanetEnergyCoreObjective Core => core;
    public IReadOnlyList<FinitePlanetFacilityArmorModule> ArmorModules =>
        armorModules;
    public bool IsBuilt => built;
    public bool IsDestroyed => core != null && core.IsDestroyed;
    public int ArmorModuleCount => armorModules.Count;
    public int DestroyedArmorModuleCount =>
        Mathf.Max(0, ArmorModuleCount - LiveArmorModuleCount);
    public int RequiredDestroyedArmorModulesToExposeCore =>
        DifficultySpec != null
            ? DifficultySpec.ShellLayers
            : int.MaxValue;
    public int BestBreachDepth
    {
        get
        {
            if (DifficultySpec == null)
                return 0;
            int best = 0;
            for (int lane = 0;
                 lane < FacilityAssaultShellLayout.BreachLaneCount;
                 lane++)
            {
                int direction = FacilityAssaultShellLayout
                    .ResolveBreachDirectionIndex(lane);
                int destroyedInLane = 0;
                for (int layer = 0;
                     layer < DifficultySpec.ShellLayers;
                     layer++)
                {
                    int moduleIndex =
                        layer * FacilityAssaultShellLayout.DirectionsPerLayer +
                        direction;
                    if (moduleIndex < armorModules.Count &&
                        armorModules[moduleIndex] != null &&
                        armorModules[moduleIndex].IsDestroyed)
                    {
                        destroyedInLane++;
                    }
                }
                best = Mathf.Max(best, destroyedInLane);
            }
            return best;
        }
    }
    public bool CoreExposed =>
        built && BestBreachDepth >=
        RequiredDestroyedArmorModulesToExposeCore;

    public bool TryResolveAutoAimTarget(
        Vector3 observerPosition,
        out Transform target)
    {
        if (IsDestroyed)
        {
            target = null;
            return false;
        }
        if (CoreExposed && core != null && !core.IsDestroyed)
        {
            target = core.transform;
            return true;
        }
        float bestDistance = float.PositiveInfinity;
        target = null;
        for (int index = 0; index < armorModules.Count; index++)
        {
            FinitePlanetFacilityArmorModule module = armorModules[index];
            if (module == null || module.IsDestroyed)
                continue;
            float distance = (module.transform.position - observerPosition)
                .sqrMagnitude;
            if (distance >= bestDistance)
                continue;
            bestDistance = distance;
            target = module.transform;
        }
        return target != null;
    }

    public bool TryResolveNearestLiveArmorTarget(
        Vector3 observerPosition,
        out Transform target)
    {
        float bestDistance = float.PositiveInfinity;
        target = null;
        int shellLayers = DifficultySpec != null
            ? DifficultySpec.ShellLayers
            : 0;
        // Auto aim should teach the authored solution instead of selecting a
        // random corner cell. For each of the six straight breach lanes only
        // the outermost remaining cell is a useful target; once it breaks the
        // next cell in the same lane becomes selectable.
        for (int lane = 0;
             lane < FacilityAssaultShellLayout.BreachLaneCount;
             lane++)
        {
            int direction = FacilityAssaultShellLayout
                .ResolveBreachDirectionIndex(lane);
            FinitePlanetFacilityArmorModule module = null;
            for (int layer = shellLayers - 1; layer >= 0; layer--)
            {
                int moduleIndex =
                    layer * FacilityAssaultShellLayout.DirectionsPerLayer +
                    direction;
                if (moduleIndex < 0 || moduleIndex >= armorModules.Count)
                    continue;
                FinitePlanetFacilityArmorModule candidate =
                    armorModules[moduleIndex];
                if (candidate == null || candidate.IsDestroyed)
                    continue;
                module = candidate;
                break;
            }
            if (module == null || module.IsDestroyed)
                continue;
            float distance = (module.transform.position - observerPosition)
                .sqrMagnitude;
            if (distance >= bestDistance)
                continue;
            bestDistance = distance;
            target = module.transform;
        }
        return target != null;
    }

    void OnEnable()
    {
        if (!activeObjectives.Contains(this))
            activeObjectives.Add(this);
    }

    void OnDisable()
    {
        activeObjectives.Remove(this);
    }

    public int LiveArmorModuleCount
    {
        get
        {
            int count = 0;
            for (int index = 0; index < armorModules.Count; index++)
            {
                FinitePlanetFacilityArmorModule module =
                    armorModules[index];
                if (module != null && !module.IsDestroyed)
                    count++;
            }
            return count;
        }
    }

    /// <summary>
    /// Stable world-space anchor for a later screen-space objective icon.
    /// </summary>
    public Vector3 MarkerWorldPosition
    {
        get
        {
            float height = DifficultySpec != null
                ? DifficultySpec.RequiredHalfExtents.y + 12f
                : 12f;
            return transform.TransformPoint(Vector3.up * height);
        }
    }

    public string StatusLabel
    {
        get
        {
            string label = "设施核心 " + (ObjectiveIndex + 1);
            if (!built)
                return label + " · 正在部署";
            if (IsDestroyed)
                return label + " · 已摧毁";
            float coreRatio = core == null ||
                              core.MaximumIntegrity <= 0.01f
                ? 0f
                : Mathf.Clamp01(
                    core.Integrity / core.MaximumIntegrity);
            if (!CoreExposed)
            {
                return label + " · 突破装甲层 " +
                       BestBreachDepth + "/" +
                       RequiredDestroyedArmorModulesToExposeCore;
            }
            return label + " · 核心已暴露 " +
                   Mathf.RoundToInt(coreRatio * 100f) + "%";
        }
    }

    public IEnumerator Build(
        ModularContentService contentService,
        IReadOnlyDictionary<string, ModularContentRecord> contentRecords,
        int difficultyTier,
        int objectiveIndex,
        Action<bool, string> completed)
    {
        yield return Build(
            contentService,
            contentRecords,
            difficultyTier,
            objectiveIndex,
            transform.position,
            transform.rotation,
            completed);
    }

    public IEnumerator Build(
        ModularContentService contentService,
        IReadOnlyDictionary<string, ModularContentRecord> contentRecords,
        int difficultyTier,
        int objectiveIndex,
        Vector3 worldGroundPosition,
        Quaternion worldRotation,
        Action<bool, string> completed)
    {
        if (building)
        {
            completed?.Invoke(false, "设施目标正在部署，不能重复创建。");
            yield break;
        }
        if (built)
        {
            completed?.Invoke(false, "设施目标已经部署完成。");
            yield break;
        }
        if (contentService == null || contentService.Catalog == null)
        {
            completed?.Invoke(false, "设施目标缺少已初始化的模块内容服务。");
            yield break;
        }
        if (!TryResolveRecord(
                contentRecords,
                ArmorSourceId,
                out ModularContentRecord armorRecord))
        {
            completed?.Invoke(
                false,
                "模块目录缺少设施外壳资源：" + ArmorSourceId);
            yield break;
        }
        if (!TryResolveRecord(
                contentRecords,
                CoreSourceId,
                out ModularContentRecord coreRecord))
        {
            completed?.Invoke(
                false,
                "模块目录缺少设施核心资源：" + CoreSourceId);
            yield break;
        }

        building = true;
        ObjectiveIndex = Mathf.Max(0, objectiveIndex);
        DifficultySpec = FacilityAssaultDifficultySpec.Resolve(
            difficultyTier);
        GroundAnchorWorldPosition = worldGroundPosition;
        // The placement API supplies the supporting surface, not an arbitrary
        // cube centre. Lift by the complete shell half-height so no lower
        // armor module is silently buried in the city ground or a roof.
        transform.SetPositionAndRotation(
            worldGroundPosition +
            worldRotation * Vector3.up *
            DifficultySpec.RequiredHalfExtents.y,
            worldRotation);
        VehicleCombatTeamUtility.SetTeam(
            gameObject,
            VehicleCombatTeam.Enemy);

        generatedRoot = new GameObject(
            "FacilityAssembly_设施模块结构").transform;
        generatedRoot.SetParent(transform, false);
        Transform prewarmRoot = new GameObject(
            "ModuleVisualPrewarm_模块视觉预热").transform;
        prewarmRoot.SetParent(generatedRoot, false);
        prewarmRoot.gameObject.SetActive(false);

        bool armorPrepared = false;
        string armorPreparationError = string.Empty;
        yield return EnsurePreparedVisual(
            contentService,
            armorRecord,
            prewarmRoot,
            (success, error) =>
            {
                armorPrepared = success;
                armorPreparationError = error;
            });
        if (!armorPrepared)
        {
            FailBuild(
                "设施外壳模块视觉无法预热：" +
                armorPreparationError,
                completed);
            yield break;
        }

        bool corePrepared = false;
        string corePreparationError = string.Empty;
        yield return EnsurePreparedVisual(
            contentService,
            coreRecord,
            prewarmRoot,
            (success, error) =>
            {
                corePrepared = success;
                corePreparationError = error;
            });
        if (!corePrepared)
        {
            FailBuild(
                "设施核心模块视觉无法预热：" +
                corePreparationError,
                completed);
            yield break;
        }

        DestroySafely(prewarmRoot.gameObject);
        if (!BuildCore(contentService, coreRecord, out string coreError))
        {
            FailBuild(coreError, completed);
            yield break;
        }
        if (!BuildArmorShell(
                contentService,
                armorRecord,
                out string armorError))
        {
            FailBuild(armorError, completed);
            yield break;
        }

        building = false;
        built = true;
        StartCoroutine(ObserveCoreDamage());
        completed?.Invoke(true, string.Empty);
    }

    bool BuildCore(
        ModularContentService contentService,
        ModularContentRecord coreRecord,
        out string error)
    {
        error = string.Empty;
        GameObject coreRoot = new GameObject(
            "FacilityCore_中央能源核心_" +
            ObjectiveIndex.ToString("D2"));
        coreRoot.transform.SetParent(generatedRoot, false);
        core = coreRoot.AddComponent<FinitePlanetEnergyCoreObjective>();
        core.Configure(
            DifficultySpec.CoreIntegrity,
            ObjectiveIndex,
            DifficultySpec.ModuleWorldSize * 0.42f,
            false);
        core.SetDamageColliderEnabled(false);
        observedCoreIntegrity = core.Integrity;
        core.Destroyed -= HandleCoreDestroyed;
        core.Destroyed += HandleCoreDestroyed;

        if (!contentService.TryInstantiatePrepared(
                coreRecord,
                coreRoot.transform,
                out GameObject visual) ||
            visual == null)
        {
            error = "设施核心模块的预热视觉不可用。";
            return false;
        }
        coreVisual = visual;
        PrepareVisualOnlyInstance(
            visual,
            coreRoot.transform,
            DifficultySpec.ModuleWorldSize);
        return true;
    }

    bool BuildArmorShell(
        ModularContentService contentService,
        ModularContentRecord armorRecord,
        out string error)
    {
        error = string.Empty;
        IReadOnlyList<Vector3Int> positions =
            FacilityAssaultShellLayout.CreatePositions(
                DifficultySpec.ShellLayers,
                ObjectiveIndex);
        for (int moduleIndex = 0;
             moduleIndex < positions.Count;
             moduleIndex++)
        {
            int layer = FacilityAssaultShellLayout.ResolveLayer(moduleIndex);
            int directionIndex =
                FacilityAssaultShellLayout.ResolveDirectionIndex(
                    moduleIndex);
            Vector3Int gridPosition = positions[moduleIndex];
            GameObject moduleRoot = new GameObject(
                "FacilityArmor_外壳模块_L" + layer + "_" +
                moduleIndex.ToString("D2"));
            moduleRoot.transform.SetParent(generatedRoot, false);
            moduleRoot.transform.localPosition =
                (Vector3)gridPosition *
                DifficultySpec.ModuleWorldSize;

            if (!contentService.TryInstantiatePrepared(
                    armorRecord,
                    moduleRoot.transform,
                    out GameObject visual) ||
                visual == null)
            {
                error = "设施外壳模块的预热视觉不可用。";
                return false;
            }
            PrepareVisualOnlyInstance(
                visual,
                moduleRoot.transform,
                DifficultySpec.ModuleWorldSize);

            BoxCollider hitCollider =
                moduleRoot.AddComponent<BoxCollider>();
            hitCollider.center = Vector3.zero;
            hitCollider.size = Vector3.one *
                (DifficultySpec.ModuleWorldSize *
                 LogicalColliderFill);
            hitCollider.isTrigger = false;

            FinitePlanetFacilityArmorModule module =
                moduleRoot.AddComponent<
                    FinitePlanetFacilityArmorModule>();
            module.Configure(
                DifficultySpec.ModuleIntegrity,
                ObjectiveIndex,
                moduleIndex,
                layer,
                directionIndex,
                gridPosition,
                DifficultySpec.ModuleWorldSize,
                hitCollider,
                visual);
            module.Destroyed -= HandleArmorModuleDestroyed;
            module.Destroyed += HandleArmorModuleDestroyed;
            module.Damaged -= HandleArmorModuleDamaged;
            module.Damaged += HandleArmorModuleDamaged;
            armorModules.Add(module);
        }

        if (armorModules.Count != DifficultySpec.ModuleCount ||
            armorModules.Count >
            FacilityAssaultDifficultySpec.MaximumModulesPerFacility)
        {
            error = "设施外壳模块数量没有满足难度预算。";
            return false;
        }
        return true;
    }

    static IEnumerator EnsurePreparedVisual(
        ModularContentService contentService,
        ModularContentRecord record,
        Transform prewarmRoot,
        Action<bool, string> completed)
    {
        if (contentService.TryInstantiatePrepared(
                record,
                prewarmRoot,
                out GameObject existing) &&
            existing != null)
        {
            DestroySafely(existing);
            completed?.Invoke(true, string.Empty);
            yield break;
        }

        GameObject warmed = null;
        yield return contentService.InstantiateAsync(
            record,
            prewarmRoot,
            instance => warmed = instance);
        if (warmed == null)
        {
            completed?.Invoke(
                false,
                "模块内容服务没有返回视觉实例。");
            yield break;
        }
        DestroySafely(warmed);

        bool prepared = contentService.TryInstantiatePrepared(
            record,
            prewarmRoot,
            out GameObject validationClone);
        if (validationClone != null)
            DestroySafely(validationClone);
        completed?.Invoke(
            prepared,
            prepared
                ? string.Empty
                : "模块资源未进入可复用的预热缓存。");
    }

    static void PrepareVisualOnlyInstance(
        GameObject visual,
        Transform logicalRoot,
        float worldSize)
    {
        if (visual == null)
            return;
        visual.name += "_仅外观";

        Collider[] colliders =
            visual.GetComponentsInChildren<Collider>(true);
        for (int index = 0; index < colliders.Length; index++)
        {
            if (colliders[index] != null)
                colliders[index].enabled = false;
        }
        Rigidbody[] rigidbodies =
            visual.GetComponentsInChildren<Rigidbody>(true);
        for (int index = 0; index < rigidbodies.Length; index++)
        {
            Rigidbody body = rigidbodies[index];
            if (body == null)
                continue;
            body.isKinematic = true;
            body.detectCollisions = false;
            DestroySafely(body);
        }
        MonoBehaviour[] behaviours =
            visual.GetComponentsInChildren<MonoBehaviour>(true);
        for (int index = 0; index < behaviours.Length; index++)
        {
            MonoBehaviour behaviour = behaviours[index];
            if (behaviour != null)
                behaviour.enabled = false;
        }

        visual.transform.localScale *= Mathf.Max(0.1f, worldSize);
        RecenterVisual(visual, logicalRoot);
    }

    static void RecenterVisual(GameObject visual, Transform logicalRoot)
    {
        Renderer[] renderers =
            visual.GetComponentsInChildren<Renderer>(true);
        bool initialized = false;
        Bounds localBounds = new Bounds();
        for (int rendererIndex = 0;
             rendererIndex < renderers.Length;
             rendererIndex++)
        {
            Renderer renderer = renderers[rendererIndex];
            if (renderer == null)
                continue;
            Bounds bounds = renderer.bounds;
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 worldCorner = new Vector3(
                    (corner & 1) == 0 ? min.x : max.x,
                    (corner & 2) == 0 ? min.y : max.y,
                    (corner & 4) == 0 ? min.z : max.z);
                Vector3 localCorner =
                    logicalRoot.InverseTransformPoint(worldCorner);
                if (!initialized)
                {
                    localBounds = new Bounds(
                        localCorner,
                        Vector3.zero);
                    initialized = true;
                }
                else
                {
                    localBounds.Encapsulate(localCorner);
                }
            }
        }
        if (initialized)
            visual.transform.localPosition -= localBounds.center;
    }

    static bool TryResolveRecord(
        IReadOnlyDictionary<string, ModularContentRecord> records,
        string sourceId,
        out ModularContentRecord record)
    {
        record = null;
        if (records == null || string.IsNullOrWhiteSpace(sourceId))
            return false;
        foreach (KeyValuePair<string, ModularContentRecord> pair in records)
        {
            ModularContentRecord candidate = pair.Value;
            if (candidate != null && string.Equals(
                    candidate.sourceId,
                    sourceId,
                    StringComparison.OrdinalIgnoreCase))
            {
                record = candidate;
                return true;
            }
        }
        return false;
    }

    void HandleCoreDestroyed(FinitePlanetEnergyCoreObjective value)
    {
        if (destroyedRaised)
            return;
        if (coreVisual != null)
            coreVisual.SetActive(false);
        Vector3 effectPosition = core != null
            ? core.transform.position
            : transform.position;
        NeoXCombatFeedbackRuntime.TrySpawnModuleBreak(
            effectPosition,
            Vector3.up,
            DifficultySpec != null
                ? DifficultySpec.ModuleWorldSize * 1.35f
                : 6f);
        destroyedRaised = true;
        Destroyed?.Invoke(this);
    }

    void HandleArmorModuleDestroyed(
        FinitePlanetFacilityArmorModule module)
    {
        if (CoreExposed && core != null)
            core.SetDamageColliderEnabled(true);
        ArmorModuleDestroyed?.Invoke(module);
    }

    void HandleArmorModuleDamaged(
        FinitePlanetFacilityArmorModule module,
        float amount)
    {
        if (amount > 0f)
            Damaged?.Invoke(this, amount);
    }

    public void ApplyCoreDamage(SpaceDamageInfo damage)
    {
        if (core == null || core.IsDestroyed || !CoreExposed ||
            damage.amount <= 0f)
        {
            return;
        }
        core.ApplyDamageAuthoritative(damage);
    }

    IEnumerator ObserveCoreDamage()
    {
        // The legacy core owns authoritative health but has no damage event.
        // Three facilities make this comparison effectively free and let the
        // EDPCG pacing bridge react to real core hits without modifying that
        // established objective class.
        while (built && core != null && !core.IsDestroyed)
        {
            float current = core.Integrity;
            float applied = Mathf.Max(
                0f,
                observedCoreIntegrity - current);
            observedCoreIntegrity = current;
            if (applied > 0.001f)
                Damaged?.Invoke(this, applied);
            yield return null;
        }
    }

    void FailBuild(string error, Action<bool, string> completed)
    {
        building = false;
        built = false;
        if (core != null)
            core.Destroyed -= HandleCoreDestroyed;
        core = null;
        coreVisual = null;
        for (int index = 0; index < armorModules.Count; index++)
        {
            if (armorModules[index] != null)
            {
                armorModules[index].Destroyed -=
                    HandleArmorModuleDestroyed;
                armorModules[index].Damaged -=
                    HandleArmorModuleDamaged;
            }
        }
        armorModules.Clear();
        if (generatedRoot != null)
            DestroySafely(generatedRoot.gameObject);
        generatedRoot = null;
        completed?.Invoke(false, error ?? "设施目标部署失败。");
    }

    static void DestroySafely(UnityEngine.Object value)
    {
        if (value == null)
            return;
        if (Application.isPlaying)
            UnityEngine.Object.Destroy(value);
        else
            UnityEngine.Object.DestroyImmediate(value);
    }

    void OnDestroy()
    {
        if (core != null)
            core.Destroyed -= HandleCoreDestroyed;
        for (int index = 0; index < armorModules.Count; index++)
        {
            if (armorModules[index] != null)
            {
                armorModules[index].Destroyed -=
                    HandleArmorModuleDestroyed;
                armorModules[index].Damaged -=
                    HandleArmorModuleDamaged;
            }
        }
    }
}

/// <summary>
/// One stationary armor cell. It owns one logical BoxCollider and one health
/// pool; the imported module hierarchy beneath it is presentation-only.
/// </summary>
[DisallowMultipleComponent]
public sealed class FinitePlanetFacilityArmorModule :
    MonoBehaviour,
    ISpaceDamageable
{
    BoxCollider hitCollider;
    GameObject visualRoot;
    float integrity;
    float maximumIntegrity;
    float visualSize;
    bool destroyedRaised;

    public event Action<FinitePlanetFacilityArmorModule> Destroyed;
    public event Action<FinitePlanetFacilityArmorModule, float> Damaged;

    public int ObjectiveIndex { get; private set; }
    public int ModuleIndex { get; private set; }
    public int ShellLayer { get; private set; }
    public int ShellDirectionIndex { get; private set; }
    public Vector3Int GridPosition { get; private set; }
    public float Integrity => integrity;
    public float MaximumIntegrity => maximumIntegrity;
    public bool IsDestroyed { get; private set; }
    public BoxCollider HitCollider => hitCollider;

    public void Configure(
        float health,
        int objectiveIndex,
        int moduleIndex,
        int shellLayer,
        int shellDirectionIndex,
        Vector3Int gridPosition,
        float moduleVisualSize,
        BoxCollider logicalCollider,
        GameObject visual)
    {
        maximumIntegrity = Mathf.Max(1f, health);
        integrity = maximumIntegrity;
        ObjectiveIndex = Mathf.Max(0, objectiveIndex);
        ModuleIndex = Mathf.Max(0, moduleIndex);
        ShellLayer = Mathf.Max(1, shellLayer);
        ShellDirectionIndex = Mathf.Clamp(
            shellDirectionIndex,
            0,
            FacilityAssaultShellLayout.DirectionsPerLayer - 1);
        GridPosition = gridPosition;
        visualSize = Mathf.Max(0.1f, moduleVisualSize);
        hitCollider = logicalCollider;
        visualRoot = visual;
        IsDestroyed = false;
        destroyedRaised = false;
        VehicleCombatTeamUtility.SetTeam(
            gameObject,
            VehicleCombatTeam.Enemy);
    }

    public void ApplyDamage(SpaceDamageInfo damage)
    {
        if (IsDestroyed || damage.amount <= 0f)
            return;
        if (damage.source != null &&
            VehicleCombatTeamUtility.AreFriendly(
                damage.source,
                transform))
        {
            return;
        }

        float previousIntegrity = integrity;
        integrity = Mathf.Max(0f, integrity - damage.amount);
        float appliedDamage = previousIntegrity - integrity;
        if (appliedDamage > 0f)
            Damaged?.Invoke(this, appliedDamage);
        if (integrity > 0f)
            return;

        IsDestroyed = true;
        if (hitCollider != null)
            hitCollider.enabled = false;
        if (visualRoot != null)
            visualRoot.SetActive(false);

        Vector3 breakDirection = damage.impulse.sqrMagnitude > 0.001f
            ? damage.impulse.normalized
            : (transform.position - damage.point).normalized;
        if (breakDirection.sqrMagnitude < 0.001f)
            breakDirection = Vector3.up;
        NeoXCombatFeedbackRuntime.TrySpawnModuleBreak(
            transform.position,
            breakDirection,
            visualSize);

        if (destroyedRaised)
            return;
        destroyedRaised = true;
        Destroyed?.Invoke(this);
    }
}
