using System;
using UnityEngine;

namespace SpacecraftEditor
{
    public enum SpacecraftPartCategory
    {
        Decoration,
        Thruster,
        Weapon
    }

    public enum SpacecraftPartScaleMode
    {
        Free,
        Fixed
    }

    public enum SpacecraftPartPlacementMode
    {
        SurfaceConforming,
        LateralWing
    }

    public enum SpaceWeaponDamageChannel
    {
        Kinetic,
        Energy,
        Distortion,
        Explosive
    }

    public enum SpaceWeaponFireMode
    {
        Repeater,
        Cannon,
        Scatter,
        Beam,
        Missile
    }

    public enum SpaceWeaponMountSize
    {
        S1 = 1,
        S2 = 2,
        S3 = 3
    }

    public enum SpaceWeaponMountMode
    {
        Fixed,
        Gimbaled
    }

    [Serializable]
    public sealed class SpacecraftWeaponDefinition
    {
        [SerializeField] private SpaceWeaponDamageChannel damageChannel = SpaceWeaponDamageChannel.Kinetic;
        [SerializeField] private SpaceWeaponFireMode fireMode = SpaceWeaponFireMode.Repeater;
        [SerializeField] private SpaceWeaponMountSize mountSize = SpaceWeaponMountSize.S1;
        [SerializeField] private SpaceWeaponMountMode mountMode = SpaceWeaponMountMode.Fixed;
        [SerializeField, Range(1f, 45f)] private float gimbalConeDegrees = 18f;
        [SerializeField, Min(1f)] private float gimbalTrackingDegreesPerSecond = 120f;
        [SerializeField, Range(1, 2)] private int defaultFireGroup = 1;
        [SerializeField, Min(0.1f)] private float roundsPerSecond = 8f;
        [SerializeField, Min(0.1f)] private float damage = 6f;
        [SerializeField, Min(1f)] private float projectileSpeed = 420f;
        [SerializeField, Min(1f)] private float range = 1600f;
        [SerializeField, Range(0f, 8f)] private float spreadDegrees = 0.25f;
        [SerializeField, Min(0)] private int ammunitionCapacity = 240;
        [SerializeField, Min(0f)] private float capacitorCost;
        [SerializeField, Min(0f)] private float heatPerShot = 0.045f;
        [SerializeField, Min(0f)] private float heatDissipation = 0.22f;
        [SerializeField, Min(0f)] private float recoilImpulse = 0.45f;
        [SerializeField, Min(0f)] private float impactImpulse = 5f;

        public SpaceWeaponDamageChannel DamageChannel => damageChannel;
        public SpaceWeaponFireMode FireMode => fireMode;
        public SpaceWeaponMountSize MountSize => mountSize;
        public SpaceWeaponMountMode MountMode => mountMode;
        public float GimbalConeDegrees => Mathf.Clamp(gimbalConeDegrees, 1f, 45f);
        public float GimbalTrackingDegreesPerSecond => Mathf.Max(1f, gimbalTrackingDegreesPerSecond);
        public int DefaultFireGroup => Mathf.Clamp(defaultFireGroup, 1, 2);
        public float RoundsPerSecond => roundsPerSecond;
        public float Damage => damage;
        public float ProjectileSpeed => projectileSpeed;
        public float Range => range;
        public float SpreadDegrees => spreadDegrees;
        public int AmmunitionCapacity => ammunitionCapacity;
        public float CapacitorCost => capacitorCost;
        public float HeatPerShot => heatPerShot;
        public float HeatDissipation => heatDissipation;
        public float RecoilImpulse => recoilImpulse;
        public float ImpactImpulse => impactImpulse;
        public bool UsesAmmunition => ammunitionCapacity > 0;
        public bool UsesCapacitor => capacitorCost > 0f;

#if UNITY_EDITOR
        public void Configure(
            SpaceWeaponDamageChannel channel,
            SpaceWeaponFireMode mode,
            SpaceWeaponMountSize size,
            int fireGroup,
            float fireRate,
            float shotDamage,
            float shotSpeed,
            float shotRange,
            float spread,
            int ammunition,
            float energyCost,
            float shotHeat,
            float cooling,
            float recoil,
            float impulse,
            SpaceWeaponMountMode weaponMountMode = SpaceWeaponMountMode.Fixed,
            float trackingCone = 18f,
            float trackingSpeed = 120f)
        {
            damageChannel = channel;
            fireMode = mode;
            mountSize = size;
            defaultFireGroup = Mathf.Clamp(fireGroup, 1, 2);
            roundsPerSecond = Mathf.Max(0.1f, fireRate);
            damage = Mathf.Max(0.1f, shotDamage);
            projectileSpeed = Mathf.Max(1f, shotSpeed);
            range = Mathf.Max(1f, shotRange);
            spreadDegrees = Mathf.Clamp(spread, 0f, 8f);
            ammunitionCapacity = Mathf.Max(0, ammunition);
            capacitorCost = Mathf.Max(0f, energyCost);
            heatPerShot = Mathf.Max(0f, shotHeat);
            heatDissipation = Mathf.Max(0f, cooling);
            recoilImpulse = Mathf.Max(0f, recoil);
            impactImpulse = Mathf.Max(0f, impulse);
            mountMode = weaponMountMode;
            gimbalConeDegrees = Mathf.Clamp(trackingCone, 1f, 45f);
            gimbalTrackingDegreesPerSecond = Mathf.Max(1f, trackingSpeed);
        }
#endif
    }
}
