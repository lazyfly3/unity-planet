using System;
using System.Collections;
using System.Collections.Generic;
using ModularAssembly;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityPlanet.EDPCG;
using UnityPlanet.ModularAssembly;
using UnityPlanet.SpaceStation;
using UnityPlanet.SpaceStation.Enhancement;

public enum FinitePlanetMissionObjectiveKind
{
    Clearance,
    Survey,
    Assault,
    Boss
}

public sealed class FinitePlanetMissionRules
{
    public FinitePlanetMissionObjectiveKind Kind { get; private set; }
    public bool RequiresUrbanEnvironment =>
        Kind == FinitePlanetMissionObjectiveKind.Boss;
    public int RequiredKills { get; private set; }
    public int RosterCount { get; private set; }
    public int ObjectiveCount { get; private set; }
    public float ScanSeconds { get; private set; }
    public float ExtractionSeconds { get; private set; }
    public float CoreIntegrity { get; private set; }
    public int PlanetDifficultyIndex { get; private set; }
    public int GalaxyCoinReward { get; private set; }
    public string DifficultyLabel { get; private set; }
    public string ObjectiveDescription { get; private set; }

    public static FinitePlanetMissionRules Resolve(string missionId)
    {
        return Resolve(missionId, 0);
    }

    public static FinitePlanetMissionRules Resolve(
        string missionId,
        int planetDifficultyIndex)
    {
        int tier = Mathf.Clamp(
            planetDifficultyIndex,
            0,
            ProceduralInterstellarGenerator.StarterSystemPlanetCount - 1);
        string difficulty = DifficultyForTier(tier);
        if (string.Equals(
                missionId,
                "modular_boss",
                StringComparison.Ordinal))
        {
            ModularBossGenerationProfile boss =
                ModularBossGenerationProfile.ForTier(tier);
            return new FinitePlanetMissionRules
            {
                Kind = FinitePlanetMissionObjectiveKind.Boss,
                PlanetDifficultyIndex = tier,
                GalaxyCoinReward = 240 + tier * 55,
                DifficultyLabel = difficulty + " · Boss",
                ObjectiveDescription =
                    $"摧毁模块化首领；核心位于 {boss.CoreCoverDepth} 层结构之后"
            };
        }
        if (string.Equals(
                missionId,
                "wind_canyon",
                StringComparison.Ordinal))
        {
            return new FinitePlanetMissionRules
            {
                Kind = FinitePlanetMissionObjectiveKind.Survey,
                RosterCount = RosterForTier(tier),
                ObjectiveCount = 3,
                ScanSeconds = 2.5f,
                ExtractionSeconds = 3f,
                PlanetDifficultyIndex = tier,
                GalaxyCoinReward = 90 + tier * 25,
                DifficultyLabel = difficulty,
                ObjectiveDescription = "扫描三座信标并抵达撤离点"
            };
        }
        if (string.Equals(
                missionId,
                "industrial_outpost",
                StringComparison.Ordinal))
        {
            return new FinitePlanetMissionRules
            {
                Kind = FinitePlanetMissionObjectiveKind.Assault,
                RequiredKills = 12 + tier * 2,
                RosterCount = RosterForTier(tier),
                ObjectiveCount = 3,
                CoreIntegrity = 350f,
                PlanetDifficultyIndex = tier,
                GalaxyCoinReward = 140 + tier * 30,
                DifficultyLabel = difficulty + " · 突袭",
                ObjectiveDescription =
                    $"摧毁三座能源核心；处理 {RosterForTier(tier)} 架敌机" +
                    $"（有效击落目标 {12 + tier * 2}）"
            };
        }
        return new FinitePlanetMissionRules
        {
            Kind = FinitePlanetMissionObjectiveKind.Clearance,
            RequiredKills = 10 + tier * 2,
            RosterCount = RosterForTier(tier),
            PlanetDifficultyIndex = tier,
            GalaxyCoinReward = 100 + tier * 25,
            DifficultyLabel = difficulty,
            ObjectiveDescription =
                $"处理 {RosterForTier(tier)} 架敌机并清理战场" +
                $"（有效击落目标 {10 + tier * 2}）"
        };
    }

    static int RosterForTier(int tier)
    {
        return EdpcgTierSettings.DefaultRosterCountForTier(tier);
    }

    static string DifficultyForTier(int tier)
    {
        switch (tier)
        {
            case 0:
                return "低危 I";
            case 1:
                return "低危 II";
            case 2:
                return "中危 I";
            case 3:
                return "中危 II";
            case 4:
                return "高危 I";
            default:
                return "高危 II";
        }
    }
}

/// <summary>
/// Adapts the CombatTest horde director to a temporary finite planet arena.
/// The planet owns terrain and perimeter entrances; the existing director
/// continues to own enemy pooling, wave composition, AI and combat balance.
/// </summary>
[DisallowMultipleComponent]
public sealed class FinitePlanetHordeCombatController : MonoBehaviour
{
    const string StationSceneName = "SpaceStationUpgradeTest";
    const string OrbitSceneName = "InterstellarFlight";
    const float DestructionReturnDelay = 1.5f;
    const float VictoryReturnDelay = 2.5f;
    const float ObjectiveRadius = 65f;

    readonly List<Vector3> ingressWorldPositions =
        new List<Vector3>(8);
    readonly List<Vector3> scanWorldPositions =
        new List<Vector3>(3);
    readonly List<FinitePlanetEnergyCoreObjective> energyCores =
        new List<FinitePlanetEnergyCoreObjective>(3);
    readonly List<FinitePlanetObjectiveMarker> objectiveMarkers =
        new List<FinitePlanetObjectiveMarker>(4);

    InfinitePlanarSurfaceWorld world;
    PlanarSurfaceModularFlightController flightController;
    Rigidbody playerBody;
    WeaponSystemCoordinator weapons;
    VehicleStructureGraph playerGraph;
    GridAssemblyModel playerModel;
    ModularContentService contentService;
    IReadOnlyDictionary<string, ModularContentRecord> contentRecords;
    HordeCombatDirector director;
    EdpcgEncounterRuntime edpcg;
    ModularBossCombatRuntime boss;
    ModularBossIntroductionDirector bossIntroduction;
    Coroutine returnRoutine;
    Vector3 battleCenter;
    Vector3 extractionPoint;
    FinitePlanetMissionRules missionRules;
    FinitePlanetObjectiveMarker extractionMarker;
    string missionId;
    string missionName;
    bool[] scanComplete = Array.Empty<bool>();
    float[] scanProgress = Array.Empty<float>();
    float extractionProgress;
    int destroyedCoreCount;
    bool finalClearStarted;
    bool missionCompleted;
    float nextStatusRefreshAt;
    string hudText = string.Empty;
    bool sessionRunning;
    bool transitionStarted;
    bool settlementStarted;
    bool bossIntroductionPlayed;
    float bossBridgeHintEndsAt;
    string bossBridgeHint = string.Empty;

    public bool IsPrepared { get; private set; }
    public string PreparationError { get; private set; } = string.Empty;
    public HordeCombatDirector Director => director;
    public EdpcgEncounterRuntime EdpcgRuntime => edpcg;
    public int IngressCount => ingressWorldPositions.Count;
    public FinitePlanetMissionRules MissionRules => missionRules;
    public string ObjectiveStatus { get; private set; } = string.Empty;

    public IEnumerator Prepare(
        InfinitePlanarSurfaceWorld targetWorld,
        PlanarSurfaceModularFlightController targetFlightController,
        Rigidbody targetBody,
        WeaponSystemCoordinator targetWeapons,
        VehicleStructureGraph targetGraph,
        GridAssemblyModel targetPlayerModel,
        ModularContentService targetContentService,
        IReadOnlyDictionary<string, ModularContentRecord> targetContentRecords)
    {
        IsPrepared = false;
        PreparationError = string.Empty;
        bossIntroductionPlayed = false;
        world = targetWorld;
        flightController = targetFlightController;
        playerBody = targetBody;
        weapons = targetWeapons;
        playerGraph = targetGraph;
        playerModel = targetPlayerModel;
        contentService = targetContentService;
        contentRecords = targetContentRecords;
        missionId = PlanetOrbitChapterSelectionContext.MissionId ??
                    string.Empty;
        missionName = PlanetOrbitChapterSelectionContext.MissionName ??
                      missionId;
        missionRules = FinitePlanetMissionRules.Resolve(
            missionId,
            PlanetOrbitChapterSelectionContext.PlanetDifficultyIndex);
        if (missionRules.RequiresUrbanEnvironment &&
            PlanetOrbitChapterSelectionContext.EnvironmentKind !=
            PlanetMissionEnvironmentKind.Urban)
        {
            PreparationError =
                "Modular Boss missions require an urban battlefield.";
            yield break;
        }
        weapons?.SetAutoAimAllowed(
            missionRules.Kind != FinitePlanetMissionObjectiveKind.Boss);
        weapons?.SetManualAimRequiredForFire(
            missionRules.Kind == FinitePlanetMissionObjectiveKind.Boss);

        if (world == null || !world.IsFiniteCombatArea)
        {
            PreparationError =
                "Finite planet horde combat requires a finite combat world.";
            yield break;
        }
        if (missionRules.RequiresUrbanEnvironment)
        {
            FinitePlanetUrbanCombatRuntime urbanCombat =
                world.GetComponent<FinitePlanetUrbanCombatRuntime>();
            if (urbanCombat == null || !urbanCombat.IsReady)
            {
                PreparationError =
                    "Modular Boss creation requires a prepared urban battlefield.";
                yield break;
            }
        }
        FinitePlanetDefenseLayoutPlan layout =
            world.FiniteCombatTerrainPlan?.DefenseLayout;
        if (layout == null || !layout.IsValid ||
            layout.enemyIngresses == null ||
            layout.enemyIngresses.Length == 0)
        {
            PreparationError =
                "Finite planet horde combat has no valid PCG enemy entrances.";
            yield break;
        }
        if (flightController == null || playerBody == null ||
            weapons == null || playerGraph == null)
        {
            PreparationError =
                "Finite planet horde combat is missing the restored player ship or weapon graph.";
            yield break;
        }

        battleCenter = ToWorld(layout.combatCenter);
        ingressWorldPositions.Clear();
        for (int index = 0; index < layout.enemyIngresses.Length; index++)
        {
            Vector3 position = ToWorld(
                layout.enemyIngresses[index].position);
            ingressWorldPositions.Add(position);
        }

        VehicleCombatTeamUtility.SetTeam(
            playerBody.gameObject,
            VehicleCombatTeam.Player);
        if (missionRules.Kind == FinitePlanetMissionObjectiveKind.Boss)
        {
            yield return PrepareBoss(layout);
            if (boss == null || !boss.IsPrepared)
            {
                PreparationError = boss == null
                    ? "Modular Boss creation failed."
                    : boss.PreparationError;
                yield break;
            }
            playerGraph.Destroyed -= HandlePlayerDestroyed;
            playerGraph.Destroyed += HandlePlayerDestroyed;
            IsPrepared = true;
            yield break;
        }
        director = GetComponent<HordeCombatDirector>() ??
                   gameObject.AddComponent<HordeCombatDirector>();
        director.ConfigureObjectiveDrivenSession(true);
        director.ConfigurePlannedIngresses(ingressWorldPositions);
        int edpcgSeed = world.FiniteCombatTerrainPlan != null
            ? world.FiniteCombatTerrainPlan.Seed
            : PlanetOrbitChapterSelectionContext.MissionSeed;
        FinitePlanetUrbanCombatRuntime urbanRuntime =
            world.GetComponent<FinitePlanetUrbanCombatRuntime>();
        edpcg = GetComponent<EdpcgEncounterRuntime>() ??
                gameObject.AddComponent<EdpcgEncounterRuntime>();
        edpcg.Configure(
            director,
            playerBody,
            playerGraph,
            urbanRuntime != null && urbanRuntime.IsReady
                ? urbanRuntime
                : null,
            missionId,
            missionRules.PlanetDifficultyIndex,
            edpcgSeed);
        director.ConfigureEdpcgRuntime(edpcg);
        WeaponVisualPool visuals =
            weapons.GetComponent<WeaponVisualPool>();
        WeaponProjectilePool projectiles =
            weapons.GetComponent<WeaponProjectilePool>();
        yield return director.Prewarm(
            null,
            visuals,
            projectiles,
            null);
        if (!director.PreparationValid)
        {
            PreparationError = string.IsNullOrWhiteSpace(
                director.PreparationError)
                ? "Finite planet enemy pool preparation failed."
                : director.PreparationError;
            yield break;
        }

        yield return director.PrepareNavigation(
            playerBody,
            battleCenter,
            world.FiniteCombatRadius,
            null);
        if (!director.NavigationReady)
        {
            PreparationError = string.IsNullOrWhiteSpace(
                director.NavigationError)
                ? "Finite planet enemy air routes could not be built."
                : director.NavigationError;
            director.EndSession();
            yield break;
        }

        if (!BuildMissionObjectives(layout, out string objectiveError))
        {
            PreparationError = objectiveError;
            director.EndSession();
            yield break;
        }

        playerGraph.Destroyed -= HandlePlayerDestroyed;
        playerGraph.Destroyed += HandlePlayerDestroyed;
        IsPrepared = true;
    }

    public void SetGameplayReady(bool ready)
    {
        if (!ready)
        {
            if (bossIntroduction != null && bossIntroduction.IsPlaying)
                bossIntroduction.Cancel(false);
            sessionRunning = false;
            director?.EndSession();
            boss?.SetCombatActive(false);
            return;
        }
        if (!IsPrepared || transitionStarted || sessionRunning ||
            (bossIntroduction != null && bossIntroduction.IsPlaying))
            return;

        int seed = world.FiniteCombatTerrainPlan != null
            ? world.FiniteCombatTerrainPlan.Seed
            : PlanetOrbitChapterSelectionContext.MissionSeed;
        extractionProgress = 0f;
        finalClearStarted = false;
        missionCompleted = false;
        nextStatusRefreshAt = 0f;
        for (int index = 0; index < scanProgress.Length; index++)
        {
            scanProgress[index] = 0f;
            scanComplete[index] = false;
        }
        if (missionRules.Kind == FinitePlanetMissionObjectiveKind.Boss)
        {
            if (!bossIntroductionPlayed &&
                TryBeginBossIntroduction(seed))
            {
                return;
            }
            StartBossCombatSession(seed);
            return;
        }
        sessionRunning = true;
        director.BeginSession(
            playerBody,
            battleCenter,
            world.FiniteCombatRadius,
            seed,
            1);
        RefreshObjectiveStatus();
        Debug.Log(
            $"[FinitePlanetHorde] Started with {IngressCount} PCG entrances " +
            $"and seed {seed}.",
            this);
    }

    bool TryBeginBossIntroduction(int seed)
    {
        Camera camera = Camera.main;
        if (boss == null || flightController == null ||
            playerBody == null || camera == null)
        {
            return false;
        }

        bossIntroduction =
            GetComponent<ModularBossIntroductionDirector>() ??
            gameObject.AddComponent<ModularBossIntroductionDirector>();
        bool started = bossIntroduction.Play(
            boss,
            flightController,
            playerBody,
            camera,
            string.IsNullOrWhiteSpace(missionName)
                ? "\u9996\u9886\u62e6\u622a"
                : missionName,
            missionRules.DifficultyLabel,
            () =>
            {
                if (this == null || transitionStarted || !IsPrepared)
                    return;
                StartBossCombatSession(seed);
            });
        bossIntroductionPlayed |= started;
        return started;
    }

    void StartBossCombatSession(int seed)
    {
        if (sessionRunning || transitionStarted || boss == null)
            return;

        sessionRunning = true;
        boss.SetCombatActive(true);
        BeginBossBridgeHint();
        RefreshObjectiveStatus();
        Debug.Log(
            $"[FinitePlanetBoss] Started tier " +
            $"{missionRules.PlanetDifficultyIndex} with seed {seed}.",
            this);
    }

    void Update()
    {
        if (!sessionRunning || transitionStarted)
            return;
        // Runtime script reloads can preserve the session flag while clearing
        // this non-Unity rules object.  End the orphaned session cleanly
        // instead of throwing a NullReferenceException every frame.
        if (missionRules == null)
        {
            SetGameplayReady(false);
            return;
        }
        if (missionRules.Kind == FinitePlanetMissionObjectiveKind.Boss)
        {
            if (Time.unscaledTime >= nextStatusRefreshAt)
            {
                nextStatusRefreshAt = Time.unscaledTime + 0.15f;
                RefreshObjectiveStatus();
            }
            return;
        }
        if (director == null)
            return;
        director.Tick(Time.deltaTime);
        UpdateMissionObjective();
    }

    IEnumerator PrepareBoss(FinitePlanetDefenseLayoutPlan layout)
    {
        if (playerModel == null || contentService == null ||
            contentRecords == null)
        {
            PreparationError =
                "Modular Boss requires the restored player module catalog.";
            yield break;
        }
        Vector3 playerPosition = playerBody.worldCenterOfMass;
        float preferredSpawnDistance =
            ModularBossCombatPolicy.InitialSpawnDistance(
                missionRules.PlanetDifficultyIndex);
        Vector3 fromPlayer = Vector3.ProjectOnPlane(
            battleCenter - playerPosition,
            Vector3.up);
        if (fromPlayer.sqrMagnitude < 0.001f)
            fromPlayer = Vector3.forward;
        fromPlayer.Normalize();
        FinitePlanetUrbanCombatRuntime urbanCombat =
            world.GetComponent<FinitePlanetUrbanCombatRuntime>();
        Vector3 spawn;
        if (urbanCombat == null || !urbanCombat.IsReady ||
            !urbanCombat.TryResolveBossRoadSpawn(
                playerPosition,
                preferredSpawnDistance,
                72f,
                out spawn,
                out _))
        {
            spawn = ResolveObjectivePosition(
                layout.combatCenter,
                72f) + fromPlayer * preferredSpawnDistance;
        }
        Vector3 facePlayer = Vector3.ProjectOnPlane(
            playerPosition - spawn,
            Vector3.up);
        if (facePlayer.sqrMagnitude < 0.001f)
            facePlayer = Vector3.back;
        Quaternion rotation = Quaternion.LookRotation(
            facePlayer.normalized,
            Vector3.up);
        GameObject root = new GameObject("FinitePlanetModularBoss");
        root.transform.SetParent(transform, true);
        boss = root.AddComponent<ModularBossCombatRuntime>();
        int seed = world.FiniteCombatTerrainPlan != null
            ? world.FiniteCombatTerrainPlan.Seed
            : PlanetOrbitChapterSelectionContext.MissionSeed;
        yield return boss.Prepare(
            world,
            playerBody,
            playerGraph,
            playerModel,
            contentService,
            contentRecords,
            spawn,
            rotation,
            missionRules.PlanetDifficultyIndex,
            seed ^ unchecked((int)0x6B055A17));
        if (!boss.IsPrepared)
            yield break;
        boss.Destroyed -= HandleBossDestroyed;
        boss.Destroyed += HandleBossDestroyed;
    }

    bool BuildMissionObjectives(
        FinitePlanetDefenseLayoutPlan layout,
        out string error)
    {
        error = string.Empty;
        if (missionRules.Kind == FinitePlanetMissionObjectiveKind.Clearance ||
            missionRules.Kind == FinitePlanetMissionObjectiveKind.Boss)
            return true;

        if (layout.powerPositions == null ||
            layout.powerPositions.Length < missionRules.ObjectiveCount)
        {
            error =
                "The selected mission requires three valid PCG objective positions.";
            return false;
        }

        if (missionRules.Kind == FinitePlanetMissionObjectiveKind.Survey)
        {
            if (layout.retreatPoints == null ||
                layout.retreatPoints.Length == 0)
            {
                error =
                    "The survey mission requires a valid PCG extraction point.";
                return false;
            }
            scanComplete = new bool[missionRules.ObjectiveCount];
            scanProgress = new float[missionRules.ObjectiveCount];
            for (int index = 0; index < missionRules.ObjectiveCount; index++)
            {
                Vector3 position = ResolveObjectivePosition(
                    layout.powerPositions[index].position,
                    18f);
                scanWorldPositions.Add(position);
                objectiveMarkers.Add(CreateMarker(
                    "SurveyBeacon_" + index.ToString("D2"),
                    position,
                    new Color(0.08f, 0.85f, 1f, 1f),
                    1f));
            }
            extractionPoint = ResolveObjectivePosition(
                layout.retreatPoints[0].position,
                26f);
            extractionMarker = CreateMarker(
                "SurveyExtraction",
                extractionPoint,
                new Color(0.18f, 1f, 0.45f, 1f),
                1.25f);
            extractionMarker.gameObject.SetActive(false);
            objectiveMarkers.Add(extractionMarker);
            return true;
        }

        for (int index = 0; index < missionRules.ObjectiveCount; index++)
        {
            Vector3 position = ResolveObjectivePosition(
                layout.powerPositions[index].position,
                7f);
            GameObject root = new GameObject(
                "EnergyCoreObjective_" + index.ToString("D2"));
            root.transform.SetParent(transform, false);
            root.transform.position = position;
            FinitePlanetEnergyCoreObjective core =
                root.AddComponent<FinitePlanetEnergyCoreObjective>();
            core.Configure(missionRules.CoreIntegrity, index);
            core.Destroyed += HandleEnergyCoreDestroyed;
            energyCores.Add(core);
        }
        return true;
    }

    FinitePlanetObjectiveMarker CreateMarker(
        string markerName,
        Vector3 position,
        Color color,
        float scale)
    {
        GameObject root = new GameObject(markerName);
        root.transform.SetParent(transform, false);
        root.transform.position = position;
        FinitePlanetObjectiveMarker marker =
            root.AddComponent<FinitePlanetObjectiveMarker>();
        marker.Configure(color, scale);
        return marker;
    }

    Vector3 ResolveObjectivePosition(
        Vector3 planPosition,
        float clearance)
    {
        FinitePlanetUrbanCombatRuntime urbanCombat =
            world.GetComponent<FinitePlanetUrbanCombatRuntime>();
        if (urbanCombat != null && urbanCombat.IsReady)
        {
            return urbanCombat.ProjectPlanPosition(planPosition) +
                   Vector3.up * clearance;
        }
        Vector3 probe = world.FromPersistentAddress(
            new PlanarSurfaceAddress(
                planPosition.x,
                planPosition.z,
                0f));
        if (world.TryProjectToSurface(probe, out PlanetSurfaceSample sample))
            return sample.point + Vector3.up * clearance;
        return ToWorld(planPosition);
    }

    void UpdateMissionObjective()
    {
        switch (missionRules.Kind)
        {
            case FinitePlanetMissionObjectiveKind.Survey:
                UpdateSurveyObjective();
                break;
            case FinitePlanetMissionObjectiveKind.Assault:
                if (destroyedCoreCount >= missionRules.ObjectiveCount &&
                    IsNonBossRosterResolved())
                {
                    BeginFinalClear();
                    if (director.AliveCount == 0)
                        CompleteMission();
                }
                break;
            default:
                if (IsNonBossRosterResolved())
                {
                    BeginFinalClear();
                    if (director.AliveCount == 0)
                        CompleteMission();
                }
                break;
        }
        if (Time.unscaledTime >= nextStatusRefreshAt)
        {
            nextStatusRefreshAt = Time.unscaledTime + 0.2f;
            RefreshObjectiveStatus();
        }
    }

    bool IsNonBossRosterResolved()
    {
        return edpcg != null
            ? edpcg.IsEncounterResolved
            : director != null &&
              director.Kills >= missionRules.RequiredKills;
    }

    void UpdateSurveyObjective()
    {
        bool allScanned = true;
        for (int index = 0; index < scanComplete.Length; index++)
        {
            if (scanComplete[index])
                continue;
            allScanned = false;
            float distance = Vector3.Distance(
                playerBody.worldCenterOfMass,
                scanWorldPositions[index]);
            scanProgress[index] = distance <= ObjectiveRadius
                ? scanProgress[index] + Time.deltaTime
                : Mathf.Max(0f, scanProgress[index] - Time.deltaTime * 0.7f);
            if (scanProgress[index] < missionRules.ScanSeconds)
                continue;
            scanComplete[index] = true;
            objectiveMarkers[index]?.MarkComplete();
        }

        allScanned = true;
        for (int index = 0; index < scanComplete.Length; index++)
            allScanned &= scanComplete[index];
        if (!allScanned)
            return;

        BeginFinalClear();
        if (extractionMarker != null &&
            !extractionMarker.gameObject.activeSelf)
        {
            extractionMarker.gameObject.SetActive(true);
        }
        float extractionDistance = Vector3.Distance(
            playerBody.worldCenterOfMass,
            extractionPoint);
        extractionProgress = extractionDistance <= ObjectiveRadius * 1.15f
            ? extractionProgress + Time.deltaTime
            : Mathf.Max(0f, extractionProgress - Time.deltaTime);
        if (extractionProgress >= missionRules.ExtractionSeconds)
            CompleteMission();
    }

    void HandleEnergyCoreDestroyed(
        FinitePlanetEnergyCoreObjective core)
    {
        destroyedCoreCount++;
        RefreshObjectiveStatus();
        if (destroyedCoreCount >= missionRules.ObjectiveCount &&
            IsNonBossRosterResolved())
            BeginFinalClear();
    }

    void HandleBossDestroyed()
    {
        if (!sessionRunning || transitionStarted)
            return;
        CompleteMission();
    }

    void BeginFinalClear()
    {
        if (finalClearStarted)
            return;
        finalClearStarted = true;
        director.BeginFinalClear();
    }

    void CompleteMission()
    {
        if (missionCompleted || transitionStarted || settlementStarted)
            return;
        missionCompleted = true;
        BeginSettlement(CombatSettlementOutcome.Victory);
    }

    public void AbandonForStationReturn()
    {
        BeginVoluntaryReturnSettlement();
    }

    public bool BeginVoluntaryReturnSettlement()
    {
        if (missionRules == null || settlementStarted ||
            missionCompleted || transitionStarted)
        {
            return false;
        }
        BeginSettlement(CombatSettlementOutcome.VoluntaryReturn);
        return settlementStarted;
    }

    void BeginSettlement(CombatSettlementOutcome outcome)
    {
        if (settlementStarted || missionRules == null)
            return;

        settlementStarted = true;
        transitionStarted = true;
        if (bossIntroduction != null && bossIntroduction.IsPlaying)
            bossIntroduction.Cancel(false);
        sessionRunning = false;
        director?.EndSession();
        boss?.SetCombatActive(false);
        flightController?.SetGameplayReady(false);
        if (playerBody != null && !playerBody.isKinematic)
        {
            playerBody.velocity = Vector3.zero;
            playerBody.angularVelocity = Vector3.zero;
        }

        int creditedKills = edpcg != null
            ? edpcg.CreditedKills
            : director == null ? 0 : director.Kills;
        int completedObjectives = 0;
        if (missionRules.Kind == FinitePlanetMissionObjectiveKind.Survey)
        {
            for (int index = 0; index < scanComplete.Length; index++)
            {
                if (scanComplete[index])
                    completedObjectives++;
            }
        }
        else if (missionRules.Kind ==
                 FinitePlanetMissionObjectiveKind.Assault)
        {
            completedObjectives = destroyedCoreCount;
        }
        float bossDamageRatio = boss == null
            ? 0f
            : 1f - boss.IntegrityRatio;
        CombatSettlementData settlement =
            CombatSettlementCalculator.Calculate(
                outcome,
                PlanetOrbitChapterSelectionContext.PlanetId,
                missionId,
                missionName,
                missionRules,
                creditedKills,
                completedObjectives,
                bossDamageRatio);

        GalaxyCurrencyService.AddGalaxyCoins(
            settlement.galaxyCoinReward);
        if (settlement.IsVictory)
        {
            PlanetMissionProgressService.RecordCompletion(
                settlement.planetId,
                settlement.missionId,
                settlement.totalScore,
                settlement.galaxyCoinReward);
        }

        ObjectiveStatus = settlement.IsVictory
            ? "任务完成，正在汇总战果"
            : outcome == CombatSettlementOutcome.Defeat
                ? "飞船损毁，正在汇总战果"
                : "战术返航，正在汇总战果";
        UpdateHudText();
        Debug.Log(
            $"[CombatSettlement] outcome={outcome}, " +
            $"mission='{missionId}', kills={creditedKills}, " +
            $"score={settlement.totalScore}, " +
            $"reward={settlement.galaxyCoinReward} Galaxy Coins.",
            this);
        CombatSettlementController.Show(
            settlement,
            ReturnToStationAfterSettlement);
    }

    void ReturnToStationAfterSettlement()
    {
        PlanetOrbitChapterSelectionContext.Clear();
        SpaceStationFlowContext.PrepareOrbitalReturnToStation();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        SceneManager.LoadScene(StationSceneName, LoadSceneMode.Single);
    }

    IEnumerator ReturnToOrbitAfterVictory()
    {
        yield return new WaitForSecondsRealtime(VictoryReturnDelay);
        transitionStarted = true;
        Vector3 forward = playerBody != null
            ? Vector3.ProjectOnPlane(
                playerBody.transform.forward,
                Vector3.up).normalized
            : Vector3.forward;
        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.forward;
        Vector3 velocity = playerBody != null
            ? playerBody.velocity
            : Vector3.zero;
        GalaxyTravelManager manager = GalaxyTravelManager.Instance;
        if (manager != null && manager.IsInterstellarGalaxy)
        {
            manager.OpenInterstellarFlightFromSurface(
                world,
                forward,
                velocity);
        }
        else
        {
            PlanetOrbitChapterSelectionContext.Clear();
            SceneManager.LoadScene(OrbitSceneName, LoadSceneMode.Single);
        }
        returnRoutine = null;
    }

    void RefreshObjectiveStatus()
    {
        if (missionCompleted)
        {
            UpdateHudText();
            return;
        }
        switch (missionRules.Kind)
        {
            case FinitePlanetMissionObjectiveKind.Boss:
            {
                if (boss == null)
                {
                    ObjectiveStatus = "模块化首领正在部署";
                    break;
                }
                int weakestCurrent = int.MaxValue;
                int weakestInitial = 0;
                foreach (ModularBossThrusterDirection direction in
                         Enum.GetValues(typeof(ModularBossThrusterDirection)))
                {
                    int current = boss.LiveThrustersFor(direction);
                    if (current >= weakestCurrent)
                        continue;
                    weakestCurrent = current;
                    weakestInitial = boss.InitialThrustersFor(direction);
                }
                ObjectiveStatus =
                    $"首领结构 {boss.IntegrityRatio:P0}  ·  " +
                    $"武器 {boss.LiveWeaponCount}  ·  " +
                    $"最弱方向喷气 {Mathf.Max(0, weakestCurrent)}/" +
                    $"{weakestInitial}\n{boss.DamageStateLabel}";
                if (!string.IsNullOrEmpty(boss.ObstacleStateLabel))
                    ObjectiveStatus += "\n" + boss.ObstacleStateLabel;
                break;
            }
            case FinitePlanetMissionObjectiveKind.Survey:
                int scanned = 0;
                for (int index = 0; index < scanComplete.Length; index++)
                    if (scanComplete[index])
                        scanned++;
                ObjectiveStatus = scanned < missionRules.ObjectiveCount
                    ? $"扫描信标 {scanned}/{missionRules.ObjectiveCount}"
                    : $"撤离点稳定 {extractionProgress:0.0}/{missionRules.ExtractionSeconds:0.0} 秒";
                break;
            case FinitePlanetMissionObjectiveKind.Assault:
                if (destroyedCoreCount < missionRules.ObjectiveCount)
                {
                    ObjectiveStatus =
                        $"摧毁能源核心 {destroyedCoreCount}/{missionRules.ObjectiveCount}  ·  " +
                        $"击落 {director.Kills}/{missionRules.RequiredKills}";
                }
                else if (director.Kills < missionRules.RequiredKills)
                {
                    ObjectiveStatus =
                        $"击落敌机 {director.Kills}/{missionRules.RequiredKills}";
                }
                else
                {
                    ObjectiveStatus = $"清理剩余敌机 {director.AliveCount}";
                }
                break;
            default:
                ObjectiveStatus = director.Kills < missionRules.RequiredKills
                    ? $"击落敌机 {director.Kills}/{missionRules.RequiredKills}"
                    : $"清理剩余敌机 {director.AliveCount}";
                break;
        }
        UpdateHudText();
    }

    void UpdateHudText()
    {
        string title = string.IsNullOrWhiteSpace(missionName)
            ? "星球战斗任务"
            : missionName;
        string combat = director != null
            ? $"敌机 {director.AliveCount}   击落 {director.Kills}"
            : boss != null
                ? "右键瞄准  ·  左键射击  ·  自动瞄准已禁用"
                : string.Empty;
        if (director != null && edpcg != null && edpcg.IsRunning)
        {
            combat =
                $"敌机 活跃 {director.AliveCount}  名单 " +
                $"{edpcg.ResolvedCount}/{edpcg.RosterCount}  " +
                $"有效击落 {edpcg.CreditedKills}/" +
                $"{edpcg.Settings.requiredCreditedKills}  " +
                $"压力 {edpcg.CurrentSample.actualPressure:P0}";
        }
        hudText = title + "\n" + ObjectiveStatus + "\n" + combat;
        if (!string.IsNullOrEmpty(bossBridgeHint) &&
            Time.unscaledTime < bossBridgeHintEndsAt)
        {
            hudText += "\n" + bossBridgeHint;
        }
    }

    void OnGUI()
    {
        if (!IsPrepared || transitionStarted ||
            (!sessionRunning && !missionCompleted))
        {
            return;
        }
        float height = Mathf.Clamp(
            GUI.skin.box.CalcHeight(new GUIContent(hudText), 360f) + 12f,
            88f,
            156f);
        GUI.Box(new Rect(18f, 18f, 360f, height), hudText);
    }

    void BeginBossBridgeHint()
    {
        bossBridgeHint = string.Empty;
        string slotId = GalaxyLaunchContext.SelectedSlotId;
        try
        {
            GalaxySaveSlotMetadata metadata =
                string.IsNullOrWhiteSpace(slotId)
                    ? GalaxySaveSlotService.GetOrCreateDevelopmentSlot()
                    : GalaxySaveSlotService.LoadMetadata(slotId);
            if (metadata == null || metadata.bossBridgeHintSeen)
                return;
            metadata.bossBridgeHintSeen = true;
            GalaxySaveSlotService.SaveMetadata(metadata);
            bossBridgeHint =
                "提示：穿过发光的狭窄连廊，可迫使大型 Boss 绕行或撞桥。";
            bossBridgeHintEndsAt = Time.unscaledTime + 8f;
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "[FinitePlanetBoss] Could not persist bridge hint: " +
                exception.Message,
                this);
        }
    }

    void HandlePlayerDestroyed()
    {
        if (!sessionRunning || transitionStarted || settlementStarted)
            return;
        BeginSettlement(CombatSettlementOutcome.Defeat);
    }

    IEnumerator ReturnToStationAfterDestruction()
    {
        yield return new WaitForSecondsRealtime(DestructionReturnDelay);
        PlanetOrbitChapterSelectionContext.Clear();
        SpaceStationFlowContext.PrepareOrbitalReturnToStation();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        SceneManager.LoadScene(StationSceneName, LoadSceneMode.Single);
        returnRoutine = null;
    }

    Vector3 ToWorld(Vector3 planPosition)
    {
        return world.FromPersistentAddress(
            new PlanarSurfaceAddress(
                planPosition.x,
                planPosition.z,
                planPosition.y));
    }

    void OnDestroy()
    {
        if (bossIntroduction != null && bossIntroduction.IsPlaying)
            bossIntroduction.Cancel(false);
        if (playerGraph != null)
            playerGraph.Destroyed -= HandlePlayerDestroyed;
        if (boss != null)
            boss.Destroyed -= HandleBossDestroyed;
        for (int index = 0; index < energyCores.Count; index++)
        {
            if (energyCores[index] != null)
                energyCores[index].Destroyed -= HandleEnergyCoreDestroyed;
        }
        director?.EndSession();
        boss?.SetCombatActive(false);
        weapons?.SetAutoAimAllowed(true);
        weapons?.SetManualAimRequiredForFire(false);
    }
}

[DisallowMultipleComponent]
public sealed class FinitePlanetObjectiveMarker : MonoBehaviour
{
    Material material;
    Transform spinner;

    public void Configure(Color color, float scale)
    {
        Shader shader = Shader.Find("Standard") ??
                        Shader.Find("Sprites/Default");
        if (shader == null)
            return;
        material = new Material(shader)
        {
            name = "RuntimeFinitePlanetObjectiveMarker",
            color = color
        };
        GameObject beacon = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        beacon.name = "Beacon";
        beacon.transform.SetParent(transform, false);
        beacon.transform.localPosition = Vector3.zero;
        beacon.transform.localScale =
            new Vector3(2.4f, 9f, 2.4f) * scale;
        DisableCollider(beacon);
        beacon.GetComponent<Renderer>().sharedMaterial = material;

        GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "TacticalMarker";
        ring.transform.SetParent(transform, false);
        ring.transform.localPosition = Vector3.up * 9f * scale;
        ring.transform.localScale =
            new Vector3(7f, 0.35f, 7f) * scale;
        DisableCollider(ring);
        ring.GetComponent<Renderer>().sharedMaterial = material;
        spinner = ring.transform;
    }

    public void MarkComplete()
    {
        if (material != null)
            material.color = new Color(0.18f, 0.55f, 0.42f, 1f);
        if (spinner != null)
            spinner.localScale *= 0.72f;
    }

    void Update()
    {
        if (spinner != null)
            spinner.Rotate(Vector3.up, 42f * Time.deltaTime, Space.Self);
    }

    static void DisableCollider(GameObject value)
    {
        Collider collider = value != null
            ? value.GetComponent<Collider>()
            : null;
        if (collider != null)
            collider.enabled = false;
    }

    void OnDestroy()
    {
        if (material != null)
            Destroy(material);
    }
}

[DisallowMultipleComponent]
public sealed class FinitePlanetEnergyCoreObjective :
    MonoBehaviour,
    ISpaceDamageable
{
    SphereCollider hitCollider;
    FinitePlanetObjectiveMarker marker;
    float integrity;
    float maximumIntegrity;

    public event Action<FinitePlanetEnergyCoreObjective> Destroyed;

    public int ObjectiveIndex { get; private set; }
    public float Integrity => integrity;
    public float MaximumIntegrity => maximumIntegrity;
    public bool IsDestroyed { get; private set; }

    public void Configure(float health, int objectiveIndex)
    {
        maximumIntegrity = Mathf.Max(1f, health);
        integrity = maximumIntegrity;
        ObjectiveIndex = objectiveIndex;
        IsDestroyed = false;
        hitCollider = gameObject.AddComponent<SphereCollider>();
        hitCollider.radius = 7f;
        VehicleCombatTeamUtility.SetTeam(
            gameObject,
            VehicleCombatTeam.Enemy);
        marker = gameObject.AddComponent<FinitePlanetObjectiveMarker>();
        marker.Configure(new Color(1f, 0.32f, 0.04f, 1f), 1.15f);
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
        integrity = Mathf.Max(0f, integrity - damage.amount);
        if (integrity > 0f)
            return;
        IsDestroyed = true;
        if (hitCollider != null)
            hitCollider.enabled = false;
        marker?.MarkComplete();
        Destroyed?.Invoke(this);
    }
}
