using UnityEngine;

namespace CityGeneration
{
    [CreateAssetMenu(
        fileName = "PlanarCityRuntimeAssets",
        menuName = "Planet/Planar City Runtime Assets")]
    public sealed class PlanarCityRuntimeAssets : ScriptableObject
    {
        public CityGenerationSettings generationSettings =
            new CityGenerationSettings();
        public GameObject[] buildingPrefabs;
        public bool useBuildingPrefabs = true;
        public bool overridePrefabMaterials;
        [Min(0.05f)] public float platformTopClearance = 0.5f;
        [Min(0.1f)] public float platformSlabThickness = 0.4f;
        [Min(2f)] public float platformSupportSpacing = 20f;
        [Min(0.2f)] public float platformSupportWidth = 1.4f;
        [Min(0.1f)] public float platformMinimumSupportHeight = 0.6f;
        public Color roadColor =
            new Color(0.09f, 0.1f, 0.12f, 1f);
        public Color blockColor =
            new Color(0.36f, 0.4f, 0.34f, 1f);
        public Color foundationColor =
            new Color(0.18f, 0.23f, 0.28f, 1f);
        public Color[] buildingColors =
        {
            new Color(0.42f, 0.58f, 0.68f, 1f),
            new Color(0.64f, 0.5f, 0.38f, 1f),
            new Color(0.38f, 0.62f, 0.54f, 1f),
            new Color(0.58f, 0.44f, 0.68f, 1f)
        };
    }
}
