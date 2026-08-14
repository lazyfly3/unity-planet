using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ModularAssembly;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityPlanet.SpaceStation.Skills;

namespace UnityPlanet.ModularAssembly
{
    public enum GridFlightSessionKind
    {
        FreeFlight,
        CombatTest
    }

    public interface IVehicleMotionCommandSource
    {
        Vector3 DesiredWorldVelocity { get; }
        Vector3 DesiredAimDirection { get; }
        bool BoostRequested { get; }
    }

    public interface IVehicleWeaponCommandSource
    {
        WeaponCommandFrame CurrentWeaponCommand { get; }
    }

    public sealed class VehicleDependencyGraph
    {
        readonly Dictionary<string, HashSet<string>> outgoing =
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        public void Clear()
        {
            outgoing.Clear();
        }

        public void AddEdge(string source, string target)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target))
                return;
            if (!outgoing.TryGetValue(source, out HashSet<string> edges))
            {
                edges = new HashSet<string>(StringComparer.Ordinal);
                outgoing[source] = edges;
            }
            edges.Add(target);
        }

        public bool CanReach(string source, string target)
        {
            if (string.Equals(source, target, StringComparison.Ordinal))
                return true;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<string>();
            queue.Enqueue(source);
            visited.Add(source);
            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                if (!outgoing.TryGetValue(current, out HashSet<string> edges))
                    continue;
                foreach (string edge in edges)
                {
                    if (string.Equals(edge, target, StringComparison.Ordinal))
                        return true;
                    if (visited.Add(edge))
                        queue.Enqueue(edge);
                }
            }
            return false;
        }
    }

    public sealed class CombatTestController : MonoBehaviour
    {
        const float BattleAltitude = 180f;
        const float EnemySpawnDistance = 120f;
        const float WarningRadius = 1200f;

        ModularAssemblyLabController lab;
        GridFlightBridge flight;
        GridAssemblyPresenter presenter;
        WeaponSystemCoordinator weapons;
        VehicleStructureGraph playerGraph;
        RobocraftMotionCoordinator playerMotion;
        Rigidbody playerBody;
        ModularBlueprintData playerSnapshot;
        EnemyAirCombatVehicle enemy;
        HordeCombatDirector hordeDirector;
        Canvas combatCanvas;
        Text statusText;
        Text playerText;
        Text enemyText;
        Text warningText;
        GameObject resultPanel;
        Text resultText;
        readonly List<Text> threatIndicators = new List<Text>(10);
        bool pendingCombat;
        bool waitingForPreparation;
        bool combatActive;
        bool resolving;
        float ineffectiveSeconds;
        Vector3 battleCenter;
        Vector3 combatPlayerSpawn;
        Quaternion combatPlayerRotation = Quaternion.identity;
        GameObject targetObject;
        ICombatArenaProvider arena;
        int combatSessionId;
        int hordeSeed;
        readonly CombatTtkTelemetry playerTtk =
            new CombatTtkTelemetry();
        readonly CombatTtkTelemetry enemyTtk =
            new CombatTtkTelemetry();

        public GridFlightSessionKind SessionKind { get; private set; } =
            GridFlightSessionKind.FreeFlight;

        public bool IsCombatActive => combatActive;
        public bool IsResolving => resolving;

        public CombatTestMode CurrentMode { get; private set; } =
            CombatTestMode.Duel;

        public void RequestExitCombat()
        {
            if (pendingCombat || combatActive || resolving ||
                SessionKind == GridFlightSessionKind.CombatTest)
            {
                ExitCombat();
            }
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (!ModularLabSceneProfile.AllowsCombatTest(
                    SceneManager.GetActiveScene()) ||
                FindObjectOfType<CombatTestController>() != null)
                return;
            new GameObject("CombatTestController")
                .AddComponent<CombatTestController>();
        }

        IEnumerator Start()
        {
            for (int frame = 0; frame < 180; frame++)
            {
                ResolveReferences();
                if (lab != null && flight != null && weapons != null)
                    break;
                yield return null;
            }
            if (lab == null || flight == null || weapons == null)
            {
                Debug.LogError(
                    "CombatTestController could not resolve the modular lab.");
                yield break;
            }

            playerGraph = weapons.StructureGraph;
            if (playerGraph != null)
            {
                playerGraph.Destroyed += HandlePlayerDestroyed;
                playerGraph.ModuleDamaged += HandlePlayerModuleDamaged;
            }
            flight.StateChanged += HandleFlightState;
            DisableLegacyTargetAi();
            BuildUi();

            CombatPreparationCoordinator preparation =
                FindObjectOfType<CombatPreparationCoordinator>(true);
            if (preparation == null)
            {
                GameObject host = new GameObject(
                    "CombatPreparationCoordinator");
                preparation =
                    host.AddComponent<CombatPreparationCoordinator>();
            }
        }

        void ResolveReferences()
        {
            lab = lab != null
                ? lab
                : FindObjectOfType<ModularAssemblyLabController>();
            flight = flight != null
                ? flight
                : FindObjectOfType<GridFlightBridge>();
            presenter = presenter != null
                ? presenter
                : FindObjectOfType<GridAssemblyPresenter>();
            weapons = weapons != null
                ? weapons
                : FindObjectOfType<WeaponSystemCoordinator>();
            playerMotion = playerMotion != null
                ? playerMotion
                : FindObjectOfType<RobocraftMotionCoordinator>();
            if (flight != null)
                playerBody = flight.GetComponent<Rigidbody>();
            GridTargetController target =
                FindObjectOfType<GridTargetController>(true);
            if (target != null)
                targetObject = target.gameObject;
        }

        void DisableLegacyTargetAi()
        {
            foreach (WeaponAirCombatAi ai in
                     FindObjectsOfType<WeaponAirCombatAi>(true))
                ai.enabled = false;
        }

        void BuildUi()
        {
            GameObject canvasObject = new GameObject(
                "CombatTestHUD",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            combatCanvas = canvasObject.GetComponent<Canvas>();
            combatCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            combatCanvas.sortingOrder = 180;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            statusText = CreateText(
                combatCanvas.transform,
                "空战测试",
                new Vector2(0f, -24f),
                new Vector2(520f, 44f),
                TextAnchor.MiddleCenter,
                24);
            statusText.rectTransform.anchorMin = new Vector2(0.5f, 1f);
            statusText.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            statusText.rectTransform.pivot = new Vector2(0.5f, 1f);

            playerText = CreateText(
                combatCanvas.transform,
                string.Empty,
                new Vector2(24f, -24f),
                new Vector2(360f, 100f),
                TextAnchor.UpperLeft,
                18);
            enemyText = CreateText(
                combatCanvas.transform,
                string.Empty,
                new Vector2(-24f, -24f),
                new Vector2(360f, 100f),
                TextAnchor.UpperRight,
                18);
            enemyText.rectTransform.anchorMin = Vector2.one;
            enemyText.rectTransform.anchorMax = Vector2.one;
            enemyText.rectTransform.pivot = Vector2.one;

            warningText = CreateText(
                combatCanvas.transform,
                string.Empty,
                new Vector2(0f, 92f),
                new Vector2(760f, 46f),
                TextAnchor.MiddleCenter,
                22);
            warningText.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            warningText.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            warningText.rectTransform.pivot = new Vector2(0.5f, 0f);
            warningText.color = new Color(1f, 0.68f, 0.2f, 1f);

            for (int index = 0; index < 10; index++)
            {
                Text indicator = CreateText(
                    combatCanvas.transform,
                    "▲",
                    Vector2.zero,
                    new Vector2(32f, 32f),
                    TextAnchor.MiddleCenter,
                    24);
                indicator.name = "HordeThreatIndicator_" + index;
                indicator.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
                indicator.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                indicator.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                indicator.gameObject.SetActive(false);
                threatIndicators.Add(indicator);
            }

            resultPanel = new GameObject(
                "CombatResult",
                typeof(RectTransform),
                typeof(Image));
            resultPanel.transform.SetParent(combatCanvas.transform, false);
            RectTransform panelRect =
                resultPanel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(520f, 260f);
            resultPanel.GetComponent<Image>().color =
                new Color(0.01f, 0.04f, 0.06f, 0.94f);
            resultText = CreateText(
                resultPanel.transform,
                string.Empty,
                new Vector2(0f, -28f),
                new Vector2(480f, 90f),
                TextAnchor.MiddleCenter,
                28);
            resultText.rectTransform.anchorMin = new Vector2(0.5f, 1f);
            resultText.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            resultText.rectTransform.pivot = new Vector2(0.5f, 1f);
            Button restart = CreateButton(
                resultPanel.transform,
                "重新挑战",
                new Vector2(-110f, 28f),
                new Vector2(200f, 52f),
                new Color(0.05f, 0.62f, 0.7f, 1f));
            RectTransform restartRect =
                restart.GetComponent<RectTransform>();
            restartRect.anchorMin = new Vector2(0.5f, 0f);
            restartRect.anchorMax = new Vector2(0.5f, 0f);
            restartRect.pivot = new Vector2(0.5f, 0f);
            restartRect.anchoredPosition = new Vector2(-110f, 28f);
            restart.onClick.AddListener(RestartCombat);
            Button exit = CreateButton(
                resultPanel.transform,
                "返回改装",
                new Vector2(110f, 28f),
                new Vector2(200f, 52f),
                new Color(0.55f, 0.18f, 0.12f, 1f));
            RectTransform exitRect =
                exit.GetComponent<RectTransform>();
            exitRect.anchorMin = new Vector2(0.5f, 0f);
            exitRect.anchorMax = new Vector2(0.5f, 0f);
            exitRect.pivot = new Vector2(0.5f, 0f);
            exitRect.anchoredPosition = new Vector2(110f, 28f);
            exit.onClick.AddListener(ExitCombat);
            resultPanel.SetActive(false);
            combatCanvas.gameObject.SetActive(false);
        }

        void Update()
        {
            if (!combatActive || resolving)
                return;
            if (CurrentMode == CombatTestMode.Horde)
                hordeDirector?.Tick(Time.deltaTime);
            UpdateCombatHud();
            UpdateThreatIndicators();
            CheckBattleOutcome();
        }

        bool CanStart(out string reason)
        {
            reason = string.Empty;
            if (lab == null || lab.Model == null)
            {
                reason = "实验场尚未初始化";
                return false;
            }
            GridAssemblyValidation validation = lab.Model.Validate();
            if (!validation.IsValid)
            {
                reason = validation.Message;
                return false;
            }
            int weaponCount = CountPlayerWeapons();
            if (weaponCount <= 0)
            {
                reason = "至少安装一件武器";
                return false;
            }
            if (playerMotion != null &&
                playerMotion.Telemetry.hoverRatio < 0.35f &&
                playerMotion.CoreAssistMode !=
                VehicleCoreAssistMode.Training)
            {
                reason = "缺少持续飞行能力";
                return false;
            }
            return true;
        }

        int CountPlayerWeapons()
        {
            if (lab?.Model == null)
                return 0;
            return lab.Model.Records.Count(item =>
                item?.Definition != null &&
                item.Definition.Category == GridModuleCategory.KineticWeapon);
        }

        public bool TryBeginCombat(out string message)
        {
            return TryBeginCombat(CombatTestMode.Duel, out message);
        }

        public bool TryBeginCombat(
            CombatTestMode mode,
            out string message)
        {
            message = string.Empty;
            if (pendingCombat || combatActive || waitingForPreparation)
            {
                message = "战斗测试已经在准备或进行中。";
                return false;
            }
            if (!CanStart(out message))
            {
                if (warningText != null)
                    warningText.text = message;
                return false;
            }
            if (lab.FlightState != GridFlightState.Build)
            {
                message = "请先返回改装状态，再开始战斗测试。";
                if (warningText != null)
                    warningText.text = message;
                return false;
            }
            CombatPreparationCoordinator preparation =
                FindObjectOfType<CombatPreparationCoordinator>(true);
            if (preparation != null && !preparation.IsReady(mode))
            {
                waitingForPreparation = true;
                preparation.EnsureReady(
                    mode,
                    (success, preparationMessage) =>
                    {
                        waitingForPreparation = false;
                        if (success)
                        {
                            if (!TryBeginCombat(
                                    mode,
                                    out string retryMessage) &&
                                warningText != null)
                            {
                                warningText.text = retryMessage;
                            }
                        }
                        else if (warningText != null)
                        {
                            warningText.text = preparationMessage;
                        }
                    });
                message =
                    "正在后台准备战斗资源 " +
                    Mathf.RoundToInt(preparation.Progress * 100f) +
                    "%";
                return false;
            }

            playerSnapshot = lab.Model.CaptureBlueprint();
            CurrentMode = mode;
            FlightEnvironmentManager environmentManager =
                FindObjectOfType<FlightEnvironmentManager>(true);
            if (environmentManager != null)
                environmentManager.SetCombatMode(mode);
            pendingCombat = true;
            SessionKind = GridFlightSessionKind.CombatTest;
            message = mode == CombatTestMode.Horde
                ? "正在进入割草战斗。"
                : "正在进入1v1战斗。";
            lab.ToggleFlight();
            return true;
        }

        void HandleFlightState(GridFlightState state, string message)
        {
            if (state == GridFlightState.Flight && pendingCombat)
            {
                pendingCombat = false;
                StartCoroutine(StartCombatAfterPhysicsReady());
            }
            else if (state == GridFlightState.Build &&
                     SessionKind == GridFlightSessionKind.CombatTest)
            {
                ModularBlueprintData beforeCombat = playerSnapshot;
                ModularBlueprintData result =
                    BuildPostCombatBlueprint();
                CleanupCombat(true);
                CommitPostCombatBlueprint(result, beforeCombat);
            }
        }

        IEnumerator StartCombatAfterPhysicsReady()
        {
            VehicleDetachedDebris.ClearAll();
            yield return new WaitForFixedUpdate();
            ResolveReferences();
            if (playerBody == null)
            {
                ExitCombat();
                yield break;
            }

            FlightEnvironmentManager environmentManager =
                FindObjectOfType<FlightEnvironmentManager>(true);
            arena = environmentManager != null
                ? environmentManager.ActiveArena
                : null;
            Vector3 playerSpawn = playerBody.position +
                                  Vector3.up * BattleAltitude;
            Quaternion playerRotation = Quaternion.identity;
            if (arena != null)
            {
                battleCenter = arena.BattleCenter;
                arena.TryGetPlayerSpawn(
                    out playerSpawn,
                    out playerRotation);
            }
            else
            {
                battleCenter = playerSpawn;
            }
            combatPlayerSpawn = playerSpawn;
            combatPlayerRotation = playerRotation;
            if (!playerBody.isKinematic)
            {
                playerBody.velocity = Vector3.zero;
                playerBody.angularVelocity = Vector3.zero;
            }
            playerBody.isKinematic = true;
            playerBody.position = playerSpawn;
            playerBody.rotation = playerRotation;
            if (CurrentMode == CombatTestMode.Duel)
                playerBody.isKinematic = false;

            VehicleCombatTeamUtility.SetTeam(
                playerBody.gameObject,
                VehicleCombatTeam.Player);

            playerGraph = weapons.StructureGraph;
            if (playerGraph != null)
            {
                playerGraph.SetAutomaticReturnToBuild(false);
                playerGraph.SetDamageEnabled(true);
                playerGraph.ResetCombatSession();
            }
            if (targetObject != null)
                targetObject.SetActive(false);

            if (combatCanvas != null)
                combatCanvas.gameObject.SetActive(true);
            if (resultPanel != null)
                resultPanel.SetActive(false);

            combatSessionId++;
            if (CurrentMode == CombatTestMode.Horde)
            {
                EnsureHordeDirector();
                statusText.text = "割草战斗  正在规划战区航路";
                warningText.text = "玩家载具保持冻结，航路完成后开始计时";
                float warningRadius = arena != null
                    ? arena.WarningRadius
                    : WarningRadius;
                yield return hordeDirector.PrepareNavigation(
                    playerBody,
                    battleCenter,
                    warningRadius,
                    (progress, navigationMessage) =>
                    {
                        if (statusText != null)
                        {
                            statusText.text =
                                $"割草战斗  规划战区航路 {progress * 100f:0}%";
                        }
                        if (warningText != null)
                            warningText.text = navigationMessage;
                    });
                if (!hordeDirector.NavigationReady)
                {
                    combatActive = true;
                    resolving = false;
                    CompleteBattle(
                        false,
                        string.IsNullOrWhiteSpace(hordeDirector.NavigationError)
                            ? "战区航路准备失败"
                            : hordeDirector.NavigationError);
                    yield break;
                }
                playerBody.isKinematic = false;
                playerBody.WakeUp();
                hordeSeed = StableHordeSeed(
                    SceneManager.GetActiveScene().name,
                    battleCenter);
                hordeDirector.BeginSession(
                    playerBody,
                    battleCenter,
                    warningRadius,
                    hordeSeed,
                    combatSessionId);
                warningText.text = string.Empty;
            }
            else
            {
                SpawnEnemy();
            }
            ineffectiveSeconds = 0f;
            playerTtk.Reset("player");
            enemyTtk.Reset(
                CurrentMode == CombatTestMode.Horde
                    ? "horde"
                    : "enemy");
            combatActive = true;
            resolving = false;
        }

        void SpawnEnemy()
        {
            Vector3 position =
                battleCenter + Vector3.forward * EnemySpawnDistance +
                Vector3.up * 20f;
            Quaternion rotation = Quaternion.LookRotation(
                battleCenter - position,
                Vector3.up);
            if (arena != null)
                arena.TryGetEnemySpawn(out position, out rotation);

            if (enemy == null)
            {
                GameObject root =
                    new GameObject("StandardAirCombatEnemy");
                root.transform.SetPositionAndRotation(position, rotation);
                enemy = root.AddComponent<EnemyAirCombatVehicle>();
                WeaponVisualPool pool =
                    weapons.GetComponent<WeaponVisualPool>();
                WeaponProjectilePool projectilePool =
                    weapons.GetComponent<WeaponProjectilePool>();
                enemy.Initialize(
                    this,
                    playerGraph,
                    playerBody,
                    pool,
                    projectilePool);
                VehicleCombatTeamUtility.SetTeam(
                    root,
                    VehicleCombatTeam.Enemy);
            }
            else
            {
                VehicleCombatTeamUtility.SetTeam(
                    enemy.gameObject,
                    VehicleCombatTeam.Enemy);
                enemy.gameObject.SetActive(true);
                enemy.ResetForCombat(
                    this,
                    playerGraph,
                    playerBody,
                    position,
                    rotation);
            }
        }

        public void PrepareEnemyPool()
        {
            if (enemy != null || weapons == null)
                return;
            GameObject root =
                new GameObject("StandardAirCombatEnemy_Prewarmed");
            root.transform.position = new Vector3(0f, -10000f, 0f);
            enemy = root.AddComponent<EnemyAirCombatVehicle>();
            enemy.Initialize(
                this,
                playerGraph,
                playerBody,
                weapons.GetComponent<WeaponVisualPool>(),
                weapons.GetComponent<WeaponProjectilePool>());
            VehicleCombatTeamUtility.SetTeam(
                root,
                VehicleCombatTeam.Enemy);
            enemy.StopCombat();
            root.SetActive(false);
        }

        public IEnumerator PrepareModeResources(
            CombatTestMode mode,
            Action<float, string> progress)
        {
            PrepareEnemyPool();
            progress?.Invoke(0.25f, "1v1敌机池已就绪");
            if (mode != CombatTestMode.Horde)
                yield break;
            EnsureHordeDirector();
            yield return hordeDirector.Prewarm(
                this,
                weapons != null
                    ? weapons.GetComponent<WeaponVisualPool>()
                    : null,
                weapons != null
                    ? weapons.GetComponent<WeaponProjectilePool>()
                    : null,
                (value, text) => progress?.Invoke(
                    Mathf.Lerp(0.25f, 1f, value),
                    text));
        }

        public bool IsModePrepared(
            CombatTestMode mode,
            out string error)
        {
            error = string.Empty;
            if (mode == CombatTestMode.Duel)
                return enemy != null;
            EnsureHordeDirector();
            if (hordeDirector.PreparationValid)
                return true;
            error = hordeDirector.PreparationError;
            return false;
        }

        void EnsureHordeDirector()
        {
            if (hordeDirector == null)
            {
                hordeDirector =
                    GetComponent<HordeCombatDirector>() ??
                    gameObject.AddComponent<HordeCombatDirector>();
            }
        }

        public void RecordPlayerDamage(float amount)
        {
            playerTtk.RecordDamage(amount);
        }

        public void RecordEnemyDamage(float amount)
        {
            enemyTtk.RecordDamage(amount);
        }

        void HandlePlayerModuleDamaged(
            VehicleModuleDamageFeedback feedback)
        {
            if (combatActive)
                RecordPlayerDamage(feedback.DamageAmount);
        }

        void CheckBattleOutcome()
        {
            if (playerGraph == null || !playerGraph.Active)
            {
                CompleteBattle(false, "载具核心被摧毁");
                return;
            }
            if (CurrentMode == CombatTestMode.Duel &&
                (enemy == null || !enemy.IsCombatCapable))
            {
                CompleteBattle(true, "敌机失去战斗能力");
                return;
            }
            if (CurrentMode == CombatTestMode.Horde)
            {
                if (hordeDirector == null || !hordeDirector.IsRunning)
                {
                    CompleteBattle(false, "割草战斗导演意外停止");
                    return;
                }
                if (hordeDirector.IsFinished)
                {
                    CompleteBattle(true, "计时结束，战区已清理");
                    return;
                }
            }

            float warningRadius =
                arena != null ? arena.WarningRadius : WarningRadius;
            float distance = Vector3.Distance(
                playerBody.position,
                battleCenter);
            if (distance > warningRadius)
            {
                warningText.text =
                    "已偏离主要交战区；战斗继续，请沿目标指示返回城市航路";
            }
            else
            {
                warningText.text = string.Empty;
            }

            bool ineffective =
                playerGraph.ConnectedCpuRatio < 0.2f ||
                CountPlayerWeapons() <= 0 ||
                playerBody.position.y < battleCenter.y - 140f;
            ineffectiveSeconds = ineffective
                ? ineffectiveSeconds + Time.deltaTime
                : 0f;
            if (ineffectiveSeconds >= 5f)
                CompleteBattle(false, "载具失去持续战斗能力");

        }

        void UpdateCombatHud()
        {
            if (playerGraph != null)
            {
                playerText.text =
                    $"玩家  连接CPU {playerGraph.ConnectedCpuRatio * 100f:0}%\n" +
                    $"状态  {StateText(playerGraph.CapabilityState)}\n" +
                    $"武器  {CountPlayerWeapons()}";
            }
            if (CurrentMode == CombatTestMode.Duel && enemy != null)
            {
                enemyText.text =
                    $"敌机  连接CPU {enemy.ConnectedRatio * 100f:0}%\n" +
                    $"状态  {StateText(enemy.CapabilityState)}\n" +
                    $"武器  {enemy.ActiveWeaponCount}";
            }
            if (CurrentMode == CombatTestMode.Horde && hordeDirector != null)
            {
                string phase = hordeDirector.IsFinalClear
                    ? "最终清场"
                    : "阶段 " + hordeDirector.CurrentPhase;
                int seconds = Mathf.CeilToInt(hordeDirector.RemainingSeconds);
                statusText.text =
                    $"割草战斗  {phase}  {seconds / 60:00}:{seconds % 60:00}";
                enemyText.text =
                    $"敌机  {hordeDirector.AliveCount}/{hordeDirector.CurrentActiveCap}\n" +
                    $"击落  {hordeDirector.Kills}\n" +
                    $"等待  {hordeDirector.QueuedCount}";
            }
            else
            {
                statusText.text = "空战测试  1 VS 1";
            }
        }

        static string StateText(CombatCapabilityState state)
        {
            switch (state)
            {
                case CombatCapabilityState.Degraded:
                    return "性能受损";
                case CombatCapabilityState.Critical:
                    return "严重受损";
                case CombatCapabilityState.Ineffective:
                    return "失去战斗能力";
                default:
                    return "作战正常";
            }
        }

        void HandlePlayerDestroyed()
        {
            if (combatActive)
                CompleteBattle(false, "玩家载具被摧毁");
        }

        public void NotifyEnemyDestroyed(string reason)
        {
            if (combatActive && CurrentMode == CombatTestMode.Duel)
                CompleteBattle(true, reason);
        }

        void CompleteBattle(bool playerWon, string reason)
        {
            if (resolving)
                return;
            resolving = true;
            combatActive = false;
            if (playerWon)
                enemyTtk.Complete(reason);
            else
                playerTtk.Complete(reason);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (playerBody != null)
            {
                if (!playerBody.isKinematic)
                {
                    playerBody.velocity = Vector3.zero;
                    playerBody.angularVelocity = Vector3.zero;
                }
                playerBody.isKinematic = true;
            }
            if (CurrentMode == CombatTestMode.Horde)
                hordeDirector?.EndSession();
            else
                enemy?.StopCombat();
            if (resultPanel != null)
            {
                resultPanel.SetActive(true);
                if (CurrentMode == CombatTestMode.Horde &&
                    hordeDirector != null)
                {
                    resultText.fontSize = 22;
                    resultText.rectTransform.sizeDelta =
                        new Vector2(480f, 150f);
                    int moduleLosses = playerGraph != null
                        ? playerGraph.CaptureUnavailableRuntimeIds().Count
                        : 0;
                    resultText.text =
                        (playerWon ? "战斗胜利" : "战斗失败") +
                        "\n" + reason +
                        $"\n击落 {hordeDirector.Kills}  " +
                        $"受伤 {playerTtk.accumulatedEffectiveDamage:0}  " +
                        $"损失模块 {moduleLosses}\n" +
                        $"峰值敌机 {hordeDirector.PeakActive}  " +
                        $"种子 {hordeSeed}";
                }
                else
                {
                    resultText.fontSize = 28;
                    resultText.rectTransform.sizeDelta =
                        new Vector2(480f, 90f);
                    resultText.text =
                        (playerWon ? "战斗胜利" : "战斗失败") +
                        "\n" + reason;
                }
            }
            warningText.text = string.Empty;
            HideThreatIndicators();
        }

        void RestartCombat()
        {
            if (playerSnapshot == null || lab?.Model == null)
                return;
            StartCoroutine(RestartRoutine());
        }

        IEnumerator RestartRoutine()
        {
            resolving = true;
            VehicleDetachedDebris.ClearAll();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            DestroyEnemy();
            playerGraph?.EndFlight();
            presenter?.ClearVisuals();
            lab.Model.RestoreBlueprint(playerSnapshot, out string ignored);
            presenter?.ForceRebuildFromModel();
            yield return null;
            yield return new WaitForFixedUpdate();
            playerGraph?.BeginFlight();
            if (!playerBody.isKinematic)
            {
                playerBody.velocity = Vector3.zero;
                playerBody.angularVelocity = Vector3.zero;
            }
            playerBody.isKinematic = true;
            playerBody.position = CurrentMode == CombatTestMode.Horde
                ? combatPlayerSpawn
                : battleCenter;
            playerBody.rotation = CurrentMode == CombatTestMode.Horde
                ? combatPlayerRotation
                : Quaternion.identity;
            playerBody.isKinematic = false;
            combatSessionId++;
            if (CurrentMode == CombatTestMode.Horde)
            {
                hordeDirector.BeginSession(
                    playerBody,
                    battleCenter,
                    arena != null ? arena.WarningRadius : WarningRadius,
                    hordeSeed,
                    combatSessionId);
            }
            else
            {
                SpawnEnemy();
            }
            ineffectiveSeconds = 0f;
            combatActive = true;
            resolving = false;
            resultPanel.SetActive(false);
        }

        void ExitCombat()
        {
            ModularBlueprintData beforeCombat = playerSnapshot;
            ModularBlueprintData result =
                BuildPostCombatBlueprint();
            CleanupCombat(false);
            if (flight != null &&
                flight.State != GridFlightState.Build)
                flight.ExitFlight();
            CommitPostCombatBlueprint(result, beforeCombat);
        }

        ModularBlueprintData BuildPostCombatBlueprint()
        {
            if (playerSnapshot == null)
                return null;
            ModularBlueprintData result =
                JsonUtility.FromJson<ModularBlueprintData>(
                    JsonUtility.ToJson(playerSnapshot));
            if (result == null || result.modules == null)
                return result;
            HashSet<string> unavailable =
                playerGraph != null
                    ? playerGraph.CaptureUnavailableRuntimeIds()
                    : new HashSet<string>(StringComparer.Ordinal);
            unavailable.Remove(GridAssemblyModel.CoreRuntimeId);
            result.modules = result.modules
                .Where(module =>
                    module != null &&
                    (module.runtimeId == GridAssemblyModel.CoreRuntimeId ||
                     !unavailable.Contains(module.runtimeId)))
                .ToArray();
            result.savedUtcTicks = DateTime.UtcNow.Ticks;
            return result;
        }

        void CommitPostCombatBlueprint(
            ModularBlueprintData result,
            ModularBlueprintData beforeCombat)
        {
            if (lab?.Model == null || result == null)
                return;
            presenter?.ClearVisuals();
            if (!lab.Model.RestoreBlueprint(
                    result,
                    out string error))
            {
                Debug.LogError(
                    "Failed to commit post-combat blueprint: " + error);
                return;
            }
            GridAssemblyValidation validation = lab.Model.Validate();
            if (validation.DisconnectedIds.Count > 0)
            {
                lab.Model.RemoveIds(
                    validation.DisconnectedIds.ToArray());
            }
            GridAssemblyValidation finalValidation =
                lab.Model.Validate();
            if (finalValidation.DisconnectedIds.Count > 0)
            {
                Debug.LogError(
                    "Post-combat blueprint still contains disconnected " +
                    "modules: " +
                    string.Join(
                        ", ",
                        finalValidation.DisconnectedIds));
            }
            presenter?.ForceRebuildFromModel();
            lab.FinalizePostCombatBuild(beforeCombat);
        }

        void CleanupCombat(bool flightAlreadyExited)
        {
            VehicleDetachedDebris.ClearAll();
            CombatTransientRoot.ClearVisuals();
            DestroyEnemy();
            combatActive = false;
            pendingCombat = false;
            resolving = false;
            SessionKind = GridFlightSessionKind.FreeFlight;
            VehicleCombatTeamMarker playerTeam = playerBody != null
                ? playerBody.GetComponent<VehicleCombatTeamMarker>()
                : null;
            if (playerTeam != null)
                playerTeam.Team = VehicleCombatTeam.Neutral;
            if (playerGraph != null)
            {
                playerGraph.SetAutomaticReturnToBuild(true);
                playerGraph.SetDamageEnabled(false);
                playerGraph.EndFlight();
            }
            if (targetObject != null)
                targetObject.SetActive(true);
            if (playerBody != null &&
                playerBody.isKinematic &&
                !flightAlreadyExited)
            {
                playerBody.isKinematic = false;
            }
            if (combatCanvas != null)
                combatCanvas.gameObject.SetActive(false);
            HideThreatIndicators();
            playerSnapshot = null;
            CurrentMode = CombatTestMode.Duel;
        }

        void DestroyEnemy()
        {
            hordeDirector?.EndSession();
            if (enemy != null)
            {
                enemy.StopCombat();
                enemy.gameObject.SetActive(false);
            }
        }

        void UpdateThreatIndicators()
        {
            if (CurrentMode != CombatTestMode.Horde ||
                hordeDirector == null || combatCanvas == null)
            {
                HideThreatIndicators();
                return;
            }
            Camera camera = Camera.main;
            RectTransform canvasRect =
                combatCanvas.GetComponent<RectTransform>();
            if (camera == null || canvasRect == null)
            {
                HideThreatIndicators();
                return;
            }
            int alive = Mathf.Min(
                threatIndicators.Count,
                hordeDirector.AliveCount);
            bool arrivalWarning = hordeDirector.TryGetArrivalWarning(
                out Vector3 arrivalPosition);
            for (int index = 0; index < threatIndicators.Count; index++)
            {
                Text indicator = threatIndicators[index];
                HordeEnemyVehicle target = index < alive
                    ? hordeDirector.GetAliveEnemy(index)
                    : null;
                bool arrival = target == null &&
                               arrivalWarning &&
                               index == alive;
                if (target == null && !arrival)
                {
                    indicator.gameObject.SetActive(false);
                    continue;
                }
                Vector3 screen = camera.WorldToScreenPoint(
                    arrival ? arrivalPosition : target.BodyPosition);
                bool behind = screen.z <= 0f;
                if (behind)
                {
                    screen.x = Screen.width - screen.x;
                    screen.y = Screen.height - screen.y;
                }
                bool onScreen = !behind &&
                                screen.x >= 24f && screen.x <= Screen.width - 24f &&
                                screen.y >= 120f && screen.y <= Screen.height - 120f;
                if (onScreen)
                {
                    indicator.gameObject.SetActive(false);
                    continue;
                }
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRect,
                    screen,
                    null,
                    out Vector2 local);
                Rect rect = canvasRect.rect;
                float halfWidth = Mathf.Max(120f, rect.width * 0.5f - 42f);
                float halfHeight = Mathf.Max(160f, rect.height * 0.5f - 120f);
                Vector2 direction = local.sqrMagnitude > 0.01f
                    ? local.normalized
                    : Vector2.up;
                float scale = Mathf.Min(
                    halfWidth / Mathf.Max(0.001f, Mathf.Abs(direction.x)),
                    halfHeight / Mathf.Max(0.001f, Mathf.Abs(direction.y)));
                Vector2 edge = direction * scale;
                if (Mathf.Abs(edge.x) < 100f && Mathf.Abs(edge.y) < 100f)
                    edge = direction * 140f;
                indicator.rectTransform.anchoredPosition = edge;
                indicator.rectTransform.localRotation = Quaternion.Euler(
                    0f,
                    0f,
                    Mathf.Atan2(-direction.x, direction.y) * Mathf.Rad2Deg);
                indicator.color = arrival || target.IsThreatening
                    ? new Color(1f, 0.16f, 0.06f, 1f)
                    : new Color(1f, 0.68f, 0.16f, 0.92f);
                indicator.gameObject.SetActive(true);
            }
        }

        void HideThreatIndicators()
        {
            for (int index = 0; index < threatIndicators.Count; index++)
                if (threatIndicators[index] != null)
                    threatIndicators[index].gameObject.SetActive(false);
        }

        static int StableHordeSeed(string sceneName, Vector3 center)
        {
            unchecked
            {
                uint hash = 2166136261u;
                string value = sceneName ?? string.Empty;
                for (int index = 0; index < value.Length; index++)
                {
                    hash ^= value[index];
                    hash *= 16777619u;
                }
                hash ^= (uint)Mathf.RoundToInt(center.x * 10f);
                hash *= 16777619u;
                hash ^= (uint)Mathf.RoundToInt(center.y * 10f);
                hash *= 16777619u;
                hash ^= (uint)Mathf.RoundToInt(center.z * 10f);
                hash *= 16777619u;
                hash ^= 0x484F5244u;
                return (int)hash;
            }
        }

        static Button CreateButton(
            Transform parent,
            string label,
            Vector2 anchoredPosition,
            Vector2 size,
            Color color)
        {
            GameObject root = new GameObject(
                label,
                typeof(RectTransform),
                typeof(Image),
                typeof(Button));
            root.transform.SetParent(parent, false);
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.one;
            rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            root.GetComponent<Image>().color = color;
            Button button = root.GetComponent<Button>();
            Text text = CreateText(
                root.transform,
                label,
                Vector2.zero,
                size,
                TextAnchor.MiddleCenter,
                16);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = Vector2.zero;
            text.rectTransform.offsetMax = Vector2.zero;
            return button;
        }

        static Text CreateText(
            Transform parent,
            string value,
            Vector2 anchoredPosition,
            Vector2 size,
            TextAnchor alignment,
            int fontSize)
        {
            GameObject root = new GameObject(
                "Text",
                typeof(RectTransform),
                typeof(Text));
            root.transform.SetParent(parent, false);
            Text text = root.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = value;
            text.color = Color.white;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.raycastTarget = false;
            RectTransform rect = text.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            return text;
        }

        void OnDestroy()
        {
            hordeDirector?.EndSession();
            if (flight != null)
                flight.StateChanged -= HandleFlightState;
            if (playerGraph != null)
            {
                playerGraph.Destroyed -= HandlePlayerDestroyed;
                playerGraph.ModuleDamaged -= HandlePlayerModuleDamaged;
            }
        }
    }

    public enum EnemyDelayedAttackState
    {
        Tracking,
        Telegraph,
        Fired,
        Recovering
    }

    public sealed class EnemyAirCombatVehicle :
        MonoBehaviour,
        IVehicleMotionCommandSource,
        IVehicleWeaponCommandSource,
        IVehicleModuleDamageAuthority
    {
        enum ModuleRole
        {
            Core,
            Structure,
            Wing,
            Thruster,
            Weapon,
            Energy
        }

        sealed class Node
        {
            public string Id;
            public Vector3Int Cell;
            public ModuleRole Role;
            public GameObject Object;
            public Renderer Renderer;
            public float Health;
            public float MaximumHealth;
            public float Mass;
            public int Cpu;
            public bool Destroyed;
            public readonly HashSet<string> Edges =
                new HashSet<string>(StringComparer.Ordinal);

            public float FunctionScale =>
                Destroyed ? 0f : 1f;
        }

        readonly Dictionary<string, Node> nodes =
            new Dictionary<string, Node>(StringComparer.Ordinal);
        readonly Dictionary<Vector3Int, string> occupancy =
            new Dictionary<Vector3Int, string>();
        readonly VehicleDependencyGraph dependencyGraph =
            new VehicleDependencyGraph();
        readonly RaycastHit[] obstacleHits = new RaycastHit[12];
        readonly VehicleForceLedger forceLedger =
            new VehicleForceLedger();
        CombatTestController owner;
        VehicleStructureGraph playerGraph;
        Rigidbody playerBody;
        WeaponVisualPool visuals;
        WeaponProjectilePool projectiles;
        Rigidbody body;
        Material coreMaterial;
        Material structureMaterial;
        Material wingMaterial;
        Material thrusterMaterial;
        Material weaponMaterial;
        Material energyMaterial;
        GameObject fleetVisual;
        float initialCpu;
        float currentCpu;
        float movementScale;
        float nextDecision;
        float nextShot;
        float attackStateUntil;
        float aimErrorYaw;
        float aimErrorPitch;
        float orbitDirection;
        bool stopped;
        string coreId;
        Vector3 desiredWorldVelocity;
        Vector3 desiredAimDirection;
        Vector3 telegraphAimPoint;
        Vector3 lockedAttackPoint;
        WeaponCommandFrame weaponCommand;
        EnemyDelayedAttackState delayedAttackState;
        LineRenderer telegraphLine;
        AudioSource telegraphAudio;
        static AudioClip telegraphClip;
        float thrusterThrottle;
        float initialThrusterCapacity;
        float initialLeftWingCapacity;
        float initialRightWingCapacity;
        float targetPropulsionIntegrity = 1f;
        float targetLeftWingIntegrity = 1f;
        float targetRightWingIntegrity = 1f;
        float propulsionIntegrity = 1f;
        float leftWingIntegrity = 1f;
        float rightWingIntegrity = 1f;
        float capabilityLostAt = -1f;

        const float ThrusterMaximumForce = 120000f;
        const float WingArea = 1f;
        const float WingIncidenceRadians = 0.19f;
        const float WingLiftSlope = 4.2f;
        const float WingZeroLiftDrag = 0.035f;
        const float WingInducedDrag = 0.08f;
        const float DamageBlendSeconds = 0.55f;
        const float MaximumDamageBankDegrees = 22f;
        const float MaximumDamageYawDegrees = 6f;
        const float AttackProjectileSpeed = 220f;
        const float TelegraphSeconds = 0.75f;
        const float FiredSeconds = 1.2f;
        const float RecoverSeconds = 0.8f;

        public Vector3 DesiredWorldVelocity => desiredWorldVelocity;
        public Vector3 DesiredAimDirection => desiredAimDirection;
        public bool BoostRequested =>
            playerBody != null &&
            Vector3.Distance(body.position, playerBody.position) > 150f;
        public WeaponCommandFrame CurrentWeaponCommand => weaponCommand;
        public float ConnectedRatio =>
            initialCpu <= 0f ? 0f : Mathf.Clamp01(currentCpu / initialCpu);
        public int ActiveWeaponCount =>
            nodes.Values.Count(item =>
                item.Role == ModuleRole.Weapon &&
                IsFunctional(item));
        public CombatCapabilityState CapabilityState
        {
            get
            {
                if (!IsCombatCapable)
                    return CombatCapabilityState.Ineffective;
                if (ConnectedRatio < 0.35f)
                    return CombatCapabilityState.Critical;
                if (ConnectedRatio < 0.7f || movementScale < 0.65f)
                    return CombatCapabilityState.Degraded;
                return CombatCapabilityState.Operational;
            }
        }
        public bool IsCombatCapable
        {
            get
            {
                if (stopped)
                    return false;
                if (nodes.TryGetValue(coreId, out Node core) &&
                    core.Destroyed)
                    return false;
                bool capable =
                    ConnectedRatio >= 0.2f &&
                    ActiveWeaponCount > 0 &&
                    movementScale > 0.15f;
                if (capable)
                {
                    capabilityLostAt = -1f;
                    return true;
                }
                if (capabilityLostAt < 0f)
                    capabilityLostAt = Time.time;
                return Time.time - capabilityLostAt <
                       CombatBalanceRuntime.Profile
                           .capabilityLossConfirmation;
            }
        }

        public void Initialize(
            CombatTestController session,
            VehicleStructureGraph targetGraph,
            Rigidbody targetBody,
            WeaponVisualPool effectPool,
            WeaponProjectilePool projectilePool)
        {
            owner = session;
            playerGraph = targetGraph;
            playerBody = targetBody;
            visuals = effectPool;
            projectiles = projectilePool;
            projectiles?.Prewarm(32);
            body = gameObject.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.drag = 0f;
            body.angularDrag = 0f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode =
                CollisionDetectionMode.ContinuousDynamic;
            body.solverIterations = 8;
            body.solverVelocityIterations = 3;
            body.maxAngularVelocity = 12f;
            orbitDirection = UnityEngine.Random.value < 0.5f ? -1f : 1f;
            CreateMaterials();
            BuildStandardEnemy();
            InstallFleetVisual();
            CaptureInitialArcadeCapabilities();
            RebuildGraphsAndMass();
            initialCpu = currentCpu;
            EnsureTelegraphFeedback();
            ResetDelayedAttack(0.35f);
            Vector3 horizontalForward =
                Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (horizontalForward.sqrMagnitude < 0.001f)
                horizontalForward = Vector3.forward;
            body.velocity = horizontalForward.normalized * 55f;
            body.angularVelocity = Vector3.zero;
        }

        public void ResetForCombat(
            CombatTestController session,
            VehicleStructureGraph targetGraph,
            Rigidbody targetBody,
            Vector3 position,
            Quaternion rotation)
        {
            owner = session;
            playerGraph = targetGraph;
            playerBody = targetBody;
            stopped = false;
            capabilityLostAt = -1f;
            nextDecision = 0f;
            nextShot = 0f;
            desiredWorldVelocity = Vector3.zero;
            desiredAimDirection = rotation * Vector3.forward;
            weaponCommand = default;
            EnsureTelegraphFeedback();
            ResetDelayedAttack(0.35f);
            foreach (Node node in nodes.Values)
            {
                node.Destroyed = false;
                node.Health = node.MaximumHealth;
                if (node.Object != null)
                    node.Object.SetActive(true);
            }
            if (fleetVisual != null)
                fleetVisual.SetActive(true);
            transform.SetPositionAndRotation(position, rotation);
            RebuildGraphsAndMass();
            if (body != null)
            {
                body.isKinematic = true;
                body.position = position;
                body.rotation = rotation;
                body.isKinematic = false;
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.WakeUp();
            }
        }

        void CreateMaterials()
        {
            Shader shader =
                Shader.Find("Universal Render Pipeline/Lit") ??
                Shader.Find("Standard");
            coreMaterial = MakeMaterial(shader, new Color(0.95f, 0.38f, 0.08f));
            structureMaterial = MakeMaterial(shader, new Color(0.28f, 0.08f, 0.06f));
            wingMaterial = MakeMaterial(shader, new Color(0.55f, 0.12f, 0.08f));
            thrusterMaterial = MakeMaterial(shader, new Color(0.1f, 0.5f, 0.72f));
            weaponMaterial = MakeMaterial(shader, new Color(0.75f, 0.72f, 0.65f));
            energyMaterial = MakeMaterial(shader, new Color(0.95f, 0.62f, 0.08f));
        }

        static Material MakeMaterial(Shader shader, Color color)
        {
            var material = new Material(shader);
            material.color = color;
            return material;
        }

        void BuildStandardEnemy()
        {
            AddNode(Vector3Int.zero, ModuleRole.Core, "Core");
            for (int z = -5; z <= 5; z++)
                AddNode(new Vector3Int(0, 0, z), ModuleRole.Structure, "Spine");
            for (int z = -3; z <= 4; z++)
            {
                AddNode(new Vector3Int(-1, 0, z), ModuleRole.Structure, "HullL");
                AddNode(new Vector3Int(1, 0, z), ModuleRole.Structure, "HullR");
            }
            foreach (int z in new[] { -2, 2 })
            for (int x = 2; x <= 4; x++)
            {
                AddNode(new Vector3Int(-x, 0, z), ModuleRole.Wing, "WingL");
                AddNode(new Vector3Int(x, 0, z), ModuleRole.Wing, "WingR");
            }
            AddNode(new Vector3Int(-1, 1, -3), ModuleRole.Thruster, "ThrusterL");
            AddNode(new Vector3Int(1, 1, -3), ModuleRole.Thruster, "ThrusterR");
            AddNode(new Vector3Int(0, 1, -4), ModuleRole.Thruster, "ThrusterC");
            AddNode(new Vector3Int(-1, 0, 5), ModuleRole.Weapon, "MachinegunL");
            AddNode(new Vector3Int(1, 0, 5), ModuleRole.Weapon, "MachinegunR");
            AddNode(new Vector3Int(-2, 0, 4), ModuleRole.Weapon, "MissileL");
            AddNode(new Vector3Int(2, 0, 4), ModuleRole.Weapon, "MissileR");
            AddNode(new Vector3Int(-1, 1, -1), ModuleRole.Energy, "EnergyL");
            AddNode(new Vector3Int(1, 1, -1), ModuleRole.Energy, "EnergyR");
        }

        void InstallFleetVisual()
        {
            fleetVisual = EnemyFleetVisualLibrary.Create(
                transform,
                "hull.sf_modular_pirate",
                "EnemyFleetVisual_Duel",
                12f);
            if (fleetVisual == null)
                return;

            // The hidden module renderers still retain their individual
            // colliders, damage receivers, topology and mass contribution.
            foreach (Node node in nodes.Values)
            {
                if (node.Renderer != null)
                    node.Renderer.enabled = false;
            }
        }

        void AddNode(
            Vector3Int cell,
            ModuleRole role,
            string prefix)
        {
            if (occupancy.ContainsKey(cell))
            {
                if (cell == Vector3Int.zero && role == ModuleRole.Core)
                {
                    Node existing = nodes[occupancy[cell]];
                    existing.Role = ModuleRole.Core;
                    coreId = existing.Id;
                }
                return;
            }

            string id = prefix + "_" + nodes.Count;
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = id;
            part.transform.SetParent(transform, false);
            part.transform.localPosition = cell;
            part.transform.localScale = Vector3.one;
            Renderer renderer = part.GetComponent<Renderer>();
            renderer.sharedMaterial = MaterialFor(role);
            Node node = new Node
            {
                Id = id,
                Cell = cell,
                Role = role,
                Object = part,
                Renderer = renderer,
                MaximumHealth = HealthFor(role),
                Health = HealthFor(role),
                Mass = MassFor(role),
                Cpu = CpuFor(role)
            };
            nodes[id] = node;
            occupancy[cell] = id;
            if (role == ModuleRole.Core)
                coreId = id;
            part.AddComponent<VehicleModuleDamageReceiver>()
                .Initialize(this, id);
        }

        Material MaterialFor(ModuleRole role)
        {
            switch (role)
            {
                case ModuleRole.Core:
                    return coreMaterial;
                case ModuleRole.Wing:
                    return wingMaterial;
                case ModuleRole.Thruster:
                    return thrusterMaterial;
                case ModuleRole.Weapon:
                    return weaponMaterial;
                case ModuleRole.Energy:
                    return energyMaterial;
                default:
                    return structureMaterial;
            }
        }

        static float HealthFor(ModuleRole role)
        {
            switch (role)
            {
                case ModuleRole.Core:
                    return 1000f;
                case ModuleRole.Structure:
                    return 260f;
                case ModuleRole.Wing:
                    return 220f;
                case ModuleRole.Thruster:
                    return 180f;
                case ModuleRole.Weapon:
                    return 160f;
                case ModuleRole.Energy:
                    return 200f;
                default:
                    return 100f;
            }
        }

        static float MassFor(ModuleRole role)
        {
            switch (role)
            {
                case ModuleRole.Core:
                    return 1000f;
                case ModuleRole.Structure:
                    return 50f;
                case ModuleRole.Wing:
                    return 70f;
                case ModuleRole.Thruster:
                    return 180f;
                case ModuleRole.Weapon:
                    return 120f;
                case ModuleRole.Energy:
                    return 120f;
                default:
                    return 50f;
            }
        }

        static int CpuFor(ModuleRole role)
        {
            switch (role)
            {
                case ModuleRole.Core:
                    return 100;
                case ModuleRole.Structure:
                    return 10;
                case ModuleRole.Wing:
                    return 30;
                case ModuleRole.Thruster:
                    return 45;
                case ModuleRole.Weapon:
                    return 80;
                case ModuleRole.Energy:
                    return 40;
                default:
                    return 10;
            }
        }

        void RebuildGraphsAndMass(
            Vector3 damageImpulse = default,
            Vector3 damagePoint = default)
        {
            foreach (Node node in nodes.Values)
                node.Edges.Clear();
            foreach (Node node in nodes.Values)
            {
                if (node.Destroyed)
                    continue;
                foreach (Vector3Int offset in NeighborOffsets)
                {
                    if (occupancy.TryGetValue(
                            node.Cell + offset,
                            out string otherId) &&
                        nodes.TryGetValue(otherId, out Node other) &&
                        !other.Destroyed)
                        node.Edges.Add(otherId);
                }
            }
            HashSet<string> connected = ConnectedToCore();
            var detachedIds = new HashSet<string>(
                nodes.Values
                    .Where(item =>
                        !item.Destroyed &&
                        !connected.Contains(item.Id))
                    .Select(item => item.Id),
                StringComparer.Ordinal);
            foreach (List<Node> component in
                     BuildDetachedComponents(detachedIds))
            {
                List<DetachedDebrisPart> debrisParts = component
                    .Where(item => item.Object != null)
                    .Select(item => new DetachedDebrisPart(
                        item.Id,
                        item.Object,
                        item.Mass))
                    .ToList();
                GameObject debris = VehicleDetachedDebris.Spawn(
                    debrisParts,
                    body,
                    damageImpulse,
                    damagePoint);
                if (debris != null)
                {
                    Renderer[] debrisRenderers =
                        debris.GetComponentsInChildren<Renderer>(true);
                    Bounds debrisBounds = debrisRenderers.Length > 0
                        ? debrisRenderers[0].bounds
                        : new Bounds(debris.transform.position, Vector3.one);
                    for (int index = 1;
                         index < debrisRenderers.Length;
                         index++)
                        debrisBounds.Encapsulate(
                            debrisRenderers[index].bounds);
                    NeoXCombatFeedbackRuntime.TrySpawnDetached(
                        debrisBounds.center,
                        damageImpulse,
                        debrisBounds.size.magnitude);
                }
                foreach (Node node in component)
                    DestroyNode(node, true);
            }
            connected = ConnectedToCore();

            dependencyGraph.Clear();
            dependencyGraph.AddEdge("core", "control");
            bool energyAvailable = nodes.Values.Any(item =>
                item.Role == ModuleRole.Energy && !item.Destroyed &&
                connected.Contains(item.Id));
            if (energyAvailable)
                dependencyGraph.AddEdge("energy", "energy_bus");
            foreach (Node node in nodes.Values.Where(item =>
                         !item.Destroyed && connected.Contains(item.Id)))
            {
                if (node.Role == ModuleRole.Weapon)
                    dependencyGraph.AddEdge("control", "weapon:" + node.Id);
                if (node.Role == ModuleRole.Thruster ||
                    node.Role == ModuleRole.Wing)
                    dependencyGraph.AddEdge("control", "motion:" + node.Id);
            }

            currentCpu = nodes.Values
                .Where(item => !item.Destroyed && connected.Contains(item.Id))
                .Sum(item => item.Cpu);
            List<Node> massNodes = nodes.Values
                .Where(item => !item.Destroyed && connected.Contains(item.Id))
                .ToList();
            float mass = Mathf.Max(100f, massNodes.Sum(item => item.Mass));
            Vector3 weighted = Vector3.zero;
            foreach (Node node in massNodes)
                weighted += (Vector3)node.Cell * node.Mass;
            body.mass = mass;
            body.centerOfMass = weighted / mass;
            body.ResetInertiaTensor();

            List<Node> movers = massNodes.Where(item =>
                item.Role == ModuleRole.Thruster ||
                item.Role == ModuleRole.Wing).ToList();
            UpdateArcadeDamageTargets(massNodes);
            float averageWing =
                (targetLeftWingIntegrity +
                 targetRightWingIntegrity) * 0.5f;
            float mobility =
                targetPropulsionIntegrity * 0.6f +
                averageWing * 0.4f;
            movementScale = movers.Count == 0 || mobility <= 0.001f
                ? 0f
                : Mathf.Lerp(0.2f, 1f, mobility);

            if (coreId == null || !nodes.ContainsKey(coreId) ||
                nodes[coreId].Destroyed ||
                (initialCpu > 0f && currentCpu / initialCpu < 0.2f) ||
                ActiveWeaponCount <= 0 ||
                movementScale <= 0.15f)
            {
                StopCombat();
                owner?.NotifyEnemyDestroyed(
                    "敌机拓扑结构失去战斗能力");
            }
        }

        static readonly Vector3Int[] NeighborOffsets =
        {
            Vector3Int.right,
            Vector3Int.left,
            Vector3Int.up,
            Vector3Int.down,
            Vector3Int.forward,
            Vector3Int.back
        };

        HashSet<string> ConnectedToCore()
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(coreId) ||
                !nodes.TryGetValue(coreId, out Node core) ||
                core.Destroyed)
                return result;
            var queue = new Queue<string>();
            queue.Enqueue(coreId);
            result.Add(coreId);
            while (queue.Count > 0)
            {
                Node node = nodes[queue.Dequeue()];
                foreach (string edge in node.Edges)
                    if (nodes.TryGetValue(edge, out Node other) &&
                        !other.Destroyed &&
                        result.Add(edge))
                        queue.Enqueue(edge);
            }
            return result;
        }

        List<List<Node>> BuildDetachedComponents(
            HashSet<string> detachedIds)
        {
            var remaining = new HashSet<string>(
                detachedIds,
                StringComparer.Ordinal);
            var result = new List<List<Node>>();
            while (remaining.Count > 0)
            {
                string seed = remaining.First();
                remaining.Remove(seed);
                var component = new List<Node>();
                var queue = new Queue<string>();
                queue.Enqueue(seed);
                while (queue.Count > 0)
                {
                    string current = queue.Dequeue();
                    if (!nodes.TryGetValue(current, out Node node))
                        continue;
                    component.Add(node);
                    foreach (string adjacent in node.Edges)
                        if (remaining.Remove(adjacent))
                            queue.Enqueue(adjacent);
                }
                if (component.Count > 0)
                    result.Add(component);
            }
            return result;
        }

        bool IsFunctional(Node node)
        {
            return node != null &&
                   !node.Destroyed &&
                   dependencyGraph.CanReach(
                       "core",
                       node.Role == ModuleRole.Weapon
                           ? "weapon:" + node.Id
                           : "motion:" + node.Id);
        }

        public float Integrity(string id)
        {
            return nodes.TryGetValue(id, out Node node)
                ? node.Health
                : 0f;
        }

        public float MaximumIntegrity(string id)
        {
            return nodes.TryGetValue(id, out Node node)
                ? node.MaximumHealth
                : 0f;
        }

        public bool IsDestroyed(string id)
        {
            return !nodes.TryGetValue(id, out Node node) ||
                   node.Destroyed;
        }

        public void ApplyDamage(string id, SpaceDamageInfo damage)
        {
            if (stopped ||
                !nodes.TryGetValue(id, out Node node) ||
                node.Destroyed)
                return;
            node.Health = Mathf.Max(
                0f,
                node.Health - Mathf.Max(0f, damage.amount));
            owner?.RecordEnemyDamage(
                Mathf.Max(0f, damage.amount));
            if (node.Health > 0f)
                return;
            DestroyNode(
                node,
                false,
                damage.impulse,
                damage.point);
            RebuildGraphsAndMass(
                damage.impulse,
                damage.point);
        }

        void DestroyNode(
            Node node,
            bool disconnected,
            Vector3 impulse = default,
            Vector3 hitPoint = default)
        {
            if (node == null || node.Destroyed)
                return;
            if (!disconnected && node.Object != null)
            {
                Renderer[] renderers =
                    node.Object.GetComponentsInChildren<Renderer>(true);
                Bounds bounds = renderers.Length > 0
                    ? renderers[0].bounds
                    : new Bounds(node.Object.transform.position, Vector3.one);
                for (int index = 1; index < renderers.Length; index++)
                    bounds.Encapsulate(renderers[index].bounds);
                Vector3 effectPoint =
                    hitPoint.sqrMagnitude > 0.0001f
                        ? hitPoint
                        : bounds.center;
                CombatFeedbackController.GetOrCreate().PlayDestruction(
                    new ModuleDestructionFeedbackContext(
                        node.Id,
                        effectPoint,
                        impulse.sqrMagnitude > 0.0001f
                            ? -impulse.normalized
                            : Vector3.up,
                        CategoryForRole(node.Role),
                        node.Role == ModuleRole.Core,
                        bounds,
                        0));
                VehicleDetachedDebris.SpawnDirectBreak(
                    new[]
                    {
                        new DetachedDebrisPart(
                            node.Id,
                            node.Object,
                            node.Mass)
                    },
                    body,
                    impulse,
                    hitPoint);
            }
            node.Destroyed = true;
            node.Health = 0f;
            if (node.Object != null)
                node.Object.SetActive(false);
        }

        void Update()
        {
            if (stopped || playerBody == null || body == null)
                return;
            if (PlayerSkillCombatEffects.AreEnemiesFrozen)
            {
                weaponCommand = default;
                if (telegraphLine != null)
                    telegraphLine.enabled = false;
                return;
            }
            if (Time.time >= nextDecision)
            {
                nextDecision = Time.time + 0.25f;
                aimErrorYaw = UnityEngine.Random.Range(-2.5f, 2.5f);
                aimErrorPitch = UnityEngine.Random.Range(-2.5f, 2.5f);
            }
            float targetDistance =
                Vector3.Distance(body.position, playerBody.position);
            Vector3 target =
                playerBody.worldCenterOfMass +
                playerBody.velocity *
                    Mathf.Clamp(
                    targetDistance / AttackProjectileSpeed,
                    0f,
                    1.2f);
            Vector3 aimDirection = target - body.worldCenterOfMass;
            Vector3 maneuverTarget = target + Vector3.up * 18f;
            Vector3 direction =
                maneuverTarget - body.worldCenterOfMass;
            if (direction.sqrMagnitude < 0.01f)
                direction = transform.forward;
            desiredAimDirection =
                Quaternion.Euler(aimErrorPitch, aimErrorYaw, 0f) *
                (aimDirection.sqrMagnitude > 0.01f
                    ? aimDirection.normalized
                    : transform.forward);
            float distance = direction.magnitude;
            Vector3 orbit = Vector3.Cross(Vector3.up, direction.normalized) *
                            orbitDirection;
            if (distance > 145f)
                desiredWorldVelocity = direction.normalized * 55f;
            else if (distance < 50f)
                desiredWorldVelocity =
                    -direction.normalized * 35f + orbit * 20f;
            else
                desiredWorldVelocity = orbit * 42f +
                                       direction.normalized * 8f;

            UpdateDelayedAttack(target);
        }

        void UpdateDelayedAttack(Vector3 predictedTarget)
        {
            EnsureTelegraphFeedback();
            switch (delayedAttackState)
            {
                case EnemyDelayedAttackState.Tracking:
                    weaponCommand = AimCommand(predictedTarget, false);
                    if (Time.time >= attackStateUntil &&
                        ActiveWeaponCount > 0)
                        BeginTelegraph(predictedTarget);
                    break;
                case EnemyDelayedAttackState.Telegraph:
                    telegraphAimPoint = predictedTarget;
                    weaponCommand = AimCommand(telegraphAimPoint, false);
                    UpdateTelegraphLine();
                    if (Time.time >= attackStateUntil)
                        BeginFiring(telegraphAimPoint);
                    break;
                case EnemyDelayedAttackState.Fired:
                    weaponCommand = AimCommand(lockedAttackPoint, true);
                    TryFire(lockedAttackPoint);
                    if (Time.time >= attackStateUntil)
                    {
                        delayedAttackState =
                            EnemyDelayedAttackState.Recovering;
                        attackStateUntil = Time.time + RecoverSeconds;
                        weaponCommand =
                            AimCommand(predictedTarget, false);
                    }
                    break;
                default:
                    weaponCommand = AimCommand(predictedTarget, false);
                    if (Time.time >= attackStateUntil)
                    {
                        delayedAttackState =
                            EnemyDelayedAttackState.Tracking;
                        attackStateUntil = Time.time;
                    }
                    break;
            }
        }

        WeaponCommandFrame AimCommand(Vector3 point, bool fire)
        {
            return new WeaponCommandFrame
            {
                WeaponGroup = 1,
                FireHeld = fire,
                AimHeld = true,
                AimPoint = point
            };
        }

        void BeginTelegraph(Vector3 predictedTarget)
        {
            delayedAttackState = EnemyDelayedAttackState.Telegraph;
            attackStateUntil = Time.time + TelegraphSeconds;
            telegraphAimPoint = predictedTarget;
            if (telegraphLine != null)
                telegraphLine.enabled = true;
            if (telegraphAudio != null && telegraphClip != null)
            {
                telegraphAudio.clip = telegraphClip;
                telegraphAudio.Play();
            }
            UpdateTelegraphLine();
        }

        void BeginFiring(Vector3 predictedTarget)
        {
            Vector3 origin = ResolveWeaponCentroid();
            Vector3 direction = predictedTarget - origin;
            if (direction.sqrMagnitude < 0.0001f)
                direction = transform.forward;
            direction = Quaternion.Euler(
                            aimErrorPitch,
                            aimErrorYaw,
                            0f) *
                        direction.normalized;
            lockedAttackPoint =
                origin + direction * Mathf.Max(
                    1f,
                    Vector3.Distance(origin, predictedTarget));
            delayedAttackState = EnemyDelayedAttackState.Fired;
            attackStateUntil = Time.time + FiredSeconds;
            nextShot = 0f;
            if (telegraphLine != null)
                telegraphLine.enabled = false;
        }

        void ResetDelayedAttack(float trackingDelay)
        {
            delayedAttackState = EnemyDelayedAttackState.Tracking;
            attackStateUntil = Time.time + Mathf.Max(0f, trackingDelay);
            telegraphAimPoint = Vector3.zero;
            lockedAttackPoint = Vector3.zero;
            if (telegraphLine != null)
                telegraphLine.enabled = false;
            if (telegraphAudio != null)
                telegraphAudio.Stop();
        }

        void EnsureTelegraphFeedback()
        {
            if (telegraphLine == null)
            {
                GameObject lineRoot =
                    new GameObject("EnemyAttackTelegraph");
                lineRoot.transform.SetParent(
                    CombatTransientRoot.GetOrCreate(),
                    false);
                telegraphLine =
                    lineRoot.AddComponent<LineRenderer>();
                telegraphLine.useWorldSpace = true;
                telegraphLine.positionCount = 2;
                telegraphLine.textureMode = LineTextureMode.Stretch;
                telegraphLine.numCapVertices = 2;
                telegraphLine.startWidth = 0.065f;
                telegraphLine.endWidth = 0.018f;
                Shader shader =
                    Shader.Find("Sprites/Default") ??
                    Shader.Find(
                        "Universal Render Pipeline/Unlit") ??
                    Shader.Find("Unlit/Color");
                telegraphLine.sharedMaterial = new Material(shader);
                telegraphLine.enabled = false;
            }
            if (telegraphAudio == null)
            {
                AudioSource existingAudio =
                    gameObject.GetComponent<AudioSource>();
                telegraphAudio = existingAudio != null
                    ? existingAudio
                    : gameObject.AddComponent<AudioSource>();
                if (telegraphAudio == null)
                    return;
                telegraphAudio.playOnAwake = false;
                telegraphAudio.spatialBlend = 0.65f;
                telegraphAudio.volume = 0.38f;
                telegraphAudio.maxDistance = 280f;
                telegraphAudio.rolloffMode =
                    AudioRolloffMode.Linear;
            }
            if (telegraphClip == null)
                telegraphClip = CreateTelegraphClip();
        }

        void UpdateTelegraphLine()
        {
            if (telegraphLine == null ||
                delayedAttackState !=
                EnemyDelayedAttackState.Telegraph)
                return;
            float progress = 1f - Mathf.Clamp01(
                (attackStateUntil - Time.time) /
                TelegraphSeconds);
            float pulse = 0.6f +
                          Mathf.Sin(progress * Mathf.PI * 8f) * 0.4f;
            Color color = Color.Lerp(
                new Color(1f, 0.62f, 0.08f, 0.35f),
                new Color(1f, 0.08f, 0.02f, 0.92f),
                progress);
            color.a *= pulse;
            telegraphLine.startColor = color;
            telegraphLine.endColor =
                new Color(color.r, color.g, color.b, color.a * 0.08f);
            telegraphLine.SetPosition(0, ResolveWeaponCentroid());
            telegraphLine.SetPosition(1, telegraphAimPoint);
        }

        Vector3 ResolveWeaponCentroid()
        {
            Vector3 sum = Vector3.zero;
            int count = 0;
            foreach (Node node in nodes.Values)
            {
                if (node.Role != ModuleRole.Weapon ||
                    !IsFunctional(node) ||
                    node.Object == null)
                    continue;
                sum += node.Object.transform.position +
                       transform.forward * 0.7f;
                count++;
            }
            return count > 0
                ? sum / count
                : body != null
                    ? body.worldCenterOfMass
                    : transform.position;
        }

        static AudioClip CreateTelegraphClip()
        {
            const int sampleRate = 22050;
            const float duration = 0.18f;
            int sampleCount =
                Mathf.CeilToInt(sampleRate * duration);
            float[] samples = new float[sampleCount];
            float phase = 0f;
            for (int index = 0; index < sampleCount; index++)
            {
                float normalized =
                    index / (float)sampleCount;
                float frequency = Mathf.Lerp(620f, 980f, normalized);
                phase += Mathf.PI * 2f * frequency / sampleRate;
                float envelope =
                    Mathf.Sin(normalized * Mathf.PI);
                samples[index] =
                    Mathf.Sin(phase) * envelope * 0.32f;
            }
            AudioClip clip = AudioClip.Create(
                "EnemyAttackTelegraph",
                sampleCount,
                1,
                sampleRate,
                false);
            clip.SetData(samples, 0);
            return clip;
        }

        static GridModuleCategory CategoryForRole(ModuleRole role)
        {
            switch (role)
            {
                case ModuleRole.Core:
                    return GridModuleCategory.Core;
                case ModuleRole.Thruster:
                    return GridModuleCategory.MainThruster;
                case ModuleRole.Wing:
                    return GridModuleCategory.Mobility;
                case ModuleRole.Weapon:
                    return GridModuleCategory.KineticWeapon;
                case ModuleRole.Energy:
                    return GridModuleCategory.Battery;
                default:
                    return GridModuleCategory.Structure;
            }
        }

        void FixedUpdate()
        {
            if (stopped || playerBody == null || body == null ||
                body.isKinematic)
                return;

            IPlanetEnvironmentProvider provider =
                PlanetEnvironmentRuntime.Active;
            PlanetEnvironmentSample environment = provider != null
                ? provider.Sample(
                    body.worldCenterOfMass,
                    Time.fixedTimeAsDouble)
                : PlanetEnvironmentSample.EarthLike(
                    Vector3.down * 9.81f,
                    body.worldCenterOfMass.y);

            forceLedger.Begin(body);
            forceLedger.AddForce(
                body.mass * environment.gravityAcceleration);

            SmoothArcadeDamageState();
            float speedScale = Mathf.Lerp(
                0.3f,
                1f,
                Mathf.Clamp01(
                    propulsionIntegrity * 0.6f +
                    (leftWingIntegrity + rightWingIntegrity) * 0.2f));
            Vector3 commandedVelocity =
                ResolveObstacleAvoidance(
                    desiredWorldVelocity * speedScale);
            AccumulateExposedFaceDrag(environment);
            AccumulateWingAerodynamics(environment);
            AccumulateThrusterForces(
                environment,
                commandedVelocity);
            AccumulateArcadeAltitudeSupport(
                environment,
                commandedVelocity);
            AccumulateArcadeAttitudeControl(environment);
            forceLedger.Apply();
        }

        void CaptureInitialArcadeCapabilities()
        {
            initialThrusterCapacity = nodes.Values
                .Where(item => item.Role == ModuleRole.Thruster)
                .Sum(CapacityWeight);
            initialLeftWingCapacity =
                WingCapacity(nodes.Values, true);
            initialRightWingCapacity =
                WingCapacity(nodes.Values, false);
            targetPropulsionIntegrity = 1f;
            targetLeftWingIntegrity = 1f;
            targetRightWingIntegrity = 1f;
            propulsionIntegrity = 1f;
            leftWingIntegrity = 1f;
            rightWingIntegrity = 1f;
        }

        void UpdateArcadeDamageTargets(
            IEnumerable<Node> activeNodes)
        {
            List<Node> active = activeNodes
                .Where(item => item != null && !item.Destroyed)
                .ToList();
            float thrusters = active
                .Where(item => item.Role == ModuleRole.Thruster)
                .Sum(CapacityWeight);
            targetPropulsionIntegrity =
                CapacityRatio(thrusters, initialThrusterCapacity);
            targetLeftWingIntegrity = CapacityRatio(
                WingCapacity(active, true),
                initialLeftWingCapacity);
            targetRightWingIntegrity = CapacityRatio(
                WingCapacity(active, false),
                initialRightWingCapacity);
        }

        float WingCapacity(
            IEnumerable<Node> source,
            bool left)
        {
            float coreX = !string.IsNullOrEmpty(coreId) &&
                          nodes.TryGetValue(coreId, out Node core)
                ? core.Cell.x
                : 0f;
            float result = 0f;
            foreach (Node node in source)
            {
                if (node == null ||
                    node.Destroyed ||
                    node.Role != ModuleRole.Wing)
                    continue;
                float offset = node.Cell.x - coreX;
                float sideWeight = Mathf.Abs(offset) < 0.5f
                    ? 0.5f
                    : left == offset < 0f
                        ? 1f
                        : 0f;
                result += CapacityWeight(node) * sideWeight;
            }
            return result;
        }

        static float CapacityWeight(Node node)
        {
            return node == null
                ? 0f
                : Mathf.Max(1f, node.Cpu);
        }

        static float CapacityRatio(
            float current,
            float initial)
        {
            return initial <= 0.001f
                ? 1f
                : Mathf.Clamp01(current / initial);
        }

        void SmoothArcadeDamageState()
        {
            float blend = 1f - Mathf.Exp(
                -Time.fixedDeltaTime /
                Mathf.Max(0.01f, DamageBlendSeconds));
            propulsionIntegrity = Mathf.Lerp(
                propulsionIntegrity,
                targetPropulsionIntegrity,
                blend);
            leftWingIntegrity = Mathf.Lerp(
                leftWingIntegrity,
                targetLeftWingIntegrity,
                blend);
            rightWingIntegrity = Mathf.Lerp(
                rightWingIntegrity,
                targetRightWingIntegrity,
                blend);
        }

        void AccumulateArcadeAltitudeSupport(
            PlanetEnvironmentSample environment,
            Vector3 commandedVelocity)
        {
            Vector3 gravity = environment.gravityAcceleration;
            float gravityMagnitude = gravity.magnitude;
            if (gravityMagnitude <= 0.001f)
                return;

            Vector3 up = -gravity / gravityMagnitude;
            float averageWing =
                (leftWingIntegrity + rightWingIntegrity) * 0.5f;
            float liftHealth = Mathf.Clamp01(
                propulsionIntegrity * 0.7f +
                averageWing * 0.3f);
            float supportFraction =
                Mathf.Lerp(0.45f, 1f, liftHealth);
            float desiredVertical =
                Vector3.Dot(commandedVelocity, up);
            float currentVertical =
                Vector3.Dot(body.velocity, up);
            float correctionAcceleration = Mathf.Clamp(
                (desiredVertical - currentVertical) * 2.2f,
                -gravityMagnitude * 0.7f,
                gravityMagnitude * 0.7f);
            float supportAcceleration =
                gravityMagnitude * supportFraction +
                correctionAcceleration * supportFraction;
            forceLedger.AddForce(
                up * body.mass * supportAcceleration);
        }

        void AccumulateArcadeAttitudeControl(
            PlanetEnvironmentSample environment)
        {
            Vector3 gravity = environment.gravityAcceleration;
            Vector3 up = gravity.sqrMagnitude > 0.001f
                ? -gravity.normalized
                : Vector3.up;
            Vector3 aim = desiredAimDirection.sqrMagnitude > 0.001f
                ? desiredAimDirection.normalized
                : transform.forward;
            float imbalance = Mathf.Clamp(
                rightWingIntegrity - leftWingIntegrity,
                -1f,
                1f);
            Vector3 damagedAim =
                Quaternion.AngleAxis(
                    imbalance * MaximumDamageYawDegrees,
                    up) * aim;
            Vector3 desiredUp =
                Quaternion.AngleAxis(
                    -imbalance * MaximumDamageBankDegrees,
                    damagedAim) * up;

            Vector3 orientationError =
                Vector3.Cross(transform.forward, damagedAim) * 3.4f +
                Vector3.Cross(transform.up, desiredUp) * 2.2f;
            float weakerWing =
                Mathf.Min(leftWingIntegrity, rightWingIntegrity);
            float controlHealth = Mathf.Clamp01(
                propulsionIntegrity * 0.55f +
                weakerWing * 0.45f);
            Vector3 targetAngularAcceleration =
                orientationError * Mathf.Lerp(1.4f, 4.2f, controlHealth) -
                body.angularVelocity *
                Mathf.Lerp(1.1f, 2.8f, controlHealth);
            float maximumAcceleration =
                Mathf.Lerp(0.35f, 4.5f, controlHealth);
            targetAngularAcceleration =
                Vector3.ClampMagnitude(
                    targetAngularAcceleration,
                    maximumAcceleration);
            forceLedger.AddTorque(
                TorqueForAngularAcceleration(
                    targetAngularAcceleration));
        }

        Vector3 ResolveObstacleAvoidance(Vector3 requestedVelocity)
        {
            int count = Physics.SphereCastNonAlloc(
                body.worldCenterOfMass,
                3f,
                transform.forward,
                obstacleHits,
                80f,
                ~0,
                QueryTriggerInteraction.Ignore);
            for (int index = 0; index < count; index++)
            {
                Collider collider = obstacleHits[index].collider;
                if (collider == null ||
                    collider.transform.IsChildOf(transform) ||
                    collider.transform.IsChildOf(playerBody.transform))
                    continue;
                Vector3 avoidance =
                    obstacleHits[index].normal + Vector3.up * 0.6f;
                return requestedVelocity +
                       avoidance.normalized * 35f;
            }
            return requestedVelocity;
        }

        void AccumulateExposedFaceDrag(
            PlanetEnvironmentSample environment)
        {
            if (!environment.hasAtmosphere ||
                environment.airDensity <= 0.0001f)
                return;
            foreach (Node node in nodes.Values)
            {
                if (node.Destroyed ||
                    node.Object == null ||
                    node.Role == ModuleRole.Wing)
                    continue;
                float cd =
                    node.Role == ModuleRole.Structure ||
                    node.Role == ModuleRole.Core
                        ? 0.7f
                        : 0.9f;
                foreach (Vector3Int offset in NeighborOffsets)
                {
                    if (occupancy.TryGetValue(
                            node.Cell + offset,
                            out string adjacentId) &&
                        nodes.TryGetValue(adjacentId, out Node adjacent) &&
                        !adjacent.Destroyed)
                    {
                        continue;
                    }
                    Vector3 normal = transform.TransformDirection(
                        (Vector3)offset).normalized;
                    Vector3 point =
                        node.Object.transform.position + normal * 0.5f;
                    Vector3 relativeVelocity =
                        body.GetPointVelocity(point) -
                        environment.atmosphereVelocity;
                    float closing =
                        Vector3.Dot(relativeVelocity, normal);
                    if (closing <= 0f)
                        continue;
                    float pressure =
                        0.5f * environment.airDensity *
                        closing * closing;
                    forceLedger.AddForceAtPoint(
                        -normal * pressure * cd,
                        point);
                }
            }
        }

        void AccumulateWingAerodynamics(
            PlanetEnvironmentSample environment)
        {
            if (!environment.hasAtmosphere ||
                environment.airDensity <= 0.0001f)
                return;
            foreach (Node node in nodes.Values)
            {
                if (node.Destroyed ||
                    node.Role != ModuleRole.Wing ||
                    node.Object == null ||
                    !IsFunctional(node))
                    continue;
                Vector3 point = node.Object.transform.position;
                Vector3 relativeVelocity =
                    body.GetPointVelocity(point) -
                    environment.atmosphereVelocity;
                float speedSquared = relativeVelocity.sqrMagnitude;
                if (speedSquared <= 0.25f)
                    continue;
                Vector3 localVelocity =
                    transform.InverseTransformDirection(relativeVelocity);
                float angleOfAttack = Mathf.Atan2(
                    -localVelocity.y,
                    Mathf.Max(0.1f, Mathf.Abs(localVelocity.z))) +
                    WingIncidenceRadians;
                float stallBlend = Mathf.InverseLerp(
                    35f * Mathf.Deg2Rad,
                    18f * Mathf.Deg2Rad,
                    Mathf.Abs(angleOfAttack));
                float liftCoefficient =
                    WingLiftSlope * angleOfAttack *
                    Mathf.Lerp(0.28f, 1f, stallBlend);
                liftCoefficient =
                    Mathf.Clamp(liftCoefficient, -1.35f, 1.35f);
                float dynamicPressure =
                    0.5f * environment.airDensity * speedSquared;
                Vector3 liftDirection =
                    Vector3.Cross(
                        relativeVelocity.normalized,
                        transform.right).normalized;
                if (Vector3.Dot(liftDirection, transform.up) < 0f)
                    liftDirection = -liftDirection;
                float dragCoefficient =
                    WingZeroLiftDrag +
                    WingInducedDrag *
                    liftCoefficient * liftCoefficient;
                Vector3 force =
                    liftDirection *
                    (dynamicPressure * WingArea * liftCoefficient) -
                    relativeVelocity.normalized *
                    (dynamicPressure * WingArea * dragCoefficient);
                forceLedger.AddForceAtPoint(force, point);
            }
        }

        void AccumulateThrusterForces(
            PlanetEnvironmentSample environment,
            Vector3 commandedVelocity)
        {
            int activeThrusters = nodes.Values.Count(node =>
                node.Role == ModuleRole.Thruster &&
                node.Object != null &&
                IsFunctional(node));
            if (activeThrusters <= 0)
            {
                thrusterThrottle = 0f;
                return;
            }
            Vector3 relativeVelocity =
                body.velocity - environment.atmosphereVelocity;
            float targetForwardSpeed = Mathf.Max(
                0f,
                Vector3.Dot(
                    commandedVelocity,
                    transform.forward));
            float currentForwardSpeed =
                Vector3.Dot(
                    relativeVelocity,
                    transform.forward);
            float requestedAcceleration = Mathf.Clamp(
                (targetForwardSpeed - currentForwardSpeed) * 0.8f,
                0f,
                30f);
            float pressureRatio = Mathf.Clamp01(
                environment.ambientPressure / 101325f);
            float environmentScale =
                Mathf.Clamp(
                    1f - pressureRatio * 0.06f,
                    0.88f,
                    1f);
            float availableForce =
                activeThrusters *
                ThrusterMaximumForce *
                environmentScale;
            float requestedForce =
                body.mass * requestedAcceleration;
            float targetThrottle = availableForce > 0.001f
                ? Mathf.Clamp01(
                    requestedForce / availableForce)
                : 0f;
            thrusterThrottle = Mathf.MoveTowards(
                thrusterThrottle,
                targetThrottle,
                Time.fixedDeltaTime / 0.12f);
            Vector3 forcePerThruster =
                transform.forward *
                (ThrusterMaximumForce *
                 environmentScale *
                 thrusterThrottle);
            foreach (Node node in nodes.Values)
            {
                if (node.Role != ModuleRole.Thruster ||
                    node.Object == null ||
                    !IsFunctional(node))
                    continue;
                forceLedger.AddForceAtPoint(
                    forcePerThruster,
                    node.Object.transform.position);
            }
        }

        void AccumulateAerodynamicControl(
            PlanetEnvironmentSample environment,
            Vector3 commandedVelocity)
        {
            if (!environment.hasAtmosphere ||
                environment.airDensity <= 0.0001f)
                return;
            Vector3 desiredForward =
                commandedVelocity.sqrMagnitude > 1f
                    ? commandedVelocity.normalized
                    : desiredAimDirection.sqrMagnitude > 0.001f
                        ? desiredAimDirection.normalized
                        : transform.forward;
            if (Mathf.Abs(
                    Vector3.Dot(
                        desiredForward,
                        Vector3.up)) > 0.98f)
            {
                desiredForward =
                    Vector3.ProjectOnPlane(
                        desiredForward,
                        Vector3.up);
                if (desiredForward.sqrMagnitude < 0.001f)
                    desiredForward = transform.forward;
                desiredForward.Normalize();
            }
            Quaternion desiredRotation =
                Quaternion.LookRotation(
                    desiredForward,
                    Vector3.up);
            Quaternion delta =
                desiredRotation *
                Quaternion.Inverse(body.rotation);
            delta.ToAngleAxis(
                out float angle,
                out Vector3 axis);
            if (angle > 180f)
                angle -= 360f;
            if (!Finite(axis))
                axis = Vector3.zero;
            Vector3 desiredAngularAcceleration =
                Vector3.ClampMagnitude(
                    axis.normalized *
                    (angle * Mathf.Deg2Rad * 2.6f) -
                    body.angularVelocity * 1.7f,
                    5f * Mathf.Max(
                        0.15f,
                        movementScale));
            Vector3 residualTorque =
                TorqueForAngularAcceleration(
                    desiredAngularAcceleration);

            float relativeSpeed =
                (body.velocity -
                 environment.atmosphereVelocity).magnitude;
            float dynamicPressure =
                0.5f *
                environment.airDensity *
                relativeSpeed *
                relativeSpeed;
            float forceLimit =
                dynamicPressure *
                WingArea *
                0.65f *
                Mathf.Max(0.15f, movementScale);
            if (forceLimit <= 0.01f)
                return;

            foreach (Node node in nodes.Values)
            {
                if (node.Destroyed ||
                    node.Role != ModuleRole.Wing ||
                    node.Object == null ||
                    !IsFunctional(node))
                    continue;
                Vector3 point =
                    node.Object.transform.position;
                Vector3 arm =
                    point - body.worldCenterOfMass;
                residualTorque = AllocateControlForce(
                    point,
                    arm,
                    transform.up,
                    forceLimit,
                    residualTorque);
                residualTorque = AllocateControlForce(
                    point,
                    arm,
                    transform.right,
                    forceLimit * 0.45f,
                    residualTorque);
            }
        }

        Vector3 AllocateControlForce(
            Vector3 point,
            Vector3 arm,
            Vector3 forceDirection,
            float forceLimit,
            Vector3 residualTorque)
        {
            Vector3 torquePerNewton =
                Vector3.Cross(arm, forceDirection);
            float denominator =
                torquePerNewton.sqrMagnitude;
            if (denominator <= 0.000001f)
                return residualTorque;
            float scalar = Mathf.Clamp(
                Vector3.Dot(
                    residualTorque,
                    torquePerNewton) /
                denominator,
                -forceLimit,
                forceLimit);
            forceLedger.AddForceAtPoint(
                forceDirection * scalar,
                point);
            return residualTorque -
                   torquePerNewton * scalar;
        }

        Vector3 TorqueForAngularAcceleration(
            Vector3 worldAngularAcceleration)
        {
            Quaternion principalToWorld =
                body.rotation *
                body.inertiaTensorRotation;
            Vector3 principalAcceleration =
                Quaternion.Inverse(principalToWorld) *
                worldAngularAcceleration;
            return principalToWorld *
                   Vector3.Scale(
                       body.inertiaTensor,
                       principalAcceleration);
        }

        static bool Finite(Vector3 value)
        {
            return !float.IsNaN(value.x) &&
                   !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) &&
                   !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) &&
                   !float.IsInfinity(value.z);
        }

        void TryFire(Vector3 target)
        {
            if (!weaponCommand.FireHeld ||
                Time.time < nextShot ||
                ActiveWeaponCount <= 0 ||
                projectiles == null)
                return;
            WeaponProfile baseProfile =
                WeaponProfileLibrary.Resolve(null);
            List<Node> firingWeapons = nodes.Values
                .Where(item =>
                    item.Role == ModuleRole.Weapon &&
                    IsFunctional(item) &&
                    item.Object != null)
                .ToList();
            if (firingWeapons.Count == 0)
                return;
            nextShot =
                Time.time +
                1f / Mathf.Max(
                    1f,
                    baseProfile.shotsPerSecond *
                    Mathf.Max(0.35f, movementScale));
            float rawDps =
                firingWeapons.Count *
                baseProfile.damage *
                baseProfile.shotsPerSecond;
            float damageScale =
                CombatBalanceRuntime.DamageScale(rawDps);
            for (int index = 0;
                 index < firingWeapons.Count;
                 index++)
            {
                Node weapon = firingWeapons[index];
                Vector3 origin =
                    weapon.Object.transform.position +
                    transform.forward * 0.7f;
                Vector3 direction =
                    (target - origin).normalized;
                WeaponProfile profile =
                    WeaponProfileLibrary.Resolve(null);
                profile.damage =
                    baseProfile.damage *
                    damageScale *
                    weapon.FunctionScale;
                profile.delivery =
                    WeaponDeliveryKind.PhysicalProjectile;
                profile.projectileSpeed = AttackProjectileSpeed;
                profile.range = 650f;
                profile.explosionRadius = 0f;
                profile.effectColor =
                    new Color(1f, 0.2f, 0.04f, 1f);
                profile.projectileEffect =
                    "sfx/mc/machinegun_bullet_shoot.sfx";
                visuals?.SpawnMuzzle(
                    origin,
                    direction,
                    profile.effectColor,
                    profile.muzzleEffect,
                    weapon.Object.transform);
                projectiles.Launch(
                    origin,
                    direction,
                    Vector3.zero,
                    profile,
                    transform,
                    null);
            }
        }

        public void StopCombat()
        {
            if (stopped)
                return;
            stopped = true;
            desiredWorldVelocity = Vector3.zero;
            weaponCommand = default;
            ResetDelayedAttack(0f);
            if (fleetVisual != null)
                fleetVisual.SetActive(false);
            if (body != null)
            {
                if (!body.isKinematic)
                {
                    body.velocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
                body.isKinematic = true;
            }
        }
    }
}
