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
        static bool initialAssembly;
        static ModularBlueprintData pendingSpaceBlueprint;
        static ModularBlueprintData activeExpeditionBlueprint;
        static SpaceStationSpawnLocation pendingStationSpawn;

        public static bool CanReturnToStationFromAssembly =>
            assemblyOpenedFromStation;

        public static bool MustSaveInitialAssembly =>
            initialAssembly;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlaySession()
        {
            assemblyOpenedFromStation = false;
            initialAssembly = false;
            pendingSpaceBlueprint = null;
            activeExpeditionBlueprint = null;
            pendingStationSpawn =
                SpaceStationSpawnLocation.Floor01Room;
        }

        public static SpaceStationSpawnLocation EnterStation()
        {
            SpaceStationSpawnLocation spawn = pendingStationSpawn;
            assemblyOpenedFromStation = false;
            initialAssembly = false;
            pendingSpaceBlueprint = null;
            activeExpeditionBlueprint = null;
            pendingStationSpawn =
                SpaceStationSpawnLocation.Floor01Room;
            return spawn;
        }

        public static void BeginAssemblyFromStation()
        {
            assemblyOpenedFromStation = true;
            initialAssembly = false;
            pendingSpaceBlueprint = null;
            activeExpeditionBlueprint = null;
        }

        public static void BeginInitialAssembly()
        {
            assemblyOpenedFromStation = true;
            initialAssembly = true;
            pendingSpaceBlueprint = null;
            activeExpeditionBlueprint = null;
        }

        public static void CompleteAssemblyReturn()
        {
            CompleteAssemblyReturn(
                SpaceStationSpawnLocation.DockingBay);
        }

        public static void CompleteInitialAssemblyReturn()
        {
            CompleteAssemblyReturn(
                SpaceStationSpawnLocation.Floor01Room);
        }

        static void CompleteAssemblyReturn(
            SpaceStationSpawnLocation spawnLocation)
        {
            assemblyOpenedFromStation = false;
            initialAssembly = false;
            pendingSpaceBlueprint = null;
            activeExpeditionBlueprint = null;
            pendingStationSpawn = spawnLocation;
        }

        public static void PrepareSpaceLaunch(
            ModularBlueprintData blueprint)
        {
            pendingSpaceBlueprint = Clone(blueprint);
            activeExpeditionBlueprint = Clone(blueprint);
            assemblyOpenedFromStation = false;
            initialAssembly = false;
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

        public static bool TryGetActiveExpeditionBlueprint(
            out ModularBlueprintData blueprint)
        {
            blueprint = Clone(activeExpeditionBlueprint);
            return blueprint != null;
        }

        public static void PrepareOrbitalReturnToStation()
        {
            assemblyOpenedFromStation = false;
            initialAssembly = false;
            pendingSpaceBlueprint = null;
            activeExpeditionBlueprint = null;
            pendingStationSpawn =
                SpaceStationSpawnLocation.DockingBay;
        }

        public static void CancelPendingSpaceLaunch()
        {
            pendingSpaceBlueprint = null;
            activeExpeditionBlueprint = null;
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
