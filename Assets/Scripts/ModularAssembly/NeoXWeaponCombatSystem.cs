using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ModularAssembly;
using SpacecraftEditor;
using UnityEngine;

namespace UnityPlanet.ModularAssembly
{
    public enum CombatCapabilityState
    {
        Operational,
        Degraded,
        Critical,
        Ineffective
    }

    public enum WeaponDeliveryKind
    {
        HitscanTracer,
        PhysicalProjectile,
        GuidedProjectile,
        ContinuousBeam
    }

    public struct WeaponCommandFrame
    {
        public bool AimHeld;
        public bool FireHeld;
        public bool FirePressed;
        public int WeaponGroup;
        public Vector3 AimPoint;
        public Transform LockedTarget;
    }

    [Serializable]
    public sealed class WeaponProfile
    {
        public string sourceId;
        public string displayName;
        public WeaponDeliveryKind delivery;
        public float damage;
        public float shotsPerSecond;
        public float projectileSpeed;
        public float range;
        public float explosionRadius;
        public float lockSeconds;
        public float homingDegreesPerSecond;
        public int ammunition;
        public float reloadSeconds;
        public float heatPerShot;
        public float energyPerShot;
        public float spinUpSeconds;
        public bool automaticFire;
        public int cpu;
        public Color effectColor;
        public string muzzleEffect;
        public string projectileEffect;
        public string impactEffect;
    }

    public static class ModuleCpuBudget
    {
        public const int Maximum = 9999;

        static readonly Dictionary<string, int> WeaponCosts =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "machinegun_111", 40 },
                { "antiair_cannon_224", 120 },
                { "gatlin_422", 180 },
                { "missile_522", 160 },
                { "missile_fighter_522", 180 },
                { "guide_missile_222", 200 },
                { "snipercannon_422", 180 },
                { "energy_cannon_422", 170 },
                { "heavy_laser_222", 220 }
            };

        public static int Cost(GridModuleDefinition definition)
        {
            if (definition == null)
                return 0;
            string id = definition.ModuleId ?? string.Empty;
            foreach (KeyValuePair<string, int> pair in WeaponCosts)
                if (id.IndexOf(pair.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                    return pair.Value;
            int cells = Mathf.Max(
                1,
                definition.Footprint.x *
                definition.Footprint.y *
                definition.Footprint.z);
            switch (definition.Category)
            {
                case GridModuleCategory.Core:
                    return 100;
                case GridModuleCategory.Structure:
                    return Mathf.Clamp(
                        Mathf.CeilToInt(definition.MassKg / 25f),
                        1,
                        40);
                case GridModuleCategory.Armor:
                    return Mathf.Clamp(
                        Mathf.CeilToInt(definition.MassKg / 20f),
                        2,
                        60);
                case GridModuleCategory.MainThruster:
                case GridModuleCategory.RcsThruster:
                case GridModuleCategory.Mobility:
                    return Mathf.Clamp(30 + cells * 5, 35, 180);
                case GridModuleCategory.Battery:
                    return Mathf.Clamp(25 + cells * 5, 30, 120);
                case GridModuleCategory.KineticWeapon:
                    return Mathf.Clamp(60 + cells * 10, 70, 220);
                default:
                    return Mathf.Clamp(cells * 2, 1, 100);
            }
        }

        public static int Total(IEnumerable<GridModuleRecord> records)
        {
            return records == null
                ? 0
                : records.Sum(record => Cost(record == null
                    ? null
                    : record.Definition));
        }
    }

    public static class WeaponProfileLibrary
    {
        public static bool IsWeapon(GridModuleView view)
        {
            if (view == null || view.Record == null)
                return false;
            NeoXBehaviorModule behavior =
                view.GetComponentInChildren<NeoXBehaviorModule>(true);
            if (behavior != null && IsWeaponBehavior(behavior.BehaviorKind))
                return true;
            return view.Record.Definition.Category ==
                   GridModuleCategory.KineticWeapon;
        }

        public static WeaponProfile Resolve(GridModuleView view)
        {
            NeoXBehaviorModule behavior =
                view == null
                    ? null
                    : view.GetComponentInChildren<NeoXBehaviorModule>(true);
            string sourceId = behavior != null
                ? behavior.SourceId
                : view != null && view.Record != null
                    ? view.Record.Definition.ModuleId
                    : string.Empty;
            string id = (sourceId ?? string.Empty).ToLowerInvariant();
            if (id.Contains("missile_fighter_522"))
                return FighterMissile(sourceId);
            if (id.Contains("guide_missile_222"))
                return GuidedMissile(sourceId);
            if (id.Contains("missile_522"))
                return Rocket(sourceId);
            if (id.Contains("antiair_cannon_224"))
                return AntiAir(sourceId);
            if (id.Contains("snipercannon_422"))
                return Sniper(sourceId);
            if (id.Contains("energy_cannon_422"))
                return EnergyCannon(sourceId);
            if (id.Contains("heavy_laser_222"))
                return HeavyLaser(sourceId);
            if (id.Contains("gatlin_422"))
                return Gatling(sourceId);
            if (id.Contains("machinegun_111"))
                return MachineGun(sourceId);
            if (behavior != null)
            {
                switch (behavior.BehaviorKind)
                {
                    case GridModuleBehaviorKind.Gatling:
                        return Gatling(sourceId);
                    case GridModuleBehaviorKind.Cannon:
                        return AntiAir(sourceId);
                    case GridModuleBehaviorKind.SniperCannon:
                        return Sniper(sourceId);
                    case GridModuleBehaviorKind.Rocket:
                        return Rocket(sourceId);
                    case GridModuleBehaviorKind.GuidedMissile:
                        return GuidedMissile(sourceId);
                    case GridModuleBehaviorKind.EnergyCannon:
                        return EnergyCannon(sourceId);
                    case GridModuleBehaviorKind.Laser:
                        return HeavyLaser(sourceId);
                }
            }
            return MachineGun(sourceId);
        }

        static bool IsWeaponBehavior(GridModuleBehaviorKind kind)
        {
            return kind >= GridModuleBehaviorKind.KineticRapid &&
                   kind <= GridModuleBehaviorKind.Saw;
        }

        static WeaponProfile Base(
            string id,
            string name,
            WeaponDeliveryKind delivery,
            float damage,
            float rate,
            float speed,
            float range,
            int ammo,
            float reload,
            float heat,
            float energy,
            int cpu,
            Color color)
        {
            return new WeaponProfile
            {
                sourceId = id ?? string.Empty,
                displayName = name,
                delivery = delivery,
                damage = damage,
                shotsPerSecond = rate,
                projectileSpeed = speed,
                range = range,
                ammunition = ammo,
                reloadSeconds = reload,
                heatPerShot = heat,
                energyPerShot = energy,
                cpu = cpu,
                effectColor = color
            };
        }

        static WeaponProfile MachineGun(string id)
        {
            WeaponProfile p = Base(
                id, "机枪", WeaponDeliveryKind.HitscanTracer,
                18f, 12f, 850f, 420f, 120, 2f, 0.025f, 0f, 40,
                new Color(1f, 0.78f, 0.28f));
            p.muzzleEffect = "sfx/mc/machinegun_bullet_emit.sfx";
            p.projectileEffect = "sfx/mc/machinegun_bullet_shoot.sfx";
            p.impactEffect = "sfx/mc/machinegun_bullet_end.sfx";
            p.automaticFire = true;
            return p;
        }

        static WeaponProfile AntiAir(string id)
        {
            WeaponProfile p = Base(
                id, "防空炮", WeaponDeliveryKind.HitscanTracer,
                55f, 3f, 700f, 520f, 30, 2.6f, 0.08f, 0f, 120,
                new Color(1f, 0.5f, 0.15f));
            p.explosionRadius = 1.5f;
            p.muzzleEffect = "sfx/mc/antiair_cannon_shoot.sfx";
            p.projectileEffect = "sfx/mc/antiair_cannon_emit.sfx";
            p.impactEffect = "sfx/mc/antiair_cannon_end.sfx";
            p.automaticFire = true;
            return p;
        }

        static WeaponProfile Gatling(string id)
        {
            WeaponProfile p = Base(
                id, "加特林", WeaponDeliveryKind.HitscanTracer,
                12f, 20f, 900f, 380f, 240, 4f, 0.03f, 0f, 180,
                new Color(1f, 0.7f, 0.2f));
            p.spinUpSeconds = 0.7f;
            p.muzzleEffect = "sfx/mc/gatlin_emit.sfx";
            p.projectileEffect = "sfx/mc/gatlin_shoot.sfx";
            p.impactEffect = "sfx/mc/gatlin_end.sfx";
            p.automaticFire = true;
            return p;
        }

        static WeaponProfile Rocket(string id)
        {
            WeaponProfile p = Base(
                id, "普通导弹", WeaponDeliveryKind.PhysicalProjectile,
                160f, 0.8f, 900f, 750f, 4, 4f, 0.22f, 0f, 160,
                new Color(1f, 0.4f, 0.12f));
            p.explosionRadius = 4f;
            p.muzzleEffect = "Forge3DMissileMuzzle";
            p.projectileEffect = "Forge3DMissileTrail";
            p.impactEffect = "Forge3DMissileImpact";
            return p;
        }

        static WeaponProfile FighterMissile(string id)
        {
            WeaponProfile p = Base(
                id, "战斗导弹", WeaponDeliveryKind.GuidedProjectile,
                120f, 1.2f, 850f, 900f, 6, 5f, 0.16f, 0f, 180,
                new Color(1f, 0.55f, 0.16f));
            p.explosionRadius = 3.5f;
            p.lockSeconds = 0.8f;
            p.homingDegreesPerSecond = 120f;
            p.muzzleEffect = "Forge3DFighterMissileMuzzle";
            p.projectileEffect = "Forge3DFighterMissileTrail";
            p.impactEffect = "Forge3DFighterMissileImpact";
            return p;
        }

        static WeaponProfile GuidedMissile(string id)
        {
            WeaponProfile p = Base(
                id, "制导导弹", WeaponDeliveryKind.GuidedProjectile,
                200f, 0.5f, 750f, 1100f, 2, 5.5f, 0.3f, 0f, 200,
                new Color(0.35f, 0.85f, 1f));
            p.explosionRadius = 5f;
            p.lockSeconds = 1.2f;
            p.homingDegreesPerSecond = 75f;
            p.muzzleEffect = "Forge3DGuidedMissileMuzzle";
            p.projectileEffect = "Forge3DGuidedMissileTrail";
            p.impactEffect = "Forge3DGuidedMissileImpact";
            return p;
        }

        static WeaponProfile Sniper(string id)
        {
            WeaponProfile p = Base(
                id, "狙击炮", WeaponDeliveryKind.HitscanTracer,
                240f, 1f / 1.4f, 1400f, 1400f, 8, 3f, 0.35f, 0f, 180,
                new Color(0.55f, 0.9f, 1f));
            p.muzzleEffect = "sfx/mc/snipercannon_bullet_emit.sfx";
            p.projectileEffect = "sfx/mc/snipercannon_bullet_shoot.sfx";
            p.impactEffect = "sfx/mc/snipercannon_bullet_end.sfx";
            return p;
        }

        static WeaponProfile EnergyCannon(string id)
        {
            WeaponProfile p = Base(
                id, "能量炮", WeaponDeliveryKind.PhysicalProjectile,
                90f, 3f, 1100f, 800f, 0, 0f, 0.15f, 16f, 170,
                new Color(0.15f, 0.9f, 1f));
            p.explosionRadius = 2f;
            p.muzzleEffect = "sfx/mc/energy_cannon_emit.sfx";
            p.projectileEffect = "sfx/mc/energy_cannon_fire.sfx";
            p.impactEffect = "sfx/mc/energy_cannon_end.sfx";
            p.automaticFire = true;
            return p;
        }

        static WeaponProfile HeavyLaser(string id)
        {
            WeaponProfile p = Base(
                id, "重激光", WeaponDeliveryKind.ContinuousBeam,
                12f, 10f, 0f, 450f, 0, 0f, 0.02f, 3f, 220,
                new Color(0.22f, 0.9f, 1f));
            p.muzzleEffect = "sfx/block/heavy/heavy_laser_open.sfx";
            p.projectileEffect = "sfx/block/heavy/heavy_laser_fire.sfx";
            p.impactEffect = "HovlLaserRayHit";
            p.automaticFire = true;
            return p;
        }
    }

    public sealed class WeaponRuntime
    {
        public GridModuleView View;
        public NeoXBehaviorModule Semantics;
        public WeaponProfile Profile;
        public int Group;
        public int Ammunition;
        public float Heat;
        public float NextShotTime;
        public float ReloadEndsAt;
        public float TriggerHeldSeconds;
        public bool Overheated;

        public WeaponRuntime(
            GridModuleView view,
            NeoXBehaviorModule semantics,
            WeaponProfile profile)
        {
            View = view;
            Semantics = semantics;
            Profile = profile;
            Group = semantics == null
                ? 1
                : Mathf.Clamp(semantics.WeaponGroup, 1, 4);
            Ammunition = profile.ammunition;
        }

        public void Tick(float deltaTime, bool triggerHeld)
        {
            Heat = Mathf.Max(0f, Heat - deltaTime * 0.18f);
            if (Overheated && Heat <= 0.6f)
                Overheated = false;
            TriggerHeldSeconds = triggerHeld
                ? TriggerHeldSeconds + deltaTime
                : Mathf.Max(0f, TriggerHeldSeconds - deltaTime * 2f);
            if (ReloadEndsAt > 0f && Time.time >= ReloadEndsAt)
            {
                Ammunition = Profile.ammunition;
                ReloadEndsAt = 0f;
            }
        }

        public bool IsReady
        {
            get
            {
                if (View == null || Profile == null ||
                    Overheated || ReloadEndsAt > 0f ||
                    Time.time < NextShotTime)
                    return false;
                if (Profile.spinUpSeconds > 0f &&
                    TriggerHeldSeconds < Profile.spinUpSeconds)
                    return false;
                return Profile.ammunition <= 0 || Ammunition > 0;
            }
        }

        public void CommitShot()
        {
            NextShotTime = Time.time +
                           1f / Mathf.Max(0.01f, Profile.shotsPerSecond);
            Heat = Mathf.Clamp01(Heat + Profile.heatPerShot);
            if (Heat >= 0.999f)
                Overheated = true;
            if (Profile.ammunition <= 0)
                return;
            Ammunition = Mathf.Max(0, Ammunition - 1);
            if (Ammunition == 0)
                ReloadEndsAt = Time.time + Profile.reloadSeconds;
        }
    }

    public sealed class WeaponSystemCoordinator : MonoBehaviour
    {
        readonly List<WeaponRuntime> weapons = new List<WeaponRuntime>();
        readonly RaycastHit[] aimHits = new RaycastHit[64];
        GridAssemblyPresenter presenter;
        GridFlightBridge flight;
        GridAssemblyModel model;
        GridTargetController target;
        Camera sceneCamera;
        RuntimeEnergyBus energy;
        WeaponVisualPool visuals;
        WeaponProjectilePool projectiles;
        VehicleStructureGraph structureGraph;
        WeaponAirCombatAi enemyAi;
        int activeGroup = 1;
        float baseFov = 60f;
        Transform aimCandidate;
        Transform lockedTarget;
        float aimCandidateSeconds;
        bool muzzleBlocked;
        string status = string.Empty;

        public int ActiveGroup => activeGroup;
        public bool IsAiming => flight != null &&
                                flight.IsFlying &&
                                Input.GetMouseButton(1);
        public VehicleStructureGraph StructureGraph => structureGraph;

        public void Initialize(
            GridAssemblyPresenter assemblyPresenter,
            GridFlightBridge flightBridge,
            GridLabCameraController cameraController,
            GridAssemblyModel assemblyModel,
            GridTargetController targetController,
            Camera camera)
        {
            presenter = assemblyPresenter;
            flight = flightBridge;
            model = assemblyModel;
            target = targetController;
            sceneCamera = camera != null ? camera : Camera.main;
            if (sceneCamera != null)
                baseFov = sceneCamera.fieldOfView;
            energy = GetComponent<RuntimeEnergyBus>() ??
                     gameObject.AddComponent<RuntimeEnergyBus>();
            visuals = GetComponent<WeaponVisualPool>() ??
                      gameObject.AddComponent<WeaponVisualPool>();
            projectiles = GetComponent<WeaponProjectilePool>() ??
                          gameObject.AddComponent<WeaponProjectilePool>();
            projectiles.Initialize(visuals);
            structureGraph = GetComponent<VehicleStructureGraph>() ??
                             gameObject.AddComponent<VehicleStructureGraph>();
            structureGraph.Initialize(model, presenter, flight, visuals);
            presenter.Rebuilt += RebuildWeapons;
            flight.StateChanged += HandleFlightState;
            RebuildWeapons();
            if (target != null)
            {
                enemyAi = target.gameObject.GetComponent<WeaponAirCombatAi>() ??
                          target.gameObject.AddComponent<WeaponAirCombatAi>();
                enemyAi.Initialize(
                    flight,
                    structureGraph,
                    visuals,
                    transform);
            }
        }

        void HandleFlightState(GridFlightState state, string message)
        {
            if (state == GridFlightState.Flight)
            {
                structureGraph.BeginFlight();
                RebuildWeapons();
            }
            else
            {
                structureGraph.EndFlight();
                lockedTarget = null;
                aimCandidate = null;
                aimCandidateSeconds = 0f;
                RestoreFov();
            }
        }

        void RebuildWeapons()
        {
            Dictionary<string, WeaponRuntime> previous =
                weapons.Where(item => item.View != null &&
                                      item.View.Record != null)
                    .ToDictionary(
                        item => item.View.Record.RuntimeId,
                        item => item,
                        StringComparer.Ordinal);
            weapons.Clear();
            foreach (GridModuleView view in presenter.Views.Values)
            {
                if (!WeaponProfileLibrary.IsWeapon(view))
                    continue;
                NeoXBehaviorModule semantics =
                    view.GetComponentInChildren<NeoXBehaviorModule>(true);
                WeaponProfile profile = WeaponProfileLibrary.Resolve(view);
                WeaponRuntime runtime =
                    new WeaponRuntime(view, semantics, profile);
                if (view.Record != null &&
                    previous.TryGetValue(
                        view.Record.RuntimeId,
                        out WeaponRuntime old) &&
                    old.Profile.sourceId == profile.sourceId)
                {
                    runtime.Ammunition = old.Ammunition;
                    runtime.Heat = old.Heat;
                    runtime.Overheated = old.Overheated;
                    runtime.ReloadEndsAt = old.ReloadEndsAt;
                }
                weapons.Add(runtime);
            }
        }

        void Update()
        {
            if (flight == null || !flight.IsFlying)
            {
                RestoreFov();
                return;
            }
            for (int group = 1; group <= 4; group++)
                if (Input.GetKeyDown(KeyCode.Alpha0 + group))
                    activeGroup = group;

            bool aimHeld = Input.GetMouseButton(1);
            bool fireHeld = Input.GetMouseButton(0);
            bool firePressed = Input.GetMouseButtonDown(0);
            UpdateFov(aimHeld);
            Vector3 aimPoint = ResolveAimPoint(out Transform candidate);
            UpdateLock(candidate, aimHeld);
            muzzleBlocked = false;
            status = string.Empty;
            foreach (WeaponRuntime weapon in weapons)
                weapon.Tick(
                    Time.deltaTime,
                    weapon.Profile.automaticFire &&
                    fireHeld &&
                    weapon.Group == activeGroup);
            if (fireHeld || firePressed)
            {
                WeaponCommandFrame command = new WeaponCommandFrame
                {
                    AimHeld = aimHeld,
                    FireHeld = fireHeld,
                    FirePressed = firePressed,
                    WeaponGroup = activeGroup,
                    AimPoint = aimPoint,
                    LockedTarget = lockedTarget
                };
                FireGroup(command);
            }
        }

        Vector3 ResolveAimPoint(out Transform candidate)
        {
            candidate = null;
            if (sceneCamera == null)
                return transform.position + transform.forward * 1000f;
            Ray ray = sceneCamera.ViewportPointToRay(
                new Vector3(0.5f, 0.5f, 0f));
            int count = Physics.RaycastNonAlloc(
                ray,
                aimHits,
                1500f,
                ~0,
                QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            RaycastHit selected = new RaycastHit();
            bool found = false;
            for (int index = 0; index < count; index++)
            {
                RaycastHit hit = aimHits[index];
                if (hit.collider == null ||
                    hit.collider.transform.IsChildOf(transform) ||
                    hit.distance >= nearest)
                    continue;
                nearest = hit.distance;
                selected = hit;
                found = true;
            }
            if (!found)
                return ray.origin + ray.direction * 1000f;
            ISpaceDamageable damageable =
                WeaponDamageUtility.FindDamageable(selected.collider.transform);
            if (damageable != null)
                candidate = selected.collider.transform;
            return selected.point;
        }

        void UpdateLock(Transform candidate, bool aimHeld)
        {
            if (!aimHeld || candidate == null)
            {
                aimCandidate = null;
                lockedTarget = null;
                aimCandidateSeconds = 0f;
                return;
            }
            if (candidate != aimCandidate)
            {
                aimCandidate = candidate;
                lockedTarget = null;
                aimCandidateSeconds = 0f;
                return;
            }
            aimCandidateSeconds += Time.deltaTime;
            float required = weapons
                .Where(item => item.Group == activeGroup &&
                               item.Profile.lockSeconds > 0f)
                .Select(item => item.Profile.lockSeconds)
                .DefaultIfEmpty(float.PositiveInfinity)
                .Min();
            if (aimCandidateSeconds >= required)
                lockedTarget = candidate;
        }

        void FireGroup(WeaponCommandFrame command)
        {
            foreach (WeaponRuntime weapon in weapons)
            {
                if (weapon.Group != command.WeaponGroup ||
                    !(weapon.Profile.automaticFire
                        ? command.FireHeld
                        : command.FirePressed) ||
                    !weapon.IsReady)
                    continue;
                if (weapon.Profile.energyPerShot > 0f &&
                    !energy.TryConsume(
                        weapon.Profile.energyPerShot,
                        1))
                {
                    status = "能源不足";
                    continue;
                }
                Vector3 muzzle = weapon.Semantics != null
                    ? weapon.Semantics.WorldMuzzlePosition
                    : weapon.View.transform.position +
                      weapon.View.transform.forward * 0.8f;
                Vector3 direction =
                    (command.AimPoint - muzzle).normalized;
                if (direction.sqrMagnitude < 0.5f)
                    direction = weapon.View.transform.forward;
                float spread = command.AimHeld ? 0.18f : 1.25f;
                if (weapon.Profile.delivery ==
                    WeaponDeliveryKind.ContinuousBeam)
                    spread = 0.05f;
                direction = ApplySpread(direction, spread);
                if (WeaponDamageUtility.IsMuzzleBlocked(
                        muzzle,
                        direction,
                        weapon.View.transform,
                        transform))
                {
                    muzzleBlocked = true;
                    status = "炮口受阻";
                    continue;
                }
                FireWeapon(weapon, muzzle, direction, command);
                weapon.CommitShot();
            }
        }

        void FireWeapon(
            WeaponRuntime weapon,
            Vector3 muzzle,
            Vector3 direction,
            WeaponCommandFrame command)
        {
            WeaponProfile profile = weapon.Profile;
            if (profile.delivery != WeaponDeliveryKind.ContinuousBeam)
                visuals.SpawnMuzzle(
                    muzzle,
                    direction,
                    profile.effectColor,
                    profile.muzzleEffect,
                    weapon.View.transform);
            switch (profile.delivery)
            {
                case WeaponDeliveryKind.HitscanTracer:
                {
                    Vector3 end = muzzle + direction * profile.range;
                    if (WeaponDamageUtility.Trace(
                            muzzle,
                            direction,
                            profile.range,
                            transform,
                            gameObject,
                            profile,
                            visuals,
                            out RaycastHit hit))
                        end = hit.point;
                    visuals.SpawnTracer(
                        muzzle,
                        end,
                        profile.effectColor,
                        0.07f,
                        profile.projectileEffect);
                    break;
                }
                case WeaponDeliveryKind.ContinuousBeam:
                {
                    Vector3 end = muzzle + direction * profile.range;
                    Vector3 hitNormal = -direction;
                    bool hasHit = WeaponDamageUtility.Trace(
                        muzzle,
                        direction,
                        profile.range,
                        transform,
                        gameObject,
                        profile,
                        visuals,
                        out RaycastHit hit);
                    if (hasHit)
                    {
                        end = hit.point;
                        hitNormal = hit.normal;
                    }
                    visuals.SpawnContinuousLaser(
                        muzzle,
                        end,
                        hitNormal,
                        hasHit,
                        0.16f);
                    break;
                }
                default:
                    projectiles.Launch(
                        muzzle,
                        direction,
                        GetComponent<Rigidbody>() == null
                            ? Vector3.zero
                            : GetComponent<Rigidbody>().velocity,
                        profile,
                        transform,
                        command.LockedTarget);
                    break;
            }
            RobocraftMotionCoordinator rc1 =
                GetComponent<RobocraftMotionCoordinator>();
            if (rc1 != null && rc1.IsActive)
                rc1.QueueVisualRecoil(
                    -direction * Mathf.Clamp(
                        profile.damage * 1.5f,
                        20f,
                        300f),
                    muzzle);
        }

        static Vector3 ApplySpread(Vector3 direction, float degrees)
        {
            Vector2 random = UnityEngine.Random.insideUnitCircle * degrees;
            Quaternion rotation =
                Quaternion.AngleAxis(random.x, Vector3.up) *
                Quaternion.AngleAxis(random.y, Vector3.right);
            return (rotation * direction).normalized;
        }

        void UpdateFov(bool aiming)
        {
            if (sceneCamera == null)
                return;
            bool sniper = weapons.Any(item =>
                item.Group == activeGroup &&
                item.Profile.sourceId.IndexOf(
                    "snipercannon_422",
                    StringComparison.OrdinalIgnoreCase) >= 0);
            float targetFov = aiming
                ? sniper ? 22f : 38f
                : baseFov;
            sceneCamera.fieldOfView = Mathf.Lerp(
                sceneCamera.fieldOfView,
                targetFov,
                1f - Mathf.Exp(-Time.unscaledDeltaTime * 9f));
        }

        void RestoreFov()
        {
            if (sceneCamera != null)
                sceneCamera.fieldOfView = Mathf.Lerp(
                    sceneCamera.fieldOfView,
                    baseFov,
                    1f - Mathf.Exp(-Time.unscaledDeltaTime * 9f));
        }

        void OnGUI()
        {
            if (model != null && (flight == null || !flight.IsFlying))
            {
                int cpu = ModuleCpuBudget.Total(model.Records);
                GUI.Box(
                    new Rect(Screen.width - 220f, 122f, 196f, 34f),
                    "CPU " + cpu + " / " + ModuleCpuBudget.Maximum);
                return;
            }
            if (flight == null || !flight.IsFlying)
                return;
            float x = Screen.width * 0.5f;
            float y = Screen.height * 0.5f;
            Color old = GUI.color;
            GUI.color = muzzleBlocked
                ? new Color(1f, 0.35f, 0.18f)
                : lockedTarget != null
                    ? new Color(0.2f, 1f, 0.45f)
                    : new Color(0.18f, 0.9f, 1f);
            GUI.DrawTexture(
                new Rect(x - 16f, y - 1f, 12f, 2f),
                Texture2D.whiteTexture);
            GUI.DrawTexture(
                new Rect(x + 4f, y - 1f, 12f, 2f),
                Texture2D.whiteTexture);
            GUI.DrawTexture(
                new Rect(x - 1f, y - 16f, 2f, 12f),
                Texture2D.whiteTexture);
            GUI.DrawTexture(
                new Rect(x - 1f, y + 4f, 2f, 12f),
                Texture2D.whiteTexture);
            GUI.color = old;
            string lockText = aimCandidate == null
                ? string.Empty
                : lockedTarget != null
                    ? "目标锁定"
                    : "锁定 " + Mathf.RoundToInt(
                        Mathf.Clamp01(aimCandidateSeconds / 1.2f) *
                        100f) + "%";
            GUI.Box(
                new Rect(20f, Screen.height - 92f, 260f, 68f),
                "武器组 " + activeGroup +
                (string.IsNullOrEmpty(lockText)
                    ? string.Empty
                    : "  " + lockText) +
                (string.IsNullOrEmpty(status)
                    ? string.Empty
                    : "\n" + status));
        }

        void OnDestroy()
        {
            if (presenter != null)
                presenter.Rebuilt -= RebuildWeapons;
            if (flight != null)
                flight.StateChanged -= HandleFlightState;
        }
    }

    public static class WeaponDamageUtility
    {
        static readonly RaycastHit[] Hits = new RaycastHit[64];
        static readonly Collider[] Overlaps = new Collider[128];

        public static ISpaceDamageable FindDamageable(Transform start)
        {
            for (Transform current = start;
                 current != null;
                 current = current.parent)
            {
                ISpaceDamageable found =
                    current.GetComponent(typeof(ISpaceDamageable))
                    as ISpaceDamageable;
                if (found != null)
                    return found;
            }
            return null;
        }

        public static bool IsMuzzleBlocked(
            Vector3 origin,
            Vector3 direction,
            Transform weaponRoot,
            Transform ownerRoot)
        {
            int count = Physics.RaycastNonAlloc(
                origin,
                direction,
                Hits,
                4f,
                ~0,
                QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            Collider selected = null;
            for (int index = 0; index < count; index++)
            {
                Collider collider = Hits[index].collider;
                if (collider == null ||
                    Hits[index].distance >= nearest ||
                    collider.transform == weaponRoot ||
                    collider.transform.IsChildOf(weaponRoot))
                    continue;
                nearest = Hits[index].distance;
                selected = collider;
            }
            return selected != null &&
                   selected.transform.IsChildOf(ownerRoot);
        }

        public static bool Trace(
            Vector3 origin,
            Vector3 direction,
            float range,
            Transform ownerRoot,
            GameObject source,
            WeaponProfile profile,
            WeaponVisualPool visuals,
            out RaycastHit selected)
        {
            selected = new RaycastHit();
            int count = Physics.RaycastNonAlloc(
                origin,
                direction,
                Hits,
                range,
                ~0,
                QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            bool found = false;
            for (int index = 0; index < count; index++)
            {
                RaycastHit hit = Hits[index];
                if (hit.collider == null ||
                    hit.collider.transform.IsChildOf(ownerRoot) ||
                    hit.distance >= nearest)
                    continue;
                nearest = hit.distance;
                selected = hit;
                found = true;
            }
            if (!found)
                return false;
            ApplyDirect(
                selected.collider,
                profile.damage,
                selected.point,
                direction,
                source);
            if (profile.explosionRadius > 0.01f)
                ApplyExplosion(
                    selected.point,
                    profile.explosionRadius,
                    profile.damage,
                    ownerRoot,
                    source);
            visuals?.SpawnImpact(
                selected.point,
                selected.normal,
                profile.effectColor,
                profile.impactEffect,
                profile.explosionRadius);
            return true;
        }

        public static void ApplyDirect(
            Collider collider,
            float damage,
            Vector3 point,
            Vector3 direction,
            GameObject source)
        {
            ISpaceDamageable damageable =
                collider == null
                    ? null
                    : FindDamageable(collider.transform);
            damageable?.ApplyDamage(new SpaceDamageInfo(
                damage,
                point,
                direction.normalized * Mathf.Clamp(
                    damage * 0.5f,
                    5f,
                    160f),
                SpaceDamageType.Projectile,
                source));
        }

        public static void ApplyExplosion(
            Vector3 point,
            float radius,
            float damage,
            Transform ownerRoot,
            GameObject source)
        {
            int count = Physics.OverlapSphereNonAlloc(
                point,
                radius,
                Overlaps,
                ~0,
                QueryTriggerInteraction.Ignore);
            var applied = new HashSet<ISpaceDamageable>();
            for (int index = 0; index < count; index++)
            {
                Collider collider = Overlaps[index];
                if (collider == null ||
                    collider.transform.IsChildOf(ownerRoot))
                    continue;
                ISpaceDamageable target =
                    FindDamageable(collider.transform);
                if (target == null || !applied.Add(target))
                    continue;
                Vector3 closest = collider.ClosestPoint(point);
                float distance = Vector3.Distance(point, closest);
                float falloff = 1f - Mathf.Clamp01(distance / radius);
                if (falloff <= 0f)
                    continue;
                Vector3 direction =
                    (closest - point).sqrMagnitude > 0.001f
                        ? (closest - point).normalized
                        : Vector3.up;
                target.ApplyDamage(new SpaceDamageInfo(
                    damage * falloff,
                    closest,
                    direction * damage * falloff,
                    SpaceDamageType.Explosion,
                    source));
            }
        }
    }

    public sealed class WeaponVisualPool : MonoBehaviour
    {
        sealed class LineSlot
        {
            public GameObject Root;
            public LineRenderer Line;
            public float EndsAt;
        }

        sealed class LaserSlot
        {
            public GameObject Root;
            public LineRenderer Line;
            public Transform HitRoot;
            public ParticleSystem[] Particles;
            public float EndsAt;
        }

        readonly List<LineSlot> lines = new List<LineSlot>();
        readonly List<LaserSlot> lasers = new List<LaserSlot>();
        readonly List<ParticleSystem> particles =
            new List<ParticleSystem>();
        Material lineMaterial;
        Material particleMaterial;
        GameObject hovlLaserPrefab;

        void Awake()
        {
            Shader unlit = Shader.Find(
                               "Universal Render Pipeline/Unlit") ??
                           Shader.Find("Unlit/Color");
            lineMaterial = new Material(unlit);
            particleMaterial = new Material(unlit);
            hovlLaserPrefab = Resources.Load<GameObject>(
                "WeaponEffects/HovlLaserRay");
        }

        void Update()
        {
            foreach (LineSlot slot in lines)
                if (slot.Root.activeSelf && Time.time >= slot.EndsAt)
                    slot.Root.SetActive(false);
            foreach (LaserSlot slot in lasers)
            {
                if (!slot.Root.activeSelf || Time.time < slot.EndsAt)
                    continue;
                foreach (ParticleSystem particle in slot.Particles)
                    if (particle != null)
                        particle.Stop(
                            true,
                            ParticleSystemStopBehavior
                                .StopEmittingAndClear);
                slot.Root.SetActive(false);
            }
        }

        public void SpawnTracer(
            Vector3 start,
            Vector3 end,
            Color color,
            float lifetime,
            string sourceEffect)
        {
            if (Forge3DWeaponPresentation.TrySpawnTracer(
                    transform,
                    start,
                    end,
                    sourceEffect,
                    lifetime))
                return;
            LineSlot slot = AcquireLine();
            slot.Root.name = "Tracer_" +
                             ShortEffectName(sourceEffect);
            slot.Root.SetActive(true);
            slot.Line.SetPosition(0, start);
            slot.Line.SetPosition(1, end);
            slot.Line.startColor = color;
            slot.Line.endColor = new Color(
                color.r,
                color.g,
                color.b,
                0.08f);
            float distance = Vector3.Distance(start, end);
            slot.Line.startWidth = Mathf.Clamp(
                0.035f + distance * 0.00008f,
                0.035f,
                0.12f);
            slot.Line.endWidth = slot.Line.startWidth * 0.35f;
            slot.EndsAt = Time.time + lifetime;
        }

        public void SpawnContinuousLaser(
            Vector3 start,
            Vector3 end,
            Vector3 hitNormal,
            bool hasHit,
            float lifetime)
        {
            LaserSlot slot = AcquireLaser();
            if (slot == null)
            {
                SpawnTracer(
                    start,
                    end,
                    new Color(0.22f, 0.9f, 1f),
                    lifetime,
                    "HovlLaserRayFallback");
                return;
            }

            Vector3 delta = end - start;
            if (delta.sqrMagnitude < 0.0001f)
                return;
            slot.Root.transform.SetPositionAndRotation(
                start,
                Quaternion.LookRotation(delta.normalized, Vector3.up));
            slot.Root.SetActive(true);
            float beamWidth = Mathf.Clamp(
                0.085f + delta.magnitude * 0.00012f,
                0.085f,
                0.16f);
            slot.Line.startColor = new Color(0.72f, 0.98f, 1f, 1f);
            slot.Line.endColor = new Color(0.08f, 0.68f, 1f, 0.92f);
            slot.Line.startWidth = beamWidth;
            slot.Line.endWidth = beamWidth * 0.72f;
            slot.Line.enabled = true;
            slot.Line.useWorldSpace = true;
            slot.Line.positionCount = 2;
            slot.Line.SetPosition(0, start);
            slot.Line.SetPosition(1, end);
            Material beamMaterial = slot.Line.material;
            float distance = delta.magnitude;
            if (beamMaterial != null &&
                beamMaterial.HasProperty("_MainTex"))
                beamMaterial.SetTextureScale(
                    "_MainTex",
                    new Vector2(Mathf.Max(1f, distance), 1f));
            if (beamMaterial != null &&
                beamMaterial.HasProperty("_Noise"))
                beamMaterial.SetTextureScale(
                    "_Noise",
                    new Vector2(Mathf.Max(1f, distance), 1f));

            if (slot.HitRoot != null)
            {
                slot.HitRoot.gameObject.SetActive(hasHit);
                if (hasHit)
                    slot.HitRoot.SetPositionAndRotation(
                        end,
                        Quaternion.LookRotation(
                            hitNormal.sqrMagnitude > 0.01f
                                ? hitNormal
                                : -delta.normalized));
            }
            foreach (ParticleSystem particle in slot.Particles)
            {
                if (particle == null ||
                    (slot.HitRoot != null &&
                     particle.transform.IsChildOf(slot.HitRoot) &&
                     !hasHit))
                    continue;
                particle.Stop(
                    true,
                    ParticleSystemStopBehavior.StopEmittingAndClear);
                particle.Play(true);
            }
            slot.EndsAt = Time.time + lifetime;
        }

        public void SpawnMuzzle(
            Vector3 position,
            Vector3 direction,
            Color color,
            string sourceEffect,
            Transform weaponRoot)
        {
            if (Forge3DWeaponPresentation.TrySpawnMuzzle(
                    transform,
                    position,
                    direction,
                    sourceEffect,
                    weaponRoot))
                return;
            EmitParticles(
                "Muzzle_" + ShortEffectName(sourceEffect),
                position,
                direction,
                color,
                8,
                0.07f,
                4f);
        }

        public void SpawnImpact(
            Vector3 position,
            Vector3 normal,
            Color color,
            string sourceEffect,
            float radius)
        {
            if (string.Equals(
                    sourceEffect,
                    "HovlLaserRayHit",
                    StringComparison.Ordinal))
                return;
            if (Forge3DWeaponPresentation.TrySpawnImpact(
                    transform,
                    position,
                    normal,
                    sourceEffect,
                    radius))
                return;
            EmitParticles(
                "Impact_" + ShortEffectName(sourceEffect),
                position,
                normal.sqrMagnitude > 0.1f ? normal : Vector3.up,
                color,
                radius > 0.1f ? 20 : 10,
                radius > 0.1f ? 0.18f : 0.09f,
                radius > 0.1f ? 8f : 4f);
        }

        public void SpawnBreakup(Bounds bounds)
        {
            EmitParticles(
                "DisconnectedModuleBreakup",
                bounds.center,
                Vector3.up,
                new Color(0.3f, 0.85f, 1f),
                Mathf.Clamp(
                    Mathf.CeilToInt(bounds.size.magnitude * 5f),
                    12,
                    48),
                0.16f,
                Mathf.Clamp(bounds.extents.magnitude * 2f, 2f, 9f));
        }

        void EmitParticles(
            string effectName,
            Vector3 position,
            Vector3 direction,
            Color color,
            int count,
            float size,
            float speed)
        {
            ParticleSystem system = AcquireParticles();
            system.gameObject.name = effectName;
            system.transform.position = position;
            system.transform.rotation = Quaternion.LookRotation(
                direction.sqrMagnitude > 0.1f
                    ? direction
                    : Vector3.forward);
            ParticleSystem.MainModule main = system.main;
            main.startLifetime = 0.8f;
            main.startSpeed = speed;
            main.startSize = size;
            main.startColor = color;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = false;
            system.Emit(count);
        }

        LaserSlot AcquireLaser()
        {
            if (hovlLaserPrefab == null)
                return null;
            LaserSlot slot = lasers.Find(item => !item.Root.activeSelf);
            if (slot != null)
                return slot;

            GameObject root = Instantiate(
                hovlLaserPrefab,
                CombatTransientRoot.GetOrCreate());
            root.name = "HeavyLaser_HovlRay";
            foreach (MonoBehaviour behaviour in
                     root.GetComponents<MonoBehaviour>())
            {
                if (behaviour != null &&
                    behaviour.GetType().Name == "Hovl_Laser")
                    behaviour.enabled = false;
            }
            LineRenderer line = root.GetComponent<LineRenderer>();
            if (line == null)
            {
                Destroy(root);
                return null;
            }
            Transform hitRoot = root
                .GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(item => item.name == "Hit");
            slot = new LaserSlot
            {
                Root = root,
                Line = line,
                HitRoot = hitRoot,
                Particles =
                    root.GetComponentsInChildren<ParticleSystem>(true)
            };
            root.SetActive(false);
            lasers.Add(slot);
            return slot;
        }

        LineSlot AcquireLine()
        {
            LineSlot slot = lines.Find(item => !item.Root.activeSelf);
            if (slot != null)
                return slot;
            GameObject root = new GameObject("WeaponTracer");
            root.transform.SetParent(
                CombatTransientRoot.GetOrCreate(),
                false);
            LineRenderer line = root.AddComponent<LineRenderer>();
            line.sharedMaterial = lineMaterial;
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.textureMode = LineTextureMode.Stretch;
            line.numCapVertices = 2;
            root.SetActive(false);
            slot = new LineSlot { Root = root, Line = line };
            lines.Add(slot);
            return slot;
        }

        ParticleSystem AcquireParticles()
        {
            ParticleSystem system = particles.Find(item =>
                item != null && !item.IsAlive(true));
            if (system != null)
                return system;
            GameObject root = new GameObject("WeaponParticles");
            root.transform.SetParent(
                CombatTransientRoot.GetOrCreate(),
                false);
            system = root.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = system.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace =
                ParticleSystemSimulationSpace.World;
            ParticleSystemRenderer renderer =
                root.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = particleMaterial;
            particles.Add(system);
            return system;
        }

        static string ShortEffectName(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
                return "Fallback";
            int slash = Mathf.Max(
                source.LastIndexOf('/'),
                source.LastIndexOf('\\'));
            return slash >= 0
                ? source.Substring(slash + 1)
                : source;
        }
    }

    public sealed class WeaponProjectilePool : MonoBehaviour
    {
        readonly List<WeaponProjectile> projectiles =
            new List<WeaponProjectile>();
        WeaponVisualPool visuals;

        public void Initialize(WeaponVisualPool effectPool)
        {
            visuals = effectPool;
        }

        public void Launch(
            Vector3 position,
            Vector3 direction,
            Vector3 inheritedVelocity,
            WeaponProfile profile,
            Transform owner,
            Transform lockedTarget)
        {
            WeaponProjectile projectile =
                projectiles.Find(item => item != null &&
                                         !item.gameObject.activeSelf);
            if (projectile == null)
            {
                GameObject root =
                    GameObject.CreatePrimitive(PrimitiveType.Cube);
                root.name = "PooledWeaponProjectile";
                root.transform.SetParent(
                    CombatTransientRoot.GetOrCreate(),
                    false);
                Collider collider = root.GetComponent<Collider>();
                if (collider != null)
                    Destroy(collider);
                projectile = root.AddComponent<WeaponProjectile>();
                projectile.Initialize(this);
                projectiles.Add(projectile);
            }
            projectile.Launch(
                position,
                direction,
                inheritedVelocity,
                profile,
                owner,
                lockedTarget,
                visuals);
        }

        public void Release(WeaponProjectile projectile)
        {
            if (projectile != null)
            {
                projectile.ResetForPool();
                projectile.gameObject.SetActive(false);
            }
        }
    }

    public sealed class WeaponProjectile : MonoBehaviour
    {
        readonly RaycastHit[] hits = new RaycastHit[32];
        WeaponProjectilePool pool;
        WeaponVisualPool visuals;
        WeaponProfile profile;
        Transform owner;
        Transform target;
        Vector3 velocity;
        float expiresAt;
        float radius;
        Renderer bodyRenderer;
        GameObject flightVisual;
        string flightVisualResource;
        Vector3 flightVisualBaseScale = Vector3.one;
        ParticleSystem[] flightVisualParticles =
            Array.Empty<ParticleSystem>();

        public Vector3 CurrentVelocity => velocity;
        public bool IsGuided =>
            profile != null &&
            profile.delivery ==
            WeaponDeliveryKind.GuidedProjectile;

        public void Initialize(WeaponProjectilePool source)
        {
            pool = source;
            bodyRenderer = GetComponent<Renderer>();
            gameObject.SetActive(false);
        }

        public void Launch(
            Vector3 position,
            Vector3 direction,
            Vector3 inheritedVelocity,
            WeaponProfile weaponProfile,
            Transform source,
            Transform lockedTarget,
            WeaponVisualPool effectPool)
        {
            profile = weaponProfile;
            owner = source;
            target =
                weaponProfile.delivery ==
                WeaponDeliveryKind.GuidedProjectile
                    ? lockedTarget
                    : null;
            visuals = effectPool;
            transform.SetParent(
                CombatTransientRoot.GetOrCreate(),
                false);
            transform.position = position;
            velocity = direction.normalized *
                       profile.projectileSpeed +
                       inheritedVelocity;
            transform.rotation =
                WeaponEffectOrientation.RotationForVelocity(
                    velocity,
                    Quaternion.identity);
            bool missile = profile.delivery ==
                           WeaponDeliveryKind.GuidedProjectile ||
                           profile.explosionRadius >= 3f;
            transform.localScale = missile
                ? new Vector3(0.18f, 0.18f, 0.65f)
                : Vector3.one * 0.24f;
            radius = missile ? 0.12f : 0.08f;
            Renderer renderer = bodyRenderer != null
                ? bodyRenderer
                : GetComponent<Renderer>();
            if (renderer != null)
            {
                var block = new MaterialPropertyBlock();
                block.SetColor("_BaseColor", profile.effectColor);
                block.SetColor("_Color", profile.effectColor);
                renderer.SetPropertyBlock(block);
            }
            expiresAt = Time.time +
                        Mathf.Max(2f, profile.range /
                        Mathf.Max(1f, profile.projectileSpeed));
            gameObject.name = "Projectile_" +
                              profile.displayName;
            gameObject.SetActive(true);
            ConfigureFlightVisual();
        }

        void ConfigureFlightVisual()
        {
            if (!Forge3DWeaponPresentation.TryGetProjectileVisual(
                    profile.projectileEffect,
                    out string resource,
                    out float visualScale))
            {
                if (flightVisual != null)
                    flightVisual.SetActive(false);
                if (bodyRenderer != null)
                    bodyRenderer.enabled = true;
                return;
            }

            if (flightVisual == null ||
                !string.Equals(
                    flightVisualResource,
                    resource,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (flightVisual != null)
                {
                    flightVisual.SetActive(false);
                    Destroy(flightVisual);
                }

                GameObject prefab = Resources.Load<GameObject>(resource);
                if (prefab == null)
                {
                    flightVisual = null;
                    flightVisualResource = null;
                    if (bodyRenderer != null)
                        bodyRenderer.enabled = true;
                    return;
                }

                flightVisual = Instantiate(prefab, transform, false);
                flightVisual.name = "Forge3D_ProjectileVisual";
                flightVisual.transform.localPosition = Vector3.zero;
                flightVisual.transform.localRotation = Quaternion.identity;
                WeaponEffectOrientation.Normalize(
                    flightVisual,
                    WeaponEffectRole.Projectile);
                flightVisualBaseScale = flightVisual.transform.localScale;
                flightVisualResource = resource;
                foreach (MonoBehaviour behaviour in
                         flightVisual.GetComponentsInChildren
                             <MonoBehaviour>(true))
                    if (behaviour != null)
                        behaviour.enabled = false;
                flightVisualParticles =
                    flightVisual.GetComponentsInChildren
                        <ParticleSystem>(true);
            }

            if (bodyRenderer != null)
                bodyRenderer.enabled = false;
            flightVisual.transform.localScale =
                flightVisualBaseScale * Mathf.Max(0.05f, visualScale);
            flightVisual.SetActive(true);
            foreach (ParticleSystem particle in flightVisualParticles)
            {
                if (particle == null)
                    continue;
                particle.Stop(
                    true,
                    ParticleSystemStopBehavior.StopEmittingAndClear);
                particle.Play(true);
            }
        }

        void FixedUpdate()
        {
            if (profile == null)
                return;
            if (target != null &&
                profile.delivery ==
                WeaponDeliveryKind.GuidedProjectile)
            {
                Vector3 desired =
                    (target.position - transform.position).normalized;
                Vector3 current = velocity.normalized;
                Vector3 steered = Vector3.RotateTowards(
                    current,
                    desired,
                    profile.homingDegreesPerSecond *
                    Mathf.Deg2Rad * Time.fixedDeltaTime,
                    0f);
                velocity = steered *
                           Mathf.Max(
                               profile.projectileSpeed,
                               velocity.magnitude);
            }
            Vector3 start = transform.position;
            Vector3 delta = velocity * Time.fixedDeltaTime;
            if (TryHit(start, delta, out RaycastHit hit))
            {
                ResolveImpact(hit);
                pool.Release(this);
                return;
            }
            transform.position = start + delta;
            if (velocity.sqrMagnitude > 0.01f)
                transform.rotation =
                    WeaponEffectOrientation.RotationForVelocity(
                        velocity,
                        transform.rotation);
            visuals.SpawnTracer(
                start,
                transform.position,
                profile.effectColor,
                0.12f,
                profile.projectileEffect);
            if (Time.time >= expiresAt)
                pool.Release(this);
        }

        void ResolveImpact(RaycastHit hit)
        {
            GameObject source =
                owner == null ? gameObject : owner.gameObject;
            WeaponDamageUtility.ApplyDirect(
                hit.collider,
                profile.damage,
                hit.point,
                velocity.normalized,
                source);
            if (profile.explosionRadius > 0.01f)
            {
                WeaponDamageUtility.ApplyExplosion(
                    hit.point,
                    profile.explosionRadius,
                    profile.damage,
                    owner,
                    source);
            }
            visuals.SpawnImpact(
                hit.point,
                hit.normal,
                profile.effectColor,
                profile.impactEffect,
                profile.explosionRadius);
        }

        public void ResetForPool()
        {
            target = null;
            owner = null;
            velocity = Vector3.zero;
            if (flightVisual != null)
                WeaponEffectOrientation.ResetForReuse(flightVisual);
        }

        bool TryHit(
            Vector3 start,
            Vector3 delta,
            out RaycastHit selected)
        {
            selected = new RaycastHit();
            float distance = delta.magnitude;
            if (distance <= 0.0001f)
                return false;
            int count = Physics.SphereCastNonAlloc(
                start,
                radius,
                delta / distance,
                hits,
                distance,
                ~0,
                QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            bool found = false;
            for (int index = 0; index < count; index++)
            {
                RaycastHit hit = hits[index];
                if (hit.collider == null ||
                    (owner != null &&
                     hit.collider.transform.IsChildOf(owner)) ||
                    hit.distance >= nearest)
                    continue;
                nearest = hit.distance;
                selected = hit;
                found = true;
            }
            return found;
        }
    }

    public sealed class VehicleModuleDamageReceiver :
        MonoBehaviour,
        ISpaceDamageable
    {
        VehicleStructureGraph graph;
        string runtimeId;

        public float Integrity =>
            graph == null ? 0f : graph.Integrity(runtimeId);
        public float MaximumIntegrity =>
            graph == null ? 0f : graph.MaximumIntegrity(runtimeId);
        public bool IsDestroyed =>
            graph == null || graph.IsDestroyed(runtimeId);

        public void Initialize(
            VehicleStructureGraph source,
            string id)
        {
            graph = source;
            runtimeId = id;
        }

        public void ApplyDamage(SpaceDamageInfo damage)
        {
            graph?.ApplyDamage(runtimeId, damage);
        }
    }

    public sealed class VehicleStructureGraph : MonoBehaviour
    {
        sealed class Node
        {
            public GridModuleRecord Record;
            public GridModuleView View;
            public float Health;
            public float MaximumHealth;
            public int Cpu;
            public bool Destroyed;
            public string SupportId;
            public readonly HashSet<string> Edges =
                new HashSet<string>(StringComparer.Ordinal);
            public readonly HashSet<string> Dependents =
                new HashSet<string>(StringComparer.Ordinal);
        }

        static readonly Vector3Int[] Neighbors =
        {
            Vector3Int.right,
            Vector3Int.left,
            Vector3Int.up,
            Vector3Int.down,
            Vector3Int.forward,
            Vector3Int.back
        };

        readonly Dictionary<string, Node> nodes =
            new Dictionary<string, Node>(StringComparer.Ordinal);
        GridAssemblyModel model;
        GridAssemblyPresenter presenter;
        GridFlightBridge flight;
        WeaponVisualPool visuals;
        ModularBlueprintData flightBlueprint;
        int initialCpu;
        bool active;
        bool processingDamage;
        bool rebuildPending;
        bool vehicleDestroyed;
        bool automaticReturnToBuild = true;

        public bool Active => active && !vehicleDestroyed;
        public bool IsVehicleDestroyed => vehicleDestroyed;
        public float ConnectedCpuRatio =>
            initialCpu <= 0
                ? 1f
                : Mathf.Clamp01(ConnectedCpu() / (float)initialCpu);
        public float OverallHealthRatio
        {
            get
            {
                float maximum = nodes.Values.Sum(item => item.MaximumHealth);
                return maximum <= 0.01f
                    ? 1f
                    : Mathf.Clamp01(
                        nodes.Values.Sum(item => item.Health) / maximum);
            }
        }
        public CombatCapabilityState CapabilityState
        {
            get
            {
                if (vehicleDestroyed || ConnectedCpuRatio < 0.2f)
                    return CombatCapabilityState.Ineffective;
                if (ConnectedCpuRatio < 0.35f)
                    return CombatCapabilityState.Critical;
                if (ConnectedCpuRatio < 0.7f || OverallHealthRatio < 0.65f)
                    return CombatCapabilityState.Degraded;
                return CombatCapabilityState.Operational;
            }
        }
        public event Action StructureChanged;
        public event Action Destroyed;

        public void SetAutomaticReturnToBuild(bool value)
        {
            automaticReturnToBuild = value;
        }

        public void Initialize(
            GridAssemblyModel assemblyModel,
            GridAssemblyPresenter assemblyPresenter,
            GridFlightBridge flightBridge,
            WeaponVisualPool effectPool)
        {
            model = assemblyModel;
            presenter = assemblyPresenter;
            flight = flightBridge;
            visuals = effectPool;
            presenter.Rebuilt += HandlePresenterRebuilt;
            RebuildGraph();
        }

        public void BeginFlight()
        {
            active = true;
            vehicleDestroyed = false;
            flightBlueprint = model.CaptureBlueprint();
            RebuildGraph();
            initialCpu = ConnectedCpu();
            StructureChanged?.Invoke();
        }

        public void EndFlight()
        {
            active = false;
        }

        void HandlePresenterRebuilt()
        {
            if (processingDamage)
            {
                rebuildPending = true;
                return;
            }
            RebuildGraph();
        }

        void RebuildGraph()
        {
            Dictionary<string, float> oldHealth =
                nodes.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.Health,
                    StringComparer.Ordinal);
            nodes.Clear();
            var occupancy =
                new Dictionary<Vector3Int, string>();
            foreach (GridModuleRecord record in model.Records)
            {
                GridModuleView view = presenter.Find(
                    record.RuntimeId);
                float maximum = Mathf.Max(
                    1f,
                    record.Definition.MaxIntegrity);
                Node node = new Node
                {
                    Record = record,
                    View = view,
                    MaximumHealth = maximum,
                    Health = oldHealth.TryGetValue(
                        record.RuntimeId,
                        out float previous)
                        ? Mathf.Min(previous, maximum)
                        : maximum,
                    Cpu = ModuleCpuBudget.Cost(record.Definition)
                };
                nodes[record.RuntimeId] = node;
                foreach (Vector3Int cell in model.GetCells(record))
                    occupancy[cell] = record.RuntimeId;
                if (view != null)
                {
                    VehicleModuleDamageReceiver receiver =
                        view.GetComponent<VehicleModuleDamageReceiver>() ??
                        view.gameObject.AddComponent<
                            VehicleModuleDamageReceiver>();
                    receiver.Initialize(this, record.RuntimeId);
                }
            }
            foreach (KeyValuePair<Vector3Int, string> pair in occupancy)
            foreach (Vector3Int offset in Neighbors)
            {
                if (!occupancy.TryGetValue(
                        pair.Key + offset,
                        out string other) ||
                    other == pair.Value)
                    continue;
                nodes[pair.Value].Edges.Add(other);
                nodes[other].Edges.Add(pair.Value);
            }
            RebuildMountDependencies();
        }

        void RebuildMountDependencies()
        {
            foreach (Node node in nodes.Values)
            {
                node.SupportId = null;
                node.Dependents.Clear();
            }
            Dictionary<string, int> depth = BuildCoreDepths();
            foreach (KeyValuePair<string, Node> pair in nodes)
            {
                Node node = pair.Value;
                if (!RequiresMountSupport(node) ||
                    !depth.TryGetValue(pair.Key, out int nodeDepth))
                    continue;
                string supportId = node.Edges
                    .Where(depth.ContainsKey)
                    .OrderBy(id => depth[id] < nodeDepth ? 0 : 1)
                    .ThenBy(id => depth[id])
                    .ThenBy(id => id, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (string.IsNullOrEmpty(supportId))
                    continue;
                node.SupportId = supportId;
                nodes[supportId].Dependents.Add(pair.Key);
            }
        }

        Dictionary<string, int> BuildCoreDepths()
        {
            var result = new Dictionary<string, int>(
                StringComparer.Ordinal);
            if (!nodes.ContainsKey(GridAssemblyModel.CoreRuntimeId))
                return result;
            var queue = new Queue<string>();
            queue.Enqueue(GridAssemblyModel.CoreRuntimeId);
            result[GridAssemblyModel.CoreRuntimeId] = 0;
            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                foreach (string edge in nodes[current].Edges)
                {
                    if (!nodes.ContainsKey(edge) ||
                        result.ContainsKey(edge))
                        continue;
                    result[edge] = result[current] + 1;
                    queue.Enqueue(edge);
                }
            }
            return result;
        }

        static bool RequiresMountSupport(Node node)
        {
            if (node?.Record?.Definition == null)
                return false;
            GridModuleCategory category =
                node.Record.Definition.Category;
            return category != GridModuleCategory.Core &&
                   category != GridModuleCategory.Structure &&
                   category != GridModuleCategory.Armor;
        }

        HashSet<string> CollectMountDependents(string supportId)
        {
            var result = new HashSet<string>(
                StringComparer.Ordinal);
            if (!nodes.TryGetValue(supportId, out Node support))
                return result;
            var queue = new Queue<string>(support.Dependents);
            while (queue.Count > 0)
            {
                string id = queue.Dequeue();
                if (!result.Add(id) ||
                    !nodes.TryGetValue(id, out Node dependent))
                    continue;
                foreach (string child in dependent.Dependents)
                    queue.Enqueue(child);
            }
            return result;
        }

        public float Integrity(string runtimeId)
        {
            return nodes.TryGetValue(runtimeId, out Node node)
                ? node.Health
                : 0f;
        }

        public float MaximumIntegrity(string runtimeId)
        {
            return nodes.TryGetValue(runtimeId, out Node node)
                ? node.MaximumHealth
                : 0f;
        }

        public bool IsDestroyed(string runtimeId)
        {
            return !nodes.TryGetValue(runtimeId, out Node node) ||
                   node.Destroyed;
        }

        public void ApplyDamage(
            string runtimeId,
            SpaceDamageInfo damage)
        {
            if (!Active ||
                processingDamage ||
                !nodes.TryGetValue(runtimeId, out Node node) ||
                node.Destroyed)
                return;
            node.Health = Mathf.Max(
                0f,
                node.Health - Mathf.Max(0f, damage.amount));
            StructureChanged?.Invoke();
            if (node.Health > 0f)
                return;
            node.Destroyed = true;
            if (runtimeId == GridAssemblyModel.CoreRuntimeId)
            {
                DestroyVehicle();
                return;
            }

            processingDamage = true;
            HashSet<string> removed =
                CollectMountDependents(runtimeId);
            removed.Add(runtimeId);
            foreach (string id in removed)
            {
                if (!nodes.TryGetValue(id, out Node removedNode))
                    continue;
                removedNode.Destroyed = true;
                SpawnBreakup(removedNode);
            }
            model.RemoveIds(removed);
            GridAssemblyValidation validation = model.Validate();
            List<string> disconnected =
                validation.DisconnectedIds.ToList();
            foreach (string id in disconnected)
                if (nodes.TryGetValue(id, out Node detached))
                    SpawnBreakup(detached);
            if (disconnected.Count > 0)
                model.RemoveIds(disconnected);
            processingDamage = false;
            if (rebuildPending)
            {
                rebuildPending = false;
                RebuildGraph();
            }
            else
                RebuildGraph();
            StructureChanged?.Invoke();
            if (initialCpu > 0 &&
                ConnectedCpu() < Mathf.CeilToInt(initialCpu * 0.2f))
                DestroyVehicle();
        }

        void SpawnBreakup(Node node)
        {
            if (node == null || node.View == null)
                return;
            Renderer[] renderers =
                node.View.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                visuals.SpawnBreakup(new Bounds(
                    node.View.transform.position,
                    Vector3.one));
                return;
            }
            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
                bounds.Encapsulate(renderers[index].bounds);
            visuals.SpawnBreakup(bounds);
        }

        int ConnectedCpu()
        {
            if (!nodes.ContainsKey(
                    GridAssemblyModel.CoreRuntimeId))
                return 0;
            var visited = new HashSet<string>(
                StringComparer.Ordinal);
            var queue = new Queue<string>();
            queue.Enqueue(GridAssemblyModel.CoreRuntimeId);
            visited.Add(GridAssemblyModel.CoreRuntimeId);
            int result = 0;
            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                Node node = nodes[current];
                if (!node.Destroyed)
                    result += node.Cpu;
                foreach (string edge in node.Edges)
                    if (nodes.ContainsKey(edge) &&
                        !nodes[edge].Destroyed &&
                        visited.Add(edge))
                        queue.Enqueue(edge);
            }
            return result;
        }

        void DestroyVehicle()
        {
            if (vehicleDestroyed)
                return;
            vehicleDestroyed = true;
            foreach (Node node in nodes.Values)
                SpawnBreakup(node);
            Destroyed?.Invoke();
            StructureChanged?.Invoke();
            if (automaticReturnToBuild)
                StartCoroutine(ReturnToBuild());
        }

        IEnumerator ReturnToBuild()
        {
            yield return new WaitForSeconds(0.9f);
            if (flightBlueprint != null)
                model.RestoreBlueprint(
                    flightBlueprint,
                    out string ignored);
            flight?.ExitFlight();
        }

        public bool TryGetTarget(out Collider collider)
        {
            collider = null;
            if (!Active)
                return false;
            List<Node> available = nodes.Values
                .Where(item => !item.Destroyed &&
                               item.View != null)
                .ToList();
            if (available.Count == 0)
                return false;
            Node selected = available[
                UnityEngine.Random.Range(0, available.Count)];
            collider = selected.View
                .GetComponentsInChildren<Collider>(true)
                .FirstOrDefault(item => item.enabled);
            return collider != null;
        }

        void OnDestroy()
        {
            if (presenter != null)
                presenter.Rebuilt -= HandlePresenterRebuilt;
        }
    }

    public sealed class WeaponAirCombatAi : MonoBehaviour
    {
        GridFlightBridge flight;
        VehicleStructureGraph player;
        WeaponVisualPool visuals;
        Transform playerRoot;
        Vector3 anchor;
        float nextShot;

        public void Initialize(
            GridFlightBridge flightBridge,
            VehicleStructureGraph playerGraph,
            WeaponVisualPool effectPool,
            Transform playerTransform)
        {
            flight = flightBridge;
            player = playerGraph;
            visuals = effectPool;
            playerRoot = playerTransform;
            anchor = transform.position;
        }

        void Update()
        {
            if (flight == null || !flight.IsFlying ||
                player == null || !player.Active ||
                playerRoot == null)
                return;
            if (Vector3.Distance(transform.position, playerRoot.position) >
                220f)
                anchor = playerRoot.position +
                         playerRoot.forward * 75f +
                         Vector3.up * 12f;
            transform.position = anchor +
                new Vector3(
                    Mathf.Sin(Time.time * 0.25f) * 18f,
                    Mathf.Sin(Time.time * 0.4f) * 5f,
                    Mathf.Cos(Time.time * 0.25f) * 18f);
            if (Time.time < nextShot ||
                !player.TryGetTarget(out Collider target))
                return;
            nextShot = Time.time + 0.65f;
            Vector3 origin = transform.position +
                             transform.forward * 2f;
            Vector3 direction =
                (target.bounds.center - origin).normalized;
            WeaponProfile profile =
                WeaponProfileLibrary.Resolve(null);
            profile.damage = 18f;
            profile.range = 500f;
            WeaponDamageUtility.Trace(
                origin,
                direction,
                profile.range,
                transform,
                gameObject,
                profile,
                visuals,
                out RaycastHit hit);
            Vector3 end = hit.collider != null
                ? hit.point
                : origin + direction * profile.range;
            visuals.SpawnTracer(
                origin,
                end,
                new Color(1f, 0.25f, 0.12f),
                0.08f,
                "sfx/mc/machinegun_bullet_shoot.sfx");
        }
    }
}
