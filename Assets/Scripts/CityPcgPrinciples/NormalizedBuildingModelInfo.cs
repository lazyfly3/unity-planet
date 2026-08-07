using UnityEngine;

namespace UnityPlanet.CityPcg
{
    /// <summary>
    /// 放在标准化预制体根节点。PCG 只旋转/缩放这个适配根节点，
    /// 不直接修正第三方模型的枢轴和朝向。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NormalizedBuildingModelInfo : MonoBehaviour
    {
        [SerializeField] Vector3 authoredSize = new Vector3(32f, 60f, 32f);
        [SerializeField] string sourceDescription = string.Empty;
        [SerializeField] bool showOrientationGizmo;

        public Vector3 AuthoredSize => authoredSize;
        public string SourceDescription => sourceDescription;
        public bool ShowOrientationGizmo
        {
            get => showOrientationGizmo;
            set => showOrientationGizmo = value;
        }

        public void Configure(Vector3 size, string description)
        {
            authoredSize = new Vector3(
                Mathf.Max(0.1f, size.x),
                Mathf.Max(0.1f, size.y),
                Mathf.Max(0.1f, size.z));
            sourceDescription = description ?? string.Empty;
        }

        void OnDrawGizmos()
        {
            if (showOrientationGizmo)
                DrawOrientationGizmo();
        }

        void OnDrawGizmosSelected()
        {
            if (!showOrientationGizmo)
                DrawOrientationGizmo();
        }

        void DrawOrientationGizmo()
        {
            float horizontal = Mathf.Max(
                8f,
                Mathf.Min(authoredSize.x, authoredSize.z) * 0.8f);
            float vertical = Mathf.Max(12f, authoredSize.y * 0.32f);
            Vector3 origin = transform.TransformPoint(Vector3.up * 0.25f);

            Gizmos.color = Color.green;
            Gizmos.DrawLine(origin, origin + transform.up * vertical);
            Gizmos.DrawSphere(
                origin + transform.up * vertical,
                horizontal * 0.055f);

            Gizmos.color = new Color(0.1f, 0.55f, 1f);
            Gizmos.DrawLine(origin, origin + transform.forward * horizontal);
            Gizmos.DrawSphere(
                origin + transform.forward * horizontal,
                horizontal * 0.06f);

            Gizmos.color = new Color(1f, 0.18f, 0.12f);
            Gizmos.DrawLine(origin, origin + transform.right * horizontal);
            Gizmos.DrawSphere(
                origin + transform.right * horizontal,
                horizontal * 0.055f);

            Gizmos.color = new Color(0.1f, 1f, 1f, 0.55f);
            Matrix4x4 previous = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(
                new Vector3(0f, 0.05f, 0f),
                new Vector3(authoredSize.x, 0.1f, authoredSize.z));
            Gizmos.matrix = previous;
        }
    }
}
