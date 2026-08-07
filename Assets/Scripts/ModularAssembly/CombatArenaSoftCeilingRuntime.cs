using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityPlanet.ModularAssembly
{
    /// <summary>
    /// Makes ICombatArenaProvider.FlightCeiling an actual combat constraint.
    /// It adds acceleration only to the assembled ship Rigidbody and therefore
    /// does not rebuild, parent, detach, or otherwise alter modular structure.
    /// </summary>
    [DefaultExecutionOrder(9001)]
    [DisallowMultipleComponent]
    public sealed class CombatArenaSoftCeilingRuntime : MonoBehaviour
    {
        const float SoftBand = 72f;
        const float MaximumAcceleration = 84f;
        const float UpwardVelocityDamping = 2.1f;

        FlightEnvironmentManager environmentManager;
        CombatTestController combatController;
        global::GridFlightBridge flightBridge;

        public bool IsApplyingConstraint { get; private set; }
        public float CurrentCeiling { get; private set; }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!ModularLabSceneProfile.AllowsCombatTest(scene) ||
                FindObjectOfType<CombatArenaSoftCeilingRuntime>() != null)
            {
                return;
            }

            var host = new GameObject("CombatArenaSoftCeilingRuntime");
            SceneManager.MoveGameObjectToScene(host, scene);
            host.AddComponent<CombatArenaSoftCeilingRuntime>();
        }

        IEnumerator Start()
        {
            for (int frame = 0; frame < 180; frame++)
            {
                if (ResolveReferences())
                    yield break;
                yield return null;
            }
        }

        void FixedUpdate()
        {
            IsApplyingConstraint = false;
            if (environmentManager == null ||
                combatController == null ||
                flightBridge == null ||
                combatController.SessionKind !=
                    GridFlightSessionKind.CombatTest ||
                !flightBridge.IsFlying)
            {
                return;
            }

            ICombatArenaProvider arena = environmentManager.ActiveArena;
            Rigidbody body = flightBridge.Body;
            if (arena == null || body == null || body.isKinematic)
                return;

            CurrentCeiling = arena.FlightCeiling;
            float acceleration = CalculateSoftCeilingAcceleration(
                body.position.y,
                CurrentCeiling,
                SoftBand,
                MaximumAcceleration);
            if (acceleration <= 0f)
                return;

            float upwardSpeed = Mathf.Max(0f, body.velocity.y);
            body.AddForce(
                Vector3.down *
                (acceleration + upwardSpeed * UpwardVelocityDamping),
                ForceMode.Acceleration);
            IsApplyingConstraint = true;
        }

        bool ResolveReferences()
        {
            if (environmentManager == null)
            {
                environmentManager =
                    FindObjectOfType<FlightEnvironmentManager>(true);
            }
            if (combatController == null)
            {
                combatController =
                    FindObjectOfType<CombatTestController>(true);
            }
            if (flightBridge == null)
            {
                flightBridge =
                    FindObjectOfType<global::GridFlightBridge>(true);
            }
            return environmentManager != null &&
                   combatController != null &&
                   flightBridge != null;
        }

        public static float CalculateSoftCeilingAcceleration(
            float height,
            float ceiling,
            float softBand = SoftBand,
            float maximumAcceleration = MaximumAcceleration)
        {
            float band = Mathf.Max(1f, softBand);
            float normalized = Mathf.InverseLerp(
                ceiling - band,
                ceiling,
                height);
            normalized = normalized * normalized *
                         (3f - 2f * normalized);
            float aboveCeiling = Mathf.Max(0f, height - ceiling);
            return normalized * Mathf.Max(0f, maximumAcceleration) +
                   aboveCeiling * 0.55f;
        }
    }
}
