using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class SpaceCombatant : MonoBehaviour, ISpaceWeaponTarget
{
    static readonly List<SpaceCombatant> ActiveCombatants = new List<SpaceCombatant>(32);

    [SerializeField] SpaceCombatFaction faction = SpaceCombatFaction.Neutral;
    [SerializeField] bool targetable = true;
    [SerializeField] Transform aimPoint;
    [SerializeField] Rigidbody attachedBody;

    ISpaceDamageable damageable;

    public SpaceCombatFaction Faction => faction;
    public SpaceWeaponTargetKind TargetKind => SpaceWeaponTargetKind.Combatant;
    public Transform TargetTransform => transform;
    public bool IsTargetable => targetable && isActiveAndEnabled && (damageable == null || !damageable.IsDestroyed);
    public Vector3 AimPosition => aimPoint == null ? transform.position : aimPoint.position;
    public Vector3 Velocity => attachedBody == null ? Vector3.zero : attachedBody.GetPointVelocity(AimPosition);
    public static IReadOnlyList<SpaceCombatant> Active => ActiveCombatants;

    void Awake()
    {
        if (attachedBody == null)
            attachedBody = GetComponentInParent<Rigidbody>();
        damageable = FindDamageable(transform);
    }

    void OnEnable()
    {
        if (!ActiveCombatants.Contains(this))
            ActiveCombatants.Add(this);
        SpaceWeaponTargetRegistry.Register(this);
    }

    void OnDisable()
    {
        ActiveCombatants.Remove(this);
        SpaceWeaponTargetRegistry.Unregister(this);
    }

    public void Configure(SpaceCombatFaction value, Transform targetPoint = null, bool canBeTargeted = true)
    {
        faction = value;
        aimPoint = targetPoint;
        targetable = canBeTargeted;
    }

    public bool IsHostileTo(SpaceCombatant other)
    {
        return other != null && faction != SpaceCombatFaction.Neutral &&
               other.faction != SpaceCombatFaction.Neutral && faction != other.faction;
    }

    public bool Owns(Transform candidate)
    {
        return candidate != null && (candidate == transform || candidate.IsChildOf(transform));
    }

    static ISpaceDamageable FindDamageable(Transform start)
    {
        Transform current = start;
        while (current != null)
        {
            var result = current.GetComponent(typeof(ISpaceDamageable)) as ISpaceDamageable;
            if (result != null)
                return result;
            current = current.parent;
        }
        return null;
    }
}
