using UnityEngine;

namespace UnityPlanet.ModularAssembly
{
    public enum WeaponEffectRole
    {
        Muzzle,
        Projectile,
        Trail,
        Tracer,
        Impact,
        Explosion
    }

    public static class CombatTransientRoot
    {
        const string RootName = "CombatTransientRoot";
        static Transform cachedRoot;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            cachedRoot = null;
        }

        public static Transform GetOrCreate()
        {
            if (cachedRoot != null)
                return cachedRoot;

            GameObject rootObject = GameObject.Find(RootName);
            if (rootObject == null)
                rootObject = new GameObject(RootName);

            rootObject.transform.rotation = Quaternion.identity;
            rootObject.transform.localScale = Vector3.one;
            if (rootObject.GetComponent<PlanetFloatingOriginParticipant>() == null)
                rootObject.AddComponent<PlanetFloatingOriginParticipant>();
            if (rootObject.GetComponent<CombatTransientRootDriver>() == null)
                rootObject.AddComponent<CombatTransientRootDriver>();

            cachedRoot = rootObject.transform;
            return cachedRoot;
        }

        public static void ClearVisuals()
        {
            Transform root = cachedRoot;
            if (root == null)
            {
                GameObject rootObject = GameObject.Find(RootName);
                root = rootObject == null
                    ? null
                    : rootObject.transform;
            }
            if (root == null)
                return;

            CombatWeaponEffectPool weaponEffects =
                root.GetComponent<CombatWeaponEffectPool>();
            if (weaponEffects != null)
                weaponEffects.Clear();
            foreach (ModuleDestructionEffectPool destructionEffects in
                     root.GetComponentsInChildren<
                         ModuleDestructionEffectPool>(true))
            {
                if (destructionEffects != null)
                    destructionEffects.Clear();
            }
            foreach (WeaponProjectile projectile in
                     root.GetComponentsInChildren<WeaponProjectile>(true))
            {
                if (projectile == null)
                    continue;
                projectile.ResetForPool();
                projectile.gameObject.SetActive(false);
            }
            foreach (ParticleSystem particle in
                     root.GetComponentsInChildren<ParticleSystem>(true))
            {
                particle.Stop(
                    true,
                    ParticleSystemStopBehavior.StopEmittingAndClear);
                particle.Clear(true);
            }
            foreach (TrailRenderer trail in
                     root.GetComponentsInChildren<TrailRenderer>(true))
                trail.Clear();
            foreach (LineRenderer line in
                     root.GetComponentsInChildren<LineRenderer>(true))
                line.enabled = false;
            foreach (Light light in
                     root.GetComponentsInChildren<Light>(true))
                light.enabled = false;
            foreach (Transform child in root)
            {
                if (child.name == "WeaponTracer" ||
                    child.name == "HeavyLaser_HovlRay")
                    child.gameObject.SetActive(false);
            }
        }
    }

    [DefaultExecutionOrder(10000)]
    sealed class CombatTransientRootDriver : MonoBehaviour
    {
        Vector3 previousPosition;

        void Awake()
        {
            previousPosition = transform.position;
        }

        void LateUpdate()
        {
            Vector3 delta = transform.position - previousPosition;
            previousPosition = transform.position;
            if (delta.sqrMagnitude < 0.000001f)
                return;

            foreach (ParticleSystem system in
                     GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = system.main;
                if (main.simulationSpace !=
                    ParticleSystemSimulationSpace.World)
                    continue;

                int count = system.particleCount;
                if (count <= 0)
                    continue;
                var values = new ParticleSystem.Particle[count];
                count = system.GetParticles(values);
                for (int index = 0; index < count; index++)
                    values[index].position += delta;
                system.SetParticles(values, count);
            }

            foreach (LineRenderer line in
                     GetComponentsInChildren<LineRenderer>(true))
            {
                if (!line.useWorldSpace || line.positionCount <= 0)
                    continue;
                var values = new Vector3[line.positionCount];
                int count = line.GetPositions(values);
                for (int index = 0; index < count; index++)
                    values[index] += delta;
                line.SetPositions(values);
            }

            foreach (TrailRenderer trail in
                     GetComponentsInChildren<TrailRenderer>(true))
            {
                int count = trail.positionCount;
                if (count <= 0)
                    continue;
                var values = new Vector3[count];
                count = trail.GetPositions(values);
                for (int index = 0; index < count; index++)
                    values[index] += delta;
                if (count > 0)
                    trail.SetPositions(values);
            }
        }
    }

    public static class WeaponEffectOrientation
    {
        public static WeaponEffectRole Infer(string resource)
        {
            string value = (resource ?? string.Empty).ToLowerInvariant();
            if (value.Contains("explosion") || value.Contains("explode"))
                return WeaponEffectRole.Explosion;
            if (value.Contains("impact") ||
                value.Contains("behit") ||
                value.Contains("hit"))
                return WeaponEffectRole.Impact;
            if (value.Contains("trail") ||
                value.Contains("smoke") ||
                value.Contains("flame") ||
                value.Contains("flare"))
                return WeaponEffectRole.Trail;
            if (value.Contains("beam") || value.Contains("tracer"))
                return WeaponEffectRole.Tracer;
            if (value.Contains("projectile") ||
                value.Contains("missile") ||
                value.Contains("plasma"))
                return WeaponEffectRole.Projectile;
            return WeaponEffectRole.Muzzle;
        }

        public static void Normalize(GameObject root, WeaponEffectRole role)
        {
            if (root == null)
                return;

            bool trajectoryVisual =
                role == WeaponEffectRole.Projectile ||
                role == WeaponEffectRole.Trail ||
                role == WeaponEffectRole.Tracer;
            bool worldSimulation = role != WeaponEffectRole.Projectile;

            foreach (ParticleSystem particle in
                     root.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = particle.main;
                if (worldSimulation)
                    main.simulationSpace =
                        ParticleSystemSimulationSpace.World;

                ParticleSystemRenderer renderer =
                    particle.GetComponent<ParticleSystemRenderer>();
                if (trajectoryVisual &&
                    renderer != null &&
                    renderer.renderMode != ParticleSystemRenderMode.Mesh)
                {
                    renderer.alignment =
                        ParticleSystemRenderSpace.Velocity;
                }
            }

            foreach (LineRenderer line in
                     root.GetComponentsInChildren<LineRenderer>(true))
            {
                line.useWorldSpace = true;
            }

            foreach (TrailRenderer trail in
                     root.GetComponentsInChildren<TrailRenderer>(true))
            {
                trail.autodestruct = false;
            }
        }

        public static void ResetForReuse(GameObject root)
        {
            if (root == null)
                return;

            foreach (TrailRenderer trail in
                     root.GetComponentsInChildren<TrailRenderer>(true))
            {
                trail.emitting = false;
                trail.Clear();
                trail.emitting = true;
            }

            foreach (ParticleSystem particle in
                     root.GetComponentsInChildren<ParticleSystem>(true))
            {
                particle.Stop(
                    true,
                    ParticleSystemStopBehavior.StopEmittingAndClear);
                particle.Clear(true);
            }
        }

        public static Quaternion RotationForVelocity(
            Vector3 velocity,
            Quaternion fallback)
        {
            return velocity.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(velocity.normalized, Vector3.up)
                : fallback;
        }
    }
}
