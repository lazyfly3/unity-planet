using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using ModularAssembly;

namespace UnityPlanet.ModularAssembly
{
    public enum FlightEnvironmentKind
    {
        PlanetLab = 0,
        CombatMapLab = 1
    }

    public enum CombatPreparationState
    {
        Idle,
        Warming,
        Ready,
        Failed
    }

    public interface IGridFlightEnvironmentWarmup
    {
        IEnumerator Warmup(Action<bool, string> completed);
    }

    public interface ICombatArenaProvider
    {
        CombatTestMode Mode { get; }
        Vector3 BattleCenter { get; }
        float WarningRadius { get; }
        float ForfeitRadius { get; }
        float FlightCeiling { get; }
        void SetMode(CombatTestMode mode);
        bool TryGetPlayerSpawn(out Vector3 position, out Quaternion rotation);
        bool TryGetEnemySpawn(out Vector3 position, out Quaternion rotation);
    }

    [Serializable]
    internal sealed class ModularLabSettingsData
    {
        public int version = 1;
        public FlightEnvironmentKind environment = FlightEnvironmentKind.PlanetLab;
    }

    /// <summary>
    /// The only environment selected by GridFlightBridge. It delegates to one
    /// concrete provider and never applies vehicle forces itself.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FlightEnvironmentManager : MonoBehaviour, IGridFlightEnvironment, IGridFlightEnvironmentWarmup
    {
        public const string CombatRuntimeSceneName = "CombatMapRuntime";

        private IGridFlightEnvironment planetEnvironment;
        private IGridFlightEnvironment combatEnvironment;
        private AsyncOperation combatSceneLoad;
        private Scene combatScene;
        private FlightEnvironmentKind selectedKind;
        private CombatPreparationState preparationState;
        private float preparationProgress;
        private string preparationMessage = string.Empty;
        private bool preparing;
        private bool inFlight;
        private CombatTestMode combatMode = CombatTestMode.Duel;

        public int Priority
        {
            get { return 100000; }
        }

        public Quaternion PreparedRotation
        {
            get
            {
                IGridFlightEnvironment provider = ActiveProvider;
                return provider != null ? provider.PreparedRotation : Quaternion.identity;
            }
        }

        public FlightEnvironmentKind SelectedKind
        {
            get { return selectedKind; }
        }

        public CombatPreparationState PreparationState
        {
            get { return preparationState; }
        }

        public float PreparationProgress
        {
            get { return preparationProgress; }
        }

        public string PreparationMessage
        {
            get { return preparationMessage; }
        }

        public ICombatArenaProvider ActiveArena
        {
            get { return ActiveProvider as ICombatArenaProvider; }
        }

        public CombatTestMode CombatMode
        {
            get { return combatMode; }
        }

        private IGridFlightEnvironment ActiveProvider
        {
            get
            {
                if (selectedKind == FlightEnvironmentKind.CombatMapLab && combatEnvironment != null)
                {
                    return combatEnvironment;
                }

                return planetEnvironment;
            }
        }

        private void Awake()
        {
            LoadSettings();
            RefreshProviders();
        }

        private void Start()
        {
            StartCoroutine(Warmup(null));
            EnsureSelectorUi();
        }

        public void SetSelectedKind(FlightEnvironmentKind kind)
        {
            if (selectedKind == kind || inFlight)
            {
                return;
            }

            selectedKind = kind;
            preparationState = CombatPreparationState.Idle;
            preparationProgress = 0f;
            preparationMessage = string.Empty;
            SaveSettings();
            StartCoroutine(Warmup(null));
            FlightEnvironmentSelectorOverlay overlay = GetComponent<FlightEnvironmentSelectorOverlay>();
            if (overlay != null)
            {
                overlay.Refresh();
            }
        }

        public void ToggleSelectedKind()
        {
            SetSelectedKind(
                selectedKind == FlightEnvironmentKind.PlanetLab
                    ? FlightEnvironmentKind.CombatMapLab
                    : FlightEnvironmentKind.PlanetLab);
        }

        public void SetCombatMode(CombatTestMode mode)
        {
            if (inFlight || combatMode == mode)
                return;
            combatMode = mode;
            ICombatArenaProvider arena = ActiveArena;
            if (arena != null)
                arena.SetMode(mode);
            if (selectedKind == FlightEnvironmentKind.CombatMapLab)
            {
                preparationState = CombatPreparationState.Idle;
                preparationProgress = 0f;
                preparationMessage = mode == CombatTestMode.Horde
                    ? "正在准备割草战区"
                    : "正在准备1v1战区";
            }
        }

        public IEnumerator Warmup(Action<bool, string> completed)
        {
            if (preparing)
            {
                while (preparing)
                {
                    yield return null;
                }

                bool alreadyReady = preparationState == CombatPreparationState.Ready;
                if (completed != null)
                {
                    completed(alreadyReady, preparationMessage);
                }

                yield break;
            }

            preparing = true;
            preparationState = CombatPreparationState.Warming;
            preparationProgress = 0.05f;
            preparationMessage = selectedKind == FlightEnvironmentKind.CombatMapLab
                ? "正在预热 CombatMapLab"
                : "正在预热 PlanetLab";

            if (selectedKind == FlightEnvironmentKind.CombatMapLab)
            {
                yield return EnsureCombatSceneLoaded();
                preparationProgress = 0.65f;
            }

            RefreshProviders();
            IGridFlightEnvironment provider = ActiveProvider;
            if (provider == null)
            {
                preparationState = CombatPreparationState.Failed;
                preparationMessage = selectedKind == FlightEnvironmentKind.CombatMapLab
                    ? "CombatMapLab 运行时场景未烘焙或未加入 Build Settings"
                    : "PlanetLab 环境不可用";
                preparing = false;
                if (completed != null)
                {
                    completed(false, preparationMessage);
                }

                yield break;
            }

            bool providerReady = true;
            string providerMessage = string.Empty;
            ICombatArenaProvider combatArena =
                provider as ICombatArenaProvider;
            if (combatArena != null)
                combatArena.SetMode(combatMode);
            IGridFlightEnvironmentWarmup warmup = provider as IGridFlightEnvironmentWarmup;
            if (warmup != null)
            {
                yield return warmup.Warmup(
                    (success, message) =>
                    {
                        providerReady = success;
                        providerMessage = message;
                    });
            }

            if (providerReady && !inFlight)
            {
                provider.ExitFlight();
            }

            preparationProgress = providerReady ? 1f : preparationProgress;
            preparationState = providerReady ? CombatPreparationState.Ready : CombatPreparationState.Failed;
            preparationMessage = string.IsNullOrWhiteSpace(providerMessage)
                ? providerReady ? "测试场已就绪" : "测试场预热失败"
                : providerMessage;
            preparing = false;
            if (completed != null)
            {
                completed(providerReady, preparationMessage);
            }
        }

        public IEnumerator PrepareFlight(
            Rigidbody target,
            Action<bool, Vector3, string> completed)
        {
            bool ready = preparationState == CombatPreparationState.Ready;
            string message = preparationMessage;
            if (!ready)
            {
                yield return Warmup(
                    (success, resultMessage) =>
                    {
                        ready = success;
                        message = resultMessage;
                    });
            }

            if (!ready || ActiveProvider == null)
            {
                completed(false, target != null ? target.position : Vector3.zero, message);
                yield break;
            }

            inFlight = true;
            yield return ActiveProvider.PrepareFlight(target, completed);
        }

        public IEnumerator ResetFlight(
            Rigidbody target,
            Action<bool, Vector3> completed)
        {
            if (ActiveProvider == null)
            {
                completed(
                    false,
                    target != null
                        ? target.position
                        : Vector3.zero);
                yield break;
            }

            yield return ActiveProvider.ResetFlight(target, completed);
        }

        public void ExitFlight()
        {
            inFlight = false;
            if (ActiveProvider != null)
            {
                ActiveProvider.ExitFlight();
            }
        }

        private IEnumerator EnsureCombatSceneLoaded()
        {
            if (combatScene.IsValid() && combatScene.isLoaded)
            {
                yield break;
            }

            Scene existing = SceneManager.GetSceneByName(CombatRuntimeSceneName);
            if (existing.IsValid() && existing.isLoaded)
            {
                combatScene = existing;
                yield break;
            }

            combatSceneLoad = SceneManager.LoadSceneAsync(CombatRuntimeSceneName, LoadSceneMode.Additive);
            if (combatSceneLoad == null)
            {
                yield break;
            }

            combatSceneLoad.allowSceneActivation = true;
            while (!combatSceneLoad.isDone)
            {
                preparationProgress = Mathf.Lerp(0.1f, 0.6f, combatSceneLoad.progress / 0.9f);
                yield return null;
            }

            combatScene = SceneManager.GetSceneByName(CombatRuntimeSceneName);
        }

        private void RefreshProviders()
        {
            planetEnvironment = null;
            combatEnvironment = null;
            MonoBehaviour[] behaviours = FindObjectsOfType<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null || behaviour == this)
                {
                    continue;
                }

                IGridFlightEnvironment candidate = behaviour as IGridFlightEnvironment;
                if (candidate == null)
                {
                    continue;
                }

                string typeName = behaviour.GetType().Name;
                if (typeName.IndexOf("BakedCombatMap", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    typeName.IndexOf("CombatMapFlight", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    combatEnvironment = candidate;
                }
                else if (typeName.IndexOf("PlanetLab", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    planetEnvironment = candidate;
                }
            }
            ICombatArenaProvider arena =
                combatEnvironment as ICombatArenaProvider;
            if (arena != null)
                arena.SetMode(combatMode);
        }

        private void EnsureSelectorUi()
        {
            if (GetComponent<FlightEnvironmentSelectorOverlay>() == null)
            {
                gameObject.AddComponent<FlightEnvironmentSelectorOverlay>();
            }
        }

        private string GetSettingsPath()
        {
            try
            {
                ModularBlueprintStore store = new ModularBlueprintStore();
                string slotId = store.ActiveSlotId;
                string directory = GalaxySaveSlotService.GetSpacecraftDirectory(slotId);
                Directory.CreateDirectory(directory);
                return Path.Combine(directory, "lab_settings.json");
            }
            catch (Exception)
            {
                string directory = Path.Combine(Application.persistentDataPath, "spacecraft");
                Directory.CreateDirectory(directory);
                return Path.Combine(directory, "lab_settings.json");
            }
        }

        private void LoadSettings()
        {
            selectedKind = FlightEnvironmentKind.PlanetLab;
            string path = GetSettingsPath();
            if (!File.Exists(path))
            {
                return;
            }

            try
            {
                ModularLabSettingsData data = JsonUtility.FromJson<ModularLabSettingsData>(File.ReadAllText(path));
                if (data != null)
                {
                    selectedKind = data.environment;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[FlightEnvironment] 无法读取测试场设置：" + exception.Message);
            }
        }

        private void SaveSettings()
        {
            string path = GetSettingsPath();
            string temporary = path + ".tmp";
            try
            {
                ModularLabSettingsData data = new ModularLabSettingsData { environment = selectedKind };
                File.WriteAllText(temporary, JsonUtility.ToJson(data, true));
                if (File.Exists(path))
                {
                    File.Replace(temporary, path, null);
                }
                else
                {
                    File.Move(temporary, path);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[FlightEnvironment] 无法保存测试场设置：" + exception.Message);
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }
    }

    [DisallowMultipleComponent]
    public sealed class FlightEnvironmentSelectorOverlay : MonoBehaviour
    {
        private FlightEnvironmentManager manager;
        private Canvas canvas;
        private Button button;
        private Text label;
        private Text stateLabel;
        private GridFlightBridge flight;

        private void Awake()
        {
            manager = GetComponent<FlightEnvironmentManager>();
            flight = FindObjectOfType<GridFlightBridge>(true);
            BuildUi();
            Refresh();
        }

        private void Update()
        {
            if (manager == null || stateLabel == null)
            {
                return;
            }

            if (flight == null)
                flight = FindObjectOfType<GridFlightBridge>(true);
            if (canvas != null)
                canvas.enabled = flight == null || !flight.IsFlying;

            stateLabel.text = manager.PreparationState == CombatPreparationState.Warming
                ? "预热 " + Mathf.RoundToInt(manager.PreparationProgress * 100f) + "%"
                : manager.PreparationState == CombatPreparationState.Failed
                    ? "测试场不可用"
                    : string.Empty;
        }

        public void Refresh()
        {
            if (manager != null && label != null)
            {
                label.text = manager.SelectedKind == FlightEnvironmentKind.PlanetLab
                    ? "测试场：PlanetLab"
                    : "测试场：CombatMap";
            }
        }

        private void BuildUi()
        {
            GameObject canvasObject = new GameObject(
                "FlightEnvironmentSelectorCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 8400;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            GameObject buttonObject = new GameObject("EnvironmentButton", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(canvasObject.transform, false);
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-24f, -164f);
            rect.sizeDelta = new Vector2(210f, 42f);
            buttonObject.GetComponent<Image>().color = new Color(0.04f, 0.33f, 0.43f, 0.96f);
            button = buttonObject.GetComponent<Button>();
            button.onClick.AddListener(manager.ToggleSelectedKind);

            label = CreateText(buttonObject.transform, string.Empty, 17, TextAnchor.MiddleCenter);
            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            stateLabel = CreateText(canvasObject.transform, string.Empty, 14, TextAnchor.MiddleRight);
            RectTransform stateRect = stateLabel.rectTransform;
            stateRect.anchorMin = new Vector2(1f, 1f);
            stateRect.anchorMax = new Vector2(1f, 1f);
            stateRect.pivot = new Vector2(1f, 1f);
            stateRect.anchoredPosition = new Vector2(-244f, -164f);
            stateRect.sizeDelta = new Vector2(180f, 42f);

            if (EventSystem.current == null)
            {
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            }
        }

        private static Text CreateText(Transform parent, string value, int size, TextAnchor alignment)
        {
            GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            Text text = textObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = value;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = Color.white;
            return text;
        }
    }

    internal static class FlightEnvironmentRuntimeBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            HandleSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!scene.IsValid() ||
                scene.name.IndexOf("ModularAssemblyLab", StringComparison.OrdinalIgnoreCase) < 0 ||
                UnityEngine.Object.FindObjectOfType<FlightEnvironmentManager>(true) != null)
            {
                return;
            }

            GameObject host = new GameObject("FlightEnvironmentManager");
            SceneManager.MoveGameObjectToScene(host, scene);
            host.AddComponent<FlightEnvironmentManager>();
        }
    }
}
