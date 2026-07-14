using UnityEngine;
using UnityEngine.UI;

public sealed class StartMenuSaveRow : MonoBehaviour
{
    [SerializeField] Button button;
    [SerializeField] Image selectionFrame;
    [SerializeField] Text nameText;
    [SerializeField] Text detailsText;

    GalaxySaveSlotInfo slot;
    StartMenuController owner;

    public GalaxySaveSlotInfo Slot => slot;

    public void Initialize(StartMenuController menuOwner, GalaxySaveSlotInfo slotInfo)
    {
        owner = menuOwner;
        slot = slotInfo;
        nameText.text = slotInfo.DisplayName;
        if (slotInfo.IsCorrupt)
        {
            detailsText.text = "存档损坏 · 仅可删除";
            nameText.color = new Color(1f, 0.38f, 0.3f);
        }
        else
        {
            DateTimeText(slotInfo.Metadata.lastPlayedUtcTicks, out string dateText);
            detailsText.text = $"最后游玩 {dateText}   ·   种子 {slotInfo.Metadata.worldSeed}";
        }

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => owner.SelectSlot(this));
        SetSelected(false);
        gameObject.SetActive(true);
    }

    public void SetSelected(bool selected)
    {
        if (selectionFrame != null)
            selectionFrame.enabled = selected;
    }

    static void DateTimeText(long ticks, out string text)
    {
        try
        {
            text = new System.DateTime(ticks, System.DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        }
        catch
        {
            text = "未知";
        }
    }
}
