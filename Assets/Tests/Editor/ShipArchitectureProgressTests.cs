using System.Linq;
using System.Reflection;
using ModularAssembly;
using NUnit.Framework;
using UnityEngine;
using UnityPlanet.ModularAssembly;
using UnityPlanet.SpacecraftArchitecture;
using Object = UnityEngine.Object;

public sealed class ShipArchitectureProgressTests
{
    [Test]
    public void FreshSaveStarterBlueprintContainsOnlyTheCore()
    {
        ModularBlueprintData blueprint =
            PlayerStarterModularBlueprint.Create();

        Assert.AreEqual(VehicleCoreAssistMode.Standard,
            blueprint.coreAssistMode);
        Assert.AreEqual(1, blueprint.modules.Length);
        Assert.AreEqual(GridAssemblyModel.CoreRuntimeId,
            blueprint.modules[0].runtimeId);
        Assert.AreEqual(GridAssemblyModel.CoreModuleId,
            blueprint.modules[0].moduleId);
    }

    [Test]
    public void FreshSaveUsesTighterPlayerCapacityCurve()
    {
        int[] expectedModules = { 40, 56, 80, 128, 224, 448, 1024 };
        int[] expectedCpu = { 1100, 1800, 2600, 3800, 5400, 7400, 99999 };

        Assert.AreEqual(40,
            ShipArchitectureProgressService.InitialModuleCapacity);
        Assert.AreEqual(1100,
            ShipArchitectureProgressService.InitialCpuCapacity);
        Assert.AreEqual(99999,
            ShipArchitectureProgressService.AbsoluteCpuCapacity);
        Assert.AreEqual(99999, ModuleCpuBudget.AbsoluteMaximum);
        Assert.AreEqual(expectedModules.Length,
            ShipArchitectureProgressService.TierCount);

        for (int level = 0; level < expectedModules.Length; level++)
        {
            Assert.AreEqual(
                expectedModules[level],
                ShipArchitectureProgressService.GetCapacity(
                    ShipArchitectureBranch.ModuleCapacity,
                    level));
            Assert.AreEqual(
                expectedCpu[level],
                ShipArchitectureProgressService.GetCapacity(
                    ShipArchitectureBranch.CpuCapacity,
                    level));
        }
    }

    [Test]
    public void VersionOneStarterMigrationPreservesItsEntitlement()
    {
        var data = new ShipArchitectureProgressData
        {
            formatVersion = 1,
            moduleCapacityLevel = 0,
            cpuCapacityLevel = 0,
            legacyBlueprintCapacityChecked = true
        };

        MethodInfo migrate = typeof(ShipArchitectureProgressService)
            .GetMethod(
                "MigrateFromVersionOne",
                BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(migrate);
        migrate.Invoke(null, new object[] { data });

        Assert.AreEqual(
            ShipArchitectureProgressData.CurrentFormatVersion,
            data.formatVersion);
        Assert.AreEqual(96, data.minimumModuleCapacity);
        Assert.AreEqual(2000, data.minimumCpuCapacity);
        Assert.AreEqual(2, data.moduleCapacityLevel);
        Assert.AreEqual(1, data.cpuCapacityLevel);
        Assert.IsTrue(data.legacyBlueprintCapacityChecked);

        MethodInfo findNext = typeof(ShipArchitectureProgressService)
            .GetMethod(
                "FindNextLevel",
                BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(findNext);
        Assert.AreEqual(
            3,
            findNext.Invoke(null, new object[]
            {
                ShipArchitectureBranch.ModuleCapacity,
                data.moduleCapacityLevel,
                data.minimumModuleCapacity
            }));
        Assert.AreEqual(
            2,
            findNext.Invoke(null, new object[]
            {
                ShipArchitectureBranch.CpuCapacity,
                data.cpuCapacityLevel,
                data.minimumCpuCapacity
            }));
    }

    [Test]
    public void PlayerDefaultBlueprintMatchesCurrent1231Composition()
    {
        ModularBlueprintData blueprint =
            PlayerDefaultModularBlueprint.Create();

        Assert.AreEqual(VehicleCoreAssistMode.Training,
            blueprint.coreAssistMode);
        Assert.AreEqual(29, blueprint.modules.Length);
        Assert.AreEqual(29, blueprint.modules
            .Select(module => module.runtimeId)
            .Distinct()
            .Count());
        Assert.AreEqual(1, blueprint.modules.Count(module =>
            module.moduleId == GridAssemblyModel.CoreModuleId));
        Assert.AreEqual(12, blueprint.modules.Count(module =>
            module.moduleId == "neox@block:common:block_111"));
        Assert.AreEqual(12, blueprint.modules.Count(module =>
            module.moduleId == "neox@block:common:rocket_222"));
        Assert.AreEqual(4, blueprint.modules.Count(module =>
            module.moduleId == "neox@block:core:core_energy_111"));
        Assert.LessOrEqual(
            blueprint.modules.Length,
            ShipArchitectureProgressService.InitialModuleCapacity);
    }

    [Test]
    public void PlayerDefaultBlueprintFitsFreshLimitsAndFoundationRule()
    {
        GridModuleDefinition core = Definition(
            GridAssemblyModel.CoreModuleId,
            GridModuleCategory.Core,
            new Vector3Int(2, 2, 2),
            1000f,
            0f);
        GridModuleDefinition structure = Definition(
            "neox@block:common:block_111",
            GridModuleCategory.Structure,
            Vector3Int.one,
            75f,
            0f);
        GridModuleDefinition rocket = Definition(
            "neox@block:common:rocket_222",
            GridModuleCategory.MainThruster,
            new Vector3Int(2, 2, 2),
            180f,
            450000f);
        GridModuleDefinition energyCore = Definition(
            "neox@block:core:core_energy_111",
            GridModuleCategory.Structure,
            Vector3Int.one,
            75f,
            0f);

        try
        {
            var model = new GridAssemblyModel(
                new[] { core, structure, rocket, energyCore },
                ShipArchitectureProgressService.InitialModuleCapacity,
                ShipArchitectureProgressService.InitialCpuCapacity);
            model.SetFoundationMountRule(true);

            Assert.IsTrue(
                model.RestoreBlueprint(
                    PlayerDefaultModularBlueprint.Create(),
                    out string restoreError),
                restoreError);
            GridAssemblyValidation validation = model.Validate();
            Assert.IsTrue(validation.IsValid, validation.Message);
            Assert.AreEqual(29, model.Records.Count);
            Assert.AreEqual(988, validation.CpuCost);
        }
        finally
        {
            Object.DestroyImmediate(core);
            Object.DestroyImmediate(structure);
            Object.DestroyImmediate(rocket);
            Object.DestroyImmediate(energyCore);
        }
    }

    static GridModuleDefinition Definition(
        string id,
        GridModuleCategory category,
        Vector3Int footprint,
        float mass,
        float thrust)
    {
        GridModuleDefinition definition =
            ScriptableObject.CreateInstance<GridModuleDefinition>();
        definition.Configure(
            id,
            id,
            category,
            null,
            footprint,
            mass,
            100f,
            0f,
            140f,
            thrust,
            null);
        return definition;
    }
}
