using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SpacecraftEditor.Tests
{
    public sealed class SpacecraftPcgAssetTests
    {
        static readonly string[] HullIds =
        {
            "hull.a30_thunderbolt",
            "hull.sf_stealth_fighter",
            "hull.sf_modular_pirate",
            "hull.sf_dropship_r35",
            "hull.sf_fighter_gr2"
        };

        [Test]
        public void ExternalFleet_RuntimeCatalogContainsExactlyFiveReviewedHulls()
        {
            GameObject workshop = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Resources/Spacecraft/SpacecraftWorkshopRoot.prefab");
            Assert.That(workshop, Is.Not.Null);
            HullCatalog catalog = workshop.GetComponentInChildren<HullCatalog>(true);
            Assert.That(catalog, Is.Not.Null);
            Assert.That(
                catalog.Definitions.Select(hull => hull.HullId),
                Is.EqualTo(HullIds));
            Assert.That(catalog.DefaultDefinition.HullId, Is.EqualTo("hull.a30_thunderbolt"));
            Assert.That(
                catalog.Definitions.Any(hull =>
                    hull.HullId.Contains(".pcg.") ||
                    hull.HullId == "hull.balanced" ||
                    hull.HullId == "hull.saucer" ||
                    hull.HullId == "hull.spindle"),
                Is.False);
        }

        [Test]
        public void ExternalFleet_HullsHaveNativeMaterialsAndSeparatePlacementMeshes()
        {
            int hullsWithSeparateGlass = 0;
            foreach (ShipHullDefinition hull in LoadExternalHulls())
            {
                Assert.That(hull.ModelPrefab, Is.Not.Null, hull.HullId);
                Assert.That(hull.Thumbnail, Is.Not.Null, hull.HullId);
                Assert.That(hull.CollisionMesh, Is.Not.Null, hull.HullId);
                Assert.That(hull.PlacementSurfaceMesh, Is.Not.Null, hull.HullId);
                Assert.That(TriangleCount(hull.CollisionMesh), Is.LessThanOrEqualTo(200), hull.HullId);
                Assert.That(TriangleCount(hull.PlacementSurfaceMesh), Is.LessThanOrEqualTo(18000), hull.HullId);
                Assert.That(
                    Mathf.Max(hull.Dimensions.x, hull.Dimensions.z),
                    Is.EqualTo(6f).Within(0.01f),
                    hull.HullId);
                Assert.That(
                    hull.ModelPrefab.GetComponentInChildren<SpacecraftPaintBinding>(true),
                    Is.Not.Null,
                    hull.HullId);
                Renderer[] renderers = hull.ModelPrefab.GetComponentsInChildren<Renderer>(true);
                Assert.That(renderers, Is.Not.Empty, hull.HullId);
                if (renderers.SelectMany(renderer => renderer.sharedMaterials)
                    .Where(material => material != null)
                    .Select(material => material.name)
                    .Any(name => name.IndexOf("Glass", StringComparison.OrdinalIgnoreCase) >= 0))
                    hullsWithSeparateGlass++;
            }
            Assert.That(hullsWithSeparateGlass, Is.GreaterThanOrEqualTo(4));
        }

        [Test]
        public void ExternalFleet_PartCatalogHasExactlyThirtyOneDefinitions()
        {
            GameObject workshop = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Resources/Spacecraft/SpacecraftWorkshopRoot.prefab");
            PartCatalog catalog = workshop.GetComponentInChildren<PartCatalog>(true);
            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog.Definitions.Count, Is.EqualTo(31));
            Assert.That(catalog.Definitions.Select(part => part.PartId).Distinct().Count(), Is.EqualTo(31));
            foreach (string id in new[]
                     {
                         "weapon.flak.s1", "weapon.flak.s2",
                         "weapon.missile_rack.s1", "weapon.missile_rack.s2",
                         "weapon.torpedo.s2",
                         "decor.hangar_deck", "decor.fighter_facility",
                         "decor.resource_platform"
                     })
            {
                ShipPartDefinition definition = catalog.Find(id);
                Assert.That(definition, Is.Not.Null, id);
                Assert.That(definition.Prefab, Is.Not.Null, id);
                Assert.That(definition.Thumbnail, Is.Not.Null, id);
            }
        }

        [Test]
        public void ExternalFleet_HasPirateLayoutForEveryActiveHull()
        {
            foreach (ShipHullDefinition hull in LoadExternalHulls())
            {
                string path =
                    "Assets/Resources/Spaceflight/Pirates/Hardpoints_" +
                    hull.HullId.Replace('.', '_') + ".asset";
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
        public void ExternalFleet_MaterialCatalogIncludesNativeRestoreSwatch()
        {
            GameObject workshop = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Resources/Spacecraft/SpacecraftWorkshopRoot.prefab");
            SpacecraftMaterialCatalog catalog =
                workshop.GetComponentInChildren<SpacecraftMaterialCatalog>(true);
            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog.Definitions.Count, Is.EqualTo(9));
            SpacecraftMaterialDefinition native =
                catalog.Definitions.Single(value =>
                    value.MaterialId == SpacecraftPaintBinding.NativePaintId);
            Assert.That(native.DisplayName, Is.EqualTo("原厂"));
            Assert.That(native.Material, Is.Null);
        }

        [Test]
        public void ExternalFleet_LegacyPcgAssetsAreArchivedOutsideResources()
        {
            Assert.That(
                AssetDatabase.IsValidFolder("Assets/SpacecraftEditor/LegacyPCG/Art/Hulls"),
                Is.True);
            Assert.That(
                AssetDatabase.IsValidFolder("Assets/SpacecraftEditor/Art/Generated/Hulls"),
                Is.False);
            string[] runtimeLegacy = AssetDatabase.FindAssets(
                "Hardpoints_hull_balanced",
                new[] { "Assets/Resources" });
            Assert.That(runtimeLegacy, Is.Empty);
        }

        static ShipHullDefinition[] LoadExternalHulls()
        {
            var lookup = AssetDatabase.FindAssets(
                    "t:ShipHullDefinition",
                    new[] { "Assets/SpacecraftEditor/ExternalFleet/Data/Hulls" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<ShipHullDefinition>)
                .Where(hull => hull != null)
                .ToDictionary(hull => hull.HullId, StringComparer.Ordinal);
            return HullIds.Select(id => lookup[id]).ToArray();
        }

        static int TriangleCount(Mesh mesh)
        {
            int count = 0;
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                count += (int)mesh.GetIndexCount(submesh) / 3;
            return count;
        }
    }
}
