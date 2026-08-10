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
        public const int CurrentFormatVersion = 1;

        public int formatVersion = CurrentFormatVersion;
        public int moduleCapacityLevel;
        public int cpuCapacityLevel;
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
        public const int InitialModuleCapacity = 96;
        public const int InitialCpuCapacity = 2000;
        public const int AbsoluteModuleCapacity = 1024;
        public const int AbsoluteCpuCapacity = 9999;

        const string ProgressFileName = "ship_architecture.json";

        static readonly int[] ModuleCapacities =
        {
            InitialModuleCapacity,
            128,
            192,
            288,
            448,
            704,
            AbsoluteModuleCapacity
        };

        static readonly int[] CpuCapacities =
        {
            InitialCpuCapacity,
            2600,
            3400,
            4500,
            5900,
            7600,
            AbsoluteCpuCapacity
        };

        static readonly int[] UpgradeCosts =
        {
            100,
            160,
            260,
            420,
            680,
            1000
        };

        static ShipArchitectureProgressData cached;
        static string cachedSlotId;

        public static event Action ProgressChanged;

        public static int TierCount => ModuleCapacities.Length;

        public static int ModuleCapacity =>
            GetCapacity(
                ShipArchitectureBranch.ModuleCapacity,
                LoadOrCreate().moduleCapacityLevel);

        public static int CpuCapacity =>
            GetCapacity(
                ShipArchitectureBranch.CpuCapacity,
                LoadOrCreate().cpuCapacityLevel);

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
            if (level >= TierCount - 1)
            {
                nextCapacity = GetCapacity(branch, level);
                cost = 0;
                return false;
            }

            nextCapacity = GetCapacity(branch, level + 1);
            cost = UpgradeCosts[Mathf.Clamp(
                level,
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
            if (previousLevel >= TierCount - 1)
            {
                message = "该架构分支已达到最高授权。";
                return false;
            }

            if (branch == ShipArchitectureBranch.ModuleCapacity)
                data.moduleCapacityLevel++;
            else
                data.cpuCapacityLevel++;
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
                if (cached == null ||
                    cached.formatVersion !=
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
