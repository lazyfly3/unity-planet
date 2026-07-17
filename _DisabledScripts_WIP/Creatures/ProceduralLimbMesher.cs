using System.Collections.Generic;
using UnityEngine;

public static class ProceduralLimbMesher
{
    const int RingSides = 14;
    const int LimbRings = 38;

    public static void AppendLimb(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        List<BoneWeight> weights,
        CreatureGenome genome,
        CreatureRig rig,
        Transform root,
        int upperNodeIndex,
        int lowerNodeIndex,
        int endNodeIndex,
        CreatureLimbKind kind)
    {
CreatureBodyNode upperNode = rig.graph.nodes[upperNodeIndex];
        CreatureLimbGenome limb = CreatureLimbGenomeGenerator.Find(
            genome, kind, upperNode.symmetryGroup);
        CreatureLimbPreset preset = CreatureLimbCatalog.FindPreset(limb != null ? limb.presetId : null, kind);
        Vector4 profile = preset != null ? preset.radiusProfile : new Vector4(1f, .85f, .62f, .45f);
        Vector3 shoulder = root.InverseTransformPoint(rig.nodeBones[upperNodeIndex].position);
        Vector3 joint = root.InverseTransformPoint(rig.nodeBones[lowerNodeIndex].position);
        Vector3 endpoint = root.InverseTransformPoint(rig.nodeBones[endNodeIndex].position);
        float baseRadius = Mathf.Max(.045f, upperNode.radius / Mathf.Max(.2f, profile.x));
        CreatureBodyJunction junction = FindJunction(genome, upperNodeIndex);
        float torsoWeightRange = junction != null ? junction.torsoWeightRange : .14f;
        Color main = Color.Lerp(genome.primaryColor, genome.secondaryColor,
            kind == CreatureLimbKind.Arm ? .22f : .08f);

        AppendMuscleSweep(vertices, triangles, colors, weights,
            shoulder, joint, endpoint, baseRadius, profile, main,
            rig.nodeBoneIndices[upperNodeIndex], rig.nodeBoneIndices[lowerNodeIndex],
            rig.nodeBoneIndices[endNodeIndex], LimbRings,
            upperNode.parentIndex >= 0 ? rig.nodeBoneIndices[upperNode.parentIndex] : -1,
            false, torsoWeightRange);

        CreatureEndpointDefinition endpointDefinition = CreatureLimbCatalog.FindEndpoint(
            limb != null ? limb.endpointId : null, kind);
        bool hasExternalEndpoint = limb != null && CreatureExternalEndpointLibrary.HasModel(limb.endpointId);
        if (hasExternalEndpoint)
        {
            CreatureAttachmentSocket socket = endpointDefinition != null ? endpointDefinition.socket : null;
            float transitionLength = socket != null ? socket.transitionLength : baseRadius * .7f;
            float transitionRadius = baseRadius * (socket != null ? socket.transitionRadiusScale : 1f);
            Vector3 direction = (endpoint - joint).sqrMagnitude > .0001f
                ? (endpoint - joint).normalized : Vector3.down;
            AppendSmoothChain(vertices, triangles, colors, weights,
                endpoint - direction * transitionLength,
                endpoint + direction * transitionLength * .15f,
                transitionRadius, transitionRadius * .72f, main,
                rig.nodeBoneIndices[lowerNodeIndex], rig.nodeBoneIndices[endNodeIndex]);
        }
        else
            AppendEndpoint(vertices, triangles, colors, weights, endpoint, endpoint - joint,
                rig.graph.nodes[endNodeIndex].size, endpointDefinition, genome.secondaryColor,
                rig.nodeBoneIndices[endNodeIndex], upperNode.side);
    
    
}

    public static void AppendOrganicHead(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        List<BoneWeight> weights,
        CreatureGenome genome,
        Vector3 center,
        Vector3 size,
        int boneIndex)
    {
size = GetVisualHeadSize(size);
        int rings = 18;
        int sides = 20;
        int style = Mathf.Abs(genome.seed % 5);
        int first = vertices.Count;
        for (int ring = 0; ring <= rings; ring++)
        {
            float v = ring / (float)rings;
            float latitude = Mathf.Lerp(-Mathf.PI * .5f, Mathf.PI * .5f, v);
            float y = Mathf.Sin(latitude);
            float radial = Mathf.Cos(latitude);
            float brow = Mathf.SmoothStep(0f, 1f, 1f - Mathf.Abs(v - .63f) * 4f);
            float muzzle = Mathf.SmoothStep(0f, 1f, 1f - Mathf.Abs(v - .42f) * 5f);
            for (int side = 0; side < sides; side++)
            {
                float u = side / (float)sides;
                float angle = u * Mathf.PI * 2f;
                float lobe = 1f + Mathf.Sin(angle * 2f) * .045f + brow * Mathf.Max(0f, Mathf.Cos(angle)) * .12f;
                Vector3 local = new Vector3(
                    Mathf.Cos(angle) * radial * size.x * .5f * lobe,
                    y * size.y * .5f,
                    Mathf.Sin(angle) * radial * size.z * .5f * (1f + muzzle * .18f));
                float front = Mathf.Clamp01(local.z / Mathf.Max(.001f, size.z * .5f));
                if (style == 1)
                {
                    local.x *= Mathf.Lerp(1f, .68f, front);
                    local.y *= Mathf.Lerp(1f, .82f, front);
                    local.z += front * size.z * .12f;
                }
                else if (style == 2)
                {
                    float browBand = Mathf.Clamp01(1f - Mathf.Abs(local.y / Mathf.Max(.001f, size.y) - .18f) * 4f);
                    local.x *= 1f + browBand * .32f;
                    local.y *= .82f;
                }
                else if (style == 3)
                {
                    local.y *= Mathf.Lerp(.72f, 1.08f, v);
                    local.z += Mathf.Max(0f, local.y) * .16f;
                }
                else if (style == 4)
                {
                    local.x *= Mathf.Lerp(1.12f, .78f, front);
                    local.z += front * front * size.z * .2f;
                }
                vertices.Add(center + local);
                colors.Add(Color.Lerp(genome.primaryColor, genome.secondaryColor, .68f + .18f * muzzle));
                weights.Add(SingleWeight(boneIndex));
            }
        }
        ConnectRings(triangles, first, rings + 1, sides);
    
    
}

    public static Vector3 GetVisualHeadSize(Vector3 source)
    {
float average = Mathf.Max(.18f, (source.x + source.y + source.z) / 3f);
        return new Vector3(
            Mathf.Clamp(source.x, average * .68f, average * 1.48f),
            Mathf.Clamp(source.y, average * .68f, average * 1.4f),
            Mathf.Clamp(source.z, average * .72f, average * 1.52f));
    
    
}

    public static void AppendSmoothChain(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        List<BoneWeight> weights,
        Vector3 start,
        Vector3 end,
        float startRadius,
        float endRadius,
        Color color,
        int startBone,
        int endBone)
    {
Vector3 mid = Vector3.Lerp(start, end, .5f);
        AppendMuscleSweep(vertices, triangles, colors, weights, start, mid, end,
            startRadius, new Vector4(1f, .86f, .62f, Mathf.Max(.08f, endRadius / Mathf.Max(.001f, startRadius))),
            color, startBone, startBone, endBone, 22);
    
    
}

    static void AppendMuscleSweep(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        List<BoneWeight> weights,
        Vector3 start,
        Vector3 middle,
        Vector3 end,
        float baseRadius,
        Vector4 radiusProfile,
        Color color,
        int upperBone,
        int lowerBone,
        int endBone,
        int ringCount = LimbRings,
        int proximalBone = -1,
        bool capStart = true,
        float proximalBlendEnd = .14f)
    {
        int first = vertices.Count;
        Vector3 previousSide = Vector3.zero;
        Vector3 previousTangent = Vector3.zero;
        for (int ring = 0; ring < ringCount; ring++)
        {
            float t = ring / (float)(ringCount - 1);
            Vector3 center = EvaluateCurve(start, middle, end, t);
            Vector3 tangent = EvaluateTangent(start, middle, end, t);
            Vector3 side;
            if (previousSide.sqrMagnitude < .5f)
            {
                Vector3 reference = Mathf.Abs(Vector3.Dot(tangent, Vector3.up)) > .88f
                    ? Vector3.forward : Vector3.up;
                side = Vector3.Cross(reference, tangent).normalized;
            }
            else
            {
                // Parallel transport avoids the abrupt frame roll caused by
                // switching the reference axis as a curved limb turns vertical.
                side = Quaternion.FromToRotation(previousTangent, tangent) * previousSide;
                side = Vector3.ProjectOnPlane(side, tangent);
                if (side.sqrMagnitude < .000001f)
                    side = Vector3.Cross(Vector3.forward, tangent);
                if (side.sqrMagnitude < .000001f)
                    side = Vector3.Cross(Vector3.up, tangent);
                side.Normalize();
            }
            Vector3 normal = Vector3.Cross(tangent, side).normalized;
            previousSide = side;
            previousTangent = tangent;
            float radius = baseRadius * EvaluateRadius(radiusProfile, t);
            float squash = Mathf.Lerp(.88f, 1.05f, Mathf.Sin(t * Mathf.PI));
            BoneWeight weight = ChainWeight(t, proximalBone, upperBone, lowerBone, proximalBlendEnd);
            for (int segment = 0; segment < RingSides; segment++)
            {
                float angle = segment / (float)RingSides * Mathf.PI * 2f;
                float organic = 1f + Mathf.Sin(angle * 3f + t * 4.7f) * .025f;
                vertices.Add(center + side * (Mathf.Cos(angle) * radius * organic)
                    + normal * (Mathf.Sin(angle) * radius * squash * organic));
                colors.Add(Color.Lerp(color, Color.white, Mathf.Max(0f, Mathf.Sin(angle)) * .045f));
                weights.Add(weight);
            }
        }
        ConnectRings(triangles, first, ringCount, RingSides);
        if (capStart)
            Cap(triangles, vertices, colors, weights, first, RingSides, start, color,
                SingleWeight(proximalBone >= 0 ? proximalBone : upperBone), true);
        int last = first + (ringCount - 1) * RingSides;
        Cap(triangles, vertices, colors, weights, last, RingSides, end, color, SingleWeight(endBone), false);
    }

    static void AppendEndpoint(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        List<BoneWeight> weights,
        Vector3 center,
        Vector3 direction,
        Vector3 sourceSize,
        CreatureEndpointDefinition definition,
        Color color,
        int boneIndex,
        CreatureBodySide side)
    {
        CreatureEndpointStyle style = definition != null ? definition.style : CreatureEndpointStyle.ThreeClaw;
        float scale = Mathf.Clamp(Mathf.Max(sourceSize.x, sourceSize.z), .18f, 1.4f);
        Vector3 forward = direction.sqrMagnitude > .0001f ? direction.normalized : Vector3.down;
        Vector3 upReference = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > .9f ? Vector3.forward : Vector3.up;
        Vector3 right = Vector3.Cross(upReference, forward).normalized;
        Vector3 up = Vector3.Cross(forward, right).normalized;
        Vector3 palmEnd = center + forward * scale * .42f;
        AppendSmoothChain(vertices, triangles, colors, weights, center, palmEnd,
            scale * .34f, scale * .27f, color, boneIndex, boneIndex);

        int digits = style == CreatureEndpointStyle.FourDigit ? 4
            : style == CreatureEndpointStyle.Pincer ? 2
            : style == CreatureEndpointStyle.Suction ? 5 : 3;
        float spread = style == CreatureEndpointStyle.Webbed ? .42f : .32f;
        for (int i = 0; i < digits; i++)
        {
            float centered = digits == 1 ? 0f : i / (float)(digits - 1) - .5f;
            float mirror = side == CreatureBodySide.Left ? -1f : 1f;
            Vector3 digitDirection = (forward + right * centered * spread * 2f * mirror + up * .08f).normalized;
            float length = scale * (style == CreatureEndpointStyle.HeavyGrip ? .38f : .55f)
                * (1f - Mathf.Abs(centered) * .15f);
            if (style == CreatureEndpointStyle.Pincer) length *= 1.18f;
            Vector3 digitEnd = palmEnd + digitDirection * length;
            AppendSmoothChain(vertices, triangles, colors, weights, palmEnd, digitEnd,
                scale * (style == CreatureEndpointStyle.HeavyGrip ? .18f : .12f),
                style == CreatureEndpointStyle.Suction ? scale * .09f : scale * .035f,
                color, boneIndex, boneIndex);
        }
    }

    static Vector3 EvaluateCurve(Vector3 a, Vector3 b, Vector3 c, float t)
    {
        if (t <= .5f)
        {
            float u = t * 2f;
            Vector3 before = a - (b - a) * .35f;
            Vector3 after = c;
            return CatmullRom(before, a, b, after, u);
        }
        else
        {
            float u = (t - .5f) * 2f;
            Vector3 before = a;
            Vector3 after = c + (c - b) * .35f;
            return CatmullRom(before, b, c, after, u);
        }
    }

    static Vector3 EvaluateTangent(Vector3 a, Vector3 b, Vector3 c, float t)
    {
        const float epsilon = .002f;
        Vector3 p0 = EvaluateCurve(a, b, c, Mathf.Clamp01(t - epsilon));
        Vector3 p1 = EvaluateCurve(a, b, c, Mathf.Clamp01(t + epsilon));
        Vector3 tangent = p1 - p0;
        return tangent.sqrMagnitude > .000001f ? tangent.normalized : (c - a).normalized;
    }

    static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;
        return .5f * ((2f * p1) + (-p0 + p2) * t
            + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
            + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    static float EvaluateRadius(Vector4 profile, float t)
    {
        float value;
        if (t < .33f) value = Mathf.Lerp(profile.x, profile.y, Mathf.SmoothStep(0f, 1f, t / .33f));
        else if (t < .68f) value = Mathf.Lerp(profile.y, profile.z, Mathf.SmoothStep(0f, 1f, (t - .33f) / .35f));
        else value = Mathf.Lerp(profile.z, profile.w, Mathf.SmoothStep(0f, 1f, (t - .68f) / .32f));
        float jointBulge = Mathf.Exp(-Mathf.Pow((t - .5f) / .095f, 2f)) * .18f;
        return Mathf.Max(.08f, value + jointBulge);
    }

    static BoneWeight ChainWeight(float t, int proximal, int upper, int lower, float proximalBlendEnd)
    {
        proximalBlendEnd = Mathf.Clamp(proximalBlendEnd, .08f, .24f);
        if (proximal >= 0 && t < proximalBlendEnd)
        {
            float rootBlend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.02f, proximalBlendEnd, t));
            return DualWeight(proximal, upper, 1f - rootBlend, rootBlend);
        }
        // The sweep ends at the ankle pivot, so the lower bone already carries
        // every ring to the correct position. Blending most of the shin with the
        // independently rotated foot bone makes linear skinning matrices cancel
        // each other and collapses a ring into the characteristic X-shaped pinch.
        if (t <= .44f)
            return SingleWeight(upper);
        if (t >= .56f)
            return SingleWeight(lower);

        float kneeBlend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.44f, .56f, t));
        return DualWeight(upper, lower, 1f - kneeBlend, kneeBlend);
    }

    static CreatureBodyJunction FindJunction(CreatureGenome genome, int limbRootNodeIndex)
    {
        if (genome == null || genome.bodyJunctions == null) return null;
        for (int i = 0; i < genome.bodyJunctions.Count; i++)
        {
            CreatureBodyJunction junction = genome.bodyJunctions[i];
            if (junction != null && junction.limbRootNodeIndex == limbRootNodeIndex) return junction;
        }
        return null;
    }

    static BoneWeight SingleWeight(int index)
    {
        return new BoneWeight { boneIndex0 = index, weight0 = 1f };
    }

    static BoneWeight DualWeight(int a, int b, float aw, float bw)
    {
        float total = Mathf.Max(.0001f, aw + bw);
        return new BoneWeight { boneIndex0 = a, weight0 = aw / total, boneIndex1 = b, weight1 = bw / total };
    }

    static void ConnectRings(List<int> triangles, int first, int rings, int sides)
    {
        for (int ring = 0; ring < rings - 1; ring++)
            for (int side = 0; side < sides; side++)
            {
                int next = (side + 1) % sides;
                int a = first + ring * sides + side;
                int b = first + ring * sides + next;
                int c = first + (ring + 1) * sides + next;
                int d = first + (ring + 1) * sides + side;
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
                triangles.Add(a); triangles.Add(c); triangles.Add(d);
            }
    }

    static void Cap(List<int> triangles, List<Vector3> vertices, List<Color> colors,
        List<BoneWeight> weights, int ringStart, int sides, Vector3 center, Color color,
        BoneWeight weight, bool reverse)
    {
        int centerIndex = vertices.Count;
        vertices.Add(center);
        colors.Add(color);
        weights.Add(weight);
        for (int side = 0; side < sides; side++)
        {
            int next = (side + 1) % sides;
            if (reverse)
            {
                triangles.Add(centerIndex); triangles.Add(ringStart + next); triangles.Add(ringStart + side);
            }
            else
            {
                triangles.Add(centerIndex); triangles.Add(ringStart + side); triangles.Add(ringStart + next);
            }
        }
    }
}
