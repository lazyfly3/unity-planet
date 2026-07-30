using System;
using UnityEngine;

namespace UnityPlanet.ModularAssembly
{
    public static class NeoXCombatFeedbackRuntime
    {
        const string CatalogResourcePath =
            "CombatFeedback/NeoX/NeoXCombatEffectCatalog";

        static NeoXCombatEffectCatalog catalog;
        static bool catalogLoadAttempted;

        public static bool TrySpawnImpact(
            Vector3 position,
            Vector3 normal,
            string sourceEffect,
            float radius)
        {
            string effectId = ResolveImpactEffect(sourceEffect, radius);
            float scale = radius > 0.1f
                ? Mathf.Clamp(0.9f + radius * 0.12f, 1f, 2.4f)
                : 1f;
            return TrySpawn(
                effectId,
                position,
                SafeDirection(normal, Vector3.up),
                scale);
        }

        public static bool TrySpawnModuleBreak(
            Vector3 position,
            Vector3 impulseDirection,
            float visualSize)
        {
            return TrySpawn(
                "module_break",
                position,
                SafeDirection(impulseDirection, Vector3.up),
                Mathf.Clamp(visualSize * 0.35f, 0.8f, 2.5f));
        }

        public static bool TrySpawnDetached(
            Vector3 position,
            Vector3 impulseDirection,
            float visualSize)
        {
            return TrySpawn(
                "detached_smoke",
                position,
                SafeDirection(impulseDirection, Vector3.up),
                Mathf.Clamp(visualSize * 0.2f, 0.8f, 2f));
        }

        public static bool TrySpawn(
            string sourceId,
            Vector3 position,
            Vector3 direction,
            float uniformScale = 1f)
        {
            NeoXCombatEffectCatalog sourceCatalog = GetCatalog();
            if (sourceCatalog == null ||
                !sourceCatalog.TryGet(sourceId, out NeoXCombatEffectCatalogEntry entry) ||
                entry == null ||
                entry.prefab == null)
                return false;

            Transform parent = CombatTransientRoot.GetOrCreate();
            Quaternion rotation = Quaternion.LookRotation(
                SafeDirection(direction, Vector3.forward),
                Vector3.up);
            GameObject instance = UnityEngine.Object.Instantiate(
                entry.prefab,
                position,
                rotation,
                parent);
            instance.name = "NeoXCombat_" + sourceId;
            instance.transform.localScale *= Mathf.Max(0.05f, uniformScale);

            NeoXCombatEffectMarker marker =
                instance.GetComponentInChildren<NeoXCombatEffectMarker>(true);
            bool worldSimulation = marker == null || marker.usesWorldSimulation;
            foreach (ParticleSystem particle in
                     instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (particle == null)
                    continue;
                if (worldSimulation)
                {
                    ParticleSystem.MainModule main = particle.main;
                    main.simulationSpace = ParticleSystemSimulationSpace.World;
                }
                particle.Stop(
                    true,
                    ParticleSystemStopBehavior.StopEmittingAndClear);
                particle.Play(true);
            }

            if (entry.audioClip != null)
            {
                AudioSource audio = instance.GetComponent<AudioSource>() ??
                                    instance.AddComponent<AudioSource>();
                audio.clip = entry.audioClip;
                audio.spatialBlend = 1f;
                audio.playOnAwake = false;
                audio.Play();
            }

            float lifetime = marker == null
                ? 1.2f
                : Mathf.Max(0.05f, marker.expectedLifetime);
            instance.AddComponent<NeoXCombatEffectLifetime>()
                .Initialize(lifetime);
            return true;
        }

        static NeoXCombatEffectCatalog GetCatalog()
        {
            if (catalog != null)
                return catalog;
            if (catalogLoadAttempted)
                return null;
            catalogLoadAttempted = true;
            catalog = Resources.Load<NeoXCombatEffectCatalog>(
                CatalogResourcePath);
            return catalog;
        }

        static string ResolveImpactEffect(
            string sourceEffect,
            float radius)
        {
            string value = (sourceEffect ?? string.Empty).ToLowerInvariant();
            if (value.Contains("laser") || value.Contains("beam"))
                return "laser_impact";
            if (value.Contains("energy") ||
                value.Contains("plasma") ||
                value.Contains("psy"))
                return "energy_impact";
            if (value.Contains("sniper"))
                return "sniper_hit";
            if (value.Contains("gatlin") || value.Contains("machinegun"))
                return "gatlin_hit";
            if (value.Contains("missile") ||
                value.Contains("rocket") ||
                value.Contains("explode") ||
                radius > 0.1f)
                return "missile_explosion";
            return "metal_hit";
        }

        static Vector3 SafeDirection(Vector3 value, Vector3 fallback)
        {
            return value.sqrMagnitude > 0.0001f
                ? value.normalized
                : fallback.normalized;
        }
    }

    public sealed class NeoXCombatEffectLifetime : MonoBehaviour
    {
        float destroyAt;

        public void Initialize(float lifetime)
        {
            destroyAt = Time.time + Mathf.Max(0.05f, lifetime);
        }

        void Update()
        {
            if (Time.time >= destroyAt)
                Destroy(gameObject);
        }
    }
}
