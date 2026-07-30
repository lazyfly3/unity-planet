using System;
using UnityEngine;

namespace UnityPlanet.CombatMap
{
    public enum CombatTeam
    {
        Player = 0,
        Enemy = 1
    }

    public enum CombatAnchorType
    {
        PlayerSpawn = 0,
        EnemySpawn = 1,
        CentralConflict = 2,
        PlayerRetreat = 3,
        EnemyRetreat = 4,
        PowerPosition = 5,
        Landmark = 6
    }

    public enum CombatRouteType
    {
        Main = 0,
        TerrainMaskedFlank = 1,
        LongRange = 2,
        Retreat = 3
    }

    public enum CombatOccluderType
    {
        Ridge = 0,
        Tower = 1
    }

    /// <summary>
    /// A tactical volume is a piece of playable airspace. Terrain is fitted
    /// around these volumes instead of deciding the spaces after the terrain
    /// has already been generated.
    /// </summary>
    public enum CombatTacticalVolumeType
    {
        SpawnBasin = 0,
        ManeuverBowl = 1,
        OcclusionGate = 2,
        ExposureLane = 3,
        RecoveryPocket = 4
    }

    /// <summary>
    /// Deterministic height-field primitives. They are intentionally simple
    /// enough to checksum and test, but expressive enough to build broken
    /// ridges, mesas, bowls and protected flight corridors.
    /// </summary>
    public enum CombatTerrainStampType
    {
        RidgeCapsule = 0,
        MesaCapsule = 1,
        Basin = 2,
        Corridor = 3
    }

    public enum CombatMapViolationSeverity
    {
        Warning = 0,
        HardError = 1
    }

    [Serializable]
    public sealed class CombatSemanticAnchor
    {
        public string stableId = string.Empty;
        public CombatAnchorType type;
        public Vector3 position;
        public Vector3 forward = Vector3.forward;
        public float radius = 32f;
    }

    [Serializable]
    public sealed class CombatSemanticRoute
    {
        public string stableId = string.Empty;
        public CombatRouteType type;
        public float width = 120f;
        public float intendedExposure;
        public string[] controlVolumeIds = Array.Empty<string>();
        public Vector3[] waypoints = Array.Empty<Vector3>();

        public float Length
        {
            get
            {
                if (waypoints == null || waypoints.Length < 2)
                    return 0f;
                float result = 0f;
                for (int i = 1; i < waypoints.Length; i++)
                    result += Vector3.Distance(
                        waypoints[i - 1],
                        waypoints[i]);
                return result;
            }
        }
    }

    [Serializable]
    public sealed class CombatTacticalVolume
    {
        public string stableId = string.Empty;
        public CombatTacticalVolumeType type;
        public Vector3 position;
        public Vector3 size = new Vector3(160f, 120f, 160f);
        public float preferredClearance = 45f;
        // -1 = player side, 0 = neutral, 1 = enemy side.
        public int teamBias;

        public float HorizontalDiameter =>
            Mathf.Max(size.x, size.z);
    }

    [Serializable]
    public sealed class CombatTerrainStamp
    {
        public string stableId = string.Empty;
        public CombatTerrainStampType type;
        public Vector3 start;
        public Vector3 end;
        public float radius = 80f;
        public float falloff = 60f;
        public float height = 40f;
    }

    [Serializable]
    public sealed class CombatOccluderData
    {
        public string stableId = string.Empty;
        public CombatOccluderType type;
        public Vector3 position;
        public Vector3 size = Vector3.one;
        public Color color = Color.gray;
    }

    [Serializable]
    public sealed class CombatSemanticPlan
    {
        public int schemaVersion = 2;
        public int generatorVersion = 1;
        public int seed;
        public int topologyVariant;
        public Vector3 mapCenter;
        public float mapSize;
        public float warningRadius;
        public float forfeitRadius;
        public CombatSemanticAnchor[] anchors =
            Array.Empty<CombatSemanticAnchor>();
        public CombatTacticalVolume[] tacticalVolumes =
            Array.Empty<CombatTacticalVolume>();
        public CombatSemanticRoute[] routes =
            Array.Empty<CombatSemanticRoute>();
        public CombatTerrainStamp[] terrainStamps =
            Array.Empty<CombatTerrainStamp>();
        public CombatOccluderData[] occluders =
            Array.Empty<CombatOccluderData>();
        public string checksum = string.Empty;

        public CombatSemanticAnchor FindAnchor(CombatAnchorType type)
        {
            if (anchors == null)
                return null;
            for (int i = 0; i < anchors.Length; i++)
            {
                CombatSemanticAnchor anchor = anchors[i];
                if (anchor != null && anchor.type == type)
                    return anchor;
            }
            return null;
        }

        public CombatSemanticRoute FindRoute(CombatRouteType type)
        {
            if (routes == null)
                return null;
            for (int i = 0; i < routes.Length; i++)
            {
                CombatSemanticRoute route = routes[i];
                if (route != null && route.type == type)
                    return route;
            }
            return null;
        }

        public CombatTacticalVolume FindVolume(string stableId)
        {
            if (tacticalVolumes == null
                || string.IsNullOrEmpty(stableId))
            {
                return null;
            }
            for (int i = 0; i < tacticalVolumes.Length; i++)
            {
                CombatTacticalVolume volume = tacticalVolumes[i];
                if (volume != null
                    && string.Equals(
                        volume.stableId,
                        stableId,
                        StringComparison.Ordinal))
                {
                    return volume;
                }
            }
            return null;
        }
    }

    [Serializable]
    public sealed class CombatMapViolation
    {
        public string code = string.Empty;
        public CombatMapViolationSeverity severity;
        public string message = string.Empty;
        public Vector3 position;
        public string[] affectedIds = Array.Empty<string>();

        public CombatMapViolation()
        {
        }

        public CombatMapViolation(
            string valueCode,
            CombatMapViolationSeverity valueSeverity,
            string valueMessage,
            Vector3 valuePosition)
        {
            code = valueCode ?? string.Empty;
            severity = valueSeverity;
            message = valueMessage ?? string.Empty;
            position = valuePosition;
        }
    }

    [Serializable]
    public sealed class CombatMapValidationReport
    {
        public float score;
        public float teamBalanceScore;
        public float routeDiversityScore;
        public float sightlineAndCoverScore;
        public float objectivePressureScore;
        public float mobilityCompatibilityScore;
        public float spawnSafetyScore;
        public float readabilityScore;
        public float performanceScore;
        public float scaleCompatibilityScore;
        public float topologyScore;
        public float coverRhythmScore;
        public float kinematicScore;
        public float firstContactSeconds;
        public float meanOcclusionSeconds;
        public float maximumExposureSeconds;
        public float minimumTurnRadius;
        public float minimumManeuverDiameter;
        public int minimumExitCount;
        public int articulationPointCount;
        public int globalEyePointCount;
        public float routeUsageEntropy;
        public float maximumRouteDominance;
        public float[] lineOfSightBands =
            Array.Empty<float>();
        public float[] lineOfSightOpenFractions =
            Array.Empty<float>();
        public float minimumCommitScore = 80f;
        public string checksum = string.Empty;
        public CombatMapViolation[] violations =
            Array.Empty<CombatMapViolation>();

        public bool HasHardErrors
        {
            get
            {
                if (violations == null)
                    return false;
                for (int i = 0; i < violations.Length; i++)
                {
                    CombatMapViolation violation = violations[i];
                    if (violation != null
                        && violation.severity
                        == CombatMapViolationSeverity.HardError)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        public bool CanCommit =>
            !HasHardErrors && score >= minimumCommitScore;
    }

    [Serializable]
    public sealed class CombatMapGenerationResult
    {
        public int candidateIndex;
        public int derivedSeed;
        public CombatSemanticPlan plan;
        public CombatMapValidationReport validation;

        public bool CanCommit =>
            plan != null
            && validation != null
            && validation.CanCommit;
    }

    [Serializable]
    public struct CombatArenaContext
    {
        public int seed;
        public string checksum;
        public Vector3 center;
        public Vector2 size;
        public float warningRadius;
        public float forfeitRadius;
        public float forfeitSeconds;
        public float minimumGroundClearance;
        public float maximumGroundClearance;
        public Vector3 playerSpawnPosition;
        public Quaternion playerSpawnRotation;
        public Vector3 enemySpawnPosition;
        public Quaternion enemySpawnRotation;

        public bool IsValid =>
            size.x > 0f
            && size.y > 0f
            && forfeitRadius > warningRadius
            && !float.IsNaN(center.x)
            && !float.IsNaN(center.y)
            && !float.IsNaN(center.z);
    }

    /// <summary>
    /// Runtime bridge consumed by combat code. The interface deliberately
    /// contains no map-generation operations, so combat can safely use it
    /// without mutating the active arena.
    /// </summary>
    public interface IAirCombatArenaProvider
    {
        bool IsReady { get; }
        CombatArenaContext CurrentContext { get; }
        CombatMapValidationReport CurrentValidation { get; }

        bool Contains(Vector3 worldPosition);

        float SampleGroundHeight(float worldX, float worldZ);

        bool TryGetSpawnPose(
            bool player,
            out Vector3 position,
            out Quaternion rotation);

        bool TryGetAiWaypoint(
            Vector3 currentPosition,
            Vector3 targetPosition,
            out Vector3 waypoint);
    }
}
