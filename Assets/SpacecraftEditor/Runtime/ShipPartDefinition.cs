using UnityEngine;

namespace SpacecraftEditor
{
    [CreateAssetMenu(menuName = "Spacecraft/Ship Part Definition", fileName = "ShipPartDefinition")]
    public sealed class ShipPartDefinition : ScriptableObject
    {
        [SerializeField] private string partId;
        [SerializeField] private string displayName;
        [SerializeField] private GameObject prefab;
        [SerializeField] private Sprite thumbnail;
        [SerializeField] private SpacecraftPartCategory category = SpacecraftPartCategory.Thruster;
        [SerializeField] private SpacecraftPartScaleMode scaleMode = SpacecraftPartScaleMode.Free;
        [SerializeField] private SpacecraftPartPlacementMode placementMode = SpacecraftPartPlacementMode.SurfaceConforming;
        [SerializeField] private float baseMass = 2f;
        [SerializeField] private float baseThrust = 40f;
        [SerializeField] private float minimumScale = 0.6f;
        [SerializeField] private float maximumScale = 1.5f;
        [SerializeField] private float fixedScale = 1f;
        [SerializeField] private string defaultMaterialId = "paint.deep_space_blue";
        [SerializeField] private Color exhaustColor = Color.cyan;
        [SerializeField] private SpacecraftWeaponDefinition weapon;
        [SerializeField] private ShipAerodynamicProfile aerodynamicProfile;

        public string PartId => partId;
        public string DisplayName => displayName;
        public GameObject Prefab => prefab;
        public Sprite Thumbnail => thumbnail;
        public SpacecraftPartCategory Category => category;
        public SpacecraftPartScaleMode ScaleMode => scaleMode;
        public SpacecraftPartPlacementMode PlacementMode => placementMode;
        public float BaseMass => baseMass;
        public float BaseThrust => baseThrust;
        public float MinimumScale => minimumScale;
        public float MaximumScale => maximumScale;
        public float FixedScale => fixedScale;
        public string DefaultMaterialId => defaultMaterialId;
        public Color ExhaustColor => exhaustColor;
        public SpacecraftWeaponDefinition Weapon => weapon;
        public ShipAerodynamicProfile Aerodynamics =>
            aerodynamicProfile != null && aerodynamicProfile.IsEnabled
                ? aerodynamicProfile
                : ShipAerodynamicProfile.CreateForPart(partId, category);
        public bool IsScalable => scaleMode == SpacecraftPartScaleMode.Free;

#if UNITY_EDITOR
        public void Configure(
            string id,
            string label,
            GameObject partPrefab,
            Sprite icon,
            float mass,
            float thrust,
            Color exhaust)
        {
            partId = id;
            displayName = label;
            prefab = partPrefab;
            thumbnail = icon;
            baseMass = mass;
            baseThrust = thrust;
            category = SpacecraftPartCategory.Thruster;
            scaleMode = SpacecraftPartScaleMode.Free;
            placementMode = SpacecraftPartPlacementMode.SurfaceConforming;
            minimumScale = 0.6f;
            maximumScale = 1.5f;
            fixedScale = 1f;
            defaultMaterialId = "paint.deep_space_blue";
            exhaustColor = exhaust;
            weapon = null;
            aerodynamicProfile = ShipAerodynamicProfile.CreateForPart(
                partId,
                category);
        }

        public void ConfigureGeneral(
            string id,
            string label,
            GameObject partPrefab,
            Sprite icon,
            SpacecraftPartCategory partCategory,
            float mass,
            SpacecraftPartScaleMode partScaleMode,
            float authoredScale,
            string materialId,
            SpacecraftWeaponDefinition weaponDefinition = null,
            SpacecraftPartPlacementMode partPlacementMode = SpacecraftPartPlacementMode.SurfaceConforming)
        {
            partId = id;
            displayName = label;
            prefab = partPrefab;
            thumbnail = icon;
            category = partCategory;
            baseMass = Mathf.Max(0f, mass);
            baseThrust = 0f;
            scaleMode = partScaleMode;
            placementMode = partPlacementMode;
            minimumScale = 0.6f;
            maximumScale = 1.5f;
            fixedScale = Mathf.Max(0.01f, authoredScale);
            defaultMaterialId = materialId ?? string.Empty;
            exhaustColor = Color.cyan;
            weapon = weaponDefinition;
            aerodynamicProfile = ShipAerodynamicProfile.CreateForPart(
                partId,
                category);
        }
#endif
    }
}
