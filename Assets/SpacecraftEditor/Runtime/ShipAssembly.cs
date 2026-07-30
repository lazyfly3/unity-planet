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
        [SerializeField] private float hullMass = 12000f;

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

        public void Configure(Rigidbody body, Transform root, PartCatalog partCatalog, float baseHullMass = 12000f)
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
            RemoveParts(new[] { part });
        }

        public void RemoveParts(
            IEnumerable<SpacecraftPart> removing,
            bool recalculate = true)
        {
            if (removing == null)
                return;

            bool changed = false;
            var visited = new HashSet<SpacecraftPart>();
            foreach (SpacecraftPart part in removing)
            {
                if (part == null ||
                    !visited.Add(part))
                {
                    continue;
                }

                changed |= parts.Remove(part);
                UnindexPart(part);
                part.gameObject.SetActive(false);
                Destroy(part.gameObject);
            }

            if (changed && recalculate)
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
                ApplyAssemblyInertiaTensor(center);
            }
            AssemblyChanged?.Invoke();
        }

        void ApplyAssemblyInertiaTensor(Vector3 centerOfMass)
        {
            if (shipBody == null)
                return;

            ShipHullController hull = GetComponentInChildren<ShipHullController>(true);
            Vector3 hullSize = hull == null
                ? new Vector3(3f, 2.2f, 6f)
                : hull.CurrentHull == null
                    ? hull.LocalBounds.size
                    : hull.CurrentHull.Dimensions;
            hullSize = new Vector3(
                Mathf.Max(0.1f, Mathf.Abs(hullSize.x)),
                Mathf.Max(0.1f, Mathf.Abs(hullSize.y)),
                Mathf.Max(0.1f, Mathf.Abs(hullSize.z)));

            // Unity leaves the compound MeshCollider inertia at (1,1,1) for the
            // runtime-built spacecraft hierarchy. For a multi-ton hull that
            // turns tiny allocator residuals into extreme angular acceleration.
            // Use the hull's box inertia plus the parallel-axis contribution of
            // every installed part so mass and layout affect handling reliably.
            float baseMass = Mathf.Max(0.01f, hullMass);
            Vector3 inertia = new Vector3(
                baseMass * (hullSize.y * hullSize.y + hullSize.z * hullSize.z) / 12f,
                baseMass * (hullSize.x * hullSize.x + hullSize.z * hullSize.z) / 12f,
                baseMass * (hullSize.x * hullSize.x + hullSize.y * hullSize.y) / 12f);
            AddPointMassInertia(ref inertia, baseMass, -centerOfMass);

            foreach (SpacecraftPart part in parts)
            {
                if (part == null)
                    continue;
                AddPointMassInertia(
                    ref inertia,
                    Mathf.Max(0f, part.ActualMass),
                    part.transform.localPosition - centerOfMass);
            }

            shipBody.inertiaTensorRotation = Quaternion.identity;
            shipBody.inertiaTensor = new Vector3(
                Mathf.Max(0.01f, inertia.x),
                Mathf.Max(0.01f, inertia.y),
                Mathf.Max(0.01f, inertia.z));
        }

        static void AddPointMassInertia(
            ref Vector3 inertia,
            float mass,
            Vector3 offset)
        {
            inertia.x += mass * (offset.y * offset.y + offset.z * offset.z);
            inertia.y += mass * (offset.x * offset.x + offset.z * offset.z);
            inertia.z += mass * (offset.x * offset.x + offset.y * offset.y);
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
