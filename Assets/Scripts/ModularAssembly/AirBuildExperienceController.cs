using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ModularAssembly;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityPlanet.SpaceStation;

namespace UnityPlanet.ModularAssembly
{
    public sealed class AirBuildExperienceController : MonoBehaviour
    {
        private const int CardsPerPage = 8;
        private const int PreviewLayer = 31;
        private static readonly string[] Categories =
            AirBuildCatalog.PaletteCategories.ToArray();

        private readonly List<Button> categoryButtons = new List<Button>();
        private readonly List<Button> cardButtons = new List<Button>();
        private readonly List<RawImage> cardImages = new List<RawImage>();
        private readonly List<Text> cardLabels = new List<Text>();
        private readonly Dictionary<string, Texture2D> thumbnails =
            new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<Renderer, bool> environmentRenderers =
            new Dictionary<Renderer, bool>();

        private ModularContentService contentService;
        private ModularAssemblyLabController controller;
        private GridAssemblyPresenter presenter;
        private Camera sceneCamera;
        private Font font;
        private Canvas canvas;
        private RectTransform sidebar;
        private GameObject fullPanel;
        private GameObject compactPanel;
        private InputField search;
        private Text countText;
        private Text pageText;
        private Text placementText;
        private Button flightButton;
        private Text flightButtonLabel;
        private Button spaceLaunchButton;
        private Button saveCanonicalButton;
        private Button savePresetButton;
        private Button presetFlightButton;
        private Button battleTestButton;
        private GameObject combatModeOverlay;
        private Button duelModeButton;
        private Button hordeModeButton;
        private int selectedCombatMode;
        private Button coreThrusterToggleButton;
        private Text coreThrusterToggleLabel;
        private RobocraftMotionCoordinator motionCoordinatorRc1;
        private Button controlSchemeButton;
        private Text controlSchemeLabel;
        private Text v3StatsText;
        private GameObject selectionPanel;
        private Text selectionName;
        private Button selectionDeleteButton;
        private GameObject wheelRoleRoot;
        private readonly List<Button> wheelRoleButtons = new List<Button>();
        private RawImage compactImage;
        private Text compactName;
        private Transform thumbnailRoot;
        private Camera thumbnailCamera;
        private Light thumbnailLight;
        private GameObject hangarRoot;
        private Transform surfaceRoot;
        private Transform ghostFrame;
        private Transform primaryGhostRoot;
        private Transform mirrorGhostRoot;
        private GameObject faceHighlight;
        private GameObject boundaryHint;
        private Material ghostMaterial;
        private Material faceMaterial;
        private Material boundaryMaterial;
        private Material hangarMaterial;
        private ModularContentRecord activeRecord;
        private GridModuleDefinition activeDefinition;
        private GridPlacementCandidate candidate;
        private string activeCategory = "结构";
        private int page;
        private int roll;
        private int ghostGeneration;
        private bool placing;
        private bool lastFlight;
        private bool presentationInitialized;
        private CameraClearFlags originalClearFlags;
        private Color originalBackground;
        private Coroutine thumbnailWorker;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!ModularLabSceneProfile.AllowsBuildExperience(
                    SceneManager.GetActiveScene()) ||
                FindObjectOfType<AirBuildExperienceController>() != null)
            {
                return;
            }

            new GameObject("AirBuildExperience").AddComponent<AirBuildExperienceController>();
        }

        private IEnumerator Start()
        {
            HideLegacyBootstrapUi();
            NeoXCatalogIntegration catalogIntegration = null;
            while (contentService == null || !contentService.IsReady ||
                   controller == null || presenter == null ||
                   catalogIntegration == null ||
                   !catalogIntegration.DefinitionsReady)
            {
                contentService = FindObjectOfType<ModularContentService>();
                controller = FindObjectOfType<ModularAssemblyLabController>();
                presenter = FindObjectsOfType<GridAssemblyPresenter>()
                    .FirstOrDefault(item => item != null && item.name == "GridShip");
                catalogIntegration =
                    FindObjectOfType<NeoXCatalogIntegration>();
                yield return null;
            }

            sceneCamera = Camera.main;
            font = FindObjectOfType<Text>()?.font ??
                   Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            AirBuildCatalog.OrderedIds.ToList().ForEach(id =>
            {
                ModularContentRecord record = contentService.Catalog.Items
                    .FirstOrDefault(item => item != null &&
                                            string.Equals(item.neoXId, id, StringComparison.OrdinalIgnoreCase));
                AirBuildCatalog.ApplyDefaults(record);
            });

            SuppressLegacy();
            BuildUi();
            BuildThumbnailStage();
            BuildGhostStage();
            BuildHangar();
            presenter.Rebuilt += HandlePresenterRebuilt;
            HandlePresenterRebuilt();
            SetBuildPresentation(!controller.IsFlying);
            RefreshCards();
            StartThumbnailWorker();
            CancelPlacement();
            FocusAssembly();
            if (ModularSpaceLaunchStatus.TryConsume(out string launchMessage)
                && placementText != null)
            {
                placementText.text = launchMessage;
            }
        }

        private static void HideLegacyBootstrapUi()
        {
            foreach (Canvas legacyCanvas in FindObjectsOfType<Canvas>())
            {
                if (legacyCanvas == null)
                {
                    continue;
                }
                legacyCanvas.enabled = false;
                GraphicRaycaster raycaster = legacyCanvas.GetComponent<GraphicRaycaster>();
                if (raycaster != null)
                {
                    raycaster.enabled = false;
                }
            }

            foreach (GridBuildSlotController legacySlots in
                     FindObjectsOfType<GridBuildSlotController>())
            {
                legacySlots.enabled = false;
            }
        }

        private void Update()
        {
            if (controller == null || presenter == null)
            {
                return;
            }

            if (Time.frameCount % 60 == 0)
            {
                SuppressLegacy();
            }

            bool flying = controller.IsFlying;
            if (flying != lastFlight)
            {
                if (flying)
                {
                    CancelPlacement();
                }
                SetBuildPresentation(!flying);
                lastFlight = flying;
            }
            if (flying)
            {
                return;
            }
            if (combatModeOverlay != null && combatModeOverlay.activeSelf)
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    CloseCombatModeSelector();
                }
                else if (Input.GetKeyDown(KeyCode.LeftArrow) ||
                         Input.GetKeyDown(KeyCode.RightArrow))
                {
                    selectedCombatMode = 1 - selectedCombatMode;
                    RefreshCombatModeSelection();
                }
                else if (Input.GetKeyDown(KeyCode.Return) ||
                         Input.GetKeyDown(KeyCode.KeypadEnter))
                {
                    StartSelectedCombatMode();
                }
                return;
            }
            if (!placing &&
                Input.GetKeyDown(KeyCode.Escape) &&
                SpaceStationFlowContext.CanReturnToStationFromAssembly)
            {
                TryReturnToStation();
                return;
            }
            SyncGhostFrame();

            if (Input.GetKeyDown(KeyCode.F))
            {
                FocusAssembly();
            }
            if (placing && Input.GetKeyDown(KeyCode.F5))
            {
                StartFlightFromBuildUi();
                return;
            }

            if (!placing)
            {
                if (Input.GetKeyDown(KeyCode.R))
                {
                    controller.Redo();
                    RefreshSelectionPanel();
                    return;
                }
                RefreshSelectionPanel();
                return;
            }

            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (ctrl && Input.GetKeyDown(KeyCode.Z))
            {
                controller.Undo();
                return;
            }
            if (ctrl && Input.GetKeyDown(KeyCode.Y))
            {
                controller.Redo();
                return;
            }
            if (Input.GetKeyDown(KeyCode.R))
            {
                roll = (roll + 1) & 3;
            }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                CancelPlacement();
                return;
            }

            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (overUi)
            {
                HideCandidate();
                return;
            }

            UpdateCandidate();
            if (Input.GetMouseButtonDown(0))
            {
                CommitCandidate();
            }
        }

        private void BeginPlacement(ModularContentRecord record)
        {
            if (record == null || record.BehaviorKind == GridModuleBehaviorKind.Core)
            {
                return;
            }

            string moduleId = "neox@" + record.sourceId.Replace("@", "_");
            if (!controller.Model.Definitions.TryGetValue(moduleId, out GridModuleDefinition definition))
            {
                placementText.text = "模块定义尚未加载。";
                return;
            }

            activeRecord = record;
            activeDefinition = definition;
            roll = 0;
            placing = true;
            candidate = null;
            surfaceRoot.gameObject.SetActive(true);
            controller.enabled = false;
            sidebar.sizeDelta = new Vector2(104f, sidebar.sizeDelta.y);
            fullPanel.SetActive(false);
            compactPanel.SetActive(true);
            compactName.text = record.chineseName + "\nR 旋转\nEsc 取消";
            compactImage.texture = thumbnails.TryGetValue(record.sourceId, out Texture2D texture)
                ? texture
                : Texture2D.grayTexture;
            placementText.text = "指向载具表面安装 " + record.chineseName;
            RebuildSurface();
            StartCoroutine(LoadGhost(record, ++ghostGeneration));
        }

        private void CancelPlacement()
        {
            placing = false;
            activeRecord = null;
            activeDefinition = null;
            candidate = null;
            ghostGeneration++;
            if (surfaceRoot != null)
            {
                surfaceRoot.gameObject.SetActive(false);
            }
            controller.enabled = true;
            sidebar.sizeDelta = new Vector2(390f, sidebar.sizeDelta.y);
            fullPanel.SetActive(true);
            compactPanel.SetActive(false);
            placementText.text = "选择模块后，指向载具表面进行安装";
            HideCandidate();
        }

        private void RefreshSelectionPanel()
        {
            if (selectionPanel == null ||
                controller == null ||
                controller.Model == null ||
                selectionName == null ||
                selectionDeleteButton == null ||
                wheelRoleRoot == null)
            {
                return;
            }

            GridModuleRecord selected =
                controller.Model.Find(controller.SelectedRuntimeId);
            if (selected != null && selected.Definition == null)
            {
                selectionPanel.SetActive(false);
                return;
            }
            bool editable = selected != null &&
                            selected.Definition.Category != GridModuleCategory.Core;
            selectionPanel.SetActive(selected != null);
            if (selected == null)
            {
                return;
            }

            selectionName.text =
                selected.Definition.DisplayName + "\n" +
                selected.Definition.Footprint.x + "×" +
                selected.Definition.Footprint.y + "×" +
                selected.Definition.Footprint.z;
            selectionDeleteButton.interactable = editable;
            bool isWheel = WheelModuleProfile.IsWheelModuleId(
                selected.Definition.ModuleId);
            wheelRoleRoot.SetActive(isWheel);
            RectTransform selectionRect =
                selectionPanel.GetComponent<RectTransform>();
            selectionRect.sizeDelta = new Vector2(
                310f,
                isWheel ? 226f : 142f);
            SetRect(
                selectionDeleteButton.GetComponent<RectTransform>(),
                new Vector2(
                    16f,
                    isWheel ? -168f : -84f),
                new Vector2(278f, 42f));
            if (isWheel)
            {
                WheelRoleOverride role =
                    WheelRoleSettings.Parse(selected.BehaviorSettings);
                for (int index = 0; index < wheelRoleButtons.Count; index++)
                {
                    wheelRoleButtons[index].GetComponent<Image>().color =
                        index == (int)role
                            ? new Color(0.04f, 0.82f, 0.70f, 0.96f)
                            : new Color(0.09f, 0.22f, 0.28f, 0.96f);
                }
            }
        }

        private void SetSelectedWheelRole(WheelRoleOverride role)
        {
            controller?.SetSelectedBehaviorSettings(
                WheelRoleSettings.Serialize(role));
            RefreshSelectionPanel();
        }

        private void DeleteSelectedModule()
        {
            if (controller == null ||
                string.IsNullOrEmpty(controller.SelectedRuntimeId))
            {
                return;
            }
            controller.DeleteSelected();
            RefreshSelectionPanel();
        }

        private void StartFlightFromBuildUi()
        {
            if (controller == null)
            {
                return;
            }

            GridAssemblyValidation validation = controller.Model.Validate();
            if (!validation.IsValid)
            {
                placementText.text = "无法试飞：" + validation.Message;
                return;
            }

            if (placing)
            {
                CancelPlacement();
            }
            controller.ToggleFlight();
        }

        private void SavePresetFromBuildUi()
        {
            if (controller == null)
                return;
            if (placing)
                CancelPlacement();
            controller.SavePreset(out string message);
            if (placementText != null)
                placementText.text = message;
        }

        private void SaveCanonicalFromBuildUi()
        {
            if (controller == null || controller.IsFlying)
                return;
            if (placing)
                CancelPlacement();
            controller.TrySaveCanonical(out string message);
            if (placementText != null)
                placementText.text = message;
        }

        private void EnterSpaceFromBuildUi()
        {
            if (controller == null || controller.IsFlying)
                return;
            if (SpaceStationFlowContext.MustSaveInitialAssembly)
            {
                TryReturnToStation();
                return;
            }
            if (placing)
                CancelPlacement();
            GridAssemblyValidation validation =
                controller.Model.Validate();
            if (!validation.IsValid)
            {
                if (placementText != null)
                    placementText.text =
                        "无法进入太空：" + validation.Message;
                return;
            }

            SpaceStationFlowContext.PrepareSpaceLaunch(
                controller.Model.CaptureBlueprint());
            if (spaceLaunchButton != null)
                spaceLaunchButton.interactable = false;
            if (placementText != null)
                placementText.text =
                    "正在使用当前设计进入太空；磁盘存档未被修改……";
            SceneManager.LoadScene("InterstellarFlight", LoadSceneMode.Single);
        }

        public bool TryReturnToStation()
        {
            if (!SpaceStationFlowContext.CanReturnToStationFromAssembly)
                return false;

            bool initialAssembly =
                SpaceStationFlowContext.MustSaveInitialAssembly;
            if (initialAssembly)
            {
                if (controller == null || controller.IsFlying)
                    return false;
                if (placing)
                    CancelPlacement();
                if (!controller.TrySaveCanonical(out string saveMessage))
                {
                    if (placementText != null)
                    {
                        placementText.text =
                            "首次飞船未能保存，仍停留在改装界面：" +
                            saveMessage;
                    }
                    return false;
                }
            }

            if (spaceLaunchButton != null)
                spaceLaunchButton.interactable = false;
            if (saveCanonicalButton != null)
                saveCanonicalButton.interactable = false;
            if (placementText != null)
            {
                placementText.text = initialAssembly
                    ? "首次飞船已保存，正在进入空间站……"
                    : "正在返回空间站；未保存的改动不会替换停靠飞船……";
            }
            SpaceStationFlowContext.CompleteAssemblyReturn();
            SceneManager.LoadScene(
                "SpaceStationUpgradeTest",
                LoadSceneMode.Single);
            return true;
        }

        private void LoadPresetFromBuildUi()
        {
            if (controller == null)
                return;
            if (placing)
                CancelPlacement();
            controller.LoadPresetForBuild(out string message);
            if (placementText != null)
                placementText.text = message;
        }

        private void StartCombatTestFromBuildUi()
        {
            if (placing)
                CancelPlacement();
            OpenCombatModeSelector();
        }

        private void StartSelectedCombatMode()
        {
            StartCombatModeFromBuildUi(
                selectedCombatMode == 0
                    ? CombatTestMode.Duel
                    : CombatTestMode.Horde);
        }

        private void StartCombatModeFromBuildUi(CombatTestMode mode)
        {
            CloseCombatModeSelector();
            CombatTestController combat =
                FindObjectOfType<CombatTestController>();
            string message;
            if (combat == null)
                message = "战斗测试系统尚未初始化。";
            else
                combat.TryBeginCombat(mode, out message);
            if (placementText != null)
                placementText.text = message;
        }

        private void ExportAiDiagnosticFromBuildUi()
        {
            if (controller == null)
                return;
            if (placing)
                CancelPlacement();

            bool success = VehicleAiDiagnosticExporter.TryExport(
                controller.Model,
                presenter,
                ResolveMotionCoordinatorRc1(),
                out string path,
                out string message);
            if (success)
            {
                GUIUtility.systemCopyBuffer = path;
                message =
                    "\u5df2\u751f\u6210 AI \u98de\u884c\u8bca\u65ad\u6587\u4ef6\uff0c" +
                    "\u8def\u5f84\u5df2\u590d\u5236\u5230\u526a\u8d34\u677f\u3002";
                Debug.Log($"AI vehicle diagnostic exported: {path}", this);
            }
            if (placementText != null)
                placementText.text = message;
        }

        private void UpdateCandidate()
        {
            if (sceneCamera == null)
            {
                sceneCamera = Camera.main;
            }
            if (sceneCamera == null || surfaceRoot == null)
            {
                HideCandidate();
                return;
            }

            Ray ray = sceneCamera.ScreenPointToRay(Input.mousePosition);
            RaycastHit[] hits = Physics.RaycastAll(
                ray,
                500f,
                ~0,
                QueryTriggerInteraction.Collide);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            RaycastHit? selected = null;
            AirBuildSurfaceCell surfaceCell = null;
            foreach (RaycastHit hit in hits)
            {
                AirBuildSurfaceCell cell = hit.collider.GetComponent<AirBuildSurfaceCell>();
                if (cell == null)
                {
                    continue;
                }
                selected = hit;
                surfaceCell = cell;
                break;
            }
            if (!selected.HasValue || surfaceCell == null)
            {
                HideCandidate();
                return;
            }

            RaycastHit surface = selected.Value;
            Vector3 localNormal = presenter.transform.InverseTransformDirection(surface.normal);
            Vector3Int normal = Cardinal(localNormal);
            GridSurfaceHit gridHit = new GridSurfaceHit(surfaceCell.Cell, normal, surface.point);
            GridPlacementResolver.TryResolve(
                controller.Model,
                activeDefinition,
                activeRecord,
                gridHit,
                Vector3Int.forward,
                Vector3Int.up,
                roll,
                controller.MirrorEnabled,
                null,
                out candidate);
            if (candidate != null)
            {
                candidate.WorldMountNormal =
                    presenter.transform.TransformDirection(candidate.MountNormal);
                candidate.WorldFunctionalDirection =
                    presenter.transform.TransformDirection(candidate.FunctionalForward);
                candidate.WorldExhaustDirection =
                    presenter.transform.TransformDirection(candidate.ExhaustDirection);
            }
            ShowCandidate(candidate);
        }

        private void CommitCandidate()
        {
            if (candidate == null || !candidate.IsValid)
            {
                if (candidate != null)
                {
                    placementText.text = candidate.Error;
                }
                return;
            }

            ModularBlueprintData before = controller.Model.CaptureBlueprint();
            HideCandidate();
            bool success = controller.Model.TryPlace(
                activeDefinition.ModuleId,
                candidate.PrimaryPose,
                controller.MirrorEnabled,
                out _,
                out string error);
            if (success)
            {
                candidate = null;
                controller.History.Record(before);
                placementText.text = activeRecord.chineseName + " 已安装，可继续放置";
            }
            else
            {
                placementText.text = error;
            }
        }

        private void ShowCandidate(GridPlacementCandidate value)
        {
            if (value == null)
            {
                HideCandidate();
                return;
            }

            Color color = value.IsValid
                ? new Color(0.05f, 0.95f, 0.68f, 0.48f)
                : value.OutOfBounds || value.NearBoundary
                    ? new Color(1f, 0.72f, 0.12f, 0.58f)
                    : new Color(1f, 0.18f, 0.12f, 0.58f);
            ghostMaterial.color = color;
            faceMaterial.color = color;
            boundaryMaterial.color = color;
            ApplyPose(primaryGhostRoot, activeDefinition, value.PrimaryPose);
            primaryGhostRoot.gameObject.SetActive(primaryGhostRoot.childCount > 0);
            if (value.HasMirror)
            {
                ApplyPose(
                    mirrorGhostRoot,
                    value.MirroredDefinition ?? activeDefinition,
                    value.MirroredPose);
                mirrorGhostRoot.gameObject.SetActive(mirrorGhostRoot.childCount > 0);
            }
            else
            {
                mirrorGhostRoot.gameObject.SetActive(false);
            }

            faceHighlight.SetActive(true);
            faceHighlight.transform.localPosition =
                (Vector3)value.Surface.Cell + Vector3.one * 0.5f +
                (Vector3)value.Surface.Normal * 0.505f;
            faceHighlight.transform.localRotation =
                Quaternion.FromToRotation(Vector3.forward, value.Surface.Normal);
            faceHighlight.transform.localScale = new Vector3(1.02f, 1.02f, 0.025f);
            boundaryHint.SetActive(value.NearBoundary || value.OutOfBounds);
            placementText.text = value.IsValid
                ? "左键安装  |  R 绕安装面旋转  |  Esc 取消"
                : value.Error;
        }

        private void HideCandidate()
        {
            if (primaryGhostRoot != null) primaryGhostRoot.gameObject.SetActive(false);
            if (mirrorGhostRoot != null) mirrorGhostRoot.gameObject.SetActive(false);
            if (faceHighlight != null) faceHighlight.SetActive(false);
            if (boundaryHint != null) boundaryHint.SetActive(false);
        }

        private void HandlePresenterRebuilt()
        {
            if (placing)
            {
                RebuildSurface();
            }
        }

        private void RebuildSurface()
        {
            if (surfaceRoot == null || controller?.Model == null)
            {
                return;
            }
            for (int index = surfaceRoot.childCount - 1; index >= 0; index--)
            {
                GameObject staleProxy =
                    surfaceRoot.GetChild(index).gameObject;
                staleProxy.SetActive(false);
                Destroy(staleProxy);
            }

            HashSet<Vector3Int> occupied = new HashSet<Vector3Int>();
            foreach (GridModuleRecord record in controller.Model.Records)
            foreach (Vector3Int cell in controller.Model.GetCells(record))
            {
                occupied.Add(cell);
            }

            Vector3Int[] directions =
            {
                Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down,
                new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1)
            };
            foreach (Vector3Int cell in occupied)
            {
                if (!directions.Any(direction => !occupied.Contains(cell + direction)))
                {
                    continue;
                }

                GameObject proxy = new GameObject("BuildSurface_" + cell);
                proxy.transform.SetParent(surfaceRoot, false);
                proxy.transform.localPosition = (Vector3)cell + Vector3.one * 0.5f;
                BoxCollider collider = proxy.AddComponent<BoxCollider>();
                collider.size = Vector3.one * 0.995f;
                proxy.AddComponent<AirBuildSurfaceCell>().Initialize(cell);
            }
            surfaceRoot.gameObject.SetActive(placing);
        }

        private IEnumerator LoadGhost(ModularContentRecord record, int generation)
        {
            ClearChildren(primaryGhostRoot);
            ClearChildren(mirrorGhostRoot);
            GameObject loaded = null;
            yield return contentService.InstantiateAsync(
                record,
                primaryGhostRoot,
                value => loaded = value);
            if (generation != ghostGeneration || loaded == null)
            {
                if (loaded != null) Destroy(loaded);
                yield break;
            }

            DisableGhostBehaviours(loaded);
            ApplyGhostMaterial(loaded);
            ModularContentRecord mirroredRecord = ResolveMirroredRecord(record);
            GameObject mirrored = null;
            if (ReferenceEquals(mirroredRecord, record))
            {
                mirrored = Instantiate(loaded, mirrorGhostRoot);
            }
            else
            {
                yield return contentService.InstantiateAsync(
                    mirroredRecord,
                    mirrorGhostRoot,
                    value => mirrored = value);
                if (generation != ghostGeneration)
                {
                    if (mirrored != null) Destroy(mirrored);
                    yield break;
                }
                if (mirrored == null)
                {
                    mirrored = Instantiate(loaded, mirrorGhostRoot);
                }
                else
                {
                    DisableGhostBehaviours(mirrored);
                    ApplyGhostMaterial(mirrored);
                }
            }
            mirrored.name = loaded.name + "_MirrorGhost";
            primaryGhostRoot.gameObject.SetActive(false);
            mirrorGhostRoot.gameObject.SetActive(false);
        }

        private ModularContentRecord ResolveMirroredRecord(ModularContentRecord source)
        {
            if (source == null || contentService?.Catalog == null)
            {
                return source;
            }

            AirBuildCatalog.ApplyDefaults(source);
            string counterpartId = source.pairedNeoXId;
            if (string.IsNullOrWhiteSpace(counterpartId))
            {
                switch (source.neoXId)
                {
                    case "large_wing_left_361":
                        counterpartId = "large_wing_right_361";
                        break;
                    case "large_wing_right_361":
                        counterpartId = "large_wing_left_361";
                        break;
                    case "small_wing_left_231":
                        counterpartId = "small_wing_right_231";
                        break;
                    case "small_wing_right_231":
                        counterpartId = "small_wing_left_231";
                        break;
                    case "speedwheel_small_l_322":
                        counterpartId = "speedwheel_small_r_322";
                        break;
                    case "speedwheel_small_r_322":
                        counterpartId = "speedwheel_small_l_322";
                        break;
                    case "speedwheel_large_l_522":
                        counterpartId = "speedwheel_large_r_522";
                        break;
                    case "speedwheel_large_r_522":
                        counterpartId = "speedwheel_large_l_522";
                        break;
                    default:
                        return source;
                }
            }

            return contentService.Catalog.Items.FirstOrDefault(item =>
                       item != null &&
                       string.Equals(
                           item.neoXId,
                           counterpartId,
                           StringComparison.OrdinalIgnoreCase))
                   ?? source;
        }

        private void BuildGhostStage()
        {
            surfaceRoot = new GameObject("AirBuildSurfaceProxies").transform;
            surfaceRoot.SetParent(presenter.transform, false);
            ghostFrame = new GameObject("AirBuildGhostStage").transform;
            ghostFrame.SetParent(transform, false);
            SyncGhostFrame();
            primaryGhostRoot = new GameObject("PrimaryModuleGhost").transform;
            primaryGhostRoot.SetParent(ghostFrame, false);
            mirrorGhostRoot = new GameObject("MirrorModuleGhost").transform;
            mirrorGhostRoot.SetParent(ghostFrame, false);

            Shader ghostShader = Shader.Find("Sprites/Default") ??
                                 Shader.Find("Universal Render Pipeline/Unlit") ??
                                 Shader.Find("Unlit/Color");
            ghostMaterial = new Material(ghostShader) { name = "AirBuildGhost" };
            faceMaterial = new Material(ghostShader) { name = "AirBuildFace" };
            boundaryMaterial = new Material(ghostShader) { name = "AirBuildBoundary" };

            faceHighlight = GameObject.CreatePrimitive(PrimitiveType.Cube);
            faceHighlight.name = "HoveredMountFace";
            faceHighlight.transform.SetParent(presenter.transform, false);
            Destroy(faceHighlight.GetComponent<Collider>());
            faceHighlight.GetComponent<Renderer>().sharedMaterial = faceMaterial;

            boundaryHint = new GameObject("BuildBoundaryHint");
            boundaryHint.transform.SetParent(presenter.transform, false);
            CreateBoundaryLines(boundaryHint.transform);
            HideCandidate();
        }

        private void SyncGhostFrame()
        {
            if (ghostFrame == null || presenter == null)
            {
                return;
            }
            ghostFrame.SetPositionAndRotation(
                presenter.transform.position,
                presenter.transform.rotation);
            ghostFrame.localScale = presenter.transform.lossyScale;
        }

        private void BuildHangar()
        {
            hangarRoot = new GameObject("BuildHangarRoot");
            hangarRoot.transform.SetParent(presenter.transform, false);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ??
                            Shader.Find("Standard");
            hangarMaterial = new Material(shader)
            {
                name = "BuildHangarMaterial",
                color = new Color(0.045f, 0.095f, 0.12f, 1f)
            };
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "BuildDeck";
            floor.transform.SetParent(hangarRoot.transform, false);
            floor.transform.localPosition = new Vector3(0f, -6f, 0f);
            floor.transform.localScale = new Vector3(48f, 0.25f, 48f);
            floor.GetComponent<Renderer>().sharedMaterial = hangarMaterial;
            Destroy(floor.GetComponent<Collider>());

            Material gridMaterial = new Material(
                Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color"))
            {
                color = new Color(0.05f, 0.48f, 0.55f, 0.22f)
            };
            for (int index = -20; index <= 20; index++)
            {
                CreateLine(
                    hangarRoot.transform,
                    new Vector3(index, -5.86f, -20f),
                    new Vector3(index, -5.86f, 20f),
                    gridMaterial,
                    0.018f);
                CreateLine(
                    hangarRoot.transform,
                    new Vector3(-20f, -5.86f, index),
                    new Vector3(20f, -5.86f, index),
                    gridMaterial,
                    0.018f);
            }

            CreatePointLight("BuildKey", new Vector3(-8f, 10f, -8f), new Color(0.70f, 0.88f, 1f), 4.2f);
            CreatePointLight("BuildFill", new Vector3(9f, 3f, -2f), new Color(0.20f, 0.75f, 0.82f), 2.4f);
            CreatePointLight("BuildRim", new Vector3(0f, 5f, 10f), new Color(1f, 0.48f, 0.20f), 2.0f);
        }

        private void SetBuildPresentation(bool build)
        {
            if (!presentationInitialized && sceneCamera != null)
            {
                originalClearFlags = sceneCamera.clearFlags;
                originalBackground = sceneCamera.backgroundColor;
                presentationInitialized = true;
            }

            if (canvas != null) canvas.gameObject.SetActive(build);
            if (hangarRoot != null) hangarRoot.SetActive(build);
            if (sceneCamera != null)
            {
                sceneCamera.clearFlags = build ? CameraClearFlags.SolidColor : originalClearFlags;
                sceneCamera.backgroundColor = build
                    ? new Color(0.008f, 0.022f, 0.032f, 1f)
                    : originalBackground;
            }

            if (build)
            {
                environmentRenderers.Clear();
                foreach (Renderer renderer in FindObjectsOfType<Renderer>())
                {
                    if (renderer == null || renderer.transform.IsChildOf(presenter.transform) ||
                        renderer.transform.IsChildOf(hangarRoot.transform))
                    {
                        continue;
                    }
                    string path = HierarchyPath(renderer.transform);
                    if (!IsTestEnvironment(path, renderer))
                    {
                        continue;
                    }
                    environmentRenderers[renderer] = renderer.enabled;
                    renderer.enabled = false;
                }
            }
            else
            {
                foreach (KeyValuePair<Renderer, bool> pair in environmentRenderers)
                {
                    if (pair.Key != null) pair.Key.enabled = pair.Value;
                }
                environmentRenderers.Clear();
            }
        }

        private void BuildUi()
        {
            GameObject canvasObject = new GameObject(
                "AirBuildCanvas",
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 260;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            sidebar = CreatePanel(
                canvas.transform,
                "AirModulePalette",
                new Color(0.018f, 0.055f, 0.075f, 0.96f)).GetComponent<RectTransform>();
            sidebar.anchorMin = new Vector2(0f, 0f);
            sidebar.anchorMax = new Vector2(0f, 1f);
            sidebar.pivot = new Vector2(0f, 0.5f);
            sidebar.anchoredPosition = new Vector2(16f, -38f);
            sidebar.sizeDelta = new Vector2(390f, -104f);

            fullPanel = new GameObject("FullPalette", typeof(RectTransform));
            fullPanel.transform.SetParent(sidebar, false);
            Stretch(fullPanel.GetComponent<RectTransform>());
            Text title = CreateText(fullPanel.transform, "空战改装", 32, FontStyle.Bold,
                new Color(0.10f, 0.95f, 0.82f, 1f));
            SetRect(title.rectTransform, new Vector2(20f, -18f), new Vector2(250f, 44f));
            countText = CreateText(fullPanel.transform, "精品模块 30", 15, FontStyle.Normal,
                new Color(0.65f, 0.78f, 0.84f, 1f));
            SetRect(countText.rectTransform, new Vector2(22f, -60f), new Vector2(300f, 28f));

            search = CreateInput(fullPanel.transform);
            SetRect(search.GetComponent<RectTransform>(), new Vector2(18f, -98f), new Vector2(354f, 44f));
            search.onValueChanged.AddListener(_ =>
            {
                page = 0;
                RefreshCards();
            });

            GameObject categoryRoot = new GameObject("Categories", typeof(RectTransform));
            categoryRoot.transform.SetParent(fullPanel.transform, false);
            SetRect(categoryRoot.GetComponent<RectTransform>(), new Vector2(18f, -154f), new Vector2(354f, 92f));
            for (int index = 0; index < Categories.Length; index++)
            {
                string category = Categories[index];
                Button button = CreateButton(categoryRoot.transform, category);
                const int categoryColumns = 3;
                const float categoryWidth = 111f;
                const float categoryGap = 8f;
                const float categoryAreaWidth = 354f;
                int column = index % categoryColumns;
                int row = index / categoryColumns;
                int rowStart = row * categoryColumns;
                int itemsInRow = Mathf.Min(
                    categoryColumns,
                    Categories.Length - rowStart);
                float rowWidth = itemsInRow * categoryWidth +
                                 (itemsInRow - 1) * categoryGap;
                float rowOffset = (categoryAreaWidth - rowWidth) * 0.5f;
                SetRect(
                    button.GetComponent<RectTransform>(),
                    new Vector2(
                        rowOffset + column *
                        (categoryWidth + categoryGap),
                        -row * 44f),
                    new Vector2(categoryWidth, 38f));
                button.GetComponentInChildren<Text>().fontSize = 15;
                button.onClick.AddListener(() =>
                {
                    activeCategory = category;
                    page = 0;
                    RefreshCategoryColors();
                    RefreshCards();
                });
                categoryButtons.Add(button);
            }

            GameObject grid = new GameObject("ModuleCards", typeof(RectTransform));
            grid.transform.SetParent(fullPanel.transform, false);
            SetRect(grid.GetComponent<RectTransform>(), new Vector2(18f, -258f), new Vector2(354f, 570f));
            for (int index = 0; index < CardsPerPage; index++)
            {
                Button card = CreateButton(grid.transform, string.Empty);
                int column = index % 2;
                int row = index / 2;
                SetRect(
                    card.GetComponent<RectTransform>(),
                    new Vector2(column * 180f, -row * 138f),
                    new Vector2(170f, 128f));
                RawImage image = new GameObject(
                    "Thumbnail",
                    typeof(RectTransform),
                    typeof(RawImage)).GetComponent<RawImage>();
                image.transform.SetParent(card.transform, false);
                image.raycastTarget = false;
                SetRect(image.rectTransform, new Vector2(8f, -7f), new Vector2(154f, 88f));
                Text label = CreateText(card.transform, string.Empty, 14, FontStyle.Bold, Color.white);
                SetRect(label.rectTransform, new Vector2(6f, -94f), new Vector2(158f, 30f));
                label.alignment = TextAnchor.MiddleCenter;
                cardButtons.Add(card);
                cardImages.Add(image);
                cardLabels.Add(label);
            }

            Button previous = CreateButton(fullPanel.transform, "上一页");
            SetRect(previous.GetComponent<RectTransform>(), new Vector2(18f, -832f), new Vector2(104f, 42f));
            previous.onClick.AddListener(() =>
            {
                page = Mathf.Max(0, page - 1);
                RefreshCards();
            });
            pageText = CreateText(fullPanel.transform, "1 / 1", 16, FontStyle.Bold, Color.white);
            SetRect(pageText.rectTransform, new Vector2(125f, -832f), new Vector2(138f, 42f));
            pageText.alignment = TextAnchor.MiddleCenter;
            Button next = CreateButton(fullPanel.transform, "下一页");
            SetRect(next.GetComponent<RectTransform>(), new Vector2(268f, -832f), new Vector2(104f, 42f));
            next.onClick.AddListener(() =>
            {
                page++;
                RefreshCards();
            });

            placementText = CreateText(
                fullPanel.transform,
                "选择模块后，指向载具表面进行安装",
                15,
                FontStyle.Normal,
                new Color(0.65f, 0.82f, 0.88f, 1f));
            SetRect(
                placementText.rectTransform,
                new Vector2(18f, -878f),
                new Vector2(354f, 42f));

            Button exportDiagnostic = CreateButton(
                fullPanel.transform,
                "\u5bfc\u51fa AI \u98de\u884c\u8bca\u65ad");
            SetRect(
                exportDiagnostic.GetComponent<RectTransform>(),
                new Vector2(18f, -930f),
                new Vector2(354f, 38f));
            exportDiagnostic.GetComponent<Image>().color =
                new Color(0.04f, 0.48f, 0.64f, 0.98f);
            exportDiagnostic.GetComponentInChildren<Text>().fontSize = 15;
            exportDiagnostic.onClick.AddListener(
                ExportAiDiagnosticFromBuildUi);

            compactPanel = new GameObject("CompactPlacement", typeof(RectTransform));
            compactPanel.transform.SetParent(sidebar, false);
            Stretch(compactPanel.GetComponent<RectTransform>());
            compactImage = new GameObject(
                "SelectedThumbnail",
                typeof(RectTransform),
                typeof(RawImage)).GetComponent<RawImage>();
            compactImage.transform.SetParent(compactPanel.transform, false);
            SetRect(compactImage.rectTransform, new Vector2(10f, -18f), new Vector2(84f, 84f));
            compactName = CreateText(compactPanel.transform, string.Empty, 14, FontStyle.Bold, Color.white);
            SetRect(compactName.rectTransform, new Vector2(8f, -114f), new Vector2(88f, 110f));
            compactName.alignment = TextAnchor.UpperCenter;
            Button cancel = CreateButton(compactPanel.transform, "取消");
            SetRect(cancel.GetComponent<RectTransform>(), new Vector2(8f, -244f), new Vector2(88f, 40f));
            cancel.onClick.AddListener(CancelPlacement);
            compactPanel.SetActive(false);

            selectionPanel = CreatePanel(
                canvas.transform,
                "SelectedModuleActions",
                new Color(0.018f, 0.055f, 0.075f, 0.96f));
            RectTransform selectionRect = selectionPanel.GetComponent<RectTransform>();
            selectionRect.anchorMin = new Vector2(1f, 0f);
            selectionRect.anchorMax = new Vector2(1f, 0f);
            selectionRect.pivot = new Vector2(1f, 0f);
            selectionRect.anchoredPosition = new Vector2(-22f, 22f);
            selectionRect.sizeDelta = new Vector2(310f, 142f);

            selectionName = CreateText(
                selectionPanel.transform,
                "已选择模块",
                18,
                FontStyle.Bold,
                Color.white);
            SetRect(
                selectionName.rectTransform,
                new Vector2(16f, -14f),
                new Vector2(278f, 58f));

            wheelRoleRoot = new GameObject(
                "WheelRole",
                typeof(RectTransform));
            wheelRoleRoot.transform.SetParent(selectionPanel.transform, false);
            SetRect(
                wheelRoleRoot.GetComponent<RectTransform>(),
                new Vector2(16f, -72f),
                new Vector2(278f, 82f));
            Text wheelRoleLabel = CreateText(
                wheelRoleRoot.transform,
                "轮胎职责",
                14,
                FontStyle.Normal,
                new Color(0.62f, 0.78f, 0.84f, 1f));
            SetRect(
                wheelRoleLabel.rectTransform,
                Vector2.zero,
                new Vector2(278f, 24f));
            string[] roleLabels = { "自动", "转向驱动", "仅驱动", "自由轮" };
            for (int index = 0; index < roleLabels.Length; index++)
            {
                WheelRoleOverride role = (WheelRoleOverride)index;
                Button roleButton = CreateButton(
                    wheelRoleRoot.transform,
                    roleLabels[index]);
                SetRect(
                    roleButton.GetComponent<RectTransform>(),
                    new Vector2(index * 69f, -30f),
                    new Vector2(65f, 38f));
                roleButton.GetComponentInChildren<Text>().fontSize = 12;
                roleButton.onClick.AddListener(
                    () => SetSelectedWheelRole(role));
                wheelRoleButtons.Add(roleButton);
            }
            wheelRoleRoot.SetActive(false);

            selectionDeleteButton = CreateButton(
                selectionPanel.transform,
                "删除模块");
            SetRect(
                selectionDeleteButton.GetComponent<RectTransform>(),
                new Vector2(16f, -84f),
                new Vector2(278f, 42f));
            selectionDeleteButton.onClick.AddListener(DeleteSelectedModule);
            selectionPanel.SetActive(false);

            flightButton = CreateButton(canvas.transform, "开始试飞  F5");
            RectTransform flightRect = flightButton.GetComponent<RectTransform>();
            flightRect.anchorMin = new Vector2(1f, 1f);
            flightRect.anchorMax = new Vector2(1f, 1f);
            flightRect.pivot = new Vector2(1f, 1f);
            flightRect.anchoredPosition = new Vector2(-24f, -24f);
            flightRect.sizeDelta = new Vector2(210f, 58f);
            flightButton.GetComponent<Image>().color =
                new Color(0.04f, 0.72f, 0.65f, 0.98f);
            flightButtonLabel = flightButton.GetComponentInChildren<Text>();
            flightButtonLabel.text = "开始试飞  F5";
            flightButtonLabel.fontSize = 19;
            flightButton.onClick.AddListener(StartFlightFromBuildUi);

            savePresetButton = CreateButton(canvas.transform, "保存预制");
            RectTransform savePresetRect =
                savePresetButton.GetComponent<RectTransform>();
            savePresetRect.anchorMin = new Vector2(1f, 1f);
            savePresetRect.anchorMax = new Vector2(1f, 1f);
            savePresetRect.pivot = new Vector2(1f, 1f);
            savePresetRect.anchoredPosition = new Vector2(-432f, -94f);
            savePresetRect.sizeDelta = new Vector2(128f, 42f);
            savePresetButton.GetComponentInChildren<Text>().fontSize = 15;
            savePresetButton.onClick.AddListener(SavePresetFromBuildUi);

            presetFlightButton = CreateButton(canvas.transform, "载入预制");
            RectTransform presetFlightRect =
                presetFlightButton.GetComponent<RectTransform>();
            presetFlightRect.anchorMin = new Vector2(1f, 1f);
            presetFlightRect.anchorMax = new Vector2(1f, 1f);
            presetFlightRect.pivot = new Vector2(1f, 1f);
            presetFlightRect.anchoredPosition = new Vector2(-296f, -94f);
            presetFlightRect.sizeDelta = new Vector2(128f, 42f);
            presetFlightButton.GetComponent<Image>().color =
                new Color(0.04f, 0.58f, 0.72f, 0.98f);
            presetFlightButton.GetComponentInChildren<Text>().fontSize = 15;
            presetFlightButton.onClick.AddListener(
                LoadPresetFromBuildUi);

            battleTestButton = CreateButton(canvas.transform, "战斗测试");
            RectTransform battleTestRect =
                battleTestButton.GetComponent<RectTransform>();
            battleTestRect.anchorMin = new Vector2(1f, 1f);
            battleTestRect.anchorMax = new Vector2(1f, 1f);
            battleTestRect.pivot = new Vector2(1f, 1f);
            battleTestRect.anchoredPosition = new Vector2(-568f, -94f);
            battleTestRect.sizeDelta = new Vector2(128f, 42f);
            battleTestButton.GetComponent<Image>().color =
                new Color(0.82f, 0.22f, 0.12f, 0.98f);
            battleTestButton.GetComponentInChildren<Text>().fontSize = 15;
            
            battleTestButton.gameObject.SetActive(
                ModularLabSceneProfile.AllowsCombatTest(gameObject.scene));
battleTestButton.onClick.AddListener(
                StartCombatTestFromBuildUi);

            spaceLaunchButton = CreateButton(
                canvas.transform,
                ResolvePrimaryDestinationLabel(
                    SpaceStationFlowContext.MustSaveInitialAssembly));
            RectTransform spaceLaunchRect =
                spaceLaunchButton.GetComponent<RectTransform>();
            spaceLaunchRect.anchorMin = new Vector2(1f, 1f);
            spaceLaunchRect.anchorMax = new Vector2(1f, 1f);
            spaceLaunchRect.pivot = new Vector2(1f, 1f);
            spaceLaunchRect.anchoredPosition = new Vector2(-24f, -94f);
            spaceLaunchRect.sizeDelta = new Vector2(128f, 42f);
            spaceLaunchButton.GetComponent<Image>().color =
                new Color(0.12f, 0.48f, 0.92f, 0.98f);
            spaceLaunchButton.GetComponentInChildren<Text>().fontSize = 15;
            spaceLaunchButton.onClick.AddListener(
                EnterSpaceFromBuildUi);

            saveCanonicalButton = CreateButton(
                canvas.transform,
                "保存设计");
            RectTransform saveCanonicalRect =
                saveCanonicalButton.GetComponent<RectTransform>();
            saveCanonicalRect.anchorMin = new Vector2(1f, 1f);
            saveCanonicalRect.anchorMax = new Vector2(1f, 1f);
            saveCanonicalRect.pivot = new Vector2(1f, 1f);
            saveCanonicalRect.anchoredPosition =
                new Vector2(-160f, -94f);
            saveCanonicalRect.sizeDelta = new Vector2(128f, 42f);
            saveCanonicalButton.GetComponent<Image>().color =
                new Color(0.04f, 0.58f, 0.48f, 0.98f);
            saveCanonicalButton.GetComponentInChildren<Text>().fontSize = 15;
            saveCanonicalButton.onClick.AddListener(
                SaveCanonicalFromBuildUi);

            coreThrusterToggleButton = CreateButton(
                canvas.transform,
                "\u6838\u5fc3\u8f85\u52a9\uff1a\u6807\u51c6");
            RectTransform coreThrusterRect =
                coreThrusterToggleButton.GetComponent<RectTransform>();
            coreThrusterRect.anchorMin = new Vector2(1f, 1f);
            coreThrusterRect.anchorMax = new Vector2(1f, 1f);
            coreThrusterRect.pivot = new Vector2(1f, 1f);
            coreThrusterRect.anchoredPosition = new Vector2(-246f, -24f);
            coreThrusterRect.sizeDelta = new Vector2(198f, 58f);
            coreThrusterToggleLabel =
                coreThrusterToggleButton.GetComponentInChildren<Text>();
            coreThrusterToggleLabel.fontSize = 17;
            coreThrusterToggleButton.onClick.AddListener(
                ToggleBuiltInCoreThrusters);
            controlSchemeButton = CreateButton(
                canvas.transform,
                "控制：辅助驾驶");
            RectTransform schemeRect =
                controlSchemeButton.GetComponent<RectTransform>();
            schemeRect.anchorMin = new Vector2(1f, 1f);
            schemeRect.anchorMax = new Vector2(1f, 1f);
            schemeRect.pivot = new Vector2(1f, 1f);
            schemeRect.anchoredPosition = new Vector2(-456f, -24f);
            schemeRect.sizeDelta = new Vector2(198f, 58f);
            controlSchemeLabel =
                controlSchemeButton.GetComponentInChildren<Text>();
            controlSchemeLabel.fontSize = 17;
            controlSchemeLabel.text = "控制：RC相机转向";
            controlSchemeButton.interactable = false;

            v3StatsText = CreateText(
                canvas.transform,
                string.Empty,
                15,
                FontStyle.Normal,
                new Color(0.72f, 0.88f, 0.91f, 1f));
            RectTransform statsRect = v3StatsText.rectTransform;
            statsRect.anchorMin = new Vector2(1f, 1f);
            statsRect.anchorMax = new Vector2(1f, 1f);
            statsRect.pivot = new Vector2(1f, 1f);
            statsRect.anchoredPosition = new Vector2(-24f, -146f);
            statsRect.sizeDelta = new Vector2(408f, 230f);
            v3StatsText.alignment = TextAnchor.UpperRight;
            BuildCombatModeSelector();
            RefreshCoreThrusterToggle();
            RefreshCategoryColors();
        }

        private void BuildCombatModeSelector()
        {
            combatModeOverlay = CreatePanel(
                canvas.transform,
                "CombatModeOverlay",
                new Color(0.005f, 0.015f, 0.02f, 0.78f));
            Stretch(combatModeOverlay.GetComponent<RectTransform>());
            Button blocker = combatModeOverlay.AddComponent<Button>();
            blocker.transition = Selectable.Transition.None;
            blocker.onClick.AddListener(CloseCombatModeSelector);

            GameObject panel = CreatePanel(
                combatModeOverlay.transform,
                "CombatModePanel",
                new Color(0.018f, 0.065f, 0.085f, 0.98f));
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = panelRect.anchorMax =
                new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = Vector2.zero;
            panelRect.sizeDelta = new Vector2(760f, 380f);

            Text title = CreateText(
                panel.transform,
                "选择战斗测试模式",
                30,
                FontStyle.Bold,
                new Color(0.16f, 0.95f, 0.84f, 1f));
            title.alignment = TextAnchor.MiddleCenter;
            RectTransform titleRect = title.rectTransform;
            titleRect.anchorMin = titleRect.anchorMax =
                new Vector2(0.5f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -24f);
            titleRect.sizeDelta = new Vector2(600f, 48f);

            duelModeButton = CreateCombatModeCard(
                panel.transform,
                "1v1 单挑",
                "保留当前模块化敌机\n单体对决 · 模块损伤 · 完整物理",
                new Vector2(-170f, -20f));
            hordeModeButton = CreateCombatModeCard(
                panel.transform,
                "割草战斗",
                "4分钟固定强度生存战\n规则化增援 · 自动航路 · 总体血量",
                new Vector2(170f, -20f));
            duelModeButton.onClick.AddListener(
                () => StartCombatModeFromBuildUi(CombatTestMode.Duel));
            hordeModeButton.onClick.AddListener(
                () => StartCombatModeFromBuildUi(CombatTestMode.Horde));

            Button close = CreateButton(panel.transform, "取消  Esc");
            RectTransform closeRect = close.GetComponent<RectTransform>();
            closeRect.anchorMin = closeRect.anchorMax =
                new Vector2(0.5f, 0f);
            closeRect.pivot = new Vector2(0.5f, 0f);
            closeRect.anchoredPosition = new Vector2(0f, 22f);
            closeRect.sizeDelta = new Vector2(180f, 44f);
            close.onClick.AddListener(CloseCombatModeSelector);
            combatModeOverlay.SetActive(false);
        }

        private Button CreateCombatModeCard(
            Transform parent,
            string title,
            string description,
            Vector2 position)
        {
            GameObject card = CreatePanel(
                parent,
                "CombatMode_" + title,
                new Color(0.07f, 0.19f, 0.24f, 0.98f));
            RectTransform rect = card.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(300f, 190f);
            Button button = card.AddComponent<Button>();

            Text heading = CreateText(
                card.transform,
                title,
                26,
                FontStyle.Bold,
                Color.white);
            heading.alignment = TextAnchor.MiddleCenter;
            RectTransform headingRect = heading.rectTransform;
            headingRect.anchorMin = new Vector2(0f, 1f);
            headingRect.anchorMax = new Vector2(1f, 1f);
            headingRect.pivot = new Vector2(0.5f, 1f);
            headingRect.anchoredPosition = new Vector2(0f, -24f);
            headingRect.sizeDelta = new Vector2(-24f, 42f);

            Text details = CreateText(
                card.transform,
                description,
                16,
                FontStyle.Normal,
                new Color(0.76f, 0.88f, 0.91f, 1f));
            details.alignment = TextAnchor.MiddleCenter;
            RectTransform detailsRect = details.rectTransform;
            detailsRect.anchorMin = new Vector2(0f, 0f);
            detailsRect.anchorMax = new Vector2(1f, 1f);
            detailsRect.offsetMin = new Vector2(18f, 18f);
            detailsRect.offsetMax = new Vector2(-18f, -72f);
            return button;
        }

        private void OpenCombatModeSelector()
        {
            if (combatModeOverlay == null)
                return;
            selectedCombatMode = 0;
            combatModeOverlay.transform.SetAsLastSibling();
            combatModeOverlay.SetActive(true);
            RefreshCombatModeSelection();
        }

        private void CloseCombatModeSelector()
        {
            if (combatModeOverlay != null)
                combatModeOverlay.SetActive(false);
        }

        private void RefreshCombatModeSelection()
        {
            if (duelModeButton == null || hordeModeButton == null)
                return;
            duelModeButton.GetComponent<Image>().color =
                selectedCombatMode == 0
                    ? new Color(0.04f, 0.72f, 0.65f, 1f)
                    : new Color(0.07f, 0.19f, 0.24f, 0.98f);
            hordeModeButton.GetComponent<Image>().color =
                selectedCombatMode == 1
                    ? new Color(0.88f, 0.28f, 0.10f, 1f)
                    : new Color(0.07f, 0.19f, 0.24f, 0.98f);
            Button selected = selectedCombatMode == 0
                ? duelModeButton
                : hordeModeButton;
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(selected.gameObject);
        }

        private RobocraftMotionCoordinator ResolveMotionCoordinatorRc1()
        {
            if (motionCoordinatorRc1 == null)
            {
                motionCoordinatorRc1 =
                    FindObjectOfType<RobocraftMotionCoordinator>();
            }
            return motionCoordinatorRc1;
        }

        public static string ResolvePrimaryDestinationLabel(
            bool initialAssembly)
        {
            return initialAssembly ? "进入空间站" : "进入太空";
        }

        private void ToggleBuiltInCoreThrusters()
        {
            RobocraftMotionCoordinator coordinator =
                ResolveMotionCoordinatorRc1();
            if (coordinator == null)
                return;
            VehicleCoreAssistMode next =
                coordinator.CoreAssistMode == VehicleCoreAssistMode.Standard
                    ? VehicleCoreAssistMode.Training
                    : VehicleCoreAssistMode.Standard;
            coordinator.SetCoreAssistMode(next);
            RefreshCoreThrusterToggle();
        }

        private void ToggleControlScheme()
        {
        }

        private void RefreshCoreThrusterToggle()
        {
            RobocraftMotionCoordinator coordinator =
                ResolveMotionCoordinatorRc1();
            VehicleCoreAssistMode level = coordinator != null
                ? coordinator.CoreAssistMode
                : VehicleCoreAssistMode.Standard;
            if (coreThrusterToggleLabel != null)
            {
                switch (level)
                {
                    case VehicleCoreAssistMode.Training:
                        coreThrusterToggleLabel.text =
                            "\u6838\u5fc3\u8f85\u52a9\uff1a\u8857\u673a";
                        break;
                    case VehicleCoreAssistMode.Disabled:
                        coreThrusterToggleLabel.text =
                            "\u6838\u5fc3\u8f85\u52a9\uff1a\u65e0\u8f85\u52a9";
                        break;
                    default:
                        coreThrusterToggleLabel.text =
                            "\u6838\u5fc3\u8f85\u52a9\uff1a\u6807\u51c6";
                        break;
                }
            }
            if (coreThrusterToggleButton != null)
            {
                Color modeColor;
                switch (level)
                {
                    case VehicleCoreAssistMode.Training:
                        modeColor = new Color(0.04f, 0.72f, 0.65f, 0.98f);
                        break;
                    case VehicleCoreAssistMode.Disabled:
                        modeColor = new Color(0.28f, 0.22f, 0.22f, 0.98f);
                        break;
                    default:
                        modeColor = new Color(0.08f, 0.35f, 0.48f, 0.98f);
                        break;
                }
                coreThrusterToggleButton.GetComponent<Image>().color =
                    modeColor;
            }
            if (controlSchemeLabel != null)
            {
                controlSchemeLabel.text = level == VehicleCoreAssistMode.Training
                    ? "街机：W/S沿准星飞行，A/D横移，松键自停"
                    : "标准：RC物理推力，Space/Ctrl升降";
            }
            if (v3StatsText != null && coordinator != null)
                v3StatsText.text = coordinator.Telemetry.BuildSummary();
        }

        private void RefreshCards()
        {
            if (contentService?.Catalog == null || fullPanel == null)
            {
                return;
            }

            List<ModularContentRecord> records = AirBuildCatalog.OrderedIds
                .Select(id => contentService.Catalog.Items.FirstOrDefault(item =>
                    item != null && string.Equals(item.neoXId, id, StringComparison.OrdinalIgnoreCase)))
                .Where(AirBuildCatalog.IsVisibleInPalette)
                .ToList();
            string term = search != null ? search.text.Trim() : string.Empty;
            records = records
                .Where(item => AirBuildCatalog.Category(item) ==
                               activeCategory)
                .ToList();
            if (!string.IsNullOrEmpty(term))
            {
                records = records.Where(item =>
                    Contains(item.chineseName, term) || Contains(item.neoXId, term)).ToList();
            }

            int pageCount = Mathf.Max(1, Mathf.CeilToInt(records.Count / (float)CardsPerPage));
            page = Mathf.Clamp(page, 0, pageCount - 1);
            List<ModularContentRecord> visible =
                records.Skip(page * CardsPerPage).Take(CardsPerPage).ToList();
            pageText.text = (page + 1) + " / " + pageCount;
            countText.text = "精品模块  " + records.Count + "    装载 " +
                             controller.Model.Records.Count + "/" +
                             GridAssemblyModel.ModuleLimit;
            for (int index = 0; index < cardButtons.Count; index++)
            {
                Button card = cardButtons[index];
                card.onClick.RemoveAllListeners();
                if (index >= visible.Count)
                {
                    card.gameObject.SetActive(false);
                    continue;
                }
                ModularContentRecord record = visible[index];
                card.gameObject.SetActive(true);
                cardImages[index].texture = thumbnails.TryGetValue(record.sourceId, out Texture2D texture)
                    ? texture
                    : Texture2D.grayTexture;
                cardLabels[index].text = record.chineseName + "\n" + Footprint(record);
                bool core = record.BehaviorKind == GridModuleBehaviorKind.Core;
                card.interactable = !core;
                if (!core)
                {
                    card.onClick.AddListener(() => BeginPlacement(record));
                }
            }
        }

        private void StartThumbnailWorker()
        {
            if (thumbnailWorker == null)
            {
                thumbnailWorker = StartCoroutine(LoadThumbnails());
            }
        }

        private IEnumerator LoadThumbnails()
        {
            foreach (string id in AirBuildCatalog.OrderedIds)
            {
                ModularContentRecord record = contentService.Catalog.Items.FirstOrDefault(item =>
                    item != null && string.Equals(item.neoXId, id, StringComparison.OrdinalIgnoreCase));
                if (record == null ||
                    !AirBuildCatalog.IsVisibleInPalette(record) ||
                    thumbnails.ContainsKey(record.sourceId))
                {
                    continue;
                }
                GameObject preview = null;
                yield return contentService.InstantiateAsync(record, thumbnailRoot, value => preview = value);
                if (preview == null)
                {
                    continue;
                }
                yield return null;
                Texture2D texture = RenderThumbnail(preview);
                Destroy(preview);
                if (texture != null)
                {
                    thumbnails[record.sourceId] = texture;
                    RefreshCards();
                }
            }
            thumbnailWorker = null;
        }

        private void BuildThumbnailStage()
        {
            GameObject stage = new GameObject("AirBuildThumbnailStage");
            stage.transform.SetParent(transform, false);
            stage.transform.position = new Vector3(10000f, 10000f, 10000f);
            SetLayerRecursively(stage, PreviewLayer);
            thumbnailRoot = new GameObject("AirBuildThumbnailRoot").transform;
            thumbnailRoot.SetParent(stage.transform, false);
            thumbnailRoot.gameObject.layer = PreviewLayer;

            GameObject cameraObject = new GameObject("AirBuildThumbnailCamera", typeof(Camera));
            cameraObject.transform.SetParent(stage.transform, false);
            thumbnailCamera = cameraObject.GetComponent<Camera>();
            thumbnailCamera.enabled = false;
            thumbnailCamera.orthographic = true;
            thumbnailCamera.clearFlags = CameraClearFlags.SolidColor;
            thumbnailCamera.backgroundColor = new Color(0.025f, 0.075f, 0.095f, 1f);
            thumbnailCamera.cullingMask = 1 << PreviewLayer;
            thumbnailCamera.nearClipPlane = 0.01f;
            thumbnailCamera.farClipPlane = 200f;

            GameObject lightObject = new GameObject("AirBuildThumbnailLight", typeof(Light));
            lightObject.transform.SetParent(stage.transform, false);
            thumbnailLight = lightObject.GetComponent<Light>();
            thumbnailLight.type = LightType.Directional;
            thumbnailLight.intensity = 1.45f;
            thumbnailLight.cullingMask = 1 << PreviewLayer;
            if (sceneCamera != null)
            {
                sceneCamera.cullingMask &= ~(1 << PreviewLayer);
            }
        }

        private Texture2D RenderThumbnail(GameObject preview)
        {
            SetLayerRecursively(preview, PreviewLayer);
            Renderer[] renderers = preview.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return null;
            }
            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }
            float radius = Mathf.Max(0.75f, bounds.extents.magnitude);
            thumbnailCamera.transform.position =
                bounds.center + new Vector3(radius * 1.35f, radius * 0.85f, -radius * 1.7f);
            thumbnailCamera.transform.LookAt(bounds.center);
            thumbnailCamera.orthographicSize = radius * 1.15f;
            thumbnailLight.transform.rotation = Quaternion.Euler(38f, -42f, 0f);

            RenderTexture target = RenderTexture.GetTemporary(256, 256, 24, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            thumbnailCamera.targetTexture = target;
            thumbnailCamera.Render();
            RenderTexture.active = target;
            Texture2D texture = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0f, 0f, 256f, 256f), 0, 0);
            texture.Apply();
            thumbnailCamera.targetTexture = null;
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            return texture;
        }

        private void SuppressLegacy()
        {
            foreach (GridBuildSlotController slots in FindObjectsOfType<GridBuildSlotController>(true))
            {
                slots.enabled = false;
                Transform oldMarkers = slots.transform.Find("LegalBuildSlots");
                if (oldMarkers != null) oldMarkers.gameObject.SetActive(false);
            }
            foreach (NeoXCleanCatalogOverlay overlay in FindObjectsOfType<NeoXCleanCatalogOverlay>(true))
            {
                overlay.gameObject.SetActive(false);
            }
            foreach (Transform item in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (item == null || !item.gameObject.scene.IsValid())
                {
                    continue;
                }
                if (item.name == "ModuleLibrary" || item.name == "NeoX目录 F2")
                {
                    item.gameObject.SetActive(false);
                }
            }
        }

        private void FocusAssembly()
        {
            GridLabCameraController rig = FindObjectOfType<GridLabCameraController>();
            if (rig == null || presenter == null)
            {
                return;
            }
            Renderer[] renderers = presenter.GetComponentsInChildren<Renderer>(true)
                .Where(item => item.enabled && item.gameObject.activeInHierarchy &&
                               !item.transform.IsChildOf(primaryGhostRoot) &&
                               !item.transform.IsChildOf(mirrorGhostRoot) &&
                               !item.transform.IsChildOf(hangarRoot.transform))
                .ToArray();
            if (renderers.Length == 0)
            {
                return;
            }
            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }
            Type type = rig.GetType();
            FieldInfo pan = type.GetField("panOffset", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo distance = type.GetField("distance", BindingFlags.Instance | BindingFlags.NonPublic);
            pan?.SetValue(rig, bounds.center - presenter.transform.position);
            distance?.SetValue(rig, Mathf.Clamp(bounds.extents.magnitude * 2.8f, 7f, 58f));
        }

        private static void ApplyPose(
            Transform target,
            GridModuleDefinition definition,
            GridModulePose pose)
        {
            target.localPosition = (Vector3)pose.Origin +
                                   (Vector3)GridOrientation.RotatedSize(
                                       definition.Footprint,
                                       pose.orientation) * 0.5f;
            target.localRotation = GridOrientation.Rotation(pose.orientation);
        }

        private void ApplyGhostMaterial(GameObject target)
        {
            foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true))
            {
                int count = Mathf.Max(1, renderer.sharedMaterials.Length);
                renderer.sharedMaterials = Enumerable.Repeat(ghostMaterial, count).ToArray();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            foreach (Collider collider in target.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
            }
        }

        private static void DisableGhostBehaviours(GameObject target)
        {
            foreach (Behaviour behaviour in target.GetComponentsInChildren<Behaviour>(true))
                behaviour.enabled = false;
        }

        private void CreateBoundaryLines(Transform parent)
        {
            Vector3 min = new Vector3(-8f, -8f, -8f);
            Vector3 max = new Vector3(8f, 8f, 8f);
            Vector3[] corners =
            {
                new Vector3(min.x,min.y,min.z), new Vector3(max.x,min.y,min.z),
                new Vector3(max.x,max.y,min.z), new Vector3(min.x,max.y,min.z),
                new Vector3(min.x,min.y,max.z), new Vector3(max.x,min.y,max.z),
                new Vector3(max.x,max.y,max.z), new Vector3(min.x,max.y,max.z)
            };
            int[,] edges =
            {
                {0,1},{1,2},{2,3},{3,0},{4,5},{5,6},{6,7},{7,4},
                {0,4},{1,5},{2,6},{3,7}
            };
            for (int index = 0; index < edges.GetLength(0); index++)
            {
                CreateLine(
                    parent,
                    corners[edges[index, 0]],
                    corners[edges[index, 1]],
                    boundaryMaterial,
                    0.045f);
            }
        }

        private void CreatePointLight(
            string lightName,
            Vector3 localPosition,
            Color color,
            float intensity)
        {
            GameObject target = new GameObject(lightName, typeof(Light));
            target.transform.SetParent(hangarRoot.transform, false);
            target.transform.localPosition = localPosition;
            Light light = target.GetComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = 30f;
            light.shadows = LightShadows.Soft;
        }

        private static void CreateLine(
            Transform parent,
            Vector3 start,
            Vector3 end,
            Material material,
            float width)
        {
            GameObject target = new GameObject("Line", typeof(LineRenderer));
            target.transform.SetParent(parent, false);
            LineRenderer line = target.GetComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = 2;
            line.SetPosition(0, start);
            line.SetPosition(1, end);
            line.startWidth = width;
            line.endWidth = width;
            line.sharedMaterial = material;
            line.numCapVertices = 2;
        }

        private static bool IsTestEnvironment(string path, Renderer renderer)
        {
            return path.Contains("AtmosphereTestZone") ||
                   path.Contains("GroundTestZone") ||
                   path.Contains("TargetAssembly") ||
                   path.Contains("FlightRing") ||
                   path.Contains("Runway") ||
                   path.Contains("ZeroGMarker") ||
                   path.Contains("NeoXSupportDrone") ||
                   renderer.sharedMaterial == null;
        }

        private static string HierarchyPath(Transform target)
        {
            string path = target.name;
            while (target.parent != null)
            {
                target = target.parent;
                path = target.name + "/" + path;
            }
            return path;
        }

        private static Vector3Int Cardinal(Vector3 value)
        {
            value = value.normalized;
            float x = Mathf.Abs(value.x);
            float y = Mathf.Abs(value.y);
            float z = Mathf.Abs(value.z);
            if (x >= y && x >= z) return new Vector3Int(value.x >= 0f ? 1 : -1, 0, 0);
            if (y >= x && y >= z) return new Vector3Int(0, value.y >= 0f ? 1 : -1, 0);
            return new Vector3Int(0, 0, value.z >= 0f ? 1 : -1);
        }

        private static void ClearChildren(Transform target)
        {
            if (target == null) return;
            for (int index = target.childCount - 1; index >= 0; index--)
            {
                Destroy(target.GetChild(index).gameObject);
            }
        }

        private static bool Contains(string source, string term)
        {
            return !string.IsNullOrEmpty(source) &&
                   source.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string Footprint(ModularContentRecord record)
        {
            int[] size = record?.footprint;
            return size != null && size.Length >= 3
                ? size[0] + "×" + size[1] + "×" + size[2]
                : "1×1×1";
        }

        private void RefreshCategoryColors()
        {
            for (int index = 0; index < categoryButtons.Count; index++)
            {
                categoryButtons[index].GetComponent<Image>().color =
                    Categories[index] == activeCategory
                        ? new Color(0.04f, 0.82f, 0.70f, 0.96f)
                        : new Color(0.09f, 0.22f, 0.28f, 0.96f);
            }
        }

        private GameObject CreatePanel(Transform parent, string panelName, Color color)
        {
            GameObject target = new GameObject(panelName, typeof(RectTransform), typeof(Image));
            target.transform.SetParent(parent, false);
            target.GetComponent<Image>().color = color;
            return target;
        }

        private Text CreateText(
            Transform parent,
            string value,
            int size,
            FontStyle style,
            Color color)
        {
            GameObject target = new GameObject("Text", typeof(RectTransform), typeof(Text));
            target.transform.SetParent(parent, false);
            Text text = target.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.text = value;
            text.alignment = TextAnchor.UpperLeft;
            return text;
        }

        private Button CreateButton(Transform parent, string label)
        {
            GameObject target = CreatePanel(
                parent,
                "Button_" + label,
                new Color(0.09f, 0.22f, 0.28f, 0.96f));
            Button button = target.AddComponent<Button>();
            Text text = CreateText(target.transform, label, 16, FontStyle.Bold, Color.white);
            Stretch(text.rectTransform);
            text.alignment = TextAnchor.MiddleCenter;
            return button;
        }

        private InputField CreateInput(Transform parent)
        {
            GameObject target = CreatePanel(
                parent,
                "Search",
                new Color(0.08f, 0.17f, 0.21f, 1f));
            InputField input = target.AddComponent<InputField>();
            Text value = CreateText(target.transform, string.Empty, 16, FontStyle.Normal, Color.white);
            value.rectTransform.offsetMin = new Vector2(12f, 4f);
            value.rectTransform.offsetMax = new Vector2(-12f, -4f);
            Text placeholder = CreateText(
                target.transform,
                "搜索中文名或 NeoX ID",
                16,
                FontStyle.Normal,
                new Color(0.55f, 0.66f, 0.70f, 1f));
            placeholder.rectTransform.offsetMin = new Vector2(12f, 4f);
            placeholder.rectTransform.offsetMax = new Vector2(-12f, -4f);
            input.textComponent = value;
            input.placeholder = placeholder;
            return input;
        }

        private static void SetRect(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void SetLayerRecursively(GameObject target, int layer)
        {
            target.layer = layer;
            foreach (Transform child in target.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }

        private void OnDestroy()
        {
            if (presenter != null)
            {
                presenter.Rebuilt -= HandlePresenterRebuilt;
            }
            if (controller != null)
            {
                controller.enabled = true;
            }
            SetBuildPresentation(false);
            foreach (Texture2D texture in thumbnails.Values)
            {
                if (texture != null) Destroy(texture);
            }
            if (ghostMaterial != null) Destroy(ghostMaterial);
            if (faceMaterial != null) Destroy(faceMaterial);
            if (boundaryMaterial != null) Destroy(boundaryMaterial);
            if (hangarMaterial != null) Destroy(hangarMaterial);
        }
    }
}
