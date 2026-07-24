using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class SpaceflightUnifiedHudLayout : MonoBehaviour
{
    static readonly Color Cyan = new Color(0.08f, 0.86f, 1f, 1f);
    static readonly Color Pale = new Color(0.79f, 0.96f, 1f, 1f);
    static readonly Color Muted = new Color(0.42f, 0.75f, 0.84f, 1f);
    static readonly Color Panel = new Color(0.008f, 0.055f, 0.09f, 0.91f);
    static readonly Color Amber = new Color(1f, 0.58f, 0.12f, 0.98f);

    public SpaceflightSegmentedBarGraphic HullBar { get; private set; }
    public SpaceflightSegmentedBarGraphic BoostBar { get; private set; }
    public SpaceflightSegmentedBarGraphic AmmunitionBar { get; private set; }
    public SpaceflightSegmentedBarGraphic CapacitorBar { get; private set; }
    public SpaceflightSegmentedBarGraphic HeatBar { get; private set; }
    public Text HullValueText { get; private set; }
    public Text WeaponGroupOneTab { get; private set; }
    public Text WeaponGroupTwoTab { get; private set; }
    public RectTransform TargetStrip { get; private set; }
    public RectTransform WarningStack { get; private set; }
    public SpaceflightAimReticleGraphic AimReticle { get; private set; }
    public RectTransform LeftWing { get; private set; }
    public RectTransform RightWing { get; private set; }
    SpaceflightHudPresentationMode presentationMode;

    public void Build(
        Canvas canvas,
        Font font,
        Image legacyIntegrityFrame,
        Image legacyIntegrityFill,
        Text integrityText,
        Text speedText,
        Text flightModeText,
        Text authorityText,
        Text speedLimitText,
        Text boostText,
        Text weaponGroupText,
        Text weaponAmmoText,
        Text weaponCapacitorText,
        Text weaponHeatText,
        Text weaponMountText,
        Text weaponLockText,
        Text targetText,
        Text cruiseText,
        Text promptText)
    {
        if (canvas == null)
            return;
        Transform canvasTransform = canvas.transform;
        Transform existing = canvasTransform.Find("UnifiedFlightHud");
        MoveOutside(existing, canvasTransform, integrityText, speedText, flightModeText, authorityText,
            speedLimitText, boostText, weaponGroupText, weaponAmmoText, weaponCapacitorText,
            weaponHeatText, weaponMountText, weaponLockText, targetText, cruiseText, promptText);
        DestroySafe(existing == null ? null : existing.gameObject);

        RectTransform root = CreateRect("UnifiedFlightHud", canvasTransform);
        Stretch(root);
        root.SetAsLastSibling();

        BuildLeftWing(
            root,
            font,
            legacyIntegrityFrame,
            integrityText,
            speedText,
            flightModeText,
            boostText);
        BuildRightWing(
            root,
            font,
            weaponGroupText,
            weaponAmmoText,
            weaponCapacitorText,
            weaponHeatText,
            weaponMountText,
            weaponLockText);
        BuildCenterLayer(root, font, targetText, cruiseText, promptText);
        BuildWarningStack(root, authorityText, speedLimitText);
        SetPresentationMode(presentationMode);

        if (legacyIntegrityFrame != null)
            legacyIntegrityFrame.gameObject.SetActive(false);
        if (legacyIntegrityFill != null)
            legacyIntegrityFill.gameObject.SetActive(false);
        Text oldCrosshair = FindText(canvasTransform, "WeaponCrosshair");
        if (oldCrosshair != null)
            oldCrosshair.gameObject.SetActive(false);
    }

    void BuildLeftWing(
        RectTransform root,
        Font font,
        Image legacyIntegrityFrame,
        Text integrityText,
        Text speedText,
        Text flightModeText,
        Text boostText)
    {
        RectTransform wing = CreateRect("LeftFlightWing", root);
        LeftWing = wing;
        SetRect(wing, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(600f, 246f), new Vector2(30f, 28f));

        RectTransform diagnostic = CreateRect("ShipDiagnostic", wing);
        SetRect(diagnostic, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(220f, 220f), new Vector2(0f, 13f));
        if (legacyIntegrityFrame != null
            && legacyIntegrityFrame.sprite != null
            && legacyIntegrityFrame.sprite.texture != null)
        {
            RawImage image = diagnostic.gameObject.AddComponent<RawImage>();
            image.texture = legacyIntegrityFrame.sprite.texture;
            image.uvRect = new Rect(0f, 0f, 0.305f, 1f);
            image.color = Color.white;
            image.raycastTarget = false;
        }
        else
        {
            SpaceflightHudPanelGraphic fallback = diagnostic.gameObject.AddComponent<SpaceflightHudPanelGraphic>();
            fallback.Configure(Panel, Cyan, Amber, 28f);
        }

        RectTransform body = CreatePanel("FlightStatusPanel", wing, new Vector2(190f, 16f), new Vector2(405f, 220f));
        SetText(integrityText, body, font, 20, TextAnchor.MiddleLeft, Cyan, new Vector2(20f, 174f), new Vector2(125f, 34f));
        HullValueText = CreateText("HullValueText", body, font, "100%", 28, TextAnchor.MiddleRight, Pale);
        SetRect(HullValueText.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(210f, 38f), new Vector2(175f, 172f));
        HullBar = CreateBar("HullBar", body, new Vector2(20f, 148f), new Vector2(365f, 22f), 20);

        CreateIcon("SpeedIcon", body, SpaceflightHudIconKind.Speed, new Vector2(20f, 99f), new Vector2(28f, 28f));
        SetText(speedText, body, font, 18, TextAnchor.MiddleLeft, Cyan, new Vector2(58f, 96f), new Vector2(300f, 32f));
        AddSeparator(body, 88f);

        CreateIcon("BoostIcon", body, SpaceflightHudIconKind.Boost, new Vector2(20f, 56f), new Vector2(28f, 28f));
        SetText(boostText, body, font, 18, TextAnchor.MiddleLeft, Cyan, new Vector2(58f, 54f), new Vector2(165f, 32f));
        BoostBar = CreateBar("BoostBar", body, new Vector2(230f, 61f), new Vector2(145f, 18f), 12);
        AddSeparator(body, 45f);

        CreateIcon("FlightModeIcon", body, SpaceflightHudIconKind.FlightMode, new Vector2(20f, 13f), new Vector2(28f, 28f));
        SetText(flightModeText, body, font, 18, TextAnchor.MiddleLeft, Cyan, new Vector2(58f, 10f), new Vector2(300f, 34f));
    }

    void BuildRightWing(
        RectTransform root,
        Font font,
        Text weaponGroupText,
        Text weaponAmmoText,
        Text weaponCapacitorText,
        Text weaponHeatText,
        Text weaponMountText,
        Text weaponLockText)
    {
        RectTransform wing = CreateRect("RightWeaponWing", root);
        RightWing = wing;
        SetRect(wing, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(560f, 220f), new Vector2(-30f, 28f));
        RectTransform body = CreatePanel("WeaponStatusPanel", wing, Vector2.zero, wing.sizeDelta);

        SetText(weaponGroupText, body, font, 20, TextAnchor.MiddleLeft, Cyan, new Vector2(24f, 174f), new Vector2(125f, 32f));
        WeaponGroupOneTab = CreateText("WeaponGroupOneTab", body, font, "1", 17, TextAnchor.MiddleCenter, Muted);
        SetRect(WeaponGroupOneTab.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(34f, 26f), new Vector2(144f, 178f));
        WeaponGroupTwoTab = CreateText("WeaponGroupTwoTab", body, font, "2", 17, TextAnchor.MiddleCenter, Muted);
        SetRect(WeaponGroupTwoTab.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(34f, 26f), new Vector2(182f, 178f));
        SetText(weaponAmmoText, body, font, 24, TextAnchor.MiddleRight, Pale, new Vector2(330f, 171f), new Vector2(205f, 38f));
        AmmunitionBar = CreateBar("AmmunitionBar", body, new Vector2(24f, 145f), new Vector2(512f, 20f), 20);

        CreateIcon("CapacitorIcon", body, SpaceflightHudIconKind.Capacitor, new Vector2(24f, 101f), new Vector2(28f, 28f));
        SetText(weaponCapacitorText, body, font, 18, TextAnchor.MiddleLeft, Cyan, new Vector2(62f, 98f), new Vector2(155f, 34f));
        CapacitorBar = CreateBar("CapacitorBar", body, new Vector2(230f, 105f), new Vector2(306f, 18f), 16);
        AddSeparator(body, 91f);

        CreateIcon("HeatIcon", body, SpaceflightHudIconKind.Heat, new Vector2(24f, 59f), new Vector2(28f, 28f));
        SetText(weaponHeatText, body, font, 18, TextAnchor.MiddleLeft, Cyan, new Vector2(62f, 56f), new Vector2(155f, 34f));
        HeatBar = CreateBar("HeatBar", body, new Vector2(230f, 63f), new Vector2(306f, 18f), 16);
        AddSeparator(body, 49f);

        CreateIcon("MountIcon", body, SpaceflightHudIconKind.Mount, new Vector2(24f, 12f), new Vector2(28f, 28f));
        SetText(weaponMountText, body, font, 17, TextAnchor.MiddleLeft, Cyan, new Vector2(62f, 9f), new Vector2(190f, 34f));
        CreateIcon("TargetIcon", body, SpaceflightHudIconKind.Target, new Vector2(302f, 12f), new Vector2(28f, 28f));
        SetText(weaponLockText, body, font, 17, TextAnchor.MiddleLeft, Cyan, new Vector2(340f, 9f), new Vector2(196f, 34f));
    }

    void BuildCenterLayer(
        RectTransform root,
        Font font,
        Text targetText,
        Text cruiseText,
        Text promptText)
    {
        TargetStrip = CreatePanel("TargetStrip", root, Vector2.zero, new Vector2(700f, 48f));
        SetRect(TargetStrip, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(700f, 48f), new Vector2(0f, -22f));
        SetText(targetText, TargetStrip, font, 19, TextAnchor.MiddleCenter, Cyan, Vector2.zero, TargetStrip.sizeDelta);

        SetText(cruiseText, root, font, 19, TextAnchor.MiddleCenter, Pale, new Vector2(0f, -80f), new Vector2(760f, 34f));
        if (cruiseText != null)
            SetTopCenter(cruiseText.rectTransform);
        SetText(promptText, root, font, 16, TextAnchor.MiddleCenter, Muted, new Vector2(0f, -112f), new Vector2(980f, 30f));
        if (promptText != null)
            SetTopCenter(promptText.rectTransform);

        RectTransform reticle = CreateRect("ThirdPersonAimReticle", root);
        SetRect(reticle, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(76f, 76f), new Vector2(0f, 26f));
        AimReticle = reticle.gameObject.AddComponent<SpaceflightAimReticleGraphic>();
        AimReticle.color = Cyan;
        reticle.SetAsFirstSibling();
    }

    void BuildWarningStack(RectTransform root, Text authorityText, Text speedLimitText)
    {
        WarningStack = CreateRect("FlightWarningStack", root);
        SetRect(WarningStack, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(405f, 68f), new Vector2(220f, 282f));
        SetText(authorityText, WarningStack, authorityText == null ? null : authorityText.font, 16, TextAnchor.MiddleLeft, Amber,
            new Vector2(0f, 34f), new Vector2(405f, 30f));
        SetText(speedLimitText, WarningStack, speedLimitText == null ? null : speedLimitText.font, 16, TextAnchor.MiddleLeft, Cyan,
            new Vector2(0f, 2f), new Vector2(405f, 30f));
    }

    public void SetPresentationMode(SpaceflightHudPresentationMode mode)
    {
        presentationMode = mode;
        bool cockpit = mode == SpaceflightHudPresentationMode.Cockpit;
        if (LeftWing != null)
            LeftWing.gameObject.SetActive(!cockpit);
        if (RightWing != null)
            RightWing.gameObject.SetActive(!cockpit);
        if (WarningStack != null)
        {
            SetRect(
                WarningStack,
                cockpit ? new Vector2(0.5f, 1f) : new Vector2(0f, 0f),
                cockpit ? new Vector2(0.5f, 1f) : new Vector2(0f, 0f),
                new Vector2(405f, 68f),
                cockpit ? new Vector2(0f, -142f) : new Vector2(220f, 282f));
        }
    }

    RectTransform CreatePanel(string name, Transform parent, Vector2 position, Vector2 size)
    {
        RectTransform rect = CreateRect(name, parent);
        SetRect(rect, new Vector2(0f, 0f), new Vector2(0f, 0f), size, position);
        SpaceflightHudPanelGraphic panel = rect.gameObject.AddComponent<SpaceflightHudPanelGraphic>();
        panel.Configure(Panel, Cyan, Amber);
        return rect;
    }

    SpaceflightSegmentedBarGraphic CreateBar(
        string name,
        Transform parent,
        Vector2 position,
        Vector2 size,
        int segments)
    {
        RectTransform rect = CreateRect(name, parent);
        SetRect(rect, new Vector2(0f, 0f), new Vector2(0f, 0f), size, position);
        SpaceflightSegmentedBarGraphic bar = rect.gameObject.AddComponent<SpaceflightSegmentedBarGraphic>();
        bar.Configure(segments);
        return bar;
    }

    static SpaceflightHudIconGraphic CreateIcon(
        string name,
        Transform parent,
        SpaceflightHudIconKind kind,
        Vector2 position,
        Vector2 size)
    {
        RectTransform rect = CreateRect(name, parent);
        SetRect(rect, new Vector2(0f, 0f), new Vector2(0f, 0f), size, position);
        SpaceflightHudIconGraphic icon = rect.gameObject.AddComponent<SpaceflightHudIconGraphic>();
        icon.color = Cyan;
        icon.Configure(kind);
        return icon;
    }

    static void AddSeparator(Transform parent, float y)
    {
        RectTransform rect = CreateRect("Separator", parent);
        SetRect(rect, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(355f, 1f), new Vector2(25f, y));
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.18f);
        image.raycastTarget = false;
    }

    static void SetText(
        Text text,
        Transform parent,
        Font fallbackFont,
        int fontSize,
        TextAnchor alignment,
        Color tint,
        Vector2 position,
        Vector2 size)
    {
        if (text == null)
            return;
        text.transform.SetParent(parent, false);
        if (text.font == null)
            text.font = fallbackFont;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = tint;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        SetRect(text.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), size, position);
        Outline outline = text.GetComponent<Outline>();
        if (outline == null)
            outline = text.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0.02f, 0.04f, 0.8f);
        outline.effectDistance = new Vector2(1f, -1f);
    }

    static Text CreateText(
        string name,
        Transform parent,
        Font font,
        string value,
        int fontSize,
        TextAnchor alignment,
        Color tint)
    {
        RectTransform rect = CreateRect(name, parent);
        Text text = rect.gameObject.AddComponent<Text>();
        text.font = font;
        text.text = value;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = tint;
        text.raycastTarget = false;
        return text;
    }

    static void SetTopCenter(RectTransform rect)
    {
        Vector2 position = rect.anchoredPosition;
        Vector2 size = rect.sizeDelta;
        SetRect(rect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), size, position);
    }

    static Text FindText(Transform root, string name)
    {
        Text[] texts = root.GetComponentsInChildren<Text>(true);
        foreach (Text text in texts)
            if (text != null && text.name == name)
                return text;
        return null;
    }

    static void MoveOutside(Transform existing, Transform destination, params Text[] texts)
    {
        if (existing == null)
            return;
        foreach (Text text in texts)
        {
            if (text != null && text.transform.IsChildOf(existing))
                text.transform.SetParent(destination, false);
        }
    }

    static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject child = new GameObject(name, typeof(RectTransform));
        RectTransform rect = child.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    static void SetRect(
        RectTransform rect,
        Vector2 pivot,
        Vector2 anchor,
        Vector2 size,
        Vector2 position)
    {
        rect.pivot = pivot;
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
    }

    static void DestroySafe(GameObject target)
    {
        if (target == null)
            return;
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            Object.DestroyImmediate(target);
            return;
        }
#endif
        Object.Destroy(target);
    }
}
