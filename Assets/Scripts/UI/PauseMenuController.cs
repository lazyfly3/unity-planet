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
    bool playerControllerWasEnabled;
    float previousTimeScale = 1f;
    bool paused;
    bool transitionStarted;

    public static bool IsPaused { get; private set; }

    void Awake()
    {
        playerController = FindObjectOfType<VoxelPlanetPlayerController>();
        BuildMainPanelActions();
        volumeSlider.SetValueWithoutNotify(PlayerPrefs.GetFloat("MasterVolume", AudioListener.volume));
        sensitivitySlider.SetValueWithoutNotify(PlayerPrefs.GetFloat("MouseSensitivity", playerController != null ? playerController.LookSpeed : 2f));
        fullscreenToggle.SetIsOnWithoutNotify(Screen.fullScreen);
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
Screen.fullScreen = fullscreen;
        PlayerPrefs.SetInt("Fullscreen", fullscreen ? 1 : 0);
    
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
        combat?.AbandonForStationReturn();

        RestoreGameState();
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
