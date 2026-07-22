using System;
using UnityEngine;

namespace SpacecraftEditor
{
    [Serializable]
    public sealed class SpacecraftHardpoint
    {
        [SerializeField] string hardpointId;
        [SerializeField] string mirrorHardpointId;
        [SerializeField] SpacecraftPartCategory category;
        [SerializeField] Vector3 localPosition;
        [SerializeField] Vector3 localEulerAngles;
        [SerializeField] SpaceWeaponMountSize minimumWeaponSize = SpaceWeaponMountSize.S1;
        [SerializeField] SpaceWeaponMountSize maximumWeaponSize = SpaceWeaponMountSize.S3;
        [SerializeField] bool allowsFixedWeapons = true;
        [SerializeField] bool allowsGimbaledWeapons = true;

        public string HardpointId => hardpointId;
        public string MirrorHardpointId => mirrorHardpointId;
        public SpacecraftPartCategory Category => category;
        public Vector3 LocalPosition => localPosition;
        public Quaternion LocalRotation => Quaternion.Euler(localEulerAngles);
        public SpaceWeaponMountSize MinimumWeaponSize => minimumWeaponSize;
        public SpaceWeaponMountSize MaximumWeaponSize => maximumWeaponSize;
        public bool AllowsFixedWeapons => allowsFixedWeapons;
        public bool AllowsGimbaledWeapons => allowsGimbaledWeapons;

        public bool Accepts(ShipPartDefinition definition)
        {
            if (definition == null || definition.Category != category)
                return false;
            if (category != SpacecraftPartCategory.Weapon || definition.Weapon == null)
                return true;
            int size = (int)definition.Weapon.MountSize;
            if (size < (int)minimumWeaponSize || size > (int)maximumWeaponSize)
                return false;
            return definition.Weapon.MountMode == SpaceWeaponMountMode.Gimbaled
                ? allowsGimbaledWeapons
                : allowsFixedWeapons;
        }

#if UNITY_EDITOR
        public void Configure(
            string id,
            string mirrorId,
            SpacecraftPartCategory partCategory,
            Vector3 position,
            Vector3 euler,
            SpaceWeaponMountSize minimumSize = SpaceWeaponMountSize.S1,
            SpaceWeaponMountSize maximumSize = SpaceWeaponMountSize.S3,
            bool fixedWeapons = true,
            bool gimbaledWeapons = true)
        {
            hardpointId = id;
            mirrorHardpointId = mirrorId;
            category = partCategory;
            localPosition = position;
            localEulerAngles = euler;
            minimumWeaponSize = minimumSize;
            maximumWeaponSize = maximumSize;
            allowsFixedWeapons = fixedWeapons;
            allowsGimbaledWeapons = gimbaledWeapons;
        }
#endif
    }

    [Serializable]
    public sealed class SpacecraftDecorationRegion
    {
        [SerializeField] string regionId;
        [SerializeField] Vector3 localCenter;
        [SerializeField] Vector3 localExtents = Vector3.one;
        [SerializeField] Vector3 localEulerAngles;

        public string RegionId => regionId;
        public Vector3 LocalCenter => localCenter;
        public Vector3 LocalExtents => localExtents;
        public Quaternion LocalRotation => Quaternion.Euler(localEulerAngles);

#if UNITY_EDITOR
        public void Configure(string id, Vector3 center, Vector3 extents, Vector3 euler)
        {
            regionId = id;
            localCenter = center;
            localExtents = new Vector3(
                Mathf.Max(0.05f, extents.x),
                Mathf.Max(0.05f, extents.y),
                Mathf.Max(0.05f, extents.z));
            localEulerAngles = euler;
        }
#endif
    }

    [CreateAssetMenu(menuName = "Spacecraft/Hardpoint Layout", fileName = "SpacecraftHardpointLayout")]
    public sealed class SpacecraftHardpointLayout : ScriptableObject
    {
        [SerializeField] string hullId;
        [SerializeField] SpacecraftHardpoint[] hardpoints = Array.Empty<SpacecraftHardpoint>();
        [SerializeField] SpacecraftDecorationRegion[] decorationRegions = Array.Empty<SpacecraftDecorationRegion>();
        [SerializeField, Min(0.1f)] float minimumForwardAcceleration = 1.5f;
        [SerializeField, Range(0f, 0.5f)] float maximumLateralMassImbalance = 0.08f;

        public string HullId => hullId;
        public SpacecraftHardpoint[] Hardpoints => hardpoints;
        public SpacecraftDecorationRegion[] DecorationRegions => decorationRegions;
        public float MinimumForwardAcceleration => minimumForwardAcceleration;
        public float MaximumLateralMassImbalance => maximumLateralMassImbalance;

#if UNITY_EDITOR
        public void Configure(
            string targetHullId,
            SpacecraftHardpoint[] points,
            SpacecraftDecorationRegion[] regions,
            float minimumAcceleration,
            float maximumImbalance)
        {
            hullId = targetHullId;
            hardpoints = points ?? Array.Empty<SpacecraftHardpoint>();
            decorationRegions = regions ?? Array.Empty<SpacecraftDecorationRegion>();
            minimumForwardAcceleration = Mathf.Max(0.1f, minimumAcceleration);
            maximumLateralMassImbalance = Mathf.Clamp(maximumImbalance, 0f, 0.5f);
        }
#endif
    }
}
