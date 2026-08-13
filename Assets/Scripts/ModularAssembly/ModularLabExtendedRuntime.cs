using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace UnityPlanet.ModularAssembly
{
    public sealed class NeoXBehaviorModule : MonoBehaviour
    {
        [SerializeField] private string sourceId;
        [SerializeField] private GridModuleBehaviorKind behaviorKind;
        [SerializeField] private int weaponGroup = 1;
        [SerializeField] private float strength = 1f;
        [SerializeField] private float energyCapacity;
        [SerializeField] private float idleEnergy;
        [SerializeField] private string appearanceId;
        [SerializeField] private Vector3 mountNormalLocal = Vector3.forward;
        [SerializeField] private Vector3 functionalForwardLocal = Vector3.forward;
        [SerializeField] private Vector3 exhaustAxisLocal = Vector3.forward;
        [SerializeField] private string muzzleSocket;
        [SerializeField] private string exhaustSocket;
        private Transform logicalRoot;
        private bool hasCachedMuzzleFallback;
        private bool hasCachedExhaustFallback;
        private bool hasCachedOppositeExhaustFallback;
        private Vector3 cachedMuzzleFallbackLocal;
        private Vector3 cachedExhaustFallbackLocal;
        private Vector3 cachedOppositeExhaustFallbackLocal;

        public string SourceId => sourceId;
        public GridModuleBehaviorKind BehaviorKind => behaviorKind;
        public int WeaponGroup => weaponGroup;
        public float Strength => Mathf.Max(0.01f, strength);
        public float EnergyCapacity => Mathf.Max(0f, energyCapacity);
        public float IdleEnergy => Mathf.Max(0f, idleEnergy);
        public string AppearanceId => appearanceId;
        public Vector3 WorldMountNormal =>
            Root.TransformDirection(mountNormalLocal).normalized;
        public Vector3 WorldMuzzleDirection =>
            Root.TransformDirection(functionalForwardLocal).normalized;
        public Vector3 WorldExhaustDirection =>
            Root.TransformDirection(exhaustAxisLocal).normalized;
        public Vector3 WorldMuzzlePosition =>
            ResolveSocketPosition(
                muzzleSocket,
                WorldMuzzleDirection,
                ref hasCachedMuzzleFallback,
                ref cachedMuzzleFallbackLocal);
        public Vector3 WorldExhaustPosition =>
            ResolveSocketPosition(
                exhaustSocket,
                WorldExhaustDirection,
                ref hasCachedExhaustFallback,
                ref cachedExhaustFallbackLocal);
        public Vector3 WorldOppositeExhaustPosition =>
            ResolveCachedSurfacePosition(
                -WorldExhaustDirection,
                ref hasCachedOppositeExhaustFallback,
                ref cachedOppositeExhaustFallbackLocal);

        private Vector3 ResolveWorldSurfacePosition(Vector3 direction)
        {
            Vector3 normalizedDirection = direction.sqrMagnitude > 0.001f
                ? direction.normalized
                : Root.forward;
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            Bounds combined = default;
            bool hasBounds = false;
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (renderer == null || renderer is ParticleSystemRenderer
                    || !IsFinite(renderer.transform.position)
                    || !IsFinite(renderer.transform.lossyScale))
                {
                    continue;
                }

                Bounds current = renderer.bounds;
                if (!IsFinite(current)
                    || current.extents.sqrMagnitude > 65536f)
                {
                    continue;
                }
                if (!hasBounds)
                {
                    combined = current;
                    hasBounds = true;
                }
                else
                {
                    combined.Encapsulate(current);
                }
            }

            if (!hasBounds || !IsFinite(combined))
            {
                return Root.position + normalizedDirection * 0.5f;
            }

            float radius =
                Mathf.Abs(Vector3.Dot(normalizedDirection, Vector3.right))
                * combined.extents.x
                + Mathf.Abs(Vector3.Dot(normalizedDirection, Vector3.up))
                * combined.extents.y
                + Mathf.Abs(Vector3.Dot(normalizedDirection, Vector3.forward))
                * combined.extents.z;
            Vector3 resolved = combined.center
                               + normalizedDirection * Mathf.Max(0.05f, radius);
            return IsFinite(resolved)
                ? resolved
                : Root.position + normalizedDirection * 0.5f;
        }
        private Transform Root => logicalRoot != null ? logicalRoot : transform;

        public void Configure(ModularContentRecord record)
        {
            sourceId = record?.sourceId ?? string.Empty;
            behaviorKind = record?.BehaviorKind ?? GridModuleBehaviorKind.None;
            weaponGroup = Mathf.Clamp(weaponGroup, 1, 4);
            appearanceId = record != null && record.appearanceIds != null && record.appearanceIds.Length > 0
                ? record.appearanceIds[0]
                : string.Empty;
            mountNormalLocal = ReadAxis(record?.mountNormalLocal, Vector3.forward);
            functionalForwardLocal = ReadAxis(record?.functionalForwardLocal, Vector3.forward);
            exhaustAxisLocal = ReadAxis(record?.exhaustAxisLocal, Vector3.forward);
            muzzleSocket = record?.muzzleSocket ?? string.Empty;
            exhaustSocket = record?.exhaustSocket ?? string.Empty;
            ClearSocketFallbackCache();
            switch (behaviorKind)
            {
                case GridModuleBehaviorKind.Battery:
                case GridModuleBehaviorKind.Energy:
                    energyCapacity = string.Equals(
                        record.neoXId,
                        "fire_energy_storage_422",
                        StringComparison.OrdinalIgnoreCase) ? 200f : 50f;
                    break;
                case GridModuleBehaviorKind.Shield:
                case GridModuleBehaviorKind.Radar:
                case GridModuleBehaviorKind.Repair:
                    idleEnergy = 3f;
                    break;
            }
        }

        public void SetWeaponGroup(int group)
        {
            weaponGroup = Mathf.Clamp(group, 1, 4);
        }

        public void SetLogicalRoot(Transform value)
        {
            logicalRoot = value;
            ClearSocketFallbackCache();
        }

private Vector3 ResolveSocketPosition(
            string socketName,
            Vector3 direction,
            ref bool hasCachedFallback,
            ref Vector3 cachedFallbackLocal)
        {
            Transform socket = FindChild(transform, socketName);
            if (socket != null && IsFinite(socket.position))
            {
                return socket.position;
            }

            if (hasCachedFallback)
            {
                Vector3 cachedWorld = Root.TransformPoint(cachedFallbackLocal);
                if (IsFinite(cachedWorld))
                {
                    return cachedWorld;
                }
                hasCachedFallback = false;
            }

            Vector3 resolved = ResolveWorldSurfacePosition(direction);
            cachedFallbackLocal = Root.InverseTransformPoint(resolved);
            hasCachedFallback = IsFinite(cachedFallbackLocal);
            return resolved;
        }

        private Vector3 ResolveCachedSurfacePosition(
            Vector3 direction,
            ref bool hasCachedFallback,
            ref Vector3 cachedFallbackLocal)
        {
            if (hasCachedFallback)
            {
                Vector3 cachedWorld = Root.TransformPoint(cachedFallbackLocal);
                if (IsFinite(cachedWorld))
                {
                    return cachedWorld;
                }
                hasCachedFallback = false;
            }

            Vector3 resolved = ResolveWorldSurfacePosition(direction);
            cachedFallbackLocal = Root.InverseTransformPoint(resolved);
            hasCachedFallback = IsFinite(cachedFallbackLocal);
            return resolved;
        }

        private void ClearSocketFallbackCache()
        {
            hasCachedMuzzleFallback = false;
            hasCachedExhaustFallback = false;
            hasCachedOppositeExhaustFallback = false;
            cachedMuzzleFallbackLocal = Vector3.zero;
            cachedExhaustFallbackLocal = Vector3.zero;
            cachedOppositeExhaustFallbackLocal = Vector3.zero;
        }

        private static bool IsFinite(Bounds value)
        {
            return IsFinite(value.center) && IsFinite(value.extents);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y)
                   && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static Transform FindChild(Transform root, string targetName)
        {
            if (root == null || string.IsNullOrWhiteSpace(targetName))
            {
                return null;
            }
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (string.Equals(child.name, targetName, StringComparison.OrdinalIgnoreCase))
                {
                    return child;
                }
            }
            return null;
        }

        private static Vector3 ReadAxis(float[] value, Vector3 fallback)
        {
            if (value == null || value.Length < 3)
            {
                return fallback;
            }
            Vector3 axis = new Vector3(value[0], value[1], value[2]);
            return axis.sqrMagnitude > 0.001f ? axis.normalized : fallback;
        }
    }

    public sealed class RuntimeEnergyBus : MonoBehaviour
    {
        [SerializeField] private float baseCapacity = 100f;
        [SerializeField] private float rechargePerSecond = 12f;
        private NeoXBehaviorModule[] modules = Array.Empty<NeoXBehaviorModule>();

        public float Capacity { get; private set; }
        public float Energy { get; private set; }
        public float Fraction => Capacity > 0f ? Energy / Capacity : 0f;

        private void Awake()
        {
            Rebuild();
        }

        private void Update()
        {
            float idle = modules.Sum(module => module != null ? module.IdleEnergy : 0f);
            Energy = Mathf.Clamp(Energy + (rechargePerSecond - idle) * Time.deltaTime, 0f, Capacity);
        }

        public void Rebuild()
        {
            modules = GetComponentsInChildren<NeoXBehaviorModule>(true);
            Capacity = baseCapacity + modules.Sum(module => module.EnergyCapacity);
            Energy = Capacity;
        }

        public bool TryConsume(float amount, int priority)
        {
            amount = Mathf.Max(0f, amount);
            if (Energy < amount)
            {
                return false;
            }
            Energy -= amount;
            return true;
        }
    }


    public sealed class ModularLabCatalogOverlay : MonoBehaviour
    {
        private ModularContentService contentService;
        private NeoXCatalogIntegration integration;
        private InputField search;
        private Text status;
        private GameObject catalogPanel;
        private readonly List<Button> rows = new List<Button>();
        private int page;

        public void Initialize(ModularContentService service)
        {
            contentService = service;
            BuildUi();
            StartCoroutine(RefreshWhenReady());
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F2))
            {
                ToggleCatalog();
            }
        }

        private IEnumerator RefreshWhenReady()
        {
            while (!contentService.IsReady)
            {
                yield return null;
            }
            integration = gameObject.AddComponent<NeoXCatalogIntegration>();
            integration.Initialize(contentService);
            RefreshRows();
        }

        private void BuildUi()
        {
            Canvas canvas = FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                GameObject canvasObject = new GameObject("NeoXCatalogCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            catalogPanel = new GameObject("NeoXCatalogPanel", typeof(RectTransform), typeof(Image));
            catalogPanel.transform.SetParent(canvas.transform, false);
            RectTransform rect = catalogPanel.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(760f, 640f);
            catalogPanel.GetComponent<Image>().color = new Color(0.025f, 0.055f, 0.075f, 0.97f);

            Text title = CreateText(catalogPanel.transform, new Vector2(16f, -10f), new Vector2(360f, 38f), 22);
            title.text = "NeoX 模块目录";
            CreateButton(catalogPanel.transform, "空战模块", new Vector2(424f, -10f), () => { page = 0; RefreshRows(); });
            search = CreateInput(catalogPanel.transform, new Vector2(16f, -54f), new Vector2(728f, 36f));
            search.placeholder.GetComponent<Text>().text = "搜索中文名或 NeoX ID";
            search.onValueChanged.AddListener(_ => { page = 0; RefreshRows(); });

            for (int index = 0; index < 12; index++)
            {
                GameObject rowObject = CreateButton(
                    catalogPanel.transform,
                    string.Empty,
                    new Vector2(16f, -100f - index * 36f),
                    () => { });
                RectTransform rowRect = rowObject.GetComponent<RectTransform>();
                rowRect.sizeDelta = new Vector2(728f, 32f);
                rows.Add(rowObject.GetComponent<Button>());
            }

            CreateButton(catalogPanel.transform, "上一页", new Vector2(16f, -544f), () => { page = Mathf.Max(0, page - 1); RefreshRows(); });
            CreateButton(catalogPanel.transform, "下一页", new Vector2(176f, -544f), () => { page++; RefreshRows(); });
            CreateButton(catalogPanel.transform, "关闭 F2", new Vector2(594f, -10f), ToggleCatalog);
            status = CreateText(catalogPanel.transform, new Vector2(16f, -588f), new Vector2(728f, 40f), 13);

            GameObject zoneBar = new GameObject("NeoXZoneBar", typeof(RectTransform), typeof(Image));
            zoneBar.transform.SetParent(canvas.transform, false);
            RectTransform zoneRect = zoneBar.GetComponent<RectTransform>();
            zoneRect.anchorMin = zoneRect.anchorMax = new Vector2(0.5f, 0f);
            zoneRect.pivot = new Vector2(0.5f, 0f);
            zoneRect.anchoredPosition = new Vector2(0f, 12f);
            zoneRect.sizeDelta = new Vector2(180f, 48f);
            zoneBar.GetComponent<Image>().color = new Color(0.025f, 0.055f, 0.075f, 0.92f);
            zoneBar.SetActive(false);

            GameObject toggle = CreateButton(canvas.transform, "NeoX目录 F2", Vector2.zero, ToggleCatalog);
            RectTransform toggleRect = toggle.GetComponent<RectTransform>();
            toggleRect.anchorMin = toggleRect.anchorMax = new Vector2(0.5f, 1f);
            toggleRect.pivot = new Vector2(0.5f, 1f);
            toggleRect.anchoredPosition = new Vector2(330f, -58f);
            catalogPanel.SetActive(false);
        }

        private void ToggleCatalog()
        {
            if (catalogPanel != null)
            {
                catalogPanel.SetActive(!catalogPanel.activeSelf);
                if (catalogPanel.activeSelf)
                {
                    RefreshRows();
                }
            }
        }

        private void RefreshRows()
        {
            if (!contentService.IsReady)
            {
                return;
            }
            IReadOnlyList<ModularContentRecord> searched = contentService.Catalog.Search(
                search.text,
                modulesOnly: true,
                take: 64,
                skip: page * 12);
            IReadOnlyList<ModularContentRecord> items = searched
                .Where(IsAirCombatRecord)
                .Take(12)
                .ToArray();
            for (int index = 0; index < rows.Count; index++)
            {
                Button row = rows[index];
                Text rowText = row.GetComponentInChildren<Text>();
                row.onClick.RemoveAllListeners();
                if (index < items.Count)
                {
                    ModularContentRecord record = items[index];
                    rowText.text = $"{record.chineseName}  [{record.neoXId}]    {record.category} / {record.behavior}";
                    row.gameObject.SetActive(true);
                    row.onClick.AddListener(() =>
                    {
                        integration?.Select(record);
                        catalogPanel.SetActive(false);
                    });
                }
                else
                {
                    rowText.text = string.Empty;
                    row.gameObject.SetActive(false);
                }
            }
            status.text = $"空战模块目录 | block 源模型 {contentService.Catalog.ModuleSourceCount} | 第 {page + 1} 页";
            if (!string.IsNullOrEmpty(contentService.LastError))
            {
                status.text += "\n" + contentService.LastError;
            }
        }

        private static bool IsAirCombatRecord(ModularContentRecord record)
        {
            GridModuleBehaviorKind kind = record.BehaviorKind;
            return kind != GridModuleBehaviorKind.Wheel &&
                   kind != GridModuleBehaviorKind.Track &&
                   kind != GridModuleBehaviorKind.Leg &&
                   record.IsGridPlaceable &&
                   record.IsModule;
        }

        private static InputField CreateInput(Transform parent, Vector2 position, Vector2 size)
        {
            GameObject root = new GameObject("Search", typeof(RectTransform), typeof(Image), typeof(InputField));
            root.transform.SetParent(parent, false);
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            root.GetComponent<Image>().color = new Color(0.1f, 0.16f, 0.19f, 1f);
            Text text = CreateText(root.transform, new Vector2(8f, -5f), new Vector2(size.x - 16f, size.y - 8f), 15);
            Text placeholder = CreateText(root.transform, new Vector2(8f, -5f), new Vector2(size.x - 16f, size.y - 8f), 15);
            placeholder.color = new Color(0.65f, 0.7f, 0.72f, 1f);
            InputField input = root.GetComponent<InputField>();
            input.textComponent = text;
            input.placeholder = placeholder;
            return input;
        }

        private static Text CreateText(Transform parent, Vector2 position, Vector2 size, int fontSize)
        {
            GameObject root = new GameObject("Text", typeof(RectTransform), typeof(Text));
            root.transform.SetParent(parent, false);
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            Text text = root.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            return text;
        }

        private static GameObject CreateButton(Transform parent, string label, Vector2 position, UnityEngine.Events.UnityAction clicked)
        {
            GameObject root = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            root.transform.SetParent(parent, false);
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(150f, 36f);
            root.GetComponent<Image>().color = new Color(0.1f, 0.42f, 0.55f, 1f);
            root.GetComponent<Button>().onClick.AddListener(clicked);
            Text text = CreateText(root.transform, Vector2.zero, rect.sizeDelta, 15);
            text.alignment = TextAnchor.MiddleCenter;
            text.text = label;
            return root;
        }
    }

    public static class ModularLabExtensionBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!ModularLabSceneProfile.AllowsBuildExperience(scene))
            {
                return;
            }
            if (UnityEngine.Object.FindObjectOfType<ModularContentService>() != null)
            {
                return;
            }

            GameObject root = new GameObject("NeoXModularLabExtension");
            ModularContentService service = root.AddComponent<ModularContentService>();
            ModularLabCatalogOverlay overlay = root.AddComponent<ModularLabCatalogOverlay>();
            if (ModularLabSceneProfile.AllowsPlanetLabFlightEnvironment(scene))
            {
                PlanetLabFlightEnvironmentController environment =
                    root.AddComponent<PlanetLabFlightEnvironmentController>();
                environment.Initialize();
            }
            service.StartCoroutine(service.Initialize());
            overlay.Initialize(service);
        }
    }
}
