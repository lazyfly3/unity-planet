using System;
using System.Collections;
using System.IO;
using ModularAssembly;
using UnityEngine;
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
        public int version = 3;
        public FlightEnvironmentKind environment = FlightEnvironmentKind.City;
    }

    /// <summary>
    /// Single scene-scoped city test-flight entry point used by the flight
    /// bridge. Natural remains a serialized enum value only so version-2 lab
    /// settings can migrate without corrupting older save slots.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FlightEnvironmentManager :
        MonoBehaviour,
        IGridFlightEnvironment,
        IGridFlightEnvironmentWarmup
    {
        IGridFlightEnvironment cityEnvironment;
        FlightEnvironmentKind selectedKind = FlightEnvironmentKind.City;
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

        IGridFlightEnvironment ActiveProvider => cityEnvironment;

        public static FlightEnvironmentKind CanonicalizeSelectedKind(
            FlightEnvironmentKind _)
        {
            return FlightEnvironmentKind.City;
        }

        public static bool RequiresCityTestFlightEnvironment(Scene scene)
        {
            return scene.IsValid() &&
                   scene.name.IndexOf(
                       "ModularAssemblyLab",
                       StringComparison.OrdinalIgnoreCase) >= 0;
        }

        void Awake()
        {
            LoadSettings();
            EnsureCityProvider();
            RefreshProviders();
        }

        void Start()
        {
            EnsureSelectorUi();
        }

        public void SetSelectedKind(FlightEnvironmentKind kind)
        {
            kind = CanonicalizeSelectedKind(kind);
            if (selectedKind == kind || inFlight)
                return;

            ActiveProvider?.ExitFlight();
            selectedKind = kind;
            preparationState = CombatPreparationState.Idle;
            preparationProgress = 0f;
            preparationMessage = string.Empty;
            SaveSettings();
            GetComponent<FlightEnvironmentSelectorOverlay>()?.Refresh();
        }

        public void SelectNatural()
        {
            // Compatibility entry point for older UI events and saved scenes.
            SetSelectedKind(FlightEnvironmentKind.City);
        }

        public void SelectCity()
        {
            SetSelectedKind(FlightEnvironmentKind.City);
        }

        public void ToggleSelectedKind()
        {
            SetSelectedKind(FlightEnvironmentKind.City);
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
            preparationMessage = "正在生成城市试飞场……";

            // Let the modular-lab scene bootstrap finish before city PCG starts.
            yield return null;
            EnsureCityProvider();
            RefreshProviders();
            IGridFlightEnvironment provider = ActiveProvider;
            if (provider == null)
            {
                preparationState = CombatPreparationState.Failed;
                preparationMessage = "城市试飞场组件未初始化。";
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
                    ? "城市试飞场已就绪。"
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
            cityEnvironment =
                GetComponent<CityTestFlightEnvironmentController>();
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
            selectedKind = FlightEnvironmentKind.City;
            string path = GetSettingsPath();
            if (!File.Exists(path))
                return;
            bool migrateToCity = false;
            try
            {
                ModularLabSettingsData data = JsonUtility.FromJson<
                    ModularLabSettingsData>(File.ReadAllText(path));
                if (data != null)
                {
                    migrateToCity = data.version < 3 ||
                                    data.environment !=
                                    FlightEnvironmentKind.City;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[FlightEnvironment] 无法读取试飞场设置：" +
                    exception.Message);
            }
            if (migrateToCity)
                SaveSettings();
        }

        void SaveSettings()
        {
            string path = GetSettingsPath();
            string temporary = path + ".tmp";
            try
            {
                var data = new ModularLabSettingsData
                {
                    version = 3,
                    environment = FlightEnvironmentKind.City
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
        Text fixedEnvironmentLabel;
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
                        ? "城市试飞场已就绪"
                        : "点击“试飞”后生成城市";
        }

        public void Refresh()
        {
            if (manager == null || fixedEnvironmentLabel == null)
                return;
            fixedEnvironmentLabel.text = "城市 PCG 试飞场（固定）";
        }

        void BuildUi()
        {
            GameObject canvasObject = new GameObject(
                "FlightEnvironmentSelectorCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 8400;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            GameObject panel = new GameObject(
                "CityFlightEnvironmentPanel",
                typeof(RectTransform),
                typeof(Image));
            panel.transform.SetParent(canvasObject.transform, false);
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(1f, 1f);
            panelRect.pivot = new Vector2(1f, 1f);
            panelRect.anchoredPosition = new Vector2(-24f, -154f);
            panelRect.sizeDelta = new Vector2(370f, 108f);
            Image panelImage = panel.GetComponent<Image>();
            panelImage.color = new Color(0.015f, 0.09f, 0.13f, 0.94f);
            panelImage.raycastTarget = false;

            Text title = CreateText(panel.transform, "试飞环境", 18,
                TextAnchor.MiddleLeft);
            SetRect(title.rectTransform, new Vector2(14f, -8f),
                new Vector2(180f, 28f));

            GameObject environmentCard = new GameObject(
                "城市PCG试飞场_固定",
                typeof(RectTransform),
                typeof(Image));
            environmentCard.transform.SetParent(panel.transform, false);
            SetRect(
                environmentCard.GetComponent<RectTransform>(),
                new Vector2(14f, -40f),
                new Vector2(342f, 32f));
            Image cardImage = environmentCard.GetComponent<Image>();
            cardImage.color = new Color(0.02f, 0.58f, 0.72f, 1f);
            cardImage.raycastTarget = false;
            fixedEnvironmentLabel = CreateText(
                environmentCard.transform,
                "城市 PCG 试飞场（固定）",
                16,
                TextAnchor.MiddleCenter);
            fixedEnvironmentLabel.rectTransform.anchorMin = Vector2.zero;
            fixedEnvironmentLabel.rectTransform.anchorMax = Vector2.one;
            fixedEnvironmentLabel.rectTransform.offsetMin = Vector2.zero;
            fixedEnvironmentLabel.rectTransform.offsetMax = Vector2.zero;

            stateLabel = CreateText(panel.transform, string.Empty, 13,
                TextAnchor.MiddleLeft);
            stateLabel.color = new Color(0.55f, 0.9f, 1f, 1f);
            SetRect(stateLabel.rectTransform, new Vector2(14f, -78f),
                new Vector2(342f, 22f));
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
            text.raycastTarget = false;
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
            if (!FlightEnvironmentManager
                    .RequiresCityTestFlightEnvironment(scene) ||
                ContainsManager(scene))
            {
                return;
            }
            var host = new GameObject("FlightEnvironmentManager");
            SceneManager.MoveGameObjectToScene(host, scene);
            host.AddComponent<FlightEnvironmentManager>();
        }

        static bool ContainsManager(Scene scene)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int index = 0; index < roots.Length; index++)
            {
                if (roots[index].GetComponentInChildren<
                        FlightEnvironmentManager>(true) != null)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
