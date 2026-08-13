using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ModularAssembly
{
    [Serializable]
    public sealed class PresetFlightQualification
    {
        public bool verified;
        public bool noCoreContribution;
        public float upwardThrustToWeight;
        public Vector3 positiveForce;
        public Vector3 negativeForce;
        public Vector3 positiveTorque;
        public Vector3 negativeTorque;
        public float minimumAngularAccelerationDegrees;
        public string message;
    }

    [Serializable]
    public sealed class ModularPresetEntry
    {
        public const int CurrentFormatVersion = 1;
        public int formatVersion = CurrentFormatVersion;
        public string id;
        public string displayName;
        public bool builtIn;
        public long savedUtcTicks;
        public int moduleCount;
        public float totalMass;
        public string physicsRevision;
        public UnityPlanet.ModularAssembly.VehicleCoreAssistMode coreAssistMode;
        public PresetFlightQualification qualification;
        public ModularBlueprintData blueprint;

        [NonSerialized] public bool isReady;
        [NonSerialized] public string readinessMessage;
    }

    public sealed class ModularPresetLibrary
    {
        const string BuiltInId = "builtin.no_assist_vtol_trainer";
        const string BuiltInName = "无辅助VTOL教练机";
        const string PresetExtension = ".preset.json";
        const string PhysicsRevision = "RC3.2";

        ModularPresetEntry cachedBuiltIn;

        public IReadOnlyList<ModularPresetEntry> ListPresets(
            GridAssemblyModel activeModel)
        {
            var result = new List<ModularPresetEntry>();
            // The library is player-authored. Do not inject a generated
            // trainer that can be mistaken for a Modular-saved design.

            string directory = GetPresetDirectory();
            if (Directory.Exists(directory))
            {
                foreach (string path in Directory.GetFiles(
                             directory,
                             "*" + PresetExtension,
                             SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        ModularPresetEntry entry =
                            JsonUtility.FromJson<ModularPresetEntry>(
                                File.ReadAllText(path));
                        if (entry == null ||
                            entry.formatVersion !=
                            ModularPresetEntry.CurrentFormatVersion ||
                            string.IsNullOrWhiteSpace(entry.id) ||
                            entry.blueprint == null ||
                            entry.blueprint.modules == null)
                        {
                            throw new InvalidDataException(
                                "预制文件格式不完整。");
                        }

                        entry.builtIn = false;
                        entry.isReady = CanResolveModules(
                            activeModel,
                            entry.blueprint,
                            out string readiness);
                        entry.readinessMessage = readiness;
                        result.Add(entry);
                    }
                    catch (Exception exception)
                    {
                        Quarantine(path, exception);
                    }
                }
            }

            return result
                .OrderByDescending(entry => entry.builtIn)
                .ThenByDescending(entry => entry.savedUtcTicks)
                .ThenBy(entry => entry.displayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public bool SaveUserPreset(
            string requestedName,
            GridAssemblyModel activeModel,
            ModularBlueprintData blueprint,
            bool overwrite,
            out ModularPresetEntry saved,
            out string error)
        {
            saved = null;
            error = string.Empty;
            string name = NormalizeName(requestedName);
            if (string.IsNullOrEmpty(name))
            {
                error = "请输入预制名称。";
                return false;
            }

            if (activeModel == null || blueprint == null)
            {
                error = "当前飞船蓝图不可用。";
                return false;
            }

            GridAssemblyValidation validation = activeModel.Validate();
            if (!validation.IsValid)
            {
                error = "无法保存预制：" + validation.Message;
                return false;
            }

            IReadOnlyList<ModularPresetEntry> existing =
                ListPresets(activeModel);
            ModularPresetEntry sameName = existing.FirstOrDefault(entry =>
                !entry.builtIn &&
                string.Equals(
                    entry.displayName,
                    name,
                    StringComparison.OrdinalIgnoreCase));
            if (sameName != null && !overwrite)
            {
                error = "已存在同名预制，请再次确认覆盖。";
                return false;
            }

            string id = sameName != null
                ? sameName.id
                : Guid.NewGuid().ToString("N");
            ModularBlueprintData copy = CloneBlueprint(blueprint);
            GridAssemblyMetrics metrics = activeModel.CalculateMetrics();
            saved = new ModularPresetEntry
            {
                id = id,
                displayName = name,
                builtIn = false,
                savedUtcTicks = DateTime.UtcNow.Ticks,
                moduleCount = copy.modules.Length,
                totalMass = metrics.totalMass,
                physicsRevision = PhysicsRevision,
                coreAssistMode = copy.coreAssistMode,
                qualification = Qualify(activeModel),
                blueprint = copy,
                isReady = true,
                readinessMessage = "资源已就绪"
            };

            try
            {
                string directory = GetPresetDirectory();
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, id + PresetExtension);
                string temporary = path + ".tmp";
                File.WriteAllText(
                    temporary,
                    JsonUtility.ToJson(saved, true));
                if (File.Exists(path))
                    File.Replace(temporary, path, null);
                else
                    File.Move(temporary, path);
                return true;
            }
            catch (Exception exception)
            {
                saved = null;
                error = "保存预制失败：" + exception.Message;
                return false;
            }
        }

        public bool TryLoadPreset(
            string presetId,
            GridAssemblyModel activeModel,
            out ModularBlueprintData blueprint,
            out string error)
        {
            blueprint = null;
            error = string.Empty;
            ModularPresetEntry entry = ListPresets(activeModel)
                .FirstOrDefault(item => string.Equals(
                    item.id,
                    presetId,
                    StringComparison.Ordinal));
            if (entry == null)
            {
                error = "找不到所选预制。";
                return false;
            }
            if (!entry.isReady)
            {
                error = string.IsNullOrEmpty(entry.readinessMessage)
                    ? "预制资源尚未就绪。"
                    : entry.readinessMessage;
                return false;
            }
            blueprint = CloneBlueprint(entry.blueprint);
            return blueprint != null;
        }

        public bool DeleteUserPreset(
            string presetId,
            GridAssemblyModel activeModel,
            out string error)
        {
            error = string.Empty;
            ModularPresetEntry entry = ListPresets(activeModel)
                .FirstOrDefault(item => string.Equals(
                    item.id,
                    presetId,
                    StringComparison.Ordinal));
            if (entry == null)
            {
                error = "找不到所选预制。";
                return false;
            }
            if (entry.builtIn)
            {
                error = "内置预制不能删除。";
                return false;
            }
            try
            {
                string path = Path.Combine(
                    GetPresetDirectory(),
                    entry.id + PresetExtension);
                if (File.Exists(path))
                    File.Delete(path);
                return true;
            }
            catch (Exception exception)
            {
                error = "删除预制失败：" + exception.Message;
                return false;
            }
        }

        ModularPresetEntry ResolveBuiltIn(GridAssemblyModel activeModel)
        {
            if (cachedBuiltIn != null && cachedBuiltIn.isReady)
                return cachedBuiltIn;
            cachedBuiltIn = BuildTrainer(activeModel);
            return cachedBuiltIn;
        }

        static ModularPresetEntry BuildTrainer(GridAssemblyModel source)
        {
            var unavailable = new ModularPresetEntry
            {
                id = BuiltInId,
                displayName = BuiltInName,
                builtIn = true,
                savedUtcTicks = 0,
                physicsRevision = PhysicsRevision,
                coreAssistMode =
                    UnityPlanet.ModularAssembly.VehicleCoreAssistMode.Disabled,
                qualification = new PresetFlightQualification
                {
                    message = "正在等待NeoX模块目录。"
                },
                isReady = false,
                readinessMessage = "资源未就绪"
            };
            if (source == null)
                return unavailable;

            bool hasBlock = TryResolveDefinitionId(
                source,
                "block_111",
                GridModuleCategory.Structure,
                "block:common:block_111",
                out string blockId);
            bool hasRocket = TryResolveDefinitionId(
                source,
                "rocket_222",
                GridModuleCategory.MainThruster,
                "block:common:rocket_222",
                out string rocketId);
            bool hasRcs = TryResolveDefinitionId(
                source,
                "speed_rocketsmall_112",
                GridModuleCategory.RcsThruster,
                "block:speed:speed_rocketsmall_112",
                out string rcsId,
                GridModuleCategory.MainThruster);
            if (!hasBlock || !hasRocket || !hasRcs)
            {
                var missing = new List<string>();
                if (!hasBlock)
                    missing.Add("结构块 block_111");
                if (!hasRocket)
                    missing.Add("主推进器 rocket_222");
                if (!hasRcs)
                    missing.Add("RCS speed_rocketsmall_112");
                unavailable.readinessMessage =
                    "缺少运行时模块定义：" + string.Join("、", missing);
                unavailable.qualification.message =
                    unavailable.readinessMessage;
                return unavailable;
            }

            var trainer =
                new GridAssemblyModel(source.Definitions.Values);
            var frameCells = new List<Vector3Int>();
            for (int x = -2; x <= 1; x++)
            for (int y = -2; y <= 1; y++)
            for (int z = -2; z <= 1; z++)
            {
                bool coreCell =
                    x >= -1 && x <= 0 &&
                    y >= -1 && y <= 0 &&
                    z >= -1 && z <= 0;
                if (!coreCell)
                    frameCells.Add(new Vector3Int(x, y, z));
            }
            frameCells = frameCells
                .OrderBy(DistanceFromCore)
                .ThenBy(cell => cell.y)
                .ThenBy(cell => cell.z)
                .ThenBy(cell => cell.x)
                .ToList();

            foreach (Vector3Int cell in frameCells)
            {
                if (!trainer.TryPlace(
                        blockId,
                        new GridModulePose(cell, 0),
                        false,
                        out _,
                        out string error))
                {
                    unavailable.readinessMessage =
                        "内置结构生成失败：" + error;
                    unavailable.qualification.message =
                        unavailable.readinessMessage;
                    return unavailable;
                }
            }

            if (!TryPlaceOnSurface(
                    trainer,
                    rocketId,
                    new Vector3Int(-1, -2, -1),
                    Vector3Int.down,
                    out string rocketError))
            {
                unavailable.readinessMessage =
                    "主推进器生成失败：" + rocketError;
                unavailable.qualification.message =
                    unavailable.readinessMessage;
                return unavailable;
            }

            var mounts = new[]
            {
                new SurfaceMount(new Vector3Int(-2, -2, -2), Vector3Int.down),
                new SurfaceMount(new Vector3Int(1, -2, -2), Vector3Int.down),
                new SurfaceMount(new Vector3Int(-2, -2, 1), Vector3Int.down),
                new SurfaceMount(new Vector3Int(1, -2, 1), Vector3Int.down),
                new SurfaceMount(new Vector3Int(-2, 1, -2), Vector3Int.up),
                new SurfaceMount(new Vector3Int(1, 1, -2), Vector3Int.up),
                new SurfaceMount(new Vector3Int(-2, 1, 1), Vector3Int.up),
                new SurfaceMount(new Vector3Int(1, 1, 1), Vector3Int.up),
                new SurfaceMount(new Vector3Int(-2, -1, -2), Vector3Int.left),
                new SurfaceMount(new Vector3Int(-2, 0, 1), Vector3Int.left),
                new SurfaceMount(new Vector3Int(1, -1, 1), Vector3Int.right),
                new SurfaceMount(new Vector3Int(1, 0, -2), Vector3Int.right),
                new SurfaceMount(new Vector3Int(-2, -1, -2), Vector3Int.back),
                new SurfaceMount(new Vector3Int(1, 0, -2), Vector3Int.back),
                new SurfaceMount(new Vector3Int(1, -1, 1), Vector3Int.forward),
                new SurfaceMount(new Vector3Int(-2, 0, 1), Vector3Int.forward)
            };
            foreach (SurfaceMount mount in mounts)
            {
                if (!TryPlaceOnSurface(
                        trainer,
                        rcsId,
                        mount.cell,
                        mount.normal,
                        out string error))
                {
                    unavailable.readinessMessage =
                        "RCS生成失败：" + error;
                    unavailable.qualification.message =
                        unavailable.readinessMessage;
                    return unavailable;
                }
            }

            trainer.SetCoreAssistMode(
                UnityPlanet.ModularAssembly.VehicleCoreAssistMode.Disabled);
            GridAssemblyValidation validation = trainer.Validate();
            PresetFlightQualification qualification = Qualify(trainer);
            if (!validation.IsValid || !qualification.verified)
            {
                unavailable.blueprint = trainer.CaptureBlueprint();
                unavailable.moduleCount = trainer.Records.Count;
                unavailable.totalMass =
                    trainer.CalculateMetrics().totalMass;
                unavailable.qualification = qualification;
                unavailable.readinessMessage = !validation.IsValid
                    ? "内置蓝图无效：" + validation.Message
                    : qualification.message;
                return unavailable;
            }

            ModularBlueprintData blueprint = trainer.CaptureBlueprint();
            GridAssemblyMetrics metrics = trainer.CalculateMetrics();
            return new ModularPresetEntry
            {
                id = BuiltInId,
                displayName = BuiltInName,
                builtIn = true,
                savedUtcTicks = 0,
                moduleCount = blueprint.modules.Length,
                totalMass = metrics.totalMass,
                physicsRevision = PhysicsRevision,
                coreAssistMode =
                    UnityPlanet.ModularAssembly.VehicleCoreAssistMode.Disabled,
                qualification = qualification,
                blueprint = blueprint,
                isReady = true,
                readinessMessage = "无辅助飞行校验通过"
            };
        }

        static bool TryResolveDefinitionId(
            GridAssemblyModel model,
            string neoXId,
            GridModuleCategory expectedCategory,
            string preferredSourceId,
            out string moduleId,
            GridModuleCategory? alternateCategory = null)
        {
            moduleId = null;
            if (model?.Definitions == null ||
                string.IsNullOrWhiteSpace(neoXId))
                return false;

            string preferredId =
                "neox@" + (preferredSourceId ?? string.Empty)
                    .Replace("@", "_");
            if (model.Definitions.TryGetValue(
                    preferredId,
                    out GridModuleDefinition preferred) &&
                preferred != null &&
                (preferred.Category == expectedCategory ||
                 (alternateCategory.HasValue &&
                  preferred.Category == alternateCategory.Value)))
            {
                moduleId = preferred.ModuleId;
                return true;
            }

            string suffix = ":" + neoXId;
            GridModuleDefinition resolved = model.Definitions.Values
                .Where(definition =>
                    definition != null &&
                    (definition.Category == expectedCategory ||
                     (alternateCategory.HasValue &&
                      definition.Category == alternateCategory.Value)) &&
                    !string.IsNullOrWhiteSpace(definition.ModuleId) &&
                    (string.Equals(
                         definition.ModuleId,
                         neoXId,
                         StringComparison.OrdinalIgnoreCase) ||
                     definition.ModuleId.EndsWith(
                         suffix,
                         StringComparison.OrdinalIgnoreCase)))
                .OrderBy(definition =>
                    definition.ModuleId.IndexOf(
                        ":common:",
                        StringComparison.OrdinalIgnoreCase) >= 0
                        ? 0
                        : 1)
                .ThenBy(definition => definition.ModuleId.Length)
                .ThenBy(
                    definition => definition.ModuleId,
                    StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (resolved == null)
                return false;

            moduleId = resolved.ModuleId;
            return true;
        }

        static bool TryPlaceOnSurface(
            GridAssemblyModel model,
            string moduleId,
            Vector3Int surfaceCell,
            Vector3Int outwardNormal,
            out string finalError)
        {
            finalError = "没有找到合法安装姿态。";
            if (!model.Definitions.TryGetValue(
                    moduleId,
                    out GridModuleDefinition definition))
                return false;
            Vector3Int target = surfaceCell + outwardNormal;
            for (int quarterTurn = 0; quarterTurn < 4; quarterTurn++)
            {
                int orientation = GridOrientation.FromOutwardNormal(
                    outwardNormal,
                    quarterTurn);
                List<Vector3Int> cells =
                    GridOrientation.NormalizedCells(
                        definition.Footprint,
                        orientation);
                foreach (Vector3Int anchor in cells)
                {
                    GridModulePose pose = new GridModulePose(
                        target - anchor,
                        orientation);
                    if (model.TryPlace(
                            moduleId,
                            pose,
                            false,
                            out _,
                            out string error))
                        return true;
                    finalError = error;
                }
            }
            return false;
        }

        static PresetFlightQualification Qualify(GridAssemblyModel model)
        {
            var result = new PresetFlightQualification
            {
                noCoreContribution = true
            };
            if (model == null)
            {
                result.message = "没有可校验的飞船模型。";
                return result;
            }

            GridAssemblyMetrics metrics = model.CalculateMetrics();
            float mass = Mathf.Max(1f, metrics.totalMass);
            Vector3 center = metrics.localCenterOfMass;
            Vector3 inertia = Vector3.zero;
            foreach (GridModuleRecord record in model.Records)
            {
                float moduleMass = Mathf.Max(
                    0.01f,
                    record.Definition.MassKg);
                Vector3 moduleCenter = CenterOf(model, record);
                Vector3 offset = moduleCenter - center;
                Vector3 size =
                    GridOrientation.RotatedSize(
                        record.Definition.Footprint,
                        record.Pose.orientation);
                inertia.x += moduleMass *
                    ((size.y * size.y + size.z * size.z) / 12f +
                     offset.y * offset.y + offset.z * offset.z);
                inertia.y += moduleMass *
                    ((size.x * size.x + size.z * size.z) / 12f +
                     offset.x * offset.x + offset.z * offset.z);
                inertia.z += moduleMass *
                    ((size.x * size.x + size.y * size.y) / 12f +
                     offset.x * offset.x + offset.y * offset.y);

                float maximumForce = ResolveThrusterForce(
                    record.Definition.ModuleId);
                if (maximumForce <= 0f)
                    continue;
                Vector3 direction =
                    GridOrientation.Rotation(record.Pose.orientation) *
                    Vector3.back;
                direction.Normalize();
                Vector3 force = direction * maximumForce;
                Vector3 torque = Vector3.Cross(
                    moduleCenter - center,
                    force);
                AddSigned(
                    force,
                    ref result.positiveForce,
                    ref result.negativeForce);
                AddSigned(
                    torque,
                    ref result.positiveTorque,
                    ref result.negativeTorque);
            }

            result.upwardThrustToWeight =
                result.positiveForce.y / (mass * 9.81f);
            float minimumAngularRadians = Mathf.Min(
                Mathf.Min(
                    Mathf.Min(
                        result.positiveTorque.x,
                        result.negativeTorque.x) /
                    Mathf.Max(0.01f, inertia.x),
                    Mathf.Min(
                        result.positiveTorque.y,
                        result.negativeTorque.y) /
                    Mathf.Max(0.01f, inertia.y)),
                Mathf.Min(
                    result.positiveTorque.z,
                    result.negativeTorque.z) /
                Mathf.Max(0.01f, inertia.z));
            result.minimumAngularAccelerationDegrees =
                minimumAngularRadians * Mathf.Rad2Deg;

            bool sixTranslation =
                AllAxesPositive(result.positiveForce, 1000f) &&
                AllAxesPositive(result.negativeForce, 1000f);
            bool sixRotation =
                AllAxesPositive(result.positiveTorque, 100f) &&
                AllAxesPositive(result.negativeTorque, 100f);
            result.verified =
                model.CoreAssistMode ==
                UnityPlanet.ModularAssembly.VehicleCoreAssistMode.Disabled &&
                result.noCoreContribution &&
                result.upwardThrustToWeight >= 1.5f &&
                sixTranslation &&
                sixRotation &&
                result.minimumAngularAccelerationDegrees >= 30f;
            result.message = result.verified
                ? "无辅助已验证"
                : $"校验未通过：升重比{result.upwardThrustToWeight:F2}，" +
                  $"最低角加速度{result.minimumAngularAccelerationDegrees:F1}°/s²";
            return result;
        }

        static float ResolveThrusterForce(string moduleId)
        {
            return UnityPlanet.ModularAssembly.NeoXThrusterPhysicsProfile.TryResolve(
                moduleId,
                out UnityPlanet.ModularAssembly.NeoXThrusterPhysicsProfile profile)
                ? profile.MaximumForce
                : 0f;
        }

        static void AddSigned(
            Vector3 value,
            ref Vector3 positive,
            ref Vector3 negative)
        {
            for (int axis = 0; axis < 3; axis++)
            {
                float component = value[axis];
                if (component >= 0f)
                    positive[axis] += component;
                else
                    negative[axis] += -component;
            }
        }

        static bool AllAxesPositive(Vector3 value, float threshold) =>
            value.x >= threshold &&
            value.y >= threshold &&
            value.z >= threshold;

        static Vector3 CenterOf(
            GridAssemblyModel model,
            GridModuleRecord record)
        {
            List<Vector3Int> cells = model.GetCells(record);
            Vector3 sum = Vector3.zero;
            foreach (Vector3Int cell in cells)
                sum += (Vector3)cell + Vector3.one * 0.5f;
            return cells.Count > 0 ? sum / cells.Count : Vector3.zero;
        }

        static int DistanceFromCore(Vector3Int cell)
        {
            int dx = cell.x < -1 ? -1 - cell.x :
                cell.x > 0 ? cell.x : 0;
            int dy = cell.y < -1 ? -1 - cell.y :
                cell.y > 0 ? cell.y : 0;
            int dz = cell.z < -1 ? -1 - cell.z :
                cell.z > 0 ? cell.z : 0;
            return dx + dy + dz;
        }

        static bool CanResolveModules(
            GridAssemblyModel model,
            ModularBlueprintData blueprint,
            out string message)
        {
            if (model == null || blueprint?.modules == null)
            {
                message = "蓝图数据不可用。";
                return false;
            }
            string[] missing = blueprint.modules
                .Where(module =>
                    module != null &&
                    !model.Definitions.ContainsKey(module.moduleId))
                .Select(module => module.moduleId)
                .Distinct()
                .ToArray();
            if (missing.Length > 0)
            {
                message = "缺少模块定义：" +
                          string.Join("、", missing.Take(3));
                return false;
            }
            message = "资源已就绪";
            return true;
        }

        string GetPresetDirectory()
        {
            // Player presets are reusable authored designs, not world state.
            // Keep them outside individual galaxy slots so every save sees
            // the same library without duplicating preset files.
            return Path.Combine(
                Application.persistentDataPath,
                "modular_presets");
        }

        static string NormalizeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            string trimmed = value.Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars())
                trimmed = trimmed.Replace(invalid.ToString(), string.Empty);
            return trimmed.Length > 24
                ? trimmed.Substring(0, 24)
                : trimmed;
        }

        static ModularBlueprintData CloneBlueprint(
            ModularBlueprintData blueprint)
        {
            if (blueprint == null)
                return null;
            return JsonUtility.FromJson<ModularBlueprintData>(
                JsonUtility.ToJson(blueprint));
        }

        static void Quarantine(string path, Exception exception)
        {
            try
            {
                string backup = Path.Combine(
                    Path.GetDirectoryName(path) ?? string.Empty,
                    Path.GetFileNameWithoutExtension(path) +
                    $".{DateTime.UtcNow:yyyyMMddHHmmssfff}.corrupt.json");
                File.Move(path, backup);
            }
            catch (Exception backupException)
            {
                Debug.LogWarning(
                    "无法隔离损坏预制：" + backupException.Message);
            }
            Debug.LogWarning(
                "预制文件已损坏并隔离：" + exception.Message);
        }

        readonly struct SurfaceMount
        {
            public readonly Vector3Int cell;
            public readonly Vector3Int normal;

            public SurfaceMount(Vector3Int cell, Vector3Int normal)
            {
                this.cell = cell;
                this.normal = normal;
            }
        }
    }

}
