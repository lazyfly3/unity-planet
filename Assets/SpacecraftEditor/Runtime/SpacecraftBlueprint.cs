using System;

namespace SpacecraftEditor
{
    [Serializable]
    public sealed class SpacecraftBlueprintData
    {
        public int formatVersion = 2;
        public string hullId;
        public string hullMaterialId;
        public PlacedPartState[] parts = Array.Empty<PlacedPartState>();
        public long savedUtcTicks;
    }

    public interface ISpacecraftBlueprintStore
    {
        string ActiveSlotId { get; }
        bool TryLoad(out SpacecraftBlueprintData blueprint);
        void Save(SpacecraftBlueprintData blueprint);
    }
}
