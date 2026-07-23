using UnityEngine;

[DefaultExecutionOrder(725)]
[DisallowMultipleComponent]
public sealed class PlanetSurfaceFarLodController : MonoBehaviour
{
    VoxelQuadSphereWorld world;
    GameObject farLodObject;
    Mesh runtimeMesh;
    Material runtimeMaterial;
    Transform player;
    float hideRadius;

    public void Configure(VoxelQuadSphereWorld valueWorld, GalaxyPlanetDefinition definition)
    {
        Cleanup();
        world = valueWorld;
        if (world == null || definition == null || !world.IsStreamingLargePlanet)
            return;

        player = world.PlayerSpawn;
        hideRadius = Mathf.Max(96f, world.PlanetRadius * 0.055f);
        runtimeMesh = PlanetLodMeshBuilder.Build(definition, 48);
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
        Vector3 hideCenter = player != null
            ? player.position
            : center + Vector3.up * world.PlanetRadius;
        runtimeMaterial.SetVector("_PlanetCenter", new Vector4(center.x, center.y, center.z, 1f));
        runtimeMaterial.SetVector("_HideCenter", new Vector4(hideCenter.x, hideCenter.y, hideCenter.z, 1f));
        runtimeMaterial.SetFloat("_HideRadius", hideRadius);
        runtimeMaterial.SetFloat("_RadialInset", 1.5f);
    }

    void Cleanup()
    {
        if (farLodObject != null)
            Destroy(farLodObject);
        if (runtimeMesh != null)
            Destroy(runtimeMesh);
        if (runtimeMaterial != null)
            Destroy(runtimeMaterial);
        farLodObject = null;
        runtimeMesh = null;
        runtimeMaterial = null;
    }

    void OnDestroy()
    {
        Cleanup();
    }
}
