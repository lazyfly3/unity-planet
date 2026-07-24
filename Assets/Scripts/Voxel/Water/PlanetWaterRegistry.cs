using System.Collections.Generic;
using UnityEngine;

public interface IPlanetWaterSampler
{
    bool TrySample(Vector3 worldPosition, out WaterSample sample);
}

public static class PlanetWaterRegistry
{
    static readonly List<IPlanetWaterSampler> Samplers =
        new List<IPlanetWaterSampler>();

    public static int ActiveCount
    {
        get
        {
            PruneDestroyed();
            return Samplers.Count;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void ResetForSceneLoad()
    {
        Samplers.Clear();
    }

    public static void Register(IPlanetWaterSampler sampler)
    {
        if (sampler == null || Samplers.Contains(sampler))
            return;
        Samplers.Add(sampler);
    }

    public static void Unregister(IPlanetWaterSampler sampler)
    {
        if (sampler != null)
            Samplers.Remove(sampler);
    }

    public static bool TrySampleAny(
        Vector3 worldPosition,
        out WaterSample sample)
    {
        PruneDestroyed();
        float bestDistance = float.PositiveInfinity;
        sample = default;
        bool found = false;
        for (int i = 0; i < Samplers.Count; i++)
        {
            IPlanetWaterSampler sampler = Samplers[i];
            if (!sampler.TrySample(worldPosition, out WaterSample candidate))
                continue;
            float distance = Mathf.Abs(candidate.signedDistance);
            if (distance >= bestDistance)
                continue;
            bestDistance = distance;
            sample = candidate;
            found = true;
        }
        return found;
    }

    static void PruneDestroyed()
    {
        for (int i = Samplers.Count - 1; i >= 0; i--)
        {
            IPlanetWaterSampler sampler = Samplers[i];
            if (sampler == null
                || (sampler is Object unityObject && unityObject == null))
            {
                Samplers.RemoveAt(i);
            }
        }
    }
}
