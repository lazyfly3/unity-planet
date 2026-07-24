using System;
using UnityEngine;
using UnityEngine.UI;

namespace SpacecraftEditor
{
    public sealed class HullCardUI : MonoBehaviour
    {
        [SerializeField] private Image frame;
        [SerializeField] private Image thumbnail;
        [SerializeField] private Text nameText;
        [SerializeField] private Text statsText;
        [SerializeField] private Button button;

        public void ConfigureReferences(Image frameImage, Image icon, Text label, Text stats, Button targetButton)
        {
            frame = frameImage;
            thumbnail = icon;
            nameText = label;
            statsText = stats;
            button = targetButton;
        }

        public void Configure(ShipHullDefinition definition, Func<ShipHullDefinition, bool> select)
        {
            if (definition == null)
                return;
            thumbnail.sprite = definition.Thumbnail;
            nameText.text = definition.DisplayName;
            statsText.text = $"长 {definition.Dimensions.z:0.0}m  ·  宽 {definition.Dimensions.x:0.0}m  ·  高 {definition.Dimensions.y:0.0}m\n" +
                             $"基础质量 {SpaceflightUnitFormatter.FormatMass(definition.BaseMass)}";
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => select(definition));
            SetSelected(false);
        }

        public void SetSelected(bool selected)
        {
            frame.color = selected
                ? new Color(0.12f, 0.88f, 1f, 1f)
                : new Color(0.055f, 0.17f, 0.22f, 0.96f);
        }
    }
}
