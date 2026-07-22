using UnityEngine;
using SpacecraftEditor;

public enum SpaceDamageType
{
    Collision,
    Projectile,
    Explosion
}

public readonly struct SpaceDamageInfo
{
    public readonly float amount;
    public readonly Vector3 point;
    public readonly Vector3 impulse;
    public readonly SpaceDamageType type;
    public readonly SpaceWeaponDamageChannel channel;
    public readonly GameObject source;

    public SpaceDamageInfo(
        float amount,
        Vector3 point,
        Vector3 impulse,
        SpaceDamageType type,
        GameObject source)
        : this(amount, point, impulse, type, SpaceWeaponDamageChannel.Kinetic, source)
    {
    }

    public SpaceDamageInfo(
        float amount,
        Vector3 point,
        Vector3 impulse,
        SpaceDamageType type,
        SpaceWeaponDamageChannel channel,
        GameObject source)
    {
        this.amount = Mathf.Max(0f, amount);
        this.point = point;
        this.impulse = impulse;
        this.type = type;
        this.channel = channel;
        this.source = source;
    }
}

public interface ISpaceDamageable
{
    float Integrity { get; }
    float MaximumIntegrity { get; }
    bool IsDestroyed { get; }
    void ApplyDamage(SpaceDamageInfo damage);
}
