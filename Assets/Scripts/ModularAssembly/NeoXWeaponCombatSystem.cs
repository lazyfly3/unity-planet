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
        IGridFlightSession flight;
        GridAssemblyModel model;
        GridTargetController target;
        Camera sceneCamera;
        GridLabCameraController cameraController;
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
        bool controlsEnabled = true;
        bool drawHud = true;
        string status = string.Empty;

        public int ActiveGroup => activeGroup;
        public bool IsAiming => flight != null &&
                                flight.IsFlying &&
                                controlsEnabled &&
                                Input.GetMouseButton(1);
        public VehicleStructureGraph StructureGraph => structureGraph;
        public IReadOnlyList<WeaponRuntime> Weapons => weapons;
        public Transform LockedTarget => lockedTarget;
        public RuntimeEnergyBus EnergyBus => energy;
        public bool MuzzleBlocked => muzzleBlocked;
        public bool ControlsEnabled
        {
            get => controlsEnabled;
            set
            {
                controlsEnabled = value;
                if (!value)
                {
                    lockedTarget = null;
                    aimCandidate = null;
                    aimCandidateSeconds = 0f;
                    RestoreFov();
                }
            }
        }

        public void SetHudVisible(bool value)
        {
            drawHud = value;
        }

        public void Initialize(
            GridAssemblyPresenter assemblyPresenter,
            IGridFlightSession flightBridge,
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
            this.cameraController = cameraController;
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
            structureGraph.Initialize(model, presenter, flight);
            VehicleDamageFeedbackPresenter damageFeedback =
                GetComponent<VehicleDamageFeedbackPresenter>() ??
                gameObject.AddComponent<VehicleDamageFeedbackPresenter>();
            damageFeedback.Initialize(
                structureGraph,
                sceneCamera,
                transform,
                cameraController);
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

            CombatWeaponBudgetController budget =
                GetComponent<CombatWeaponBudgetController>() ??
                gameObject.AddComponent<CombatWeaponBudgetController>();
            budget.Bind(this);
            budget.RefreshNow();
        }

        public void PrewarmCombatResources()
        {
            if (visuals != null)
                visuals.Prewarm();
            if (projectiles != null)
                projectiles.Prewarm(24);
        }

        void Update()
        {
            if (flight == null || !flight.IsFlying || !controlsEnabled)
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
                        0.11f,
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
            bool sniper = weapons.Any(item =>
                item.Group == activeGroup &&
                item.Profile.sourceId.IndexOf(
                    "snipercannon_422",
                    StringComparison.OrdinalIgnoreCase) >= 0);
            if (cameraController != null)
            {
                cameraController.SetAimPresentation(aiming, sniper);
                return;
            }
            if (sceneCamera == null)
                return;
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
            if (cameraController != null)
            {
                cameraController.SetAimPresentation(false, false);
                return;
            }
            if (sceneCamera != null)
                sceneCamera.fieldOfView = Mathf.Lerp(
                    sceneCamera.fieldOfView,
                    baseFov,
                    1f - Mathf.Exp(-Time.unscaledDeltaTime * 9f));
        }

        void OnGUI()
        {
            if (!drawHud)
                return;
            if (model != null && (flight == null || !flight.IsFlying))
            {
                int cpu = ModuleCpuBudget.Total(model.Records);
                GUI.Box(
                    new Rect(
                        Screen.width * 0.5f - 98f,
                        24f,
                        196f,
                        34f),
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
                    (ownerRoot != null &&
                     hit.collider.transform.IsChildOf(ownerRoot)) ||
                    VehicleCombatTeamUtility.AreFriendly(
                        ownerRoot,
                        hit.collider.transform) ||
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
            if (collider == null ||
                VehicleCombatTeamUtility.AreFriendly(
                    source,
                    collider.transform))
                return;
            ISpaceDamageable damageable =
                FindDamageable(collider.transform);
            if (damageable == null)
                return;
            float integrityBefore = damageable.Integrity;
            bool destroyedBefore = damageable.IsDestroyed;
            VehicleCombatTeam sourceTeam = source != null
                ? VehicleCombatTeamUtility.Resolve(source.transform)
                : VehicleCombatTeam.Neutral;
            VehicleCombatTeam targetTeam =
                VehicleCombatTeamUtility.Resolve(collider.transform);
            damageable.ApplyDamage(new SpaceDamageInfo(
                damage,
                point,
                direction.normalized * Mathf.Clamp(
                    damage * 0.5f,
                    5f,
                    160f),
                SpaceDamageType.Projectile,
                source));
            ReportAppliedDamage(
                sourceTeam,
                targetTeam,
                point,
                damage,
                integrityBefore,
                damageable,
                destroyedBefore);
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
                    (ownerRoot != null &&
                     collider.transform.IsChildOf(ownerRoot)) ||
                    VehicleCombatTeamUtility.AreFriendly(
                        source,
                        collider.transform))
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
                float integrityBefore = target.Integrity;
                bool destroyedBefore = target.IsDestroyed;
                VehicleCombatTeam sourceTeam = source != null
                    ? VehicleCombatTeamUtility.Resolve(source.transform)
                    : VehicleCombatTeam.Neutral;
                VehicleCombatTeam targetTeam =
                    VehicleCombatTeamUtility.Resolve(collider.transform);
                float appliedDamage = damage * falloff;
                target.ApplyDamage(new SpaceDamageInfo(
                    appliedDamage,
                    closest,
                    direction * appliedDamage,
                    SpaceDamageType.Explosion,
                    source));
                ReportAppliedDamage(
                    sourceTeam,
                    targetTeam,
                    closest,
                    appliedDamage,
                    integrityBefore,
                    target,
                    destroyedBefore);
            }
        }

        static void ReportAppliedDamage(
            VehicleCombatTeam sourceTeam,
            VehicleCombatTeam targetTeam,
            Vector3 point,
            float requestedDamage,
            float integrityBefore,
            ISpaceDamageable target,
            bool destroyedBefore)
        {
            if (target == null)
                return;
            if (target is UnityEngine.Object unityTarget &&
                unityTarget == null)
            {
                CombatDamageFeedbackBus.Report(
                    new CombatDamageAppliedFeedback(
                        sourceTeam,
                        targetTeam,
                        point,
                        requestedDamage,
                        !destroyedBefore));
                return;
            }
            float integrityAfter = target.Integrity;
            bool destroyed = !destroyedBefore && target.IsDestroyed;
            float applied = Mathf.Clamp(
                integrityBefore - integrityAfter,
                0f,
                Mathf.Max(0f, requestedDamage));
            if (applied <= 0.0001f && !destroyed)
                return;
            CombatDamageFeedbackBus.Report(new CombatDamageAppliedFeedback(
                sourceTeam,
                targetTeam,
                point,
                applied,
                destroyed));
        }
    }

    public sealed class WeaponVisualPool : MonoBehaviour
    {
        sealed class LineSlot
        {
            public GameObject Root;
            public LineRenderer Line;
            public LineRenderer Halo;
            public Vector3 Start;
            public Vector3 End;
            public Color Color;
            public float CoreWidth;
            public float HaloWidth;
            public float SegmentFraction;
            public float StartedAt;
            public float EndsAt;
        }

        sealed class LaserSlot
        {
            public GameObject Root;
            public LineRenderer Line;
            public Transform HitRoot;
            public ParticleSystem[] Particles;
            public MaterialPropertyBlock Properties;
            public float EndsAt;
        }

        readonly List<LineSlot> lines = new List<LineSlot>();
        readonly List<LaserSlot> lasers = new List<LaserSlot>();
        readonly List<ParticleSystem> particles =
            new List<ParticleSystem>();
        Material lineMaterial;
        Material particleMaterial;
        GameObject hovlLaserPrefab;
        CombatWeaponEffectPool combatWeaponEffects;
        bool laserValidationErrorLogged;
        bool fallbackMaterialErrorLogged;

        void Awake()
        {
            Shader unlit = ResolveRuntimeUnlitShader();
            if (unlit != null)
            {
                lineMaterial = new Material(unlit)
                {
                    name = "RuntimeWeaponFallbackLine"
                };
                particleMaterial = new Material(unlit)
                {
                    name = "RuntimeWeaponFallbackParticle"
                };
            }
            else
            {
                LogFallbackMaterialError();
            }
            hovlLaserPrefab = Resources.Load<GameObject>(
                "WeaponEffects/HovlLaserRay");
            CombatTransientRoot.EnsureCameraDepthTexture();
            if (hovlLaserPrefab != null &&
                !HasRenderableLaserMaterials(hovlLaserPrefab))
            {
                LogLaserValidationError();
                hovlLaserPrefab = null;
            }
            EnsureCombatWeaponEffects();
        }

        void EnsureCombatWeaponEffects()
        {
            if (combatWeaponEffects != null)
                return;
            Transform transientRoot = CombatTransientRoot.GetOrCreate();
            combatWeaponEffects =
                transientRoot.GetComponent<CombatWeaponEffectPool>() ??
                transientRoot.gameObject
                    .AddComponent<CombatWeaponEffectPool>();
            combatWeaponEffects.Prewarm();
        }

        static Shader ResolveRuntimeUnlitShader()
        {
            string[] candidates =
            {
                "Sprites/Default",
                "Particles/Standard Unlit",
                "Unlit/Color"
            };
            foreach (string name in candidates)
            {
                Shader shader = Shader.Find(name);
                if (shader != null &&
                    shader.isSupported &&
                    shader.name.IndexOf(
                        "InternalErrorShader",
                        StringComparison.OrdinalIgnoreCase) < 0)
                    return shader;
            }
            return null;
        }

        void LogFallbackMaterialError()
        {
            if (fallbackMaterialErrorLogged)
                return;
            fallbackMaterialErrorLogged = true;
            Debug.LogError(
                "[Combat VFX] No supported Built-in unlit shader is " +
                "available for the emergency weapon visual fallback.");
        }

        public void Prewarm()
        {
            RemoveDestroyedSlots();
            EnsureCombatWeaponEffects();
            LineSlot line = AcquireLine();
            if (line != null && line.Root != null)
                line.Root.SetActive(false);
            LaserSlot laser = AcquireLaser();
            if (laser != null && laser.Root != null)
                laser.Root.SetActive(false);

            Resources.Load<GameObject>("WeaponEffects/HovlLaserRay");
            Resources.Load<GameObject>("WeaponEffects/Forge3DProjectile");
            Resources.Load<GameObject>("WeaponEffects/Forge3DExplosion");
        }

        void Update()
        {
            RemoveDestroyedSlots();
            foreach (LineSlot slot in lines)
            {
                if (!slot.Root.activeSelf)
                    continue;
                if (Time.time >= slot.EndsAt)
                {
                    slot.Line.enabled = false;
                    slot.Halo.enabled = false;
                    slot.Root.SetActive(false);
                    continue;
                }
                UpdateTracer(slot);
            }
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
            if (slot == null)
            {
                LogFallbackMaterialError();
                return;
            }
            slot.Root.name = "Tracer_" +
                             ShortEffectName(sourceEffect);
            slot.Root.SetActive(true);
            slot.Line.enabled = true;
            slot.Halo.enabled = true;
            float distance = Vector3.Distance(start, end);
            slot.Start = start;
            slot.End = end;
            slot.Color = color;
            slot.CoreWidth = Mathf.Clamp(
                0.11f + distance * 0.00045f,
                0.11f,
                0.32f);
            slot.HaloWidth = Mathf.Clamp(
                slot.CoreWidth * 2.75f,
                0.28f,
                0.82f);
            slot.SegmentFraction = distance <= 20f
                ? 1f
                : Mathf.Clamp(26f / distance, 0.13f, 0.62f);
            slot.StartedAt = Time.time;
            slot.EndsAt = Time.time + Mathf.Max(0.11f, lifetime);
            UpdateTracer(slot);
        }

        static void UpdateTracer(LineSlot slot)
        {
            float duration = Mathf.Max(
                0.01f,
                slot.EndsAt - slot.StartedAt);
            float progress = Mathf.Clamp01(
                (Time.time - slot.StartedAt) / duration);
            float travel = progress * (1f - slot.SegmentFraction);
            Vector3 visibleStart = Vector3.Lerp(
                slot.Start,
                slot.End,
                travel);
            Vector3 visibleEnd = Vector3.Lerp(
                slot.Start,
                slot.End,
                Mathf.Min(1f, travel + slot.SegmentFraction));
            float fade = 1f - Mathf.SmoothStep(0.58f, 1f, progress);

            slot.Line.SetPosition(0, visibleStart);
            slot.Line.SetPosition(1, visibleEnd);
            Color core = Color.Lerp(slot.Color, Color.white, 0.72f);
            core.a = Mathf.Clamp01(slot.Color.a) * fade;
            slot.Line.startColor = core;
            slot.Line.endColor = new Color(
                core.r,
                core.g,
                core.b,
                core.a * 0.48f);
            slot.Line.startWidth = slot.CoreWidth;
            slot.Line.endWidth = slot.CoreWidth * 0.52f;

            slot.Halo.SetPosition(0, visibleStart);
            slot.Halo.SetPosition(1, visibleEnd);
            Color halo = slot.Color;
            halo.a = Mathf.Clamp01(slot.Color.a) * 0.28f * fade;
            slot.Halo.startColor = halo;
            slot.Halo.endColor = new Color(
                halo.r,
                halo.g,
                halo.b,
                halo.a * 0.18f);
            slot.Halo.startWidth = slot.HaloWidth;
            slot.Halo.endWidth = slot.HaloWidth * 0.58f;
        }

        public void SpawnContinuousLaser(
            Vector3 start,
            Vector3 end,
            Vector3 hitNormal,
            bool hasHit,
            float lifetime)
        {
            CombatTransientRoot.EnsureCameraDepthTexture();
            LaserSlot slot = AcquireLaser();
            if (slot == null)
            {
                SpawnTracer(
                    start,
                    end,
                    new Color(0.22f, 0.9f, 1f),
                    lifetime,
                    "HovlLaserRayFallback");
                if (hasHit && combatWeaponEffects != null)
                    combatWeaponEffects.SpawnImpact(
                        end,
                        hitNormal,
                        new Color(0.22f, 0.9f, 1f),
                        "HovlLaserRayHit",
                        0f);
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
                0.14f + delta.magnitude * 0.0002f,
                0.14f,
                0.32f);
            slot.Line.startColor = new Color(0.72f, 0.98f, 1f, 1f);
            slot.Line.endColor = new Color(0.08f, 0.68f, 1f, 0.92f);
            slot.Line.startWidth = beamWidth;
            slot.Line.endWidth = beamWidth * 0.72f;
            slot.Line.enabled = true;
            slot.Line.useWorldSpace = true;
            slot.Line.positionCount = 2;
            slot.Line.SetPosition(0, start);
            slot.Line.SetPosition(1, end);
            Material beamMaterial = slot.Line.sharedMaterial;
            float distance = delta.magnitude;
            MaterialPropertyBlock properties =
                slot.Properties ?? new MaterialPropertyBlock();
            slot.Properties = properties;
            slot.Line.GetPropertyBlock(properties);
            if (beamMaterial != null &&
                beamMaterial.HasProperty("_MainTex"))
            {
                Vector2 offset =
                    beamMaterial.GetTextureOffset("_MainTex");
                properties.SetVector(
                    "_MainTex_ST",
                    new Vector4(
                        Mathf.Max(1f, distance),
                        1f,
                        offset.x,
                        offset.y));
            }
            if (beamMaterial != null &&
                beamMaterial.HasProperty("_Noise"))
            {
                Vector2 offset =
                    beamMaterial.GetTextureOffset("_Noise");
                properties.SetVector(
                    "_Noise_ST",
                    new Vector4(
                        Mathf.Max(1f, distance),
                        1f,
                        offset.x,
                        offset.y));
            }
            slot.Line.SetPropertyBlock(properties);

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
            EnsureCombatWeaponEffects();
            if (combatWeaponEffects != null &&
                combatWeaponEffects.SpawnMuzzle(
                    position,
                    direction,
                    color,
                    sourceEffect,
                    weaponRoot))
                return;
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
            EnsureCombatWeaponEffects();
            if (string.Equals(
                    sourceEffect,
                    "HovlLaserRayHit",
                    StringComparison.Ordinal))
                return;
            if (combatWeaponEffects != null &&
                combatWeaponEffects.SpawnImpact(
                    position,
                    normal,
                    color,
                    sourceEffect,
                    radius))
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
            if (NeoXCombatFeedbackRuntime.TrySpawnModuleBreak(
                    bounds.center,
                    Vector3.up,
                    bounds.size.magnitude))
                return;
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
            if (system == null)
            {
                LogFallbackMaterialError();
                return;
            }
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
            RemoveDestroyedSlots();
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
            if (line == null ||
                !HasRenderableLaserMaterials(root))
            {
                LogLaserValidationError();
                Destroy(root);
                hovlLaserPrefab = null;
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
                    root.GetComponentsInChildren<ParticleSystem>(true),
                Properties = new MaterialPropertyBlock()
            };
            root.SetActive(false);
            lasers.Add(slot);
            return slot;
        }

        static bool HasRenderableLaserMaterials(GameObject root)
        {
            if (root == null)
                return false;
            LineRenderer line = root.GetComponent<LineRenderer>();
            if (line == null || !IsRenderableMaterial(line.sharedMaterial))
                return false;

            ParticleSystemRenderer[] renderers =
                root.GetComponentsInChildren<ParticleSystemRenderer>(true);
            if (renderers.Length == 0)
                return false;
            foreach (ParticleSystemRenderer renderer in renderers)
            {
                bool hasMain =
                    IsRenderableMaterial(renderer.sharedMaterial);
                ParticleSystem particle =
                    renderer.GetComponent<ParticleSystem>();
                bool hasTrail =
                    particle != null &&
                    particle.trails.enabled &&
                    IsRenderableMaterial(renderer.trailMaterial);
                if (!hasMain && !hasTrail)
                    return false;
            }
            return true;
        }

        static bool IsRenderableMaterial(Material material)
        {
            if (material == null ||
                material.shader == null ||
                !material.shader.isSupported)
                return false;
            return material.shader.name.IndexOf(
                       "InternalErrorShader",
                       StringComparison.OrdinalIgnoreCase) < 0;
        }

        void LogLaserValidationError()
        {
            if (laserValidationErrorLogged)
                return;
            laserValidationErrorLogged = true;
            Debug.LogError(
                "[Combat VFX] HovlLaserRay has no complete renderable " +
                "Built-in material set. Falling back to the native beam " +
                "and energy-impact visuals.");
        }

        LineSlot AcquireLine()
        {
            RemoveDestroyedSlots();
            if (!IsRenderableMaterial(lineMaterial))
                return null;
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
            line.alignment = LineAlignment.View;
            line.numCapVertices = 2;
            line.numCornerVertices = 2;
            GameObject haloRoot = new GameObject("TracerHalo");
            haloRoot.transform.SetParent(root.transform, false);
            LineRenderer halo = haloRoot.AddComponent<LineRenderer>();
            halo.sharedMaterial = lineMaterial;
            halo.positionCount = 2;
            halo.useWorldSpace = true;
            halo.textureMode = LineTextureMode.Stretch;
            halo.alignment = LineAlignment.View;
            halo.numCapVertices = 2;
            halo.numCornerVertices = 2;
            root.SetActive(false);
            slot = new LineSlot
            {
                Root = root,
                Line = line,
                Halo = halo
            };
            lines.Add(slot);
            return slot;
        }

        ParticleSystem AcquireParticles()
        {
            RemoveDestroyedSlots();
            if (!IsRenderableMaterial(particleMaterial))
                return null;
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
            renderer.renderMode =
                ParticleSystemRenderMode.Stretch;
            renderer.alignment =
                ParticleSystemRenderSpace.Velocity;
            renderer.velocityScale = 0.08f;
            renderer.lengthScale = 2f;
            particles.Add(system);
            return system;
        }

        public void Clear()
        {
            RemoveDestroyedSlots();
            foreach (LineSlot slot in lines)
            {
                if (slot?.Root == null)
                    continue;
                if (slot.Line != null)
                    slot.Line.enabled = false;
                if (slot.Halo != null)
                    slot.Halo.enabled = false;
                slot.Root.SetActive(false);
                slot.StartedAt = 0f;
                slot.EndsAt = 0f;
            }
            foreach (LaserSlot slot in lasers)
            {
                if (slot?.Root == null)
                    continue;
                foreach (ParticleSystem particle in slot.Particles)
                {
                    if (particle == null)
                        continue;
                    particle.Stop(
                        true,
                        ParticleSystemStopBehavior.StopEmittingAndClear);
                    particle.Clear(true);
                }
                if (slot.Line != null)
                    slot.Line.enabled = false;
                slot.Root.SetActive(false);
                slot.EndsAt = 0f;
            }
            foreach (ParticleSystem particle in particles)
            {
                if (particle == null)
                    continue;
                particle.Stop(
                    true,
                    ParticleSystemStopBehavior.StopEmittingAndClear);
                particle.Clear(true);
            }
        }

        void OnDisable()
        {
            Clear();
        }

        void OnDestroy()
        {
            DestroyOwnedVisuals();
            DestroyRuntimeMaterial(lineMaterial);
            DestroyRuntimeMaterial(particleMaterial);
            lineMaterial = null;
            particleMaterial = null;
        }

        void DestroyOwnedVisuals()
        {
            foreach (LineSlot slot in lines)
                DestroyRuntimeObject(slot?.Root);
            foreach (LaserSlot slot in lasers)
                DestroyRuntimeObject(slot?.Root);
            foreach (ParticleSystem particle in particles)
                DestroyRuntimeObject(
                    particle == null ? null : particle.gameObject);
            lines.Clear();
            lasers.Clear();
            particles.Clear();
        }

        void RemoveDestroyedSlots()
        {
            lines.RemoveAll(item =>
                item == null ||
                item.Root == null ||
                item.Line == null ||
                item.Halo == null);
            lasers.RemoveAll(item =>
                item == null ||
                item.Root == null ||
                item.Line == null);
            particles.RemoveAll(item => item == null);
        }

        static void DestroyRuntimeObject(GameObject value)
        {
            if (value == null)
                return;
            value.SetActive(false);
            if (Application.isPlaying)
                Destroy(value);
            else
                DestroyImmediate(value);
        }

        static void DestroyRuntimeMaterial(Material material)
        {
            if (material == null)
                return;
            if (Application.isPlaying)
                Destroy(material);
            else
                DestroyImmediate(material);
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

        public void Prewarm(int count)
        {
            int targetCount = Mathf.Clamp(count, 0, 64);
            while (projectiles.Count < targetCount)
            {
                WeaponProjectile projectile = CreateProjectile();
                projectile.gameObject.SetActive(false);
            }
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
                projectile = CreateProjectile();
            projectile.Launch(
                position,
                direction,
                inheritedVelocity,
                profile,
                owner,
                lockedTarget,
                visuals);
        }

        WeaponProjectile CreateProjectile()
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
            WeaponProjectile projectile =
                root.AddComponent<WeaponProjectile>();
            projectile.Initialize(this);
            projectiles.Add(projectile);
            return projectile;
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
        static readonly HashSet<string> InvalidFlightVisualErrors =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetValidationErrors()
        {
            InvalidFlightVisualErrors.Clear();
        }

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

            // A pooled projectile may have used a non-Forge profile between
            // two energy shots, which intentionally leaves this cached child
            // inactive. Reactivate the candidate before evaluating only the
            // renderers that would actually be visible.
            flightVisual.SetActive(true);
            if (!Forge3DEffectPool.HasRenderableRendererSet(flightVisual))
            {
                if (InvalidFlightVisualErrors.Add(resource))
                {
                    Debug.LogError(
                        "[Combat VFX] Projectile visual '" + resource +
                        "' contains an active renderer with a missing or " +
                        "unsupported material. The native projectile body " +
                        "remains enabled.");
                }
                RejectFlightVisual();
                return;
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

        void RejectFlightVisual()
        {
            if (flightVisual != null)
            {
                flightVisual.SetActive(false);
                if (Application.isPlaying)
                    Destroy(flightVisual);
                else
                    DestroyImmediate(flightVisual);
            }
            flightVisual = null;
            flightVisualResource = null;
            flightVisualBaseScale = Vector3.one;
            flightVisualParticles = Array.Empty<ParticleSystem>();
            if (bodyRenderer != null)
                bodyRenderer.enabled = true;
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
                    VehicleCombatTeamUtility.AreFriendly(
                        owner,
                        hit.collider.transform) ||
                    hit.distance >= nearest)
                    continue;
                nearest = hit.distance;
                selected = hit;
                found = true;
            }
            return found;
        }
    }

    public interface IVehicleModuleDamageAuthority
    {
        float Integrity(string runtimeId);
        float MaximumIntegrity(string runtimeId);
        bool IsDestroyed(string runtimeId);
        void ApplyDamage(string runtimeId, SpaceDamageInfo damage);
    }

    public sealed class VehicleModuleDamageReceiver :
        MonoBehaviour,
        ISpaceDamageable
    {
        IVehicleModuleDamageAuthority authority;
        string runtimeId;

        public float Integrity =>
            authority == null ? 0f : authority.Integrity(runtimeId);
        public float MaximumIntegrity =>
            authority == null ? 0f : authority.MaximumIntegrity(runtimeId);
        public bool IsDestroyed =>
            authority == null || authority.IsDestroyed(runtimeId);

        public void Initialize(
            IVehicleModuleDamageAuthority source,
            string id)
        {
            authority = source;
            runtimeId = id;
        }

        public void ApplyDamage(SpaceDamageInfo damage)
        {
            authority?.ApplyDamage(runtimeId, damage);
        }
    }

    public sealed class VehicleStructureGraph :
        MonoBehaviour,
        IVehicleModuleDamageAuthority
    {
        sealed class Node
        {
            public GridModuleRecord Record;
            public GridModuleView View;
            public float Health;
            public float MaximumHealth;
            public int Cpu;
            public bool Destroyed;
            public readonly HashSet<string> Edges =
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
        readonly HashSet<string> combatDamagedRuntimeIds =
            new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> combatRemovedRuntimeIds =
            new HashSet<string>(StringComparer.Ordinal);
        GridAssemblyModel model;
        GridAssemblyPresenter presenter;
        IGridFlightSession flight;
        ModularBlueprintData flightBlueprint;
        int initialCpu;
        bool active;
        bool processingDamage;
        bool vehicleDestroyed;
        bool automaticReturnToBuild = true;
        bool damageEnabled;

        public bool Active =>
            active && damageEnabled && !vehicleDestroyed;
        public bool IsVehicleDestroyed => vehicleDestroyed;
        public VehicleStructureDelta LastDestructionDelta
        {
            get;
            private set;
        }
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
        public event Action<VehicleStructureDelta> StructureChanged;
        public event Action<VehicleModuleDamageFeedback> ModuleDamaged;
        public event Action Destroyed;

        public void SetAutomaticReturnToBuild(bool value)
        {
            automaticReturnToBuild = value;
        }

        public void SetDamageEnabled(bool value)
        {
            damageEnabled = value;
            if (!value)
                VehicleDetachedDebris.ClearAll();
        }

        public void Initialize(
            GridAssemblyModel assemblyModel,
            GridAssemblyPresenter assemblyPresenter,
            IGridFlightSession flightBridge)
        {
            model = assemblyModel;
            presenter = assemblyPresenter;
            flight = flightBridge;
            presenter.Rebuilt -= HandlePresenterRebuilt;
            presenter.Rebuilt += HandlePresenterRebuilt;
            CombatFeedbackController.GetOrCreate().Observe(this);
            RebuildGraph();
        }

        public void BeginFlight()
        {
            active = true;
            vehicleDestroyed = false;
            LastDestructionDelta = null;
            combatDamagedRuntimeIds.Clear();
            combatRemovedRuntimeIds.Clear();
            flightBlueprint = model.CaptureBlueprint();
            RebuildGraph();
            ResetNodeDamage();
            initialCpu = ConnectedCpu();
            StructureChanged?.Invoke(
                VehicleStructureDelta.Initial(nodes.Keys));
        }

        public void ResetCombatSession()
        {
            active = true;
            vehicleDestroyed = false;
            processingDamage = false;
            LastDestructionDelta = null;
            combatDamagedRuntimeIds.Clear();
            combatRemovedRuntimeIds.Clear();
            flightBlueprint = model.CaptureBlueprint();
            if (nodes.Count == 0)
                RebuildGraph();
            ResetNodeDamage();
            initialCpu = ConnectedCpu();
            StructureChanged?.Invoke(
                VehicleStructureDelta.Initial(nodes.Keys));
        }

        public void PrepareCombatCache()
        {
            if (!active)
                RebuildGraph();
        }

        public void EndFlight()
        {
            active = false;
            processingDamage = false;
            vehicleDestroyed = false;
            LastDestructionDelta = null;
            ResetNodeDamage();
        }

        void ResetNodeDamage()
        {
            foreach (Node node in nodes.Values)
            {
                node.Health = node.MaximumHealth;
                node.Destroyed = false;
            }
        }

        void HandlePresenterRebuilt()
        {
            if (processingDamage)
                return;
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
                    GridModuleDamageReceiver legacyTarget =
                        view.GetComponent<GridModuleDamageReceiver>();
                    if (legacyTarget != null)
                    {
                        legacyTarget.enabled = false;
                        Destroy(legacyTarget);
                    }
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

        public HashSet<string> CaptureUnavailableRuntimeIds()
        {
            var result = new HashSet<string>(
                combatDamagedRuntimeIds,
                StringComparer.Ordinal);
            result.UnionWith(combatRemovedRuntimeIds);
            return result;
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
            float appliedDamage = Mathf.Max(0f, damage.amount);
            if (appliedDamage <= 0f)
                return;
            combatDamagedRuntimeIds.Add(runtimeId);
            node.Health = Mathf.Max(
                0f,
                node.Health - appliedDamage);
            string moduleName = node.Record.Definition.DisplayName;
            if (string.IsNullOrWhiteSpace(moduleName))
                moduleName = runtimeId;
            ModuleDamaged?.Invoke(
                new VehicleModuleDamageFeedback(
                    runtimeId,
                    moduleName,
                    damage.point,
                    damage.impulse,
                    ResolveNodeBounds(node),
                    node.Health /
                    Mathf.Max(1f, node.MaximumHealth),
                    node.Health <= 0f,
                    appliedDamage,
                    node.Record.Definition.Category,
                    runtimeId == GridAssemblyModel.CoreRuntimeId));
            if (node.Health > 0f)
                return;
            node.Destroyed = true;
            SpawnDirectDebris(node, damage);
            if (runtimeId == GridAssemblyModel.CoreRuntimeId)
            {
                DisableGameplay(node);
                if (node.View != null)
                    node.View.gameObject.SetActive(false);
                var coreDelta = new VehicleStructureDelta
                {
                    DirectHitRuntimeId = runtimeId,
                    HitPoint = damage.point,
                    Impulse = damage.impulse,
                    CoreDestroyed = true
                };
                coreDelta.RemovedRuntimeIds.AddRange(nodes.Keys);
                combatRemovedRuntimeIds.UnionWith(
                    coreDelta.RemovedRuntimeIds);
                DestroyVehicle(coreDelta);
                return;
            }

            processingDamage = true;
            HashSet<string> connected = ConnectedAliveToCore();
            var detachedIds = new HashSet<string>(
                nodes.Values
                    .Where(item =>
                        !item.Destroyed &&
                        !connected.Contains(item.Record.RuntimeId))
                    .Select(item => item.Record.RuntimeId),
                StringComparer.Ordinal);
            List<List<Node>> detachedComponents =
                BuildDetachedComponents(detachedIds);
            var removed = new HashSet<string>(
                detachedIds,
                StringComparer.Ordinal)
            {
                runtimeId
            };
            VehicleStructureDelta delta = BuildDelta(
                runtimeId,
                removed,
                detachedComponents,
                damage);
            combatRemovedRuntimeIds.UnionWith(removed);
            Rigidbody sourceBody = GetComponent<Rigidbody>();
            foreach (List<Node> component in detachedComponents)
            {
                List<DetachedDebrisPart> debrisParts = component
                    .Where(item => item.View != null)
                    .Select(item => new DetachedDebrisPart(
                        item.Record.RuntimeId,
                        item.View.gameObject,
                        item.Record.Definition.MassKg))
                    .ToList();
                GameObject debris = VehicleDetachedDebris.Spawn(
                    debrisParts,
                    sourceBody,
                    Vector3.zero,
                    Vector3.zero);
                if (debris != null)
                {
                    Renderer[] debrisRenderers =
                        debris.GetComponentsInChildren<Renderer>(true);
                    Bounds debrisBounds = debrisRenderers.Length > 0
                        ? debrisRenderers[0].bounds
                        : new Bounds(debris.transform.position, Vector3.one);
                    for (int index = 1;
                         index < debrisRenderers.Length;
                         index++)
                        debrisBounds.Encapsulate(
                            debrisRenderers[index].bounds);
                    NeoXCombatFeedbackRuntime.TrySpawnDetached(
                        debrisBounds.center,
                        damage.impulse,
                        debrisBounds.size.magnitude);
                }
                foreach (Node detached in component)
                {
                    detached.Destroyed = true;
                    DisableGameplay(detached);
                }
            }
            DisableGameplay(node);
            model.RemoveIds(removed);
            processingDamage = false;
            RebuildGraph();
            if (initialCpu > 0 &&
                ConnectedCpu() < Mathf.CeilToInt(initialCpu * 0.2f))
                DestroyVehicle(delta);
            else
                StructureChanged?.Invoke(delta);
        }

        void SpawnDirectDebris(Node node, SpaceDamageInfo damage)
        {
            if (node?.View == null)
                return;
            VehicleDetachedDebris.SpawnDirectBreak(
                new[]
                {
                    new DetachedDebrisPart(
                        node.Record.RuntimeId,
                        node.View.gameObject,
                        node.Record.Definition.MassKg)
                },
                GetComponent<Rigidbody>(),
                damage.impulse,
                damage.point);
        }

        HashSet<string> ConnectedAliveToCore()
        {
            var result = new HashSet<string>(
                StringComparer.Ordinal);
            if (!nodes.TryGetValue(
                    GridAssemblyModel.CoreRuntimeId,
                    out Node core) ||
                core.Destroyed)
                return result;
            var queue = new Queue<string>();
            queue.Enqueue(GridAssemblyModel.CoreRuntimeId);
            result.Add(GridAssemblyModel.CoreRuntimeId);
            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                foreach (string adjacent in nodes[current].Edges)
                {
                    if (!nodes.TryGetValue(adjacent, out Node other) ||
                        other.Destroyed ||
                        !result.Add(adjacent))
                        continue;
                    queue.Enqueue(adjacent);
                }
            }
            return result;
        }

        List<List<Node>> BuildDetachedComponents(
            HashSet<string> detachedIds)
        {
            var remaining = new HashSet<string>(
                detachedIds,
                StringComparer.Ordinal);
            var result = new List<List<Node>>();
            while (remaining.Count > 0)
            {
                string seed = remaining.First();
                remaining.Remove(seed);
                var component = new List<Node>();
                var queue = new Queue<string>();
                queue.Enqueue(seed);
                while (queue.Count > 0)
                {
                    string current = queue.Dequeue();
                    if (!nodes.TryGetValue(current, out Node currentNode))
                        continue;
                    component.Add(currentNode);
                    foreach (string adjacent in currentNode.Edges)
                        if (remaining.Remove(adjacent))
                            queue.Enqueue(adjacent);
                }
                if (component.Count > 0)
                    result.Add(component);
            }
            return result;
        }

        VehicleStructureDelta BuildDelta(
            string directHit,
            HashSet<string> removed,
            List<List<Node>> detachedComponents,
            SpaceDamageInfo damage)
        {
            var result = new VehicleStructureDelta
            {
                DirectHitRuntimeId = directHit,
                HitPoint = damage.point,
                Impulse = damage.impulse
            };
            result.RemovedRuntimeIds.AddRange(removed);
            result.RemainingRuntimeIds.AddRange(
                nodes.Keys.Where(id => !removed.Contains(id)));
            Rigidbody sourceBody = GetComponent<Rigidbody>();
            if (nodes.TryGetValue(directHit, out Node directNode))
            {
                result.DirectDestroyedComponent = BuildSnapshot(
                    new[] { directNode },
                    sourceBody,
                    true);
            }
            foreach (List<Node> component in detachedComponents)
                result.DetachedComponents.Add(
                    BuildSnapshot(component, sourceBody, false));
            return result;
        }

        DetachedComponentSnapshot BuildSnapshot(
            IEnumerable<Node> component,
            Rigidbody sourceBody,
            bool directHit)
        {
            var snapshot = new DetachedComponentSnapshot
            {
                IsDirectHit = directHit
            };
            float mass = 0f;
            Vector3 weighted = Vector3.zero;
            Bounds bounds = default;
            bool hasBounds = false;
            foreach (Node item in component)
            {
                float itemMass = Mathf.Max(
                    0.01f,
                    item.Record.Definition.MassKg);
                snapshot.RuntimeIds.Add(item.Record.RuntimeId);
                mass += itemMass;
                if (item.View == null)
                    continue;
                weighted += item.View.transform.position * itemMass;
                foreach (Renderer renderer in
                         item.View.GetComponentsInChildren<Renderer>(true))
                {
                    if (!hasBounds)
                    {
                        bounds = renderer.bounds;
                        hasBounds = true;
                    }
                    else
                        bounds.Encapsulate(renderer.bounds);
                }
            }
            snapshot.MassKg = mass;
            snapshot.WorldCenter = mass > 0.01f
                ? weighted / mass
                : transform.position;
            if (sourceBody != null)
            {
                snapshot.WorldLinearVelocity =
                    sourceBody.GetPointVelocity(snapshot.WorldCenter);
                snapshot.WorldAngularVelocity = sourceBody.angularVelocity;
            }
            Vector3 size = hasBounds ? bounds.size : Vector3.one;
            snapshot.PrincipalInertia = new Vector3(
                mass * (size.y * size.y + size.z * size.z) / 12f,
                mass * (size.x * size.x + size.z * size.z) / 12f,
                mass * (size.x * size.x + size.y * size.y) / 12f);
            return snapshot;
        }

        static void DisableGameplay(Node node)
        {
            if (node?.View == null)
                return;
            foreach (Collider collider in
                     node.View.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            foreach (MonoBehaviour behaviour in
                     node.View.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour != null &&
                    !(behaviour is GridModuleView))
                    behaviour.enabled = false;
        }

        static Bounds ResolveNodeBounds(Node node)
        {
            if (node?.View == null)
                return new Bounds(Vector3.zero, Vector3.one);
            Renderer[] renderers =
                node.View.GetComponentsInChildren<Renderer>(true);
            bool initialized = false;
            Bounds result = new Bounds(
                node.View.transform.position,
                Vector3.one);
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null ||
                    renderer is ParticleSystemRenderer ||
                    renderer is LineRenderer ||
                    renderer is TrailRenderer ||
                    !renderer.enabled)
                    continue;
                if (!initialized)
                {
                    result = renderer.bounds;
                    initialized = true;
                }
                else
                    result.Encapsulate(renderer.bounds);
            }
            return result;
        }

        public Bounds ResolveVisualBounds()
        {
            bool initialized = false;
            Bounds result = new Bounds(transform.position, Vector3.one);
            foreach (Node node in nodes.Values)
            {
                if (node == null ||
                    node.Destroyed ||
                    node.View == null)
                    continue;
                Bounds nodeBounds = ResolveNodeBounds(node);
                if (!initialized)
                {
                    result = nodeBounds;
                    initialized = true;
                }
                else
                    result.Encapsulate(nodeBounds);
            }
            return result;
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

        void DestroyVehicle(VehicleStructureDelta delta = null)
        {
            if (vehicleDestroyed)
                return;
            vehicleDestroyed = true;
            LastDestructionDelta =
                delta ?? VehicleStructureDelta.Initial(nodes.Keys);
            Destroyed?.Invoke();
            StructureChanged?.Invoke(LastDestructionDelta);
            if (automaticReturnToBuild)
                StartCoroutine(ReturnToBuild());
        }

        IEnumerator ReturnToBuild()
        {
            yield return new WaitForSeconds(0.9f);
            flight?.ExitFlight();
            yield return null;
            if (flightBlueprint != null)
            {
                model.RestoreBlueprint(
                    flightBlueprint,
                    out string ignored);
            }
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
        IGridFlightSession flight;
        VehicleStructureGraph player;
        WeaponVisualPool visuals;
        Transform playerRoot;
        Vector3 anchor;
        float nextShot;

        public void Initialize(
            IGridFlightSession flightBridge,
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
