using System.Collections.Generic;
using UnityEngine;

namespace UnityPlanet.ModularAssembly
{
    public interface IVehicleExternalForceSink
    {
        void QueueExternalImpulse(Vector3 worldImpulse, Vector3 worldPosition);
    }

    public interface IVehicleCurrentPhysicsStepImpulseSink
    {
        bool TryApplyExternalImpulseInCurrentPhysicsStep(
            Vector3 worldImpulse,
            Vector3 worldPosition);
    }

    public static class VehicleExternalForces
    {
        public static void ApplyImpulse(
            Rigidbody target,
            Vector3 worldImpulse,
            Vector3 worldPosition)
        {
            if (target == null ||
                !VehicleWholeBodyAudit.Finite(worldImpulse) ||
                !VehicleWholeBodyAudit.Finite(worldPosition))
                return;

            IVehicleExternalForceSink sink =
                target.GetComponent<IVehicleExternalForceSink>();
            if (sink != null)
            {
                sink.QueueExternalImpulse(worldImpulse, worldPosition);
                return;
            }

            if (!target.isKinematic)
            {
                target.AddForceAtPosition(
                    worldImpulse,
                    worldPosition,
                    ForceMode.Impulse);
            }
        }

        public static void ApplyImpulseInCurrentPhysicsStep(
            Rigidbody target,
            Vector3 worldImpulse,
            Vector3 worldPosition)
        {
            if (target == null ||
                !VehicleWholeBodyAudit.Finite(worldImpulse) ||
                !VehicleWholeBodyAudit.Finite(worldPosition))
            {
                return;
            }

            IVehicleCurrentPhysicsStepImpulseSink sink =
                target.GetComponent<
                    IVehicleCurrentPhysicsStepImpulseSink>();
            if (sink != null &&
                sink.TryApplyExternalImpulseInCurrentPhysicsStep(
                    worldImpulse,
                    worldPosition))
            {
                return;
            }

            if (!target.isKinematic)
            {
                target.AddForceAtPosition(
                    worldImpulse,
                    worldPosition,
                    ForceMode.Impulse);
            }
        }
    }

    public struct VehicleMassProperties
    {
        public float totalMass;
        public Vector3 centerOfMassLocal;
        public Vector3 inertiaTensor;
        public Quaternion inertiaTensorRotation;
    }

    public sealed class VehicleForceLedger
    {
        struct ExternalImpulseState
        {
            public Vector3 linearImpulse;
            public Vector3 angularImpulse;
        }

        Rigidbody body;
        Vector3 force;
        Vector3 torque;
        bool applied;
        readonly Dictionary<Rigidbody, ExternalImpulseState>
            externalImpulses =
                new Dictionary<Rigidbody, ExternalImpulseState>();

        public Vector3 TotalForce => force;
        public Vector3 TotalTorque => torque;

        public void Begin(Rigidbody target)
        {
            body = target;
            force = Vector3.zero;
            torque = Vector3.zero;
            applied = false;
            externalImpulses.Clear();
        }

        public void AddForce(Vector3 worldForce)
        {
            if (applied)
                return;
            force += worldForce;
        }

        public void AddAcceleration(Vector3 worldAcceleration)
        {
            if (!applied && body != null)
                force += worldAcceleration * body.mass;
        }

        public void AddTorque(Vector3 worldTorque)
        {
            if (applied)
                return;
            torque += worldTorque;
        }

        public void AddForceAtPosition(
            Vector3 worldForce,
            Vector3 worldPosition)
        {
            if (applied)
                return;
            force += worldForce;
            if (body != null)
            {
                torque += Vector3.Cross(
                    worldPosition - body.worldCenterOfMass,
                    worldForce);
            }
        }

        public void AddForceAtPoint(
            Vector3 worldForce,
            Vector3 worldPosition)
        {
            AddForceAtPosition(worldForce, worldPosition);
        }

        public bool AddImpulseAtPosition(
            Vector3 worldImpulse,
            Vector3 worldPosition,
            float step)
        {
            if (applied ||
                body == null ||
                !VehicleWholeBodyAudit.Finite(worldImpulse) ||
                !VehicleWholeBodyAudit.Finite(worldPosition) ||
                !VehicleWholeBodyAudit.Finite(step) ||
                step <= 0f)
            {
                return false;
            }

            AccumulateImpulseForAudit(
                worldImpulse,
                worldPosition,
                step);
            return true;
        }

        public bool RecordAppliedImpulseAtPosition(
            Vector3 worldImpulse,
            Vector3 worldPosition,
            float step)
        {
            if (!applied ||
                body == null ||
                !VehicleWholeBodyAudit.Finite(worldImpulse) ||
                !VehicleWholeBodyAudit.Finite(worldPosition) ||
                !VehicleWholeBodyAudit.Finite(step) ||
                step <= 0f)
            {
                return false;
            }

            AccumulateImpulseForAudit(
                worldImpulse,
                worldPosition,
                step);
            return true;
        }

        public void AddExternalImpulse(
            Rigidbody target,
            Vector3 worldImpulse,
            Vector3 worldPosition)
        {
            if (applied ||
                target == null ||
                target == body ||
                target.isKinematic ||
                !VehicleWholeBodyAudit.Finite(worldImpulse) ||
                !VehicleWholeBodyAudit.Finite(worldPosition))
            {
                return;
            }

            externalImpulses.TryGetValue(
                target,
                out ExternalImpulseState state);
            state.linearImpulse += worldImpulse;
            state.angularImpulse += Vector3.Cross(
                worldPosition - target.worldCenterOfMass,
                worldImpulse);
            externalImpulses[target] = state;
            VehicleExternalForces.ApplyImpulseInCurrentPhysicsStep(
                target,
                worldImpulse,
                worldPosition);
        }

        public Vector3 PredictedExternalPointVelocity(
            Rigidbody target,
            Vector3 worldPosition)
        {
            if (target == null)
                return Vector3.zero;

            Vector3 result = target.GetPointVelocity(worldPosition);
            if (target.isKinematic ||
                !externalImpulses.TryGetValue(
                    target,
                    out ExternalImpulseState state))
            {
                return result;
            }

            Vector3 linearDelta =
                state.linearImpulse /
                Mathf.Max(0.01f, target.mass);
            Vector3 angularDelta = ApplyWorldInverseInertia(
                target,
                state.angularImpulse);
            return result +
                   linearDelta +
                   Vector3.Cross(
                       angularDelta,
                       worldPosition - target.worldCenterOfMass);
        }

        public bool Apply()
        {
            if (applied ||
                body == null ||
                body.isKinematic)
                return false;
            if (!VehicleWholeBodyAudit.Finite(force) ||
                !VehicleWholeBodyAudit.Finite(torque))
            {
                Debug.LogError(
                    "RC3.2 rejected a non-finite whole-vehicle force result.");
                return false;
            }
            body.AddForce(force, ForceMode.Force);
            body.AddTorque(torque, ForceMode.Force);
            applied = true;
            return true;
        }

        void AccumulateImpulseForAudit(
            Vector3 worldImpulse,
            Vector3 worldPosition,
            float step)
        {
            float inverseStep = 1f / Mathf.Max(0.0001f, step);
            force += worldImpulse * inverseStep;
            torque += Vector3.Cross(
                worldPosition - body.worldCenterOfMass,
                worldImpulse) * inverseStep;
        }

        static Vector3 ApplyWorldInverseInertia(
            Rigidbody target,
            Vector3 value)
        {
            Quaternion inertiaWorldRotation =
                target.rotation * target.inertiaTensorRotation;
            Vector3 local =
                Quaternion.Inverse(inertiaWorldRotation) * value;
            Vector3 inertia = target.inertiaTensor;
            local = new Vector3(
                local.x / Mathf.Max(0.01f, inertia.x),
                local.y / Mathf.Max(0.01f, inertia.y),
                local.z / Mathf.Max(0.01f, inertia.z));
            return inertiaWorldRotation * local;
        }
    }

    public struct VehicleWholeBodyAuditSnapshot
    {
        public Vector3 netForceWorld;
        public Vector3 netTorqueWorld;
        public Vector3 predictedLinearAccelerationWorld;
        public Vector3 predictedAngularAccelerationWorld;
        public bool ownershipExclusive;
        public bool gravityExclusive;
        public bool nativeDragDisabled;
        public bool inertiaValid;
        public bool finite;
        public bool valid;
        public string message;
    }

    public static class VehicleWholeBodyAudit
    {
        public static VehicleWholeBodyAuditSnapshot Evaluate(
            Rigidbody body,
            VehicleForceLedger ledger,
            bool ownershipExclusive,
            bool inertiaTriangleValid)
        {
            VehicleWholeBodyAuditSnapshot result =
                new VehicleWholeBodyAuditSnapshot
                {
                    ownershipExclusive = ownershipExclusive,
                    inertiaValid = inertiaTriangleValid
                };
            if (body == null || ledger == null)
            {
                result.message = "RC3.2 whole-body audit has no body or ledger.";
                return result;
            }

            result.netForceWorld = ledger.TotalForce;
            result.netTorqueWorld = ledger.TotalTorque;
            result.gravityExclusive = !body.useGravity;
            result.nativeDragDisabled =
                Mathf.Abs(body.drag) <= 0.0001f &&
                Mathf.Abs(body.angularDrag) <= 0.0001f;

            bool massValid = Finite(body.mass) && body.mass > 0f;
            bool tensorValid =
                Finite(body.inertiaTensor) &&
                body.inertiaTensor.x > 0f &&
                body.inertiaTensor.y > 0f &&
                body.inertiaTensor.z > 0f;
            result.finite =
                massValid &&
                tensorValid &&
                Finite(result.netForceWorld) &&
                Finite(result.netTorqueWorld);

            if (massValid)
            {
                result.predictedLinearAccelerationWorld =
                    result.netForceWorld / body.mass;
            }
            if (tensorValid)
            {
                Quaternion principalWorld =
                    body.rotation * body.inertiaTensorRotation;
                Vector3 principalTorque =
                    Quaternion.Inverse(principalWorld) *
                    result.netTorqueWorld;
                Vector3 principalAngularVelocity =
                    Quaternion.Inverse(principalWorld) *
                    body.angularVelocity;
                Vector3 principalAngularMomentum = Vector3.Scale(
                    body.inertiaTensor,
                    principalAngularVelocity);
                Vector3 gyroscopicTorque = Vector3.Cross(
                    principalAngularVelocity,
                    principalAngularMomentum);
                Vector3 effectiveTorque =
                    principalTorque - gyroscopicTorque;
                Vector3 principalAcceleration = new Vector3(
                    effectiveTorque.x / body.inertiaTensor.x,
                    effectiveTorque.y / body.inertiaTensor.y,
                    effectiveTorque.z / body.inertiaTensor.z);
                result.predictedAngularAccelerationWorld =
                    principalWorld * principalAcceleration;
            }

            result.valid =
                result.finite &&
                result.ownershipExclusive &&
                result.gravityExclusive &&
                result.nativeDragDisabled &&
                result.inertiaValid &&
                Finite(result.predictedLinearAccelerationWorld) &&
                Finite(result.predictedAngularAccelerationWorld);
            result.message = result.valid
                ? $"RC3.2 whole-body closure valid; " +
                  $"|a|={result.predictedLinearAccelerationWorld.magnitude:0.00} m/s2, " +
                  $"|alpha|={result.predictedAngularAccelerationWorld.magnitude:0.00} rad/s2."
                : BuildFailureMessage(result);
            return result;
        }

        static string BuildFailureMessage(
            VehicleWholeBodyAuditSnapshot value)
        {
            if (!value.ownershipExclusive)
                return "RC3.2 has more than one physics owner.";
            if (!value.gravityExclusive)
                return "RC3.2 Rigidbody gravity is enabled in addition to ledger gravity.";
            if (!value.nativeDragDisabled)
                return "RC3.2 Rigidbody drag conflicts with module aerodynamics.";
            if (!value.inertiaValid)
                return "RC3.2 inertia tensor failed whole-body validation.";
            return "RC3.2 whole-body force or acceleration is non-finite.";
        }

        public static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public static bool Finite(Vector3 value)
        {
            return Finite(value.x) && Finite(value.y) && Finite(value.z);
        }
    }
}
