using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SpacecraftEditor
{
    public sealed class PartCardUI : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        private BuildModeController controller;
        private ShipPartDefinition definition;
        [SerializeField] private Image icon;
        [SerializeField] private Text nameText;
        [SerializeField] private Text statsText;

        public void ConfigureReferences(Image iconImage, Text label, Text stats)
        {
            icon = iconImage;
            nameText = label;
            statsText = stats;
        }

        public void Configure(BuildModeController target, ShipPartDefinition partDefinition)
        {
            controller = target;
            definition = partDefinition;
            if (definition == null)
                return;
            if (icon != null)
                icon.sprite = definition.Thumbnail;
            if (nameText != null)
                nameText.text = definition.DisplayName;
            if (statsText != null)
                statsText.text = BuildStats(definition);
        }

        static string BuildStats(ShipPartDefinition value)
        {
            if (value.Category == SpacecraftPartCategory.Decoration)
                return $"装饰  质量 {SpaceflightUnitFormatter.FormatMass(value.BaseMass)}";
            if (value.Category == SpacecraftPartCategory.Weapon)
            {
                SpacecraftWeaponDefinition weapon = value.Weapon;
                return weapon == null
                    ? $"武器  质量 {SpaceflightUnitFormatter.FormatMass(value.BaseMass)}"
                    : $"{weapon.MountSize}  {weapon.Damage:0} 伤害  {weapon.RoundsPerSecond:0.#}/s";
            }
            return $"推力 {SpaceflightUnitFormatter.FormatForce(value.BaseThrust)}  质量 {SpaceflightUnitFormatter.FormatMass(value.BaseMass)}";
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
                controller?.BeginPlacement(definition);
        }

        public void OnDrag(PointerEventData eventData)
        {
            controller?.UpdatePlacement(eventData.position);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
                controller?.EndPlacement(eventData.position);
        }
    }
}
