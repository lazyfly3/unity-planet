using System.Collections.Generic;
using UnityEngine;

public enum SpaceCombatFaction
{
    Neutral,
    Player,
    Pirate
}

public enum SpaceWeaponTargetKind
{
    None,
    Combatant,
    Asteroid
}

public interface ISpaceWeaponTarget
{
    SpaceWeaponTargetKind TargetKind { get; }
    Transform TargetTransform { get; }
    Vector3 AimPosition { get; }
    Vector3 Velocity { get; }
    bool IsTargetable { get; }
    bool Owns(Transform candidate);
}

public static class SpaceWeaponTargetRegistry
{
    static readonly List<ISpaceWeaponTarget> Targets = new List<ISpaceWeaponTarget>(256);

    public static IReadOnlyList<ISpaceWeaponTarget> Active => Targets;
    public static int RegisteredCount => Targets.Count;

    public static void Register(ISpaceWeaponTarget target)
    {
        if (!IsUnityObjectAlive(target) || Targets.Contains(target))
            return;
        Targets.Add(target);
    }

    public static void Unregister(ISpaceWeaponTarget target)
    {
        if (target != null)
            Targets.Remove(target);
    }

    public static void RemoveInvalidEntries()
    {
        for (int index = Targets.Count - 1; index >= 0; index--)
        {
            if (!IsUnityObjectAlive(Targets[index]))
                Targets.RemoveAt(index);
        }
    }

    public static bool IsUnityObjectAlive(ISpaceWeaponTarget target)
    {
        if (target == null)
            return false;
        return target is Object unityObject && unityObject != null;
    }
}

public readonly struct SpacecraftFireCommand
{
    public readonly int selectedGroup;
    public readonly bool fireHeld;
    public readonly bool targetLockEnabled;

    public SpacecraftFireCommand(int selectedGroup, bool fireHeld, bool targetLockEnabled = true)
    {
        this.selectedGroup = Mathf.Clamp(selectedGroup, 1, 2);
        this.fireHeld = fireHeld;
        this.targetLockEnabled = targetLockEnabled;
    }
}

public interface ISpacecraftWeaponCommandSource
{
    SpacecraftFireCommand FireCommand { get; }
}
