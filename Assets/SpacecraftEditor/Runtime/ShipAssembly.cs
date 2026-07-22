using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpacecraftEditor
{
    [DisallowMultipleComponent]
    public sealed class ShipAssembly : MonoBehaviour
    {
        [SerializeField] private Rigidbody shipBody;
        [SerializeField] private Transform partsRoot;
        [SerializeField] private PartCatalog catalog;
        [SerializeField] private float hullMass = 100f;

        private readonly List<SpacecraftPart> parts = new List<SpacecraftPart>();
        private readonly List<ThrusterPart> thrusters = new List<ThrusterPart>();
        private readonly List<WeaponPart> weapons = new List<WeaponPart>();
        private AssemblyMetrics metrics;

        public event Action AssemblyChanged;
        public Rigidbody ShipBody => shipBody;
        public Transform PartsRoot => partsRoot;
        public IReadOnlyList<SpacecraftPart> Parts => parts;
        public IReadOnlyList<ThrusterPart> Thrusters => thrusters;
        public IReadOnlyList<WeaponPart> Weapons => weapons;
        public AssemblyMetrics Metrics => metrics;
        public float HullMass => hullMass;

        private void Awake()
        {
            if (shipBody == null)
                shipBody = GetComponent<Rigidbody>();
            if (partsRoot == null)
                partsRoot = transform.Find("Parts");
            if (catalog == null)
                catalog = FindObjectOfType<PartCatalog>();
            RefreshPartList();
            Recalculate();
        }

        public void Configure(Rigidbody body, Transform root, PartCatalog partCatalog, float baseHullMass = 100f)
        {
            shipBody = body;
            partsRoot = root;
            catalog = partCatalog;
            hullMass = baseHullMass;
            RefreshPartList();
            Recalculate();
        }

        public void SetHullMass(float value)
        {
            hullMass = Mathf.Max(0.01f, value);
            Recalculate();
        }

        public SpacecraftPart AddGenericPart(
            ShipPartDefinition definition,
            Vector3 localPosition,
            Quaternion localRotation,
            float scale,
            string runtimeId = null,
            string mirrorGroupId = null,
            KeyCode activationKey = KeyCode.None,
            string materialId = null,
            int weaponGroup = 0)
        {
            if (definition == null || definition.Prefab == null || partsRoot == null)
                return null;

            var instance = Instantiate(definition.Prefab, partsRoot);
            instance.name = definition.DisplayName + "_" + (parts.Count + 1).ToString("00");
            instance.transform.localPosition = localPosition;
            instance.transform.localRotation = localRotation;

            SpacecraftPart part = ResolvePartComponent(instance, definition.Category);
            part.Configure(
                definition,
                scale,
                runtimeId,
                mirrorGroupId,
                activationKey,
                materialId,
                weaponGroup);
            parts.Add(part);
            IndexPart(part);
            Recalculate();
            return part;
        }

        // Compatibility entry point for existing thruster construction code.
        public ThrusterPart AddPart(
            ShipPartDefinition definition,
            Vector3 localPosition,
            Quaternion localRotation,
            float scale,
            string runtimeId = null,
            string mirrorGroupId = null,
            KeyCode activationKey = KeyCode.None)
        {
            return AddGenericPart(
                definition,
                localPosition,
                localRotation,
                scale,
                runtimeId,
                mirrorGroupId,
                activationKey) as ThrusterPart;
        }

        public void RemovePart(SpacecraftPart part)
        {
            if (part == null)
                return;
            UnindexPart(part);
            parts.Remove(part);
            Destroy(part.gameObject);
            Recalculate();
        }

        public void RemovePartAndMirror(SpacecraftPart part)
        {
            if (part == null)
                return;
            string groupId = part.MirrorGroupId;
            for (int index = parts.Count - 1; index >= 0; index--)
            {
                SpacecraftPart candidate = parts[index];
                if (candidate == part || (!string.IsNullOrEmpty(groupId) && candidate.MirrorGroupId == groupId))
                {
                    parts.RemoveAt(index);
                    UnindexPart(candidate);
                    if (candidate != null)
                        Destroy(candidate.gameObject);
                }
            }
            Recalculate();
        }

        public SpacecraftPart FindPartByRuntimeId(string runtimeId)
        {
            return parts.Find(part => part != null && part.RuntimeId == runtimeId);
        }

        public ThrusterPart FindByRuntimeId(string runtimeId)
        {
            return FindPartByRuntimeId(runtimeId) as ThrusterPart;
        }

        public SpacecraftPart FindMirrorMate(SpacecraftPart source)
        {
            if (source == null || string.IsNullOrEmpty(source.MirrorGroupId))
                return null;
            return parts.Find(part => part != null && part != source && part.MirrorGroupId == source.MirrorGroupId);
        }

        public List<PlacedPartState> CaptureStates()
        {
            var result = new List<PlacedPartState>(parts.Count);
            foreach (SpacecraftPart part in parts)
            {
                if (part != null && part.Definition != null)
                    result.Add(part.CaptureState());
            }
            return result;
        }

        public void RestoreStates(IReadOnlyList<PlacedPartState> states)
        {
            foreach (SpacecraftPart part in parts)
            {
                if (part != null)
                    Destroy(part.gameObject);
            }
            parts.Clear();
            thrusters.Clear();
            weapons.Clear();

            if (states != null && catalog != null)
            {
                foreach (PlacedPartState state in states)
                {
                    ShipPartDefinition definition = catalog.Find(state.partId);
                    if (definition != null)
                    {
                        float scale = definition.IsScalable ? state.uniformScale : definition.FixedScale;
                        AddGenericPart(
                            definition,
                            state.localPosition,
                            state.localRotation,
                            scale,
                            state.runtimeId,
                            state.mirrorGroupId,
                            state.activationKey,
                            state.materialId,
                            state.weaponGroup);
                    }
                }
            }
            Recalculate();
        }

        public void Recalculate()
        {
            parts.RemoveAll(part => part == null);
            thrusters.RemoveAll(part => part == null || !parts.Contains(part));
            weapons.RemoveAll(part => part == null || !parts.Contains(part));

            float totalMass = hullMass;
            Vector3 weightedCenter = Vector3.zero;
            foreach (SpacecraftPart part in parts)
            {
                float mass = part.ActualMass;
                totalMass += mass;
                weightedCenter += part.transform.localPosition * mass;
            }

            Vector3 center = totalMass > 0.001f ? weightedCenter / totalMass : Vector3.zero;
            Vector3 force = Vector3.zero;
            Vector3 torque = Vector3.zero;
            foreach (ThrusterPart thruster in thrusters)
            {
                Vector3 localForce = transform.InverseTransformDirection(thruster.ThrustDirection) * thruster.ActualThrust;
                force += localForce;
                torque += Vector3.Cross(thruster.transform.localPosition - center, localForce);
            }

            metrics = new AssemblyMetrics
            {
                totalMass = totalMass,
                localCenterOfMass = center,
                localResultantForce = force,
                localResultantTorque = torque
            };

            if (shipBody != null)
            {
                shipBody.mass = Mathf.Max(0.01f, totalMass);
                shipBody.centerOfMass = center;
                shipBody.ResetInertiaTensor();
            }
            AssemblyChanged?.Invoke();
        }

        private void RefreshPartList()
        {
            parts.Clear();
            thrusters.Clear();
            weapons.Clear();
            if (partsRoot == null)
                return;

            foreach (SpacecraftPart part in partsRoot.GetComponentsInChildren<SpacecraftPart>(true))
            {
                if (part.transform == partsRoot || part.GetComponentInParent<SpacecraftPart>() != part)
                    continue;
                parts.Add(part);
                IndexPart(part);
            }
        }

        private void IndexPart(SpacecraftPart part)
        {
            if (part is ThrusterPart thruster && !thrusters.Contains(thruster))
                thrusters.Add(thruster);
            if (part is WeaponPart weapon && !weapons.Contains(weapon))
                weapons.Add(weapon);
        }

        private void UnindexPart(SpacecraftPart part)
        {
            if (part is ThrusterPart thruster)
                thrusters.Remove(thruster);
            if (part is WeaponPart weapon)
                weapons.Remove(weapon);
        }

        private static SpacecraftPart ResolvePartComponent(GameObject instance, SpacecraftPartCategory category)
        {
            switch (category)
            {
                case SpacecraftPartCategory.Decoration:
                    return instance.GetComponent<DecorationPart>() ?? instance.AddComponent<DecorationPart>();
                case SpacecraftPartCategory.Weapon:
                    return instance.GetComponent<WeaponPart>() ?? instance.AddComponent<WeaponPart>();
                default:
                    return instance.GetComponent<ThrusterPart>() ?? instance.AddComponent<ThrusterPart>();
            }
        }
    }
}
