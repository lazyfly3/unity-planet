using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace UnityPlanet.ModularAssembly
{
    public enum NeoXModuleCategory
    {
        Core,
        Structure,
        Mobility,
        Propulsion,
        Aerodynamic,
        Weapon,
        Defense,
        Energy,
        Utility,
        Decoration
    }

    public enum GridModuleBehaviorKind
    {
        None,
        Core,
        Structure,
        Wheel,
        Track,
        Leg,
        Hover,
        Thruster,
        Wing,
        ControlSurface,
        KineticRapid,
        Gatling,
        Cannon,
        SniperCannon,
        Rocket,
        GuidedMissile,
        Mortar,
        Bomb,
        EnergyCannon,
        Laser,
        Flame,
        Drill,
        Saw,
        Shield,
        Repair,
        EMP,
        Radar,
        Drone,
        ForceField,
        Battery,
        Energy,
        Decoration
    }

    [Serializable]
    public sealed class ModularContentMaterialBinding
    {
        public int slot;
        public string name;
        public string shader;
        public string albedo;
        public string normal;
        public string metallic;
        public string emission;
        public string sourceMtg;
        public string slotConfidence;
    }

    [Serializable]
    public sealed class ModularContentPhysics
    {
        public string boneMatrix;
        public string boneName;
        public int color;
        public float friction;
        public float mass;
        public float[] position = Array.Empty<float>();
        public string rBType;
        public float restitution;
        public float[] size = Array.Empty<float>();
    }

    [Serializable]
    public sealed class ModularContentRelatedFiles
    {
        public string[] textures = Array.Empty<string>();
        public string[] animations = Array.Empty<string>();
        public string[] effects = Array.Empty<string>();
        public string[] materials = Array.Empty<string>();
        public ModularContentMaterialBinding[] materialBindings =
            Array.Empty<ModularContentMaterialBinding>();
    }

    [Serializable]
    public sealed class ModularContentRecord
    {
        public string sourceId;
        public string sourcePath;
        public string neoXId;
        public string chineseName;
        public int officialModelId;
        public string officialModelName;
        public string animatorPath;
        public string contentKind;
        public string ownership;
        public string baseKey;
        public string category;
        public string behavior;
        public int[] footprint = { 1, 1, 1 };
        public bool explicitFootprint;
        public string environment = "All";
        public string bundleAddress;
        public string assetAddress;
        public string[] appearanceIds = Array.Empty<string>();
        public string[] lodSourceIds = Array.Empty<string>();
        public string[] effectSourceIds = Array.Empty<string>();
        public ModularContentRelatedFiles related = new ModularContentRelatedFiles();
        public ModularContentPhysics physics = new ModularContentPhysics();
        public string animationGraphPath;
        public string animationDataPath;
        public string animationDecodeStatus;
        public string[] animationBones = Array.Empty<string>();
        public string[] animationClips = Array.Empty<string>();
        public string pairedNeoXId;
        public float[] wheelAxleLocal = Array.Empty<float>();
        public float[] wheelRollingForwardLocal = Array.Empty<float>();
        public string conversion;
        public bool selectableForAirBuild;
        public int[] mountAnchor = Array.Empty<int>();
        public float[] visualEuler = Array.Empty<float>();
        public float[] visualOffset = Array.Empty<float>();
        public float visualScale = 1f;
        public string mountMode = "Center";
        public string mountProfile = "Center";
        public float[] mountNormalLocal = Array.Empty<float>();
        public float[] functionalForwardLocal = Array.Empty<float>();
        public float[] exhaustAxisLocal = Array.Empty<float>();
        public float mountPlaneOffset;
        public string muzzleSocket;
        public string exhaustSocket;

        public bool IsModule => string.Equals(contentKind, "module", StringComparison.OrdinalIgnoreCase);
        public bool IsBase => string.Equals(ownership, "base", StringComparison.OrdinalIgnoreCase);
        public bool IsGridPlaceable =>
            explicitFootprint ||
            (BehaviorKind != GridModuleBehaviorKind.None &&
             BehaviorKind != GridModuleBehaviorKind.Structure &&
             BehaviorKind != GridModuleBehaviorKind.Decoration);

        public GridModuleBehaviorKind BehaviorKind
        {
            get
            {
                return Enum.TryParse(behavior, true, out GridModuleBehaviorKind value)
                    ? value
                    : GridModuleBehaviorKind.None;
            }
        }
    }

    [Serializable]
    public sealed class ModularContentCatalogData
    {
        public int formatVersion;
        public string generatedUtc;
        public string sourceRoot;
        public int modelCount;
        public int moduleSourceCount;
        public int propSourceCount;
        public ModularContentRecord[] items = Array.Empty<ModularContentRecord>();
    }

    public sealed class ModularContentCatalog
    {
        private readonly ModularContentCatalogData data;
        private readonly Dictionary<string, ModularContentRecord> byId;

        public int Count => data.items?.Length ?? 0;
        public int ModuleSourceCount => data.moduleSourceCount;
        public int PropSourceCount => data.propSourceCount;
        public IReadOnlyList<ModularContentRecord> Items => data.items;

        private ModularContentCatalog(ModularContentCatalogData source)
        {
            data = source ?? new ModularContentCatalogData();
            data.items = data.items ?? Array.Empty<ModularContentRecord>();
            foreach (ModularContentRecord item in data.items)
            {
                AirBuildCatalog.ApplyDefaults(item);
            }
            byId = data.items
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.sourceId))
                .GroupBy(item => item.sourceId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        }

        public static ModularContentCatalog FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new ModularContentCatalog(new ModularContentCatalogData());
            }

            ModularContentCatalogData parsed = JsonUtility.FromJson<ModularContentCatalogData>(json);
            return new ModularContentCatalog(parsed);
        }

        public bool TryGet(string sourceId, out ModularContentRecord record)
        {
            return byId.TryGetValue(sourceId ?? string.Empty, out record);
        }

        public IReadOnlyList<ModularContentRecord> Search(
            string query,
            string category = null,
            string behavior = null,
            string environment = null,
            bool modulesOnly = true,
            int skip = 0,
            int take = 64)
        {
            string term = (query ?? string.Empty).Trim();
            IEnumerable<ModularContentRecord> result = data.items.Where(item => item != null && item.IsBase);
            if (modulesOnly)
            {
                result = result.Where(item => item.IsModule);
            }

            if (!string.IsNullOrWhiteSpace(category) && category != "All")
            {
                result = result.Where(item => string.Equals(item.category, category, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(behavior) && behavior != "All")
            {
                result = result.Where(item => string.Equals(item.behavior, behavior, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(environment) && environment != "All")
            {
                result = result.Where(item =>
                    string.Equals(item.environment, "All", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(item.environment, environment, StringComparison.OrdinalIgnoreCase));
            }

            if (term.Length > 0)
            {
                result = result.Where(item =>
                    Contains(item.chineseName, term) ||
                    Contains(item.neoXId, term) ||
                    Contains(item.sourceId, term));
            }

            return result
                .OrderBy(item => item.category)
                .ThenBy(item => item.chineseName)
                .ThenBy(item => item.neoXId)
                .Skip(Mathf.Max(0, skip))
                .Take(Mathf.Clamp(take, 1, 256))
                .ToArray();
        }

        private static bool Contains(string value, string term)
        {
            return !string.IsNullOrEmpty(value) &&
                   value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    public sealed class ModularContentService : MonoBehaviour
    {
        private static readonly List<AsyncOperation> GlobalAssetRequests =
            new List<AsyncOperation>();
        private readonly Dictionary<string, AssetBundle> bundles =
            new Dictionary<string, AssetBundle>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, GameObject> prefabCache =
            new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, GameObject> preparedVisualCache =
            new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, AssetBundleCreateRequest> loadingBundles =
            new Dictionary<string, AssetBundleCreateRequest>(StringComparer.OrdinalIgnoreCase);
        private int pendingAssetRequests;

        public ModularContentCatalog Catalog { get; private set; }
        public bool IsReady => Catalog != null;
        public bool IsLoadingAssets => pendingAssetRequests > 0 || loadingBundles.Count > 0;
        public static bool HasGlobalPendingAssetRequests
        {
            get
            {
                GlobalAssetRequests.RemoveAll(request => request == null || request.isDone);
                return GlobalAssetRequests.Count > 0;
            }
        }
        public string LastError { get; private set; }

        public IEnumerator Initialize()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "ModularContent", "modular_content_catalog.json");
            string json = null;
            if (path.Contains("://"))
            {
                using (UnityWebRequest request = UnityWebRequest.Get(path))
                {
                    yield return request.SendWebRequest();
                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        json = request.downloadHandler.text;
                    }
                    else
                    {
                        LastError = request.error;
                    }
                }
            }
            else if (File.Exists(path))
            {
                json = File.ReadAllText(path);
            }

            Catalog = ModularContentCatalog.FromJson(json);
            if (Catalog.Count == 0 && string.IsNullOrEmpty(LastError))
            {
                LastError = "模块目录为空，请先在外部研究目录生成NeoX内容目录。";
            }
        }

        public IEnumerator InstantiateAsync(
            ModularContentRecord record,
            Transform parent,
            Action<GameObject> completed)
        {
            if (record == null)
            {
                completed?.Invoke(CreatePlaceholder(null, parent));
                yield break;
            }

            if (TryInstantiatePrepared(record, parent, out GameObject prepared))
            {
                completed?.Invoke(prepared);
                yield break;
            }

            if (!prefabCache.TryGetValue(record.sourceId, out GameObject prefab))
            {
                yield return LoadPrefab(record, value => prefab = value);
                if (prefab != null)
                {
                    prefabCache[record.sourceId] = prefab;
                }
            }

            GameObject instance = prefab != null
                ? Instantiate(prefab, parent)
                : CreatePlaceholder(record, parent);
            instance.name = record.neoXId;
            // A newly instantiated Renderer does not inherit
            // forceRenderingOff from its parent. The authored mesh exists
            // before its asynchronous material requests finish, so without
            // this immediate refresh it can render for several frames in the
            // cyan fallback material even while the presenter is blocked.
            parent?.GetComponentInParent<GridAssemblyPresenter>()
                ?.RefreshPresentationVisibility();
            NeoXBehaviorModule behavior = instance.GetComponent<NeoXBehaviorModule>();
            if (behavior == null)
            {
                behavior = instance.AddComponent<NeoXBehaviorModule>();
            }
            behavior.Configure(record);
            NormalizeVisual(instance, record);
            yield return ApplyRuntimeMaterial(record, instance);
            if (instance == null)
            {
                yield break;
            }
            EnsureBoundsCollider(instance);
            if (prefab != null)
            {
                CachePreparedVisual(record, instance);
            }
            completed?.Invoke(instance);
        }

        public bool TryInstantiatePrepared(
            ModularContentRecord record,
            Transform parent,
            out GameObject instance)
        {
            instance = null;
            if (record == null || string.IsNullOrWhiteSpace(record.sourceId) ||
                !preparedVisualCache.TryGetValue(
                    record.sourceId,
                    out GameObject prepared) ||
                prepared == null)
            {
                return false;
            }

            instance = Instantiate(prepared, parent, false);
            instance.name = record.neoXId;
            instance.SetActive(true);
            NeoXBehaviorModule behavior =
                instance.GetComponent<NeoXBehaviorModule>() ??
                instance.AddComponent<NeoXBehaviorModule>();
            behavior.Configure(record);
            EnsureBoundsCollider(instance);
            return true;
        }

        private void CachePreparedVisual(
            ModularContentRecord record,
            GameObject instance)
        {
            if (record == null || instance == null ||
                string.IsNullOrWhiteSpace(record.sourceId) ||
                preparedVisualCache.TryGetValue(
                    record.sourceId,
                    out GameObject existing) && existing != null)
            {
                return;
            }

            GameObject prepared = Instantiate(instance, transform, false);
            prepared.name = "PreparedVisualCache_" + record.neoXId;
            prepared.SetActive(false);
            preparedVisualCache[record.sourceId] = prepared;
        }

        private IEnumerator ApplyRuntimeMaterial(ModularContentRecord record, GameObject instance)
        {
            if (instance == null)
            {
                yield break;
            }
            string root = ResolveBundleRoot();
            string bundlePath = Path.Combine(root, record.bundleAddress.Replace('/', Path.DirectorySeparatorChar));
            if (!bundles.TryGetValue(bundlePath, out AssetBundle bundle) || bundle == null)
            {
                yield break;
            }
            ModularContentMaterialBinding[] bindings =
                record.related?.materialBindings ?? Array.Empty<ModularContentMaterialBinding>();
            if (bindings.Length == 0)
            {
                string source = record.sourcePath.Replace('\\', '/');
                int extension = source.LastIndexOf(".mesh", StringComparison.OrdinalIgnoreCase);
                string stem = extension >= 0 ? source.Substring(0, extension) : Path.ChangeExtension(source, null);
                bindings = new[]
                {
                    new ModularContentMaterialBinding
                    {
                        slot = 0,
                        name = "Material_0",
                        albedo = stem + "_d.png",
                        normal = stem + "_n.png",
                        metallic = stem + "_m.png",
                        emission = stem + "_d_dzt.png"
                    }
                };
            }

            Dictionary<int, Material> materials = new Dictionary<int, Material>();
            foreach (ModularContentMaterialBinding binding in bindings)
            {
                Texture2D albedo = null;
                Texture2D normal = null;
                Texture2D metallic = null;
                Texture2D emission = null;
                yield return LoadTexture(bundle, TextureAssetAddress(binding.albedo), value => albedo = value);
                if (instance == null)
                {
                    DestroyRuntimeMaterials(materials);
                    yield break;
                }
                yield return LoadTexture(bundle, TextureAssetAddress(binding.normal), value => normal = value);
                if (instance == null)
                {
                    DestroyRuntimeMaterials(materials);
                    yield break;
                }
                yield return LoadTexture(bundle, TextureAssetAddress(binding.metallic), value => metallic = value);
                if (instance == null)
                {
                    DestroyRuntimeMaterials(materials);
                    yield break;
                }
                yield return LoadTexture(bundle, TextureAssetAddress(binding.emission), value => emission = value);
                if (instance == null)
                {
                    DestroyRuntimeMaterials(materials);
                    yield break;
                }
                if (albedo == null && normal == null && metallic == null && emission == null)
                {
                    continue;
                }

                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                Material material = new Material(shader)
                {
                    name = record.neoXId + "_" + (binding.name ?? $"Material_{binding.slot}")
                };
                if (albedo != null)
                {
                    material.SetTexture("_BaseMap", albedo);
                    material.SetTexture("_MainTex", albedo);
                }
                if (normal != null)
                {
                    material.SetTexture("_BumpMap", normal);
                    material.EnableKeyword("_NORMALMAP");
                }
                if (metallic != null)
                {
                    material.SetTexture("_MetallicGlossMap", metallic);
                    material.EnableKeyword("_METALLICSPECGLOSSMAP");
                }
                if (emission != null)
                {
                    material.SetTexture("_EmissionMap", emission);
                    material.SetColor("_EmissionColor", Color.white);
                    material.EnableKeyword("_EMISSION");
                }
                materials[Mathf.Max(0, binding.slot)] = material;
            }

            if (materials.Count == 0)
            {
                yield break;
            }
            if (instance == null)
            {
                DestroyRuntimeMaterials(materials);
                yield break;
            }
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                Material[] slots = renderer.sharedMaterials;
                for (int index = 0; index < slots.Length; index++)
                {
                    if (materials.TryGetValue(index, out Material exact))
                    {
                        slots[index] = exact;
                    }
                    else if (materials.Count == 1)
                    {
                        slots[index] = materials.Values.First();
                    }
                }
                renderer.sharedMaterials = slots;
            }
        }

        private static void DestroyRuntimeMaterials(
            Dictionary<int, Material> materials)
        {
            foreach (Material material in materials.Values)
            {
                if (material != null)
                Destroy(material);
            }
            materials.Clear();
        }

        private IEnumerator LoadTexture(AssetBundle bundle, string address, Action<Texture2D> completed)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                completed(null);
                yield break;
            }
            AssetBundleRequest request = bundle.LoadAssetAsync<Texture2D>(address);
            TrackRequest(request);
            pendingAssetRequests++;
            yield return request;
            pendingAssetRequests = Mathf.Max(0, pendingAssetRequests - 1);
            completed(request.asset as Texture2D);
        }

        private static string TextureAssetAddress(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return string.Empty;
            }
            string address = source.Replace('\\', '/');
            if (address.EndsWith(".ktx", StringComparison.OrdinalIgnoreCase) ||
                address.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
            {
                address = address.Substring(0, address.Length - 4) + ".png";
            }
            return address;
        }

        private static void NormalizeVisual(GameObject instance, ModularContentRecord record)
        {
            AirBuildCatalog.ApplyDefaults(record);
            if (record?.visualEuler != null && record.visualEuler.Length >= 3)
            {
                instance.transform.localRotation *= Quaternion.Euler(
                    record.visualEuler[0],
                    record.visualEuler[1],
                    record.visualEuler[2]);
            }

            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return;
            }

            Transform space = instance.transform.parent != null
                ? instance.transform.parent
                : instance.transform;
            Bounds bounds = BoundsInSpace(renderers, space);
            int[] footprint = record?.footprint;
            Vector3 target = footprint != null && footprint.Length >= 3
                ? new Vector3(Mathf.Max(1, footprint[0]), Mathf.Max(1, footprint[1]), Mathf.Max(1, footprint[2]))
                : Vector3.one;
            Vector3 size = bounds.size;
            float scale = Mathf.Min(
                target.x / Mathf.Max(0.001f, size.x),
                target.y / Mathf.Max(0.001f, size.y),
                target.z / Mathf.Max(0.001f, size.z));
            scale *= record != null ? Mathf.Max(0.01f, record.visualScale) : 1f;
            instance.transform.localScale *= Mathf.Clamp(scale, 0.001f, 1000f);

            Bounds scaled = BoundsInSpace(renderers, space);
            Vector3 offset = record?.visualOffset != null && record.visualOffset.Length >= 3
                ? new Vector3(record.visualOffset[0], record.visualOffset[1], record.visualOffset[2])
                : Vector3.zero;
            Vector3 correction;
            string mountMode = record?.mountMode ?? "Center";
            if (string.Equals(mountMode, "SurfaceBack", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mountMode, "ThrusterInterface", StringComparison.OrdinalIgnoreCase))
            {
                correction = new Vector3(
                    -scaled.center.x,
                    -scaled.center.y,
                    -target.z * 0.5f - scaled.min.z + (record?.mountPlaneOffset ?? 0f));
            }
            else if (string.Equals(mountMode, "WeaponBase", StringComparison.OrdinalIgnoreCase))
            {
                correction = new Vector3(
                    -scaled.center.x,
                    -target.y * 0.5f - scaled.min.y + (record?.mountPlaneOffset ?? 0f),
                    -scaled.center.z);
            }
            else if (string.Equals(mountMode, "WingRootLeft", StringComparison.OrdinalIgnoreCase))
            {
                correction = new Vector3(
                    target.x * 0.5f - scaled.max.x - (record?.mountPlaneOffset ?? 0f),
                    -scaled.center.y,
                    -scaled.center.z);
            }
            else if (string.Equals(mountMode, "WingRootRight", StringComparison.OrdinalIgnoreCase))
            {
                correction = new Vector3(
                    -target.x * 0.5f - scaled.min.x + (record?.mountPlaneOffset ?? 0f),
                    -scaled.center.y,
                    -scaled.center.z);
            }
            else
            {
                correction = -scaled.center;
            }
            instance.transform.localPosition += correction + offset;
        }

        private static Bounds BoundsInSpace(Renderer[] renderers, Transform space)
        {
            bool initialized = false;
            Bounds result = new Bounds();
            Matrix4x4 worldToSpace = space != null
                ? space.worldToLocalMatrix
                : Matrix4x4.identity;
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                // Renderer.bounds is a world-axis-aligned box. Converting its
                // corners back into a rotated module repeats the AABB
                // expansion and makes the measured mesh depend on the
                // vehicle's world rotation. That poisoned the prepared visual
                // cache and made every later copy of the same module too
                // small. Transform the renderer's real local bounds directly
                // into the requested space instead.
                Bounds localBounds = renderer.localBounds;
                Vector3 minimum = localBounds.min;
                Vector3 maximum = localBounds.max;
                Matrix4x4 rendererToSpace =
                    worldToSpace * renderer.transform.localToWorldMatrix;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = new Vector3(
                        (corner & 1) == 0 ? minimum.x : maximum.x,
                        (corner & 2) == 0 ? minimum.y : maximum.y,
                        (corner & 4) == 0 ? minimum.z : maximum.z);
                    Vector3 local = rendererToSpace.MultiplyPoint3x4(point);
                    if (!initialized)
                    {
                        result = new Bounds(local, Vector3.zero);
                        initialized = true;
                    }
                    else
                    {
                        result.Encapsulate(local);
                    }
                }
            }
            return result;
        }

        private static void EnsureBoundsCollider(GameObject instance)
        {
            if (instance.GetComponentInChildren<Collider>() != null)
            {
                return;
            }
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return;
            }
            Bounds bounds = BoundsInSpace(renderers, instance.transform);
            BoxCollider collider = instance.AddComponent<BoxCollider>();
            collider.center = bounds.center;
            collider.size = bounds.size;
        }

        public void UnloadUnused()
        {
            // AssetBundles are shared by the build view, thumbnail worker, core
            // replacer and flight bridge. Unloading from one service invalidates
            // the same global AssetBundle object still used by the others.
            bundles.Clear();
            prefabCache.Clear();
            ClearPreparedVisualCache();
            loadingBundles.Clear();
            Resources.UnloadUnusedAssets();
        }

        private void ClearPreparedVisualCache()
        {
            foreach (GameObject prepared in preparedVisualCache.Values)
            {
                if (prepared != null)
                {
                    Destroy(prepared);
                }
            }
            preparedVisualCache.Clear();
        }

        private void OnDestroy()
        {
            StopAllCoroutines();
            // Do not unload process-global AssetBundle objects here. Other
            // ModularContentService instances may still have async requests.
            // Unity releases them when the player/editor process exits.
            bundles.Clear();
            prefabCache.Clear();
            preparedVisualCache.Clear();
            loadingBundles.Clear();
        }

        private IEnumerator LoadPrefab(ModularContentRecord record, Action<GameObject> completed)
        {
            string root = ResolveBundleRoot();
            string bundlePath = Path.Combine(root, record.bundleAddress.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(bundlePath))
            {
                completed(null);
                yield break;
            }

            if (!bundles.TryGetValue(bundlePath, out AssetBundle bundle) || bundle == null)
            {
                bundle = AssetBundle.GetAllLoadedAssetBundles()
                    .FirstOrDefault(candidate =>
                        candidate != null &&
                        string.Equals(
                            candidate.name.Replace('\\', '/'),
                            record.bundleAddress.Replace('\\', '/'),
                            StringComparison.OrdinalIgnoreCase));
                if (bundle != null)
                {
                    bundles[bundlePath] = bundle;
                }
            }

            if (bundle == null)
            {
                bool ownsRequest = false;
                if (!loadingBundles.TryGetValue(bundlePath, out AssetBundleCreateRequest bundleRequest))
                {
                    bundleRequest = AssetBundle.LoadFromFileAsync(bundlePath);
                    TrackRequest(bundleRequest);
                    loadingBundles[bundlePath] = bundleRequest;
                    ownsRequest = true;
                }
                if (ownsRequest)
                {
                    yield return bundleRequest;
                }
                else
                {
                    while (!bundleRequest.isDone)
                    {
                        yield return null;
                    }
                }
                bundle = bundleRequest.assetBundle;
                if (bundle == null)
                {
                    LastError = "Bundle 加载失败: " + bundlePath;
                    loadingBundles.Remove(bundlePath);
                    completed(null);
                    yield break;
                }
                bundles[bundlePath] = bundle;
                loadingBundles.Remove(bundlePath);
                LastError = null;
            }

            AssetBundleRequest assetRequest = bundle.LoadAssetAsync<GameObject>(record.assetAddress);
            TrackRequest(assetRequest);
            pendingAssetRequests++;
            yield return assetRequest;
            pendingAssetRequests = Mathf.Max(0, pendingAssetRequests - 1);
            completed(assetRequest.asset as GameObject);
        }

        private static void TrackRequest(AsyncOperation request)
        {
            if (request != null)
            {
                GlobalAssetRequests.Add(request);
            }
        }

        private static string ResolveBundleRoot()
        {
            return Path.Combine(
                Application.streamingAssetsPath,
                "ModularContent",
                "Windows");
        }

        private static GameObject CreatePlaceholder(ModularContentRecord record, Transform parent)
        {
            GameObject placeholder = GameObject.CreatePrimitive(PrimitiveType.Cube);
            placeholder.transform.SetParent(parent, false);
            int[] size = record?.footprint;
            placeholder.transform.localScale = size != null && size.Length >= 3
                ? new Vector3(Mathf.Max(1, size[0]), Mathf.Max(1, size[1]), Mathf.Max(1, size[2]))
                : Vector3.one;
            Renderer renderer = placeholder.GetComponent<Renderer>();
            renderer.material.color = record != null && record.IsModule
                ? new Color(0.18f, 0.62f, 0.82f)
                : new Color(0.82f, 0.54f, 0.18f);
            return placeholder;
        }
    }

    [Serializable]
    public sealed class LabPropPose
    {
        public string sourceId;
        public Vector3 position;
        public Vector3 eulerAngles;
        public Vector3 scale = Vector3.one;
        public bool dynamicEffect;
    }

    [Serializable]
    public sealed class LabLayoutData
    {
        public int formatVersion = 1;
        public string savedUtc;
        public LabPropPose[] props = Array.Empty<LabPropPose>();
    }

    public static class LabLayoutStore
    {
        public const int PropLimit = 512;
        public const int ActiveDynamicEffectLimit = 64;

        public static bool Save(string galaxyDirectory, LabLayoutData layout, out string error)
        {
            error = null;
            try
            {
                if (layout == null)
                {
                    throw new ArgumentNullException(nameof(layout));
                }

                layout.props = (layout.props ?? Array.Empty<LabPropPose>()).Take(PropLimit).ToArray();
                layout.savedUtc = DateTime.UtcNow.ToString("O");
                string directory = Path.Combine(galaxyDirectory, "spacecraft");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "lab_layout.json");
                string temporary = path + ".tmp";
                File.WriteAllText(temporary, JsonUtility.ToJson(layout, true));
                if (File.Exists(path))
                {
                    File.Replace(temporary, path, null);
                }
                else
                {
                    File.Move(temporary, path);
                }
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        public static bool TryLoad(string galaxyDirectory, out LabLayoutData layout, out string error)
        {
            layout = new LabLayoutData();
            error = null;
            string path = Path.Combine(galaxyDirectory, "spacecraft", "lab_layout.json");
            if (!File.Exists(path))
            {
                return true;
            }

            try
            {
                layout = JsonUtility.FromJson<LabLayoutData>(File.ReadAllText(path)) ?? new LabLayoutData();
                layout.props = (layout.props ?? Array.Empty<LabPropPose>()).Take(PropLimit).ToArray();
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                string quarantine = path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                File.Move(path, quarantine);
                layout = new LabLayoutData();
                return false;
            }
        }
    }
}
