using System;
using UnityEngine;

namespace UnityPlanet.CityPcg
{
    public enum DarkCity2DistrictKind
    {
        Commercial,
        Industrial,
        Transit,
        Mixed,
        Service
    }

    public enum DarkCity2AssetCategory
    {
        LowBuilding,
        MediumBuilding,
        HighBuilding,
        FacilityBuilding,
        BackgroundBuilding,
        RoadSurface,
        Sidewalk,
        StraightSkybridge,
        CurvedSkybridge,
        SupportedOverpass,
        BridgeHead,
        RooftopDecoration,
        FacadeDecoration,
        StreetDecoration
    }

    public enum DarkCity2StretchAxis
    {
        None,
        X,
        Y,
        Z
    }

    public enum DarkCity2PlacementRole
    {
        None,
        LowBlock,
        MidBlock,
        Tower,
        Landmark,
        Facility,
        DistantSkyline,
        BridgeStraight,
        BridgeCorner,
        BridgeSupported,
        BridgeHeadLeft,
        BridgeHeadRight,
        RoofAntenna,
        RoofMechanical,
        RoofEnergy,
        RoofGenerator,
        RoofPipeFrame,
        FacadeBillboard,
        FacadeNeon,
        FacadeFireEscape,
        FacadeBalcony,
        FacadePipe,
        FacadeCable,
        FacadeMechanical,
        FacadeShop,
        StreetLight,
        StreetUtility,
        StreetBarrier,
        StreetTransit,
        StreetWarning,
        StreetIndustrial,
        IndustrialWarehouse,
        IndustrialSilo,
        TransitGarage
    }

    [Serializable]
    public struct DarkCity2ConnectionSocket
    {
        public string name;
        public Vector3 localPosition;
        public Vector3 localForward;
        public Vector2 openingSize;
    }

    [CreateAssetMenu(
        fileName = "DarkCity2UrbanCatalog",
        menuName = "Unity Planet/City PCG/Dark City 2 Catalog")]
    public sealed class DarkCity2UrbanCatalog : ScriptableObject
    {
        [Header("Building catalog used by the existing combat-city planner")]
        public NewGenUrbanBuildingCatalog buildings;

        [Header("One-mesh distant skyline")]
        public GameObject[] backgroundBuildings = Array.Empty<GameObject>();

        [Header("Canonical +Z span connection pieces")]
        public GameObject[] straightSkybridges = Array.Empty<GameObject>();
        public GameObject[] curvedSkybridges = Array.Empty<GameObject>();
        public GameObject[] supportedOverpasses = Array.Empty<GameObject>();
        public GameObject[] bridgeHeads = Array.Empty<GameObject>();

        [Header("District-defining buildings")]
        public GameObject[] districtBuildings = Array.Empty<GameObject>();

        [Header("Semantic decoration groups")]
        public GameObject[] rooftopDecorations = Array.Empty<GameObject>();
        public GameObject[] facadeDecorations = Array.Empty<GameObject>();
        public GameObject[] streetDecorations = Array.Empty<GameObject>();

        [Header("Road surfaces: a single generated surface owns each height")]
        public Material asphalt;
        public Material sidewalk;
        public Material cityPaving;
        public Material roadLines;
        public Material bridgeSleeve;
        [Header("Aerial building-to-building cable materials")]
        public Material cableRed;
        public Material cableDark;
        public Material cableBlue;
        [Min(2f)] public float roadTextureMeters = 8f;
        [Min(0.001f)] public float roadSurfaceY = 0.018f;
        [Min(0.001f)] public float sidewalkSurfaceY = 0.075f;
        [Min(0.001f)] public float markingSurfaceY = 0.035f;

        public GameObject ResolveStraightBridge(int stableVariant)
        {
            return Resolve(straightSkybridges, stableVariant);
        }

        public GameObject ResolveBridgeHead(int stableVariant)
        {
            return Resolve(bridgeHeads, stableVariant);
        }

        public GameObject ResolveBackground(int stableVariant)
        {
            return Resolve(backgroundBuildings, stableVariant);
        }

        public GameObject ResolveByRole(
            GameObject[] source,
            DarkCity2PlacementRole role,
            int stableVariant)
        {
            if (source == null || source.Length == 0)
                return null;
            int start = stableVariant == int.MinValue
                ? 0
                : Mathf.Abs(stableVariant % source.Length);
            for (int offset = 0; offset < source.Length; offset++)
            {
                GameObject candidate = source[(start + offset) % source.Length];
                if (candidate == null)
                    continue;
                DarkCity2AssetDescriptor descriptor =
                    candidate.GetComponent<DarkCity2AssetDescriptor>();
                if (descriptor != null && descriptor.PlacementRole == role)
                    return candidate;
            }
            return null;
        }

        static GameObject Resolve(GameObject[] source, int variant)
        {
            if (source == null || source.Length == 0)
                return null;
            int value = variant == int.MinValue ? 0 : variant;
            int index = value % source.Length;
            if (index < 0)
                index += source.Length;
            return source[index];
        }
    }
}
