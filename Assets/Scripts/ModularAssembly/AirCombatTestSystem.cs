using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ModularAssembly;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

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
        const float ForfeitRadius = 1500f;
        const float ForfeitSeconds = 5f;

        ModularAssemblyLabController lab;
        GridFlightBridge flight;
        GridAssemblyPresenter presenter;
        WeaponSystemCoordinator weapons;
        VehicleStructureGraph playerGraph;
        RobocraftMotionCoordinator playerMotion;
        Rigidbody playerBody;
        ModularBlueprintData playerSnapshot;
        EnemyAirCombatVehicle enemy;
        Canvas combatCanvas;
        Text statusText;
        Text playerText;
        Text enemyText;
        Text warningText;
        GameObject resultPanel;
        Text resultText;
        bool pendingCombat;
        bool combatActive;
        bool resolving;
        float outsideSeconds;
        float ineffectiveSeconds;
        Vector3 battleCenter;
        float basePlayerDrag;
        float basePlayerAngularDrag;
        GameObject targetObject;

        public GridFlightSessionKind SessionKind { get; private set; } =
            GridFlightSessionKind.FreeFlight;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (SceneManager.GetActiveScene().name != "ModularAssemblyLab" ||
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
                playerGraph.Destroyed += HandlePlayerDestroyed;
            flight.StateChanged += HandleFlightState;
            DisableLegacyTargetAi();
            BuildUi();
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
            UpdateCombatHud();
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
                playerMotion.CoreAssistMode ==
                VehicleCoreAssistMode.Standard)
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
            message = string.Empty;
            if (pendingCombat || combatActive)
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
            playerSnapshot = lab.Model.CaptureBlueprint();
            pendingCombat = true;
            SessionKind = GridFlightSessionKind.CombatTest;
            message = "正在进入战斗测试。";
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
                CleanupCombat(true);
            }
        }

        IEnumerator StartCombatAfterPhysicsReady()
        {
            yield return new WaitForFixedUpdate();
            ResolveReferences();
            if (playerBody == null)
            {
                ExitCombat();
                yield break;
            }

            battleCenter = playerBody.position +
                           Vector3.up * BattleAltitude;
            if (!playerBody.isKinematic)
            {
                playerBody.velocity = Vector3.zero;
                playerBody.angularVelocity = Vector3.zero;
            }
            playerBody.isKinematic = true;
            playerBody.position = battleCenter;
            playerBody.rotation = Quaternion.identity;
            basePlayerDrag = playerBody.drag;
            basePlayerAngularDrag = playerBody.angularDrag;
            playerBody.isKinematic = false;

            playerGraph = weapons.StructureGraph;
            if (playerGraph != null)
            {
                playerGraph.SetAutomaticReturnToBuild(false);
                playerGraph.EndFlight();
                playerGraph.BeginFlight();
            }
            playerMotion?.SetLegacyDamageEnabled(false);
            if (targetObject != null)
                targetObject.SetActive(false);
            SpawnEnemy();
            outsideSeconds = 0f;
            ineffectiveSeconds = 0f;
            combatActive = true;
            resolving = false;
            if (combatCanvas != null)
                combatCanvas.gameObject.SetActive(true);
            if (resultPanel != null)
                resultPanel.SetActive(false);
        }

        void SpawnEnemy()
        {
            DestroyEnemy();
            GameObject root = new GameObject("StandardAirCombatEnemy");
            root.transform.position =
                battleCenter + Vector3.forward * EnemySpawnDistance +
                Vector3.up * 20f;
            root.transform.rotation = Quaternion.LookRotation(
                battleCenter - root.transform.position,
                Vector3.up);
            enemy = root.AddComponent<EnemyAirCombatVehicle>();
            WeaponVisualPool pool =
                weapons.GetComponent<WeaponVisualPool>();
            enemy.Initialize(
                this,
                playerGraph,
                playerBody,
                pool);
        }

        void CheckBattleOutcome()
        {
            if (playerGraph == null || !playerGraph.Active)
            {
                CompleteBattle(false, "载具核心被摧毁");
                return;
            }
            if (enemy == null || !enemy.IsCombatCapable)
            {
                CompleteBattle(true, "敌机失去战斗能力");
                return;
            }

            float distance = Vector3.Distance(
                playerBody.position,
                battleCenter);
            if (distance > WarningRadius)
            {
                outsideSeconds += Time.deltaTime;
                warningText.text =
                    $"正在脱离战斗空域  {outsideSeconds:0.0}/{ForfeitSeconds:0.0}秒";
                if (distance > ForfeitRadius &&
                    outsideSeconds >= ForfeitSeconds)
                {
                    CompleteBattle(false, "脱离战斗空域");
                    return;
                }
            }
            else
            {
                outsideSeconds = 0f;
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

            float damage = 1f - playerGraph.OverallHealthRatio;
            playerBody.drag = basePlayerDrag + damage * 0.22f;
            playerBody.angularDrag =
                basePlayerAngularDrag + damage * 0.45f;
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
            if (enemy != null)
            {
                enemyText.text =
                    $"敌机  连接CPU {enemy.ConnectedRatio * 100f:0}%\n" +
                    $"状态  {StateText(enemy.CapabilityState)}\n" +
                    $"武器  {enemy.ActiveWeaponCount}";
            }
            statusText.text = "空战测试  1 VS 1";
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
            if (combatActive)
                CompleteBattle(true, reason);
        }

        void CompleteBattle(bool playerWon, string reason)
        {
            if (resolving)
                return;
            resolving = true;
            combatActive = false;
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
            enemy?.StopCombat();
            if (resultPanel != null)
            {
                resultPanel.SetActive(true);
                resultText.text =
                    (playerWon ? "战斗胜利" : "战斗失败") +
                    "\n" + reason;
            }
            warningText.text = string.Empty;
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
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            DestroyEnemy();
            lab.Model.RestoreBlueprint(playerSnapshot, out string ignored);
            yield return null;
            yield return new WaitForFixedUpdate();
            playerGraph?.EndFlight();
            playerGraph?.BeginFlight();
            if (!playerBody.isKinematic)
            {
                playerBody.velocity = Vector3.zero;
                playerBody.angularVelocity = Vector3.zero;
            }
            playerBody.isKinematic = true;
            playerBody.position = battleCenter;
            playerBody.rotation = Quaternion.identity;
            playerBody.isKinematic = false;
            SpawnEnemy();
            outsideSeconds = 0f;
            ineffectiveSeconds = 0f;
            combatActive = true;
            resolving = false;
            resultPanel.SetActive(false);
        }

        void ExitCombat()
        {
            if (lab?.Model != null && playerSnapshot != null)
                lab.Model.RestoreBlueprint(
                    playerSnapshot,
                    out string ignored);
            CleanupCombat(false);
            if (flight != null &&
                flight.State != GridFlightState.Build)
                flight.ExitFlight();
        }

        void CleanupCombat(bool flightAlreadyExited)
        {
            DestroyEnemy();
            combatActive = false;
            pendingCombat = false;
            resolving = false;
            SessionKind = GridFlightSessionKind.FreeFlight;
            if (playerGraph != null)
                playerGraph.SetAutomaticReturnToBuild(true);
            playerMotion?.SetLegacyDamageEnabled(true);
            if (targetObject != null)
                targetObject.SetActive(true);
            if (playerBody != null)
            {
                playerBody.drag = basePlayerDrag;
                playerBody.angularDrag = basePlayerAngularDrag;
                if (playerBody.isKinematic && !flightAlreadyExited)
                    playerBody.isKinematic = false;
            }
            if (combatCanvas != null)
                combatCanvas.gameObject.SetActive(false);
            playerSnapshot = null;
        }

        void DestroyEnemy()
        {
            if (enemy == null)
                return;
            Destroy(enemy.gameObject);
            enemy = null;
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
            if (flight != null)
                flight.StateChanged -= HandleFlightState;
            if (playerGraph != null)
                playerGraph.Destroyed -= HandlePlayerDestroyed;
        }
    }

    public sealed class EnemyCombatModuleReceiver :
        MonoBehaviour,
        ISpaceDamageable
    {
        EnemyAirCombatVehicle owner;
        string runtimeId;

        public float Integrity =>
            owner == null ? 0f : owner.Integrity(runtimeId);
        public float MaximumIntegrity =>
            owner == null ? 0f : owner.MaximumIntegrity(runtimeId);
        public bool IsDestroyed =>
            owner == null || owner.IsDestroyed(runtimeId);

        public void Initialize(
            EnemyAirCombatVehicle source,
            string id)
        {
            owner = source;
            runtimeId = id;
        }

        public void ApplyDamage(SpaceDamageInfo damage)
        {
            owner?.ApplyDamage(runtimeId, damage);
        }
    }

    public sealed class EnemyAirCombatVehicle :
        MonoBehaviour,
        IVehicleMotionCommandSource,
        IVehicleWeaponCommandSource
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
            public string SupportId;
            public readonly HashSet<string> Edges =
                new HashSet<string>(StringComparer.Ordinal);
            public readonly HashSet<string> Dependents =
                new HashSet<string>(StringComparer.Ordinal);

            public float FunctionScale =>
                Destroyed
                    ? 0f
                    : Health / Mathf.Max(1f, MaximumHealth) > 0.5f
                        ? 1f
                        : 0.85f;
        }

        readonly Dictionary<string, Node> nodes =
            new Dictionary<string, Node>(StringComparer.Ordinal);
        readonly Dictionary<Vector3Int, string> occupancy =
            new Dictionary<Vector3Int, string>();
        readonly VehicleDependencyGraph dependencyGraph =
            new VehicleDependencyGraph();
        readonly RaycastHit[] obstacleHits = new RaycastHit[12];
        CombatTestController owner;
        VehicleStructureGraph playerGraph;
        Rigidbody playerBody;
        WeaponVisualPool visuals;
        Rigidbody body;
        Material coreMaterial;
        Material structureMaterial;
        Material wingMaterial;
        Material thrusterMaterial;
        Material weaponMaterial;
        Material energyMaterial;
        float initialCpu;
        float currentCpu;
        float movementScale;
        float nextDecision;
        float nextShot;
        float burstUntil;
        float burstCooldownUntil;
        float aimErrorYaw;
        float aimErrorPitch;
        float orbitDirection;
        bool stopped;
        string coreId;
        Vector3 desiredWorldVelocity;
        Vector3 desiredAimDirection;
        WeaponCommandFrame weaponCommand;

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
        public bool IsCombatCapable =>
            !stopped &&
            ConnectedRatio >= 0.2f &&
            ActiveWeaponCount > 0 &&
            movementScale > 0.15f;

        public void Initialize(
            CombatTestController session,
            VehicleStructureGraph targetGraph,
            Rigidbody targetBody,
            WeaponVisualPool effectPool)
        {
            owner = session;
            playerGraph = targetGraph;
            playerBody = targetBody;
            visuals = effectPool;
            body = gameObject.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.drag = 0.08f;
            body.angularDrag = 0.72f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode =
                CollisionDetectionMode.ContinuousDynamic;
            orbitDirection = UnityEngine.Random.value < 0.5f ? -1f : 1f;
            CreateMaterials();
            BuildStandardEnemy();
            RebuildGraphsAndMass();
            initialCpu = currentCpu;
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
            part.AddComponent<EnemyCombatModuleReceiver>()
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

        void RebuildGraphsAndMass()
        {
            foreach (Node node in nodes.Values)
            {
                node.Edges.Clear();
                node.SupportId = null;
                node.Dependents.Clear();
            }
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
            RebuildMountDependencies();

            HashSet<string> connected = ConnectedToCore();
            foreach (Node node in nodes.Values)
            {
                if (!node.Destroyed && !connected.Contains(node.Id))
                    DestroyNode(node, true);
            }

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
            int initialMovers = nodes.Values.Count(item =>
                item.Role == ModuleRole.Thruster ||
                item.Role == ModuleRole.Wing);
            movementScale = initialMovers <= 0
                ? 0f
                : movers.Sum(item => item.FunctionScale) / initialMovers;

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

        void RebuildMountDependencies()
        {
            Dictionary<string, int> depth = BuildCoreDepths();
            foreach (Node node in nodes.Values)
            {
                if (node.Destroyed ||
                    !RequiresMountSupport(node) ||
                    !depth.TryGetValue(node.Id, out int nodeDepth))
                    continue;
                string supportId = node.Edges
                    .Where(depth.ContainsKey)
                    .OrderBy(id => depth[id] < nodeDepth ? 0 : 1)
                    .ThenBy(id => depth[id])
                    .ThenBy(id => id, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (string.IsNullOrEmpty(supportId))
                    continue;
                node.SupportId = supportId;
                nodes[supportId].Dependents.Add(node.Id);
            }
        }

        Dictionary<string, int> BuildCoreDepths()
        {
            var result = new Dictionary<string, int>(
                StringComparer.Ordinal);
            if (string.IsNullOrEmpty(coreId) ||
                !nodes.TryGetValue(coreId, out Node core) ||
                core.Destroyed)
                return result;
            var queue = new Queue<string>();
            queue.Enqueue(coreId);
            result[coreId] = 0;
            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                foreach (string edge in nodes[current].Edges)
                {
                    if (!nodes.TryGetValue(edge, out Node other) ||
                        other.Destroyed ||
                        result.ContainsKey(edge))
                        continue;
                    result[edge] = result[current] + 1;
                    queue.Enqueue(edge);
                }
            }
            return result;
        }

        static bool RequiresMountSupport(Node node)
        {
            return node != null &&
                   (node.Role == ModuleRole.Weapon ||
                    node.Role == ModuleRole.Energy ||
                    node.Role == ModuleRole.Thruster);
        }

        HashSet<string> CollectMountDependents(string supportId)
        {
            var result = new HashSet<string>(
                StringComparer.Ordinal);
            if (!nodes.TryGetValue(supportId, out Node support))
                return result;
            var queue = new Queue<string>(support.Dependents);
            while (queue.Count > 0)
            {
                string id = queue.Dequeue();
                if (!result.Add(id) ||
                    !nodes.TryGetValue(id, out Node dependent))
                    continue;
                foreach (string child in dependent.Dependents)
                    queue.Enqueue(child);
            }
            return result;
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
            if (node.Health > 0f)
            {
                if (node.Health / node.MaximumHealth <= 0.5f &&
                    node.Renderer != null)
                    node.Renderer.sharedMaterial = structureMaterial;
                RebuildGraphsAndMass();
                return;
            }
            HashSet<string> removed =
                CollectMountDependents(id);
            DestroyNode(node, false);
            foreach (string dependentId in removed)
                if (nodes.TryGetValue(
                        dependentId,
                        out Node dependent))
                    DestroyNode(dependent, true);
            RebuildGraphsAndMass();
        }

        void DestroyNode(Node node, bool disconnected)
        {
            if (node == null || node.Destroyed)
                return;
            node.Destroyed = true;
            node.Health = 0f;
            if (node.Object != null)
            {
                Renderer renderer = node.Renderer;
                Bounds bounds = renderer != null
                    ? renderer.bounds
                    : new Bounds(node.Object.transform.position, Vector3.one);
                visuals?.SpawnBreakup(bounds);
                node.Object.SetActive(false);
            }
        }

        void Update()
        {
            if (stopped || playerBody == null || body == null)
                return;
            if (Time.time >= nextDecision)
            {
                nextDecision = Time.time + 0.25f;
                aimErrorYaw = UnityEngine.Random.Range(-2.5f, 2.5f);
                aimErrorPitch = UnityEngine.Random.Range(-2.5f, 2.5f);
            }
            Vector3 target =
                playerBody.worldCenterOfMass +
                playerBody.velocity *
                Mathf.Clamp(
                    Vector3.Distance(body.position, playerBody.position) /
                    650f,
                    0f,
                    1.2f);
            Vector3 direction = target - body.worldCenterOfMass;
            if (direction.sqrMagnitude < 0.01f)
                direction = transform.forward;
            desiredAimDirection =
                Quaternion.Euler(aimErrorPitch, aimErrorYaw, 0f) *
                direction.normalized;
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

            if (Time.time >= burstCooldownUntil)
            {
                burstUntil = Time.time + 1.2f;
                burstCooldownUntil = burstUntil + 0.8f;
            }
            weaponCommand = new WeaponCommandFrame
            {
                WeaponGroup = 1,
                FireHeld = Time.time <= burstUntil,
                AimHeld = true,
                AimPoint = target
            };
            TryFire(target);
        }

        void FixedUpdate()
        {
            if (stopped || playerBody == null || body == null ||
                body.isKinematic)
                return;

            Vector3 velocityError =
                desiredWorldVelocity - body.velocity;
            Vector3 acceleration = Vector3.ClampMagnitude(
                velocityError * 1.8f,
                22f * Mathf.Max(0.15f, movementScale));
            Vector3 hover = Vector3.up * 9.81f;
            body.AddForce(
                (acceleration + hover) * body.mass,
                ForceMode.Force);

            Quaternion desiredRotation = Quaternion.LookRotation(
                desiredAimDirection.sqrMagnitude > 0.001f
                    ? desiredAimDirection
                    : transform.forward,
                Vector3.up);
            Quaternion delta =
                desiredRotation * Quaternion.Inverse(body.rotation);
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f)
                angle -= 360f;
            Vector3 torque =
                axis.normalized * (angle * Mathf.Deg2Rad) *
                body.mass * 7f * movementScale -
                body.angularVelocity * body.mass * 2.4f;
            body.AddTorque(
                Vector3.ClampMagnitude(
                    torque,
                    body.mass * 30f * movementScale),
                ForceMode.Force);

            ApplyObstacleAvoidance();
        }

        void ApplyObstacleAvoidance()
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
                body.AddForce(
                    (obstacleHits[index].normal + Vector3.up * 0.6f) *
                    body.mass * 14f,
                    ForceMode.Force);
                break;
            }
        }

        void TryFire(Vector3 target)
        {
            if (!weaponCommand.FireHeld ||
                Time.time < nextShot ||
                ActiveWeaponCount <= 0)
                return;
            nextShot = Time.time + 0.16f / Mathf.Max(0.35f, movementScale);
            Node weapon = nodes.Values
                .Where(item =>
                    item.Role == ModuleRole.Weapon &&
                    IsFunctional(item) &&
                    item.Object != null)
                .OrderBy(item => UnityEngine.Random.value)
                .FirstOrDefault();
            if (weapon == null)
                return;
            Vector3 origin = weapon.Object.transform.position +
                             transform.forward * 0.7f;
            Vector3 direction = (target - origin).normalized;
            WeaponProfile profile = WeaponProfileLibrary.Resolve(null);
            profile.damage = 14f * weapon.FunctionScale;
            profile.range = 650f;
            WeaponDamageUtility.Trace(
                origin,
                direction,
                profile.range,
                transform,
                gameObject,
                profile,
                visuals,
                out RaycastHit hit);
            Vector3 end = hit.collider != null
                ? hit.point
                : origin + direction * profile.range;
            visuals?.SpawnTracer(
                origin,
                end,
                new Color(1f, 0.22f, 0.08f),
                0.07f,
                "sfx/mc/machinegun_bullet_shoot.sfx");
        }

        public void StopCombat()
        {
            if (stopped)
                return;
            stopped = true;
            desiredWorldVelocity = Vector3.zero;
            weaponCommand = default;
            if (body != null)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
            }
        }
    }
}
