using System;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-2000)]
[DisallowMultipleComponent]
public sealed class StableAntelopeSpeciesRandomizer : MonoBehaviour
{
    static readonly string[] BodyModules =
    {
        "_Body_Deer",
        "_Body_Fat"
    };

    static readonly string[] RequiredModules =
    {
        "_Head_Deer",
        "DeerEyes"
    };

    // These modules were authored for the deer head and validated against the
    // inverse-bind armature. Modules from other head families are deliberately
    // excluded even when their bone names happen to match.
    static readonly string[] EarModules =
    {
        "_HDEars_1",
        "_HDEars_9"
    };

    static readonly string[] HornModules =
    {
        "_HDHorns_1",
        "_HDHorns_2",
        "_HDHorns_3",
        "_HDHorns_4",
        "_HDHorns_5"
    };

    static readonly string[] TailModules =
    {
        "_Tail_Alien0",
        "_Tail_Alien1",
        "_Tail_Alien4"
    };

    static readonly string[] DeerAccessoryModules =
    {
        "_DeerAcc_1N2",
        "_DeerAcc_2N2",
        "_DeerAcc_5N2",
        "_DeerAcc_6N2",
        "_DeerAcc_23",
        "_DeerAcc_24",
        "_DeerAcc_25"
    };

    static readonly string[] FatAccessoryModules =
    {
        "_FatAcc_1N",
        "_FatAcc_2N",
        "_FatAcc_5N",
        "_FatAcc_6N",
        "_FatAcc_11OK",
        "_FatAcc_12OK",
        "_FatAcc_14OK"
    };

    // The FBX also carries compatible modules reserved for later head profiles.
    // Listing them here makes sure they stay hidden until a complete profile is
    // implemented rather than appearing as disconnected geometry.
    static readonly string[] ReservedModules =
    {
        "_BHEars_1N",
        "_GD1Ear_1N",
        "_GD1Ear_2N",
        "_GD2Ear_1N1",
        "_GD2Ear_2N1",
        "_GD2Ear_8N",
        "_BHHorns_1",
        "_BHHorns_2",
        "_BHHorns_3",
        "_BHHorns_4"
    };

    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int PrimaryColorId = Shader.PropertyToID("_PrimaryColor");
    static readonly int SecondaryColorId = Shader.PropertyToID("_SecondaryColor");
    static readonly int AccentColorId = Shader.PropertyToID("_AccentColor");
    static readonly int GlossinessId = Shader.PropertyToID("_Glossiness");
    static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");

    [SerializeField] Transform rigRoot;
    [SerializeField] int speciesSeed = 12345;
    [SerializeField] bool regenerateWithR = true;
    [SerializeField] bool logSelection = true;

    readonly Dictionary<string, SkinnedMeshRenderer> modulesByName =
        new Dictionary<string, SkinnedMeshRenderer>(StringComparer.Ordinal);
    readonly List<SkinnedMeshRenderer> activeRenderers =
        new List<SkinnedMeshRenderer>(8);
    MaterialPropertyBlock propertyBlock;

    public int SpeciesSeed => speciesSeed;
    public string SpeciesName { get; private set; }
    public string SpeciesSignature { get; private set; }
    public SkinnedMeshRenderer[] ActiveRenderers => activeRenderers.ToArray();

    public static bool IsKnownModuleName(string moduleName)
    {
        return Contains(BodyModules, moduleName)
            || Contains(RequiredModules, moduleName)
            || Contains(EarModules, moduleName)
            || Contains(HornModules, moduleName)
            || Contains(TailModules, moduleName)
            || Contains(DeerAccessoryModules, moduleName)
            || Contains(FatAccessoryModules, moduleName)
            || Contains(ReservedModules, moduleName);
    }

    public void Configure(Transform targetRigRoot, int seed, bool allowKeyboardRegeneration)
    {
        rigRoot = targetRigRoot;
        speciesSeed = seed;
        regenerateWithR = allowKeyboardRegeneration;
    }

    void Awake()
    {
        ApplySpecies(speciesSeed);
    }

    void Update()
    {
        if (regenerateWithR && Input.GetKeyDown(KeyCode.R))
            ApplySpecies(unchecked(speciesSeed + 1));
    }

    public void ApplySpecies(int seed)
    {
        speciesSeed = seed;
        RebuildModuleIndex();
        DisableKnownModules();

        var random = new StableRandom(unchecked((ulong)(uint)seed) ^ 0xD1B54A32D192ED03UL);
        string body = BodyModules[random.Next(BodyModules.Length)];
        string ears = random.NextFloat() < .82f
            ? EarModules[random.Next(EarModules.Length)]
            : string.Empty;
        string horns = random.NextFloat() < .68f
            ? HornModules[random.Next(HornModules.Length)]
            : string.Empty;
        string tail = random.NextFloat() < .9f
            ? TailModules[random.Next(TailModules.Length)]
            : string.Empty;
        string[] accessories = string.Equals(body, "_Body_Fat", StringComparison.Ordinal)
            ? FatAccessoryModules
            : DeerAccessoryModules;
        string accessory = random.NextFloat() < .62f
            ? accessories[random.Next(accessories.Length)]
            : string.Empty;

        EnableRequired(body);
        for (int i = 0; i < RequiredModules.Length; i++)
            EnableRequired(RequiredModules[i]);
        EnableOptional(ears);
        EnableOptional(horns);
        EnableOptional(tail);
        EnableOptional(accessory);

        Color primary;
        Color secondary;
        Color accent;
        CreatePalette(ref random, out primary, out secondary, out accent);
        ApplyPalette(primary, secondary, accent, ref random);

        SpeciesName = GenerateName(ref random);
        SpeciesSignature = string.Join("|", new[]
        {
            "antelope-v2",
            seed.ToString(),
            body,
            EmptyAsNone(ears),
            EmptyAsNone(horns),
            EmptyAsNone(tail),
            EmptyAsNone(accessory),
            ColorUtility.ToHtmlStringRGB(primary),
            ColorUtility.ToHtmlStringRGB(accent)
        });

        SkinnedMeshRenderer[] renderers = activeRenderers.ToArray();
        ConservativeFootPlacementIK footPlacement =
            GetComponent<ConservativeFootPlacementIK>();
        if (footPlacement != null)
            footPlacement.SetModuleRenderers(renderers);
        FixedQuadrupedRigV2 validator = GetComponent<FixedQuadrupedRigV2>();
        if (validator != null)
            validator.SetModuleRenderers(renderers);

        if (logSelection)
        {
            Debug.Log(
                $"Generated stable Antelope species '{SpeciesName}'. " +
                $"Seed={seed}, Body={body}, Ears={EmptyAsNone(ears)}, " +
                $"Horns={EmptyAsNone(horns)}, Tail={EmptyAsNone(tail)}, " +
                $"Accessory={EmptyAsNone(accessory)}, Renderers={activeRenderers.Count}. " +
                "The validated Armature, bind poses and WALK animation were not modified.",
                this);
        }
    }

    void RebuildModuleIndex()
    {
        modulesByName.Clear();
        Transform searchRoot = rigRoot != null ? rigRoot : transform;
        SkinnedMeshRenderer[] renderers =
            searchRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            SkinnedMeshRenderer renderer = renderers[i];
            if (renderer != null && IsKnownModuleName(renderer.name)
                && !modulesByName.ContainsKey(renderer.name))
                modulesByName.Add(renderer.name, renderer);
        }
    }

    void DisableKnownModules()
    {
        activeRenderers.Clear();
        foreach (KeyValuePair<string, SkinnedMeshRenderer> pair in modulesByName)
        {
            pair.Value.enabled = false;
            pair.Value.SetPropertyBlock(null);
        }
    }

    void EnableRequired(string moduleName)
    {
        if (EnableOptional(moduleName))
            return;
        Debug.LogError(
            $"Stable Antelope species module '{moduleName}' is missing from the imported library.",
            this);
    }

    bool EnableOptional(string moduleName)
    {
        if (string.IsNullOrEmpty(moduleName))
            return false;
        if (!modulesByName.TryGetValue(moduleName, out SkinnedMeshRenderer renderer))
            return false;
        renderer.enabled = true;
        activeRenderers.Add(renderer);
        return true;
    }

    void ApplyPalette(Color primary, Color secondary, Color accent, ref StableRandom random)
    {
        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();

        for (int i = 0; i < activeRenderers.Count; i++)
        {
            SkinnedMeshRenderer renderer = activeRenderers[i];
            string moduleName = renderer.name;
            Color tint;
            if (moduleName.IndexOf("Eyes", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                tint = Color.Lerp(Color.white, accent, .18f);
            }
            else if (moduleName.IndexOf("Horn", StringComparison.OrdinalIgnoreCase) >= 0
                || moduleName.IndexOf("Acc", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                tint = accent;
            }
            else
            {
                tint = Color.Lerp(primary, secondary, random.Range(.08f, .42f));
            }

            renderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(ColorId, tint);
            propertyBlock.SetColor(BaseColorId, tint);
            propertyBlock.SetColor(PrimaryColorId, primary);
            propertyBlock.SetColor(SecondaryColorId, secondary);
            propertyBlock.SetColor(AccentColorId, accent);
            float smoothness = random.Range(.16f, .38f);
            propertyBlock.SetFloat(GlossinessId, smoothness);
            propertyBlock.SetFloat(SmoothnessId, smoothness);
            renderer.SetPropertyBlock(propertyBlock);
            propertyBlock.Clear();
        }
    }

    static void CreatePalette(
        ref StableRandom random,
        out Color primary,
        out Color secondary,
        out Color accent)
    {
        float hue = random.NextFloat();
        primary = Color.HSVToRGB(hue, random.Range(.38f, .68f), random.Range(.5f, .82f));
        secondary = Color.HSVToRGB(
            Mathf.Repeat(hue + random.Range(-.12f, .12f), 1f),
            random.Range(.28f, .58f),
            random.Range(.58f, .9f));
        accent = Color.HSVToRGB(
            Mathf.Repeat(hue + random.Range(.38f, .62f), 1f),
            random.Range(.45f, .78f),
            random.Range(.62f, .95f));
    }

    static string GenerateName(ref StableRandom random)
    {
        string[] starts = { "Astra", "Cera", "Doro", "Ily", "Kera", "Mora", "Nexa", "Tavi" };
        string[] ends = { "don", "fera", "lume", "nox", "ra", "rix", "ther", "vane" };
        return starts[random.Next(starts.Length)] + ends[random.Next(ends.Length)];
    }

    static bool Contains(string[] values, string value)
    {
        for (int i = 0; i < values.Length; i++)
            if (string.Equals(values[i], value, StringComparison.Ordinal))
                return true;
        return false;
    }

    static string EmptyAsNone(string value)
    {
        return string.IsNullOrEmpty(value) ? "None" : value;
    }

    struct StableRandom
    {
        ulong state;

        public StableRandom(ulong seed)
        {
            state = seed != 0UL ? seed : 0x9E3779B97F4A7C15UL;
        }

        public int Next(int maximum)
        {
            if (maximum <= 1)
                return 0;
            return (int)(NextUInt64() % (uint)maximum);
        }

        public float NextFloat()
        {
            return (NextUInt64() >> 40) * (1f / 16777216f);
        }

        public float Range(float minimum, float maximum)
        {
            return Mathf.Lerp(minimum, maximum, NextFloat());
        }

        ulong NextUInt64()
        {
            state += 0x9E3779B97F4A7C15UL;
            ulong value = state;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }
    }
}
