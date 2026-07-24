using NUnit.Framework;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

public sealed class SpaceflightCockpitTests
{
    [Test]
    public void CockpitLayer_IsReservedForViewModel()
    {
        Assert.That(LayerMask.NameToLayer("CockpitView"), Is.EqualTo(9));
    }

    [Test]
    public void UniversalCockpitPrefab_ContainsIndependentInstrumentAndControlNodes()
    {
        GameObject prefab = Resources.Load<GameObject>("Spaceflight/UniversalCockpit");
        Assert.That(prefab, Is.Not.Null);
        Assert.That(FindDeep(prefab.transform, "LeftMFD_Anchor"), Is.Not.Null);
        Assert.That(FindDeep(prefab.transform, "CenterRadar_Anchor"), Is.Not.Null);
        Assert.That(FindDeep(prefab.transform, "RightMFD_Anchor"), Is.Not.Null);
        Assert.That(FindDeep(prefab.transform, "StickPivot"), Is.Not.Null);
        Assert.That(FindDeep(prefab.transform, "ThrottlePivot"), Is.Not.Null);
        Assert.That(prefab.GetComponentsInChildren<Collider>(true), Is.Empty);
    }

    [Test]
    public void UniversalCockpitPrefab_MfdAnchorsFaceTheEyePoint()
    {
        GameObject prefab = Resources.Load<GameObject>("Spaceflight/UniversalCockpit");
        Assert.That(prefab, Is.Not.Null);
        string[] anchorNames =
        {
            "LeftMFD_Anchor",
            "CenterRadar_Anchor",
            "RightMFD_Anchor"
        };
        foreach (string anchorName in anchorNames)
        {
            Transform anchor = FindDeep(prefab.transform, anchorName);
            Assert.That(anchor, Is.Not.Null, anchorName);
            Vector3 eyeDirection = (prefab.transform.position - anchor.position).normalized;
            float facing = Mathf.Abs(Vector3.Dot(anchor.forward, eyeDirection));
            Assert.That(
                facing,
                Is.GreaterThan(0.72f),
                $"{anchorName} is still too oblique to the cockpit eye point.");
        }
    }

    [Test]
    public void UniversalCockpitPrefab_HasNoFirstPersonCanopyFrame()
    {
        GameObject prefab = Resources.Load<GameObject>("Spaceflight/UniversalCockpit");
        Assert.That(prefab, Is.Not.Null);
        Assert.That(FindDeep(prefab.transform, "CanopyFrame_Left"), Is.Null);
        Assert.That(FindDeep(prefab.transform, "CanopyFrame_Right"), Is.Null);
        Assert.That(FindDeep(prefab.transform, "CanopyFrame_Top"), Is.Null);
        Assert.That(FindDeep(prefab.transform, "CanopyFrame_LeftRail"), Is.Null);
        Assert.That(FindDeep(prefab.transform, "CanopyFrame_RightRail"), Is.Null);
    }

    [Test]
    public void CockpitCamera_HardLocksToShipTranslation()
    {
        GameObject targetObject = new GameObject("ShipTarget");
        GameObject rigObject = new GameObject("CameraRig");
        try
        {
            InterstellarCameraRig rig = rigObject.AddComponent<InterstellarCameraRig>();
            SetField(rig, "target", targetObject.transform);
            SetField(rig, "effectiveMode", SpaceflightCameraMode.Cockpit);
            SetField(rig, "cockpitBlend", 1f);
            SetField(rig, "cockpitAnchorLocal", new Vector3(0f, 0.7f, 1.1f));
            SetField(rig, "localPoseTransitionActive", false);

            Invoke(rig, "LateUpdate");
            targetObject.transform.position = new Vector3(0f, 0f, 26000f);
            Invoke(rig, "LateUpdate");

            Vector3 expected = targetObject.transform.TransformPoint(
                new Vector3(0f, 0.7f, 1.1f));
            Assert.That(Vector3.Distance(rig.transform.position, expected), Is.LessThan(0.001f));
        }
        finally
        {
            Object.DestroyImmediate(rigObject);
            Object.DestroyImmediate(targetObject);
        }
    }

    [Test]
    public void CockpitCamera_DefaultFovAndRecoilLimitsAreSafe()
    {
        GameObject rigObject = new GameObject("CameraRig");
        try
        {
            InterstellarCameraRig rig = rigObject.AddComponent<InterstellarCameraRig>();
            Assert.That(GetField<float>(rig, "cockpitFieldOfView"), Is.EqualTo(66f));
            Assert.That(GetField<float>(rig, "cockpitSpeedFovAddition"), Is.EqualTo(2f));
            Assert.That(
                GetField<Vector3>(rig, "cockpitRecoilLimits"),
                Is.EqualTo(new Vector3(0.03f, 0.03f, 0.08f)));
        }
        finally
        {
            Object.DestroyImmediate(rigObject);
        }
    }

    [Test]
    public void CockpitPresentation_HidesOnlySideWings()
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

            layout.SetPresentationMode(SpaceflightHudPresentationMode.Cockpit);
            Assert.That(layout.LeftWing.gameObject.activeSelf, Is.False);
            Assert.That(layout.RightWing.gameObject.activeSelf, Is.False);
            Assert.That(layout.AimReticle.gameObject.activeSelf, Is.True);
            Assert.That(layout.TargetStrip.gameObject.activeSelf, Is.True);

            layout.SetPresentationMode(SpaceflightHudPresentationMode.ThirdPerson);
            Assert.That(layout.LeftWing.gameObject.activeSelf, Is.True);
            Assert.That(layout.RightWing.gameObject.activeSelf, Is.True);
        }
        finally
        {
            Object.DestroyImmediate(host);
            Object.DestroyImmediate(canvasObject);
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

    static Transform FindDeep(Transform root, string targetName)
    {
        if (root.name == targetName)
            return root;
        for (int index = 0; index < root.childCount; index++)
        {
            Transform result = FindDeep(root.GetChild(index), targetName);
            if (result != null)
                return result;
        }
        return null;
    }

    static void SetField<T>(object target, string name, T value)
    {
        FieldInfo field = target.GetType().GetField(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, name);
        field.SetValue(target, value);
    }

    static T GetField<T>(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, name);
        return (T)field.GetValue(target);
    }

    static void Invoke(object target, string name)
    {
        MethodInfo method = target.GetType().GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, name);
        method.Invoke(target, null);
    }
}
