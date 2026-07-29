using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SpacecraftEditor
{
    [DisallowMultipleComponent]
    public sealed class EditorUIController : MonoBehaviour
    {
        private static readonly Color Cyan = new Color(0.12f, 0.88f, 1f, 1f);
        private static readonly Color Gold = new Color(1f, 0.72f, 0.12f, 1f);

        [SerializeField] private WorkshopUIReferences hierarchy;

        private SpacecraftApp app;
        private ShipAssembly assembly;
        private BuildModeController buildController;
        private CommandHistory history;
        private ShipFlightController flight;
        private ForceVisualizer visualizer;
        private ShipHullController hullController;
        private PartCatalog partCatalog;
        private SpacecraftMaterialCatalog materialCatalog;
        private readonly Dictionary<ShipHullDefinition, HullCardUI> hullCards =
            new Dictionary<ShipHullDefinition, HullCardUI>();
        private bool refreshingScale;
        private bool capturingBinding;
        private ThrusterPart bindingTarget;
        private float bindingFeedbackUntil;
        private float nextRefresh;
        private bool hullSelectionMode;
        private SpacecraftPartCategory activeCategory = SpacecraftPartCategory.Thruster;

        public RectTransform BuildViewport => hierarchy == null ? null : hierarchy.BuildViewport;
        public RectTransform HullSelectionViewport => hierarchy == null ? null : hierarchy.HullSelectionViewport;
        public bool IsHullSelectionMode => hullSelectionMode;

        public void SetHierarchy(WorkshopUIReferences value)
        {
            hierarchy = value;
        }

        private void Start()
        {
            // Scene bootstrap can finish replacing serialized UI events after Awake.
            // Rebinding here keeps the hierarchy-authored controls authoritative.
            if (hierarchy != null && hierarchy.IsConfigured && buildController != null)
                BindButtons();
        }

        public void Configure(
            SpacecraftApp owner,
            PartCatalog partCatalog,
            HullCatalog shipHullCatalog,
            ShipHullController shipHullController,
            ShipAssembly targetAssembly,
            BuildModeController builder,
            CommandHistory commandHistory,
            ShipFlightController flightController,
            ForceVisualizer forceVisualizer)
        {
            app = owner;
            this.partCatalog = partCatalog;
            materialCatalog = FindObjectOfType<SpacecraftMaterialCatalog>();
            assembly = targetAssembly;
            buildController = builder;
            history = commandHistory;
            flight = flightController;
            visualizer = forceVisualizer;
            hullController = shipHullController;
            if (hierarchy == null)
                hierarchy = GetComponent<WorkshopUIReferences>();
            if (hierarchy == null || !hierarchy.IsConfigured)
            {
                Debug.LogError("Spacecraft workshop UI hierarchy is incomplete.", this);
                enabled = false;
                return;
            }

            BindButtons();
            BuildCards(partCatalog, shipHullCatalog);
            buildController.SelectionChanged += HandleSelectionChanged;
            buildController.MirrorChanged += HandleMirrorChanged;
            buildController.SnapStateChanged += RefreshSnapStatus;
            history.HistoryChanged += RefreshHistoryButtons;
            assembly.AssemblyChanged += RefreshStats;
            flight.StateChanged += RefreshFlightHud;
            if (hullController != null)
                hullController.HullChanged += HandleHullChanged;

            HandleSelectionChanged(null);
            HandleMirrorChanged(buildController.MirrorEnabled);
            RefreshSnapStatus();
            RefreshHistoryButtons();
            RefreshStats();
            RefreshForceButton();
            SetFlightMode(false);
        }

        private void BindButtons()
        {
            Bind(hierarchy.UndoButton, buildController.Undo);
            Bind(hierarchy.RedoButton, app.RestartBuild);
            Bind(hierarchy.MirrorButton, buildController.ToggleMirror);
            Bind(hierarchy.ForceButton, () =>
            {
                visualizer.ToggleVisible();
                RefreshForceButton();
            });
            Bind(hierarchy.DeleteButton, buildController.DeleteSelection);
            Bind(hierarchy.BindingButton, BeginBindingCapture);
            Bind(hierarchy.FlightButton, app.EnterSpace);
            Bind(hierarchy.HullConfirmButton, () => app.ConfirmHullSelection());
            BindOptional(hierarchy.DecorationTabButton, () => SetPartCategory(SpacecraftPartCategory.Decoration));
            BindOptional(hierarchy.ThrusterTabButton, () => SetPartCategory(SpacecraftPartCategory.Thruster));
            BindOptional(hierarchy.WeaponTabButton, () => SetPartCategory(SpacecraftPartCategory.Weapon));
            BindOptional(hierarchy.WeaponGroup1Button, () => buildController.SetSelectedWeaponGroup(1));
            BindOptional(hierarchy.WeaponGroup2Button, () => buildController.SetSelectedWeaponGroup(2));

            hierarchy.ScaleSlider.onValueChanged.RemoveAllListeners();
            hierarchy.ScaleSlider.onValueChanged.AddListener(value =>
            {
                if (!refreshingScale)
                    buildController.SetSelectedScale(value);
            });
            var committer = hierarchy.ScaleSlider.GetComponent<ScaleSliderCommitter>();
            if (committer != null)
                committer.Configure(buildController);
        }

        private static void Bind(Button button, UnityEngine.Events.UnityAction action)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }

        private static void BindOptional(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null)
                Bind(button, action);
        }

        private void BuildCards(PartCatalog partCatalog, HullCatalog shipHullCatalog)
        {
            ClearGeneratedChildren(hierarchy.HullCardRoot, hierarchy.HullCardTemplate.transform);
            hierarchy.PartCardTemplate.gameObject.SetActive(false);
            hierarchy.HullCardTemplate.gameObject.SetActive(false);
            hullCards.Clear();
            BuildPartCards();
            BuildMaterialSwatches();

            var hullDefinitions = shipHullCatalog == null ? null : shipHullCatalog.Definitions;
            if (hullDefinitions == null)
                return;
            for (var index = 0; index < hullDefinitions.Count; index++)
            {
                var definition = hullDefinitions[index];
                if (definition == null)
                    continue;
                var card = Instantiate(hierarchy.HullCardTemplate, hierarchy.HullCardRoot);
                card.name = definition.HullId;
                card.gameObject.SetActive(true);
                card.Configure(definition, app.PreviewHull);
                hullCards[definition] = card;
            }
        }

        private void BuildPartCards()
        {
            ClearGeneratedChildren(hierarchy.PartCardRoot, hierarchy.PartCardTemplate.transform);
            var partDefinitions = partCatalog == null ? null : partCatalog.Definitions;
            if (partDefinitions == null)
                return;
            for (int index = 0; index < partDefinitions.Count; index++)
            {
                ShipPartDefinition definition = partDefinitions[index];
                if (definition == null || definition.Category != activeCategory)
                    continue;
                PartCardUI card = Instantiate(hierarchy.PartCardTemplate, hierarchy.PartCardRoot);
                card.name = definition.PartId;
                card.gameObject.SetActive(true);
                card.Configure(buildController, definition);
            }

            ScrollRect scrollRect = hierarchy.PartCardRoot.GetComponentInParent<ScrollRect>();
            if (scrollRect != null)
            {
                Canvas.ForceUpdateCanvases();
                scrollRect.verticalNormalizedPosition = 1f;
            }
        }

        private void BuildMaterialSwatches()
        {
            if (hierarchy.MaterialSwatchRoot == null || hierarchy.MaterialSwatchTemplate == null || materialCatalog == null)
                return;
            ClearGeneratedChildren(hierarchy.MaterialSwatchRoot, hierarchy.MaterialSwatchTemplate.transform);
            hierarchy.MaterialSwatchTemplate.gameObject.SetActive(false);
            var definitions = materialCatalog.Definitions;
            if (definitions == null)
                return;
            for (int index = 0; index < definitions.Count; index++)
            {
                SpacecraftMaterialDefinition definition = definitions[index];
                if (definition == null)
                    continue;
                Button swatch = Instantiate(hierarchy.MaterialSwatchTemplate, hierarchy.MaterialSwatchRoot);
                swatch.name = definition.MaterialId;
                swatch.gameObject.SetActive(true);
                if (swatch.targetGraphic is Image image)
                    image.color = definition.PreviewColor;
                AddSurfacePreview(swatch, definition);
                AddSurfaceCode(swatch, definition);
                string materialId = definition.MaterialId;
                string displayName = definition.DisplayName;
                swatch.onClick.RemoveAllListeners();
                swatch.onClick.AddListener(() =>
                {
                    if (!buildController.ApplySelectedMaterial(materialId))
                        app.ApplyHullMaterial(materialId);
                    if (hierarchy.SelectionText != null)
                    {
                        string target = buildController.SelectedPart == null ? "船体" : "部件";
                        hierarchy.SelectionText.text = target + "材质：" + displayName;
                    }
                });
            }
        }

        private static void AddSurfacePreview(
            Button swatch,
            SpacecraftMaterialDefinition definition)
        {
            if (swatch == null || definition?.Material == null)
                return;
            Texture texture = definition.Material.GetTexture("_MainTex");
            if (texture == null)
                return;
            var previewObject = new GameObject(
                "SurfacePreview",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(RawImage));
            previewObject.transform.SetParent(swatch.transform, false);
            var rect = (RectTransform)previewObject.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(2f, 2f);
            rect.offsetMax = new Vector2(-2f, -2f);
            RawImage preview = previewObject.GetComponent<RawImage>();
            preview.texture = texture;
            preview.uvRect = new Rect(0f, 0f, 1f, 1f);
            preview.color = definition.Material.HasProperty("_Color")
                ? definition.Material.GetColor("_Color")
                : Color.white;
            preview.raycastTarget = false;
        }

        private void AddSurfaceCode(Button swatch, SpacecraftMaterialDefinition definition)
        {
            if (swatch == null || definition == null)
                return;
            var labelObject = new GameObject(
                "SurfaceCode",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Text));
            labelObject.transform.SetParent(swatch.transform, false);
            var rect = (RectTransform)labelObject.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            Text label = labelObject.GetComponent<Text>();
            label.font = hierarchy.SelectionText == null ? null : hierarchy.SelectionText.font;
            label.fontSize = 9;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false;
            label.text = SurfaceCode(definition.MaterialId);
            Color color = definition.PreviewColor;
            float luminance = color.r * 0.2126f + color.g * 0.7152f + color.b * 0.0722f;
            label.color = luminance > 0.55f
                ? new Color(0.02f, 0.035f, 0.045f, 0.95f)
                : new Color(0.92f, 0.98f, 1f, 0.95f);
        }

        private static string SurfaceCode(string materialId)
        {
            switch (materialId)
            {
                case "paint.native": return "OEM";
                case "paint.deep_space_blue": return "Ti";
                case "paint.gunmetal": return "GM";
                case "paint.ceramic_white": return "Al";
                case "paint.warning_red": return "Zn";
                case "paint.industrial_copper": return "Cu";
                case "paint.explorer_green": return "Ag";
                case "paint.brushed_brass": return "Br";
                case "paint.graphite_pitted": return "Gr";
                default: return "M";
            }
        }

        public void SetPartCategory(SpacecraftPartCategory category)
        {
            activeCategory = category;
            buildController.CancelPlacement();
            BuildPartCards();
        }

        private static void ClearGeneratedChildren(RectTransform root, Transform template)
        {
            for (var index = root.childCount - 1; index >= 0; index--)
            {
                var child = root.GetChild(index);
                if (child == template)
                    continue;
                Destroy(child.gameObject);
            }
        }

        private void OnDestroy()
        {
            if (buildController != null)
            {
                buildController.SetKeyboardCaptureActive(false);
                buildController.SelectionChanged -= HandleSelectionChanged;
                buildController.MirrorChanged -= HandleMirrorChanged;
                buildController.SnapStateChanged -= RefreshSnapStatus;
            }
            if (history != null)
                history.HistoryChanged -= RefreshHistoryButtons;
            if (assembly != null)
                assembly.AssemblyChanged -= RefreshStats;
            if (flight != null)
                flight.StateChanged -= RefreshFlightHud;
            if (hullController != null)
                hullController.HullChanged -= HandleHullChanged;
        }

        private void Update()
        {
            if (capturingBinding)
                UpdateBindingCapture();
            if (Time.unscaledTime < nextRefresh)
                return;
            nextRefresh = Time.unscaledTime + 0.1f;
            if (flight != null && flight.IsFlying)
                RefreshFlightHud();
            else
                RefreshStats();
        }

        public void SetFlightMode(bool value)
        {
            if (value)
                EndBindingCapture();
            hierarchy.BuildInterface.SetActive(!value && !hullSelectionMode);
            hierarchy.FlightHud.SetActive(value);
            hierarchy.HullSelectionPanel.SetActive(!value && hullSelectionMode);
            if (value)
                RefreshFlightHud();
        }

        public void SetHullSelectionMode(bool value)
        {
            hullSelectionMode = value;
            hierarchy.HullSelectionPanel.SetActive(value);
            hierarchy.BuildInterface.SetActive(!value && (flight == null || !flight.IsFlying));
            hierarchy.FlightHud.SetActive(false);
            RefreshHistoryButtons();
        }

        private void HandleHullChanged(ShipHullDefinition definition)
        {
            foreach (var pair in hullCards)
                pair.Value.SetSelected(pair.Key == definition);
            hierarchy.HullConfirmButton.interactable = definition != null;
            hierarchy.HullSelectionSummary.text = definition == null
                ? "请选择船体"
                : $"<b>{definition.DisplayName}</b>　{definition.Description}\n" +
                  $"尺寸 {definition.Dimensions.z:0.0} × {definition.Dimensions.x:0.0} × {definition.Dimensions.y:0.0}m　　基础质量 {SpaceflightUnitFormatter.FormatMass(definition.BaseMass)}";
        }

        private void HandleSelectionChanged(SpacecraftPart part)
        {
            var hasSelection = part != null;
            bool isThruster = part is ThrusterPart;
            bool isWeapon = part is WeaponPart;
            hierarchy.ScaleSlider.interactable = hasSelection && part.Definition != null && part.Definition.IsScalable;
            hierarchy.DeleteButton.interactable = hasSelection;
            hierarchy.BindingButton.gameObject.SetActive(isThruster);
            hierarchy.BindingStatusText.gameObject.SetActive(isThruster);
            hierarchy.BindingButton.interactable = isThruster;
            refreshingScale = true;
            hierarchy.ScaleSlider.value = hasSelection ? part.UniformScale : 1f;
            refreshingScale = false;
            hierarchy.SelectionText.text = hasSelection
                ? BuildSelectionSummary(part)
                : "未选择部件";
            RefreshBindingControl(part as ThrusterPart);
            if (hierarchy.WeaponGroup1Button != null)
            {
                hierarchy.WeaponGroup1Button.gameObject.SetActive(isWeapon);
                hierarchy.WeaponGroup1Button.interactable = isWeapon;
            }
            if (hierarchy.WeaponGroup2Button != null)
            {
                hierarchy.WeaponGroup2Button.gameObject.SetActive(isWeapon);
                hierarchy.WeaponGroup2Button.interactable = isWeapon;
            }
        }

        private static string BuildSelectionSummary(SpacecraftPart part)
        {
            if (part is ThrusterPart thruster)
                return $"{part.Definition.DisplayName}  |  推力 {SpaceflightUnitFormatter.FormatForce(thruster.ActualThrust)}  |  {SpaceflightUnitFormatter.FormatMass(part.ActualMass)}";
            if (part is WeaponPart weapon && weapon.Weapon != null)
                return $"{part.Definition.DisplayName}  |  {weapon.Weapon.MountSize}  |  {weapon.Weapon.Damage:0} 伤害  |  {SpaceflightUnitFormatter.FormatMass(part.ActualMass)}";
            ShipAerodynamicProfile aerodynamics =
                part.Definition.Aerodynamics;
            if (aerodynamics != null && aerodynamics.IsEnabled)
            {
                float area = aerodynamics.ReferenceArea
                    * part.UniformScale
                    * part.UniformScale;
                return $"{part.Definition.DisplayName}  |  气动 {aerodynamics.Role}  |  "
                    + $"面积 {area:0.0} m²  |  "
                    + SpaceflightUnitFormatter.FormatMass(part.ActualMass);
            }
            return $"{part.Definition.DisplayName}  |  装饰  |  {SpaceflightUnitFormatter.FormatMass(part.ActualMass)}";
        }

        private void BeginBindingCapture()
        {
            var selected = buildController == null ? null : buildController.SelectedPart as ThrusterPart;
            if (selected == null)
                return;
            capturingBinding = true;
            bindingTarget = selected;
            bindingFeedbackUntil = 0f;
            buildController.SetKeyboardCaptureActive(true);
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
            RefreshBindingControl(selected);
        }

        private void UpdateBindingCapture()
        {
            if (buildController == null || buildController.SelectedPart != bindingTarget || bindingTarget == null)
            {
                EndBindingCapture();
                return;
            }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                EndBindingCapture();
                return;
            }
            if (Input.GetKeyDown(KeyCode.Backspace))
            {
                buildController.SetSelectedActivationKey(KeyCode.None);
                EndBindingCapture();
                return;
            }
            for (var index = 0; index < ThrusterKeyBinding.BindableKeys.Count; index++)
            {
                var key = ThrusterKeyBinding.BindableKeys[index];
                if (!Input.GetKeyDown(key))
                    continue;
                buildController.SetSelectedActivationKey(key);
                EndBindingCapture();
                return;
            }
            if (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.T) ||
                Input.GetKeyDown(KeyCode.R) || Input.GetKeyDown(KeyCode.C) || Input.GetKeyDown(KeyCode.Delete))
            {
                bindingFeedbackUntil = Time.unscaledTime + 1.2f;
                RefreshBindingControl(bindingTarget);
            }
        }

        private void EndBindingCapture()
        {
            capturingBinding = false;
            bindingTarget = null;
            bindingFeedbackUntil = 0f;
            if (buildController != null)
                buildController.SetKeyboardCaptureActive(false);
            RefreshBindingControl(buildController == null ? null : buildController.SelectedPart as ThrusterPart);
        }

        private void RefreshBindingControl(ThrusterPart part)
        {
            var hasSelection = part != null;
            hierarchy.BindingButton.interactable = hasSelection;
            if (!hasSelection)
            {
                hierarchy.BindingButtonText.text = "先选择推进器";
                hierarchy.BindingStatusText.text = "点火按键";
                return;
            }
            hierarchy.BindingStatusText.text = string.IsNullOrEmpty(part.MirrorGroupId)
                ? "点火按键 · 可与其他推进器共享"
                : "点火按键 · 镜像同步";
            if (capturingBinding && part == bindingTarget)
            {
                hierarchy.BindingButtonText.text = Time.unscaledTime < bindingFeedbackUntil
                    ? "该按键已用于飞行控制"
                    : "请按目标键 · Esc 取消 · Backspace 清除";
                return;
            }
            hierarchy.BindingButtonText.text = part.IsBound
                ? ThrusterKeyBinding.GetDisplayName(part.ActivationKey) + " · 点击修改"
                : "未绑定 · 点击设置";
        }

        private void HandleMirrorChanged(bool enabled)
        {
            hierarchy.MirrorButtonText.text = enabled ? "镜像：开启" : "镜像：关闭";
            hierarchy.MirrorButtonText.color = enabled ? Cyan : Color.white;
        }

        private void RefreshForceButton()
        {
            if (visualizer == null)
                return;
            hierarchy.ForceButtonText.text = visualizer.IsVisible ? "力矢量：显示" : "力矢量：隐藏";
            hierarchy.ForceButtonText.color = visualizer.IsVisible ? Gold : Color.white;
        }

        private void RefreshSnapStatus()
        {
            if (buildController == null)
                return;
            if (buildController.IsSnapTemporarilyDisabled)
            {
                hierarchy.SnapStatusText.text = "自由放置（Alt）";
                hierarchy.SnapStatusText.color = Gold;
                return;
            }
            var axis = buildController.ActiveSnapAxis;
            if (axis == PlacementSnapAxis.None)
            {
                hierarchy.SnapStatusText.text =
                    "自由表面放置 · Ctrl 网格 · 靠近邻件磁吸 · Alt 关闭磁吸";
                hierarchy.SnapStatusText.color = new Color(0.58f, 0.72f, 0.78f, 0.95f);
                return;
            }

            string location;
            string thrustDirection;
            switch (axis)
            {
                case PlacementSnapAxis.Forward: location = "正前方"; thrustDirection = "-Z"; break;
                case PlacementSnapAxis.Rear: location = "正后方"; thrustDirection = "+Z"; break;
                case PlacementSnapAxis.Left: location = "左侧"; thrustDirection = "+X"; break;
                case PlacementSnapAxis.Right: location = "右侧"; thrustDirection = "-X"; break;
                case PlacementSnapAxis.Top: location = "上方"; thrustDirection = "-Y"; break;
                default: location = "下方"; thrustDirection = "+Y"; break;
            }
            var center = buildController.IsCenterSnapped ? "（中心）" : string.Empty;
            hierarchy.SnapStatusText.text =
                $"磁吸：{location}{center} · 推力 {thrustDirection} · Alt 关闭";
            hierarchy.SnapStatusText.color = Cyan;
        }

        private void RefreshHistoryButtons()
        {
            hierarchy.UndoButton.interactable = history != null && history.CanUndo;
            hierarchy.RedoButton.interactable = app != null && app.IsHullSelectionConfirmed;
        }

        private void RefreshStats()
        {
            if (assembly == null)
                return;
            var metrics = assembly.Metrics;
            ShipHullDefinition hull = hullController == null
                ? null
                : hullController.CurrentHull;
            SpacecraftPerformanceMetrics performance =
                SpacecraftPerformanceAnalyzer.Analyze(assembly, hull);
            Vector3 acceleration =
                performance.directionalAuthority.positiveAcceleration;
            string aerodynamics = performance.wingArea > 0.001f
                ? $"翼面积 {performance.wingArea:0.0} m²  翼载 {performance.wingLoading:0} kg/m²  "
                    + $"预计失速 {performance.estimatedStallSpeed:0.0} m/s  升阻比 {performance.estimatedLiftToDrag:0.0}"
                : "未安装有效升力面（仍可依靠 IFCS 垂直起降）";
            hierarchy.StatsText.text =
                $"部件 {assembly.Parts.Count}  总质量 {SpaceflightUnitFormatter.FormatMass(metrics.totalMass)}  "
                + $"加速度 X/Y/Z {acceleration.x:0.0}/{acceleration.y:0.0}/{acceleration.z:0.0} m/s²\n"
                + "重力支撑负载 0.5g/1g/1.8g：25%/49%/88%  "
                + $"物理爬升余量 {acceleration.y:0.0} m/s²\n"
                + aerodynamics;
            HandleSelectionChanged(buildController.SelectedPart);
        }

        private void RefreshFlightHud()
        {
            if (flight == null)
                return;
            hierarchy.FlightStatsText.text = $"试飞模式\n速度  {SpaceflightUnitFormatter.FormatSpeed(flight.Speed)}\n角速度  {flight.AngularSpeed:0.0} °/s\n" +
                                               $"稳定器  {(flight.StabilizationEnabled ? "开启" : "关闭")}\n推进器  {BuildBindingSummary()}";
        }

        private string BuildBindingSummary()
        {
            if (assembly == null || assembly.Thrusters.Count == 0)
                return "无推进器";
            var groups = new Dictionary<KeyCode, int>();
            var unbound = 0;
            foreach (var part in assembly.Thrusters)
            {
                if (part == null || !part.IsBound)
                {
                    unbound++;
                    continue;
                }
                groups.TryGetValue(part.ActivationKey, out var count);
                groups[part.ActivationKey] = count + 1;
            }
            var keys = new List<KeyCode>(groups.Keys);
            keys.Sort((left, right) => string.CompareOrdinal(
                ThrusterKeyBinding.GetDisplayName(left), ThrusterKeyBinding.GetDisplayName(right)));
            var labels = new List<string>();
            foreach (var key in keys)
            {
                var label = ThrusterKeyBinding.GetDisplayName(key) + " ×" + groups[key];
                if (Input.GetKey(key))
                    label = "<color=#1FE6FF>" + label + "</color>";
                labels.Add(label);
            }
            if (unbound > 0)
                labels.Add("未绑定 ×" + unbound);
            return string.Join("　", labels.ToArray());
        }
    }
}
