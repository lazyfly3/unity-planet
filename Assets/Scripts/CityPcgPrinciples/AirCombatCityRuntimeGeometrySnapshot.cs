using UnityEngine;

namespace UnityPlanet.CityPcg
{
    /// <summary>
    /// Runtime-only, data-only description of the instantiated playable city.
    /// It deliberately contains no EDPCG, enemy, Boss or player references so
    /// the CityPcg assembly remains the lowest-level owner of city geometry.
    /// The snapshot is diagnostic/advisory and never participates in the
    /// AirCombatCityReport validity gate.
    /// </summary>
    [System.Serializable]
    public sealed class AirCombatCityRuntimeGeometrySnapshot
    {
        public const int CurrentSchemaVersion = 1;

        public int schemaVersion = CurrentSchemaVersion;
        public int requestedSeed;
        public int resolvedSeed;
        public int plannedBuildingCount;
        public int instantiatedBuildingCount;
        public int colliderCount;
        public int triggerColliderCount;
        public int skybridgeCount;
        public int aerialCableCount;
        public int aerialCableTriggerCount;
        public float geometryBuildMilliseconds;
        public float connectionBuildMilliseconds;
        public float presentationBuildMilliseconds;
        public float snapshotBuildMilliseconds;
        public float totalBuildMilliseconds;
        public AirCombatRuntimeBuildingGeometry[] buildings =
            System.Array.Empty<AirCombatRuntimeBuildingGeometry>();
        public AirCombatRuntimeConnectionGeometry[] skybridges =
            System.Array.Empty<AirCombatRuntimeConnectionGeometry>();
        public AirCombatRuntimeConnectionGeometry[] aerialCables =
            System.Array.Empty<AirCombatRuntimeConnectionGeometry>();
        public AirCombatRuntimeRouteGeometry[] routes =
            System.Array.Empty<AirCombatRuntimeRouteGeometry>();
        public AirCombatRuntimeIngressGeometry[] ingresses =
            System.Array.Empty<AirCombatRuntimeIngressGeometry>();

        public bool IsUsable => schemaVersion == CurrentSchemaVersion &&
                                buildings != null &&
                                instantiatedBuildingCount == buildings.Length;
    }

    [System.Serializable]
    public struct AirCombatRuntimeBuildingGeometry
    {
        public string stableId;
        public Bounds localBounds;
        public AirCombatBuildingBand band;
        public AirCombatBuildingArchetype archetype;
        public int clusterId;
        public bool destructible;
    }

    [System.Serializable]
    public struct AirCombatRuntimeConnectionGeometry
    {
        public Vector3 localStart;
        public Vector3 localEnd;
    }

    [System.Serializable]
    public struct AirCombatRuntimeRouteGeometry
    {
        public string stableId;
        public AirCombatRouteKind kind;
        public float width;
        public Vector3[] localPoints;
    }

    [System.Serializable]
    public struct AirCombatRuntimeIngressGeometry
    {
        public string stableId;
        public Vector3 localPosition;
        public Vector3 localTarget;
        public AirCombatEnemyLaneKind laneKind;
        public float warningSeconds;
    }
}
