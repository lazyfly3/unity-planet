using UnityEngine;

public enum CreatureActionSemantic
{
    Idle,
    Locomotion
}

public sealed class CreatureLegRig
{
    public CreatureBodySide side;
    public Transform upper;
    public Transform lower;
    public Transform distal;
    public Transform foot;
    public float upperLength;
    public float lowerLength;
    public float distalLength;
    public float footSoleOffset;
    public float phaseOffset;
    public float longitudinalPosition;
    public int gaitGroup;
    public Vector3 plantedPosition;
    public Vector3 swingStart;
    public Vector3 swingTarget;
    public Vector3 desiredPosition;
    public bool initialized;
    public bool wasSwinging;

    public bool IsFront => longitudinalPosition >= 0.5f;
    public bool IsLeft => side == CreatureBodySide.Left;
    public float TotalLength => upperLength + lowerLength + distalLength;
}

public sealed class CreatureSecondaryRig
{
    public Transform bone;
    public Quaternion restRotation;
    public CreatureBodyNodeType type;
    public float phase;
    public CreatureBodySide side;
}

public sealed class CreatureRig
{
    public CreatureTopology topology;
    public Transform armature;
    public Transform body;
    public Transform neck;
    public Transform head;
    public Transform tailBase;
    public Transform tailTip;
    public CreatureLegRig[] legs;
    public Transform[] spineBones;
    public Transform[] nodeBones;
    public int[] nodeBoneIndices;
    public CreatureSecondaryRig[] secondaryBones;
    public CreatureBodyGraph graph;
    public Transform[] bones;

    public int bodyIndex;
    public int neckIndex;
    public int headIndex;
    public int tailBaseIndex;
    public int tailTipIndex;
    public int[] upperLegIndices;
    public int[] lowerLegIndices;
    public int[] distalLegIndices;
    public int[] footIndices;
    public int[] spineIndices;
}
