using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ModularAssembly;
using SpacecraftEditor;
using UnityEngine;
using UnityPlanet.CityPcg;
using UnityPlanet.ModularAssembly;

public enum ModularBossFlightTestPhase
{
    Idle = 0,
    Preparing = 1,
    PursuitRoute = 2,
    DamagedFlight = 3,
    CoverStaging = 4,
    AwaitingBreach = 5,
    RamTelegraph = 6,
    RamCharge = 7,
    Completed = 8,
    Failed = 9
}

/// <summary>
/// Offsite Play Mode course for the real Boss PCG hull and RC3 module flight.
/// It is inert in Edit Mode and never uses the city's existing buildings.
/// </summary>
[DisallowMultipleComponent]
public sealed class ModularBossFlightTestCourse : MonoBehaviour
{
    sealed class MatchingCourseEnvironment : IPlanetEnvironmentProvider
    {
        public IPlanetEnvironmentProvider Source { get; set; }
        public bool ForceNoWind { get; set; }

        public PlanetEnvironmentSample Sample(
            Vector3 worldPosition,
            double simulationTime)
        {
            if (Source != null)
            {
                return Source.Sample(worldPosition, simulationTime);
            }
            // This is the exact production fallback used when a planar world
            // provider is unavailable, not a reduced-gravity test preset.
            return PlanetEnvironmentSample.EarthLike(
                Vector3.down * 9.81f,
                Mathf.Max(0f, worldPosition.y));
        }
    }

    const int TestLayer = ModularBossFunctionalTestHarness.PreviewLayer;
    const float TestTimeoutSeconds = 120f;
    const float WaypointRadius = 12f;
    const float StallSpeed = 1.5f;
    const float StallGraceSeconds = 2.5f;
    const float PlayerRouteSpeed = 24f;
    const float WaypointHoldSeconds = 2.2f;
    const float DamagedFlightObservationSeconds = 8f;
    const float DamagedFlightRecoveryTimeoutSeconds = 16f;
    // One large-hull height of transient drop is acceptable after losing half
    // of an upward bank, but by the end of the window the descent must already
    // be arrested. This distinguishes "slower" from "can no longer fly".
    const float MaximumDamagedAltitudeLoss = 32f;
    const float MinimumDamagedVerticalSpeed = -2.5f;
    const float MaximumCoverStagingSeconds = 24f;
    // A facade can be touched during the telegraph. Production combat then
    // resolves the intentional ram from OnCollisionStay after the charge has
    // committed (its first 0.45 s still enforces impact speed). Do not judge
    // the result from the earlier contact callback.
    const float BuildingRamSettlementSeconds = 1.35f;

    // The real runtime Boss replaces the static preview at its exact location.
    // The rest of the route is expressed relative to that model anchor.
    static readonly Vector3 BossSpawnLocal = Vector3.zero;
    static readonly Vector3[] LocalRoute =
    {
        // These are player positions. The production combat brain decides
        // the Boss path, orbit, altitude, braking and obstacle states.
        new Vector3(140f, 7f, 125f),
        new Vector3(275f, 23f, 245f),
        new Vector3(380f, 95f, 335f),
        new Vector3(475f, 57f, 410f)
    };
    static readonly string[] RouteLabels =
    {
        "远距接敌",
        "横向追击与战斗半径",
        "玩家升高后的高度追赶",
        "建筑前沿重新定位"
    };
    static readonly Vector3 LocalBuildingCenter =
        new Vector3(595f, 45f, 488f);
    static readonly Vector3 BuildingSize = new Vector3(76f, 150f, 54f);

    [SerializeField, Range(0, 5)] int difficultyTier;
    [SerializeField] int seed = 7319;
    [SerializeField] bool runContinuously = true;
    [SerializeField, Min(1f)] float loopRestartDelaySeconds = 4f;

    readonly List<GridModuleDefinition> definitions =
        new List<GridModuleDefinition>();
    readonly List<Material> materials = new List<Material>();

    GameObject runtimeRoot;
    Rigidbody bossBody;
    RobocraftMotionCoordinator motion;
    VehicleStructureGraph structureGraph;
    GridAssemblyPresenter bossPresenter;
    ModularBossGridFlightSession bossFlight;
    ModularBossReadabilityPresentation readability;
    ModularBossShieldRuntime shield;
    ModularBossCombatRuntime combat;
    ModularBossBuildResult build;
    Rigidbody playerBody;
    VehicleStructureGraph playerGraph;
    GridAssemblyPresenter playerPresenter;
    ModularContentService contentService;
    IReadOnlyDictionary<string, ModularContentRecord> contentRecords;
    readonly MatchingCourseEnvironment courseEnvironment =
        new MatchingCourseEnvironment();
    UrbanDestructibleBuilding testBuilding;
    Camera testCamera;
    int waypointIndex;
    int reachedWaypointCount;
    float waypointReachedAt = -1f;
    float startedAt;
    float phaseStartedAt;
    float distanceTravelled;
    float minimumAltitude;
    float maximumAltitude;
    float stalledSeconds;
    float worstContinuousStall;
    float lastSampleAt;
    Vector3 lastSamplePosition;
    bool impactDetected;
    float impactDetectedAt = -1f;
    float impactSpeed;
    Vector3 impactPoint;
    float shieldBeforeImpact;
    float shieldAfterImpact;
    bool buildingDestroyed;
    bool ramTelegraphSeen;
    bool ramChargeSeen;
    float ramChargeSeenAt = -1f;
    bool ramWarningSeen;
    bool facadeImpact;
    bool collisionGeometryValid;
    int collisionModuleCount;
    int collisionColliderCount;
    bool damagedFlightPassed;
    int deliberatelyRemovedThrusters;
    Vector3 damagedFlightStartPosition;
    float damagedFlightStartAltitude;
    bool damageSequenceStarted;
    Vector3 coverFrontTarget;
    Vector3 coverBehindTarget;
    Vector3 committedCoverApproach;
    Vector3 expectedFacadePoint;
    float coverStagingStartedAt;
    string failureReason = string.Empty;
    string lastReport = "尚未开始 Play Mode 试飞。";
    int completedCycles;
    int failedCycles;

    public int DifficultyTier => difficultyTier;
    public int Seed => seed;
    public ModularBossFlightTestPhase Phase { get; private set; } =
        ModularBossFlightTestPhase.Idle;
    public string LastReport => lastReport;
    public bool IsRunning => Phase != ModularBossFlightTestPhase.Idle &&
                             Phase != ModularBossFlightTestPhase.Completed &&
                             Phase != ModularBossFlightTestPhase.Failed;
    public bool Passed => Phase == ModularBossFlightTestPhase.Completed;
    public int ReachedWaypointCount => reachedWaypointCount;
    public int RequiredWaypointCount => LocalRoute.Length;
    public float CurrentSpeed => bossBody != null ? bossBody.velocity.magnitude : 0f;
    public float DistanceTravelled => distanceTravelled;
    public float AltitudeSpan => Mathf.Max(0f, maximumAltitude - minimumAltitude);
    public bool ImpactDetected => impactDetected;
    public bool BuildingDestroyed => buildingDestroyed;
    public bool RunContinuously => runContinuously;
    public int CompletedCycles => completedCycles;
    public int FailedCycles => failedCycles;

    void Start()
    {
        if (!Application.isPlaying || !runContinuously)
            return;
        Application.runInBackground = true;
        StartCoroutine(BeginAutomaticallyWhenSceneIsReady());
    }

    void OnValidate()
    {
        difficultyTier = Mathf.Clamp(difficultyTier, 0, 5);
        loopRestartDelaySeconds = Mathf.Max(1f, loopRestartDelaySeconds);
    }

    IEnumerator BeginAutomaticallyWhenSceneIsReady()
    {
        // Let the city's wind/magnetic test harness finish its own Start()
        // setup before the independent Boss course creates production actors.
        yield return null;
        BeginTest();
    }

    public void Configure(int tier, int testSeed)
    {
        difficultyTier = Mathf.Clamp(tier, 0, 5);
        seed = testSeed;
    }

    public void ConfigureLoop(bool enabled, float restartDelaySeconds = 4f)
    {
        runContinuously = enabled;
        loopRestartDelaySeconds = Mathf.Max(1f, restartDelaySeconds);
        if (!Application.isPlaying)
            return;
        if (enabled)
            BeginTest();
    }

    public void BeginTest()
    {
        if (!Application.isPlaying)
        {
            lastReport = "进入 Play Mode 后会在当前场景自动开始并循环，无需点击单次试飞按钮。";
            return;
        }
        StopAllCoroutines();
        StartCoroutine(RunTestLoop());
    }

    public void StopLoop()
    {
        runContinuously = false;
        StopAllCoroutines();
        combat?.SetCombatActive(false);
        ResetRuntime();
        Phase = ModularBossFlightTestPhase.Idle;
        lastReport = "Boss 循环已暂停；可在 Boss 工具中重新启动。";
    }

    IEnumerator RunTestLoop()
    {
        do
        {
            yield return BeginTestRoutine();
            while (IsRunning)
                yield return null;
            if (!runContinuously)
                break;
            yield return new WaitForSecondsRealtime(loopRestartDelaySeconds);
        }
        while (runContinuously && Application.isPlaying);
    }

    IEnumerator BeginTestRoutine()
    {
        ResetRuntime();
        ResetMeasurements();
        GetComponent<ModularBossFunctionalTestHarness>()?.ClearPreview();
        courseEnvironment.Source = PlanetEnvironmentRuntime.Active;
        courseEnvironment.ForceNoWind = false;
        Phase = ModularBossFlightTestPhase.Preparing;
        if (!BuildCourse(out string courseError))
        {
            Fail(courseError);
            yield break;
        }
        yield return LoadProductionContent();
        if (!string.IsNullOrEmpty(failureReason))
            yield break;
        yield return BuildPlayerTarget();
        if (!string.IsNullOrEmpty(failureReason))
            yield break;
        yield return BuildBoss();
        if (!string.IsNullOrEmpty(failureReason))
            yield break;
        if (!ValidateBossCollisionGeometry(out string collisionError))
        {
            Fail(collisionError);
            yield break;
        }
        BuildTestCamera();
        startedAt = Time.time;
        phaseStartedAt = startedAt;
        lastSampleAt = startedAt;
        lastSamplePosition = bossBody.position;
        minimumAltitude = bossBody.position.y;
        maximumAltitude = bossBody.position.y;
        waypointIndex = 0;
        playerBody.position = transform.TransformPoint(LocalRoute[0]);
        Phase = ModularBossFlightTestPhase.PursuitRoute;
        UpdateReport("真实 Boss 战斗状态机测试已开始");
        Debug.Log(
            "[Boss试飞] 使用生产内容、生产碰撞体、RC3 与 ModularBossCombatRuntime。",
            this);
    }

    void FixedUpdate()
    {
        if (!IsRunning || Phase == ModularBossFlightTestPhase.Preparing ||
            bossBody == null || motion == null || combat == null ||
            playerBody == null)
        {
            return;
        }
        if (Time.time - startedAt > TestTimeoutSeconds)
        {
            Fail("超过 120 秒仍未完成真实追击、损伤飞行和掩体冲撞链路。");
            return;
        }

        SampleFlight();
        switch (Phase)
        {
            case ModularBossFlightTestPhase.PursuitRoute:
                AdvancePlayerPursuitRoute();
                break;
            case ModularBossFlightTestPhase.DamagedFlight:
                AdvanceDamagedFlight();
                break;
            case ModularBossFlightTestPhase.CoverStaging:
                AdvanceCoverStaging();
                break;
            case ModularBossFlightTestPhase.AwaitingBreach:
            case ModularBossFlightTestPhase.RamTelegraph:
            case ModularBossFlightTestPhase.RamCharge:
                AdvanceRealBreachState();
                break;
        }
    }

    void LateUpdate()
    {
        if (testCamera == null || bossBody == null)
            return;
        Vector3 velocityDirection = bossBody.velocity.sqrMagnitude > 4f
            ? bossBody.velocity.normalized
            : bossBody.transform.forward;
        Vector3 desired = bossBody.worldCenterOfMass -
                          velocityDirection * 115f + Vector3.up * 58f;
        testCamera.transform.position = Vector3.Lerp(
            testCamera.transform.position,
            desired,
            1f - Mathf.Exp(-4f * Time.deltaTime));
        bool breachPhase = Phase == ModularBossFlightTestPhase.AwaitingBreach ||
                           Phase == ModularBossFlightTestPhase.RamTelegraph ||
                           Phase == ModularBossFlightTestPhase.RamCharge;
        Vector3 lookTarget = breachPhase && testBuilding != null
            ? Vector3.Lerp(
                bossBody.worldCenterOfMass,
                testBuilding.DestructionBounds.center,
                0.42f)
            : bossBody.worldCenterOfMass + velocityDirection * 25f;
        testCamera.transform.rotation = Quaternion.Slerp(
            testCamera.transform.rotation,
            Quaternion.LookRotation(
                lookTarget - testCamera.transform.position,
                Vector3.up),
            1f - Mathf.Exp(-5f * Time.deltaTime));
    }

    void AdvancePlayerPursuitRoute()
    {
        if (waypointIndex >= LocalRoute.Length)
        {
            if (!damageSequenceStarted)
                StartCoroutine(BeginThrusterDamageSequence());
            return;
        }
        Vector3 target = transform.TransformPoint(LocalRoute[waypointIndex]);
        MovePlayerTowards(target);
        float distance = Vector3.Distance(playerBody.position, target);
        if (distance <= WaypointRadius)
        {
            if (waypointReachedAt < 0f)
            {
                waypointReachedAt = Time.time;
                return;
            }
            if (Time.time - waypointReachedAt < WaypointHoldSeconds)
                return;
            reachedWaypointCount++;
            waypointIndex++;
            waypointReachedAt = -1f;
            string reachedLabel = RouteLabels[Mathf.Clamp(
                waypointIndex - 1, 0, RouteLabels.Length - 1)];
            UpdateReport(
                $"完成 {reachedLabel} {reachedWaypointCount}/{RequiredWaypointCount}");
            return;
        }
        waypointReachedAt = -1f;
    }

    void AdvanceDamagedFlight()
    {
        MovePlayerTowards(coverFrontTarget);
        if (Time.time - phaseStartedAt < DamagedFlightObservationSeconds)
            return;

        float altitudeLoss = damagedFlightStartAltitude -
                             bossBody.worldCenterOfMass.y;
        float movement = Vector3.Distance(
            damagedFlightStartPosition,
            bossBody.worldCenterOfMass);
        damagedFlightPassed = combat.EmergencyAssistActive &&
                              altitudeLoss <= MaximumDamagedAltitudeLoss &&
                              bossBody.velocity.y >=
                              MinimumDamagedVerticalSpeed &&
                              movement >= 8f &&
                              bossBody.velocity.magnitude < 95f;
        if (damagedFlightPassed)
        {
            BeginCoverStaging();
            return;
        }
        if (Time.time - phaseStartedAt <
            DamagedFlightRecoveryTimeoutSeconds)
        {
            UpdateReport("推进器受损后正在恢复高度控制");
            return;
        }
        if (!damagedFlightPassed)
        {
            Fail(
                $"真实AI/受损飞行失败：16秒内没有恢复高度控制；" +
                $"应急辅助={combat.EmergencyAssistActive}，" +
                $"掉高={altitudeLoss:0.0}m，垂直速度={bossBody.velocity.y:0.0}m/s，" +
                $"移动={movement:0.0}m。");
            return;
        }
    }

    void BeginCoverStaging()
    {
        Bounds building = testBuilding.DestructionBounds;
        Vector3 approach = Vector3.ProjectOnPlane(
            building.center - bossBody.worldCenterOfMass,
            Vector3.up);
        if (approach.sqrMagnitude < 0.001f)
            approach = Vector3.forward;
        approach.Normalize();
        committedCoverApproach = approach;
        float projectedExtent = Mathf.Abs(approach.x) * building.extents.x +
                                Mathf.Abs(approach.z) * building.extents.z;
        coverFrontTarget = building.center -
                           approach * (projectedExtent + 12f);
        coverBehindTarget = building.center +
                            approach * (projectedExtent + 52f);
        Bounds bossBounds = structureGraph.ResolveVisualBounds();
        expectedFacadePoint = ModularBossCombatPolicy.
            ResolveBuildingFacadeBreachPoint(building, bossBounds);
        float nominalBossAltitudeOffset = Mathf.Max(
            22f + difficultyTier * 2f,
            bossBounds.extents.y + 14f) + 7f;
        coverFrontTarget.y = expectedFacadePoint.y -
                             nominalBossAltitudeOffset;
        coverBehindTarget.y = coverFrontTarget.y;
        coverStagingStartedAt = Time.time;
        phaseStartedAt = Time.time;
        Phase = ModularBossFlightTestPhase.CoverStaging;
        UpdateReport("推进器损伤飞行通过，接近建筑边缘");
    }

    void AdvanceCoverStaging()
    {
        if (testBuilding == null || testBuilding.IsUrbanDestroyed)
        {
            Fail("专用测试楼在 Boss 实际撞击前已经失效。");
            return;
        }
        MovePlayerTowards(coverFrontTarget);
        Bounds building = testBuilding.DestructionBounds;
        float facadeDistance = Vector3.Distance(
            bossBody.worldCenterOfMass,
            building.ClosestPoint(bossBody.worldCenterOfMass));
        bool playerReady = Vector3.Distance(
            playerBody.position,
            coverFrontTarget) <= WaypointRadius;
        Bounds bossBounds = structureGraph.ResolveVisualBounds();
        expectedFacadePoint = ModularBossCombatPolicy.
            ResolveBuildingFacadeBreachPoint(building, bossBounds);
        float maximumAcquisitionDistance =
            build.Profile.PreferredCombatRadius +
            Mathf.Max(bossBounds.extents.x, bossBounds.extents.z) +
            ModularBossCombatPolicy.CoverBreachStandoff;
        bool aboveFootprint =
            bossBounds.center.x > building.min.x &&
            bossBounds.center.x < building.max.x &&
            bossBounds.center.z > building.min.z &&
            bossBounds.center.z < building.max.z;
        bool bossAtEdge = facadeDistance <= maximumAcquisitionDistance &&
                          !aboveFootprint &&
                          ModularBossCombatPolicy.
                              IsCoverBreachVerticallyAligned(
                                  bossBounds,
                                  expectedFacadePoint);
        bool timedOut = Time.time - coverStagingStartedAt >=
                        MaximumCoverStagingSeconds;
        if (!playerReady || !shield.IsActive)
            return;

        if (!bossAtEdge)
        {
            if (timedOut)
            {
                Fail(
                    "真实AI/立面集结失败：Boss未能在24秒内从楼外对齐侧立面；" +
                    $"立面距离={facadeDistance:0.0}/{maximumAcquisitionDistance:0.0}m，" +
                    $"楼顶投影={aboveFootprint}，" +
                    $"高度误差={Mathf.Abs(bossBounds.center.y - expectedFacadePoint.y):0.0}m。");
            }
            return;
        }

        playerBody.MovePosition(coverBehindTarget);
        phaseStartedAt = Time.time;
        Phase = ModularBossFlightTestPhase.AwaitingBreach;
        UpdateReport("玩家进入楼后，等待真实掩体冲撞状态");
    }

    void AdvanceRealBreachState()
    {
        KeepPlayerHiddenBehindTestBuilding();
        ModularBossObstacleState state = combat.ObstacleState;
        if (state == ModularBossObstacleState.CoverBreachTelegraph)
        {
            if (!ramTelegraphSeen)
            {
                ramTelegraphSeen = true;
                shieldBeforeImpact = shield.Integrity;
            }
            ramWarningSeen |= combat.RamWarningVisible;
            Phase = ModularBossFlightTestPhase.RamTelegraph;
        }
        else if (state == ModularBossObstacleState.CoverBreachCharge)
        {
            ramChargeSeen = true;
            ramChargeSeenAt = Time.time;
            ramWarningSeen |= combat.RamWarningVisible;
            Phase = ModularBossFlightTestPhase.RamCharge;
        }

        if (!impactDetected)
            return;
        buildingDestroyed = testBuilding == null ||
                            testBuilding.IsUrbanDestroyed;
        shieldAfterImpact = shield != null ? shield.Integrity : 0f;
        bool normalFlight = reachedWaypointCount >= RequiredWaypointCount &&
                            distanceTravelled >= 260f &&
                            AltitudeSpan >= 32f &&
                            worstContinuousStall <= 4.5f;
        float actualShieldLoss = shieldBeforeImpact > 0.01f
            ? Mathf.Clamp01(
                (shieldBeforeImpact - shieldAfterImpact) /
                shieldBeforeImpact)
            : 0f;
        bool shieldResponded = actualShieldLoss >= 0.30f;
        bool impactResolutionSettled = buildingDestroyed && shieldResponded;
        if (!impactResolutionSettled &&
            Time.time - impactDetectedAt < BuildingRamSettlementSeconds)
        {
            return;
        }
        bool realRamPresentation = ramTelegraphSeen && ramChargeSeen &&
                                   ramWarningSeen;
        if (normalFlight && damagedFlightPassed && collisionGeometryValid &&
            buildingDestroyed && shieldResponded && facadeImpact &&
            realRamPresentation)
        {
            Complete();
        }
        else
        {
            Fail(
                $"真实链路不完整：飞行={normalFlight}，损伤飞行={damagedFlightPassed}，" +
                $"碰撞体={collisionGeometryValid}，预警/蓄力表现={realRamPresentation}，" +
                $"立面撞击={facadeImpact}，楼房破坏={buildingDestroyed}，" +
                $"护盾响应={shieldResponded}。");
        }
    }

    void KeepPlayerHiddenBehindTestBuilding()
    {
        if (testBuilding == null || bossBody == null || playerBody == null)
            return;
        Bounds building = testBuilding.DestructionBounds;
        Vector3 approach = Vector3.ProjectOnPlane(
            building.center - bossBody.worldCenterOfMass,
            Vector3.up);
        if (approach.sqrMagnitude < 0.001f)
            approach = committedCoverApproach.sqrMagnitude > 0.001f
                ? committedCoverApproach.normalized
                : Vector3.forward;
        approach.Normalize();
        float projectedExtent = Mathf.Abs(approach.x) * building.extents.x +
                                Mathf.Abs(approach.z) * building.extents.z;
        coverBehindTarget = building.center +
                            approach * (projectedExtent + 24f);
        coverBehindTarget.y = Mathf.Clamp(
            coverFrontTarget.y,
            building.min.y + 12f,
            building.max.y - 12f);
        // This phase represents a player deliberately rotating around the same
        // tower to keep hard cover. The target stays kinematic and combat still
        // has to identify the live building and enter its own breach states.
        playerBody.MovePosition(coverBehindTarget);
    }

    void MovePlayerTowards(Vector3 target)
    {
        Vector3 next = Vector3.MoveTowards(
            playerBody.position,
            target,
            PlayerRouteSpeed * Time.fixedDeltaTime);
        playerBody.MovePosition(next);
    }

    void SampleFlight()
    {
        float now = Time.time;
        float delta = Mathf.Max(0f, now - lastSampleAt);
        Vector3 position = bossBody.worldCenterOfMass;
        distanceTravelled += Vector3.Distance(position, lastSamplePosition);
        minimumAltitude = Mathf.Min(minimumAltitude, position.y);
        maximumAltitude = Mathf.Max(maximumAltitude, position.y);
        if (now - startedAt > StallGraceSeconds &&
            bossBody.velocity.magnitude < StallSpeed)
        {
            stalledSeconds += delta;
            worstContinuousStall = Mathf.Max(
                worstContinuousStall,
                stalledSeconds);
        }
        else
        {
            stalledSeconds = 0f;
        }
        lastSampleAt = now;
        lastSamplePosition = position;
        if (Time.frameCount % 30 == 0)
            UpdateReport("运行中");
    }

    public void NotifyBossCollision(Collision collision)
    {
        bool recentObservedCharge = ramChargeSeen &&
            ramChargeSeenAt >= 0f &&
            Time.time - ramChargeSeenAt <=
            ModularBossCombatPolicy.CoverBreachChargeSeconds + 1.5f;
        if ((Phase != ModularBossFlightTestPhase.RamTelegraph &&
             Phase != ModularBossFlightTestPhase.RamCharge &&
             Phase != ModularBossFlightTestPhase.AwaitingBreach) ||
            collision == null || collision.contactCount <= 0 ||
            testBuilding == null || combat == null ||
            !recentObservedCharge)
        {
            return;
        }
        Collider hitCollider = collision.collider;
        UrbanDestructibleBuilding hitBuilding =
            hitCollider != null
                ? hitCollider.GetComponentInParent<UrbanDestructibleBuilding>()
                : null;
        if (hitBuilding != testBuilding)
            return;

        ContactPoint contact = collision.GetContact(0);
        for (int index = 0; index < collision.contactCount; index++)
        {
            ContactPoint candidate = collision.GetContact(index);
            if (!ModularBossCombatPolicy.IsFacadeImpactNormal(
                    candidate.normal,
                    Vector3.up))
            {
                continue;
            }
            contact = candidate;
            break;
        }
        if (impactDetected)
            return;
        impactDetected = true;
        impactDetectedAt = Time.time;
        impactSpeed = collision.relativeVelocity.magnitude;
        impactPoint = contact.point;
        Bounds buildingBounds = testBuilding.DestructionBounds;
        const float faceTolerance = 1.5f;
        bool onSideFace =
            Mathf.Abs(impactPoint.x - buildingBounds.min.x) <= faceTolerance ||
            Mathf.Abs(impactPoint.x - buildingBounds.max.x) <= faceTolerance ||
            Mathf.Abs(impactPoint.z - buildingBounds.min.z) <= faceTolerance ||
            Mathf.Abs(impactPoint.z - buildingBounds.max.z) <= faceTolerance;
        bool insideFacadeHeight =
            impactPoint.y >= buildingBounds.min.y - faceTolerance &&
            impactPoint.y <= buildingBounds.max.y + faceTolerance;
        facadeImpact = onSideFace && insideFacadeHeight &&
                       ModularBossCombatPolicy.IsFacadeImpactNormal(
                           contact.normal,
                           Vector3.up);
        // Damage, shield loss, building collapse and recovery are deliberately
        // left to ModularBossCombatRuntime.OnCollisionEnter/Stay. This relay
        // only observes the result, so the test cannot make a bad ram pass.
        UpdateReport("真实战斗状态机已发生建筑碰撞");
    }

    IEnumerator LoadProductionContent()
    {
        GridModuleDefinition[] baseDefinitions =
            Resources.LoadAll<GridModuleDefinition>(
                "ModularAssembly/Definitions");
        if (baseDefinitions == null || baseDefinitions.Length == 0)
        {
            Fail("缺少生产用 ModularAssembly/Definitions，不能用替身模型测试。");
            yield break;
        }

        contentService = runtimeRoot.AddComponent<ModularContentService>();
        yield return contentService.Initialize();
        if (contentService.Catalog == null ||
            contentService.Catalog.Count == 0)
        {
            Fail("生产模块内容目录不可用：" +
                 (contentService.LastError ?? "目录为空"));
            yield break;
        }

        var registry = new GridAssemblyModel(baseDefinitions);
        contentRecords = NeoXCatalogIntegration.RegisterDefinitions(
            registry,
            contentService.Catalog);
        definitions.Clear();
        definitions.AddRange(registry.Definitions.Values);
    }

    IEnumerator BuildPlayerTarget()
    {
        var playerModel = new GridAssemblyModel(definitions);
        if (playerModel.Find(GridAssemblyModel.CoreRuntimeId) == null)
        {
            Fail("生产模块定义没有可用核心，无法建立真实 Boss 目标。");
            yield break;
        }

        GameObject player = new GameObject("Boss测试_模拟玩家目标");
        player.layer = TestLayer;
        player.transform.SetParent(runtimeRoot.transform, false);
        player.transform.localPosition = LocalRoute[0];
        playerBody = player.AddComponent<Rigidbody>();
        playerBody.useGravity = false;
        playerBody.isKinematic = true;
        playerBody.collisionDetectionMode =
            CollisionDetectionMode.ContinuousSpeculative;

        Transform parts = new GameObject("Parts").transform;
        parts.SetParent(player.transform, false);
        Transform core = new GameObject("CoreVisual").transform;
        core.SetParent(player.transform, false);
        ShipAssembly assembly = player.AddComponent<ShipAssembly>();
        assembly.Configure(playerBody, parts, null, 650f);
        playerPresenter = player.AddComponent<GridAssemblyPresenter>();
        playerPresenter.Initialize(playerModel, assembly, core);
        NeoXCatalogIntegration integration =
            player.AddComponent<NeoXCatalogIntegration>();
        integration.InitializeRuntime(
            contentService,
            playerPresenter,
            contentRecords);
        while (!integration.IsReady || contentService.IsLoadingAssets)
            yield return null;
        integration.StopRuntimeRebuildTracking();
        SetLayerRecursively(player, TestLayer);

        ModularBossGridFlightSession flight =
            player.AddComponent<ModularBossGridFlightSession>();
        playerGraph = player.AddComponent<VehicleStructureGraph>();
        playerGraph.Initialize(playerModel, playerPresenter, flight);
        playerGraph.SetAutomaticReturnToBuild(false);
        playerGraph.ConfigureIntegrityMultipliers(10f, 10000f, 10f, 10f);
        playerGraph.SetDamageEnabled(true);
        playerGraph.BeginFlight();
        VehicleCombatTeamUtility.SetTeam(player, VehicleCombatTeam.Player);
    }

    IEnumerator BeginThrusterDamageSequence()
    {
        damageSequenceStarted = true;

        // Reproduce the real order: shield breaks first, then player damage
        // reaches exposed modules on a later frame.
        shield.ApplyEnvironmentalImpact(
            1f,
            bossBody.worldCenterOfMass,
            playerBody.gameObject);
        yield return null;
        yield return null;

        string[] upwardThrusters = build.ThrusterDirections
            .Where(item => item.Value == ModularBossThrusterDirection.Up)
            .Select(item => item.Key)
            .Take(Mathf.Max(
                1,
                Mathf.CeilToInt(
                    build.CountThrusters(ModularBossThrusterDirection.Up) *
                    0.5f)))
            .ToArray();
        structureGraph.BeginDamageBatch();
        try
        {
            foreach (string runtimeId in upwardThrusters)
            {
                float damage = structureGraph.MaximumIntegrity(runtimeId) + 1f;
                structureGraph.ApplyDamage(
                    runtimeId,
                    new SpaceDamageInfo(
                        damage,
                        bossBody.worldCenterOfMass,
                        Vector3.up * damage,
                        SpaceDamageType.Projectile,
                        playerBody.gameObject));
            }
        }
        finally
        {
            structureGraph.EndDamageBatch();
        }
        yield return new WaitForSeconds(0.25f);

        deliberatelyRemovedThrusters =
            combat.InitialThrusterCount - combat.LiveThrusterCount;
        if (deliberatelyRemovedThrusters <= 0)
        {
            Fail("推进器损伤步骤没有移除真实推进器模块。");
            yield break;
        }
        if (!ValidateBossCollisionGeometry(out string postDamageError))
        {
            Fail("Boss geometry changed after module damage: " +
                 postDamageError);
            yield break;
        }
        damagedFlightStartPosition = bossBody.worldCenterOfMass;
        damagedFlightStartAltitude = bossBody.worldCenterOfMass.y;
        Vector3 forward = Vector3.ProjectOnPlane(
            playerBody.position - bossBody.worldCenterOfMass,
            Vector3.up);
        if (forward.sqrMagnitude < 0.001f)
            forward = bossBody.transform.forward;
        coverFrontTarget = playerBody.position +
                           forward.normalized * 90f +
                           Vector3.up * 48f;
        phaseStartedAt = Time.time;
        Phase = ModularBossFlightTestPhase.DamagedFlight;
        UpdateReport("已拆除半组上升推进器，验证降级飞行");
    }

    bool ValidateBossCollisionGeometry(out string error)
    {
        error = string.Empty;
        collisionModuleCount = bossPresenter != null
            ? bossPresenter.Views.Count
            : 0;
        collisionColliderCount = 0;
        int missing = 0;
        int incorrectCollider = 0;
        int incorrectBossPose = 0;
        int incorrectStructureVisual = 0;
        bool initialized = false;
        Bounds colliderBounds = default;
        if (bossPresenter != null)
        {
            foreach (GridModuleView view in bossPresenter.Views.Values)
            {
                if (view == null || !view.gameObject.activeInHierarchy)
                    continue;
                Collider[] colliders = view.GetComponentsInChildren<Collider>(true)
                    .Where(item => item != null && item.enabled &&
                                   !item.isTrigger)
                    .ToArray();
                if (colliders.Length == 0)
                {
                    missing++;
                    continue;
                }
                collisionColliderCount += colliders.Length;
                BoxCollider logical = colliders.Length == 1
                    ? colliders[0] as BoxCollider
                    : null;
                Vector3 expectedColliderSize =
                    (Vector3)view.Record.Definition.Footprint;
                if (logical == null || logical.transform != view.transform ||
                    !Approximately(logical.center, Vector3.zero, 0.001f) ||
                    !Approximately(
                        logical.size,
                        expectedColliderSize,
                        0.001f))
                {
                    incorrectCollider++;
                }
                Vector3 expectedPosition = GridAssemblyModel.ModuleCenter(
                    view.Record) * ModularBossModuleScalePolicy.LinearScale;
                if (!Approximately(
                        view.transform.localScale,
                        Vector3.one *
                        ModularBossModuleScalePolicy.LinearScale,
                        0.001f) ||
                    !Approximately(
                        view.transform.localPosition,
                        expectedPosition,
                        0.001f))
                {
                    incorrectBossPose++;
                }
                if (view.Record.Definition.Category ==
                        GridModuleCategory.Structure &&
                    view.Record.Definition.ModuleId.IndexOf(
                        "block_111",
                        StringComparison.OrdinalIgnoreCase) >= 0 &&
                    (!TryResolveEnabledVisualBounds(view, out Bounds local) ||
                     !Approximately(
                         local.size,
                         expectedColliderSize,
                         0.035f) ||
                     !Approximately(local.center, Vector3.zero, 0.035f)))
                {
                    incorrectStructureVisual++;
                }
                foreach (Collider collider in colliders)
                {
                    if (!initialized)
                    {
                        colliderBounds = collider.bounds;
                        initialized = true;
                    }
                    else
                    {
                        colliderBounds.Encapsulate(collider.bounds);
                    }
                }
            }
        }
        Bounds visualBounds = structureGraph.ResolveVisualBounds();
        float sizeRatio = initialized
            ? colliderBounds.size.magnitude /
              Mathf.Max(0.01f, visualBounds.size.magnitude)
            : 0f;
        collisionGeometryValid = bossBody != null && initialized &&
                                  missing == 0 &&
                                  collisionColliderCount == collisionModuleCount &&
                                  incorrectCollider == 0 &&
                                  incorrectBossPose == 0 &&
                                  incorrectStructureVisual == 0 &&
                                  sizeRatio >= 0.80f && sizeRatio <= 1.20f &&
                                  Vector3.Distance(
                                     colliderBounds.center,
                                     visualBounds.center) <=
                                 Mathf.Max(6f, visualBounds.extents.magnitude * 0.2f);
        if (!collisionGeometryValid)
        {
            error =
                $"Boss 复合碰撞体不完整：模块={collisionModuleCount}，" +
                $"碰撞体={collisionColliderCount}，缺失模块={missing}，" +
                $"包围盒比例={sizeRatio:0.00}。";
        }
        return collisionGeometryValid;
    }

    static bool TryResolveEnabledVisualBounds(
        GridModuleView view,
        out Bounds result)
    {
        result = default;
        if (view == null)
            return false;
        bool initialized = false;
        Matrix4x4 worldToView = view.transform.worldToLocalMatrix;
        foreach (Renderer renderer in
                 view.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || !renderer.enabled ||
                !renderer.gameObject.activeInHierarchy ||
                renderer is ParticleSystemRenderer ||
                renderer is LineRenderer || renderer is TrailRenderer)
            {
                continue;
            }
            Bounds localBounds = renderer.localBounds;
            Matrix4x4 rendererToView =
                worldToView * renderer.transform.localToWorldMatrix;
            Vector3 minimum = localBounds.min;
            Vector3 maximum = localBounds.max;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = rendererToView.MultiplyPoint3x4(
                    new Vector3(
                        (corner & 1) == 0 ? minimum.x : maximum.x,
                        (corner & 2) == 0 ? minimum.y : maximum.y,
                        (corner & 4) == 0 ? minimum.z : maximum.z));
                if (!initialized)
                {
                    result = new Bounds(point, Vector3.zero);
                    initialized = true;
                }
                else
                    result.Encapsulate(point);
            }
        }
        return initialized;
    }

    static bool Approximately(Vector3 first, Vector3 second, float tolerance)
    {
        return Mathf.Abs(first.x - second.x) <= tolerance &&
               Mathf.Abs(first.y - second.y) <= tolerance &&
               Mathf.Abs(first.z - second.z) <= tolerance;
    }

    bool BuildCourse(out string error)
    {
        error = string.Empty;
        runtimeRoot = new GameObject("BossFlightAndBuildingTest_RuntimeOnly");
        runtimeRoot.layer = TestLayer;
        runtimeRoot.transform.SetParent(transform, false);
        runtimeRoot.transform.localPosition = Vector3.zero;

        BuildRouteMarkers();
        GameObject coordinatorObject = new GameObject("TestUrbanDestructionCoordinator");
        coordinatorObject.layer = TestLayer;
        coordinatorObject.transform.SetParent(runtimeRoot.transform, false);
        UrbanDestructionCoordinator coordinator =
            coordinatorObject.AddComponent<UrbanDestructionCoordinator>();
        coordinator.Configure(new UrbanDestructionSettings());

        GameObject buildingObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        buildingObject.name = "Boss专用可撞测试楼";
        buildingObject.layer = TestLayer;
        buildingObject.transform.SetParent(runtimeRoot.transform, false);
        buildingObject.transform.localPosition = LocalBuildingCenter;
        buildingObject.transform.localScale = BuildingSize;
        buildingObject.GetComponent<Renderer>().sharedMaterial =
            CreateMaterial("TestBuilding", new Color(0.20f, 0.24f, 0.31f),
                0.62f, 0.35f, false);
        testBuilding = buildingObject.AddComponent<UrbanDestructibleBuilding>();
        testBuilding.Configure(
            new AirCombatBuildingLot
            {
                stableId = "boss-flight-test-building",
                center = buildingObject.transform.position,
                size = BuildingSize,
                yaw = 0f,
                band = AirCombatBuildingBand.High,
                archetype = AirCombatBuildingArchetype.CombatTower
            },
            coordinator);
        return true;
    }

    IEnumerator BuildBoss()
    {
        if (!ModularBossPcgGenerator.TryBuild(
                definitions,
                difficultyTier,
                seed,
                out build,
                out string generationError))
        {
            Fail(generationError);
            yield break;
        }

        GameObject boss = new GameObject("Boss真实模块试飞体");
        boss.layer = TestLayer;
        boss.transform.SetParent(runtimeRoot.transform, false);
        boss.transform.localPosition = BossSpawnLocal;
        Vector3 firstDirection = LocalRoute[0] - BossSpawnLocal;
        firstDirection = Vector3.ProjectOnPlane(
            firstDirection,
            Vector3.up);
        boss.transform.localRotation = Quaternion.LookRotation(
            firstDirection.normalized,
            Vector3.up);

        bossBody = boss.AddComponent<Rigidbody>();
        bossBody.useGravity = false;
        bossBody.drag = 0f;
        bossBody.angularDrag = 0f;
        bossBody.isKinematic = true;
        bossBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        bossBody.interpolation = RigidbodyInterpolation.Interpolate;

        Transform parts = new GameObject("Parts").transform;
        parts.SetParent(boss.transform, false);
        Transform core = new GameObject("CoreVisual").transform;
        core.SetParent(boss.transform, false);
        ShipAssembly assembly = boss.AddComponent<ShipAssembly>();
        assembly.Configure(bossBody, parts, null, 650f);
        bossPresenter = boss.AddComponent<GridAssemblyPresenter>();
        bossPresenter.Initialize(build.Model, assembly, core);
        ModularBossModuleScalePolicy.Apply(bossPresenter);

        NeoXCatalogIntegration integration =
            boss.AddComponent<NeoXCatalogIntegration>();
        integration.InitializeRuntime(
            contentService,
            bossPresenter,
            contentRecords);
        while (!integration.IsReady || contentService.IsLoadingAssets)
            yield return null;
        integration.StopRuntimeRebuildTracking();
        bossPresenter.SetRuntimeRemovalOptimization(true);
        foreach (GridModuleView view in bossPresenter.Views.Values)
        {
            if (view == null)
                continue;
            view.gameObject.SetActive(true);
            SetLayerRecursively(view.gameObject, TestLayer);
        }
        ModularBossModuleScalePolicy.Apply(bossPresenter);
        ModularBossModuleColliderPolicy.Apply(bossPresenter);

        bossFlight =
            boss.AddComponent<ModularBossGridFlightSession>();
        structureGraph = boss.AddComponent<VehicleStructureGraph>();
        structureGraph.Initialize(build.Model, bossPresenter, bossFlight);
        structureGraph.SetAutomaticReturnToBuild(false);
        structureGraph.ConfigureIntegrityMultipliers(
            build.Profile.StructureIntegrityMultiplier,
            build.Profile.CoreIntegrityMultiplier,
            build.Profile.SystemIntegrityMultiplier,
            build.Profile.WeaponIntegrityMultiplier);
        structureGraph.SetDamageEnabled(true);
        structureGraph.BeginFlight();

        motion = boss.AddComponent<RobocraftMotionCoordinator>();
        motion.ConfigureExplicit(
            bossBody,
            assembly,
            build.Model,
            bossPresenter);
        motion.SetMassGeometryScale(ModularBossModuleScalePolicy.LinearScale);
        // Use the active production environment provider when the scene has
        // one; otherwise use RC3's own production Earth-like fallback.
        motion.SetEnvironmentProvider(courseEnvironment);
        motion.ControlsEnabled = true;
        motion.SetCoreAssistMode(VehicleCoreAssistMode.Standard);
        float multiplier = ModularBossFlightAuthorityPolicy.ResolveMultiplier(
                               motion.Telemetry.totalMass,
                               9.81f,
                               motion.CaptureActuatorDiagnostics()) *
                           ModularBossCombatPolicy.PursuitForceMultiplier;
        motion.SetActuatorForceMultiplier(Mathf.Min(
            ModularBossFlightAuthorityPolicy.MaximumMultiplier,
            multiplier));
        Vector3 emergencyForce =
            ModularBossFlightAuthorityPolicy.ResolveEmergencyCoreForce(
                motion.Telemetry.totalMass,
                9.81f);
        motion.ConfigureTrainingCoreAuthority(
            emergencyForce.y,
            ModularBossFlightAuthorityPolicy.ResolveEmergencyDownForce(
                motion.Telemetry.totalMass),
            Mathf.Max(emergencyForce.x, emergencyForce.z));
        if (!ModularBossFlightAuthorityPolicy.HasMinimumAuthority(
                motion.Telemetry.totalMass,
                9.81f,
                motion.CaptureActuatorDiagnostics()))
        {
            Fail("试飞 Boss 没有达到真实六轴最低推力要求。");
            yield break;
        }
        if (!motion.TryBeginFlight(false, out string physicsMessage))
        {
            Fail("RC3 试飞启动失败：" + physicsMessage);
            yield break;
        }

        readability =
            boss.AddComponent<ModularBossReadabilityPresentation>();
        readability.Configure(bossPresenter, build);
        readability.StopTrackingPresenterRebuilds();
        shield = boss.AddComponent<ModularBossShieldRuntime>();
        shield.Configure(structureGraph, bossPresenter, difficultyTier);

        combat = boss.AddComponent<ModularBossCombatRuntime>();
        if (!combat.BindPreparedDiagnosticRig(
                bossBody,
                playerBody,
                playerGraph,
                bossPresenter,
                structureGraph,
                motion,
                bossFlight,
                readability,
                shield,
                build,
                motion.ActuatorForceMultiplier,
                out string bindError))
        {
            Fail(bindError);
            yield break;
        }
        ModularBossFlightTestImpactRelay relay =
            boss.AddComponent<ModularBossFlightTestImpactRelay>();
        relay.Configure(this);
    }

    void BuildRouteMarkers()
    {
        Material routeMaterial = CreateLineMaterial(
            "BossFlightRoute", new Color(0.05f, 0.75f, 1f, 0.82f));
        GameObject routeObject = new GameObject("Boss试飞航线");
        routeObject.layer = TestLayer;
        routeObject.transform.SetParent(runtimeRoot.transform, false);
        LineRenderer line = routeObject.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.positionCount = LocalRoute.Length + 1;
        line.startWidth = 1.2f;
        line.endWidth = 1.2f;
        line.sharedMaterial = routeMaterial;
        line.startColor = new Color(0.05f, 0.75f, 1f, 0.82f);
        line.endColor = new Color(1f, 0.24f, 0.06f, 0.88f);
        for (int index = 0; index < LocalRoute.Length; index++)
        {
            line.SetPosition(index, LocalRoute[index]);
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = $"航点_{index}_{RouteLabels[index]}";
            marker.layer = TestLayer;
            marker.transform.SetParent(runtimeRoot.transform, false);
            marker.transform.localPosition = LocalRoute[index];
            marker.transform.localScale = Vector3.one * 7f;
            Collider collider = marker.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);
            marker.GetComponent<Renderer>().sharedMaterial = routeMaterial;
        }
        line.SetPosition(LocalRoute.Length, LocalBuildingCenter);
    }

    void BuildTestCamera()
    {
        GameObject cameraObject = new GameObject("Boss试飞跟随相机");
        cameraObject.transform.SetParent(runtimeRoot.transform, true);
        testCamera = cameraObject.AddComponent<Camera>();
        testCamera.depth = 100f;
        testCamera.clearFlags = CameraClearFlags.SolidColor;
        testCamera.backgroundColor = new Color(0.006f, 0.018f, 0.042f, 1f);
        testCamera.fieldOfView = 52f;
        testCamera.nearClipPlane = 0.3f;
        testCamera.farClipPlane = 1200f;
        testCamera.transform.position = bossBody.position +
                                        new Vector3(-100f, 55f, -100f);
        testCamera.transform.LookAt(bossBody.worldCenterOfMass);

        GameObject lightObject = new GameObject("Boss试飞主光源");
        lightObject.transform.SetParent(runtimeRoot.transform, false);
        lightObject.transform.rotation = Quaternion.Euler(42f, -38f, 0f);
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(0.72f, 0.86f, 1f);
        light.intensity = 1.45f;
        light.shadows = LightShadows.Soft;
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
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", metallic);
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", smoothness);
        if (emission && material.HasProperty("_EmissionColor"))
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * 1.5f);
        }
        materials.Add(material);
        return material;
    }

    Material CreateLineMaterial(string materialName, Color color)
    {
        Shader shader = Shader.Find("Sprites/Default") ??
                        Shader.Find("Universal Render Pipeline/Unlit") ??
                        Shader.Find("Unlit/Color");
        Material material = new Material(shader)
        {
            name = materialName,
            hideFlags = HideFlags.HideAndDontSave,
            renderQueue = 3100
        };
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
        materials.Add(material);
        return material;
    }

    void Complete()
    {
        Phase = ModularBossFlightTestPhase.Completed;
        combat?.SetCombatActive(false);
        completedCycles++;
        UpdateReport("全部通过");
        WriteResultArtifact();
        Debug.Log("[Boss试飞][通过]\n" + lastReport, this);
    }

    void Fail(string reason)
    {
        failureReason = reason ?? "未知失败";
        Phase = ModularBossFlightTestPhase.Failed;
        combat?.SetCombatActive(false);
        failedCycles++;
        UpdateReport("失败");
        WriteResultArtifact();
        Debug.LogError("[Boss试飞][失败]\n" + lastReport, this);
    }

    void UpdateReport(string state)
    {
        var report = new StringBuilder(600);
        report.AppendLine($"[{state}] 难度 {difficultyTier} / 种子 {seed}");
        report.AppendLine(
            $"自动循环：{runContinuously}  通过：{completedCycles}  失败：{failedCycles}  " +
            $"复位等待：{loopRestartDelaySeconds:0.#}s");
        report.AppendLine($"阶段：{Phase}  航点：{reachedWaypointCount}/{RequiredWaypointCount}");
        if (Phase == ModularBossFlightTestPhase.PursuitRoute &&
            waypointIndex >= 0 && waypointIndex < RouteLabels.Length)
        {
            report.AppendLine("当前追击行为：" + RouteLabels[waypointIndex]);
        }
        report.AppendLine($"速度：{CurrentSpeed:0.0} m/s  路程：{distanceTravelled:0.0} m");
        report.AppendLine($"高度变化：{AltitudeSpan:0.0} m  最长停滞：{worstContinuousStall:0.0} s");
        report.AppendLine(
            $"真实AI：{combat?.ObstacleState.ToString() ?? "未绑定"}  " +
            $"预警={ramTelegraphSeen}  蓄力={ramChargeSeen}  红光表现={ramWarningSeen}");
        if (combat != null)
        {
            report.AppendLine(
                $"撞楼诊断：非立面拒绝={combat.RejectedNonFacadeBuildingContacts}  " +
                $"冲锋到期未命中={combat.ExpiredCoverBreachCharges}  " +
                $"最后拒绝法线={combat.LastRejectedBuildingContactNormal}  " +
                $"接触点={combat.LastRejectedBuildingContactPoint}");
        }
        report.AppendLine(
            $"碰撞体：{collisionColliderCount}/{collisionModuleCount}  " +
            $"完整={collisionGeometryValid}  损伤推进器={deliberatelyRemovedThrusters}  " +
            $"降级飞行={damagedFlightPassed}");
        report.AppendLine($"撞楼：{impactDetected}  撞击速度：{impactSpeed:0.0} m/s  楼房破坏：{buildingDestroyed}");
        if (impactDetected)
            report.AppendLine($"撞击点：{impactPoint}  建筑立面={facadeImpact}");
        if (impactDetected)
            report.AppendLine($"护盾：{shieldBeforeImpact:0} → {shieldAfterImpact:0}");
        if (!string.IsNullOrEmpty(failureReason))
            report.AppendLine("原因：" + failureReason);
        report.AppendLine(
            "场景：UrbanEnvironmentalTrapTest 原场景内、Boss模型工具根节点；" +
            "运行时专用楼，不占用或改写城市建筑。");
        lastReport = report.ToString();
    }

    void WriteResultArtifact()
    {
        try
        {
            string directory = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "Artifacts", "BossFlightTests"));
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                Path.Combine(directory, "latest_boss_flight_test.txt"),
                lastReport,
                Encoding.UTF8);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Boss 试飞报告写入失败：" + exception.Message, this);
        }
    }

    void ResetMeasurements()
    {
        waypointIndex = 0;
        reachedWaypointCount = 0;
        startedAt = 0f;
        phaseStartedAt = 0f;
        distanceTravelled = 0f;
        minimumAltitude = float.PositiveInfinity;
        maximumAltitude = float.NegativeInfinity;
        stalledSeconds = 0f;
        worstContinuousStall = 0f;
        impactDetected = false;
        impactDetectedAt = -1f;
        impactSpeed = 0f;
        impactPoint = Vector3.zero;
        shieldBeforeImpact = 0f;
        shieldAfterImpact = 0f;
        buildingDestroyed = false;
        ramTelegraphSeen = false;
        ramChargeSeen = false;
        ramChargeSeenAt = -1f;
        ramWarningSeen = false;
        facadeImpact = false;
        collisionGeometryValid = false;
        collisionModuleCount = 0;
        collisionColliderCount = 0;
        damagedFlightPassed = false;
        deliberatelyRemovedThrusters = 0;
        damagedFlightStartPosition = Vector3.zero;
        damagedFlightStartAltitude = 0f;
        damageSequenceStarted = false;
        coverFrontTarget = Vector3.zero;
        coverBehindTarget = Vector3.zero;
        committedCoverApproach = Vector3.zero;
        expectedFacadePoint = Vector3.zero;
        coverStagingStartedAt = 0f;
        waypointReachedAt = -1f;
        failureReason = string.Empty;
        lastReport = "正在创建城外试飞跑道……";
    }

    void ResetRuntime()
    {
        bossBody = null;
        motion = null;
        structureGraph = null;
        bossPresenter = null;
        bossFlight = null;
        readability = null;
        shield = null;
        combat = null;
        build = null;
        playerBody = null;
        playerGraph = null;
        playerPresenter = null;
        contentService = null;
        contentRecords = null;
        testBuilding = null;
        testCamera = null;
        if (runtimeRoot != null)
            Destroy(runtimeRoot);
        runtimeRoot = null;
        // Definitions are production Resources and catalog registrations,
        // not temporary ScriptableObjects owned by this tool.
        definitions.Clear();
        foreach (Material material in materials)
            if (material != null)
                Destroy(material);
        materials.Clear();
    }

    void OnDestroy()
    {
        StopAllCoroutines();
        ResetRuntime();
    }

    static void SetLayerRecursively(GameObject root, int layer)
    {
        if (root == null)
            return;
        root.layer = layer;
        foreach (Transform child in root.transform)
            SetLayerRecursively(child.gameObject, layer);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.05f, 0.75f, 1f, 0.9f);
        Vector3 bossSpawn = transform.TransformPoint(BossSpawnLocal);
        Gizmos.DrawWireCube(bossSpawn, Vector3.one * 10f);
        for (int index = 0; index < LocalRoute.Length; index++)
        {
            Vector3 point = transform.TransformPoint(LocalRoute[index]);
            Gizmos.DrawWireSphere(point, 7f);
            if (index > 0)
                Gizmos.DrawLine(
                    transform.TransformPoint(LocalRoute[index - 1]),
                    point);
            else
                Gizmos.DrawLine(bossSpawn, point);
        }
        Gizmos.color = new Color(1f, 0.25f, 0.05f, 0.9f);
        Vector3 buildingCenter = transform.TransformPoint(LocalBuildingCenter);
        Gizmos.DrawWireCube(buildingCenter, BuildingSize);
        Gizmos.DrawLine(
            transform.TransformPoint(LocalRoute[LocalRoute.Length - 1]),
            buildingCenter);
    }
}

[DisallowMultipleComponent]
public sealed class ModularBossFlightTestImpactRelay : MonoBehaviour
{
    ModularBossFlightTestCourse owner;

    public void Configure(ModularBossFlightTestCourse value)
    {
        owner = value;
    }

    void OnCollisionEnter(Collision collision)
    {
        owner?.NotifyBossCollision(collision);
    }

    void OnCollisionStay(Collision collision)
    {
        owner?.NotifyBossCollision(collision);
    }
}
