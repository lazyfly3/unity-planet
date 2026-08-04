using ModularAssembly;
using UnityEngine;

namespace UnityPlanet.SpaceStation
{
    public enum SpaceStationSpawnLocation
    {
        Floor01Room,
        DockingBay
    }

    public static class SpaceStationFlowContext
    {
        static bool assemblyOpenedFromStation;
        static ModularBlueprintData pendingSpaceBlueprint;
        static SpaceStationSpawnLocation pendingStationSpawn;

        public static bool CanReturnToStationFromAssembly =>
            assemblyOpenedFromStation;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlaySession()
        {
            assemblyOpenedFromStation = false;
            pendingSpaceBlueprint = null;
            pendingStationSpawn =
                SpaceStationSpawnLocation.Floor01Room;
        }

        public static SpaceStationSpawnLocation EnterStation()
        {
            SpaceStationSpawnLocation spawn = pendingStationSpawn;
            assemblyOpenedFromStation = false;
            pendingSpaceBlueprint = null;
            pendingStationSpawn =
                SpaceStationSpawnLocation.Floor01Room;
            return spawn;
        }

        public static void BeginAssemblyFromStation()
        {
            assemblyOpenedFromStation = true;
            pendingSpaceBlueprint = null;
        }

        public static void CompleteAssemblyReturn()
        {
            assemblyOpenedFromStation = false;
            pendingSpaceBlueprint = null;
            pendingStationSpawn =
                SpaceStationSpawnLocation.DockingBay;
        }

        public static void PrepareSpaceLaunch(
            ModularBlueprintData blueprint)
        {
            pendingSpaceBlueprint = Clone(blueprint);
            assemblyOpenedFromStation = false;
            pendingStationSpawn =
                SpaceStationSpawnLocation.Floor01Room;
        }

        public static bool TryTakePendingSpaceBlueprint(
            out ModularBlueprintData blueprint)
        {
            blueprint = pendingSpaceBlueprint;
            pendingSpaceBlueprint = null;
            return blueprint != null;
        }

        public static void CancelPendingSpaceLaunch()
        {
            pendingSpaceBlueprint = null;
        }

        static ModularBlueprintData Clone(
            ModularBlueprintData blueprint)
        {
            if (blueprint == null)
            {
                return null;
            }

            return JsonUtility.FromJson<ModularBlueprintData>(
                JsonUtility.ToJson(blueprint));
        }
    }
}
