using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace CityGeneration
{
    public enum CityPlanningState
    {
        Idle,
        DrawBoundary,
        PlaceAnchors,
        PaintZones,
        GeneratePreview,
        ReviewIssues,
        ConfirmRepairs,
        Constructing,
        Locked,
        Replan
    }

    public enum CityPlanningAnchorType
    {
        CityGate,
        Cbd,
        TransitHub,
        IndustrialHub,
        Park,
        CivicCenter,
        Landmark
    }

    public enum CityZoneType
    {
        Unassigned,
        Commercial,
        MixedUse,
        Residential,
        Industrial,
        Civic,
        Park
    }

    public enum CityRoadHierarchy
    {
        Arterial,
        Collector,
        Local
    }

    public enum CityLockedObjectType
    {
        Anchor,
        Zone,
        Road,
        Block,
        Lot,
        Building
    }

    public enum CitySemanticPointType
    {
        VehicleLane,
        Parking,
        BusStop,
        Pedestrian,
        Crosswalk,
        StreetLight,
        Tree,
        Bench
    }

    public enum CityPlanningIssueSeverity
    {
        Info,
        Warning,
        Error
    }

    [Serializable]
    public sealed class CityPlanningAnchorData
    {
        public string stableId;
        public CityPlanningAnchorType type;
        public Vector2 position;
        [Min(1f)] public float influenceRadius = 60f;
        public bool isLocked;

        public CityPlanningAnchorData Clone()
        {
            return new CityPlanningAnchorData
            {
                stableId = stableId,
                type = type,
                position = position,
                influenceRadius = influenceRadius,
                isLocked = isLocked
            };
        }
    }

    [Serializable]
    public struct CityZonePaintRun
    {
        public int startIndex;
        public int length;
        public CityZoneType zoneType;

        public CityZonePaintRun(
            int valueStartIndex,
            int valueLength,
            CityZoneType valueZoneType)
        {
            startIndex = valueStartIndex;
            length = valueLength;
            zoneType = valueZoneType;
        }
    }

    [Serializable]
    public sealed class CityZonePaintGrid
    {
        public Vector2 origin;
        [Min(1f)] public float cellSize = 5f;
        [Min(0)] public int width;
        [Min(0)] public int height;
        public CityZonePaintRun[] runs = Array.Empty<CityZonePaintRun>();

        public int CellCount => Mathf.Max(0, width) * Mathf.Max(0, height);

        public CityZoneType[] Decode()
        {
            var values = new CityZoneType[CellCount];
            if (runs == null)
                return values;
            for (int runIndex = 0; runIndex < runs.Length; runIndex++)
            {
                CityZonePaintRun run = runs[runIndex];
                int first = Mathf.Clamp(
                    run.startIndex,
                    0,
                    values.Length);
                int last = Mathf.Clamp(
                    run.startIndex + Mathf.Max(0, run.length),
                    first,
                    values.Length);
                for (int index = first; index < last; index++)
                    values[index] = run.zoneType;
            }
            return values;
        }

        public void Encode(IReadOnlyList<CityZoneType> values)
        {
            int count = CellCount;
            if (values == null || values.Count != count || count == 0)
            {
                runs = Array.Empty<CityZonePaintRun>();
                return;
            }

            var encoded = new List<CityZonePaintRun>();
            int first = 0;
            CityZoneType current = values[0];
            for (int index = 1; index <= values.Count; index++)
            {
                if (index < values.Count && values[index] == current)
                    continue;
                encoded.Add(new CityZonePaintRun(
                    first,
                    index - first,
                    current));
                if (index < values.Count)
                {
                    first = index;
                    current = values[index];
                }
            }
            runs = encoded.ToArray();
        }

        public CityZonePaintGrid Clone()
        {
            return new CityZonePaintGrid
            {
                origin = origin,
                cellSize = cellSize,
                width = width,
                height = height,
                runs = runs == null
                    ? Array.Empty<CityZonePaintRun>()
                    : (CityZonePaintRun[])runs.Clone()
            };
        }
    }

    [Serializable]
    public sealed class CityLockedOverride
    {
        public string stableId;
        public CityLockedObjectType objectType;
        public Vector2 position;
        public Vector2[] footprint = Array.Empty<Vector2>();
        public bool isLocked = true;

        public CityLockedOverride Clone()
        {
            return new CityLockedOverride
            {
                stableId = stableId,
                objectType = objectType,
                position = position,
                footprint = footprint == null
                    ? Array.Empty<Vector2>()
                    : (Vector2[])footprint.Clone(),
                isLocked = isLocked
            };
        }
    }

    [Serializable]
    public sealed class CityPlanData
    {
        public const int CurrentGeneratorVersion = 1;
        public const float DefaultZoneCellSize = 5f;
        public const float MaximumBoundarySpan = 512f;

        public string cityId;
        public int generatorVersion = CurrentGeneratorVersion;
        public int seed = 12345;
        public Vector2[] boundary = Array.Empty<Vector2>();
        public float platformElevation;
        public CityPlanningAnchorData[] anchors =
            Array.Empty<CityPlanningAnchorData>();
        public CityZonePaintGrid zonePaintGrid = new CityZonePaintGrid();
        public CityLockedOverride[] lockedOverrides =
            Array.Empty<CityLockedOverride>();
        public int planRevision;

        public CityPlanData Clone()
        {
            var result = new CityPlanData
            {
                cityId = cityId,
                generatorVersion = generatorVersion,
                seed = seed,
                boundary = boundary == null
                    ? Array.Empty<Vector2>()
                    : (Vector2[])boundary.Clone(),
                platformElevation = platformElevation,
                zonePaintGrid = zonePaintGrid?.Clone()
                    ?? new CityZonePaintGrid(),
                planRevision = planRevision
            };

            result.anchors = new CityPlanningAnchorData[
                anchors?.Length ?? 0];
            for (int i = 0; i < result.anchors.Length; i++)
                result.anchors[i] = anchors[i]?.Clone();

            result.lockedOverrides = new CityLockedOverride[
                lockedOverrides?.Length ?? 0];
            for (int i = 0; i < result.lockedOverrides.Length; i++)
            {
                result.lockedOverrides[i] =
                    lockedOverrides[i]?.Clone();
            }
            return result;
        }
    }

    [Serializable]
    public sealed class CityZoneData
    {
        public string stableId;
        public CityZoneType type;
        public Vector2[] boundary = Array.Empty<Vector2>();
        [Range(0f, 1f)] public float density = 0.5f;
        public float minimumBuildingHeight = 8f;
        public float maximumBuildingHeight = 32f;
        public float targetBlockArea = 1600f;
    }

    [Serializable]
    public sealed class CityRoadNodeData
    {
        public string stableId;
        public Vector2 position;
        public string[] connectedEdgeIds = Array.Empty<string>();
    }

    [Serializable]
    public sealed class CityRoadEdgeData
    {
        public string stableId;
        public string startNodeId;
        public string endNodeId;
        public CityRoadHierarchy hierarchy;
        public float width;
        public Vector2[] controlPoints = Array.Empty<Vector2>();
        public bool isLocked;
        public bool hasVehicleLanes = true;
    }

    [Serializable]
    public sealed class CityPlannedBlockData
    {
        public string stableId;
        public string zoneId;
        public CityZoneType zoneType;
        public Vector2[] footprint = Array.Empty<Vector2>();
        public string[] frontageRoadIds = Array.Empty<string>();
        public float area;
        public bool isLocked;
    }

    [Serializable]
    public sealed class CityPlannedLotData
    {
        public string stableId;
        public string blockId;
        public CityZoneType zoneType;
        public Vector2[] footprint = Array.Empty<Vector2>();
        public Vector2 frontageStart;
        public Vector2 frontageEnd;
        public Vector2 entrance;
        public float setback;
        public float coverage;
    }

    [Serializable]
    public sealed class CityPlannedBuildingData
    {
        public string stableId;
        public string lotId;
        public string catalogId;
        public CityZoneType zoneType;
        public Vector2[] footprint = Array.Empty<Vector2>();
        public Vector2 frontageDirection;
        public float height;
        public float uniformScale = 1f;
        public int materialIndex;
        public bool isLocked;
    }

    [Serializable]
    public sealed class CitySemanticPointData
    {
        public string stableId;
        public CitySemanticPointType type;
        public Vector2 position;
        public Vector2 forward = Vector2.up;
        public string roadId;
        public string lotId;
    }

    [Serializable]
    public sealed class CityPlanningIssue
    {
        public string code;
        public CityPlanningIssueSeverity severity;
        public string message;
        public Vector2 position;
        public string[] affectedIds = Array.Empty<string>();
        public string[] repairOptions = Array.Empty<string>();
    }

    [Serializable]
    public sealed class CityValidationReport
    {
        public float score;
        public CityPlanningIssue[] issues =
            Array.Empty<CityPlanningIssue>();

        public bool HasErrors
        {
            get
            {
                if (issues == null)
                    return false;
                for (int i = 0; i < issues.Length; i++)
                {
                    if (issues[i] != null
                        && issues[i].severity
                        == CityPlanningIssueSeverity.Error)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        public bool CanCommit => !HasErrors && score >= 80f;
    }

    [Serializable]
    public sealed class CityGenerationOutput
    {
        public CityZoneData[] zones = Array.Empty<CityZoneData>();
        public CityRoadNodeData[] roadNodes =
            Array.Empty<CityRoadNodeData>();
        public CityRoadEdgeData[] roadEdges =
            Array.Empty<CityRoadEdgeData>();
        public CityPlannedBlockData[] blocks =
            Array.Empty<CityPlannedBlockData>();
        public CityPlannedLotData[] lots =
            Array.Empty<CityPlannedLotData>();
        public CityPlannedBuildingData[] buildings =
            Array.Empty<CityPlannedBuildingData>();
        public CitySemanticPointData[] semanticPoints =
            Array.Empty<CitySemanticPointData>();
        public CityGenerationResult legacyResult;
        public string roadPatternId;
        public string layoutChecksum;
    }

    public sealed class CityPreviewResult
    {
        public CityPlanData Plan { get; internal set; }
        public CityGenerationOutput Output { get; internal set; }
        public CityValidationReport Validation { get; internal set; }
        public int CandidateIndex { get; internal set; }
        public float Score =>
            Validation == null ? 0f : Validation.score;
    }

    [Serializable]
    public sealed class CityDirtyRegionSet
    {
        public bool wholeCity = true;
        public string[] zoneIds = Array.Empty<string>();
        public Bounds[] localBounds = Array.Empty<Bounds>();
    }

    [Serializable]
    public sealed class CityPlanSaveEntry
    {
        public int planningSchemaVersion = 1;
        public int generatorVersion =
            CityPlanData.CurrentGeneratorVersion;
        public CityPlanData planData;
        public CityGenerationOutput layoutCache;
        public string layoutChecksum;
        public bool legacyFrozen;
    }

    public interface ICityPlanningPipeline
    {
        Task<CityPreviewResult> GenerateAutomaticPreviewAsync(
            CityPlanData boundaryOnlyPlan,
            CancellationToken cancellationToken);

        Task<CityPreviewResult> GeneratePreviewAsync(
            CityPlanData plan,
            CityDirtyRegionSet dirtyRegions,
            CancellationToken cancellationToken);

        CityValidationReport Validate(
            CityPlanData plan,
            CityGenerationOutput output);
    }
}
