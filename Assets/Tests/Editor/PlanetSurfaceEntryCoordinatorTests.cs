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
    public void LoadingUiCoordinatorHandoffKeepsExistingProgress()
    {
        GameObject root = new GameObject(
            "LoadingUiHandoffTest",
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

            loadingUI.Show("城市规划开始");
            loadingUI.SetProgress(0.72f, "城市主体完成");
            loadingUI.ShowOrContinue(0.30f, "协调器接管");

            Assert.IsTrue(loadingUI.IsVisible);
            Assert.That(fill.fillAmount, Is.EqualTo(0.72f).Within(0.0001f));
            Assert.AreEqual("72%", progressObject.GetComponent<Text>().text);
            Assert.AreEqual("协调器接管", statusObject.GetComponent<Text>().text);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void LoadingUiSimulatedProgressAdvancesSmoothlyAndMonotonically()
    {
        using (LoadingUiTestRig rig = new LoadingUiTestRig())
        {
            rig.LoadingUI.Show("开始规划城市");
            InvokeInstanceMethod(
                rig.LoadingUI,
                "BeginSimulatedProgress",
                0.03f,
                0.68f,
                "正在规划城市");
            float initial = rig.Fill.fillAmount;

            InvokeInstanceMethod(
                rig.LoadingUI,
                "AdvanceSimulatedProgress",
                0.25f);
            float first = rig.Fill.fillAmount;
            InvokeInstanceMethod(
                rig.LoadingUI,
                "AdvanceSimulatedProgress",
                0.25f);
            float second = rig.Fill.fillAmount;

            Assert.That(first, Is.GreaterThan(initial));
            Assert.That(second, Is.GreaterThan(first));
            Assert.That(second, Is.LessThan(0.68f));
        }
    }

    [Test]
    public void LoadingUiSimulatedProgressNeverCrossesItsCeiling()
    {
        using (LoadingUiTestRig rig = new LoadingUiTestRig())
        {
            rig.LoadingUI.Show("开始规划城市");
            InvokeInstanceMethod(
                rig.LoadingUI,
                "BeginSimulatedProgress",
                0.03f,
                0.68f,
                "正在规划城市");

            for (int step = 0; step < 1000; step++)
            {
                InvokeInstanceMethod(
                    rig.LoadingUI,
                    "AdvanceSimulatedProgress",
                    1f);
                Assert.That(
                    rig.Fill.fillAmount,
                    Is.LessThanOrEqualTo(0.68f + 0.000001f));
            }
        }
    }

    [Test]
    public void LoadingUiInvalidSimulatedRangeStopsExistingSimulation()
    {
        using (LoadingUiTestRig rig = new LoadingUiTestRig())
        {
            rig.LoadingUI.Show("开始规划城市");
            rig.LoadingUI.BeginSimulatedProgress(
                0.03f,
                0.68f,
                "正在规划城市");
            rig.LoadingUI.AdvanceSimulatedProgress(2f);
            float progressBeforeInvalidRange = rig.Fill.fillAmount;

            rig.LoadingUI.BeginSimulatedProgress(
                0.68f,
                0.68f,
                "无效模拟区间");
            rig.LoadingUI.AdvanceSimulatedProgress(30f);

            Assert.That(
                rig.Fill.fillAmount,
                Is.EqualTo(progressBeforeInvalidRange).Within(0.0001f));
            Assert.AreEqual("正在规划城市", rig.Status.text);
        }
    }

    [Test]
    public void LoadingUiRealProgressTakesOverFromSimulatedProgress()
    {
        using (LoadingUiTestRig rig = new LoadingUiTestRig())
        {
            rig.LoadingUI.Show("开始规划城市");
            InvokeInstanceMethod(
                rig.LoadingUI,
                "BeginSimulatedProgress",
                0.03f,
                0.68f,
                "正在规划城市");
            InvokeInstanceMethod(
                rig.LoadingUI,
                "AdvanceSimulatedProgress",
                2f);

            rig.LoadingUI.SetProgress(0.70f, "真实城市规划完成");
            InvokeInstanceMethod(
                rig.LoadingUI,
                "AdvanceSimulatedProgress",
                30f);

            Assert.That(
                rig.Fill.fillAmount,
                Is.EqualTo(0.70f).Within(0.0001f));
            Assert.AreEqual("70%", rig.Progress.text);
            Assert.AreEqual("真实城市规划完成", rig.Status.text);
        }
    }

    [Test]
    public void LoadingUiFailureStopsSimulatedProgress()
    {
        using (LoadingUiTestRig rig = new LoadingUiTestRig())
        {
            rig.LoadingUI.Show("开始规划城市");
            InvokeInstanceMethod(
                rig.LoadingUI,
                "BeginSimulatedProgress",
                0.03f,
                0.68f,
                "正在规划城市");
            InvokeInstanceMethod(
                rig.LoadingUI,
                "AdvanceSimulatedProgress",
                2f);
            rig.LoadingUI.ShowFailure("城市规划失败");
            float progressAtFailure = rig.Fill.fillAmount;

            InvokeInstanceMethod(
                rig.LoadingUI,
                "AdvanceSimulatedProgress",
                30f);

            Assert.That(
                rig.Fill.fillAmount,
                Is.EqualTo(progressAtFailure).Within(0.0001f));
            Assert.AreEqual("城市规划失败", rig.Status.text);
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

    static void InvokeInstanceMethod(
        object target,
        string methodName,
        params object[] arguments)
    {
        MethodInfo method = target.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public |
            BindingFlags.NonPublic);
        Assert.IsNotNull(method, $"Missing method {methodName}");
        method.Invoke(target, arguments);
    }

    sealed class LoadingUiTestRig : System.IDisposable
    {
        public readonly GameObject Root;
        public readonly PlanetLoadingUI LoadingUI;
        public readonly Image Fill;
        public readonly Text Progress;
        public readonly Text Status;

        readonly GameObject fillObject;
        readonly GameObject progressObject;
        readonly GameObject statusObject;
        readonly GameObject remainingObject;

        public LoadingUiTestRig()
        {
            Root = new GameObject(
                "SimulatedLoadingUiTest",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasGroup));
            fillObject = CreateChild("Fill", typeof(Image));
            progressObject = CreateChild("Progress", typeof(Text));
            statusObject = CreateChild("Status", typeof(Text));
            remainingObject = CreateChild("Remaining", typeof(Text));

            LoadingUI = Root.AddComponent<PlanetLoadingUI>();
            Fill = fillObject.GetComponent<Image>();
            Progress = progressObject.GetComponent<Text>();
            Status = statusObject.GetComponent<Text>();
            SetPrivateField(
                LoadingUI,
                "canvasGroup",
                Root.GetComponent<CanvasGroup>());
            SetPrivateField(LoadingUI, "progressFill", Fill);
            SetPrivateField(LoadingUI, "progressText", Progress);
            SetPrivateField(LoadingUI, "statusText", Status);
            SetPrivateField(
                LoadingUI,
                "remainingText",
                remainingObject.GetComponent<Text>());
        }

        GameObject CreateChild(string name, System.Type componentType)
        {
            GameObject child = new GameObject(
                name,
                typeof(RectTransform),
                componentType);
            child.transform.SetParent(Root.transform, false);
            return child;
        }

        public void Dispose()
        {
            if (Root != null)
                Object.DestroyImmediate(Root);
        }
    }
}
