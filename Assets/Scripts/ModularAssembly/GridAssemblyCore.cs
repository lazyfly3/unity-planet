using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ModularAssembly
{
    public enum GridModuleCategory
    {
        Core,
        Structure,
        Armor,
        MainThruster,
        RcsThruster,
        KineticWeapon,
        Battery,
        Mobility
    }

    [CreateAssetMenu(menuName = "Modular Assembly/Grid Module Definition")]
    public sealed class GridModuleDefinition : ScriptableObject
    {
        [SerializeField] string moduleId;
        [SerializeField] string displayName;
        [SerializeField] GridModuleCategory category;
        [SerializeField] GameObject prefab;
        [SerializeField] Vector3Int footprint = Vector3Int.one;
        [SerializeField] float massKg;
        [SerializeField] float energyCapacity;
        [SerializeField] float energyCost;
        [SerializeField] float maxIntegrity;
        [SerializeField] float thrustNewtons;
        [SerializeField] SpacecraftEditor.ShipPartDefinition runtimePartDefinition;

        public string ModuleId => moduleId;
        public string DisplayName => displayName;
        public GridModuleCategory Category => category;
        public GameObject Prefab => prefab;
        public Vector3Int Footprint => footprint;
        public float MassKg => massKg;
        public float EnergyCapacity => energyCapacity;
        public float EnergyCost => energyCost;
        public float MaxIntegrity => maxIntegrity;
        public float ThrustNewtons => thrustNewtons;
        public SpacecraftEditor.ShipPartDefinition RuntimePartDefinition => runtimePartDefinition;

        public void Configure(
            string id,
            string label,
            GridModuleCategory moduleCategory,
            GameObject modulePrefab,
            Vector3Int size,
            float mass,
            float capacity,
            float cost,
            float integrity,
            float thrust,
            SpacecraftEditor.ShipPartDefinition partDefinition)
        {
            moduleId = id;
            displayName = label;
            category = moduleCategory;
            prefab = modulePrefab;
            footprint = new Vector3Int(
                Mathf.Max(1, size.x),
                Mathf.Max(1, size.y),
                Mathf.Max(1, size.z));
            massKg = Mathf.Max(0f, mass);
            energyCapacity = Mathf.Max(0f, capacity);
            energyCost = Mathf.Max(0f, cost);
            maxIntegrity = Mathf.Max(1f, integrity);
            thrustNewtons = Mathf.Max(0f, thrust);
            runtimePartDefinition = partDefinition;
        }
    }

    [Serializable]
    public struct GridModulePose
    {
        public int x;
        public int y;
        public int z;
        public int orientation;
        public string mirrorGroupId;

        public Vector3Int Origin => new Vector3Int(x, y, z);

        public GridModulePose(Vector3Int origin, int rotation, string mirrorGroup = "")
        {
            x = origin.x;
            y = origin.y;
            z = origin.z;
            orientation = GridOrientation.NormalizeIndex(rotation);
            mirrorGroupId = mirrorGroup ?? string.Empty;
        }
    }

    [Serializable]
    public sealed class ModularBlueprintModule
    {
        public string runtimeId;
        public string moduleId;
        public GridModulePose pose;
        public string behaviorSettings;
    }

    [Serializable]
    public sealed class ModularBlueprintData
    {
        public const int CurrentFormatVersion = 5;
        public int formatVersion = CurrentFormatVersion;
        public ModularBlueprintModule[] modules = Array.Empty<ModularBlueprintModule>();
        public long savedUtcTicks;
        public UnityPlanet.ModularAssembly.VehicleCoreAssistMode coreAssistMode =
            UnityPlanet.ModularAssembly.VehicleCoreAssistMode.Standard;
    }

    public sealed class GridModuleRecord
    {
        public string RuntimeId;
        public GridModuleDefinition Definition;
        public GridModulePose Pose;
        public string BehaviorSettings;

        public GridModuleRecord Clone()
        {
            return new GridModuleRecord
            {
                RuntimeId = RuntimeId,
                Definition = Definition,
                Pose = Pose,
                BehaviorSettings = BehaviorSettings
            };
        }
    }

    public sealed class GridAssemblyValidation
    {
        public bool HasCore;
        public bool HasOverlap;
        public bool ExceedsModuleLimit;
        public bool ExceedsEnergy;
        public bool ExceedsCpu;
        public int CpuCost;
        public readonly List<string> DisconnectedIds = new List<string>();
        public string Message;
        public bool IsValid =>
            HasCore
            && !HasOverlap
            && !ExceedsModuleLimit
            && !ExceedsEnergy
            && !ExceedsCpu
            && DisconnectedIds.Count == 0;
    }

    public struct GridAssemblyMetrics
    {
        public int moduleCount;
        public float totalMass;
        public float energyCapacity;
        public float energyCost;
        public float totalThrust;
        public Vector3 positiveAuthority;
        public Vector3 negativeAuthority;
        public Vector3 estimatedAcceleration;
        public Vector3 localCenterOfMass;
    }

    public static class GridOrientation
    {
        static readonly Quaternion[] rotations = BuildRotations();

        public static int Count => rotations.Length;
        public static int NormalizeIndex(int value) =>
            rotations.Length == 0 ? 0 : (value % rotations.Length + rotations.Length) % rotations.Length;
        public static Quaternion Rotation(int index) => rotations[NormalizeIndex(index)];

        public static Vector3Int Rotate(Vector3Int value, int orientation)
        {
            Vector3 result = Rotation(orientation) * (Vector3)value;
            return new Vector3Int(
                Mathf.RoundToInt(result.x),
                Mathf.RoundToInt(result.y),
                Mathf.RoundToInt(result.z));
        }

        public static List<Vector3Int> NormalizedCells(Vector3Int footprint, int orientation)
        {
            var result = new List<Vector3Int>(footprint.x * footprint.y * footprint.z);
            Vector3Int minimum = new Vector3Int(int.MaxValue, int.MaxValue, int.MaxValue);
            for (int x = 0; x < footprint.x; x++)
            for (int y = 0; y < footprint.y; y++)
            for (int z = 0; z < footprint.z; z++)
            {
                Vector3Int rotated = Rotate(new Vector3Int(x, y, z), orientation);
                result.Add(rotated);
                minimum = Vector3Int.Min(minimum, rotated);
            }
            for (int index = 0; index < result.Count; index++)
                result[index] -= minimum;
            return result;
        }

        public static Vector3Int RotatedSize(Vector3Int footprint, int orientation)
        {
            List<Vector3Int> cells = NormalizedCells(footprint, orientation);
            Vector3Int maximum = Vector3Int.zero;
            foreach (Vector3Int cell in cells)
                maximum = Vector3Int.Max(maximum, cell);
            return maximum + Vector3Int.one;
        }

        public static int FromForwardAndUp(Vector3 forward, Vector3 up)
        {
            forward.Normalize();
            up = Vector3.ProjectOnPlane(up, forward).normalized;
            float best = float.NegativeInfinity;
            int bestIndex = 0;
            for (int index = 0; index < rotations.Length; index++)
            {
                Quaternion rotation = rotations[index];
                float score =
                    Vector3.Dot(rotation * Vector3.forward, forward) * 2f
                    + Vector3.Dot(rotation * Vector3.up, up);
                if (score > best)
                {
                    best = score;
                    bestIndex = index;
                }
            }
            return bestIndex;
        }

        public static int FromOutwardNormal(Vector3Int normal, int quarterTurns)
        {
            Vector3 forward = normal;
            Vector3 up = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > 0.9f
                ? Vector3.forward
                : Vector3.up;
            up = Quaternion.AngleAxis(quarterTurns * 90f, forward) * up;
            return FromForwardAndUp(forward, up);
        }

        static Quaternion[] BuildRotations()
        {
            var result = new List<Quaternion> { Quaternion.identity };
            Vector3[] axes =
            {
                Vector3.forward, Vector3.right, Vector3.back,
                Vector3.left, Vector3.up, Vector3.down
            };
            foreach (Vector3 forward in axes)
            foreach (Vector3 up in axes)
            {
                if (Mathf.Abs(Vector3.Dot(forward, up)) > 0.01f)
                    continue;
                Quaternion candidate = Quaternion.LookRotation(forward, up);
                bool duplicate = result.Exists(item => Mathf.Abs(Quaternion.Dot(item, candidate)) > 0.9999f);
                if (!duplicate)
                    result.Add(candidate);
            }
            return result.ToArray();
        }
    }

    public sealed class GridAssemblyModel
    {
        public const int ModuleLimit = 1024;
        public const string CoreRuntimeId = "core";
        public const string CoreModuleId = "core";
        static readonly Vector3Int[] Neighbors =
        {
            Vector3Int.right, Vector3Int.left, Vector3Int.up,
            Vector3Int.down, new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1)
        };

        readonly Dictionary<string, GridModuleDefinition> definitions;
        readonly List<GridModuleRecord> records = new List<GridModuleRecord>();
        UnityPlanet.ModularAssembly.VehicleCoreAssistMode coreAssistMode =
            UnityPlanet.ModularAssembly.VehicleCoreAssistMode.Standard;

        public event Action Changed;
        public IReadOnlyList<GridModuleRecord> Records => records;
        public IReadOnlyDictionary<string, GridModuleDefinition> Definitions => definitions;
        public UnityPlanet.ModularAssembly.VehicleCoreAssistMode CoreAssistMode =>
            coreAssistMode;

        public GridAssemblyModel(IEnumerable<GridModuleDefinition> moduleDefinitions)
        {
            definitions = moduleDefinitions
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.ModuleId))
                .GroupBy(item => item.ModuleId)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            InitializeCore();
        }

        public void InitializeCore()
        {
            records.Clear();
            if (definitions.TryGetValue(CoreModuleId, out GridModuleDefinition core))
            {
                records.Add(new GridModuleRecord
                {
                    RuntimeId = CoreRuntimeId,
                    Definition = core,
                    Pose = new GridModulePose(new Vector3Int(-1, -1, -1), 0)
                });
            }
            Changed?.Invoke();
        }

        public GridModuleRecord Find(string runtimeId) =>
            records.Find(item => item.RuntimeId == runtimeId);

        public bool TrySetBehaviorSettings(
            string runtimeId,
            string settings,
            out string error)
        {
            GridModuleRecord selected = Find(runtimeId);
            if (selected == null)
            {
                error = "未找到所选模块。";
                return false;
            }
            string value = settings ?? string.Empty;
            selected.BehaviorSettings = value;
            if (!string.IsNullOrEmpty(selected.Pose.mirrorGroupId))
            {
                foreach (GridModuleRecord paired in records.Where(item =>
                             item.RuntimeId != selected.RuntimeId &&
                             item.Pose.mirrorGroupId == selected.Pose.mirrorGroupId))
                {
                    paired.BehaviorSettings = value;
                }
            }
            error = string.Empty;
            Changed?.Invoke();
            return true;
        }

        public void SetCoreAssistMode(
            UnityPlanet.ModularAssembly.VehicleCoreAssistMode value)
        {
            if (!Enum.IsDefined(
                    typeof(UnityPlanet.ModularAssembly.VehicleCoreAssistMode),
                    value))
            {
                value =
                    UnityPlanet.ModularAssembly.VehicleCoreAssistMode.Standard;
            }
            coreAssistMode = value;
            Changed?.Invoke();
        }

        public List<Vector3Int> GetCells(GridModuleRecord record) =>
            GetCells(record.Definition, record.Pose);

        public static List<Vector3Int> GetCells(GridModuleDefinition definition, GridModulePose pose)
        {
            List<Vector3Int> cells = GridOrientation.NormalizedCells(definition.Footprint, pose.orientation);
            for (int index = 0; index < cells.Count; index++)
                cells[index] += pose.Origin;
            return cells;
        }

        public bool CanPlacePose(
            GridModuleDefinition definition,
            GridModulePose pose,
            bool mirror,
            string ignoreRuntimeId,
            out string error)
        {
            List<string> ignored = ResolveOperationIds(ignoreRuntimeId);
            List<GridModuleRecord> candidates = BuildCandidates(definition, pose, mirror, ignoreRuntimeId);
            return CanApply(candidates, ignored, out error);
        }

        public bool TryPlace(
            string moduleId,
            GridModulePose pose,
            bool mirror,
            out string primaryRuntimeId,
            out string error)
        {
            primaryRuntimeId = string.Empty;
            if (!definitions.TryGetValue(moduleId, out GridModuleDefinition definition)
                || definition.Category == GridModuleCategory.Core)
            {
                error = "模块定义无效。";
                return false;
            }

            string runtimeId = Guid.NewGuid().ToString("N");
            List<GridModuleRecord> candidates = BuildCandidates(definition, pose, mirror, runtimeId);
            if (!CanApply(candidates, Array.Empty<string>(), out error))
                return false;
            records.AddRange(candidates);
            primaryRuntimeId = runtimeId;
            Changed?.Invoke();
            return true;
        }

        public bool TryMove(string runtimeId, GridModulePose pose, out string error)
        {
            GridModuleRecord source = Find(runtimeId);
            if (source == null || source.RuntimeId == CoreRuntimeId)
            {
                error = "核心不能移动。";
                return false;
            }
            List<string> movingIds = ResolveOperationIds(runtimeId);
            bool paired = movingIds.Count > 1;
            List<GridModuleRecord> candidates = BuildCandidates(
                source.Definition,
                pose,
                paired,
                source.RuntimeId,
                source.Pose.mirrorGroupId);
            foreach (GridModuleRecord candidate in candidates)
                candidate.BehaviorSettings = source.BehaviorSettings;
            if (!CanApply(candidates, movingIds, out error))
                return false;
            records.RemoveAll(item => movingIds.Contains(item.RuntimeId));
            records.AddRange(candidates);
            Changed?.Invoke();
            return true;
        }

        public bool TryRemove(string runtimeId, out string error)
        {
            if (runtimeId == CoreRuntimeId)
            {
                error = "核心不能删除。";
                return false;
            }
            List<string> removing = ResolveOperationIds(runtimeId);
            if (removing.Count == 0)
            {
                error = "未找到模块。";
                return false;
            }
            records.RemoveAll(item => removing.Contains(item.RuntimeId));
            error = string.Empty;
            Changed?.Invoke();
            return true;
        }

        public bool TryUnlinkMirror(string runtimeId)
        {
            GridModuleRecord source = Find(runtimeId);
            if (source == null || string.IsNullOrEmpty(source.Pose.mirrorGroupId))
                return false;
            string group = source.Pose.mirrorGroupId;
            foreach (GridModuleRecord record in records)
            {
                if (record.Pose.mirrorGroupId != group)
                    continue;
                GridModulePose pose = record.Pose;
                pose.mirrorGroupId = string.Empty;
                record.Pose = pose;
            }
            Changed?.Invoke();
            return true;
        }

        public void RemoveIds(IEnumerable<string> runtimeIds)
        {
            var set = new HashSet<string>(runtimeIds ?? Array.Empty<string>());
            set.Remove(CoreRuntimeId);
            if (set.Count == 0)
                return;
            records.RemoveAll(item => set.Contains(item.RuntimeId));
            ClearOrphanedMirrorGroups();
            Changed?.Invoke();
        }

        public GridAssemblyValidation Validate()
        {
            var result = new GridAssemblyValidation();
            result.HasCore = records.Count(item => item.Definition.Category == GridModuleCategory.Core) == 1;
            result.ExceedsModuleLimit = records.Count > ModuleLimit;
            var occupancy = new Dictionary<Vector3Int, string>();
            foreach (GridModuleRecord record in records)
            foreach (Vector3Int cell in GetCells(record))
            {
                if (occupancy.ContainsKey(cell))
                    result.HasOverlap = true;
                else
                    occupancy[cell] = record.RuntimeId;
            }

            HashSet<string> connected = ConnectedToCore(occupancy);
            foreach (GridModuleRecord record in records)
                if (!connected.Contains(record.RuntimeId))
                    result.DisconnectedIds.Add(record.RuntimeId);

            GridAssemblyMetrics metrics = CalculateMetrics();
            result.ExceedsEnergy = metrics.energyCost > metrics.energyCapacity + 0.001f;
            result.CpuCost =
                UnityPlanet.ModularAssembly.ModuleCpuBudget.Total(records);
            result.ExceedsCpu =
                result.CpuCost >
                UnityPlanet.ModularAssembly.ModuleCpuBudget.Maximum;
            if (!result.HasCore) result.Message = "缺少核心";
            else if (result.HasOverlap) result.Message = "模块占格冲突";
            else if (result.ExceedsModuleLimit)
                result.Message = $"超过{ModuleLimit}个模块";
            else if (result.DisconnectedIds.Count > 0) result.Message = "存在未连接核心的模块";
            else if (result.ExceedsEnergy) result.Message = "能源预算不足";
            else if (result.ExceedsCpu)
                result.Message =
                    $"CPU超过{UnityPlanet.ModularAssembly.ModuleCpuBudget.Maximum}";
            else result.Message = "设计有效";
            return result;
        }

        public GridAssemblyMetrics CalculateMetrics()
        {
            var metrics = new GridAssemblyMetrics();
            Vector3 weightedCenter = Vector3.zero;
            foreach (GridModuleRecord record in records)
            {
                GridModuleDefinition definition = record.Definition;
                float mass = definition.MassKg;
                Vector3 center = ModuleCenter(record);
                metrics.moduleCount++;
                metrics.totalMass += mass;
                metrics.energyCapacity += definition.EnergyCapacity;
                metrics.energyCost += definition.EnergyCost;
                weightedCenter += center * mass;
                if (definition.ThrustNewtons <= 0f)
                    continue;
                Vector3 forceDirection = GridOrientation.Rotation(record.Pose.orientation) * Vector3.back;
                Vector3 force = forceDirection.normalized * definition.ThrustNewtons;
                metrics.totalThrust += definition.ThrustNewtons;
                AccumulateAuthority(ref metrics.positiveAuthority, ref metrics.negativeAuthority, force);
            }
            metrics.localCenterOfMass = metrics.totalMass > 0.001f
                ? weightedCenter / metrics.totalMass
                : Vector3.zero;
            metrics.estimatedAcceleration = metrics.totalMass > 0.001f
                ? new Vector3(
                    Mathf.Max(metrics.positiveAuthority.x, metrics.negativeAuthority.x),
                    Mathf.Max(metrics.positiveAuthority.y, metrics.negativeAuthority.y),
                    Mathf.Max(metrics.positiveAuthority.z, metrics.negativeAuthority.z)) / metrics.totalMass
                : Vector3.zero;
            return metrics;
        }

        public List<List<GridModuleRecord>> GetDisconnectedComponents()
        {
            GridAssemblyValidation validation = Validate();
            var remaining = new HashSet<string>(validation.DisconnectedIds);
            var occupancy = BuildOccupancy(records);
            var result = new List<List<GridModuleRecord>>();
            while (remaining.Count > 0)
            {
                string seed = remaining.First();
                var component = new List<GridModuleRecord>();
                var queue = new Queue<string>();
                queue.Enqueue(seed);
                remaining.Remove(seed);
                while (queue.Count > 0)
                {
                    string current = queue.Dequeue();
                    GridModuleRecord record = Find(current);
                    if (record != null)
                        component.Add(record.Clone());
                    foreach (string adjacent in AdjacentModuleIds(current, occupancy))
                        if (remaining.Remove(adjacent))
                            queue.Enqueue(adjacent);
                }
                result.Add(component);
            }
            return result;
        }

        public ModularBlueprintData CaptureBlueprint()
        {
            return new ModularBlueprintData
            {
                formatVersion = ModularBlueprintData.CurrentFormatVersion,
                savedUtcTicks = DateTime.UtcNow.Ticks,
                coreAssistMode = coreAssistMode,
                modules = records.Select(item => new ModularBlueprintModule
                {
                    runtimeId = item.RuntimeId,
                    moduleId = item.Definition.ModuleId,
                    pose = item.Pose,
                    behaviorSettings = item.BehaviorSettings
                }).ToArray()
            };
        }

        public bool RestoreBlueprint(ModularBlueprintData blueprint, out string error)
        {
            if (blueprint == null ||
                blueprint.formatVersion !=
                ModularBlueprintData.CurrentFormatVersion ||
                blueprint.modules == null)
            {
                error = "蓝图格式不受支持。";
                return false;
            }
            var restored = new List<GridModuleRecord>();
            var occupied = new HashSet<Vector3Int>();
            foreach (ModularBlueprintModule module in blueprint.modules)
            {
                if (module == null || !definitions.TryGetValue(module.moduleId, out GridModuleDefinition definition))
                    continue;
                string id = definition.Category == GridModuleCategory.Core
                    ? CoreRuntimeId
                    : string.IsNullOrWhiteSpace(module.runtimeId)
                        ? Guid.NewGuid().ToString("N")
                        : module.runtimeId;
                GridModulePose pose = definition.Category == GridModuleCategory.Core
                    ? new GridModulePose(new Vector3Int(-1, -1, -1), 0)
                    : module.pose;
                var record = new GridModuleRecord
                {
                    RuntimeId = id,
                    Definition = definition,
                    Pose = pose,
                    BehaviorSettings = module.behaviorSettings ?? string.Empty
                };
                List<Vector3Int> cells = GetCells(record);
                if (cells.Any(occupied.Contains))
                {
                    error = "蓝图包含重叠模块。";
                    return false;
                }
                foreach (Vector3Int cell in cells)
                    occupied.Add(cell);
                restored.Add(record);
            }
            if (!restored.Exists(item => item.Definition.Category == GridModuleCategory.Core)
                && definitions.TryGetValue(CoreModuleId, out GridModuleDefinition core))
            {
                restored.Insert(0, new GridModuleRecord
                {
                    RuntimeId = CoreRuntimeId,
                    Definition = core,
                    Pose = new GridModulePose(new Vector3Int(-1, -1, -1), 0)
                });
            }
            records.Clear();
            records.AddRange(restored.Take(ModuleLimit));
            ClearOrphanedMirrorGroups();
            coreAssistMode = blueprint.coreAssistMode;
            if (!Enum.IsDefined(
                    typeof(UnityPlanet.ModularAssembly.VehicleCoreAssistMode),
                    coreAssistMode))
            {
                coreAssistMode =
                    UnityPlanet.ModularAssembly.VehicleCoreAssistMode.Standard;
            }
            error = string.Empty;
            Changed?.Invoke();
            return true;
        }

        public static Vector3 ModuleCenter(GridModuleRecord record)
        {
            Vector3Int size = GridOrientation.RotatedSize(
                record.Definition.Footprint,
                record.Pose.orientation);
            return (Vector3)record.Pose.Origin + (Vector3)size * 0.5f;
        }

        public GridModulePose MirrorPose(GridModuleDefinition definition, GridModulePose source, string group)
        {
            List<Vector3Int> reflected = GetCells(definition, source)
                .Select(cell => new Vector3Int(-cell.x - 1, cell.y, cell.z))
                .ToList();
            Vector3 forward = GridOrientation.Rotation(source.orientation) * Vector3.forward;
            Vector3 up = GridOrientation.Rotation(source.orientation) * Vector3.up;
            forward.x = -forward.x;
            up.x = -up.x;
            if (RequiresMirroredAxleFlip(definition))
            {
                // Generic wheels reuse one visual for both sides. Reflecting the
                // pose preserves their +X mount axis. Turn the wheel 180 degrees
                // around its up axis so the hub faces the hull on the other side
                // without turning the suspension bracket upside down.
                forward = -forward;
            }
            int orientation = GridOrientation.FromForwardAndUp(forward, up);
            Vector3Int minimum = reflected.Aggregate(Vector3Int.Min);
            return new GridModulePose(minimum, orientation, group);
        }

        static bool RequiresMirroredAxleFlip(GridModuleDefinition definition)
        {
            string id = definition?.ModuleId ?? string.Empty;
            return id.IndexOf(
                       "wheel_basic_111",
                       StringComparison.OrdinalIgnoreCase) >= 0
                   || id.IndexOf(
                       "wheel_m_222",
                       StringComparison.OrdinalIgnoreCase) >= 0
                   || id.IndexOf(
                       "wheel_l_422",
                       StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public GridModuleDefinition GetMirroredDefinition(GridModuleDefinition definition)
        {
            return ResolveMirroredDefinition(definition);
        }

        List<GridModuleRecord> BuildCandidates(
            GridModuleDefinition definition,
            GridModulePose pose,
            bool mirror,
            string runtimeId,
            string existingGroup = null)
        {
            string group = mirror
                ? string.IsNullOrEmpty(existingGroup) ? Guid.NewGuid().ToString("N") : existingGroup
                : string.Empty;
            pose.mirrorGroupId = group;
            var primary = new GridModuleRecord
            {
                RuntimeId = runtimeId,
                Definition = definition,
                Pose = pose
            };
            var result = new List<GridModuleRecord> { primary };
            if (!mirror)
                return result;
            GridModuleDefinition mirroredDefinition = ResolveMirroredDefinition(definition);
            GridModulePose mirroredPose = MirrorPose(definition, pose, group);
            var mirrored = new GridModuleRecord
            {
                RuntimeId = Guid.NewGuid().ToString("N"),
                Definition = mirroredDefinition,
                Pose = mirroredPose
            };
            var primaryCells = new HashSet<Vector3Int>(GetCells(primary));
            if (!primaryCells.SetEquals(GetCells(mirrored)))
                result.Add(mirrored);
            else
            {
                primary.Pose.mirrorGroupId = string.Empty;
            }
            return result;
        }

        GridModuleDefinition ResolveMirroredDefinition(GridModuleDefinition source)
        {
            if (source == null || string.IsNullOrEmpty(source.ModuleId))
                return source;

            string counterpartId = source.ModuleId;
            if (counterpartId.IndexOf("large_wing_left_361", StringComparison.OrdinalIgnoreCase) >= 0)
                counterpartId = ReplaceIgnoreCase(counterpartId, "large_wing_left_361", "large_wing_right_361");
            else if (counterpartId.IndexOf("large_wing_right_361", StringComparison.OrdinalIgnoreCase) >= 0)
                counterpartId = ReplaceIgnoreCase(counterpartId, "large_wing_right_361", "large_wing_left_361");
            else if (counterpartId.IndexOf("small_wing_left_231", StringComparison.OrdinalIgnoreCase) >= 0)
                counterpartId = ReplaceIgnoreCase(counterpartId, "small_wing_left_231", "small_wing_right_231");
            else if (counterpartId.IndexOf("small_wing_right_231", StringComparison.OrdinalIgnoreCase) >= 0)
                counterpartId = ReplaceIgnoreCase(counterpartId, "small_wing_right_231", "small_wing_left_231");
            else if (counterpartId.IndexOf("speedwheel_small_l_322", StringComparison.OrdinalIgnoreCase) >= 0)
                counterpartId = ReplaceIgnoreCase(counterpartId, "speedwheel_small_l_322", "speedwheel_small_r_322");
            else if (counterpartId.IndexOf("speedwheel_small_r_322", StringComparison.OrdinalIgnoreCase) >= 0)
                counterpartId = ReplaceIgnoreCase(counterpartId, "speedwheel_small_r_322", "speedwheel_small_l_322");
            else if (counterpartId.IndexOf("speedwheel_large_l_522", StringComparison.OrdinalIgnoreCase) >= 0)
                counterpartId = ReplaceIgnoreCase(counterpartId, "speedwheel_large_l_522", "speedwheel_large_r_522");
            else if (counterpartId.IndexOf("speedwheel_large_r_522", StringComparison.OrdinalIgnoreCase) >= 0)
                counterpartId = ReplaceIgnoreCase(counterpartId, "speedwheel_large_r_522", "speedwheel_large_l_522");
            else
                return source;

            if (definitions.TryGetValue(counterpartId, out GridModuleDefinition counterpart))
                return counterpart;

            return definitions.Values.FirstOrDefault(item =>
                       item != null &&
                       string.Equals(item.ModuleId, counterpartId, StringComparison.OrdinalIgnoreCase))
                   ?? source;
        }

        static string ReplaceIgnoreCase(string source, string oldValue, string newValue)
        {
            int index = source.IndexOf(oldValue, StringComparison.OrdinalIgnoreCase);
            return index < 0
                ? source
                : source.Substring(0, index) + newValue + source.Substring(index + oldValue.Length);
        }

        bool CanApply(
            List<GridModuleRecord> candidates,
            IEnumerable<string> ignoredIds,
            out string error)
        {
            var ignored = new HashSet<string>(ignoredIds ?? Array.Empty<string>());
            List<GridModuleRecord> baseRecords = records.Where(item => !ignored.Contains(item.RuntimeId)).ToList();
            if (baseRecords.Count + candidates.Count > ModuleLimit)
            {
                error = $"超过{ModuleLimit}个模块上限。";
                return false;
            }
            int cpuCost =
                UnityPlanet.ModularAssembly.ModuleCpuBudget.Total(baseRecords)
                + UnityPlanet.ModularAssembly.ModuleCpuBudget.Total(candidates);
            if (cpuCost >
                UnityPlanet.ModularAssembly.ModuleCpuBudget.Maximum)
            {
                error =
                    $"CPU超过{UnityPlanet.ModularAssembly.ModuleCpuBudget.Maximum}。";
                return false;
            }
            Dictionary<Vector3Int, string> occupancy = BuildOccupancy(baseRecords);
            foreach (GridModuleRecord candidate in candidates)
            foreach (Vector3Int cell in GetCells(candidate))
            {
                if (occupancy.ContainsKey(cell))
                {
                    error = "该位置已被占用。";
                    return false;
                }
                occupancy[cell] = candidate.RuntimeId;
            }
            var finalIds = new HashSet<string>(baseRecords.Select(item => item.RuntimeId));
            finalIds.UnionWith(candidates.Select(item => item.RuntimeId));
            HashSet<string> connected = ConnectedToCore(occupancy);
            if (candidates.Any(item => !connected.Contains(item.RuntimeId)))
            {
                error = "模块必须与核心结构整面连接。";
                return false;
            }
            error = string.Empty;
            return true;
        }

        List<string> ResolveOperationIds(string runtimeId)
        {
            GridModuleRecord source = Find(runtimeId);
            if (source == null)
                return new List<string>();
            if (string.IsNullOrEmpty(source.Pose.mirrorGroupId))
                return new List<string> { source.RuntimeId };
            return records
                .Where(item => item.Pose.mirrorGroupId == source.Pose.mirrorGroupId)
                .Select(item => item.RuntimeId)
                .ToList();
        }

        int ClearOrphanedMirrorGroups()
        {
            HashSet<string> invalidGroups = records
                .Where(item =>
                    item != null &&
                    !string.IsNullOrEmpty(item.Pose.mirrorGroupId))
                .GroupBy(item => item.Pose.mirrorGroupId)
                .Where(group => group.Count() != 2)
                .Select(group => group.Key)
                .ToHashSet(StringComparer.Ordinal);
            if (invalidGroups.Count == 0)
                return 0;

            int cleared = 0;
            foreach (GridModuleRecord record in records)
            {
                if (record == null ||
                    !invalidGroups.Contains(record.Pose.mirrorGroupId))
                    continue;
                GridModulePose pose = record.Pose;
                pose.mirrorGroupId = string.Empty;
                record.Pose = pose;
                cleared++;
            }
            return cleared;
        }

        static Dictionary<Vector3Int, string> BuildOccupancy(IEnumerable<GridModuleRecord> source)
        {
            var result = new Dictionary<Vector3Int, string>();
            foreach (GridModuleRecord record in source)
            foreach (Vector3Int cell in GetCells(record.Definition, record.Pose))
                if (!result.ContainsKey(cell))
                    result[cell] = record.RuntimeId;
            return result;
        }

        HashSet<string> ConnectedToCore(Dictionary<Vector3Int, string> occupancy)
        {
            var connected = new HashSet<string>();
            GridModuleRecord core = records.Find(item => item.Definition.Category == GridModuleCategory.Core);
            if (core == null)
                return connected;
            var queue = new Queue<string>();
            queue.Enqueue(core.RuntimeId);
            connected.Add(core.RuntimeId);
            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                foreach (string adjacent in AdjacentModuleIds(current, occupancy))
                    if (connected.Add(adjacent))
                        queue.Enqueue(adjacent);
            }
            return connected;
        }

        static IEnumerable<string> AdjacentModuleIds(
            string runtimeId,
            Dictionary<Vector3Int, string> occupancy)
        {
            var result = new HashSet<string>();
            foreach (KeyValuePair<Vector3Int, string> pair in occupancy)
            {
                if (pair.Value != runtimeId)
                    continue;
                foreach (Vector3Int direction in Neighbors)
                    if (occupancy.TryGetValue(pair.Key + direction, out string adjacent)
                        && adjacent != runtimeId)
                        result.Add(adjacent);
            }
            return result;
        }

        static void AccumulateAuthority(ref Vector3 positive, ref Vector3 negative, Vector3 force)
        {
            positive += new Vector3(
                Mathf.Max(0f, force.x),
                Mathf.Max(0f, force.y),
                Mathf.Max(0f, force.z));
            negative += new Vector3(
                Mathf.Max(0f, -force.x),
                Mathf.Max(0f, -force.y),
                Mathf.Max(0f, -force.z));
        }
    }

    public sealed class GridAssemblyHistory
    {
        readonly Stack<ModularBlueprintData> undo = new Stack<ModularBlueprintData>();
        readonly Stack<ModularBlueprintData> redo = new Stack<ModularBlueprintData>();

        public bool CanUndo => undo.Count > 0;
        public bool CanRedo => redo.Count > 0;

        public ModularBlueprintData Capture(GridAssemblyModel model) => model.CaptureBlueprint();

        public void Record(ModularBlueprintData previous)
        {
            if (previous == null)
                return;
            undo.Push(previous);
            while (undo.Count > 64)
            {
                ModularBlueprintData[] items = undo.Reverse().Skip(1).ToArray();
                undo.Clear();
                foreach (ModularBlueprintData item in items)
                    undo.Push(item);
            }
            redo.Clear();
        }

        public bool Undo(GridAssemblyModel model)
        {
            if (!CanUndo)
                return false;
            redo.Push(model.CaptureBlueprint());
            return model.RestoreBlueprint(undo.Pop(), out _);
        }

        public bool Redo(GridAssemblyModel model)
        {
            if (!CanRedo)
                return false;
            undo.Push(model.CaptureBlueprint());
            return model.RestoreBlueprint(redo.Pop(), out _);
        }

        public void Clear()
        {
            undo.Clear();
            redo.Clear();
        }
    }

    public sealed class ModularBlueprintStore
    {
        const string FileName = "modular_ship.json";
        readonly string fileName;
        string slotId;

        public ModularBlueprintStore(string requestedFileName = FileName)
        {
            fileName = string.IsNullOrWhiteSpace(requestedFileName)
                ? FileName
                : Path.GetFileName(requestedFileName);
        }

        public string ActiveSlotId
        {
            get
            {
                EnsureSlot();
                return slotId;
            }
        }

        public bool TryLoad(out ModularBlueprintData blueprint, out string error)
        {
            blueprint = null;
            error = string.Empty;
            string path = GetPath();
            if (!File.Exists(path))
                return false;
            try
            {
                blueprint = JsonUtility.FromJson<ModularBlueprintData>(File.ReadAllText(path));
                if (blueprint == null ||
                    blueprint.formatVersion !=
                    ModularBlueprintData.CurrentFormatVersion ||
                    blueprint.modules == null)
                    throw new InvalidDataException("不支持或不完整的模块蓝图。");
                return true;
            }
            catch (Exception exception)
            {
                string backup = Path.Combine(
                    Path.GetDirectoryName(path) ?? string.Empty,
                    $"{Path.GetFileNameWithoutExtension(fileName)}.{DateTime.UtcNow:yyyyMMddHHmmssfff}.corrupt.json");
                try
                {
                    File.Move(path, backup);
                }
                catch (Exception backupException)
                {
                    Debug.LogWarning("无法隔离损坏蓝图：" + backupException.Message);
                }
                error = "蓝图已损坏并隔离：" + exception.Message;
                blueprint = null;
                return false;
            }
        }

        public void Save(ModularBlueprintData blueprint)
        {
            if (blueprint == null)
                throw new ArgumentNullException(nameof(blueprint));
            blueprint.formatVersion =
                ModularBlueprintData.CurrentFormatVersion;
            blueprint.savedUtcTicks = DateTime.UtcNow.Ticks;
            string path = GetPath();
            string directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory ?? throw new InvalidOperationException("无效的蓝图目录。"));
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(blueprint, true));
            if (File.Exists(path))
                File.Replace(temporary, path, null);
            else
                File.Move(temporary, path);
        }

        string GetPath()
        {
            EnsureSlot();
            return Path.Combine(GalaxySaveSlotService.GetSpacecraftDirectory(slotId), fileName);
        }

        void EnsureSlot()
        {
            if (!string.IsNullOrEmpty(slotId))
                return;
            slotId = GalaxyLaunchContext.SelectedSlotId;
            if (string.IsNullOrWhiteSpace(slotId))
            {
                GalaxySaveSlotMetadata development = GalaxySaveSlotService.GetOrCreateDevelopmentSlot();
                slotId = development.slotId;
                GalaxyLaunchContext.SelectSlot(slotId);
            }
        }
    }
}
