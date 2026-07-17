using System;
using System.Collections.Generic;
using UnityEngine;

public enum CreatureLimbKind
{
    Arm,
    Leg
}

public enum CreatureEndpointStyle
{
    ThreeClaw,
    FourDigit,
    Webbed,
    Pincer,
    Suction,
    HeavyGrip
}

[Serializable]
public sealed class CreatureAttachmentSocket
{
    public Vector3 connectionAxis = Vector3.up;
    public float visualScale = 1f;
    public float surfaceInset = .06f;
    public float transitionLength = .16f;
    public float transitionRadiusScale = 1f;
}

[Serializable]
public sealed class CreatureFootContactProfile
{
    public float soleOffset = .08f;
    public Vector3 heelPoint;
    public Vector3 toePoint;
    public Vector3 leftPoint;
    public Vector3 rightPoint;
    public Vector3[] soleSamples;

    public static CreatureFootContactProfile CreateFallback(float size)
    {
float halfWidth = Mathf.Max(.05f, size * .4f);
        float halfLength = Mathf.Max(.08f, size * .62f);
        float sole = Mathf.Max(.025f, size * .15f);
        return new CreatureFootContactProfile
        {
            soleOffset = sole,
            heelPoint = new Vector3(0f, -sole, -halfLength),
            toePoint = new Vector3(0f, -sole, halfLength),
            leftPoint = new Vector3(-halfWidth, -sole, 0f),
            rightPoint = new Vector3(halfWidth, -sole, 0f),
            soleSamples = new[]
            {
                new Vector3(0f, -sole, -halfLength),
                new Vector3(0f, -sole, halfLength),
                new Vector3(-halfWidth, -sole, 0f),
                new Vector3(halfWidth, -sole, 0f)
            }
        };
    
    
}
}

[Serializable]
public sealed class CreatureLimbGenome
{
    public int symmetryGroup;
    public CreatureLimbKind kind;
    public string presetId;
    public string endpointId;
    public float lengthScale = 1f;
    public float thicknessScale = 1f;
    public float bendScale = 1f;
}

[Serializable]
public sealed class CreatureLimbPreset
{
    public string id;
    public CreatureLimbKind kind;
    public Vector3 middle;
    public Vector3 end;
    public Vector4 radiusProfile;
    public Vector2 upperJointLimit;
    public Vector2 lowerJointLimit;
}

[Serializable]
public sealed class CreatureEndpointDefinition
{
    public string id;
    public CreatureLimbKind kind;
    public CreatureEndpointStyle style;
    public Vector2 scaleRange;
    public CreatureAttachmentSocket socket;
}

[DisallowMultipleComponent]
public sealed class CreatureLimbPresetAuthoring : MonoBehaviour
{
    public string presetId;
    public CreatureLimbKind kind;
    public Transform rootJoint;
    public Transform middleJoint;
    public Transform endJoint;
    public Transform endSocket;
    public Vector4 radiusProfile;
    public Vector2 upperJointLimit;
    public Vector2 lowerJointLimit;
}

public static class CreatureLimbCatalog
{
    static readonly CreatureLimbPreset[] Presets =
    {
        Arm("arm_straight", new Vector3(0.08f, -0.52f, 0.04f), new Vector3(0.12f, -1f, 0.08f), 1f, .82f, .62f, .45f),
        Arm("arm_curved", new Vector3(0.2f, -0.48f, 0.2f), new Vector3(0.05f, -0.94f, 0.35f), .95f, .78f, .58f, .42f),
        Arm("arm_slender", new Vector3(0.05f, -0.57f, -0.05f), new Vector3(0.18f, -1.16f, 0.08f), .7f, .55f, .42f, .3f),
        Arm("arm_power", new Vector3(0.14f, -0.46f, 0.08f), new Vector3(0.08f, -.9f, 0.18f), 1.35f, 1.12f, .86f, .64f),
        Arm("arm_forward", new Vector3(0.06f, -0.45f, 0.3f), new Vector3(0.12f, -.88f, .56f), 1f, .86f, .62f, .42f),
        Arm("arm_reverse", new Vector3(0.08f, -0.48f, -0.28f), new Vector3(0.14f, -.96f, .08f), .96f, .82f, .6f, .4f),
        Arm("arm_short", new Vector3(.12f, -.4f, .12f), new Vector3(.08f, -.72f, .22f), 1.18f, .98f, .8f, .58f),
        Arm("arm_knuckle", new Vector3(.26f, -.5f, .12f), new Vector3(.08f, -1.04f, .18f), 1.1f, 1.28f, .66f, .48f),
        Leg("leg_straight", new Vector3(.08f, -.53f, .04f), new Vector3(.1f, -1f, .12f), 1.05f, .9f, .68f, .5f),
        Leg("leg_curved", new Vector3(.12f, -.5f, .22f), new Vector3(.06f, -.96f, .38f), 1f, .86f, .64f, .46f),
        Leg("leg_slender", new Vector3(.05f, -.58f, .02f), new Vector3(.1f, -1.18f, .16f), .7f, .58f, .44f, .32f),
        Leg("leg_power", new Vector3(.12f, -.48f, .06f), new Vector3(.08f, -.9f, .16f), 1.45f, 1.2f, .92f, .7f),
        Leg("leg_forward", new Vector3(.06f, -.5f, .3f), new Vector3(.08f, -.98f, .48f), 1.04f, .94f, .66f, .48f),
        Leg("leg_reverse", new Vector3(.08f, -.48f, -.32f), new Vector3(.1f, -1.02f, .12f), 1.02f, .9f, .62f, .44f),
        Leg("leg_short", new Vector3(.12f, -.4f, .08f), new Vector3(.06f, -.74f, .18f), 1.28f, 1.08f, .82f, .62f),
        Leg("leg_hock", new Vector3(.14f, -.46f, -.2f), new Vector3(.06f, -1.08f, .3f), 1.12f, 1.32f, .68f, .45f)
    };

    static readonly CreatureEndpointDefinition[] Endpoints =
    {
        Endpoint("hand_three_claw", CreatureLimbKind.Arm, CreatureEndpointStyle.ThreeClaw, .82f, 1.18f),
        Endpoint("hand_four_digit", CreatureLimbKind.Arm, CreatureEndpointStyle.FourDigit, .82f, 1.16f),
        Endpoint("hand_webbed", CreatureLimbKind.Arm, CreatureEndpointStyle.Webbed, .86f, 1.2f),
        Endpoint("hand_pincer", CreatureLimbKind.Arm, CreatureEndpointStyle.Pincer, .85f, 1.2f),
        Endpoint("hand_suction", CreatureLimbKind.Arm, CreatureEndpointStyle.Suction, .84f, 1.18f),
        Endpoint("hand_heavy", CreatureLimbKind.Arm, CreatureEndpointStyle.HeavyGrip, .9f, 1.24f),
        Endpoint("foot_paw", CreatureLimbKind.Leg, CreatureEndpointStyle.HeavyGrip, .86f, 1.2f),
        Endpoint("foot_three_claw", CreatureLimbKind.Leg, CreatureEndpointStyle.ThreeClaw, .84f, 1.2f),
        Endpoint("foot_hoof", CreatureLimbKind.Leg, CreatureEndpointStyle.Pincer, .86f, 1.18f),
        Endpoint("foot_webbed", CreatureLimbKind.Leg, CreatureEndpointStyle.Webbed, .86f, 1.22f),
        Endpoint("foot_suction", CreatureLimbKind.Leg, CreatureEndpointStyle.Suction, .84f, 1.2f),
        Endpoint("foot_broad", CreatureLimbKind.Leg, CreatureEndpointStyle.FourDigit, .9f, 1.26f)
    };

    public static IReadOnlyList<CreatureLimbPreset> AllPresets => Presets;
    public static IReadOnlyList<CreatureEndpointDefinition> AllEndpoints => Endpoints;

    public static CreatureLimbPreset FindPreset(string id, CreatureLimbKind kind)
    {
for (int i = 0; i < Presets.Length; i++)
            if (Presets[i].kind == kind && Presets[i].id == id) return Presets[i];
        for (int i = 0; i < Presets.Length; i++)
            if (Presets[i].kind == kind) return Presets[i];
        return null;
    
    
}

    public static CreatureEndpointDefinition FindEndpoint(string id, CreatureLimbKind kind)
    {
for (int i = 0; i < Endpoints.Length; i++)
            if (Endpoints[i].kind == kind && Endpoints[i].id == id) return Endpoints[i];
        for (int i = 0; i < Endpoints.Length; i++)
            if (Endpoints[i].kind == kind) return Endpoints[i];
        return null;
    
    
}

    public static CreatureLimbPreset GetPreset(CreatureLimbKind kind, int index)
    {
int offset = kind == CreatureLimbKind.Arm ? 0 : 8;
        return Presets[offset + Mathf.Abs(index % 8)];
    
    
}

    public static CreatureEndpointDefinition GetEndpoint(CreatureLimbKind kind, int index)
    {
int offset = kind == CreatureLimbKind.Arm ? 0 : 6;
        return Endpoints[offset + Mathf.Abs(index % 6)];
    
    
}

    static CreatureLimbPreset Arm(string id, Vector3 middle, Vector3 end, float a, float b, float c, float d)
    {
        return Preset(id, CreatureLimbKind.Arm, middle, end, a, b, c, d);
    }

    static CreatureLimbPreset Leg(string id, Vector3 middle, Vector3 end, float a, float b, float c, float d)
    {
        return Preset(id, CreatureLimbKind.Leg, middle, end, a, b, c, d);
    }

    static CreatureLimbPreset Preset(string id, CreatureLimbKind kind, Vector3 middle, Vector3 end,
        float a, float b, float c, float d)
    {
        return new CreatureLimbPreset
        {
            id = id,
            kind = kind,
            middle = middle,
            end = end,
            radiusProfile = new Vector4(a, b, c, d),
            upperJointLimit = new Vector2(-75f, 75f),
            lowerJointLimit = new Vector2(5f, 155f)
        };
    }

    static CreatureEndpointDefinition Endpoint(string id, CreatureLimbKind kind,
        CreatureEndpointStyle style, float minimum, float maximum)
    {
        return new CreatureEndpointDefinition
        {
            id = id,
            kind = kind,
            style = style,
            scaleRange = new Vector2(minimum, maximum),
            socket = CreateSocket(id, kind)
        };
    }

    static CreatureAttachmentSocket CreateSocket(string id, CreatureLimbKind kind)
    {
        float calibration = 1f;
        if (id == "foot_hoof") calibration = 1.12f;
        else if (id == "foot_paw") calibration = 1.18f;
        else if (id == "foot_broad") calibration = 1.08f;
        else if (id == "hand_pincer") calibration = 1.1f;
        return new CreatureAttachmentSocket
        {
            connectionAxis = Vector3.up,
            visualScale = calibration,
            surfaceInset = kind == CreatureLimbKind.Leg ? .075f : .055f,
            transitionLength = kind == CreatureLimbKind.Leg ? .2f : .15f,
            transitionRadiusScale = kind == CreatureLimbKind.Leg ? 1.12f : 1f
        };
    }
}

public static class CreatureLimbGenomeGenerator
{
    public static void GenerateAndApply(CreatureGenome genome)
    {
if (genome == null || genome.bodyGraph == null) return;
        if (genome.limbs == null) genome.limbs = new List<CreatureLimbGenome>();
        genome.limbs.Clear();
        var seen = new HashSet<int>();
        for (int i = 0; i < genome.bodyGraph.nodes.Count; i++)
        {
            CreatureBodyNode node = genome.bodyGraph.nodes[i];
            CreatureLimbKind kind;
            if (node.type == CreatureBodyNodeType.UpperLeg) kind = CreatureLimbKind.Leg;
            else if (node.type == CreatureBodyNodeType.UpperArm) kind = CreatureLimbKind.Arm;
            else continue;
            int key = ((int)kind << 24) ^ node.symmetryGroup;
            if (!seen.Add(key)) continue;
            uint hash = Hash(unchecked((uint)genome.seed), unchecked((uint)key));
            CreatureLimbPreset preset = CreatureLimbCatalog.GetPreset(kind, (int)(hash & 7u));
            CreatureEndpointDefinition endpoint = CreatureLimbCatalog.GetEndpoint(kind, (int)((hash >> 5) % 6u));
            var limb = new CreatureLimbGenome
            {
                symmetryGroup = node.symmetryGroup,
                kind = kind,
                presetId = preset.id,
                endpointId = endpoint.id,
                lengthScale = Mathf.Lerp(.82f, 1.2f, Unit(hash >> 9)),
                thicknessScale = Mathf.Lerp(.78f, 1.3f, Unit(hash >> 17)),
                bendScale = Mathf.Lerp(.78f, 1.25f, Unit(hash >> 23))
            };
            genome.limbs.Add(limb);
            ApplyToGraph(genome.bodyGraph, limb, preset);
        }
    
    
}

    public static CreatureLimbGenome Find(CreatureGenome genome, CreatureLimbKind kind, int symmetryGroup)
    {
if (genome != null && genome.limbs != null)
            for (int i = 0; i < genome.limbs.Count; i++)
                if (genome.limbs[i].kind == kind && genome.limbs[i].symmetryGroup == symmetryGroup)
                    return genome.limbs[i];
        return null;
    
    
}

    static void ApplyToGraph(CreatureBodyGraph graph, CreatureLimbGenome limb, CreatureLimbPreset preset)
    {
        for (int i = 0; i < graph.nodes.Count; i++)
        {
            CreatureBodyNode upper = graph.nodes[i];
            bool matches = limb.kind == CreatureLimbKind.Leg
                ? upper.type == CreatureBodyNodeType.UpperLeg
                : upper.type == CreatureBodyNodeType.UpperArm;
            if (!matches || upper.symmetryGroup != limb.symmetryGroup) continue;
            int lowerIndex = FindDirectChild(graph, i, limb.kind == CreatureLimbKind.Leg
                ? CreatureBodyNodeType.LowerLeg : CreatureBodyNodeType.LowerArm);
            int endIndex = lowerIndex >= 0 ? FindDirectChild(graph, lowerIndex, limb.kind == CreatureLimbKind.Leg
                ? CreatureBodyNodeType.Foot : CreatureBodyNodeType.Hand) : -1;
            if (lowerIndex < 0 || endIndex < 0) continue;
            float nominalLength = graph.nodes[lowerIndex].localPosition.magnitude
                + graph.nodes[endIndex].localPosition.magnitude;
            float length = Mathf.Max(.35f, nominalLength * limb.lengthScale);
            float mirror = upper.side == CreatureBodySide.Left ? -1f : 1f;
            Vector3 middle = preset.middle;
            Vector3 end = preset.end;
            middle.x *= mirror * limb.bendScale;
            middle.z *= limb.bendScale;
            end.x *= mirror * limb.bendScale;
            end.z *= limb.bendScale;
            graph.nodes[lowerIndex].localPosition = middle * length;
            graph.nodes[endIndex].localPosition = (end - middle) * length;
            upper.radius *= limb.thicknessScale * preset.radiusProfile.x;
            graph.nodes[lowerIndex].radius = upper.radius * preset.radiusProfile.z;
            graph.nodes[endIndex].radius = upper.radius * preset.radiusProfile.w;
        }
    }

    static int FindDirectChild(CreatureBodyGraph graph, int parent, CreatureBodyNodeType type)
    {
        for (int i = parent + 1; i < graph.nodes.Count; i++)
            if (graph.nodes[i].parentIndex == parent && graph.nodes[i].type == type) return i;
        return -1;
    }

    static uint Hash(uint seed, uint value)
    {
        uint hash = seed ^ (value + 0x9E3779B9u + (seed << 6) + (seed >> 2));
        hash ^= hash >> 16;
        hash *= 0x7FEB352Du;
        hash ^= hash >> 15;
        hash *= 0x846CA68Bu;
        return hash ^ (hash >> 16);
    }

    static float Unit(uint value)
    {
        return (value & 0xFFFFu) / 65535f;
    }
}
