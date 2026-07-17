using System;
using UnityEngine;

[Serializable]
public sealed class PlanetRiverSettings
{
    [SerializeField, HideInInspector] int physicsVersion = 1;
    public bool enabled;
    [Range(0, 12)] public int riverCount;
    [Min(8)] public int nodesPerRiver = 48;
    [Min(0.1f)] public float minWidth = 2f;
    [Min(0.1f)] public float maxWidth = 4f;
    [Min(0.1f)] public float minDepth = 0.8f;
    [Min(0.1f)] public float maxDepth = 1.5f;
    [Min(0f)] public float flowSpeed = 1.5f;
    [Min(1f)] public float lakeRadius = 6f;
    [Min(0.1f)] public float lakeDepth = 2f;
    [Min(1)] public int localRerouteRadius = 16;
    public int seedOffset = 44021;
    public Color shallowColor = new Color(0.08f, 0.72f, 0.68f, 0.58f);
    public Color deepColor = new Color(0.01f, 0.18f, 0.3f, 0.82f);
    [Range(0f, 1f)] public float foamStrength = 0.65f;

    [Header("Shallow Water Physics")]
    [Min(0.02f)] public float simulationStep = 0.05f;
    [Min(0f)] public float sourceFlowRate = 1.2f;
    [Range(0.005f, 0.15f)] public float manningRoughness = 0.035f;
    [Min(0.01f)] public float minimumWaterDepth = 0.03f;
    [Min(0f)] public float evaporationRate;
    [Range(1, 12)] public int maxSubstepsPerFixedUpdate = 4;

    public PlanetRiverSettings Clone() => (PlanetRiverSettings)MemberwiseClone();

    public void ClampValues()
    {
if (physicsVersion < 1)
        {
            simulationStep = 0.05f;
            sourceFlowRate = Mathf.Max(0.1f,
                (minWidth + maxWidth) * 0.5f
                * (minDepth + maxDepth) * 0.5f
                * flowSpeed * 0.7f);
            manningRoughness = 0.035f;
            minimumWaterDepth = 0.03f;
            maxSubstepsPerFixedUpdate = 4;
            physicsVersion = 1;
        }
        riverCount = Mathf.Clamp(riverCount, 0, 12);
        nodesPerRiver = Mathf.Clamp(nodesPerRiver, 8, 128);
        minWidth = Mathf.Max(0.1f, minWidth);
        maxWidth = Mathf.Max(minWidth, maxWidth);
        minDepth = Mathf.Max(0.1f, minDepth);
        maxDepth = Mathf.Max(minDepth, maxDepth);
        flowSpeed = Mathf.Max(0f, flowSpeed);
        lakeRadius = Mathf.Max(1f, lakeRadius);
        lakeDepth = Mathf.Max(0.1f, lakeDepth);
        localRerouteRadius = Mathf.Max(1, localRerouteRadius);
        shallowColor.a = Mathf.Clamp01(shallowColor.a);
        deepColor.a = Mathf.Clamp01(deepColor.a);
        foamStrength = Mathf.Clamp01(foamStrength);
        simulationStep = Mathf.Max(0.02f, simulationStep);
        sourceFlowRate = Mathf.Max(0f, sourceFlowRate);
        manningRoughness = Mathf.Clamp(manningRoughness, 0.005f, 0.15f);
        minimumWaterDepth = Mathf.Max(0.01f, minimumWaterDepth);
        evaporationRate = Mathf.Max(0f, evaporationRate);
        maxSubstepsPerFixedUpdate = Mathf.Clamp(maxSubstepsPerFixedUpdate, 1, 12);
    
}

    public int CalculateHash()
    {
unchecked
        {
            int hash = enabled ? 486187739 : 17;
            hash = hash * 31 + physicsVersion;
            hash = hash * 31 + riverCount;
            hash = hash * 31 + nodesPerRiver;
            hash = hash * 31 + minWidth.GetHashCode();
            hash = hash * 31 + maxWidth.GetHashCode();
            hash = hash * 31 + minDepth.GetHashCode();
            hash = hash * 31 + maxDepth.GetHashCode();
            hash = hash * 31 + flowSpeed.GetHashCode();
            hash = hash * 31 + lakeRadius.GetHashCode();
            hash = hash * 31 + lakeDepth.GetHashCode();
            hash = hash * 31 + localRerouteRadius;
            hash = hash * 31 + seedOffset;
            hash = hash * 31 + simulationStep.GetHashCode();
            hash = hash * 31 + sourceFlowRate.GetHashCode();
            hash = hash * 31 + manningRoughness.GetHashCode();
            hash = hash * 31 + minimumWaterDepth.GetHashCode();
            hash = hash * 31 + evaporationRate.GetHashCode();
            return hash;
        }
    
}
}

[Serializable]
public sealed class GalaxyRiverSaveData
{
    public int configurationHash;
    public GalaxyRiverPathSaveEntry[] rivers;
}

[Serializable]
public sealed class GalaxyRiverPathSaveEntry
{
    public GalaxyRiverNodeSaveEntry[] nodes;
    public float lakeRadius;
    public float lakeDepth;
    public float[] bedRadii;
    public float[] waterDepths;
    public float[] discharges;
    public float lakeWaterDepth;
}

[Serializable]
public struct GalaxyRiverNodeSaveEntry
{
    public Vector3 direction;
    public float waterRadius;
    public float width;
    public float depth;
    public float flowSpeed;
}

public struct WaterSample
{
    public Vector3 surfacePoint;
    public Vector3 surfaceNormal;
    public Vector3 flowVelocity;
    public float depth;
    public float signedDistance;

    public float Submersion => Mathf.Clamp01(-signedDistance / Mathf.Max(0.1f, depth));
}
