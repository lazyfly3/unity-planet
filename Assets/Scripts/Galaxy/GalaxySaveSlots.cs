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
public sealed class BiotaDiscoveryRecord
{
    public string stableId;
    public BiotaDiscoveryType discoveryType;
    public string displayName;
    public string description;
    public string planetId;
    public string planetName;
    public long discoveredUtcTicks;
}

public enum PlanetSurfaceTopology
{
    LegacySphere = 0,
    InfinitePlanar = 1
}

[Serializable]
public sealed class GalaxySaveSlotMetadata
{
    public int formatVersion = 9;
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
    public int starterEquipmentVersion;
    public BiotaDiscoveryRecord[] biotaCodex = Array.Empty<BiotaDiscoveryRecord>();
    public bool developmentSlot;
    public double weatherTimeSeconds;
    public double universeTimeSeconds;
    public GalaxyMode galaxyMode;
    public long shipCoordinateX;
    public long shipCoordinateY;
    public long currentPlanetCoordinateX;
    public long currentPlanetCoordinateY;
    public int galaxyGeneratorVersion;
    public GalaxyShipFacing shipFacing = GalaxyShipFacing.Up;
    public long shipCoordinateZ;
    public long currentPlanetCoordinateZ;
    public double spacePositionX;
    public double spacePositionY;
    public double spacePositionZ;
    public bool hasHierarchicalSpacePosition;
    public long spaceSystemX;
    public long spaceSystemY;
    public long spaceSystemZ;
    public double spaceLocalX;
    public double spaceLocalY;
    public double spaceLocalZ;
    public string nearObservationPlanetId;
    public float spacecraftHullIntegrity = 100f;
    public VisitedPlanetRecord[] visitedPlanets = Array.Empty<VisitedPlanetRecord>();
    public PlanetSurfaceTopology surfaceTopology = PlanetSurfaceTopology.LegacySphere;
    public bool bossBridgeHintSeen;
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
    {
SelectedSlotId = slotId;
    
}

    public static void Clear()
    {
SelectedSlotId = null;
    
}
}

public static class GalaxySaveSlotService
{
    const string MetadataFileName = "slot.json";
    const string DevelopmentSlotId = "development";

    public static string RootDirectory => Path.Combine(Application.persistentDataPath, "galaxy_saves");

    public static IReadOnlyList<GalaxySaveSlotInfo> ListSlots()
    {
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
    
}

    public static GalaxySaveSlotMetadata CreateSlot(string displayName, int? requestedSeed)
    {
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
            currentPlanetId = ProceduralInterstellarGenerator.EncodePlanetId(InterstellarCoordinate.Zero),
            galaxyMode = GalaxyMode.Interstellar3DProcedural,
            galaxyGeneratorVersion = ProceduralInterstellarGenerator.CurrentVersion,
            surfaceTopology = PlanetSurfaceTopology.InfinitePlanar,
            shipFacing = GalaxyShipFacing.Up,
            spacePositionZ = 14000d
        };
        InitializePhysicalSpacePosition(metadata);
        SaveMetadata(metadata);
        return metadata;
    
}

    public static GalaxySaveSlotMetadata LoadMetadata(string slotId)
    {
ValidateSlotId(slotId);
        string path = Path.Combine(GetSlotDirectory(slotId), MetadataFileName);
        if (!File.Exists(path))
            return null;

        GalaxySaveSlotMetadata metadata = JsonUtility.FromJson<GalaxySaveSlotMetadata>(File.ReadAllText(path));
        ValidateMetadata(metadata, slotId);
        if (MigrateToInterstellarV7(metadata, path))
            SaveMetadata(metadata);
        return metadata;
    
}

    public static GalaxySaveSlotMetadata GetOrCreateDevelopmentSlot()
    {
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
            currentPlanetId = ProceduralInterstellarGenerator.EncodePlanetId(InterstellarCoordinate.Zero),
            developmentSlot = true,
            galaxyMode = GalaxyMode.Interstellar3DProcedural,
            galaxyGeneratorVersion = ProceduralInterstellarGenerator.CurrentVersion,
            surfaceTopology = PlanetSurfaceTopology.InfinitePlanar,
            shipFacing = GalaxyShipFacing.Up,
            spacePositionZ = 14000d
        };
        InitializePhysicalSpacePosition(metadata);
        SaveMetadata(metadata);
        return metadata;
    
}

    public static void SaveMetadata(GalaxySaveSlotMetadata metadata)
    {
if (metadata == null)
            throw new ArgumentNullException(nameof(metadata));
        metadata.formatVersion = 9;
        ValidateSlotId(metadata.slotId);
        metadata.displayName = NormalizeDisplayName(metadata.displayName);
        metadata.lastPlayedUtcTicks = DateTime.UtcNow.Ticks;
        if (metadata.inventory == null)
            metadata.inventory = Array.Empty<GalaxyInventorySaveEntry>();
        if (metadata.visitedPlanets == null)
            metadata.visitedPlanets = Array.Empty<VisitedPlanetRecord>();
        if (metadata.biotaCodex == null)
            metadata.biotaCodex = Array.Empty<BiotaDiscoveryRecord>();

        string directory = GetSlotDirectory(metadata.slotId);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, MetadataFileName);
        string temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonUtility.ToJson(metadata, true));
        if (File.Exists(path))
            File.Replace(temporaryPath, path, null);
        else
            File.Move(temporaryPath, path);
    
}

    public static void RenameSlot(string slotId, string newName)
    {
GalaxySaveSlotMetadata metadata = LoadMetadata(slotId)
            ?? throw new FileNotFoundException("Save slot metadata was not found.");
        metadata.displayName = NormalizeDisplayName(newName);
        SaveMetadata(metadata);
    
}

    public static void DeleteSlot(string slotId)
    {
ValidateSlotId(slotId);
        string root = Path.GetFullPath(RootDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string target = Path.GetFullPath(GetSlotDirectory(slotId));
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refused to delete a path outside the save root.");
        if (Directory.Exists(target))
            Directory.Delete(target, true);
    
}

    public static string GetPlanetsDirectory(string slotId)
    {
ValidateSlotId(slotId);
        return Path.Combine(GetSlotDirectory(slotId), "planets");
    
}

    public static string GetSpacecraftDirectory(string slotId)
    {
        ValidateSlotId(slotId);
        return Path.Combine(GetSlotDirectory(slotId), "spacecraft");
    }

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
        if (metadata == null || (metadata.formatVersion < 1 || metadata.formatVersion > 9)
            || metadata.slotId != expectedSlotId)
            throw new InvalidDataException("Invalid save slot metadata.");
        if (string.IsNullOrWhiteSpace(metadata.displayName))
            throw new InvalidDataException("Save slot has no display name.");
    }

    static bool MigrateToInterstellarV7(GalaxySaveSlotMetadata metadata, string metadataPath)
    {
        bool changed = false;
        if (metadata.visitedPlanets == null)
        {
            metadata.visitedPlanets = Array.Empty<VisitedPlanetRecord>();
            changed = true;
        }
        if (metadata.biotaCodex == null)
        {
            metadata.biotaCodex = Array.Empty<BiotaDiscoveryRecord>();
            changed = true;
        }

        if (metadata.galaxyMode == GalaxyMode.InfiniteProcedural)
        {
            string backupPath = metadataPath + ".pre-interstellar-v6.backup";
            if (!File.Exists(backupPath))
                File.Copy(metadataPath, backupPath);

            var visited = new List<VisitedPlanetRecord>(metadata.visitedPlanets);
            var used = new HashSet<InterstellarCoordinate>();
            foreach (VisitedPlanetRecord item in visited)
                if (item != null)
                    used.Add(item.Coordinate);

            string planetsDirectory = GetPlanetsDirectory(metadata.slotId);
            if (Directory.Exists(planetsDirectory))
            {
                foreach (string directory in Directory.GetDirectories(planetsDirectory))
                {
                    string definitionPath = Path.Combine(directory, "definition.json");
                    string worldPath = Path.Combine(directory, "world.planet.gz");
                    if (!File.Exists(definitionPath) && !File.Exists(worldPath))
                        continue;

                    string planetId = Path.GetFileName(directory);
                    if (visited.Exists(item => item != null && item.planetId == planetId))
                        continue;

                    GalaxyGeneratedPlanetRecord definition = null;
                    if (File.Exists(definitionPath))
                    {
                        try
                        {
                            definition = JsonUtility.FromJson<GalaxyGeneratedPlanetRecord>(File.ReadAllText(definitionPath));
                        }
                        catch (Exception)
                        {
                            // A damaged definition remains untouched and can still be diagnosed separately.
                        }
                    }

                    long sourceX = definition != null ? definition.coordinateX : 0L;
                    long sourceY = definition != null ? definition.coordinateY : 0L;
                    var coordinate = planetId == metadata.currentPlanetId && planetId.Contains("_0_0")
                        ? InterstellarCoordinate.Zero
                        : new InterstellarCoordinate(sourceX, 0L, sourceY);
                    while (used.Contains(coordinate))
                        coordinate.z++;
                    used.Add(coordinate);
                    visited.Add(new VisitedPlanetRecord
                    {
                        planetId = planetId,
                        displayName = definition != null && !string.IsNullOrWhiteSpace(definition.displayName)
                            ? definition.displayName
                            : planetId,
                        coordinateX = coordinate.x,
                        coordinateY = coordinate.y,
                        coordinateZ = coordinate.z,
                        lastLandingDirection = Vector3.up,
                        lastVisitedUtcTicks = metadata.lastPlayedUtcTicks
                    });
                }
            }

            VisitedPlanetRecord current = visited.Find(item => item != null && item.planetId == metadata.currentPlanetId);
            if (current == null && visited.Count > 0)
                current = visited[0];
            InterstellarCoordinate currentCoordinate = current != null ? current.Coordinate : InterstellarCoordinate.Zero;
            metadata.currentPlanetId = current != null
                ? current.planetId
                : ProceduralInterstellarGenerator.EncodePlanetId(currentCoordinate);
            metadata.currentPlanetCoordinateX = currentCoordinate.x;
            metadata.currentPlanetCoordinateY = currentCoordinate.y;
            metadata.currentPlanetCoordinateZ = currentCoordinate.z;
            metadata.shipCoordinateX = currentCoordinate.x;
            metadata.shipCoordinateY = currentCoordinate.y;
            metadata.shipCoordinateZ = currentCoordinate.z;
            metadata.galaxyMode = GalaxyMode.Interstellar3DProcedural;
            metadata.galaxyGeneratorVersion = ProceduralInterstellarGenerator.CurrentVersion;
            metadata.visitedPlanets = visited.ToArray();
            changed = true;
        }

        if (metadata.formatVersion < 6)
        {
            metadata.formatVersion = 6;
            changed = true;
        }

        // The development slot is disposable scene-test state. Keep production
        // LegacyFinite saves untouched, but make direct scene launches exercise
        // the current interstellar and large-planet pipeline.
        if (metadata.developmentSlot
            && metadata.galaxyMode != GalaxyMode.Interstellar3DProcedural)
        {
            string backupPath = metadataPath + ".pre-interstellar-development-v7.backup";
            if (File.Exists(metadataPath) && !File.Exists(backupPath))
                File.Copy(metadataPath, backupPath);

            metadata.galaxyMode = GalaxyMode.Interstellar3DProcedural;
            metadata.galaxyGeneratorVersion = ProceduralInterstellarGenerator.CurrentVersion;
            metadata.currentPlanetId = ProceduralInterstellarGenerator.EncodePlanetId(InterstellarCoordinate.Zero);
            metadata.shipCoordinateX = 0L;
            metadata.shipCoordinateY = 0L;
            metadata.shipCoordinateZ = 0L;
            metadata.currentPlanetCoordinateX = 0L;
            metadata.currentPlanetCoordinateY = 0L;
            metadata.currentPlanetCoordinateZ = 0L;
            metadata.spacePositionX = 0d;
            metadata.spacePositionY = 0d;
            metadata.spacePositionZ = 14000d;
            changed = true;
        }

        if (metadata.formatVersion < 7)
        {
            string backupPath = metadataPath + ".pre-large-planets-v7.backup";
            if (File.Exists(metadataPath) && !File.Exists(backupPath))
                File.Copy(metadataPath, backupPath);

            // Version 6 interstellar saves commonly started at the universe origin.
            // Version 7 reserves that coordinate for the 2 km origin planet itself.
            double distanceSquared = metadata.spacePositionX * metadata.spacePositionX
                + metadata.spacePositionY * metadata.spacePositionY
                + metadata.spacePositionZ * metadata.spacePositionZ;
            if (metadata.galaxyMode == GalaxyMode.Interstellar3DProcedural
                && distanceSquared < 5000d * 5000d)
            {
                metadata.spacePositionX = 0d;
                metadata.spacePositionY = 0d;
                metadata.spacePositionZ = 14000d;
            }

            metadata.formatVersion = 7;
            changed = true;
        }
        if (metadata.formatVersion < 8)
        {
            string backupPath = metadataPath + ".pre-physical-universe-v8.backup";
            if (File.Exists(metadataPath) && !File.Exists(backupPath))
                File.Copy(metadataPath, backupPath);

            if (metadata.galaxyMode == GalaxyMode.Interstellar3DProcedural)
                InitializePhysicalSpacePosition(metadata);
            metadata.galaxyGeneratorVersion = ProceduralInterstellarGenerator.CurrentVersion;
            metadata.formatVersion = 8;
            changed = true;
        }
        if (metadata.formatVersion < 9)
        {
            string backupPath = metadataPath + ".pre-planar-surface-v9.backup";
            if (File.Exists(metadataPath) && !File.Exists(backupPath))
                File.Copy(metadataPath, backupPath);

            // Existing saves deliberately remain on the legacy sphere. Only
            // newly-created slots opt into the infinite planar surface.
            metadata.surfaceTopology = PlanetSurfaceTopology.LegacySphere;
            metadata.formatVersion = 9;
            changed = true;
        }
        return changed;
    }

    static void InitializePhysicalSpacePosition(GalaxySaveSlotMetadata metadata)
    {
        if (metadata == null)
            return;
        var generator = new ProceduralInterstellarGenerator(
            metadata.worldSeed,
            Array.Empty<GalaxyResourceCatalogEntry>());
        var planetCoordinate = new InterstellarCoordinate(
            metadata.currentPlanetCoordinateX,
            metadata.currentPlanetCoordinateY,
            metadata.currentPlanetCoordinateZ);
        UniversePosition planet = generator.GetPlanetUniverseAddress(
            planetCoordinate,
            metadata.universeTimeSeconds);
        GalaxyPlanetDefinition definition = generator.GeneratePlanet(planetCoordinate);
        double radius = definition?.celestial?.Physical.radiusMeters
            ?? PlanetPhysicalProfile.EarthRadiusMeters;
        double nearDistance = radius * 4.2d;
        UniversePosition ship = planet.Add(new DoubleVector3(0d, 0d, nearDistance));
        metadata.hasHierarchicalSpacePosition = true;
        metadata.spaceSystemX = ship.systemCoordinate.x;
        metadata.spaceSystemY = ship.systemCoordinate.y;
        metadata.spaceSystemZ = ship.systemCoordinate.z;
        metadata.spaceLocalX = ship.localMeters.x;
        metadata.spaceLocalY = ship.localMeters.y;
        metadata.spaceLocalZ = ship.localMeters.z;
        DoubleVector3 legacy = ship.ToAbsoluteMeters();
        metadata.spacePositionX = legacy.x;
        metadata.spacePositionY = legacy.y;
        metadata.spacePositionZ = legacy.z;
        metadata.shipCoordinateX = planetCoordinate.x;
        metadata.shipCoordinateY = planetCoordinate.y;
        metadata.shipCoordinateZ = planetCoordinate.z;
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
