using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace UnityPlanet.SpaceStation.Enhancement
{
    [Serializable]
    public sealed class AcquiredEnhancementData
    {
        public string definitionId;
        public int stacks;
        public float totalMagnitude;
        public long lastPurchasedUtcTicks;
    }

    [Serializable]
    public sealed class GalaxyEnhancementProgressData
    {
        public const int CurrentFormatVersion = 1;
        public int formatVersion = CurrentFormatVersion;
        public int galaxyCoins = GalaxyCurrencyService.StartingBalance;
        public int offerSequence;
        public int drawsWithoutGold;
        public bool currentDrawPaid;
        public bool[] revealedCards = new bool[
            EnhancementPcgRules.CardsPerDraw];
        public EnhancementOfferData[] currentOffers =
            Array.Empty<EnhancementOfferData>();
        public AcquiredEnhancementData[] acquiredEnhancements =
            Array.Empty<AcquiredEnhancementData>();
    }

    public static class GalaxyCurrencyService
    {
        public const int StartingBalance = 300;
        const string ProgressFileName = "enhancement_progress.json";

        public static event Action<int> BalanceChanged;

        public static GalaxyEnhancementProgressData LoadOrCreate()
        {
            string path = GetPath();
            if (!File.Exists(path))
            {
                var created = new GalaxyEnhancementProgressData();
                Save(created);
                return created;
            }

            try
            {
                GalaxyEnhancementProgressData data =
                    JsonUtility.FromJson<GalaxyEnhancementProgressData>(
                        File.ReadAllText(path));
                if (data == null ||
                    data.formatVersion !=
                    GalaxyEnhancementProgressData.CurrentFormatVersion)
                {
                    throw new InvalidDataException(
                        "不支持的强化进度格式。");
                }
                Normalize(data);
                return data;
            }
            catch (Exception exception)
            {
                string backup = path + ".corrupt." +
                                DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") +
                                ".json";
                try
                {
                    File.Copy(path, backup, false);
                }
                catch (Exception backupException)
                {
                    Debug.LogWarning(
                        "[Enhancement] 无法备份损坏的强化进度：" +
                        backupException.Message);
                }

                Debug.LogError(
                    "[Enhancement] 强化进度损坏，已建立安全的新进度：" +
                    exception.Message);
                var replacement = new GalaxyEnhancementProgressData();
                Save(replacement);
                return replacement;
            }
        }

        public static void Save(GalaxyEnhancementProgressData data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }
            Normalize(data);
            data.formatVersion =
                GalaxyEnhancementProgressData.CurrentFormatVersion;

            string path = GetPath();
            string directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory ??
                throw new InvalidOperationException("无效的强化进度目录。"));
            string temporary = path + ".tmp";
            File.WriteAllText(
                temporary,
                JsonUtility.ToJson(data, true));
            if (File.Exists(path))
            {
                File.Replace(temporary, path, null);
            }
            else
            {
                File.Move(temporary, path);
            }
            BalanceChanged?.Invoke(data.galaxyCoins);
        }

        public static bool TryBeginDraw(
            GalaxyEnhancementProgressData data,
            int drawCost,
            out string message)
        {
            if (data == null)
            {
                message = "强化进度无效。";
                return false;
            }
            if (data.currentDrawPaid)
            {
                message = "本轮推演费用已经支付。";
                return true;
            }
            if (drawCost <= 0 || data.galaxyCoins < drawCost)
            {
                message = "银河币不足，无法开始本轮翻牌。";
                return false;
            }

            data.galaxyCoins -= drawCost;
            data.currentDrawPaid = true;
            Save(data);
            message = "推演已启动，请逐张翻开三张卡牌。";
            return true;
        }

        public static bool TryAcquireFreeEnhancement(
            GalaxyEnhancementProgressData data,
            EnhancementOfferData offer,
            out string message)
        {
            if (data == null || offer == null ||
                !EnhancementTypeCatalog.TryGet(
                    offer.definitionId,
                    out EnhancementTypeDefinition definition))
            {
                message = "强化方案无效。";
                return false;
            }
            var acquired = new List<AcquiredEnhancementData>(
                data.acquiredEnhancements ??
                Array.Empty<AcquiredEnhancementData>());
            AcquiredEnhancementData entry = acquired.Find(item =>
                item != null && item.definitionId == definition.Id);
            if (entry == null)
            {
                entry = new AcquiredEnhancementData
                {
                    definitionId = definition.Id
                };
                acquired.Add(entry);
            }
            if (entry.stacks >= definition.MaximumStacks)
            {
                message = "该强化已达到当前叠加上限。";
                return false;
            }

            entry.stacks++;
            entry.totalMagnitude = Mathf.Round(
                (entry.totalMagnitude + offer.magnitude) * 10f) / 10f;
            entry.lastPurchasedUtcTicks = DateTime.UtcNow.Ticks;
            data.acquiredEnhancements = acquired.ToArray();
            data.currentOffers = Array.Empty<EnhancementOfferData>();
            data.currentDrawPaid = false;
            data.revealedCards = new bool[
                EnhancementPcgRules.CardsPerDraw];
            Save(data);
            message = "已安装：" + definition.DisplayName;
            return true;
        }

        public static void AddGalaxyCoins(int amount)
        {
            if (amount <= 0)
            {
                return;
            }
            GalaxyEnhancementProgressData data = LoadOrCreate();
            data.galaxyCoins = checked(data.galaxyCoins + amount);
            Save(data);
        }

        public static bool TrySpendGalaxyCoins(
            int amount,
            out int remainingBalance,
            out string message)
        {
            GalaxyEnhancementProgressData data = LoadOrCreate();
            amount = Mathf.Max(0, amount);
            if (amount <= 0)
            {
                remainingBalance = data.galaxyCoins;
                message = string.Empty;
                return true;
            }
            if (data.galaxyCoins < amount)
            {
                remainingBalance = data.galaxyCoins;
                message = "银河币不足，需要 " + amount + "。";
                return false;
            }

            data.galaxyCoins -= amount;
            Save(data);
            remainingBalance = data.galaxyCoins;
            message = "已消耗 " + amount + " 银河币。";
            return true;
        }

        public static float GetTotalMagnitude(EnhancementStat stat)
        {
            GalaxyEnhancementProgressData data = LoadOrCreate();
            float total = 0f;
            foreach (AcquiredEnhancementData entry in
                     data.acquiredEnhancements)
            {
                if (entry != null &&
                    EnhancementTypeCatalog.TryGet(
                        entry.definitionId,
                        out EnhancementTypeDefinition definition) &&
                    definition.Stat == stat)
                {
                    total += entry.totalMagnitude;
                }
            }
            return total;
        }

        static string GetPath()
        {
            string slotId = GalaxyLaunchContext.SelectedSlotId;
            if (string.IsNullOrWhiteSpace(slotId))
            {
                GalaxySaveSlotMetadata development =
                    GalaxySaveSlotService.GetOrCreateDevelopmentSlot();
                slotId = development.slotId;
                GalaxyLaunchContext.SelectSlot(slotId);
            }
            return Path.Combine(
                GalaxySaveSlotService.GetSpacecraftDirectory(slotId),
                ProgressFileName);
        }

        static void Normalize(GalaxyEnhancementProgressData data)
        {
            data.galaxyCoins = Mathf.Max(0, data.galaxyCoins);
            data.offerSequence = Mathf.Max(0, data.offerSequence);
            data.drawsWithoutGold = Mathf.Max(0, data.drawsWithoutGold);
            if (data.currentOffers == null)
            {
                data.currentOffers = Array.Empty<EnhancementOfferData>();
            }
            if (data.revealedCards == null ||
                data.revealedCards.Length !=
                EnhancementPcgRules.CardsPerDraw)
            {
                bool[] repaired = new bool[
                    EnhancementPcgRules.CardsPerDraw];
                if (data.revealedCards != null)
                {
                    Array.Copy(
                        data.revealedCards,
                        repaired,
                        Mathf.Min(
                            data.revealedCards.Length,
                            repaired.Length));
                }
                data.revealedCards = repaired;
            }
            if (!data.currentDrawPaid)
            {
                Array.Clear(
                    data.revealedCards,
                    0,
                    data.revealedCards.Length);
            }
            if (data.acquiredEnhancements == null)
            {
                data.acquiredEnhancements =
                    Array.Empty<AcquiredEnhancementData>();
            }
        }
    }
}
