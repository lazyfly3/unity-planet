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
    public void RuntimeFacilitySeedPassesOneBoundedPlanAttempt()
    {
        var settings = new AirCombatCitySettings
        {
            seed = 206718097,
            mission = AirCombatCityMission.FacilityAssault,
            maximumAttempts = 1,
            combatDifficulty = CombatCityDifficultyProfile.CreateForTier(
                0,
                AirCombatCityMission.FacilityAssault)
        };

        AirCombatCityPlan plan = AirCombatCityGenerator.Generate(
            settings,
            out AirCombatCityReport report);

        Assert.That(plan, Is.Not.Null);
        Assert.That(report.valid, Is.True, report.failureReason);
        Assert.That(report.attempts, Is.EqualTo(1));
        Assert.That(report.routesClear, Is.True);
        Assert.That(report.buildingRoadOverlapCount, Is.Zero);
        Assert.That(report.recoveryPocketOutputBlockedCount, Is.EqualTo(2));
        Assert.That(report.occlusionBreakCount, Is.GreaterThanOrEqualTo(4));
    }

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
            Transform[] junctions = objects.Where(item =>
                    item.name.StartsWith(
                        "Junction_Fallback_",
                        StringComparison.Ordinal))
                .ToArray();
            Assert.That(junctions, Is.Not.Empty);
            foreach (Transform junction in junctions)
            {
                MeshFilter filter = junction.GetComponent<MeshFilter>();
                Assert.That(filter, Is.Not.Null, junction.name);
                Assert.That(filter.sharedMesh, Is.Not.Null, junction.name);
                Bounds patch = filter.sharedMesh.bounds;
                float verticalWidth = lab.Plan.roads
                    .Where(road =>
                        Mathf.Abs(road.end.z - road.start.z) >=
                        Mathf.Abs(road.end.x - road.start.x) &&
                        Mathf.Abs(road.start.x - patch.center.x) < 0.05f &&
                        CoordinateWithinRoad(
                            patch.center.z,
                            road.start.z,
                            road.end.z))
                    .Max(road => road.width);
                float horizontalWidth = lab.Plan.roads
                    .Where(road =>
                        Mathf.Abs(road.end.x - road.start.x) >
                        Mathf.Abs(road.end.z - road.start.z) &&
                        Mathf.Abs(road.start.z - patch.center.z) < 0.05f &&
                        CoordinateWithinRoad(
                            patch.center.x,
                            road.start.x,
                            road.end.x))
                    .Max(road => road.width);
                Assert.That(
                    patch.size.x,
                    Is.EqualTo(verticalWidth + 15f).Within(0.05f),
                    junction.name + " must reclaim both 7.5 m road shoulders.");
                Assert.That(
                    patch.size.z,
                    Is.EqualTo(horizontalWidth + 15f).Within(0.05f),
                    junction.name + " must reclaim both 7.5 m road shoulders.");
            }
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
                Is.GreaterThanOrEqualTo(lab.Settings.crossBlockSkybridgeTarget),
                "Cross-block bridges must satisfy the post-merge block quota.");
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
            Collider[] cableTriggers = aerialCableLinks.SelectMany(item =>
                    item.GetComponentsInChildren<Collider>(true))
                .ToArray();
            Assert.That(cableTriggers, Is.Not.Empty);
            Assert.That(cableTriggers.All(item => item.isTrigger), Is.True,
                "Aerial cable gameplay contacts must remain non-blocking triggers.");
            Assert.That(aerialCableLinks.All(item =>
                    item.GetComponent<AerialCableSlowHazard>() != null),
                Is.True,
                "Every generated cable bundle must expose the shared slowdown hazard.");
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
            Assert.That(objects
                    .Where(item => item.name.StartsWith(
                        "TacticalClosePair_",
                        StringComparison.Ordinal))
                    .All(item => ReadNamedGapMetres(item.name) >=
                                 lab.Settings.MinimumDefaultPresetBuildingGap),
                Is.True,
                "Every authored building gate must pass the default preset envelope.");

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

    static bool CoordinateWithinRoad(
        float coordinate,
        float first,
        float second)
    {
        return coordinate >= Mathf.Min(first, second) - 0.05f &&
               coordinate <= Mathf.Max(first, second) + 0.05f;
    }

    [Test]
    public void BuildingCatalogRejectsSparsePillarFoundations()
    {
        const string TemplatePath =
            "Assets/Resources/PlanetSurface/UrbanCombatCityTemplate.prefab";
        const string PrefabFolder =
            "Assets/CityPcgPrinciplesLab/DarkCity2Derived/Prefabs/";
        GameObject template = AssetDatabase.LoadAssetAtPath<GameObject>(
            TemplatePath);
        Assert.That(template, Is.Not.Null);
        AirCombatCityPcgLab lab = template.GetComponent<AirCombatCityPcgLab>();
        Assert.That(lab, Is.Not.Null);
        System.Reflection.MethodInfo supportCheck =
            typeof(AirCombatCityPcgLab).GetMethod(
                "IsGroundSupportedBuildingPrefab",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);
        Assert.That(supportCheck, Is.Not.Null);

        string[] rejected =
        {
            "DC2_Building_02_L1",
            "DC2_Building_07_L1",
            "DC2_Building_14_L1"
        };
        string[] supported =
        {
            "DC2_Building_01_L1",
            "DC2_Building_03_L1",
            "DC2_Skyscraper_03"
        };
        foreach (string name in rejected)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                PrefabFolder + name + ".prefab");
            Assert.That(prefab, Is.Not.Null, name);
            Assert.That(
                (bool)supportCheck.Invoke(lab, new object[] { prefab }),
                Is.False,
                name + " has too little real ground support for an ordinary building.");
        }
        foreach (string name in supported)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                PrefabFolder + name + ".prefab");
            Assert.That(prefab, Is.Not.Null, name);
            Assert.That(
                (bool)supportCheck.Invoke(lab, new object[] { prefab }),
                Is.True,
                name + " is a control building with a real foundation.");
        }
    }

    [Test]
    public void DesignerRebuildKeepsDiagnosticGeometryOutOfGameCameras()
    {
        GameObject template = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Resources/PlanetSurface/UrbanCombatCityTemplate.prefab");
        Assert.That(template, Is.Not.Null);
        GameObject city = UnityEngine.Object.Instantiate(template);
        try
        {
            AirCombatCityPcgLab lab = city.GetComponent<AirCombatCityPcgLab>();
            CombatCityPcgDesignProfile profile =
                Resources.Load<CombatCityPcgDesignProfile>(
                    AirCombatCityPcgLab.DefaultDesignProfileResourcePath);
            lab.ConfigureDesignProfile(
                profile,
                AirCombatCityMission.Clearance,
                0,
                7319,
                true);

            string[] debugPrefixes = { "01_", "02_", "05_", "06_" };
            Transform[] debugRoots = city
                .GetComponentsInChildren<Transform>(true)
                .Where(item => debugPrefixes.Any(prefix =>
                    item.name.StartsWith(prefix, StringComparison.Ordinal)))
                .ToArray();
            Assert.That(debugRoots, Has.Length.EqualTo(4));
            Assert.That(
                debugRoots.All(item => !item.gameObject.activeSelf),
                Is.True,
                "Scene diagnostics must be drawn by Handles, not Game-camera renderers.");
            Assert.That(
                debugRoots.SelectMany(item =>
                    item.GetComponentsInChildren<Renderer>(true)),
                Is.Not.Empty,
                "The regression check must cover the legacy route/ring renderers.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(city);
        }
    }

    [Test]
    public void BaseParcelsReserveTheDefaultPresetBuildingGap()
    {
        var requested = new AirCombatCitySettings
        {
            seed = 7319,
            mission = AirCombatCityMission.Clearance
        };
        AirCombatCitySettings settings = requested.ValidatedCopy();
        AirCombatCityPlan plan = AirCombatCityGenerator.Generate(
            settings,
            out AirCombatCityReport report);

        Assert.That(plan, Is.Not.Null);
        Assert.That(report.valid, Is.True, report.failureReason);
        float maximumParcelSpan = settings.buildingSpacing -
                                  settings.MinimumDefaultPresetBuildingGap;
        AirCombatBuildingLot[] baseParcels = plan.buildings
            .Where(item =>
                item.stableId.StartsWith("building.large.", StringComparison.Ordinal) ||
                item.stableId.StartsWith("building.standard.", StringComparison.Ordinal) ||
                item.stableId.StartsWith("building.small-gapfill.", StringComparison.Ordinal))
            .ToArray();
        Assert.That(baseParcels, Is.Not.Empty);
        Assert.That(baseParcels.All(item =>
                item.size.x <= maximumParcelSpan + 0.01f &&
                item.size.z <= maximumParcelSpan + 0.01f),
            Is.True,
            "Adjacent base-grid buildings must leave the configured flyable gap.");
    }

    [Test]
    public void ExcessiveSkybridgeQuotaBuildsMaximumLegalNetwork()
    {
        GameObject template = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Resources/PlanetSurface/UrbanCombatCityTemplate.prefab");
        Assert.That(template, Is.Not.Null);
        GameObject city = UnityEngine.Object.Instantiate(template);
        try
        {
            AirCombatCityPcgLab lab = city.GetComponent<AirCombatCityPcgLab>();
            lab.Settings.seed = 1321;
            lab.Settings.mission = AirCombatCityMission.Clearance;
            lab.Settings.intraBlockSkybridgeTarget = 62;
            lab.Settings.crossBlockSkybridgeTarget = 80;
            lab.Settings.skybridgeMaximumSegmentCount = 1;
            lab.Rebuild();

            Assert.That(lab.Report.skybridgeNetworkValid, Is.True,
                lab.LastSummary);
            Assert.That(lab.LastCrossRoadBlockSkybridgeCount,
                Is.LessThan(lab.Settings.crossBlockSkybridgeTarget));
            Assert.That(lab.LastCrossRoadBlockSkybridgeCount,
                Is.GreaterThan(0));
            Assert.That(lab.LastSkybridgeCount,
                Is.GreaterThanOrEqualTo(
                    lab.Settings.intraBlockSkybridgeTarget));
            Assert.That(
                city.GetComponentsInChildren<UrbanDestructibleBridge>(true),
                Has.Length.EqualTo(lab.LastSkybridgeCount));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(city);
        }
    }

    [Test]
    public void ModularSkybridgesReachCrossBlockQuotaWithoutOverstretching()
    {
        GameObject template = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Resources/PlanetSurface/UrbanCombatCityTemplate.prefab");
        Assert.That(template, Is.Not.Null);
        GameObject city = UnityEngine.Object.Instantiate(template);
        try
        {
            AirCombatCityPcgLab lab = city.GetComponent<AirCombatCityPcgLab>();
            lab.Settings.seed = 1321;
            lab.Settings.mission = AirCombatCityMission.Clearance;
            lab.Settings.intraBlockSkybridgeTarget = 62;
            lab.Settings.crossBlockSkybridgeTarget = 80;
            lab.Settings.skybridgeMaximumSegmentCount = 3;
            lab.Rebuild();

            Assert.That(lab.Report.skybridgeNetworkValid, Is.True,
                lab.LastSummary);
            Assert.That(lab.LastCrossBlockSkybridgeCandidateCount,
                Is.GreaterThanOrEqualTo(
                    lab.Settings.crossBlockSkybridgeTarget));
            Assert.That(lab.LastCrossRoadBlockSkybridgeCount,
                Is.EqualTo(lab.Settings.crossBlockSkybridgeTarget));
            Assert.That(lab.LastSkybridgeCount,
                Is.EqualTo(
                    lab.Settings.intraBlockSkybridgeTarget +
                    lab.Settings.crossBlockSkybridgeTarget));

            DarkCity2AssetDescriptor[] spans = city
                .GetComponentsInChildren<DarkCity2AssetDescriptor>(true)
                .Where(item => item.name.StartsWith(
                    "BridgeSpan_",
                    StringComparison.Ordinal))
                .ToArray();
            Assert.That(spans.Any(item => item.name.Contains("of2")), Is.True,
                "The ordinary network should use modular spans when one safe span is too short.");
            Assert.That(spans.All(item =>
                    item.transform.localScale.z + 0.001f >=
                    item.AllowedStretch.x &&
                    item.transform.localScale.z <=
                    item.AllowedStretch.y + 0.001f),
                Is.True,
                "Every modular span must remain inside its catalog stretch limits.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(city);
        }
    }

    [Test]
    public void ReportedRuntimeClearanceSeedBuildsBothCollapseAmbushBridges()
    {
        GameObject template = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Resources/PlanetSurface/UrbanCombatCityTemplate.prefab");
        Assert.That(template, Is.Not.Null);
        GameObject city = UnityEngine.Object.Instantiate(template);
        try
        {
            AirCombatCityPcgLab lab = city.GetComponent<AirCombatCityPcgLab>();
            CombatCityPcgDesignProfile profile =
                Resources.Load<CombatCityPcgDesignProfile>(
                    AirCombatCityPcgLab.DefaultDesignProfileResourcePath);
            Assert.That(profile, Is.Not.Null);
            lab.ConfigureDesignProfile(
                profile,
                AirCombatCityMission.Clearance,
                0,
                898490944);

            Assert.That(lab.Report.resolvedSeed, Is.EqualTo(-884632481));
            Assert.That(lab.HasValidPlan, Is.True, lab.LastSummary);
            Assert.That(lab.Report.skybridgeNetworkValid, Is.True);
            Assert.That(lab.Report.destructionAmbushBridgeCount,
                Is.EqualTo(2));
            Assert.That(lab.LastDestructionBridgeCandidateBuildingCount,
                Is.EqualTo(2));
            Assert.That(lab.LastSkybridgeCount,
                Is.GreaterThanOrEqualTo(
                    AirCombatCityPcgLab.MinimumCitySkybridges));

            UrbanDestructibleBridge[] destructionBridges = city
                .GetComponentsInChildren<UrbanDestructibleBridge>(true)
                .Where(item => item.name.Contains(
                    "collapse-candidate"))
                .ToArray();
            Assert.That(destructionBridges, Has.Length.EqualTo(2));
            DarkCity2AssetDescriptor[] modularSpans = destructionBridges
                .SelectMany(item => item.GetComponentsInChildren<
                    DarkCity2AssetDescriptor>(true))
                .Where(item => item.name.StartsWith(
                    "BridgeSpan_",
                    StringComparison.Ordinal))
                .ToArray();
            Assert.That(modularSpans, Is.Not.Empty);
            Assert.That(modularSpans.Any(item => item.name.Contains("of2")),
                Is.True,
                "The reported seed must use the legal two-span fallback.");
            Assert.That(modularSpans.All(item =>
                    item.transform.localScale.z + 0.001f >=
                    item.AllowedStretch.x &&
                    item.transform.localScale.z <=
                    item.AllowedStretch.y + 0.001f),
                Is.True,
                "Every modular span must remain inside its catalog stretch limits.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(city);
        }
    }

    [Test]
    public void RuntimeMissionKeepsRequestedSeedAndBuildsWithoutOuterFallback()
    {
        GameObject template = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Resources/PlanetSurface/UrbanCombatCityTemplate.prefab");
        Assert.That(template, Is.Not.Null);
        GameObject city = UnityEngine.Object.Instantiate(template);
        try
        {
            AirCombatCityPcgLab lab = city.GetComponent<AirCombatCityPcgLab>();
            lab.ConfigureRuntimeMission(
                898435511,
                AirCombatCityMission.Clearance,
                0);

            Assert.That(lab.Settings.seed, Is.EqualTo(898435511));
            Assert.That(lab.Report.requestedSeed, Is.EqualTo(898435511));
            Assert.That(lab.Report.attempts, Is.EqualTo(1), lab.LastSummary);
            Assert.That(lab.HasValidPlan, Is.True, lab.LastSummary);
            Assert.That(lab.Report.skybridgeNetworkValid, Is.True);
            Assert.That(lab.LastSkybridgeCount,
                Is.GreaterThanOrEqualTo(
                    AirCombatCityPcgLab.MinimumCitySkybridges));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(city);
        }
    }

    static float ReadNamedGapMetres(string objectName)
    {
        int marker = objectName.IndexOf("_Gap", StringComparison.Ordinal);
        Assert.That(marker, Is.GreaterThanOrEqualTo(0), objectName);
        int start = marker + 4;
        int end = objectName.IndexOf("m_", start, StringComparison.Ordinal);
        Assert.That(end, Is.GreaterThan(start), objectName);
        return float.Parse(
            objectName.Substring(start, end - start),
            System.Globalization.CultureInfo.InvariantCulture);
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
                    lab.Settings.bossCrossBlockSkybridgeTarget));
            AirCombatTacticalSkybridge[] tactical = city
                .GetComponentsInChildren<AirCombatTacticalSkybridge>(true);
            IGrouping<string, AirCombatTacticalSkybridge>[] groups = tactical
                .Where(item =>
                    item.Role == AirCombatSkybridgeRole.TacticalLower ||
                    item.Role == AirCombatSkybridgeRole.TacticalUpper)
                .GroupBy(item => item.GroupId)
                .ToArray();
            Assert.That(groups, Has.Length.EqualTo(14));
            Assert.That(lab.Report.boundTacticalChokeCount,
                Is.EqualTo(groups.Length));
            Assert.That(lab.Plan.opportunities.Count(opportunity =>
                    opportunity.kind == TacticalOpportunityKind.TacticalChoke &&
                    opportunity.physicalFeatureCount == 2 &&
                    !string.IsNullOrEmpty(opportunity.runtimeBindingId)),
                Is.EqualTo(groups.Length));
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
            AirCombatTacticalSkybridge[] ambushBridges = tactical
                .Where(item =>
                    item.Role == AirCombatSkybridgeRole.DestructionAmbush)
                .ToArray();
            Assert.That(ambushBridges, Has.Length.EqualTo(2));
            Assert.That(lab.Report.destructionAmbushBridgeCount, Is.EqualTo(2));
            Assert.That(ambushBridges.Select(item => item.GroupId).Distinct().Count(),
                Is.EqualTo(2));
            Assert.That(AirCombatCityPcgLab.TacticalChokeClearWidth,
                Is.GreaterThanOrEqualTo(
                    lab.Settings.wingspan +
                    lab.Settings.combatSpeed * 0.16f * 1.5f));
            Transform[] attackPerchCover = city
                .GetComponentsInChildren<Transform>(true)
                .Where(item => item.name.StartsWith("FortifiedAttackPerch_"))
                .ToArray();
            Assert.That(attackPerchCover, Is.Empty);
            Assert.That(lab.Plan.opportunities.Any(item =>
                item.kind == TacticalOpportunityKind.AttackPerch), Is.False);
            Transform[] airWalls = city
                .GetComponentsInChildren<Transform>(true)
                .Where(item => item.name.StartsWith("BoundaryAirWall_"))
                .ToArray();
            Assert.That(airWalls, Has.Length.EqualTo(4));
            Assert.That(airWalls.All(item =>
            {
                BoxCollider wallCollider = item.GetComponent<BoxCollider>();
                return wallCollider != null &&
                       wallCollider.enabled &&
                       !wallCollider.isTrigger &&
                       wallCollider.size.y > lab.Settings.maximumAltitude;
            }), Is.True);
            Assert.That(city.GetComponentsInChildren<Transform>(true).Any(item =>
                    item.name.StartsWith("EnemyIngress_") ||
                    item.name.Contains("route.ingress")),
                Is.False);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(city);
        }
    }

    [Test]
    public void MapSixBossSeedBuildsValidatedRuntimeCity()
    {
        GameObject template = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Resources/PlanetSurface/UrbanCombatCityTemplate.prefab");
        Assert.That(template, Is.Not.Null);
        GameObject city = UnityEngine.Object.Instantiate(template);
        try
        {
            AirCombatCityPcgLab lab = city.GetComponent<AirCombatCityPcgLab>();
            lab.ConfigureRuntimeMission(
                -1610282716,
                AirCombatCityMission.BossEncounter,
                4);

            Assert.That(lab.HasValidPlan, Is.True, lab.LastSummary);
            Assert.That(lab.Report.requestedSeed, Is.EqualTo(-1610282716));
            Assert.That(lab.Report.combatRegionsPhysical, Is.True);
            Assert.That(lab.Report.occlusionBoundaryTowerCount,
                Is.GreaterThanOrEqualTo(7));
            Assert.That(lab.Report.exposureShortcutSavingRatio,
                Is.InRange(
                    CombatDrivenCityPcgPlanner
                        .MinimumExposureShortcutSavingRatio,
                    CombatDrivenCityPcgPlanner
                        .MaximumExposureShortcutSavingRatio));
            Assert.That(lab.Report.skybridgeNetworkValid, Is.True);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(city);
        }
    }

    [Test]
    public void HighestBossTierKeepsExposureShortcutWithinPhysicalBudget()
    {
        var settings = new AirCombatCitySettings
        {
            seed = -1610282716,
            mission = AirCombatCityMission.BossEncounter,
            combatDifficulty = CombatCityDifficultyProfile.CreateForTier(
                5,
                AirCombatCityMission.BossEncounter)
        };

        AirCombatCityGenerator.Generate(
            settings,
            out AirCombatCityReport report);

        Assert.That(report.valid, Is.True, report.failureReason);
        Assert.That(report.combatRegionsPhysical, Is.True);
        Assert.That(report.exposureShortcutSavingRatio,
            Is.InRange(
                CombatDrivenCityPcgPlanner
                    .MinimumExposureShortcutSavingRatio,
                CombatDrivenCityPcgPlanner
                    .MaximumExposureShortcutSavingRatio));
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
    public void GeneratedCombatCityDoesNotReserveRemovedDangerPark()
    {
        var settings = new AirCombatCitySettings { seed = 7319 };
        AirCombatCityPlan plan = AirCombatCityGenerator.Generate(
            settings,
            out AirCombatCityReport report);

        Assert.That(report.valid, Is.True, report.failureReason);
        Assert.That(report.tacticalRolesComplete, Is.True);
        Assert.That(
            plan.volumes.Any(volume =>
                volume.stableId.IndexOf(
                    "danger-plaza",
                    StringComparison.OrdinalIgnoreCase) >= 0),
            Is.False);
    }

    [Test]
    public void EnvironmentalTrapDistributionControlsMoveLegalTrapCandidates()
    {
        var innerSettings = new AirCombatCitySettings
        {
            seed = 7319,
            environmentalTrapRandomness = 0f,
            environmentalTrapEdgeBias = 0f
        };
        var outerSettings = new AirCombatCitySettings
        {
            seed = innerSettings.seed,
            environmentalTrapRandomness = 0f,
            environmentalTrapEdgeBias = 1f
        };

        AirCombatCityPlan innerPlan = AirCombatCityGenerator.Generate(
            innerSettings,
            out AirCombatCityReport innerReport);
        AirCombatCityPlan outerPlan = AirCombatCityGenerator.Generate(
            outerSettings,
            out AirCombatCityReport outerReport);

        Assert.That(innerReport.valid, Is.True, innerReport.failureReason);
        Assert.That(outerReport.valid, Is.True, outerReport.failureReason);
        Assert.That(innerReport.buildingRoadOverlapCount, Is.Zero);
        Assert.That(outerReport.buildingRoadOverlapCount, Is.Zero);

        float innerMagneticRadius = innerPlan.volumes
            .Where(volume => volume.kind == AirCombatVolumeKind.RecoveryPocket)
            .Average(volume => new Vector2(
                volume.center.x,
                volume.center.z).magnitude);
        float outerMagneticRadius = outerPlan.volumes
            .Where(volume => volume.kind == AirCombatVolumeKind.RecoveryPocket)
            .Average(volume => new Vector2(
                volume.center.x,
                volume.center.z).magnitude);
        Assert.That(outerMagneticRadius, Is.GreaterThan(innerMagneticRadius));

        Assert.That(
            UrbanEnvironmentalFieldDirector.TryResolvePlannedWindTrapPosition(
                innerPlan,
                innerSettings.ValidatedCopy(),
                out Vector3 innerWind),
            Is.True);
        Assert.That(
            UrbanEnvironmentalFieldDirector.TryResolvePlannedWindTrapPosition(
                outerPlan,
                outerSettings.ValidatedCopy(),
                out Vector3 outerWind),
            Is.True);
        Assert.That(
            new Vector2(outerWind.x, outerWind.z).magnitude,
            Is.GreaterThan(new Vector2(innerWind.x, innerWind.z).magnitude));
    }

    [Test]
    public void EnvironmentalTrapCountsUseDistinctLegalCandidates()
    {
        var settings = new AirCombatCitySettings
        {
            seed = 7319,
            naturalStreetGaleCount = 4,
            magneticCourtyardCount = 6
        };

        AirCombatCityPlan plan = AirCombatCityGenerator.Generate(
            settings,
            out AirCombatCityReport report);

        Assert.That(report.valid, Is.True, report.failureReason);
        Assert.That(report.buildingRoadOverlapCount, Is.Zero);
        Assert.That(
            plan.volumes.Count(volume =>
                volume.kind == AirCombatVolumeKind.RecoveryPocket),
            Is.EqualTo(6));
        Assert.That(
            UrbanEnvironmentalFieldDirector.ResolvePlannedWindTrapCount(
                plan,
                settings.ValidatedCopy(),
                out _),
            Is.EqualTo(4));
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
        for (int index = 0; index < plan.ingresses.Count; index++)
        {
            Assert.That(
                AirCombatCityGenerator
                    .EnemyIngressIntersectsRecoveryDistrict(
                        plan,
                        plan.ingresses[index].position),
                Is.False,
                "敌人入口不能落进恢复庭院的实体围墙预留区。");
        }
    }

    [Test]
    public void FacilityAssaultHighTiersConstructValidPadsAndCentralHeightMixWithinFormalAttempts()
    {
        int[] tiers = { 4, 5 };
        for (int index = 0; index < tiers.Length; index++)
        {
            int tier = tiers[index];
            var settings = new AirCombatCitySettings
            {
                seed = 7319 + tier * 104729,
                mission = AirCombatCityMission.FacilityAssault,
                maximumAttempts = 10,
                combatDifficulty = CombatCityDifficultyProfile.CreateForTier(
                    tier,
                    AirCombatCityMission.FacilityAssault)
            };
            AirCombatCityPlan plan = AirCombatCityGenerator.Generate(
                settings,
                out AirCombatCityReport report);

            Assert.That(report.valid, Is.True, report.failureReason);
            Assert.That(report.attempts, Is.LessThanOrEqualTo(10));
            Assert.That(report.facilityReachable, Is.True);
            Assert.That(report.centralHeightMixValid, Is.True);
            Assert.That(plan.facilityCores.Count, Is.EqualTo(3));
            for (int first = 0; first < plan.facilityCores.Count; first++)
            for (int second = first + 1;
                 second < plan.facilityCores.Count;
                 second++)
            {
                Assert.That(
                    Vector3.Distance(
                        plan.facilityCores[first],
                        plan.facilityCores[second]),
                    Is.GreaterThanOrEqualTo(
                        AirCombatCityGenerator
                            .FacilityPadMinimumSeparation));
            }
        }
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

    [Test]
    public void PostPlanBuildingsKeepTheFormalIngressFormationPadClear()
    {
        var ingresses = new List<AirCombatEnemyIngress>
        {
            new AirCombatEnemyIngress
            {
                position = Vector3.zero
            }
        };
        Vector3 size = new Vector3(20f, 100f, 20f);

        Assert.That(
            AirCombatCityPcgLab.IntersectsEnemyIngressReservation(
                new Vector3(80f, 0f, 0f),
                size,
                ingresses),
            Is.True);
        Assert.That(
            AirCombatCityPcgLab.IntersectsEnemyIngressReservation(
                new Vector3(90f, 0f, 0f),
                size,
                ingresses),
            Is.False);
    }

    [Test]
    public void CombatOpportunityNetworkProvidesNonLinearChoices()
    {
        var settings = new AirCombatCitySettings
        {
            seed = 7319,
            mission = AirCombatCityMission.Clearance,
            combatDifficulty = CombatCityDifficultyProfile.CreateForTier(
                2,
                AirCombatCityMission.Clearance)
        };

        AirCombatCityPlan plan = AirCombatCityGenerator.Generate(
            settings,
            out AirCombatCityReport report);

        Assert.That(plan, Is.Not.Null);
        Assert.That(report.tacticalOpportunityNetworkValid, Is.True);
        Assert.That(report.dominantRouteDetected, Is.False);
        Assert.That(report.tacticalOpportunityCount, Is.GreaterThanOrEqualTo(6));
        Assert.That(report.minimumTacticalChoices, Is.GreaterThanOrEqualTo(2));
        Assert.That(report.recoveryOpportunityCount, Is.EqualTo(2));
        Assert.That(report.exposureShortcutCount, Is.GreaterThanOrEqualTo(1));
        Assert.That(report.kiteLoopOpportunityCount, Is.GreaterThanOrEqualTo(1));
        foreach (TacticalOpportunity opportunity in plan.opportunities.Where(
                     item => item.kind == TacticalOpportunityKind.RecoveryPocket))
        {
            Assert.That(opportunity.safeWindowSeconds, Is.GreaterThanOrEqualTo(10f));
        }
    }

    [Test]
    public void DifficultyProfileChangesPhysicalEnvelopeAndPressure()
    {
        var easy = new AirCombatCitySettings
        {
            seed = 8871,
            mission = AirCombatCityMission.BossEncounter,
            combatDifficulty = CombatCityDifficultyProfile.CreateForTier(
                0,
                AirCombatCityMission.BossEncounter)
        };
        var hard = new AirCombatCitySettings
        {
            seed = easy.seed,
            mission = easy.mission,
            combatDifficulty = CombatCityDifficultyProfile.CreateForTier(
                5,
                AirCombatCityMission.BossEncounter)
        };

        Assert.That(easy.MainCorridorWidth, Is.GreaterThan(hard.MainCorridorWidth));
        Assert.That(easy.RecoveryDiameter, Is.GreaterThan(hard.RecoveryDiameter));

        AirCombatCityGenerator.Generate(easy, out AirCombatCityReport easyReport);
        AirCombatCityGenerator.Generate(hard, out AirCombatCityReport hardReport);

        Assert.That(easyReport.tacticalOpportunityNetworkValid, Is.True);
        Assert.That(hardReport.tacticalOpportunityNetworkValid, Is.True);
        Assert.That(hardReport.ingressCount, Is.GreaterThanOrEqualTo(easyReport.ingressCount));
        Assert.That(
            hardReport.longestExposureSeconds,
            Is.GreaterThan(easyReport.longestExposureSeconds));
        Assert.That(
            easyReport.tacticalOpportunityCount,
            Is.GreaterThanOrEqualTo(hardReport.tacticalOpportunityCount));
    }

    [Test]
    public void CombatRegionsAreBackedByMeasurableCityGeometry()
    {
        var settings = new AirCombatCitySettings
        {
            seed = 7319,
            mission = AirCombatCityMission.BossEncounter,
            combatDifficulty = CombatCityDifficultyProfile.CreateForTier(
                2,
                AirCombatCityMission.BossEncounter)
        };

        AirCombatCityPlan plan = AirCombatCityGenerator.Generate(
            settings,
            out AirCombatCityReport report);

        Assert.That(plan, Is.Not.Null);
        Assert.That(report.valid, Is.True, report.failureReason);
        Assert.That(report.combatRegionsPhysical, Is.True);
        Assert.That(report.exposureShortcutSavingRatio,
            Is.InRange(0.20f, 0.35f));
        Assert.That(report.occlusionBreakCount, Is.GreaterThanOrEqualTo(4));
        Assert.That(report.occlusionBoundaryTowerCount,
            Is.GreaterThanOrEqualTo(8));
        Assert.That(report.combatBoundaryTowerCount,
            Is.GreaterThanOrEqualTo(16));
        Assert.That(report.combatBoundaryAirWallCount, Is.EqualTo(4));
        Assert.That(report.recoveryPocketOutputBlockedCount, Is.EqualTo(2));
        Assert.That(report.kiteLoopObstructionCount, Is.GreaterThanOrEqualTo(1));
        Assert.That(report.destructionAmbushFeatureCount,
            Is.GreaterThanOrEqualTo(2));
        Assert.That(report.physicalAttackPerchCount, Is.Zero);
        Assert.That(report.coveredAttackPerchCount, Is.Zero);
        Assert.That(report.verticalEscapePhysical, Is.True);
        Assert.That(plan.routes.Any(route =>
            route.kind == AirCombatRouteKind.KiteLoop), Is.True);
        Assert.That(plan.routes.Any(route =>
            route.kind == AirCombatRouteKind.VerticalEscape), Is.True);
        Assert.That(plan.routes.Any(route =>
            route.kind == AirCombatRouteKind.EnemyIngress), Is.False);
        Assert.That(plan.opportunities.Any(opportunity =>
                opportunity.kind == TacticalOpportunityKind.AttackPerch),
            Is.False);
        Assert.That(plan.volumes.Any(volume =>
                volume.kind == AirCombatVolumeKind.DominancePerch),
            Is.False);
        float upperCombatAltitude = Mathf.Lerp(
            settings.mediumAltitude,
            settings.highAltitude,
            0.35f);
        Assert.That(plan.routes
                .Where(route =>
                    route.kind == AirCombatRouteKind.LongRange ||
                    route.kind == AirCombatRouteKind.VerticalEscape)
                .SelectMany(route => route.points)
                .Max(point => point.y),
            Is.LessThanOrEqualTo(upperCombatAltitude + 0.01f));
    }

    [Test]
    public void DefaultRuntimeDesignProfileContainsSixPlanetTiers()
    {
        CombatCityPcgDesignProfile profile =
            Resources.Load<CombatCityPcgDesignProfile>(
                AirCombatCityPcgLab.DefaultDesignProfileResourcePath);

        Assert.That(profile, Is.Not.Null);
        profile.EnsureInitialized();
        Assert.That(profile.planetBindings.Count, Is.EqualTo(6));
        Assert.That(profile.clearance.difficultyTiers.Length, Is.EqualTo(6));
        Assert.That(profile.assault.difficultyTiers.Length, Is.EqualTo(6));
        Assert.That(profile.boss.difficultyTiers.Length, Is.EqualTo(6));
        Assert.That(
            profile.Resolve(AirCombatCityMission.Clearance, 0).recoveryGenerosity,
            Is.GreaterThan(
                profile.Resolve(AirCombatCityMission.Clearance, 5)
                    .recoveryGenerosity));
        Assert.That(
            profile.Resolve(AirCombatCityMission.Clearance, 0).roadWidthScale,
            Is.GreaterThan(
                profile.Resolve(AirCombatCityMission.Clearance, 5)
                    .roadWidthScale));
        Assert.That(
            profile.Resolve(AirCombatCityMission.Clearance, 0)
                .blockMergeStrength,
            Is.LessThan(
                profile.Resolve(AirCombatCityMission.Clearance, 5)
                    .blockMergeStrength));
    }

    [Test]
    public void TacticalBlocksCoverTheCityAndMergedSeamsRemoveRealRoads()
    {
        CombatCityDifficultyProfile difficulty =
            CombatCityDifficultyProfile.CreateForTier(
                2,
                AirCombatCityMission.Clearance);
        difficulty.roadWidthScale = 1.1f;
        difficulty.blockMergeStrength = 0.6f;
        var settings = new AirCombatCitySettings
        {
            seed = 7319,
            mission = AirCombatCityMission.Clearance,
            combatDifficulty = difficulty
        };

        AirCombatCityPlan plan = AirCombatCityGenerator.Generate(
            settings,
            out AirCombatCityReport report);

        Assert.That(plan, Is.Not.Null);
        Assert.That(report.valid, Is.True, report.failureReason);
        Assert.That(report.tacticalBlockCount, Is.EqualTo(64));
        Assert.That(report.unassignedTacticalBlockCount, Is.Zero);
        Assert.That(report.tacticalRegionCount, Is.GreaterThanOrEqualTo(6));
        Assert.That(report.tacticalBlockCoverageValid, Is.True);
        Assert.That(report.roadWidthsVaried, Is.True);
        Assert.That(report.maximumRoadWidth - report.minimumRoadWidth,
            Is.GreaterThanOrEqualTo(4f));
        Assert.That(report.mergedBlockGroupCount, Is.GreaterThanOrEqualTo(1));
        Assert.That(report.removedInternalRoadSegments,
            Is.GreaterThanOrEqualTo(8));
        Assert.That(report.occlusionMergedRoadSegments,
            Is.GreaterThanOrEqualTo(1));
        Assert.That(plan.tacticalBlocks.Any(block =>
            block.role == CombatCityBlockRole.CombatBoundary), Is.True);

        bool checkedMergedConnection = false;

        foreach (CombatCityBlockPlan block in plan.tacticalBlocks)
        {
            Assert.That(block.primaryOpportunityId, Is.Not.Empty);
            Assert.That(block.tacticalRegionId, Is.Not.Empty);
            Assert.That(block.mergedGroupId, Is.Not.Empty);

            if (block.mergeEast)
            {
                CombatCityBlockPlan east =
                    CombatDrivenCityPcgPlanner.GetTacticalBlock(
                        plan,
                        block.gridX + 1,
                        block.gridZ);
                Assert.That(east, Is.Not.Null);
                Assert.That(
                    CombatDrivenCityPcgPlanner.TryCrossesMergedBlockBoundary(
                        plan,
                        new Vector2(block.bounds.center.x, block.bounds.center.z),
                        new Vector2(east.bounds.center.x, east.bounds.center.z),
                        out bool crossesMergedEast),
                    Is.True);
                Assert.That(crossesMergedEast, Is.False,
                    "A removed east seam must remain inside one merged block.");
                checkedMergedConnection = true;
                float seamX = block.bounds.max.x;
                float sampleZ = block.bounds.center.z;
                Assert.That(plan.roads.Any(road =>
                        Mathf.Abs(road.start.x - road.end.x) < 0.01f &&
                        Mathf.Abs(road.start.x - seamX) < 0.01f &&
                        sampleZ > Mathf.Min(road.start.z, road.end.z) + 0.1f &&
                        sampleZ < Mathf.Max(road.start.z, road.end.z) - 0.1f),
                    Is.False,
                    "Merged east seam still contains a road: " + block.stableId);
            }

            if (block.mergeNorth)
            {
                CombatCityBlockPlan north =
                    CombatDrivenCityPcgPlanner.GetTacticalBlock(
                        plan,
                        block.gridX,
                        block.gridZ + 1);
                Assert.That(north, Is.Not.Null);
                Assert.That(
                    CombatDrivenCityPcgPlanner.TryCrossesMergedBlockBoundary(
                        plan,
                        new Vector2(block.bounds.center.x, block.bounds.center.z),
                        new Vector2(north.bounds.center.x, north.bounds.center.z),
                        out bool crossesMergedNorth),
                    Is.True);
                Assert.That(crossesMergedNorth, Is.False,
                    "A removed north seam must remain inside one merged block.");
                checkedMergedConnection = true;
                float seamZ = block.bounds.max.z;
                float sampleX = block.bounds.center.x;
                Assert.That(plan.roads.Any(road =>
                        Mathf.Abs(road.start.z - road.end.z) < 0.01f &&
                        Mathf.Abs(road.start.z - seamZ) < 0.01f &&
                        sampleX > Mathf.Min(road.start.x, road.end.x) + 0.1f &&
                        sampleX < Mathf.Max(road.start.x, road.end.x) - 0.1f),
                    Is.False,
                    "Merged north seam still contains a road: " + block.stableId);
            }
        }
        Assert.That(checkedMergedConnection, Is.True);
    }

    [Test]
    public void RoadWidthScaleChangesTheGeneratedRoadEnvelope()
    {
        CombatCityDifficultyProfile narrowDifficulty =
            CombatCityDifficultyProfile.CreateForTier(
                2,
                AirCombatCityMission.Clearance);
        narrowDifficulty.roadWidthScale = 0.72f;
        CombatCityDifficultyProfile wideDifficulty =
            narrowDifficulty.ValidatedCopy();
        wideDifficulty.roadWidthScale = 1.35f;

        var narrowSettings = new AirCombatCitySettings
        {
            seed = 7319,
            mission = AirCombatCityMission.Clearance,
            combatDifficulty = narrowDifficulty
        };
        var wideSettings = new AirCombatCitySettings
        {
            seed = narrowSettings.seed,
            mission = narrowSettings.mission,
            combatDifficulty = wideDifficulty
        };

        AirCombatCityGenerator.Generate(
            narrowSettings,
            out AirCombatCityReport narrowReport);
        AirCombatCityGenerator.Generate(
            wideSettings,
            out AirCombatCityReport wideReport);

        Assert.That(wideReport.maximumRoadWidth,
            Is.GreaterThan(narrowReport.maximumRoadWidth));
        Assert.That(wideReport.minimumRoadWidth,
            Is.GreaterThanOrEqualTo(narrowReport.minimumRoadWidth));
    }
}
