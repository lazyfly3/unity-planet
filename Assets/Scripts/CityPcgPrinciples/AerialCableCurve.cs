using UnityEngine;

namespace UnityPlanet.CityPcg
{
    /// <summary>
    /// Read-only authored result for one generated aerial cable. It owns the
    /// small runtime tube mesh so repeated edit-mode PCG rebuilds do not leak
    /// meshes. It intentionally has no Collider and no flight-speed behavior.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AerialCableCurve : MonoBehaviour
    {
        [SerializeField] Vector3[] localPoints = System.Array.Empty<Vector3>();
        Mesh generatedMesh;

        public int PointCount => localPoints == null ? 0 : localPoints.Length;

        public void Configure(Vector3[] points, Mesh mesh)
        {
            localPoints = points ?? System.Array.Empty<Vector3>();
            generatedMesh = mesh;
        }

        public Vector3 GetWorldPoint(int index)
        {
            if (localPoints == null || localPoints.Length == 0)
                return transform.position;
            return transform.TransformPoint(localPoints[Mathf.Clamp(
                index,
                0,
                localPoints.Length - 1)]);
        }

        void OnDestroy()
        {
            if (generatedMesh == null)
                return;
            if (Application.isPlaying)
                Destroy(generatedMesh);
            else
                DestroyImmediate(generatedMesh);
            generatedMesh = null;
        }
    }
}
