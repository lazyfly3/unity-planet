using System;
using UnityEngine;

[Serializable]
public struct CreatureAnatomyAssessment
{
    public float proportionScore;
    public float supportScore;
    public float attachmentScore;
    public float aestheticScore;
    public string failureReason;

    public float TotalScore => (proportionScore + supportScore + attachmentScore + aestheticScore) * .25f;
    public bool IsAcceptable => string.IsNullOrEmpty(failureReason) && TotalScore >= .72f;
}

public static class CreatureNaturalAnatomy
{
    public static CreatureAnatomyArchetype SelectArchetype(int seed, CreatureTopology topology)
    {
if (topology == CreatureTopology.Serpentine) return CreatureAnatomyArchetype.Serpent;
        if (topology == CreatureTopology.Biped) return CreatureAnatomyArchetype.Primate;
        if (topology == CreatureTopology.Hexapod) return CreatureAnatomyArchetype.Reptile;
        return (Hash(unchecked((uint)seed)) & 3u) == 0u
            ? CreatureAnatomyArchetype.Reptile : CreatureAnatomyArchetype.Beast;
    
    
}

    public static void ApplyGenerationConstraints(CreatureGenome genome)
    {
if (genome == null) return;
        genome.eyeCount = 2;
        genome.earScale = Mathf.Clamp(genome.earScale, 0f, .8f);
        genome.hornLength = Mathf.Clamp(genome.hornLength, 0f, genome.bodyHeight * .55f);
        genome.legThickness = genome.legPairCount > 0
            ? Mathf.Clamp(genome.legThickness, genome.bodyWidth * .075f, genome.bodyWidth * .22f)
            : 0f;
        genome.footScale = genome.legPairCount > 0
            ? Mathf.Clamp(genome.footScale, genome.legThickness * 1.55f, genome.bodyWidth * .48f)
            : 0f;
        genome.legSpread = Mathf.Clamp(genome.legSpread, .34f, .48f);

        switch (genome.anatomyArchetype)
        {
            case CreatureAnatomyArchetype.Primate:
                genome.tailLength = Mathf.Min(genome.tailLength, genome.bodyLength * .75f);
                genome.frontLegLength = Mathf.Clamp(genome.frontLegLength,
                    genome.bodyHeight * 1.15f, genome.bodyHeight * 2.4f);
                genome.rearLegLength = genome.frontLegLength;
                break;
            case CreatureAnatomyArchetype.Reptile:
                genome.bodyHeight = Mathf.Min(genome.bodyHeight, genome.bodyWidth * .9f);
                genome.frontLegLength = Mathf.Clamp(genome.frontLegLength,
                    genome.bodyHeight * .85f, genome.bodyHeight * 2.25f);
                genome.rearLegLength = Mathf.Clamp(genome.rearLegLength,
                    genome.frontLegLength * .9f, genome.frontLegLength * 1.1f);
                genome.tailLength = Mathf.Clamp(genome.tailLength,
                    genome.bodyLength * .45f, genome.bodyLength * .95f);
                break;
            case CreatureAnatomyArchetype.Beast:
                genome.frontLegLength = Mathf.Clamp(genome.frontLegLength,
                    genome.bodyHeight * .8f, genome.bodyHeight * 2.15f);
                genome.rearLegLength = Mathf.Clamp(genome.rearLegLength,
                    genome.frontLegLength * .88f, genome.frontLegLength * 1.12f);
                genome.tailLength = Mathf.Min(genome.tailLength, genome.bodyLength * .9f);
                break;
            case CreatureAnatomyArchetype.Serpent:
                genome.hornLength = 0f;
                genome.earScale = 0f;
                break;
        }
        genome.legLength = Mathf.Max(genome.frontLegLength, genome.rearLegLength);
    
    
}

    public static CreatureAnatomyAssessment Assess(CreatureGenome genome, CreatureBodyGraph graph)
    {
var result = new CreatureAnatomyAssessment
        {
            proportionScore = 1f,
            supportScore = 1f,
            attachmentScore = 1f,
            aestheticScore = 1f
        };
        if (genome == null || graph == null)
        {
            result.failureReason = "Missing genome or body graph.";
            return result;
        }
        string graphError;
        if (!graph.Validate(out graphError))
        {
            result.failureReason = graphError;
            return result;
        }

        float slenderness = genome.bodyLength / Mathf.Max(.1f, genome.bodyWidth);
        if (genome.anatomyArchetype != CreatureAnatomyArchetype.Serpent
            && (slenderness < .85f || slenderness > 5.5f))
            result.proportionScore -= .4f;
        if (genome.legPairCount > 0)
        {
            float legRatio = genome.legLength / Mathf.Max(.1f, genome.bodyHeight);
            if (legRatio < .65f || legRatio > 2.7f) result.proportionScore -= .35f;
            if (genome.legThickness < genome.bodyWidth * .065f) result.supportScore -= .5f;
        }
        if (graph.tentacleCount > 0) result.aestheticScore -= .55f;
        if (graph.ornamentCount > 4) result.aestheticScore -= .4f;
        if (graph.headCount > 1 || genome.eyeCount != 2) result.aestheticScore -= .45f;
        if (graph.supportLegPairCount != genome.legPairCount) result.supportScore -= .7f;
        if (graph.armPairCount > 1) result.attachmentScore -= .45f;
        for (int i = 0; i < graph.nodes.Count; i++)
        {
            CreatureBodyNode node = graph.nodes[i];
            if (!RequiresAttachedParent(node.type)) continue;
            if (node.parentIndex < 0 || node.parentIndex >= graph.nodes.Count)
            {
                result.attachmentScore = 0f;
                result.failureReason = $"Node {i} has no anatomical parent.";
                break;
            }
            CreatureBodyNode parent = graph.nodes[node.parentIndex];
            if ((node.type == CreatureBodyNodeType.UpperLeg
                    || node.type == CreatureBodyNodeType.UpperArm
                    || node.type == CreatureBodyNodeType.Horn
                    || node.type == CreatureBodyNodeType.BackPlate
                    || node.type == CreatureBodyNodeType.Sensor)
                && parent.type != CreatureBodyNodeType.Spine)
            {
                result.attachmentScore = 0f;
                result.failureReason = $"Node {i} is not rooted in the torso spine.";
                break;
            }
        }

        result.proportionScore = Mathf.Clamp01(result.proportionScore);
        result.supportScore = Mathf.Clamp01(result.supportScore);
        result.attachmentScore = Mathf.Clamp01(result.attachmentScore);
        result.aestheticScore = Mathf.Clamp01(result.aestheticScore);
        if (result.TotalScore < .72f) result.failureReason = "Natural anatomy score is below the safe threshold.";
        return result;
    
    
}

    public static bool ValidateGeneratedRange(int firstSeed, int count, out string failure)
    {
count = Mathf.Clamp(count, 1, 10000);
        for (int i = 0; i < count; i++)
        {
            int seed = unchecked(firstSeed + i);
            CreatureGenome genome = ProceduralCreatureGenerator.Generate(seed);
            CreatureAnatomyAssessment assessment = Assess(genome, genome.bodyGraph);
            if (!assessment.IsAcceptable)
            {
                failure = $"Seed {seed}: {assessment.failureReason} Score={assessment.TotalScore:F2}";
                return false;
            }
        }
        failure = null;
        return true;
    
    
}

    static bool RequiresAttachedParent(CreatureBodyNodeType type)
    {
        return type != CreatureBodyNodeType.Spine && type != CreatureBodyNodeType.Torso;
    }

    static uint Hash(uint value)
    {
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        return value ^ (value >> 16);
    }
}
