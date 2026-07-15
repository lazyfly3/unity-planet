using UnityEngine;

public enum CreatureTopologyOverride
{
    Random,
    Biped,
    Quadruped,
    Hexapod,
    Serpentine
}

[DisallowMultipleComponent]
public sealed class BioCreatureTestController : MonoBehaviour
{
    [SerializeField] int seed = 12345;
    [SerializeField] CreatureTopologyOverride topologyOverride = CreatureTopologyOverride.Random;
    [SerializeField] SphericalGravitySource gravitySource;
    [SerializeField] Material sharedCreatureMaterial;
    [SerializeField] BioCreatureFollowCamera followCamera;
    [SerializeField] LayerMask groundLayers = ~0;
    [SerializeField] Vector3 spawnDirection = Vector3.up;

    GameObject currentCreature;
    Material fallbackMaterial;

    public int Seed => seed;
    public Transform CurrentCreature => currentCreature != null ? currentCreature.transform : null;

    public void Configure(
        SphericalGravitySource source,
        Material creatureMaterial,
        BioCreatureFollowCamera cameraController)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(28);}
    try
    {
        gravitySource = source;
        sharedCreatureMaterial = creatureMaterial;
        followCamera = cameraController;
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    void Start()
    {
        SpawnCurrentSeed();
    }

    void Update()
    {
        if (!Input.GetKeyDown(KeyCode.R))
            return;

        RegenerateNextSeed();
    }

    public void RegenerateNextSeed()
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(29);}
    try
    {
        seed = unchecked(seed + 1);
        SpawnCurrentSeed();
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public void RegenerateAtSeed(int newSeed)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(30, (int)newSeed);}
    try
    {
        seed = newSeed;
        SpawnCurrentSeed();
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    void OnDestroy()
    {
        if (fallbackMaterial != null)
            Destroy(fallbackMaterial);
    }

    void SpawnCurrentSeed()
    {
        if (gravitySource == null)
        {
            Debug.LogError("BioCreatureTestController requires a SphericalGravitySource.", this);
            return;
        }

        if (currentCreature != null)
        {
            currentCreature.SetActive(false);
            Destroy(currentCreature);
        }

        Material material = sharedCreatureMaterial;
        if (material == null)
        {
            if (fallbackMaterial == null)
                fallbackMaterial = new Material(Shader.Find("Standard")) { name = "BioCreatureFallback" };
            material = fallbackMaterial;
        }

        CreatureTopology? forcedTopology = topologyOverride == CreatureTopologyOverride.Random
            ? null
            : (CreatureTopology)((int)topologyOverride - 1);
        CreatureGenome genome = ProceduralCreatureGenerator.Generate(seed, forcedTopology);
        Vector3 direction = spawnDirection.sqrMagnitude > 0.0001f ? spawnDirection.normalized : Vector3.up;
        Vector3 surfacePoint = FindSurfacePoint(direction);
        Vector3 up = gravitySource.GetUp(surfacePoint);
        float clearance = ProceduralCreatureAssembler.GetBodyClearance(genome);
        Vector3 spawnPosition = surfacePoint + up * (clearance + 0.15f);
        Quaternion spawnRotation = Quaternion.LookRotation(Vector3.Cross(Vector3.forward, up).normalized, up);

        currentCreature = ProceduralCreatureAssembler.Build(
            genome,
            transform,
            gravitySource,
            material,
            groundLayers,
            spawnPosition,
            spawnRotation);

        if (followCamera != null)
            followCamera.SetTarget(currentCreature.transform, true);

        Debug.Log(
            $"Bio creature generated. Seed={seed}, Topology={genome.topology}, Style={genome.bodyStyle}, " +
            $"Graph={genome.bodyGraph.StructureSignature}, Nodes={genome.bodyGraph.nodes.Count}, " +
            $"Body={genome.bodyWidth:F2}x{genome.bodyHeight:F2}x{genome.bodyLength:F2}, " +
            $"LegPairs={genome.bodyGraph.supportLegPairCount}, Spine={genome.bodyGraph.spineCount}, " +
            $"Legs={genome.frontLegLength:F2}/{genome.rearLegLength:F2}, Neck={genome.neckLength:F2}, " +
            $"Eyes={genome.eyeCount}, Horn={genome.hornLength:F2}, Gait={genome.gaitFrequency:F2}Hz",
            currentCreature);
    }

    Vector3 FindSurfacePoint(Vector3 direction)
    {
        Vector3 origin = gravitySource.Center + direction * (gravitySource.Radius + 20f);
        RaycastHit[] hits = Physics.RaycastAll(origin, -direction, 40f, groundLayers, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        Vector3 result = gravitySource.GetSurfacePoint(direction);
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null || hit.distance >= nearest)
                continue;
            nearest = hit.distance;
            result = hit.point;
        }
        return result;
    }
}
