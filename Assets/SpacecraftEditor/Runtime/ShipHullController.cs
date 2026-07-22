using System;
using UnityEngine;

namespace SpacecraftEditor
{
    [DisallowMultipleComponent]
    public sealed class ShipHullController : MonoBehaviour
    {
        [SerializeField] private Transform modelRoot;
        [SerializeField] private MeshCollider hullCollider;
        [SerializeField] private Material hullMaterial;
        [SerializeField] private Material accentMaterial;
        [SerializeField] private ShipHullDefinition currentHull;
        [SerializeField] private string currentMaterialId = "paint.deep_space_blue";
        [SerializeField] private SpacecraftMaterialCatalog materialCatalog;

        public event Action<ShipHullDefinition> HullChanged;

        public ShipHullDefinition CurrentHull => currentHull;
        public MeshCollider HullCollider => hullCollider;
        public string CurrentMaterialId => currentMaterialId;
        public Bounds LocalBounds => hullCollider != null && hullCollider.sharedMesh != null
            ? hullCollider.sharedMesh.bounds
            : new Bounds(Vector3.zero, currentHull == null ? new Vector3(3f, 2.2f, 6f) : currentHull.Dimensions);

        private void Awake()
        {
            ResolveReferences();
            if (materialCatalog == null)
                materialCatalog = FindObjectOfType<SpacecraftMaterialCatalog>();
        }

        public void Configure(Transform visualRoot, MeshCollider collider, Material mainMaterial, Material detailMaterial)
        {
            modelRoot = visualRoot == null ? transform : visualRoot;
            hullCollider = collider;
            hullMaterial = mainMaterial;
            accentMaterial = detailMaterial;
        }

        public bool ApplyHull(ShipHullDefinition definition)
        {
            if (definition == null || definition.ModelPrefab == null || definition.CollisionMesh == null)
                return false;

            ResolveReferences();
            RemoveCurrentModel();

            var model = Instantiate(definition.ModelPrefab, modelRoot);
            model.name = "Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;
            ApplyMaterials(model);

            hullCollider.sharedMesh = definition.CollisionMesh;
            hullCollider.convex = true;
            hullCollider.enabled = true;
            currentHull = definition;
            HullChanged?.Invoke(currentHull);
            return true;
        }

        public bool ApplyPaint(string materialId)
        {
            if (string.IsNullOrEmpty(materialId))
                return false;
            if (materialCatalog == null)
                materialCatalog = FindObjectOfType<SpacecraftMaterialCatalog>();
            SpacecraftMaterialDefinition paint = materialCatalog == null ? null : materialCatalog.Find(materialId);
            if (paint == null || paint.Material == null)
                return false;
            currentMaterialId = paint.MaterialId;
            hullMaterial = paint.Material;
            Transform model = modelRoot == null ? null : modelRoot.Find("Model");
            if (model != null)
                ApplyMaterials(model.gameObject);
            return true;
        }

        private void ResolveReferences()
        {
            if (modelRoot == null)
                modelRoot = transform;
            if (hullCollider == null)
                hullCollider = GetComponent<MeshCollider>();
            if (hullCollider == null)
                hullCollider = gameObject.AddComponent<MeshCollider>();
        }

        private void RemoveCurrentModel()
        {
            for (var index = modelRoot.childCount - 1; index >= 0; index--)
            {
                var child = modelRoot.GetChild(index);
                if (child.name != "Model")
                    continue;
                child.gameObject.SetActive(false);
                child.SetParent(null, false);
                if (Application.isPlaying)
                    Destroy(child.gameObject);
                else
                    DestroyImmediate(child.gameObject);
            }
        }

        private void ApplyMaterials(GameObject model)
        {
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                var isAccent = renderer.name.IndexOf("Accent", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               renderer.name.IndexOf("Ring", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               renderer.name.IndexOf("Canopy", StringComparison.OrdinalIgnoreCase) >= 0;
                var material = isAccent ? accentMaterial : hullMaterial;
                if (material != null)
                    renderer.sharedMaterial = material;
            }
        }
    }
}
