using NUnit.Framework;
using UnityEngine;

public sealed class ProceduralPlantTests
{
    ProceduralPlantSpecies species;

    [SetUp]
    public void SetUp()
    {
        species = ScriptableObject.CreateInstance<ProceduralPlantSpecies>();
        species.speciesId = "test-tree";
        species.baseSeed = 9001;
        species.family = ProceduralPlantFamily.AlienBroadleaf;
        species.parameters = new PlantGenerationParameters
        {
            age = 1f,
            trunkLength = 5f,
            trunkRadius = 0.35f,
            trunkSegments = 7,
            branchDepth = 3,
            budsPerBranch = 3,
            branchAngle = 42f,
            leafMode = PlantLeafMode.SolidLeaf,
            leafDensity = 1f
        };
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(species);
    }

    [Test]
    public void SameInputsProduceSameSkeletonAndMesh()
    {
        PlantSpeciesSnapshot a = species.CreateSnapshot(222);
        PlantSpeciesSnapshot b = species.CreateSnapshot(222);
        PlantSkeleton skeletonA = ProceduralPlantSkeletonBuilder.Build(a);
        PlantSkeleton skeletonB = ProceduralPlantSkeletonBuilder.Build(b);
        Assert.AreEqual(skeletonA.StableHash(), skeletonB.StableHash());

        ProceduralPlantMeshData meshA = ProceduralPlantMeshGenerator.Build(a, PlantMeshQuality.Lod1);
        ProceduralPlantMeshData meshB = ProceduralPlantMeshGenerator.Build(b, PlantMeshQuality.Lod1);
        Assert.AreEqual(meshA.sourceHash, meshB.sourceHash);
        Assert.AreEqual(meshA.vertices, meshB.vertices);
        Assert.AreEqual(meshA.barkTriangles, meshB.barkTriangles);
        Assert.AreEqual(meshA.foliageTriangles, meshB.foliageTriangles);
    }

    [Test]
    public void AgeGrowthDoesNotReduceHeightOrBranchCount()
    {
        float previousHeight = 0f;
        int previousBranches = 0;
        for (int i = 1; i <= 6; i++)
        {
            PlantGenerationParameters parameters = species.parameters.Clone();
            parameters.age = i / 6f;
            PlantSkeleton skeleton = ProceduralPlantSkeletonBuilder.Build(
                species.CreateSnapshot(222, parameters));
            Assert.GreaterOrEqual(skeleton.Bounds.size.y + 0.001f, previousHeight);
            Assert.GreaterOrEqual(skeleton.Branches.Count, previousBranches);
            previousHeight = skeleton.Bounds.size.y;
            previousBranches = skeleton.Branches.Count;
        }
    }

    [Test]
    public void GeneratedMeshesAreFiniteAndIndexed()
    {
        foreach (PlantLeafMode mode in new[]
                 {
                     PlantLeafMode.SolidLeaf,
                     PlantLeafMode.CardLeaf,
                     PlantLeafMode.None
                 })
        {
            PlantGenerationParameters parameters = species.parameters.Clone();
            parameters.leafMode = mode;
            foreach (PlantMeshQuality quality in System.Enum.GetValues(typeof(PlantMeshQuality)))
            {
                ProceduralPlantMeshData mesh = ProceduralPlantMeshGenerator.Build(
                    species.CreateSnapshot(731, parameters),
                    quality);
                Assert.IsTrue(mesh.IsValid(out string error), error);
                Assert.Greater(mesh.VertexCount, 0);
                Assert.Greater(mesh.barkTriangles.Length, 0);
            }
        }
    }

    [Test]
    public void VariantSelectionIsStable()
    {
        int first = ProceduralPlantFactory.GetVariantIndex(
            species, 42, "catalog", "catalog:0012", 12);
        int second = ProceduralPlantFactory.GetVariantIndex(
            species, 42, "catalog", "catalog:0012", 12);
        Assert.AreEqual(first, second);
        Assert.That(first, Is.InRange(0, 11));
    }

    [Test]
    public void SpawnSourceMustBeExclusive()
    {
        var settings = new PlanetSurfacePropSpawnSettings
        {
            proceduralPlantSpecies = species
        };
        Assert.IsTrue(settings.HasValidSource);
        settings.prefab = new GameObject("Prefab");
        Assert.IsFalse(settings.HasValidSource);
        Object.DestroyImmediate(settings.prefab);
    }
}
