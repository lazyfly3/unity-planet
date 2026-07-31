using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ModularAssembly;
using SpacecraftEditor;
using UnityEngine;
using UnityEngine.EventSystems;

namespace UnityPlanet.ModularAssembly
{
    public sealed class NeoXCatalogIntegration : MonoBehaviour
    {
        private const string ModulePrefix = "neox@";
        private readonly Dictionary<string, ModularContentRecord> recordsByModuleId =
            new Dictionary<string, ModularContentRecord>(StringComparer.OrdinalIgnoreCase);

        private ModularContentService contentService;
        private ModularAssemblyLabController controller;
        private GridAssemblyPresenter presenter;
        private LabPropPlacementController propPlacement;

        public void Initialize(ModularContentService service)
        {
            contentService = service;
            controller = FindObjectOfType<ModularAssemblyLabController>();
            presenter = FindObjectsOfType<GridAssemblyPresenter>()
                .FirstOrDefault(candidate => candidate != null && candidate.name == "GridShip") ??
                        FindObjectsOfType<GridAssemblyPresenter>()
                            .FirstOrDefault(candidate => candidate != null && candidate.name != "TargetAssembly");
            propPlacement = gameObject.AddComponent<LabPropPlacementController>();
            propPlacement.Initialize(service, Camera.main);
            RegisterDefinitions();
            if (presenter != null)
            {
                presenter.Rebuilt += UpgradeViews;
                UpgradeViews();
            }
        }

        private void OnDestroy()
        {
            if (presenter != null)
            {
                presenter.Rebuilt -= UpgradeViews;
            }
        }

        public void Select(ModularContentRecord record)
        {
            if (record == null)
            {
                return;
            }
            if (!record.IsModule)
            {
                propPlacement.BeginPlacement(record);
                return;
            }
            if (controller == null)
            {
                controller = FindObjectOfType<ModularAssemblyLabController>();
            }
            if (controller == null)
            {
                return;
            }
            string moduleId = ToModuleId(record);
            if (controller.Model.Definitions.TryGetValue(moduleId, out GridModuleDefinition definition))
            {
                controller.SelectDefinition(definition);
            }
        }

        private void RegisterDefinitions()
        {
            if (controller == null || contentService?.Catalog == null)
            {
                return;
            }
            IDictionary<string, GridModuleDefinition> definitions =
                controller.Model.Definitions as IDictionary<string, GridModuleDefinition>;
            if (definitions == null)
            {
                Debug.LogError("NeoX: GridAssemblyModel definitions are not dynamically writable.");
                return;
            }
            GameObject fallback = definitions.Values
                .FirstOrDefault(definition => definition.Category == GridModuleCategory.Structure)?.Prefab;
            foreach (ModularContentRecord record in contentService.Catalog.Items.Where(item =>
                         item.IsModule &&
                         item.IsBase &&
                         item.IsGridPlaceable &&
                         item.BehaviorKind != GridModuleBehaviorKind.Track &&
                         item.BehaviorKind != GridModuleBehaviorKind.Leg &&
                         (item.BehaviorKind != GridModuleBehaviorKind.Wheel ||
                          AirBuildCatalog.IsPolished(item))))
            {
                string moduleId = ToModuleId(record);
                recordsByModuleId[moduleId] = record;
                if (!definitions.TryGetValue(
                        moduleId,
                        out GridModuleDefinition definition))
                {
                    definition =
                        ScriptableObject.CreateInstance<GridModuleDefinition>();
                    definition.name = moduleId;
                    definition.hideFlags = HideFlags.HideAndDontSave;
                    definitions[moduleId] = definition;
                }
                ModuleStats stats = ModuleStats.For(record);
                definition.Configure(
                    moduleId,
                    $"{record.chineseName} [{record.neoXId}]",
                    stats.category,
                    fallback,
                    ToFootprint(record),
                    stats.mass,
                    stats.energyCapacity,
                    stats.energyCost,
                    stats.integrity,
                    stats.thrust,
                    null);
            }
        }

        private void UpgradeViews()
        {
            if (presenter == null)
            {
                return;
            }
            foreach (KeyValuePair<string, GridModuleView> pair in presenter.Views)
            {
                GridModuleView view = pair.Value;
                string moduleId = view?.Record?.Definition?.ModuleId;
                if (view == null || string.IsNullOrEmpty(moduleId) ||
                    !moduleId.StartsWith(ModulePrefix, StringComparison.OrdinalIgnoreCase) ||
                    !recordsByModuleId.TryGetValue(
                        moduleId,
                        out ModularContentRecord record))
                {
                    continue;
                }
                NeoXBehaviorModule behavior =
                    view.GetComponent<NeoXBehaviorModule>();
                bool requiresVisualUpgrade = behavior == null;
                behavior = behavior ??
                           view.gameObject.AddComponent<NeoXBehaviorModule>();
                behavior.Configure(record);
                if (record.BehaviorKind == GridModuleBehaviorKind.Wheel)
                {
                    ConfigureWheelElements(view, record);
                }
                Rigidbody vehicleBody = view.GetComponentInParent<Rigidbody>();
                if (vehicleBody != null)
                {
                    NeoXUtilityController utilities = vehicleBody.GetComponent<NeoXUtilityController>() ??
                                                      vehicleBody.gameObject.AddComponent<NeoXUtilityController>();
                    utilities.Rebuild();
                }
                if (requiresVisualUpgrade)
                {
                    StartCoroutine(
                        UpgradeView(pair.Key, view, record));
                }
                else
                {
                    view.GetComponentInParent<
                        RobocraftMotionCoordinator>()?.Rebuild();
                }
            }
        }

        private static ModularWheelRuntime ConfigureWheelElements(
            GridModuleView view,
            ModularContentRecord record)
        {
            WheelTyreGeometry[] tyres;
            bool hasAuthoredGeometry =
                WheelModuleGeometryCatalog.TryResolve(
                    record?.neoXId,
                    out tyres) &&
                tyres.Length > 0;
            int tyreCount = hasAuthoredGeometry
                ? tyres.Length
                : 1;
            string behaviorSettings =
                view.Record?.BehaviorSettings;
            ModularWheelRuntime primary =
                view.GetComponent<ModularWheelRuntime>() ??
                view.gameObject.AddComponent<ModularWheelRuntime>();
            primary.enabled = true;
            primary.ConfigureTyreElement(
                record,
                behaviorSettings,
                0,
                view.transform);
            ConfigureWheelDust(primary, hasAuthoredGeometry);

            var activeElements = new HashSet<Transform>();
            for (int index = 1; index < tyreCount; index++)
            {
                string elementName =
                    "WheelTyreElement_" + index;
                Transform elementRoot =
                    view.transform.Find(elementName);
                if (elementRoot == null)
                {
                    elementRoot =
                        new GameObject(elementName).transform;
                    elementRoot.SetParent(view.transform, false);
                }
                elementRoot.gameObject.SetActive(true);
                elementRoot.localPosition = Vector3.zero;
                elementRoot.localRotation = Quaternion.identity;
                elementRoot.localScale = Vector3.one;
                activeElements.Add(elementRoot);

                ModularWheelRuntime element =
                    elementRoot.GetComponent<ModularWheelRuntime>() ??
                    elementRoot.gameObject.AddComponent<
                        ModularWheelRuntime>();
                element.enabled = true;
                element.ConfigureTyreElement(
                    record,
                    behaviorSettings,
                    index,
                    view.transform);
                ConfigureWheelDust(element, true);
            }

            for (int index = view.transform.childCount - 1;
                 index >= 0;
                 index--)
            {
                Transform child = view.transform.GetChild(index);
                if (child == null ||
                    !child.name.StartsWith(
                        "WheelTyreElement_",
                        StringComparison.Ordinal) ||
                    activeElements.Contains(child))
                {
                    continue;
                }
                foreach (ModularWheelRuntime stale in
                         child.GetComponents<ModularWheelRuntime>())
                {
                    stale.enabled = false;
                }
                child.gameObject.SetActive(false);
            }
            return primary;
        }

        private static void ConfigureWheelDust(
            ModularWheelRuntime wheel,
            bool supportedSemanticWheel)
        {
            WheelDustVisualRuntime dust =
                wheel.GetComponent<WheelDustVisualRuntime>();
            if (!supportedSemanticWheel)
            {
                if (dust != null)
                {
                    dust.Bind(null);
                    dust.enabled = false;
                }
                return;
            }

            dust = dust ??
                   wheel.gameObject.AddComponent<
                       WheelDustVisualRuntime>();
            dust.enabled = true;
            // ConfigureTyreElement has already supplied the exact authored
            // centre, radius, width and dual-tyre count at this point.
            dust.Bind(wheel);
        }

        private IEnumerator UpgradeView(string runtimeId, GridModuleView view, ModularContentRecord record)
        {
            GameObject loaded = null;
            yield return contentService.InstantiateAsync(record, view.transform, value => loaded = value);
            if (view == null || loaded == null)
            {
                yield break;
            }
            foreach (Collider collider in loaded.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
            }
            foreach (Renderer renderer in view.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.transform.IsChildOf(loaded.transform) &&
                    !IsWheelDustRenderer(renderer))
                {
                    renderer.enabled = false;
                }
            }
            ModularWheelRuntime primaryWheel =
                view.GetComponent<ModularWheelRuntime>();
            primaryWheel?.BindVisual(loaded.transform);
            if (WheelModuleGeometryCatalog.TryResolve(
                    record.neoXId,
                    out WheelTyreGeometry[] tyres) &&
                tyres.Length > 0)
            {
                WheelCarrierCollisionRuntime carrier =
                    view.GetComponent<WheelCarrierCollisionRuntime>() ??
                    view.gameObject.AddComponent<
                        WheelCarrierCollisionRuntime>();
                carrier.BindMotionRoot(
                    primaryWheel != null
                        ? primaryWheel.VisualMotionRoot
                        : view.transform);
                carrier.Configure(record.neoXId);
            }
            view.GetComponentInParent<RobocraftMotionCoordinator>()?.Rebuild();
            view.GetComponentInParent<NeoXUtilityController>()?.Rebuild();
        }

        private static bool IsWheelDustRenderer(Renderer renderer)
        {
            if (renderer == null)
                return false;

            WheelDustVisualRuntime owner =
                renderer.GetComponentInParent<
                    WheelDustVisualRuntime>();
            Transform effectRoot = owner?.EffectRoot;
            return effectRoot != null &&
                   (renderer.transform == effectRoot ||
                    renderer.transform.IsChildOf(effectRoot));
        }

        private static string ToModuleId(ModularContentRecord record)
        {
            return ModulePrefix + record.sourceId.Replace("@", "_");
        }

        private static Vector3Int ToFootprint(ModularContentRecord record)
        {
            int[] size = record.footprint;
            return size != null && size.Length >= 3
                ? new Vector3Int(Mathf.Max(1, size[0]), Mathf.Max(1, size[1]), Mathf.Max(1, size[2]))
                : Vector3Int.one;
        }

        private struct ModuleStats
        {
            public GridModuleCategory category;
            public float mass;
            public float energyCapacity;
            public float energyCost;
            public float integrity;
            public float thrust;

            public static ModuleStats For(ModularContentRecord record)
            {
                GridModuleBehaviorKind behavior = record.BehaviorKind;
                ModuleStats value = new ModuleStats
                {
                    category = GridModuleCategory.Structure,
                    mass = 75f,
                    integrity = 140f
                };
                if (behavior == GridModuleBehaviorKind.Thruster)
                {
                    value.category = GridModuleCategory.MainThruster;
                    value.mass = 180f;
                    // RC3 propulsion is governed by installed thrust, mass,
                    // atmosphere and actuator authority. It is not a consumer
                    // of the weapon/active-function construction energy pool.
                    value.energyCost = 0f;
                    value.thrust = 6000f;
                }
                else if (behavior == GridModuleBehaviorKind.Battery || behavior == GridModuleBehaviorKind.Energy)
                {
                    value.category = GridModuleCategory.Battery;
                    value.mass = 120f;
                    value.energyCapacity = string.Equals(
                        record.neoXId,
                        "fire_energy_storage_422",
                        StringComparison.OrdinalIgnoreCase) ? 200f : 50f;
                    value.integrity = 100f;
                }
                else if (behavior >= GridModuleBehaviorKind.KineticRapid && behavior <= GridModuleBehaviorKind.Saw)
                {
                    value.category = GridModuleCategory.KineticWeapon;
                    value.mass = 220f;
                    value.energyCost = behavior == GridModuleBehaviorKind.Laser ||
                                       behavior == GridModuleBehaviorKind.EnergyCannon ? 25f : 5f;
                }
                else if (behavior == GridModuleBehaviorKind.Shield)
                {
                    value.category = GridModuleCategory.Armor;
                    value.mass = 160f;
                    value.energyCost = 12f;
                    value.integrity = 320f;
                }
                else if (behavior == GridModuleBehaviorKind.Wheel)
                {
                    WheelModuleProfile profile =
                        WheelModuleProfile.ForNeoXId(record.neoXId);
                    value.category = GridModuleCategory.Mobility;
                    value.mass = profile.massKg;
                    value.energyCost = 0f;
                    value.thrust = 0f;
                    value.integrity = 140f;
                }
                else if (behavior == GridModuleBehaviorKind.Track ||
                          behavior == GridModuleBehaviorKind.Leg ||
                          behavior == GridModuleBehaviorKind.Hover)
                {
                    value.category = GridModuleCategory.RcsThruster;
                    value.mass = 110f;
                    value.energyCost = 0f;
                    value.thrust = 1500f;
                }
                return value;
            }
        }
    }

    public sealed class LabPropPlacementController : MonoBehaviour
    {
        private readonly List<GameObject> placed = new List<GameObject>();
        private readonly ModularBlueprintStore blueprintStore = new ModularBlueprintStore();
        private ModularContentService contentService;
        private Camera worldCamera;
        private ModularContentRecord pending;
        private GameObject preview;
        private float yaw;
        private float scale = 1f;

        public void Initialize(ModularContentService service, Camera camera)
        {
            contentService = service;
            worldCamera = camera;
            LoadLayout();
        }

        public void BeginPlacement(ModularContentRecord record)
        {
            CancelPlacement();
            pending = record;
            StartCoroutine(contentService.InstantiateAsync(record, null, value =>
            {
                preview = value;
                if (preview != null)
                {
                    SetPreviewState(preview, true);
                }
            }));
        }

        private void Update()
        {
            if (preview == null || pending == null || worldCamera == null)
            {
                return;
            }
            if (Input.GetMouseButtonDown(1))
            {
                CancelPlacement();
                return;
            }
            if (Input.GetKeyDown(KeyCode.R))
            {
                yaw += 90f;
            }
            if (Input.GetKeyDown(KeyCode.LeftBracket))
            {
                scale = Mathf.Max(0.25f, scale - 0.25f);
            }
            if (Input.GetKeyDown(KeyCode.RightBracket))
            {
                scale = Mathf.Min(8f, scale + 0.25f);
            }
            Ray ray = worldCamera.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 2000f, ~0, QueryTriggerInteraction.Ignore))
            {
                preview.transform.position = hit.point;
                preview.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                preview.transform.localScale = Vector3.one * scale;
            }
            if (Input.GetMouseButtonDown(0) &&
                (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()))
            {
                SetPreviewState(preview, false);
                placed.Add(preview);
                preview = null;
                pending = null;
                SaveLayout();
            }
        }

        private void CancelPlacement()
        {
            if (preview != null)
            {
                Destroy(preview);
            }
            preview = null;
            pending = null;
            yaw = 0f;
            scale = 1f;
        }

        private static void SetPreviewState(GameObject target, bool isPreview)
        {
            foreach (Collider collider in target.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = !isPreview;
            }
            foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in renderer.materials)
                {
                    if (isPreview)
                    {
                        material.color = Color.Lerp(material.color, new Color(0.15f, 0.85f, 1f), 0.45f);
                    }
                }
            }
        }

        private void LoadLayout()
        {
            string slot = blueprintStore.ActiveSlotId;
            if (string.IsNullOrEmpty(slot))
            {
                return;
            }
            string spacecraft = GalaxySaveSlotService.GetSpacecraftDirectory(slot);
            string galaxyDirectory = Directory.GetParent(spacecraft)?.FullName ?? spacecraft;
            if (!LabLayoutStore.TryLoad(galaxyDirectory, out LabLayoutData data, out string error))
            {
                Debug.LogWarning("NeoX 道具布局载入失败: " + error);
            }
            foreach (LabPropPose pose in data.props)
            {
                if (contentService.Catalog.TryGet(pose.sourceId, out ModularContentRecord record))
                {
                    StartCoroutine(contentService.InstantiateAsync(record, null, value =>
                    {
                        if (value == null)
                        {
                            return;
                        }
                        value.transform.position = pose.position;
                        value.transform.eulerAngles = pose.eulerAngles;
                        value.transform.localScale = pose.scale;
                        placed.Add(value);
                    }));
                }
            }
        }

        private void SaveLayout()
        {
            string slot = blueprintStore.ActiveSlotId;
            if (string.IsNullOrEmpty(slot))
            {
                return;
            }
            string spacecraft = GalaxySaveSlotService.GetSpacecraftDirectory(slot);
            string galaxyDirectory = Directory.GetParent(spacecraft)?.FullName ?? spacecraft;
            LabLayoutData data = new LabLayoutData
            {
                props = placed.Where(value => value != null).Take(LabLayoutStore.PropLimit).Select(value =>
                {
                    NeoXBehaviorModule module = value.GetComponent<NeoXBehaviorModule>();
                    return new LabPropPose
                    {
                        sourceId = module != null ? module.SourceId : value.name,
                        position = value.transform.position,
                        eulerAngles = value.transform.eulerAngles,
                        scale = value.transform.localScale
                    };
                }).ToArray()
            };
            if (!LabLayoutStore.Save(galaxyDirectory, data, out string error))
            {
                Debug.LogWarning("NeoX 道具布局保存失败: " + error);
            }
        }
    }

    public sealed class NeoXProjectileDamage : MonoBehaviour
    {
        public float damage = 35f;
        public float explosionRadius;
        public GameObject source;

        private void OnCollisionEnter(Collision collision)
        {
            Apply(collision.collider, collision.GetContact(0).point, GetComponent<Rigidbody>()?.velocity ?? Vector3.zero);
            if (explosionRadius > 0f)
            {
                foreach (Collider target in Physics.OverlapSphere(transform.position, explosionRadius))
                {
                    Apply(target, transform.position, (target.transform.position - transform.position).normalized * damage);
                }
            }
            Destroy(gameObject);
        }

        private void Apply(Collider target, Vector3 point, Vector3 impulse)
        {
            ISpaceDamageable damageable = target.GetComponentInParent<ISpaceDamageable>();
            if (damageable != null)
            {
                damageable.ApplyDamage(new SpaceDamageInfo(
                    damage,
                    point,
                    impulse,
                    explosionRadius > 0f ? SpaceDamageType.Explosion : SpaceDamageType.Projectile,
                    source));
            }
            Rigidbody body = target.attachedRigidbody;
            if (damageable == null)
                VehicleExternalForces.ApplyImpulse(
                    body,
                    impulse,
                    point);
        }
    }

    public sealed class NeoXUtilityController : MonoBehaviour
    {
        private Rigidbody body;
        private RuntimeEnergyBus energy;
        private NeoXBehaviorModule[] modules = Array.Empty<NeoXBehaviorModule>();
        private NeoXShieldBubble shield;
        private float nextUtility;
        private readonly List<GameObject> drones = new List<GameObject>();

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            energy = GetComponent<RuntimeEnergyBus>() ?? gameObject.AddComponent<RuntimeEnergyBus>();
            Rebuild();
        }

        public void Rebuild()
        {
            modules = GetComponentsInChildren<NeoXBehaviorModule>(true);
            int shields = modules.Count(module =>
                module != null &&
                module.BehaviorKind == GridModuleBehaviorKind.Shield);
            if (shields > 0)
            {
                shield = GetComponent<NeoXShieldBubble>() ?? gameObject.AddComponent<NeoXShieldBubble>();
                shield.Configure(energy, 150f * shields);
            }
            else if (shield != null)
            {
                Destroy(shield);
                shield = null;
            }
            energy?.Rebuild();
        }

        private void Update()
        {
            Repair();
            ApplyMelee();
            if (Input.GetMouseButtonDown(1) && Time.time >= nextUtility)
            {
                nextUtility = Time.time + 2f;
                ActivateUtility();
            }
        }

        private void Repair()
        {
            int repairers = modules.Count(module =>
                module != null &&
                module.BehaviorKind == GridModuleBehaviorKind.Repair);
            if (repairers == 0 || energy == null || !energy.TryConsume(repairers * Time.deltaTime * 2f, 3))
            {
                return;
            }
            foreach (SpacecraftDamageReceiver receiver in GetComponentsInChildren<SpacecraftDamageReceiver>())
            {
                if (!receiver.IsDestroyed && receiver.Integrity < receiver.MaximumIntegrity)
                {
                    receiver.SetIntegrity(Mathf.Min(receiver.MaximumIntegrity, receiver.Integrity + repairers * 8f * Time.deltaTime));
                }
            }
        }

        private void ApplyMelee()
        {
            if (!Input.GetMouseButton(0))
            {
                return;
            }
            foreach (NeoXBehaviorModule module in modules)
            {
                if (module == null)
                {
                    continue;
                }
                if (module.BehaviorKind != GridModuleBehaviorKind.Drill &&
                    module.BehaviorKind != GridModuleBehaviorKind.Saw)
                {
                    continue;
                }
                if (Physics.SphereCast(
                        module.transform.position,
                        0.65f,
                        module.transform.forward,
                        out RaycastHit hit,
                        1.5f,
                        ~0,
                        QueryTriggerInteraction.Ignore))
                {
                    ISpaceDamageable damageable = hit.collider.GetComponentInParent<ISpaceDamageable>();
                    damageable?.ApplyDamage(new SpaceDamageInfo(
                        45f * Time.deltaTime,
                        hit.point,
                        module.transform.forward * 25f,
                        SpaceDamageType.Collision,
                        gameObject));
                }
            }
        }

        private void ActivateUtility()
        {
            if (modules.Any(module =>
                    module != null &&
                    module.BehaviorKind == GridModuleBehaviorKind.EMP) &&
                energy.TryConsume(18f, 3))
            {
                foreach (Collider target in Physics.OverlapSphere(transform.position, 30f))
                {
                    Rigidbody targetBody = target.attachedRigidbody;
                    if (targetBody == null || targetBody == body)
                    {
                        continue;
                    }
                    NeoXEmpStatus status = targetBody.GetComponent<NeoXEmpStatus>() ??
                                           targetBody.gameObject.AddComponent<NeoXEmpStatus>();
                    status.DisableFor(4f);
                }
            }
            if (modules.Any(module =>
                    module != null &&
                    module.BehaviorKind == GridModuleBehaviorKind.ForceField) &&
                energy.TryConsume(12f, 3))
            {
                foreach (Collider target in Physics.OverlapSphere(transform.position, 25f))
                {
                    Rigidbody targetBody = target.attachedRigidbody;
                    if (targetBody == null || targetBody == body)
                    {
                        continue;
                    }
                    Vector3 delta = targetBody.worldCenterOfMass - body.worldCenterOfMass;
                    float factor = 1f - Mathf.Clamp01(delta.magnitude / 25f);
                    VehicleExternalForces.ApplyImpulse(
                        targetBody,
                        delta.normalized * 8000f * factor,
                        targetBody.worldCenterOfMass);
                }
            }
            int droneModules = modules.Count(module =>
                module != null &&
                module.BehaviorKind == GridModuleBehaviorKind.Drone);
            if (droneModules > 0 && drones.Count(value => value != null) < Mathf.Min(4, droneModules) &&
                energy.TryConsume(20f, 3))
            {
                SpawnDrone();
            }
        }

        private void SpawnDrone()
        {
            GameObject drone = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            drone.name = "NeoXSupportDrone";
            drone.transform.position = transform.position + transform.right * (3f + drones.Count);
            drone.transform.localScale = Vector3.one * 0.75f;
            Rigidbody droneBody = drone.AddComponent<Rigidbody>();
            droneBody.useGravity = false;
            droneBody.mass = 20f;
            NeoXDroneController controller = drone.AddComponent<NeoXDroneController>();
            controller.Configure(transform, gameObject);
            drones.Add(drone);
        }
    }

    public sealed class NeoXShieldBubble : MonoBehaviour, ISpaceDamageable
    {
        private RuntimeEnergyBus energy;
        private float maximum = 100f;
        private float integrity = 100f;
        public float Integrity => integrity;
        public float MaximumIntegrity => maximum;
        public bool IsDestroyed => integrity <= 0f;

        public void Configure(RuntimeEnergyBus bus, float capacity)
        {
            energy = bus;
            maximum = Mathf.Max(1f, capacity);
            integrity = Mathf.Min(maximum, Mathf.Max(integrity, maximum * 0.5f));
        }

        private void Update()
        {
            if (energy != null && integrity < maximum && energy.TryConsume(2f * Time.deltaTime, 2))
            {
                integrity = Mathf.Min(maximum, integrity + 10f * Time.deltaTime);
            }
        }

        public void ApplyDamage(SpaceDamageInfo info)
        {
            float absorbed = Mathf.Min(integrity, Mathf.Max(0f, info.amount));
            float energyCost = absorbed * 0.08f;
            if (energy == null || energy.TryConsume(energyCost, 2))
            {
                integrity -= absorbed;
            }
        }
    }

    public sealed class NeoXEmpStatus : MonoBehaviour
    {
        private float until;
        private RobocraftMotionCoordinator motion;
        private SpacecraftIfcsMotor ifcs;
        private bool restoreMotion;
        private bool restoreIfcs;
        private bool restoreIfcsControls;

        public void DisableFor(float seconds)
        {
            until = Mathf.Max(until, Time.time + seconds);
            motion = GetComponent<RobocraftMotionCoordinator>();
            ifcs = GetComponent<SpacecraftIfcsMotor>();
            if (motion != null)
            {
                restoreMotion = motion.IsActive;
                motion.SetDamageDisabled(true);
            }
            if (ifcs != null)
            {
                restoreIfcs = ifcs.enabled;
                restoreIfcsControls = ifcs.ControlsEnabled;
                ifcs.enabled = false;
                ifcs.ControlsEnabled = false;
            }
        }

        private void Update()
        {
            if (Time.time < until)
            {
                return;
            }
            if (motion != null && restoreMotion)
            {
                motion.SetDamageDisabled(false);
            }
            if (ifcs != null)
            {
                ifcs.enabled = restoreIfcs;
                ifcs.ControlsEnabled =
                    restoreIfcs && restoreIfcsControls;
            }
            Destroy(this);
        }
    }

    public sealed class NeoXDroneController : MonoBehaviour
    {
        private Transform owner;
        private GameObject source;
        private Rigidbody body;
        private float nextShot;

        public void Configure(Transform follow, GameObject damageSource)
        {
            owner = follow;
            source = damageSource;
            body = GetComponent<Rigidbody>();
            Destroy(gameObject, 45f);
        }

        private void FixedUpdate()
        {
            if (owner == null || body == null)
            {
                return;
            }
            Vector3 targetPosition = owner.position + owner.right * 4f + owner.up * 2f;
            body.AddForce((targetPosition - transform.position) * 12f - body.velocity * 5f, ForceMode.Acceleration);
            Rigidbody target = FindObjectsOfType<Rigidbody>()
                .Where(candidate => candidate != body && candidate.transform.root != owner.root)
                .OrderBy(candidate => (candidate.position - transform.position).sqrMagnitude)
                .FirstOrDefault();
            if (target != null && Time.time >= nextShot && (target.position - transform.position).sqrMagnitude < 80f * 80f)
            {
                nextShot = Time.time + 0.8f;
                if (Physics.Raycast(transform.position, (target.position - transform.position).normalized, out RaycastHit hit, 80f))
                {
                    ISpaceDamageable damageable = hit.collider.GetComponentInParent<ISpaceDamageable>();
                    damageable?.ApplyDamage(new SpaceDamageInfo(
                        12f,
                        hit.point,
                        (target.position - transform.position).normalized * 8f,
                        SpaceDamageType.Projectile,
                        source));
                }
            }
        }
    }
}
