using UnityEngine;
using UnityEngine.UI;

namespace SpacecraftEditor
{
    [DisallowMultipleComponent]
    public sealed class WorkshopUIReferences : MonoBehaviour
    {
        public GameObject BuildInterface;
        public GameObject FlightHud;
        public GameObject HullSelectionPanel;
        public RectTransform BuildViewport;
        public RectTransform HullSelectionViewport;
        public RectTransform PartCardRoot;
        public PartCardUI PartCardTemplate;
        public Button DecorationTabButton;
        public Button ThrusterTabButton;
        public Button WeaponTabButton;
        public RectTransform MaterialSwatchRoot;
        public Button MaterialSwatchTemplate;
        public Button WeaponGroup1Button;
        public Button WeaponGroup2Button;
        public RectTransform HullCardRoot;
        public HullCardUI HullCardTemplate;
        public Text StatsText;
        public Text SelectionText;
        public Text MirrorButtonText;
        public Text ForceButtonText;
        public Text FlightStatsText;
        public Text SnapStatusText;
        public Text BindingButtonText;
        public Text BindingStatusText;
        public Text HullSelectionSummary;
        public Button UndoButton;
        public Button RedoButton;
        public Button MirrorButton;
        public Button ForceButton;
        public Button FlightButton;
        public Button DeleteButton;
        public Button BindingButton;
        public Button HullConfirmButton;
        public Slider ScaleSlider;

        public bool IsConfigured => BuildInterface != null && FlightHud != null && HullSelectionPanel != null &&
                                    BuildViewport != null && HullSelectionViewport != null &&
                                    PartCardRoot != null && PartCardTemplate != null &&
                                    HullCardRoot != null && HullCardTemplate != null &&
                                    StatsText != null && SelectionText != null && MirrorButtonText != null &&
                                    ForceButtonText != null && FlightStatsText != null && SnapStatusText != null &&
                                    BindingButtonText != null && BindingStatusText != null && HullSelectionSummary != null &&
                                    UndoButton != null && RedoButton != null && MirrorButton != null &&
                                    ForceButton != null && FlightButton != null && DeleteButton != null &&
                                    BindingButton != null && HullConfirmButton != null && ScaleSlider != null;
    }
}
