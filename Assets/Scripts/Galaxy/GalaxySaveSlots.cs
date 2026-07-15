using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public sealed class GalaxyInventorySaveEntry
{
    public string itemId;
    public string displayName;
    public int maxStack;
    public int amount;
    public string iconPngBase64;
}

[Serializable]
public sealed class GalaxySaveSlotMetadata
{
    public int formatVersion = 4;
    public string slotId;
    public string displayName;
    public int worldSeed;
    public long createdUtcTicks;
    public long lastPlayedUtcTicks;
    public string currentPlanetId = "origin";
    public int shipGridX = -1;
    public int shipGridY = -1;
    public int selectedInventorySlot;
    public GalaxyInventorySaveEntry[] inventory = Array.Empty<GalaxyInventorySaveEntry>();
    public bool developmentSlot;
    public double weatherTimeSeconds;
    public GalaxyMode galaxyMode;
    public long shipCoordinateX;
    public long shipCoordinateY;
    public long currentPlanetCoordinateX;
    public long currentPlanetCoordinateY;
    public int galaxyGeneratorVersion;
    public GalaxyShipFacing shipFacing = GalaxyShipFacing.Up;
}

public sealed class GalaxySaveSlotInfo
{
    public string SlotId { get; set; }
    public string DisplayName { get; set; }
    public GalaxySaveSlotMetadata Metadata { get; set; }
    public bool IsCorrupt { get; set; }
    public string Error { get; set; }
}

public static class GalaxyLaunchContext
{
    public static string SelectedSlotId { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetForPlaySession()
    {
        SelectedSlotId = null;
    }

    public static void SelectSlot(string slotId)
    {if(FSPDebuger.EnableLogTrackInternal){FSPDebuger.PushDepth();FSPDebuger.LogTrack(27);}
        SelectedSlotId = slotId;
    
    if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.PopDepth();}

    public static void Clear()
    {if(FSPDebuger.EnableLogTrackInternal){FSPDebuger.PushDepth();FSPDebuger.LogTrack(28);}
        SelectedSlotId = null;
    
    if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.PopDepth();}
}

public static class GalaxySaveSlotService
{
    const string MetadataFileName = "slot.json";
    const string DevelopmentSlotId = "development";

    public static string RootDirectory => Path.Combine(Application.persistentDataPath, "galaxy_saves");

    public static IReadOnlyList<GalaxySaveSlotInfo> ListSlots()
    {if(FSPDebuger.EnableLogTrackInternal){FSPDebuger.PushDepth();FSPDebuger.LogTrack(29);}
        Directory.CreateDirectory(RootDirectory);
        var slots = new List<GalaxySaveSlotInfo>();
        foreach (string directory in Directory.GetDirectories(RootDirectory))
        {
            string slotId = Path.GetFileName(directory);
            if (slotId == DevelopmentSlotId)
                continue;

            string metadataPath = Path.Combine(directory, MetadataFileName);
            try
            {
                if (!File.Exists(metadataPath))
                    throw new InvalidDataException("Missing slot.json");

                GalaxySaveSlotMetadata metadata = JsonUtility.FromJson<GalaxySaveSlotMetadata>(File.ReadAllText(metadataPath));
                ValidateMetadata(metadata, slotId);
                slots.Add(new GalaxySaveSlotInfo
                {
                    SlotId = slotId,
                    DisplayName = metadata.displayName,
                    Metadata = metadata
                });
            }
            catch (Exception exception)
            {
                slots.Add(new GalaxySaveSlotInfo
                {
                    SlotId = slotId,
                    DisplayName = $"损坏的存档 ({slotId})",
                    IsCorrupt = true,
                    Error = exception.Message
                });
            }
        }

        slots.Sort((left, right) =>
        {
            long leftTicks = left.Metadata != null ? left.Metadata.lastPlayedUtcTicks : 0L;
            long rightTicks = right.Metadata != null ? right.Metadata.lastPlayedUtcTicks : 0L;
            return rightTicks.CompareTo(leftTicks);
        });
        return slots;
    
    if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.PopDepth();}

    public static GalaxySaveSlotMetadata CreateSlot(string displayName, int? requestedSeed)
    {if(FSPDebuger.EnableLogTrackInternal){FSPDebuger.PushDepth();FSPDebuger.LogTrack(30, requestedSeed.GetValueOrDefault());}
        displayName = NormalizeDisplayName(displayName);
        string slotId = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        long now = DateTime.UtcNow.Ticks;
        var metadata = new GalaxySaveSlotMetadata
        {
            slotId = slotId,
            displayName = displayName,
            worldSeed = requestedSeed ?? CreateRandomSeed(),
            createdUtcTicks = now,
            lastPlayedUtcTicks = now,
            currentPlanetId = ProceduralGalaxyGenerator.EncodePlanetId(GalaxyCoordinate.Zero),
            galaxyMode = GalaxyMode.InfiniteProcedural,
            galaxyGeneratorVersion = ProceduralGalaxyGenerator.CurrentVersion,
            shipFacing = GalaxyShipFacing.Up
        };
        SaveMetadata(metadata);
        return metadata;
    
    if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.PopDepth();}

    public static GalaxySaveSlotMetadata LoadMetadata(string slotId)
    {if(FSPDebuger.EnableLogTrackInternal){FSPDebuger.PushDepth();FSPDebuger.LogTrack(31);}
        ValidateSlotId(slotId);
        string path = Path.Combine(GetSlotDirectory(slotId), MetadataFileName);
        if (!File.Exists(path))
            return null;

        GalaxySaveSlotMetadata metadata = JsonUtility.FromJson<GalaxySaveSlotMetadata>(File.ReadAllText(path));
        ValidateMetadata(metadata, slotId);
        return metadata;
    
    if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.PopDepth();}

    public static GalaxySaveSlotMetadata GetOrCreateDevelopmentSlot()
    {if(FSPDebuger.EnableLogTrackInternal){FSPDebuger.PushDepth();FSPDebuger.LogTrack(32);}
        GalaxySaveSlotMetadata existing = LoadMetadata(DevelopmentSlotId);
        if (existing != null)
            return existing;

        long now = DateTime.UtcNow.Ticks;
        var metadata = new GalaxySaveSlotMetadata
        {
            slotId = DevelopmentSlotId,
            displayName = "开发测试",
            worldSeed = 7319,
            createdUtcTicks = now,
            lastPlayedUtcTicks = now,
            currentPlanetId = "origin",
            developmentSlot = true,
            galaxyMode = GalaxyMode.LegacyFinite,
            shipFacing = GalaxyShipFacing.Up
        };
        SaveMetadata(metadata);
        return metadata;
    
    if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.PopDepth();}

    public static void SaveMetadata(GalaxySaveSlotMetadata metadata)
    {if(FSPDebuger.EnableLogTrackInternal){FSPDebuger.PushDepth();FSPDebuger.LogTrack(33);}
        if (metadata == null)
            throw new ArgumentNullException(nameof(metadata));
        metadata.formatVersion = 4;
        ValidateSlotId(metadata.slotId);
        metadata.displayName = NormalizeDisplayName(metadata.displayName);
        metadata.lastPlayedUtcTicks = DateTime.UtcNow.Ticks;
        if (metadata.inventory == null)
            metadata.inventory = Array.Empty<GalaxyInventorySaveEntry>();

        string directory = GetSlotDirectory(metadata.slotId);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, MetadataFileName);
        string temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonUtility.ToJson(metadata, true));
        if (File.Exists(path))
            File.Replace(temporaryPath, path, null);
        else
            File.Move(temporaryPath, path);
    
    if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.PopDepth();}

    public static void RenameSlot(string slotId, string newName)
    {if(FSPDebuger.EnableLogTrackInternal){FSPDebuger.PushDepth();FSPDebuger.LogTrack(34);}
        GalaxySaveSlotMetadata metadata = LoadMetadata(slotId)
            ?? throw new FileNotFoundException("Save slot metadata was not found.");
        metadata.displayName = NormalizeDisplayName(newName);
        SaveMetadata(metadata);
    
    if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.PopDepth();}

    public static void DeleteSlot(string slotId)
    {if(FSPDebuger.EnableLogTrackInternal){FSPDebuger.PushDepth();FSPDebuger.LogTrack(35);}
        ValidateSlotId(slotId);
        string root = Path.GetFullPath(RootDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string target = Path.GetFullPath(GetSlotDirectory(slotId));
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refused to delete a path outside the save root.");
        if (Directory.Exists(target))
            Directory.Delete(target, true);
    
    if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.PopDepth();}

    public static string GetPlanetsDirectory(string slotId)
    {if(FSPDebuger.EnableLogTrackInternal){FSPDebuger.PushDepth();FSPDebuger.LogTrack(36);}
        ValidateSlotId(slotId);
        return Path.Combine(GetSlotDirectory(slotId), "planets");
    
    if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.PopDepth();}

    static string GetSlotDirectory(string slotId)
    {
        return Path.Combine(RootDirectory, slotId);
    }

    static string NormalizeDisplayName(string value)
    {
        string name = string.IsNullOrWhiteSpace(value) ? "新的世界" : value.Trim();
        if (name.Length > 24)
            name = name.Substring(0, 24);
        return name;
    }

    static void ValidateMetadata(GalaxySaveSlotMetadata metadata, string expectedSlotId)
    {
        if (metadata == null || (metadata.formatVersion < 1 || metadata.formatVersion > 4)
            || metadata.slotId != expectedSlotId)
            throw new InvalidDataException("Invalid save slot metadata.");
        if (string.IsNullOrWhiteSpace(metadata.displayName))
            throw new InvalidDataException("Save slot has no display name.");
    }

    static void ValidateSlotId(string slotId)
    {
        if (string.IsNullOrWhiteSpace(slotId)
            || slotId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || slotId.Contains("..")
            || slotId.Contains("/")
            || slotId.Contains("\\"))
        {
            throw new ArgumentException("Invalid save slot id.", nameof(slotId));
        }
    }

    static int CreateRandomSeed()
    {
        unchecked
        {
            int seed = Environment.TickCount ^ Guid.NewGuid().GetHashCode();
            return seed == 0 ? 1 : seed;
        }
    }
}
