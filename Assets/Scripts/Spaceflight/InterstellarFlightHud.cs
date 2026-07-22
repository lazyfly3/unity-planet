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
    [SerializeField] RectTransform[] enemyThreatArrows = new RectTransform[3];
    [SerializeField, Min(0f)] float attackThreatHoldSeconds = 2.5f;
    [SerializeField, Min(0f)] float hitThreatHoldSeconds = 4f;
    [SerializeField] Image integrityFill;
    [SerializeField] Image integrityFrame;

    const int MaximumThreatArrows = 3;
    float damageFlashUntil;
    SpaceTargetLockGraphic weaponTargetGraphic;
    readonly PirateShipAiController[] threatQueryBuffer = new PirateShipAiController[MaximumThreatArrows];
    readonly ThreatSlot[] threatSlots = new ThreatSlot[MaximumThreatArrows];
    readonly Text[] threatArrowTexts = new Text[MaximumThreatArrows];

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
        ResolveThreatArrows();
        if (integrityFill == null)
            integrityFill = GameObject.Find("IntegrityFill")?.GetComponent<Image>();
        if (integrityFrame == null)
            integrityFrame = GameObject.Find("IntegrityFrame")?.GetComponent<Image>();
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
            targetText.text = navigation != null && navigation.HasTarget
                ? $"目标  {navigation.TargetName}\n距离  {FormatDistance(navigation.TargetDistance)}"
                : "目标  未发现星球";
        if (speedText != null)
            speedText.text = $"速度  {(ship == null ? 0f : ship.Speed):0} m/s";
        if (flightModeText != null)
            flightModeText.text = telemetry.assistMode == SpacecraftAssistMode.Decoupled
                ? "DECOUPLED  惯性模式"
                : "COUPLED  辅助模式";
        if (authorityText != null)
            authorityText.text = $"控制权威  {telemetry.controlAuthority * 100f:0}%";
        if (speedLimitText != null)
            speedLimitText.text = $"速度限制  {telemetry.speedLimit:0} m/s";
        if (boostText != null)
            boostText.text = $"BOOST  {telemetry.boostRatio * 100f:0}%";
        UpdateIntegrity();
        UpdateWeapons();
        UpdateCruiseText();
        UpdateArrow();
        UpdateThreatArrows();
    }

    void UpdateWeapons()
    {
        SpacecraftWeaponSystem weapons = ship == null ? null : ship.WeaponSystem;
        if (weaponGroupText != null)
            weaponGroupText.text = weapons == null ? "武器组 --" : $"武器组 {weapons.SelectedGroup}";
        if (weaponAmmoText != null)
            weaponAmmoText.text = weapons == null ? "弹药 --" : $"弹药 {weapons.SelectedAmmunition}";
        if (weaponCapacitorText != null)
            weaponCapacitorText.text = weapons == null ? "电容 --" : $"电容 {weapons.CapacitorRatio * 100f:0}%";
        if (weaponHeatText != null)
            weaponHeatText.text = weapons == null ? "热量 --" : $"热量 {weapons.SelectedHeat * 100f:0}%";
        if (weaponMountText != null)
            weaponMountText.text = weapons == null ? "挂载 --" : $"挂载 {weapons.SelectedMountLabel}";
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
        }
        UpdateWeaponTargetMarker(weapons);
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
            integrityText.text = $"船体完整度  {(damageReceiver == null ? 100f : damageReceiver.Integrity):0}%";
        float ratio = damageReceiver == null
            ? 1f
            : Mathf.Clamp01(damageReceiver.Integrity / damageReceiver.MaximumIntegrity);
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

    void UpdateCruiseText()
    {
        if (cruiseText != null)
        {
            cruiseText.text = ship != null && ship.CruiseActive
                ? $"星际巡航  {CruiseLabel(ship.CruiseState)}"
                : "B  启动星际巡航";
        }
        if (promptText != null)
        {
            promptText.text = navigation != null && navigation.CanEnterSelected
                ? "正在进入星球引力范围"
                : "1/2 武器组 | 左键开火 | Tab 云台锁定 | N 星球目标 | C 模式 | X 刹车 | Q/E 横滚";
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
        if (targetArrow == null || worldCamera == null || navigation == null || !navigation.HasTarget)
        {
            if (targetArrow != null)
                targetArrow.gameObject.SetActive(false);
            return;
        }

        targetArrow.gameObject.SetActive(true);
        Vector3 direction = navigation.DirectionToTarget;
        Vector3 viewport = worldCamera.WorldToViewportPoint(worldCamera.transform.position + direction.normalized * 10f);
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

    static string CruiseLabel(InterstellarCruiseState state)
    {
        switch (state)
        {
            case InterstellarCruiseState.Spooling: return "预热";
            case InterstellarCruiseState.Accelerating: return "加速";
            case InterstellarCruiseState.Cruising: return "恒速";
            case InterstellarCruiseState.Decelerating: return "自动制动";
            case InterstellarCruiseState.Cooldown: return "冷却";
            default: return "关闭";
        }
    }
}
