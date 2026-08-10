using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityPlanet.SpaceStation;

public sealed class StartMenuController : MonoBehaviour
{
    [Header("Pages")]
    [SerializeField] GameObject titleImage;
    [SerializeField] GameObject mainPanel;
    [SerializeField] GameObject saveBrowserPanel;
    [SerializeField] GameObject createDialog;
    [SerializeField] GameObject renameDialog;
    [SerializeField] GameObject deleteDialog;

    [Header("Main Menu")]
    [SerializeField] Button startButton;
    [SerializeField] Button settingsButton;
    [SerializeField] Button quitButton;

    [Header("Save Browser")]
    [SerializeField] Transform saveListContent;
    [SerializeField] StartMenuSaveRow saveRowTemplate;
    [SerializeField] Button enterButton;
    [SerializeField] Button renameButton;
    [SerializeField] Button deleteButton;
    [SerializeField] Text statusText;

    [Header("Create Dialog")]
    [SerializeField] InputField createNameInput;
    [SerializeField] InputField createSeedInput;

    [Header("Rename Dialog")]
    [SerializeField] InputField renameInput;

    [Header("Delete Dialog")]
    [SerializeField] Text deleteMessageText;

    [Header("Scenes")]
    [SerializeField] string modularAssemblySceneName =
        "ModularAssemblyLab";
    [SerializeField] string spaceStationSceneName =
        "SpaceStationUpgradeTest";

    readonly List<StartMenuSaveRow> rows = new List<StartMenuSaveRow>();
    readonly List<Vector2Int> availableResolutions =
        new List<Vector2Int>();
    readonly List<Button> resolutionOptionButtons =
        new List<Button>();
    StartMenuSaveRow selectedRow;
    bool launchModularAssembly;
    GameObject settingsRoot;
    GameObject briefingRoot;
    Slider runtimeVolumeSlider;
    Slider runtimeSensitivitySlider;
    Text runtimeVolumeValue;
    Text runtimeSensitivityValue;
    Text runtimeResolutionValue;
    Text runtimeDisplayModeValue;
    GameObject resolutionMenuRoot;
    RectTransform resolutionOptionsRoot;
    RawImage briefingIllustration;
    Text briefingTitle;
    Text briefingBody;
    Text briefingPageIndicator;
    Button briefingPreviousButton;
    Button briefingNextButton;
    Text briefingNextButtonText;
    Texture2D[] briefingTextures;
    int briefingPage;
    string pendingBriefingSlotId;
    bool pendingBriefingForceModular;
    bool pendingBriefingInitialAssembly;

    void Awake()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        GalaxyLaunchContext.Clear();
        EnsureRuntimeMenu();
        ApplySavedSettings();
        ShowMainPage();
        SetStatus(string.Empty);
    }

    public void ShowMainPage()
    {
        titleImage.SetActive(true);
        mainPanel.SetActive(true);
        saveBrowserPanel.SetActive(false);
        CloseDialogs();
        if (settingsRoot != null)
            settingsRoot.SetActive(false);
        if (briefingRoot != null)
            briefingRoot.SetActive(false);
}

    public void ShowSaveBrowser()
    {
        launchModularAssembly = false;
        ShowSaveBrowserPage();
    }

    public void ShowModularAssemblyBrowser()
    {
        launchModularAssembly = true;
        ShowSaveBrowserPage();
    }

    void ShowSaveBrowserPage()
    {
        titleImage.SetActive(false);
        mainPanel.SetActive(false);
        saveBrowserPanel.SetActive(true);
        CloseDialogs();
        if (settingsRoot != null)
            settingsRoot.SetActive(false);
        if (briefingRoot != null)
            briefingRoot.SetActive(false);
        RefreshSaveList();
    
}

    public void OpenCreateDialog()
    {
CloseDialogs();
        createNameInput.text = "新的世界";
        createSeedInput.text = string.Empty;
        createDialog.SetActive(true);
        createNameInput.ActivateInputField();
    
}

    public void ConfirmCreate()
    {
int? seed = null;
        string seedText = createSeedInput.text.Trim();
        if (seedText.Length > 0)
        {
            if (!int.TryParse(seedText, out int parsedSeed))
            {
                SetStatus("种子必须是 -2147483648 到 2147483647 之间的整数。");
                return;
            }
            seed = parsedSeed;
        }

        try
        {
            GalaxySaveSlotMetadata metadata = GalaxySaveSlotService.CreateSlot(createNameInput.text, seed);
            GalaxyLaunchContext.SelectSlot(metadata.slotId);
            ShowGameLoopBriefing(
                metadata.slotId,
                true,
                true);
        }
        catch (Exception exception)
        {
            SetStatus("创建存档失败：" + exception.Message);
        }
    
}

    public void OpenRenameDialog()
    {
if (!HasUsableSelection())
            return;
        CloseDialogs();
        renameInput.text = selectedRow.Slot.DisplayName;
        renameDialog.SetActive(true);
        renameInput.ActivateInputField();
    
}

    public void ConfirmRename()
    {
if (!HasUsableSelection())
            return;
        try
        {
            GalaxySaveSlotService.RenameSlot(selectedRow.Slot.SlotId, renameInput.text);
            CloseDialogs();
            RefreshSaveList();
        }
        catch (Exception exception)
        {
            SetStatus("重命名失败：" + exception.Message);
        }
    
}

    public void OpenDeleteDialog()
    {
if (selectedRow == null)
            return;
        CloseDialogs();
        deleteMessageText.text = $"确定永久删除“{selectedRow.Slot.DisplayName}”吗？\n此操作无法撤销。";
        deleteDialog.SetActive(true);
    
}

    public void ConfirmDelete()
    {
if (selectedRow == null)
            return;
        try
        {
            GalaxySaveSlotService.DeleteSlot(selectedRow.Slot.SlotId);
            CloseDialogs();
            RefreshSaveList();
        }
        catch (Exception exception)
        {
            SetStatus("删除存档失败：" + exception.Message);
        }
    
}

    public void EnterSelectedWorld()
    {
if (!HasUsableSelection())
            return;
        GalaxyLaunchContext.SelectSlot(selectedRow.Slot.SlotId);
        bool requiresInitialAssembly =
            !HasSavedModularDesign(selectedRow.Slot.SlotId);
        GalaxySaveSlotMetadata metadata = selectedRow.Slot.Metadata ??
            GalaxySaveSlotService.LoadMetadata(selectedRow.Slot.SlotId);
        if (requiresInitialAssembly && metadata != null &&
            !metadata.gameLoopBriefingSeen)
        {
            ShowGameLoopBriefing(
                selectedRow.Slot.SlotId,
                true,
                true);
            return;
        }
        LoadConstructionScene(
            selectedRow.Slot.SlotId,
            launchModularAssembly || requiresInitialAssembly,
            requiresInitialAssembly);
    
}

    public void CloseDialogs()
    {
createDialog.SetActive(false);
        renameDialog.SetActive(false);
        deleteDialog.SetActive(false);
    
}

    public void SelectSlot(StartMenuSaveRow row)
    {
selectedRow = row;
        foreach (StartMenuSaveRow candidate in rows)
            candidate.SetSelected(candidate == row);
        RefreshActionState();
        SetStatus(row.Slot.IsCorrupt ? row.Slot.Error : string.Empty);
    
}

    void RefreshSaveList()
    {
        foreach (StartMenuSaveRow row in rows)
            Destroy(row.gameObject);
        rows.Clear();
        selectedRow = null;

        IReadOnlyList<GalaxySaveSlotInfo> slots = GalaxySaveSlotService.ListSlots();
        foreach (GalaxySaveSlotInfo slot in slots)
        {
            StartMenuSaveRow row = Instantiate(saveRowTemplate, saveListContent);
            row.Initialize(this, slot);
            rows.Add(row);
        }

        saveRowTemplate.gameObject.SetActive(false);
        RefreshActionState();
        SetStatus(slots.Count == 0 ? "还没有存档，创建一个新世界开始探险。" : string.Empty);
    }

    void RefreshActionState()
    {
        bool selected = selectedRow != null;
        bool usable = selected && !selectedRow.Slot.IsCorrupt;
        enterButton.interactable = usable;
        renameButton.interactable = usable;
        deleteButton.interactable = selected;
        Text enterLabel = enterButton == null
            ? null
            : enterButton.GetComponentInChildren<Text>();
        if (enterLabel != null)
        {
            enterLabel.text = "进入空间站";
        }
    }

    void LoadConstructionScene(
        string slotId,
        bool forceModular,
        bool initialAssembly = false)
    {
        if (forceModular)
        {
            if (initialAssembly)
            {
                SpaceStationFlowContext.BeginInitialAssembly();
            }
            else
            {
                SpaceStationFlowContext.BeginAssemblyFromStation();
            }
            SceneManager.LoadScene(
                string.IsNullOrWhiteSpace(modularAssemblySceneName)
                    ? "ModularAssemblyLab"
                    : modularAssemblySceneName,
                LoadSceneMode.Single);
            return;
        }

        SceneManager.LoadScene(
            string.IsNullOrWhiteSpace(spaceStationSceneName)
                ? "SpaceStationUpgradeTest"
                : spaceStationSceneName,
            LoadSceneMode.Single);
    }

    static bool HasSavedModularDesign(string slotId)
    {
        string spacecraftDirectory =
            GalaxySaveSlotService.GetSpacecraftDirectory(slotId);
        return File.Exists(Path.Combine(
            spacecraftDirectory,
            "modular_ship.json"));
    }

    bool HasUsableSelection()
    {
        return selectedRow != null && !selectedRow.Slot.IsCorrupt;
    }

    void EnsureRuntimeMenu()
    {
        Canvas menuCanvas = mainPanel == null
            ? null
            : mainPanel.GetComponentInParent<Canvas>();
        if (menuCanvas == null)
            return;

        if (EventSystem.current == null)
        {
            GameObject eventObject = new GameObject(
                "StartMenuEventSystem");
            eventObject.AddComponent<EventSystem>();
            eventObject.AddComponent<StandaloneInputModule>();
        }

        RectTransform panelRect =
            mainPanel.GetComponent<RectTransform>();
        if (panelRect != null)
        {
            panelRect.sizeDelta = new Vector2(540f, 350f);
            panelRect.anchoredPosition = new Vector2(0f, -190f);
        }

        startButton = startButton ??
            mainPanel.transform.Find("StartGameButton")?.GetComponent<Button>();
        if (startButton != null)
            LayoutMainButton(startButton, 82f, "开始游戏");

        settingsButton = settingsButton ??
            mainPanel.transform.Find("SettingsButton")?.GetComponent<Button>();
        if (settingsButton != null)
        {
            LayoutMainButton(settingsButton, 0f, "设置");
            settingsButton.onClick.RemoveListener(OpenSettings);
            settingsButton.onClick = new Button.ButtonClickedEvent();
            settingsButton.onClick.AddListener(OpenSettings);
        }

        quitButton = quitButton ??
            mainPanel.transform.Find("QuitButton")?.GetComponent<Button>();
        if (quitButton != null)
        {
            LayoutMainButton(quitButton, -82f, "离开游戏");
            quitButton.onClick.RemoveListener(QuitGame);
            quitButton.onClick = new Button.ButtonClickedEvent();
            quitButton.onClick.AddListener(QuitGame);
        }

        if (startButton == null || settingsButton == null ||
            quitButton == null)
        {
            Debug.LogError(
                "[StartMenu] 主菜单层级不完整：场景必须直接包含开始、设置和离开游戏三个按钮。",
                this);
        }

        CreateSettingsPanel(menuCanvas.transform);
        CreateBriefingPanel(menuCanvas.transform);
    }

    static void LayoutMainButton(
        Button button,
        float anchoredY,
        string label)
    {
        if (button == null)
            return;
        RectTransform rect = button.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, anchoredY);
        rect.sizeDelta = new Vector2(410f, 68f);
        Text text = button.GetComponentInChildren<Text>(true);
        if (text == null)
            return;
        text.text = label;
        text.alignment = TextAnchor.MiddleCenter;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = 16;
        text.resizeTextMaxSize = 28;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = new Vector2(18f, 8f);
        text.rectTransform.offsetMax = new Vector2(-18f, -8f);
    }

    void CreateSettingsPanel(Transform canvasTransform)
    {
        Transform existing = canvasTransform.Find("RuntimeSettingsRoot");
        if (existing != null)
        {
            settingsRoot = existing.gameObject;
            return;
        }

        settingsRoot = new GameObject(
            "RuntimeSettingsRoot",
            typeof(RectTransform),
            typeof(Image));
        settingsRoot.transform.SetParent(canvasTransform, false);
        SetStretch(settingsRoot.GetComponent<RectTransform>());
        Image dim = settingsRoot.GetComponent<Image>();
        dim.color = new Color(0f, 0.015f, 0.025f, 0.78f);

        GameObject panelObject = new GameObject(
            "SettingsFrame",
            typeof(RectTransform),
            typeof(Image));
        panelObject.transform.SetParent(settingsRoot.transform, false);
        RectTransform panel = panelObject.GetComponent<RectTransform>();
        SetRect(panel, Vector2.zero, new Vector2(760f, 560f));
        Image panelImage = panelObject.GetComponent<Image>();
        panelImage.color = new Color(0.008f, 0.045f, 0.072f, 0.985f);
        Outline outline = panelObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.08f, 0.78f, 0.9f, 0.9f);
        outline.effectDistance = new Vector2(2f, -2f);

        Text title = CreateRuntimeText(
            panel,
            "系统设置",
            32,
            TextAnchor.MiddleCenter,
            new Color(0.8f, 0.98f, 1f, 1f));
        title.fontStyle = FontStyle.Bold;
        title.gameObject.AddComponent<Shadow>().effectColor =
            new Color(0f, 0.75f, 1f, 0.55f);
        SetRect(title.rectTransform, new Vector2(0f, 234f),
            new Vector2(650f, 48f));

        Text subtitle = CreateRuntimeText(
            panel,
            "显示与操作",
            16,
            TextAnchor.MiddleCenter,
            new Color(0.38f, 0.72f, 0.8f, 1f));
        SetRect(subtitle.rectTransform, new Vector2(0f, 202f),
            new Vector2(650f, 30f));

        Image divider = CreateRuntimeImage(
            panel,
            "HeaderDivider",
            new Color(0.05f, 0.64f, 0.76f, 0.72f));
        SetRect(divider.rectTransform, new Vector2(0f, 178f),
            new Vector2(650f, 2f));

        CreateSettingRow(panel, "VolumeRow", 120f);
        CreateSettingRow(panel, "SensitivityRow", 42f);
        CreateSettingRow(panel, "ResolutionRow", -36f);
        CreateSettingRow(panel, "DisplayModeRow", -114f);

        CreateSettingLabel(panel, "主音量", 120f);
        runtimeVolumeSlider = CreateRuntimeSlider(
            panel,
            new Vector2(74f, 120f),
            new Vector2(250f, 28f),
            0f,
            1f);
        runtimeVolumeValue = CreateValueText(panel, 270f, 120f);
        runtimeVolumeSlider.onValueChanged.AddListener(ApplyVolume);

        CreateSettingLabel(panel, "视角灵敏度", 42f);
        runtimeSensitivitySlider = CreateRuntimeSlider(
            panel,
            new Vector2(74f, 42f),
            new Vector2(250f, 28f),
            0.2f,
            5f);
        runtimeSensitivityValue = CreateValueText(panel, 270f, 42f);
        runtimeSensitivitySlider.onValueChanged.AddListener(
            ApplySensitivity);

        CreateSettingLabel(panel, "分辨率", -36f);
        Button resolutionButton = CreateRuntimeButton(
            panel,
            "ResolutionButton",
            string.Empty,
            new Vector2(118f, -36f),
            new Vector2(340f, 44f));
        runtimeResolutionValue =
            resolutionButton.GetComponentInChildren<Text>(true);
        resolutionButton.onClick.AddListener(ToggleResolutionMenu);

        CreateSettingLabel(panel, "显示模式", -114f);
        Button displayModeButton = CreateRuntimeButton(
            panel,
            "DisplayModeButton",
            string.Empty,
            new Vector2(118f, -114f),
            new Vector2(340f, 44f));
        runtimeDisplayModeValue =
            displayModeButton.GetComponentInChildren<Text>(true);
        displayModeButton.onClick.AddListener(ToggleDisplayMode);

        Button close = CreateRuntimeButton(
            panel,
            "CloseSettingsButton",
            "保存并返回",
            new Vector2(0f, -232f),
            new Vector2(300f, 54f));
        close.onClick.AddListener(CloseSettings);

        CreateResolutionMenu(panel);
        settingsRoot.SetActive(false);
    }

    void CreateBriefingPanel(Transform canvasTransform)
    {
        Transform existing = canvasTransform.Find("GameLoopBriefingRoot");
        if (existing != null)
        {
            briefingRoot = existing.gameObject;
            return;
        }

        briefingRoot = new GameObject(
            "GameLoopBriefingRoot",
            typeof(RectTransform),
            typeof(Image));
        briefingRoot.transform.SetParent(canvasTransform, false);
        SetStretch(briefingRoot.GetComponent<RectTransform>());
        briefingRoot.GetComponent<Image>().color =
            new Color(0f, 0.01f, 0.02f, 0.86f);

        GameObject panelObject = new GameObject(
            "BriefingFrame",
            typeof(RectTransform),
            typeof(Image));
        panelObject.transform.SetParent(briefingRoot.transform, false);
        RectTransform panel = panelObject.GetComponent<RectTransform>();
        SetRect(panel, Vector2.zero, new Vector2(1060f, 800f));
        panelObject.GetComponent<Image>().color =
            new Color(0.006f, 0.038f, 0.066f, 0.99f);
        Outline outline = panelObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.1f, 0.86f, 0.9f, 0.9f);
        outline.effectDistance = new Vector2(2f, -2f);

        briefingTitle = CreateRuntimeText(
            panel,
            string.Empty,
            34,
            TextAnchor.MiddleCenter,
            new Color(0.28f, 0.96f, 1f, 1f));
        briefingTitle.fontStyle = FontStyle.Bold;
        Shadow titleGlow = briefingTitle.gameObject.AddComponent<Shadow>();
        titleGlow.effectColor = new Color(0f, 0.7f, 1f, 0.65f);
        titleGlow.effectDistance = new Vector2(2f, -2f);
        SetRect(briefingTitle.rectTransform, new Vector2(0f, 348f),
            new Vector2(780f, 52f));

        briefingPageIndicator = CreateRuntimeText(
            panel,
            string.Empty,
            17,
            TextAnchor.MiddleRight,
            new Color(0.48f, 0.78f, 0.86f, 1f));
        SetRect(briefingPageIndicator.rectTransform,
            new Vector2(438f, 348f), new Vector2(100f, 40f));

        Image illustrationFrame = CreateRuntimeImage(
            panel,
            "IllustrationFrame",
            new Color(0.012f, 0.08f, 0.12f, 1f));
        SetRect(illustrationFrame.rectTransform, new Vector2(0f, 50f),
            new Vector2(920f, 518f));
        Outline illustrationOutline =
            illustrationFrame.gameObject.AddComponent<Outline>();
        illustrationOutline.effectColor =
            new Color(0.04f, 0.66f, 0.82f, 0.82f);
        illustrationOutline.effectDistance = new Vector2(2f, -2f);

        GameObject imageObject = new GameObject(
            "TutorialIllustration",
            typeof(RectTransform),
            typeof(RawImage),
            typeof(AspectRatioFitter));
        imageObject.transform.SetParent(illustrationFrame.transform, false);
        briefingIllustration = imageObject.GetComponent<RawImage>();
        briefingIllustration.raycastTarget = false;
        SetStretch(briefingIllustration.rectTransform,
            new Vector2(8f, 8f));
        AspectRatioFitter imageFitter =
            imageObject.GetComponent<AspectRatioFitter>();
        imageFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        imageFitter.aspectRatio = 16f / 9f;

        briefingBody = CreateRuntimeText(
            panel,
            string.Empty,
            20,
            TextAnchor.MiddleCenter,
            new Color(0.82f, 0.95f, 1f, 1f));
        briefingBody.lineSpacing = 1.14f;
        briefingBody.resizeTextForBestFit = true;
        briefingBody.resizeTextMinSize = 16;
        briefingBody.resizeTextMaxSize = 20;
        SetRect(briefingBody.rectTransform, new Vector2(0f, -251f),
            new Vector2(900f, 78f));

        briefingPreviousButton = CreateRuntimeButton(
            panel,
            "PreviousBriefingButton",
            "上一页",
            new Vector2(-190f, -346f),
            new Vector2(250f, 54f));
        briefingPreviousButton.onClick.AddListener(PreviousBriefingPage);

        briefingNextButton = CreateRuntimeButton(
            panel,
            "NextBriefingButton",
            "下一页",
            new Vector2(190f, -346f),
            new Vector2(300f, 54f));
        briefingNextButtonText =
            briefingNextButton.GetComponentInChildren<Text>(true);
        briefingNextButton.onClick.AddListener(NextBriefingPage);

        briefingTextures = new[]
        {
            Resources.Load<Texture2D>("UI/Tutorial/GameLoopJourney"),
            Resources.Load<Texture2D>("UI/Tutorial/BossUnlockPath"),
            Resources.Load<Texture2D>("UI/Tutorial/ShipGrowthSystems")
        };
        RefreshBriefingPage();
        briefingRoot.SetActive(false);
    }

    void ShowGameLoopBriefing(
        string slotId,
        bool forceModular,
        bool initialAssembly)
    {
        pendingBriefingSlotId = slotId;
        pendingBriefingForceModular = forceModular;
        pendingBriefingInitialAssembly = initialAssembly;
        titleImage.SetActive(false);
        mainPanel.SetActive(false);
        saveBrowserPanel.SetActive(false);
        CloseDialogs();
        settingsRoot?.SetActive(false);
        briefingPage = 0;
        RefreshBriefingPage();
        briefingRoot?.SetActive(true);
    }

    public void PreviousBriefingPage()
    {
        if (briefingPage <= 0)
            return;
        briefingPage--;
        RefreshBriefingPage();
    }

    public void NextBriefingPage()
    {
        if (briefingPage < 2)
        {
            briefingPage++;
            RefreshBriefingPage();
            return;
        }
        ConfirmGameLoopBriefing();
    }

    void RefreshBriefingPage()
    {
        briefingPage = Mathf.Clamp(briefingPage, 0, 2);
        if (briefingTitle == null || briefingBody == null)
            return;

        switch (briefingPage)
        {
            case 0:
                briefingTitle.text = "远征循环";
                briefingBody.text =
                    "空间站整备 → 改装飞船 → 选择星球任务 → 战斗结算 → 返回空间站。\n" +
                    "银河币来自实际战果，用于下一轮成长。";
                break;
            case 1:
                briefingTitle.text = "解锁首领";
                briefingBody.text =
                    "每颗星球有两个普通区域和一个首领区域。\n" +
                    "完成两个普通区域后，首领节点才会解锁。";
                break;
            default:
                briefingTitle.text = "强化飞船";
                briefingBody.text =
                    "架构协议：提高模块与 CPU 上限。强化推演：提高战斗属性。技能装载：配置主动技能。\n" +
                    "三套系统互不占用，可以自由组合。";
                break;
        }

        if (briefingIllustration != null && briefingTextures != null &&
            briefingPage < briefingTextures.Length)
        {
            briefingIllustration.texture = briefingTextures[briefingPage];
            Texture texture = briefingIllustration.texture;
            AspectRatioFitter fitter = briefingIllustration.GetComponent<
                AspectRatioFitter>();
            if (texture != null && fitter != null && texture.height > 0)
            {
                fitter.aspectRatio = texture.width / (float)texture.height;
            }
        }
        if (briefingPageIndicator != null)
            briefingPageIndicator.text = (briefingPage + 1) + " / 3";
        if (briefingPreviousButton != null)
            briefingPreviousButton.gameObject.SetActive(briefingPage > 0);
        if (briefingNextButtonText != null)
        {
            briefingNextButtonText.text = briefingPage < 2
                ? "下一页"
                : "开始远征";
        }
    }

    public void ConfirmGameLoopBriefing()
    {
        string slotId = pendingBriefingSlotId;
        if (string.IsNullOrWhiteSpace(slotId))
            return;
        try
        {
            GalaxySaveSlotMetadata metadata =
                GalaxySaveSlotService.LoadMetadata(slotId);
            if (metadata != null)
            {
                metadata.gameLoopBriefingSeen = true;
                GalaxySaveSlotService.SaveMetadata(metadata);
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "[StartMenu] 新存档简报状态暂未保存：" +
                exception.Message,
                this);
        }
        briefingRoot?.SetActive(false);
        LoadConstructionScene(
            slotId,
            pendingBriefingForceModular,
            pendingBriefingInitialAssembly);
    }

    public void OpenSettings()
    {
        mainPanel.SetActive(false);
        titleImage.SetActive(false);
        BuildResolutionList();
        UpdateSettingsControls();
        settingsRoot?.SetActive(true);
    }

    public void CloseSettings()
    {
        resolutionMenuRoot?.SetActive(false);
        PlayerPrefs.Save();
        settingsRoot?.SetActive(false);
        ShowMainPage();
    }

    public void QuitGame()
    {
        PlayerPrefs.Save();
#if UNITY_EDITOR
        Debug.Log("[StartMenu] 离开游戏：编辑器中不会关闭 Unity。", this);
#else
        Application.Quit();
#endif
    }

    void ApplySavedSettings()
    {
        AudioListener.volume = Mathf.Clamp01(
            PlayerPrefs.GetFloat("MasterVolume", AudioListener.volume));
        DisplayResolutionSettings.ApplySavedSettings();
        BuildResolutionList();
        UpdateSettingsControls();
    }

    void UpdateSettingsControls()
    {
        if (runtimeVolumeSlider != null)
            runtimeVolumeSlider.SetValueWithoutNotify(AudioListener.volume);
        if (runtimeVolumeValue != null)
            runtimeVolumeValue.text = Mathf.RoundToInt(
                AudioListener.volume * 100f) + "%";
        float sensitivity = PlayerPrefs.GetFloat(
            "MouseSensitivity",
            2f);
        if (runtimeSensitivitySlider != null)
            runtimeSensitivitySlider.SetValueWithoutNotify(sensitivity);
        if (runtimeSensitivityValue != null)
            runtimeSensitivityValue.text = sensitivity.ToString("0.0");
        if (runtimeResolutionValue != null)
        {
            int width = PlayerPrefs.GetInt(
                "ResolutionWidth",
                Screen.width);
            int height = PlayerPrefs.GetInt(
                "ResolutionHeight",
                Screen.height);
            runtimeResolutionValue.text =
                DisplayResolutionSettings.Format(
                    width,
                    height) + "    选择";
        }
        if (runtimeDisplayModeValue != null)
        {
            runtimeDisplayModeValue.text = Screen.fullScreen
                ? "全屏模式    切换"
                : "窗口模式    切换";
        }
        RefreshResolutionOptionVisuals();
    }

    public void ApplyVolume(float value)
    {
        value = Mathf.Clamp01(value);
        AudioListener.volume = value;
        PlayerPrefs.SetFloat("MasterVolume", value);
        if (runtimeVolumeValue != null)
            runtimeVolumeValue.text = Mathf.RoundToInt(value * 100f) + "%";
    }

    public void ApplySensitivity(float value)
    {
        value = Mathf.Clamp(value, 0.2f, 5f);
        PlayerPrefs.SetFloat("MouseSensitivity", value);
        if (runtimeSensitivityValue != null)
            runtimeSensitivityValue.text = value.ToString("0.0");
    }

    public void ApplyFullscreen(bool value)
    {
        DisplayResolutionSettings.Apply(
            PlayerPrefs.GetInt("ResolutionWidth", Screen.width),
            PlayerPrefs.GetInt("ResolutionHeight", Screen.height),
            value);
        if (runtimeDisplayModeValue != null)
        {
            runtimeDisplayModeValue.text = value
                ? "全屏模式    切换"
                : "窗口模式    切换";
        }
    }

    public void ToggleDisplayMode()
    {
        ApplyFullscreen(!Screen.fullScreen);
    }

    public void ToggleResolutionMenu()
    {
        if (resolutionMenuRoot == null)
            return;
        BuildResolutionList();
        bool show = !resolutionMenuRoot.activeSelf;
        resolutionMenuRoot.SetActive(show);
        if (show)
        {
            resolutionMenuRoot.transform.SetAsLastSibling();
            RefreshResolutionOptionVisuals();
        }
    }

    public void SelectResolution(int width, int height)
    {
        DisplayResolutionSettings.Apply(
            width,
            height,
            Screen.fullScreen);
        resolutionMenuRoot?.SetActive(false);
        if (runtimeResolutionValue != null)
        {
            runtimeResolutionValue.text =
                DisplayResolutionSettings.Format(width, height) +
                "    选择";
        }
        RefreshResolutionOptionVisuals(width, height);
    }

    void BuildResolutionList()
    {
        availableResolutions.Clear();
        availableResolutions.AddRange(
            DisplayResolutionSettings.BuildAvailableList());
        RebuildResolutionMenuOptions();
    }

    void CreateResolutionMenu(RectTransform parent)
    {
        resolutionMenuRoot = new GameObject(
            "ResolutionMenu",
            typeof(RectTransform),
            typeof(Image),
            typeof(Button));
        resolutionMenuRoot.transform.SetParent(parent, false);
        SetStretch(resolutionMenuRoot.GetComponent<RectTransform>());
        Image blocker = resolutionMenuRoot.GetComponent<Image>();
        blocker.color = new Color(0f, 0.008f, 0.018f, 0.88f);
        Button blockerButton = resolutionMenuRoot.GetComponent<Button>();
        blockerButton.transition = Selectable.Transition.None;
        blockerButton.onClick.AddListener(
            () => resolutionMenuRoot.SetActive(false));

        Image popup = CreateRuntimeImage(
            resolutionMenuRoot.transform,
            "ResolutionPopup",
            new Color(0.008f, 0.055f, 0.086f, 1f));
        SetRect(popup.rectTransform, Vector2.zero,
            new Vector2(620f, 430f));
        Outline popupOutline = popup.gameObject.AddComponent<Outline>();
        popupOutline.effectColor =
            new Color(0.08f, 0.78f, 0.9f, 0.95f);
        popupOutline.effectDistance = new Vector2(2f, -2f);

        Text title = CreateRuntimeText(
            popup.transform,
            "选择分辨率",
            28,
            TextAnchor.MiddleCenter,
            new Color(0.75f, 0.97f, 1f, 1f));
        title.fontStyle = FontStyle.Bold;
        SetRect(title.rectTransform, new Vector2(0f, 170f),
            new Vector2(500f, 42f));

        Text hint = CreateRuntimeText(
            popup.transform,
            "选择后立即应用，重新启动游戏仍会保留",
            16,
            TextAnchor.MiddleCenter,
            new Color(0.42f, 0.74f, 0.8f, 1f));
        SetRect(hint.rectTransform, new Vector2(0f, 136f),
            new Vector2(520f, 30f));

        GameObject optionsObject = new GameObject(
            "ResolutionOptions",
            typeof(RectTransform));
        optionsObject.transform.SetParent(popup.transform, false);
        resolutionOptionsRoot =
            optionsObject.GetComponent<RectTransform>();
        SetRect(resolutionOptionsRoot, new Vector2(0f, -22f),
            new Vector2(560f, 280f));

        Button close = CreateRuntimeButton(
            popup.transform,
            "CloseResolutionMenu",
            "返回设置",
            new Vector2(0f, -176f),
            new Vector2(230f, 44f));
        close.onClick.AddListener(
            () => resolutionMenuRoot.SetActive(false));
        resolutionMenuRoot.SetActive(false);
    }

    void RebuildResolutionMenuOptions()
    {
        if (resolutionOptionsRoot == null ||
            resolutionOptionButtons.Count > 0)
        {
            return;
        }

        int count = Mathf.Min(availableResolutions.Count, 10);
        int rowCount = Mathf.CeilToInt(count / 2f);
        float startY = (rowCount - 1) * 27f;
        for (int index = 0; index < count; index++)
        {
            Vector2Int option = availableResolutions[index];
            int capturedWidth = option.x;
            int capturedHeight = option.y;
            int column = index % 2;
            int row = index / 2;
            Button button = CreateRuntimeButton(
                resolutionOptionsRoot,
                "Resolution_" + option.x + "x" + option.y,
                DisplayResolutionSettings.Format(option.x, option.y),
                new Vector2(column == 0 ? -137f : 137f,
                    startY - row * 54f),
                new Vector2(252f, 42f));
            button.onClick.AddListener(
                () => SelectResolution(capturedWidth, capturedHeight));
            resolutionOptionButtons.Add(button);
        }
        RefreshResolutionOptionVisuals();
    }

    void RefreshResolutionOptionVisuals()
    {
        RefreshResolutionOptionVisuals(
            PlayerPrefs.GetInt("ResolutionWidth", Screen.width),
            PlayerPrefs.GetInt("ResolutionHeight", Screen.height));
    }

    void RefreshResolutionOptionVisuals(int width, int height)
    {
        int count = Mathf.Min(
            resolutionOptionButtons.Count,
            availableResolutions.Count);
        for (int index = 0; index < count; index++)
        {
            Button button = resolutionOptionButtons[index];
            Vector2Int option = availableResolutions[index];
            bool selected = option.x == width && option.y == height;
            Image image = button.GetComponent<Image>();
            if (image != null)
            {
                image.color = selected
                    ? new Color(0.08f, 0.5f, 0.58f, 1f)
                    : new Color(0.022f, 0.16f, 0.22f, 1f);
            }
            Text label = button.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                label.text = DisplayResolutionSettings.Format(
                    option.x,
                    option.y) + (selected ? "    当前" : string.Empty);
                label.color = selected
                    ? new Color(0.82f, 1f, 0.96f, 1f)
                    : new Color(0.76f, 0.92f, 0.96f, 1f);
            }
        }
    }

    static void CreateSettingRow(
        RectTransform parent,
        string name,
        float y)
    {
        Image row = CreateRuntimeImage(
            parent,
            name,
            new Color(0.012f, 0.09f, 0.125f, 0.88f));
        SetRect(row.rectTransform, new Vector2(0f, y),
            new Vector2(650f, 64f));
        Outline outline = row.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.04f, 0.3f, 0.38f, 0.7f);
        outline.effectDistance = new Vector2(1f, -1f);
        row.raycastTarget = false;
    }

    static void CreateSettingLabel(
        RectTransform parent,
        string label,
        float y)
    {
        Text text = CreateRuntimeText(
            parent,
            label,
            20,
            TextAnchor.MiddleLeft,
            new Color(0.72f, 0.9f, 0.94f, 1f));
        SetRect(text.rectTransform, new Vector2(-230f, y),
            new Vector2(190f, 44f));
    }

    static Text CreateValueText(
        RectTransform parent,
        float x,
        float y)
    {
        Text value = CreateRuntimeText(
            parent,
            string.Empty,
            18,
            TextAnchor.MiddleCenter,
            new Color(0.25f, 0.95f, 0.96f, 1f));
        SetRect(value.rectTransform, new Vector2(x, y),
            new Vector2(76f, 40f));
        return value;
    }

    static Text CreateRuntimeText(
        Transform parent,
        string value,
        int fontSize,
        TextAnchor alignment,
        Color color)
    {
        GameObject textObject = new GameObject(
            "Text",
            typeof(RectTransform),
            typeof(Text));
        textObject.transform.SetParent(parent, false);
        Text text = textObject.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>(
            "LegacyRuntime.ttf");
        text.text = value ?? string.Empty;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        return text;
    }

    static Button CreateRuntimeButton(
        Transform parent,
        string name,
        string label,
        Vector2 position,
        Vector2 size)
    {
        GameObject buttonObject = new GameObject(
            name,
            typeof(RectTransform),
            typeof(Image),
            typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        SetRect(buttonObject.GetComponent<RectTransform>(), position, size);
        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.04f, 0.34f, 0.42f, 0.98f);
        Outline outline = buttonObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.12f, 0.88f, 0.92f, 0.85f);
        outline.effectDistance = new Vector2(1f, -1f);
        Button button = buttonObject.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
        colors.pressedColor = new Color(0.75f, 0.9f, 0.92f, 1f);
        button.colors = colors;
        Text text = CreateRuntimeText(
            buttonObject.transform,
            label,
            20,
            TextAnchor.MiddleCenter,
            new Color(0.88f, 0.98f, 1f, 1f));
        SetStretch(text.rectTransform, new Vector2(12f, 6f));
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = 14;
        text.resizeTextMaxSize = 20;
        return button;
    }

    static Image CreateRuntimeImage(
        Transform parent,
        string name,
        Color color)
    {
        GameObject imageObject = new GameObject(
            name,
            typeof(RectTransform),
            typeof(Image));
        imageObject.transform.SetParent(parent, false);
        Image image = imageObject.GetComponent<Image>();
        image.color = color;
        return image;
    }

    static Slider CreateRuntimeSlider(
        Transform parent,
        Vector2 position,
        Vector2 size,
        float minimum,
        float maximum)
    {
        GameObject root = new GameObject(
            "Slider",
            typeof(RectTransform),
            typeof(Slider));
        root.transform.SetParent(parent, false);
        SetRect(root.GetComponent<RectTransform>(), position, size);

        GameObject background = new GameObject(
            "Background",
            typeof(RectTransform),
            typeof(Image));
        background.transform.SetParent(root.transform, false);
        SetStretch(background.GetComponent<RectTransform>(),
            new Vector2(0f, 10f));
        background.GetComponent<Image>().color =
            new Color(0.015f, 0.07f, 0.09f, 1f);

        GameObject fill = new GameObject(
            "Fill",
            typeof(RectTransform),
            typeof(Image));
        fill.transform.SetParent(root.transform, false);
        RectTransform fillRect = fill.GetComponent<RectTransform>();
        SetStretch(fillRect, new Vector2(4f, 12f));
        fill.GetComponent<Image>().color =
            new Color(0.05f, 0.82f, 0.86f, 1f);

        GameObject handle = new GameObject(
            "Handle",
            typeof(RectTransform),
            typeof(Image));
        handle.transform.SetParent(root.transform, false);
        RectTransform handleRect = handle.GetComponent<RectTransform>();
        SetRect(handleRect, Vector2.zero, new Vector2(14f, 24f));
        handle.GetComponent<Image>().color =
            new Color(0.85f, 0.98f, 1f, 1f);

        Slider slider = root.GetComponent<Slider>();
        slider.minValue = minimum;
        slider.maxValue = maximum;
        slider.fillRect = fillRect;
        slider.handleRect = handleRect;
        slider.targetGraphic = handle.GetComponent<Image>();
        slider.direction = Slider.Direction.LeftToRight;
        return slider;
    }

    static void SetRect(
        RectTransform rect,
        Vector2 position,
        Vector2 size)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    static void SetStretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    static void SetStretch(RectTransform rect, Vector2 inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = inset;
        rect.offsetMax = -inset;
    }

    void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message ?? string.Empty;
    }
}
