using UnityEngine;

public static class CreatureBodyGraphBuilder
{
    const int GeneratorVersion = 3;
    // One extra Armature transform keeps the complete skin rig within 48 bones.
    const int MaximumNodes = 47;

    public static CreatureDesignLanguage GenerateDesignLanguage(CreatureGenome genome)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(32);}
    try
    {
        var random = new GraphRandom(genome.seed, 0x51ED270Bu);
        return new CreatureDesignLanguage
        {
            massDistribution = random.Range(-1f, 1f),
            bodyCurve = random.Range(-0.45f, 0.65f),
            taper = random.Range(0.25f, 0.85f),
            limbAngularStyle = random.Range(0f, 1f),
            headBodyRatio = random.Range(0.55f, 1.5f),
            ornamentDensity = random.Range(0f, 1f),
            asymmetry = random.Range(0f, 0.18f),
            patternFrequency = random.Range(0.8f, 4.5f),
            primaryColor = genome.primaryColor,
            secondaryColor = genome.secondaryColor,
            bellyColor = Color.Lerp(genome.primaryColor, Color.white, random.Range(0.25f, 0.6f)),
            ornamentColor = Color.Lerp(genome.secondaryColor, Color.black, random.Range(0.05f, 0.35f))
        };
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public static CreatureBodyGraph Build(CreatureGenome genome)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(33);}
    try
    {
        for (int attempt = 0; attempt < 16; attempt++)
        {
            var random = new GraphRandom(genome.seed, unchecked((uint)(0x9E3779B9u + attempt * 0x85EBCA6Bu)));
            CreatureBodyGraph graph = BuildAttempt(genome, ref random);
            if (graph.Validate(out _))
                return graph;
        }
        return BuildFallback(genome);
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    static CreatureBodyGraph BuildAttempt(CreatureGenome genome, ref GraphRandom random)
    {
        bool serpentine = genome.topology == CreatureTopology.Serpentine;
        var graph = new CreatureBodyGraph
        {
            generatorVersion = GeneratorVersion,
            torsoCount = serpentine ? 1 : random.Range(1, 4),
            spineCount = genome.torsoSpline != null && genome.torsoSpline.points != null
                ? genome.torsoSpline.points.Count
                : (serpentine ? Mathf.Clamp(genome.spineSegmentCount, 9, 16) : random.Range(2, 9)),
            supportLegPairCount = genome.legPairCount,
            armPairCount = serpentine ? 0 : random.Range(0, 3),
            headCount = !serpentine && random.Value() < 0.12f ? 2 : 1,
            tailCount = serpentine ? 1 : random.Range(0, 3),
            tentacleCount = random.Value() < 0.58f ? random.Range(0, 5) : 0
        };

        int[] spineNodes = BuildSpine(genome, graph);
        BuildTorsos(genome, graph, spineNodes, ref random);
        BuildHeads(genome, graph, spineNodes[spineNodes.Length - 1], ref random);
        BuildSupportLegs(genome, graph, spineNodes, ref random);
        BuildArms(genome, graph, spineNodes, ref random);
        BuildTails(genome, graph, spineNodes[0], ref random);
        BuildTentacles(genome, graph, spineNodes, ref random);
        BuildOrnaments(genome, graph, spineNodes, ref random);
        return graph;
    }

    static int[] BuildSpine(CreatureGenome genome, CreatureBodyGraph graph)
    {
        if (genome.torsoSpline == null || !genome.torsoSpline.Validate(out _))
            genome.torsoSpline = CreatureTorsoSpline.CreateLegacyFallback(
                genome.bodyLength, genome.bodyWidth, genome.bodyHeight,
                genome.topology == CreatureTopology.Serpentine ? 9 : 5);
        graph.spineCount = genome.torsoSpline.points.Count;
        var indices = new int[graph.spineCount];
        for (int i = 0; i < graph.spineCount; i++)
        {
            float t = graph.spineCount == 1 ? 0.5f : i / (float)(graph.spineCount - 1);
            CreatureTorsoControlPoint point = genome.torsoSpline.points[i];
            Vector3 localPosition = i == 0
                ? point.localPosition
                : point.localPosition - genome.torsoSpline.points[i - 1].localPosition;
            indices[i] = AddNode(graph, new CreatureBodyNode
            {
                parentIndex = i == 0 ? -1 : indices[i - 1],
                type = CreatureBodyNodeType.Spine,
                side = CreatureBodySide.Center,
                socket = CreatureSocketType.Core,
                chainIndex = i,
                longitudinalPosition = t,
                localPosition = localPosition,
                size = new Vector3(
                    point.width,
                    point.height,
                    i == 0 ? Vector3.Distance(point.localPosition, genome.torsoSpline.points[1].localPosition)
                        : Vector3.Distance(point.localPosition, genome.torsoSpline.points[i - 1].localPosition)),
                radius = Mathf.Max(0.08f, Mathf.Min(point.width, point.height) * 0.5f),
                animationPhase = t
            });
        }
        return indices;
    }

    static void BuildTorsos(CreatureGenome genome, CreatureBodyGraph graph, int[] spine, ref GraphRandom random)
    {
        for (int i = 0; i < graph.torsoCount && graph.nodes.Count < MaximumNodes; i++)
        {
            float t = graph.torsoCount == 1 ? 0.5f : Mathf.Lerp(0.24f, 0.76f, i / (float)(graph.torsoCount - 1));
            t = Mathf.Clamp01(t + genome.designLanguage.massDistribution * 0.12f);
            int spineIndex = Mathf.RoundToInt(t * (spine.Length - 1));
            float scale = random.Range(0.72f, 1.3f);
            AddNode(graph, new CreatureBodyNode
            {
                parentIndex = spine[spineIndex],
                type = CreatureBodyNodeType.Torso,
                side = CreatureBodySide.Center,
                socket = CreatureSocketType.Core,
                chainIndex = i,
                longitudinalPosition = t,
                localPosition = Vector3.zero,
                size = new Vector3(genome.bodyWidth * scale, genome.bodyHeight * scale,
                    genome.bodyLength / graph.torsoCount * random.Range(0.72f, 1.08f)),
                radius = genome.bodyWidth * 0.45f * scale
            });
        }
    }

    static void BuildHeads(CreatureGenome genome, CreatureBodyGraph graph, int parent, ref GraphRandom random)
    {
        for (int i = 0; i < graph.headCount && graph.nodes.Count + 2 <= MaximumNodes; i++)
        {
            float side = graph.headCount == 1 ? 0f : (i == 0 ? -1f : 1f);
            int symmetry = graph.headCount == 1 ? 0 : 100;
            int neck = AddNode(graph, new CreatureBodyNode
            {
                parentIndex = parent,
                type = CreatureBodyNodeType.Neck,
                side = side < 0f ? CreatureBodySide.Left : side > 0f ? CreatureBodySide.Right : CreatureBodySide.Center,
                socket = CreatureSocketType.Front,
                symmetryGroup = symmetry,
                localPosition = new Vector3(side * genome.bodyWidth * 0.28f,
                    genome.bodyHeight * random.Range(0.08f, 0.32f),
                    Mathf.Max(0.1f, genome.neckLength * 0.35f)),
                localEulerAngles = new Vector3(0f, side * random.Range(5f, 22f), 0f),
                size = new Vector3(genome.headWidth * 0.42f, genome.headHeight * 0.42f,
                    Mathf.Max(0.15f, genome.neckLength)),
                radius = Mathf.Max(0.12f, genome.headWidth * 0.22f)
            });
            AddNode(graph, new CreatureBodyNode
            {
                parentIndex = neck,
                type = CreatureBodyNodeType.Head,
                side = side < 0f ? CreatureBodySide.Left : side > 0f ? CreatureBodySide.Right : CreatureBodySide.Center,
                socket = CreatureSocketType.Front,
                symmetryGroup = symmetry,
                localPosition = new Vector3(0f, genome.neckLength * 0.35f,
                    genome.neckLength * 0.62f + genome.headLength * 0.38f),
                size = new Vector3(genome.headWidth, genome.headHeight, genome.headLength)
                    * genome.designLanguage.headBodyRatio,
                radius = Mathf.Max(0.15f, genome.headWidth * 0.45f),
                animationPhase = i * 0.5f
            });
        }
    }

    static void BuildSupportLegs(CreatureGenome genome, CreatureBodyGraph graph, int[] spine, ref GraphRandom random)
    {
        for (int pair = 0; pair < graph.supportLegPairCount && graph.nodes.Count + 6 <= MaximumNodes; pair++)
        {
            float t = graph.supportLegPairCount == 1 ? 0.48f : pair / (float)(graph.supportLegPairCount - 1);
            t = Mathf.Lerp(0.72f, 0.26f, t);
            int parent = spine[Mathf.RoundToInt(t * (spine.Length - 1))];
            float length = Mathf.Lerp(genome.frontLegLength, genome.rearLegLength, 1f - t);
            for (int sideIndex = 0; sideIndex < 2; sideIndex++)
            {
                float side = sideIndex == 0 ? -1f : 1f;
                int upper = AddNode(graph, new CreatureBodyNode
                {
                    parentIndex = parent,
                    type = CreatureBodyNodeType.UpperLeg,
                    side = side < 0f ? CreatureBodySide.Left : CreatureBodySide.Right,
                    socket = CreatureSocketType.Bottom,
                    symmetryGroup = 200 + pair,
                    chainIndex = pair,
                    gaitGroup = (pair + sideIndex) & 1,
                    longitudinalPosition = t,
                    localPosition = new Vector3(side * genome.bodyWidth * genome.legSpread,
                        -genome.bodyHeight * 0.24f, 0f),
                    localEulerAngles = new Vector3(0f, 0f, side * genome.designLanguage.limbAngularStyle * 12f),
                    size = new Vector3(genome.legThickness, length * 0.52f, genome.legThickness),
                    radius = genome.legThickness
                });
                int lower = AddNode(graph, new CreatureBodyNode
                {
                    parentIndex = upper,
                    type = CreatureBodyNodeType.LowerLeg,
                    side = side < 0f ? CreatureBodySide.Left : CreatureBodySide.Right,
                    symmetryGroup = 200 + pair,
                    chainIndex = pair,
                    gaitGroup = (pair + sideIndex) & 1,
                    longitudinalPosition = t,
                    localPosition = Vector3.down * length * 0.52f,
                    size = new Vector3(genome.legThickness * 0.82f, length * 0.48f, genome.legThickness * 0.82f),
                    radius = genome.legThickness * 0.82f
                });
                AddNode(graph, new CreatureBodyNode
                {
                    parentIndex = lower,
                    type = CreatureBodyNodeType.Foot,
                    side = side < 0f ? CreatureBodySide.Left : CreatureBodySide.Right,
                    symmetryGroup = 200 + pair,
                    chainIndex = pair,
                    gaitGroup = (pair + sideIndex) & 1,
                    longitudinalPosition = t,
                    localPosition = Vector3.down * length * 0.48f,
                    size = new Vector3(genome.footScale, genome.footScale * 0.3f, genome.footScale * 1.35f),
                    radius = genome.footScale * 0.45f
                });
            }
        }
    }

    static void BuildArms(CreatureGenome genome, CreatureBodyGraph graph, int[] spine, ref GraphRandom random)
    {
        int createdPairs = 0;
        for (int pair = 0; pair < graph.armPairCount && graph.nodes.Count + 6 <= MaximumNodes; pair++)
        {
            float t = Mathf.Lerp(0.62f, 0.82f, graph.armPairCount == 1 ? 0.5f : pair);
            int parent = spine[Mathf.RoundToInt(t * (spine.Length - 1))];
            float length = Mathf.Lerp(genome.bodyHeight, genome.bodyLength * 0.38f, random.Range(0.35f, 0.8f));
            for (int sideIndex = 0; sideIndex < 2; sideIndex++)
            {
                float side = sideIndex == 0 ? -1f : 1f;
                int upper = AddNode(graph, new CreatureBodyNode
                {
                    parentIndex = parent,
                    type = CreatureBodyNodeType.UpperArm,
                    side = side < 0f ? CreatureBodySide.Left : CreatureBodySide.Right,
                    socket = CreatureSocketType.Side,
                    symmetryGroup = 300 + pair,
                    chainIndex = pair,
                    localPosition = new Vector3(side * genome.bodyWidth * 0.48f, genome.bodyHeight * 0.08f, 0f),
                    localEulerAngles = new Vector3(random.Range(-25f, 20f), 0f, side * random.Range(25f, 70f)),
                    size = new Vector3(genome.legThickness * 0.72f, length * 0.52f, genome.legThickness * 0.72f),
                    radius = genome.legThickness * 0.72f,
                    animationPhase = sideIndex * 0.5f
                });
                int lower = AddNode(graph, new CreatureBodyNode
                {
                    parentIndex = upper,
                    type = CreatureBodyNodeType.LowerArm,
                    side = side < 0f ? CreatureBodySide.Left : CreatureBodySide.Right,
                    symmetryGroup = 300 + pair,
                    chainIndex = pair,
                    localPosition = Vector3.down * length * 0.52f,
                    size = new Vector3(genome.legThickness * 0.58f, length * 0.48f, genome.legThickness * 0.58f),
                    radius = genome.legThickness * 0.58f
                });
                AddNode(graph, new CreatureBodyNode
                {
                    parentIndex = lower,
                    type = CreatureBodyNodeType.Hand,
                    side = side < 0f ? CreatureBodySide.Left : CreatureBodySide.Right,
                    symmetryGroup = 300 + pair,
                    chainIndex = pair,
                    localPosition = Vector3.down * length * 0.48f,
                    size = Vector3.one * Mathf.Max(0.18f, genome.footScale * 0.55f),
                    radius = Mathf.Max(0.12f, genome.footScale * 0.25f)
                });
            }
            createdPairs++;
        }
        graph.armPairCount = createdPairs;
    }

    static void BuildTails(CreatureGenome genome, CreatureBodyGraph graph, int parent, ref GraphRandom random)
    {
        int created = 0;
        for (int i = 0; i < graph.tailCount && graph.nodes.Count + 2 <= MaximumNodes; i++)
        {
            float side = graph.tailCount == 1 ? 0f : (i == 0 ? -1f : 1f);
            Vector3 direction = new Vector3(side * genome.bodyWidth * 0.16f,
                random.Range(-0.08f, 0.18f) * genome.tailLength,
                -genome.tailLength * random.Range(0.72f, 1.08f));
            int tailBase = AddNode(graph, new CreatureBodyNode
            {
                parentIndex = parent,
                type = CreatureBodyNodeType.Tail,
                side = side < 0f ? CreatureBodySide.Left : side > 0f ? CreatureBodySide.Right : CreatureBodySide.Center,
                socket = CreatureSocketType.Back,
                symmetryGroup = graph.tailCount == 1 ? 0 : 400,
                chainIndex = i,
                localPosition = direction * 0.48f,
                localEulerAngles = new Vector3(0f, side * random.Range(4f, 18f), 0f),
                size = new Vector3(genome.tailThickness, genome.tailThickness, genome.tailLength),
                radius = genome.tailThickness,
                animationPhase = i * 0.5f
            });
            AddNode(graph, new CreatureBodyNode
            {
                parentIndex = tailBase,
                type = CreatureBodyNodeType.Tail,
                side = graph.nodes[tailBase].side,
                socket = CreatureSocketType.Back,
                symmetryGroup = graph.nodes[tailBase].symmetryGroup,
                chainIndex = i,
                localPosition = direction * 0.52f,
                size = new Vector3(genome.tailThickness * 0.35f, genome.tailThickness * 0.35f,
                    genome.tailLength * 0.52f),
                radius = Mathf.Max(0.035f, genome.tailThickness * 0.28f),
                animationPhase = i * 0.5f + 0.2f
            });
            created++;
        }
        graph.tailCount = created;
    }

    static void BuildTentacles(CreatureGenome genome, CreatureBodyGraph graph, int[] spine, ref GraphRandom random)
    {
        int created = 0;
        for (int i = 0; i < graph.tentacleCount && graph.nodes.Count + 2 <= MaximumNodes; i++)
        {
            int parent = spine[random.Range(0, spine.Length)];
            float angle = i / (float)Mathf.Max(1, graph.tentacleCount) * Mathf.PI * 2f;
            float length = random.Range(0.6f, 1.8f) * genome.headScale;
            int first = AddNode(graph, new CreatureBodyNode
            {
                parentIndex = parent,
                type = CreatureBodyNodeType.Tentacle,
                side = Mathf.Cos(angle) < 0f ? CreatureBodySide.Left : CreatureBodySide.Right,
                socket = CreatureSocketType.Top,
                chainIndex = i,
                localPosition = new Vector3(Mathf.Cos(angle), 0.7f, Mathf.Sin(angle)).normalized * length * 0.5f,
                size = new Vector3(genome.legThickness * 0.35f, genome.legThickness * 0.35f, length * 0.5f),
                radius = Mathf.Max(0.06f, genome.legThickness * 0.32f),
                animationPhase = random.Range(0f, 1f)
            });
            AddNode(graph, new CreatureBodyNode
            {
                parentIndex = first,
                type = CreatureBodyNodeType.Tentacle,
                side = graph.nodes[first].side,
                chainIndex = i,
                localPosition = Vector3.forward * length * 0.5f,
                size = new Vector3(genome.legThickness * 0.18f, genome.legThickness * 0.18f, length * 0.5f),
                radius = Mathf.Max(0.035f, genome.legThickness * 0.16f),
                animationPhase = graph.nodes[first].animationPhase + 0.25f
            });
            created++;
        }
        graph.tentacleCount = created;
    }

    static void BuildOrnaments(CreatureGenome genome, CreatureBodyGraph graph, int[] spine, ref GraphRandom random)
    {
        int requested = Mathf.RoundToInt(genome.designLanguage.ornamentDensity * 10f);
        int created = 0;
        for (int i = 0; i < requested && graph.nodes.Count < MaximumNodes; i++)
        {
            int parent = spine[Mathf.RoundToInt(i / (float)Mathf.Max(1, requested - 1) * (spine.Length - 1))];
            bool plate = random.Value() < 0.55f;
            float sideOffset = random.Value() < genome.designLanguage.asymmetry
                ? random.Range(-0.35f, 0.35f) * genome.bodyWidth
                : 0f;
            AddNode(graph, new CreatureBodyNode
            {
                parentIndex = parent,
                type = plate ? CreatureBodyNodeType.BackPlate : CreatureBodyNodeType.Horn,
                side = sideOffset < 0f ? CreatureBodySide.Left : sideOffset > 0f ? CreatureBodySide.Right : CreatureBodySide.Center,
                socket = CreatureSocketType.Top,
                chainIndex = i,
                localPosition = new Vector3(sideOffset, genome.bodyHeight * random.Range(0.38f, 0.72f), 0f),
                localEulerAngles = new Vector3(random.Range(-15f, 15f), 0f, random.Range(-8f, 8f)),
                size = plate
                    ? new Vector3(random.Range(0.18f, 0.55f), random.Range(0.5f, 1.35f), random.Range(0.12f, 0.32f))
                    : new Vector3(0.16f, random.Range(0.4f, 1.4f), 0.16f),
                radius = plate ? 0.18f : 0.1f,
                animationPhase = i * 0.17f
            });
            created++;
        }
        graph.ornamentCount = created;
    }

    static int AddNode(CreatureBodyGraph graph, CreatureBodyNode node)
    {
        node.id = graph.nodes.Count;
        graph.nodes.Add(node);
        return node.id;
    }

    static CreatureBodyGraph BuildFallback(CreatureGenome genome)
    {
        genome.designLanguage.bodyCurve = 0f;
        var random = new GraphRandom(genome.seed, 1u);
        CreatureBodyGraph graph = BuildAttempt(genome, ref random);
        graph.generatorVersion = GeneratorVersion;
        return graph;
    }

    struct GraphRandom
    {
        uint state;

        public GraphRandom(int seed, uint salt)
        {
            uint value = unchecked((uint)seed) ^ salt;
            value = (value ^ (value >> 16)) * 0x85EBCA6Bu;
            value = (value ^ (value >> 13)) * 0xC2B2AE35u;
            state = value ^ (value >> 16);
            if (state == 0u) state = 0xA341316Cu;
        }

        public float Value() => Next01();
        public float Range(float minimum, float maximum) => Mathf.Lerp(minimum, maximum, Next01());
        public int Range(int minimum, int maximum) => minimum + Mathf.FloorToInt(Next01() * (maximum - minimum));

        float Next01()
        {
            uint value = state;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            state = value;
            return (value & 0x00FFFFFFu) / 16777216f;
        }
    }
}
