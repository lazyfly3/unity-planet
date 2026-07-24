using System.Collections.Generic;
using UnityEngine;

public readonly struct OceanLandingPose
{
    public readonly Vector3 planetCenter;
    public readonly Vector3 up;
    public readonly Vector3 forward;
    public readonly float oceanRadius;
    public readonly float maximumVisualWaveHeight;

    public OceanLandingPose(
        Vector3 center,
        Vector3 radialUp,
        Vector3 tangentForward,
        float meanOceanRadius,
        float maximumWaveHeight)
    {
        planetCenter = center;
        up = radialUp.normalized;
        forward = Vector3.ProjectOnPlane(tangentForward, up).normalized;
        oceanRadius = meanOceanRadius;
        maximumVisualWaveHeight = maximumWaveHeight;
    }
}

[DisallowMultipleComponent]
public sealed class ProceduralOceanLandingPlatform : MonoBehaviour
{
    const float MinimumDeckSize = 24f;
    const float SafetyMarginPerSide = 4f;
    const float DeckThickness = 0.65f;

    readonly List<Material> runtimeMaterials = new List<Material>();
    Transform animatedVisuals;
    Vector3 animatedBasePosition;
    Quaternion animatedBaseRotation;

    public Vector3 DeckSurfacePoint { get; private set; }
    public Vector3 PlayerSpawnPoint { get; private set; }
    public Vector3 Up { get; private set; }
    public Vector3 Forward { get; private set; }
    public Vector2 DeckSize { get; private set; }
    public Collider DeckCollider { get; private set; }

    public static ProceduralOceanLandingPlatform Build(
        Bounds shipBounds,
        OceanLandingPose oceanPose)
    {
        Vector3 up = oceanPose.up.sqrMagnitude > 0.001f
            ? oceanPose.up
            : Vector3.up;
        Vector3 forward = oceanPose.forward.sqrMagnitude > 0.001f
            ? oceanPose.forward
            : Vector3.ProjectOnPlane(Vector3.forward, up).normalized;
        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.Cross(up, Vector3.right).normalized;
        Vector3 right = Vector3.Cross(up, forward).normalized;
        forward = Vector3.Cross(right, up).normalized;

        Vector2 deckSize = CalculateDeckDimensions(shipBounds, right, forward);
        Vector3 deckSurface = oceanPose.planetCenter
            + up * (oceanPose.oceanRadius
                + oceanPose.maximumVisualWaveHeight
                + 1.2f);

        GameObject root = new GameObject("ProceduralOceanLandingPlatform");
        root.transform.SetPositionAndRotation(
            deckSurface,
            Quaternion.LookRotation(forward, up));
        ProceduralOceanLandingPlatform platform =
            root.AddComponent<ProceduralOceanLandingPlatform>();
        platform.Up = up;
        platform.Forward = forward;
        platform.DeckSurfacePoint = deckSurface;
        platform.DeckSize = deckSize;
        platform.BuildGeometry(deckSize);
        return platform;
    }

    public static Vector2 CalculateDeckDimensions(
        Bounds shipBounds,
        Vector3 right,
        Vector3 forward)
    {
        Vector3 extents = shipBounds.extents;
        float halfWidth = Mathf.Abs(right.x) * extents.x
            + Mathf.Abs(right.y) * extents.y
            + Mathf.Abs(right.z) * extents.z;
        float halfLength = Mathf.Abs(forward.x) * extents.x
            + Mathf.Abs(forward.y) * extents.y
            + Mathf.Abs(forward.z) * extents.z;
        return new Vector2(
            Mathf.Max(MinimumDeckSize, halfWidth * 2f + SafetyMarginPerSide * 2f),
            Mathf.Max(MinimumDeckSize, halfLength * 2f + SafetyMarginPerSide * 2f));
    }

    void BuildGeometry(Vector2 size)
    {
        Material deck = CreateMaterial(
            "Platform Deck",
            new Color(0.055f, 0.075f, 0.09f, 1f),
            0.72f,
            Color.black);
        Material trim = CreateMaterial(
            "Platform Trim",
            new Color(0.08f, 0.16f, 0.19f, 1f),
            0.65f,
            new Color(0f, 0.85f, 1.15f, 1f));
        Material pontoon = CreateMaterial(
            "Platform Pontoons",
            new Color(0.035f, 0.055f, 0.075f, 1f),
            0.84f,
            new Color(0f, 0.18f, 0.25f, 1f));
        Material marking = CreateMaterial(
            "Platform Markings",
            new Color(0.12f, 0.5f, 0.58f, 1f),
            0.6f,
            new Color(0f, 1.2f, 1.5f, 1f));

        GameObject deckObject = CreateBox(
            "StableDeck",
            transform,
            new Vector3(0f, -DeckThickness * 0.5f, 0f),
            new Vector3(size.x, DeckThickness, size.y),
            deck,
            true);
        BoxCollider deckCollider = deckObject.GetComponent<BoxCollider>();
        deckCollider.sharedMaterial = null;
        DeckCollider = deckCollider;

        float rail = 0.18f;
        CreateBox("RimLeft", transform, new Vector3(-size.x * .5f + rail, .08f, 0),
            new Vector3(rail * 2f, .16f, size.y), trim, false);
        CreateBox("RimRight", transform, new Vector3(size.x * .5f - rail, .08f, 0),
            new Vector3(rail * 2f, .16f, size.y), trim, false);
        CreateBox("RimFore", transform, new Vector3(0, .08f, size.y * .5f - rail),
            new Vector3(size.x, .16f, rail * 2f), trim, false);
        CreateBox("RimAft", transform, new Vector3(0, .08f, -size.y * .5f + rail),
            new Vector3(size.x, .16f, rail * 2f), trim, false);

        CreateBox("LandingMarkX", transform, new Vector3(0, .035f, 0),
            new Vector3(Mathf.Min(7f, size.x * .32f), .04f, .22f), marking, false);
        CreateBox("LandingMarkZ", transform, new Vector3(0, .035f, 0),
            new Vector3(.22f, .04f, Mathf.Min(7f, size.y * .32f)), marking, false);

        GameObject visualRoot = new GameObject("AnimatedPontoonVisuals");
        visualRoot.transform.SetParent(transform, false);
        animatedVisuals = visualRoot.transform;
        animatedBasePosition = animatedVisuals.localPosition;
        animatedBaseRotation = animatedVisuals.localRotation;
        float pontoonWidth = Mathf.Clamp(size.x * .17f, 2.5f, 5f);
        float pontoonLength = Mathf.Max(8f, size.y - 3f);
        float lateral = size.x * .5f + pontoonWidth * .35f;
        CreateCapsule("PontoonLeft", animatedVisuals,
            new Vector3(-lateral, -1.1f, 0), pontoonWidth, pontoonLength, pontoon);
        CreateCapsule("PontoonRight", animatedVisuals,
            new Vector3(lateral, -1.1f, 0), pontoonWidth, pontoonLength, pontoon);

        float playerSide = size.x * .5f - 2f;
        PlayerSpawnPoint = transform.TransformPoint(new Vector3(playerSide, 1.2f, 0f));
    }

    Material CreateMaterial(string materialName, Color color, float smoothness, Color emission)
    {
        Shader shader = Shader.Find("Standard");
        Material material = new Material(shader)
        {
            name = materialName,
            hideFlags = HideFlags.DontSave
        };
        material.color = color;
        material.SetFloat("_Glossiness", smoothness);
        if (emission.maxColorComponent > 0.001f)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emission);
        }
        runtimeMaterials.Add(material);
        return material;
    }

    static GameObject CreateBox(
        string objectName,
        Transform parent,
        Vector3 localPosition,
        Vector3 localScale,
        Material material,
        bool keepCollider)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = objectName;
        box.transform.SetParent(parent, false);
        box.transform.localPosition = localPosition;
        box.transform.localScale = localScale;
        box.GetComponent<Renderer>().sharedMaterial = material;
        if (!keepCollider)
            DestroyGenerated(box.GetComponent<Collider>());
        return box;
    }

    static void CreateCapsule(
        string objectName,
        Transform parent,
        Vector3 localPosition,
        float diameter,
        float length,
        Material material)
    {
        GameObject capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        capsule.name = objectName;
        capsule.transform.SetParent(parent, false);
        capsule.transform.localPosition = localPosition;
        capsule.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        capsule.transform.localScale = new Vector3(
            diameter,
            Mathf.Max(diameter, length) * .5f,
            diameter);
        capsule.GetComponent<Renderer>().sharedMaterial = material;
        DestroyGenerated(capsule.GetComponent<Collider>());
    }

    static void DestroyGenerated(Object value)
    {
        if (value == null)
            return;
        if (Application.isPlaying)
            Destroy(value);
        else
            DestroyImmediate(value);
    }

    void Update()
    {
        if (animatedVisuals == null)
            return;
        float wave = Mathf.Sin(Time.time * 0.72f);
        animatedVisuals.localPosition = animatedBasePosition + Vector3.up * (wave * 0.09f);
        animatedVisuals.localRotation = animatedBaseRotation
            * Quaternion.Euler(0f, 0f, wave * 0.45f);
    }

    void OnDestroy()
    {
        for (int i = 0; i < runtimeMaterials.Count; i++)
        {
            if (runtimeMaterials[i] != null)
                Destroy(runtimeMaterials[i]);
        }
        runtimeMaterials.Clear();
    }
}
