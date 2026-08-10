using System;
using SpacecraftEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityPlanet.ModularAssembly
{
    /// <summary>
    /// Reuses the reviewed spacecraft catalog for combat-only enemy visuals.
    /// Physics and damage remain owned by the combat vehicle root.
    /// </summary>
    public static class EnemyFleetVisualLibrary
    {
        const string WorkshopResource = "Spacecraft/SpacecraftWorkshopRoot";
        const string OutlineShaderResource =
            "Shaders/EnemySilhouetteOutline";

        static HullCatalog catalog;
        static bool catalogResolved;
        static Material hostileOutlineMaterial;
        static bool outlineWarningIssued;

        public static GameObject Create(
            Transform parent,
            string hullId,
            string visualName,
            float targetLength,
            bool addHostileOutline = true)
        {
            if (parent == null)
                return null;

            ShipHullDefinition definition = Resolve(hullId);
            if (definition == null || definition.ModelPrefab == null)
                return null;

            GameObject visual = UnityEngine.Object.Instantiate(
                definition.ModelPrefab,
                parent,
                false);
            visual.name = string.IsNullOrWhiteSpace(visualName)
                ? "EnemyFleetVisual"
                : visualName;
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            float sourceLength = Mathf.Max(0.01f, definition.Dimensions.z);
            visual.transform.localScale = Vector3.one *
                                          (Mathf.Max(0.1f, targetLength) /
                                           sourceLength);

            DisableImportedPhysics(visual);
            CenterRendererBounds(parent, visual);
            if (addHostileOutline)
                AddHostileOutline(visual);
            return visual;
        }

        public static bool HasHull(string hullId)
        {
            ShipHullDefinition definition = Resolve(hullId);
            return definition != null && definition.ModelPrefab != null;
        }

        static ShipHullDefinition Resolve(string hullId)
        {
            if (!catalogResolved)
            {
                catalogResolved = true;
                GameObject workshop =
                    Resources.Load<GameObject>(WorkshopResource);
                catalog = workshop == null
                    ? null
                    : workshop.GetComponentInChildren<HullCatalog>(true);
            }
            return catalog == null || string.IsNullOrWhiteSpace(hullId)
                ? null
                : catalog.Find(hullId);
        }

        static void DisableImportedPhysics(GameObject visual)
        {
            Collider[] colliders =
                visual.GetComponentsInChildren<Collider>(true);
            for (int index = 0; index < colliders.Length; index++)
            {
                Collider collider = colliders[index];
                if (collider == null)
                    continue;
                collider.enabled = false;
                DestroyComponent(collider);
            }

            Rigidbody[] bodies =
                visual.GetComponentsInChildren<Rigidbody>(true);
            for (int index = 0; index < bodies.Length; index++)
            {
                Rigidbody body = bodies[index];
                if (body == null)
                    continue;
                body.isKinematic = true;
                body.detectCollisions = false;
                DestroyComponent(body);
            }
        }

        static void CenterRendererBounds(Transform parent, GameObject visual)
        {
            Renderer[] renderers =
                visual.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return;

            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                if (renderers[index] != null)
                    bounds.Encapsulate(renderers[index].bounds);
            }
            Vector3 localCenter = parent.InverseTransformPoint(bounds.center);
            visual.transform.localPosition -= localCenter;
        }

        static void AddHostileOutline(GameObject visual)
        {
            Material material = ResolveOutlineMaterial();
            if (visual == null || material == null)
                return;

            Renderer[] sources =
                visual.GetComponentsInChildren<Renderer>(true);
            for (int index = 0; index < sources.Length; index++)
            {
                Renderer source = sources[index];
                if (source == null || source is ParticleSystemRenderer ||
                    source is TrailRenderer || source is LineRenderer)
                {
                    continue;
                }

                if (source is SkinnedMeshRenderer skinned)
                {
                    if (skinned.sharedMesh == null)
                        continue;
                    GameObject outlineObject = CreateOutlineObject(source);
                    SkinnedMeshRenderer outline =
                        outlineObject.AddComponent<SkinnedMeshRenderer>();
                    outline.sharedMesh = skinned.sharedMesh;
                    outline.rootBone = skinned.rootBone;
                    outline.bones = skinned.bones;
                    outline.localBounds = skinned.localBounds;
                    outline.quality = skinned.quality;
                    outline.updateWhenOffscreen = skinned.updateWhenOffscreen;
                    ConfigureOutlineRenderer(
                        outline,
                        source,
                        material,
                        LargestDimension(skinned.localBounds));
                    continue;
                }

                MeshFilter sourceFilter = source.GetComponent<MeshFilter>();
                if (!(source is MeshRenderer) || sourceFilter == null ||
                    sourceFilter.sharedMesh == null)
                {
                    continue;
                }

                GameObject meshOutlineObject = CreateOutlineObject(source);
                MeshFilter outlineFilter =
                    meshOutlineObject.AddComponent<MeshFilter>();
                outlineFilter.sharedMesh = sourceFilter.sharedMesh;
                MeshRenderer meshOutline =
                    meshOutlineObject.AddComponent<MeshRenderer>();
                ConfigureOutlineRenderer(
                    meshOutline,
                    source,
                    material,
                    LargestDimension(sourceFilter.sharedMesh.bounds));
            }
        }

        static GameObject CreateOutlineObject(Renderer source)
        {
            GameObject value = new GameObject(
                source.gameObject.name + "_HostileOutline");
            value.layer = source.gameObject.layer;
            Transform target = value.transform;
            Transform sourceTransform = source.transform;
            target.SetParent(sourceTransform.parent, false);
            target.localPosition = sourceTransform.localPosition;
            target.localRotation = sourceTransform.localRotation;
            target.localScale = sourceTransform.localScale;
            return value;
        }

        static void ConfigureOutlineRenderer(
            Renderer outline,
            Renderer source,
            Material material,
            float meshSize)
        {
            // Unity renders one material slot per submesh. Supplying only one
            // outline material outlined just the first submesh (typically the
            // cockpit) on the authored enemy hulls. Mirror the source slot
            // count so the complete silhouette participates in the border.
            int materialCount = Mathf.Max(
                1,
                source.sharedMaterials == null
                    ? 0
                    : source.sharedMaterials.Length);
            var outlineMaterials = new Material[materialCount];
            for (int index = 0; index < outlineMaterials.Length; index++)
                outlineMaterials[index] = material;
            outline.sharedMaterials = outlineMaterials;
            outline.enabled = source.enabled;
            outline.shadowCastingMode = ShadowCastingMode.Off;
            outline.receiveShadows = false;
            outline.lightProbeUsage = LightProbeUsage.Off;
            outline.reflectionProbeUsage = ReflectionProbeUsage.Off;
            outline.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
            outline.sortingLayerID = source.sortingLayerID;
            outline.sortingOrder = source.sortingOrder + 1;
            var properties = new MaterialPropertyBlock();
            properties.SetFloat(
                "_OutlineWidth",
                Mathf.Clamp(meshSize * 0.010f, 0.010f, 0.24f));
            outline.SetPropertyBlock(properties);
        }

        static float LargestDimension(Bounds bounds)
        {
            return Mathf.Max(
                Mathf.Abs(bounds.size.x),
                Mathf.Abs(bounds.size.y),
                Mathf.Abs(bounds.size.z));
        }

        static Material ResolveOutlineMaterial()
        {
            if (hostileOutlineMaterial != null)
                return hostileOutlineMaterial;

            Shader shader = Resources.Load<Shader>(OutlineShaderResource) ??
                            Shader.Find(
                                "Hidden/UnityPlanet/EnemySilhouetteOutline");
            if (shader == null)
            {
                if (!outlineWarningIssued)
                {
                    outlineWarningIssued = true;
                    Debug.LogWarning(
                        "[EnemyFleetVisual] 敌方轮廓 Shader 不可用，已保留原模型。" );
                }
                return null;
            }

            hostileOutlineMaterial = new Material(shader)
            {
                name = "EnemySilhouetteOutline_Runtime",
                hideFlags = HideFlags.HideAndDontSave
            };
            hostileOutlineMaterial.SetColor(
                "_OutlineColor",
                new Color(1.75f, 0.035f, 0.018f, 0.70f));
            hostileOutlineMaterial.SetFloat("_OutlineWidth", 0.04f);
            return hostileOutlineMaterial;
        }

        static void DestroyComponent(Component component)
        {
            if (component == null)
                return;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(component);
            else
                UnityEngine.Object.DestroyImmediate(component);
        }
    }
}
