using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class ProceduralPlanetLabTests
{
    sealed class LabRig
    {
        public GameObject root;
        public ProceduralPlanetLabController controller;
        public ProceduralPlanetPreset preset;
    }

    [Test]
    public void CloneDefinitionIsDeepAndMeshIsDeterministic()
    {
        ProceduralPlanetPreset preset =
            ScriptableObject.CreateInstance<ProceduralPlanetPreset>();
        preset.ApplyTemplate(ProceduralPlanetLabTemplate.CrimsonOcean);
        GalaxyPlanetDefinition first = preset.CloneDefinition();
        GalaxyPlanetDefinition second = preset.CloneDefinition();

        Assert.AreNotSame(first.terrain, second.terrain);
        Assert.AreNotSame(first.lowPolyVisual, second.lowPolyVisual);
        Assert.AreEqual(first.seed, second.seed);
        Assert.AreEqual(first.celestial.radius, second.celestial.radius);

        Mesh firstMesh = PlanetLodMeshBuilder.Build(first, 12);
        Mesh secondMesh = PlanetLodMeshBuilder.Build(second, 12);
        try
        {
            Assert.AreEqual(firstMesh.vertexCount, secondMesh.vertexCount);
            CollectionAssert.AreEqual(firstMesh.vertices, secondMesh.vertices);
            CollectionAssert.AreEqual(firstMesh.colors, secondMesh.colors);
        }
        finally
        {
            Object.DestroyImmediate(firstMesh);
            Object.DestroyImmediate(secondMesh);
            Object.DestroyImmediate(preset);
        }
    }

    [Test]
    public void AppearanceChangesDoNotRebuildTerrain()
    {
        LabRig rig = CreateRig();
        try
        {
            rig.controller.RebuildNow(12);
            int revision = rig.controller.TerrainBuildRevision;
            Mesh terrain = rig.controller.TerrainMesh;

            rig.preset.visual.lowlandColor = Color.magenta;
            rig.preset.visual.snowLine = 0.31f;
            rig.preset.preview.sunIntensity = 1.73f;
            rig.controller.ApplyPreset(rig.preset, false);

            Assert.AreEqual(revision, rig.controller.TerrainBuildRevision);
            Assert.AreSame(terrain, rig.controller.TerrainMesh);
        }
        finally
        {
            DestroyRig(rig);
        }
    }

    [Test]
    public void OceanRadiusMatchesPresetAndShapeRebuilds()
    {
        LabRig rig = CreateRig();
        try
        {
            rig.preset.radius = 120f;
            rig.preset.maximumTerrainElevation = 20f;
            rig.preset.visual.oceanLevel = 0.04f;
            rig.controller.RebuildNow(12);
            Assert.That(rig.controller.OceanRadius, Is.EqualTo(120.8f).Within(0.001f));

            int revision = rig.controller.TerrainBuildRevision;
            rig.preset.terrain.ridgeHeight += 3f;
            rig.controller.RebuildNow(12);
            Assert.Greater(rig.controller.TerrainBuildRevision, revision);
        }
        finally
        {
            DestroyRig(rig);
        }
    }

    [Test]
    public void OneHundredRegenerationsDoNotAccumulateTransientResources()
    {
        int meshBaseline = CountLabMeshes();
        int materialBaseline = CountLabMaterials();
        LabRig rig = CreateRig();
        try
        {
            int childCount = rig.root.GetComponentsInChildren<Transform>(true).Length;
            for (int index = 0; index < 100; index++)
            {
                rig.preset.seed = 10000 + index * 7919;
                rig.controller.RebuildNow(6);
                Assert.AreEqual(
                    childCount,
                    rig.root.GetComponentsInChildren<Transform>(true).Length);
                Assert.LessOrEqual(CountLabMeshes(), meshBaseline + 2);
                Assert.LessOrEqual(CountLabMaterials(), materialBaseline + 2);
            }
        }
        finally
        {
            DestroyRig(rig);
        }

        Assert.LessOrEqual(CountLabMeshes(), meshBaseline);
        Assert.LessOrEqual(CountLabMaterials(), materialBaseline);
    }

    [TestCase(1920, 1080)]
    [TestCase(3840, 2160)]
    public void CapturePreviewWritesSupportedDisplayResolution(int width, int height)
    {
        LabRig rig = CreateRig();
        string path = Path.Combine(
            Path.GetFullPath("Temp"),
            "PlanetLabTests",
            "capture_" + width + "x" + height + ".png");
        try
        {
            rig.controller.RebuildNow(12);
            rig.controller.CapturePreview(path, width, height);
            Assert.IsTrue(File.Exists(path));
            Assert.Greater(new FileInfo(path).Length, 1024);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
            DestroyRig(rig);
        }
    }

    [Test]
    public void LaboratorySceneContainsOnlyPreviewEquipment()
    {
        ProceduralPlanetLabAssetBuilder.EnsureLaboratoryAssets();
        bool alreadyLoaded = SceneManager.GetSceneByPath(
            ProceduralPlanetLabAssetBuilder.ScenePath).isLoaded;
        Scene scene = alreadyLoaded
            ? SceneManager.GetSceneByPath(ProceduralPlanetLabAssetBuilder.ScenePath)
            : EditorSceneManager.OpenScene(
                ProceduralPlanetLabAssetBuilder.ScenePath,
                OpenSceneMode.Additive);
        try
        {
            GameObject[] roots = scene.GetRootGameObjects();
            Assert.AreEqual(1, roots.Length);
            Assert.AreEqual("PlanetLabRoot", roots[0].name);
            Assert.AreEqual(
                1,
                roots[0].GetComponentsInChildren<ProceduralPlanetLabController>(
                    true).Length);
            Assert.AreEqual(1, roots[0].GetComponentsInChildren<Camera>(true).Length);
            Assert.AreEqual(2, roots[0].GetComponentsInChildren<Light>(true).Length);
            Assert.AreEqual(
                2,
                roots[0].GetComponentsInChildren<MeshFilter>(true).Length);
            Assert.AreEqual(
                0,
                roots[0].GetComponentsInChildren<Collider>(true).Length);
            Assert.AreEqual(
                0,
                roots[0].GetComponentsInChildren<GalaxyTravelManager>(true).Length);
            Assert.AreEqual(
                0,
                roots[0].GetComponentsInChildren<VoxelQuadSphereWorld>(true).Length);
        }
        finally
        {
            if (!alreadyLoaded)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    static LabRig CreateRig()
    {
        var root = new GameObject("PlanetLabTestRoot");
        ProceduralPlanetLabController controller =
            root.AddComponent<ProceduralPlanetLabController>();

        var planet = new GameObject("Planet");
        planet.transform.SetParent(root.transform, false);
        MeshFilter terrainFilter = planet.AddComponent<MeshFilter>();
        MeshRenderer terrainRenderer = planet.AddComponent<MeshRenderer>();

        var ocean = new GameObject("Ocean");
        ocean.transform.SetParent(planet.transform, false);
        MeshFilter oceanFilter = ocean.AddComponent<MeshFilter>();
        MeshRenderer oceanRenderer = ocean.AddComponent<MeshRenderer>();

        var cameraObject = new GameObject("PreviewCamera");
        cameraObject.transform.SetParent(root.transform, false);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;

        var sunObject = new GameObject("Sun");
        sunObject.transform.SetParent(root.transform, false);
        Light sun = sunObject.AddComponent<Light>();
        sun.type = LightType.Directional;

        var fillObject = new GameObject("Fill");
        fillObject.transform.SetParent(root.transform, false);
        Light fill = fillObject.AddComponent<Light>();
        fill.type = LightType.Directional;

        controller.ConfigureSceneReferences(
            planet.transform,
            terrainFilter,
            terrainRenderer,
            oceanFilter,
            oceanRenderer,
            camera,
            sun,
            fill);
        ProceduralPlanetPreset preset =
            ScriptableObject.CreateInstance<ProceduralPlanetPreset>();
        preset.ApplyTemplate(ProceduralPlanetLabTemplate.TemperateOcean);
        controller.ApplyPreset(preset, false);
        return new LabRig
        {
            root = root,
            controller = controller,
            preset = preset
        };
    }

    static void DestroyRig(LabRig rig)
    {
        if (rig == null)
            return;
        if (rig.root != null)
            Object.DestroyImmediate(rig.root);
        if (rig.preset != null)
            Object.DestroyImmediate(rig.preset);
    }

    static int CountLabMeshes()
    {
        return Resources.FindObjectsOfTypeAll<Mesh>()
            .Count(mesh => mesh != null
                && (mesh.name.StartsWith("PlanetLabTerrain_")
                    || mesh.name.StartsWith("PlanetLabOcean_")));
    }

    static int CountLabMaterials()
    {
        return Resources.FindObjectsOfTypeAll<Material>()
            .Count(material => material != null
                && (material.name == "PlanetLabTerrainMaterial"
                    || material.name == "PlanetLabOceanMaterial"));
    }
}
