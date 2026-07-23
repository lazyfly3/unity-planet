using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public sealed class CreatureV5GenerationTests
{
    [Test]
    public void Seed222UsesAnatomicalV5Ungulate()
    {
        CreatureGenome genome = ProceduralCreatureGenerator.Generate(222, CreatureTopology.Quadruped);
        Assert.AreEqual(CreatureGenerationVersions.AnatomicalV5, genome.generatorVersion);
        Assert.AreEqual(CreatureLocomotionArchetype.CursorialUngulate, genome.locomotionArchetype);
        Assert.IsTrue(CreatureV5PhenotypeBuilder.Supports(genome));
        Assert.NotNull(genome.v5Parameters);
        Assert.AreEqual(CreatureV5MeshSchema.Current, genome.v5Parameters.schemaVersion);
    }

    [Test]
    public void V5MeshGenerationIsDeterministic()
    {
        CreatureGenome firstGenome = ProceduralCreatureGenerator.Generate(222, CreatureTopology.Quadruped);
        CreatureGenome secondGenome = ProceduralCreatureGenerator.Generate(222, CreatureTopology.Quadruped);
        CreatureV5Phenotype firstPhenotype = CreatureV5PhenotypeBuilder.Build(firstGenome);
        CreatureV5Phenotype secondPhenotype = CreatureV5PhenotypeBuilder.Build(secondGenome);
        CreatureV5MeshData first = CreatureV5MeshGenerator.Build(firstPhenotype, CreatureBodyMeshQuality.Lod2, 11);
        CreatureV5MeshData second = CreatureV5MeshGenerator.Build(secondPhenotype, CreatureBodyMeshQuality.Lod2, 12);

        Assert.AreEqual(firstPhenotype.shapeHash, secondPhenotype.shapeHash);
        Assert.AreEqual(first.vertices.Length, second.vertices.Length);
        Assert.AreEqual(first.triangles.Length, second.triangles.Length);
        for (int i = 0; i < first.vertices.Length; i++)
            Assert.Less(Vector3.SqrMagnitude(first.vertices[i] - second.vertices[i]), 0.0000000001f, $"Vertex {i}");
        for (int i = 0; i < first.triangles.Length; i++)
            Assert.AreEqual(first.triangles[i], second.triangles[i], $"Index {i}");
    }

    [Test]
    public void V5ShapeHashIncludesEverySemanticEditorParameter()
    {
        CreatureGenome baselineGenome = ProceduralCreatureGenerator.Generate(222, CreatureTopology.Quadruped);
        int baseline = CreatureV5PhenotypeBuilder.Build(baselineGenome).shapeHash;
        var changes = new System.Action<CreatureV5EditableParameters>[]
        {
            p => p.bodyLength = DifferentValue(p.bodyLength, 2.8f, 6.2f),
            p => p.chestWidth = DifferentValue(p.chestWidth, p.bodyLength * 0.18f, p.bodyLength * 0.42f),
            p => p.chestDepth = DifferentValue(p.chestDepth, p.bodyLength * 0.2f, p.bodyLength * 0.42f),
            p => p.waistTuck = DifferentValue(p.waistTuck, 0.52f, 0.88f),
            p => p.pelvisWidth = DifferentValue(p.pelvisWidth, p.chestWidth * 0.68f, p.chestWidth * 1.02f),
            p => p.pelvisDepth = DifferentValue(p.pelvisDepth, p.chestDepth * 0.68f, p.chestDepth * 1.02f),
            p => p.legLength = DifferentValue(p.legLength, p.chestDepth * 1.35f, p.bodyLength * 0.95f),
            p => p.legThickness = DifferentValue(p.legThickness, p.chestWidth * 0.07f, p.chestWidth * 0.135f),
            p => p.distalLegFraction = DifferentValue(p.distalLegFraction, 0.3f, 0.42f),
            p => p.neckLength = DifferentValue(p.neckLength, p.chestDepth * 0.65f, p.bodyLength * 0.42f),
            p => p.neckAngle = DifferentValue(p.neckAngle, 18f, 48f),
            p => p.headLength = DifferentValue(p.headLength, p.chestWidth * 0.42f, p.bodyLength * 0.35f),
            p => p.headWidth = DifferentValue(p.headWidth, p.chestWidth * 0.26f, p.chestWidth * 0.52f),
            p => p.headHeight = DifferentValue(p.headHeight, p.headWidth * 0.72f, p.headWidth * 1.3f),
            p => p.hoofLength = DifferentValue(p.hoofLength, p.legThickness * 1.4f, p.legThickness * 2.6f),
            p => p.hoofWidth = DifferentValue(p.hoofWidth, p.legThickness * 0.85f, p.hoofLength * 0.72f),
            p => p.bellyRise = DifferentValue(p.bellyRise, 0.1f, 0.28f),
            p => p.neckRootScale = DifferentValue(p.neckRootScale, 0.82f, 1.2f),
            p => p.muzzleLengthRatio = DifferentValue(p.muzzleLengthRatio, 0.45f, 0.7f),
            p => p.earLengthRatio = DifferentValue(p.earLengthRatio, 0.7f, 1.1f),
            p => p.earOutwardAngle = DifferentValue(p.earOutwardAngle, 15f, 42f)
        };

        for (int i = 0; i < changes.Length; i++)
        {
            CreatureGenome genome = ProceduralCreatureGenerator.Generate(222, CreatureTopology.Quadruped);
            changes[i](genome.v5Parameters);
            int changed = CreatureV5PhenotypeBuilder.Build(genome).shapeHash;
            Assert.AreNotEqual(baseline, changed, $"Semantic parameter {i} did not affect the shape hash");
        }
    }

    [Test]
    public void FiveHundredV5UngulatesProduceValidBaseMeshes()
    {
        int generated = 0;
        for (int seed = 0; seed < 5000 && generated < 500; seed++)
        {
            CreatureGenome genome = ProceduralCreatureGenerator.Generate(seed, CreatureTopology.Quadruped);
            if (!CreatureV5PhenotypeBuilder.Supports(genome)) continue;
            CreatureV5Phenotype phenotype = CreatureV5PhenotypeBuilder.Build(genome);
            CreatureV5MeshData mesh = CreatureV5MeshGenerator.Build(
                phenotype, CreatureBodyMeshQuality.Lod2, generated + 1);
            Assert.IsTrue(mesh.Validate(out string error), $"Seed {seed}: {error}");
            Assert.AreEqual(4, phenotype.motion.legs.Length, $"Seed {seed}");
            Assert.AreEqual(CreatureGenerationVersions.AnatomicalV5, genome.generatorVersion, $"Seed {seed}");
            generated++;
        }
        Assert.AreEqual(500, generated);
    }

    [Test]
    public void V5LodsMeetBudgetsAndRemainClosedManifolds()
    {
        CreatureGenome genome = ProceduralCreatureGenerator.Generate(222, CreatureTopology.Quadruped);
        CreatureV5Phenotype phenotype = CreatureV5PhenotypeBuilder.Build(genome);
        CreatureV5MeshData lod0 = CreatureV5MeshGenerator.Build(phenotype, CreatureBodyMeshQuality.Final, 21);
        CreatureV5MeshData lod1 = CreatureV5MeshGenerator.Build(phenotype, CreatureBodyMeshQuality.Lod1, 22);
        CreatureV5MeshData lod2 = CreatureV5MeshGenerator.Build(phenotype, CreatureBodyMeshQuality.Lod2, 23);

        Assert.That(lod0.vertices.Length, Is.InRange(15000, 25000));
        Assert.That(lod1.vertices.Length, Is.InRange(6000, 10000));
        Assert.That(lod2.vertices.Length, Is.InRange(1200, 3000));
        AssertClosed(lod0);
        AssertClosed(lod1);
        AssertClosed(lod2);
        Assert.AreEqual(9, lod2.diagnostics.connectedShellCount);
        Assert.AreEqual(0, lod2.diagnostics.boundaryEdgeCount);
        Assert.AreEqual(0, lod2.diagnostics.nonManifoldEdgeCount);
        Assert.AreEqual(0, lod2.diagnostics.inconsistentWindingEdgeCount);
        Assert.AreEqual(0, lod2.diagnostics.degenerateTriangleCount);
        Assert.AreEqual(0, lod2.diagnostics.invalidNormalCount);
        Assert.Greater(lod2.diagnostics.signedVolume, 0f);
    }

    [Test]
    public void V5LogicalWeightsAreNormalizedAndStayOnTheirLegChains()
    {
        CreatureGenome genome = ProceduralCreatureGenerator.Generate(222, CreatureTopology.Quadruped);
        CreatureV5Phenotype phenotype = CreatureV5PhenotypeBuilder.Build(genome);
        CreatureV5MeshData mesh = CreatureV5MeshGenerator.Build(phenotype, CreatureBodyMeshQuality.Lod2, 31);
        int[] nodeLegOwner = BuildNodeLegOwners(phenotype);
        for (int i = 0; i < mesh.logicalWeights.Length; i++)
        {
            CreatureV5LogicalWeight weight = mesh.logicalWeights[i];
            float sum = weight.weight0 + weight.weight1 + weight.weight2 + weight.weight3;
            Assert.AreEqual(1f, sum, 0.001f, $"Vertex {i}");
            AssertNodeValid(weight.node0, weight.weight0, phenotype.motion.graph.nodes.Count, i);
            AssertNodeValid(weight.node1, weight.weight1, phenotype.motion.graph.nodes.Count, i);
            AssertNodeValid(weight.node2, weight.weight2, phenotype.motion.graph.nodes.Count, i);
            AssertNodeValid(weight.node3, weight.weight3, phenotype.motion.graph.nodes.Count, i);
            AssertSingleLegChain(weight, nodeLegOwner, i);
        }
    }

    [Test]
    public void V5UsesPairedClosedHoofShellsWithSharedFootType()
    {
        CreatureGenome genome = ProceduralCreatureGenerator.Generate(222, CreatureTopology.Quadruped);
        CreatureV5Phenotype phenotype = CreatureV5PhenotypeBuilder.Build(genome);
        Vector3? firstSize = null;
        foreach (CreaturePhenotypeLeg leg in phenotype.motion.legs)
        {
            Vector3 size = phenotype.motion.graph.nodes[leg.footNode].size;
            Assert.Less(size.x, size.z);
            if (firstSize.HasValue)
            {
                Assert.Less(Mathf.Abs(size.y - firstSize.Value.y), 0.001f);
                Assert.Less(Mathf.Abs(size.z - firstSize.Value.z), 0.001f);
                Assert.Less(size.x / firstSize.Value.x, 1.081f);
                Assert.Greater(size.x / firstSize.Value.x, 0.925f);
            }
            else firstSize = size;
        }
    }

    [Test]
    public void V51AdultDoeProfileHasChestWaistPelvisAndSemanticSockets()
    {
        CreatureGenome genome = ProceduralCreatureGenerator.Generate(222, CreatureTopology.Quadruped);
        CreatureV5Phenotype phenotype = CreatureV5PhenotypeBuilder.Build(genome);
        CreatureV5TorsoSection pelvis = CreatureV5PhenotypeBuilder.SampleTorsoSection(
            phenotype.torsoSections, 0.22f);
        CreatureV5TorsoSection waist = CreatureV5PhenotypeBuilder.SampleTorsoSection(
            phenotype.torsoSections, 0.5f);
        CreatureV5TorsoSection chest = CreatureV5PhenotypeBuilder.SampleTorsoSection(
            phenotype.torsoSections, 0.79f);

        Assert.Greater(chest.width, waist.width);
        Assert.Greater(chest.Height, waist.Height);
        Assert.Greater(pelvis.width, waist.width);
        Assert.Greater((waist.centerY - waist.bottomRadius)
            - (chest.centerY - chest.bottomRadius), genome.v5Parameters.chestDepth * 0.1f);
        Assert.GreaterOrEqual(phenotype.headProfile.skullWidth,
            phenotype.headProfile.upperNeckWidth * 1.15f - 0.0001f);
        Assert.That(phenotype.earProfile.outwardAngle, Is.InRange(15f, 42f));

        for (int i = 0; i < phenotype.legSockets.Length; i++)
        {
            CreatureV5SocketDescriptor socket = phenotype.legSockets[i];
            Assert.That(socket.longitudinalPosition,
                phenotype.motion.legs[i].IsFront ? Is.InRange(0.8f, 0.86f) : Is.InRange(0.16f, 0.23f));
            Assert.AreEqual(3, socket.axialSpan);
            Assert.AreEqual(3, socket.circumferentialSpan);
        }
    }

    [Test]
    public void V51MigratesLegacyEditableParametersWithoutChangingGeneratorVersion()
    {
        CreatureGenome genome = ProceduralCreatureGenerator.Generate(222, CreatureTopology.Quadruped);
        CreatureV5EditableParameters parameters = genome.v5Parameters;
        parameters.schemaVersion = 0;
        parameters.bellyRise = 0f;
        parameters.neckRootScale = 0f;
        parameters.muzzleLengthRatio = 0f;
        parameters.earLengthRatio = 0f;
        parameters.earOutwardAngle = 0f;

        CreatureV5PhenotypeBuilder.Build(genome);

        Assert.AreEqual(CreatureGenerationVersions.AnatomicalV5, genome.generatorVersion);
        Assert.AreEqual(CreatureV5MeshSchema.Current, parameters.schemaVersion);
        Assert.AreEqual(0.19f, parameters.bellyRise, 0.0001f);
        Assert.AreEqual(1f, parameters.neckRootScale, 0.0001f);
        Assert.AreEqual(0.58f, parameters.muzzleLengthRatio, 0.0001f);
        Assert.AreEqual(0.92f, parameters.earLengthRatio, 0.0001f);
        Assert.AreEqual(28f, parameters.earOutwardAngle, 0.0001f);
    }

    static void AssertClosed(CreatureV5MeshData mesh)
    {
        var edges = new Dictionary<ulong, int>(mesh.triangles.Length);
        for (int i = 0; i < mesh.triangles.Length; i += 3)
        {
            AddEdge(edges, mesh.triangles[i], mesh.triangles[i + 1]);
            AddEdge(edges, mesh.triangles[i + 1], mesh.triangles[i + 2]);
            AddEdge(edges, mesh.triangles[i + 2], mesh.triangles[i]);
        }
        foreach (KeyValuePair<ulong, int> edge in edges)
            Assert.AreEqual(2, edge.Value, $"Non-manifold edge {edge.Key}");
    }

    static void AddEdge(IDictionary<ulong, int> edges, int a, int b)
    {
        uint minimum = (uint)Mathf.Min(a, b);
        uint maximum = (uint)Mathf.Max(a, b);
        ulong key = ((ulong)minimum << 32) | maximum;
        edges.TryGetValue(key, out int count);
        edges[key] = count + 1;
    }

    static void AssertNodeValid(int node, float weight, int count, int vertex)
    {
        if (weight <= 0.0001f) return;
        Assert.That(node, Is.InRange(0, count - 1), $"Vertex {vertex}");
    }

    static int[] BuildNodeLegOwners(CreatureV5Phenotype phenotype)
    {
        var owners = new int[phenotype.motion.graph.nodes.Count];
        for (int i = 0; i < owners.Length; i++) owners[i] = -1;
        for (int legIndex = 0; legIndex < phenotype.motion.legs.Length; legIndex++)
        {
            CreaturePhenotypeLeg leg = phenotype.motion.legs[legIndex];
            owners[leg.upperNode] = legIndex;
            owners[leg.lowerNode] = legIndex;
            owners[leg.distalNode] = legIndex;
            owners[leg.footNode] = legIndex;
        }
        return owners;
    }

    static void AssertSingleLegChain(CreatureV5LogicalWeight weight, int[] owners, int vertex)
    {
        int owner = -1;
        AssertOwner(weight.node0, weight.weight0, owners, ref owner, vertex);
        AssertOwner(weight.node1, weight.weight1, owners, ref owner, vertex);
        AssertOwner(weight.node2, weight.weight2, owners, ref owner, vertex);
        AssertOwner(weight.node3, weight.weight3, owners, ref owner, vertex);
    }

    static void AssertOwner(int node, float weight, int[] owners, ref int owner, int vertex)
    {
        if (weight <= 0.0001f || node < 0 || owners[node] < 0) return;
        if (owner < 0) owner = owners[node];
        else Assert.AreEqual(owner, owners[node], $"Vertex {vertex} crosses leg chains");
    }

    static float DifferentValue(float current, float minimum, float maximum)
    {
        float midpoint = (minimum + maximum) * 0.5f;
        return current < midpoint
            ? Mathf.Lerp(midpoint, maximum, 0.75f)
            : Mathf.Lerp(minimum, midpoint, 0.25f);
    }
}
