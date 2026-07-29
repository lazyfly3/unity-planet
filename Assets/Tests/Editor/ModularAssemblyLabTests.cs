using ModularAssembly;
using NUnit.Framework;
using UnityEngine;

public sealed class ModularAssemblyLabTests
{
    GridModuleDefinition core;
    GridModuleDefinition structure;
    GridModuleDefinition battery;
    GridModuleDefinition thruster;

    [SetUp]
    public void SetUp()
    {
        core = Definition("core", GridModuleCategory.Core, new Vector3Int(2, 2, 2), 1000f, 100f, 0f, 0f);
        structure = Definition("structure", GridModuleCategory.Structure, Vector3Int.one, 50f, 0f, 0f, 0f);
        battery = Definition("battery", GridModuleCategory.Battery, Vector3Int.one, 120f, 50f, 0f, 0f);
        thruster = Definition("main_thruster", GridModuleCategory.MainThruster, new Vector3Int(1, 1, 2), 180f, 0f, 20f, 6000f);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(core);
        Object.DestroyImmediate(structure);
        Object.DestroyImmediate(battery);
        Object.DestroyImmediate(thruster);
    }

    [Test]
    public void MultiCellRotationKeepsTwoOccupiedCells()
    {
        for (int index = 0; index < GridOrientation.Count; index++)
        {
            var cells = GridOrientation.NormalizedCells(thruster.Footprint, index);
            Assert.AreEqual(2, cells.Count);
            Assert.AreEqual(2, new System.Collections.Generic.HashSet<Vector3Int>(cells).Count);
        }
    }

    [Test]
    public void PlacementRequiresFaceConnection()
    {
        var model = Model();
        Assert.IsTrue(model.TryPlace(
            "structure",
            new GridModulePose(new Vector3Int(1, 0, 0), 0),
            false,
            out _,
            out _));
        Assert.IsFalse(model.TryPlace(
            "structure",
            new GridModulePose(new Vector3Int(10, 0, 0), 0),
            false,
            out _,
            out _));
    }

    [Test]
    public void DeletingBridgeMarksOrphanDisconnected()
    {
        var model = Model();
        model.TryPlace("structure", new GridModulePose(new Vector3Int(1, 0, 0), 0), false, out string bridge, out _);
        model.TryPlace("structure", new GridModulePose(new Vector3Int(2, 0, 0), 0), false, out _, out _);
        Assert.IsTrue(model.TryRemove(bridge, out _));
        Assert.AreEqual(1, model.Validate().DisconnectedIds.Count);
    }

    [Test]
    public void BlueprintRoundTripPreservesMetrics()
    {
        var model = Model();
        model.TryPlace("battery", new GridModulePose(new Vector3Int(1, 0, 0), 0), false, out _, out _);
        ModularBlueprintData data = model.CaptureBlueprint();
        var restored = Model();
        Assert.IsTrue(restored.RestoreBlueprint(data, out _));
        Assert.AreEqual(model.CalculateMetrics().totalMass, restored.CalculateMetrics().totalMass);
        Assert.AreEqual(model.Records.Count, restored.Records.Count);
    }

    GridAssemblyModel Model() => new GridAssemblyModel(new[] { core, structure, battery, thruster });

    static GridModuleDefinition Definition(
        string id,
        GridModuleCategory category,
        Vector3Int footprint,
        float mass,
        float capacity,
        float cost,
        float thrust)
    {
        var definition = ScriptableObject.CreateInstance<GridModuleDefinition>();
        definition.Configure(id, id, category, null, footprint, mass, capacity, cost, 100f, thrust, null);
        return definition;
    }
}
