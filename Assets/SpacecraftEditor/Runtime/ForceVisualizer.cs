using System.Collections.Generic;
using UnityEngine;

namespace SpacecraftEditor
{
    public sealed class ForceVisualizer : MonoBehaviour
    {
        private static readonly Color ThrustColor = new Color(0.25f, 1f, 0.45f, 1f);
        private static readonly Color ExhaustColor = new Color(1f, 0.48f, 0.08f, 1f);
        private static readonly Color TorqueColor = new Color(0.85f, 0.28f, 1f, 1f);
        private static readonly Color ForwardColor = new Color(0.18f, 0.58f, 1f, 1f);

        [SerializeField] private ShipAssembly assembly;
        [SerializeField] private BuildModeController buildController;
        [SerializeField] private Transform centerOfMassMarker;

        private readonly List<VectorArrow> arrows = new List<VectorArrow>();
        private VectorArrow selectedThrustArrow;
        private VectorArrow selectedExhaustArrow;
        private VectorArrow netForceArrow;
        private VectorArrow torqueArrow;
        private VectorArrow shipForwardArrow;
        private Mesh arrowHeadMesh;
        private bool visible = true;

        public bool IsVisible => visible;

        public void Configure(ShipAssembly targetAssembly, BuildModeController controller, Transform marker)
        {
            assembly = targetAssembly;
            buildController = controller;
            centerOfMassMarker = marker;
            ClearArrows();

            arrowHeadMesh = CreateArrowHeadMesh();
            selectedThrustArrow = CreateArrow("SelectedThrust", ThrustColor, 0.05f, false);
            selectedExhaustArrow = CreateArrow("SelectedExhaust", ExhaustColor, 0.04f, true);
            netForceArrow = CreateArrow("NetForce", ThrustColor, 0.065f, false);
            torqueArrow = CreateArrow("NetTorque", TorqueColor, 0.05f, false);
            shipForwardArrow = CreateArrow("ShipForwardPositiveZ", ForwardColor, 0.045f, false);
        }

        public void ToggleVisible()
        {
            SetVisible(!visible);
        }

        public void SetVisible(bool value)
        {
            visible = value;
            gameObject.SetActive(value);
        }

        private void LateUpdate()
        {
            if (!visible || assembly == null)
                return;

            var metrics = assembly.Metrics;
            var worldCenter = assembly.transform.TransformPoint(metrics.localCenterOfMass);
            if (centerOfMassMarker != null)
            {
                centerOfMassMarker.localPosition = metrics.localCenterOfMass;
                centerOfMassMarker.gameObject.SetActive(true);
            }

            DrawVector(netForceArrow, worldCenter, assembly.transform.TransformDirection(metrics.localResultantForce), 0.012f, 4.5f);
            DrawVector(torqueArrow, worldCenter, assembly.transform.TransformDirection(metrics.localResultantTorque), 0.006f, 3.5f);
            DrawVector(shipForwardArrow, assembly.transform.TransformPoint(new Vector3(0f, 0f, 3.1f)), assembly.transform.forward, 1.35f, 1.35f);

            var selected = buildController == null ? null : buildController.SelectedPart as ThrusterPart;
            if (selected != null)
            {
                DrawVector(selectedThrustArrow, selected.transform.position, selected.ThrustDirection * selected.ActualThrust, 0.012f, 4f);
                DrawVector(selectedExhaustArrow, selected.ExhaustOrigin, selected.ExhaustDirection * selected.ActualThrust, 0.012f, 4f);
            }
            else
            {
                selectedThrustArrow?.Hide();
                selectedExhaustArrow?.Hide();
            }
        }

        private VectorArrow CreateArrow(string arrowName, Color color, float width, bool dashed)
        {
            var arrow = new VectorArrow(transform, arrowName, color, width, dashed, arrowHeadMesh);
            arrows.Add(arrow);
            return arrow;
        }

        private static void DrawVector(VectorArrow arrow, Vector3 origin, Vector3 vector, float scale, float maximumLength)
        {
            if (arrow == null)
                return;

            var magnitude = vector.magnitude;
            if (magnitude <= 0.001f)
            {
                arrow.Hide();
                return;
            }

            arrow.Draw(origin, vector.normalized, Mathf.Min(maximumLength, magnitude * scale));
        }

        private void ClearArrows()
        {
            foreach (var arrow in arrows)
                arrow.Dispose();
            arrows.Clear();

            if (arrowHeadMesh != null)
            {
                if (Application.isPlaying)
                    Destroy(arrowHeadMesh);
                else
                    DestroyImmediate(arrowHeadMesh);
                arrowHeadMesh = null;
            }
        }

        private void OnDestroy()
        {
            ClearArrows();
        }

        private static Mesh CreateArrowHeadMesh()
        {
            const int sides = 12;
            var vertices = new Vector3[sides + 2];
            var triangles = new int[sides * 6];
            vertices[0] = Vector3.zero;
            vertices[1] = new Vector3(0f, 0f, -1f);
            for (var index = 0; index < sides; index++)
            {
                var angle = index * Mathf.PI * 2f / sides;
                vertices[index + 2] = new Vector3(Mathf.Cos(angle) * 0.42f, Mathf.Sin(angle) * 0.42f, -1f);
                var next = (index + 1) % sides;
                var triangle = index * 6;
                triangles[triangle] = 0;
                triangles[triangle + 1] = index + 2;
                triangles[triangle + 2] = next + 2;
                triangles[triangle + 3] = 1;
                triangles[triangle + 4] = next + 2;
                triangles[triangle + 5] = index + 2;
            }

            var mesh = new Mesh { name = "ForceVectorArrowHead" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private sealed class VectorArrow
        {
            private const int DashCount = 7;
            private readonly GameObject root;
            private readonly LineRenderer[] lines;
            private readonly Transform head;
            private readonly Material material;
            private readonly float width;
            private readonly bool dashed;

            public VectorArrow(Transform parent, string arrowName, Color color, float arrowWidth, bool useDashes, Mesh headMesh)
            {
                width = arrowWidth;
                dashed = useDashes;
                root = new GameObject(arrowName);
                root.transform.SetParent(parent, false);

                var shader = Shader.Find("Sprites/Default");
                material = new Material(shader) { name = arrowName + "Material", color = color };
                var lineCount = dashed ? DashCount : 1;
                lines = new LineRenderer[lineCount];
                for (var index = 0; index < lineCount; index++)
                {
                    var lineObject = new GameObject(dashed ? "Dash_" + index.ToString("00") : "Shaft");
                    lineObject.transform.SetParent(root.transform, false);
                    var line = lineObject.AddComponent<LineRenderer>();
                    line.positionCount = 2;
                    line.useWorldSpace = true;
                    line.widthMultiplier = width;
                    line.numCapVertices = 4;
                    line.startColor = Color.white;
                    line.endColor = Color.white;
                    line.sharedMaterial = material;
                    lines[index] = line;
                }

                var headObject = new GameObject("ArrowHead", typeof(MeshFilter), typeof(MeshRenderer));
                headObject.transform.SetParent(root.transform, false);
                headObject.GetComponent<MeshFilter>().sharedMesh = headMesh;
                headObject.GetComponent<MeshRenderer>().sharedMaterial = material;
                head = headObject.transform;
                Hide();
            }

            public void Draw(Vector3 origin, Vector3 direction, float length)
            {
                if (length <= 0.001f || direction.sqrMagnitude <= 0.001f)
                {
                    Hide();
                    return;
                }

                root.SetActive(true);
                direction.Normalize();
                var headLength = Mathf.Min(length * 0.35f, width * 5.2f);
                var shaftLength = Mathf.Max(0f, length - headLength * 0.72f);

                if (dashed)
                {
                    for (var index = 0; index < lines.Length; index++)
                    {
                        var startDistance = shaftLength * index / lines.Length;
                        var endDistance = shaftLength * (index + 0.58f) / lines.Length;
                        SetLine(lines[index], origin + direction * startDistance, origin + direction * endDistance);
                    }
                }
                else
                {
                    SetLine(lines[0], origin, origin + direction * shaftLength);
                }

                head.position = origin + direction * length;
                var up = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.98f ? Vector3.forward : Vector3.up;
                head.rotation = Quaternion.LookRotation(direction, up);
                head.localScale = Vector3.one * headLength;
                head.gameObject.SetActive(true);
            }

            public void Hide()
            {
                if (root != null)
                    root.SetActive(false);
            }

            public void Dispose()
            {
                if (material != null)
                {
                    if (Application.isPlaying)
                        Object.Destroy(material);
                    else
                        Object.DestroyImmediate(material);
                }

                if (root != null)
                {
                    if (Application.isPlaying)
                        Object.Destroy(root);
                    else
                        Object.DestroyImmediate(root);
                }
            }

            private static void SetLine(LineRenderer line, Vector3 start, Vector3 end)
            {
                line.SetPosition(0, start);
                line.SetPosition(1, end);
            }
        }
    }
}
