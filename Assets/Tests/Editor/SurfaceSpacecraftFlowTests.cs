using NUnit.Framework;
using UnityEngine;

public sealed class SurfaceSpacecraftFlowTests
{
    [TearDown]
    public void TearDown()
    {
        PendingSurfaceDepartureContext.Clear();
        Time.timeScale = 1f;
    }

    [Test]
    public void SurfaceSpacecraftStateNormalizesRadialFrame()
    {
        var state = new SurfaceSpacecraftState
        {
            valid = true,
            radialDirection = new Vector3(0f, 5f, 0f),
            tangentForward = new Vector3(1f, 2f, 0f),
            hoverAltitude = 200f
        };

        state.ClampValues();

        Assert.That(state.radialDirection.magnitude, Is.EqualTo(1f).Within(0.0001f));
        Assert.That(state.tangentForward.magnitude, Is.EqualTo(1f).Within(0.0001f));
        Assert.That(
            Vector3.Dot(state.radialDirection, state.tangentForward),
            Is.EqualTo(0f).Within(0.0001f));
        Assert.AreEqual(40f, state.hoverAltitude);
    }

    [Test]
    public void DepartureContextCanOnlyBeConsumedOnce()
    {
        Quaternion rotation = Quaternion.Euler(12f, 34f, 5f);
        Vector3 velocity = new Vector3(0f, 120f, 0f);
        PendingSurfaceDepartureContext.Set(rotation, velocity);

        Assert.IsTrue(PendingSurfaceDepartureContext.TryConsume(
            out Quaternion consumedRotation,
            out Vector3 consumedVelocity));
        Assert.AreEqual(rotation, consumedRotation);
        Assert.AreEqual(velocity, consumedVelocity);
        Assert.IsFalse(PendingSurfaceDepartureContext.TryConsume(out _, out _));
    }

    [Test]
    public void RecallDurationIsAlwaysFourToEightSeconds()
    {
        Assert.AreEqual(4f, SurfaceSpacecraftController.CalculateRecallDuration(0f));
        Assert.AreEqual(6f, SurfaceSpacecraftController.CalculateRecallDuration(90f));
        Assert.AreEqual(8f, SurfaceSpacecraftController.CalculateRecallDuration(180f));
        Assert.AreEqual(8f, SurfaceSpacecraftController.CalculateRecallDuration(720f));
    }

    [Test]
    public void HoverRootPositionRejectsKilometreScaleVisualBounds()
    {
        Vector3 player = new Vector3(100f, 200f, 300f);
        Vector3 root = SurfaceSpacecraftController.CalculateHoverRootPosition(
            player,
            Vector3.right,
            Vector3.up,
            -2000f,
            10f,
            6f);

        Assert.AreEqual(10f, player.x - root.x, 0.001f);
        Assert.AreEqual(66f, root.y - player.y, 0.001f);
        Assert.AreEqual(player.z, root.z, 0.001f);
        Assert.Less(Vector3.Distance(player, root), 80f);
    }

    [Test]
    public void GreatCircleSamplingStaysOnPlanetShell()
    {
        Vector3 midpoint = SurfaceSpacecraftController.SampleGreatCircleDirection(
            Vector3.up,
            Vector3.forward,
            0.5f,
            Vector3.right);

        Assert.That(midpoint.magnitude, Is.EqualTo(1f).Within(0.0001f));
        Assert.That(Vector3.Angle(Vector3.up, midpoint), Is.EqualTo(45f).Within(0.01f));
        Assert.That(Vector3.Angle(midpoint, Vector3.forward), Is.EqualTo(45f).Within(0.01f));
    }

    [Test]
    public void SurfaceMenuBuildsProceduralRadialGraphic()
    {
        var canvasObject = new GameObject(
            "SurfaceMenuTestCanvas",
            typeof(RectTransform),
            typeof(Canvas));
        var radialObject = new GameObject(
            "SurfaceMenuTestRadial",
            typeof(RectTransform));
        try
        {
            radialObject.transform.SetParent(canvasObject.transform, false);
            SurfaceRadialMenuGraphic graphic =
                radialObject.AddComponent<SurfaceRadialMenuGraphic>();
            graphic.SetState(true, true);
            Canvas.ForceUpdateCanvases();
            Assert.IsNotNull(graphic);
            Assert.IsTrue(graphic.enabled);
            Assert.IsNotNull(radialObject.GetComponent<CanvasRenderer>());
        }
        finally
        {
            Object.DestroyImmediate(radialObject);
            Object.DestroyImmediate(canvasObject);
        }
    }

    [Test]
    public void SurfaceRadialMenuResolvesCallAndScanSectors()
    {
        Assert.AreEqual(
            SurfaceRadialAction.CallSpacecraft,
            SurfaceMultifunctionController.ResolveAction(
                new Vector2(0f, 100f)));
        Assert.AreEqual(
            SurfaceRadialAction.Scan,
            SurfaceMultifunctionController.ResolveAction(
                new Vector2(100f, 0f)));
        Assert.IsNull(
            SurfaceMultifunctionController.ResolveAction(
                new Vector2(5f, 5f)));
        Assert.IsNull(
            SurfaceMultifunctionController.ResolveAction(
                new Vector2(-100f, 0f)));
    }

    [Test]
    public void SurfaceRadialMenuSupportsDynamicActionCounts()
    {
        const int actionCount = 7;
        for (int i = 0; i < actionCount; i++)
        {
            float angle = SurfaceRadialMenuGraphic.GetActionCenterAngle(
                i,
                actionCount) * Mathf.Deg2Rad;
            Vector2 pointer = new Vector2(
                Mathf.Cos(angle),
                Mathf.Sin(angle)) * 100f;
            Assert.AreEqual(
                i,
                SurfaceMultifunctionController.ResolveActionIndex(
                    pointer,
                    actionCount));
        }
    }

    [Test]
    public void SurfaceRadialMenuPaginatesWithoutActionLimit()
    {
        Assert.AreEqual(
            1,
            SurfaceMultifunctionController.GetPageCount(0));
        Assert.AreEqual(
            1,
            SurfaceMultifunctionController.GetPageCount(8));
        Assert.AreEqual(
            2,
            SurfaceMultifunctionController.GetPageCount(9));
        Assert.AreEqual(
            16,
            SurfaceMultifunctionController.GetPageCount(128));
    }

    [Test]
    public void ScannerFormatsMetersAndKilometres()
    {
        Assert.AreEqual("824 m", SurfaceScannerController.FormatDistance(824.2f));
        Assert.AreEqual("1.2 km", SurfaceScannerController.FormatDistance(1240f));
    }

    [Test]
    public void ScannerClampsOffscreenArrowToSafeEdge()
    {
        Rect safe = new Rect(0f, 0f, 1920f, 1080f);
        Vector2 right = SurfaceScannerController.ClampDirectionToSafeRect(
            safe.center,
            Vector2.right,
            safe,
            82f);
        Vector2 diagonal = SurfaceScannerController.ClampDirectionToSafeRect(
            safe.center,
            new Vector2(1f, 1f),
            safe,
            82f);

        Assert.That(right.x, Is.EqualTo(1838f).Within(0.001f));
        Assert.That(right.y, Is.EqualTo(540f).Within(0.001f));
        Assert.That(diagonal.x, Is.LessThanOrEqualTo(1838f));
        Assert.That(diagonal.y, Is.EqualTo(998f).Within(0.001f));
    }

    [Test]
    public void ScannerPreservesDirectionForTargetsBehindCamera()
    {
        Vector2 behindRightUp =
            SurfaceScannerController.CalculateBehindCameraDirection(
                new Vector3(4f, 2f, -10f),
                16f / 9f,
                60f,
                Vector2.left);
        Vector2 behindLeftDown =
            SurfaceScannerController.CalculateBehindCameraDirection(
                new Vector3(-4f, -2f, -10f),
                16f / 9f,
                60f,
                Vector2.right);
        Vector2 directlyBehind =
            SurfaceScannerController.CalculateBehindCameraDirection(
                new Vector3(0f, 0f, -10f),
                16f / 9f,
                60f,
                Vector2.left);

        Assert.Greater(behindRightUp.x, 0f);
        Assert.Greater(behindRightUp.y, 0f);
        Assert.Less(behindLeftDown.x, 0f);
        Assert.Less(behindLeftDown.y, 0f);
        Assert.AreEqual(Vector2.left, directlyBehind);
    }
}
