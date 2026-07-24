using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlanetLowPolyCloudField : MonoBehaviour
{
    [SerializeField, Range(4, 32)] int maximumClouds = 24;
    [SerializeField, Min(1f)] float altitudeSpread = 18f;
    [SerializeField] Vector2 scaleRange = new Vector2(10f, 24f);

    GameObject[] sourcePrefabs;
    Transform[] instances;
    Vector3[] baseDirections;
    float[] altitudeOffsets;
    float[] scales;
    Quaternion[] localTilts;
    VoxelQuadSphereWorld world;
    PlanetLowPolyVisualProfile visual;
    float coverage;
    Vector3 angularAxis = Vector3.up;
    float angularSpeed;
    float angularOffset;
    bool configured;

    public void Configure(VoxelQuadSphereWorld targetWorld, PlanetLowPolyVisualProfile profile)
    {
        world = targetWorld;
        visual = profile != null ? profile.Clone() : null;
        if (world == null || visual == null)
        {
            SetVisibleCount(0);
            configured = false;
            return;
        }

        visual.ClampValues();
        EnsurePool();
        BuildDistribution();
        configured = true;
        UpdateTransforms(true);
    }

    public void SetWeather(float valueCoverage, Vector3 wind)
    {
        coverage = Mathf.Clamp01(valueCoverage);
        if (wind.sqrMagnitude > 0.0001f && world != null)
        {
            Vector3 radial = transform.position - world.GetPlanetCenterWorld();
            if (radial.sqrMagnitude < 0.0001f)
                radial = Vector3.up;
            angularAxis = Vector3.Cross(radial.normalized, wind.normalized);
            if (angularAxis.sqrMagnitude < 0.0001f)
                angularAxis = Vector3.up;
            angularAxis.Normalize();
            angularSpeed = Mathf.Clamp(wind.magnitude * 0.018f, 0.02f, 0.3f);
        }
    }

    void LateUpdate()
    {
        if (!configured || world == null)
            return;
        angularOffset = Mathf.Repeat(angularOffset + angularSpeed * Time.deltaTime, 360f);
        UpdateTransforms(false);
    }

    void OnDisable()
    {
        SetVisibleCount(0);
    }

    void EnsurePool()
    {
        if (sourcePrefabs == null || sourcePrefabs.Length == 0)
        {
            sourcePrefabs = Resources.LoadAll<GameObject>("PlanetLowPolyKit");
            Array.Sort(sourcePrefabs, (a, b) => string.CompareOrdinal(a.name, b.name));
        }

        int count = Mathf.Clamp(maximumClouds, 4, 32);
        if (instances != null && instances.Length == count)
            return;

        DestroyPool();
        instances = new Transform[count];
        baseDirections = new Vector3[count];
        altitudeOffsets = new float[count];
        scales = new float[count];
        localTilts = new Quaternion[count];
        if (sourcePrefabs == null || sourcePrefabs.Length == 0)
            return;

        Transform poolParent = transform.parent != null ? transform.parent : transform;
        for (int i = 0; i < count; i++)
        {
            GameObject instance = Instantiate(sourcePrefabs[i % sourcePrefabs.Length], poolParent);
            instance.name = $"RuntimeCloud_{i:00}";
            DisableColliders(instance);
            instances[i] = instance.transform;
            instance.SetActive(false);
        }
    }

    void BuildDistribution()
    {
        if (instances == null)
            return;

        uint state = unchecked((uint)(visual.cloudSeed ^ 0x62c59a8d));
        int count = instances.Length;
        float goldenAngle = Mathf.PI * (3f - Mathf.Sqrt(5f));
        for (int i = 0; i < count; i++)
        {
            float y = 1f - 2f * ((i + 0.5f) / count);
            float radius = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            float angle = i * goldenAngle + Next01(ref state) * 0.65f;
            baseDirections[i] = new Vector3(Mathf.Cos(angle) * radius, y,
                Mathf.Sin(angle) * radius).normalized;
            altitudeOffsets[i] = (Next01(ref state) - 0.5f) * altitudeSpread;
            scales[i] = Mathf.Lerp(scaleRange.x, scaleRange.y, Next01(ref state));
            localTilts[i] = Quaternion.Euler(
                Next01(ref state) * 18f - 9f,
                Next01(ref state) * 360f,
                Next01(ref state) * 10f - 5f);
        }
    }

    void UpdateTransforms(bool force)
    {
        if (instances == null || baseDirections == null)
            return;

        int visibleCount = Mathf.Clamp(Mathf.RoundToInt(
            Mathf.Lerp(0f, instances.Length, coverage * visual.cloudCoverage)), 0, instances.Length);
        Vector3 center = world.GetPlanetCenterWorld();
        Quaternion drift = Quaternion.AngleAxis(angularOffset, angularAxis);
        float baseRadius = world.PlanetRadius + Mathf.Max(8f, visual.atmosphereThickness * 0.42f);
        for (int i = 0; i < instances.Length; i++)
        {
            Transform cloud = instances[i];
            if (cloud == null)
                continue;
            bool visible = i < visibleCount;
            if (cloud.gameObject.activeSelf != visible)
                cloud.gameObject.SetActive(visible);
            if (!visible)
                continue;

            Vector3 direction = drift * baseDirections[i];
            cloud.position = center + direction * (baseRadius + altitudeOffsets[i]);
            Vector3 tangent = Vector3.ProjectOnPlane(Vector3.forward, direction);
            if (tangent.sqrMagnitude < 0.001f)
                tangent = Vector3.ProjectOnPlane(Vector3.right, direction);
            cloud.rotation = Quaternion.LookRotation(tangent.normalized, direction) * localTilts[i];
            if (force || cloud.localScale.x <= 0f)
                cloud.localScale = Vector3.one * scales[i];
        }
    }

    void SetVisibleCount(int count)
    {
        if (instances == null)
            return;
        for (int i = 0; i < instances.Length; i++)
            if (instances[i] != null)
                instances[i].gameObject.SetActive(i < count);
    }

    void DestroyPool()
    {
        if (instances == null)
            return;
        for (int i = 0; i < instances.Length; i++)
        {
            if (instances[i] == null)
                continue;
            if (Application.isPlaying)
                Destroy(instances[i].gameObject);
            else
                DestroyImmediate(instances[i].gameObject);
        }
        instances = null;
    }

    static void DisableColliders(GameObject root)
    {
        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
            colliders[i].enabled = false;
    }

    static float Next01(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return (state & 0x00ffffffu) / 16777216f;
    }
}
