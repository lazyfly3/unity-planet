using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ModularAssembly;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace UnityPlanet.ModularAssembly
{
    public sealed class GridBuildSlotController : MonoBehaviour
    {
        private const int OrientationCount = 24;

        private readonly List<GridBuildSlotMarker> markers = new List<GridBuildSlotMarker>();
        private static readonly IComparer<RaycastHit> RaycastHitDistanceComparer =
            Comparer<RaycastHit>.Create(
                (left, right) => left.distance.CompareTo(right.distance));
        private RaycastHit[] raycastHits = new RaycastHit[32];
        private ModularAssemblyLabController controller;
        private GridAssemblyPresenter presenter;
        private GridModuleDefinition activeDefinition;
        private Transform markerRoot;
        private Material markerMaterial;
        private FieldInfo legacyActiveDefinition;
        private Camera sceneCamera;
        private int orientation;
        private int lastRecordCount = -1;
        private bool lastMirror;
        private bool slotMode;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!ModularLabSceneProfile.AllowsBuildExperience(
                    SceneManager.GetActiveScene()) ||
                FindObjectOfType<GridBuildSlotController>() != null)
            {
                return;
            }

            new GameObject("GridBuildSlotController").AddComponent<GridBuildSlotController>();
        }

        private IEnumerator Start()
        {
            while (controller == null || presenter == null)
            {
                controller = FindObjectOfType<ModularAssemblyLabController>();
                presenter = FindObjectsOfType<GridAssemblyPresenter>()
                    .FirstOrDefault(item => item != null && item.name == "GridShip");
                yield return null;
            }

            sceneCamera = Camera.main;
            legacyActiveDefinition = typeof(ModularAssemblyLabController).GetField(
                "activeDefinition",
                BindingFlags.Instance | BindingFlags.NonPublic);
            markerRoot = new GameObject("LegalBuildSlots").transform;
            markerRoot.SetParent(presenter.transform, false);
            markerMaterial = CreateMarkerMaterial();
        }

        public void BeginPlacement(ModularContentRecord record)
        {
            if (record == null || controller?.Model == null)
            {
                return;
            }

            string moduleId = "neox@" + record.sourceId.Replace("@", "_");
            if (controller.Model.Definitions.TryGetValue(moduleId, out GridModuleDefinition definition))
            {
                BeginPlacement(definition);
            }
        }

        public void BeginPlacement(GridModuleDefinition definition)
        {
            if (definition == null || controller?.Model == null)
            {
                return;
            }

            activeDefinition = definition;
            orientation = 0;
            slotMode = true;
            controller.enabled = false;
            legacyActiveDefinition?.SetValue(controller, null);
            lastMirror = controller.MirrorEnabled;
            RefreshSlots();
        }

        private void Update()
        {
            if (controller == null || presenter == null)
            {
                return;
            }

            if (!slotMode)
            {
                GridModuleDefinition legacySelection =
                    legacyActiveDefinition?.GetValue(controller) as GridModuleDefinition;
                if (legacySelection != null)
                {
                    BeginPlacement(legacySelection);
                }
                return;
            }

            if (controller.IsFlying)
            {
                CancelPlacement();
                return;
            }

            int recordCount = controller.Model.Records.Count;
            if (recordCount != lastRecordCount || lastMirror != controller.MirrorEnabled)
            {
                lastMirror = controller.MirrorEnabled;
                RefreshSlots();
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                orientation = GridOrientation.NormalizeIndex(orientation + 1);
                RefreshSlots();
            }

            if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape))
            {
                CancelPlacement();
                return;
            }

            UpdateHoverAndPlacement();
        }

        private void UpdateHoverAndPlacement()
        {
            if (sceneCamera == null)
            {
                sceneCamera = Camera.main;
            }
            if (sceneCamera == null ||
                EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                SetHovered(null);
                return;
            }

            Ray ray = sceneCamera.ScreenPointToRay(Input.mousePosition);
            GridBuildSlotMarker hovered = null;
            int hitCount = QueryRaycastHits(
                ray,
                2000f,
                ~0,
                QueryTriggerInteraction.Collide);
            Array.Sort(
                raycastHits,
                0,
                hitCount,
                RaycastHitDistanceComparer);
            for (int index = 0; index < hitCount; index++)
            {
                hovered = raycastHits[index].collider
                    .GetComponentInParent<GridBuildSlotMarker>();
                if (hovered != null)
                {
                    break;
                }
            }
            SetHovered(hovered);

            if (hovered == null || !Input.GetMouseButtonDown(0))
            {
                return;
            }

            bool placed = controller.Model.TryPlace(
                activeDefinition.ModuleId,
                hovered.Pose,
                controller.MirrorEnabled,
                out _,
                out string error);
            if (!placed)
            {
                Debug.LogWarning("格位放置失败: " + error);
            }
            RefreshSlots();
        }

        private void RefreshSlots()
        {
            ClearMarkers();
            if (!slotMode || activeDefinition == null || controller?.Model == null || markerRoot == null)
            {
                return;
            }

            List<Vector3Int> localCells =
                GridOrientation.NormalizedCells(activeDefinition.Footprint, orientation);
            bool mirrored = controller.MirrorEnabled;

            for (int x = GridBuildBounds.Min; x < GridBuildBounds.MaxExclusive; x++)
            {
                for (int y = GridBuildBounds.Min; y < GridBuildBounds.MaxExclusive; y++)
                {
                    for (int z = GridBuildBounds.Min; z < GridBuildBounds.MaxExclusive; z++)
                    {
                        GridModulePose pose = new GridModulePose
                        {
                            x = x,
                            y = y,
                            z = z,
                            orientation = orientation
                        };
                        if (!InsideBuildVolume(localCells, pose.Origin, mirrored))
                        {
                            continue;
                        }
                        if (!controller.Model.CanPlacePose(
                                activeDefinition,
                                pose,
                                mirrored,
                                null,
                                out _))
                        {
                            continue;
                        }

                        CreateMarker(pose, localCells);
                    }
                }
            }

            lastRecordCount = controller.Model.Records.Count;
        }

        private int QueryRaycastHits(
            Ray ray,
            float maximumDistance,
            int layerMask,
            QueryTriggerInteraction triggerInteraction)
        {
            int count;
            while ((count = Physics.RaycastNonAlloc(
                       ray,
                       raycastHits,
                       maximumDistance,
                       layerMask,
                       triggerInteraction)) >= raycastHits.Length)
            {
                Array.Resize(ref raycastHits, raycastHits.Length * 2);
            }
            return count;
        }

        private static bool InsideBuildVolume(
            IEnumerable<Vector3Int> localCells,
            Vector3Int origin,
            bool mirrored)
        {
            foreach (Vector3Int local in localCells)
            {
                Vector3Int cell = origin + local;
                if (!Inside(cell))
                {
                    return false;
                }
                if (mirrored)
                {
                    Vector3Int mirrorCell = new Vector3Int(-cell.x - 1, cell.y, cell.z);
                    if (!Inside(mirrorCell))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private static bool Inside(Vector3Int cell)
        {
            return GridBuildBounds.Contains(cell);
        }

        private void CreateMarker(
            GridModulePose pose,
            IReadOnlyList<Vector3Int> localCells)
        {
            Vector3 center = Vector3.zero;
            for (int index = 0; index < localCells.Count; index++)
            {
                center += (Vector3)(pose.Origin + localCells[index]) + Vector3.one * 0.5f;
            }
            center /= Mathf.Max(1, localCells.Count);

            GameObject markerObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            markerObject.name = $"Slot_{pose.x}_{pose.y}_{pose.z}_{pose.orientation}";
            markerObject.transform.SetParent(markerRoot, false);
            markerObject.transform.localPosition = center;
            markerObject.transform.localRotation = Quaternion.identity;
            markerObject.transform.localScale = Vector3.one * 0.38f;
            markerObject.GetComponent<MeshRenderer>().sharedMaterial = markerMaterial;
            GridBuildSlotMarker marker = markerObject.AddComponent<GridBuildSlotMarker>();
            marker.Initialize(pose);
            markers.Add(marker);
        }

        private void SetHovered(GridBuildSlotMarker hovered)
        {
            for (int index = 0; index < markers.Count; index++)
            {
                markers[index].SetHovered(markers[index] == hovered);
            }
        }

        private void CancelPlacement()
        {
            slotMode = false;
            activeDefinition = null;
            ClearMarkers();
            if (controller != null)
            {
                controller.enabled = true;
            }
        }

        private void ClearMarkers()
        {
            for (int index = 0; index < markers.Count; index++)
            {
                if (markers[index] != null)
                {
                    Destroy(markers[index].gameObject);
                }
            }
            markers.Clear();
        }

        private static Material CreateMarkerMaterial()
        {
            Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Standard");
            Material material = new Material(shader)
            {
                name = "LegalGridSlot",
                color = new Color(0.05f, 0.95f, 0.75f, 0.20f)
            };
            return material;
        }

        private void OnGUI()
        {
            if (!slotMode || activeDefinition == null)
            {
                return;
            }

            GUIStyle style = new GUIStyle(GUI.skin.box)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
            GUI.Box(
                new Rect(Screen.width * 0.5f - 250f, 112f, 500f, 42f),
                $"点击发光格位安装 {activeDefinition.DisplayName}  |  R 旋转  |  右键取消",
                style);
        }

        private void OnDestroy()
        {
            ClearMarkers();
            if (markerMaterial != null)
            {
                Destroy(markerMaterial);
            }
            if (controller != null)
            {
                controller.enabled = true;
            }
        }
    }

    public sealed class GridBuildSlotMarker : MonoBehaviour
    {
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private MaterialPropertyBlock propertyBlock;
        private MeshRenderer meshRenderer;

        public GridModulePose Pose { get; private set; }

        public void Initialize(GridModulePose pose)
        {
            Pose = pose;
            propertyBlock = new MaterialPropertyBlock();
            meshRenderer = GetComponent<MeshRenderer>();
            SetHovered(false);
        }

        public void SetHovered(bool hovered)
        {
            if (meshRenderer == null || propertyBlock == null)
            {
                return;
            }

            propertyBlock.SetColor(
                ColorId,
                hovered
                    ? new Color(1f, 0.86f, 0.20f, 0.58f)
                    : new Color(0.05f, 0.95f, 0.75f, 0.20f));
            meshRenderer.SetPropertyBlock(propertyBlock);
        }
    }
}
