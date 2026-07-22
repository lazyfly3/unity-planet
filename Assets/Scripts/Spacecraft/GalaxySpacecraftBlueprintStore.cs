using System;
using System.IO;
using SpacecraftEditor;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class GalaxySpacecraftBlueprintStore : MonoBehaviour, ISpacecraftBlueprintStore
{
    const string BlueprintFileName = "ship.json";
    string activeSlotId;

    public string ActiveSlotId
    {
        get
        {
            EnsureSlot();
            return activeSlotId;
        }
    }

    public bool TryLoad(out SpacecraftBlueprintData blueprint)
    {
        blueprint = null;
        string path = GetBlueprintPath();
        if (!File.Exists(path))
            return false;
        try
        {
            blueprint = JsonUtility.FromJson<SpacecraftBlueprintData>(File.ReadAllText(path));
            if (blueprint == null || blueprint.formatVersion < 1 || blueprint.formatVersion > 2 ||
                string.IsNullOrWhiteSpace(blueprint.hullId))
                throw new InvalidDataException("Unsupported or incomplete spacecraft blueprint.");
            if (blueprint.parts == null)
                blueprint.parts = Array.Empty<PlacedPartState>();

            // Version 1 had no paint or weapon-group fields. JsonUtility leaves those
            // values empty/zero, which is exactly the compatibility fallback expected
            // by the assembly when it restores the old thruster-only blueprint.
            return true;
        }
        catch (Exception exception)
        {
            string backup = Path.Combine(
                Path.GetDirectoryName(path) ?? string.Empty,
                $"ship.{DateTime.UtcNow:yyyyMMddHHmmssfff}.corrupt.json");
            try
            {
                File.Move(path, backup);
            }
            catch (Exception backupException)
            {
                Debug.LogWarning($"Could not back up damaged spacecraft blueprint: {backupException.Message}", this);
            }
            Debug.LogError($"Spacecraft blueprint was damaged and has been isolated: {exception.Message}", this);
            blueprint = null;
            return false;
        }
    }

    public void Save(SpacecraftBlueprintData blueprint)
    {
        if (blueprint == null)
            throw new ArgumentNullException(nameof(blueprint));
        blueprint.formatVersion = 2;
        if (blueprint.parts == null)
            blueprint.parts = Array.Empty<PlacedPartState>();
        blueprint.savedUtcTicks = DateTime.UtcNow.Ticks;

        string path = GetBlueprintPath();
        string directory = Path.GetDirectoryName(path);
        Directory.CreateDirectory(directory ?? throw new InvalidOperationException("Invalid spacecraft save directory."));
        string temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonUtility.ToJson(blueprint, true));
        if (File.Exists(path))
            File.Replace(temporaryPath, path, null);
        else
            File.Move(temporaryPath, path);
    }

    string GetBlueprintPath()
    {
        EnsureSlot();
        return Path.Combine(GalaxySaveSlotService.GetSpacecraftDirectory(activeSlotId), BlueprintFileName);
    }

    void EnsureSlot()
    {
        if (!string.IsNullOrEmpty(activeSlotId))
            return;
        activeSlotId = GalaxyLaunchContext.SelectedSlotId;
        if (string.IsNullOrWhiteSpace(activeSlotId))
        {
            GalaxySaveSlotMetadata development = GalaxySaveSlotService.GetOrCreateDevelopmentSlot();
            activeSlotId = development.slotId;
            GalaxyLaunchContext.SelectSlot(activeSlotId);
        }
        else if (GalaxySaveSlotService.LoadMetadata(activeSlotId) == null)
        {
            throw new InvalidDataException($"Galaxy save slot '{activeSlotId}' does not exist.");
        }
    }
}
