using System.Threading.Tasks;
using UnityEngine;

[DefaultExecutionOrder(725)]
[DisallowMultipleComponent]
public sealed class PlanetSurfaceFarLodController : MonoBehaviour
{
    VoxelQuadSphereWorld world;
    GameObject farLodObject;
    Mesh runtimeMesh;
    Material runtimeMaterial;
    GameObject transitionObject;
    Mesh transitionMesh;
    Material transitionMaterial;
    Transform player;
    float hideRadius;
    float heightScale;
    GalaxyPlanetDefinition planetDefinition;
    PlanetLowPolyVisualProfile visualProfile;
    ProceduralPlanetOcean oceanVisual;
    Vector3 displayedHideCenter;
    Vector3 hideCenterVelocity;
    float displayedHideRadius;
    bool hasDisplayedCutout;
    Vector3 lastTransitionCenterWorld;
    float lastTransitionRadius;
    Vector3 transitionBuildCenterWorld;
    float transitionBuildRadius;
    Vector3[] transitionFarVertices;
    Task<PlanetSurfaceLodTransitionMeshData> transitionBuildTask;
    bool transitionBuildSuperseded;

    public void Configure(VoxelQuadSphereWorld valueWorld, GalaxyPlanetDefinition definition)
    {
        Cleanup();
        world = valueWorld;
        if (world == null || definition == null || !world.IsStreamingLargePlanet)
            return;

        planetDefinition = definition;
        player = world.PlayerSpawn;
        visualProfile = definition.lowPolyVisual != null ? definition.lowPolyVisual.Clone() : null;
        visualProfile?.ClampValues();
        PlanetCelestialProfile celestial = definition.celestial
            ?? PlanetCelestialProfile.CreateCompatibleDefault();
        heightScale = Mathf.Max(1f, celestial.maximumTerrainElevation);
        hideRadius = Mathf.Max(96f, world.PlanetRadius * 0.055f);
        runtimeMesh = PlanetLodMeshBuilder.Build(definition, 64);
        transitionFarVertices = runtimeMesh != null
            ? runtimeMesh.vertices
            : null;
        Shader shader = Shader.Find("VoxelPlanet/SurfaceFarLod");
        if (shader == null)
        {
            Debug.LogError("PlanetSurfaceFarLodController: missing VoxelPlanet/SurfaceFarLod shader.", this);
            return;
        }

        runtimeMaterial = new Material(shader) { name = "RuntimeSurfaceFarLod" };
        farLodObject = new GameObject("RuntimeSurfaceFarLod");
        farLodObject.transform.SetParent(world.transform, false);
        farLodObject.transform.localPosition = world.GetPlanetCenterLocal();
        MeshFilter filter = farLodObject.AddComponent<MeshFilter>();
        MeshRenderer renderer = farLodObject.AddComponent<MeshRenderer>();
        filter.sharedMesh = runtimeMesh;
        renderer.sharedMaterial = runtimeMaterial;
        oceanVisual = world.GetComponent<ProceduralPlanetOcean>()
            ?? world.gameObject.AddComponent<ProceduralPlanetOcean>();
        oceanVisual.Configure(
            definition,
            world.GetPlanetCenterLocal(),
            56,
            PlanetOceanRenderMode.Surface);
        ApplyShaderProperties();
    }

    void LateUpdate()
    {
        ApplyShaderProperties();
    }

    void ApplyShaderProperties()
    {
        if (runtimeMaterial == null || world == null)
            return;

        Vector3 center = world.GetPlanetCenterWorld();
        bool canRevealNearTerrain = world.TryGetReadySurfaceCutout(
            out Vector3 hideCenter,
            out float readyHideRadius);
        if (!canRevealNearTerrain)
        {
            hideCenter = player != null
                ? player.position
                : center + Vector3.up * world.PlanetRadius;
            readyHideRadius = hideRadius;
        }
        float targetHideRadius = Mathf.Min(hideRadius, Mathf.Max(1f, readyHideRadius));
        if (!hasDisplayedCutout)
        {
            displayedHideCenter = hideCenter;
            displayedHideRadius = targetHideRadius;
            hasDisplayedCutout = true;
        }
        else
        {
            displayedHideCenter = Vector3.SmoothDamp(
                displayedHideCenter,
                hideCenter,
                ref hideCenterVelocity,
                0.18f,
                Mathf.Infinity,
                Time.unscaledDeltaTime);
            displayedHideRadius = Mathf.MoveTowards(
                displayedHideRadius,
                targetHideRadius,
                Mathf.Max(24f, hideRadius * 0.8f) * Time.unscaledDeltaTime);
        }
        runtimeMaterial.SetVector("_PlanetCenter", new Vector4(center.x, center.y, center.z, 1f));
        runtimeMaterial.SetVector(
            "_HideCenter",
            new Vector4(
                displayedHideCenter.x,
                displayedHideCenter.y,
                displayedHideCenter.z,
                1f));
        runtimeMaterial.SetFloat("_HideRadius", displayedHideRadius);
        runtimeMaterial.SetFloat("_RadialInset", 3.25f);
        runtimeMaterial.SetFloat("_MinimumAmbient", 0.23f);
        // Advance the far-LOD cutout only after the complete near terrain region
        // has meshes and baked colliders. The previous center remains a safety
        // net while the next streaming region is being prepared.
        runtimeMaterial.SetFloat(
            "_EnableNearTerrainCutout",
            canRevealNearTerrain ? 1f : 0f);
        runtimeMaterial.SetFloat("_UseLowPolyVisual", visualProfile != null ? 1f : 0f);
        float transitionWidth = Mathf.Clamp(
            displayedHideRadius * 0.035f,
            2.5f,
            4.5f);
        float coarseTriangleArc = world.PlanetRadius
            * Mathf.PI
            / (64f * 2f);
        float transitionOuterRadius = displayedHideRadius
            + Mathf.Max(36f, coarseTriangleArc * 1.25f);
        float transitionSkirtDepth = Mathf.Max(
            18f,
            heightScale * 0.18f);
        runtimeMaterial.SetFloat("_TransitionWidth", transitionWidth);
        runtimeMaterial.SetFloat(
            "_TransitionOuterRadius",
            transitionOuterRadius);
        runtimeMaterial.SetFloat(
            "_TransitionDepression",
            Mathf.Max(
                transitionSkirtDepth * 2f,
                heightScale * 1.35f));
        if (visualProfile != null)
        {
            runtimeMaterial.SetColor("_LowlandColor", visualProfile.lowlandColor);
            runtimeMaterial.SetColor("_HighlandColor", visualProfile.highlandColor);
            runtimeMaterial.SetColor("_CliffColor", visualProfile.cliffColor);
            runtimeMaterial.SetColor("_RockColor", visualProfile.rockColor);
            runtimeMaterial.SetColor("_AccentColor", visualProfile.accentColor);
            runtimeMaterial.SetColor("_ShoreColor", visualProfile.shoreColor);
            runtimeMaterial.SetColor("_SnowColor", visualProfile.snowColor);
            runtimeMaterial.SetFloat("_FacetStrength", visualProfile.facetStrength);
            runtimeMaterial.SetFloat("_LightingBands", visualProfile.lightingBands);
            runtimeMaterial.SetFloat("_MacroColorSize", visualProfile.macroColorSize);
            runtimeMaterial.SetFloat("_MacroVariation", visualProfile.macroVariation);
            runtimeMaterial.SetFloat("_CliffSlope", visualProfile.cliffSlope);
            runtimeMaterial.SetFloat("_PlanetRadius", world.PlanetRadius);
            runtimeMaterial.SetFloat("_HeightScale", heightScale);
            runtimeMaterial.SetFloat("_SeaLevel", visualProfile.oceanLevel);
            runtimeMaterial.SetFloat("_ShoreWidth", visualProfile.shoreWidth);
            runtimeMaterial.SetFloat("_SnowLine", visualProfile.snowLine);
            runtimeMaterial.SetFloat("_SnowAmount", visualProfile.snowAmount);
        }

        UpdateTransitionRing(
            canRevealNearTerrain,
            hideCenter,
            targetHideRadius,
            transitionWidth,
            targetHideRadius
                + Mathf.Max(36f, coarseTriangleArc * 1.25f),
            transitionSkirtDepth);
    }

    void UpdateTransitionRing(
        bool canRevealNearTerrain,
        Vector3 centerWorld,
        float cutoutRadius,
        float transitionWidth,
        float outerRadius,
        float skirtDepth)
    {
        ApplyCompletedTransitionBuild();
        if (!canRevealNearTerrain
            || planetDefinition == null
            || runtimeMesh == null)
        {
            if (transitionObject != null)
                transitionObject.SetActive(false);
            transitionBuildSuperseded = transitionBuildTask != null;
            return;
        }

        EnsureTransitionObject();
        transitionObject.SetActive(true);
        transitionMaterial.CopyPropertiesFromMaterial(runtimeMaterial);
        transitionMaterial.SetFloat("_EnableNearTerrainCutout", 0f);
        transitionMaterial.SetFloat("_RadialInset", 0f);
        transitionMaterial.SetFloat("_TransitionDepression", 0f);
        transitionMaterial.SetFloat("_CullMode", 0f);
        transitionMaterial.renderQueue = 1995;

        float rebuildDistance = Mathf.Max(8f, cutoutRadius * 0.08f);
        bool requiresRebuild = transitionMesh == null
            || Vector3.Distance(
                lastTransitionCenterWorld,
                centerWorld) >= rebuildDistance
            || Mathf.Abs(lastTransitionRadius - cutoutRadius) >= 1.5f;
        if (!requiresRebuild)
            return;

        if (transitionBuildTask != null)
        {
            if (Vector3.Distance(
                    transitionBuildCenterWorld,
                    centerWorld) >= rebuildDistance
                || Mathf.Abs(
                    transitionBuildRadius
                    - cutoutRadius) >= 1.5f)
            {
                transitionBuildSuperseded = true;
            }
            return;
        }

        Vector3 localDirection =
            world.transform.InverseTransformPoint(centerWorld)
            - world.GetPlanetCenterLocal();
        if (localDirection.sqrMagnitude < 0.001f)
            localDirection = Vector3.up;
        localDirection.Normalize();

        float innerRadius = Mathf.Max(
            3f,
            cutoutRadius - transitionWidth - 7f);
        PlanetSurfaceLodTransitionBuildInput input =
            PlanetSurfaceLodTransitionMeshBuilder.CaptureInput(
            planetDefinition,
            transitionFarVertices,
            localDirection,
            innerRadius,
            outerRadius,
            96,
            5,
            skirtDepth);
        if (input == null)
            return;

        transitionBuildCenterWorld = centerWorld;
        transitionBuildRadius = cutoutRadius;
        transitionBuildSuperseded = false;
        transitionBuildTask = Task.Run(
            () => PlanetSurfaceLodTransitionMeshBuilder.BuildData(input));
    }

    void ApplyCompletedTransitionBuild()
    {
        if (transitionBuildTask == null
            || !transitionBuildTask.IsCompleted)
        {
            return;
        }

        Task<PlanetSurfaceLodTransitionMeshData> completed =
            transitionBuildTask;
        transitionBuildTask = null;
        if (completed.IsFaulted)
        {
            Debug.LogError(
                "PlanetSurfaceFarLodController: background transition mesh " +
                $"generation failed. {completed.Exception?.GetBaseException().Message}",
                this);
            transitionBuildSuperseded = false;
            return;
        }

        if (transitionBuildSuperseded
            || transitionObject == null
            || world == null)
        {
            transitionBuildSuperseded = false;
            return;
        }

        transitionMesh =
            PlanetSurfaceLodTransitionMeshBuilder.ApplyData(
                completed.Result,
                transitionMesh);
        if (transitionMesh == null)
            return;
        transitionObject.GetComponent<MeshFilter>().sharedMesh =
            transitionMesh;
        lastTransitionCenterWorld = transitionBuildCenterWorld;
        lastTransitionRadius = transitionBuildRadius;
    }

    void EnsureTransitionObject()
    {
        if (transitionObject != null)
            return;

        transitionObject = new GameObject("RuntimeSurfaceLodTransition")
        {
            hideFlags = HideFlags.DontSave
        };
        transitionObject.transform.SetParent(world.transform, false);
        transitionObject.transform.localPosition =
            world.GetPlanetCenterLocal();
        transitionObject.AddComponent<MeshFilter>();
        MeshRenderer renderer =
            transitionObject.AddComponent<MeshRenderer>();
        renderer.shadowCastingMode =
            UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = true;
        transitionMaterial = new Material(runtimeMaterial)
        {
            name = "RuntimeSurfaceLodTransitionMaterial",
            hideFlags = HideFlags.DontSave
        };
        renderer.sharedMaterial = transitionMaterial;
    }

    void Cleanup()
    {
        if (farLodObject != null)
            Destroy(farLodObject);
        if (runtimeMesh != null)
            Destroy(runtimeMesh);
        if (runtimeMaterial != null)
            Destroy(runtimeMaterial);
        if (transitionObject != null)
            Destroy(transitionObject);
        if (transitionMesh != null)
            Destroy(transitionMesh);
        if (transitionMaterial != null)
            Destroy(transitionMaterial);
        if (oceanVisual != null)
            oceanVisual.Configure(null, Vector3.zero);
        farLodObject = null;
        runtimeMesh = null;
        runtimeMaterial = null;
        transitionObject = null;
        transitionMesh = null;
        transitionMaterial = null;
        transitionFarVertices = null;
        transitionBuildTask = null;
        transitionBuildSuperseded = false;
        planetDefinition = null;
        visualProfile = null;
        heightScale = 0f;
        oceanVisual = null;
        displayedHideCenter = Vector3.zero;
        hideCenterVelocity = Vector3.zero;
        displayedHideRadius = 0f;
        hasDisplayedCutout = false;
        lastTransitionCenterWorld = Vector3.zero;
        lastTransitionRadius = 0f;
        transitionBuildCenterWorld = Vector3.zero;
        transitionBuildRadius = 0f;
    }

    void OnDestroy()
    {
        Cleanup();
    }
}
