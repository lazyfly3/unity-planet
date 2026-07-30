using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityPlanet.ModularAssembly
{
    public enum NeoXCombatEffectStage
    {
        Hit,
        Destroy,
        Detached,
        Explosion,
        Muzzle,
        Projectile,
        Trail,
        Shield
    }

    [Serializable]
    public sealed class NeoXCombatEffectCatalogEntry
    {
        public string sourceId;
        public string sourcePath;
        public string family;
        public NeoXCombatEffectStage stage;
        public GameObject prefab;
        public AudioClip audioClip;
        public bool degraded;
        [TextArea] public string fallbackReason;
    }

    [Serializable]
    public sealed class NeoXEffectConversionRecord
    {
        public string sourceId;
        public string sourceFile;
        public string outputPrefab;
        public string outputMaterial;
        public string outputTexture;
        public string outputAudio;
        public string[] dependencies;
        public string[] supportedNodes;
        public string[] degradedNodes;
        public bool succeeded;
        public bool degraded;
        public string failureReason;
    }

    [CreateAssetMenu(
        fileName = "NeoXCombatEffectCatalog",
        menuName = "Unity Planet/NeoX Combat Effect Catalog")]
    public sealed class NeoXCombatEffectCatalog : ScriptableObject
    {
        [SerializeField] private List<NeoXCombatEffectCatalogEntry> entries = new();

        private Dictionary<string, NeoXCombatEffectCatalogEntry> byId;

        public IReadOnlyList<NeoXCombatEffectCatalogEntry> Entries => entries;

        public bool TryGet(string sourceId, out NeoXCombatEffectCatalogEntry entry)
        {
            if (byId == null)
            {
                RebuildLookup();
            }

            return byId.TryGetValue(sourceId ?? string.Empty, out entry);
        }

#if UNITY_EDITOR
        public void EditorReplaceEntries(List<NeoXCombatEffectCatalogEntry> replacement)
        {
            entries = replacement ?? new List<NeoXCombatEffectCatalogEntry>();
            RebuildLookup();
        }
#endif

        private void OnEnable()
        {
            RebuildLookup();
        }

        private void RebuildLookup()
        {
            byId = new Dictionary<string, NeoXCombatEffectCatalogEntry>(
                StringComparer.OrdinalIgnoreCase);
            foreach (NeoXCombatEffectCatalogEntry entry in entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.sourceId))
                {
                    continue;
                }

                byId[entry.sourceId] = entry;
            }
        }
    }
}
