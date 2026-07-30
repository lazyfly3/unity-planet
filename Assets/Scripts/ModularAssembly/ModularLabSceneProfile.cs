using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityPlanet.ModularAssembly
{
    /// <summary>
    /// Declares which Modular Lab runtime extensions are valid for a scene.
    /// Scene-name fallback keeps the original ModularAssemblyLab compatible.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ModularLabSceneProfile : MonoBehaviour
    {
        [SerializeField] bool enableBuildExperience = true;
        [SerializeField] bool enableCombatTest = true;
        
        [SerializeField] bool enableCombatMapFlightEnvironment;
[SerializeField] bool enablePlanetLabFlightEnvironment = true;

        public bool EnableBuildExperience => enableBuildExperience;
        public bool EnableCombatTest => enableCombatTest;
        
        public bool EnableCombatMapFlightEnvironment =>
            enableCombatMapFlightEnvironment;
public bool EnablePlanetLabFlightEnvironment =>
            enablePlanetLabFlightEnvironment;

public void Configure(
            bool buildExperience,
            bool combatTest,
            bool planetLabFlightEnvironment,
            bool combatMapFlightEnvironment = false)
        {
            enableBuildExperience = buildExperience;
            enableCombatTest = combatTest;
            enablePlanetLabFlightEnvironment =
                planetLabFlightEnvironment;
            enableCombatMapFlightEnvironment =
                combatMapFlightEnvironment;
        }

        public static ModularLabSceneProfile Find(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return null;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int index = 0; index < roots.Length; index++)
            {
                ModularLabSceneProfile profile =
                    roots[index].GetComponentInChildren<
                        ModularLabSceneProfile>(true);
                if (profile != null)
                    return profile;
            }
            return null;
        }

        public static bool AllowsBuildExperience(Scene scene)
        {
            ModularLabSceneProfile profile = Find(scene);
            return profile != null
                ? profile.EnableBuildExperience
                : IsLegacyLab(scene);
        }

        public static bool AllowsCombatTest(Scene scene)
        {
            ModularLabSceneProfile profile = Find(scene);
            return profile != null
                ? profile.EnableCombatTest
                : IsLegacyLab(scene);
        }

        public static bool AllowsPlanetLabFlightEnvironment(
            Scene scene)
        {
            ModularLabSceneProfile profile = Find(scene);
            return profile != null
                ? profile.EnablePlanetLabFlightEnvironment
                : IsLegacyLab(scene);
        }

public static bool AllowsCombatMapFlightEnvironment(
            Scene scene)
        {
            ModularLabSceneProfile profile = Find(scene);
            return profile != null
                && profile.EnableCombatMapFlightEnvironment;
        }


        static bool IsLegacyLab(Scene scene)
        {
            return scene.IsValid() &&
                   string.Equals(
                       scene.name,
                       "ModularAssemblyLab",
                       StringComparison.OrdinalIgnoreCase);
        }
    }
}
