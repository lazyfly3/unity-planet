using System;
using UnityEngine;

public enum NmsCreatureMotionMode
{
    ImportedAnimator,
    Procedural
}

[Serializable]
public sealed class NmsProceduralLegProfile
{
    public string chainId;
    [Range(0f, 1f)] public float walkPhase;
    [Range(0f, 1f)] public float runPhase;
    public Vector3 bendHintLocal = Vector3.forward;
}

[CreateAssetMenu(
    menuName = "Creatures/NMS Procedural Motion Profile",
    fileName = "NmsProceduralMotionProfile")]
public sealed class NmsProceduralMotionProfile : ScriptableObject
{
    [SerializeField] string familyId = "AntelopeQuadruped";
    [SerializeField] string bodyBoneName = "HipJNT";
    [SerializeField] string neckBoneName = "Neck1JNT";
    [SerializeField] string headBoneName = "HeadJNT";
    [SerializeField] string tailBoneName = "Tail1JNT";
    [SerializeField] NmsProceduralGaitTemplate walkGaitTemplate;
    [SerializeField, Min(0.1f)] float mass = 18f;
    [SerializeField, Range(0.1f, 1f)] float colliderRadiusScale = 0.34f;
    [SerializeField, Min(0.1f)] float runSpeedMultiplier = 2.4f;
    [SerializeField, Min(0.01f)] float normalizedStepLength = 0.42f;
    [SerializeField, Min(0.01f)] float normalizedStepHeight = 0.18f;
    [SerializeField, Min(0.1f)] float jumpHeight = 1.1f;
    [SerializeField, Min(0f)] float attackImpulse = 4f;
    [Header("Ground Adhesion")]
    [SerializeField, Min(1f)] float groundHeightKp = 105f;
    [SerializeField, Min(0f)] float groundHeightKd = 21f;
    [SerializeField, Range(0.05f, 0.3f)] float groundGraceTime = 0.12f;
    [SerializeField, Range(1f, 4f)] float maximumAdhesionGravity = 2.5f;
    [SerializeField] NmsProceduralLegProfile[] legs =
    {
        new NmsProceduralLegProfile
        {
            chainId = "FrontLeft", walkPhase = 0f, runPhase = 0f,
            bendHintLocal = Vector3.forward
        },
        new NmsProceduralLegProfile
        {
            chainId = "FrontRight", walkPhase = 0.5f, runPhase = 0.5f,
            bendHintLocal = Vector3.forward
        },
        new NmsProceduralLegProfile
        {
            chainId = "RearLeft", walkPhase = 0.75f, runPhase = 0.5f,
            bendHintLocal = Vector3.back
        },
        new NmsProceduralLegProfile
        {
            chainId = "RearRight", walkPhase = 0.25f, runPhase = 0f,
            bendHintLocal = Vector3.back
        }
    };

    public string FamilyId => familyId;
    public string BodyBoneName => bodyBoneName;
    public string NeckBoneName => neckBoneName;
    public string HeadBoneName => headBoneName;
    public string TailBoneName => tailBoneName;
    public NmsProceduralGaitTemplate WalkGaitTemplate => walkGaitTemplate;
    public float Mass => mass;
    public float ColliderRadiusScale => colliderRadiusScale;
    public float RunSpeedMultiplier => runSpeedMultiplier;
    public float NormalizedStepLength => normalizedStepLength;
    public float NormalizedStepHeight => normalizedStepHeight;
    public float JumpHeight => jumpHeight;
    public float AttackImpulse => attackImpulse;
    public float GroundHeightKp => groundHeightKp;
    public float GroundHeightKd => groundHeightKd;
    public float GroundGraceTime => groundGraceTime;
    public float MaximumAdhesionGravity => maximumAdhesionGravity;
    public NmsProceduralLegProfile[] Legs => legs;

    public bool TryGetLeg(string chainId, out NmsProceduralLegProfile result)
    {
        if (legs != null)
        {
            for (int i = 0; i < legs.Length; i++)
            {
                if (legs[i] != null
                    && string.Equals(legs[i].chainId, chainId, StringComparison.Ordinal))
                {
                    result = legs[i];
                    return true;
                }
            }
        }
        result = null;
        return false;
    }

#if UNITY_EDITOR
    public void SetWalkGaitTemplateEditor(NmsProceduralGaitTemplate value)
    {
        walkGaitTemplate = value;
    }
#endif
}

public readonly struct NmsCreatureSpawnContext
{
    public readonly GameObject root;
    public readonly NmsCreatureFamilyDefinition family;
    public readonly NmsCreatureSpeciesDefinition species;
    public readonly Animator animator;
    public readonly NmsProceduralMotionController proceduralController;
    public readonly NmsImportedAnimatorMotionController importedController;

    public NmsCreatureSpawnContext(
        GameObject creatureRoot,
        NmsCreatureFamilyDefinition creatureFamily,
        NmsCreatureSpeciesDefinition creatureSpecies,
        Animator creatureAnimator,
        NmsProceduralMotionController controller,
        NmsImportedAnimatorMotionController importedMotionController = null)
    {
        root = creatureRoot;
        family = creatureFamily;
        species = creatureSpecies;
        animator = creatureAnimator;
        proceduralController = controller;
        importedController = importedMotionController;
    }
}
