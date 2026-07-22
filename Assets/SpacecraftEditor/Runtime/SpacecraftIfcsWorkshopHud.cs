using UnityEngine;
using UnityEngine.UI;

namespace SpacecraftEditor
{
    [DisallowMultipleComponent]
    public sealed class SpacecraftIfcsWorkshopHud : MonoBehaviour
    {
        [SerializeField] ShipFlightController flight;
        [SerializeField] Text modeText;
        [SerializeField] Text authorityText;
        [SerializeField] Text speedLimitText;
        [SerializeField] Text boostText;
        [SerializeField] RectTransform vjoyBoundary;
        [SerializeField] RectTransform vjoyCursor;

        void Awake()
        {
            if (flight == null)
                flight = GetComponentInParent<SpacecraftApp>()?.GetComponentInChildren<ShipFlightController>(true);
            modeText = ResolveText(modeText, "WorkshopFlightModeText");
            authorityText = ResolveText(authorityText, "WorkshopAuthorityText");
            speedLimitText = ResolveText(speedLimitText, "WorkshopSpeedLimitText");
            boostText = ResolveText(boostText, "WorkshopBoostText");
            vjoyBoundary = ResolveRect(vjoyBoundary, "WorkshopVJoyBoundary");
            vjoyCursor = ResolveRect(vjoyCursor, "WorkshopVJoyCursor");
        }

        void Update()
        {
            if (flight == null)
                return;
            SpacecraftControlTelemetry telemetry = flight.Telemetry;
            if (modeText != null)
                modeText.text = telemetry.assistMode == SpacecraftAssistMode.Direct
                    ? "DIRECT  诊断模式"
                    : telemetry.assistMode == SpacecraftAssistMode.Decoupled
                        ? "DECOUPLED  惯性模式"
                        : "COUPLED  辅助模式";
            if (authorityText != null)
                authorityText.text = $"控制权威  {telemetry.controlAuthority * 100f:0}%";
            if (speedLimitText != null)
                speedLimitText.text = $"速度限制  {telemetry.speedLimit:0} m/s";
            if (boostText != null)
                boostText.text = $"BOOST  {telemetry.boostRatio * 100f:0}%";
            if (vjoyCursor != null && flight.FlightInput != null)
            {
                float radius = vjoyBoundary == null
                    ? Mathf.Min(Screen.width, Screen.height) * 0.34f
                    : Mathf.Min(vjoyBoundary.rect.width, vjoyBoundary.rect.height) * 0.5f;
                vjoyCursor.anchoredPosition = flight.FlightInput.VJoyCursor * radius;
            }
        }

        Text ResolveText(Text current, string objectName)
        {
            if (current != null)
                return current;
            Text[] texts = GetComponentsInChildren<Text>(true);
            for (int index = 0; index < texts.Length; index++)
            {
                if (texts[index].name == objectName)
                    return texts[index];
            }
            return null;
        }

        RectTransform ResolveRect(RectTransform current, string objectName)
        {
            if (current != null)
                return current;
            RectTransform[] rects = GetComponentsInChildren<RectTransform>(true);
            for (int index = 0; index < rects.Length; index++)
            {
                if (rects[index].name == objectName)
                    return rects[index];
            }
            return null;
        }
    }
}
