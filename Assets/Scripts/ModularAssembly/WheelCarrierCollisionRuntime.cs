using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityPlanet.ModularAssembly
{
    /// <summary>
    /// Builds low-friction rigid collision for the visible wheel carrier.
    /// Tyres and enclosed hubs are deliberately excluded: tyre contact is
    /// resolved by <see cref="ModularWheelRuntime"/>.
    /// </summary>
    public sealed class WheelCarrierCollisionRuntime : MonoBehaviour
    {
        private struct BoxSpec
        {
            public string name;
            public Vector3 center;
            public Vector3 size;

            public BoxSpec(string name, Vector3 center, Vector3 size)
            {
                this.name = name;
                this.center = center;
                this.size = size;
            }
        }

        private const string GeneratedRootName =
            "__WheelCarrierCollision";
        private const float InsetPerFace = 0.005f;
        private const float MinimumBoxSize = 0.01f;

        private readonly List<BoxCollider> generatedColliders =
            new List<BoxCollider>(5);

        private string configuredNeoXId = string.Empty;
        private Transform generatedRoot;
        private Transform motionRoot;
        private PhysicMaterial carrierMaterial;

        public string ConfiguredNeoXId => configuredNeoXId;
        public IReadOnlyList<BoxCollider> GeneratedColliders =>
            generatedColliders;

        public void Configure(string neoXId)
        {
            configuredNeoXId = neoXId ?? string.Empty;
            Rebuild();
        }

        public void BindMotionRoot(Transform target)
        {
            motionRoot = target != null ? target : transform;
            if (generatedRoot != null)
                generatedRoot.SetParent(motionRoot, false);
        }

        public void Rebuild()
        {
            ClearGenerated();

            BoxSpec[] boxes = ResolveBoxes(configuredNeoXId);
            if (boxes.Length == 0)
                return;

            GameObject rootObject = new GameObject(GeneratedRootName);
            generatedRoot = rootObject.transform;
            generatedRoot.SetParent(
                motionRoot != null ? motionRoot : transform,
                false);
            rootObject.layer = gameObject.layer;

            PhysicMaterial material = EnsureCarrierMaterial();
            for (int index = 0; index < boxes.Length; index++)
                AddBox(boxes[index], material);
        }

        public void ClearGenerated()
        {
            Transform root = generatedRoot != null
                ? generatedRoot
                : transform.Find(GeneratedRootName);
            generatedRoot = null;
            generatedColliders.Clear();
            if (root == null)
                return;

            foreach (Collider collider in
                     root.GetComponentsInChildren<Collider>(true))
            {
                if (collider != null)
                    collider.enabled = false;
            }

            if (Application.isPlaying)
                Destroy(root.gameObject);
            else
                DestroyImmediate(root.gameObject);
        }

        private void AddBox(
            BoxSpec spec,
            PhysicMaterial material)
        {
            GameObject boxObject =
                new GameObject("CarrierCollider_" + spec.name);
            Transform boxTransform = boxObject.transform;
            boxTransform.SetParent(generatedRoot, false);
            boxTransform.localPosition = spec.center;
            boxObject.layer = gameObject.layer;

            BoxCollider collider =
                boxObject.AddComponent<BoxCollider>();
            collider.center = Vector3.zero;
            collider.size = new Vector3(
                Mathf.Max(
                    MinimumBoxSize,
                    spec.size.x - InsetPerFace * 2f),
                Mathf.Max(
                    MinimumBoxSize,
                    spec.size.y - InsetPerFace * 2f),
                Mathf.Max(
                    MinimumBoxSize,
                    spec.size.z - InsetPerFace * 2f));
            collider.sharedMaterial = material;
            collider.isTrigger = false;
            generatedColliders.Add(collider);
        }

        private PhysicMaterial EnsureCarrierMaterial()
        {
            if (carrierMaterial != null)
                return carrierMaterial;

            carrierMaterial = new PhysicMaterial(
                "WheelCarrier_LowFriction")
            {
                staticFriction = 0.03f,
                dynamicFriction = 0.02f,
                bounciness = 0f,
                frictionCombine = PhysicMaterialCombine.Minimum,
                bounceCombine = PhysicMaterialCombine.Minimum,
                hideFlags = HideFlags.HideAndDontSave
            };
            return carrierMaterial;
        }

        private static BoxSpec[] ResolveBoxes(string neoXId)
        {
            string id = neoXId ?? string.Empty;

            // The 4x2x2 large wheel is intentionally out of scope.
            if (Contains(id, "wheel_l_422"))
                return Array.Empty<BoxSpec>();
            if (Contains(id, "wheel_basic_111"))
                return BasicBoxes();
            if (Contains(id, "wheel_m_222"))
                return MediumBoxes();
            if (Contains(id, "speedwheel_small_l_322"))
                return SmallLeftBoxes();
            if (Contains(id, "speedwheel_small_r_322"))
                return SmallRightBoxes();
            if (Contains(id, "speedwheel_large_l_522"))
                return RearLeftBoxes();
            if (Contains(id, "speedwheel_large_r_522"))
                return RearRightBoxes();
            return Array.Empty<BoxSpec>();
        }

        private static BoxSpec[] BasicBoxes()
        {
            // The authored carrier arm overlaps the tyre near +X. Clip that
            // hidden attachment at x=-0.01485 before the common 5 mm inset.
            return new[]
            {
                new BoxSpec(
                    "BasicArm",
                    new Vector3(
                        -0.1844524f,
                        0.0698178f,
                        0.0040045f),
                    new Vector3(
                        0.3392068f,
                        0.6430451f,
                        0.2831773f))
            };
        }

        private static BoxSpec[] MediumBoxes()
        {
            return new[]
            {
                new BoxSpec(
                    "MediumMain",
                    new Vector3(
                        -0.6135197f,
                        0.2820167f,
                        -0.0002239f),
                    new Vector3(
                        0.2570437f,
                        1.4359670f,
                        1.4641170f)),
                new BoxSpec(
                    "MediumBridge",
                    new Vector3(
                        -0.3214304f,
                        0.2823100f,
                        0.0016497f),
                    new Vector3(
                        0.4036241f,
                        0.6916461f,
                        0.7422798f)),
                new BoxSpec(
                    "MediumUpperBrace",
                    new Vector3(
                        -0.3282890f,
                        0.3344099f,
                        0.0004901f),
                    new Vector3(
                        0.3955860f,
                        0.5384997f,
                        0.2726745f))
            };
        }

        private static BoxSpec[] SmallLeftBoxes()
        {
            return new[]
            {
                new BoxSpec(
                    "SmallNegativeEnd",
                    new Vector3(
                        -0.2917632f,
                        0.0875002f,
                        -1.0252610f),
                    new Vector3(
                        0.8549168f,
                        1.0809930f,
                        0.9494781f)),
                new BoxSpec(
                    "SmallPositiveEnd",
                    new Vector3(
                        0f,
                        0.0262089f,
                        1.0281820f),
                    new Vector3(
                        1.4384430f,
                        0.9588774f,
                        0.9436359f)),
                new BoxSpec(
                    "SmallInner",
                    new Vector3(
                        0.0599970f,
                        -0.0388239f,
                        0.0052338f),
                    new Vector3(
                        0.1184098f,
                        0.8283452f,
                        1.0845510f)),
                new BoxSpec(
                    "SmallUpperBridge",
                    new Vector3(
                        -0.2924668f,
                        0.5491407f,
                        0.0002706f),
                    new Vector3(
                        0.8535094f,
                        0.2720342f,
                        0.9838473f))
            };
        }

        private static BoxSpec[] SmallRightBoxes()
        {
            return new[]
            {
                new BoxSpec(
                    "SmallNegativeEnd",
                    new Vector3(
                        0.2917632f,
                        0.0875002f,
                        -1.0252610f),
                    new Vector3(
                        0.8549170f,
                        1.0809930f,
                        0.9494779f)),
                new BoxSpec(
                    "SmallPositiveEnd",
                    new Vector3(
                        0f,
                        0.0262089f,
                        1.0281820f),
                    new Vector3(
                        1.4384430f,
                        0.9588774f,
                        0.9436359f)),
                new BoxSpec(
                    "SmallInner",
                    new Vector3(
                        -0.0599970f,
                        -0.0388239f,
                        0.0052338f),
                    new Vector3(
                        0.1184098f,
                        0.8283452f,
                        1.0845510f)),
                new BoxSpec(
                    "SmallUpperBridge",
                    new Vector3(
                        0.2924669f,
                        0.5491407f,
                        0.0002706f),
                    new Vector3(
                        0.8535095f,
                        0.2720342f,
                        0.9838474f))
            };
        }

        private static BoxSpec[] RearLeftBoxes()
        {
            return new[]
            {
                new BoxSpec(
                    "RearNegativeEndAndTail",
                    new Vector3(
                        0f,
                        0.1152055f,
                        -2.0863940f),
                    new Vector3(
                        1.5973090f,
                        1.5231820f,
                        0.6744537f)),
                new BoxSpec(
                    "RearPositiveEnd",
                    new Vector3(
                        0.0713354f,
                        -0.0233252f,
                        1.8070500f),
                    new Vector3(
                        1.4546380f,
                        1.2498810f,
                        1.2331420f)),
                new BoxSpec(
                    "RearLeftRail",
                    new Vector3(
                        -0.6814333f,
                        -0.0732989f,
                        -0.4081138f),
                    new Vector3(
                        0.2309096f,
                        0.9311408f,
                        2.4705610f)),
                new BoxSpec(
                    "RearUpperBridge",
                    new Vector3(
                        0.0009449f,
                        0.7641428f,
                        -0.4569432f),
                    new Vector3(
                        1.5954190f,
                        0.4717144f,
                        2.2849480f))
            };
        }

        private static BoxSpec[] RearRightBoxes()
        {
            return new[]
            {
                new BoxSpec(
                    "RearNegativeEndAndTail",
                    new Vector3(
                        0f,
                        0.1152055f,
                        -2.0863940f),
                    new Vector3(
                        1.5973090f,
                        1.5231820f,
                        0.6744537f)),
                new BoxSpec(
                    "RearPositiveEnd",
                    new Vector3(
                        -0.0713355f,
                        -0.0233253f,
                        1.8070500f),
                    new Vector3(
                        1.4546380f,
                        1.2498810f,
                        1.2331420f)),
                new BoxSpec(
                    "RearRightRail",
                    new Vector3(
                        0.6814334f,
                        -0.0732989f,
                        -0.4081136f),
                    new Vector3(
                        0.2309098f,
                        0.9311409f,
                        2.4705610f)),
                new BoxSpec(
                    "RearUpperBridge",
                    new Vector3(
                        -0.0009449f,
                        0.7641428f,
                        -0.4569430f),
                    new Vector3(
                        1.5954190f,
                        0.4717144f,
                        2.2849480f))
            };
        }

        private static bool Contains(string value, string token)
        {
            return value.IndexOf(
                token,
                StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void OnDestroy()
        {
            ClearGenerated();
            if (carrierMaterial == null)
                return;

            if (Application.isPlaying)
                Destroy(carrierMaterial);
            else
                DestroyImmediate(carrierMaterial);
            carrierMaterial = null;
        }
    }
}
