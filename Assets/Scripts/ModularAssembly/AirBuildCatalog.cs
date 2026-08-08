using System;
using System.Collections.Generic;
using System.Linq;
using ModularAssembly;
using UnityEngine;

namespace UnityPlanet.ModularAssembly
{
    public static class AirBuildCatalog
    {
        private static readonly string[] PaletteCategoryNames =
        {
            "结构", "机翼", "推进", "能源", "武器"
        };

        private static readonly HashSet<string> PaletteStructureIds =
            new HashSet<string>(new[]
            {
                "core_heavy_222", "block_111", "block_1_2_111"
            }, StringComparer.OrdinalIgnoreCase);

        private static readonly string[] PolishedIds =
        {
            "core_heavy_222",
            "block_111",
            "block_1_2_111",
            "assemble_222",
            "barrier_222",
            "emrg_beast_432",
            "camouflage_232",
            "rocket_222",
            "speed_rocketsmall_112",
            "small_propeller_224",
            "large_wing_left_361",
            "large_wing_right_361",
            "small_wing_left_231",
            "small_wing_right_231",
            "rudder_wing_223",
            "waste_rudder",
            "machinegun_111",
            "antiair_cannon_224",
            "gatlin_422",
            "missile_522",
            "missile_fighter_522",
            "guide_missile_222",
            "snipercannon_422",
            "energy_cannon_422",
            "heavy_laser_222",
            "core_energy_111",
            "fire_energy_storage_422",
            "radar_222",
            "shield_121",
            "heavy_energyshield_242",
            "uav_222",
            "wheel_basic_111",
            "wheel_m_222",
            "wheel_l_422",
            "speedwheel_small_l_322",
            "speedwheel_small_r_322",
            "speedwheel_large_l_522",
            "speedwheel_large_r_522"
        };

        private static readonly HashSet<string> Polished =
            new HashSet<string>(PolishedIds, StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> Thrusters =
            new HashSet<string>(new[]
            {
                "rocket_222", "speed_rocketsmall_112", "small_propeller_224"
            }, StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> Weapons =
            new HashSet<string>(new[]
            {
                "machinegun_111", "antiair_cannon_224", "gatlin_422",
                "missile_522", "missile_fighter_522", "guide_missile_222",
                "snipercannon_422", "energy_cannon_422", "heavy_laser_222"
            }, StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> Wheels =
            new HashSet<string>(new[]
            {
                "wheel_basic_111", "wheel_m_222", "wheel_l_422",
                "speedwheel_small_l_322", "speedwheel_small_r_322",
                "speedwheel_large_l_522", "speedwheel_large_r_522"
            }, StringComparer.OrdinalIgnoreCase);

        public static IReadOnlyList<string> OrderedIds => PolishedIds;
        public static IReadOnlyList<string> PaletteCategories =>
            PaletteCategoryNames;

        public static bool IsPolished(ModularContentRecord record)
        {
            return record != null &&
                   (record.selectableForAirBuild || Polished.Contains(record.neoXId ?? string.Empty));
        }

        public static bool IsVisibleInPalette(ModularContentRecord record)
        {
            if (!IsPolished(record))
            {
                return false;
            }

            string id = record.neoXId ?? string.Empty;
            if (PaletteStructureIds.Contains(id))
            {
                return true;
            }
            if (string.Equals(
                    id,
                    "core_energy_111",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string category = Category(record);
            return category == "机翼" ||
                   category == "推进" ||
                   category == "武器";
        }

        public static void ApplyDefaults(ModularContentRecord record)
        {
            if (record == null)
            {
                return;
            }

            if (string.Equals(record.neoXId, "assemble_battery_543", StringComparison.OrdinalIgnoreCase))
            {
                record.selectableForAirBuild = false;
                record.category = "Weapon";
                record.behavior = "EnergyCannon";
                record.chineseName = "电能炮 543";
            }
            else if (string.Equals(record.neoXId, "core_energy_111", StringComparison.OrdinalIgnoreCase))
            {
                record.category = "Energy";
                record.behavior = "Energy";
                record.chineseName = "小型能量核心";
            }
            else if (string.Equals(record.neoXId, "fire_energy_storage_422", StringComparison.OrdinalIgnoreCase))
            {
                record.category = "Energy";
                record.behavior = "Energy";
                record.chineseName = "大型储能模块";
            }

            if (Polished.Contains(record.neoXId ?? string.Empty))
            {
                record.selectableForAirBuild = true;
            }

            if (Wheels.Contains(record.neoXId ?? string.Empty))
            {
                record.category = "Mobility";
                record.behavior = "Wheel";
                string stem = PathWithoutExtension(record.sourcePath);
                record.animationGraphPath = stem + ".ags";
                record.animationDataPath = stem + ".gis";
                record.animationDecodeStatus =
                    string.IsNullOrEmpty(record.animationDecodeStatus)
                        ? "metadata+procedural"
                        : record.animationDecodeStatus;
                record.animationBones = record.animationBones != null &&
                                        record.animationBones.Length > 0
                    ? record.animationBones
                    : record.neoXId.StartsWith(
                        "speedwheel_large",
                        StringComparison.OrdinalIgnoreCase)
                        ? new[] { "biped", "wheel", "wheel2" }
                        : new[] { "biped", "wheel" };
                record.animationClips = record.animationClips != null &&
                                        record.animationClips.Length > 0
                    ? record.animationClips
                    : new[] { "idle", "wheel_rotate", "wheel_rotate_back" };
                record.related = record.related ?? new ModularContentRelatedFiles();
                record.related.animations = new[]
                {
                    record.animationGraphPath,
                    record.animationDataPath
                };
                record.wheelAxleLocal = Axis(1, 0, 0);
                record.wheelRollingForwardLocal = Axis(0, 0, 1);
            }

            if (record.visualScale <= 0f)
            {
                record.visualScale = 1f;
            }

            if (record.mountAnchor == null || record.mountAnchor.Length < 3)
            {
                int[] footprint = record.footprint ?? new[] { 1, 1, 1 };
                record.mountAnchor = new[]
                {
                    Mathf.Max(0, (Mathf.Max(1, footprint[0]) - 1) / 2),
                    Mathf.Max(0, (Mathf.Max(1, footprint[1]) - 1) / 2),
                    0
                };
            }

            if (record.visualEuler == null || record.visualEuler.Length < 3)
            {
                record.visualEuler = new[] { 0f, 0f, 0f };
            }

            if (record.visualOffset == null || record.visualOffset.Length < 3)
            {
                record.visualOffset = new[] { 0f, 0f, 0f };
            }

            if (string.IsNullOrWhiteSpace(record.mountMode))
            {
                record.mountMode = string.Equals(
                    record.neoXId,
                    "core_heavy_222",
                    StringComparison.OrdinalIgnoreCase)
                    ? "Center"
                    : IsPolished(record) ? "SurfaceBack" : "Center";
            }

            ApplyMountSemantics(record);
        }

        private static void ApplyMountSemantics(ModularContentRecord record)
        {
            string id = record.neoXId ?? string.Empty;
            Vector3Int footprint = Footprint(record);
            if (Wheels.Contains(id))
            {
                bool right = id.IndexOf("_r_", StringComparison.OrdinalIgnoreCase) >= 0;
                record.mountMode = "WheelAxle";
                record.mountProfile = "WheelAxle";
                record.mountNormalLocal = right ? Axis(-1, 0, 0) : Axis(1, 0, 0);
                record.functionalForwardLocal = Axis(0, 0, 1);
                record.wheelAxleLocal = Axis(1, 0, 0);
                record.wheelRollingForwardLocal = Axis(0, 0, 1);
                record.mountAnchor = new[]
                {
                    right ? Mathf.Max(0, footprint.x - 1) : 0,
                    Mathf.Max(0, (footprint.y - 1) / 2),
                    Mathf.Max(0, (footprint.z - 1) / 2)
                };
                if (id.IndexOf("speedwheel_small_l_322", StringComparison.OrdinalIgnoreCase) >= 0)
                    record.pairedNeoXId = "speedwheel_small_r_322";
                else if (id.IndexOf("speedwheel_small_r_322", StringComparison.OrdinalIgnoreCase) >= 0)
                    record.pairedNeoXId = "speedwheel_small_l_322";
                else if (id.IndexOf("speedwheel_large_l_522", StringComparison.OrdinalIgnoreCase) >= 0)
                    record.pairedNeoXId = "speedwheel_large_r_522";
                else if (id.IndexOf("speedwheel_large_r_522", StringComparison.OrdinalIgnoreCase) >= 0)
                    record.pairedNeoXId = "speedwheel_large_l_522";
                return;
            }
            if (string.Equals(id, "large_wing_left_361", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "small_wing_left_231", StringComparison.OrdinalIgnoreCase))
            {
                record.mountMode = "WingRootLeft";
                record.mountProfile = "WingRootLeft";
                record.mountNormalLocal = Axis(-1, 0, 0);
                record.functionalForwardLocal = Axis(0, 0, 1);
                record.mountAnchor = new[]
                {
                    Mathf.Max(0, footprint.x - 1),
                    Mathf.Max(0, (footprint.y - 1) / 2),
                    Mathf.Max(0, (footprint.z - 1) / 2)
                };
                return;
            }

            if (string.Equals(id, "large_wing_right_361", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "small_wing_right_231", StringComparison.OrdinalIgnoreCase))
            {
                record.mountMode = "WingRootRight";
                record.mountProfile = "WingRootRight";
                record.mountNormalLocal = Axis(1, 0, 0);
                record.functionalForwardLocal = Axis(0, 0, 1);
                record.mountAnchor = new[]
                {
                    0,
                    Mathf.Max(0, (footprint.y - 1) / 2),
                    Mathf.Max(0, (footprint.z - 1) / 2)
                };
                return;
            }

            if (Thrusters.Contains(id))
            {
                record.mountMode = "ThrusterInterface";
                record.mountProfile = "ThrusterInterface";
                record.mountNormalLocal = Axis(0, 0, 1);
                record.functionalForwardLocal = Axis(0, 1, 0);
                record.exhaustAxisLocal = Axis(0, 0, 1);
                record.mountAnchor = new[]
                {
                    Mathf.Max(0, (footprint.x - 1) / 2),
                    Mathf.Max(0, (footprint.y - 1) / 2),
                    0
                };
                if (string.Equals(
                        id,
                        "small_propeller_224",
                        StringComparison.OrdinalIgnoreCase))
                {
                    // The propeller source mesh already points from its mounting
                    // base toward the rotor. The rocket correction reverses it
                    // and places the rotor face against the hull.
                    record.visualEuler = new[] { 0f, 0f, 0f };
                }
                else if (IsZero(record.visualEuler))
                {
                    record.visualEuler = new[] { 0f, 180f, 0f };
                }
                return;
            }

            if (Weapons.Contains(id))
            {
                record.mountMode = "WeaponBase";
                record.mountProfile = "WeaponBase";
                record.mountNormalLocal = Axis(0, 1, 0);
                record.functionalForwardLocal = Axis(0, 0, 1);
                record.mountAnchor = new[]
                {
                    Mathf.Max(0, (footprint.x - 1) / 2),
                    0,
                    Mathf.Max(0, (footprint.z - 1) / 2)
                };
                return;
            }

            if (string.IsNullOrWhiteSpace(record.mountProfile))
            {
                record.mountProfile = record.mountMode;
            }
            EnsureAxis(ref record.mountNormalLocal, 0f, 0f, 1f);
            EnsureAxis(ref record.functionalForwardLocal, 0f, 1f, 0f);
            EnsureAxis(ref record.exhaustAxisLocal, 0f, 0f, 1f);
        }

        private static Vector3Int Footprint(ModularContentRecord record)
        {
            int[] value = record.footprint;
            return value != null && value.Length >= 3
                ? new Vector3Int(
                    Mathf.Max(1, value[0]),
                    Mathf.Max(1, value[1]),
                    Mathf.Max(1, value[2]))
                : Vector3Int.one;
        }

        private static float[] Axis(float x, float y, float z) => new[] { x, y, z };

        private static string PathWithoutExtension(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }
            int index = value.LastIndexOf('.');
            return index > 0 ? value.Substring(0, index) : value;
        }

        private static void EnsureAxis(ref float[] value, float x, float y, float z)
        {
            if (value == null || value.Length < 3)
            {
                value = Axis(x, y, z);
            }
        }

        private static bool IsZero(float[] value)
        {
            return value == null || value.Length < 3 ||
                   (Mathf.Abs(value[0]) < 0.01f &&
                    Mathf.Abs(value[1]) < 0.01f &&
                    Mathf.Abs(value[2]) < 0.01f);
        }

        public static Vector3Int MountAnchor(ModularContentRecord record, Vector3Int footprint)
        {
            ApplyDefaults(record);
            int[] value = record?.mountAnchor;
            if (value == null || value.Length < 3)
            {
                return new Vector3Int(
                    Mathf.Max(0, (footprint.x - 1) / 2),
                    Mathf.Max(0, (footprint.y - 1) / 2),
                    0);
            }

            return new Vector3Int(
                Mathf.Clamp(value[0], 0, Mathf.Max(0, footprint.x - 1)),
                Mathf.Clamp(value[1], 0, Mathf.Max(0, footprint.y - 1)),
                Mathf.Clamp(value[2], 0, Mathf.Max(0, footprint.z - 1)));
        }

        public static string Category(ModularContentRecord record)
        {
            if (record == null)
            {
                return "结构";
            }

            string behavior = (record.behavior ?? string.Empty).ToLowerInvariant();
            if (behavior == "wheel") return "移动";
            if (behavior == "wing") return "机翼";
            if (behavior.Contains("thruster")) return "推进";
            if (behavior == "controlsurface") return "机翼";
            if (behavior == "hover") return "推进";
            if (behavior == "shield" || behavior.Contains("armor")) return "防御";
            if (behavior == "battery" || behavior == "energy" || behavior == "radar" ||
                behavior == "drone" || behavior == "repair" || behavior == "emp")
            {
                return "能源";
            }

            string[] weapons =
            {
                "cannon", "kineticrapid", "gatling", "rocket", "snipercannon",
                "energycannon", "laser", "flame", "mortar", "bomb", "drill", "saw"
            };
            if (weapons.Any(item => behavior == item))
            {
                return "武器";
            }
            return "结构";
        }
    }

    [Serializable]
    public struct GridSurfaceHit
    {
        public Vector3Int Cell;
        public Vector3Int Normal;
        public Vector3 WorldPoint;

        public GridSurfaceHit(Vector3Int cell, Vector3Int normal, Vector3 worldPoint)
        {
            Cell = cell;
            Normal = normal;
            WorldPoint = worldPoint;
        }
    }

    public sealed class GridPlacementCandidate
    {
        public GridModulePose PrimaryPose;
        public GridModulePose MirroredPose;
        public GridModuleDefinition MirroredDefinition;
        public GridSurfaceHit Surface;
        public bool HasMirror;
        public bool IsValid;
        public bool NearBoundary;
        public bool OutOfBounds;
        public string Error;
        public Vector3Int MountNormal;
        public Vector3Int FunctionalForward;
        public Vector3Int ExhaustDirection;
        public Vector3 WorldMountNormal;
        public Vector3 WorldFunctionalDirection;
        public Vector3 WorldExhaustDirection;
    }

    public static class GridPlacementResolver
    {
        public static bool TryResolve(
            GridAssemblyModel model,
            GridModuleDefinition definition,
            ModularContentRecord record,
            GridSurfaceHit surface,
            Vector3Int coreForward,
            Vector3Int coreUp,
            int quarterTurns,
            bool mirror,
            string ignoreRuntimeId,
            out GridPlacementCandidate candidate)
        {
            candidate = new GridPlacementCandidate
            {
                Surface = surface,
                HasMirror = mirror,
                Error = string.Empty
            };
            if (model == null || definition == null || surface.Normal == Vector3Int.zero)
            {
                candidate.Error = "没有可用的安装表面。";
                return false;
            }

            AirBuildCatalog.ApplyDefaults(record);
            Vector3Int functionalForward = ProjectAxis(coreForward, surface.Normal);
            if (functionalForward == Vector3Int.zero)
            {
                functionalForward = ProjectAxis(coreUp, surface.Normal);
            }
            if (functionalForward == Vector3Int.zero)
            {
                functionalForward = AnyPerpendicular(surface.Normal);
            }
            functionalForward = RotateQuarter(
                functionalForward,
                surface.Normal,
                quarterTurns & 3);

            int orientation = ResolveOrientation(
                record,
                surface.Normal,
                functionalForward,
                quarterTurns & 3);
            candidate.MountNormal = surface.Normal;
            candidate.FunctionalForward = GridOrientation.Rotate(
                Axis(record.functionalForwardLocal, Vector3Int.forward),
                orientation);
            candidate.ExhaustDirection =
                string.Equals(record.mountProfile, "ThrusterInterface", StringComparison.OrdinalIgnoreCase)
                    ? surface.Normal
                    : GridOrientation.Rotate(
                        Axis(record.exhaustAxisLocal, Vector3Int.forward),
                        orientation);
            Vector3Int anchor = AirBuildCatalog.MountAnchor(record, definition.Footprint);
            Vector3Int minimum = RawRotatedMinimum(definition.Footprint, orientation);
            Vector3Int normalizedAnchor = GridOrientation.Rotate(anchor, orientation) - minimum;
            Vector3Int origin = surface.Cell + surface.Normal - normalizedAnchor;
            candidate.PrimaryPose = new GridModulePose(origin, orientation);

            List<Vector3Int> primaryCells = GridAssemblyModel.GetCells(definition, candidate.PrimaryPose);
            candidate.OutOfBounds = primaryCells.Any(cell => !GridBuildBounds.Contains(cell));
            candidate.NearBoundary = primaryCells.Any(GridBuildBounds.NearEdge);
            if (mirror)
            {
                candidate.MirroredDefinition = model.GetMirroredDefinition(definition);
                candidate.MirroredPose = model.MirrorPose(definition, candidate.PrimaryPose, "preview");
                List<Vector3Int> mirroredCells =
                    GridAssemblyModel.GetCells(
                        candidate.MirroredDefinition ?? definition,
                        candidate.MirroredPose);
                candidate.OutOfBounds |= mirroredCells.Any(cell => !GridBuildBounds.Contains(cell));
                candidate.NearBoundary |= mirroredCells.Any(GridBuildBounds.NearEdge);
            }

            if (candidate.OutOfBounds)
            {
                candidate.Error = GridBuildBounds.OutOfBoundsMessage;
                return false;
            }

            candidate.IsValid = model.CanPlacePose(
                definition,
                candidate.PrimaryPose,
                mirror,
                ignoreRuntimeId,
                out string error);
            candidate.Error = candidate.IsValid ? string.Empty : error;
            return candidate.IsValid;
        }

        private static Vector3Int RawRotatedMinimum(Vector3Int footprint, int orientation)
        {
            Vector3Int minimum = new Vector3Int(int.MaxValue, int.MaxValue, int.MaxValue);
            for (int x = 0; x < footprint.x; x++)
            for (int y = 0; y < footprint.y; y++)
            for (int z = 0; z < footprint.z; z++)
            {
                minimum = Vector3Int.Min(
                    minimum,
                    GridOrientation.Rotate(new Vector3Int(x, y, z), orientation));
            }
            return minimum;
        }

        private static int ResolveOrientation(
            ModularContentRecord record,
            Vector3Int surfaceNormal,
            Vector3Int desiredForward,
            int quarterTurns)
        {
            string profile = record?.mountProfile ?? record?.mountMode ?? "SurfaceBack";
            if (!string.Equals(profile, "WingRootLeft", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(profile, "WingRootRight", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(profile, "WeaponBase", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(profile, "WheelAxle", StringComparison.OrdinalIgnoreCase))
            {
                return GridOrientation.FromOutwardNormal(surfaceNormal, quarterTurns);
            }

            Vector3Int localMount = Axis(record.mountNormalLocal, Vector3Int.forward);
            Vector3Int localForward = Axis(record.functionalForwardLocal, Vector3Int.forward);
            int best = GridOrientation.FromOutwardNormal(surfaceNormal, quarterTurns);
            int bestScore = int.MinValue;
            for (int orientation = 0; orientation < 24; orientation++)
            {
                if (GridOrientation.Rotate(localMount, orientation) != surfaceNormal)
                {
                    continue;
                }
                Vector3Int forward = GridOrientation.Rotate(localForward, orientation);
                int score = Dot(forward, desiredForward);
                if (score > bestScore)
                {
                    best = orientation;
                    bestScore = score;
                }
            }
            return best;
        }

        private static Vector3Int Axis(float[] value, Vector3Int fallback)
        {
            if (value == null || value.Length < 3)
            {
                return fallback;
            }
            Vector3Int axis = new Vector3Int(
                Mathf.RoundToInt(value[0]),
                Mathf.RoundToInt(value[1]),
                Mathf.RoundToInt(value[2]));
            return axis == Vector3Int.zero ? fallback : axis;
        }

        private static Vector3Int ProjectAxis(Vector3Int axis, Vector3Int normal)
        {
            return axis - normal * Dot(axis, normal);
        }

        private static int Dot(Vector3Int left, Vector3Int right)
        {
            return left.x * right.x + left.y * right.y + left.z * right.z;
        }

        private static Vector3Int AnyPerpendicular(Vector3Int normal)
        {
            return Mathf.Abs(normal.y) < 1
                ? Vector3Int.up
                : Vector3Int.forward;
        }

        private static Vector3Int RotateQuarter(
            Vector3Int axis,
            Vector3Int normal,
            int quarterTurns)
        {
            Vector3 rotated = Quaternion.AngleAxis(quarterTurns * 90f, normal) * (Vector3)axis;
            return new Vector3Int(
                Mathf.RoundToInt(rotated.x),
                Mathf.RoundToInt(rotated.y),
                Mathf.RoundToInt(rotated.z));
        }
    }

    public sealed class AirBuildSurfaceCell : MonoBehaviour
    {
        public Vector3Int Cell { get; private set; }

        public void Initialize(Vector3Int cell)
        {
            Cell = cell;
        }
    }
}
