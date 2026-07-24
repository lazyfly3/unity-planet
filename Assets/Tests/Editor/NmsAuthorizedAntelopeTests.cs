#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public sealed class NmsAuthorizedAntelopeTests
{
    const string LibraryPath =
        "Assets/Creatures/Authorized/NMS/Procedural/AntelopeAuthorizedLibrary.asset";
    const string ProfilePath =
        "Assets/Creatures/Authorized/NMS/Procedural/AntelopeQuadrupedMotionProfile.asset";
    const string FamilyPath =
        "Assets/Creatures/Generated/NMS/AntelopeQuadruped/AntelopeQuadrupedFamily.asset";
    const string ValidatedSourcePath =
        "Assets/Creatures/Authorized/NMS/Procedural/AntelopeProceduralSource.fbx";
    const string GaitTemplatePath =
        "Assets/Creatures/Authorized/NMS/Procedural/AntelopeWalkGaitTemplate.asset";

    [Test]
    public void Bio1VariantScales_AreDeterministicDistinctAndConstrained()
    {
        Vector3[] first = Bio1AntelopeVariantShowcase.CreateVariantScales(12345);
        Vector3[] second = Bio1AntelopeVariantShowcase.CreateVariantScales(12345);
        Assert.That(first, Has.Length.EqualTo(3));
        Assert.That(second, Has.Length.EqualTo(3));
        var heights = new float[3];
        for (int i = 0; i < first.Length; i++)
        {
            Assert.That(Vector3.Distance(first[i], second[i]), Is.LessThan(0.000001f));
            Assert.That(first[i].x, Is.InRange(0.82f, 1.22f));
            Assert.That(first[i].y, Is.InRange(0.72f, 1.30f));
            Assert.That(first[i].z, Is.InRange(0.96f, 1.04f));
            heights[i] = first[i].y;
        }
        System.Array.Sort(heights);
        Assert.That(heights[0], Is.InRange(0.72f, 0.88f));
        Assert.That(heights[1], Is.InRange(0.91f, 1.08f));
        Assert.That(heights[2], Is.InRange(1.12f, 1.30f));
    }

    [Test]
    public void SharedVariantSampler_IsDeterministicForAuthorizedAntelope()
    {
        NmsCreatureFamilyDefinition family =
            AssetDatabase.LoadAssetAtPath<NmsCreatureFamilyDefinition>(FamilyPath);
        Assert.That(family, Is.Not.Null);
        Assert.That(NmsCreatureVariantSampler.TrySample(
            family, 24680, out NmsCreatureSpeciesDefinition first,
            out string firstError), Is.True, firstError);
        Assert.That(NmsCreatureVariantSampler.TrySample(
            family, 24680, out NmsCreatureSpeciesDefinition second,
            out string secondError), Is.True, secondError);
        Assert.That(first.signature, Is.EqualTo(second.signature));
        Assert.That(first.selectedModules, Is.EqualTo(second.selectedModules));
        Assert.That(first.primaryColor, Is.EqualTo(second.primaryColor));
        Assert.That(first.secondaryColor, Is.EqualTo(second.secondaryColor));
        Assert.That(first.accentColor, Is.EqualTo(second.accentColor));
    }

    [Test]
    public void SharedVariantSampler_AlwaysCoversTheAntelopeTailSocket()
    {
        NmsCreatureFamilyDefinition family =
            AssetDatabase.LoadAssetAtPath<NmsCreatureFamilyDefinition>(FamilyPath);
        Assert.That(family, Is.Not.Null);
        for (int seed = 0; seed < 500; seed++)
        {
            Assert.That(NmsCreatureVariantSampler.TrySample(
                family, seed, out NmsCreatureSpeciesDefinition species,
                out string error), Is.True, error);
            int bodyCount = 0;
            int tailCount = 0;
            for (int module = 0; module < species.selectedModules.Length; module++)
            {
                string id = species.selectedModules[module];
                if (id == "_Body_Deer" || id == "_Body_Fat")
                    bodyCount++;
                if (id == "_Tail_Alien0" || id == "_Tail_Alien1"
                    || id == "_Tail_Alien4")
                    tailCount++;
            }
            Assert.That(bodyCount, Is.EqualTo(1), $"Seed {seed}");
            Assert.That(tailCount, Is.EqualTo(1), $"Seed {seed}");
        }
    }

    [Test]
    public void AppearanceOptions_AreManifestDrivenAndExcludeOpenTailSocket()
    {
        NmsCreatureFamilyDefinition family =
            AssetDatabase.LoadAssetAtPath<NmsCreatureFamilyDefinition>(FamilyPath);
        Assert.That(NmsAntelopeEditingOptions.TryCreate(
            family, out NmsAntelopeEditingOptions options,
            out string error), Is.True, error);
        Assert.That(options.Bodies, Is.EquivalentTo(
            new[] { "_Body_Deer", "_Body_Fat" }));
        Assert.That(options.Tails, Is.EquivalentTo(
            new[] { "_Tail_Alien0", "_Tail_Alien1", "_Tail_Alien4" }));
        Assert.That(options.Tails, Has.None.Empty);
        Assert.That(options.GetOptions(
            NmsAntelopeEditablePart.Accessory, "_Body_Deer"),
            Has.Some.EqualTo("_DeerAcc_25"));
        Assert.That(options.GetOptions(
            NmsAntelopeEditablePart.Accessory, "_Body_Fat"),
            Has.Some.EqualTo("_FatAcc_14OK"));
    }

    [Test]
    public void AppearanceParameters_RoundTripWithoutChangingSelectedModules()
    {
        NmsCreatureFamilyDefinition family =
            AssetDatabase.LoadAssetAtPath<NmsCreatureFamilyDefinition>(FamilyPath);
        Assert.That(NmsCreatureVariantSampler.TrySample(
            family, 9876, out NmsCreatureSpeciesDefinition sampled,
            out string sampleError), Is.True, sampleError);
        var descriptor = new NmsAntelopeVariantDescriptor(
            9876, new Vector3(1.1f, 0.9f, 1.02f), sampled);
        NmsAntelopeVariantParameters parameters =
            NmsAntelopeVariantParameters.FromDescriptor(descriptor);
        Assert.That(NmsAntelopeEditingOptions.TryCreate(
            family, out NmsAntelopeEditingOptions options,
            out string optionsError), Is.True, optionsError);
        options.Constrain(parameters);
        NmsCreatureSpeciesDefinition rebuilt =
            NmsCreatureVariantSampler.CreateManualSpecies(
                family.FamilyId, parameters.seed, parameters.BuildModuleIds(),
                parameters.primaryColor, parameters.secondaryColor,
                parameters.accentColor);
        Assert.That(rebuilt.selectedModules, Is.EqualTo(sampled.selectedModules));
        Assert.That(rebuilt.primaryColor, Is.EqualTo(sampled.primaryColor));
        Assert.That(rebuilt.secondaryColor, Is.EqualTo(sampled.secondaryColor));
        Assert.That(rebuilt.accentColor, Is.EqualTo(sampled.accentColor));
    }

    [TestCase("_Body_Deer", "_Head_Deer", "DeerEyes", "_HDEars_1", "_HDHorns_4", "_Tail_Alien1", "_DeerAcc_25")]
    [TestCase("_Body_Fat", "_Head_Deer", "DeerEyes", "_HDEars_9", "_HORNS_None", "_TAIL_None", "_FatAcc_14OK")]
    public void AuthorizedModules_RebindToOneCanonicalRig(params string[] selected)
    {
        NmsAuthorizedAntelopeLibrary library =
            AssetDatabase.LoadAssetAtPath<NmsAuthorizedAntelopeLibrary>(LibraryPath);
        Assert.That(library, Is.Not.Null);
        var parent = new GameObject("NmsAntelopeTestParent");
        var renderers = new List<Renderer>();
        GameObject root = null;
        try
        {
            bool built = library.TryBuild(
                parent.transform, selected, renderers, out root, out string error);
            Assert.That(built, Is.True, error);
            Assert.That(root, Is.Not.Null);
            int expectedCount = 0;
            for (int i = 0; i < selected.Length; i++)
                if (selected[i].IndexOf("None", System.StringComparison.OrdinalIgnoreCase) < 0)
                    expectedCount++;
            Assert.That(renderers.Count, Is.EqualTo(expectedCount));
            Assert.That(root.GetComponentsInChildren<SkinnedMeshRenderer>(true),
                Has.Length.GreaterThanOrEqualTo(expectedCount));
            for (int i = 0; i < renderers.Count; i++)
            {
                var skinned = renderers[i] as SkinnedMeshRenderer;
                Assert.That(skinned, Is.Not.Null);
                Assert.That(skinned.rootBone, Is.Not.Null);
                Assert.That(skinned.bones, Has.None.Null);
                Assert.That(skinned.bones.Length,
                    Is.EqualTo(skinned.sharedMesh.bindposes.Length));
                var baked = new Mesh();
                try
                {
                    skinned.BakeMesh(baked, false);
                    Assert.That(baked.vertexCount, Is.EqualTo(skinned.sharedMesh.vertexCount));
                    Assert.That(IsFinite(baked.bounds.size), Is.True, skinned.name);
                }
                finally
                {
                    Object.DestroyImmediate(baked);
                }
            }
        }
        finally
        {
            if (root != null)
                Object.DestroyImmediate(root);
            Object.DestroyImmediate(parent);
        }
    }

    [Test]
    public void AuthorizedAnimator_AllModuleBindingsSurviveAuthoritativeWalk()
    {
        NmsAuthorizedAntelopeLibrary library =
            AssetDatabase.LoadAssetAtPath<NmsAuthorizedAntelopeLibrary>(LibraryPath);
        Assert.That(library, Is.Not.Null);
        Assert.That(library.AnimatorController, Is.Not.Null);
        Assert.That(library.RigPrefab, Is.Not.Null);
        var selected = new string[library.Modules.Length];
        for (int i = 0; i < selected.Length; i++)
            selected[i] = library.Modules[i].SelectionId;

        var parent = new GameObject("NmsAnimatedVariantTestParent");
        var renderers = new List<Renderer>();
        GameObject root = null;
        try
        {
            Assert.That(library.TryBuild(
                parent.transform, selected, renderers,
                out root, out Animator animator, out string error), Is.True, error);
            Assert.That(animator, Is.Not.Null);
            Assert.That(animator.transform.Find("Armature"), Is.Not.Null);
            Assert.That(animator.runtimeAnimatorController,
                Is.SameAs(library.AnimatorController));
            bool hasSpeedParameter = false;
            for (int i = 0; i < animator.parameters.Length; i++)
                if (animator.parameters[i].name == library.SpeedParameter
                    && animator.parameters[i].type == AnimatorControllerParameterType.Float)
                    hasSpeedParameter = true;
            Assert.That(hasSpeedParameter, Is.True);

            AnimationClip[] clips = library.AnimatorController.animationClips;
            Assert.That(clips, Has.Length.EqualTo(3));
            for (int i = 0; i < clips.Length; i++)
                Assert.That(clips[i].name, Is.EqualTo("AntelopeSafeWalk"));
            AnimationMode.StartAnimationMode();
            try
            {
                for (int clipIndex = 0; clipIndex < clips.Length; clipIndex++)
                {
                    AnimationClip clip = clips[clipIndex];
                    EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
                    for (int bindingIndex = 0; bindingIndex < bindings.Length; bindingIndex++)
                    {
                        EditorCurveBinding binding = bindings[bindingIndex];
                        if (binding.type == typeof(Transform) && !string.IsNullOrEmpty(binding.path))
                            Assert.That(animator.transform.Find(binding.path), Is.Not.Null,
                                $"{clip.name}: {binding.path}");
                    }
                    for (int sample = 0; sample < 8; sample++)
                    {
                        AnimationMode.SampleAnimationClip(
                            animator.gameObject, clip, clip.length * sample / 8f);
                        for (int rendererIndex = 0; rendererIndex < renderers.Count; rendererIndex++)
                        {
                            var skinned = renderers[rendererIndex] as SkinnedMeshRenderer;
                            Assert.That(skinned, Is.Not.Null);
                            var baked = new Mesh();
                            try
                            {
                                skinned.BakeMesh(baked, false);
                                Assert.That(baked.vertexCount,
                                    Is.EqualTo(skinned.sharedMesh.vertexCount), skinned.name);
                                Assert.That(IsFinite(baked.bounds.size), Is.True, skinned.name);
                                Vector3 rendererScale = skinned.transform.lossyScale;
                                float sourceScale = Mathf.Max(
                                    Mathf.Abs(rendererScale.x),
                                    Mathf.Abs(rendererScale.y),
                                    Mathf.Abs(rendererScale.z));
                                Assert.That(baked.bounds.size.magnitude,
                                    Is.LessThan(skinned.sharedMesh.bounds.size.magnitude
                                        * sourceScale * 4f),
                                    $"{clip.name}: {skinned.name}");
                            }
                            finally
                            {
                                Object.DestroyImmediate(baked);
                            }
                        }
                    }
                }
            }
            finally
            {
                AnimationMode.StopAnimationMode();
            }
        }
        finally
        {
            if (root != null)
                Object.DestroyImmediate(root);
            Object.DestroyImmediate(parent);
        }
    }

    [Test]
    public void MotionProfile_CoversEveryAntelopeLoadBearingChain()
    {
        NmsProceduralMotionProfile profile =
            AssetDatabase.LoadAssetAtPath<NmsProceduralMotionProfile>(ProfilePath);
        NmsCreatureFamilyDefinition family =
            AssetDatabase.LoadAssetAtPath<NmsCreatureFamilyDefinition>(FamilyPath);
        Assert.That(profile, Is.Not.Null);
        Assert.That(family, Is.Not.Null);
        Assert.That(profile.FamilyId, Is.EqualTo(family.FamilyId));
        Assert.That(family.LoadBearingChains.Count, Is.EqualTo(4));
        for (int i = 0; i < family.LoadBearingChains.Count; i++)
        {
            NmsCreatureLegChain chain = family.LoadBearingChains[i];
            Assert.That(chain.bones, Has.Length.EqualTo(4));
            Assert.That(profile.TryGetLeg(chain.id, out NmsProceduralLegProfile leg),
                Is.True, chain.id);
            Assert.That(leg.bendHintLocal.sqrMagnitude, Is.GreaterThan(0.5f));
        }
    }

    [Test]
    public void ValidatedSource_HasSymmetricContiguousThreeSegmentLegs()
    {
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(ValidatedSourcePath);
        NmsCreatureFamilyDefinition family =
            AssetDatabase.LoadAssetAtPath<NmsCreatureFamilyDefinition>(FamilyPath);
        Assert.That(source, Is.Not.Null);
        Assert.That(family, Is.Not.Null);
        GameObject instance = Object.Instantiate(source);
        try
        {
            Transform[] hierarchy = instance.GetComponentsInChildren<Transform>(true);
            var bones = new Dictionary<string, Transform>(System.StringComparer.Ordinal);
            for (int i = 0; i < hierarchy.Length; i++)
                if (!bones.ContainsKey(hierarchy[i].name))
                    bones.Add(hierarchy[i].name, hierarchy[i]);

            var lengths = new Vector3[4];
            for (int chainIndex = 0; chainIndex < family.LoadBearingChains.Count; chainIndex++)
            {
                NmsCreatureLegChain chain = family.LoadBearingChains[chainIndex];
                Transform upper = bones[chain.bones[0]];
                Transform lower = bones[chain.bones[1]];
                Transform distal = bones[chain.bones[2]];
                Transform foot = bones[chain.bones[3]];
                Assert.That(lower.parent, Is.SameAs(upper), chain.id);
                Assert.That(distal.parent, Is.SameAs(lower), chain.id);
                Assert.That(foot.parent, Is.SameAs(distal), chain.id);
                lengths[chainIndex] = new Vector3(
                    Vector3.Distance(upper.position, lower.position),
                    Vector3.Distance(lower.position, distal.position),
                    Vector3.Distance(distal.position, foot.position));
            }

            Assert.That(lengths[0].x, Is.EqualTo(lengths[1].x).Within(0.001f));
            Assert.That(lengths[0].y, Is.EqualTo(lengths[1].y).Within(0.001f));
            Assert.That(lengths[0].z, Is.EqualTo(lengths[1].z).Within(0.001f));
            Assert.That(lengths[2].x, Is.EqualTo(lengths[3].x).Within(0.001f));
            Assert.That(lengths[2].y, Is.EqualTo(lengths[3].y).Within(0.001f));
            Assert.That(lengths[2].z, Is.EqualTo(lengths[3].z).Within(0.001f));

            AssertMirroredRestChain(instance.transform, bones,
                family.LoadBearingChains[0], family.LoadBearingChains[1]);
            AssertMirroredRestChain(instance.transform, bones,
                family.LoadBearingChains[2], family.LoadBearingChains[3]);
            Assert.That(instance.GetComponentInChildren<Animator>(true), Is.Null,
                "The procedural source must not import an Animator or AnimationClip pose.");
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void ProceduralWalkTemplate_IsCompleteAndRuntimeHasNoClipDependency()
    {
        NmsProceduralGaitTemplate template =
            AssetDatabase.LoadAssetAtPath<NmsProceduralGaitTemplate>(GaitTemplatePath);
        Assert.That(template, Is.Not.Null);
        Assert.That(template.IsValidFor("AntelopeQuadruped", 4, out string error),
            Is.True, error);
        Assert.That(template.SchemaVersion,
            Is.EqualTo(NmsProceduralGaitTemplate.CurrentSchema));
        Assert.That(template.SchemaVersion, Is.EqualTo(2));
        Assert.That(template.SampleCount, Is.EqualTo(64));
        Assert.That(template.BoneTracks, Has.Length.GreaterThanOrEqualTo(7));

        string[] legIds = { "FrontLeft", "FrontRight", "RearLeft", "RearRight" };
        for (int i = 0; i < legIds.Length; i++)
        {
            Assert.That(template.TryGetLeg(
                legIds[i], out NmsProceduralGaitLegTrack leg), Is.True, legIds[i]);
            Assert.That(leg.SampleCount, Is.EqualTo(64));
            Assert.That(leg.FirstPoleSampleCount, Is.EqualTo(64));
            Assert.That(leg.SecondPoleSampleCount, Is.EqualTo(64));
            Assert.That(leg.IsComplete(64, out string legError), Is.True, legError);

            leg.Sample(leg.LiftOffPhase,
                out Vector3 liftOffset, out _, out float liftProgress,
                out _, out _);
            leg.Sample(leg.TouchDownPhase,
                out Vector3 touchOffset, out _, out float touchProgress,
                out _, out _);
            Assert.That(Vector3.Distance(
                liftOffset, leg.NormalizedLiftOffOffset), Is.LessThan(0.0001f));
            Assert.That(Vector3.Distance(
                touchOffset, leg.NormalizedTouchDownOffset), Is.LessThan(0.0001f));
            Assert.That(liftProgress, Is.LessThan(0.001f));
            Assert.That(touchProgress, Is.GreaterThan(0.999f));

            Vector3 firstKnee = Vector3.zero;
            Vector3 firstHock = Vector3.zero;
            Vector3 previousKnee = Vector3.zero;
            Vector3 previousHock = Vector3.zero;
            for (int sample = 0; sample < 64; sample++)
            {
                leg.Sample(sample / 64f, out Vector3 footOffset,
                    out float stance, out float swing,
                    out Vector3 kneePole, out Vector3 hockPole);
                Assert.That(IsFinite(footOffset), Is.True, $"{legIds[i]} foot {sample}");
                Assert.That(kneePole.sqrMagnitude,
                    Is.EqualTo(1f).Within(0.001f), $"{legIds[i]} knee {sample}");
                Assert.That(hockPole.sqrMagnitude,
                    Is.EqualTo(1f).Within(0.001f), $"{legIds[i]} hock {sample}");
                Assert.That(stance, Is.InRange(0f, 1f));
                Assert.That(swing, Is.InRange(0f, 1f));
                if (sample == 0)
                {
                    firstKnee = kneePole;
                    firstHock = hockPole;
                }
                else
                {
                    Assert.That(Vector3.Dot(previousKnee, kneePole),
                        Is.GreaterThanOrEqualTo(0f), $"{legIds[i]} knee flip {sample}");
                    Assert.That(Vector3.Dot(previousHock, hockPole),
                        Is.GreaterThanOrEqualTo(0f), $"{legIds[i]} hock flip {sample}");
                }
                previousKnee = kneePole;
                previousHock = hockPole;
            }
            Assert.That(Vector3.Dot(previousKnee, firstKnee),
                Is.GreaterThanOrEqualTo(0f), $"{legIds[i]} knee seam");
            Assert.That(Vector3.Dot(previousHock, firstHock),
                Is.GreaterThanOrEqualTo(0f), $"{legIds[i]} hock seam");
        }

        FieldInfo[] runtimeFields = typeof(NmsProceduralMotionController).GetFields(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        for (int i = 0; i < runtimeFields.Length; i++)
            Assert.That(runtimeFields[i].FieldType, Is.Not.EqualTo(typeof(AnimationClip)),
                runtimeFields[i].Name);
    }

    static void AssertMirroredRestChain(
        Transform root, Dictionary<string, Transform> bones,
        NmsCreatureLegChain left, NmsCreatureLegChain right)
    {
        for (int i = 0; i < 4; i++)
        {
            Vector3 leftPoint = root.InverseTransformPoint(bones[left.bones[i]].position);
            Vector3 rightPoint = root.InverseTransformPoint(bones[right.bones[i]].position);
            Assert.That(leftPoint.x, Is.EqualTo(-rightPoint.x).Within(0.001f),
                $"{left.bones[i]} and {right.bones[i]} are not mirrored in X.");
            Assert.That(leftPoint.y, Is.EqualTo(rightPoint.y).Within(0.01f),
                $"{left.bones[i]} and {right.bones[i]} differ in rest height.");
            Assert.That(leftPoint.z, Is.EqualTo(rightPoint.z).Within(0.01f),
                $"{left.bones[i]} and {right.bones[i]} differ along the body axis.");
        }
    }

    static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
            && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
            && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}
#endif
