using System;
using UnityEngine;

namespace SpacecraftEditor
{
    [DisallowMultipleComponent]
    public sealed class ShipHullController : MonoBehaviour
    {
        [SerializeField] private Transform modelRoot;
        [SerializeField] private MeshCollider hullCollider;
        [SerializeField] private MeshCollider placementCollider;
        [SerializeField] private Material hullMaterial;
        [SerializeField] private Material accentMaterial;
        [SerializeField] private ShipHullDefinition currentHull;
        [SerializeField] private string currentMaterialId = SpacecraftPaintBinding.NativePaintId;
        [SerializeField] private SpacecraftMaterialCatalog materialCatalog;

        public event Action<ShipHullDefinition> HullChanged;

        public ShipHullDefinition CurrentHull => currentHull;
        public MeshCollider HullCollider => hullCollider;
        public Collider PlacementCollider =>
            placementCollider != null && placementCollider.sharedMesh != null
                ? placementCollider
                : hullCollider;
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
            hullCollider.sharedMesh = definition.CollisionMesh;
            hullCollider.convex = true;
            hullCollider.enabled = true;
            ResolvePlacementCollider();
            placementCollider.sharedMesh = definition.PlacementSurfaceMesh;
            placementCollider.convex = false;
            placementCollider.enabled = definition.PlacementSurfaceMesh != null;
            currentHull = definition;
            currentMaterialId = SpacecraftPaintBinding.NativePaintId;
            ApplyMaterials(model);
            HullChanged?.Invoke(currentHull);
            return true;
        }

        public bool ApplyPaint(string materialId)
        {
            if (string.IsNullOrEmpty(materialId))
                return false;
            Transform model = modelRoot == null ? null : modelRoot.Find("Model");
            if (materialId == SpacecraftPaintBinding.NativePaintId)
            {
                currentMaterialId = materialId;
                if (model != null)
                    RestoreNativeMaterials(model.gameObject);
                return true;
            }
            if (materialCatalog == null)
                materialCatalog = FindObjectOfType<SpacecraftMaterialCatalog>();
            SpacecraftMaterialDefinition paint = materialCatalog == null ? null : materialCatalog.Find(materialId);
            if (paint == null || paint.Material == null)
                return false;
            currentMaterialId = paint.MaterialId;
            hullMaterial = paint.Material;
            if (model != null)
                ApplyMaterials(model.gameObject);
            return true;
        }

        public void SetPlacementEnabled(bool value)
        {
            if (placementCollider != null && placementCollider.sharedMesh != null)
                placementCollider.enabled = value;
        }

        private void ResolveReferences()
        {
            if (modelRoot == null)
                modelRoot = transform;
            if (hullCollider == null)
                hullCollider = GetComponent<MeshCollider>();
            if (hullCollider == null)
                hullCollider = gameObject.AddComponent<MeshCollider>();
            ResolvePlacementCollider();
        }

        private void ResolvePlacementCollider()
        {
            if (placementCollider != null)
                return;
            Transform existing = transform.Find("PlacementSurfaceCollider");
            GameObject target;
            if (existing != null)
                target = existing.gameObject;
            else
            {
                target = new GameObject("PlacementSurfaceCollider");
                target.transform.SetParent(transform, false);
            }
            placementCollider = target.GetComponent<MeshCollider>();
            if (placementCollider == null)
                placementCollider = target.AddComponent<MeshCollider>();
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
            SpacecraftPaintBinding[] bindings =
                model.GetComponentsInChildren<SpacecraftPaintBinding>(true);
            if (bindings.Length > 0)
            {
                if (currentMaterialId == SpacecraftPaintBinding.NativePaintId)
                {
                    foreach (SpacecraftPaintBinding binding in bindings)
                        binding.RestoreNative();
                }
                else if (hullMaterial != null)
                {
                    foreach (SpacecraftPaintBinding binding in bindings)
                        binding.ApplyPaint(hullMaterial);
                }
                return;
            }

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

        private static void RestoreNativeMaterials(GameObject model)
        {
            foreach (SpacecraftPaintBinding binding in
                     model.GetComponentsInChildren<SpacecraftPaintBinding>(true))
                binding.RestoreNative();
        }
    }
}
