using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityGeneration
{
    [Serializable]
    public sealed class CityGenerationSettings
    {
        [Min(2f)] public float roadSegmentLength = 10f;
        [Min(1f)] public float majorRoadWidth = 5f;
        [Min(0.5f)] public float minorRoadWidth = 3f;
        [Min(8f)] public float blockSpacing = 32f;
        [Min(0f)] public float sidewalkWidth = 1.5f;
        [Min(0f)] public float lotGap = 1.5f;
        [Min(0.25f)] public float buildingSetback = 1.5f;
        [Min(1f)] public float minimumBuildingHeight = 8f;
        [Min(1f)] public float maximumBuildingHeight = 32f;
        [Range(0f, 1f)] public float roadCurvature = 0.38f;
        [Range(0f, 1f)] public float minorRoadDensity = 0.62f;
        [Range(0f, 1f)] public float blockDensity = 0.72f;
        [Range(0f, 1f)] public float buildingDensity = 0.76f;
        [Min(120f)] public float targetLotArea = 420f;
        [Min(4f)] public float minimumLotFrontage = 8f;
        [Range(0f, 0.35f)] public float blockIrregularity = 0.18f;
        [Range(0f, 1f)] public float buildingChamferChance = 0.48f;
        public bool useModularRoadLayout;
        [Min(4f)] public float modularRoadCellSize = 10f;
        [Min(0.5f)] public float modularPathwayUnitSize = 2.5f;
        [Range(4, 32)] public int modularChunkSize = 16;
        [Range(0.02f, 0.3f)] public float maximumRoadGrade = 0.1f;
        [Min(0.5f)] public float terrainBlendWidth = 5f;
        [Min(1)] public int minimumBranchSegments = 3;
        [Min(1)] public int maximumBranchSegments = 8;
        [Min(1)] public int maxMajorRoadSegments = 300;
        [Min(1)] public int maxMinorRoadSegments = 1200;
        [Min(0.1f)] public float minimumBoundaryEdge = 5f;
        [Min(1f)] public float minimumBoundaryArea = 400f;
        [Min(0.01f)] public float boundarySnapTolerance = 0.25f;
        [Range(3, 128)] public int maximumBoundaryPoints = 128;

        public CityGenerationSettings ValidatedCopy()
        {
            return new CityGenerationSettings
            {
                roadSegmentLength = Mathf.Max(2f, roadSegmentLength),
                majorRoadWidth = Mathf.Max(1f, majorRoadWidth),
                minorRoadWidth = Mathf.Max(0.5f, minorRoadWidth),
                blockSpacing = Mathf.Max(8f, blockSpacing),
                sidewalkWidth = Mathf.Max(0f, sidewalkWidth),
                lotGap = Mathf.Max(0f, lotGap),
                buildingSetback = Mathf.Max(0.25f, buildingSetback),
                minimumBuildingHeight = Mathf.Max(1f, minimumBuildingHeight),
                maximumBuildingHeight = Mathf.Max(minimumBuildingHeight, maximumBuildingHeight),
                roadCurvature = Mathf.Clamp01(roadCurvature),
                minorRoadDensity = Mathf.Clamp01(minorRoadDensity),
                blockDensity = Mathf.Clamp01(blockDensity),
                buildingDensity = Mathf.Clamp01(buildingDensity),
                targetLotArea = Mathf.Max(120f, targetLotArea),
                minimumLotFrontage = Mathf.Max(4f, minimumLotFrontage),
                blockIrregularity = Mathf.Clamp(blockIrregularity, 0f, 0.35f),
                buildingChamferChance = Mathf.Clamp01(buildingChamferChance),
                useModularRoadLayout = useModularRoadLayout,
                modularRoadCellSize = Mathf.Max(4f, modularRoadCellSize),
                modularPathwayUnitSize = Mathf.Clamp(
                    modularPathwayUnitSize,
                    0.5f,
                    Mathf.Max(0.5f, modularRoadCellSize * 0.5f)),
                modularChunkSize = Mathf.Clamp(modularChunkSize, 4, 32),
                maximumRoadGrade = Mathf.Clamp(maximumRoadGrade, 0.02f, 0.3f),
                terrainBlendWidth = Mathf.Max(0.5f, terrainBlendWidth),
                minimumBranchSegments = Mathf.Max(1, minimumBranchSegments),
                maximumBranchSegments = Mathf.Max(
                    Mathf.Max(1, minimumBranchSegments),
                    maximumBranchSegments),
                maxMajorRoadSegments = Mathf.Max(1, maxMajorRoadSegments),
                maxMinorRoadSegments = Mathf.Max(1, maxMinorRoadSegments),
                minimumBoundaryEdge = Mathf.Max(0.1f, minimumBoundaryEdge),
                minimumBoundaryArea = Mathf.Max(1f, minimumBoundaryArea),
                boundarySnapTolerance = Mathf.Clamp(
                    boundarySnapTolerance,
                    0.01f,
                    2f),
                maximumBoundaryPoints = Mathf.Clamp(
                    maximumBoundaryPoints,
                    3,
                    128)
            };
        }
    }

    public enum CityBoundaryRepairMode
    {
        None,
        ConcaveHull,
        ConvexHull
    }

    public sealed class CityBoundaryResolution
    {
        public IReadOnlyList<Vector2> SourcePoints { get; internal set; }
        public IReadOnlyList<List<Vector2>> Regions { get; internal set; }
        public IReadOnlyList<List<Vector2>> IgnoredRegions { get; internal set; }
        public int IntersectionCount { get; internal set; }
        public int CollapsedPointCount { get; internal set; }
        public CityBoundaryRepairMode RepairMode { get; internal set; }
        public bool WasAutoRepaired =>
            RepairMode != CityBoundaryRepairMode.None;
        public float TotalArea { get; internal set; }
        public string Message { get; internal set; }
    }

    public sealed class CityRoadSegment
    {
        public Vector2 Start { get; }
        public Vector2 End { get; }
        public float Width { get; }
        public bool IsMajor { get; }
        public float TerrainGrade { get; }
        public bool CrossesWater { get; }
        public bool IsSwitchback { get; }

        public CityRoadSegment(
            Vector2 start,
            Vector2 end,
            float width,
            bool isMajor,
            float terrainGrade = 0f,
            bool crossesWater = false,
            bool isSwitchback = false)
        {
            Start = start;
            End = end;
            Width = width;
            IsMajor = isMajor;
            TerrainGrade = Mathf.Max(0f, terrainGrade);
            CrossesWater = crossesWater;
            IsSwitchback = isSwitchback;
        }

        public Vector2[] GetCorners()
        {
            Vector2 direction = (End - Start).normalized;
            Vector2 normal = new Vector2(-direction.y, direction.x) * (Width * 0.5f);
            return new[]
            {
                Start + normal,
                End + normal,
                End - normal,
                Start - normal
            };
        }
    }

    public sealed class CityBlockData
    {
        public IReadOnlyList<Vector2> Footprint { get; }
        public CityZoneType ZoneType { get; }

        public CityBlockData(
            IReadOnlyList<Vector2> footprint,
            CityZoneType zoneType = CityZoneType.Residential)
        {
            Footprint = footprint;
            ZoneType = zoneType;
        }
    }

    public sealed class CityLotData
    {
        public IReadOnlyList<Vector2> Footprint { get; }

        public CityLotData(IReadOnlyList<Vector2> footprint)
        {
            Footprint = footprint;
        }
    }

    public sealed class CityBuildingData
    {
        public IReadOnlyList<Vector2> Footprint { get; }
        public float Height { get; }
        public int MaterialIndex { get; }
        public int PrefabIndex { get; }
        public Vector2 FrontageDirection { get; }

        public CityBuildingData(
            IReadOnlyList<Vector2> footprint,
            float height,
            int materialIndex,
            int prefabIndex = 0,
            Vector2 frontageDirection = default)
        {
            Footprint = footprint;
            Height = height;
            MaterialIndex = materialIndex;
            PrefabIndex = prefabIndex;
            FrontageDirection = frontageDirection;
        }
    }

    public sealed class CityGenerationResult
    {
        public bool IsSuccess { get; internal set; }
        public string Error { get; internal set; }
        public IReadOnlyList<Vector2> Boundary { get; internal set; }
        public IReadOnlyList<List<Vector2>> Regions { get; internal set; }
        public CityBoundaryResolution BoundaryResolution { get; internal set; }
        public Vector2 ModularRoadAxis { get; internal set; } = Vector2.right;
        public List<CityRoadSegment> Roads { get; } = new List<CityRoadSegment>();
        public List<CityBlockData> Blocks { get; } = new List<CityBlockData>();
        public List<CityLotData> Lots { get; } = new List<CityLotData>();
        public List<CityBuildingData> Buildings { get; } = new List<CityBuildingData>();
        public List<CityRoadModulePlacement> RoadModules { get; } =
            new List<CityRoadModulePlacement>();
        public List<CityPathwayModulePlacement> PathwayModules { get; } =
            new List<CityPathwayModulePlacement>();
        public CityModularNetworkResult ModularNetworkResult
        {
            get;
            internal set;
        }
        public CityRoadLayoutValidationResult RoadLayoutValidation
        {
            get;
            internal set;
        }
        public bool EnforcePackageOnlyRoads { get; internal set; }
        public bool EnforcePackageOnlySurfaces { get; internal set; }
        public CityGenerationDiagnostics Diagnostics { get; } =
            new CityGenerationDiagnostics();

        internal static CityGenerationResult Failed(string error)
        {
            return new CityGenerationResult
            {
                IsSuccess = false,
                Error = error ?? "城市生成失败。"
            };
        }
    }

    public sealed class CityGenerationDiagnostics
    {
        public float MaximumRoadGrade { get; internal set; }
        public int WaterRoadCount { get; internal set; }
        public int SwitchbackRoadCount { get; internal set; }
        public int SlopedBuildingCount { get; internal set; }
        public int WaterBuildingCount { get; internal set; }
        public int SupportPileCount { get; internal set; }
        public float MaximumGroundHeight { get; internal set; }
        public float PlatformTopHeight { get; internal set; }
        public float MaximumFoundationClearance { get; internal set; }
        public int FoundationCount { get; internal set; }
        public int FoundationColumnCount { get; internal set; }
        public int BoundaryIntersectionCount { get; internal set; }
        public int IgnoredBoundaryRegionCount { get; internal set; }
        public bool BoundaryWasAutoRepaired { get; internal set; }
        public int RoadConnectedComponentCount { get; internal set; }
        public int RoadTopologyRepairCount { get; internal set; }
        public float MaximumRoadSocketError { get; internal set; }
        public float MaximumRoadSurfaceHeightError { get; internal set; }
    }
}
