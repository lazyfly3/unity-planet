using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class PauseMenuController : MonoBehaviour
{
    [SerializeField] GameObject pauseRoot;
    [SerializeField] GameObject mainPanel;
    [SerializeField] GameObject settingsPanel;
    [SerializeField] Slider volumeSlider;
    [SerializeField] Slider sensitivitySlider;
    [SerializeField] Toggle fullscreenToggle;
    [SerializeField] string startMenuSceneName = "StartMenu";

    VoxelPlanetPlayerController playerController;
    bool playerControllerWasEnabled;
    float previousTimeScale = 1f;
    bool paused;

    public static bool IsPaused { get; private set; }

    void Awake()
    {
        playerController = FindObjectOfType<VoxelPlanetPlayerController>();
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
RestoreGameState();
        GalaxyTravelManager manager = GalaxyTravelManager.Instance;
        if (manager != null)
            manager.ReturnToMainMenu(startMenuSceneName);
        else
            SceneManager.LoadScene(startMenuSceneName, LoadSceneMode.Single);
    
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
