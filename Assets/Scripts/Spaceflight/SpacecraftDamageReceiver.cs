using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class SpacecraftDamageReceiver : MonoBehaviour, ISpaceDamageable
{
    [SerializeField, Min(1f)] float maximumIntegrity = 100f;
    [SerializeField, Min(1f)] float collisionEnergyPerDamage = 850f;
    [SerializeField, Min(0f)] float safeCollisionSpeed = 3f;
    float integrity;
    bool destroyed;

    public float Integrity => integrity;
    public float MaximumIntegrity => maximumIntegrity;
    public bool IsDestroyed => destroyed;
    public event Action<float, float, SpaceDamageInfo> Damaged;
    public event Action<SpaceDamageInfo> Destroyed;

    void Awake()
    {
        integrity = maximumIntegrity;
    }

    public void SetIntegrity(float value)
    {
        integrity = Mathf.Clamp(value, 0f, maximumIntegrity);
        destroyed = integrity <= 0f;
    }

    public void ApplyDamage(SpaceDamageInfo damage)
    {
        if (destroyed || damage.amount <= 0f || IsFriendlyDamage(damage))
            return;
        float previousIntegrity = integrity;
        integrity = Mathf.Max(0f, integrity - damage.amount);
        if (integrity < previousIntegrity)
            Damaged?.Invoke(integrity, maximumIntegrity, damage);
        if (integrity <= 0f)
        {
            destroyed = true;
            Destroyed?.Invoke(damage);
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        if (destroyed)
            return;
        AsteroidBody asteroid = collision.collider.GetComponentInParent<AsteroidBody>();
        if (asteroid == null)
            return;

        float excessSpeed = Mathf.Max(0f, collision.relativeVelocity.magnitude - safeCollisionSpeed);
        if (excessSpeed <= 0f)
            return;
        float reducedMass = Mathf.Min(GetComponent<Rigidbody>()?.mass ?? 1f, asteroid.PhysicalMass);
        float energy = 0.5f * reducedMass * excessSpeed * excessSpeed;
        ContactPoint contact = collision.contactCount > 0 ? collision.GetContact(0) : default;
        ApplyDamage(new SpaceDamageInfo(
            Mathf.Max(0.25f, energy / collisionEnergyPerDamage),
            contact.point,
            collision.impulse,
            SpaceDamageType.Collision,
            asteroid.gameObject));
        asteroid.ApplyDamage(new SpaceDamageInfo(
            Mathf.Max(1f, energy / 400f),
            contact.point,
            -collision.impulse,
            SpaceDamageType.Collision,
            gameObject));
    }

    bool IsFriendlyDamage(SpaceDamageInfo damage)
    {
        SpaceCombatant self = GetComponent<SpaceCombatant>();
        SpaceCombatant source = damage.source == null ? null : damage.source.GetComponentInParent<SpaceCombatant>();
        return self != null && source != null && self.Faction != SpaceCombatFaction.Neutral &&
               self.Faction == source.Faction;
    }
}
