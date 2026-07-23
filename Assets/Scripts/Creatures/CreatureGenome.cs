using System;
using UnityEngine;

public enum CreatureTopology
{
    Biped,
    Quadruped,
    Hexapod,
    Serpentine
}

public enum CreatureLocomotionArchetype
{
    CursorialCanid,
    GraviportalElephant,
    CursorialUngulate,
    HexapodTripod
}

public static class CreatureGenerationVersions
{
    public const int LegacyV3 = 3;
    public const int ImplicitV4 = 4;
    public const int AnatomicalV5 = 5;
}

[Serializable]
public sealed class CreatureGenome
{
    public int seed;
    public int generatorVersion = 3;
    public CreatureDesignLanguage designLanguage;
    public CreatureBodyGraph bodyGraph;
    public CreatureTorsoSpline torsoSpline;
    public CreatureTopology topology;
    public CreatureLocomotionArchetype locomotionArchetype;
    public int bodyStyle;
    public int legPairCount;
    public int spineSegmentCount;
    public float bodyLength;
    public float bodyHeight;
    public float bodyWidth;
    public float headScale;
    public float headWidth;
    public float headHeight;
    public float headLength;
    public float neckLength;
    public float earScale;
    public float hornLength;
    public int eyeCount;
    public float tailLength;
    public float tailThickness;
    public float legLength;
    public float frontLegLength;
    public float rearLegLength;
    public float legThickness;
    public float legSpread;
    public float footScale;
    public float gaitHeight;
    public float gaitFrequency;
    public float serpentineWaveAmplitude;
    public float serpentinePhaseLag;
    public float serpentineLateralFriction;
    public float serpentineLongitudinalFriction;
    public float serpentineBackwardFriction;
    public float serpentineTractionEfficiency;
    public float serpentineBodyFlattening;
    public float serpentineHeadWidth;
    public float serpentineTailTaper;
    public Color primaryColor;
    public Color secondaryColor;
    public CreatureV5EditableParameters v5Parameters;
}
