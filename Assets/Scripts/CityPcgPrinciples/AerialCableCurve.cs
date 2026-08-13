using UnityEngine;

namespace UnityPlanet.CityPcg
{
    /// <summary>
    /// Authored result for one generated aerial cable. It owns the small
    /// runtime tube mesh so repeated edit-mode PCG rebuilds do not leak meshes,
    /// and deforms only that visual mesh when its bundle is struck. Collision
    /// detection and slowdown stay on AerialCableSlowHazard so the cable never
    /// becomes a solid physics obstacle.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AerialCableCurve : MonoBehaviour
    {
        [SerializeField] Vector3[] localPoints = System.Array.Empty<Vector3>();
        Mesh generatedMesh;
        Vector3[] baseVertices = System.Array.Empty<Vector3>();
        Vector3[] deformedVertices = System.Array.Empty<Vector3>();
        Vector3 shakeDirectionLocal;
        float shakeStartedAt = -1f;
        float shakeDuration;
        float shakeAmplitude;
        float shakeImpactT = 0.5f;
        bool meshIsDeformed;

        public int PointCount => localPoints == null ? 0 : localPoints.Length;

        public void Configure(Vector3[] points, Mesh mesh)
        {
            localPoints = points ?? System.Array.Empty<Vector3>();
            generatedMesh = mesh;
            if (generatedMesh == null)
                return;
            generatedMesh.MarkDynamic();
            baseVertices = generatedMesh.vertices;
            deformedVertices = new Vector3[baseVertices.Length];
            System.Array.Copy(baseVertices, deformedVertices, baseVertices.Length);
            enabled = false;
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

        public void PlayImpact(
            Vector3 worldPoint,
            Vector3 worldTravelDirection,
            float amplitude,
            float duration)
        {
            if (!Application.isPlaying || generatedMesh == null ||
                localPoints == null || localPoints.Length < 2 ||
                baseVertices.Length == 0)
            {
                return;
            }

            int nearest = 0;
            float nearestDistance = float.PositiveInfinity;
            for (int index = 0; index < localPoints.Length; index++)
            {
                float distance = (GetWorldPoint(index) - worldPoint).sqrMagnitude;
                if (distance >= nearestDistance)
                    continue;
                nearestDistance = distance;
                nearest = index;
            }

            int previous = Mathf.Max(0, nearest - 1);
            int next = Mathf.Min(localPoints.Length - 1, nearest + 1);
            Vector3 tangent = localPoints[next] - localPoints[previous];
            Vector3 travelLocal = transform.InverseTransformDirection(
                worldTravelDirection);
            Vector3 direction = Vector3.Cross(tangent, travelLocal);
            if (direction.sqrMagnitude < 0.001f)
                direction = Vector3.Cross(tangent, Vector3.up);
            if (direction.sqrMagnitude < 0.001f)
                direction = Vector3.right;

            shakeDirectionLocal = direction.normalized;
            shakeImpactT = nearest / (float)(localPoints.Length - 1);
            shakeAmplitude = Mathf.Clamp(
                Mathf.Max(shakeAmplitude * 0.55f, amplitude),
                0.35f,
                2.1f);
            shakeDuration = Mathf.Max(0.05f, duration);
            shakeStartedAt = Time.time;
            enabled = true;
        }

        void LateUpdate()
        {
            if (shakeStartedAt < 0f || generatedMesh == null ||
                localPoints == null || localPoints.Length < 2 ||
                baseVertices.Length != generatedMesh.vertexCount)
            {
                return;
            }

            float progress = (Time.time - shakeStartedAt) /
                             Mathf.Max(0.01f, shakeDuration);
            if (progress >= 1f)
            {
                RestoreMesh();
                shakeStartedAt = -1f;
                shakeAmplitude = 0f;
                enabled = false;
                return;
            }

            float envelope = (1f - progress) * (1f - progress);
            float oscillation = Mathf.Sin(progress * Mathf.PI * 11f);
            float displacement = shakeAmplitude * envelope * oscillation;
            int verticesPerPoint = Mathf.Max(
                1,
                baseVertices.Length / localPoints.Length);
            for (int vertex = 0; vertex < baseVertices.Length; vertex++)
            {
                int pointIndex = Mathf.Min(
                    localPoints.Length - 1,
                    vertex / verticesPerPoint);
                float cableT = pointIndex / (float)(localPoints.Length - 1);
                float endpointAnchor = Mathf.Sin(cableT * Mathf.PI);
                float fromImpact = cableT - shakeImpactT;
                float localWeight = 0.38f + 0.62f *
                    Mathf.Exp(-fromImpact * fromImpact * 14f);
                deformedVertices[vertex] = baseVertices[vertex] +
                    shakeDirectionLocal *
                    (displacement * endpointAnchor * localWeight);
            }
            generatedMesh.vertices = deformedVertices;
            meshIsDeformed = true;
        }

        void RestoreMesh()
        {
            if (!meshIsDeformed || generatedMesh == null ||
                baseVertices.Length != generatedMesh.vertexCount)
            {
                return;
            }
            generatedMesh.vertices = baseVertices;
            meshIsDeformed = false;
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
