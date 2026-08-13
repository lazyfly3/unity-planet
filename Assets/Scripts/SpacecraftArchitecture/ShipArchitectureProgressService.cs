using System;
using System.IO;
using UnityEngine;

namespace UnityPlanet.SpacecraftArchitecture
{
    public enum ShipArchitectureBranch
    {
        ModuleCapacity = 0,
        CpuCapacity = 1
    }

    [Serializable]
    public sealed class ShipArchitectureProgressData
    {
        public const int CurrentFormatVersion = 2;

        public int formatVersion = CurrentFormatVersion;
        public int moduleCapacityLevel;
        public int cpuCapacityLevel;
        // Version-one saves started at 96 modules / 2000 CPU. These floors
        // preserve their exact entitlement while new saves use the tighter
        // progression curve.
        public int minimumModuleCapacity;
        public int minimumCpuCapacity;
        public bool legacyBlueprintCapacityChecked;
        public long lastUpgradeUtcTicks;
    }

    /// <summary>
    /// Owns deterministic ship-capacity progression. This data deliberately
    /// lives outside both the random enhancement draw and the player-skill
    /// progression files; only the station UI chooses to share the wallet.
    /// </summary>
    public static class ShipArchitectureProgressService
    {
        public const int InitialModuleCapacity = 40;
        // The current default "1231" blueprint consumes 1096 CPU with the
        // runtime catalog. Keep only a four-point safety margin so a fresh
        // player must earn the first architecture upgrade before expanding it.
        public const int InitialCpuCapacity = 1100;
        public const int AbsoluteModuleCapacity = 1024;
        public const int AbsoluteCpuCapacity = 9999;

        const string ProgressFileName = "ship_architecture.json";

        static readonly int[] ModuleCapacities =
        {
            InitialModuleCapacity,
            56,
            80,
            128,
            224,
            448,
            AbsoluteModuleCapacity
        };

        static readonly int[] CpuCapacities =
        {
            InitialCpuCapacity,
            1800,
            2600,
            3800,
            5400,
            7400,
            AbsoluteCpuCapacity
        };

        static readonly int[] UpgradeCosts =
        {
            200,
            350,
            600,
            1000,
            1700,
            2800
        };

        static readonly int[] VersionOneModuleCapacities =
        {
            96, 128, 192, 288, 448, 704, 1024
        };

        static readonly int[] VersionOneCpuCapacities =
        {
            2000, 2600, 3400, 4500, 5900, 7600, 9999
        };

        static ShipArchitectureProgressData cached;
        static string cachedSlotId;

        public static event Action ProgressChanged;

        public static int TierCount => ModuleCapacities.Length;

        public static int ModuleCapacity =>
            GetCurrentCapacity(ShipArchitectureBranch.ModuleCapacity);

        public static int CpuCapacity =>
            GetCurrentCapacity(ShipArchitectureBranch.CpuCapacity);

        public static int GetCurrentCapacity(
            ShipArchitectureBranch branch)
        {
            return CurrentCapacity(branch, LoadOrCreate());
        }

        public static int GetLevel(ShipArchitectureBranch branch)
        {
            ShipArchitectureProgressData data = LoadOrCreate();
            return branch == ShipArchitectureBranch.ModuleCapacity
                ? data.moduleCapacityLevel
                : data.cpuCapacityLevel;
        }

        public static int GetCapacity(
            ShipArchitectureBranch branch,
            int level)
        {
            int[] capacities = branch ==
                ShipArchitectureBranch.ModuleCapacity
                    ? ModuleCapacities
                    : CpuCapacities;
            return capacities[Mathf.Clamp(level, 0, capacities.Length - 1)];
        }

        public static bool TryGetNextUpgrade(
            ShipArchitectureBranch branch,
            out int nextCapacity,
            out int cost)
        {
            int level = GetLevel(branch);
            int currentCapacity = GetCurrentCapacity(branch);
            int nextLevel = FindNextLevel(
                branch,
                level,
                currentCapacity);
            if (nextLevel < 0)
            {
                nextCapacity = currentCapacity;
                cost = 0;
                return false;
            }

            nextCapacity = GetCapacity(branch, nextLevel);
            cost = UpgradeCosts[Mathf.Clamp(
                nextLevel - 1,
                0,
                UpgradeCosts.Length - 1)];
            return true;
        }

        public static bool TryUpgrade(
            ShipArchitectureBranch branch,
            out string message)
        {
            ShipArchitectureProgressData data = LoadOrCreate();
            int previousLevel = branch ==
                ShipArchitectureBranch.ModuleCapacity
                    ? data.moduleCapacityLevel
                    : data.cpuCapacityLevel;
            int nextLevel = FindNextLevel(
                branch,
                previousLevel,
                CurrentCapacity(branch, data));
            if (nextLevel < 0)
            {
                message = "该架构分支已达到最高授权。";
                return false;
            }

            if (branch == ShipArchitectureBranch.ModuleCapacity)
                data.moduleCapacityLevel = nextLevel;
            else
                data.cpuCapacityLevel = nextLevel;
            data.lastUpgradeUtcTicks = DateTime.UtcNow.Ticks;

            try
            {
                SaveCached();
                message = branch == ShipArchitectureBranch.ModuleCapacity
                    ? "装配框架授权已提升。"
                    : "运算核心授权已提升。";
                ProgressChanged?.Invoke();
                return true;
            }
            catch (Exception exception)
            {
                if (branch == ShipArchitectureBranch.ModuleCapacity)
                    data.moduleCapacityLevel = previousLevel;
                else
                    data.cpuCapacityLevel = previousLevel;
                message = "架构进度保存失败：" + exception.Message;
                return false;
            }
        }

        /// <summary>
        /// Performs a one-time, minimum-tier migration for designs created
        /// before capacity progression existed. It never shrinks an existing
        /// ship and never grants more than the closest tier it actually needs.
        /// </summary>
        public static void EnsureSupportsLegacyBlueprint(
            int moduleCount,
            int cpuCost)
        {
            ShipArchitectureProgressData data = LoadOrCreate();
            if (data.legacyBlueprintCapacityChecked)
                return;

            data.moduleCapacityLevel = Mathf.Max(
                data.moduleCapacityLevel,
                FindSupportingLevel(ModuleCapacities, moduleCount));
            data.cpuCapacityLevel = Mathf.Max(
                data.cpuCapacityLevel,
                FindSupportingLevel(CpuCapacities, cpuCost));
            data.legacyBlueprintCapacityChecked = true;
            try
            {
                SaveCached();
            }
            catch (Exception exception)
            {
                // Keep the raised in-memory limits so a legacy ship never
                // becomes unloadable merely because its migration file could
                // not be committed during this session.
                data.legacyBlueprintCapacityChecked = false;
                Debug.LogWarning(
                    "[ShipArchitecture] 旧蓝图容量迁移暂未保存：" +
                    exception.Message);
            }
            ProgressChanged?.Invoke();
        }

        public static ShipArchitectureProgressData LoadOrCreate()
        {
            string slotId = ResolveSlotId();
            if (cached != null && string.Equals(
                    cachedSlotId,
                    slotId,
                    StringComparison.Ordinal))
            {
                return cached;
            }

            cachedSlotId = slotId;
            string path = GetPath(slotId);
            if (!File.Exists(path))
            {
                cached = new ShipArchitectureProgressData();
                SaveCached();
                return cached;
            }

            try
            {
                cached = JsonUtility.FromJson<
                    ShipArchitectureProgressData>(File.ReadAllText(path));
                if (cached == null)
                {
                    throw new InvalidDataException(
                        "不支持的舰体架构进度格式。");
                }
                if (cached.formatVersion == 1)
                {
                    MigrateFromVersionOne(cached);
                    try
                    {
                        SaveCached();
                    }
                    catch (Exception exception)
                    {
                        // The in-memory entitlement is already safe. Retry
                        // persistence next time instead of replacing a valid
                        // version-one save as though it were corrupt.
                        Debug.LogWarning(
                            "[ShipArchitecture] 旧架构授权迁移暂未保存：" +
                            exception.Message);
                    }
                }
                else if (cached.formatVersion !=
                         ShipArchitectureProgressData.CurrentFormatVersion)
                {
                    throw new InvalidDataException(
                        "不支持的舰体架构进度格式。");
                }
                Normalize(cached);
                return cached;
            }
            catch (Exception exception)
            {
                BackupCorruptFile(path);
                Debug.LogError(
                    "[ShipArchitecture] 架构进度损坏，已建立安全的新进度：" +
                    exception.Message);
                cached = new ShipArchitectureProgressData();
                SaveCached();
                return cached;
            }
        }

        static int FindSupportingLevel(int[] capacities, int requirement)
        {
            int safeRequirement = Mathf.Max(0, requirement);
            for (int level = 0; level < capacities.Length; level++)
            {
                if (safeRequirement <= capacities[level])
                    return level;
            }
            return capacities.Length - 1;
        }

        static int FindNextLevel(
            ShipArchitectureBranch branch,
            int currentLevel,
            int currentCapacity)
        {
            int[] capacities = branch ==
                ShipArchitectureBranch.ModuleCapacity
                    ? ModuleCapacities
                    : CpuCapacities;
            for (int level = Mathf.Max(0, currentLevel + 1);
                 level < capacities.Length;
                 level++)
            {
                if (capacities[level] > currentCapacity)
                    return level;
            }
            return -1;
        }

        static int CurrentCapacity(
            ShipArchitectureBranch branch,
            ShipArchitectureProgressData data)
        {
            int level = branch == ShipArchitectureBranch.ModuleCapacity
                ? data.moduleCapacityLevel
                : data.cpuCapacityLevel;
            int minimum = branch == ShipArchitectureBranch.ModuleCapacity
                ? data.minimumModuleCapacity
                : data.minimumCpuCapacity;
            return Mathf.Max(GetCapacity(branch, level), minimum);
        }

        static void MigrateFromVersionOne(
            ShipArchitectureProgressData data)
        {
            int oldModuleCapacity = VersionOneModuleCapacities[
                Mathf.Clamp(
                    data.moduleCapacityLevel,
                    0,
                    VersionOneModuleCapacities.Length - 1)];
            int oldCpuCapacity = VersionOneCpuCapacities[
                Mathf.Clamp(
                    data.cpuCapacityLevel,
                    0,
                    VersionOneCpuCapacities.Length - 1)];

            data.minimumModuleCapacity = oldModuleCapacity;
            data.minimumCpuCapacity = oldCpuCapacity;
            data.moduleCapacityLevel = FindHighestLevelAtOrBelow(
                ModuleCapacities,
                oldModuleCapacity);
            data.cpuCapacityLevel = FindHighestLevelAtOrBelow(
                CpuCapacities,
                oldCpuCapacity);
            data.formatVersion =
                ShipArchitectureProgressData.CurrentFormatVersion;
            Normalize(data);
        }

        static int FindHighestLevelAtOrBelow(
            int[] capacities,
            int entitlement)
        {
            int result = 0;
            for (int level = 1; level < capacities.Length; level++)
            {
                if (capacities[level] > entitlement)
                    break;
                result = level;
            }
            return result;
        }

        static void SaveCached()
        {
            if (cached == null)
                throw new InvalidOperationException("架构进度尚未载入。");

            Normalize(cached);
            cached.formatVersion =
                ShipArchitectureProgressData.CurrentFormatVersion;
            string path = GetPath(cachedSlotId ?? ResolveSlotId());
            string directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory ??
                throw new InvalidOperationException("无效的架构进度目录。"));
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(cached, true));
            if (File.Exists(path))
                File.Replace(temporary, path, null);
            else
                File.Move(temporary, path);
        }

        static void Normalize(ShipArchitectureProgressData data)
        {
            data.moduleCapacityLevel = Mathf.Clamp(
                data.moduleCapacityLevel,
                0,
                ModuleCapacities.Length - 1);
            data.cpuCapacityLevel = Mathf.Clamp(
                data.cpuCapacityLevel,
                0,
                CpuCapacities.Length - 1);
            data.minimumModuleCapacity = Mathf.Clamp(
                data.minimumModuleCapacity,
                0,
                AbsoluteModuleCapacity);
            data.minimumCpuCapacity = Mathf.Clamp(
                data.minimumCpuCapacity,
                0,
                AbsoluteCpuCapacity);
        }

        static string ResolveSlotId()
        {
            string slotId = GalaxyLaunchContext.SelectedSlotId;
            if (!string.IsNullOrWhiteSpace(slotId))
                return slotId;

            GalaxySaveSlotMetadata development =
                GalaxySaveSlotService.GetOrCreateDevelopmentSlot();
            GalaxyLaunchContext.SelectSlot(development.slotId);
            return development.slotId;
        }

        static string GetPath(string slotId)
        {
            return Path.Combine(
                GalaxySaveSlotService.GetSpacecraftDirectory(slotId),
                ProgressFileName);
        }

        static void BackupCorruptFile(string path)
        {
            try
            {
                string backup = path + ".corrupt." +
                                DateTime.UtcNow.ToString(
                                    "yyyyMMddHHmmssfff") +
                                ".json";
                File.Copy(path, backup, false);
            }
            catch (Exception backupException)
            {
                Debug.LogWarning(
                    "[ShipArchitecture] 无法备份损坏的架构进度：" +
                    backupException.Message);
            }
        }
    }
}
