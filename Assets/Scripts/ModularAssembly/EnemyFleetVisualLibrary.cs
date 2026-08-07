using System;
using SpacecraftEditor;
using UnityEngine;

namespace UnityPlanet.ModularAssembly
{
    /// <summary>
    /// Reuses the reviewed spacecraft catalog for combat-only enemy visuals.
    /// Physics and damage remain owned by the combat vehicle root.
    /// </summary>
    public static class EnemyFleetVisualLibrary
    {
        const string WorkshopResource = "Spacecraft/SpacecraftWorkshopRoot";

        static HullCatalog catalog;
        static bool catalogResolved;

        public static GameObject Create(
            Transform parent,
            string hullId,
            string visualName,
            float targetLength)
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
