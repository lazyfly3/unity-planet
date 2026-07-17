using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class StartMenuController : MonoBehaviour
{
    [Header("Pages")]
    [SerializeField] GameObject titleImage;
    [SerializeField] GameObject mainPanel;
    [SerializeField] GameObject saveBrowserPanel;
    [SerializeField] GameObject createDialog;
    [SerializeField] GameObject renameDialog;
    [SerializeField] GameObject deleteDialog;

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
    [SerializeField] string surfaceSceneName = "star";

    readonly List<StartMenuSaveRow> rows = new List<StartMenuSaveRow>();
    StartMenuSaveRow selectedRow;

    void Awake()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        GalaxyLaunchContext.Clear();
        ShowMainPage();
        SetStatus(string.Empty);
    }

    public void ShowMainPage()
    {
titleImage.SetActive(true);
        mainPanel.SetActive(true);
        saveBrowserPanel.SetActive(false);
        CloseDialogs();
    
}

    public void ShowSaveBrowser()
    {
titleImage.SetActive(false);
        mainPanel.SetActive(false);
        saveBrowserPanel.SetActive(true);
        CloseDialogs();
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
            SceneManager.LoadScene(surfaceSceneName, LoadSceneMode.Single);
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
        SceneManager.LoadScene(surfaceSceneName, LoadSceneMode.Single);
    
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
    }

    bool HasUsableSelection()
    {
        return selectedRow != null && !selectedRow.Slot.IsCorrupt;
    }

    void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message ?? string.Empty;
    }
}
