using System.Collections.Generic;
using UnityEngine;

namespace SpacecraftEditor
{
    [System.Serializable]
    public sealed class SpacecraftMaterialDefinition
    {
        [SerializeField] private string materialId;
        [SerializeField] private string displayName;
        [SerializeField] private Material material;
        [SerializeField] private Color previewColor = Color.white;

        public string MaterialId => materialId;
        public string DisplayName => displayName;
        public Material Material => material;
        public Color PreviewColor => previewColor;

#if UNITY_EDITOR
        public void Configure(string id, string label, Material sharedMaterial, Color color)
        {
            materialId = id;
            displayName = label;
            material = sharedMaterial;
            previewColor = color;
        }
#endif
    }

    public sealed class SpacecraftMaterialCatalog : MonoBehaviour
    {
        [SerializeField] private SpacecraftMaterialDefinition[] definitions;
        [SerializeField] private string defaultMaterialId = "paint.deep_space_blue";

        private readonly Dictionary<string, SpacecraftMaterialDefinition> lookup =
            new Dictionary<string, SpacecraftMaterialDefinition>();

        public IReadOnlyList<SpacecraftMaterialDefinition> Definitions => definitions;
        public string DefaultMaterialId => defaultMaterialId;

        private void Awake()
        {
            RebuildLookup();
        }

        public void Configure(SpacecraftMaterialDefinition[] values, string defaultId)
        {
            definitions = values;
            defaultMaterialId = defaultId ?? string.Empty;
            RebuildLookup();
        }

        public SpacecraftMaterialDefinition Find(string id)
        {
            if (lookup.Count == 0)
                RebuildLookup();
            if (!string.IsNullOrEmpty(id) && lookup.TryGetValue(id, out var result))
                return result;
            return !string.IsNullOrEmpty(defaultMaterialId) && lookup.TryGetValue(defaultMaterialId, out result)
                ? result
                : null;
        }

        private void RebuildLookup()
        {
            lookup.Clear();
            if (definitions == null)
                return;
            foreach (var definition in definitions)
            {
                if (definition != null && !string.IsNullOrEmpty(definition.MaterialId))
                    lookup[definition.MaterialId] = definition;
            }
        }
    }
}
