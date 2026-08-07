using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityPlanet.CityPcg;

public sealed class AirCombatCityPcgTests
{
    [Test]
    public void DarkCityCatalogContainsTheExpandedSemanticFamilies()
    {
        DarkCity2UrbanCatalog catalog = AssetDatabase.LoadAssetAtPath<
            DarkCity2UrbanCatalog>(
            "Assets/CityPcgPrinciplesLab/DarkCity2Derived/DarkCity2UrbanCatalog.asset");
        Assert.That(catalog, Is.Not.Null);
        Assert.That(catalog.rooftopDecorations.Length, Is.GreaterThanOrEqualTo(15));
        Assert.That(catalog.facadeDecorations.Length, Is.GreaterThanOrEqualTo(30));
        Assert.That(catalog.streetDecorations.Length, Is.GreaterThanOrEqualTo(24));
        Assert.That(catalog.districtBuildings.Length, Is.GreaterThanOrEqualTo(5));
        Assert.That(catalog.cableRed, Is.Not.Null);
        Assert.That(catalog.cableDark, Is.Not.Null);
        Assert.That(catalog.cableBlue, Is.Not.Null);

        var roles = new HashSet<DarkCity2PlacementRole>();
        IEnumerable<GameObject> all = catalog.rooftopDecorations
            .Concat(catalog.facadeDecorations)
            .Concat(catalog.streetDecorations)
            .Concat(catalog.districtBuildings);
        foreach (GameObject prefab in all)
        {
            if (prefab == null)
                continue;
            DarkCity2AssetDescriptor descriptor =
                prefab.GetComponent<DarkCity2AssetDescriptor>();
            if (descriptor != null)
                roles.Add(descriptor.PlacementRole);
        }

        DarkCity2PlacementRole[] required =
        {
            DarkCity2PlacementRole.RoofGenerator,
            DarkCity2PlacementRole.RoofPipeFrame,
            DarkCity2PlacementRole.FacadeFireEscape,
            DarkCity2PlacementRole.FacadeBalcony,
            DarkCity2PlacementRole.FacadePipe,
            DarkCity2PlacementRole.FacadeCable,
            DarkCity2PlacementRole.FacadeShop,
            DarkCity2PlacementRole.StreetTransit,
            DarkCity2PlacementRole.StreetWarning,
            DarkCity2PlacementRole.StreetIndustrial,
            DarkCity2PlacementRole.IndustrialWarehouse,
            DarkCity2PlacementRole.IndustrialSilo,
            DarkCity2PlacementRole.TransitGarage
        };
        foreach (DarkCity2PlacementRole role in required)
            Assert.That(roles, Does.Contain(role), role.ToString());
    }

    [Test]
    public void FormalCityActuallyInstantiatesDistrictArtWithoutDecorationColliders()
    {
        GameObject template = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Resources/PlanetSurface/UrbanCombatCityTemplate.prefab");
        Assert.That(template, Is.Not.Null);
        GameObject city = UnityEngine.Object.Instantiate(template);
        try
        {
            AirCombatCityPcgLab lab = city.GetComponent<AirCombatCityPcgLab>();
            Assert.That(lab, Is.Not.Null);
            lab.ConfigureRuntimeMission(7319, AirCombatCityMission.Clearance);

            UrbanDestructibleBuilding[] destructibleBuildings = city
                .GetComponentsInChildren<UrbanDestructibleBuilding>(true);
            int largeParcels = destructibleBuildings.Count(building =>
                building.StableId.StartsWith(
                    "building.large.",
                    StringComparison.Ordinal) ||
                building.StableId.StartsWith(
                    "infill-LargeCompositeParcel",
                    StringComparison.Ordinal));
            int smallParcels = destructibleBuildings.Count(building =>
                building.StableId.StartsWith(
                    "building.small-gapfill.",
                    StringComparison.Ordinal) ||
                building.StableId.StartsWith(
                    "infill-SmallClusterParcel",
                    StringComparison.Ordinal));
            Assert.That(largeParcels, Is.GreaterThanOrEqualTo(100));
            Assert.That(largeParcels, Is.GreaterThanOrEqualTo(smallParcels * 2),
                "Large parcels must form the city body; small parcels only fill gaps.");
            Assert.That(
                city.GetComponentsInChildren<UrbanCityGlowRuntime>(true),
                Has.Length.EqualTo(1));
            Shader bloomShader = Shader.Find("Hidden/UnityPlanet/CityBloom");
            Assert.That(bloomShader, Is.Not.Null);
            Assert.That(bloomShader.isSupported, Is.True);
            Assert.That(bloomShader.passCount, Is.EqualTo(4));

            Transform[] objects = city.GetComponentsInChildren<Transform>(true);
            Assert.That(objects.Count(item =>
                item.name.StartsWith("RoofEquipment_", StringComparison.Ordinal)),
                Is.GreaterThanOrEqualTo(60));
            Assert.That(objects.Count(item =>
                item.name.StartsWith("FacadeAttachment_", StringComparison.Ordinal)),
                Is.GreaterThanOrEqualTo(40));
            Assert.That(objects.Count(item =>
                item.name.StartsWith("StreetUtility_", StringComparison.Ordinal)),
                Is.GreaterThanOrEqualTo(12));
            UrbanDestructibleBridge[] bridges = city
                .GetComponentsInChildren<UrbanDestructibleBridge>(true);
            Assert.That(bridges, Has.Length.GreaterThanOrEqualTo(150));
            Assert.That(
                lab.LastCrossRoadBlockSkybridgeCount,
                Is.GreaterThanOrEqualTo(Mathf.CeilToInt(bridges.Length * 0.5f)),
                "At least half of the physical skybridges must cross a road-separated city block.");
            Assert.That(bridges.Count(bridge =>
                    bridge.name.EndsWith(
                        "_CrossRoadBlock",
                        StringComparison.Ordinal)),
                Is.EqualTo(lab.LastCrossRoadBlockSkybridgeCount));
            IGrouping<string, UrbanDestructibleBridge>[] bridgePairs = bridges
                .GroupBy(BridgePairKey)
                .ToArray();
            Assert.That(bridgePairs.Count(pair => pair.Count() >= 2),
                Is.GreaterThanOrEqualTo(12),
                "The same two buildings must be able to carry several bridges.");
            Assert.That(bridgePairs.Count(HasIrregularVerticalSpacing),
                Is.GreaterThanOrEqualTo(3),
                "Multi-level bridge stacks must not become a fixed-height grid.");
            Transform[] aerialCableLinks = objects.Where(item =>
                item.name.StartsWith("AerialCableLink_", StringComparison.Ordinal))
                .ToArray();
            Assert.That(aerialCableLinks, Has.Length.GreaterThanOrEqualTo(18));
            Assert.That(aerialCableLinks.All(item =>
                item.GetComponentsInChildren<AerialCableCurve>(true).Length == 3),
                Is.True,
                "Each aerial link must remain a red/dark/blue three-tube bundle.");
            Assert.That(aerialCableLinks.SelectMany(item =>
                item.GetComponentsInChildren<Collider>(true)),
                Is.Empty,
                "Aerial cables are visual connections and must not change ship physics.");
            Assert.That(objects.Count(item =>
                item.name.StartsWith("TacticalClosePair_", StringComparison.Ordinal) &&
                item.name.IndexOf("CompactHullGate", StringComparison.Ordinal) >= 0),
                Is.GreaterThanOrEqualTo(2));
            Assert.That(objects.Count(item =>
                item.name.StartsWith("TacticalClosePair_", StringComparison.Ordinal) &&
                item.name.IndexOf("StandardSkillGate", StringComparison.Ordinal) >= 0),
                Is.GreaterThanOrEqualTo(2));
            Assert.That(objects.Count(item =>
                item.name.StartsWith("TacticalClosePair_", StringComparison.Ordinal) &&
                item.name.IndexOf("HeavyHullGate", StringComparison.Ordinal) >= 0),
                Is.GreaterThanOrEqualTo(2));

            string[] districts =
            {
                "Commercial", "Industrial", "Transit", "Mixed", "Service"
            };
            foreach (string district in districts)
            {
                Assert.That(objects.Any(item =>
                    item.name.IndexOf(district, StringComparison.Ordinal) >= 0),
                    Is.True,
                    district);
            }

            Transform decorationRoot = objects.FirstOrDefault(item =>
                item.name.StartsWith("04B_", StringComparison.Ordinal));
            Assert.That(decorationRoot, Is.Not.Null);
            Assert.That(
                decorationRoot.GetComponentsInChildren<Collider>(true),
                Is.Empty,
                "Decorative fire escapes, cables and signs must not snag the modular ship.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(city);
        }
    }

    static string BridgePairKey(UrbanDestructibleBridge bridge)
    {
        string value = bridge != null ? bridge.name : string.Empty;
        int pairStartMarker = value.IndexOf('_', "Skybridge_".Length);
        int layerMarker = value.IndexOf("_Layer", StringComparison.Ordinal);
        return pairStartMarker >= 0 && layerMarker > pairStartMarker
            ? value.Substring(
                pairStartMarker + 1,
                layerMarker - pairStartMarker - 1)
            : value;
    }

    static bool HasIrregularVerticalSpacing(
        IGrouping<string, UrbanDestructibleBridge> pair)
    {
        float[] heights = pair
            .Select(bridge => bridge.DestructionBounds.center.y)
            .OrderBy(height => height)
            .ToArray();
        if (heights.Length < 3)
            return false;
        var gaps = new List<float>();
        for (int index = 1; index < heights.Length; index++)
            gaps.Add(heights[index] - heights[index - 1]);
        return gaps.Max() - gaps.Min() >= 1.5f && gaps.Min() >= 18f;
    }

    [TestCase(0, 14)]
    [TestCase(1, 13)]
    [TestCase(2, 12)]
    [TestCase(3, 10)]
    [TestCase(4, 9)]
    [TestCase(5, 8)]
    public void BossDifficultyControlsGuaranteedChokeCount(
        int tier,
        int expected)
    {
        Assert.That(
            AirCombatCityPcgLab.ResolveBossTacticalChokeTarget(tier),
            Is.EqualTo(expected));
    }

    [Test]
    public void BossCityBuildsDenseValidatedTacticalBridgeNetwork()
    {
        GameObject template = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Resources/PlanetSurface/UrbanCombatCityTemplate.prefab");
        Assert.That(template, Is.Not.Null);
        GameObject city = UnityEngine.Object.Instantiate(template);
        try
        {
            AirCombatCityPcgLab lab = city.GetComponent<AirCombatCityPcgLab>();
            lab.ConfigureRuntimeMission(
                7319,
                AirCombatCityMission.BossEncounter,
                0);

            Assert.That(lab.HasValidPlan, Is.True, lab.LastSummary);
            Assert.That(
                lab.LastSkybridgeCount,
                Is.GreaterThanOrEqualTo(
                    AirCombatCityPcgLab.BossCitySkybridgeTarget));
            Assert.That(
                lab.LastCrossRoadBlockSkybridgeCount,
                Is.GreaterThanOrEqualTo(
                    Mathf.CeilToInt(lab.LastSkybridgeCount * 0.5f)));
            AirCombatTacticalSkybridge[] tactical = city
                .GetComponentsInChildren<AirCombatTacticalSkybridge>(true);
            IGrouping<string, AirCombatTacticalSkybridge>[] groups = tactical
                .GroupBy(item => item.GroupId)
                .ToArray();
            Assert.That(groups, Has.Length.EqualTo(14));
            foreach (IGrouping<string, AirCombatTacticalSkybridge> group in groups)
            {
                Assert.That(group.Count(), Is.EqualTo(2));
                Assert.That(
                    group.Select(item => item.Role),
                    Does.Contain(AirCombatSkybridgeRole.TacticalLower));
                Assert.That(
                    group.Select(item => item.Role),
                    Does.Contain(AirCombatSkybridgeRole.TacticalUpper));
                Assert.That(
                    group.Min(item => item.ClearHeight),
                    Is.GreaterThanOrEqualTo(
                        AirCombatCityPcgLab.TacticalChokeClearHeight));
                Assert.That(
                    group.Min(item => item.ClearWidth),
                    Is.GreaterThanOrEqualTo(
                        AirCombatCityPcgLab.TacticalChokeClearWidth));
                AirCombatTacticalSkybridge lower = group.Single(item =>
                    item.Role == AirCombatSkybridgeRole.TacticalLower);
                AirCombatTacticalSkybridge upper = group.Single(item =>
                    item.Role == AirCombatSkybridgeRole.TacticalUpper);
                Bounds lowerCollision = ResolveColliderBounds(lower.gameObject);
                Bounds upperCollision = ResolveColliderBounds(upper.gameObject);
                Assert.That(
                    upperCollision.min.y - lowerCollision.max.y,
                    Is.GreaterThanOrEqualTo(
                        AirCombatCityPcgLab.TacticalChokeClearHeight - 0.01f));
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(city);
        }
    }

    static Bounds ResolveColliderBounds(GameObject root)
    {
        Collider[] colliders = root.GetComponentsInChildren<Collider>(true)
            .Where(item => item.enabled && !item.isTrigger)
            .ToArray();
        Assert.That(colliders, Is.Not.Empty);
        Bounds result = colliders[0].bounds;
        for (int index = 1; index < colliders.Length; index++)
            result.Encapsulate(colliders[index].bounds);
        return result;
    }

    [Test]
    public void RepresentativeSeedsKeepMixedSkylineAndNoVerticalBypass()
    {
        foreach (AirCombatCityMission mission in
                 System.Enum.GetValues(typeof(AirCombatCityMission)))
        {
            for (int index = 0; index < 12; index++)
            {
                var settings = new AirCombatCitySettings
                {
                    seed = 7319 + index * 7919,
                    mission = mission
                };
                AirCombatCityGenerator.Generate(
                    settings,
                    out AirCombatCityReport report);

                Assert.That(
                    report.valid,
                    Is.True,
                    mission + " seed " + settings.seed + ": " +
                    report.failureReason);
                Assert.That(report.heightMixDistributed, Is.True);
                Assert.That(report.highAltitudeBypassControlled, Is.True);
                Assert.That(report.routesClear, Is.True);
            }
        }
    }

    [Test]
    public void SameSeedIsDeterministicAndDifferentSeedChangesLayout()
    {
        var settings = new AirCombatCitySettings();
        AirCombatCityPlan first = AirCombatCityGenerator.Generate(
            settings,
            out AirCombatCityReport firstReport);
        AirCombatCityPlan second = AirCombatCityGenerator.Generate(
            settings,
            out AirCombatCityReport secondReport);

        Assert.That(first, Is.Not.Null);
        Assert.That(second, Is.Not.Null);
        Assert.That(firstReport.valid, Is.True, firstReport.failureReason);
        Assert.That(secondReport.checksum, Is.EqualTo(firstReport.checksum));

        settings.seed++;
        AirCombatCityGenerator.Generate(settings, out AirCombatCityReport changed);
        Assert.That(changed.checksum, Is.Not.EqualTo(firstReport.checksum));
    }

    [Test]
    public void ClearanceMixesLowAndMediumCoverUnderTheManeuverBowl()
    {
        var settings = new AirCombatCitySettings
        {
            mission = AirCombatCityMission.Clearance
        };
        AirCombatCityPlan plan = AirCombatCityGenerator.Generate(
            settings,
            out AirCombatCityReport report);
        int lowCoverCount = 0;
        int mediumCoverCount = 0;
        float radius = settings.ValidatedCopy().ManeuverDiameter * 0.5f;
        for (int i = 0; i < plan.buildings.Count; i++)
        {
            AirCombatBuildingLot building = plan.buildings[i];
            Vector2 position = new Vector2(
                building.center.x,
                building.center.z);
            if (position.magnitude < radius &&
                building.band == AirCombatBuildingBand.Low)
            {
                lowCoverCount++;
            }
            if (position.magnitude < radius &&
                building.band == AirCombatBuildingBand.Medium)
            {
                mediumCoverCount++;
            }
        }

        Assert.That(report.valid, Is.True, report.failureReason);
        Assert.That(report.routesClear, Is.True);
        Assert.That(report.firstContactSeconds, Is.GreaterThanOrEqualTo(4.5f));
        Assert.That(report.highAltitudeBypassControlled, Is.True);
        Assert.That(report.skylineAnchorQuadrants, Is.EqualTo(4));
        Assert.That(
            report.verticalOverflightGap,
            Is.LessThan(settings.wingspan * 0.45f + 8f));
        Assert.That(lowCoverCount, Is.GreaterThanOrEqualTo(4));
        Assert.That(mediumCoverCount, Is.GreaterThanOrEqualTo(4));
    }

    [Test]
    public void FacilityAssaultBuildsThreeReachableCores()
    {
        var settings = new AirCombatCitySettings
        {
            mission = AirCombatCityMission.FacilityAssault
        };
        AirCombatCityPlan plan = AirCombatCityGenerator.Generate(
            settings,
            out AirCombatCityReport report);

        Assert.That(report.valid, Is.True, report.failureReason);
        Assert.That(report.facilityReachable, Is.True);
        Assert.That(plan.facilityCores.Count, Is.EqualTo(3));
        Assert.That(report.routesClear, Is.True);
    }

    [Test]
    public void FormalCityHasCombatHeightCoverAndSpawnCompatibleIngresses()
    {
        foreach (AirCombatCityMission mission in
                 System.Enum.GetValues(typeof(AirCombatCityMission)))
        {
            var settings = new AirCombatCitySettings
            {
                seed = 7319,
                mission = mission
            };
            AirCombatCityPlan plan = AirCombatCityGenerator.Generate(
                settings,
                out AirCombatCityReport report);
            AirCombatCitySettings validated = settings.ValidatedCopy();
            int centralUsefulCover = 0;
            float centralRadius = validated.ManeuverDiameter * 0.5f;
            float farthestIngress = 0f;

            for (int i = 0; i < plan.buildings.Count; i++)
            {
                AirCombatBuildingLot building = plan.buildings[i];
                Vector2 position = new Vector2(
                    building.center.x,
                    building.center.z);
                if (position.magnitude < centralRadius &&
                    building.size.y >= validated.lowAltitude + 12f)
                {
                    centralUsefulCover++;
                }
            }
            for (int i = 0; i < plan.ingresses.Count; i++)
            {
                Vector3 position = plan.ingresses[i].position;
                farthestIngress = Mathf.Max(
                    farthestIngress,
                    new Vector2(position.x, position.z).magnitude);
            }

            Assert.That(report.valid, Is.True, report.failureReason);
            Assert.That(report.maximumTowerHeight, Is.GreaterThanOrEqualTo(330f));
            Assert.That(centralUsefulCover, Is.GreaterThanOrEqualTo(8));
            Assert.That(farthestIngress, Is.LessThanOrEqualTo(510f));
        }
    }
}
