using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class PlanarDroppedObjectTests
{
    [Test]
    public void AerodynamicDragOpposesMotionAndUsesSpeedSquared()
    {
        Vector3 slow =
            PlanetaryDroppedBody.CalculateAerodynamicDrag(
                Vector3.forward * 10f,
                Quaternion.identity,
                1f,
                1.2f,
                1.05f);
        Vector3 fast =
            PlanetaryDroppedBody.CalculateAerodynamicDrag(
                Vector3.forward * 20f,
                Quaternion.identity,
                1f,
                1.2f,
                1.05f);

        Assert.Less(Vector3.Dot(slow, Vector3.forward), 0f);
        Assert.That(
            fast.magnitude / slow.magnitude,
            Is.EqualTo(4f).Within(0.001f));
        Assert.AreEqual(
            Vector3.zero,
            PlanetaryDroppedBody.CalculateAerodynamicDrag(
                Vector3.forward * 20f,
                Quaternion.identity,
                1f,
                0f,
                1.05f));
    }

    [Test]
    public void CubeSubmersionIsContinuousAcrossWaterSurface()
    {
        Assert.AreEqual(
            0f,
            PlanetaryDroppedBody.CalculateSubmersion(0.5f, 0.5f));
        Assert.That(
            PlanetaryDroppedBody.CalculateSubmersion(0f, 0.5f),
            Is.EqualTo(0.5f).Within(0.0001f));
        Assert.AreEqual(
            1f,
            PlanetaryDroppedBody.CalculateSubmersion(-0.5f, 0.5f));
    }

    [Test]
    public void ReleasePointIsOutsideLowestShipCollider()
    {
        var ship = new GameObject("ReleasePointShip");
        try
        {
            Rigidbody body = ship.AddComponent<Rigidbody>();
            BoxCollider collider = ship.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, -0.5f, 0f);
            collider.size = new Vector3(4f, 2f, 6f);
            Physics.SyncTransforms();

            Vector3 release =
                PlanarDroppedObjectSystem.CalculateReleasePoint(
                    body,
                    new Collider[] { collider },
                    Vector3.up,
                    1f);

            Assert.Less(
                release.y + 0.5f,
                collider.bounds.min.y);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(ship);
        }
    }

    [Test]
    public void ChunkCoordinateUsesCenteredOneHundredTwentyEightMeterCells()
    {
        var root = new GameObject("DroppedObjectChunkCoordinate");
        try
        {
            PlanetLabInfiniteTerrainStreamer streamer =
                root.AddComponent<PlanetLabInfiniteTerrainStreamer>();
            Assert.AreEqual(
                new Vector2Int(0, 0),
                streamer.GetChunkCoordinate(63.999d, -63.999d));
            Assert.AreEqual(
                new Vector2Int(1, -1),
                streamer.GetChunkCoordinate(64d, -64.001d));
            Assert.AreEqual(
                new Vector2Int(20, -10),
                streamer.GetChunkCoordinate(2560d, -1280d));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void VersionFifteenPlanetSnapshotRoundTripsDroppedObjectsAndCities()
    {
        string path = Path.Combine(
            Application.temporaryCachePath,
            "planet-dropped-object-" + Guid.NewGuid().ToString("N")
                + ".bin");
        try
        {
            var first = new GalaxyDroppedObjectSaveEntry
            {
                objectId = "cube-a",
                payloadTypeId =
                    PlanarDroppedObjectSystem.PlaceholderCubeTypeId,
                planarX = 123456789.25d,
                planarZ = -987654321.5d,
                planarY = 42.75f,
                rotation = Quaternion.Euler(10f, 20f, 30f),
                velocity = new Vector3(4f, -5f, 6f),
                angularVelocity = new Vector3(0.1f, 0.2f, 0.3f),
                sleeping = false
            };
            var second = new GalaxyDroppedObjectSaveEntry
            {
                objectId = "cube-b",
                payloadTypeId =
                    PlanarDroppedObjectSystem.PlaceholderCubeTypeId,
                planarX = -12d,
                planarZ = 34d,
                planarY = 5f,
                rotation = Quaternion.identity,
                sleeping = true
            };
            var data = new GalaxyPlanetSaveData
            {
                formatVersion = 15,
                planetId = "drop-test",
                seed = 7,
                chunkSize = 16,
                terrainSettings = new PlanetTerrainSettings(),
                chunks = new QuadSphereChunkSaveEntry[0],
                harvestedResourceIds = new string[0],
                resources = new GalaxyResourceSaveEntry[0],
                buildings = new GalaxyBuildingSaveEntry[0],
                harvestedSurfacePropIds = new string[0],
                surfaceProps = new GalaxySurfacePropSaveEntry[0],
                surfaceTopology =
                    PlanetSurfaceTopology.InfinitePlanar,
                droppedObjects =
                    new GalaxyDroppedObjectSaveEntry[] { first, second },
                planarCities = new[]
                {
                    new GalaxyPlanarCitySaveEntry
                    {
                        cityId = "city-a",
                        boundary = new[]
                        {
                            new GalaxyPlanarPointSaveEntry(10d, 20d),
                            new GalaxyPlanarPointSaveEntry(30d, 20d),
                            new GalaxyPlanarPointSaveEntry(20d, 40d)
                        },
                        districts = new[]
                        {
                            new GalaxyPlanarCityDistrictSaveEntry
                            {
                                districtId = "district-a",
                                anchorX = 20d,
                                anchorZ = 25d,
                                platformTopHeight = 12f,
                                boundaryRegions = new[]
                                {
                                    new GalaxyCityPolygonSaveEntry
                                    {
                                        points = new[]
                                        {
                                            new Vector2(-10f, -5f),
                                            new Vector2(10f, -5f),
                                            new Vector2(0f, 15f)
                                        }
                                    }
                                },
                                platformRegions =
                                    new GalaxyCityPolygonSaveEntry[0],
                                roads = new GalaxyCityRoadSaveEntry[0],
                                blocks = new GalaxyCityPolygonSaveEntry[0],
                                lots = new GalaxyCityPolygonSaveEntry[0],
                                buildings =
                                    new GalaxyCityBuildingSaveEntry[0]
                            }
                        }
                    }
                }
            };
            first.cityDraftId = "draft-a";
            first.cityBoundaryOrder = 2;
            first.cityBoundaryLocked = true;
            first.claimedCityId = "city-a";
            first.claimedDistrictId = "district-a";

            InvokeStatic(
                "WritePlanetBinary",
                path,
                data);
            GalaxyPlanetSaveData restored =
                (GalaxyPlanetSaveData)InvokeStatic(
                    "ReadPlanetBinary",
                    path);

            Assert.AreEqual(15, restored.formatVersion);
            Assert.AreEqual(2, restored.droppedObjects.Length);
            Assert.AreEqual(
                first.objectId,
                restored.droppedObjects[0].objectId);
            Assert.AreEqual(
                first.planarX,
                restored.droppedObjects[0].planarX);
            Assert.AreEqual(
                first.velocity,
                restored.droppedObjects[0].velocity);
            Assert.IsTrue(restored.droppedObjects[1].sleeping);
            Assert.IsTrue(
                restored.droppedObjects[0].cityBoundaryLocked);
            Assert.AreEqual(
                "district-a",
                restored.droppedObjects[0].claimedDistrictId);
            Assert.AreEqual(1, restored.planarCities.Length);
            Assert.AreEqual(
                "city-a",
                restored.planarCities[0].cityId);
            Assert.AreEqual(
                12f,
                restored.planarCities[0]
                    .districts[0].platformTopHeight);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
            if (File.Exists(path + ".tmp"))
                File.Delete(path + ".tmp");
        }
    }

    static object InvokeStatic(
        string methodName,
        params object[] arguments)
    {
        MethodInfo method = typeof(GalaxyTravelManager).GetMethod(
            methodName,
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method.Invoke(null, arguments);
    }
}
