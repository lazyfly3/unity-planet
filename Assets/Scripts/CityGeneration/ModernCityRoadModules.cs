using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityGeneration
{
    public enum RoadModuleNormalizationMode
    {
        SharedKitCoordinates,
        LegacyBoundsFit
    }

    [Flags]
    public enum CityRoadConnectionMask
    {
        None = 0,
        North = 1,
        East = 2,
        South = 4,
        West = 8,
        All = North | East | South | West
    }

    public enum CityRoadModuleType
    {
        End,
        Straight,
        StraightCrossing,
        CurveSmall,
        CurveLong,
        TIntersection,
        CrossIntersection
    }

    public enum CityRoadSocketTag
    {
        ClosedA,
        Road2LaneA,
        SidewalkA
    }

    public readonly struct CityRoadSocketSet
    {
        public readonly CityRoadSocketTag North;
        public readonly CityRoadSocketTag East;
        public readonly CityRoadSocketTag South;
        public readonly CityRoadSocketTag West;

        public CityRoadSocketSet(
            CityRoadSocketTag north,
            CityRoadSocketTag east,
            CityRoadSocketTag south,
            CityRoadSocketTag west)
        {
            North = north;
            East = east;
            South = south;
            West = west;
        }

        public CityRoadConnectionMask RoadConnectionMask
        {
            get
            {
                CityRoadConnectionMask mask =
                    CityRoadConnectionMask.None;
                if (North == CityRoadSocketTag.Road2LaneA)
                    mask |= CityRoadConnectionMask.North;
                if (East == CityRoadSocketTag.Road2LaneA)
                    mask |= CityRoadConnectionMask.East;
                if (South == CityRoadSocketTag.Road2LaneA)
                    mask |= CityRoadConnectionMask.South;
                if (West == CityRoadSocketTag.Road2LaneA)
                    mask |= CityRoadConnectionMask.West;
                return mask;
            }
        }

        public CityRoadSocketTag Get(CityRoadConnectionMask side)
        {
            switch (side)
            {
                case CityRoadConnectionMask.North:
                    return North;
                case CityRoadConnectionMask.East:
                    return East;
                case CityRoadConnectionMask.South:
                    return South;
                case CityRoadConnectionMask.West:
                    return West;
                default:
                    return CityRoadSocketTag.ClosedA;
            }
        }

        public CityRoadSocketSet RotateClockwise(int quarterTurns)
        {
            int turns = ((quarterTurns % 4) + 4) % 4;
            CityRoadSocketSet result = this;
            for (int turn = 0; turn < turns; turn++)
            {
                result = new CityRoadSocketSet(
                    result.West,
                    result.North,
                    result.East,
                    result.South);
            }
            return result;
        }

        public static CityRoadSocketSet FromRoadMask(
            CityRoadConnectionMask roadMask)
        {
            return new CityRoadSocketSet(
                TagForSide(CityRoadConnectionMask.North),
                TagForSide(CityRoadConnectionMask.East),
                TagForSide(CityRoadConnectionMask.South),
                TagForSide(CityRoadConnectionMask.West));

            CityRoadSocketTag TagForSide(
                CityRoadConnectionMask side)
            {
                return (roadMask & side) != 0
                    ? CityRoadSocketTag.Road2LaneA
                    : CityRoadSocketTag.SidewalkA;
            }
        }
    }

    public readonly struct CityRoadModuleDefinition
    {
        public readonly GameObject Prefab;
        public readonly CityRoadConnectionMask CanonicalConnections;
        public readonly CityRoadConnectionMask CanonicalCrosswalkEdge;
        public readonly int FootprintCells;
        public readonly bool IsEnabled;

        public CityRoadModuleDefinition(
            GameObject prefab,
            CityRoadConnectionMask canonicalConnections,
            int footprintCells,
            bool isEnabled,
            CityRoadConnectionMask canonicalCrosswalkEdge =
                CityRoadConnectionMask.None)
        {
            Prefab = prefab;
            CanonicalConnections = canonicalConnections;
            CanonicalCrosswalkEdge = canonicalCrosswalkEdge;
            FootprintCells = Mathf.Max(1, footprintCells);
            IsEnabled = isEnabled;
        }
    }

    public sealed class CityRoadModulePlacement
    {
        public int StableIndex { get; }
        public int RegionIndex { get; }
        public int GridX { get; }
        public int GridY { get; }
        public Vector2 Position { get; }
        public CityRoadModuleType Type { get; }
        public int QuarterTurns { get; }
        public bool IsMajor { get; }
        public int FootprintCells { get; }
        public CityRoadConnectionMask ConnectionMask { get; }
        public CityRoadSocketSet SocketTags { get; }

        public CityRoadModulePlacement(
            int stableIndex,
            int regionIndex,
            int gridX,
            int gridY,
            Vector2 position,
            CityRoadModuleType type,
            int quarterTurns,
            bool isMajor,
            int footprintCells = 1,
            CityRoadConnectionMask connectionMask =
                CityRoadConnectionMask.None,
            CityRoadSocketSet? socketTags = null)
        {
            StableIndex = stableIndex;
            RegionIndex = regionIndex;
            GridX = gridX;
            GridY = gridY;
            Position = position;
            Type = type;
            QuarterTurns = ((quarterTurns % 4) + 4) % 4;
            IsMajor = isMajor;
            FootprintCells = Mathf.Max(1, footprintCells);
            ConnectionMask = connectionMask;
            SocketTags = socketTags
                ?? CityRoadSocketSet.FromRoadMask(connectionMask);
        }
    }

    public sealed class CityPathwayModulePlacement
    {
        public int StableIndex { get; }
        public int RegionIndex { get; }
        public int GridX { get; }
        public int GridY { get; }
        public Vector2 Position { get; }
        public int SizeX { get; }
        public int SizeY { get; }
        public int Variant { get; }
        public int QuarterTurns { get; }

        public CityPathwayModulePlacement(
            int stableIndex,
            int regionIndex,
            int gridX,
            int gridY,
            Vector2 position,
            int sizeX,
            int sizeY,
            int variant,
            int quarterTurns)
        {
            StableIndex = stableIndex;
            RegionIndex = regionIndex;
            GridX = gridX;
            GridY = gridY;
            Position = position;
            SizeX = Mathf.Max(1, sizeX);
            SizeY = Mathf.Max(1, sizeY);
            Variant = Mathf.Abs(variant) % 4;
            QuarterTurns = ((quarterTurns % 4) + 4) % 4;
        }
    }

    public sealed class CityRoadLayoutValidationResult
    {
        public bool IsValid { get; internal set; }
        public int ConnectedComponentCount { get; internal set; }
        public int RepairCount { get; internal set; }
        public float MaximumSocketError { get; internal set; }
        public float MaximumSurfaceHeightError { get; internal set; }
        public string Error { get; internal set; } = string.Empty;
    }

    public sealed class CityRoadGeometryCalibration
    {
        public float SourceCellSize { get; internal set; }
        public float UniformScale { get; internal set; }
        public Vector3 SharedSourceOrigin { get; internal set; }
        public int SourceVerticalAxis { get; internal set; }
        public float MaximumSocketError { get; internal set; }
        public float MaximumSurfaceHeightError { get; internal set; }
    }

    [Serializable]
    public sealed class ModernCityRoadModuleLibrary
    {
        [Header("Kit Coordinate Normalization")]
        public RoadModuleNormalizationMode normalizationMode =
            RoadModuleNormalizationMode.SharedKitCoordinates;
        [Min(0.001f)] public float socketPositionTolerance = 0.01f;
        [Min(0.001f)] public float surfaceHeightTolerance = 0.005f;
        [Min(0f)] public float maximumAutomaticOffset = 0.25f;

        [Header("Road Modules")]
        public GameObject roadStraight;
        public GameObject roadStraightCrossing;
        public GameObject roadEnd;
        public GameObject roadCurveSmall;
        public GameObject roadCurveLong;
        public GameObject roadT;
        public GameObject roadX;
        [Tooltip("Per-model clockwise quarter-turn corrections after import.")]
        public int roadStraightQuarterTurnCorrection;
        public int roadCrossingQuarterTurnCorrection;
        public int roadEndQuarterTurnCorrection;
        public int roadCurveSmallQuarterTurnCorrection;
        public int roadCurveLongQuarterTurnCorrection;
        public int roadTQuarterTurnCorrection;
        public int roadXQuarterTurnCorrection;

        [Header("Pathway Variants A-D")]
        public GameObject[] pathway1x1 = new GameObject[4];
        public GameObject[] pathway2x1 = new GameObject[4];
        public GameObject[] pathway2x2 = new GameObject[4];
        public GameObject[] pathway4x4 = new GameObject[4];

        [Header("Shared Textures")]
        public Texture2D roadAlbedo;
        public Texture2D roadNormal;
        public Texture2D pathwayAlbedo;

        public bool IsComplete(out string error)
        {
            if (roadStraight == null
                || roadStraightCrossing == null
                || roadEnd == null
                || roadCurveSmall == null
                || roadCurveLong == null
                || roadT == null
                || roadX == null)
            {
                error = "Modern City 道路模块引用不完整。";
                return false;
            }

            if (!HasFour(pathway1x1)
                || !HasFour(pathway2x1)
                || !HasFour(pathway2x2)
                || !HasFour(pathway4x4))
            {
                error = "Modern City Pathway A-D 模块引用不完整。";
                return false;
            }

            if (roadAlbedo == null
                || roadNormal == null
                || pathwayAlbedo == null)
            {
                error = "Modern City 道路 Albedo 或 Normal 贴图缺失。";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public bool TryValidateGeometry(
            float targetCellSize,
            out CityRoadGeometryCalibration calibration,
            out string error)
        {
            return CityModularRoadMeshFactory.TryValidateLibraryGeometry(
                this,
                targetCellSize,
                out calibration,
                out error);
        }

        public float SocketPositionTolerance =>
            socketPositionTolerance > 0f ? socketPositionTolerance : 0.01f;

        public float SurfaceHeightTolerance =>
            surfaceHeightTolerance > 0f ? surfaceHeightTolerance : 0.005f;

        public float MaximumAutomaticOffset =>
            maximumAutomaticOffset > 0f ? maximumAutomaticOffset : 0.25f;

        public GameObject GetRoadPrefab(CityRoadModuleType type)
        {
            switch (type)
            {
                case CityRoadModuleType.End:
                    return roadEnd;
                case CityRoadModuleType.StraightCrossing:
                    return roadStraightCrossing;
                case CityRoadModuleType.CurveSmall:
                    return roadCurveSmall;
                case CityRoadModuleType.CurveLong:
                    return roadCurveLong;
                case CityRoadModuleType.TIntersection:
                    return roadT;
                case CityRoadModuleType.CrossIntersection:
                    return roadX;
                default:
                    return roadStraight;
            }
        }

        public CityRoadModuleDefinition GetRoadDefinition(
            CityRoadModuleType type)
        {
            switch (type)
            {
                case CityRoadModuleType.End:
                    return new CityRoadModuleDefinition(
                        roadEnd,
                        CityRoadConnectionMask.North,
                        1,
                        true);
                case CityRoadModuleType.StraightCrossing:
                    return new CityRoadModuleDefinition(
                        roadStraightCrossing,
                        CityRoadConnectionMask.North
                            | CityRoadConnectionMask.South,
                        1,
                        true,
                        CityRoadConnectionMask.North);
                case CityRoadModuleType.CurveSmall:
                    return new CityRoadModuleDefinition(
                        roadCurveSmall,
                        CityRoadConnectionMask.North
                            | CityRoadConnectionMask.East,
                        1,
                        true);
                case CityRoadModuleType.CurveLong:
                    return new CityRoadModuleDefinition(
                        roadCurveLong,
                        CityRoadConnectionMask.North
                            | CityRoadConnectionMask.West,
                        2,
                        false);
                case CityRoadModuleType.TIntersection:
                    return new CityRoadModuleDefinition(
                        roadT,
                        CityRoadConnectionMask.North
                            | CityRoadConnectionMask.East
                            | CityRoadConnectionMask.South,
                        1,
                        true);
                case CityRoadModuleType.CrossIntersection:
                    return new CityRoadModuleDefinition(
                        roadX,
                        CityRoadConnectionMask.All,
                        1,
                        true);
                default:
                    return new CityRoadModuleDefinition(
                        roadStraight,
                        CityRoadConnectionMask.North
                            | CityRoadConnectionMask.South,
                        1,
                        true);
            }
        }

        public CityRoadSocketSet GetCanonicalSocketTags(
            CityRoadModuleType type)
        {
            switch (type)
            {
                case CityRoadModuleType.End:
                    return new CityRoadSocketSet(
                        CityRoadSocketTag.Road2LaneA,
                        CityRoadSocketTag.SidewalkA,
                        CityRoadSocketTag.ClosedA,
                        CityRoadSocketTag.SidewalkA);
                case CityRoadModuleType.CurveSmall:
                    return new CityRoadSocketSet(
                        CityRoadSocketTag.Road2LaneA,
                        CityRoadSocketTag.Road2LaneA,
                        CityRoadSocketTag.SidewalkA,
                        CityRoadSocketTag.SidewalkA);
                case CityRoadModuleType.CurveLong:
                    return new CityRoadSocketSet(
                        CityRoadSocketTag.Road2LaneA,
                        CityRoadSocketTag.SidewalkA,
                        CityRoadSocketTag.SidewalkA,
                        CityRoadSocketTag.Road2LaneA);
                case CityRoadModuleType.TIntersection:
                    return new CityRoadSocketSet(
                        CityRoadSocketTag.Road2LaneA,
                        CityRoadSocketTag.Road2LaneA,
                        CityRoadSocketTag.Road2LaneA,
                        CityRoadSocketTag.SidewalkA);
                case CityRoadModuleType.CrossIntersection:
                    return new CityRoadSocketSet(
                        CityRoadSocketTag.Road2LaneA,
                        CityRoadSocketTag.Road2LaneA,
                        CityRoadSocketTag.Road2LaneA,
                        CityRoadSocketTag.Road2LaneA);
                default:
                    return new CityRoadSocketSet(
                        CityRoadSocketTag.Road2LaneA,
                        CityRoadSocketTag.SidewalkA,
                        CityRoadSocketTag.Road2LaneA,
                        CityRoadSocketTag.SidewalkA);
            }
        }

        public CityRoadSocketSet GetSocketTags(
            CityRoadModuleType type,
            int clockwiseQuarterTurns)
        {
            return GetCanonicalSocketTags(type)
                .RotateClockwise(clockwiseQuarterTurns);
        }

        public static CityRoadConnectionMask Opposite(
            CityRoadConnectionMask side)
        {
            switch (side)
            {
                case CityRoadConnectionMask.North:
                    return CityRoadConnectionMask.South;
                case CityRoadConnectionMask.East:
                    return CityRoadConnectionMask.West;
                case CityRoadConnectionMask.South:
                    return CityRoadConnectionMask.North;
                case CityRoadConnectionMask.West:
                    return CityRoadConnectionMask.East;
                default:
                    return CityRoadConnectionMask.None;
            }
        }

        public static bool AreOpposingSocketsCompatible(
            CityRoadSocketTag first,
            CityRoadSocketTag second)
        {
            return first == CityRoadSocketTag.Road2LaneA
                && second == CityRoadSocketTag.Road2LaneA;
        }

        public bool TrySolveQuarterTurns(
            CityRoadModuleType type,
            CityRoadConnectionMask desiredConnections,
            CityRoadConnectionMask preferredCrosswalkEdge,
            out int quarterTurns)
        {
            CityRoadModuleDefinition definition =
                GetRoadDefinition(type);
            quarterTurns = 0;
            if (!definition.IsEnabled)
            {
                return false;
            }

            int firstConnectionMatch = -1;
            for (int turns = 0; turns < 4; turns++)
            {
                if (RotateMask(
                        definition.CanonicalConnections,
                        turns)
                    != desiredConnections)
                {
                    continue;
                }
                if (firstConnectionMatch < 0)
                    firstConnectionMatch = turns;
                if (preferredCrosswalkEdge
                        == CityRoadConnectionMask.None
                    || RotateMask(
                        definition.CanonicalCrosswalkEdge,
                        turns)
                        == preferredCrosswalkEdge)
                {
                    quarterTurns = turns;
                    return true;
                }
            }

            if (firstConnectionMatch < 0)
                return false;
            quarterTurns = firstConnectionMatch;
            return true;
        }

        public static CityRoadConnectionMask RotateMask(
            CityRoadConnectionMask value,
            int clockwiseQuarterTurns)
        {
            int turns =
                ((clockwiseQuarterTurns % 4) + 4) % 4;
            CityRoadConnectionMask rotated = value;
            for (int turn = 0; turn < turns; turn++)
            {
                CityRoadConnectionMask next =
                    CityRoadConnectionMask.None;
                if ((rotated & CityRoadConnectionMask.North) != 0)
                    next |= CityRoadConnectionMask.East;
                if ((rotated & CityRoadConnectionMask.East) != 0)
                    next |= CityRoadConnectionMask.South;
                if ((rotated & CityRoadConnectionMask.South) != 0)
                    next |= CityRoadConnectionMask.West;
                if ((rotated & CityRoadConnectionMask.West) != 0)
                    next |= CityRoadConnectionMask.North;
                rotated = next;
            }
            return rotated;
        }

        public int GetRoadQuarterTurnCorrection(CityRoadModuleType type)
        {
            switch (type)
            {
                case CityRoadModuleType.End:
                    return roadEndQuarterTurnCorrection;
                case CityRoadModuleType.StraightCrossing:
                    return roadCrossingQuarterTurnCorrection;
                case CityRoadModuleType.CurveSmall:
                    return roadCurveSmallQuarterTurnCorrection;
                case CityRoadModuleType.CurveLong:
                    return roadCurveLongQuarterTurnCorrection;
                case CityRoadModuleType.TIntersection:
                    return roadTQuarterTurnCorrection;
                case CityRoadModuleType.CrossIntersection:
                    return roadXQuarterTurnCorrection;
                default:
                    return roadStraightQuarterTurnCorrection;
            }
        }

        public GameObject GetPathwayPrefab(
            int variant,
            int sizeX,
            int sizeY,
            out int quarterTurnCorrection)
        {
            variant = Mathf.Abs(variant) % 4;
            quarterTurnCorrection = 0;
            if (sizeX >= 4 && sizeY >= 4)
                return pathway4x4[variant];
            if (sizeX >= 2 && sizeY >= 2)
                return pathway2x2[variant];
            if (sizeX >= 2 || sizeY >= 2)
            {
                quarterTurnCorrection = sizeY > sizeX ? 1 : 0;
                return pathway2x1[variant];
            }
            return pathway1x1[variant];
        }

        static bool HasFour(IReadOnlyList<GameObject> values)
        {
            if (values == null || values.Count < 4)
                return false;
            for (int i = 0; i < 4; i++)
            {
                if (values[i] == null)
                    return false;
            }
            return true;
        }
    }
}
