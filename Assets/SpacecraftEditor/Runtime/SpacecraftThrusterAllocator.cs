using System;
using UnityEngine;

namespace SpacecraftEditor
{
    public sealed class SpacecraftThrusterAllocator
    {
        struct Actuator
        {
            public ThrusterPart part;
            public Vector3 localPosition;
            public Vector3 localDirection;
            public float baseForce;
            public bool builtIn;
        }

        const int RcsActuatorCount = 24;
        const int SolverIterations = 18;
        const float SolutionRegularization = 0.015f;
        const float RiseTime = 0.12f;
        const float FallTime = 0.08f;

        Actuator[] actuators = Array.Empty<Actuator>();
        Vector3[] localForces = Array.Empty<Vector3>();
        Vector3[] localTorques = Array.Empty<Vector3>();
        float[] targetThrottles = Array.Empty<float>();
        float[] appliedThrottles = Array.Empty<float>();
        ShipAssembly sourceAssembly;
        ShipHullDefinition sourceHull;
        float referenceLength = 1f;

        public int ActuatorCount => actuators.Length;
        public float ControlAuthority { get; private set; } = 1f;
        public float MaximumAppliedThrottle { get; private set; }
        public Vector3 AppliedLocalForce { get; private set; }
        public Vector3 AppliedLocalTorque { get; private set; }

        public void Rebuild(ShipAssembly assembly, ShipHullDefinition hull)
        {
            sourceAssembly = assembly;
            sourceHull = hull;
            int externalCount = 0;
            if (assembly != null)
            {
                for (int index = 0; index < assembly.Thrusters.Count; index++)
                {
                    if (assembly.Thrusters[index] != null && assembly.Thrusters[index].Definition != null)
                        externalCount++;
                }
            }

            int count = externalCount + (hull == null ? 0 : RcsActuatorCount);
            actuators = new Actuator[count];
            localForces = new Vector3[count];
            localTorques = new Vector3[count];
            targetThrottles = new float[count];
            appliedThrottles = new float[count];

            int cursor = 0;
            if (assembly != null)
            {
                for (int index = 0; index < assembly.Thrusters.Count; index++)
                {
                    ThrusterPart part = assembly.Thrusters[index];
                    if (part == null || part.Definition == null)
                        continue;
                    actuators[cursor++] = new Actuator
                    {
                        part = part,
                        localPosition = assembly.transform.InverseTransformPoint(part.transform.position),
                        localDirection = assembly.transform.InverseTransformDirection(part.ThrustDirection).normalized,
                        baseForce = Mathf.Max(0f, part.ActualThrust),
                        builtIn = false
                    };
                }
            }

            if (hull != null)
                AppendBuiltInRcs(hull, ref cursor);
            referenceLength = hull == null ? 1f : Mathf.Max(0.5f, hull.Dimensions.magnitude * 0.25f);
            ControlAuthority = 1f;
            MaximumAppliedThrottle = 0f;
            AppliedLocalForce = Vector3.zero;
            AppliedLocalTorque = Vector3.zero;
        }

        public float SolveAndApply(
            Rigidbody body,
            Transform shipTransform,
            Vector3 desiredLocalForce,
            Vector3 desiredLocalTorque,
            float torqueWeight,
            float thrustMultiplier,
            float deltaTime)
        {
            if (body == null || shipTransform == null || actuators.Length == 0)
            {
                ControlAuthority = desiredLocalForce.sqrMagnitude + desiredLocalTorque.sqrMagnitude < 0.0001f ? 1f : 0f;
                return ControlAuthority;
            }

            RefreshExternalActuators(shipTransform);
            BuildWrenches(body.centerOfMass, Mathf.Max(1f, thrustMultiplier));
            Solve(desiredLocalForce, desiredLocalTorque, Mathf.Max(0.01f, torqueWeight));
            Apply(body, shipTransform, Mathf.Max(1f, thrustMultiplier), Mathf.Max(0.0001f, deltaTime));
            return ControlAuthority;
        }

        public void StopAll()
        {
            for (int index = 0; index < actuators.Length; index++)
            {
                targetThrottles[index] = 0f;
                appliedThrottles[index] = 0f;
                if (!actuators[index].builtIn && actuators[index].part != null)
                    actuators[index].part.SetExhaust(0f);
            }
            MaximumAppliedThrottle = 0f;
            AppliedLocalForce = Vector3.zero;
            AppliedLocalTorque = Vector3.zero;
            ControlAuthority = 1f;
        }

        public bool Matches(ShipAssembly assembly, ShipHullDefinition hull)
        {
            if (sourceAssembly != assembly || sourceHull != hull)
                return false;
            int expected = hull == null ? 0 : RcsActuatorCount;
            if (assembly != null)
            {
                for (int index = 0; index < assembly.Thrusters.Count; index++)
                {
                    if (assembly.Thrusters[index] != null && assembly.Thrusters[index].Definition != null)
                        expected++;
                }
            }
            return expected == actuators.Length;
        }

        void AppendBuiltInRcs(ShipHullDefinition hull, ref int cursor)
        {
            Vector3 halfSize = Vector3.Scale(hull.Dimensions, new Vector3(0.42f, 0.42f, 0.42f));
            Vector3 perNozzleForce = hull.FlightProfile.IntegratedRcsNozzleForce;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 position = new Vector3(x * halfSize.x, y * halfSize.y, z * halfSize.z);
                actuators[cursor++] = BuiltIn(position, new Vector3(-x, 0f, 0f), perNozzleForce.x);
                actuators[cursor++] = BuiltIn(position, new Vector3(0f, -y, 0f), perNozzleForce.y);
                actuators[cursor++] = BuiltIn(position, new Vector3(0f, 0f, -z), perNozzleForce.z);
            }
        }

        static Actuator BuiltIn(Vector3 position, Vector3 direction, float force)
        {
            return new Actuator
            {
                localPosition = position,
                localDirection = direction,
                baseForce = Mathf.Max(0f, force),
                builtIn = true
            };
        }

        void RefreshExternalActuators(Transform shipTransform)
        {
            for (int index = 0; index < actuators.Length; index++)
            {
                Actuator actuator = actuators[index];
                if (actuator.builtIn || actuator.part == null)
                    continue;
                actuator.localPosition = shipTransform.InverseTransformPoint(actuator.part.transform.position);
                actuator.localDirection = shipTransform.InverseTransformDirection(actuator.part.ThrustDirection).normalized;
                actuator.baseForce = Mathf.Max(0f, actuator.part.ActualThrust);
                actuators[index] = actuator;
            }
        }

        void BuildWrenches(Vector3 localCenterOfMass, float thrustMultiplier)
        {
            for (int index = 0; index < actuators.Length; index++)
            {
                Vector3 force = actuators[index].localDirection * (actuators[index].baseForce * thrustMultiplier);
                localForces[index] = force;
                localTorques[index] = Vector3.Cross(actuators[index].localPosition - localCenterOfMass, force);
            }
        }

        void Solve(Vector3 desiredForce, Vector3 desiredTorque, float torqueWeight)
        {
            float desiredMagnitudeSquared = WeightedDot(
                desiredForce,
                desiredTorque,
                desiredForce,
                desiredTorque,
                torqueWeight);
            if (desiredMagnitudeSquared < 0.0001f)
            {
                Array.Clear(targetThrottles, 0, targetThrottles.Length);
                ControlAuthority = 1f;
                return;
            }

            Vector3 residualForce = desiredForce;
            Vector3 residualTorque = desiredTorque;
            for (int index = 0; index < actuators.Length; index++)
            {
                targetThrottles[index] = Mathf.Clamp01(targetThrottles[index]);
                residualForce -= localForces[index] * targetThrottles[index];
                residualTorque -= localTorques[index] * targetThrottles[index];
            }

            for (int iteration = 0; iteration < SolverIterations; iteration++)
            {
                bool reverse = (iteration & 1) != 0;
                for (int step = 0; step < actuators.Length; step++)
                {
                    int index = reverse ? actuators.Length - 1 - step : step;
                    float previous = targetThrottles[index];
                    residualForce += localForces[index] * previous;
                    residualTorque += localTorques[index] * previous;

                    float actuatorMagnitude = WeightedDot(
                        localForces[index],
                        localTorques[index],
                        localForces[index],
                        localTorques[index],
                        torqueWeight);
                    float denominator = actuatorMagnitude * (1f + SolutionRegularization) + 0.000001f;
                    float numerator = WeightedDot(
                        localForces[index],
                        localTorques[index],
                        residualForce,
                        residualTorque,
                        torqueWeight);
                    float next = denominator > 0.000001f ? Mathf.Clamp01(numerator / denominator) : 0f;
                    targetThrottles[index] = next;
                    residualForce -= localForces[index] * next;
                    residualTorque -= localTorques[index] * next;
                }
            }

            float desiredMagnitude = Mathf.Sqrt(Mathf.Max(0f, desiredMagnitudeSquared));
            float residualMagnitude = Mathf.Sqrt(Mathf.Max(0f, WeightedDot(
                residualForce,
                residualTorque,
                residualForce,
                residualTorque,
                torqueWeight)));
            ControlAuthority = desiredMagnitude < 0.01f
                ? 1f
                : Mathf.Clamp01(1f - residualMagnitude / desiredMagnitude);
        }

        void Apply(Rigidbody body, Transform shipTransform, float thrustMultiplier, float deltaTime)
        {
            MaximumAppliedThrottle = 0f;
            AppliedLocalForce = Vector3.zero;
            AppliedLocalTorque = Vector3.zero;
            for (int index = 0; index < actuators.Length; index++)
            {
                float duration = targetThrottles[index] > appliedThrottles[index] ? RiseTime : FallTime;
                // All nozzles that are rising from rest must preserve the ratios
                // produced by the wrench solver. An absolute MoveTowards step
                // saturates small throttle targets first and temporarily breaks
                // paired RCS symmetry, which turns a pure translation request
                // into a large unintended torque. A common exponential response
                // keeps proportional targets proportional throughout spool-up.
                float response = 1f - Mathf.Exp(-deltaTime / duration);
                appliedThrottles[index] = Mathf.Lerp(
                    appliedThrottles[index],
                    targetThrottles[index],
                    response);
                if (Mathf.Abs(appliedThrottles[index] - targetThrottles[index]) < 0.0001f)
                    appliedThrottles[index] = targetThrottles[index];
                float throttle = appliedThrottles[index];
                MaximumAppliedThrottle = Mathf.Max(MaximumAppliedThrottle, throttle);

                Actuator actuator = actuators[index];
                Vector3 appliedForce = localForces[index] * throttle;
                AppliedLocalForce += appliedForce;
                AppliedLocalTorque += Vector3.Cross(
                    actuator.localPosition - body.centerOfMass,
                    appliedForce);
                if (actuator.builtIn)
                {
                    if (throttle <= 0.0001f)
                        continue;
                    Vector3 force = shipTransform.TransformDirection(actuator.localDirection)
                        * (actuator.baseForce * thrustMultiplier * throttle);
                    Vector3 position = shipTransform.TransformPoint(actuator.localPosition);
                    body.AddForceAtPosition(force, position, ForceMode.Force);
                }
                else if (actuator.part != null)
                {
                    actuator.part.ApplyThrust(body, throttle, thrustMultiplier);
                }
            }
        }

        float WeightedDot(
            Vector3 forceA,
            Vector3 torqueA,
            Vector3 forceB,
            Vector3 torqueB,
            float torqueWeight)
        {
            float torqueScale = torqueWeight / referenceLength;
            return Vector3.Dot(forceA, forceB)
                + Vector3.Dot(torqueA, torqueB) * torqueScale * torqueScale;
        }
    }
}
