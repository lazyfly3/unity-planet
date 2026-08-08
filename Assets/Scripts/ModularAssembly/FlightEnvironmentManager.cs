using System;
using System.Collections;
using System.IO;
using ModularAssembly;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace UnityPlanet.ModularAssembly
{
    public enum FlightEnvironmentKind
    {
        Natural = 0,
        City = 1,

        // Source-compatible names for older code and persisted version-1 data.
        PlanetLab = Natural,
        CombatMapLab = City
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
        public int version = 2;
        public FlightEnvironmentKind environment = FlightEnvironmentKind.Natural;
    }

    /// <summary>
    /// Single scene-scoped entry point used by the flight bridge.  Designers
    /// select either the infinite natural PlanetLab or the combat-city PCG;
    /// the manager delegates lifecycle calls without changing ship physics.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FlightEnvironmentManager :
        MonoBehaviour,
        IGridFlightEnvironment,
        IGridFlightEnvironmentWarmup
    {
        IGridFlightEnvironment naturalEnvironment;
        IGridFlightEnvironment cityEnvironment;
        FlightEnvironmentKind selectedKind;
        CombatPreparationState preparationState;
        float preparationProgress;
        string preparationMessage = string.Empty;
        bool preparing;
        bool inFlight;
        CombatTestMode combatMode = CombatTestMode.Duel;

        public int Priority => 100000;
        public Quaternion PreparedRotation =>
            ActiveProvider != null
                ? ActiveProvider.PreparedRotation
                : Quaternion.identity;
        public FlightEnvironmentKind SelectedKind => selectedKind;
        public CombatPreparationState PreparationState => preparationState;
        public float PreparationProgress => preparationProgress;
        public string PreparationMessage => preparationMessage;
        public ICombatArenaProvider ActiveArena =>
            ActiveProvider as ICombatArenaProvider;
        public CombatTestMode CombatMode => combatMode;

        IGridFlightEnvironment ActiveProvider =>
            selectedKind == FlightEnvironmentKind.City
                ? cityEnvironment
                : naturalEnvironment;

        void Awake()
        {
            LoadSettings();
            EnsureCityProvider();
            RefreshProviders();
        }

        void Start()
        {
            EnsureSelectorUi();
            StartCoroutine(Warmup(null));
        }

        public void SetSelectedKind(FlightEnvironmentKind kind)
        {
            kind = kind == FlightEnvironmentKind.City
                ? FlightEnvironmentKind.City
                : FlightEnvironmentKind.Natural;
            if (selectedKind == kind || inFlight)
                return;

            ActiveProvider?.ExitFlight();
            selectedKind = kind;
            preparationState = CombatPreparationState.Idle;
            preparationProgress = 0f;
            preparationMessage = string.Empty;
            SaveSettings();
            StartCoroutine(Warmup(null));
            GetComponent<FlightEnvironmentSelectorOverlay>()?.Refresh();
        }

        public void SelectNatural()
        {
            SetSelectedKind(FlightEnvironmentKind.Natural);
        }

        public void SelectCity()
        {
            SetSelectedKind(FlightEnvironmentKind.City);
        }

        public void ToggleSelectedKind()
        {
            SetSelectedKind(
                selectedKind == FlightEnvironmentKind.Natural
                    ? FlightEnvironmentKind.City
                    : FlightEnvironmentKind.Natural);
        }

        public void SetCombatMode(CombatTestMode mode)
        {
            if (inFlight || combatMode == mode)
                return;
            combatMode = mode;
            ActiveArena?.SetMode(mode);
        }

        public IEnumerator Warmup(Action<bool, string> completed)
        {
            if (preparing)
            {
                while (preparing)
                    yield return null;
                bool ready = preparationState == CombatPreparationState.Ready;
                completed?.Invoke(ready, preparationMessage);
                yield break;
            }

            preparing = true;
            preparationState = CombatPreparationState.Warming;
            preparationProgress = 0.05f;
            preparationMessage = selectedKind == FlightEnvironmentKind.City
                ? "正在生成城市试飞场……"
                : "正在准备自然试飞场……";

            // Other AfterSceneLoad bootstraps create the natural provider in
            // the same frame.  One yield makes bootstrap ordering irrelevant.
            yield return null;
            EnsureCityProvider();
            RefreshProviders();
            IGridFlightEnvironment provider = ActiveProvider;
            if (provider == null)
            {
                preparationState = CombatPreparationState.Failed;
                preparationMessage = selectedKind == FlightEnvironmentKind.City
                    ? "城市试飞场组件未初始化。"
                    : "自然试飞场组件未初始化。";
                preparing = false;
                completed?.Invoke(false, preparationMessage);
                yield break;
            }

            preparationProgress = 0.25f;
            bool providerReady = true;
            string providerMessage = string.Empty;
            if (provider is ICombatArenaProvider arena)
                arena.SetMode(combatMode);
            if (provider is IGridFlightEnvironmentWarmup warmup)
            {
                yield return warmup.Warmup((success, message) =>
                {
                    providerReady = success;
                    providerMessage = message;
                });
            }

            if (providerReady && !inFlight)
                provider.ExitFlight();
            preparationProgress = providerReady ? 1f : preparationProgress;
            preparationState = providerReady
                ? CombatPreparationState.Ready
                : CombatPreparationState.Failed;
            preparationMessage = string.IsNullOrWhiteSpace(providerMessage)
                ? providerReady
                    ? selectedKind == FlightEnvironmentKind.City
                        ? "城市试飞场已就绪。"
                        : "自然试飞场已就绪。"
                    : "试飞场准备失败。"
                : providerMessage;
            preparing = false;
            completed?.Invoke(providerReady, preparationMessage);
        }

        public IEnumerator PrepareFlight(
            Rigidbody target,
            Action<bool, Vector3, string> completed)
        {
            bool ready = preparationState == CombatPreparationState.Ready;
            string message = preparationMessage;
            if (!ready)
            {
                yield return Warmup((success, resultMessage) =>
                {
                    ready = success;
                    message = resultMessage;
                });
            }
            if (!ready || ActiveProvider == null)
            {
                completed?.Invoke(
                    false,
                    target != null ? target.position : Vector3.zero,
                    message);
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
                completed?.Invoke(false,
                    target != null ? target.position : Vector3.zero);
                yield break;
            }
            yield return ActiveProvider.ResetFlight(target, completed);
        }

        public void ExitFlight()
        {
            inFlight = false;
            ActiveProvider?.ExitFlight();
        }

        void EnsureCityProvider()
        {
            if (GetComponent<CityTestFlightEnvironmentController>() == null)
                gameObject.AddComponent<CityTestFlightEnvironmentController>();
        }

        void RefreshProviders()
        {
            naturalEnvironment = null;
            cityEnvironment = null;
            MonoBehaviour[] behaviours = FindObjectsOfType<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null || behaviour == this ||
                    !(behaviour is IGridFlightEnvironment candidate))
                {
                    continue;
                }
                if (behaviour is CityTestFlightEnvironmentController)
                    cityEnvironment = candidate;
                else if (behaviour is PlanetLabFlightEnvironmentController)
                    naturalEnvironment = candidate;
            }
            ActiveArena?.SetMode(combatMode);
        }

        void EnsureSelectorUi()
        {
            if (GetComponent<FlightEnvironmentSelectorOverlay>() == null)
                gameObject.AddComponent<FlightEnvironmentSelectorOverlay>();
        }

        string GetSettingsPath()
        {
            try
            {
                var store = new ModularBlueprintStore();
                string directory = GalaxySaveSlotService.GetSpacecraftDirectory(
                    store.ActiveSlotId);
                Directory.CreateDirectory(directory);
                return Path.Combine(directory, "lab_settings.json");
            }
            catch (Exception)
            {
                string directory = Path.Combine(
                    Application.persistentDataPath,
                    "spacecraft");
                Directory.CreateDirectory(directory);
                return Path.Combine(directory, "lab_settings.json");
            }
        }

        void LoadSettings()
        {
            selectedKind = FlightEnvironmentKind.Natural;
            string path = GetSettingsPath();
            if (!File.Exists(path))
                return;
            try
            {
                ModularLabSettingsData data = JsonUtility.FromJson<
                    ModularLabSettingsData>(File.ReadAllText(path));
                if (data != null)
                {
                    selectedKind = data.environment == FlightEnvironmentKind.City
                        ? FlightEnvironmentKind.City
                        : FlightEnvironmentKind.Natural;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[FlightEnvironment] 无法读取试飞场设置：" +
                    exception.Message);
            }
        }

        void SaveSettings()
        {
            string path = GetSettingsPath();
            string temporary = path + ".tmp";
            try
            {
                var data = new ModularLabSettingsData
                {
                    version = 2,
                    environment = selectedKind
                };
                File.WriteAllText(temporary, JsonUtility.ToJson(data, true));
                if (File.Exists(path))
                    File.Replace(temporary, path, null);
                else
                    File.Move(temporary, path);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[FlightEnvironment] 无法保存试飞场设置：" +
                    exception.Message);
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }
    }

    [DisallowMultipleComponent]
    public sealed class FlightEnvironmentSelectorOverlay : MonoBehaviour
    {
        FlightEnvironmentManager manager;
        Canvas canvas;
        Button naturalButton;
        Button cityButton;
        Text naturalLabel;
        Text cityLabel;
        Text stateLabel;
        global::GridFlightBridge flight;

        void Awake()
        {
            manager = GetComponent<FlightEnvironmentManager>();
            flight = FindObjectOfType<global::GridFlightBridge>(true);
            BuildUi();
            Refresh();
        }

        void Update()
        {
            if (manager == null || stateLabel == null)
                return;
            if (flight == null)
                flight = FindObjectOfType<global::GridFlightBridge>(true);
            if (canvas != null)
                canvas.enabled = flight == null || !flight.IsFlying;
            stateLabel.text = manager.PreparationState ==
                              CombatPreparationState.Warming
                ? manager.PreparationMessage + " " +
                  Mathf.RoundToInt(manager.PreparationProgress * 100f) + "%"
                : manager.PreparationState == CombatPreparationState.Failed
                    ? manager.PreparationMessage
                    : manager.PreparationState == CombatPreparationState.Ready
                        ? "已就绪"
                        : string.Empty;
        }

        public void Refresh()
        {
            if (manager == null || naturalButton == null || cityButton == null)
                return;
            bool natural = manager.SelectedKind == FlightEnvironmentKind.Natural;
            SetButtonState(naturalButton, naturalLabel, natural);
            SetButtonState(cityButton, cityLabel, !natural);
        }

        void BuildUi()
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

            GameObject panel = new GameObject(
                "MapSelectionPanel",
                typeof(RectTransform),
                typeof(Image));
            panel.transform.SetParent(canvasObject.transform, false);
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(1f, 1f);
            panelRect.pivot = new Vector2(1f, 1f);
            panelRect.anchoredPosition = new Vector2(-24f, -154f);
            panelRect.sizeDelta = new Vector2(370f, 112f);
            panel.GetComponent<Image>().color = new Color(0.015f, 0.09f, 0.13f, 0.94f);

            Text title = CreateText(panel.transform, "试飞地图", 18,
                TextAnchor.MiddleLeft);
            SetRect(title.rectTransform, new Vector2(14f, -8f),
                new Vector2(180f, 28f));
            naturalButton = CreateButton(panel.transform, "自然场景",
                new Vector2(14f, -42f), manager.SelectNatural, out naturalLabel);
            cityButton = CreateButton(panel.transform, "城市场景",
                new Vector2(190f, -42f), manager.SelectCity, out cityLabel);
            stateLabel = CreateText(panel.transform, string.Empty, 13,
                TextAnchor.MiddleLeft);
            stateLabel.color = new Color(0.55f, 0.9f, 1f, 1f);
            SetRect(stateLabel.rectTransform, new Vector2(14f, -82f),
                new Vector2(342f, 22f));

            if (EventSystem.current == null)
                new GameObject("EventSystem", typeof(EventSystem),
                    typeof(StandaloneInputModule));
        }

        static Button CreateButton(
            Transform parent,
            string value,
            Vector2 position,
            UnityEngine.Events.UnityAction clicked,
            out Text label)
        {
            GameObject root = new GameObject(
                value,
                typeof(RectTransform),
                typeof(Image),
                typeof(Button));
            root.transform.SetParent(parent, false);
            RectTransform rect = root.GetComponent<RectTransform>();
            SetRect(rect, position, new Vector2(166f, 34f));
            Button button = root.GetComponent<Button>();
            button.onClick.AddListener(clicked);
            label = CreateText(root.transform, value, 16,
                TextAnchor.MiddleCenter);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;
            return button;
        }

        static void SetButtonState(Button button, Text label, bool selected)
        {
            button.GetComponent<Image>().color = selected
                ? new Color(0.02f, 0.58f, 0.72f, 1f)
                : new Color(0.05f, 0.24f, 0.31f, 0.96f);
            label.text = (selected ? "✓ " : string.Empty) +
                         (label == null ? string.Empty :
                             label.gameObject.transform.parent.name);
        }

        static Text CreateText(
            Transform parent,
            string value,
            int size,
            TextAnchor alignment)
        {
            GameObject textObject = new GameObject(
                "Text",
                typeof(RectTransform),
                typeof(Text));
            textObject.transform.SetParent(parent, false);
            Text text = textObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = value;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = Color.white;
            return text;
        }

        static void SetRect(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }

    internal static class FlightEnvironmentRuntimeBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            HandleSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!scene.IsValid() ||
                scene.name.IndexOf(
                    "ModularAssemblyLab",
                    StringComparison.OrdinalIgnoreCase) < 0 ||
                UnityEngine.Object.FindObjectOfType<
                    FlightEnvironmentManager>(true) != null)
            {
                return;
            }
            var host = new GameObject("FlightEnvironmentManager");
            SceneManager.MoveGameObjectToScene(host, scene);
            host.AddComponent<FlightEnvironmentManager>();
        }
    }
}
