using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityPlanet.SpaceStation;

public sealed class PauseMenuController : MonoBehaviour
{
    [SerializeField] GameObject pauseRoot;
    [SerializeField] GameObject mainPanel;
    [SerializeField] GameObject settingsPanel;
    [SerializeField] Slider volumeSlider;
    [SerializeField] Slider sensitivitySlider;
    [SerializeField] Toggle fullscreenToggle;
    [SerializeField] string startMenuSceneName = "StartMenu";
    [SerializeField] string spaceStationSceneName =
        "SpaceStationUpgradeTest";

    VoxelPlanetPlayerController playerController;
    Button returnToStationButton;
    Button resolutionButton;
    Text resolutionLabel;
    readonly List<Vector2Int> availableResolutions =
        new List<Vector2Int>();
    bool playerControllerWasEnabled;
    float previousTimeScale = 1f;
    bool paused;
    bool transitionStarted;

    public static bool IsPaused { get; private set; }

    void Awake()
    {
        playerController = FindObjectOfType<VoxelPlanetPlayerController>();
        BuildMainPanelActions();
        NormalizeSettingsLayout();
        volumeSlider.SetValueWithoutNotify(PlayerPrefs.GetFloat("MasterVolume", AudioListener.volume));
        sensitivitySlider.SetValueWithoutNotify(PlayerPrefs.GetFloat("MouseSensitivity", playerController != null ? playerController.LookSpeed : 2f));
        DisplayResolutionSettings.ApplySavedSettings();
        bool fullscreen = PlayerPrefs.GetInt(
            "Fullscreen",
            Screen.fullScreen ? 1 : 0) != 0;
        fullscreenToggle.SetIsOnWithoutNotify(fullscreen);
        ApplyVolume(volumeSlider.value);
        ApplySensitivity(sensitivitySlider.value);
        pauseRoot.SetActive(false);
        settingsPanel.SetActive(false);
    }

    void Update()
    {
        if (!Input.GetKeyDown(KeyCode.Escape))
            return;

        if (paused && settingsPanel.activeSelf)
        {
            ShowMainPanel();
            return;
        }

        SetPaused(!paused);
    }

    void OnDestroy()
    {
        if (paused)
            RestoreGameState();
    }

    public void ContinueGame()
    {
SetPaused(false);
    
}

    public void OpenSettings()
    {
        mainPanel.SetActive(false);
        settingsPanel.SetActive(true);
        RefreshResolutionLabel();
    
}

    public void ShowMainPanel()
    {
settingsPanel.SetActive(false);
        mainPanel.SetActive(true);
    
}

    public void ApplyVolume(float value)
    {
value = Mathf.Clamp01(value);
        AudioListener.volume = value;
        PlayerPrefs.SetFloat("MasterVolume", value);
    
}

    public void ApplySensitivity(float value)
    {
value = Mathf.Clamp(value, 0.2f, 5f);
        if (playerController == null)
            playerController = FindObjectOfType<VoxelPlanetPlayerController>();
        if (playerController != null)
            playerController.LookSpeed = value;
        PlayerPrefs.SetFloat("MouseSensitivity", value);
    
}

    public void ApplyFullscreen(bool fullscreen)
    {
        DisplayResolutionSettings.Apply(
            PlayerPrefs.GetInt("ResolutionWidth", Screen.width),
            PlayerPrefs.GetInt("ResolutionHeight", Screen.height),
            fullscreen);
    
}

    public void SelectNextResolution()
    {
        BuildResolutionList();
        if (availableResolutions.Count == 0)
            return;
        int current = availableResolutions.FindIndex(value =>
            value.x == PlayerPrefs.GetInt(
                "ResolutionWidth",
                Screen.width) &&
            value.y == PlayerPrefs.GetInt(
                "ResolutionHeight",
                Screen.height));
        int next = (current + 1 + availableResolutions.Count) %
                   availableResolutions.Count;
        Vector2Int resolution = availableResolutions[next];
        DisplayResolutionSettings.Apply(
            resolution.x,
            resolution.y,
            fullscreenToggle != null
                ? fullscreenToggle.isOn
                : Screen.fullScreen);
        RefreshResolutionLabel(
            resolution.x,
            resolution.y);
    }

    public void ExitToMainMenu()
    {
        if (transitionStarted)
            return;
        transitionStarted = true;
        RestoreGameState();
        GalaxyTravelManager manager = GalaxyTravelManager.Instance;
        if (manager != null)
            manager.ReturnToMainMenu(startMenuSceneName);
        else
            SceneManager.LoadScene(startMenuSceneName, LoadSceneMode.Single);
    }

    public void ReturnToSpaceStation()
    {
        if (transitionStarted)
            return;

        transitionStarted = true;
        if (returnToStationButton != null)
            returnToStationButton.interactable = false;

        FinitePlanetHordeCombatController combat =
            FindObjectOfType<FinitePlanetHordeCombatController>();
        RestoreGameState();
        if (combat != null &&
            combat.BeginVoluntaryReturnSettlement())
        {
            return;
        }
        PlanetOrbitChapterSelectionContext.Clear();
        SpaceStationFlowContext.PrepareOrbitalReturnToStation();
        SceneManager.LoadScene(
            string.IsNullOrWhiteSpace(spaceStationSceneName)
                ? "SpaceStationUpgradeTest"
                : spaceStationSceneName,
            LoadSceneMode.Single);
    }

    void BuildMainPanelActions()
    {
        if (mainPanel == null)
            return;

        RectTransform continueAction = FindMainAction(
            "ContinueButton");
        RectTransform settingsAction = FindMainAction(
            "SettingsButton");
        RectTransform exitAction = FindMainAction(
            "MainMenuButton");
        if (continueAction == null || settingsAction == null ||
            exitAction == null)
        {
            Debug.LogWarning(
                "Pause menu actions are incomplete; " +
                "the station return button was not created.",
                this);
            return;
        }

        Transform existing = mainPanel.transform.Find(
            "ReturnToStationButton");
        RectTransform returnAction;
        if (existing == null)
        {
            GameObject instance = Instantiate(
                settingsAction.gameObject,
                mainPanel.transform,
                false);
            instance.name = "ReturnToStationButton";
            returnAction = instance.GetComponent<RectTransform>();
        }
        else
        {
            returnAction = existing as RectTransform;
        }

        if (returnAction == null)
            return;

        returnAction.SetSiblingIndex(
            settingsAction.GetSiblingIndex() + 1);
        returnToStationButton =
            returnAction.GetComponent<Button>();
        if (returnToStationButton == null)
            return;

        returnToStationButton.onClick =
            new Button.ButtonClickedEvent();
        returnToStationButton.onClick.AddListener(
            ReturnToSpaceStation);
        Text returnLabel =
            returnAction.GetComponentInChildren<Text>(true);
        if (returnLabel != null)
            returnLabel.text = "返回空间站";

        LayoutMainAction(continueAction, 82f);
        LayoutMainAction(settingsAction, -12f);
        LayoutMainAction(returnAction, -106f);
        LayoutMainAction(exitAction, -200f);
    }

    RectTransform FindMainAction(string objectName)
    {
        Transform child = mainPanel.transform.Find(objectName);
        return child as RectTransform;
    }

    static void LayoutMainAction(
        RectTransform action,
        float anchoredY)
    {
        if (action == null)
            return;

        action.anchorMin = new Vector2(0.5f, 0.5f);
        action.anchorMax = new Vector2(0.5f, 0.5f);
        action.pivot = new Vector2(0.5f, 0.5f);
        action.anchoredPosition = new Vector2(0f, anchoredY);
        action.sizeDelta = new Vector2(430f, 76f);

        Text label = action.GetComponentInChildren<Text>(true);
        if (label == null)
            return;

        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(18f, 8f);
        labelRect.offsetMax = new Vector2(-18f, -8f);
        label.alignment = TextAnchor.MiddleCenter;
        label.resizeTextForBestFit = true;
        label.resizeTextMinSize = 18;
        label.resizeTextMaxSize = Mathf.Max(18, label.fontSize);
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Truncate;
    }

    void NormalizeSettingsLayout()
    {
        if (settingsPanel == null)
            return;

        RectTransform panel = settingsPanel.GetComponent<RectTransform>();
        if (panel != null)
            panel.sizeDelta = new Vector2(760f, 620f);

        Transform titleTransform = settingsPanel.transform.Find("Title");
        LayoutSettingsElement(
            titleTransform as RectTransform,
            new Vector2(0f, 240f),
            new Vector2(620f, 58f));
        ConfigureSettingsText(
            titleTransform == null
                ? null
                : titleTransform.GetComponent<Text>(),
            20,
            32);

        LayoutSettingsLabel("VolumeLabel", "主音量", 145f);
        LayoutSettingsSlider(volumeSlider, 145f);
        LayoutSettingsLabel(
            "SensitivityLabel",
            "视角灵敏度",
            65f);
        LayoutSettingsSlider(sensitivitySlider, 65f);
        LayoutSettingsLabel(
            "FullscreenLabel",
            "显示模式",
            -15f);
        LayoutSettingsElement(
            fullscreenToggle == null
                ? null
                : fullscreenToggle.GetComponent<RectTransform>(),
            new Vector2(110f, -15f),
            new Vector2(64f, 52f));

        Text fullscreenLabel = settingsPanel.transform
            .Find("FullscreenLabel")?.GetComponent<Text>();
        Transform resolutionLabelTransform =
            settingsPanel.transform.Find("ResolutionLabel");
        if (resolutionLabelTransform == null && fullscreenLabel != null)
        {
            resolutionLabelTransform = Instantiate(
                fullscreenLabel.gameObject,
                settingsPanel.transform,
                false).transform;
            resolutionLabelTransform.name = "ResolutionLabel";
        }
        LayoutSettingsLabel("ResolutionLabel", "分辨率", -95f);

        Button backButton = settingsPanel.transform.Find("BackButton")
            ?.GetComponent<Button>();
        Transform resolutionTransform =
            settingsPanel.transform.Find("ResolutionButton");
        if (resolutionTransform == null && backButton != null)
        {
            resolutionTransform = Instantiate(
                backButton.gameObject,
                settingsPanel.transform,
                false).transform;
            resolutionTransform.name = "ResolutionButton";
        }
        if (resolutionTransform != null)
        {
            resolutionButton = resolutionTransform.GetComponent<Button>();
            resolutionButton.onClick = new Button.ButtonClickedEvent();
            resolutionButton.onClick.AddListener(SelectNextResolution);
            resolutionLabel = resolutionTransform
                .GetComponentInChildren<Text>(true);
            LayoutSettingsElement(
                resolutionTransform as RectTransform,
                new Vector2(110f, -95f),
                new Vector2(360f, 58f));
            ConfigureSettingsText(resolutionLabel, 16, 22);
        }

        Transform vsyncLabelTransform =
            settingsPanel.transform.Find("VsyncLabel");
        if (vsyncLabelTransform != null)
            vsyncLabelTransform.gameObject.SetActive(false);
        Transform vsyncTransform =
            settingsPanel.transform.Find("VsyncToggle");
        if (vsyncTransform != null)
            vsyncTransform.gameObject.SetActive(false);

        if (backButton != null)
        {
            LayoutSettingsElement(
                backButton.GetComponent<RectTransform>(),
                new Vector2(0f, -235f),
                new Vector2(360f, 68f));
            ConfigureSettingsText(
                backButton.GetComponentInChildren<Text>(true),
                17,
                24);
        }
        BuildResolutionList();
        RefreshResolutionLabel();
    }

    void LayoutSettingsLabel(
        string objectName,
        string value,
        float y)
    {
        Text text = settingsPanel.transform.Find(objectName)
            ?.GetComponent<Text>();
        if (text == null)
            return;
        text.text = value;
        LayoutSettingsElement(
            text.rectTransform,
            new Vector2(-220f, y),
            new Vector2(210f, 46f));
        ConfigureSettingsText(text, 16, 22);
        text.alignment = TextAnchor.MiddleLeft;
    }

    static void LayoutSettingsSlider(Slider slider, float y)
    {
        if (slider == null)
            return;
        LayoutSettingsElement(
            slider.GetComponent<RectTransform>(),
            new Vector2(110f, y),
            new Vector2(360f, 48f));
        Transform handle = slider.transform.Find(
            "Handle Slide Area/Handle");
        RectTransform handleRect = handle as RectTransform;
        if (handleRect != null)
            handleRect.sizeDelta = new Vector2(14f, 26f);
    }

    static void LayoutSettingsElement(
        RectTransform rect,
        Vector2 position,
        Vector2 size)
    {
        if (rect == null)
            return;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    static void ConfigureSettingsText(
        Text text,
        int minimum,
        int maximum)
    {
        if (text == null)
            return;
        text.alignment = TextAnchor.MiddleCenter;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = minimum;
        text.resizeTextMaxSize = maximum;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
    }

    void BuildResolutionList()
    {
        availableResolutions.Clear();
        availableResolutions.AddRange(
            DisplayResolutionSettings.BuildAvailableList());
    }

    void RefreshResolutionLabel()
    {
        RefreshResolutionLabel(
            PlayerPrefs.GetInt("ResolutionWidth", Screen.width),
            PlayerPrefs.GetInt("ResolutionHeight", Screen.height));
    }

    void RefreshResolutionLabel(int width, int height)
    {
        if (resolutionLabel != null)
            resolutionLabel.text = width + " × " + height + "  ›";
    }

    void SetPaused(bool shouldPause)
    {
        if (paused == shouldPause)
            return;

        paused = shouldPause;
        IsPaused = shouldPause;
        if (shouldPause)
        {
            InventoryUI inventoryUI = FindObjectOfType<InventoryUI>();
            inventoryUI?.CloseInventory();
            previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            if (playerController == null)
                playerController = FindObjectOfType<VoxelPlanetPlayerController>();
            playerControllerWasEnabled = playerController != null && playerController.enabled;
            if (playerController != null)
                playerController.enabled = false;
            pauseRoot.SetActive(true);
            ShowMainPanel();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            RestoreGameState();
        }
    }

    void RestoreGameState()
    {
        paused = false;
        IsPaused = false;
        Time.timeScale = previousTimeScale > 0f ? previousTimeScale : 1f;
        pauseRoot.SetActive(false);
        settingsPanel.SetActive(false);
        if (playerController != null)
            playerController.enabled = playerControllerWasEnabled;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        PlayerPrefs.Save();
    }
}
