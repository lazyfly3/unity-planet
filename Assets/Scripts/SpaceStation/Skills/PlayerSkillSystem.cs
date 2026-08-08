using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ModularAssembly;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityPlanet.ModularAssembly;
using UnityPlanet.SpaceStation.Enhancement;

namespace UnityPlanet.SpaceStation.Skills
{
    public sealed class PlayerSkillDefinition
    {
        public string Id { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public string IconResourcePath { get; }
        public int MaximumLevel { get; }
        public float CooldownSeconds { get; }
        readonly float[] cooldownByLevel;

        public PlayerSkillDefinition(
            string id,
            string displayName,
            string description,
            string iconResourcePath,
            int maximumLevel,
            float cooldownSeconds,
            float[] levelCooldowns = null)
        {
            Id = id;
            DisplayName = displayName;
            Description = description;
            IconResourcePath = iconResourcePath;
            MaximumLevel = maximumLevel;
            CooldownSeconds = cooldownSeconds;
            cooldownByLevel = levelCooldowns == null
                ? null
                : (float[])levelCooldowns.Clone();
        }

        public float CooldownForLevel(int level)
        {
            if (cooldownByLevel == null || cooldownByLevel.Length == 0)
                return Mathf.Max(0f, CooldownSeconds);
            int index = Mathf.Clamp(level, 1, MaximumLevel) - 1;
            return Mathf.Max(
                0f,
                cooldownByLevel[Mathf.Min(
                    index,
                    cooldownByLevel.Length - 1)]);
        }

        public float ValueForLevel(int level)
        {
            int clamped = Mathf.Clamp(level, 1, MaximumLevel);
            float[] rates = { 20f, 30f, 45f, 65f, 90f };
            return rates[Mathf.Min(clamped - 1, rates.Length - 1)];
        }

        public int UpgradeCostFromLevel(int level)
        {
            int[] costs = { 200, 450, 800, 1300 };
            return level >= MaximumLevel
                ? 0
                : costs[Mathf.Clamp(level - 1, 0, costs.Length - 1)];
        }
    }

    public static class PlayerSkillCatalog
    {
        public const float SelfRepairDurationSeconds = 10f;
        public const string SelfRepairId = "skill.self_repair";
        public const string ChronoExecutionId = "skill.chrono_execution";
        public const string DefenseMatrixId = "skill.defense_matrix";
        public const string DamageAmplifierId = "skill.damage_amplifier";
        public const string SupportDroneId = "skill.support_drone";
        public const string FireRateOverdriveId = "skill.fire_rate_overdrive";
        public const string TerrainBreakerRoundId = "skill.terrain_breaker_round";
        public const string AbsoluteFreezeId = "skill.absolute_freeze";

        static readonly PlayerSkillDefinition[] Definitions =
        {
            new PlayerSkillDefinition(
                SelfRepairId,
                "自我修复",
                "启动纳米维修阵列，在10秒内重建本次战斗中损坏或脱落的舰体模块。",
                "UI/Skills/SelfRepair",
                5,
                60f,
                new[] { 60f, 54f, 48f, 42f, 36f }),
            new PlayerSkillDefinition(
                ChronoExecutionId,
                "零时处决",
                "短暂冻结战场并进入慢动作，同时锁定视野内多架敌机。再次按技能键或鼠标左键可提前处决，锁定完成的目标将受到致命攻击。",
                "UI/Skills/ChronoExecution",
                5,
                30f,
                new[] { 30f, 27f, 24f, 21f, 18f }),
            new PlayerSkillDefinition(
                DefenseMatrixId,
                "防御矩阵",
                "展开临时能量防护层，在不改变舰体结构的情况下按比例降低所有模块受到的伤害。",
                "UI/Skills/DefenseMatrix",
                5,
                38f,
                new[] { 38f, 36f, 34f, 32f, 30f }),
            new PlayerSkillDefinition(
                DamageAmplifierId,
                "武器增幅",
                "短时间提高全部舰载武器造成的伤害，可与射速强化同时生效。",
                "UI/Skills/DamageAmplifier",
                5,
                40f,
                new[] { 40f, 38f, 36f, 34f, 32f }),
            new PlayerSkillDefinition(
                SupportDroneId,
                "支援无人机",
                "召唤一架无碰撞支援无人机自动攻击附近敌机，持续时间结束后撤离。",
                "UI/Skills/SupportDrone",
                5,
                45f,
                new[] { 45f, 42f, 39f, 36f, 33f }),
            new PlayerSkillDefinition(
                FireRateOverdriveId,
                "火控超频",
                "短时间提高全部舰载武器射速，不改变弹丸物理参数和武器模块结构。",
                "UI/Skills/FireRateOverdrive",
                5,
                36f,
                new[] { 36f, 34f, 32f, 30f, 28f }),
            new PlayerSkillDefinition(
                TerrainBreakerRoundId,
                "月牙光刃",
                "按下后立即向舰首发射宽幅月牙能量刃；可沿命中高度切割城市建筑和连廊，也能在允许变形的自然地形上留下切痕。",
                "UI/Skills/TerrainBreakerRound",
                5,
                32f,
                new[] { 32f, 30f, 28f, 26f, 24f }),
            new PlayerSkillDefinition(
                AbsoluteFreezeId,
                "绝对冻结",
                "冻结当前战场中的全部敌机，使其暂时停止移动和攻击，结束后恢复冻结前的运动状态。",
                "UI/Skills/AbsoluteFreeze",
                5,
                48f,
                new[] { 48f, 45f, 42f, 39f, 36f })
        };

        public static IReadOnlyList<PlayerSkillDefinition> All => Definitions;

        public static float SelfRepairIntegrityBudgetForStep(
            float remainingIntegrity,
            float elapsedSeconds,
            float stepSeconds)
        {
            remainingIntegrity = Mathf.Max(0f, remainingIntegrity);
            stepSeconds = Mathf.Max(0f, stepSeconds);
            if (remainingIntegrity <= 0f || stepSeconds <= 0f)
                return 0f;
            float remainingDuration = Mathf.Max(
                0f,
                SelfRepairDurationSeconds - Mathf.Max(0f, elapsedSeconds));
            return remainingDuration <= stepSeconds
                ? remainingIntegrity
                : remainingIntegrity * stepSeconds / remainingDuration;
        }

        public static bool TryGet(
            string id,
            out PlayerSkillDefinition definition)
        {
            definition = Definitions.FirstOrDefault(item =>
                string.Equals(item.Id, id, StringComparison.Ordinal));
            return definition != null;
        }

        public static string EffectForLevel(string id, int level)
        {
            level = Mathf.Clamp(level, 1, 5);
            switch (id)
            {
                case SelfRepairId:
                    return "修复时间  " +
                           SelfRepairDurationSeconds.ToString("0") +
                           " 秒  ·  冷却时间  " +
                           Definitions[0].CooldownForLevel(level)
                               .ToString("0") + " 秒";
                case ChronoExecutionId:
                    return "冷却时间  " +
                           Definitions[1].CooldownForLevel(level)
                               .ToString("0") + " 秒";
                case DefenseMatrixId:
                    return "减伤 " +
                           (45 + (level - 1) * 8) + "%  ·  持续 " +
                           (7f + (level - 1) * 0.75f).ToString("0.0") +
                           " 秒";
                case DamageAmplifierId:
                    return "伤害提高 " +
                           (40 + (level - 1) * 10) + "%  ·  持续 " +
                           (8f + (level - 1) * 0.75f).ToString("0.0") +
                           " 秒";
                case SupportDroneId:
                    return "无人机伤害 " +
                           (24 + (level - 1) * 8) + "  ·  持续 " +
                           (11f + (level - 1) * 1.5f).ToString("0.0") +
                           " 秒";
                case FireRateOverdriveId:
                    return "射速提高 " +
                           (35f + (level - 1) * 7.5f).ToString("0") +
                           "%  ·  持续 " +
                           (8f + (level - 1) * 0.75f).ToString("0.0") +
                           " 秒";
                case TerrainBreakerRoundId:
                    return "光刃宽度 " +
                           (26 + (level - 1) * 4) + " 米  ·  切割伤害 " +
                           (160 + (level - 1) * 45);
                case AbsoluteFreezeId:
                    return "全场冻结 " +
                           (2.5f + (level - 1) * 0.5f).ToString("0.0") +
                           " 秒";
                default:
                    return string.Empty;
            }
        }
    }

    [Serializable]
    public sealed class PlayerSkillProgressEntry
    {
        public string skillId;
        public int level = 1;
        public int reserveSkillCores;
    }

    [Serializable]
    public sealed class PlayerSkillProgressData
    {
        public const int CurrentFormatVersion = 1;
        public int formatVersion = CurrentFormatVersion;
        public PlayerSkillProgressEntry[] skills =
            Array.Empty<PlayerSkillProgressEntry>();
        public string[] equippedSkillIds = new string[3];
    }

    public static class PlayerSkillProgressService
    {
        const string FileName = "player_skills.json";
        public const int SlotCount = 3;

        public static event Action Changed;

        public static PlayerSkillProgressData LoadOrCreate()
        {
            string path = GetPath();
            PlayerSkillProgressData data = null;
            if (File.Exists(path))
            {
                try
                {
                    data = JsonUtility.FromJson<PlayerSkillProgressData>(
                        File.ReadAllText(path));
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        "[Skills] 技能存档读取失败，使用安全默认值：" +
                        exception.Message);
                }
            }

            if (data == null ||
                data.formatVersion !=
                PlayerSkillProgressData.CurrentFormatVersion)
            {
                data = new PlayerSkillProgressData();
                Normalize(data);
                Save(data);
                return data;
            }

            bool changed = Normalize(data);
            if (changed)
                Save(data);
            return data;
        }

        public static void Save(PlayerSkillProgressData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            Normalize(data);
            data.formatVersion =
                PlayerSkillProgressData.CurrentFormatVersion;
            string path = GetPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path) ??
                throw new InvalidOperationException("技能存档目录无效。"));
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(data, true));
            if (File.Exists(path))
                File.Replace(temporary, path, null);
            else
                File.Move(temporary, path);
            Changed?.Invoke();
        }

        public static bool TryEquip(
            string skillId,
            int slotIndex,
            out string message)
        {
            PlayerSkillProgressData data = LoadOrCreate();
            if (slotIndex < 0 || slotIndex >= SlotCount ||
                !PlayerSkillCatalog.TryGet(skillId, out _) ||
                Find(data, skillId) == null)
            {
                message = "技能或装载槽无效。";
                return false;
            }

            for (int index = 0; index < data.equippedSkillIds.Length; index++)
            {
                if (string.Equals(
                        data.equippedSkillIds[index],
                        skillId,
                        StringComparison.Ordinal))
                    data.equippedSkillIds[index] = string.Empty;
            }
            data.equippedSkillIds[slotIndex] = skillId;
            Save(data);
            message = "已将技能装载到按键 " + (slotIndex + 1) + "。";
            return true;
        }

        public static bool TryUnequip(int slotIndex, out string message)
        {
            PlayerSkillProgressData data = LoadOrCreate();
            if (slotIndex < 0 || slotIndex >= SlotCount)
            {
                message = "装载槽无效。";
                return false;
            }
            if (string.IsNullOrWhiteSpace(data.equippedSkillIds[slotIndex]))
            {
                message = "这个装载槽本来就是空的。";
                return false;
            }
            data.equippedSkillIds[slotIndex] = string.Empty;
            Save(data);
            message = "已卸载按键 " + (slotIndex + 1) + " 的技能。";
            return true;
        }

        public static bool TryUpgrade(
            string skillId,
            out string message)
        {
            PlayerSkillProgressData data = LoadOrCreate();
            PlayerSkillProgressEntry entry = Find(data, skillId);
            if (entry == null ||
                !PlayerSkillCatalog.TryGet(
                    skillId,
                    out PlayerSkillDefinition definition))
            {
                message = "尚未拥有这个技能。";
                return false;
            }
            if (entry.level >= definition.MaximumLevel)
            {
                message = "技能已经满级。";
                return false;
            }

            int cost = definition.UpgradeCostFromLevel(entry.level);
            if (!GalaxyCurrencyService.TrySpendGalaxyCoins(
                    cost,
                    out _,
                    out message))
                return false;

            entry.level++;
            Save(data);
            message = definition.DisplayName + " 已升级至 Lv." +
                      entry.level + "：" +
                      PlayerSkillCatalog.EffectForLevel(
                          definition.Id,
                          entry.level) + "。";
            return true;
        }

        public static bool TryAcquireGoldSkill(
            string skillId,
            out string message)
        {
            if (!PlayerSkillCatalog.TryGet(
                    skillId,
                    out PlayerSkillDefinition definition))
            {
                message = "金卡中的技能数据无效。";
                return false;
            }

            PlayerSkillProgressData data = LoadOrCreate();
            var entries = new List<PlayerSkillProgressEntry>(data.skills);
            PlayerSkillProgressEntry entry = Find(data, skillId);
            if (entry == null)
            {
                entry = new PlayerSkillProgressEntry
                {
                    skillId = skillId,
                    level = 1
                };
                entries.Add(entry);
                data.skills = entries.ToArray();
                message = "获得技能：" + definition.DisplayName;
            }
            else if (entry.level < definition.MaximumLevel)
            {
                entry.level++;
                message = "获得重复技能数据，" + definition.DisplayName +
                          " 提升至 Lv." + entry.level + "。";
            }
            else
            {
                entry.reserveSkillCores++;
                message = definition.DisplayName +
                          " 已满级，技能数据已转为备用技能核心。";
            }
            Save(data);
            return true;
        }

        public static int UnlockAllForTesting()
        {
            PlayerSkillProgressData data = LoadOrCreate();
            var entries = new List<PlayerSkillProgressEntry>(
                data.skills ?? Array.Empty<PlayerSkillProgressEntry>());
            int unlocked = 0;
            foreach (PlayerSkillDefinition definition in
                     PlayerSkillCatalog.All)
            {
                if (definition == null ||
                    entries.Any(item =>
                        item != null &&
                        string.Equals(
                            item.skillId,
                            definition.Id,
                            StringComparison.Ordinal)))
                    continue;
                entries.Add(new PlayerSkillProgressEntry
                {
                    skillId = definition.Id,
                    level = 1
                });
                unlocked++;
            }
            data.skills = entries.ToArray();
            Save(data);
            return unlocked;
        }

        public static PlayerSkillProgressEntry Find(
            PlayerSkillProgressData data,
            string skillId)
        {
            return data?.skills?.FirstOrDefault(item =>
                item != null &&
                string.Equals(
                    item.skillId,
                    skillId,
                    StringComparison.Ordinal));
        }

        static bool Normalize(PlayerSkillProgressData data)
        {
            bool changed = false;
            var normalized = new List<PlayerSkillProgressEntry>();
            foreach (PlayerSkillProgressEntry entry in
                     data.skills ?? Array.Empty<PlayerSkillProgressEntry>())
            {
                if (entry == null ||
                    !PlayerSkillCatalog.TryGet(
                        entry.skillId,
                        out PlayerSkillDefinition definition) ||
                    normalized.Any(item => item.skillId == entry.skillId))
                {
                    changed = true;
                    continue;
                }
                int level = Mathf.Clamp(
                    entry.level,
                    1,
                    definition.MaximumLevel);
                if (level != entry.level || entry.reserveSkillCores < 0)
                    changed = true;
                entry.level = level;
                entry.reserveSkillCores = Mathf.Max(
                    0,
                    entry.reserveSkillCores);
                normalized.Add(entry);
            }

            if (normalized.All(item =>
                    item.skillId != PlayerSkillCatalog.SelfRepairId))
            {
                normalized.Add(new PlayerSkillProgressEntry
                {
                    skillId = PlayerSkillCatalog.SelfRepairId,
                    level = 1
                });
                changed = true;
            }
            data.skills = normalized.ToArray();

            if (data.equippedSkillIds == null ||
                data.equippedSkillIds.Length != SlotCount)
            {
                string[] repaired = new string[SlotCount];
                if (data.equippedSkillIds != null)
                {
                    Array.Copy(
                        data.equippedSkillIds,
                        repaired,
                        Mathf.Min(data.equippedSkillIds.Length, SlotCount));
                }
                data.equippedSkillIds = repaired;
                changed = true;
            }

            var equipped = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < SlotCount; index++)
            {
                string id = data.equippedSkillIds[index] ?? string.Empty;
                if (string.IsNullOrWhiteSpace(id))
                {
                    data.equippedSkillIds[index] = string.Empty;
                    continue;
                }
                if (Find(data, id) == null || !equipped.Add(id))
                {
                    data.equippedSkillIds[index] = string.Empty;
                    changed = true;
                }
            }
            return changed;
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
                FileName);
        }
    }

    [DisallowMultipleComponent]
    public sealed class PlayerSkillRuntimeController : MonoBehaviour
    {
        sealed class SlotHud
        {
            public GameObject Root;
            public Text Key;
            public Text Name;
            public Text Status;
            public RawImage Icon;
            public Image Cooldown;
            public Image Progress;
        }

        sealed class HologramMeshPart
        {
            public Mesh Mesh;
            public Matrix4x4 LocalMatrix;
        }

        sealed class HologramModuleVisual
        {
            public GameObject SourcePrefab;
            public VehicleSelfRepairPreview Preview;
            public readonly List<HologramMeshPart> Meshes =
                new List<HologramMeshPart>();
            public Bounds Bounds;
        }

        VehicleStructureGraph graph;
        PlayerSkillProgressData progress;
        readonly SlotHud[] slots = new SlotHud[
            PlayerSkillProgressService.SlotCount];
        readonly float[] cooldownEnds = new float[
            PlayerSkillProgressService.SlotCount];
        Canvas hud;
        int repairingSlot = -1;
        float repairElapsedSeconds;
        int chronoExecutionSlot = -1;
        int chronoExecutionLevel = 1;
        ChronoExecutionSkillRuntime chronoExecution;
        GoldCombatSkillRuntime goldCombatSkills;
        LineRenderer repairRing;
        Material repairMaterial;
        Text repairMessage;
        readonly Dictionary<string, HologramModuleVisual> hologramModules =
            new Dictionary<string, HologramModuleVisual>(
                StringComparer.Ordinal);
        readonly List<VehicleSelfRepairPreview> hologramPreviewBuffer =
            new List<VehicleSelfRepairPreview>();
        readonly HashSet<string> hologramActiveIds =
            new HashSet<string>(StringComparer.Ordinal);
        bool hasHologramPreview;
        Material hologramMaterial;
        MaterialPropertyBlock hologramBlock;
        LineRenderer hologramScanLine;
        Material hologramScanMaterial;
        TextMesh hologramProgressLabel;
        Bounds hologramBounds;

        public void Initialize(VehicleStructureGraph target)
        {
            graph = target;
            if (chronoExecution != null)
                chronoExecution.Initialize(graph);
            if (goldCombatSkills != null)
                goldCombatSkills.Initialize(graph);
        }

        void Awake()
        {
            graph = graph ?? GetComponent<VehicleStructureGraph>();
            chronoExecution = GetComponent<ChronoExecutionSkillRuntime>();
            if (chronoExecution == null)
                chronoExecution = gameObject.AddComponent<
                    ChronoExecutionSkillRuntime>();
            chronoExecution.Initialize(graph);
            chronoExecution.Finished += HandleChronoExecutionFinished;
            goldCombatSkills = GetComponent<GoldCombatSkillRuntime>();
            if (goldCombatSkills == null)
                goldCombatSkills = gameObject.AddComponent<
                    GoldCombatSkillRuntime>();
            goldCombatSkills.Initialize(graph);
            progress = PlayerSkillProgressService.LoadOrCreate();
            PlayerSkillProgressService.Changed += ReloadProgress;
            BuildHud();
        }

        void OnDestroy()
        {
            PlayerSkillProgressService.Changed -= ReloadProgress;
            if (chronoExecution != null)
            {
                chronoExecution.Finished -= HandleChronoExecutionFinished;
                chronoExecution.Cancel(false);
            }
            if (hud != null)
                Destroy(hud.gameObject);
            if (repairMaterial != null)
                Destroy(repairMaterial);
            ClearHolographicRepair(true);
        }

        void Update()
        {
            if (graph == null)
            {
                if (hud != null)
                    hud.gameObject.SetActive(false);
                return;
            }

            if (hud == null || slots.Any(item => item == null))
                BuildHud();

            if (PlayerSkillCombatEffects.NoCooldownForTesting)
                Array.Clear(cooldownEnds, 0, cooldownEnds.Length);

            bool visible = graph.Active;
            if (hud != null && hud.gameObject.activeSelf != visible)
                hud.gameObject.SetActive(visible);
            if (!visible)
            {
                repairingSlot = -1;
                repairElapsedSeconds = 0f;
                if (chronoExecution != null && chronoExecution.Active)
                    chronoExecution.Cancel(false);
                chronoExecutionSlot = -1;
                SetRepairEffect(false);
                return;
            }

            if (!ArcadeFlightRuntimeTuningOverlay.IsInputCaptured)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1))
                    TryActivate(0);
                if (Input.GetKeyDown(KeyCode.Alpha2))
                    TryActivate(1);
                if (Input.GetKeyDown(KeyCode.Alpha3))
                    TryActivate(2);
            }

            TickRepair();
            RefreshHud();
        }

        void TryActivate(int slotIndex)
        {
            if (progress?.equippedSkillIds == null ||
                slotIndex < 0 || slotIndex >= progress.equippedSkillIds.Length)
                return;
            string skillId = progress.equippedSkillIds[slotIndex];
            if (string.Equals(
                    skillId,
                    PlayerSkillCatalog.ChronoExecutionId,
                    StringComparison.Ordinal))
            {
                TryActivateChronoExecution(slotIndex);
                return;
            }
            if (PlayerSkillCatalog.TryGet(
                    skillId,
                    out PlayerSkillDefinition selectedDefinition) &&
                !string.Equals(
                    skillId,
                    PlayerSkillCatalog.SelfRepairId,
                    StringComparison.Ordinal))
            {
                TryActivateGoldCombatSkill(
                    slotIndex,
                    skillId,
                    selectedDefinition);
                return;
            }
            if (!string.Equals(
                    skillId,
                    PlayerSkillCatalog.SelfRepairId,
                    StringComparison.Ordinal))
                return;
            if (Time.unscaledTime < cooldownEnds[slotIndex])
            {
                ShowMessage("自我修复仍在冷却。", new Color(1f, 0.72f, 0.25f));
                return;
            }
            if (repairingSlot >= 0)
            {
                ShowMessage("自我修复已经启动。", new Color(0.32f, 0.92f, 1f));
                return;
            }
            if (!graph.NeedsSelfRepair)
            {
                ShowMessage("舰体结构完整，无需修复。", Color.white);
                return;
            }

            repairingSlot = slotIndex;
            repairElapsedSeconds = 0f;
            ShowMessage("纳米维修阵列启动", new Color(0.2f, 1f, 0.9f));
            SetRepairEffect(true);
        }

        void TryActivateChronoExecution(int slotIndex)
        {
            if (chronoExecution != null && chronoExecution.Active)
            {
                if (chronoExecutionSlot == slotIndex)
                    chronoExecution.Execute();
                else
                    ShowMessage("零时处决正在锁定目标。", new Color(0.32f, 0.92f, 1f));
                return;
            }
            if (Time.unscaledTime < cooldownEnds[slotIndex])
            {
                ShowMessage("零时处决仍在冷却。", new Color(1f, 0.72f, 0.25f));
                return;
            }
            PlayerSkillProgressEntry entry =
                PlayerSkillProgressService.Find(
                    progress,
                    PlayerSkillCatalog.ChronoExecutionId);
            chronoExecutionLevel = Mathf.Clamp(entry?.level ?? 1, 1, 5);
            chronoExecutionSlot = slotIndex;
            if (chronoExecution == null || !chronoExecution.Begin())
            {
                chronoExecutionSlot = -1;
                ShowMessage("当前战斗镜头无法启动零时处决。", new Color(1f, 0.72f, 0.25f));
                return;
            }
            ShowMessage("零时处决：凝固时间并锁定视野内敌机", new Color(0.38f, 0.95f, 1f));
        }

        void TryActivateGoldCombatSkill(
            int slotIndex,
            string skillId,
            PlayerSkillDefinition definition)
        {
            if (Time.unscaledTime < cooldownEnds[slotIndex])
            {
                ShowMessage(
                    definition.DisplayName + "仍在冷却。",
                    new Color(1f, 0.72f, 0.25f));
                return;
            }
            PlayerSkillProgressEntry entry =
                PlayerSkillProgressService.Find(progress, skillId);
            int level = entry?.level ?? 1;
            string message = string.Empty;
            if (goldCombatSkills == null ||
                !goldCombatSkills.TryActivate(
                    skillId,
                    level,
                    out message))
            {
                ShowMessage(
                    string.IsNullOrWhiteSpace(message)
                        ? "技能启动失败。"
                        : message,
                    new Color(1f, 0.58f, 0.22f));
                return;
            }
            cooldownEnds[slotIndex] =
                PlayerSkillCombatEffects.NoCooldownForTesting
                    ? Time.unscaledTime
                    : Time.unscaledTime +
                      definition.CooldownForLevel(level);
            ShowMessage(message, new Color(0.3f, 0.95f, 1f));
        }

        void HandleChronoExecutionFinished(int fired, int lethal)
        {
            if (chronoExecutionSlot < 0)
                return;
            int slotIndex = chronoExecutionSlot;
            chronoExecutionSlot = -1;
            if (PlayerSkillCatalog.TryGet(
                    PlayerSkillCatalog.ChronoExecutionId,
                    out PlayerSkillDefinition definition))
                cooldownEnds[slotIndex] =
                    PlayerSkillCombatEffects.NoCooldownForTesting
                        ? Time.unscaledTime
                        : Time.unscaledTime +
                          definition.CooldownForLevel(
                              chronoExecutionLevel);

            if (lethal > 0)
                ShowMessage("零时处决：击毁 " + lethal + " 架敌机", new Color(1f, 0.72f, 0.2f));
            else if (fired > 0)
                ShowMessage("零时齐射：命中 " + fired + " 个目标", new Color(0.38f, 0.95f, 1f));
            else
                ShowMessage("零时处决结束：未捕获有效目标", Color.white);
        }

        void TickRepair()
        {
            if (repairingSlot < 0)
                return;
            if (!graph.Active || graph.IsVehicleDestroyed)
            {
                repairingSlot = -1;
                repairElapsedSeconds = 0f;
                SetRepairEffect(false);
                return;
            }

            PlayerSkillProgressEntry entry =
                PlayerSkillProgressService.Find(
                    progress,
                    PlayerSkillCatalog.SelfRepairId);
            if (!PlayerSkillCatalog.TryGet(
                    PlayerSkillCatalog.SelfRepairId,
                    out PlayerSkillDefinition definition))
                return;
            float step = Mathf.Max(0f, Time.unscaledDeltaTime);
            float remainingIntegrity = graph.RemainingSelfRepairIntegrity;
            float integrityBudget =
                PlayerSkillCatalog.SelfRepairIntegrityBudgetForStep(
                    remainingIntegrity,
                    repairElapsedSeconds,
                    step);
            if (integrityBudget <= 0f && graph.NeedsSelfRepair)
                integrityBudget = 0.001f;
            graph.ApplySelfRepair(integrityBudget);
            repairElapsedSeconds += step;
            UpdateHolographicRepair();
            if (graph.NeedsSelfRepair)
                return;

            cooldownEnds[repairingSlot] =
                PlayerSkillCombatEffects.NoCooldownForTesting
                    ? Time.unscaledTime
                    : Time.unscaledTime +
                      definition.CooldownForLevel(entry?.level ?? 1);
            repairingSlot = -1;
            repairElapsedSeconds = 0f;
            SetRepairEffect(false);
            ShowMessage("舰体已恢复至本次出击前的结构", new Color(0.3f, 1f, 0.72f));
        }

        void ReloadProgress()
        {
            progress = PlayerSkillProgressService.LoadOrCreate();
            RefreshHud();
        }

        void BuildHud()
        {
            if (hud != null)
                Destroy(hud.gameObject);
            GameObject root = new GameObject(
                "PlayerSkillHud",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            hud = root.GetComponent<Canvas>();
            hud.renderMode = RenderMode.ScreenSpaceOverlay;
            hud.sortingOrder = 560;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform holder = CreateRect(
                "Slots",
                root.transform,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(840f, 164f),
                new Vector2(0f, 118f));
            RawImage generatedFrame = holder.gameObject.AddComponent<RawImage>();
            generatedFrame.texture = Resources.Load<Texture2D>(
                "UI/Skills/CombatSkillHudFrameV2");
            generatedFrame.color = Color.white;
            generatedFrame.raycastTarget = false;

            RectTransform slotHolder = CreateRect(
                "SlotContent",
                holder,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero);
            slotHolder.offsetMin = new Vector2(22f, 20f);
            slotHolder.offsetMax = new Vector2(-22f, -18f);
            var layout = slotHolder.gameObject.AddComponent<
                HorizontalLayoutGroup>();
            layout.spacing = 16f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;

            for (int index = 0; index < slots.Length; index++)
                slots[index] = BuildSlot(slotHolder, index);

            repairMessage = CreateText(
                "RepairMessage",
                root.transform,
                string.Empty,
                25,
                TextAnchor.MiddleCenter);
            RectTransform messageRect = repairMessage.rectTransform;
            messageRect.anchorMin = new Vector2(0.5f, 0f);
            messageRect.anchorMax = new Vector2(0.5f, 0f);
            messageRect.sizeDelta = new Vector2(700f, 42f);
            messageRect.anchoredPosition = new Vector2(0f, 228f);
            repairMessage.gameObject.SetActive(false);
            root.SetActive(false);
        }

        SlotHud BuildSlot(RectTransform parent, int index)
        {
            RectTransform root = CreateRect(
                "SkillSlot" + (index + 1),
                parent,
                Vector2.zero,
                Vector2.zero,
                new Vector2(250f, 118f),
                Vector2.zero);

            Text key = CreateText(
                "Key",
                root,
                (index + 1).ToString(),
                24,
                TextAnchor.MiddleCenter);
            SetRect(
                key.rectTransform,
                new Vector2(10f, 70f),
                new Vector2(36f, 36f));
            key.color = new Color(1f, 0.77f, 0.22f);

            RectTransform iconRect = CreateRect(
                "Icon",
                root,
                Vector2.zero,
                Vector2.zero,
                new Vector2(78f, 78f),
                Vector2.zero);
            SetRect(
                iconRect,
                new Vector2(48f, 22f),
                new Vector2(78f, 78f));
            RawImage icon = iconRect.gameObject.AddComponent<RawImage>();
            icon.color = Color.clear;
            icon.raycastTarget = false;

            Text name = CreateText(
                "Name",
                root,
                "空槽",
                19,
                TextAnchor.MiddleLeft);
            SetRect(
                name.rectTransform,
                new Vector2(138f, 66f),
                new Vector2(100f, 30f));
            Text status = CreateText(
                "Status",
                root,
                string.Empty,
                15,
                TextAnchor.MiddleLeft);
            SetRect(
                status.rectTransform,
                new Vector2(138f, 36f),
                new Vector2(100f, 26f));
            status.color = new Color(0.38f, 0.88f, 1f);

            RectTransform progressBackRect = CreateRect(
                "ProgressBack",
                root,
                Vector2.zero,
                Vector2.zero,
                new Vector2(100f, 5f),
                Vector2.zero);
            SetRect(
                progressBackRect,
                new Vector2(138f, 22f),
                new Vector2(100f, 5f));
            Image progressBack =
                progressBackRect.gameObject.AddComponent<Image>();
            progressBack.color = new Color(0f, 0.06f, 0.09f, 0.9f);
            RectTransform progressRect = CreateRect(
                "Progress",
                progressBackRect,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero);
            Image progress = progressRect.gameObject.AddComponent<Image>();
            progress.color = new Color(0.08f, 1f, 0.78f, 0.95f);
            progress.type = Image.Type.Filled;
            progress.fillMethod = Image.FillMethod.Horizontal;
            progress.fillOrigin = 0;
            progress.fillAmount = 0f;

            RectTransform cooldownRect = CreateRect(
                "Cooldown",
                iconRect,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero);
            Image cooldown = cooldownRect.gameObject.AddComponent<Image>();
            cooldown.color = new Color(0f, 0f, 0f, 0.68f);
            cooldown.type = Image.Type.Filled;
            cooldown.fillMethod = Image.FillMethod.Radial360;
            cooldown.fillOrigin = 2;
            cooldown.fillClockwise = false;

            return new SlotHud
            {
                Root = root.gameObject,
                Key = key,
                Name = name,
                Status = status,
                Icon = icon,
                Cooldown = cooldown,
                Progress = progress
            };
        }

        void RefreshHud()
        {
            if (progress?.equippedSkillIds == null)
                return;
            for (int index = 0; index < slots.Length; index++)
            {
                SlotHud slot = slots[index];
                if (slot == null)
                    continue;
                string id = progress.equippedSkillIds[index];
                if (!PlayerSkillCatalog.TryGet(
                        id,
                        out PlayerSkillDefinition definition))
                {
                    slot.Name.text = "空槽";
                    slot.Status.text = "未装载";
                    slot.Icon.texture = null;
                    slot.Icon.color = Color.clear;
                    slot.Icon.gameObject.SetActive(false);
                    slot.Cooldown.fillAmount = 0f;
                    slot.Progress.fillAmount = 0f;
                    continue;
                }
                PlayerSkillProgressEntry entry =
                    PlayerSkillProgressService.Find(progress, id);
                int level = entry?.level ?? 1;
                slot.Name.text = definition.DisplayName;
                if (repairingSlot == index)
                {
                    slot.Status.text = "修复中 " +
                        Mathf.RoundToInt(graph.SelfRepairCompletion * 100f) + "%";
                    slot.Progress.fillAmount = graph.SelfRepairCompletion;
                }
                else if (chronoExecutionSlot == index &&
                         chronoExecution != null && chronoExecution.Active)
                {
                    slot.Status.text = "锁定 " + chronoExecution.FullyLockedCount +
                        "/" + chronoExecution.TargetCount;
                    slot.Progress.fillAmount =
                        1f - chronoExecution.NormalizedRemaining;
                }
                else
                {
                    slot.Status.text = "Lv." + level;
                    slot.Progress.fillAmount = 0f;
                }
                slot.Icon.texture = Resources.Load<Texture2D>(
                    definition.IconResourcePath);
                slot.Icon.color = Color.white;
                slot.Icon.gameObject.SetActive(true);
                float remaining = Mathf.Max(
                    0f,
                    cooldownEnds[index] - Time.unscaledTime);
                float cooldownDuration = definition.CooldownForLevel(level);
                slot.Cooldown.fillAmount = cooldownDuration <= 0f
                    ? 0f
                    : remaining / cooldownDuration;
                if (remaining > 0f)
                    slot.Status.text = remaining.ToString("0.0") + "s";
            }
        }

        void SetRepairEffect(bool active)
        {
            if (!active)
            {
                if (repairRing != null)
                    repairRing.enabled = false;
                ClearHolographicRepair(false);
                return;
            }
            if (repairRing != null)
                repairRing.enabled = false;
            UpdateHolographicRepair();
        }

        void UpdateRepairEffect()
        {
            if (repairRing == null || !repairRing.enabled)
                return;
            Bounds bounds = graph.ResolveVisualBounds();
            float radius = Mathf.Max(
                1.2f,
                Mathf.Max(bounds.extents.x, bounds.extents.z) * 1.2f);
            Vector3 center = bounds.center;
            float pulse = 1f + Mathf.Sin(Time.time * 7f) * 0.04f;
            for (int index = 0; index < repairRing.positionCount; index++)
            {
                float angle = (index / (float)repairRing.positionCount) *
                              Mathf.PI * 2f + Time.time * 0.9f;
                repairRing.SetPosition(
                    index,
                    center + new Vector3(
                        Mathf.Cos(angle) * radius * pulse,
                        Mathf.Sin(angle * 3f) * bounds.extents.y * 0.24f,
                        Mathf.Sin(angle) * radius * pulse));
            }
        }

        void UpdateHolographicRepair()
        {
            hologramPreviewBuffer.Clear();
            if (graph == null ||
                graph.GetSelfRepairPreviews(hologramPreviewBuffer) <= 0)
            {
                ClearHolographicRepair(false);
                return;
            }

            hologramActiveIds.Clear();
            foreach (VehicleSelfRepairPreview preview in
                     hologramPreviewBuffer)
            {
                if (preview.VisualPrefab == null ||
                    string.IsNullOrWhiteSpace(preview.RuntimeId))
                    continue;
                hologramActiveIds.Add(preview.RuntimeId);
                if (!hologramModules.TryGetValue(
                        preview.RuntimeId,
                        out HologramModuleVisual visual))
                {
                    visual = new HologramModuleVisual();
                    hologramModules[preview.RuntimeId] = visual;
                }
                if (visual.SourcePrefab != preview.VisualPrefab)
                {
                    visual.SourcePrefab = preview.VisualPrefab;
                    RebuildHologramMeshCache(
                        visual,
                        preview.VisualPrefab);
                }
                visual.Preview = preview;
            }

            foreach (string runtimeId in hologramModules.Keys
                         .Where(item => !hologramActiveIds.Contains(item))
                         .ToArray())
                hologramModules.Remove(runtimeId);
            hasHologramPreview = hologramModules.Values.Any(
                item => item.Meshes.Count > 0);
            if (hasHologramPreview)
                EnsureHologramResources();
        }

        static void RebuildHologramMeshCache(
            HologramModuleVisual visual,
            GameObject source)
        {
            if (source == null || visual == null)
                return;
            visual.Meshes.Clear();
            Matrix4x4 rootInverse = source.transform.worldToLocalMatrix;
            foreach (MeshFilter filter in
                     source.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter == null || filter.sharedMesh == null)
                    continue;
                visual.Meshes.Add(new HologramMeshPart
                {
                    Mesh = filter.sharedMesh,
                    LocalMatrix = rootInverse *
                                  filter.transform.localToWorldMatrix
                });
            }
            foreach (SkinnedMeshRenderer renderer in
                     source.GetComponentsInChildren<
                         SkinnedMeshRenderer>(true))
            {
                if (renderer == null || renderer.sharedMesh == null)
                    continue;
                visual.Meshes.Add(new HologramMeshPart
                {
                    Mesh = renderer.sharedMesh,
                    LocalMatrix = rootInverse *
                                  renderer.transform.localToWorldMatrix
                });
            }
        }

        void EnsureHologramResources()
        {
            if (hologramMaterial == null)
            {
                Shader shader = Shader.Find(
                    "ModularAssembly/SelfRepairHologram");
                if (shader == null)
                    shader = Shader.Find(
                        "CityGeneration/HolographicConstruction");
                if (shader != null)
                {
                    hologramMaterial = new Material(shader)
                    {
                        name = "Self Repair City Hologram"
                    };
                    if (hologramMaterial.HasProperty("_HologramColor"))
                    {
                        hologramMaterial.SetColor(
                            "_HologramColor",
                            new Color(0.02f, 0.72f, 2.4f, 1f));
                    }
                    if (hologramMaterial.HasProperty("_SolidColor"))
                    {
                        hologramMaterial.SetColor(
                            "_SolidColor",
                            new Color(0.02f, 0.42f, 1.4f, 0.52f));
                    }
                    if (hologramMaterial.HasProperty("_GridScale"))
                        hologramMaterial.SetFloat("_GridScale", 3.2f);
                    if (hologramMaterial.HasProperty("_ScanWidth"))
                        hologramMaterial.SetFloat("_ScanWidth", 0.12f);
                    if (hologramMaterial.HasProperty("_WireWidth"))
                        hologramMaterial.SetFloat("_WireWidth", 2.25f);
                    hologramMaterial.enableInstancing = true;
                }
            }
            if (hologramBlock == null)
                hologramBlock = new MaterialPropertyBlock();

            if (hologramScanLine == null)
            {
                GameObject scan = new GameObject(
                    "SelfRepairModuleScanLine");
                scan.transform.SetParent(transform, false);
                hologramScanLine = scan.AddComponent<LineRenderer>();
                hologramScanLine.useWorldSpace = true;
                hologramScanLine.loop = true;
                hologramScanLine.positionCount = 4;
                hologramScanLine.widthMultiplier = 0.085f;
                hologramScanLine.numCapVertices = 3;
                hologramScanLine.numCornerVertices = 2;
                hologramScanLine.startColor =
                    new Color(0.08f, 1f, 0.95f, 0.95f);
                hologramScanLine.endColor =
                    new Color(0.18f, 0.65f, 1f, 0.62f);
                Shader shader = Shader.Find("Sprites/Default");
                if (shader == null)
                    shader = Shader.Find("Unlit/Color");
                if (shader != null)
                {
                    hologramScanMaterial = new Material(shader)
                    {
                        name = "Self Repair Module Scan Line"
                    };
                    hologramScanLine.sharedMaterial =
                        hologramScanMaterial;
                }
            }
            hologramScanLine.enabled = true;

            if (hologramProgressLabel == null)
            {
                GameObject label = new GameObject(
                    "SelfRepairModuleProgress");
                label.transform.SetParent(transform, false);
                hologramProgressLabel = label.AddComponent<TextMesh>();
                hologramProgressLabel.font =
                    Resources.GetBuiltinResource<Font>(
                        "LegacyRuntime.ttf");
                hologramProgressLabel.fontSize = 72;
                hologramProgressLabel.characterSize = 0.025f;
                hologramProgressLabel.anchor = TextAnchor.LowerCenter;
                hologramProgressLabel.alignment = TextAlignment.Center;
                hologramProgressLabel.color =
                    new Color(0.18f, 1f, 0.9f, 0.95f);
                MeshRenderer labelRenderer =
                    label.GetComponent<MeshRenderer>();
                if (labelRenderer != null &&
                    hologramProgressLabel.font != null)
                {
                    labelRenderer.sharedMaterial =
                        hologramProgressLabel.font.material;
                    labelRenderer.sortingOrder = 120;
                }
            }
            hologramProgressLabel.gameObject.SetActive(true);
        }

        void LateUpdate()
        {
            if (!hasHologramPreview ||
                hologramMaterial == null ||
                graph == null ||
                repairingSlot < 0)
                return;

            bool hasActiveModule = false;
            float activeProgress = 0f;
            foreach (HologramModuleVisual visual in
                     hologramModules.Values)
            {
                if (visual == null || visual.Meshes.Count == 0)
                    continue;
                Matrix4x4 moduleMatrix = visual.Preview.WorldMatrix *
                                         Matrix4x4.Scale(
                                             Vector3.one * 1.012f);
                bool initialized = false;
                Bounds bounds = default;
                foreach (HologramMeshPart part in visual.Meshes)
                {
                    if (part?.Mesh == null)
                        continue;
                    Bounds transformed = TransformBounds(
                        part.Mesh.bounds,
                        moduleMatrix * part.LocalMatrix);
                    if (!initialized)
                    {
                        bounds = transformed;
                        initialized = true;
                    }
                    else
                        bounds.Encapsulate(transformed);
                }
                if (!initialized)
                    continue;
                visual.Bounds = bounds;

                float progress = visual.Preview.Holding
                    ? 1f
                    : Mathf.Clamp01(
                        Mathf.Max(0.025f, visual.Preview.Progress));
                hologramBlock.Clear();
                hologramBlock.SetFloat("_RevealProgress", progress);
                hologramBlock.SetFloat("_RevealMode", 0f);
                hologramBlock.SetFloat("_BuildMinY", bounds.min.y);
                hologramBlock.SetFloat("_BuildMaxY", bounds.max.y);
                hologramBlock.SetVector("_BuildOrigin", bounds.center);
                foreach (HologramMeshPart part in visual.Meshes)
                {
                    if (part?.Mesh == null)
                        continue;
                    Matrix4x4 matrix = moduleMatrix * part.LocalMatrix;
                    int subMeshes = Mathf.Max(1, part.Mesh.subMeshCount);
                    for (int subMesh = 0;
                         subMesh < subMeshes;
                         subMesh++)
                    {
                        Graphics.DrawMesh(
                            part.Mesh,
                            matrix,
                            hologramMaterial,
                            gameObject.layer,
                            null,
                            subMesh,
                            hologramBlock,
                            ShadowCastingMode.Off,
                            false,
                            null,
                            LightProbeUsage.Off,
                            null);
                    }
                }

                if (visual.Preview.Holding || hasActiveModule)
                    continue;
                hasActiveModule = true;
                activeProgress = progress;
                hologramBounds = bounds;
            }

            if (hologramScanLine != null)
                hologramScanLine.enabled = hasActiveModule;
            if (hologramProgressLabel != null)
                hologramProgressLabel.gameObject.SetActive(hasActiveModule);
            if (hasActiveModule)
                UpdateHologramIndicators(activeProgress);
        }

        void UpdateHologramIndicators(float progress)
        {
            if (hologramScanLine != null)
            {
                float y = Mathf.Lerp(
                    hologramBounds.min.y,
                    hologramBounds.max.y,
                    progress);
                float padding = Mathf.Clamp(
                    hologramBounds.extents.magnitude * 0.055f,
                    0.06f,
                    0.18f);
                hologramScanLine.widthMultiplier = Mathf.Clamp(
                    hologramBounds.extents.magnitude * 0.045f,
                    0.065f,
                    0.14f);
                hologramScanLine.SetPosition(
                    0,
                    new Vector3(
                        hologramBounds.min.x - padding,
                        y,
                        hologramBounds.min.z - padding));
                hologramScanLine.SetPosition(
                    1,
                    new Vector3(
                        hologramBounds.max.x + padding,
                        y,
                        hologramBounds.min.z - padding));
                hologramScanLine.SetPosition(
                    2,
                    new Vector3(
                        hologramBounds.max.x + padding,
                        y,
                        hologramBounds.max.z + padding));
                hologramScanLine.SetPosition(
                    3,
                    new Vector3(
                        hologramBounds.min.x - padding,
                        y,
                        hologramBounds.max.z + padding));
            }
            if (hologramProgressLabel == null)
                return;
            hologramProgressLabel.text =
                Mathf.RoundToInt(progress * 100f) + "%";
            hologramProgressLabel.transform.position =
                hologramBounds.center +
                Vector3.up * (hologramBounds.extents.y + 0.18f);
            Camera camera = Camera.main;
            if (camera != null)
            {
                Vector3 direction =
                    hologramProgressLabel.transform.position -
                    camera.transform.position;
                if (direction.sqrMagnitude > 0.001f)
                {
                    hologramProgressLabel.transform.rotation =
                        Quaternion.LookRotation(
                            direction.normalized,
                            camera.transform.up);
                }
            }
        }

        void ClearHolographicRepair(bool destroyResources)
        {
            hasHologramPreview = false;
            hologramModules.Clear();
            hologramPreviewBuffer.Clear();
            hologramActiveIds.Clear();
            if (hologramScanLine != null)
                hologramScanLine.enabled = false;
            if (hologramProgressLabel != null)
                hologramProgressLabel.gameObject.SetActive(false);
            if (!destroyResources)
                return;
            if (hologramScanLine != null)
                Destroy(hologramScanLine.gameObject);
            if (hologramProgressLabel != null)
                Destroy(hologramProgressLabel.gameObject);
            if (hologramMaterial != null)
                Destroy(hologramMaterial);
            if (hologramScanMaterial != null)
                Destroy(hologramScanMaterial);
            hologramScanLine = null;
            hologramProgressLabel = null;
            hologramMaterial = null;
            hologramScanMaterial = null;
            hologramBlock = null;
        }

        static Bounds TransformBounds(
            Bounds localBounds,
            Matrix4x4 matrix)
        {
            Vector3 center = matrix.MultiplyPoint3x4(localBounds.center);
            Vector3 extents = localBounds.extents;
            Vector3 axisX = matrix.MultiplyVector(
                new Vector3(extents.x, 0f, 0f));
            Vector3 axisY = matrix.MultiplyVector(
                new Vector3(0f, extents.y, 0f));
            Vector3 axisZ = matrix.MultiplyVector(
                new Vector3(0f, 0f, extents.z));
            extents = new Vector3(
                Mathf.Abs(axisX.x) +
                Mathf.Abs(axisY.x) +
                Mathf.Abs(axisZ.x),
                Mathf.Abs(axisX.y) +
                Mathf.Abs(axisY.y) +
                Mathf.Abs(axisZ.y),
                Mathf.Abs(axisX.z) +
                Mathf.Abs(axisY.z) +
                Mathf.Abs(axisZ.z));
            return new Bounds(center, extents * 2f);
        }

        void ShowMessage(string message, Color color)
        {
            if (repairMessage == null)
                return;
            repairMessage.text = message;
            repairMessage.color = color;
            repairMessage.gameObject.SetActive(true);
            CancelInvoke(nameof(HideMessage));
            Invoke(nameof(HideMessage), 2.2f);
        }

        void HideMessage()
        {
            if (repairMessage != null)
                repairMessage.gameObject.SetActive(false);
        }

        static RectTransform CreateRect(
            string name,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 size,
            Vector2 position)
        {
            GameObject gameObject = new GameObject(
                name,
                typeof(RectTransform));
            RectTransform rect = gameObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            if (anchorMin == Vector2.zero && anchorMax == Vector2.one)
            {
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }
            return rect;
        }

        static Text CreateText(
            string name,
            Transform parent,
            string value,
            int size,
            TextAnchor alignment)
        {
            RectTransform rect = CreateRect(
                name,
                parent,
                Vector2.zero,
                Vector2.zero,
                new Vector2(100f, 32f),
                Vector2.zero);
            Text text = rect.gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>(
                "LegacyRuntime.ttf");
            text.text = value;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            return text;
        }

        static void SetRect(
            RectTransform rect,
            Vector2 position,
            Vector2 size)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }

    static class PlayerSkillRuntimeInstaller
    {
        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStaticState()
        {
            SkillRuntimeInstallerHost.ResetInstance();
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            SkillRuntimeInstallerHost.GetOrCreate();
        }
    }

    sealed class SkillRuntimeInstallerHost : MonoBehaviour
    {
        static SkillRuntimeInstallerHost instance;
        float nextScan;

        public static void ResetInstance()
        {
            instance = null;
        }

        public static SkillRuntimeInstallerHost GetOrCreate()
        {
            if (instance != null)
                return instance;
            instance = UnityEngine.Object.FindObjectOfType<
                SkillRuntimeInstallerHost>();
            if (instance != null)
                return instance;
            GameObject root = new GameObject("PlayerSkillRuntimeInstaller");
            UnityEngine.Object.DontDestroyOnLoad(root);
            instance = root.AddComponent<SkillRuntimeInstallerHost>();
            return instance;
        }

        void Update()
        {
            if (Time.unscaledTime < nextScan)
                return;
            nextScan = Time.unscaledTime + 0.5f;
            foreach (VehicleStructureGraph graph in
                     FindObjectsOfType<VehicleStructureGraph>())
            {
                if (graph == null ||
                    VehicleCombatTeamUtility.Resolve(graph.transform) !=
                    VehicleCombatTeam.Player ||
                    graph.GetComponent<PlayerSkillRuntimeController>() != null)
                    continue;
                PlayerSkillRuntimeController runtime = graph.gameObject
                    .AddComponent<PlayerSkillRuntimeController>();
                runtime.Initialize(graph);
            }
        }
    }
}
