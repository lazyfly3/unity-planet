using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

public sealed class SpaceflightUnifiedHudTests
{
    [Test]
    public void Build_CreatesThirdPersonWingLayout_WhenOptionalTextsAreMissing()
    {
        GameObject canvasObject = new GameObject(
            "SpaceflightCanvas",
            typeof(RectTransform),
            typeof(Canvas));
        GameObject host = new GameObject("HudHost");

        try
        {
            host.transform.SetParent(canvasObject.transform, false);
            SpaceflightUnifiedHudLayout layout =
                host.AddComponent<SpaceflightUnifiedHudLayout>();

            layout.Build(
                canvasObject.GetComponent<Canvas>(),
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null);

            Transform root = canvasObject.transform.Find("UnifiedFlightHud");
            Assert.That(root, Is.Not.Null);
            Assert.That(root.Find("LeftFlightWing"), Is.Not.Null);
            Assert.That(root.Find("RightWeaponWing"), Is.Not.Null);
            Assert.That(root.Find("ThirdPersonAimReticle"), Is.Not.Null);
            Assert.That(layout.HullBar, Is.Not.Null);
            Assert.That(layout.AmmunitionBar, Is.Not.Null);
            Assert.That(layout.TargetStrip, Is.Not.Null);
        }
        finally
        {
            Object.DestroyImmediate(host);
            Object.DestroyImmediate(canvasObject);
        }
    }

    [Test]
    public void Build_IsIdempotent_AndDoesNotDuplicateUnifiedRoot()
    {
        GameObject canvasObject = new GameObject(
            "SpaceflightCanvas",
            typeof(RectTransform),
            typeof(Canvas));
        GameObject host = new GameObject("HudHost");

        try
        {
            host.transform.SetParent(canvasObject.transform, false);
            SpaceflightUnifiedHudLayout layout =
                host.AddComponent<SpaceflightUnifiedHudLayout>();

            BuildMinimal(layout, canvasObject.GetComponent<Canvas>());
            BuildMinimal(layout, canvasObject.GetComponent<Canvas>());

            int rootCount = 0;
            foreach (Transform child in canvasObject.transform)
                if (child.name == "UnifiedFlightHud")
                    rootCount++;
            Assert.That(rootCount, Is.EqualTo(1));
        }
        finally
        {
            Object.DestroyImmediate(host);
            Object.DestroyImmediate(canvasObject);
        }
    }

    [Test]
    public void SegmentedBar_ClampsTelemetryRatio()
    {
        GameObject barObject = new GameObject(
            "Bar",
            typeof(RectTransform),
            typeof(SpaceflightSegmentedBarGraphic));
        try
        {
            SpaceflightSegmentedBarGraphic bar =
                barObject.GetComponent<SpaceflightSegmentedBarGraphic>();
            bar.SetValue(1.4f, Color.cyan);
            Assert.That(bar.Value, Is.EqualTo(1f));
            bar.SetValue(-0.3f, Color.red);
            Assert.That(bar.Value, Is.EqualTo(0f));
        }
        finally
        {
            Object.DestroyImmediate(barObject);
        }
    }

    static void BuildMinimal(SpaceflightUnifiedHudLayout layout, Canvas canvas)
    {
        layout.Build(
            canvas,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null);
    }
}
