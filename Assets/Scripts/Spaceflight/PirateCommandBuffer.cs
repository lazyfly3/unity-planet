using SpacecraftEditor;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PirateCommandBuffer : MonoBehaviour, ISpacecraftFlightCommandSource, ISpacecraftWeaponCommandSource
{
    SpacecraftFlightCommand flightCommand;
    SpacecraftFireCommand fireCommand = new SpacecraftFireCommand(1, false, true);

    public SpacecraftFlightCommand Command => flightCommand;
    public SpacecraftFireCommand FireCommand => fireCommand;

    public void SetFlightCommand(SpacecraftFlightCommand value)
    {
        flightCommand = value;
    }

    public void SetFireCommand(SpacecraftFireCommand value)
    {
        fireCommand = value;
    }

    public void Clear()
    {
        flightCommand = default;
        fireCommand = new SpacecraftFireCommand(
            fireCommand.selectedGroup,
            false,
            fireCommand.targetLockEnabled);
    }
}
