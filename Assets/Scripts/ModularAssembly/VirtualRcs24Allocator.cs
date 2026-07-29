using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityPlanet.ModularAssembly
{
    [Serializable]
    public struct Rcs24Channel
    {
        public int index;
        public Vector3 localPosition;
        public Vector3 localDirection;
        public float maximumForce;
        public float currentForce;
        public bool hasCoreContribution;
    }

    [Serializable]
    public struct ThrusterAxisContribution
    {
        public int thrusterIndex;
        public int axis;
        public Vector3 localPosition;
        public Vector3 localForce;
        public Vector3 localTorque;
        public float maximumForce;
        public bool core;
    }

    [Serializable]
    public struct DirectionalAuthority24
    {
        public Vector3 positiveForce;
        public Vector3 negativeForce;
        public Vector3 positiveTorque;
        public Vector3 negativeTorque;

        public float ForceCapacity(int axis, float sign)
        {
            return sign >= 0f ? positiveForce[axis] : negativeForce[axis];
        }

        public float TorqueCapacity(int axis, float sign)
        {
            return sign >= 0f ? positiveTorque[axis] : negativeTorque[axis];
        }
    }

    public struct Rcs24ThrusterInput
    {
        public int sourceIndex;
        public Vector3 localPosition;
        public Vector3 localDirection;
        public float maximumForce;
        public float responseTime;
    }

    public struct Rcs24SolveRequest
    {
        public Vector3 desired;
        public bool strictDirection;
        public string group;
    }

    public struct Rcs24SolveResult
    {
        public Vector3 localForce;
        public Vector3 localTorque;
        public float commonScale;
        public float residualError;
        public int missingAxisMask;
        public string bottleneckAxis;
        public float[] thrusterThrottles;
    }

    /// <summary>
    /// Robocraft-style virtual RCS. The 24 channels are eight authority-box
    /// corners with three inward axial channels at each corner. Physical
    /// thrusters remain the shared resources behind those virtual channels.
    /// </summary>
    public sealed class VirtualRcs24Allocator
    {
        const float Epsilon = 0.01f;

        sealed class Contribution
        {
            public int thrusterIndex;
            public bool core;
            public Vector3 localPosition;
            public Vector3 forceVector;
            public Vector3 torqueVector;
            public float maximumForce;
            public float stepScale;
            public float usedFraction;
            public Vector3 allocatedForce;
            public Vector3 allocatedTorque;
            public readonly int[] channelIndices = new int[4];
            public readonly float[] channelWeights = new float[4];
            public int channelCount;
        }

        readonly List<Contribution> contributions =
            new List<Contribution>();
        readonly List<Rcs24Channel> channels =
            new List<Rcs24Channel>(24);
        Rcs24ThrusterInput[] thrusters =
            Array.Empty<Rcs24ThrusterInput>();
        Vector3[] targetForces = Array.Empty<Vector3>();
        Vector3[] targetTorques = Array.Empty<Vector3>();
        Vector3[] actualForces = Array.Empty<Vector3>();
        Vector3[] actualTorques = Array.Empty<Vector3>();
        float[] targetThrottles = Array.Empty<float>();
        float[] actualThrottles = Array.Empty<float>();
        Vector3 centerOfMass;
        Vector3 halfExtents = Vector3.one;
        Vector3 coreForce;
        Vector3 coreTorque;
        float lastTranslationScale = 1f;
        float lastRotationScale = 1f;
        int lastMissingAxisMask;
        string lastBottleneckAxis = "none";

        public DirectionalAuthority24 Authority { get; private set; }
        public IReadOnlyList<Rcs24Channel> Channels => channels;
        public string LastBottleneckAxis => lastBottleneckAxis;
        public int LastMissingAxisMask => lastMissingAxisMask;

        public void Rebuild(
            IReadOnlyList<Rcs24ThrusterInput> sources,
            Vector3 localCenterOfMass,
            bool includeTrainingCore,
            float trainingUpForce,
            float trainingDownForce,
            float trainingPlanarForce)
        {
            centerOfMass = localCenterOfMass;
            int count = sources != null ? sources.Count : 0;
            thrusters = new Rcs24ThrusterInput[count];
            for (int i = 0; i < count; i++)
                thrusters[i] = sources[i];

            ResizeRuntimeArrays(count);
            contributions.Clear();
            BuildAuthorityBounds();
            BuildChannels();

            for (int i = 0; i < thrusters.Length; i++)
                AddPhysicalThruster(thrusters[i]);
            if (includeTrainingCore)
            {
                AddTrainingCore(
                    trainingUpForce,
                    trainingDownForce,
                    trainingPlanarForce);
            }

            RecalculateAuthority();
            ResetState();
        }

        public void BeginStep(IReadOnlyList<float> forceScales)
        {
            Array.Clear(targetForces, 0, targetForces.Length);
            Array.Clear(targetTorques, 0, targetTorques.Length);
            Array.Clear(targetThrottles, 0, targetThrottles.Length);
            coreForce = Vector3.zero;
            coreTorque = Vector3.zero;
            lastTranslationScale = 1f;
            lastRotationScale = 1f;
            lastMissingAxisMask = 0;
            lastBottleneckAxis = "none";

            for (int i = 0; i < channels.Count; i++)
            {
                Rcs24Channel channel = channels[i];
                channel.currentForce = 0f;
                channels[i] = channel;
            }

            foreach (Contribution contribution in contributions)
            {
                contribution.usedFraction = 0f;
                contribution.allocatedForce = Vector3.zero;
                contribution.allocatedTorque = Vector3.zero;
                contribution.stepScale = contribution.core
                    ? 1f
                    : contribution.thrusterIndex >= 0 &&
                      forceScales != null &&
                      contribution.thrusterIndex < forceScales.Count
                        ? Mathf.Clamp01(
                            forceScales[contribution.thrusterIndex])
                        : 1f;
            }
        }

        public Rcs24SolveResult SolveTranslation(Rcs24SolveRequest request)
        {
            return SolveVector(request, false);
        }

        public Rcs24SolveResult SolveRotation(Rcs24SolveRequest request)
        {
            return SolveVector(request, true);
        }

        public Rcs24SolveResult SolveHover(Rcs24SolveRequest request)
        {
            return SolveVector(request, false);
        }

        public Rcs24SolveResult CompleteStep(float deltaTime)
        {
            foreach (Contribution contribution in contributions)
            {
                if (contribution.core)
                {
                    coreForce += contribution.allocatedForce;
                    coreTorque += contribution.allocatedTorque;
                    continue;
                }

                int index = contribution.thrusterIndex;
                if (index < 0 || index >= thrusters.Length)
                    continue;
                targetForces[index] += contribution.allocatedForce;
                targetTorques[index] += contribution.allocatedTorque;
                targetThrottles[index] = Mathf.Max(
                    targetThrottles[index],
                    contribution.usedFraction);
            }

            Vector3 resultForce = coreForce;
            Vector3 resultTorque = coreTorque;
            for (int i = 0; i < thrusters.Length; i++)
            {
                float duration = Mathf.Max(
                    0.01f,
                    thrusters[i].responseTime);
                float response =
                    1f - Mathf.Exp(-Mathf.Max(0f, deltaTime) / duration);
                actualForces[i] = Vector3.Lerp(
                    actualForces[i],
                    targetForces[i],
                    response);
                actualTorques[i] = Vector3.Lerp(
                    actualTorques[i],
                    targetTorques[i],
                    response);
                actualThrottles[i] = Mathf.Lerp(
                    actualThrottles[i],
                    targetThrottles[i],
                    response);
                resultForce += actualForces[i];
                resultTorque += actualTorques[i];
            }

            return new Rcs24SolveResult
            {
                localForce = resultForce,
                localTorque = resultTorque,
                commonScale = Mathf.Min(
                    lastTranslationScale,
                    lastRotationScale),
                residualError = 0f,
                missingAxisMask = lastMissingAxisMask,
                bottleneckAxis = lastBottleneckAxis,
                thrusterThrottles = (float[])actualThrottles.Clone()
            };
        }

        public void ResetState()
        {
            Array.Clear(targetForces, 0, targetForces.Length);
            Array.Clear(targetTorques, 0, targetTorques.Length);
            Array.Clear(actualForces, 0, actualForces.Length);
            Array.Clear(actualTorques, 0, actualTorques.Length);
            Array.Clear(targetThrottles, 0, targetThrottles.Length);
            Array.Clear(actualThrottles, 0, actualThrottles.Length);
            foreach (Contribution contribution in contributions)
            {
                contribution.usedFraction = 0f;
                contribution.allocatedForce = Vector3.zero;
                contribution.allocatedTorque = Vector3.zero;
            }
        }

        Rcs24SolveResult SolveVector(
            Rcs24SolveRequest request,
            bool torque)
        {
            Vector3 desired = request.desired;
            if (desired.sqrMagnitude < Epsilon * Epsilon)
            {
                return new Rcs24SolveResult
                {
                    commonScale = 1f,
                    bottleneckAxis = "none"
                };
            }

            float commonScale = 1f;
            int missingMask = 0;
            string bottleneck = "none";
            for (int axis = 0; axis < 3; axis++)
            {
                float demand = desired[axis];
                if (Mathf.Abs(demand) < Epsilon)
                    continue;
                float capacity = AvailableCapacity(
                    axis,
                    Mathf.Sign(demand),
                    torque);
                if (capacity < Epsilon)
                {
                    missingMask |= 1 << axis;
                    bottleneck = AxisName(axis, demand);
                    if (request.strictDirection)
                    {
                        RecordSolveState(
                            torque,
                            0f,
                            missingMask,
                            bottleneck);
                        return new Rcs24SolveResult
                        {
                            commonScale = 0f,
                            residualError = desired.magnitude,
                            missingAxisMask = missingMask,
                            bottleneckAxis = bottleneck
                        };
                    }
                    continue;
                }

                float axisScale = capacity / Mathf.Abs(demand);
                if (axisScale < commonScale)
                {
                    commonScale = axisScale;
                    bottleneck = AxisName(axis, demand);
                }
            }
            commonScale = Mathf.Clamp01(commonScale);

            Vector3 output = desired * commonScale;
            for (int axis = 0; axis < 3; axis++)
            {
                float demand = output[axis];
                if (Mathf.Abs(demand) < Epsilon)
                    continue;
                AllocateAxis(
                    axis,
                    Mathf.Sign(demand),
                    Mathf.Abs(demand),
                    torque);
            }

            RecordSolveState(
                torque,
                commonScale,
                missingMask,
                bottleneck);
            return new Rcs24SolveResult
            {
                localForce = torque ? Vector3.zero : output,
                localTorque = torque ? output : Vector3.zero,
                commonScale = commonScale,
                residualError =
                    (desired - output).magnitude,
                missingAxisMask = missingMask,
                bottleneckAxis = bottleneck
            };
        }

        void AllocateAxis(
            int axis,
            float sign,
            float demand,
            bool torque)
        {
            float capacity = AvailableCapacity(axis, sign, torque);
            if (capacity < Epsilon)
                return;
            float fraction = Mathf.Clamp01(demand / capacity);

            foreach (Contribution contribution in contributions)
            {
                float component = torque
                    ? contribution.torqueVector[axis]
                    : contribution.forceVector[axis];
                if (component * sign <= Epsilon)
                    continue;
                float remaining = 1f - contribution.usedFraction;
                if (remaining <= Epsilon)
                    continue;
                float addedUse = remaining * fraction;
                contribution.usedFraction += addedUse;
                float output = Mathf.Abs(component) *
                               contribution.stepScale *
                               addedUse;
                if (torque)
                {
                    contribution.allocatedTorque[axis] += sign * output;
                }
                else
                {
                    contribution.allocatedForce[axis] += sign * output;
                    AddChannelOutput(contribution, output);
                }
            }
        }

        float AvailableCapacity(int axis, float sign, bool torque)
        {
            float result = 0f;
            foreach (Contribution contribution in contributions)
            {
                float component = torque
                    ? contribution.torqueVector[axis]
                    : contribution.forceVector[axis];
                if (component * sign <= Epsilon)
                    continue;
                result += Mathf.Abs(component) *
                          contribution.stepScale *
                          Mathf.Max(
                              0f,
                              1f - contribution.usedFraction);
            }
            return result;
        }

        void RecordSolveState(
            bool torque,
            float scale,
            int missingMask,
            string bottleneck)
        {
            if (torque)
                lastRotationScale = Mathf.Min(lastRotationScale, scale);
            else
                lastTranslationScale =
                    Mathf.Min(lastTranslationScale, scale);
            lastMissingAxisMask |= missingMask;
            if (!string.IsNullOrEmpty(bottleneck) &&
                bottleneck != "none")
            {
                lastBottleneckAxis = bottleneck;
            }
        }

        void BuildAuthorityBounds()
        {
            halfExtents = new Vector3(0.75f, 0.75f, 0.75f);
            foreach (Rcs24ThrusterInput source in thrusters)
            {
                Vector3 offset = source.localPosition - centerOfMass;
                halfExtents.x = Mathf.Max(
                    halfExtents.x,
                    Mathf.Abs(offset.x));
                halfExtents.y = Mathf.Max(
                    halfExtents.y,
                    Mathf.Abs(offset.y));
                halfExtents.z = Mathf.Max(
                    halfExtents.z,
                    Mathf.Abs(offset.z));
            }
        }

        void BuildChannels()
        {
            channels.Clear();
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 position = centerOfMass + new Vector3(
                    x * halfExtents.x,
                    y * halfExtents.y,
                    z * halfExtents.z);
                AddChannel(position, new Vector3(-x, 0f, 0f));
                AddChannel(position, new Vector3(0f, -y, 0f));
                AddChannel(position, new Vector3(0f, 0f, -z));
            }
        }

        void AddChannel(Vector3 position, Vector3 direction)
        {
            channels.Add(new Rcs24Channel
            {
                index = channels.Count,
                localPosition = position,
                localDirection = direction
            });
        }

        void AddPhysicalThruster(Rcs24ThrusterInput source)
        {
            Vector3 direction = source.localDirection.sqrMagnitude > Epsilon
                ? source.localDirection.normalized
                : Vector3.forward;
            for (int axis = 0; axis < 3; axis++)
            {
                float signedForce =
                    direction[axis] * Mathf.Max(0f, source.maximumForce);
                if (Mathf.Abs(signedForce) < Epsilon)
                    continue;
                Vector3 force = Axis(axis) * signedForce;
                Contribution contribution = new Contribution
                {
                    thrusterIndex = source.sourceIndex,
                    localPosition = source.localPosition,
                    forceVector = force,
                    torqueVector = Vector3.Cross(
                        source.localPosition - centerOfMass,
                        force),
                    maximumForce = Mathf.Abs(signedForce)
                };
                AssignChannelWeights(contribution, axis, Mathf.Sign(signedForce));
                contributions.Add(contribution);
            }
        }

        void AddTrainingCore(float up, float down, float planar)
        {
            for (int i = 0; i < channels.Count; i++)
            {
                Rcs24Channel channel = channels[i];
                Vector3 direction = channel.localDirection;
                float total = Mathf.Abs(direction.y) > 0.5f
                    ? direction.y > 0f ? up : down
                    : planar;
                float force = Mathf.Max(0f, total) * 0.25f;
                if (force < Epsilon)
                    continue;
                Vector3 forceVector = direction * force;
                Contribution contribution = new Contribution
                {
                    thrusterIndex = -1,
                    core = true,
                    localPosition = channel.localPosition,
                    forceVector = forceVector,
                    torqueVector = Vector3.Cross(
                        channel.localPosition - centerOfMass,
                        forceVector),
                    maximumForce = force,
                    channelCount = 1
                };
                contribution.channelIndices[0] = i;
                contribution.channelWeights[0] = 1f;
                contributions.Add(contribution);
                channel.hasCoreContribution = true;
                channels[i] = channel;
            }
        }

        void AssignChannelWeights(
            Contribution contribution,
            int axis,
            float directionSign)
        {
            int first = (axis + 1) % 3;
            int second = (axis + 2) % 3;
            float normalizedFirst = Mathf.Clamp(
                (contribution.localPosition[first] - centerOfMass[first]) /
                Mathf.Max(Epsilon, halfExtents[first]),
                -1f,
                1f);
            float normalizedSecond = Mathf.Clamp(
                (contribution.localPosition[second] - centerOfMass[second]) /
                Mathf.Max(Epsilon, halfExtents[second]),
                -1f,
                1f);
            int cursor = 0;
            for (int firstSign = -1; firstSign <= 1; firstSign += 2)
            for (int secondSign = -1; secondSign <= 1; secondSign += 2)
            {
                int[] corner = { 0, 0, 0 };
                corner[axis] = directionSign > 0f ? -1 : 1;
                corner[first] = firstSign;
                corner[second] = secondSign;
                contribution.channelIndices[cursor] =
                    FindChannel(corner[0], corner[1], corner[2], axis);
                contribution.channelWeights[cursor] =
                    0.25f *
                    (1f + firstSign * normalizedFirst) *
                    (1f + secondSign * normalizedSecond);
                cursor++;
            }
            contribution.channelCount = cursor;
        }

        int FindChannel(int x, int y, int z, int axis)
        {
            int corner = ((x + 1) / 2) * 4 +
                         ((y + 1) / 2) * 2 +
                         ((z + 1) / 2);
            return corner * 3 + axis;
        }

        void RecalculateAuthority()
        {
            DirectionalAuthority24 authority = default;
            for (int i = 0; i < channels.Count; i++)
            {
                Rcs24Channel channel = channels[i];
                channel.maximumForce = 0f;
                channels[i] = channel;
            }

            foreach (Contribution contribution in contributions)
            {
                AddSigned(
                    contribution.forceVector,
                    ref authority.positiveForce,
                    ref authority.negativeForce);
                AddSigned(
                    contribution.torqueVector,
                    ref authority.positiveTorque,
                    ref authority.negativeTorque);
                for (int i = 0; i < contribution.channelCount; i++)
                {
                    int index = contribution.channelIndices[i];
                    if (index < 0 || index >= channels.Count)
                        continue;
                    Rcs24Channel channel = channels[index];
                    channel.maximumForce +=
                        contribution.maximumForce *
                        contribution.channelWeights[i];
                    channels[index] = channel;
                }
            }
            Authority = authority;
        }

        void AddChannelOutput(Contribution contribution, float force)
        {
            for (int i = 0; i < contribution.channelCount; i++)
            {
                int index = contribution.channelIndices[i];
                if (index < 0 || index >= channels.Count)
                    continue;
                Rcs24Channel channel = channels[index];
                channel.currentForce +=
                    force * contribution.channelWeights[i];
                channels[index] = channel;
            }
        }

        void ResizeRuntimeArrays(int count)
        {
            targetForces = new Vector3[count];
            targetTorques = new Vector3[count];
            actualForces = new Vector3[count];
            actualTorques = new Vector3[count];
            targetThrottles = new float[count];
            actualThrottles = new float[count];
        }

        static void AddSigned(
            Vector3 value,
            ref Vector3 positive,
            ref Vector3 negative)
        {
            positive.x += Mathf.Max(0f, value.x);
            positive.y += Mathf.Max(0f, value.y);
            positive.z += Mathf.Max(0f, value.z);
            negative.x += Mathf.Max(0f, -value.x);
            negative.y += Mathf.Max(0f, -value.y);
            negative.z += Mathf.Max(0f, -value.z);
        }

        static Vector3 Axis(int axis)
        {
            return axis == 0
                ? Vector3.right
                : axis == 1
                    ? Vector3.up
                    : Vector3.forward;
        }

        static string AxisName(int axis, float value)
        {
            string sign = value >= 0f ? "+" : "-";
            return sign + (axis == 0 ? "X" : axis == 1 ? "Y" : "Z");
        }
    }
}
