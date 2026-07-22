using System;
using UnityEngine;

namespace SpacecraftEditor
{
    [DisallowMultipleComponent]
    public class SpacecraftPart : MonoBehaviour
    {
        [SerializeField] private ShipPartDefinition definition;
        [SerializeField] private string runtimeId;
        [SerializeField] private string mirrorGroupId;
        [SerializeField] private string materialId;

        private float uniformScale = 1f;
        private SpacecraftMaterialCatalog materialCatalog;

        public ShipPartDefinition Definition => definition;
        public string RuntimeId => runtimeId;
        public string MirrorGroupId => mirrorGroupId;
        public string MaterialId => materialId;
        public float UniformScale => uniformScale;
        public virtual Vector3 ExhaustDirection => Vector3.zero;
        public virtual Vector3 ThrustDirection => Vector3.zero;
        public virtual float ActualMass => definition == null
            ? 0f
            : definition.BaseMass * uniformScale * uniformScale * uniformScale;

        protected virtual void Awake()
        {
            uniformScale = Mathf.Max(0.01f, transform.localScale.x);
            materialCatalog = FindObjectOfType<SpacecraftMaterialCatalog>();
        }

        public virtual void Configure(
            ShipPartDefinition partDefinition,
            float scale,
            string id = null,
            string groupId = null,
            KeyCode activationKey = KeyCode.None,
            string paintId = null,
            int weaponGroup = 0)
        {
            definition = partDefinition;
            runtimeId = string.IsNullOrEmpty(id) ? Guid.NewGuid().ToString("N") : id;
            mirrorGroupId = groupId ?? string.Empty;
            SetUniformScale(scale);
            ApplyMaterial(string.IsNullOrEmpty(paintId)
                ? partDefinition == null ? string.Empty : partDefinition.DefaultMaterialId
                : paintId);
        }

        public void SetMirrorGroup(string value)
        {
            mirrorGroupId = value ?? string.Empty;
        }

        public virtual void SetUniformScale(float value)
        {
            if (definition == null)
                uniformScale = Mathf.Clamp(value, 0.6f, 1.5f);
            else if (!definition.IsScalable)
                uniformScale = definition.FixedScale;
            else
                uniformScale = Mathf.Clamp(value, definition.MinimumScale, definition.MaximumScale);
            transform.localScale = Vector3.one * uniformScale;
        }

        public virtual void ApplyMaterial(string paintId)
        {
            if (materialCatalog == null)
                materialCatalog = FindObjectOfType<SpacecraftMaterialCatalog>();
            var selected = materialCatalog == null ? null : materialCatalog.Find(paintId);
            materialId = selected == null ? paintId ?? string.Empty : selected.MaterialId;
            if (selected == null || selected.Material == null)
                return;

            foreach (var renderer in GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!IsPaintable(renderer))
                    continue;
                var shared = renderer.sharedMaterials;
                if (shared == null || shared.Length == 0)
                {
                    renderer.sharedMaterial = selected.Material;
                    continue;
                }
                shared[0] = selected.Material;
                renderer.sharedMaterials = shared;
            }
        }

        public virtual PlacedPartState CaptureState()
        {
            return new PlacedPartState
            {
                runtimeId = runtimeId,
                partId = definition == null ? string.Empty : definition.PartId,
                localPosition = transform.localPosition,
                localRotation = transform.localRotation,
                uniformScale = uniformScale,
                mirrorGroupId = mirrorGroupId,
                materialId = materialId,
                activationKey = KeyCode.None,
                weaponGroup = 0
            };
        }

        static bool IsPaintable(Renderer renderer)
        {
            if (renderer == null || renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer)
                return false;
            string value = renderer.name.ToLowerInvariant();
            return !value.Contains("exhaust") && !value.Contains("glow") &&
                   !value.Contains("emission") && !value.Contains("glass") &&
                   !value.Contains("muzzle") && !value.Contains("structure") &&
                   !value.Contains("nozzle") && !value.Contains("accent") &&
                   !value.Contains("barrel") && !value.Contains("breech") &&
                   !value.Contains("mount") && !value.Contains("coil") &&
                   !value.Contains("emitter") && !value.Contains("frame");
        }
    }
}
