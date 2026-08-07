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
    public void PlanarPatchIsDeterministicAndSamplesSphereAtItsCenter()
    {
        ProceduralPlanetPreset preset =
            ScriptableObject.CreateInstance<ProceduralPlanetPreset>();
        preset.ApplyTemplate(ProceduralPlanetLabTemplate.TemperateOcean);
        preset.radius = 1200f;
        preset.planar.autoAnchor = false;
        preset.planar.anchorLatitude = 28f;
        preset.planar.anchorLongitude = -64f;
        GalaxyPlanetDefinition definition = preset.CloneDefinition();
        PlanetLabPlanarPatchBuildResult first =
            PlanetLabPlanarPatchMeshBuilder.Build(definition, preset.planar, 12);
        PlanetLabPlanarPatchBuildResult second =
            PlanetLabPlanarPatchMeshBuilder.Build(definition, preset.planar, 12);
        try
        {
            Assert.AreEqual(
                first.terrainMesh.vertexCount,
                second.terrainMesh.vertexCount);
            CollectionAssert.AreEqual(
                first.terrainMesh.vertices,
                second.terrainMesh.vertices);
            Assert.That(
                first.terrainMesh.bounds.size.x,
                Is.EqualTo(PlanetLabPlanarSettings.PatchSize).Within(0.001f));
            Assert.That(
                first.terrainMesh.bounds.size.z,
                Is.EqualTo(PlanetLabPlanarSettings.PatchSize).Within(0.001f));

            float expectedCenterHeight =
                VoxelQuadSphereTerrain.GetSurfaceNoise(
                    preset.planar.AnchorDirection * preset.radius,
                    preset.seed,
                    preset.terrain);
            Assert.That(
                first.centerHeight,
                Is.EqualTo(expectedCenterHeight).Within(0.0001f));
            Assert.That(
                first.spawnPosition.y,
                Is.GreaterThan(first.centerHeight));
            Assert.Greater(first.oceanMesh.bounds.size.y, 1f);
            Assert.IsTrue(first.terrainMesh.vertices.All(vertex =>
                float.IsFinite(vertex.x)
                && float.IsFinite(vertex.y)
                && float.IsFinite(vertex.z)));
            Assert.IsTrue(first.terrainMesh.normals.All(normal =>
                float.IsFinite(normal.x)
                && float.IsFinite(normal.y)
                && float.IsFinite(normal.z)));
        }
        finally
        {
            Object.DestroyImmediate(first.terrainMesh);
            Object.DestroyImmediate(first.oceanMesh);
            Object.DestroyImmediate(second.terrainMesh);
            Object.DestroyImmediate(second.oceanMesh);
            Object.DestroyImmediate(preset);
        }
    }

    [Test]
    public void AutomaticPlanarAnchorIsStableAndSelectsLand()
    {
        ProceduralPlanetPreset preset =
            ScriptableObject.CreateInstance<ProceduralPlanetPreset>();
        preset.ApplyTemplate(ProceduralPlanetLabTemplate.TemperateOcean);
        preset.radius = 1200f;
        GalaxyPlanetDefinition definition = preset.CloneDefinition();
        try
        {
            Vector3 first =
                PlanetLabPlanarPatchMeshBuilder.FindBestLandAnchor(definition);
            Vector3 second =
                PlanetLabPlanarPatchMeshBuilder.FindBestLandAnchor(definition);
            float height = VoxelQuadSphereTerrain.GetSurfaceNoise(
                first * preset.radius,
                preset.seed,
                preset.terrain);
            float seaHeight = preset.visual.oceanLevel
                * preset.maximumTerrainElevation;

            Assert.That(first.magnitude, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(Vector3.Distance(first, second), Is.LessThan(0.0001f));
            Assert.That(height, Is.GreaterThan(seaHeight));
        }
        finally
        {
            Object.DestroyImmediate(preset);
        }
    }

    [Test]
    public void SurfaceModeSwitchBuildsPlanarColliderAndPreservesGlobeMesh()
    {
        LabRig rig = CreateRig();
        try
        {
            rig.controller.RebuildNow(12);
            Mesh globeMesh = rig.controller.TerrainMesh;

            rig.controller.SetSurfaceMode(PlanetLabSurfaceMode.Planar);
            Assert.AreSame(globeMesh, rig.controller.TerrainMesh);
            Assert.IsNotNull(rig.controller.PlanarTerrainMesh);
            Assert.IsNotNull(
                rig.root.GetComponentInChildren<MeshCollider>(true).sharedMesh);
            Assert.IsFalse(rig.root.transform.Find("Planet").gameObject.activeSelf);
            Assert.IsTrue(
                rig.root.transform.Find("PlanarExperiment").gameObject.activeSelf);

            rig.controller.SetSurfaceMode(PlanetLabSurfaceMode.Globe);
            Assert.IsTrue(rig.root.transform.Find("Planet").gameObject.activeSelf);
            Assert.IsFalse(
                rig.root.transform.Find("PlanarExperiment").gameObject.activeSelf);
        }
        finally
        {
            DestroyRig(rig);
        }
    }

    [Test]
    public void InfinitePlanarChunksAreDeterministicAndShareSeamHeights()
    {
        ProceduralPlanetPreset preset =
            ScriptableObject.CreateInstance<ProceduralPlanetPreset>();
        preset.ApplyTemplate(ProceduralPlanetLabTemplate.TemperateOcean);
        preset.radius = 1200f;
        preset.planar.autoAnchor = false;
        preset.planar.anchorLatitude = 28f;
        preset.planar.anchorLongitude = -64f;
        GalaxyPlanetDefinition definition = preset.CloneDefinition();
        Vector3 anchor = preset.planar.AnchorDirection;
        PlanetLabPlanarPatchMeshBuilder.BuildTangentBasis(
            anchor,
            out Vector3 east,
            out Vector3 north);

        Mesh first = PlanetLabInfiniteTerrainStreamer.BuildInfiniteChunkMesh(
            definition, anchor, east, north, Vector2Int.zero, 128f, 8);
        Mesh repeated = PlanetLabInfiniteTerrainStreamer.BuildInfiniteChunkMesh(
            definition, anchor, east, north, Vector2Int.zero, 128f, 8);
        Mesh neighbor = PlanetLabInfiniteTerrainStreamer.BuildInfiniteChunkMesh(
            definition, anchor, east, north, Vector2Int.right, 128f, 8);
        try
        {
            CollectionAssert.AreEqual(first.vertices, repeated.vertices);
            CollectionAssert.AreEqual(first.normals, repeated.normals);

            const int vertexSide = 9;
            int center = 4 * vertexSide + 4;
            float expectedCenter = VoxelQuadSphereTerrain.GetSurfaceNoise(
                anchor * preset.radius,
                preset.seed,
                preset.terrain);
            Assert.That(
                first.vertices[center].y,
                Is.EqualTo(expectedCenter).Within(0.0001f));

            for (int z = 0; z < vertexSide; z++)
            {
                Vector3 leftEdge = first.vertices[z * vertexSide + 8];
                Vector3 rightEdge = neighbor.vertices[z * vertexSide];
                Assert.That(leftEdge.y, Is.EqualTo(rightEdge.y).Within(0.0001f));
                Assert.That(
                    Vector3.Distance(
                        first.normals[z * vertexSide + 8],
                        neighbor.normals[z * vertexSide]),
                    Is.LessThan(0.0001f));
            }
        }
        finally
        {
            Object.DestroyImmediate(first);
            Object.DestroyImmediate(repeated);
            Object.DestroyImmediate(neighbor);
            Object.DestroyImmediate(preset);
        }
    }

    [Test]
    public void InfiniteModeBuildsBoundedStreamingGridAndKeepsGlobeMesh()
    {
        LabRig rig = CreateRig();
        try
        {
            rig.controller.RebuildNow(12);
            Mesh globeMesh = rig.controller.TerrainMesh;
            var orphanedChunk = new GameObject("Chunk");
            orphanedChunk.transform.SetParent(
                rig.controller.InfiniteTerrainStreamer.transform,
                false);
            rig.controller.SetSurfaceMode(PlanetLabSurfaceMode.InfinitePlanar);

            Assert.AreSame(globeMesh, rig.controller.TerrainMesh);
            Assert.IsTrue(orphanedChunk == null);
            Assert.AreEqual(
                25,
                rig.controller.InfiniteTerrainStreamer.ActiveChunkCount);
            Assert.AreEqual(
                25,
                rig.controller.InfiniteTerrainStreamer.CreatedChunkCount);
            Mesh oceanMesh =
                rig.controller.InfiniteTerrainStreamer.InfiniteOceanMesh;
            Assert.IsNotNull(oceanMesh);
            Assert.AreEqual(
                (PlanetLabPlanarSettings.InfiniteChunkResolution + 1)
                * (PlanetLabPlanarSettings.InfiniteChunkResolution + 1),
                oceanMesh.vertexCount);
            Assert.Greater(oceanMesh.bounds.size.y, 1f);
            Assert.IsFalse(rig.root.transform.Find("Planet").gameObject.activeSelf);
            Assert.IsTrue(
                rig.root.transform.Find("PlanarExperiment").gameObject.activeSelf);
            Assert.IsTrue(
                rig.root.transform
                    .Find("PlanarExperiment/InfiniteTerrain")
                    .gameObject.activeSelf);

            rig.controller.SetSurfaceMode(PlanetLabSurfaceMode.Planar);
            Assert.IsFalse(
                rig.root.transform
                    .Find("PlanarExperiment/InfiniteTerrain")
                    .gameObject.activeSelf);
            Assert.IsTrue(
                rig.root.transform
                    .Find("PlanarExperiment/Terrain")
                    .gameObject.activeSelf);
        }
        finally
        {
            DestroyRig(rig);
        }
    }

    [Test]
    public void PbrLibraryContainsAllFortyDeterministicMaterialSets()
    {
        PlanetLabTerrainPbrLibrary library =
            PlanetLabTerrainPbrLibraryBuilder.EnsureLibrary();

        Assert.IsNotNull(library);
        Assert.IsTrue(library.IsReady);
        Assert.AreEqual(
            PlanetLabTerrainPbrLibrary.ExpectedMaterialCount,
            library.entries.Count);
        Assert.AreEqual(
            PlanetLabTerrainPbrLibrary.ExpectedMaterialCount,
            library.entries.Select(entry => entry.id).Distinct().Count());
        Assert.AreEqual(
            PlanetLabTerrainPbrLibrary.ExpectedMaterialCount,
            library.entries.Select(entry => entry.slice).Distinct().Count());
        Assert.AreEqual(
            PlanetLabTerrainPbrLibrary.ExpectedMaterialCount,
            library.albedoArray.depth);
        Assert.AreEqual(
            PlanetLabTerrainPbrLibrary.ExpectedMaterialCount,
            library.normalArray.depth);
        Assert.AreEqual(
            PlanetLabTerrainPbrLibrary.ExpectedMaterialCount,
            library.maskArray.depth);

        foreach (PlanetLabTerrainPbrRole role in new[]
                 {
                     PlanetLabTerrainPbrRole.Ground,
                     PlanetLabTerrainPbrRole.Rock,
                     PlanetLabTerrainPbrRole.Shore,
                     PlanetLabTerrainPbrRole.Cold
                 })
        {
            int first = library.SelectSlice(7319, role, 101 + (int)role);
            int second = library.SelectSlice(7319, role, 101 + (int)role);
            Assert.AreEqual(first, second);
            Assert.That(first, Is.InRange(0, library.entries.Count - 1));
            Assert.AreNotEqual(
                0,
                library.entries[first].roles & role,
                $"Selected slice {first} is not classified for {role}.");
        }
    }

    [Test]
    public void SkyboxLibraryHasFiveDeterministicChoicesPerPlanetTemplate()
    {
        PlanetLabSkyboxLibrary library =
            PlanetLabSkyboxLibraryBuilder.EnsureLibrary();

        Assert.IsNotNull(library);
        Assert.IsTrue(library.IsReady);
        Assert.AreEqual(
            PlanetLabSkyboxLibrary.ExpectedEntryCount,
            library.entries.Count);
        Assert.AreEqual(
            PlanetLabSkyboxLibrary.ExpectedEntryCount,
            library.entries.Select(entry => entry.id).Distinct().Count());
        foreach (ProceduralPlanetLabTemplate template
                 in System.Enum.GetValues(typeof(ProceduralPlanetLabTemplate)))
        {
            Assert.AreEqual(
                PlanetLabSkyboxLibrary.CandidatesPerTemplate,
                library.entries.Count(entry => entry.template == template));
            PlanetLabSkyboxEntry first = library.SelectEntry(7319, template);
            PlanetLabSkyboxEntry second = library.SelectEntry(7319, template);
            Assert.IsNotNull(first);
            Assert.AreSame(first, second);
            Assert.AreEqual(template, first.template);
            Assert.IsNotNull(first.material);
        }

        foreach (PlanetLabSkyboxEntry entry in library.entries)
        {
            Assert.IsNotNull(entry.material, entry.id);
            Assert.IsNotNull(entry.material.shader, entry.id);
            Assert.AreEqual(
                "Skybox/6 Sided",
                entry.material.shader.name,
                entry.id);
            foreach (string textureName in new[]
                     {
                         "_FrontTex",
                         "_BackTex",
                         "_LeftTex",
                         "_RightTex",
                         "_UpTex",
                         "_DownTex"
                     })
            {
                Assert.IsNotNull(
                    entry.material.GetTexture(textureName),
                    entry.id + " " + textureName);
            }
        }
    }

    [Test]
    public void SkyboxIsSharedByFixedAndInfinitePlanarButNotGlobe()
    {
        Material previous = RenderSettings.skybox;
        LabRig rig = CreateRig();
        try
        {
            PlanetLabSkyboxLibrary library =
                PlanetLabSkyboxLibraryBuilder.EnsureLibrary();
            rig.controller.ConfigureSkyboxLibrary(library);

            rig.controller.SetSurfaceMode(PlanetLabSurfaceMode.Planar);
            Material fixedSkybox = rig.controller.CurrentPlanarSkybox;
            Assert.IsNotNull(fixedSkybox);
            Assert.AreSame(fixedSkybox, RenderSettings.skybox);

            rig.controller.SetSurfaceMode(PlanetLabSurfaceMode.InfinitePlanar);
            Assert.AreSame(fixedSkybox, rig.controller.CurrentPlanarSkybox);
            Assert.AreSame(fixedSkybox, RenderSettings.skybox);

            rig.controller.SetSurfaceMode(PlanetLabSurfaceMode.Globe);
            Assert.IsNull(RenderSettings.skybox);
        }
        finally
        {
            DestroyRig(rig);
            RenderSettings.skybox = previous;
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
    public void LaboratorySceneContainsOnlyIsolatedExperimentEquipment()
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
            ProceduralPlanetLabController controller =
                roots[0].GetComponent<ProceduralPlanetLabController>();
            Assert.IsNotNull(controller.PlanarPbrLibrary);
            Assert.IsTrue(controller.PlanarPbrLibrary.IsReady);
            Assert.IsNotNull(controller.PlanarSkyboxLibrary);
            Assert.IsTrue(controller.PlanarSkyboxLibrary.IsReady);
            Assert.AreEqual(2, roots[0].GetComponentsInChildren<Camera>(true).Length);
            Assert.AreEqual(2, roots[0].GetComponentsInChildren<Light>(true).Length);
            Assert.AreEqual(
                4,
                roots[0].GetComponentsInChildren<MeshFilter>(true)
                    .Count(filter =>
                        filter.GetComponentInParent<
                            PlanetLabInfiniteTerrainStreamer>(true) == null));
            Assert.AreEqual(
                1,
                roots[0].GetComponentsInChildren<MeshCollider>(true)
                    .Count(collider =>
                        collider.GetComponentInParent<
                            PlanetLabInfiniteTerrainStreamer>(true) == null));
            Assert.AreEqual(
                1,
                roots[0].GetComponentsInChildren<CharacterController>(true).Length);
            Assert.AreEqual(
                1,
                roots[0].GetComponentsInChildren<PlanetLabFirstPersonController>(
                    true).Length);
            Assert.AreEqual(
                1,
                roots[0].GetComponentsInChildren<PlanetLabInfiniteTerrainStreamer>(
                    true).Length);
            Assert.IsNotNull(roots[0].transform.Find("Planet"));
            Assert.IsNotNull(roots[0].transform.Find("PlanarExperiment"));
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

        var planarRoot = new GameObject("PlanarExperiment");
        planarRoot.transform.SetParent(root.transform, false);
        var planarTerrain = new GameObject("Terrain");
        planarTerrain.transform.SetParent(planarRoot.transform, false);
        MeshFilter planarTerrainFilter = planarTerrain.AddComponent<MeshFilter>();
        MeshRenderer planarTerrainRenderer =
            planarTerrain.AddComponent<MeshRenderer>();
        MeshCollider planarTerrainCollider =
            planarTerrain.AddComponent<MeshCollider>();
        var planarOcean = new GameObject("Ocean");
        planarOcean.transform.SetParent(planarRoot.transform, false);
        MeshFilter planarOceanFilter = planarOcean.AddComponent<MeshFilter>();
        MeshRenderer planarOceanRenderer =
            planarOcean.AddComponent<MeshRenderer>();
        var infiniteTerrain = new GameObject("InfiniteTerrain");
        infiniteTerrain.transform.SetParent(planarRoot.transform, false);
        PlanetLabInfiniteTerrainStreamer infiniteTerrainStreamer =
            infiniteTerrain.AddComponent<PlanetLabInfiniteTerrainStreamer>();
        var player = new GameObject("PlanarPlayer");
        player.transform.SetParent(planarRoot.transform, false);
        player.AddComponent<CharacterController>();
        PlanetLabFirstPersonController playerController =
            player.AddComponent<PlanetLabFirstPersonController>();
        var playerCameraObject = new GameObject("FirstPersonCamera");
        playerCameraObject.transform.SetParent(player.transform, false);
        Camera playerCamera = playerCameraObject.AddComponent<Camera>();
        playerCamera.enabled = false;
        playerController.Configure(playerCameraObject.transform, Vector3.zero);

        controller.ConfigureSceneReferences(
            planet.transform,
            terrainFilter,
            terrainRenderer,
            oceanFilter,
            oceanRenderer,
            camera,
            sun,
            fill);
        controller.ConfigurePlanarReferences(
            planarRoot,
            planarTerrainFilter,
            planarTerrainRenderer,
            planarTerrainCollider,
            planarOceanFilter,
            planarOceanRenderer,
            player,
            playerController,
            playerCamera,
            infiniteTerrain,
            infiniteTerrainStreamer);
        controller.ConfigureSkyboxLibrary(
            PlanetLabSkyboxLibraryBuilder.EnsureLibrary());
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
                    || mesh.name.StartsWith("PlanetLabOcean_")
                    || mesh.name.StartsWith("PlanetLabPlanarTerrain_")
                    || mesh.name.StartsWith("PlanetLabPlanarOcean_")));
    }

    static int CountLabMaterials()
    {
        return Resources.FindObjectsOfTypeAll<Material>()
            .Count(material => material != null
                && (material.name == "PlanetLabTerrainMaterial"
                    || material.name == "PlanetLabOceanMaterial"
                    || material.name == "PlanetLabPlanarTerrainMaterial"
                    || material.name == "PlanetLabPlanarOceanMaterial"));
    }
}
