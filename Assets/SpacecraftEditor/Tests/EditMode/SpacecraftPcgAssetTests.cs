using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SpacecraftEditor.Tests
{
    public sealed class SpacecraftPcgAssetTests
    {
        static readonly string[] Archetypes = { "balanced", "spindle", "saucer" };

        [Test]
        public void PcgFleet_HasTwentyFourUniqueCompatibleHullDefinitions()
        {
            ShipHullDefinition[] hulls = LoadPcgHulls();
            Assert.That(hulls, Has.Length.EqualTo(24));
            Assert.That(hulls.Select(hull => hull.HullId).Distinct().Count(), Is.EqualTo(24));
            Assert.That(hulls.Any(hull => hull.HullId == "hull.balanced"), Is.True);
            Assert.That(hulls.Any(hull => hull.HullId == "hull.spindle"), Is.True);
            Assert.That(hulls.Any(hull => hull.HullId == "hull.saucer"), Is.True);

            foreach (string archetype in Archetypes)
            {
                ShipHullDefinition canonical =
                    hulls.Single(hull => hull.HullId == "hull." + archetype);
                ShipHullDefinition[] family = hulls
                    .Where(hull => hull.HullId == "hull." + archetype ||
                                   hull.HullId.StartsWith(
                                       "hull." + archetype + ".pcg.",
                                       StringComparison.Ordinal))
                    .ToArray();
                Assert.That(family, Has.Length.EqualTo(8), archetype);
                foreach (ShipHullDefinition variant in family)
                {
                    Assert.That(variant.BaseMass, Is.EqualTo(canonical.BaseMass), variant.HullId);
                    Assert.That(variant.Dimensions, Is.EqualTo(canonical.Dimensions), variant.HullId);
                    Assert.That(
                        variant.FlightProfile.IntegratedRcsNozzleForce,
                        Is.EqualTo(canonical.FlightProfile.IntegratedRcsNozzleForce),
                        variant.HullId);
                    Assert.That(
                        variant.FlightProfile.MaximumAngularSpeed,
                        Is.EqualTo(canonical.FlightProfile.MaximumAngularSpeed),
                        variant.HullId);
                }
            }
        }

        [Test]
        public void PcgFleet_HullsHaveHighDetailLodsAndLowDetailCollision()
        {
            foreach (ShipHullDefinition hull in LoadPcgHulls())
            {
                Assert.That(hull.ModelPrefab, Is.Not.Null, hull.HullId);
                Assert.That(hull.CollisionMesh, Is.Not.Null, hull.HullId);
                Assert.That(hull.Thumbnail, Is.Not.Null, hull.HullId);
                Assert.That(TriangleCount(hull.CollisionMesh), Is.LessThanOrEqualTo(200), hull.HullId);

                LODGroup group = hull.ModelPrefab.GetComponentInChildren<LODGroup>(true);
                Assert.That(group, Is.Not.Null, hull.HullId);
                LOD[] lods = group.GetLODs();
                Assert.That(lods, Has.Length.EqualTo(3), hull.HullId);
                Assert.That(TriangleCount(lods[0]), Is.LessThanOrEqualTo(250000), hull.HullId);
                Assert.That(TriangleCount(lods[1]), Is.LessThanOrEqualTo(80000), hull.HullId);
                Assert.That(TriangleCount(lods[2]), Is.LessThanOrEqualTo(20000), hull.HullId);
                Assert.That(TriangleCount(lods[0]), Is.GreaterThan(TriangleCount(lods[1])), hull.HullId);
                Assert.That(TriangleCount(lods[1]), Is.GreaterThan(TriangleCount(lods[2])), hull.HullId);
            }
        }

        [Test]
        public void PcgFleet_PreservesAllThirteenFunctionalModulePrefabs()
        {
            var expected = new Dictionary<string, Type>
            {
                { "Assets/SpacecraftEditor/Prefabs/ThrusterSmall.prefab", typeof(ThrusterPart) },
                { "Assets/SpacecraftEditor/Prefabs/ThrusterMedium.prefab", typeof(ThrusterPart) },
                { "Assets/SpacecraftEditor/Prefabs/ThrusterLarge.prefab", typeof(ThrusterPart) },
                { "Assets/SpacecraftEditor/Prefabs/ModularParts/SweptWing.prefab", typeof(DecorationPart) },
                { "Assets/SpacecraftEditor/Prefabs/ModularParts/DeltaWing.prefab", typeof(DecorationPart) },
                { "Assets/SpacecraftEditor/Prefabs/ModularParts/Canard.prefab", typeof(DecorationPart) },
                { "Assets/SpacecraftEditor/Prefabs/ModularParts/VerticalFin.prefab", typeof(DecorationPart) },
                { "Assets/SpacecraftEditor/Prefabs/ModularParts/Radiator.prefab", typeof(DecorationPart) },
                { "Assets/SpacecraftEditor/Prefabs/ModularParts/SensorMast.prefab", typeof(DecorationPart) },
                { "Assets/SpacecraftEditor/Prefabs/ModularParts/EngineNacelle.prefab", typeof(DecorationPart) },
                { "Assets/SpacecraftEditor/Prefabs/ModularParts/ArmorFairing.prefab", typeof(DecorationPart) },
                { "Assets/SpacecraftEditor/Prefabs/ModularParts/EnergyPulse.prefab", typeof(WeaponPart) },
                { "Assets/SpacecraftEditor/Prefabs/ModularParts/KineticRepeater.prefab", typeof(WeaponPart) }
            };

            foreach (KeyValuePair<string, Type> pair in expected)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(pair.Key);
                Assert.That(prefab, Is.Not.Null, pair.Key);
                Assert.That(prefab.GetComponent(pair.Value), Is.Not.Null, pair.Key);
                Assert.That(
                    prefab.GetComponentsInChildren<Transform>(true)
                        .Any(transform => transform.name == "PCGModel"),
                    Is.True,
                    pair.Key);
                Assert.That(prefab.GetComponentInChildren<LODGroup>(true), Is.Not.Null, pair.Key);
            }
        }

        [Test]
        public void PcgFleet_HasHardpointLayoutForEveryHull()
        {
            foreach (ShipHullDefinition hull in LoadPcgHulls())
            {
                string path =
                    "Assets/Resources/Spaceflight/Pirates/Hardpoints_" +
                    hull.HullId.Replace('.', '_') +
                    ".asset";
                SpacecraftHardpointLayout layout =
                    AssetDatabase.LoadAssetAtPath<SpacecraftHardpointLayout>(path);
                Assert.That(layout, Is.Not.Null, hull.HullId);
                Assert.That(layout.HullId, Is.EqualTo(hull.HullId));
                Assert.That(
                    layout.Hardpoints.Count(point =>
                        point.Category == SpacecraftPartCategory.Thruster),
                    Is.GreaterThanOrEqualTo(2),
                    hull.HullId);
                Assert.That(
                    layout.Hardpoints.Count(point =>
                        point.Category == SpacecraftPartCategory.Weapon),
                    Is.GreaterThanOrEqualTo(2),
                    hull.HullId);
            }
        }

        [Test]
        public void PcgFleet_SurfaceLibraryUsesEightDistinctMetalPbrMaterials()
        {
            string[] names =
            {
                "deep_space_blue",
                "gunmetal",
                "ceramic_white",
                "warning_red",
                "industrial_copper",
                "explorer_green",
                "brushed_brass",
                "graphite_pitted"
            };
            var physicalProfiles = new HashSet<string>(StringComparer.Ordinal);
            foreach (string name in names)
            {
                string path = "Assets/SpacecraftEditor/Art/Materials/Paints/" + name + ".mat";
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                Assert.That(material, Is.Not.Null, path);
                Assert.That(material.GetTexture("_MainTex"), Is.Not.Null, path);
                Assert.That(material.GetTexture("_MetallicGlossMap"), Is.Not.Null, path);
                Assert.That(material.GetTexture("_BumpMap"), Is.Not.Null, path);
                Assert.That(material.GetTexture("_OcclusionMap"), Is.Not.Null, path);
                physicalProfiles.Add(
                    material.GetFloat("_Metallic").ToString("0.000") + "/" +
                    material.GetFloat("_Glossiness").ToString("0.000"));
            }
            Assert.That(physicalProfiles, Has.Count.EqualTo(names.Length));

            foreach (string name in new[]
                     {
                         "deep_space_blue",
                         "gunmetal",
                         "ceramic_white",
                         "warning_red",
                         "industrial_copper",
                         "explorer_green",
                         "brushed_brass",
                         "graphite_pitted"
                     })
            {
                const string root =
                    "Assets/SpacecraftEditor/Art/Materials/Paints/PBRLibrary/";
                foreach (string map in new[]
                         {
                             "BaseColor",
                             "Metallic",
                             "Roughness",
                             "Normal",
                             "AO",
                             "MetallicSmoothness"
                         })
                {
                    string path = root + name + "_" + map + ".png";
                    Assert.That(
                        AssetDatabase.LoadAssetAtPath<Texture2D>(path),
                        Is.Not.Null,
                        path);
                }
            }

            GameObject workshop = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Resources/Spacecraft/SpacecraftWorkshopRoot.prefab");
            Assert.That(workshop, Is.Not.Null);
            SpacecraftMaterialCatalog catalog =
                workshop.GetComponentInChildren<SpacecraftMaterialCatalog>(true);
            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog.Definitions.Count, Is.EqualTo(8));
            Assert.That(
                catalog.Definitions.Select(definition => definition.MaterialId),
                Is.EquivalentTo(names.Select(name => "paint." + name)));
        }

        static ShipHullDefinition[] LoadPcgHulls()
        {
            return AssetDatabase.FindAssets(
                    "t:ShipHullDefinition",
                    new[]
                    {
                        "Assets/SpacecraftEditor/Data",
                        "Assets/SpacecraftEditor/Data/Generated/Hulls"
                    })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct()
                .Select(AssetDatabase.LoadAssetAtPath<ShipHullDefinition>)
                .Where(hull => hull != null &&
                               (hull.HullId == "hull.balanced" ||
                                hull.HullId == "hull.spindle" ||
                                hull.HullId == "hull.saucer" ||
                                hull.HullId.Contains(".pcg.")))
                .OrderBy(hull => hull.HullId, StringComparer.Ordinal)
                .ToArray();
        }

        static int TriangleCount(LOD lod)
        {
            return lod.renderers
                .Where(renderer => renderer != null)
                .SelectMany(renderer =>
                {
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();
                    if (filter != null && filter.sharedMesh != null)
                        return new[] { filter.sharedMesh };
                    SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
                    return skinned != null && skinned.sharedMesh != null
                        ? new[] { skinned.sharedMesh }
                        : Array.Empty<Mesh>();
                })
                .Distinct()
                .Sum(TriangleCount);
        }

        static int TriangleCount(Mesh mesh)
        {
            if (mesh == null)
                return 0;
            int count = 0;
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                count += (int)mesh.GetIndexCount(submesh) / 3;
            return count;
        }
    }
}
