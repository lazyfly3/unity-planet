using CityGeneration;
using NUnit.Framework;
using UnityEngine;

public sealed class PlanarCityRuntimeTests
{
    [Test]
    public void ConstructionJobConvertsLocalPlanToFloatingWorldRoot()
    {
        var root = new GameObject("CitySpaceRoot");
        try
        {
            root.transform.position =
                new Vector3(120f, 7f, -340f);
            var job = new CityConstructionJob(
                new[]
                {
                    new Vector2(-5f, -5f),
                    new Vector2(5f, -5f),
                    new Vector2(0f, 5f)
                },
                Vector2.zero,
                0f,
                1f,
                2f,
                root.transform);

            Assert.AreEqual(
                new Vector3(123f, 11f, -338f),
                job.ToWorldPoint(new Vector2(3f, 2f), 4f));
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void SidePresentationLooksAcrossShortAxisSoLongAxisFillsFrame()
    {
        var cameraObject = new GameObject(
            "CityPresentationCamera",
            typeof(Camera));
        var target = new GameObject("CityPresentationTarget");
        try
        {
            Camera camera = cameraObject.GetComponent<Camera>();
            cameraObject.transform.position =
                new Vector3(0f, 30f, -80f);
            SurfaceFlightCameraRig rig =
                cameraObject.AddComponent<SurfaceFlightCameraRig>();
            var bounds = new Bounds(
                new Vector3(0f, 10f, 0f),
                new Vector3(200f, 40f, 60f));

            Assert.IsTrue(rig.BeginCityPresentation(
                camera,
                target.transform,
                bounds));
            Assert.That(
                Mathf.Abs(rig.CityViewDirectionLocal.z),
                Is.EqualTo(1f).Within(0.001f));
            Assert.That(
                Mathf.Abs(rig.CityViewDirectionLocal.x),
                Is.LessThan(0.001f));
            rig.ForceEndCityPresentation();
        }
        finally
        {
            Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(target);
        }
    }

    [Test]
    public void GroundCityPresentationOwnsCameraUntilExactRestore()
    {
        var playerObject = new GameObject("CameraOwnerPlayer");
        var cameraObject = new GameObject(
            "GroundCamera",
            typeof(Camera));
        var cityTarget = new GameObject("CityTarget");
        try
        {
            cameraObject.transform.SetParent(
                playerObject.transform,
                false);
            cameraObject.transform.localPosition =
                new Vector3(0f, 1.6f, 0f);
            cameraObject.transform.localRotation =
                Quaternion.Euler(12f, 0f, 0f);
            VoxelPlanetPlayerController player =
                playerObject.AddComponent<
                    VoxelPlanetPlayerController>();
            SurfaceFlightCameraRig rig =
                cameraObject.AddComponent<SurfaceFlightCameraRig>();

            Assert.IsTrue(rig.BeginCityPresentation(
                cameraObject.GetComponent<Camera>(),
                cityTarget.transform,
                new Bounds(
                    Vector3.zero,
                    new Vector3(80f, 20f, 50f))));
            Assert.IsTrue(player.ExternalCameraControlActive);
            Assert.IsNull(cameraObject.transform.parent);
            Assert.IsNotNull(
                cameraObject.GetComponent<
                    PlanetFloatingOriginParticipant>());

            rig.ForceEndCityPresentation();

            Assert.IsFalse(player.ExternalCameraControlActive);
            Assert.AreSame(
                playerObject.transform,
                cameraObject.transform.parent);
            Assert.That(
                cameraObject.transform.localPosition,
                Is.EqualTo(new Vector3(0f, 1.6f, 0f)));
            Assert.That(
                Quaternion.Angle(
                    cameraObject.transform.localRotation,
                    Quaternion.Euler(12f, 0f, 0f)),
                Is.LessThan(0.001f));
        }
        finally
        {
            Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(playerObject);
            Object.DestroyImmediate(cityTarget);
        }
    }

    [Test]
    public void CitySaveCollectionPreservesDoublePrecisionAnchorsAndPlan()
    {
        var source = new GalaxyPlanarCitySaveCollection
        {
            cities = new[]
            {
                new GalaxyPlanarCitySaveEntry
                {
                    cityId = "city",
                    districts = new[]
                    {
                        new GalaxyPlanarCityDistrictSaveEntry
                        {
                            districtId = "district",
                            anchorX = 123456789.125d,
                            anchorZ = -987654321.875d,
                            roads = new[]
                            {
                                new GalaxyCityRoadSaveEntry
                                {
                                    start = new Vector2(-5f, 0f),
                                    end = new Vector2(8f, 2f),
                                    width = 5f,
                                    connector = true,
                                    startHeight = 12f,
                                    endHeight = 14f
                                }
                            }
                        }
                    }
                }
            }
        };

        GalaxyPlanarCitySaveCollection restored =
            JsonUtility.FromJson<GalaxyPlanarCitySaveCollection>(
                JsonUtility.ToJson(source));

        Assert.AreEqual(
            source.cities[0].districts[0].anchorX,
            restored.cities[0].districts[0].anchorX);
        Assert.AreEqual(
            source.cities[0].districts[0].anchorZ,
            restored.cities[0].districts[0].anchorZ);
        Assert.IsTrue(
            restored.cities[0].districts[0].roads[0].connector);
    }
}
