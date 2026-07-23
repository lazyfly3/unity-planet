using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public sealed class CreatureV4GenerationTests
{
    [Test]
    public void QuadrupedV4GenerationIsDeterministic()
    {
        CreatureGenome firstGenome = ProceduralCreatureGenerator.Generate(222, CreatureTopology.Quadruped);
        CreatureGenome secondGenome = ProceduralCreatureGenerator.Generate(222, CreatureTopology.Quadruped);
        CreaturePhenotype first = CreaturePhenotypeBuilder.Build(firstGenome);
        CreaturePhenotype second = CreaturePhenotypeBuilder.Build(secondGenome);

        Assert.That(firstGenome.generatorVersion, Is.EqualTo(secondGenome.generatorVersion));
        Assert.That(firstGenome.generatorVersion, Is.GreaterThanOrEqualTo(CreaturePhenotype.CurrentVersion));
        Assert.That(firstGenome.bodyGraph.generatorVersion, Is.EqualTo(firstGenome.generatorVersion));
        Assert.That(first.shapeHash, Is.EqualTo(second.shapeHash));
        Assert.That(first.legs.Length, Is.EqualTo(4));
        Assert.That(first.nodePositions, Is.EqualTo(second.nodePositions));
        Assert.That(first.runSpeed, Is.GreaterThan(first.walkSpeed));
    }

    [Test]
    public void FiveHundredQuadrupedSeedsProduceValidPhenotypes()
    {
        for (int seed = 0; seed < 500; seed++)
        {
            CreatureGenome genome = ProceduralCreatureGenerator.Generate(seed, CreatureTopology.Quadruped);
            Assert.That(genome.bodyGraph.Validate(out string graphError), Is.True, $"Seed {seed}: {graphError}");
            CreaturePhenotype phenotype = CreaturePhenotypeBuilder.Build(genome);
            Assert.That(phenotype.legs.Length, Is.EqualTo(4), $"Seed {seed}");
            Assert.That(IsFinite(phenotype.fieldBounds.center), Is.True, $"Seed {seed}");
            Assert.That(IsFinite(phenotype.fieldBounds.size), Is.True, $"Seed {seed}");
            Assert.That(phenotype.mass, Is.InRange(2f, 180f), $"Seed {seed}");
            Assert.That(phenotype.bodyClearance, Is.GreaterThan(0.25f), $"Seed {seed}");
            Assert.That(genome.bodyLength, Is.GreaterThanOrEqualTo(genome.bodyHeight * 1.8f - 0.001f), $"Seed {seed}");
            Assert.That(genome.bodyLength, Is.GreaterThanOrEqualTo(genome.bodyWidth * 1.7f - 0.001f), $"Seed {seed}");
            float maximumLeg = genome.bodyLength * 0.66f + genome.bodyHeight * 0.3f;
            Assert.That(genome.frontLegLength, Is.LessThanOrEqualTo(maximumLeg + 0.001f), $"Seed {seed}");
            Assert.That(genome.rearLegLength, Is.LessThanOrEqualTo(maximumLeg + 0.001f), $"Seed {seed}");
            float maximumNeckFraction = phenotype.locomotionArchetype == CreatureLocomotionArchetype.CursorialUngulate
                ? 0.52f : 0.3f;
            Assert.That(genome.neckLength,
                Is.LessThanOrEqualTo(genome.bodyLength * maximumNeckFraction + 0.001f), $"Seed {seed}");
            for (int legIndex = 0; legIndex < phenotype.legs.Length; legIndex++)
            {
                CreaturePhenotypeLeg leg = phenotype.legs[legIndex];
                Assert.That(leg.upperLength, Is.GreaterThan(0.1f), $"Seed {seed}, leg {legIndex}");
                Assert.That(leg.lowerLength, Is.GreaterThan(0.1f), $"Seed {seed}, leg {legIndex}");
                Assert.That(leg.distalLength, Is.GreaterThan(0.08f), $"Seed {seed}, leg {legIndex}");
            }
        }
    }

    [Test]
    public void V4LimbWeightsDoNotCrossBetweenLegChains()
    {
        CreatureGenome genome = ProceduralCreatureGenerator.Generate(222, CreatureTopology.Quadruped);
        CreaturePhenotype phenotype = CreaturePhenotypeBuilder.Build(genome);
        var rig = new CreatureRig
        {
            nodeBoneIndices = new int[phenotype.graph.nodes.Count]
        };
        for (int i = 0; i < rig.nodeBoneIndices.Length; i++) rig.nodeBoneIndices[i] = i;

        for (int legIndex = 0; legIndex < phenotype.legs.Length; legIndex++)
        {
            CreaturePhenotypeLeg leg = phenotype.legs[legIndex];
            Vector3 upperSample = Vector3.Lerp(
                phenotype.nodePositions[leg.upperNode],
                phenotype.nodePositions[leg.lowerNode], 0.45f);
            BoneWeight upperWeight = CreatureV4SkinWeightSolver.Calculate(
                new[] { upperSample }, phenotype, rig)[0];
            Assert.That(upperWeight.boneIndex0, Is.EqualTo(leg.upperNode),
                $"Upper segment of leg {legIndex} must be driven by its hip bone.");

            Vector3 sample = Vector3.Lerp(
                phenotype.nodePositions[leg.lowerNode],
                phenotype.nodePositions[leg.distalNode], 0.45f);
            BoneWeight weight = CreatureV4SkinWeightSolver.Calculate(
                new[] { sample }, phenotype, rig)[0];
            Assert.That(weight.boneIndex0, Is.EqualTo(leg.lowerNode),
                $"Lower segment of leg {legIndex} must be driven by its knee bone.");
            AssertLimbBoneBelongsToLeg(phenotype, leg, weight.boneIndex0, weight.weight0);
            AssertLimbBoneBelongsToLeg(phenotype, leg, weight.boneIndex1, weight.weight1);
            AssertLimbBoneBelongsToLeg(phenotype, leg, weight.boneIndex2, weight.weight2);
            AssertLimbBoneBelongsToLeg(phenotype, leg, weight.boneIndex3, weight.weight3);

            Vector3 distalSample = Vector3.Lerp(
                phenotype.nodePositions[leg.distalNode],
                phenotype.nodePositions[leg.footNode], 0.18f);
            BoneWeight distalWeight = CreatureV4SkinWeightSolver.Calculate(
                new[] { distalSample }, phenotype, rig)[0];
            Assert.That(WeightForBone(distalWeight, leg.distalNode),
                Is.GreaterThan(WeightForBone(distalWeight, leg.footNode)),
                $"Distal segment of leg {legIndex} must be driven by its hock/wrist bone.");
        }
    }

    [Test]
    public void V4UsesOneFootArchetypeWithAnatomicalBendDirections()
    {
        CreatureGenome genome = ProceduralCreatureGenerator.Generate(222, CreatureTopology.Quadruped);
        CreaturePhenotype phenotype = CreaturePhenotypeBuilder.Build(genome);
        Vector3? footSize = null;

        for (int i = 0; i < phenotype.legs.Length; i++)
        {
            CreaturePhenotypeLeg leg = phenotype.legs[i];
            Vector3 currentFootSize = phenotype.graph.nodes[leg.footNode].size;
            if (footSize.HasValue)
                Assert.That(Vector3.Distance(currentFootSize, footSize.Value), Is.LessThan(0.0001f));
            else
                footSize = currentFootSize;

            float bendDirection = Vector3.Dot(leg.bendHintLocal, Vector3.forward);
            Assert.That(bendDirection, leg.IsFront ? Is.LessThan(0f) : Is.GreaterThan(0f),
                $"Leg {i} has the wrong anatomical bend direction.");
        }
    }

    [Test]
    public void BodyStyleTwoUsesUngulateMorphologyInsteadOfCanidFeet()
    {
        CreatureGenome genome = ProceduralCreatureGenerator.Generate(222, CreatureTopology.Quadruped);
        genome.bodyStyle = 2;

        CreaturePhenotype phenotype = CreaturePhenotypeBuilder.Build(genome);

        Assert.AreEqual(CreatureLocomotionArchetype.CursorialUngulate, phenotype.locomotionArchetype);
        Assert.AreEqual(4, phenotype.legs.Length);
        foreach (CreaturePhenotypeLeg leg in phenotype.legs)
        {
            CreatureBodyNode foot = phenotype.graph.nodes[leg.footNode];
            Assert.Less(foot.size.x, foot.size.z,
                "Ungulate feet should be narrow, forward-pointing hooves.");
            Assert.Greater(leg.distalLength, leg.upperLength * 0.9f,
                "Ungulate distal limbs should stay long instead of using canid proportions.");
        }
    }

    [Test]
    public void UnifiedV4MeshIsClosedAndFinite()
    {
        CreatureGenome genome = ProceduralCreatureGenerator.Generate(222, CreatureTopology.Quadruped);
        CreaturePhenotype phenotype = CreaturePhenotypeBuilder.Build(genome);
        CreatureImplicitMeshData mesh = CreatureImplicitBodyMesher.Build(
            phenotype, CreatureBodyMeshQuality.Lod2);

        Assert.That(mesh.vertices.Length, Is.GreaterThan(100));
        Assert.That(mesh.triangles.Length, Is.GreaterThan(300));
        Assert.That(mesh.triangles.Length % 3, Is.Zero);
        for (int i = 0; i < mesh.vertices.Length; i++)
        {
            Assert.That(IsFinite(mesh.vertices[i]), Is.True, $"Vertex {i}");
            Assert.That(IsFinite(mesh.normals[i]), Is.True, $"Normal {i}");
            Assert.That(mesh.normals[i].sqrMagnitude, Is.GreaterThan(0.5f), $"Normal {i}");
        }

        var edgeUses = new Dictionary<ulong, int>(mesh.triangles.Length);
        for (int i = 0; i < mesh.triangles.Length; i += 3)
        {
            AddEdge(edgeUses, mesh.triangles[i], mesh.triangles[i + 1]);
            AddEdge(edgeUses, mesh.triangles[i + 1], mesh.triangles[i + 2]);
            AddEdge(edgeUses, mesh.triangles[i + 2], mesh.triangles[i]);
        }
        foreach (KeyValuePair<ulong, int> edge in edgeUses)
            Assert.That(edge.Value, Is.EqualTo(2), $"Open/non-manifold edge {edge.Key}");
    }

    static void AddEdge(IDictionary<ulong, int> edges, int a, int b)
    {
        uint minimum = (uint)Mathf.Min(a, b);
        uint maximum = (uint)Mathf.Max(a, b);
        ulong key = ((ulong)minimum << 32) | maximum;
        edges.TryGetValue(key, out int count);
        edges[key] = count + 1;
    }

    static void AssertLimbBoneBelongsToLeg(
        CreaturePhenotype phenotype,
        CreaturePhenotypeLeg expectedLeg,
        int nodeIndex,
        float weight)
    {
        if (weight <= 0.0001f) return;
        CreatureBodyNode node = phenotype.graph.nodes[nodeIndex];
        bool limb = node.type == CreatureBodyNodeType.UpperLeg
            || node.type == CreatureBodyNodeType.LowerLeg
            || node.type == CreatureBodyNodeType.DistalLeg
            || node.type == CreatureBodyNodeType.Foot;
        if (!limb) return;
        Assert.That(nodeIndex == expectedLeg.upperNode
            || nodeIndex == expectedLeg.lowerNode
            || nodeIndex == expectedLeg.distalNode
            || nodeIndex == expectedLeg.footNode, Is.True,
            $"Leg {expectedLeg.side}/{expectedLeg.longitudinalPosition:F2} received weight from node {nodeIndex}.");
    }

    static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    static float WeightForBone(BoneWeight weight, int boneIndex)
    {
        float result = 0f;
        if (weight.boneIndex0 == boneIndex) result += weight.weight0;
        if (weight.boneIndex1 == boneIndex) result += weight.weight1;
        if (weight.boneIndex2 == boneIndex) result += weight.weight2;
        if (weight.boneIndex3 == boneIndex) result += weight.weight3;
        return result;
    }

    static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
