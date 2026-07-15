using UnityEngine;

public static class ProceduralCreatureGenerator
{
    public static CreatureGenome Generate(int seed)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(40, (int)seed);}
    try
    {
        return Generate(seed, null);
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public static CreatureGenome Generate(int seed, CreatureTopology? forcedTopology)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(41, (int)seed);}
    try
    {
        var random = new StableCreatureRandom(seed);
        CreatureTopology generatedTopology = (CreatureTopology)random.Range(0, 4);
        CreatureTopology topology = forcedTopology ?? generatedTopology;
        int bodyStyle = random.Range(0, 4);
        float hue = random.Range(0f, 1f);
        float accentHue = Mathf.Repeat(hue + random.Range(0.28f, 0.58f), 1f);

        GenerateBodyProportions(topology, bodyStyle, ref random,
            out float bodyLength, out float bodyHeight, out float bodyWidth,
            out float baseLegLength, out float neckLength, out float headScale);

        int legPairCount = topology == CreatureTopology.Biped ? 1
            : topology == CreatureTopology.Quadruped ? 2
            : topology == CreatureTopology.Hexapod ? 3
            : 0;
        int spineSegmentCount = topology == CreatureTopology.Serpentine ? random.Range(9, 17) : 1;
        float frontLegLength = legPairCount > 0 ? baseLegLength * random.Range(0.82f, 1.18f) : 0f;
        float rearLegLength = legPairCount > 0 ? baseLegLength * random.Range(0.82f, 1.18f) : 0f;
        float tailLength = topology == CreatureTopology.Serpentine
            ? bodyLength * random.Range(0.3f, 0.65f)
            : random.Range(0.25f, 4.6f);

        var genome = new CreatureGenome
        {
            seed = seed,
            topology = topology,
            bodyStyle = bodyStyle,
            legPairCount = legPairCount,
            spineSegmentCount = spineSegmentCount,
            bodyLength = bodyLength,
            bodyHeight = bodyHeight,
            bodyWidth = bodyWidth,
            headScale = headScale,
            headWidth = headScale * random.Range(0.75f, 1.45f),
            headHeight = headScale * random.Range(0.65f, 1.35f),
            headLength = headScale * random.Range(0.8f, 1.65f),
            neckLength = neckLength,
            earScale = topology == CreatureTopology.Serpentine ? 0f : random.Range(0.15f, 1.25f),
            hornLength = topology == CreatureTopology.Serpentine
                ? 0f
                : (random.Value() < 0.58f ? random.Range(0.35f, 1.8f) : 0f),
            eyeCount = topology == CreatureTopology.Serpentine
                ? (random.Value() < 0.82f ? 2 : 4)
                : (random.Value() < 0.18f ? 1 : (random.Value() < 0.72f ? 2 : 4)),
            tailLength = tailLength,
            tailThickness = topology == CreatureTopology.Serpentine
                ? bodyWidth * random.Range(0.24f, 0.42f)
                : random.Range(0.12f, 0.48f),
            legLength = Mathf.Max(frontLegLength, rearLegLength),
            frontLegLength = frontLegLength,
            rearLegLength = rearLegLength,
            legThickness = legPairCount > 0
                ? random.Range(0.16f, 0.5f) * Mathf.Lerp(0.75f, 1.3f, bodyWidth / 3.2f)
                : 0f,
            legSpread = random.Range(0.36f, 0.52f),
            footScale = legPairCount > 0 ? random.Range(0.32f, 0.82f) : 0f,
            gaitHeight = topology == CreatureTopology.Serpentine
                ? random.Range(0.1f, 0.3f)
                : random.Range(0.18f, 0.58f),
            gaitFrequency = topology == CreatureTopology.Serpentine
                ? random.Range(0.65f, 1.4f)
                : random.Range(0.85f, 2.25f),
            serpentineWaveAmplitude = topology == CreatureTopology.Serpentine ? random.Range(18f, 34f) : 0f,
            serpentinePhaseLag = topology == CreatureTopology.Serpentine ? random.Range(0.55f, 0.82f) : 0f,
            serpentineLateralFriction = topology == CreatureTopology.Serpentine ? random.Range(18f, 28f) : 0f,
            serpentineLongitudinalFriction = topology == CreatureTopology.Serpentine ? random.Range(0.18f, 0.55f) : 0f,
            serpentineBackwardFriction = topology == CreatureTopology.Serpentine ? random.Range(7f, 12f) : 0f,
            serpentineTractionEfficiency = topology == CreatureTopology.Serpentine ? random.Range(8f, 12f) : 0f,
            serpentineBodyFlattening = topology == CreatureTopology.Serpentine ? random.Range(0.52f, 0.72f) : 1f,
            serpentineHeadWidth = topology == CreatureTopology.Serpentine ? random.Range(1.1f, 1.5f) : 1f,
            serpentineTailTaper = topology == CreatureTopology.Serpentine ? random.Range(0.78f, 0.94f) : 1f,
            primaryColor = Color.HSVToRGB(hue, random.Range(0.55f, 0.9f), random.Range(0.65f, 0.95f)),
            secondaryColor = Color.HSVToRGB(accentHue, random.Range(0.45f, 0.85f), random.Range(0.65f, 1f))
        };
        genome.designLanguage = CreatureBodyGraphBuilder.GenerateDesignLanguage(genome);
        genome.bodyGraph = CreatureBodyGraphBuilder.Build(genome);
        return genome;
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    static void GenerateBodyProportions(
        CreatureTopology topology,
        int style,
        ref StableCreatureRandom random,
        out float length,
        out float height,
        out float width,
        out float legLength,
        out float neckLength,
        out float headScale)
    {
        if (topology == CreatureTopology.Biped)
        {
            length = random.Range(2.1f, 3.5f);
            height = random.Range(1.2f, 2.1f);
            width = random.Range(1f, 1.7f);
            legLength = random.Range(2.5f, 4.2f);
            neckLength = random.Range(0.5f, 1.8f);
            headScale = random.Range(0.65f, 1.25f);
            return;
        }
        if (topology == CreatureTopology.Hexapod)
        {
            length = random.Range(4.5f, 6.8f);
            height = random.Range(0.8f, 1.65f);
            width = random.Range(1.4f, 2.5f);
            legLength = random.Range(1.5f, 2.8f);
            neckLength = random.Range(0.05f, 0.75f);
            headScale = random.Range(0.55f, 1.15f);
            return;
        }
        if (topology == CreatureTopology.Serpentine)
        {
            length = random.Range(6.5f, 11f);
            height = random.Range(0.5f, 0.95f);
            width = random.Range(0.75f, 1.55f);
            legLength = 0f;
            neckLength = random.Range(0.05f, 0.22f);
            headScale = random.Range(0.65f, 1.05f);
            return;
        }

        switch (style)
        {
            case 0:
                length = random.Range(4.1f, 5.5f);
                height = random.Range(0.9f, 1.45f);
                width = random.Range(0.9f, 1.45f);
                legLength = random.Range(2.2f, 3.25f);
                neckLength = random.Range(0.15f, 0.65f);
                headScale = random.Range(0.55f, 0.9f);
                break;
            case 1:
                length = random.Range(3.3f, 4.5f);
                height = random.Range(1.45f, 2.2f);
                width = random.Range(1.9f, 2.75f);
                legLength = random.Range(1.45f, 2.15f);
                neckLength = random.Range(0.45f, 1.1f);
                headScale = random.Range(1f, 1.45f);
                break;
            case 2:
                length = random.Range(2.3f, 3.3f);
                height = random.Range(1.05f, 1.7f);
                width = random.Range(1f, 1.55f);
                legLength = random.Range(3.1f, 4.25f);
                neckLength = random.Range(1.15f, 2.2f);
                headScale = random.Range(0.65f, 1.05f);
                break;
            default:
                length = random.Range(2.9f, 4f);
                height = random.Range(2.1f, 2.9f);
                width = random.Range(2.3f, 3.2f);
                legLength = random.Range(1.15f, 1.75f);
                neckLength = random.Range(0.05f, 0.4f);
                headScale = random.Range(1.25f, 1.85f);
                break;
        }
    }

    struct StableCreatureRandom
    {
        uint state;

        public StableCreatureRandom(int seed)
        {
            uint mixed = unchecked((uint)seed) + 0x9E3779B9u;
            mixed = (mixed ^ (mixed >> 16)) * 0x85EBCA6Bu;
            mixed = (mixed ^ (mixed >> 13)) * 0xC2B2AE35u;
            state = mixed ^ (mixed >> 16);
            if (state == 0u)
                state = 0xA341316Cu;
        }

        public int Range(int minimum, int maximum)
        {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(42, (int)minimum, (int)maximum);}
    try
    {
            return minimum + Mathf.FloorToInt(Next01() * (maximum - minimum));
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

        public float Value()
        {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(43);}
    try
    {
            return Next01();
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

        public float Range(float minimum, float maximum)
        {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(44, (int)minimum, (int)maximum);}
    try
    {
            return Mathf.Lerp(minimum, maximum, Next01());
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

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
