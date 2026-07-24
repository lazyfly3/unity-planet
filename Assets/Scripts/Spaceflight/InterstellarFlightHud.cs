using SpacecraftEditor;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class InterstellarFlightHud : MonoBehaviour
{
    [SerializeField] InterstellarShipController ship;
    [SerializeField] InterstellarNavigationSystem navigation;
    [SerializeField] SpacecraftDamageReceiver damageReceiver;
    [SerializeField] PirateEncounterDirector pirateEncounterDirector;
    [SerializeField] Camera worldCamera;
    [SerializeField] Text targetText;
    [SerializeField] Text speedText;
    [SerializeField] Text integrityText;
    [SerializeField] Text cruiseText;
    [SerializeField] Text promptText;
    [SerializeField] Text flightModeText;
    [SerializeField] Text authorityText;
    [SerializeField] Text speedLimitText;
    [SerializeField] Text boostText;
    [SerializeField] Text weaponGroupText;
    [SerializeField] Text weaponAmmoText;
    [SerializeField] Text weaponCapacitorText;
    [SerializeField] Text weaponHeatText;
    [SerializeField] Text weaponMountText;
    [SerializeField] Text weaponLockText;
    [SerializeField] RectTransform weaponTargetMarker;
    [SerializeField] RectTransform targetArrow;
    [SerializeField] RectTransform planetLightLayer;
    [SerializeField] RectTransform planetLightTemplate;
    [SerializeField] InterstellarWarpStreakGraphic warpOverlay;
    [SerializeField] RectTransform[] enemyThreatArrows = new RectTransform[3];
    [SerializeField, Min(0f)] float attackThreatHoldSeconds = 2.5f;
    [SerializeField, Min(0f)] float hitThreatHoldSeconds = 4f;
    [SerializeField] Image integrityFill;
    [SerializeField] Image integrityFrame;

    const int MaximumThreatArrows = 3;
    const int MaximumPlanetLights = 16;
    static readonly Color HudCyan = new Color(0.08f, 0.86f, 1f, 1f);
    static readonly Color HudMuted = new Color(0.42f, 0.75f, 0.84f, 1f);
    static readonly Color HudAmber = new Color(1f, 0.58f, 0.12f, 1f);
    static readonly Color HudDanger = new Color(1f, 0.18f, 0.08f, 1f);
    float damageFlashUntil;
    SpaceTargetLockGraphic weaponTargetGraphic;
    SpaceflightUnifiedHudLayout unifiedLayout;
    readonly PirateShipAiController[] threatQueryBuffer = new PirateShipAiController[MaximumThreatArrows];
    readonly ThreatSlot[] threatSlots = new ThreatSlot[MaximumThreatArrows];
    readonly Text[] threatArrowTexts = new Text[MaximumThreatArrows];
    readonly InterstellarPlanetTargetSnapshot[] planetSnapshots =
        new InterstellarPlanetTargetSnapshot[MaximumPlanetLights];
    readonly RectTransform[] planetLights = new RectTransform[MaximumPlanetLights];
    readonly Text[] planetLightDots = new Text[MaximumPlanetLights];
    readonly Text[] planetLightRings = new Text[MaximumPlanetLights];
    readonly Text[] planetLightLabels = new Text[MaximumPlanetLights];

    struct ThreatSlot
    {
        public PirateShipAiController source;
        public float expiresAt;
        public float hitPulseUntil;
    }

    void Awake()
    {
        if (ship == null)
            ship = FindObjectOfType<InterstellarShipController>();
        if (navigation == null)
            navigation = FindObjectOfType<InterstellarNavigationSystem>();
        if (damageReceiver == null && ship != null)
            damageReceiver = ship.GetComponent<SpacecraftDamageReceiver>();
        if (pirateEncounterDirector == null)
            pirateEncounterDirector = FindObjectOfType<PirateEncounterDirector>();
        if (worldCamera == null)
            worldCamera = Camera.main;
        targetText = ResolveText(targetText, "TargetText");
        speedText = ResolveText(speedText, "SpeedText");
        integrityText = ResolveText(integrityText, "IntegrityText");
        cruiseText = ResolveText(cruiseText, "CruiseText");
        promptText = ResolveText(promptText, "PromptText");
        flightModeText = ResolveText(flightModeText, "FlightModeText");
        authorityText = ResolveText(authorityText, "AuthorityText");
        speedLimitText = ResolveText(speedLimitText, "SpeedLimitText");
        boostText = ResolveText(boostText, "BoostText");
        weaponGroupText = ResolveText(weaponGroupText, "WeaponGroupText");
        weaponAmmoText = ResolveText(weaponAmmoText, "WeaponAmmoText");
        weaponCapacitorText = ResolveText(weaponCapacitorText, "WeaponCapacitorText");
        weaponHeatText = ResolveText(weaponHeatText, "WeaponHeatText");
        weaponMountText = ResolveText(weaponMountText, "WeaponMountText");
        weaponLockText = ResolveText(weaponLockText, "WeaponLockText");
        weaponTargetMarker = ResolveRect(weaponTargetMarker, "WeaponTargetMarker");
        ConfigureWeaponTargetMarker();
        targetArrow = ResolveRect(targetArrow, "TargetArrow");
        planetLightLayer = ResolveRect(planetLightLayer, "PlanetLightLayer");
        planetLightTemplate = ResolveRect(planetLightTemplate, "PlanetLightTemplate");
        if (planetLightTemplate == null && planetLightLayer != null)
            planetLightTemplate = planetLightLayer.Find("PlanetLightTemplate") as RectTransform;
        if (warpOverlay == null)
            warpOverlay = GameObject.Find("WarpOverlay")?.GetComponent<InterstellarWarpStreakGraphic>();
        BuildPlanetLightPool();
        ResolveThreatArrows();
        if (integrityFill == null)
            integrityFill = GameObject.Find("IntegrityFill")?.GetComponent<Image>();
        if (integrityFrame == null)
            integrityFrame = GameObject.Find("IntegrityFrame")?.GetComponent<Image>();
        RebuildUnifiedLayout();
    }

    public void RebuildUnifiedLayout()
    {
        Canvas canvas = GetComponent<Canvas>();
        if (canvas == null)
            canvas = GetComponentInParent<Canvas>(true);
        if (canvas == null)
            return;
        unifiedLayout = GetComponent<SpaceflightUnifiedHudLayout>();
        if (unifiedLayout == null)
            unifiedLayout = gameObject.AddComponent<SpaceflightUnifiedHudLayout>();
        Font font = integrityText != null
            ? integrityText.font
            : weaponGroupText != null
                ? weaponGroupText.font
                : null;
        unifiedLayout.Build(
            canvas,
            font,
            integrityFrame,
            integrityFill,
            integrityText,
            speedText,
            flightModeText,
            authorityText,
            speedLimitText,
            boostText,
            weaponGroupText,
            weaponAmmoText,
            weaponCapacitorText,
            weaponHeatText,
            weaponMountText,
            weaponLockText,
            targetText,
            cruiseText,
            promptText);
        if (weaponTargetMarker != null)
            weaponTargetMarker.SetAsLastSibling();
        if (enemyThreatArrows != null)
        {
            foreach (RectTransform arrow in enemyThreatArrows)
                if (arrow != null)
                    arrow.SetAsLastSibling();
            if (enemyThreatArrows.Length > 0
                && enemyThreatArrows[0] != null
                && enemyThreatArrows[0].parent != null)
            {
                enemyThreatArrows[0].parent.SetAsLastSibling();
            }
        }
    }

    void OnEnable()
    {
        if (damageReceiver != null)
            damageReceiver.Damaged += HandleDamaged;
    }

    void OnDisable()
    {
        if (damageReceiver != null)
            damageReceiver.Damaged -= HandleDamaged;
        for (int index = 0; index < MaximumThreatArrows; index++)
        {
            if (enemyThreatArrows != null && index < enemyThreatArrows.Length && enemyThreatArrows[index] != null)
                enemyThreatArrows[index].gameObject.SetActive(false);
        }
    }

    void Update()
    {
        SpacecraftControlTelemetry telemetry = ship == null ? default : ship.Telemetry;
        if (targetText != null)
            targetText.text = navigation != null && navigation.HasLockedTarget
                ? $"{navigation.TargetName}　　{FormatDistance(navigation.TargetDistance)}"
                : string.Empty;
        if (unifiedLayout != null && unifiedLayout.TargetStrip != null)
            unifiedLayout.TargetStrip.gameObject.SetActive(
                navigation != null && navigation.HasLockedTarget);
        if (speedText != null)
            speedText.text = $"速度  {(ship == null ? 0f : ship.Speed):0} m/s";
        if (flightModeText != null)
            flightModeText.text = telemetry.assistMode == SpacecraftAssistMode.Decoupled
                ? "惯性模式"
                : "辅助模式";
        if (authorityText != null)
        {
            authorityText.text = $"控制权威  {telemetry.controlAuthority * 100f:0}%";
            authorityText.gameObject.SetActive(telemetry.controlAuthority < 0.98f);
        }
        if (speedLimitText != null)
        {
            speedLimitText.text = $"速度限制  {telemetry.speedLimit:0} m/s";
            float speedRatio = telemetry.speedLimit <= 0f
                ? 0f
                : (ship == null ? 0f : ship.Speed) / telemetry.speedLimit;
            speedLimitText.gameObject.SetActive(speedRatio >= 0.85f);
        }
        if (boostText != null)
            boostText.text = $"BOOST  {telemetry.boostRatio * 100f:0}%";
        if (unifiedLayout != null && unifiedLayout.BoostBar != null)
            unifiedLayout.BoostBar.SetValue(telemetry.boostRatio, HudCyan);
        UpdateIntegrity();
        UpdateWeapons();
        UpdateCruiseText();
        UpdatePlanetLights();
        UpdateArrow();
        UpdateThreatArrows();
        if (warpOverlay != null)
            warpOverlay.SetIntensity(ship == null ? 0f : ship.WarpVisualIntensity);
    }

    void UpdateWeapons()
    {
        SpacecraftWeaponSystem weapons = ship == null ? null : ship.WeaponSystem;
        if (weaponGroupText != null)
            weaponGroupText.text = "武器组";
        if (weaponAmmoText != null)
            weaponAmmoText.text = weapons == null
                ? "弹药 --"
                : weapons.SelectedAmmunitionCapacity <= 0
                    ? "弹药 ∞"
                    : $"弹药 {weapons.SelectedAmmunition}";
        if (weaponCapacitorText != null)
            weaponCapacitorText.text = weapons == null ? "电容 --" : $"电容 {weapons.CapacitorRatio * 100f:0}%";
        if (weaponHeatText != null)
            weaponHeatText.text = weapons == null ? "热量 --" : $"热量 {weapons.SelectedHeat * 100f:0}%";
        if (weaponMountText != null)
            weaponMountText.text = weapons == null
                ? "挂载离线"
                : LocalizedMountLabel(weapons.SelectedMountLabel);
        UpdateWeaponBars(weapons);
        if (weaponLockText != null)
        {
            SpaceWeaponTargetKind kind = weapons == null
                ? SpaceWeaponTargetKind.None
                : weapons.CurrentTargetKind;
            weaponLockText.text = weapons == null
                ? "武器离线"
                : !weapons.SelectedGroupHasGimbal
                    ? "固定准星射击"
                    : !weapons.TargetLockEnabled
                        ? "Tab 开启云台锁定"
                        : kind == SpaceWeaponTargetKind.Combatant
                            ? "海盗锁定"
                            : kind == SpaceWeaponTargetKind.Asteroid
                                ? "陨石锁定"
                                : "搜索目标";
            weaponLockText.color = TargetColor(kind);
            if (unifiedLayout != null && unifiedLayout.AimReticle != null)
                unifiedLayout.AimReticle.color = TargetColor(kind);
        }
        UpdateWeaponTargetMarker(weapons);
    }

    void UpdateWeaponBars(SpacecraftWeaponSystem weapons)
    {
        if (unifiedLayout == null)
            return;
        float ammunitionRatio = weapons == null
            ? 0f
            : weapons.SelectedAmmunitionCapacity <= 0
                ? 1f
                : Mathf.Clamp01(
                    weapons.SelectedAmmunition
                    / (float)Mathf.Max(1, weapons.SelectedAmmunitionCapacity));
        Color ammunitionColor = weapons != null
            && weapons.SelectedAmmunitionCapacity > 0
            && weapons.SelectedAmmunition == 0
                ? HudDanger
                : ammunitionRatio < 0.2f ? HudAmber : HudCyan;
        unifiedLayout.AmmunitionBar?.SetValue(ammunitionRatio, ammunitionColor);

        float capacitorRatio = weapons == null ? 0f : weapons.CapacitorRatio;
        unifiedLayout.CapacitorBar?.SetValue(
            capacitorRatio,
            capacitorRatio < 0.2f ? HudAmber : HudCyan);
        float heatRatio = weapons == null ? 0f : weapons.SelectedHeat;
        unifiedLayout.HeatBar?.SetValue(
            heatRatio,
            heatRatio >= 0.95f ? HudDanger : heatRatio >= 0.72f ? HudAmber : HudCyan);

        if (unifiedLayout.WeaponGroupOneTab != null)
        {
            unifiedLayout.WeaponGroupOneTab.gameObject.SetActive(
                weapons == null || weapons.GroupOneAvailable);
            unifiedLayout.WeaponGroupOneTab.color = weapons != null && weapons.SelectedGroup == 1
                ? Color.white
                : HudMuted;
            unifiedLayout.WeaponGroupOneTab.fontStyle = weapons != null && weapons.SelectedGroup == 1
                ? FontStyle.Bold
                : FontStyle.Normal;
        }
        if (unifiedLayout.WeaponGroupTwoTab != null)
        {
            unifiedLayout.WeaponGroupTwoTab.gameObject.SetActive(
                weapons != null && weapons.GroupTwoAvailable);
            unifiedLayout.WeaponGroupTwoTab.color = weapons != null && weapons.SelectedGroup == 2
                ? Color.white
                : HudMuted;
            unifiedLayout.WeaponGroupTwoTab.fontStyle = weapons != null && weapons.SelectedGroup == 2
                ? FontStyle.Bold
                : FontStyle.Normal;
        }
    }

    void UpdateWeaponTargetMarker(SpacecraftWeaponSystem weapons)
    {
        ISpaceWeaponTarget target = weapons == null ? null : weapons.CurrentTarget;
        if (weaponTargetMarker == null || worldCamera == null ||
            weapons == null || !weapons.HasTargetLock || target == null)
        {
            if (weaponTargetMarker != null)
                weaponTargetMarker.gameObject.SetActive(false);
            return;
        }

        Vector3 viewport = worldCamera.WorldToViewportPoint(target.AimPosition);
        if (viewport.z <= 0f || viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f)
        {
            weaponTargetMarker.gameObject.SetActive(false);
            return;
        }
        weaponTargetMarker.gameObject.SetActive(true);
        if (weaponTargetGraphic != null)
            weaponTargetGraphic.color = TargetColor(target.TargetKind);
        float pulse = 1f + Mathf.Sin(Time.unscaledTime * 5f) * 0.06f;
        weaponTargetMarker.localScale = Vector3.one * pulse;
        SetViewportAnchor(weaponTargetMarker, viewport, 0.03f);
    }

    void ConfigureWeaponTargetMarker()
    {
        if (weaponTargetMarker == null)
            return;
        weaponTargetMarker.sizeDelta = new Vector2(104f, 104f);
        weaponTargetGraphic = weaponTargetMarker.GetComponent<SpaceTargetLockGraphic>();
    }

    void UpdateIntegrity()
    {
        if (integrityText != null)
            integrityText.text = "船体";
        float ratio = damageReceiver == null
            ? 1f
            : Mathf.Clamp01(damageReceiver.Integrity / damageReceiver.MaximumIntegrity);
        if (unifiedLayout != null && unifiedLayout.HullValueText != null)
            unifiedLayout.HullValueText.text = $"{ratio * 100f:0}%";
        Color hullColor = ratio < 0.3f
            ? HudDanger
            : ratio < 0.6f
                ? HudAmber
                : HudCyan;
        if (damageFlashUntil > Time.unscaledTime
            && Mathf.FloorToInt(Time.unscaledTime * 18f) % 2 == 0)
        {
            hullColor = Color.white;
        }
        unifiedLayout?.HullBar?.SetValue(ratio, hullColor);
        if (integrityFill != null)
        {
            integrityFill.fillAmount = ratio;
            integrityFill.color = ratio < 0.3f
                ? new Color(1f, 0.16f, 0.08f, 0.92f)
                : ratio < 0.6f
                    ? new Color(1f, 0.55f, 0.08f, 0.9f)
                    : new Color(0.08f, 0.9f, 1f, 0.86f);
        }
        if (integrityFrame != null)
        {
            float remaining = damageFlashUntil - Time.unscaledTime;
            float pulse = remaining > 0f ? 0.5f + Mathf.Sin(Time.unscaledTime * 42f) * 0.5f : 0f;
            integrityFrame.color = Color.Lerp(Color.white, new Color(1f, 0.2f, 0.08f, 1f), pulse * 0.75f);
        }
    }

    static string LocalizedMountLabel(string value)
    {
        switch (value)
        {
            case "GIMBAL":
                return "云台挂载";
            case "MIXED":
                return "混合挂载";
            case "FIXED":
                return "固定挂载";
            default:
                return "挂载 --";
        }
    }

    void UpdateCruiseText()
    {
        if (cruiseText != null)
            cruiseText.text = WarpStatusText();
        if (promptText != null)
            promptText.text = ship != null && (ship.CruiseActive || ship.SurfaceEntryActive)
                ? "跃迁演出进行中"
                : navigation != null && navigation.IsNearLockedPlanet
                    ? "对准星球按 B 进入地表"
                    : "对准星球按 B 开启近星跃迁  |  N 切换目标";
    }

    string WarpStatusText()
    {
        if (ship != null && ship.WarpCancelReason == InterstellarWarpCancelReason.NoReticleTarget)
            return "请将准星对准星球";
        if (ship != null)
        {
            switch (ship.WarpState)
            {
                case InterstellarWarpState.Aligning:
                    return "自动对准";
                case InterstellarWarpState.Spooling:
                    return "跃迁门充能";
                case InterstellarWarpState.Transit:
                    return "加速穿越";
                case InterstellarWarpState.Exiting:
                    return "近星抵达";
            }
            if (ship.SurfaceEntryActive)
                return "进入地表";
        }
        if (navigation != null && navigation.HasLockedTarget)
        {
            string action = navigation.IsNearLockedPlanet
                ? "B 进入地表"
                : "B 开启近星跃迁";
            return $"{action}  {navigation.TargetName}  |  距离 {FormatDistance(navigation.TargetDistance)}";
        }
        return "对准星球按 B";
    }

    void BuildPlanetLightPool()
    {
        if (planetLightLayer == null || planetLightTemplate == null)
            return;
        planetLightTemplate.gameObject.SetActive(false);
        for (int index = 0; index < MaximumPlanetLights; index++)
        {
            RectTransform marker = Instantiate(planetLightTemplate, planetLightLayer);
            marker.name = "PlanetLight_" + index.ToString("00");
            marker.gameObject.SetActive(false);
            planetLights[index] = marker;
            planetLightDots[index] = marker.GetComponent<Text>();
            Transform ring = marker.Find("PlanetSelectionRing");
            Transform label = marker.Find("PlanetLightLabel");
            planetLightRings[index] = ring == null ? null : ring.GetComponent<Text>();
            planetLightLabels[index] = label == null ? null : label.GetComponent<Text>();
        }
    }

    void UpdatePlanetLights()
    {
        int count = navigation == null
            ? 0
            : navigation.CopyTargetSnapshots(planetSnapshots);
        for (int index = 0; index < MaximumPlanetLights; index++)
        {
            RectTransform marker = planetLights[index];
            if (marker == null)
                continue;
            if (index >= count || worldCamera == null)
            {
                marker.gameObject.SetActive(false);
                continue;
            }

            InterstellarPlanetTargetSnapshot snapshot = planetSnapshots[index];
            Vector3 direction = navigation.GetDirectionToUniversePosition(snapshot.universePosition);
            float forwardDot = Vector3.Dot(worldCamera.transform.forward, direction);
            Vector3 viewport = worldCamera.WorldToViewportPoint(
                worldCamera.transform.position + direction * 10f);
            bool visible = forwardDot > 0f && viewport.z > 0f
                && viewport.x >= 0f && viewport.x <= 1f
                && viewport.y >= 0f && viewport.y <= 1f;
            if (!visible)
            {
                marker.gameObject.SetActive(false);
                continue;
            }

            marker.gameObject.SetActive(true);
            SetViewportAnchor(marker, viewport, 0.018f);
            float distanceRatio = Mathf.Clamp01((float)(snapshot.distance / 260000d));
            float size = Mathf.Lerp(14f, 8f, Mathf.Sqrt(distanceRatio));
            Text dot = planetLightDots[index];
            if (dot != null)
            {
                dot.text = snapshot.locked ? "\u25c6" : "\u25cf";
                dot.fontSize = Mathf.RoundToInt(snapshot.locked ? size + 3f : size);
                dot.color = Color.Lerp(snapshot.color, Color.white, snapshot.locked ? 0.45f : 0.2f);
            }

            float angle = Vector3.Angle(worldCamera.transform.forward, direction);
            Text ring = planetLightRings[index];
            if (ring != null)
            {
                ring.gameObject.SetActive(snapshot.locked || angle <= 10f);
                ring.text = snapshot.locked ? "\u25c7" : "\u25cb";
                ring.color = snapshot.locked
                    ? new Color(0.12f, 0.88f, 1f, 1f)
                    : new Color(0.38f, 0.72f, 1f, 0.7f);
            }

            Text label = planetLightLabels[index];
            if (label != null)
            {
                label.gameObject.SetActive(snapshot.locked);
                label.text = snapshot.locked
                    ? snapshot.displayName + "  " + FormatDistance(snapshot.distance)
                    : string.Empty;
                label.color = new Color(0.65f, 0.92f, 1f, 0.96f);
            }
        }
    }

    void HandleDamaged(float currentIntegrity, float maximumIntegrity, SpaceDamageInfo damage)
    {
        damageFlashUntil = Time.unscaledTime + 0.45f;
        PirateShipAiController attacker = damage.source == null
            ? null
            : damage.source.GetComponentInParent<PirateShipAiController>();
        if (attacker != null)
            TouchThreat(attacker, Time.unscaledTime + hitThreatHoldSeconds, true);
    }

    void ResolveThreatArrows()
    {
        if (enemyThreatArrows == null || enemyThreatArrows.Length != MaximumThreatArrows)
            enemyThreatArrows = new RectTransform[MaximumThreatArrows];
        for (int index = 0; index < MaximumThreatArrows; index++)
        {
            enemyThreatArrows[index] = ResolveRect(enemyThreatArrows[index], "EnemyThreatArrow" + (index + 1));
            threatArrowTexts[index] = enemyThreatArrows[index] == null
                ? null
                : enemyThreatArrows[index].GetComponent<Text>();
            if (enemyThreatArrows[index] != null)
                enemyThreatArrows[index].gameObject.SetActive(false);
        }
    }

    void UpdateThreatArrows()
    {
        float now = Time.unscaledTime;
        int activeCount = pirateEncounterDirector == null
            ? 0
            : pirateEncounterDirector.GetActiveThreatsNonAlloc(threatQueryBuffer);
        for (int index = 0; index < activeCount; index++)
            TouchThreat(threatQueryBuffer[index], now + attackThreatHoldSeconds, false);

        for (int index = 0; index < MaximumThreatArrows; index++)
        {
            ThreatSlot slot = threatSlots[index];
            if (slot.source == null || !slot.source.gameObject.activeInHierarchy || slot.expiresAt <= now)
                threatSlots[index] = default;
        }

        SortThreatSlots(now);
        for (int index = 0; index < MaximumThreatArrows; index++)
            UpdateThreatArrow(index, now);
    }

    void TouchThreat(PirateShipAiController source, float expiresAt, bool wasHit)
    {
        if (source == null)
            return;
        int slotIndex = -1;
        int replacementIndex = 0;
        float earliestExpiry = float.PositiveInfinity;
        for (int index = 0; index < MaximumThreatArrows; index++)
        {
            if (threatSlots[index].source == source)
            {
                slotIndex = index;
                break;
            }
            if (threatSlots[index].source == null)
            {
                slotIndex = index;
                break;
            }
            if (threatSlots[index].expiresAt < earliestExpiry)
            {
                earliestExpiry = threatSlots[index].expiresAt;
                replacementIndex = index;
            }
        }
        if (slotIndex < 0)
            slotIndex = replacementIndex;
        ThreatSlot slot = threatSlots[slotIndex];
        slot.source = source;
        slot.expiresAt = Mathf.Max(slot.expiresAt, expiresAt);
        if (wasHit)
            slot.hitPulseUntil = Mathf.Max(slot.hitPulseUntil, expiresAt);
        threatSlots[slotIndex] = slot;
    }

    void SortThreatSlots(float now)
    {
        for (int left = 0; left < MaximumThreatArrows - 1; left++)
        {
            int best = left;
            float bestScore = ThreatScore(threatSlots[left], now);
            for (int right = left + 1; right < MaximumThreatArrows; right++)
            {
                float score = ThreatScore(threatSlots[right], now);
                if (score <= bestScore)
                    continue;
                best = right;
                bestScore = score;
            }
            if (best == left)
                continue;
            ThreatSlot swap = threatSlots[left];
            threatSlots[left] = threatSlots[best];
            threatSlots[best] = swap;
        }
    }

    float ThreatScore(ThreatSlot slot, float now)
    {
        if (slot.source == null || slot.expiresAt <= now)
            return float.NegativeInfinity;
        float score = slot.source.IsActivelyAttacking ? 100000f : 0f;
        if (slot.hitPulseUntil > now)
            score += 200000f;
        Vector3 origin = ship == null ? transform.position : ship.transform.position;
        score -= Vector3.Distance(origin, slot.source.AimPosition);
        return score;
    }

    void UpdateThreatArrow(int index, float now)
    {
        RectTransform arrow = enemyThreatArrows == null || index >= enemyThreatArrows.Length
            ? null
            : enemyThreatArrows[index];
        ThreatSlot slot = threatSlots[index];
        if (arrow == null || worldCamera == null || slot.source == null || slot.expiresAt <= now)
        {
            if (arrow != null)
                arrow.gameObject.SetActive(false);
            return;
        }

        Vector3 viewport = worldCamera.WorldToViewportPoint(slot.source.AimPosition);
        bool onScreen = viewport.z > 0f && viewport.x >= 0f && viewport.x <= 1f && viewport.y >= 0f && viewport.y <= 1f;
        arrow.gameObject.SetActive(true);
        if (onScreen)
        {
            viewport.y = Mathf.Min(0.94f, viewport.y + 0.055f);
            SetViewportAnchor(arrow, viewport, 0.055f);
            arrow.localRotation = Quaternion.Euler(0f, 0f, 180f);
        }
        else
        {
            Vector2 direction = new Vector2(viewport.x - 0.5f, viewport.y - 0.5f);
            if (viewport.z < 0f)
                direction = -direction;
            if (direction.sqrMagnitude < 0.0001f)
                direction = Vector2.up;
            direction.Normalize();
            float component = Mathf.Max(Mathf.Abs(direction.x), Mathf.Abs(direction.y));
            Vector2 anchor = new Vector2(0.5f, 0.5f) + direction * (0.44f / Mathf.Max(0.001f, component));
            arrow.anchorMin = anchor;
            arrow.anchorMax = anchor;
            arrow.anchoredPosition = Vector2.zero;
            arrow.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f);
        }

        bool hitPulse = slot.hitPulseUntil > now;
        float rankScale = index == 0 ? 1.18f : index == 1 ? 1f : 0.88f;
        float pulse = hitPulse ? 1f + Mathf.Sin(Time.unscaledTime * 12f) * 0.1f : 1f;
        arrow.localScale = Vector3.one * rankScale * pulse;
        Text text = threatArrowTexts[index];
        if (text != null)
        {
            float alpha = index == 0 ? 1f : index == 1 ? 0.82f : 0.66f;
            text.color = hitPulse
                ? new Color(1f, 0.17f, 0.04f, 1f)
                : new Color(1f, 0.38f, 0.08f, alpha);
        }
    }

    void UpdateArrow()
    {
        if (targetArrow == null || worldCamera == null || navigation == null || !navigation.HasLockedTarget)
        {
            if (targetArrow != null)
                targetArrow.gameObject.SetActive(false);
            return;
        }

        Vector3 direction = navigation.DirectionToTarget;
        Vector3 viewport = worldCamera.WorldToViewportPoint(worldCamera.transform.position + direction.normalized * 10f);
        bool onScreen = viewport.z > 0f && viewport.x >= 0f && viewport.x <= 1f
            && viewport.y >= 0f && viewport.y <= 1f;
        if (onScreen)
        {
            targetArrow.gameObject.SetActive(false);
            return;
        }
        targetArrow.gameObject.SetActive(true);
        Vector2 fromCenter = new Vector2(viewport.x - 0.5f, viewport.y - 0.5f);
        if (viewport.z < 0f)
        {
            viewport.x = 1f - viewport.x;
            viewport.y = 1f - viewport.y;
        }
        if (fromCenter.sqrMagnitude < 0.0001f)
            fromCenter = Vector2.up;
        SetViewportAnchor(targetArrow, viewport, 0.06f);
        targetArrow.localRotation = Quaternion.Euler(
            0f,
            0f,
            Mathf.Atan2(fromCenter.y, fromCenter.x) * Mathf.Rad2Deg - 90f);
    }

    static void SetViewportAnchor(RectTransform target, Vector3 viewport, float margin)
    {
        Vector2 clamped = new Vector2(
            Mathf.Clamp(viewport.x, margin, 1f - margin),
            Mathf.Clamp(viewport.y, margin, 1f - margin));
        target.anchorMin = clamped;
        target.anchorMax = clamped;
        target.anchoredPosition = Vector2.zero;
    }

    static Text ResolveText(Text current, string objectName)
    {
        return current != null ? current : GameObject.Find(objectName)?.GetComponent<Text>();
    }

    static RectTransform ResolveRect(RectTransform current, string objectName)
    {
        return current != null ? current : GameObject.Find(objectName)?.GetComponent<RectTransform>();
    }

    static Color TargetColor(SpaceWeaponTargetKind kind)
    {
        return kind == SpaceWeaponTargetKind.Combatant
            ? new Color(1f, 0.5f, 0.14f, 0.96f)
            : new Color(0.08f, 0.82f, 1f, 0.96f);
    }

    static string FormatDistance(double distance)
    {
        return distance >= 1000d ? $"{distance / 1000d:0.0} km" : $"{distance:0} m";
    }

}
