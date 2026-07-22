using System.Collections.Generic;
using UnityEngine;

namespace SpacecraftEditor
{
    public sealed class PartCatalog : MonoBehaviour
    {
        [SerializeField] private ShipPartDefinition[] definitions;
        private readonly Dictionary<string, ShipPartDefinition> lookup = new Dictionary<string, ShipPartDefinition>();

        public IReadOnlyList<ShipPartDefinition> Definitions => definitions;

        private void Awake()
        {
            RebuildLookup();
        }

        public void Configure(ShipPartDefinition[] values)
        {
            definitions = values;
            RebuildLookup();
        }

        public ShipPartDefinition Find(string id)
        {
            if (lookup.Count == 0)
                RebuildLookup();
            ShipPartDefinition result;
            return lookup.TryGetValue(id ?? string.Empty, out result) ? result : null;
        }

        private void RebuildLookup()
        {
            lookup.Clear();
            if (definitions == null)
                return;
            foreach (var definition in definitions)
            {
                if (definition != null && !string.IsNullOrEmpty(definition.PartId))
                    lookup[definition.PartId] = definition;
            }
        }
    }
}
