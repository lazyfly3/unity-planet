using UnityEngine;

namespace UnityPlanet.ModularAssembly
{
    public sealed class NeoXCombatEffectMarker : MonoBehaviour
    {
        [Min(0.05f)] public float expectedLifetime = 1f;
        public Vector3 localEmissionAxis = Vector3.forward;
        public bool usesWorldSimulation = true;
    }
}
