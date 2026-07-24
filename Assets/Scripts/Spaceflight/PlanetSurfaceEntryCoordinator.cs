using System.Collections;
using UnityEngine;

public enum PlanetSurfaceLoadStage
{
    Preparing,
    LandingTerrain,
    ActiveTerrain,
    Water,
    LandingPlatform,
    Spacecraft,
    Player,
    ScenePlacement,
    FinalValidation,
    Ready,
    Failed
}

[DefaultExecutionOrder(-9000)]
[DisallowMultipleComponent]
public sealed class PlanetSurfaceEntryCoordinator : MonoBehaviour
{
    VoxelQuadSphereWorld world;
    PlanetLoadingUI loadingUI;
    SurfaceLandedSpacecraftRestorer restorer;
    VoxelPlanetPlayerController player;
    Coroutine monitorRoutine;
    bool requiresRestorer;
    bool buildingsRestored;
    float reportedProgress;

    public bool IsReadyForReveal { get; private set; }
    public bool HasFailed { get; private set; }
    public bool IsInitialized { get; private set; }
    public string FailureReason { get; private set; }
    public PlanetSurfaceLoadStage CurrentStage { get; private set; }

    public void Initialize(
        VoxelQuadSphereWorld targetWorld,
        PlanetLoadingUI targetLoadingUI,
        bool requireRestorer)
    {
        world = targetWorld;
        loadingUI = targetLoadingUI;
        IsInitialized = true;
        requiresRestorer = requireRestorer;
        buildingsRestored = false;
        reportedProgress = 0f;
        IsReadyForReveal = false;
        HasFailed = false;
        FailureReason = string.Empty;

        if (Time.timeScale <= 0.001f)
            Time.timeScale = 1f;

        if (loadingUI == null)
            loadingUI = FindObjectOfType<PlanetLoadingUI>(true);
        loadingUI?.Show("正在读取星球、存档和着陆数据");
        SetPlayerInputLocked(true);

        if (monitorRoutine != null)
            StopCoroutine(monitorRoutine);
        monitorRoutine = StartCoroutine(MonitorSurfaceEntry());
    }

    public void BindRestorer(SurfaceLandedSpacecraftRestorer targetRestorer)
    {
        restorer = targetRestorer;
    }

    public void MarkBuildingsRestored()
    {
        buildingsRestored = true;
    }

    public void ReportStage(
        PlanetSurfaceLoadStage stage,
        float progress,
        string status)
    {
        if (HasFailed || IsReadyForReveal)
            return;

        float clamped = Mathf.Clamp01(progress);
        if (clamped + 0.0001f < reportedProgress)
            return;

        reportedProgress = Mathf.Max(reportedProgress, clamped);
        CurrentStage = stage;
        loadingUI?.SetProgress(reportedProgress, status);
    }

    IEnumerator MonitorSurfaceEntry()
    {
        ReportStage(
            PlanetSurfaceLoadStage.Preparing,
            0.02f,
            "正在读取星球、存档和着陆数据");

        while (world != null && !world.HasStartedSurfaceGeneration)
        {
            SetPlayerInputLocked(true);
            yield return null;
        }

        ReportStage(
            PlanetSurfaceLoadStage.LandingTerrain,
            0.10f,
            "正在生成着陆区高精度地形和碰撞体");

        while (world != null && !world.IsInitialSurfaceReady)
        {
            SetPlayerInputLocked(true);
            if (world.HasFinishedSurfaceGeneration
                && world.InitialSurfaceGenerationFailed
                && !world.IsInitialSurfaceRecoveryRunning
                && (!requiresRestorer || restorer == null || restorer.HasFailed))
            {
                Fail(
                    "着陆区高精度地形生成失败",
                    $"LoadedChunks={world.PinnedLandingChunkCount}");
                yield break;
            }
            yield return null;
        }
        if (world == null)
            yield break;

        ReportStage(
            PlanetSurfaceLoadStage.LandingTerrain,
            0.45f,
            "着陆区高精度地形已完成");

        while (!world.IsActiveSurfaceRegionReady)
        {
            if (CheckRestoreFailure())
                yield break;
            SetPlayerInputLocked(true);
            yield return null;
        }
        ReportStage(
            PlanetSurfaceLoadStage.ActiveTerrain,
            0.65f,
            "活动半径地形与安全 LOD 已接管");

        while (!world.IsWaterReady)
        {
            if (CheckRestoreFailure())
                yield break;
            yield return null;
        }
        ReportStage(
            PlanetSurfaceLoadStage.Water,
            0.72f,
            "海洋、河流与水体交互已完成");

        if (requiresRestorer)
        {
            while (restorer == null)
                yield return null;

            while (!restorer.IsPlatformReady)
            {
                if (CheckRestoreFailure())
                    yield break;
                yield return null;
            }
            ReportStage(
                PlanetSurfaceLoadStage.LandingPlatform,
                0.76f,
                "着陆平台与碰撞体已准备");

            while (!restorer.IsSpacecraftReady)
            {
                if (CheckRestoreFailure())
                    yield break;
                yield return null;
            }
            ReportStage(
                PlanetSurfaceLoadStage.Spacecraft,
                0.82f,
                "飞船蓝图已恢复并完成高度校正");

            while (!restorer.IsPlayerReady)
            {
                if (CheckRestoreFailure())
                    yield break;
                yield return null;
            }
        }
        else
        {
            while (!world.IsInitialPlayerPlaced)
                yield return null;
        }

        ReportStage(
            PlanetSurfaceLoadStage.Player,
            0.88f,
            "玩家、相机与第一人称状态已安置");

        while (!world.IsScenePlacementReady || !buildingsRestored)
        {
            if (CheckRestoreFailure())
                yield break;
            yield return null;
        }
        ReportStage(
            PlanetSurfaceLoadStage.ScenePlacement,
            0.98f,
            "建筑、植被、岩石、水晶和资源已生成");

        if (requiresRestorer && !restorer.IsRestoreComplete)
        {
            while (!restorer.IsRestoreComplete)
            {
                if (CheckRestoreFailure())
                    yield break;
                yield return null;
            }
        }

        ReportStage(
            PlanetSurfaceLoadStage.FinalValidation,
            0.99f,
            "正在同步物理并检查出生点、飞船和相机");
        SetPlayerInputLocked(true);

        Physics.SyncTransforms();
        // Surface entry can begin while another UI has left timeScale at zero.
        // WaitForFixedUpdate would never resume in that state and would strand
        // the loading screen at 99%, so stabilize over two unscaled frames.
        yield return null;
        Physics.SyncTransforms();
        yield return null;
        Physics.SyncTransforms();

        player = FindObjectOfType<VoxelPlanetPlayerController>();
        if (player == null)
        {
            Fail("玩家安置校验失败", "未找到 VoxelPlanetPlayerController");
            yield break;
        }

        IsReadyForReveal = true;
        CurrentStage = PlanetSurfaceLoadStage.Ready;
        if (loadingUI != null)
            yield return loadingUI.CompleteAndFade(0.6f);

        player.SetSurfacePhysicsReady(true);
        player.SetGameplayInputBlocked(false);
        SurfaceMultifunctionController multifunction =
            player.GetComponent<SurfaceMultifunctionController>();
        multifunction?.SetInputBlocked(false);
        monitorRoutine = null;
    }

    bool CheckRestoreFailure()
    {
        if (restorer == null || !restorer.HasFailed)
            return false;

        Fail(
            "飞船、平台或玩家恢复失败",
            restorer.FailureReason);
        return true;
    }

    void SetPlayerInputLocked(bool locked)
    {
        if (player == null)
            player = FindObjectOfType<VoxelPlanetPlayerController>();
        if (player == null)
            return;

        player.SetGameplayInputBlocked(locked);
        if (locked)
            player.SetSurfacePhysicsReady(false);
        SurfaceMultifunctionController multifunction =
            player.GetComponent<SurfaceMultifunctionController>();
        multifunction?.SetInputBlocked(locked);
    }

    void Fail(string stage, string diagnostics)
    {
        HasFailed = true;
        FailureReason = string.IsNullOrWhiteSpace(diagnostics)
            ? stage
            : $"{stage}: {diagnostics}";
        CurrentStage = PlanetSurfaceLoadStage.Failed;
        SetPlayerInputLocked(true);
        loadingUI?.ShowFailure(stage);
        Debug.LogError(
            $"PlanetSurfaceEntryCoordinator: {FailureReason}. " +
            "The loading screen remains active and gameplay input stays locked.",
            this);
        monitorRoutine = null;
    }
}
