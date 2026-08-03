#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityPlanet.CombatMap;
using UnityPlanet.ModularAssembly;

namespace UnityPlanet.EditorTools
{
    // One-shot targeted verification; removed after the final audit.
    internal static class CombatMapFinalVerificationOnce
    {
        private const string RequestPath =
            "Temp/CombatMapFinalVerification.request";
        private const string ResultPath =
            "Temp/CombatMapFinalVerification.result.txt";

        [InitializeOnLoadMethod]
        private static void Schedule()
        {
            if (!File.Exists(RequestPath))
                return;
            File.Delete(RequestPath);
            EditorApplication.delayCall += Verify;
        }

        private static void Verify()
        {
            var failures = new List<string>();
            Scene scene = default;
            try
            {
                scene = EditorSceneManager.OpenScene(
                    "Assets/Scenes/CombatMapRuntime.unity",
                    OpenSceneMode.Additive);
                BakedCombatMapFlightEnvironment provider = null;
                GameObject duel = null;
                GameObject horde = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    BakedCombatMapFlightEnvironment candidate =
                        root.GetComponent<BakedCombatMapFlightEnvironment>();
                    if (candidate != null)
                        provider = candidate;
                    if (root.name == "CombatMapEnvironment_Duel")
                    {
                        duel = root;
                    }
                    else if (root.name == "CombatMapEnvironment_Horde")
                    {
                        horde = root;
                    }
                }

                if (provider == null)
                    failures.Add("Baked provider missing");
                else if (provider.BakeVersion != 4)
                    failures.Add("Expected bake version 4, got "
                        + provider.BakeVersion);
                VerifyMode("Duel", duel, provider, CombatTestMode.Duel,
                    failures);
                VerifyMode("Horde", horde, provider, CombatTestMode.Horde,
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(exception.ToString());
            }
            finally
            {
                if (scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }

            File.WriteAllText(ResultPath,
                failures.Count == 0
                    ? "PASS\nTargeted CombatMap bake verification passed."
                    : "FAIL\n" + string.Join("\n", failures));
        }

        private static void VerifyMode(
            string label,
            GameObject root,
            BakedCombatMapFlightEnvironment provider,
            CombatTestMode mode,
            List<string> failures)
        {
            if (root == null)
            {
                failures.Add(label + ": environment root missing");
                return;
            }
            root.SetActive(true);
            Physics.SyncTransforms();

            Transform containment = root.transform.Find(
                "FlightContainmentAirWalls");
            BoxCollider[] airWalls = containment != null
                ? containment.GetComponentsInChildren<BoxCollider>(true)
                : Array.Empty<BoxCollider>();
            if (airWalls.Length != 5)
            {
                failures.Add(label + ": expected five containment walls, got "
                    + airWalls.Length);
            }

            int modelCount = 0;
            int roadCount = 0;
            float highest = float.NegativeInfinity;
            Transform[] transforms = root.GetComponentsInChildren<Transform>(
                true);
            foreach (Transform current in transforms)
            {
                if (current.name.StartsWith("PlaceholderVisual_",
                        StringComparison.Ordinal))
                {
                    failures.Add(label + ": placeholder remains at "
                        + HierarchyPath(current));
                }
                if (current.name.StartsWith("CombatMapUrbanLot_",
                        StringComparison.Ordinal))
                {
                    failures.Add(label + ": obsolete lot slab remains at "
                        + HierarchyPath(current));
                }
                if (current.name.StartsWith("CombatMapRoad_",
                        StringComparison.Ordinal))
                {
                    roadCount++;
                }
                if (current.name != "Model_Building"
                    && current.name != "Model_Beacon")
                {
                    continue;
                }
                modelCount++;
                VerifyBuilding(label, current, failures);
            }
            if (roadCount < 4)
                failures.Add(label + ": too few road objects: " + roadCount);
            if (modelCount < (mode == CombatTestMode.Horde ? 18 : 8))
            {
                failures.Add(label + ": too few authored buildings: "
                    + modelCount);
            }

            Shader errorShader = Shader.Find("Hidden/InternalErrorShader");
            foreach (Renderer renderer in
                     root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.name.StartsWith("PlaceholderVisual_",
                        StringComparison.Ordinal))
                {
                    continue;
                }
                highest = Mathf.Max(highest, renderer.bounds.max.y);
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null || material.shader == null
                        || material.shader == errorShader)
                    {
                        failures.Add(label + ": invalid material on "
                            + HierarchyPath(renderer.transform));
                    }
                }
            }
            if (provider != null)
            {
                provider.SetMode(mode);
                if (!(provider.FlightCeiling > highest + 17.5f))
                {
                    failures.Add(label + ": ceiling "
                        + provider.FlightCeiling.ToString("F2")
                        + " is not safely above geometry "
                        + highest.ToString("F2"));
                }
            }
        }

        private static void VerifyBuilding(
            string label,
            Transform model,
            List<string> failures)
        {
            Vector3 scale = model.localScale;
            float sx = Mathf.Abs(scale.x);
            float sy = Mathf.Abs(scale.y);
            float sz = Mathf.Abs(scale.z);
            float tolerance = Mathf.Max(0.0001f,
                Mathf.Max(sx, Mathf.Max(sy, sz)) * 0.001f);
            if (Mathf.Abs(sx - sy) > tolerance
                || Mathf.Abs(sx - sz) > tolerance)
            {
                failures.Add(label + ": non-uniform building scale at "
                    + HierarchyPath(model) + " = " + scale);
            }

            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(
                true);
            if (renderers.Length == 0)
            {
                failures.Add(label + ": building has no renderer at "
                    + HierarchyPath(model));
                return;
            }
            Bounds modelBounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
                modelBounds.Encapsulate(renderers[index].bounds);
            float horizontal = Mathf.Max(
                modelBounds.size.x,
                modelBounds.size.z);
            if (modelBounds.size.y <= horizontal * 1.20f)
            {
                failures.Add(label + ": building is not upright at "
                    + HierarchyPath(model) + " size=" + modelBounds.size);
            }

            BoxCollider proxy = model.parent != null
                ? model.parent.GetComponent<BoxCollider>()
                : null;
            if (proxy == null)
            {
                failures.Add(label + ": building collider missing at "
                    + HierarchyPath(model));
                return;
            }
            Bounds proxyBounds = proxy.bounds;
            float bottomDelta = Mathf.Abs(
                modelBounds.min.y - proxyBounds.min.y);
            float allowed = Mathf.Max(0.35f, modelBounds.size.y * 0.01f);
            if (bottomDelta > allowed)
            {
                failures.Add(label + ": building bottom/collider mismatch at "
                    + HierarchyPath(model) + " delta="
                    + bottomDelta.ToString("F3"));
            }
            if (!Contains(proxyBounds, modelBounds, allowed))
            {
                failures.Add(label + ": collider does not cover building at "
                    + HierarchyPath(model));
            }
        }

        private static bool Contains(
            Bounds outer,
            Bounds inner,
            float tolerance)
        {
            return inner.min.x >= outer.min.x - tolerance
                && inner.min.y >= outer.min.y - tolerance
                && inner.min.z >= outer.min.z - tolerance
                && inner.max.x <= outer.max.x + tolerance
                && inner.max.y <= outer.max.y + tolerance
                && inner.max.z <= outer.max.z + tolerance;
        }

        private static string HierarchyPath(Transform current)
        {
            string path = current.name;
            while (current.parent != null)
            {
                current = current.parent;
                path = current.name + "/" + path;
            }
            return path;
        }
    }
}
#endif
