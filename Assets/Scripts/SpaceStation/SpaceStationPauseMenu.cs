using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace UnityPlanet.SpaceStation
{
    /// <summary>
    /// Runtime-owned pause menu for the walkable space-station scene. Keeping
    /// the interface self-contained avoids coupling the station scene to the
    /// planet-only PauseMenuController hierarchy.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpaceStationPauseMenu : MonoBehaviour
    {
        const string StationSceneName = "SpaceStationUpgradeTest";
        const string StartMenuSceneName = "StartMenu";
        const int SortingOrder = 9000;

        readonly List<Vector2Int> availableResolutions =
            new List<Vector2Int>();

        SpaceStationFirstPersonController firstPersonController;
        GameObject menuRoot;
        GameObject mainPanel;
        GameObject settingsPanel;
        Slider volumeSlider;
        Slider sensitivitySlider;
        Text volumeValue;
        Text sensitivityValue;
        Text resolutionValue;
        Text displayModeValue;
        Button continueButton;
        Button settingsBackButton;

        bool menuOpen;
        bool transitionStarted;
        bool firstPersonWasEnabled;
        float previousTimeScale = 1f;
        bool previousAudioPause;
        CursorLockMode previousCursorLock;
        bool previousCursorVisible;

        public static bool IsInstalled { get; private set; }
        public static bool IsOpen { get; private set; }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            IsInstalled = false;
            IsOpen = false;
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RegisterSceneLoadListener()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.AfterSceneLoad)]
        static void InstallForInitialScene()
        {
            TryInstall(SceneManager.GetActiveScene().name);
        }

        static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene == SceneManager.GetActiveScene())
                TryInstall(scene.name);
        }

        static void TryInstall(string sceneName)
        {
            if (!ShouldInstall(sceneName) ||
                FindObjectOfType<SpaceStationPauseMenu>() != null)
            {
                return;
            }

            new GameObject("太空站暂停菜单")
                .AddComponent<SpaceStationPauseMenu>();
        }

        public static bool ShouldInstall(string sceneName)
        {
            return string.Equals(
                sceneName,
                StationSceneName,
                System.StringComparison.Ordinal);
        }

        public static bool ShouldOpenForEscape(
            bool escapePressed,
            bool firstPersonInputAvailable)
        {
            return escapePressed && firstPersonInputAvailable;
        }

        void Awake()
        {
            IsInstalled = true;
            firstPersonController = FindObjectOfType<
                SpaceStationFirstPersonController>();
            EnsureEventSystem();
            BuildInterface();
        }

        void Update()
        {
            if (transitionStarted)
                return;

            if (menuOpen)
            {
                if (!Input.GetKeyDown(KeyCode.Escape))
                    return;
                if (settingsPanel.activeSelf)
                    ShowMainPanel();
                else
                    CloseMenu();
                return;
            }

            if (firstPersonController == null)
            {
                firstPersonController = FindObjectOfType<
                    SpaceStationFirstPersonController>();
            }

            if (ShouldOpenForEscape(
                    Input.GetKeyDown(KeyCode.Escape),
                    firstPersonController != null &&
                    firstPersonController.isActiveAndEnabled))
            {
                OpenMenu();
            }
        }

        void OnDisable()
        {
            if (menuOpen)
                RestoreGameState();
        }

        void OnDestroy()
        {
            if (menuOpen)
                RestoreGameState();
            IsInstalled = false;
            IsOpen = false;
        }

        public void OpenMenu()
        {
            if (menuOpen || transitionStarted ||
                firstPersonController == null ||
                !firstPersonController.isActiveAndEnabled)
            {
                return;
            }

            menuOpen = true;
            IsOpen = true;
            previousTimeScale = Time.timeScale;
            previousAudioPause = AudioListener.pause;
            previousCursorLock = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            firstPersonWasEnabled = firstPersonController.enabled;

            Time.timeScale = 0f;
            AudioListener.pause = true;
            firstPersonController.enabled = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            menuRoot.SetActive(true);
            ShowMainPanel();
        }

        public void CloseMenu()
        {
            if (!menuOpen)
                return;
            RestoreGameState();
        }

        public void OpenSettings()
        {
            if (!menuOpen)
                return;
            RefreshSettingsControls();
            mainPanel.SetActive(false);
            settingsPanel.SetActive(true);
            Select(settingsBackButton);
        }

        public void ShowMainPanel()
        {
            PlayerPrefs.Save();
            settingsPanel.SetActive(false);
            mainPanel.SetActive(true);
            Select(continueButton);
        }

        public void ExitToMainMenu()
        {
            if (transitionStarted)
                return;

            transitionStarted = true;
            PlayerPrefs.Save();
            RestoreGameState();
            GalaxyTravelManager manager = GalaxyTravelManager.Instance;
            if (manager != null)
                manager.ReturnToMainMenu(StartMenuSceneName);
            else
                SceneManager.LoadScene(
                    StartMenuSceneName,
                    LoadSceneMode.Single);
        }

        public void ApplyVolume(float value)
        {
            value = Mathf.Clamp01(value);
            AudioListener.volume = value;
            PlayerPrefs.SetFloat("MasterVolume", value);
            if (volumeValue != null)
                volumeValue.text = Mathf.RoundToInt(value * 100f) + "%";
        }

        public void ApplySensitivity(float value)
        {
            value = Mathf.Clamp(value, 0.2f, 5f);
            PlayerPrefs.SetFloat("MouseSensitivity", value);
            if (firstPersonController != null)
                firstPersonController.MouseSensitivity = value;
            if (sensitivityValue != null)
                sensitivityValue.text = value.ToString("0.0");
        }

        public void SelectNextResolution()
        {
            BuildResolutionList();
            if (availableResolutions.Count == 0)
                return;

            int width = PlayerPrefs.GetInt(
                "ResolutionWidth",
                Screen.width);
            int height = PlayerPrefs.GetInt(
                "ResolutionHeight",
                Screen.height);
            int current = availableResolutions.FindIndex(value =>
                value.x == width && value.y == height);
            int next = (current + 1 + availableResolutions.Count) %
                       availableResolutions.Count;
            Vector2Int selected = availableResolutions[next];
            DisplayResolutionSettings.Apply(
                selected.x,
                selected.y,
                Screen.fullScreen);
            RefreshResolutionValue(selected.x, selected.y);
        }

        public void ToggleDisplayMode()
        {
            bool fullscreen = !Screen.fullScreen;
            DisplayResolutionSettings.Apply(
                PlayerPrefs.GetInt("ResolutionWidth", Screen.width),
                PlayerPrefs.GetInt("ResolutionHeight", Screen.height),
                fullscreen);
            RefreshDisplayModeValue(fullscreen);
        }

        void RestoreGameState()
        {
            menuOpen = false;
            IsOpen = false;
            Time.timeScale = previousTimeScale;
            AudioListener.pause = previousAudioPause;
            if (menuRoot != null)
                menuRoot.SetActive(false);

            if (firstPersonController != null && firstPersonWasEnabled)
                firstPersonController.enabled = true;
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
        }

        void RefreshSettingsControls()
        {
            float volume = Mathf.Clamp01(PlayerPrefs.GetFloat(
                "MasterVolume",
                AudioListener.volume));
            float sensitivity = Mathf.Clamp(
                PlayerPrefs.GetFloat(
                    "MouseSensitivity",
                    firstPersonController != null
                        ? firstPersonController.MouseSensitivity
                        : 2f),
                0.2f,
                5f);
            volumeSlider.SetValueWithoutNotify(volume);
            sensitivitySlider.SetValueWithoutNotify(sensitivity);
            ApplyVolume(volume);
            ApplySensitivity(sensitivity);
            RefreshResolutionValue(
                PlayerPrefs.GetInt("ResolutionWidth", Screen.width),
                PlayerPrefs.GetInt("ResolutionHeight", Screen.height));
            RefreshDisplayModeValue(Screen.fullScreen);
        }

        void BuildResolutionList()
        {
            availableResolutions.Clear();
            availableResolutions.AddRange(
                DisplayResolutionSettings.BuildAvailableList());
        }

        void RefreshResolutionValue(int width, int height)
        {
            if (resolutionValue != null)
                resolutionValue.text = width + " × " + height;
        }

        void RefreshDisplayModeValue(bool fullscreen)
        {
            if (displayModeValue != null)
            {
                displayModeValue.text = fullscreen
                    ? "全屏模式"
                    : "窗口模式";
            }
        }

        void BuildInterface()
        {
            Font font = Resources.GetBuiltinResource<Font>(
                "LegacyRuntime.ttf");

            GameObject canvasObject = new GameObject(
                "太空站暂停画布",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode =
                CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            menuRoot = CreateImage(
                canvasObject.transform,
                "暂停遮罩",
                new Color(0.004f, 0.012f, 0.024f, 0.84f));
            Stretch(menuRoot.GetComponent<RectTransform>());

            mainPanel = CreatePanel(
                menuRoot.transform,
                "主菜单",
                new Vector2(660f, 540f));
            CreateText(
                mainPanel.transform,
                "标题",
                "太空站暂停",
                font,
                40,
                new Vector2(0f, 190f),
                new Vector2(560f, 62f),
                new Color(0.28f, 0.9f, 1f));
            CreateText(
                mainPanel.transform,
                "说明",
                "太空站活动已暂停",
                font,
                21,
                new Vector2(0f, 132f),
                new Vector2(540f, 38f),
                new Color(0.72f, 0.86f, 0.93f));

            continueButton = CreateButton(
                mainPanel.transform,
                "继续游戏按钮",
                "继续游戏",
                font,
                new Vector2(0f, 48f),
                new Color(0.06f, 0.48f, 0.62f),
                CloseMenu);
            CreateButton(
                mainPanel.transform,
                "设置按钮",
                "设置",
                font,
                new Vector2(0f, -36f),
                new Color(0.09f, 0.31f, 0.43f),
                OpenSettings);
            CreateButton(
                mainPanel.transform,
                "退出主界面按钮",
                "退出至主界面",
                font,
                new Vector2(0f, -120f),
                new Color(0.43f, 0.17f, 0.14f),
                ExitToMainMenu);
            CreateText(
                mainPanel.transform,
                "提示",
                "ESC：继续游戏",
                font,
                17,
                new Vector2(0f, -205f),
                new Vector2(520f, 32f),
                new Color(0.48f, 0.64f, 0.72f));

            settingsPanel = CreatePanel(
                menuRoot.transform,
                "设置菜单",
                new Vector2(760f, 700f));
            CreateText(
                settingsPanel.transform,
                "设置标题",
                "设置",
                font,
                38,
                new Vector2(0f, 278f),
                new Vector2(620f, 58f),
                new Color(0.28f, 0.9f, 1f));

            CreateSettingLabel(
                settingsPanel.transform,
                "主音量",
                font,
                174f);
            volumeSlider = CreateSlider(
                settingsPanel.transform,
                "主音量滑杆",
                new Vector2(45f, 126f),
                0f,
                1f);
            volumeSlider.onValueChanged.AddListener(ApplyVolume);
            volumeValue = CreateText(
                settingsPanel.transform,
                "主音量值",
                string.Empty,
                font,
                20,
                new Vector2(280f, 126f),
                new Vector2(90f, 38f),
                Color.white);

            CreateSettingLabel(
                settingsPanel.transform,
                "视角灵敏度",
                font,
                62f);
            sensitivitySlider = CreateSlider(
                settingsPanel.transform,
                "灵敏度滑杆",
                new Vector2(45f, 14f),
                0.2f,
                5f);
            sensitivitySlider.onValueChanged.AddListener(ApplySensitivity);
            sensitivityValue = CreateText(
                settingsPanel.transform,
                "灵敏度值",
                string.Empty,
                font,
                20,
                new Vector2(280f, 14f),
                new Vector2(90f, 38f),
                Color.white);

            CreateSettingLabel(
                settingsPanel.transform,
                "分辨率",
                font,
                -50f);
            Button resolutionButton = CreateButton(
                settingsPanel.transform,
                "分辨率按钮",
                string.Empty,
                font,
                new Vector2(100f, -92f),
                new Color(0.08f, 0.30f, 0.42f),
                SelectNextResolution,
                new Vector2(400f, 58f));
            resolutionValue = resolutionButton.GetComponentInChildren<Text>();

            CreateSettingLabel(
                settingsPanel.transform,
                "显示模式",
                font,
                -162f);
            Button displayModeButton = CreateButton(
                settingsPanel.transform,
                "显示模式按钮",
                string.Empty,
                font,
                new Vector2(100f, -204f),
                new Color(0.08f, 0.30f, 0.42f),
                ToggleDisplayMode,
                new Vector2(400f, 58f));
            displayModeValue = displayModeButton
                .GetComponentInChildren<Text>();

            settingsBackButton = CreateButton(
                settingsPanel.transform,
                "返回按钮",
                "返回",
                font,
                new Vector2(0f, -292f),
                new Color(0.08f, 0.48f, 0.62f),
                ShowMainPanel,
                new Vector2(360f, 62f));

            settingsPanel.SetActive(false);
            menuRoot.SetActive(false);
            RefreshSettingsControls();
        }

        static GameObject CreatePanel(
            Transform parent,
            string name,
            Vector2 size)
        {
            GameObject panel = CreateImage(
                parent,
                name,
                new Color(0.022f, 0.065f, 0.105f, 0.985f));
            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            Outline outline = panel.AddComponent<Outline>();
            outline.effectColor = new Color(0.1f, 0.62f, 0.76f, 0.7f);
            outline.effectDistance = new Vector2(2f, -2f);
            return panel;
        }

        static GameObject CreateImage(
            Transform parent,
            string name,
            Color color)
        {
            GameObject instance = new GameObject(
                name,
                typeof(RectTransform),
                typeof(Image));
            instance.transform.SetParent(parent, false);
            instance.GetComponent<Image>().color = color;
            return instance;
        }

        static Text CreateText(
            Transform parent,
            string name,
            string value,
            Font font,
            int fontSize,
            Vector2 position,
            Vector2 size,
            Color color)
        {
            GameObject instance = new GameObject(
                name,
                typeof(RectTransform),
                typeof(Text));
            instance.transform.SetParent(parent, false);
            RectTransform rect = instance.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            Text text = instance.GetComponent<Text>();
            text.font = font;
            text.text = value;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = Mathf.Max(13, fontSize - 5);
            text.resizeTextMaxSize = fontSize;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        static void CreateSettingLabel(
            Transform parent,
            string value,
            Font font,
            float y)
        {
            Text label = CreateText(
                parent,
                value + "标签",
                value,
                font,
                21,
                new Vector2(-245f, y),
                new Vector2(190f, 42f),
                new Color(0.77f, 0.9f, 0.96f));
            label.alignment = TextAnchor.MiddleLeft;
        }

        static Button CreateButton(
            Transform parent,
            string name,
            string label,
            Font font,
            Vector2 position,
            Color color,
            UnityEngine.Events.UnityAction action,
            Vector2? customSize = null)
        {
            GameObject instance = new GameObject(
                name,
                typeof(RectTransform),
                typeof(Image),
                typeof(Button));
            instance.transform.SetParent(parent, false);
            RectTransform rect = instance.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = customSize ?? new Vector2(430f, 64f);
            Image image = instance.GetComponent<Image>();
            image.color = color;
            Button button = instance.GetComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            colors.pressedColor = new Color(0.78f, 0.86f, 0.9f, 1f);
            colors.selectedColor = colors.highlightedColor;
            button.colors = colors;
            button.onClick.AddListener(action);
            CreateText(
                instance.transform,
                "文字",
                label,
                font,
                23,
                Vector2.zero,
                rect.sizeDelta - new Vector2(30f, 12f),
                Color.white);
            return button;
        }

        static Slider CreateSlider(
            Transform parent,
            string name,
            Vector2 position,
            float minimum,
            float maximum)
        {
            GameObject sliderObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(Slider));
            sliderObject.transform.SetParent(parent, false);
            RectTransform sliderRect =
                sliderObject.GetComponent<RectTransform>();
            sliderRect.anchorMin = sliderRect.anchorMax =
                new Vector2(0.5f, 0.5f);
            sliderRect.pivot = new Vector2(0.5f, 0.5f);
            sliderRect.anchoredPosition = position;
            sliderRect.sizeDelta = new Vector2(390f, 42f);

            GameObject background = CreateImage(
                sliderObject.transform,
                "背景",
                new Color(0.015f, 0.03f, 0.05f, 1f));
            RectTransform backgroundRect =
                background.GetComponent<RectTransform>();
            backgroundRect.anchorMin = new Vector2(0f, 0.35f);
            backgroundRect.anchorMax = new Vector2(1f, 0.65f);
            backgroundRect.offsetMin = new Vector2(8f, 0f);
            backgroundRect.offsetMax = new Vector2(-8f, 0f);

            GameObject fillArea = new GameObject(
                "填充区域",
                typeof(RectTransform));
            fillArea.transform.SetParent(sliderObject.transform, false);
            Stretch(fillArea.GetComponent<RectTransform>(), 14f, 14f);
            GameObject fill = CreateImage(
                fillArea.transform,
                "填充",
                new Color(0.12f, 0.78f, 0.9f, 1f));
            Stretch(fill.GetComponent<RectTransform>());

            GameObject handleArea = new GameObject(
                "滑块区域",
                typeof(RectTransform));
            handleArea.transform.SetParent(sliderObject.transform, false);
            Stretch(handleArea.GetComponent<RectTransform>(), 14f, 14f);
            GameObject handle = CreateImage(
                handleArea.transform,
                "滑块",
                new Color(0.82f, 0.97f, 1f, 1f));
            RectTransform handleRect = handle.GetComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(24f, 34f);

            Slider slider = sliderObject.GetComponent<Slider>();
            slider.minValue = minimum;
            slider.maxValue = maximum;
            slider.direction = Slider.Direction.LeftToRight;
            slider.fillRect = fill.GetComponent<RectTransform>();
            slider.handleRect = handleRect;
            slider.targetGraphic = handle.GetComponent<Image>();
            return slider;
        }

        static void Stretch(
            RectTransform rect,
            float horizontalInset = 0f,
            float verticalInset = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(
                horizontalInset,
                verticalInset);
            rect.offsetMax = new Vector2(
                -horizontalInset,
                -verticalInset);
        }

        static void Select(Selectable selectable)
        {
            if (EventSystem.current != null && selectable != null)
            {
                EventSystem.current.SetSelectedGameObject(
                    selectable.gameObject);
            }
        }

        static void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null)
                return;
            new GameObject(
                "EventSystem",
                typeof(EventSystem),
                typeof(StandaloneInputModule));
        }
    }
}
