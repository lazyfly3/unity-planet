using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

public sealed class PlanetSurfaceEntryCoordinatorTests
{
    [Test]
    public void LoadingUiProgressNeverMovesBackward()
    {
        GameObject root = new GameObject(
            "LoadingUiTest",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasGroup));
        GameObject fillObject = new GameObject(
            "Fill",
            typeof(RectTransform),
            typeof(Image));
        GameObject progressObject = new GameObject(
            "Progress",
            typeof(RectTransform),
            typeof(Text));
        GameObject statusObject = new GameObject(
            "Status",
            typeof(RectTransform),
            typeof(Text));
        GameObject remainingObject = new GameObject(
            "Remaining",
            typeof(RectTransform),
            typeof(Text));

        try
        {
            fillObject.transform.SetParent(root.transform, false);
            progressObject.transform.SetParent(root.transform, false);
            statusObject.transform.SetParent(root.transform, false);
            remainingObject.transform.SetParent(root.transform, false);
            PlanetLoadingUI loadingUI = root.AddComponent<PlanetLoadingUI>();
            Image fill = fillObject.GetComponent<Image>();
            SetPrivateField(loadingUI, "canvasGroup", root.GetComponent<CanvasGroup>());
            SetPrivateField(loadingUI, "progressFill", fill);
            SetPrivateField(loadingUI, "progressText", progressObject.GetComponent<Text>());
            SetPrivateField(loadingUI, "statusText", statusObject.GetComponent<Text>());
            SetPrivateField(loadingUI, "remainingText", remainingObject.GetComponent<Text>());

            loadingUI.Show("开始");
            loadingUI.SetProgress(0.72f, "水体完成");
            loadingUI.SetProgress(0.45f, "旧地形回调");

            Assert.IsTrue(loadingUI.IsVisible);
            Assert.That(fill.fillAmount, Is.EqualTo(0.72f).Within(0.0001f));
            Assert.AreEqual("72%", progressObject.GetComponent<Text>().text);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void RestorerDoesNotReportReadyBeforeInitializationCompletes()
    {
        GameObject root = new GameObject("RestorerTest");
        try
        {
            SurfaceLandedSpacecraftRestorer restorer =
                root.AddComponent<SurfaceLandedSpacecraftRestorer>();

            Assert.IsFalse(restorer.IsPlatformReady);
            Assert.IsFalse(restorer.IsSpacecraftReady);
            Assert.IsFalse(restorer.IsPlayerReady);
            Assert.IsFalse(restorer.IsRestoreComplete);
            Assert.IsFalse(restorer.HasFailed);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void SurfacePhysicsReadinessIsPubliclyObservable()
    {
        GameObject root = new GameObject(
            "SurfacePlayerTest",
            typeof(Rigidbody),
            typeof(CapsuleCollider));
        try
        {
            VoxelPlanetPlayerController player =
                root.AddComponent<VoxelPlanetPlayerController>();

            player.SetSurfacePhysicsReady(false);
            Assert.IsFalse(player.IsSurfacePhysicsReady);
            Assert.IsTrue(root.GetComponent<Rigidbody>().isKinematic);

            player.SetSurfacePhysicsReady(true);
            Assert.IsTrue(player.IsSurfacePhysicsReady);
            Assert.IsFalse(root.GetComponent<Rigidbody>().isKinematic);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    static void SetPrivateField<T>(object target, string fieldName, T value)
    {
        FieldInfo field = target.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, $"Missing field {fieldName}");
        field.SetValue(target, value);
    }
}
