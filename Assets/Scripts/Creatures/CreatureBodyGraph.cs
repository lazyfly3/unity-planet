using System;
using System.Collections.Generic;
using UnityEngine;

public enum CreatureBodyNodeType
{
    Spine,
    Torso,
    Neck,
    Head,
    UpperLeg,
    LowerLeg,
    Foot,
    UpperArm,
    LowerArm,
    Hand,
    Tail,
    Tentacle,
    Horn,
    BackPlate,
    Sensor
}

public enum CreatureBodySide
{
    Center,
    Left,
    Right
}

public enum CreatureSocketType
{
    Core,
    Front,
    Back,
    Side,
    Top,
    Bottom
}

[Serializable]
public sealed class CreatureDesignLanguage
{
    public float massDistribution;
    public float bodyCurve;
    public float taper;
    public float limbAngularStyle;
    public float headBodyRatio;
    public float ornamentDensity;
    public float asymmetry;
    public float patternFrequency;
    public Color primaryColor;
    public Color secondaryColor;
    public Color bellyColor;
    public Color ornamentColor;
}

[Serializable]
public sealed class CreatureBodyNode
{
    public int id;
    public int parentIndex = -1;
    public CreatureBodyNodeType type;
    public CreatureBodySide side;
    public CreatureSocketType socket;
    public int symmetryGroup;
    public int chainIndex;
    public int gaitGroup;
    public float longitudinalPosition;
    public Vector3 localPosition;
    public Vector3 localEulerAngles;
    public Vector3 size = Vector3.one;
    public float radius = 0.25f;
    public float animationPhase;
}

[Serializable]
public sealed class CreatureBodyGraph
{
    public int generatorVersion;
    public int torsoCount;
    public int spineCount;
    public int supportLegPairCount;
    public int armPairCount;
    public int headCount;
    public int tailCount;
    public int tentacleCount;
    public int ornamentCount;
    public List<CreatureBodyNode> nodes = new List<CreatureBodyNode>();

    public string StructureSignature => $"T{torsoCount}-S{spineCount}-L{supportLegPairCount}"
        + $"-A{armPairCount}-H{headCount}-R{tailCount}-N{tentacleCount}-O{ornamentCount}";

    public bool Validate(out string error)
    {
if (nodes == null || nodes.Count == 0 || nodes.Count > 64)
        {
            error = "Node count must be between 1 and 64.";
            return false;
        }
        for (int i = 0; i < nodes.Count; i++)
        {
            CreatureBodyNode node = nodes[i];
            if (node == null || node.id != i)
            {
                error = $"Invalid node at index {i}.";
                return false;
            }
            if (node.parentIndex >= i || node.parentIndex < -1)
            {
                error = $"Node {i} has an invalid or cyclic parent.";
                return false;
            }
            if (!IsFinite(node.localPosition) || !IsFinite(node.size)
                || node.size.x <= 0f || node.size.y <= 0f || node.size.z <= 0f)
            {
                error = $"Node {i} contains invalid dimensions.";
                return false;
            }
        }
        error = null;
        return true;
    
}

    static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
