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
            // 逐格难度生成会把一部分原来的通用大地块换成带独立
            // stableId 的战术高度锚点。验收真实实体城的总体量，再
            // 要求大复合地块仍占主体，避免用旧命名数量误判缩水。
            Assert.That(destructibleBuildings.Length,
                Is.GreaterThanOrEqualTo(160));
            Assert.That(largeParcels, Is.GreaterThanOrEqualTo(80));
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
    public void ReportedRuntimeClearanceSeedKeepsSingleCityAndBuildsBothCollapseAmbushBridges()
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

            Assert.That(lab.Report.resolvedSeed, Is.EqualTo(898490944));
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
            Assert.That(destructionBridges, Has.Length.GreaterThanOrEqualTo(2),
                "两组坍塌伏击可以由多个可破坏桥段共同组成；权威组数由报告字段验证。");
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

    [Test]
    public void SurvivalCostKeepsSafeCellsCheapAndChoosesMinimumExposurePath()
    {
        float[] threat = { 0.80f, 0.70f, 0.10f, 0.20f };
        bool[] flyable = { true, true, true, true };
        var edges = new List<AirCombatGridSurvivalSolver.Edge>
        {
            new AirCombatGridSurvivalSolver.Edge
                { from = 0, to = 1, travelSeconds = 1f },
            new AirCombatGridSurvivalSolver.Edge
                { from = 1, to = 2, travelSeconds = 1f },
            new AirCombatGridSurvivalSolver.Edge
                { from = 0, to = 3, travelSeconds = 10f }
        };

        AirCombatGridSurvivalSolver.Result[] result =
            AirCombatGridSurvivalSolver.Solve(threat, flyable, edges);

        Assert.That(result[0].targetIndex, Is.EqualTo(2));
        Assert.That(result[0].pathCellCount, Is.EqualTo(2));
        Assert.That(result[0].difficulty, Is.LessThan(threat[0]));
        Assert.That(result[2].difficulty, Is.EqualTo(threat[2]).Within(0.0001f),
            "安全格不应因为难以脱离而额外变难。");
        Assert.That(result[2].pathCellCount, Is.Zero);
    }

    [Test]
    public void CityGenerationUsesOneSeedAndBuildsBlocksFromCenterOutward()
    {
        var settings = new AirCombatCitySettings
        {
            seed = 7319,
            mission = AirCombatCityMission.Clearance,
            maximumAttempts = 4,
            combatDifficulty = CombatCityDifficultyProfile.CreateForTier(
                2,
                AirCombatCityMission.Clearance)
        };

        AirCombatCityPlan plan = AirCombatCityGenerator.Generate(
            settings,
            out AirCombatCityReport report);

        Assert.That(plan.requestedSeed, Is.EqualTo(settings.seed));
        Assert.That(plan.resolvedSeed, Is.EqualTo(settings.seed));
        Assert.That(report.attempts, Is.EqualTo(1),
            "局部修正不能再被记成多座候选城市。");
        Assert.That(plan.tacticalBlocks.Count, Is.EqualTo(64));
        CombatCityBlockPlan[] ordered = plan.tacticalBlocks
            .OrderBy(block => block.generationOrder)
            .ToArray();
        float previousRing = -1f;
        var generatedCoordinates = new HashSet<Vector2Int>();
        for (int index = 0; index < ordered.Length; index++)
        {
            CombatCityBlockPlan block = ordered[index];
            float ring = Mathf.Max(
                Mathf.Abs(block.gridX - 3.5f),
                Mathf.Abs(block.gridZ - 3.5f));
            Assert.That(ring + 0.0001f, Is.GreaterThanOrEqualTo(previousRing));
            previousRing = ring;
            Assert.That(block.generatedForDifficulty, Is.True);
            bool perimeter = block.gridX == 0 || block.gridZ == 0 ||
                             block.gridX == 7 || block.gridZ == 7;
            Assert.That(block.excludedFromDifficulty, Is.EqualTo(perimeter));
            if (!perimeter)
            {
                bool firstGreedyBlock = generatedCoordinates.Count == 0;
                bool touchesCommittedFrontier =
                    generatedCoordinates.Contains(new Vector2Int(
                        block.gridX - 1, block.gridZ)) ||
                    generatedCoordinates.Contains(new Vector2Int(
                        block.gridX + 1, block.gridZ)) ||
                    generatedCoordinates.Contains(new Vector2Int(
                        block.gridX, block.gridZ - 1)) ||
                    generatedCoordinates.Contains(new Vector2Int(
                        block.gridX, block.gridZ + 1));
                Assert.That(firstGreedyBlock || touchesCommittedFrontier,
                    Is.True,
                    "每个新格必须从已提交城市的四邻接前沿扩张：" +
                    block.stableId);
                Assert.That(block.localCorrectionCount,
                    Is.InRange(3, 5),
                    "字段现在记录该格实际比较的贪心候选数。" );
                Assert.That(float.IsNaN(block.greedyCandidateScore) ||
                            float.IsInfinity(block.greedyCandidateScore),
                    Is.False);
                generatedCoordinates.Add(new Vector2Int(
                    block.gridX, block.gridZ));
            }
        }
    }

    [Test]
    public void GreedyGenerationFeedsGlobalBudgetIntoLaterNeighborBlocks()
    {
        var settings = new AirCombatCitySettings
        {
            seed = 7319,
            mission = AirCombatCityMission.Clearance,
            maximumAttempts = 10,
            combatDifficulty = CombatCityDifficultyProfile.CreateForTier(
                5,
                AirCombatCityMission.Clearance)
        };

        AirCombatCityPlan plan = AirCombatCityGenerator.Generate(
            settings,
            out AirCombatCityReport report);
        CombatCityBlockPlan[] greedy = plan.tacticalBlocks
            .Where(block => !block.excludedFromDifficulty)
            .OrderBy(block => block.generationOrder)
            .ToArray();

        Assert.That(greedy, Has.Length.EqualTo(36));
        Assert.That(greedy.Any(block => Mathf.Abs(
                block.greedyRequestedDifficulty -
                block.targetDifficulty) > 0.01f),
            Is.True,
            "如果每格仍只读取开局固定目标，说明全局剩余预算没有接通。" );
        Assert.That(greedy.Any(block => block.greedyCandidateOpenness >= 0.50f),
            Is.True,
            "最高档必须允许贪心器选择显著开放候选。" );
        Assert.That(report.plannedAverageDifficulty,
            Is.GreaterThan(0.50f));
    }

    [Test]
    public void GreedyGenerationIsDeterministicIncludingChosenCandidates()
    {
        var settings = new AirCombatCitySettings
        {
            seed = 38995,
            mission = AirCombatCityMission.Clearance,
            maximumAttempts = 10,
            combatDifficulty = CombatCityDifficultyProfile.CreateForTier(
                4,
                AirCombatCityMission.Clearance)
        };

        AirCombatCityPlan first = AirCombatCityGenerator.Generate(
            settings,
            out AirCombatCityReport firstReport);
        AirCombatCityPlan second = AirCombatCityGenerator.Generate(
            settings,
            out AirCombatCityReport secondReport);

        Assert.That(secondReport.checksum, Is.EqualTo(firstReport.checksum));
        for (int index = 0; index < first.tacticalBlocks.Count; index++)
        {
            Assert.That(second.tacticalBlocks[index].generationOrder,
                Is.EqualTo(first.tacticalBlocks[index].generationOrder));
            Assert.That(second.tacticalBlocks[index]
                    .greedyCandidateOpenness,
                Is.EqualTo(first.tacticalBlocks[index]
                    .greedyCandidateOpenness).Within(0.0001f));
            Assert.That(second.tacticalBlocks[index]
                    .greedyRequestedDifficulty,
                Is.EqualTo(first.tacticalBlocks[index]
                    .greedyRequestedDifficulty).Within(0.0001f));
        }
    }

    [Test]
    public void SameSeedProducesHigherSpatialDifficultyAtHighestTier()
    {
        var low = new AirCombatCitySettings
        {
            seed = 7319,
            mission = AirCombatCityMission.Clearance,
            maximumAttempts = 3,
            combatDifficulty = CombatCityDifficultyProfile.CreateForTier(
                0,
                AirCombatCityMission.Clearance)
        };
        var high = new AirCombatCitySettings
        {
            seed = low.seed,
            mission = low.mission,
            maximumAttempts = low.maximumAttempts,
            combatDifficulty = CombatCityDifficultyProfile.CreateForTier(
                5,
                AirCombatCityMission.Clearance)
        };

        AirCombatCityGenerator.Generate(low, out AirCombatCityReport lowReport);
        AirCombatCityGenerator.Generate(high,
            out AirCombatCityReport highReport);

        Assert.That(lowReport.cityDifficultyTargetMet, Is.True);
        Assert.That(highReport.cityDifficultyTargetMet, Is.True);
        Assert.That(highReport.plannedAverageDifficulty,
            Is.GreaterThan(lowReport.plannedAverageDifficulty + 0.08f));
        Assert.That(highReport.plannedHighRiskCellRatio,
            Is.GreaterThan(lowReport.plannedHighRiskCellRatio));
    }

    [Test]
    public void GreedyShowcaseTraceReplaysCenterOutAndRecalculatesOldBlocks()
    {
        var settings = new AirCombatCitySettings
        {
            seed = 7319,
            mission = AirCombatCityMission.Clearance,
            maximumAttempts = 10,
            combatDifficulty = CombatCityDifficultyProfile.CreateForTier(
                4,
                AirCombatCityMission.Clearance)
        };
        AirCombatCityPlan plan = AirCombatCityGenerator.Generate(
            settings,
            out AirCombatCityReport report);
        int buildingCount = plan.buildings.Count;
        bool[] generatedBefore = plan.tacticalBlocks
            .Select(block => block.generatedForDifficulty)
            .ToArray();

        CityGreedyGenerationShowcase.Trace trace =
            CityGreedyGenerationShowcase.BuildTrace(settings, plan);

        Assert.That(trace, Is.Not.Null);
        Assert.That(trace.steps, Has.Count.EqualTo(36));
        float nearestCenterDistance = plan.tacticalBlocks
            .Where(block => !block.excludedFromDifficulty)
            .Min(block => new Vector2(
                block.bounds.center.x,
                block.bounds.center.z).sqrMagnitude);
        float firstCenterDistance = new Vector2(
            trace.steps[0].block.bounds.center.x,
            trace.steps[0].block.bounds.center.z).sqrMagnitude;
        Assert.That(firstCenterDistance,
            Is.EqualTo(nearestCenterDistance).Within(0.01f),
            "逐格回放必须从离城市中心最近的内部区块开始。" );
        var committed = new HashSet<Vector2Int>();
        bool sawOldCellRecalculated = false;
        for (int index = 0; index < trace.steps.Count; index++)
        {
            CityGreedyGenerationShowcase.Step step = trace.steps[index];
            Vector2Int coordinate = new Vector2Int(
                step.block.gridX, step.block.gridZ);
            if (index > 0)
            {
                bool adjacent = committed.Any(previous =>
                    Mathf.Abs(previous.x - coordinate.x) +
                    Mathf.Abs(previous.y - coordinate.y) == 1);
                Assert.That(adjacent, Is.True,
                    "演示步骤必须保持正式算法的四邻接扩张顺序。" );
            }
            committed.Add(coordinate);
            Assert.That(step.candidatesTested, Is.InRange(3, 5));
            Assert.That(float.IsNaN(step.cityDifficultyAfter) ||
                        float.IsInfinity(step.cityDifficultyAfter),
                Is.False);
            if (step.recalculatedCells.Any(change =>
                    Mathf.Abs(change.Delta) >= 0.005f))
            {
                sawOldCellRecalculated = true;
            }
        }
        Assert.That(sawOldCellRecalculated, Is.True,
            "加入新格后必须展示至少一次旧格危险度的重新计算。" );
        Assert.That(plan.buildings, Has.Count.EqualTo(buildingCount),
            "分析回放不得改写正式城市规划。" );
        for (int index = 0; index < plan.tacticalBlocks.Count; index++)
        {
            Assert.That(plan.tacticalBlocks[index].generatedForDifficulty,
                Is.EqualTo(generatedBefore[index]),
                "分析结束后必须恢复每个区块的正式生成状态。" );
        }
        Assert.That(report.valid, Is.True, report.failureReason);
    }

    [TestCase(0.10f)]
    [TestCase(0.50f)]
    [TestCase(0.90f)]
    public void ExplicitTargetDifficultyRoundTripsThroughFormalProfile(
        float requested)
    {
        CombatCityDifficultyProfile profile =
            CombatCityDifficultyProfile.CreateForTargetDifficulty(
                requested,
                AirCombatCityMission.Clearance);

        AirCombatCityDifficultyTarget target =
            AirCombatCityDifficultyPcg.ResolveTarget(profile);

        Assert.That(profile.useExplicitAverageDifficulty, Is.True);
        Assert.That(target.averageDifficulty,
            Is.EqualTo(requested).Within(0.0001f));
        Assert.That(profile.navigationChallenge, Is.InRange(0f, 1f));
        Assert.That(profile.exposurePressure, Is.InRange(0f, 1f));
        Assert.That(profile.recoveryGenerosity, Is.InRange(0f, 1f));
    }

    [Test]
    public void ExplicitNinetyPercentOpenCityCanReachHighThreatDomain()
    {
        var settings = new AirCombatCitySettings
        {
            seed = 7319,
            mission = AirCombatCityMission.Clearance,
            maximumAttempts = 5,
            combatDifficulty = CombatCityDifficultyProfile
                .CreateForTargetDifficulty(0.90f,
                    AirCombatCityMission.Clearance)
        };

        AirCombatCityPlan plan = AirCombatCityGenerator.Generate(
            settings,
            out AirCombatCityReport report);

        Assert.That(report.plannedAverageDifficulty,
            Is.GreaterThanOrEqualTo(0.80f),
            "90%目标必须能由区块主体接近，不能等到风场阶段伪造危险度。");
        Assert.That(report.plannedHighRiskCellRatio,
            Is.GreaterThanOrEqualTo(0.80f));
        Assert.That(report.cityDifficultyTargetMet,
            Is.True, report.failureReason);
        Assert.That(plan.tacticalBlocks.Count(block =>
                !block.excludedFromDifficulty &&
                block.generatedForDifficulty),
            Is.EqualTo(36));
    }

    [Test]
    public void CableOnlyAddsDelayWhenPlayerCorridorTouchesCurve()
    {
        var cable = new AirCombatRuntimeConnectionGeometry
        {
            localStart = new Vector3(50f, 60f, -20f),
            localEnd = new Vector3(50f, 60f, 20f),
            localPoints = new[]
            {
                new Vector3(50f, 60f, -20f),
                new Vector3(50f, 60f, 20f)
            },
            physicalRadius = 1.05f,
            playerSlowdown = 0.28f
        };

        float hit = AirCombatCityDifficultyPcg.ApplyCableTraversalDelay(
            new Vector3(0f, 60f, 0f),
            new Vector3(100f, 60f, 0f),
            9f,
            2f,
            new[] { cable });
        float miss = AirCombatCityDifficultyPcg.ApplyCableTraversalDelay(
            new Vector3(0f, 120f, 0f),
            new Vector3(100f, 120f, 0f),
            9f,
            2f,
            new[] { cable });

        Assert.That(hit, Is.GreaterThan(2f));
        Assert.That(miss, Is.EqualTo(2f).Within(0.0001f));
    }

    [Test]
    public void WindEvaluatesBothDirectionsWithoutAssumingPlayerRoute()
    {
        var wind = new AirCombatRuntimeWindGeometry
        {
            localCenter = new Vector3(50f, 60f, 0f),
            localDirection = Vector3.right,
            size = new Vector3(60f, 120f, 140f),
            strength = 1.5f
        };
        float tailwind = AirCombatCityDifficultyPcg
            .ApplyWindTraversalModifier(
                new Vector3(0f, 60f, 0f),
                new Vector3(100f, 60f, 0f),
                2f,
                new[] { wind });
        float headwind = AirCombatCityDifficultyPcg
            .ApplyWindTraversalModifier(
                new Vector3(100f, 60f, 0f),
                new Vector3(0f, 60f, 0f),
                2f,
                new[] { wind });
        float crosswind = AirCombatCityDifficultyPcg
            .ApplyWindTraversalModifier(
                new Vector3(50f, 60f, -50f),
                new Vector3(50f, 60f, 50f),
                2f,
                new[] { wind });

        Assert.That(tailwind, Is.LessThan(2f));
        Assert.That(headwind, Is.GreaterThan(2f));
        Assert.That(crosswind, Is.GreaterThan(2f));
        Assert.That(headwind, Is.GreaterThan(crosswind));
    }
}
