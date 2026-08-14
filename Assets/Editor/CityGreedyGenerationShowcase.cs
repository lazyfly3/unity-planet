using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityPlanet.CityPcg;

/// <summary>
/// 城市逐格贪心生成的纯编辑器回放器。它复用现有预览城市的临时层级，
/// 只开关渲染器与碰撞体，不创建或保存场景对象。
/// </summary>
[InitializeOnLoad]
public static class CityGreedyGenerationShowcase
{
    const float SignificantRecalculationThreshold = 0.005f;
    const float RecalculationScanEnd = 0.22f;
    const float RecalculationSequenceEnd = 0.62f;
    const float RecalculationSummaryStart = 0.70f;

    public sealed class CellChange
    {
        public CombatCityBlockPlan block;
        public float before;
        public float after;
        public float Delta => after - before;
    }

    public sealed class Step
    {
        public CombatCityBlockPlan block;
        public float cityDifficultyBefore;
        public float cityDifficultyAfter;
        public float generatedDifficultyBefore;
        public float generatedDifficultyAfter;
        public float selectedCellDifficulty;
        public float targetDifficulty;
        public float remainingRequiredBefore;
        public float remainingRequiredAfter;
        public bool requestedMoreDanger;
        public bool directionSatisfied;
        public float solverPredictionBefore;
        public float solverPredictionAfter;
        public int candidatesTested;
        public readonly List<CellChange> recalculatedCells =
            new List<CellChange>(36);
    }

    public sealed class FinalTuningStage
    {
        public string title = string.Empty;
        public string purpose = string.Empty;
        public string decision = string.Empty;
        public float difficultyBefore;
        public float difficultyAfter;
        public int acceptedCount;
    }

    public sealed class Trace
    {
        public readonly List<Step> steps = new List<Step>(36);
        public readonly List<FinalTuningStage> finalStages =
            new List<FinalTuningStage>(3);
        public float requestedDifficulty;
        public float targetDifficulty;
        public float forecastBaselineDifficulty;
        public int seed;
        public double analysisMilliseconds;
    }

    sealed class ComponentState<T> where T : Component
    {
        public T component;
        public bool originalEnabled;
        public int generationOrder;
        public float revealThreshold;
    }

    sealed class BuildingPresentation
    {
        public int generationOrder;
        public float revealThreshold;
        public readonly List<ComponentState<Renderer>> renderers =
            new List<ComponentState<Renderer>>(8);
        public readonly List<ComponentState<Collider>> colliders =
            new List<ComponentState<Collider>>(8);
    }

    static readonly List<BuildingPresentation> Buildings =
        new List<BuildingPresentation>(512);
    static readonly List<ComponentState<Renderer>> SkybridgeRenderers =
        new List<ComponentState<Renderer>>(512);
    static readonly List<ComponentState<Collider>> SkybridgeColliders =
        new List<ComponentState<Collider>>(512);
    static readonly List<ComponentState<Renderer>> CableRenderers =
        new List<ComponentState<Renderer>>(256);
    static readonly List<ComponentState<Collider>> CableColliders =
        new List<ComponentState<Collider>>(256);
    static readonly List<ComponentState<Renderer>> WindRenderers =
        new List<ComponentState<Renderer>>(128);
    static readonly List<ComponentState<Collider>> WindColliders =
        new List<ComponentState<Collider>>(128);
    static readonly List<ComponentState<Renderer>> DecorationRenderers =
        new List<ComponentState<Renderer>>(1024);
    static readonly List<ComponentState<Collider>> DecorationColliders =
        new List<ComponentState<Collider>>(1024);
    static readonly List<ComponentState<Renderer>> AuxiliaryRenderers =
        new List<ComponentState<Renderer>>(256);
    static readonly List<ComponentState<Collider>> AuxiliaryColliders =
        new List<ComponentState<Collider>>(256);

    static AirCombatCityPcgLab generator;
    static Trace trace;
    static int frameIndex;
    static bool playing;
    static double nextAdvanceAt;
    static float secondsPerStep = 1.05f;
    static bool showRecalculation = true;
    static bool showChineseLabels = true;
    static bool showNextDirection = true;
    static float transitionProgress = 1f;
    static FieldInfo semanticGizmosField;
    static bool originalSemanticGizmos;
    static bool semanticGizmosCaptured;

    public static event Action Changed;

    public static bool IsPrepared => generator != null && trace != null;
    public static bool IsPreparedFor(AirCombatCityPcgLab source) =>
        IsPrepared && ReferenceEquals(generator, source);
    public static bool IsPlaying => playing;
    public static int FrameIndex => frameIndex;
    public static int LastFrameIndex => trace == null
        ? 0
        : trace.steps.Count + trace.finalStages.Count;
    public static int GeneratedBlockCount => trace == null
        ? 0
        : Mathf.Clamp(frameIndex, 0, trace.steps.Count);
    public static Trace CurrentTrace => trace;
    public static Step CurrentStep => trace != null && frameIndex > 0 &&
                                      frameIndex <= trace.steps.Count
        ? trace.steps[frameIndex - 1]
        : null;
    public static FinalTuningStage CurrentFinalStage =>
        trace != null && frameIndex > trace.steps.Count &&
        frameIndex <= LastFrameIndex
            ? trace.finalStages[frameIndex - trace.steps.Count - 1]
            : null;
    public static float TransitionProgress => transitionProgress;
    public static float SecondsPerStep
    {
        get => secondsPerStep;
        set => secondsPerStep = Mathf.Clamp(value, 0.4f, 4f);
    }
    public static bool ShowRecalculation
    {
        get => showRecalculation;
        set { showRecalculation = value; SceneView.RepaintAll(); }
    }
    public static bool ShowChineseLabels
    {
        get => showChineseLabels;
        set { showChineseLabels = value; SceneView.RepaintAll(); }
    }
    public static bool ShowNextDirection
    {
        get => showNextDirection;
        set { showNextDirection = value; SceneView.RepaintAll(); }
    }

    static CityGreedyGenerationShowcase()
    {
        EditorApplication.update -= Update;
        EditorApplication.update += Update;
        SceneView.duringSceneGui -= DrawScene;
        SceneView.duringSceneGui += DrawScene;
        AssemblyReloadEvents.beforeAssemblyReload -= Clear;
        AssemblyReloadEvents.beforeAssemblyReload += Clear;
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    public static bool Prepare(
        AirCombatCityPcgLab source,
        out string error)
    {
        Clear();
        error = string.Empty;
        if (source == null || source.Plan == null)
        {
            error = "当前预览城市还没有可用规划，请先重建城市。";
            return false;
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            error = "逐格生成演示只在编辑模式的场景视图中运行。";
            return false;
        }

        generator = source;
        try
        {
            SuppressGeneratorSemanticGizmos(source);
            trace = BuildTrace(source.Settings, source.Plan);
            if (trace == null || trace.steps.Count == 0)
            {
                error = "当前城市没有可回放的内部难度区块。";
                Clear();
                return false;
            }
            AppendFinalTuningStages(source, trace);
            CachePresentation(source);
            frameIndex = 0;
            ApplyFrame();
            Changed?.Invoke();
            return true;
        }
        catch (Exception exception)
        {
            error = "准备逐格演示失败：" + exception.Message;
            Debug.LogException(exception);
            Clear();
            return false;
        }
    }

    public static Trace BuildTrace(
        AirCombatCitySettings settings,
        AirCombatCityPlan plan)
    {
        if (settings == null || plan == null)
            return null;

        var timer = System.Diagnostics.Stopwatch.StartNew();
        var result = new Trace
        {
            seed = plan.resolvedSeed,
            requestedDifficulty = AirCombatCityDifficultyPcg.ResolveTarget(
                settings.Difficulty).averageDifficulty,
            targetDifficulty = AirCombatCityDifficultyPcg
                .ResolveEffectiveGenerationTarget(settings.Difficulty),
            forecastBaselineDifficulty = AirCombatCityDifficultyPcg
                .ResolveUngeneratedForecastBaseline(settings.Difficulty)
        };
        var ordered = new List<CombatCityBlockPlan>(36);
        for (int index = 0; index < plan.tacticalBlocks.Count; index++)
        {
            CombatCityBlockPlan block = plan.tacticalBlocks[index];
            if (block != null && !block.excludedFromDifficulty &&
                block.generationOrder >= 0)
            {
                ordered.Add(block);
            }
        }
        ordered.Sort((left, right) =>
            left.generationOrder.CompareTo(right.generationOrder));

        var originalBuildings = new List<AirCombatBuildingLot>(
            plan.buildings);
        var originalGenerated = new bool[plan.tacticalBlocks.Count];
        var lotsByBlock = new Dictionary<CombatCityBlockPlan,
            List<AirCombatBuildingLot>>();
        var fixedLots = new List<AirCombatBuildingLot>(128);
        for (int index = 0; index < plan.tacticalBlocks.Count; index++)
        {
            CombatCityBlockPlan block = plan.tacticalBlocks[index];
            originalGenerated[index] = block != null &&
                                       block.generatedForDifficulty;
            if (block != null)
                block.generatedForDifficulty = block.excludedFromDifficulty;
        }
        for (int index = 0; index < originalBuildings.Count; index++)
        {
            AirCombatBuildingLot lot = originalBuildings[index];
            CombatCityBlockPlan block = ResolveBlock(plan, lot.center);
            // BuildBuildings 真正开始时只有外围封边楼已经存在；
            // Facility、中央补强、恢复庭院等都属于之后的实体后处理，
            // 不能提前放进逐格重算，否则回放会偷看未来几何。
            if (block != null && block.excludedFromDifficulty)
            {
                fixedLots.Add(lot);
                continue;
            }
            if (block == null || !IsDifficultyAdjustable(lot))
                continue;
            if (!lotsByBlock.TryGetValue(block,
                    out List<AirCombatBuildingLot> blockLots))
            {
                blockLots = new List<AirCombatBuildingLot>(16);
                lotsByBlock.Add(block, blockLots);
            }
            blockLots.Add(lot);
        }

        plan.buildings.Clear();
        plan.buildings.AddRange(fixedLots);
        try
        {
            AirCombatCityDifficultyEvaluation previous =
                AirCombatCityDifficultyPcg.Evaluate(settings, plan);
            for (int order = 0; order < ordered.Count; order++)
            {
                CombatCityBlockPlan block = ordered[order];
                if (lotsByBlock.TryGetValue(block,
                        out List<AirCombatBuildingLot> blockLots))
                {
                    plan.buildings.AddRange(blockLots);
                }
                block.generatedForDifficulty = true;
                AirCombatCityDifficultyEvaluation current =
                    AirCombatCityDifficultyPcg.Evaluate(settings, plan);
                var step = new Step
                {
                    block = block,
                    cityDifficultyBefore = PredictWholeCityDifficulty(
                        plan, previous, block,
                        result.forecastBaselineDifficulty),
                    cityDifficultyAfter = PredictWholeCityDifficulty(
                        plan, current, null,
                        result.forecastBaselineDifficulty),
                    generatedDifficultyBefore = previous.IsUsable
                        ? previous.averageDifficulty
                        : 0f,
                    generatedDifficultyAfter = current.IsUsable
                        ? current.averageDifficulty
                        : 0f,
                    selectedCellDifficulty = FindCellDifficulty(
                        current, block.stableId),
                    targetDifficulty = result.targetDifficulty,
                    remainingRequiredBefore =
                        block.greedyRemainingBudgetBefore,
                    remainingRequiredAfter =
                        block.greedyRemainingBudgetAfter,
                    requestedMoreDanger =
                        block.greedyRemainingBudgetBefore >=
                        result.targetDifficulty,
                    directionSatisfied = block.greedyDirectionSatisfied,
                    solverPredictionBefore =
                        block.greedyPredictedCityBefore,
                    solverPredictionAfter =
                        block.greedyPredictedCityAfter,
                    candidatesTested = Mathf.Max(1,
                        block.localCorrectionCount)
                };
                AppendRecalculatedCells(plan, previous, current, block,
                    step.recalculatedCells);
                result.steps.Add(step);
                previous = current;
            }
        }
        finally
        {
            plan.buildings.Clear();
            plan.buildings.AddRange(originalBuildings);
            for (int index = 0; index < plan.tacticalBlocks.Count; index++)
            {
                CombatCityBlockPlan block = plan.tacticalBlocks[index];
                if (block != null)
                    block.generatedForDifficulty = originalGenerated[index];
            }
        }
        timer.Stop();
        result.analysisMilliseconds = timer.Elapsed.TotalMilliseconds;
        return result;
    }

    static void AppendFinalTuningStages(
        AirCombatCityPcgLab source,
        Trace result)
    {
        if (source == null || result == null || source.Plan == null)
            return;
        AirCombatCityRuntimeGeometrySnapshot snapshot =
            source.RuntimeGeometrySnapshot;
        if (snapshot == null)
            return;

        AirCombatCityRuntimeGeometrySnapshot buildingsOnly =
            CopySnapshot(snapshot,
                Array.Empty<AirCombatRuntimeConnectionGeometry>(),
                Array.Empty<AirCombatRuntimeConnectionGeometry>(),
                Array.Empty<AirCombatRuntimeWindGeometry>());
        AirCombatCityRuntimeGeometrySnapshot withBridges =
            CopySnapshot(snapshot,
                snapshot.skybridges,
                Array.Empty<AirCombatRuntimeConnectionGeometry>(),
                Array.Empty<AirCombatRuntimeWindGeometry>());
        AirCombatCityRuntimeGeometrySnapshot withCables =
            CopySnapshot(snapshot,
                snapshot.skybridges,
                snapshot.aerialCables,
                Array.Empty<AirCombatRuntimeWindGeometry>());
        AirCombatCityDifficultyEvaluation body =
            AirCombatCityDifficultyPcg.Evaluate(source.Settings,
                source.Plan, buildingsOnly);
        AirCombatCityDifficultyEvaluation bridge =
            AirCombatCityDifficultyPcg.Evaluate(source.Settings,
                source.Plan, withBridges);
        AirCombatCityDifficultyEvaluation cable =
            AirCombatCityDifficultyPcg.Evaluate(source.Settings,
                source.Plan, withCables);
        AirCombatCityDifficultyEvaluation wind =
            AirCombatCityDifficultyPcg.Evaluate(source.Settings,
                source.Plan, snapshot);

        bool boss = source.Settings.mission ==
                    AirCombatCityMission.BossEncounter;
        result.finalStages.Add(new FinalTuningStage
        {
            title = "末段一：连廊三维复核",
            purpose = "同时复核遮挡枪线与机体通行；楼间缝、桥下低空和桥上方只要有一条包线可过，就不会被误判为封路。",
            decision = boss
                ? "Boss 战术连廊与破坏连廊是强制语义，保留后再评估普通连廊。共 " +
                  (snapshot.skybridges?.Length ?? 0) + " 座。"
                : "普通关只把连廊当可验证的遮枪／通行修正，共 " +
                  (snapshot.skybridges?.Length ?? 0) + " 座。",
            difficultyBefore = body.averageDifficulty,
            difficultyAfter = bridge.averageDifficulty,
            acceptedCount = snapshot.skybridges?.Length ?? 0
        });
        result.finalStages.Add(new FinalTuningStage
        {
            title = "末段二：电线减速校准",
            purpose = "电线是触发减速，不是实体墙：不负责挡枪或封路，只按穿越接触造成的速度损失增加暴露时间。",
            decision = "在连廊之后加入 " +
                       (snapshot.aerialCables?.Length ?? 0) +
                       " 组电线，并重新求解全城最低火力路径。",
            difficultyBefore = bridge.averageDifficulty,
            difficultyAfter = cable.averageDifficulty,
            acceptedCount = snapshot.aerialCables?.Length ?? 0
        });
        float windStrength = snapshot.winds != null &&
                             snapshot.winds.Length > 0
            ? snapshot.winds[0].strength
            : 0f;
        result.finalStages.Add(new FinalTuningStage
        {
            title = "末段三：风场方向与强度校准",
            purpose = "同一风场不预设顺逆风；系统按每条有向边穿过风场的真实长度比例、飞行方向、有效占空比和强度修正耗时。",
            decision = "PCG 不假定玩家路线；它对每条相邻格候选边的正反方向分别求值，再由最低火力路径选择理论可执行路线。加入 " +
                       (snapshot.winds?.Length ?? 0) +
                       " 个风场，校准后强度倍率 " +
                       windStrength.ToString("0.00") +
                       "。风场只做有限微调，不替代主体区块难度。",
            difficultyBefore = cable.averageDifficulty,
            difficultyAfter = wind.averageDifficulty,
            acceptedCount = snapshot.winds?.Length ?? 0
        });
    }

    static AirCombatCityRuntimeGeometrySnapshot CopySnapshot(
        AirCombatCityRuntimeGeometrySnapshot source,
        AirCombatRuntimeConnectionGeometry[] skybridges,
        AirCombatRuntimeConnectionGeometry[] cables,
        AirCombatRuntimeWindGeometry[] winds)
    {
        return new AirCombatCityRuntimeGeometrySnapshot
        {
            requestedSeed = source.requestedSeed,
            resolvedSeed = source.resolvedSeed,
            plannedBuildingCount = source.plannedBuildingCount,
            instantiatedBuildingCount = source.instantiatedBuildingCount,
            buildings = source.buildings ??
                        Array.Empty<AirCombatRuntimeBuildingGeometry>(),
            skybridges = skybridges ??
                         Array.Empty<AirCombatRuntimeConnectionGeometry>(),
            aerialCables = cables ??
                           Array.Empty<AirCombatRuntimeConnectionGeometry>(),
            winds = winds ?? Array.Empty<AirCombatRuntimeWindGeometry>(),
            routes = source.routes ??
                     Array.Empty<AirCombatRuntimeRouteGeometry>(),
            ingresses = source.ingresses ??
                        Array.Empty<AirCombatRuntimeIngressGeometry>()
        };
    }

    public static void SetFrame(int value)
    {
        if (!IsPrepared)
            return;
        playing = false;
        frameIndex = Mathf.Clamp(value, 0, LastFrameIndex);
        transitionProgress = 1f;
        ApplyFrame();
        Changed?.Invoke();
    }

    public static void Previous() => SetFrame(frameIndex - 1);
    public static void Next() => SetFrame(frameIndex + 1);
    public static void Reset() => SetFrame(0);
    public static void Finish() => SetFrame(LastFrameIndex);

    public static void SetPlaying(bool value)
    {
        if (!IsPrepared || EditorApplication.isPlayingOrWillChangePlaymode)
            value = false;
        if (value && frameIndex >= LastFrameIndex)
            frameIndex = 0;
        playing = value;
        transitionProgress = value ? 0f : 1f;
        nextAdvanceAt = EditorApplication.timeSinceStartup + secondsPerStep;
        ApplyFrame();
        Changed?.Invoke();
    }

    public static void Focus()
    {
        if (generator == null || SceneView.lastActiveSceneView == null)
            return;
        SceneView view = SceneView.lastActiveSceneView;
        view.pivot = generator.transform.TransformPoint(
            new Vector3(0f, 115f, 0f));
        view.size = generator.Settings.mapSize * 0.70f;
        view.rotation = Quaternion.Euler(48f, 38f, 0f);
        view.Repaint();
    }

    public static void Clear()
    {
        playing = false;
        RestorePresentation();
        RestoreGeneratorSemanticGizmos();
        generator = null;
        trace = null;
        frameIndex = 0;
        SceneView.RepaintAll();
        Changed?.Invoke();
    }

    static void Update()
    {
        if (!playing || !IsPrepared)
            return;
        double remaining = nextAdvanceAt - EditorApplication.timeSinceStartup;
        transitionProgress = Mathf.Clamp01(
            1f - (float)(remaining / Mathf.Max(0.01f, secondsPerStep)));
        if (transitionProgress < 1f)
            ApplyFrame();
        if (EditorApplication.timeSinceStartup < nextAdvanceAt)
            return;
        if (frameIndex >= LastFrameIndex)
        {
            SetPlaying(false);
            return;
        }
        frameIndex++;
        transitionProgress = 0f;
        nextAdvanceAt = EditorApplication.timeSinceStartup + secondsPerStep;
        ApplyFrame();
        Changed?.Invoke();
    }

    static void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode)
            Clear();
    }

    static void CachePresentation(AirCombatCityPcgLab source)
    {
        Buildings.Clear();
        SkybridgeRenderers.Clear();
        SkybridgeColliders.Clear();
        CableRenderers.Clear();
        CableColliders.Clear();
        WindRenderers.Clear();
        WindColliders.Clear();
        DecorationRenderers.Clear();
        DecorationColliders.Clear();
        AuxiliaryRenderers.Clear();
        AuxiliaryColliders.Clear();
        AirCombatCityPlan plan = source.Plan;
        UrbanDestructibleBuilding[] destructibles =
            source.GetComponentsInChildren<UrbanDestructibleBuilding>(true);
        for (int index = 0; index < destructibles.Length; index++)
        {
            UrbanDestructibleBuilding destructible = destructibles[index];
            if (destructible == null)
                continue;
            Vector3 localCenter = ResolveLocalCenter(source.transform,
                destructible.gameObject);
            CombatCityBlockPlan block = ResolvePresentationBlock(
                plan, localCenter);
            int order = ResolveGenerationOrder(block);

            var presentation = new BuildingPresentation
            {
                generationOrder = order,
                revealThreshold = ResolveRevealThreshold(localCenter)
            };
            Renderer[] renderers = destructible.GetComponentsInChildren<
                Renderer>(true);
            for (int rendererIndex = 0;
                 rendererIndex < renderers.Length;
                 rendererIndex++)
            {
                presentation.renderers.Add(new ComponentState<Renderer>
                {
                    component = renderers[rendererIndex],
                    originalEnabled = renderers[rendererIndex].enabled
                });
            }
            Collider[] colliders = destructible.GetComponentsInChildren<
                Collider>(true);
            for (int colliderIndex = 0;
                 colliderIndex < colliders.Length;
                 colliderIndex++)
            {
                presentation.colliders.Add(new ComponentState<Collider>
                {
                    component = colliders[colliderIndex],
                    originalEnabled = colliders[colliderIndex].enabled
                });
            }
            Buildings.Add(presentation);
        }

        Transform generated = source.transform.Find(
            "Generated_AirCombatCity_空战语义先行");
        CacheSubtree(generated,
            "01_空战语义层_先定可玩空域",
            AuxiliaryRenderers, AuxiliaryColliders);
        CacheSubtree(generated,
            "02_三层飞行航路_低60_中110_高160",
            AuxiliaryRenderers, AuxiliaryColliders);
        CacheSubtree(generated,
            "04D_环境陷阱预览_仅视觉无物理",
            WindRenderers, WindColliders);
        CacheSubtree(generated,
            "05_任务层",
            AuxiliaryRenderers, AuxiliaryColliders);
        CacheSubtree(generated,
            "06_验证层_转弯半径与高度包线",
            AuxiliaryRenderers, AuxiliaryColliders);
        Transform connections = generated != null
            ? generated.Find("04A_Skybridges_AuditedSockets_MultiRoute")
            : null;
        CacheConnectionChannels(source.transform, connections);
        Transform decorations = generated != null
            ? generated.Find(
                "04B_城市装饰层_楼顶设备_广告牌_路灯_花坛")
            : null;
        CacheOrderedSubtree(source.transform, plan, decorations,
            DecorationRenderers, DecorationColliders);
    }

    static void ApplyFrame()
    {
        if (!IsPrepared)
            return;
        int visibleOrder = frameIndex - 1;
        for (int index = 0; index < Buildings.Count; index++)
        {
            BuildingPresentation building = Buildings[index];
            bool visible = building.generationOrder < 0 ||
                           building.generationOrder < visibleOrder ||
                           (building.generationOrder == visibleOrder &&
                            transitionProgress >=
                            building.revealThreshold);
            SetStates(building.renderers, visible);
            SetStates(building.colliders, visible);
        }
        SetOrderedStates(DecorationRenderers, visibleOrder);
        SetOrderedStates(DecorationColliders, visibleOrder);
        int finalStage = Mathf.Max(0, frameIndex - trace.steps.Count);
        SetTransitionStates(SkybridgeRenderers, finalStage >= 1);
        SetTransitionStates(SkybridgeColliders, finalStage >= 1);
        SetTransitionStates(CableRenderers, finalStage >= 2);
        SetTransitionStates(CableColliders, finalStage >= 2);
        SetTransitionStates(WindRenderers, finalStage >= 3);
        SetTransitionStates(WindColliders, finalStage >= 3);
        SetStates(AuxiliaryRenderers, false);
        SetStates(AuxiliaryColliders, false);
        SceneView.RepaintAll();
    }

    static void RestorePresentation()
    {
        RestoreStates(Buildings);
        RestoreStates(SkybridgeRenderers);
        RestoreStates(SkybridgeColliders);
        RestoreStates(CableRenderers);
        RestoreStates(CableColliders);
        RestoreStates(WindRenderers);
        RestoreStates(WindColliders);
        RestoreStates(DecorationRenderers);
        RestoreStates(DecorationColliders);
        RestoreStates(AuxiliaryRenderers);
        RestoreStates(AuxiliaryColliders);
        Buildings.Clear();
        SkybridgeRenderers.Clear();
        SkybridgeColliders.Clear();
        CableRenderers.Clear();
        CableColliders.Clear();
        WindRenderers.Clear();
        WindColliders.Clear();
        DecorationRenderers.Clear();
        DecorationColliders.Clear();
        AuxiliaryRenderers.Clear();
        AuxiliaryColliders.Clear();
    }

    static void RestoreStates(List<BuildingPresentation> buildings)
    {
        for (int index = 0; index < buildings.Count; index++)
        {
            RestoreStates(buildings[index].renderers);
            RestoreStates(buildings[index].colliders);
        }
    }

    static void SetStates<T>(List<ComponentState<T>> states, bool visible)
        where T : Component
    {
        for (int index = 0; index < states.Count; index++)
        {
            ComponentState<T> state = states[index];
            SetComponentEnabled(state.component,
                visible && state.originalEnabled);
        }
    }

    static void SetOrderedStates<T>(
        List<ComponentState<T>> states,
        int visibleOrder)
        where T : Component
    {
        for (int index = 0; index < states.Count; index++)
        {
            ComponentState<T> state = states[index];
            bool visible = state.generationOrder < 0 ||
                           state.generationOrder < visibleOrder ||
                           (state.generationOrder == visibleOrder &&
                            transitionProgress >= state.revealThreshold);
            SetComponentEnabled(state.component,
                visible && state.originalEnabled);
        }
    }

    static void SetTransitionStates<T>(
        List<ComponentState<T>> states,
        bool stageReached)
        where T : Component
    {
        for (int index = 0; index < states.Count; index++)
        {
            ComponentState<T> state = states[index];
            bool visible = stageReached &&
                           (!playing || transitionProgress >=
                            state.revealThreshold);
            SetComponentEnabled(state.component,
                visible && state.originalEnabled);
        }
    }

    static void RestoreStates<T>(List<ComponentState<T>> states)
        where T : Component
    {
        for (int index = 0; index < states.Count; index++)
        {
            ComponentState<T> state = states[index];
            SetComponentEnabled(state.component, state.originalEnabled);
        }
    }

    static void SetComponentEnabled(Component component, bool enabled)
    {
        Renderer renderer = component as Renderer;
        if (renderer != null)
        {
            renderer.enabled = enabled;
            return;
        }
        Collider collider = component as Collider;
        if (collider != null)
            collider.enabled = enabled;
    }

    static void CacheSubtree(
        Transform generated,
        string childName,
        List<ComponentState<Renderer>> renderers,
        List<ComponentState<Collider>> colliders)
    {
        Transform child = generated != null ? generated.Find(childName) : null;
        if (child == null)
            return;
        Renderer[] foundRenderers = child.GetComponentsInChildren<Renderer>(
            true);
        for (int index = 0; index < foundRenderers.Length; index++)
        {
            renderers.Add(new ComponentState<Renderer>
            {
                component = foundRenderers[index],
                originalEnabled = foundRenderers[index].enabled
            });
        }
        Collider[] foundColliders = child.GetComponentsInChildren<Collider>(
            true);
        for (int index = 0; index < foundColliders.Length; index++)
        {
            colliders.Add(new ComponentState<Collider>
            {
                component = foundColliders[index],
                originalEnabled = foundColliders[index].enabled
            });
        }
    }

    static void CacheOrderedSubtree(
        Transform cityRoot,
        AirCombatCityPlan plan,
        Transform subtree,
        List<ComponentState<Renderer>> renderers,
        List<ComponentState<Collider>> colliders)
    {
        if (cityRoot == null || plan == null || subtree == null)
            return;
        Renderer[] foundRenderers = subtree.GetComponentsInChildren<Renderer>(
            true);
        for (int index = 0; index < foundRenderers.Length; index++)
        {
            Renderer renderer = foundRenderers[index];
            Vector3 localCenter = ResolveLocalCenter(cityRoot,
                renderer.gameObject);
            renderers.Add(new ComponentState<Renderer>
            {
                component = renderer,
                originalEnabled = renderer.enabled,
                generationOrder = ResolveGenerationOrder(
                    ResolvePresentationBlock(plan,
                        localCenter)),
                revealThreshold = ResolveRevealThreshold(localCenter)
            });
        }
        Collider[] foundColliders = subtree.GetComponentsInChildren<Collider>(
            true);
        for (int index = 0; index < foundColliders.Length; index++)
        {
            Collider collider = foundColliders[index];
            Vector3 localCenter = cityRoot.InverseTransformPoint(
                collider.bounds.center);
            colliders.Add(new ComponentState<Collider>
            {
                component = collider,
                originalEnabled = collider.enabled,
                generationOrder = ResolveGenerationOrder(
                    ResolvePresentationBlock(plan,
                        localCenter)),
                revealThreshold = ResolveRevealThreshold(localCenter)
            });
        }
    }

    static void CacheConnectionChannels(
        Transform cityRoot,
        Transform connectionRoot)
    {
        if (cityRoot == null || connectionRoot == null || trace == null)
            return;
        for (int index = 0; index < connectionRoot.childCount; index++)
        {
            Transform child = connectionRoot.GetChild(index);
            if (child == null)
                continue;
            bool cable = child.name.StartsWith(
                "AerialCableLink_", StringComparison.Ordinal);
            CacheOrderedSubtree(
                cityRoot,
                generator != null ? generator.Plan : null,
                child,
                cable ? CableRenderers : SkybridgeRenderers,
                cable ? CableColliders : SkybridgeColliders);
        }
    }

    static float ResolveRevealThreshold(Vector3 localCenter)
    {
        float deterministic = Mathf.Abs(Mathf.Sin(
            localCenter.x * 0.071f +
            localCenter.z * 0.113f +
            localCenter.y * 0.017f));
        return Mathf.Lerp(0.08f, 0.78f, deterministic);
    }

    static void DrawScene(SceneView sceneView)
    {
        if (!IsPrepared || generator == null ||
            EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }
        Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
        DrawGeneratedBlocks();
        Step step = CurrentStep;
        if (step != null)
        {
            if (showRecalculation)
                DrawRecalculatedCells(step);
            DrawCurrentBlock(step);
            if (showNextDirection)
                DrawNextArrow(step);
        }
        DrawScenePanel(sceneView, step);
    }

    static void DrawGeneratedBlocks()
    {
        int visibleCount = Mathf.Min(frameIndex, trace.steps.Count);
        for (int index = 0; index < visibleCount; index++)
        {
            CombatCityBlockPlan block = trace.steps[index].block;
            DrawBlockOutline(block, new Color(0.30f, 0.82f, 0.52f, 0.38f),
                2f, 2.2f);
        }
    }

    static void DrawCurrentBlock(Step step)
    {
        float reveal = playing
            ? Mathf.SmoothStep(0f, 1f, transitionProgress)
            : 1f;
        Color fill = step.requestedMoreDanger
            ? new Color(1f, 0.25f, 0.08f, 0.18f)
            : new Color(0.08f, 0.78f, 1f, 0.18f);
        fill.a *= Mathf.Lerp(0.25f, 1f, reveal);
        Color outline = step.requestedMoreDanger
            ? new Color(1f, 0.34f, 0.10f, 1f)
            : new Color(0.10f, 0.86f, 1f, 1f);
        DrawBlockFill(step.block, fill, outline, 4.5f);
        if (!showChineseLabels)
            return;
        Vector3 label = generator.transform.TransformPoint(
            step.block.bounds.center + Vector3.up * 18f);
        Handles.Label(label,
            "当前区块 " + step.block.gridX + "，" + step.block.gridZ +
            "\n目标缺口 " + FormatSignedPercent(
                step.targetDifficulty - step.cityDifficultyBefore) +
            "｜本格输入 " +
            (step.selectedCellDifficulty * 100f).ToString("0") + "%" +
            "\n本格加入后全城 " +
            FormatSignedPercent(step.cityDifficultyAfter -
                                step.cityDifficultyBefore) +
            (step.directionSatisfied
                ? "｜向目标收敛"
                : "｜本楼位方案均受硬约束，已取最小反向量"),
            BuildWorldLabelStyle(outline));
    }

    static void DrawRecalculatedCells(Step step)
    {
        int totalVisible = CountVisibleChanges(step);
        int visibleSequence = 0;
        for (int index = 0; index < step.recalculatedCells.Count; index++)
        {
            CellChange change = step.recalculatedCells[index];
            bool significant = Mathf.Abs(change.Delta) >=
                               SignificantRecalculationThreshold;
            float revealAt = significant && totalVisible > 0
                ? Mathf.Lerp(RecalculationScanEnd,
                    RecalculationSequenceEnd,
                    (visibleSequence + 1f) / totalVisible)
                : 0f;
            if (significant)
                visibleSequence++;
            bool revealed = !playing || transitionProgress >= revealAt;
            if (!significant || !revealed)
            {
                float scanAlpha = !playing
                    ? 0.035f
                    : transitionProgress < RecalculationScanEnd
                        ? Mathf.Lerp(0.04f, 0.13f,
                            transitionProgress /
                            RecalculationScanEnd)
                        : Mathf.Lerp(0.09f, 0.025f,
                            Mathf.InverseLerp(
                                RecalculationScanEnd,
                                RecalculationSummaryStart,
                                transitionProgress));
                DrawBlockFill(change.block,
                    new Color(0.78f, 0.86f, 0.92f, scanAlpha * 0.35f),
                    new Color(0.72f, 0.82f, 0.90f, scanAlpha),
                    1f);
                continue;
            }
            Color color = change.Delta > 0f
                ? new Color(1f, 0.14f, 0.06f, 0.12f)
                : new Color(0.08f, 0.55f, 1f, 0.12f);
            float pulse = playing
                ? transitionProgress >= RecalculationSummaryStart
                    ? 1f
                    : 0.70f + 0.30f * Mathf.Sin(
                        (transitionProgress - revealAt) * 18f)
                : 1f;
            color.a *= pulse;
            DrawBlockFill(change.block, color,
                new Color(color.r, color.g, color.b, 0.72f), 1.5f);
            if (!showChineseLabels)
                continue;
            Vector3 label = generator.transform.TransformPoint(
                change.block.bounds.center + Vector3.up * 8f);
            Handles.Label(label,
                "旧格重算 " +
                (change.before * 100f).ToString("0.0") + "% → " +
                (change.after * 100f).ToString("0.0") + "%\n受新格 " +
                step.block.gridX + "，" + step.block.gridZ + " 影响 " +
                FormatSignedPercent(change.Delta),
                BuildWorldLabelStyle(color));
        }
    }

    static void DrawNextArrow(Step current)
    {
        int nextIndex = frameIndex;
        if (nextIndex < 0 || nextIndex >= trace.steps.Count)
            return;
        CombatCityBlockPlan next = trace.steps[nextIndex].block;
        Vector3 start = generator.transform.TransformPoint(
            current.block.bounds.center + Vector3.up * 34f);
        Vector3 end = generator.transform.TransformPoint(
            next.bounds.center + Vector3.up * 34f);
        Vector3 direction = end - start;
        if (direction.sqrMagnitude < 0.01f)
            return;
        Color strategyColor = current.requestedMoreDanger
            ? new Color(1f, 0.25f, 0.08f, 1f)
            : new Color(0.08f, 0.78f, 1f, 1f);
        Handles.color = strategyColor;
        const int segmentCount = 16;
        var curve = new Vector3[segmentCount + 1];
        Vector3 lift = Vector3.up * Mathf.Min(95f,
            direction.magnitude * 0.24f);
        for (int index = 0; index <= segmentCount; index++)
        {
            float t = index / (float)segmentCount;
            curve[index] = Vector3.Lerp(start, end, t) +
                           lift * (4f * t * (1f - t));
        }
        Handles.DrawAAPolyLine(3.5f, curve);
        for (int index = 3; index < segmentCount; index += 4)
        {
            Handles.SphereHandleCap(0, curve[index], Quaternion.identity,
                3.5f, EventType.Repaint);
        }
        Handles.SphereHandleCap(0, start, Quaternion.identity, 5f,
            EventType.Repaint);
        Handles.ConeHandleCap(0, end,
            Quaternion.LookRotation((end - curve[segmentCount - 1])
                .normalized),
            Mathf.Clamp(direction.magnitude * 0.025f, 4f, 8f),
            EventType.Repaint);
        if (showChineseLabels)
        {
            Handles.Label(Vector3.Lerp(start, end, 0.5f) + Vector3.up * 6f,
                "全城控制预测 " +
                (current.cityDifficultyAfter * 100f).ToString("0.0") +
                "%｜权威目标 " +
                (current.targetDifficulty * 100f).ToString("0.0") + "%\n" +
                "剩余缺口 " + FormatSignedPercent(
                    current.targetDifficulty -
                    current.cityDifficultyAfter) + "｜下一格应补" +
                (current.cityDifficultyAfter < current.targetDifficulty
                    ? "更危险楼位方案"
                    : "更安全楼位方案"),
                BuildWorldLabelStyle(Handles.color));
        }
    }

    static void DrawScenePanel(SceneView sceneView, Step step)
    {
        Handles.BeginGUI();
        float panelX = sceneView.position.width >= 720f ? 64f : 16f;
        float width = Mathf.Min(520f, Mathf.Max(300f,
            sceneView.position.width - panelX - 16f));
        FinalTuningStage finalStage = CurrentFinalStage;
        float panelHeight = frameIndex == 0
            ? 154f
            : finalStage != null
                ? 210f
            : playing && step != null && showRecalculation &&
              step.recalculatedCells.Count > 0
                ? 268f
                : 224f;
        Rect panelRect = new Rect(panelX, 16f, width, panelHeight);
        EditorGUI.DrawRect(panelRect,
            new Color(0.035f, 0.050f, 0.075f, 0.94f));
        GUILayout.BeginArea(panelRect,
            GUIContent.none, EditorStyles.helpBox);
        GUILayout.Label("城市逐格贪心生成过程", EditorStyles.boldLabel);
        if (frameIndex == 0)
        {
            GUILayout.Label("阶段：道路网格与街区合并已经先完成");
            GUILayout.Label("权威目标　" +
                            (trace.requestedDifficulty * 100f)
                            .ToString("0.0") + "%　未生成格保守基线　" +
                            (trace.forecastBaselineDifficulty * 100f)
                            .ToString("0.0") + "%");
            GUILayout.Label("下一步：从中心区块开始，按四邻接向外扩张");
            GUILayout.Label("外围最高楼已参与枪线计算，但不计入难度平均");
        }
        else if (step != null)
        {
            GUILayout.Label("步骤 " + frameIndex + " / " +
                            trace.steps.Count + "　区块 " +
                            step.block.gridX + "，" + step.block.gridZ);
            GUILayout.Label("全城控制预测　" +
                            (step.cityDifficultyBefore * 100f).ToString("0.0") +
                            "% → " +
                            (step.cityDifficultyAfter * 100f).ToString("0.0") +
                            "%　权威目标 " +
                            (step.targetDifficulty * 100f).ToString("0.0") +
                            "%");
            GUILayout.Label("本步误差　" +
                            FormatSignedPercent(step.cityDifficultyBefore -
                                                step.targetDifficulty) +
                            " → " +
                            FormatSignedPercent(step.cityDifficultyAfter -
                                                step.targetDifficulty) +
                            "（负数代表仍偏低，正数代表偏高）");
            GUILayout.Label("已生成区域实测　" +
                            (step.generatedDifficultyBefore * 100f)
                            .ToString("0.0") + "% → " +
                            (step.generatedDifficultyAfter * 100f)
                            .ToString("0.0") + "%");
            GUILayout.Label("剩余区块所需均值　" +
                            (step.remainingRequiredBefore * 100f).ToString("0.0") +
                            "% → " +
                            (step.remainingRequiredAfter * 100f).ToString("0.0") +
                            "%");
            int changedCount = CountVisibleChanges(step);
            GUILayout.Label("下一格策略：" +
                            (step.requestedMoreDanger ? "逐楼位向更危险方向逼近" :
                                "逐楼位向更安全方向偿还") +
                            "　楼位方案试算 " + step.candidatesTested +
                            " 次　参与重算 " +
                            step.recalculatedCells.Count +
                            " 格　显著变化 " + changedCount + " 格");
            GUILayout.Label(step.directionSatisfied
                ? "逐楼位结果：已按目标方向收敛"
                : "逐楼位结果：全部合法方案都会反向变化；已选变化最小者，等待后续格与终局回修偿还");
            if (playing && showRecalculation &&
                step.recalculatedCells.Count > 0)
            {
                GUILayout.Label("重算阶段　" +
                                ResolveRecalculationStageLabel() +
                                "　显著结果 " +
                                CountRevealedChanges(step) + " / " +
                                changedCount +
                                "（灰框已参与，蓝色变安全，红色变危险）");
            }
        }
        else if (finalStage != null)
        {
            int stageIndex = frameIndex - trace.steps.Count;
            GUILayout.Label("主体区块完成　末段校准 " + stageIndex + " / " +
                            trace.finalStages.Count);
            GUILayout.Label(finalStage.title, EditorStyles.boldLabel);
            GUILayout.Label(finalStage.purpose,
                EditorStyles.wordWrappedLabel);
            GUILayout.Label("全城难度　" +
                            (finalStage.difficultyBefore * 100f)
                            .ToString("0.0") + "% → " +
                            (finalStage.difficultyAfter * 100f)
                            .ToString("0.0") + "%　权威目标 " +
                            (trace.targetDifficulty * 100f)
                            .ToString("0.0") + "%");
            GUILayout.Label(finalStage.decision,
                EditorStyles.wordWrappedLabel);
        }
        GUILayout.EndArea();
        Handles.EndGUI();
    }

    static int CountVisibleChanges(Step step)
    {
        int count = 0;
        for (int index = 0; index < step.recalculatedCells.Count; index++)
        {
            if (Mathf.Abs(step.recalculatedCells[index].Delta) >=
                SignificantRecalculationThreshold)
                count++;
        }
        return count;
    }

    static int CountRevealedChanges(Step step)
    {
        int total = CountVisibleChanges(step);
        if (!playing || total == 0)
            return total;
        int revealed = 0;
        for (int sequence = 0; sequence < total; sequence++)
        {
            float revealAt = Mathf.Lerp(RecalculationScanEnd,
                RecalculationSequenceEnd,
                (sequence + 1f) / total);
            if (transitionProgress >= revealAt)
                revealed++;
        }
        return revealed;
    }

    static string ResolveRecalculationStageLabel()
    {
        if (!playing)
            return "汇总停留";
        if (transitionProgress < RecalculationScanEnd)
            return "全体旧格重新求解";
        if (transitionProgress < RecalculationSummaryStart)
            return "显著变化逐格呈现";
        return "全部重算结果停留";
    }

    static string FormatSignedPercent(float value)
    {
        return (value >= 0f ? "+" : string.Empty) +
               (value * 100f).ToString("0.0") + "%";
    }

    static void DrawBlockFill(
        CombatCityBlockPlan block,
        Color fill,
        Color outline,
        float lineWidth)
    {
        if (block == null || generator == null)
            return;
        Vector3[] corners = ResolveCorners(block, 1.2f);
        Handles.DrawSolidRectangleWithOutline(corners, fill, outline);
        Handles.color = outline;
        Handles.DrawAAPolyLine(lineWidth,
            corners[0], corners[1], corners[2], corners[3], corners[0]);
    }

    static void DrawBlockOutline(
        CombatCityBlockPlan block,
        Color color,
        float lineWidth,
        float height)
    {
        if (block == null || generator == null)
            return;
        Vector3[] corners = ResolveCorners(block, height);
        Handles.color = color;
        Handles.DrawAAPolyLine(lineWidth,
            corners[0], corners[1], corners[2], corners[3], corners[0]);
    }

    static Vector3[] ResolveCorners(
        CombatCityBlockPlan block,
        float height)
    {
        Bounds bounds = block.bounds;
        float y = bounds.min.y + height;
        return new[]
        {
            generator.transform.TransformPoint(
                new Vector3(bounds.min.x, y, bounds.min.z)),
            generator.transform.TransformPoint(
                new Vector3(bounds.max.x, y, bounds.min.z)),
            generator.transform.TransformPoint(
                new Vector3(bounds.max.x, y, bounds.max.z)),
            generator.transform.TransformPoint(
                new Vector3(bounds.min.x, y, bounds.max.z))
        };
    }

    static GUIStyle BuildWorldLabelStyle(Color color)
    {
        var style = new GUIStyle(EditorStyles.helpBox)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white }
        };
        style.normal.background = Texture2D.grayTexture;
        return style;
    }

    static void AppendRecalculatedCells(
        AirCombatCityPlan plan,
        AirCombatCityDifficultyEvaluation before,
        AirCombatCityDifficultyEvaluation after,
        CombatCityBlockPlan current,
        List<CellChange> destination)
    {
        if (before == null || after == null)
            return;
        int count = Mathf.Min(plan.tacticalBlocks.Count,
            Mathf.Min(before.cells.Count, after.cells.Count));
        for (int index = 0; index < count; index++)
        {
            CombatCityBlockPlan block = plan.tacticalBlocks[index];
            if (block == null || ReferenceEquals(block, current) ||
                block.excludedFromDifficulty ||
                !before.cells[index].generated ||
                !after.cells[index].generated ||
                !before.cells[index].flyable ||
                !after.cells[index].flyable)
            {
                continue;
            }
            destination.Add(new CellChange
            {
                block = block,
                before = before.cells[index].survivalDifficulty,
                after = after.cells[index].survivalDifficulty
            });
        }
    }

    static float FindCellDifficulty(
        AirCombatCityDifficultyEvaluation evaluation,
        string stableId)
    {
        if (evaluation == null)
            return 0f;
        for (int index = 0; index < evaluation.cells.Count; index++)
        {
            AirCombatCityDifficultyCell cell = evaluation.cells[index];
            if (cell != null && cell.stableId == stableId)
                return cell.survivalDifficulty;
        }
        return 0f;
    }

    static float PredictWholeCityDifficulty(
        AirCombatCityPlan plan,
        AirCombatCityDifficultyEvaluation evaluation,
        CombatCityBlockPlan temporarilyUngenerated,
        float effectiveTarget)
    {
        float total = 0f;
        int count = 0;
        for (int index = 0; index < plan.tacticalBlocks.Count; index++)
        {
            CombatCityBlockPlan block = plan.tacticalBlocks[index];
            if (block == null || block.excludedFromDifficulty)
                continue;
            count++;
            bool generated = block.generatedForDifficulty &&
                             !ReferenceEquals(block,
                                 temporarilyUngenerated);
            if (generated && evaluation != null &&
                index < evaluation.cells.Count &&
                evaluation.cells[index].flyable)
            {
                total += evaluation.cells[index].survivalDifficulty;
            }
            else
            {
                // 未生成格不能再用角色软目标充当“已经实现的结果”。
                // 这会让20%输入在第19步虚假显示命中，随后每加一格又
                // 连续回升。这里改用本次可实现的全城控制目标作为中性
                // 预测；实际候选提交后仍会重算所有已生成格。
                total += effectiveTarget;
            }
        }
        return count > 0 ? total / count : 0f;
    }

    static AirCombatBuildingLot FindLot(
        AirCombatCityPlan plan,
        string stableId)
    {
        if (plan == null || string.IsNullOrEmpty(stableId))
            return null;
        for (int index = 0; index < plan.buildings.Count; index++)
        {
            AirCombatBuildingLot lot = plan.buildings[index];
            if (lot != null && lot.stableId == stableId)
                return lot;
        }
        return null;
    }

    static CombatCityBlockPlan ResolveBlock(
        AirCombatCityPlan plan,
        Vector3 localPosition)
    {
        if (plan == null)
            return null;
        CombatCityBlockPlan nearest = null;
        float nearestDistance = float.PositiveInfinity;
        for (int index = 0; index < plan.tacticalBlocks.Count; index++)
        {
            CombatCityBlockPlan block = plan.tacticalBlocks[index];
            if (block == null)
                continue;
            Vector3 sample = new Vector3(localPosition.x,
                block.bounds.center.y, localPosition.z);
            if (block.bounds.Contains(sample))
                return block;
            Vector2 delta = new Vector2(
                localPosition.x - block.bounds.center.x,
                localPosition.z - block.bounds.center.z);
            if (delta.sqrMagnitude < nearestDistance)
            {
                nearestDistance = delta.sqrMagnitude;
                nearest = block;
            }
        }
        return nearestDistance <= 4f ? nearest : null;
    }

    static CombatCityBlockPlan ResolvePresentationBlock(
        AirCombatCityPlan plan,
        Vector3 localPosition)
    {
        CombatCityBlockPlan contained = ResolveBlock(plan, localPosition);
        if (contained != null)
            return contained;
        if (plan == null)
            return null;

        CombatCityBlockPlan nearestInner = null;
        CombatCityBlockPlan nearestAny = null;
        float nearestInnerDistance = float.PositiveInfinity;
        float nearestAnyDistance = float.PositiveInfinity;
        for (int index = 0; index < plan.tacticalBlocks.Count; index++)
        {
            CombatCityBlockPlan block = plan.tacticalBlocks[index];
            if (block == null)
                continue;
            Vector2 delta = new Vector2(
                localPosition.x - block.bounds.center.x,
                localPosition.z - block.bounds.center.z);
            float distance = delta.sqrMagnitude;
            if (distance < nearestAnyDistance)
            {
                nearestAnyDistance = distance;
                nearestAny = block;
            }
            if (!block.excludedFromDifficulty &&
                block.generationOrder >= 0 &&
                distance < nearestInnerDistance)
            {
                nearestInnerDistance = distance;
                nearestInner = block;
            }
        }
        return nearestInner ?? nearestAny;
    }

    static int ResolveGenerationOrder(CombatCityBlockPlan block)
    {
        if (block != null && block.excludedFromDifficulty)
            return -1;
        if (block != null && block.generationOrder >= 0)
            return block.generationOrder;
        if (trace != null && trace.steps.Count > 0)
            return trace.steps[trace.steps.Count - 1].block.generationOrder;
        return 0;
    }

    static bool IsDifficultyAdjustable(AirCombatBuildingLot lot)
    {
        if (lot == null || string.IsNullOrEmpty(lot.stableId))
            return false;
        return lot.stableId.StartsWith("building.large.",
                   StringComparison.Ordinal) ||
               lot.stableId.StartsWith("building.standard.",
                   StringComparison.Ordinal) ||
               lot.stableId.StartsWith("building.small-gapfill.",
                   StringComparison.Ordinal);
    }

    static Vector3 ResolveLocalCenter(Transform root, GameObject target)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return root.InverseTransformPoint(target.transform.position);
        Bounds bounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
            bounds.Encapsulate(renderers[index].bounds);
        return root.InverseTransformPoint(bounds.center);
    }

    static void SuppressGeneratorSemanticGizmos(AirCombatCityPcgLab source)
    {
        semanticGizmosField = typeof(AirCombatCityPcgLab).GetField(
            "showSemanticGizmos",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (semanticGizmosField == null || source == null)
            return;
        originalSemanticGizmos = (bool)semanticGizmosField.GetValue(source);
        semanticGizmosCaptured = true;
        semanticGizmosField.SetValue(source, false);
    }

    static void RestoreGeneratorSemanticGizmos()
    {
        if (!semanticGizmosCaptured || semanticGizmosField == null ||
            generator == null)
        {
            semanticGizmosCaptured = false;
            return;
        }
        semanticGizmosField.SetValue(generator, originalSemanticGizmos);
        semanticGizmosCaptured = false;
    }
}
