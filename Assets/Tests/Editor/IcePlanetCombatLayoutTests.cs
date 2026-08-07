using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityPlanet.IcePlanet;

public sealed class IcePlanetCombatLayoutTests
{
    static readonly string[] MissionIds =
    {
        "abandoned_mine",
        "wind_canyon",
        "industrial_outpost"
    };

    [TestCaseSource(nameof(MissionIds))]
    public void LayoutIsDeterministicAndValid(string missionId)
    {
        IcePlanetCombatLayoutPlan first =
            IcePlanetCombatLayoutPlanner.Create(missionId, 712367);
        IcePlanetCombatLayoutPlan second =
            IcePlanetCombatLayoutPlanner.Create(missionId, 712367);

        Assert.AreEqual(first.CalculateSignature(), second.CalculateSignature());
        List<string> errors = IcePlanetCombatLayoutValidator.Validate(first);
        Assert.IsEmpty(errors, string.Join("\n", errors));
    }

    [Test]
    public void ThreeMissionsHaveDifferentStructuralSignatures()
    {
        uint mine = IcePlanetCombatLayoutPlanner
            .Create(MissionIds[0], 9001).CalculateSignature();
        uint canyon = IcePlanetCombatLayoutPlanner
            .Create(MissionIds[1], 9001).CalculateSignature();
        uint outpost = IcePlanetCombatLayoutPlanner
            .Create(MissionIds[2], 9001).CalculateSignature();

        Assert.AreNotEqual(mine, canyon);
        Assert.AreNotEqual(mine, outpost);
        Assert.AreNotEqual(canyon, outpost);
    }

    [TestCaseSource(nameof(MissionIds))]
    public void EveryMissionProvidesObjectivesSpawnsAndLowGripIce(
        string missionId)
    {
        IcePlanetCombatLayoutPlan plan =
            IcePlanetCombatLayoutPlanner.Create(missionId, 1267);
        int objectives = 0;
        int spawns = 0;
        int icePatches = 0;
        for (int index = 0; index < plan.Anchors.Count; index++)
        {
            if (plan.Anchors[index].role
                == IcePlanetCombatAnchorRole.Objective)
            {
                objectives++;
            }
            else if (plan.Anchors[index].role
                     == IcePlanetCombatAnchorRole.EnemySpawn)
            {
                spawns++;
            }
        }
        for (int index = 0; index < plan.Elements.Count; index++)
        {
            if (plan.Elements[index].role
                == IcePlanetCombatVisualRole.IcePatch)
            {
                icePatches++;
            }
        }

        Assert.AreEqual(3, objectives);
        Assert.GreaterOrEqual(spawns, 8);
        Assert.GreaterOrEqual(icePatches, 1);
    }

    [Test]
    public void DifferentSeedsCreateDifferentMineLayouts()
    {
        uint first = IcePlanetCombatLayoutPlanner
            .Create("abandoned_mine", 1).CalculateSignature();
        uint second = IcePlanetCombatLayoutPlanner
            .Create("abandoned_mine", 2).CalculateSignature();
        Assert.AreNotEqual(first, second);
    }

    [Test]
    public void AllWaterTundraUsesFrozenShelfInsteadOfFailing()
    {
        Scene preview = EditorSceneManager.NewPreviewScene();
        ProceduralPlanetPreset preset = null;
        try
        {
            var root = new GameObject("IceCenterFallbackTest");
            SceneManager.MoveGameObjectToScene(root, preview);
            var playerObject = new GameObject("Player");
            SceneManager.MoveGameObjectToScene(playerObject, preview);
            VoxelPlanetPlayerController player =
                playerObject.AddComponent<VoxelPlanetPlayerController>();

            preset = ScriptableObject.CreateInstance<ProceduralPlanetPreset>();
            preset.ApplyTemplate(ProceduralPlanetLabTemplate.Frozen);
            GalaxyPlanetDefinition definition = preset.CloneDefinition();
            definition.climate = PlanetClimate.Tundra;
            definition.lowPolyVisual.oceanEnabled = true;
            definition.lowPolyVisual.oceanLevel = 0.08f;
            definition.terrain.continentHeight = 0f;
            definition.terrain.detailHeight = 0f;
            definition.terrain.ridgeHeight = 0f;

            var terrainObject = new GameObject("Terrain");
            SceneManager.MoveGameObjectToScene(terrainObject, preview);
            terrainObject.transform.SetParent(root.transform, false);
            PlanetLabInfiniteTerrainStreamer streamer =
                terrainObject.AddComponent<
                    PlanetLabInfiniteTerrainStreamer>();
            var settings = new PlanetLabPlanarSettings
            {
                autoAnchor = false
            };
            settings.SetAnchorDirection(Vector3.up);
            streamer.Configure(
                definition,
                settings,
                playerObject.transform,
                null,
                null,
                true,
                1,
                8,
                64f);

            InfinitePlanarSurfaceWorld world =
                root.AddComponent<InfinitePlanarSurfaceWorld>();
            const BindingFlags Fields =
                BindingFlags.Instance | BindingFlags.NonPublic;
            SetPrivateField(world, "definition", definition);
            SetPrivateField(world, "player", player);
            SetPrivateField(world, "streamer", streamer);
            SetPrivateField(world, "<OceanEnabled>k__BackingField", true);

            var generatorObject = new GameObject("Generator");
            SceneManager.MoveGameObjectToScene(generatorObject, preview);
            IcePlanetCombatAreaGenerator generator =
                generatorObject.AddComponent<IcePlanetCombatAreaGenerator>();
            SetPrivateField(generator, "world", world);
            IcePlanetCombatLayoutPlan plan =
                IcePlanetCombatLayoutPlanner.Create("wind_canyon", 42);
            MethodInfo method = typeof(IcePlanetCombatAreaGenerator)
                .GetMethod("TryFindCombatAreaCenter", Fields);
            Assert.NotNull(method);
            object[] arguments =
            {
                plan,
                Vector3.forward * plan.ApproachDistance,
                Vector3.forward,
                new PlanetSurfaceSample(),
                null
            };

            bool succeeded = (bool)method.Invoke(generator, arguments);
            PlanetSurfaceSample sample =
                (PlanetSurfaceSample)arguments[3];
            string report = arguments[4] as string;
            bool usesShelf = (bool)typeof(IcePlanetCombatAreaGenerator)
                .GetField("usingFrozenShelf", Fields)
                .GetValue(generator);

            Assert.IsTrue(succeeded);
            Assert.IsTrue(usesShelf);
            Assert.IsFalse(sample.isWater);
            Assert.That(
                sample.point.y,
                Is.EqualTo(world.SeaHeight + 0.18f).Within(0.001f));
            StringAssert.Contains("dry=0", report);
            StringAssert.Contains("water=318", report);
        }
        finally
        {
            if (preset != null)
                Object.DestroyImmediate(preset);
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    static void SetPrivateField(
        object target,
        string fieldName,
        object value)
    {
        FieldInfo field = target.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field, fieldName);
        field.SetValue(target, value);
    }
}
