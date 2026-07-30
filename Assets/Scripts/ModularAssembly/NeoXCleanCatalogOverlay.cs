using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace UnityPlanet.ModularAssembly
{
    public sealed class NeoXCleanCatalogOverlay : MonoBehaviour
    {
        private const int PreviewLayer = 31;
        private const int CardsPerPage = 8;

        private static readonly string[] CategoryLabels =
        {
            "全部", "基础", "输出", "辅助", "武器", "防御", "能源"
        };

        private readonly List<Button> categoryButtons = new List<Button>();
        private readonly List<Button> cardButtons = new List<Button>();
        private readonly List<RawImage> cardImages = new List<RawImage>();
        private readonly List<Text> cardLabels = new List<Text>();
        private readonly List<Canvas> suppressedCanvases = new List<Canvas>();
        private readonly Dictionary<string, Texture2D> thumbnailCache =
            new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        private ModularContentService contentService;
        private NeoXCatalogIntegration integration;
        private ModularAssemblyLabController controller;
        private GameObject root;
        private InputField search;
        private Text titleText;
        private Text countText;
        private Text detailTitle;
        private Text detailBody;
        private Text pageText;
        private Button applyButton;
        private Canvas ownCanvas;
        private Camera previewCamera;
        private Transform previewRoot;
        private Light previewLight;
        private Font uiFont;
        private string activeCategory = "全部";
        private int page;
        private ModularContentRecord selected;
        private List<ModularContentRecord> visibleRecords = new List<ModularContentRecord>();
        private int thumbnailGeneration;
        private Coroutine thumbnailWorker;
        private string lastClickedId;
        private float lastClickTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!ModularLabSceneProfile.AllowsBuildExperience(
                    SceneManager.GetActiveScene()) ||
                FindObjectOfType<NeoXCleanCatalogOverlay>() != null)
            {
                return;
            }

            new GameObject("NeoXCleanCatalogOverlay").AddComponent<NeoXCleanCatalogOverlay>();
        }

        private void Awake()
        {
            Camera sceneCamera = Camera.main;
            if (sceneCamera != null)
            {
                sceneCamera.cullingMask &= ~(1 << PreviewLayer);
            }
            Text sceneText = FindObjectOfType<Text>();
            uiFont = sceneText != null && sceneText.font != null
                ? sceneText.font
                : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildUi();
            BuildPreviewStage();
            root.SetActive(false);
            StartCoroutine(BindRuntime());
        }

        private IEnumerator BindRuntime()
        {
            while (contentService == null || !contentService.IsReady || integration == null)
            {
                contentService = FindObjectOfType<ModularContentService>();
                integration = FindObjectOfType<NeoXCatalogIntegration>();
                controller = FindObjectOfType<ModularAssemblyLabController>();
                yield return null;
            }

            DisableLegacyCatalog();
            HideLegacyUi();
            if (root.activeSelf)
            {
                RefreshCards();
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F2))
            {
                SetVisible(!root.activeSelf);
            }

            if (root.activeSelf && Input.GetKeyDown(KeyCode.Escape))
            {
                SetVisible(false);
            }
        }

        private void OnDestroy()
        {
            RestoreLegacyCanvases();
            foreach (Texture2D texture in thumbnailCache.Values)
            {
                if (texture != null)
                {
                    Destroy(texture);
                }
            }
        }

        private void SetVisible(bool visible)
        {
            root.SetActive(visible);
            HideLegacyUi();
            if (!visible)
            {
                RestoreLegacyCanvases();
                return;
            }

            SuppressLegacyCanvases();
            controller = controller != null ? controller : FindObjectOfType<ModularAssemblyLabController>();
            int moduleCount = CurrentModuleCount();
            countText.text =
                $"装载  {moduleCount}/" +
                global::ModularAssembly.GridAssemblyModel.ModuleLimit;
            RefreshCards();
        }

        private void RefreshCards()
        {
            if (contentService?.Catalog == null)
            {
                return;
            }

            IEnumerable<ModularContentRecord> query = contentService.Catalog.Items
                .Where(IsAirModule)
                .Where(MatchesCategory);
            string term = search != null ? search.text.Trim() : string.Empty;
            if (!string.IsNullOrEmpty(term))
            {
                query = query.Where(record =>
                    Contains(record.chineseName, term) ||
                    Contains(record.neoXId, term) ||
                    Contains(record.behavior, term));
            }

            List<ModularContentRecord> filtered = query.ToList();
            int pageCount = Mathf.Max(1, Mathf.CeilToInt(filtered.Count / (float)CardsPerPage));
            page = Mathf.Clamp(page, 0, pageCount - 1);
            visibleRecords = filtered.Skip(page * CardsPerPage).Take(CardsPerPage).ToList();
            pageText.text = $"{page + 1} / {pageCount}";
            countText.text =
                $"可用模块  {filtered.Count}    装载  {CurrentModuleCount()}/" +
                global::ModularAssembly.GridAssemblyModel.ModuleLimit;

            for (int index = 0; index < cardButtons.Count; index++)
            {
                Button card = cardButtons[index];
                RawImage image = cardImages[index];
                Text label = cardLabels[index];
                card.onClick.RemoveAllListeners();
                if (index >= visibleRecords.Count)
                {
                    card.gameObject.SetActive(false);
                    continue;
                }

                ModularContentRecord record = visibleRecords[index];
                card.gameObject.SetActive(true);
                label.text = $"{record.chineseName}\n{FormatFootprint(record)}";
                image.texture = thumbnailCache.TryGetValue(record.sourceId, out Texture2D cached)
                    ? cached
                    : Texture2D.grayTexture;
                SetCardSelected(card, selected != null && selected.sourceId == record.sourceId);
                card.onClick.AddListener(() => SelectCard(record));
            }

            thumbnailGeneration++;
            if (thumbnailWorker == null)
            {
                thumbnailWorker = StartCoroutine(ThumbnailWorker());
            }
        }

        private IEnumerator ThumbnailWorker()
        {
            while (true)
            {
                int generation = thumbnailGeneration;
                ModularContentRecord[] records = visibleRecords.ToArray();
                for (int index = 0; index < records.Length; index++)
                {
                    ModularContentRecord record = records[index];
                    if (!thumbnailCache.ContainsKey(record.sourceId))
                    {
                        GameObject preview = null;
                        yield return contentService.InstantiateAsync(
                            record,
                            previewRoot,
                            value => preview = value);
                        if (preview != null)
                        {
                            yield return null;
                            Texture2D thumbnail = RenderThumbnail(preview);
                            Destroy(preview);
                            if (thumbnail != null)
                            {
                                AddThumbnail(record.sourceId, thumbnail);
                            }
                        }
                    }

                    if (generation != thumbnailGeneration)
                    {
                        break;
                    }

                    int cardIndex = visibleRecords.FindIndex(item => item.sourceId == record.sourceId);
                    if (cardIndex >= 0 && cardIndex < cardImages.Count &&
                        thumbnailCache.TryGetValue(record.sourceId, out Texture2D texture))
                    {
                        cardImages[cardIndex].texture = texture;
                    }
                }

                if (generation == thumbnailGeneration)
                {
                    thumbnailWorker = null;
                    yield break;
                }
            }
        }

        private Texture2D RenderThumbnail(GameObject preview)
        {
            Renderer[] renderers = preview.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return null;
            }

            SetLayerRecursively(preview, PreviewLayer);
            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            float radius = Mathf.Max(0.8f, bounds.extents.magnitude);
            previewCamera.transform.position =
                bounds.center + new Vector3(radius * 1.25f, radius * 0.85f, -radius * 1.7f);
            previewCamera.transform.LookAt(bounds.center);
            previewCamera.orthographicSize = radius * 1.2f;
            previewLight.transform.position =
                bounds.center + new Vector3(-radius, radius * 1.5f, -radius);
            previewLight.transform.LookAt(bounds.center);

            RenderTexture target = RenderTexture.GetTemporary(256, 256, 24, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            previewCamera.targetTexture = target;
            previewCamera.Render();
            RenderTexture.active = target;
            Texture2D texture = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0f, 0f, 256f, 256f), 0, 0);
            texture.Apply();
            previewCamera.targetTexture = null;
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            return texture;
        }

        private void AddThumbnail(string sourceId, Texture2D texture)
        {
            if (thumbnailCache.Count >= 128)
            {
                KeyValuePair<string, Texture2D> oldest = thumbnailCache.First();
                thumbnailCache.Remove(oldest.Key);
                if (oldest.Value != null)
                {
                    Destroy(oldest.Value);
                }
            }
            thumbnailCache[sourceId] = texture;
        }

        private void SelectCard(ModularContentRecord record)
        {
            lastClickedId = record.sourceId;
            lastClickTime = Time.unscaledTime;
            selected = record;
            detailTitle.text = record.chineseName;
            detailBody.text =
                $"NeoX ID  {record.neoXId}\n" +
                $"占格  {FormatFootprint(record)}\n" +
                $"类型  {BehaviorLabel(record)}\n" +
                $"环境  空战 / 大气\n" +
                $"资源  {(string.Equals(record.conversion, "ok", StringComparison.OrdinalIgnoreCase) ? "完整" : "降级占位")}";
            applyButton.interactable = true;
            RefreshCardSelection();
            ApplySelection();
        }

        private void ApplySelection()
        {
            if (selected == null)
            {
                return;
            }

            GridBuildSlotController slots = FindObjectOfType<GridBuildSlotController>();
            if (slots != null)
            {
                slots.BeginPlacement(selected);
            }
            else
            {
                integration?.Select(selected);
            }
            SetVisible(false);
        }

        private void RefreshCardSelection()
        {
            for (int index = 0; index < visibleRecords.Count && index < cardButtons.Count; index++)
            {
                SetCardSelected(
                    cardButtons[index],
                    selected != null && selected.sourceId == visibleRecords[index].sourceId);
            }
        }

        private static bool IsAirModule(ModularContentRecord record)
        {
            if (record == null || !record.IsModule || !record.IsBase || !record.IsGridPlaceable)
            {
                return false;
            }

            string behavior = record.behavior ?? string.Empty;
            return !behavior.Equals("Wheel", StringComparison.OrdinalIgnoreCase) &&
                   !behavior.Equals("Track", StringComparison.OrdinalIgnoreCase) &&
                   !behavior.Equals("Leg", StringComparison.OrdinalIgnoreCase);
        }

        private bool MatchesCategory(ModularContentRecord record)
        {
            if (activeCategory == "全部")
            {
                return true;
            }

            string behavior = (record.behavior ?? string.Empty).ToLowerInvariant();
            string category = (record.category ?? string.Empty).ToLowerInvariant();
            switch (activeCategory)
            {
                case "基础":
                    return category.Contains("structure") || category.Contains("armor") ||
                           behavior == "structure";
                case "输出":
                    return behavior.Contains("thruster") || behavior == "wing" ||
                           behavior.Contains("controlsurface") || behavior == "hover";
                case "辅助":
                    return behavior == "radar" || behavior == "repair" ||
                           behavior == "emp" || behavior == "drone" ||
                           behavior.Contains("forcefield");
                case "武器":
                    return category.Contains("weapon") || IsWeaponBehavior(behavior);
                case "防御":
                    return category.Contains("defense") || behavior == "shield" ||
                           behavior.Contains("armor");
                case "能源":
                    return category.Contains("energy") || behavior == "battery" ||
                           behavior == "energy";
                default:
                    return true;
            }
        }

        private static bool IsWeaponBehavior(string behavior)
        {
            string[] tokens =
            {
                "kinetic", "gatling", "cannon", "rocket", "missile", "mortar",
                "bomb", "laser", "flame", "drill", "saw"
            };
            return tokens.Any(behavior.Contains);
        }

        private static bool Contains(string source, string term)
        {
            return !string.IsNullOrEmpty(source) &&
                   source.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int CurrentModuleCount()
        {
            GridAssemblyPresenter presenter = FindObjectsOfType<GridAssemblyPresenter>()
                .FirstOrDefault(item => item != null && item.name == "GridShip");
            return presenter != null ? presenter.Views.Count : 0;
        }

        private static string FormatFootprint(ModularContentRecord record)
        {
            int[] size = record?.footprint;
            return size != null && size.Length >= 3
                ? $"{size[0]} × {size[1]} × {size[2]}"
                : "1 × 1 × 1";
        }

        private static string BehaviorLabel(ModularContentRecord record)
        {
            return string.IsNullOrEmpty(record?.behavior) ? "结构" : record.behavior;
        }

        private void BuildUi()
        {
            GameObject canvasObject = new GameObject(
                "CleanCatalogCanvas",
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            ownCanvas = canvasObject.GetComponent<Canvas>();
            ownCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            ownCanvas.sortingOrder = 300;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            root = CreatePanel(canvasObject.transform, "CatalogRoot", Color.clear);
            Stretch(root.GetComponent<RectTransform>());
            GameObject shade = CreatePanel(root.transform, "Shade",
                new Color(0.03f, 0.11f, 0.17f, 0.25f));
            Stretch(shade.GetComponent<RectTransform>());

            titleText = CreateText(root.transform, "空战改装", 44, FontStyle.Bold,
                new Color(0.05f, 0.95f, 0.82f, 1f));
            SetRect(titleText.rectTransform, new Vector2(28f, -24f), new Vector2(380f, 70f));
            countText = CreateText(root.transform, "可用模块", 18, FontStyle.Normal, Color.white);
            SetRect(countText.rectTransform, new Vector2(32f, -92f), new Vector2(500f, 38f));

            Button close = CreateButton(root.transform, "×", new Color(0.08f, 0.35f, 0.42f, 0.7f));
            SetTopRight(close.GetComponent<RectTransform>(), new Vector2(-28f, -24f), new Vector2(64f, 64f));
            close.GetComponentInChildren<Text>().fontSize = 44;
            close.onClick.AddListener(() => SetVisible(false));

            GameObject rail = CreatePanel(root.transform, "CategoryRail",
                new Color(0.05f, 0.17f, 0.25f, 0.72f));
            SetRect(rail.GetComponent<RectTransform>(), new Vector2(20f, -145f), new Vector2(110f, 760f));
            for (int index = 0; index < CategoryLabels.Length; index++)
            {
                string category = CategoryLabels[index];
                Button button = CreateButton(
                    rail.transform,
                    category,
                    index == 0
                        ? new Color(0.04f, 0.83f, 0.73f, 0.95f)
                        : new Color(0.18f, 0.32f, 0.40f, 0.76f));
                SetRect(button.GetComponent<RectTransform>(),
                    new Vector2(0f, -index * 96f), new Vector2(110f, 82f));
                button.GetComponentInChildren<Text>().fontSize = 24;
                categoryButtons.Add(button);
                button.onClick.AddListener(() =>
                {
                    activeCategory = category;
                    page = 0;
                    UpdateCategoryVisuals();
                    RefreshCards();
                });
            }

            search = CreateInput(root.transform);
            SetRect(search.GetComponent<RectTransform>(), new Vector2(150f, -144f), new Vector2(440f, 50f));
            search.onValueChanged.AddListener(_ =>
            {
                page = 0;
                RefreshCards();
            });

            GameObject grid = CreatePanel(root.transform, "CardGrid", Color.clear);
            SetRect(grid.GetComponent<RectTransform>(), new Vector2(150f, -212f), new Vector2(440f, 642f));
            for (int index = 0; index < CardsPerPage; index++)
            {
                int column = index % 2;
                int row = index / 2;
                Button card = CreateButton(grid.transform, string.Empty,
                    new Color(0.22f, 0.34f, 0.42f, 0.72f));
                SetRect(card.GetComponent<RectTransform>(),
                    new Vector2(column * 220f, -row * 158f), new Vector2(204f, 144f));
                RawImage thumbnail = CreateRawImage(card.transform);
                SetRect(thumbnail.rectTransform, new Vector2(8f, -8f), new Vector2(188f, 104f));
                Text label = CreateText(card.transform, string.Empty, 15, FontStyle.Bold, Color.white);
                SetRect(label.rectTransform, new Vector2(8f, -108f), new Vector2(188f, 34f));
                label.alignment = TextAnchor.MiddleCenter;
                cardButtons.Add(card);
                cardImages.Add(thumbnail);
                cardLabels.Add(label);
            }

            Button previous = CreateButton(root.transform, "上一页",
                new Color(0.08f, 0.40f, 0.50f, 0.9f));
            SetRect(previous.GetComponent<RectTransform>(), new Vector2(150f, -870f), new Vector2(126f, 48f));
            previous.onClick.AddListener(() =>
            {
                page = Mathf.Max(0, page - 1);
                RefreshCards();
            });
            pageText = CreateText(root.transform, "1 / 1", 18, FontStyle.Bold, Color.white);
            SetRect(pageText.rectTransform, new Vector2(280f, -870f), new Vector2(180f, 48f));
            pageText.alignment = TextAnchor.MiddleCenter;
            Button next = CreateButton(root.transform, "下一页",
                new Color(0.08f, 0.40f, 0.50f, 0.9f));
            SetRect(next.GetComponent<RectTransform>(), new Vector2(464f, -870f), new Vector2(126f, 48f));
            next.onClick.AddListener(() =>
            {
                page++;
                RefreshCards();
            });

            GameObject details = CreatePanel(root.transform, "Details",
                new Color(0.03f, 0.12f, 0.18f, 0.76f));
            RectTransform detailsRect = details.GetComponent<RectTransform>();
            detailsRect.anchorMin = new Vector2(0.34f, 0f);
            detailsRect.anchorMax = new Vector2(1f, 0f);
            detailsRect.pivot = new Vector2(0f, 0f);
            detailsRect.anchoredPosition = new Vector2(0f, 22f);
            detailsRect.sizeDelta = new Vector2(-28f, 170f);

            detailTitle = CreateText(details.transform, "选择一个模块", 30, FontStyle.Bold,
                new Color(0.08f, 0.95f, 0.82f, 1f));
            SetRect(detailTitle.rectTransform, new Vector2(26f, -20f), new Vector2(390f, 46f));
            detailBody = CreateText(details.transform,
                "单击查看模块，双击可直接进入放置。", 18, FontStyle.Normal, Color.white);
            SetRect(detailBody.rectTransform, new Vector2(28f, -66f), new Vector2(560f, 92f));

            Button cancel = CreateButton(details.transform, "取消",
                new Color(0.05f, 0.70f, 0.68f, 0.94f));
            SetBottomRight(cancel.GetComponent<RectTransform>(),
                new Vector2(-250f, 28f), new Vector2(200f, 66f));
            cancel.onClick.AddListener(() => SetVisible(false));
            applyButton = CreateButton(details.transform, "应用",
                new Color(1f, 0.35f, 0.16f, 0.96f));
            SetBottomRight(applyButton.GetComponent<RectTransform>(),
                new Vector2(-28f, 28f), new Vector2(200f, 66f));
            applyButton.interactable = false;
            applyButton.onClick.AddListener(ApplySelection);
        }

        private void BuildPreviewStage()
        {
            GameObject stage = new GameObject("ThumbnailStage");
            stage.transform.SetParent(transform, false);
            stage.transform.position = new Vector3(10000f, 10000f, 10000f);
            stage.layer = PreviewLayer;
            previewRoot = new GameObject("PreviewRoot").transform;
            previewRoot.SetParent(stage.transform, false);
            previewRoot.gameObject.layer = PreviewLayer;

            GameObject cameraObject = new GameObject("ThumbnailCamera", typeof(Camera));
            cameraObject.transform.SetParent(stage.transform, false);
            previewCamera = cameraObject.GetComponent<Camera>();
            previewCamera.enabled = false;
            previewCamera.orthographic = true;
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = new Color(0.08f, 0.16f, 0.21f, 1f);
            previewCamera.cullingMask = 1 << PreviewLayer;
            previewCamera.nearClipPlane = 0.01f;
            previewCamera.farClipPlane = 200f;
            previewCamera.allowHDR = false;

            GameObject lightObject = new GameObject("ThumbnailLight", typeof(Light));
            lightObject.transform.SetParent(stage.transform, false);
            previewLight = lightObject.GetComponent<Light>();
            previewLight.type = LightType.Directional;
            previewLight.intensity = 1.35f;
            previewLight.cullingMask = 1 << PreviewLayer;
        }

        private void UpdateCategoryVisuals()
        {
            for (int index = 0; index < categoryButtons.Count; index++)
            {
                categoryButtons[index].GetComponent<Image>().color =
                    CategoryLabels[index] == activeCategory
                        ? new Color(0.04f, 0.83f, 0.73f, 0.95f)
                        : new Color(0.18f, 0.32f, 0.40f, 0.76f);
            }
        }

        private void DisableLegacyCatalog()
        {
            MonoBehaviour legacy = Resources.FindObjectsOfTypeAll<MonoBehaviour>()
                .FirstOrDefault(item =>
                    item != null &&
                    item.gameObject.scene.IsValid() &&
                    item.GetType().Name == "ModularLabCatalogOverlay");
            if (legacy == null)
            {
                return;
            }

            legacy.enabled = false;
            System.Reflection.FieldInfo panelField = legacy.GetType().GetField(
                "catalogPanel",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);
            GameObject panel = panelField?.GetValue(legacy) as GameObject;
            panel?.SetActive(false);
        }

        private void HideLegacyUi()
        {
            foreach (Transform candidate in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (candidate == null || !candidate.gameObject.scene.IsValid())
                {
                    continue;
                }
                if (candidate.name == "ModuleLibrary" ||
                    candidate.name == "NeoX目录 F2")
                {
                    candidate.gameObject.SetActive(false);
                }
            }
        }

        private void SuppressLegacyCanvases()
        {
            RestoreLegacyCanvases();
            foreach (Canvas canvas in FindObjectsOfType<Canvas>())
            {
                if (canvas == null || canvas == ownCanvas || !canvas.enabled)
                {
                    continue;
                }
                suppressedCanvases.Add(canvas);
                canvas.enabled = false;
            }
        }

        private void RestoreLegacyCanvases()
        {
            for (int index = 0; index < suppressedCanvases.Count; index++)
            {
                if (suppressedCanvases[index] != null)
                {
                    suppressedCanvases[index].enabled = true;
                }
            }
            suppressedCanvases.Clear();
        }

        private static void SetCardSelected(Button card, bool isSelected)
        {
            card.GetComponent<Image>().color = isSelected
                ? new Color(0.04f, 0.86f, 0.76f, 0.92f)
                : new Color(0.22f, 0.34f, 0.42f, 0.72f);
        }

        private GameObject CreatePanel(Transform parent, string name, Color color)
        {
            GameObject panel = new GameObject(name, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            panel.GetComponent<Image>().color = color;
            return panel;
        }

        private Text CreateText(
            Transform parent,
            string value,
            int size,
            FontStyle style,
            Color color)
        {
            GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            Text text = textObject.GetComponent<Text>();
            text.font = uiFont;
            text.text = value;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private Button CreateButton(Transform parent, string label, Color color)
        {
            GameObject buttonObject =
                new GameObject("Button_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            Image image = buttonObject.GetComponent<Image>();
            image.color = color;
            Button button = buttonObject.GetComponent<Button>();
            ColorBlock colors = button.colors;
            colors.highlightedColor = Color.Lerp(color, Color.white, 0.18f);
            colors.pressedColor = Color.Lerp(color, Color.black, 0.16f);
            colors.selectedColor = colors.highlightedColor;
            button.colors = colors;
            Text text = CreateText(buttonObject.transform, label, 22, FontStyle.Bold, Color.white);
            Stretch(text.rectTransform);
            text.alignment = TextAnchor.MiddleCenter;
            return button;
        }

        private InputField CreateInput(Transform parent)
        {
            GameObject inputObject =
                new GameObject("Search", typeof(RectTransform), typeof(Image), typeof(InputField));
            inputObject.transform.SetParent(parent, false);
            inputObject.GetComponent<Image>().color = new Color(0.07f, 0.18f, 0.23f, 0.86f);
            Text value = CreateText(inputObject.transform, string.Empty, 18, FontStyle.Normal, Color.white);
            SetOffsets(value.rectTransform, 16f, 12f, 16f, 10f);
            Text placeholder = CreateText(
                inputObject.transform,
                "搜索中文名或 NeoX ID",
                18,
                FontStyle.Normal,
                new Color(0.72f, 0.82f, 0.86f, 0.78f));
            SetOffsets(placeholder.rectTransform, 16f, 12f, 16f, 10f);
            InputField input = inputObject.GetComponent<InputField>();
            input.textComponent = value;
            input.placeholder = placeholder;
            return input;
        }

        private static RawImage CreateRawImage(Transform parent)
        {
            GameObject imageObject = new GameObject("Thumbnail", typeof(RectTransform), typeof(RawImage));
            imageObject.transform.SetParent(parent, false);
            RawImage image = imageObject.GetComponent<RawImage>();
            image.color = Color.white;
            image.raycastTarget = false;
            return image;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void SetRect(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void SetTopRight(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void SetBottomRight(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void SetOffsets(RectTransform rect, float left, float top, float right, float bottom)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private static void SetLayerRecursively(GameObject target, int layer)
        {
            target.layer = layer;
            foreach (Transform child in target.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }
    }
}
