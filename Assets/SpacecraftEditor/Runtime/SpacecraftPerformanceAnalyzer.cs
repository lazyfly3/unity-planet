using UnityEngine;

namespace SpacecraftEditor
{
    public struct SpacecraftPerformanceMetrics
    {
        public float totalMass;
        public Vector3 localCenterOfMass;
        public Vector3 aerodynamicCenter;
        public float wingArea;
        public float wingLoading;
        public float estimatedStallSpeed;
        public float estimatedLiftToDrag;
        public float sideStability;
        public SpacecraftDirectionalAuthority directionalAuthority;

        public float HoverMargin(float gravity, float gravitySupport = 20f)
        {
            return gravitySupport
                + directionalAuthority.positiveAcceleration.y
                - Mathf.Max(0f, gravity);
        }
    }

    public static class SpacecraftPerformanceAnalyzer
    {
        const float StandardAirDensity = 1.225f;
        const float StandardGravity = 9.80665f;

        public static SpacecraftPerformanceMetrics Analyze(
            ShipAssembly assembly,
            ShipHullDefinition hull)
        {
            if (assembly == null)
                return default;

            float mass = Mathf.Max(1f, assembly.Metrics.totalMass);
            Vector3 positiveForce = Vector3.zero;
            Vector3 negativeForce = Vector3.zero;
            Vector3 positiveTorque = Vector3.zero;
            Vector3 negativeTorque = Vector3.zero;
            Vector3 center = assembly.Metrics.localCenterOfMass;

            if (hull != null)
            {
                Vector3 force = hull.FlightProfile.IntegratedRcsNozzleForce
                    * 4f;
                positiveForce += force;
                negativeForce += force;
            }

            for (int index = 0; index < assembly.Thrusters.Count; index++)
            {
                ThrusterPart thruster = assembly.Thrusters[index];
                if (thruster == null)
                    continue;
                Vector3 force = assembly.transform.InverseTransformDirection(
                        thruster.ThrustDirection)
                    * thruster.ActualThrust;
                Vector3 torque = Vector3.Cross(
                    thruster.transform.localPosition - center,
                    force);
                AccumulateSigned(
                    force,
                    ref positiveForce,
                    ref negativeForce);
                AccumulateSigned(
                    torque,
                    ref positiveTorque,
                    ref negativeTorque);
            }

            float wingArea = 0f;
            float weightedLiftCoefficient = 0f;
            float baseDragArea = 0f;
            float sideStability = 0f;
            Vector3 aerodynamicCenterSum = Vector3.zero;
            float aerodynamicArea = 0f;
            for (int index = 0; index < assembly.Parts.Count; index++)
            {
                SpacecraftPart part = assembly.Parts[index];
                if (part == null || part.Definition == null)
                    continue;
                ShipAerodynamicProfile profile =
                    part.Definition.Aerodynamics;
                if (profile == null || !profile.IsEnabled)
                    continue;

                float area = profile.ReferenceArea
                    * part.UniformScale
                    * part.UniformScale;
                Vector3 localCenter = part.transform.localPosition
                    + part.transform.localRotation
                    * (profile.CenterOffset * part.UniformScale);
                aerodynamicCenterSum += localCenter * area;
                aerodynamicArea += area;
                baseDragArea += area * profile.BaseDragCoefficient;
                sideStability +=
                    area * profile.SideStabilityCoefficient;
                if (!profile.ProducesVerticalLift)
                    continue;
                wingArea += area;
                float maximumLift = Mathf.Abs(
                    profile.EvaluateLiftCoefficient(
                        profile.StallAngleDegrees));
                weightedLiftCoefficient += maximumLift * area;
            }

            ShipAerodynamicProfile hullAero =
                hull == null ? null : hull.Aerodynamics;
            if (hullAero != null && hullAero.IsEnabled)
            {
                baseDragArea += hullAero.ReferenceArea
                    * hullAero.BaseDragCoefficient;
                sideStability += hullAero.ReferenceArea
                    * hullAero.SideStabilityCoefficient;
            }

            float maximumLiftCoefficient = wingArea > 0.001f
                ? weightedLiftCoefficient / wingArea
                : 0f;
            float stallSpeed = wingArea > 0.001f
                    && maximumLiftCoefficient > 0.001f
                ? Mathf.Sqrt(
                    2f * mass * StandardGravity
                    / (StandardAirDensity
                        * wingArea
                        * maximumLiftCoefficient))
                : 0f;
            float liftToDrag = maximumLiftCoefficient > 0.001f
                    && baseDragArea > 0.001f
                ? maximumLiftCoefficient * wingArea
                    / baseDragArea
                : 0f;
            float inverseMass = 1f / mass;
            var authority = new SpacecraftDirectionalAuthority
            {
                positiveForce = positiveForce,
                negativeForce = negativeForce,
                positiveTorque = positiveTorque,
                negativeTorque = negativeTorque,
                positiveAcceleration = positiveForce * inverseMass,
                negativeAcceleration = negativeForce * inverseMass
            };
            return new SpacecraftPerformanceMetrics
            {
                totalMass = mass,
                localCenterOfMass = center,
                aerodynamicCenter = aerodynamicArea > 0.001f
                    ? aerodynamicCenterSum / aerodynamicArea
                    : Vector3.zero,
                wingArea = wingArea,
                wingLoading = wingArea > 0.001f
                    ? mass / wingArea
                    : 0f,
                estimatedStallSpeed = stallSpeed,
                estimatedLiftToDrag = liftToDrag,
                sideStability = sideStability,
                directionalAuthority = authority
            };
        }

        static void AccumulateSigned(
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
    }
}
