using System.Collections.Generic;
using UnityEngine;

namespace SpacecraftEditor
{
    public sealed class HullCatalog : MonoBehaviour
    {
        [SerializeField] private ShipHullDefinition[] definitions;
        private readonly Dictionary<string, ShipHullDefinition> lookup = new Dictionary<string, ShipHullDefinition>();

        public IReadOnlyList<ShipHullDefinition> Definitions => definitions;
        public ShipHullDefinition DefaultDefinition => definitions != null && definitions.Length > 0 ? definitions[0] : null;

        private void Awake()
        {
            RebuildLookup();
        }

        public void Configure(ShipHullDefinition[] values)
        {
            definitions = values;
            RebuildLookup();
        }

        public ShipHullDefinition Find(string id)
        {
            if (lookup.Count == 0)
                RebuildLookup();
            ShipHullDefinition result;
            return lookup.TryGetValue(id ?? string.Empty, out result) ? result : null;
        }

        private void RebuildLookup()
        {
            lookup.Clear();
            if (definitions == null)
                return;
            foreach (var definition in definitions)
            {
                if (definition != null && !string.IsNullOrEmpty(definition.HullId))
                    lookup[definition.HullId] = definition;
            }
        }
    }
}
