using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SpacecraftEditor
{
    public sealed class BuildModeController : MonoBehaviour
    {
        [SerializeField] private ShipAssembly assembly;
        [SerializeField] private CommandHistory history;
        [SerializeField] private Camera sceneCamera;
        [SerializeField] private Collider hullCollider;
        [SerializeField] private bool mirrorEnabled = true;
        [Header("Placement Snapping")]
        [SerializeField] private bool snappingEnabled = true;
        [SerializeField, Range(0f, 45f)] private float snapEnterAngle = 22f;
        [SerializeField, Range(0f, 45f)] private float snapReleaseAngle = 28f;
        [SerializeField, Range(0f, 1f)] private float centerSnapNormalizedRadius = 0.20f;
        [SerializeField, Min(0f)] private float centerSnapWorldDistance = 0.10f;
        [SerializeField, Min(0.05f)] private float surfaceGridSize = 0.25f;
        [SerializeField, Min(0f)] private float neighborAlignmentDistance = 0.10f;
        [SerializeField] private bool gridSnapRequiresControl = true;
        [SerializeField] private bool allowPartSurfacePlacement = true;

        private ShipPartDefinition placingDefinition;
        private GameObject preview;
        private bool previewValid;
        private Pose previewPose;
        private Pose mirrorPreviewPose;
        private float previewScale = 1f;
        private float previewTwist;
        private bool buildMode = true;
        private bool draggingExisting;
        private bool rotatingSelection;
        private bool movedDuringDrag;
        private SpacecraftPart selectedPart;
        private PlacementSnapAxis activeSnapAxis;
        private bool centerSnapped;
        private bool snapTemporarilyDisabled;
        private bool keyboardCaptureActive;
        private Vector3 hullLocalHalfExtents = new Vector3(1.5f, 1.1f, 3f);

        private const float PlacementSurfaceOffset = 0.025f;

        public event Action<SpacecraftPart> SelectionChanged;
        public event Action<bool> MirrorChanged;
        public event Action SnapStateChanged;
        public ShipAssembly Assembly => assembly;
        public SpacecraftPart SelectedPart => selectedPart;
        public bool MirrorEnabled => mirrorEnabled;
        public bool IsBuildMode => buildMode;
        public PlacementSnapAxis ActiveSnapAxis => activeSnapAxis;
        public bool IsCenterSnapped => centerSnapped;
        public bool IsSnapTemporarilyDisabled => snapTemporarilyDisabled;
        public bool IsPlacementPreviewValid => previewValid;
        public bool IsKeyboardCaptureActive => keyboardCaptureActive;
        public Collider HullCollider => hullCollider;
        public Vector3 HullLocalHalfExtents => hullLocalHalfExtents;

        public void Configure(ShipAssembly targetAssembly, CommandHistory commandHistory, Camera camera, Collider hull)
        {
            assembly = targetAssembly;
            history = commandHistory;
            sceneCamera = camera;
            hullCollider = hull;
            hullLocalHalfExtents = CalculateHullLocalHalfExtents();
            if (history != null)
                history.Restored += HandleHistoryRestored;
        }

        public void SetHullCollider(Collider value)
        {
            CancelPlacement();
            draggingExisting = false;
            SelectPart(null);
            hullCollider = value;
            hullLocalHalfExtents = CalculateHullLocalHalfExtents();
            SetSnapState(PlacementSnapAxis.None, false, false);
        }

        private void OnDestroy()
        {
            if (history != null)
                history.Restored -= HandleHistoryRestored;
        }

        private void Update()
        {
            if (!buildMode || assembly == null || sceneCamera == null)
                return;

            if (keyboardCaptureActive)
                return;

            HandleHistoryShortcuts();
            HandleSelectedPartShortcuts();

            if (placingDefinition != null)
            {
                UpdatePlacement(Input.mousePosition);
                return;
            }

            var overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (Input.GetMouseButtonDown(0) && !overUi)
                BeginExistingPartInteraction(Input.mousePosition);

            if (draggingExisting && Input.GetMouseButton(0))
                DragSelectedPart(Input.mousePosition);

            if (draggingExisting && Input.GetMouseButtonUp(0))
            {
                draggingExisting = false;
                if (movedDuringDrag)
                    history?.Record();
                movedDuringDrag = false;
                SetSnapState(PlacementSnapAxis.None, false, false);
            }
        }

        public void SetBuildMode(bool value)
        {
            buildMode = value;
            if (!value)
            {
                CancelPlacement();
                draggingExisting = false;
                keyboardCaptureActive = false;
                SelectPart(null);
            }
            enabled = value;
        }

        public void SetMirrorEnabled(bool value)
        {
            mirrorEnabled = value;
            MirrorChanged?.Invoke(value);
            if (placingDefinition != null)
                UpdatePlacement(Input.mousePosition);
        }

        public void ToggleMirror()
        {
            SetMirrorEnabled(!mirrorEnabled);
        }

        public void BeginPlacement(ShipPartDefinition definition)
        {
            if (!buildMode || definition == null || definition.Prefab == null)
                return;
            CancelPlacement();
            SetSnapState(PlacementSnapAxis.None, false, false);
            placingDefinition = definition;
            previewScale = definition.IsScalable ? 1f : definition.FixedScale;
            previewTwist = 0f;
            preview = Instantiate(definition.Prefab);
            preview.name = "PlacementPreview";
            foreach (var collider in preview.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            foreach (SpacecraftPart part in preview.GetComponentsInChildren<SpacecraftPart>(true))
                part.enabled = false;
            foreach (var particles in preview.GetComponentsInChildren<ParticleSystem>(true))
                particles.gameObject.SetActive(false);
            SetPreviewTint(false, false);
        }

        public void UpdatePlacement(Vector2 screenPosition)
        {
            if (placingDefinition == null || preview == null)
                return;
            Pose pose;
            if (!TryGetHullPose(
                    screenPosition,
                    previewTwist,
                    placingDefinition,
                    previewScale,
                    out pose))
            {
                preview.SetActive(false);
                previewValid = false;
                return;
            }

            preview.SetActive(true);
            previewPose = pose;
            preview.transform.SetPositionAndRotation(pose.position, pose.rotation);
            preview.transform.localScale = Vector3.one * previewScale;
            previewValid = IsPoseValid(placingDefinition, pose, previewScale, null, null);

            if (mirrorEnabled)
            {
                mirrorPreviewPose = ReflectPose(pose, placingDefinition, previewTwist);
                var localX = assembly.transform.InverseTransformPoint(pose.position).x;
                if (Mathf.Abs(localX) > 0.08f)
                    previewValid &= IsPoseValid(placingDefinition, mirrorPreviewPose, previewScale, null, null);
            }
            SetPreviewTint(previewValid, activeSnapAxis != PlacementSnapAxis.None);
        }

        public void EndPlacement(Vector2 screenPosition)
        {
            if (placingDefinition == null)
                return;
            UpdatePlacement(screenPosition);
            if (previewValid)
                CommitPlacement();
            CancelPlacement();
        }

        public void CancelPlacement()
        {
            placingDefinition = null;
            if (preview != null)
                Destroy(preview);
            preview = null;
            previewValid = false;
            SetSnapState(PlacementSnapAxis.None, false, false);
        }

        public void SetSelectedScale(float value)
        {
            if (selectedPart == null || selectedPart.Definition == null || !selectedPart.Definition.IsScalable)
                return;
            selectedPart.SetUniformScale(value);
            SyncMirror(selectedPart);
            assembly.Recalculate();
        }

        public void SetKeyboardCaptureActive(bool value)
        {
            keyboardCaptureActive = value && buildMode;
        }

        public bool SetSelectedActivationKey(KeyCode key)
        {
            return SetPartActivationKey(selectedPart as ThrusterPart, key);
        }

        public bool SetPartActivationKey(ThrusterPart part, KeyCode key)
        {
            if (part == null || assembly == null || part.transform.parent != assembly.PartsRoot ||
                (key != KeyCode.None && !ThrusterKeyBinding.IsBindable(key)))
                return false;

            var mate = assembly.FindMirrorMate(part) as ThrusterPart;
            if (part.ActivationKey == key && (mate == null || mate.ActivationKey == key))
                return true;

            part.SetActivationKey(key);
            if (mate != null)
                mate.SetActivationKey(key);
            history?.Record();
            if (selectedPart == part || selectedPart == mate)
                SelectionChanged?.Invoke(selectedPart);
            return true;
        }

        public bool ApplySelectedMaterial(string materialId)
        {
            if (selectedPart == null || string.IsNullOrEmpty(materialId))
                return false;
            selectedPart.ApplyMaterial(materialId);
            SpacecraftPart mate = assembly.FindMirrorMate(selectedPart);
            if (mate != null)
                mate.ApplyMaterial(materialId);
            history?.Record();
            SelectionChanged?.Invoke(selectedPart);
            return true;
        }

        public bool SetSelectedWeaponGroup(int group)
        {
            var weapon = selectedPart as WeaponPart;
            if (weapon == null)
                return false;
            weapon.SetFireGroup(group);
            var mate = assembly.FindMirrorMate(weapon) as WeaponPart;
            if (mate != null)
                mate.SetFireGroup(group);
            history?.Record();
            SelectionChanged?.Invoke(selectedPart);
            return true;
        }

        public void CommitScale()
        {
            if (selectedPart != null)
                history?.Record();
        }

        public void DeleteSelection()
        {
            if (selectedPart == null)
                return;
            var deleting = selectedPart;
            SelectPart(null);
            assembly.RemovePartAndMirror(deleting);
            history?.Record();
        }

        public void Undo()
        {
            SelectPart(null);
            history?.Undo();
        }

        public void Redo()
        {
            SelectPart(null);
            history?.Redo();
        }

        private void CommitPlacement()
        {
            var localPosition = assembly.transform.InverseTransformPoint(previewPose.position);
            var localRotation = Quaternion.Inverse(assembly.transform.rotation) * previewPose.rotation;
            var groupId = string.Empty;
            var localX = localPosition.x;
            if (mirrorEnabled && Mathf.Abs(localX) > 0.08f)
                groupId = Guid.NewGuid().ToString("N");

            var placed = assembly.AddGenericPart(placingDefinition, localPosition, localRotation, previewScale, null, groupId);
            if (!string.IsNullOrEmpty(groupId))
            {
                var mirrorLocalPosition = assembly.transform.InverseTransformPoint(mirrorPreviewPose.position);
                var mirrorLocalRotation = Quaternion.Inverse(assembly.transform.rotation) * mirrorPreviewPose.rotation;
                assembly.AddGenericPart(placingDefinition, mirrorLocalPosition, mirrorLocalRotation, previewScale, null, groupId);
            }
            history?.Record();
            SelectPart(placed);
        }

        private void BeginExistingPartInteraction(Vector2 screenPosition)
        {
            RaycastHit hit;
            if (!Physics.Raycast(sceneCamera.ScreenPointToRay(screenPosition), out hit, 200f))
            {
                SelectPart(null);
                return;
            }

            var part = hit.collider.GetComponentInParent<SpacecraftPart>();
            if (part == null || part.transform.parent != assembly.PartsRoot)
            {
                SelectPart(null);
                return;
            }

            SelectPart(part);
            draggingExisting = true;
            movedDuringDrag = false;
            SetSnapState(PlacementSnapAxis.None, false, false);
            previewTwist = CalculatePlacementTwist(
                part.Definition,
                part.transform.rotation,
                part.transform.localPosition);
        }

        private void DragSelectedPart(Vector2 screenPosition)
        {
            if (selectedPart == null)
                return;
            Pose pose;
            if (!TryGetHullPose(
                    screenPosition,
                    previewTwist,
                    selectedPart.Definition,
                    selectedPart.UniformScale,
                    out pose))
                return;
            var mate = assembly.FindMirrorMate(selectedPart);
            if (!IsPoseValid(selectedPart.Definition, pose, selectedPart.UniformScale, selectedPart, mate))
                return;
            if (mate != null)
            {
                var reflected = ReflectPose(pose, selectedPart.Definition, previewTwist);
                if (!IsPoseValid(selectedPart.Definition, reflected, selectedPart.UniformScale, selectedPart, mate))
                    return;
            }

            selectedPart.transform.localPosition = assembly.transform.InverseTransformPoint(pose.position);
            selectedPart.transform.localRotation = Quaternion.Inverse(assembly.transform.rotation) * pose.rotation;
            SyncMirror(selectedPart);
            assembly.Recalculate();
            movedDuringDrag = true;
        }

        private void HandleSelectedPartShortcuts()
        {
            if (selectedPart == null)
                return;

            if (Input.GetKeyDown(KeyCode.Delete))
            {
                DeleteSelection();
                return;
            }

            var direction = 0f;
            if (Input.GetKey(KeyCode.Q)) direction += 1f;
            if (Input.GetKey(KeyCode.E)) direction -= 1f;
            if (Mathf.Abs(direction) > 0.01f && !draggingExisting)
            {
                if (!rotatingSelection)
                    rotatingSelection = true;
                var delta = direction * 75f * Time.deltaTime;
                previewTwist += delta;
                selectedPart.transform.Rotate(0f, 0f, delta, Space.Self);
                SyncMirror(selectedPart);
                assembly.Recalculate();
            }
            else if (rotatingSelection)
            {
                rotatingSelection = false;
                history?.Record();
            }
        }

        private void HandleHistoryShortcuts()
        {
            if (!Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl))
                return;
            if (Input.GetKeyDown(KeyCode.Z)) Undo();
            if (Input.GetKeyDown(KeyCode.Y)) Redo();
        }

        private bool TryGetHullPose(
            Vector2 screenPosition,
            float twist,
            ShipPartDefinition definition,
            float scale,
            out Pose pose)
        {
            pose = default(Pose);
            var hits = Physics.RaycastAll(sceneCamera.ScreenPointToRay(screenPosition), 300f);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            foreach (var hit in hits)
            {
                bool hitHull =
                    hit.collider == hullCollider ||
                    hit.collider.transform.IsChildOf(hullCollider.transform);
                SpacecraftPart hitPart = hit.collider.GetComponentInParent<SpacecraftPart>();
                SpacecraftPart mirrorMate =
                    selectedPart == null ? null : assembly.FindMirrorMate(selectedPart);
                bool hitPlaceablePart =
                    allowPartSurfacePlacement &&
                    hitPart != null &&
                    hitPart != selectedPart &&
                    hitPart != mirrorMate &&
                    hitPart.transform.IsChildOf(assembly.PartsRoot);
                if (!hitHull && !hitPlaceablePart)
                    continue;

                var altHeld = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
                var localHitPoint = assembly.transform.InverseTransformPoint(hit.point);
                if (definition != null && definition.PlacementMode == SpacecraftPartPlacementMode.LateralWing)
                    return TryBuildLateralWingPose(hit, localHitPoint, twist, out pose);
                Vector3 localHitNormal =
                    assembly.transform.InverseTransformDirection(hit.normal).normalized;
                PlacementSnapAxis snapAxis = ResolveSnapAxis(
                    localHitNormal,
                    activeSnapAxis,
                    snappingEnabled && !altHeld,
                    snapEnterAngle,
                    snapReleaseAngle);

                Vector3 surfacePoint = hit.point;
                Vector3 surfaceNormal = hit.normal.normalized;
                bool isCenterSnap = false;
                PlacementAlignmentKind alignment = PlacementAlignmentKind.None;
                Quaternion surfaceRotation = BuildSurfaceRotation(surfaceNormal, twist);
                if (snapAxis != PlacementSnapAxis.None &&
                    snappingEnabled &&
                    !altHeld)
                {
                    Vector3 snappedLocalPoint = localHitPoint;
                    bool controlHeld =
                        Input.GetKey(KeyCode.LeftControl) ||
                        Input.GetKey(KeyCode.RightControl);
                    if (!gridSnapRequiresControl || controlHeld)
                    {
                        snappedLocalPoint =
                            SnapTangentialToGrid(
                                snappedLocalPoint,
                                snapAxis,
                                surfaceGridSize);
                        alignment |= PlacementAlignmentKind.Grid;
                    }

                    SpacecraftPlacementSolver.SurfaceTangents(
                        snapAxis,
                        out Vector3 tangentA,
                        out Vector3 tangentB);
                    float distanceFromCenter = new Vector2(
                        Vector3.Dot(localHitPoint, tangentA),
                        Vector3.Dot(localHitPoint, tangentB)).magnitude;
                    isCenterSnap =
                        centerSnapWorldDistance > 0f &&
                        distanceFromCenter <= centerSnapWorldDistance;
                    if (isCenterSnap)
                    {
                        snappedLocalPoint =
                            ClearTangentialCoordinates(
                                snappedLocalPoint,
                                snapAxis);
                        alignment |= PlacementAlignmentKind.Center;
                    }

                    float provisionalOffset =
                        SpacecraftPlacementSolver.CalculateMountOffset(
                            definition,
                            surfaceRotation,
                            surfaceNormal,
                            scale,
                            PlacementSurfaceOffset);
                    Quaternion localRotation =
                        Quaternion.Inverse(assembly.transform.rotation) *
                        surfaceRotation;
                    alignment |= SpacecraftPlacementSolver.AlignToNeighbors(
                        assembly.transform,
                        assembly.Parts,
                        definition,
                        scale,
                        snapAxis,
                        localRotation,
                        provisionalOffset,
                        neighborAlignmentDistance,
                        ref snappedLocalPoint,
                        selectedPart,
                        mirrorMate);

                    if (alignment != PlacementAlignmentKind.None)
                    {
                        if (hitHull)
                        {
                            if (TryGetHullSurfacePoint(
                                    snappedLocalPoint,
                                    snapAxis,
                                    out Vector3 hullSurfacePoint,
                                    out Vector3 hullSurfaceNormal))
                            {
                                surfacePoint = hullSurfacePoint;
                                surfaceNormal = hullSurfaceNormal;
                            }
                        }
                        else
                        {
                            Plane surfacePlane = new Plane(hit.normal, hit.point);
                            surfacePoint = surfacePlane.ClosestPointOnPlane(
                                assembly.transform.TransformPoint(snappedLocalPoint));
                        }
                        surfaceRotation =
                            BuildSurfaceRotation(surfaceNormal, twist);
                    }
                }

                float mountOffset =
                    SpacecraftPlacementSolver.CalculateMountOffset(
                        definition,
                        surfaceRotation,
                        surfaceNormal,
                        scale,
                        PlacementSurfaceOffset);
                pose = new Pose(
                    surfacePoint + surfaceNormal * mountOffset,
                    surfaceRotation);
                SetSnapState(
                    alignment == PlacementAlignmentKind.None
                        ? PlacementSnapAxis.None
                        : snapAxis,
                    isCenterSnap,
                    altHeld);
                return true;
            }
            SetSnapState(PlacementSnapAxis.None, false, false);
            return false;
        }

        private bool TryBuildLateralWingPose(
            RaycastHit originalHit,
            Vector3 localHitPoint,
            float twist,
            out Pose pose)
        {
            float side = ResolveLateralSide(localHitPoint, originalHit.normal);
            float clampedY = Mathf.Clamp(
                localHitPoint.y,
                -hullLocalHalfExtents.y * 0.65f,
                hullLocalHalfExtents.y * 0.65f);
            float clampedZ = Mathf.Clamp(
                localHitPoint.z,
                -hullLocalHalfExtents.z * 0.92f,
                hullLocalHalfExtents.z * 0.92f);

            Vector3 surfacePoint;
            if (!TryGetLateralSurfacePoint(side, clampedY, clampedZ, out surfacePoint))
                surfacePoint = originalHit.point;

            Vector3 localPosition = assembly.transform.InverseTransformPoint(surfacePoint);
            localPosition.x += side * PlacementSurfaceOffset;
            Quaternion localRotation = BuildLateralWingLocalRotation(side, twist);
            pose = new Pose(
                assembly.transform.TransformPoint(localPosition),
                assembly.transform.rotation * localRotation);
            SetSnapState(side > 0f ? PlacementSnapAxis.Right : PlacementSnapAxis.Left, false, false);
            return true;
        }

        private float ResolveLateralSide(Vector3 localHitPoint, Vector3 worldNormal)
        {
            if (Mathf.Abs(localHitPoint.x) > hullLocalHalfExtents.x * 0.05f)
                return Mathf.Sign(localHitPoint.x);
            Vector3 localNormal = assembly.transform.InverseTransformDirection(worldNormal);
            if (Mathf.Abs(localNormal.x) > 0.05f)
                return Mathf.Sign(localNormal.x);
            return 1f;
        }

        private bool TryGetLateralSurfacePoint(float side, float localY, float localZ, out Vector3 surfacePoint)
        {
            surfacePoint = Vector3.zero;
            if (hullCollider == null || assembly == null)
                return false;

            float outsideX = side * (hullLocalHalfExtents.x + 2f);
            Vector3 rayOrigin = assembly.transform.TransformPoint(new Vector3(outsideX, localY, localZ));
            Vector3 rayDirection = assembly.transform.TransformDirection(Vector3.left * side).normalized;
            float distance = hullLocalHalfExtents.x * 2f + 4f;
            RaycastHit hit;
            if (!hullCollider.Raycast(new Ray(rayOrigin, rayDirection), out hit, distance))
                return false;
            surfacePoint = hit.point;
            return true;
        }

        public static Quaternion BuildLateralWingLocalRotation(float side, float twist)
        {
            Vector3 planeNormal = side >= 0f ? Vector3.up : Vector3.down;
            Quaternion baseRotation = Quaternion.LookRotation(planeNormal, Vector3.forward);
            return Quaternion.AngleAxis(twist, planeNormal) * baseRotation;
        }

        public static PlacementSnapAxis ResolveSnapAxis(
            Vector3 localRadialDirection,
            PlacementSnapAxis currentAxis,
            bool snappingAllowed,
            float enterAngle = 22f,
            float releaseAngle = 28f)
        {
            return SpacecraftPlacementSolver.ResolveAxis(
                localRadialDirection,
                currentAxis,
                snappingAllowed,
                enterAngle,
                releaseAngle);
        }

        public static Vector3 GetSnapAxisDirection(PlacementSnapAxis axis)
        {
            return SpacecraftPlacementSolver.AxisDirection(axis);
        }

        public static bool IsWithinCenterSnap(
            Vector3 localPoint,
            PlacementSnapAxis axis,
            Vector3 hullHalfExtents,
            float normalizedRadius = 0.20f)
        {
            return SpacecraftPlacementSolver.IsWithinCenter(
                localPoint,
                axis,
                hullHalfExtents,
                normalizedRadius);
        }

        private bool TryGetHullSurfacePoint(
            Vector3 localPoint,
            PlacementSnapAxis axis,
            out Vector3 surfacePoint,
            out Vector3 surfaceNormal)
        {
            surfacePoint = Vector3.zero;
            surfaceNormal = Vector3.zero;
            if (hullCollider == null || assembly == null)
                return false;

            Vector3 localAxis = GetSnapAxisDirection(axis);
            float distance = hullCollider.bounds.extents.magnitude + 3f;
            Vector3 localOrigin = localPoint + localAxis * distance;
            Vector3 worldOrigin = assembly.transform.TransformPoint(localOrigin);
            Vector3 worldDirection =
                assembly.transform.TransformDirection(-localAxis).normalized;
            var ray = new Ray(worldOrigin, worldDirection);
            RaycastHit hit;
            if (!hullCollider.Raycast(ray, out hit, distance * 2f))
                return false;
            surfacePoint = hit.point;
            surfaceNormal = hit.normal.normalized;
            return true;
        }

        public static Vector3 SnapTangentialToGrid(
            Vector3 localPoint,
            PlacementSnapAxis axis,
            float gridSize)
        {
            return SpacecraftPlacementSolver.SnapToGrid(
                localPoint,
                axis,
                gridSize);
        }

        public static Vector3 ClearTangentialCoordinates(
            Vector3 localPoint,
            PlacementSnapAxis axis)
        {
            return SpacecraftPlacementSolver.ClearTangents(
                localPoint,
                axis);
        }

        private Vector3 CalculateHullLocalHalfExtents()
        {
            if (assembly == null || hullCollider == null)
                return new Vector3(1.5f, 1.1f, 3f);

            var meshCollider = hullCollider as MeshCollider;
            if (meshCollider != null && meshCollider.sharedMesh != null)
            {
                var bounds = meshCollider.sharedMesh.bounds;
                var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
                var max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
                for (var x = 0; x < 2; x++)
                for (var y = 0; y < 2; y++)
                for (var z = 0; z < 2; z++)
                {
                    var meshPoint = new Vector3(
                        x == 0 ? bounds.min.x : bounds.max.x,
                        y == 0 ? bounds.min.y : bounds.max.y,
                        z == 0 ? bounds.min.z : bounds.max.z);
                    var localPoint = assembly.transform.InverseTransformPoint(hullCollider.transform.TransformPoint(meshPoint));
                    min = Vector3.Min(min, localPoint);
                    max = Vector3.Max(max, localPoint);
                }
                return (max - min) * 0.5f;
            }

            return new Vector3(1.5f, 1.1f, 3f);
        }

        private void SetSnapState(PlacementSnapAxis axis, bool isCenterSnap, bool temporarilyDisabled)
        {
            if (activeSnapAxis == axis && centerSnapped == isCenterSnap && snapTemporarilyDisabled == temporarilyDisabled)
                return;
            activeSnapAxis = axis;
            centerSnapped = isCenterSnap;
            snapTemporarilyDisabled = temporarilyDisabled;
            SnapStateChanged?.Invoke();
        }

        private Quaternion BuildSurfaceRotation(Vector3 normal, float twist)
        {
            var tangentUp = Vector3.ProjectOnPlane(assembly.transform.forward, normal).normalized;
            if (tangentUp.sqrMagnitude < 0.001f)
                tangentUp = Vector3.ProjectOnPlane(assembly.transform.up, normal).normalized;
            var baseRotation = Quaternion.LookRotation(normal, tangentUp);
            return Quaternion.AngleAxis(twist, normal) * baseRotation;
        }

        private float CalculateTwist(Quaternion rotation, Vector3 normal)
        {
            var baseRotation = BuildSurfaceRotation(normal, 0f);
            return Vector3.SignedAngle(baseRotation * Vector3.up, rotation * Vector3.up, normal);
        }

        private Pose ReflectPose(Pose worldPose, ShipPartDefinition definition, float twist)
        {
            var localPosition = assembly.transform.InverseTransformPoint(worldPose.position);
            localPosition.x = -localPosition.x;
            if (definition != null && definition.PlacementMode == SpacecraftPartPlacementMode.LateralWing)
            {
                float side = localPosition.x >= 0f ? 1f : -1f;
                Quaternion wingRotation = BuildLateralWingLocalRotation(side, twist);
                return new Pose(
                    assembly.transform.TransformPoint(localPosition),
                    assembly.transform.rotation * wingRotation);
            }

            var localRotation = Quaternion.Inverse(assembly.transform.rotation) * worldPose.rotation;
            localRotation = ReflectLocalRotation(localRotation);
            return new Pose(assembly.transform.TransformPoint(localPosition), assembly.transform.rotation * localRotation);
        }

        public static Quaternion ReflectLocalRotation(Quaternion rotation)
        {
            var forward = rotation * Vector3.forward;
            var up = rotation * Vector3.up;
            forward.x = -forward.x;
            up.x = -up.x;
            return Quaternion.LookRotation(forward, up);
        }

        private void SyncMirror(SpacecraftPart source)
        {
            var mate = assembly.FindMirrorMate(source);
            if (mate == null)
                return;
            var localPosition = source.transform.localPosition;
            localPosition.x = -localPosition.x;
            mate.transform.localPosition = localPosition;
            if (source.Definition != null &&
                source.Definition.PlacementMode == SpacecraftPartPlacementMode.LateralWing)
            {
                float sourceTwist = CalculatePlacementTwist(
                    source.Definition,
                    source.transform.rotation,
                    source.transform.localPosition);
                float mateSide = localPosition.x >= 0f ? 1f : -1f;
                mate.transform.localRotation = BuildLateralWingLocalRotation(mateSide, sourceTwist);
            }
            else
            {
                mate.transform.localRotation = ReflectLocalRotation(source.transform.localRotation);
            }
            mate.SetUniformScale(source.UniformScale);
            mate.ApplyMaterial(source.MaterialId);
            if (source is ThrusterPart sourceThruster && mate is ThrusterPart mateThruster)
                mateThruster.SetActivationKey(sourceThruster.ActivationKey);
            if (source is WeaponPart sourceWeapon && mate is WeaponPart mateWeapon)
                mateWeapon.SetFireGroup(sourceWeapon.FireGroup);
        }

        private bool IsPoseValid(ShipPartDefinition definition, Pose pose, float scale, SpacecraftPart ignoreA, SpacecraftPart ignoreB)
        {
            return !SpacecraftPlacementSolver.HasBlockingPartOverlap(
                assembly,
                definition,
                pose,
                scale,
                0.012f,
                ignoreA,
                ignoreB);
        }

        private void SelectPart(SpacecraftPart value)
        {
            if (selectedPart == value)
                return;
            SetSelectionTint(selectedPart, false);
            selectedPart = value;
            SetSelectionTint(selectedPart, true);
            if (selectedPart != null)
                previewTwist = CalculatePlacementTwist(
                    selectedPart.Definition,
                    selectedPart.transform.rotation,
                    selectedPart.transform.localPosition);
            SelectionChanged?.Invoke(selectedPart);
        }

        private float CalculatePlacementTwist(
            ShipPartDefinition definition,
            Quaternion worldRotation,
            Vector3 localPosition)
        {
            if (definition == null || definition.PlacementMode != SpacecraftPartPlacementMode.LateralWing)
                return CalculateTwist(worldRotation, worldRotation * Vector3.forward);

            float side = localPosition.x >= 0f ? 1f : -1f;
            Quaternion baseRotation = BuildLateralWingLocalRotation(side, 0f);
            Quaternion localRotation = Quaternion.Inverse(assembly.transform.rotation) * worldRotation;
            Vector3 axis = baseRotation * Vector3.forward;
            return Vector3.SignedAngle(baseRotation * Vector3.up, localRotation * Vector3.up, axis);
        }

        private static void SetSelectionTint(SpacecraftPart part, bool selected)
        {
            if (part == null)
                return;
            foreach (var renderer in part.GetComponentsInChildren<Renderer>(true))
            {
                if (!selected)
                {
                    renderer.SetPropertyBlock(null);
                    continue;
                }
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                block.SetColor("_EmissionColor", new Color(0.05f, 0.8f, 1f, 1f) * 1.6f);
                renderer.SetPropertyBlock(block);
            }
        }

        private void SetPreviewTint(bool valid, bool snapped)
        {
            if (preview == null)
                return;
            var color = !valid
                ? new Color(1f, 0.18f, 0.18f, 1f)
                : snapped
                    ? new Color(0.10f, 0.72f, 1f, 1f)
                    : new Color(0.15f, 1f, 0.65f, 1f);
            foreach (var renderer in preview.GetComponentsInChildren<Renderer>(true))
            {
                var block = new MaterialPropertyBlock();
                block.SetColor("_Color", color);
                block.SetColor("_EmissionColor", color * 1.8f);
                renderer.SetPropertyBlock(block);
            }
        }

        private void HandleHistoryRestored()
        {
            SelectPart(null);
        }
    }
}
