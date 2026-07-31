using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityPlanet.ModularAssembly;

public sealed class ModularWheelDeepRecoveryTests
{
    private const float Step = 0.02f;

    private Scene testScene;
    private Scene previousActiveScene;
    private readonly List<Mesh> meshes = new List<Mesh>();

    [SetUp]
    public void SetUp()
    {
        previousActiveScene = SceneManager.GetActiveScene();
        testScene = EditorSceneManager.NewPreviewScene();
        Assert.That(testScene.GetPhysicsScene().IsValid(), Is.True);
    }

    [TearDown]
    public void TearDown()
    {
        if (previousActiveScene.IsValid())
            SceneManager.SetActiveScene(previousActiveScene);
        if (testScene.IsValid())
            EditorSceneManager.ClosePreviewScene(testScene);
        foreach (Mesh mesh in meshes)
        {
            if (mesh != null)
                Object.DestroyImmediate(mesh);
        }
        meshes.Clear();
    }

    [Test]
    public void SameMeshBridgeTunnel_DoesNotRecoverToRemoteUpperDeck()
    {
        CreateLayeredMesh(includeUpperDeck: true);
        ModularWheelRuntime wheel = CreateDeeplyBuriedWheel();
        Physics.SyncTransforms();

        Assert.That(wheel.ProbeContact(Vector3.up, Step), Is.True);
        Assert.That(wheel.ContactNormal.y, Is.GreaterThan(0.9f));
        Assert.That(
            wheel.ContactPoint.y,
            Is.EqualTo(0f).Within(0.02f),
            "The lower road is locally reachable; the disconnected upper " +
            "deck of the same MeshCollider is not.");
        Assert.That(wheel.Telemetry.penetrationDepth, Is.InRange(0.5f, 1.1f));
    }

    [Test]
    public void ConcaveMesh_DeepBurialStillRecoversUpward()
    {
        CreateLayeredMesh(includeUpperDeck: false);
        ModularWheelRuntime wheel = CreateDeeplyBuriedWheel();
        Physics.SyncTransforms();

        Assert.That(wheel.ProbeContact(Vector3.up, Step), Is.True);
        Assert.That(wheel.ContactNormal.y, Is.GreaterThan(0.9f));
        Assert.That(wheel.ContactPoint.y, Is.EqualTo(0f).Within(0.02f));
        Assert.That(wheel.Telemetry.penetrationDepth, Is.GreaterThan(0.5f));
        Assert.That(
            wheel.transform.position.y -
            wheel.CurrentVisualDroop -
            wheel.Profile.radius,
            Is.GreaterThanOrEqualTo(-0.02f));
    }

    [Test]
    public void OneSidedMesh_FullyCrossedTreadUsesReachableHistory()
    {
        CreateSingleSidedGround();
        ModularWheelRuntime wheel = CreateDeeplyBuriedWheel(0.6f);
        Rigidbody body = wheel.GetComponentInParent<Rigidbody>();
        Physics.SyncTransforms();

        Assert.That(wheel.ProbeContact(Vector3.up, Step), Is.True);
        Assert.That(wheel.ContactPoint.y, Is.EqualTo(0f).Within(0.02f));

        body.transform.position = new Vector3(0f, -0.05f, 0f);
        Physics.SyncTransforms();

        Assert.That(wheel.ProbeContact(Vector3.up, Step), Is.True);
        Assert.That(wheel.ContactNormal.y, Is.GreaterThan(0.9f));
        Assert.That(wheel.ContactPoint.y, Is.EqualTo(0f).Within(0.02f));
        Assert.That(wheel.Telemetry.penetrationDepth, Is.GreaterThan(0.4f));
    }

    private ModularWheelRuntime CreateDeeplyBuriedWheel(
        float bodyHeight = -0.3f)
    {
        GameObject bodyObject = new GameObject("DeepRecoveryBody");
        SceneManager.MoveGameObjectToScene(bodyObject, testScene);
        bodyObject.transform.position = new Vector3(0f, bodyHeight, 0f);
        Rigidbody body = bodyObject.AddComponent<Rigidbody>();
        body.useGravity = false;
        body.mass = 500f;

        GameObject wheelObject = new GameObject("DeepRecoveryWheel");
        wheelObject.transform.SetParent(bodyObject.transform, false);
        ModularWheelRuntime wheel =
            wheelObject.AddComponent<ModularWheelRuntime>();
        wheel.Configure(
            new ModularContentRecord
            {
                neoXId = "wheel_basic_111",
                wheelAxleLocal = new[] { 1f, 0f, 0f },
                wheelRollingForwardLocal = new[] { 0f, 0f, 1f }
            },
            WheelRoleSettings.Serialize(WheelRoleOverride.Auto));
        wheel.BindVehicle(body, body.transform);
        wheel.SetSprungMass(500f);
        wheel.SetFlightMode(true);
        return wheel;
    }

    private void CreateLayeredMesh(bool includeUpperDeck)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        AddClosedSlab(vertices, triangles, -1f, 0f);
        if (includeUpperDeck)
            AddClosedSlab(vertices, triangles, 2.5f, 2.7f);

        var mesh = new Mesh
        {
            name = includeUpperDeck
                ? "SameColliderBridgeTunnel"
                : "ConcaveDeepRecoverySlab"
        };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        meshes.Add(mesh);

        GameObject ground = new GameObject("LayeredMeshGround");
        SceneManager.MoveGameObjectToScene(ground, testScene);
        MeshCollider collider = ground.AddComponent<MeshCollider>();
        collider.sharedMesh = mesh;
        collider.convex = false;
    }

    private void CreateSingleSidedGround()
    {
        var mesh = new Mesh
        {
            name = "OneSidedRecoveryGround",
            vertices = new[]
            {
                new Vector3(-4f, 0f, -4f),
                new Vector3(4f, 0f, -4f),
                new Vector3(4f, 0f, 4f),
                new Vector3(-4f, 0f, 4f)
            },
            triangles = new[] { 0, 3, 1, 1, 3, 2 }
        };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        meshes.Add(mesh);

        GameObject ground = new GameObject("OneSidedMeshGround");
        SceneManager.MoveGameObjectToScene(ground, testScene);
        MeshCollider collider = ground.AddComponent<MeshCollider>();
        collider.sharedMesh = mesh;
        collider.convex = false;
    }

    private static void AddClosedSlab(
        List<Vector3> vertices,
        List<int> triangles,
        float bottom,
        float top)
    {
        const float halfWidth = 4f;
        const float halfLength = 4f;
        int first = vertices.Count;
        vertices.Add(new Vector3(-halfWidth, bottom, -halfLength));
        vertices.Add(new Vector3(halfWidth, bottom, -halfLength));
        vertices.Add(new Vector3(halfWidth, bottom, halfLength));
        vertices.Add(new Vector3(-halfWidth, bottom, halfLength));
        vertices.Add(new Vector3(-halfWidth, top, -halfLength));
        vertices.Add(new Vector3(halfWidth, top, -halfLength));
        vertices.Add(new Vector3(halfWidth, top, halfLength));
        vertices.Add(new Vector3(-halfWidth, top, halfLength));

        AddFace(triangles, first, 4, 7, 5, 5, 7, 6);
        AddFace(triangles, first, 0, 1, 3, 1, 2, 3);
        AddFace(triangles, first, 0, 4, 1, 1, 4, 5);
        AddFace(triangles, first, 3, 2, 7, 2, 6, 7);
        AddFace(triangles, first, 0, 3, 4, 3, 7, 4);
        AddFace(triangles, first, 1, 5, 2, 2, 5, 6);
    }

    private static void AddFace(
        List<int> triangles,
        int first,
        int a,
        int b,
        int c,
        int d,
        int e,
        int f)
    {
        triangles.Add(first + a);
        triangles.Add(first + b);
        triangles.Add(first + c);
        triangles.Add(first + d);
        triangles.Add(first + e);
        triangles.Add(first + f);
    }
}
