using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public sealed class PlanetMissionCompletionRecord
{
    public string planetId;
    public string missionId;
    public int completionCount;
    public int bestScore;
    public int bestGalaxyCoinReward;
    public long lastCompletedUtcTicks;
}

[Serializable]
public sealed class PlanetMissionProgressData
{
    public const int CurrentFormatVersion = 1;

    public int formatVersion = CurrentFormatVersion;
    public bool allMissionsUnlocked;
    public PlanetMissionCompletionRecord[] completedMissions =
        Array.Empty<PlanetMissionCompletionRecord>();
}

/// <summary>
/// Per-save, deterministic mission completion ledger. It is intentionally
/// independent from ship upgrades and random enhancements. The orbit hub uses
/// it for real Boss gating; settlement records successful runs exactly once.
/// </summary>
public static class PlanetMissionProgressService
{
    const string ProgressFileName = "planet_missions.json";

    static PlanetMissionProgressData cached;
    static string cachedSlotId;

    public static bool IsCompleted(string planetId, string missionId)
    {
        PlanetMissionProgressData data = LoadOrCreate();
        return Find(data, planetId, missionId) != null;
    }

    public static bool AreAllMissionsUnlocked()
    {
        return LoadOrCreate().allMissionsUnlocked;
    }

    public static bool UnlockAllForTesting()
    {
        PlanetMissionProgressData data = LoadOrCreate();
        if (data.allMissionsUnlocked)
            return false;

        data.allMissionsUnlocked = true;
        SaveCached();
        return true;
    }

    public static int CountCompleted(
        string planetId,
        params string[] missionIds)
    {
        if (missionIds == null || missionIds.Length == 0)
            return 0;

        int count = 0;
        for (int index = 0; index < missionIds.Length; index++)
        {
            if (IsCompleted(planetId, missionIds[index]))
                count++;
        }
        return count;
    }

    public static void RecordCompletion(
        string planetId,
        string missionId,
        int score,
        int galaxyCoinReward)
    {
        if (string.IsNullOrWhiteSpace(planetId) ||
            string.IsNullOrWhiteSpace(missionId))
        {
            return;
        }

        PlanetMissionProgressData data = LoadOrCreate();
        var records = new List<PlanetMissionCompletionRecord>(
            data.completedMissions ??
            Array.Empty<PlanetMissionCompletionRecord>());
        PlanetMissionCompletionRecord record =
            Find(data, planetId, missionId);
        if (record == null)
        {
            record = new PlanetMissionCompletionRecord
            {
                planetId = planetId,
                missionId = missionId
            };
            records.Add(record);
        }

        record.completionCount = Mathf.Max(0, record.completionCount) + 1;
        record.bestScore = Mathf.Max(record.bestScore, score);
        record.bestGalaxyCoinReward = Mathf.Max(
            record.bestGalaxyCoinReward,
            galaxyCoinReward);
        record.lastCompletedUtcTicks = DateTime.UtcNow.Ticks;
        data.completedMissions = records.ToArray();
        SaveCached();
    }

    public static PlanetMissionProgressData LoadOrCreate()
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
            cached = new PlanetMissionProgressData();
            SaveCached();
            return cached;
        }

        try
        {
            cached = JsonUtility.FromJson<PlanetMissionProgressData>(
                File.ReadAllText(path));
            if (cached == null ||
                cached.formatVersion !=
                PlanetMissionProgressData.CurrentFormatVersion)
            {
                throw new InvalidDataException(
                    "不支持的星球任务进度格式。");
            }
            Normalize(cached);
            return cached;
        }
        catch (Exception exception)
        {
            BackupCorruptFile(path);
            Debug.LogError(
                "[PlanetMissionProgress] 进度损坏，已建立安全的新进度：" +
                exception.Message);
            cached = new PlanetMissionProgressData();
            SaveCached();
            return cached;
        }
    }

    static PlanetMissionCompletionRecord Find(
        PlanetMissionProgressData data,
        string planetId,
        string missionId)
    {
        if (data == null || data.completedMissions == null)
            return null;

        for (int index = 0; index < data.completedMissions.Length; index++)
        {
            PlanetMissionCompletionRecord record =
                data.completedMissions[index];
            if (record != null &&
                string.Equals(
                    record.planetId,
                    planetId,
                    StringComparison.Ordinal) &&
                string.Equals(
                    record.missionId,
                    missionId,
                    StringComparison.Ordinal))
            {
                return record;
            }
        }
        return null;
    }

    static void SaveCached()
    {
        Normalize(cached);
        cached.formatVersion =
            PlanetMissionProgressData.CurrentFormatVersion;
        string path = GetPath(cachedSlotId ?? ResolveSlotId());
        string directory = Path.GetDirectoryName(path);
        Directory.CreateDirectory(directory ??
            throw new InvalidOperationException("无效的任务进度目录。"));
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonUtility.ToJson(cached, true));
        if (File.Exists(path))
            File.Replace(temporary, path, null);
        else
            File.Move(temporary, path);
    }

    static void Normalize(PlanetMissionProgressData data)
    {
        if (data == null)
            throw new InvalidOperationException("任务进度尚未载入。");
        if (data.completedMissions == null)
        {
            data.completedMissions =
                Array.Empty<PlanetMissionCompletionRecord>();
        }
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
                "[PlanetMissionProgress] 无法备份损坏的进度：" +
                backupException.Message);
        }
    }
}
