using System;

namespace UnityPlanet.ModularAssembly
{
    public interface IGridFlightSession
    {
        GridFlightState State { get; }
        bool IsFlying { get; }
        event Action<GridFlightState, string> StateChanged;
        void ExitFlight();
    }
}
