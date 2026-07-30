using System;
using System.Collections;
using UnityEngine;

namespace UnityPlanet.ModularAssembly
{
    /// <summary>
    /// Scene-scoped provider that prepares an environment before the shared
    /// Modular Lab flight bridge releases the player Rigidbody.
    /// </summary>
    public interface IGridFlightEnvironment
    {
        int Priority { get; }
        Quaternion PreparedRotation { get; }

        IEnumerator PrepareFlight(
            Rigidbody target,
            Action<bool, Vector3, string> completed);

        IEnumerator ResetFlight(
            Rigidbody target,
            Action<bool, Vector3> completed);

        void ExitFlight();
    }
}
